# Story 017: UnitLedger conservation counters, tick-end check, INVENTORY_ASSERT full scan and the canonical dump section

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-012` (O(1) conservation counters checked each tick; `INVENTORY_ASSERT` compiled out of Release)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay; ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: A `UnitLedger` holds O(1) counters per location state (`Placed, Cursor, World, Flight, Belt, Backlog, Consumed, Destroyed`) for mail and per-itemId item units, plus `spawned`, `granted`; only the lowest mutator primitives touch it; the tick-end check asserts `Σ live = spawned + granted − consumed − destroyed` in every build and writes an always-on record on mismatch (never throws); the full multiset scan runs behind `[Conditional("INVENTORY_ASSERT")]` (ADR-0009 §6). Inventory state is a canonical-dump section in ascending ids (ADR-0006 §4).
**ADR Version**: ADR-0009 2026-10-16; ADR-0006 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: `[Conditional("INVENTORY_ASSERT")]` binding per calling assembly in a Godot Release export is an OQ4 build-spike item (ADR-0001) — irrelevant to the Sim test project; verify in `godot-host-shell`.

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: conservation via `UnitLedger` touched only by the lowest mutator primitives; tick-end Σ check in every build, always-on record on mismatch, never throws; full scan behind `[Conditional("INVENTORY_ASSERT")]` (ADR-0009 §6)
- Required (Foundation, ADR-0006 §4): `ISimStateContributor` writes fields one by one, little-endian, collections ascending by id, unique never-reused `SectionId`
- Required (Foundation, ADR-0001 §4): build-dependent behaviour only via `SIM_ASSERT`/`INVENTORY_ASSERT`

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] The ledger is updated only in `InventoryMutator` primitives (an architecture/grep test finds no other writer); after any sequence of M-1a calls, `Σ live = spawned + granted − consumed − destroyed` for mail and per itemId.
- [ ] **H-X-2 (ledger clause, moved from story 015)** — after `DetachForThrow(slot, 88, 5)` the ledger shows Placed −5, Flight +5 for mail; after `ConsumeUnit(ref, reason, actor)` Placed −1, Consumed +1 (the `ConsumeReason` is recorded with the crossing).
- [ ] A deliberately corrupted counter (test seam) makes the tick-end check write one always-on `SimCheckRing` record naming the location state and delta, and the tick completes (no exception) in Debug and Release.
- [ ] In Debug the `INVENTORY_ASSERT` full scan detects a seeded duplicated mailId and a lost unit (multiset vs ledger) and asserts; in Release the scan method is absent from the call site (IL/reflection check that the `[Conditional]` call is compiled out).
- [ ] The inventory `ISimStateContributor` section writes containers (versions, `lastReplaceVersion`, `statEpoch`), entries by entryId, cursors, WorldItems, outfeed nodes, `arrivalSeq` counters, the entryId allocator and the tombstone ring, ascending ids; two worlds built and driven identically produce byte-identical sections.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ledger/UnitLedger.cs`, `ConservationCheck.cs` (`ITickEndCheck`), `InventoryStateContributor.cs`.
- Counters are per location state × {mail} plus a per-itemId array sized from the ItemDef table (`DefTable<ItemDef>.Count`), preallocated.
- Retrofit: stories 005/012/013/014/015 call mutator primitives; this story adds the ledger calls inside those primitives (one place). If those stories land first, their tests must still pass unchanged.
- `SimCheckRing` / always-on records: `production/epics/simulation-core/story-007-simcounters-simcheckring.md`; dump: `production/epics/simulation-core/story-019-canonical-state-dump.md`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 018: the H-R19-1 fuzz and H-DET-1.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/UnitLedgerTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 012–015
- Depends on: `production/epics/simulation-core/story-007-simcounters-simcheckring.md`, `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (`INVENTORY_ASSERT`)
- Unlocks: Story 018
