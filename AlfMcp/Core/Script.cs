using Jint;
using ModelContextProtocol;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AlfMcp.Core;

// JavaScript çalıştırıcı (Jint). Betik yalnızca args, http, db, fn ve log'a erişir; dosya ve işletim sistemine erişemez.
// Hem kayıtlı fonksiyonlar (func) hem anlık kod (eval) bunu kullanır.
// Bir çalıştırmanın isteğe bağlı ayarları: süre (sn), iptal belirteci ve ilerleme bildirimi (MCP progress).
public sealed record RunOptions(int? TimeoutSeconds = null, CancellationToken Token = default, IProgress<ProgressNotificationValue>? Progress = null);

public sealed partial class Script(Store store, Jsonl jsonl, Functions functions, HttpClient client) {

    static Script() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
        var contentType = options.TryGetProperty("headers", out var headerObject) ? headerObject.EnumerateObject().FirstOrDefault(header => string.Equals(header.Name, "Content-Type", StringComparison.OrdinalIgnoreCase)) : default;
        var requestType = contentType.Value.ValueKind == JsonValueKind.String ? MediaTypeHeaderValue.Parse(contentType.Value.GetString()!) : null;
        var requestEncoding = Charset(requestType?.CharSet) ?? new UTF8Encoding(false);
        if(options.TryGetProperty("form", out var form) && form.ValueKind == JsonValueKind.Object) {
            var pairs = form.EnumerateObject().Select(field => PercentEncode(field.Name, requestEncoding) + "=" + PercentEncode(field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString()! : field.Value.ToString(), requestEncoding));
            request.Content = new ByteArrayContent(Encoding.ASCII.GetBytes(string.Join("&", pairs)));
            request.Content.Headers.ContentType = requestType ?? new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        }
        else if(options.TryGetProperty("body", out var body)) {
            var isText = body.ValueKind == JsonValueKind.String;
            request.Content = new ByteArrayContent(requestEncoding.GetBytes(isText ? body.GetString()! : body.GetRawText()));
            request.Content.Headers.ContentType = requestType ?? new MediaTypeHeaderValue(isText ? "text/plain" : "application/json") { CharSet = "utf-8" };
        }

        if(options.TryGetProperty("headers", out var headers)) {
            foreach(var header in headers.EnumerateObject().Where(header => !string.Equals(header.Name, "Content-Type", StringComparison.OrdinalIgnoreCase) && !string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase))) {
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

        var status = (int)response.StatusCode;
        var responseHeaders = response.Headers.Concat(response.Content.Headers).ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(", ", header.Value));
        return JsonSerializer.Serialize(new { status, ok = status is >= 200 and < 300, url = response.RequestMessage?.RequestUri?.ToString() ?? url, headers = responseHeaders, body = DecodeBody(response.Content.Headers.ContentType, buffer, length) });
    }

    // Yanıt gövdesini doğru karakter kümesiyle çözer: önce başlıktaki charset, sonra html/xml içindeki meta charset, sonra katı UTF-8, olmazsa windows-1254 (eski Türkçe siteler).
    private static string DecodeBody(MediaTypeHeaderValue? type, byte[] buffer, int length) {
        var encoding = Charset(type?.CharSet);
        if(encoding is null && type?.MediaType is { } media && (media.Contains("html") || media.Contains("xml"))) {
            var head = Encoding.ASCII.GetString(buffer, 0, Math.Min(length, 4096));
            var match = MetaCharset().Match(head);
            encoding = match.Success ? Charset(match.Groups[1].Value) : null;
        }
        if(encoding is null) {
            try {
                encoding = new UTF8Encoding(false, true);
                return encoding.GetString(buffer, 0, length).TrimStart('﻿');
            }
            catch(DecoderFallbackException) {
                encoding = Charset("windows-1254")!;
            }
        }
        return encoding.GetString(buffer, 0, length).TrimStart('﻿');
    }

    private static Encoding? Charset(string? name) {
        if(string.IsNullOrWhiteSpace(name)) {
            return null;
        }
        try {
            return Encoding.GetEncoding(name.Trim().Trim('"', '\''));
        }
        catch(ArgumentException) {
            return null;
        }
    }

    // Form alanı kodlaması: baytlar verilen karakter kümesine göre yüzde-kodlanır (eski siteler windows-1254 ister).
    private static string PercentEncode(string text, Encoding encoding) {
        var result = new StringBuilder();
        foreach(var value in encoding.GetBytes(text)) {
            if(value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~') {
                result.Append((char)value);
            }
            else if(value == (byte)' ') {
                result.Append('+');
            }
            else {
                result.Append('%').Append(value.ToString("X2"));
            }
        }
        return result.ToString();
    }

    [GeneratedRegex("charset\\s*=\\s*[\"']?([\\w-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharset();

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
