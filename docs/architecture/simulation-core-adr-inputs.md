# Simulation Core — ADR Inputs

> **UNREVIEWED SEED — input to TD ADRs, not a contract.**
> Created 2026-10-16 by the `/design-review` revision of `design/gdd/simulation-core.md` (creative-director scope ruling: the GDD keeps behaviour and contracts; algorithms, storage, build mechanics, thread model and perf slices move here). It holds (a) review corrections the ADR authors must apply, (b) the technical-director's perf-budget rulings, and (c) the moved blocks, **verbatim as reviewed** (receipt `f4b99598e4ecc6a09f44d8dbaff7ae136d328f90`), so nothing was deleted.
> **Precedence:** on any conflict the revised GDD wins. A verbatim block here may carry a defect the review found; section 1 lists the correction. Nothing in this file is reviewed or approved. Each ADR that consumes a section records that here.
> **Owner:** technical-director. **Engine facts:** everything Godot-side is UNVERIFIED unless `docs/engine-reference/godot/` sources it.

## 0. ADR list (technical-director ruling, 9 ADRs)

| # | ADR | Closes | Seed sections |
|---|---|---|---|
| 1 | Sim assembly and build configurations. Symbols are keyed on `$(Optimize)` or a custom property, never on `Configuration == Release`. | OQ4, DEV-7 | 3.9 (R31–R33) |
| 2 | Tick driver and FrameRunner: F2 algebra, guard, pause | OQ1 | 3.4, 3.10 |
| 3 | Sim thread and transport boundary. Waits for the multiplayer spike. "MPSC, nothing else changes" is deleted. | OQ2 | 3.4 (R18) |
| 4 | Phase order, composition root (`Composition/`) and cross-system calls | DEV-1/2/3/6/8 | GDD R3–R4 |
| 5 | Op queue, drain cap and replies (reason namespace, wire codec per OpKind descriptor) | — | 3.1 |
| 6 | Event log and replication classes (entry layout, payload ceiling, cursor and coalescing mechanics, overflow masks) | — | 3.2 |
| 7 | Identity scheme | DEV-4 | 3.5, 3.12 |
| 8 | RNG streams, F5/F6 (`CanonicalHash64` deferred to its first consumer) | OQ3, DEV-5 | 3.13 |
| 9 | Numerics, determinism rules, dump and replay (R27–R30, R32) | — | 3.6–3.8, 3.14 |

OQ7 (spec 07 drift) is closed by each ADR recording its DEVs. OQ5 is closed by the perf-budget doc (section 2), not by an ADR. The TD overruled lead-programmer's split of ADR 9: numerics are the main lever for determinism.

## 1. Review corrections the ADRs must apply (from the 2026-10-16 review)

**F7 `MulDivRH` overflow** [systems-designer]. The verbatim `sign(a·b·c) × ((2·|a·b| + |c|) / (2·|c|))` overflows Int128 at `a = b = long.MinValue`, and `Math.Abs(long.MinValue)` throws. Use instead: `neg = (a<0) ^ (b<0) ^ (c<0)`, then `n = |a|·|b|` and `d = |c|` as `UInt128`, then `q = n / d`, `r = n % d`, `q += (2r ≥ d) ? 1 : 0`. Write `2r ≥ d` as `r ≥ d − r` so nothing overflows. `c = 0` is a fault: it returns 0 and is counted. A result outside the caller's domain is clamped and counted (±2^62 for money, ±2^47 for Fx16).

**`checked`/`unchecked`** [godot-csharp-specialist, systems-designer].
- `checked` fires at ±2^63, not at the `Cents` bound ±2^62, so `Cents` needs an explicit range check.
- A project-wide Debug `CheckForOverflowUnderflow` makes PCG, `Mix64` and FNV throw. They must be written `unchecked(...)`, or the Debug and Release dumps differ.
- Write `(uint)class << 24`; a byte 0xF0 shifted as `int` goes negative.
- XSH-RR narrowing is unchecked. Write the rotate as `(32 − (int)rot) & 31`.
- The `NextInt` int overload asserts `b ≥ 0`.

**F2 two-step driver** [systems-designer, qa-lead, technical-director].
- `Advance`: `n = min(due, cap)`; `acc30 −= n·freq`; if `due > n`, then `dropped += due − n` and `acc30 %= freq`; return n.
- `DropRemaining(m)`: `droppedTicksTotal += m`, with no change to `acc30`.
- Invariant: `ran + dropped + acc30/freq = Σ min(Δ, freq)·30/freq`.
- A backwards jump is counted as `backwardsClock`. A jump over `freq` auto-resyncs.
- FrameRunner (Godot-free, `Hosting/`, injected `IMonotonicClock`) owns the pump order, the first-tick edge flag, the guard and `DropRemaining`, and the transport flush after `Tick()` returns. `GameLoop` becomes one `RunFrame()` call.

**F8 sizing** [systems-designer, technical-director].
- With `maxOpsDrainedPerTick` (default 64), `peakOpsPerTick = cap`.
- Constraint: `cap × entriesPerOp + 1.5 × systemEventsPerTick ≤ eventLogCapacity − eventLogReserve`, which gives 826 ≤ 3 840.
- Re-derive `systemEventsPerTick` per phase from the coincident worst tick. The 380 estimate omits phase 1 (100 expiries) and phase 2 (32 placements).
- Include the reserves: `opQueueCapacity ≥ nextPow2(Q·rate·burst·players + systemOpReserve)`.
- `systemOpReserve` 64 is enough: the worst disconnect storm is about 24 System ops.

**F4 allocation rate** [systems-designer]. Ops are not the only allocation source: spills, expiries, feeders, waves and throws also allocate. Re-derive per class. `world_item` is the dominant class. Per-class capacity is about 4 660 allocations/s for a 1 h run.

**F5 collision figure** [systems-designer]. Replace "~10^-8" with this: FNV collisions are excluded by the startup test, and seed-level collisions are about C(50,2)/2^64 ≈ 7 × 10^-17. Add a per-name allowed-index column to `RngNames` (`mail` is 1–5).

**Op struct** [godot-csharp-specialist]. Zero-initialise the padding and union tail, and write the dump field by field (a raw blit breaks T1). Use `Unsafe.SizeOf<T>()` for the ≤ 48 B test. Union fields are unmanaged, and `Int128` is not in the union (16 B alignment).

**R28 analyzer** [godot-csharp-specialist]. BannedApiAnalyzers bans symbols. It probably does not flag the implicit `GetEnumerator` in a `foreach`, and it cannot see a `Dictionary` passed as `IEnumerable`. Rule 3 needs a custom Roslyn analyzer with a deliberately failing fixture. Until then it is a review rule (GDD OQ10).

**R32 measurement** [godot-csharp-specialist].
- `GC.CollectionCount(0)` is process-wide, so run that gate in a dedicated process.
- Warm-up allocations come from static init, delegate caches and type loading, not from tier-up.
- Non-capturing lambdas are cached from C# 11.
- Add to the ban list: exceptions, `Enum.GetValues`, struct enumerators boxed as `IEnumerable`, `async`, `yield`.
- `GetAllocatedBytesForCurrentThread` is a CoreCLR API, so the export spike is a smoke check.

**R33** [godot-csharp-specialist]. The `Deterministic` csproj flag means reproducible builds; it is not runtime determinism. Godot.NET.Sdk `ExportDebug`/`ExportRelease` configurations (UNVERIFIED for 4.7.1) must still give an optimised Sim with `AssertsEnabled == false`. State "no trimming", because descriptor tables use reflection.

**Network codecs** [network-programmer]. Each `OpKind` and event-kind descriptor carries its wire codec and wire size. A hash of the descriptor table is the protocol-version check. The envelope belongs to Networking.

**Thread** [godot-csharp-specialist]. Moving to an MPSC ring re-specifies R5 (sample stamping), R6 (`opSeq` stamping) and R7 (full-ring reply and thread assert).

**Judder risk** [technical-director, not blocking]. On a 120/144 Hz host, a 5 ms tick inside a 6.9 ms frame causes 30 Hz judder. Add this to the OQ1 spike and the R18 triggers.

## 2. Perf budget (technical-director-owned; moves to `docs/architecture/perf-budget.md` when OQ5 is signed off, before M0)

TD rulings 2026-10-16 (they amend the verbatim F3 in 3.11):
- **Accepted as PROPOSED:** 5.0 / 8.0 / 3.0 ms with a 0.50 ms reserve. The margin at 60 fps is about 1.3 ms.
- **Deleted:** "a spike source counts against p99 only if it hits ≥ 2 %" and "Σ p99 is a conservative bound". Per-phase p99s are for attribution only.
- **Spike cadence:** each spike source fires at the cadence its owning GDD sets. Full coincidence is forced every **150th** tick (0.67 %), so max gates the coincident ticks and p99 gates the rest.
- **Integer rank:** `r = (99N + 99) / 100`, and `p99 = sorted[r − 1]`.
- **Reserve:** `budgetReserve` is derived (`tickBudgetP99 − Σ slices`) and must stay ≥ 0.3 ms. Budgets are test thresholds, not `SimSettings` keys.
- **SC-FIX-WORST:** ops change to 8 players × 2 ops per tick (F8 keeps 60 ops/s/player), and the coincident tick drains exactly 64 ops.
- **Relative 2× gate:** blocking only for the total p99 and for phases with a baseline ≥ 0.2 ms, normalised by a calibration loop in the same CI job (qa-lead). Otherwise advisory.
- **Network send cost:** the send happens after `Tick()` (GDD R12). Its cost goes in OQ5's network bucket, which the 10 ms render figure must state.

## 2b. AC disposition (qa-lead plan, applied by the revision; the 131 originals are verbatim in 3.17)

- **Kept or merged into the GDD's contract ACs:** see the GDD. Every rule kept there has at least one AC.
- **Moved to ADR validation or the Technical Setup spike plan (no pass condition, or mechanism-only):** SC-R17-5, SC-R18-2, SC-R33-2, SC-ALLOC-2, SC-PERF-1, SC-PERF-2, SC-PERF-4, SC-R31-1, the csproj part of SC-R33-1, SC-R33-3.
- **Moved to story test plans (`/create-stories`, `/qa-plan`):** SC-R2-2, SC-R6-1, SC-R8-4, SC-R14-3, SC-R17-4, SC-R20-1, SC-R22-1, SC-R23-1, SC-R27-1, SC-R28-2, SC-R28-4, SC-R31-2, SC-F2-7, SC-F3-1, SC-F5-2, SC-F6-2, SC-F7-2, SC-F7-3, SC-EC-7, SC-EC-8 (replay story, M5), and the boundary rows of SC-F1-1/F1-2/F2-*.
- **Moved to owning GDDs (hand-backs in GDD Open Questions):**

  | ACs | Owning GDD |
  |---|---|
  | SC-R5-1, SC-R5-2 | Player Character |
  | SC-R11-2 | Grid Inventory |
  | SC-R16-4 | Networking |
  | SC-R24-2 | Throwing |
  | SC-F1-3 | Content & Data |
  | SC-EC-1, SC-EC-2 | Health & Damage |
  | SC-EC-3 | Run Structure |
  | SC-X-1 | Mail |
  | SC-X-2 | Health & Damage, Grid Inventory |
  | Input half of SC-R17-2 | Input & Controls |

- **Id reuse:** the revised GDD reuses the ids SC-X-1…X-5, SC-R14-1/2, SC-R16-1/2, SC-R20-1, SC-R21-2, SC-F2-1…3, SC-PERF-1 and SC-ST-3 for new or merged contract ACs. Ids in this section and in 3.17 always mean the originals.
- **Dropped:** SC-R1-3, SC-R18-1, SC-PERF-5 (doc-lint; the F3 sums 4.50 and 12.1 were verified correct), SC-F8-1.

## 3. Verbatim moved blocks (as reviewed)


### 3.1 R6–R10 Operations (op layout, ring, drain, replies)

*(verbatim, GDD lines 151–211 as reviewed)*

#### Operations (`Inputs.Apply`)

**R6 — What an op is.** An op is a 64-byte blittable struct. Its 16-byte header is:

| Field | Size |
|---|---|
| `opSeq` | u32 |
| `actor` | u32 |
| `kind` | u16 |
| padding | 2 B |
| `reqId` | u32 |

It is followed by a 48-byte explicit-layout `OpPayload` union.
- **Payload size.** Each `OpKind` registers its payload struct in a descriptor table, and a unit test asserts `sizeof ≤ 48` for every kind. For scale, Throwing's `ThrowRelease` is 25 B (registry `throwPayloadBytes`) and Grid Inventory ops are ≤ 32 B.
- **`ActorId`.** An `ActorId` is `EntityId`-width:
  - a player is their `player` `EntityId`;
  - a construct is its `construct` `EntityId`;
  - `System` is the reserved value `0x00000001` (class 0, counter 1).

  `actor` is set by the adapter from the transport peer, never from a payload field, so clients cannot impersonate.
- **`opSeq`.** It is stamped by `SimWorld.EnqueueOp`. It is monotonic per `SimWorld`, never reused, and never wraps in a run (F4). This is the single "arrival order" key that Grid Inventory R7, Mail R6 and Manual Delivery MD6 refer to.
  - It is deliberately not called `arrivalSeq`: that name is Grid Inventory's per-entry Intake counter.
- **Not an op.** Continuous per-tick intent (the input frame) never enters `SimWorld` (R2, **DEV-2**).

**R7 — The queue.** One preallocated ring with capacity `opQueueCapacity` (Tuning Knobs).
- **Full ring.** If the ring is full, `EnqueueOp` returns `false` and consumes no `opSeq`. The adapter answers the requester `busy` outside the event log, a counter increments, and nothing throws. The op is not recorded, because it had no Sim effect.
- **System reserve.** Player ops are refused at `opQueueCapacity − systemOpReserve` (64). `System` ops (`PlayerJoin`, `PlayerLeave`, `FreezePlayer`) may use the whole ring, so a dropped `PlayerLeave` can never leave a ghost player.
- Enqueue is thread-affine to the Sim thread (Debug assert). A transport whose callbacks arrive off-thread must marshal (R18).
- The host's own client uses the same `EnqueueOp` through the loopback adapter.

**R8 — The drain.** At the start of phase 0 the queue captures `n = count`. It applies the first `n` ops in ascending `opSeq` through the bound `IOpHandler[]` (R4). Each handler has one method: `Apply(in Op, EventLog) → Reply`.
- **Timing.** Ops enqueued during the drain, or between ticks, apply on the next tick.
- **Unknown actor.** An op from an unknown or departed actor is rejected with `unknown_actor`.
- **Atomicity.** One op is the atomic unit.
  - A handler validates fully before its first mutation, id allocation or RNG draw, and raises events only on commit.
  - A reject changes no state, no allocator and no RNG, and raises no event, apart from its reply.
- **Composite actions.** A composite action is **one** `OpKind` whose handler calls documented APIs in a fixed order. Examples are a throw, and Player Character P13 followed by `deliver_matching`. A client must never rely on two ops applying together.
- **Recording.** Every *drained* op, accepted or rejected, is offered to the optional `IOpRecorder.OnDrained(applyTick, in Op, result)`. Rejected ops consume `opSeq`, which is in the dump, so they must be recorded too.
  - Transform samples are recorded through `OnSample(receivedTick, slot, sample)`.
  - This is the replay and determinism-test hook. The M-1a recorder is in-memory and feeds H-DET-1 / SC-DET-1. A file writer is deferred to M5 (spec 06 §9).

**R9 — Only requests use the FIFO; every in-tick caller acts in its own phase (DEV-6).** The op FIFO carries player and `System` requests only. Every in-tick system caller calls documented synchronous `I…Api` methods inside its own phase. The in-tick callers are:
- `Items.Expire` and `Intake.Drain`;
- Throwing's landings and catches (`TryCatch`, WorldItem creation, `Consume`/wallet through Mail);
- automation: inserters, feeders, depots;
- Vehicles' NPC drops;
- `Mail.Step`;
- `Combat.React`.

The rules for these calls:
- Each call is one atomic mutation. It bumps container versions and raises events exactly like an op handler does, but it produces no reply.
- Every `I…Api` mutation may be called from any phase except `Events.Flush` and tick end.
- The deterministic total order of a tick is therefore: phase order, then the owner's order inside the phase.
- On a contested last free cell, a player op (phase 0) always beats an inserter (phase 5). A losing inserter retries next tick. Pillar 3 favours this: *hands beat machines*.
- This amends Grid Inventory R7 and the edge case at gi:360, which said `Inputs.Apply` drains an inserter's `TryInsert` "in arrival order". That is hand-back HB1.
- Putting automation in the FIFO would add a tick of latency to every inserter, and it would break Mail R6.

**R10 — Replies.** A handler returns `Reply {reqId u32, result u8, reason u16, extra 24 B}`.
- `reason` comes from a per-system code registry, where 0 means ok. `extra` carries, for example, Grid Inventory's `resultVersion` for up to two containers and its `byActorId`.
- Sim Core appends the reply to the tick's event log as kind `OpReply` **after** every event that handler raised. "Event first, then reply" (Grid Inventory, Throwing's `Removed → ThrowLaunched → ok`) therefore holds by construction, not by adapter discipline.
- Replies have replication class `OwnerOnly` (R12).

### 3.2 R11–R14 Event bus (entry layout, cursors, coalescing, overflow)

*(verbatim, GDD lines 213–255 as reviewed)*

#### Event bus

**R11 — One ordered log per tick.** Every `SimEvent` and `Reply` raised during tick T goes into one preallocated append-only log. Each entry has:
- `ordinal`: uint32, counted from 0 per tick;
- `kind`;
- `replicationClass`;
- `audienceMask` (u8, one bit per `PlayerSlot`), resolved by the raising system **at raise time**, so a player who opens a container later never receives earlier events;
- a blittable payload with no strings (strings are `StringId`s from the content table).

After flush the log is cleared, so events do not survive a tick. A system that needs something on T+1 stores state, for example a pending-damage list.

**R12 — Visibility and flush.**
- **No synchronous subscribers.** This rules out re-entrancy and hidden ordering.
  - A later phase in the same tick may read entries raised earlier through a pull cursor, `Events.Read(ref cursor)`. It can never see entries from a later phase.
  - Each reader and its kind filter are declared at bind time, with at most `maxEventReaders` (8) readers. A 9th reader bind is a build-time error (R4).
  - Events a reader raises append after everything already in the log. For a knockdown the log order is `PlayerDied`, container close, `Removed`/spill, `CarrierKnockedDown`.
- **Flush.** In phase 13, `IEventSink.Deliver(tick, ReadOnlySpan<entry>)` is called **once**, in raise order. This is the only output point of the Sim.
- **Replication classes.** Every event kind declares one in a static descriptor table. A unit test enumerates all kinds, so an undeclared kind fails.

  | Class | Delivered to |
  |---|---|
  | `Internal` | never leaves the Sim (for example `MailConsumed`) |
  | `Replicated` | all peers |
  | `OwnerOnly` | the single player in `audienceMask` |
  | `Targeted` | the players in `audienceMask`, resolved at raise time by the owning system's subscription query (for example Grid Inventory's container viewers) |

  The **fail-closed default is `Internal`**.
- The adapter, not the Sim, does per-peer filtering and packing.

**R13 — Coalescing.** Sim Core never merges events on its own.
- An owner may declare a kind `Coalescible` and rewrite its own entry in place through `ref SimEvent Events.Update(index)`, **only within the phase that raised it**. A later phase raises a new entry. Otherwise a `replace` rewritten to its final state in phase 5 would make clients apply a phase-5 insert twice.
- The entry keeps the position of its first raise, so a later reply still follows it.
- Grid Inventory uses this for "one `replace` per container per tick".

**R14 — Misuse and overflow.**
- **Raising outside `Tick()`.** Debug asserts. Release drops the event, increments `eventsOutsideTick` and logs.
- **Log full** (`eventLogCapacity`), with two-tier admission:
  - Kinds that have a declared pull-cursor reader (for example `PlayerDied`), and `OpReply`, may fill the whole log.
  - All other kinds stop at `eventLogCapacity − eventLogReserve` (256).
  - A dropped entry is counted and the flushed batch carries `overflow = true`. State is **not** rolled back, because the handler already committed.
  - Networking & Session must treat an overflowed batch as a resync trigger for every audience in it.
  - No event is ever reordered.
  - A replay whose recording saw an overflow is marked non-reproducible.

### 3.3 R15–R16 Timers and pause

*(verbatim, GDD lines 257–271 as reviewed)*

#### Time, pause, driver and thread

**R15 — Timers are absolute ticks.** A timer stores `deadlineTick`. It is due when `CurrentTick ≥ deadlineTick`.
- Seconds exist only in content and display. They are converted at load by F1, and back for display by F1.
- The run clock shown to players is `phaseDeadlineTick − currentTick` (spec 07 §4.1).
- Prep, Payday and Draft keep ticking. Only a solo pause stops time.

**R16 — Pause.**
- **Pause is a `TickDriver` state, not a `SimWorld` state.**
- **Solo pause** (exactly one connected player, no remote peers): `TickDriver.Pause()` makes `Advance()` return 0 and stops accumulating. `SimWorld` simply is not ticked. On `Resume()` the accumulator is cleared, so there is no burst and no banked time.
  - Every absolute-tick timer freezes for free, which is what Grid Inventory D6 and Input H-R2-2 need.
  - Ops enqueued while paused apply on the first tick after resume.
- **Multiplayer never pauses.** Networking & Session owns the "no remote peers" check and refuses `Pause()` while any remote peer is connected. The pause menu is a local overlay and the input frame is zeroed (Input & Controls).
- **Connecting while paused.** If a connection attempt arrives during a solo pause (Lobby/Prep only), Networking & Session calls `Resume()` **before** the `System` `PlayerJoin` op is enqueued.
- **`SceneTree.Paused` is never used** to pause the Sim.

### 3.4 R17–R18 Tick driver (Godot side, rejected options) and thread

*(verbatim, GDD lines 273–295 as reviewed)*

**R17 — Tick driver (resolves Grid Inventory OQ20, recommended default; ADR + spike required, OQ1).**
- **The driver class.** `TickDriver` is a Godot-free class in the Sim assembly (`Hosting/`). It exposes `Advance(long timestamp) → ticksRun`, `Alpha`, `Pause()`, `Resume()`, `Resync()` and counters.
  - It takes the timestamp as an argument and never reads a clock itself. Tests pass integers, and the clock stays in the Godot adapter (R28.7).
  - `SimWorld.Tick()` knows nothing about the driver.
- **The Godot side.** One root `GameLoop` node calls `driver.Advance()` from `_Process`, then calls Presentation explicitly with `Alpha`. The Sim therefore does not depend on node process-priority ordering. `ProcessMode = Always` is set defensively on that node only. Both names are **UNVERIFIED**.
- **The time source.** It is a monotonic integer timestamp supplied by the adapter: .NET `Stopwatch.GetTimestamp()` is recommended. The `_Process` `delta` argument is not used; whether the engine scales or clamps it is UNVERIFIED. The accumulator is integer (F2), so there is no float drift.
- **Catch-up.** At most `maxCatchUpTicksPerFrame` ticks (4) run per frame.
  - **Wall-clock guard (M0).** `catchUpGuardMs` (10 ms) stops catching up early. Because the driver never reads a clock, `GameLoop` enforces the guard: it re-reads its timestamp after each tick, and once `catchUpGuardMs` has elapsed since the frame's first tick began, it stops and calls `driver.DropRemaining(timestamp)`. That call counts the unrun ticks as dropped and applies F2's drop rule. At least one tick always runs. Excess time is **dropped, never banked**: one log line per hitch and a `droppedTicksTotal` counter. Game time then slips against wall time. This is acceptable because a slip only ever gives the crew more real time before a deadline, never less, and every player sees the same slip.
- **`Resync()`.** It is called after known blocking work (world generation, scene load, focus regain) to clear the accumulator without logging a hitch.
- **Input edges.** Latched edges attach to the **first** tick run in a frame. Later catch-up ticks carry no edges, the current held bits and the latest `moveIntent` (Input & Controls R11, H-EC-4). A dropped tick never discards an edge.
- **`Alpha`.** `accumulator / tickDuration` lies in [0, 1). It is Presentation-only and the Sim never reads it. It is frozen at 0 while paused.
- **Rejected.** `_PhysicsProcess` with physics at 30 Hz: it drags all presentation physics (Jolt) to 30 Hz, couples to `SceneTree.Paused` and time scale, and its edge ordering is unverified. A `System.Threading` timer: it forces a thread model now.

**R18 — Thread (recommended default; ADR, OQ2).**
- **v1 runs the Sim on the Godot main thread.** `SimWorld` has a single owner thread. Debug asserts a captured thread id in `Tick()` and `EnqueueOp()`.
- **Transport boundary.** Transport callbacks only *enqueue*. Nothing is applied inside a callback. Whether Godot multiplayer callbacks arrive on the main thread inside the frame is **UNVERIFIED** (Grid Inventory OQ12). If they do not, the queue becomes a lock-free MPSC ring and nothing else changes.
- **Triggers for moving to a dedicated Sim thread** (any one of them):
  1. In the shift-5, 8-player fixture, host tick p99 plus render p99 exceeds 16.6 ms after tuning.
  2. A dedicated server ships.
  3. Main-thread GC or Godot hitches push tick latency past 2 ticks.

  The Sim API is held to "single owner thread, no statics, no Godot types", which keeps the move mechanical.
- **Headless.** xUnit, BalanceSim and BotClient have **no driver**. They call `SimWorld.Tick()` in a plain loop.

### 3.5 R19–R22 Identity (class table, allocation, players, serial helpers)

*(verbatim, GDD lines 297–341 as reviewed)*

#### Identity

**R19 — `EntityId`.** An `EntityId` is a `uint32`. The high byte is `EntityClass`, the low 24 bits a per-class counter starting at 1. `0x00000000` is `None`.

| Byte | Class | Allocated by | Notes |
|---|---|---|---|
| 0x01 | `player` | Sim Core on `AddPlayer` | See R21 |
| 0x02 | `world_item` | Grid Inventory via Sim Core | Grid Inventory R12 |
| 0x03 | `construct` | Building | Walls, belts, sorters, depots, turrets |
| 0x04 | `vehicle` | Vehicles | |
| 0x05 | `enemy` | Enemy AI & Waves | |
| 0x06 | `npc` | Vehicles & NPC Drivers | NPC drivers. **DEV-4** |
| 0x07 | `destination` | World Generation, in lot order at world build | Mailboxes. The u32 form is what Manual Delivery and HUD use. **DEV-4** |
| 0x08 | `prop` | World Generation | Resource nodes, interactables with no other class. **DEV-4** |
| 0x09–0xEF | reserved | — | A new class is an amendment to this table |
| 0xF0–0xFF | test/debug | test fixtures only | Never in a shipped run |

- **Mail is not an entity class.** `mailId` is a separate `uint32` counter owned by Sim Core (spec 07 §4.2). Mail allocates from it in manifest order at the batch tick, and lower means older (Mail & Destinations).

**R20 — Allocation.**
- `EntityIds.Allocate(class)` and `MailIds.Allocate()` run only on the Sim thread inside `Tick()`. World build at `Constructed` is allowed for `destination` and `prop`.
- Ids are **never reused within a run**.
- **Initial counters.** `EntityIds`, `MailIds` and `SimWorld`'s tick can be constructed from initial counter values. This is the same path a Free Play save/load will use (spec 07 §7.3), and tests use it to reach exhaustion without a test-only seam.
- **Exhaustion.** At counter 2^24 − 1, `Allocate` returns `None`, writes one always-on log record (R31) and asserts in Debug. The caller refuses the spawn. Ids never wrap or skip. F4 shows exhaustion is unreachable in a run.
- **Tie-breaks** use ascending raw `uint32`. Within one class that is creation order. Player Character's "lowest entityId" rules rely on this.

**R21 — Players.** `AddPlayer(PlayerSlot)` is invoked **only** by the `System` `PlayerJoin` op handler in phase 0. It takes a slot, never an account. It allocates a `player` EntityId from the counter like any other class, unless the op carries a rejoin `playerId` (see below).
- Networking & Session keeps the run-scoped account → `playerId` map. A reconnect within the 120 s window (spec 06 §6) gets the **same** `playerId`. A new account gets a new one, and ids are never reused.
- Separately, each connected player has a **`PlayerSlot`** (byte 0–7), assigned by Networking & Session. It is the compact wire form used by Throwing's 1-byte `throwerId`, the transform packet's player index, Grid Inventory temp-id namespaces and HUD Teammate pings.
- The Sim keeps a `slot ↔ playerId` table. It never sees accounts or transport peers.

**R22 — System-local ids** are not `EntityId`s and are owned by their systems:

| Id | Owner |
|---|---|
| `entryId` (u32) | Grid Inventory. Bit 31 is reserved for client temp ids. |
| `containerId`, `version`, Intake `arrivalSeq` | their owners |
| `flightId` (u16, wrap-safe) | Throwing |
| `reqId` (u32) | each client |
| `outcomeSeq` | Mail |

- Sim Core provides the shared wrap-safe serial-number helpers `SerialU8`, `SerialU16` and `SerialU32`, plus checked narrowing helpers `WireNarrow.*` (assert in Debug, clamp and log in Release).
  - The serial helpers follow RFC 1982 for n bits. `Diff(a, b)` is `(a − b) mod 2^n` read as signed, and `IsNewer(a, b)` is true when that `Diff` is in 1 … 2^(n−1) − 1.
  - The exact half-range difference 2^(n−1) is neither newer nor older, and asserts in Debug.
- A narrow wire id such as Throwing's 2-byte `receiverId` must come from an id map. It must never be the low bits of an `EntityId`, because those are not unique.

### 3.6 R27–R28 Numeric types and determinism checklist (incl. analyzer paragraph)

*(verbatim, GDD lines 379–410 as reviewed)*

#### Numbers and determinism

**R27 — Numeric types in authoritative state.**

| Quantity | Type | Notes |
|---|---|---|
| Money | `long` cents in `readonly struct Cents` | `Cents.MulPermille` is the only rounding entry point (F7) |
| Ratios | `int` permille | Throw spread is in mrad |
| Positions and distances | `int` millimetres | Stored and on the wire |
| Sub-millimetre integration | `Fx16` (a `long` with 16 fractional bits), floored to mm | Each system decides which of its quantities use it |
| Angles | `ushort` BAM (65536 = 360°) | Committed Q15 sin/cos table of 4096 entries, guarded by a hash test |
| Ticks | `uint32` | |

`float`, `double` and `decimal` are **banned in authoritative state and in anything that orders events**. They are allowed in Presentation and tools.

**R28 — Determinism rules (code-review checklist).** These apply to all code in the Sim assembly that affects state, event order or the dump.
1. No float, double or decimal.
2. Integer maths widens to `long` before multiplying. Ratios go through `Fix.MulDiv(a, b, c)` with the documented rounding (F7).
3. No iteration over `Dictionary` or `HashSet`. They are lookup-only. Ordered iteration uses arrays or lists in ascending id or `opSeq` order.
4. Removal from an ordered list is a stable compaction. Swap-remove is banned wherever order matters.
5. No `Array.Sort`, `List.Sort` or LINQ `OrderBy`, because they are unstable or version-dependent. Use `Sim.StableSort` with preallocated scratch space. Every comparer ends with an `EntityId` or `opSeq` tie-break.
6. No `System.Random`, `Guid`, `string.GetHashCode` or `object.GetHashCode` in state.
7. No time sources (`DateTime`, `Stopwatch`, `Environment.TickCount`) and no `Thread`, `Task` or `Parallel` inside the Sim. Ticks are the only clock.
   - Timing is measured through an optional `IPhaseProbe` with `Enter(ProbeScope)` / `Exit(ProbeScope)`. The probe is injected into `SimWorld` and implemented outside the Sim (the adapter uses `Stopwatch`).
   - `ProbeScope` is a compile-time enum. Every phase is a scope, and systems may open sub-scopes, for example `InventoryOps` inside phase 0 and `InventoryEndpointCalls` inside phase 5.
   - The default probe is a null object that allocates nothing. Probe output is never read by Sim logic.
   - This is how Grid Inventory's "Stopwatch around the inventory phase" is met: that span covers several phases, so it is a set of sub-scopes, not a single stopwatch.
8. No static mutable state; only `static readonly` tables. Two `SimWorld`s must coexist in one process (H-DET-1, and parallel xUnit).
9. No culture-dependent text in state or the dump. Use explicit little-endian writes through `BinaryPrimitives`.
10. Content def ids come from a sorted-key table at load, never from file or dictionary order.

**What the analyzer can and cannot catch.** The banned-API analyzer (R33) enforces rules 1, 3 (`foreach`, `Keys`, `Values` on `Dictionary`/`HashSet`), 5, 6 and 7. Rules 2, 4, 8, 9 and 10 are enforced by code review, by the SC-DET-1 two-instance fixture and by the static-state reflection test.

### 3.7 R29–R30 Determinism tiers and canonical dump

*(verbatim, GDD lines 412–427 as reviewed)*

**R29 — Determinism scope (v1).**

| Tier | Guarantee | Proof |
|---|---|---|
| **T1, same machine** | Two `SimWorld`s with the same `ContentSet`, `runSeed` and applied-op log give identical canonical dumps after every tick. | H-DET-1 (Grid Inventory), SC-DET-1 |
| **T2, cross-platform** | `OwnedCarrier.Step`, the RNG and hash primitives, and the shared pure functions are bit-identical on Windows and Linux x64. So is the canonical encoding used by checksums that host and client both compute, such as Grid Inventory's container checksum. | PCM-P2-1; SC-DET-2 golden vectors |
| **Not required** | Clients never lockstep or re-simulate the world. World generation is not regenerated on clients (spec 13 §3: the server sends tables). HUD needs none. | — |

**R30 — Canonical state dump.** Every system with state implements `ISimStateContributor.Write(CanonicalWriter)`. The writer is little-endian and writes sections in fixed registration order, with collections in ascending id.
- **Included:** the tick, all id counters, the mail counter, `opSeq`, every live RNG stream (R26), and the op queue contents.
- **Excluded:** perf counters, the log ring, caches and anything derivable.
- **When it is valid.** The dump is valid **between ticks** only.
  - Determinism harnesses that need a hash "after each op" (Grid Inventory H-DET-1) apply one op per tick. There is no test-only seam in production code.
- **Hashing.** The Sim has no cryptography reference. Tests and tools hash the dump with SHA-256.
- **`CanonicalHash64`.** A non-cryptographic hash over the same encoding: F5's `Mix64` folded over 8-byte little-endian words. It is defined **when a cross-machine checksum first needs it**, for example Grid Inventory's container checksum. Until then it is not built (ADR OQ3).
- **Separate artefact.** JoinState blocks (Network section) are a different artefact from the dump.

### 3.8 R31 Assertions and always-on checks

*(verbatim, GDD lines 429–439 as reviewed)*

#### Safety and cost

**R31 — Assertions and always-on checks.**
- **Symbols.** `[Conditional]` binds at the *caller's* assembly, so every assertion call lives inside the Sim assembly with symbols defined in the Sim `csproj`.
  - `SIM_ASSERT` is the umbrella, and it turns on every per-system alias in `Directory.Build.props`.
  - A per-system alias (`<SYSTEM>_ASSERT`) exists only once a system needs to be toggled alone. The first is `INVENTORY_ASSERT`, which Grid Inventory R19 cites. Others are added the same way.
- **Configurations.** Debug has asserts and `checked` arithmetic on. Release has them off and is the only configuration perf budgets are measured in. `ReleaseAssert` is used for nightly soaks (H-R19-1, TC-X-1).
- **Release guard.** `Sim.AssertsEnabled` is a `const` logged at start. A test fails if a Release build has it set to true.
- **Pure arguments.** Assertion arguments must be pure, because they are not evaluated when compiled out.
- **Always-on checks (every build).** O(1) counters updated in the lowest mutation primitive, and one cheap check per tick. A failure writes `{code, tick, a, b}` to a fixed log ring, rate-limited per code. It never throws, formats strings or allocates.
- Full-scan invariants are assert-only. Grid Inventory R19 is the model.

### 3.9 R32–R33 Allocation-free tick measurement and assembly shape

*(verbatim, GDD lines 441–467 as reviewed)*

**R32 — Allocation-free tick.** After warm-up (the first `warmupTicks` = 300 ticks), `SimWorld.Tick()` allocates **0 bytes** on its thread.
- Events and ops are blittable structs in preallocated rings.
- Collections are pre-sized from `SimSettings`. Growth is allowed only during warm-up, and a test asserts no growth afterwards.
- **Banned in the tick path:** LINQ, lambdas and closures, method-group delegates, `foreach` over interface-typed collections, `params`, string concatenation or interpolation (including logging), boxing, and `Enum.ToString`.
- Measured with `GC.GetAllocatedBytesForCurrentThread()` around N ticks in Release.
  - The test runs in a non-parallel xUnit collection, warms up by tick count, and pins JIT settings to match the perf build: tiered compilation or PGO fixed for the run. This avoids flakiness from tier-up allocations.
  - Whether this measurement works in an exported Godot build is UNVERIFIED (spike).

**R33 — Assembly shape (recommended default; ADR + spike, OQ4).**
- `src/Sim/PerformativeMail.Sim.csproj` is a plain .NET class library targeting **`net8.0`**, with `Nullable` and `Deterministic` on (**DEV-7**: spec 07 says `netstandard2.1`).
- It has **zero Godot references** and no runtime package dependencies. Analyzers referenced with `PrivateAssets=all` are allowed, for example BannedApiAnalyzers enforcing R28. Content loaders take `Stream` or `ReadOnlySpan<byte>`; they have no `res://` access.
- **Folders** (namespace = `PerformativeMail.Sim.<Folder>`). This replaces spec 07 §2 for the Sim; `Net/` is dropped per spec 13 §3.

  | Folder | Contents |
  |---|---|
  | `Core/` | `SimWorld`, builder, `PhaseId`, op ring, `EventLog`, `EntityIds`, `MailIds`, `RngStream`, dump |
  | `Primitives/` | `Cents`, `Fx16`, BAM tables, `Serial*`, `WireNarrow`, `StableSort`, `CanonicalWriter`. No dependency on `Core/`. |
  | `Hosting/` | `TickDriver` |
  | `ClientLayer/` | The shell only |
  | One folder per system | Spec 07's `Content/ World/ Mail/ Inventory/ Building/ Automation/ Vehicles/ Combat/ Run/ Meta/`, plus `Players/` and `Throwing/` |

- **Godot side.** Adapters live in the Godot project under `src/Game/Net/`, outside the Sim: transport, the `IEventSink` implementation, the op encoder, loopback, the `IHostClock` implementation, the `IPhaseProbe` implementation, and `GameLoop`.
- The Godot game project excludes `src/Sim/**`, `tests/**`, `tools/**` and `**/obj/**` from its compile glob, and references the Sim by ProjectReference.
  - Whether the Godot .NET SDK in 4.7.1 accepts a ProjectReference to a `net8.0` library, and how its default globs behave, is **UNVERIFIED** (spike pass/fail).
  - Fallback: compile the Sim sources into the game assembly through a shared folder. The no-Godot rule is then enforced on the namespace.
- **Enforcement (CI).** An architecture test asserts that the Sim assembly references no assembly named `Godot*`, and a grep fails on `using Godot` under `src/Sim`.
- **Tests.** One xUnit project, `tests/unit/PerformativeMail.Sim.Tests/`, with a folder per system. A project per system would multiply build time for a solo developer. Grid Inventory's `tests/unit/inventory/` becomes the `Inventory/` folder (HB1).

### 3.10 Milestone slice table

*(verbatim, GDD lines 533–553 as reviewed)*

**Milestone slice.** The systems index calls this system "throwaway in M-1 → real in M0". The lead-programmer's recommendation was adopted: M-1a writes the **real** files with a subset of features, because seven GDDs already depend on these contracts and a rewrite risks drift. M-1a is single-player and local (spec 13 §6), so ops travel only through the in-process loopback adapter. The remote path of `ClientLayer` waits for M0.

| Item | M-1a (greybox) | M-1b | M0 (multiplayer) |
|---|---|---|---|
| `PhaseId` order array | full (14 phases + tick end) | = | = |
| Bound phases | 0–4, 8, 12 (Run.Step stub: shift timer) | + 5 (one belt, sorter, inserter), 10–11 (scripted raid pulse) | all built systems |
| Lifecycle | Constructed / Ready / Running / Stopped | = | = |
| Op ring, drain, `IOpHandler`, replies after events, atomicity rule | real | = | = |
| `IOpRecorder` | in-memory (SC-DET-1, H-DET-1) | = | + sample recording; file writer at M5 |
| `EventLog`: ordinals, replication descriptors and their enumeration test, coalescing, overflow flag | real | + pull cursor (`Combat.React`) | = |
| `EntityIds` / `MailIds` | full class table; `player`, `world_item`, `destination` used | + `construct`, `enemy` | all |
| RNG | PCG32, `Derive`, `NextInt`, `mail`, `mail.special`, `IRngSource`, golden vectors | + `waves` | all v1 streams, draw audit in overlay |
| Numerics | `Cents`, `Fx16`, BAM and Q15 tables (Player Character P2) | `Fix.MulDiv` as consumers appear | = |
| `CanonicalWriter`, `ISimStateContributor`, SHA-256 in tests | real | = | `CanonicalHash64` only if a checksum needs it |
| `TickDriver` | accumulator, cap 4, drop, `Alpha`, `Resync`, counters, solo pause | = | + `catchUpGuardMs` |
| Thread | main thread, Debug thread assert | = | = (move triggers R18) |
| `ClientLayer` | shell with `InputLatch` and `CarrierStep` over loopback | + `Previews` | + `MirrorDrain` for remote batches |
| Asserts | `SIM_ASSERT` + `INVENTORY_ASSERT` | = | + `ReleaseAssert` nightly, always-on log ring |
| 0-alloc test | measured and logged (advisory) | = | **blocking** |
| CI guards | `using Godot` grep, arch test, Linux runner | = | + reflection dependency test, Windows runner (T2) |
| `IHostClock`, `ISnapshotSource`, `IPhaseProbe` | host-only trivial `IHostClock`; others absent | `IPhaseProbe` | real |

### 3.11 F2 Catch-up accumulator and F3 Tick budget split (superseded in part by section 2)

*(verbatim, GDD lines 591–708 as reviewed)*

### F2 — Catch-up accumulator

The accumulator formula is defined as:

`acc30 += min(max(now − last, 0), freq) × 30;  clampedTimeTotal += max(now − last − freq, 0);  last = max(last, now)`
`due = acc30 / freq`  (integer floor)
`ticksRun = min(due, maxCatchUpTicksPerFrame, ticksBeforeGuard)`
`acc30 −= ticksRun × freq`
`if (due > ticksRun) { droppedTicks = due − ticksRun; droppedTicksTotal += droppedTicks; acc30 = acc30 mod freq; log once }`
`alphaPermille = acc30 × 1000 / freq`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| freq | int64 | e.g. 10^7 or 10^9 | calculated | Timestamp counts per second, given to the driver once by the adapter (`Stopwatch.Frequency` recommended) |
| now, last | int64 | monotonic | calculated | `Advance(timestamp)` argument and the previous one |
| acc30 | int64 | below freq after every `Advance`; at most 31 × freq inside it | calculated | Accumulator scaled by 30, so one tick is exactly `freq` units and there is no remainder drift |
| maxCatchUpTicksPerFrame | int | 4 (safe 3–8) | data file | Tuning Knob (R17). Input H-EC-4 needs at least 3. |
| ticksBeforeGuard | int | 1 to due (at least 1 tick always runs when due ≥ 1; the guard is checked after each tick) | calculated | Ticks that ran before `catchUpGuardMs` (10 ms) of wall time elapsed inside this `Advance` (M0) |
| droppedTicks | int | 0 to due | calculated | Whole ticks discarded this frame |
| alphaPermille | int | 0–999 | calculated | Presentation only. `Alpha = acc30 / freq`, in [0, 1). |

**Output Range:** `ticksRun` is 0 to 4.
- **Bounds.** Clamping each frame's Δ to 1 s keeps `acc30 ≤ 31 × freq` (about 3.1 × 10^10), so there is no overflow.
- **No time is created.** Over the long run, `ticksRunTotal + droppedTicksTotal ≤ floor(Σ elapsed × 30)`: Sim time never runs ahead of wall time, and a hitch can only lose time.
- **When drops start.** At or above 7.5 fps nothing is dropped, because 4 ticks per frame covers 30 ticks per second.
- **Pause and resync.** `Pause`, `Resume`, `Resync` and `Start` set `last = now` and `acc30 = 0`, and log nothing.
- **Clock going backwards.** A timestamp earlier than `last` adds nothing and does not move `last` back. Otherwise the next forward step would re-count the backward span and create time.
- **Long stalls.** Time beyond the 1 s clamp is counted in `clampedTimeTotal` for the overlay.

**Example** (freq = 10^7, so a tick is due when `acc30 ≥ 10^7`):
- **150 ms hitch from acc30 = 0.** Δ = 1 500 000 gives acc30 = 45 000 000, so due = 4. ticksRun = 4, acc30 = 5 000 000, **dropped 0**, Alpha = 0.5. The half tick is carried, not lost.
- **Same hitch again, with the 5 000 000 carried.** acc30 = 50 000 000, so due = 5. ticksRun = 4, **dropped 1**, acc30 = 0, Alpha = 0.
- **400 ms hitch from 0.** acc30 = 120 000 000, so due = 12. ticksRun = 4, **dropped 8**, and one log line is written.
- **Steady 60 fps** (Δ = 166 667). Ticks alternate 0 and 1 per frame, with Alpha alternating about 0.5 and about 0.0.

### F3 — Tick budget split

The tick budget formula is defined as:

`p99(totalTick, fixture) ≤ tickBudgetP99(mode)`  and  `max(totalTick, fixture) ≤ tickBudgetMax(mode)`  (the gates)
`Σ_{phase} p99Budget(phase) + reserve = tickBudgetP99(host)`  (the allocation)
`p99(x) = sorted(x)[ceil(0.99 × N) − 1]`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| totalTick | int µs | ≥ 0 | calculated | `SimWorld.Tick()` wall time, phases 0–13 plus tick end, from `IPhaseProbe` |
| p99(phase) | int µs | ≥ 0 | calculated | 99th percentile of one phase's `ProbeScope` |
| tickBudgetP99(host) | int µs | 5 000 | constant (PROPOSED) | Listen-server host. A tick frame must also fit ClientLayer (0.3 ms) and render (≤ 10 ms) within 16.6 ms at 60 fps (engine-programmer, R18 trigger 1). |
| tickBudgetMax(host) | int µs | 8 000 | constant | Spec 07 §8 ceiling, applied to the whole tick in every mode |
| tickBudgetMedian(host) | int µs | 3 000 | constant (PROPOSED) | SC-FIX-WORST median |
| tickBudgetP99(dedicated) | int µs | 8 000 | constant | Spec 07 §8. No dedicated table until a dedicated server ships (spec 13 §2.2). |
| reserve | int µs | 500 | constant (PROPOSED) | Unallocated. Released only by the technical-director. |
| N | int | ≥ 18 000 | data file | Ticks measured after warm-up |

**The split (host mode, SC-FIX-WORST, Release, PROPOSED):**

| # | Phase / scope | p99 budget (ms) | max (ms, advisory) | Owner, and notes |
|---|---|---|---|---|
| — | Sim Core overhead (drain loop, probe, tick end O(1) checks) | 0.10 | 0.3 | Sim Core |
| 0 | `Inputs.Apply` | 0.80 | 2.0 | All op handlers. The Grid Inventory sub-scope is 0.50. |
| 1 | `Items.Expire` | 0.20 | 0.5 | Grid Inventory. 100 expiries in one tick. |
| 2 | `Intake.Drain` | 0.30 | 0.8 | Grid Inventory. ≤ 32 placements. |
| 3 | `Players.Step` (host role) | 0.20 | 0.4 | Player Character |
| 4 | `Throwing.Step` | 0.15 | 0.5 | Throwing |
| 5 | `Automation.Step` | 1.20 | 2.5 | Belt Logistics. **Includes** the belt endpoint calls into Grid Inventory, ≤ 0.25 ms per 400 calls, and feeder offers. |
| 6 | `Vehicles.Step` | 0.15 | 0.4 | Vehicles. Includes NPC drop offers. |
| 7 | `Agents.Step` | 0.50 | 1.5 | Enemy AI. 40 enemies plus megas. |
| 8 | `Mail.Step` | 0.25 | 1.0 | Mail. The max covers the 0.5 ms manifest roll. |
| 9 | `Economy.Flush` | 0.10 | 0.3 | Economy |
| 10 | `Combat.Resolve` | 0.20 | 0.6 | Health & Damage |
| 11 | `Combat.React` | 0.05 | 0.2 | Player Character |
| 12 | `Run.Step` | 0.05 | 0.5 | Run Structure. The max covers the raid trigger. |
| 13 | `Events.Flush` | 0.25 | 0.6 | Sim Core. Includes the synchronous sink call (adapter serialisation). |
| | **Allocated** | **4.50** | | The per-phase maxes sum to 12.1 by design. The 8 ms max gates the whole tick, not that sum. |
| | **Reserve** | **0.50** | | Total 5.00 |
| | **ClientLayer** (per client tick, not inside `SimWorld.Tick`) | 0.30 | 0.6 | Mirror 0.10, input latch 0.02, `OwnedCarrier.Step` 0.10, previews 0.08 |

**Fixtures** (seeded, 18 000 ticks after warm-up):

- **SC-FIX-WORST**
  - Players and belts: 8 players (7 remote transform latches), 400 belt tiles, 5 000 belt items.
  - Enemies, constructs and vehicles: 40 enemies + 2 megas, 150 constructs including 50 feeders, 4 trucks.
  - Mail and items: 512 WorldItems, a 1 200-unit backlog, a 64-unit batch.
  - Throwing: 16 flights and 8 armed catchers.
  - Run state: shift-5 raid.
  - Ops: 8 × 4 ops per tick, plus one rotating Intake sort.
  - Schedule: every 30th tick is a coincident worst tick (batch, 50-feeder spike, 100 expiries, sort).
  - Counts are PROPOSED. Enemy AI and Belt Logistics confirm theirs.
- **SC-FIX-STEADY**
  - 8 players, 400 tiles, 2 500 items, 40 enemies, no megas, no backlog or expiry burst, no raid.
  - Gate: median ≤ 1.5 ms, p99 ≤ 3.0 ms.

**Output Range:** pass or fail per fixture and mode.
- **The gate is the total-tick p99.** Per-phase p99s exist for attribution: quantiles do not add, and Σ p99 is a conservative bound when load is correlated.
- **Over budget.** A phase over its slice but inside the reserve is **flagged, not passed**. Changing the split is a technical-director decision.
- **Rare spikes.** A spike source counts against p99 only if it hits ≥ 2 % of ticks. Rarer spikes are judged on max.

**Gate status:**

| Gate | Status |
|---|---|
| 0 B allocated per tick after warm-up (`GC.GetAllocatedBytesForCurrentThread()` delta, R32), no collection growth, 0 gen-0 GCs in the headless harness (`GC.CollectionCount(0)` delta), Sim managed heap ≤ 64 MB on SC-FIX-WORST (`GC.GetTotalMemory(true)` after the run, outside the timed span) | **BLOCKING** from M0 (measured and advisory in M-1) |
| Any phase or total p99 > 2 × the recorded CI baseline (per runner class) | **BLOCKING** from M0. Baselines live in `production/qa/perf-baseline.md`. |
| Absolute ms gates (5 / 8 / 3 ms) | **ADVISORY** until a reference machine exists (Open Questions OQ6), then blocking by technical-director decision |

**Example:**
- N = 18 000 gives rank ceil(0.99 × 18 000) = 17 820, so p99 = sorted[17 819].
- Suppose SC-FIX-WORST measures a total-tick p99 of 4.1 ms, with `Automation.Step` at 1.35 ms (over its 1.20 slice by 0.15, inside the 0.50 reserve). The run **passes** the tick gate and **flags** Automation to the technical-director.

**Hand-backs created by this split** (Open Questions HB2, HB4–HB6):
- Grid Inventory H-PERF-1/3 shrinks from 2.0/3.0 ms p99 to 0.5/1.0 ms p99 (steady/worst). Belt endpoint calls move to Automation.
- Mail M-PERF-2's 1.0 ms p99 splits into Mail.Step 0.25, with hand ops counted in phase 0, feeders in phase 5 and NPC drops in phase 6.
- Player Character's PCM-PERF-1 becomes host 0.20 ms plus ClientLayer 0.10 ms.
- Throwing's TC-P-2 sweep becomes 0.15 ms p99 and 0.5 ms max.

### 3.12 F4 EntityId layout and exhaustion

*(verbatim, GDD lines 710–735 as reviewed)*

### F4 — EntityId layout and exhaustion

The entity id formula is defined as:

`id = (class << 24) | counter`;  `class = id >> 24`;  `counter = id & 0xFFFFFF`
`exhaustionSeconds = (2^24 − 1) / allocRatePerSecond`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| class | byte | 0x01–0x08 shipped; 0xF0–0xFF test (R19) | constant | `EntityClass` |
| counter | uint32 | 1 to 16 777 215 | calculated | Per class. Never reused, never wraps. |
| allocRatePerSecond | analysis only | 0–480 | calculated | Worst case: 8 players × 60 ops/s, at most one entity per op |
| mailId, opSeq | uint32 | 1 to 4 294 967 295 | calculated | Separate counters (R19, R6) |

**Output Range:** `id` is in 0x01000001–0x08FFFFFF in shipped runs, and 0 is `None`. `ActorId` System is 0x00000001 (R6).
- **Entity ids.** 16 777 215 / 480 = 34 952 s ≈ **9.7 h** of nonstop worst-case allocation. A run is under 1 h, so exhaustion is unreachable in v1. Free Play (deferred) must re-check this.
- **`opSeq`.** 2^32 / 480 ≈ 103.6 days. A 2 h run uses 0.08 %.
- **`mailId`.** Under 10^4 per run.
- **`Tick`.** 2^32 / 30 s ≈ 4.5 years. The Debug limit of 2^31 is about 2.3 years.

**Example:**
- Player 1 = (0x01 << 24) | 1 = 0x01000001 = **16 777 217**, the example `entityId` in spec 08 §3.
- Construct 1 = 0x03000001 = **50 331 649**, which matches spec 08 §3.5.
- Decoding 50 331 649 gives class 3, counter 1.

### 3.13 F5 RNG derivation and F6 bounded draw

*(verbatim, GDD lines 737–796 as reviewed)*

### F5 — RNG derivation

The stream seed formula is defined as:

`Fnv1a64(name): h = 0xCBF29CE484222325; for each UTF-8 byte b: h = (h ^ b) × 0x100000001B3 (mod 2^64)`
`Mix64(x): z = x + 0x9E3779B97F4A7C15; z = (z ^ (z >> 30)) × 0xBF58476D1CE4E5B9; z = (z ^ (z >> 27)) × 0x94D049BB133111EB; return z ^ (z >> 31)`
`s = Derive(runSeed, name, index) = Mix64(Mix64(runSeed ^ Fnv1a64(name)) ^ (u64)index)`
`initState = Mix64(s)`;  `initSeq = Mix64(s ^ 0xDA3E39CB94B95BDB)`
`pcg32_srandom: state = 0; inc = (initSeq << 1) | 1; step(); state += initState; step()`
`step: state = state × 6364136223846793005 + inc (mod 2^64)`
`next32 (XSH-RR): old = state; step(); xs = (u32)(((old >> 18) ^ old) >> 27); rot = (u32)(old >> 59); out = (xs >> rot) | (xs << ((−rot) & 31))`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| runSeed | uint64 | 0 to 2^64 − 1 | data file | Run seed from lobby settings (`SimSettings`) |
| name | ASCII string | lowercase, dot-separated | constant | `RngNames` registry (R23–R24). Never player text. |
| index | uint32 | 0 to 2^32 − 1 | calculated | Shift number, or 0 for run-lifetime streams (R24) |
| s, initState, initSeq | uint64 | full | calculated | Derived seeds |
| state, inc | uint64 | full; inc is odd | calculated | PCG32 state, stored in the dump (R26) |
| out | uint32 | full | calculated | One draw |

**Output Range:** All arithmetic is mod 2^64 (`unchecked`).
- **Period.** Each stream has period 2^64.
- **Distinct indices.** `Mix64` is a bijection, so for a fixed `(runSeed, name)` distinct indices always give distinct `s`.
- **Distinct names.** A startup test asserts that every `RngNames` entry has a distinct `Fnv1a64`.

**Example / golden vectors.** Computed 2026-10-16 with a reference script; SC-DET-2 commits and re-verifies them on both OSes.

| Input | Output |
|---|---|
| `pcg32_srandom(42, 54)`, first four outputs (PCG reference demo values, checks the PCG core) | `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293` |
| `Mix64(0)` | `0xe220a8397b1dcdaf` |
| `Fnv1a64("mail")` | `0x1f5964a2ce98072e` |
| `Derive(1, "mail", 2)` | `0x120344c795c4b10e` |
| `RngStream.Create(1, "mail", 2)`, first three `Next32` | `0x915ab962, 0xc3f6d39c, 0x5d7b280d` |

### F6 — Bounded integer draw

The bounded draw formula is defined as:

`NextInt(b) = (u32)(((u64)next32 × (u64)b) >> 32)`. For b = 0 it returns 0 and still consumes one draw.

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| next32 | uint32 | full | calculated | One PCG32 draw (F5) |
| b | uint32 | 0 to 2^32 − 1 (in practice ≤ 65 535) | calculated | Exclusive upper bound |
| result | uint32 | 0 to b − 1 (0 when b = 0) | calculated | The draw |

**Output Range:** For b ≥ 1 the result is at most b − 1, and the product is below 2^64.
- **Bias.** Each outcome has floor or ceil of 2^32/b preimages, so the relative bias is at most b/2^32 (about 2.3 × 10^-9 for b = 10).
- **Draw count.** There is no rejection loop, so every call consumes exactly one draw. This is Mail's `K` draw-count contract.

**Example:**
- b = 10 with next32 = 0x80000000 gives 5. With 0xFFFFFFFF it gives 9, and with 0 it gives 0.
- The first draw of `Create(1, "mail", 2)` is 0x915ab962, and `NextInt(10)` on it gives **5**.
- Since 2^32 mod 10 = 6, six outcomes have 429 496 730 preimages and four have 429 496 729.

### 3.14 F7 Money and fixed-point rounding (MulDivRH superseded by section 1)

*(verbatim, GDD lines 798–827 as reviewed)*

### F7 — Money and fixed-point rounding

The rounding formulas are defined as:

`Cents.MulPermille(c, pm) = sign(c) × clamp(((Int128)|c| × pm + 500) / 1000, 0, 2^62)`. The product is widened to `Int128`, and a clamp is counted as `moneyClamped`.
`Fix.MulDiv(a, b, c) = trunc((Int128)a × b / c)`
`Fix.MulDivRH(a, b, c) = sign(a·b·c) × ((2·|a·b| + |c|) / (2·|c|))`
`Fx16.ToMm(fx) = fx >> 16`  and  `Fx16.FromMm(mm) = mm × 65536`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| c | long (cents) | ±2^62 | calculated | Money amount |
| pm | int | 0 to 10^6 (negative asserts) | data file / calculated | Permille multiplier |
| a, b, c (MulDiv) | long | full | calculated | Operands. Multi-factor numerators accumulate in `Int128` with one division at the end. |
| fx | long (Fx16) | ±2^47 | calculated | 16 fractional bits |

**Output Range:**
- **Money rounding.** Rounding is done on the magnitude, half up, and the sign is applied afterwards (symmetric about 0). This is the "RH" already used by the registered formulas `mailUnitPay`, `mailSpawnValue` and `misdeliveryPenalty`, so their examples are unchanged.
- **`MulDiv`.** Uses C# truncation toward zero. `weightSpeed` keeps this mode, and it is not money.
- **Errors.**
  - A zero divisor asserts in Debug; in Release it returns 0 and is counted.
  - A result outside `long` is clamped and counted. Nothing throws inside the tick (R31).
- **`Fx16.ToMm`.** It uses an arithmetic shift, which is floor, also for negatives. `/ 65536` is never used for this.

**Example:**
- `MulPermille(15, 1500) = (22 500 + 500)/1000 = 23`, and `MulPermille(−15, 1500) = −23`.
- `MulDiv(7, 1, 2) = 3` and `MulDiv(−7, 1, 2) = −3`.
- `Fx16.ToMm(98 304)` (1.5 mm) = 1, and `ToMm(−98 304)` = **−2**, where truncation would give −1.

### 3.15 F8 Queue and log capacity

*(verbatim, GDD lines 829–855 as reviewed)*

### F8 — Queue and log capacity

The capacity formulas are defined as:

`opQueueCapacity ≥ nextPow2(ceil(players × opRatePerPlayer × burstSeconds × safetyQ))`
`eventLogCapacity ≥ nextPow2(ceil(safetyE × (entriesPerOp × peakOpsPerTick + systemEventsPerTick)))`
`memoryBytes = capacity × entryBytes`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| players | int | 1–8 | data file | Connected players |
| opRatePerPlayer | int | 60 ops/s | constant (PROPOSED) | Worst-case sustained ops per player |
| burstSeconds | rational | 1.0 | constant | Longest stall whose arrivals land in one drain. Matches the 1 s Δ clamp in F2. |
| safetyQ, safetyE | rational | Q = 8, E = 1.5 | constant | Headroom |
| entriesPerOp | int | 4 | constant (PROPOSED) | A reply plus up to 3 events |
| peakOpsPerTick | int | ≤ 480 | calculated | Ops applied in one drain |
| systemEventsPerTick | int | ≈ 380 | calculated (estimate) | Automation ≈ 200, combat ≈ 100, mail batch ≈ 64, run ≈ 16. Belt items raise inserts and removes only, not per-tick events. |
| entryBytes | int | Op 64 / event entry 64 | constant | Preallocated struct sizes (R6, R11) |

**Output Range:** Capacities are powers of two. The defaults are `opQueueCapacity` 4096 (safe 1024–16 384) and `eventLogCapacity` 4096 (safe 2048–16 384). Too small degrades loudly: a full op ring answers `busy`, and a full event log flags `overflow` and forces a resync (R14). Too large costs only memory.

**Example:**
- **Op queue.** 8 × 60 × 1.0 × 8 = 3 840, which rounds up to **4 096**. 4 096 × 64 B = 256 KiB.
- **Event log.** In a worst burst tick, 480 × 4 + 380 = 2 300 entries. × 1.5 = 3 450, which rounds up to **4 096**. That is 256 KiB.
- Both together are 512 KiB, far inside the 64 MB Sim heap ceiling.

### 3.16 Edge Cases (full original set)

*(verbatim, GDD lines 857–952 as reviewed)*

## Edge Cases

*Specialist: systems-designer (round 3). Its proposals were adopted in autonomous mode. Where one amends a rule or formula, the amendment was applied there too: R7 System reserve, R14 two-tier admission, F2 backwards clock and range, F7 `Int128` widening. Drift it found in R12, the Run Structure row and F2/F7 is fixed.*

### Zero, max and out-of-range

- **If a content duration is 0 ms**: it converts to 0 ticks. The timer is due at its owner's next check, which may be later in the same tick. Any ms ≥ 1 gives at least 1 tick (F1, R15).
- **If content seconds have more than 3 fractional digits, or ms is negative or above 86 400 000**: the content fails to load and no world is built (F1).
- **If `deadlineTick < CurrentTick`**: `r` clamps to 0 and the clock shows 0:00. Timers fire on `CurrentTick ≥ deadlineTick`, never on `r` (F1, R15).
- **If `MulPermille`'s product exceeds `long`**: it is computed in `Int128`, the result is clamped to ±2^62, and `moneyClamped` is counted (F7).
- **If a `Cents` add or subtract leaves ±2^62**: Debug throws via `checked`. Release saturates at ±2^62 and counts `moneySaturated`. Negative balances inside the range are legal, for example Mail's `wallet_floor` of −500 (R27, R31).
- **If `Fix.MulDiv` has a zero divisor or an out-of-range result**: it returns 0 or clamps, counted, and never throws (F7, R31).
- **If `NextInt(0)` is called**: Debug asserts. Release returns 0 and still consumes one draw (R25, F6).

### Same-tick ordering and collisions

- **If two lethal damage entries hit one carrier in the same tick**: Health & Damage raises **one** `PlayerDied`, for the carrier's first lethal pending entry, and killer credit goes to that entry (hand-back HB7). As a backstop, phase 11 ignores a second `PlayerDied` for a carrier that is already Dead. Exactly one `CarrierKnockedDown` results (R3).
- **If a `Combat.React` reaction raises damage**: Debug asserts. Release applies the damage in phase 10 of T+1 and counts `lateDamage` (R3).
- **If phase 11 teleports a carrier (P15)**: `Teleport` bumps `seq`, clears history and seeds one sample at the new position.
  - Phase 3 of T has already run, so there is no double handling in T.
  - At T+1, range checks in phase 0 see only the seeded sample.
  - Phase 3 discards client samples carrying the older `seq` (R5).
- **If one player is teleported twice in a tick** (for example in phase 6, then phase 11): `seq` bumps twice and two `TeleportIssued` events are logged. The client applies the higher `seq`, so the later teleport wins (R5).
- **If `Run.Step` changes the run phase while ops for the old phase are queued**: the change applies from T+1. Ops are validated against the phase at apply time, so a build request queued during Prep tick T is rejected at T+1 with its system's `phase_closed` reason. Nothing is rolled back (R3, R8).
- **If a host loopback op and a remote op arrive in the same frame**: `opSeq` follows call order. `GameLoop` pumps transport callbacks first, then the loopback adapter, then runs ticks. The host's op therefore sorts after any remote op already received. This is a documented host-side tie-break bias. The remote op left its sender earlier in real time (R6, R17, R18).
- **If a player op and an inserter target the last free cell in the same tick**: the player op wins, because phase 0 runs before phase 5. The inserter retries next tick (R9).

### Time, driver and pause

- **If the timestamp frequency is outside 1 000–10^12 counts/s**: the `TickDriver` constructor throws, outside any tick. The frequency is fixed for the driver's lifetime (F2, R17).
- **If a timestamp goes backwards** (`now < last`): Δ = 0, no time is added, and `last` does not move back (F2).
- **If one frame's Δ is 5 s**: Δ clamps to 1 s, 4 ticks run, about 26 are dropped, and the 4 s beyond the clamp go to `clampedTimeTotal` (F2).
- **If every tick costs more than 33.3 ms**: one tick runs per frame (the guard, checked after each tick) and the surplus is dropped. The game runs in slow motion, with no death spiral. The hitch log is rate-limited to one line per second (R17, F2).
- **If a frame runs 0 ticks**: latched input edges stay latched, in order, for the next frame that runs a tick. When 4 ticks run, edges attach to the first only, and ticks 2–4 carry the held bits and the latest `moveIntent`. A dropped tick never drops an edge (R17).
- **If `Alpha` is read while paused**: it is 0. Presentation shows the latest tick without extrapolation, which may look up to 33 ms behind the pause overlay (R17).
- **If `Pause()` is requested while a remote peer is connected or mid-handshake**: Networking & Session refuses it. Any transport peer in any handshake state counts as a remote peer (R16).
- **If a connection starts during a solo pause in Lobby or Prep**: Networking calls `Resume()` at the first transport connect callback, so Prep time runs during the handshake. The driver never re-pauses on its own if the handshake fails (R16).
- **If a connection arrives while joins are closed** (Shift phases): Networking refuses it and does not resume. A `PlayerJoin` that reaches phase 0 after Prep ends is rejected `join_closed` (R16, R21).
- **If a `PlayerJoin` is somehow enqueued while paused**: it waits in the ring and applies on the first tick after resume. The Sim cannot see driver state, so this is a Networking contract (R16).
- **If `Pause()` or `Resume()` is called twice, or `Advance` while `Idle`**: idempotent no-op; `Advance` returns 0 (R17).

### Ops and replies

- **If an op is enqueued during the drain**: it gets the next `opSeq` and applies next tick. Drained slots are freed at pop (R7, R8).
- **If an actor's op and that actor's `PlayerLeave` are in the same drain**: an op with lower `opSeq` than the leave applies normally. A higher one is rejected `unknown_actor`. Either way it is recorded and consumes its `opSeq` (R8).
- **If a slot freed by `PlayerLeave` gets a `PlayerJoin` in the same tick**: the join is rejected `slot_busy`. A slot can be reassigned from the next tick only, because `audienceMask` is resolved at raise time and the newcomer must not receive the leaver's replies (R21, R11).
- **If the op ring is at the player limit**: player ops get `busy` and are not recorded, while `System` ops still enter the reserved slots (R7).
- **If an op has an unregistered `kind` or a payload length that does not match its descriptor**: it is rejected `unknown_kind`, consumes its `opSeq` and is recorded. The adapter validates `kind` before enqueue where it can (R4, R6, R8).
- **If a handler rejects after partial validation**: nothing changes — no state, allocator, RNG or events. Only the reply is raised (R8).

### Events

- **If the event log fills during a handler**: state is not rolled back. Kinds with a pull-cursor reader and `OpReply` can use the last `eventLogReserve` entries, others are dropped and counted, and the batch carries `overflow = true` (R14). `PlayerDied` is never the dropped kind before the reserve is exhausted, so knockdowns stay consistent.
- **If `Events.Update(index)` targets an entry from an earlier phase, or a dropped entry**: Debug asserts. Release writes to a scratch entry, counts, and loses the write (R13).
- **If an event is raised in the tick-end hook or during `Events.Flush`**: the log is sealed. Debug asserts. Release drops the event, counts `eventsAtTickEnd` and sets `overflow = true`, because a missing event desyncs clients (R3, R14).
- **If an `OwnerOnly` event has zero or several `audienceMask` bits**: zero delivers to nobody. Several asserts in Debug, and Release delivers to the lowest set bit only (R12).
- **If the sink keeps the span after `Deliver` returns**: the span is invalid. Debug fills the cleared log with 0xDD so stale reads fail fast (R12).

### Ids and RNG

- **If code asks for a stream name that is not registered**: it cannot compile. `StreamName` is a struct with a private constructor, and the only instances are the `RngNames` constants.
  - An out-of-range index (for example `mail` with shift 0 or 6) asserts in Debug. Release derives the stream anyway and counts (R23, R24).
- **If the same `(name, index)` stream is created twice in one tick** (Mail's Prep re-roll): both start from identical state and give identical sequences. The registry keeps one live handle per key. The second `Create` retires the first and resets `draws`, so the dump lists one entry (R24, R26, F5).
- **If two different names derive colliding seeds**: this is not defended. It cannot happen within one name (`Mix64` is a bijection). Across about 45 name pairs the chance is around 10^-8 per run, and the two streams would share a sequence undetected (F5).
- **If a per-class counter reaches 2^24 − 1**: `Allocate` returns `None`, one log record is written, and the caller refuses the spawn. A caller that uses `None` asserts in Debug.
  - `mailId` exhaustion returns 0 the same way.
  - `EnqueueOp` returns false at `opSeq` 2^32 − 1.
  - All of these are unreachable in a run (R20, F4).
- **If a mail batch has zero units**: no `mailId` is allocated (R19).
- **If an `EntityId` from another `SimWorld` is used** (tests only): ids are plain `uint32`, so this goes undetected (R19).

### Determinism and replay

- **If a replay's `ContentSet` hash, state-affecting `SimSettings` or `runSeed` differ from the recording's header**: the loader refuses, and no world is built (R8, R29).
- **If the recording or the replay saw an event-log overflow**: the replay is marked non-reproducible, because capacity knobs decide which events drop (R14).
- **If a dump is requested during `Tick()`, including from inside the sink**: Debug asserts. Release returns false and counts `dumpDuringTick`. A `Stopped` world's dump is valid (R30).
- **If `float`, `double` or `decimal` appears in `src/Sim` state code, or a `Dictionary`/`HashSet` is iterated**: the build fails. The banned-API analyzer reports an error, not a warning, with no allowlist inside the Sim (R28).
- **If a GC pause lands inside a tick**: output is unchanged, because ticks are logical. It appears only as a wall-clock hitch handled by F2. Post-warm-up allocation fails the 0-byte gate (R32, F3).
- **If a Debug `checked` overflow fires where Release would wrap**: it is a bug. Debug surfaces it, and the Debug and Release dumps are otherwise identical (R31, R29).
- **If an exception escapes `Tick()`**: Debug rethrows. Release has the adapter call `Stop()`, mark the world faulted and show an error. The dump stays readable, and no further ticks run (R31).

### Lifecycle and join/leave

- **If `Stop()` is called from a phase other than 12**: Debug asserts. Release latches it as if raised in phase 12, finishes the tick through `Events.Flush`, then transitions and counts `stopOutOfPhase` (States).
- **If `Stop()` is called twice, or on a `Stopped` world**: ignored and counted, with no assert. A host quit can race Results legitimately (States).
- **If `Tick()` is called on a `Constructed` or `Stopped` world, or re-entrantly**: no phase runs, `CurrentTick` does not advance, and nothing is flushed. Debug asserts and Release counts `badTick` (States).
- **If `PlayerJoin` has an out-of-range slot, an occupied slot, or a rejoin `playerId` already present**: rejected `slot_invalid`, `slot_busy` or `duplicate_player` respectively, and nothing is allocated (R21).
- **If the last player leaves**: the world keeps ticking. Only `Stop()` ends it (R21).
- **If two `SimWorld`s run in one process** (tests): allowed. Each has its own thread capture, probe and counters, and only `static readonly` tables are shared. The 0-alloc test runs in the non-parallel collection (R28, R32).

### Degenerate and abuse cases (friends-only trust model, spec 13 §4.3)

- **If a peer floods ops**: the player share of the ring fills and every player gets `busy`. There is no per-actor quota. **Not defended**; the host kicks the peer by hand (R7).
- **If a client resends a `reqId` or replays an op**: it applies twice. The Sim does not deduplicate `reqId` and relies on reliable-ordered transport. **Not defended** (R6, R10).
- **If a client lies in a payload field** (`holdStartTick`, transform samples, `originClient`): impersonation is blocked, because `actor` is stamped by the adapter. Handlers range-check for *consistency* only, and movement is client-authoritative. **Not defended** (R5, R6).
- **If the host edits its build or a replay log**: undetected. The dump hash proves consistency, not honesty. **Not defended** (R29).

### 3.16b Developer overlay field list (Dev Tooling input)

*(verbatim, GDD lines 1039–1052 as reviewed)*

## UI Requirements

**No player-facing UI.** No UX Flag is raised for this system. The run clock *value* rule (F1 `displaySeconds`) is consumed by HUD & Notifications, which owns its presentation.

**Developer overlay** (spec 07 §9). The fields below are owned and built by **Dev Tooling**. Simulation Core only exposes them:
- Per-phase and `ProbeScope` p50/p99/max (`IPhaseProbe`).
- Tick time on tick frames versus non-tick frames.
- `ticksRunTotal`, `droppedTicksTotal`, `clampedTimeTotal`.
- Op queue depth and `busy` count; event-log high-water mark, `overflow` count and reserve use.
- Per-stream RNG `draws` per tick.
- Id counters per class.
- Always-on log ring tail.
- `Sim.AssertsEnabled`.
- GC collections per minute.

### 3.16c Network N1–N6 (Networking & Session input; N3.4–N3.7, N5 and the IHostClock estimator are Networking-owned)

*(verbatim, GDD lines 1054–1118 as reviewed)*

## Network

*Specialist: network-programmer (round 1), reconciled with the round-2 rules.*

Simulation Core replicates nothing itself. It defines **what the Sim exposes to the network adapter**, and what it must never know. Transport, lobby, JoinState envelope, chunking and pacing belong to Networking & Session (#5). Godot high-level multiplayer behaviour for 4.4–4.7 is **NOT SOURCEABLE** from `docs/engine-reference/godot/`. Every reliance on it below is UNVERIFIED and routed to the pre-M0 multiplayer spike (Grid Inventory OQ12, OQ2 here).

### N1 — The boundary (what the Sim exposes)

| Direction | Port | Called when | Notes |
|---|---|---|---|
| in | `SimWorld.EnqueueOp(actor, in Op)` → bool | Any time, on the Sim thread | `actor` is stamped by the adapter from the peer (R6). Returns false when full: the adapter replies `busy` (R7). |
| in | Transform sample latch (Player Character's `ISampleSink`) | Any time, on the Sim thread | Stamped with `receivedTick = CurrentTick` (R5) |
| out | `IEventSink.Deliver(tick, ReadOnlySpan<entry>)` | Once per tick, in phase 13 | Ordered entries, with replication class, `audienceMask` and an `overflow` flag (R11–R14). The span is valid only during the call. |
| out | `ISnapshotSource.Write(tick, ISnapshotWriter)` | Between ticks, after `Deliver` | Read-only state for continuous replication: remote transforms, vehicles, enemies, HP percent. Each owning system writes its own block. No mutation. |
| out | Join snapshot | Between ticks, at a tick boundary | The JoinState contribution interface is **defined by Networking & Session**. Sim Core guarantees only that a snapshot taken between ticks after `Deliver` is consistent, and that it records `joinTick`. |
| both | `IHostClock { uint32 EstimatedHostTick; bool IsValid }` | Read by `ClientLayer` and Presentation | **Host:** returns `CurrentTick`, frozen during a solo pause. **Client:** last batch tick + smoothed RTT/2, monotonic non-decreasing, slewing ≤ 10 %, snapping only on resync. Implemented by Networking. |

**What the Sim must not know:** peers, accounts, channels, RPC names, chunking, pacing, wall clock, RTT, `hostTickEst` (it only defines the interface), per-peer filtering, packing, or any Godot type.

### N2 — Roles on the wire

- **Host process.** One `SimWorld` plus one `ClientLayer`, connected by the **loopback adapter**.
  - Loopback uses the same op encoder as remote clients.
  - It hands each batch to `ClientLayer`'s inbox synchronously when `SimWorld.Tick()` returns, never through `call_deferred` or a frame queue. `MirrorDrain` applies it at the start of the next client tick (R2).
  - A host op enqueued in frame F therefore applies at the next `Inputs.Apply`, and its reply reaches the host's client layer in the same or the next tick (Manual Delivery).
  - Whether loopback can bypass the MultiplayerAPI with a direct call is UNVERIFIED. This design assumes it can.
- **Remote client.** One `ClientLayer`. It never holds a `SimWorld`, and never re-simulates the world (R29).
- **Dedicated server** (deferred). A `SimWorld` with no `ClientLayer`.

### N3 — Event replication contract

1. **One batch per tick, in raise order.** The adapter delivers it on **one reliable-ordered channel**; that ordering holds across RPC methods is UNVERIFIED.
2. **Audience.**
   - `Internal` entries never leave the process.
   - `OwnerOnly` and `Targeted` entries go only to the players in `audienceMask`.
   - `Replicated` entries go to all players.
3. **Replies.** `OpReply` entries are `OwnerOnly` and already follow their events (R10). The adapter must not reorder within a peer's stream.
4. **Coalescing and packing.** The adapter may coalesce or pack per peer under owner rules, for example Grid Inventory `ContainerDelta`. It must never change the relative order of two entries delivered to the same peer.
5. **Overflow.** A batch with `overflow = true` forces Networking to resync every audience in that batch (R14).
6. **Missed events are not replayed** (Mail rule). Rejoin uses a state resync, not event history.
7. **JoinState vs events.** A joiner discards every batch with `tick ≤ joinTick`, which removes the JoinState-on-channel-2 versus events-on-channel-1 race.

### N4 — Wire widths owned here

| Value | Width | Rule |
|---|---|---|
| Tick | u32 | Narrower forms are allowed only where the field is documented wrap-safe and uses the `Serial*` helpers (R22) |
| `EntityId` | u32 | |
| `mailId` | u32 | |
| `PlayerSlot` | u8, 0–7 | |
| `opSeq`, event `ordinal` | — | Sim-internal: never on the wire, except in replay logs |

Compact ids such as Throwing's 2-byte `receiverId` come from id maps, never from truncation (R22).

**FLAG (HB6).** Any tick a client puts in a request must be in the **host tick domain** (`IHostClock.EstimatedHostTick`, or its low 16 bits where the field is wrap-safe), never a client's local counter. Manual Delivery's `holdStartTick` already is. Throwing's registered `ThrowRelease` carries no tick (`throwPayloadBytes`). Input & Controls R11 still lists a `clientTick` on the throw release, so the two GDDs must agree: drop it, or define it in the host domain.

### N5 — Bandwidth notes (PROPOSED; budgets belong to Networking)

- **Per-batch overhead.** The event envelope costs 0 B on empty ticks and ≤ 3 B per non-empty batch (tick delta varint + count). Each event costs a 1-byte type id plus its typed payload.
- **Per-packet overhead.** About 40 B per packet (IP/UDP/ENet, UNVERIFIED). One event packet every tick would cost about 40 B/tick per peer, which is as much as Grid Inventory's whole payload budget.
- **Recommendation to Networking.** Send at most one event packet per tick. Consider an `eventSendEveryNTicks` knob (default 1; 2 halves the overhead for +33 ms).

### N6 — Determinism across machines

Only R29 T2 is cross-machine: the carrier step, RNG and hash primitives, pure functions, and any checksum both sides compute. The world is never lockstepped. A client never needs the world RNG; mail manifests, waves and drops arrive as events or state.

### 3.17 Original Acceptance Criteria (131) — input to story test plans, ADR validation and owning GDDs

*(verbatim, GDD lines 1139–1701 as reviewed)*

## Acceptance Criteria

*Specialist: qa-lead (round 3). qa-lead recomputed every formula example and the F5 golden vectors with an independent script, and all match. It found six ambiguities, now fixed in the rules: the F3 max-column sum, the GameLoop-enforced guard (R17), initial counters for exhaustion tests (R20), the 9th reader bind (R12), the serial-helper semantics (R22) and the gate measurement APIs (F3).*

**Legend.**
- Milestones: [M-1a], [M-1b] and [M0], from the milestone slice.
- Types: [Logic], [Integration], [Perf] and [CI].
- **[SPIKE]** means the criterion depends on UNVERIFIED engine behaviour. Its pass condition covers only the Godot-free part. The Godot-side result is recorded as spike pass/fail, with no assumed outcome.
- The playtest fantasy tests (Player Fantasy) are not covered here. They need an M0 playtest protocol (`/playtest-report`).

### Tick, roles, phases

- **SC-R1-1** [M-1a][Logic]
  - **GIVEN** a `Ready` world
  - **WHEN** `Tick()` is called 30 times
  - **THEN** handlers see `CurrentTick` = 0 during the first call, it reads 1 after that call, and 30 after the last.
- **SC-R1-2** [M-1a][Logic]
  - **GIVEN** a world ticked 100 times and then dropped
  - **WHEN** a new world is built from the same inputs
  - **THEN** it starts at tick 0 and shares no state with the first.
- **SC-R1-3** [M-1a][CI]
  - **GIVEN** the Sim assembly
  - **WHEN** reflection inspects `SimWorld.Tick`
  - **THEN** it takes no time or delta parameter (no variable step).
- **SC-R2-1** [M-1a][Integration]
  - **GIVEN** a host process with the loopback adapter
  - **WHEN** `Advance` returns 3
  - **THEN** the call log reads Client, Sim, Client, Sim, Client, Sim, and input edges attach only at i = 0.
- **SC-R2-2** [M-1a][Logic]
  - **GIVEN** `ClientLayer` steps bound in shuffled order
  - **WHEN** a client tick runs
  - **THEN** they execute in `ClientStepId` order: MirrorDrain, InputLatch, CarrierStep, Previews. Unbound steps are no-ops.
- **SC-R2-3** [M0][Integration]
  - **GIVEN** a remote sample ingested in phase 3 of tick T
  - **WHEN** the host's own `ClientLayer` reads `IRemoteCapsuleSource` during tick T
  - **THEN** it returns the T−1 ingest.
- **SC-R2-4** [M-1a][CI]
  - **GIVEN** the `ClientLayer` namespace
  - **WHEN** the architecture test runs
  - **THEN** it has no reference to `SimWorld` and names no gameplay type.
- **SC-R3-1** [M-1a][Logic]
  - **GIVEN** a stub bound to every phase
  - **WHEN** one tick runs
  - **THEN** the recorded order is phases 0…12, then the tick-end hooks, then 13. This equals the `static readonly PhaseId[]`.
- **SC-R3-2** [M-1a][Logic]
  - **GIVEN** only phases 0–4, 8 and 12 bound
  - **WHEN** 300 ticks run
  - **THEN** no exception occurs and `CurrentTick` = 300.
- **SC-R3-3** [M-1a][Logic]
  - **GIVEN** a tick-end hook, or code running in `Events.Flush`, that raises an event
  - **WHEN** it runs
  - **THEN** Debug asserts. Release drops the event, counts `eventsAtTickEnd` and sets `overflow = true`.
- **SC-R4-1** [M-1a][Logic]
  - **GIVEN** a builder
  - **WHEN** a phase is bound twice, an `OpKind` has no handler, a 9th event reader is bound, or anything is bound after `Freeze()`
  - **THEN** each is a build-time error.
- **SC-R4-2** [M0][CI]
  - **GIVEN** `SimComposition.Build()`
  - **WHEN** the reflection test runs
  - **THEN** every system constructor parameter is in the allowed-dependency list, and a synthetic dependency cycle fails the build.
- **SC-R5-1** [M0][Integration]
  - **GIVEN** a remote transform sample arriving at tick T, and a sample carrying an older Teleport `seq`
  - **WHEN** phase 3 of T runs
  - **THEN** the first is ingested with `receivedTick` = T and the stale one is discarded.
- **SC-R5-2** [M-1b][Logic]
  - **GIVEN** one player teleported in phase 6 and again in phase 11 of a tick
  - **WHEN** the tick completes
  - **THEN** `seq` is bumped twice, two `OwnerOnly` `TeleportIssued` events are logged, and history holds one seeded sample.

### Ops and replies

- **SC-R6-1** [M-1a][Logic]
  - **GIVEN** the op descriptor table
  - **WHEN** it is enumerated
  - **THEN** `sizeof(Op)` = 64, the header is 16 B, and every registered payload is ≤ 48 B.
- **SC-R6-2** [M-1a][Logic]
  - **GIVEN** an op whose payload names another player
  - **WHEN** the adapter enqueues it
  - **THEN** the handler sees the adapter-stamped `actor`, and `System` = 0x00000001.
- **SC-R6-3** [M-1a][Logic]
  - **GIVEN** 1 000 enqueues, some of them refused
  - **WHEN** their `opSeq` values are read
  - **THEN** accepted ops have strictly increasing `opSeq`, with no reuse, and refused enqueues consume none.
- **SC-R7-1** [M-1a][Logic]
  - **GIVEN** capacity 4096 and `systemOpReserve` 64, with 4032 player ops queued
  - **WHEN** another player op is enqueued
  - **THEN** it returns false, consumes no `opSeq`, increments the busy counter and does not throw. `System` ops are still accepted up to 4096.
- **SC-R7-2** [M-1a][Logic]
  - **GIVEN** a `Constructed` or `Stopped` world
  - **WHEN** `EnqueueOp` is called
  - **THEN** it returns false and is counted. In a `Ready` world, the op is queued for tick 0.
- **SC-R7-3** [M-1a][Logic]
  - **GIVEN** a Debug build
  - **WHEN** `EnqueueOp` or `Tick` is called from a non-owner thread
  - **THEN** an assert fires.
- **SC-R8-1** [M-1a][Logic]
  - **GIVEN** 5 queued ops, one of whose handlers enqueues another op
  - **WHEN** the tick drains
  - **THEN** the 5 apply in ascending `opSeq` and the new op applies next tick.
- **SC-R8-2** [M-1a][Logic]
  - **GIVEN** a handler that rejects after validation
  - **WHEN** it is applied
  - **THEN** the state hash, allocators and RNG `draws` are unchanged, and the only log entry is its `OpReply`.
- **SC-R8-3** [M-1a][Logic]
  - **GIVEN** an op from a departed actor and an op with an unregistered `kind`
  - **WHEN** they are drained
  - **THEN** the replies are `unknown_actor` and `unknown_kind`, and both consume `opSeq` and are recorded.
- **SC-R8-4** [M-1a][Logic]
  - **GIVEN** an in-memory `IOpRecorder`
  - **WHEN** 10 ops drain, 3 of them rejected
  - **THEN** `OnDrained` is called 10 times, each with the apply tick and the result.
- **SC-R9-1** [M-1b][Integration]
  - **GIVEN** one free cell, targeted by a player op (phase 0) and an inserter (phase 5)
  - **WHEN** the tick runs
  - **THEN** the player op wins, the inserter's synchronous call fails without an `OpReply`, and the inserter succeeds on a later tick once a cell is free.
- **SC-R10-1** [M-1a][Logic]
  - **GIVEN** a handler that raises 2 events
  - **WHEN** it commits
  - **THEN** the log reads event, event, `OpReply`, and the reply is `OwnerOnly` with `reason` = 0.

### Event bus

- **SC-R11-1** [M-1a][Logic]
  - **GIVEN** two consecutive ticks that each raise entries
  - **WHEN** they run
  - **THEN** ordinals are 0…n−1 in each tick, and the log is empty at the start of the next. In Debug the cleared log reads 0xDD.
- **SC-R11-2** [M-1a][Logic]
  - **GIVEN** a container viewer who opens after event E was raised
  - **WHEN** the batch is built
  - **THEN** that viewer's bit is absent from E's `audienceMask`.
- **SC-R12-1** [M-1b][Logic]
  - **GIVEN** a `Combat.React` cursor reader
  - **WHEN** it reads in phase 11
  - **THEN** it sees phase-10 entries and never a later phase's, and entries it raises append after all existing entries.
- **SC-R12-2** [M-1a][Logic]
  - **GIVEN** a sink stub
  - **WHEN** a tick runs
  - **THEN** `Deliver` is called exactly once, from phase 13, with entries in raise order.
- **SC-R12-3** [M-1a][Logic]
  - **GIVEN** all event kinds
  - **WHEN** the enumeration test runs
  - **THEN** any kind with no declared replication class fails it, and `Internal` kinds never reach the sink's per-peer output.
- **SC-R12-4** [M-1a][Logic]
  - **GIVEN** an `OwnerOnly` event with 0 audience bits, and another with 2
  - **WHEN** audiences are resolved
  - **THEN** 0 bits delivers to nobody. 2 bits asserts in Debug, and Release delivers to the lowest bit.
- **SC-R13-1** [M-1a][Logic]
  - **GIVEN** a `Coalescible` `replace` raised and updated within the same phase
  - **WHEN** a later `OpReply` is raised
  - **THEN** the entry stays at its first position and the reply follows it.
- **SC-R13-2** [M-1a][Logic]
  - **GIVEN** `Events.Update` on an entry from an earlier phase
  - **WHEN** called
  - **THEN** Debug asserts. Release writes to a scratch entry, counts it, and the write is lost.
- **SC-R14-1** [M-1a][Logic]
  - **GIVEN** `eventLogCapacity` 4096 and `eventLogReserve` 256
  - **WHEN** non-reader kinds fill the log
  - **THEN** they stop at 3840, the rest are dropped and counted, and `overflow = true`. Kinds with a reader (`PlayerDied`) and `OpReply` are still accepted up to 4096, and order is unchanged.
- **SC-R14-2** [M-1a][Logic]
  - **GIVEN** a Release build
  - **WHEN** an event is raised outside `Tick()`
  - **THEN** it is dropped and `eventsOutsideTick` increments.
- **SC-R14-3** [M0][Logic]
  - **GIVEN** a recording or replay that saw an overflow
  - **WHEN** it is loaded
  - **THEN** it is marked non-reproducible.

### Time, pause, driver, thread

- **SC-R15-1** [M-1a][Logic]
  - **GIVEN** `deadlineTick` = 100
  - **WHEN** `CurrentTick` is 99, then 100
  - **THEN** the timer is not due, then due.
- **SC-R16-1** [M-1a][Logic]
  - **GIVEN** a `Running` driver that is then paused
  - **WHEN** `Advance` is called with timestamps 10 s apart
  - **THEN** it returns 0 and `Alpha` = 0.
- **SC-R16-2** [M-1a][Logic]
  - **GIVEN** a driver paused for 10 s and then resumed
  - **WHEN** `Advance` is called one tick-time later
  - **THEN** it returns ≤ 1, and no time is banked.
- **SC-R16-3** [M-1a][Integration]
  - **GIVEN** ops enqueued while paused
  - **WHEN** the driver resumes
  - **THEN** they apply on the first tick after resume.
- **SC-R16-4** [M0][Integration] (shared with the Networking & Session GDD's criteria)
  - **GIVEN** a connection starting during a solo pause in Prep
  - **WHEN** the connect callback fires
  - **THEN** `Resume()` is called before `PlayerJoin` is enqueued, and `Pause()` is refused while any remote peer is in any handshake state.
- **SC-R16-5** [M-1a][CI]
  - **GIVEN** the source tree
  - **WHEN** it is grepped
  - **THEN** `SceneTree.Paused` is never used to pause the Sim.
- **SC-R17-1** [M-1a][CI]
  - **GIVEN** `Hosting/`
  - **WHEN** the architecture test runs
  - **THEN** `TickDriver` has no Godot reference and no clock read (`Stopwatch`, `DateTime`), and tests pass integer timestamps.
- **SC-R17-2** [M-1a][Logic]
  - **GIVEN** a frame that runs 3 ticks, with a press and a release latched
  - **WHEN** the edges are consumed
  - **THEN** both attach to the first tick only, ticks 2–3 carry held bits and the latest `moveIntent`, and the hold is cancelled (Input H-EC-4). A frame that runs 0 ticks keeps its edges latched, in order.
- **SC-R17-3** [M-1a][Logic]
  - **GIVEN** a long stall
  - **WHEN** `Resync(ts)` is called and `Advance` follows
  - **THEN** `acc30` = 0, no hitch is logged, and `droppedTicksTotal` is unchanged.
- **SC-R17-4** [M-1a][Logic]
  - **GIVEN** repeated hitches within 1 s
  - **WHEN** they are logged
  - **THEN** at most one line per second is written.
- **SC-R17-5** [M-1a][SPIKE]
  - **GIVEN** a Godot `GameLoop` calling `Advance(Stopwatch.GetTimestamp())` from `_Process`
  - **WHEN** the spike runs for 60 s at 60 Hz, at 144 Hz, and with vsync off
  - **THEN** the spike records `ticksRunTotal`, `droppedTicksTotal`, and whether `ProcessMode = Always` is honoured. No pass is asserted until OQ1's ADR sets one.
- **SC-R18-1** [M-1a][Logic]
  - **GIVEN** the headless harnesses (xUnit, BalanceSim, BotClient)
  - **WHEN** they run
  - **THEN** they call `SimWorld.Tick()` in a plain loop, with no driver.
- **SC-R18-2** [M0][SPIKE]
  - **GIVEN** transport callbacks
  - **WHEN** they fire
  - **THEN** they only `EnqueueOp` and apply nothing. Which thread they arrive on is recorded by the spike, not assumed.

### Identity

- **SC-R19-1** [M-1a][Logic]
  - **GIVEN** the class table
  - **WHEN** ids are allocated
  - **THEN** class 0x01 counter 1 = 0x01000001, ids decode back to (class, counter), `None` = 0, and classes 0x09–0xEF cannot be allocated in a shipped build.
- **SC-R19-2** [M-1a][Logic]
  - **GIVEN** a mail batch of 3 units, then an empty batch
  - **WHEN** `mailId`s are allocated
  - **THEN** the 3 ids are consecutive in manifest order from a counter separate from the entity counters, and the empty batch allocates none.
- **SC-R20-1** [M-1a][Logic]
  - **GIVEN** 10 000 allocations interleaved with removals
  - **WHEN** the ids are compared
  - **THEN** none is reused, and within a class ascending `uint32` order equals creation order.
- **SC-R20-2** [M-1a][Logic]
  - **GIVEN** `EntityIds` constructed with a class counter at 2^24 − 1 (initial counters, R20)
  - **WHEN** `Allocate` is called
  - **THEN** it returns `None`, writes one log record, and never wraps. `MailIds` at 2^32 − 1 returns 0.
- **SC-R21-1** [M-1a][Logic]
  - **GIVEN** `PlayerJoin` ops
  - **WHEN** they apply in phase 0
  - **THEN** each allocates a fresh `player` id, a rejoin carrying its `playerId` reuses it, and `AddPlayer` is reachable only from that handler.
- **SC-R21-2** [M-1a][Logic]
  - **GIVEN** a `PlayerLeave` and a `PlayerJoin` for the same slot in one tick
  - **WHEN** they drain
  - **THEN** the join is rejected `slot_busy`. A bad slot gives `slot_invalid` and a present `playerId` gives `duplicate_player`, with nothing allocated. After the last player leaves, the world keeps ticking.
- **SC-R22-1** [M-1a][Logic]
  - **GIVEN** `SerialU16`
  - **WHEN** `IsNewer(1, 65535)`, `IsNewer(65535, 1)` and `Diff(1, 65535)` are evaluated
  - **THEN** they give true, false and 2. A half-range difference asserts in Debug. `WireNarrow` asserts in Debug and clamps and logs in Release.

### Randomness

- **SC-R23-1** [M-1a][Logic]
  - **GIVEN** the `RngNames` registry
  - **WHEN** the startup test runs
  - **THEN** every name is lowercase, ASCII and dot-separated, and all `Fnv1a64` values are distinct.
- **SC-R23-2** [M-1a][CI]
  - **GIVEN** `src/Sim`
  - **WHEN** the analyzer runs
  - **THEN** any use of `System.Random`, `Guid.NewGuid` or `GetHashCode` in state code is a build error.
- **SC-R24-1** [M-1a][Logic]
  - **GIVEN** `mail` streams for shifts 1 and 2 of one seed, and `Create(seed, "mail", 1)` called twice
  - **WHEN** they are drawn
  - **THEN** the two shifts' sequences differ, the two shift-1 streams are identical, and the dump holds one entry for that key with `draws` reset.
- **SC-R24-2** [M-1a][Logic]
  - **GIVEN** a composed world
  - **WHEN** the RNG registry is inspected and 100 throws are launched
  - **THEN** Throwing holds no `RngStream` handle, and the total `draws` across all streams is unchanged by the throws (`throwNoDice`).
- **SC-R24-3** [M-1a][Logic]
  - **GIVEN** a `mail` stream requested for shift 0 or shift 6
  - **WHEN** it is created
  - **THEN** Debug asserts, and Release derives it and counts.
- **SC-R25-1** [M-1a][Logic]
  - **GIVEN** `NextInt(0)`
  - **WHEN** called
  - **THEN** it returns 0, `draws` increases by exactly 1, and Debug asserts. Reflection finds no float-returning method on `RngStream`.
- **SC-R26-1** [M-1a][Logic]
  - **GIVEN** 3 live streams drawn 5, 0 and 7 times
  - **WHEN** the dump is written
  - **THEN** it records `(name, index, state, inc, draws)` for each, with `draws` = 5, 0 and 7.

### Numbers, determinism, dump, safety

- **SC-R27-1** [M-1a][Logic]
  - **GIVEN** the Q15 sin/cos table
  - **WHEN** the guard test runs
  - **THEN** it has 4096 entries and its hash equals the committed value. `Cents` is a readonly struct over `long`.
- **SC-R28-1** [M-1a][CI]
  - **GIVEN** a commit that adds `float`, `double` or `decimal` to Sim state code, or iterates a `Dictionary` or `HashSet`
  - **WHEN** CI builds
  - **THEN** the banned-API analyzer fails the build with an error.
- **SC-R28-2** [M-1a][Logic]
  - **GIVEN** `Sim.StableSort` on items with equal primary keys
  - **WHEN** sorted
  - **THEN** the output follows the `EntityId` or `opSeq` tie-break, with no allocation after warm-up.
- **SC-R28-3** [M-1a][Logic]
  - **GIVEN** two `SimWorld`s fed identical inputs in one process
  - **WHEN** they run in parallel
  - **THEN** their dumps are equal, and reflection finds no static mutable field in the Sim assembly.
- **SC-R28-4** [M-1b][Logic]
  - **GIVEN** a recording `IPhaseProbe` and the default null probe
  - **WHEN** 1 000 ticks run with each
  - **THEN** the dumps are identical, and the null probe allocates 0 B.
- **SC-DET-1** [M-1a][Logic]
  - **GIVEN** two `SimWorld`s with the same `ContentSet` and `runSeed`, and a recorded 50 000-op log
  - **WHEN** the log is applied one op per tick and the SHA-256 of the canonical dump is taken after every tick
  - **THEN** all hashes match (H-DET-1, R29 T1).
- **SC-DET-1b** [M-1a][Logic]
  - **GIVEN** the same setup with a different `runSeed`, and separately a replay whose header mismatches
  - **WHEN** they are run
  - **THEN** the hashes diverge, and the mismatched replay is refused before any world is built.
- **SC-DET-2** [M0][CI]
  - **GIVEN** the committed golden vectors (SC-F5-1) and PCM-P2-1's 600-tick script
  - **WHEN** they run on Windows and Linux x64 CI
  - **THEN** every output and hash matches exactly (R29 T2).
- **SC-R30-1** [M-1a][Logic]
  - **GIVEN** a dump
  - **WHEN** it is written
  - **THEN** it is little-endian, with sections in fixed order. It includes the tick, the id counters, the mail counter, `opSeq`, the RNG streams and the queued ops. It excludes perf counters and the log ring.
- **SC-R30-2** [M-1a][Logic]
  - **GIVEN** a dump requested from inside `Tick()` or the sink
  - **WHEN** it is called
  - **THEN** Debug asserts, and Release returns false and counts `dumpDuringTick`. A `Stopped` world's dump is valid.
- **SC-R31-1** [M-1a][CI]
  - **GIVEN** a Release build
  - **WHEN** the guard test runs
  - **THEN** `Sim.AssertsEnabled` is false. Debug and ReleaseAssert builds log it at start.
- **SC-R31-2** [M0][Logic]
  - **GIVEN** an always-on check forced to fail repeatedly
  - **WHEN** it fires
  - **THEN** `{code, tick, a, b}` records are written to the ring, rate-limited per code, with no throw and no allocation.
- **SC-R31-3** [M-1a][Logic]
  - **GIVEN** an exception escaping `Tick()`
  - **WHEN** it occurs
  - **THEN** Debug rethrows. In Release the adapter calls `Stop()`, the world is marked faulted, the dump stays readable, and no further tick runs.
- **SC-R32-1** [M-1a advisory → M0 BLOCKING][Perf]
  - **GIVEN** SC-FIX-WORST in Release, with JIT settings pinned and 300 warm-up ticks, in a non-parallel xUnit collection
  - **WHEN** `GC.GetAllocatedBytesForCurrentThread()` is sampled around 18 000 ticks
  - **THEN** the delta is 0 B and no collection has grown.
- **SC-ALLOC-1** [M0][Perf][BLOCKING]
  - **GIVEN** the headless harness on SC-FIX-WORST
  - **WHEN** 18 000 ticks run
  - **THEN** the `GC.CollectionCount(0)` delta is 0, and `GC.GetTotalMemory(true)` after the run is ≤ 64 MB.
- **SC-ALLOC-2** [M0][SPIKE]
  - **GIVEN** an exported Godot build
  - **WHEN** the allocation measurement is attempted
  - **THEN** the spike records whether it works. No pass is claimed.
- **SC-R33-1** [M-1a][CI]
  - **GIVEN** `PerformativeMail.Sim.csproj`
  - **WHEN** CI runs
  - **THEN** the project targets `net8.0` with Nullable and Deterministic on, references no `Godot*` assembly, and `grep "using Godot" src/Sim` finds nothing.
- **SC-R33-2** [M-1a][SPIKE]
  - **GIVEN** the Godot game project with a ProjectReference to the Sim
  - **WHEN** it builds with the 4.7.1 .NET SDK
  - **THEN** the spike records pass or fail. On fail, the shared-folder fallback with namespace enforcement applies.
- **SC-R33-3** [M0][CI]
  - **GIVEN** the CI matrix
  - **WHEN** it runs
  - **THEN** a Windows runner runs SC-DET-2 and the reflection dependency test.

### States

- **SC-ST-1** [M-1a][Logic]
  - **GIVEN** a world in each state
  - **WHEN** the transition table is exercised
  - **THEN** only Constructed → Ready → Running → Stopped is legal. `Tick()` on a Constructed or Stopped world, or re-entrantly, runs no phase, leaves `CurrentTick` unchanged, flushes nothing, and counts `badTick` (asserts in Debug).
- **SC-ST-2** [M-1a][Logic]
  - **GIVEN** `Stop()` called in phase 12 of tick T
  - **WHEN** the tick finishes
  - **THEN** it completes through `Events.Flush`, with no extra tick, then becomes `Stopped`. `Stop()` outside a tick transitions immediately, and a second `Stop()` is ignored and counted.
- **SC-ST-3** [M-1a][Logic]
  - **GIVEN** an `Idle` driver
  - **WHEN** `Advance` is called
  - **THEN** it returns 0, and a double `Pause` or double `Resume` is a no-op.

### Formulas

- **SC-F1-1** [M-1a][Logic]
  - **GIVEN** `contentTicks`
  - **WHEN** called with 400, 150, 15 000, 0, 1 and 100 ms
  - **THEN** it returns 12, 5, 450, 0, 1 and 3. Separately, `ticksToMsFloor(1)` = 33 and `ticksToMsCeil(1)` = 34.
- **SC-F1-2** [M-1a][Logic]
  - **GIVEN** the display clock
  - **WHEN** r = 360, 331, 330, 1, 0 and 2160
  - **THEN** it reads "0:12", "0:12", "0:11", "0:01", "0:00" and "1:12". A deadline already passed reads "0:00".
- **SC-F1-3** [M-1a][Logic]
  - **GIVEN** content values "0.1" s, "0.0001" s, a negative duration, 86 400 000 ms and 86 400 001 ms
  - **WHEN** they are loaded
  - **THEN** "0.1" loads as 3 ticks and 86 400 000 ms loads. The other three fail, and no world is built.
- **SC-F2-1** [M-1a][Logic]
  - **GIVEN** freq = 10^7 and acc30 = 0
  - **WHEN** a 150 ms Δ is applied
  - **THEN** `ticksRun` = 4, dropped = 0, acc30 = 5 000 000 and Alpha = 500 ‰.
- **SC-F2-2** [M-1a][Logic]
  - **GIVEN** acc30 = 5 000 000 carried from SC-F2-1
  - **WHEN** another 150 ms Δ is applied
  - **THEN** `ticksRun` = 4, dropped = 1, acc30 = 0 and Alpha = 0.
- **SC-F2-3** [M-1a][Logic]
  - **GIVEN** acc30 = 0
  - **WHEN** a 400 ms Δ is applied
  - **THEN** due = 12, `ticksRun` = 4, dropped = 8, one log line is written, and `droppedTicksTotal` = 8.
- **SC-F2-4** [M-1a][Logic]
  - **GIVEN** a steady Δ of 166 667 counts
  - **WHEN** 600 frames run
  - **THEN** ticks per frame alternate 0 and 1, nothing is dropped, and Alpha alternates about 500 ‰ and about 0.
- **SC-F2-5** [M-1a][Logic]
  - **GIVEN** a single 5 s Δ
  - **WHEN** it is applied
  - **THEN** it clamps to 1 s, `ticksRun` = 4, dropped = 26, and `clampedTimeTotal` grows by 4 s of counts.
- **SC-F2-6** [M-1a][Logic]
  - **GIVEN** a timestamp earlier than `last`
  - **WHEN** it is applied
  - **THEN** nothing is added, `last` does not move back, and the next forward step does not re-count the span.
- **SC-F2-7** [M-1a][Logic]
  - **GIVEN** a freq of 999 or 10^12 + 1
  - **WHEN** a driver is constructed
  - **THEN** construction throws. freq = 1 000 and freq = 10^12 succeed.
- **SC-F2-8** [M-1a][Logic]
  - **GIVEN** a seeded random Δ sequence, plus a run of 1 000 Δ of 125 ms
  - **WHEN** it runs
  - **THEN** `acc30` < freq after every `Advance`, `ticksRunTotal + droppedTicksTotal ≤ floor(Σ elapsed × 30)`, and the 125 ms run (8 fps) drops nothing.
- **SC-F2-9** [M0][Logic]
  - **GIVEN** `catchUpGuardMs` = 10 and a `GameLoop` test double whose tick 1 takes 12 ms
  - **WHEN** the frame runs
  - **THEN** exactly 1 tick runs, and `DropRemaining` counts the rest as dropped.
- **SC-F3-1** [M-1a][Logic]
  - **GIVEN** a synthetic array of N = 18 000 values
  - **WHEN** p99 is computed
  - **THEN** it equals `sorted[17819]`.
- **SC-PERF-1** [M-1b][Perf][ADVISORY until OQ6]
  - **GIVEN** SC-FIX-WORST in host mode, Release, 18 000 ticks after warm-up
  - **WHEN** `IPhaseProbe` measures `Tick()`
  - **THEN** median ≤ 3 000 µs, p99 ≤ 5 000 µs and max ≤ 8 000 µs.
- **SC-PERF-2** [M-1b][Perf][ADVISORY]
  - **GIVEN** SC-FIX-STEADY
  - **WHEN** it runs
  - **THEN** median ≤ 1.5 ms and p99 ≤ 3.0 ms.
- **SC-PERF-3** [M0][Perf][BLOCKING]
  - **GIVEN** `production/qa/perf-baseline.md` for the runner class
  - **WHEN** CI runs
  - **THEN** any phase or total p99 above 2× the baseline fails the build.
- **SC-PERF-4** [M-1b][Perf]
  - **GIVEN** a total p99 of 4.1 ms with `Automation.Step` at 1.35 ms
  - **WHEN** the report is built
  - **THEN** the tick gate passes and Automation is flagged to the technical-director.
- **SC-PERF-5** [M-1a][CI]
  - **GIVEN** the F3 table
  - **WHEN** a doc-lint sums it
  - **THEN** the p99 slices total 4.50, which with the 0.50 reserve gives 5.00. The ClientLayer split sums to 0.30, and the max column sums to the stated 12.1.
- **SC-F4-1** [M-1a][Logic]
  - **GIVEN** the id layout
  - **WHEN** player 1 and construct 1 are encoded, and 50 331 649 is decoded
  - **THEN** they encode to 16 777 217 and 50 331 649, and the decode gives class 3, counter 1.
- **SC-F4-2** [M-1a][Logic]
  - **GIVEN** a world constructed with `opSeq` at 2^32 − 1
  - **WHEN** `EnqueueOp` is called
  - **THEN** it returns false.
- **SC-F5-1** [M-1a][Logic]
  - **GIVEN** the golden vectors
  - **WHEN** the RNG and hash primitives run
  - **THEN** they produce:
    - `pcg32_srandom(42, 54)` → `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293`
    - `Mix64(0)` = `0xe220a8397b1dcdaf`
    - `Fnv1a64("mail")` = `0x1f5964a2ce98072e`
    - `Derive(1, "mail", 2)` = `0x120344c795c4b10e`
    - `Create(1, "mail", 2)` → `0x915ab962, 0xc3f6d39c, 0x5d7b280d`
- **SC-F5-2** [M-1a][Logic]
  - **GIVEN** a fixed seed and name
  - **WHEN** `Derive` is evaluated for 1 000 distinct indices
  - **THEN** all 1 000 results are distinct.
- **SC-F6-1** [M-1a][Logic]
  - **GIVEN** `NextInt(10)`
  - **WHEN** `next32` is 0x80000000, 0xFFFFFFFF and 0
  - **THEN** it returns 5, 9 and 0. The first draw of `Create(1, "mail", 2)` gives 5.
- **SC-F6-2** [M-1a][Logic]
  - **GIVEN** an `IRngSource` stub
  - **WHEN** `NextInt(b)` is called 10 000 times with varying b
  - **THEN** `draws` increases by exactly 1 per call.
- **SC-F7-1** [M-1a][Logic]
  - **GIVEN** `MulPermille`
  - **WHEN** called with (15, 1500), (−15, 1500) and an overflowing product
  - **THEN** it returns 23 and −23, and the overflow clamps to ±2^62 and counts `moneyClamped`.
- **SC-F7-2** [M-1b][Logic]
  - **GIVEN** `MulDiv`
  - **WHEN** called with (7, 1, 2), (−7, 1, 2) and a zero divisor
  - **THEN** it returns 3 and −3, and the zero divisor returns 0 with a count (Release). Nothing throws.
- **SC-F7-3** [M-1a][Logic]
  - **GIVEN** `Fx16.ToMm` and `Cents` arithmetic
  - **WHEN** `ToMm` is called with 98 304 and −98 304, and a `Cents` add crosses ±2^62
  - **THEN** `ToMm` returns 1 and −2. The `Cents` add throws in Debug (`checked`) and saturates in Release, counting `moneySaturated`.
- **SC-F8-1** [M-1a][Logic]
  - **GIVEN** 8 players × 60 ops/s × 1.0 s × 8
  - **WHEN** the capacity function runs
  - **THEN** it returns 4 096 for the op queue, and 4 096 for the event log (1.5 × 2 300). Each ring is 256 KiB.

### Edge cases

- **SC-EC-1** [M-1b][Integration]
  - **GIVEN** two lethal damage entries on one carrier in one tick
  - **WHEN** the tick runs
  - **THEN** exactly one `PlayerDied` and one `CarrierKnockedDown` are logged.
- **SC-EC-2** [M-1b][Logic]
  - **GIVEN** a `Combat.React` reaction that raises damage
  - **WHEN** it runs
  - **THEN** Debug asserts. Release applies the damage in phase 10 of T+1 and counts `lateDamage`.
- **SC-EC-3** [M-1b][Integration]
  - **GIVEN** `Run.Step` changing the run phase in tick T, and a build request queued in tick T
  - **WHEN** the request drains at T+1
  - **THEN** it is rejected `phase_closed`, with no rollback.
- **SC-EC-4** [M0][Integration]
  - **GIVEN** a host loopback op and a remote op in the same frame
  - **WHEN** the tick runs
  - **THEN** the remote op has the lower `opSeq`, because transport is pumped before loopback.
- **SC-EC-5** [M-1a][Logic]
  - **GIVEN** an actor's `PlayerLeave`, plus two of its ops, one with lower `opSeq` and one with higher
  - **WHEN** they drain
  - **THEN** the lower applies, the higher is rejected `unknown_actor`, and both are recorded.
- **SC-EC-6** [M0][Logic]
  - **GIVEN** ticks that each cost more than 33.3 ms
  - **WHEN** 300 frames run
  - **THEN** each frame runs exactly one tick and drops the surplus, and no frame runs more than one tick (no spiral).
- **SC-EC-7** [M-1a][Logic]
  - **GIVEN** a sink that keeps its span after `Deliver`
  - **WHEN** it reads from the span after the log is cleared, in Debug
  - **THEN** it reads 0xDD.
- **SC-EC-8** [M-1a][CI]
  - **GIVEN** a replay whose `ContentSet` hash, `SimSettings` or `runSeed` differs from its header
  - **WHEN** it is loaded
  - **THEN** it is refused.

### Cross-system

- **SC-X-1** [M-1b][Integration]
  - **GIVEN** a hand delivery, a thrown landing, a feeder offer and an NPC offer to one destination in the same tick
  - **WHEN** the tick runs
  - **THEN** outcomes resolve hand (phase 0), landing (4), feeder (5), NPC (6), identically across 100 seeded repeats (Mail R6). The NPC part is stubbed until Vehicles exists.
- **SC-X-2** [M-1b][Integration]
  - **GIVEN** a lethal hit on a carrier with an open container, in tick T
  - **WHEN** it resolves
  - **THEN** the log order is `PlayerDied`, container close, `Removed`/spill, `CarrierKnockedDown`, all in tick T.
- **SC-X-3** [M-1a][Integration]
  - **GIVEN** a Grid Inventory op that changes a container
  - **WHEN** the batch is delivered
  - **THEN** its events precede its `OpReply`.
- **SC-X-4** [M-1a][Integration]
  - **GIVEN** a throw op
  - **WHEN** it applies
  - **THEN** the batch reads `Removed`, `ThrowLaunched`, then the ok reply.
- **SC-X-5** [M-1a][Integration]
  - **GIVEN** a Manual Delivery request from the host's own client
  - **WHEN** it applies
  - **THEN** its reply reaches `ClientLayer` on the same tick or the next, with no frame-delayed queue.
- **SC-X-6** [M-1a][Integration]
  - **GIVEN** a solo pause of 10 s, with Grid Inventory D6 and Input H-R2-2 timers armed
  - **WHEN** play resumes
  - **THEN** every absolute-tick timer expires after the same number of ticks as without the pause.
- **SC-X-7** [M0][Integration]
  - **GIVEN** the nightly TC-X-1 / H-R19-1 soak (8 players, 18 000 ticks, seeded, ReleaseAssert)
  - **WHEN** it runs
  - **THEN** no assertion or always-on check fails.

### 3.18 Original Open Questions engine table, hand-backs HB1–HB10 and ADR list

*(verbatim, GDD lines 1703–1772 as reviewed)*

## Open Questions

### Engine-dependent decisions (UNVERIFIED; Technical Setup spike + ADR, owner technical-director, before M-1a code)

No `docs/engine-reference/godot/modules/core.md` exists, so feasibility was **not checked against the pinned engine** for the Core domain. Every default below is a recommendation that the spike must confirm or overturn.

| # | Question | Recommended default | What the spike measures |
|---|---|---|---|
| OQ1 | Tick driver (resolves Grid Inventory OQ20, Player Character OQ6) | Godot-free `TickDriver` with an integer accumulator, `Advance(timestamp)` from one `GameLoop` node's `_Process`, `Stopwatch` timestamps, cap of 4, drop-not-bank, `ProcessMode = Always` defensively (R17) | Ticks per frame at 60/144 Hz and with vsync off; `_Process` during window drag/minimise; `delta` scaling vs `Stopwatch`; hitch injection at 50/150/500 ms; `_Input` arriving before `_Process` (edge latching); behaviour under accidental `SceneTree.Paused` |
| OQ2 | Sim thread and transport callback thread (shared with Grid Inventory OQ12) | Main thread; callbacks only enqueue; move triggers in R18 | Which thread RPCs arrive on, and whether inside the frame; loopback latency in ticks; reliable ordering across RPC methods on one channel; Steam peer ordering |
| OQ3 | RNG and hash function ADR (answers Mail md:847) | PCG32 XSH-RR, `Derive` = nested SplitMix64 over FNV-1a-64 (F5), multiply-shift `NextInt` (F6), SHA-256 for tests, `CanonicalHash64` deferred | None. Commit the F5 golden vectors (SC-DET-2). |
| OQ4 | Sim assembly shape (DEV-7) | `net8.0` class library, ProjectReference, glob exclusion, shared-folder fallback (R33) | ProjectReference with the 4.7.1 .NET SDK; default compile globs and `obj/` leakage; `.gdignore` need; export configuration → Sim symbols; tiered JIT/PGO in export; `GC.GetAllocatedBytesForCurrentThread` in an exported build; the .NET SDK minimum for 4.6/4.7 (NOT SOURCEABLE today) |

### Design questions

| # | Question | Owner | Target |
|---|---|---|---|
| OQ5 | Host-mode budget numbers (5 / 8 / 3 ms, 0.50 reserve, per-phase split) need sign-off, and the 10 ms render figure must say whether it includes engine network polling and per-peer encoding. Nothing budgets those yet. | technical-director + performance-analyst | Before M0 |
| OQ6 | No reference machine exists for absolute ms gates (also HUD HN-19, Input OQ-IC-17). Until it does, absolute gates are advisory and the relative 2 × baseline gate applies. | technical-director / producer | M1 |
| OQ7 | Spec 07 amendments DEV-1…DEV-8 should be folded into spec 07 or an ADR so the spec stops contradicting this GDD. | technical-director | With OQ1–OQ4 ADRs |
| OQ8 | Systems-index edges: Manual Delivery, Throwing, Mail, Input and HUD rows do not list Simulation Core in "Depends On". The phase table also makes Health & Damage → Player Character a reaction edge (phase 11). | producer (`/map-systems` update) | Next index pass |
| OQ9 | SC-FIX-WORST counts (megas, constructs, feeders, trucks) are placeholders. | Enemy AI & Waves, Belt Logistics, Vehicles | When those GDDs are written |
| OQ10 | Should the float ban cover all of the Sim assembly, or only state paths? R28 says state and ordering paths. An analyzer cannot tell state from diagnostics, so the M-1a analyzer bans floats assembly-wide, with no allowlist. Revisit only if a non-state use is truly needed. | lead-programmer | M-1a |
| OQ11 | Free Play save/load (`Loading` state, constructing from a dump) and dedicated-server budgets are deferred. | technical-director | Post-v1 |
| OQ12 | The playtest protocol for the Player Fantasy tests (90 % agreement, host/client dispute parity) | qa-lead | M0 |
| OQ13 | Scope of the Provisional re-validation after the greybox (CD-GDD-ALIGN note). Only the player-facing claims depend on the greybox: Player Fantasy, Game Feel, R16 pause. The determinism contracts do not. The header line is kept verbatim as the caller specified. | creative-director | After the M-1a two-friend greybox |

### Hand-backs to other GDDs (not edited here; the owning GDD applies them)

- **HB1 — Grid Inventory.**
  - (a) R7 and the edge case at gi:360: replace "`Inputs.Apply` drains both in arrival order" with "the player op (phase 0) always runs before an inserter's `TryInsert` (phase 5) in the same tick. An inserter that loses retries next tick." R9 here.
  - (b) All `AutoPlace`, `Spawn`, `TryInsert`, `TakeUnit`, `TryCatch` and `Grant` APIs are callable from any phase except `Events.Flush` and tick end. Each call is atomic and bumps `version`.
  - (c) The R19 conservation check runs in the tick-end hook.
  - (d) `worldItemCap` is enforced only in phase 1. Items created later in the tick may overshoot it until the next tick.
  - (e) The test project path becomes `tests/unit/PerformativeMail.Sim.Tests/Inventory/`.
  - (f) The op ordering key is Sim Core's `opSeq`. Grid Inventory's `arrivalSeq` stays the Intake per-entry counter.
- **HB2 — Grid Inventory perf.** H-PERF-1/3 shrinks to ≤ 0.5 ms p99 / 1.5 ms max (steady) and ≤ 1.0 / 2.5 ms (worst), measured as `ProbeScope` sub-scopes. Belt endpoint calls move to Automation's 1.20 ms slice, with ≤ 0.25 ms per 400 calls. H-PERF-2 sort becomes ≤ 0.3 ms p99 / 0.8 ms max. (F3)
- **HB3 — Throwing.** Throwing has no randomness (registry `throwNoDice`), so no stream is registered for it.
  - Its Detailed Design still mentions spin and spread seeded by `hash(runSeed, flightId)` (tc:213, 936). Remove those mentions, or make the spin seed a pure cosmetic derivation, consistent with `throwNoDice`.
  - If randomness ever returns, it must be a registered stream (R23) indexed by a per-run u32 launch ordinal. It must never be `flightId`, which wraps at u16.
- **HB4 — Mail & Destinations.**
  - Stream names become `mail` and `mail.special`, indexed by shift through F5.
  - M-PERF-2 splits by phase: Mail.Step 0.25 / 1.0 ms. Hand ops count in phase 0, feeders in phase 5, NPC drops in phase 6.
  - Wallet debits are synchronous `IEconomyApi` calls (phase 9 only flushes).
  - "Arrival order" means `opSeq`.
- **HB5 — Player Character.**
  - P15 runs in phase 11 `Combat.React` as a cursor reader. The host-role `Players.Step` is phase 3.
  - `Teleport` is an `IPlayersApi` call, `TeleportAck` is an `OpKind`, and samples are stamped with `receivedTick = CurrentTick`.
  - PCM-PERF-1 becomes host 0.20 ms plus `ClientLayer` `CarrierStep` 0.10 ms.
  - `IRemoteCapsuleSource` on the host returns the T−1 ingest.
- **HB6 — Throwing.**
  - TC-P-2 becomes 0.15 ms p99 / 0.5 ms max.
  - Confirm `ThrowRelease` carries no tick, and reconcile this with Input & Controls R11's `clientTick`. Any tick kept must be in the host tick domain (N4).
  - Catches and landings are synchronous calls in phase 4 (R9).
- **HB7 — Health & Damage** (not yet written).
  - Raise **one** `PlayerDied` per carrier per tick, from the first lethal pending entry, with killer credit to that entry.
  - Reactions to deaths happen in phase 11, not by calling up into Player Character.
- **HB8 — Manual Delivery.** Add Simulation Core (Hard) to its Dependencies table: ops, `IHostClock`, loopback timing.
- **HB9 — HUD & Notifications.** Add Simulation Core (Soft, Presentation-only) for the F1 display rule and `IHostClock` semantics. Its `renderMs` must come from `IHostClock` (Networking), never from a HUD-only clock.
- **HB10 — Networking & Session** (not yet written). Reconnect and retry paths must never resend an op the host may already have drained. Sim Core does not deduplicate `reqId` (Edge Cases, degenerate cases), so a resent op would apply twice (CD-GDD-ALIGN C4).

### ADRs to write (lead-programmer list)

1. **Sim assembly shape:** `net8.0`, ProjectReference and globs, fallback, CI enforcement (OQ4).
2. **Tick driver and thread:** accumulator, cap, drop policy, solo pause, main thread, move triggers (OQ1, OQ2).
3. **Phase order, op queue and cross-system call rule:** R3, R4, R6–R10, DEV-1…DEV-3, DEV-6, DEV-8.
4. **Event bus semantics:** single log, no synchronous subscribers, replication classes failing closed, coalescing, overflow → resync, reply after events (R11–R14).
5. **Id scheme:** `EntityId` classes, `mailId`, `PlayerSlot`, system-local ids, serial helpers (R19–R22).
6. **RNG and hash functions** (OQ3).
7. **Numeric types and determinism tiers:** R27–R29 and the banned-API list (control-manifest lift with R31, R32).
