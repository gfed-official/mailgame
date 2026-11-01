# Story 006: Complaint deltas (F9) — lazy door and team token buckets, late-complaint window, `ComplaintRaised`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~200 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-29 (docs PR `docs/pc017-md006-pc019-readiness`, off main 7c8f8ec): full `/story-readiness` pass of 2026-10-28 (log `/workspace/repos/logs/md006-readiness.txt`, handoff `/workspace/repos/logs/md006-handoff.md`) — NEEDS WORK, no blocker, no ADR contradiction (the bucket formula is ADR-0017 §7 verbatim); the mechanical fixes below are applied (shipped data-key names and units, the team-cap formula, exact AC-4 sums, the signatures, new AC-6 for TR-mail-012), the manifest stamp is 2026-10-16.2 → 2026-10-17.1 (the 2026-10-17.1 revision adds only Godot input-adapter and `WorldTablesBuilder` rules, nothing for Mail), and five Mail Lead planning defaults are batched in TD-187 — **all five Approved by Evan 2026-10-29 7:41 AM PT, Mail Lead chat, in his own words answering Mail Lead's card 'Next stories: decisions': "Approve all five TD-187 defaults for mail-destinations 006"** (TD-187 closed; each label below reads "approved by Evan 2026-10-29 7:41 AM PT (TD-187 item n)"; decision log 2026-10-29). Status stays Ready. 2026-10-29 (PR #69 r2, TD-200, Evan approved 10:40 PM PT): line cite `Mail/MailSystem.cs:109-117` → `106-114` after the TD-200 deletion of `MailSystem.Table` (same `Bind` body). Text only; no AC changed.

## Context

**GDD**: `design/gdd/mail-destinations.md` (R9 complaint deltas; F9)
**Requirement**: `TR-mail-010` (complaint token buckets in int64 ticks × permille, elapsed clamped), `TR-mail-012` (pause freezes buckets/windows)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §7, §9 `ComplaintRaised`); ADR-0006 (int64, one rounding)
**ADR Decision Summary**: Per door and team-wide, `tokensPm` and `lastTick` are `long`. Refill is lazy, on touch only: `tokens = min(capPm × window, tokens + min(now − lastTick, window) × capPm)`, elapsed clamped to `window` (900). Nothing iterates buckets per tick. `lastLateTick` is team-wide. `ComplaintRaised{destination EntityId, source u8, amountPermille u16}` is Replicated (the GDD's `mailboxId` is `destination`).
**ADR Version**: ADR-0017 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: complaint buckets refill lazily on touch; an idle `Mail.Step` touches only feeder credits (ADR-0017)
- Required: integer-only, int64 intermediates, ticks as absolute values (ADR-0006)
- Forbidden: per-tick bucket iteration; wall-clock reads; int32 elapsed × cap products
- Guardrail: 0 B per call

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (letters raw 5,000 ‰ unless stated), scoped to this story:*

- [ ] **AC-1 (M-F9-1, M-R9-1)** Fresh buckets (`ChargeMisdelivery`, direct calls): **a** one 20-letter op → 20,000 (10,000 with `Stat.MisdeliveryComplaintMultPm` 500, applied by `ChargeMisdelivery` after the cap — TD-187 item 1); **b** six one-letter ops at six doors → 5,000 each; **c** two 20-letter ops at one door in one tick → 20,000 then 0; **d** a 40-postcard op (raw 120,000) → 20,000; **e** emptied at T: T + 450 allows 10,000, T + 900 allows 20,000; after 200,000 idle ticks the bucket is exactly full, with no overflow; **f** one letter per 900 ticks → 5,000 each; **g** `MisdeliveryComplaintMult` 500 halves the raised value after the cap, and the bucket is still debited the full allowance (M-R9-2c).
- [ ] **AC-2 (M-F9-2)** n = 1, one one-letter op every 15 ticks at one door for 1,800 ticks (last at 1,785), fresh buckets: Σ complaint = 59,666 ‰ ± 2, and ticks 0–899 sum to 39,666 ‰ ± 2 (driven by direct bucket calls; the feeder path is M-1b).
- [ ] **AC-3 (M-F9-3)** Contract tests on `ChargeLate(now, graceSuppresses)`: a call at T with `graceSuppresses == false` → +2,000 ‰ (`LateComplaint`) and `lastLateTick = T`; a call for any door at T + 899 → 0; at T + 900 → +2,000 (the window is team-wide: `LateComplaintWindowSeconds` 30_000 ms → 900 ticks); a call with `graceSuppresses == true` (Grace Period, `stats.Has(RuleFlag.SuppressLateComplaint)`) → 0 and does **not** set `lastLateTick`; one call per op. The GDD's c/d/e rows are the caller's classification, restated here as the contract the caller (story 007) must meet and which story 007 adds to its hand-path ACs as M-F9-3 c/d/e (neither 007 nor 008 cites M-F9-3 today; Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 1)): a dead-letter-only Accept makes no `ChargeLate` call (0, `lastLateTick` unchanged); a Misdeliver op with late units makes no late call (0 late complaint); two late entries in one hold make one call (+2,000 once).
- [ ] **AC-4 (M-F9-4)** Exact sums, fresh buckets, eight 20-letter ops (raw 100,000 each) at eight distinct doors in one tick, in call order: at `playerScalePm` 3864 (n = 8; `teamCapPm` 155,000) Σ = **155,000** ‰ — the first seven doors raise 20,000 each (door-capped; the team bucket goes 155,000 → 15,000) and the eighth raises **15,000** (team-capped); at `playerScalePm` 1000 (n = 1; `teamCapPm` 40,000) Σ = **40,000** ‰ — the first two doors raise 20,000 each and the remaining six raise 0. A team bucket emptied at T allows exactly **40,000** ‰ at T + 900 at n = 1 (and 20,000 at T + 450); after 200,000 idle ticks it is exactly full, no overflow.
- [ ] **AC-5 (M-R9-2a)** Zero allowed complaint raises no `ComplaintRaised`. Each non-zero raise emits exactly one `ComplaintRaised` (`(EventKind)0x0100`, through `MailSystem.RaiseComplaint`; TD-187 item 1) with `Source` ∈ {misdelivery, late}; `EventDescriptorAssert.Validate(descriptor, OpKindBlock.MailDelivery)` (`tests/PerformativeMail.Sim.Tests/Fixtures/EventDescriptorAssert.cs:25`) reports no violation.
- [ ] **AC-6 (TR-mail-012, pause freezes buckets and windows)** A second `ChargeMisdelivery` or `ChargeLate` call at the same `now` as the first refills 0 (elapsed 0 on both the door and the team bucket) and leaves `lastLateTick` unchanged; `MailScan.WallClockReads("PerformativeMail.Sim.Mail")` (`tests/unit/mail/MailArchitectureTests.cs:99-112`) stays empty with `ComplaintBuckets.cs` present (no `DateTime`, `DateTimeOffset`, `Stopwatch` or `Environment.TickCount` read — time only ever enters as the `now` tick argument). Under `SIM_ASSERT`, `now < lastTick` fires `SimAssertCode.MailComplaintTickRegressed = 86`; in Release the elapsed time is treated as 0 (TD-187 item 5).

---

## Implementation Notes

*Derived from ADR-0017 §7 and GDD F9 (read the F9 block; constants below are its data keys):*

- **File:** `src/PerformativeMail.Sim/Mail/ComplaintBuckets.cs` (new), built in the `MailSystem` constructor after the registry. Door state lives in the registry's `tokensPm`/`lastTick` columns (story 002, `Mail/DestinationRegistry.cs:115-116`); the team bucket and `lastLateTick` are fields on the same object. **"All start full":** the `ComplaintBuckets` constructor writes `capPm × window` into every row's `TokensPm` and into the team bucket and leaves `LastTick` 0; the registry itself still initialises both columns to 0 (`DestinationRegistry.cs:17`; the bare-registry pins `tests/unit/mail/DestinationRegistryTests.cs:158-159` stay valid; update the registry doc comments at `DestinationRegistry.cs:17` and `:184` only) — Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 2). `lastLateTick` starts at "never" (a `lateCharged` byte flag, not `long.MinValue` arithmetic).
- **API (internal):** `int ChargeMisdelivery(DestinationOrdinal d, long now, int rawPm, IStatSheet stats, ref SimCounterSink sink)` refills lazily, computes `allowed`, applies both debits, returns 0 when nothing is allowed, and otherwise returns the raised value: `allowed` scaled by `Stat.MisdeliveryComplaintMultPm` (Team scope, `src/PerformativeMail.Sim/Content/Stats/StatDefs.cs:128-129`) **after the cap** with one `Fix.MulDivRH` — the bucket is still debited the full `allowed` (AC-1g). `int ChargeLate(long now, bool graceSuppresses)`, with `graceSuppresses = stats.Has(RuleFlag.SuppressLateComplaint)` (`Content/Stats/RuleFlag.cs:28`, `IStatSheet.cs:70`; GDD Grace Period) evaluated by the caller. The sink is last, as on every `Fix` money function (`Primitives/Fix.cs:77` `MulDivRH`, `:92` `FloorDiv`; ADR-0017 Key Interfaces); `MailSystem` already holds `_stats` and `_counters` (`Mail/MailSystem.cs:36-44`). A `MailSystem`-internal `RaiseComplaint(DestinationOrdinal d, ComplaintSource source, int amountPm)` emits `ComplaintRaised` when `amountPm > 0`; stories 007/008 call it. The caller (story 007/008) sums raw complaint over units actually consumed as Misdeliver in one op and calls `ChargeMisdelivery` once per op — Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 1): this story declares the event and owns the multiplier and the raise; the story as first written handed both to 007/008, leaving AC-1a's parenthetical, AC-1g and AC-5 no test seam here.
- **Refill order:** refill the door and team buckets lazily at `now`, then `allowed = min(raw, doorTokens / window, teamTokens / window)` with `FloorDiv` on non-negative values, then subtract `allowed × window` from both. Clamp elapsed to `window` **before** multiplying (`long`). **Counter:** each lazy refill touch adds 1 to the existing `CounterId.MailBucketsVisited` (57; `Core/CounterId.cs:190`, "one per complaint bucket visited") through the system's `SimCounters` — 2 per `ChargeMisdelivery` (door + team), 0 per `ChargeLate`; no new counter; an idle tick still visits 0 (`tests/integration/mail/MailCompositionTests.cs:103`) — Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 4). **Assert:** `SimAssertCode.MailComplaintTickRegressed = 86` (`Primitives/SimAssertCode.cs` ends at `MailApiNotImplemented = 85`) fires when `now < lastTick` under `SIM_ASSERT`; otherwise the elapsed time is treated as 0 — Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 5).
- **Data keys (shipped names and units; "PROPOSED" in the GDD is a balance marker, the keys are built and pinned by `tests/integration/content/BuiltInContentTests.cs:398-402, 519-522`):** read them from `MailBalance` (`src/PerformativeMail.Sim/Content/Balance/MailBalance.cs:46-59`) and `MailKindDef`, never as literals — `DoorComplaintCap` 20_000 milli-points (key `mail.doorComplaintCap`; milli-points equal permille, so `capPm` is the key value directly); `DoorComplaintWindowSeconds` 30_000 **ms** (key `mail.doorComplaintWindowSeconds`) and `LateComplaintWindowSeconds` 30_000 ms, each converted with `TickTime.ContentTicks(ms)` (`Hosting/TickTime.cs:83`) = 900 ticks; `LateComplaint` 2000 milli-points (key `mail.lateComplaint`); `TeamComplaintCap` 40_000 milli-points before `playerScale` (key `mail.teamComplaintCap`); and `MailKindDef.ComplaintOnMisdeliveryMilliPoints` from the data key `complaintOnMisdeliveryPoints` (`Content/Parsing/Builder.cs:264`; letters 5,000).
- **Team cap:** `teamCapPm = Fix.MulDivRH(TeamComplaintCap, playerScalePm, 1_000_000, ref sink) × 1000` — round to whole points, then back to permille. At `playerScalePm` 3864: 40_000 × 3864 / 1_000_000 = 154.56 → RH 155 → **155,000**; at 1000: 40 → **40,000**. (The formula as first written, `RH(teamComplaintCap × playerScalePm / 1000) × 1000`, applied to the shipped 40_000 gives 154,560,000.) `playerScalePm` (n^0.65 in permille) is Run Structure's and arrives with the deliveries/shift input (story 009's stub passes it, 1000 solo); this story takes it as a parameter.
- **Event `ComplaintRaised` (this story declares it; Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 1)):** `Mail/Events/MailEventKinds.cs` with `ComplaintRaised = (EventKind)0x0100` — the first use of the Mail/Delivery block 0x0100–0x013F (`Core/OpKindBlock.cs:46`, allocated by ADR-0007 §4; story 007's `DeliveryOutcome` then takes 0x0101; append-only); `Mail/Events/ComplaintRaisedPayload.cs` `{ uint Destination; ushort AmountPermille; byte Source; byte Pad0; }` — 8 B, `[StructLayout(LayoutKind.Sequential, Pack = 1)]` like `WalletChangedPayload.cs:11`, padding-free per ADR-0008 (`destination EntityId` is `uint`, ADR-0017 §9 l.173; the GDD's `mailboxId`); descriptor `EventDescriptor.For<ComplaintRaisedPayload>(kind, ReplicationClass.Replicated, coalescible: false, resyncScope: 2, wireCodecId: 0, wireSize: 0)` until story 015 (the `EconEventKinds.cs:30-31` shape), bound in `MailSystem.Bind` (`Mail/MailSystem.cs:106-114`). Audience all.
- **Dump:** append `teamTokensPm i64, teamLastTick i64, lastLateTick i64, lateCharged u8` to the Mail section in `MailStateContributor.Write` after the registry's columns and before story 009's deliveries block (`Mail/MailStateContributor.cs:36-38`; edit that comment and story 009's note); no new section — `SectionIds.Mail = 3` (`tests/unit/mail/MailTableTests.cs:641` pins the id only, no byte pin breaks) — Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 3).
- **Ids reserved for this story:** `EventKind` 0x0100; `SimAssertCode` 86; no new `CounterId` (reuse 57) and no new `SectionIds`. `tests/unit/sim/Core/SimCountersTests.cs:175` (`Length == 58`) is unchanged by this story. Next-free ids were checked on main 7c8f8ec and in open PRs #58 and #59; player-character 017 takes CounterId 58–60 and CheckCode 16, player-character 019 takes CounterId 61–63 and SimAssertCode 87; these do not move if the landing order changes.
- **Must stay true:** `DestinationRegistryTests.cs:158-159` (bare registry `TokensPm`/`LastTick` 0; TD-187 item 2), `DestinationRegistryTests.cs:470-483` (dump determinism), `MailCompositionTests.cs:80-118` (idle ticks unchanged, `MailBucketsVisited` stays 0), `SimCountersTests.cs:175`. No Mail-section golden exists; the carrier golden is the per-tick transform hash only.
- **Test legs that must stay green:** dev loop — this story's tests on net10.0 Debug; close — net10.0 Debug and net8.0 Release in full; CI — all four Sim-test legs (net10.0/net8.0 × Debug/Release) plus "Game project build (no editor)". 0 B checks are advisory until M0 (E2, ADR-0017 Errata).

### Mail Lead planning defaults, approved by Evan 2026-10-29 7:41 AM PT (TD-187, closed)

*The story is built against these; none contradicts an Accepted ADR or an AC. Each is labelled at its place of use above.*

1. **`ComplaintRaised` ownership:** this story declares the event (kind `(EventKind)0x0100`; payload `{uint Destination; ushort AmountPermille; byte Source; byte Pad0}`, 8 B, `Pack = 1`; descriptor Replicated, not coalescible, resync scope 2, codec 0 until story 015) and `MailSystem.RaiseComplaint` emits it; stories 007/008 call it; `ChargeMisdelivery` applies `Stat.MisdeliveryComplaintMultPm` after the cap; AC-3 c/d/e become contract tests on `ChargeLate` and story 007 picks up M-F9-3 c/d/e. (Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 1).)
2. **"All buckets start full":** the `ComplaintBuckets` constructor writes `capPm × window` into every row and the team bucket, `LastTick` 0; the registry itself still initialises to 0. (Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 2).)
3. **Dump:** `teamTokensPm i64, teamLastTick i64, lastLateTick i64, lateCharged u8` appended to the Mail section after the registry and before story 009's block. (Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 3).)
4. **Counter:** each lazy refill adds 1 to `MailBucketsVisited` (2 per `ChargeMisdelivery`, 0 per `ChargeLate`); no new counter. (Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 4).)
5. **Assert:** `SimAssertCode.MailComplaintTickRegressed = 86` fires when `now < lastTick` under `SIM_ASSERT`; otherwise the elapsed time is treated as 0. (Mail Lead planning default, approved by Evan 2026-10-29 7:41 AM PT (TD-187 item 5).)

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007/008: deciding which units were consumed as Misdeliver (floor stops raise nothing, M-R9-2a/b), the call per op, and the late-unit classification (story 007 adds M-F9-3 c/d/e to its hand-path ACs; TD-187 item 1). `DeliveryOutcome` (0x0101) is story 007's.
- The complaint meter, its decay and thresholds (Run Structure, M1). `CursedMisdelivered` (R14, M4).
- `misdeliveriesThisShift` (story 007).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY ran in full mode on the 2026-10-28 `/story-readiness` pass and returned **GAPS** — four findings: caller-behaviour ACs (AC-3 c/d/e, AC-5), the AC-4 inequality, no TR-mail-012 AC, and the data-key units; each was re-verified against src/ and tests/ and is applied above or batched in TD-187 — log `/workspace/repos/logs/md006-readiness.txt`.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/ComplaintBucketTests.cs` (new; `[Trait("Id", "M-F9-1")]`, `M-F9-2`, `M-F9-3`, `M-F9-4`, `M-R9-1`, `M-R9-2a`, and the story-local `MD006-AC6` for TR-mail-012) — must exist and pass on net10.0 and net8.0, Debug and Release; includes the `EventDescriptorAssert.Validate(…, OpKindBlock.MailDelivery)` check on the `ComplaintRaised` descriptor and the `MailScan.WallClockReads` scan (AC-6)
- Advisory (E2, reported not blocking): `tests/unit/mail/ComplaintBucketAllocationTests.cs` (0 B per call; `[Collection(NonParallelCollection.Name)]`, the `MailPayAllocationTests.cs:12, 29` pattern)
- Must stay green unchanged: `tests/unit/mail/DestinationRegistryTests.cs:158-159`, `tests/integration/mail/MailCompositionTests.cs:80-118`, `tests/unit/sim/Core/SimCountersTests.cs:175`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (registry columns), Story 005 (Mail system, event registration); `production/epics/content-data/story-005-m1a-def-records.md` (`complaintOnMisdelivery`)
- Unlocks: Story 007, Story 008
