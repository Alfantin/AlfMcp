using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlfMcp.Core;

// Claude Desktop yapılandırmasına "Alf MCP" girdisini ekler/kaldırır ve uygulamayı yeniden başlatır.
// Hiçbir yol veya paket kimliği sabit değildir; hepsi o makinedeki kurulumdan bulunur (Store veya klasik kurulum).
public sealed class ClaudeDesktop(string? configPath = null)
{
    // Claude bu anahtarı sunucunun görünen adı olarak gösterir; eski sürümlerin "alfmcp" anahtarı Setup/Unsetup ile temizlenir.
    public const string ServerName = "Alf MCP";
    private const string LegacyName = "alfmcp";

    public string ConfigPath { get; } = configPath ?? FindConfigPath();

    // Girdi yalnızca şu an çalışan AlfMcp'yi gösteriyorsa "kurulu" sayılır; başka bir yolu gösteriyorsa Setup onu günceller.
    public bool IsInstalled()
    {
        return File.Exists(ConfigPath) && JsonNode.DeepEquals(Load()["mcpServers"]?[ServerName], Entry());
    }

    public void Install()
    {
        var root = Load();
        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        root["mcpServers"] = servers;
        servers.Remove(LegacyName);
        servers[ServerName] = Entry();
        Save(root);
    }

    public bool Remove()
    {
        if (!File.Exists(ConfigPath)) {
            return false;
        }

        var root = Load();
        if (root["mcpServers"] is not JsonObject servers || !(servers.Remove(ServerName) | servers.Remove(LegacyName))) {
            return false;
        }

        Save(root);
        return true;
    }

    // Açıksa kapatıp yeniden başlatır, kapalıysa başlatır.
    public string Restart()
    {
        var running = Process.GetProcessesByName("claude").Where(IsDesktopApp).ToList();
        var path = running.Select(ProgramPath).FirstOrDefault(file => file is not null);
        foreach (var process in running) {
            process.Kill(true);
        }

        foreach (var process in running) {
            process.WaitForExit(10000);
        }

        Start(path);
        return running.Count > 0 ? "Claude Desktop restarted." : "Claude Desktop started.";
    }

    // Girdi, AlfMcp'nin şu an çalıştığı exe'yi gösterir; klasör taşınırsa Setup yeniden çalıştırılır.
    public static JsonObject Entry()
    {
        var program = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the AlfMcp path.");
        var arguments = new JsonArray();
        if (Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) {
            arguments.Add(Environment.GetCommandLineArgs()[0]);
        }

        arguments.Add("--stdio");
        return new JsonObject { ["command"] = program, ["args"] = arguments };
    }

    private static string? ProgramPath(Process process)
    {
        try {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) {
            return null;
        }
    }

    // Claude Code CLI de "claude" adını taşıdığı için yalnızca masaüstü uygulaması seçilir.
    private static bool IsDesktopApp(Process process)
    {
        var file = ProgramPath(process) ?? "";
        return file.Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) || file.Contains("AnthropicClaude", StringComparison.OrdinalIgnoreCase);
    }

    private static void Start(string? runningPath)
    {
        var family = StoreFamily(runningPath);
        if (family is not null) {
            Process.Start("explorer.exe", $@"shell:AppsFolder\{family}!Claude");
            return;
        }

        var classic = runningPath ?? Path.Combine(LocalAppData(), "AnthropicClaude", "claude.exe");
        if (!File.Exists(classic)) {
            throw new InvalidOperationException("Claude Desktop is not installed on this machine.");
        }

        Process.Start(new ProcessStartInfo(classic) { UseShellExecute = true });
    }

    // Store paket ailesi adı: çalışan exe yolundan ("Claude_1.2_x64__abc" -> "Claude_abc") ya da Packages klasöründen bulunur.
    private static string? StoreFamily(string? runningPath)
    {
        var folder = runningPath?.Split('\\').FirstOrDefault(part => part.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase) && part.Contains("__"));
        if (folder is not null) {
            return "Claude_" + folder[(folder.LastIndexOf("__", StringComparison.Ordinal) + 2)..];
        }

        return StorePackages().Select(Path.GetFileName).FirstOrDefault();
    }

    private static string[] StorePackages()
    {
        var packages = Path.Combine(LocalAppData(), "Packages");
        return Directory.Exists(packages) ? Directory.GetDirectories(packages, "Claude_*") : [];
    }

    private static string LocalAppData()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static string FindConfigPath()
    {
        var store = StorePackages().Select(package => Path.Combine(package, "LocalCache", "Roaming", "Claude")).FirstOrDefault(Directory.Exists);
        var folder = store ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
        return Path.Combine(folder, "claude_desktop_config.json");
    }

    private JsonObject Load()
    {
        if (!File.Exists(ConfigPath)) {
            return new JsonObject();
        }

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonNode.Parse(File.ReadAllText(ConfigPath), documentOptions: options) as JsonObject ?? new JsonObject();
    }

    private void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var backup = ConfigPath + ".alfmcp.bak";
        if (File.Exists(ConfigPath) && !File.Exists(backup)) {
            File.Copy(ConfigPath, backup);
        }

        File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
