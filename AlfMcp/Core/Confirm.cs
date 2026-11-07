using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AlfMcp.Core;

// Yıkıcı işlemlerden önce istemci üzerinden kullanıcıya onay sorar (MCP elicitation).
// İstemci elicitation desteklemiyorsa (ya da sunucu yoksa, örneğin testlerde) sorulmaz; o durumda kılavuzdaki "silmeden önce onay al" kuralı geçerlidir.
public static class Confirm {

    public static bool Ask(McpServer? server, string message) {
        if(server?.ClientCapabilities?.Elicitation is null) {
            return true;
        }
        // Araç metotları eşzamanlı olduğu için yanıt burada beklenir; protokol okuması başka iş parçacığında sürer.
        var reply = server.ElicitAsync(new ElicitRequestParams { Message = message, RequestedSchema = new ElicitRequestParams.RequestSchema() }).GetAwaiter().GetResult();
        return reply.Action == "accept";
    }

    public static void Require(McpServer? server, string message) {
        if(!Ask(server, message)) {
            throw new InvalidOperationException("The user declined, nothing was changed.");
        }
    }

}
