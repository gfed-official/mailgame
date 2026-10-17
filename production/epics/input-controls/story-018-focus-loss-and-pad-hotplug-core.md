# Story 018: Focus loss and pad hot-plug in the router core — release-with-cancel, mute, swallowed recapture click, auto-pause request

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~0.5–1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R11 "Focus loss", R9 "Hot-plug", Edge Cases "Router-state changes" — focus lost mid-drag, pad disconnects mid-aim, Enter mid-hold; G2 causes "the window loses focus" and "the device that started it disconnects"; ACs H-EC-5 router half, H-EC-6 router half, H-G2-1 focus-loss and disconnect rows)
**Requirement**: `TR-input-022` (focus loss: release-with-cancel, zero movement, mute, swallow recapture click, solo auto-pause) — router half; the notification delivery is the adapter's (story 027)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "Focus loss" and "Hot-plug") — Accepted 2026-10-16 (router API); the **delivery** of `NotificationApplicationFocusOut` and `JoyConnectionChanged` is UNVERIFIED and spike-gated (story 001) — that part is story 027
**ADR Decision Summary**: `router.OnFocusLost()` synthesises release-with-cancel for every held input, zeroes axes, mutes everything until released, swallows the recapturing click, unlatches toggled sprint and mic, and — in solo, setting on — asks the host shell to pause. Hot-plug: a disconnect synthesises releases (cancelling holds and aim — **never a commit**); in solo, auto-pause if that pad was the last-used device; in multiplayer a "Controller disconnected" banner. A reconnected pad's held buttons are muted until released.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (router API); HIGH (delivery, story 027)
**Engine Notes**: Which of `NotificationApplicationFocusOut` / `NotificationWMWindowFocusOut` fires on which platform is spike item 5; both are mapped to the same router call by the adapter.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: focus loss synthesises release-with-cancel and pauses through `TickDriver.Pause()` in solo (manifest Presentation, ADR-0012 §7); a cancel is **shown**, never silent
- Forbidden: a synthetic release that commits (never a throw / deliver / drop); `SceneTree.Paused`
- Guardrail: 0 B; all effects inside one router call

---

## Acceptance Criteria

*From GDD R11 focus loss, R9 hot-plug, Edge Cases and H-EC-5 / H-EC-6 / H-G2-1, scoped to this story:*

- [ ] **`OnFocusLost()` (H-EC-5 router half).** With an Interact deliver hold running (E down), an LMB drag in the panel and LS held: one call produces `Cancel(FocusLost)` for the Interact and Aim machines (and for a drag, a **cancel** of the pick/place machine — **no `Place` immediate**, the entry stays on the cursor, the panel stays open), clears every `HeldBits` bit, zeroes `moveIntent` (and sets `Flags.FocusLost` for one flush), marks **every** pressed machine `Muted` until its physical release (swallowed), and writes `hold_cancel{FocusLost}`-equivalent log lines. A toggled sprint (and, M0, a toggled mic) unlatches.
- [ ] **Swallowed recapture click.** `OnFocusGained()` arms a one-shot guard: the first mouse-button press (and its release) after focus returns, while the cursor is being recaptured, is swallowed — it cannot attack (Tool), place or pick; the guard also clears on the first key press or after 500 ms of fake time so a keyboard-only return is not delayed.
- [ ] **Solo auto-pause.** With the setting `autoPauseSolo` true (default) `OnFocusLost()` raises `PauseRequested(solo: true, reason: FocusLost)` through `IPauseRequestSink` (story 016); with it false it does not pause but still cancels and mutes. In multiplayer (`solo = false`) no pause is requested. (The `solo` flag is supplied by the host shell via `SetSessionMode`; default solo at M-1a.)
- [ ] **H-EC-6 (router half).** `OnPadConnection(pad, connected: false)` mid-aim (Hold and Toggle modes) synthesises a **cancel**, never a commit: `Cancel(DeviceLost)` for the Aim machine, no Throw immediate, `aiming` cleared; releases for everything that pad held (held bits recomputed as the OR of the remaining devices); solo auto-pauses **if that pad was the last-used device**; in multiplayer a `Cue(ControllerDisconnected)` is logged and exposed as `router.ControllerDisconnectedBanner` (persistent until reconnect or dismissal) and nothing pauses. Reconnect with A held (`OnPadConnection(pad, true)` followed by an A press record with `Pressed` already true) → the button is **muted**: no edge until A is released and pressed again; the pad's axis values are re-seeded by the adapter (story 026), not here.
- [ ] **H-G2-1 (router-origin rows).** Parametrised: with a deliver hold at tick 6 (and an aim for the Back row in story 016), focus loss and device disconnect each yield `Cancel` edges with `EdgeReason.FocusLost` / `DeviceLost`, progress is irrelevant to the router (Sim resets it), nothing is sent, and the pressed inputs' releases are swallowed. (The ten Sim-originated rows run in story 020.)
- [ ] **Mid-drag and Enter mid-hold clauses.** A drag cut off by focus loss leaves the entry on the cursor (no `Place`, no `Return`); "Enter mid-hold" (TextEntry) is M0 and not asserted.

---

## Implementation Notes

*Derived from ADR-0012 §7 and GDD R9 / R11:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.Focus.cs` (`OnFocusLost`, `OnFocusGained`, `OnPadConnection`, `SetSessionMode(bool solo)`), `RecaptureGuard.cs`, `ControllerBanner.cs`.
- All effects use the same primitives as story 007's `OnStateChanged` (mute) and the cancel emitters in 012 / 014 — do not add a parallel path. The only new state is the recapture guard and the banner.
- `IPauseRequestSink` is the story 016 port; the router never calls `TickDriver` (a Sim type it cannot reference as a driver anyway — the host does).
- The adapter calls these methods from the main thread inside `_Notification` / the signal handler (story 027); nothing here allocates.
- `padIndex` identity at reconnect (hot-plug index reuse) is UNVERIFIED (spike item 4); the router treats a connect as a fresh device in that slot.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 027: receiving `NotificationApplicationFocusOut/In`, `JoyConnectionChanged`, re-applying mouse capture, the project setting. Story 029: the Godot-hosted integration run (H-EC-5 / H-EC-6 full) and MANUAL evidence. Story 028: calling `TickDriver.Pause()` on `PauseRequested`.
- M0: TextEntry capture, mic latch, "Reconnecting" capture state (OQ-IC-18).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/FocusLossTests.cs` (`[Trait("Id", "H-EC-5")]`, `[Trait("Id", "H-G2-1")]`), `tests/unit/input/PadHotplugTests.cs` (`[Trait("Id", "H-EC-6")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 012, 014, 016 (pause sink), 017 (last-used device, pad slots), 013 (drag/Place immediate suppression)
- Unlocks: Stories 020, 021, 027; `production/epics/player-character/story-017-hold-counter-cancels-channel-focus-latch.md` (receives the `Cancel` edges)
