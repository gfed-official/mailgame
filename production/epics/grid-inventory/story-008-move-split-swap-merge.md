# Story 008: Move split-and-place, hotbar swap from the cursor, and the Merge op

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (readiness fixes against story 007 as merged)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-004`, `TR-inventory-005` (oldest-first), `TR-inventory-002`
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0004: Identity and Id Spaces
**ADR Decision Summary**: A `move` with `count < total` is an atomic split-and-place (the new piece gets a new entryId, the remainder keeps the old; the k **lowest** mailIds move); a `move` from the cursor onto an occupied, non-mergeable **slots** cell swaps (one op, one bump); `merge` moves `min(count, effMax − target.count)`; entry ids are never reused (ADR-0009; ADR-0004).
**ADR Version**: ADR-0009 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: reason precedence is one ordered chain per op kind, verbatim from the GDD (placement: `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`) (ADR-0009 §2)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)
- Re-checked against manifest 2026-10-17.1: no rule cited here changed.

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R4-2 c/d** — c: A = 10 letters at (0,0), `move {A, count: 4 → (0,0)}` (split onto own cells) → `blocked`; d (swap): hotbar slot 1 = Axe, cursor = 3 letters, `move {cursor → hotbar, x: 1}` → `ok`, slot 1 = the letters, cursor = the Axe, hotbar +1 and cursor +1 (one bump per touched container, the story 007 / TD-150 item 3 pattern), no other container; the same onto an occupied **grid** cell → `blocked` (no grid swap); slot 1 = 20 letters to #3 and the cursor 2 letters to #3 → `stack_full`, no swap.
- [ ] **Split-and-place** — `move {A (7 letters [ids 1..7]), count 3 → free cell}` → a new entry with a new entryId holding `[1,2,3]`, A keeps its id with `[4..7]`, one version bump per touched container; `count > total` → `bad_count`; `count` 0 or `== total` stays story 007's whole move and keeps its id (replace 007's "until 008" `bad_count` test). A split onto a mergeable stack is a partial merge of `min(count, room)` with no new entry. If the mail-id arena cannot reserve the new block, or the entry pool is full / out of ids, → `capacity_exhausted` and the entry stays where it is (on the cursor for a place; Evan ruling 2026-10-22 5:11 AM PT, as AutoPlace and `TryInsert`).
- [ ] **H-R7-3** — T = 18 letters to #5, S = 5 to #5: `merge {S → T}` → T 20, S 3 in place, both versions +1 (same container: +1 once); again → `stack_full`; S' = 5 to #6 onto T → `blocked`.
- [ ] **H-R4-3 c** — `merge {A → A}` → `blocked`, A unchanged, version unchanged.
- [ ] **H-R6-1 b (op form)** — `merge {A [4,9,15,21,30] → B [2,50]}` → B `[2,4,9,15,21,30,50]`, A removed; every destination list ascending, no duplicate id.
- [ ] **Merge precedence** — `bad_request → not_open (from and to) → stale_entry (entry, then target; an entry not in `from` or a target not in `to`) → not_allowed → blocked (self, StackKey mismatch, effMax 1) → stack_full (room = min(effMax, 255) − target.count ≤ 0) → capacity_exhausted (union block)`; a parametrised test asserts the earliest code.
- [ ] **Rejects raise nothing (TD-105 item 5)** — every rejected split, swap or merge leaves only the `OpReply` (no record, no seal) and changes nothing.
- [ ] **Indexes and weight** — a split raises the owner's P by the new entry's classPts (PerEntry, even inside one container); a merge that empties its source lowers it; a swap nets zero; `CountItem` and the StackKey index match a recount.
- [ ] **0 B** — a split / swap / merge / reject cycle allocates 0 B per op after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: extend `MoveHandler.cs`; add `MergeHandler.cs`.
- Replace `MoveHandler`'s `p.Count != 0 && p.Count != e.Count` → `bad_count` branch with three arms: `> total` → `bad_count`; 0 / `== total` → whole move; `1 ≤ count < total` → split. The split branch calls `PlacementRules.Validate(..., wholeMove: false)`, and the merge arm uses the requested count, not `e.Count`.
- Split-and-place: Validate plans `AddAcquiredBlock(k)` for mail, runs `CheckArenaReservation`, and pre-checks the entry pool (`LiveCount < Capacity`, `LastId < MaxRealId`), because Commit never fails. Commit uses a **new `InventoryMutator` split primitive** (no handler writes entries directly, ADR-0009): allocate the new entryId (in Commit, never Validate — ADR-0009 §2), create the k-lowest-id block before taking it (the order `MergeInto` uses), take via `TakeOldest` (story 012; mail and items), raise `InvSplitFrom`, then `Place`. Keep a preallocated id buffer (as `_movedIds`) for 0 B.
- Swap: only when the verdict is `blocked` on a `layout = slots` destination (hotbar), the source is the actor's cursor, the move is whole, and one non-mergeable owner holds the cell (read it with `Occupancy.OwnerAt`). A same-key full slot is `stack_full` first; slot 0 is `reserved_slot` before anything else; a split or a non-cursor source stays `blocked`; a displaced entry that fails the cursor whitelist stays `blocked` (unreachable with the M-1a defs). Commit: `Remove(displaced)`, `Transfer(cursor → slot)`, `Place(displaced → cursor)`, in that order.
- `MergeHandler` copies `MoveHandler`'s 255 room cap (`Math.Min(v.EffMax, byte.MaxValue)`). The same cap is missing in `AutoPlace` (TD-151, recorded by this story; the content-side root is TD-126).
- Bind `InvOpDescriptors.Merge` in `InventorySystem.Bind` beside Close/Pick/Move; add Merge to `BoundBySystem` in `tests/unit/inventory/InvMalformedOpTests.cs` and move its `ValidCases` row into `MoveSplitMergeTests` (an unplaced entry → `stale_entry`).
- Rig: `InvAutoPlaceRig` (`HotbarOf`, `CursorOf`, `PlaceMail` with explicit ascending ids, `PlaceItem`); `PlacementFixture` is rules-only.
- Performance: Validate O(footprint cells); Commit O(k) id copy; probe counters unchanged.
- The standalone `split` op (cursor-lift with temp id) is M1 (epic scope) — not this story.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- M1: `Split` op, temp ids, `MoveGroup`.

---

## QA Test Cases

*No qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/MoveSplitMergeTests.cs` — must exist and pass
- Allocation: `tests/unit/inventory/MoveSplitMergeAllocationTests.cs` — `AllocAssert.Zero` plus the every-build twin (pattern: `PickMoveAllocationTests.cs`)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003, Story 007 (Complete, PR #31), Story 009 (`InventoryMutator.MergeInto` and the StackKey index hooks), Story 012 (`TakeOldest`)
- Unlocks: Story 010, Story 018
