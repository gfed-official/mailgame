# Story 007: F1 StatResolve and F5 WorstCaseStat — pure Int128 formulas

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (F1, F5, CD13; "Determinism and rounding")
**Requirement**: `TR-content-015`
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§7 Resolution) (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§2 rounding toolkit)
**ADR Decision Summary**:
- If any Set is live, `X = S` (min-wins or max-wins per `StatDef`).
- Otherwise `N = base × ΠM_i + 1000^k × (ΣA_i + transientAdd)`, `D = 1000^k`, and `X = R_r(N, D)` with **one rounding**: HalfUp symmetric on magnitude, true Floor, or true Ceil.
- Then clamp to `[min, max]` in Int128 before narrowing to `int`. `k ≤ 6` is asserted.
- HalfUp uses ADR-0006 half-away-from-zero semantics (`UInt128` magnitude form, never `Math.Abs`).

**ADR Version**: ADR-0010 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: `Int128` is a .NET 7+ shared-framework type. It is UNVERIFIED in a 4.7.1 release export (spike S2, deferred to `ContentHost`); it does not block Sim tests.

**Control Manifest Rules (Foundation)**:
- Required: resolution exactly as GDD F1, with one rounding (HalfUp/Floor/Ceil) via the ADR-0006 helpers, the clamp in `Int128` before narrowing to `int`, and `k ≤ 6` asserted. (ADR-0010 §7)
- Required: multi-factor numerators accumulate in `Int128` and divide once. (ADR-0006 §2)
- Forbidden: resolving stats as `base × Σmultipliers + Σadders`, or rounding per modifier. (ADR-0010 Alt 5)
- Forbidden: `Math.Abs` on the rounding path, and `float`/`double`/`decimal`. (ADR-0006)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] `Resolve` is parametrised over the F1 Example lines and returns each listed value:
  - `InventoryRows` depot 10 ×1.5 Ceil → 15; chest 4 → 6; chest 5 → 8;
  - `MoveSpeedAddPm` 360 + transient 300 → 400 (unclamped 660); 360 without the transient;
  - `LateValueRatioPm` Set 1000 max-wins → 1000, and 500 with no Set;
  - `DeliverInsertPm` +500 → 500; `MailMaxStack` letter 20 ×2.0 Floor → 40; `QuotaMultPm`-shaped 1000 × 900 × 700 HalfUp → 630;
  - sign: N = −1500, D = 1000 → −2;
  - `RespawnMs` Set 500 → 1000 (clamped) and Set 3000 → 3000;
  - Floor(−1, 1000) = −1 and Ceil(−1, 1000) = 0;
  - two max-wins Sets 700/900 → 900;
  - depot rows base 10 with ×1.5 and +1 → 16.

  (CDD-STAT-10, CDD-STAT-3 last clause)
- [x] Extreme bound: a synthetic `StatDef` (min 0, max `int.MaxValue`, HalfUp) with base 2^31−1, six Mul of 10 000 and 65 535 Adds of 2^31−1 returns an `unclamped` value equal to a `System.Numerics.BigInteger` reference (saturated to `long`). The resolved value is `int.MaxValue`. No overflow occurs in checked builds. (CDD-STAT-10)
- [x] Determinism property: 1 000 random modifier sets (seed logged; PCG32 from the simulation-core primitives) each give the same result in every push order. Each result equals the `BigInteger` reference implementation of F1. (CDD-STAT-14)
- [x] `WorstCase` (F5) over synthetic raising units:
  - maxStack 40 × Mul 2000 Floor → 80; 20 → 40; 200 → 400;
  - `MoveSpeedAddPm` Add 120 ×3 stacks with T = 300 → 660;
  - F5-low over Muls 900 × 700 × 450 on base 1000 HalfUp → 284.

  A `k_worst > 6` input reports "too many factors" and skips the value. (CDD-STAT-11, formula half)
- [x] `k > 6` passed to `Resolve` trips `SIM_ASSERT` in Debug.

---

## Implementation Notes

*Derived from ADR-0010 §7 and ADR-0006 §2:*

- **Location:** `src/PerformativeMail.Sim/Content/Stats/StatResolve.cs` (pure static functions) and `WorstCase.cs`. Both are allocation-free. Inputs are spans of modifier values, never lists.
- **Explain components.** The core function returns the components the sheet will cache, and `Read`/`Explain` reuse them (story 014):
  - `baseTimesP` (Int128), `k`, `sumA` (long), the Set state, `unclamped` (saturated to `long`) and `clampedAt` (none/min/max).
- **Rounding helpers.** F1's numerator can reach about 2.3×10^33, beyond `long`, so the rounding needs `Int128` forms.
  - Use the rounding toolkit from `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.FloorDiv`/`CeilDiv`/`MulDivRH`, `long` forms; ADR-0006 §2).
  - If it has no `Int128` overloads, add `Fix.DivRoundHalfAway(Int128 n, Int128 d)`, `Fix.FloorDiv(Int128, Int128)` and `Fix.CeilDiv(Int128, Int128)` to `Sim/Primitives/Fix.cs`:
    - HalfUp = `sign(N) × ((2|N| + D) / (2D))`, computed on the `UInt128` magnitude (never `Math.Abs`);
    - Floor is true floor (`Int128 /` truncates, so correct it explicitly);
    - Ceil = `−FloorDiv(−N, D)`.
  - Add a differential test showing the `Int128` overloads agree with the `long` helpers on the `long` domain (ADR-0006 N-vectors).
- **Order of terms** (F1): `transientAdd` is an Add. It enters ΣA before the Set check and before the clamp. A Set discards every Add and Mul but **not** the clamp.
- **Empty product:** P = 1, k = 0, D = 1.
- **F5:**
  - `U_M` = Mul units with M > 1000; `U_A` = Add units with A > 0; Sets count only when they raise. `k⁺ = |U_M|`.
  - F5-low mirrors this over the lowering units.
  - The personal-cap and `excludes` selection need PerkDef and are M1 (CV-PRK-08/10). This story implements F5 over an already-selected unit list. At M-1a, CV-GRD-01 calls it with zero sources.
- **Reference implementation:** the `BigInteger` F1 reference lives in the test project only. `System.Numerics` stays out of the Sim.

---

## Out of Scope

- Story 014: sheet cells, `Read`/`Explain`/`Has`.
- Story 010: the CV-GRD-01 rule that calls `WorstCase`.
- M1 (Perks epic): CV-PRK-08/10 source selection.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/StatResolveTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/StatResolveTests.cs` (68 cases + 1 NonParallel alloc test) and `tests/unit/sim/Primitives/FixInt128Tests.cs` (13)

---

## Dependencies

- Depends on: Story 006 (`StatDef`). Also `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix` helpers; this story adds the `Int128` overloads) and `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md` (PCG32).
- Unlocks: Story 010, Story 014

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (no deferred items)
**Deviations** (all ADVISORY, logged as TD-050):
- Int128 `Fix` overloads return 0 for d ≤ 0 with no counter and no assert (matches the long forms per GDD F7 / TD-005; LP finding 1 declined for consistency).
- F5 Set selection is `s > best` / `s < best` (max(R(N⁺), Sets) / min(R(N⁻), Sets)); GDD F5 names no reference point — C&D to confirm.
- Added Debug guards beyond the story (assert codes 2006–2011: k > 6, Set on NotApplicable stat, Mul outside 0..10 000, > 65 535 adds, min > max, unknown rounding). Story 014 must enforce these at Build (Release does not check them).
- `DefIndexTests` content assert-code count updated 6 → 12 (content-data's own test).
**Test Evidence**: Logic — `tests/unit/content/StatResolveTests.cs` (68 cases incl. F1 example Theory, extreme bounds vs BigInteger, 1 000-set PCG32 property test seed 0x5EEDF00D00000007 stream 54, F5 High/Low + BigInteger loops, SIM_ASSERT guard trips) + `StatResolveAllocationTests` (0 B, Release legs); `tests/unit/sim/Primitives/FixInt128Tests.cs` (13, differential vs long helpers + BigInteger). Four legs (net10/net8 × Debug/Release) 1,891 tests/leg green, 0 warnings, Debug 14 skipped (TD-008 alloc guardrails) — verified by the orchestrator in the real tree.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (0 blocking) resolved in one revision round (findings 2–6 applied, 1 declined per TD-005); QL-TEST-COVERAGE GAPS → gaps 1–4 fixed in the revision; godot-csharp-specialist APPROVE.
**Run result**: N/A — Godot-free Sim formulas; nothing rendered.
