using AlfMcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AlfMcp.Tools;

// Kullanıcı "alf mcp" dediğinde ya da bir şey yaptırmak istediğinde çağrılır: kılavuzu ve namespace dizinini tek seferde verir.
[McpServerToolType]
public sealed class AlfTool(Store store) {

    [McpServerTool(Name = "alf", Title = "Alf MCP guide", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Call this FIRST, once per chat, whenever the user mentions Alf MCP or wants a tool, function, automation, query, reportor form. Returns the assistant guide and the namespace index (folders with their function and collection counts) so you can reuse what exists.")]
    public string Alf() {
        Guide.MarkDelivered();
        return Json.Do(() => new { guide = Guide.Full, namespaces = store.Namespaces() });
    }

}
