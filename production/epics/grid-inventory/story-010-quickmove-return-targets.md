# Story 010: QuickMove (entry form), Return, and the R9 quick_move target order

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-007` (AutoPlace use), `TR-inventory-005` (oldest-first partials)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: `quick_move` is AutoPlace into R9's targets (partial allowed for stackables, non-stackables all-or-nothing); `return` puts the cursor entry back into its origin cells if still free/mergeable, else AutoPlace into the panel (R9 "put away"), else the cursor keeps it (`no_room`) — never a WorldItem (ADR-0009 §1–§2; GDD R7, R9).
**ADR Version**: ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: reason precedence is one ordered chain per op kind, verbatim from the GDD (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`) (ADR-0009 §2)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R9-1 (M-1a rows)** — every container has space: inventory with an external open → external only; inventory, no external → hotbar; hotbar, no external → inventory; from an external (the Intake at M-1a): Axe → hotbar, Plank → inventory, letter → inventory; inventory with the external open but full → `no_room` (no fallback). (Backpack rows apply once the backpack exists, M-1b.)
- [ ] **H-R6-1 d** — `quick_move {A [4,9,15,21,30] to #5}` into a panel whose only room is one unit in a hotbar stack to #5 → 4 moves, A `[9,15,21,30]`.
- [ ] **H-R7-9** — entry E picked from an external at (2,1): `return` with (2,1) still free → E back at (2,1), same entryId, version +1; with (2,1) filled meanwhile → E AutoPlaced into the panel; with the panel also full → `no_room`, E stays on the cursor; `return` never creates a WorldItem.
- [ ] A `quick_move` naming an entry outside the actor's open set → `not_open`; `return` with an empty cursor → `cursor_empty`.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/QuickMoveHandler.cs`, `ReturnHandler.cs`, `Placement/TargetOrder.cs` (pure R9 table; also used by story 011's leash put-away and story 014's pickup/catch rows).
- The cursor entry remembers its origin `(container, x, y, rotated)` at `pick` time (cursor state, owner-only events).
- Mail from an external → `[inventory, backpack, hotbar]` (CD D-C); tool/weapon/consumable → `[hotbar, inventory, backpack]`; with no backpack container the target is skipped.
- `QuickMoveGroup` (manifest `fromGroup`) is M1 with R18.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: pickup/catch target rows.
- M1: `QuickMoveGroup`, `TakeAll`.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/QuickMoveReturnTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007, Story 008, Story 009, Story 011
- Unlocks: Story 018, Story 024
