# Story 004: HostAdapter as IEventSink — copy-only Deliver, audience fan-out and ordered per-peer batches

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (≈ 1 day; ≈ 500 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` R12 (delivery), §Network N3 (event replication contract: one batch per tick in raise order; audience; replies follow events)
**Requirement**: `TR-sim-022` (`IEventSink.Deliver` once in phase 13, copy-only — sink half), `TR-inventory-019` (reliable ordered delivery; events, replies, snapshots on one channel; event before reply), `TR-mail-014` (DeliveriesState is a reliable-ordered event, not on a snapshot — satisfied by the generic path)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: Event Log, Replication Classes and Event Delivery (§7 delivery and adapter ordering rule) — primary; ADR-0011 (§6 per-peer outbound policy) — secondary
**ADR Decision Summary**: Inside `Deliver` the adapter only (a) copies the entries it needs into its own preallocated buffers, (b) resolves each entry's audience mask to a peer set through its slot → peer map (so a queued batch holds peers, never slots), (c) updates its per-peer reply cache from `OpReply` entries, and (d) may tag same-`CallSeq` events with the reply's `reqId`; it makes no transport, Godot or Sim call and keeps no span. After `Tick()` returns, `FlushOutbound(tick)` encodes each peer's entries for that tick into one ordered batch in log order (`Internal` never leaves) on the reliable channel; two entries to one peer are never reordered (cross-call merging is off until H-NET-1 proves it).
**ADR Version**: 2026-10-16 (ADR-0008, ADR-0011)

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: None at M-1a (loopback). Whether the real transport keeps reliable order across message types on one channel is an M0 spike item (ADR-0011 §5 item 1).

**Control Manifest Rules (Core — network seam)**:
- Required: `Deliver` does only (a)–(d); batches hold peers, never slots; after `Tick()` each peer's entries become one ordered batch in log order; `Internal` never leaves; per-peer outbound queue with byte accounting; chunked runs reassembled in order.
- Forbidden: calling the transport, Godot or the Sim inside `Deliver`, or retaining its span; reordering two entries to one peer; sending before `Tick()` returns; cross-call merging before H-NET-1.
- Guardrail: one batch packet per peer per tick; empty-tick Sim output 0 B on the wire for a peer with nothing; per-peer queue ≤ high-water (24 KB PROPOSED); `Deliver` and `FlushOutbound` allocate 0 B after warm-up.

---

## Acceptance Criteria

*From GDD N3.1–N3.3 and ADR-0008 §7 / ADR-0011 §6, scoped to this story:*

- [ ] **AC-1 Copy-only Deliver.** A `Deliver` call with a mix of `Internal`, `Replicated`, `OwnerOnly` and `OpReply` entries makes no transport call (a transport spy records nothing until `FlushOutbound`), copies the entries into preallocated per-peer buffers, and is unaffected by the Sim later overwriting the span (Debug `0xDD` fill); an entry whose audience bit maps to no admitted peer is dropped for that slot.
- [ ] **AC-2 Ordered batch.** After `FlushOutbound(T)`, the loopback client receives exactly one batch for tick T containing, in log order, every non-`Internal` entry whose audience includes its slot — so a handler's events precede its `OpReply` exactly as in the log — and no `Internal` entry; a tick with no entries for the peer sends nothing; a `Continuation` chunk run arrives contiguous and in order.
- [ ] **AC-3 Peers, not slots.** An `OwnerOnly` entry for slot 2 raised in tick T is queued for the peer that held slot 2 at `Deliver` time; if that peer leaves and another peer takes slot 2 before the flush, the newcomer does not receive it (slot→peer resolved inside `Deliver`).

---

## Implementation Notes

*Derived from ADR-0008 §7 and ADR-0011 §6:*

- `HostAdapter : IEventSink` (`Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)`): for each entry with `Class != Internal`, for each set audience bit, look up the peer for that slot and append a copy of the 64 B entry (or a compact `(index, entry)` record) to that peer's preallocated per-tick list; remember `overflow` for the M0 resync path (store only).
- `FlushOutbound(uint tick)` (the `IFrameTransport` hook, called by `FrameRunner` after each `Tick()`): for each peer with entries, encode one batch message: header `tick u32 · count u16`, then per entry `kind u16 · payload` via an `IEventCodec` registry keyed by the descriptor's `WireCodecId`. Register the `OpReply` codec here (36 B `Reply` → its wire form with `reqId` truncated to u16, ADR-0007 §6) and a generic fixed-length passthrough codec usable by event kinds whose wire form equals their payload (feature epics register real codecs; packing such as `ContainerDelta`/`GroupCounts` is theirs and must encode consecutive entries only).
- Event payloads cross the wire only through codecs — never by blitting the 64 B entry.
- Reply-cache update (c) is implemented in Story 005 (call site lives here: when copying an `OpReply` for a peer, hand it to the dedupe component). Tagging (d) is applied by grid-inventory/ADR-0009 when it needs reqId-tagged events — leave a hook, do not implement.
- Pacing, high-water valve and overflow-driven resync are M0 (sustained + burst numbers are PROPOSED; no burst producers exist at M-1a); keep per-peer byte accounting so they can be added.

---

## Out of Scope

- Story 005: reply cache and in-flight set.
- Story 006: client-side batch decode into the `ClientLayer` inbox.
- Pacing, high-water valve, overflow resync, keyed snapshots via `ISnapshotSource`, JoinState — M0.
- Feature event codecs and packing (`ContainerDelta`, `GroupCounts`, `UnitsRemoved`, `DeliveryOutcome`) — their epics.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/HostAdapterOutboundTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/net/HostAdapterOutboundTests.cs (+ OpDrainTests CallSeq re-pin, PlayerJoinTests Extra pin) — Unit.Net 211/211 on four legs

---

## Dependencies

- Depends on: Story 003; simulation-core Story 015 (`IEventSink`, `EventEntry`), Story 016 (`OverflowInfo`), Story 020 (`OpReply`)
- Unlocks: Story 005, Story 006, Story 007

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 copy-only Deliver, AC-2 ordered batch per tick, AC-3 peers not slots) — none deferred
**Deviations** (advisory, accepted by LP): (1) batch wire `tag u8 = 0x02 · tick u32 · count u16 · records (kindField u16, bit 15 = Continuation · payload)`; OpReply record = `ReplyCodec` body; bare busy/bad_request reply = tag 0x01 + same body (one reply codec) — recorded in ADR-0011 §6 as-implemented note; (2) batches over `AdapterSettings.MaxBatchBytes` (default 65 536) split into several same-tick batches in order — departs from "one batch per peer per tick" only above 64 KB; (3) stale guard: a Deliver at or before the last delivered tick is ignored, a repeated flush tick sends nothing (TD-057(1)); (4) lenient `EventCodecRegistry` — kinds without a codec dropped + counted (`NoCodecDropCount`, `NoWireKindCount`, `MissingCodecCount`); (5) **Sim changes**: `EventLog.TryRaiseWithCallSeq` (internal) + `SimWorld.Drain` — OpReply carries its handler scope's CallSeq, 0 for pre-check rejects (TD-048(1) decision; ADR-0008 note); `PlayerJoinHandler` join reply `Extra[0..4)` = new player id (ADR-0007 §5 note) — adapter completes the join from it; (6) world needs its sink at build time → tests use a relay; production composition needs one (TD-065). Advisories logged as TD-065.
**Test Evidence**: Logic — tests/unit/net/HostAdapterOutboundTests.cs; tests/unit/sim/Core/OpDrainTests.cs, PlayerJoinTests.cs; Unit.Net 211/211, OpDrainTests 21/21, PlayerJoinTests 18/18 on net10/net8 × Debug/Release; full suite 2 622 / 0 failed / 16 skipped at net10 Debug (real tree).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (no blocking) → advisories 1–5 fixed in one round; QL-TEST-COVERAGE ADEQUATE (gap 6 fixed); godot-csharp-specialist APPROVE.
