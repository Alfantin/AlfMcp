using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlfMcp.Core;

// Koleksiyonlar namespace klasörlerinde birer .jsonl dosyasıdır (ad bir yoldur: "fatura/odemeler"): satır başına bir JSON nesnesi.
// Append ile eklenen her kayda otomatik artan bir _id verilir.
public sealed class Jsonl(Store store) {

    private static readonly JsonSerializerOptions LineOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public List<string> Names(string? prefix = null) {
        return store.Names(".jsonl", prefix);
    }

    public bool Exists(string collection) {
        return File.Exists(PathOf(collection));
    }

    public void Require(string collection) {
        if(!Exists(collection)) {
            throw new InvalidOperationException($"Collection '{collection}' does not exist. Create it with action=create or just insert into it.");
        }
    }

    public List<JsonObject> Read(string collection) {
        var path = PathOf(collection);
        if(!File.Exists(path)) {
            return [];
        }
        return File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
    }

    public void Write(string collection, List<JsonObject> rows) {
        var path = PathOf(collection);
        var temp = path + ".tmp";
        File.WriteAllLines(temp, rows.Select(row => row.ToJsonString(LineOptions)));
        File.Move(temp, path, true);
    }

    public List<long> Append(string collection, IEnumerable<JsonObject> items) {
        var rows = Read(collection);
        var nextId = NextId(rows);
        var ids = new List<long>();
        foreach(var item in items) {
            rows.Add(WithId(item, nextId));
            ids.Add(nextId++);
        }
        Write(collection, rows);
        return ids;
    }

    // Koleksiyonun tüm içeriğini verilen kayıtlarla değiştirir; _id'si olmayan kayıtlara yeni _id verilir.
    public int Replace(string collection, IEnumerable<JsonObject> items) {
        var rows = items.ToList();
        var nextId = NextId(rows);
        var records = rows.Select(row => row["_id"] is null ? WithId(row, nextId++) : row).ToList();
        Write(collection, records);
        return records.Count;
    }

    public int Count(string collection) {
        return Read(collection).Count;
    }

    public void Create(string collection) {
        if(Exists(collection)) {
            throw new InvalidOperationException($"Collection '{collection}' already exists.");
        }
        Write(collection, []);
    }

    public void Drop(string collection) {
        Require(collection);
        File.Delete(PathOf(collection));
    }

    public void Copy(string collection, string target) {
        Require(collection);
        if(Exists(target)) {
            throw new InvalidOperationException($"Collection '{target}' already exists.");
        }
        File.Copy(PathOf(collection), PathOf(target));
    }

    public void Rename(string collection, string target) {
        Require(collection);
        if(Exists(target)) {
            throw new InvalidOperationException($"Collection '{target}' already exists.");
        }
        File.Move(PathOf(collection), PathOf(target));
    }

    private static long NextId(List<JsonObject> rows) {
        return rows.Select(row => row["_id"]?.GetValue<long>() ?? 0).DefaultIfEmpty(0).Max() + 1;
    }

    private static JsonObject WithId(JsonObject item, long id) {
        var record = new JsonObject { ["_id"] = id };
        foreach(var property in item.Where(property => property.Key != "_id")) {
            record[property.Key] = property.Value?.DeepClone();
        }
        return record;
    }

    private string PathOf(string collection) {
        return store.PathOf(collection, ".jsonl");
    }

}
