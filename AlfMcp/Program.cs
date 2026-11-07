using AlfMcp.Core;
using AlfMcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var store = new Store();

// Elle (konsolda) çalıştırılınca menü açılır; MCP istemcisi başlatınca stdin yönlendirilmiş olur ve stdio MCP sunulur.
if (!args.Contains("--stdio") && !Console.IsInputRedirected) {
    Menu.Run(store, new ClaudeDesktop(), new Codex());
    return;
}

// Stdout yalnızca MCP protokolüne ayrılır; günlükler stderr'e gider.
var host = Host.CreateApplicationBuilder(args);
host.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
host.Services.AddSingleton(store);
host.Services.AddSingleton<Jsonl>();
host.Services.AddSingleton<Functions>();
host.Services.AddSingleton<Script>();
host.Services.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 AlfMcp" } } });
foreach (var toolType in typeof(Program).Assembly.GetTypes().Where(type => type.Name.EndsWith("Tool") && type.Namespace == "AlfMcp.Tools")) {
    host.Services.AddSingleton(toolType);
}

var touched = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
static string GetPath(CallToolRequestParams? call) {
    foreach(var key in new[] { "name", "collection" }) {
        if(call?.Arguments?.TryGetValue(key, out var value) == true && value.ValueKind == System.Text.Json.JsonValueKind.String) {
            return value.GetString() ?? "";
        }
    }
    return "";
}

host.Services.AddMcpServer(options => {
    options.ServerInstructions = Guide.Instructions;
    options.ServerInfo = new Implementation { Name = "Alf MCP", Title = "Alf MCP", Version = "1.0.0" };
})
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly()
    .WithListResourcesHandler((context, token) => {
        var functions = context.Services!.GetRequiredService<Functions>();
        var jsonl = context.Services!.GetRequiredService<Jsonl>();
        var resources = functions.List().Select(function => new Resource { Uri = $"alf://script/{function["name"]}", Name = (string)function["name"]!, Description = function["description"]?.ToString(), MimeType = "application/javascript" })
            .Concat(jsonl.Names().Select(name => new Resource { Uri = $"alf://data/{name}", Name = name, MimeType = "application/x-ndjson" }))
            .ToList();
        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    })
    // Oturumdaki ilk Alf MCP aracı çağrısına kılavuzu bir kez ekler (alf zaten kendisi verir); kullanıcının "alf mcp" yazması gerekmez.
    // Her çağrının süresi stderr'e (istemcinin MCP günlüğüne) yazılır: yavaşlık sunucuda mı, model/istemci tarafında mı görülebilsin.
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, token) => {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = await next(context, token);
        context.Services?.GetService<ILoggerFactory>()?.CreateLogger("AlfMcp").LogInformation("tool {Tool} took {Milliseconds} ms", context.Params?.Name, timer.ElapsedMilliseconds);
        // Bir namespace'e ilk dokunuşta o namespace'in fonksiyonları bir kez sonuca eklenir; model dizinden seçer, sunucu yüklemeyi otomatik yapar.
        var space = Store.NamespaceOf(GetPath(context.Params));
        if(space.Length > 0 && touched.TryAdd(space, true) && context.Services!.GetRequiredService<Functions>().Summary(space) is { Length: > 0 } summary) {
            result.Content.Add(new TextContentBlock { Text = $"NAMESPACE {space}:{Environment.NewLine}{summary}" });
        }
        if(context.Params?.Name != "alf" && Guide.TryDeliver()) {
            result.Content.Add(new TextContentBlock { Text = "ALF MCP GUIDE (read once and follow it for the rest of this chat):\n" + Guide.Full });
        }
        return result;
    }))
    .WithTools([McpServerTool.Create(
        typeof(WidgetTool).GetMethod(nameof(WidgetTool.Run))!,
        context => context.Services!.GetRequiredService<WidgetTool>(),
        new McpServerToolCreateOptions {
            Name = "widget",
            Title = "Widget",
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            Meta = new JsonObject {
                ["ui"] = new JsonObject { ["resourceUri"] = WidgetResource.Uri },
                ["ui/resourceUri"] = WidgetResource.Uri
            }
        })]);
await host.Build().RunAsync();
