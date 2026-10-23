# Epic: Manual Delivery

> **Layer**: Feature
> **GDD**: design/gdd/manual-delivery.md (In Review — revision applied, awaiting re-review)
> **Architecture Module**: Sim.Delivery (`src/PerformativeMail.Sim/Delivery/`; `DeliverPreviewTracker` in Sim.ClientLayer `Previews`) + prompt render, reach/riffle and mailbox flap (Godot)
> **Milestone slice**: M-1a (the whole GDD is M-1a; GDD OQ14: the greybox needs MD1–MD5, MD7, F1–F2)
> **Engine Risk**: LOW (Sim). HIGH for the prompt's UI/live-region surface (NOT SOURCEABLE `accessibility_live`; HUD renders it).
> **Est. size**: M — 10–12 stories
> **Implementation priority**: 6 of 9
> **Status**: Ready
> **Stories**: 12 stories — 1 Complete, 9 Ready, 2 Deferred (counts refreshed 2026-10-23 from the story headers, m1a-replan; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

Manual Delivery is the carrier's hand at the slot. It is the certain verb beside throwing's risky one. It owns:
- the single Deliver verb on every mailbox;
- the address-match prompt that says exactly what one hold will do at this door;
- the rule that turns a completed hold into one `deliver_matching` request.

One hold at the right door posts every matching piece you carry. With no match, it posts exactly one piece of the held mail to the wrong house, and the prompt says so first. Otherwise nothing happens. A hold that started on a ✓ can never misdeliver. The epic connects four other systems: Player Character's interaction framework, Input & Controls' hold timing, Grid Inventory's op pipeline and Mail & Destinations' verdict.

**In scope (M-1a):**
- **MD1 — the Deliver verb:** one Deliver `IInteractable` per destination, registered at run start, priority 2, never deregistered.
- **MD2 — availability:** the ordered 6-reason availability check, with `deliver.pending` derived from the pending queue.
- **MD3 — the preview:** a pure function on the owning client in the ClientLayer `Previews` phase, using `Evaluate`, F1 figures in integer cents with one RH per unit, and an entry scan order (cursor → hotbar → inventory → backpack) that equals host consumption.
- **MD4 — press, latch and hold:** press-tick recompute; the hold latches `(dest, branch, entry, holdTicks)`; F2 `holdEff` ticks; a branch or selection change cancels.
- **MD5 — the request:** exactly one `deliver_matching` request (MATCH omits `selectedEntryId`; `holdStartTick` in the host domain via `IHostClock`).
- **MD6 — host handling:** the anchor check, and `reqId` duplicates returning the original reply.
- **MD7 — reply and verdict on the client:** the hand beat fires once per hold, keyed `(actor, dest, applyTick)`.
- **MD8 — delivery stance:** carry pose (presentation).
- **MD10:** the boundaries of what this system never does.
- **Prompt and telemetry:** prompt content supplied as data (localisation keys, no concatenation) to HUD; `latchedBranch` and `handShare` telemetry to the session stats file.
- **Tests:** the race suite (no misdelivery for a MATCH-latched hold across ticks ±2 and RTTs) and the 10 000-seed soak on the T1 harness.

**Out of scope:**
- the Quick Hands perk itself (MD9; Perks at M1). Only the `insertPm` stat path with its 0…750 clamp is honoured now;
- the live-region announcer (M4);
- the HUD prompt widget → HUD epic, not created.

**Why these ADRs govern it.**
- ADR-0011 fixes the packed `deliver_matching` (24/28 B incl. claimed position — erratum 2026-10-16, supersedes MD's 12/16 B), the host-domain `holdStartTick` and "no replicated state, JoinState 0 B".
- ADR-0007 makes the request idempotent through the adapter's `reqId` replay.
- ADR-0006 provides the race-suite harness.
- ADR-0009 owns the `deliver_matching` op handler and its oldest-first consumption.
- ADR-0003 fixes the per-tick client order: events and replies → focus → preview → Interact edge.

**Risks.**
- (1) **18 of 24 TRs are untraced.** They fall under ADR-0014 (interaction and hold), ADR-0016 (ClientLayer and presentation timing), ADR-0018 (HUD composition), ADR-0019 (scenes) and ADR-0024 (persistence and telemetry). The M-1a-critical ones are ADR-0014 and ADR-0016. **Recommended:** write them before `/create-stories manual-delivery`, since they are shared with `player-character` and `throwing-catching`.
- (2) No HUD epic exists yet. The prompt render has no home until `/create-epics hud-notifications` (Presentation) runs.
- (3) M-1a provisional (OQ14).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0011: Network Adapter Boundary | packed `deliver_matching`, `holdStartTick` via `IHostClock`, host anchor check, no replicated state | LOW (seam) |
| ADR-0007: Op Queue, Drain and Replies | idempotent u16 `reqId`, duplicate → original reply (adapter) | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | race suite + 10 000-seed soak via T1 harness and replay | LOW |
| ADR-0009: InventoryOp Pipeline and Inventory Storage Layout | `deliver_matching` handler, validation precedence, oldest-first | LOW |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | `InputsApply` order; sync `I…Api` into Mail inside the handler's phase | LOW |
| *ADR-0014 (planned)*: Interaction framework & hold timing | anchor registration, availability, `ShouldCancel`/`GetHoldTicks`, `holdEff` with `insertPm` | — **missing** |
| *ADR-0016 (planned)*: ClientLayer, shared pure functions & presentation timing | `Previews` phase, `DeliverPreviewTracker` 6-tick settle, client/host `Evaluate` equality, reach echo | — **missing** |
| *ADR-0018 / 0019 / 0024 (planned)* | HUD prompt data + live region; flap/lunge nodes; telemetry file | — **missing** |

## GDD Requirements

Source: architecture.md v1.1 Appendix A. **6 / 24 traced.**

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-delivery-001 | One Deliver IInteractable per destination at run start; priority 2; never deregistered | ❌ No ADR (planned ADR-0014, not written) | M-1a |
| TR-delivery-002 | Ordered 6-reason availability; deliver.pending derived from pending queue | ❌ No ADR (planned ADR-0014, not written) | M-1a |
| TR-delivery-003 | Deliver preview pure function every focused tick in ClientLayer Previews phase | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-004 | DeliverPreviewTracker 6-tick settle counts Sim ticks | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-005 | Per-tick client order: events/replies → focus → preview → resolve Interact edge | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-006 | Press-tick preview recompute; hold latches (dest,branch,entry,holdTicks) | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-007 | ShouldCancel/GetHoldTicks hooks; branch or selection change cancels | ❌ No ADR (planned ADR-0014, not written) | M-1a |
| TR-delivery-008 | deliver_matching: MATCH omits selectedEntryId; 24/28 B packed incl. claimed position (revised 2026-10-16) | ADR-0011 ✅ | M-1a (loopback codec) |
| TR-delivery-009 | holdStartTick in host domain via IHostClock; host clamps window | ADR-0011 ✅ | M-1a |
| TR-delivery-010 | Host check: mailboxId is a registered Deliver anchor; no throttle | ADR-0011 ✅ | M-1a |
| TR-delivery-011 | Idempotent reqId u16; duplicate returns original reply | ADR-0007 ✅ | M-1a |
| TR-delivery-012 | Client Evaluate/F10 equal host results over 1,000 seeded pairs | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-013 | Preview money in integer cents, single RH per unit | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-014 | Entry scan order cursor→hotbar→inventory→backpack equals host consumption | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-015 | holdEff with Quick Hands insertPm 500; DeliverInsertPm clamp 0…750 | ❌ No ADR (planned ADR-0014, not written) | M-1a |
| TR-delivery-016 | No host state, no replicated state; JoinState 0 B | ADR-0011 ✅ | M-1a |
| TR-delivery-017 | Hand beat once per hold keyed (actor,dest,applyTick) from DeliveryOutcome | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-018 | Reach echo starts only when Sim-published canStartHoldNext was true | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-delivery-019 | Prompt content as data from Sim; HUD renders; localisation keys, no concatenation | ❌ No ADR (planned ADR-0018, not written) | M-1a |
| TR-delivery-020 | Live-region announcements policy; accessibility_live API unsourced | ❌ No ADR (planned ADR-0018, not written) | M4 announcer; schema M-1a |
| TR-delivery-021 | Race suite: no misdeliver for MATCH-latched hold across ticks ±2 and RTTs; 10,000-seed soak | ADR-0006 ✅ | M-1a |
| TR-delivery-022 | Telemetry latchedBranch; handShare ≥20% | ❌ No ADR (planned ADR-0024, not written) | M-1a (session stats file) |
| TR-delivery-023 | Mailbox flap separate node within 150-tri budget; reach layer scrubbed by hold progress | ❌ No ADR (planned ADR-0019, not written) | M-1a |
| TR-delivery-024 | Visual-only lunge ≤0.8 m via rig offset, never feeds Sim | ❌ No ADR (planned ADR-0019, not written) | M-1a |

**Untraced (18):**
- ADR-0014: TR-delivery-001, 002, 007, 015;
- ADR-0016: TR-delivery-003 through 006, 012, 013, 014, 017, 018;
- ADR-0018: TR-delivery-019, 020;
- ADR-0024: TR-delivery-022;
- ADR-0019: TR-delivery-023, 024.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/manual-delivery.md` are verified (all are M-1a), except MD9's perk-grant rows (M1) and the live-region announcer (M4)
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/manual-delivery/**`, `tests/integration/**`), including the race suite and the 10 000-seed soak
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (prompt branches ✓ / ✗ / nothing, reach echo, flap), with a lead sign-off for Visual/Feel

## Stories

*ADR-0014 and ADR-0016 are now Accepted (2026-10-16), which resolves risk (1) for TR-delivery-001…007 and 012…018. The 2026-10-16 `/architecture-review` moved the `DeliverMatching` OpKind and handler from Inventory to Delivery (ADR-0009 erratum: block `0x0100–0x013F`, handler in `Sim/Delivery`; ADR-0011 erratum: 24 B MATCH / 28 B MISDELIVER_ONE on the wire, including the claimed position). That is story 005. `production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md` depends on it. Story 005 uses grid-inventory 022's `HostPanelView` (`production/epics/grid-inventory/story-022-deliver-matching-handler.md`; TD-172 D1, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat)), so the order is 002 → 022 → 005 (corrected 2026-10-23, Mail Review PR #46 r1 B1).*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Deliver anchors (MD1) + `GetHoldTicks` via `holdEff` (F2, MD9)](story-001-deliver-anchors-hold-ticks.md) | Logic | Complete | ADR-0014 |
| 002 | [`DeliverScan` + pure preview (MD3) + F1 figures](story-002-deliver-scan-preview-f1.md) | Logic | Ready | ADR-0016, ADR-0017 |
| 003 | [Availability (MD2), `DeliverPreview` step, settle tracker](story-003-availability-preview-step-tracker.md) | Logic | Ready | ADR-0016, ADR-0014 |
| 004 | [Press, latch, cancels, request build (MD4, MD5 client)](story-004-press-latch-cancels-request.md) | Logic | Ready | ADR-0014, ADR-0016 |
| 005 | [**`DeliverMatching` op (24 B; wire 24/28 B) + host handler**](story-005-delivermatching-op-and-handler.md) | Integration | Ready | ADR-0009 (errata), ADR-0017, ADR-0014, ADR-0011 |
| 006 | [Reply/verdict table + once-per-hold hand-beat key (MD7)](story-006-reply-verdict-hand-beat-keys.md) | Logic | Ready | ADR-0016 |
| 007 | [End-to-end over loopback: parity, network, edge cases](story-007-integration-loopback-parity-edge-cases.md) | Integration | Ready | ADR-0016, ADR-0011 |
| 008 | [Race suite, bell fuzz, 10,000-seed soak](story-008-race-suite-bell-fuzz-soak.md) | Integration | Deferred (Evan 2026-10-23 8:54 AM PT) | ADR-0006 |
| 009 | [Telemetry `latchedBranch` / `handShare`](story-009-telemetry-latched-branch-handshare.md) | Logic | Ready | ADR-0024 (M-1a slice) |
| 010 | [Prompt content as data (`PromptArgs` key + typed args; narrowed 2026-10-23)](story-010-prompt-content-as-data.md) | Logic | Ready | ADR-0014 |
| 011 | [Hand presentation: flap, reach, lunge, echo, riffle, stance](story-011-hand-presentation-flap-reach-lunge-stance.md) | Visual/Feel | Ready | ADR-0019, ADR-0016 |
| 012 | [Deliver prompt accessibility and localisation — the screen-reader settle gate (M-MD4-1e), ](story-012-deferred-prompt-accessibility-localisation.md) | Logic | Deferred (Evan 2026-10-23 8:54 AM PT) | — |

**Recommended order:** 001 → 002 → 005 → 003 → 004 → 006 → 007 → 008; → 011 (after the godot-host-shell stories, `production/epics/player-character/story-022-playerview-scene-render-pose.md` and `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md`); 009/010 when their ADRs are written.

**Untraced / blocked requirements:** TR-delivery-019, 020 (ADR-0018 not written, story 010); TR-delivery-022 (ADR-0024 not written, story 009). No new requirement was needed. The GDD's M-NET-1 "12 B / 16 B" is superseded by TR-delivery-008 (revised; story 007 AC-4 asserts 24/28 B). HUD rendering ACs (M-MD2-3, M-MD2-4, M-UI-2, M-UI-3) have no home until a HUD epic exists (risk 2).

**Notes for `/architecture-review`:** grid-inventory R7's "one version bump per touched container" conflicts with per-unit `ConsumeUnit` (ADR-0017 §5 + ADR-0009 §3); story 005 asserts the ADR behaviour. Story 005 uses `IInventoryApi.HostPanelView` from grid-inventory 022 (re-scoped 2026-10-23; the API is TD-172 D1, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat)); it adds no accessor of its own.

## Open Questions

- **OQ-MD001-1 — Resolved 2026-10-22 (Evan, 2026-10-22 7:13 PM PT, Mail Dev chat: "Keep the verb catalogue as the source (recommended)"; decision log 2026-10-22, "Deliver's priority comes from the Players `VerbCatalogue`"): Deliver's priority comes from the `VerbCatalogue` code table, as story 001 built it; no content field.** Originally: does "priority read from Player Character's data" (GDD / control manifest) mean the Players verb catalogue (`VerbCatalogue`) code table? No content field holds a priority, so story 001 takes it from the catalogue via `AnchorDef.For(..., Verb.Deliver)` (Deliver = `VerbCatalogue.DefaultPriority(Verb.PickUp) + 1`) and its test asserts that. Raised in story 001's Completion Notes (PR #14). If the answer is a content field, story 001's anchor registration and test change.
- **OQ-MD005-1:** see story 005 AC-7 (registry access) — open.

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): Track A (playable greybox): 002 (A6), 003 (A7), 005 (A8), 004 (A9), 006 (A10), 010 (A10), 011 (A12); then Track B: 009 (B1), 007 (B2); Deferred by Evan 2026-10-23 (not scheduled): 008, 012. Earlier notes below are history.

Story 001 is Complete (PR #14; its Completion Notes' open question is tagged OQ-MD001-1). Next: `/story-readiness` then `/dev-story` on the next story in the recommended order whose dependencies are Complete (002 waits on mail-destinations 010).
