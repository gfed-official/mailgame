# ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist`. TD-ADR skipped (lean mode). Evidence: `prototypes/spike-godot-dotnet/REPORT.md` (OQ1 tick-loop spike, 2026-10-16). Inputs: `design/gdd/simulation-core.md` R1, R2, R15–R18, F1, F2, States (Provisional); `docs/architecture/simulation-core-adr-inputs.md` §1 (F2 two-step driver, thread, judder), §3.3, §3.4, §3.10 (unreviewed seed, re-checked against the GDD); `docs/architecture/architecture.md` §Sim.Hosting and §Data Flow 1.

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
The Sim must advance in whole 1/30 s ticks, driven by a variable-rate Godot frame, without drift, banked bursts or a dependency on Godot's frame timing. This ADR fixes a Godot-free integer accumulator, `TickDriver` (cap 4 ticks/frame, drop-not-bank, Δ clamp 1 s, solo pause as a driver state). A Godot-free `FrameRunner` owns the per-frame pump. One Godot `GameLoop` node's `_Process` calls `FrameRunner.RunFrame()` with a `Stopwatch` timestamp, and the whole Sim runs on the Godot main thread. The spike verified that `_Process` is on the main thread, that the accumulator gives exactly 30 Hz at 145/60/20 fps, and that `_Process` delta is unusable as a time source.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET (`4.7.1.stable.mono.official.a13da4feb`), C# |
| **Domain** | Core (frame loop, threading) |
| **Knowledge Risk** | HIGH for the `GameLoop` half (`Node._Process`, `ProcessMode` are post-cutoff-unverified in `docs/engine-reference/godot/`). It is reduced by the spike (VERIFIED below). LOW for `TickDriver`/`FrameRunner` (plain C#). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (4.5: `RenderingServer.instance_set_interpolated` / `instance_reset_physics_interpolation` removed; 4.6: Jolt default), `deprecated-apis.md`, `current-best-practices.md`; `prototypes/spike-godot-dotnet/REPORT.md` |
| **Post-Cutoff APIs Used** | None new. It uses 4.0-era APIs that the reference lists as unchanged and the spike exercised on 4.7.1: `Node._Process(double)`, `Node.ProcessMode = ProcessModeEnum.Always`, `SceneTree.Paused` (F9, F10, F15). `Engine.MaxFps` and `OS.GetCmdlineUserArgs()` were used by the spike only. |
| **Verification Required** | (1) Which thread Godot multiplayer callbacks arrive on: the ADR-0011 transport spike (M0). The rule here ("callbacks only enqueue") is safe either way. (2) `_Input` vs `_Process` order within a frame: the ADR-0012 input spike (OQ-IC-9). (3) Frame pacing on a 120/144 Hz display with vsync (judder; the spike was headless). (4) Windows `Stopwatch.Frequency` (expected 10^7; any value in 10^3..10^12 is accepted). |

**Spike-verified facts** (REPORT §4–§5):
- **F9** `_Process` runs on managed thread 1 every frame.
- **F10** The integer accumulator fed by `Stopwatch.GetTimestamp()` (Linux `Frequency` = 10^9, high-res) runs 150 ticks in 5.00 s at 145, 60 and 20 fps. At 5 fps the cap and drop rule engage: 104 run, 51 dropped, max 4 per frame.
- **F11** Under slow frames, `_Process(delta)` **under-reports** wall time: 125.7 ms average vs 185.6 ms measured.
- **F12** Headless runs uncapped (~145 fps) with a ~40 ms first-frame hitch.
- **F15** `ProcessMode = Always` keeps `_Process` running while `SceneTree.Paused` is true.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (the `Hosting/` folder in the Godot-free Sim; the `GameLoop` lives in the Godot game project); ADR-0003 (`SimWorld.Tick()`, the lifecycle and `Freeze()` that captures the owner thread, `ClientLayer.Step()`) |
| **Enables** | ADR-0011 (adapter seam: inbound drain before ticks, flush after each tick, thread rule), ADR-0012 (first-tick input-edge flag), ADR-0016 (ClientLayer timing, `AlphaPermille`), ADR-0019 (`GameLoop` placement: autoload vs root), ADR-0020 (transform sampling cadence relative to ticks) |
| **Blocks** | The Sim Core "TickDriver + FrameRunner" story; the first Godot greybox story (`GameLoop` node) |
| **Ordering Note** | The M0 wall-clock guard (`catchUpGuardMs`) is specified now and built at M0 (Sim Core milestone slice). Moving the Sim off the main thread is out of scope; R18's triggers would supersede this ADR. |

## Context

### Problem Statement
Godot calls `_Process` at a variable rate (uncapped headless ~145 fps; vsync-bound on a display; single digits under hitches). The Sim is a fixed 30 Hz integer simulation (R1, spec 07 §1.4).

Something has to turn wall time into whole ticks. It must not create time, burst after hitches, or tie the Sim to Godot's pause, physics or delta handling. It must also decide which thread the Sim lives on.

Grid Inventory OQ20 and Sim Core OQ1 left the Godot side to a spike. The spike now exists.

### Constraints
- Integer-only Sim, with no clock reads inside the Sim (TR-sim-003, TR-sim-032 / ADR-0006).
- Input & Controls H-EC-4 needs ≥ 3 catch-up ticks; edges attach to the first tick of a frame.
- Multiplayer never pauses (spec 06, R16). The MP pause menu is a local overlay (TR-input-014).
- Godot multiplayer callback threading is NOT SOURCEABLE for 4.7.1 (VERSION.md).
- Seed corrections (§1): the two-step F2 (`Advance` then `DropRemaining`); `FrameRunner` owns pump order, edge flag, guard and transport flush; "MPSC, nothing else changes" is deleted; the 120/144 Hz judder risk goes into the spike and the R18 triggers.

### Requirements
TR-sim-001..009, TR-input-014, TR-movement-007 (see GDD Requirements Addressed).

## Decision

### 1. `TickDriver` (Sim, `Hosting/`, Godot-free, never reads a clock)

- **Construction:** `new TickDriver(long frequency, int maxCatchUpTicksPerFrame)`.
  - `frequency` is the timestamp unit per second, given once by the host. It must be in 10^3..10^12, else `ArgumentOutOfRangeException` at construction (outside any tick).
  - `maxCatchUpTicksPerFrame` comes from `SimSettings` (default 4, safe 3–8).
  - `TickRateHz = 30` is a `const` in `Hosting/TickRate.cs`, never data.
- **State machine** (GDD States): `Idle → Running ⇄ Paused`, `→ Idle` via `Stop()`. `Advance` while `Idle` returns 0. Double `Pause`/`Resume` are idempotent no-ops.
- **`Advance(long now)` — GDD F2, two-step form:**
  ```
  Δ = now − last
  if Δ < 0 : backwardsClock++ ; Δ = 0          // last is not moved back
  if Δ > freq : clampedTimeTotal += Δ − freq ; Δ = freq
  last = max(last, now)
  acc30 += Δ × 30
  due = acc30 / freq
  n = min(due, maxCatchUp)
  acc30 −= n × freq
  if due > n : droppedTicksTotal += due − n ; acc30 %= freq ; hitches++   // drop, never bank
  ticksRunTotal += n            // provisional; corrected by DropRemaining
  return n
  ```
  While `Paused`, it returns 0 and accumulates nothing.
- **`DropRemaining(int m)`**: `ticksRunTotal −= m; droppedTicksTotal += m` (`acc30` unchanged). It is called by `FrameRunner` when the guard stops early.
- **`Start(now)`, `Resume(now)`, `Resync(now)`** each set `last = now; acc30 = 0`.
  - `Resync` is legal only in `Running`. It is called after known blocking work (world generation, scene load, focus regain) and writes no hitch.
- **Invariant (TR-sim-006, F2):** `(ticksRunTotal + droppedTicksTotal) × freq + acc30 == Σ min(max(Δ,0), freq) × 30`, summed since the last `Start`/`Resume`/`Resync`. A property test checks it over random integer Δ sequences.
- **`AlphaPermille`** = `acc30 × 1000 / freq` (0–999). It is frozen at 0 while paused.
  - It is Presentation-only, and no Sim code reads it.
  - The Sim exposes no `float`/`double` `Alpha` (ADR-0006); Godot converts with `AlphaPermille / 1000f`.
- **Counters** (`TickDriverCounters`, a readonly struct): `TicksRunTotal`, `DroppedTicksTotal`, `ClampedTimeTotal`, `BackwardsClock`, `Hitches`.

### 2. `FrameRunner` (Sim, `Hosting/`, Godot-free)

- **Construction:** `FrameRunner(IMonotonicClock clock, TickDriver driver, ClientLayer client, SimWorld? world, IFrameTransport transport, FrameRunnerSettings s)`.
  - `world` is null on a pure client (M0).
  - `IFrameTransport` is the adapter hook. Its final name and members are ADR-0011's; the call points below are this ADR's.
- **`RunFrame()`** (main thread only; one call per Godot frame):
  1. `transport.DrainInbound()`: ADR-0011's frame order (remote `transport.Poll`, then `loopback.Poll`; remote ops get the lower `opSeq`). The adapter moves everything its callbacks queued since the last frame into `SimWorld.EnqueueOp` / the sample ring. This is the only place inbound bytes reach the Sim.
  2. `n = driver.Advance(clock.Timestamp)`.
  3. For `i = 0 … n−1`:
     - `client.Step(isFirstTickInBatch: i == 0)`;
     - `world?.Tick()`;
     - `transport.FlushAfterTick(tick)`, which sends the batch the adapter's `IEventSink` received in phase 13.
     - **Guard (M0):** if `catchUpGuardMs > 0` and the clock shows `≥ catchUpGuardMs` since step 3 began, and `i + 1 < n`, then `driver.DropRemaining(n − i − 1)` and stop. At least one tick always runs.
  4. If `driver.Counters.Hitches` increased this frame, write one record to the world's `SimCheckRing` (ADR-0001 §5, code `TickHitch`, a = dropped this frame, b = clamped this frame). The ring rate-limits it.
- **Input edges:** latched edges attach to the tick with `isFirstTickInBatch == true`. Later catch-up ticks carry only held bits and the latest `moveIntent`. A frame that runs 0 ticks keeps its edges latched, in order. A dropped tick never discards an edge (router side: ADR-0012).
- `M-1a`: `catchUpGuardMs = 0` (guard off). `M0`: 10 ms, constrained to `catchUpGuardMs ≥ 2 × tickBudgetP99(host)` (F2).

### 3. Godot side: one `GameLoop` node

```csharp
// src/Game/Hosting/GameLoop.cs (Godot project)
public partial class GameLoop : Node
{
    private FrameRunner _runner = null!;
    private IPresentationRoot _presentation = null!;
    public override void _Ready()  { ProcessMode = ProcessModeEnum.Always; /* boot composes runner (ADR-0019 order) */ }
    public override void _Process(double delta)   // delta deliberately unused (F11)
    {
        _runner.RunFrame();
        _presentation.Present(_runner.Driver.AlphaPermille);
    }
}
```
- **Exactly one** `GameLoop` per process. Whether it is an autoload or the root scene node is ADR-0019's.
- **No other node's `_Process`/`_PhysicsProcess` may call into `SimWorld`, `ClientLayer` or `FrameRunner`.** Presentation is called *explicitly* after `RunFrame` (`IPresentationRoot.Present`), so the result never depends on process-priority order. Views may animate in their own `_Process` from data `Present` handed them.
- **Driver start:** `TickDriver.Start(now)` is called on the first `_Process` after boot and world load complete, not in `_Ready`. That keeps load time and the first-frame hitch (F12) out of the accumulator.
- **View ordering:** `GameLoop` gets the lowest `ProcessPriority` in the tree (`int.MinValue`), so views that animate in their own `_Process` read this frame's `Present` data, not last frame's. Views should animate from `AlphaPermille` rather than their own `delta`: `delta` is smoothed (`application/run/delta_smoothing`, the likely cause of F11; UNVERIFIED).
- **Project settings:** `application/run/low_processor_mode` must stay off, because it runs `_Process` only on redraw and game time would slip. `GameLoop` and its ancestors must use the main-thread (or inherited) `ProcessThreadGroup`; a sub-thread group would move `_Process` off the main thread and trip the thread assert. Both settings are 4.x features whose 4.7.1 behaviour is UNVERIFIED.
- **Time source:** `StopwatchMonotonicClock : IMonotonicClock` (`Stopwatch.GetTimestamp()`, `Stopwatch.Frequency`) in `src/Game/Hosting/`. Tools and BotClient use their own. The `_Process` `delta` is never used (F11: it under-reports wall time under slow frames).
- `ProcessMode = Always` is set defensively (F15 VERIFIED). `SceneTree.Paused` is **never** used to pause the Sim, and the Sim is unaffected if UI code pauses the tree.
- **Remote/own-carrier smoothing (TR-movement-007):** Presentation interpolates or extrapolates ≤ 1 tick using `AlphaPermille`. Godot physics interpolation is not used, because nothing moves in `_PhysicsProcess`. `physics/common/physics_interpolation` must stay false (whether the setting still exists in 4.7.1 is UNVERIFIED; 4.5 removed two `RenderingServer` interpolation methods).

### 4. Pause (R16, TR-sim-009, TR-input-014)
- Pause is a `TickDriver` state, never a `SimWorld` state and never `SceneTree.Paused`.
- **Solo pause:** `Pause()` makes `Advance` return 0. `Resume(now)` clears the accumulator: no burst. Ops enqueued while paused apply on the first tick after resume.
- **Multiplayer never pauses.** Networking & Session owns the "no remote peers" check. It refuses `Pause()` while any peer is in any handshake state, and calls `Resume()` at the first connect callback, **before** enqueueing `PlayerJoin`. The MP pause menu is a local overlay with a zeroed input frame. The Sim keeps ticking.

### 5. Thread (R18, TR-sim-007, TR-sim-008)
- **v1 runs the whole Sim on the Godot main thread** (F9).
- `SimWorld` captures its owner thread at `Freeze()` (ADR-0003). `SIM_ASSERT` builds assert it in `Tick()`, `EnqueueOp()` and the sample-ring drain. `FrameRunner.RunFrame` asserts the same thread.
- **Transport callbacks only enqueue** into the adapter's own inbound queue. Nothing reaches the Sim inside a callback. Step 1 of `RunFrame` drains that queue on the main thread, so `opSeq` and sample `receivedTick` stamping stay main-thread operations whichever thread the callbacks come from.
  - **Recommended to ADR-0011:** set `SceneTree.MultiplayerPoll = false` and poll the multiplayer API explicitly inside `IFrameTransport.DrainInbound()`. That makes packet arrival a fixed point in the frame and keeps callbacks on the main thread. (From memory, `SceneMultiplayer` otherwise polls inside the SceneTree process step: UNVERIFIED.)
  - If ADR-0011's spike finds off-main-thread callbacks, only the adapter's inbound queue becomes thread-safe (MPSC). The Sim API and R5/R6/R7 stamping are unchanged, because stamping happens at the drain, not at the callback.
- **Headless** (xUnit, BalanceSim, BotClient) has no driver. Those hosts call `SimWorld.Tick()` (and `ClientLayer.Step()` where needed) in a plain loop.
- **Triggers to revisit (superseding ADR):** (1) the shift-5, 8-player fixture's host tick p99 + render p99 > 16.6 ms after tuning; (2) a dedicated server ships; (3) main-thread GC or Godot hitches push tick latency past 2 ticks; (4) judder on 120/144 Hz hosts that interpolation cannot hide.

### Architecture Diagram

```
Godot main thread, one frame
 GameLoop._Process(delta: ignored)
   └─ FrameRunner.RunFrame()
        1 transport.DrainInbound() ──EnqueueOp/samples──► SimWorld op ring / sample ring
        2 n = TickDriver.Advance(Stopwatch ts)          0..4, drop-not-bank, Δ ≤ 1 s
        3 repeat n:  ClientLayer.Step(first = i==0)
                     SimWorld.Tick()  ── phase 13 ──► IEventSink (adapter copies)
                     transport.FlushAfterTick()
                     [M0 guard: elapsed ≥ 10 ms → DropRemaining, stop; ≥1 tick ran]
        4 hitch → SimCheckRing record
   └─ IPresentationRoot.Present(AlphaPermille)          views interpolate ≤ 1 tick
 Transport callbacks (any thread) ──► adapter inbound queue only
```

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.Hosting;
public static class TickRate { public const int Hz = 30; }
public interface IMonotonicClock { long Timestamp { get; } long Frequency { get; } }   // host-implemented
public sealed class TickDriver
{
    public TickDriver(long frequency, int maxCatchUpTicksPerFrame);
    public TickDriverState State { get; }                  // Idle, Running, Paused
    public void Start(long now); public void Stop();
    public int  Advance(long now);                         // 0..maxCatchUp
    public void DropRemaining(int m);
    public void Pause(); public void Resume(long now); public void Resync(long now);
    public int  AlphaPermille { get; }                     // 0..999, Presentation only
    public TickDriverCounters Counters { get; }
}
public interface IFrameTransport { void DrainInbound(); void FlushAfterTick(uint tick); }   // ADR-0011: HostAdapter.PumpInbound / FlushOutbound(tick)
public sealed class FrameRunner
{
    public FrameRunner(IMonotonicClock clock, TickDriver driver, ClientLayer client,
                       SimWorld? world, IFrameTransport transport, FrameRunnerSettings settings);
    public TickDriver Driver { get; }
    public void RunFrame();                                // main thread; once per Godot frame
}
```
This replaces architecture.md's sketch `Advance(long timestamp, long frequency)`: the frequency is fixed at construction (GDD F2: "given once by the adapter"), and `Alpha` is `AlphaPermille`.

### Implementation Guidelines
- `TickDriver` and `FrameRunner` **must** live in `PerformativeMail.Sim/Hosting/` with no Godot reference. `TickDriver` **must never** read a clock.
- The tick rate **must** be the compile-time constant `TickRate.Hz = 30`. Variable-step simulation is forbidden.
- Excess ticks **must** be dropped and counted, never banked. Δ **must** be clamped to 1 s. Backwards timestamps **must** add nothing and be counted.
- The Godot `GameLoop` **must** be the only caller of `FrameRunner.RunFrame()`, exactly once per `_Process`. It **must** have the lowest `ProcessPriority` and run in the main-thread process group. `low_processor_mode` and `physics_interpolation` **must** stay off. It **must not** use the `delta` argument. It **must** use `Stopwatch.GetTimestamp()` via `IMonotonicClock`.
- `_PhysicsProcess`, Godot `Timer` nodes, `System.Threading` timers and background threads **must never** drive or call the Sim.
- `GameLoop` **must** catch `SimBuildException` at boot, and any exception from `RunFrame` (a `SIM_ASSERT` rethrow from a `Faulted` world). It shows the error and ends the session, then stops calling `RunFrame`. It must not rely on Godot's log-and-continue handling (ADR-0003 Risks).
- `SceneTree.Paused` **must never** be used to pause the Sim. Pause **must** go through `TickDriver.Pause()`, and only when Networking reports no remote peers.
- Transport callbacks **must** only enqueue into the adapter's inbound queue. **Nothing** may call `EnqueueOp`, `Tick` or a sample-ring write from a callback.
- Presentation **must** read `AlphaPermille` only. No Sim code may read it.
- Input edges **must** attach only to the first tick of a frame (`isFirstTickInBatch`).
- Tests **must** drive `SimWorld.Tick()` directly. `TickDriver` tests **must** use integer timestamps and include the F2 invariant property test and the GDD F2 worked examples (freq 10^7: 150 ms → 4/0; again → 4/1; 400 ms → 4/8; 60 fps → alternating 0/1, alpha 500/0 ‰).

## Alternatives Considered

### Alternative 1: `_PhysicsProcess` with `physics/common/physics_ticks_per_second = 30`
- **Description**: Let Godot's fixed physics step drive `SimWorld.Tick()`.
- **Pros**: The engine does the accumulation; familiar.
- **Cons**: Drags all presentation physics (Jolt) to 30 Hz. Couples to `SceneTree.Paused` and `Engine.TimeScale`. Godot's catch-up policy (`max_physics_steps_per_frame`) and its edge ordering are unverified for 4.7.1. Not unit-testable without Godot.
- **Rejection Reason**: It violates "pause is a driver state" and "testable Godot-free driver" (R16, R17).

### Alternative 2: Accumulate `_Process(delta)`
- **Description**: `acc += delta; while (acc >= 1/30) Tick();`
- **Pros**: Trivial.
- **Cons**: Floating-point drift. **Spike F11:** under slow frames `delta` under-reported wall time by about 32 %. No drop/bank control.
- **Rejection Reason**: Measured inaccuracy, plus the float ban.

### Alternative 3: Dedicated Sim thread (System.Threading timer or loop)
- **Description**: The Sim ticks on its own thread; Godot reads snapshots.
- **Pros**: Isolates the Sim from render hitches; ready for a dedicated server.
- **Cons**: Forces a thread model now: snapshot double-buffering, an MPSC op queue, re-specified R5/R6/R7 stamping. Godot callback threading is unverified. Overkill for M-1a single-player.
- **Rejection Reason**: Deferred behind the R18 triggers. The Sim API is kept "single owner thread, no statics, no Godot types" so the move stays mechanical.

### Alternative 4: Bank excess ticks (catch up fully after a hitch)
- **Description**: No drop; run every due tick over following frames.
- **Pros**: Game time never slips.
- **Cons**: Hitch → burst → longer frames → spiral. Remote clients see a burst.
- **Rejection Reason**: GDD R17: a slip only gives the crew more real time and every player sees the same slip.

## Consequences

### Positive
- The driver is pure integer arithmetic with a provable invariant; it is fully unit-testable without Godot.
- Godot coupling is one node and one call, verified on 4.7.1.
- Pause cannot desync multiplayer, because the tree pause is irrelevant to the Sim.
- The threading story is simple and safe whichever thread transport callbacks use.

### Negative
- Game time can slip behind wall time after hitches (by design). Deadline-based UIs must read Sim ticks, not wall time.
- Rendering and simulation share the main thread. A slow tick lengthens that frame.
- One more indirection (`IPresentationRoot.Present`) instead of free-running view `_Process` logic.

## Risks
- **Judder on high-refresh hosts** (seed §1, TD): at 144 Hz a ~5 ms tick lands in one frame of about five. *Mitigation:* `AlphaPermille` interpolation; M0 frame-pacing check on a real 120/144 Hz display; R18 trigger 4.
- **Frame-time spikes when 4 catch-up ticks run** (spike: max 4 at 5 fps). *Mitigation:* M0 guard (10 ms); perf budget (`perf-budget.md`).
- **Windows timer resolution / `Stopwatch.Frequency` differs.** *Mitigation:* the driver accepts 10^3..10^12; T2 Windows leg at M0 (ADR-0001 §7).
- **Headless timing ≠ windowed timing** (no vsync headless, F12). *Mitigation:* the greybox story records windowed numbers.
- **Engine validation (godot-specialist, 2026-10-16): 0 blocking, 10 minor.** Folded into §3 and §5 (interpolation reason and setting, low-processor mode, thread groups, view ordering, mixed time bases, driver start, multiplayer poll) and the items below.
- **Main-loop stalls** (Windows title-bar drag, minimise or unfocus throttling, macOS App Nap, debugger breakpoints) stall both the Sim and the transport on a host. Anything over 1 s is clamped and counted as a hitch. *Mitigation:* the M0 Windows run tests drag and minimise on a host. Platform behaviour is UNVERIFIED.
- **Input accumulation:** `Input.UseAccumulatedInput` and agile flushing may change when edges are latched. *Mitigation:* added to ADR-0012's input spike checklist, alongside `_Input` vs `_Process` order.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R1 / TR-sim-001: whole 1/30 s ticks; `TickRateHz = 30` code constant; no variable step | `TickRate.Hz` const; driver returns whole ticks |
| simulation-core.md | R1 / TR-sim-002: tick is `uint32` from 0; differences in int64; first `Tick()` after `Ready` is tick 0 | `FrameRunner` calls `Tick()` only; tick ownership stays in `SimWorld` (ADR-0003). Driver arithmetic is int64. |
| simulation-core.md | R17 / TR-sim-003: Godot-free integer accumulator in `Hosting/`; takes timestamps, never reads a clock | §1 |
| simulation-core.md | F2 / TR-sim-004: cap 4 (safe 3–8); excess dropped, never banked | §1 `Advance` |
| simulation-core.md | R17 / TR-sim-005: M0 guard 10 ms re-read after each tick; ≥ 1 tick always runs | §2 step 3 guard |
| simulation-core.md | F2 / TR-sim-006: invariant; Δ clamped to 1 s; freq 10^3..10^12; backwards clock counted | §1 + property test |
| simulation-core.md | R18 / TR-sim-007: Sim on Godot main thread; owner thread captured at `Freeze()`; Debug asserts | §5 (F9 verified) |
| simulation-core.md | R18 / TR-sim-008: transport callbacks only enqueue | §5, `IFrameTransport.DrainInbound` |
| simulation-core.md | R16 / TR-sim-009: pause is a driver state; solo pause → 0; `SceneTree.Paused` never used | §4 |
| simulation-core.md | R2: one `FrameRunner` per process; `ClientLayer.Step()` before `SimWorld.Tick()`; edges at `i == 0` | §2 step 3 |
| input-controls.md | TR-input-014: MP pause is a local overlay, never `SceneTree.Paused`; the Sim keeps ticking | §4 |
| input-controls.md | H-EC-4: ≥ 3 catch-up ticks; edges on the first tick only | cap 4; `isFirstTickInBatch` |
| player-character-movement.md | TR-movement-007: fixed-step driver with catch-up cap; local extrapolation ≤ 1 tick; no engine physics interpolation | §1, §3 smoothing rule |

**GDD impact (no GDD edited):**
- simulation-core.md R17 names the property `Alpha` (`acc30 / freq`, in [0, 1)). This ADR exposes it as integer `AlphaPermille` (the GDD's own F2 `alphaPermille`), because the Sim assembly bans floating point. **GDD sync:** rename `Alpha` → `AlphaPermille` in R17 and the `TickDriver` States table at re-review.
- architecture.md §Sim.Hosting `Advance(timestamp, freq)` is superseded by `Advance(now)` with the frequency fixed at construction. The GDD already says this.

## Performance Implications
- **CPU**: The driver is O(1) per frame. `FrameRunner` adds one clock read per tick (M0 guard). The cost is dominated by `SimWorld.Tick()` × n (≤ 4).
- **Memory**: A few longs. No allocation per frame.
- **Load Time**: None.
- **Network**: Outbound batches are flushed once per tick, right after it, so latency is not tied to frame rate.

## Migration Plan
New code, no migration.
- The Sim Core story implements `TickRate`, `TickDriver`, `TickDriverCounters`, `IMonotonicClock`, `FrameRunner` (guard off) and tests.
- The first Godot story adds `GameLoop`, `StopwatchMonotonicClock` and a stub `IPresentationRoot`.
- The spike's `SpikeTickDriver` is throwaway and must not be copied: it lacks counters, pause, resync and `DropRemaining`.

## Validation Criteria
- Unit: the F2 invariant property test (≥ 10^4 random Δ sequences including negative and > freq values); the GDD F2 examples; state-machine idempotence; `Resume` clears the accumulator; `AlphaPermille` is 0 while paused.
- `FrameRunner` with a fake clock: edges only on `i == 0`; the guard drops the remainder and still runs ≥ 1 tick; flush is called once per tick.
- Godot greybox: 60 s windowed at 60 Hz and uncapped shows `TicksRunTotal + DroppedTicksTotal` within ±1 of `30 × elapsed`. Re-run the spike's `--maxfps=5` case on the real `GameLoop`.
- Debug thread assert fires if `Tick()` is called from a non-owner thread (test).

## Related
- Evidence: `prototypes/spike-godot-dotnet/REPORT.md` (F9–F12, F15).
- ADR-0001 (layout, `SimCheckRing`), ADR-0003 (`SimWorld` lifecycle, `ClientLayer`), ADR-0011 (adapter seam, transport spike), ADR-0012 (input edges), ADR-0016 (ClientLayer timing), ADR-0019 (GameLoop placement), ADR-0020 (transforms).
- `design/gdd/simulation-core.md` R1, R2, R15–R18, F1, F2; `input-controls.md` R11, H-EC-4; `player-character-movement.md`.
