# Simulation Core

> **Status**: In Review (Provisional) — `/design-review` 2026-10-16 MAJOR REVISION NEEDED (11 blocking); revision applied the same day, awaiting a fresh re-review. Scope ruling: 1,772 → ≤ 1,050 lines and 131 → ≤ 55 ACs. Algorithms, layouts, build mechanics and perf slices were moved to `docs/architecture/simulation-core-adr-inputs.md` (UNREVIEWED SEED).
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 (6 text fixes: slow-motion is the only time distortion; test 3 cites F2 'no time is created'; R17 slip rationale; Pillar 1 claim = accepted op applies exactly once (HB10 to Networking); seed anchor requires same crew size; test 2 given a 1.5× threshold and latency routing)
> **Provisional**: designed before the M-1a two-friend greybox (systems-index GUARD overridden by owner instruction 2026-10-16); re-validate after the greybox.
> **Author**: owner + agents (autonomous mode, /design-system; revised by /design-review)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Implements Pillar**: all five (see Player Fantasy → Pillars served); MDA Fellowship (#1).

## Summary

Simulation Core is the invisible clock and referee behind every gameplay system. It advances the game in fixed 30-per-second ticks and runs every system's step in one published order. It owns the shared rules the systems agree on: one queue for player actions, one ordered log of what happened, ids that never repeat, seeded randomness and whole-cent money.

For players, this is why a misdelivery or a same-tick tie resolves the same way for all eight of them and for the host. It is also why a seed replays and why a full factory does not stutter the clock.

It exists because spec 07 mandates a plain-C# simulation with no Godot dependency, and because seven other GDDs already depend on its contracts: the phase order, the op queue, the event order, the ids, the RNG streams, the tick driver and the tick budget.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP (real files from M-1a; full at M0)` · Key deps: `None upstream. Downstream: Content & Data, Grid Inventory, Player Character, Input & Controls, Mail & Destinations, Manual Delivery, Throwing & Catching, HUD (soft), and every undesigned Sim-side system`

## Overview

Simulation Core is the root of the plain-C# `PerformativeMail.Sim` assembly (spec 07 §1). A `SimWorld` advances in fixed 30 Hz steps (one *tick* = 1/30 s) and runs every gameplay system's step in one published order each tick. It owns the shared services those systems need to agree with each other:
- the tick counter and tick-based timers;
- the single ordered queue of player operations (`Inputs.Apply`);
- the in-process event log, whose ordered output is what gets replicated;
- entity-id allocation and named deterministic random-number streams;
- integer money and fixed-point helpers;
- the state dump that proves two runs given the same seed and inputs come out identical.

It contains no gameplay rules of its own and no Godot types. Godot sees it only through a thin *frame runner* (which decides when ticks run) and an *adapter* (which feeds inputs in and carries events out).

Players never touch it directly. They feel it as a game that is *fair and legible*. A letter thrown on the same tick as a teammate's hand delivery resolves the same way every time. A deadline shown as "0:12" means exactly 360 ticks. A seed replays the same mail manifest. Eight players on a full factory do not stutter the clock.

This GDD states **behaviour and contracts**: what a tick does, in what order, and what each service guarantees. How each is built (byte layouts, algorithms, thread model, `csproj` shape, perf slices) is input to the nine ADRs listed in Open Questions. That material is kept, unreviewed, in `docs/architecture/simulation-core-adr-inputs.md` (the **seed**). On any conflict this GDD wins.

## Player Fantasy

*Framing: **indirect**. The player feels what this system does, never the system itself. Shaped by the creative-director: candidate 2, "Same tick, same story", with candidate 1, "The clock never lies", folded in as its proof.*

Simulation Core is never seen, but it is the reason the crew can trust what they saw. Its fantasy is **one shared story**. Whatever happened on the island happened the same way for everyone, so a misdelivery is a joke the crew tells together, not an argument about whose screen was right. The game's humour depends on misdelivery risk (Pillar 2, *Friendslop surface, factory depth*: "a bad throw is never softened"). A misdelivery is only funny if all eight players agree it really happened. If the outcome could be blamed on the host, on lag or on a hidden reroll, the laugh turns into blame. Fellowship is the #1 aesthetic in `game-pillars.md`, and it does not survive that.

**Anchor moments**

- Two carriers deliver to the same door on the same tick. The game decides who wins by a published order (Core Rules R3), and decides the same way every time.
- "0:12" on the closing clock means exactly 360 ticks, for the host and for every client (Formulas F1).
- A crew shares a seed, and another crew of the same size gets the same first-shift mail manifest (R24; Mail & Destinations).
- Eight players run a full factory and the clock does not slow down (Formulas F3).

**The player must never feel**

- that the game cheated, or rerolled their luck behind their back;
- "it worked on the host": host and clients play by the same rules (R2, one code path for the host loopback);
- that world time skipped, burst forward or snapped back. Under a severe host hitch it may only slow, and it slows for everyone together (R17, F2);
- that a bad throw was softened, or a good delivery quietly eaten.

This section promises consistency of the **world clock and resolution order** only. How a player's own carrier feels under latency is Player Character & Movement's promise, because clients own their movement (spec 13 §3).

**Pillars served**: *The mail must flow* (every accepted op applies exactly once, in one published order; a refused op is answered `busy`, never silently lost), *Friendslop surface, factory depth* (honest outcomes keep the joke funny), *Hands beat machines* (a player op beats an inserter on the same tick, R9), *Short runs, long game* (seeds replay), *Scales from 1 to 8* (the tick budget holds at eight).

**Fantasy tests** (playtest, M0 onward; PROPOSED thresholds; protocol OQ12)

1. After a contested same-tick delivery, ask each player separately who got it. At least 90 % of answers match the logged outcome, and nobody reports "it counted for them, not me".
2. Across at least 5 sessions with at least 10 disputes logged in total, the per-player "that wasn't fair" rate on clients is no more than 1.5× the rate on the host. A failure caused by latency rather than by a rules difference routes to Networking & Session (lag handling), not to Sim Core.
3. In 8-player full-factory sessions, outside injected or logged host hitches, nobody reports the clock jumping or the game slowing down. The displayed run clock never advances faster than real time (F2, "no time is created") and never jumps by more than 4 ticks in one frame (the catch-up cap).

## Detailed Design

*Round-1 specialists: systems-designer (primary), engine-programmer, godot-csharp-specialist, network-programmer. Review specialists 2026-10-16: systems-designer, lead-programmer, godot-csharp-specialist, network-programmer, qa-lead, creative-director, technical-director. Choices were made in autonomous mode and logged in `production/session-logs/decision-log.md`. Deviations from spec 07 are tagged **DEV-n** (listed under Interactions). Godot behaviour not in `docs/engine-reference/godot/` is **UNVERIFIED** and routed to the Technical Setup spike and ADRs. Nothing here relies on it being true.*

### Core Rules

#### Tick and roles

**R1 — The tick.** The authoritative simulation advances only in whole ticks of exactly 1/30 s (`TickRateHz = 30`).
- `Tick` is a `uint32` owned by `SimWorld`. The first `Tick()` after `Ready` processes tick 0, which is the first tick of shift 1's Prep.
- During `Tick()`, `CurrentTick` is T. Afterwards it is T+1, the next tick to run.
- A `uint32` lasts about 4.5 years at 30 Hz, so comparisons use plain `<`/`≥`. Debug asserts `tick < 2^31`. Tick *differences* are computed in `int64` (F1).
- There is no `SimWorld` in the lobby. One is built at run start from `SimSettings` + `ContentSet` + `runSeed`. A new run builds a new one, and the tick restarts at 0.
- Variable-step simulation is forbidden (spec 07 §1.4).

**R2 — Two roles, one assembly, one code path.** The Sim assembly contains two composed objects. They are not a mode flag.
- **`SimWorld` (host role)** is the single authoritative world. It runs the phase list (R3) and is the only place gameplay state is decided (spec 13 §3).
- **`ClientLayer` (client role)** runs in every client process, including the host's own. It is a shell. Once per client tick it runs bound `IClientStep`s in a fixed `ClientStepId` order:
  1. `MirrorDrain` (host event batches and snapshot state into per-system mirrors);
  2. `InputLatch` (the input frame, Input & Controls R11);
  3. `CarrierStep` (Player Character's `OwnedCarrier.Step`);
  4. `Previews` (for example Manual Delivery's tracker and Throwing's flight query).
- Owning systems bind client steps through `ClientLayerBuilder`. Sim Core never names a gameplay type.
- The Sim assembly also holds each system's **pure functions** (`holdEff`, Mail `Evaluate`, the flight query), so host and client call the same code.
- `ClientLayer` never decides authoritative state. **It reads only its mirror, never `SimWorld`**, even on the host. On the host, `IRemoteCapsuleSource` therefore returns tick T−1's ingest, exactly like a remote client with zero latency.
- **The host process** runs one `SimWorld` and one `ClientLayer`, connected through the same op encoder and a loopback adapter. The host gets no shortcut, so serialisation bugs show up for the host too (spec 07 §3).
- **One `FrameRunner` per process** (R17). Each frame it runs, for `i` from 0 to `ticksRun − 1`: `ClientLayer.Step()` first, then `SimWorld.Tick()` if this process hosts a world. Input edges attach when `i == 0`.
- A dedicated server (deferred, spec 13 §2.2) would be a `SimWorld` with no `ClientLayer`.

#### Phase order

**R3 — The phase list (authoritative).** `SimWorld.Tick(T)` runs exactly these phases in this order. Each has a stable `PhaseId`, and no other GDD may introduce a phase without amending this table.

| # | PhaseId | Owner | What runs | Why here |
|---|---|---|---|---|
| 0 | `Inputs.Apply` | Sim Core drains; each op kind's handler is owned by its system | The op FIFO, up to the drain cap (R6–R8): hand deliveries, inventory ops, throw releases, interaction, build/shop/draft requests, join/leave (System actor) | Every player request applies before anything else this tick. It wins pile-expiry and knockdown races (Grid Inventory R12, Player Character P15, Throwing). |
| 1 | `Items.Expire` | Grid Inventory | WorldItem expiry and cap eviction | A pickup applied in phase 0 on the expiry tick wins (Grid Inventory R12). |
| 2 | `Intake.Drain` | Grid Inventory | Backlog drain, ≤ `backlogDrainPerTick` (32) | "After `Inputs.Apply`" (Grid Inventory R14). |
| 3 | `Players.Step` | Player Character | Host role only (R5) | Before any phase that range-checks player positions. |
| 4 | `Throwing.Step` | Throwing & Catching | Flight sweeps and landings, in flight-creation order | Mail R6: hand → **landing** → feeder → NPC. |
| 5 | `Automation.Step` | Belt Logistics | Belts, splitters, sorters, inserters, depots. Feeder offers in ascending construct id. | Mail R6: feeder before NPC. **DEV-1** |
| 6 | `Vehicles.Step` | Vehicles & NPC Drivers | Vehicle model, NPC drop offers in ascending vehicle id | Mail R6. Reads NPC-driver intent from phase 7 of T−1. |
| 7 | `Agents.Step` | Enemy AI & Waves | Enemy and NPC-driver behaviour; attacks queue damage | Spec 07 order kept for agents. |
| 8 | `Mail.Step` | Mail & Destinations | Batch spawns at absolute ticks, backlog append, deadlines | New batches enter the Intake. Any backlog is placed by phase 2 of T+1. |
| 9 | `Economy.Flush` | Economy, Wallet & Shop | Shop and queued purchases; aggregated wallet events | Wallet credit and debit are **synchronous** `IEconomyApi` calls made where they happen. This phase only flushes. |
| 10 | `Combat.Resolve` | Health & Damage | Pending damage, deaths, ruins, drops; raises `PlayerDied` | After `Inputs.Apply`, so a request applied on the knockdown tick runs first. |
| 11 | `Combat.React` | Every system that reacts to a death (Player Character P15 first) | Pulls phase 10's entries through declared cursors; one `PlayerDied` per carrier is acted on | Same-tick knockdown without synchronous subscribers. **DEV-8** |
| 12 | `Run.Step` | Run Structure | Phase timers, quota checks, raid trigger, `RequestStop` | **Last gameplay phase.** A flag it sets in T takes effect from T+1 (Mail R5). |
| — | tick end | Sim Core | Bound `ITickEndCheck`s: per-tick invariant checks (for example Grid Inventory R19) | After the last mutating phase. Nothing here mutates state or raises events. |
| 13 | `Events.Flush` | Sim Core | Hands the tick's ordered log to the sink (R12) | The only exit point |

- Tick end is not a `PhaseId`. `PhaseId` values are 0–13, and tick end is a hook between 12 and 13.
- Order *inside* a phase is the phase owner's rule, and the owner states it in its own GDD. When several systems bind one phase, they run in **composition order** (R4).
- **Reactions must not raise damage.** Damage raised in phase 11 or later applies on T+1, through Health & Damage's pending list.
- A phase with no bound system is a no-op, so an unbuilt system costs nothing and breaks nothing.

**R4 — Binding and composition.**
- **Binding.** These builder calls are made before `Freeze()`:
  - `SimWorldBuilder.Bind(PhaseId, ISimPhase)`. Several systems may bind the same phase; they run in composition order.
  - `BindTickEnd(ITickEndCheck)`, run in composition order.
  - `BindOp(OpKindDescriptor, IOpHandler)`. The descriptor declares the payload type, the wire codec and the handler's reply reasons (R10).
  - `BindEventKind(EventKindDescriptor)`, which declares the replication class (R12) and coalescibility.
  - `BindReader(cursor, kindFilter)`, at most `maxEventReaders`.
  - `BindStateContributor(ISimStateContributor)` (R30).
  - `BindSnapshotSource(ISnapshotSource)` (N1).
  - These are build-time errors and throw `SimBuildException{Code}`: two handlers for one `OpKind`, an `OpKind` with no handler, a 9th reader, any bind after `Freeze()`, and a dependency cycle. At `Freeze()` all tables are frozen; there is no runtime add or remove.
- **Cross-system calls.** Each system publishes one `I<System>Api` holding only its documented cross-system methods.
  - One composition root, `SimComposition.Build(profile)`, lives in its own `Composition/` folder (not `Core/`). It constructs systems in topological order of the systems-index dependency map.
  - It hands each system, by constructor, only: `EventLog`, `EntityIds`, `MailIds`, its own `RngStream` handles, a tick reader, `IRunControl` (Run Structure only), and the `I…Api`s it depends on.
  - A `profile` selects which systems a milestone or test binds.
  - There is no service locator, no static, and no `SimWorld` reference inside a system.
  - Calls go *down* the dependency map; information goes *up* through events. Run Structure is the one orchestrator (systems-index cycle rule).
  - A reflection test checks every system constructor's parameters against the allowed-dependency list.
- **`IRunControl.RequestStop()`** is the only way gameplay ends a world (States).

**R5 — `Players.Step` is a host-role phase (DEV-3).** Spec 13 §3 makes movement client-authoritative, so the host **never steps an Alive remote player**. In this phase it:
1. ingests the transform samples drained into the latch since the previous tick;
2. applies Player Character's Teleport `seq` discard rule;
3. appends per-player position history (Player Character owns its depth);
4. steps host-authored bodies: Dead and Frozen bodies, respawn and Frozen timers (PCM P19);
5. updates the stale and dead-reckoned flags.

Range-gated requests applied in phase 0 of tick T see history through T−1. Player Character's 300 ms range slack covers this.
- **Sample stamping.** Samples are not ops. The adapter enqueues them into a sample ring (thread rules as R7). The ring is drained into the latch **between ticks, immediately before the tick that ingests them**, and each drained sample is stamped `receivedTick` = that tick. Stamping never happens in a transport callback. Each sample carries a per-sender sequence number, and the latch keeps every sample in sequence order, discarding older-or-equal ones.
- **Teleport.** `IPlayersApi.Teleport(player, pos, yaw, reason)` is a synchronous API callable from any mutating phase. It bumps `seq`, clears history, seeds one sample, and raises an `OwnerOnly` `TeleportIssued` event. Samples carrying an older `seq` are discarded in phase 3. `TeleportAck` is an `OpKind`.
- **Recording.** Ingested samples are recorded through `IOpRecorder.OnSample(receivedTick, slot, in TransformSample)` **from M-1a** (`TransformSample` is a Core-owned blittable type). A T1 replay therefore reproduces range checks (R29).

#### Operations (`Inputs.Apply`)

**R6 — What an op is.** An op is a fixed-size blittable value: a header (`opSeq`, `actor`, `kind`, `reqId`) plus a typed payload of at most 48 B. The layout is in seed §3.1. Each `OpKind` registers its payload type in its descriptor, and a unit test asserts the size bound for every kind.
- **`actor`.** An `ActorId` is `EntityId`-width. A player is their `player` id, a construct is its `construct` id, and `System` is the reserved value `0x00000001`. The adapter sets `actor` from the transport peer, never from a payload field, so clients cannot impersonate.
- **`opSeq`.** `SimWorld.EnqueueOp` stamps it. It is monotonic per world, never reused, and never wraps in a run (F4). It is the single "arrival order" key that Grid Inventory R7, Mail R6 and Manual Delivery MD6 refer to. Grid Inventory's `arrivalSeq` is a different thing: its per-entry Intake counter.
- **`reqId`.** It is opaque to the Sim: echoed in the reply and never interpreted. Its wire width belongs to the owning protocol (Grid Inventory rule 8: u16 per connection, wrapping).
- **Not an op.** Continuous per-tick intent (the input frame) never enters `SimWorld` (R2, **DEV-2**).

**R7 — The queue.** One preallocated ring of `opQueueCapacity` ops.
- **Full ring.** `EnqueueOp` returns `false` and consumes no `opSeq`. The op is not recorded. The adapter answers the requester `busy` **on the same reliable channel as that peer's replies, immediately**, and a counter increments. Clients match replies by `reqId`, never by position.
- **System reserve.** Player ops are refused at `opQueueCapacity − systemOpReserve`. `System` ops (`PlayerJoin`, `PlayerLeave`, `FreezePlayer`) may use the whole ring. A `System` op refused on a completely full ring is an always-on check failure (R31).
- Enqueue is thread-affine to the Sim thread (Debug assert; the thread is captured at `Freeze()`). A transport whose callbacks arrive off-thread must marshal (R18).
- The host's own client uses the same `EnqueueOp` through the loopback adapter.

**R8 — The drain.** At the start of phase 0 the queue captures `n = min(count, maxOpsDrainedPerTick)`. It applies those `n` ops in ascending `opSeq` through the bound handlers. Each handler has one method: `Apply(in Op, EventLog) → Reply`.
- **Cap.** Ops beyond the cap stay queued, in order, for the next tick. A `drainBacklog` counter records how many were left. `System` ops get no bypass. The cap is in `SimSettings`, and so in the replay header.
- **Timing.** Ops enqueued during the drain, or between ticks, apply on a later tick.
- **Unknown actor or kind.** An op from an unknown or departed actor is rejected `unknown_actor`. An unregistered `kind`, or a payload that does not match its descriptor, is rejected `unknown_kind`.
- **Atomicity.** One op is the atomic unit.
  - A handler validates fully before its first mutation, id allocation or RNG draw, and raises events only on commit.
  - A reject changes no state, no allocator and no RNG, and raises nothing except its reply. Sim Core guarantees its own part: `opSeq` consumed, recorder called, reply appended. Each owning GDD tests its handlers' part.
- **Composite actions.** A composite action is **one** `OpKind` whose handler calls documented APIs in a fixed order (a throw; Player Character P13 then `deliver_matching`). A client must never rely on two ops applying together.
- **Recording.** Every *drained* op, accepted or rejected, is offered to the optional `IOpRecorder.OnDrained(applyTick, in Op, result)`. The recorder also stores each op's enqueue boundary tick, so a replay can re-enqueue at the same boundary and assert the recomputed apply tick (seed §2 TD ruling). The M-1a recorder is in-memory; a file writer is deferred to M5 (spec 06 §9).

**R9 — Only requests use the FIFO; every in-tick caller acts in its own phase (DEV-6).** The op FIFO carries player and `System` requests only. Every in-tick system caller calls documented synchronous `I…Api` methods inside its own phase. That covers expiry and drain, Throwing's landings and catches, automation, Vehicles' NPC drops, `Mail.Step` and `Combat.React`.
- Each call is one atomic mutation. It bumps container versions and raises events exactly like an op handler does, but it produces no reply.
- Every `I…Api` mutation may be called from any phase except tick end and `Events.Flush`.
- The deterministic total order of a tick is therefore: phase order, then composition order, then the owner's order inside the phase.
- On a contested last free cell, a player op (phase 0) always beats an inserter (phase 5). A losing inserter retries next tick. This is Pillar 3, *hands beat machines*, and it amends Grid Inventory R7 (HB1).
- Putting automation in the FIFO would add a tick of latency to every inserter and would break Mail R6.

**R10 — Replies.** A handler returns `Reply {reqId, result, reason, extra ≤ 24 B}`.
- **Reasons.** `reason` is a u16 keyed by `(OpKind, reason)` and declared in the op descriptor (R4). 0 is ok. **1–63 are reserved for Sim Core**: `busy`, `unknown_actor`, `unknown_kind`, `slot_invalid`, `slot_busy`, `duplicate_player`, `join_closed`. Each system's reasons start at 64. `extra` carries, for example, Grid Inventory's `resultVersion` and `byActorId`.
- **Order.** Sim Core appends the reply to the log as kind `OpReply` **after** every event raised during that handler call. "Event first, then reply" (Grid Inventory; Throwing's `Removed → ThrowLaunched → ok`) therefore holds by construction.
- **Routing.** Replies are `OwnerOnly` to the actor's slot. A reply to the `System` actor has an empty audience and never leaves the Sim.
- **Duplicates.** Sim Core has **no generic `reqId` dedupe**. A handler that promises idempotence implements it itself and owns that state. Grid Inventory rule 8 does: the last 16 replies per connection, with a duplicate answered by the original reply. Manual Delivery M-NET-2 relies on that. Networking's main defence is never to resend a drained op (HB10).

#### Event bus

**R11 — One ordered log per tick.** Every event and reply raised during tick T goes into one preallocated append-only log. Each entry carries:
- `ordinal`, counted from 0 per tick;
- `kind`;
- `replicationClass`;
- `audienceMask` (one bit per `PlayerSlot`), resolved by the raising system **at raise time**, so a player who opens a container later never receives earlier events;
- a blittable payload with no strings (strings are `StringId`s).

Entry layout and the payload ceiling are seed §3.2 (ADR 6). Large state, such as a full container `replace`, is carried by reference to a snapshot block or chunked under the owner's rules, never by inflating the entry. After flush the log is cleared, so events do not survive a tick.

**R12 — Visibility and flush.**
- **No synchronous subscribers.** A later phase reads entries raised earlier in the same tick through its declared pull cursor (`Events.Read(ref cursor)`), filtered by kind. It never sees entries from a later phase. Events a reader raises append after everything already in the log. For a knockdown the order is `PlayerDied`, container close, `Removed`/spill, `CarrierKnockedDown`.
- **Flush.** In phase 13, `IEventSink.Deliver(tick, ReadOnlySpan<entry>, overflowInfo)` is called **once**, in raise order. This is the Sim's only output point.
- **What `Deliver` may do.** It **only copies** the span (or the entries it needs) into an adapter-owned preallocated buffer. Before returning, it **resolves each entry's `audienceMask` to a peer set**. It makes no transport call, no Godot call and no Sim call. `FrameRunner` sends after `Tick()` returns. A queued batch holds peers, never slots, so a reused slot never receives a leaver's events.
- **Replication classes.** Every event kind declares one in its descriptor (R4). An undeclared kind fails the enumeration test, and the fail-closed default is `Internal`.

  | Class | Delivered to |
  |---|---|
  | `Internal` | never leaves the process (for example `MailConsumed`) |
  | `Replicated` | all connected players |
  | `OwnerOnly` | the single player in `audienceMask` |
  | `Targeted` | the players in `audienceMask`, resolved by the owning system's subscription query (for example Grid Inventory's container viewers) |

- `Deliver` receives every entry, `Internal` included, so in-process tools see the full log. The adapter never sends `Internal` entries off-process, and does all per-peer filtering and packing.

**R13 — Coalescing.** Sim Core never merges events on its own. An owner may declare a kind `Coalescible` and rewrite its own entry in place through `ref Events.Update(index)`, **only within the same handler or API call that raised it**. Any later call raises a new entry. A rewrite therefore never moves state past a reply that already follows it, and batch order stays causal. The mechanics are in seed §3.2. This narrows Grid Inventory's "one `replace` per container per tick" to one per call (HB1g).

**R14 — Misuse and overflow.**
- **Raising outside `Tick()`, in tick end or during flush.** The event is dropped and counted (`eventsOutsideTick`, `eventsAtTickEnd`). Inside a tick this also sets `overflow`, because a missing event desyncs clients.
- **Log full** (`eventLogCapacity`). Admission has two tiers:
  - kinds with a declared cursor reader, and `OpReply`, may fill the whole log;
  - all other kinds stop at `eventLogCapacity − eventLogReserve`.
- **Dropped entries.** Each dropped entry is counted. The flushed batch carries `overflowInfo { overflow, droppedAudienceMask, droppedKindsMask }`. Each dropped entry's audience is ORed in; a dropped `Replicated` entry contributes all connected slots. State is **not** rolled back, because the handler already committed. No event is ever reordered.
- **What Networking does.** It resyncs exactly `droppedAudienceMask`, scoped by `droppedKindsMask`. It owns the cooldown and the scoping (HB10). A requester whose `OpReply` was dropped is in the mask, so its pending map is cleared by that resync.
- **Sizing.** With the drain cap (R8), the log is sized so it cannot overflow on the worst fixture tick (F8, SC-R8-4).
- A recording or replay that saw an overflow is marked non-reproducible, because capacity knobs decide which events drop.

#### Time, pause, driver and thread

**R15 — Timers are absolute ticks.** A timer stores `deadlineTick` and is due when `CurrentTick ≥ deadlineTick`.
- Seconds exist only in content and display, and F1 converts both ways.
- The run clock shown to players is `phaseDeadlineTick − currentTick` (spec 07 §4.1).
- Prep, Payday and Draft keep ticking. Only a solo pause stops time.

**R16 — Pause.**
- **Pause is a driver state, not a `SimWorld` state.**
- **Solo pause** (exactly one connected player, no remote peers): `TickDriver.Pause()` makes `Advance` return 0 and accumulate nothing, so `SimWorld` simply is not ticked. `Resume(timestamp)` clears the accumulator: no burst, no banked time.
  - Every absolute-tick timer freezes for free (Grid Inventory D6, Input H-R2-2).
  - Ops enqueued while paused apply on the first tick after resume.
- **Multiplayer never pauses.** Networking & Session owns the "no remote peers" check. It refuses `Pause()` while any transport peer is in any handshake state, and calls `Resume()` at the first connect callback during a solo pause (Lobby/Prep), **before** the `PlayerJoin` op is enqueued. The pause menu is a local overlay, and the input frame is zeroed (Input & Controls).
- **`SceneTree.Paused` is never used** to pause the Sim.

**R17 — Tick driver and frame runner (resolves Grid Inventory OQ20; recommended default; ADR 2 + spike OQ1).**
- **`TickDriver`** is a pure, Godot-free accumulator in `Hosting/`. Its API is `Start(ts)`, `Advance(ts) → ticksDue`, `DropRemaining(m)`, `Pause()`, `Resume(ts)`, `Resync(ts)`, `Stop()`, `Alpha` and counters. It takes timestamps as arguments and never reads a clock. `SimWorld.Tick()` knows nothing about it.
- **`FrameRunner`** is Godot-free, also in `Hosting/`, and takes an injected `IMonotonicClock`. It owns:
  - the per-frame loop (R2);
  - the first-tick edge flag (`isFirstTickInBatch`);
  - the wall-clock guard;
  - `DropRemaining`;
  - the transport flush after `Tick()` returns (R12).
- **Godot side.** The Godot side is one `GameLoop` node whose `_Process` calls `FrameRunner.RunFrame()`. Presentation is then called explicitly with `Alpha`. The `_Process` `delta` is not used. `ProcessMode = Always` is set on that node. Both node names and the process-mode behaviour are UNVERIFIED. The rejected options and the time-source choice are in seed §3.4.
- **Catch-up.** At most `maxCatchUpTicksPerFrame` ticks run per frame (F2).
  - **Guard (M0).** After each tick, `FrameRunner` re-reads its clock. Once `catchUpGuardMs` has elapsed since the frame's first tick began, it stops and calls `DropRemaining`. At least one tick always runs.
  - On a slow host the guard may therefore run fewer ticks than the cap.
  - Excess time is **dropped, never banked**. Each drop writes one hitch record (rate-limited, R31) and adds to `droppedTicksTotal`.
  - Game time then slips against wall time. This is acceptable because a slip only ever gives the crew more real time before a deadline, never less, and every player sees the same slip.
- **`Resync(ts)`.** Called after known blocking work (world generation, scene load, focus regain). It clears the accumulator without a hitch record.
- **Input edges.** Latched edges attach to the **first** tick run in a frame. Later catch-up ticks carry no edges, only the current held bits and the latest `moveIntent` (Input & Controls R11, H-EC-4). A frame that runs 0 ticks keeps its edges latched, in order. A dropped tick never discards an edge.
- **`Alpha`** = `acc30 / freq`, in [0, 1). It is Presentation-only, never read by the Sim, and frozen at 0 while paused.

**R18 — Thread (recommended default; ADR 3, OQ2).**
- v1 runs the Sim on the Godot main thread. `SimWorld` has one owner thread, captured at `Freeze()`, and Debug asserts it in `Tick()`, `EnqueueOp()` and the sample-ring drain.
- **Transport boundary.** Transport callbacks only *enqueue*; nothing is applied inside a callback. Which thread Godot multiplayer callbacks arrive on is UNVERIFIED (Grid Inventory OQ12). Moving off the main thread re-specifies R5 (stamping), R6 (`opSeq`) and R7 (full-ring reply, thread assert). Those triggers are in seed §3.4.
- **Headless.** xUnit, BalanceSim and BotClient have no driver. They call `SimWorld.Tick()` in a plain loop.

#### Identity

**R19 — `EntityId`.** An `EntityId` is a `uint32`: an 8-bit `EntityClass` and a per-class counter starting at 1 (encoding F4). `0` is `None`.

| Class | Name | Allocated by | Notes |
|---|---|---|---|
| 0x01 | `player` | Sim Core (`PlayerJoin`) | R21 |
| 0x02 | `world_item` | Grid Inventory via Sim Core | Grid Inventory R12 |
| 0x03 | `construct` | Building | Walls, belts, sorters, depots, turrets |
| 0x04 | `vehicle` | Vehicles | |
| 0x05 | `enemy` | Enemy AI & Waves | |
| 0x06 | `npc` | Vehicles & NPC Drivers | **DEV-4** |
| 0x07 | `destination` | World Generation, in lot order at world build | Mailboxes; the u32 form Manual Delivery and HUD use. **DEV-4** |
| 0x08 | `prop` | World Generation | Resource nodes, other interactables. **DEV-4** |
| 0x09–0xEF | reserved | — | A new class is an amendment to this table |
| 0xF0–0xFF | test/debug | test fixtures only | Never in a shipped run |

- **Mail is not an entity class.** `mailId` is a separate `uint32` counter owned by Sim Core (spec 07 §4.2). Mail allocates from it in manifest order at the batch tick, and lower means older.

**R20 — Allocation.**
- `EntityIds.Allocate(class)` and `MailIds.Allocate()` run only on the Sim thread inside `Tick()`. The one exception is world build in `Constructed`, which may allocate `destination` and `prop`.
- Ids are **never reused within a run**.
- **Initial counters.** `EntityIds`, `MailIds`, `opSeq` and the tick can be constructed from initial values. Free Play save/load will use this path (spec 07 §7.3), and tests use it to reach exhaustion without a test-only seam.
- **Exhaustion** (unreachable in a run, F4). A class counter at 2^24 − 1 makes `Allocate` return `None`. `mailId` at 2^32 − 1 returns 0. `opSeq` at 2^32 − 1 makes `EnqueueOp` return false. Each writes one always-on record (R31) and asserts in Debug. The caller refuses the spawn. Ids never wrap or skip.
- **Tie-breaks** use ascending raw `uint32`, which within one class is creation order.

**R21 — Players, slots, joins and reconnects.**
- **Ids.** A `player` id is allocated by the `System` `PlayerJoin` handler in phase 0, and only there. Networking & Session keeps the run-scoped account → `playerId` map. A new account gets a new id; ids are never reused.
- **Slots.** Each player in the run holds a **`PlayerSlot`** (byte 0–7) assigned by Networking. It is the compact wire form used by Throwing's `throwerId`, the transform index, Grid Inventory temp-id namespaces and HUD pings. The Sim keeps a `slot ↔ playerId` table and never sees accounts or transport peers.
- **Join** (new account). Accepted in Lobby and Prep only (spec 06 §6). Otherwise it is rejected `join_closed`.
- **Disconnect.** Networking enqueues the `System` op `FreezePlayer`. Player Character applies it: the body is held Frozen for 120 s, and inventory stays on the body (PCM P19, spec 06 §6). **The slot stays held by that `playerId`.**
- **Reconnect** (same account, body Frozen). A `PlayerJoin` carrying the existing `playerId` is accepted **in any run phase**. It keeps the same `playerId` and the same slot. Player Character and Grid Inventory own the RejoinState and `Teleport` (P19).
- **Leave.** The slot is freed when the player is removed: a `PlayerLeave` op (menu quit, kick, Lobby or Joining disconnect) or Player Character's Frozen expiry at 120 s, which calls `IPlayersApi` and in turn Sim Core's `RemovePlayer`. What happens to the carrier and inventory (spill, despawn) is Player Character's and Grid Inventory's rule.
- **Same-tick reuse.** A slot freed in tick T can be reassigned from T+1 only, so a `PlayerJoin` for it in the same drain is rejected `slot_busy`.
- **Other rejects.** An out-of-range slot gives `slot_invalid`. A `playerId` that is present and not Frozen gives `duplicate_player`. Nothing is allocated on a reject.
- **Last player.** If the last player leaves, the world keeps ticking. Only a stop ends it.

**R22 — System-local ids** are not `EntityId`s and belong to their systems: `entryId` (Grid Inventory; bit 31 reserved for client temp ids), `containerId`, `version`, Intake `arrivalSeq`, `flightId` (Throwing, u16 wrap-safe), `reqId` (opaque, R6) and `outcomeSeq` (Mail). Sim Core provides wrap-safe serial-number helpers (`SerialU8/U16/U32`, RFC 1982) and checked narrowing helpers (`WireNarrow`). Their semantics are in seed §3.5. A narrow wire id must come from an id map, never from the low bits of an `EntityId`.

#### Randomness

**R23 — One derivation for every stream.** `RngStream.Create(runSeed, StreamName, index)` seeds a PCG32 (XSH-RR) generator from `Derive(runSeed, name, index)` (F5).
- Stream names are compile-time constants in one `RngNames` registry, lowercase ASCII and dot-separated. Each name declares its allowed index range. `StreamName` has a private constructor, so an unregistered name cannot compile.
- Names are never built from player text.
- `System.Random`, `GetHashCode`, `Guid` and floating point are never used for randomness in the Sim.

**R24 — v1 stream registry (DEV-5).**

| Stream name | Index | Owner | Lifetime |
|---|---|---|---|
| `worldgen.<stage>` | 0 | World Generation | Per stage |
| `mail` | shift (1–5) | Mail & Destinations | Recreated on demand. Pure per shift, so a Prep re-roll gives the same result. Replaces spec 07's `mail.spawn` / `mail.address`. |
| `mail.special` | shift (1–5) | Mail & Destinations | As above |
| `waves` | 0 | Enemy AI & Waves | Run |
| `mega` | 0 | Enemy AI & Waves | Run |
| `drops` | 0 | Health & Damage / Enemy AI | Run |
| `perks` | 0 | Perks & Perk Draft | Run |
| `shop` | 0 | Economy, Wallet & Shop | Run |
| `events` | shift (1–5) | Stamps & Run Events | Recreated on demand |

- Throwing has no stream (registry `throwNoDice`).
- Spec 07 §4.3's reproducibility statement is kept. `mail` and `perks` are fully reproducible from the seed. `waves` and `drops` depend on player behaviour through the complaint meter.
- **Re-creating a stream.** Creating the same `(name, index)` twice gives identical sequences. The registry keeps one live handle per key, so the second `Create` retires the first and resets `draws`.

**R25 — Draw contract.**
- `Next32()` returns the PCG32 output.
- `NextInt(b)` is the multiply-shift draw (F6). It consumes **exactly one** draw per call, including `b = 0`, which returns 0 (Mail's `K` draw-count contract).
- There is no float method.
- Tests inject an `IRngSource` stub (Mail M-F4-1).

**R26 — Ownership and audit.** The composition root gives a stream handle only to its registered owner (R4). Holding the handle is the ownership check. Each stream counts `draws`. The dump records `(name, index, state, inc, draws)` for every live stream. Cosmetic randomness on clients uses a Presentation-side RNG and never feeds the Sim.

#### Numbers and determinism

**R27 — Numeric types in authoritative state.**

| Quantity | Type | Rule |
|---|---|---|
| Money | `Cents` (`long` cents) | Range ±2^62. `MulPermille` is the only rounding entry point (F7). |
| Ratios | `int` permille | Throw spread is in mrad |
| Positions and distances | `int` millimetres | Stored and on the wire |
| Sub-millimetre integration | `Fx16` (16 fractional bits), floored to mm | Each system decides which quantities use it |
| Angles | `ushort` BAM (65536 = 360°) | Committed Q15 sin/cos table, guarded by a hash test |
| Ticks | `uint32`; differences `int64` | |

`float`, `double` and `decimal` are **banned in authoritative state and in anything that orders events**. They are allowed in Presentation and tools.

**R28 — Determinism rules (code-review checklist; the full checklist with rationale is seed §3.6, ADR 9).**
1. No float, double or decimal anywhere in the Sim assembly (OQ10).
2. Integer maths widens before multiplying. Ratios use `Fix.MulDiv` with the documented rounding (F7).
3. No iteration over `Dictionary` or `HashSet`; they are lookup-only.
4. Order-preserving removal only.
5. No unstable or version-dependent sorts (`Array.Sort`, `List.Sort`, LINQ `OrderBy`). Use `Sim.StableSort`, and every comparer ends with an `EntityId` or `opSeq` tie-break.
6. No `System.Random`, `Guid`, `string.GetHashCode` or `object.GetHashCode` in state.
7. No time source and no `Thread`/`Task`/`Parallel` inside the Sim. Timing is measured only through an injected `IPhaseProbe` (null by default, allocation-free, never read by Sim logic).
8. No static mutable state, so two `SimWorld`s coexist in one process.
9. No culture-dependent text in state or the dump.
10. Content def ids come from a sorted-key table at load.
11. **Overflow semantics are explicit.** Hash and PRNG code is written `unchecked`. Every other overflow is either a range-checked clamp (F7) or a bug that Debug's `checked` surfaces. So Debug and Release dumps are identical whenever no Debug check fires.

Rules 1, 5, 6 and 7 are enforced at build time by the banned-API analyzer. Rule 3 is a review rule until the custom analyzer in ADR 9 exists (OQ10). The rest are enforced by review, the SC-DET-1 fixture and the static-state reflection test.

**R29 — Determinism scope (v1).**

| Tier | Guarantee | Proof |
|---|---|---|
| **T1, same machine** | Two `SimWorld`s with the same `ContentSet`, `SimSettings`, `runSeed`, op log and ingested-sample log give identical canonical dumps after every tick. | H-DET-1 (Grid Inventory), SC-DET-1 |
| **T2, cross-platform** | `OwnedCarrier.Step`, the RNG and hash primitives, the shared pure functions, and the canonical encoding of any checksum both sides compute are bit-identical on Windows and Linux x64. | PCM-P2-1, SC-DET-2 |
| **Not required** | Clients never lockstep or re-simulate the world. World generation is not regenerated on clients (spec 13 §3). | — |

**R30 — Canonical state dump.** Every system with state binds an `ISimStateContributor` (R4). The dump writes sections in fixed registration order, with collections in ascending id. Its encoding is in seed §3.7.
- **Included:** the tick, all id counters, the mail counter, `opSeq`, every live RNG stream (R26), the op queue contents, and the transform-sample latch.
- **Excluded:** perf counters, the record ring, caches and anything derivable.
- **When it is valid.** The dump is valid only **between ticks**, defined as immediately after `Tick()` returns. A request during `Tick()` (including from the sink) returns false and is counted. A `Stopped` world's dump is valid. A `Faulted` world's is not.
- Tests and tools hash the dump with SHA-256. The Sim has no cryptography reference. `CanonicalHash64` is built only when a cross-machine checksum first needs it.

#### Safety and cost

**R31 — Assertions, counters and always-on checks.**
- **Asserts.** Assertion calls compile out of Release, and assertion arguments must be pure. Per-system toggles exist where a GDD needs one (`INVENTORY_ASSERT`, Grid Inventory R19). Symbol mechanics are in seed §3.8.
- **Counters.** Every "counted" outcome in this GDD increments a named `SimCounters[CounterId]`, readable between ticks.
- **Always-on checks** run in every build. They are O(1) checks plus one cheap check per tick. A failure writes `{code, tick, a, b}` to a fixed **record ring**: at most one record per code per 30 ticks, plus a suppressed count. It never throws, formats strings or allocates. In this GDD, "log" and "record" always mean this ring, never text logging inside the tick.
- **Full-scan invariants** are assert-only. Grid Inventory R19 is the model.
- **Release.** Release builds have asserts off and are the only configuration perf budgets are measured in. `ReleaseAssert` is used for nightly soaks.

**R32 — Allocation-free tick.** After `warmupTicks`, `SimWorld.Tick()` allocates **0 bytes** on its thread, measured with no recorder attached.
- Events and ops are blittable structs in preallocated rings.
- Collections are pre-sized from `SimSettings` and do not grow after warm-up.
- The banned constructs and the measurement method are in seed §3.9 (ADR 9). The gate is SC-R32-1.

**R33 — Assembly shape (recommended default; ADR 1 + spike, OQ4).**
- The Sim is a plain .NET class library targeting **`net8.0`**. **DEV-7**: spec 07 says `netstandard2.1`, which lacks the `Int128` that F7 needs.
- It has **zero Godot references** and no runtime package dependencies. Content loaders take `Stream` or `ReadOnlySpan<byte>`.
- Adapters live in the Godot project, outside the Sim: transport, the `IEventSink` implementation, the op encoder, loopback, `IHostClock`, the `IPhaseProbe` implementation, the `IMonotonicClock` implementation, and `GameLoop`.
- CI fails if the Sim references any `Godot*` assembly or contains `using Godot`.
- The folder layout, the ProjectReference/globs question, the shared-folder fallback, build-configuration mapping and the test-project layout are in seed §3.9. Whether the Godot .NET SDK in 4.7.1 accepts a ProjectReference to a `net8.0` library is UNVERIFIED.

### States and Transitions

`SimWorld` lifecycle:

| State | Ticks? | `EnqueueOp`? | Entered by | Valid next |
|---|---|---|---|---|
| `Constructed` | no | no (false, counted) | `SimComposition.Build()` | `Ready` via `Freeze()`, after binds, content and world tables load, and world-build id allocation |
| `Ready` | no | **yes** (queued for tick 0) | `Freeze()` | `Running` (first `Tick()`) |
| `Running` | yes, when `Tick()` is called | yes | first `Tick()` | `Stopped`, `Faulted` |
| `Stopped` | no | no (false, counted) | `IRunControl.RequestStop()` inside a tick (Run Structure at Results, phase 12) completes that tick through `Events.Flush`, then transitions, with no extra tick. `Stop()` from the host adapter outside a tick (host quit) transitions immediately. | none; results and dump stay readable |
| `Faulted` | no | no (false, counted) | An exception escaping a phase in Release: the tick is abandoned, its partial log is **not** delivered, and the re-entrancy flag is cleared in a `finally` | none; the dump is invalid; the adapter shows an error and ends the session |

- An illegal call asserts in Debug and is ignored and counted in Release. `Tick()` on a non-`Running` world, or a re-entrant `Tick()`, runs no phase and counts `badTick`.
- `RequestStop()` from a phase other than 12 is latched as if raised in phase 12 and counted (`stopOutOfPhase`). A second stop is ignored and counted.
- In Debug, an exception escaping `Tick()` is rethrown after the world is marked `Faulted`.
- There is no `Disposed` state; the world holds only managed memory.
- Pause is not a `SimWorld` state (R16), and joins, reconnects and leaves are ops, not states (R21).
- A Free Play `Loading` state is deferred (OQ11).

`TickDriver` lifecycle (one per process, owned by `FrameRunner`):

| State | `Advance` returns | Entered by | Valid next |
|---|---|---|---|
| `Idle` | 0 | construction, `Stop()` | `Running` (`Start(ts)`: accumulator cleared) |
| `Running` | 0 – `maxCatchUpTicksPerFrame` (F2) | `Start`, `Resume` | `Paused` (solo only), `Idle` |
| `Paused` | 0, accumulating nothing; `Alpha` 0 | `Pause()` | `Running` (`Resume(ts)`: accumulator cleared), `Idle` |

`Resync(ts)` is legal in `Running`. A double `Pause`, double `Resume`, or `Advance` while `Idle` is an idempotent no-op.

### Interactions with Other Systems

Sim Core defines these ports: `ISimPhase`, `ITickEndCheck`, `IOpHandler`, `IClientStep`, `IEventSink`, `ISnapshotSource`, `IOpRecorder`, `ISimStateContributor`, `IRunControl`, `IHostClock`, `IPhaseProbe`, `IMonotonicClock` and `IRngSource`. It also defines the concrete services `EventLog`, `EntityIds`, `MailIds` and `RngStream`. The JoinState contribution interface is defined by Networking & Session.

| System | Data in → Sim Core | Data out ← Sim Core | Interface owner |
|---|---|---|---|
| Content & Data Definitions | `ContentSet` (immutable, sorted ids), `SimSettings`, balance constants; seconds → ticks via F1 | `StringId` contract | Content & Data owns defs and the loader; Sim Core owns its `SimSettings` keys |
| Networking & Session | `EnqueueOp` (including System `PlayerJoin`/`FreezePlayer`/`PlayerLeave`), samples, `IHostClock` client estimator, `Resume()` before a join | Per-tick batch + `overflowInfo`, replies, `ISnapshotSource` reads between ticks, `slot ↔ playerId` | Sim Core defines the ports; Networking owns transport, JoinState, resync policy and the pause rule |
| Grid Inventory | Phases 1–2, op handlers, `Coalescible` events, contributors, its idempotent-`reqId` cache | `world_item` ids, `mailId`, `opSeq`, event log, tick-end hook | Grid Inventory (HB1) |
| Player Character | Phase 3 host role; phase 11 reader; `CarrierStep`; `FreezePlayer` handler | `player` ids, tick, sample latch timing | Player Character |
| Input & Controls | Input frame into `ClientLayer` (never `SimWorld`), edges on the first tick | `isFirstTickInBatch`, F1 | Input |
| Mail & Destinations | Phase 8, delivery handlers, R6 order inside phases 0/4/5/6 | `mail`/`mail.special` streams, `NextInt`, `mailId`, `destination` ids, F7 | Mail |
| Manual Delivery | Deliver requests as ops | `IHostClock` (host tick domain), next-client-tick loopback replies | Manual Delivery |
| Throwing & Catching | Phase 4, `throw` op | Serial helpers; no RNG stream (`throwNoDice`) | Throwing |
| HUD & Notifications | none (Presentation) | Event-fed ClientWorld models, `IHostClock`, F1 display rule | HUD |
| Health & Damage | Phase 10 (one `PlayerDied` per carrier per tick) | Event log, `drops` stream, phase 11 readers | Health & Damage |
| World Generation | World tables at `Constructed` | `worldgen.*`, `destination`/`prop` ids | World Generation |
| Run Structure | Phase 12, `IRunControl.RequestStop()` | Pause rule, F1 display | Run Structure |
| Belt Logistics, Vehicles, Enemy AI, Economy, Perks, Stamps | Their phase (R3) | Their ids (R19), streams (R24), `Cents` | owners |
| Dev Tooling | `IPhaseProbe` and `IMonotonicClock` implementations, overlay | Dump, recorder, `SimCounters`, record ring (field list: seed §3.16b) | Dev Tooling |

**Deviations from spec 07** (folded into ADRs, OQ7):
- **DEV-1:** `Items.Expire`, `Intake.Drain` and `Throwing.Step` added after `Inputs.Apply`; `Automation.Step` before `Vehicles.Step`.
- **DEV-2:** `Inputs.Apply` no longer applies movement or look.
- **DEV-3:** `Players.Step` is host-role.
- **DEV-4:** classes `npc`, `destination` and `prop` added.
- **DEV-5:** per-shift indexed `mail` streams and an `events` stream.
- **DEV-6:** automation uses synchronous APIs in its own phase, not the FIFO.
- **DEV-7:** the Sim targets `net8.0`.
- **DEV-8:** `Combat.React` and a tick-end hook added; `Economy.Flush` only flushes.

**Milestone slice.** M-1a writes the **real** files for the contracts seven GDDs depend on, because a rewrite risks drift (lead-programmer). It freezes every interface and implements the rest at its first consumer (technical-director ruling). M-1a is single-player and local (spec 13 §6), so ops travel only through loopback.
- **Real at M-1a:**
  - the assembly, analyzer and architecture tests;
  - builder, phase table, composition root and reflection test;
  - op ring, drain cap, replies and reasons;
  - event log, cursors, flush and descriptors (single-tier overflow flag and masks);
  - `TickDriver` and `FrameRunner` (no guard);
  - `EntityIds`, `MailIds`, `RngStream` with golden vectors, `Cents`/`Fix`/`Fx16`/BAM;
  - dump, in-memory recorder (ops **and** samples), and SC-DET-1.
- **Frozen interface, built by the first consumer:** probe (null), coalescing, two-tier admission, `Serial*`/`WireNarrow`, solo pause, `ISnapshotSource`, the guard (M0), and the 0-alloc gate (advisory until M0).
- The per-milestone table is seed §3.10.

## Formulas

*Specialists: systems-designer (F1, F2, F4–F8), performance-analyst and technical-director (F3). Numbers that are not engine or spec constants are **PROPOSED**. All integer maths is `long`, or `Int128` where stated, never float (R28). Source column: constant (compile-time or spec), data file (`ContentSet`/`SimSettings`), or calculated. Derivations, overflow analysis and algorithms are in seed §3.11–§3.15.*

### F1 — Tick and time

The tick-to-time formula is defined as:

`tickDurationUs = 1_000_000 / 30` (exact rational, never stored rounded)
`ticksToMsFloor(t) = (t × 1000) / 30`  and  `ticksToMsCeil(t) = (t × 1000 + 29) / 30`
`contentTicks(ms) = (ms × 30 + 999) / 1000`
`displaySeconds(r) = (r + 29) / 30`, where `r = max(0, (int64)deadlineTick − (int64)currentTick)`. The clock reads `min(displaySeconds, 5999) / 60 : % 60`, capped at "99:59".

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| t | int64 | 0 to 2^31 − 1 | calculated | Tick count or tick difference |
| ms | int64 | 0 to 86 400 000 (ContentValidator cap) | data file | Content duration, in whole milliseconds |
| r | int64 | 0 to 2^31 − 1 | calculated | Ticks remaining, clamped at 0 (int64 subtraction, never uint32) |
| TickRateHz | int | 30 | constant | R1 |

**Output Range:** `t × 1000` is at most 2.1 × 10^12, inside int64.
- `ticksToMsFloor` is the test and input-clock mapping (Input & Controls). `ticksToMsCeil` is used for "not before" bounds.
- **Content parsing.** Content seconds (spec 08 §1) are parsed as decimal text with at most 3 fractional digits into integer ms, then converted. More digits fail the load. The float product `seconds × 30` is forbidden.
- **Computed durations.** Durations derived by multiplication, such as `holdEff`, are re-validated against the cap after multiplying.
- **Bounds.** `contentTicks(0) = 0`, and any `ms ≥ 1` gives at least 1 tick.
- **Round trip.** The round trip is not an identity (`contentTicks(ticksToMsCeil(1)) = 2`). Never convert ticks → ms → ticks.
- `displaySeconds` uses ceil: it is 0 only when `r = 0`, so the clock never shows "0:00" while time remains.

**Example:** 400 ms → **12 ticks**. 150 ms → **5**. 15 000 ms → **450**. r = 360 shows **"0:12"**, still 0:12 at r = 331, and 0:11 at r = 330. `ticksToMsFloor(1) = 33` and `ticksToMsCeil(1) = 34`.

### F2 — Catch-up accumulator

The accumulator formula is defined as:

`Advance(now)`: `Δ = clamp(now − last, 0, freq); last = max(last, now); acc30 += Δ × 30; due = acc30 / freq; n = min(due, maxCatchUpTicksPerFrame); acc30 −= n × freq; if (due > n) { droppedTicksTotal += due − n; acc30 %= freq; one hitch record }; return n`
`DropRemaining(m)` (called by `FrameRunner` when the guard stops early, m = returned − run): `droppedTicksTotal += m` (acc30 unchanged)
`alphaPermille = acc30 × 1000 / freq`
**Invariant ("no time is created"):** `(ticksRunTotal + droppedTicksTotal) × freq + acc30 = Σ min(max(Δ, 0), freq) × 30`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| freq | int64 | 10^3 to 10^12 (the constructor throws outside it) | calculated | Timestamp counts per second, given once by the adapter |
| now, last | int64 | monotonic | calculated | `Advance` argument and the previous one |
| acc30 | int64 | below freq after every `Advance` | calculated | Accumulator scaled by 30, so one tick is exactly `freq` units |
| maxCatchUpTicksPerFrame | int | 4 (safe 3–8) | data file | Tuning Knob. Input H-EC-4 needs ≥ 3. |
| alphaPermille | int | 0–999 | calculated | Presentation only |

**Output Range:** `n` is 0 to 4. Sim time never runs ahead of wall time; a hitch can only lose time.
- With the cap alone, nothing is dropped at ≥ 7.5 fps. With the M0 guard, a host whose ticks are slow may run fewer ticks than the cap (R17), so the constraint `catchUpGuardMs ≥ 2 × tickBudgetP99(host)` keeps ≥ 3 catch-up ticks within budget.
- `Start`, `Resume` and `Resync` set `last = now` and `acc30 = 0`.
- A backwards timestamp adds nothing and does not move `last` back. It is counted as `backwardsClock`.
- Time beyond the 1 s clamp is counted in `clampedTimeTotal`.

**Example** (freq = 10^7):
- A 150 ms hitch from 0 gives due = 4: **run 4, dropped 0**, acc30 = 5 000 000, Alpha 500 ‰.
- The same hitch again gives due = 5: **run 4, dropped 1**, acc30 = 0.
- A 400 ms hitch gives due = 12: **run 4, dropped 8**.
- Steady 60 fps (Δ = 166 667) alternates 0 and 1 ticks, with Alpha exactly 500 and 0 ‰.

### F3 — Tick budget gates

The tick budget formula is defined as:

`p99(totalTick, fixture) ≤ tickBudgetP99(mode)`, `max(totalTick, fixture) ≤ tickBudgetMax(mode)`, `median(totalTick) ≤ tickBudgetMedian(mode)`
`p99(x) = sorted(x)[r − 1]`, where `r = (99 × N + 99) / 100` (integer division)

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| totalTick | int µs | ≥ 0 | calculated | `SimWorld.Tick()` wall time, phases 0–13 + tick end, via `IPhaseProbe` |
| tickBudgetP99 / Max / Median (host) | int µs | 5 000 / 8 000 / 3 000 | constant (PROPOSED; max is spec 07 §8) | Listen-server host gates on SC-FIX-WORST. A tick frame must also fit ClientLayer and render (≤ 10 ms) in 16.6 ms. |
| tickBudgetP99 (dedicated) | int µs | 8 000 | constant | Spec 07 §8 (deferred) |
| clientLayerBudgetP99 | int µs | 300 | constant (PROPOSED) | Per client tick, outside `SimWorld.Tick` |
| N | int | ≥ 18 000 | constant | Ticks measured after warm-up |

**Output Range:** pass or fail per fixture and mode.
- **Fixtures.** **SC-FIX-STEADY** (median ≤ 1.5 ms, p99 ≤ 3.0 ms) and **SC-FIX-WORST**. Their contents are in seed §2 and §3.11. In SC-FIX-WORST every spike source fires at the cadence its owning GDD sets, and a full coincidence is forced every 150th tick (0.67 %), so the max gates coincident ticks and the p99 gates the rest.
- **Per-phase slices.** The per-phase p99 slices (sum 4.50 ms + derived reserve ≥ 0.3 ms) are **owned by the technical-director** in the perf budget: seed §2 now, `docs/architecture/perf-budget.md` from OQ5. They are for attribution only, because quantiles do not add. A phase over its slice but inside the reserve is **flagged, not passed**.
- Budgets are test thresholds, not `SimSettings` keys.

**Gate status:**

| Gate | Status |
|---|---|
| 0 B allocated per tick after warm-up and no collection growth (R32); 0 gen-0 GCs and Sim heap ≤ 64 MB in a dedicated process | **BLOCKING** from M0 (measured, advisory in M-1) |
| Total p99, or the p99 of any phase with a baseline ≥ 0.2 ms, above 2 × the calibration-normalised CI baseline (`production/qa/perf-baseline.md`) | **BLOCKING** from M0; other phases advisory |
| Absolute 5 / 8 / 3 ms gates | **ADVISORY** until a reference machine exists (OQ6) |

**Example:** N = 18 000 gives r = (1 782 000 + 99)/100 = 17 820, so p99 = `sorted[17 819]`.

### F4 — EntityId layout and exhaustion

The entity id formula is defined as:

`id = ((uint)class << 24) | counter`;  `class = id >> 24`;  `counter = id & 0xFFFFFF`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| class | byte | 0x01–0x08 shipped; 0xF0–0xFF test | constant | R19 |
| counter | uint32 | 1 to 16 777 215 per class | calculated | Never reused, never wraps |

**Output Range:** shipped ids lie in 0x01000001–0x08FFFFFF, and 0 is `None`.
- **Per class.** One class can sustain about 4 660 allocations/s for a 1 h run. `world_item` is the dominant class (spills, expiries, splits), and its worst-case rate is re-derived in seed §1 (OQ9).
- **`opSeq`.** It lasts about 103 days at 480 enqueues/s.
- **Mail and tick.** `mailId` stays under 10^4 per run. `Tick` lasts 4.5 years.

**Example:** player 1 = 0x01000001 = **16 777 217**; construct 1 = 0x03000001 = **50 331 649** (both match spec 08 §3). Decoding 50 331 649 gives class 3, counter 1.

### F5 — RNG stream derivation

The stream seed formula is defined as:

`s = Derive(runSeed, name, index) = Mix64(Mix64(runSeed ^ Fnv1a64(utf8 name)) ^ (u64)index)`; PCG32 XSH-RR is seeded with `initState = Mix64(s)` and `initSeq = Mix64(s ^ 0xDA3E39CB94B95BDB)`. `Mix64` is the SplitMix64 finaliser. FNV-1a-64, `Mix64` and the PCG step are written `unchecked` (seed §3.13).

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| runSeed | uint64 | full | data file | From lobby settings (`SimSettings`) |
| name | ASCII | `RngNames` registry | constant | R23–R24 |
| index | uint32 | the name's declared range (`mail` 1–5) | calculated | Shift number, or 0 |

**Output Range:** every stream has period 2^64.
- **Distinct indices.** `Mix64` is a bijection, so distinct indices under one name always give distinct seeds.
- **Distinct names.** A startup test asserts that every name has a distinct FNV hash. A seed-level collision across names is about C(50,2)/2^64 ≈ 7 × 10^-17 per run, which is accepted.

**Example:** `Derive(1, "mail", 2) = 0x120344c795c4b10e`, and its first `Next32` is `0x915ab962`. The full golden vectors are in SC-F5-1 and were recomputed independently on 2026-10-16.

### F6 — Bounded integer draw

The bounded draw formula is defined as:

`NextInt(b) = (u32)(((u64)next32 × (u64)b) >> 32)`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| next32 | uint32 | full | calculated | One PCG32 draw |
| b | uint32 | 0 to 2^32 − 1 (an int overload asserts b ≥ 0) | calculated | Exclusive bound |

**Output Range:** 0 to b − 1, and 0 when b = 0. Every call consumes exactly one draw (no rejection loop). The relative bias is at most b/2^32.

**Example:** with b = 10, next32 0x80000000 gives 5, 0xFFFFFFFF gives 9 and 0 gives 0. The first draw of `Create(1, "mail", 2)` gives **5**.

### F7 — Money and fixed-point rounding

The rounding formulas are defined as:

`Cents.MulPermille(c, pm) = sign(c) × min((|c| × pm + 500) / 1000, 2^62)`, computed in `Int128`; a clamp is counted as `moneyClamped`.
`Fix.MulDiv(a, b, c) = trunc(a × b / c)` (Int128 product)  ·  `Fix.MulDivRH(a, b, c)` = `a × b / c` rounded half away from zero (overflow-free algorithm: seed §1)
`Fx16.ToMm(fx) = fx >> 16` (floor)  ·  `Fx16.FromMm(mm) = mm × 65536` for |mm| ≤ 2^31

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| c | `Cents` | ±2^62 | calculated | Money |
| pm | int | 0 to 10^6 | data file / calculated | Permille. A negative value asserts in Debug; Release treats it as 0 and counts it. |
| a, b, c (MulDiv) | long | full | calculated | Multi-factor numerators accumulate in Int128 with one final division |
| fx | long | ±2^47 | calculated | 16 fractional bits |

**Output Range:**
- **Money rounding.** It rounds half away from zero, on the magnitude, with the sign applied afterwards. This is the "RH" of the registered `mailUnitPay`, `mailSpawnValue` and `misdeliveryPenalty`.
- **`MulDiv`.** It truncates toward zero (`weightSpeed` keeps this).
- **Faults and clamps.** A zero divisor is a fault: it returns 0 and is counted. A result outside the caller's domain is clamped (±2^62 for money, ±2^47 for Fx16) and counted. Nothing throws inside a tick (R31).
- **`Cents` add and subtract.** These are range-checked against ±2^62: Debug asserts, and Release saturates and counts `moneySaturated`. The check is explicit, because `checked` arithmetic only fires at 2^63. Negative balances inside the range are legal (Mail's `wallet_floor` −500).

**Example:** `MulPermille(15, 1500) = 23` and `MulPermille(−15, 1500) = −23`. `MulDiv(−7, 1, 2) = −3`. `Fx16.ToMm(−98 304) = −2`, where truncation would give −1.

### F8 — Queue, drain and log capacity

The capacity formulas are defined as:

`opQueueCapacity ≥ nextPow2(players × opRatePerPlayer × burstSeconds × safetyQ + systemOpReserve)`
`maxOpsDrainedPerTick ≥ 2 × players × opRatePerPlayer / 30`
`maxOpsDrainedPerTick × entriesPerOp + 1.5 × systemEventsPerTick ≤ eventLogCapacity − eventLogReserve`
`eventLogReserve ≤ eventLogCapacity / 4`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| players | int | 1–8 | data file | Connected players |
| opRatePerPlayer | int | 60 ops/s | constant (PROPOSED) | Worst-case sustained ops per player |
| burstSeconds × safetyQ | rational | 1.0 × 8 | constant | Arrival headroom: about 8 s of worst-case ops |
| entriesPerOp | int | 4 | constant (PROPOSED) | A reply plus up to 3 events |
| systemEventsPerTick | int | ≈ 380 (estimate; re-derive per phase, OQ9) | calculated | Automation, combat, mail batch, run, expiry, drain |

**Output Range:** the defaults are `opQueueCapacity` 4096, `systemOpReserve` 64, `maxOpsDrainedPerTick` 64, `eventLogCapacity` 4096 and `eventLogReserve` 256. `SimSettings` validation rejects any combination that breaks a constraint, and no world is built.
- A full ring answers `busy`.
- Ops above the cap wait in order.
- The log cannot overflow on the worst fixture tick (SC-R8-4).

**Example:**
- Op queue: 8 × 60 × 1.0 × 8 + 64 = 3 904, which rounds up to **4 096** (256 KiB).
- Drain cap: 2 × 8 × 60 / 30 = 32 ≤ 64.
- Event log: 64 × 4 + 1.5 × 380 = 826 ≤ 3 840. Even doubling `systemEventsPerTick` gives 1 396 ≤ 3 840.

## Edge Cases

*Specialists: systems-designer (round 3) and the 2026-10-16 review. The full original set with every counter is seed §3.16. "Counted" means a `SimCounters` entry (R31).*

### Values and time
- **If a content duration is 0 ms**: it becomes 0 ticks, and the timer is due at its owner's next check. Any ms ≥ 1 gives at least 1 tick (F1).
- **If content has more than 3 fractional digits, a negative value, or more than 86 400 000 ms**: the load fails and no world is built (F1).
- **If `deadlineTick < CurrentTick`**: `r` clamps to 0 and the clock shows "0:00". Timers fire on `CurrentTick ≥ deadlineTick`, never on `r` (F1, R15).
- **If a money product or sum leaves ±2^62, or a divisor is 0**: the result is clamped, saturated or 0, it is counted, and nothing throws (F7).
- **If `NextInt(0)` is called**: Debug asserts. Release returns 0 and still consumes one draw (R25).
- **If the timestamp frequency is outside 10^3–10^12**: the driver constructor throws, outside any tick (F2).
- **If a timestamp goes backwards**: nothing is added, `last` stays, and `backwardsClock` is counted (F2).
- **If one frame's Δ is 5 s**: Δ clamps to 1 s, 4 ticks run, 26 are dropped, and 4 s go to `clampedTimeTotal` (F2).
- **If every tick costs more than 33.3 ms (M0 guard)**: one tick runs per frame and the surplus is dropped. The game plays in slow motion, with no death spiral (R17).
- **If a frame runs 0 ticks**: latched edges stay latched, in order (R17).

### Same-tick ordering
- **If two lethal entries hit one carrier in a tick**: Health & Damage raises one `PlayerDied`, from the first lethal entry (HB7). Phase 11 ignores a second one for a carrier already Dead.
- **If a `Combat.React` reaction raises damage**: Debug asserts. Release applies it at phase 10 of T+1 and counts `lateDamage` (R3).
- **If one player is teleported twice in a tick**: `seq` bumps twice and two `TeleportIssued` events are logged. The client applies the higher `seq` (R5).
- **If `Run.Step` changes the run phase while requests are queued**: they are validated at apply time on T+1, so a Prep-only request is rejected with its system's `phase_closed`. Nothing rolls back.
- **If a host loopback op and a remote op arrive in the same frame**: `FrameRunner` pumps transport, then loopback, then ticks. The remote op gets the lower `opSeq`, a documented host-side tie-break bias (R6, R17).
- **If a player op and an inserter want the last free cell**: the player op wins, and the inserter retries next tick (R9).

### Ops, replies and events
- **If more than `maxOpsDrainedPerTick` ops are queued**: the first `cap` apply in `opSeq` order, the rest wait, and `drainBacklog` is counted (R8).
- **If an actor's op and its `PlayerLeave` are in one drain**: an op with lower `opSeq` applies, and a higher one is rejected `unknown_actor`. Both are recorded (R8).
- **If the ring is at the player limit**: player ops get `busy` on their reply channel, while `System` ops still enter (R7).
- **If a client resends a `reqId`**: Sim Core applies it as a new op unless its handler is idempotent. Grid Inventory answers a duplicate with the original reply (R10). Networking must not resend drained ops (HB10).
- **If the log fills**: entries are dropped and counted, `overflowInfo` names every affected audience and kind, and Networking resyncs exactly those (R14). A dropped `OpReply` puts its owner in the mask.
- **If an event is raised outside a tick, in tick end or during flush**: it is dropped and counted. Inside a tick, `overflow` is also set (R14).
- **If `Events.Update` targets an entry from an earlier call**: Debug asserts. Release writes to a scratch entry, and the write is lost and counted (R13).
- **If an `OwnerOnly` event has 0 or several audience bits**: 0 delivers to nobody. Several assert in Debug, and Release delivers to the lowest bit (R12).
- **If the sink keeps the span after `Deliver`**: the span is invalid. Debug fills the cleared log with 0xDD (R12).

### Ids, RNG and determinism
- **If an out-of-range stream index is requested** (`mail` with shift 0 or 6): Debug asserts. Release derives it anyway and counts it (R23).
- **If a counter is exhausted**: `None`, 0 or `false` is returned, one record is written, and the caller refuses (R20). Unreachable in a run.
- **If a mail batch has zero units**: no `mailId` is allocated (R19).
- **If a replay header's `ContentSet` hash, `SimSettings` or `runSeed` differ**: the loader refuses, and no world is built (R29).
- **If banned APIs or types appear in `src/Sim`**: the build fails. Dictionary iteration is caught in review until the custom analyzer exists (R28, OQ10).
- **If a GC pause lands inside a tick**: the output is unchanged, and the pause shows only as a wall-clock hitch (F2). Allocation after warm-up fails the 0-byte gate (R32).

### Lifecycle, joins and trust
- **If an exception escapes a phase**: Debug marks the world `Faulted` and rethrows. In Release the world becomes `Faulted`, the tick's partial log is never delivered, no further tick runs, and the adapter ends the session with an error (States).
- **If a new account connects during a Shift phase**: Networking refuses it, and any `PlayerJoin` that reaches phase 0 is rejected `join_closed` (R21).
- **If a disconnected player reconnects within 120 s during a Shift**: their `PlayerJoin` carries the existing `playerId`, is accepted into the held slot, and Player Character resumes the Frozen body (R21, PCM P19).
- **If a slot freed in tick T gets a `PlayerJoin` in tick T**: it is rejected `slot_busy`. The slot is reusable from T+1, and `Deliver`'s synchronous peer resolution keeps the leaver's events from the newcomer (R12, R21).
- **If the last player leaves**: the world keeps ticking. Only a stop ends it.
- **If a peer floods ops, lies in a payload, or the host edits a replay**: **Not defended** (friends-only trust, spec 13 §4.3). `actor` is stamped by the adapter, so impersonation is blocked. The dump hash proves consistency, not honesty.

## Dependencies

### Upstream

**None among game systems.** Simulation Core is the root of the dependency map (systems-index row 1). External requirements:
- **.NET 8 runtime** (R33). The desktop minimum is sourced from the 4.5 upgrade page; nothing is sourced for 4.6/4.7 (OQ4).
- **Godot 4.7.1**, only through the Godot-side adapters (`GameLoop`, transport, the sink, `IHostClock`, `IPhaseProbe`, `IMonotonicClock`). Every engine fact they need is UNVERIFIED (OQ1–OQ4).

### Sim/Presentation split

**Simulation Core lives entirely on the Sim side.** The Sim assembly holds `SimWorld`, `ClientLayer`, `TickDriver`, `FrameRunner` and all primitives, with no Godot types, no clocks and no floats in state. The Presentation side *drives* the Sim (`GameLoop` → `FrameRunner.RunFrame()`), *carries* ops in and batches out through the adapters, and *reads* `Alpha`, `IHostClock` and the ClientWorld mirrors.
- **No Presentation value ever feeds the Sim.** The one crossing value is the monotonic timestamp. It decides only *how many* ticks run, never what a tick computes.

### Downstream

| System | Hard / Soft | What it uses | GDD status | Lists Sim Core? |
|---|---|---|---|---|
| Content & Data Definitions | Hard | `SimSettings` keys, F1, `StringId`, sorted ids | Designed (Provisional) | yes |
| Grid Inventory & Containers | Hard | Phases 0–2, ops, replies, events, coalescing, `world_item`, `mailId`, tick-end hook | In Review | yes. HB1, HB2. |
| Player Character & Movement | Hard | Phases 3 and 11, `CarrierStep`, `player` ids, samples, `FreezePlayer` | In Review | yes. HB5. |
| Input & Controls | Hard (M0) | `InputLatch`, first-tick edges, F1, solo pause | Designed | yes |
| Mail & Destinations | Hard | Phase 8, order R6, `mail` streams, `NextInt`, `mailId`, F7 | Designed | yes. HB4. |
| Manual Delivery | Hard | Ops, `IHostClock`, loopback reply timing, `Previews` | In Review | **no**. HB8. |
| Throwing & Catching | Hard | Phase 4, ops, serial helpers, event order; no RNG | In Review | yes. HB3, HB6. |
| HUD & Notifications | Soft | Event batches, `IHostClock`, F1 display rule | Designed | **no**. HB9. |
| Networking & Session | Hard | Every port, `PlayerSlot`, pause rule, `overflowInfo` → resync, join/reconnect snapshot rule | not written | (index: yes). HB10. |
| Health & Damage | Hard | Phases 10–11, `drops`, single `PlayerDied` | not written | (index: yes). HB7. |
| World Generation, Profile & Persistence, Belt Logistics, Vehicles, Enemy AI, Economy, Run Structure, Building, Perks, Stamps, Resource Harvesting, Dev Tooling | Hard (Persistence, Dev Tooling: Soft) | Their phase, ids, streams, ports | not written | must list Sim Core when written |

**Bidirectionality.** Manual Delivery and HUD do not yet list Sim Core (HB8, HB9). Systems-index edge gaps are OQ8.

## Tuning Knobs

Knobs live in `SimSettings`: loaded at world build, part of the replay header, never runtime-mutable inside a run. Knobs marked *code* are compile-time. All values are PROPOSED unless spec-sourced. Budget numbers are test thresholds (F3), not knobs.

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `TickRateHz` | 30 | **locked** (*code*, spec 07 §1.4) | Every tick-denominated value | — | — | A change is a re-design (F1) |
| `maxCatchUpTicksPerFrame` | 4 | 3–8 | Hitch recovery (F2) | < 3 breaks Input H-EC-4 | Long frames run many ticks back to back | `catchUpGuardMs` |
| `catchUpGuardMs` (M0) | 10 | 10–16, and ≥ 2 × host p99 budget | Wall-clock cap on catch-up | Drops ticks on mild hitches | Host frame passes 16.6 ms | `maxCatchUpTicksPerFrame` |
| `opQueueCapacity` | 4096 | 4096–16 384, power of two (F8) | Arrival burst tolerance | `busy` under load | Memory only | `systemOpReserve` |
| `systemOpReserve` | 64 | 32–256 | Slots only `System` ops may use | A join/leave refused in a flood | Shrinks player capacity | `opQueueCapacity` |
| `maxOpsDrainedPerTick` | 64 | 32–256 (F8) | Ops applied per tick | Request latency grows under load | Phase-0 cost and log pressure | `eventLogCapacity` |
| `eventLogCapacity` | 4096 | 4096–16 384, power of two (F8) | Entries per tick before overflow | Overflow → resync | Memory only | `eventLogReserve` |
| `eventLogReserve` | 256 | 64 – capacity/4 | Room kept for cursor kinds and replies | Knockdown/reply loss in a flood | Ordinary events overflow earlier | `eventLogCapacity` |
| `maxEventReaders` | 8 | 4–16 (*code*) | Pull-cursor readers | A system cannot react same-tick | Cursor cost | Phase 11 |
| `warmupTicks` | 300 | 150–900 | Ticks excluded from perf and alloc gates | JIT and type-load allocations count | Hides real startup allocation | Grid Inventory warm-up |

**Not knobs here.** Network send pacing belongs to Networking. Per-system timers belong to their owners, converted with F1. Per-phase budget slices belong to the TD's perf budget (F3).

## Visual/Audio Requirements

**None player-facing.** Simulation Core renders nothing and plays no sound, so the art and audio directors were not consulted. Every outcome the player sees or hears is owned by the system that raises the event.

What it *guarantees* to presentation:
- **Event timing.** On the host, a tick-T event reaches local Presentation in the same frame that ran tick T, read from the adapter's copy after `RunFrame`. The host `ClientLayer` mirror applies it at the next client tick (R2). On remote clients the delay is network latency. Presentation may schedule effects at `ticksToMsFloor(T)` on its timeline.
- **Order.** Batch order is causal order, because coalescing never crosses a reply (R13). An effect is never shown before its cause.
- **Raid presentation** is keyed off Run Structure's events, never wall time (spec 13 §4.5).

## Game Feel

Simulation Core cannot add feel, but it can destroy it. Its feel-budget obligations (PROPOSED):

| Feel target | Requirement | Source |
|---|---|---|
| Input → own-carrier motion | ≤ 1 tick + 1 frame (≤ 50 ms at 60 fps) | Player Character, R2, R17 |
| Look | Applied every rendered frame by Presentation, never quantised to ticks | Input & Controls |
| Hitch | Plays as slow motion, never as a burst of more than 4 ticks in one frame, never a spiral; lost time is dropped, not banked | R17, F2 |
| Resume from solo pause | No burst: zero banked time | R16 |
| Smoothness | `Alpha` lets Presentation extrapolate the local carrier by ≤ 1 tick | R17 |
| Host smoothness | Host tick p99 ≤ 5 ms, so a tick frame still fits 60 fps. 120/144 Hz judder is measured by the OQ1 spike. | F3 |
| Fairness | Same inputs, same outcome order, on host and client | R3, R29 |

## UI Requirements

**No player-facing UI**, and no UX Flag. HUD & Notifications consumes the F1 `displaySeconds` rule. The **developer overlay** (spec 07 §9) is owned and built by Dev Tooling. Sim Core exposes the probe scopes, driver counters, queue and log high-water marks, `overflowInfo`, per-stream `draws`, id counters, the record ring and `Sim.AssertsEnabled` (field list: seed §3.16b).

## Network

*Specialist: network-programmer (round 1, then review 2026-10-16). Godot high-level multiplayer for 4.4–4.7 is **NOT SOURCEABLE**, so every reliance on it is UNVERIFIED (OQ2). Channel choice, packing, pacing, bandwidth, resync policy and the `IHostClock` estimator belong to Networking & Session. Their original notes are seed §3.16c.*

### N1 — The boundary (what the Sim exposes)

| Direction | Port | Called when | Contract |
|---|---|---|---|
| in | `SimWorld.EnqueueOp(actor, in Op)` → bool | Between ticks, on the Sim thread | `actor` stamped by the adapter (R6). False → the adapter replies `busy` (R7). |
| in | Sample ring | Between ticks, on the Sim thread | Drained and stamped at the next tick boundary (R5) |
| out | `IEventSink.Deliver(tick, span, overflowInfo)` | Once per tick, phase 13 | Copy-only; mask → peers resolved inside the call; the span is valid only during the call (R12, R14) |
| out | `ISnapshotSource.Write(tick, writer)` | Between ticks | Read-only continuous state (remote transforms, vehicles, enemies, HP). Bound per system (R4). Stamped with the tick it describes. |
| out | Join snapshot | Between ticks | Networking defines the envelope. Sim Core guarantees a point-in-time consistent snapshot after `Deliver` of `joinTick` (N3). |
| both | `IHostClock { EstimatedHostTick; IsValid }` | Read by `ClientLayer` and Presentation | Host: `CurrentTick`, frozen during a solo pause. Client: monotonic non-decreasing, in the host tick domain, error ≤ 3 ticks p95 at 150 ms RTT. The estimator is Networking's. |

**What the Sim must not know:** peers, accounts, channels, RPC names, chunking, pacing, wall clock, RTT, per-peer filtering, packing, or any Godot type.

### N2 — Roles on the wire

- **Host process.** One `SimWorld` and one `ClientLayer` over the **loopback adapter**. Loopback uses the same op encoder. `FrameRunner` hands each batch to the `ClientLayer` inbox when `Tick()` returns, and `MirrorDrain` applies it at the **next client tick**. A host op enqueued in frame F applies at the next `Inputs.Apply`, and its reply is visible to the host `ClientLayer` at exactly the next client tick (Manual Delivery; HB8). Whether loopback can bypass the MultiplayerAPI is UNVERIFIED; this design assumes it can.
- **Remote client.** One `ClientLayer`, never a `SimWorld`. It never re-simulates the world (R29).

### N3 — Event replication contract

1. **One batch per tick, in raise order.** The adapter must never change the relative order of two entries delivered to the same peer.
2. **Audience.**
   - `Internal` entries never leave the process.
   - `OwnerOnly` and `Targeted` entries go only to the peers resolved from `audienceMask` inside `Deliver`.
   - `Replicated` entries go to all connected players.
3. **Replies** follow their events (R10). `busy` travels on the same reliable channel (R7).
4. **Overflow.** Networking resyncs exactly `droppedAudienceMask` (R14) and owns the cooldown.
5. **Join and reconnect.**
   - `joinTick` is the tick whose phase 0 applied the `PlayerJoin`.
   - The join snapshot is a point-in-time copy taken after that tick's `Deliver`.
   - The joiner **buffers** every batch with `tick > joinTick` until JoinState has applied, then replays them in order. A buffer cap breach forces a rejoin. It drops batches with `tick ≤ joinTick`.
   - Continuous snapshot blocks apply only when their tick is ≥ `joinTick`, after JoinState.
   - A reconnect follows the same rule and discards its old mirror.
6. **Missed events are not replayed.** Rejoin uses a state resync.

### N4 — Wire widths owned here

Tick is u32 (narrower only where wrap-safe, with `Serial*`). `EntityId` and `mailId` are u32. `PlayerSlot` is u8, 0–7. `opSeq` and `ordinal` are Sim-internal, never on the wire except in replay logs. Each op and event descriptor carries its wire codec, and a hash of the descriptor table is the protocol-version check (ADR 5/6).

**Host tick domain (HB6).** Any tick a client puts in a request is in the host tick domain (`IHostClock`), never a client's local counter. Manual Delivery's `holdStartTick` already is. Throwing's `ThrowRelease` carries no tick (registry `throwPayloadBytes`).

### N5 — Bandwidth

Bandwidth budgets and send pacing belong to Networking. The Sim's empty-tick cost is 0 B. The original estimates are seed §3.16c.

### N6 — Determinism across machines

Only R29 T2 is cross-machine. The world is never lockstepped, and a client never needs the world RNG: manifests, waves and drops arrive as events or state.

## Cross-References

| This document references | Where | What is used | Kind |
|---|---|---|---|
| `design/spec/07-technical-architecture.md` | §1–§4.3, §7.3, §8, §9 | Split, tick loop, ids, RNG, perf targets, tooling (DEV-1…DEV-8) | source (amended) |
| `design/spec/06-multiplayer-netcode.md` | §3, §6, §8, §9 | 30 Hz, join and 120 s reconnect windows, replay | source |
| `design/spec/13-v1-cut-and-decisions.md` | §2.2, §3, §4.3, §4.5, §6 | Client-authoritative movement, trust model, milestones | source (wins on conflict) |
| `design/spec/08-data-schemas.md` | §1, §3 | Content seconds → ticks; example ids | source |
| `design/gdd/grid-inventory.md` | R7, R12, R14, R19, rule (8), D6, H-DET-1, OQ12, OQ20 | Op order, phases, idempotent `reqId`, determinism; OQ20 resolved here | contract; HB1, HB2 |
| `design/gdd/player-character-movement.md` | P2, P13, P15, P19, PCM-P2-1 | Carrier step, knockdown, disconnect/reconnect | contract; HB5 |
| `design/gdd/input-controls.md` | R11, H-EC-4, H-R2-2 | Input frame, first-tick edges, pause | contract |
| `design/gdd/mail-destinations.md` | R5, R6, F8 `wallet_floor`, M-PERF-2 | Phase order, streams, `NextInt` | contract; HB4 |
| `design/gdd/manual-delivery.md` | MD6, M-NET-2 | Op order, `IHostClock`, loopback timing, `reqId` duplicate | contract; HB8 |
| `design/gdd/throwing-catching.md` | step order, `flightId`, TC-P-2 | Phase 4, serial helpers, `throwNoDice` | contract; HB3, HB6 |
| `design/gdd/hud-notifications.md` | `deadlineTick` | F1 display rule, `IHostClock` | contract; HB9 |
| `design/gdd/content-data-definitions.md` | loader, ContentValidator | F1 parsing, `SimSettings` validation | contract |
| `design/gdd/game-pillars.md` | Pillars 1–5 | Player Fantasy, R9 | design intent |
| `design/registry/entities.yaml` | Sim Core entries; `mailUnitPay`, `mailSpawnValue`, `misdeliveryPenalty`, `backlogDrainPerTick`, `throwNoDice`, `throwPayloadBytes` | Rounding, tick literals | registry |
| `docs/architecture/simulation-core-adr-inputs.md` | all | Moved algorithms, layouts, perf slices, original ACs (UNREVIEWED SEED) | ADR input |
| `docs/engine-reference/godot/` | VERSION.md, breaking-changes.md | Jolt default (new projects), .NET 8 minimum (non-Android) | engine reference |

## Acceptance Criteria

*Specialists: qa-lead (round 3; review 2026-10-16). The set was cut from 131 to 53 contract ACs (CD ceiling 55; TD ceiling 25 at M-1a). The originals, verbatim, and their disposition (story test plans, ADR validation, owning GDDs) are in seed §2b and §3.17. Every rule kept above has at least one AC.*

**Legend.**
- **Milestones:** [M-1a] (25 ACs), [M-1b] (18) and [M0] (10).
- **Types:** [Logic], [Integration], [Perf] and [CI].
- **Configurations.** The suite runs in **Debug and Release**. "Debug asserts" means the call throws `SimAssertException` under `SIM_ASSERT`.
- **Counters.** "Counted X" means `SimCounters[X]` increased by exactly 1, read between ticks.
- **Records.** "Record" means one entry in the R31 record ring.
- **Determinism.** Every AC uses fixed seeds and fixed inputs.
- **Clocks.** Frame-loop ACs drive the Godot-free `FrameRunner` with an injected `IMonotonicClock`, never a Godot node.

### Tick, roles, phases, composition
- **SC-R1-1** [M-1a][Logic] **GIVEN** a `Ready` world and a timer with `deadlineTick` = 100 **WHEN** `Tick()` is called 101 times **THEN** handlers see `CurrentTick` = 0 during the first call, it reads 1 after it and 101 after the last, and the timer is not due at `CurrentTick` 99 and due at 100 (R15).
- **SC-R2-1** [M-1a][Logic] **GIVEN** a host `FrameRunner` over loopback whose clock advances so `Advance` returns 3 **WHEN** `RunFrame()` runs **THEN** the call log reads Client, Sim, Client, Sim, Client, Sim, edges attach only at i = 0, and the transport flush runs after the last `Tick()` returns.
- **SC-R2-3** [M0][Integration] **GIVEN** a remote sample ingested in phase 3 of tick T **WHEN** the host's own `ClientLayer` reads `IRemoteCapsuleSource` during tick T **THEN** it returns the T−1 ingest.
- **SC-R3-1** [M-1a][Logic] **GIVEN** stubs bound to every phase, two stubs bound to phase 11 and two tick-end checks **WHEN** one tick runs **THEN** the order is phases 0…12, both phase-11 stubs in composition order, both tick-end checks once each in composition order, then 13.
- **SC-R4-1** [M-1a][Logic] **GIVEN** a builder **WHEN** each row is attempted (a second handler for one `OpKind`; an `OpKind` with no handler; a 9th reader; a bind after `Freeze()`; a constructor dependency cycle) **THEN** each throws `SimBuildException` with its own code, one row per Theory case.
- **SC-ARCH-1** [M-1a][CI] **GIVEN** the built Sim assembly **WHEN** the architecture tests run **THEN** they all pass: no reference to any `Godot*` assembly and no `using Godot` under `src/Sim`; `ClientLayer` names no `SimWorld` or gameplay type; no static mutable field; no clock API in `Hosting/`; every system constructor parameter on the allowed-dependency list; each `RngStream` handed only to its registered owner.
- **SC-ARCH-2** [M-1a][CI] **GIVEN** fixture files under `src/Sim` using `float`, `double`, `decimal`, `System.Random`, `Guid.NewGuid`, `GetHashCode`, `DateTime` or `Stopwatch` **WHEN** CI builds **THEN** each fails with an analyzer error, and a grep finds no `SceneTree.Paused` in the Sim or `GameLoop`.

### Ops and replies
- **SC-R6-1** [M-1a][Logic] **GIVEN** 1 000 enqueues where every 10th is refused (ring held full), one carrying a payload that names another player **WHEN** they are enqueued and drained **THEN** accepted ops have strictly increasing, never-reused `opSeq`; refused ones consume none; the handler sees the adapter-stamped `actor`; and `System` = 0x00000001.
- **SC-R7-1** [M-1b][Logic] **GIVEN** capacity 4096 and reserve 64 with 4 032 player ops queued **WHEN** a player op, then a `System` op, is enqueued, and separately `EnqueueOp` is called on a `Constructed` and a `Stopped` world **THEN** the player op returns false, consumes no `opSeq` and is counted `busy`; the `System` op is accepted; both other calls return false and are counted.
- **SC-R8-1** [M-1a][Logic] **GIVEN** `maxOpsDrainedPerTick` = 64 and 100 queued ops **WHEN** one tick runs, and a test hook enqueues an op during phase 0 **THEN** exactly ops 1–64 apply in `opSeq` order, `drainBacklog` reads 36, ops 65–100 apply next tick before the hooked op, and the hooked op applies on a later tick.
- **SC-R8-2** [M-1a][Logic] **GIVEN** a test handler that validates and rejects **WHEN** its op drains **THEN** the dump hash, every allocator and every stream's `draws` are unchanged except `opSeq`; the only log entry is its `OpReply`; and `IOpRecorder.OnDrained` is called once with the apply tick and the result.
- **SC-R8-3** [M-1b][Logic] **GIVEN** an actor with ops whose `opSeq` falls just below and just above its `PlayerLeave`, an op with an unregistered `kind` and one with a wrong payload length **WHEN** they drain **THEN** the lower op applies; the higher is rejected `unknown_actor`; the others are rejected `unknown_kind`; and all four consume `opSeq` and are recorded.
- **SC-R8-4** [M0][Perf] **GIVEN** SC-FIX-WORST in Release with 8 players **WHEN** 18 000 ticks run **THEN** the event log's high-water mark stays ≤ `eventLogCapacity − eventLogReserve`, overflow count is 0, and the peak `drainBacklog` is reported.
- **SC-R9-1** [M-1b][Integration] **GIVEN** one free cell targeted by a player op (phase 0) and an inserter (phase 5) in one tick **WHEN** the tick runs **THEN** the player op wins, the inserter's call fails with no `OpReply`, and the inserter succeeds on a later tick once a cell is free.
- **SC-R10-1** [M-1a][Logic] **GIVEN** a handler that raises 2 events and commits, a Core reject (`unknown_kind`) and a `System`-actor op **WHEN** they drain **THEN** the log reads event, event, `OpReply` (OwnerOnly, reason 0, `reqId` echoed unchanged); the Core reject carries a reason in 1–63; and the `System` reply has an empty audience.
- **SC-R10-2** [M-1b][Logic] **GIVEN** two consecutive ops on one container whose handlers each raise and then `Update` a `Coalescible` replace **WHEN** they drain **THEN** the log reads replace₁, reply₁, replace₂, reply₂, and `Update` on replace₁ from the second call asserts in Debug and is lost and counted in Release.

### Event bus
- **SC-R11-1** [M-1b][Logic] **GIVEN** two consecutive ticks that each raise entries **WHEN** they run **THEN** ordinals are 0…n−1 in each tick, the log is empty at the start of the next, and in Debug a span kept from the previous `Deliver` reads 0xDD.
- **SC-R12-1** [M-1b][Logic] **GIVEN** phase 10 raising A and B (matching the phase-11 reader's filter) and C (not matching) **WHEN** the phase-11 reader reads twice and raises D **THEN** the first read returns A and B only, the second returns none, and D's ordinal is greater than C's.
- **SC-R12-2** [M-1a][Logic] **GIVEN** a sink stub **WHEN** a tick runs **THEN** `Deliver` is called exactly once, from phase 13, with every entry (Internal included) in raise order, each carrying its class and mask, and the stub can make no Sim call (re-entry asserts).
- **SC-R12-3** [M-1b][Logic] **GIVEN** the event descriptor table and a kind registered without a class **WHEN** the enumeration test runs **THEN** the undeclared kind fails the test and resolves to `Internal`; an `OwnerOnly` entry with 0 bits reaches no peer; one with 2 bits asserts in Debug and reaches the lowest bit's peer in Release.
- **SC-R14-1** [M-1a][Logic] **GIVEN** a small log and three audiences, where slot 2's only entries fall after the limit **WHEN** the log overflows **THEN** later entries are dropped and counted, nothing is reordered, state is not rolled back, and `overflowInfo` = { true, slot 2's bit OR the other dropped masks, the dropped kinds }; a dropped `Replicated` entry sets every connected slot.
- **SC-R14-2** [M-1b][Logic] **GIVEN** capacity 4096 and reserve 256 **WHEN** non-reader kinds fill the log **THEN** they stop at 3 840, while `PlayerDied` (a reader kind) and `OpReply` are still accepted up to 4 096.

### Time, pause, driver
- **SC-R16-1** [M-1b][Logic] **GIVEN** a `TickDriver` **WHEN** its state table is exercised **THEN** every row holds:
  - `Advance` while `Idle` returns 0.
  - Paused, `Advance` 10 s apart returns 0 with `Alpha` 0.
  - After `Resume(t0)`, `Advance(t0 + tick − 1)` returns 0 and `Advance(t0 + tick)` returns exactly 1.
  - `Resync` clears `acc30` with no hitch record.
  - A double `Pause`/`Resume` is a no-op.
  - `droppedTicksTotal` is unchanged throughout.
- **SC-R16-2** [M-1b][Integration] **GIVEN** a 10 s solo pause with Grid Inventory D6 and Input H-R2-2 timers armed and ops enqueued while paused **WHEN** play resumes **THEN** the ops apply on the first tick after resume, and each timer expires after the same number of ticks as without the pause.
- **SC-R17-1** [M-1a][Logic] **GIVEN** a frame that runs 3 ticks with a press and a release latched, and a later frame that runs 0 ticks with one press latched **WHEN** the edges are consumed **THEN** both edges attach to the first tick only, ticks 2–3 carry held bits and the latest `moveIntent`, and the 0-tick frame keeps its edge latched for the next tick.
- **SC-F2-1** [M-1a][Logic] **GIVEN** freq = 10^7 **WHEN** each table row is applied **THEN** each row matches:

  | Row | Run | Dropped | acc30 / Alpha | Other |
  |---|---|---|---|---|
  | 150 ms from 0 | 4 | 0 | 5 000 000 / 500 ‰ | |
  | the same 150 ms again | 4 | 1 | 0 / 0 | |
  | 400 ms from 0 | 4 | 8 | — | one hitch record |
  | 5 s | 4 | 26 | — | `clampedTimeTotal` grows by 4 s |
  | a backwards timestamp | — | — | nothing added | `last` unchanged, `backwardsClock` counted |
  | 600 frames of 166 667 counts | 0, 1, 0, 1 … | 0 | Alpha exactly 500, 0 … | |
  | freq 999 or 10^12 + 1 | — | — | — | the constructor throws (10^3 and 10^12 succeed) |
- **SC-F2-2** [M-1b][Logic] **GIVEN** a seeded random Δ sequence plus 1 000 frames of 125 ms **WHEN** it runs **THEN** `acc30 < freq` after every `Advance`, the F2 invariant holds exactly after every frame, and the 125 ms run drops nothing.
- **SC-F2-3** [M0][Logic] **GIVEN** `catchUpGuardMs` = 10 and an injected clock where the first tick costs 12 ms, and separately 300 frames whose ticks each cost 40 ms **WHEN** the frames run **THEN** exactly 1 tick runs in each frame, `DropRemaining` counts the rest as dropped, and the F2 invariant holds.

### Identity and players
- **SC-R19-1** [M-1a][Logic] **GIVEN** the id scheme **WHEN** player 1 and construct 1 are allocated, 50 331 649 is decoded, and a 3-unit then a 0-unit mail batch allocate **THEN** the ids are 16 777 217 and 50 331 649; the decode gives class 3, counter 1; `None` = 0; classes 0x09–0xEF cannot be allocated in a shipped build; and the mail ids are 3 consecutive values from a separate counter, with none for the empty batch.
- **SC-R20-1** [M-1b][Logic] **GIVEN** worlds constructed with a class counter at 2^24 − 1, `MailIds` at 2^32 − 1 and `opSeq` at 2^32 − 1 **WHEN** each allocates or enqueues **THEN** they return `None`, 0 and false respectively, each writes one record, and nothing wraps.
- **SC-R21-1** [M-1a][Logic] **GIVEN** `PlayerJoin` ops in Prep **WHEN** they apply **THEN** each new account gets a fresh `player` id and the given slot, and `AddPlayer` is reachable only from that handler. As Theory rows, a slot ≥ 8 gives `slot_invalid`, a slot freed by a `PlayerLeave` in the same drain gives `slot_busy`, and a connected `playerId` gives `duplicate_player`, each allocating nothing. After the last player leaves, the world keeps ticking.
- **SC-R21-2** [M0][Integration] **GIVEN** a Shift phase with one player Frozen after a disconnect **WHEN** that account's `PlayerJoin` (carrying its `playerId`) and a new account's `PlayerJoin` drain **THEN** the reconnect is accepted into the same slot with the same `playerId`, the new account is rejected `join_closed`, and the reconnecting client applies only batches with tick > `joinTick` after its JoinState.

### Randomness
- **SC-R24-1** [M-1a][Logic] **GIVEN** `mail` streams for shifts 1 and 2 of one seed, `Create(seed, "mail", 1)` called twice, and `mail` with shift 0 **WHEN** they are drawn **THEN** the shifts' sequences differ; the two shift-1 streams are identical and the dump holds one entry for that key with `draws` reset; and shift 0 asserts in Debug, while Release derives it and counts it.
- **SC-R25-1** [M-1a][Logic] **GIVEN** an `IRngSource` stub returning 0x80000000, 0xFFFFFFFF, 0 and then any value **WHEN** `NextInt(10)` ×3 and `NextInt(0)` are called **THEN** they return 5, 9, 0 and 0, and `draws` increases by exactly 1 per call.
- **SC-R26-1** [M-1b][Logic] **GIVEN** 3 live streams drawn 5, 0 and 7 times, and a composed world with 100 throws launched **WHEN** the dump is written and the registry inspected **THEN** it records `(name, index, state, inc, draws)` with 5, 0 and 7, Throwing holds no stream handle, and total `draws` are unchanged by the throws.
- **SC-F5-1** [M-1a][Logic] **GIVEN** the golden vectors **WHEN** the primitives run **THEN** `pcg32_srandom(42, 54)` gives `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293`; `Mix64(0)` = `0xe220a8397b1dcdaf`; `Fnv1a64("mail")` = `0x1f5964a2ce98072e`; `Derive(1, "mail", 2)` = `0x120344c795c4b10e`; and `Create(1, "mail", 2)` gives `0x915ab962, 0xc3f6d39c, 0x5d7b280d`. This holds in Debug and Release.

### Numbers, determinism, dump
- **SC-F1-1** [M-1a][Logic] **GIVEN** F1 **WHEN** it is evaluated **THEN** each case matches:
  - `contentTicks`(400, 150, 15 000, 0, 1, 100 ms) = 12, 5, 450, 0, 1, 3.
  - `ticksToMsFloor(1)` = 33 and `ticksToMsCeil(1)` = 34.
  - The clock at r = 360, 331, 330, 1, 0, 2 160 reads 0:12, 0:12, 0:11, 0:01, 0:00, 1:12.
  - r = 200 000 reads "99:59".
  - A deadline 10 ticks in the past reads 0:00.
- **SC-F7-1** [M-1a][Logic] **GIVEN** the F7 helpers **WHEN** each case is evaluated **THEN** each case matches:
  - `MulPermille`(15, 1500) = 23 and (−15, 1500) = −23; (2^62, 10^6) = 2^62, counting `moneyClamped`.
  - `MulDiv`(7, 1, 2) = 3 and (−7, 1, 2) = −3; a zero divisor gives 0, counted.
  - `MulDivRH(long.MinValue, long.MinValue, 1)` clamps and counts without throwing.
  - `Fx16.ToMm`(98 304) = 1 and (−98 304) = −2.
  - A `Cents` add crossing 2^62 asserts in Debug, and in Release saturates and counts `moneySaturated`.
- **SC-DET-1** [M-1a][Logic] **GIVEN** two `SimWorld`s in one process (run in parallel) with the same `ContentSet`, `SimSettings` and `runSeed`, a recorded 50 000-op log and its ingested-sample log **WHEN** the logs are applied one op per tick and SHA-256 of the dump is taken after every tick **THEN** all hashes match (H-DET-1, R29 T1). A third world with a different `runSeed` diverges.
- **SC-DET-2** [M0][CI] **GIVEN** the SC-F5-1 vectors and PCM-P2-1's 600-tick script **WHEN** they run on Windows and Linux x64 CI **THEN** every output and hash matches exactly (R29 T2).
- **SC-R30-1** [M-1b][Logic] **GIVEN** a dumped world **WHEN** each included item (tick, each id counter, mail counter, `opSeq`, a stream state, a queued op, a latched sample) is changed in turn, then each excluded item (a perf counter, the record ring), then the insertion order of a keyed collection, and a dump is requested from inside `Tick()` **THEN** the hash changes for every included item and for no excluded item or reordering, and the in-tick request returns false, counting `dumpDuringTick`.
- **SC-R32-1** [M0][Perf] **GIVEN** SC-FIX-WORST in Release with no recorder, 300 warm-up ticks, and a dedicated process **WHEN** `GC.GetAllocatedBytesForCurrentThread()` is sampled around 18 000 ticks **THEN** the delta is 0 B, no Sim collection grows, the gen-0 count delta is 0, and the Sim heap is ≤ 64 MB after the run.
- **SC-PERF-1** [M0][Perf] **GIVEN** `production/qa/perf-baseline.md` and a calibration loop in the same CI job **WHEN** SC-FIX-WORST and SC-FIX-STEADY run (18 000 ticks, Release, p99 by the F3 integer rank) **THEN** the build fails if the total p99, or the p99 of any phase with a baseline ≥ 0.2 ms, exceeds 2 × the normalised baseline. The absolute 5/8/3 ms and 1.5/3.0 ms results are reported as advisory (OQ6).
- **SC-SET-1** [M-1b][Logic] **GIVEN** `SimSettings` rows breaking each F8 constraint (a non-power-of-two capacity, a reserve above capacity/4, a drain cap below 32, cap × 4 + 570 above capacity − reserve) and one valid row **WHEN** a world is built **THEN** each invalid row fails with its own code and builds no world, and the valid row builds.

### States
- **SC-ST-1** [M-1a][Logic] **GIVEN** a world in each state **WHEN** every transition and `Tick()` call is attempted **THEN** only Constructed → Ready → Running → Stopped/Faulted is legal, and `Tick()` on a non-`Running` world or re-entrantly runs no phase, leaves `CurrentTick` unchanged, flushes nothing and counts `badTick`.
- **SC-ST-2** [M-1b][Logic] **GIVEN** `IRunControl.RequestStop()` called in phase 12 of tick T, in phase 5 of another world, and twice **WHEN** the ticks finish **THEN** each world completes its tick through `Events.Flush` with no extra tick and becomes `Stopped`; the phase-5 call counts `stopOutOfPhase`; the second stop is ignored and counted; and a host-adapter `Stop()` outside a tick transitions immediately.
- **SC-ST-3** [M-1b][Logic] **GIVEN** a phase that throws, in Release **WHEN** the tick runs **THEN** the world becomes `Faulted`, `Deliver` is not called for that tick, the re-entrancy flag is clear, the dump request returns false, and a further `Tick()` counts `badTick`. In Debug the exception is rethrown after the world is marked `Faulted`.

### Misuse and cross-system
- **SC-MIS-1** [M-1b][Logic] **GIVEN** each misuse row **WHEN** it is triggered **THEN** Debug throws `SimAssertException`, and Release gives the row's outcome:

  | Misuse | Release outcome |
  |---|---|
  | Raise outside `Tick()` | dropped, counts `eventsOutsideTick` |
  | Raise in a tick-end check | dropped, counts `eventsAtTickEnd`, sets `overflow` |
  | `EnqueueOp` or `Tick()` from a non-owner thread | Debug only |
  | A `Combat.React` reaction raises damage | applied at phase 10 of T+1, counts `lateDamage` |
  | `WireNarrow` overflow | clamped, one record |
  | `Teleport` called during tick end | rejected and counted |
- **SC-X-1** [M-1a][Integration] **GIVEN** a real Grid Inventory op that changes a container, and a real throw op **WHEN** they apply **THEN** each batch lists the system's events before its `OpReply`, and the throw reads `Removed`, `ThrowLaunched`, then the ok reply.
- **SC-X-2** [M-1a][Integration] **GIVEN** a Manual Delivery request from the host's own client in frame F **WHEN** frames run **THEN** each step lands as specified:
  - The request applies at the first `Inputs.Apply` after F.
  - Host Presentation sees the resulting events in the frame that ran that tick.
  - The host `ClientLayer` sees the reply at exactly the next client tick, with no frame-delayed queue.
  - The host `IHostClock` reads `CurrentTick`.
- **SC-X-3** [M0][Integration] **GIVEN** a host loopback op and a remote op received in the same frame **WHEN** the tick runs **THEN** the remote op has the lower `opSeq`.
- **SC-X-4** [M0][Integration] **GIVEN** a slot freed at tick T and reassigned at T+1, with `OwnerOnly` events for the leaver in T and the adapter sending every 2nd tick **WHEN** the batches are sent **THEN** the newcomer's peer receives none of the leaver's entries, and no transport call happens inside `Tick()`.
- **SC-X-5** [M0][Integration] **GIVEN** the nightly TC-X-1 / H-R19-1 soak (8 players, 18 000 ticks, seeded, ReleaseAssert) **WHEN** it runs **THEN** no assertion fires and the record ring holds no always-on failure.

## Open Questions

### Engine-dependent decisions (UNVERIFIED; Technical Setup spike + ADR, owner technical-director, before M-1a code)

No `docs/engine-reference/godot/modules/core.md` exists, so the Core domain was **not checked against the pinned engine**. What each spike measures is in seed §3.18. The spike items formerly written as ACs (SC-R17-5, SC-R18-2, SC-R33-2, SC-ALLOC-2) are spike-plan items, not ACs.

| # | Question | Recommended default |
|---|---|---|
| OQ1 | Tick driver and FrameRunner (resolves Grid Inventory OQ20, Player Character OQ6). Also measure 30 Hz judder on 120/144 Hz hosts. | R17 |
| OQ2 | Sim thread and transport callback thread (shared with Grid Inventory OQ12) | R18: main thread; callbacks only enqueue |
| OQ3 | RNG and hash function ADR (answers Mail md:847) | F5/F6; SHA-256 in tests; `CanonicalHash64` deferred |
| OQ4 | Sim assembly shape and build configurations (Godot `ExportDebug`/`ExportRelease` mapping, no trimming, .NET SDK minimum for 4.6/4.7) | R33 |

### Design questions

| # | Question | Owner | Target |
|---|---|---|---|
| OQ5 | Sign off the budget numbers (5/8/3 ms, per-phase slices, reserve ≥ 0.3 ms). State whether the 10 ms render figure covers network send and per-peer encoding (now outside `Tick()`, R12). Then create `docs/architecture/perf-budget.md`. | technical-director + performance-analyst | Before M0 |
| OQ6 | No reference machine exists for absolute ms gates (also HUD HN-19, Input OQ-IC-17). | technical-director / producer | M1 |
| OQ7 | Spec 07 drift DEV-1…DEV-8 is closed by each ADR recording its DEVs. | technical-director | With the ADRs |
| OQ8 | Systems-index edges: Manual Delivery, Throwing, Mail, Input and HUD rows do not list Simulation Core. Health & Damage → Player Character is a phase-11 reaction edge. | producer (`/map-systems`) | Next index pass |
| OQ9 | Re-derive per phase: SC-FIX-WORST counts, `systemEventsPerTick`, and the `world_item` allocation rate. | Enemy AI, Belt Logistics, Vehicles, Grid Inventory | When written |
| OQ10 | The float ban is assembly-wide. Dictionary/HashSet iteration (R28 rule 3) needs a custom Roslyn analyzer with a failing fixture; until then it is a review rule. | lead-programmer | ADR 9 |
| OQ11 | Free Play save/load (`Loading` state, construct from dump) and dedicated-server budgets are deferred. | technical-director | Post-v1 |
| OQ12 | Playtest protocol for the Player Fantasy tests | qa-lead | M0 |
| OQ13 | Scope of the Provisional re-validation after the greybox. Only Player Fantasy, Game Feel and R16 depend on it. | creative-director | After M-1a |
| OQ14 | Mid-Delivery entry for a player whose 120 s window expired (spec 06 §6 says "next Prep"; PCM P19 leaves it to Networking) | Networking & Session | When written |

### Hand-backs to other GDDs (not edited here; the owning GDD applies them; original wording in seed §3.18)

- **HB1 — Grid Inventory:** (a) R7/gi:360: the player op (phase 0) beats an inserter's `TryInsert` (phase 5), and the loser retries; (b) its APIs are callable from any phase except tick end and flush; (c) R19 runs as an `ITickEndCheck`; (d) `worldItemCap` is enforced in phase 1 only; (e) shared Sim test project; (f) the order key is `opSeq`; (g) **new:** coalescing is per call, so "one `replace` per container per tick" becomes one per call (R13); (h) **new:** take SC-R11-2; (i) **new:** the rule (8) dedupe cache is handler state in its dump contributor.
- **HB2 — Grid Inventory perf:** H-PERF-1/2/3 take their slices from the TD perf budget (seed §2).
- **HB3 — Throwing:** no stream (`throwNoDice`). Any future randomness is a registered stream indexed by a u32 launch ordinal, never `flightId`. Take SC-R24-2.
- **HB4 — Mail:** `mail`/`mail.special` per shift; M-PERF-2 split by phase (perf budget); synchronous wallet debits; "arrival order" = `opSeq`. Take the original SC-X-1 (four-way same-tick order, seed §3.17; the id is reused here).
- **HB5 — Player Character:** P15 is a phase-11 reader; `Teleport` is an `IPlayersApi` call and `TeleportAck` an `OpKind`; samples carry a per-sender sequence and are stamped at the latch drain (R5); `FreezePlayer` is its handler and the Frozen expiry calls `RemovePlayer` (R21). Take SC-R5-1/2.
- **HB6 — Throwing:** TC-P-2 slice from the perf budget; `ThrowRelease` carries no tick; catches and landings are synchronous phase-4 calls.
- **HB7 — Health & Damage** (not written): one `PlayerDied` per carrier per tick, from the first lethal entry; reactions in phase 11. Take SC-EC-1/2 and the original SC-X-2 (knockdown log order, seed §3.17; the id is reused here).
- **HB8 — Manual Delivery:** add Simulation Core (Hard) to its Dependencies; M-NET-2 "same or next tick" → "exactly the next client tick" (N2); replies, `busy` included, are matched by `reqId` (R7); the duplicate-`reqId` promise rests on Grid Inventory rule (8), not on Sim Core (R10).
- **HB9 — HUD & Notifications:** add Simulation Core (Soft, Presentation-only); `renderMs` comes from `IHostClock`.
- **HB10 — Networking & Session** (not written): never resend a drained op; resync exactly `droppedAudienceMask`, with cooldown and per-system scoping; N3.5 join/reconnect buffering; the `IHostClock` client estimator against N1's bound; refuse `Pause()` with any peer and call `Resume()` before `PlayerJoin` (take SC-R16-4); `busy` on the reply channel; resolve peers inside `Deliver`; bandwidth and pacing (seed §3.16c).
- **HB11 — Content & Data:** take SC-F1-3 (content-seconds parsing); the ContentValidator also runs the F8 `SimSettings` constraints.
- **HB12 — Run Structure** (not written): end a run only through `IRunControl.RequestStop()` in phase 12. Take SC-EC-3.

### ADRs to write (technical-director ruling, 2026-10-16)

1. Sim assembly and build configurations (OQ4, DEV-7).
2. Tick driver and FrameRunner (OQ1).
3. Sim thread and transport boundary (OQ2).
4. Phase order, composition root and cross-system calls (DEV-1/2/3/6/8).
5. Op queue, drain cap and replies.
6. Event log and replication classes.
7. Identity scheme (DEV-4).
8. RNG streams (OQ3, DEV-5).
9. Numerics, determinism rules, dump and replay (R27–R30, R32).

Inputs for each are in `docs/architecture/simulation-core-adr-inputs.md` §0.
