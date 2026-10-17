# ADR-0011: Network Adapter Boundary (Sim ↔ Transport Seam)

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist`. TD-ADR skipped (lean mode). Inputs: simulation-core.md N1–N6 and R6–R14, grid-inventory.md Network, content-data-definitions.md Network, architecture.md §Network adapter boundary, spec 06 §1–§2, spec 13 §3.

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
The Sim must run identically in solo, as a listen-server host and (later) headless, while Godot 4.7.1's multiplayer behaviour is NOT SOURCEABLE until the M0 spike. This ADR fixes the seam now — what the Sim exposes (op ingress `EnqueueOp`, the sample ring, one-shot event/reply egress `IEventSink.Deliver`, between-tick `ISnapshotSource` reads, `IHostClock`), what it must never know, and a transport-agnostic adapter core in plain C# that owns peers, actor stamping, the per-descriptor codec, `reqId` dedupe with last-16 reply replay, per-peer packing/pacing and the content-hash handshake — with the host's own client going through the same adapter over an in-process loopback transport. The rule "never resend an op the host may have applied" is part of the seam. The concrete transport (SceneMultiplayer/ENet vs raw peers, RPC vs bytes, channel map, auth callbacks, Steam) is explicitly **not** decided here: it is deferred to the M0 multiplayer spike and its own ADR.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Networking (seam only; no Godot networking API is chosen here) |
| **Knowledge Risk** | HIGH for the domain (4.4–4.7 multiplayer behaviour NOT SOURCEABLE); LOW for this ADR's decided content, which is plain C# and makes **no** assumption about `MultiplayerAPI`, `SceneMultiplayer`, `ENetMultiplayerPeer` or RPC semantics. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md` ("NOT SOURCEABLE — high-level multiplayer API behaviour"), `breaking-changes.md` (4.5 `Node.get_rpc_config` → `get_node_rpc_config`; 4.6 `StreamPeerTCP`/`TCPServer` members moved to `StreamPeerSocket`/`SocketServer`), `deprecated-apis.md`, `current-best-practices.md` |
| **Post-Cutoff APIs Used** | None. The deferred transport must avoid the renamed members above. |
| **Verification Required** | All by the **M0 multiplayer spike** (Grid OQ12, SC OQ2, CDD S5, Q-ARCH-05), before the transport ADR: reliable-ordered delivery of one peer's messages on one channel **across message types**; channel count and mapping; payloads > 64 KB / fragmentation; RPC vs raw-bytes (`SendBytes`-style) serialisation and never decoding untrusted bytes with `GD.BytesToVar`; callback thread and inbound polling vs the tick; peer-id ranges and stability; disconnect timeout; authentication hook for `Hello`/`HelloAck`; whether loopback can bypass `MultiplayerAPI`; Steam peer ordering (ADR-0023). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (`SimWorld` lifecycle and the bind tables: `BindOp`, `BindEventKind`, `BindSnapshotSource`; `FrameRunner` ownership of the frame); ADR-0004 (`PlayerSlot`, `EntityId`/`ActorId`, `System` actor, temp-id namespace, `Serial*` helpers); ADR-0007 (64 B `Op` with u32 `reqId` header slot, `EnqueueOp` → false = `busy`, reply layout, no Sim dedupe); ADR-0008 (`IEventSink.Deliver` copy-only contract, replication classes, raise-time `audienceMask`, `overflowInfo`, by-reference payloads) |
| **Enables** | ADR-0009 (ordering contract and loopback for inventory ops); ADR-0010 (handshake carries the content hash); ADR-0016 (ClientLayer inbox/`MirrorDrain`); ADR-0020, 0021, 0022, 0023 (transform, flight, JoinState, Steam — all built on this seam); the M0 transport ADR |
| **Blocks** | Networking & Session epic; any story that sends a request from Presentation to the Sim (M-1a uses the loopback transport) |
| **Ordering Note** | The seam and the loopback transport are needed at M-1a (solo play is a one-player co-op session through loopback). The real transport, channel map, auth and Steam are M0 and need a new ADR after the spike; that ADR must conform to §5's transport contract or amend this one. |

## Context

### Problem Statement
Every system GDD has a Network section that assumes things about delivery order, peers, ticks and duplicates, and the Sim Core GDD forbids the Sim from knowing any of them (TR-sim-042). Without a fixed seam, each system would leak transport assumptions into the Sim, the host's own client would take a shortcut that remote clients cannot (spec 06 §1: "the listen server does not trust the host's client any more than others"), and duplicated or resent requests would double-apply. The transport itself cannot be chosen yet: Godot 4.7.1 multiplayer is unverified and the Steam path is open (Q-ARCH-09). M-1a still needs a working request/event path for solo.

### Constraints
- The Sim knows no peers, accounts, channels, RPC names, chunking, pacing, wall clock, RTT, per-peer filtering, packing or Godot types (SC N1, R33).
- Sim main-thread only; transport callbacks may only enqueue (SC R7, R18).
- `IEventSink.Deliver` is called once per tick in phase 13, is copy-only, and resolves `audienceMask` to peers inside the call (SC R12).
- Sim Core has no generic `reqId` dedupe; `reqId` is opaque in a u32 header slot (SC R6, R10, ADR-0007).
- Friends-only trust (spec 13 §4.3): consistency, not anti-cheat; `actor` must still be stamped from the transport peer.
- Inventory requires reliable, ordered delivery of one peer's events and replies on one channel (Grid Network; ADR-0009).

### Requirements
- TR-sim-042/043; TR-inventory-020/021; TR-input-013; TR-delivery-008…010, 016; TR-mail-014, 016, 017, 021, 023; TR-comms-001…006 (as seam requirements; their transport specifics are deferred).
- Simulation Core HB10: Networking never resends an op the host may have applied.

## Decision

### 1. What the Sim exposes (the only crossing points)

| Direction | Port | When | Contract |
|---|---|---|---|
| in | `SimWorld.EnqueueOp(in Op)` → `bool` | Between ticks, Sim thread | `Op.Actor` is stamped by the adapter from the transport peer; the payload is never trusted for identity. `false` (ring full / player limit) → the adapter answers `busy` on that peer's reply stream immediately. |
| in | Transform sample ring | Between ticks, Sim thread | Drained and stamped `receivedTick` at the next tick boundary (SC R5); never stamped in a callback. |
| out | `IEventSink.Deliver(tick, ReadOnlySpan<EventEntry>, OverflowInfo)` | Once per tick, phase 13 | Copy-only into adapter-owned preallocated buffers; resolve each entry's `audienceMask` to the **current peer set** before returning; no transport, Godot or Sim call inside. Chunked runs (`Continuation`) are copied with their head; by-reference entries are resolved after `Tick()` through `ISnapshotSource` (ADR-0008 §1.3). |
| out | `ISnapshotSource.Write(in SnapshotRequest, ref SnapshotWriter)` | Between ticks, after `Deliver` | Read-only. Two uses: continuous blocks (transforms, vehicles, enemies, HP — bound per system) and **keyed snapshots** (`kind`, `key`, `forSlot`) such as one container for subscribe/open/resync (ADR-0009) and the JoinState blocks (envelope: ADR-0022). Every snapshot is stamped with the tick it describes and the owner's version. |
| both | `IHostClock { uint EstimatedHostTick; bool IsValid; }` | Read by `ClientLayer`/Presentation | Interface defined by the Sim. Host implementation returns `CurrentTick` (frozen in a solo pause); the client estimator is Networking's (≤ 3 ticks p95 at 150 ms RTT). Every tick a client puts in a request is in this host domain (HB6). |

**The Sim must not know:** peers, accounts, connections, channels, RPC names, chunking, pacing, wall clock, RTT, per-peer filtering, packing, or any Godot type. `PlayerSlot` (0–7) is the only player-addressing concept that crosses the seam, and the Sim keeps the `slot ↔ playerId` table (SC R21).

### 2. The adapter core is plain C#, transport-agnostic

The adapter is split in two:

- **`NetAdapter` core** — Godot-free, in `PerformativeMail.Client.Core/Net/` (no new assembly; ADR-0001's layout), used by both roles:
  - **`HostAdapter`**: peer table `peer → (accountKey, playerId, slot)`; actor stamping; the content-hash handshake gate (§4); op decode → `EnqueueOp`; `busy` and codec-level `bad_request` replies; the `reqId` dedupe cache (§3); the `IEventSink` implementation (copy, audience → peers); per-peer outbound queues with packing (`ContainerDelta`, `GroupCounts`, `UnitsRemoved` coalescing per GDD rules), burst pacing and the high-water valve (resync-pending); overflow-driven resync of exactly `droppedAudienceMask` × `droppedKindsMask`; snapshot requests via `ISnapshotSource`; join/rejoin sequencing (`joinTick`, buffering rule, SC N3.5).
  - **`ClientAdapter`**: request encode with per-connection `reqId` (u16, wrapping); inbound decode into the `ClientLayer` inbox; `IHostClock` client estimator (Networking-owned algorithm).
- **`ITransport`** — the only transport-specific piece, implemented outside the core:
  - `LoopbackTransport` (plain C#, in Client.Core): an in-process pair of byte queues; used for solo and for the host's own client.
  - The real transport (Godot multiplayer / Steam) — **deferred** (§5).

```csharp
namespace PerformativeMail.Client.Core.Net;

public interface ITransport
{
    // Inbound: the transport only enqueues; FrameRunner calls Poll on the main thread.
    void Poll(ITransportSink sink);                       // delivers queued (peer, channel, bytes) in arrival order
    void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes);
    void Disconnect(PeerHandle peer, DisconnectReason reason);
}
public interface ITransportSink
{
    void OnConnected(PeerHandle peer); void OnDisconnected(PeerHandle peer);
    void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes);
}
public enum NetChannel : byte { Reliable = 1, Unreliable = 0, Bulk = 2, Comms = 3 } // logical; mapping to real channels is the transport ADR's
```

**Loopback is the remote path.** The host's own `ClientLayer` uses `ClientAdapter` over `LoopbackTransport` to its own `HostAdapter`: the same codec, the same `reqId` and dedupe, the same per-peer packing and the same reply/event order as a remote peer. The host's slot has a peer handle like any other; the only difference is zero latency (accepted host advantage, Grid Network §Host). No code path checks "is this the host's client".

**Frame order (with ADR-0002/0003):** `FrameRunner` per frame: (1) `transport.Poll` (remote inbound), (2) `loopback.Poll` (host inbound), (3) run 0..N ticks, after each tick sending that tick's per-peer batches, (4) loopback hands batches to the host `ClientLayer` inbox for its next client tick. A remote op and a host op arriving in the same frame: the remote one gets the lower `opSeq` (documented host-side tie-break bias, SC edge case).

### 3. Requests: codec, actor, `reqId`, and never resending an applied op

- **Codec per descriptor (ADR-0007 §4).** Each Sim `OpDescriptor` (and each event kind descriptor, ADR-0008) declares only a **wire codec id and wire size**; the codec implementations (pure `TryDecode`/`Encode` over spans) live in the adapter core's codec registry keyed by that id. The Sim contains no codec and never encodes or decodes; the hash of the Sim's descriptor table is the wire protocol check (Sim Core N4). Decoding zero-initialises the whole `Op`, sets `PayloadLength`, and zero-extends the u16 `reqId` into the u32 header slot (ADR-0007 §1, §6).
- **Envelope (reliable request):** `kind u16 · reqId u16 · payload`. `TryDecode` failure, an unknown kind, or trailing bytes → the adapter replies Sim Core reason `bad_request` (8) itself and enqueues nothing (no `opSeq`, not recorded, like `busy`); a fuzz test feeds random bytes and structurally valid out-of-range payloads and asserts every input yields `bad_request` or a valid `Op` with no exception (H-NET-5).
- **Actor.** The adapter sets `Op.Actor` from the peer table; any player id inside a payload is ignored. Unknown peers' messages before admission are dropped.
- **`reqId`** is u16 per connection on the wire, zero-extended into the Op header's u32 slot; the Sim echoes it and never interprets it (ADR-0007).
- **Dedupe lives here, never in the Sim — ADR-0007 §6 adopted verbatim** (resolves Q-ARCH-04 / Grid rule 8 vs SC R10's handler reading). `HostAdapter` keeps, per connection, keyed `(peer, reqId u16)`: a **reply cache of the last 16 replies** (encoded reply bytes copied from `Deliver`) and an **in-flight set** of enqueued `reqId`s whose reply has not yet been delivered. On a request: in the reply cache → resend the cached **original reply**, enqueue nothing (no `opSeq`); in the in-flight set → drop silently (the original reply follows); otherwise → decode, `EnqueueOp`, add to the in-flight set; on `false` reply `busy` and do **not** cache it. A `reqId` outside both is a new op (an op naming a gone entry then fails `stale_entry` on its own merits). Cache, in-flight set and expected counter reset on rejoin. ADR-0007's "Godot-side network adapter" is realised as this Godot-free adapter core (§2): "Godot-side" means "outside the Sim", not "inside a Godot assembly".
- **Never resend an op the host may have applied (HB10).** `ClientAdapter` sends each request **exactly once** on the reliable channel and never retries at the application level. The single exception is a request answered **`busy`**: it never entered the Sim, so the client may send it again as a **new attempt** (ADR-0007 §6). A missing reply after `pendingReplyTimeoutSeconds` (5 s) triggers a **state resync**, not a resend; the resync snapshot carries `ackedReqId` so the client can retire what was applied. On disconnect, requests in flight are abandoned with the connection; after rejoin the client starts a fresh `reqId` space from the `RejoinState`. The dedupe cache is a defence against transport duplication only.
- **Request ticks** (e.g. Manual Delivery's `holdStartTick`) are in the host tick domain from `IHostClock`; the owning handler clamps them (MD5/MD6).

### 4. Admission and the content-hash handshake

- A connecting peer is **not admitted** — no `PlayerJoin` op, no events, no `DefIndex`-bearing message in either direction — until `Hello{gameVersion, contentHash[32], profileId, displayName, kit}` is answered `HelloAck{ok}`. Refusal precedence: `ContentLoadFailed → VersionMismatch → ContentMismatch → RunInProgress` (CDD Network 3; hash from ADR-0010). On `ok`, `HostAdapter` maps the peer to an account (`profileId`), asks Networking's roster for the `playerId` (existing for a rejoin), and enqueues the `System` `PlayerJoin` op.
- **Join/rejoin ordering (SC N3.5):** `joinTick` = the tick whose phase 0 applied `PlayerJoin`; the JoinState snapshot is taken after that tick's `Deliver`; the joiner buffers every batch with `tick > joinTick` until JoinState has applied and drops batches with `tick ≤ joinTick`. A rejoin discards the old mirror, pending queue and `reqId` counter. The joiner may send no container op until the last JoinState chunk is applied (Grid Network).
- **Disconnect:** `HostAdapter` enqueues the `System` `FreezePlayer` op; the peer handle is retired and its queued outbound batches are dropped (a queued batch holds peers, never slots, so a reused slot never receives a leaver's events — SC R12).

### 5. Transport contract (what the deferred transport ADR must provide)

The M0 spike picks the transport; whatever it picks must satisfy, or this ADR is amended:
1. **Reliable, ordered delivery per logical channel, across message types**, for `NetChannel.Reliable` (requests, events, replies, keyed snapshots) — one peer's messages arrive in send order. If the verified API cannot provide this on one channel, the adapter adds a per-peer sequence number and reorder buffer (and Grid's version-gap resync becomes the normal path).
2. A **bulk** reliable path for JoinState chunks (≤ 64 KB each, spec 06 §5), an **unreliable** path for transform samples, and a **comms** path (dedicated channel 3 if the channel count allows — TR-comms-001; otherwise multiplexed on Reliable with its own sequence and high-water).
3. Inbound callbacks only enqueue; delivery to the core happens in `Poll` on the main thread (SC R18).
4. Stable peer handles for the life of a connection; a connect/disconnect notification; fragmentation of large messages below the adapter.
5. Raw bytes in and out; untrusted bytes are never decoded by `GD.BytesToVar` or any Variant deserialiser.
6. **No assumption beyond what every supported peer type provides** (ENet and a Steam `MultiplayerPeer` may differ in ordering, channel count and packet size — ADR-0023).
7. **Fragmentation ownership:** the adapter chunks JoinState/RejoinState and any keyed snapshot to ≤ 64 KB messages itself; the transport must deliver a ≤ 64 KB message intact (its own fragmentation below that size is allowed). The spike confirms the transport's maximum message size.

**M0 spike checklist (from the engine review; every item UNVERIFIED for 4.7.1):** (a) whether `MultiplayerPeer`/`SceneMultiplayer` poll automatically per frame or must be driven by `FrameRunner` step 1, and on which thread packets/callbacks arrive; (b) whether a send between polls arrives in the same or the next frame; (c) whether the channel count is fixed at peer creation and whether `SceneMultiplayer` reserves low channels for its own traffic (clash with the logical map 0–3); (d) maximum packet size and who fragments; (e) peer-id conventions (server id 1, large non-contiguous client ids — `PeerHandle` assumes neither); (f) sender identity for raw packets (`GetPacketPeer` after `GetPacket`; `GetRemoteSenderId` is RPC-only) — actor stamping uses it, never the payload; (g) raw-packet delivery path under `SceneMultiplayer` (signal vs poll, root-path needs) and what its authentication callback offers for `Hello`/`HelloAck`; (h) a host running a server peer while solo stays offline with no dummy peer; (i) disconnect detection delay and ordering of the last messages before `OnDisconnected`; (j) whether `GetPacket` allocates a fresh `byte[]` per packet (GC pressure against the per-frame budget); (k) the renames in `deprecated-apis.md` re-checked by the transport ADR.

### 6. Per-peer outbound policy (mechanism decided here; numbers are Networking's knobs)

- One outbound queue per peer with byte accounting. Inside `Deliver` the adapter only does what ADR-0008 §7 (a)–(d) allows: copy the needed entries, resolve audiences to peers, update the per-connection reply cache from `OpReply` entries (ADR-0007 §6), and tag same-`CallSeq` entries bound for the actor's peer with the reply's `reqId`. After `Tick()` returns, each peer's entries for the tick are encoded into **one ordered batch message in log order** (filtered by audience; `Internal` never leaves). `ContainerDelta`, `GroupCounts` and `UnitsRemoved` are compact encodings of **consecutive** entries within that order, never a reordering (SC N3.1; ADR-0008 §7). Chunked runs (`Continuation`) are reassembled in order. A by-reference entry (Grid `InvReplace`) is followed in place by the keyed snapshot pulled through `ISnapshotSource` between ticks.
- **Merging across calls** (Grid's "merge one container's replaces of a tick at flush") is done **only** under ADR-0008 §7 (i) same `Coalescible` kind and subject, (ii) at the earliest position, (iii) no other entry about that subject in between. It is off until H-NET-1 and the version-gap tests prove it; until then entries are sent unmerged.
- **Bursts** (open, `replace`, resync, mid-Delivery RejoinState) are paced (`inventoryBurstBytes`, ≤ 800 B/tick PROPOSED) behind the sustained stream.
- **High-water valve:** if a peer's unsent queue *excluding paced snapshot bytes* exceeds the high-water (`inventoryHighWaterBytes`, 24 KB PROPOSED), the adapter discards that peer's queued deltas — **never replies** — for the container with the most queued bytes and marks it resync-pending for that peer; a fresh keyed snapshot follows as a burst.
- **Overflow:** a batch whose `overflowInfo.overflow` is set triggers a resync of exactly `droppedAudienceMask`, scoped by `droppedKindsMask`, with Networking's cooldown.
- Comms (frozen draft): host-originated relay; sender slot from the peer, never the payload; shape-only validation; drop not kick; its own high-water (drop-oldest). Messages are Sim `OpKind`s/event kinds like any other.

> **As implemented (2026-10-16, network-adapter-loopback story-004):** a batch is one Reliable message `tag u8 = 0x02 · tick u32 · count u16 · record[count]`, little-endian, records in log order; each record is `kindField u16 · payload`, where `kindField` bits 0–14 are the `EventKind` and bit 15 marks a `Continuation` chunk, and the payload is the kind's `IEventCodec` output (`EventCodecRegistry`, keyed by `WireCodecId`; a generic `FixedLengthEventCodec` for kinds whose wire form equals their payload). An `OpReply` record's payload is the `ReplyCodec` body (`reqId u16 · kind u16 · reason u16 · extraLen u8 · extra`); a bare adapter reply (`busy`/`bad_request`) is tag `0x01` plus the same body. Batches are stamped with the tick their entries were delivered in; one peer's records for a tick that exceed `AdapterSettings.MaxBatchBytes` (default 64 KB) go out as several batches of the same tick, in order. Join replies (`reqId ≥ 0x1_0000`) complete the join inside `Deliver` and are never encoded.

### Architecture Diagram

```
  Presentation / ClientLayer (host or remote)                 Host process
  ───────────────────────────────────────────                 ─────────────────────────────────────────────────────
  ClientAdapter ── encode(kind,reqId,payload) ──► ITransport ──► HostAdapter ── Hello/HelloAck gate (content hash)
     ▲  IHostClock (estimator)                    │ (Loopback │      │ peer→slot, stamp Actor, TryDecode, dedupe(16)
     │                                            │  or M0    │      ▼
     │                                            │  spike)   │   SimWorld.EnqueueOp ──► Tick() phases 0..12 ──► tick end
     │                                            │           │                                                 │
     │◄── decode batches ◄────────────────────────┘◄──────────┤◄── per-peer queues (pack · pace · high-water) ◄─ IEventSink.Deliver (phase 13, copy-only)
     │                                                        │◄── keyed snapshots / JoinState ◄── ISnapshotSource (between ticks)
  MirrorDrain (next client tick)
```

### Key Interfaces

```csharp
// Sim side (defined by ADR-0003/0007/0008; listed here as the seam)
public sealed class SimWorld { public bool EnqueueOp(in Op op); public void Tick(); public uint CurrentTick { get; } }
public interface IEventSink { void Deliver(uint tick, ReadOnlySpan<EventEntry> events, in OverflowInfo overflow); }
public interface ISnapshotSource { SnapshotKind Kind { get; } void Write(in SnapshotRequest req, ref SnapshotWriter w); }
public interface IHostClock { uint EstimatedHostTick { get; } bool IsValid { get; } }
// Adapter core (Client.Core/Net, Godot-free); codecs registered by the descriptor's WireCodecId (ADR-0007 §4)
public interface IOpCodec   { bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst); int Encode(in OpPayload src, Span<byte> dst); }
public interface IEventCodec{ int Encode(ReadOnlySpan<EventEntry> entryAndContinuations, Span<byte> dst); }
public sealed class HostAdapter : IEventSink { public HostAdapter(SimWorld sim, ITransport t, AdapterSettings s, IRoster roster); public void PumpInbound(); public void FlushOutbound(uint tick); }
public sealed class ClientAdapter { public ushort Send(OpKind kind, ReadOnlySpan<byte> payload); public void PumpInbound(IClientInbox inbox); }
public sealed class LoopbackTransport : ITransport { public static (LoopbackTransport host, LoopbackTransport client) CreatePair(); }
```

### Implementation Guidelines
- The Sim assembly **must never** reference peers, connections, channels, RPC names or Godot types; an architecture test enforces that `PerformativeMail.Sim` has no reference to `Client.Core` or `GodotSharp`.
- `Op.Actor` **must** be set only by `HostAdapter` from its peer table; payload fields **must never** be used for identity.
- The host's own client **must** use `ClientAdapter` over `LoopbackTransport`; no "is host" shortcut **must** exist on any request or event path.
- `ClientAdapter` **must never** resend a request except one answered `busy`; a reply timeout **must** trigger a resync, not a retry.
- `reqId` dedupe **must** live only in `HostAdapter` (ADR-0007 §6: last-16 reply cache + in-flight set per connection, reset on rejoin); the Sim **must never** dedupe.
- Codec implementations **must** live in the adapter core, keyed by `WireCodecId`; the Sim assembly **must never** contain a wire codec.
- `IEventSink.Deliver` **must** only copy and resolve audiences; it **must never** call the transport, Godot or the Sim.
- The adapter **must never** reorder two entries delivered to the same peer, and the high-water valve **must never** discard a reply.
- Untrusted bytes **must** be decoded only by the descriptor codecs; Variant deserialisation of network input **must never** be used.
- Transport callbacks **must** only enqueue; all processing happens in `Poll` on the main thread.

## Alternatives Considered

### Alternative 1: Godot high-level RPCs straight into the Sim (spec 13 §3 literal)
- **Description**: One `[Rpc]` method per request with Variant arguments, calling Sim methods; `MultiplayerSynchronizer` for state.
- **Pros**: Least code; spec 13 §3 chose it to delete the `Sim/Net` serialisation layer.
- **Cons**: Puts Godot types and peer ids into request handling; Variant decoding of untrusted input; cross-method ordering is NOT SOURCEABLE; the host's own calls would bypass the network path; no fuzzable decoder (H-NET-5), no canonical payload sizes (Grid/MD byte budgets), no plain-C# tests of ordering and dedupe.
- **Rejection Reason**: The GDDs written since spec 13 (Grid Network, Sim Core N1–N3, CDD Network) require exactly the properties this loses. RPCs may still be the **transport** under `ITransport` (decided by the spike) carrying opaque byte payloads.

### Alternative 2: Host's client calls the Sim directly (no loopback)
- **Description**: In-process client enqueues ops and reads events without encoding.
- **Pros**: Zero overhead.
- **Cons**: Two code paths; bugs in the codec, dedupe, packing or ordering appear only for remote players; violates spec 06 §1's "same validation path".
- **Rejection Reason**: Solo and host play would stop testing the remote path.

### Alternative 3: Dedupe inside the Sim (per-handler idempotence, SC R10 wording)
- **Description**: Inventory handlers keep the last 16 replies per player.
- **Pros**: Idempotence co-located with semantics.
- **Cons**: A per-connection concept in the Sim; duplicates still consume `opSeq` and are recorded; every idempotent system re-implements it.
- **Rejection Reason**: Grid rule 8 and architecture Q-ARCH-04 place it in the adapter keyed `(peer, reqId)`; the Sim stays peer-free.

### Alternative 4: Client retries un-acked requests
- **Description**: Resend after a timeout, relying on dedupe.
- **Pros**: Survives a lossy transport.
- **Cons**: The transport is already reliable; a retry after a dedupe-cache eviction double-applies; HB10 forbids it.
- **Rejection Reason**: Resync on timeout is safe; resend is not.

### Alternative 5: Decide the transport now (ENet + SceneMultiplayer, 4 channels)
- **Description**: Commit to spec 06 §2's ENet channel map today.
- **Pros**: Fewer open questions.
- **Cons**: Every behaviour it relies on is NOT SOURCEABLE for 4.7.1; the Steam path (separate MultiplayerPeer) is open (Q-ARCH-09).
- **Rejection Reason**: Architecture principle 5 — verify the engine before trusting it; the seam lets M-1a proceed without the decision.

## Consequences

### Positive
- The Sim stays peer-free, Godot-free and fully testable; ordering, dedupe, packing and join sequencing are tested in plain C# over `LoopbackTransport`.
- Solo, host and remote clients exercise one request/event path.
- The transport can change (ENet, Steam, dedicated server) behind `ITransport` without touching the Sim or any system.
- Double-apply is prevented by construction: one send per request, dedupe in one place, resync instead of retry.

### Negative
- A hand-written codec per op and event kind (more code than RPC Variant arguments; spec 13 §3's saving is partly given back).
- Loopback encodes and decodes for the host's own client (small CPU cost every frame).
- Several numbers (pacing, high-water, burst interval) are PROPOSED until the Networking & Session GDD ratifies them.

## Risks
- **The spike finds no reliable cross-type ordering on one channel.** Mitigation: §5.1's per-peer sequence + reorder buffer inside the adapter core; Grid's version-gap resync as the normal path.
- **Large JoinState/RejoinState payloads exceed what the transport fragments.** Mitigation: the adapter chunks to ≤ 64 KB already (spec 06 §5).
- **Loopback cannot bypass `MultiplayerAPI` in Godot** (SC N2 UNVERIFIED). Mitigation: `LoopbackTransport` is plain C# and never touches `MultiplayerAPI`; only the remote transport does.
- **Host-advantage complaints.** Mitigation: `contestedGrabWinsByHost` telemetry (Grid Network §Host); input-time tie-breaking revisited only above the threshold.
- **Overflow-driven resync storms.** Mitigation: Networking-owned cooldown; the event log is sized so the worst fixture tick cannot overflow (SC F8).
- **The real transport copies or allocates per send/receive** (RPC `byte[]` arguments, `GetPacket` arrays — UNVERIFIED), eroding the preallocated, span-based intent. Mitigation: the adapter core stays 0-alloc; the transport's cost is measured in the M0 spike (checklist j) and pooled where the API allows. `ITransportSink.OnMessage(ReadOnlySpan<byte>)` is called only synchronously from `Poll` on the main thread (never captured in lambdas or async code). *(godot-specialist engine review 2026-10-16: APPROVE WITH NOTES, no blocking issue; checklist items added to §5.)*

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | N1 / TR-sim-042: Sim knows no peers, accounts, channels, RPC, wall clock, RTT, Godot types; request ticks in host domain | §1 crossing points; architecture test; `IHostClock` host domain |
| simulation-core.md | N3.5 / TR-sim-043: join snapshot after `Deliver` of `joinTick`; joiner buffers later batches | §4 join/rejoin ordering |
| simulation-core.md | HB10, R7, R10: never resend; `busy` on the reply stream; no Sim dedupe | §3 |
| simulation-core.md | N2: host process uses the loopback adapter with the same encoder | §2 loopback |
| grid-inventory.md | Network: reliable-ordered, events/replies/snapshots in-stream, event before reply | §5.1 transport contract; §6 never reorder per peer |
| grid-inventory.md | Network rule 8: `reqId` u16 per connection, last-16 reply replay keyed `(peer, reqId)`, reset on rejoin | §3 dedupe |
| grid-inventory.md | TR-inventory-020: Intake/Depot non-openers get `GroupCounts` only; ≤ 40 B/tick/peer | §6 packing (budget ratified by Networking) |
| grid-inventory.md | TR-inventory-021: bursts paced ≤ 800 B/tick; 24 KB high-water → resync-pending | §6 pacing and valve |
| grid-inventory.md | Network §Join: sender from transport peer, never a client `playerId`; no container ops before JoinState applied | §3 actor; §4 |
| input-controls.md | TR-input-013: server-validated verbs are reliable requests, no hold-duration field | §3 request envelope (payloads owned by each OpKind) |
| manual-delivery.md | TR-delivery-008…010, 016: packed `deliver_matching`, `holdStartTick` in host domain via `IHostClock`, host-side anchor check, no replicated state | §1 `IHostClock`; §3 codec per descriptor; checks stay in the handler |
| mail-destinations.md | TR-mail-014, 016, 017, 021, 023: `DeliveriesState` reliable-ordered event, value-hint runs, JoinState ≤ 72 B + `outcomeSeq` drop rule, paced `MailShiftStats`, host-authoritative payment | §5.1 reliable channel; §6 pacing; §4 join rules; payload sizes are Mail's descriptors |
| comms.md (frozen) | TR-comms-001…006: channel 3, host relay, sender from peer, ≤ 10 B/tick + 32 KB drop-oldest, ping resolution, JoinState replay | §5.2 channel requirement; §6 comms policy; ping resolution is a Sim op/event like any other |
| content-data-definitions.md | Network 3 / TR-content-025: handshake before admission, refusal precedence | §4 |

## Performance Implications
- **CPU**: Per frame: one `Poll` per transport, one decode per request, one copy pass in `Deliver`, one pack/encode pass per peer. Loopback adds one encode/decode per host request and per host-bound event (small; measured in the ClientLayer budget).
- **Memory**: Per peer: outbound queue up to the high-water (24 KB) + paced burst buffer; dedupe cache 16 replies × ~32 B; all preallocated.
- **Load Time**: None.
- **Network**: Envelope overhead 4 B per request (`kind`, `reqId`); event batching one packet per peer per tick (SC N5 recommendation); budgets owned by Networking & Session.

## Migration Plan
No code exists. M-1a: Sim seam types (with ADR-0003/0007/0008), `HostAdapter`/`ClientAdapter` core, descriptor codecs for M-1a op/event kinds, `LoopbackTransport`, dedupe, join sequencing for the single local player, H-NET-1/5 and dedupe tests. M0: multiplayer spike → transport ADR → `ITransport` implementation(s), channel map, `Hello`/`HelloAck` mechanism, pacing/high-water numbers ratified.

## Validation Criteria
- An architecture test proves `PerformativeMail.Sim` references neither `Client.Core` nor `GodotSharp`, and no Sim type mentions a peer.
- H-NET-1 (reply after event, no double-apply), H-NET-2 (snapshot vs events), H-NET-5 (decoder fuzz: `bad_request` or valid op, no exception) pass over `LoopbackTransport`.
- Dedupe test: a duplicated `reqId` inside the last 16 gets the original reply and enqueues nothing; outside the cache it is a new op.
- No-resend test: a client with a withheld reply never sends the request twice and resyncs after 5 s.
- Join test: batches with `tick ≤ joinTick` dropped, later ones buffered and replayed in order.
- At M0: the same suite passes over the real transport chosen by the spike.

## GDD Impact (not edited here — owners apply at re-review)
- **simulation-core.md R10 "Duplicates"** says a handler that promises idempotence implements it itself and that Grid rule 8 does; Grid rule 8 (current text) puts the cache in the network adapter keyed `(peer, reqId)`. This ADR adopts the adapter placement (Q-ARCH-04); SC R10 needs a one-line correction ("Networking's adapter, not the handler, answers duplicates").
- **spec 13 §3** ("Godot high-level multiplayer: RPCs for events and requests … deletes `Sim/Net` serialisation layer") is narrowed: RPC/MultiplayerAPI may remain the transport, but requests and events are opaque bytes encoded by per-descriptor codecs.
- **spec 06 §2** channel map (0 unreliable, 1 reliable-ordered, 2 bulk) is retained as the logical map; real channel assignment and comms channel 3 wait for the spike.
- **architecture.md §Network adapter boundary** lists the adapter "in the Godot project"; this ADR moves the adapter core into Godot-free `Client.Core/Net` and leaves only `ITransport` implementations in the Godot project.
- **Erratum 2026-10-16 (/architecture-review, technical-director) — manual-delivery.md Network table / M-NET-1 / TR-delivery-008.** The packed `deliver_matching` request is **24 B MATCH / 28 B MISDELIVER_ONE**, not 12/16 B. It is this ADR's envelope (`kind u16 · reqId u16`, not MD's `op code u8 · version + flags u8`) + `mailboxId u32 · holdStartTick u32 · ClaimedPosition 12 B` (P13 feet x,y,z i32 mm, required on every range-gated op per grid-inventory R7 / ADR-0009 §1 / ADR-0014 §5) [+ `selectedEntryId u32` on MISDELIVER_ONE only; presence implied by length, any other length → `bad_request`]. Transport framing is excluded. The wire layout is decided here and the GDD figure is superseded; the owner syncs MD at re-review. The codec is registered under the Delivery-owned `DeliverMatching` descriptor (ADR-0009 erratum, same date).

## Related
- ADR-0003, 0004, 0007, 0008 (dependencies); ADR-0002 (FrameRunner order); ADR-0009 (inventory ordering contract), ADR-0010 (content hash), ADR-0016 (ClientLayer), ADR-0020/0021/0022/0023 (transforms, flights, JoinState, Steam); the M0 transport ADR (to be written after the spike).
- `design/gdd/simulation-core.md` N1–N6, R5–R14, R21; `design/gdd/grid-inventory.md` Network; `design/gdd/content-data-definitions.md` Network; `design/spec/06-multiplayer-netcode.md` §1–§2, §5–§6; `design/spec/13-v1-cut-and-decisions.md` §3.
