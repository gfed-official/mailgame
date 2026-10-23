# Story 016: T14 telemetry — per-launch and per-terminal records, `redAgeAtReleaseMs` and the throw-share analyser (session stats file)

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature (Client.Core / tooling; never the Sim tick path)
> **Type**: Logic
> **Estimate**: S (~0.5 day; reuses the manual-delivery 009 writer)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (unblocked against ADR-0024 slice)
> Mail Review PR #47 B4 fix 2026-10-23: nothing in `src/` emits `DroppedBuffers` — the counter and its `session_end` field are now defined by manual-delivery story 009 AC-0c and this story only cites them; TC-FE-3 narrowed to per-session buckets because no record or event carries a shift index or a build id (manual-delivery 009 AC-3). Second pass (same day, audit of the rewrite): "log incomplete" also covers `session_end.reason = "fault"` (009 AC-0b trigger 4); the analyser's home is named (no tools assembly exists).

> **UNBLOCKED 2026-10-23.** ADR-0024 (the M-1a session stats file slice, `docs/architecture/adr-0024-session-stats-file-m1a.md`) is Accepted (Evan, Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"). The writer, port and Godot shim are built by manual-delivery story 009; this story adds only its record types, the collector and the analyser.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T14 telemetry; Open Questions 14; Fantasy tests 3 and 4; AC TC-T14-1, TC-FE-3)
**Requirement**: `TR-throwing-020` (telemetry to the session stats file only; never shown to players)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T14-1 (M-1a fields), TC-FE-3.)*

**ADR Governing Implementation**: ADR-0024: Session Stats File (M-1a slice) — Accepted 2026-10-23. Secondary: ADR-0016 (telemetry is fed from published client state and `PresentationEventStream`, never from the Sim tick path) and ADR-0015 (the events it reads) — Accepted
**ADR Decision Summary**: ADR-0024: producers call `ISessionStatsSink.Append(in record)` (allocation-free after warm-up); one JSONL line per record `{"v":1,"type":…,"tick":…}` in `user://stats/session-<UTC>.jsonl`; flush at run end, on quit and every 60 s; a failure warns, counts and drops; a missing `session_end` line or a drop count means "log incomplete". From ADR-0016: telemetry consumers read the published preview/release state and domain-event cursors; nothing in the Sim writes a file or reads a clock.
**ADR Version**: ADR-0024 2026-10-23; ADR-0016 2026-10-16; ADR-0015 2026-10-16

**Engine**: Godot 4.7.1 (.NET) | **Risk**: LOW (Client.Core; file IO path per ADR-0024)
**Engine Notes**: None. No Godot API here; the `FileAccess` shim is manual-delivery story 009's.

**Control Manifest Rules (Global)**:
- Required: the Sim reads no clock and writes no file; telemetry lives outside the Sim (Global rules)
- Forbidden: a telemetry write inside `Tick()`; showing telemetry to players; any bonk/fumble counter reachable from Payday, statistics, achievements or perks (CD ruling, T8)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (T14, TC-T14-1, TC-FE-3), scoped to the M-1a fields:*

- [ ] **Per launch.** Every sent release logs `units`, entry kind, class, `hop` (always 0 at M-1a), `originClamped` (from the `ThrowLaunched` flag) and `redAgeAtReleaseMs` (from story 007's `ReleaseRecord`: how long the arc had been red at release; 0 if it was not red), keyed by `seq`/`flightId`.
- [ ] **Per terminal.** Every `FlightResolved` logs the outcome (terminal), distance from origin (mm, integer horizontal) and flight ticks.
- [ ] **TC-T14-1 (M-1a form).** The stats file holds every M-1a T14 field; the schema reserves, but does not yet populate, the catch (`armedBeforeLaunch`, thrower/catcher ids), no-catch-cause and bonk/fumble fields (M0). A schema check asserts no bonk or fumble field exists in any Payday, statistics, achievement or perk data type (vacuous at M-1a; kept as an architecture test so it bites when those types appear).
- [ ] **TC-FE-3 (analyser; M-1a bucketing narrowed).** Given a stats log, the analyser reports the throw share and hand share of player-driven correctly delivered units **per session** — one `user://stats/session-<UTC>.jsonl` file is one bucket, labelled with its UTC stamp (manual-delivery story 009 AC-3; Manual Delivery test 3: derived from `DeliveryOutcome.via`, ADR-0017 §9 l.171, over units with pay > 0, dead letters excluded; "insufficient sample" below 200 units) and the `redAgeAtReleaseMs` distribution (min / p50 / p90 / max and the count of releases with red age < 250 ms — the "flick" bucket). Synthetic logs of known composition reproduce the shares exactly. The GDD's per shift × build bucket (l.1273) is not computed at M-1a and the analyser must not invent either field: no record or event carries a shift index (the only `Shift` values in `src/` are the content defs `ShiftMix.Shift` and `MailKindDef.UnlockShift` and the per-piece `MailRow.SpawnShift` / `DeadlineShift`; none is run state, and the Run Structure orchestrator of ADR-0003 phase 12 has no story) or a build id (no `Version` in `Directory.Build.props` or the csproj files; no `application/config/version` in `project.godot`). Stamping both is the M0 Run Structure / Persistence amendment's. The hand-share half defers to manual-delivery story 009's definition; this story must not fork it. *(Mail Lead planning default, pending Evan.)*
- [ ] **Never in the tick path.** A test asserts the collector subscribes only to `PresentationEventStream` cursors and the published `ReleaseRecord` ring, allocates nothing per tick beyond its pre-sized buffer, and that `Sim/Throwing/` has no reference to the stats sink.
- [ ] **Failure is visible (mechanism owned by manual-delivery story 009 AC-0c; this story adds no counter).** If the sink cannot be opened or a write fails, the session continues; the writer's `SessionStatsWriter.DroppedBuffers` (`src/PerformativeMail.Client.Core/Telemetry/SessionStatsWriter.cs`, built by manual-delivery 009) increments, its one-time `warn` line is logged, and `Close()` writes `"droppedBuffers":<n>` into the `session_end` line. This story's analyser reads only the file: a missing `session_end` line, or `droppedBuffers` > 0 in it, or `"reason":"fault"` in it (009's `GameLoop.FaultSink` wrap, AC-0b trigger 4), makes it report "log incomplete" instead of computing shares from a partial file. A test feeds (a) a log with no `session_end`, (b) a log whose `session_end` has `"droppedBuffers":1`, (c) a log whose `session_end` has `"reason":"fault"`, and (d) a clean log, and gets "log incomplete" three times and then the shares.

---

## Implementation Notes

- Record types (Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)): `throw_launch` (`seq`, `flightId`, `units`, `entryKind`, `class`, `hop`, `originClamped`, `redAgeAtReleaseMs`) and `throw_terminal` (`flightId`, `terminal`, `distanceMm`, `flightTicks`), plus reserved, unpopulated M0 fields per TC-T14-1. The file itself is ADR-0024's; do not add another path.
- "Log incomplete" = no `session_end` line, or `droppedBuffers` > 0 in that line, or `reason` = `fault` in it. All are written by manual-delivery story 009's `SessionStatsWriter.Close()` (its AC-0c: `{"v":1,"type":"session_end","tick":…,"records":…,"droppedBuffers":…,"reason":"stopped"|"quit"|"fault"}`); the run-end, quit, 60 s and fault triggers are 009's AC-0b (`SimWorld.State` → `SimState.Stopped`, `SessionStatsNode._ExitTree`, `IMonotonicClock`, the `GameLoop.FaultSink` wrap). Nothing in this story flushes or closes the file.
- `redAgeAtReleaseMs` is a **client-side** metric (the host never learns the preview's colour history); in solo it lives in the same process as the host. It rides on `ReleaseRecord`, not on the wire.
- The analyser is a pure function over parsed records in `src/PerformativeMail.Client.Core/Telemetry/ThrowShareAnalyser.cs`, beside 009's `HandShareAnalyser.cs`, whose hand-share function it calls rather than re-deriving; there is no tools assembly (`tools/` holds only `GenTrigQ15` and the Roslyn `PerformativeMail.Analyzers`) *(Mail Lead planning default, pending Evan)*.

---

## Out of Scope

- Catch / no-catch-cause / bonk / fumble / hop-chain telemetry (M0). The playtest protocols (producer, qa-lead). The stats file writer itself (manual-delivery story 009).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* QL-STORY-READY: `/story-readiness` 2026-10-23 (m1a-replan).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowTelemetryTests.cs` (`[Trait("Id", "TC-T14-1")]`, `TC-FE-3`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0024 (Accepted 2026-10-23); Stories 007, 010, 011, 017 (data sources); `production/epics/manual-delivery/story-009-telemetry-latched-branch-handshare.md` (the writer and the hand-share definition)
- Unlocks: playtest instruments for fantasy tests 3 and 4 (no story depends on it)
