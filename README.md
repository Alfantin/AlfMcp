# AlfMcp

A minimal MCP server over stdio. Talk to an AI and it builds collections, widgets and its own scripts for you.
One console executable, plain-file storage, no UI or HTTP port. The MCP client (Claude Desktop, Codex) starts the process.

## What it does

| Tool | Purpose |
| --- | --- |
| `alf` | Entry tool: the assistant guide and an index of your namespaces |
| `data` | JSON Lines database: create, list, insert, update and delete records in collections |
| `script` | JavaScript scripts in a sandbox (Jint): run once, or store under a name and call later. Scripts can read and write collections and call `http()` |
| `widget` | Small HTML widget shown inline in the chat (MCP Apps); store it under a name to reopen it |

Everything is saved as plain files in one flat folder, `workspace/` next to the exe (override with `ALFMCP_HOME`): `<name>.js`, `<name>.jsonl`, `<name>.html`. A name is 1-5 parts joined with `-`; everything before the last `-` is its namespace (`invoices-rate.js` is in `invoices`).

## Build

```powershell
dotnet publish AlfMcp -c Release -r win-x64 -p:PublishSingleFile=true -o AlfMcp.Release
```

Or double-click `build.bat`. Use the published `AlfMcp.exe`, not `bin\Debug`.

## Use

1. Run `AlfMcp.Release\AlfMcp.exe` by double-clicking it. A menu opens (arrow keys + Enter).
2. Choose **Setup Claude** (Claude Desktop) or **Setup Codex** (needs the Codex CLI on `PATH`), then **Restart** the client.
3. Ask the AI for what you need, for example "keep a list of my invoices" or "show a chart of them".

If you move the exe, run Setup again (the entry stores its path). Do not run several clients against the same `workspace/` at once.

## Tests

```powershell
dotnet test
```
