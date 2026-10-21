# Story 015: IInventoryApi hooks for feature epics — ConsumeUnit, DetachForThrow, TryInsert, CountItem

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-005` (oldest-first consume; payment per mailId), `TR-inventory-001` (in-tick sync calls)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: In-tick callers use synchronous `IInventoryApi` (same versions/events as ops, no reply): `ConsumeUnit(entryRef, reason, actor)` consumes the oldest unit; `DetachForThrow(slot, entryId, units)` detaches the **whole selected hotbar entry** to Flight (cursor / non-hotbar → `bad_request`; count ≠ units or selection change → `stale_entry`); `TryInsert` runs the whitelist and AutoPlace (merge pass first); `CountItem` reads a per-container `itemId → units` index, never a scan (ADR-0009 Key Interfaces, §5; ADR-0003 §5).
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
- [ ] **H-X-2 (inventory half)** — hotbar index 3 selected with 5 letters (entry 88): `DetachForThrow(slot, 88, 5)` → index 3 empty, version +1, the 5 ids are in the Flight ledger state, weight points drop by 1; naming the cursor entry or a non-hotbar entry → `bad_request`, nothing changes; `units` ≠ the entry's count → `stale_entry`.
- [ ] **H-R5-1 (TryInsert path)** — `TryInsert` of a Plank into a mail-only container → `false` (`not_allowed`), zero entries, version unchanged; a letter into a container with a matching non-full stack merges first.
- [ ] **CountItem index** — after each of 1 000 random item mutations (place, merge, consume, transfer) `CountItem([panel], itemId)` equals a brute-force scan (H-R17-1 last clause).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/InventoryApi.cs` (implements `IInventoryApi`; `[SimSystem]` publishes it for declared dependents in `SystemGraph`).
- `ConsumeReason` (Delivered / Misdelivered / ReturnedToSender) is recorded on the ledger and the internal `UnitsRemoved` event; Mail's `MailConsumed`/`DeliveryOutcome` are Mail's (ADR-0017) — not raised here.
- `DetachForThrow` is called by Throwing's `ThrowRelease` handler after Throwing's own checks (ADR-0009 §1) — inventory never owns a throw op.
- `Grant`/`ConsumeItems` (R17) are M1; `SpillToBag` is M-1b — leave them off the interface until their stories (or as `NotImplemented`-free absent members).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 022: `DeliverMatching` (Blocked).
- M1: `Grant`, `ConsumeItems`.
- M-1b: `SpillToBag`, inserter selectors.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

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
