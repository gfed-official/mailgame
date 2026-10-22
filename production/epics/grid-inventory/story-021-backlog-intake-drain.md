# Story 021: Backlog append and the IntakeDrain phase (≤ 32 entries/tick, idle exit O(1))

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-10-22 (docs only: cap-eviction-to-backlog and interim-rule retirement ACs added from TD-138 / owner ruling Q3; re-derive the rest at `/story-readiness`)

> **Unblocked 2026-10-16**: ADR-0017 is Accepted. **Note:** ADR-0017 §6 puts the backlog queue's *storage* and drain in Inventory (`Sim/Inventory/Backlog`). Mail appends through `IInventoryApi.AppendBacklog`. The decision summary below predates that, so re-derive it at `/story-readiness`.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-011` (backlog drain ≤ 32 entries/tick, ≤ 8 000 probes, 0 when idle)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0017: Mail table, destination registry & IWalletGate — Accepted 2026-10-16 (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: The backlog FIFO is owned by Mail & Destinations and shared with Inventory's drain (ADR-0009 Enables: "ADR-0017 (mail table, backlog queue shared with the drain)"; GDD R14); the drain binds phase 2 `IntakeDrain`, exits O(1) when the backlog is empty or the Intake has no free cell, runs only after a non-drain Intake change / backlog growth / a capped pass, places ≤ `backlogDrainPerTick` (32) entries, skips entries larger than the free-cell count, no head-of-line blocking (ADR-0009 §5; GDD R14). The queue's storage and ownership are ADR-0017's — Accepted 2026-10-16.
**ADR Version**: ADR-0017 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (Sim half) — re-assess when the blocking ADRs are written
**Engine Notes**: Re-derive when ADR-0017 lands.

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R14-2** — Intake full except (19,15) and backlog `[Large, L1, L2]` → L1 at (19,15), backlog `[Large, L2]`, `backlogUnits = 2`, Intake version +1; next tick with no Intake change → zero placement probes; 100 freed cells and 100 backlog letters → 32, 32, 32, 4 placed at T…T+3, T+4 zero probes.
- [ ] **H-R14-4 (backlog half)** — with a non-empty backlog and free Intake cells, a spawned letter is appended to the backlog tail and makes no Intake placement probe.
- [ ] **H-PERF-6c** — one drain pass with a 2 800-entry backlog ≤ 8 500 probes; a pass with an empty backlog or a full Intake = 0 probes.
- [ ] Expiry/water returns (story 013) and spawn overflow (story 012 `SpawnResult`) append to the backlog when the Intake is full or the backlog is non-empty.
- [ ] **Cap eviction to the backlog (TD-138; owner ruling Q3, decision log 2026-10-21)** — with the Intake full, a qualifying mail cap candidate (age ≥ 300 ticks, lowest `(expiresTick, worldItemId)`) is evicted and appended to the backlog tail (GDD R12 "or backlog if full"), the WorldItem count drops by one, and no non-mail item is destroyed in its place; this replaces story 013's interim rule (the mail stays at its FIFO head and the lowest non-mail item is destroyed instead).
- [ ] **Retire the interim full-Intake rule (TD-138)** — `CounterId.InventoryWorldItemReturnRetries` (47) is retired (no longer incremented; the append-only id stays reserved, not reused) and story 013's `Interim_*` tests in `tests/unit/inventory/WorldItemTests.cs` are removed or rewritten to the backlog behaviour; TD-138 closes with this story.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Per-footprint sub-queues are ADR-0025 (M1); at M-1a a single FIFO with the skip rule meets the probe bound for the solo greybox (solo inflow never fills the Intake — GDD D2 ≈ 268 cells shift 5). Confirm with ADR-0017.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 012: Spawn/outfeed (Ready).
- mail-destinations epic: backlog queue owner.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/IntakeDrainTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 012, Story 013
- Unlocks: mail-destinations epic — `production/epics/mail-destinations/story-014-batch-firing-intake-backlog.md` (Mail appends through `IInventoryApi.AppendBacklog`). *(2026-10-16 hygiene: the former "Depends on: mail-destinations backlog story (not yet created)" is reversed — ADR-0017 §6 puts the backlog storage and drain in Inventory, and that story already depends on this one.)*
