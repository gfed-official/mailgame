# Story 029: Godot adapter integration suite and the MANUAL evidence runs (real pad, focus, AZERTY)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 8:10 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; was Blocked)
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~300 lines of smoke harness + a hands-on evidence session)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

> **DEFERRED 2026-10-23 by Evan, not deleted.** Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E1 (ADR-0012 l.281: H-EC-5/6/11 pad hot-plug, AZERTY and the MANUAL rows)**. Reason: real-pad, AZERTY and manual evidence runs; the adapter stories 026–028 stay. Re-run `/story-readiness` when M0 planning starts; the text below is unchanged from the last Ready/Blocked version.

> **BLOCKED: awaits implementation of stories 026–028.** The OQ-IC-9 spike gate is satisfied (story 001 Complete 2026-10-16, verdict GO-WITH-DELTAS; apply ADR-0012 deltas D-1..D-8, TD-021). Remaining blocker (this story's own unblock condition): stories 026, 027 and 028 implemented. Flip to Ready when they are. This story owns the MANUAL rows M-1..M-9 under the spike report's `## MANUAL / NOT RUN`, including M-8 / TD-022 (S-25 retention follow-up, which also gates story 026 Done), and the S-08 GUI mouse-click check.

## Context

**GDD**: `design/gdd/input-controls.md` (ACs H-R9-2, H-R11-3, H-EC-5, H-EC-6, H-EC-11 (MANUAL), H-EC-12, H-EC-13 prompt half (MANUAL), H-R6-2 (f), H-G4-1 integration, H-R12-1 boot; "Open flags from the qa-lead pass")
**Requirement**: `TR-input-007`, `TR-input-015`, `TR-input-016`, `TR-input-022` (the adapter rows, end to end); `TR-input-014` solo-pause clause
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (Validation Criteria — "H-R1-2 lint fails on the planted violation; H-R9-2, H-R11-3, H-EC-5/6/11/12 pass through the adapter after the spike") — Accepted 2026-10-16
**ADR Decision Summary**: The router core is proven headless (stories 004–024); the adapter half is proven in a headless Godot run with injected events, plus MANUAL evidence for what headless cannot show (a real pad, OS focus delivery, an AZERTY layout, Steam).
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot-mono`) | **Risk**: HIGH
**Engine Notes**: gdUnit4 is deferred (`tests/README.md`): the Godot-side suite is a headless smoke scene launched with `mise exec dotnet@10.0.402 -- ~/.local/bin/godot-mono --headless --path . -- --smoke=input-adapter` that injects events (`Input.ParseInputEvent` + `Input.FlushBufferedEvents()`), asserts, prints `PASS/FAIL` rows and exits non-zero on failure; its log is the retained evidence. Synthetic injection does not prove OS focus delivery or real pad behaviour — those rows are MANUAL.

**Control Manifest Rules (Presentation)**:
- Required: any Godot behaviour not in `docs/engine-reference/godot/` is verified by a spike before code depends on it; evidence for MANUAL rows is signed off in `production/qa/evidence/`
- Forbidden: marking a MANUAL row passed from a headless run; adding gdUnit4 here (its own story)
- Guardrail: the smoke scene runs in < 30 s on CI-less local machines

---

## Acceptance Criteria

*From the GDD ACs above. Each row is one `PASS/FAIL` line in the smoke log or one signed MANUAL row; a MANUAL row not run is recorded `NOT RUN — <what is needed>`.*

- [ ] **H-R9-2.** Key, mouse and joypad-0 events: the device is identified via `DeviceIdKeyboard` / `DeviceIdMouse`; a keyboard event never reads as pad 0 (assert on the converted `RawInputRecord`s).
- [ ] **H-R11-3.** (1) A release consumed by a UI node (a focused `Button` activated, then released) still clears the router's raw held state; (2) `ignore_joypad_on_unfocused_application` is `true`; (3) the mouse is captured in World and visible in Panel, Pause (TextEntry at M0); (4) with a Control focused, Space and Enter in World never activate it.
- [ ] **H-EC-5 (integration).** Focus loss mid-hold (E down, deliver hold) and mid-drag (LMB in the panel): releases synthesised, movement zeroed, every button muted, the drag entry stays on the cursor, the panel stays open, the recapture click is swallowed, solo auto-pauses (`TickDriver` paused, `SceneTree.Paused` false). The smoke scene sends `NotificationApplicationFocusOut`/`In` (and `NotificationWMWindowFocusOut`/`In`); the real alt-tab on this machine's compositor is the MANUAL row.
- [ ] **H-EC-6 (integration).** A pad unplugged mid-aim (Hold and Toggle) → cancel, never a throw; solo auto-pauses if that pad was last-used; MP "Controller disconnected" (banner state) without pausing; reconnect with A held → no edge until A is released. Headless uses the spike's recorded method for driving `JoyConnectionChanged`; a **real pad** hot-plug run is MANUAL.
- [ ] **H-EC-12.** A synthetic `InputEventAction` (device 0) while the last-used device is KBM leaves the glyph set KBM.
- [ ] **H-R6-2 (f).** Y in Panel keeps the panel open, B closes it, Tab toggles it; Tab never moves Control focus; no focused Control activates on a game input.
- [ ] **H-G4-1 (integration clause) and mouse accumulation.** E held with injected echo events at 30 / s for 1 s → one press edge; 16 mouse-motion events per frame with accumulation off give the same summed look as the accumulated case.
- [ ] **H-R12-1 (boot).** Re-run of story 028's file variants inside the smoke harness (holdSettingMs 600 + Deliver Toggle applied; malformed → defaults + one warning).
- [ ] **Adapter allocation.** Bytes per event measured for key / motion / joypad-motion in the production adapter, recorded next to the spike's numbers (OQ-IC-17 input; **not** a gate at M-1a).
- [ ] **MANUAL rows (signed in the evidence file).** H-EC-11 AZERTY — the physical W-position key moves forward and the prompt label shows "Z"; number-row prompts read 1–8 (label API UNVERIFIED — physical-name fallback is acceptable and recorded); real-pad hot-plug and `ignore_joypad_on_unfocused_application` with a pad; real alt-tab on X11/XWayland and native Wayland (whichever the machine offers); H-EC-13 prompt half and Steam Input double-pad behaviour are `NOT RUN — needs a Steam build (M0 spike)` unless a Steam session exists.
- [ ] **Evidence file.** `production/qa/evidence/input-adapter-evidence.md` contains: environment block; the smoke log (or path to it); one row per AC above with Result (PASS / FAIL / NOT RUN + reason); MANUAL sign-off (name, date); and a list of any spike deltas that proved wrong in the field (feeds an ADR-0012 §7 amendment).

---

## Implementation Notes

*Derived from ADR-0012 Validation Criteria and the spike report's MANUAL list:*

- The smoke scene lives under `src/Game/Input/Smoke/` (debug-only, excluded from release exports by the host shell's export filter — **`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (debug-only `--smoke=<name>` gating) and `production/epics/godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md` (smoke discovery and CI); the release-export filter itself belongs to the export story, outside the godot-host-shell M-1a scope**). Keep it self-contained: it constructs the real `InputAdapter` and router over a scripted `ICarrierStatusSource` and records `IImmediateActionSink` calls.
- Use the spike's harness as the model (`prototypes/spike-oq-ic-9-input/`), but write the production smoke fresh (prototype code is never promoted).
- Panel-open rows need a Panel state without the real panel view: drive the router's overlay through the transition table (story 008) with a stub `ICarrierStatusSource`; a real Grid Inventory panel is not required.
- Record `Godot.Input` behaviour that differs from the spike in the evidence file's last section instead of fixing the spike report.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Stories 026–028 (the code under test). The router's own logic (headless suites 004–024). M0: H-FX-2, H-FX-3b (MANUAL playtests), chat / TextEntry rows. M1: H-PERF-1/2 adapter timing, H-A11Y-2/3/4/6.
- gdUnit4 installation (its own story).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `production/qa/evidence/input-adapter-evidence.md` (smoke log + MANUAL sign-off), and the retained smoke-run log — a playtest-doc-style artifact checked by `/story-done`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 001 (spike verdict and its MANUAL list)**, Stories 026, 027, 028; `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (smoke harness), `production/epics/godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md` (headless smoke / CI)
- Unlocks: Closes the epic's Godot-adapter acceptance; unblocks the M1 adapter perf gate (H-PERF-1/2) and the Controls settings screen work
