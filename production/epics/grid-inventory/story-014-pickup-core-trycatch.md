# Story 014: Pickup core (AutoPlace by the R9 pickup rows) and the TryCatch API with fumble

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: L (~1.5–2 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (readiness fixes against main f504231, 007/008/010 merged: partial-pickup AC, M-1a `entryId` form (heap selection deferred, TD-143), `unavailable` as a core verdict, `FlightPayload`/`CatchResult` shapes and fumble refusals, rejects/versions/weight/0 B ACs; Mail Lead planning defaults pending Evan, TD-154. Before that: tombstone AC, Review advisory A5)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-007` (TargetFirst AutoPlace), `TR-inventory-013` (WorldItem cap and expiry; the pickup race itself is GDD R12 l.137 and edge case l.363)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: Pickup = AutoPlace one entry into the panel by R9's pickup row (mail: `[hotbar, inventory, backpack]` `TargetFirst`; non-mail: category order), partial allowed, `no_room` if nothing fits; two pickups of one entry in a tick: first arrival wins, second `container_gone` with `byActorId`; `TryCatch(slot, flight) → {unitsIn, unitsFumbled}`: oldest units go in, the newest that fit nowhere become one WorldItem (`origin = Thrown`) at the catcher's feet; `unitsIn = 0` is a fumble — never the cursor (ADR-0009 §1, Key Interfaces; GDD R9, R12).
**ADR Version**: ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: boundary crossings into/out of Flight happen only through `IInventoryApi` (ADR-0009 §6)
- Re-checked against manifest 2026-10-17.1: no rule cited here changed (that revision touched only ADR-0012 §7 and ADR-0026 rules).

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R12-1 (core form)** — one Small WorldItem, players A and B with room, both pickups applied in one tick, A first → A gains it, B gets `container_gone, byActorId = A`, cause `taken` (the tombstone A's removal wrote, age 0), exactly one copy exists.
- [ ] **Partial pickup (GDD R12 l.137, edge case l.363)** — a 5-letter mail WorldItem and a panel with room for 3 (one hotbar stack to the same address at 17/20, nothing else free): the 3 oldest merge into the hotbar stack; the WorldItem survives under the same `worldItemId` and `entryId` with the 2 newest, `expiresTick`, position and FIFO node unchanged; one `WorldItemEntriesChanged {id, next worldSeq, entryId, count 2}` is raised (no `WorldItemDespawned`, no tombstone); hotbar +1 only; reply `ok`, requested 5 / got 3. A second pickup by B in the same tick takes the 2 and leaves the tombstone (`byActorId = B`, `taken`). 5 mailIds in total, none duplicated. On a whole pickup `WorldItems.Remove` runs before the AutoPlace commit (the mutator frees a merged-away source).
- [ ] **H-R9-2 a–e** — a caught 5 letters to #4 with an inventory stack of 3 to #4 at (0,0): a: hotbar slot 2 = 10 to #4 → merged into slot 2 (15), inventory untouched; b: no #4 hotbar stack, slots 1 and 3 free → new entry in slot 1 (never 0); c: hotbar full, no #4 stack → merged into the inventory stack (8); d: hotbar and inventory full and the inventory #4 stack at 20/20, cursor empty → one WorldItem `origin = Thrown` at the catcher's feet, `expiresTick = tick + 9000`, cursor stays empty, no unit lost, `{0, 5}`; e: room for 3 (one hotbar stack at 17/20) → the 3 oldest merge, the 2 newest become the WorldItem, `{3, 2}`. (Backpack rows apply once it exists.)
- [ ] A caught Axe with room → hotbar then inventory (category order, `TargetOrder.CursorRow`, PassFirst); a pickup of a mail WorldItem behaves as a–c, and returns `no_room` instead of d (the item stays, nothing bumps).
- [ ] **Floating item and `entryId` (M-1a form)** — a pickup of a floating (water) WorldItem is refused by the core with a core-level verdict (no `InvReason`; story 019's anchor maps it to Player Character P13's `unavailable` before the core runs), nothing mutated, nothing bumped. `entryId` 0 or the WorldItem's own entry id → the pickup proceeds; a well-formed id that is not this WorldItem's entry (live elsewhere, or dead) → `stale_entry`, nothing mutated (namespace and high-water failures stay `bad_request`). Heap selection (several entries, default the lowest entryId, GDD l.101) is deferred to M-1b with `Spill` (TD-143 item 3): `WorldItemRecord` holds one entry, so no fixture heap at M-1a.
- [ ] **Pickup naming an expired or destroyed WorldItem (tombstone; Review advisory A5)** — a WorldItem removed at tick T (mail expired to the Intake → cause `expired`; non-mail destroyed by expiry, water or cap eviction → cause `destroyed`; both by the system actor) and a pickup op naming its id: at T + 900 the tombstone is alive and the op gets `container_gone` with `byActorId` = system and that cause; at T + 901 it is dead and the op gets `stale_entry` (30 s = `Tombstones.LifetimeTicks` = 900 ticks, alive while `now − T ≤ 900`, as `Tombstones.TryResolve` on main does). Nothing is mutated and no version bumps in either case. Also a WorldItem picked up by A at T: B naming it at T + 900 gets `container_gone, byActorId = A`, cause `taken`; at T + 901 `stale_entry`.
- [ ] **Catch refusals (Mail Lead planning default pending Evan, TD-154)** — (i) the plan leaves units unplaced and the WorldItem table is full → `TryCatch` refuses the whole catch in Validate with `Reason = capacity_exhausted`, `{0, 0}`, nothing mutated, the entry stays live and detached (Throwing's, as `CreateWorldItem`'s `None` contract), one `CheckCode.InventoryWorldItemCapacity` record; (ii) a mail merge's union block fails `CheckArenaReservation` → the whole catch is a fumble `{0, N}` with `Reason = capacity_exhausted` (the entry keeps its own block, so the WorldItem needs no new block); (iii) the fumble's WorldItem creation runs story 013's cap rule: at 512 live items exactly one eviction happens by 013's rules. Never the cursor; no unit lost in any case.
- [ ] **Rejects raise nothing (TD-105 item 5)** — every refused pickup (`bad_request`, `container_gone`, `stale_entry`, floating, `no_room`, `capacity_exhausted`) and every refused catch leaves no record and no seal; versions unchanged.
- [ ] **Versions, events, weight, indexes** — a pickup or catch bumps each panel container it changed by exactly +1, one `InvVersion` seal after that container's records (a WorldItem has no version). Event order: a pickup raises its world record (`WorldItemDespawned` or `WorldItemEntriesChanged`) first, then the panel records and seals; a fumbling catch raises the panel records and seals first, then `WorldItemSpawned` in `CreateWorldItem`'s nested call scope. Weight P (PerEntry) rises by classPts only when a new entry is placed in the panel; a merge and the fumble add nothing. After an Axe pickup `CountItem` and the StackKey index equal a recount.
- [ ] **0 B** — pickup (whole, partial, `container_gone`, `stale_entry`) and catch (merge, place, fumble) cycles allocate 0 B per call after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/World/PickupCore.cs`; `TryCatch` declared on `IInventoryApi` (`IInventoryApi.cs`) and implemented in `InventorySystem.cs`.
- **Target rows** (TD-153 item 4): add `TargetOrder.PickupRow(hotbar, playerInventory, targets)` writing `[hotbar, player_inventory]` (backpack skipped, so `MaxRowLength` stays 2), run `PlaceOrder.TargetFirst` with `MergePolicy.Merge` for mail pickup and catch; a non-mail pickup or catch uses `TargetOrder.CursorRow` with `PassFirst`. `CursorRow` and `QuickMoveRow` stay unchanged. `RunTargetFirst` (`Placement/AutoPlace.cs`) already merges into a matching hotbar stack (oldest ids, R6), else the lowest free slot ≥ 1, then the inventory; a detached source plans partial merges and a whole free placement of the remainder. Pickup and catch never touch the cursor or its `CursorOrigin` (GDD R9 "never the cursor").
- `PickupCore.Apply(actor, worldItemId, entryId)` contains everything after the range gate; the `Pickup` **OpKind handler** that runs Player Character's P13 chain first is story 019 (Ready; waits on player-character 019 for `IPlayersApi.CheckInteraction`). Core chain (Mail Lead planning default, TD-154): `bad_request` (payload, entry namespace, WorldItem id not of class WorldItem or never allocated — `WorldItems.Resolve`) → `container_gone` (tombstone alive: `byActorId`, cause) → `stale_entry` (dead tombstone, or an `entryId` that is not this item's) → floating (core verdict, no `InvReason`) → `no_room` → `capacity_exhausted` last (`CheckArenaReservation`, as `MoveHandler`). `not_allowed`/`not_carryable` are unreachable at M-1a (every WorldItem entry came from a hand-carried container or a mail spawn): assert under `INVENTORY_ASSERT`, Release answers `no_room`; 019 declares them on Pickup if ever needed. The core returns the reason plus `byActorId` and cause; the test handler writes them into `InvReplyExtra`. Seals: actor = the picker's player entity, cause `None`; the tombstone: `byActorId` = the picker, cause `Taken`.
- **Partial pickup**: `WorldItems` gains one primitive, `RaiseEntriesChanged(slot)` (next `worldSeq`, replicated, no version), called after a commit that left units on the WorldItem's entry; a whole pickup calls `WorldItems.Remove(id, actor, Taken)` first.
- **Test-only handler**: `PickupCatchTests` binds `InvOpDescriptors.Pickup` to a test handler over `PickupCore` in its own rig (built like `InvFixtureActionHandler`, `tests/unit/inventory/InventoryEventRig.cs`), never in `InventoryWithStubOps` (that would throw `DuplicateOpHandler`). `InventorySystem.Bind`, `BoundBySystem` and `InvMalformedOpTests` stay unchanged; production binds Pickup in story 019. Tests call the core directly and through the test handler.
- `TryCatch` is called by Throwing (flight resolution, ADR-0015 Accepted; the flight protocol ADR-0021 is not written) — Throwing owns the call site. Shapes (Mail Lead planning defaults for ADR-0009 Key Interfaces names, TD-154, folded into the TD-144 as-implemented note): `readonly record struct FlightPayload(EntryId Entry, Vec3Mm CatcherPosMm, bool OnWater)` — the entry is the live detached entry `DetachForThrow` left, mailIds still in the arena, so the payload carries no ids or units; `readonly record struct CatchResult(byte UnitsIn, byte UnitsFumbled, InvReason Reason)` — Reason 0 on a catch or a completed fumble, `bad_request` for an entry that is not live and detached, is in a WorldItem, or outside a tick phase, `capacity_exhausted` per the refusals AC. The fumble is `CreateWorldItem(Entry, CatcherPosMm, Thrown, OnWater, createdTick = now)`, so `expiresTick = now + 9000` and the entry keeps its id.
- Ledger: 014 adds no ledger calls. Story 017 (which depends on 014) retrofits the ledger inside the mutator primitives; leave `// story 017 ledger hook` comments at the Flight → Placed (catch) and World → Placed (pickup) crossings, as `ConsumeUnit` and `DetachForThrow` do; the Flight → World crossing is `CreateWorldItem`'s existing hook.
- Performance: no new gate; AutoPlace probes count per cell (H-PERF-6, TD-134); 0 B per call.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 019: range-gated `Pickup`/`Open` handlers (Ready; the P13 chain, including `unavailable`).
- M-1b: Spill-heap entry selection (TD-143 item 3).
- Story 017: the ledger calls.
- throwing-catching epic: the catch decision and `FlightPayload` producer.

---

## QA Test Cases

*No qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/PickupCatchTests.cs` — must exist and pass
- Allocation: `tests/unit/inventory/PickupCatchAllocationTests.cs` — `AllocAssert.Zero` plus the every-build twin (pattern: `PickMoveAllocationTests.cs`)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009, Story 010 (target orders), Story 013 — all Complete and merged (010 in PR #33, main f504231)
- Unlocks: Story 017, Story 018, Story 019; throwing-catching epic

---

## Mail Lead planning defaults (pending Evan)

These are **Mail Lead planning defaults, 2026-10-22, open for Evan's confirmation — not Evan's rulings**. The story is built on them; TD-154 tracks them. None contradicts an Accepted ADR; each follows TD-150, TD-152 and TD-153.

1. Mail pickup and catch use a new `TargetOrder.PickupRow` = `[hotbar, player_inventory]`, `TargetFirst`, Merge; non-mail pickup and catch reuse `CursorRow`, `PassFirst` (TD-153 item 4).
2. A partial pickup leaves the WorldItem alive with the newest units under the same `worldItemId` and `entryId` (`expiresTick`, position and FIFO node unchanged) and raises `WorldItemEntriesChanged`; only a whole pickup removes it and writes the `taken` tombstone.
3. At M-1a `entryId` is 0 or the item's own entry id; any other well-formed id → `stale_entry`. Heap selection waits for M-1b `Spill` (TD-143 item 3).
4. A floating item is refused by the core with a core-level verdict, not an `InvReason`; story 019 maps it to P13's `unavailable` (ADR-0009 l.115). No Pickup descriptor change.
5. Core chain: `bad_request → container_gone → stale_entry → floating → no_room → capacity_exhausted` (`container_gone` in id resolution, ADR-0009 l.116); `not_allowed`/`not_carryable` unreachable at M-1a (assert; Release `no_room`).
6. `FlightPayload(EntryId Entry, Vec3Mm CatcherPosMm, bool OnWater)` and `CatchResult(byte UnitsIn, byte UnitsFumbled, InvReason Reason)`; the fumble keeps the flight entry's id and is created at the catch tick (`expiresTick = now + 9000`).
7. Catch refusals: WorldItem table full → the whole catch refused in Validate (`capacity_exhausted`, `{0, 0}`, nothing mutated, entry stays detached, one check record); a mail merge's union block refused by the arena → the whole catch fumbles (`{0, N}`, `capacity_exhausted`), the pattern of Evan's 2026-10-22 5:11 AM PT ruling (the entry is not split onto a place it cannot fully go), which itself covers only a leashed letter on the cursor.
8. Event order: a pickup raises its world record first, then panel records and seals; a fumbling catch raises panel records and seals first, then `WorldItemSpawned` in `CreateWorldItem`'s nested scope. Seal actor is the picker's or catcher's player entity, cause `None`.
