using ModelContextProtocol;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlfMcp.Core;

public static class Json {

    private static readonly JsonSerializerOptions options = new() {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Araç gövdelerini sarar; hata mesajı istemciye olduğu gibi iletilir.
    public static string Do(Func<object?> action) {
        try {
            var value = action();
            return value is string text ? text : JsonSerializer.Serialize(value, options);
        }
        catch(McpException) {
            throw;
        }
        catch(Exception exception) {
            throw new McpException(exception.Message, exception);
        }
    }

    public static string Required(string? value) {
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException("A required argument is missing.") : value;
    }

}
