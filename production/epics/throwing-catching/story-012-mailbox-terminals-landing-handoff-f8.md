# Story 012: Mailbox terminals — `ResolveThrownLanding` hand-off, MAILBOX_ACCEPT / MISDELIVER / REJECT and the F8 reject / remainder drop point

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines prod + ~400 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Flight resolution story (2 of 3).** The heart of the game's comedy: the stack lands in a mailbox and the *right* box pays for every piece while the *wrong* one misdelivers every piece. Mail & Destinations decides acceptance, money and complaint; this story owns the landing test result, the hand-off and the bounce WorldItem.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 receiver rows "Mailbox catch volume, destination OPEN …" and "… Reject"; T11 phase boundaries; F8 reject / remainder drop point; Edge Cases "If the wallet is exactly −500 when a Misdeliver lands", "If two thrown Misdelivers hit one wallet in one tick", "If a hand op, a landing and a feeder hit one mailbox in one tick")
**Requirement**: `TR-throwing-013` (one terminal; units conserved through `IMailApi`), `TR-throwing-006` (mailbox rank 1, descending entry), `TR-throwing-001` (`releaseTick` judged, not landing tick)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T6-2 b, TC-F8-1 a–e, TC-X-2 a–c, TC-E-3 a/b, TC-T11-1 a–c.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§5 terminal table rows `MAILBOX_*`; §2 F8 uses `Throw.RH`) — Accepted 2026-10-16 (primary); ADR-0017: Mail Table, Destination Registry and Wallet Port (§5 throw row `ResolveThrownLanding`, `LandingResult`) and ADR-0003 (phase 0 hand ops before phase 4 landings) secondary — Accepted
**ADR Decision Summary**: A mailbox contact calls `IMailApi.ResolveThrownLanding(destinationId, in EntryRef, thrower, thrownPm, releaseTick)` synchronously in phase 4. Accept pays every unit with the snapshotted `thrownPm`; Misdeliver consumes every unit oldest-first until the wallet floor; Reject consumes nothing. The `LandingResult` tells Throwing what remains: a Reject entry or the floor remainder becomes **one WorldItem at the F8 drop point, created by Throwing** (Mail never creates the bounce). Mail judges the closing grace from `releaseTick`. The thrown path is `MailConsumer.Consume` with `actionStartTick = releaseTick`; tick order hand → landing is phase order, no extra queue.
**ADR Version**: ADR-0015 2026-10-16; ADR-0017 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: units leave Flight only through `IInventoryApi`/`IMailApi`; every consume path goes through `MailConsumer.Consume`; the wallet is reached only through Mail's `IWalletGate` (ADR-0015, ADR-0017)
- Required: F8 rounding is `Throw.RH`; `FlightResolved` raised before the domain events it causes
- Forbidden: a second payment or penalty path; Mail creating the bounce WorldItem; reading the thrower's live StatSheet at landing (use `thrownPm`); Inventory calling Mail
- Guardrail: 0 B on the throw path (Mail M-PERF-1b)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T6-2 b, TC-F8-1, TC-X-2, TC-E-3, TC-T11-1), scoped to this story. Fixtures FX-Flat, FX-Wal, FX-L20:*

- [ ] **TC-X-2 a.** A 20-letter FX-L20 stack thrown into its **matching** open mailbox calls `ResolveThrownLanding` exactly once; terminal MAILBOX_ACCEPT; all 20 consumed; Mail pays 240¢ with `actorId` = the thrower (also when the thrower has disconnected); no WorldItem is created. (+260¢ at `thrownPm` 1100 is the Later vector, non-gating.)
- [ ] **TC-X-2 b / TC-E-3.** The same stack into a **wrong** house at wallet −440: terminal MAILBOX_MISDELIVER, 10 units consumed (wallet −500), the other 10 become **one** WorldItem at the F8 point (`origin = Thrown`, 300 s timer); one `DeliveryOutcome`. At wallet −500 (FX-Wal): 0 consumed, one WorldItem for all 20 at the F8 point, `DeliveryOutcome{units 0, stoppedAtFloor}` is raised by Mail at most once per door and thrower per 30 ticks, no complaint, no `CursedMisdelivered`.
- [ ] **TC-E-3 b.** Two Misdelivers into one wallet in one tick resolve in creation order; the second may consume 0 and its whole entry becomes one WorldItem at the F8 point.
- [ ] **TC-T6-2 b (Reject, parametrised: closed, LOCKED, non-mail).** terminal MAILBOX_REJECT; the whole entry becomes one WorldItem at the F8 point with the 300 s timer; **0** wallet calls, **0** complaints (spy on `IWalletGate` and the complaint buckets); the reject reason comes from `LandingResult.MailRejectReason`.
- [ ] **TC-X-2 c / TC-T11-1.** Deliveries close at tick T. A flight released at T−1 landing at T+89 (and, by injection, T+90) is judged OPEN; landing at T+91 (injected) is `closed` → Reject. A flight released at T lands `closed` → Reject. A flight released in Prep at S−10 landing at S+10 (deliveries open at S) is `closed`. The judgement is by `releaseTick` passed to Mail, never by the landing tick.
- [ ] **TC-F8-1 (drop point).** C (10250, 944, 10500), vel (0, 2911, −13695), `dropPushbackMm` 500 → drop (10250, 0, 11000). **b** C.z = 10 050 → pushed in 100 mm steps to z 10 650 (the first step satisfying `max(|dx|, |dz|) ≥ 600` from the volume centre). **c** A 45° entry at the volume's corner ends with `max(|dx|, |dz|) ≥ 600`. **d** `|vxz| < 1000` → the push direction is the mailbox's outward front normal (`Lots[i].Face`). **e** No walkable point within 1000 mm → the mailbox base front point; the result is always a walkable point outside the mailbox footprint, so a re-grab is a normal Pick up, never a second landing.
- [ ] **Hand → landing order.** One mailbox, one tick, wallet −496, a 4 ¢ Misdeliver: a hand deliver op (phase 0) and a thrown landing (phase 4) on the same door → the hand op consumes (−500), the landing returns `wallet_floor` with the whole entry remaining → one WorldItem at the F8 point; without the hand op the landing consumes (real `SimWorld`, phase order — not call order).
- [ ] **Conservation.** For every case, `unitsConsumed + unitsInWorldItem = N`; the Flight ledger column returns to 0; the tick-end record (story 008) and Grid's ledger check stay green.
- [ ] **Event order.** For a landing the log order is `FlightResolved`, then Mail's `MailConsumed`/`DeliveryOutcome` (and the WorldItem spawn), all in one call scope; `FlightResolved.receiver` is the destination's `EntityId`, `unitsIn/unitsFumbled` are 0 (catch-only fields).
- [ ] **Descending only.** A rising segment through the volume does not land (story 002); this story asserts end to end that a flat lob over a nearer mailbox lands in the farther one.

---

## Implementation Notes

*Derived from ADR-0015 §5, ADR-0017 §5 and Mail story 008:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `MailboxLanding.cs` (`IMailboxLanding` implementation; maps `LandingResult.Kind` Accepted/Misdelivered/Rejected → `Terminal`), `DropPoint.cs` (`static bool F8(...)` pure: inputs C, vel, volume centre/footprint, front normal, `dropPushbackMm`, `IWorldQuery`), wired by `TerminalResolver` (story 011).
- `LandingResult` and the call signature are Mail story 008's (`LandingResult ResolveThrownLanding(EntityId dest, in EntryRef e, PlayerSlot thrower, int thrownPm, uint releaseTick)`; `LandingResult { Kind, UnitsConsumed, UnitsRemaining, StoppedAtFloor, MailRejectReason }`). The Flight-held `EntryRef` form is the open point Mail story 008 flags: use the handle story 008 (this epic) defines and confirm it against `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md`; Mail consumes units through `IInventoryApi.ConsumeUnit` on that ref — Throwing never consumes units itself.
- After the call, if `UnitsRemaining > 0`, create the single WorldItem for the remaining units with `CreateWorldItem(...)` at the F8 point; the remaining units are the **newest** (Mail consumes oldest-first).
- F8: `u = (vx, vz)/|vxz|` in ‰ via `Throw.RH` (`|vxz| = ISqrt(vx² + vz²)`); `D = C − RH(dropPushbackMm × u, 1000)`; step 100 mm along −u until `max(|D.x − V.x|, |D.z − V.z|) ≥ 600` (V = volume centre); then `TrySnapWalkable(ClampInsideMap(D, 1000), 1000)` else the mailbox base front point (anchor − pushback along the front normal). The returned y is the terrain height.
- `releaseTick` and `thrownPm` come from the `FlightRecord` (story 010); `openAtRelease` is not an argument to Mail.
- The wallet floor, complaint buckets and `DeliveryOutcome` rate limit are Mail's; assert them through Mail's public behaviour only.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Mail & Destinations: acceptance rule, pay, penalty, complaint, `DeliveryOutcome` rate limit (`production/epics/mail-destinations/story-007-consume-routine-hand-path-outcomes.md`, `story-008-thrown-landing-and-tick-order.md`).
- The mailbox flag/wobble/resident beats (`production/epics/mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md`). Story 011: non-mailbox terminals. Story 013: run-end flush (a mailbox below a flushed flight does not count).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/throwing/MailboxLandingTests.cs` and `tests/unit/throwing/DropPointTests.cs` (`[Trait("Id", "TC-X-2")]`, `TC-E-3`, `TC-T6-2b`, `TC-T11-1`, `TC-F8-1`) — must exist and pass on net10.0 and net8.0, with the real Mail system and `StubWallet`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 003, 008, 010, 011; `production/epics/mail-destinations/story-008-thrown-landing-and-tick-order.md` (`ResolveThrownLanding`); `production/epics/mail-destinations/story-005-walletgate-stubwallet-mail-system.md`; `production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md`; `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (`Lots[i].Face`, destination ids)
- Unlocks: Stories 013, 014, 015
