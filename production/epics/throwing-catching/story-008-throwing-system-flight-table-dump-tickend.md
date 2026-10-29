# Story 008: `ThrowingSystem` shell, the `FlightTable` (SoA, 160 slots, wrap-safe `FlightId`), the canonical dump section and the tick-end conservation record

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Op/host story (2 of 5).** The host-side state every other host story mutates: the live flights in creation order, the per-slot cooldown clock, and the always-on record that proves each flight resolved exactly once. No behaviour yet — launch is story 010, stepping is story 011.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T4 "The host creates the `FlightRecord`" table; T5 "Flights are processed in creation order"; T6 "Every flight ends in exactly one terminal state"; Edge Cases "If `flightId` wraps")
**Requirement**: `TR-throwing-009` (`FlightRecord` snapshot fields; `flightId` wrap-safe u16), `TR-throwing-013` (exactly one terminal per flight — the record half), `TR-throwing-008` (phase 4, 0 alloc, capacity), `TR-throwing-006` (creation order)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-E-6 c, TC-P-2 (0 B half, story 015), TC-X-1 (invariant half, story 015).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§9 state, capacity, dump; §5 conservation) — Accepted 2026-10-16 (primary); ADR-0004 (`FlightId` u16, never compared numerically), ADR-0003 (`[SimSystem]`, one public constructor, `SystemGraph`, `ITickEndCheck`), ADR-0006 (`ISimStateContributor`, canonical dump, 0-alloc) secondary — Accepted
**ADR Decision Summary**: `FlightRecord` is SoA, preallocated: `FlightId`, entry ref + `mailIds` by reference (ADR-0009 Flight ledger), thrower slot, `classPts`, origin, vel, `launchTick`, `thrownPm` (snapshotted), `releaseTick`, `openAtRelease`, `hop`, `originClamped`. Capacity `maxFlights` = **160** (8 players × 6/s × 3 s = 144, plus slack); 16 is the perf fixture. Per slot: `TAT`, arm state, `lastCatch`. A flight leaves the live list only by resolving; an `ITickEndCheck` asserts `launched == resolved + live` and that no `FlightId` resolved twice (an always-on record, never throws). Throwing's `ISimStateContributor` writes live flights in creation order, the `FlightId` serial, per-slot `TAT` and arm state.
**ADR Version**: ADR-0015 2026-10-16; ADR-0004 2026-10-16; ADR-0003 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: each system lives in `Sim/<System>/`, is marked `[SimSystem(SystemId)]`, has exactly one public constructor with parameters only from the ADR-0003 §4 injectable list; dependencies declared in `Composition/SystemGraph.cs`; bind only phases you own (Throwing: phase 4 and the phase-0 op handlers)
- Required: bind an `ISimStateContributor` for any state; size every collection from settings; allocate 0 B in the tick path; ids ordered by raw `Value`, `FlightId` never compared numerically (ADR-0006 §4/§8, ADR-0004)
- Forbidden: an RNG stream in Throwing; statics or a service locator; subscribing synchronously to another system
- Guardrail: `maxFlights` = 160 preallocated; `FlightTable` operations O(1) amortised, allocation-free

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-E-6 c) and ADR-0015 §5/§9, scoped to this story:*

- [ ] **Shell and composition.** `ThrowingSystem` is `[SimSystem(SystemId.Throwing)]` with one public constructor taking only injectable parameters (`EventLog` writer, read-only `SlotTable`, `IInventoryApi`, `IMailApi`, `IPlayersApi`, `IWorldQuery`, `ThrowBalance`/settings record, `SimCheckRing`, `SimCounters`, `IStatSheet`); `SystemGraph` declares Throwing → Inventory, Mail, Players, World; the composition-root reflection test (`production/epics/simulation-core/story-025-composition-root-reflection-test.md`) and architecture rules pass with it. It binds `PhaseId.ThrowingStep` (an empty step here; story 011 fills it) and exposes `IThrowingApi { int LiveFlights { get; } void FlushAllAsTimeout(); }` (the flush body is story 013; until then `FlushAllAsTimeout` is a no-op that returns immediately — never `NotImplementedException` — covered by a test that asserts 0 live flights afterwards).
- [ ] **Capacity and fields.** `FlightTable.TryAppend(in FlightInit)` stores every ADR field (flightId, entry id, `mailIds` ref, units, kind/def, thrower slot, `classPts`, origin, vel, `launchTick`, `thrownPm`, `releaseTick`, `openAtRelease`, `hop`, `originClamped`) and returns an index; 160 appends succeed; the 161st returns false (the caller's `flight_cap` path, story 009) and is counted.
- [ ] **Creation order.** Iteration is by array position = launch order. Resolving (removing) a middle flight and compacting stably preserves the relative order of the others; a randomised churn test (10 000 appends/removes, ≤ 160 live) keeps order equal to a reference list.
- [ ] **TC-E-6 c (`FlightId` wrap).** The serial is a u16; after 70 000 launches (each resolved shortly after) the table still processes in creation order and no code path compares two `FlightId`s with `<`/`>` or sorts by them (an architecture test greps `Sim/Throwing/` for such use; `FlightId` equality is allowed); a `FlightId` is never live twice (the allocator skips an id still live — impossible below 65 536 launches in flight, asserted).
- [ ] **Per-slot clocks.** `Tat[slot]` (absolute tick, u32) is stored per `PlayerSlot`, initial 0, cleared on `PlayerLeave`/rejoin per the Players join/leave hooks (`production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`); story 009 reads/writes it.
- [ ] **Tick-end record.** An `ITickEndCheck` asserts after phase 12 each tick that `launched == resolved + live` and that no `FlightId` resolved twice in the run; it writes to `SimCheckRing` and counters and **never throws** (always-on, like the Grid conservation record). A test that resolves one flight twice or drops one without resolving it sees the check fire (counter and ring entry); a normal churn run never fires it.
- [ ] **Dump.** The Throwing `ISimStateContributor` section writes live flights in creation order field by field, the `FlightId` serial, per-slot `Tat`, and a reserved block for the catch arm state; changing any single stored field (including `hop`, `openAtRelease`, `thrownPm`, `originClamped`) changes the dump hash; two tables reaching the same live set by different churn histories dump identically.
- [ ] **0 B.** Constructing the table allocates its arrays once; 100 000 append/remove cycles allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0015 §9 and ADR-0003 §4:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `ThrowingSystem.cs`, `IThrowingApi.cs`, `FlightTable.cs` (struct-of-arrays; `int Count`; `ref readonly` accessors; `Remove(int index)` marks dead and `Compact()` stably closes gaps at the end of the step), `FlightInit.cs`, `FlightIdSerial.cs` (u16 counter; `FlightId` from `production/epics/simulation-core/story-002-id-value-types.md`), `ThrowingStateContributor.cs`, `ThrowingTickEndCheck.cs`, `ThrowSettings` wiring from story 001.
- Entry reference: the record keeps the `EntryId` and the by-reference `mailIds` handle that `IInventoryApi.DetachForThrow` returns (`DetachResult`, `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md`; Flight ledger column, `production/epics/grid-inventory/story-017-unitledger-conservation.md`). **Flagged:** the exact type of the Flight-held entry handle (how Throwing later hands it to `TryCatch`, `ResolveThrownLanding` and `CreateWorldItem`) is not pinned by the grid-inventory stories; define `FlightEntryRef` here as a thin wrapper over whatever `DetachResult` returns and confirm against story 015/017 when implementing. Do not add a Throwing-side unit list.
- Capacity: `maxFlights` is a `throwing` settings key (default 160) — add it to the settings record alongside the balance keys; content-data story 008 owns `ThrowBalance`, this key is an engine capacity, not a tuning knob (log it).
- `Tat` belongs to the Throwing dump section because the leaky bucket must replay identically (ADR-0015 §9).
- The tick-end check registers through `SimWorldBuilder.BindTickEnd` (`production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`); the always-on record pattern mirrors Grid's conservation check (`story-017`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: validation chain and the bucket arithmetic. Story 010: launch and `ThrowLaunched`. Story 011: sweeping and terminals. Story 013: `FlushAllAsTimeout`. Story 015: the 8-player conservation fuzz and perf.
- `lastCatch` / T13 relay hops (M0) — the reserved dump block only.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/FlightTableTests.cs`, `tests/unit/throwing/ThrowingDumpTests.cs` (`[Trait("Id", "TC-E-6c")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; `production/epics/simulation-core/story-002-id-value-types.md`; `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`; `production/epics/simulation-core/story-018-id-allocators-slottable.md`; `production/epics/simulation-core/story-019-canonical-state-dump.md`; `production/epics/simulation-core/story-025-composition-root-reflection-test.md`; `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`DetachForThrow`, `DetachResult`)
- Unlocks: Stories 009, 010, 011, 012, 013, 015, 022
