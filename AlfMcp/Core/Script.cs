using Jint;
using ModelContextProtocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlfMcp.Core;

// JavaScript çalıştırıcı (Jint). Betik yalnızca args, http, db, fn ve log'a erişir; dosya ve işletim sistemine erişemez.
// Hem kayıtlı fonksiyonlar (func) hem anlık kod (eval) bunu kullanır.
// Bir çalıştırmanın isteğe bağlı ayarları: süre (sn), iptal belirteci ve ilerleme bildirimi (MCP progress).
public sealed record RunOptions(int? TimeoutSeconds = null, CancellationToken Token = default, IProgress<ProgressNotificationValue>? Progress = null);

public sealed class Script(Store store, Jsonl jsonl, Functions functions, HttpClient client) {

    private const int MaxDepth = 5;
    private const int MaxBodyBytes = 5_000_000;

    [ThreadStatic]
    private static int depth;

    // name verilirse kayıtlı bir fonksiyondur ve hata mesajı hangi fonksiyonun kırıldığını söyler.
    public object Run(string code, string? argsJson, string? name = null, RunOptions? options = null) {
        var logs = new List<string>();
        options ??= new RunOptions();
        var output = name is null ? Execute(code, argsJson ?? "{}", logs, options) : ExecuteNamed(name, code, argsJson ?? "{}", logs, options);
        return new { result = output is null ? null : (object)JsonDocument.Parse(output).RootElement.Clone(), logs };
    }

    private string? ExecuteNamed(string name, string code, string argsJson, List<string> logs, RunOptions options) {
        try {
            return Execute(code, argsJson, logs, options);
        }
        catch(Exception exception) when(exception is not InvalidOperationException { Message: var message } || !message.StartsWith("function '")) {
            throw new InvalidOperationException($"function '{name}' failed: {exception.Message}");
        }
    }

    // Kodu çalıştırır; dönen değerin JSON metnini verir (değer yoksa null). İç içe çağrılar aynı günlük listesini paylaşır.
    private string? Execute(string code, string argsJson, List<string> logs, RunOptions options) {
        if(depth >= MaxDepth) {
            throw new InvalidOperationException("Maximum nested function depth reached.");
        }

        var timeout = Timeout(options);
        var cancellation = options.Token;
        var engine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(timeout))
            .CancellationToken(cancellation)
            .LimitMemory(256_000_000)
            .LimitRecursion(256)
            .CatchClrExceptions());
        engine.SetValue("__http", new Func<string, string, string>((url, json) => Fetch(url, json, timeout, options.Token)));
        engine.SetValue("__db", new Func<string, string, string, string>(Database));
        engine.SetValue("__fn", new Func<string, string, string>((name, json) => {
            var stored = functions.Require(Store.ValidName(name));
            return ExecuteNamed(name, (string)stored["code"]!, json, logs, options) ?? "null";
        }));
        engine.SetValue("__log", new Action<string>(logs.Add));
        engine.SetValue("__progress", new Action<double, double, string>((done, total, message) => options.Progress?.Report(new ProgressNotificationValue { Progress = (float)done, Total = total > 0 ? (float)total : null, Message = message.Length > 0 ? message : null })));
        engine.SetValue("__args", argsJson);
        engine.Execute(Prelude);

        depth++;
        try {
            var output = engine.Evaluate($"JSON.stringify((function(args) {{\n{code}\n}})(JSON.parse(__args)))");
            return output.IsUndefined() ? null : output.AsString();
        }
        finally {
            depth--;
        }
    }

    // Süre: çağrıdaki değer, yoksa js.timeout ayarı, yoksa 30 sn; en fazla bir saat.
    private int Timeout(RunOptions options) {
        var seconds = options.TimeoutSeconds ?? (int.TryParse(store.GetSetting("js.timeout"), out var configured) ? configured : 30);
        return Math.Clamp(seconds, 1, 3600);
    }

    // Betik yalnızca http/https adreslerine istek atabilir; yanıt gövdesi 5 MB ile sınırlıdır.
    private string Fetch(string url, string optionsJson, int timeoutSeconds, CancellationToken token) {
        if(!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            throw new InvalidOperationException("http() accepts only absolute http/https URLs.");
        }

        var options = JsonDocument.Parse(optionsJson).RootElement;
        var request = new HttpRequestMessage(new HttpMethod(options.TryGetProperty("method", out var method) ? method.GetString()!.ToUpperInvariant() : "GET"), uri);
        if(options.TryGetProperty("body", out var body)) {
            var text = body.ValueKind == JsonValueKind.String ? body.GetString()! : body.GetRawText();
            request.Content = new StringContent(text, Encoding.UTF8, body.ValueKind == JsonValueKind.String ? "text/plain" : "application/json");
        }

        if(options.TryGetProperty("headers", out var headers)) {
            foreach(var header in headers.EnumerateObject()) {
                if(!request.Headers.TryAddWithoutValidation(header.Name, header.Value.ToString())) {
                    request.Content?.Headers.TryAddWithoutValidation(header.Name, header.Value.ToString());
                }
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var response = client.Send(request, linked.Token);
        using var stream = response.Content.ReadAsStream();
        var buffer = new byte[MaxBodyBytes];
        var length = 0;
        int read;
        while(length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0) {
            length += read;
        }

        return JsonSerializer.Serialize(new { status = (int)response.StatusCode, body = Encoding.UTF8.GetString(buffer, 0, length) });
    }

    // Betiğin veri erişimi: namespace klasörlerindeki koleksiyonlar.
    private string Database(string operation, string collection, string json) {
        if(operation == "collections") {
            return JsonSerializer.Serialize(jsonl.Names());
        }
        return operation switch {
            "read" => JsonSerializer.Serialize(jsonl.Read(collection)),
            "insert" => JsonSerializer.Serialize(jsonl.Append(collection, Records(json))),
            "replace" => JsonSerializer.Serialize(jsonl.Replace(collection, Records(json))),
            _ => throw new InvalidOperationException("Unknown db operation.")
        };
    }

    private static List<JsonObject> Records(string json) {
        var node = JsonNode.Parse(json);
        if(node is JsonObject single) {
            return [single];
        }
        if(node is JsonArray array && array.All(item => item is JsonObject)) {
            return array.Select(item => item!.AsObject()).ToList();
        }
        throw new InvalidOperationException("rows must be an object or an array of objects.");
    }

    private const string Prelude = """
        const http = (url, options) => {
            const reply = JSON.parse(__http(url, JSON.stringify(options ?? {})));
            try { reply.json = JSON.parse(reply.body); } catch { }
            return reply;
        };
        const db = {
            collections: () => JSON.parse(__db('collections', '', '')),
            read: (collection) => JSON.parse(__db('read', collection, '')),
            insert: (collection, rows) => JSON.parse(__db('insert', collection, JSON.stringify(rows))),
            replace: (collection, rows) => JSON.parse(__db('replace', collection, JSON.stringify(rows)))
        };
        const fn = (name, args) => JSON.parse(__fn(name, JSON.stringify(args ?? {})));
        const progress = (done, total, message) => __progress(done, total ?? 0, message ?? '');
        const log = (...values) => __log(values.map(value => typeof value === 'string' ? value : JSON.stringify(value)).join(' '));
        """;

}
