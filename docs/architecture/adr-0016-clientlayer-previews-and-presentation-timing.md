# ADR-0016: ClientLayer, Shared Pure Functions and Presentation Timing

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
Autonomous planning run (architecture-decision skill, `--review lean`: TD-ADR skipped, engine specialist consulted). Acceptance pending the project owner's batch confirmation.

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Amended before acceptance: the 4d target-vs-branch wording now matches ADR-0014 §3.

## Summary
Every client (the host's own one included) must show deliver prompts, aim arcs, money hints and hold feedback that equal the host's later outcome, without ever playing an outcome beat before the host decided it. This ADR fills ADR-0003's `ClientLayer` shell. It defines the ports through which the Sim-side ClientLayer reads Client.Core's mirrors and sends requests, and the fixed sub-order of the `Previews` step (focus → deliver preview + tracker → throw preview → edge resolution → hold advance). It also states the "compiled once" rule for the shared pure functions (`Evaluate`, F6/F7/F10 pay, ballistic/sweep, `HoldEff`), the predictive-vs-outcome presentation rule with its event stream, and a single render clock that derives `RenderSimTimeMs` from ADR-0011's unchanged `IHostClock`.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (.NET) |
| **Domain** | Core (client simulation) / Presentation timing |
| **Knowledge Risk** | LOW. Everything decided here is plain C# in `PerformativeMail.Sim` (`ClientLayer/`, `<System>/Client/`) and `PerformativeMail.Client.Core`. Godot appears only as the consumer of published read-only state (views, ADR-0019). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md`; spike `prototypes/spike-godot-dotnet/REPORT.md` (F9 main thread, F11 `delta` unusable) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | (1) `ClientLayer.Step` allocates 0 B after warm-up on the net8.0 leg. (2) The 0.3 ms p99 ClientLayer budget, on the reference machine once it exists (Q-ARCH-10). Neither touches a Godot API. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0002 (FrameRunner calls `ClientLayer.Step` before each `SimWorld.Tick`; `AlphaPermille`; `isFirstTickInBatch`), ADR-0003 (`ClientLayer` shell, `ClientStepId`, "ClientLayer never reads SimWorld"), ADR-0006 (integer maths, `Cents`), ADR-0008 (event entries, ordinals, `CallSeq`), ADR-0009 (`ContainerMirror` seal rule, `PendingQueue`, non-optimistic ops), ADR-0011 (`ClientAdapter` inbox, `IHostClock`, loopback = remote) — all Accepted. **Also** ADR-0012 (`InputFrame` edges), ADR-0014 (focus, `IInteractable`, hold counter), ADR-0015 (ballistic/sweep functions) and ADR-0017 (`Evaluate`, F6/F7/F10, `DeliveryOutcome`) — all Accepted 2026-10-16. |
| **Enables** | ADR-0018 (HUD reads `IRenderClock` and `PresentationEventStream` through `IHudState`), ADR-0019 (views read the published preview states), ADR-0020 (remote-body timeline slot), ADR-0021 (flight timeline, ghost reconcile) |
| **Blocks** | Manual Delivery preview/tracker/latch stories; Throwing aim-preview and launch-ghost stories; the Mail client-`Evaluate` parity story (M-INT-2); the HUD timer story; the `ClientLayer` composition story in simulation-core |
| **Ordering Note** | ADR-0014 must bind focus as `PreviewSubStep.Focus` (§2) — done: ADR-0014 §3/§7 amended 2026-10-16. ADR-0012 owns edge *latching*; this ADR owns where latched edges are *resolved*. M-1a builds `ContainerMirror` confirmed state + seal staging only; the `PendingQueue` arrives at M0 with the transport (ADR-0009), so at M-1a `panelView` = confirmed state. |

## Context

### Problem Statement
ADR-0003 created a `ClientLayer` with four named steps and a rule (it reads only its mirror), but left open:
- what the mirror is, and how the Sim-side steps read container state that ADR-0009 put in Client.Core;
- where in those steps the deliver preview, its settle tracker, the throw preview and the press-tick latch run;
- how "the same code on host and client" is enforced;
- how Presentation tells a prediction from an outcome;
- where the HUD's `renderSimTimeMs` comes from, given that ADR-0011's `IHostClock` exposes only `EstimatedHostTick` and `IsValid`.

Manual Delivery, Throwing, Mail and HUD all need these answers at M-1a, solo over loopback.

### Constraints
- `ClientLayer` never reads `SimWorld`, even on the host (ADR-0003 §6). Host requests use loopback (ADR-0011).
- The Sim references no other assembly. Client.Core references the Sim, never the reverse (ADR-0001).
- Integer only: ticks, mm, permille, cents (ADR-0006). `AlphaPermille` is Presentation-only (ADR-0002).
- The client step order is fixed: `MirrorDrain`, `InputLatch`, `CarrierStep`, `Previews` (ADR-0003). Adding a top-level step needs an ADR-0003 amendment, so this ADR works inside `Previews`.
- A host event reaches Presentation in the frame it arrives and the mirror at the next client tick (Sim Core N2, L772).
- Mail's GDD requires 0 B allocation, ≤ 6 predicate checks and 0 registry scans per `Evaluate`. The deliver preview scans ≤ 26 entries per focused tick, with no cache.

### Requirements
- TR-delivery-003..006, 012..014, 017, 018; TR-mail-005; TR-throwing-016, 018; TR-hud-003, 004 (Appendix A, primary here).
- The preview equals the outcome: 1,000 seeded pairs (M-INT-2) and 10,000 static throws (Throwing feel test 1).
- ClientLayer p99 ≤ 0.3 ms per client tick, with `CarrierStep` + focus ≤ 0.10 ms of that (advisory until Q-ARCH-10). 0 B per tick after warm-up.

## Decision

### 1. Anatomy: ClientLayer (Sim) + ports + Client.Core mirrors

`ClientLayer` stays in `PerformativeMail.Sim/ClientLayer/`, composed by `SimComposition.Build`. Each system's client step lives in its own folder, `PerformativeMail.Sim/<System>/Client/` (for example `Delivery/Client/DeliverPreviewStep.cs`). Steps therefore call the Sim's pure functions directly, and the Sim's analyzers (no floats, no clocks, no statics) cover them.

The ClientLayer reads and writes the outside world only through **ports defined in `Sim/ClientLayer/Ports`** and implemented in Client.Core (or in test doubles):

| Port (Sim-defined) | Implemented by (Client.Core) | Used by |
|---|---|---|
| `IClientInbox` — `Drain(IMirrorSink sink)` applies every decoded batch received since the last drain, in host order (tick, ordinal), and replies in stream order | `ClientAdapter` inbox (ADR-0011) | `MirrorDrain` |
| `IMirrorPart` — `Apply(in EventEntry e, ReadOnlySpan<byte> blob)`, `ApplyReply(in Reply r)` | `ContainerMirror` (ADR-0009, Client.Core); Sim-side parts below | `MirrorDrain` dispatch by event kind |
| `IPanelView` — the displayed own containers (confirmed + replayed pending), read-only, enumerated in scan order | `ContainerMirror` / `PendingQueue` (ADR-0009) | deliver preview (MD3), throw preview (selected entry) |
| `IRequestSink` — `bool TrySend(in Op op)`; the adapter assigns the `reqId` | `ClientAdapter` request encoder | edge resolution (§2 step 4d) |
| `IHostClock` (ADR-0011, unchanged) | host: `CurrentTick`; client: Networking's estimator | `holdStartTick`, flight timeline |

The Sim-side mirror parts live in `Sim/<System>/Client/` and are fed only by `MirrorDrain`:
- `DestinationMirror`: static registry rows from the world tables plus OPEN/CLOSED/LOCKED from `DeliveriesState` (ADR-0017);
- `WalletMirror`: the value, from `WalletChanged`;
- `RunMirror`: phase, `deadlineTick`, `closeTick`;
- `FlightMirror`: live flights, from `ThrowLaunched`/`FlightResolved`;
- the client `StatSheet` copy (static at M-1a).

Binding uses `ClientLayerBuilder.BindMirrorPart(EventKindSet kinds, IMirrorPart part)`. This is an additive builder method beside ADR-0003's `Bind(ClientStepId, IClientStep)`, and the same frozen-table rules apply.

The host shell attaches the ports once, after `SimComposition.Build` and before the first `Step`, with `ClientLayer.Attach(in ClientPorts ports)`. `SimBuildArgs` is unchanged. A test harness attaches in-memory ports and calls `Step` in a plain loop (ADR-0002 §5).

### 2. Step order, including the `Previews` sub-order

| Order | Step (ADR-0003 id) | What runs |
|---|---|---|
| 1 | `MirrorDrain` | `IClientInbox.Drain`. Every received entry is applied to its mirror part. Replies retire pending ops (ADR-0009: only a reply retires). This covers MD3's "apply own container events and replies" first. |
| 2 | `InputLatch` | Latches this tick's `InputFrame` (ADR-0012). Edges are present on the first tick of a catch-up batch only. **No verb resolves an edge here.** |
| 3 | `CarrierStep` | Owned carrier step (ADR-0013), client-authoritative (PC P2). |
| 4 | `Previews` | One composite step owned by ClientLayer, with fixed `PreviewSubStep` order:<br>**4a `Focus`**: the interaction focus and 4-tick dwell (ADR-0014; PC P11).<br>**4b `DeliverPreview`**: MD3 `Preview(...)` for the focused Deliver anchor, then `DeliverPreviewTracker.Update` (§4).<br>**4c `ThrowPreview`**: T3, the same F2/F4 functions (ADR-0015), keyed cache, colour from `Evaluate`.<br>**4d `EdgeResolve`**: walks the latched edges in arrival order. Each Interact/Throw/Catch edge goes to its verb using **this tick's** 4a–4c state. That state is the press-tick recompute of the owner's **branch** (MD4, TR-delivery-006). The **target** stays the anchor named by the edge's presented snapshot (Input G1, ADR-0014 §3). *(Clarified 2026-10-16: the draft said the recompute "overrides" the presented snapshot, which contradicted ADR-0014 §3 and G1.)* Requests go out through `IRequestSink`.<br>**4e `HoldAdvance`**: hold counters accrue after edges (Input R11) and are cancelled by `ShouldCancel(latch)` (ADR-0014). |

This resolves the contradiction between MD3's order (events → focus → preview → resolve the press) and ADR-0003's `InputLatch`-before-`Previews` order. Latching stays in `InputLatch`; resolution moves into `Previews`. Order 1 → 4a → 4b → 4d is exactly TR-delivery-005.

### 3. Shared pure functions are compiled once, in the Sim, and take facts, not systems

| Function | Home (Sim) | Host caller | Client caller |
|---|---|---|---|
| `MailRules.Evaluate(in DestinationFacts, in UnitFacts) → DeliverEval` | `Sim/Mail/` (ADR-0017) | Mail consume routine | 4b, 4c (arc colour) |
| `MailPay.UnitPay` / `UnitPenalty` / `HintPayout` (F6/F7/F10) | `Sim/Mail/` | Mail consume | 4b `predictedPay`/`predictedPenalty` |
| `Ballistic.PositionAt`, `FlightQuery.Sweep` (F2/F4) | `Sim/Throwing/` (ADR-0015) | Throwing.Step | 4c, flight timeline (§6) |
| `Hold.HoldEff` | `Sim/Players/` (ADR-0012/0014) | hold validation | 4d latch `holdTicks` |
| `IStatSheet.Read` | `Sim/Content/` (ADR-0010) | systems | client sheet copy |

**Facts structs.** Each function takes small `readonly struct` "facts" (for example `DestinationFacts {DestinationId, AddressId, TypeIndex, DistrictId, State}` and `UnitFacts {Category, KindIndex, AddressId, SpawnValue, deadlines}`), or `ReadOnlySpan`s. The host builds them from authoritative tables and the client from its mirror parts. No shared function may take a system object, a mirror, `SimWorld` or an interface with hidden state. That is what makes "same code" testable: both sides only build facts.

**Preview money** is Σ over units of `MailPay.UnitPay(...)`, rounded once per unit exactly as F10 (MD F1). It is never one rounding over a total.

**Scan order** is cursor → hotbar → inventory → backpack, row-major. It is produced by one Sim helper, `DeliverScan.Enumerate(IPanelView, ...)`. The host's `deliver_matching` consumption uses the same helper over authoritative containers (TR-delivery-014).

### 4. `DeliverPreviewTracker` (MD4, TR-delivery-004)

- Lives in `Sim/Delivery/Client/` and runs in sub-step 4b. Never in Presentation.
- State: `(BranchKey key, uint sinceTick)`, with `BranchKey = (branch kind, destinationId, selectedEntryId if MISDELIVER_ONE)`. `displayedTicks = clientTick − sinceTick`. Ticks are Sim ticks (the `tick` passed to `ClientLayer.Step`), never frames or ms.
- `sinceTick` resets on a key change or when MD2 rows 1–5 make the verb unavailable. A payload change (counts, pay) does not reset it.
- Publishes a read-only `DeliverPreviewState { Branch, payload, uint DisplayedTicks, bool CanStartHoldNext }` with `CanStartHoldNext = displayedTicks ≥ branchSettleTicks` (6, from content).
- A Deliver press in 4d starts a hold only if the tracker allowed it, and latches `(destinationId, branch, selectedEntryId + slot, holdTicks = HoldEff(...))`.
- `holdStartTick` in `deliver_matching` is `IHostClock.EstimatedHostTick` read at latch time (MD5).

### 5. Presentation timing: predictive visuals vs outcome beats (TR-throwing-018, TR-delivery-017/018, TR-hud-003)

**`PresentationEventStream`** (Client.Core).
- `ClientAdapter` publishes each decoded batch to Presentation **in the frame it arrives**: the same decoded entries `MirrorDrain` applies at the next client tick.
- Entries are ordered `(sourceTick, ordinal)`. HUD's `opId` is the event's `Ordinal` within its tick (GDD naming: see GDD impact).
- Consumers (the `IHudState` implementation, view controllers, audio) each hold a cursor. No consumer re-sorts. An entry is never delivered twice to a cursor, so "an `opId` is never rendered twice" (H30).

**Two classes of visual.** Every Presentation component declares one.
- **Predictive** (allowed before the host decides):
  - deliver prompt states, ✓/✗, counts, the "+N¢ / −N¢" labelled predicted;
  - reach and ring progress, scrubbed by `heldTicks/holdTicks`;
  - the neutral slot-in, the aim ribbon and arc colour;
  - the launch ghost (spawned on the release frame from the hand socket), the Catch prompt;
  - the static-landing settle and the mailbox teeter.
  - Inputs: the published ClientLayer states (§4, throw preview), own input, `IRenderClock`.
- **Outcome beats** (only on a domain event):
  - hand beat, cha-ching, landing sfx, flag, wobble, resident;
  - complaint, wallet change, outcome figures, catch success, WorldItem appearance.
  - Triggered **only** by a cursor reading the matching domain event (`DeliveryOutcome`, `FlightResolved`, `WalletChanged`, `ComplaintRaised`, inventory seals). A beat may be delayed (for example Mail's `max(0.2 s, outcome)`) but never advanced. Nothing in the predictive path may start one.

**Specific rules.**
- **Hand beat:** once per hold, keyed `(actorSlot, destinationId, applyTick)`. The beat controller keeps a 16-entry recent-key ring; a hold's k `DeliveryOutcome`s with one key fire one beat.
- **Reach echo:** starts only if `CanStartHoldNext` was `true` in the **last completed** client tick's published state; Presentation never recomputes it. It withdraws within 0.1 s when the reply refuses.
- **Launch ghost:** cosmetic. It reconciles to `ThrowLaunched` per Throwing (lerp over 3 ticks, blend, snap on `originClamped`, hold ≤ 600 ms). It never produces a landing beat; only `FlightResolved` does.

**Enforcement.**
- A Client.Core architecture test: types tagged `[OutcomeBeat]` may receive data only from `PresentationEventStream` cursors.
- An integration test runs loopback through a `DelayTransport` (fixed 3–10 tick delays) and asserts that every recorded outcome-beat start frame is ≥ its event's arrival frame.

### 6. One render clock, several timelines (TR-hud-004, TR-throwing-016)

`IHostClock` (ADR-0011) is **not** amended. Client.Core adds:

```csharp
namespace PerformativeMail.Client.Core.Timing;
public interface IRenderClock
{
    uint HostTick { get; }            // IHostClock.EstimatedHostTick at frame start (host: SimWorld.CurrentTick via the host clock impl)
    int  AlphaPermille { get; }       // TickDriver.AlphaPermille (ADR-0002), 0..999
    long RenderSimTimeMs { get; }     // ((long)HostTick * 1000 + AlphaPermille) / 30  — integer, floor
    bool IsValid { get; }             // IHostClock.IsValid
}
```

- `RenderClock` is a Client.Core class sampled once per frame by the host shell after `RunFrame`, before `Present`.
- On the host it is exact Sim time with no slew and no step (HUD F3). On a remote client its smoothness is the Networking estimator's (M0).
- On tick boundaries `RenderSimTimeMs` equals Sim Core F1 `displaySeconds × 1000` (unit test). The HUD runs no clock of its own.

**Timeline registry** (each timeline is derived from `IRenderClock`, never from `delta`):

| Timeline | Rule | Owner |
|---|---|---|
| Own carrier | present, ≤ 1-tick interpolation by `AlphaPermille` | ADR-0002 / this ADR |
| Remote bodies | `RenderSimTimeMs − interpDelayMs` (160), extrapolate ≤ 100 ms | ADR-0020 (slot reserved) |
| Flights (all, incl. own after reconcile) | `HostTick − flightRenderDelayTicks` (3) + alpha; a late `ThrowLaunched` is fast-forwarded by `Ballistic.PositionAt(age)`; the own ghost leads | this ADR (+ ADR-0021 wire) |
| HUD timers | `RenderSimTimeMs` (F3) | ADR-0018 |
| Holds / settle | client Sim ticks | §4 |

Flights contribute **0 B** to JoinState: `FlightMirror` is rebuilt only from events, and a joiner does not see flights already in the air (accepted; Throwing L1095).

### 7. Budgets and allocation
- Advisory until Q-ARCH-10: `ClientLayer.Step` ≤ 0.3 ms p99 per client tick, with `CarrierStep` + 4a ≤ 0.10 ms.
- 4b scans ≤ 26 entries and calls `Evaluate` (≤ 6 predicates) per scanned unit, every focused tick, with no cache (MD3). An evaluation counter and a scanned-entries counter are test seams (M-MD3-3).
- 4c recomputes only when its quantised cache key changes.
- 0 B allocated per `Step` after 300 warm-up ticks (same gate shape as ADR-0006's tick gate; advisory M-1a, blocking M0).

### Architecture Diagram

```
FrameRunner (ADR-0002), per tick i of this frame:
  ClientLayer.Step(tick, isFirst = i==0)                       [Sim/ClientLayer]
    1 MirrorDrain ── IClientInbox.Drain ──► IMirrorPart.Apply (Sim parts + Client.Core ContainerMirror)
    2 InputLatch  ── InputFrame (edges on first tick only)
    3 CarrierStep ── owned carrier (ADR-0013)
    4 Previews    ── 4a Focus → 4b DeliverPreview+Tracker → 4c ThrowPreview → 4d EdgeResolve → 4e HoldAdvance
                         │ pure fns (Evaluate, F6/F7/F10, F2/F4, HoldEff) over *facts*      │
                         └──────────── IRequestSink.TrySend(op) ──► ClientAdapter ──► loopback/remote
  SimWorld.Tick()   (host only)
After RunFrame:  RenderClock.Sample() → IPresentationRoot.Present(alpha)
  Views/HUD: predictive ◄── published ClientLayer states    outcome beats ◄── PresentationEventStream cursors
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.ClientLayer;
public enum PreviewSubStep : byte { Focus, DeliverPreview, ThrowPreview, EdgeResolve, HoldAdvance }
public readonly struct ClientPorts { public readonly IClientInbox Inbox; public readonly IPanelView Panel;
                                     public readonly IRequestSink Requests; public readonly IHostClock HostClock; }
public interface IClientInbox { void Drain(IMirrorSink sink); }
public interface IMirrorSink  { void OnEntry(in EventEntry e, ReadOnlySpan<byte> blob); void OnReply(in Reply r); }
public interface IMirrorPart  { void Apply(in EventEntry e, ReadOnlySpan<byte> blob); void ApplyReply(in Reply r); }
public interface IPanelView   { int EntryCount(PanelSection s); PanelEntryFacts Entry(PanelSection s, int i); int CursorSlot { get; } }
public interface IRequestSink { bool TrySend(in Op op); }   // false → local "busy" (no op sent)
public sealed partial class ClientLayer { public void Attach(in ClientPorts ports); /* once, before first Step */ }
public sealed partial class ClientLayerBuilder
{ public void BindPreview(PreviewSubStep s, IClientStep step); public void BindMirrorPart(EventKindSet kinds, IMirrorPart part); }

namespace PerformativeMail.Sim.Delivery.Client;
public readonly struct DeliverPreviewState { public readonly DeliverBranch Branch; public readonly uint DisplayedTicks;
    public readonly bool CanStartHoldNext; /* + MATCH / MISDELIVER_ONE payload (counts, Cents predicted, flags) */ }
public interface IDeliverPreviewRead { ref readonly DeliverPreviewState Current { get; } }   // read by Presentation

namespace PerformativeMail.Client.Core.Presentation;
public sealed class PresentationEventStream { public EventCursor OpenCursor(EventKindSet kinds); }
public struct EventCursor { public bool TryRead(out PresentedEvent e); }   // (sourceTick, ordinal) order
[AttributeUsage(AttributeTargets.Class)] public sealed class OutcomeBeatAttribute : Attribute { }
```

### Implementation Guidelines
- Client steps **must** live in the Sim (`<System>/Client/`) and **must** read only facts built from mirror parts or ports. They **must never** reference `SimWorld` or any host-side system instance.
- Shared rule functions **must** be `static`, pure and 0-alloc, taking facts structs or spans. Client.Core and the Godot project **must never** reimplement a rule, a rounding or a ballistic formula; they call the Sim function.
- Edges **must** be resolved only in `PreviewSubStep.EdgeResolve`, after 4a–4c of the same tick. `InputLatch` **must not** dispatch verbs.
- `DeliverPreviewTracker` **must** count client Sim ticks. Presentation **must never** derive `CanStartHoldNext` itself.
- Outcome-beat components **must** be tagged `[OutcomeBeat]` and fed only from `PresentationEventStream` cursors. Predictive components **must never** start an outcome beat.
- Presentation time **must** come from `IRenderClock`. `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned for gameplay-facing timing (ADR-0002).
- `IHostClock` **must not** gain members through this ADR. Render time is derived in Client.Core.

## Alternatives Considered

### Alternative 1: Run previews in Presentation (Godot) with a C# copy of the rules
- **Description**: Views compute prompts and arcs each frame by calling helper classes in Client.Core.
- **Pros**: Per-frame responsiveness; no Sim-side client code.
- **Cons**: MD4 explicitly forbids it ("never Presentation"). Frame-rate-dependent settle. A second copy of the rules invites drift that M-INT-2 would catch only after it happened.
- **Rejection Reason**: Violates MD4 and principle 3 ("one code path").

### Alternative 2: Put the ClientLayer steps in Client.Core
- **Description**: Move preview steps and mirrors to Client.Core, which already holds `ContainerMirror`.
- **Pros**: No ports needed for container state.
- **Cons**: ADR-0003 composes `ClientLayer` in the Sim and fixes its step ids there. The Sim's float/clock analyzers would not cover client rule code. Client.Core may use wall clocks, a risk for tick-counted settle.
- **Rejection Reason**: Contradicts ADR-0003 §6 and weakens determinism enforcement. Ports cost one interface each.

### Alternative 3: Add a sixth top-level client step (`EdgeResolve`) after `Previews`
- **Description**: Amend ADR-0003's `ClientStepId` enum.
- **Pros**: Visible in the top-level order.
- **Cons**: Amends an Accepted ADR, and focus and holds would still need ordering inside `Previews`.
- **Rejection Reason**: A sub-order inside `Previews` achieves the same order without reopening ADR-0003.

### Alternative 4: Add `RenderSimTimeMs` to `IHostClock`
- **Description**: Amend ADR-0011's interface as HUD H30/F3 literally ask.
- **Pros**: One interface.
- **Cons**: It would put `AlphaPermille` (Presentation-only, ADR-0002) into a Sim-defined interface, and it amends an Accepted ADR.
- **Rejection Reason**: Deriving ms in Client.Core keeps the Sim free of frame-time data. The GDD wording is synced instead (GDD impact).

## Consequences

### Positive
- Preview == outcome holds by construction (same function, same facts shape) and is checked by M-INT-2 and Throwing feel test 1.
- MD's press-tick semantics and ADR-0003's step ids no longer conflict.
- One audited path for outcome beats makes "never before the domain event" testable with a delay transport.
- HUD, flights and remote bodies share one clock source. No view runs its own clock.

### Negative
- Four small ports and `ClientLayer.Attach` add composition ceremony for the host shell and tests.
- Facts structs duplicate a few fields (address, kind, state) already held by the mirrors. This is accepted for the purity guarantee.
- A predictive/outcome tag on every Presentation component is a review burden.

## Risks
- **Facts drift**: a field added to the host facts but not the client facts makes previews diverge. *Mitigation:* one facts constructor per side is unit-tested against the same fixture; M-INT-2 runs in CI.
- **`IPanelView` implemented in Client.Core but read inside the Sim tick**: a Client.Core bug can break a Sim-side step. *Mitigation:* the port is read-only; ADR-0009's seal staging guarantees no torn state; tests use an in-memory `IPanelView`.
- **Sub-step order dependence on ADR-0014**: if ADR-0014 places focus elsewhere, 4b reads stale focus. *Mitigation:* the Ordering Note binds ADR-0014 to `PreviewSubStep.Focus`; a composition test asserts the sub-order.
- **Remote client render time jumps** when the estimator corrects. *Mitigation:* Networking's estimator owns slewing (M0); HUD F3's "no step" is guaranteed on the host only. Stated here, synced to the HUD GDD.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| manual-delivery.md | MD3 / TR-delivery-003: preview pure function every focused tick in ClientLayer `Previews` | §2 sub-step 4b; §3 facts |
| manual-delivery.md | MD4 / TR-delivery-004: tracker 6-tick settle in Sim ticks | §4 |
| manual-delivery.md | TR-delivery-005: events/replies → focus → preview → resolve Interact | §2 (1 → 4a → 4b → 4d) |
| manual-delivery.md | MD4 / TR-delivery-006: press-tick recompute, latch (dest, branch, entry, holdTicks) | §2 4d; §4 |
| manual-delivery.md | M-INT-2 / TR-delivery-012: client `Evaluate`/F10 equal host over 1,000 seeded pairs | §3 compiled once + facts; Validation |
| manual-delivery.md | F1 / TR-delivery-013: integer cents, rounding per unit as F10 | §3 preview money |
| manual-delivery.md | TR-delivery-014: scan order equals host consumption | §3 `DeliverScan.Enumerate` shared |
| manual-delivery.md | MD7 / TR-delivery-017: hand beat once per hold keyed (actor, dest, applyTick) | §5 |
| manual-delivery.md | TR-delivery-018: reach echo only when Sim-published `canStartHoldNext` | §4 published state; §5 |
| mail-destinations.md | R5 / TR-mail-005: `Evaluate` pure, 0-alloc, same code in client | §3 |
| throwing-catching.md | TR-throwing-016: flights 0 B JoinState; remotes at hostTickEst−3, fast-forward via F2 | §6 timeline registry |
| throwing-catching.md | TR-throwing-018: launch ghost on release frame; outcome beats never before domain event | §5 |
| hud-notifications.md | H30 / TR-hud-003: HUD reads only `IHudState` ordered by (sourceTick, opId), loopback from M-1a | §5 `PresentationEventStream` (consumed by ADR-0018's `IHudState`) |
| hud-notifications.md | F3 / TR-hud-004: timer from `renderSimTimeMs`; HUD runs no clock | §6 `IRenderClock` |

## Performance Implications
- **CPU**: ≤ 0.3 ms p99 per client tick in total (advisory); 4b dominates in a Deliver focus (≤ 26 entries × ≤ 6 predicates).
- **Memory**: mirror parts are preallocated at `Attach`; the beat-key ring is 16 entries; there are no per-tick allocations.
- **Load Time**: none.
- **Network**: none added. Previews are local; requests are unchanged (MD5, ThrowRelease).

## Migration Plan
No code exists. Order:
1. ClientLayer ports + `Attach` + `PreviewSubStep` composite (simulation-core).
2. `DestinationMirror`/`WalletMirror`/`RunMirror` and `ContainerMirror` confirmed state (M-1a).
3. Deliver preview + tracker (manual-delivery).
4. Throw preview + `FlightMirror` (throwing-catching).
5. `PresentationEventStream` + `RenderClock` (godot-host-shell / HUD).

`PendingQueue` replay into `IPanelView` lands at M0 (ADR-0009).

## Validation Criteria
- M-INT-2: 1,000 seeded (destination state × unit) pairs including CLOSED, LOCKED and `wont_fit` give equal results through the host consume path and the client facts path; with no lag, `predictedPay` equals Σ outcome pay.
- Throwing feel test 1: over 10,000 static throws, the preview end point and class equal the host's.
- Tracker: a branch displayed at T is holdable from T+6 (M-MD4-1); a payload-only change does not reset it.
- Delay-transport test: every `[OutcomeBeat]` start is ≥ its event's arrival frame. The hand beat fires once per hold for k outcomes.
- `RenderSimTimeMs` equals F1 `displaySeconds × 1000` on tick boundaries; it is monotonic on the host.
- Composition test: the sub-step order is Focus → DeliverPreview → ThrowPreview → EdgeResolve → HoldAdvance. The arch test confirms no `Sim/*/Client` type references `SimWorld`.
- 0 B per `ClientLayer.Step` after warm-up (net8.0 leg).

## GDD Impact (do not edit GDDs here; reconcile at re-review)
- **hud-notifications.md H30/F3:** "`renderSimTimeMs` from Sim Core's `IHostClock`" → from Client.Core `IRenderClock`, derived from `IHostClock.EstimatedHostTick` + `AlphaPermille`. "No slew, no step" holds on the host; on remote clients it is the estimator's property. HUD `opId` = event `Ordinal` within `sourceTick`.
- **simulation-core.md HB9** calls the same value `renderMs`; rename it to `RenderSimTimeMs`.
- **simulation-core.md R2 / input-controls.md G1:** Interact press resolution happens in `Previews.EdgeResolve`, not in `InputLatch`. Input R11/L583's "drained by `Inputs.Apply(tick)` on the owning client" should read `InputLatch` (`Inputs.Apply` is host phase 0).
- **manual-delivery.md Game Feel L394** "same or next tick" → exactly the next client tick (Sim Core N2).
- **player-character-movement.md P2** names the client step `Players.Step`; it is `CarrierStep` (host phase 3 is `PlayersStep`).

## Related
- ADR-0002, 0003, 0008, 0009, 0011 (Accepted dependencies); ADR-0012, 0013, 0014, 0015, 0017 (sibling decisions); ADR-0018 (HUD), ADR-0019 (views), ADR-0020/0021 (remote timelines).
- `design/gdd/manual-delivery.md` MD3–MD7, F1, Game Feel; `throwing-catching.md` T3/T4, Game Feel, Network; `mail-destinations.md` R5/R6, Game Feel; `hud-notifications.md` H30, F3; `simulation-core.md` R2, N1/N2.
