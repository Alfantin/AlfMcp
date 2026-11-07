using AlfMcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AlfMcp.Tools;

// İstemcinin istem menüsünden seçilebilir: kullanıcı "alf mcp başlat" yazmak yerine bunu seçer.
[McpServerPromptType]
public sealed class AlfPrompt {

    [McpServerPrompt(Name = "alf")]
    [Description("Start working with Alf MCP: loads the assistant guide into this chat.")]
    public string Start() {
        Guide.MarkDelivered();
        return Guide.Full;
    }

}
