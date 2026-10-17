# Story 004: `MailPay` — timeliness (F5), unit pay (F6), unit penalty (F7), wallet guard (F8), value-hint payout (F10)

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~200 lines prod + ~300 lines parametrised tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/mail-destinations.md` (R7 payment, R8 penalty and floor, R17 value hint, R18 stat keys; F5, F6, F7, F8, F10)
**Requirement**: `TR-mail-001` (integer money/permille math, int64 intermediates, one RH per formula), `TR-mail-005` (the same pay functions compiled into the client), `TR-mail-023` (host-authoritative payment; client F10 is display-only)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §4); ADR-0006 (`Cents`, `MulDivRH`, one rounding per formula); ADR-0016 §3 (preview money = Σ per-unit pay, never one rounding over a total); ADR-0010 (StatSheet reads)
**ADR Decision Summary**: `MailPay.Timeliness` (F5), `UnitPay` (F6), `UnitPenalty` (F7), `HintPayout` (F10) and `WalletGuard` (F8) are static, pure functions over `Cents`/permille with one `MulDivRH` per unit. The host consume routine and the client preview call the same functions. Stats come through `IStatSheet.Read`; Cannon Arm's `thrownPm` is passed in, snapshotted at the throw.
**ADR Version**: ADR-0017 2026-10-16; ADR-0006 2026-10-16; ADR-0016 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None. Golden values run on net10.0 and net8.0 (T2).

**Control Manifest Rules (Feature layer + Global)**:
- Required: all quantities use ADR-0006 units and helpers (`Cents`, permille), one rounding per formula (ADR-0006 §1–§2)
- Required: read stats through `IStatSheet.Read` (ADR-0010 §7)
- Required: `MailPay` is static, pure and 0-alloc (ADR-0017)
- Forbidden: rounding per factor; floats; a second pay/penalty implementation (ADR-0017)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to this story:*

- [ ] **AC-1 (M-F5-1)** Timeliness (spawn shift, current shift) → ‰: (2, 2) 1000, (2, 3) 500, (2, 4) 0. **b** With Grace Period (`LateValueRatio` 1000): (2, 3) 1000, (2, 4) 0. **c** With `deadLetterShifts = 1`: (2, 3) 0.
- [ ] **AC-2 (M-F6-1, M-R7-1a, M-R8-1a)** A late letter (spawn 14) with Stamp Collector pays 9; a thrown on-time letter (8) with Stamp Collector and Cannon Arm (`thrownPm` 1100) pays 11; a dead letter pays 0 under any modifiers. Stamp Collector (1250 ‰) applies to postcard and letter only (letter 8 → 10, small 30 → 30). `thrownPm` is a parameter, never read from the StatSheet inside `UnitPay`. A 40-unit entry of the largest per-unit pay sums within int32.
- [ ] **AC-3 (M-F7-1, M-R8-1a/d)** Penalty: letter 8 → 4; letter 22 → 11; value 5 → 3 (half up); dead letter spawn 8 → 4 (spawn-based). With Fragile Handling (`MisdeliveryPenaltyMult` 500): postcard 4 → 1, value 9 → 2, value 1 → 1 via `minPenaltyCents`. Stamp Collector does not change the penalty.
- [ ] **AC-4 (M-F8-1d, part of c)** `WalletGuard(balance, penalty, floor)`: −496 with 4 → true (−500 exactly allowed); −500 with 1 → false; −600 with 1 → false, with no negative quotient computed.
- [ ] **AC-5 (M-F10-1a, c, d; M-R7-2a)** `HintPayout` over runs [(deadline 1, count 3, value 8), (deadline 2, count 2, value 9)] at currentShift 2 = 30 ¢, and the one-unit penalty = 4 ¢. Recomputing at a new `currentShift` needs no new hint input. Inspector / Neighbourhood Watch / zero-misdelivery StatSheet entries leave unit pay at the baseline (they are Run Structure's, at Payday).

---

## Implementation Notes

*Derived from ADR-0017 §4, ADR-0006 and GDD F5–F8, F10 (read the formula blocks in the GDD; do not paraphrase them):*

- **File:** `src/PerformativeMail.Sim/Mail/MailPay.cs`. Signatures follow ADR-0017 Key Interfaces: `Cents UnitPay(in UnitFacts u, int timelinessPm, IStatSheet s, PlayerSlot? p)`, `Cents UnitPenalty(in UnitFacts u, IStatSheet s)`, `bool WalletGuard(Cents balance, Cents penalty, Cents floor)`. Add `int Timeliness(int currentShift, int deadlineShift, int lateValueRatioPm, int deadLetterShifts)` and `Cents HintPayout(ReadOnlySpan<HintRun> runs, int currentShift, …)` (`HintRun { byte DeadlineShift; byte Count; ushort UnitSpawnValue; }`).
- **`IStatSheet` is the one interface parameter allowed.** ADR-0017's own signatures take it, and ADR-0016 §3 lists `IStatSheet.Read` as a shared function with a client sheet copy. It is a read-only, deterministic stat read, not hidden mutable state. Keep the read count fixed per call.
- **F6 (one rounding):** `pay = MulDivRH(spawnValue × T‰ × MailValueMult‰[kind], thrownPm, 10^9)`. Build the product in `long` (or `Int128` if `MulDivRH` needs it) and round once. Do not round after each factor.
- **F7:** `max(minPenaltyCents, MulDivRH(spawnValue × misdeliveryPenaltyRatio‰, MisdeliveryPenaltyMult‰, 10^6))`.
- **F8:** compare `balance − penalty ≥ floor` on `Cents` directly. Never divide (the GDD's quotient shortcut is valid only for equal penalties; M-F8-2 in story 008 proves the per-unit rule).
- **Data keys:** `lateValueRatio`, `deadLetterShifts`, `misdeliveryPenaltyRatio`, `minPenaltyCents` and `walletFloor` are balance keys (`production/epics/content-data/story-004-balance-registry-simsettings.md`). The StatSheet keys `MailValueMult.<kind>`, `ThrownCorrectPayMult`, `LateValueRatio` and `MisdeliveryPenaltyMult` are R18's (`production/epics/content-data/story-006-stat-statdef-ruleflag.md`, `story-014-statsheet-build-read-explain.md`). If a key is missing from the M-1a stat subset, test through a stub `IStatSheet` and flag the missing key in the completion notes.
- `Cents` and `Fix.MulDivRH` come from `production/epics/simulation-core/story-003-numerics-toolkit.md`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: applying pay and penalties through `IWalletGate` per unit (M-F8-1a–c, M-F8-2, M-X-4).
- Story 010: client facts and the value-hint input from mirrors. The replicated hint runs on the wire are M0 (TR-mail-016).
- Payday shift-level adjustments (Run Structure, M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/MailPayTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — `tests/unit/mail/MailPayTests.cs` (38), `MailPayAllocationTests.cs` (1); green on net10.0 and net8.0 Release and on Debug

---

## Dependencies

- Depends on: Story 003 (`UnitFacts`); `production/epics/simulation-core/story-003-numerics-toolkit.md`; `production/epics/content-data/story-014-statsheet-build-read-explain.md`
- Unlocks: Story 007, Story 008, Story 010; manual-delivery `predictedPay`/`predictedPenalty`

---

## Completion Notes
**Completed**: 2026-10-17
**Criteria**: 5/5 passing (none deferred)
**Deviations**: Signatures extend the ADR-0017 sketch: every money function takes `ref SimCounterSink sink` last (so `Fix` clamp / div-zero counters are not dropped); `UnitPay` takes `thrownPm` as a parameter (per the story); `UnitPenalty` takes `misdeliveryPenaltyRatioPm` and `minPenaltyCents` from the caller's `MailBalance` (no statics); `WalletGuard` takes the floor as `Cents` because no Economy-owned `walletFloor` balance key exists yet; `HintPayout`/`HintPenalty`/`HintRun` are additions (F10). `HintPayout` computes `Count × UnitPay(run)` (identical units round identically = Σ per-unit pay, ADR-0016 §3). M-R7-2a is tested with proxy stats because no Run Structure stats exist in M-1a. ADR erratum owed: TD-096.
**Test Evidence**: Logic — `tests/unit/mail/MailPayTests.cs`, `tests/unit/mail/MailPayAllocationTests.cs` (39 tests; Mail namespace 189 pass / 1 skip on Release net10 and net8, Debug green; Release build 0 warnings; full Release net10 suite green at the implementer's run: 4,424 / 0 / 1).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (advisories → TD-096); QL-TEST-COVERAGE GAPS → G1 (single-rounding discriminator: letter 5 × 500‰ × 1250‰ × 1100‰ = 3, per-factor would give 4), G2 (Cannon Arm on the sheet ignored when thrownPm = 1000) and G5 (deadLetterShifts 3 band) fixed before closure; G3 trait ids corrected; G4 and the rest → TD-096.
