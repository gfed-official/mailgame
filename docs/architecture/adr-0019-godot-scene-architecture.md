# ADR-0019: Godot Presentation Scene Architecture, ContentHost, ViewCatalog and Export Configuration

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
Autonomous planning run (architecture-decision skill, `--review lean`: TD-ADR skipped). The godot-specialist validated the draft, and the godot-csharp-specialist ran the spike `prototypes/spike-adr0019/`. Acceptance is pending the project owner's batch confirmation.

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). The export story still waits for the 4.7.1 mono export templates (Ordering Note).

## Summary
The Godot half of the game needs one fixed shape before any Presentation story starts. That means where the single `GameLoop` sits, how the scene tree is laid out, how content bytes and view assets reach the game, how views are pooled within the GDDs' caps, how the window scales, and how the project is exported. This ADR covers:
- **`GameLoop` as the root of the one main scene, with zero autoloads and no scene changes.** Spike OQ1: autoload order verified.
- **A plain-C# `ContentHost`** that reads `res://content` bytes. Spike S1: packing verified.
- **An editor-saved `ViewCatalog` resource** of parallel `string[]`/`PackedScene[]` exports, resolved to `DefIndex` arrays. Spike S4: uid rename survival verified.
- **Event-driven, pre-allocated view pools.**
- **Explicit stretch settings:** `canvas_items`/`keep_height`, 1920×1080 reference, minimum 1280×720.
- **An Upgrade-Project-Files policy.**
- **The export configuration deferred from ADR-0001:** a Godot-only `PerformativeMail.Game.sln` with Export* configurations, plus a committed `export_presets.cfg` whose `include_filter` packs the JSONC.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (.NET), `4.7.1.stable.mono.official.a13da4feb` |
| **Domain** | Core (scene tree, resources, export) / Rendering / UI |
| **Knowledge Risk** | **HIGH.** 4.4–4.7 are post-cutoff. The decisions rest on the spikes below and on `docs/engine-reference/godot/`. |
| **References Consulted** | `VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md`; spikes `prototypes/spike-godot-dotnet/REPORT.md` (F1–F16) and `prototypes/spike-adr0019/REPORT.md` (OQ1, S1, S4, DEF) |
| **Post-Cutoff APIs Used** | `FileAccess.GetFileAsBytes` + `GetOpenError` (bytes, because 4.6 removed `get_as_text(skip_cr)`); `Control.AccessibilityLive` typed `AccessibilityServer.AccessibilityLiveMode` (4.7); `.uid` sidecar files and `uid://` ext_resources (4.4+); 4.6 scene format (no `load_steps`, unique node ids) and Upgrade Project Files; 4.7 stretch defaults; 4.7 removal of CanvasItem line antialiasing; `AudioStreamPlayer.area_mask` default 0 (4.7); `Animation.Length` is `double` (4.7); `AnimationPlayer` `StringName` members (4.6) |
| **Verification Required** | (V1) A full template-backed export: C# `ContentHost` reads `content/*` from the PCK (S1-5 gap); `Utf8JsonReader`/`ImmutableArray`/`Int128`/managed SHA-256 golden digest run in a release export (S2); bundled runtime version, Invariant flag, `Sim.AssertsEnabled == false`, Sim `Optimize` (ADR-0001). (V2) The exporter's solution-name lookup (`<assembly_name>.sln` assumed). (V3) The aim ribbon allocates 0 managed bytes per frame through the chosen draw call. (V4) `keep_height` behaviour at < 16:9, and `Window.MinSize` on Wayland and hiDPI (the unit is window pixels). The setting names are VERIFIED (engine-specialist check). (V5) Windows build. All are owned by the first export story and the first ribbon story. |

**Spike results (2026-10-16, `prototypes/spike-adr0019/`, Linux, headless):**

| ID | Result | Status |
|---|---|---|
| OQ1-1 | An autoload's `_EnterTree` runs before the main scene. `_Ready` order is autoload, then scene children, then scene root. | VERIFIED |
| OQ1-2 | At equal priority an autoload processes first. `ProcessPriority = int.MinValue` orders correctly in both placements. | VERIFIED |
| OQ1-3 | An autoload survives `ChangeSceneToFile`. | VERIFIED |
| S4-1 | `[Export] string[]` and `PackedScene[]` on a `[GlobalClass]` C# `Resource` save, load and instantiate correctly. | VERIFIED |
| S4-2 | A catalog saved from a **runtime** (non-editor) run stores paths only, with no `uid://`. Saved from an editor context, every ext_resource carries `uid=`. | VERIFIED |
| S4-3 | After a rename plus an editor rescan, the uid-bearing catalog still resolves the scene. A path-only catalog breaks. | VERIFIED |
| S4-4 | A selected-resources export pulls in the catalog's scenes through dependency tracking. | VERIFIED |
| S1-1/2 | `--export-pack` works without templates. `*.jsonc`/`manifest.txt` are packed **only** with `include_filter`. | VERIFIED |
| S1-3 | Export needs a `.sln` next to the game csproj. Without one it logs errors and ships no assembly, yet still exits 0. | VERIFIED |
| S1-4 | A GDScript `FileAccess.get_file_as_bytes` reads both files from the PCK, with error 0. | VERIFIED |
| S1-5 | A C# read under the editor binary's `--main-pack` fails (no assembly in the pack). A real export is needed. | UNVERIFIED (V1) |
| S2 | BCL types in a release export. | UNVERIFIED (no templates; V1) |
| DEF-1 | A hand-written 4.7.1 `project.godot` gets stretch `disabled`/`keep`, not the new-project `canvas_items`/`expand`. | VERIFIED |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (game csproj, configs, glob, no trimming/AOT), ADR-0002 (`GameLoop` contract, `IPresentationRoot.Present`, `AlphaPermille`), ADR-0003 (boot composition, error handling), ADR-0004 (`EntityId` → node map), ADR-0010 (`IContentSource`, `ContentSet`, ViewCatalog contract, F6 semantics) — all Accepted. ADR-0016 (published preview states, `PresentationEventStream`, `IRenderClock`) — Accepted 2026-10-16. |
| **Enables** | ADR-0018 (HUD CanvasLayer stack sits in the `Hud` subtree; live-region Controls), ADR-0024 (`user://` stores are called from boot step 3), ADR-0029 (rig/camera nodes), every Presentation story |
| **Blocks** | godot-host-shell epic (all stories); every epic's presentation stories (panel view, PlayerView/camera, mailbox views, aim ribbon); the first export story |
| **Ordering Note** | M-1a uses `BuiltInContent` (ADR-0010), so `ContentHost` is built at M0. ViewCatalog, scene tree, pools and stretch settings are M-1a. The export story waits for 4.7.1 mono export templates to be installed (user-level, like the editor). |

## Context

### Problem Statement
ADR-0002 left "autoload vs root" for `GameLoop` open. ADR-0010 deferred `ContentHost`, the `ViewCatalog` resource shape and F6 key handling to this ADR, gated on spikes S1/S2/S4. ADR-0001 deferred export verification and the export-configuration mapping. HUD HN-18 notes that `project.godot` sets neither stretch nor minimum window. The presentation TRs (panel Control, mailbox flap, lunge, caps, ribbon, MultiMesh bundles) need one tree to live in. The Godot .NET build is now installed, so the spikes could run.

### Constraints
- One `GameLoop`, lowest `ProcessPriority`, `ProcessMode.Always`, main-thread process group. No other node calls the Sim (ADR-0002).
- Presentation never mutates the Sim. Outcome beats come only from domain events (ADR-0016 §5).
- Nothing Godot in Sim/Client.Core (ADR-0001). The Sim never reads `res://` (ADR-0010).
- No trimming or AOT in exports. No `Assembly.Location` (ADR-0001, spike F13).
- Per-frame 0 B in steady state for the grid panel and aim ribbon. Fixed presentation caps (Mail L637–639, Throwing L967–969).

### Requirements
- TR-inventory-027; TR-delivery-023, 024; TR-mail-022; TR-throwing-017, 021 (primary); TR-content-027 (implementation half), TR-content-026 (F6 key handling); HUD HN-18.

## Decision

### 1. `GameLoop` is the root of the main scene; zero autoloads; no scene changes

- `res://src/Game/Main.tscn`, whose root is `Main` (`Node`, script `GameLoop.cs`), is `application/run/main_scene`. **The project registers no autoloads.**
- Screen flow (boot, error, menus at M0+, run, results) swaps child subtrees under `Main/Screens`. `ChangeSceneToFile`/`ChangeSceneToPacked` are **never** used, so nothing needs to survive a scene change. That was OQ1-3's only advantage for an autoload.
- Why root rather than autoload:
  - Godot readies children before their parent (OQ1-1). `GameLoop._Ready` therefore runs **after** every static child view exists and can compose them explicitly in boot order.
  - There is no global lookup path. Views receive their dependencies from `GameLoop`, mirroring ADR-0003's ban on locators.
  - Tests instantiate `Main.tscn` like any scene.
  - OQ1-2: with no autoloads, the scene root at `int.MinValue` is first in process order. ADR-0002's priority rule is still set explicitly.
- HUD's notification queue (HUD L36, "autoload vs node") is a **node** under `Main/Hud`.

### 2. Scene tree

```
Main (Node · GameLoop.cs · ProcessMode=Always · ProcessPriority=int.MinValue · ProcessThreadGroup=Inherit/main)
├─ World (Node3D · WorldPresenter)            entity/terrain views; reads ClientLayer published state + event cursors
│  ├─ Terrain (TerrainView)                   built once from IWorldQuery tables (ADR-0026) at boot step 7
│  ├─ Destinations (Node3D)                   one MailboxView per lot (static, not pooled); flap = child node
│  ├─ Entities (Node3D)                       PlayerView, WorldItemView … via EntityViewRegistry + pools
│  ├─ Flights (Node3D)                        one MultiMeshInstance3D per bundle tier (4), ×N Label3D pool (8)
│  ├─ Props (Node3D)                          envelope pool (48), resident pool (8), heap props (12)
│  └─ CameraRig (Node3D) → Boom → Camera3D    P9 boom; aim profile
├─ Hud (Node · HudPresenter)                  ADR-0018 owns its CanvasLayer stack (seed order: MarkerLayer → persistent
│                                             → notifications → panels → chat/wheel → banners → pause → debug)
├─ Audio (Node · AudioPresenter)              voice pools (§5)
├─ Screens (Node)                             boot / content-error / run / results subtrees, swapped by GameLoop
└─ Debug (CanvasLayer · debug builds only)    F6 reload, counters overlay
```

- `GameLoop` owns a plain C# `PresentationRoot : IPresentationRoot` (ADR-0002). `Present(alpha)` calls `World`, `Hud` and `Audio` presenters **in that order**, once per frame after `RunFrame` and `RenderClock.Sample()` (ADR-0016 §6).
- Views animate in their own `_Process` only from data `Present` handed them, using `AlphaPermille`/`IRenderClock`, never `delta`.
- Nothing moves in `_PhysicsProcess`. `physics/common/physics_interpolation` and `application/run/low_processor_mode` stay off (ADR-0002).

### 3. Boot order (architecture.md §4, made concrete)

`GameLoop._Ready`:
1. Window/stretch guard: min size (§7).
2. Content: M-1a `BuiltInContent`; M0 `ContentHost` → `ContentLoader.Load` → `ContentSet`. On failure, show the content-error screen and never compose the Sim.
3. Persistence (ADR-0024).
4. Input router (ADR-0012). The `InputAdapter` node is the **last child of the root window** (`GetTree().Root`, after `Main`, not under it) and re-asserts that position (`MoveChild(this, -1)`) whenever a node enters the root (`ChildEnteredTree`; likely via `CallDeferred`, verified in input-controls story 028), with `ProcessMode = Always` (ADR-0012 §7 D-1). OQ1's autoload-first / `ProcessPriority` ordering does not apply to input: input callbacks run in reverse tree order and `ProcessPriority` has no effect on them.
5. Session: loopback (ADR-0011).
6. `SimComposition.Build` → `ClientLayer.Attach(ports)` (ADR-0016).
7. World tables → `Terrain`/`Destinations` built.
8. `ViewCatalogResolver.Resolve(ContentSet)` → pools pre-instantiated.
9. `TickDriver.Start`.

Any exception goes to the error screen and `RunFrame` stops (ADR-0002/0003).

### 4. `ContentHost` (M0) and F6 hot reload

- `src/Game/Content/ContentHost.cs` is a **plain C# class implementing `IContentSource`**, not a Node and not an autoload. `GameLoop` constructs it and passes it to `ContentLoader`.
- It exposes `ContentHostState { Unloaded, Loading, Valid, Failed, Reloading }` (CD state table).
- **Synchronous** load at boot: the advisory budget is ≤ 100 ms (ADR-0010). There are no threads and no `CallDeferred` marshalling (ADR-0002 bans Sim-adjacent threads). This answers C&D L792's "async load marshalled back with `CallDeferred`": not needed.
- `ListManifest()` reads `res://content/manifest.txt`. `Read(path)` returns `FileAccess.GetFileAsBytes("res://content/" + path)` and checks `FileAccess.GetOpenError()` on every read; an error becomes a loader error with the path. **Never** `GetAsText`.
- The `EmbeddedResource` fallback (ADR-0010 Risks) is kept behind the same interface, used only if V1 shows the C# PCK read fails.
- `DirAccess` enumeration of the PCK (S1's optional question) is **not used**. The manifest stays mandatory (ADR-0010).
- **F6 reload** is handled by `Debug/ContentReloadKey`. It exists only when `OS.IsDebugBuild()`. Directory resolution order:
  1. `--content-dir=<path>` from `OS.GetCmdlineUserArgs()`;
  2. env `PM_CONTENT_DIR`;
  3. in the editor (`OS.HasFeature("editor")`), `ProjectSettings.GlobalizePath("res://content")`.

  It builds a Sim `DirectorySource` and follows ADR-0010 §4 (keep the old set on error; restart the run from its seed, tainted). It is refused while any remote peer is connected.

### 5. `ViewCatalog` (TR-content-027 implementation) and view pools

```csharp
// src/Game/Views/ViewCatalog.cs
[GlobalClass] public partial class ViewCatalog : Resource
{
    [Export] public string[] DefIds { get; set; } = [];        // content def id strings (CD28), parallel to Scenes
    [Export] public PackedScene[] Scenes { get; set; } = [];   // S4-1 VERIFIED shape
    [Export] public PackedScene Placeholder { get; set; } = null!;
}
```
- One catalog `.tres` per def table that has views (items/mail kinds, destination types, props), under `res://src/Game/Views/Catalogs/`.
- Catalogs **must be created and saved in the editor**, by hand in the inspector or via an editor tool. **Never** with `ResourceSaver.Save` from game code (S4-2). The editor writes `uid=` on every ext_resource, and renames then survive (S4-3).
- A CI text check fails any `Catalogs/*.tres` ext_resource line without `uid="uid://`.
- `.uid` sidecar files are committed with their sources.
- **Resolution at boot:** `ViewCatalogResolver.Resolve(ContentSet)` maps each `DefIds[i]` through the def table's id → `DefIndex` and builds `PackedScene[] byDefIndex`. Missing or unknown entries get the placeholder plus a warning in debug, and a **content error** in release exports (CD28, checked by `!OS.IsDebugBuild()`). Array length mismatch is an error in every build.
- Catalogs are read-only at runtime. No `Duplicate` of any kind (CD28's `DEEP_DUPLICATE_ALL` ban is moot).
- **Entity views:**
  - `EntityViewRegistry` is a `Dictionary<EntityId, Node3D>` preallocated to the cap. An entry is added on the spawn event and removed on the despawn event. Every lookup is guarded with `GodotObject.IsInstanceValid` (ADR-0004 risk).
  - Iteration never orders gameplay (Presentation only).
- **Pools** are pre-instantiated at boot step 8, with fixed caps from the GDDs. Exhaustion recycles the oldest **cosmetic** instance or skips the visual, never allocates:

  | Pool | Cap | Source |
  |---|---|---|
  | envelope props | 48 island-wide | Mail L637 |
  | residents | 8 | Mail L637 |
  | heap props | 12 | Mail L618 |
  | flashes | ≤ 3 per second on screen (HUD core governor) | Mail L637 |
  | bundle ×N tags (`Label3D`) | 8, hidden beyond 35 m | Throwing L968 |
  | mail audio voices | 16 total; cha-ching 4, wrong-door 3, `sfx_feed_tick` 6, Lost Parcel chirp 3; one reserved voice for the local player's own outcome | Mail L639 |

- **Flights:** one `MultiMeshInstance3D` per bundle tier (1.0 / 1.15 / 1.35 / 1.6×). `TransformFormat` and `Mesh` are set **before** `InstanceCount`, once. `InstanceCount` is preallocated to the flight cap, `CustomAabb` is set explicitly to the map bounds (no culling surprises when visible counts change), and `VisibleInstanceCount` is updated each frame. Spin is a pure function of `flightId`. The ×N `Label3D` pool sets `Billboard`, `FixedSize` and `NoDepthTest` once and writes `Text` only when the value changes, since each change rebuilds the mesh. Positions come from ADR-0016's flight timeline.

### 6. Specific view rules
- **Grid panel (TR-inventory-027):** one custom-drawn `Control` per panel. `_Draw` runs only after `QueueRedraw()` on a mirror version change. Text and layout buffers are cached, so the steady state is 0 B per frame. Feedback goes to a dedicated live-region `Control` whose `AccessibilityLive` is set with the 4.7 type `AccessibilityServer.AccessibilityLiveMode`.
- **MailboxView (TR-delivery-023):** ≤ 150 tris. The flap is a **separate rotating child node**. States are material/vertex swaps plus a decal quad.
- **PlayerView (TR-delivery-024):**
  - `PlayerView/RigOffset` (Node3D) carries the **visual-only lunge** (≤ 0.8 m local offset). Nothing reads it back.
  - The reach upper-body layer is posed from `heldTicks / holdTicks` (seek, not a time-scaled clip).
  - `Animation.Length` is `double` in 4.7.
  - `AnimationPlayer` members are `StringName` (4.6).
- **Aim ribbon (TR-throwing-017):**
  - A screen-space dashed ribbon drawn by one `Control` on a CanvasLayer. Each dash is a 4-point **`DrawPrimitive` quad** using GodotSharp 4.7.1's `ReadOnlySpan<Vector2>` overloads over preallocated buffers (VERIFIED in `GodotSharp.xml`). `DrawPolygon`/`DrawColoredPolygon` triangulate on the CPU every call, so they are avoided. **Never `DrawLine`/`DrawPolyline`** (line AA removed in 4.7; HUD rule). Quad edges alias without 2D MSAA: the ribbon story decides `rendering/anti_aliasing/quality/msaa_2d` or a feathered texture.
  - Points come from the throw preview (ADR-0016 4c), projected with `Camera3D.UnprojectPosition` into preallocated buffers.
  - Budget: ≤ 0.3 ms per frame, hard cap 0.5 ms, 0 managed bytes per frame.
  - If V3 shows the C# draw call marshals a managed allocation, switch to `RenderingServer` canvas-item polygon calls or a preallocated `ArrayMesh` with surface updates. The decision (screen-space, quads, preallocated) stands either way.

### 7. Window, stretch and scale (HUD HN-18)

`project.godot` sets these **explicitly**. DEF-1 shows a hand-written project gets `disabled`/`keep`.

| Setting | Value | Why |
|---|---|---|
| `display/window/size/viewport_width` / `viewport_height` | 1920 / 1080 | 1080-reference px (HUD L235) |
| `display/window/stretch/mode` | `canvas_items` | UI scales crisply; 3D renders at window resolution |
| `display/window/stretch/aspect` | `keep_height` | scale = window height / 1080 exactly (HN-18 "height / 1080"); width expands |
| `display/window/size/window_width_override` / `window_height_override` | 1600 / 900 | the viewport size would otherwise also be the initial window size and overflow a 1080p desktop |
| minimum window | 1280×720, set in boot step 1 with `GetWindow().MinSize = new Vector2I(1280, 720)`. **No project setting exists** for this (VERIFIED: `ProjectSettings.get_property_list()` has no `display/window/size/min_*`). | HUD L235, Grid L535 |

The aspect hint values (`ignore,keep,keep_width,keep_height,expand`) and stretch mode values (`disabled,canvas_items,viewport`) were VERIFIED headlessly. The C# enum is `Window.ContentScaleAspectEnum.KeepHeight`.
| UI scale (accessibility, HUD H23) | multiplies `Window.ContentScaleFactor` | one mechanism |

### 8. Project-file hygiene: Upgrade Project Files policy
- When the engine pin moves (patch or minor, for example 4.7.1 → 4.7.2), run **Project > Tools > Upgrade Project Files once**, in a change that contains only that bulk re-save. Never re-save scenes piecemeal across a pin change (4.6 format: no `load_steps`, unique node ids).
- `.uid` files are always committed. `.godot/` is ignored.
- Folders that are not part of the game carry a `.gdignore` so the editor never scans or exports them:
  - top-level `tests/`, `tools/`, `prototypes/`, `docs/`, `design/`, `production/`;
  - the Godot-free libraries `src/PerformativeMail.Sim/` and `src/PerformativeMail.Client.Core/` (their `.cs` files would otherwise be scanned and packed as script resources; the game compiles them via `ProjectReference`);
  - any `bin/`, `obj/` and `TestResults/` (or `exclude_filter`).

  A `.gdignore` also beats `include_filter` (VERIFIED, `prototypes/spike-adr0019-check/`), so **`content/` must never carry one**. `*.cs` must never be excluded by filter. Spike projects still run with `--path prototypes/<spike>`, which is a separate project root.
- With two solutions and a csproj at the root, a bare `dotnet build` is ambiguous. Every script and CI step **must** name the solution or project explicitly.
- `EnableGodotDotNetPreview` (spike F14) is not used.

### 9. Export configuration (the half deferred from ADR-0001)
- **`project.godot`** `[dotnet] project/assembly_name="PerformativeMail.Game"` matches `PerformativeMail.Game.csproj`.
- **`PerformativeMail.Game.sln`** at the repo root contains `PerformativeMail.Game.csproj` and its two `ProjectReference`s. Its solution configurations are `Debug`, `ExportDebug` and `ExportRelease`, each mapped to the same-named project configuration for the game and to `Debug`/`Release`/`Release` for the libraries. ADR-0001's props map `ExportRelease` → `Optimize=true` regardless.
  - The exporter needs a solution next to the csproj (S1-3), and a solution containing the Godot project needs Export* configurations (spike F16/C10).
  - The Godot-free `PerformativeMail.sln` is unchanged and still excludes the game project (ADR-0001).
  - The name lookup is assumed to be `<assembly_name>.sln` (V2).
- **`export_presets.cfg`** is committed. It contains no secrets and no signing data; those live in the uncommitted `.godot/export_credentials.cfg`.
  - Presets: `Linux` (x86_64) and `Windows Desktop` (x86_64).
  - `export_filter="all_resources"`.
  - `include_filter="content/*.jsonc, content/manifest.txt"` (S1-2: required).
  - `exclude_filter="tests/*, tools/*, prototypes/*, docs/*, design/*, production/*"`, as a belt-and-braces addition to `.gdignore`.
  - No trimming or AOT option is enabled (ADR-0001). The first export story records every `dotnet/*` preset key it finds (`dotnet/include_scripts_content` and `dotnet/embed_build_outputs` are expected; UNVERIFIED names).
- Godot "export debug" builds `ExportDebug`; "export release" builds `ExportRelease` (spike F6).
- **First export story (V1):**
  1. Install 4.7.1 mono templates.
  2. Run `godot-mono --headless --export-release Linux <out>` (and Windows at M0).
  3. Record the bundled runtime version, the Invariant flag, `Sim.AssertsEnabled == false`, Sim `Optimize`, and `[Conditional]` removal (`INVENTORY_ASSERT`).
  4. Read `content/*` through C# `ContentHost`.
  5. Run the S2 smoke test (`Utf8JsonReader`, `ImmutableArray`, `Int128`, SHA-256 golden digest).
  6. Then set `project.yaml` `commands.build` (ADR-0001).

### Key Interfaces

```csharp
// Godot project (assembly PerformativeMail.Game)
public partial class GameLoop : Node { /* ADR-0002 body; root of Main.tscn; composes children in boot order */ }
public sealed class PresentationRoot : IPresentationRoot { public void Present(int alphaPermille); } // World → Hud → Audio
public sealed class ContentHost : IContentSource
{ public ContentHostState State { get; } public IEnumerable<string> ListManifest(); public ReadOnlyMemory<byte> Read(string path); }
public enum ContentHostState : byte { Unloaded, Loading, Valid, Failed, Reloading }
[GlobalClass] public partial class ViewCatalog : Resource { /* DefIds, Scenes, Placeholder */ }
public static class ViewCatalogResolver { public static ResolvedViews Resolve(ViewCatalog c, ContentSet set, DefTableId table, bool release); }
public sealed class EntityViewRegistry { public bool TryGet(EntityId id, out Node3D view); public void Add(EntityId id, Node3D v); public void Remove(EntityId id); }
public sealed class NodePool<T> where T : Node { public NodePool(PackedScene s, int cap, Node parent); public T? Rent(); public void Return(T n); }
```

### Implementation Guidelines
- The project **must** register no autoloads and **must never** call `ChangeSceneToFile`/`ChangeSceneToPacked`. Screens are child subtrees of `Main/Screens`.
- Child views `_Ready` before `GameLoop._Ready` (OQ1-1). They **must** be inert until `GameLoop` injects their dependencies through an explicit `Initialize(deps)`. Views **must never** look dependencies up in `_Ready`.
- `Main.tscn`'s root **must** be the only `GameLoop`. Presenters are invoked by `PresentationRoot.Present`, in the order World → Hud → Audio.
- `ContentHost` **must** read bytes with `FileAccess.GetFileAsBytes` and check `GetOpenError()` every call. It **must never** use `GetAsText` or `DirAccess` enumeration of the PCK.
- `ViewCatalog` `.tres` files **must** be saved by the editor (uid-bearing). Game code **must never** `ResourceSaver.Save` a catalog. The CI uid check **must** pass.
- Pools **must** be pre-instantiated at boot within the §5 caps. Steady-state frames **must** instantiate no nodes and allocate 0 B in the panel and ribbon.
- The ribbon and HUD **must never** use `DrawLine`/`DrawPolyline`.
- No Presentation value (rig offset, camera, animation time) **may** feed the Sim.
- `export_presets.cfg` **must** keep the `include_filter` for `content/*.jsonc` and `content/manifest.txt`. The game solution **must** carry Export* configurations.

## Alternatives Considered

### Alternative 1: `GameLoop` as an autoload
- **Description**: Register `GameLoop` in `[autoload]` and keep the main scene for views.
- **Pros**: Enters the tree first; survives scene changes (OQ1-1/3); conventional in Godot.
- **Cons**: A global entry point that invites static lookups; boot order split between the autoload and the scene's `_Ready`; it only pays off if scenes change, which this design never does.
- **Rejection Reason**: Explicit composition from the scene root gives the same ordering guarantees (OQ1-2) with no global.

### Alternative 2: `ViewCatalog` as `Godot.Collections.Dictionary<string, PackedScene>`
- **Pros**: One export field; keyed lookup in the inspector.
- **Cons**: Not spiked. A Variant-typed dictionary marshals through Variant at resolve time. Dependency tracking was verified only for the array shape.
- **Rejection Reason**: The parallel arrays are the shape S4 verified. Resolution happens once at boot, so ergonomics matter little.

### Alternative 3: Content as `EmbeddedResource` from day one
- **Pros**: No export filter; no PCK read path.
- **Cons**: Loses export-time hot reload (CD26). Rebuilds the assembly for every data change.
- **Rejection Reason**: S1-2/S1-4 show PCK packing works. `EmbeddedResource` stays the V1 fallback.

### Alternative 4: Leave stretch at defaults / use `expand`
- **Pros**: `expand` is the 4.7 new-project default.
- **Cons**: A hand-written project gets `disabled` (DEF-1). `expand` scales by min(w/1920, h/1080), not height/1080 as HN-18 requires.
- **Rejection Reason**: `keep_height` is the literal HN-18 mechanism.

## Consequences

### Positive
- All four deferred Godot decisions (OQ1, ContentHost, ViewCatalog, export) are closed, with spike evidence for everything checkable without templates.
- Views and pools are bounded by construction to the GDD caps, and the steady state is allocation-free.
- Renaming view scenes is safe (uid). JSONC ships in the PCK.

### Negative
- The catalog must be edited in the editor, never generated at runtime. A generator tool would need to be an EditorPlugin.
- Two solutions at the repo root (Godot-free and Godot) must be kept in sync by hand when a library is added.
- Screen flow without scene changes means `GameLoop` manages subtree lifetimes itself.

## Risks
- **The real export cannot read content from C# (S1-5).** *Mitigation:* `EmbeddedResource` fallback behind `IContentSource`; V1 runs first.
- **`keep_height` clips HUD layouts at narrow aspects (< 16:9).** *Mitigation:* the 1280×720 minimum window; HUD anchors to edges; the UX review checks 4:3 and 16:10 (V4).
- **The exporter looks for a differently named solution.** *Mitigation:* V2 checks it in the first export story; renaming is mechanical.
- **Export exits 0 despite errors (S1-3).** *Mitigation:* the export script greps the log for "no solution file" and C# exceptions and fails the build, and asserts the presence of `PerformativeMail.Game.dll` in the output.
- **The editor `--quit-after` segfaulted at shutdown once in the spike.** Not investigated. *Mitigation:* CI headless runs use `--quit` from code, and the issue is noted for the export script.
- **The ribbon's C# draw call marshals arrays (V3).** *Mitigation:* the `RenderingServer`/`ArrayMesh` fallback (§6).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| grid-inventory.md | L535/L543/H-PERF-4 / TR-inventory-027: one custom-drawn Control; 0 B/frame; live-region feedback | §6 grid panel |
| manual-delivery.md | L376 / TR-delivery-023: flap a separate node within 150 tris; reach scrubbed by hold progress | §6 MailboxView, PlayerView |
| manual-delivery.md | L367 / TR-delivery-024: visual-only lunge ≤ 0.8 m via rig offset, never feeds the Sim | §6 `RigOffset` |
| mail-destinations.md | L618/L637/L639 / TR-mail-022: residents, envelope pool, flashes, audio voices | §5 pool table |
| throwing-catching.md | L1023 / TR-throwing-017: aim preview ≤ 0.3 ms/frame, 0 alloc, screen-space dashed ribbon | §6 aim ribbon |
| throwing-catching.md | L967–969 / TR-throwing-021: spin from flightId; MultiMesh bundles; ×N hidden beyond 35 m | §5 flights, tag pool |
| content-data-definitions.md | CD28 / TR-content-027: ViewCatalog keyed by def id → DefIndex arrays; placeholder | §5 |
| content-data-definitions.md | CD26 / TR-content-026: F6 key, debug only, directory resolution, refused with peers | §4 |
| content-data-definitions.md | S1/S2/S4 spikes | Spike table; V1 |
| hud-notifications.md | HN-18: stretch mode, aspect, height/1080, min window | §7 |
| simulation-core.md | R17 / ADR-0002: one GameLoop, `ProcessMode.Always`; placement | §1 |

## Performance Implications
- **CPU**: the ribbon ≤ 0.3 ms per frame; the panel redraws only on change; pools remove per-frame instantiation.
- **Memory**: pre-instantiated pools (≈ 48 + 8 + 12 props, 8 tags, 4 MultiMeshes, ≤ 16 voices) are fixed at boot.
- **Load Time**: pool instantiation and catalog resolution at boot (small). Content parse ≤ 100 ms (ADR-0010).
- **Network**: none.

## Migration Plan
1. Godot-host-shell story 1: `project.godot` settings (§7, main scene, `[dotnet]`), `PerformativeMail.Game.csproj` (ADR-0001), `PerformativeMail.Game.sln` (§9), `Main.tscn` + `GameLoop` + `PresentationRoot` stub, `.gdignore` files.
2. ViewCatalog + resolver + pools (M-1a, with `BuiltInContent` def ids).
3. Per-epic views.
4. M0: `ContentHost`, F6 key, `export_presets.cfg`, the first export story (V1–V5).

## Validation Criteria
- A headless boot of `Main.tscn` logs the boot steps in §3 order. `ProcessPriority` and `ProcessMode` are asserted. No autoloads in `project.godot` (CI grep).
- A catalog uid CI check passes. Renaming a view scene plus an editor rescan keeps the catalog resolving (repeat of S4-3 on the real project).
- The resolver test: missing id → placeholder in debug, content error with `release=true`.
- Pool caps: a soak with 200 flights and 100 outcomes per second never exceeds the caps or instantiates nodes after boot.
- Panel and ribbon: 0 B managed per frame over 600 frames (`GC.GetAllocatedBytesForCurrentThread`). Ribbon ≤ 0.3 ms.
- The first export passes V1 (all items recorded in the export story's evidence).

## GDD Impact (do not edit GDDs here; reconcile at re-review)
- **content-data-definitions.md L792:** "ContentHost shape (autoload vs static; async load marshalled back with `CallDeferred`)" → plain C# class, synchronous, no autoload, no `CallDeferred`.
- **content-data-definitions.md CD28:** the `.tres` must be editor-saved (uid). "`Duplicate(true)` allowed" is moot: catalogs are never duplicated.
- **content-data-definitions.md S1:** answered for packing (`include_filter` required). The `DirAccess`-enumeration question is dropped (the manifest stays).
- **hud-notifications.md HN-18 / L36:** answered (§7; the notification queue is a node, not an autoload).
- **simulation-core.md R17:** placement decided: main-scene root, no autoload.
- **architecture.md §4 step 1** "single autoload or root node; NEEDS VERIFICATION" → root node, VERIFIED (OQ1).

## Related
- ADR-0001 (export mapping completed here), ADR-0002 (`GameLoop`), ADR-0003, ADR-0004, ADR-0010 (ContentHost/ViewCatalog contract), ADR-0016 (published states, render clock), ADR-0018 (HUD stack), ADR-0024, ADR-0029.
- `prototypes/spike-adr0019/REPORT.md`, `prototypes/spike-godot-dotnet/REPORT.md`.

## Errata
- 2026-10-17 — §3 step 4: input-adapter placement was implied by OQ1's autoload-first / `ProcessPriority` ordering; now the adapter is the last child of the root window, re-asserted on `ChildEnteredTree` (input runs in reverse tree order; `ProcessPriority` has no effect) — consequence of the owner-accepted ADR-0012 §7 D-1; source TD-021.
- 2026-10-23 — accepted by owner — **E6** (l.313, TR-inventory-027): live-region feedback for the grid panel moves to the M1 accessibility pass (grid-inventory 023/024 ACs split into a Deferred story). The headless smoke CI at l.338 **stays** at M-1a. Evan, Mail Lead chat, 2026-10-23 8:10 AM PT, approving draft errata E1–E6 and E9 (decision log 2026-10-23, m1a-replan): "approve all". Context: 7:15 AM PT "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example".
- 2026-10-23 — accepted by owner — **E2** (l.342): the panel/ribbon 0 B-per-frame checks are reported, not blocking, until M0. Evan, Mail Lead chat, 2026-10-23 8:10 AM PT, approving draft errata E1–E6 and E9 (decision log 2026-10-23, m1a-replan): "approve all". Context: 7:15 AM PT "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example".
