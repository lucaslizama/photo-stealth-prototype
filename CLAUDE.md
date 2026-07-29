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

## The photo system

Three subjects (`photo_subject` group), each with its own pass mark. A shot is graded:

```
hard gate : subject centre behind camera or off-screen -> 0
quality   = (0.7*coverage + 0.3*centering) * visibility * lighting
```

The three factors **multiply**, so any single failure kills the shot. Coverage is scored in a
*band* — a subject filling 2% of frame is a speck, one filling 90% is cropped, both are bad
photos. Zoom needs no special-casing anywhere: the scorer reads the live projection, so a
narrower FOV raises coverage on its own.

### The flash is two things at once

It is a **scoring term** (`PhotoScoringSettings.FlashOn`, falling off with distance) *and* a
real `OmniLight3D` at `Player/Head/FlashLight`, switched on only for the capture frame.

Both halves are required. With only the scoring half, a dark-room shot reported "95%" and
handed back a black PNG — the number and the image have to agree.

Firing it calls `StealthDirector.ReportDisturbance`, which spikes nearby guards' meters scaled
by distance and **ignores line of sight** on purpose: a flash lights the whole room, so hiding
behind a crate is exactly when the player should get caught out. This is the coupling that makes
darkness a trade-off rather than pure safety — dark subjects need the flash.

### Capture never blocks gameplay

`PhotoCamera.TakePhoto()` does all game-state work synchronously, then fires the image grab as
best-effort. An earlier version awaited the grab first, and on a build with no rendering device
`FramePostDraw` never resolved — `_capturing` stuck true and **the camera silently stopped
working after one shot**. The grab now bails early when `DisplayServer.GetName() == "headless"`
and restores HUD/flash state in a `finally`.

The photo is the player's own viewport with `hud`-group CanvasLayers hidden for one frame, so it
is exactly what was framed. Do not "improve" this into an off-screen SubViewport — that is what
the MCP `screenshot-camera` tool does, and it renders the wrong thing.

## Window / display

Starts **maximized windowed**, scaling up to fill the screen without distortion:

```
window/size/mode=2                  ; 2 = Maximized (keeps the titlebar)
window/stretch/mode="canvas_items"  ; 3D renders at real window size; only the HUD scales
window/stretch/aspect="keep"        ; Godot 4's default, so it is NOT written to the file
```

`1152x648` is the UI **design space**, not the render resolution — the HUD offsets were laid out
against it and scale up from there. On a 2880x1620 screen the maximized window is 2880x1518 (the
taskbar takes the rest), which is 1.897:1, so `keep` pillarboxes to a 2698x1518 16:9 viewport
with ~91px bars each side.

**`keep` over `expand` is a deliberate design decision, not a default.** Framing *is* the scoring
in this game, so every player must get the same 16:9 field of view. `expand` would use those last
91px but would also hand wider-monitor players more of the level in frame.

> **Godot rewrites `project.godot` on every editor save**, stripping comments and any line whose
> value equals the engine default. Do not put rationale in that file — it will vanish. That is
> why the above is documented here instead.

## PC builds

```bash
./build-pc.sh          # Linux + Windows x86_64 release into build/
```

Requires the **mono** export templates in
`~/.local/share/godot/export_templates/4.7.1.stable.mono/` (only `linux_*` and
`windows_*` are installed, which is exactly PC — there is no macOS template).
`export_presets.cfg` is committed on purpose so builds are reproducible; only start
ignoring it once it holds signing credentials.

The script refuses to run while the editor is open — the export spawns its own engine
instance writing into the same `.godot/mono/temp`, and concurrent builds corrupt the output.

### The addon must not ship in the game

`godot_mcp` is an **editor** tool, but Godot compiles every `.cs` in the project into one
assembly, so a naive export shipped the addon's source plus ~25 SignalR / ASP.NET DLLs. The
`.csproj` excludes it from exports only:

```xml
<IsGodotExport Condition="'$(Configuration)' == 'ExportRelease' Or '$(Configuration)' == 'ExportDebug'">true</IsGodotExport>
```

**Godot exports with `Configuration=ExportRelease`/`ExportDebug` while editor builds use
`Debug`** — that is the discriminator. Do **not** condition on `GodotTargetPlatform`: the Sdk
sets it for ordinary builds too, so the condition matches always and the addon silently stops
compiling for the editor. The presets also carry
`exclude_filter="addons/godot_mcp/*"` to keep its promo images out of the `.pck`.

After changing any of this, verify **both** halves: the editor still prints
`[Godot-MCP] plugin loaded`, *and* the exported `data_*/` folder contains no
`Microsoft.AspNetCore.*` / `McpPlugin` / `ReflectorNet` DLLs.

## Traps worth knowing

- **Godot readies children before parents.** `PhotoCamera` is a child of the player, so
  `PlayerController.Head` is still null in its `_Ready`. Reach nodes by path
  (`GetNode<Camera3D>("Head/Camera")`), and defer group lookups of later siblings.
- **`mouse_filter = 2` on every HUD Control.** With the mouse captured it sits at screen centre,
  so a viewfinder panel with the default STOP filter silently eats the shutter click.
- **Not everything crosses into GDScript.** `PhotoScore` (plain struct), `Rid?`, and
  `IReadOnlyList<T>` are not marshallable, so a probe reading them fails at runtime with
  "Invalid access to property". Use `PhotoCamera.DescribeBestShot()` / `PhotoScore.ToDictionary()`.
- **Yaw convention** (cost me three wrong test results): `0` faces **-Z / north**, `90` faces
  **-X / west**, `180` faces **+Z / south**.
- **`global_position` is not propagated during `_init`** — it reads `(0,0,0)`. Resolve positions
  lazily inside `_process`.
- Test hooks on `PhotoCamera`: `ForceFov`, `SetFlash`, `TakePhoto`, `DescribeBestShot`.

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
