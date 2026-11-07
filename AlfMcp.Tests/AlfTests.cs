using AlfMcp.Core;
using AlfMcp.Tools;

namespace AlfMcp.Tests;

public class AlfTests : IDisposable {

    private readonly Fixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void Alf_ReturnsGuide_AndTheNamespaceIndex() {
        fixture.Get<ScriptTool>().Run(code: "return 1", name: "invoices-rate", description: "USD/TRY rate. args: {base}");
        fixture.Get<WidgetTool>().Run("<p></p>", name: "invoices-view");
        fixture.Get<DataTool>().Run("insert", "invoices-payments", data: System.Text.Json.JsonDocument.Parse("""{"x":1}""").RootElement);

        var reply = fixture.Get<AlfTool>().Alf();

        Assert.Contains("ALF MCP GUIDE", reply);
        Assert.Contains("Reuse before creating", reply);
        Assert.Contains("K&R braces", reply);
        Assert.Contains("\"name\":\"invoices\",\"funcs\":1,\"collections\":1,\"widgets\":1", reply);
    }

    [Fact]
    public void Guide_IsASingleSource_AndInstructionsStayShort() {
        Assert.True(Guide.Instructions.Length < 900, "instructions are sent on every connection");
        Assert.Contains("never with local files, Python or shell", Guide.Instructions);
        Assert.Contains("TWO short sentences", Guide.Full);
        Assert.Contains("screens = widget", Guide.Instructions);
        Assert.Contains("text-fg", Guide.Full);
        Assert.Contains("never write `dark:` pairs", Guide.Full);
        Assert.Contains("a `name` makes it stored", Guide.Instructions);
        Assert.Contains("ONE call", Guide.Full);
        Assert.Contains("fix it with `code` + the same `name`", Guide.Full);
        Assert.Contains("no usage explanation", Guide.Instructions);
        Assert.Equal(Guide.Full, new AlfPrompt().Start());
    }

    [Fact]
    public void Tools_DeclareAnnotations_SoClientsCanTellSafeFromRiskyCalls() {
        var alf = (ModelContextProtocol.Server.McpServerToolAttribute)typeof(AlfTool).GetMethod(nameof(AlfTool.Alf))!.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)[0];
        var jsonl = (ModelContextProtocol.Server.McpServerToolAttribute)typeof(DataTool).GetMethod(nameof(DataTool.Run))!.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)[0];

        Assert.True(alf.ReadOnly);
        Assert.False(alf.OpenWorld);
        Assert.True(jsonl.Destructive);
        Assert.False(jsonl.OpenWorld);
    }

    [Fact]
    public void SandboxApi_IsDescribedOnlyInTheScriptTool() {
        var script = typeof(ScriptTool).GetMethod(nameof(ScriptTool.Run))!.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);

        Assert.Contains("db.read", ((System.ComponentModel.DescriptionAttribute)script[0]).Description);
        Assert.DoesNotContain("db.read", Guide.Full);
    }

}
