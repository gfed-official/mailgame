# Story 027: Adapter — mouse mode, GUI-focus policy, focus-loss delivery and the joypad project setting

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete (greybox; focus-loss handlers and pause requests not built, input-controls 016/018 harden later → TD-240)
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines prod + smoke rows)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; focus-loss handlers and pause requests not built, input-controls 016/018 harden later → TD-240); delivered by PR #90, merge 7b4b6d2; see Completion Notes). Earlier: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23 (E1, ADR-0012).** The hot-plug delivery AC (`Input.JoyConnectionChanged` → `router.OnPadConnection`, H-EC-6 integration half) moved to **story 031 (Deferred to M0)**, alongside story 018's router half. The joypad project setting `ignore_joypad_on_unfocused_application` is a focus-loss setting (TR-input-016, asserted by H-R11-3), not a hot-plug one, so it stays here. Story 029 (the real-pad / real-display evidence session) stays Ready; this story records its own headless smoke rows in the evidence file. Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all".

> **Unblocked 2026-10-16 (story 001 Complete, OQ-IC-9 spike verdict GO-WITH-DELTAS — satisfies the ADR-0012 §7 "spike recorded under `prototypes/`" gate):** apply ADR-0012 deltas D-1..D-8 (TD-021) — 026 not Done until the S-25 retention follow-up (TD-022). Read `prototypes/spike-oq-ic-9-input/REPORT.md` `## ADR-0012 §7 deltas` and the `## Consumed by` row for this story before implementing; `/story-readiness` still recommended. Depends on story 026 and `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` / `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (dependencies, not blocks).

## Context

**GDD**: `design/gdd/input-controls.md` (R11 "`ui_*` actions", "Focus loss", "Mouse mode"; Edge Cases; ACs H-R11-3 clauses 2–4, H-EC-5 integration half, H-R6-2 (f))
**Requirement**: `TR-input-007` (`ui_*` InputMap never mutated; game Controls `FocusMode = None`), `TR-input-016` (`ignore_joypad_on_unfocused_application = true`), `TR-input-022` (focus loss — delivery to the router). `TR-input-015` hot-plug delivery is story 031 (M0).
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "`ui_*` and GUI focus", "Project settings", "Mouse mode", "Focus loss") — Accepted 2026-10-16 (adapter rows provisional); §7 "Hot-plug" is story 031 (E1, M0); ADR-0002 (§4 solo pause is `TickDriver.Pause`, never `SceneTree.Paused`) secondary
**ADR Decision Summary**: The `ui_*` InputMap is never mutated; HUD and in-world Controls use `FocusMode = None`; entering a game context calls `GuiReleaseFocus()`; menus outside play keep stock `ui_*` navigation. `input_devices/joypads/ignore_joypad_on_unfocused_application = true`. `Input.MouseMode = Captured` in body states with look, `Visible` in Panel, Map, Station, Draft, History, Pause and TextEntry, switched by the adapter on router-state change. Focus loss (`NotificationApplicationFocusOut`, UNVERIFIED) → `router.OnFocusLost()`.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0002)

**Engine**: Godot 4.7.1 .NET (`src/Game/Input/`) | **Risk**: HIGH
**Engine Notes**: Handle **both** `NotificationApplicationFocusOut` and `NotificationWMWindowFocusOut` (and the matching In notifications): either triggers `OnFocusLost` / `OnFocusGained`. Capture is commonly lost on alt-tab, so mouse capture is re-applied after focus-in (spike item 7). Wayland capture is UNVERIFIED (spike item 7). On a headless run `Notification()` proves only the handler path.

**Control Manifest Rules (Presentation)**:
- Required: HUD and in-world Controls use `FocusMode = None`; entering a game context calls `GuiReleaseFocus()`; `ignore_joypad_on_unfocused_application = true`; focus loss pauses through `TickDriver.Pause()` in solo (manifest Presentation, ADR-0012)
- Forbidden: `SceneTree.Paused`; mutating `ui_*` or any `InputMap` action; ending a pause by timer
- Guardrail: mouse-mode switch only on a state change (no per-frame writes)

---

## Acceptance Criteria

*From GDD R11 and ADR-0012 §7, scoped to this story. Verified by the headless smoke scene, recorded in `production/qa/evidence/input-adapter-evidence.md`; rows that need a real display stay MANUAL and are collected by story 029. Each row follows the spike report where it differs from the ADR text.*

- [ ] **Mouse mode.** The adapter applies `router.MouseModeRequest` (story 008) on a router-state change only: `Input.MouseMode = Captured` in World / Holding-closed / Aiming, `Visible` in Panel, Holding-open and Pause (and in TextEntry / Map / Station / Draft / History when those exist); cursor position is restored/centred as the spike recorded; capture is **re-applied after focus-in** when the state still requests it.
- [x] **GUI-focus policy.** A `GameControl` convention (static helper `GameControlFocus.Apply(Control)` setting `FocusMode.None`, used by the HUD and in-world Controls) exists; on entering a game context the adapter calls `GetViewport().GuiReleaseFocus()`; a debug-build `FocusPolicyCheck` walks `Main` once after boot and reports any visible `Control` under `Hud` / `World` with a focus mode other than `None`. The `ui_*` InputMap is untouched (a boot-time assertion compares `InputMap.ActionGetEvents("ui_accept")` etc. against the engine defaults captured at startup; no `InputMap` write exists anywhere — the story 025 CI lint is Deferred to M0, so the boot-time assertion is the only check at M-1a).
- [x] **H-R11-3 (clauses 2–4).** With the smoke scene: `ignore_joypad_on_unfocused_application` reads `true` at runtime; the mouse is captured in World and visible in Panel, Pause (and TextEntry at M0); with a `Button` focused (set before entering World) Space and Enter in World never activate it.
- [x] **Project setting.** The repo-root `project.godot` carries `[input_devices] joypads/ignore_joypad_on_unfocused_application=true` (set by this story; verify the exact key against the spike's B3 row, not memory).
- [ ] **Focus loss delivery.** `NotificationApplicationFocusOut` and `NotificationWMWindowFocusOut` call `router.OnFocusLost()`; the In notifications call `router.OnFocusGained()`, re-apply mouse capture and re-seed the axis cache (story 026). A duplicate Out (both notifications) is idempotent. **H-EC-5 (integration half)**, run headless here via `Notification()` and recorded in the evidence file (the real-display rerun is story 029, M0): focus loss mid-hold and mid-drag → releases synthesised, movement zeroed, every button muted, the drag entry stays on the cursor, the panel stays open, the recapture click is swallowed, solo auto-pauses.
- [ ] **Pause requests.** The adapter does not pause the Sim; it exposes the router's `PauseRequested` / `ResumeRequested` to `GameLoop` (story 028). `tools/ci/check-no-scenetree-paused.sh` stays green.

---

## Implementation Notes

*Derived from ADR-0012 §7 and Engine Validation note 4 (apply spike deltas):*

- Files in `src/Game/Input/`: `MouseModeController.cs`, `GameControlFocus.cs`, `FocusPolicyCheck.cs`, additions to `InputAdapter.cs` (`_Notification`). The `FocusPolicyCheck` is debug-only; gate it at runtime with `OS.IsDebugBuild()` rather than preprocessor symbols (the manifest bans `DEBUG` / `EXPORTDEBUG` branching in Sim and Client.Core; keep the same habit here).
- Menus outside play (boot, content-error, pause menu items) keep stock `ui_*` navigation (`FocusMode` all) — the policy is "game contexts", so the screen subtree under `Main/Screens` is exempt; document the boundary.
- The recapture-click swallow is the router's (story 018); the adapter only reports focus-in and the mouse-mode re-apply.
- `NotificationApplicationFocusOut` on Wayland/X11/Windows differences and capture behaviour are the spike's `## MANUAL / NOT RUN` rows: this story implements the handler for all and story 029 collects the real-display evidence; until then the rows stay marked MANUAL / NOT RUN in the evidence file.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 031 (Deferred, M0): the `JoyConnectionChanged` signal hookup and H-EC-6. Story 026: event gathering. Story 028: `TickDriver.Pause` calls and construction. Story 029 (Ready): the real-pad / real-display evidence session. Router logic (stories 016, 018). M1: Steam virtual-pad handling, glyph-family detection, remap UI.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: smoke-scene log rows for these ACs in `production/qa/evidence/input-adapter-evidence.md` (this story creates the file if story 026 has not; story 029 owns the MANUAL sign-offs) — must exist; rows that need a real display are recorded as MANUAL / NOT RUN, never marked passed from a headless run

**Status**: [x] Created and passing — smoke `input-mouse-focus` (16 asserts, 0 failed); rows in `production/qa/evidence/input-adapter-evidence.md` (focus-loss rows NOT RUN), PR #90 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #90 (merge `7b4b6d2`), package WP-B part 2 (M-1a wave 2), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310).
- **Built:** `MouseModeController` (applies `MouseModeRequest` on the router's `StateChanged` event only, TD-240 (62)), `GameControlFocus`, the debug-only `FocusPolicyCheck` (TD-240 (67)), the `ui_*` InputMap snapshot check (`GD.PushError` on a mismatch, TD-240 (66)), and `project.godot` `[input_devices] joypads/ignore_joypad_on_unfocused_application=true`.
- **ACs met:** AC-2 (GUI-focus policy: `GuiReleaseFocus()` on entering a game context, TD-240 (63)), AC-3 (H-R11-3 clauses 2–4 in the `input-mouse-focus` smoke), AC-4 (project setting).
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 "no focus loss"; held by the package TD row):** AC-1 — mouse mode switches on state change and the cursor is warped to the viewport centre when capture ends (TD-240 (64), (65)); the re-apply after focus-in is not built because no focus handler exists. AC-5 — focus-loss delivery (`OnFocusLost` / `OnFocusGained`, H-EC-5) not built: stories 016 and 018 are on the harden-later list, so the router has no focus API yet. AC-6 — `PauseRequested` / `ResumeRequested` not built; the solo pause in the playable build is WP-F's `BackLadder` → `GameLoop.TogglePause` (PR #94, TD-270 (7)).
- **Relaxed dependencies (regroup plan §2):** 027 → 016 / 018 (neither built).
- **Harden later:** focus loss (AC-1 re-apply, AC-5) and the router pause requests (AC-6) with input-controls 016 / 018.
- **Tests / smokes:** smoke `input-mouse-focus` (16/0); four legs green, no test deleted.

---

## Dependencies

- Depends on: **Story 001 (spike verdict)**, Story 026, Story 008 (`MouseModeRequest`), Story 018 (router focus API), Story 016 (pause requests); `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Main.tscn` / scene root), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`Hud` presenter)
- Unlocks: Stories 028, 029; Deferred: 031 (M0)
