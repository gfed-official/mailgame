# Story 009: Telemetry — `latchedBranch` per hold start and the `handShare` metric (session stats file)

> **Epic**: Manual Delivery
> **Status**: Blocked
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **BLOCKED: ADR-0024 (persistence and telemetry, session stats file) is not written.** `TR-delivery-022`'s governing ADR is ADR-0024 (registry coverage: gap). The session stats file's location, format and write path are undecided. Write it with `/architecture-decision` (ADR-0024), accept it, then run `/story-readiness`.

## Context

**GDD**: `design/gdd/manual-delivery.md` (M-FX-1b telemetry; M-FX-4 fantasy test 3 instrument; Mail OQ26 `handShare`)
**Requirement**: `TR-delivery-022` (telemetry `latchedBranch`; `handShare` ≥ 20 %)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0024 (planned, **not written**): persistence & telemetry. Secondary: ADR-0016 (telemetry is fed from published client state and `PresentationEventStream`, never from the Sim tick path)
**ADR Decision Summary**: — (pending ADR-0024)
**ADR Version**: ADR-0024 unversioned (not written); ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: LOW (Client.Core; file IO path per ADR-0024)
**Engine Notes**: Re-derive once ADR-0024 exists.

**Control Manifest Rules (Global)**:
- Required: the Sim reads no clock and writes no file; telemetry lives outside the Sim (Global)
- Forbidden: a telemetry write inside `Tick()`

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story:*

- [ ] **AC-1 (M-FX-1b)** Every hold start logs `latchedBranch` (MATCH / MISDELIVER_ONE) to telemetry, so playtest logs can audit fantasy test 1.
- [ ] **AC-2 (M-FX-4)** Given a synthetic outcome stream of 60 hand units with pay > 0, 40 thrown units with pay > 0, 20 hand dead-letter units and 10 feeder units, `handShare` = 0.60, the machine line reports 10 units, and a bucket under 200 units reports "insufficient sample" (ADVISORY gate at M-1a).

---

## Implementation Notes

- Compute `handShare` from `DeliveryOutcome.via` (Mail OQ26 instrument) over units with pay > 0, excluding dead letters, as M-FX-4 defines. Read the GDD's fantasy test 3 text before implementing.
- Everything about where and how the session stats file is written waits for ADR-0024.

---

## Out of Scope

- The playtest protocols (MANUAL, qa-lead).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliveryTelemetryTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0024 (not written); Story 004, Story 006
- Unlocks: M-1a gate metric (`handShare`, Mail OQ26 / MD OQ12)
