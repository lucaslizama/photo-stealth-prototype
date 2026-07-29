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

> **Launching `godot-mono` directly rewrites all 42 of them with the CLOUD URL.**
> Regeneration reads the connection mode from the environment, so without the
> `GODOT_MCP_CONNECTION_MODE=Custom` / `GODOT_MCP_HOST` vars that `run-editor.sh`
> exports, it falls back to the Cloud default and every `curl` example silently becomes
> `https://ai-game.dev/mcp/...` instead of `http://localhost:8080/...`. This happens even
> for a plain `--script` run with no editor. After running the project by hand, check
> `git status` and `git checkout -- .claude/skills/` if they turn up modified —
> `grep -rl ai-game.dev .claude/skills/` should return nothing.

## The two views

The game is played from a **3/4 overhead camera** and switches to **first person only
while the viewfinder is up** (hold RMB). These are two control schemes, not two framings:

| | top-down | first person |
| --- | --- | --- |
| camera | `Player/CameraRig/TopCamera`, 58° pitch, fixed yaw | `Player/Head/Camera` |
| WASD | screen-relative, built from the rig's yaw | body-relative (ordinary FPS strafe) |
| body yaw | turns toward its own movement | mouse |
| mouse look | ignored | pitch + yaw |
| speed | walk / sprint / crouch | capped at `AimSpeed` (1.2 m/s), no sprint |
| shutter | refused | the only place a photo can be taken |

`PlayerController` is the **single authority** on which view is live; the rig and
`PhotoCamera` react to its `ViewChanged` signal instead of polling `aim_camera`
themselves, so they cannot disagree about it. Detection does not care which view is
active — the stealth model reads position, stance, motion and light, never the camera.

Because the body's yaw comes from its own movement, **you cannot aim while standing
still in top-down.** That is deliberate: first person inherits whatever yaw top-down
left behind, and the mouse takes over from there.

### Camera switching is a hard cut, on purpose

`TakePhoto` grabs the live viewport. A camera interpolating between the two poses
would let the shutter fire mid-transition and hand back a frame that does not match
the framing the scorer measured. The rig owns *both* cameras' `Current` flag so
exactly one is ever live.

The player's `Body` mesh is hidden in first person — the camera sits inside the
capsule and would otherwise render the inside of the player's own head. It is
squashed on the Y axis when crouching, because the eye-height drop that sells a
crouch in first person is invisible from overhead.

### Why one wall is missing from the top-down view

A wall of height `h` hides the player whenever they stand closer to it than
`(h - FocusHeight) / tan(pitch)` — **1.19m for a 3m wall at 58°**. Camera `Distance`
cancels out of that entirely, so no amount of pulling back fixes it; only pitch does,
and clearing a 3m wall would need ~79°, i.e. abandoning the 3/4 look.

`YawDegrees` being **fixed** is what makes this cheap to solve: only ever one wall can
sit between the camera and the player. So `WallSouth` is on visual layer 2 and
`TopCamera.cull_mask` excludes it (`1048573` = all layers but bit 2).

This is a *render* layer, so:

- the wall still blocks guard line of sight (physics is untouched — verified by ray);
- it still renders, lit, through the first-person lens, so photos facing south are
  unaffected. Lights are **not** affected by `VisualInstance3D.layers`; they have their
  own `light_cull_mask`, which defaults to every layer. That is why no light needed
  editing.

Nothing is lost visually: the camera is south of the player, so the south wall only
ever presented its *exterior* face and top to it.

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

Three subjects (`photo_subject` group), each with its own pass mark. The shutter is
gated on being in first person and having film left. A shot is graded:

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

### Making a subject read as a subject

The original three subjects were two featureless pale planes and a guard capsule
identical to the other guard — you could only tell what to photograph by reading the
shot list and deducing it. Three layers now answer that, at three different moments:

1. **The props look like their names.** A wall safe with an open door, a dial and a
   fan of printed sheets; a blueprint-blue floor plan with white walls and a title
   block, pinned in a frame; and a VIP in a purple suit, top hat, gold sash and
   briefcase, so they cannot be confused with the red guard. Colour does most of the
   work here — blueprint blue and cream paper are recognisable before any detail is.
2. **A beacon and name tag float over each one in top-down** — built in code by
   `PhotoSubject`, not placed per subject, so the tag text cannot drift from
   `DisplayName` and any subject added later gets one free. They turn green with a ✓
   once a passing shot exists.
3. **The viewfinder brackets and names every subject in frame** (`ViewfinderTargets`),
   with the grade it would score right now and, when it would fail, the reason. With
   a finite roll this is what keeps the limit fair — otherwise the player spends
   frames discovering what the scorer already knew. `ShowLiveQuality` turns the
   number off for a harsher run.

Two rules hold this together:

- **Decorative prop geometry carries no collision.** Occlusion is decided by physics
  rays, so a safe housing or an open door with `use_collision` would quietly make its
  own documents unphotographable.
- **Beacons and name tags are on visual layer 3** (`PhotoSubject.MarkerVisualLayer`),
  which the first-person camera's cull mask excludes. A label reading "wall plans"
  hovering over the plans is exactly what you want while scouting and exactly what you
  never want in the photograph. Same trick as the south wall, opposite direction.

### Film is the pressure

`FilmCapacity` (8) exposures per run, and **every shutter press spends one, hit or miss.**
A wasted frame is the cost of a careless click — that is the whole point, so don't
"fix" misses into being free. At zero the shutter goes dead and the run is over
(`R` restarts); `PhotoCamera` emits `OutOfFilm` so the HUD can say so.

`MaxStoredPhotos` is gone. Photos used to be a ring buffer that evicted the oldest;
now the roll *is* the limit, so every frame taken is kept and appended in shot order
(`FrameNumber` has to agree with that order).

## The album

`Tab` opens a full-screen album (`scenes/Album.tscn`) — thumbnail strip, the selected
frame large, and the score breakdown that explains its grade. Unshot frames are drawn
as empty slots, because the budget is the mechanic and should be visible as film left
rather than only as a number.

Three non-obvious constraints:

- **It pauses the tree.** Studying a bad shot should not double as a free look at the
  patrol you are hiding from. This needs `ProcessMode.Always` to keep taking input, and
  it doubles as an input lock — everything else stops receiving keys, so the album's
  `ui_cancel` cannot collide with the player's mouse-release toggle.
- **It is NOT in `PhotoCamera.HudGroup`.** The capture frame hides every hud layer and
  then restores them *all* to visible, which would pop the album open on its own. It
  hides the hud itself on open instead (the panel does not cover the full screen, and
  leftover HUD around its edges reads as part of the album). Those two sweeps cannot
  interleave: a capture cannot start while paused, and `IsCapturing` blocks opening
  during one — without that guard, opening the album in the one frame between the
  shutter and `FramePostDraw` photographs the album.
- **Mouse mode is restored, not forced back to captured.** Getting caught frees the
  cursor on purpose; closing the album must not quietly undo that.

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
  **-X / west**, `180` faces **+Z / south**. Recovering a yaw from a forward vector is
  therefore `atan2(-x, -z)`, *not* `atan2(x, z)` — and use `LerpAngle`, or turning from
  +179° to -179° spins the long way round.
- **`global_position` is not propagated during `_init`** — it reads `(0,0,0)`. Resolve positions
  lazily inside `_process`. The camera rig snaps to its target on the first processed
  frame for exactly this reason, instead of swooping in from the world origin.
- **The camera rig must be `TopLevel`.** In top-down the body spins to face its own
  movement, and a camera inheriting that transform swings with every change of direction.
- **A container forced wider than its parent drags its siblings off-centre.** The album's
  film strip is `FilmCapacity` slots wide; at the fixed 128px it overflowed the panel by
  ~46px, and the visible symptom was *the large photo sitting off-centre*, not anything
  obviously wrong with the strip. Slot width is derived from the panel's own width
  (a `Panel` is not a container, so its size is unaffected by its children).
- **Don't `queue_free()` guards in a probe.** `StealthDirector` holds hard references, so
  its next `ReportDisturbance` throws on a disposed node. Park them underground with
  `process_mode = DISABLED` instead.
- **Unprojected pixels are not HUD pixels.** `stretch/mode="canvas_items"` renders 3D at
  the real window size while Controls work in the 1152x648 design space, so
  `UnprojectPosition` output cannot be used as a Control position directly. Normalise by
  the viewport size and rescale by the Control's own size — both cover the same
  letterboxed area, so that is correct at any resolution and needs no scale factor.
  (`ViewfinderTargets.ToLocalRect`.)
- **Visual layers are the tool for "visible in one camera only".** Two opposite uses now:
  layer 2 hides the south wall from the *top* camera, layer 3 hides beacons/name tags
  from the *first-person* camera. Neither touches physics, so guard line of sight and
  photo occlusion are unaffected either way.
- Test hooks on `PhotoCamera`: `ForceFov`, `SetFlash`, `TakePhoto` (returns whether the
  shutter actually fired), `RefillFilm`, `DescribeBestShot`, `DescribeAlbum`.
  On `PlayerController`: `ForceView(-1 input | 0 top-down | 1 first person)` — needed
  because a probe cannot hold a mouse button, and the input path additionally requires a
  captured mouse it does not have.

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

### Grabbing real frames without the editor

Drop `--headless` and the same probe gets a rendering device, so it can save exactly what
the player would see — the way to judge camera framing, occlusion and HUD layout:

```gdscript
func grab(name):
    await RenderingServer.frame_post_draw          # required, or the texture is stale
    root.get_texture().get_image().save_png("/tmp/%s.png" % name)
```

Two things that will waste your time:

- **Let the rig settle.** It follows with an exponential lerp, so after teleporting the
  player ~8 frames is nowhere near converged and the player renders well off-centre. That
  is not a centring bug — give it ~70 frames. (In play the lag is ~`speed / FollowSpeed`,
  about 0.67m at a sprint, which is the intended trailing feel.)
- **A windowed run is not deterministic.** `delta` tracks real frame time, so guard patrol
  positions differ between runs and a guard can wander into shot. Two identical runs
  scored `0.99` and `0.00` on the same setup for that reason alone. Park the guards
  (above) before judging anything about scoring.

For a binary "is this actually being rendered" question, comparing a sampled pixel with
the object hidden beats squinting at a screenshot — that is how the wall cull mask was
confirmed to affect only the top camera.

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
