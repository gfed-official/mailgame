# Story 007: Pick and Move ops — whole-entry moves, cross-container atomicity, no-op moves

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Evan's approval of 2:24 PM PT, confirmed in scope 3:00 PM PT recorded, docs only: the Mail Lead planning defaults below are now Evan's rulings). Before that: 2026-10-22 (Complete, PR #31; readiness fixes; H-R10-2 move leg taken from story 011; TD-105 item 3 built on the Mail Lead default `count 0 = whole` per Accepted ADR-0009 §1, pending Evan)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-001` (ops through the one FIFO, phase 0), `TR-inventory-002`, `TR-inventory-006`
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0007: Op Queue, Drain and Replies
**ADR Decision Summary**: Each op is a handler `Apply = Validate → Commit → Reply` drained in phase 0; cross-container moves are all-or-nothing (destination fit computed in `Validate`, committed unchanged); a move onto the entry's own cells with the same rotation is a no-op (`ok:true`, no bump, no event) (ADR-0009 §2–§3; ADR-0007 validate-then-mutate).
**ADR Version**: ADR-0009 2026-10-16; ADR-0007 2026-10-16

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

- [x] **H-R1-2** — GIVEN an empty hotbar and an Axe on the cursor, WHEN `move {to: hotbar, x: 0}` → `reserved_slot`, Axe stays on the cursor, hotbar version unchanged; `move {x: 1}` → `ok`, Axe at slot 1.
- [x] **Pick** — `pick {from, entryId}` lifts the whole entry onto an **empty** cursor (`cursor_busy` otherwise, cursor and source unchanged); picking an entry outside the actor's open set → `not_open`.
- [x] **H-R4-1 a–f (op form)** — the story 006 arrangement (chest 8×4, Medium at (6,2), 4 letters to #3 at (0,0)) rebuilt in an op-driven rig and driven through `move` from the cursor: a → implicit merge, stack 6, cursor empty, chest +1; b `out_of_bounds`, c `blocked`, d `out_of_bounds`, e `blocked`, f `blocked`; every rejection leaves the chest version and the cursor unchanged.
- [x] **H-R4-2 a/b** — a: a Large at (0,0) moved to (1,0) (overlapping its own cells) → `ok`, version +1; b: a cross-container move of chest Medium E to inventory (6,0), where an Axe covers (6,0),(6,1) → `blocked`, entry unmoved, **both** versions unchanged.
- [x] **H-R4-3 a/b** — fixture: chest 8×4, A = 5 letters to #3 at (0,0). a: a drag-place of 2 cursor letters to #3 onto free (4,2) never merges (new entry at (4,2), A still 5, the chest bumps once, and the cursor once as the other touched container); b: `move {A → its own (0,0), rotated: false}` → `ok:true`, no version bump, no event.
- [x] **Precedence test, full placement chain (ADR-0009 Implementation Guidelines; moved from story 006 on 2026-10-21)** — a parametrised test sends a `move`/`place` request that violates each step of `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full` **and every later one** and asserts the earliest code (story 006 covers the rule-level suffix from `reserved_slot`); `bad_count` (count > total) sits right after `stale_entry`. The `reserved_slot` row runs on the hotbar with a non-cursor source (no 008 swap branch); `out_of_bounds` cannot co-occur on a slots layout (story 006 advisory), and grid defs cannot carry `reservedSlots`. Rows that need a `carryByHand: false` item or an `acceptsInsert: false` def use a custom `ContentSet` passed to `InvAutoPlaceRig(content:)`.
- [x] **H-R10-2 (move leg, from story 011)** — players A and B joined, A's inventory holds E: B's `move {from: A.inventory, entryId: E, to: B.inventory, x: 0, y: 0}` → `ok:false, not_open`, nothing changes; B's `pick {from: A.inventory, E}` → `not_open`; A's own move passes `not_open`. `not_open` checks both `from` and `to` (`IsInOpenSet`, story 011); an actor with no slot (System) has an empty open set.
- [x] **Rejects raise nothing (TD-105 item 5)** — every rejected `pick`/`move` leaves only the `OpReply` in the tick log (no record, no `InvVersion` seal), and a non-actor subscriber receives nothing.
- [x] **Count** — `count` 0 or `count == total` → whole move, the entry keeps its id; `count > total` → `bad_count`, nothing changes; `1 ≤ count < total` → `bad_count` until story 008 adds split-and-place (test marked "until 008").
- [x] **Indexes and weight** — after a chest → inventory move P rises by the entry's classPts, inventory → chest it falls, an in-container move leaves it unchanged; `CountItem` matches a recount.
- [x] **0 B** — a pick / move / no-op / reject cycle allocates 0 B per op after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ops/PickHandler.cs`, `MoveHandler.cs`.
- `Move` with `count = 0` or `count == total` (whole) is this story (ADR-0009 §1 "0 = whole"; GDD l.103/l.343 say `count = 0` → `bad_count`: GDD errata approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, rule-text fold pending, TD-105 item 3); `count < total` (split-and-place) and the hotbar swap are story 008 — keep one `MoveHandler` and add those branches there.
- "Place" is `move` from the cursor; there is no `place` kind. A `move` whose `to` is the cursor is an ordinary slots destination.
- Pick chain: `bad_request → not_open → stale_entry → cursor_busy`; commit `Transfer(entry, cursorSlot, 0, 0, false)`. A bulk / `carryByHand: false` pick is dormant at M-1a and undeclared on Pick (TD-105 item 4).
- Commit uses the existing primitives: cross-container = `InventoryMutator.Transfer` (Remove then Place), in-container = `Transfer`'s same-container path (it treats the source's own cells as free, so H-R4-2 a works, and returns before marking a no-op). Onto a mergeable stack = `MergeInto(min(count, effMax − target.count))`, with the block reserved in Validate (`plan.AddAcquiredBlock`); the remainder stays at the source with its id, a full target → `stack_full`. Weight, `StackIndex` and `ItemCountIndex` are kept inside the primitives; the handlers add nothing for them.
- Positional ops carry `baseVersion`; `stale_layout` iff `baseVersion < lastReplaceVersion` of the destination. `lastReplaceVersion` only changes on `sort` (M1), so at M-1a the check is present but never fires — test seam: write the rig's `Header(to).LastReplaceVersion = n` between ticks, then `move {baseVersion: n − 1}` → `stale_layout` and `{baseVersion: n}` → evaluated normally.
- Source resolution order: entry id → `CheckRequest` (`bad_request`) → `ResolveEntries` (`stale_entry`); a live entry not in `from` → `stale_entry`. Entry ids have no tombstone, and no container is freed at M-1a, so `container_gone` stays declared but unreachable (the container-tombstone step is M-1b, TD-136). Do not look container ids up in the WorldItem-keyed tombstone ring.
- The open-set predicate comes from story 011 (`IsInOpenSet(PlayerSlot, ContainerId)`, `Inventory/OpenSet.cs`); the actor's slot comes from `ISlotView` via `Op.Actor`; panel containers and the cursor are always in their owner's set.
- Bind `InvOpDescriptors.Pick` and `.Move` in `InventorySystem.Bind` beside 011's `Close`. Update `InventoryWithStubOps` (`tests/unit/inventory/InvMalformedOpTests.cs`) to skip Pick and Move too, and move its Pick/Move expectations into `PickMoveTests` (an allocated but unplaced entry → `stale_entry`; the fuzz counts stub plus handler calls).
- Rig: `InvAutoPlaceRig.CreateGrid(8, 4)` plus `PlaceMail`/`PlaceItem` (add a `CursorA` accessor) or `InvEventRig` (players A and B) — `PlacementFixture` is rules-only and cannot drive ops.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: split-and-place, hotbar swap, `Merge`.
- Story 010: `QuickMove`, `Return`.
- M1: `MoveGroup`, `Split`, `Sort`, `TakeAll`.

---

## QA Test Cases

*No qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/PickMoveTests.cs` — must exist and pass
- Allocation: `tests/unit/inventory/PickMoveAllocationTests.cs` — `AllocAssert.Zero` plus the every-build twin (pattern: `IntakeAllocationTests.cs`)

**Status**: [x] Created and passing (4 legs green: net10.0/net8.0 × Debug/Release)

---

## Dependencies

- Depends on: Story 004, Story 005, Story 006, Story 009 (`MergeInto`, StackKey index; Complete), Story 011 (open-set predicate, `Close` bound first)
- Unlocks: Story 008, Story 010, Story 018

---

## Approved defaults (Evan, 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT)

Items 1–3 were Mail Lead planning defaults of 2026-10-22; **Evan approved them on 2026-10-22 at 2:24 PM PT, confirmed in scope 3:00 PM PT** ("to all recommended decisions", Mail Lead chat), so they are now Evan's rulings. Item 4 is settled by an Evan ruling (his own answer in the Mail Review chat). All four are tracked in TD-150.

1. `count = 0` and `count == total` both move the whole entry, which keeps its id. This follows Accepted ADR-0009 §1 (TD-105 item 3). `count > total` → `bad_count`. A GDD errata note is added at the edge cases; the GDD rule text is unchanged and the ADR is not edited.
2. A partial-fit implicit merge moves `min(count, effMax − target.count)` units. The remainder stays at the source and keeps its id. A full target → `stack_full`.
3. A cursor → chest move bumps both containers, once each. This settles the wording of H-R4-3 a.
4. Existing behaviour from story 011, kept, and no longer a default — Evan ruling 2026-10-22 5:11 AM PT (Evan's answer to Mail Review in the Mail Review chat): on a full mail-id arena, a letter whose merge block can't be reserved stays on the cursor (`capacity_exhausted`), the same as AutoPlace and `TryInsert`.

---

## Completion Notes

**Completed**: 2026-10-22 (PR #31, branch `feat/grid-inventory-007`, base main `ffdf3c0`)
**Criteria**: 11/11 passing.

**Handlers**
- `Ops/PickHandler.cs` and `Ops/MoveHandler.cs` each have an explicit, named pure `Validate` and a separate `Commit` that never fails, as ADR-0009's Implementation Guidelines require. They do not follow 011's single-method `ApplyClose`.
- `Validate` touches both containers and, for a merge, reserves its block (`plan.AddAcquiredBlock`).
- `Commit` applies `Transfer` or `MergeInto` exactly as planned.
- `InvPlan` gains an `InvMerge` field. The size pinned in `AutoPlaceTests` grows from 568 B to 572 B.
- Pick and Move are bound beside `Close`. `InventorySystem.ActorSlot` now serves all three handlers.

**Reason chains**
- Pick: `bad_request → not_open → stale_entry → cursor_busy`.
- Move: `bad_request → not_open → stale_entry → bad_count → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`, plus `capacity_exhausted` when a merge can't reserve its arena block.
- `not_open` checks both `from` and `to`. Pick checks `from` and the actor's cursor.

**Implementation choices beyond the defaults**
- An entry count can't exceed 255, so merge room is capped there. A target already at 255 gets `stack_full`.
- Picking the cursor's own entry gets `cursor_busy`.
- `container_gone` stays unreachable at M-1a: a dead id fails `not_open` first.

**Test rig**
- `InventoryWithStubOps` now skips Pick, Move and Close. The malformed-op fuzz counts stub calls and real-handler calls exactly.
- The Pick and Move rows left `ValidCases`. Unplaced-entry expectations became `stale_entry`.

**New tests: 47 methods, 195 cases**
- `PickMoveTests`: 45 methods, 193 cases. `PickMoveAllocationTests`: 2.
- Every core theory runs from slot 0 and from slot 1.
- Boundaries tested:
  - count 0, 1, total−1, total, total+1 and 255;
  - merge room exactly full, one short and one over, including the 255 cap;
  - `stale_layout` at n−1 and n.
- Every reject asserts the full side-effect snapshot:
  - no record and no seal;
  - headers, occupancy and entries;
  - the arena, the check ring and the id allocators;
  - the negative-fit cache;
  - P and `CountItem`.
- The suite grew from 5394 to 5586 (+192). Slice 1 removed one `InvMalformedOpTests` fact and the two Pick/Move `ValidCases` rows.

**Code review** (`/code-review`, Sonnet): APPROVE, no blocking findings. Advisories not fixed here:
1. R7 (l.103) has no pointer to the errata note at l.344.
2. `MoveHandler` passes `wholeMove: true` on cross-container moves, but the `PlacementRules` doc says "in-container". This is harmless.
3. The 572 B `InvPlan` size is pinned by a test.
