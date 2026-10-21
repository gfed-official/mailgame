# Story 005: reqId dedupe — last-16 reply cache and in-flight set

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S–M (≈ 0.5 day; ≈ 300 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` R6/R10 (`reqId` opaque to the Sim; no dedupe in the Sim); Grid Inventory Network rule 8; Manual Delivery MD5 ("the host answers a duplicate `reqId` with the original reply and applies nothing")
**Requirement**: `TR-delivery-011` (idempotent `reqId` u16; duplicate returns the original reply), `TR-sim-017` (a `busy` answer is never cached — a retry is a new attempt)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Op Queue, Drain and Replies (§6 `reqId` width, ownership and deduplication) — primary; ADR-0011 (§3 dedupe lives in `HostAdapter`, adopted verbatim) — secondary
**ADR Decision Summary**: Deduplication lives in the adapter, never the Sim, keyed `(peer, reqId u16)`. Per connection the adapter keeps a reply cache of the last 16 replies (`reqId` → encoded reply bytes copied from `Deliver`) and an in-flight set of enqueued `reqId`s whose reply has not been delivered. A request whose `reqId` is cached gets the cached original reply resent and nothing enqueued (no `opSeq` consumed); one in flight is dropped silently; otherwise it is decoded, enqueued and added to the in-flight set — on `EnqueueOp == false` it is answered `busy` and not cached. Cache, in-flight set and expected counter reset on rejoin.
**ADR Version**: 2026-10-16 (ADR-0007, ADR-0011)

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: None. Duplicates are a transport anomaly (the client never resends an applied op — HB10); this cache is defence in depth.

**Control Manifest Rules (Core — network seam)**:
- Required: dedupe per connection keyed `(peer, reqId u16)`: last-16 reply cache (resend original, enqueue nothing) + in-flight set (drop silently); otherwise decode, `EnqueueOp`, add to in-flight; on `false` reply `busy`, do not cache; reset on rejoin; cache is adapter memory outside the Sim dump.
- Forbidden: dedupe in the Sim or in any handler; storing `reqId` in Sim state; application-level retries of un-acked requests.
- Guardrail: dedupe cache 16 replies × ~32 B per peer, preallocated; 0 B per request.

---

## Acceptance Criteria

*From TR-delivery-011 and ADR-0007 §6 (decision table), scoped to this story:*

- [ ] **AC-1 Cached duplicate.** After request `reqId 7` has been applied and its `OpReply` delivered, a second request with `reqId 7` from the same peer receives a byte-identical copy of the original reply, enqueues nothing, and leaves the Sim's `opSeq` and canonical dump unchanged; the same `reqId 7` from a **different** peer is treated as new.
- [ ] **AC-2 In-flight duplicate and busy.** A duplicate of a request that is enqueued but not yet replied is dropped silently (no reply, nothing enqueued) and the original reply still arrives once; a request answered `busy` is not cached, so re-sending the same `reqId` after the ring drains is enqueued as a new attempt and applied once.
- [ ] **AC-3 Window and reset.** With 17 replied requests (`reqId` 1–17), `reqId` 1 is no longer cached (a new request with `reqId` 1 is enqueued) while 2–17 are; the u16 `reqId` wraps (65 535 → 0) without breaking the cache; `ResetConnection(peer)` (rejoin) clears that peer's cache and in-flight set.

---

## Implementation Notes

*Derived from ADR-0007 §6 and ADR-0011 §3:*

- `src/PerformativeMail.Client.Core/Net/ReplyCache.cs` — per peer: a 16-slot ring of `{ ushort reqId; byte length; fixed-size encoded reply bytes }` and an in-flight set (a small fixed array or 65 536-bit bitmap per peer — 8 KiB × 8 peers is acceptable; a 16–64 entry array with linear search is simpler given `pendingQueueMax` 16). All preallocated per peer slot.
- Hook points: `HostAdapter.PumpInbound` consults the cache before decode (Story 003's path), adds to in-flight after a successful `EnqueueOp`; `HostAdapter.Deliver` (Story 004) moves a `reqId` from in-flight to the cache when it copies that peer's `OpReply` (encode the reply bytes once — the same bytes are what gets sent).
- Replies to the `System` actor (audience 0) never reach the cache (no peer).
- The client side never retries except after `busy` (Story 006) — this cache is the only dedupe.

---

## Out of Scope

- Story 006: client `reqId` allocation (u16 wrapping) and the busy-only resend rule.
- Reply-timeout resync with `ackedReqId` — M0 (needs keyed snapshots).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/ReplyCacheTests.cs` (`[Trait("Id", "TR-delivery-011")]`) — must exist and pass

**Status**: [x] Created — tests/unit/net/ReplyCacheTests.cs (Trait Id TR-delivery-011) — Unit.Net 227/227 on four legs

---

## Dependencies

- Depends on: Story 003, Story 004; simulation-core Story 019 (dump hash for AC-1)
- Unlocks: Story 007, Story 009

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 cached duplicate byte-identical body / other peer new; AC-2 in-flight drop + busy not cached; AC-3 window of 16, u16 wrap, ResetConnection) — none deferred
**Deviations** (advisory, accepted by LP): (1) resend = bare reply (tag 0x01 + cached `ReplyCodec` body, the same bytes as the original batch OpReply record) sent at once on Reliable — AC-1 "byte-identical" verified on the body; (2) a dropped-but-encodable OpReply (full outbound buffer) is still cached and released (op applied → duplicate gets the reply); unencodable → released uncached + Debug assert `ReplyCacheEncodeFailed` (1005); the flush-time release was removed (single release point at queue time); (3) only in-flight reqIds are cached; (4) in-flight set 64/peer, 65th concurrent request → `busy` (ADR silent); (5) per-row orphan list (128): `ResetConnection`/admit reset move in-flight reqIds to orphans so a pre-reset op's reply (forwarded uncached, `OrphanReplyCount`) is never cached for a new same-reqId request; (6) cache lookup after decode; NetAssertCode 1004 `InFlightAddFailed`, 1005. Advisories logged as TD-066.
**Test Evidence**: Logic — tests/unit/net/ReplyCacheTests.cs (+ HostAdapterInboundTests 0 B test resets per cycle); Unit.Net 227/227 on net10/net8 × Debug/Release (re-verified net10 Debug and net8 Release on the real tree); full suite 2 704 / 0 failed / 17 skipped at net10 Debug.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (re-check APPROVE; also served as the .NET pass); QL-TEST-COVERAGE GAPS → required gaps fixed + orphan defect fixed → ADEQUATE.
