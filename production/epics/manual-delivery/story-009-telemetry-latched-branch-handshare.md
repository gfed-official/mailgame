# Story 009: Telemetry — `latchedBranch` per hold start and the `handShare` metric (session stats file)

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod incl. the stats writer and the Godot file shim + ~200 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (unblocked against ADR-0024 slice)
> Mail Review PR #47 B4 fix 2026-10-23: `RunEnded` is not an event anywhere in `src/` or the ADRs — AC-0b now names the real end-of-run signal (`SimWorld.State` → `SimState.Stopped`, latched through `IRunControl.RequestStop`) and the real quit and timer hooks; AC-0c names the drop counter (`SessionStatsWriter.DroppedBuffers`, written into `session_end`); AC-3 narrows reporting to per-session buckets because no record or event carries a shift index or a build id. Second pass (same day, audit of the rewrite): a faulted session now reaches the file through the real `GameLoop.FaultSink` hook (AC-0b trigger 4, `"reason":"fault"`) instead of being closed as a clean quit; the analyser's home is named (no tools assembly exists).

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

- [ ] **AC-0a (ADR-0024 writer: order, format, allocation, isolation).** With an in-memory `IStatsFile`, `SessionStatsWriter` writes the records in `Append` order, one JSON object per line with `"v":1`, `"type"` and `"tick"`. `Append` allocates 0 B after warm-up. `PerformativeMail.Sim` has no reference to `ISessionStatsSink` (an architecture test in `tests/unit/architecture/`, the `AssemblyRulesTests.cs` pattern).
- [ ] **AC-0b (flush triggers — the real signals; there is no `RunEnded` event in the code or in any ADR).** `SessionStatsWriter` exposes `Flush()` and an idempotent `Close()`; nothing else writes the file. The host calls them from three places, all outside the tick path, and a test drives each through doubles. (1) **Run end.** The Sim's only end-of-run state is `SimWorld.State` becoming `SimState.Stopped` (`src/PerformativeMail.Sim/Core/SimState.cs` l.17), which `IRunControl.RequestStop` latches from phase 12 (`src/PerformativeMail.Sim/Core/IRunControl.cs`; ADR-0003 l.92) or `SimWorld.Stop()` sets between ticks (`src/PerformativeMail.Sim/Core/SimWorld.cs` l.362). `SessionStatsHook : IPostPresentHook` (`src/Game/Telemetry/SessionStatsHook.cs`; the hook interface is `src/Game/Hosting/IPostPresentHook.cs`, registered with `GameLoop.RegisterPostPresentHook` before the first frame) polls that state after each `Present` through a `Func<SimState>` that the godot-host-shell story 005 composition supplies (boot steps 5–6 are still `UnwiredBootStep` in `src/Game/Boot/BootPlan.cs` l.94–95; until 005 lands the source is null and only triggers 2 and 3 run). The first observation of `Stopped` calls `Close()`, which writes `session_end`. A `Faulted` world is never seen by the hook (`GameLoop` runs no frame after a fault, `src/Game/Hosting/GameLoop.cs` l.361–364), so the fault is caught by trigger 4 below; the later quit (trigger 2) then writes `session_end` with `"reason":"fault"`, which the analyser reads as "log incomplete" (AC-0c). (2) **Quit or window close.** `SessionStatsNode : Node` (`src/Game/Telemetry/SessionStatsNode.cs`, added under `Main` by `StatsBoot`, below) overrides `_ExitTree()` and calls `Close()`; the tree exit covers the window close (`SceneTree.AutoAcceptQuit` default), `SceneTree.Quit` from the smoke runner (`src/Game/Smoke/SmokeRunner.cs` l.224) and the error screen's Quit (`src/Game/Screens/BootErrorScreen.cs` l.92). (3) **60 s wall timer.** The hook reads `IMonotonicClock` (`src/PerformativeMail.Sim/Hosting/IMonotonicClock.cs`; the loop's `FrameSequence.Clock`) and calls `Flush()` when ≥ 60 s have passed since the last flush; with a fake clock a test sees exactly one flush per 60 s and none inside it. (4) **Fault.** The only host-side fault signal is `IFaultSink.OnFault(string)` (`src/Game/Hosting/IFaultSink.cs` l.13–17), called once from `GameLoop.Fault` (`GameLoop.cs` l.403–415) through the settable `GameLoop.FaultSink` property (l.93–97), which boot points at `BootErrorPanel` before step 1. `StatsBoot` (step 3, so after that assignment) wraps the installed sink in a `SessionStatsFaultSink : IFaultSink` that calls `SessionStatsWriter.MarkFaulted(message)` (sets the `Close()` reason to `fault`, flushes nothing itself — the fault path must stay cheap and must not throw) and then forwards the message to the inner sink unchanged. A test faults a `GameLoop` double, then quits, and reads `"reason":"fault"` in `session_end`; a second test shows the inner sink still receives the message. The Run Structure orchestrator that will call `RequestStop` at Victory / Run Over (ADR-0003 phase 12; ADR-0015 l.126) has no story yet; this story does not create it and needs nothing from it. *(Mail Lead planning default, pending Evan: the hook, the node and the `Func<SimState>` seam.)*
- [ ] **AC-0c (drop counter and the `session_end` line; this is the mechanism throwing-catching story 016 "Failure is visible" cites).** A throwing `IStatsFile.Write` or `Flush` leaves the caller running, is reported once through the `Action<string> warn` the writer takes at construction (`src/Game` passes `GD.PushWarning`), increments `SessionStatsWriter.DroppedBuffers` (read-only `int` property in `src/PerformativeMail.Client.Core/Telemetry/SessionStatsWriter.cs`) and drops that buffer. `Close()` writes `{"v":1,"type":"session_end","tick":<last tick appended>,"records":<lines appended>,"droppedBuffers":<DroppedBuffers>,"reason":"stopped"|"quit"|"fault"}` (`fault` when `MarkFaulted` ran, AC-0b trigger 4; otherwise `stopped` if trigger 1 fired first, else `quit`). A test with a file that fails once then recovers sees `DroppedBuffers` = 1, one warning, the later records intact and `"droppedBuffers":1` in `session_end`. The analyser's rule is: `session_end` missing, or its `droppedBuffers` > 0, or its `reason` = `fault`, ⇒ "log incomplete". *(Mail Lead planning default, pending Evan: the counter and field names.)*
- [ ] **AC-1 (M-FX-1 b, l.540)** Every hold start logs `latchedBranch` (MATCH / MISDELIVER_ONE) to telemetry, so playtest logs can audit fantasy test 1. The record is built from the published interaction state when `FocusState.Latched` (`src/PerformativeMail.Sim/Players/Client/FocusState.cs` l.33) turns true, reading the branch from byte 0 of the latch's `OwnerPayload16` (`src/PerformativeMail.Sim/Players/OwnerPayload16.cs`; story 004 stores the branch kind there) — never from inside a ClientLayer step or `Tick()`.
- [ ] **AC-2 (M-FX-4)** Given a synthetic outcome stream of 60 hand units with pay > 0, 40 thrown units with pay > 0, 20 hand dead-letter units and 10 feeder units, `handShare` = 0.60, the machine line reports 10 units, and a bucket under 200 units reports "insufficient sample" (ADVISORY gate at M-1a). `via` is `DeliveryOutcome.via u8 {hand, throw, feeder, npc}` (ADR-0017 §9 l.171; produced by mail-destinations story 007, not yet in `src/`).
- [ ] **AC-3 (bucketing at M-1a — narrowed: no record or event carries a shift index or a build id).** The `handShare` analyser buckets per **session**: one `user://stats/session-<UTC>.jsonl` file is one bucket (ADR-0024 §2) and the output is labelled with the file's UTC stamp; a test feeding two session files gets two buckets. The GDD's per shift × build-level buckets (M-FX-4 l.67; OQ row 12 l.562 schedules that instrumentation "at M0") are not computed, and the analyser must not invent either field: the code has no live shift counter (`ShiftMix.Shift` and `MailKindDef.UnlockShift` under `src/PerformativeMail.Sim/Content/Defs/` are content defs and `MailRow.SpawnShift` / `DeadlineShift` in `src/PerformativeMail.Sim/Mail/MailRow.cs` l.49–52 are per-piece data, not run state; the Run Structure orchestrator has no story) and no build id (`Directory.Build.props` and the csproj files set no `Version`; `project.godot` sets no `application/config/version`). Stamping shift and build into the records belongs to the M0 Run Structure / Persistence amendment. *(Mail Lead planning default, pending Evan.)*

---

## Implementation Notes

- Compute `handShare` from `DeliveryOutcome.via` (Mail OQ26 instrument) over units with pay > 0, excluding dead letters, as M-FX-4 defines. Read the GDD's fantasy test 3 text before implementing.
- Files (Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)): `src/PerformativeMail.Client.Core/Telemetry/SessionStatsWriter.cs`, `ISessionStatsSink.cs`, `IStatsFile.cs`; `src/Game/Telemetry/UserStatsFile.cs` (`IStatsFile` over `FileAccess`). Boot wiring (B4 fix, pending Evan): `src/Game/Telemetry/StatsBoot.cs` is the `IBootContributor` for step 3 (`Step => BootPlan.PersistenceStep`, `src/Game/Boot/BootPlan.cs` l.36; one line in `BootPlan.CreateContributors`) that creates `UserStatsFile`, the writer, `SessionStatsNode`, `SessionStatsHook` and the `SessionStatsFaultSink` wrap of `GameLoop.FaultSink` (AC-0b). Nothing here edits ADR-0024. Record type here: `{"type":"delivery_hold_start","latchedBranch":"MATCH"|"MISDELIVER_ONE",…}` and `{"type":"delivery_outcome","via":…,"pay":…,"dead":…}`. The `handShare` analyser is a pure function over parsed records in `src/PerformativeMail.Client.Core/Telemetry/HandShareAnalyser.cs` — there is no tools assembly to put it in (`tools/` holds only `GenTrigQ15` and the Roslyn `PerformativeMail.Analyzers`), and Client.Core is the only Godot-free assembly besides the Sim *(Mail Lead planning default, pending Evan)*.
- Records come from published client state and `PresentationEventStream` (ADR-0016 §5 l.130; the stream itself is godot-host-shell story 006's), never from `Tick()`.

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

- Depends on: ADR-0024 (Accepted 2026-10-23); Story 004 (the latch's `OwnerPayload16` branch byte), Story 006; godot-host-shell story 005 (world composition: the `Func<SimState>` seam of AC-0b) and story 006 (`PresentationEventStream`); mail-destinations story 007 (`DeliveryOutcome.via`)
- Unlocks: `production/epics/throwing-catching/story-016-telemetry-t14-session-stats.md` (reuses the writer); M-1a gate metric (`handShare`, Mail OQ26 / MD OQ12)
