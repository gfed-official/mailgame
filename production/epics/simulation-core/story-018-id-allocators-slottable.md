# Story 018: Entity and mail id allocators, and the SlotTable

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R19 ids, R20 exhaustion, R21 players and slots; F4)
**Requirement**: `TR-sim-025` (EntityId `class << 24 | counter`; classes 01–08; 09–EF reserved; F0–FF tests; never reused), `TR-sim-026` (`mailId` separate u32; `PlayerSlot` u8 0–7; slot freed in T reusable from T+1)
*(Requirement text: `docs/architecture/tr-registry.yaml` TR-sim-025 "EntityId uint32 class<<24|counter; classes 01–08; 09–EF reserved; F0–FF tests; never reused", TR-sim-026 "mailId separate uint32; PlayerSlot u8 0–7; slot freed in T reusable from T+1")*

**ADR Governing Implementation**: ADR-0004: Identity and Id Spaces (§2–§4)
**ADR Decision Summary**: `EntityIds` (one `uint[256]` of next counters per world) and `MailIds` (one `uint`) are per-world instances injected by the composition root; allocation is legal only on the owner thread inside `Tick()` (exception: `Destination`/`Prop` while `Constructed`), and an illegal Release call returns None and counts `idAllocIllegal`. Reserved classes 0x09–0xEF return None + record; 0xF0–0xFF succeed only when built with `allowTestClasses: true`. Exhaustion (class counter at 2^24 − 1, `MailIds` at 2^32 − 1) returns None/0, writes one `IdExhausted` record and asserts — tested before incrementing, never wrapping. `SlotTable` is a `PlayerSlotEntry[8]` whose slot freed in tick T is reassignable only when `CurrentTick ≥ T + 1`.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `EntityClass` table (0x01 Player … 0x08 Prop; 0x09–0xEF reserved → `Allocate` returns None + record; 0xF0–0xFF test-only via `EntityIds(allowTestClasses: true)`); `EntityIds`/`MailIds` per world, injected; owner-thread inside `Tick()` only (Destination/Prop also while `Constructed`); Release illegal call returns None + `idAllocIllegal`; exhaustion returns None/0, one `IdExhausted` record + assert, tested before incrementing; ids ordered by raw `Value`; `SlotTable` (`Core/`) is `PlayerSlotEntry[8]`, freed in T reassignable when `CurrentTick ≥ T + 1`.
- Forbidden: reusing, wrapping or skipping ids (no free lists, no generation counters); static allocators; one global counter with a side-table class.
- Guardrail: allocation 0 B; `uint[256]` per world.

---

## Acceptance Criteria

*From GDD SC-R19-1 (allocation half) and ADR-0004 §2–§4, scoped to this story:*

- [x] **AC-1 SC-R19-1.** Inside a tick, the first `Allocate(Player)` returns 16 777 217 and the first `Allocate(Construct)` returns 50 331 649; a 3-unit mail batch allocates 3 consecutive `MailId`s from the separate mail counter (1, 2, 3) and a 0-unit batch allocates none; `Allocate` of any class in 0x09–0xEF returns `EntityId.None`, writes one check record and (under `SIM_ASSERT`) asserts; a test class 0xF0 is refused by a default `EntityIds` and succeeds with `allowTestClasses: true`.
- [x] **AC-2 Legality and exhaustion.** `Allocate(Player)` between ticks asserts under `SIM_ASSERT`, else returns None and counts `idAllocIllegal`; `Allocate(Destination)` while the world is `Constructed` succeeds; `EntityIds` constructed with a class counter at 2^24 − 1 and `MailIds` at 2^32 − 1 return None/0 on the next allocation, write one `IdExhausted` record each (a = class, b = counter) and never wrap (Debug checked build does not throw an overflow).
- [x] **AC-3 SlotTable.** A slot freed at tick T cannot be assigned while `CurrentTick == T` and can be from T + 1; `IsPresent(playerId)` is true only for occupied slots; `ConnectedMask` returns one bit per occupied slot; the table is readable through a read-only view (any system) and writable only by Sim Core.

---

## Implementation Notes

*Derived from ADR-0004 §2–§4, Key Interfaces and Implementation Guidelines:*

- `src/PerformativeMail.Sim/Core/EntityIds.cs`: `EntityIds(ITickReader tick, SimCheckRing ring, SimCounters counters, ReadOnlySpan<uint> initialCounters = default, bool allowTestClasses = false)`; `Allocate(EntityClass c)`; `Peek(EntityClass c)` for tests and the dump. Counters start at 1 (counter 0 is never issued). Check order: legality → reserved/test class → exhaustion (`next > 0xFF_FFFF`) → issue `EntityId.Compose(c, next++)`.
- `src/PerformativeMail.Sim/Core/MailIds.cs`: `MailIds(ITickReader tick, SimCheckRing ring, SimCounters counters, uint initial = 1)`; `Allocate()`; `Peek`. Lower = older (ADR-0004 §3).
- Legality uses `ITickReader.Stage == InPhase` (inside `Tick()`) plus the owner-thread assert; the world-build exception for `Destination`/`Prop` uses the world state `Constructed`. Expose state through a narrow interface rather than a `SimWorld` reference (ADR-0003 §4: no system holds `SimWorld`; allocators are Core services but keep the same discipline).
- `src/PerformativeMail.Sim/Core/SlotTable.cs`: `PlayerSlotEntry { EntityId PlayerId; uint FreedTick; bool Occupied; }` × 8; `ISlotView` (read-only: `IsPresent(ActorId)`, `SlotOf(EntityId)`, `ConnectedMask`, `CanAssign(PlayerSlot, uint currentTick)`); write methods (`Assign`, `Free`) are internal to Core — the `PlayerJoin`/`PlayerLeave` handlers (Story 022) are their only callers.
- `CheckCode.IdExhausted` exists from Story 007; add `IdReservedClass` if a distinct code is wanted for the reserved-class record.
- `SimInitialCounters` (ADR-0003 `SimBuildArgs`) will carry the class counters, `mailId` and `opSeq` start values — accept them via constructors now; the composition story wires them.


> **Correction (Story 018 implementation, 2026-10-16):** counters store the **last issued** id, not the next one — `EntityIds` counters and `MailIds(…, uint lastIssued = 0)` default to 0 (first id issued = 1), `Peek` returns the last issued value, and exhaustion is "stored counter == 2^24 − 1 (entity) / 2^32 − 1 (mail)". This is the only reading under which AC-2 holds and matches `SimInitialCounters.LastOpSeq`. ADR-0004 Key Interfaces amendment tracked in TD-043.

---

## Out of Scope

- Story 002: id value types and `Compose`/decode.
- Story 022: `PlayerJoin`/`PlayerLeave` handlers (`slot_invalid`, `slot_busy`, `duplicate_player`), SC-R21-1.
- Story 019: dumping counters and the slot table.
- SC-R20-1 (M-1b) — mechanics land here; the AC is verified at M-1b.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/IdAllocatorTests.cs` (`[Trait("Id", "SC-R19-1")]`), `tests/unit/sim/Core/SlotTableTests.cs` — must exist and pass (Debug + Release)

**Status**: [x] Created and passing — `tests/unit/sim/Core/IdAllocatorTests.cs` (SC-R19-1; + NonParallel `IdAllocatorAllocationTests`), `tests/unit/sim/Core/SlotTableTests.cs`

---

## Dependencies

- Depends on: Story 002, Story 007, Story 012 (stage/state, owner thread)
- Unlocks: Story 019, Story 020 (`unknown_actor` pre-check), Story 022, every allocating system

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 → IdAllocatorTests `Ac1_FirstIds_PlayerAndConstruct`, `Ac1_MailBatchOfThree_…`, `Ac1_ReservedClass_…` Theory, `Ac1_WholeReservedRange_NeverAllocates`, `Ac1_TestClass_RefusedByDefault_AndSucceedsWithAllowTestClasses` [SC-R19-1]; AC-2 → `Ac2_PlayerAllocatedBetweenTicks_…`, `Ac2_MailAllocatedBetweenTicks_IsIllegal`, `Ac2_AllocationFromAnotherThreadInsideATick_IsIllegal`, `Ac2_DestinationAndProp_AreLegalWhileConstructed_…`, `Ac2_EntityClassAtMax_…`, `Ac2_MailAtMax_…`; AC-3 → SlotTableTests `Ac3_SlotFreedAtTickT_CannotBeAssignedAtT_ButCanFromTPlusOne`, `Ac3_IsPresent_IsTrueOnlyForOccupiedSlots`, `Ac3_ConnectedMask_HasOneBitPerOccupiedSlot`, `Ac3_ReadOnlyView_ExposesNoWrites_AndWritersAreNotPublic`)
**Verification run**: closer re-ran all four legs from disk — 1 514 tests/leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 10 skipped = 8 pre-existing + 2 new optimised-only alloc tests, TD-008 pattern). Count includes other runners' concurrent tests.
**Files**: new src/PerformativeMail.Sim/Core/{IWorldStateReader,IdAllocGate,EntityIds,MailIds,SlotTable}.cs; SimWorld.cs implements IWorldStateReader; Primitives/SimAssertCode.cs 43 IdAllocIllegal, 44 IdReservedClass, 45 IdExhausted; Core/CheckCode.cs 6 IdReservedClass; tests IdAllocatorTests.cs, SlotTableTests.cs (new). CounterId.IdAllocIllegal (18) reused.
**Deviations**: ADVISORY (accepted by LP) — last-issued counter convention (see Correction above; ADR amendment → TD-043); class 0x00 treated as reserved (ADR §2 "never allocated"); test class without allowTestClasses uses the IdReservedClass record; each allocator pins its owner thread at construction; `tick as IWorldStateReader` fallback when no `state` passed; SlotTable.Assign/Free return bool (no assert) for Story 022 reject mapping, `PlayerSlotEntry.NeverFreed = uint.MaxValue`. SimWorldBuilder / SimInitialCounters / composition untouched (Story 025).
**Gates**: QL-TEST-COVERAGE GAPS (minor, advisory → TD-043); LP-CODE-REVIEW APPROVE (7 advisories → TD-043).
**Test Evidence**: Logic: tests/unit/sim/Core/IdAllocatorTests.cs, tests/unit/sim/Core/SlotTableTests.cs
**Code Review**: Complete (LP-CODE-REVIEW APPROVE)
**Run result**: N/A — Godot-free Sim logic, no observable surface; verified by the four-leg suite run above.
