# Story 006: Reply and verdict on the client (MD7) — the reply → hand-response table, the once-per-hold beat key, idle/late/resync rules (Sim-side published state)

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~200 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD7 reply and verdict on the client; States SENT/MUTED; M-EC-1)
**Requirement**: `TR-delivery-017` (hand beat once per hold, keyed `(actor, dest, applyTick)` from `DeliveryOutcome`), `TR-delivery-018` (reach echo starts only when the Sim-published `canStartHoldNext` was true)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0016: ClientLayer, Previews and Presentation Timing (primary: §5 predictive vs outcome beats, hand-beat key and 16-entry recent-key ring, reach-echo rule, `PresentationEventStream` cursors, `[OutcomeBeat]`)
**ADR Decision Summary**: Outcome beats start only from a cursor on `PresentationEventStream` reading the matching domain event (`DeliveryOutcome`, replies, `WalletChanged`, …). They may be delayed but never advanced. The hand beat fires once per hold, keyed `(actorSlot, destinationId, applyTick)`, via a 16-entry recent-key ring, so a hold's k `DeliveryOutcome`s fire one beat. The reach echo starts only if `CanStartHoldNext` was true in the last completed client tick's published state; Presentation never recomputes it.
**ADR Version**: ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used (Client.Core + Sim) | **Risk**: LOW
**Engine Notes**: None. The Godot views that consume this are story 011's.

**Control Manifest Rules (Feature + Presentation core)**:
- Required: outcome-beat components are tagged `[OutcomeBeat]` and fed only from `PresentationEventStream` cursors; predictive components never start an outcome beat (ADR-0016)
- Required: presentation time comes from `IRenderClock`; Godot `Timer`/`delta` are banned for gameplay-facing timing (ADR-0016, ADR-0002)
- Forbidden: a beat started from the predictive path; Presentation deriving `CanStartHoldNext`

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story (the beat controller is tested headless against a scripted event stream):*

- [ ] **AC-1 (M-MD7-1, 10 rows; data half)** Each reply maps to the hand response: `ok` (got = requested) → slot-in, plus the riffle if k ≥ 2; `ok` MISDELIVER_ONE → careful handover; `wallet_floor` → pieces-out + `deliver.floor_refusal`; `no_target` → pieces-out + `deliver.nothing`; `closed`/`not_on_route` → pieces-out + the `mail.reject.*` key; `out_of_range`/`position_stale` → "couldn't reach" key; `dry_land` → pieces-out + `deliver.dry_land`; `dead` → no hand beat; `frozen` → no hand beat; 5 s timeout → pending until the resync snapshot, then the "connection hiccup" key. (The reason strip's on-screen lifetime is HUD F1's, not asserted here.)
- [ ] **AC-2 (M-MD7-2)** A hold producing 3 `DeliveryOutcome` events fires exactly 1 hand beat. With no reply, the hand is not idle at tick 29 after completion and is idle at tick 30 while the published state keeps "Delivering…". A reply at tick 40 fires the verdict beat with no slot-in. A reply after a resync fires 0 hand beats. A reply while the carrier is beyond 2.9 m attaches no beat to the current prompt (the feed still receives it).
- [ ] **AC-3 (M-EC-1)** 26 matching entries, one hold: one request, `requested = got = 26`, the hand beat fires once, and the riffle descriptor shows 5 stacks (`min(k, 5)`) within 300 ms (F4 timing values as data).
- [ ] **AC-4 Reach echo gate (TR-delivery-018).** A press on a tick whose last completed published state had `CanStartHoldNext = false` produces no reach-echo start event; with `true` it does. An architecture test asserts that every `[OutcomeBeat]` type receives data only from `PresentationEventStream` cursors.

---

## Implementation Notes

*Derived from ADR-0016 §5:*

- **Files:** `src/PerformativeMail.Client.Core/Presentation/Delivery/HandBeatController.cs` (`[OutcomeBeat]`; a cursor over `DeliveryOutcome` + own replies; a 16-entry recent-key ring keyed `(actorSlot, destinationId, applyTick)`), `Presentation/Delivery/HandResponseTable.cs` (reply → `HandResponse { beat id, reason key, slotIn bool }`), `Presentation/Delivery/RiffleTiming.cs` (GDD F4: k = 1, 2, 5, 9, 26 → 0, 120, 300, 300, 300 ms; `min(k, 5)` stacks; content values). Outputs are plain descriptors (beat id + start render time) for the Godot views (story 011) to play.
- `PresentationEventStream` and `[OutcomeBeat]` are ADR-0016 §5's (Client.Core). If they do not exist yet, add them here exactly as ADR-0016 Key Interfaces shows; this is the first outcome-beat consumer. Mail's door beats (mail-destinations story 016) reuse them.
- The "1 s hand idle" and the late-reply rule use `IRenderClock`-independent **tick** counts where the GDD gives ticks (tick 30), so the test is deterministic.
- "Beyond 2.9 m" uses the carrier position published by Players and the anchor point. No Godot call.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 011: playing the beats, the reach echo animation, flap and lunge in Godot. Story 010: prompt and reason-strip text.
- Mail door beats and the "+N¢ · k stacks" pill (mail-destinations story 016; HUD).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/HandBeatControllerTests.cs` and `tests/unit/delivery/HandResponseTableTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003, Story 004, Story 005; `production/epics/mail-destinations/story-015-mail-event-wire-codecs.md` (`DeliveryOutcome` over loopback); `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`
- Unlocks: Story 011; `production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md` (shared beat stream)
