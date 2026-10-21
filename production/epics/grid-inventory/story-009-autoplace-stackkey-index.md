# Story 009: AutoPlace (passes M/U/R, PassFirst/TargetFirst), StackKey non-full index and negative-fit cache

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 11:43 AM PT (owner ruling: probe unit per cell, not per anchor — notes on the PASS U/R probe AC and the Mail Review A1 completion note point to the decision log; code fix TD-134; docs only). 2026-10-21 (closed in PR #20; earlier the same day: readiness fixes folded in from the Planner resolutions R1–R9, Lead-authorized: manifest re-stamp (no Inventory/Core rule changed since 2026-10-16); count-change primitive owned here (story 008 now depends on 009); plan shape, index/cache rules, probe counters, fixtures and the readiness ACs added; stale ADR-0025 citation fixed)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-007` (AutoPlace passes; amortised O(1) non-full-stack index)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay
**ADR Decision Summary**: AutoPlace runs PASS M (merge into non-full stacks of the same StackKey, row-major), PASS U (unrotated first-fit, row-major scan; slots: lowest free usable slot ≥ 1), PASS R (rotated, only if w ≠ h); `PassFirst` runs each pass over all targets, `TargetFirst` (mail catch / pickup) runs M→U→R per target. Per container an open-addressed `StackKey → head` map of intrusive non-full lists kept in row-major order, and a negative-fit cache `(shape, rotated) → versionNoFit` (ADR-0009 §5; GDD R8).
**ADR Version**: ADR-0009 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R8-1** — targets `[inventory, second grid]` (the second grid stands in for the M-1b backpack that GDD H-R8-1 names); inventory has 19 letters to #2 at (5,1) and free (0,0); the second grid has 10 letters to #2 at (0,0); AutoPlacing a detached entry of 3 letters to #2 (ascending mailIds) → the lowest id merges into the inventory stack (20), 2 merge into the second stack (12), and **no** new entry is created at inventory (0,0). The merges are committed through the count-change primitive `InventoryMutator.MergeInto`.
- [x] **H-R8-2 (fixture containers)** — targets `[8×2 grid (empty), 8×4 grid with a free 2×4 column at (0,0)]`: a Large lands in the 8×4 grid at (0,0) **unrotated** (pass U over all targets precedes pass R); with that grid removed → the 8×2 grid at (0,0) `rotated = true`; both full → `no_room` (77), no entry created and every target's version unchanged; a mail-only target (`po_intake`, the only mail-only def at M-1a) and a Plank → `not_allowed` (73); a mail-only target with no space and a letter → `no_room`. (bike/chest are 8×2 and 8×4 instances of the unrestricted `player_inventory` def via `PlacementFixture.CreateContainer(defId, w, h)` — no chest/bike def exists at M-1a; their content is M-1b; Large is 2×4.)
- [x] `MergePolicy.NoMerge` skips pass M; `effMax == 1` skips pass M; `TargetFirst` fills the first target (M→U→R) before the next.
- [x] The StackKey index stays equal to a brute-force scan of non-full stacks (count < effMax read at op time; stats constant during the run), each list in row-major order, across 10 000 random `Place`/`Remove`/`Transfer`/`MergeInto` calls (property test); PASS M probes are ≤ list length (1–3 typical) per target, never a container scan; PASS U and PASS R count one probe per candidate anchor (Release-compiled probe counters). *(Superseded 2026-10-21 11:43 AM PT by owner ruling: probes count per cell, as GDD H-PERF-6 says, not per anchor — decision log; code fix TD-134.)*

*Added by readiness (GDD R8/R15, ADR-0009 §3/§5; Planner resolutions 2026-10-21):*

- [x] **Slots layout** — on a slots container, PASS U takes the lowest free usable slot ≥ 1 and never slot 0 (reserved_slot is skipped, not returned); PASS M merges into the first mergeable slot in slot order.
- [x] **Partial and all-or-nothing** — a 5-unit stackable into room for 3 places the 3 oldest ids and leaves the 2 newest on the source, in id order, reply ok; a non-stackable that does not fit whole places nothing and reports `no_room`.
- [x] **Negative-fit cache** — after a (shape, rotated) fails at version v, a repeat AutoPlace of that shape on that container at v visits 0 anchors; a version bump, a statEpoch bump or a row change clears it. With the cache disabled, the same 10 000-call sequence yields identical plans, replies and state.
- [x] **effMax change** — after a test `MailMaxStack` push (`GridMailRuleFixtures.DoubleStackPm`), a statEpoch bump and `RebuildIndex` on the container, a 1-letter AutoPlace merges into an existing 20-stack that is now non-full at effMax 40.
- [x] **Allocation** — the index, the negative-fit cache and the AutoPlace plan allocate 0 B per call after warm-up (`AllocAssert`, Release, in a `[Collection(NonParallelCollection.Name)]` class); capacities come from `SimSettings` / the container's cell count at creation.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Placement/AutoPlace.cs`, `StackIndex.cs`, `NegativeFitCache.cs`.
- The index is maintained inside `InventoryMutator` on insert, remove, count change and move (ADR-0009 §5) — not by AutoPlace itself. Story 005 shipped only `Place`, `Remove` and `Transfer`; this story adds the count-change primitive `InventoryMutator.MergeInto(ref plan, sourceSlot, targetSlot, units)`: take the `units` oldest mailIds from the source (arena take), union them into the target, set both counts, raise `InvMerged` plus its mailId-run `Continuation` chunks, remove and free a source left at 0, and update the index. Story 008's Merge op reuses it; story 017 adds its ledger hook (a Placed→Placed merge crosses no ledger state, so ADR-0009 §6 is untouched).
- Index layout: per container an open-addressed `StackKey` (packed u64) → head map (a new preallocated 64-bit map, never enumerated) of intrusive row-major lists. PASS M always re-checks `count < effMax` at op time (GDD R8). `RebuildIndex(containerSlot)` is called when a container's `statEpoch` bumps (nothing bumps it at M-1a; tests call it).
- Use the rules shipped in story 006: `PlacementSubject.ForMail` / `ForItem` for effMax (read at op time, never cached), `StackKey`, `PlacementRules.CanMerge` in PASS M, and `InsertionWhitelist.Check` once per target (non-zero → the target is skipped in every pass). PASS U/R test anchors with `Occupancy` row masks (one probe each), not `PlacementRules.Validate` per position. Nothing moved: if no target admitted → `not_carryable` if any target was skipped for carryability, else `not_allowed` (ADR-0009 §2 chain order); otherwise `no_room`.
- Probe counters (manifest guardrail; `Occupancy` defers them to this story): always-on `CounterId`s for PASS M candidate visits and PASS U/R cell probes, compiled into Release, excluded from the dump (ADR-0006). This definition binds H-PERF-6 later. This story also creates `MergePolicy {Merge, NoMerge}` and the `PassFirst` / `TargetFirst` order enum.
- The StackKey index and negative-fit cache are derived memos: excluded from the canonical dump (ADR-0006), rebuilt from entries on load, never change a result. The negative-fit cache stays ADR-literal (`(footprintShape, rotated) → versionNoFit`, ADR-0009 §5) and is additionally cleared on a statEpoch bump or row growth. A handler's `Validate` only **reads** the cache; no-fit records are written on the `Commit` path and by `IInventoryApi`-style callers, never inside `Validate` (ADR-0009 §2).
- Do not name a type `Placement` (`InvPlan.Placement` and `PlacementRules` exist); the namespace is `PerformativeMail.Sim.Inventory.Placement`, fully qualified where it shadows (TD-074 pattern).
- Arrange test entries through `InventoryMutator` (or call `RebuildIndex` after arranging): `PlacementFixture.AddMail`/`AddItem` and `InvEventRig.AddMail` bypass the mutator, so the index would be empty.
- AutoPlace is pure: it fills a fixed-size `AutoPlacePlan` (merges `(targetEntrySlot, units)` in PASS M order, plus at most one free-cell `InvPlacement` for the remainder, plus the unplaced count) and does not widen `InvPlan` (story 005 is Complete). Input = `PlacementSubject` + unit count + optional source entry slot, so Spawn (story 012) needs no pre-built entry. During validation it `Touch`es every target it may change (TD-118 item 1) and calls `InvPlan.AddAcquiredBlock` per mail merge (reason 85 rule). `Commit` applies it through `MergeInto` then `Place`/`Transfer` and never recomputes (ADR-0009 §2). The source keeps its entryId; merged units leave oldest-first. Partial placement is allowed for stackables; non-stackables are all-or-nothing.
- Per-shape monotone start hints (GDD D3) serve the backlog drain (story 021). The ADR that records them, ADR-0025, is planned and not yet written. AutoPlace does not need them for correctness.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 010: R9 target orders.
- Story 014: `TargetFirst` callers (pickup, catch).
- M1/ADR-0025: sort hints.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/AutoPlaceTests.cs` — must exist and pass

**Status**: [x] Created and passing — `tests/unit/inventory/AutoPlaceTests.cs`, `StackIndexPropertyTests.cs`, `AutoPlaceAllocationTests.cs`

---

## Dependencies

- Depends on: Story 005, Story 006
- Unlocks: Story 008, Story 010, Story 012, Story 014, Story 015

---

## Completion Notes
**Completed**: 2026-10-21 (PR #20, stacked on PR #16)
**Criteria**: 9/9 passing (4 story ACs + 5 readiness ACs; none deferred)
- H-R8-1: `HR81_ThreeLettersToTwo_LowestMergesIntoInventoryTo20_TwoIntoSecondGridTo12_NoNewEntry_ViaMergeInto`
- H-R8-2: `HR82_LargeLandsInThe8x4FreeColumnUnrotated_PassUOverAllTargetsPrecedesPassR`, `HR82_WithTheChestRemoved_LargeLandsInTheBikeAt00Rotated`, `HR82_BothFull_NoRoom77_NoEntryCreated_EveryTargetVersionUnchanged`, `HR82_MailOnlyIntakeAndAPlank_NotAllowed73_VersionUnchanged`, `HR82_MailOnlyIntakeWithNoSpaceAndALetter_NoRoom`, `NothingAdmitted_NotCarryableBeforeNotAllowed_WhenAnyTargetSkippedForCarryability`
- NoMerge / effMax 1 / TargetFirst: `NoMerge_SkipsPassM_…`, `EffMaxOne_SkipsPassM_…`, `Mediums_EffMaxOne_NeverMerge_…`, `TargetFirst_FillsTheFirstTarget_MThenUThenR_BeforeTheNext_…`, `TargetFirst_FirstTargetHasNoRoom_…`, `TargetFirst_ANoSourceSpawnOfMoreThanEffMax_…`
- Index property + probes: `StackIndexPropertyTests.TenThousandRandomCalls_IndexEqualsBruteForceScanOfNonFullStacks_RowMajor_AutoPlaceMatchesTheOracle_ProbesBounded` (PASS M visits equal the oracle's exact count, two-sided), `PassM_ALinkedStackFullAtTheCurrentEffMax_IsSkippedAtOpTime_…`, `ProbeCounters42And43_AreNotInTheCanonicalDump_…`, `ProbeCounters_PassMVisitsTheKeysListOnly_PassUCountsOneProbePerAnchorUpToTheFirstFit`, `Index_FollowsPlaceRemoveAndTransfer_RowMajor_…`, `RebuildIndex_UnlinksAStaleLinkWhoseCellsWereClearedOutsideTheMutator`
- Slots layout: `Slots_PassUTakesTheLowestFreeUsableSlotFrom1_NeverSlot0_…`, `Slots_EverySlotFrom1Full_NoRoom_…`, `Slots_ASlotsContainerWithoutReservedSlots_…`
- Partial / all-or-nothing: `Partial_FiveStackableUnitsIntoRoomForThree_…`, `AllOrNothing_ANonStackableThatDoesNotFitWhole_PlacesNothing_NoRoom`, `PartOfAPlacedStack_…`, `APlacedSourceWithNoMergeTarget_…`, `CountAboveEffMax_*` (4)
- Negative-fit cache: `NegativeFit_ARepeatOfAFailedShapeAtTheSameVersion_VisitsZeroAnchors`, `NegativeFit_AVersionBump_ClearsIt`, `NegativeFit_AStatEpochBump_ClearsIt_AndSoDoesRebuildIndex`, `NegativeFit_ARowChange_ClearsIt`, `NegativeFit_CacheDisabled_…`, `NegativeFit_ValidateOnlyReads_…`, `StackIndexPropertyTests.TheSameTenThousandCallSequence_WithTheNegativeFitCacheDisabled_GivesIdenticalPlansRepliesAndState`
- effMax change: `EffMaxChange_DoubleStackPushStatEpochBumpAndRebuildIndex_AOneLetterAutoPlaceMergesIntoTheFormerlyFull20Stack`
- 0 B: `AutoPlaceAllocationTests.IndexCacheAndAutoPlacePlan_Over10000Calls_AllocateNothing_InOptimisedBuilds`, `MergeCommitAndPlaceRemove_Over1000Cycles_AllocateNothing_InOptimisedBuilds` (Release only), each with an `…_InEveryBuild` twin that runs in Debug and Release (owner skip rule)
- MergeInto (R1): `MergeInto_*` (7); TD-116 / R4: `TenMailMergesInOnePlan_Succeed_NoAssert66_NoReason85`, `OneHundredSingleUnitMailMergesAcrossFourTargets_…`, `TwoHundredFiftyFiveSingleUnitMerges_TheExactWorstCase_…` (items), `TwoHundredFiftyFiveSingleUnitMailMerges_TheExactWorstCaseWithMail_…` (enlarged test arena), `MergeBound_CoversEveryLegalAutoPlace_AndThePlanStructsStayStackSized`
- Duplicate targets (Mail Review A2): `DuplicateTarget_IsBadRequest_BeforeAnyMutation_SoOneStackIsNeverMergedTwice` (PassFirst, TargetFirst), `DuplicateTarget_NotAdjacent_IsBadRequest_AndThePlanIsUntouched`
**Deviations**:
- ADVISORY: fixture grids are built with `InvAutoPlaceRig.CreateGrid(w, h, "player_inventory")` (same def and dims as `PlacementFixture.CreateContainer`, but in a mutator-backed world so the index is maintained, per R8); `PlacementFixture.Index` is reused for def lookups.
- ADVISORY: the mutator now stores slots-layout entries as 1×1 unrotated, matching `PlacementRules` (it used w×h before) — TD-130 item 6.
- TD-116 (R4, default "raise"): `InvPlan.MaxAcquiredBlocks` 8 → 263 (`AutoPlacePlan.MaxMerges` 255 + 8 handler headroom); PASS M never stops early. New `SimAssertCode` 69 (`InventoryMergeInvalid`), 70 (`InventoryAutoPlaceMergeBoundExceeded`); new `CounterId` 42 (`InventoryMergeCandidates`), 43 (`InventoryPlacementProbes`). Validate only reads the negative-fit cache (no ADR-0009 §2 change). New TD-129, TD-130.
- Mail Review A2 fix: `AutoPlace.Plan` refuses a target list naming the same container twice (`bad_request`, before any mutation; GDD R8/R9 target lists are distinct containers, ADR-0009 §2), and PASS M never plans a stack twice. `Plan` split into `IsWellFormed`/`Admit` and the PASS M merge step extracted (A6, no behaviour change).
- Out-of-scope files touched (Mail Review A5), each for this story's wiring only: `src/PerformativeMail.Sim/Inventory/Storage/MailIdArena.cs` (`ushort` `CanReserve` overload for the 263-block plan), `src/PerformativeMail.Sim/Core/EventLogFacade.cs` (internal `Counters` accessor for the probe counters), `src/PerformativeMail.Sim/Inventory/Ops/InvRequestCheck.cs` (`ushort` `CheckArenaReservation` overload), `src/PerformativeMail.Sim/Inventory/InventorySystem.cs` (AutoPlace/index wiring), `src/PerformativeMail.Sim/Primitives/SimAssertCode.cs` and `src/PerformativeMail.Sim/Core/CounterId.cs` (appended values 69/70, 42/43), `tests/unit/sim/Core/SimCountersTests.cs` (counter-length pin 44), `tests/unit/inventory/InventoryEventRig.cs` (mutator-backed `InvAutoPlaceRig`, slots 1×1 move check, optional arena size).
- Mail Review A1 (probe unit per anchor vs per cell) and A3 (one shared index table vs ADR §5's per-container map) are recorded as erratum items in TD-129. A1 was decided 2026-10-21 11:43 AM PT (owner ruling, decision log): per cell, as the GDD says; the shipped per-anchor count is fixed by a follow-up PR (TD-134).
**Test Evidence**: Logic — `tests/unit/inventory/AutoPlaceTests.cs`, `StackIndexPropertyTests.cs`, `AutoPlaceAllocationTests.cs`; 5,145 tests per leg, 0 failed (net10.0/net8.0 × Debug/Release)
**Run result**: N/A — Sim-only, no Godot surface (no smoke run)
**Code Review**: read-only review (Sonnet): APPROVE WITH SUGGESTIONS; suggestions 1–3, 6–8 applied in cfc958a, 4–5 recorded as TD-129/TD-130
