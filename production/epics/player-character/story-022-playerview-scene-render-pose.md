# Story 022: `PlayerView` greybox scene and the local carrier's render pose (≤ 1-tick extrapolation, clamped to the swept result)

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M (~1 day; ~400 lines prod + tests + evidence)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/player-character-movement.md` (P2 "The Presentation renders the local carrier from the latest tick, extrapolated horizontally only … for at most one tick and clamped to that tick's swept result"; PCM-P2-3; Visual/Audio "The carrier is a capsule 1.4 m, hat first"; Game Feel)
**Requirement**: `TR-movement-007` (fixed-step driver with catch-up cap; local extrapolation ≤ 1 tick; no engine physics interpolation — the presentation half), `TR-movement-025` (greybox rig scene shape; the AnimationTree itself is Story 024; registry coverage gap — ADR-0029 planned, not written)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P2-3, PCM-FEEL-2 (advisory).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§2 `World/Entities` hosts `PlayerView` via `EntityViewRegistry`; §6 `PlayerView/RigOffset` carries the visual-only lunge; views animate only from data `Present` hands them) — Accepted 2026-10-16 (primary); ADR-0002 (§3 own-carrier smoothing ≤ 1 tick using `AlphaPermille`), ADR-0013 (§3 axis convention: Presentation sets `Rotation.Y = θ·2π/65536` with no sign flip; engine notes 1–2) secondary — Accepted
**ADR Decision Summary**: `PlayerView` is a `Node3D` view per player entity, looked up through `EntityViewRegistry` (`Dictionary<EntityId, Node3D>` preallocated to the cap, `IsInstanceValid` guarded). `Present(alphaPermille)` hands views their data; nothing moves in `_PhysicsProcess`; engine physics interpolation is off. `PlayerView/RigOffset` is a child `Node3D` that carries the visual-only lunge (≤ 0.8 m local offset) and nothing reads it back. BAM yaw maps to `Rotation.Y` with no sign flip; presentation smooths body yaw by shortest arc.
**ADR Version**: ADR-0019 2026-10-16; ADR-0002 2026-10-16; ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`) | **Risk**: MEDIUM
**Engine Notes**: `MeshInstance3D.skeleton` must be set explicitly on any skinned mesh (the 4.6 default changed to `""` — matters from Story 024); scenes are saved in the 4.6+ format (no `load_steps`, unique node ids) — create `PlayerView.tscn` in the editor (or via a scripted `--headless` save) and commit its `.uid`; run Upgrade Project Files only in a dedicated bulk change (ADR-0019 §8). Verify `Node3D.Rotation.Y = +90°` turns −Z forward toward −X (the ADR-0013 convention smoke test below).

**Control Manifest Rules (Presentation layer)**:
- Required: remote/own-carrier smoothing interpolates or extrapolates ≤ 1 tick using `AlphaPermille / 1000f` (float allowed in Presentation only) (ADR-0002 §1, §3)
- Required: Godot views map `EntityId → node` through a C# `Dictionary` owned by Presentation; id structs travel no further than Presentation's C# layer; pass `.Value` to Godot (ADR-0004)
- Forbidden: moving Sim-driven entities in `_PhysicsProcess`; Godot physics interpolation; any physics body/`CharacterBody3D` for the carrier; feeding rig offset, camera or animation time into the Sim (ADR-0002, ADR-0013, ADR-0019)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (P2, PCM-P2-3, P1), scoped to this story:*

- [ ] **Scene shape.** `PlayerView.tscn` = `PlayerView (Node3D) → RigOffset (Node3D) → { Body (greybox capsule radius 0.3 m, height 1.4 m, feet at local origin), Hat (box on top — "hat first"), Facing (a nose/wedge showing body yaw), HeldItemAnchor (Marker3D), HatAnchor }`; no collision shapes, no physics bodies, no scripts with `_PhysicsProcess`. The view is created for the local carrier when `ILocalPlayer.IsKnown` (M-1a solo), registered in `EntityViewRegistry` under the player `EntityId`, and removed on despawn; every registry lookup is `IsInstanceValid`-guarded.
- [ ] **Render pose rule (pure, Godot-free, `Client.Core`).** `CarrierRenderPose.Compute(prev, curr, predictedNext, alphaPermille, IWorldQuery)` returns the rendered feet position and yaw: horizontal `xz = curr + (predictedNext − curr) × alphaPermille / 1000` (extrapolation toward the **same-input one-tick probe**, so it is clamped to that tick's swept result — never into a wall); vertical `y = curr.Y` (no vertical extrapolation) raised to `TerrainHeightMm(x, z)` if it would be below; `alphaPermille` 0 returns `curr` exactly; a paused driver (alpha frozen at 0) shows `curr`; `predictedNext` is computed **once per new tick** by running `OwnedCarrier.Step` on a *copy* of the state with `ICarrierView.LastInput` (edges cleared) and a throwaway `CarrierScratch` — it never mutates the real state and never counts in `SimCounters`.
- [ ] **PCM-P2-3.** Over 600 frames of the golden script (Story 010) replayed at random `alphaPermille`, the rendered pose is never below the latest tick's ground height, never farther from `curr` than `predictedNext` is along each horizontal axis, and with the carrier pressed against a wall (blocked) the rendered position equals `curr` for every alpha; free walking at 5000 mm/s at alpha 500 renders 83 ± 1 mm ahead of `curr`.
- [ ] **Facing and conventions.** `Rotation.Y = bam × 2π / 65536` with no sign flip: BAM 16384 makes the node's −Z forward point along −X, BAM 0 along −Z (one view smoke test, ADR-0019 / ADR-0013 §Related); body-yaw display smoothing takes the shortest signed arc (wrap BAM deltas as `short`); camera yaw → BAM uses rounding and an unchecked `& 0xFFFF` (negative yaws wrap) — each covered by a pure unit test of the conversion helpers.
- [ ] **Driven only by `Present`.** The view moves only inside `Present(alphaPermille)` (or a `_Process` fed from its data); `_PhysicsProcess` is not overridden; `physics/common/physics_interpolation` is false in `project.godot`; the node reads `ICarrierView` and writes nothing back (a code-review checklist item and an architecture test: `src/Game/Views/Player/**` has no reference to `SimWorld`/`ClientLayer`).
- [ ] **Units.** Sim mm → Godot metres by `mm / 1000f` at the view boundary only; Sim +Y up equals Godot +Y up; `EntityId` crosses into Godot only as `.Value`.
- [ ] **Evidence (Visual/Feel).** `production/qa/evidence/player-view-render-pose-evidence.md` with a screenshot of the greybox carrier from behind on the M-1a street, a short recorded trace of rendered vs tick position at 30/60/144 Hz frame pacing (no backward steps, no wall penetration), and a lead sign-off; PCM-FEEL-2 (input → first moved frame ≤ 50 ms at 60 fps) is measured and logged **advisory**.

---

## Implementation Notes

*Derived from ADR-0019 §2/§6, ADR-0002 §3, GDD P2:*

- Files: `src/PerformativeMail.Client.Core/Presentation/CarrierRenderPose.cs` + `YawMath.cs` (pure, integer mm; shortest-arc helper); `src/Game/Views/Player/PlayerView.tscn`, `PlayerView.cs` (`Initialize(deps)` — no `_Ready` lookups), `PlayerPresenter.cs` (owned by `WorldPresenter`; creates/removes the view, calls `Present`).
- **Spec ambiguity resolved here (logged):** the GDD says *extrapolate horizontally ≤ 1 tick, clamped to that tick's swept result*; ADR-0016 §6's timeline table says "present, ≤ 1-tick interpolation by `AlphaPermille`". Both are satisfied by blending toward the one-tick probe: it is ≤ 1 tick, alpha-driven, and clamped by construction. The probe runs the Sim's own solver on a copy — it does not reimplement any rule in Presentation (manifest: "never reimplement a Sim rule").
- The probe is per *tick*, cached in the presenter keyed by the carrier's tick; at 144 Hz it is computed once per 4–5 frames. Its cost is ≤ one `CarrierStep` (budget note in Story 020's perf test).
- Greybox only: no art assets, no AnimationTree here (Story 024 attaches the skeleton/rig under `RigOffset`). The `RigOffset` node exists so Manual Delivery's lunge can offset it later — this story must not write to it.
- `EntityViewRegistry`, `PresentationRoot.Present` and the `World/Entities` node come from the godot-host-shell epic (`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` for `World/Entities`, `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` for `PresentationRoot.Present`, `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md` for `EntityViewRegistry`); until they exist, this story's tests use a stub presenter in a throwaway test scene.
- Run with `godot-mono --headless --path . -- …` for scripted checks (no rendering); screenshots need a windowed run.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 024: skeleton, AnimationTree, carry pose, weight blend. Story 025: idle doze. Story 023: the camera that follows this pose.
- Remote players' interpolation (160 ms delay, ADR-0020, M0); Teleport snaps; knockdown/clock-in presentation (M-1b); MailboxView/lunge (Manual Delivery epic).
- Terrain and house views (godot-host-shell greybox scene).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**:
- Visual/Feel: retained screenshot(s) in `production/qa/evidence/` + sign-off in `production/qa/evidence/player-view-render-pose-evidence.md`; plus `tests/unit/player/CarrierRenderPoseTests.cs` (`[Trait("Id", "PCM-P2-3")]`, conversion helpers) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 020 (`ICarrierView`, `LastInput`), Stories 005–010 (the probe uses the locked solver), `production/epics/simulation-core/story-024-framerunner.md` (`Present(alpha)`), `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (scene root / `World/Entities`), `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md` (`EntityViewRegistry`), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`Initialize(deps)`)
- Unlocks: Stories 023 (camera follows the rendered pose), 024, 025, 026; manual-delivery epic's PlayerView lunge/reach layers (`RigOffset`)
