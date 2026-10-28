# Story 014: Pickup core (AutoPlace by the R9 pickup rows) and the TryCatch API with fumble

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: L (~1.5–2 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Evan's approval of 2:24 PM PT, confirmed in scope 3:00 PM PT recorded, docs only: the Mail Lead planning defaults below are now Evan's rulings). Before that: 2026-10-22 (Mail Review r1 fixes: B1 arena-refused catch is a refusal with an always-on record per ADR-0009 §5 l.157, B2 core precedence test, advisory tests; before that story-done: Complete, slices 1–5; before that readiness fixes against main f504231, 007/008/010 merged: partial-pickup AC, M-1a `entryId` form (heap selection deferred, TD-143), `unavailable` as a core verdict, `FlightPayload`/`CatchResult` shapes and fumble refusals, rejects/versions/weight/0 B ACs; Mail Lead planning defaults pending Evan, TD-154. Before that: tombstone AC, Review advisory A5)

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

- [x] **H-R12-1 (core form)** — one Small WorldItem, players A and B with room, both pickups applied in one tick, A first → A gains it, B gets `container_gone, byActorId = A`, cause `taken` (the tombstone A's removal wrote, age 0), exactly one copy exists.
- [x] **Partial pickup (GDD R12 l.137, edge case l.363)** — a 5-letter mail WorldItem and a panel with room for 3 (one hotbar stack to the same address at 17/20, nothing else free): the 3 oldest merge into the hotbar stack; the WorldItem survives under the same `worldItemId` and `entryId` with the 2 newest, `expiresTick`, position and FIFO node unchanged; one `WorldItemEntriesChanged {id, next worldSeq, entryId, count 2}` is raised (no `WorldItemDespawned`, no tombstone); hotbar +1 only; reply `ok`, requested 5 / got 3. A second pickup by B in the same tick takes the 2 and leaves the tombstone (`byActorId = B`, `taken`). 5 mailIds in total, none duplicated. On a whole pickup `WorldItems.Remove` runs before the AutoPlace commit (the mutator frees a merged-away source).
- [x] **H-R9-2 a–e** — a caught 5 letters to #4 with an inventory stack of 3 to #4 at (0,0): a: hotbar slot 2 = 10 to #4 → merged into slot 2 (15), inventory untouched; b: no #4 hotbar stack, slots 1 and 3 free → new entry in slot 1 (never 0); c: hotbar full, no #4 stack → merged into the inventory stack (8); d: hotbar and inventory full and the inventory #4 stack at 20/20, cursor empty → one WorldItem `origin = Thrown` at the catcher's feet, `expiresTick = tick + 9000`, cursor stays empty, no unit lost, `{0, 5}`; e: room for 3 (one hotbar stack at 17/20) → the 3 oldest merge, the 2 newest become the WorldItem, `{3, 2}`. (Backpack rows apply once it exists.)
- [x] A caught Axe with room → hotbar then inventory (category order, `TargetOrder.CursorRow`, PassFirst); a pickup of a mail WorldItem behaves as a–c, and returns `no_room` instead of d (the item stays, nothing bumps).
- [x] **Floating item and `entryId` (M-1a form)** — a pickup of a floating (water) WorldItem is refused by the core with a core-level verdict (no `InvReason`; story 019's anchor maps it to Player Character P13's `unavailable` before the core runs), nothing mutated, nothing bumped. `entryId` 0 or the WorldItem's own entry id → the pickup proceeds; a well-formed id that is not this WorldItem's entry (live elsewhere, or dead) → `stale_entry`, nothing mutated (namespace and high-water failures stay `bad_request`). Heap selection (several entries, default the lowest entryId, GDD l.101) is deferred to M-1b with `Spill` (TD-143 item 3): `WorldItemRecord` holds one entry, so no fixture heap at M-1a.
- [x] **Pickup naming an expired or destroyed WorldItem (tombstone; Review advisory A5)** — a WorldItem removed at tick T (mail expired to the Intake → cause `expired`; non-mail destroyed by expiry, water or cap eviction → cause `destroyed`; both by the system actor) and a pickup op naming its id: at T + 900 the tombstone is alive and the op gets `container_gone` with `byActorId` = system and that cause; at T + 901 it is dead and the op gets `stale_entry` (30 s = `Tombstones.LifetimeTicks` = 900 ticks, alive while `now − T ≤ 900`, as `Tombstones.TryResolve` on main does). Nothing is mutated and no version bumps in either case. Also a WorldItem picked up by A at T: B naming it at T + 900 gets `container_gone, byActorId = A`, cause `taken`; at T + 901 `stale_entry`.
- [x] **Catch refusals ((i) approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-154; (ii) ADR-0009 §5 l.157)** — (i) the plan leaves units unplaced and the WorldItem table is full → `TryCatch` refuses the whole catch in Validate with `Reason = capacity_exhausted`, `{0, 0}`, nothing mutated, the entry stays live and detached (Throwing's, as `CreateWorldItem`'s `None` contract), one `CheckCode.InventoryWorldItemCapacity` record; (ii) a mail merge's union block fails `CheckArenaReservation` → `TryCatch` refuses the whole catch in Validate with `Reason = capacity_exhausted`, `{0, 0}`, nothing mutated (no WorldItem, no `worldSeq` bump, no event), the entry stays live and detached (Throwing's), and `TryCatch` writes one `CheckCode.InventoryArenaExhausted` record outside Validate (ADR-0009 §5 l.157: "nothing mutated … an API caller gets `false` plus an always-on record"; the record's payload is TD-154 item 7's default); (iii) the fumble's WorldItem creation runs story 013's cap rule: at 512 live items exactly one eviction happens by 013's rules. Never the cursor; no unit lost in any case.
- [x] **Rejects raise nothing (TD-105 item 5)** — every refused pickup (`bad_request`, `container_gone`, `stale_entry`, floating, `no_room`, `capacity_exhausted`) and every refused catch leaves no record and no seal; versions unchanged.
- [x] **Versions, events, weight, indexes** — a pickup or catch bumps each panel container it changed by exactly +1, one `InvVersion` seal after that container's records (a WorldItem has no version). Event order: a pickup raises its world record (`WorldItemDespawned` or `WorldItemEntriesChanged`) first, then the panel records and seals; a fumbling catch raises the panel records and seals first, then `WorldItemSpawned` in `CreateWorldItem`'s nested call scope. Weight P (PerEntry) rises by classPts only when a new entry is placed in the panel; a merge and the fumble add nothing. After an Axe pickup `CountItem` and the StackKey index equal a recount.
- [x] **0 B** — pickup (whole, partial, `container_gone`, `stale_entry`) and catch (merge, place, fumble) cycles allocate 0 B per call after warm-up.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/World/PickupCore.cs`; `TryCatch` declared on `IInventoryApi` (`IInventoryApi.cs`) and implemented in `InventorySystem.cs`.
- **Target rows** (TD-153 item 4): add `TargetOrder.PickupRow(hotbar, playerInventory, targets)` writing `[hotbar, player_inventory]` (backpack skipped, so `MaxRowLength` stays 2), run `PlaceOrder.TargetFirst` with `MergePolicy.Merge` for mail pickup and catch; a non-mail pickup or catch uses `TargetOrder.CursorRow` with `PassFirst`. `CursorRow` and `QuickMoveRow` stay unchanged. `RunTargetFirst` (`Placement/AutoPlace.cs`) already merges into a matching hotbar stack (oldest ids, R6), else the lowest free slot ≥ 1, then the inventory; a detached source plans partial merges and a whole free placement of the remainder. Pickup and catch never touch the cursor or its `CursorOrigin` (GDD R9 "never the cursor").
- `PickupCore.Apply(actor, worldItemId, entryId)` contains everything after the range gate; the `Pickup` **OpKind handler** that runs Player Character's P13 chain first is story 019 (Ready; waits on player-character 019 for `IPlayersApi.CheckInteraction`). Core chain (approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-154): `bad_request` (payload, entry namespace, WorldItem id not of class WorldItem or never allocated — `WorldItems.Resolve`) → `container_gone` (tombstone alive: `byActorId`, cause) → `stale_entry` (dead tombstone, or an `entryId` that is not this item's) → floating (core verdict, no `InvReason`) → `no_room` → `capacity_exhausted` last (`CheckArenaReservation`, as `MoveHandler`). `not_allowed`/`not_carryable` are unreachable at M-1a (every WorldItem entry came from a hand-carried container or a mail spawn): assert under `INVENTORY_ASSERT`, Release answers `no_room`; 019 declares them on Pickup if ever needed. The core returns the reason plus `byActorId` and cause; the test handler writes them into `InvReplyExtra`. Seals: actor = the picker's player entity, cause `None`; the tombstone: `byActorId` = the picker, cause `Taken`.
- **Partial pickup**: `WorldItems` gains one primitive, `RaiseEntriesChanged(slot)` (next `worldSeq`, replicated, no version), called after a commit that left units on the WorldItem's entry; a whole pickup calls `WorldItems.Remove(id, actor, Taken)` first.
- **Test-only handler**: `PickupCatchTests` binds `InvOpDescriptors.Pickup` to a test handler over `PickupCore` in its own rig (built like `InvFixtureActionHandler`, `tests/unit/inventory/InventoryEventRig.cs`), never in `InventoryWithStubOps` (that would throw `DuplicateOpHandler`). `InventorySystem.Bind`, `BoundBySystem` and `InvMalformedOpTests` stay unchanged; production binds Pickup in story 019. Tests call the core directly and through the test handler.
- `TryCatch` is called by Throwing (flight resolution, ADR-0015 Accepted; the flight protocol ADR-0021 is not written) — Throwing owns the call site. Shapes (approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; now in ADR-0009 Key Interfaces, erratum 2026-10-22; TD-154): `readonly record struct FlightPayload(EntryId Entry, Vec3Mm CatcherPosMm, bool OnWater)` — the entry is the live detached entry `DetachForThrow` left, mailIds still in the arena, so the payload carries no ids or units; `readonly record struct CatchResult(byte UnitsIn, byte UnitsFumbled, InvReason Reason)` — two forms only: Reason 0 when the catch mutated (`{x, N}`, a partial or whole fumble included); `{0, 0}` with a reason when it was refused with nothing changed: `bad_request` for an entry that is not live and detached, is in a WorldItem, or outside a tick phase, `capacity_exhausted` per the refusals AC. The fumble is `CreateWorldItem(Entry, CatcherPosMm, Thrown, OnWater, createdTick = now)`, so `expiresTick = now + 9000` and the entry keeps its id.
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

**Status**: [x] Created and passing — `tests/unit/inventory/PickupCatchTests.cs` (73 test cases incl. theory rows) and `tests/unit/inventory/PickupCatchAllocationTests.cs` (2: `AllocAssert.Zero`, skipped in unoptimised builds per TD-008, plus the every-build twin). The 0 B guard was seen failing with an injected allocation in `PickupCore.Apply`, and the event-order test with the world record moved after the panel commit. Mail Review r1: the AC 7(ii) test was seen failing against the old `{0, N}` fumble before the B1 fix; the precedence test kills all 7 chain-order mutants of `PickupCore.Validate` (reorders of adjacent checks — M2 and M6 are reorders, not literal swaps — the review's two survivors, the core namespace result ignored; the mutant log `logs/gi014-r1-mutants.txt` is outside the repo; advisory fix, Mail Review PR #34 r2 advisory 3); each advisory test below was seen failing against the review survivor it targets.

**AC → tests** (`PickupCatchTests` unless noted):
1. H-R12-1: `HR121_OneSmallWorldItem_AAndBPickUpInOneTick_AFirst_AGainsIt_BGetsContainerGoneByA_Taken_OneCopy`.
2. Partial pickup: `PartialPickup_5Letters_RoomFor3_The3OldestMerge_TheItemKeepsIdEntryTimerPositionAndNode_With2Newest_OneEntriesChanged`, `PartialPickup_ThenBInTheSameTick_TakesThe2Left_WritesTheTakenTombstoneByB_NoMailIdDuplicated`; whole pickup: `WholePickup_OfALetterWorldItem_IntoAnEmptyPanel_GoesToHotbarSlot1_RemovesTheItem_AndLeavesTheTakenTombstone`, `WholePickup_ThroughTheCoreInATickPhase_IsTheSameAsThroughTheHandler`.
3. H-R9-2 a–e: `HR92a_…`, `HR92b_…`, `HR92c_…`, `HR92d_…`, `HR92e_…`.
4. Axe and mail pickup: `CaughtAxe_WithRoom_GoesToTheHotbarFirst_ThenTheInventoryWhenTheHotbarIsFull`, `MailPickup_BehavesAsCatchAToC` (a, b, c), `MailPickup_WithThePanelOfD_AnswersNoRoom_TheItemStays_NothingBumps`.
5. Floating and `entryId`: `FloatingWorldItem_IsRefusedByTheCore_WithTheFloatingVerdict_NoReason_NothingMutated_NothingRaised`, `Pickup_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep` (bad_request row through `p.Core`: the core's own namespace check), `EntryId_ZeroOrTheWorldItemsOwnEntry_ThePickupProceeds` (zero, own), `EntryId_NotTheWorldItemsEntry_StaleEntry_ButNamespaceAndHighWaterFailuresStayBadRequest_NothingMutated` (live elsewhere, dead, above high-water, other slot's temp).
6. Tombstones: `Tombstone_LetterExpiredToTheIntakeAtT_…`, `Tombstone_PlankDestroyedByTheWaterFloatAtT_…`, `Tombstone_PickedUpByAAtT_…`.
7. Catch refusals: `Catch_TableFullAndUnitsWouldFumble_RefusedWhole_CapacityExhausted_0And0_NothingMutated_EntryStaysDetached_OneCheckRecord`, `Catch_ArenaRefusesTheMailMergeUnionBlock_RefusedWhole_CapacityExhausted_0And0_NothingMutated_EntryStaysDetached_OneArenaRecord` (unchanged `Storage()` incl. versions, WorldItem table and `worldSeq`; no inventory record; one `InventoryArenaExhausted` record, a = 15, b = its class), `Catch_FumbleAt512LivePlanks_EvictsExactlyOne_TheLowestExpiresTickPlank_By013Rules`; also `Catch_TableFull_ButEverythingFits_Catches`, `Catch_MalformedCall_IsBadRequest_NothingChanged`, `Catch_WholeFumbleOnWater_TheThrownWorldItemFloats_ExpiringAfterTheWaterFloat300Ticks`.
8. Rejects raise nothing: `RefusedPickup_LeavesNoRecordAndNoSeal_VersionsUnchanged` (bad_request, container_gone, stale_entry, floating, no_room, capacity_exhausted; every row checks its reply), `RefusedCatch_BadRequestOrTableFull_LeavesNoRecordAndNoSeal`, `Pickup_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep` (ADR-0009 Implementation Guidelines l.226: 6 rows × slots 0 and 1, through the test handler and `PickupCore.Apply`), `TryCatchAndPickup_OutsideATickPhase_AreRefused_CountMutatingApiOutOfPhase_NothingMutated`, `NoRoomPickup_AndWholeFumble_RecordTheNoFitFact_AtTheUnchangedVersion` (pickup, catch).
9. Versions, events, weight, indexes: `WholePickup_IntoAnEmptyPanel_WorldRecordFirst_ThenInserted_ThenOneHotbarSeal_ActorThePicker_CauseNone_PRisesByClassPts` and `PartialPickup_EntriesChangedFirst_ThenMerged_ThenTheHotbarSeal_PUnchanged` (both: reply ok and after the seal, ADR-0009 §4 rule 5), `SplitPickup_IntoHotbarAndPlayerInventory_TwoSealsBeforeTheReply_ResultVersionAIsTheHotbar_BThePlayerInventory` (slots 0 and 1; TD-155 item 2), `FumblingCatch_PanelRecordsAndSealFirst_ThenWorldItemSpawnedInANestedCallScope_PUnchangedByMergeAndFumble`, `CatchPlacingANewEntry_RaisesInsertedAndOneSeal_PRisesByClassPts`, `TwoCatchesByOneInTickCaller_EachIsOneCallScope_ItsRecordAndSealShareIt`, `AxePickup_CountItemAndTheStackKeyIndexEqualARecount`, `NonMailPickup_RunsPassFirst_MergesIntoTheSecondTargetsStack_BeforeAFreeCellOfTheFirst`, `Pickup_OnTheItemsExpiryTick_Wins_TheLetterIsTaken_NotReturnedToTheIntake` (GDD l.363, expiry half).
10. 0 B: `PickupCatchAllocationTests.PickupAndCatch_Over500Cycles_AllocateNothing_InOptimisedBuilds` and `…_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild` (catch merge + partial fumble, whole, a whole `{0, 2}` fumble, whole, `container_gone`, `stale_entry`, partial, whole, catch place; and a second world refusing a catch by the arena and by a full WorldItem table each cycle).

**Verification** (2026-10-22, `DOTNET_ROOT=/home/box/.dotnet10`):
- net10.0 Debug: 6136 total, 0 failed, 56 skipped; build 0 warnings.
- net10.0 Release: 6136 total, 0 failed, 3 skipped; build 0 warnings.
- net8.0 Debug: 6136 total, 0 failed, 56 skipped; build 0 warnings.
- net8.0 Release: 6136 total, 0 failed, 3 skipped; build 0 warnings.
- `bash tools/ci/check-sim-access.sh`: exit 0; `--self-check`: all cases PASS.

**Verification after Mail Review r1** (2026-10-22, at 24047e9, `DOTNET_ROOT=/home/box/.dotnet10`):
- net10.0 Debug: 6157 total, 6101 passed, 56 skipped, 0 failed; build 0 warnings.
- net10.0 Release: 6157 total, 6154 passed, 3 skipped, 0 failed; build 0 warnings.
- net8.0 legs, `check-sim-access.sh`, the smokes and CI were not rerun in the r1 fix session.

---

## Completion Notes

**Completed**: 2026-10-22 on branch `feat/grid-inventory-014` (from origin/main f504231; slices 1–5), opened as PR #34 at 7560950 (merged as main c2d4a64). Mail Review r1 (BLOCKED: B1, B2) is fixed by the r1 commits `ebc38e3` (B1, B2) and `24047e9` (advisories), pushed as head `09804ab` and passed by Mail Review r2 (READY) (advisory fix, Mail Review PR #34 r2 advisory 3). No `/code-review` or lead-programmer gate was run in this session.

**Credit**: Evan's 2026-10-22 5:11 AM PT ruling covers only that a leashed letter stays on the cursor when the mail-id arena is full; 014 does not build on it. Everything 014 decides that no ADR or GDD line states was a Mail Lead planning default and is approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (TD-154, TD-155 for the implementation calls below, TD-159 for the deferred tests). The `capacity_exhausted` code (85) is ADR-0009 l.121's reason-code list; the arena-refused catch failing with nothing mutated plus an always-on record is ADR-0009 §5 l.157; the core chain order is TD-154 item 5 (no ADR/GDD line states it).

**As built**:
- `TargetOrder.PickupRow` `[hotbar, player_inventory]`; mail pickup/catch run `TargetFirst` + `Merge`, non-mail `CursorRow` + `PassFirst` (`PickupCore.PlanIntoPanel`, shared by `TryCatch`).
- `PickupCore` (`Inventory/World/PickupCore.cs`): named pure `Validate` and a `Commit` that never fails; `Apply` = tick guard, Validate, Commit. Results are `PickupResult` with a `PickupVerdict` (`Taken`, `Refused`, `Floating`); `InventorySystem.Pickup` exposes it. No Pickup handler is bound in production (`InventorySystem.Bind`, `InventoryWithStubOps`, `BoundBySystem` and `InvMalformedOpTests` unchanged); the tests bind a test-only `PickupTestHandler` through a new optional `bindExtra` hook of `InvAutoPlaceRig`.
- `IInventoryApi.TryCatch(PlayerSlot, in FlightPayload) → CatchResult`, with `InventorySystem.ValidateCatch` (pure) and `CommitCatch` (never fails). `FlightPayload`/`CatchResult` as TD-154 item 6.
- `WorldItems`: `RaiseEntriesChanged(slot, countAfter)`, `CanCreate()`, `TableFull`, `RecordCapacityRefusal(now)`.
- Review r1 (B1): `ValidateCatch` refuses an arena-refused merge (`CatchPlan.ArenaRefused`, `{0, 0}`, `capacity_exhausted`); `TryCatch` then writes ADR-0009 §5's always-on record outside Validate through the new `MailIdArena.RecordReservationRefusal(blocks, tick)` (`CheckCode.InventoryArenaExhausted` 12, a = the largest refused block, b = its class, rate-limited like every check record), as the table-full branch writes `RecordCapacityRefusal`. Validate stays pure; `CommitCatch` still never fails and now asserts `Create`'s result.
- New ids: `SimAssertCode.InventoryPickupNotAdmitted` = 81 (the unreachable `not_allowed`/`not_carryable`) and, in Review r1, `SimAssertCode.InventoryCatchFumbleNotCreated` = 83 (`CommitCatch`'s `Create` returned None; unreachable; 82 is story 021's `InventoryBacklogRingFull`). No new CounterId, EventKind or CheckCode. New TD rows: TD-155 (story-done) and TD-159 (Review r1 deferred tests); TD-156 is reserved for Mail Lead's docs row and TD-157/TD-158 are story 021's, so this branch alone has a gap at TD-156…TD-158. Next free: TD-160, SimAssert 84, CounterId 51, EventKind 0x008B, after 021.
- Test rig (Review r1 advisory 6): `InvAutoPlaceRig.Run`/`RunTick` assert the tick was captured, so a stale log can no longer be read as the current one; `WorldItemTests.CreateWorldItem_InvalidCalls_…` turns capture on for its arrange placement.
- Ledger: none; `// story 017 ledger hook` comments at the World → Placed (pickup) and Flight → Placed (catch) crossings.

**Deviations from the handoff** (TD-155):
- `RaiseEntriesChanged` takes the post-commit count and is raised before the panel commit, so AC 9's "world record first" holds (the handoff sketched `RaiseEntriesChanged(slot)` after the commit).
- The pickup reply's primaries are hotbar (A) and player_inventory (B).
- `TryCatch` refuses a would-be fumble whenever no WorldItem can be created (full table — with the record — or no/exhausted WorldItem ids — without), so the commit cannot fail.

**Out of scope, still open**: story 019 (range-gated handler, floating → P13 `unavailable` before the core runs, TD-154 item 4), M-1b heap selection (TD-143 item 3), story 017 ledger calls, the ADR-0009 amendment (TD-144/TD-154 item 6 shapes; TD-154 item 7: the record payload, and `PutAwayCursor`/`TryInsert` refusing an arena-exhausted plan with no §5 record), TD-159 (WorldItem id-exhaustion and eviction-tick tests). EPIC.md, the epics index and HANDOFF were recounted in 7560950 (105 / 1 / 6 / 95; grid-inventory 16 Complete / 7 Ready / 1 Blocked); Review r1 corrected EPIC.md's header line, which 7560950 had left at 15 / 8 / 1.

---

## Dependencies

- Depends on: Story 009, Story 010 (target orders), Story 013 — all Complete and merged (010 in PR #33, main f504231)
- Unlocks: Story 017, Story 018, Story 019; throwing-catching epic

---

## Approved defaults (Evan, 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT)

These were Mail Lead planning defaults of 2026-10-22; **Evan approved them on 2026-10-22 at 2:24 PM PT, confirmed in scope 3:00 PM PT** ("to all recommended decisions", Mail Lead chat), so they are now Evan's rulings. The story is built on them; TD-154 tracks them. None contradicts an Accepted ADR (rechecked after Mail Review r1: item 7's arena half now follows ADR-0009 §5 l.157 rather than defaulting, item 4 holds because story 019 maps floating before the core runs, and items 2 and 6 extend names the ADR leaves undefined); each follows TD-150, TD-152 and TD-153.

1. Mail pickup and catch use a new `TargetOrder.PickupRow` = `[hotbar, player_inventory]`, `TargetFirst`, Merge; non-mail pickup and catch reuse `CursorRow`, `PassFirst` (TD-153 item 4).
2. A partial pickup leaves the WorldItem alive with the newest units under the same `worldItemId` and `entryId` (`expiresTick`, position and FIFO node unchanged) and raises `WorldItemEntriesChanged`; only a whole pickup removes it and writes the `taken` tombstone.
3. At M-1a `entryId` is 0 or the item's own entry id; any other well-formed id → `stale_entry`. Heap selection waits for M-1b `Spill` (TD-143 item 3).
4. A floating item is refused by the core with a core-level verdict, not an `InvReason`; story 019 maps it to P13's `unavailable` (ADR-0009 l.115). No Pickup descriptor change.
5. Core chain: `bad_request → container_gone → stale_entry → floating → no_room → capacity_exhausted` (`container_gone` in id resolution, ADR-0009 l.116); `not_allowed`/`not_carryable` unreachable at M-1a (assert; Release `no_room`).
6. `FlightPayload(EntryId Entry, Vec3Mm CatcherPosMm, bool OnWater)` and `CatchResult(byte UnitsIn, byte UnitsFumbled, InvReason Reason)`; the fumble keeps the flight entry's id and is created at the catch tick (`expiresTick = now + 9000`).
7. Catch refusals: WorldItem table full → the whole catch refused in Validate (`capacity_exhausted`, `{0, 0}`, nothing mutated, entry stays detached, one `InventoryWorldItemCapacity` record) — the default. A mail merge's union block refused by the arena → refused the same way plus one `InventoryArenaExhausted` record — not a default but ADR-0009 §5 l.157 (Review r1 B1 replaced the earlier whole fumble, which contradicted it); only the record's payload (the largest refused block and its class) is the default.
8. Event order: a pickup raises its world record first, then panel records and seals; a fumbling catch raises panel records and seals first, then `WorldItemSpawned` in `CreateWorldItem`'s nested scope. Seal actor is the picker's or catcher's player entity, cause `None`.
