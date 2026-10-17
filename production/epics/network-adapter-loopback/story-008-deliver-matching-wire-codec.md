# Story 008: deliver_matching wire codec (24 B MATCH / 28 B MISDELIVER_ONE)

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S (≈ 2–4 hours once unblocked; ≈ 150 lines incl. tests)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-10-16

> **Unblocked 2026-10-16 (story hygiene) — formerly BLOCKED on a story dependency only; now Ready with that dependency listed under Depends on.**
> 1. ~~Payload conflict~~ **RESOLVED 2026-10-16** by `/architecture-review` (technical-director erratum, recorded in ADR-0011 and ADR-0009 GDD Impact). The wire carries the claimed position, as grid-inventory R7, ADR-0009 §1 and ADR-0014 §5 require. The layout is envelope `kind u16 · reqId u16` + `mailboxId u32 · holdStartTick u32 · ClaimedPosition (x, y, z i32 mm, 12 B)` [+ `selectedEntryId u32` on MISDELIVER_ONE] = **24 B MATCH / 28 B MISDELIVER_ONE**. Manual Delivery's 12/16 B figure is superseded (GDD sync pending). TR-delivery-008 is revised in `docs/architecture/tr-registry.yaml`.
> 2. **The op must exist.** The codec registers under the `DeliverMatching` `OpKind` descriptor and payload struct. Per the same-day ADR-0009 ownership erratum, these are **Delivery-owned** (Mail/Delivery block `0x0100–0x013F`; handler in `Sim/Delivery`, `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`), not grid-inventory story 022. That descriptor and its 24 B payload struct are built by `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` — implement after it (Depends on).

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD5, Network table, M-NET-1); `design/gdd/simulation-core.md` §Network N4
**Requirement**: `TR-delivery-008` (`deliver_matching`: MATCH omits `selectedEntryId`; 24/28 B packed incl. claimed position), `TR-delivery-009` (`holdStartTick` in the host domain via `IHostClock`)
*(Requirement text: `docs/architecture/tr-registry.yaml` — TR-delivery-008 revised 2026-10-16 to 24/28 B)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§3 codec per descriptor) — primary; ADR-0009: Inventory Op Pipeline (§1 payload + 2026-10-16 errata) — secondary; ADR-0007 (§4 `WireCodecId`/`WireSize`)
**ADR Decision Summary**: Codecs live in the adapter core keyed by the descriptor's `WireCodecId`; envelope `kind u16 · reqId u16 · payload`; the Sim's payload struct stays fixed-size while the wire form may be compact (a presence-dependent length is the codec's business, validated as `bad_request` on mismatch).
**ADR Version**: 2026-10-16 (ADR-0011, ADR-0009, ADR-0007)

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core — network seam)**:
- Required: codec in the adapter core registry; decode zero-initialises the `Op`; trailing bytes / wrong length → `bad_request`; `holdStartTick` in the host tick domain.
- Forbidden: a codec in the Sim; hold duration or predicted counts in the request.
- Guardrail: 24 B MATCH / 28 B MISDELIVER_ONE including the 4 B envelope (transport framing excluded) — ADR-0011 GDD Impact erratum 2026-10-16.

---

## Acceptance Criteria

*From TR-delivery-008/009 and Manual Delivery M-NET-1 (size half), scoped to this story:*

- [ ] **AC-1** At maximum field values, a MATCH `deliver_matching` request serialises to exactly 24 B (envelope 4 B + `mailboxId` u32 + `holdStartTick` u32 + `ClaimedPosition` x,y,z i32), and a MISDELIVER_ONE request serialises to exactly 28 B (+ trailing `selectedEntryId` u32). Both round-trip bit-exactly into the 24 B Sim payload (MATCH decodes `selectedEntryId = 0`; negative position components survive). Frames of 22, 26 and 32 B decode to `bad_request`. A MISDELIVER_ONE frame carrying `selectedEntryId = 0` decodes to `bad_request` (0 means absent).

---

## Implementation Notes

- Register the codec under the `DeliverMatching` descriptor's `WireCodecId`; presence of `selectedEntryId` is implied by payload length (20 vs 24 bytes after the envelope); wire field order is `mailboxId, holdStartTick, ClaimedPosition.x, .y, .z, [selectedEntryId]` (optional field last), little-endian, no flags byte. ADR-0011 owns the envelope (`kind u16 · reqId u16`; Manual Delivery's `op code u8 · version + flags u8` is superseded).
- Add the row to Story 002's round-trip Theory by registration only.

---

## Out of Scope

- The `DeliverMatching` descriptor, payload struct and handler: manual-delivery epic (Delivery-owned per the ADR-0009 erratum 2026-10-16).
- The client hold → request flow: manual-delivery.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/net/DeliverMatchingCodecTests.cs` (`[Trait("Id", "M-NET-1")]`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002; `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` (Delivery-owned `DeliverMatching` OpKind, descriptor and 24 B payload). Payload conflict resolved 2026-10-16.
- Unlocks: Story 009
