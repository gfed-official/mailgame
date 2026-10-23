# Story 010: TickDriver — the F2 integer accumulator

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 400 lines incl. property test)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R16 pause as a driver state, R17 tick driver; F2 catch-up accumulator; States: driver `Idle → Running ⇄ Paused`)
**Requirement**: `TR-sim-003` (Godot-free integer accumulator in `Hosting/`; takes timestamps, never reads a clock), `TR-sim-004` (cap 4, excess dropped, never banked), `TR-sim-006` (F2 invariant; Δ clamped to 1 s; freq 10^3..10^12; backwards clock counted), `TR-sim-009` (pause is a driver state; `Advance` returns 0 — interface + minimal behaviour)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim (§1, §4)
**ADR Decision Summary**: `TickDriver` is a Godot-free integer accumulator in `Hosting/` that never reads a clock: `new TickDriver(long frequency, int maxCatchUpTicksPerFrame)` (frequency 10^3..10^12 else `ArgumentOutOfRangeException`), and `Advance(long now)` implements GDD F2's two-step form exactly — backwards Δ counted and treated as 0, Δ clamped to 1 s, `n = min(due, cap)`, excess dropped and counted (never banked). `Start`/`Resume`/`Resync` set `last = now; acc30 = 0`; `DropRemaining(m)` moves m from run to dropped; `AlphaPermille = acc30 × 1000 / freq` is Presentation-only and frozen at 0 while paused.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (`TickDriver` is plain C#) | **Risk**: LOW
**Engine Notes**: ADR-0002's HIGH risk is the `GameLoop` half (godot-host-shell epic). Windows `Stopwatch.Frequency` (expected 10^7) is a host fact; any value in 10^3..10^12 is accepted here.

**Control Manifest Rules (Foundation)**:
- Required: `TickDriver` in `PerformativeMail.Sim/Hosting/`, Godot-free, never reads a clock; `Advance(long now)` implements the F2 two-step pseudocode (ADR-0002 §1) exactly; `Start`/`Resume`/`Resync` set `last = now; acc30 = 0`; `Resync` legal only in `Running`, writes no hitch; `AlphaPermille` 0–999, frozen at 0 while paused, Presentation-only; tests use integer timestamps and include the F2 invariant property test and the GDD F2 worked examples.
- Forbidden: banking excess ticks; any clock read in `Hosting/`; a `float`/`double` `Alpha`; Sim code reading `AlphaPermille`.
- Guardrail: O(1) per frame, ≤ `maxCatchUpTicksPerFrame` ticks per frame, 0 B per frame.

---

## Acceptance Criteria

*From GDD SC-F2-1 and ADR-0002 Implementation Guidelines, scoped to this story:*

- [x] **AC-1 SC-F2-1 (freq = 10^7, cap 4).** Each row holds: 150 ms from 0 → run 4, dropped 0, `acc30` 5 000 000, Alpha 500 ‰; the same 150 ms again → run 4, dropped 1, `acc30` 0, Alpha 0; 400 ms from 0 → run 4, dropped 8, `Hitches` +1; 5 s → run 4, dropped 26, `ClampedTimeTotal` grows by 4 s (4 × 10^7); a backwards timestamp → returns 0, adds nothing, `last` unchanged, `BackwardsClock` +1; 600 frames of 166 667 counts → 0, 1, 0, 1 …, dropped 0, Alpha exactly 500, 0, …; constructing with freq 999 or 10^12 + 1 throws `ArgumentOutOfRangeException` while 10^3 and 10^12 succeed.
- [x] **AC-2 F2 invariant property test.** Over a seeded random sequence of integer Δ (including negatives, 0, values above `freq`, and `DropRemaining` calls), after every call `0 ≤ acc30 < freq`, `n ≤ cap`, and `(TicksRunTotal + DroppedTicksTotal) × freq + acc30 == Σ min(max(Δ, 0), freq) × 30` since the last `Start`/`Resume`/`Resync`, exactly.
- [x] **AC-3 Driver states.** `Advance` while `Idle` (before `Start`, after `Stop`) returns 0; while `Paused` it returns 0, accumulates nothing and `AlphaPermille` reads 0; `Resume(t0)` clears the accumulator (`Advance(t0 + tick − 1)` → 0, `Advance(t0 + tick)` → 1, no burst); double `Pause`/`Resume` are no-ops; `Resync(now)` in `Running` clears `acc30` and changes no counter; `Resync` outside `Running` asserts.

---

## Implementation Notes

*Derived from ADR-0002 §1, §4 and Key Interfaces:*

- `src/PerformativeMail.Sim/Hosting/TickDriver.cs`, `TickDriverState.cs` (`Idle, Running, Paused`), `TickDriverCounters.cs` (readonly struct: `TicksRunTotal`, `DroppedTicksTotal`, `ClampedTimeTotal`, `BackwardsClock`, `Hitches`).
- `Advance(long now)` — implement this pseudocode literally (ADR-0002 §1):
  ```
  Δ = now − last
  if Δ < 0 : backwardsClock++ ; Δ = 0          // last is not moved back
  if Δ > freq : clampedTimeTotal += Δ − freq ; Δ = freq
  last = max(last, now)
  acc30 += Δ × 30
  due = acc30 / freq
  n = min(due, maxCatchUp)
  acc30 −= n × freq
  if due > n : droppedTicksTotal += due − n ; acc30 %= freq ; hitches++
  ticksRunTotal += n
  return n
  ```
  `Δ × 30` ≤ 3 × 10^13 fits `long`. Returns 0 (and touches nothing) unless `Running`.
- `DropRemaining(int m)`: `ticksRunTotal −= m; droppedTicksTotal += m` (`acc30` unchanged). Used by Story 024's M0 guard; implement and unit-test now (it is in the invariant).
- `AlphaPermille => State == Paused ? 0 : (int)(acc30 * 1000 / freq)` (0–999).
- `maxCatchUpTicksPerFrame` comes from `SimSettings` (default 4, safe 3–8) — the driver takes it as a constructor `int`; validate ≥ 1 here; the 3–8 safe range is a `SimSettings` validation concern.
- Pause policy (only when no remote peers; Networking calls `Resume` before `PlayerJoin`) is the network/run-structure layer's — this story builds only the state.
- The GDD's "one hitch record" is written by `FrameRunner` (Story 024) when `Hitches` increases; the driver itself holds no `SimCheckRing`.
- Tests: `tests/unit/sim/Hosting/TickDriverTests.cs` — one Theory per SC-F2-1 row, plus `TickDriverInvariantTests` (seeded `Pcg32` from Story 008 or a local LCG for Δ generation — no `System.Random`; tests are not under the analyzer but stay deterministic).

---

## Out of Scope

- Story 006: `TickRate.Hz`, F1 helpers.
- Story 024: `FrameRunner` (calls `Advance`, writes the `TickHitch` record, runs ticks).
- SC-F2-2 (M-1b, 1 000 frames of 125 ms) and SC-R16-1 (M-1b state table, SC-R16-2 pause integration) — later milestone; AC-2/AC-3 cover their mechanics.
- SC-F2-3 / `catchUpGuardMs` (M0).
- `StopwatchMonotonicClock` / `GameLoop` — godot-host-shell.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Hosting/TickDriverTests.cs` (`[Trait("Id", "SC-F2-1")]`) and the invariant property test — must exist and pass

**Status**: [x] Created — tests/unit/sim/Hosting/TickDriverTests.cs, tests/unit/sim/Hosting/TickDriverInvariantTests.cs

---

## Dependencies

- Depends on: Story 001; Story 006 (`TickRate.Hz`)
- Unlocks: Story 024 (`FrameRunner`), godot-host-shell (`GameLoop`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 every SC-F2-1 row asserted verbatim incl. ctor bounds 999 / 10^12+1; AC-2 F2 invariant over 7 seeded Pcg32 runs × 20 000 ops incl. negatives, >freq, DropRemaining, Pause/Resume/Resync; AC-3 Idle/Paused return 0, Resume no-burst, idempotent Pause/Resume, Resync in Running clears acc30, outside Running asserts (`SimAssertCode.TickDriverResyncState = 17`) or is ignored)
**Deviations**: None blocking. Decisions: `DropRemaining` asserts outside 0..TicksRunTotal (`TickDriverDropRange = 18`), clamps otherwise; `Start` legal from any state, keeps counters; `Stop` clears acc30; `Last`/`Acc30` internal. Forward-looking items logged as TD-012 (per-frame DropRemaining check for story 024, focus-regain Resync vs Paused for GameLoop/ADR-0002, trait IDs, invariant lacks Start/Stop, counters doc nit).
**Test Evidence**: Logic — TickDriverTests.cs (29 methods / 44 cases), TickDriverInvariantTests.cs (2 methods / 6 cases); suite 587/leg: Release 587/587, Debug 585 + 2 skipped (0-B tests, TD-008), net10.0 and net8.0.
**Run result**: N/A — Godot-free integer accumulator; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (6 non-blocking; .NET idiom review folded in); QL-TEST-COVERAGE ADEQUATE.
