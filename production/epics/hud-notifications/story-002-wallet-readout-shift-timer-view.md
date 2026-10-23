# Story 002: `Main/Hud/Persistent` layer, `HudLayer` refresh, the wallet readout (`WalletView`) and the time-only shift timer (`ShiftTimerView`)

> **Epic**: HUD & Notifications (M-1a minimal slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S–M (~0.75 day; ~250 lines prod + smoke scenarios + evidence)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (created by the m1a-replan docs PR against ADR-0018, Accepted 2026-10-23 8:10 AM PT; Mail Lead planning defaults, approved by Evan 2026-10-23 8:10 AM PT (TD-173) are labelled inline)

> **UNBLOCKED 2026-10-23 8:10 AM PT: ADR-0018 is Accepted** (Evan, Mail Lead chat, 2026-10-23 8:10 AM PT: "approve all", replying to the widget whose first option was "accept ADR-0018 (minimal HUD)"). The same reply approved the Mail Lead planning defaults labelled inline (TD-173). `/story-readiness` re-run in the m1a-replan PR.

## Context

**GDD**: `design/gdd/hud-notifications.md` (H10 widget table: "Shift / phase timer … M-1a time only" l.176, "Team wallet … via the formatter … M-1a" l.179; H23 floors "timer digits ≥ 28 with tabular figures; wallet and quota ≥ 20" l.236 and 1080-reference px l.235; UI Requirements safe area 48 / 32 and top-bar zones l.531–532; Visual "What never moves: timer digits" l.505; Game Feel "Persistent widgets are quiet" l.522); `design/ux/hud-notifications-seed.md` (§1 PROPOSED layout: Top-left y 32 timer l.26; Top-right column 360 wide, wallet l.28; H29 dirty flags l.250); `design/spec/12-milestones.md` l.24
**Requirement**: `TR-hud-015` (1080-reference px scaled by height; floors; min 1280×720 — the floors half; stretch is ADR-0019 §7), `TR-hud-016` (0 alloc over idle frames; no `DrawLine` — the view half), `TR-hud-005` (text written on change only — the view half), `TR-hud-012` (one formatter call site — the view half)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: H-WIDGET-1 (wallet, time-only timer), H-WIDGET-2 (view half), H-PERF-3 (ADVISORY at M-1a), H-F3-1 (text rows).)*

**ADR Governing Implementation**: ADR-0018: HUD Composition — minimal M-1a slice (§4.1 one `CanvasLayer` `Main/Hud/Persistent`; §4.2 `HudLayer` refreshes `IHudState` first in tree order; §4.3 `WalletView`/`ShiftTimerView` layout and floors; §4.5 frame hygiene) — **Proposed 2026-10-23** (primary); ADR-0019: Godot presentation scene architecture (§2 `Hud (Node · HudPresenter)` l.104 and `Present` order World → Hud → Audio l.111; §7 `canvas_items`/`keep_height`, 1920×1080 reference, min 1280×720 l.192–205; Implementation Guidelines: views inert until `Initialize(deps)` l.257, 0 B steady state l.261, no `DrawLine` l.262) — Accepted; ADR-0012 (§7 HUD Controls `FocusMode = None` l.129) — Accepted
**ADR Decision Summary**: `Main/Hud` is `HudPresenter`; this story adds one `CanvasLayer` child `Main/Hud/Persistent` (script `HudLayer`), registered through `HudPresenter.AddLayer(layer, order)` with order 1 (the seed list's "persistent" position; ADR-0018 §4.1). `HudLayer` is first in tree order, implements `IInjectable<PresenterDeps>` and `IPresentedView` (godot-host-shell 006) and its `Present` calls `IHudState.Refresh()` so every view under it presents after the refresh in the same frame. `WalletView` and `ShiftTimerView` are `Control`s with `FocusMode = None`, inert until `Initialize(deps)`, each writing one `Label` only when its `HudDirty` flag is set (plus once at `Initialize`), the wallet through the one `IMoneyFormatter.Format(WalletCents, Balance)` call and the timer through `HudStringsEn.TimerText`. Nothing animates; no `DrawLine`; 0 B per frame in steady state.
**ADR Version**: ADR-0018 2026-10-23 (Accepted 8:10 AM PT); ADR-0019 2026-10-16 + erratum 2026-10-17; ADR-0012 2026-10-16 + amendment 2026-10-17

**Engine**: Godot 4.7.1 .NET (`~/.local/bin/godot`, 4.7.2 mono on the box — `production/session-state/HANDOFF.md`) | **Risk**: MEDIUM
**Engine Notes**: `CanvasLayer`, `Control`, `Label`, `Control.FocusMode` are pre-4.0 APIs. VERIFY-IN-ENGINE: (1) under ADR-0019 §7's stretch a `CanvasLayer` under `Main/Hud` composites above the 3D viewport at the 1080-reference size; (2) tabular figures via `FontVariation` (`tnum`, seed l.299) for the timer digits; (3) guard `Label.Text` writes in C# (compare first) — do not rely on the setter early-outing. No per-frame `GetChildren()` (story 006 engine note). `GodotObject.IsInstanceValid` guards stored node references (ADR-0004 risk).

**Control Manifest Rules (Presentation)**:
- Required: views are inert until `GameLoop` injects dependencies through `Initialize(deps)`; presenters run World → Hud → Audio (`docs/architecture/control-manifest.md` l.236); presentation time comes from `IRenderClock` (l.511)
- Forbidden: any view reading `SimWorld`/`ClientLayer` directly (l.514, l.522) — views read only `IHudState`; `DrawLine`/`DrawPolyline` in the HUD (l.526); `delta`/`Timer` for gameplay-facing timing (l.511)
- Guardrail: `Present` allocates 0 B in steady state (godot-host-shell 006 guardrail; H-PERF-3)

---

## Acceptance Criteria

*From ADR-0018 §4, ADR-0019 §2/§7, GDD H10/H23/H29 and the seed §1 layout, scoped to the layer and the two value widgets (the prompt is story 003):*

- [ ] **AC-1 Scene and injection (ADR-0019 l.104, l.257; story 006).** `Main.tscn` has `Main/Hud/Persistent` (`CanvasLayer`, script `HudLayer.cs`) as the first child of `Main/Hud`, with children `WalletView` and `ShiftTimerView` (`Control`, `FocusMode = None`). `HudLayer` is registered with `HudPresenter.AddLayer(layer, order: 1)` and all three implement `IInjectable<PresenterDeps>` + `IPresentedView`. A smoke scenario (`hud-persistent-injection`, the shape of story 006's `presenter-injection`) proves they are not initialised before boot step 7, are initialised exactly once after it, a second `Initialize` is refused, and `IHudState` reaches them through `PresenterDeps` (field `HudState`, added here **if absent** and recorded in Last Updated — ADR-0018 §4.2, planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)).
- [ ] **AC-2 Refresh before views (ADR-0018 §4.2).** In every presented frame `HudLayer.Present` calls `IHudState.Refresh()` exactly once and before `WalletView.Present` and `ShiftTimerView.Present` (recording fake `IHudState` in a smoke scenario `hud-refresh-order`); the presenter order World → Hud → Audio is unchanged (story 006's `present-order` scenario still passes). `HudPresenter.cs` is not edited.
- [ ] **AC-3 Wallet text (H-WIDGET-1 wallet row; H-WIDGET-2 view half; H10 l.179).** With a scripted `IHudState` stepping `WalletCents` 0 → 9 → −500 (mail-destinations story 005 AC-1 values and the floor, l.428), the `Label` text equals the fake `IMoneyFormatter`'s output for `(value, Balance)` after each change, the formatter is called exactly once per change and zero times on unchanged frames, and the text is written once at `Initialize` and then only when `Dirty.Wallet` is set. The story 001 lint (no "¢" / currency literal) now also covers `src/Game/Hud/**`.
- [ ] **AC-4 Timer text and visibility (H-WIDGET-1 time-only timer; H-F3-1 text rows; TR-hud-005).** The timer `Label` is hidden while `Timer.Visible == false` and shows `HudStringsEn.TimerText(DisplaySeconds)` ("02:14", "00:01", "00:00") otherwise; text is written only when `Dirty.Timer` is set; a scripted `IHudState` whose `DisplaySeconds` steps once per 1 000 ms over 10 s causes exactly 10 text writes (H-PERF-3 row). No prefix ("Shift 3 / 5 · DELIVERY") and no Amber / Red state at M-1a (l.176 "M-1a time only → M1").
- [ ] **AC-5 Layout and floors (H23 l.235–236; UI Requirements l.531–532; seed l.26, l.28 PROPOSED; TR-hud-015).** At 1920×1080 and text 100 %: the timer sits top-left at y 32 inside the 48 px side / 32 px top safe area with digits ≥ 28 ref px and tabular figures (`FontVariation` `tnum` — VERIFY-IN-ENGINE); the wallet sits in the top-right column inside the safe area with numerals ≥ 20 ref px; neither widget moves, scales or animates while running (l.505, l.522). Retained screenshots at 1920×1080 and at the 1280×720 minimum window (ADR-0019 §7) — **ADVISORY** until a render rig exists (GDD l.565).
- [ ] **AC-6 Frame hygiene (H29 seed l.250; ADR-0019 l.262).** A smoke scenario (`hud-idle`) runs 600 frames with an unchanged `IHudState` and records 0 `Label.Text` writes; a source check finds no `DrawLine`/`DrawPolyline` in `src/Game/Hud/**`. No `_Process` on these nodes; nothing reads `delta`.
- *Advisory, not an AC (lighter-process proposal, decision log 2026-10-23; H-PERF-3 is ADVISORY at M-1a):* the same scenario may log managed bytes per `Present` (the `src/Game/Smoke/FrameAllocSmoke.cs` shape); a non-zero number is a completion note, not a failure.
- [ ] **AC-7 Evidence (UI).** `production/qa/evidence/hud/wallet-timer-evidence.md` with the AC-5 screenshots (both resolutions), the `SMOKE … PASS` lines of AC-1/2/3/4/6 and a note of which `PresenterDeps` case AC-1 applied.

---

## Implementation Notes

*Derived from ADR-0018 §4.1–§4.3, §4.5; ADR-0019 §2, §7; godot-host-shell 006:*

- **Files:** `src/Game/Hud/HudLayer.cs`, `WalletView.cs`, `ShiftTimerView.cs` (+ `.uid` files, committed — ADR-0019 §8); `src/Game/Main.tscn` (nodes added; the scene file is saved by the editor, never piecemeal re-saved across a pin change); `src/Game/Presentation/PresenterDeps.cs` (+ `HudState` if absent); `src/Game/Smoke/HudPersistentSmoke.cs`; evidence doc above. `HudState` itself is composed at boot step 7 beside the other `PresenterDeps` contents (`PresentersBootStep`, story 006) from the published `IWalletRead`/`IRunRead`/`IInteractionQuery` and the `IRenderClock`.
- **Layer order:** `AddLayer(layer, 1)` maps to `CanvasLayer.Layer`; orders are the ADR-0019 l.104–105 seed-list positions (ADR-0018 §4.1, planning default). Throwing-catching 018's `AimLayer` keeps its own lower order.
- **Sizes:** all in 1080-reference px, which equal design px under `canvas_items`/`keep_height` (ADR-0019 l.199–200); `Window.ContentScaleFactor` is the one scale mechanism (l.205) — do not add a second.
- **Writes:** `if (_last != text) { _label.Text = text; _last = text; }` — cache the last string; never set `Text` unconditionally. Hidden timer: `Visible = false` on the `Label`, toggled only on a `Visible` change.
- **No styling pass here:** Chalk-on-Charcoal plates, fonts and the art-bible amendments are `/asset-spec` and `/ux-design` work (GDD l.497, l.508); the greybox uses the default theme with the floors enforced. Record any font that lacks `tnum` or the U+2212 minus in the evidence doc.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- The read model, F3 and the formatter (story 001). The prompt view (story 003). Accessibility (screen-reader gate, live regions): Deferred, manual-delivery story 012.
- Pills, the wallet floor outline flash and the pending-purchase greying (H10 l.179; M0+/M1 — ADR-0018 §6); timer Amber / Red, icon and outline (M1); the complaint envelope, quota, compass, hotbar frame, held chip (ADR-0018 §6).
- The `/ux-design` HUD spec, art-bible styling and `/asset-spec` (GDD l.508, l.536).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: smoke scenarios `hud-persistent-injection`, `hud-refresh-order`, `hud-wallet-timer-text`, `hud-idle` in `src/Game/Smoke/HudPersistentSmoke.cs` (`SMOKE … PASS`, run by the godot-host-shell 011 smoke wrapper) — must pass; retained screenshots at 1920×1080 and 1280×720 plus sign-off in `production/qa/evidence/hud/wallet-timer-evidence.md` (ADVISORY until a render rig exists, GDD l.565)
- Logic half: the `DrawLine` lint and the extended "¢" lint rows in `tests/unit/ui/HudUiRulesTests.cs` (story 001's file) — must pass on net10.0 Debug and net8.0 Release

- **Screenshot ruling:** the retained screenshots are **required** for `/story-done` (the AC-5 shots at 1920×1080; the 1280×720 shot is optional). "ADVISORY until a render rig exists" applies only to pixel-floor measurement.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0018 Accepted (`docs/architecture/adr-0018-hud-composition-minimal.md`); Story 001 (`IHudState`, `IMoneyFormatter`, `HudStringsEn.TimerText`); `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md` (`Main.tscn`, smoke harness); `production/epics/godot-host-shell/story-005-host-session-composition-ports-render-clock.md` (`RenderClock` sample per frame); `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (`PresenterDeps`, `IInjectable`, `IPresentedView`, `HudPresenter.AddLayer`/`Register`); `production/epics/godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md` (smoke wrapper, CI)
- Unlocks: Story 003 (shares `HudLayer` and `PresenterDeps.HudState`); the M0+ amendment's pill lane under the wallet
