# Story 007: End-to-end over loopback — focus vs Pick-up, preview order = host order, client/host `Evaluate` parity (M-INT-2), network rules and edge cases

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~100 lines glue + ~400 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-21 (AC-7 added: the M-MD1-1a run-cycle leg handed over from story 001 — Deliver anchors survive the M-1a shift-stub cycle; depends on mail-destinations 009. Manifest stamp not re-reviewed in this pass)

## Context

**GDD**: `design/gdd/manual-delivery.md` (Network; Cross-system integration; Edge cases; MD1 focus bias)
**Requirement**: `TR-delivery-012` (client `Evaluate`/F10 equal host results over 1,000 seeded pairs), `TR-delivery-011` (idempotent `reqId` u16; a duplicate returns the original reply), `TR-delivery-016` (no host state, no replicated state; JoinState 0 B), `TR-delivery-014` (preview scan order equals host consumption)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0016 (primary: §3 compiled-once + facts; M-INT-2 validation); ADR-0011 (loopback = remote path; dedupe per `(peer, reqId)`; rejoin resets); ADR-0007 (duplicate → original reply, adapter-side); ADR-0014 (§2 focus score with Priority)
**ADR Decision Summary**: The host's own client uses `ClientAdapter` over `LoopbackTransport` to its own `HostAdapter`: same codec, `reqId`, dedupe and ordering as a remote peer. Dedupe is per connection, keyed `(peer, reqId u16)`, with a last-16 reply cache that resends the original and enqueues nothing; it resets on rejoin. Shared rule functions take facts; M-INT-2 proves host and client facts paths agree.
**ADR Version**: ADR-0016 2026-10-16; ADR-0011 2026-10-16; ADR-0007 2026-10-16; ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Global)**:
- Required: one code path; loopback uses the remote codec; host and client run the same pure functions (Global, ADR-0011 §2)
- Forbidden: an "is host" shortcut; dedupe in the Sim (ADR-0011)

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story (host loopback session, the M-1a street or FX-MB):*

- [ ] **AC-1 (M-MD1-1b)** A parcel lying at the foot of #17; the carrier faces the door from 1.5 m → focus is Deliver; looking down at the parcel → focus is Pick-up. Positions, angles and the expected F4 scores (from Player Character's data) are asserted.
- [ ] **AC-2 (M-INT-1)** With matching entries at the cursor, hotbar slot 2, inventory (r0, c1) and the backpack, the preview's entry order equals the host's `DeliveryOutcome` order: cursor → hotbar → inventory row-major → backpack.
- [ ] **AC-3 (M-INT-2)** Over 1,000 seeded (destination state × unit) pairs including CLOSED, LOCKED and `wont_fit`, the client `Evaluate` (facts from mirrors) equals the host's every time. With no lag, `predictedPay` equals the summed outcome pay. With a StatSheet one event stale, the outcome figure replaces the prediction.
- [ ] **AC-4 (M-NET-1, layout per the ADR-0011 erratum)** From press to completion the only message is the request (no hold-start, progress or posting packet). The serialised request is 24 B (MATCH) and 28 B (MISDELIVER_ONE) at maximum field values, transport framing excluded (supersedes the GDD's 12/16 B). The JoinState/RejoinState diff with and without this system is 0 B.
- [ ] **AC-5 (M-NET-2)** A client that disconnects after sending and rejoins has its pending state cleared, `deliver.pending` false, and missed outcomes not replayed. A duplicate `reqId` gets the original reply and consumes nothing more. The host's own hold gets its reply in the client layer on the same or the next tick, with an exact `holdStartTick`.
- [ ] **AC-6 (M-EC-2, M-EC-3a, M-EC-4, M-MD4-4 end to end)** A knockdown on the completion tick: request first → success and the rest spills; knockdown first → `dead`, no hand beat (both orders run). At wallet −496 a teammate spend to −497 mid MISDELIVER_ONE hold: the hold continues, the published preview shows `floorRefusal` within 1 tick, the host replies `wallet_floor` with 0 consumed. A teammate's throw arriving mid-hold leaves the panel unchanged and the hold completes. A 5-tick catch-up never shows progress above `heldTicks/holdTicks`. A Cursed MISDELIVER_ONE stopped at the floor with 0 consumed emits no `CursedMisdelivered`. Toggle mode: walking beyond 2.9 m cancels with 0 requests; a completion at 2.7 m is accepted by the host.
- [ ] **AC-7 (M-MD1-1a, run-cycle leg — handed over from story 001)** On a composed M-1a world (story 005's composition case), every Deliver anchor registered at run start is still registered, with an unchanged `AnchorDef` (ref, point, verb, mode, hold base, priority), at the end of the run cycle. At M-1a the run cycle is the shift stub's Prep → Delivery → Closed (`production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md`; there is no Results phase until Run Structure at M1, so "Prep to Results" is asserted as Prep through Closed and re-pointed when Run Structure lands). The check reads `PlayersSystem.Anchors` (`Count`, `TryGet(AnchorRef, out index)`, `DefAt(index)`) on the first Prep tick, every phase transition and 60 ticks into Closed: all 10 present every time and `Count` never drops.

---

## Implementation Notes

*Derived from ADR-0011 §2–§4 and ADR-0016 §3:*

- Build the fixture on `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`'s `HostSession`: `SimWorld` (World, Inventory, Mail, Delivery, Players, shift stub) + `ClientLayer` with the Delivery client steps + loopback adapters + the `DeliverMatching` codec (network story 008).
- M-INT-2's pair generator uses a seeded PCG32 stream from the test (never `System.Random`) and prints the failing seed. Host facts come from the registry/table, client facts from mirrors (mail-destinations story 010), and both go through `MailRules.Evaluate`.
- M-EC-4b's Cursed half only needs a test `cursed` flag on a letter; Cursed spawning is M4.
- Remote rejoin uses the loopback "disconnect → rejoin" path the network epic provides. If rejoin is not available at M-1a, assert the host-own and duplicate cases now and mark the rejoin sub-case BLOCKED (not failed) with a pointer to the network story.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: race suite, bell fuzz and the 10,000-seed soak (M-FX-1a, M-MD6-4).
- Wire byte budgets at M0 scale (Mail M-NET-4).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/delivery/DeliverLoopbackTests.cs`, `tests/integration/delivery/EvaluateParityTests.cs` and `tests/integration/delivery/DeliverAnchorLifetimeTests.cs` (`[Trait("Id", "M-MD1-1a")]`, AC-7 run-cycle leg) — must exist and pass on net10.0 and net8.0, Debug and Release

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001–006; `production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md` (the M-1a Prep → Delivery → Closed cycle, AC-7); `production/epics/network-adapter-loopback/story-005-reqid-dedupe-reply-cache.md`, `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`, `production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md`; `production/epics/player-character/story-014-focus-scoring-dwell-preview-binding.md`, `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md`; `production/epics/mail-destinations/story-015-mail-event-wire-codecs.md`
- Unlocks: Story 008; `production/epics/network-adapter-loopback/story-009-sc-x-2-manual-delivery-over-loopback.md`
