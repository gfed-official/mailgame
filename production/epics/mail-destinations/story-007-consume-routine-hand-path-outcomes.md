# Story 007: One consume routine (`MailConsumer.Consume`) and the hand path (`IMailApi.ConsumeHand`) — per-unit pay/penalty/floor, `MailConsumed`, `DeliveryOutcome`, `misdeliveriesThisShift`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: L-ish M (~1 day; ~350 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R6 consumption; R7/R8 per-unit money and floor; R9 complaint per op; R16 events and statistics)
**Requirement**: `TR-mail-008` (one consume routine for hand/throw/feeder/NPC in fixed tick order), `TR-mail-015` (`DeliveryOutcome` one event per op, canonical encoding; Sim payload here), `TR-mail-013` (closing grace from `holdStartTick`), `TR-mail-019` (0 B on the hand path; advisory ms)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §5, §9); ADR-0008 (event ≤ 48 B, Replicated class, events before the reply); ADR-0009 (`IInventoryApi.ConsumeUnit`, oldest unit first); ADR-0003 (sync `I…Api` calls in the caller's phase)
**ADR Decision Summary**: Every path enters `MailConsumer.Consume(in ConsumeCall c)` (internal to `Sim/Mail`) through an `IMailApi` entry point called by the owning system in its own phase. The hand path is `ConsumeHand(dest, ReadOnlySpan<EntryRef> inScanOrder, actor, holdStartTick)`, called from Delivery's `deliver_matching` handler in phase 0 (`opSeq` order) with `actionStartTick = holdStartTick`. Per unit, oldest first: build facts with the effective state (OPEN or within grace); `Evaluate`; **Accept** → `IInventoryApi.ConsumeUnit`, `IWalletGate.Credit(pay, DeliveryPay)`, retire the row; **Misdeliver** → `TryDebitPenalty(penalty, walletFloor)`, and on `false` stop and set `stoppedAtFloor`, else consume and update the complaint buckets; **Reject** → nothing consumed. `MailConsumed` (Internal) is raised per unit; one `DeliveryOutcome` (Replicated, 34 B Sim payload) per call per result group; a hold's k outcomes get contiguous `outcomeSeq`.
**ADR Version**: ADR-0017 2026-10-16; ADR-0008 2026-10-16; ADR-0009 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: every consume path goes through `MailConsumer.Consume`; there is no second payment or penalty path (ADR-0017)
- Required: every mutating `I…Api` method starts with `TickGuard.AssertMutable()` and opens `using var scope = log.BeginCall();`, is one atomic mutation, raises events on commit and returns no reply (ADR-0003 §5, ADR-0008 §5)
- Required: inventory interaction goes only through `IInventoryApi` (ADR-0009 §6)
- Forbidden: Inventory calling Mail; Mail reading the wallet balance; one rounding over a stack total
- Guardrail: a k-unit entry → exactly k `ConsumeUnit` calls, ≤ k wallet calls, 1 outcome; 0 B on the hand path (M-PERF-1b); a 40-unit op ≤ 0.2 ms (advisory)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to the hand path (fixture: FX-W stub wallet, FX-L letter 8 ¢ / penalty 4 ¢, an `IInventoryApi` stub or the real Inventory):*

- [ ] **AC-1 (M-R6-1a hand, M-X-1c)** A 20-letter entry for the wrong house via `ConsumeHand` consumes exactly 1 unit, the lowest mailId.
- [ ] **AC-2 (M-R6-1b)** `ConsumeHand` over 4 matching entries in scan order → 4 ops, 4 `DeliveryOutcome`s in that order, all in one tick, with contiguous `outcomeSeq` and 0 misdeliveries.
- [ ] **AC-3 (M-R6-2 hand, M-R9-2a)** A Reject (`closed`) consumes nothing, makes no `IInventoryApi`, wallet or complaint call, raises no `DeliveryOutcome` (Rejects reply to the requester only), and the returned summary counts the entry as not got with reason `Closed`.
- [ ] **AC-4 (M-F8-1c, M-X-4, M-R9-2b)** At wallet −496 a hand misdeliver of a 4 ¢ letter consumes and leaves −500; the next one returns `stoppedAtFloor` with 0 units, makes exactly one refused `TryDebitPenalty`, raises no `ComplaintRaised` and does not count in `misdeliveriesThisShift`. Across all ACs, the wallet receives exactly one `TryDebitPenalty` per misdelivered unit attempted and one `Credit` per paid unit, all in the consuming tick.
- [ ] **AC-5 (M-R16-1b/d, R16 floor broadcast)** `DeliveryOutcome.actor` = the requesting player; `via` = hand. A zero-unit floor stop is broadcast with `stoppedAtFloor`, at most once per (destination, actor) per 30 ticks. `misdeliveriesThisShift` increments once per op that consumed ≥ 1 Misdeliver unit.
- [ ] **AC-6 (M-R6-4, M-R4-2c hand)** Two players' `ConsumeHand` calls at one mailbox in one tick both succeed sequentially in call order, each against their own entries. A hold started at T − 1 (before close at T) and applied at T + 90 is accepted; applied at T + 91 it is Rejected `closed`.
- [ ] **AC-7 (M-PERF-1b)** A 40-unit Accept entry makes exactly 40 `ConsumeUnit` calls, 40 `Credit` calls and 1 outcome, and the hand path allocates 0 B after warm-up.

---

## Implementation Notes

*Derived from ADR-0017 §5, §9 and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Mail/MailConsumer.cs` (internal; the one routine), `ConsumeCall.cs` (`readonly ref struct` or struct: path, destination ordinal, entry ref, actor, `actionStartTick?`, `thrownPm`, apply tick), `Events/DeliveryOutcome.cs`, `Events/MailConsumed.cs`.
- **Hand misdeliver = exactly one unit per op** (GDD R6 table, OQ4); Accept = the whole entry; Reject = nothing. `ConsumeHand` needs no mode flag: a MATCH entry evaluates to Accept (same address), and the single `MISDELIVER_ONE` entry evaluates to Misdeliver.
- **Return value (story-level, flagged):** ADR-0017 Key Interfaces declares `void ConsumeHand(...)`, but Delivery's handler needs `requested`/`got`, the first reject reason and `stoppedAtFloor` to build its reply. Return a `HandConsumeSummary` readonly struct (`EntriesGot`, `UnitsConsumed`, `FirstReject`, `StoppedAtFloor`, `CentsDelta`). This is a return-type addition, not a semantic change. It is recorded in the decision log for an ADR-0017 erratum at the next `/architecture-review`.
- **Facts:** host `DestinationFacts` come from the registry + the deliveries state store (story 009) through story 003's `Effective(..., actionStartTick: holdStartTick, applyTick: now)`. Host `UnitFacts` come from the mail table row (story 001) for the entry's lowest mailId. All units of one entry share kind and address, so `Evaluate` runs once per entry and pay/penalty run per unit (spawn values can differ by spawn shift).
- **Per-unit loop (oldest first):** read the entry's lowest mailId (from the `EntryRef`'s inventory view; `ConsumeUnit` removes exactly that unit). Misdeliver: `penalty = MailPay.UnitPenalty`; `if (!wallet.TryDebitPenalty(penalty, walletFloor)) { stoppedAtFloor = true; break; }`; then `ConsumeUnit`, retire the row, add the unit's `complaintOnMisdelivery` to the op's raw sum. Accept: `pay = MailPay.UnitPay(u, Timeliness(...), stats, actor)` with `thrownPm = 1000`; `ConsumeUnit`; `Credit`; retire. After the loop, call story 006's `ChargeMisdelivery` once with the raw sum (if > 0), and `ChargeLate` for an Accept with ≥ 1 late unit. Then raise `DeliveryOutcome` (one per entry), with `ComplaintRaised` when non-zero.
- **Event order within the call:** Inventory's removal records/seals (raised inside `ConsumeUnit`), then `MailConsumed` per unit, then `ComplaintRaised`, then `DeliveryOutcome`. The Delivery handler's reply comes last (ADR-0008: events before the reply; M-NET-1b "after the entries' Removed events").
- **`DeliveryOutcome` payload (ADR-0017 §9, 34 B Sim):** `outcomeSeq u32, destination u32, actor u8, via u8, result u8, kind u16, units u16, cents i64, lateUnits u16, deadUnits u16, complaintPermille u16, flags u8, intendedAddress u32`. Order the fields so that no implicit padding remains, or add explicit zeroed pads (ADR-0008). `outcomeSeq` is a Mail-owned u32 counter in the dump.
- **Floor-stop broadcast throttle:** a per-(destination ordinal, PlayerSlot) `lastZeroFloorTick` array (lots × 8 longs, preallocated), dumped.
- **Version bumps (flag for readiness):** ADR-0017 §5 calls `ConsumeUnit` per unit, and ADR-0009 §3 bumps a container +1 per mutating call. A k-unit Accept therefore bumps the entry's container k times. Grid Inventory H-R7-6/H-R7-7 say "+1 / one bump per touched container". Implement the ADR behaviour. This deviation is logged as GDD impact (decision log) for `/architecture-review`. Do not invent a batch API here.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 008: the thrown path and the cross-path tick-order test (M-R6-3).
- manual-delivery: the `DeliverMatching` op, its handler, range gate, scan, reply and reason codes (`production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`).
- Story 015: wire encoding of `DeliveryOutcome` (≤ 19 B). M-NET-1 peer fan-out (M0).
- `MailShiftStats` lines and the Payday roll-call (M-R16-1c/e): M1.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/ConsumeHandTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001–006; `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`ConsumeUnit`, `EntryRef`)
- Unlocks: Story 008; `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`
