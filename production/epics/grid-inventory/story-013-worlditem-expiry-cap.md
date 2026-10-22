# Story 013: WorldItem entity, absolute-tick expiry FIFOs, water float, cap eviction and tombstones

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (close-out: ACs ticked, Test Evidence, Completion Notes; review R1: Out of Scope drops the stale Blocked tags on 020/021, manifest re-stamped 2026-10-17.1, deferrals recorded; Q1–Q3 owner rulings recorded)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-013` (cap 512, expiry FIFOs, lossless mail eviction), `TR-inventory-014` (absolute-tick timers; expiry after Inputs.Apply)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0004: Identity and Id Spaces; ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: WorldItem expiry uses one FIFO per duration class (`mailWorldItem` 9000, `nonMailWorldItem`/`spillHeap` 9000, `waterFloat` 300, `bag` 900), appended at creation so append order is `expiresTick` order (ties → ascending `worldItemId`); phase 1 (`ItemsExpire`, after `InputsApply`) pops heads with `expiresTick ≤ now`, skipping lazy tombstones; cap eviction takes the `mailWorldItem` head if older than `capEvictMinAgeSeconds`, else the lower `(expiresTick, id)` non-mail head; the new item is never a candidate; a 256-entry tombstone ring `(id, byActorId, cause, worldSeq, tick)` (30 s) feeds `container_gone` (ADR-0009 §5; ADR-0004 `EntityClass.WorldItem`).
**ADR Version**: ADR-0009 2026-10-16; ADR-0004 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Forbidden: a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, a per-entry `List<uint>` for `mailIds` (ADR-0009 Alts 3–5)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)
- Required (Foundation, ADR-0003): systems bind phase 1 `ItemsExpire`; a pickup arriving on the expiry tick (phase 0) wins

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R12-2** — a letter (id 500) dropped at tick 1000 → at tick 10000 the WorldItem is gone and 500 is in `po_intake` at the back of the outfeed queue; Plank ×5 dropped at 1000 → at 10000 gone, `ItemsLost {plank, 5}` emitted, in no container; a letter (id 600) landing on water at 2000 while the Intake holds 601–610 → floats until 2300, then enters the Intake with `arrivalSeq` greater than 610's entry, `PeekOutfeed` still 601; a Plank landing on water → DESTROYED at 2300 with `ItemsLost`. (Backlog-if-full branch: story 021.)
- [x] **H-R12-3** — 512 WorldItems (half mail, half Plank, distinct `expiresTick`, all older than 10 s) + a 513th → the mail item with the lowest `expiresTick` returns to the Intake, count 512, no Plank destroyed; 512 Planks + a 513th Plank → the lowest-`expiresTick` Plank destroyed with `ItemsLost`; 511 Planks + one letter landed 2 s ago + a dropped Plank → the letter is **not** evicted, the lowest Plank is; mail 900 and 901 with equal lowest `expiresTick` → 900 evicted; 512 young-mail items + a 513th → nothing evicted, count 513, `worldItemCapExceeded` logged, and the first to age past 10 s is evicted that tick.
- [x] **H-D6-1** — `expiresTick = createdTick + seconds × 30`: dropped at 1000 → 10000; thrown at 1000, landing 1045 → 10045 (timer from landing); a solo pause (ticks not advancing) does not move expiry — the item expires on sim tick 10000.
- [x] **H-PERF-6d** — a 400-WorldItem burst at the cap visits ≤ (items created + 1) candidates in that tick (probe counter).
- [x] A removed WorldItem (picked up, expired, evicted) leaves a tombstone; a later op naming it within 30 s → `container_gone` with `byActorId` and `cause` (`taken`/`expired`/`destroyed`); after 30 s → `stale_entry`.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/World/` — `WorldItemTable.cs` (struct array, `EntityId` class `WorldItem` via injected `EntityIds`), `ExpiryFifos.cs`, `Tombstones.cs`; `IInventoryApi.CreateWorldItem(entry, posMm, origin, onWater, createdTick)`.
- Position is int32 (GDD: cm; Sim Core uses mm — store `Vec3Mm`, flag the GDD unit to its re-review). The **caller** supplies position and the water flag (Throwing's landing resolution, Drop's ground snap — both elsewhere); this story only stores them.
- Origins at M-1a: `Dropped`, `Thrown`; `BeltLeak` (M-1b), `Spill` (M-1b, death), `Loot` (Mail R15 — exemption is a flag that keeps the item off every FIFO; build the flag, its Payday release is Mail's).
- Mail return on expiry calls story 012's `InventorySystem.ReturnToIntake` (enters whole, NoMerge, new `arrivalSeq`).
- `WorldItem{Spawned, Despawned, EntriesChanged}` events with `worldSeq` are `Replicated` (ADR-0008) — raise them through the mutator.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: `pickup` core and `TryCatch`.
- Story 020: `Drop`.
- Story 021: backlog branch.
- M-1b: Spill heaps, bags, BeltLeak.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/WorldItemTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/inventory/WorldItemTests.cs`, `WorldItemStorageTests.cs`, `WorldItemAllocationTests.cs`; all pass on net10.0/net8.0 × Debug/Release

---

## Dependencies

- Depends on: Story 005, Story 012
- Depends on: `production/epics/simulation-core/story-018-id-allocators-slottable.md` (`EntityIds`), `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (phase binds, `ITickEndCheck`, `SimCoreSettings`)
- Unlocks: Story 014, Story 015, Story 018, Story 020

## Completion Notes
**Completed**: 2026-10-21 (PR #26)
**Criteria**: 5/5 passing. Deferred: (1) the op-reply half of the tombstone AC — that an op naming a removed WorldItem gets a reply carrying `container_gone` with `byActorId`/`cause` (and `stale_entry` after 30 s) — is untested until story 014's pickup, the first op that names a WorldItem; here it is tested through `WorldItems.Resolve`, the function the op `Validate` will call; (2) M-1b: H-D6-1's bag case ("a bag at 2000 → 2900", GDD l.797; the bag FIFO exists but is empty at M-1a, only its 900-tick duration is pinned) and H-R12-3's "Spill heap of 40 entries → exactly one eviction" (GDD l.728; Spill is refused until M-1b); (3) the backlog-if-full branch of H-R12-2 is story 021's, as the AC says.
- H-R12-2: `HR122_Letter500DroppedAt1000_At10000IsGone_AndIsInTheIntakeAtTheBackOfTheOutfeedQueue`, `HR122_Plank5DroppedAt1000_At10000IsGone_ItemsLostPlank5_AndInNoContainer`, `HR122_Letter600LandingOnWaterAt2000_WithIntakeHolding601To610_FloatsUntil2300_ThenEntersAfter610_PeekStill601`, `HR122_PlankLandingOnWater_IsDestroyedAt2300WithItemsLost`
- H-R12-3: all five cases — `HR123_512HalfMailHalfPlankAllOld_A513thDropped_…`, `HR123_512Planks_A513thPlank_…`, `HR123_511PlanksAndALetterLanded2sAgo_ADroppedPlank_…`, `HR123_QualifyingMail900And901WithEqualLowestExpiresTick_…`, `HR123_512YoungMail_A513thLands_NothingEvicted_Count513_CapExceededLogged_…`; the tie test pins the exact eviction sequence 900 → 901 → lowest plank and the Intake order, and `HR123_GroundAndWaterPlankWithEqualLowestExpiresTick_…` pins the cross-FIFO non-mail tie (lower id first; review R1); plus the H-R12-4 Loot exemption under cap pressure: `HR124_511PlanksAndTheOldestALootLetter_…`, `HR124_CapFullOfLootOnly_…`
- H-D6-1: `HD61_DroppedAt1000_Expires10000_ThrownAt1000LandingAt1045_Expires10045`, `HD61_SoloPauseOf300TicksOfWallTime_DoesNotMoveExpiry_TheItemExpiresOnSimTick10000`
- H-PERF-6d: `HPERF6d_A400PlankBurstAt512OldPlanks_…` (399 visits for 400 created), `HPERF6d_A400BurstOverYoungMailOldPlanksAndWaterPlanks_…` (11 + 387 = 398), `HPERF6d_A400YoungMailBurstAt512YoungMail_…` (0); Release probe counter `CounterId.InventoryWorldItemCapCandidates` (46)
- Tombstones: `Tombstone_PickedUpWorldItem_ResolvesContainerGoneWithTheTakerAndTaken_For30s_ThenStaleEntry`, `Tombstone_ExpiredMail_IsExpired_DestroyedPlank_IsDestroyed_EvictedItemsAlike_ByTheSystemActor`, `Tombstone_EvictedMail_IsExpired_AndANeverAllocatedOrForeignClassIdIsBadRequest`, storage tests `Tombstone_IsAliveFor30SecondsInclusive`, `Tombstone_Ring_OverwritesTheOldest_After256`. No op names a WorldItem until story 014's pickup, so the AC is tested through the id-resolution path the op handlers will call (readiness G6).
- Also: creation-order tests (`CreationOrder_*`, 3), `Expiry_DueItemsOfSeveralFifos_…`, `CreateWorldItem_*` (2), `LootLetter_IsOnNoFifo_AndStillExistsAt10000`, interim-rule tests (`Interim_*`, 4), `Dump_InventorySectionCarries…`, `Pins_NewEventKindsCheckCodesAndAssertCode_AreAppendOnlyValues`, allocation `CreateEvictExpireReturnPickupAndResolve_Over400Ticks_AllocateNothing_InOptimisedBuilds` (Release) with its `…_InEveryBuild` twin
**Q1 settled**: H-PERF-6d visit = queue-node read, gate created + 1 (Evan 2026-10-21 7:22 PM PT via Mail Lead; confirmed to Mail Review 7:43 PM PT, Blocker 2 withdrawn). Counter 46 stays exactly as coded: one per FIFO node read on the cap-eviction path; a head already checked (the `ExpiryFifos` head memo) and `Compact` clean-out reads do not count. No pin depended on anything beyond that definition, so 399, 11 + 387, 0 and the HR124 `+ 1` stay exact (a brief tick-scoped-memo change, 099dcbc, was reverted in 6823bed).
**New values** (append-only): `CounterId` 46 `InventoryWorldItemCapCandidates` (H-PERF-6d probe, Release), 47 `InventoryWorldItemReturnRetries` (interim rule), 48 `InventoryWorldItemCapExceeded`; `CheckCode` 13 `InventoryWorldItemCapExceeded`, 14 `InventoryWorldItemCapacity`; `SimAssertCode` 76 `InventoryWorldItemFifoFull`, 77 `InventoryWorldItemCreatedOutOfOrder`; `EventKind` 0x0086 `WorldItemSpawned` (44 B), 0x0087 `WorldItemDespawned` (16 B), 0x0088 `WorldItemEntriesChanged` (16 B), 0x0089 `ItemsLost` (8 B), all `Replicated` to `ISlotView.ConnectedMask`, codec id 0. "`worldItemCapExceeded` logged" = counter 48 per over-cap creation plus one check-ring record per episode.
**Deviations** (all ADVISORY, as-implemented):
1. The `waterFloat` duration class is two queues (mail, non-mail; 300 ticks each), plus `Bag` (900, empty): five FIFOs. The non-mail cap candidate is the lower of the two non-mail heads, so a floating plank can be evicted (ADR-0009 §5 erratum 2026-10-21).
2. H-PERF-6d counting: each FIFO memoises that its head was verified; an already-checked head is not counted again, nor are `Compact` clean-out reads — the Q1 ruling above.
3. `CreateWorldItem` takes an `EntryId`, not a raw entry slot (manifest id-wrapper rule).
4. `InventorySystem`'s public constructor gains `EntityIds` and `ISlotView`; composition root and test rigs updated.
5. `createdTick` must be ≤ now and not earlier than the previous creation on the same expiry FIFO (Loot, on no FIFO, is exempt); otherwise `EntityId.None`, nothing changed, `SimAssertCode` 77 under `INVENTORY_ASSERT`. Callers must handle `None` (the entry stays detached and is theirs) — `IInventoryApi.CreateWorldItem` remarks.
6. `BeltLeak` and `Spill` origins are refused until M-1b; `Loot` is on no FIFO and never a cap candidate.
7. "Older than 10 s" means 300 ticks or more (`>=`): a letter created at tick c is cap-evictable from c + 300 (**Q2 settled**, Evan 2026-10-21 9:16 PM PT, given directly to Mail Review: "At 300 ticks or more (>=), as coded"; this supersedes the 8:01 PM PT relay of strictly greater; Sonnet review item 3). Pin: the young-mail test `HR123_512YoungMail_…` is the boundary test — 513 live at tick 1299 (age 299, not evicted) and the head letter evicted at 1300 (age 300). Content rule CV-GRD-06 warns exactly when no eviction window exists (min-age ticks ≥ despawn ticks).
8. A tombstone answers `container_gone` up to 900 ticks after removal inclusive, `stale_entry` from 901; a full 256-ring overwrites the oldest whatever its age.
9. Causes: mail returned to the Intake (expiry or eviction) is `expired`; destroyed non-mail is `destroyed`; a pickup is `taken`.
10. Wrong-class or never-allocated ids resolve as `bad_request`.
11. Balance durations in ms convert to ticks rounding down; the bag's 900 is a constant (no balance key yet).
12. **Interim full-Intake rule (readiness G1, until story 021):** when `ReturnToIntake` fails, the mail WorldItem stays at its FIFO head and nothing is removed; each blocked head is retried at most once per FIFO per (tick, Intake version), counted in counter 47; a mail cap candidate whose return fails is not evicted (the non-mail rule applies, or the count goes over the cap). Story 021 replaces this with the backlog append (TD-138).
13. Event order: `WorldItemDespawned` is raised before the Intake records and seal, in the same call scope.
14. `Remove` opens no call scope of its own (it uses story 014's pickup handler's); `WorldItemEntriesChanged` is declared and bound but raised by 014's partial pickup.
15. `worldItemCapacity` is validated to ≤ 65,535 (ushort slot), although the balance knob allows 65,536.
16. `ReturnToIntake` is split into plan and commit steps so expiry runs one call scope per item; it now refuses an entry that a WorldItem holds.
**Owner flags** (no GDD edited; the ADR-0009 erratum 2026-10-21 was approved by Evan, see (d)): (a) the interim full-Intake rule above, until story 021 — **Q3 settled** (Evan 2026-10-21 8:01 PM PT via Mail Lead): while the Intake is full, cap eviction destroys the lowest non-mail item instead of the blocked mail, as coded, until story 021, which gets a backlog AC at its readiness check (TD-138); (b) GDD position is cm, the Sim stores mm (`Vec3Mm`) — Mail Lead fixes the GDD in a docs PR after the merge; (c) the four world event kinds carry codec id 0 — the network epic assigns wire codecs (TD-140); (d) ADR-0009 §5 erratum (five FIFOs, `ItemsLost`, the world record layouts and the Q1 visit definition) — **B1 resolved**: written in PR #26 with a matching control-manifest Errata line, approved by Evan directly at 8:11 PM PT (decision log 2026-10-21); (e) none open: Q1, Q2, Q3 and B1 are settled (decision log 2026-10-21); (f) readiness G2/G3 resolved in review R1: the Out of Scope Blocked tags are dropped and the Manifest Version is re-stamped 2026-10-17.1.
**New TD**: TD-135 (ledger hooks, story 017), TD-136 (container tombstones, M-1b), TD-137 (WorldItem load/rebuild and undumped derived state), TD-138 (interim rule → story 021), TD-139 (GDD/ADR errata), TD-140 (world event codecs), TD-141 (H-PERF-6d visit definition), TD-142 (small residuals), TD-143 (M-1b deferrals: H-D6-1 bag, H-R12-3 Spill heap).
**Test Evidence**: Logic — see above; at the PR #26 head (code last changed in a048137; legs run at 74ebbf6): 5,282 tests per leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 5,234 passed / 48 skipped, Release 5,279 passed / 3 skipped); guards all pass, `check-sim-access --self-check` 17 cases
**Run result**: N/A — Sim-only, no Godot surface (no smoke run)
**Code Review**: read-only review (Sonnet): APPROVE, 0 blocking, 8 non-blocking; items 4–7 applied in 83c2c40 (per-FIFO creation order and retry memo, Loot cap tests, TODO(story-017)); 1–3 recorded as TD-141/TD-137/TD-139 owner questions (1 and 3 since settled as Q1/Q2; Q2 = `>=`, Evan 9:16 PM PT); 8 applied here (Test Evidence, 014 hand-off)
