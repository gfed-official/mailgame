# Story 003: mailIds size-class arena — oldest-first take, sorted-union merge, exhaustion

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-19

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-003` (ascending `mailIds[]`), `TR-inventory-005` (take-k removes the lowest mailIds)

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay
**ADR Decision Summary**: `mailIds` live in size classes of 1, 2, 4 … 256 `u32`s, one preallocated slab + free-index stack per class; a handle is `(class, block, start, count)`; take-k-oldest advances `start` (O(1), R6); a merge is a two-pointer sorted union into a block of the target class; a block whose live count falls to ≤ ¼ of its class is re-homed on its next mutation; an empty class borrows from the next larger; if all larger are empty validation fails `capacity_exhausted` (nothing mutated) (ADR-0009 §5).
**ADR Version**: ADR-0009 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: storage preallocated from `SimSettings` at world build; never `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any inventory tick path (ADR-0009 §5, Implementation Guidelines)
- Forbidden: a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, a per-entry `List<uint>` for `mailIds` (ADR-0009 Alts 3–5)
- Required: capacities sized so the H-R19-1 fuzz fixture peaks below 50 % of each class (asserted by a test) (ADR-0009 §5)
- Required (Foundation, ADR-0006): integer-only; order by raw id `Value`; never `GetHashCode` for ordering; 0 B allocation per tick after warm-up
- Guardrail: `EntryRecord` ≤ 40 B; total inventory heap inside H-PERF-5's 1 MB with 512 WorldItems (advisory until perf baseline)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] **H-R6-1 a/b (arena form)** — GIVEN A = `[4, 9, 15, 21, 30]`: take-2 → `[4, 9]` returned, A = `[15, 21, 30]`; union of A with B = `[2, 50]` → `[2, 4, 9, 15, 21, 30, 50]`; every result ascending with no duplicate.
- [x] Take-k-oldest is O(1) (advances `start`, no copy); a union allocates exactly one block of the result's size class and frees both inputs; re-homing triggers only when live ≤ ¼ of the class, and a block oscillating ±1 around the threshold re-homes at most once per crossing (no thrash).
- [x] With every larger class exhausted, a request that needs a new block is refused at arena level (`CanReserve` false; for an API call, `false` plus one `CheckCode.InventoryArenaExhausted` record) and nothing is mutated; borrowing from a larger class is used before that. The validator half (mapping to `capacity_exhausted`, reason 85) is story 005.
- [x] A seeded property test (10 000 random take/union/append sequences, units up to 255 per handle) keeps every handle's contents equal to a reference sorted list, and frees every block when all handles are released (no leak).
- [x] **(a) Multi-union reservation** — `CanReserve` validates the block count of an op that needs several unions (ADR-0009 §2: validation decides, commit never fails): it counts the blocks the op needs across classes (with borrowing); a reservation that fits passes and the subsequent commit succeeds; one block short fails and nothing is mutated.
- [x] **(b) Best-effort re-homing** — re-homing during commit is best-effort and never fails: when no smaller block is free the run keeps its existing block, and the take still succeeds.

*Scope note (2026-10-19): the arena reports exhaustion through `CanReserve` (false) and, for an API caller, `false` plus one `CheckCode.InventoryArenaExhausted` (12) record. Mapping that to reason `capacity_exhausted` (85) in the validator is story 005 (which wires the arena into the validator); this story tests the arena-level result only.*

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Storage/MailIdArena.cs`.
- Class capacities are `InventorySettings` fields (story 001), PROPOSED; story 018's fuzz asserts the < 50 % peak and retunes them.
- The arena exposes only span reads (`ReadOnlySpan<uint>`) to callers; `MailId` structs wrap at the API boundary (ids cross system boundaries as `MailId`, ADR-0004).
- `capacity_exhausted` is checked in `Validate` (dry-run reservation), never discovered in `Commit` (Commit never fails).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: wiring the arena into mutator primitives and `Inserted`/`Merged` mailId runs.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/MailIdArenaTests.cs` — must exist and pass

**Status**: [x] `tests/unit/inventory/MailIdArenaTests.cs` — `MailIdArenaTests` (24 methods: 22 Fact + 2 Theory × 3 rows — oscillation × 3 start counts, property test × 3 seeds × 10 000 executed ops — = 28 cases) + `MailIdArenaAllocationTests` (1 NonParallel 0 B test, Release-only) = 29 cases; 4,638 / 0 failed on net10.0 + net8.0 × Debug + Release

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 005, Story 008

---

## Completion Notes
**Completed**: 2026-10-19
**Criteria**: 6/6 passing (4 original + new (a), (b))
- H-R6-1 a/b — `HR61a_TakeTwoOfA_ReturnsFourAndNine_AndALeaves15To30`, `HR61b_UnionOfAWith2And50_IsTheSortedUnion_AndBothInputsAreFreed`, `HR61_TakeThenUnionChained_EveryResultStrictlyAscending`
- O(1) take / one-block union / ≤ ¼ re-home / no thrash — `AC2_TakeK_AdvancesStartInPlace_NoCopyNoBlockChangeNoRecord`, `AC2_Union_AllocatesOneBlockOfResultClass_AndFreesBothInputs`, `AC2_Append_ReplacesTheRunWithOneBlockOfResultClass_AndFreesTheOldBlock`, `AC2_ReHome_TriggersOnlyWhenPreTakeCountIsAtMostAQuarterOfTheClass`, `AC2_ReHome_NeverWhilePreTakeCountIsAboveAQuarter_ThenOnceAtTheQuarter`, `AC2_OscillationAroundTheQuarter_ReHomesAtMostOnce_AndSettles` (64/65/66), `AC2_DrainBySingleTakes_ReHomesOncePerQuarterCrossing_NeverThrashes`
- borrow-up, then exhaustion with nothing mutated (arena level; reason 85 is story 005) — `AC3_FittingClassEmpty_CreateUnionAppendBorrowFromTheNextLargerClass`, `AC3_EveryLargerClassExhausted_ApiReturnsFalseWithOneRecord_AndNothingIsMutated`
- seeded property test — `AC4_SeededRandomTakeUnionAppendFree_MatchesReferenceSortedLists_AndLeaksNothing` (3 seeds × 10 000 executed take/union/append ops, peak run 255, 350–675 ms per seed on Debug)
- (a) — `ACa_ThreeUnionsAcrossClassesWithOneBorrow_ReservationPasses_AndEveryCommitSucceeds`, `ACa_OneBlockShort_ReservationFails_AndNothingIsMutated`, `ACa_SingleBlockChecksEachPass_ButTheCombinedReservationFails_AsTheCommitWould`, `ACa_RunOverMaxUnitsFails_NonPositiveEntriesAreIgnored`, `ACa_ReservationIgnoresBlocksTheCommitFrees_ConservativeByDesign`, plus randomised batches inside AC4
- (b) — `ACb_NoSmallerBlockFree_TakeKeepsTheBlock_AndSucceeds`, `ACb_Control_SmallerBlockFreeInRange_TakeReHomes`
**Deviations**: ADVISORY — the reason-85 validator mapping (deferred by the scope note) is owned by story 005's "Arena exhaustion → reason 85" AC (TD-102 (1)); AC-4 "sequences" read as executed ops per seed (TD-102 (3)). NOTE — the pre-existing `check-no-scenetree-paused.sh` false positive on main (literal `SceneTree.Paused` in the `TickDriverState.cs` doc comment) is fixed separately by PR #1 (`fix/ci-scenetree-paused-guard`, guard skips C# comments), not in this story; `TickDriverState.cs` is unchanged on this branch, and local guard runs for this story were made with that fix applied/expected. Code: new `MailIdArena.CanReserve(ReadOnlySpan<int>)`; `Take`/`TryMerge` split into `TryReHome`/`MergeInto` (no behaviour change); class doc hysteresis claim corrected.
**Test Evidence**: Logic: `tests/unit/inventory/MailIdArenaTests.cs`
**Code Review**: Complete — /code-review (C# specialist + QA tester; findings fixed), LP-CODE-REVIEW APPROVE, QL-TEST-COVERAGE GAPS (G1 → TD-102; G2/G3 closed with tests; G4 → TD-102; G5 fixed)
**Tech debt**: TD-102
