# Story 017: UnitLedger conservation counters, tick-end check, INVENTORY_ASSERT full scan and the canonical dump section

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: L (~2–3 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (TD-160 items 12 and 15 Approved by Evan 2026-10-22 7:12 PM PT ("I approve all"), formerly Mail Lead defaults, pending Evan; decision log 2026-10-22. Before that: Mail Review r1 A1–A6: AC 11's O(1) clause is by inspection (A1); `EnterHolder`/`LeaveHolder`/`CountSpawned` internal plus a caller allow-list test (A2); holder bytes on load logged as TD-163 (A3); distinct non-zero dump counters (A4); a test renamed and AC 6's note lists the five edited tests (A5); `LiveStateCount`/`ConsumeReasonCount` pinned to their enums (A6). Before that: Mail Review B1: the ADR-0009 §6 l.170 erratum (with the l.171 never-throws clarification) approved by Evan 2026-10-22 6:41 PM PT — "Approve the ADR-0009 l.170 erratum as drafted, including the l.171 never-throws clarification" — applied in ADR-0009 and the control manifest; l.18, l.112 and TD-160 items 1, 13 and 14 cite it. Mail Review r2 A8: the `PutAwayCursor` §5 question marked decided — approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (ADR-0009 erratum §5); `TryInsert`'s record is TD-162. Before that: main 43c5348 (Mail Lead's docs PR #37) merged in at 454125d, no rebase; TD-160 items 2–11 relabelled approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; items 1, 13 and 14 stay Mail Lead defaults, pending Evan (ADR-0009 l.170, Review B1); items 12 and 15 stay Mail Lead defaults, pending Evan. Before that: close-out at `/story-done`: ACs 11/11 ticked with their tests, Completion Notes; PR #36, main c889f59 (PR #35 merged) merged in at e941cb4. Before that: readiness fixes against main c2d4a64, story 014 / PR #34 merged, checked against story 021 / PR #35 ab0ae6d: Manifest Version re-stamped; Decision Summary made precise (`spawned` / `granted`, the Cursor and holder states, sole writer); the crossing table covering 012–015 and 014's pickup/catch and 021's backlog append/drain; check record, counter, scan, IL, Recount seam, `TryInsert` duplicate (TD-145), dump field list, rejects, 0 B, determinism ACs; depends on 021; estimate M → L; Mail Lead planning defaults pending Evan, TD-160)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-012` (O(1) conservation counters checked each tick; `INVENTORY_ASSERT` compiled out of Release)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay; ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: A `UnitLedger` holds O(1) counters per location state (`Placed, Cursor, World, Flight, Belt, Backlog, Consumed, Destroyed`) for mail and per-itemId item units, plus `spawned`, `granted` (and `consumed`, `destroyed`); only the lowest mutator primitives touch it, and boundary crossings into Flight, Belt and Backlog happen only through `IInventoryApi` (ADR-0009 §6 l.169–170, as corrected by the ADR-0009 §6 erratum 2026-10-22, approved by Evan 2026-10-22 6:41 PM PT — "Approve the ADR-0009 l.170 erratum as drafted, including the l.171 never-throws clarification": the primitives are `InventoryMutator`'s methods, the ledger's only writer, including the ledger-only `EnterHolder`/`LeaveHolder`; the `IInventoryApi` rule binds systems outside Inventory, and `WorldItems` and the internal `TryAppendDetached` make World and Backlog crossings directly); Mail's units cross into Backlog at `IInventoryApi.AppendBacklog` (ADR-0017 §6 l.137); the tick-end check asserts `Σ live = spawned + granted − consumed − destroyed` in every build and writes an always-on record on mismatch (never throws; under `INVENTORY_ASSERT` the full scan's finding also asserts — the same erratum); the full multiset scan runs behind `[Conditional("INVENTORY_ASSERT")]` (ADR-0009 §6). Inventory state is a canonical-dump section in ascending ids (ADR-0006 §4).
**ADR Version**: ADR-0009 2026-10-16 (errata through 2026-10-21); ADR-0006 2026-10-16; ADR-0003 2026-10-16; ADR-0017 2026-10-16 (§6, the Backlog crossing)

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: `[Conditional("INVENTORY_ASSERT")]` binding per calling assembly in a Godot Release export is an OQ4 build-spike item (ADR-0001) — irrelevant to the Sim test project; verify in `godot-host-shell`.

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: conservation via a `UnitLedger` of O(1) counters written only by `InventoryMutator`'s primitives (ADR-0009 §6 erratum 2026-10-22); tick-end Σ check in every build, always-on record on mismatch, never throws; full scan behind `[Conditional("INVENTORY_ASSERT")]` (ADR-0009 §6, manifest l.350; quote updated to the post-erratum manifest — advisory fix, Mail Review PR #36 A12)
- Required (Foundation, ADR-0006 §4): `ISimStateContributor` writes fields one by one, little-endian, collections ascending by id, unique never-reused `SectionId`
- Required (Foundation, ADR-0001 §4): build-dependent behaviour only via `SIM_ASSERT`/`INVENTORY_ASSERT`
- Required: inventory interaction goes only through `IInventoryApi` (boundary crossings into Flight, Belt, Backlog included) — for systems outside Inventory; Inventory's own `WorldItems` and `TryAppendDetached` are internal (ADR-0009 §6 erratum 2026-10-22, manifest l.432; quote updated to the post-erratum manifest — advisory fix, Mail Review PR #36 A12); `Validate` never touches the ledger (ADR-0009 §2, manifest l.339)
- Re-checked against manifest 2026-10-17.1: current (revision .1 changes no rule this story cites).

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **Ledger shape** — `UnitLedger` (preallocated at world build) holds, for mail and for each itemId (array sized `DefTable<ItemDef>.Count`), count-weighted live counters `Placed, Cursor, World, Flight, Belt, Backlog`, `Consumed` split by `ConsumeReason` (3 sub-counters whose sum is Consumed), `Destroyed`, `spawned`, `granted`. The Cursor is its own state (`InventoryMutator.IsCursor`); an entry detached inside one call (mid-pickup) is in no state and is never seen at tick end. **`spawned`** counts a unit's first entry into Inventory custody: at `Spawn` placement, per placed unit, or at `AppendBacklog`, per appended unit; units `Spawn` leaves unplaced are Mail's and in no state (ADR-0017 §6 l.137). **`granted`**: no production grant exists at M-1a (Grant is M1); see the `Recount` seam AC. *(Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; formerly a Mail Lead planning default; TD-160 items 2–4, 9.)*
- [x] **Sole writer** — only `InventoryMutator` writes the ledger; holder crossings (World, Flight, Belt, Backlog) made today in `InventorySystem`, `WorldItems` and `PickupCore` go through two new mutator primitives `EnterHolder` / `LeaveHolder` (TD-160 item 1). An architecture test (`ArchitectureScan.BodyReferences` over the Sim assembly, as `tests/unit/architecture/*RulesTests.cs`) finds no ledger writer outside `InventoryMutator` and `UnitLedger`'s own test seams, with a seen-failing fixture.
- [x] **Crossing table** — a fixed script (≤ 50 steps) exercises every row below, asserts each row's exact deltas, and asserts `Σ live = spawned + granted − consumed − destroyed` after every step for mail and every itemId:
  - spawn placement: spawned +n, Placed +n; spawn overflow left to Mail: no change; `AppendBacklog` (story 021): spawned +n, Backlog +n;
  - pick / split onto the cursor: Placed −n, Cursor +n; place / merge / move / quick_move / return / leash put-away (`PutAwayCursor`): source state −n, target state +n (Cursor → Placed, Placed → Placed); a move inside one container: no change;
  - `DetachForThrow`: Placed −n, Flight +n; `TryCatch` (story 014): Flight −unitsIn, Placed +unitsIn, and Flight −fumbled, World +fumbled; `CreateWorldItem`: World +n from Flight (Thrown) or from Placed/Cursor (Dropped, the caller's `Remove`);
  - pickup (story 014): whole — World −n, Placed +n; partial — World −moved, Placed +moved, the remainder stays World;
  - mail expiry / water return to the Intake: World −n, Placed +n; to the backlog (story 021, Intake full or backlog non-empty, also a cap-evicted mail): World −n, Backlog +n; drain (story 021): Backlog −n, Placed +n;
  - non-mail expiry / water / cap eviction (`DestroyNonMail` → `DestroyDetached`, story 013, TD-135): World −n, Destroyed +n;
  - `TakeUnit`: Placed −1, Belt +1; `TryInsert` (true only): Belt −1, Placed +1; `ConsumeUnit`: source state −1, Consumed[reason] +1.
- [x] **H-X-2 (ledger clause, moved from story 015)** — after `DetachForThrow(slot, 88, 5)` the ledger shows Placed −5, Flight +5 for mail; after `ConsumeUnit(ref, Delivered, actor)` Placed −1, Consumed +1 and Consumed[Delivered] = 1.
- [x] **Rejects change nothing** — a refused op, a false `IInventoryApi` call (`TryInsert`, `AppendBacklog`, `ConsumeUnit`, `DetachForThrow`), a refused `PutAwayCursor` plan, a drain skip and a refused catch (`{0, 0}`) leave every counter unchanged.
- [x] **Recount seam** — `UnitLedger.Recount` (internal, test seam) recounts the live states from the entry pool, WorldItems, the backlog and the detached Flight entries (Belt = 0) and sets `spawned` / `granted` so Σ holds; the rigs that build an `InventorySystem` and arrange entries outside the mutator call it after arranging (`InvEventRig` and `InvAutoPlaceRig` in `tests/unit/inventory/InventoryEventRig.cs`, `InvOpRig` in `InvMalformedOpTests.cs`, and `WeightSpeedTests`' direct-placement helper; `PlacementFixture` builds no `InventorySystem` or ledger), as they call `RebuildIndex` today; the existing inventory tests pass in all four legs. Five existing tests were edited, their intent unchanged (Mail Review r1 A5): the three dump-layout tests now read the earlier blocks at tail-relative offsets past this story's appended block (`ContainerCatalogueTests.Dump_InventorySection_HasTheDocumentedLayout_InAscendingIdOrder_AndIsIdenticalAcrossIdenticalWorlds`, `IntakeDrainTests.Dump_TheBacklogRingFollowsTheWorldItemsBlock_CountThenIdsInQueueOrder_IdenticalAcrossIdenticalWorlds`, `WorldItemTests.Dump_InventorySectionCarriesWorldSeqTheItemsInIdOrderTheFifoOrderAndTheTombstones_IdenticalAcrossIdenticalWorlds`), `InventoryApiTests.ConsumeUnit_OnAnEmptyEntry_ReturnsFalse_AndRaisesNothing` gains one `Recount` after arranging, and `SimCountersTests.CounterId_LaterValues_ArePinned` pins the new CounterId 51.
- [x] **Tick-end check (every build)** — `InventoryConservationCheck` (`ITickEndCheck`) checks, per ledger, Σ live vs `spawned + granted − consumed − destroyed` and any negative live counter. A deliberately corrupted `spawned` (test seam) makes the check write one always-on `SimCheckRing` record, new `CheckCode` 15 `InventoryConservationMismatch` (a = ledger: 0 = mail, itemId + 1, with state ordinal + 1 in bits 24–31 for a negative counter, 0 for the Σ check; b = Σ live − expected, or the negative value) and bump new `CounterId` 51 `InventoryConservationMismatches`; the tick completes with no exception in Debug and Release (the corruption touches only `spawned`, so the Debug scan does not trip). *(TD-160 items 5–6; the ring's per-code rate limit applies to the record, not the counter.)*
- [x] **`INVENTORY_ASSERT` full scan** — `[Conditional("INVENTORY_ASSERT")]`, called from the check every tick: per-state recount from storage vs the ledger, and duplicate mailIds (live ids sorted into a preallocated scratch, adjacent equality); a finding asserts new `SimAssertCode` 84 `InventoryConservationScan`. Driven through `InventoryConservationCheck.Check` (the test assembly does not define `INVENTORY_ASSERT`): a seeded duplicated mailId and a seeded lost unit assert in Debug (`Assert.SkipUnless` Debug; each runs in the two Debug legs); one unskipped test asserts the check's body references the scan in Debug and not in Release (`ArchitectureScan.BodyReferences`, `TestBuildExpectation.SimAssertsExpected`).
- [x] **Duplicate `TryInsert` (TD-145, narrowed)** — inserting a unit that was never taken drives Belt negative: in Release the check writes the negative-counter record; in Debug the scan names the duplicated mailId. Counters cannot refuse a per-id crossing; TD-145 is closed as narrowed (TD-160 item 8).
- [x] **Dump** — the inventory `ISimStateContributor` section (SectionId 4, unchanged; fields appended after story 021's backlog block, so existing fixed-offset reads do not move) already writes containers (versions, `lastReplaceVersion`, `statEpoch`, `nextArrivalSeq`), WorldItems, the FIFO and the tombstone ring, and (021) the backlog; this story appends, in order: (1) the ledger — mail row, then itemId rows ascending, each live state, Consumed per reason, Destroyed, `spawned`, `granted`; (2) the entryId allocator (`EntryPool.LastId`); (3) live entries ascending by entryId (preallocated sort; pool slots are reused): entryId, container id, x, y, rotated, count, isMail, mailKind/itemId, addressId, flags, W, H, `arrivalSeq`, mailIds ascending; (4) the per-slot cursor origin (story 010: entry id, container id, x, y, rotated; 0 = none); (5) the per-slot open external (story 011, 8 × u32 in slot order, 0 = none). Not dumped (derived, rebuilt): outfeed nodes (story 012, TD-132 item 1), `PanelIds` and the open masks (TD-146). Two worlds built and driven identically produce byte-identical sections, and for each appended field class a world differing only in it produces a different section.
- [x] **0 B, cost, determinism** — every crossing and the tick-end check allocate 0 B (`AllocAssert.Zero` plus the every-build twin, as `WorldItemAllocationTests`); each crossing is O(1), no scan in any primitive — by inspection, not by a test (every `Add*`/`SetHolder` is a single array write, and `EnterHolder`, `LeaveHolder`, `CountSpawned` and `ConsumeOldest` have no loops; the every-tick Debug scan proves correctness, not cost; Mail Review r1 A1); the Release check is O(item defs), fixed at world build.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ledger/UnitLedger.cs`, `ConservationCheck.cs` (`ITickEndCheck`), `InventoryStateContributor.cs`.
- Counters are per location state × {mail} plus a per-itemId array sized from the ItemDef table (`DefTable<ItemDef>.Count`), preallocated.
- Retrofit: stories 005/012/013/014/015/021 call mutator primitives; this story adds the ledger calls inside those primitives (one place). The existing hook sites are marked `// story 017 ledger hook` (`InventorySystem.cs` `ConsumeUnit`, `DetachForThrow`, `CommitCatch`, `TryInsert`; on PR #35 also the drain and `TryAppendDetached`; `World/PickupCore.cs:280`) and `TODO(story-017)` (`World/WorldItems.cs` `DestroyNonMail`, TD-135); the mutator doc comments (`InventoryMutator.cs` l.34, 205, 420, 496, 541, 604) name the rest. Those stories' tests must still pass, given the `Recount` call in the rigs.
- Holder crossings move into `InventoryMutator.EnterHolder` / `LeaveHolder` (TD-160 item 1); `Place`/`Remove` pick Cursor vs Placed by `IsCursor`; each primitive branches on `e.IsMail` for the mail row vs the per-itemId row.
- `InventoryConservationCheck` (`InventoryPhases.cs`, bound with no arguments today) takes the ledger and the `SimCheckRing`; the next free `CheckCode` is 15 (`Core/CheckCode.cs`), `CounterId` 51, `SimAssertCode` 84, after story 021's 50 and 82. No new EventKind (0x008B stays free).
- §5 note (not this story's work): `TryCatch` (story 014 r1) and `AppendBacklog` (story 021) write ADR-0009 §5's always-on record on an arena refusal; `TryInsert` still refuses with only its no-fit facts (TD-154 item 7) — a code gap for a small hardening change, not 017; the internal `PutAwayCursor` is exempt from §5 (ADR-0009 erratum 2026-10-22, §5; approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT); `TryInsert`'s record is TD-162.
- `SimCheckRing` / always-on records: `production/epics/simulation-core/story-007-simcounters-simcheckring.md`; dump: `production/epics/simulation-core/story-019-canonical-state-dump.md`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 018: the H-R19-1 fuzz (its invariant (a) uses this story's `spawned`) and H-DET-1.
- A load path that reads the section back (TD-137, TD-146).
- A production `Grant` (M1), the item-form `TryInsert` and belts (M-1b, TD-144), death / disconnect cursor moves (M1).
- The §5 arena record in `TryInsert` / `PutAwayCursor` (TD-154 item 7, TD-160).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/UnitLedgerTests.cs` (shape, crossing table, H-X-2, rejects, check, scan, duplicate `TryInsert`), `tests/unit/inventory/InventoryStateContributorTests.cs` (dump), `tests/unit/inventory/UnitLedgerAllocationTests.cs` (0 B), and the sole-writer and IL cases in `tests/unit/architecture/` — must exist and pass in all four legs (net10.0/net8.0 × Debug/Release); a test skipped in one config runs and passes in the other (owner skip rule, decision log 2026-10-19)

**Status**: [x] Created — `tests/unit/inventory/UnitLedgerTests.cs`, `UnitLedgerAllocationTests.cs`, `InventoryStateContributorTests.cs`, `tests/unit/architecture/LedgerWriterRulesTests.cs` and `ConservationScanRulesTests.cs`; all pass on the four legs (net10.0/net8.0 × Debug/Release), each Debug-only test with a Release twin

---

## Dependencies

- Depends on: Story 005, Story 012–015 (all Complete; 014 merged in PR #34 as c2d4a64), Story 021 (PR #35, the Backlog append/drain crossings; merged as main c889f59, merged in at e941cb4, no rebase; PR 35's head 28a25e8 was merged in earlier at efc8ba2)
- Depends on: `production/epics/simulation-core/story-007-simcounters-simcheckring.md`, `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (`INVENTORY_ASSERT`)
- Unlocks: Story 018

---

## Planning defaults (TD-160): items 2–11 Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; item 1 Approved by Evan 2026-10-22 6:41 PM PT (ADR-0009 §6 l.170 erratum)

*Items 2–11 below (TD-160 items 2–11, which stood before Mail Lead's 2:46 PM PT list) were Mail Lead planning defaults and are now Evan's approval. Item 1 (TD-160 item 1) is Approved by Evan 2026-10-22 6:41 PM PT (the ADR-0009 §6 l.170 erratum, Review B1; TD-144). Of TD-160 items 12–15 (added after that list), 13 and 14 are Approved by Evan 2026-10-22 6:41 PM PT (the same erratum); 12 and 15 are Approved by Evan 2026-10-22 7:12 PM PT ("I approve all") (formerly Mail Lead defaults; see Deviations). Consistent with ADR-0009 §5–§6, ADR-0017 §6 and TD-150/152/153/154/155/157/158/159.*

1. Sole writer through two new mutator primitives `EnterHolder` / `LeaveHolder`; the as-built primitive list is written into ADR-0009 §6 by the l.170 erratum of 2026-10-22 (approved by Evan 2026-10-22 6:41 PM PT).
2. `spawned` = first entry into Inventory custody (`Spawn` placement or `AppendBacklog`); unplaced spawn units are Mail's.
3. `granted` and fixture-built live counts come from the `UnitLedger.Recount` test seam (no production Grant at M-1a).
4. The Cursor is its own state; a mid-call detached entry is in no state; `TryInsert` crosses Belt → Placed.
5. Check record `CheckCode` 15 with the payload above, plus `CounterId` 51; the corruption seam corrupts `spawned`.
6. Full scan every tick under `INVENTORY_ASSERT`, sort-based duplicate detection, `SimAssertCode` 84.
7. TD-145 narrowed: negative Belt in Release, the duplicate named by the Debug scan.
8. `Consumed` split by `ConsumeReason`.
9. Dump: append after 021's backlog block in the listed order; counters dumped; derived state not dumped.
10. Estimate L, dump completion kept in 017 (alternative for Evan: split entries / cursor origin / open external into a follow-on dump story).

**Decided (approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT):** `PutAwayCursor` is not an "API caller" under ADR-0009 §5 l.157 (it is the leash side effect of `CloseExternal`, neither an op nor an `IInventoryApi` call) and is exempt from §5's record (ADR-0009 erratum 2026-10-22, §5); `TryInsert`'s one-line record is TD-162.

---

## Completion Notes
**Completed**: 2026-10-22 (PR #36; Mail Lead's docs PR #37 merged as main 43c5348, merged in at 454125d, no rebase; story 021's PR #35 merged as main c889f59, merged in at e941cb4; PR 35's head 28a25e8 was merged in earlier at efc8ba2)
**Criteria**: 11/11 passing (`UnitLedgerTests` unless named otherwise; all in `tests/unit/inventory/` or `tests/unit/architecture/`).
- Ledger shape: `Ledger_AtWorldBuild_HasTheMailRowPlusOneRowPerItemDef_EveryCounterZero`, `RowOf_Mail_IsRowZero_AnItem_IsItsItemIdPlusOne`, `Consumed_IsTheSumOfItsThreeReasonCounters_AndExpectedSubtractsItAndDestroyed`, `Crossings_OnAnOutOfRangeRowOrReason_ChangeNothing`, `LiveStateCount_AndConsumeReasonCount_AreTheMemberCountsOfTheirEnums_ValuedZeroToCountMinusOne` (Mail Review r1 A6)
- Sole writer: `LedgerWriterRulesTests` — `SimAssembly_OnlyInventoryMutatorAndUnitLedgerWriteTheLedger`, `SimAssembly_InventoryMutator_IsTheLedgerWriter`, `UnitLedger_Writers_AreExactlyTheCrossingsAndTheTestSeams`, `RuleEngine_FixtureReader_IsClean`, and the seen-failing fixture `RuleEngine_BadFixture_ReportsExactlyTheExpectedViolations`; since Mail Review r1 A2, `EnterHolder`, `LeaveHolder` and `CountSpawned` are `internal` and `HolderPrimitiveCallerTests` enforces the ADR-0009 §6 erratum's caller list (`SimAssembly_OnlyTheAllowedTypesCallTheHolderPrimitives`: only `InventorySystem`, `WorldItems`, `PickupCore` and `InventoryMutator`; `SimAssembly_WithOnlyTheMutatorAllowed_ReportsInventorySystemAndWorldItems`, `RuleEngine_BadFixture_NamesTheOffendingCallerOfEachPrimitive`, `Primitives_AreTheFiveInternalMethods`)
- Crossing table: `CrossingTable_AFixedScriptOverEveryRow_EachStepMovesExactlyItsDeltas_AndTheSumHoldsAfterEveryStep` (34 steps, ≤ 50; exact deltas per step and Σ for mail and every itemId after every step; review A7 added the Dropped-from-cursor, non-mail cap-eviction and ground-expiry steps; mail timer expiry and water return share `TryReturnMail`, driven by the water steps)
- H-X-2: `HX2_DetachForThrowOfEntry88Of5_PlacedMinus5FlightPlus5_ThenConsumeUnitDelivered_PlacedMinus1ConsumedPlus1`
- Rejects: `Rejects_ARefusedOp_FalseApiCalls_ARefusedPutAway_ADrainSkip_AndARefusedCatch_LeaveEveryCounterUnchanged` (drain skip, refused op, `TryInsert` ×2, `AppendBacklog`, `ConsumeUnit`, `DetachForThrow`, refused put-away, `{0, 0}` catch)
- Recount seam: `Recount_ArrangedStorage_CountsEachLiveStateAndHolder_AndSetsGrantedSoTheSumHolds`, `Recount_AfterAnEntryLeavesItsHolder_ForgetsTheHolderAndItsUnits`; `Recount` is called in `InvEventRig`, `InvAutoPlaceRig`, `InvOpRig` and `WeightSpeedTests`' helper, and the existing suite passes on all four legs with the every-tick Debug scan live
- Tick-end check: `Ids_CheckCode15_CounterId51_SimAssertCode84_ArePinned`, `Check_ABalancedWorldDrivenThroughCrossings_WritesNoRecord_AndCountsNoMismatch`, `Check_ACorruptedSpawned_WritesOneRateLimitedRecord_ACountsEveryTick_AndTheTickCompletes_InEveryBuild` (renamed in Mail Review r1 A5), `Check_DirectlyOnACorruptedItemRow_RecordsTheRowAndTheDelta`, `Check_ANegativeBeltFromAnInsertNeverTaken_RecordsTheStateInBits24To31AndTheValue_InEveryBuild`; `SimCountersTests` pins counter 51
- `INVENTORY_ASSERT` scan: `Scan_Debug_ASeededLostUnit_AssertsWithTheRowAndState`, `Scan_Debug_ASeededDuplicatedMailId_AssertsThroughTheTick_AndNamesTheId`, `Scan_Debug_ADetachedEntryWhoseHolderIsNotWhereItIsHeld_Asserts_AndAnUnheldDetachedEntryDoesNot`, with Release twins `Scan_Release_ASeededLostUnit_…`, `Scan_Release_ASeededDuplicatedMailId_…`, `Scan_Release_AHolderMismatch_IsNotScanned`; unskipped IL tests `ConservationScanRulesTests.Scan_IsConditionalOnInventoryAssert` and `CheckBody_ReferencesTheScan_InAnInventoryAssertBuildOnly`
- Duplicate `TryInsert`: `DuplicateTryInsert_Release_ShowsANegativeBeltRecord_AndTheTickCompletes`, `DuplicateTryInsert_Debug_TheScanNamesTheDuplicatedMailId_AfterTheNegativeBeltRecord`
- Dump: `InventoryStateContributorTests` — `Section_TwoIdenticallyDrivenWorlds_AreByteIdentical`, `Section_TheAppendedBlock_HasTheDocumentedLayout_EntriesAscendingByEntryIdNotBySlot` (since Mail Review r1 A4, every dumped ledger counter has a distinct non-zero value, so a swap of any two fields fails), one differing-world test per appended field class (`…OnlyInTheLedger…`, `…OnlyInTheEntryIdAllocator…`, `…OnlyInOneEntryField…`, `…OnlyInACursorOrigin…`, `…OnlyInAnOpenExternal…`), `Section_TheEarlierBlocks_AreUnmovedByTheAppendedBlock`
- 0 B, cost, determinism: `UnitLedgerAllocationTests.EveryCrossingAndTheTickEndCheck_Over600Cycles_AllocateNothing_InOptimisedBuilds` (the primitive workload plus, since review A1, 300 custody cycles over every AC 3 row) with its every-build twins `EveryCrossingAndTheTickEndCheck_TheMeasuredWindow_GivesTheExpectedResults_AndABalancedLedger_InEveryBuild` and `TheCustodyCrossings_TheMeasuredWindow_GiveTheExpectedResults_AndABalancedLedger_InEveryBuild`; the non-mail cap-eviction destroy runs with the ledger live in `WorldItemAllocationTests.CreateEvictExpireReturnPickupAndResolve_Over400Ticks_AllocateNothing_InOptimisedBuilds` (+ twin); O(1) per crossing: each crossing is a counter add (no scan in any primitive), and the every-tick Debug scan recounting storage equals the ledger across the whole suite (story 016's incremental-vs-recount pattern); the Release check walks the rows fixed at world build; determinism: `Section_TwoIdenticallyDrivenWorlds_AreByteIdentical`
**New values** (append-only): `CheckCode` 15 `InventoryConservationMismatch`; `CounterId` 51 `InventoryConservationMismatches`; `SimAssertCode` 84 `InventoryConservationScan`; TD-160. No new EventKind (0x008B stays free); TD-156 untouched.
**Deviations** (TD-160 items 2–11 approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; items 1, 13 and 14 Approved by Evan 2026-10-22 6:41 PM PT (ADR-0009 §6 l.170 erratum, Review B1); items 12 and 15 Approved by Evan 2026-10-22 7:12 PM PT ("I approve all")): see "Planning defaults" above; as built, holder exits live inside the consuming primitive (`Place` whole, `MergeInto` merged units; item 12), the primitive set also has `CountSpawned` and `ConsumeOldest` (item 13), the WorldItem return into the backlog uses the internal `TryAppendDetached` (seen by the ledger through `EnterHolder`; item 14), and an out-of-range ledger row is ignored by the ledger and the scan alike (accepted advisory, item 15).
**Register**: TD-163 added (Mail Review r1 A3: the holder bytes must be rebuilt on load); TD-135 closed (`DestroyNonMail`/`DestroyDetached` and `CreateWorldItem` report to the ledger); TD-145 closed as narrowed (TD-160 item 8); TD-144 note names the as-built primitives.
**Test Evidence**: 6,249 tests per leg, 0 failed, all four legs (net10.0/net8.0 × Debug/Release): Debug 6,187 passed / 62 skipped, Release 6,242 passed / 7 skipped (at 03525d7, after Mail Review r1 A1–A6; was 6,244 / 6,182 / 6,237 before them — advisory fix, Mail Review PR #36 A12); every guard (each with `--self-check`), the game build and `tools/ci/check-sim-access.sh` pass.
**Run result**: N/A — Sim-only, no Godot surface (no smoke run)
**Code Review**: completion review (Sonnet, read-only): COMPLETE WITH NOTES, no BLOCKING; advisories A1–A9 fixed on Opus (A1 custody 0 B workload, A2/A3 `ConservationCheck` doc, A4 `PlacementFixture` dropped from the Recount AC, A5 accepted as TD-160 item 15, A6/A9 register rows, A7 crossing steps, A8 contributor example).
