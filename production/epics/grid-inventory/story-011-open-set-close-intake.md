# Story 011: Open-set bookkeeping, Close op, leash put-away hook — and the Intake as the M-1a external

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Complete, PR #30)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-001` (Close through the FIFO); open-set consistency rule of GDD R10
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0008: Event Log, Replication Classes and Event Delivery
**ADR Decision Summary**: A player's open set = own panel + cursor + at most one external container; opening another closes the first; ops on containers outside the set → `not_open` (decided during validation); external containers' records are `Targeted` to their subscriber set (= the players that have them open) resolved at raise time (ADR-0009 §2, §4 rule 1; GDD R10).
**ADR Version**: ADR-0009 2026-10-16; ADR-0008 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: audience resolved at raise time from the container's subscriber set; panel and cursor records are `Targeted` with exactly the owner's bit (as built, TD-114) (ADR-0009 §4)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Re-checked against manifest 2026-10-17.1: no rule cited here changed.

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R10-2** — GIVEN A has external C1 open, WHEN A's open set gains C2 (through the internal `TryOpenExternal(slot, c)` used by the range-gated `Open` handler of story 019), THEN C1 is closed for A (`IsInOpenSet(A, C1)` false, A's subscriber bit cleared; a later `Close {C1}` from A → `not_open`) and B, who also has C1 open, is unaffected (`IsInOpenSet(B, C1)` true, B's bit kept); `IsInOpenSet(B, A.inventory)` is false. The `move {from: A.inventory}` → `not_open` reply runs in story 007's precedence test (no `move` handler before 007).
- [x] `Close {container}` op removes the external from the actor's set (no range gate, no record, no version bump); closing a container not in the actor's open set → `not_open`, nothing changes (GDD state table; shipped `InvOpDescriptors.Close`); id 0 / above high-water → `bad_request`; tombstoned id → `container_gone`.
- [x] `TryOpenExternal` returns false (nothing changes) for a dead id or a container with an owner slot; re-opening the current external returns true and changes nothing. Own panel and cursor are always in their owner's set, never in another slot's.
- [x] `OnPlayerLeft(slot)` clears the slot's external and its subscriber bit (no event, no version; GDD l.194); a rejoin starts with no external.
- [x] **Leash put-away hook (H-R10-1 put-away clause)** — `CloseExternal(slot, cause: leash)` closes the external and, in the same call, puts a cursor entry away into the panel by the R9 cursor row (AutoPlace); with a full panel it stays on the cursor and the external still closes. M-1a cursor row: Tool/Weapon/Consumable → `[hotbar, player_inventory]`, every other category (mail included) → `[player_inventory, hotbar]` (no backpack at M-1a). The put-away is one mutator bracket sealed with the slot's player as actor. (The leash *distance* trigger is story 019.)
- [x] Subscriber sets drive event audiences: after A opens the Intake, a spawn into the Intake reaches A's records; after A closes it, A receives no Intake records (group counts for non-openers are M0).
- [x] `TryOpenExternal`, `CloseExternal` (with and without a cursor entry) and the `Close` op allocate 0 B per call after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/OpenSet.cs` holds only the per-slot external `ContainerId` (8 entries, none allowed) and `IsInOpenSet(PlayerSlot, ContainerId)` (story 007's predicate). The per-container subscriber bitmask already exists (`ContainerSubscribers.cs`, story 005): `TryOpenExternal`/`CloseExternal` call its `Add`/`Remove`; do not duplicate it.
- Signatures (internal, story 019 calls them): `bool TryOpenExternal(PlayerSlot, ContainerId)`, `void CloseExternal(PlayerSlot, CloseCause)`. Outside an op, a mutating `CloseExternal` (put-away) takes the TickGuard, its own event scope and one `Begin/End` bracket, as the `IInventoryApi` calls do; inside the `Close` handler it uses the op's scope. `CloseExternal` stays off `IInventoryApi` for now (ADR-0014 §6 knockdown chain promotes it later; TD-149).
- The `Close` handler is the first production inventory op bound in `InventorySystem.Bind`; it resolves the actor's slot from `Op.Actor` through `ISlotView`. Update the test rig `InventoryWithStubOps` (`tests/unit/inventory/InvMalformedOpTests.cs`) to skip kinds the system now binds (`DuplicateOpHandler`).
- The cursor row lives in a new `src/PerformativeMail.Sim/Inventory/Placement/TargetOrder.cs` (cursor row only; story 010 adds the rest).
- Open sends no full-state snapshot at M-1a (no `BindSnapshotSource`; the network epic owns it). The per-slot external is not in the canonical dump yet: story 017's section adds it, and the masks are rebuilt from it on load (TD-146 list).
- **Decision (recorded in `production/session-logs/decision-log.md`) — M-1a Intake access:** `po_intake` is the solo greybox's **one external container** under GDD R10 (its console is the interaction anchor). At M-1a the entry-level ops apply to it unchanged: `pick`, `move`, `merge`, `quick_move {entryId}`, `return`. It is **drawn with the grid view** until the manifest view lands. D7 would flip it to manifest (320 > 160 cells), but R18 is M1 and the panel stories are Blocked anyway. Still deferred:
  - `MoveGroup`/`QuickMoveGroup`/`TakeAll` (M1, R18);
  - `GroupCounts` for non-openers (M0);
  - contention UI (M0).

  This follows R10 (any external, the Intake named) and R18 ("any op still requires `open`"). It adds no new mechanic. The UI phase tag "external containers M0+" is pulled forward for the Intake only.
- The range-gated `Open` OpKind handler (P13 chain, 2.5 m + 0.5 m slack) and the 3.0 m leash trigger need Player Character's interaction framework (ADR-0014, Accepted 2026-10-16) → story 019 (Ready; waits on player-character 019 and story 014). This story exposes `TryOpenExternal` and `CloseExternal` for it; tests call them directly.
- Mounted cargo as an open external is M-1b (vehicles).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 019: range-gated `Open`/`Pickup` handlers and the leash distance check.
- Story 007: the `move`/`pick` handlers and their `not_open` precedence leg.
- M0: `GroupCounts`, contention chips, optimistic client.

---

## QA Test Cases

*No qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.) Cases: idempotent re-open; opening C2 moves A's bit only; B's bit untouched; panel/cursor refused by `TryOpenExternal`; put-away of a tool vs a letter; full panel keeps the cursor; PlayerLeave clears.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/OpenSetTests.cs` — must exist and pass
- Allocation: `tests/unit/inventory/OpenSetAllocationTests.cs` — `AllocAssert.Zero` plus the every-build twin (pattern: `IntakeAllocationTests.cs`)
- Leash put-away: `tests/unit/inventory/OpenSetPutAwayTests.cs` (AC 5)

**Status**: [x] Created and passing (4 legs green: net10.0/net8.0 × Debug/Release)

---

## Dependencies

- Depends on: Story 004, Story 005, Story 009 (AutoPlace, Complete)
- Unlocks: Story 007, Story 010, Story 019, Story 024

---

## Fix and Mail Lead planning defaults

- **Fix (matches the GDD and the shipped descriptor):** `Close` on a container that is not in the actor's open set → `not_open`, with nothing changed. This matches the GDD state table and `InvOpDescriptors.Close`. No ADR change.
- **Mail Lead planning defaults, 2026-10-22, pending Evan's confirmation. These are not Evan's rulings:**
  - (a) `CloseExternal` stays internal, off `IInventoryApi`. It is promoted when the ADR-0014 §6 knockdown chain needs it. Logged as TD-149, with no ADR edit.
  - (b) In 011, a player leaving clears their open external and their subscriber bit (GDD l.194). The cursor on disconnect stays M0.

---

## Completion Notes

**Completed**: 2026-10-22 (PR #30, branch `feat/grid-inventory-011`, base main `bf1d299`)
**Criteria**: 7/7 passing.

**What was built**
- `Inventory/OpenSet.cs` holds the 8 per-slot externals, `IsInOpenSet` and `CloseCause { Close, Leash }`. The subscriber masks stay in `ContainerSubscribers` (story 005) and are not duplicated.
- `InventorySystem` has two internal members:
  - `TryOpenExternal(PlayerSlot, ContainerId)`
  - `CloseExternal(PlayerSlot, CloseCause)`
- The production `Close` handler is bound in `Bind`, the first production inventory op. It checks in this order: `CheckRequest` (`bad_request`), then `container_gone`, then `not_open`. It writes no record and bumps no version.
  - Closing the actor's own panel or cursor returns `Ok` with nothing changed. The AC does not state this case.
- `OnPlayerLeft` clears the slot's external and its subscriber bit.
- `InventoryWithStubOps` skips the kinds the system binds (`Close`).
- The leash put-away lives in `CloseExternal(slot, Leash)`:
  - The external closes first. Then the cursor entry is planned by the `Placement/TargetOrder.cs` cursor row with `AutoPlace.Plan`, which runs the merge pass first.
  - The commit is one `Begin/End` bracket in its own event scope, sealed with the slot's player as actor.
  - A full panel or a refused arena reservation keeps the entry on the cursor.
  - `CloseCause.Close` leaves the cursor alone (GDD R11).
- No new SimAssertCode or CounterId. TD-149 is added, and TD-146 is extended with the open-set and mask rebuild on load.

**Tests**
- 44 new test cases:
  - `OpenSetTests`: 15
  - `OpenSetPutAwayTests`: 27 (11 facts plus 4 theories with 16 cases)
  - `OpenSetAllocationTests`: 2
- The suite total went from 5351 to 5394 (+43), because the `Close` row left `InvMalformedOpTests.ValidCases`, since its real handler is covered by `OpenSetTests`.

**Code review (`/code-review`, Sonnet):** APPROVE, no blocking findings. The advisories, not fixed here:
1. `PutAwayCursor` always opens its own event scope and bracket, even inside an op. No op path reaches it today: the Close op uses `CloseCause.Close`. Story 019 should confirm which scope it expects for an in-op leash.
2. The `container_gone` test fakes an issued-but-dead id by setting the private `_nextId` through reflection. No container-destroy path exists before M-1b. The dead-external branches of `ClearExternal` and `ApplyClose` are unexercised until tombstones land.
3. (Owner question, from the implementer.) In an arena-exhausted state, a letter whose merge block can't be reserved stays on the cursor even when a free cell exists, because the merge pass plans first. This matches AutoPlace and `TryInsert`.
