# Story 015: Wire codecs for Mail/Run/Wallet events — `DeliveryOutcome` ≤ 19 B with ordinal narrowing, `ComplaintRaised`, `BatchArrived`, `DeliveriesState`, `WalletChanged`

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature (codecs live in `Client.Core/Net`)
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (§Network; R16 `DeliveryOutcome`; M-NET-2)
**Requirement**: `TR-mail-015` (`DeliveryOutcome` one event per op, ≤ 19 B, canonical encoding), `TR-mail-014` (`DeliveriesState` reliable-ordered event, not on the snapshot)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (primary, §3 codec registry keyed by `WireCodecId`, §6 outbound batches); ADR-0017 (§1/§3 ordinal id maps: `destination u16` = `DestinationOrdinal`, `AddressOrdinal u16`; §9 `outcomeSeq` u16 on the wire with serial arithmetic); ADR-0008 (event wire form via the descriptor's `WireCodecId`)
**ADR Decision Summary**: Codecs live in the adapter core's registry, keyed by `WireCodecId`, as pure `TryDecode`/`Encode` over spans. The loopback uses the same codec as a remote peer (one code path). Mail's events carry full u32 ids in the Sim. The codec narrows `AddressId` to `AddressOrdinal u16` and destinations to `DestinationOrdinal u16` through id maps that are identical on every peer (built from the world tables), never by truncation. `outcomeSeq` is u32 in the Sim and u16 on the wire.
**ADR Version**: ADR-0011 2026-10-16; ADR-0017 2026-10-16; ADR-0008 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None (Client.Core is Godot-free).

**Control Manifest Rules (Core network seam + Feature)**:
- Required: codecs live in the adapter core's registry keyed by `WireCodecId` (pure `TryDecode`/`Encode` over spans) (ADR-0011 §3)
- Required: wire narrowing of `AddressId`/destination uses the ordinal id maps (ADR-0017 §1, §3)
- Forbidden: a wire codec in the Sim; truncating ids; Variant serialisation (ADR-0011)
- Guardrail: `DeliveryOutcome` ≤ 19 B on the wire at maximum field values (M-NET-2); mail ≤ 16 B/tick typical (budget measured at M0, M-NET-4)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md` (M-NET-2) and ADR-0011 §3/§6, scoped to this story:*

- [ ] **AC-1 (M-NET-2)** The canonical `DeliveryOutcome` encoding is ≤ 19 B at maximum field values (units 255, maximum varint cents, `intendedAddress` set) and round-trips: decode(encode(x)) equals x for every field after the documented narrowing (`outcomeSeq` mod 2^16 re-expanded by serial arithmetic against the mirror's last value; destination and address through the ordinal maps).
- [ ] **AC-2 Other events.** `ComplaintRaised`, `BatchArrived`, `DeliveriesState` and `WalletChanged` each round-trip through their codec. `DeliveriesState` and `DeliveryOutcome` are marked for the reliable-ordered channel.
- [ ] **AC-3 Loopback end-to-end.** In a host loopback session (`production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`), a hand consume (story 007's fixture, driven by a test op) produces `DeliveryOutcome` entries that reach the host `ClientLayer` inbox decoded and equal to the Sim payloads (after narrowing), in log order, in the next client tick.
- [ ] **AC-4 Robustness.** Each decoder returns failure (never throws) on truncated input, trailing bytes, an out-of-range ordinal or an unknown result/via value (the H-NET-5 fuzz harness from `production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md` runs over these codecs with 10,000 random inputs).

---

## Implementation Notes

*Derived from ADR-0011 §3/§6 and ADR-0017 §1, §3, §9:*

- **Files:** `src/PerformativeMail.Client.Core/Net/Codecs/MailEventCodecs.cs` (one codec per event kind), `Net/Codecs/WorldIdMaps.cs` (`DestinationOrdinal` ↔ `EntityId` and `AddressOrdinal` ↔ `AddressId`, built from `IWorldQuery.Lots` on both ends; Client.Core references Sim, which is the allowed direction).
- **`DeliveryOutcome` wire layout (story-level, ≤ 19 B):** `outcomeSeq u16 · destination u16 · actor u8 · via(2b)|result(2b)|flags(2b) packed u8 · kind u16 · units u8 (≤ 255; the 255-unit case is synthetic, M-F6-1c) · cents zigzag varint (≤ 5 B for int32) · lateUnits u8 · deadUnits u8 · complaintPermille varint (≤ 3 B) · [intendedAddress u16 iff result = misdeliver]`. Verify the maximum ≤ 19 B in the test. If the bound fails, tighten the field widths while staying within the Sim payload's ranges, and keep the layout documented in the codec file. ADR-0017 fixes only the ≤ 19 B bound and the ordinal narrowing.
- Register each event kind's `WireCodecId` on its `EventDescriptor` (ADR-0008). The registry is `production/epics/network-adapter-loopback/story-004-hostadapter-event-sink-outbound-batches.md`'s `IEventCodec` registry, which leaves feature codecs to their epics.
- `WalletChanged` (Economy stub) and `DeliveriesState` (Run stub) are carried here because their only M-1a producers are this epic's stubs.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Value-hint runs, JoinState and `MailShiftStats` wire formats (M0: TR-mail-016/017/021). M-NET-1/3/4 multi-peer and byte-budget tests (M0).
- Inventory event codecs (grid-inventory / network epics).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/MailEventCodecTests.cs` (+ the fuzz case) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 006, Story 007, Story 009, Story 014 (event kinds exist); `production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md`, `production/epics/network-adapter-loopback/story-004-hostadapter-event-sink-outbound-batches.md`, `production/epics/network-adapter-loopback/story-007-host-loopback-session-frame-timing.md`
- Unlocks: Story 010 end-to-end over loopback; Story 016; manual-delivery outcome beats over loopback
