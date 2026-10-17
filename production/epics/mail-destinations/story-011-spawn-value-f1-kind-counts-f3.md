# Story 011: Spawn value (F1) and units per kind (F3) — pure manifest arithmetic

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~150 lines prod + ~250 lines parametrised tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/mail-destinations.md` (R1 mail kinds, R10 the shift spawner; F1, F3). OQ21: spawner details stay provisional until the two-friend greybox. This story builds the formulas exactly as specified, so the greybox "fixed spawn cadence" is the F1–F4 manifest at shift 1 / District 1.
**Requirement**: `TR-mail-001` (integer money/permille math, int64 intermediates, one RH per formula), `TR-mail-002` (the manifest is a pure function of seed, shift, profile and addresses; F3 is its deterministic part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (§10 manifest roller); ADR-0006 (one rounding per formula, `MulDivRH`, `FloorDiv`); ADR-0010 (`MailKindDef`, `MailMixDef`, balance keys)
**ADR Decision Summary**: The manifest roller runs in phase 8 at the batch tick within ≤ 0.5 ms and is a pure function of its inputs. F1 is one rounding at the end over the product of five factors. F3 computes per-kind counts deterministically, in descending `baseValue` order with a carried remainder, and uses no RNG (randomness enters only in F4).
**ADR Version**: ADR-0017 2026-10-16; ADR-0006 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None. Golden values run on net10.0 and net8.0 (T2).

**Control Manifest Rules (Feature layer + Global)**:
- Required: all quantities use ADR-0006 units and helpers; one rounding per formula (ADR-0006 §1–§2)
- Forbidden: floats (F3's "rational" E_k and r must be carried as exact integer numerator/denominator pairs, never doubles); per-factor rounding in F1
- Guardrail: manifest roll ≤ 0.5 ms (advisory), 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to this story:*

- [x] **AC-1 (M-F1-1)** Spawn value, one rounding: **a** postcard D2 s2 → 6; **b** Medium D4 s3 Cursed → 221 (per-factor rounding would give 222); **c** letter D5 s5 → 22; **d** postcard D1 s2 → 4; **e** Found Large D5 s5 → 1,344; **f** Cursed Large D5 s5 → 672; **g** Found Medium D1 s2 → 231. Every result fits `ushort`.
- [x] **AC-2 (M-F3-1)** V = 960 on 10 District-1 houses (FX-D1-style), shift 1, no Cursed → Medium 1 (r = 26), Small 10 (r = 14), Letter 74 (r = −2); N = 85; expected value 962. **b** |Σ n_k E_k − V| ≤ E_last / 2.
- [x] **AC-3 (M-F3-2)** On FX-I50 (50 houses, 10/8/10/10/12 per district), shift 5, V = 24,704 → exactly Postcard 288, Letter 289, Small 77, Medium 32, Large 22, N = 708. **b** V = 672 solo shift 1 → Medium 1, Small 7, Letter 49, N = 57. **c** A kind with `V_k + r < E_k / 2` gets n = 0 and carries everything on (V_k 4, r −5, E 8 → n 0, r −1). **d** V = 0 → N = 0, no abort. **e** Zero unlocked houses → a content error result (not an exception in a tick). **f** `cursedShare` 0 vs 100 gives identical counts.

---

## Implementation Notes

*Derived from ADR-0017 §10, ADR-0006 and the GDD F1/F3 blocks (read them; do not paraphrase):*

- **Files:** `src/PerformativeMail.Sim/Mail/Manifest/SpawnValue.cs` (`static ushort Compute(int baseValue, int district, int spawnShift, bool cursed, bool found, in MailValueKnobs k)`), `Manifest/KindCounts.cs` (`static int Compute(long v, ReadOnlySpan<KindShare> shares, in MeanDist md, int spawnShift, Span<int> nOut)`, which returns N or an error code).
- **F1:** `RH(baseValue × distPm × shiftPm × cursedPm × foundPm / 10^12)`. The product of four permille factors (≤ 2000 × 1400 × 1500 × 3000) times 160 fits `long`. Use `Fix.MulDivRH(product, 1, 10^12)` (or split as `MulDivRH(a, b, 10^12)`) with one rounding. Knobs `distanceStepPm` (250), `shiftStepPm` (100), `cursedValueMultPm` (1500), `foundValueMultPm` (3000) and `baseValue` come from content (`MailKindDef`, balance keys).
- **F3 exactness:** `meanDistPm = 1000 + 250 × Σ(district − 1) / H` is rational. Represent E_k as `(numerator, denominator)` with the denominator `H × 10^6` (or the LCM the formula needs), and carry `r` in the same denominator. `n_k = max(0, FloorDiv(2(V_k + r) + E_k, 2E_k))` is round-half-up on exact values. Process kinds in **descending `baseValue`** order. The fallback rule (shares of kinds with no unlocked accepting destination fold into `fallbackKind`) applies at M-1a only to deferred kinds (`cargo` is excluded from BuiltInContent, per content-data decision 10). Implement the fold, and test it with M-R1-1b's fixture only if the def supports `fallbackKind` (otherwise note it).
- **Inputs at M-1a:** shares come from `MailMixDef` (content-data decision 2: "the fixed spawn profile is realised as `MailMixDef`"; `production/epics/content-data/story-005-m1a-def-records.md`). The addresses' districts come from the destination registry (story 002), passed as a span of district ids in ascending `AddressId` order.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 012: batch schedule (F2), destination draws (F4) and the RNG draw order. Story 013: assembling the manifest and `PrepareShift`.
- Found/Lost Parcel spawning (R15) and Cursed rolls (R14) beyond the F1 factors: later milestones.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/SpawnValueTests.cs` and `tests/unit/mail/KindCountsTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/mail/SpawnValueTests.cs, KindCountsTests.cs, KindCountsSpawnValueAllocationTests.cs (NonParallel)

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-003-numerics-toolkit.md`; `production/epics/content-data/story-005-m1a-def-records.md`, `production/epics/content-data/story-008-contentset-builtincontent-m1a.md`
- Unlocks: Story 013

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 3/3 passing (all automated; none deferred)
**Deviations** (advisory, TD-085): exact V_k (GDD F3 "truncates" contradicts the M-F3-2 golden — GDD erratum for game-designer); `KindCounts.Compute` takes an extra `in MailValueKnobs k` (shift step); F1 uses `Fix.DivRoundHalfAway(Int128, 10^12)` (RH) instead of the `MulDivRH` hint; F3 status codes are local `KindCountsStatus` constants (story 013 maps them to the content error); cursed/found multiplier balance keys absent (passed to `MailValueKnobs.From`); M-F3-2f covered by varying the cursed multiplier (F3 has no cursed input).
**Implementation**: `src/PerformativeMail.Sim/Mail/Manifest/{SpawnValue,KindCounts,KindShare,MeanDist,MailValueKnobs}.cs` (namespace `PerformativeMail.Sim.MailDestinations`). F1: Int128 product of five factors (each clamped to [0, 2^20]), one RH, saturating ushort. F3: exact rationals over the GDD denominator H×10^6 in Int128, `FloorDiv(2(V_k+r)+E_k, 2E_k)`, descending baseValue (ties → lower index), fallback fold via validated `FallbackIndex` chains (hop-bounded), ≤ 32 kinds, MaxV 10^9, MaxHouses 10,000, 0 B.
**Test Evidence**: Logic — tests/unit/mail/SpawnValueTests.cs, KindCountsTests.cs, KindCountsSpawnValueAllocationTests.cs (MailDestinations namespace 80 tests). Full suite 4,035/leg, 0 failed on net10/net8 × Debug/Release (Debug 31 skipped — TD-008), orchestrator-verified on the real repo.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (blocking: Compute 100 lines/CC 25 → split; malformed fallback chain threw → validate-first, InvalidInput) + C#-specialist pass (Int128 on net8 acceptable); QL-TEST-COVERAGE GAPS → gaps 1/2/4/5/6 fixed.
