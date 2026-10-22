# Epic: Network Adapter Core — Loopback (M-1a)

> **Layer**: Core
> **GDD**: design/gdd/simulation-core.md §Network (N1–N3; Networking & Session #5 has no GDD), plus the Network sections of `grid-inventory.md`, `manual-delivery.md` and `mail-destinations.md` for the M-1a op kinds
> **Architecture Module**: Network adapter core (Godot-free `src/PerformativeMail.Client.Core/Net/`: `HostAdapter`, `ClientAdapter`, per-OpKind codec, dedupe, `LoopbackTransport`) — ADR-0011
> **Milestone slice**: M-1a seam + loopback only; transport (SceneMultiplayer/ENet/Steam) is M0
> **Engine Risk**: LOW (plain C#; ADR-0011 makes no Godot multiplayer assumption). The transport half is HIGH (NOT SOURCEABLE), deferred to the M0 spike.
> **Est. size**: M — 6–8 stories
> **Implementation priority**: alongside 1–2 (Simulation Core's `FrameRunner` needs an `IFrameTransport`; every feature op crosses this codec)
> **Status**: Ready
> **Stories**: 9 stories — 7 Complete, 2 Ready (counts refreshed 2026-10-20 from the story headers) — see the Stories table below

## Overview

The architecture's third principle is "one code path": the host's own client reaches the Sim through the **same** adapter and the **same** wire codec as a remote client, over an in-process loopback transport. M-1a is solo, but ADR-0011 decides that the seam half is needed now. If the codec were skipped at M-1a, M0 would be the first time any op was serialised, and every feature's op shape would be tested late.

This epic creates the `PerformativeMail.Client.Core` assembly (ADR-0001: "created by the first Input, HUD or network-seam story") and builds the following in it:
- **The `ITransport` abstraction and `LoopbackTransport`.**
- **`HostAdapter`:**
  - peer → account → `PlayerSlot` map (one local slot at M-1a);
  - actor stamping from the transport peer, never from a payload;
  - `EnqueueOp` on the main thread only, with `busy` replies on a full ring;
  - the op wire encoder/decoder driven by per-`OpKind` descriptors;
  - u16 `reqId` dedupe with last-16 reply replay keyed `(peer, reqId)`;
  - the `IEventSink` implementation, which copies inside `Deliver` and sends after `Tick()`;
  - audience-mask fan-out;
  - in-stream ordering: events, then replies, never reordered per peer.
- **`ClientAdapter`:** request envelopes and mirror-batch intake for `ClientLayer`.
- **An `IHostClock` estimator:** trivially exact on loopback, but behind the interface.

Feature epics register their `OpKind` descriptors: the inventory ops, `deliver_matching` (24/28 B packed incl. claimed position, MATCH omits `selectedEntryId` — erratum 2026-10-16), `throw`, and `PlayerJoin`/`PlayerLeave`.

**Out of scope (M0, after the multiplayer spike):**
- `ITransport` implementations over SceneMultiplayer/ENet/Steam (`src/Game/Net/`);
- channels and RPC ordering;
- the `Hello`/`HelloAck` auth and content-hash handshake (TR-content-025);
- JoinState chunking (ADR-0022);
- per-peer pacing and the 24 KB high-water valve;
- GroupCounts packing;
- comms channel 3.

**Why these ADRs govern it.**
- ADR-0011 is the module's own decision.
- ADR-0007 puts `reqId` dedupe in the adapter and keeps it out of the Sim (the Sim's header holds an opaque u32; the wire carries u16).
- ADR-0008 fixes the one-shot copy-only `Deliver` in phase 13 and the replication classes the adapter fans out.
- ADR-0002 forbids applying anything inside a transport callback. Callbacks only enqueue, and `FrameRunner` drains before ticks.

**Risks.**
- (1) There is no Networking & Session GDD. The M-1a scope is fully specified by ADR-0011 plus Sim Core N1–N3, but byte budgets (TR-inventory-020/021, TR-mail-016/017/021) wait for that GDD (Q-ARCH-06: upload bandwidth exceeds spec 06).
- (2) `Client.Core` must stay Godot-free. The architecture test from `simulation-core` (SC-ARCH-1) is extended to cover it.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0011: Network Adapter Boundary | seam (`EnqueueOp`, sample ring, `IEventSink.Deliver`, `ISnapshotSource`, `IHostClock`); transport-agnostic adapter core in plain C#; loopback = remote codec; the Sim knows no peers | HIGH domain / LOW decided content |
| ADR-0007: Op Queue, Drain and Replies | u32 `reqId` header slot, u16 on the wire, dedupe + last-16 replay in the adapter, `busy` on a full ring | LOW (Sim) / HIGH (adapter transport) |
| ADR-0008: Event Log, Replication Classes and Event Delivery | `Deliver` once in phase 13, copy-only, send after `Tick()`; classes Internal/Replicated/OwnerOnly/Targeted | LOW (Sim) / HIGH (transport) |
| ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim | transport callbacks only enqueue; `FrameRunner` drains before ticks and flushes after the last tick | LOW (driver) |
| ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework | `PerformativeMail.Client.Core` net8.0, Godot-free, referenced by the tests | LOW |

## GDD Requirements

There are no `TR-net-*` IDs (no Networking GDD). These Appendix A rows are governed by this module:

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-sim-008 | Transport callbacks only enqueue; nothing applied inside a callback | ADR-0002 ✅ | M-1a |
| TR-sim-014 | SimWorld (host) and ClientLayer (client shell) separate; host loopback uses same encoder | ADR-0003 ✅ | M-1a |
| TR-sim-016 | Actor stamped by adapter from peer; System=0x00000001; opSeq monotonic, never reused | ADR-0007 ✅ | M-1a |
| TR-sim-017 | Op ring 4096 (pow2); full → false, no opSeq consumed, adapter replies busy; systemOpReserve 64 | ADR-0007 ✅ | M-1a (adapter `busy` half) |
| TR-sim-022 | No sync subscribers; pull cursors ≤8; IEventSink.Deliver once in phase 13, copy-only | ADR-0008 ✅ | M-1a (sink half) |
| TR-sim-042 | Sim knows no peers/accounts/channels/RPC/wall clock/RTT/Godot types; request ticks in host domain | ADR-0011 ✅ | M-1a |
| TR-sim-043 | Join snapshot after Deliver of joinTick; joiner buffers later batches | ADR-0011 ✅ | M0 |
| TR-inventory-019 | Reliable ordered delivery; events, replies, snapshots on one channel, event before reply | ADR-0009 ✅ | M-1a (loopback) |
| TR-inventory-025 | Sender from transport peer, never client playerId; malformed → bad_request, no exception | ADR-0007 ✅ | M-1a |
| TR-input-013 | Server-validated verbs bypass the frame as reliable requests; no hold-duration field | ADR-0011 ✅ | M-1a |
| TR-delivery-008 | deliver_matching: MATCH omits selectedEntryId; 24/28 B packed incl. claimed position (revised 2026-10-16) | ADR-0011 ✅ | M-1a (codec descriptor) |
| TR-delivery-009 | holdStartTick in host domain via IHostClock; host clamps window | ADR-0011 ✅ | M-1a |
| TR-delivery-011 | Idempotent reqId u16; duplicate returns original reply | ADR-0007 ✅ | M-1a |
| TR-mail-014 | DeliveriesState reliable-ordered event, not on 5 Hz snapshot | ADR-0011 ✅ | M-1a |
| TR-inventory-020 | Intake/Depot non-openers get GroupCounts only; ≤40 B/tick/peer | ADR-0011 ✅ | M0 |
| TR-inventory-021 | Bursts ≤12 KB paced ≤800 B/tick; 24 KB high-water → resync-pending | ADR-0011 ✅ | M0 |
| TR-content-025 | Join handshake compares 32-byte hash with refusal precedence; content never transferred | ADR-0010 ✅ | M0 |

**Untraced:** none for the M-1a slice. The M0 transport half waits on the multiplayer spike and ADR-0020–0023.

## Stories

Implementation order = story number (each story's `Depends on:` is authoritative; most also depend on simulation-core stories). Sizing: one `/dev-story` session each.

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Client.Core assembly, transport abstractions and LoopbackTransport](story-001-client-core-assembly-loopback-transport.md) | Logic | Complete | ADR-0011, ADR-0001 |
| 002 | [Op wire codec registry, request envelope and the H-NET-5 fuzz test](story-002-op-codec-registry-envelope-fuzz.md) | Logic | Complete | ADR-0011, ADR-0007 |
| 003 | [HostAdapter inbound — peer table, actor stamping, EnqueueOp, busy and bad_request](story-003-hostadapter-inbound-actor-busy.md) | Logic | Complete | ADR-0011, ADR-0007, ADR-0002 |
| 004 | [HostAdapter as IEventSink — copy-only Deliver, audience fan-out and ordered per-peer batches](story-004-hostadapter-event-sink-outbound-batches.md) | Logic | Complete | ADR-0008, ADR-0011 |
| 005 | [reqId dedupe — last-16 reply cache and in-flight set](story-005-reqid-dedupe-reply-cache.md) | Logic | Complete | ADR-0007, ADR-0011 |
| 006 | [ClientAdapter — reliable requests, batch intake for ClientLayer, IHostClock estimator](story-006-clientadapter-requests-inbox-hostclock.md) | Logic | Complete | ADR-0011, ADR-0007, ADR-0003 |
| 007 | [Host loopback session — FrameRunner over loopback and next-tick reply timing](story-007-host-loopback-session-frame-timing.md) | Integration | Complete | ADR-0011, ADR-0002, ADR-0008 |
| 008 | [deliver_matching wire codec (24 B MATCH / 28 B MISDELIVER_ONE)](story-008-deliver-matching-wire-codec.md) | Logic | Ready | ADR-0011, ADR-0009 (payload resolved 2026-10-16) |
| 009 | [SC-X-2 — a real Manual Delivery request from the host's own client over loopback](story-009-sc-x-2-manual-delivery-over-loopback.md) | Integration | Ready | ADR-0011, ADR-0007 (+ ADR-0014/0016 Accepted 2026-10-16) |

**Formerly blocked:** 008 — payload conflict RESOLVED 2026-10-16 (/architecture-review: 24 B MATCH / 28 B MISDELIVER_ONE with claimed position; errata in ADR-0011/ADR-0009 GDD Impact); the Delivery-owned `DeliverMatching` descriptor + payload story now exists (`production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`), so 008 and 009 are Ready with it listed under Depends on (2026-10-16 story hygiene).

**Unowned — inventory event codecs (TD-140, 2026-10-22):** all ten inventory event kinds 0x0080–0x0089 carry wire codec id 0. Grid-inventory story-004 (Out of Scope) says this epic owns the codecs; this epic's story-004 (Out of Scope) says the feature epics do. One story — a new story here or one in grid-inventory — must take ownership of the inventory event codecs before H-NET-7 (GDD grid-inventory, the BLOCKING inventory-bytes gate).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- SC-R2-1 (`FrameRunner` over loopback), SC-X-2 (a Manual Delivery request from the host's own client) and the `reqId` duplicate-replay rule (TR-delivery-011) are verified over the loopback codec
- Every M-1a `OpKind` round-trips encode → decode bit-exactly (one Theory row per descriptor)
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/net/**`, `tests/integration/**`); the architecture test asserts `Client.Core` has no Godot reference
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (none expected)

## Next Step

Run `/story-readiness` on Story 001 once simulation-core Stories 001 and 004 are DONE, then `/dev-story`. Work through stories in order — each story's `Depends on:` field tells you what must be DONE first.
