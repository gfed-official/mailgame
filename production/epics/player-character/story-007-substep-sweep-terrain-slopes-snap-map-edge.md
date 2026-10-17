# Story 007: Sub-step sweep over the height field — slopes, snap-down and the map-edge clamp

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P4 ground, slopes, steps and colliders — terrain half; Edge Cases "Zero, max and out-of-range")
**Requirement**: `TR-movement-006` (sub-steps ≤ 300 mm, ≤ 3 slides, step-up / snap 400 mm, slope 700 ‰, ≤ 4 sub-steps per tick), `TR-movement-005` (feet-point sample of the bilinear height; analytic gradient), `TR-movement-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P4-1a, P4-2b, PCM-PERF-1 probe counters.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§4 collision model and the sub-step sweep) — Accepted 2026-10-16; ADR-0026 (§2, §4 `ClampInsideMap`, `TerrainHeightMm`) secondary
**ADR Decision Summary**: Displacement `D` (own + support + vertical) is split into `n = clamp(CeilDiv(max(|Dx|, |Dz|, |Dy|), 300), 1, 4)` sub-steps; a displacement over 1200 mm is clamped and counted `carrierStepClamped`. Slope is the bilinear patch's analytic gradient at the feet point, walkable iff `1000²·(Gx² + Gz²) ≤ slopeMaxPm²·S⁴` evaluated in `Int128` on the un-divided numerators, so 700 ‰ walks and 702 ‰ does not. Steeper terrain is a wall: motion that would raise the feet onto it is resolved as a slide along the contour, and gravity applies. After the sweep the position is clamped 1000 mm inside the map and the outward velocity component zeroed.
**ADR Version**: ADR-0013 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: `Int128` is available on net8.0 and deterministic but software-implemented; its cost is measured under PCM-PERF-1 and it never appears in stored state (ADR-0013 engine note 4).

**Control Manifest Rules (Feature layer)**:
- Required: sub-steps ≤ 300 mm and ≤ 4 per tick, ≤ 3 slides per sub-step, both counted; candidate buffers fixed-capacity in `CarrierScratch` (ADR-0013)
- Required: height comes from `WorldHeight` only; slope tests compare un-divided numerators in `Int128`, never the rounded permille (ADR-0013 Guidelines, ADR-0026 §2)
- Forbidden: Godot physics, Jolt queries, continuous TOI solvers (ADR-0013 Alternatives 1–2)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md`, scoped to this story (terrain only — boxes are story 008):*

- [x] **PCM-P4-1a.** FX-Ramp rises of 1400 mm and 1404 mm over 2000 mm (700 ‰ / 702 ‰): walking straight up the 1400 mm ramp ascends it to the top; walking straight up the 1404 mm ramp never raises the feet above where the steep terrain begins (the carrier is held at the contour / slides along it), and standing on the 1404 mm ramp with no input the feet move monotonically downhill (terrain height non-increasing) until walkable terrain or the map edge is reached. Walkability is decided on exact numerators (a 1401 mm ramp passes the `Int128` test only if `1000²·g² ≤ 700²·S⁴`; add the 1400/1401 boundary vector).
- [x] **Sub-step split.** Displacements of 1 mm, 300 mm, 301 mm, 600 mm, 650 mm, 1199 mm, 1200 mm produce 1, 1, 2, 2, 3, 4, 4 sub-steps; a 1201 mm (or larger) displacement is clamped to 1200 mm, uses 4 sub-steps and increments `StepClamped`; the per-tick maximum of sub-steps never exceeds 4 and slide iterations never exceed 3 per sub-step over a 10 000-tick fuzz (assert via the `CarrierScratch` counters).
- [x] **Terrain follow.** A grounded carrier walking over FX-Ramp (≤ 700 ‰) keeps `Y = TerrainHeightMm(X, Z)` at every sub-step end; a grounded carrier crossing a ridge/dip never floats more than the 400 mm snap above terrain: a terrain drop ≤ 400 mm below the feet in one sub-step stays Grounded (terrain snap), a steeper fall goes Airborne with `CoyoteLeft = coyoteTicks`.
- [x] **PCM-P4-2b.** at the map edge the carrier comes to rest ≥ 1000 mm inside the map rectangle (`ClampInsideMap(p, 1000)`) with the outward velocity component zeroed (the inward and tangential components are kept: sliding along the edge continues); height lookups clamp their corner indices (no exception at the last tile).
- [x] **Feet-point rule.** The capsule radius plays no part against terrain: a carrier whose 300 mm radius overlaps a steeper tile but whose feet point is on a walkable tile is not blocked.
- [x] The terrain sweep allocates 0 B and its worst case (4 sub-steps) stays inside the PCM-PERF-1 per-tick counters on the FX-Ramp fixture.

---

## Implementation Notes

*Derived from ADR-0013 §4 and the Implementation Guidelines:*

- Files in `src/PerformativeMail.Sim/Players/`: `CarrierSweep.cs` (sub-step loop: move horizontally → [candidate resolution: story 008] → vertical move and ground resolution → snap-down), `CarrierTerrain.cs` (feet-point sample, gradient test), `CarrierBounds.cs` (map clamp).
- Per sub-step: `Dsub = D / n` using the same floor/remainder discipline as story 005 so the sub-steps sum exactly to the tick displacement (no drift; assert `Σ sub-steps == D`). Do not re-quantise the 1/30 remainder per sub-step.
- Slope: `WorldHeight.Gradient(corners, fx, fz, out gx, out gz)` (un-divided numerators) then `WorldHeight.IsWalkableSlope(gx, gz, slopeMaxPm)` from world-stub story 001 — both already exist; do not re-implement. `slopeMaxPm` is 700 from `PlayerSettings`.
- Wall-slide on steep terrain: project the horizontal displacement onto the contour (perpendicular to the gradient) when the move would raise the feet onto non-walkable terrain; count it as a slide iteration (still ≤ 3 per sub-step). Gravity applies while on non-walkable terrain.
- Map clamp uses `IWorldQuery.ClampInsideMap(p, 1000)` and zeroes only the outward velocity component (compare each axis against the bound).
- Snap-down / `stepUpMm` constants are 400 mm from settings; this story implements the terrain case, story 008 the box case — structure the code so both use one `GroundResolve` routine returning the support height.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: static AABBs, blocked tiles, step-up onto box tops, ceilings, depenetration, box tunnelling tests (PCM-P4-1b/1c/2a/2c/2d).
- Story 009: water state applied "from the next tick" and haul-out.
- Vehicle OBBs and support carry (P7) and player push (P8): out of M-1a scope; their sources stay empty.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/SweepTerrainTests.cs` (`[Trait("Id", "PCM-P4-1a")]`, `PCM-P4-2b`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/player/SweepTerrainTests.cs (PCM-P4-1a / PCM-P4-2b traits); player namespace 356/0 on four legs

---

## Dependencies

- Depends on: Story 006 (vertical step, `OwnedCarrier.Step` order), Story 002 (`CarrierScratch` counters), `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`WorldHeight.Gradient`, `IsWalkableSlope`), `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`ClampInsideMap`, `Bounds`), `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (FX-Ramp via `WorldTablesFixture`; tests may build tiles directly with `WorldTablesBuilder` until it lands)
- Unlocks: Stories 008, 009, 010

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 6/6 passing (all covered by automated tests; none deferred)
**Deviations**: Advisory only, logged as TD-078. (1) AC "Terrain follow" wording: a ≤ 400 mm drop onto a *steep* (non-walkable) face goes Airborne with coyote, not Grounded. Steep terrain is not support, per ADR-0013 §4 "gravity applies on non-walkable terrain". This is pinned by `Walking_MinusXOffASmallDropOntoTheSteepFace_…`; the AC means walkable dips. (2) `CarrierBounds.InsetMm = 1000` is a constant, not a setting, because GDD/ADR-0026 §4 fix it at 1 m. (3) LP structure advisories, listed in TD-078.
**Test Evidence**: Logic — tests/unit/player/SweepTerrainTests.cs (≈57 tests incl. 12 added for QL gaps). Four legs verified on a /tmp copy: net10 Debug player 356/0 (5 Debug alloc skips); net10 Release full 3640/0; net8 Debug player 356/0; net8 Release full 3640/0. The net8 legs were built for net8.0 and run with DOTNET_ROLL_FORWARD=Major, because no net8 runtime is installed.
**Gate fix in-story**: QL gap 4 exposed a bug. `CarrierSweep.Settle` (re-grounding after the map clamp) let the downhill wall-slide push the feet back outside the 1000 mm inset. It now passes maxSlides 0, and the next tick's sweep slides.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (incl. .NET pass; 7 advisories → TD-078); QL-TEST-COVERAGE GAPS → gaps 1–6 fixed in-story; gap 7 (cross-leg golden hash) is story 010's scope; gap 8 is answered, since the Release legs ran the 0 B test unskipped.
**Resumed work**: completed the partial work left by two earlier implementers that hit the turn limit; it was not restarted.
