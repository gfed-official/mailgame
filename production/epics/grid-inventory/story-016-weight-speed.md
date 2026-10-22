# Story 016: Weight → speed (R16, D1): weightPoints and speedPermille

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Evan's approval of 2:24 PM PT recorded, docs only: the Mail Lead planning defaults below are now Evan's rulings). Before that: 2026-10-22 (Complete, PR #29)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-015` (integer-only weight→speed, permille, one division; `weightPoints` int16)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout; ADR-0010: Content Format, Loading, ContentSet and StatSheet
**ADR Decision Summary**: Integer units with one division per formula over a wide numerator (ADR-0006 §2); `WeightPenaltyPm` is read through `IStatSheet` (ADR-0010); counted containers hotbar, inventory, backpack, cursor; `weightBasis = PerEntry`.
**ADR Version**: ADR-0006 2026-10-16; ADR-0009 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required (Foundation, ADR-0006): one division per formula; ratios through `Fix.MulDiv`/`MulDivRH`; no float
- Required (ADR-0010): stats only through `IStatSheet.Read`
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up
- Re-checked against manifest 2026-10-17.1: no rule affecting this story changed (weight stays `short weightPoints`, l.145).

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-D1-1** — `classPts {1,3,8}`, `ptsPermille` 10, floor 600: P ∈ {0, 2, 8, 12, 39, 40, 41, 112} → `speedPermille` {1000, 980, 920, 880, 610, 600, 600, 600}; with a synthetic `WeightPenaltyPm` Mul 500 source (Strong Back) → {1000, 990, 960, 940, 805, 800, 800, 800}.
- [x] **H-D1-2** — P = 39 with Strong Back (805‰): walk 5000 → 4025, sprint 7500 → 6037 (truncation); with a second 1500‰ multiplier, Player Character's existing one-division F1 `CarrierSpeed.Final(7500, 805, addPm: 500, buffCapPm: 500, wadePm: 1000, ref sink)` returns `7500 × 805 × 1500 / 1 000 000 = 9056` — a two-division composition (9055) fails. This story adds no second composition helper.
- [x] **H-R16-1 a/b** — hotbar: Large (8); inventory: a 20-letter stack (1) and a 1-letter stack (1); cursor: Medium (3); a non-counted external holding 2 Large → P = 13, `speedPermille` 870; moving the Medium into the external → P = 10, 900. (c mounted / d swimming are applied by Player Character — weight exposes the permille only.) Large / Medium are the `large` / `medium` mail kinds (Heavy 8, Medium 3); the external is `InvEventRig.Chest` (a `player_inventory`-def instance with `OwnerSlot = NoOwner`, `tests/unit/inventory/InventoryEventRig.cs`), since no chest or bike-cargo def exists at M-1a.
- [x] **H-X-2 (weight clause)** — removing a 5-letter hotbar entry with the mutator's `Remove` primitive drops P by 1 (letters PerEntry, class 1), and so does `IInventoryApi.DetachForThrow(slot, entry, 5)` (story 015, Complete); the detached entry no longer counts.
- [x] `WeightPoints(slot)` is maintained incrementally by the mutator (O(1) per mutation) and equals a brute-force recount after 10 000 seeded random mutations (recorded seed; `Place`, `Remove`, `Transfer`, `MergeInto`, `TakeOldest` over two slots' panels and the Chest, refusals included).
- [x] P is accumulated in `int`; `WeightPoints` returns `short`, saturating at `short.MaxValue` (unreachable at the shipped knobs: 25 counted entries × 8 = 200), and a saturation trips `SimAssertCode.InventoryWeightPointsSaturated` (79) under `INVENTORY_ASSERT`. A Bulk entry in a counted container (unreachable: R5) contributes 0 in Release and trips `SimAssertCode.InventoryWeightBulkCounted` (80) under `INVENTORY_ASSERT` (GDD D1: `classPts[bulk]` undefined).
- [x] After `RebuildWeight` (the load-path recount, beside `RebuildIndex`; TD-146) `WeightPoints` equals the brute-force recount for every slot.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Weight.cs`; `IInventoryApi.WeightPoints(PlayerSlot)` / `SpeedPermille(PlayerSlot)` (not in the ADR-0009 Key Interfaces sketch: record in TD-148 for the as-implemented note, no ADR edit).
- Counted = a container with `ContainerHeader.OwnerSlot != NoOwner`; at M-1a that is exactly hotbar, player_inventory and cursor. Backpack (R16) has no ContainerDef at M-1a: deferred to whichever story adds it, no code here. Hooks: +classPts in `InventoryMutator.Place`, −classPts in `Remove` (every other primitive reaches entry count changes through these two; detached/Flight entries do not count). `WeightPoints` is a derived memo, not in the canonical dump.
- Every division is `Fix.MulDiv` (truncation, ADR-0006 §2), never `MulDivRH`; the helper takes `ref SimCounterSink`. No new `CounterId`.
- D1: `penalty0 = min(ptsPermille × P, 1000 − floor)`; `penalty = penalty0 × WeightPenaltyPm / 1000` (Strong Back: CD12 `WeightPenaltyPm` = 1000 − strongBackPermille; floor division); `speedPermille = 1000 − penalty`.
- Balance keys `inventory.weightPoints.light/medium/heavy`, `inventory.ptsPermille`, `inventory.weightSpeedFloor` come from `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`InventoryBalance`).
- Speed composition is Player Character's `CarrierSpeed.Final` (F1, one division, already in `src/PerformativeMail.Sim/Players/CarrierSpeed.cs`); this story exposes `speedPermille` only (ADR-0013 owns the call site).
- `weightBasis` is fixed PerEntry (PerUnit is a rejected alternative) — no runtime switch.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- player-character epic: applying `speedPermille` to locomotion, mount/swim rules.

---

## QA Test Cases

*N/A — no qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/WeightSpeedTests.cs` — must exist and pass
- Allocation: `tests/unit/inventory/WeightSpeedAllocationTests.cs` — 0 B for `WeightPoints`, `SpeedPermille` and a Place/Remove cycle after warm-up (pattern: `InventoryApiAllocationTests.cs`)
- Property / load rebuild: `tests/unit/inventory/WeightRecountPropertyTests.cs` — 10 000 seeded mutations (SplitMix64 over `SplitMix.Mix64`, seed `0x16`) against a brute-force recount after every call; `RebuildWeight` after a scrambled memo and from a zero memo

**Status**: [x] Created and passing (4 legs green: net10.0/net8.0 × Debug/Release)

---

## Dependencies

- Depends on: Story 005, Story 015 (Complete; `DetachForThrow` for H-X-2)
- Depends on: `production/epics/content-data/story-014-statsheet-build-read-explain.md`, `production/epics/simulation-core/story-003-numerics-toolkit.md`
- Unlocks: player-character epic (carrier speed)

---

## Approved defaults (Evan, 2026-10-22 2:24 PM PT)

These were Mail Lead planning defaults of 2026-10-22; **Evan approved them on 2026-10-22 at 2:24 PM PT** ("to all recommended decisions", Mail Lead chat), so they are now Evan's rulings. The story is built on them; TD-148 tracks them.

1. `short WeightPoints(PlayerSlot)` and `int SpeedPermille(PlayerSlot)` are new `IInventoryApi` members outside the ADR-0009 Key Interfaces sketch: now in ADR-0009 Key Interfaces (erratum 2026-10-22; TD-148 closed). No ADR edit.
2. Speed composition stays in Player Character's `CarrierSpeed.Final` (F1, one division); this story exposes `speedPermille` only (`MulPermilleChain` dropped).
3. P saturates at `short.MaxValue` and trips `SimAssertCode.InventoryWeightPointsSaturated` (79); a Bulk entry in a counted container adds 0 and trips `InventoryWeightBulkCounted` (80). No new content-validator rule.

---

## Completion Notes

**Completed**: 2026-10-22 (PR #29, branch `feat/grid-inventory-016`, base main `1bd98be`)
**Criteria**: 7/7 passing, plus the 0 B allocation evidence.
- `src/PerformativeMail.Sim/Inventory/Weight.cs`: per-slot `int` P memo; classPts from `InventorySettings` (`inventory.weightPoints.*`, read once); D1 with one `Fix.MulDiv` (truncation; never `MulDivRH`).
- Hooks only in `InventoryMutator.Place` (+classPts) and `Remove` (−classPts), keyed on `ContainerHeader.OwnerSlot != NoOwner`; `RebuildWeight(PlayerSlot)` beside `RebuildIndex` (load path, TD-146). Derived memo, not in the canonical dump.
- `IInventoryApi.WeightPoints` / `SpeedPermille` in `InventorySystem` (`WeightPenaltyPm` through `IStatSheet.Read`). `SpeedPermille` reads the unsaturated `int` P.
- New ids: SimAssertCode 79, 80. No new CounterId. TD-148 added.
- Fuzz op counts (committed / refused): Place 1992/1016, Remove 1299/198, Transfer 1747/666, MergeInto 442/1050, TakeOldest 1320/170.
- Code review (`/code-review`, Sonnet): APPROVE, no blocking findings. Advisories, not fixed here: (1) nothing validates `inventory.weightSpeedFloor` ≤ 1000 / `ptsPermille` ranges, so an out-of-range knob could give a speed above 1000 (unreachable at shipped knobs; covered by default 3's "no new content rule", pending Evan); (2) the saturation test sits far above the limit (P = 40 000) rather than on the 32 767/32 768 boundary; (3) the 79/80 tests branch on `SimAssertsExpected`, so each branch runs only on its matching leg (Debug and Release both run in CI); (4) the Bulk test exercises the assert on `Place` only, not `Remove` or `Rebuild`; (5) H-R16-1 (a) arranges state outside the mutator then calls `RebuildWeight` (the Place hook is covered by the fuzz); (6) `SpeedPermille` writes the shared primitive counter block only on a divide-by-zero or clamp, which a constant divisor of 1000 cannot hit.
