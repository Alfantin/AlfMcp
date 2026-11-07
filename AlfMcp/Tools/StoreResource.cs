using AlfMcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AlfMcp.Tools;

// Fonksiyonlar ve koleksiyonlar kaynak (resource) olarak da açılır: kullanıcı istemcinin kaynak menüsünden sohbete ekleyebilir.
[McpServerResourceType]
public sealed class StoreResource(Functions functions, Jsonl jsonl, Store store) {

    private const int MaxRecords = 500;

    [McpServerResource(UriTemplate = "alf://script/{name}", Name = "script", Title = "Script", MimeType = "application/javascript")]
    [Description("Source code of a stored script, e.g. alf://script/invoices-rate")]
    public string Function(string name) {
        var function = functions.Require(Store.ValidName(name));
        return $"// description: {function["description"]}\n{function["code"]}";
    }

    [McpServerResource(UriTemplate = "alf://data/{name}", Name = "collection", Title = "Collection", MimeType = "application/x-ndjson")]
    [Description("First 500 records of a collection as JSON Lines, e.g. alf://data/invoices-payments")]
    public string Collection(string name) {
        jsonl.Require(Store.ValidName(name));
        return string.Join('\n', File.ReadLines(store.PathOf(name, ".jsonl")).Take(MaxRecords));
    }

}
