using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlfMcp.Core;

// Tüm içerik tek düz klasördür (Home, exe yanındaki "workspace"): <ad>.js = fonksiyon, <ad>.jsonl = koleksiyon, <ad>.html = kayıtlı arayüz (widget).
// Namespace, adın "-" ile ayrılmış öneklerinden oluşur ("fatura-ab-kur" -> namespace "fatura-ab"); alt klasör yoktur. Sistem ayarları settings.json içinde durur.
public sealed partial class Store {

    public string Home { get; }
    private string SettingsPath => Path.Combine(Home, "settings.json");

    public Store(string? home = null) {
        Home = home ?? Environment.GetEnvironmentVariable("ALFMCP_HOME") ?? Path.Combine(AppContext.BaseDirectory, "workspace");
        Directory.CreateDirectory(Home);
    }

    public string? GetSetting(string key) {
        return ReadSettings().GetValueOrDefault(key);
    }

    public void SetSetting(string key, string value) {
        var settings = ReadSettings();
        settings[key] = value;
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string ValidName(string? name) {
        if(string.IsNullOrWhiteSpace(name) || !NamePattern().IsMatch(name)) {
            throw new InvalidOperationException("name is 1-5 parts joined with '-', each with letters, digits or '_' (max 50), e.g. 'invoices-rate'; the leading parts are the namespace.");
        }
        return name;
    }

    public static string NamespaceOf(string name) {
        var dash = name.LastIndexOf('-');
        return dash < 0 ? "" : name[..dash];
    }

    public string PathOf(string name, string extension) {
        return Path.Combine(Home, ValidName(name) + extension);
    }

    // Verilen uzantıdaki tüm adlar; prefix verilirse yalnızca o namespace'in (alt namespace'ler dahil) içindekiler.
    public List<string> Names(string extension, string? prefix = null) {
        var start = string.IsNullOrWhiteSpace(prefix) ? "" : ValidName(prefix.Trim('-')) + "-";
        return Directory.GetFiles(Home, "*" + extension, SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(extension))
            .Select(path => Path.GetFileName(path)[..^extension.Length])
            .Where(name => NamePattern().IsMatch(name) && name.StartsWith(start))
            .Order()
            .ToList();
    }

    // Önek olarak kullanılan her namespace ve içindeki fonksiyon ile koleksiyon sayısı: namespace dizini.
    public List<object> Namespaces() {
        var funcs = Names(".js");
        var collections = Names(".jsonl");
        var widgets = Names(".html");
        return funcs.Concat(collections).Concat(widgets)
            .Select(NamespaceOf)
            .Distinct()
            .Order()
            .Select(space => (object)new { name = space.Length == 0 ? "(root)" : space, funcs = funcs.Count(name => NamespaceOf(name) == space), collections = collections.Count(name => NamespaceOf(name) == space), widgets = widgets.Count(name => NamespaceOf(name) == space) })
            .ToList();
    }

    private Dictionary<string, string> ReadSettings() {
        if(!File.Exists(SettingsPath)) {
            return [];
        }
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? [];
    }

    [GeneratedRegex(@"^[\p{L}\p{N}_]{1,50}(-[\p{L}\p{N}_]{1,50}){0,4}$")]
    private static partial Regex NamePattern();

}
