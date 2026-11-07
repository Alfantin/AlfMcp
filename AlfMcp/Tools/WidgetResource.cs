using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection;

namespace AlfMcp.Tools;

// MCP Apps: widget'ın çizildiği sayfa. jQuery ve Tailwind (browser build) sayfaya gömülüdür; iframe içinde dış adres gerekmez.
// Widget'ın kendisi araç sonucuyla (structuredContent) gelir; bu sayfa yalnızca onu çizer.
[McpServerResourceType]
public sealed class WidgetResource {

    public const string Uri = "ui://alf/widget";

    [McpServerResource(UriTemplate = Uri, Name = "widget_page", MimeType = "text/html;profile=mcp-app")]
    [Description("Inline widget page (MCP Apps).")]
    public string Page() {
        return Asset("widget.html").Replace("[[jquery]]", Asset("jquery.min.js")).Replace("[[tailwind]]", Asset("tailwind.browser.js"));
    }

    private static string Asset(string file) {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("assets/" + file) ?? throw new InvalidOperationException($"Embedded asset '{file}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

}
