# Story 021: Camera rig profiles and the boom solver against the Sim geometry (Godot-free, `Client.Core`)

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/player-character-movement.md` (P9 camera — Presentation only, never replicated; Acceptance PCM-P9-1; Game Feel look latency)
**Requirement**: `TR-movement-024` (camera boom solved in C# against the Sim height field / AABBs — M-1a minimal boom; **dither fade is M1**; the registry shows a coverage gap because ADR-0029 is planned, not written)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance id: PCM-P9-1.)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§2 scene tree: `CameraRig → Boom → Camera3D` as a `World` child; "No Presentation value (rig offset, camera, animation time) may feed the Sim") — Accepted 2026-10-16 (primary); ADR-0013 (§6 `Sim/Geometry` primitives and the "presentation colliders" parity risk) and ADR-0016 (§6 `IRenderClock`) secondary — Accepted. **ADR-0029 (camera boom, animation LOD) is planned and not written; this story's solver is the M-1a minimum and feeds it.**
**ADR Decision Summary**: The scene tree places the camera rig under `World`. The boom is solved in C# against the same height field and construct AABBs the Sim uses — no terrain colliders in Godot, and no second geometry copy: Presentation colliders that copy `IWorldQuery` geometry drift, so the boom queries `IWorldQuery` through the shared `Sim/Geometry` primitives (ADR-0013 §6). Presentation time comes from `IRenderClock`, never `delta`.
**ADR Version**: ADR-0019 2026-10-16; ADR-0013 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (this story is Godot-free, in `PerformativeMail.Client.Core`) | **Risk**: LOW (pure maths). The Godot-facing node is Story 023 (MEDIUM).
**Engine Notes**: `Camera3D.Fov` semantics (vertical by default via `KeepAspect = Height`) are VERIFY-IN-ENGINE in Story 023; this story only emits integer degrees.

**Control Manifest Rules (Presentation layer)**:
- Required: presentation colliders (camera boom, picking) are not a second copy — they read `IWorldQuery`/`StaticSolids`; if any Godot-side mesh copy exists it needs a parity test against `StaticSolids` (ADR-0013 Risks)
- Required: presentation time comes from `IRenderClock`; `delta` and `Time.GetTicksMsec` are banned for gameplay-facing timing (ADR-0016)
- Forbidden: feeding any Presentation value (camera, rig offset) into the Sim; reimplementing a Sim rule in Client.Core (ADR-0019, ADR-0016)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P9-1, P9), scoped to this story:*

- [ ] **PCM-P9-1 (profiles).** Default settings give pivot **1600** mm above the feet, boom **3000** mm, shoulder **+600** mm (swappable: the sign flips on a shoulder-swap request), pitch clamped **−60°…+70°**, vertical FOV **80°** (59 → 60, 101 → 100, 80 stays 80); aim held: shoulder **+750**, boom **2400**, look sensitivity multiplier **600 ‰** (`aimCamMult`), FOV unchanged by default (the optional −10° is a setting, off); swimming: pivot **1100** (lowered 0.5 m) and the camera's world `y ≥ 0` (never below the water plane). Profile blends (aim ease-in over 0.1 s) are a pure function of elapsed ms.
- [ ] **Boom solve.** `BoomSolver.Solve(IWorldQuery, pivot, yaw/pitch (Q15 direction), shoulderMm, desiredBoomMm, radiusMm = 250)` returns the longest safe boom length ≥ a floor of 300 mm: a static box between pivot and camera shortens the boom to just in front of it (box inflated by the 250 mm radius, `SegmentIntervals.Slab`); a terrain ridge shortens it (terrain raised by the radius via `HeightfieldMarch`); a clear path returns the full boom; a pivot inside a box returns the floor length, never NaN/negative; players, items and props never influence the result (the solver takes no such input).
- [ ] **Smoothing (integer, time-based).** `BoomSmoother.Step(currentMm, targetMm, dtMs)` pulls in to a shorter target within **50 ms** (reaches ≤ target after 50 ms of continuous obstruction) and eases out to a longer target over **400 ms** (within 5 % of the target after 400 ms, monotonic, no overshoot); `dtMs` is a difference of `IRenderClock.RenderSimTimeMs`, and a zero or negative dt changes nothing.
- [ ] **Terrain floor.** The final camera position is never below `TerrainHeightMm(x, z) + 250` nor, while swimming, below `y = 0` (checked over a sweep of 2 000 random pivots/yaws/pitches on the M-1a street fixture and FX-Beach).
- [ ] **Parity.** The solid set the boom tests for a region equals `IWorldQuery.QueryStaticSolids` for that region (random-region property test); the solver holds no cached geometry of its own.
- [ ] **Determinism/allocation.** The solver and smoother use only integer maths (`checked` arithmetic per ADR-0001), allocate 0 B over 10 000 calls, and are pure functions of their arguments.
- [ ] **Not in scope check.** The code contains no dither fade, no player/prop/item colliders and no sprint-FOV kick/landing dip (those are M1/Accessibility); a code-review checklist item.

---

## Implementation Notes

*Derived from GDD P9 and ADR-0013 §6, ADR-0019 §2, ADR-0016 §6:*

- Files in `src/PerformativeMail.Client.Core/Camera/` (the assembly is created by `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md`): `CameraProfile.cs` (`Default`, `Aim`, `Swim`, readonly structs of ints), `CameraSettings.cs` (FOV clamp 60–100, shoulder side), `BoomSolver.cs`, `BoomSmoother.cs`, `CameraBlend.cs`.
- The boom is a swept sphere approximated as: segment from pivot to desired camera position tested against each `StaticSolid` AABB inflated by `radius` (`SegmentIntervals.Slab`, ascending index, ≤ 64 candidates from `QueryStaticSolids` over the segment AABB) and against the height field with the segment lowered by `radius` (`HeightfieldMarch.FirstContact`); take the minimum `f` in ‰, `boom = max(floor, desired × f / 1000)` with `Fix.MulDiv`-style integer rounding. Client.Core may reference `PerformativeMail.Sim` (ADR-0001), so reuse Sim `Fix` and `Geometry`.
- Direction from yaw/pitch: horizontal back vector `(sin yaw, cos yaw)` in Q15 (ADR-0013 §3 axis convention) times `cos(pitch)`, vertical `−sin(pitch)`; pitch in degrees × 100 index → use a small Q15 sine lookup built from `TrigQ15` by degree (do not add a second table: convert degrees to BAM and call `TrigQ15.Sin`). Shoulder offset is applied along the camera's right vector.
- The 0.08 s critically-damped *follow spring* and the rotation (never smoothed) live in the Godot node (Story 023): they are float Presentation maths and never reach the Sim.
- Look/aim/shoulder-swap inputs arrive from the router (input-controls epic: `LookState`, `SetLook`, shoulder-swap tap); this story accepts them as plain arguments.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 023: the `CameraRig` node, follow spring, FOV/`FocusCosMinPm`, applying the result to `Camera3D`.
- Dither fade of the carrier and screen-door dither of houses (M1, technical-artist; VERIFY-IN-ENGINE). Mounted per-vehicle orbit profiles (Vehicles, M1). Knocked-down orbit camera (M-1b, P16). Comfort options (Accessibility epic).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/CameraBoomTests.cs` (`[Trait("Id", "PCM-P9-1")]`) — must exist and pass on net10.0 and net8.0 (the test project must reference `PerformativeMail.Client.Core`; ADR-0001 §3 lists it)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (`SegmentIntervals`, `HeightfieldMarch`), `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`), `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`TerrainHeightMm`), `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` (Client.Core assembly), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`TrigQ15`)
- Unlocks: Story 023 (camera rig node); throwing-catching aim-camera values reuse `CameraProfile.Aim`
