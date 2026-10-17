# Story 016: Event overflow (single tier + masks), misuse raises and pull readers

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 400 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R12 pull cursors, R14 overflow and `overflowInfo`; N3 resync scopes)
**Requirement**: `TR-sim-024` (overflow admission; `overflowInfo` masks — **M-1a slice: single-tier flag + masks**; two-tier admission at its first consumer), `TR-sim-022` (pull cursors ≤ 8; no sync subscribers)
*(Requirement text: `docs/architecture/tr-registry.yaml` TR-sim-022 "No sync subscribers; pull cursors ≤8; IEventSink.Deliver once in phase 13, copy-only", TR-sim-024 "Two-tier overflow admission; overflowInfo masks; adapter resyncs exactly those" — M-1a slice is single tier)*

**ADR Governing Implementation**: ADR-0008: Event Log, Replication Classes and Event Delivery (§4 pull cursors, §6 overflow and misuse)
**ADR Decision Summary**: When the log cannot admit an entry it is dropped — never reordered, never rolled back — and the drop increments `eventsDropped`, sets `OverflowInfo.Overflow`, ORs the entry's raise-time audience into `DroppedAudienceMask` (a dropped `Replicated` entry contributes the connected mask) and `1UL << ResyncScope` into `DroppedKindsMask`. Raises outside the legal window (outside `Tick()`, in a tick-end check, during `Deliver`) assert in Debug and are dropped and counted in Release; the last two also set `Overflow`. Later-phase consumers declare a pull reader at build (≤ 8, sorted `ushort[]` filter) and read with `EventLog.Read(ref cursor, out entry)`; cursors see only entries already in the log and rewind each tick; kinds with a reader are flagged `HasReader` at `Freeze()`.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: overflow admission per ADR-0008 §6 — each drop counts `eventsDropped`, sets `Overflow`, ORs audience into `DroppedAudienceMask` and `1UL << ResyncScope` into `DroppedKindsMask`; no rollback, no reorder; later-phase consumers declare a pull reader at build (≤ 8, sorted `ushort[]` filter) and read with `EventLog.Read(ref cursor, out entry)`; cursors rewind each tick.
- Forbidden: resyncing every audience on overflow (use the masks); raising outside phases 0–12, in a tick-end check or during `Deliver`; synchronous subscribers.
- Guardrail: event log 4 096 × 64 B, reserve 256; F8 `cap × 4 + 1.5 × systemEventsPerTick ≤ capacity − reserve` (826 ≤ 3 840); `Read` 0 B.

---

## Acceptance Criteria

*From GDD SC-R14-1 and ADR-0008 §4, §6, scoped to this story:*

- [x] **AC-1 SC-R14-1 (single tier).** Given a small log (test capacity, e.g. 16) and three audiences where slot 2's only entries fall after the limit, when the log overflows: later entries are dropped and `eventsDropped` counts each; nothing already appended is reordered; state changes made by the raisers are not rolled back; the `OverflowInfo` passed to `Deliver` is `{ Overflow = true, DroppedAudienceMask = slot 2's bit OR the other dropped entries' masks, DroppedKindsMask = OR of 1 << ResyncScope of the dropped kinds, DroppedCount = n }`; a dropped `Replicated` entry contributes every connected slot's bit; the accumulator resets after `Deliver`.
- [x] **AC-2 Misuse raises.** A raise between ticks, a raise from an `ITickEndCheck`, and a raise from inside `IEventSink.Deliver` each throw `SimAssertException` under `SIM_ASSERT`; otherwise each is dropped and counted (`eventsOutsideTick`, `eventsAtTickEnd`, `eventsDuringFlush` respectively), and the latter two set `Overflow` for that tick's delivery.
- [x] **AC-3 Pull readers.** A reader declared at build with a filter of kinds {A, B}: after phase 10 raises A, C, B, a phase-11 read loop returns A then B and then false; a second loop in the same phase returns false (cursor advanced); an entry the reader raises itself is appended after everything already in the log; the cursor starts at 0 again on the next tick; `HasReader` is true for A and B after `Freeze()` and false for C; `Read` allocates 0 B.

---

## Implementation Notes

*Derived from ADR-0008 §4, §6 and Key Interfaces:*

- **Single-tier admission at M-1a** (GDD milestone slice): drop when `count == capacity`. The second tier (`count ≥ capacity − reserve` drops kinds that are neither `HasReader` nor `OpReply`) is the frozen interface built by its first consumer; compute and store `HasReader` now (AC-3) so the second tier is a one-line addition with SC-R14-2 (M-1b).
- `OverflowInfo` (readonly struct): `Overflow`, `DroppedAudienceMask` (byte), `DroppedKindsMask` (ulong, bit = `ResyncScope`), `DroppedCount`. Accumulate inside `EventLog`; pass `in` to `Deliver`; reset with the log.
- "Dropped `Replicated` contributes the connected mask": the audience stored at raise already is the connected mask (the raiser computes it from `SlotTable`, Story 018); OR the raise-time audience regardless of class.
- Legality window: `TryRaise` checks the world's stage (`ITickReader.Stage`): `BetweenTicks` → outside-tick; `TickEnd` → tick-end; `_delivering` → during-flush. Use `SimAssert.Fail(code)` first (Debug throws), then count and drop (Release). `TryRaise` returns `false` on any drop.
- Readers: `SimWorldBuilder.BindEventReader(ReaderId id, EventKind[] filter)` (manifest I-3: ADR-0008's name) copies and sorts the filter into a `ushort[]` (use Story 005 `StableSort` or a tiny insertion sort — no `Array.Sort`); `EventCursor` is a struct `{ ReaderId, Position }`; `Read(ref EventCursor c, out EventRef e)` scans from `c.Position` to the current `count`, matching kinds by binary search over the filter. `EventRef` gives read-only access to the entry and `PayloadAs<T>()` (unaligned). Cursors are owned by the reading system; Sim Core rewinds them at tick start (keep a registry of cursor positions in the log indexed by `ReaderId`, so the reader's struct is just a handle).
- The 9th reader → `SimBuildException(ReaderLimit)` belongs to Story 017's Theory; enforce the limit here and let Story 017 assert the code.
- `IOpRecorder.OnOverflow(tick)` is called from the flush when `Overflow` is set — call site lands with Story 026.

---

## Out of Scope

- Story 015: log append, scopes, delivery.
- Story 017: `ReaderLimit` build code in the SC-R4-1 Theory.
- Two-tier reserve admission (SC-R14-2), SC-R12-1 reader ordering AC, SC-MIS-1 rows — M-1b ACs; mechanics land here.
- Adapter resync of `DroppedAudienceMask × DroppedKindsMask` — network-adapter-loopback / M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/EventOverflowTests.cs` (`[Trait("Id", "SC-R14-1")]`), `tests/unit/sim/Core/EventReaderTests.cs` — must exist and pass (Debug + Release)

**Status**: [x] Created and passing — `tests/unit/sim/Core/EventOverflowTests.cs` (SC-R14-1), `tests/unit/sim/Core/EventReaderTests.cs` (+ NonParallel `EventReaderAllocationTests`); TD-032/TD-033 tests added to EventLogTests.cs / EventDeliveryTests.cs

---

## Dependencies

- Depends on: Story 015
- Unlocks: Story 017, Story 026 (`OnOverflow`), player-character (phase-11 reader), health-damage

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 → EventOverflowTests `Ac1_LogOverflows_…` [SC-R14-1], `Ac1_OnlySlot2Dropped_…`, `Ac1_QuietTickAfterAnOverflow_…`; AC-2 → EventOverflowTests `Ac2_RaiseBetweenTicks_…`, `Ac2_RaiseFromATickEndCheck_…`, `Ac2_RaiseFromInsideDeliver_…`; AC-3 → EventReaderTests `Ac3_ReaderFilteringAAndB_…`, `Ac3_EntryTheReaderRaisesItself_…`, `Ac3_ThroughAWorld_…CursorRestartsAtZeroNextTick`, `Ac3_HasReader_…`, `EventReaderAllocationTests.Ac3_Read_AllocatesNothing`)
**Verification run**: closer re-ran all four legs from disk after the LP fixes — 1 441 tests/leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 8 skipped = pre-existing optimised-only alloc tests). Count includes other runners' concurrent tests (story 016 adds 47 + 0 on fix round).
**Files**: new src/PerformativeMail.Sim/Core/{ReaderId,EventCursor,EventRef}.cs; modified Core/{EventLog,EventCallScope,OverflowInfo,IEventSink,SimWorld,SimWorldBuilder,SimWorldParts,SimBuildException,CounterId,SimCoreSettings}.cs, Primitives/SimAssertCode.cs; tests EventOverflowTests.cs, EventReaderTests.cs (new), EventLogTests.cs, EventDeliveryTests.cs, SimCountersTests.cs.
**New ids**: CounterId.ScopeLeak = 24; SimAssertCode 35 EventPayloadTypeMismatch, 36 EventRaiseOutsideTick, 37 EventRaiseAtTickEnd, 38 EventRaiseDuringFlush, 39 EventReaderUnknown, 42 EventReaderFilterInvalid; SimBuildErrorCode 6 ReaderLimit, 7 InvalidEventReader (Story 017 extends from 8).
**Deviations**: ADVISORY (accepted by LP) — eventsDropped also counts misuse drops (ADR §6 "every dropped entry"); between-ticks drops do not set Overflow (no delivery carries them); live `in` OverflowInfo passed to Deliver (documented); ReaderId opaque, log scans ≤ 8 ids; EventCursor.Position is a get-only mirror of the log's registry; Release truncates an over-descriptor payload to the descriptor length (story-015 test now expects 4). TD-032 item 4: LatchStop in phase 13 still counted AND latched (ADR-0003 §5), documented + pinned. Carry-over done: both TODO(Story-016) filled, scopeLeak counter, `Ac1_RaiseAfterDeliver_BetweenTicks_IsAcceptedAgain` → `…_IsMisuse_AndTheLogIsOpenAgainOnTheNextTick`.
**Gates**: QL-TEST-COVERAGE ADEQUATE (minor gaps → TD-040); LP-CODE-REVIEW CONCERNS → 1 blocking (TryRaise 57 lines / CC≈13) fixed by extraction (TryRaise now 23 lines) + 5 advisories applied; fix verified by the closer (line count + four legs), LP not re-spawned. godot-csharp-specialist cross-check not run (LP covered the .NET surface; prioritising Story 017). Advisories → TD-040; TD-032 Resolved; TD-033 Open (item 2 → Story 025).
**Test Evidence**: Logic: tests/unit/sim/Core/EventOverflowTests.cs, tests/unit/sim/Core/EventReaderTests.cs
**Code Review**: Complete (LP-CODE-REVIEW CONCERNS resolved)
**Run result**: N/A — Godot-free Sim logic, no observable surface; verified by the four-leg suite run above.
