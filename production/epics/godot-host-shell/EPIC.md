# Epic: Godot Host Shell & Greybox Scene

> **Layer**: Foundation (architecture.md v1.1 §System Layer Map: "Godot host shell (GameLoop node, ContentHost, boot)")
> **GDD**: design/gdd/simulation-core.md (R2, R16–R18 `GameLoop`/host role) + design/gdd/content-data-definitions.md (CD21 `IContentSource`, CD28 `ViewCatalog`). This module has no GDD of its own: it is the Presentation half of those two Foundation systems (architecture.md §Per-system layer assignment, rows 1–2).
> **Architecture Module**: Godot host shell (`src/Game/`; `PerformativeMail.Game.csproj` at the repo root, `Godot.NET.Sdk/4.7.1`)
> **Milestone slice**: M-1a
> **Engine Risk**: HIGH. `Node._Process` main-thread timing and `ProcessMode` (verified by the spike in `prototypes/spike-godot-dotnet/`); `ProjectReference` from `Godot.NET.Sdk/4.7.1` (verified, ADR-0001 F1); the `FileAccess.GetFileAsBytes` export path and the PCK filter (NEEDS VERIFICATION, S1); scene placement (ADR-0019, Accepted 2026-10-16; the spikes `prototypes/spike-adr0019/` verified OQ1/S1/S4 but a template-backed export is still UNVERIFIED, V1).
> **Est. size**: M — 11 stories (planning estimate was 8–10; the extra story splits boot from session wiring)
> **Implementation priority**: 9 of 9 (last; uses `~/.local/bin/godot-mono`)
> **Status**: Ready
> **Stories**: 11 stories — 3 Complete, 8 Ready (counts refreshed 2026-10-21 from the story headers) — see the Stories table below

## Overview

This epic puts the Godot-free Sim on screen. It covers:
- the hand-written `PerformativeMail.Game.csproj` at the repo root (`Godot.NET.Sdk/4.7.1`), which excludes `src/PerformativeMail.*/**`, `tests/**`, `tools/**` and `**/obj/**` from its compile glob and references the Sim and `Client.Core` once each by `ProjectReference`. The Godot project is kept out of the Godot-free `PerformativeMail.sln`.
- the `ExportDebug`/`ExportRelease` → `Optimize`/`SIM_ASSERT` mapping via `Directory.Build.props`/`.targets` (ADR-0001 §4).
- one `GameLoop` node per process, whose `_Process` calls `FrameRunner.RunFrame()` with a `Stopwatch` timestamp. It never uses `_Process` delta, `_PhysicsProcess` or `SceneTree.Paused`, and the Sim runs on the main thread only (ADR-0002 §3, §5).
- the boot order:
  1. build the `ContentSet` (`BuiltInContent` at M-1a; a `ContentHost : IContentSource` reading bytes from `res://content/` when the minimal loader is used);
  2. `SimComposition.Build(profile)`;
  3. the loopback adapter (`network-adapter-loopback`);
  4. `ClientLayer`;
  5. views.
- a minimal `ViewCatalog`: def id → `PackedScene`, with a placeholder view and a warning when a view is missing.
- the M-1a greybox scene: the hand-authored street from `world-stub` rendered as untextured geometry, plus the scene root that the feature epics' views (carrier, mailboxes, WorldItems, aim ribbon, prompt anchor) attach to.

The spike in `prototypes/spike-godot-dotnet/` (`GameLoop.cs`, `Main.tscn`, `REPORT.md`) is reference material only. Prototype code is never promoted; the stories rewrite it to the ADR contracts.

**In scope:**
- the Godot csproj and build mapping;
- `GameLoop` and `FrameRunner` integration;
- the boot/composition order;
- a minimal `ContentHost` and `ViewCatalog`;
- the greybox scene root and street geometry;
- a headless smoke run (`godot-mono --headless`) that boots, ticks 300 times and exits cleanly, with CI wiring.

**Out of scope:**
- the Godot input adapter → `input-controls`;
- entity views and the HUD → their own epics;
- export templates and the export path (Q-ARCH-02 remainder → ADR-0019);
- hot reload → M0.

**Risks.**
- (1) **ADR-0019 (Godot presentation scene architecture) is Accepted (2026-10-16, verified by grep of `docs/architecture/adr-0019-godot-scene-architecture.md`).** It decides `GameLoop` as the root of `Main.tscn` with zero autoloads, the scene tree, boot order, `ContentHost`, `ViewCatalog`, view pools and caps, the stretch mode and aspect (`canvas_items` / `keep_height`, 1920×1080 reference, 1600×900 initial window) and the 1280×720 minimum window, the Upgrade-Project-Files policy and the export configuration. Every story here cites it. *(This paragraph previously said ADR-0019 was "not written"; corrected when the stories were created.)*
- (2) `Directory.Build.props` builds the Godot project warning-free except for an `Environment` ambiguity under `ImplicitUsings` (ADR-0001 F7). The csproj must handle it.
- (3) `InvariantGlobalization` is not in force in editor runs (ADR-0001 F4).
- (4) The 4.6 scene format change: run Upgrade Project Files once (architecture.md §MEDIUM risk).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework | `PerformativeMail.Game.csproj` at root, glob exclusions, `ProjectReference` (spike-verified), Export config → `Optimize`/`SIM_ASSERT` mapping | HIGH (spike-verified) |
| ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim | one `GameLoop` node, `_Process` → `FrameRunner.RunFrame()` with a `Stopwatch` timestamp, main thread only, never `SceneTree.Paused` | HIGH (spike-verified) |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | the host calls `SimComposition.Build` once at boot; no statics or service locators | LOW |
| ADR-0010: Content Format, Loading, ContentSet and StatSheet | `IContentSource` is the only file seam; `ViewCatalog` contract (keyed by def id, placeholder on missing view) | HIGH (Godot half deferred, S1–S4) |
| ADR-0011: Network Adapter Boundary | the host process uses the loopback adapter with the same encoder | LOW (seam) |
| ADR-0019: Godot Presentation Scene Architecture, ContentHost, ViewCatalog and Export Configuration (Accepted 2026-10-16) | `GameLoop` is the root of `Main.tscn` (zero autoloads, no scene changes), the node tree, the nine-step boot order, plain-C# `ContentHost`, editor-saved `ViewCatalog` + resolver, view pools and caps, stretch/aspect/min window, Upgrade Project Files policy, `.gdignore` list, Godot-only game `.sln` and export config | HIGH (spikes OQ1/S1/S4 verified; export V1–V5 UNVERIFIED) |
| ADR-0012: Input Router Core and Godot Input Adapter (Accepted; adapter rows provisional) | `GameLoop` calls `DispatchImmediates` then `FlushToLatch` immediately before `RunFrame`; `Main/Input` slot — the shell supplies the hook points only | HIGH (adapter, spike-gated; input-controls owns it) |
| ADR-0016: ClientLayer Previews and Presentation Timing (Accepted) | `ClientLayer.Attach(ports)` once after `Build`; `RenderClock` sampled once per frame before `Present`; `PresentationEventStream` published in the frame a batch arrives | LOW |
| ADR-0026: World Query Contract and the M-1a Street Stub (Accepted) | the terrain/solids/lots/Post Office the greybox street and per-lot views are built from | LOW |

## GDD Requirements

This module has no Appendix A rows of its own. These rows carry its Godot half:

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-sim-007 | Sim on Godot main thread; owner thread captured at Freeze(); Debug asserts | ADR-0002 ✅ | M-1a |
| TR-sim-008 | Transport callbacks only enqueue; nothing applied inside a callback | ADR-0002 ✅ | M-1a |
| TR-sim-009 | Pause is a driver state; solo pause → Advance returns 0; SceneTree.Paused never used | ADR-0002 ✅ | M-1a |
| TR-sim-040 | Always-on checks to fixed record ring; asserts compile out of Release; SimCounters | ADR-0001 ✅ | M-1a (Export mapping half) |
| TR-movement-007 | Fixed-step driver with catch-up cap; local extrapolation ≤1 tick; no engine physics interpolation | ADR-0002 ✅ | M-1a |
| TR-movement-022 | Golden per-tick transform hash equal on Windows and Linux; Sim references no Godot | ADR-0001 ✅ | M-1a (glob/reference half) |
| TR-input-014 | MP pause is a local overlay, never SceneTree.Paused; Sim keeps ticking | ADR-0002 ✅ | M-1a |
| TR-content-021 | Loader reads via IContentSource; Sim never reads res:// | ADR-0010 ✅ | M-1a |
| TR-content-027 | Presentation ViewCatalog keyed by def id → DefIndex arrays; placeholder on missing view | ADR-0010 + ADR-0019 ✅ (both Accepted) | M-1a placeholder |
| TR-sim-001 / TR-sim-004 | Whole fixed ticks at 30 Hz; catch-up cap 4, excess dropped (the host constructs the driver from them) | ADR-0002 ✅ | M-1a (story 005) |
| TR-sim-013 / TR-sim-014 | Composition root called once by the host; `SimWorld`/`ClientLayer` separate, host loopback same encoder | ADR-0003 ✅ | M-1a (story 005) |
| TR-content-028 | Boot parse + validate + hash ≤ 100 ms CPU (ContentHost synchronous-load budget, advisory) | ADR-0010 ✅ | M-1a (story 007) |
| TR-hud-015 *(partial)* | 1080-reference px scaled by height; min 1280×720; stretch — the stretch/aspect/min-window numbers only; row still points at unwritten ADR-0018 | ADR-0019 §7 ✅ (registry row not updated) | M-1a (stories 002, 004) |
| TR-mail-022 *(caps only)* | Presentation caps (envelope props 48, residents 8, heap 12, voices 16) — constants and pool mechanism; pools' content is mail-destinations 016 | ADR-0019 §5 ✅ | M-1a (story 009) |

**Untraced (no TR-ID in `docs/architecture/tr-registry.yaml`; stories cite the ADR section instead):** scene placement / `GameLoop`-as-root / zero autoloads, the scene tree and `Initialize(deps)` inertness, the nine-step boot order, `.gdignore` hygiene and the game `.sln`, view pools and caps (the caps' source rows are `TR-mail-022` and the throwing rows, owned by those epics), stretch / minimum window (only partly `TR-hud-015`, whose registry row still reads "stretch undecided" and points at the unwritten ADR-0018), the headless smoke gate and CI wiring. The registry is not edited by this epic. Also registered but **owned elsewhere**: `TR-content-026` (F6 hot reload, M0).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- The SC R2/R16–R18 host-side behaviour is verified: one `GameLoop`, main-thread asserts, no `SceneTree.Paused`
- A headless `godot-mono` smoke run boots the greybox, ticks ≥300 times and exits 0. It is wired into `tests/smoke/critical-paths.md`
- All Logic and Integration stories have passing test files in `tests/` (Godot-free parts) or a recorded headless run (Godot parts)
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (the greybox street at 1280×720 and 1920×1080)

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Game csproj, game `.sln`, `[dotnet]` setting, `.gdignore` hygiene, Export* build check](story-001-game-csproj-solution-gdignore-build-mapping.md) | Integration | Complete | ADR-0001 |
| 002 | [`Main.tscn` scene root, ADR-0019 node tree, explicit project settings, headless smoke harness](story-002-main-scene-tree-project-settings-smoke-harness.md) | Integration | Complete | ADR-0019 |
| 003 | [`GameLoop` frame loop: `Stopwatch` clock, `RunFrame` once per frame, hook points, driver-state pause, fault stop](story-003-gameloop-frame-loop-clock-pause-fault.md) | Integration | Complete | ADR-0002 |
| 004 | [Boot sequence (nine steps), error screen, window guard, `BuiltInContent` step, extension slots](story-004-boot-sequence-error-screen-content-step.md) | Integration | Ready | ADR-0019 |
| 005 | [Host session wiring: `SimComposition.Build` over loopback, `ClientLayer.Attach(ports)`, sample sink, local join, render clock](story-005-host-session-composition-ports-render-clock.md) | Integration | Ready | ADR-0019 |
| 006 | [Presentation plumbing: `PresenterDeps`, `Initialize(deps)` injection, World/Hud/Audio registration seams, `PresentationEventStream` feed](story-006-presenters-deps-injection-event-stream.md) | Integration | Ready | ADR-0019 |
| 007 | [Minimal `ContentHost : IContentSource` over `res://content/` and the opt-in host content mode](story-007-contenthost-minimal-iconentsource.md) | Integration | Ready | ADR-0019 |
| 008 | [`ViewCatalog`, `ViewCatalogResolver`, placeholder view + warning, editor-saved catalogs, uid CI check](story-008-viewcatalog-resolver-placeholder-uid-check.md) | Integration | Ready | ADR-0019 |
| 009 | [`EntityViewRegistry`, `NodePool<T>` + caps, one view per lot under `World/Destinations`](story-009-entity-view-registry-pools-destination-views.md) | Integration | Ready | ADR-0019 |
| 010 | [Greybox street: `TerrainView` from `IWorldQuery` (ground, water, solids, pad, light, fallback camera)](story-010-greybox-street-terrain-view.md) | Visual/Feel | Ready | ADR-0019 |
| 011 | [Headless smoke (boot, ≥ 300 ticks, exit 0), smoke wrapper, CI wiring, `critical-paths.md`](story-011-headless-smoke-300-ticks-ci-critical-paths.md) | Integration | Ready | ADR-0019 |

**11 stories: 10 Integration, 1 Visual/Feel — 2 Complete, 9 Ready, 0 Blocked (refreshed 2026-10-20).** Every governing ADR (0001, 0002, 0003, 0010, 0011, 0012, 0016, 0019, 0026) is Accepted. Stories depend on other stories, not on missing decisions, so none is Blocked.

**Suggested order:** 001 → 002 → 003 → 004 → 005 → 006 → 007 → 008 → 009 → 010 → 011. 007 may run any time after 004. Stories 001–004 can start as soon as simulation-core 001/010/024/025, network-adapter-loopback 001 and content-data 008 exist; 005 needs network-adapter-loopback 007 and player-character 019/020; 009–010 need the world-stub street (006/007).

**Sizing.** `story_granularity: fine` (one AC per story) was overridden by the orchestrator rule: each story is one `/dev-story` session (≤ ~1 day, ≤ ~600 lines of production code and tests), 1–4 closely related ACs. `docs.density: thorough`. Review mode `lean`: QL-STORY-READY skipped (every `## QA Test Cases` reads "N/A — no qa-lead specs at this tier"). Godot-side evidence is the headless smoke scenarios (`--smoke=<name>`, story 002) until gdUnit4 exists.

### Attach points the feature epics' Godot stories use

| Capability a feature epic needs | Provided by | Used by |
|---|---|---|
| `Main.tscn`, `Main/Input` slot (first child), `World/{Terrain,Destinations,Entities,Flights,Props,CameraRig/Boom/Camera3D}`, `Hud`, `Audio`, `Screens`, `Debug` | Story 002 | player-character 022/023, throwing-catching 020, input-controls 026, mail-destinations 016 |
| `GameLoop` frame order, pre-frame / post-present hooks, `TryPause`/`Resume`, fault stop | Story 003 | input-controls 028 (`DispatchImmediates` → `FlushToLatch` → `RunFrame` → `Present` → `PresentedPromptLatch`; pause wiring) |
| Boot plan with explicit contributor slots (steps 3, 4, 6, 7, 8), error screen | Story 004 | input-controls 028 (`InputBoot` at step 4) |
| `IPortContributor` / `ClientPorts` / `ClientLayer.Attach`, `ITransformSampleSink` → `TryWriteSample`, `IRenderClock`, local join | Story 005 | player-character 019/020/025, input-controls 028 |
| `PresenterDeps`, `IInjectable<T>.Initialize(deps)`, `IPresentedView`, `WorldPresenter.Register`, `HudPresenter.AddLayer`, `AudioPresenter.RegisterSink`, `PresentationEventStream` cursors | Story 006 | player-character 022/023/026, throwing-catching 018/020/021, manual-delivery 006/011, mail-destinations 016, grid-inventory 023/024 |
| `ContentHost` (opt-in) | Story 007 | M0 content work |
| `ViewCatalog` + resolver + the three `Catalogs/*.tres` | Story 008 | mail-destinations 016 (`house`), grid-inventory 023 (item icons), the unowned `WorldItemView` |
| `EntityViewRegistry`/`EntityViewHost`, `NodePool<T>`/`PoolSpec`/`PresentationCaps`, `IDestinationView`/`LotBinding` | Story 009 | player-character 022, mail-destinations 016, throwing-catching 020/021 |
| Greybox street, light, fallback camera | Story 010 | every Godot-side screenshot |
| `--smoke=<name>` harness and `--smoke=list` discovery, wrapper, CI job | Stories 002 and 011 | input-controls 029 (`input-adapter`), every feature epic's Godot evidence |

## Notes (decisions, deferrals, gaps)

**Scope.** M-1a only. Out of scope (per this epic's Overview): the Godot input adapter (input-controls 026–029), entity views and HUD (their epics), export templates and the export path (ADR-0019 §9 / V1–V5, the "first export story"), hot reload and F6 (TR-content-026, M0), the real transport, persistence (ADR-0024 not written), the audio mix. `ContentHost` is built now (story 007) although ADR-0019's Ordering Note schedules it for M0, because the epic's scope names a minimal one; the default content source stays `BuiltInContent` and the host mode is opt-in.

**TR coverage.** Registry rows used: TR-sim-001/004/007/008/009/013/014/040, TR-movement-007/022, TR-input-014, TR-content-021/027/028, TR-hud-015 (partial), TR-mail-022 (caps only). Requirements with **no TR-ID in the registry** (cited to the ADR section in each story): `GameLoop`-as-root and zero autoloads, the scene tree and `Initialize(deps)`, the nine-step boot order, the pre-frame/post-present hook points, `ClientPorts`/`Attach` wiring, `PresenterDeps` and the event-stream feed, `.gdignore` and game `.sln` hygiene, the smoke gate. The registry was not edited.

**Unowned seams surfaced (no story in any epic owns them; the stories above add or flag them):**
- `ClientPorts` / `ClientLayer.Attach` / `IRequestSink` (ADR-0016 §1): added by story 005 if absent (the player-character and sim-core notes flagged this). 
- `PresentationEventStream` publication from `ClientAdapter` (ADR-0016 §5): no loopback story publishes decoded batches; story 006 adds the smallest observer seam.
- `IWorldQuery` reaching Presentation: story 006 asks `SimHost` for a read-only accessor (immutable table interface).
- The greybox `WorldItemView` (ADR-0019 §2 lists it under `Entities`; throwing-catching 020 notes it is unowned): the shell provides `EntityViewHost.SpawnForDef` and the item/mail-kind catalogs; the view itself belongs to grid-inventory or a presentation story.
- `ICarrierLoadSource` and `IHeldItemView` adapters (player-character 020/025 define the ports; the grid-inventory Client.Core mirror must supply them): attach through story 005's `IPortContributor`.
- The selected-hotbar-slot consumer (input-controls 013 / throwing-catching EPIC seam 1): not a shell concern; input-controls 028 registers whatever sink exists.
- Hud CanvasLayer stack order and notification queue (ADR-0018, not written): story 006 supplies `AddLayer(order)` only.
- `IPeerPresence` (`AnyRemotePeer`) and `SoloPausePolicy` in `src/PerformativeMail.Client.Core/Net/` — **owned by Networking & Session** (ADR-0002 §4, GDD R16); story 003 adds the seam and the M-1a `LoopbackPeerPresence` only, and `GameLoop.TryPause` obeys the policy's answer. ADR-0011 amendment pending (TD-111); the M0 remote session fills it.

**Conflicts and resolutions.**
1. **Stale epic text.** The previous EPIC said ADR-0019 was "not written / missing"; it is Accepted (2026-10-16). Corrected throughout.
2. **`ViewCatalog` release behaviour.** CD28 / CDD-SCHEMA-7 say a missing view is a placeholder + warning "in every build" with a *release-export check* making it an error; ADR-0019 §5 says placeholder + warning in debug and a content error in release exports (`!OS.IsDebugBuild()`). Stories follow ADR-0019 (Accepted, later); M-1a runs debug builds so the difference is not yet observable. GDD wording to reconcile at the next content-data GDD pass.
3. **`TickDriver.Start` placement.** ADR-0019 §3 lists `TickDriver.Start` as boot step 9; ADR-0002 §3 says it is called on the first `_Process` after boot, not in `_Ready`. Resolved: step 9 only arms `StartPending` (story 004); story 003 starts the driver on the first `_Process`.
4. **`.gdignore` and the game solution.** ADR-0001 §2 puts `src/.gdignore` in place and calls `PerformativeMail.Game.sln` "IDE-only"; ADR-0019 §8–§9 moves the ignore into each `src/PerformativeMail.*` folder and makes the Godot-only `.sln` required by the exporter. Story 001 follows ADR-0019.
5. **`Godot.NET.Sdk` source.** ADR-0001 says nuget.org and forbids a committed machine-specific `nuget.config`; the spike used a local nupkg path. Story 001 treats clean-runner restore as UNVERIFIED and fails loudly.
6. **Duplicate catalog ids** and the **`EntityViewRegistry` capacity (512)** are story decisions where the ADR is silent; both are flagged in their stories (008, 009) and in Last Updated when implemented.

**Engine risk.** HIGH (VERSION.md). Every story carries it; UNVERIFIED items stay UNVERIFIED in the stories: exported-build content read (V1/S1-5), `ProcessThreadGroup`/`low_processor_mode`/`physics_interpolation` effects, `Window.MinSize` on Wayland/hiDPI and `keep_height` below 16:9 (V4), Godot 4.7.1 mono download URL on CI, headless shared-library needs.

## Next Step

Run `/story-readiness production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md`, then `/dev-story` — in story order. Stories 001–003 need only simulation-core 001/010/024 and network-adapter-loopback 001; stories 005 onward need the loopback session (network-adapter-loopback 007), `BuiltInContent` (content-data 008) and the world-stub street (world-stub 006/007) to be Done first (epics index order 1–7).
