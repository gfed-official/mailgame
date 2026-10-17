# Story 009: SC-X-2 — a real Manual Delivery request from the host's own client over loopback

> **Epic**: Network Adapter Core — Loopback (M-1a)
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Estimate**: S (≈ 2–4 hours once unblocked; ≈ 150 lines of test)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

> **Unblocked 2026-10-16 (story hygiene):** previously BLOCKED on stories and on ADR-0014/ADR-0016, which are now Accepted. SC-X-2 names a **Manual Delivery request**. That needs (a) Story 008's `deliver_matching` codec, (b) the Delivery-owned `DeliverMatching` op and handler (`production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md`; ADR-0009 erratum moved it out of grid-inventory story 022), and (c) the manual-delivery client flow (hold → one request, `production/epics/manual-delivery/story-004-press-latch-cancels-request.md`). All exist and are listed under Depends on; the timing mechanics are already proven with a fixture op in Story 007 (AC-3).

## Context

**GDD**: `design/gdd/simulation-core.md` (SC-X-2; §Network N2); `design/gdd/manual-delivery.md` (MD5)
**Requirement**: `TR-delivery-008`, `TR-delivery-009` (`holdStartTick` via `IHostClock`), `TR-delivery-011` (duplicate `reqId` returns the original reply), `TR-sim-014`
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0011: Network Adapter Boundary (§2 loopback, §1 `IHostClock`, §3 dedupe) — primary; ADR-0007 (§6), ADR-0009 (`DeliverMatching`); ADR-0014/ADR-0016 — Accepted 2026-10-16 (manual-delivery prerequisites)
**ADR Decision Summary**: The host's own client reaches the Sim only through the loopback adapter with the remote codec; a host op enqueued in frame F applies at the next `Inputs.Apply`, Presentation sees the events in the frame that ran that tick, and the host `ClientLayer` sees the reply at exactly the next client tick.
**ADR Version**: 2026-10-16 (ADR-0011, ADR-0007, ADR-0009); ADR-0014/0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (plain C#) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core — network seam)**:
- Required: no "is host" path; `holdStartTick` from `IHostClock` (host: exact `CurrentTick`, offset 0); duplicate `reqId` → original reply, nothing applied.
- Forbidden: the host's client calling the Sim directly.
- Guardrail: none.

---

## Acceptance Criteria

*From GDD SC-X-2 (with the real request) and TR-delivery-011, scoped to this story:*

- [ ] **AC-1 SC-X-2.** Given a Manual Delivery `deliver_matching` request from the host's own client in frame F: it applies at the first `Inputs.Apply` after F; host Presentation sees the resulting events in the frame that ran that tick; the host `ClientLayer` sees the reply at exactly the next client tick, with no frame-delayed queue; the host `IHostClock` reads `CurrentTick` (and the request's `holdStartTick` equals it at hold start); and a duplicate of the request with the same `reqId` returns the original reply and delivers nothing more.

---

## Implementation Notes

- Re-run Story 007's harness with the real `M1aGreybox` profile (Grid + Manual Delivery systems) and the real codec; reuse its frame/tick assertions.

---

## Out of Scope

- Remote-peer variants and SC-X-3/X-4 — M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/integration/net/ManualDeliveryLoopbackTests.cs` (`[Trait("Id", "SC-X-2")]`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 007, Story 008; `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` (Delivery-owned `DeliverMatching` op + handler); `production/epics/manual-delivery/story-004-press-latch-cancels-request.md` (client request; ADR-0014/0016 Accepted)
- Unlocks: epic Definition of Done (SC-X-2)
