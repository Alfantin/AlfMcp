using System.Diagnostics;
using System.Text.Json.Nodes;

namespace AlfMcp.Core;

// Alf MCP'yi Codex'e Codex'in kendi komutuyla (codex mcp add/remove/get) ekler ve kaldırır; yapılandırma dosyasına doğrudan dokunmaz.
public sealed class Codex {

    public const string ServerName = "alf";

    public bool IsInstalled() {
        var (exitCode, output) = Run("mcp", "get", ServerName, "--json");
        if(exitCode != 0 || !output.TrimStart().StartsWith('{')) {
            return false;
        }
        var transport = JsonNode.Parse(output)?["transport"];
        var expected = ClaudeDesktop.Entry();
        return transport?["command"]?.GetValue<string>() == expected["command"]!.GetValue<string>()
            && JsonNode.DeepEquals(transport["args"], expected["args"]);
    }

    public void Install() {
        var entry = ClaudeDesktop.Entry();
        Remove();
        var arguments = new List<string> { "mcp", "add", ServerName, "--", entry["command"]!.GetValue<string>() };
        arguments.AddRange(entry["args"]!.AsArray().Select(argument => argument!.GetValue<string>()));
        var (exitCode, output) = Run(arguments.ToArray());
        if(exitCode != 0) {
            throw new InvalidOperationException("codex mcp add failed: " + output.Trim());
        }
    }

    public bool Remove() {
        var (exitCode, _) = Run("mcp", "remove", ServerName);
        return exitCode == 0;
    }

    private const string DesktopPackage = "OpenAI.Codex_";

    // Çalışan Codex süreçleri: masaüstü uygulaması (Store paketi) ve codex.exe (VS Code eklentisi ile terminal oturumları aynı dosyayı kullanır).
    public int RunningCount() {
        return Running().Count;
    }

    // Codex yapılandırmayı başlarken okur: çalışan süreçler kapatılır ve (çalışıyor ya da kapalı olsun) masaüstü uygulaması başlatılır.
    // VS Code eklentisi ve terminal buradan başlatılamaz: eklenti bir sonraki mesajda, terminal "codex" yazınca açılır.
    public string Stop() {
        var running = Running();
        var desktopFamily = running.Select(process => DesktopFamily(PathOf(process))).FirstOrDefault(family => family is not null) ?? InstalledFamily();
        foreach(var process in running) {
            try {
                process.Kill(true);
            }
            catch(InvalidOperationException) {
            }
        }
        foreach(var process in running) {
            process.WaitForExit(10000);
        }
        if(desktopFamily is not null) {
            Process.Start("explorer.exe", $@"shell:AppsFolder\{desktopFamily}!App");
            return (running.Count > 0 ? $"Stopped {running.Count} Codex process(es) and started" : "Codex was not running, started") + " Codex Desktop. In VS Code or a terminal, start Codex again.";
        }
        return running.Count > 0 ? $"Stopped {running.Count} Codex process(es). Codex Desktop is not installed; start Codex again (VS Code: next message, terminal: run codex)." : "Codex is not running and Codex Desktop is not installed; start it from VS Code or a terminal.";
    }

    // Çalışmıyorsa da kurulu masaüstü uygulamasını Packages klasöründen bulur (klasör adı uygulama ailesi adıdır).
    private static string? InstalledFamily() {
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        return Directory.Exists(packages) ? Directory.GetDirectories(packages, DesktopPackage + "*").Select(Path.GetFileName).FirstOrDefault() : null;
    }

    private static List<Process> Running() {
        return Process.GetProcesses().Where(process => process.ProcessName.Equals("codex", StringComparison.OrdinalIgnoreCase) || DesktopFamily(PathOf(process)) is not null).ToList();
    }

    private static string? PathOf(Process process) {
        try {
            return process.MainModule?.FileName;
        }
        catch(Exception exception) when(exception is System.ComponentModel.Win32Exception or InvalidOperationException) {
            return null;
        }
    }

    // Masaüstü uygulaması yolundaki paket klasöründen ("OpenAI.Codex_1.2_x64__abc" -> "OpenAI.Codex_abc") uygulama ailesi adı.
    private static string? DesktopFamily(string? path) {
        var folder = path?.Split('\\').FirstOrDefault(part => part.StartsWith(DesktopPackage, StringComparison.OrdinalIgnoreCase) && part.Contains("__"));
        return folder is null ? null : DesktopPackage + folder[(folder.LastIndexOf("__", StringComparison.Ordinal) + 2)..];
    }

    // Windows'ta npm "codex.cmd" kısayolu kurar; bu yüzden komut cmd.exe üzerinden çalıştırılır.
    private static (int ExitCode, string Output) Run(params string[] arguments) {
        var command = "codex " + string.Join(' ', arguments.Select(Quote));
        var info = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", $"/c \"{command}\"") : new ProcessStartInfo("sh", ["-c", command]);
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        try {
            using var process = Process.Start(info)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + error.Result);
        }
        catch(System.ComponentModel.Win32Exception) {
            throw new InvalidOperationException("Codex CLI was not found. Install it first (npm i -g @openai/codex).");
        }
    }

    private static string Quote(string argument) {
        return argument.Length > 0 && argument.All(character => char.IsLetterOrDigit(character) || "-_.:/\\=".Contains(character)) ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
    }

}
