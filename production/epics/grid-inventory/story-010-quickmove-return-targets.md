# Story 010: QuickMove (entry form), Return, and the R9 quick_move target order

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1–1.5 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Evan's approval of 2:24 PM PT, confirmed in scope 3:00 PM PT recorded, docs only: the Mail Lead planning defaults below are now Evan's rulings). Before that: 2026-10-22 (readiness fixes against 007 as merged and the 008 fix patch; Mail Review r1: cursor origin kept by `InventoryMutator`, advisories 2, 3, 4, 6)

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

- [x] **H-R9-1 (M-1a rows)** — every container has space: inventory with an external open → external only; inventory, no external → hotbar; hotbar, no external → inventory; from an external: Axe → hotbar, Plank → inventory, letter → inventory; inventory with the external open but full → `no_room` (no fallback). The external for the item rows is an ownerless 8×4 `player_inventory`-def grid opened with `TryOpenExternal` (`InvAutoPlaceRig.CreateGrid` + the `OpenChest` helper): `po_intake` is mail-only and outfeed-only, so it cannot hold an Axe or a Plank. The letter rows also run against the Intake: a letter from the inventory with the Intake open → Intake, inserted NoMerge and stamped with a new `arrivalSeq`; an Axe from the inventory with the Intake open → `not_allowed`, no fallback. Every container not named stays unchanged (entry count and version). (Backpack rows apply once the backpack exists, M-1b.)
- [x] **H-R6-1 d** — A = letters `[4, 9, 15, 21, 30]` to #5 in the open chest; the inventory is full and the only room in the panel is one unit in a hotbar stack to #5 (19/20). `quick_move {A, count 0}` → exactly one unit, mailId 4 (the lowest), merges into the hotbar stack (19 → 20, ascending, no duplicate); A keeps its entryId and cells with `[9, 15, 21, 30]`; reply `ok` with requested 5 / got 1; chest +1 and hotbar +1, inventory and cursor unchanged. A second `quick_move {A}` → `no_room`, nothing changes.
- [x] **H-R7-9** — entry E picked from an external at (2,1): `return` with (2,1) still free → E back at (2,1), same entryId, chest +1 and cursor +1 (one bump per touched container, TD-150 item 3); with (2,1) filled meanwhile → E AutoPlaced into the panel; with the panel also full → `no_room`, E stays on the cursor; `return` never creates a WorldItem.
- [x] A `quick_move` naming an entry outside the actor's open set → `not_open`; `return` with an empty cursor → `cursor_empty`.
- [x] **QuickMove count and precedence** — `count` 0 or `== total` → the whole entry; `1 ≤ count < total` (stackables) → the k oldest units are split off and AutoPlaced; `count > total` → `bad_count`. Non-stackables are all-or-nothing (a Large with no 2×4 room in any target → `no_room`, nothing moved). Chain: `bad_request → not_open (from) → stale_entry → bad_count → not_carryable → not_allowed → no_room → capacity_exhausted`; `capacity_exhausted` is ADR-0009 §2 code 85; an arena refusal leaves the entry where it is (approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, formerly a Mail Lead planning default, TD-153; it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed letter staying on the cursor when the mail-id arena is full, even with a free cell). The chain order is approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (TD-153). A parametrised test asserts the earliest code.
- [x] **Return fallbacks** — the origin leg runs only if an origin is recorded for the cursor's entry and its container is live and in the actor's open set; a free fit places the whole entry, a mergeable stack takes `min(count, room)`; anything else (blocked, full, not open) falls through to the panel leg (`TargetOrder.CursorRow`, merge pass first), never a reject. A remainder stays on the cursor; `ok` if any unit moved, else `no_room`. The WorldItem count never changes.
- [x] **Rejects raise nothing (TD-105 item 5)** — every rejected `quick_move`/`return` leaves only the `OpReply` (no record, no seal).
- [x] **Versions, weight, indexes** — each container that changed bumps exactly +1 (source and each target that received units), others unchanged; a chest → panel quick_move raises the owner's P by the moved entries' classPts; `CountItem` and the StackKey index match a recount.
- [x] **0 B** — a quick_move / return / reject cycle allocates 0 B per op after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/QuickMoveHandler.cs`, `ReturnHandler.cs`, and the quick_move rows in `Placement/TargetOrder.cs` (story 011 shipped `CursorRow` only; keep it unchanged for the leash put-away). Add `QuickMoveRow(source role, category, externalOpen, hotbar, inventory, external, Span<ushort> targets)` returning the row length (≤ 2 at M-1a). The source role comes from comparing `from` with the actor's `HotbarOf`/`PlayerInventoryOf`/`CursorOf`; any ownerless container is the external. `quick_move {from: cursor}` maps to `CursorRow`. Story 014 adds the mail pickup/catch row; correct the `TargetOrder.cs` header comment to say so.
- Mail from an external → `[inventory, backpack, hotbar]` (CD D-C); tool/weapon/consumable → `[hotbar, inventory, backpack]`; with no backpack container the target is skipped. An outfeed target (the Intake) is planned NoMerge.
- QuickMove Validate: whole entry → `AutoPlace.Plan(subject, e.Count, entrySlot, row, Merge or NoMerge, PassFirst)`; partial → plan with `NoSource` and `units = count`, and Commit splits the k oldest off with story 008's `InventoryMutator` split primitive (new entryId in Commit) and then `AutoPlace.Commit`. Arena reservation (`CheckArenaReservation`) is checked last, as in `MoveHandler`. Declare `bad_count` on `InvOpDescriptors.QuickMove`; `not_carryable` is dormant at M-1a (TD-105 item 4).
- Cursor origin: a per-slot `CursorOrigin { EntryId Entry; ContainerId Container; byte X, Y; bool Rotated }` (type in `InventorySystem`), set and cleared at one choke point, `InventoryMutator` (Mail Review r1 B1; no handler writes it): its `Place` into a cursor records where the entry came from in that op — a `pick` or a `move` onto the cursor the cells it left, the swap's displaced entry its hotbar slot, a piece split onto the cursor its source entry's cells (the move/split rule, cursor origin = the cells it left, is approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-153 item 5) — and its `Remove` from a cursor clears it (a whole place, quick_move, return, leash put-away, merge out, consume). So an origin is present iff the cursor holds the entry it was recorded for; a partial place or merge out keeps it (the entry keeps its id on the cursor), and an entry that arrives from no container gets none. It is Sim state, so story 017's dump section carries it; a load without it leaves origin none and `return` falls to the panel leg.
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

**Status**: [x] Created; green on the current tree (the Mail Review r1 commit on top of 64f9ff6) — net10.0 and net8.0, Debug and Release, 6,087 tests each (6,023 at 18cc838; +64 in r1), 0 failed (Debug 6,032 passed / 55 skipped, the Debug skips including the two Release-only 0 B tests, which pass in Release; Release 6,084 passed / 3 skipped, the assert-build tests); 0 build warnings; tools/ci guards and their self-checks, check-game-build and check-sim-access (with `--self-check`) pass. Evidence supplied by the run owner; `/story-done` did not run the suite.
Files:
- `tests/unit/inventory/QuickMoveReturnTests.cs` (origin, R9 rows, H-R9-1, H-R6-1 d, count, precedence, rejects raise nothing, versions/weight/indexes, H-R7-9, Return fallbacks)
- `tests/unit/inventory/QuickMoveReturnAllocationTests.cs` (`AllocAssert.Zero` plus the every-build twin)
- `tests/unit/inventory/InvMalformedOpTests.cs` (`BoundBySystem` now holds QuickMove and Return)
- `tests/unit/inventory/MoveSplitMergeTests.cs` (shared `SideEffects` incl. CursorOrigin; Move union-block and mail-255 tests)

---

## Completion Notes

**Completed**: 2026-10-22 — PR #33 (merged as main f504231), head 18cc838; Mail Review r1 fixes in the commit after 64f9ff6. The interactive lead-programmer gate was skipped: the review already ran (Sonnet `/code-review` 3e29e00..a75d131).

**Credit**: Evan's 2026-10-22 5:11 AM PT ruling covers only that a leashed letter stays on the cursor when the mail-id arena is full, even with a free cell. Everything below was a Mail Lead planning default and is approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (TD-153) unless it cites the ADR/GDD line that states it.

**Defaults approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (TD-153)**:
- `count > total` → `bad_count`; `count` 0 or `== total` is the whole entry (`count` 0 = whole is this Lead default, TD-153 item 1; ADR-0009 l.347 shows "(0 = whole)" in field notation only and leaves the GDD vs ADR ruling open).
- QuickMove chain order `bad_request → not_open → stale_entry → bad_count → not_carryable → not_allowed → no_room → capacity_exhausted`. Only `not_allowed` before `no_room` is stated (GDD R8 l.115).
- A QuickMove/Return arena refusal leaves the entry where it is (approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; it follows the pattern of the 5:11 AM PT ruling, which covers only the leash).
- Return: the origin leg falls through to the panel leg on any blocked/full/not-open/gone origin and never answers `not_open`; `blocked`/`stack_full` stay declared but unreachable; the Return chain position of `cursor_empty` and `capacity_exhausted`.
- A partial origin merge continues into the panel leg in the same op (`AutoPlace.PlanRemainder`); the remainder stays on the cursor; reply `ok`.
- Return's origin leg never merges into an Intake stack (GDD R14 l.141 states Intake NoMerge); the panel leg maps any AutoPlace refusal with nothing moved to `no_room`.
- `TargetOrder.QuickMoveRow` only; story 014 adds pickup/catch rows.

**Review findings and handling** (Sonnet `/code-review`, fixed in slice 3, 18cc838):
- B1 (blocking citation): QuickMove chain positions relabelled Mail Lead default (TD-153) by analogy with the placement chain.
- A2: `count` 0 = whole cited to the ADR-0009 l.347 erratum (superseded in Mail Review r1, advisory 2: now cited only as a Lead default, TD-153 item 1). A6: Return origin-leg position after `cursor_empty` relabelled a Lead default.
- T1–T4 (test gaps): partial quick_move union + new block arena boundary; MaxRealId pre-check; Return to a free Intake origin (NoMerge, new arrivalSeq); `CursorOrigin` in `MoveSplitMergeTests.SideEffects`. The Move union-block mutant (`targetCount + units` → `targetCount`) was re-run and killed on slots 0 and 1.
- Advisories kept as notes: `container_gone` is declared but unreachable on QuickMove (as Pick/Move, TD-152/TD-153); Return's panel-leg `no_room` mapping (TD-153); `Validate` length is comparable to `MoveHandler`.

**Mail Review r1 (64f9ff6, BLOCKED) and handling** (code on Opus, claude-opus-5-5; review checks on Sonnet):
- B1 (blocking, a stale cursor origin): fixed structurally. The origin table moved into `InventoryMutator`, the one writer of a cursor's cells: `Place` into a cursor records where the entry left in that call (a pick or move onto the cursor its source cells, the swap's displaced entry its hotbar slot, a split piece its source entry's cells) and `Remove` from a cursor clears it. The bespoke writes in `PickHandler` and the swap commit and `InventorySystem.SetCursorOrigin` are gone. The move/split origin rule is TD-153 item 5, **Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT** (was "Mail Lead planning default pending Evan"; advisory fix, Mail Review PR #36 A11); GDD l.95 / l.178 say only "its origin cells". Tests: both repros (pick → move to inventory (3,0) → move back → `return` lands at inventory (3,0); leash put-away → move → move back), a different entry on the cursor, both players holding origins, every way an entry leaves the cursor, and move/split onto the cursor.
- Advisory 2: `count` 0 = whole cited only as a Lead default (TD-153 item 1), not ADR-0009 l.347. Advisory 3: TD-153 item 2 names the tension with ADR-0009 l.113. Advisory 4: the Return union size-class test, the 0 B cycle (partial origin merge, Intake route, `no_room` and `capacity_exhausted` rejects; 14 → 24 ops), QuickMove one pool slot left, partial quick_move from the cursor and into the Intake, whole quick_move at the 255 cap. Advisory 6: `TargetOrder.cs` cites GDD R9 l.128; the PR-draft wording and the swapped skip description are fixed.
- Advisories 1, 5 and 7 stay open as before (story 017 dump; TD-151; Validate purity).

**Untested criteria**: none.

**Cursor origin**: not yet in the canonical dump (story 017).

---

## Dependencies

- Depends on: Story 007, Story 008 (split primitive, Merge bound; must be merged), Story 009, Story 011
- Unlocks: Story 014, Story 018, Story 024
