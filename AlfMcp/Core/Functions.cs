namespace AlfMcp.Core;

// Kayıtlı fonksiyonlar namespace klasörlerinde birer .js dosyasıdır; ad bir yoldur ("fatura/kur-al").
// Açıklama dosyanın ilk satırında "// description: ..." yorumu olarak durur; geri kalanı koddur.
public sealed class Functions(Store store) {

    private const string DescriptionPrefix = "// description:";

    public List<Dictionary<string, object?>> List(string? prefix = null) {
        return store.Names(".js", prefix)
            .Select(name => Find(name)!)
            .Select(function => new Dictionary<string, object?> { ["name"] = function["name"], ["description"] = function["description"] })
            .ToList();
    }

    // Bir namespace'in fonksiyonlarını satır satır özetler (ilk dokunuşta sohbete eklenir).
    public string Summary(string space) {
        return string.Join('\n', List(space).Select(function => $"{function["name"]}: {function["description"]}"));
    }

    public Dictionary<string, object?>? Find(string name) {
        var path = PathOf(name);
        if(!File.Exists(path)) {
            return null;
        }
        var lines = File.ReadAllLines(path).ToList();
        string? description = null;
        if(lines.Count > 0 && lines[0].StartsWith(DescriptionPrefix)) {
            description = lines[0][DescriptionPrefix.Length..].Trim();
            lines.RemoveAt(0);
        }
        return new Dictionary<string, object?> {
            ["name"] = name,
            ["description"] = description,
            ["code"] = string.Join("\n", lines),
            ["updated"] = File.GetLastWriteTimeUtc(path).ToString("O")
        };
    }

    public Dictionary<string, object?> Require(string name) {
        return Find(name) ?? throw new InvalidOperationException($"Function '{name}' was not found. Use action=list to see what exists.");
    }

    public void Save(string name, string code, string? description) {
        var path = PathOf(name);
        var header = string.IsNullOrWhiteSpace(description) ? "" : $"{DescriptionPrefix} {description.ReplaceLineEndings(" ").Trim()}\n";
        File.WriteAllText(path, header + code);
    }

    public int Delete(string name) {
        var path = PathOf(name);
        if(!File.Exists(path)) {
            return 0;
        }
        File.Delete(path);
        return 1;
    }

    private string PathOf(string name) {
        return store.PathOf(name, ".js");
    }

}
