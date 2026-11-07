using AlfMcp.Tools;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace AlfMcp.Tests;

public class WidgetTests : IDisposable {

    private readonly Fixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private static string TextOf(CallToolResult reply) {
        return ((TextContentBlock)reply.Content[0]).Text;
    }

    [Fact]
    public void Widget_ReturnsHtmlAndArgs_AsStructuredContent_AndSavesNothingByDefault() {
        var reply = fixture.Get<WidgetTool>().Run("<p class=\"p-4\">hi</p><script>$('p').text(args.name);</script>", JsonDocument.Parse("""{"name":"Ayşe"}""").RootElement);
        var data = reply.StructuredContent!.Value;

        Assert.True(reply.IsError != true);
        Assert.Equal("<p class=\"p-4\">hi</p><script>$('p').text(args.name);</script>", data.GetProperty("html").GetString());
        Assert.Equal("Ayşe", data.GetProperty("args").GetProperty("name").GetString());
        Assert.DoesNotContain("<p", TextOf(reply));
        Assert.Empty(Directory.GetFiles(fixture.Store.Home, "*.html"));
    }

    [Fact]
    public void Widget_Args_AreOptional() {
        var data = fixture.Get<WidgetTool>().Run("<b>x</b>").StructuredContent!.Value;

        Assert.Equal(JsonValueKind.Object, data.GetProperty("args").ValueKind);
    }

    [Fact]
    public void Name_StoresTheWidget_AndNameAloneOpensItWithoutTheHtml() {
        var tool = fixture.Get<WidgetTool>();

        var saved = tool.Run("<p id=\"q\"></p><script>$('#q').text(args.text);</script>", name: "math-quiz");
        var opened = tool.Run(name: "math-quiz", args: JsonDocument.Parse("""{"text":"2+2"}""").RootElement);
        var data = opened.StructuredContent!.Value;

        Assert.Contains("stored as 'math-quiz'", TextOf(saved));
        Assert.True(File.Exists(Path.Combine(fixture.Store.Home, "math-quiz.html")));
        Assert.Equal("<p id=\"q\"></p><script>$('#q').text(args.text);</script>", data.GetProperty("html").GetString());
        Assert.Equal("<p id=\"q\"></p><script>$('#q').text(args.text);</script>", File.ReadAllText(Path.Combine(fixture.Store.Home, "math-quiz.html")));
        Assert.Equal("2+2", data.GetProperty("args").GetProperty("text").GetString());
    }

    [Fact]
    public void SameName_Replaces_AndNoArgumentsListsTheNames() {
        var tool = fixture.Get<WidgetTool>();
        tool.Run("<p>old</p>", name: "math-quiz");
        tool.Run("<p>new</p>", name: "math-quiz");
        tool.Run("<p>x</p>", name: "hr-form");

        var opened = tool.Run(name: "math-quiz").StructuredContent!.Value;
        var listed = TextOf(tool.Run());

        Assert.Equal("<p>new</p>", opened.GetProperty("html").GetString());
        Assert.Equal("Stored widgets: hr-form, math-quiz", listed);
    }

    [Fact]
    public void Mistakes_AreErrorResults_NotExceptions() {
        var tool = fixture.Get<WidgetTool>();
        tool.Run("<p>x</p>", name: "math-quiz");

        Assert.True(tool.Run(name: "nope").IsError);
        Assert.True(tool.Run("<p>y</p>", name: "bad/name").IsError);
        Assert.True(tool.Run("<p>y</p>", name: "../evil").IsError);
        Assert.Equal("<p>x</p>", tool.Run(name: "math-quiz").StructuredContent!.Value.GetProperty("html").GetString());
    }

    [Fact]
    public void Page_EmbedsJQueryAndTailwind_WithTransparentBackground_AndClassBasedDarkMode() {
        var page = new WidgetResource().Page();

        Assert.Contains("jQuery v3", page);
        Assert.Contains("tailwindcss", page);
        Assert.Contains("@custom-variant dark", page);
        Assert.Contains("background: transparent", page);
        Assert.Contains("prefers-color-scheme", page);
        Assert.Contains("name=\"color-scheme\" content=\"light dark\"", page);
        Assert.Contains(":root.dark { color-scheme: dark;", page);
        Assert.Contains("--color-muted: var(--muted)", page);
        Assert.DoesNotContain("[[", page.Split("<script>")[0]);
        Assert.Contains("Ui.call", page.Replace("call: function", "Ui.call"));
    }

}
