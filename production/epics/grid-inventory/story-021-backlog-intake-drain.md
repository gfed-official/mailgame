# Story 021: Backlog append and the IntakeDrain phase (≤ 32 entries/tick, idle exit O(1))

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1.5–2 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (close-out at `/story-done` on Sonnet, PR #35, open, not merged; was stacked on PR #34 (merged PR 34 round-1 head 09804ab at 417a8ad) until PR #34 merged into main c2d4a64, which the branch merged in at 5e9fe8d (no rebase): 13/13 ACs ticked, Test Evidence, Completion Notes; completion-review fixes — drain tick guard, a 0 drain cap no longer re-runs the pass, tightened H-R14-3 asserts, a full-Intake young-mail cap test, 0x008A and assert 82 pinned, TD-138 closed, TD-158 item 8. Before that: readiness fixes against main f504231, independent of story 014 / PR #34: Decision Summary re-derived from ADR-0017 §6; `AppendBacklog`/`BacklogEntry`/`BacklogUnits`/`BacklogEntries` shapes, ring capacity, drain gating, start hints and probe accounting, both fresh-`arrivalSeq` cases, the 013 interim-rule retirement, versions/events/rejects/0 B/dump ACs; Mail Lead planning defaults pending Evan, TD-157. Before that, docs only: cap-eviction-to-backlog and interim-rule retirement ACs added from TD-138 / owner ruling Q3; fresh-`arrivalSeq` merge AC (Evan 12:28 PM PT); TR-inventory-011 probe bound 8,500 to match GDD H-PERF-6c / ADR-0009 l.57; Manifest Version re-stamped 2026-10-17.1; re-derive the rest at `/story-readiness`)

> **Unblocked 2026-10-16**: ADR-0017 is Accepted; the Decision Summary was re-derived from its §6 on 2026-10-22 at `/story-readiness` (Inventory stores and drains the backlog; Mail appends through `IInventoryApi.AppendBacklog`).

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-011` (backlog drain ≤ 32 entries/tick, ≤ 8,500 probes, 0 when idle)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0017: Mail table, destination registry & IWalletGate — Accepted 2026-10-16 (primary, §6); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (binding table, §3–§6, l.312); ADR-0003: Phase Order, Composition Root and Cross-System Calls (§1 phase 2, §5 in-phase `I…Api` calls)
**ADR Decision Summary**: The backlog's storage and drain are Inventory's (ADR-0017 §6; GDD R14 l.141 and the state table l.164 still say "owned by Mail & Destinations" — superseded by the Accepted ADR, GDD errata pending, TD-157 item 8): `Sim/Inventory/Backlog` holds an append-ordered ring of whole mail entries with O(1) append. Mail appends through `IInventoryApi.AppendBacklog(in BacklogEntry)` in phase 8 at the batch tick for the units `Spawn` did not place (`Spawn` is per same-key group — Evan 2026-10-21 1:21 PM PT; Mail appends the remainder through `AppendBacklog` — Mail Lead planning default pending Evan, TD-157 item 2); Inventory's own returns (expiry, water float, cap eviction) append internally through the mutator. Units cross into the ledger's `Backlog` state at append and back to `Placed` at drain (ADR-0009 §6; the ledger calls are story 017's). The drain is phase 2 `IntakeDrain` (ADR-0003 §1; ADR-0009 `Bind(PhaseId.IntakeDrain, …)`, bound empty since story 001): append order, NoMerge whole entries each with a fresh `arrivalSeq`, at most `backlogDrainPerTick` (32) per pass; O(1) exit with 0 probes when the backlog is empty or the Intake has no free cell; otherwise a pass runs only when the Intake `version` changed by something other than the drain's own seal, the backlog grew, or the last pass hit the cap (ADR-0009 l.312: gating by `version` and the negative-fit cache); an entry whose footprint area exceeds the free-cell count is skipped in O(1); per-shape monotone start hints within a pass; no head-of-line blocking (GDD R14). Mail reads `BacklogUnits`/`BacklogEntries` through `IInventoryApi` and never holds the queue (ADR-0017 Alt 3; Inventory never calls Mail, ADR-0017 l.239). ADR-0017 §6's "ADR-0025 per-footprint sub-queues inside" cannot be built (ADR-0025 is unwritten): M-1a ships one ring with the skip rule — Mail Lead planning default pending Evan (TD-157 item 1).
**ADR Version**: ADR-0017 2026-10-16 (errata through 2026-10-20); ADR-0009 2026-10-16 (errata through 2026-10-21); ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 and ADR-0017 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: the backlog's storage and drain are Inventory's; Mail appends through `IInventoryApi.AppendBacklog`; Inventory never calls Mail (ADR-0017 §6, manifest l.338); boundary crossings into Backlog only through `IInventoryApi` (ADR-0009 §6); the drain runs in its own phase 2 binding (ADR-0003 §1)
- Re-checked against manifest 2026-10-17.1: current.

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R14-2** — Intake full except (19,15) and backlog `[Large, L1, L2]` → L1 at (19,15), backlog `[Large, L2]`, `backlogUnits = 2`, Intake version +1 (the Large is skipped in O(1): area 8 > 1 free cell; no head-of-line blocking); next tick with no Intake change → zero placement probes and zero node visits. Third clause (the M1 sort does not exist at M-1a, so cells are freed by `TakeUnit`): GIVEN 100 Intake cells freed by 100 `TakeUnit(FIFO)` calls in tick T−1's `MailStep` (the `InventoryEventRig` hook; standing in for the M1 sort) and a backlog of 100 letters, THEN the drain at phase 2 of tick T runs because the Intake version changed by something other than the drain, places 32 at T, 32 at T+1, 32 at T+2 (each re-run because the previous pass hit `backlogDrainPerTick`, not because of its own seal) and 4 at T+3, Intake version +1 per pass, and T+4 makes 0 placement probes and 0 node visits.
- [x] **H-R14-4 (backlog half)** — with a non-empty backlog and free Intake cells, a spawned letter is appended to the backlog tail and makes no Intake placement probe.
- [x] **H-PERF-6c** — one drain pass with a 2 800-entry backlog ≤ 8 500 placement probes (`CounterId.InventoryPlacementProbes` 43, per cell, TD-134; plus merge candidates 42, zero under NoMerge) against an Intake with scattered 1×1 holes; a pass with an empty backlog or a full Intake = 0 probes. `AutoPlace` gains a per-oriented-shape start anchor reset per pass (valid because occupancy only grows within a pass; without it 32 placements into scattered holes can cost 10 240 cells). Node visits of the backlog walk count on a new `CounterId` 50 (`InventoryBacklogVisits`, one per ring node read, the H-PERF-6d unit), gated separately at ≤ entries walked + 1 per pass (Mail Lead planning default pending Evan, TD-157 item 4).
- [x] **Backlog API (ADR-0017 §6; shapes are Mail Lead planning defaults pending Evan, TD-157 item 2)** — `IInventoryApi` gains `bool AppendBacklog(in BacklogEntry e)` with `readonly ref struct BacklogEntry(DefIndex MailKind, uint AddressId, byte Flags, ReadOnlySpan<MailId> Ids)` (1–255 strictly ascending ids, one whole entry), and `int BacklogUnits { get; }` / `int BacklogEntries { get; }` (O(1) memos, read-only). Mail (mail-destinations 014) calls `AppendBacklog` in phase 8 for the ids `Spawn` left unplaced (`ids[r.Placed..]`); Inventory never calls Mail. A malformed entry (none or unknown kind, 0 or > 255 ids, ids not strictly ascending), a call outside a tick phase (counted by `TickGuard`), or an entry-pool / arena refusal → `false`, nothing mutated; a pool or arena refusal writes the existing always-on record (`InventoryEntryPoolExhausted` / `InventoryArenaExhausted`, ADR-0009 §5 "false plus an always-on record") and the units stay Mail's — the same "stays where it is" pattern as Evan's 2026-10-22 5:11 AM PT ruling (which covers only a leashed letter on the cursor).
- [x] **Nothing overtakes the backlog (GDD R14, H-R14-4)** — while the backlog is non-empty, `Spawn` places nothing and makes no Intake placement probe (`IntakeAccepting` = `BacklogEntries == 0`; the whole group comes back in `SpawnResult.Unplaced` for Mail to append). Expiry and water returns of mail (story 013) append the whole entry to the backlog tail when the Intake is full or the backlog is non-empty: `ReturnToIntake` gains the backlog branch, returns true and the detached entry is no longer the caller's, so `TryReturnMail` always removes the WorldItem and pops its FIFO node. Payday / Loot returns (GDD R12, H-R12-4) are Mail's M1 work (mail-destinations), out of scope; bag expiry is M-1b.
- [x] **Ring capacity (Mail Lead planning default pending Evan, TD-157 item 3)** — the ring is preallocated at world build with capacity = `inventory.entryPoolCapacity` (no new balance key; ADR-0009 §5 "preallocated, never grows" governs GDD R14's "unbounded by design"). Every backlog entry is a live detached entry-pool slot, so a return can never find the ring full: that case is `SimAssertCode` 82 (`InventoryBacklogRingFull`, unreachable; Release drops nothing and keeps the WorldItem). The 2 800-entry H-PERF fixture fits.
- [x] **Cap eviction to the backlog (TD-138; owner ruling Q3, Evan 2026-10-21 9:17 PM PT: the interim rule holds until this story)** — with the Intake full, a qualifying mail cap candidate (age ≥ 300 ticks, i.e. created at c it qualifies from c + 300 — owner ruling 2026-10-21 9:16 PM PT, `>=`; lowest `(expiresTick, worldItemId)`) is evicted and appended to the backlog tail (GDD R12 "or backlog if full"), the WorldItem count drops by one, and no non-mail item is destroyed in its place; young mail (age ≤ 299) is still never a candidate, and with no qualifying mail the non-mail rule of story 013 applies unchanged. This replaces story 013's interim rule (the mail stays at its FIFO head and the lowest non-mail item is destroyed instead).
- [x] **Drained units leave last (owner ruling, Evan, 2026-10-21 12:28 PM PT; GDD R14, H-R14-3, l.399)** — two cases, both in `IntakeDrainTests`:
  - **PLACE case (the drain's path; GDD R14: every Intake insertion, the drain included, is NoMerge)** — GIVEN the Intake holding three spawned letters to three different addresses with `arrivalSeq` 1, 2, 3 (`NextArrivalSeq` 4) and one backlog letter with an older mailId than all three, WHEN the drain places it, THEN it is a new count-1 entry with `arrivalSeq` 4 (`NextArrivalSeq` 5), one `InvInserted` and no `InvMerged`, the three residents unchanged, and four `TakeUnit(FIFO)` calls return the three residents in `arrivalSeq` order and the drained letter **last** (all three leave ahead of it; a lower mailId never jumps the queue).
  - **MERGE case (not reachable through the drain at M-1a; the merging paths are the `Merge` op with the Intake open and `InventoryMutator.MergeInto`)** — GIVEN three residents with `arrivalSeq` 1, 2, 3 where the entry at 2 is a letter to #9 and the others are other addresses, WHEN a detached letter to #9 is merged onto the entry at 2 (`MergeInto`, as story 012's `IntakeSpawnOutfeedTests` do), THEN that stack is re-stamped `arrivalSeq` 4 (`NextArrivalSeq` 5), the **two** other residents (1 and 3) leave ahead of it, the merged stack leaves last with its lowest mailId first (R6), and no entry's `arrivalSeq` decreased. The rule applies unchanged if `intakeSpawnMergePolicy = Merge` (M1, OQ15) ever lets the drain merge.
- [x] **Retire the interim full-Intake rule (TD-138)** — `CounterId.InventoryWorldItemReturnRetries` (47) is retired (no longer incremented; the append-only id and its `SimCountersTests` pin stay, not reused); the per-FIFO `_blocked*` memo and the class-doc bullet in `WorldItems.cs` go; story 013's four `Interim_*` tests in `tests/unit/inventory/WorldItemTests.cs` are rewritten to the backlog behaviour: (l.894) both letters append in FIFO order at their expiry, both WorldItems gone, drained ahead of later arrivals when cells free; (l.936) the old letter is evicted to the backlog, the lowest Plank survives, count 512; (l.959) the head letter goes to the backlog on the 513th creation, count 512, no over-cap episode; (l.984) ground and water heads append at their expiry, counter 47 unchanged, nothing duplicated. TD-138 closes with this story.
- [x] **Versions and events** — one drain pass is one call scope and one mutator bracket: the Intake bumps +1 per pass (never per entry; ADR-0009 §3 batch rule), one `InvInserted` (with mailId runs) per drained entry, then one `InvVersion` seal; a pass that places nothing raises nothing and bumps nothing (the bracket opens only once the first entry is planned). A new record `InvEventKinds.BacklogUnits = 0x008A`, payload `{units u16, entries u16}` (4 B, padding-free, saturating), `Replicated`, is raised once per call scope in which the backlog changed, after the Intake seal (append raises only `BacklogUnits`); codec id 0 (TD-140).
- [x] **Rejects raise nothing** — a refused `AppendBacklog` and an idle drain pass leave no record, no seal and no version change.
- [x] **0 B** — 1 000 appends and 300 drain passes allocate 0 B per call after warm-up.
- [x] **State and determinism** — the ring is Sim state: 021 appends it to `InventoryStateContributor.Write` after the WorldItems block (`u32 count`, then the entry ids in queue order; the entries themselves are in the entry section). The gating memo is not dumped: a load runs the first pass unconditionally, which is state-identical because a pass over an unchanged Intake places nothing. Two worlds built and driven identically produce byte-identical sections including the backlog. Story 017 hooks the `Backlog` ledger crossings at the mutator.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Backlog/BacklogRing.cs` (the ring of detached entry-pool slots, the WorldItems pattern: a return already owns a detached entry and arena block, so append is O(1) with no copy; a spawn-overflow append allocates the entry and its arena block at append time exactly as `Spawn` does), the drain in `IntakeDrainPhase` (`InventoryPhases.cs`, bound at `InventorySystem.cs` `Bind`), `IntakeAccepting` and the `ReturnToIntake` backlog branch in `InventorySystem.cs`, the interim-rule removal in `World/WorldItems.cs`.
- The drain plans each entry with `TryPlanReturnToIntake` and commits with `CommitReturnToIntake` (NoMerge, fresh `arrivalSeq`) inside one bracket per pass; the in-pass per-shape exhaustion comes from the negative-fit cache because the version is constant until the seal; record the no-fit facts after the seal at the sealed version, as `Spawn` does.
- Gating memo: the Intake version after the drain's own seal, the append sequence last seen, and a last-pass-hit-cap flag; exit O(1) when `BacklogEntries == 0` or the Intake's free-cell count is 0.
- Per-footprint sub-queues are ADR-0025 (M1, unwritten); at M-1a a single ring with the skip rule meets the probe bound for the solo greybox (solo inflow never fills the Intake — GDD D2 ≈ 268 cells shift 5).
- Performance: H-PERF-6c (≤ 8 500 placement probes per pass) with the start hints; idle passes 0 probes and 0 visits; 0 B per call.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 012 (Complete, PR #21): Spawn, outfeed, the `IntakeAccepting` seam, `ReturnToIntake` — reused, contract unchanged.
- mail-destinations story 014: the batch-tick caller of `Spawn` and `AppendBacklog` (what enters, in what order, when) and the publication of `backlogUnits` in `MailShiftStats` (ADR-0017 §6, §9).
- Mail's Payday dead-letter sweep and Loot returns (M1).
- Story 017: the ledger calls.
- The M1 Intake sort (D3, ADR-0025).

---

## QA Test Cases

*No qa-lead specs; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/IntakeDrainTests.cs` — must exist and pass
- Logic: the four rewritten backlog tests in `tests/unit/inventory/WorldItemTests.cs` (formerly `Interim_*`)
- Allocation: `tests/unit/inventory/IntakeDrainAllocationTests.cs` — `AllocAssert.Zero` plus the every-build twin (pattern: `WorldItemAllocationTests.cs`)

**Status**: [x] Created — `tests/unit/inventory/IntakeDrainTests.cs`, `IntakeDrainAllocationTests.cs`, `BacklogRingTests.cs`; the four rewritten `WorldItemTests` tests `Backlog_IntakeFullAtExpiry_BothLettersAppendInFifoOrder_BothWorldItemsGone_DrainedAheadOfLaterArrivals`, `Backlog_IntakeFullAtCapEviction_TheOldLetterIsEvictedToTheBacklog_TheLowestPlankSurvives_Count512`, `Backlog_IntakeFull_512OldLetters_A513th_TheHeadLetterGoesToTheBacklog_Count512_NoOverCapEpisode`, `Backlog_IntakeFull_GroundAndWaterMailHeads_AppendAtTheirExpiry_Counter47Unchanged_NothingDuplicated` (plus the close-out's `Backlog_IntakeFullAtCapEviction_A299TickOldLetterIsNoCandidate_TheLowestPlankIsDestroyed_TheBacklogStaysEmpty`); and `PickupCatchTests.Catch_FumbleAtTheCapWithAFullIntake_EvictsTheOldLetterToTheBacklog_NoPlankDestroyed`; all pass on net10.0 × Debug/Release

---

## Dependencies

- Depends on: Story 012, Story 013 (both Complete). Was stacked on story 014 (PR #34): the branch merged PR 34's round-1 head 09804ab in at 417a8ad (no rebase), resolving the file overlap in `InventorySystem.cs`, `IInventoryApi.cs`, `World/WorldItems.cs`, `SimAssertCode.cs` and `tests/unit/inventory/InventoryEventRig.cs`. PR #34 has merged (main c2d4a64, contains 09804ab); the branch merged main c2d4a64 in at 5e9fe8d (no rebase), so PR #35 is no longer stacked.
- Unlocks: Story 017 (`Backlog` ledger crossings at the mutator hooks), Story 018 (fuzz covers the backlog); mail-destinations epic — `production/epics/mail-destinations/story-014-batch-firing-intake-backlog.md` (Mail appends through `IInventoryApi.AppendBacklog`). *(2026-10-16 hygiene: the former "Depends on: mail-destinations backlog story (not yet created)" is reversed — ADR-0017 §6 puts the backlog storage and drain in Inventory, and that story already depends on this one.)*

---

## Mail Lead planning defaults (pending Evan)

These are **Mail Lead planning defaults, 2026-10-22, open for Evan's confirmation — not Evan's rulings**. The story is built on them; TD-157 tracks them. None contradicts an Accepted ADR; each is consistent with TD-150, TD-152, TD-153, TD-154 and TD-155 and with Evan's rulings (Q3, `>=` 300 ticks, 12:28 PM fresh `arrivalSeq`, 1:21 PM per-group `Spawn`, 5:11 AM a leashed letter stays on the cursor when the arena is full).

1. M-1a ships one backlog ring with the drain's skip rule; ADR-0017 §6's ADR-0025 sub-queues wait for that ADR (proposed ADR-0017 text erratum for Evan below).
2. Shapes: `AppendBacklog(in BacklogEntry)`, `BacklogEntry(DefIndex MailKind, uint AddressId, byte Flags, ReadOnlySpan<MailId> Ids)`, `BacklogUnits`, `BacklogEntries` — ADR-0017 names them but never defines them; fold into the TD-144 as-implemented note.
3. Ring capacity = `inventory.entryPoolCapacity`; a spawn-overflow append refused by the pool or arena → `false` + the existing always-on record, the units stay Mail's (mail-destinations 014 re-offers them at the next batch tick); a full ring on a return is unreachable (`SimAssertCode` 82).
4. Probe accounting: the 8 500 gate is placement probes (counter 43, per cell); backlog node visits count on new counter 50 with their own gate (≤ entries walked + 1 per pass), as H-PERF-6d's visit unit.
5. The drain is NoMerge; the fresh-`arrivalSeq` MERGE case is pinned through `MergeInto` only (not a drain path at M-1a).
6. One drain pass = one call scope, Intake +1 per pass; `BacklogUnits` (0x008A, 4 B, Replicated) after the seal.
7. The backlog ring is in this story's dump section; the gating memo is not dumped (the first pass after a load runs unconditionally).
8. GDD errata (not edited here): grid-inventory R14 l.141 and the state table l.164 ("owned by Mail & Destinations", "unbounded by design") to match ADR-0017 §6 and ADR-0009 §5.

**Proposed ADR-0017 text erratum for Evan (not applied):** §6 l.136 "with ADR-0025's per-footprint sub-queues inside" → "with ADR-0025's per-footprint sub-queues inside once that ADR is written; M-1a ships one ring with the drain's skip rule", and the Key Interfaces gain the default 2 shapes.

---

## Completion Notes
**Completed**: 2026-10-22 (PR #35, open, not merged; was stacked on PR #34 (round-1 head 09804ab merged in at 417a8ad) until PR #34 merged as main c2d4a64, merged in at 5e9fe8d — no longer stacked)
**Criteria**: 13/13 passing (all in `tests/unit/inventory/`; `IntakeDrainTests` unless named otherwise).
- H-R14-2: `Drain_IntakeFullExceptTheLastCell_BacklogLargeL1L2_PlacesL1_SkipsTheLarge_OneSeal_ThenAnIdleTickProbesNothing` (320 probes, 2 visits, then 0/0), `Drain_100CellsFreedInTheMailStep_100BacklogLetters_Places32_32_32_4_OneSealPerPass_ThenIdle` (32/32/32/4/0, Intake +1 per pass, T+4 0 probes and 0 visits)
- H-R14-4 (backlog half): `Spawn_WithANonEmptyBacklogAndFreeCells_PlacesNothing_MakesNoProbe_AndTheLetterGoesToTheBacklogTail`, `AppendBacklog_AWellFormedEntry_QueuesOneDetachedEntryAtTheTail_AndRaisesOnlyBacklogUnits`
- H-PERF-6c: `Drain_A2800EntryPass_IntoScattered1x1Holes_StaysWithin8500PlacementProbes_AndEntriesWalkedPlus1Visits` (placement probes 43 plus merge candidates 42 ≤ 8,500, 42 = 0; visits ≤ walked + 1), `Drain_AnEmptyBacklogOrAFullIntake_MakesNoProbeAndNoVisit_AndRaisesNothing`
- Backlog API: `AppendBacklog_AWellFormedEntry_…`, `AppendBacklog_AMalformedEntry_IsRefused_WithNothingMutatedAndNothingRaised` (7 shapes), `AppendBacklog_OutsideATickPhase_IsRefusedAndCounted_WithNothingMutated`, `AppendBacklog_AFullEntryPool_IsRefused_WithThePoolRecord_AndNothingMutated`, `AppendBacklog_AnArenaRefusal_IsRefused_WithTheArenaRecord_AndNothingMutated`
- Nothing overtakes the backlog: `Spawn_WithANonEmptyBacklogAndFreeCells_…`, `ReturnToIntake_WithANonEmptyBacklogAndFreeCells_AppendsBehindIt_WithNoProbe`, `ReturnToIntake_ANonMailEntryTheIntakeRefuses_IsNotQueued`, `IntakeSpawnOutfeedTests.ReturnToIntake_NoRoom_AppendsTheMailEntryToTheBacklog_TheIntakeUnchanged`, `WorldItemTests.Backlog_IntakeFullAtExpiry_…`, `WorldItemTests.Backlog_IntakeFull_GroundAndWaterMailHeads_…`
- Ring capacity: `BacklogRingTests` — `Ring_IsPreallocatedAtWorldBuild_WithTheConfiguredEntryPoolCapacity_AndHoldsThe2800EntryFixture`, `Ring_TakesEveryEntryThePoolCanHold_AndThePoolRefusesTheNextAppend_NotTheRing`, and assert 82 through the internal `LimitForTests` seam: `Expiry_OfAMailWorldItem_IntakeFullAndTheRingForcedFull_…`, `CapEviction_OfAQualifyingLetter_IntakeFullAndTheRingForcedFull_…`, `ReturnToIntakeAndTryAppendDetached_WithTheRingForcedFull_…` (Debug throws 82; Release keeps the WorldItem/entry and queues nothing)
- Cap eviction to the backlog: `WorldItemTests.Backlog_IntakeFullAtCapEviction_TheOldLetterIsEvictedToTheBacklog_TheLowestPlankSurvives_Count512`, `WorldItemTests.Backlog_IntakeFull_512OldLetters_A513th_TheHeadLetterGoesToTheBacklog_Count512_NoOverCapEpisode`, `WorldItemTests.Backlog_IntakeFullAtCapEviction_A299TickOldLetterIsNoCandidate_TheLowestPlankIsDestroyed_TheBacklogStaysEmpty` (close-out: young mail and the unchanged non-mail rule with the Intake full), `PickupCatchTests.Catch_FumbleAtTheCapWithAFullIntake_EvictsTheOldLetterToTheBacklog_NoPlankDestroyed`; the `>=` 300 boundary stays pinned by story 013's `HR123_512YoungMail_…`
- Drained units leave last: `Drain_PlaceCase_AnOlderMailIdFromTheBacklog_GetsArrivalSeq4_OneInsertedNoMerged_AndLeavesAfterAllThreeResidents` (a new count-1 entry, the three residents' ids, counts and seqs unchanged), `MergeCase_ALetterMergedOntoTheSecondResident_IsRestampedSeq4_TheOtherTwoLeaveAhead_ThenTheStackLowestIdFirst` (the other two residents keep seqs 1 and 3)
- Retire the interim rule: the four rewritten `WorldItemTests.Backlog_*` tests (counter 47 unchanged); `SimCountersTests` keeps the id-47 pin; the `_blocked*` memo and its class-doc bullet are gone from `WorldItems.cs`
- Versions and events: `Drain_100CellsFreedInTheMailStep_…` (32 `InvInserted`, one seal, one `BacklogUnits` per pass; +1 per pass), `Drain_IntakeFullExceptTheLastCell_…` (`Inserted, Version, BacklogUnits`, cause `System`), `Drain_APassThatRunsButPlacesNothing_RaisesNothing_AndBumpsNothing` (close-out), `AppendBacklog_AWellFormedEntry_…` (append raises only `BacklogUnits`, 4 B); `InventoryEventsTests` descriptor theory (Replicated, padding-free) and `PayloadSizes_ArePinned_…` (4 B); `WorldItemTests.Pins_…` pins 0x008A
- Rejects raise nothing: the four `AppendBacklog_*` refusal tests, `Drain_AnEmptyBacklogOrAFullIntake_…`, `Drain_IntakeFullExceptTheLastCell_…` (the idle tick)
- 0 B: `IntakeDrainAllocationTests.AppendBacklogAndDrainPasses_Over300Ticks_AllocateNothing_InOptimisedBuilds` (1,200 appends, 300 passes; Release) with its `…_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild` twin
- State and determinism: `Dump_TheBacklogRingFollowsTheWorldItemsBlock_CountThenIdsInQueueOrder_IdenticalAcrossIdenticalWorlds`, `WorldItemTests.Dump_InventorySectionCarries…` (empty ring `u32 0` at the section end)
**New values** (append-only): `CounterId` 50 `InventoryBacklogVisits`; `SimAssertCode` 82 `InventoryBacklogRingFull`; `EventKind` 0x008A `BacklogUnits` (`{units u16, entries u16}`, 4 B, `Replicated`, codec id 0, TD-140). Counter 47 `InventoryWorldItemReturnRetries` is retired, not reused.
**Deviations** (all ADVISORY, as-implemented; Mail Lead planning defaults pending Evan, TD-157 items 1–8 and TD-158 items 1–8): one ring with the skip rule instead of ADR-0025 sub-queues; the `AppendBacklog`/`BacklogEntry`/`BacklogUnits`/`BacklogEntries` shapes; ring capacity = `inventory.entryPoolCapacity`; counter 50 with its own visit gate; the drain seals with actor and cause `System`; a backlog return leaves no Intake seal; `AppendBacklog` checks the arena before the pool; `BacklogRing` is an intrusive doubly linked queue; at most 16 start anchors and 16 re-recorded no-fit facts per pass. Close-out: the drain checks `TickGuard` after its O(1) idle exits (stub rigs inject their own tick reader and never hold a backlog), and a `backlogDrainPerTick` of 0 no longer marks every pass as having hit the cap.
**Deferrals**: TD-157 (planning defaults 1–8, incl. the GDD R14 errata and the proposed ADR-0017 §6 erratum) and TD-158 (as-built choices 1–7, and 8: the two unreachable full-ring follow-ups — Release cap eviction falling through to the non-mail rule, `AppendBacklog`'s unchecked ring append) — both pending Evan. TD-138 closed by this story.
**Watched fails**: (1) H-PERF-6c without the per-shape start anchors: 9,008 probes vs the 8,500 gate, 1 failed of 84 in `IntakeDrainTests` + `AutoPlaceTests` (unmutated: 4,358); (2) the catch eviction test with no backlog eviction: 5 failed of 102 in `PickupCatchTests` + `WorldItemTests`, among them the catch test at `PickupCatchTests.cs:586`.
**Test Evidence**: Logic — see above; full suite net10.0, Debug and Release: 6,189 tests per leg, 0 failed (Debug 6,132 passed / 57 skipped, Release 6,186 passed / 3 skipped).
**Run result**: N/A — Sim-only, no Godot surface (no smoke run)
**Code Review**: completion review (Sonnet, `/story-done`): items applied at close-out — stale `WorldItemTests` class doc; 0x008A, its 4 B payload and assert 82 pinned and the `PickupCatchTests` inventory-kind range widened to 0x008A; the drain's tick guard; the 0-cap re-run; tightened H-R14-3 asserts; the full-Intake young-mail test; the full-ring follow-ups recorded as TD-158 item 8.
