# Story 008: Thrown landings (`IMailApi.ResolveThrownLanding`, R13) and the hand → landing tick order

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~200 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R13 thrown landings; R6 path table and tick order; R4 closing grace for throws)
**Requirement**: `TR-mail-008` (one consume routine, fixed tick order = phase order), `TR-mail-013` (grace by `releaseTick`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §5 throw row); ADR-0015 (landing call contract: Reject or floor remainder → one WorldItem at the F8 drop point, created by Throwing); ADR-0003 (phase 0 before phase 4)
**ADR Decision Summary**: `ResolveThrownLanding(dest, in EntryRef, thrower, thrownPm, releaseTick)` is called by `Throwing.Step` in phase 4 in flight order, with `actionStartTick = releaseTick`, and goes through the same `MailConsumer.Consume`. Accept → every unit pays, with Cannon Arm from the snapshotted `thrownPm`. Misdeliver → every unit misdelivers, oldest first, until the wallet floor. Reject → nothing is consumed. The result tells Throwing what remains: a Reject entry or the floor remainder becomes one WorldItem, which Throwing creates (ADR-0015). R6's tick order (hand → thrown → feeders → NPC) is phase order, with no extra queue.
**ADR Version**: ADR-0017 2026-10-16; ADR-0015 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: every consume path goes through `MailConsumer.Consume` (ADR-0017)
- Required: units leave Flight only through `IInventoryApi`/`IMailApi` (ADR-0015)
- Forbidden: Mail creating the bounce WorldItem (Throwing owns it, ADR-0015); reading the thrower's live StatSheet at landing (use `thrownPm`)
- Guardrail: 0 B on the throw path (M-PERF-1b)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to the throw path (FX-W stub wallet; a Throwing caller stub that holds the entry in Flight):*

- [ ] **AC-1 (M-X-2a, M-F8-1b, M-R6-1a throw)** At a wrong house, wallet −440, 20 letters at 11 ¢ → 5 consumed, wallet −495, `LandingResult` reports 15 remaining with `stoppedAtFloor`; one `DeliveryOutcome` (`via` throw, `units` 5, `stoppedAtFloor` set). With wallet 0 and 4 ¢ letters, a 20-stack costs exactly 80 ¢ and all 20 are consumed (M-R8-1b).
- [ ] **AC-2 (M-F8-2)** One unit (spawn 8, penalty 4) then two units (spawn 9, penalty 5) thrown at wallet −492 → 1 consumed (−496), the second unit fails (−501 would breach), 2 units remain; raw complaint counts only the consumed unit (5,000 ‰).
- [ ] **AC-3 (M-X-2b, M-R7-1a, M-R16-1a/b)** Accept pays every unit with the passed `thrownPm` (1100 → Cannon Arm applies), also when the thrower's slot is no longer connected. A 20-letter thrown misdelivery raises 20 `MailConsumed` and exactly 1 `DeliveryOutcome` (`units` 20, `result` misdeliver, `intendedAddress` set, `outcomeSeq` = previous + 1, `actor` = the thrower).
- [ ] **AC-4 (M-X-2c, M-R6-2 thrown, M-R4-2d)** A Reject returns the whole entry to the caller (`LandingResult.Rejected`, units unchanged), with no wallet, complaint or stats change and no outcome. A throw released before close T that lands at T + 90 resolves as OPEN; one released at ≥ T is Rejected `closed`.
- [ ] **AC-5 (M-R6-3 a, b, d — hand and throw sub-tests)** One mailbox, one tick, wallet −496, 4 ¢ Misdeliver: a hand op (phase 0) and a thrown landing (phase 4) → the hand op consumes (−500) and the landing returns `wallet_floor` with everything remaining. Without the hand op, the landing consumes. At wallet 0, four one-letter Misdeliver ops at one door in one tick debit the door bucket 5,000 ‰ each in order and the wallet 4 ¢ each. (Feeder and NPC sub-cases of M-R6-3 a/c are M-1b and later: excluded, not failed.)

---

## Implementation Notes

*Derived from ADR-0017 §5 and ADR-0015's landing contract:*

- **Signature:** `LandingResult ResolveThrownLanding(EntityId dest, in EntryRef e, PlayerSlot thrower, int thrownPm, uint releaseTick)` (ADR-0017 Key Interfaces). `LandingResult { Kind (Accepted, Misdelivered, Rejected), UnitsConsumed, UnitsRemaining, StoppedAtFloor, MailRejectReason }`. Throwing (ADR-0015 `MAILBOX_*` receivers) turns `Rejected` or `UnitsRemaining > 0` into one WorldItem at its F8 drop point.
- Reuse story 007's routine: path = throw, `thrownPm` passed in, `actionStartTick = releaseTick`. The thrown misdeliver consumes the whole stack (no one-unit cap). The per-unit guard order is identical to the hand path.
- The Flight-held entry is consumed through `IInventoryApi.ConsumeUnit` on the Flight `EntryRef` (Flight is an Inventory ledger state, ADR-0009). Confirm the `EntryRef` form for Flight entries against `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md`. If Flight entries need a distinct ref, use that story's API and do not add a Mail-side path.
- Timeliness is read at the apply tick's shift (GDD R13: no shift boundary falls inside a flight plus grace).
- **Tick-order test:** run a real `SimWorld` with Mail, the stub wallet and stub caller systems bound to phase 0 (a test op handler calling `ConsumeHand`) and phase 4 (a test step calling `ResolveThrownLanding`). Do not simulate order by call sequence in one method: the point is that phase order is the order.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- throwing-catching epic: flights, catch volumes, the swept test, the bounce WorldItem and the arc colour (M-X-2d).
- Feeder (`ConsumeFed`) and NPC paths (M-1b, later).
- Story 015: wire encoding.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/ThrownLandingTests.cs` and `tests/integration/mail/ConsumeTickOrderTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007; `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (phases for the order test)
- Unlocks: throwing-catching mailbox landing story; Story 016 (outcome beats)
