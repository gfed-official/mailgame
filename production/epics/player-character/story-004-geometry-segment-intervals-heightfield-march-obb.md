# Story 004: `Sim/Geometry` primitives — `SegmentIntervals`, `HeightfieldMarch` and `Obb.ToLocal`

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 occlusion "must not cross terrain or a solid box"; P4 OBB colliders); `design/gdd/throwing-catching.md` (F4 swept contact — the interval choices this story must reproduce exactly)
**Requirement**: `TR-movement-005` (height field 2 m bilinear int64 — the march and interval consumers), `TR-movement-011` (focus occlusion primitive), `TR-movement-001` (integer-only geometry)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§6 shared `Sim/Geometry`) — Accepted 2026-10-16; ADR-0026 (§2 the one height kernel) secondary
**ADR Decision Summary**: `Sim/Geometry/` holds pure static T2 functions shared by the carrier, focus occlusion (ADR-0014) and the flight sweep (ADR-0015). `SegmentIntervals` (slab/AABB, circle xz, sphere, cylinder = circle ∩ y-slab) return `[tEnter, tExit]` in ‰ of the segment with exactly Throwing F4's `CeilDiv`/`FloorDiv` choices and its `A = 0` point test; callers apply their own broad-phase bound (|R| ≤ 65 536 mm) before the quadratic forms. `HeightfieldMarch` is Throwing F4's sampled march, shared with focus occlusion. `Obb.ToLocal` rotates into box-local coordinates with Q15.
**ADR Version**: ADR-0013 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Goldens run on the net10.0 and net8.0 legs (T2).

**Control Manifest Rules (Feature layer)**:
- Required: Height comes from `WorldHeight` only; the primitives never reimplement bilinear sampling (ADR-0013 Guidelines, ADR-0026 §2)
- Required: integer maths widened to `long` before the first multiply; one rounding per formula; quadratic forms only after the |R| ≤ 65 536 mm broad-phase reject (ADR-0006, ADR-0015)
- Forbidden: a second ballistic / sweep implementation or a second world-solid grid (ADR-0015, ADR-0026)

---

## Acceptance Criteria

*From ADR-0013 §6 and `throwing-catching.md` F4 / PCM-P11-1 (occlusion), scoped to this story:*

- [x] **Slab (AABB).** For segment `P0 → P1` and box `[lo, hi]`: per axis with `D_a ≠ 0` use `lo' = (lo_a − P0_a) × 1000`, `hi' = (hi_a − P0_a) × 1000` (swap if `D_a < 0`, divide by `|D_a|`), `enter_a = CeilDiv(lo', |D_a|)`, `exit_a = FloorDiv(hi', |D_a|)`; `D_a = 0` is `(−∞, +∞)` iff `lo_a ≤ P0_a ≤ hi_a`, else no hit; `tEnter = max enter`, `tExit = min exit`. Vectors: a segment starting inside the box yields `f = 0`; a box behind the segment (`tExit < 0`) is no hit; touching at exactly `t = 1000` hits.
- [x] **Circle (xz) / sphere (3D).** `A = |D|²`, `B = D·R`, `C = |R|² − r²`, `disc = B² − A·C`; no hit if `disc < 0` or (`A = 0 ∧ C > 0`); `s = ISqrt(disc)`, `enter = CeilDiv(1000·(−B − s), A)`, `exit = FloorDiv(1000·(−B + s), A)`; `A = 0 ∧ C ≤ 0` is `(−∞, +∞)`. Vectors cover tangent (disc = 0), start-inside, both roots negative (no hit).
- [x] **Cylinder.** Circle interval ∩ y-slab interval (flat-capped); a cylinder entered above its top takes the later of the circle and y-slab entries (Throwing F4).
- [x] `hit ⇔ tEnter ≤ tExit ∧ tExit ≥ 0 ∧ tEnter ≤ 1000`; `f = max(tEnter, 0)`. The result type is `(int EnterPm, int ExitPm, bool Hit)`; a `Hit` helper applies the rule once.
- [x] **Broad phase.** A debug assertion / counted check rejects `|R| > 65 536 mm` before any quadratic form (the form is not evaluated); an int64 overflow property test with |R| = 65 536, |D| up to 4 000 mm passes.
- [x] **`HeightfieldMarch.FirstContact(IWorldQuery w, in Vec3Mm p0, in Vec3Mm p1, out int fPm)`** implements Throwing F4 exactly: `n = max(1, CeilDiv(|Dxz|, min(cell/2, 250)), CeilDiv(|Dy|, 250))`, `Q_i = P0 + RH(D × i / n)`, `gap_i = Q_i.y − H(Q_i)` with `H` from `WorldHeight.At`, first `i ≥ 1` with `gap_i ≤ 0` and `gap_{i−1} > 0`, `f = CeilDiv(1000 × ((i−1) × (gap_{i−1} − gap_i) + gap_{i−1}), n × (gap_{i−1} − gap_i))`. Vectors: the F4 example flight on a flat plane, a descending segment crossing the FX-Ramp, a segment that never touches (returns false), one that starts below terrain (contact `f = 0`).
- [x] **`Obb.ToLocal`** rotates a world point/segment into box-local coordinates with Q15 `Sin/Cos`: a box yawed 90° (BAM 16384) maps (x, z) offsets to (z, −x)-style local offsets exactly *(clarified 2026-10-16: per ADR-0013 §3 `right = (cos, −sin)`, `back = (sin, cos)`, yaw 16384 maps world (dx, dz) → local (−dz, dx); story 015 must use this sign)*; yaw 0 is the identity; round trip world → local → world is within 1 mm for |offset| ≤ 20 m.
- [x] All functions allocate 0 B and use no `float`/`double` (analyzer clean).

---

## Implementation Notes

*Derived from ADR-0013 §6, `throwing-catching.md` F4:*

- Files in `src/PerformativeMail.Sim/Geometry/`: `SegmentIntervals.cs`, `HeightfieldMarch.cs`, `Obb.cs`. `WorldHeight.Sample/Gradient/At`, `HeightCorners`, `Vec3Mm`, `AabbMm` are created by `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` — reuse them, do not recreate. The march calls `WorldHeight.At(w, x, z)`.
- Use `Fix.CeilDiv`/`FloorDiv` from sim-core story 003 (floor semantics for negative numerators — never C#'s truncating `/`) and `Fix.ISqrt`. `RH` in the march is `Fix.MulDivRH`.
- The march and intervals are the single implementation for focus occlusion (story 015) and the flight sweep (throwing-catching epic, ADR-0015). Their goldens are the shared T2 vector set: keep the table as data in `tests/unit/geometry/` so throwing-catching can reuse it.
- Cell size for the march is `IWorldQuery.TileSizeMm` (2000), so the sampling step is `min(1000, 250) = 250` mm.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- The flight sweep, receiver ranks and terminals: throwing-catching epic (ADR-0015).
- Using these primitives for focus occlusion: Story 015. Cylinder-vs-AABB collision and the MTV: Story 008.
- `WorldHeight.Sample/Gradient/At`: world-stub story 001.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/geometry/SegmentIntervalsTests.cs`, `tests/unit/geometry/HeightfieldMarchTests.cs`, `tests/unit/geometry/ObbTests.cs` (net10.0 + net8.0) — must exist and pass

**Status**: [x] Created — `tests/unit/geometry/{SegmentIntervalsTests,HeightfieldMarchTests,ObbTests}.cs` + shared `GeometryGoldenVectors.cs` (115 geometry tests), green on all four legs

---

## Dependencies

- Depends on: `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`WorldHeight`, `IWorldQuery`, `Vec3Mm`, `AabbMm`), `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.*`), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`TrigQ15`)
- Unlocks: Story 015 (occlusion), Story 021 (camera boom); `production/epics/throwing-catching/story-002-flightsweep-contact-ranks-winner.md`, `production/epics/throwing-catching/story-003-receiver-view-static-grid-solids-chest-probe.md` (flight sweep)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 8/8 passing (Obb AC wording clarified to the ADR-0013 §3 sign — implementation unchanged)
**Deviations**: None blocking. Advisory: broad phase additionally bounds |D|² ≤ 2³¹−1 and radius ∈ [0, 65 536] (needed for int64 safety); broad-phase/march-extent rejects are `SimAssert` (codes 40 `GeometryBroadPhase`, 41 `GeometryMarchRange`, appended to `SimAssertCode.cs`) and silent no-hit in Release — no runtime counter; march |Dxz| uses ceil-sqrt and `gap_0 ≤ 0` → f = 0. Carry-forwards logged as TD-038 (code quality/perf) and TD-039 (tests).
**Test Evidence**: Logic: `tests/unit/geometry/SegmentIntervalsTests.cs`, `HeightfieldMarchTests.cs`, `ObbTests.cs`, `GeometryGoldenVectors.cs` — full suite 1 430 tests/leg green (net10/net8 × Debug/Release; Debug 8 skipped)
**Code Review**: Complete — QL-TEST-COVERAGE GAPS (advisory; gaps 1–3 fixed: cylinder vertical-drop row, level/rising ramp march vectors, public `HeightfieldMarch.IsWithinExtent`); LP-CODE-REVIEW APPROVE (C#/.NET idiom pass folded in for godot-csharp-specialist)
**Run result**: N/A — pure Sim geometry primitives with no scene, node or rendered output; nothing is observable outside the unit tests
