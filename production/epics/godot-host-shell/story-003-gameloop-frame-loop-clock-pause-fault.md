# Story 003: `GameLoop` frame loop — `_Process` → `FrameRunner.RunFrame()` with a `Stopwatch` clock, the hook points, driver-state pause and fault stop

> **Epic**: Godot Host Shell & Greybox Scene
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines prod + smoke scenarios)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (R2 one `FrameRunner` per process; R16 pause is a driver state, `SceneTree.Paused` never used; R17 tick driver and frame runner, the `GameLoop` node, `ProcessMode = Always`, `delta` unused; R18 main-thread Sim)
**Requirement**: `TR-sim-007` (Sim on the Godot main thread; Godot-side thread check), `TR-sim-008` (the shell half: no callback path into the Sim — only `GameLoop` calls `RunFrame`), `TR-sim-009` (pause is a driver state; `SceneTree.Paused` never used), `TR-movement-007` (fixed-step driver with catch-up cap, presentation called with `AlphaPermille`, no engine physics interpolation), `TR-input-014` (a UI/MP pause overlay or `SceneTree.Paused` never stops the Sim)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. The pre-frame/post-present hook points have **no TR-ID in the registry**; they come from ADR-0012 §4/§6 and ADR-0016 §6.)*

**ADR Governing Implementation**: ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim (§3 Godot side: one `GameLoop` node, §4 pause, §5 thread; Implementation Guidelines) — Accepted 2026-10-16 (primary); ADR-0012 (§4/§6: `DispatchImmediates` then `FlushToLatch` immediately before `RunFrame`), ADR-0016 (§6: `RenderClock` sampled once per frame after `RunFrame`, before `Present`), ADR-0019 (§1 root placement, §2 `PresentationRoot.Present` order World → Hud → Audio) — Accepted 2026-10-16 (secondary)
**ADR Decision Summary**: `GameLoop._Process(double)` ignores `delta`. Each frame it runs the pre-frame hooks, calls `FrameRunner.RunFrame()` exactly once, samples the render clock, calls `IPresentationRoot.Present(driver.AlphaPermille)`, then the post-present hooks. The time source is `StopwatchMonotonicClock : IMonotonicClock` (`Stopwatch.GetTimestamp()`/`Frequency`). `TickDriver.Start(now)` happens on the first `_Process` after boot completes, not in `_Ready`. Pause is `TickDriver.Pause()`/`Resume(now)` only (solo, no remote peers); `SceneTree.Paused` is never used and `ProcessMode = Always` keeps the loop alive anyway. `SimBuildException` and any `RunFrame` exception end the session and stop calling `RunFrame` — Godot would otherwise log and continue every frame.
**ADR Version**: ADR-0002 2026-10-16; ADR-0012 2026-10-16; ADR-0016 2026-10-16; ADR-0019 2026-10-16

**Engine**: Godot 4.7.1 .NET | **Risk**: HIGH
**Engine Notes**: Spike-verified (ADR-0002): F9 `_Process` runs on managed thread 1; F10 the integer accumulator fed by `Stopwatch` gives exactly 30 Hz at 145/60/20 fps; F11 `_Process(delta)` under-reports wall time by ~32 % under slow frames — `delta` is never used; F12 headless runs uncapped (~145 fps) with a ~40 ms first-frame hitch (hence `Start` on the first `_Process`, after boot); F15 `ProcessMode = Always` keeps `_Process` alive while `SceneTree.Paused` is true. UNVERIFIED (ADR-0002 Verification Required): frame pacing on a 120/144 Hz vsync display; Windows `Stopwatch.Frequency` (any value in 10^3..10^12 accepted). Unhandled exceptions in Godot callbacks are logged and the engine keeps running (UNVERIFIED for 4.7) — this story catches explicitly.

**Control Manifest Rules (Foundation)**:
- Required: exactly one `GameLoop : Node`; `_Process` calls `RunFrame()` then `Present(Driver.AlphaPermille)`; `StopwatchMonotonicClock` in `src/Game/Hosting/`; `TickDriver.Start(now)` on the first `_Process` after boot; the driver is the only pause mechanism (ADR-0002 §3–§4)
- Required: the host catches `SimBuildException` at boot and any exception from `RunFrame`, shows the error, ends the session and stops calling `RunFrame` (ADR-0002 Implementation Guidelines); a `Faulted` world in a non-`SIM_ASSERT` build does **not** rethrow (simulation-core 012), so the loop also checks the world state
- Forbidden: `delta` in `GameLoop`; `_PhysicsProcess`, Godot `Timer` nodes, `System.Threading` timers or background threads driving or calling the Sim; any node other than `GameLoop` calling `SimWorld`/`ClientLayer`/`FrameRunner`; `SceneTree.Paused` to pause the Sim (ADR-0002)
- Guardrail: `_Process` adds no per-frame allocation of its own (preallocated hook arrays, no LINQ/closures per frame)

---

## Acceptance Criteria

*From GDD R2/R16/R17/R18 and ADR-0002 §3–§5, scoped to this story. Delta from the repo today: story 002 left `GameLoop` as a skeleton with no `_Process`; `FrameRunner`/`TickDriver` exist in the Sim (simulation-core 010/024) but nothing calls them.*

- [ ] **Frame order, once per frame.** Per `_Process`, in this order and exactly once each: (1) the pre-frame hooks, in registration order; (2) `FrameRunner.RunFrame()`; (3) the render-clock sample; (4) `IPresentationRoot.Present(driver.AlphaPermille)`; (5) the post-present hooks, in registration order. A smoke scenario with recording doubles (a recording run-frame, recording hooks, a recording presentation root) proves the order over a frame that runs 0 ticks and over a frame that runs 3 (catch-up) ticks, and that `Present` receives the driver's `AlphaPermille` (0–999). `delta` is never read (code review + a grep guard on the `_Process` body). The only call site of `RunFrame()` under `src/Game/` is `GameLoop`'s frame sequence: new `tools/ci/check-sim-access.sh` (with `--self-check`) fails on `FrameRunner`/`SimWorld`/`ClientLayer`/`.RunFrame(` references under `src/Game/` outside `Hosting/` and `Boot/` (TR-sim-008 shell half).
- [ ] **Hook points exist for the feature epics.** `IPreFrameHook { void OnPreFrame(); }` and `IPostPresentHook { void OnPostPresent(); }` are registered through explicit code in `GameLoop` (no reflection discovery) before the first frame; a hook that throws is a fault (below). Input-controls 028 registers `DispatchImmediates()` then `FlushToLatch(latch)` as pre-frame hooks and the `PresentedPromptLatch` update as a post-present hook; this story registers none of them and the smoke proves an empty hook list is fine.
- [ ] **Clock and start.** `StopwatchMonotonicClock : IMonotonicClock` reads `Stopwatch.GetTimestamp()` and `Stopwatch.Frequency` (a unit test of the class is trivial; the boot log prints `Frequency` — story 004). `TickDriver.Start(clock.Timestamp)` is called on the first `_Process` after boot completes (a `StartPending` flag armed by boot step 9), not in `_Ready`; frames before that run no ticks. `GameLoop` has `ProcessMode = Always` (set defensively, F15) and the lowest `ProcessPriority`; `_PhysicsProcess` is not overridden.
- [ ] **Pause is a driver state (TR-sim-009, TR-input-014).** `GameLoop` exposes `TryPause(out string reason)` and `Resume()`: pause calls `TickDriver.Pause()` and is refused with a logged reason while the session reports any remote peer (`IPeerPresence.AnyRemotePeer`; the M-1a loopback session reports false), resume calls `Resume(clock.Timestamp)`. A smoke scenario with a real `TickDriver` and a fake clock shows: while paused `Advance` returns 0 and `AlphaPermille` is frozen at 0 and the hooks and `Present` still run; the first frame after resume runs ≤ 1 tick (no burst); setting `GetTree().Paused = true` from the scenario neither stops `_Process` nor changes the tick count. `tools/ci/check-no-scenetree-paused.sh` (and its `--self-check`) is extended to scan `src/Game/` as well as the Sim and still passes.
- [ ] **Thread and fault stop.** The thread running boot is recorded; `_Process` asserts `System.Environment.CurrentManagedThreadId` equals it (a mismatch is a fault). An exception thrown by a hook, `RunFrame`, the render-clock sample or `Present`, **or** a world that is `Faulted` after `RunFrame` (non-`SIM_ASSERT` builds do not rethrow), logs the exception with its type and stack, marks the loop `Faulted`, hands the message to an `IFaultSink` (story 004 supplies the error screen) and from then on never calls `RunFrame` again (a smoke scenario counts calls across 10 frames after an injected throw: 0). `SimBuildException` is caught at boot with its `Code` reported (story 004).
- [ ] **No steady-state allocation of its own.** Over 600 frames of the recording doubles, `GC.GetAllocatedBytesForCurrentThread()` deltas attributable to the frame sequence are 0 B after warm-up (measured and logged; advisory — Godot's own per-frame marshalling is excluded by measuring the managed sequence method, not `_Process` itself).

---

## Implementation Notes

*Derived from ADR-0002 §3–§5, ADR-0012 §6, ADR-0016 §6:*

- Files in `src/Game/Hosting/`: `GameLoop.cs` (extends story 002's skeleton), `StopwatchMonotonicClock.cs`, `FrameSequence.cs` (plain C#; takes `Action runFrame`-style `IFrameRun`, `IRenderClockSampler`, `IPresentationRoot`, the hook arrays; this is the unit that the smoke scenarios drive with doubles), `IPresentationRoot.cs`, `PresentationRoot.cs` (plain C#; holds `IPresenter[] { World, Hud, Audio }` and calls them in that order — presenter behaviour is story 006), `IPreFrameHook.cs`, `IPostPresentHook.cs`, `IFaultSink.cs`, `IPeerPresence.cs`.
- `IPresentationRoot` lives in the Godot project, not the Sim (simulation-core 024 leaves `Present(AlphaPermille)` to this epic).
- Render-clock sample (ADR-0016 §6): `RenderClock` is a Client.Core class (`PerformativeMail.Client.Core.Timing`, `IRenderClock { HostTick, AlphaPermille, RenderSimTimeMs, IsValid }`). `production/epics/throwing-catching/story-005-preview-label-model-f7-money-and-settle.md` adds it "if absent"; if it is still absent when this story runs, add it exactly as ADR-0016 §6 shows (with its integer `RenderSimTimeMs = (HostTick*1000 + AlphaPermille)/30` test) and record that in Last Updated. This story only calls `Sample()`; wiring its inputs (host clock, driver) is story 005.
- The fault path must not depend on the error screen existing yet: `IFaultSink` has a log-only default; story 004 replaces it.
- Do not poll `Input.*` here (lint, input-controls 025). Do not add a `delta` parameter name that is used; name it `_` or `delta` and leave it unreferenced, with the F11 comment.
- Solo pause callers: input-controls 016/018/028 raise `PauseRequested(solo: true)`; 028 maps it to `TryPause`/`Resume` — this story owns the two methods and their refusal rule only.
- Headless runs are uncapped (~145 fps): any smoke scenario that depends on tick counts uses the fake clock, not wall time.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: boot sequence, error screen, window guard. Story 005: constructing `FrameRunner`/`TickDriver`/`HostSession` and the render clock's inputs. Story 006: presenters. Story 011: the real 300-tick run.
- Input-controls 028: registering the router hooks, pause/resume wiring from `PauseRequested`/`ResumeRequested`, `ReplyObserved`, `SetLook`, sink registration.
- M0: the wall-clock `catchUpGuardMs` (simulation-core 024 keeps it 0), remote-peer pause refusal beyond the `IPeerPresence` seam, the multiplayer pause overlay.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: smoke scenarios `frame-order`, `pause-driver-state` and `fault-stop` (`SMOKE … PASS`, exit 0) recorded in `production/qa/evidence/host-shell-gameloop-evidence.md`, plus `tools/ci/check-sim-access.sh` / `check-no-scenetree-paused.sh` runs with `--self-check` — `/story-done` checks the scenarios, scripts and evidence file exist; pass/fail via `/smoke-check`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (`GameLoop` skeleton, smoke harness); `production/epics/simulation-core/story-010-tickdriver-f2-accumulator.md`, `production/epics/simulation-core/story-024-framerunner.md` (`IMonotonicClock`, `FrameRunner`), `production/epics/simulation-core/story-012-world-lifecycle-guards-faults.md` (`Faulted` state)
- Unlocks: Stories 004, 005, 011; `production/epics/input-controls/story-028-gameloop-hookup-router-boot-settings-file.md` (hook points, pause API), every presentation story (the `Present` call)
