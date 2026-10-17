# Story 016: Weight → speed (R16, D1): weightPoints and speedPermille

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-015` (integer-only weight→speed, permille, one division; `weightPoints` int16)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout; ADR-0010: Content Format, Loading, ContentSet and StatSheet
**ADR Decision Summary**: Integer units with one division per formula over a wide numerator (ADR-0006 §2); `WeightPenaltyPm` is read through `IStatSheet` (ADR-0010); counted containers hotbar, inventory, backpack, cursor; `weightBasis = PerEntry`.
**ADR Version**: ADR-0006 2026-10-16; ADR-0009 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required (Foundation, ADR-0006): one division per formula; ratios through `Fix.MulDiv`/`MulDivRH`; no float
- Required (ADR-0010): stats only through `IStatSheet.Read`
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-D1-1** — `classPts {1,3,8}`, `ptsPermille` 10, floor 600: P ∈ {0, 2, 8, 12, 39, 40, 41, 112} → `speedPermille` {1000, 980, 920, 880, 610, 600, 600, 600}; with a synthetic `WeightPenaltyPm` Mul 500 source (Strong Back) → {1000, 990, 960, 940, 805, 800, 800, 800}.
- [ ] **H-D1-2** — P = 39 with Strong Back (805‰): walk 5000 → 4025, sprint 7500 → 6037; with a second 1500‰ multiplier the composed helper returns `7500 × 805 × 1500 / 1 000 000 = 9056` (one division) — a two-division implementation (9055) fails.
- [ ] **H-R16-1 a/b** — hotbar: Large (8); inventory: a 20-letter stack (1) and a 1-letter stack (1); cursor: Medium (3); a non-counted external holding 2 Large → P = 13, `speedPermille` 870; moving the Medium into the external → P = 10, 900. (c mounted / d swimming are applied by Player Character — weight exposes the permille only.)
- [ ] `WeightPoints(slot)` is maintained incrementally by the mutator (O(1) per mutation, int16) and equals a brute-force recount after 10 000 random mutations.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Weight.cs`; `IInventoryApi.WeightPoints(PlayerSlot)` / `SpeedPermille(PlayerSlot)`.
- D1: `penalty0 = min(ptsPermille × P, 1000 − floor)`; `penalty = penalty0 × WeightPenaltyPm / 1000` (Strong Back: CD12 `WeightPenaltyPm` = 1000 − strongBackPermille; floor division); `speedPermille = 1000 − penalty`.
- Balance keys `inventory.weightPoints.light/medium/heavy`, `inventory.ptsPermille`, `inventory.weightSpeedFloor` come from `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`InventoryBalance`).
- Provide a composed `MulPermilleChain(base, p1, p2)` helper (one `Int128` division) for Player Character's speed composition, or use `Fix.MulDiv` with an `Int128` numerator; the PC solver owns the call site (ADR-0013).
- `weightBasis` is fixed PerEntry (PerUnit is a rejected alternative) — no runtime switch.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- player-character epic: applying `speedPermille` to locomotion, mount/swim rules.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/WeightSpeedTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Depends on: `production/epics/content-data/story-014-statsheet-build-read-explain.md`, `production/epics/simulation-core/story-003-numerics-toolkit.md`
- Unlocks: player-character epic (carrier speed)
