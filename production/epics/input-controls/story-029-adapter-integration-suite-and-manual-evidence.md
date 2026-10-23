# Story 029: Godot adapter integration suite and the MANUAL evidence runs (real pad, focus, AZERTY)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~300 lines of smoke harness + a hands-on evidence session)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23. Status Blocked → Ready. The only remaining block was this story's own dependency on 026–028. Those are Ready stories, and `/story-readiness` treats a dependency as blocking only when the story is Draft or missing; they stay listed under Depends on. The Engine Notes were corrected to the box toolchain (HANDOFF 2026-10-20). Manifest re-stamped 2026-10-17.1, because the 2026-10-17.1 revision is ADR-0012 §7's D-1…D-8 adapter rules, which this story already applies (see the note below).

> **Was BLOCKED (to 2026-10-23): awaited stories 026–028.** Do not start `/dev-story` until 026, 027 and 028 are Complete; this is now a Depends-on gate, not a status. The OQ-IC-9 spike gate is satisfied (story 001 Complete 2026-10-16, verdict GO-WITH-DELTAS; apply ADR-0012 deltas D-1..D-8, TD-021). Start condition: stories 026, 027 and 028 are Complete. This story owns the MANUAL rows M-1..M-9 under the spike report's `## MANUAL / NOT RUN`, including M-8 / TD-022 (S-25 retention follow-up, which also gates story 026 Done), and the S-08 GUI mouse-click check.


> **Narrowed 2026-10-23 (m1a-replan) under erratum E1, Evan 2026-10-23 8:10 AM PT, Mail Lead chat: "approve all".** The pad hot-plug integration AC (H-EC-6) moved to story 031 and the glyph AC (H-EC-12) moved to story 030, both Deferred with the hot-plug and glyph halves of stories 018 and 017. The AZERTY and real-pad MANUAL rows stay listed here and may close NOT RUN with a dated follow-up (TD-172 D3, Approved by Evan 2026-10-23 7:56 AM PT; its details approved 8:20 AM PT); E1 moves passing them to M0/M1. Status stays Ready.

## Context

**GDD**: `design/gdd/input-controls.md` (ACs H-R9-2, H-R11-3, H-EC-5, H-EC-6, H-EC-11 (MANUAL), H-EC-12, H-EC-13 prompt half (MANUAL), H-R6-2 (f), H-G4-1 integration, H-R12-1 boot; "Open flags from the qa-lead pass")
**Requirement**: `TR-input-007`, `TR-input-015`, `TR-input-016`, `TR-input-022` (the adapter rows, end to end); `TR-input-014` solo-pause clause
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (Validation Criteria — "H-R1-2 lint fails on the planted violation; H-R9-2, H-R11-3, H-EC-5/6/11/12 pass through the adapter after the spike") — Accepted 2026-10-16
**ADR Decision Summary**: The router core is proven headless (stories 004–024); the adapter half is proven in a headless Godot run with injected events, plus MANUAL evidence for what headless cannot show (a real pad, OS focus delivery, an AZERTY layout, Steam).
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (pinned; the box has Godot 4.7.2 mono at `~/.local/bin/godot`, and there is no `godot-mono` — see HANDOFF.md; never commit the `.csproj` SDK rewrite the 4.7.2 editor makes) | **Risk**: HIGH
**Engine Notes**: gdUnit4 is deferred (`tests/README.md`): the Godot-side suite is a headless smoke scene launched with `~/.local/bin/godot --headless --path . -- --smoke=input-adapter` (with `~/.dotnet` on `PATH`; `mise` is not installed) that injects events (`Input.ParseInputEvent` + `Input.FlushBufferedEvents()`), asserts, prints `PASS/FAIL` rows and exits non-zero on failure; its log is the retained evidence. Synthetic injection does not prove OS focus delivery or real pad behaviour — those rows are MANUAL.

**Control Manifest Rules (Presentation)**:
- Required: any Godot behaviour not in `docs/engine-reference/godot/` is verified by a spike before code depends on it; evidence for MANUAL rows is signed off in `production/qa/evidence/` (this story's close-with-NOT-RUN exception, TD-172 D3, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat), is in the Evidence file AC)
- Forbidden: marking a MANUAL row passed from a headless run; adding gdUnit4 here (its own story)
- Guardrail: the smoke scene runs in < 30 s on CI-less local machines

---

## Acceptance Criteria

*From the GDD ACs above. Each row is one `PASS/FAIL` line in the smoke log or one signed MANUAL row; a MANUAL row not run is recorded `NOT RUN — <what is needed>`.*

- [ ] **H-R9-2.** Key, mouse and joypad-0 events: the device is identified via `DeviceIdKeyboard` / `DeviceIdMouse`; a keyboard event never reads as pad 0 (assert on the converted `RawInputRecord`s).
- [ ] **H-R11-3.** (1) A release consumed by a UI node (a focused `Button` activated, then released) still clears the router's raw held state; (2) `ignore_joypad_on_unfocused_application` is `true`; (3) the router's `MouseModeRequest` (story 008) reads `Captured` in World and `Visible` in Panel and Pause (TextEntry at M0) — assert on that property, which headless can observe. Headless ignores `Input.MouseMode` (spike `REPORT.md` l.78, S-31: `MouseMode=Captured` ignored, reads `Visible`), and no story provides a record of the adapter applying the request, so the apply and the re-apply on focus-in and first click (D-2, manifest l.505) plus the readback are MANUAL M-3 until story 027 (the adapter's `MouseModeController`, the code under test) adds an observable apply record; its shape is 027's call (Approved by Evan 2026-10-23 8:20 AM PT (TD-172 D6)); (4) with a Control focused, Space and Enter in World never activate it.
- [ ] **H-EC-5 (integration).** Focus loss mid-hold (E down, deliver hold) and mid-drag (LMB in the panel): releases synthesised, movement zeroed, every button muted, the drag entry stays on the cursor, the panel stays open, the recapture click is swallowed, solo auto-pauses (`TickDriver` paused, `SceneTree.Paused` false). The smoke scene sends `NotificationApplicationFocusOut`/`In` and emits the root Window's `FocusExited`/`FocusEntered` (D-4, manifest l.506); either path must trigger the idempotent `OnFocusLost()`; the real alt-tab on this machine's compositor is the MANUAL row.
- [ ] **H-R6-2 (f).** Y in Panel keeps the panel open, B closes it, Tab toggles it; Tab never moves Control focus; no focused Control activates on a game input.
- [ ] **H-G4-1 (integration clause) and mouse accumulation.** E held with injected echo events at 30 / s for 1 s → one press edge; 16 mouse-motion events per frame with accumulation off give the same summed look as the accumulated case.
- [ ] **H-R12-1 (boot).** Re-run of story 028's file variants inside the smoke harness, one smoke scenario (one `--smoke=` run) per file variant, because `SmokeRunner` boots once per process (holdSettingMs 600 + Deliver Toggle applied; malformed → defaults + one warning).
- [ ] **Adapter allocation.** Bytes per event measured for key / motion / joypad-motion in the production adapter, recorded next to the spike's numbers (OQ-IC-17 input; **not** a gate at M-1a).
- [ ] **MANUAL rows (one evidence row per spike id M-1…M-9, `prototypes/spike-oq-ic-9-input/REPORT.md` l.149–157).** M-1 real pad (hot-plug, `ignore_joypad_on_unfocused_application`). M-2 real alt-tab on X11/XWayland and native Wayland (whichever the machine offers). M-3 mouse capture across alt-tab. M-4 OS key repeat `Echo`. M-5 AZERTY (H-EC-11: the W-position key moves forward and the prompt shows "Z"; number row 1–8; the label API is UNVERIFIED, so a physical-name fallback is acceptable and recorded). M-6 mouse click on a GUI Control (S-08). M-7 Steam Input and the H-EC-13 prompt half: `NOT RUN — needs a Steam build (M0 spike)` unless a Steam session exists. M-8 records the TD-022 outcome already taken at 026 Done. M-9 Windows: `NOT RUN — no Windows machine`. Each row is PASS, FAIL or `NOT RUN — <what is needed>`.
- [ ] **Evidence file.** `production/qa/evidence/input-adapter-evidence.md` contains: environment block; the smoke log (or path to it); one row per AC above with Result (PASS / FAIL / NOT RUN + reason); a signature (name, date) on each MANUAL row that was run, and `NOT RUN — <what is needed>` otherwise. **Exception (TD-172 D3):** story 029 may close with the MANUAL (hands-on) rows NOT RUN, and Evan runs the S-25 retention check (M-8, TD-022) later — Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat): "Approve all three: HostPanelView per player slot, CursorSlot as the source hotbar slot or −1, and 029 closing with the hands-on checks not run (I'll do the retention check later)". Two details were Approved by Evan 2026-10-23 8:20 AM PT (Mail Lead chat, "Approve D4–D6, the D3 details and the ADR-0009/ADR-0016 errata"): a dated follow-up (owner, date) is recorded in the file for each row not run, and the retention check is tied to story 026 closing. This is an exception, not a change to the rule or to ADR-0012: ADR-0012 Validation Criteria l.281 lists H-EC-11 among the rows that pass through the adapter, and H-EC-11 is MANUAL-only (M-5), so that criterion stays open until the MANUAL rows are run and signed. The file also holds a list of any spike deltas that proved wrong in the field (feeds an ADR-0012 §7 amendment).

---

## Implementation Notes

*Derived from ADR-0012 Validation Criteria and the spike report's MANUAL list:*

- The smoke scene lives under `src/Game/Smoke/` as `InputAdapterSmoke.cs` (the repo convention, godot-host-shell 011; discovery is attribute-based) (debug-only, excluded from release exports by the host shell's export filter — **`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (debug-only `--smoke=<name>` gating) and `production/epics/godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md` (smoke discovery and CI); the release-export filter itself belongs to the export story, outside the godot-host-shell M-1a scope**). Keep it self-contained: it constructs the real `InputAdapter` and router over a scripted `ICarrierStatusSource` and records `IImmediateActionSink` calls.
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

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* QL-STORY-READY: `/story-readiness` run 2026-10-23 on the Mail Review PR #46 r1 text (model claude-fable-5-1, log `/workspace/repos/logs/pr46-readiness-ic029.log`): READY, 21/21 checks; qa-lead GAPS, advisory only (H-R12-1's three 028 variants, one log per scenario, the allocation method), not folded in. **MANUAL rows need a person at a real machine** (a pad, an AZERTY layout, a real alt-tab on X11/Wayland). Until a person runs them, the agent records them as `NOT RUN — <what is needed>`; the story closes with the MANUAL sign-off l.27 and l.81 require, or under the TD-172 D3 exception (Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat); the dated follow-up and the tie of M-8 to 026 closing Approved by Evan 2026-10-23 8:20 AM PT). ADR-0012 Validation Criteria l.281 (H-EC-11, MANUAL-only, M-5) stays open until the rows are signed.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `production/qa/evidence/input-adapter-evidence.md` (smoke log + MANUAL sign-off, or the TD-172 D3 exception, Approved by Evan 2026-10-23 7:56 AM PT (Mail Lead chat), with the dated follow-up, Approved by Evan 2026-10-23 8:20 AM PT), and the retained smoke-run log — a playtest-doc-style artifact checked by `/story-done`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 001 (spike verdict and its MANUAL list)**, Stories 026, 027, 028; `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (smoke harness), `production/epics/godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md` (headless smoke / CI)
- Unlocks: Closes the epic's Godot-adapter acceptance; unblocks the M1 adapter perf gate (H-PERF-1/2) and the Controls settings screen work
