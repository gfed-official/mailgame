# Story 007: One consume routine (`MailConsumer.Consume`) and the hand path (`IMailApi.ConsumeHand`) — per-unit pay/penalty/floor, `MailConsumed`, `DeliveryOutcome`, `misdeliveriesThisShift`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: L-ish M (~1 day; ~350 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-11-04 (branch `docs/rulings-td208-td209`): TD-208 items 4–8, 13, 14 approved by Evan 9:23 AM PT by card click (card t271s1234, listing t271s1233). Earlier, same day (readiness branch `docs/readiness-md007-pc018`, PR #77): TD-208 items 1, 2 option (a) and 3 approved by Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213), ADR-0009 and ADR-0017 errata entered; item 12's order settled by ADR-0017 §5 l.129. Before that, same day: main 579319a merged in (API as shipped by PR #63, now on main; TD-208 items). Earlier: 2026-11-03 (branch `story/mail-destinations-006`, PR #63): AC-8–AC-10 (M-F9-3 c/d/e) added — TD-193 item 7b, approved by Evan 2026-11-03 9:15 PM PT by card click (card t257s1188, listing t257s1187).

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

> **Mostly resolved 2026-11-04 (TD-208):** the ADR questions are settled — items 1 (A1), 2 option (a) (A2) and 3 (A3) approved by Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213; ADR-0009 / ADR-0017 errata of that date), and item 12's order (A4) settled by Accepted ADR-0017 §5 l.129, not an Evan ruling. Items 4, 5, 6, 7, 8, 13 and 14 Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): "Approve TD-208 item 8 (mail code keeps its own shift number starting at 1; adds a step to story 013), as listed in your message above"; "Approve Part 2 items 1-14 (TD-208 items 4, 5, 6, 7, 13, 14 and TD-209 items 2-9), as listed in your message above" (see AC-7 and Implementation Notes). **Still pending Evan — do not build until resolved:** TD-208 item 9 (where the −500 ¢ floor value lives), item 10 (outcome numbering, the floor-message throttle, the per-shift wrong-delivery count and their dump order), item 12 (assert code 89 and the Release stop when `ConsumeUnit` refuses mid-loop) and listing t271s1233's open points (i) (what a stopped hand-over still reports) and (iv) (an always-on record of the stop in Release); not selected on card t271s1234 (not approved, not declined). *Facts on what depends on them (no decision): item 10 is asserted directly by AC-2 (contiguous `outcomeSeq`), AC-4 and AC-5 (`misdeliveriesThisShift`, the 30-tick throttle) and fixes the dump order; item 9 decides only where the −500 ¢ value lives (fixed in code or a game setting) — the value itself is the GDD's, so the floor checks in AC-4 read the same either way; item 12, (i) and (iv) touch only the branch where `ConsumeUnit` refuses mid-loop, which no AC exercises. AC-1, AC-3, AC-6–AC-10 and the approved items depend on none of them.*

*From GDD `design/gdd/mail-destinations.md`, scoped to the hand path (fixture: FX-W stub wallet, FX-L letter 8 ¢ / penalty 4 ¢, an `IInventoryApi` stub or the real Inventory):*

- [ ] **AC-1 (M-R6-1a hand, M-X-1c)** A 20-letter entry for the wrong house via `ConsumeHand` consumes exactly 1 unit, the lowest mailId.
- [ ] **AC-2 (M-R6-1b)** `ConsumeHand` over 4 matching entries in scan order → 4 ops, 4 `DeliveryOutcome`s in that order, all in one tick, with contiguous `outcomeSeq` and 0 misdeliveries.
- [ ] **AC-3 (M-R6-2 hand, M-R9-2a)** A Reject (`closed`) consumes nothing, makes no `IInventoryApi`, wallet or complaint call, raises no `DeliveryOutcome` (Rejects reply to the requester only), and the returned summary counts the entry as not got with reason `Closed`.
- [ ] **AC-4 (M-F8-1c, M-X-4, M-R9-2b)** At wallet −496 a hand misdeliver of a 4 ¢ letter consumes and leaves −500; the next one returns `stoppedAtFloor` with 0 units, makes exactly one refused `TryDebitPenalty`, raises no `ComplaintRaised` and does not count in `misdeliveriesThisShift`. Across all ACs, the wallet receives exactly one `TryDebitPenalty` per misdelivered unit attempted and one `Credit` per paid unit, all in the consuming tick.
- [ ] **AC-5 (M-R16-1b/d, R16 floor broadcast)** `DeliveryOutcome.actor` = the requesting player; `via` = hand. A zero-unit floor stop raises one `DeliveryOutcome` with `units` 0 and the `stoppedAtFloor` flag, at most once per (destination, actor) per 30 ticks. `misdeliveriesThisShift` increments once per op that consumed ≥ 1 Misdeliver unit.
- [ ] **AC-6 (M-R6-4, M-R4-2c hand)** Two players' `ConsumeHand` calls at one mailbox in one tick both succeed sequentially in call order, each against their own entries. A hold started at T − 1 (before close at T) and applied at T + 90 is accepted; applied at T + 91 it is Rejected `closed`.
- [ ] **AC-7 (M-PERF-1b)** A 40-unit postcard Accept entry (postcard `maxStack` 40, 4 ¢; a letter stacks to 20) (fixture-built; shift unlocks don't apply) makes exactly 40 `ConsumeUnit` calls, 40 `Credit` calls and 1 outcome, and the hand path allocates 0 B after warm-up; the 0 B clause is advisory until M0 (ADR-0017 erratum E2). *TD-208 item 14, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): a letter stack holds only 20, so the test becomes "a 40-postcard stack to the right house" (postcards stack to 40, 4¢ each); it's built by the test itself, so shift unlocks don't apply; the 'uses no extra memory' check is reported but doesn't fail the build until the next milestone (M0). Uncredited: the `BuiltInContent.cs:119-120` values and the ADR-0017 E2 cite.*

*M-F9-3 c/d/e, moved here from story 006 by TD-193 item 7b — approved by Evan 2026-11-03 9:15 PM PT by card click (card t257s1188, listing t257s1187): "Approve TD-193 item 7b: move the three late-complaint rules to story 007, as listed in your message above". The three rules, as the listing gave them, are the caller's side of story 006's `ChargeLate` contract:*

- [ ] **AC-8 (M-F9-3c)** An Accept with only dead letters makes no late complaint.
- [ ] **AC-9 (M-F9-3d)** A misdelivery makes no late complaint.
- [ ] **AC-10 (M-F9-3e)** Two late letters in one hold count once.

*Uncredited implementation detail (Mail Dev, not part of Evan's ruling): in story 006's terms (AC-3; `tests/unit/mail/ComplaintBucketTests.cs:153-157`) a dead-letter-only Accept makes no `ChargeLate` call (0, `lastLateTick` unchanged); a Misdeliver op with late units makes no late call (0 late complaint); two late entries in one hold make one call (+2,000 ‰ once). Story 006 tests `ChargeLate` itself; these three are tested here, through `ConsumeHand`.*

---

## Implementation Notes

*Derived from ADR-0017 §5, §9 and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Mail/MailConsumer.cs` (internal; the one routine), `ConsumeCall.cs` (`readonly ref struct` or struct: path, destination ordinal, entry ref, actor, `actionStartTick?`, `thrownPm`, apply tick), `Events/DeliveryOutcomePayload.cs`, `Events/MailConsumedPayload.cs`, kinds and descriptors in `Events/MailEventKinds.cs` (PR #63, on main since 579319a; `MailEventDescriptors` l.23-37).
- **Hand misdeliver = exactly one unit per op** (GDD R6 table, OQ4); Accept = the whole entry; Reject = nothing. `ConsumeHand` needs no mode flag: a MATCH entry evaluates to Accept (same address), and the single `MISDELIVER_ONE` entry evaluates to Misdeliver.
- **Return value — approved (A3), Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213): "Approve A3 (TD-208 item 3): delivery returns the receipt with an ADR-0017 erratum, as listed in your message above".** As listed: after a hand delivery the game gets a receipt: number of stacks handed over (right or wrong house), letters used, first refusal reason, whether the −500 ¢ wallet floor stopped a wrong delivery, and net change in cents. Example: 4 one-letter stacks on time at 8 ¢ each = 4 stacks, 4 letters, +32 ¢. ADR-0017 erratum 2026-11-04 (Key Interfaces l.225 was `void`); Delivery's handler builds its reply from it. *Uncredited (Mail Lead planning default, TD-208 item 3; not part of Evan's approval): a `HandConsumeSummary` readonly record struct (`EntriesGot`, `UnitsConsumed`, `FirstReject`, `StoppedAtFloor`, `CentsDelta`).*
- **Facts:** host `DestinationFacts` come from the registry + `DeliveriesState` (`src/PerformativeMail.Sim/Mail/DeliveriesState.cs`, via `DestinationRegistry.Deliveries`; closed by default, tests open it with its internal `Set`; story 009 adds `SetDeliveriesState`) through story 003's `Effective(..., actionStartTick: holdStartTick, applyTick: now)`. Host `UnitFacts` come from the mail table row (story 001) for the entry's lowest mailId. All units of one entry share kind and address, so `Evaluate` runs once per entry and pay/penalty run per unit (spawn values can differ by spawn shift).
- **Stack read — approved (A1), Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213): "Approve A1 (TD-208 item 1): add the look-only stack read with an ADR-0009 erratum, as listed in your message above".** As listed: a look-only inventory call that reads one stack's mail kind, address, flags, letter count and oldest letter id; changes nothing, allocates nothing; a stale reference returns nothing, same as removal does today (ADR-0009 erratum 2026-11-04). *Uncredited (Mail Lead planning default, TD-208 item 1): `bool IInventoryApi.TryReadEntry(EntryRef, out EntryView)`.* **Actor — approved (A2) option (a), same card: "Approve A2 (TD-208 item 2) option (a): slot-to-player lookup, no ADR change, as listed in your message above".** As listed: a 'which player is in this slot' lookup so the inventory removal record names the delivering player; an empty slot gives no player; no ADR change. *Uncredited (Mail Lead planning default, TD-208 item 2): `EntityId ISlotView.PlayerOf(PlayerSlot)`, `MailSystem` takes `ISlotView`, `ConsumeUnit(..., ActorId.FromEntity(slots.PlayerOf(actor)))`.* **Wrong-delivery order (A4) — settled by Accepted ADR-0017 §5 l.129, not an Evan ruling:** charge, then remove; `IWalletGate` has no refund call, so a later `ConsumeUnit` refusal leaves the penalty standing (the assert code and Release stop are TD-208 item 12, pending Evan). **Per-unit loop (oldest first):** read the entry's lowest mailId (from the `EntryRef`'s inventory view; `ConsumeUnit` removes exactly that unit). Misdeliver: `penalty = MailPay.UnitPenalty`; `if (!wallet.TryDebitPenalty(penalty, walletFloor)) { stoppedAtFloor = true; break; }`; then `ConsumeUnit`, retire the row, add the unit's `complaintOnMisdelivery` to the op's raw sum. Accept: `pay = MailPay.UnitPay(u, Timeliness(...), stats, actor)` with `thrownPm = 1000`; `ConsumeUnit`; `Credit`; retire. After the loop, call `_complaints.ChargeMisdelivery(d, now, rawSum, _stats, ref sink)` once when `rawSum > 0` and `_complaints.ChargeLate(now, _stats.Has(RuleFlag.SuppressLateComplaint))` once per Accept with ≥ 1 late unit, pass each result to `RaiseComplaint(d, ComplaintSource.Misdelivery | Late, amountPm)` (which emits only when > 0, `MailSystem.cs:166-171`), then raise `DeliveryOutcome` (one per entry) (as shipped by PR #63, merged as main 579319a).
- **Event order within the call:** Inventory's removal records/seals (raised inside `ConsumeUnit`), then `MailConsumed` per unit, then `ComplaintRaised`, then `DeliveryOutcome`. The Delivery handler's reply comes last (ADR-0008: events before the reply; M-NET-1b "after the entries' Removed events").
- **`DeliveryOutcome` payload (ADR-0017 §9, 34 B Sim):** `outcomeSeq u32, destination u32, actor u8, via u8, result u8, kind u16, units u16, cents i64, lateUnits u16, deadUnits u16, complaintPermille u16, flags u8, intendedAddress u32`. Order the fields so that no implicit padding remains, or add explicit zeroed pads (ADR-0008). `outcomeSeq` is a Mail-owned u32 counter in the dump.
- **Floor-stop broadcast throttle:** a per-(destination ordinal, PlayerSlot) `lastZeroFloorTick` array (lots × 8 longs, preallocated), dumped.
- **Version bumps (flag for readiness):** ADR-0017 §5 calls `ConsumeUnit` per unit, and ADR-0009 §3 bumps a container +1 per mutating call. A k-unit Accept therefore bumps the entry's container k times. Grid Inventory H-R7-6/H-R7-7 say "+1 / one bump per touched container". Implement the ADR behaviour. This deviation is logged as GDD impact (decision log) for `/architecture-review`. Do not invent a batch API here.
- **Approved 2026-11-04 9:23 AM PT (TD-208 items 4–8 and 13; Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233)), in the listing's words; the names, types and code placements after each are uncredited Mail Lead planning detail from the TD-208 row:** (4) the 'delivery happened' message every player gets: 34 bytes with 13 fields: number, house, player, how, result, mail kind, letters, cents, late letters, dead letters, complaint, flags, intended address; message id `0x0101`, sent to all 8 players; result 1 = right house, 2 = wrong house; flag 1 = stopped at the wallet floor, flag 2 = cursed; cents are positive pay for a right house and negative penalty for a wrong one; the intended address is filled in only for a wrong house (0 otherwise) — *uncredited:* `[Pack = 1] DeliveryOutcomePayload` in `Mail/Events/DeliveryOutcomePayload.cs`, `ReplicationClass.Replicated`, resync scope 2, `AudienceAll` 0xFF, `Result = (byte)EvalResult`; how the 'number' is assigned is item 10 (pending). (5) numbers for how mail arrived: None 0, Hand 1, Throw 2, Feeder (machine) 3, Npc (computer-driven) 4 — *uncredited:* `public enum DeliveryVia : byte` in `Mail/DeliveryVia.cs`. (6) an internal per-letter note, never sent over the network: message id `0x0102`, 16 bytes: letter id, cents, result, late yes/no, dead yes/no, player — *uncredited:* `[Pack = 1] MailConsumedPayload`, `ReplicationClass.Internal`, resync scope 2. (7) overdue = current shift minus deadline shift; late if 1 or more but less than the dead-letter limit (default 2); dead if at or past it; the Grace Period perk changes the pay but not the late count. (8) the mail code keeps its own shift number, starting at 1; it isn't saved and tests can set it; story 013 gains the step "set the current shift at shift start (`PrepareShift`)" — *uncredited:* `internal int CurrentShift { get; private set; } = 1` on `MailSystem`, an internal test setter. (13) the removal reason the inventory records: "Delivered" for the right house, "Misdelivered" for the wrong one (`ConsumeReason.Delivered` / `.Misdelivered`).

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

- Depends on: Stories 001–005 and 006 (all Complete on main; 006 merged by PR #63 as main 579319a); `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`ConsumeUnit`, `EntryRef`)
- Unlocks: Story 008; `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`
