# Story 014: Batch firing — mailIds at the batch tick, Intake hand-off (`Spawn`, NoMerge), backlog append (`AppendBacklog`), `BatchArrived`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~200 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R11 Intake hand-off and the backlog; R10 batches fire only in Delivery/Raid; R2 rows at the batch tick)
**Requirement**: `TR-mail-009` (backlog: ordered O(1) append; drain owned by Inventory; `backlogUnits` u16), `TR-mail-006` (rows created at the batch tick), `TR-mail-020` (idle tick touches only feeder credits)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §6 backlog, §9 `BatchArrived`); ADR-0009 (`IInventoryApi.Spawn` NoMerge, `AppendBacklog`, `UnitLedger` `Backlog` state, the phase-2 drain); ADR-0004 (mailIds in manifest order at the batch tick)
**ADR Decision Summary**: The backlog queue's storage and drain are Inventory's (`Sim/Inventory/Backlog`, phase 2, ≤ 32 per tick). Mail appends whole entries in manifest order through `IInventoryApi.AppendBacklog(in BacklogEntry)` in phase 8 at the batch tick, for units that did not fit the Intake. Units cross into the ledger's `Backlog` state there. Mail reads `BacklogUnits`/`BacklogEntries` through `IInventoryApi` and publishes `backlogUnits` (u16, saturating). Mail owns what enters, in what order and when, and the counters the HUD sees. `BatchArrived{units u16}` is Replicated.
**ADR Version**: ADR-0017 2026-10-16; ADR-0009 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Core Inventory)**:
- Required: Mail appends backlog entries through `IInventoryApi`; Inventory never calls Mail (ADR-0017)
- Required: inventory interaction goes only through `IInventoryApi` (ADR-0009 §6)
- Required: ids allocated via injected `MailIds` inside `Tick()` only (ADR-0004)
- Forbidden: a Mail-held backlog queue drained by Inventory (ADR-0017 Alt 3); spawning outside Delivery/Raid
- Guardrail: a c_b-unit batch tick makes exactly c_b `Spawn` calls; an append to a non-empty backlog is O(1) with 0 Intake probes (M-PERF-1d)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to this story (real Inventory or its `IInventoryApi` stub with the drain):*

- [ ] **AC-1 (M-R2-1a, M-X-1b)** At a batch tick of 3 units with the mail counter at 41, units get mailIds 42, 43, 44 in manifest order, rows are created (story 001), and each unit becomes its own Intake entry via `Spawn(po_intake, unit, NoMerge)`.
- [ ] **AC-2 (M-R11-1a, b)** An Intake with one free cell and a 60-unit batch → 1 unit enters and 59 are appended to the backlog in manifest order (`backlogUnits` = 59). A later spawn appends while the backlog is non-empty even if an Intake cell is free.
- [ ] **AC-3 (M-R11-1d, M-X-1d)** Conservation: spawned = Intake + backlog + circulation + consumed (the Inventory `UnitLedger` check passes every tick). 40 backlog entries against the 32-per-tick drain leave `backlogUnits` reflecting the 8 left after one tick.
- [ ] **AC-4 (M-R10-1e, M-PERF-1d)** Batches fire only while deliveries are open (Delivery; Raid at M1); a batch tick in a closed state fires nothing and is not deferred. A 64-unit batch is covered by `Spawn` + `AppendBacklog` together for all 64 units (`Spawn` takes one same-key manifest group per call, grid-inventory story 012), and an append to a non-empty backlog makes 0 Intake probes. Each fired batch raises one `BatchArrived{units}`.

---

## Implementation Notes

*Derived from ADR-0017 §6, §9 and ADR-0009:*

- **Where:** Mail's phase 8 `MailStep` (story 005). When `currentTick == manifest.batchTick[next]` and deliveries are open: for each unit in manifest order, `MailTable.Create` (story 001) → if `BacklogEntries == 0`, try `IInventoryApi.Spawn(po_intake, unit, NoMerge)`, else (or on no-room) `AppendBacklog(new BacklogEntry(unit))`. The "backlog non-empty → append" rule (R11) is checked before trying the Intake, so no Intake probe is spent.
- Check the exact `Spawn`/`AppendBacklog` signatures in `production/epics/grid-inventory/story-012-intake-spawn-outfeed.md` and `production/epics/grid-inventory/story-021-backlog-intake-drain.md`. Story 021's decision summary predates ADR-0017 §6 (its own note says so); follow ADR-0017 §6 (storage and drain in Inventory, append via `IInventoryApi`).
- `backlogUnits` is read from `IInventoryApi.BacklogUnits`, saturating to u16 for publication. At M-1a it is exposed on Mail's state for the HUD/mirror. `MailShiftStats` is M0 (TR-mail-021).
- **Idle cost:** between batch ticks, `MailStep` compares one tick value and returns (plus feeder credits, 0 at M-1a). It visits no rows (TR-mail-020).
- `BatchArrived`: payload `units u16` + explicit pad; Replicated; audience all.
- Returned mail (expiry, water, eviction) re-entering the backlog is Inventory's path through the same append. This story does not create it.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Inventory's drain (phase 2), Intake outfeed and WorldItem returns: grid-inventory stories 012, 013, 021.
- Payday returned-to-sender (M-R11-2, M1). `SpawnSpecial` (later).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/BatchIntakeBacklogTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 005, Story 009, Story 013; `production/epics/grid-inventory/story-012-intake-spawn-outfeed.md`, `production/epics/grid-inventory/story-017-unitledger-conservation.md`, `production/epics/grid-inventory/story-021-backlog-intake-drain.md`
- Unlocks: the M-1a greybox loop (mail appears in `po_intake`); Story 016
