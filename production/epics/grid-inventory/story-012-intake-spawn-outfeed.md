# Story 012: Spawn into the Intake (NoMerge, arrivalSeq) and the outfeed FIFO (PeekOutfeed / TakeUnit)

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1.5 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (readiness fixes folded in from the handoff's Planner resolutions R1–R10, Lead-authorized: manifest re-stamp (no Inventory/Core rule changed since 2026-10-16); estimate M → M–L; requirement line; Spawn signature with the StackKey; arrivalSeq/FIFO in the mutator; `IInventoryApi` created here; `ReturnToIntake`; `TakeOldest`/`UnitRef`/`Selector`; backlog seam; 64×100 perf container; PR #19 pin; tick guard; readiness ACs added)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-010` (outfeed by arrivalSeq; Peek/Take ≤ 4 entries amortised — pulled forward with Spawn, belts are M-1b), `TR-sim-019` (in-tick callers use the synchronous `IInventoryApi` in their own phase; ADR-0003 §5)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: `Spawn(container, units, NoMerge)`: one unit per entry, first-fit; every insertion into the Intake is NoMerge; a returned entry enters whole; each Intake entry gets a per-Intake `arrivalSeq` (u32); outfeed = an index-linked FIFO of `(arrivalSeq, entrySlot, entryId)` nodes with lazy tombstones; `PeekOutfeed` returns the lowest mailId of the lowest-`arrivalSeq` live entry (ADR-0009 §5; GDD R14).
**ADR Version**: ADR-0009 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Forbidden: a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, a per-entry `List<uint>` for `mailIds` (ADR-0009 Alts 3–5)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Forbidden: routing automation through the inventory op FIFO; an event-per-mutation with embedded version pairs instead of the seal (ADR-0009 Alts 1–2)
- Required: in-tick callers use synchronous `IInventoryApi` in their own phase (ADR-0009 Decision; ADR-0003 §5)
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R14-1** — GIVEN an empty Intake, WHEN 3 letters to #4 (ids 100–102) spawn NoMerge, THEN 3 entries of count 1 at (0,0),(1,0),(2,0) in id order; `PeekOutfeed` = 100; after `TakeUnit(FIFO)` → 101 and (0,0) is empty; GIVEN a returned letter id 50 is then inserted, THEN it is a separate count-1 entry and `PeekOutfeed` is still 101 (arrival order, not mailId).
- [x] **H-R14-4 (whole-return half)** — GIVEN an Intake holding a 5-letter entry to #9, WHEN a detached 20-letter entry to #9 (ids 200–219) is returned through `InventorySystem.ReturnToIntake`, THEN the Intake gains exactly **one** new entry of count 20 with one new `arrivalSeq` and the same entryId, the resident entry is unchanged (NoMerge), Intake version +1.
- [x] **H-PERF-6a** — `PeekOutfeed` and `TakeUnit(FIFO)` visit ≤ 4 candidates per call amortised over 10 000 calls (balanced take/spawn so n stays constant) at n = 320 (`po_intake`, 20×16) and at n = 6 400 on a **64×100** test container (the GDD's 80×80 cannot be built: `Occupancy.BitmapWidth` = 64, ADR-0009 §5; GDD text flagged for re-review); fails on any O(n) scan (Release probe counter `CounterId.InventoryOutfeedCandidates`, one increment per FIFO node inspected, tombstone pops included).
- [x] `SpawnResult` reports how many ids were placed (span order, stopping at the first id that finds no cell or that the entry pool / arena refuses, nothing mutated for that id); the unplaced remainder is `ids[Placed..]` in order. A full Intake → `Placed = 0`, no entry allocated, version unchanged; `AutoPlace.RecordNoFits` is called after the reject, outside planning. The "nothing overtakes the backlog" gate lives with the backlog, story 021 (ADR-0017 §6): this story exposes one internal `IntakeAccepting` predicate (always true at M-1a) consulted by `Spawn` and `ReturnToIntake`.

*Added by readiness (Planner resolutions 2026-10-21):*

- [x] **Whitelist pin** — `Spawn` into `po_intake` with the M-1a `BuiltInContent` passes `InsertionWhitelist.Check` (mail category, `AcceptsInsert` true); a Plank → `not_allowed`, nothing mutated.
- [x] **Every Intake insertion stamps** — an entry placed into the Intake by any mutator path (Spawn, `ReturnToIntake`, and later player ops 007/008/010) gets a fresh `arrivalSeq` and a FIFO node; leaving the Intake resets `ArrivalSeq` to 0 and leaves a lazy tombstone.
- [x] **Tick guard** — `Spawn`, `TakeUnit` and `ReturnToIntake` called outside a tick phase are refused with nothing mutated and no event (asserts under `SIM_ASSERT`; Release ignores and counts) — ADR-0003 §5.
- [x] **Allocation** — `OutfeedFifo`, `Spawn`, `PeekOutfeed`, `TakeUnit` and `ReturnToIntake` allocate 0 B per call after warm-up (`AllocAssert`, Release, `[Collection(NonParallelCollection.Name)]`).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Intake/OutfeedFifo.cs` (one FIFO per container whose def has `BeltAccess.OutfeedOnly`; nodes `(arrivalSeq u32, entrySlot u16, entryId u32)` in one preallocated pool; compaction when tombstones exceed live nodes). **This story creates** `Inventory/IInventoryApi.cs` (`Spawn`, `PeekOutfeed`, `TakeUnit`, plus `SpawnResult`, `UnitRef`, `enum Selector : byte { Fifo }` — Street is M-1b); `InventorySystem` implements and publishes it for `SystemGraph` dependents. Story 015 adds `ConsumeUnit`/`DetachForThrow`/`TryInsert`/`CountItem`. The `IGridInventoryApi` comment names in `SimComposition.cs` / `SimSystemContext.cs` become the ADR name `IInventoryApi`.
- **Spawn (R1):** `SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p)` — one StackKey per call (Inventory never reads Mail, ADR-0017 §5), ids ascending, 1..255. One `EventLogFacade.BeginCall` scope and one `Begin`/`End` bracket per call (one Intake seal), actor `ActorId.System`, cause `InvCause.System`. Per id: `PlacementSubject.ForMail` → `AutoPlace.Plan(units: 1, NoSource, [intake], NoMerge, PassFirst)` → arena reservation check → `EntryPool.TryAllocate` + one-unit arena block → `AutoPlace.Commit(…, newSlot)` (a NoSource plan places ≤ effMax units as one entry, TD-130 item 2, so the loop is per id for R14's one unit per entry). `SpawnResult { ushort Placed; ushort Unplaced; }`. Mail (mail-destinations 014) calls it once per unit with a 1-id span (GDD M-PERF-1d: exactly c_b `Spawn` calls; R11).
- **arrivalSeq + FIFO in the mutator (R2):** new `ContainerHeader` fields: outfeed handle (head/tail) and `NextArrivalSeq` (u32, first stamp 1; `EntryRecord.ArrivalSeq` 0 = not an Intake entry). `InventoryMutator.Place`/`Transfer` into an outfeed container stamp `e.ArrivalSeq = h.NextArrivalSeq++` before `RaiseInserted` and append the node; `Remove`/`Transfer`-out reset `ArrivalSeq` to 0 and leave the tombstone; `PeekOutfeed` pops heads whose `(entrySlot, entryId, arrivalSeq)` no longer match a live placed entry. `NextArrivalSeq` is **state** (dumped by `InventoryStateContributor` after `StatEpoch`); the FIFO nodes are a derived memo (not dumped, rebuilt from live entries by `arrivalSeq` on load, reset on container-slot reuse — TD-129 item 4).
- **Every Intake insertion stamps — as-implemented clarifications (2026-10-21, Sonnet review):** a move inside the Intake (a same-container `Transfer`) is not an insertion: the entry keeps its `arrivalSeq` and its FIFO node, so its outfeed position is unchanged (pinned by `AMoveInsideTheIntake_KeepsTheEntrysArrivalSeq_AndItsFifoPosition`). A `MergeInto` onto an Intake entry does not re-stamp the target: it keeps its `arrivalSeq` and node, and the merged-in units are served from its position, lowest id first (pinned by `MergeIntoAnIntakeEntry_DoesNotRestampTheTarget_AndKeepsItsFifoPosition`).
- **Return-to-Intake (R4):** internal `bool InventorySystem.ReturnToIntake(ushort detachedEntrySlot, ActorId actor, InvCause cause)` (callers: story 013 expiry/water; bags later): `AutoPlace.Plan(units = count, sourceSlot = entry, [intake], NoMerge, PassFirst)` + `Commit`; enters whole, keeps its entryId, gets a new `arrivalSeq`; `false` with nothing changed on no room (story 021 adds the backlog branch). Not on `IInventoryApi` until Mail's Payday Loot return (M1).
- **TakeUnit (R5):** new count-change primitive `InventoryMutator.TakeOldest(ref plan, entrySlot, k, Span<uint> taken)` (arena take k oldest, `SetCount`, index update, `InvRemoved{c, entry, k}`, `Remove` + free at 0); story 015's `ConsumeUnit` reuses it. `TakeUnit(c, Fifo, actor, out UnitRef u)` pops stale heads, takes the head entry's lowest mailId, seals with the caller's actor, cause `None`. `readonly record struct UnitRef(MailId Mail, EntryId Entry, DefIndex MailKind, uint AddressId, byte Flags)`. `PeekOutfeed` is read-only (no scope, no seal; its tombstone pops touch only the memo). On a non-Intake container both return false. The Placed → Belt ledger crossing is story 017's hook.
- **Backlog seam (R6):** one internal `bool IntakeAccepting` predicate (always true at M-1a) at the top of `Spawn` and `ReturnToIntake`; story 021 fills it. Mail-destinations 014's own pre-check stays.
- **Perf fixture and counter (R7):** a 64×100 test container from a test content set (`BuiltInContent.CreateM1a()` + a 64-wide mail-only grid def with `BeltAccess.OutfeedOnly`, `AcceptsInsert = true`, `View` set, `inventory.maxRows` = 100, `inventory.mailIdArenaUnits` ≥ 65 536), created via `InventorySystem.TryCreateContainer`. Node pool preallocated (≥ 2 × live entries; capacity key or derivation is the dev's call, logged). New always-on `CounterId.InventoryOutfeedCandidates`, Release-compiled, not dumped.
- **PR #19 neutral defaults (R8):** `ContainerDef.AcceptsInsert` defaults true today and `BuiltInContent` does not set it on `po_intake`; content-data 018 flips the default and spells values out. Pinned by the whitelist AC; `BuiltInContent` is not edited here (018 owns it). Any test def this story builds spells out `AcceptsInsert` and `View`.
- **Tick access and guard (R9):** `InventorySystem` takes `ITickReader` from `SimSystemContext` (for `EntryPool.TryAllocate(tick, …)` and arena records) and checks the ADR-0003 §5 guard (`Stage == InPhase`) at the top of each mutating API method.
- **Spawn probe cost (R10, non-blocking):** NoMerge first-fit restarts at (0,0) per id; a near-full Intake can cost an estimated ~12–16k cells examined per tick at the GDD steady-state mix (GDD per-cell unit; ≈ 11–13k anchors; TD-133 item 1). No BLOCKING gate covers it; accepted for M-1a (optional TD for a within-call start hint, GDD D3).
- Actor for spawns/returns is `ActorId.System`; mail ids come from Mail (`MailIds` allocator, `production/epics/simulation-core/story-018-id-allocators-slottable.md`) — Spawn receives the StackKey parts plus `ReadOnlySpan<MailId>` (see Spawn above).
- The Intake-sort FIFO rebuild and Presort street selectors are M1/M-1b; `TakeUnit(FIFO)` on non-Intake containers (min-heap, inserters) is M-1b automation.
- Intake insertion uses AutoPlace with `NoMerge` (story 009).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 021: backlog append and the `IntakeDrain` phase (Blocked on ADR-0017).
- Story 013: WorldItem expiry/water returns call `InventorySystem.ReturnToIntake`.
- M-1b: inserter/belt `TakeUnit` selectors, Presort.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/IntakeSpawnOutfeedTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/inventory/IntakeSpawnOutfeedTests.cs`, `OutfeedPerfTests.cs`, `IntakeAllocationTests.cs`; all pass on net10.0/net8.0 × Debug/Release

---

## Dependencies

- Depends on: Story 005, Story 009
- Depends on: `production/epics/simulation-core/story-018-id-allocators-slottable.md`
- Related: `production/epics/content-data/story-018-schema-and-build-stages.md` (PR #19 ruling: `ContainerDef.AcceptsInsert` defaults to false once 018 lands — test defs here spell out `AcceptsInsert = true` and `View`)
- Unlocks: Story 013, Story 018, Story 021; mail-destinations epic (spawner)

## Completion Notes
**Completed**: 2026-10-21 (PR #21)
**Criteria**: 8/8 passing (4 story ACs + 4 readiness ACs; none deferred)
- H-R14-1: `SpawnThreeLetters_ThenTakeOne_ThenReturnALowerId_PeekFollowsArrivalOrderNotMailId`
- H-R14-4 (whole-return half): `ReturnToIntake_ADetached20LetterEntry_EntersWholeAsOneNewEntry_KeepsItsId_ResidentUnchanged_VersionPlusOne`, `ReturnToIntake_NoRoom_ReturnsFalse_AndChangesNothing`
- H-PERF-6a: `OutfeedPerfTests.PeekAndTake_AtN320OnPoIntake_…`, `PeekAndTake_AtN6400OnA64x100Container_…`, `DrainingAnIntakeWithHalfItsEntriesRemovedMidQueue_…` (measured mean 1.500 / 1.500 / 2.489 candidates per call; bound 4); counter unit pinned per candidate visit
- SpawnResult / RecordNoFits: `Spawn_IntoAFullIntake_PlacesNothing_…`, `Spawn_IntoANearlyFullIntake_…`, `Spawn_AfterAPartialSpawn_…_ProbesNothing_…`, `Spawn_WhenTheArenaRefusesTheBlock_…`, `Spawn_Malformed_…`, `Spawn_MergePolicyOnTheIntake_IsNoMerge_…`
- Whitelist pin: `Whitelist_PoIntakeOfTheM1aContent_AcceptsMail_AndRefusesAPlankWithNothingMutated`
- Every Intake insertion stamps: `EveryIntakeInsertion_Stamps_AndLeaving_ResetsTheStamp_…`, `AMoveInsideTheIntake_KeepsTheEntrysArrivalSeq_…`, `MergeIntoAnIntakeEntry_DoesNotRestampTheTarget_…`, `PeekOutfeed_SkipsAMidQueueTombstone_…`, `StateDump_CarriesNextArrivalSeq_AndARebuiltOutfeed_…`, `StateDump_IsIdenticalAcrossTwoWorlds…`
- Tick guard: `MutatingCalls_OutsideATickPhase_AreRefused_WithNothingMutatedAndNoEvent`
- Allocation: `IntakeAllocationTests.SpawnPeekTakeAndReturn_Over1000Cycles_AllocateNothing_InOptimisedBuilds` (Release) with the `…_InEveryBuild` twin (owner skip rule)
- Also: `TakeUnit_OnAMultiUnitEntry_…`, `TakeOldest_*` (2), `ANonMailOutfeedHead_…`, `PeekOutfeed_IsReadOnly_…`, `PeekAndTake_OnAContainerWithoutAnOutfeed_…`
**Deviations** (all ADVISORY, as-implemented):
- Spawn: per-id scratch plan for the arena reservation check (all ids commit into one plan, one seal); a malformed or out-of-phase call returns `SpawnResult(0, ids.Length)` (no reason field, TD-133 item 5); `MergePolicy` is forced to NoMerge on a container with an outfeed and honoured elsewhere; the failing id's no-fit facts are recorded after `End` at the sealed version (`AutoPlace.RecordNoFitsAtCurrentVersion`).
- FIFO: the node count lives in `OutfeedFifo`; the header holds head, tail and `NextArrivalSeq` (dumped for every container, 1 without an outfeed). No inventory load path exists yet — `InventoryMutator.RebuildOutfeed` is provided and the round-trip test rebuilds in-process (TD-132). Node pool = 2 × `entryPoolCapacity`, no new balance key.
- Tick guard: `Core/TickGuard.IsMutable` checks `Stage == InPhase && Phase ≤ RunStep` (full ADR-0003 §5 rule); new `CounterId` 44 (`InventoryOutfeedCandidates`, one per candidate visit), 45 (`MutatingApiOutOfPhase`); new `SimAssertCode` 71 (out-of-phase), 72 (FIFO node pool empty), 73 (invalid `TakeOldest`), 75 (non-mail outfeed head; Release reads as empty). 74 is the 009 follow-up (PASS M planned-twice guard).
- `InventorySystem`'s constructor takes `ITickReader`; `InventoryEventRig` passes it and gains a `MailStep` hook so tests call the API inside a real tick. Pins updated: `SimCountersTests` (44/45, length 46), `ContainerCatalogueTests` (dump record 28 B).
- New TD: TD-131 (tick-guard follow-ups), TD-132 (outfeed lifecycle seams), TD-133 (Spawn cost/API notes, incl. R10's start hint).
**Test Evidence**: Logic — see above; 5,211 tests per leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 5,165 passed / 46 skipped, Release 5,209 passed / 2 skipped)
**Run result**: N/A — Sim-only, no Godot surface (no smoke run)
**Code Review**: read-only review (Sonnet): APPROVE, no blocking findings; items 1–3 and 8 applied in 2c29d82, 4–7 recorded as TD-132/TD-133, 9 (Release-only 0 B test) covered by CI's Release legs and the twin
