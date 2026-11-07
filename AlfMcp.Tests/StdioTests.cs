using AlfMcp.Core;
using AlfMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using System.Text.Json.Nodes;

namespace AlfMcp.Tests;

// Her test kendi geçici data klasöründe çalışır.
public sealed class Fixture : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "alfmcp-test-" + Guid.NewGuid().ToString("N"));
    public ServiceProvider Services { get; }
    public Store Store => Services.GetRequiredService<Store>();

    public Fixture()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Store(home));
        services.AddSingleton(new HttpClient());
        services.AddSingleton<Jsonl>();
        services.AddSingleton<Functions>();
        services.AddSingleton<Script>();
        foreach (var type in typeof(AlfTool).Assembly.GetTypes().Where(type => type.Namespace == "AlfMcp.Tools" && type.Name.EndsWith("Tool"))) {
            services.AddSingleton(type);
        }

        Services = services.BuildServiceProvider();
    }

    public T Get<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    // Koleksiyon açıp var olup olmadığına bakmak için küçük yardımcılar.
    public void CreateTable(string name) {
        Get<Jsonl>().Append(name, [new JsonObject { ["a"] = 1 }]);
    }

    public bool HasTable(string name) {
        return Get<Jsonl>().Exists(name);
    }

    public void Dispose()
    {
        Services.Dispose();
        if (Directory.Exists(home)) {
            Directory.Delete(home, true);
        }
    }
}

public class StdioTests
{
    [Fact]
    public async Task Stdio_Mode_KeepsStdoutForProtocolOnly()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "AlfMcp.dll");
        var home = Path.Combine(Path.GetTempPath(), "alfmcp-stdio-" + Guid.NewGuid().ToString("N"));
        var info = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{dll}\" --stdio") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["ALFMCP_HOME"] = home;
        using var process = System.Diagnostics.Process.Start(info)!;
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        var initialize = await process.StandardOutput.ReadLineAsync();

        // Yanıtlar eşzamanlı işlenir, sıraları garanti değildir; bu yüzden ardışık gönderip id ile eşlenir.
        async Task<string> Ask(int id, string method, string parameters)
        {
            await process.StandardInput.WriteLineAsync($$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}""");
            while(true) {
                var line = await process.StandardOutput.ReadLineAsync();
                if(line!.Contains($"\"id\":{id},")) {
                    return line;
                }
            }
        }

        var firstCall = await Ask(2, "tools/call", """{"name":"script","arguments":{}}""");
        var secondCall = await Ask(3, "tools/call", """{"name":"script","arguments":{}}""");
        var prompts = await Ask(4, "prompts/list", "{}");
        process.Kill(true);

        Assert.Contains("\"id\":1", initialize);
        Assert.Contains("never with local files", initialize);
        Assert.Contains("ALF MCP GUIDE", firstCall);
        Assert.DoesNotContain("ALF MCP GUIDE", secondCall);
        Assert.Contains("\"name\":\"alf\"", prompts);
        try { Directory.Delete(home, true); } catch (IOException) { }
    }

}
