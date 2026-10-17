# Story 014: Pickup core (AutoPlace by the R9 pickup rows) and the TryCatch API with fumble

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-007` (TargetFirst AutoPlace), `TR-inventory-013` (WorldItem pickup race)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: Pickup = AutoPlace one entry into the panel by R9's pickup row (mail: `[hotbar, inventory, backpack]` `TargetFirst`; non-mail: category order), partial allowed, `no_room` if nothing fits; two pickups of one entry in a tick: first arrival wins, second `container_gone` with `byActorId`; `TryCatch(slot, flight) → {unitsIn, unitsFumbled}`: oldest units go in, the newest that fit nowhere become one WorldItem (`origin = Thrown`) at the catcher's feet; `unitsIn = 0` is a fumble — never the cursor (ADR-0009 §1, Key Interfaces; GDD R9, R12).
**ADR Version**: ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: boundary crossings into/out of Flight happen only through `IInventoryApi` (ADR-0009 §6)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R12-1 (core form)** — one Small WorldItem, players A and B with room, both pickups applied in one tick, A first → A gains it, B gets `container_gone, byActorId = A`, exactly one copy exists.
- [ ] **H-R9-2 a–e** — a caught 5 letters to #4 with an inventory stack of 3 to #4 at (0,0): a: hotbar slot 2 = 10 to #4 → merged into slot 2 (15), inventory untouched; b: no #4 hotbar stack, slots 1 and 3 free → new entry in slot 1 (never 0); c: hotbar full, no #4 stack → merged into the inventory stack (8); d: hotbar and inventory full and the inventory #4 stack at 20/20, cursor empty → one WorldItem `origin = Thrown` at the catcher's feet, `expiresTick = tick + 9000`, cursor stays empty, no unit lost, `{0, 5}`; e: room for 3 (one hotbar stack at 17/20) → the 3 oldest merge, the 2 newest become the WorldItem, `{3, 2}`. (Backpack rows apply once it exists.)
- [ ] A caught Axe with room → hotbar then inventory (category order); a pickup of a mail WorldItem behaves as a–c, and returns `no_room` instead of d (the item stays).
- [ ] A pickup of a floating (water) WorldItem → `unavailable`; on a heap with several entries `entryId` picks one (gone → `stale_entry`), default the lowest entryId (heaps first appear at M-1b; test with a fixture heap).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/World/PickupCore.cs`; `IInventoryApi.TryCatch` in `InventoryApi.cs`.
- `PickupCore.Apply(actor, worldItemId, entryId?)` contains everything after the range gate; the `Pickup` **OpKind handler** that runs Player Character's P13 chain first is story 019 (Blocked on ADR-0014). Tests call the core directly and through a test-only handler.
- `TryCatch` is called by Throwing (flight resolution, ADR-0015/0021 — not written) with a `FlightPayload` (entry key + mailIds + catcher position); define the payload as the inventory-side input only (entry key, units, catcher `Vec3Mm`) — Throwing owns the call site.
- Ledger: catch moves units Flight → Placed (+ World for the fumble) — via story 017's primitives once landed.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 019: range-gated `Pickup`/`Open` handlers (Blocked).
- throwing-catching epic: the catch decision and `FlightPayload` producer.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/PickupCatchTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009, Story 010 (target orders), Story 013
- Unlocks: Story 015, Story 018, Story 019; throwing-catching epic
