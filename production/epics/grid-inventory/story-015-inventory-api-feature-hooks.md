# Story 015: IInventoryApi hooks for feature epics — ConsumeUnit, DetachForThrow, TryInsert, CountItem

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: In Progress
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (readiness fixes: ledger and weight observables moved to 017/016; no new event; TryInsert mail form)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-005` (oldest-first consume; payment per mailId), `TR-inventory-001` (in-tick sync calls)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: In-tick callers use synchronous `IInventoryApi` (same versions/events as ops, no reply): `ConsumeUnit(entryRef, reason, actor)` consumes the oldest unit; `DetachForThrow(slot, entryId, units)` detaches the **whole hotbar entry** to Flight (cursor / non-hotbar → `bad_request`; count ≠ units → `stale_entry`; the selected-slot check is Throwing's validation chain row 2, ADR-0015 §1, before it calls `DetachForThrow` — no selected-slot state is read here); `TryInsert` runs the whitelist and AutoPlace (merge pass first); `CountItem` reads a per-container `itemId → units` index, never a scan (ADR-0009 Key Interfaces, §5; ADR-0003 §5).
**ADR Version**: ADR-0009 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required (Feature/Core, ADR-0003 §5): cross-system calls only through a declared `I…Api` in the caller's own phase
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R6-1 c (API form)** — `ConsumeUnit` on A = `[4, 9, 15, 21, 30]` (Misdeliver path) → 4 consumed, A = `[9, 15, 21, 30]`, version +1; consuming the last unit removes the entry in the same call.
- [ ] **H-X-2 (inventory half)** — hotbar index 3 holds 5 letters (entry 88): `DetachForThrow(slot, 88, 5)` → index 3 empty, hotbar version +1 (one `InvRemoved{hotbar, 88, 5}` then one `InvVersion` seal), entry 88 stays live and detached (`ContainerSlot == NoContainer`, in no container and in no WorldItem) with its 5 mailIds intact, and the result carries entry 88 and 5 units; naming the cursor entry or a non-hotbar entry → `bad_request`, nothing changes, no record; `units` ≠ the entry's count → `stale_entry`, nothing changes. *(The ledger clause "5 ids in FLIGHT" is checked in story 017; the weight clause "P drops by 1" in story 016.)*
- [ ] **H-R5-1 / H-X-3 c (TryInsert path, mail form)** — `TryInsert(po_intake, letter, actor)` (outfeed_only) → returns `false`, no record, zero entries added, version unchanged; a letter into a container whose whitelist refuses it (test fixture container) → `false`, nothing changes; a letter into a container with a matching non-full stack merges into it first (AutoPlace merge pass), one `InvMerged` record, version +1. *(The Plank / item-form leg of H-R5-1 waits for an item-form `TryInsert` — see Out of Scope.)*
- [ ] **CountItem index** — after each of 1 000 random item mutations driven through the `InventoryMutator` primitives (`Place`, `MergeInto`, `TakeOldest`, `Transfer`, `Remove`) `CountItem([panel], itemId)` equals a brute-force scan (H-R17-1 last clause).
- [ ] **Perf / allocation** — `CountItem` is a map read whose probes do not grow with entry count; all four calls allocate 0 B on the tick path (allocation test as `WorldItemAllocationTests`/`IntakeAllocationTests` do).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: extends story 012's `IInventoryApi` (`src/PerformativeMail.Sim/Inventory/IInventoryApi.cs`, implemented by `InventorySystem` and published for declared dependents in `SystemGraph`) with `ConsumeUnit`, `DetachForThrow`, `TryInsert`, `CountItem`; `ConsumeUnit` reuses story 012's `InventoryMutator.TakeOldest`.
- `ConsumeReason` (Delivered / Misdelivered / ReturnedToSender) is a parameter of `ConsumeUnit`; the call raises the existing `InvRemoved{c, entry, 1}` record through `TakeOldest`, sealed with cause `InvCause.Consumed`. No new event kind (`UnitsRemoved{entryIds[]}` is the GDD's network coalescing name, grid-inventory.md l.599 bandwidth paragraph — not a Sim record). Recording the reason on the `UnitLedger` (PLACED → CONSUMED) is story 017's hook, like `TakeOldest`'s and `DestroyDetached`'s. Mail's `MailConsumed`/`DeliveryOutcome` are Mail's (ADR-0017) — not raised here.
- `DetachForThrow` removes the entry with `InventoryMutator.Remove` and leaves it live and detached (`ContainerSlot == NoContainer`, the representation story 013's WorldItems use); the PLACED → FLIGHT ledger crossing is story 017's hook. Weight (`WeightPoints`) follows from `Remove` once story 016 lands.
- New types (planning defaults, ADR-0009 Key Interfaces names): `readonly record struct EntryRef(ContainerId Container, EntryId Entry)`; `readonly record struct DetachResult(InvReason Reason, EntryId Entry, byte Units)` (`Reason` none on success); `enum ConsumeReason : byte { Delivered, Misdelivered, ReturnedToSender }`. `ConsumeUnit` returns `false`, mutating nothing, on a stale ref, an empty entry or a call outside a tick phase.
- `TryInsert` takes the ADR-0009 sketch's mail form `bool TryInsert(ContainerId c, in UnitRef u, ActorId actor)` (story 012's `UnitRef`): whitelist and outfeed_only check, then AutoPlace merge pass first; it creates the unit's entry only when it places.
- `CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId)`: create the per-container open-addressed `itemId → units` map (ADR-0009 §5 table — it does not exist yet), preallocated, maintained inside every `InventoryMutator` item primitive (one place, so op handlers 007/008/010 get it free).
- Counters: a probe counter for the map, if one is added, takes the next free `CounterId` (49 at PR 26's head) — check `src/PerformativeMail.Sim/Core/CounterId.cs` at build time.
- `DetachForThrow` is called by Throwing's `ThrowRelease` handler after Throwing's own checks (ADR-0009 §1) — inventory never owns a throw op.
- `Grant`/`ConsumeItems` (R17) are M1; `SpillToBag` is M-1b — leave them off the interface until their stories (or as `NotImplemented`-free absent members).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 016: weight points (H-X-2's "P drops by 1").
- Story 017: `UnitLedger` crossings (H-X-2's "ids in FLIGHT", ConsumeReason → CONSUMED).
- Item-form `TryInsert` (H-R5-1's Plank leg, H-X-3 b): Belt Logistics / owner question on an ADR-0009 as-implemented note.
- Story 022: `DeliverMatching` (Blocked).
- M1: `Grant`, `ConsumeItems`.
- M-1b: `SpillToBag`, inserter selectors.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY ran 2026-10-21: GAPS, addressed by the 2026-10-21 fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/InventoryApiTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009, Story 013
- Depends on: `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`
- Unlocks: Story 017, Story 018, Story 022; manual-delivery, throwing-catching, mail-destinations epics
