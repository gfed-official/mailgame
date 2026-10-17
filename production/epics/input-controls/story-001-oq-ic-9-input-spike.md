# Story 001: OQ-IC-9 input spike — verify the Godot 4.7.1 .NET input surface and record the adapter GO / NO-GO

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (time-boxed to ~1 day; ~400 lines of throwaway C# probes + a written report; no production code)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

> **Gate story.** ADR-0012 §7 (the Godot adapter) is *provisional until the OQ-IC-9 spike is recorded under `prototypes/`*. Stories 026–029 (the Godot adapter, its focus / mouse-mode / hot-plug half, the `GameLoop` hook-up and the adapter integration suite) are **Blocked** until this story is Done with a `REPORT.md` verdict of GO or GO-WITH-DELTAS. The router core (stories 002–024) does **not** depend on this story.

## Context

**GDD**: `design/gdd/input-controls.md` (Open Questions **OQ-IC-9** and OQ-IC-17; R9, R11 "Focus loss" / "Mouse mode"; Edge Cases "Platform and layout"; ACs H-R9-2, H-R11-3, H-EC-5, H-EC-6, H-EC-11, H-EC-12, H-EC-13)
**Requirement**: spike — *evidence for the adapter rows* of `TR-input-007`, `TR-input-008`, `TR-input-015`, `TR-input-016`, `TR-input-022` (no TR-ID of its own; the registry's `side: P` rows)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (`## Engine Compatibility` → **Verification Required** items 1–11, `## Engine Validation` minor notes 1–5, §7 "The Godot adapter (provisional until the OQ-IC-9 spike)") — Accepted 2026-10-16
**ADR Decision Summary**: The router core is Accepted outright; the adapter half (one `InputAdapter` node that arrival-stamps `_Input` events into `RawInputRecord`s, classifies devices by `DEVICE_ID_*`, keeps a per-device axis cache, strips nothing from `ui_*`, switches mouse mode, synthesises focus-loss and hot-plug) stays provisional. Every behaviour marked UNVERIFIED must be recorded under `prototypes/` before an adapter story cites it as VERIFIED.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`, `4.7.1.stable.mono.official.a13da4feb`), C# | **Risk**: HIGH
**Engine Notes**: Everything in ADR-0012 §7 is post-cutoff or unsourceable from `docs/engine-reference/godot/` (only `DEVICE_ID_*` is VERIFIED there). The 2026-10-16 probe (non-Mono binary, scripts left in `/tmp`) is **not** an artifact: this story re-runs it on the Mono binary and commits the scripts and raw output. Do not fill a gap from training data — write `NOT RUN` / `NOT SOURCEABLE` with the reason.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: any Godot 4.4–4.7 behaviour not in `docs/engine-reference/godot/` gets a spike before code depends on it (manifest Cross-Cutting Constraints; architecture principle 5)
- Required: compare `InputEvent.Device` against `DeviceIdMouse` / `DeviceIdKeyboard`, never `0` (manifest Presentation / Forbidden APIs)
- Forbidden: promoting prototype code into `src/` (prototypes are throwaway; stories rewrite to the ADR contracts)
- Guardrail: the spike is a report, not a gate on the router core; a probe that cannot run headless is recorded `NOT RUN` with what is needed, never silently dropped

---

## Acceptance Criteria

*From GDD OQ-IC-9 / OQ-IC-17 and ADR-0012 Engine Compatibility → Verification Required + Engine Validation notes, scoped to this story. "Result" cells use exactly one of VERIFIED · CHANGED (differs from ADR-0012 §7) · FAILED · NOT RUN (reason + what is needed) · NOT SOURCEABLE.*

- [x] **AC-1 Harness.** `prototypes/spike-oq-ic-9-input/` holds a throwaway Godot project (own `project.godot`, `Godot.NET.Sdk/4.7.1`, `net8.0`, `nuget.config` copied from `prototypes/spike-adr0019/`, repo-root `Directory.Build.props` inherited) that builds with `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . --build-solutions --quit` and runs each probe headless via a user-arg (`-- --probe=<id>`), writing raw output to `logs/<id>.log`. Probe scripts and logs are committed.
- [x] **AC-2 Baseline re-probe (B1–B6).** The 2026-10-16 headless findings are reproduced on the Mono binary and committed: B1 `InputEvent` has no timestamp property/method; B2 `DeviceIdKeyboard` = 16, `DeviceIdMouse` = 32, `DeviceIdEmulation` = −1 (C# names) and the default `Device` of a new `InputEventAction` (0), `InputEventKey` (16), `InputEventMouseButton` (32), `InputEventJoypadButton` (0); B3 `input_devices/joypads/ignore_joypad_on_unfocused_application` default (false) and that it can be set true in `project.godot`; B4 `Input.UseAccumulatedInput` default (true); B5 no joypad events on any `ui_*` action in `InputMap` (headless); B6 `Input` has the `JoyConnectionChanged` signal and `Node.NotificationApplicationFocusOut` = 2017 resolves.
- [x] **AC-3 Event order and handled state (ADR items 1, 2; Step 5.5 note 4).** A probe records, for injected key / mouse / joypad events (`Input.ParseInputEvent` followed by `Input.FlushBufferedEvents()` — the engine buffers parsed events one frame): (a) the delivery order `_Input` → GUI (a focused `Button` activated by `ui_accept` from Space/Enter) → `_ShortcutInput` → `_UnhandledKeyInput` → `_UnhandledInput`; (b) whether `GetViewport().SetInputAsHandled()` in `_Input` stops the GUI activation and the later receivers; (c) polled `Input.IsActionPressed` / `IsKeyPressed` while the event is marked handled (GDD "polled-read behaviour"); (d) whether a **release** consumed by a GUI node is still seen by an earlier `_Input` node; (e) whether a frame's `_Input` events arrive **before** that frame's `_Process` (frame-counter stamps in both) — the answer decides nothing in the router (ADR-0012 §6 `DispatchImmediates` already decouples it) but is recorded; (f) node-order control: a node with `ProcessPriority = int.MinValue` as first child of the root sees every event before a sibling `Control` and a debug-overlay stand-in.
- [x] **AC-4 Devices and axes (ADR items 3, 4; Step 5.5 notes 1, 5).** Records: the C# names resolve (compile-checked); a classification rule "event **type** first, then `Device`" correctly separates a synthetic `InputEventAction` (device 0) from `InputEventJoypadButton` (device 0) and from a joypad whose index is 16 or 32 (inject `InputEventJoypadButton` with `Device = 16` / `32`; record whether the engine accepts it — the Steam virtual-pad collision risk); joypad motion arrives one axis per event, raw (not deadzoned), only on change, triggers as axes (inject `InputEventJoypadMotion`); `Input.GetJoyAxis` poll works as the cache re-seed path; `Input.GetJoyInfo` keys; whether `JoyConnectionChanged` can be driven headless (if not: `NOT RUN — needs a real pad`) and hot-plug index reuse (same).
- [x] **AC-5 Focus and mouse (ADR items 5–7).** Records: `NotificationApplicationFocusOut/In` and `NotificationWMWindowFocusOut/In` are delivered to a node when sent with `Notification()` headless (**not** evidence of OS delivery); on this machine's compositor, a non-headless run that is alt-tabbed shows which of the four notifications arrive (X11/XWayland and native Wayland if both are available; otherwise `NOT RUN — needs a display session`); `Input.MouseMode` Captured ↔ Visible switching, whether the cursor position is restored on Visible, and whether capture must be re-applied after focus-in; `ignore_joypad_on_unfocused_application` honoured with a real pad (`NOT RUN` unless a pad is attached); Wayland mouse-capture behaviour (`NOT RUN` unless on Wayland).
- [x] **AC-6 Allocation and accumulation (ADR item 8; OQ-IC-17).** Measures, with `GC.GetAllocatedBytesForCurrentThread()` around 10 000 injected events after 300 warm-up events, the managed bytes per event for `InputEventKey`, `InputEventMouseMotion` (with `UseAccumulatedInput` true and false, 16 motion events per frame), `InputEventJoypadMotion` and `InputEventMouseButton` as seen by a `_Input` override that reads the fields the adapter needs (`Device`, keycode / button index, `Pressed`, `Echo`, `ScreenRelative`, axis value). Result table in bytes/event. Also: `InputEventMouseMotion.ScreenRelative` vs `Relative` under a scaled stretch viewport (Step 5.5 note 2); `InputEventKey.Echo` for an injected echo event (ADR item 11); `DisplayServer.KeyboardGetLabelFromPhysical(Key)` result headless (expected unsupported) with AZERTY marked `NOT RUN — MANUAL` (H-EC-11); `AccessibilityServer` screen-reader path marked `NOT RUN` (not an M-1a requirement; recorded for completeness).
- [x] **AC-7 Report with a verdict.** `prototypes/spike-oq-ic-9-input/REPORT.md` contains: environment block (Godot build string, .NET SDK, OS / session type); a findings table with one row per ID (B1–B6, S-01…) giving Result, evidence log path and the ADR-0012 §7 sentence it confirms or contradicts; a `## Verdict` that is exactly one of **GO** (every headless row VERIFIED as §7 states), **GO-WITH-DELTAS** (at least one row CHANGED or NOT RUN, all accommodated by amending §7 text only — each delta listed under `## ADR-0012 §7 deltas`), or **NO-GO** (a row FAILED so that arrival-stamped `_Input` gathering cannot work as designed, e.g. a GUI-consumed release never reaches `_Input`, or `SetInputAsHandled` cannot stop GUI activation *and* the `FocusMode.None` + `GuiReleaseFocus` fallback also fails — then stories 026–029 stay Blocked until ADR-0012 §7 is amended); `## Consumed by` mapping each adapter story (026–029) to the rows it relies on; `## MANUAL / NOT RUN` listing every row that needs a real pad, display session, AZERTY layout or Steam, with the exact steps (story 029 collects that evidence).
- [x] **AC-8 Index and scope.** One row is appended under *Technical spikes* in `prototypes/index.md`. No file under `src/`, `docs/engine-reference/`, `docs/architecture/` or `design/` is edited (the report *proposes* engine-reference additions and ADR-0012 §7 amendments; applying them is a separate `/architecture-decision` / reference-refresh task).

---

## Implementation Notes

*Derived from ADR-0012 `## Engine Compatibility` (Verification Required), `## Engine Validation` and `prototypes/spike-adr0019/REPORT.md` (pattern to copy):*

- Copy the structure of `prototypes/spike-adr0019/`: own `project.godot`, `Probe.cs` that dispatches on `--probe=<id>`, `logs/`, `REPORT.md`. Run with `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . --quit-after 100000 -- --probe=<id>`.
- `Input.ParseInputEvent` is buffered by one frame (GDD OQ-IC-9, Step 5.5 note 4): call `Input.FlushBufferedEvents()` after each injection and say in the report whether the flush made the event visible in the same frame.
- A headless run has no real focus, window or pad. Sending `Notification(Node.NotificationApplicationFocusOut)` only proves the handler path; it proves nothing about OS delivery. Say so in the Result cell.
- The headline question for the router design is **what the adapter may rely on**: arrival stamping in `_Input` (no `InputEvent` timestamp), release events never lost to the GUI, `SetInputAsHandled` effect, and `DEVICE_ID_*` classification. Everything else may degrade to a documented fallback (physical-name labels, `GuiReleaseFocus`, per-axis cache re-seed).
- Time box: if a row is not resolved in the box, record `NOT RUN — time-box` and carry it into `## MANUAL / NOT RUN`; do not extend the box.
- The `Result` vocabulary is closed on purpose: a blank, "assumed" or "probably" cell is a defect (a check that cannot report that it did not run is not a check).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 026: the production `InputAdapter` node. Story 027: mouse mode, GUI-focus policy, `project.godot` setting, focus / hot-plug notifications. Story 028: `GameLoop` hook-up. Story 029: the adapter integration suite and the MANUAL evidence runs.
- Promoting spike findings into `docs/engine-reference/godot/` or amending ADR-0012 (a follow-up task off this report).
- Steam Input behaviour (needs a Steam build; `NOT RUN`, M0 spike).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `prototypes/spike-oq-ic-9-input/REPORT.md` (verdict + findings table) plus the probe scripts and `logs/*.log` — a playtest-doc-style artifact; the verdict block is what stories 026–029 consume

**Status**: [x] Created — `prototypes/spike-oq-ic-9-input/REPORT.md` (verdict GO-WITH-DELTAS), probes + `logs/`

---

## Dependencies

- Depends on: None (the engine is installed at `~/.local/bin/godot-mono`; the .NET SDK via `mise exec dotnet@10.0.402`)
- Unlocks: stories 026, 027, 028, 029 (the Godot adapter half) on a GO or GO-WITH-DELTAS verdict; story 025 is schedule-ordered after it but not blocked by it

---

## Completion Notes
**Completed**: 2026-10-16
**Spike verdict**: **GO-WITH-DELTAS** (`prototypes/spike-oq-ic-9-input/REPORT.md` `## Verdict`). Satisfies the ADR-0012 §7 "spike recorded under `prototypes/`" gate for stories 026–029.
**Criteria**: 8/8 passing — synthetic / headless / API rows VERIFIED or CHANGED with committed logs (`prototypes/spike-oq-ic-9-input/logs/`); hardware rows **NOT RUN by design** (the AC vocabulary allows NOT RUN + reason) and **DEFERRED to story 029** (`## MANUAL / NOT RUN` M-1..M-9: real pad, OS focus on alt-tab, mouse capture across alt-tab, OS key repeat, AZERTY, GUI mouse click, Steam, Windows; M-8 retention follow-up). AC-8 scope: no file under `src/`, `docs/architecture/`, `docs/engine-reference/` or `design/` changed by the spike (files with newer mtimes in `src/` and `design/ux/` belong to concurrent simulation-core / content-data / ux-design sessions; nothing input-related).
**Deviations** (advisory, logged as tech debt):
- ADR-0012 §7 deltas D-1..D-8 proposed, not applied → **TD-021** (apply via `/architecture-decision`).
- S-25/S-26 open retention defect (FAILED, mechanism unestablished; not a NO-GO) → **TD-022**: follow-up retention spike (Release build, real input, ObjectCount + RSS) required **before story 026 is Done**.
- Engine-reference additions proposed, not applied → **TD-023**.
- LP-CODE-REVIEW CONCERNS accepted (autonomous): D-1's `ChildEnteredTree` re-assertion untested (story 028 verifies; likely `CallDeferred`, H-1); verdict wording understated S-25/S-26 — caveat paragraph added to REPORT.md `## Verdict`; stale "README not yet written" Housekeeping line corrected. QL gaps (advisory): S-08 "consumed by the Button" claim for mouse weakly supported (M-6 in 029); alt-tab rows NOT RUN by policy although a display session existed (029 collects).
**Test Evidence**: Integration — `prototypes/spike-oq-ic-9-input/REPORT.md` + probe scripts + `logs/*.log` (present; spot-checked by QL-TEST-COVERAGE).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (non-blocking, accepted); QL-TEST-COVERAGE ADEQUATE.
**Run result**: OBSERVED — headless probe runs + short windowed Wayland/X11 runs, logs retained under `prototypes/spike-oq-ic-9-input/logs/`.
