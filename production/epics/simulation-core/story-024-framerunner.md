# Story 024: FrameRunner — the per-frame pump

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5 day; ≈ 300 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R2 one `FrameRunner` per process; R17 frame order, input edges, hitch record; R18 transport callbacks only enqueue)
**Requirement**: `TR-sim-003` (Godot-free driver path in `Hosting/`), `TR-sim-008` (inbound drained by `RunFrame` step 1 on the main thread; nothing applied in a callback), `TR-sim-014` (client step and world tick separate, same frame loop)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim (§2, §5) — primary; ADR-0011 (§2 frame order and hook names) — secondary
**ADR Decision Summary**: `FrameRunner` (Godot-free, `Hosting/`) owns the per-frame pump. `RunFrame()` on the main thread: (1) the adapter hook pumps inbound (remote poll, then loopback poll) into `EnqueueOp`/the sample ring; (2) `n = driver.Advance(clock.Timestamp)`; (3) for `i = 0…n−1`: `client.Step(isFirstTickInBatch: i == 0)`, `world?.Tick()`, then the adapter flushes that tick's outbound batch; (4) if the driver's `Hitches` increased this frame, one `TickHitch` record (a = dropped this frame, b = clamped this frame). The M0 wall-clock guard (`catchUpGuardMs`) is 0 at M-1a. Headless hosts skip the runner and call `Tick()` in a loop.
**ADR Version**: 2026-10-16 (ADR-0002, ADR-0011)

**Engine**: Godot 4.7.1 .NET (`FrameRunner` is plain C#) | **Risk**: LOW
**Engine Notes**: The Godot `GameLoop` that calls `RunFrame()` once per `_Process` and `StopwatchMonotonicClock` are the godot-host-shell epic (HIGH-risk half, spike-verified F9–F12).

**Control Manifest Rules (Foundation)**:
- Required: `FrameRunner.RunFrame()` order (1) inbound pump, (2) `Advance(clock.Timestamp)`, (3) per tick `client.Step(i == 0)`, `world?.Tick()`, outbound flush(tick), (4) one `TickHitch` record if hitches increased (names per manifest I-7: ADR-0011's `PumpInbound` / `FlushOutbound(tick)`); `catchUpGuardMs` = 0 at M-1a; owner-thread assert in `RunFrame` under `SIM_ASSERT`; frame-loop ACs drive the runner with an injected `IMonotonicClock`.
- Forbidden: reading a clock anywhere except through the injected `IMonotonicClock`; timers, threads or `_PhysicsProcess` driving the Sim; calling `EnqueueOp`/`Tick`/sample writes from a transport callback.
- Guardrail: `RunFrame` allocates 0 B; ≤ `maxCatchUpTicksPerFrame` ticks per frame.

---

## Acceptance Criteria

*From GDD SC-R2-1 (runner half, with a recording stub transport — the loopback version is the network-adapter-loopback epic's) and ADR-0002 §2, scoped to this story:*

- [x] **AC-1 SC-R2-1 (stub transport).** Given a host `FrameRunner` with a recording stub `IFrameTransport`, a recording `ClientLayer` step, a `SimWorld` with a recording phase, and an injected clock advanced so `Advance` returns 3: one `RunFrame()` records `PumpInbound` first, then Client, Sim, Flush(T), Client, Sim, Flush(T+1), Client, Sim, Flush(T+2); `isFirstTickInBatch` is true only for i = 0; each flush happens after its `Tick()` returned and carries that tick's number, so the last flush runs after the last `Tick()` returns.
- [x] **AC-2 Zero ticks, pure client, hitch.** A frame whose `Advance` returns 0 still pumps inbound and runs no client step, tick or flush; a runner built with `world == null` (pure client) runs client steps and flushes with no tick; a frame in which the driver drops ticks writes exactly one `TickHitch` record with a = ticks dropped this frame and b = time clamped this frame, and a frame with no new hitch writes none.
- [x] **AC-3 Thread and allocation.** `RunFrame()` from a non-owner thread asserts under `SIM_ASSERT`; 1 000 warmed frames of a stub runner allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0002 §2, §5 and ADR-0011 §2:*

- Files in `src/PerformativeMail.Sim/Hosting/`: `IMonotonicClock.cs` (`long Timestamp { get; } long Frequency { get; }` — host-implemented), `IFrameTransport.cs`, `FrameRunnerSettings.cs` (`CatchUpGuardMs` = 0 at M-1a), `FrameRunner.cs`.
- **Hook names — manifest I-7.** ADR-0002 sketches `IFrameTransport { DrainInbound(); FlushAfterTick(uint tick); }` and defers the final names to ADR-0011, whose `HostAdapter` exposes `PumpInbound()` / `FlushOutbound(uint tick)`. Define the Sim-side interface with ADR-0011's names: `public interface IFrameTransport { void PumpInbound(); void FlushOutbound(uint tick); }` — `HostAdapter` (and `ClientAdapter` for a pure client) implement it in Client.Core; the Sim never references Client.Core.
- **Flush cadence.** ADR-0002 §2 and ADR-0011 §2 flush after **each** tick; GDD R2/SC-R2-1 phrase it as "the transport flush runs after the last `Tick()` returns". Per-tick flushing satisfies both (the final flush is after the last tick); follow the ADRs.
- Constructor per ADR-0002 Key Interfaces: `FrameRunner(IMonotonicClock clock, TickDriver driver, ClientLayer client, SimWorld? world, IFrameTransport transport, FrameRunnerSettings settings)`; expose `Driver` (Presentation reads `Driver.AlphaPermille`).
- Tick number for the flush: read `world.CurrentTick` **before** calling `Tick()` (the tick being run); for a pure client use the client tick counter.
- Hitch record: snapshot `driver.Counters` before `Advance`, compare after; write to the world's `SimCheckRing` (`CheckCode.TickHitch`, tick = world's current tick). No world → skip the record (a client-side ring is not specified).
- The M0 guard (`DropRemaining` after `catchUpGuardMs` with ≥ 1 tick always run, SC-F2-3) is **not** built: the setting exists with value 0 and no code path reads the clock mid-frame. Leave a comment pointing to ADR-0002 §2 step 3.
- Tests use a fake `IMonotonicClock` with integer timestamps (frequency 10^7) and the real `TickDriver` (Story 010).

---

## Out of Scope

- network-adapter-loopback: `HostAdapter`/`ClientAdapter` implementing `IFrameTransport`, SC-R2-1 over the real `LoopbackTransport`, remote-before-loopback `opSeq` ordering (SC-X-3, M0).
- godot-host-shell: `GameLoop`, `StopwatchMonotonicClock`, `Present(AlphaPermille)`.
- M0 guard (SC-F2-3).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Hosting/FrameRunnerTests.cs` (`[Trait("Id", "SC-R2-1")]`) — must exist and pass

**Status**: [x] Created and passing — FrameRunnerTests.cs (16 tests, 4 tagged SC-R2-1, NonParallel 0 B over 1 000 frames)

---

## Dependencies

- Depends on: Story 007 (check ring), Story 010 (`TickDriver`), Story 012 (owner thread, world states), Story 023 (`ClientLayer`)
- Unlocks: network-adapter-loopback (adapter implements `IFrameTransport`), godot-host-shell (`GameLoop`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (none deferred)
**Deviations**: None blocking. Advisory (logged TD-057): flush can repeat a tick number when the world cannot tick; hitch record tick = `CurrentTick` after the batch; pure client: owner = constructing thread, flush tick = client tick, no hitch record; negative `CatchUpGuardMs` throws. M0 guard not built (comment → ADR-0002 §2 step 3, TD-012(1) retargeted to the M0 guard story). Extra file: `Core/SimWorld.cs` (+ `internal int OwnerThreadId`). Manifest stamped 2026-10-16.2.
**Test Evidence**: Logic — tests/unit/sim/Hosting/FrameRunnerTests.cs; FrameRunner 16/16 on all four legs; full suite 2 180/leg on the real tree with 33 failures, all `Unit.Content.RuleRegistryTests` from content-data's in-flight `ContentValidationRunner` edit (unrelated; 0 non-Content failures)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE, QL-TEST-COVERAGE ADEQUATE
