# Story 003: HostAdapter inbound — peer table, actor stamping, EnqueueOp, busy and bad_request

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 450 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` R6 (actor stamped by the adapter), R7 (`busy`), R18 (callbacks only enqueue); §Network N1–N2
**Requirement**: `TR-sim-016` (actor stamped from the peer, never the payload), `TR-sim-017` (full ring → adapter replies `busy` — adapter half), `TR-sim-008` (nothing applied inside a callback), `TR-inventory-025` (sender from the transport peer; malformed → `bad_request`, no exception)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§2 `HostAdapter`, frame order; §3 actor; §4 admission) — primary; ADR-0007 (§2 `busy` on the peer's reply stream, §5 reasons), ADR-0002 (§5 callbacks only enqueue) — secondary
**ADR Decision Summary**: `HostAdapter` owns the peer table `peer → (accountKey, playerId, slot)`, stamps `Op.Actor` from that table (any player id in a payload is ignored), decodes requests with the codec registry and calls `SimWorld.EnqueueOp` on the main thread inside `PumpInbound()` — remote `transport.Poll` first, then `loopback.Poll`, so a same-frame remote op gets the lower `opSeq`. If `EnqueueOp` returns false it answers `busy` (1) at once on that peer's reliable reply stream (never through the event log); a decode failure is answered `bad_request` (8); neither consumes `opSeq` or is recorded. Messages from unknown (unadmitted) peers are dropped. On admission it enqueues the `System` `PlayerJoin` op.
**ADR Version**: 2026-10-16 (ADR-0011, ADR-0007, ADR-0002)

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: Which thread real transport callbacks arrive on is UNVERIFIED (M0 spike). Irrelevant here: `ITransport.Poll` is called from `PumpInbound` on the main thread, which is where stamping happens (ADR-0002 §5).

**Control Manifest Rules (Core — network seam)**:
- Required: `HostAdapter` sets `Op.Actor` from its peer table; unknown peers' messages before admission are dropped; `EnqueueOp` false → `busy` reply on that peer's reliable stream, not cached; decode failure → `bad_request`, nothing enqueued; frame order remote poll → loopback poll; on admission enqueue the `System` `PlayerJoin`.
- Forbidden: payload fields used for identity; an "is host" shortcut; calling `EnqueueOp` from a transport callback; dedupe inside the Sim; representing `busy` as an event-log entry.
- Guardrail: `PumpInbound` allocates 0 B after warm-up.

---

## Acceptance Criteria

*From GDD SC-R6-1 (adapter side), R7 and ADR-0011 §2–§4, scoped to this story:*

- [ ] **AC-1 Actor stamping and admission.** With the local loopback peer admitted into slot 0 (M-1a local admission — `HostAdapter.AdmitLocal(peer, slot)` enqueues a `System` `PlayerJoin` and maps the peer to the resulting `playerId` once the join reply is delivered), a request from that peer whose payload names another player's id is enqueued with `Actor == that peer's playerId`; a request from an unadmitted peer handle is dropped (nothing enqueued, nothing replied, counted).
- [ ] **AC-2 busy and bad_request.** When the Sim's ring is full for player ops, a request receives an adapter-generated reply with reason `busy` (1) and the request's `reqId` on that peer's reliable channel during the same `PumpInbound`, and no `opSeq` is consumed; a request with an unknown kind, a short payload or trailing bytes receives `bad_request` (8) the same way and nothing is enqueued; the encoded adapter reply is byte-identical in shape to a Sim `OpReply` sent by Story 004 (one reply codec).
- [ ] **AC-3 Frame order and callbacks.** `PumpInbound()` polls the remote transport (possibly none at M-1a) before the loopback transport, so a remote request and a loopback request arriving in the same frame get ascending `opSeq` in that order (a fake second transport stands in for remote); no `EnqueueOp` happens outside `PumpInbound` (a transport stub that tries to deliver during `Send` sees its message queued until the next `Poll`).

---

## Implementation Notes

*Derived from ADR-0011 §2–§4 and Key Interfaces; ADR-0007 §2, §5:*

- `src/PerformativeMail.Client.Core/Net/HostAdapter.cs` — `HostAdapter(SimWorld sim, ITransport loopback, ITransport? remote, AdapterSettings s, IRoster roster)` (ADR-0011's sketch has one transport; M-1a needs the loopback end and an optional remote one — keep the remote optional and `null` at M-1a). It implements `ITransportSink` for both and, from simulation-core Story 024, `IFrameTransport` (`PumpInbound`, `FlushOutbound(uint tick)` — the latter in Story 004).
- Peer table: preallocated array of 8 entries `{ PeerHandle peer; ulong accountKey; EntityId playerId; PlayerSlot slot; PeerState state; }`; lookups by handle are a linear scan of ≤ 8 (no dictionary).
- **Admission at M-1a.** `Hello{gameVersion, contentHash, profileId, …}` / `HelloAck` and refusal precedence are M0 (TR-content-025). At M-1a the host admits its own loopback peer locally (`AdmitLocal`), with the content hash trivially equal; keep the state machine shaped so `Hello` slots in at M0 (`Connected → Admitted`). `IRoster` (Networking's account → `playerId` roster) is a small interface; at M-1a a single-entry in-memory roster.
- Reply generation for `busy`/`bad_request`: build a `Reply { ReqId, Kind, Reason, Result = Rejected }`, encode with the shared reply codec (Story 004 owns it; implement the encoder here if this story lands first and let Story 004 reuse it) and `Send` on `NetChannel.Reliable` to that peer immediately. Do **not** cache it (Story 005).
- Never call `EnqueueOp` from `OnMessage` if `OnMessage` could be invoked outside `Poll` — with the `ITransport` contract it cannot, but assert the stage flag (`_pumping`) in Debug.
- Disconnect handling (`FreezePlayer` System op, drop queued batches) is M0 for remote peers; for loopback at M-1a a disconnect ends the session — log and ignore.

---

## Out of Scope

- Story 004: `IEventSink`, per-peer batches, outbound flush, the reply codec's normal (Sim `OpReply`) path.
- Story 005: dedupe cache and in-flight set.
- `Hello`/`HelloAck`, content-hash handshake, JoinState, remote transports — M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/HostAdapterInboundTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/net/HostAdapterInboundTests.cs (+ ReplyCodecTests.cs) — Unit.Net 185/185 on four legs

---

## Dependencies

- Depends on: Story 001, Story 002; simulation-core Story 014 (`EnqueueOp`), Story 022 (`PlayerJoin`), Story 024 (`IFrameTransport`)
- Unlocks: Story 004, Story 005, Story 007

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 actor stamping/admission, AC-2 busy/bad_request + one reply codec, AC-3 frame order/callbacks) — none deferred
**Deviations** (advisory, accepted by LP): (1) ctor takes an extra `OpCodecRegistry`; (2) `HostAdapter` does not implement `ITransportSink` itself — two private per-transport sinks; peer table keyed by (`PeerOrigin`, `PeerHandle`) (closes TD-016(1) of loopback story-001); (3) `AdmitLocal` → `Joining`; internal `CompleteJoin(joinReqId, reason, playerId)` (join reqId = `JoinReqIdBase` 0x10000 + slot) is called by Story 004's `Deliver`; requests from Connected/Joining peers dropped + counted; (4) `ReplyCodec` wire shape `tag u8 · reqId u16 · kind u16 · reason u16 · extraLen u8 · extra` (trailing zeros trimmed), not yet recorded in ADR-0011 §3; (5) non-Reliable-channel requests dropped + counted; (6) SystemOnly kinds from peers → `bad_request` (TD-056(5)); NetAssertCode 1001–1003. HostAdapter split into partials (HostAdapter.cs / .Admission.cs / .Inbound.cs). Advisories logged as TD-062.
**Test Evidence**: Logic — tests/unit/net/HostAdapterInboundTests.cs, tests/unit/net/ReplyCodecTests.cs; Unit.Net 185/185 on net10/net8 × Debug/Release (rsync copy; re-run 185/185 on the real tree at net10 Debug); full suite 2 429 / 0 failed at net10 Debug before the revision round.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS → APPROVE after one revision round (Admit split to ≤ 40 lines/CC ≤ 10, DuplicateConnectCount fix); QL-TEST-COVERAGE ADEQUATE (gaps 2 and 5 fixed in-story); godot-csharp-specialist APPROVE.
