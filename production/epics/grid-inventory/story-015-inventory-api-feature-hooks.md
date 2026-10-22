# Story 015: IInventoryApi hooks for feature epics — ConsumeUnit, DetachForThrow, TryInsert, CountItem

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (Evan's approval of 2:24 PM PT recorded, docs only: the Mail Lead planning defaults below are now Evan's rulings). Before that: 2026-10-21 (readiness fixes: ledger and weight observables moved to 017/016; no new event; TryInsert mail form)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-005` (oldest-first consume; payment per mailId), `TR-inventory-001` (in-tick sync calls)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: In-tick callers use synchronous `IInventoryApi` (same versions/events as ops, no reply): `ConsumeUnit(entryRef, reason, actor)` consumes the oldest unit; `DetachForThrow(slot, entryId, units)` detaches the **whole hotbar entry** to Flight (cursor / non-hotbar → `bad_request`; count ≠ units → `stale_entry`; the selected-slot check is Throwing's validation chain row 2, ADR-0015 §1, before it calls `DetachForThrow` — no selected-slot state is read here); `TryInsert` runs the whitelist and AutoPlace (merge pass first); `CountItem` reads a per-container `itemId → units` index, never a scan (ADR-0009 Key Interfaces, §5; ADR-0003 §5).
**ADR Version**: ADR-0009 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required (Feature/Core, ADR-0003 §5): cross-system calls only through a declared `I…Api` in the caller's own phase
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: id wrapper structs (`EntryId`, `MailId`, `EntityId`, `PlayerSlot`, `DefIndex`) on every API crossing a system boundary; ids never reused (ADR-0004)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R6-1 c (API form)** — `ConsumeUnit` on A = `[4, 9, 15, 21, 30]` (Misdeliver path) → 4 consumed, A = `[9, 15, 21, 30]`, version +1; consuming the last unit removes the entry in the same call.
- [x] **H-X-2 (inventory half)** — hotbar index 3 holds 5 letters (entry 88): `DetachForThrow(slot, 88, 5)` → index 3 empty, hotbar version +1 (one `InvRemoved{hotbar, 88, 5}` then one `InvVersion` seal), entry 88 stays live and detached (`ContainerSlot == NoContainer`, in no container and in no WorldItem) with its 5 mailIds intact, and the result carries entry 88 and 5 units; naming the cursor entry or a non-hotbar entry → `bad_request`, nothing changes, no record; `units` ≠ the entry's count → `stale_entry`, nothing changes. *(The ledger clause "5 ids in FLIGHT" is checked in story 017; the weight clause "P drops by 1" in story 016.)*
- [x] **H-R5-1 / H-X-3 c (TryInsert path, mail form)** — `TryInsert(po_intake, letter, actor)` (outfeed_only) → returns `false`, no record, zero entries added, version unchanged; a letter into a container whose whitelist refuses it (test fixture container) → `false`, nothing changes; a letter into a container with a matching non-full stack merges into it first (AutoPlace merge pass), one `InvMerged` record, version +1. *(The Plank / item-form leg of H-R5-1 waits for an item-form `TryInsert` — see Out of Scope.)*
- [x] **CountItem index** — after each of 1 000 random item mutations driven through the `InventoryMutator` primitives (`Place`, `MergeInto`, `TakeOldest`, `Transfer`, `Remove`) `CountItem([panel], itemId)` equals a brute-force scan (H-R17-1 last clause).
- [x] **Perf / allocation** — `CountItem` is a map read whose probes do not grow with entry count; all four calls allocate 0 B on the tick path (allocation test as `WorldItemAllocationTests`/`IntakeAllocationTests` do).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: extends story 012's `IInventoryApi` (`src/PerformativeMail.Sim/Inventory/IInventoryApi.cs`, implemented by `InventorySystem` and published for declared dependents in `SystemGraph`) with `ConsumeUnit`, `DetachForThrow`, `TryInsert`, `CountItem`; `ConsumeUnit` reuses story 012's `InventoryMutator.TakeOldest`.
- `ConsumeReason` (Delivered / Misdelivered / ReturnedToSender) is a parameter of `ConsumeUnit`; the call raises the existing `InvRemoved{c, entry, 1}` record through `TakeOldest`, sealed with cause `InvCause.Consumed`. No new event kind (`UnitsRemoved{entryIds[]}` is the GDD's network coalescing name, grid-inventory.md l.599 bandwidth paragraph — not a Sim record). Recording the reason on the `UnitLedger` (PLACED → CONSUMED) is story 017's hook, like `TakeOldest`'s and `DestroyDetached`'s. Mail's `MailConsumed`/`DeliveryOutcome` are Mail's (ADR-0017) — not raised here.
- `DetachForThrow` removes the entry with `InventoryMutator.Remove` and leaves it live and detached (`ContainerSlot == NoContainer`, the representation story 013's WorldItems use); the PLACED → FLIGHT ledger crossing is story 017's hook. Weight (`WeightPoints`) follows from `Remove` once story 016 lands.
- New types (Mail Lead planning defaults, approved by Evan 2026-10-22 2:24 PM PT; now in ADR-0009 Key Interfaces, erratum 2026-10-22): `readonly record struct EntryRef(ContainerId Container, EntryId Entry)`; `readonly record struct DetachResult(InvReason Reason, EntryId Entry, byte Units)` (`Reason` none on success); `enum ConsumeReason : byte { Delivered, Misdelivered, ReturnedToSender }`. `ConsumeUnit` returns `false`, mutating nothing, on a stale ref, an empty entry or a call outside a tick phase.
- `TryInsert` takes the ADR-0009 sketch's mail form `bool TryInsert(ContainerId c, in UnitRef u, ActorId actor)` (story 012's `UnitRef`): whitelist and outfeed_only check, then AutoPlace merge pass first; it creates the unit's entry only when it places.
- `CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId)`: create the per-container open-addressed `itemId → units` map (ADR-0009 §5 table — it does not exist yet), preallocated, maintained inside every `InventoryMutator` item primitive (one place, so op handlers 007/008/010 get it free).
- Counters: a probe counter for the map, if one is added, takes the next free `CounterId` (49 at PR 26's head) — check `src/PerformativeMail.Sim/Core/CounterId.cs` at build time.
- `DetachForThrow` is called by Throwing's `ThrowRelease` handler after Throwing's own checks (ADR-0009 §1) — inventory never owns a throw op.
- `Grant`/`ConsumeItems` (R17) are M1; `SpillToBag` is M-1b — leave them off the interface until their stories (or as `NotImplemented`-free absent members).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 016: weight points (H-X-2's "P drops by 1").
- Story 017: `UnitLedger` crossings (H-X-2's "ids in FLIGHT", ConsumeReason → CONSUMED).
- Item-form `TryInsert` (H-R5-1's Plank leg, H-X-3 b): Belt Logistics / owner question on an ADR-0009 as-implemented note.
- Story 022: `DeliverMatching` (Blocked).
- M1: `Grant`, `ConsumeItems`.
- M-1b: `SpillToBag`, inserter selectors.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY ran 2026-10-21: GAPS, addressed by the 2026-10-21 fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/InventoryApiTests.cs` — must exist and pass

**Status**: [x] Created and passing — `tests/unit/inventory/InventoryApiTests.cs`, `tests/unit/inventory/InventoryApiAllocationTests.cs`

**Completion notes (2026-10-21, /story-done, headless; no build or test run by this review)**:
- Evidence from the Mail Dev verify run on the working tree at 34bcd3b (`gi009-verify.sh`, all four legs and guards): net10.0 and net8.0, Debug and Release, 5,313 tests each, 0 failed (Debug 49 skipped, Release 3 skipped; the Release-only 0 B allocation test passes in Release); 0 build warnings; `tools/ci` guards and `check-game-build.sh` pass.
- After the Mail Review R1 fixes (c7accdc) all four legs ran again, locally in Mail Review's fresh worktree and in CI run 39740074900: net10.0 and net8.0, Debug and Release, 5,323 tests each, 0 failed (Debug 49 skipped, Release 3 skipped), 0 build warnings. After the Mail Review R2 fixes all four legs ran again locally (`gi009-verify.sh`): net10.0 and net8.0, Debug and Release, 5,329 tests each, 0 failed (Debug 49 skipped, Release 3 skipped), 0 build warnings, all `tools/ci` guards and `--self-check`s pass; CI runs the same four legs on push.
- AC coverage (read against `src/PerformativeMail.Sim/Inventory/InventorySystem.cs` l.463–672, `ConsumeUnit`'s doc through the end of `CountItem`, after the Mail Review R2 fixes):
  - H-R6-1 c: `ConsumeUnit_OnA_4_9_15_21_30_…`, `ConsumeUnit_OfTheLastUnit_RemovesAndFreesTheEntryInTheSameCall`, plus the stale/empty/out-of-phase refusal tests.
  - H-X-2: `DetachForThrow_Hotbar3Entry88Of5Letters_…`, `…_FromSlot1sHotbar_SealsWithPlayerBsActor`, `…_OfTheCursorEntryOrANonHotbarEntryOrAMalformedId_IsBadRequest_…`, `…_OfAnOnceAllocatedEntryIdThatIsNoLongerLiveOrAnUnmappedTempIdOfTheThrower_IsStaleEntry_…`, `…_TheSameEntryTwice_TheDuplicateReleaseIsBadRequest_…`, `…_WrongOnBothChecks_AnswersTheFirstFailingCheck_…`, `…_AfterTheSlotRejoinsWithANewPlayer_…`, `…_WithUnitsOtherThanTheEntryCount_IsStaleEntry_…`, `…_OutsideATickPhase_…`.
  - H-R5-1 / H-X-3 c: `TryInsert_IntoPoIntake_OutfeedOnly_…`, `TryInsert_ALetterIntoAContainerWhoseWhitelistRefusesIt_…`, `TryInsert_ALetterWithAMatchingNonFullStack_MergesFirst_…`, plus placement, no-room, pool/arena-exhaustion no-leak tests.
  - CountItem index: `OneThousandRandomItemMutations_CountItemEqualsABruteForceScan_AfterEveryMutation` (Place, MergeInto incl. detached source, TakeOldest, Transfer incl. cross-container, Remove), `RebuildIndex_…`, `ItemCountIndex_InsertsAndDeletesManyKeys_WithBackwardShift_…`.
  - Perf / allocation: `CountItem_ProbesPerLookup_StayUnderAConstantBound_…` (realistic load and max load), `ItemCountIndex_OneItemInSuccessiveContainers_AtMaxLoad_…` (discriminates the top-bits Home fix); `InventoryApiAllocationTests.ConsumeDetachInsertAndCount_Over1000Cycles_AllocateNothing_InOptimisedBuilds` (and its every-build twin).
- Deviations and as-implemented choices (none changes a GDD rule; each is a choice the GDD and ADRs leave open, or a guard they imply):
  - `DetachForThrow` seals with `InvCause.None` (no existing cause fits).
  - `DetachForThrow`'s reasons (Mail Review R1 B1, R2 B2), checked in this order: out of phase, a slot that is none, out of range or without a hotbar → `bad_request`; then the id through `InvRequestCheck`'s own `CheckEntryNamespace` and `ResolveEntry` (now `internal static`, shared, with the thrower's slot as the temp namespace): an entry id of 0, a real id above `EntryPool.LastId`, a temp id with reserved bits 23–30 set or a temp id of another slot's namespace → `bad_request`; a temp id of the thrower's own namespace (never mapped: no temp id is mapped yet; ADR-0009 l.78, l.117, l.165, GDD l.603 rule (2), H-OPT-2) or a real id at or below `LastId` that is not live (consumed, merged away, retired or freed) → `stale_entry` at once, since entry ids have no tombstone (ADR-0009 l.116–117, ADR-0015 §1 row 2, GDD H-R7-4); a live entry not placed in this slot's hotbar (the cursor's, a panel's, another slot's, or an already detached entry, i.e. a duplicate release) → `bad_request`; then `units` ≠ count → `stale_entry`.
  - `TryInsert` ignores `UnitRef.Entry` and does not detect duplicate mail ids (story 017's ledger); it returns `MergeUnitInto`'s result on the merge path, so the unit crosses into PLACED only on true.
  - `ItemCountIndex` refuses a container capacity above 65,535 at construction (`<= 65535`, not `< 65535`): the highest container slot is 65,534, and slot + 1 = 0xFFFF still fits the key's 16 bits with key 0 kept as the empty marker; `InventorySettings` already allows 65,535 containers; 0xFFFF is never a container slot, being `EntryRecord.NoContainer`.
  - `CountItem` never counts mail: mail entries store `ItemId` 0, which is also the axe def's ordinal, so only non-mail entries feed the index.
  - `CountItem` sums the listed containers as given: a container listed twice is summed twice.
  - `ConsumeUnit` refuses an undefined `ConsumeReason` (above `ReturnedToSender`) with nothing changed.
  - `MergeUnitInto` / `RaiseMerged` refactor: `TryInsert`'s merge commits one unit onto the target directly, with no throwaway source `EntryId` spent; `RaiseMerged` is shared with `MergeInto` and keeps the outfeed restamp.
- Logged as TD-144 (ADR-0009 as-implemented note), TD-145, TD-146 (`_panels` restore path), TD-147 (`StackIndex.Slot` hash).
- Ledger crossings (PLACED→CONSUMED, PLACED→FLIGHT, into PLACED) and weight are marked as story 017/016 hooks in the code, per Out of Scope.
- Lead-programmer gate skipped: the Sonnet /code-review of slices 1–3 already ran and its findings were fixed or logged.

---

## Dependencies

- Depends on: Story 009, Story 013
- Depends on: `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`
- Unlocks: Story 017, Story 018, Story 022; manual-delivery, throwing-catching, mail-destinations epics
