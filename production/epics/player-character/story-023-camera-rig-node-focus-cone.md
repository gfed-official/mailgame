# Story 023: `CameraRig` node — follow spring, look, aim/swim profiles and the focus-cone cosine

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete (greybox; swimming and shoulder-swap profiles, cone table, replay, alloc probe and evidence dropped → TD-213 (8))
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day; ~350 lines prod + evidence)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; swimming and shoulder-swap profiles, cone table, replay, alloc probe and evidence dropped → TD-213 (8)); delivered by PR #89, merge c6a6b4b; see Completion Notes). Earlier: 2026-11-01 (docs branch `docs/lead-cleanups-2026-11-01`, off main 56c9446): implementation note added — place the camera from `CameraPose.Compose`'s output (Story 021 as built; TD-201 item 3).

## Context

**GDD**: `design/gdd/player-character-movement.md` (P9 camera; P11/F4 `focusConePm` → `cosMinPm`; Game Feel "Input to motion … look is per-frame (one frame)"; Visual/Audio "Locomotion is exempt from anticipation")
**Requirement**: `TR-movement-024` (camera boom — M-1a minimal; dither fade M1; registry coverage gap, ADR-0029 planned/not written), `TR-movement-007` (local extrapolation ≤ 1 tick; no engine physics interpolation — camera follows the rendered pose), `TR-movement-011` (the cone threshold handed to the Sim)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P9-1 (in-engine half), PCM-P11-1 (cone values), PCM-P2-3 (camera never below the player).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§2 `CameraRig → Boom → Camera3D` under `World`; `Present(alpha)` drives views; views never read `delta`; Initialize(deps) injection) — Accepted 2026-10-16 (primary); ADR-0014 (§2 the cone threshold is computed by the Presentation and passed into the Sim as an integer via the `InputFrame`) and ADR-0002 (§3 views animate from `AlphaPermille`) secondary — Accepted
**ADR Decision Summary**: `GameLoop` composes children explicitly through `Initialize(deps)`; views are inert until injected and animate only from data `PresentationRoot.Present(alphaPermille)` hands them. Nothing moves in `_PhysicsProcess` and `physics_interpolation` stays off. The focus cone's `cosMinPm` is computed from FOV and aspect by the Presentation and travels to the Sim as an integer in `InputFrame.FocusCosMinPm` (ADR-0012), never read back from a Godot call inside the Sim.
**ADR Version**: ADR-0019 2026-10-16; ADR-0014 2026-10-16; ADR-0002 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`) | **Risk**: MEDIUM
**Engine Notes**: Verify in-engine: `Camera3D.Fov` is the **vertical** FOV with the default `KeepAspect = Height`; `Node3D.Rotation` Euler order; BAM → yaw conversions use rounding and an unchecked `& 0xFFFF` because negative yaws wrap (ADR-0013 engine note 1). No post-cutoff API is needed beyond the 4.7.1 C# shapes in the manifest (`Animation.Length` double etc. are not used here). `physics/common/physics_interpolation` stays false.

**Control Manifest Rules (Presentation layer)**:
- Required: Presentation is called explicitly by `GameLoop` via `IPresentationRoot.Present(AlphaPermille)`; views animate from `AlphaPermille`/`IRenderClock`, never `delta` (ADR-0002 §3, ADR-0016)
- Required: views receive dependencies through an explicit `Initialize(deps)`; no lookups in `_Ready` (ADR-0019)
- Forbidden: moving Sim-driven entities or the camera in `_PhysicsProcess`; Godot physics interpolation; feeding any camera value into the Sim (ADR-0002, ADR-0019)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (P9, PCM-P9-1, PCM-P11-1), scoped to this story. Evidence per the Visual/Feel type: screenshots + numeric trace + sign-off.*

- [x] **Scene shape.** `World/CameraRig (Node3D) → Boom (Node3D) → Camera3D` exists exactly as ADR-0019 §2 draws it; the rig is inert until `Initialize(ICarrierView, IWorldQuery, IRenderClock, IFocusConeSink, …)` is called by `GameLoop`/the World presenter; it has no `_PhysicsProcess`.
- [x] **Follow and look.** The pivot follows the rendered carrier pose (Story 022) with a ≈ **0.08 s critically damped spring** (positions only; time from `IRenderClock`, never `delta`); camera rotation is **never smoothed** — the look yaw/pitch from the router is applied every rendered frame (look latency one frame); pitch is clamped −60°…+70° (Story 021 profile).
- [ ] **Profiles in engine.** With the default profile the trace (pivot, boom, shoulder, FOV) matches PCM-P9-1: pivot 1.6 m, boom 3.0 m, shoulder +0.6 m, vertical FOV 80°. Aim held: shoulder +0.75 m and boom 2.4 m eased in over 0.1 s, FOV unchanged; swimming: pivot 1.1 m and the camera never below the water plane; shoulder swap flips the offset sign. A camera pull-in never changes the aim vector (Throwing reads look yaw/pitch, not the camera position).
- [ ] **Boom collision in engine.** Walking the carrier toward a house wall pulls the camera in (via Story 021's solver) within 50 ms and it eases back out over ~400 ms when clear; the camera never clips below terrain or into a house box on the M-1a street.
- [ ] **Focus cone.** `FocusCone.CosMinPm(vFovDeg, aspect, focusConePm 900)` (computed on FOV or aspect change only) returns **635** at FOV 80 / 16:9, **526** at FOV 100, **752** at FOV 60, **549** at 21:9 (±1 for float rounding; round half away from zero), is pushed to the router/`InputFrame.FocusCosMinPm` as an integer, and changes only when FOV/aspect change (no per-frame recompute). The function is deterministic and covered by a Godot-free unit test (placed in `Client.Core` if its `BannedSymbols.txt` permits `double`, else in `src/Game` with a headless run).
- [ ] **PCM-P2-3 (camera half).** The camera never renders below the local carrier's latest-tick ground height + its radius clearance, in 600 frames of the golden script replayed through the view.
- [ ] **Allocation.** The rig allocates 0 managed bytes per frame in steady state (frame-allocation probe, advisory M-1a, per ADR-0019 pool/allocation rules).
- [ ] **Evidence.** `production/qa/evidence/player-camera-rig-evidence.md` records: screenshots (default behind-the-shoulder, pulled in against a wall, aim profile, swimming), the numeric trace above, the cone values, and a lead sign-off. If the dev machine cannot render screenshots under `godot-mono --headless`, run windowed or record the numeric trace plus `Viewport` texture captures; never mark the screenshot rows passed without images.

---

## Implementation Notes

*Derived from ADR-0019 §2–§3, ADR-0014 §2, ADR-0002 §3:*

- Files under `src/Game/Camera/`: `CameraRig.cs` (`Node3D`, `Initialize(deps)`, `Present(int alphaPermille)`), `FollowSpring.cs` (critically damped, semi-implicit, float; `dt` from `IRenderClock.RenderSimTimeMs` differences), `FocusCone.cs`. `CameraRig` is a child of `Main/World` in `Main.tscn`; the scene file itself is created by the godot-host-shell epic's scene story (`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`) — add the three nodes there or in a `CameraRig.tscn` instanced by it. Run **Project > Tools > Upgrade Project Files** policy per ADR-0019 §8 if the scene format needs it; commit `.uid` files.
- Camera yaw/pitch come from the router's look adapter (input-controls epic — `production/epics/input-controls/story-009-analog-input-deadzone-trigger-move-look.md`); until it exists, drive the rig from a debug mouse-look in the Debug subtree only.
- The aim state (profile blend) comes from `CarrierStatus.Aiming` (Story 013/020); swimming from `CarrierStatus.Swimming`.
- **Camera placement (Mail Lead note, 2026-11-01):** place `Camera3D` from `CameraPose.Compose`'s output, never from the solver's boom length alone. Per rendered frame: `pivot = CameraPose.Pivot(in feet, in profile)` on the spring-followed rendered feet, then `cam = CameraPose.Compose(world, in pivot, yawBam, pitchBam, shoulderMm, profile.BoomMm, smoothedBoomMm, swimming)` (`src/PerformativeMail.Client.Core/Camera/CameraPose.cs:68-69`; `smoothedBoomMm` from `BoomSmoother.Step`, seeded from a solved length, not 0 — HANDOFF 2026-10-29). `Compose` scales the full offset by `smoothedBoomMm / desiredBoomMm`, then clamps the result to `TerrainHeightMm(x, z) + FloorClearanceMm` (250 mm; `CameraPose.cs:24`, clamp at `:84-85`) and, while swimming, to the water plane (`:27`, `:86-89`). That clamp is the camera's only ground guarantee: TD-201 item 3 (Story 021 AC "Terrain floor", `production/epics/player-character/story-021-camera-boom-solver-config.md:41-42`) lets `BoomSolver.Solve` return its 300 mm floor below terrain + 250, so a rig that composes its own position from `Solve`'s length can clip into the ground and fail the "Boom collision" and PCM-P2-3 ACs above. Convert the mm vector to metres once at the `Camera3D` transform; look yaw/pitch is applied as rotation separately (never smoothed), and nothing read from Godot feeds back into `Compose`.
- Tell the router the cone: `IFocusConeSink.SetFocusCosMinPm(int)` — a one-method Client.Core interface the input-controls epic's router implements (cite: `production/epics/input-controls/story-009-analog-input-deadzone-trigger-move-look.md`, which declares `IFocusConeSink`). Until then the sink is a stub holding the value for tests.
- Test the rig with a scripted `ICarrierView` stub (no Sim) under `godot-mono --headless` for the numeric trace; screenshots need a render driver.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 021: the pure solver/profiles/smoother. Story 022: the carrier's rendered pose and `PlayerView`. Story 026: focus anchor projection.
- Dither fade, screen-door dither (M1), sprint FOV kick and landing dip (Accessibility), Mounted/knockdown cameras (M1 / M-1b), look sensitivity and invert (input-controls).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshots in `production/qa/evidence/` + sign-off in `production/qa/evidence/player-camera-rig-evidence.md`; plus a Godot-free unit test for `FocusCone.CosMinPm` (`tests/unit/player/FocusConeTests.cs`)

**Status**: [x] Created and passing — smoke `camera-rig` (PR #95 added the pitch clamp +70/−60 and level-right-axis checks), PR #89 (2026-11-09). `tests/unit/player/FocusConeTests.cs`, screenshots and the evidence file were not created (greybox; the cone is a fixed value).

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #89 (merge `c6a6b4b`), package WP-A (M-1a wave 1), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). Later: PR #95 — the boom treats the map sides as walls (TD-271 (6)); the `camera-rig` smoke checks the pitch clamp +70/−60 and a level right axis.
- **ACs met:** AC-1 (`World/CameraRig` is now `CameraRig`, injected, no `_PhysicsProcess`), AC-2 (0.08 s critically damped follow spring on the render clock; look unsmoothed with the pitch clamp).
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 simplify table; held by the package TD row):** AC-3 — default and aim profiles only; swimming and shoulder swap dropped → TD-213 (8). AC-4 — `BoomSolver` + `CameraPose.Compose` place the camera; the 50 ms / ~400 ms in-engine timing rows were not evidenced. AC-5 — focus cone fixed at 635 via `CameraRig.BindFocusConeSink(Action<int>)`; the cone table (526 / 752 / 549) and `FocusCone.CosMinPm` test dropped → TD-213 (8). AC-6 — 600-frame replay dropped. AC-7 — alloc probe dropped (advisory until M0, erratum E2). AC-8 — screenshots, trace and sign-off dropped → TD-213 (8).
- **Harden later:** input-controls 009's production binding of the focus-cone sink is not wired — only the `camera-rig` smoke binds it, so the router's `InputFrame.FocusCosMinPm` does not yet come from the rig.
- **Tests / smokes:** smoke `camera-rig`.

---

## Dependencies

- Depends on: Story 021 (solver/profiles), Story 020 (`ICarrierView`, `CarrierStatus`), Story 022 (rendered pose), `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (scene root), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`World` presenter / `Initialize(deps)`), `production/epics/input-controls/story-009-analog-input-deadzone-trigger-move-look.md` (look adapter, `IFocusConeSink` router cone push), `production/epics/simulation-core/story-024-framerunner.md` (`Present(alphaPermille)` call site)
- Unlocks: Story 026 (projection needs the camera); throwing-catching aim-camera feel; manual-delivery view tests
