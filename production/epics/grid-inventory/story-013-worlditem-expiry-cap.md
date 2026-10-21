# Story 013: WorldItem entity, absolute-tick expiry FIFOs, water float, cap eviction and tombstones

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: In Progress
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-013` (cap 512, expiry FIFOs, lossless mail eviction), `TR-inventory-014` (absolute-tick timers; expiry after Inputs.Apply)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0004: Identity and Id Spaces; ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: WorldItem expiry uses one FIFO per duration class (`mailWorldItem` 9000, `nonMailWorldItem`/`spillHeap` 9000, `waterFloat` 300, `bag` 900), appended at creation so append order is `expiresTick` order (ties → ascending `worldItemId`); phase 1 (`ItemsExpire`, after `InputsApply`) pops heads with `expiresTick ≤ now`, skipping lazy tombstones; cap eviction takes the `mailWorldItem` head if older than `capEvictMinAgeSeconds`, else the lower `(expiresTick, id)` non-mail head; the new item is never a candidate; a 256-entry tombstone ring `(id, byActorId, cause, worldSeq, tick)` (30 s) feeds `container_gone` (ADR-0009 §5; ADR-0004 `EntityClass.WorldItem`).
**ADR Version**: ADR-0009 2026-10-16; ADR-0004 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Forbidden: a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, a per-entry `List<uint>` for `mailIds` (ADR-0009 Alts 3–5)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)
- Required (Foundation, ADR-0003): systems bind phase 1 `ItemsExpire`; a pickup arriving on the expiry tick (phase 0) wins

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R12-2** — a letter (id 500) dropped at tick 1000 → at tick 10000 the WorldItem is gone and 500 is in `po_intake` at the back of the outfeed queue; Plank ×5 dropped at 1000 → at 10000 gone, `ItemsLost {plank, 5}` emitted, in no container; a letter (id 600) landing on water at 2000 while the Intake holds 601–610 → floats until 2300, then enters the Intake with `arrivalSeq` greater than 610's entry, `PeekOutfeed` still 601; a Plank landing on water → DESTROYED at 2300 with `ItemsLost`. (Backlog-if-full branch: story 021.)
- [ ] **H-R12-3** — 512 WorldItems (half mail, half Plank, distinct `expiresTick`, all older than 10 s) + a 513th → the mail item with the lowest `expiresTick` returns to the Intake, count 512, no Plank destroyed; 512 Planks + a 513th Plank → the lowest-`expiresTick` Plank destroyed with `ItemsLost`; 511 Planks + one letter landed 2 s ago + a dropped Plank → the letter is **not** evicted, the lowest Plank is; mail 900 and 901 with equal lowest `expiresTick` → 900 evicted; 512 young-mail items + a 513th → nothing evicted, count 513, `worldItemCapExceeded` logged, and the first to age past 10 s is evicted that tick.
- [ ] **H-D6-1** — `expiresTick = createdTick + seconds × 30`: dropped at 1000 → 10000; thrown at 1000, landing 1045 → 10045 (timer from landing); a solo pause (ticks not advancing) does not move expiry — the item expires on sim tick 10000.
- [ ] **H-PERF-6d** — a 400-WorldItem burst at the cap visits ≤ (items created + 1) candidates in that tick (probe counter).
- [ ] A removed WorldItem (picked up, expired, evicted) leaves a tombstone; a later op naming it within 30 s → `container_gone` with `byActorId` and `cause` (`taken`/`expired`/`destroyed`); after 30 s → `stale_entry`.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/World/` — `WorldItemTable.cs` (struct array, `EntityId` class `WorldItem` via injected `EntityIds`), `ExpiryFifos.cs`, `Tombstones.cs`; `IInventoryApi.CreateWorldItem(entry, posMm, origin, onWater, createdTick)`.
- Position is int32 (GDD: cm; Sim Core uses mm — store `Vec3Mm`, flag the GDD unit to its re-review). The **caller** supplies position and the water flag (Throwing's landing resolution, Drop's ground snap — both elsewhere); this story only stores them.
- Origins at M-1a: `Dropped`, `Thrown`; `BeltLeak` (M-1b), `Spill` (M-1b, death), `Loot` (Mail R15 — exemption is a flag that keeps the item off every FIFO; build the flag, its Payday release is Mail's).
- Mail return on expiry calls story 012's `InventorySystem.ReturnToIntake` (enters whole, NoMerge, new `arrivalSeq`).
- `WorldItem{Spawned, Despawned, EntriesChanged}` events with `worldSeq` are `Replicated` (ADR-0008) — raise them through the mutator.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: `pickup` core and `TryCatch`.
- Story 020: `Drop` (Blocked).
- Story 021: backlog branch (Blocked).
- M-1b: Spill heaps, bags, BeltLeak.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/WorldItemTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 012
- Depends on: `production/epics/simulation-core/story-018-id-allocators-slottable.md` (`EntityIds`), `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (phase binds, `ITickEndCheck`, `SimCoreSettings`)
- Unlocks: Story 014, Story 015, Story 018, Story 020
