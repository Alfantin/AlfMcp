using AlfMcp.Core;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace AlfMcp.Tools;

// JavaScript betikleri: tek seferlik çalıştırma, kaydetme, adıyla çağırma, listeleme. Kural widget ile aynı: name verilirse kayıtlıdır.
[McpServerToolType]
public sealed class ScriptTool(Functions functions, Script script) {

    [McpServerTool(Name = "script", Title = "Scripts", Destructive = true, OpenWorld = true)]
    [Description("JavaScript scripts: run once, store, call by name. `code` is a function BODY that reads `args` and returns a JSON-able value. One rule: a `name` makes it stored. `code` alone runs once (nothing saved); `code` + `name` runs it and, if it succeeds, stores it (the same name replaces it; add `description`: one line on what it does and which args it expects); `name` alone calls the stored script with `args`; no arguments lists the stored scripts (optional `prefix` = namespace). `action`: get (source of `name`) or delete (asks the user to confirm). Names are flat, parts joined with `-`: `invoices-rate` is in the namespace `invoices`; fn() and db use full names. Sandbox API: args, progress(done, total, message), db.collections(), db.read(collection) -> records, db.insert(collection, rows) -> ids, db.replace(collection, rows), http(url, {method, headers, body}) -> {status, body, json}, fn(name, args) runs a stored script, log(...). No file or OS access. Returns {result, logs}.")]
    public string Run(
        [Description("JavaScript function body; omit it to call the stored script `name`")] string? code = null,
        [Description("stored script name, e.g. invoices-rate (1-5 parts joined with -, letters digits or _)")] string? name = null,
        [Description("object passed as `args`")] JsonElement? args = null,
        [Description("with code + name: one line on what it does and which args it expects")] string? description = null,
        [Description("get | delete (omit to run, store, call or list)")] string? action = null,
        [Description("list only this namespace, e.g. invoices")] string? prefix = null,
        [Description("max seconds (default 30, up to 3600) for long jobs")] int? timeout = null,
        McpServer? server = null,
        CancellationToken cancellationToken = default,
        IProgress<ProgressNotificationValue>? progress = null) {
        return Json.Do(() => action switch {
            null => Dispatch(code, name, args?.GetRawText(), description, prefix, new RunOptions(timeout, cancellationToken, progress)),
            "get" => functions.Require(Store.ValidName(name)),
            "delete" => Delete(Store.ValidName(name), server),
            _ => throw new InvalidOperationException("Unknown action. Use get or delete, or omit action to run, store, call or list.")
        });
    }

    private object Dispatch(string? code, string? name, string? argsJson, string? description, string? prefix, RunOptions options) {
        if(code is null && name is null) {
            return functions.List(prefix);
        }
        if(code is null) {
            return script.Run((string)functions.Require(Store.ValidName(name))["code"]!, argsJson, name, options);
        }
        if(name is not null) {
            Store.ValidName(name);
        }
        var output = script.Run(code, argsJson, null, options);
        if(name is not null) {
            functions.Save(name, code, description ?? functions.Find(name)?["description"]?.ToString());
        }
        return output;
    }

    private object Delete(string name, McpServer? server) {
        functions.Require(name);
        Confirm.Require(server, $"Delete script '{name}'?");
        return new { deleted = functions.Delete(name) };
    }

}
