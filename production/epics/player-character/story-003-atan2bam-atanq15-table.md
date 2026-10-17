# Story 003: `TrigQ15.Atan2Bam` and the committed `AtanQ15` table

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~200 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P3 "body yaw turns toward the move direction"; P4 "Fixed-point trig … → ADR")
**Requirement**: `TR-movement-005` (fixed-point trig LUT), `TR-movement-002` (integer units, one rounding per formula)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P3-3 (camera-relative heading, 180° in 8 ticks), ADR-0013 Validation `Atan2Bam` octant/quadrant vectors and table hash.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§3 angles) — Accepted 2026-10-16; ADR-0006 (§6 Q15 table, BAM, T2) secondary
**ADR Decision Summary**: `TrigQ15.Atan2Bam(long y, long x) → ushort` does octant reduction (swap/negate so `0 ≤ t = min/max ≤ 1`) plus a committed `AtanQ15` table of 1025 `ushort` BAM values for `t = i/1024`, indexed by `MulDivRH(min, 1024, max)`. `(0, 0)` returns 0 and is never used for turning. The table is generated once by `tools/GenTrigQ15` (which already generates the sine table), committed as a `ReadOnlySpan<ushort>` literal and SHA-256 hash-tested.
**ADR Version**: ADR-0013 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Tables are `static ReadOnlySpan<T> X => new T[] { … }` *properties* (compiler emits `RuntimeHelpers.CreateSpan`, no allocation), never `static readonly` arrays (ADR-0013 engine validation note 3). `tools/GenTrigQ15` is a dev-time tool and may use `double`; the Sim never does.

**Control Manifest Rules (Feature layer)**:
- Required: angles are BAM with the ADR-0013 §3 axis convention (+X right, +Y up, −Z forward at yaw 0; positive BAM turns counter-clockwise from +Y); `atan2` goes through `TrigQ15.Atan2Bam`; both tables committed and hash-tested (ADR-0013 Guidelines)
- Forbidden: floats, `Math.Atan2`, interpolation of the sine table in Sim code (ADR-0006, ADR-0013 §3)

---

## Acceptance Criteria

*From ADR-0013 §3 and Validation Criteria, scoped to this story:*

- [x] `Atan2Bam` axis vectors: `(y=0, x=1) → 0`, `(1, 0) → 16384`, `(0, −1) → 32768`, `(−1, 0) → 49152`, and `(0, 0) → 0`.
- [x] Accuracy: over ≥ 10 000 deterministic pseudo-random `(y, x)` pairs (test code may use `Math.Atan2`/`double` as the oracle, the Sim may not), `|Atan2Bam − round(atan2(y, x) × 65536 / 2π)| ≤ 10 BAM` (≈ 0.056°, the ADR figure). All eight octant boundaries and both signs of each axis are covered, including large magnitudes (|x|, |y| up to 2³¹).
- [x] Symmetry: for every tested vector with a non-zero angle, `Atan2Bam(−y, x) == unchecked((ushort)(65536 − Atan2Bam(y, x)))` and `Atan2Bam(y, −x) == unchecked((ushort)(32768 − Atan2Bam(y, x)))` (exact — the reduction is symmetric).
- [x] Round trip with the ADR convention: for BAM `θ` over a stride of 257 values, let `(wx, wz) = (−Sin θ, −Cos θ)` (the `forward(θ)` vector, Q15); then `Atan2Bam(−wx, −wz)` is within 10 BAM of `θ & ~15` and within 25 BAM of `θ` (the body-yaw inverse used by story 005). *Revised 2026-10-16: unattainable as written ("within 10 BAM of θ") because `Sin`/`Cos` ignore the low 4 BAM bits (ADR-0006 §6) — see `production/session-logs/decision-log.md` (2026-10-17T01:59:27Z orchestration entry).*
- [x] The `AtanQ15` table has 1025 entries, `[0] == 0`, `[1024] == 8192`, is non-decreasing, and the SHA-256 of its 2050 little-endian bytes equals the digest recorded in this story's Implementation Notes at generation time (the T2 pin, re-run by SC-DET-2). Reading the table and calling `Atan2Bam` 100 000 times allocates 0 B.

---

## Implementation Notes

*Derived from ADR-0013 §3:*

- Extend `tools/GenTrigQ15/` (created by `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md`) to also emit `src/PerformativeMail.Sim/Primitives/AtanQ15.g.cs` (`ReadOnlySpan<ushort>` property) and print the SHA-256. Run once, commit, paste the digest here: `AtanQ15 SHA-256: 59418BF648C36A7998072AEA0D9EF34D3BABB4A734582D70F6A229278CCACB70`. Entry `i` = `round-half-away(atan(i/1024) × 65536 / 2π)`.
- `Atan2Bam` lives in `Primitives/TrigQ15.cs` as an additive `partial` member; do not touch the sine table. Reduction: `ax = |x|, ay = |y|` in `long` (`checked` against `long.MinValue`); index `MulDivRH(min, 1024, max)` via `Fix.MulDivRH` (sim-core story 003); octant offset from the quadrant and the swap flag; all BAM arithmetic in `unchecked((ushort)…)`.
- Do **not** interpolate the table or add a second table. The Sim value is only used for drop position and pose; presentation smooths body yaw by shortest arc (ADR-0013 engine note 1).
- Body-yaw convention reminder for story 005: `target = Atan2Bam(−wx, −wz)` where `w` is the world-space move vector.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: camera-relative input, the yaw-step turn (F2) and the move vector.
- sim-core story 005: `Sin`/`Cos` table and its hash.
- Presentation-side BAM → `Rotation.Y` conversion: story 022.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/sim/Primitives/Atan2BamTests.cs` (Debug + Release, net10.0 + net8.0) — must exist and pass

**Status**: [x] Created — `tests/unit/sim/Primitives/Atan2BamTests.cs` (10 methods), green on all four legs

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`TrigQ15`, `tools/GenTrigQ15`), `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.MulDivRH`)
- Unlocks: Story 005 (body yaw), Story 014 (focus cosine uses the Q15 forward vector, not atan, but shares the convention)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (AC-4 revised 2026-10-16 — spec defect, implementation unchanged; see decision log 2026-10-17T01:59:27Z)
**Deviations**: None blocking. Advisory: LP-CODE-REVIEW CONCERNS (non-blocking) and QL carry-forwards logged as TD-035 (code quality/perf) and TD-036 (tests). Story 005 note added: yaw round trip must allow 25 BAM vs θ (or ≤ 10 vs θ & ~15).
**Test Evidence**: Logic: `tests/unit/sim/Primitives/Atan2BamTests.cs` — worst error 6 BAM (≤ 10); full suite 1 237 tests/leg green (net10/net8 × Debug/Release; Debug 6 skipped)
**Code Review**: Complete — QL-TEST-COVERAGE ADEQUATE; LP-CODE-REVIEW CONCERNS (non-blocking; stale TrigQ15 doc line fixed); godot-csharp-specialist no blockers
**Run result**: N/A — pure Sim math primitive with no scene, node or rendered output; nothing is observable outside the unit tests
