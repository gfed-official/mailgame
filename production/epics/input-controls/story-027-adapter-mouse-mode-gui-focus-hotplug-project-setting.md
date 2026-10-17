# Story 027: Adapter — mouse mode, GUI-focus policy, focus-loss and hot-plug delivery, and the joypad project setting

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines prod + smoke rows)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

> **Unblocked 2026-10-16 (story 001 Complete, OQ-IC-9 spike verdict GO-WITH-DELTAS — satisfies the ADR-0012 §7 "spike recorded under `prototypes/`" gate):** apply ADR-0012 deltas D-1..D-8 (TD-021) — 026 not Done until the S-25 retention follow-up (TD-022). Read `prototypes/spike-oq-ic-9-input/REPORT.md` `## ADR-0012 §7 deltas` and the `## Consumed by` row for this story before implementing; `/story-readiness` still recommended. Depends on story 026 and `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` / `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (dependencies, not blocks).

## Context

**GDD**: `design/gdd/input-controls.md` (R9 Hot-plug, R11 "`ui_*` actions", "Focus loss", "Mouse mode"; Edge Cases; ACs H-R11-3 clauses 2–4, H-EC-5, H-EC-6 integration halves, H-R6-2 (f))
**Requirement**: `TR-input-007` (`ui_*` InputMap never mutated; game Controls `FocusMode = None`), `TR-input-016` (`ignore_joypad_on_unfocused_application = true`), `TR-input-022` (focus loss — delivery to the router), `TR-input-015` (hot-plug via `joy_connection_changed`, UNVERIFIED)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "`ui_*` and GUI focus", "Project settings", "Mouse mode", "Focus loss", "Hot-plug") — Accepted 2026-10-16 (adapter rows provisional); ADR-0002 (§4 solo pause is `TickDriver.Pause`, never `SceneTree.Paused`) secondary
**ADR Decision Summary**: The `ui_*` InputMap is never mutated; HUD and in-world Controls use `FocusMode = None`; entering a game context calls `GuiReleaseFocus()`; menus outside play keep stock `ui_*` navigation. `input_devices/joypads/ignore_joypad_on_unfocused_application = true`. `Input.MouseMode = Captured` in body states with look, `Visible` in Panel, Map, Station, Draft, History, Pause and TextEntry, switched by the adapter on router-state change. Focus loss (`NotificationApplicationFocusOut`, UNVERIFIED) → `router.OnFocusLost()`; hot-plug (`Input.JoyConnectionChanged`, UNVERIFIED) → `router.OnPadConnection`.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0002)

**Engine**: Godot 4.7.1 .NET (`src/Game/Input/`) | **Risk**: HIGH
**Engine Notes**: Handle **both** `NotificationApplicationFocusOut` and `NotificationWMWindowFocusOut` (and the matching In notifications): either triggers `OnFocusLost` / `OnFocusGained`. Capture is commonly lost on alt-tab, so mouse capture is re-applied after focus-in (spike item 7). Hot-plug index reuse and Wayland capture are UNVERIFIED (spike items 4, 7). On a headless run `Notification()` proves only the handler path.

**Control Manifest Rules (Presentation)**:
- Required: HUD and in-world Controls use `FocusMode = None`; entering a game context calls `GuiReleaseFocus()`; `ignore_joypad_on_unfocused_application = true`; focus loss pauses through `TickDriver.Pause()` in solo (manifest Presentation, ADR-0012)
- Forbidden: `SceneTree.Paused`; mutating `ui_*` or any `InputMap` action; ending a pause by timer
- Guardrail: mouse-mode switch only on a state change (no per-frame writes)

---

## Acceptance Criteria

*From GDD R9 / R11 and ADR-0012 §7, scoped to this story. Verified by the headless smoke scene + MANUAL rows in story 029's evidence; each row follows the spike report where it differs from the ADR text.*

- [ ] **Mouse mode.** The adapter applies `router.MouseModeRequest` (story 008) on a router-state change only: `Input.MouseMode = Captured` in World / Holding-closed / Aiming, `Visible` in Panel, Holding-open and Pause (and in TextEntry / Map / Station / Draft / History when those exist); cursor position is restored/centred as the spike recorded; capture is **re-applied after focus-in** when the state still requests it.
- [ ] **GUI-focus policy.** A `GameControl` convention (static helper `GameControlFocus.Apply(Control)` setting `FocusMode.None`, used by the HUD and in-world Controls) exists; on entering a game context the adapter calls `GetViewport().GuiReleaseFocus()`; a debug-build `FocusPolicyCheck` walks `Main` once after boot and reports any visible `Control` under `Hud` / `World` with a focus mode other than `None`. The `ui_*` InputMap is untouched (a boot-time assertion compares `InputMap.ActionGetEvents("ui_accept")` etc. against the engine defaults captured at startup; no `InputMap` write exists anywhere — story 025 lint).
- [ ] **H-R11-3 (clauses 2–4).** With the smoke scene: `ignore_joypad_on_unfocused_application` reads `true` at runtime; the mouse is captured in World and visible in Panel, Pause (and TextEntry at M0); with a `Button` focused (set before entering World) Space and Enter in World never activate it.
- [ ] **Project setting.** The repo-root `project.godot` carries `[input_devices] joypads/ignore_joypad_on_unfocused_application=true` (set by this story; verify the exact key against the spike's B3 row, not memory).
- [ ] **Focus loss delivery.** `NotificationApplicationFocusOut` and `NotificationWMWindowFocusOut` call `router.OnFocusLost()`; the In notifications call `router.OnFocusGained()`, re-apply mouse capture and re-seed the axis cache (story 026). A duplicate Out (both notifications) is idempotent. **H-EC-5 (integration half)** is recorded in story 029: focus loss mid-hold and mid-drag → releases synthesised, movement zeroed, every button muted, the drag entry stays on the cursor, the panel stays open, the recapture click is swallowed, solo auto-pauses.
- [ ] **Hot-plug delivery.** `Input.JoyConnectionChanged(device, connected)` calls `router.OnPadConnection((byte)device, connected)` (signal subscribed in `Initialize`, unsubscribed in `_ExitTree`); **H-EC-6 (integration half)** in story 029: a pad unplugged mid-aim cancels (never throws); solo auto-pauses if it was the last-used device; MP shows "Controller disconnected" and nothing pauses; reconnect with A held produces no edge until released.
- [ ] **Pause requests.** The adapter does not pause the Sim; it exposes the router's `PauseRequested` / `ResumeRequested` to `GameLoop` (story 028). `tools/ci/check-no-scenetree-paused.sh` stays green.

---

## Implementation Notes

*Derived from ADR-0012 §7 and Engine Validation note 4 (apply spike deltas):*

- Files in `src/Game/Input/`: `MouseModeController.cs`, `GameControlFocus.cs`, `FocusPolicyCheck.cs`, additions to `InputAdapter.cs` (`_Notification`, signal hookup). The `FocusPolicyCheck` is debug-only; gate it at runtime with `OS.IsDebugBuild()` rather than preprocessor symbols (the manifest bans `DEBUG` / `EXPORTDEBUG` branching in Sim and Client.Core; keep the same habit here).
- Menus outside play (boot, content-error, pause menu items) keep stock `ui_*` navigation (`FocusMode` all) — the policy is "game contexts", so the screen subtree under `Main/Screens` is exempt; document the boundary.
- The recapture-click swallow is the router's (story 018); the adapter only reports focus-in and the mouse-mode re-apply.
- `NotificationApplicationFocusOut` on Wayland/X11/Windows differences, capture behaviour and index reuse are the spike's `## MANUAL / NOT RUN` rows: this story implements the handler for all and story 029 collects the real-display evidence.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 026: event gathering. Story 028: `TickDriver.Pause` calls and construction. Story 029: evidence. Router logic (stories 016, 018). M1: Steam virtual-pad handling, glyph-family detection, remap UI.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: smoke-scene log rows and MANUAL sign-offs for these ACs in `production/qa/evidence/input-adapter-evidence.md` (story 029) — must exist; MANUAL rows that need a real pad / display are recorded as such, never marked passed from a headless run

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 001 (spike verdict)**, Story 026, Story 008 (`MouseModeRequest`), Story 018 (router focus / hot-plug API), Story 016 (pause requests); `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Main.tscn` / scene root), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`Hud` presenter)
- Unlocks: Stories 028, 029
