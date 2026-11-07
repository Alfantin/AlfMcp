using AlfMcp.Core;

namespace AlfMcp.Tests;

// Gerçek Codex komutunu geçici bir CODEX_HOME ile çalıştırır; kullanıcının yapılandırmasına dokunmaz. Codex kurulu değilse atlanır.
public class CodexTests : IDisposable {

    private readonly string home = Path.Combine(Path.GetTempPath(), "alfmcp-codex-" + Guid.NewGuid().ToString("N"));
    private readonly string? previous = Environment.GetEnvironmentVariable("CODEX_HOME");

    public CodexTests() {
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("CODEX_HOME", home);
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        try { Directory.Delete(home, true); } catch(IOException) { }
    }

    private static bool CodexExists() {
        try {
            new Codex().IsInstalled();
            return true;
        }
        catch(InvalidOperationException) {
            return false;
        }
    }

    [Fact]
    public void Install_AddsTheEntry_AndRemoveUndoesIt() {
        if(!CodexExists()) {
            return;
        }
        var codex = new Codex();

        Assert.False(codex.IsInstalled());
        codex.Install();
        codex.Install();

        Assert.True(codex.IsInstalled());
        Assert.Contains("[mcp_servers.alf]", File.ReadAllText(Path.Combine(home, "config.toml")));
        Assert.True(codex.Remove());
        Assert.False(codex.IsInstalled());
    }

}
