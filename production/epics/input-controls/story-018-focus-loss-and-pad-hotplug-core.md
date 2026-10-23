# Story 018: Focus loss in the router core — release-with-cancel, mute, swallowed recapture click, auto-pause request

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (~0.5 day; ~300 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23 (E1, ADR-0012).** Pad hot-plug — `OnPadConnection`, H-EC-6 (router half), the `DeviceLost` row of H-G2-1 and the controller-disconnected banner — moved to **story 031 (Deferred to M0)**, together with story 027's delivery AC. This story keeps focus loss: stuck keys after alt-tab are a play bug, so release-with-cancel, mute, the swallowed recapture click and the solo auto-pause request stay in M-1a. Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all".

## Context

**GDD**: `design/gdd/input-controls.md` (R11 "Focus loss", Edge Cases "Router-state changes" — focus lost mid-drag, Enter mid-hold; G2 cause "the window loses focus"; ACs H-EC-5 router half, H-G2-1 focus-loss row)
**Requirement**: `TR-input-022` (focus loss: release-with-cancel, zero movement, mute, swallow recapture click, solo auto-pause) — router half; the notification delivery is the adapter's (story 027)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "Focus loss") — Accepted 2026-10-16 (router API); the **delivery** of `NotificationApplicationFocusOut` is UNVERIFIED and spike-gated (story 001) — that part is story 027. §7 "Hot-plug" is story 031 (E1, M0).
**ADR Decision Summary**: `router.OnFocusLost()` synthesises release-with-cancel for every held input, zeroes axes, mutes everything until released, swallows the recapturing click, unlatches toggled sprint and mic, and — in solo, setting on — asks the host shell to pause.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW (router API); HIGH (delivery, story 027)
**Engine Notes**: Which of `NotificationApplicationFocusOut` / `NotificationWMWindowFocusOut` fires on which platform is spike item 5; both are mapped to the same router call by the adapter.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: focus loss synthesises release-with-cancel and pauses through `TickDriver.Pause()` in solo (manifest Presentation, ADR-0012 §7); a cancel is **shown**, never silent
- Forbidden: a synthetic release that commits (never a throw / deliver / drop); `SceneTree.Paused`
- Guardrail: 0 B (advisory until M0, E2); all effects inside one router call

---

## Acceptance Criteria

*From GDD R11 focus loss, Edge Cases and H-EC-5 / H-G2-1, scoped to this story:*

- [ ] **`OnFocusLost()` (H-EC-5 router half).** With an Interact deliver hold running (E down), an LMB drag in the panel and LS held: one call produces `Cancel(FocusLost)` for the Interact and Aim machines (and for a drag, a **cancel** of the pick/place machine — **no `Place` immediate**, the entry stays on the cursor, the panel stays open), clears every `HeldBits` bit, zeroes `moveIntent` (and sets `Flags.FocusLost` for one flush), marks **every** pressed machine `Muted` until its physical release (swallowed), and writes `hold_cancel{FocusLost}`-equivalent log lines. A toggled sprint (and, M0, a toggled mic) unlatches.
- [ ] **Swallowed recapture click.** `OnFocusGained()` arms a one-shot guard: the first mouse-button press (and its release) after focus returns, while the cursor is being recaptured, is swallowed — it cannot attack (Tool), place or pick; the guard also clears on the first key press or after 500 ms of fake time so a keyboard-only return is not delayed.
- [ ] **Solo auto-pause.** With the setting `autoPauseSolo` true (default) `OnFocusLost()` raises `PauseRequested(solo: true, reason: FocusLost)` through `IPauseRequestSink` (story 016); with it false it does not pause but still cancels and mutes. In multiplayer (`solo = false`) no pause is requested. (The `solo` flag is supplied by the host shell via `SetSessionMode`; default solo at M-1a.)
- [ ] **H-G2-1 (focus-loss row).** Parametrised: with a deliver hold at tick 6 (and an aim for the Back row in story 016), focus loss yields `Cancel` edges with `EdgeReason.FocusLost`, progress is irrelevant to the router (Sim resets it), nothing is sent, and the pressed inputs' releases are swallowed. (The `DeviceLost` row is story 031; the ten Sim-originated rows run in story 020.) `EdgeReason` keeps its `DeviceLost` member so story 031 adds no enum change.
- [ ] **Mid-drag and Enter mid-hold clauses.** A drag cut off by focus loss leaves the entry on the cursor (no `Place`, no `Return`); "Enter mid-hold" (TextEntry) is M0 and not asserted.

---

## Implementation Notes

*Derived from ADR-0012 §7 and GDD R9 / R11:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.Focus.cs` (`OnFocusLost`, `OnFocusGained`, `SetSessionMode(bool solo)`), `RecaptureGuard.cs`.
- All effects use the same primitives as story 007's `OnStateChanged` (mute) and the cancel emitters in 012 / 014 — do not add a parallel path. The only new state is the recapture guard.
- `IPauseRequestSink` is the story 016 port; the router never calls `TickDriver` (a Sim type it cannot reference as a driver anyway — the host does).
- The adapter calls these methods from the main thread inside `_Notification` (story 027); nothing here allocates.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 031 (Deferred, M0): `OnPadConnection`, H-EC-6, the `DeviceLost` row, the controller banner and the `JoyConnectionChanged` delivery. Story 027: receiving `NotificationApplicationFocusOut/In`, re-applying mouse capture, the project setting. Story 029 (Ready, Track B): the Godot-hosted integration run (H-EC-5 full) and MANUAL evidence. Story 028: calling `TickDriver.Pause()` on `PauseRequested`.
- M0: TextEntry capture, mic latch, "Reconnecting" capture state (OQ-IC-18).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/FocusLossTests.cs` (`[Trait("Id", "H-EC-5")]`, `[Trait("Id", "H-G2-1")]` focus-loss row) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 012, 014, 016 (pause sink), 017 (per-input ownership and mute across devices), 013 (drag/Place immediate suppression)
- Unlocks: Stories 020, 021, 027; Deferred: 031 (hot-plug, M0); `production/epics/player-character/story-017-hold-counter-cancels-channel-focus-latch.md` (receives the `Cancel` edges)
