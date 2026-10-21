# Story 006: ClientAdapter — reliable requests, batch intake for ClientLayer, IHostClock estimator

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 450 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` §Network N1 (`IHostClock`), N2 (batches handed to the `ClientLayer` inbox, applied by `MirrorDrain` at the next client tick), N4 (host tick domain); R2 (`ClientLayer` reads only its mirror)
**Requirement**: `TR-input-013` (server-validated verbs travel as reliable requests outside the input frame; no hold-duration field), `TR-delivery-009` (`holdStartTick` in the host domain via `IHostClock`), `TR-sim-014` (host's client uses the same path as a remote client)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§2 `ClientAdapter`, §3 never resend an applied op, §1 `IHostClock`) — primary; ADR-0007 (§6 u16 wrapping `reqId` per connection), ADR-0003 (§6 `ClientLayer` mirror only) — secondary
**ADR Decision Summary**: `ClientAdapter` encodes requests with a per-connection u16 wrapping `reqId` and sends each exactly once on the reliable channel; it never retries at the application level, except that a request answered `busy` may be sent again as a new attempt. It decodes inbound batches and replies into the `ClientLayer` inbox (never touching `SimWorld`); `MirrorDrain` applies them at the next client tick. It implements the client `IHostClock` estimator (Networking-owned algorithm; trivially exact on loopback) — every tick a client puts in a request is in the host tick domain.
**ADR Version**: 2026-10-16 (ADR-0011, ADR-0007, ADR-0003)

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: The remote estimator's accuracy target (≤ 3 ticks p95 at 150 ms RTT) needs the real transport — M0.

**Control Manifest Rules (Core — network seam)**:
- Required: `ClientAdapter` sends each request exactly once on the reliable channel; only a `busy`-answered request may be re-sent (new attempt); u16 wrapping `reqId` per connection; inbound decode into the `ClientLayer` inbox; `IHostClock { uint EstimatedHostTick; bool IsValid; }` client estimator.
- Forbidden: an "is host" shortcut; the client calling the Sim; application-level retries of un-acked requests; a hold-duration field in any request.
- Guardrail: 4 B envelope per request; inbound decode allocates 0 B after warm-up.

---

## Acceptance Criteria

*From ADR-0011 §2–§3 and GDD N2/N4, scoped to this story:*

- [x] **AC-1 Requests.** `ClientAdapter.Send(kind, payload)` returns the `reqId` it used, starting at 1 and incrementing per request with u16 wrap (65 535 → 0 → 1 … — 0 is skipped only if the Sim reserves it; otherwise allowed), and emits exactly one reliable message per call; no API re-sends a request automatically; `Resend(reqId)` is accepted only for a request whose reply was `busy` and is sent under a **new** `reqId`.
- [x] **AC-2 Inbox.** A batch for tick T and the replies it contains are decoded into an `IClientInbox` (entries in order, each with its tick), not applied before the `ClientLayer`'s `MirrorDrain` step runs; an adapter-generated `busy`/`bad_request` reply is delivered to the inbox like any other reply; malformed inbound bytes are dropped and counted, never thrown.
- [x] **AC-3 IHostClock (loopback).** Before the first batch `IsValid` is false; after receiving the batch for tick T, `EstimatedHostTick == T + 1` (the host's `CurrentTick` between frames); the estimate never decreases.

---

## Implementation Notes

*Derived from ADR-0011 §2–§3 and Key Interfaces:*

- `src/PerformativeMail.Client.Core/Net/ClientAdapter.cs` — `ushort Send(OpKind kind, ReadOnlySpan<byte> payload)` (ADR-0011 sketch), `void PumpInbound(IClientInbox inbox)`, `IHostClock Clock { get; }`. It is an `ITransportSink` for its transport end.
- `IClientInbox` lives in Client.Core (`Net/` or `Mirror/`): receives `(tick, entry)` and `(reply)` records into preallocated storage; a `MirrorDrainStep : IClientStep` (bound at `ClientStepId.MirrorDrain`) drains it each client tick and dispatches to per-system mirrors (none exist yet at M-1a besides tests — feature epics register mirror consumers). Note: the Sim-side `ClientLayer` shell (simulation-core Story 023) cannot reference Client.Core; Client.Core binds its `MirrorDrainStep` into the Sim's `ClientLayerBuilder` (dependency direction Client.Core → Sim is allowed).
- Busy handling: keep a small pending table (`pendingQueueMax` 16, Grid rule) of `reqId → (kind, payload bytes)` so a `busy` reply can be re-sent as a new attempt by the owning feature; anything else waits for its reply. A 5 s reply timeout triggers a resync at M0 (needs snapshots) — at M-1a only count it.
- `IHostClock` client estimator: loopback = last batch tick + 1, `IsValid` after the first batch. The RTT/2 ping algorithm is Networking's (M0).
- Requests never carry a hold duration (TR-input-013); `holdStartTick` (Manual Delivery) is filled by the feature from `Clock.EstimatedHostTick`.

---

## Out of Scope

- Story 007: wiring `ClientAdapter` + `HostAdapter` + `FrameRunner` for the host process.
- Per-system mirrors (`ContainerMirror`, `PendingQueue`, temp ids) — grid-inventory (M0 client side).
- Remote estimator, ping, reply-timeout resync — M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/ClientAdapterTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/net/ClientAdapterTests.cs (+ EventCodecRoundTripTests.cs) — Unit.Net 306/306 on four legs

---

## Dependencies

- Depends on: Story 001, Story 002, Story 004 (batch format); simulation-core Story 023 (`ClientLayer`, `IHostClock`)
- Unlocks: Story 007

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (none deferred)
**Deviations**: Advisory only — TrySend/TryResend non-throwing variants added (Send/Resend throw on refusal); reqId 0 allowed on wrap, still-pending ids skipped; pending table 16 (full → refuse, evict oldest busy-answered); reply delivered only when its reqId is awaited and its kind matches (TD-066(1)); batch handling atomic (inbox-full drops the whole batch, clock still advances); bare replies stamped with the last batch tick (0 before any); reply timeout counted via UpdateTime(nowMs) with lazy SentMs stamp; inbox/drain step in Client.Core/Mirror/; IEventCodec gains TryMeasure/TryDecode + EventBatchReader (TD-065(3)); NetAssertCode 1006–1008. Loopback clock = last batch tick + 1 lags CurrentTick on quiet ticks (HostAdapter sends no empty batch) — story 007 to resolve. Remaining advisories → TD-068.
**Test Evidence**: Logic: tests/unit/net/ClientAdapterTests.cs (+ tests/unit/net/EventCodecRoundTripTests.cs); full suite 2,945 / 0 failed on four legs (net10/net8 × Debug/Release)
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (1 blocking: HandleBatch size/complexity) → fixed → APPROVE; QL-TEST-COVERAGE ADEQUATE; .NET review folded into LP (no issues)

