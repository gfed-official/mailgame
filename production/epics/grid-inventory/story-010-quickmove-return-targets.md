# Story 010: QuickMove (entry form), Return, and the R9 quick_move target order

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1–1.5 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (readiness fixes against 007 as merged and the 008 fix patch)

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
- Re-checked against manifest 2026-10-17.1: no rule cited here changed.

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R9-1 (M-1a rows)** — every container has space: inventory with an external open → external only; inventory, no external → hotbar; hotbar, no external → inventory; from an external: Axe → hotbar, Plank → inventory, letter → inventory; inventory with the external open but full → `no_room` (no fallback). The external for the item rows is an ownerless 8×4 `player_inventory`-def grid opened with `TryOpenExternal` (`InvAutoPlaceRig.CreateGrid` + the `OpenChest` helper): `po_intake` is mail-only and outfeed-only, so it cannot hold an Axe or a Plank. The letter rows also run against the Intake: a letter from the inventory with the Intake open → Intake, inserted NoMerge and stamped with a new `arrivalSeq`; an Axe from the inventory with the Intake open → `not_allowed`, no fallback. Every container not named stays unchanged (entry count and version). (Backpack rows apply once the backpack exists, M-1b.)
- [ ] **H-R6-1 d** — A = letters `[4, 9, 15, 21, 30]` to #5 in the open chest; the inventory is full and the only room in the panel is one unit in a hotbar stack to #5 (19/20). `quick_move {A, count 0}` → exactly one unit, mailId 4 (the lowest), merges into the hotbar stack (19 → 20, ascending, no duplicate); A keeps its entryId and cells with `[9, 15, 21, 30]`; reply `ok` with requested 5 / got 1; chest +1 and hotbar +1, inventory and cursor unchanged. A second `quick_move {A}` → `no_room`, nothing changes.
- [ ] **H-R7-9** — entry E picked from an external at (2,1): `return` with (2,1) still free → E back at (2,1), same entryId, chest +1 and cursor +1 (one bump per touched container, TD-150 item 3); with (2,1) filled meanwhile → E AutoPlaced into the panel; with the panel also full → `no_room`, E stays on the cursor; `return` never creates a WorldItem.
- [ ] A `quick_move` naming an entry outside the actor's open set → `not_open`; `return` with an empty cursor → `cursor_empty`.
- [ ] **QuickMove count and precedence** — `count` 0 or `== total` → the whole entry; `1 ≤ count < total` (stackables) → the k oldest units are split off and AutoPlaced; `count > total` → `bad_count`. Non-stackables are all-or-nothing (a Large with no 2×4 room in any target → `no_room`, nothing moved). Chain: `bad_request → not_open (from) → stale_entry → bad_count → not_carryable → not_allowed → no_room → capacity_exhausted`; `capacity_exhausted` is ADR-0009 §2 code 85; an arena refusal leaves the entry where it is (Mail Lead planning default pending Evan, TD-153; it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed letter staying on the cursor when the mail-id arena is full, even with a free cell). The chain order is a Mail Lead default (TD-153). A parametrised test asserts the earliest code.
- [ ] **Return fallbacks** — the origin leg runs only if an origin is recorded for the cursor's entry and its container is live and in the actor's open set; a free fit places the whole entry, a mergeable stack takes `min(count, room)`; anything else (blocked, full, not open) falls through to the panel leg (`TargetOrder.CursorRow`, merge pass first), never a reject. A remainder stays on the cursor; `ok` if any unit moved, else `no_room`. The WorldItem count never changes.
- [ ] **Rejects raise nothing (TD-105 item 5)** — every rejected `quick_move`/`return` leaves only the `OpReply` (no record, no seal).
- [ ] **Versions, weight, indexes** — each container that changed bumps exactly +1 (source and each target that received units), others unchanged; a chest → panel quick_move raises the owner's P by the moved entries' classPts; `CountItem` and the StackKey index match a recount.
- [ ] **0 B** — a quick_move / return / reject cycle allocates 0 B per op after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/QuickMoveHandler.cs`, `ReturnHandler.cs`, and the quick_move rows in `Placement/TargetOrder.cs` (story 011 shipped `CursorRow` only; keep it unchanged for the leash put-away). Add `QuickMoveRow(source role, category, externalOpen, hotbar, inventory, external, Span<ushort> targets)` returning the row length (≤ 2 at M-1a). The source role comes from comparing `from` with the actor's `HotbarOf`/`PlayerInventoryOf`/`CursorOf`; any ownerless container is the external. `quick_move {from: cursor}` maps to `CursorRow`. Story 014 adds the mail pickup/catch row; correct the `TargetOrder.cs` header comment to say so.
- Mail from an external → `[inventory, backpack, hotbar]` (CD D-C); tool/weapon/consumable → `[hotbar, inventory, backpack]`; with no backpack container the target is skipped. An outfeed target (the Intake) is planned NoMerge.
- QuickMove Validate: whole entry → `AutoPlace.Plan(subject, e.Count, entrySlot, row, Merge or NoMerge, PassFirst)`; partial → plan with `NoSource` and `units = count`, and Commit splits the k oldest off with story 008's `InventoryMutator` split primitive (new entryId in Commit) and then `AutoPlace.Commit`. Arena reservation (`CheckArenaReservation`) is checked last, as in `MoveHandler`. Declare `bad_count` on `InvOpDescriptors.QuickMove`; `not_carryable` is dormant at M-1a (TD-105 item 4).
- Cursor origin: a per-slot `CursorOrigin { EntryId Entry; ContainerId Container; byte X, Y; bool Rotated }` in `InventorySystem` beside `_panels`. `PickHandler.Commit` writes it (a 007 retrofit), and so does 008's swap commit for the displaced entry (its hotbar slot). It is valid only while the cursor holds `Entry`, so a whole place, swap-out, leash put-away or drop makes it inert without a clearing hook, and a partial place keeps it. It is Sim state, so story 017's dump section carries it; a load without it leaves origin none and `return` falls to the panel leg.
- Return chain: `bad_request → cursor_empty → [origin leg] → [panel leg] → no_room → capacity_exhausted`. `blocked`/`stack_full` stay declared on `InvOpDescriptors.Return` but are unreachable (fall-through).
- Bind `InvOpDescriptors.QuickMove` and `.Return` in `InventorySystem.Bind` beside Close/Pick/Move/Merge; add both to `BoundBySystem` in `tests/unit/inventory/InvMalformedOpTests.cs` and move their `ValidCases` rows into `QuickMoveReturnTests` (unplaced entry → `stale_entry`; empty cursor → `cursor_empty`); re-check `GoldenFuzzHash`.
- Performance: Validate = one `AutoPlace.Plan` (per-cell probe counters unchanged, H-PERF-6) plus O(footprint) for the origin leg; Commit O(k) id copy.
- `QuickMoveGroup` (manifest `fromGroup`) is M1 with R18.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: pickup/catch target rows.
- M1: `QuickMoveGroup`, `TakeAll`.

---

## QA Test Cases

*No qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/QuickMoveReturnTests.cs` — must exist and pass
- Allocation: `tests/unit/inventory/QuickMoveReturnAllocationTests.cs` — `AllocAssert.Zero` plus the every-build twin (pattern: `PickMoveAllocationTests.cs`)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007, Story 008 (split primitive, Merge bound; must be merged), Story 009, Story 011
- Unlocks: Story 014, Story 018, Story 024
