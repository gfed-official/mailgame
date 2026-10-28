# Story 023: Fantasy-test scripts — the hold-free shift (H-FX-4), route input-count parity (H-FX-3a) and per-class Toggle coverage (H-A11Y-1)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 7:13 AM PT)
> **Deferred to**: M1 (accessibility pass) (out of the M-1a slice; was Ready)
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~350 lines of scripted tests on the story 020 harness)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **DEFERRED 2026-10-23 by Evan, not deleted.** Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". Reason: accessibility and fantasy-test scripting (H-FX-4, H-FX-3a, H-A11Y-1) is production hardening, not friends' fun (Evan 7:15). Not in ADR-0012's Validation Criteria: l.280 covers R1–R12, Formulas and Edge Cases; H-FX-1 at l.282 is story 022's. The GDD tags these ACs M-1a (input-controls.md l.864, l.866, l.875); reconcile the GDD tags at the next re-review. Re-run `/story-readiness` when M1 (accessibility pass) planning starts; the text below is unchanged from the last Ready/Blocked version.

## Context

**GDD**: `design/gdd/input-controls.md` (Player Fantasy → Fantasy test 2 "Ten-second guest" (the Logic half, H-FX-3a) and 3 "Hold-free shift" (H-FX-4); Accessibility H-A11Y-1; R10 hold classes)
**Requirement**: no TR-ID in the registry for the fantasy tests — cited by GDD ACs H-FX-3a, H-FX-4, H-A11Y-1 (the toggles and the controller-complete guarantee are the GDD's accessibility promise, not a TR)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 Toggle translation; Validation Criteria) — Accepted 2026-10-16 (primary); ADR-0014 (§4 hold counter), ADR-0015 (throw payload) secondary
**ADR Decision Summary**: Toggle modes change only *how the press is given*, never what the verb does: the window to back out is kept for toggled holds and a toggled aim still needs a separate deliberate press to release, so accessibility never makes a verb easier to misfire. The Sim sees only logical press / release / cancel.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim + Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: tests exercise the real router and Sim code through the story 020 harness
- Forbidden: asserting HUD text or colour (HUD has no epic yet)
- Guardrail: tests only

---

## Acceptance Criteria

*From GDD H-FX-3a, H-FX-4, H-A11Y-1, scoped to this story (M-1a verbs; catch, comms wheel and push-to-talk rows are M0 / M1 and listed as deferred):*

- [ ] **H-FX-4 (hold-free shift).** A scripted player with **every toggle on** (Deliver, Throw aim, Sprint on both devices) whose every press lasts ≤ 300 ms completes, in one run: *take* (Interact tap on a WorldItem), *open panel* (Tab), *deliver* (Toggle hold — one press, completes by itself at tick 12 with the button up), *aim and throw* (first press enters Aiming, a second press ≥ 250 ms later throws), *cancel aim* (Back), and *drop* (Q) — each verb's effect is observed in the recorded requests / state. A toggled aim with **no second press for 30 s** never throws, and the same script with a second press at 200 ms (< `throwMinAim`) is ignored and aiming continues. (The catch row — "catches a teammate's lob using the Toggle catch-armed latch" — is M0.)
- [ ] **H-FX-3a (route parity).** Every registered action is reachable on gamepad alone (story 006's pad-path check is re-run here against the built map). A scripted 10-address hand route — one `open` of the Intake, one take-all and one deliver hold per house — costs the **same number of inputs** on KBM and on pad, counted from the router's fired-action log: the `open` and the ten deliver holds are asserted now; the take-all step is `[Fact(Skip = "take-all is panel-button / action-menu (M1); no M-1a binding")]` with that reason, so the parity assertion cannot silently pass over it. **Bound:** Grid Inventory's H-FX-1 counts 1 + 1 + 10 = **12** ops; Input & Controls' H-FX-3a text says "≤ 11" — an internal inconsistency in the GDD pair (see EPIC.md "GDD conflicts"). This story asserts parity and the Grid bound (≤ 12) and records the discrepancy in Last Updated.
- [ ] **H-A11Y-1.** Each hold class in `InputSettings.HoldMode` (story 019) — Deliver / timed holds, Throw aim, Sprint (per device) — has a Toggle (Sprint also Auto), and **every verb in the class works under it**: a `[Theory]` over the class enum (derived from the enum, so a new class forces a new case) runs the class's verb end to end in Hold and in Toggle and asserts the same effect; the classes Comms wheel and Push-to-talk (M1 / M0) are asserted to be **absent** from the enum at M-1a with the deferral named in the test (so adding them without cases fails).
- [ ] **No easier misfire.** In Toggle mode a deliver hold still takes the full `holdEff` before sending and still offers the cancel window (a second press before tick 12 cancels); Toggle throw still needs `aimHeld ≥ throwMinAim` — i.e. toggles never make a verb easier to misfire than Hold mode (asserted against the Hold-mode run of the same script).

---

## Implementation Notes

*Derived from the GDD fantasy tests and ADR-0012 §3:*

- Files: `tests/integration/input/HoldFreeShiftTests.cs`, `RouteParityTests.cs`, `HoldClassCoverageTests.cs` on `InputToSimHarness` (story 020). Namespace `PerformativeMail.Sim.Tests.Integration.Input`.
- "Take" at M-1a is a WorldItem pickup (Interact, Tap) via Grid Inventory's `Pickup` op (`production/epics/grid-inventory/story-014-pickup-core-trycatch.md`, `story-019-range-gated-open-pickup-leash.md`); "drop" is Grid's `drop` (`story-020-drop-op.md`).
- Count inputs as router `GateFired` log lines for `Frame` Hold/Tap and `Immediate` actions (not raw keystrokes); a Toggle press counts once.
- The 10-address route fixture is Grid Inventory H-FX-1's (10 letter stacks + 4 Small/Medium packages in district d1). If its builder is not yet available, build the same data in the test and say so in Last Updated.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 022: the unauthored-verb corpus. H-FX-2 and H-FX-3b (MANUAL playtests, M0). H-A11Y-2/3/4/6 (keyboard-only / gamepad-only panel flows, remap-screen operability, live-region text — M1). H-A11Y-5 (hold-ring greyscale [UI], M-1a) needs a HUD prompt/ring screen; HUD has no epic and no UX spec yet — listed as an unowned seam in EPIC.md.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/integration/input/HoldFreeShiftTests.cs` (`[Trait("Id", "H-FX-4")]`), `RouteParityTests.cs` (`[Trait("Id", "H-FX-3a")]`), `HoldClassCoverageTests.cs` (`[Trait("Id", "H-A11Y-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 020, 021, 006 (pad-path check), 019 (hold classes); `production/epics/grid-inventory/story-014-pickup-core-trycatch.md`, `story-020-drop-op.md`
- Unlocks: None (closes the router-core acceptance set)
