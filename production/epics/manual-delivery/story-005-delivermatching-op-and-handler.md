# Story 005: `DeliverMatching` — the Delivery-owned OpKind, descriptor, 24 B payload (24 B MATCH / 28 B MISDELIVER_ONE on the wire) and host handler

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M–L (~1 day; ~300 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-21 (receives the Delivery system shell and its composition case from story 001: `[SimSystem(SystemId.ManualDelivery)]` — `SystemId.Delivery` does not exist; `ConstructShipped` case calling `DeliverAnchors.RegisterAll`; AC-7 composed world has 10 Deliver anchors; depends on mail-destinations 005; registry source is OQ-MD005-1. Manifest stamp not re-reviewed in this pass)

> **Origin (2026-10-16 `/architecture-review`):** the `DeliverMatching` op and handler moved from Inventory to Delivery. ADR-0017 forbids Inventory → Mail, and `ConsumeHand` is called "from Delivery's `deliver_matching` handler". The errata are recorded in ADR-0009 and ADR-0011 GDD Impact. This story replaces the handler half of `production/epics/grid-inventory/story-022-deliver-matching-handler.md` (whose wallet/match tests H-R7-6/7/11 and H-X-1 move here), and it is the descriptor that `production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md` registers its codec under.

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD5 the request, MD6 host handling); `design/gdd/grid-inventory.md` R7 `deliver_matching` row (CD D-B); `design/gdd/mail-destinations.md` R6 hand row
**Requirement**: `TR-delivery-008` (MATCH omits `selectedEntryId`; envelope + `mailboxId u32` + `holdStartTick u32` + ClaimedPosition 12 B [+ `selectedEntryId u32`] = 24 B MATCH / 28 B MISDELIVER_ONE; any other length → `bad_request`), `TR-delivery-009` (`holdStartTick` in the host domain; the host clamps the window), `TR-delivery-010` (host check: `mailboxId` is a registered Deliver anchor; no throttle), `TR-delivery-016` (no host state, no replicated state)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time; TR-delivery-008 was revised 2026-10-16)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline (primary for the op: §1 payload, §2 range-gated and batch rules, **errata 2026-10-16** for ownership and wire layout); ADR-0017 (§5 `ConsumeHand`, the hand path); ADR-0014 (§5 `CheckInteraction` chain, `ClaimedPosition`); ADR-0011 (§3 envelope `kind u16 · reqId u16`, codec per descriptor; erratum 24/28 B); ADR-0007 (OpKind block `0x0100–0x013F`, descriptor, reply ≤ 24 B extra, reason codes ≥ 64)
**ADR Decision Summary**: `DeliverMatching` is a **Delivery** `OpKind` in the Mail/Delivery block `0x0100–0x013F`, with its handler in `Sim/Delivery`. The handler runs `IPlayersApi.CheckInteraction` → the anchor check → `IMailApi.ConsumeHand`. Inventory supplies only primitives: the read-only match scan in cursor → hotbar → inventory → backpack row-major order, `ConsumeUnit`, version bumps and seals. The Sim payload is fixed at **24 B**: `mailboxId u32 · selectedEntryId u32 (0 = absent) · holdStartTick u32 · ClaimedPosition x, y, z i32`. The wire form (ADR-0011 codec) is envelope `kind u16 · reqId u16` + `mailboxId u32 · holdStartTick u32 · ClaimedPosition 12 B` [+ `selectedEntryId u32` on MISDELIVER_ONE only] = **24 B MATCH / 28 B MISDELIVER_ONE**, transport framing excluded. Presence is implied by length, and any other length is `bad_request`. Delivery declares its own reasons (`no_target`, `wallet_floor`, …). It is a batch op: op-level checks come first; a batch that commits nothing replies `ok:false` with the first op-level reason; a partial batch is `Ok` with `requested`/`got`.
**ADR Version**: ADR-0009 2026-10-16; ADR-0017 2026-10-16; ADR-0014 2026-10-16; ADR-0011 2026-10-16; ADR-0007 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Core network seam)**:
- Required: player requests are `OpKind`s in your block with an `OpDescriptor`; handlers validate fully before mutating, return exactly one `Reply`, declare system reason codes ≥ 64 in the descriptor (ADR-0003 §5, ADR-0007)
- Required: range-gated op handlers call `IPlayersApi.CheckInteraction(actor, anchor, ClaimedPosition)` first and map its result; the host range is `RangeMm + rangeSlackMm` (ADR-0014 §5)
- Required: every consume path goes through Mail's `MailConsumer.Consume` (via `IMailApi.ConsumeHand`); Inventory never calls Mail (ADR-0017)
- Required: payloads ≤ 32 B and padding-free (ADR-0009 §1); never use payload fields for identity (`Op.Actor` is stamped by the adapter) (ADR-0011)
- Forbidden: a wire codec in the Sim (the codec is network story 008's); a hold duration or predicted counts in the payload; a host throttle on deliver requests (MD6); `reqId` dedupe in the handler (adapter's job, ADR-0011)
- Guardrail: a 40-unit hold ≤ 0.2 ms (advisory, Mail M-PERF-2); 0 B on the handler path

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md` (M-MD5-2, M-MD6-1/2/3, M-EC-3b), `design/gdd/grid-inventory.md` (H-R7-6, H-R7-7, H-R7-11, H-X-1, moved from grid-inventory story 022) and the ADR-0009/0011 errata, scoped to this story:*

- [ ] **AC-1 Op shape.** `OpKind.DeliverMatching` is registered in `0x0100–0x013F` with an `OpDescriptor` whose payload struct is exactly 24 B (`Unsafe.SizeOf` test), field order `mailboxId, selectedEntryId, holdStartTick, ClaimedPosition{X, Y, Z}`, padding-free, with `selectedEntryId = 0` meaning absent. The descriptor exposes the `WireCodecId` network story 008 registers against, and declares the Delivery reason codes below.
- [ ] **AC-2 Matches (H-R7-6, H-R7-7).** At #17's mailbox with mocked Accept: 4 letters to #17 at inventory (3,1) → all 4 consumed; the same entry on the cursor → same result, cursor empty. Hotbar slot 2 = 5 letters #17, inventory = 2 Mediums #17, inventory also 6 letters #18 → the #17 entries are consumed in cursor → hotbar → inventory row-major order, reply `requested = got`, #18 untouched, and the Misdeliver branch is never invoked on the match branch.
- [ ] **AC-3 Fallback and `no_target` (H-R7-11, M-MD5-2).** No #17 entry, selected hotbar slot 2 = entry 60 (5 letters to #18, ids 300–304), `selectedEntryId = 60`, mocked Misdeliver → exactly mailId 300 consumed, entry 60 = 301–304. `selectedEntryId` absent → `no_target`, 0 consumed, wallet unchanged, no misdeliver outcome. One #17 letter elsewhere → only it is delivered (a match wins over the fallback; 0 penalties).
- [ ] **AC-4 Wallet (H-X-1 a–c, M-EC-3b).** Wrong house at wallet −496 ¢ → one unit consumed, wallet −500; again → `wallet_floor`, nothing changes; correct house → 3 consumed; a mocked Reject → nothing changes. Two players misdelivering 4 ¢ at −496 in one tick: the first succeeds (−500), the second gets `wallet_floor`.
- [ ] **AC-5 Host checks (M-MD6-1, M-MD6-3).** A `mailboxId` naming a destination with no Deliver anchor (fixture dock) → `no_target`. A claimed position 2,900 mm from the anchor is accepted; 3,200 mm → `out_of_range`. A test client at swim depth → `dry_land`, 0 consumed. Two requests from one player one tick apart are both applied (no throttle). `dead`/`frozen` from `CheckInteraction` map to their reply codes with nothing consumed.
- [ ] **AC-7 Composition (from story 001).** A world composed with `SimProfile.M1aGreybox` over the M-1a street (10 house lots) has a built `DeliverySystem`, and after `SimComposition.Build` returns, the Players anchor registry holds exactly 10 Deliver anchors — one `AnchorRef(destId, 0)` per destination, `Verb.Deliver`, `InteractMode.Hold` — registered while the world was `Constructed`. A fixture that makes registration fail (a pre-registered `Verb.Deliver` on one destination's `AnchorRef(destId, 0)` → `RegisterResult.DuplicateVerb`) makes `Build` throw `SimBuildException` with the new Delivery error code and the failing `RegisterResult` in the message; nothing is half-built silently.
- [ ] **AC-6 `holdStartTick` clamp and grace (M-MD6-2 a, b).** Delivery closing at the start of tick T: a hold begun at T − 12 applied at T − 1, T + 5 or T + 90 → Accept; applied at T + 91 → `closed`. One begun at T → `closed`, nothing consumed. A `holdStartTick` of applyTick + 200 is clamped to applyTick (→ `closed` after close); applyTick − 160 is clamped to applyTick − 150 (`maxHoldTicks` 60 + `closeGraceTicks` 90), never replaced. Two players posting to #17 in one tick both succeed, in arrival order.

---

## Implementation Notes

*Derived from the ADR-0009 errata (ownership, wire layout), ADR-0014 §5, ADR-0017 §5 and GDD MD6:*

- **Files:** `src/PerformativeMail.Sim/Delivery/DeliverMatchingOp.cs` (payload struct + `OpDescriptor` registration), `Delivery/DeliverMatchingHandler.cs` (`IOpHandler`), `Delivery/DeliveryReasons.cs`, `Delivery/DeliverySystem.cs` (`[SimSystem(SystemId.ManualDelivery)]` — `SystemId.ManualDelivery = 11` in `Core/SystemId.cs`; there is no `SystemId.Delivery`; one public constructor of ADR-0003 §4 injectables: `IPlayersApi` (from `SystemId.PlayerCharacter`), `IMailApi` (from `SystemId.MailDestinations`), `IInventoryApi` (from `SystemId.GridInventory`), `ContentSet`, `IStatSheet` (for story 001's `DeliverInteractable`), `SimCounters`, its settings record). `SystemGraph` already declares `ManualDelivery → PlayerCharacter, MailDestinations, GridInventory` (`Composition/SystemGraph.cs`) — no graph edit.
- **Composition case (handed over from story 001).** Add `case SystemId.ManualDelivery:` to `SimComposition.ConstructShipped` (`Composition/SimComposition.cs`; today it falls through to `return null`). It runs while the world is `SimState.Constructed` (systems are constructed before `builder.Freeze()`), builds the `DeliverySystem`, constructs one `DeliverInteractable(IStatSheet)` (`context.StatSheet`) and calls story 001's `DeliverAnchors.RegisterAll(IPlayersApi players, DestinationRegistry registry, ContentSet content, DeliverInteractable impl)` with `context.Api<IPlayersApi>(SystemId.PlayerCharacter)`, `context.Content` and the Mail-built `DestinationRegistry` (source: OQ-MD005-1). A result other than `RegisterResult.Ok` throws `SimBuildException` with a new append-only `SimBuildErrorCode` (next free value after `InventoryDefInvalid = 52` on `main` @ 25f477a — re-check after open PR #11 merges) naming the failing `RegisterResult`. Like `PlayerCharacter`, the case returns `null` when `context.World` is null (no lots, no Players).
- **Inputs the shell needs, by consumer:** `DeliverAnchors.RegisterAll` ← `IPlayersApi`, `DestinationRegistry`, `ContentSet`; `DeliverInteractable` ← `IStatSheet`. On `main` today: `IPlayersApi`, `ContentSet`, `IStatSheet`, `SimCounters`, `SimSystemContext.Api<T>/Content/StatSheet/World`, `DestinationRegistry` (`internal sealed`, `PerformativeMail.Sim.MailDestinations`). Not yet on `main` (arrive with this story's dependencies): `IMailApi` (mail-destinations 005/007), `IInventoryApi` (grid-inventory 015), `DeliverAnchors`/`DeliverInteractable` (story 001).
- **Reason codes** (declared on this descriptor, ≥ 64, never reused; numbering is this story's): `no_target`, `wallet_floor`, `closed`, `not_on_route`, `wont_fit`, `dry_land`, `out_of_range`, `position_stale`, `dead`, `frozen`. `bad_request` is Sim Core's 8 (structural). The mapping is: `CheckInteraction` result → its code (`Unavailable` carries the reason key; `deliver.dry_land` → `dry_land`, `mail.reject.closed` → `closed`, `mail.reject.not_on_route` → `not_on_route`); `MailRejectReason` → `closed`/`not_on_route`/`wont_fit`; `HandConsumeSummary.StoppedAtFloor` with 0 got → `wallet_floor`.
- **Handler order (ADR-0009 erratum, MD6):** (1) `CheckInteraction(actor, AnchorRef(mailboxId's destination, slot 0), claim)`; first failure → reply, nothing else. (2) Anchor check: `mailboxId` must name a registered **Deliver** anchor (the registry from story 001), else `no_target`. (3) Clamp `holdStartTick` to `[applyTick − (maxHoldTicks + closeGraceTicks), applyTick]`, where `maxHoldTicks = CeilDiv(max(manualInsertHold, holdCeil) × 30, 1000)` for the destination's type (60 at the 2.0 s ceiling → bound 150). (4) Scan the actor's own cursor and panel in cursor → hotbar → inventory → backpack row-major order with `DeliverScan` (story 002; ADR-0016 §3: one helper, host and client). Entries whose address equals the destination's and whose kind is accepted form the MATCH list. If the list is empty and `selectedEntryId ≠ 0` names a mail entry in the actor's **selected** hotbar slot, the list is that one entry; otherwise `no_target`. (5) `IMailApi.ConsumeHand(dest, list, actor, holdStartTick)` (`production/epics/mail-destinations/story-007-consume-routine-hand-path-outcomes.md`; it returns a `HandConsumeSummary`). (6) Reply: `ok` with `requested`/`got` in the reply extra (entries and units, saturating u16), or `ok:false` with the first reason when nothing committed.
- **Host-side scan input:** `DeliverScan.Enumerate` takes an `IPanelView`. On the host, use `IInventoryApi.HostPanelView(actor)` (TD-172 D1, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat): "HostPanelView per player slot") from `production/epics/grid-inventory/story-022-deliver-matching-handler.md` (re-scoped 2026-10-23 to this read-only primitive; the ADR-0009 erratum assigns the scan to Inventory). Do not add a second accessor here.
- The handler holds no state between ticks (TR-delivery-016) and writes no dump section. `reqId` dedupe is the adapter's (`production/epics/network-adapter-loopback/story-005-reqid-dedupe-reply-cache.md`).
- **Version bumps:** the GDD rows (grid-inventory R7: "one version bump per touched container") and per-unit `ConsumeUnit` (ADR-0017 §5, ADR-0009 §3: +1 per mutating call) disagree. This is already logged (mail-destinations story 007, decision log). Assert the ADR behaviour (+units per touched container) and do not assert "+1" until `/architecture-review` reconciles it.

---

## Open Questions

- **OQ-MD005-1 — OPEN (owner / ADR-0003 + ADR-0017). How does the composition case get the Mail-built `DestinationRegistry`?** ADR-0017 §3 has Mail build the registry; ADR-0017's `IMailApi` exposes no registry accessor (only `TryGetDestinationFacts`), and `DestinationRegistry` is not on ADR-0003 §4's closed injectable list, so it must not become a `DeliverySystem` constructor parameter. Options for the owner (not decided here): (a) an ADR-0017 erratum adding a read-only registry/ordinal accessor to `IMailApi`; (b) composition reads the registry from the `MailSystem` it built (composition-root only, no constructor injection) — needs an ADR-0003 note; (c) a `RegisterAll` overload over `IWorldQuery.Lots` (same lot order) plus `IMailApi.TryGetDestinationFacts` for the type — uses only listed surfaces but changes story 001's helper signature. AC-7 is testable once the owner picks one; nothing else in this story depends on it.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- `production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md`: the 24/28 B wire codec, `bad_request` on other lengths, fuzz.
- Story 004: building the request on the client (`TryBuildRequest`, `holdStartTick` from `IHostClock`). Story 006: reply handling on the client.
- Mail: pay, penalty, floor, complaint, `DeliveryOutcome` (mail-destinations story 007).
- Story 008: the race suite, the bell-fuzz (M-MD6-4) and the soak.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/delivery/DeliverMatchingHandlerTests.cs` (+ the payload-size unit test in `tests/unit/delivery/DeliverMatchingOpTests.cs`) and `tests/integration/delivery/DeliveryCompositionTests.cs` (AC-7: composed M-1a world → 10 Deliver anchors; the `DuplicateVerb` fixture → `SimBuildException`) — must exist and pass on net10.0 and net8.0, Debug and Release

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (Deliver anchors: `DeliverAnchors.RegisterAll`, `DeliverInteractable`), Story 002 (`DeliverScan`); `production/epics/grid-inventory/story-022-deliver-matching-handler.md` (`IInventoryApi.HostPanelView`); `production/epics/mail-destinations/story-005-walletgate-stubwallet-mail-system.md` (the Mail system shell that builds the `DestinationRegistry` in composition and publishes `IMailApi`); `production/epics/mail-destinations/story-007-consume-routine-hand-path-outcomes.md` (`ConsumeHand`); `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`ConsumeUnit`, `EntryRef`); `production/epics/simulation-core/story-013-op-reply-types-descriptors.md`, `production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md`; `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md` (`IPlayersApi.CheckInteraction`), `production/epics/player-character/story-012-anchor-registry-iinteractable-spatial-grid.md` (anchor registry)
- Unlocks: `production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md`, Story 007, Story 008; `production/epics/network-adapter-loopback/story-009-sc-x-2-manual-delivery-over-loopback.md`
