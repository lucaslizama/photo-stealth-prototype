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

## The stealth model

Detection is **gradual**, not a binary "seen" flag. Each guard integrates a per-frame
visibility value into its own 0..1 meter:

```
visibility = angleFactor × distanceFactor × player.Exposure     (0 if out of cone/range/LOS)
exposure   = stanceFactor × motionFactor × lightFactor
```

`Exposure` is deliberately allowed **above 1.0** — sprinting through a lit room should be worse
than the baseline, not merely capped at it. All the tuning knobs are `[Export]`ed, so they can
be tweaked live in the Inspector.

Guards cycle `Patrol → Investigate → Alert / Search → Patrol`. One non-obvious detail:
the meter falls below the suspicion threshold within a fraction of a second of losing sight, so
exiting `Investigate` on that alone made the state last a *single frame*. `InvestigateTimeout`
plus an "arrived at last known position" check is what makes the guard actually walk over and
check the spot.

**`LightZone` is a gameplay signal, not real light sampling.** It is an `Area3D` that overrides
how lit the player counts as being, deliberately decoupled from the renderer — so a designer can
make a bright-looking corner mechanically dark, and detection stays deterministic.

## Verifying gameplay without clicking around

Headless scripts driving the real scene are far faster than playtesting for logic checks:

```bash
godot-mono --headless --path . --script /tmp/some_probe.gd   # SceneTree script
```

Instantiate `res://scenes/Main.tscn`, add it to `root`, and read `Detection` / `State` /
`Exposure` over a few seconds. This caught the single-frame `Investigate` bug.

**`screenshot-camera` (MCP) does not render the real view** — it returned a pixel-identical
image after the player was rotated 56°. Don't judge framing or lighting from it. The editor
viewport screenshot (`screenshot-viewport`) is accurate; for the running game, look at the
window.

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
