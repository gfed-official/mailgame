# Story 009: Telemetry — `latchedBranch` per hold start and the `handShare` metric (session stats file)

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod incl. the stats writer and the Godot file shim + ~200 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (unblocked against ADR-0024 slice)

> **UNBLOCKED 2026-10-23.** ADR-0024 (the M-1a session stats file slice, `docs/architecture/adr-0024-session-stats-file-m1a.md`) is Accepted (Evan, Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"). This story builds the shared writer, so it grew from S to M; throwing-catching story 016 reuses it.

## Context

**GDD**: `design/gdd/manual-delivery.md` (M-FX-1 part b, l.540, telemetry; M-FX-4 fantasy test 3 instrument; Mail OQ26 `handShare`)
**Requirement**: `TR-delivery-022` (telemetry `latchedBranch`; `handShare` ≥ 20 %)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0024: Session Stats File (M-1a slice) — Accepted 2026-10-23. Secondary: ADR-0016 (telemetry is fed from published client state and `PresentationEventStream`, never from the Sim tick path)
**ADR Decision Summary**: Client.Core `SessionStatsWriter` behind `ISessionStatsSink.Append(in record)`, which buffers in memory and allocates nothing after warm-up. Writes go through an `IStatsFile` port (Godot `FileAccess` in `src/Game`, created at ADR-0019 boot step 3) to `user://stats/session-<UTC yyyyMMddTHHmmssZ>.jsonl`, one JSON object per line `{"v":1,"type":…,"tick":…}`. The writer flushes at run end, on quit and every 60 s; a failure warns once, counts and drops. A `session_end` line closes a clean session.
**ADR Version**: ADR-0024 2026-10-23; ADR-0016 2026-10-16; ADR-0019 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: LOW
**Engine Notes**: `FileAccess.Open(path, FileAccess.ModeFlags.Write)` and `DirAccess.MakeDirRecursiveAbsolute("user://stats")` in `src/Game` only (stable APIs; the ADR-0019 boot already uses `FileAccess`). Client.Core has no Godot reference (ADR-0001).

**Control Manifest Rules (Global)**:
- Required: the Sim reads no clock and writes no file; telemetry lives outside the Sim (Global)
- Forbidden: a telemetry write inside `Tick()`

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story:*

- [ ] **AC-0 (ADR-0024 writer).** With an in-memory `IStatsFile`, `SessionStatsWriter` writes the records in `Append` order, one JSON object per line with `"v":1`, `"type"` and `"tick"`. It flushes on `RunEnded`, on close and when the 60 s wall timer fires (injected clock), and writes `session_end` on clean close. `Append` allocates 0 B after warm-up. A throwing `IStatsFile` leaves the caller running, logs one warning and increments `DroppedBuffers`. `PerformativeMail.Sim` has no reference to `ISessionStatsSink` (architecture test).
- [ ] **AC-1 (M-FX-1 b, l.540)** Every hold start logs `latchedBranch` (MATCH / MISDELIVER_ONE) to telemetry, so playtest logs can audit fantasy test 1.
- [ ] **AC-2 (M-FX-4)** Given a synthetic outcome stream of 60 hand units with pay > 0, 40 thrown units with pay > 0, 20 hand dead-letter units and 10 feeder units, `handShare` = 0.60, the machine line reports 10 units, and a bucket under 200 units reports "insufficient sample" (ADVISORY gate at M-1a).

---

## Implementation Notes

- Compute `handShare` from `DeliveryOutcome.via` (Mail OQ26 instrument) over units with pay > 0, excluding dead letters, as M-FX-4 defines. Read the GDD's fantasy test 3 text before implementing.
- Files (Mail Lead planning default, pending Evan): `src/PerformativeMail.Client.Core/Telemetry/SessionStatsWriter.cs`, `ISessionStatsSink.cs`, `IStatsFile.cs`; `src/Game/Telemetry/UserStatsFile.cs` (`IStatsFile` over `FileAccess`, wired at boot step 3 in the godot-host-shell composition). Record type here: `{"type":"delivery_hold_start","latchedBranch":"MATCH"|"MISDELIVER_ONE",…}` and `{"type":"delivery_outcome","via":…,"pay":…,"dead":…}`. The `handShare` analyser is a pure function over parsed records (tools assembly or Client.Core).
- Records come from published client state and `PresentationEventStream` (ADR-0016), never from `Tick()`.

---

## Out of Scope

- The playtest protocols (MANUAL, qa-lead).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* QL-STORY-READY: `/story-readiness` 2026-10-23 (m1a-replan).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliveryTelemetryTests.cs` and `tests/unit/telemetry/SessionStatsWriterTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0024 (Accepted 2026-10-23); Story 004, Story 006
- Unlocks: `production/epics/throwing-catching/story-016-telemetry-t14-session-stats.md` (reuses the writer); M-1a gate metric (`handShare`, Mail OQ26 / MD OQ12)
