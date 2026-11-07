using AlfMcp.Core;
using System.Text.Json.Nodes;

namespace AlfMcp.Tests;

public class ClaudeDesktopTests
{
    private static string TempConfig(string? content)
    {
        var path = Path.Combine(Path.GetTempPath(), "alfmcp-claude-" + Guid.NewGuid().ToString("N"), "claude_desktop_config.json");
        if (content is not null) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return path;
    }

    [Fact]
    public void Install_AddsEntry_KeepsOthers_BacksUpOnce_AndRemoveUndoes()
    {
        var path = TempConfig("""{"mcpServers":{"other":{"command":"x"}},"keep":1}""");
        var claude = new ClaudeDesktop(path);

        claude.Install();
        claude.Install();
        var servers = JsonNode.Parse(File.ReadAllText(path))!["mcpServers"]!.AsObject();

        Assert.True(claude.IsInstalled());
        Assert.NotNull(servers["other"]);
        Assert.Equal("--stdio", servers[ClaudeDesktop.ServerName]!["args"]!.AsArray().Last()!.GetValue<string>());
        Assert.DoesNotContain("alfmcp", File.ReadAllText(path + ".alfmcp.bak"));
        Assert.True(claude.Remove());
        Assert.False(claude.IsInstalled());
        Assert.False(claude.Remove());
        Assert.Equal(1, JsonNode.Parse(File.ReadAllText(path))!["keep"]!.GetValue<int>());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Install_ReplacesTheLegacyEntry_AndRemoveClearsIt()
    {
        var path = TempConfig("""{"mcpServers":{"alfmcp":{"command":"/old/AlfMcp.exe","args":["--stdio"]}}}""");
        var claude = new ClaudeDesktop(path);

        claude.Install();
        var servers = JsonNode.Parse(File.ReadAllText(path))!["mcpServers"]!.AsObject();

        Assert.Null(servers["alfmcp"]);
        Assert.NotNull(servers["Alf MCP"]);

        File.WriteAllText(path, """{"mcpServers":{"alfmcp":{"command":"x"}}}""");
        Assert.True(claude.Remove());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void EntryPointingElsewhere_IsNotInstalled_UntilSetupRewritesIt()
    {
        var path = TempConfig("""{"mcpServers":{"Alf MCP":{"command":"/old/AlfMcp.exe","args":["--stdio"]}}}""");
        var claude = new ClaudeDesktop(path);

        Assert.False(claude.IsInstalled());
        claude.Install();

        Assert.True(claude.IsInstalled());
        Assert.DoesNotContain("/old/", File.ReadAllText(path));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Install_CreatesMissingConfig_AndRemoveWithoutFileIsSafe()
    {
        var path = TempConfig(null);

        Assert.False(new ClaudeDesktop(path).Remove());
        new ClaudeDesktop(path).Install();

        Assert.True(new ClaudeDesktop(path).IsInstalled());
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
