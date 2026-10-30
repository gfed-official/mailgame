# Story 021: Camera rig profiles and the boom solver against the Sim geometry (Godot-free, `Client.Core`)

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Presentation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-29 — Complete (branch `story/player-character-021`, Mail Dev): all six ACs met, `tests/unit/player/CameraBoomTests.cs`; eight as-built judgment calls batched in TD-201 (Mail Dev defaults, approved by Evan 2026-10-29 11:10 PM PT: items 1–8 as listed in message t205s987, card t205s988; TD-201 Resolved). Before that: readiness pass (Mail Lead): yaw/pitch are BAM; terrain-floor, allocation and parity ACs rewritten to one test each; review-only clauses moved to a PR checklist; planning defaults batched in TD-198, approved by Evan 2026-10-29 7:54 PM PT.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P9 camera — Presentation only, never replicated; Acceptance PCM-P9-1; Game Feel look latency)
**Requirement**: `TR-movement-024` (camera boom solved in C# against the Sim height field / AABBs — M-1a minimal boom; **dither fade is M1**; the registry shows a coverage gap because ADR-0029 is planned, not written)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance id: PCM-P9-1.)*

ADR-0029 (camera boom, animation LOD) is planned and not written; this story's solver is the M-1a minimum and feeds it.

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§2 scene tree: `CameraRig → Boom → Camera3D` as a `World` child; "No Presentation value (rig offset, camera, animation time) may feed the Sim") — Accepted 2026-10-16 (primary); ADR-0013 (§6 `Sim/Geometry` primitives; the presentation-collider parity risk, Consequences l.234 and Risks l.285) and ADR-0016 (§6 `IRenderClock`) secondary — Accepted.
**ADR Decision Summary**: The scene tree places the camera rig under `World`. The boom is solved in C# against the same height field and construct AABBs the Sim uses — no terrain colliders in Godot, and no second geometry copy: Presentation colliders that copy `IWorldQuery` geometry drift, so the boom queries `IWorldQuery` through the shared `Sim/Geometry` primitives (ADR-0013 §6). Presentation time comes from `IRenderClock`, never `delta`.
**ADR Version**: ADR-0019 2026-10-16; ADR-0013 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (this story is Godot-free, in `PerformativeMail.Client.Core`) | **Risk**: LOW (pure maths). The Godot-facing node is Story 023 (MEDIUM).
**Engine Notes**: `Camera3D.Fov` semantics (vertical by default via `KeepAspect = Height`) are VERIFY-IN-ENGINE in Story 023; this story only emits integer degrees.
**Performance**: no impact expected; one `Solve` per rendered frame, ≤ 64 slab tests and ≤ 14 height samples (`HeightfieldMarch.cs:68-75`: the start sample plus `SampleCount` = 13 intervals of ≤ 250 mm over the 3000 mm boom with the 600 mm shoulder), 0 B.

**Control Manifest Rules (Presentation layer)**:
- Required: presentation colliders (camera boom, picking) that copy `IWorldQuery` geometry need a parity test against `StaticSolids` (ADR-0013 Risks l.285, assigned to ADR-0019's scope). This solver keeps no copy: it queries `IWorldQuery` per call, so the copy-parity test belongs to the Godot view that holds a copy (Story 023 / terrain view).
- Required: presentation time comes from `IRenderClock`; `delta` and `Time.GetTicksMsec` are banned for gameplay-facing timing (ADR-0016)
- Forbidden: feeding any Presentation value (camera, rig offset) into the Sim; reimplementing a Sim rule in Client.Core (ADR-0019, ADR-0016)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P9-1, P9), scoped to this story. Values marked "TD-198 item n" are Mail Lead planning defaults, approved by Evan 2026-10-29 7:54 PM PT (card 'PR 68: approve the eight items listed above (TD-198 items 1–6 for player-character 021, TD-199 items 1–2 for throwing-catching 003)?', t197s951, after message t197s950 (7:51 PM PT) listed all eight items in the register's plain wording just before the card: "Approve TD-198 items 1-6 for player-character 021 and TD-199 items 1-2 for throwing-catching 003, as listed in your message above"; `docs/tech-debt-register.md` TD-198).*

- [x] **PCM-P9-1 (profiles).** Default settings give pivot **1600** mm above the feet, boom **3000** mm, shoulder **+600** mm (swappable: the sign flips on a shoulder-swap request), pitch clamped **−60°…+70°** (−10 923…+12 743 BAM, `CameraSettings.ClampPitch(short)`), vertical FOV **80°** (59 → 60, 101 → 100, 80 stays 80); aim held: shoulder **+750**, boom **2400**, look sensitivity multiplier **600 ‰** (`aimCamMult`), FOV unchanged by default (the optional −10° is a setting, off); swimming: pivot **1100** (lowered 0.5 m) and the camera's world `y ≥ 0` (never below the water plane). The aim blend is linear in elapsed ms over 100 ms: 0 ms → Default, 50 ms → shoulder 675 / boom 2700, ≥ 100 ms → Aim (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 5)).
- [x] **Boom solve.** `BoomSolver.Solve(IWorldQuery w, in Vec3Mm pivot, ushort yawBam, short pitchBam, int shoulderMm, int desiredBoomMm, int radiusMm = 250)` returns the longest safe boom length ≥ a floor of 300 mm (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 1)): a static box between pivot and camera shortens the boom to just in front of it (box inflated by the 250 mm radius, `SegmentIntervals.Slab`; a box whose inflated face lies 2000 mm along a 3000 mm desired boom gives 2000 ± 3 mm, permille resolution of the segment fraction); a terrain ridge shortens it (segment lowered by the radius via `HeightfieldMarch.FirstContact`); a clear path returns the full boom; a pivot inside a box returns the floor length, never negative; players, items and props never influence the result (the solver takes no such input). One parameterised test.
- [x] **Smoothing (integer, time-based).** `BoomSmoother.Step(currentMm, targetMm, dtMs)` pulls in linearly to a shorter target within **50 ms** (≤ target after 50 ms of continuous obstruction) and eases out linearly to a longer target over **400 ms** (within 5 % of the target after 400 ms, monotonic, no overshoot) (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 4)); `dtMs` is a difference of `IRenderClock.RenderSimTimeMs`, and a zero or negative dt changes nothing.
- [x] **Terrain floor.** `BoomSolver.Solve` never returns a length that puts the camera below `TerrainHeightMm(x, z) + 250`, and `CameraPose.Compose` clamps the composed (smoothed) position to that floor and to `y ≥ 0` while swimming (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 4)). One parameterised test with three rows: the M-1a street beside a house wall (pivot 1600 above ground, yaw facing the house, world built with `M1aStreet.Author` as in `tests/unit/world/M1aStreetTests.cs:16-21`), the FX-Beach slope looking downhill (`WorldTablesFixture.Beach()`), and a swimmer at pivot 1100 with pitch +70° (expected `y = 0`).
- [x] **Solid set is the world's.** With a stub `IWorldQuery` whose `QueryStaticSolids` returns one box across the boom, `Solve` shortens the boom; with the same stub returning no index for the same pivot, `Solve` returns the full boom.
- [x] **Determinism/allocation (advisory until M0, erratum E2).** `Solve` and `Step` use only integer maths (no float: PMSIM001 is on for Client.Core; `checked` arithmetic per ADR-0001); one `AllocAssert.Zero` window over one `Solve` on the street plus one `Step` (`tests/PerformativeMail.Sim.Tests/AllocAssert.cs:61`, in a `NonParallelCollection` class) reports 0 B.

---

## Implementation Notes

*Derived from GDD P9 and ADR-0013 §6, ADR-0019 §2, ADR-0016 §6:*

- Files in `src/PerformativeMail.Client.Core/Camera/` (the assembly is created by `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md`): `CameraProfile.cs` (`Default`, `Aim`, `Swim`, readonly structs of ints), `CameraSettings.cs` (FOV clamp 60–100, shoulder side, `ClampPitch`), `BoomSolver.cs`, `BoomSmoother.cs`, `CameraBlend.cs`, `CameraPose.cs` (`Compose`: pivot + direction × smoothed boom + shoulder, then the hard floor clamp).
- The boom is a swept sphere approximated as (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 2)): the segment from pivot to desired camera position tested against each `StaticSolid` AABB inflated by `radius` (`SegmentIntervals.Slab`, ascending index) and against the height field with the segment lowered by `radius` (`HeightfieldMarch.FirstContact`); take the smallest `f` in ‰, `boom = max(floor, Fix.MulDiv(desired, f, 1000, ref sink))` (a local `SimCounterSink`; `Fix.cs:38`); a shortened boom scales the whole pivot-to-camera vector, shoulder included. Candidates come from `QueryStaticSolids` over the segment's AABB into a 64-entry buffer; on overflow the lowest-index hits are kept; Client.Core adds no counter of its own, and the world query counts the overflow in `WorldSolidQueryOverflow` (26) (`IWorldQuery.cs:86`, `WorldTables.cs:372`) (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 3)). Client.Core may reference `PerformativeMail.Sim` (ADR-0001), so reuse Sim `Fix` and `Geometry`.
- Direction from BAM: back = `(TrigQ15.Sin(yaw), TrigQ15.Cos(yaw)) × TrigQ15.Cos(pitch)` in x/z, vertical `−TrigQ15.Sin(pitch)` (pitch passed as `(ushort)pitchBam`; `TrigQ15.Sin/Cos` take `ushort`); right = `(Cos yaw, −Sin yaw)` (ADR-0013 §3 axis convention). No degree table and no second lookup. Shoulder offset is applied along the right vector.
- The 0.08 s critically-damped *follow spring* and the rotation (never smoothed) live in the Godot node (Story 023): they are float Presentation maths and never reach the Sim.
- Look/aim/shoulder-swap inputs arrive from the router (input-controls epic: `LookState`, `SetLook`, shoulder-swap tap); `InputFrame.LookYaw` is `ushort` BAM and `LookPitch` signed BAM `short` (`src/PerformativeMail.Sim/ClientLayer/InputFrame.cs:45,48`); this story accepts them as plain arguments.
- Parity reading (Mail Lead planning default, approved by Evan 2026-10-29 7:54 PM PT (TD-198 item 6)): the solver keeps no copy of the geometry, so the manifest's copy-parity test stays with the Godot view that holds a copy (Story 023); this story shows the solver reads the world with the stub-world AC.
- Registry trace (advisory): `docs/architecture/tr-registry.yaml:2134` has `stories: []` for TR-movement-024; add 021 and 023 at the next `/architecture-review`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 023: the `CameraRig` node, follow spring, FOV/`FocusCosMinPm`, applying the result to `Camera3D`, and any copy-parity test.
- Dither fade of the carrier and screen-door dither of houses (M1, technical-artist; VERIFY-IN-ENGINE). Mounted per-vehicle orbit profiles (Vehicles, M1). Knocked-down orbit camera (M-1b, P16). Comfort options (Accessibility epic).
- **Review checklist (PR body, not tests):** no dither fade, no player/prop/item colliders, no sprint-FOV kick or landing dip; `BoomSolver` and `BoomSmoother` hold no fields and read no clock; no float.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-29: GAPS, folded into the ACs above.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/CameraBoomTests.cs` (`[Trait("Id", "PCM-P9-1")]`) — must exist and pass on net10.0 and net8.0 (the test project must reference `PerformativeMail.Client.Core`; ADR-0001 §3 lists it)

**Status**: [x] Created and passing — 2026-10-29, branch `story/player-character-021` (main 099637a): `CameraBoomTests` (5 methods, 10 cases) and `CameraBoomAllocationTests` (1). Full suite, every leg 5,777 total, 0 failed: net10.0 Debug 5,712 passed / 65 skipped; net10.0 Release 5,770 / 7; net8.0 Debug 5,712 / 65; net8.0 Release 5,770 / 7 (the allocation window skips on Debug builds, TD-008, and reports 0 B on Release).

---

## Dependencies

- Depends on: Story 004 (`SegmentIntervals`, `HeightfieldMarch`), `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`), `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`TerrainHeightMm`), `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` (Client.Core assembly), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`TrigQ15`) — all Complete on main.
- Unlocks: Story 023 (camera rig node); throwing-catching aim-camera values reuse `CameraProfile.Aim`

---

## Completion Notes

**Completed**: 2026-10-29 (branch `story/player-character-021`, Mail Dev; `/dev-story` on Opus, `/code-review` on Sonnet: no required changes, suggestions S1 and S2 applied)
**Criteria**: 6/6 met.
- Profiles: `ProfilesSettingsAndAimBlend_GiveTheP9Values`.
- Boom solve: `Solve_Scenario_ReturnsLongestSafeBoom` (rows box 2000 ± 3, ridge, clear, pivot inside a box → 300).
- Smoothing: `Step_PullsInWithin50Ms_EasesOutOver400Ms_IgnoresNonPositiveDt`.
- Terrain floor: `SolveAndCompose_AtPitchPlus70_KeepCameraAboveTerrainFloorAndWater` (street, beach, swimmer; the street and beach rows hand `Compose` the unsmoothed full boom, so they reach its clamp — shown failing with the clamp removed).
- Solid set is the world's: `Solve_UsesTheWorldQuerysSolidSet`.
- Allocation (advisory, E2): `CameraBoomAllocationTests.SolveOnStreetPlusStep_AllocatesZeroBytes` (0 B on Release; skips on Debug, TD-008).
**Bug fixed in review**: with the optional aim FOV cut on, FOV 60 gave 50; `FovDeg` now re-clamps to 60–100 (one assertion in the profiles test, seen failing first).
**Deviations**: eight as-built judgment calls, labelled in code and batched in **TD-201** (Mail Dev defaults, approved by Evan 2026-10-29 11:10 PM PT, items 1–8 as listed in message t205s987, card t205s988; TD-201 Resolved): terrain hit one permille early; an over-long segment falls back to the 300 mm floor; the 300 mm floor wins over terrain + 250 inside `Solve` (the `Compose` clamp is the hard guarantee; a wall closer than 300 mm is not covered); fixed smoothing rates from the 3000 mm span; a mutable `CameraSettings` class; the aim FOV cut re-clamped; unstated profile values (look 1000 ‰ outside aim, Swim = Default with pivot 1100); the aim blend also blends pivot and look multiplier.
**For story 023**: call `Solve` on the thread that ticks the Sim, between ticks (the world query writes Sim counters); seed the smoothed boom from a solved length, not 0.
