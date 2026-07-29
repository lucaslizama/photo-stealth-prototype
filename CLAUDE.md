# photo-stealth-prototype

Godot 4.7.1 **Mono (C#/.NET)** prototype, wired to the Godot editor through
[Godot-MCP](https://github.com/IvanMurzak/Godot-MCP) so an AI agent can drive the editor
directly (create nodes/scenes, edit C#, screenshot the viewport, read the console, press play).

## Non-obvious things you must know

### 1. Use `godot-mono`, never `godot`

This machine has **two** Godot 4.7.1 binaries on `PATH`:

| Binary | Build | Usable here? |
| --- | --- | --- |
| `/usr/bin/godot-mono` | `4.7.1.stable.mono` | ✅ yes |
| `/usr/bin/godot` | `4.7.1.stable` (GDScript-only) | ❌ cannot compile the addon |

`godot-cli` resolves "the first Godot binary on `PATH`" and only prefers the mono build *on
Windows*, so on Linux it picks the wrong one. Always pass `GODOT_BIN=/usr/bin/godot-mono`.
`run-editor.sh` does this for you and hard-fails if the binary isn't a mono build.

### 2. Local (Custom) mode on purpose — **never run `godot-cli login`**

Godot-MCP defaults to **Cloud** mode, routing editor traffic through the maintainer's hosted
`ai-game.dev` backend. This project is deliberately configured for **Custom** mode against a
local server, so no project data leaves the machine:

```
GODOT_MCP_CONNECTION_MODE=Custom
GODOT_MCP_HOST=http://localhost:8080
```

`.mcp.json` points Claude Code at `http://localhost:8080/mcp` with no token and no cloud host.
Don't "fix" it back to the cloud URL, and don't authenticate — there is no account.

### 3. The editor must be running for MCP tools to do anything

The architecture is three pieces:

```
Claude Code  ──http──>  gamedev-mcp-server (localhost:8080)  <──  Godot editor + godot_mcp addon
```

The server is just a hub. If the Godot editor isn't open with the addon loaded, the tools
connect but have no editor to act on.

## Running it

```bash
./run-mcp-server.sh     # terminal 1 — downloads (once, checksum-verified) + runs the hub
./run-editor.sh         # terminal 2 — builds, then opens the Godot editor in Custom mode
```

On a successful addon load the editor Output panel prints `[Godot-MCP] plugin loaded`.
**If that line is missing, the addon did not compile** — fix `dotnet build` first; nothing
downstream will work.

The addon dock's **Server** card can also start/stop the hub itself; `run-mcp-server.sh` is the
headless equivalent, useful before the editor is up.

## Version pinning

`run-mcp-server.sh` reads the server version straight out of the addon's `ServerVersion`
constant (`addons/godot_mcp/Runtime/Connection/GodotMcpServerView.cs`, currently `9.2.4`).
The addon and the server it talks to must match exactly — don't hardcode a version anywhere
else. When the addon is upgraded, the script picks up the new pin automatically.

Addon NuGet pins in `PhotoStealthPrototype.csproj` must match the addon's own csproj:
`com.IvanMurzak.ReflectorNet` and `com.IvanMurzak.McpPlugin`. The
`<EmbeddedResource ... LogicalName="Godot-MCP.extensions.catalog.json" />` line is **not
optional** — without it the addon's Extensions panel is silently empty.

## `.claude/skills/` is generated, not hand-written

The addon regenerates `.claude/skills/` (one `SKILL.md` per MCP tool, 42 of them) on **every**
editor load. They're committed so the docs are available without the editor running — but
never hand-edit them, your changes will be overwritten on the next launch.

## Known warnings

The addon emits 3 `CS0618` deprecation warnings on Godot 4.7 (`AddControlToDock` /
`RemoveControlFromDocks` were superseded by `AddDock` / `RemoveDock`). Harmless — the old API
still works. Upstream only tests up to 4.5, so 4.7 is slightly ahead of what the maintainer
verifies.

## Layout

```
scenes/     .tscn scene files (Main.tscn is the main scene)
scripts/    C# gameplay code
assets/     art, audio, models
addons/     godot_mcp (vendored, committed)
```
