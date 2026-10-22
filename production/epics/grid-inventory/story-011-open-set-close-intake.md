# Story 011: Open-set bookkeeping, Close op, leash put-away hook — and the Intake as the M-1a external

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (readiness fixes)

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

- [ ] **H-R10-2** — GIVEN A has external C1 open, WHEN A's open set gains C2 (through the internal `TryOpenExternal(slot, c)` used by the range-gated `Open` handler of story 019), THEN C1 is closed for A (`IsInOpenSet(A, C1)` false, A's subscriber bit cleared; a later `Close {C1}` from A → `not_open`) and B, who also has C1 open, is unaffected (`IsInOpenSet(B, C1)` true, B's bit kept); `IsInOpenSet(B, A.inventory)` is false. The `move {from: A.inventory}` → `not_open` reply runs in story 007's precedence test (no `move` handler before 007).
- [ ] `Close {container}` op removes the external from the actor's set (no range gate, no record, no version bump); closing a container not in the actor's open set → `not_open`, nothing changes (GDD state table; shipped `InvOpDescriptors.Close`); id 0 / above high-water → `bad_request`; tombstoned id → `container_gone`.
- [ ] `TryOpenExternal` returns false (nothing changes) for a dead id or a container with an owner slot; re-opening the current external returns true and changes nothing. Own panel and cursor are always in their owner's set, never in another slot's.
- [ ] `OnPlayerLeft(slot)` clears the slot's external and its subscriber bit (no event, no version; GDD l.194); a rejoin starts with no external.
- [ ] **Leash put-away hook (H-R10-1 put-away clause)** — `CloseExternal(slot, cause: leash)` closes the external and, in the same call, puts a cursor entry away into the panel by the R9 cursor row (AutoPlace); with a full panel it stays on the cursor and the external still closes. M-1a cursor row: Tool/Weapon/Consumable → `[hotbar, player_inventory]`, every other category (mail included) → `[player_inventory, hotbar]` (no backpack at M-1a). The put-away is one mutator bracket sealed with the slot's player as actor. (The leash *distance* trigger is story 019.)
- [ ] Subscriber sets drive event audiences: after A opens the Intake, a spawn into the Intake reaches A's records; after A closes it, A receives no Intake records (group counts for non-openers are M0).
- [ ] `TryOpenExternal`, `CloseExternal` (with and without a cursor entry) and the `Close` op allocate 0 B per call after warm-up.

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 005, Story 009 (AutoPlace, Complete)
- Unlocks: Story 007, Story 010, Story 019, Story 024
