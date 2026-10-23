# Story 008: Move split-and-place, hotbar swap from the cursor, and the Merge op

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Evan's approval of 2:24 PM PT, confirmed in scope 3:00 PM PT recorded, docs only: the Mail Lead planning defaults below are now Evan's rulings). Before that: 2026-10-22 (implemented, PR #32; readiness fixes against story 007 as merged)

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

- [x] **H-R4-2 c/d** — c: A = 10 letters at (0,0), `move {A, count: 4 → (0,0)}` (split onto own cells) → `blocked`; d (swap): hotbar slot 1 = Axe, cursor = 3 letters, `move {cursor → hotbar, x: 1}` → `ok`, slot 1 = the letters, cursor = the Axe, hotbar +1 and cursor +1 (one bump per touched container, the story 007 / TD-150 item 3 pattern), no other container; the same onto an occupied **grid** cell → `blocked` (no grid swap); slot 1 = 20 letters to #3 and the cursor 2 letters to #3 → `stack_full`, no swap.
- [x] **Split-and-place** — `move {A (7 letters [ids 1..7]), count 3 → free cell}` → a new entry with a new entryId holding `[1,2,3]`, A keeps its id with `[4..7]`, one version bump per touched container; `count > total` → `bad_count`; `count` 0 or `== total` stays story 007's whole move and keeps its id (replace 007's "until 008" `bad_count` test). A split onto a mergeable stack is a partial merge of `min(count, room)` with no new entry. If the mail-id arena cannot reserve the new block, or the entry pool is full / out of ids, → `capacity_exhausted` (reason code from ADR-0009 §2) and the entry stays where it is (on the cursor for a place, as AutoPlace and `TryInsert`). The entry staying put on a refused split, for the arena and for the pool, is approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (formerly a Mail Lead planning default, TD-152); it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed letter staying on the cursor when the mail-id arena is full.
- [x] **H-R7-3** — T = 18 letters to #5, S = 5 to #5: `merge {S → T}` → T 20, S 3 in place, both versions +1 (same container: +1 once); again → `stack_full`; S' = 5 to #6 onto T → `blocked`.
- [x] **H-R4-3 c** — `merge {A → A}` → `blocked`, A unchanged, version unchanged.
- [x] **H-R6-1 b (op form)** — `merge {A [4,9,15,21,30] → B [2,50]}` → B `[2,4,9,15,21,30,50]`, A removed; every destination list ascending, no duplicate id.
- [x] **Merge precedence** — `bad_request → container_gone (from and to; GDD l.379, H-X-4 l.817) → not_open (from and to) → stale_entry (entry, then target; an entry not in `from` or a target not in `to`) → not_allowed → blocked (self, StackKey mismatch, effMax 1) → stack_full (room = min(effMax, 255) − target.count ≤ 0) → capacity_exhausted (union block)`; a parametrised test asserts the earliest code.
- [x] **Rejects raise nothing (TD-105 item 5)** — every rejected split, swap or merge leaves only the `OpReply` (no record, no seal) and changes nothing.
- [x] **Indexes and weight** — a split raises the owner's P by the new entry's classPts (PerEntry, even inside one container); a merge that empties its source lowers it; a swap nets zero; `CountItem` and the StackKey index match a recount.
- [x] **0 B** — a split / swap / merge / reject cycle allocates 0 B per op after warm-up.

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

**Status**: [x] Created and passing — `MoveSplitMergeTests.cs` 38 methods / 225 cases; `MoveSplitMergeAllocationTests.cs` 2 methods / 2 cases (the 0 B test runs in Release only; the twin runs in every build). Suite 5586 → 5812 on every leg (net10.0/net8.0 × Debug/Release: Debug 5759 passed / 53 skipped / 0 failed; Release 5809 / 3 / 0).

---

## Dependencies

- Depends on: Story 003, Story 007 (Complete, PR #31), Story 009 (`InventoryMutator.MergeInto` and the StackKey index hooks), Story 012 (`TakeOldest`)
- Unlocks: Story 010, Story 018

---

## Approved defaults (Evan, 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; TD-152)

These were Mail Lead planning defaults of 2026-10-22; **Evan approved them on 2026-10-22 at 2:24 PM PT, confirmed in scope 3:00 PM PT** ("to all recommended decisions", Mail Lead chat), so they are now Evan's rulings:

1. **Merge precedence** uses the declared-reasons chain `bad_request → container_gone → not_open (from and to) → stale_entry (entry, then target) → not_allowed → blocked → stack_full → capacity_exhausted`, with a GDD errata note at `design/gdd/grid-inventory.md` l.103. `container_gone` sits before `not_open` per GDD l.379 / H-X-4 l.817; the Merge precedence AC above matches. Move, Pick and QuickMove keep `not_open` first (unchanged; logged follow-up in TD-152).
2. **A swap bumps each touched container once** (hotbar +1, cursor +1).
3. **A refused split (entry pool full / out of ids, or the mail-id arena cannot reserve the new block) → `capacity_exhausted`** (code from ADR-0009 §2), the entry stays put. Evan's 2026-10-22 5:11 AM PT ruling covers only a leashed letter staying on the cursor when the mail-id arena is full; a split follows the same pattern, as a default approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT.

---

## Completion Notes

**Completed**: 2026-10-22 (PR #32)
**Criteria**: 9/9 passing.
**Files**: `Ops/MoveHandler.cs` (three count arms, split-and-place, cursor → hotbar swap), new `Ops/MergeHandler.cs` (Validate / Commit, room cap `min(effMax, 255)`), `InventoryMutator.SplitAndPlace` (new primitive: new entryId allocated in Commit, k lowest mailIds, `InvSplitFrom`), `InvPlan` (`InvSplit` / `InvSwap`; pinned size 572 → 580), `InventorySystem.Bind` (Merge bound), `InventorySettings.EntryInitialLastId` (test seam, default 0), `InvMalformedOpTests` (Merge in `BoundBySystem`; its `ValidCases` row moved to `MoveSplitMergeTests`), `PickMoveTests` (the "until 008" `bad_count` test replaced by a 4-case split test).
**Tests**: `MoveSplitMergeTests` 38 methods / 225 cases (slice 1: 18 / 94; slice 2: 12 / 100; slice 3: 1 / 8; Mail Review advisories: 7 / 23); `MoveSplitMergeAllocationTests` 2 / 2; one `InvMalformedOpTests` case removed. Every theory runs from slot 0 and slot 1.
**Tech debt**: TD-150 corrected (GDD note l.344; item 4's `capacity_exhausted` is from ADR-0009; Evan's ruling covers only a leashed letter staying on the cursor when the mail-id arena is full), TD-151 added (AutoPlace has no 255 cap), TD-152 added (the defaults above).
**Review advisories (Sonnet /code-review, 11285de..ae6af53)**:
- B1 (pool-full case credited to Evan in the AC): the AC now credits `capacity_exhausted` to ADR-0009 and the stays-put behaviour on a split to a Mail Lead default (Mail Review B1, PR #32), since then TD-152 item 3, **Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT** (advisory fix, Mail Review PR #36 A11).
- A1 Move/Pick vs Merge `container_gone` order, A5 same-container merge into an `acceptsInsert: false` container answers `not_allowed`, A3 the `container_gone` test reflects on `ContainerTable._nextId` (guarded): recorded in TD-152.
- A4 `EntryInitialLastId` doc: corrected (no resume path yet; identical on every peer). Mail Review A1 then made it `internal`, and its init accessor throws `SimBuildException(InventorySettingsInvalid)` above `EntryPool.MaxRealId` (control manifest l.109), tested at the boundary. The `InventorySettings` class doc ("three keys", "capacities") is older wording, not touched.
- A6 the split arm against the 255 cap: covered by slice 3 (250 + 4 / 5 / 6, target at 255).
- A7 Validate purity is shown through the reject snapshots only (no direct purity test; Mail Review A4, skipped).

**Mail Review advisories (PR #32, at 2479970)**: A1 above; A3 tests for a split with exactly one id or one pool slot left, an arena with room for 1 unit but not 2, and Merge's union block needing a larger size class; A6 tests for a mail stack at the 255 cap (test content with letter `maxStack` 255; the built-in mail kinds stop at 40), a whole move (count 0 and the total) onto a stack of 250, and a cursor swap with count equal to the total; `capacity_exhausted` and `container_gone` rejects added to the 0 B cycle (now 10 ops); `Id` traits on the three reject/stale theories. A4 skipped.

