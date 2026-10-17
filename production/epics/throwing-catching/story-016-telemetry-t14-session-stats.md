# Story 016: T14 telemetry — per-launch and per-terminal records, `redAgeAtReleaseMs` and the throw-share analyser (session stats file)

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Blocked
> **Layer**: Feature (Client.Core / tooling; never the Sim tick path)
> **Type**: Logic
> **Estimate**: S (~0.5 day once unblocked)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **BLOCKED: ADR-0024 (persistence and telemetry, session stats file) is not written.** `TR-throwing-020`'s governing ADR is ADR-0024 (registry coverage: gap; "Planned — not written (Persistence)"). The session stats file's location, format, flush policy and write path are undecided, so the sink half of this story cannot be implemented. Write it with `/architecture-decision` (ADR-0024), accept it, then run `/story-readiness`. The record model and analyser below can be developed against an in-memory sink in the meantime, but the story is not Ready until the file contract exists. (Same precedent: `production/epics/manual-delivery/story-009-telemetry-latched-branch-handshare.md`.)

## Context

**GDD**: `design/gdd/throwing-catching.md` (T14 telemetry; Open Questions 14; Fantasy tests 3 and 4; AC TC-T14-1, TC-FE-3)
**Requirement**: `TR-throwing-020` (telemetry to the session stats file only; never shown to players)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T14-1 (M-1a fields), TC-FE-3.)*

**ADR Governing Implementation**: ADR-0024 (planned, **not written**): persistence & telemetry. Secondary: ADR-0016 (telemetry is fed from published client state and `PresentationEventStream`, never from the Sim tick path) and ADR-0015 (the events it reads) — Accepted
**ADR Decision Summary**: — (pending ADR-0024). From ADR-0016: telemetry consumers read the published preview/release state and domain-event cursors; nothing in the Sim writes a file or reads a clock.
**ADR Version**: ADR-0024 unversioned (not written); ADR-0016 2026-10-16; ADR-0015 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: LOW (Client.Core; file IO path per ADR-0024)
**Engine Notes**: Re-derive once ADR-0024 exists. No Godot API is expected; if the file path uses `OS.GetUserDataDir()` or `FileAccess`, the Godot-side shim belongs to the godot-host-shell epic.

**Control Manifest Rules (Global)**:
- Required: the Sim reads no clock and writes no file; telemetry lives outside the Sim (Global rules)
- Forbidden: a telemetry write inside `Tick()`; showing telemetry to players; any bonk/fumble counter reachable from Payday, statistics, achievements or perks (CD ruling, T8)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (T14, TC-T14-1, TC-FE-3), scoped to the M-1a fields:*

- [ ] **Per launch.** Every sent release logs `units`, entry kind, class, `hop` (always 0 at M-1a), `originClamped` (from the `ThrowLaunched` flag) and `redAgeAtReleaseMs` (from story 007's `ReleaseRecord`: how long the arc had been red at release; 0 if it was not red), keyed by `seq`/`flightId`.
- [ ] **Per terminal.** Every `FlightResolved` logs the outcome (terminal), distance from origin (mm, integer horizontal) and flight ticks.
- [ ] **TC-T14-1 (M-1a form).** The stats file holds every M-1a T14 field; the schema reserves, but does not yet populate, the catch (`armedBeforeLaunch`, thrower/catcher ids), no-catch-cause and bonk/fumble fields (M0). A schema check asserts no bonk or fumble field exists in any Payday, statistics, achievement or perk data type (vacuous at M-1a; kept as an architecture test so it bites when those types appear).
- [ ] **TC-FE-3 (analyser).** Given a stats log, the analyser reports the throw share and hand share of player-driven correctly delivered units per shift × build bucket (Manual Delivery test 3: derived from `DeliveryOutcome.via` over units with pay > 0, dead letters excluded; "insufficient sample" below 200 units) and the `redAgeAtReleaseMs` distribution (min / p50 / p90 / max and the count of releases with red age < 250 ms — the "flick" bucket). Synthetic logs of known composition reproduce the shares exactly. The hand-share half defers to manual-delivery story 009's definition; this story must not fork it.
- [ ] **Never in the tick path.** A test asserts the collector subscribes only to `PresentationEventStream` cursors and the published `ReleaseRecord` ring, allocates nothing per tick beyond its pre-sized buffer, and that `Sim/Throwing/` has no reference to the stats sink.
- [ ] **Failure is visible.** If the sink cannot be opened or a write fails, the session continues, a counter and a one-line debug log record it, and the analyser reports "log incomplete" rather than computing shares from a partial file.

---

## Implementation Notes

- Wait for ADR-0024 for everything about the file. Do not guess a path or format.
- `redAgeAtReleaseMs` is a **client-side** metric (the host never learns the preview's colour history); in solo it lives in the same process as the host. It rides on `ReleaseRecord`, not on the wire.
- The analyser is a pure function over parsed records and can live in the tools assembly.

---

## Out of Scope

- Catch / no-catch-cause / bonk / fumble / hop-chain telemetry (M0). The playtest protocols (producer, qa-lead). The stats file writer itself until ADR-0024.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowTelemetryTests.cs` (`[Trait("Id", "TC-T14-1")]`, `TC-FE-3`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0024 accepted; Stories 007, 010, 011, 017 (data sources); `production/epics/manual-delivery/story-009-telemetry-latched-branch-handshare.md` (hand-share definition, also Blocked on ADR-0024)
- Unlocks: playtest instruments for fantasy tests 3 and 4 (no story depends on it)
