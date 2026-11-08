using AlfMcp.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlfMcp.Tools;

// MCP Apps: widget gösterir. Kural fonksiyonlarla aynı: name verilirse kayıtlıdır. html + name = göster ve <name>.html olarak kaydet, yalnız name = kayıtlıyı aç, name yok = anlık (eval gibi).
// Araç meta verisi (ui.resourceUri) Program.cs'te eklenir; sınıf [McpServerToolType] taşımaz.
public sealed class WidgetTool(Store store) {

    private const string Extension = ".html";

    [Description("Shows a small widget inline in the chat. Use it when a visual result (table, chart, stats, form) is clearer than text. `name` makes it stored, like a function name (flat, parts joined with -, e.g. `math-quiz`): `html` alone shows a one-off widget; `html` + `name` shows and stores it (the same name replaces); `name` alone opens the stored widget without rewriting its html; no arguments lists the stored names; `action` delete (with `name`) removes the stored widget file after the user confirms (never fake a delete by overwriting with empty html). `html` is a body fragment (it may contain `<script>`) styled with Tailwind v4 classes; the background is transparent and the page follows the light/dark theme by itself. Use the theme colors `text-fg`, `text-muted`, `border-line`, `bg-surface`, `text-accent` (they adapt to light and dark, never write `dark:` pairs or `bg-white`/`text-black`); other Tailwind colors only for accents (`text-emerald-500`). Layout: a card is `rounded-2xl border border-line bg-surface p-4`; use `grid grid-cols-3 gap-3` for equal columns and `flex items-center justify-between` for rows, keep widths `w-full` so everything aligns. Behavior goes in a `<script>` inside html; it runs after the html with jQuery `$`, a global `args` and `Ui.call(scriptName, args)`, which runs a stored script and returns a Promise of its result, and `Ui.confirm(message)`, a Promise of true/false (never use confirm()/alert()/prompt(): they are blocked in the iframe). Keep it short and put data in `args`; do not paste long data into html.")]
    public CallToolResult Run(
        [Description("HTML fragment (Tailwind classes); omit it to open the stored widget `name`")] string? html = null,
        [Description("data object the page script reads as `args`")] JsonElement? args = null,
        [Description("stored widget name; with html it stores, without html it opens")] string? name = null,
        [Description("delete (needs name): removes the stored widget file; omit to show, store, open or list")] string? action = null,
        McpServer? server = null) {
        try {
            if(action is not null) {
                return Delete(action, name, server);
            }
            if(name is null && html is null) {
                return Text("Stored widgets: " + string.Join(", ", store.Names(Extension)));
            }
            var saved = false;
            if(html is null) {
                html = Load(name!);
            }
            else if(name is not null) {
                if(string.IsNullOrWhiteSpace(html)) {
                    throw new InvalidOperationException("Empty html is not stored. To remove a widget use action=delete with its name.");
                }
                File.WriteAllText(store.PathOf(name, Extension), html);
                saved = true;
            }
            var data = new JsonObject {
                ["html"] = html,
                ["args"] = args.HasValue ? JsonNode.Parse(args.Value.GetRawText()) : new JsonObject()
            };
            return new CallToolResult {
                Content = [new TextContentBlock { Text = saved ? $"Widget shown and stored as '{name}'." : "Widget shown." }],
                StructuredContent = JsonSerializer.SerializeToElement(data)
            };
        }
        catch(Exception exception) when(exception is InvalidOperationException or IOException) {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = exception.Message }] };
        }
    }

    private CallToolResult Delete(string action, string? name, McpServer? server) {
        if(action != "delete") {
            throw new InvalidOperationException("Unknown action. Use delete, or omit action to show, store, open or list.");
        }
        var path = store.PathOf(name ?? throw new InvalidOperationException("delete needs the widget name."), Extension);
        if(!File.Exists(path)) {
            throw new InvalidOperationException($"Widget '{name}' was not found. Call widget with no arguments to list stored widgets.");
        }
        Confirm.Require(server, $"Delete widget '{name}'?");
        File.Delete(path);
        return Text($"Widget '{name}' deleted.");
    }

    private string Load(string name) {
        var path = store.PathOf(name, Extension);
        if(!File.Exists(path)) {
            throw new InvalidOperationException($"Widget '{name}' was not found. Call widget with no arguments to list stored widgets.");
        }
        return File.ReadAllText(path);
    }

    private static CallToolResult Text(string text) {
        return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
    }

}
