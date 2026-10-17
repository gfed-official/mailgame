# Story 002: `Main.tscn` scene root, the ADR-0019 node tree, explicit project settings and the headless smoke harness

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines prod + the scene + smoke scenario)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17 — implemented (dev-story, autonomous). `SmokeRunner : Node` is added as the last child of `Main` by `GameLoop._Ready` only when `--smoke=` is present in a debug build and polls in its own `_Process` (story 003's `GameLoop._Process` untouched); `GameLoop.RequestBoot()` is the story-004 seam and returns false today, so `NeedsBoot` scenarios exit 4. `Main.tscn` is hand-written `format=3` (4.7.1 loads it); one headless `--editor --quit` pass created the `.uid` sidecars, which are kept. The live `Window` content-scale assertions hold under `--headless`. `check-scene-rules.sh` normalises `project.godot`'s section-relative keys to full `section/key` lines before matching (the first cut grepped the flat form and missed `[application] run/main_scene`). Evidence: `production/qa/evidence/host-shell-scene-root-evidence.md`.

## Context

**GDD**: `design/gdd/simulation-core.md` (R17 "one `GameLoop` node"; node placement is ADR-0019's), `design/gdd/hud-notifications.md` HN-18 (stretch, aspect, 1080-reference px, minimum window — numbers decided by ADR-0019 §7)
**Requirement**: `TR-movement-007` (the project-settings half: `physics_interpolation` and `low_processor_mode` stay off; nothing moves in `_PhysicsProcess`), `TR-hud-015` (the stretch / aspect / 1080-reference half only — the registry row still says "stretch undecided"; ADR-0019 §7 decided it; the registry is not edited here)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The scene tree, `GameLoop`-as-root, zero autoloads, `Initialize(deps)` inertness and the smoke harness have **no TR-ID in the registry**; they cite ADR-0019 §1–§2, §7 and Implementation Guidelines.)*

**ADR Governing Implementation**: ADR-0019: Godot Presentation Scene Architecture, ContentHost, ViewCatalog and Export Configuration (§1 `GameLoop` is the root of the one main scene, zero autoloads, no scene changes; §2 the scene tree; §7 window, stretch and scale) — Accepted 2026-10-16 (primary); ADR-0002 (§3 `ProcessMode = Always`, `ProcessPriority = int.MinValue`, main-thread process group, `low_processor_mode` and `physics_interpolation` off) — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: `res://src/Game/Main.tscn`, root `Main` (`Node`, `GameLoop.cs`), is the main scene. There are **no autoloads** and `ChangeSceneToFile`/`ChangeSceneToPacked` are never used; screens are child subtrees of `Main/Screens`. Children `_Ready` before the root (spike OQ1-1), so every view is inert until `GameLoop` injects its dependencies through `Initialize(deps)`; views never look dependencies up in `_Ready`. `project.godot` sets stretch explicitly (a hand-written 4.7.1 project gets `disabled`/`keep`, spike DEF-1): viewport 1920×1080, `canvas_items`, `keep_height`, window override 1600×900.
**ADR Version**: ADR-0019 2026-10-16; ADR-0002 2026-10-16

**Engine**: Godot 4.7.1 .NET (`mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . -- --smoke=<name>`) | **Risk**: HIGH
**Engine Notes**: Scenes are saved in the 4.6+ format (no `load_steps`, unique node ids): author `Main.tscn` by hand or via a scripted headless save, run `godot-mono --headless --editor --path . --quit` once so the editor assigns `.uid` sidecars, and commit them. Do **not** run Upgrade Project Files piecemeal (ADR-0019 §8). Verify the project-setting key names at runtime (`ProjectSettings.GetSetting`) — a mistyped key in a hand-written `project.godot` is silently ignored (DEF-1). The C# enums are `Window.ContentScaleModeEnum.CanvasItems` and `Window.ContentScaleAspectEnum.KeepHeight`. `ProcessThreadGroup`, `low_processor_mode` and `physics_interpolation` behaviour in 4.7.1 are UNVERIFIED (ADR-0002) — this story asserts the settings, not their engine effects. The editor's `--quit-after` segfaulted at shutdown once in a spike: smoke runs quit from code (`GetTree().Quit(code)`).

**Control Manifest Rules (Foundation / Presentation)**:
- Required: `Main.tscn`'s root is the only `GameLoop`; screens are child subtrees of `Main/Screens`; views are inert until `GameLoop` injects dependencies via `Initialize(deps)` (ADR-0019 §1, Implementation Guidelines)
- Required: `GameLoop` has `ProcessMode = Always`, `ProcessPriority = int.MinValue` and the main-thread (or inherited) `ProcessThreadGroup`; `low_processor_mode` and `physics/common/physics_interpolation` stay off (ADR-0002 §3)
- Forbidden: autoloads; `ChangeSceneToFile`/`ChangeSceneToPacked`; `_PhysicsProcess` driving anything; `SceneTree.Paused` for game pause (ADR-0019, ADR-0002)
- Guardrail: headless smoke runs finish in well under 60 s and always end with an explicit exit code

---

## Acceptance Criteria

*From ADR-0019 §1, §2, §7 and Validation Criteria ("a headless boot of `Main.tscn` … `ProcessPriority` and `ProcessMode` are asserted. No autoloads in `project.godot` (CI grep)"), scoped to this story. Delta from the repo today: `project.godot` has no main scene, no display or stretch settings and nothing beyond name, features and the renderer; `src/Game/` does not exist before story 001.*

- [ ] **Scene tree (ADR-0019 §2).** `src/Game/Main.tscn` has the tree `Main (Node, GameLoop) → { Input (Node, placeholder slot — the FIRST child), World (Node3D, WorldPresenter) → { Terrain (Node3D), Destinations (Node3D), Entities (Node3D), Flights (Node3D), Props (Node3D), CameraRig (Node3D) → Boom (Node3D) → Camera3D }, Hud (Node, HudPresenter), Audio (Node, AudioPresenter), Screens (Node), Debug (CanvasLayer) }`. Node names are exactly these (feature epics address them by path: `Main/Input`, `Main/World/Entities`, `Main/World/Destinations`, `Main/World/Flights`, `Main/World/Props`, `Main/World/CameraRig/Boom/Camera3D`, `Main/Hud`, `Main/Audio`, `Main/Screens`). `Main` has `ProcessMode = Always`, `ProcessPriority = int.MinValue`, `ProcessThreadGroup = Inherit`; `Debug` is removed in `GameLoop._Ready` when `!OS.IsDebugBuild()`. `WorldPresenter`, `HudPresenter`, `AudioPresenter` exist as inert classes (namespace `PerformativeMail.Game.Presentation`) implementing a small `IPresenter { void Present(int alphaPermille); }`; their real behaviour is story 006.
- [ ] **Inert children; one root; no scene changes; no autoloads.** Instantiating `Main.tscn` with `GameLoop.AutoBoot = false` (a documented test seam, default `true`) and adding it to a `SceneTree` leaves every presenter/view with `IsInitialized == false`, performs no `GetNode`/singleton lookups in any child `_Ready`, and starts no tick. `project.godot` has no `[autoload]` section; `src/Game/**` contains no `ChangeSceneToFile`/`ChangeSceneToPacked`; the tree contains exactly one `GameLoop`. New `tools/ci/check-scene-rules.sh` (with a `--self-check` seen failing on planted violations) greps for the autoload section, scene-change calls, `low_processor_mode=true`, `physics_interpolation=true` and `_PhysicsProcess` overrides under `src/Game/`, printing `checked N rules` and exiting non-zero on any hit or on zero rules.
- [ ] **Project settings are explicit and read back at runtime (ADR-0019 §7).** `project.godot` sets `application/run/main_scene="res://src/Game/Main.tscn"`, `display/window/size/viewport_width=1920`, `viewport_height=1080`, `window_width_override=1600`, `window_height_override=900`, `display/window/stretch/mode="canvas_items"`, `display/window/stretch/aspect="keep_height"`, `application/run/low_processor_mode=false`, `physics/common/physics_interpolation=false`. A smoke scenario reads each back through `ProjectSettings.GetSetting` **and** asserts the live `Window` state (`ContentScaleMode == CanvasItems`, `ContentScaleAspect == KeepHeight`, `ContentScaleSize == (1920, 1080)`), because a typo'd key is silently ignored. (The 1280×720 minimum window is applied by story 004's boot step 1; no project setting exists for it.)
- [ ] **Smoke harness.** `godot-mono --headless --path . -- --smoke=<name>` runs the named scenario (`[SmokeScenario("name")]` attribute discovery over the assembly, so other epics add scenarios — input-controls 029's `input-adapter` — without touching shell code), prints `SMOKE <name> PASS|FAIL <detail>` lines and a final `SMOKE RESULT name=<n> pass=<p> fail=<f>`, and quits from code: exit `0` pass, `1` a failed assertion, `2` unknown scenario, `3` zero assertions made (a scenario that asserted nothing is **not** a pass), `4` boot failed for a scenario that needs boot, `5` timeout (default 60 s wall clock, scenario-overridable). A negative-path scenario that is *designed* to see a failed boot declares `ExpectedExit = 4` (default 0), and `--smoke=list` prints one `SMOKE LIST <name> expect=<code>` line per discovered scenario (story 011's wrapper derives its scenario set from it). `--smoke` is honoured only when `OS.IsDebugBuild()` (logged and ignored otherwise). A scenario declares `NeedsBoot` and gets `Start(ctx)` plus a per-frame `Poll(ctx)` (`Running`/`Done`); scenarios that do not need boot run against the un-booted tree. The first scenario, `main-tree`, asserts the three criteria above.

---

## Implementation Notes

*Derived from ADR-0019 §1–§2, §7 and Implementation Guidelines:*

- Files: `src/Game/Main.tscn` (+ `.uid`), `src/Game/Hosting/GameLoop.cs` (skeleton only: `partial class GameLoop : Node`, `[Export] public bool AutoBoot = true`, `_Ready` sets `ProcessMode`/`ProcessPriority`, frees `Debug` in non-debug builds, then — if not smoke-without-boot — leaves the boot call to story 004; the frame loop is story 003), `src/Game/Presentation/IPresenter.cs` + `WorldPresenter.cs`/`HudPresenter.cs`/`AudioPresenter.cs`, `src/Game/Smoke/{SmokeScenarioAttribute,SmokeScenario,SmokeContext,SmokeRunner,MainTreeSmoke}.cs`, `tools/ci/check-scene-rules.sh`. Namespaces `PerformativeMail.Game.*`.
- The `Main/Input` slot is a plain `Node` named `Input`. Input-controls story 026 attaches `InputAdapter.cs` to it (it is the first child of `Main`, before `World`, with `ProcessMode = Always`, `ProcessPriority = int.MinValue`); this story only guarantees the slot exists and is first. Note: `Main/Input` is a node name, not the `Godot.Input` singleton — never shadow `Godot.Input` with a using-alias.
- Named slots other epics rely on, and what attaches to each: `World/Terrain` ← `TerrainView` (story 010); `World/Destinations` ← per-lot views (story 009; mail-destinations 016's `MailboxView`); `World/Entities` ← `PlayerView`, `WorldItemView` (player-character 022; the grid/throwing "unowned `WorldItemView` seam"); `World/Flights` ← `FlightView` (throwing-catching 020); `World/Props` ← envelope/resident/heap pools (mail-destinations 016); `World/CameraRig/Boom/Camera3D` ← `CameraRig.cs` (player-character 023, a child of `Main/World`); `Hud`/`Audio` ← sub-presenters (story 006); `Screens` ← boot/error/run subtrees (story 004).
- `GameLoop` must not be `static`-reachable: no `Instance` singleton; presenters receive it (or its services) through `Initialize(deps)` in story 006.
- Smoke harness conventions: scenarios are plain classes found by reflection over `[SmokeScenario]` (read-only discovery at startup, off the per-frame path); `SmokeContext` offers `Pass(msg)`, `Fail(msg)`, `Quit()` and the booted `GameLoop`. Keep log lines machine-greppable (story 011's wrapper parses them).
- `project.godot` is hand-written; keep the existing header and keys, add sections in Godot's canonical order (`[application]`, `[display]`, `[dotnet]`, `[physics]`, `[rendering]`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: `GameLoop._Process`, the frame order, pause, fault stop. Story 004: boot sequence, error screen, the 1280×720 minimum window. Story 006: presenter behaviour, `Initialize(deps)` injection, `PresenterDeps`.
- Story 010: `TerrainView`; story 009: `EntityViewRegistry`, pools, per-lot views.
- Input-controls 026/027: the `InputAdapter` node and the `ignore_joypad_on_unfocused_application` project setting. Hud CanvasLayer stack order (ADR-0018, not written). Camera behaviour (player-character 021/023).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: the `main-tree` smoke log (`SMOKE main-tree PASS …`, exit 0) from `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . -- --smoke=main-tree`, plus `tools/ci/check-scene-rules.sh` and its `--self-check` output, retained in `production/qa/evidence/host-shell-scene-root-evidence.md` — `/story-done` checks the scenario, the script and the evidence file exist; pass/fail is established by `/smoke-check` (story 011 wires the run into CI)

**Status**: [x] Created — `production/qa/evidence/host-shell-scene-root-evidence.md` (main-tree smoke 39/0 exit 0; `selftest-*` scenarios executed for exits 1/1/3/4/5; `list` shows expect= per scenario; unknown→2; scene-rules self-check 23 plants + 10/10 rules; re-captured 2026-10-17 after the gate fixes)

---

## Dependencies

- Depends on: Story 001 (csproj, `src/Game/`, `[dotnet]`)
- Unlocks: Stories 003, 004, 006, 009, 010, 011; `production/epics/input-controls/story-026-godot-input-adapter-node.md` (the `Main/Input` slot), `production/epics/player-character/story-022-playerview-scene-render-pose.md`, `production/epics/player-character/story-023-camera-rig-node-focus-cone.md`, `production/epics/throwing-catching/story-020-flight-view-multimesh-trail-shadow.md` (scene root and named nodes)

---

## Completion Notes
**Completed**: 2026-10-17
**Criteria**: 4/4 passing (no deferred criteria; the non-debug branch of AC-2/AC-4 — `Debug` stripped, `--smoke` ignored — is asserted in code but can only be observed in an exported release build, TD-098 (1))
**Deviations**: None from ADR-0019/ADR-0002. Design choices logged: `SmokeRunner : Node` polls in its own `_Process` (story 003's `GameLoop._Process` untouched); `GameLoop.RequestBoot()` returns false until story 004 (`NeedsBoot` scenarios exit 4); `Main.tscn` hand-written `format=3`, editor re-save deferred (TD-098 (4)). Five `selftest-*` scenarios were added beyond the story's `main-tree` so every exit code has executed evidence; `check-scene-rules.sh` gained R9 (presenter/view `_Ready` does no lookups) beyond the AC's five greps.
**Test Evidence**: Integration — `production/qa/evidence/host-shell-scene-root-evidence.md` (all seven smoke runs with process exit codes, scene-rules self-check and run); no xUnit test (Godot-side; the Godot-free test project cannot load it). The headless run is wired into CI by story 011; `check-scene-rules.sh` already runs in the `game-build` job.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (no blocking; findings 1/2/3/4/7 + part of 6 fixed in-story; 5/8/9/10/11/12 → TD-098) · QL-TEST-COVERAGE GAPS (none critical; gaps 1/2/5a/5b/5c/5e/5f fixed in-story; 3/4/5d → TD-098).
