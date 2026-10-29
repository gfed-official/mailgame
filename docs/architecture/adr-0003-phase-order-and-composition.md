# ADR-0003: Phase Order, Composition Root and Cross-System Calls

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist`. TD-ADR skipped (lean mode). Inputs: `design/gdd/simulation-core.md` R2–R4, R9, R16, R21, States, Interactions (DEV-1/2/3/6/8) (Provisional); `design/gdd/content-data-definitions.md` CD14, CD21 (OQ-CDD-1 hand-back); `docs/architecture/simulation-core-adr-inputs.md` §0, §3.10 (unreviewed seed, re-checked against the GDD); `docs/architecture/architecture.md` §Sim.Core, §Data Flow, §Initialisation order.

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Determinism needs a single fixed answer to "what runs before what" inside a tick. Without one fixed composition, systems would also reach each other through statics or locators. This ADR fixes:
- exactly 14 `PhaseId`s run in a fixed order, with an `ITickEndCheck` hook between phases 12 and 13;
- one `SimWorldBuilder` that binds phases, ops, events, readers and contributors, then freezes;
- one composition root, `SimComposition.Build`, that constructs systems in dependency order by constructor injection from a closed injectable list (checked by a reflection test);
- a rule that only player and `System` requests use the op FIFO. Every in-tick caller calls a callee's synchronous `I…Api` inside the caller's own phase.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET, C# |
| **Domain** | Core (plain-C# Sim; no engine surface) |
| **Knowledge Risk** | LOW. Everything lives in `PerformativeMail.Sim` (zero Godot references, ADR-0001). The one engine touchpoint, where the Godot host calls `SimComposition.Build` at boot, is ADR-0002's `GameLoop` and ADR-0019's boot order. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (nothing applicable) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None engine-side. The reflection test runs under xUnit on .NET 10 and (from M0) .NET 8 (ADR-0001 §7). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (`Core/`, `Composition/`, `ClientLayer/` folders; architecture-test home; `SimCheckRing`/`SimCounters`); ADR-0004 (`EntityIds`, `MailIds`, `PlayerSlot`/`SlotTable` in the injectable list) |
| **Enables** | ADR-0002 (`SimWorld.Tick()`, `Freeze()` owner thread, `ClientLayer.Step()`), ADR-0005 (`RngStream` handles injected per system), ADR-0006 (`ISimStateContributor` binding, dump order = composition order), ADR-0007 (phase 0 drain, `IOpHandler` binding), ADR-0008 (phase 13 flush, `EventKindDescriptor`, reader binding), ADR-0009, ADR-0010 (`ContentSet`/`IStatSheet`/`IStatWriter` injection), ADR-0011 (`ISnapshotSource` binding), ADR-0013–0017 (each system's phase binding and `I…Api`) |
| **Blocks** | Sim Core "builder, phase table, composition root, reflection test" story; every system story that binds a phase or publishes an `I…Api` |
| **Ordering Note** | Types this ADR only *lists* as injectable or bindable (`RngStream` 0005, `EventLog`/`EventKindDescriptor` 0008, `OpKindDescriptor`/`IOpHandler`/`IOpRecorder` 0007, `ContentSet`/`IStatSheet`/`IStatWriter` 0010, `IHostClock`/`ISnapshotSource` 0011) have their shapes finalised by those ADRs. This ADR fixes only *where* they bind and *who* may receive them. |

## Context

### Problem Statement
Spec 07 gave a phase sketch. The GDDs then needed more:
- an expiry phase after the op drain (Grid R12);
- an Intake drain;
- a Throwing step between players and automation (Mail R6: hand → landing → feeder → NPC);
- a separate reaction phase for deaths (DEV-8);
- a tick-end invariant hook (Grid R19).

Cross-system effects also need a rule. Putting automation through the FIFO adds a tick of latency and breaks Mail R6 (DEV-6), while free-form calls make the order composition-dependent. Content & Data (OQ-CDD-1) asks for `ContentSet`, `IStatSheet` and `IStatWriter` to join the injectable list.

### Constraints
- Determinism (ADR-0006): the total order of a tick is phase order, then composition order, then the owner's order inside the phase (R9).
- Allocation-free tick (R32): the phase loop is arrays and `for` loops, with no LINQ, delegates or interface-typed `foreach` over collections.
- Orchestrator rule (systems-index TD gate): Run Structure calls down; lower systems report up through events.
- No statics, no service locator, no `SimWorld` reference inside a system (R4).
- M-1a binds phases 0–4, 8 and 12 only. Unbound phases must cost nothing.

### Requirements
TR-sim-010..014, 019, 041, 044 (see GDD Requirements Addressed).

## Decision

### 1. The phase list (authoritative; amending it requires amending this ADR and Sim Core R3)

| # | `PhaseId` | Owner | Bindable by systems? |
|---|---|---|---|
| 0 | `InputsApply` | Sim Core drains the op FIFO (≤ cap, by `opSeq`; ADR-0007); handlers are per-`OpKind` | **No**: systems bind `IOpHandler`s, not the phase |
| 1 | `ItemsExpire` | Grid Inventory | yes |
| 2 | `IntakeDrain` | Grid Inventory | yes |
| 3 | `PlayersStep` | Player Character (host role, DEV-3) | yes |
| 4 | `ThrowingStep` | Throwing & Catching | yes |
| 5 | `AutomationStep` | Belt Logistics (DEV-1) | yes |
| 6 | `VehiclesStep` | Vehicles & NPC Drivers | yes |
| 7 | `AgentsStep` | Enemy AI & Waves | yes |
| 8 | `MailStep` | Mail & Destinations | yes |
| 9 | `EconomyFlush` | Economy (flush only, DEV-8) | yes |
| 10 | `CombatResolve` | Health & Damage | yes |
| 11 | `CombatReact` | every death reactor, Player Character first (DEV-8) | yes |
| 12 | `RunStep` | Run Structure, the last gameplay phase | yes |
| — | tick end | Sim Core runs bound `ITickEndCheck`s in composition order; nothing mutates or raises events | via `BindTickEnd` |
| 13 | `EventsFlush` | Sim Core hands the tick's log to `IEventSink` (ADR-0008) | **No** |

- `PhaseId` is `enum PhaseId : byte` with values 0–13. Tick end is not a `PhaseId`.
- A phase with no binder is a no-op.
- Several binders of one phase run in **composition order**. Order *inside* a binder is the owner's rule, stated in its GDD.
- Damage raised in phase 11 or later applies on T+1 (Health's pending list).
- `RequestStop` from phase 12 takes effect after phase 13 of the same tick.
- Transform samples (R5) are drained into the latch **between ticks, immediately before** `Tick()`, by Sim Core (`SimWorld.Tick` prologue). Phase 3 ingests them.

### 2. `SimWorld.Tick()` execution
```
Tick():
  guard: State == Running, owner thread, not re-entrant (else count badTick, run nothing)
  prologue: drain sample ring → latch (receivedTick = CurrentTick)
  Stage = InPhase
  for p in 0..12:  _ctx.Phase = p; probe.Before(p); if p == 0: InputsDrain(); for b in _phases[p]: b.Run(in ctx); probe.After(p)
  Stage = TickEnd;  for c in _tickEnd: c.Check(CurrentTick)
  Stage = InPhase; _ctx.Phase = 13; probe.Before(13); EventsFlush(); probe.After(13)   // IEventSink.Deliver, once
  Stage = BetweenTicks; CurrentTick++ ; apply latched stop → Stopped
  on exception escaping a phase: State = Faulted; partial log NOT delivered; re-entrancy flag cleared in finally;
     SIM_ASSERT builds rethrow after marking Faulted
```
- `_phases` is `ISimPhase[][]`, frozen at `Freeze()`. Phase 0's built-in drain is a private `InputsDrain()` branch run inside phase 0 (after `probe.Before(0)`), not an entry of `_phases[0]`; phase 13's flush is built in. Neither phase can be bound (`ReservedPhase`).
- `IPhaseProbe` (Dev Tooling timing) defaults to a null object. It is never null-checked per call. It brackets phases 0–12 and phase 13 (`EventsFlush`); `Before(0)` also wraps the phase-0 drain.
- `TickContext` is a `readonly struct { uint Tick; PhaseId Phase; }` passed `in`. Services are not carried in it; they arrive by constructor.

### 3. Builder and freeze (R4)
- `SimWorldBuilder` (`Core/`) is used only in `Constructed`:
  - `Bind(PhaseId, ISimPhase)`
  - `BindTickEnd(ITickEndCheck)`
  - `BindOp(OpKindDescriptor, IOpHandler)` (ADR-0007)
  - `BindEventKind(EventKindDescriptor)` (ADR-0008)
  - `BindReader(cursor, kindFilter)`, at most `maxEventReaders` (8)
  - `BindStateContributor(ISimStateContributor)` (ADR-0006)
  - `BindSnapshotSource(ISnapshotSource)` (ADR-0011)
- **`Freeze()`** validates, then copies every table to arrays, captures the owner thread (ADR-0002) and moves `Constructed → Ready`. **`Build` and `Freeze` must run on the thread that will call `Tick()`** (the Godot main thread). Async content loading may happen only before `Build`.
- **Build errors** throw `SimBuildException { Code }`. This is the only place the Sim throws by design, and it happens at boot, never in a tick.
  - `DuplicateOpHandler`, `MissingOpHandler`, `ReaderLimit`, `BindAfterFreeze`, `ReservedPhase` (binding 0 or 13), `DependencyCycle`, `UndeclaredDependency` (a system asks for an `I…Api` it does not declare), `DuplicateSystem`.
  - ADR-0007/0008 may add codes for descriptor validation.

### 4. Composition root (R4, TR-sim-013)
- **One root:** `PerformativeMail.Sim.Composition.SimComposition.Build(in SimBuildArgs args) → SimHost`. It returns `SimHost { SimWorld World; ClientLayer Client; }`.
  - `SimBuildArgs` = `{ SimProfile profile, ContentSet content, ulong runSeed, SimInitialCounters? counters, IHostClock? hostClock }` plus optional init-only members `IWorldQuery? World`, `IEventSink? EventSink` and `IOpRecorder? Recorder`. A null `HostClock` gives the `ClientLayer` a clock whose `IsValid` is false; a null `World` leaves world-reading systems unbuilt; a null `Recorder` uses the world's null-object recorder. `SimSettings` and the balance table are members of `ContentSet` (C&D CD21).
  - `SimHost` also exposes the public `ContentSet Content` it was built with (the instance passed in `SimBuildArgs.Content`) and `IStatSheet StatSheet`, plus internal `Services`, `Order` and `Systems` for tests and dump access.
  - `ClientLayer` is composed alongside, but `SimWorld` and `ClientLayer` hold no reference to each other (§6).
- **Construction order** is the topological order of the systems-index dependency map. It is written as explicit, ordered code in `SimComposition`: no reflection-driven discovery and no DI container.
  - `Composition/SystemGraph.cs` declares each system's dependencies as data.
  - A test checks that the code order is a valid topological order of `SystemGraph` and that `SystemGraph` is acyclic.
- **Composition order = construction order.** It also fixes same-phase binder order, tick-end order and contributor (dump) order.
- **Profiles:** `SimProfile` selects which systems are built: `M1aGreybox` (Grid, Players, Throwing, Mail, Manual Delivery, Run stub), `Full`, and per-test profiles. An unbuilt system's phase is a no-op, and any `I…Api` it would provide must not be required by a built system (`UndeclaredDependency`).
- **The injectable list (closed).** A system's public constructor may take only:

| Parameter type | Who may receive it | Shape owned by |
|---|---|---|
| `EventLog` writer facade | any system | ADR-0008 |
| `EntityIds`, `MailIds` | any system that allocates (Mail only for `MailIds`) | ADR-0004 |
| `ISlotView` (read-only view of `SlotTable`) | any system; write face Sim Core only | ADR-0004 |
| its owner-filtered `IRngStreams` view (names it owns) | stream owners only; Throwing gets none | ADR-0005 |
| `ITickReader` (current tick, phase) | any system | this ADR |
| `IWorldQuery` (read-only world) | any system | ADR-0026 |
| `IWalletGate` (ADR-0017 §8 port) | Mail & Destinations only | ADR-0017 |
| `IRunControl` | **Run Structure only** | this ADR |
| `I<System>Api` of a **declared** dependency | per `SystemGraph` | each system's ADR |
| `ContentSet` (or a sub-table of it) | any system | ADR-0010 |
| `IStatSheet` | any system that reads stats | ADR-0010 |
| `IStatWriter` | **Perks & Perk Draft, Stamps & Run Events only** (C&D CD14) | ADR-0010 |
| `SimCheckRing`, `SimCounters` | any system | ADR-0001 |
| the system's own settings record (a `SimSettings` sub-record) | its owner | ADR-0010 |

- **Reflection test** (`tests/unit/architecture/CompositionRulesTests.cs`, ADR-0001 §8):
  - every type marked `[SimSystem(SystemId)]` has exactly one public constructor (`Id` maps it to `SystemGraph`);
  - each parameter type is in the table above for that system;
  - every `I…Api` parameter is a declared dependency in `SystemGraph`.
- **No statics:** an architecture test fails on any static field in the Sim that is not `const`, or not `static readonly` of an immutable type (primitive, `string`, or a type marked `[ImmutableTable]`). `[CompilerGenerated]` types (lambda caches, `<PrivateImplementationDetails>`) are excluded. A `static readonly T[]` counts as **mutable**: constant tables are `ReadOnlySpan<T>` literal properties or `[ImmutableTable]` wrappers. For an element type wider than one byte, the literal getter binds to `RuntimeHelpers.CreateSpan`, which allocates (~64 B per call) when the getter is not optimised; shipped table getters carry `[MethodImpl(MethodImplOptions.AggressiveOptimization)]`, and a fully unoptimised (Debug/ExportDebug) Sim still allocates there. Pure static helper methods (`SimAssert`, `SerialU16`) are allowed.
- No system holds a `SimWorld` reference, and there is no service locator.

### 5. Cross-system calls (R9, DEV-6, TR-sim-019)
- **The op FIFO carries only player and `System` requests** (`PlayerJoin`, `PlayerLeave`, `FreezePlayer`, hand deliveries, inventory ops, throw releases, interaction, build/shop/draft requests).
- **Every in-tick system caller** (expiry, Intake drain, Throwing landings and catches, automation, Vehicles' NPC drops, `MailStep`, `CombatReact`) calls the callee's documented synchronous `I…Api` method **inside the caller's own phase**.
  - Each call is one atomic mutation that raises events like an op handler, but it produces no reply.
- **Where `I…Api` mutations are legal:** any phase 0–12. They are illegal at tick end, in `EventsFlush`, and between ticks.
  - Between ticks, only `ISnapshotSource` and other read-only faces are callable.
  - `SIM_ASSERT` builds check `Stage == InPhase && Phase ≤ RunStep` at the top of every mutating `I…Api` method (`TickGuard.AssertMutable()`). In Release an illegal call is ignored and counted.
- **Direction:** calls go *down* the dependency map; information goes *up* through events (pull cursors, ADR-0008). There are no synchronous subscribers and no callbacks from a callee into its caller.
- **Run Structure is the one orchestrator.** It calls down into Mail, Waves and World and holds `IRunControl`.
- **Hands beat machines:** on a contested last free cell, a player op (phase 0) always beats an inserter (phase 5). The inserter retries next tick (Pillar 3, amends Grid R7).
- `IRunControl.RequestStop(StopReason)` is legal in phase 12. From any other phase it is latched as if raised in phase 12 and counted (`stopOutOfPhase`). A second stop is ignored and counted.

### 6. Two roles: `SimWorld` (host) and `ClientLayer` (every client) (R2, TR-sim-014)
- `SimWorld` is the only authoritative state.
- `ClientLayer` (`ClientLayer/`) is a shell. It runs bound `IClientStep`s in fixed `ClientStepId` order:
  1. `MirrorDrain`
  2. `InputLatch`
  3. `CarrierStep`
  4. `Previews`

  Systems bind through `ClientLayerBuilder.Bind(ClientStepId, IClientStep)`.
- `ClientLayer` reads only its mirror, **never `SimWorld`**, even on the host. Host requests go through the same op encoder and a loopback adapter as remote ones (ADR-0011).
- Pure functions (`holdEff`, Mail `Evaluate`, the flight query) live in the Sim assembly and are called by both roles.

### 7. Lifecycle (TR-sim-044)

| State | Ticks | `EnqueueOp` | Entered by | Next |
|---|---|---|---|---|
| `Constructed` | no | false, counted | `SimComposition.Build` | `Ready` via `Freeze()` (after binds, content, world tables, world-build id allocation) |
| `Ready` | no | yes (queued for tick 0) | `Freeze()` | `Running` (first `Tick()` processes tick 0) |
| `Running` | yes | yes | first `Tick()` | `Stopped`, `Faulted` |
| `Stopped` | no | false, counted | `RequestStop` completing its tick through phase 13; or host `Stop()` outside a tick | — (results and dump readable) |
| `Faulted` | no | false, counted | an exception escaping a phase | — (dump invalid; adapter ends the session) |

Pause is not a world state (ADR-0002). Joins, rejoins and leaves are ops (ADR-0004 §4).

### Architecture Diagram

```
SimComposition.Build(args)                         [Composition/]
  ├─ new <SystemA>(ctor-injected: EventLog, EntityIds, IRngStreams(own), I<Dep>Api, ContentSet, IStatSheet …)
  ├─ new <SystemB>(…, I<SystemA>Api)              construction order = topological = composition order
  ├─ builder.Bind(PhaseId.X, systemA.Phase) / BindOp / BindEventKind / BindReader / BindTickEnd / BindStateContributor
  ├─ ClientLayerBuilder.Bind(ClientStepId.*, …)
  └─ Freeze() → SimWorld Ready (owner thread captured) + ClientLayer

SimWorld.Tick(T)
  [sample drain] → 0 InputsApply(op FIFO) → 1 ItemsExpire → 2 IntakeDrain → 3 PlayersStep → 4 ThrowingStep
  → 5 AutomationStep → 6 VehiclesStep → 7 AgentsStep → 8 MailStep → 9 EconomyFlush → 10 CombatResolve
  → 11 CombatReact → 12 RunStep → [tick end: ITickEndCheck*] → 13 EventsFlush(IEventSink)
  in-tick effects: caller ──sync I…Api──► callee (inside caller's phase); upward news via EventLog cursors
```

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.Core;
public enum PhaseId : byte { InputsApply, ItemsExpire, IntakeDrain, PlayersStep, ThrowingStep, AutomationStep,
    VehiclesStep, AgentsStep, MailStep, EconomyFlush, CombatResolve, CombatReact, RunStep, EventsFlush }
public readonly struct TickContext { public readonly uint Tick; public readonly PhaseId Phase; }
public interface ISimPhase      { void Run(in TickContext ctx); }
public interface ITickEndCheck  { void Check(uint tick); }                 // must not mutate or raise events
public enum TickStage : byte { BetweenTicks, InPhase, TickEnd }
public interface ITickReader    { uint CurrentTick { get; } TickStage Stage { get; } PhaseId Phase { get; } } // Phase valid only when Stage == InPhase
public interface IRunControl    { void RequestStop(StopReason reason); }   // Run Structure only; phase 12
public interface IPhaseProbe    { void Before(PhaseId p); void After(PhaseId p); }   // Dev Tooling; null object default
public enum SimState : byte { Constructed, Ready, Running, Stopped, Faulted }
public sealed class SimWorldBuilder
{
    public void Bind(PhaseId phase, ISimPhase p);                 // 0 and 13 → SimBuildException(ReservedPhase)
    public void BindTickEnd(ITickEndCheck c);
    public void BindOp(OpKindDescriptor d, IOpHandler h);         // ADR-0007
    public void BindEventKind(EventKindDescriptor d);             // ADR-0008
    public void BindReader(/* cursor, kind filter */);            // ADR-0008; ≤ 8
    public void BindStateContributor(ISimStateContributor c);     // ADR-0006
    public void BindSnapshotSource(ISnapshotSource s);            // ADR-0011
    public SimWorld Freeze();
}
public sealed class SimBuildException : Exception { public SimBuildErrorCode Code { get; } }
public sealed class SimWorld { public SimState State { get; } public uint CurrentTick { get; }
                               public bool EnqueueOp(ActorId actor, in Op op) /* ADR-0007 */; public void Tick(); public void Stop(); }
namespace PerformativeMail.Sim.ClientLayer;
public enum ClientStepId : byte { MirrorDrain, InputLatch, CarrierStep, Previews }
public interface IClientStep { void Step(in ClientStepContext ctx); }   // ctx carries isFirstTickInBatch (ADR-0002)
namespace PerformativeMail.Sim.Composition;
[AttributeUsage(AttributeTargets.Class)] public sealed class SimSystemAttribute : Attribute { public SimSystemAttribute(SystemId id) => Id = id; public SystemId Id { get; } }
public static class SimComposition { public static SimHost Build(in SimBuildArgs args); }
```
**Port interfaces (TR-sim-041), with their owning ADR:**
- this ADR: `ISimPhase`, `ITickEndCheck`, `IClientStep`, `IRunControl`, `ITickReader`, `IPhaseProbe`;
- ADR-0007: `IOpHandler`, `IOpRecorder` (recording mechanics: ADR-0006);
- ADR-0008: `IEventSink`;
- ADR-0011: `ISnapshotSource`, `IHostClock`;
- ADR-0002: `IMonotonicClock`;
- ADR-0005: `IRngSource`;
- ADR-0006: `ISimStateContributor`.

### Implementation Guidelines
- Phases **must** run exactly in the §1 order. A new phase **must** amend this ADR and Sim Core R3. Systems **must never** bind phase 0 or 13.
- Same-phase binders **must** run in composition order. Owners **must** state their intra-phase order in their GDD.
- `ITickEndCheck` implementations **must not** mutate state or raise events.
- Systems **must** receive dependencies only through their constructor, only from the §4 injectable list.
  - Static mutable state, service locators, DI containers and `SimWorld` references inside systems **must never** be used.
  - `IRunControl` **must** go only to Run Structure, and `IStatWriter` only to Perks and Stamps.
- Composition **must** be explicit ordered code in `SimComposition`, in a topological order of `SystemGraph`.
- The op FIFO **must** carry only player and `System` requests. In-tick effects **must** be synchronous `I…Api` calls made inside the caller's own phase.
- Mutating `I…Api` methods **must** start with `TickGuard.AssertMutable()`. They **must never** be called at tick end, in phase 13 or between ticks.
- Calls **must** go down the dependency map only. Upward information **must** use events. Callees **must never** call back into callers.
- `ClientLayer` **must never** read `SimWorld`.
- The phase loop **must** be allocation-free: arrays and `for`, no LINQ or delegates.
- `SimBuildException` **must** be thrown only during composition and freeze, never inside `Tick()`.

## Alternatives Considered

### Alternative 1: Spec 07's original order (no `ItemsExpire`/`IntakeDrain`/`ThrowingStep`/`CombatReact`; automation after vehicles)
- **Description**: Keep the spec sketch and let systems order themselves within fewer phases.
- **Pros**: Fewer phases; matches the spec.
- **Cons**: It breaks Grid R12 (a pickup must beat expiry on the same tick), Mail R6 (hand → landing → feeder → NPC) and the same-tick knockdown without synchronous subscribers.
- **Rejection Reason**: The GDD deviations DEV-1/3/8 exist because the spec order fails those rules.

### Alternative 2: Event-driven / subscriber model (systems react synchronously to events)
- **Description**: Systems subscribe to each other's events and react immediately.
- **Pros**: Loose coupling; familiar.
- **Cons**: The order depends on subscription order, which re-enters and recurses. Hard to make 0-alloc and deterministic.
- **Rejection Reason**: R12 forbids synchronous subscribers. Pull cursors in a later phase give the same decoupling deterministically.

### Alternative 3: DI container or service locator
- **Description**: `Microsoft.Extensions.DependencyInjection` or a static `Services` registry.
- **Pros**: Less wiring code.
- **Cons**: A runtime package in the Sim (banned, ADR-0001). Construction order becomes implicit, and so do composition, phase and dump order. Hidden dependencies defeat the reflection test.
- **Rejection Reason**: Explicit ordered composition is what makes composition order a deterministic, reviewable fact.

### Alternative 4: Route automation and other in-tick effects through the op FIFO
- **Description**: Every mutation is an op, including system-generated ones.
- **Pros**: One mutation path; uniform recording.
- **Cons**: Adds a tick of latency to every inserter and breaks Mail R6. The FIFO cap would throttle automation.
- **Rejection Reason**: DEV-6 / R9.

## Consequences

### Positive
- One reproducible total order per tick: phase → composition → owner order.
- Unbuilt systems cost nothing, so milestones bind subsets by profile.
- Dependencies are visible in constructors and machine-checked.
- The host takes the same code path as clients (loopback), so serialisation bugs surface early.

### Negative
- Adding a phase is a deliberate amendment, not a local change.
- `SimComposition` is hand-maintained ordered code that grows with every system.
- Synchronous `I…Api` calls create compile-time coupling down the map. Systems-index layering must stay acyclic.

## Risks
- **Composition order silently changes behaviour** (same-phase binder order, dump order). *Mitigation:* the golden canonical-dump tests (ADR-0006) catch any reorder; reorders need a test-fixture update and a review note.
- **Mutating `I…Api` called between ticks from Presentation.** *Mitigation:* `TickGuard.AssertMutable()`; Presentation gets only read faces (`ISnapshotSource`, mirrors).
- **Phase 3 sample drain vs ADR-0011's adapter timing.** *Mitigation:* the drain happens in the `Tick()` prologue on the owner thread (ADR-0002 §5). ADR-0011 only fills the ring.
- **C&D/Sim Core re-review (Q-ARCH-12) changes injectables.** *Mitigation:* the injectable table is the single place to amend; the reflection test enforces it.
- **Engine validation (godot-specialist, 2026-10-16): 0 blocking, 6 minor.** Folded in: explicit `TickStage` (tick end is not a `PhaseId`); `SimSystemAttribute.Id`; the no-statics test excludes compiler-generated types and treats arrays as mutable; `Build`/`Freeze` on the ticking thread. Also:
  - **Unhandled exceptions in Godot callbacks** are logged and the engine keeps running (UNVERIFIED for 4.7). An uncaught `SimBuildException` at boot, or a `SIM_ASSERT` rethrow from a `Faulted` world, would otherwise leave `GameLoop` throwing every frame. *Mitigation:* the host catches both explicitly and ends the session (ADR-0002 §3 guideline; ADR-0019 boot).
  - **`TreatWarningsAsErrors`:** `SimBuildException` needs the analyzer-expected constructors (CA1032 if enabled).
  - **Editor hot reload with a live `SimWorld`** is unverified. A reload is treated as a reboot of the session; the Sim is never serialised across it.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R3 / TR-sim-010: exactly 14 `PhaseId`s 0–13 in fixed order; `ITickEndCheck` between 12 and 13 | §1, §2 |
| simulation-core.md | R3 / TR-sim-011: unbound phase is a no-op; same-phase binders in composition order; new phases need an R3 amendment | §1 |
| simulation-core.md | R4 / TR-sim-012: `SimWorldBuilder` binds pre-`Freeze`; violations throw `SimBuildException` | §3 |
| simulation-core.md | R4 / TR-sim-013: composition root in `Composition/`; constructor injection only; reflection test | §4 |
| simulation-core.md | R2 / TR-sim-014: `SimWorld` and `ClientLayer` separate; host loopback uses the same encoder | §6 |
| simulation-core.md | R9 / TR-sim-019: only player/`System` requests use the FIFO; in-tick callers use sync `I…Api` in their own phase | §5 |
| simulation-core.md | Interactions / TR-sim-041: port interfaces | Key Interfaces (owner per port) |
| simulation-core.md | States / TR-sim-044: lifecycle; `RequestStop` via `IRunControl` in phase 12 | §5, §7 |
| grid-inventory.md | R12 (pickup beats expiry), R14 (drain after `InputsApply`), R19 (tick-end conservation), R7 amended (player op beats inserter) | §1 phases 0–2, tick end; §5 |
| mail-destinations.md | R6: hand → landing → feeder → NPC; flags set in T take effect from T+1 | phases 0, 4, 5, 6, 12 |
| player-character-movement.md | P15 knockdown reaction in `CombatReact`; host-role `PlayersStep` (DEV-3) | §1 |
| content-data-definitions.md | CD14/CD21 / OQ-CDD-1: `ContentSet`, `IStatSheet`, `IStatWriter` (Perks/Stamps only) join the injectable list | §4 table |
| throwing-catching.md | `ThrowingStep` after `PlayersStep`, flights in creation order | §1 |

**GDD impact (no GDD edited):**
- content-data-definitions.md CD21 (and ADR-0010) write `new SimWorld(settings, content, runSeed)`. Under this ADR, that constructor is called only inside `SimComposition.Build(args)`, which passes the same `ContentSet` by reference. This is consistent; **GDD sync:** CD21 should name `SimComposition.Build` at C&D re-review.
- simulation-core.md R4's injectable list does not yet include `ContentSet`/`IStatSheet`/`IStatWriter`, `SlotTable`, `SimCheckRing`/`SimCounters` or the per-system settings record. This ADR adds them (closing the OQ-CDD-1 hand-back). **GDD sync:** Sim Core R4 at re-review.
- Phase names are PascalCase (`InputsApply`) where the GDD writes `Inputs.Apply`. This is notation only.

## Performance Implications
- **CPU**: The phase loop is 14 array walks per tick; unbound phases are empty arrays. `I…Api` calls are direct interface calls. Per-phase budgets are in `perf-budget.md`.
- **Memory**: Frozen arrays sized at build; no growth after `Freeze()`.
- **Load Time**: Composition is O(systems); negligible next to content load.
- **Network**: None directly. Loopback parity (§6) means host and clients exercise the same codecs.

## Migration Plan
New code. In the Sim Core "builder/phases/composition" story:
- add `PhaseId`, `TickContext`, `ISimPhase`, `ITickEndCheck`, `SimWorldBuilder` (with stubs for descriptor types until ADR-0007/0008 land), `SimWorld` lifecycle, `ClientLayer` shell, `SimComposition` with the `M1aGreybox` profile, `SystemGraph`, and the reflection and no-statics architecture tests.

## Validation Criteria
- A test world with recording phases runs them in exactly the §1 order; tick end runs between 12 and 13; unbound phases record nothing.
- Each `SimBuildErrorCode` has a failing fixture.
- The reflection test fails on a fixture system that takes `IStatWriter` (not Perks/Stamps), takes an undeclared `I…Api`, or has a static mutable field.
- The topological-order test fails when `SystemGraph` gets a cycle.
- A mutating `I…Api` call between ticks asserts in Debug and is counted in Release.
- Lifecycle: `Tick()` on `Ready` → `Running`; `RequestStop` in phase 8 → latched, stops after phase 13, `stopOutOfPhase` counted; an exception in a phase → `Faulted`, sink not called.

## Related
- ADR-0001 (folders, architecture tests), ADR-0002 (`FrameRunner` calls `ClientLayer.Step`/`SimWorld.Tick`; owner thread), ADR-0004 (ids, `SlotTable`), ADR-0005–ADR-0011 (types bound or injected here), ADR-0009 (Grid phases 1–2, tick-end check).
- `design/gdd/simulation-core.md` R2–R4, R9, States, Interactions; `design/gdd/systems-index.md` dependency map; `content-data-definitions.md` CD14, CD21.

## Errata
- 2026-10-17 — §2: the tick pseudocode and notes said phase 0's drain is the first entry of `_phases[0]` and showed the probe around phases 0–12 only; now the drain is a private `InputsDrain()` branch inside phase 0 and `IPhaseProbe` also brackets phase 13 (as shipped in `SimWorld.RunPhases`/`RunEventsFlush`); source TD-013 items 3 and 7.
- 2026-10-17 — §4: `SimBuildArgs` was `{ profile, content, runSeed, counters }`; now also lists `IHostClock? HostClock` and the optional `World`, `EventSink` and `Recorder` members, and `SimHost`'s public `Content` (and `StatSheet`) plus internal `Services`/`Order`/`Systems`; source TD-061 item 1, TD-080 item 2.
- 2026-10-17 — §4 injectable table: had no `IWorldQuery` row; now `IWorldQuery` (read-only world, any system, ADR-0026), as allowed by `CompositionRulesTests`; source TD-064 item 2.
- 2026-10-17 — §4 no-statics rule: `ReadOnlySpan<T>` literal tables were presented as allocation-free unconditionally; now notes the `RuntimeHelpers.CreateSpan` Tier-0/unoptimised allocation and the `AggressiveOptimization` getter used by shipped tables; source TD-008.
- 2026-10-17 — §4 injectable table: "`SlotTable` (read-only view)" was the row name; now `ISlotView`, the interface the composition rule test actually checks; source: errata pass review.
- 2026-10-28 — §4 injectable table: had no row for the Mail-owned wallet port, so ADR-0017 §8's "the composition root injects `IWalletGate` into Mail's constructor" had no legal implementation (`CompositionRulesTests.CheckParameter` rejects any parameter not on the table and not an `I…Api`); now the row "`IWalletGate` (ADR-0017 §8 port) | Mail & Destinations only | ADR-0017". The port is injected by the composition root (`SimSystemContext.WalletGate`, guarded to `SystemId.MailDestinations`), not declared as a `SystemGraph` edge; `SystemGraph` is unchanged. `CompositionRulesTests.Table()` gains the matching rule — that test change is mail-destinations story 005's code work. Approved by Evan 2026-10-28 7:26 AM PT, in his own words in the Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged"; source: mail-destinations 005 readiness (gap 1 item 5), TD-177.
