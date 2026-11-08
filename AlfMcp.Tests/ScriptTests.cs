using AlfMcp.Core;
using AlfMcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text.Json;

namespace AlfMcp.Tests;

public class ScriptTests : IDisposable {

    private readonly Fixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private sealed class StubHandler : HttpMessageHandler {

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"rates":{"TRY":43.5}}""") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return Task.FromResult(Send(request, cancellationToken));
        }

    }

    private sealed class Collector : IProgress<ProgressNotificationValue> {

        public List<ProgressNotificationValue> Values { get; } = [];

        public void Report(ProgressNotificationValue value) {
            Values.Add(value);
        }

    }

    private static JsonElement Args(string json) {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private string Save(string name, string code, string? description = null) {
        return fixture.Get<ScriptTool>().Run(code: code, name: name, description: description);
    }

    // Çalışırken hata veren betikler name ile kaydedilemez (önce çalıştırılır); testte dosya doğrudan yazılır.
    private void Write(string name, string code) {
        File.WriteAllText(Path.Combine(fixture.Store.Home, name + ".js"), code);
    }

    //============================================
    // Tek kural: name verilirse kayıtlıdır
    //============================================

    [Fact]
    public void CodeAlone_RunsOnce_WithArgs_AndStoresNothing() {
        var reply = fixture.Get<ScriptTool>().Run("log('hi'); return args.x * 2", args: Args("""{"x":21}"""));

        Assert.Contains("\"result\":42", reply);
        Assert.Contains("hi", reply);
        Assert.Equal("[]", fixture.Get<ScriptTool>().Run());
    }

    [Fact]
    public void CodeWithName_RunsAndStores_NameAloneCalls_NoArgumentsLists() {
        var tool = fixture.Get<ScriptTool>();

        var stored = tool.Run(code: "return args.x + 1", name: "plus-one", args: Args("""{"x":1}"""), description: "adds one to args.x");
        var called = tool.Run(name: "plus-one", args: Args("""{"x":9}"""));
        var listed = tool.Run();

        Assert.Contains("\"result\":2", stored);
        Assert.Contains("\"result\":10", called);
        Assert.Contains("adds one to args.x", listed);
        Assert.DoesNotContain("return", listed);
        Assert.True(File.Exists(Path.Combine(fixture.Store.Home, "plus-one.js")));
    }

    [Fact]
    public void SameName_Replaces_AndKeepsTheDescriptionWhenNoneIsGiven() {
        var tool = fixture.Get<ScriptTool>();
        Save("rate", "return 1", "Returns one. args: none");

        Save("rate", "return 2");

        Assert.Contains("\"result\":2", tool.Run(name: "rate"));
        Assert.Contains("Returns one. args: none", tool.Run());
        Assert.StartsWith("// description: Returns one. args: none", File.ReadAllText(Path.Combine(fixture.Store.Home, "rate.js")));
    }

    [Fact]
    public void FailedRun_IsNotStored() {
        Assert.Throws<McpException>(() => Save("broken", "throw new Error('boom')"));

        Assert.Equal("[]", fixture.Get<ScriptTool>().Run());
    }

    [Fact]
    public void GetAndDelete_AreActions() {
        var tool = fixture.Get<ScriptTool>();
        Save("double", "return args.x * 2", "Doubles args.x");

        var got = tool.Run(name: "double", action: "get");
        var deleted = tool.Run(name: "double", action: "delete");

        Assert.Contains("return args.x * 2", got);
        Assert.Contains("Doubles args.x", got);
        Assert.Contains("\"deleted\":1", deleted);
        Assert.Equal("[]", tool.Run());
    }

    [Fact]
    public void UnknownNames_BadNames_AndBadActions_AreRejected() {
        var tool = fixture.Get<ScriptTool>();

        Assert.Throws<McpException>(() => tool.Run(name: "nope"));
        Assert.Throws<McpException>(() => tool.Run(name: "nope", action: "get"));
        Assert.Throws<McpException>(() => tool.Run(name: "nope", action: "delete"));
        Assert.Throws<McpException>(() => Save("bad name!", "return 1"));
        Assert.Throws<McpException>(() => Save("../evil", "return 1"));
        Assert.Throws<McpException>(() => tool.Run(name: "a", action: "explode"));
    }

    //============================================
    // Namespace ve zincir
    //============================================

    [Fact]
    public void Namespaces_ArePrefixes_AndListCanFilterByPrefix() {
        var tool = fixture.Get<ScriptTool>();
        Save("invoices-rate", "return 1", "rate of the day");
        Save("invoices-eu-vat", "return 2");
        Save("hr-leave", "return 3");

        var invoices = tool.Run(prefix: "invoices");

        Assert.Contains("invoices-rate", invoices);
        Assert.Contains("invoices-eu-vat", invoices);
        Assert.DoesNotContain("hr-leave", invoices);
        Assert.True(File.Exists(Path.Combine(fixture.Store.Home, "invoices-eu-vat.js")));
        Assert.Contains("\"result\":3", tool.Run(name: "hr-leave"));
    }

    [Fact]
    public void Fn_UsesFullNames_AcrossNamespaces_AndRecursionIsStopped() {
        var tool = fixture.Get<ScriptTool>();
        Save("util-double", "return args.n * 2");
        Save("invoices-total", "return fn('util-double', { n: args.n })");
        Write("loop", "return fn('loop', {})");

        Assert.Contains("\"result\":14", tool.Run(name: "invoices-total", args: Args("""{"n":7}""")));
        Assert.Throws<McpException>(() => tool.Run(name: "loop"));
    }

    [Fact]
    public void FailingScript_ErrorNamesTheScript_AlsoInAChain() {
        var tool = fixture.Get<ScriptTool>();
        Write("inner", "throw new Error('boom')");
        Write("outer", "return fn('inner', {})");

        var direct = Assert.Throws<McpException>(() => tool.Run(name: "inner"));
        var chained = Assert.Throws<McpException>(() => tool.Run(name: "outer"));

        Assert.Contains("function 'inner'", direct.Message);
        Assert.Contains("boom", direct.Message);
        Assert.Contains("function 'inner'", chained.Message);
        Assert.Contains("function 'outer'", chained.Message);
    }

    [Fact]
    public void Scripts_AreJsFiles_WithTheDescriptionOnTheFirstLine() {
        Save("rate", "return 1", "Returns one. args: none");

        var text = File.ReadAllText(Path.Combine(fixture.Store.Home, "rate.js"));

        Assert.StartsWith("// description: Returns one. args: none", text);
        Assert.Contains("return 1", text);
    }

    //============================================
    // Sandbox: veri, ağ, güvenlik
    //============================================

    [Fact]
    public void Script_CanReadInsertAndReplaceCollections() {
        var tool = fixture.Get<ScriptTool>();

        var ids = tool.Run("return db.insert('people', [{name:'Ayşe', age:25}, {name:'Ali', age:30}])");
        var adults = tool.Run("return db.read('people').filter(row => row.age > 26).map(row => row.name)");
        tool.Run("return db.replace('people', db.read('people').map(row => ({...row, age: row.age + 1})))");
        var after = fixture.Get<DataTool>().Run("list", "people", where: Args("""{"_id":1}"""));

        Assert.Contains("\"result\":[1,2]", ids);
        Assert.Contains("\"result\":[\"Ali\"]", adults);
        Assert.Contains("\"age\":26", after);
        Assert.Contains("Ayşe", after);
    }

    [Fact]
    public void StoredScript_CanUseTheDatabase() {
        Save("total", "return db.read('sales').reduce((sum, row) => sum + row.amount, 0)");
        fixture.Get<DataTool>().Run("insert", "sales", data: Args("""[{"amount":10},{"amount":32}]"""));

        Assert.Contains("\"result\":42", fixture.Get<ScriptTool>().Run(name: "total"));
    }

    [Fact]
    public void Http_ReturnsStatusBodyAndParsedJson() {
        var handler = new StubHandler();
        var tool = new ScriptTool(fixture.Get<Functions>(), new Script(fixture.Store, fixture.Get<Jsonl>(), fixture.Get<Functions>(), new HttpClient(handler)));

        var reply = tool.Run("const r = http('https://api.example.test/latest?base=' + args.base, { headers: { 'X-Key': 'k' } }); log('status', r.status); return r.json.rates.TRY;", args: Args("""{"base":"USD"}"""));

        Assert.Contains("\"result\":43.5", reply);
        Assert.Contains("status 200", reply);
        Assert.Equal("https://api.example.test/latest?base=USD", handler.Requests.Single().RequestUri!.ToString());
        Assert.Equal("k", handler.Requests.Single().Headers.GetValues("X-Key").Single());
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {

        public string? RequestBody { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return respond(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return Task.FromResult(Send(request, cancellationToken));
        }

    }

    private ScriptTool ToolWith(DelegateHandler handler) {
        return new ScriptTool(fixture.Get<Functions>(), new Script(fixture.Store, fixture.Get<Jsonl>(), fixture.Get<Functions>(), new HttpClient(handler)));
    }

    private static HttpResponseMessage Bytes(byte[] bytes, string contentType) {
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public void Http_ContentTypeHeader_ReplacesTheDefault_NotAppendedToIt() {
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });

        ToolWith(handler).Run("return http('https://x.test/search', { method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: 'q=a' }).status");

        Assert.Equal("q=a", handler.RequestBody);
    }

    [Fact]
    public void Http_ContentTypeHeaderSeenByTheServer_IsExactlyTheOneTheScriptChose() {
        string? seen = null;
        var handler = new DelegateHandler(request => {
            seen = request.Content!.Headers.ContentType!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });

        ToolWith(handler).Run("return http('https://x.test/s', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=windows-1254' }, body: 'q=a' }).status");

        Assert.Equal("application/x-www-form-urlencoded; charset=windows-1254", seen);
    }

    [Fact]
    public void Http_Form_IsPercentEncoded_InTheRequestedCharset() {
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });

        ToolWith(handler).Run("return http('https://x.test/s', { method: 'POST', form: { q: 'şeker & çay' } }).status");
        var utf8 = handler.RequestBody;
        ToolWith(handler).Run("return http('https://x.test/s', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=windows-1254' }, form: { q: 'şeker & çay' } }).status");

        Assert.Equal("q=%C5%9Feker+%26+%C3%A7ay", utf8);
        Assert.Equal("q=%FEeker+%26+%E7ay", handler.RequestBody);
    }

    [Fact]
    public void Http_ResponseCharset_FromHeader_Meta_AndLegacyFallback_AreDecoded() {
        var turkish = System.Text.Encoding.GetEncoding("windows-1254");
        var viaHeader = ToolWith(new DelegateHandler(_ => Bytes(turkish.GetBytes("Çağrı Şeker"), "text/html; charset=windows-1254")));
        var viaMeta = ToolWith(new DelegateHandler(_ => Bytes(turkish.GetBytes("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1254\">Çağrı Şeker"), "text/html")));
        var viaFallback = ToolWith(new DelegateHandler(_ => Bytes(turkish.GetBytes("Çağrı Şeker"), "text/plain")));
        var utf8 = ToolWith(new DelegateHandler(_ => Bytes(System.Text.Encoding.UTF8.GetBytes("Çağrı Şeker"), "text/plain")));

        Assert.Contains("Çağrı Şeker", viaHeader.Run("return http('https://x.test/').body"));
        Assert.Contains("Çağrı Şeker", viaMeta.Run("return http('https://x.test/').body"));
        Assert.Contains("Çağrı Şeker", viaFallback.Run("return http('https://x.test/').body"));
        Assert.Contains("Çağrı Şeker", utf8.Run("return http('https://x.test/').body"));
    }

    [Fact]
    public void Http_ReturnsOkFlag_FinalUrl_AndResponseHeaders() {
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") });

        var reply = ToolWith(handler).Run("const r = http('https://x.test/a'); return [r.ok, r.status, r.url, typeof r.headers].join('|')");

        Assert.Contains("false|400|https://x.test/a|object", reply);
    }

    [Fact]
    public void Http_RejectsNonHttpSchemes() {
        Assert.Throws<McpException>(() => fixture.Get<ScriptTool>().Run("return http('file:///c:/windows/win.ini').body"));
    }

    [Fact]
    public void Script_HasNoAccessToTheHost() {
        var reply = fixture.Get<ScriptTool>().Run("return [typeof System, typeof process, typeof require, typeof importNamespace].join(',')");

        Assert.Contains("undefined,undefined,undefined,undefined", reply);
    }

    //============================================
    // Süre, ilerleme, iptal
    //============================================

    [Fact]
    public void InfiniteLoop_IsStoppedByTimeout() {
        fixture.Store.SetSetting("js.timeout", "1");

        Assert.Throws<McpException>(() => fixture.Get<ScriptTool>().Run("while (true) {}"));
    }

    [Fact]
    public void TimeoutParameter_OverridesTheSetting() {
        fixture.Store.SetSetting("js.timeout", "60");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<McpException>(() => fixture.Get<ScriptTool>().Run("while (true) {}", timeout: 1));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Progress_ReportsFromTheScript_ToTheClient() {
        var collector = new Collector();

        fixture.Get<ScriptTool>().Run("progress(1, 3, 'first'); progress(2, 3); return 1", progress: collector);

        Assert.Equal(2, collector.Values.Count);
        Assert.Equal("first", collector.Values[0].Message);
        Assert.Equal(3, collector.Values[0].Total);
        Assert.Null(collector.Values[1].Message);
    }

    [Fact]
    public void Cancellation_StopsARunningScript() {
        using var source = new CancellationTokenSource(300);

        Assert.Throws<McpException>(() => fixture.Get<ScriptTool>().Run("while (true) {}", cancellationToken: source.Token));
    }

}
