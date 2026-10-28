# Story 012: World lifecycle, tick guards, owner thread and faults

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (States and Transitions; R18 main thread; `IRunControl`)
**Requirement**: `TR-sim-044` (lifecycle Constructed → Ready → Running → Stopped/Faulted; `RequestStop` via `IRunControl` in phase 12), `TR-sim-007` (Sim on the main thread; owner thread captured at `Freeze()`; Debug asserts)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§2 Tick sequence, §5 `RequestStop`, §7 lifecycle) — primary; ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim (§5 thread) — secondary
**ADR Decision Summary**: The world moves only Constructed → Ready (`Freeze`) → Running (first `Tick`) → Stopped | Faulted. `Tick()` first guards: state `Running` (or `Ready` for the first tick), owner thread, not re-entrant — otherwise it counts `badTick` and runs nothing. An exception escaping a phase marks the world `Faulted`, the partial log is not delivered, the re-entrancy flag is cleared in `finally`, and `SIM_ASSERT` builds rethrow after marking. `IRunControl.RequestStop` in phase 12 is latched and applied after phase 13 of the same tick; a host `Stop()` outside a tick transitions immediately. The owner thread is captured at `Freeze()` and asserted (Debug) with `Environment.CurrentManagedThreadId`.
**ADR Version**: 2026-10-16 (ADR-0003), 2026-10-16 (ADR-0002)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: Spike fact F9: Godot's `_Process` runs on managed thread 1 — the owner-thread rule matches it; nothing engine-side is touched here.

**Control Manifest Rules (Foundation)**:
- Required: lifecycle `Constructed → Ready (Freeze) → Running (first Tick) → Stopped | Faulted`; `Tick()` guard per ADR-0003 §2 (count `badTick`); exception → `Faulted`, partial log not delivered, `SIM_ASSERT` builds rethrow; owner thread captured at `Freeze()`, asserted in `Tick()` under `SIM_ASSERT` using `Environment.CurrentManagedThreadId`; pause is not a world state.
- Forbidden: `Thread`/`Thread.CurrentThread` (banned API); `SimBuildException` inside `Tick()`; exceptions as control flow in the tick path.
- Guardrail: the guard and stage flags allocate 0 B.

---

## Acceptance Criteria

*From GDD SC-ST-1 and ADR-0003 §2/§5/§7, scoped to this story:*

- [x] **AC-1 SC-ST-1.** For a world in each state, every transition and `Tick()` call is attempted: only Constructed → Ready → Running → Stopped/Faulted is legal (illegal transitions such as `Freeze()` twice or `Stop()` on a `Constructed` world are refused); `Tick()` on a `Constructed`, `Stopped` or `Faulted` world, and a re-entrant `Tick()` from inside a phase, runs no phase, leaves `CurrentTick` unchanged, flushes nothing and counts `badTick` +1 each.
- [x] **AC-2 Fault path.** A phase that throws marks the world `Faulted`; phase 13 does not run for that tick; the re-entrancy flag is clear afterwards (a later `Tick()` counts `badTick`, it does not throw "re-entrant"); with `SIM_ASSERT` the original exception is rethrown after marking, without it `Tick()` returns normally.
- [x] **AC-3 Stop.** `IRunControl.RequestStop` called from a phase-12 stub during tick T lets tick T finish through phase 13, increments `CurrentTick` once, then the world is `Stopped` and the next `Tick()` counts `badTick`; `SimWorld.Stop()` called between ticks moves `Running → Stopped` immediately.
- [x] **AC-4 Owner thread.** `Tick()` called from a thread other than the one that called `Freeze()` throws `SimAssertException` under `SIM_ASSERT` (test spawns the thread in the test project — the Sim never starts threads).

---

## Implementation Notes

*Derived from ADR-0003 §2, §5, §7 and ADR-0002 §5:*

- Extend `SimWorld.Tick()` (Story 011) to the full ADR-0003 §2 shape:
  ```
  guard: (State == Running || State == Ready) && ownerThread && !_inTick, else counters.Add(BadTick); return
  _inTick = true
  try { if Ready: State = Running; [sample prologue — Story 021]; phases 0..12; tick-end; 13; CurrentTick++; if _stopLatched: State = Stopped }
  catch (Exception) when (MarkFaulted()) { }        // or catch → State = Faulted; if (Sim.AssertsEnabled) throw;
  finally { _inTick = false; Stage = BetweenTicks }
  ```
  The rethrow is keyed on `SIM_ASSERT` (`Sim.AssertsEnabled`), never `DEBUG`. "Partial log not delivered" means phase 13 is skipped; Story 015 ensures the log is still reset.
- Owner thread: `Freeze()` stores `Environment.CurrentManagedThreadId`; `Tick()` (and later `EnqueueOp`, sample-ring writes, `RunFrame`) call a `[Conditional("SIM_ASSERT")]` `AssertOwnerThread()`. `Environment.CurrentManagedThreadId` is not on the banned list; `Thread.CurrentThread` is.
- `IRunControl` implementation lives inside `SimWorld` (or a small internal class holding the world's stop latch); it is handed only to Run Structure by the composition root (ADR-0003 §4). Legal in phase 12; the out-of-phase latch-and-count (`stopOutOfPhase`) and "second stop ignored and counted" rules are ADR-0003 §5 — implement them (trivial) but their test, SC-ST-2, is M-1b.
- `CheckCode.BadTick` record: optional; the AC requires the counter. Do not format strings.
- `EnqueueOp`'s lifecycle refusals (`enqueueBadState`) are Story 014.

---

## Out of Scope

- Story 011: phase order and tick counter.
- Story 014: `EnqueueOp` state checks; Story 015: `Deliver` closure rules; Story 019: `TryWriteDump` on `Faulted`.
- SC-ST-2, SC-ST-3 as written (M-1b) — mechanics land here, the M-1b ACs are verified later.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/WorldLifecycleTests.cs` (`[Trait("Id", "SC-ST-1")]`) — must exist and pass (Debug + Release)

**Status**: [x] Created — `tests/unit/sim/Core/WorldLifecycleTests.cs` (26 test methods, Trait SC-ST-1), green on all 4 legs

---

## Dependencies

- Depends on: Story 011
- Unlocks: Story 014, Story 015, Story 019, Story 024

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (AC-1..AC-4 covered by `WorldLifecycleTests.cs`; 0-B guardrail test runs on Release legs, skipped on Debug by design — TD-008)
**Deviations**: None blocking. First review (LP CONCERNS) rejected `Ready → Stopped` via `Stop()`; revised to refuse it per the GDD States table / AC-1. Amendment candidate (allow Ready → Stopped; GDD L413/L416 "illegal call asserts in Debug" vs silent refusal) logged as TD-017 for an owner ruling. Advisory test/doc gaps logged as TD-018. TD-013 items 1–2 resolved here (`Tick()` split, `finally` resets Stage/Phase; `SimWorldParts`).
**Test Evidence**: Logic: `tests/unit/sim/Core/WorldLifecycleTests.cs`, `tests/unit/sim/Core/SimCountersTests.cs` — 639 tests/leg green on net10.0 + net8.0, Debug (4 skipped) + Release; Release build 0 warnings
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (re-review, 7 non-blocking); QL-TEST-COVERAGE ADEQUATE (re-review, 5 advisory)
**Run result**: N/A — Godot-free Sim lifecycle code; nothing renders or is observable outside the test suite
