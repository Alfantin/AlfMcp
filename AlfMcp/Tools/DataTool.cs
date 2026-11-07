using AlfMcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlfMcp.Tools;

// JSON Lines veritabanı: koleksiyon = namespace klasöründeki bir .jsonl dosyası.
[McpServerToolType]
public sealed class DataTool(Jsonl jsonl) {

    [McpServerTool(Name = "data", Title = "Data (JSONL)", Destructive = true, OpenWorld = false)]
    [Description("JSON Lines database: a collection is a .jsonl file named flat with parts joined by `-` (`invoices-payments` is in the namespace `invoices`) (one object per line) and every record gets an automatic numeric `_id`. Actions: collections | create | drop | copy | rename (collection -> target) | insert (data = object or array) | list (where, limit, offset; returns total) | count (where) | update (where, data merged) | delete (where). `where` is an equality filter such as {\"_id\":3} or {\"city\":\"Izmir\"}; update and delete require a non-empty where.")]
    public string Run(
        [Description("collections | create | drop | copy | rename | insert | list | count | update | delete")] string action,
        [Description("collection name, e.g. invoices-payments (1-5 parts joined with -, letters digits or _)")] string? collection = null,
        [Description("copy/rename: new name")] string? target = null,
        [Description("insert: object or array of objects; update: fields to merge")] JsonElement? data = null,
        [Description("equality filter object, e.g. {\"_id\":3}")] JsonElement? where = null,
        [Description("list: max records (default 100, max 1000)")] int? limit = null,
        [Description("list: records to skip (default 0), for paging")] int? offset = null,
        [Description("collections: only this namespace, e.g. invoices")] string? prefix = null,
        McpServer? server = null) {
        return Json.Do(() => action switch {
            "collections" => jsonl.Names(prefix).Select(name => new { name, count = jsonl.Count(name) }).ToList(),
            "create" => Create(Name(collection)),
            "drop" => Drop(Name(collection), server),
            "copy" => Copy(Name(collection), Name(target)),
            "rename" => Rename(Name(collection), Name(target)),
            "insert" => Insert(Name(collection), data),
            "list" => List(Name(collection), where, limit, offset),
            "count" => new { count = Filter(Name(collection), where).Count },
            "update" => Update(Name(collection), where, data),
            "delete" => Delete(Name(collection), where, server),
            _ => throw new InvalidOperationException("Unknown action. Use collections, create, drop, copy, rename, insert, list, count, update or delete.")
        });
    }

    private object Create(string name) {
        jsonl.Create(name);
        return new { created = name };
    }

    private object Drop(string name, McpServer? server) {
        jsonl.Require(name);
        Confirm.Require(server, $"Drop collection '{name}' with {jsonl.Count(name)} records?");
        jsonl.Drop(name);
        return new { dropped = name };
    }

    private object Copy(string name, string target) {
        jsonl.Copy(name, target);
        return new { copied = target };
    }

    private object Rename(string name, string target) {
        jsonl.Rename(name, target);
        return new { renamed = target };
    }

    private object Insert(string name, JsonElement? data) {
        var element = data ?? throw new InvalidOperationException("data is required.");
        var items = element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : [element];
        if(items.Count == 0 || items.Any(item => item.ValueKind != JsonValueKind.Object)) {
            throw new InvalidOperationException("data must be an object or a non-empty array of objects.");
        }
        var ids = jsonl.Append(name, items.Select(item => JsonNode.Parse(item.GetRawText())!.AsObject()));
        return new { inserted = ids.Count, ids };
    }

    private object List(string name, JsonElement? where, int? limit, int? offset) {
        var rows = Filter(name, where);
        var skipped = Math.Max(offset ?? 0, 0);
        return new { total = rows.Count, offset = skipped, rows = rows.Skip(skipped).Take(Math.Clamp(limit ?? 100, 1, 1000)).ToList() };
    }

    private object Update(string name, JsonElement? where, JsonElement? data) {
        RequireFilter(where, "update");
        var changes = data is { ValueKind: JsonValueKind.Object } fields ? fields : throw new InvalidOperationException("data must be an object.");
        jsonl.Require(name);
        var rows = jsonl.Read(name);
        var matched = rows.Where(row => Matches(row, where)).ToList();
        foreach(var row in matched) {
            foreach(var property in changes.EnumerateObject().Where(property => property.Name != "_id")) {
                row[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }
        jsonl.Write(name, rows);
        return new { updated = matched.Count };
    }

    private object Delete(string name, JsonElement? where, McpServer? server) {
        RequireFilter(where, "delete");
        jsonl.Require(name);
        var rows = jsonl.Read(name);
        var kept = rows.Where(row => !Matches(row, where)).ToList();
        Confirm.Require(server, $"Delete {rows.Count - kept.Count} records from '{name}'?");
        jsonl.Write(name, kept);
        return new { deleted = rows.Count - kept.Count };
    }

    private List<JsonObject> Filter(string name, JsonElement? where) {
        jsonl.Require(name);
        return jsonl.Read(name).Where(row => Matches(row, where)).ToList();
    }

    private static bool Matches(JsonObject row, JsonElement? where) {
        if(where is not { ValueKind: JsonValueKind.Object } filter) {
            return true;
        }
        return filter.EnumerateObject().All(property => JsonNode.DeepEquals(row[property.Name], JsonNode.Parse(property.Value.GetRawText())));
    }

    private static void RequireFilter(JsonElement? where, string action) {
        if(where is not { ValueKind: JsonValueKind.Object } filter || !filter.EnumerateObject().Any()) {
            throw new InvalidOperationException($"{action} needs a non-empty where (use {{\"_id\":N}} for one record; action=drop removes a whole collection).");
        }
    }

    private static string Name(string? collection) {
        return Json.Required(collection);
    }

}
