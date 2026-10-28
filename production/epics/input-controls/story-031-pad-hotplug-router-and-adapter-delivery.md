# Story 031: Pad hot-plug — router `OnPadConnection` (cancel-never-commit, reconnect mute, disconnect banner) and the adapter's `JoyConnectionChanged` delivery (H-EC-6)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; split from stories 018 and 027)
> **Layer**: Foundation (router half) / Presentation (adapter half)
> **Type**: Logic (router ACs) + Integration (delivery AC)
> **Estimate**: S (~0.5 day; ~150 lines router + ~50 lines adapter + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E1 (ADR-0012 — pad hot-plug, H-EC-6)**. Reason: unplugging a pad mid-play is not a friends'-game blocker, whereas stuck keys after alt-tab are, so focus loss stays in story 018 / 027 and hot-plug moves here. The ACs below were moved verbatim from stories 018 (router half) and 027 (delivery). Re-run `/story-readiness` when M0 planning starts.

## Context

**GDD**: `design/gdd/input-controls.md` (R9 "Hot-plug", Edge Cases "Router-state changes" — pad disconnects mid-aim; G2 cause "the device that started it disconnects"; ACs H-EC-6 router and integration halves, H-G2-1 disconnect row)
**Requirement**: `TR-input-015` (hot-plug via `joy_connection_changed`, UNVERIFIED) — adapter half; the disconnect clause of `TR-input-022`'s cancel rules — router half
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "Hot-plug") — Accepted 2026-10-16 (router API); the **delivery** of `JoyConnectionChanged` is UNVERIFIED and spike-gated (story 001); E1 (2026-10-23) moves H-EC-6 hot-plug to M0
**ADR Decision Summary**: Hot-plug: a disconnect synthesises releases (cancelling holds and aim — **never a commit**); in solo, auto-pause if that pad was the last-used device; in multiplayer a "Controller disconnected" banner. A reconnected pad's held buttons are muted until released. Hot-plug (`Input.JoyConnectionChanged`, UNVERIFIED) → `router.OnPadConnection`.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core for the router half; `src/Game/Input/` for delivery) | **Risk**: LOW (router API); HIGH (delivery)
**Engine Notes**: `padIndex` identity at reconnect (hot-plug index reuse) is UNVERIFIED (spike item 4); the router treats a connect as a fresh device in that slot. The axis cache is re-seeded by the adapter on reconnect (story 026).

**Control Manifest Rules (Foundation / Presentation)**:
- Required: a cancel is **shown**, never silent (manifest Presentation, ADR-0012 §7)
- Forbidden: a synthetic release that commits (never a throw / deliver / drop); `SceneTree.Paused`
- Guardrail: 0 B (advisory until M0, E2); all router effects inside one router call

---

## Acceptance Criteria

*From GDD R9 hot-plug, Edge Cases and H-EC-6 / H-G2-1, moved verbatim from stories 018 and 027:*

- [ ] **H-EC-6 (router half)** (from story 018). `OnPadConnection(pad, connected: false)` mid-aim (Hold and Toggle modes) synthesises a **cancel**, never a commit: `Cancel(DeviceLost)` for the Aim machine, no Throw immediate, `aiming` cleared; releases for everything that pad held (held bits recomputed as the OR of the remaining devices); solo auto-pauses **if that pad was the last-used device**; in multiplayer a `Cue(ControllerDisconnected)` is logged and exposed as `router.ControllerDisconnectedBanner` (persistent until reconnect or dismissal) and nothing pauses. Reconnect with A held (`OnPadConnection(pad, true)` followed by an A press record with `Pressed` already true) → the button is **muted**: no edge until A is released and pressed again; the pad's axis values are re-seeded by the adapter (story 026), not here.
- [ ] **H-G2-1 (disconnect row)** (from story 018, where the focus-loss row remains). Parametrised: with a deliver hold at tick 6 (and an aim for the Back row in story 016), device disconnect yields `Cancel` edges with `EdgeReason.DeviceLost`, progress is irrelevant to the router (Sim resets it), nothing is sent, and the pressed inputs' releases are swallowed.
- [ ] **Hot-plug delivery** (from story 027). `Input.JoyConnectionChanged(device, connected)` calls `router.OnPadConnection((byte)device, connected)` (signal subscribed in `Initialize`, unsubscribed in `_ExitTree`); **H-EC-6 (integration half)** (the integration AC moved from story 029, below): a pad unplugged mid-aim cancels (never throws); solo auto-pauses if it was the last-used device; MP shows "Controller disconnected" and nothing pauses; reconnect with A held produces no edge until released.
- [ ] **H-EC-6 (integration) (from story 029).** A pad unplugged mid-aim (Hold and Toggle) → cancel, never a throw; solo auto-pauses if that pad was last-used; MP "Controller disconnected" (banner state) without pausing; reconnect with A held → no edge until A is released. Headless emits `Input.Singleton.EmitSignal(Input.SignalName.JoyConnectionChanged, device, connected)` from the smoke scene, as the spike's S-16 probe does (`prototypes/spike-oq-ic-9-input/DeviceProbe.cs` l.121–128); a **real pad** hot-plug run is MANUAL.

*"Last-used device" is story 030's (M1). Until it lands, keep a minimal local marker (the last `PadIndex` that produced a press edge) inside this story's router code; do not build the glyph machine here.*

---

## Implementation Notes

*Derived from ADR-0012 §7 and GDD R9 (moved from stories 018 and 027):*

- Router files in `src/PerformativeMail.Client.Core/Input/`: `OnPadConnection` added to `InputRouter.Focus.cs` (story 018), `ControllerBanner.cs`. Use story 007's mute primitive and the cancel emitters in 012 / 014 — do not add a parallel path; the only new state is the banner and the per-slot "fresh device" bookkeeping over story 017's pad slots.
- Adapter: the `JoyConnectionChanged` signal hookup in `src/Game/Input/InputAdapter.cs` (story 026), called on the main thread; `OnPadConnection` must not allocate.
- Story 029 (Ready) no longer carries the H-EC-6 integration AC: it moved here (AC above). The real-pad MANUAL row M-1 hot-plug part runs with this story.

---

## Out of Scope

- Story 018 (Ready, M-1a): focus loss (`OnFocusLost` / `OnFocusGained`, recapture guard, solo auto-pause on focus loss). Story 027 (Ready, M-1a): mouse mode, GUI-focus policy, focus notifications, the joypad project setting. Story 030 (M1): the last-used device / glyph machine. M0: "Reconnecting" capture state (OQ-IC-18). Steam virtual-pad handling (M1 / spike).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic + Integration
**Required evidence**:
- Logic: `tests/unit/input/PadHotplugTests.cs` (`[Trait("Id", "H-EC-6")]`) — must exist and pass on net10.0 and net8.0 (from story 018); the `DeviceLost` row of `tests/unit/input/FocusLossTests.cs` (`[Trait("Id", "H-G2-1")]`) moves here with it
- Integration: smoke-scene log rows and MANUAL sign-offs for the delivery AC in `production/qa/evidence/input-adapter-evidence.md` (story 029) — MANUAL rows that need a real pad are recorded as such, never marked passed from a headless run (from story 027)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 018 (router focus API, mute / cancel primitives, `SetSessionMode`), Story 027 (adapter `_Notification` / signal hookup pattern, `Initialize`), Story 017 (pad slots), Story 016 (`IPauseRequestSink`); Story 030 (M1) only for the final `LastUsedDevice` source — see the AC note
- Unlocks: —
