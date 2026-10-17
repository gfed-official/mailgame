# Story 011: Phase table, builder binds and the tick loop

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 450 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R1 the tick, R3 phase list, R4 binding, R15 absolute deadlines)
**Requirement**: `TR-sim-002` (tick is `uint32` from 0; first `Tick()` after `Ready` is tick 0), `TR-sim-010` (exactly 14 `PhaseId`s; `ITickEndCheck` between 12 and 13), `TR-sim-011` (unbound phase is a no-op; same-phase binders in composition order), `TR-sim-041` (port interfaces — the ADR-0003-owned subset)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§1, §2, §3 binds/freeze, Key Interfaces)
**ADR Decision Summary**: Exactly 14 `PhaseId`s (0 `InputsApply` … 12 `RunStep`, 13 `EventsFlush`) run in a fixed order, with bound `ITickEndCheck`s between 12 and 13. `SimWorldBuilder` (used only while `Constructed`) binds `ISimPhase`s per phase and tick-end checks; several binders of one phase run in composition (bind) order; an unbound phase is a no-op; binding 0 or 13 throws `SimBuildException(ReservedPhase)`. `Freeze()` copies tables to arrays and moves to `Ready`. `TickContext` is `readonly struct { uint Tick; PhaseId Phase; }` passed `in`; `IPhaseProbe` defaults to a null object never null-checked per call.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: phases run exactly 0…12, tick-end checks, 13 (`enum PhaseId : byte`); `SimWorld.Tick()` follows the ADR-0003 §2 sequence; `TickContext` is `readonly struct { uint Tick; PhaseId Phase; }` passed `in`, services by constructor (manifest I-2: follow ADR-0003); `IPhaseProbe` null object, never null-checked per call (manifest I-1: ADR-0003's `Before(PhaseId)/After(PhaseId)` shape); the phase loop is allocation-free (arrays and `for`, no LINQ or delegates).
- Forbidden: binding phase 0 or 13 from a system; `ITickEndCheck` that mutates or raises; synchronous subscribers; LINQ/delegates/interface `foreach` in the loop; throwing `SimBuildException` inside `Tick()`.
- Guardrail: an unbound phase costs nothing; 0 B per tick from the loop itself.

---

## Acceptance Criteria

*From GDD SC-R1-1 and SC-R3-1, scoped to this story:*

- [x] **AC-1 SC-R1-1.** Given a `Ready` world and a test timer phase with `deadlineTick = 100`, when `Tick()` is called 101 times: a bound phase observes `CurrentTick == 0` (and `TickContext.Tick == 0`) during the first call; `CurrentTick` reads 1 after it and 101 after the last; the timer (`CurrentTick ≥ deadlineTick`) is not due at `CurrentTick` 99 and is due at 100.
- [x] **AC-2 SC-R3-1.** Given stubs bound to every bindable phase (1–12), two stubs bound to phase 11 and two tick-end checks, when one tick runs, the recorded order (stubs + `IPhaseProbe` brackets) is phases 0…12 in order, both phase-11 stubs in bind (composition) order, both tick-end checks once each in bind order, then 13; `ITickReader.Stage` reads `InPhase` inside phases, `TickEnd` inside checks, `BetweenTicks` after `Tick()` returns.
- [x] **AC-3 Reserved phases and freeze.** `Bind(PhaseId.InputsApply, …)` and `Bind(PhaseId.EventsFlush, …)` throw `SimBuildException` with `Code == ReservedPhase`; any `Bind`/`BindTickEnd` after `Freeze()` throws `Code == BindAfterFreeze`; a world with no bound phases ticks (all phases no-ops).

---

## Implementation Notes

*Derived from ADR-0003 §1–§3, Key Interfaces and Implementation Guidelines:*

- Files in `src/PerformativeMail.Sim/Core/`: `PhaseId.cs` (`enum PhaseId : byte { InputsApply, ItemsExpire, IntakeDrain, PlayersStep, ThrowingStep, AutomationStep, VehiclesStep, AgentsStep, MailStep, EconomyFlush, CombatResolve, CombatReact, RunStep, EventsFlush }`), `TickContext.cs`, `ISimPhase.cs` (`void Run(in TickContext ctx)`), `ITickEndCheck.cs` (`void Check(uint tick)`), `TickStage.cs`, `ITickReader.cs` (`CurrentTick`, `Stage`, `Phase` — `Phase` valid only when `Stage == InPhase`), `IPhaseProbe.cs` + `NullPhaseProbe`, `IRunControl.cs` (declared; behaviour in Story 012), `SimState.cs`, `SimBuildException.cs` + `SimBuildErrorCode` (seed with `ReservedPhase`, `BindAfterFreeze`; Story 017/025 append), `SimWorldBuilder.cs`, `SimWorld.cs`, `SimCoreSettings.cs`.
- **`SimCoreSettings`** — Sim Core's own `SimSettings` sub-record (ADR-0003 §4 injectable list; GDD "Sim Core owns its `SimSettings` keys"): `sealed record class` with init-only F8 defaults — `OpQueueCapacity` 4096, `SystemOpReserve` 64, `MaxOpsDrainedPerTick` 64, `EventLogCapacity` 4096, `EventLogReserve` 256, `MaxEventReaders` 8, `MaxCatchUpTicksPerFrame` 4, `CheckRingCapacity` 256. content-data's `SimSettings` will hold it as a member; validation rows (SC-SET-1) are M-1b.
- **`SimWorld.Tick()` for this story** (Story 012 adds the guards/fault path; Stories 021/020/015 fill the sample prologue, the phase-0 drain and the phase-13 flush):
  ```
  Stage = InPhase
  for p in 0..12: _ctx = new TickContext(CurrentTick, p); probe.Before(p);
                  var arr = _phases[p]; for (int i = 0; i < arr.Length; i++) arr[i].Run(in _ctx); probe.After(p)
  Stage = TickEnd; for c in _tickEnd: c.Check(CurrentTick)
  Stage = InPhase; Phase = 13; probe.Before(13); EventsFlush(); probe.After(13)
  Stage = BetweenTicks; CurrentTick++
  ```
  `_phases` is `ISimPhase[][]` frozen at `Freeze()`; empty phases are zero-length arrays. Phase 0's built-in drain becomes the first entry of `_phases[0]` in Story 020; phase 13's flush is built in (Story 015). Bracketing phase 13 with the probe is needed to time `Events.Flush` (ADR-0008 perf slice); ADR-0003's pseudocode shows the probe only for 0–12 — note as an erratum candidate.
- `SimWorld` implements `ITickReader` (or exposes one) for systems; it is never injected into a system as `SimWorld` (ADR-0003 §4).
- `Freeze()` moves `Constructed → Ready`; the first `Tick()` moves `Ready → Running` and processes tick 0. Owner-thread capture is Story 012.
- R15: deadlines are absolute ticks; a timer is due when `CurrentTick ≥ deadlineTick` (plain `uint` compare — R1 asserts `tick < 2^31`).
- Allocation-free loop: no LINQ, no `foreach` over interface-typed collections, no delegates (ADR-0003 Implementation Guidelines).

---

## Out of Scope

- Story 012: lifecycle guards, `badTick`, re-entrancy, owner thread, `Faulted`, `Stop()`, `IRunControl.RequestStop`.
- Story 015: event log, phase-run call scopes, `IEventSink.Deliver` in phase 13.
- Story 017: remaining `SimBuildException` codes and the SC-R4-1 Theory.
- Story 020: the phase-0 drain.
- Story 025: `SimComposition` (composition order = construction order); here bind order stands in for it.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/TickLoopTests.cs` (`[Trait("Id", "SC-R1-1")]`, `[Trait("Id", "SC-R3-1")]`) — must exist and pass

**Status**: [x] Created — tests/unit/sim/Core/TickLoopTests.cs

---

## Dependencies

- Depends on: Story 001; Story 007 (`SimCounters`/`SimCheckRing` held by the world)
- Unlocks: Story 012, Story 014, Story 015, every phase-binding system story

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 SC-R1-1 tick 0 first, CurrentTick 1 / 101, R15 timer due at 100 not 99; AC-2 SC-R3-1 phases 0…12 → tick-end checks in bind order → 13 with probe brackets, phase-11 binders in bind order, Stage InPhase/TickEnd/BetweenTicks; AC-3 ReservedPhase for 0/13, BindAfterFreeze after Freeze, empty world ticks)
**Deviations**: None blocking. Advisory: probe also brackets phase 13 (ADR-0003 §2 pseudocode shows 0–12 — erratum, TD-013); phase-0 drain hook is a private `InputsDrain()` branch rather than the first entry of `_phases[0]` (behaviour-equivalent; story 020 must not add both — TD-013); `StopReason` stub enum (`Unspecified`); second `Freeze()` → BindAfterFreeze; frozen check precedes reserved-phase check. Forward items for stories 012/015/017/020 → TD-013.
**Test Evidence**: Logic — tests/unit/sim/Core/TickLoopTests.cs (20 methods / 21 cases, incl. Release-only 0-B tick test); suite 607/leg: Release 607/607, Debug 604 + 3 skipped (TD-008), net10.0 and net8.0.
**Run result**: N/A — Godot-free tick loop; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (9 non-blocking; finding 8's "no 0-B test" is incorrect — `Tick_WithBoundStubPhases_AllocatesNothing_InOptimisedBuilds` exists); QL-TEST-COVERAGE ADEQUATE.
