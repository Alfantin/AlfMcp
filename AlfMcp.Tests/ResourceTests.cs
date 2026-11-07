using AlfMcp.Core;
using AlfMcp.Tools;
using ModelContextProtocol;
using System.Text.Json;

namespace AlfMcp.Tests;

public class ResourceTests : IDisposable {

    private readonly Fixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void Function_ResourceReturnsDescriptionAndCode() {
        fixture.Get<ScriptTool>().Run(code: "return 1", name: "invoices-rate", description: "rate of the day");

        var text = new StoreResource(fixture.Get<Functions>(), fixture.Get<Jsonl>(), fixture.Store).Function("invoices-rate");

        Assert.Equal("// description: rate of the day\nreturn 1", text);
    }

    [Fact]
    public void Collection_ResourceReturnsJsonLines_AndRejectsBadNames() {
        fixture.Get<DataTool>().Run("insert", "invoices-payments", data: JsonDocument.Parse("""[{"a":1},{"a":2}]""").RootElement);
        var resource = new StoreResource(fixture.Get<Functions>(), fixture.Get<Jsonl>(), fixture.Store);

        var lines = resource.Collection("invoices-payments").Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Contains("\"a\":2", lines[1]);
        Assert.Throws<InvalidOperationException>(() => resource.Collection("../evil"));
        Assert.Throws<InvalidOperationException>(() => resource.Function("nope"));
    }

    [Fact]
    public void Confirm_WithoutAServer_DoesNotBlockDestructiveActions() {
        fixture.Get<ScriptTool>().Run(code: "return 1", name: "temp");

        Assert.True(Confirm.Ask(null, "Delete?"));
        Assert.Contains("\"deleted\":1", fixture.Get<ScriptTool>().Run(name: "temp", action: "delete"));
        Assert.Throws<McpException>(() => fixture.Get<ScriptTool>().Run(name: "temp", action: "delete"));
    }

}
