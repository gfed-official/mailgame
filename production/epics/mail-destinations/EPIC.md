# Epic: Mail & Destinations (M-1a slice)

> **Layer**: Feature
> **GDD**: design/gdd/mail-destinations.md (In Review)
> **Architecture Module**: Sim.Mail (`src/PerformativeMail.Sim/Mail/`) + mailbox/house views (Godot)
> **Milestone slice**: M-1a. The GDD's OQ21 lists what the greybox needs: `Evaluate`, payment and penalty (F5–F8), the door bucket (F9) and a fixed spawn cadence. Everything else is provisional until the two-friend greybox.
> **Engine Risk**: LOW (Sim). The mailbox views and stings are MEDIUM (Rendering/Audio) and wait on ADR-0019.
> **Est. size**: M/L — 12–14 stories
> **Implementation priority**: 4 of 9
> **Status**: Ready
> **Stories**: 16 stories — 7 Complete, 9 Ready (counts refreshed 2026-10-28 from the story headers, story 005 Complete, branch `story/mail-destinations-005`; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

Mail & Destinations decides what every piece of mail is worth and where it is going. It owns every mailbox and the one rule that judges anything reaching one: **Reject, Accept or Misdeliver**, whether the mail came by hand, by throw, or later by belt or truck. It is the Pillar 1 hub. Hands, throws and machines are held to the same honest rule because they all call one pure `Evaluate` and one consume routine.

**In scope at M-1a:**
- **Mail data:** the M-1a `MailKindDef` subset (R1; letters and the packages the inventory fixtures use); the server-only SoA mail table keyed by `mailId` with packed `AddressId` (R2).
- **Destinations:** the destination registry built from `world-stub`'s address table (R3); destination states OPEN/CLOSED (R4).
- **The verdict:**
  - `Evaluate(destination, unit)` as a pure, zero-allocation function compiled once and shared with the client preview (R5);
  - one consume routine, oldest unit first, used by the hand and throw paths in fixed tick order (R6);
  - payment with timeliness (R7, F5, F6);
  - penalty and the wallet-floor guard through the Mail-owned `IWalletGate` port, a stub at M-1a with Economy not yet built (R8, F7, F8);
  - per-door complaint deltas with token buckets in int64 ticks × permille (R9, F9).
- **Spawning:** a **fixed spawn cadence** into `po_intake` via `IInventoryApi.Spawn` (NoMerge). The batch arrival hand-off and backlog follow R11; the backlog drain is Inventory's. The full shift-manifest spawner (R10, F1–F4 on the `mail` stream) is built at its simplest deterministic form, one shift and one district, because ADR-0005 already fixes the stream and draw rules.
- **Thrown landings:** `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)` for Throwing (R13; hand-back HB in GDD OQ8).
- **Events:**
  - `MailConsumed`, `DeliveryOutcome` (one per op, ≤19 B, before the reply), `ComplaintRaised`, `DeliveriesState` and `BatchArrived` (R16);
  - the value hint and client F10 reconstruction, so the prompt and arc money label can be shown (R17);
  - the stat keys (R18) read through `IStatSheet`.
- **M-1a shift stub:** a `System`-actor driver that opens deliveries, runs one Delivery-phase deadline (the HUD timer reads it) and closes with `closeGraceTicks` 90. Run Structure (#19) has no GDD, so this stub is the minimum orchestrator call-down (`PrepareShift`, `SetDeliveriesOpen`) named in architecture.md Q-ARCH-11. It is replaced at M1.
- **Presentation:** a greybox mailbox and house view with flag pop and wobble on the outcome, keyed from `DeliveryOutcome`, after `godot-host-shell`.

**Out of scope:**
- automated feeders, F11 and the feeder throttle → M-1b;
- Cursed mail (R14) → stamps, M4;
- Lost Parcels / `SpawnSpecial` (R15) → later;
- district unlocks and the Payday roll-call → M1;
- JoinState, `MailShiftStats` pacing and the value-hint wire budget → M0.

**Why these ADRs govern it.**
- ADR-0005 makes the manifest a pure function of `(seed, shift, profile, addresses)` on per-shift-indexed `mail`/`mail.special` streams with a fixed draw count. A re-roll can never move a Lost Parcel.
- ADR-0006 gives one rounding per formula and integer cents.
- ADR-0008 fixes `DeliveryOutcome`'s size and its event-before-reply ordering.
- ADR-0011 keeps payment host-authoritative, with the client F10 display-only.
- ADR-0010 defines the defs and balance keys.
- ADR-0003 fixes the hand → landing → feeder → NPC order across phases 0, 4, 5, 6 and 12.

**Risks.**
- (1) **ADR-0017 (Mail table, destination registry & `IWalletGate`) is not written.** That leaves 9 TRs untraced: SoA layout, `AddressId` packing, wallet port, consume routine, backlog queue, complaint buckets and closing grace. They are central to this epic. **Recommended:** write ADR-0017 before `/create-stories mail-destinations`.
- (2) `Evaluate`'s sharing with the client is decided in ADR-0016 (not written).
- (3) The GDD is M-1a provisional (OQ21). Spawner details may change after the greybox.
- (4) The shift stub has no GDD. Keep it minimal so it is thrown away cleanly at M1.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0005: RNG Streams and Derivation | per-shift indexed `mail`/`mail.special` streams, single-draw `NextInt`, fixed draw count | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | integer cents, int64 intermediates, one RH per formula, no wall clock | LOW |
| ADR-0008: Event Log, Replication Classes and Event Delivery | `DeliveryOutcome` ≤19 B, one per op, before the reply | LOW (Sim) |
| ADR-0011: Network Adapter Boundary | `DeliveriesState` reliable-ordered; host-authoritative payment; JoinState/value-hint rules (M0) | LOW (seam) |
| ADR-0010: Content Format, Loading, ContentSet and StatSheet | `MailKindDef`, `DestinationTypeDef`, `MailMixDef`, balance keys, stat keys | LOW |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | phase placement, flags set in T effective from T+1, sync `I…Api` calls from Inventory/Throwing | LOW |
| ADR-0004: Identity and Id Spaces | `mailId` u32 allocated in manifest order at the batch tick | LOW |
| ADR-0009: InventoryOp Pipeline and Inventory Storage Layout | `deliver`/`deliver_matching` orchestration through the op pipeline; oldest-first unit removal | LOW |
| *ADR-0017 (planned, not written)*: Mail table, destination registry & `IWalletGate` | SoA table, `AddressId` packing, wallet port, backlog queue | — **missing** |
| *ADR-0016 (planned, not written)*: ClientLayer, shared pure functions & presentation timing | `Evaluate` compiled once for host and client | — **missing** |

## GDD Requirements

Source: architecture.md v1.1 Appendix A. **12 / 23 traced.**

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-mail-001 | All money/permille/tick math integer; int64 intermediates; one RH per formula | ADR-0006 ✅ | M-1a |
| TR-mail-002 | Per-shift manifest pure function of (seed, shift, profile, addresses); stream mail | ADR-0005 ✅ | M-1a |
| TR-mail-003 | Fixed RNG draw count; NextInt without rejection loop | ADR-0005 ✅ | M-1a |
| TR-mail-004 | Separate mail.special stream so re-rolls never move Lost Parcels | ADR-0005 ✅ | M-1a |
| TR-mail-005 | Evaluate pure, 0-alloc, same code compiled into client for prompt and arc colour | ❌ No ADR (planned ADR-0016, not written) | M-1a |
| TR-mail-006 | Server-only SoA mail table keyed by mailId; packed AddressId | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-007 | Wallet via Mail-owned IWalletGate; no Mail→Economy assembly/namespace reference | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-008 | One consume routine for hand/throw/feeder/NPC in fixed tick order | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-009 | Backlog ordered queue O(1) append; drain owned by Inventory; backlogUnits u16 | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-010 | Complaint token buckets int64 ticks×permille, elapsed clamped | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-011 | Feeder throttle integer credit; stagger feederId mod 15 | ❌ No ADR (planned ADR-0017, not written) | M-1b (feeders) |
| TR-mail-012 | No wall-clock reads; pause freezes buckets/windows/credits | ADR-0006 ✅ | M-1a |
| TR-mail-013 | Closing grace by action start tick (host domain); closeGraceTicks 90 | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-014 | DeliveriesState reliable-ordered event, not on 5 Hz snapshot | ADR-0011 ✅ | M-1a event; network delivery M0 |
| TR-mail-015 | DeliveryOutcome one event per op ≤19 B, canonical encoding; ≤16 B/tick typical | ADR-0008 ✅ | M-1a |
| TR-mail-016 | Value hint ≤5 runs per entry; client recomputes payout | ADR-0011 ✅ | M0 |
| TR-mail-017 | JoinState ≤72 B; outcomeSeq drop rule on rejoin | ADR-0011 ✅ | M0 |
| TR-mail-018 | Mail content defs loaded and validated | ADR-0010 ✅ | M-1a |
| TR-mail-019 | Mail ≤1.0 ms p99 / 2 ms max; manifest ≤0.5 ms; 0 alloc hot paths (advisory) | ❌ No ADR (planned ADR-0017, not written) | advisory |
| TR-mail-020 | Idle tick touches only feeder credits; scans no rows | ❌ No ADR (planned ADR-0017, not written) | M-1a |
| TR-mail-021 | MailShiftStats ≤2 KB at Payday, paced ≤800 B/tick | ADR-0011 ✅ | M0 |
| TR-mail-022 | Presentation caps: residents, envelope pool, flashes, audio voices | ❌ No ADR (planned ADR-0019, not written) | M-1a |
| TR-mail-023 | Host-authoritative payment/complaint; client F10 display-only | ADR-0011 ✅ | M-1a |

**Untraced (11):** TR-mail-005 (ADR-0016); TR-mail-006 through 011, 013, 019 and 020 (ADR-0017); TR-mail-022 (ADR-0019).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- The acceptance criteria from `design/gdd/mail-destinations.md` for the M-1a slice (R2–R9, R11, R13, R16–R18; F5–F9, F10) are verified. Feeder, Cursed, special-spawn and district criteria stay open on their milestones
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/mail/**`, `tests/integration/**`)
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (mailbox accept and misdeliver beats), with a lead sign-off for Visual/Feel

## Stories

*ADR-0017 and ADR-0016 are now Accepted (2026-10-16), which resolves risks (1) and (2). The order follows ADR-0017's Migration Plan, plus the shift stub, the client mirrors, the manifest at its simplest form (OQ21: provisional until the greybox) and the codecs.*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Mail table — paged SoA, rows, retire, dump](story-001-mail-table-paged-soa.md) | Logic | Complete | ADR-0017 |
| 002 | [Destination registry from `IWorldQuery.Lots`](story-002-destination-registry.md) | Logic | Complete | ADR-0017, ADR-0026 |
| 003 | [`MailRules.Evaluate` + effective state / closing grace](story-003-evaluate-and-effective-state.md) | Logic | Complete | ADR-0017, ADR-0016 |
| 004 | [`MailPay` F5–F8, F10](story-004-mailpay-f5-f8-f10.md) | Logic | Complete | ADR-0017, ADR-0006 |
| 005 | [`IWalletGate`, `StubWallet`, Mail system shell](story-005-walletgate-stubwallet-mail-system.md) | Integration | Complete | ADR-0017, ADR-0003 |
| 006 | [Complaint buckets (F9), `ComplaintRaised`](story-006-complaint-buckets-f9.md) | Logic | Ready | ADR-0017 |
| 007 | [Consume routine + hand path (`ConsumeHand`), outcomes](story-007-consume-routine-hand-path-outcomes.md) | Integration | Ready | ADR-0017, ADR-0008 |
| 008 | [Thrown landings + hand → landing tick order](story-008-thrown-landing-and-tick-order.md) | Integration | Ready | ADR-0017, ADR-0015 |
| 009 | [M-1a shift stub, `SetDeliveriesState`, `DeliveriesState`](story-009-shift-stub-deliveries-state.md) | Integration | Ready | ADR-0017, ADR-0003 |
| 010 | [Client mirrors + client facts](story-010-client-mirrors-and-facts.md) | Integration | Ready | ADR-0016, ADR-0017 |
| 011 | [Spawn value (F1) + kind counts (F3)](story-011-spawn-value-f1-kind-counts-f3.md) | Logic | Complete | ADR-0017, ADR-0006 |
| 012 | [Batch schedule (F2), destination draws (F4), draw count](story-012-batch-schedule-f2-destinations-f4-draws.md) | Logic | Complete | ADR-0005, ADR-0017 |
| 013 | [Manifest roller + `PrepareShift`](story-013-manifest-roller-prepareshift.md) | Integration | Ready | ADR-0017, ADR-0005 |
| 014 | [Batch firing, Intake hand-off, backlog append](story-014-batch-firing-intake-backlog.md) | Integration | Ready | ADR-0017, ADR-0009 |
| 015 | [Mail/Run/Wallet event wire codecs](story-015-mail-event-wire-codecs.md) | Integration | Ready | ADR-0011, ADR-0017 |
| 016 | [Greybox `MailboxView` + outcome beats](story-016-mailbox-greybox-view-outcome-beats.md) | Visual/Feel | Ready | ADR-0019, ADR-0016 |

**Recommended order:** 001 → 002 → 003 → 004 → 005 → 006 → 007 → 009 → 008 → 010 → 011 → 012 → 013 → 014 → 015 → (016 once godot-host-shell has stories).

**Notes for `/story-readiness` and `/architecture-review` (no ADR or GDD edited here):**
- `AddressId` is created by `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (`Sim/Primitives`). Mail reuses it.
- Story 007: `ConsumeHand` returns a `HandConsumeSummary` (ADR-0017 Key Interfaces says `void`), because Delivery's reply needs requested/got/reason. This is a return-type addition and needs an ADR-0017 erratum.
- Story 007: per-unit `ConsumeUnit` (ADR-0017 §5) means +k container versions per k-unit Accept (ADR-0009 §3), against Grid Inventory H-R7-6/7's "+1 / one bump per touched container". This is GDD impact, flagged.
- M-R3-1b (lot spacing) is enforced by the world builder (world-stub story 004), not ContentValidator, at M-1a.
- Untraced requirements: none. Every M-1a story cites an existing TR. The `DeliveryOutcome` wire layout (story 015) is a story-level layout within ADR-0017's ≤ 19 B bound.

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): Track A (playable greybox): 005 (A1; Complete 2026-10-28, branch `story/mail-destinations-005`), 006 (A2), 007 (A3), 008 (A4), 009 (A4), 010 (A5), 013 (A5), 014 (A6), 015 (A7), 016 (A11). Earlier notes below are history.

`/story-readiness production/epics/mail-destinations/story-001-mail-table-paged-soa.md`, then `/dev-story` in the order above.
