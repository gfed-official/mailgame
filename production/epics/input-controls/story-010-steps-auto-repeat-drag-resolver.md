# Story 010: Stepped verbs — F5 auto-repeat and stick-as-direction, F6 saturating step counts, F7 click-or-drag

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (Formulas F5, F6, F7; R3 "Click-or-drag" grammar; G4 "Mouse-wheel notches arrive as press and release in one event and count as steps"; ACs H-F5-1, H-F6-1, H-F7-1, H-EC-14 comms-wheel half is M1)
**Requirement**: `TR-input-017` (deadzone / trigger / repeat / step caps in integer math; one step per rendered frame — this story: repeat, step caps, drag); `TR-input-010` (the monotonic integer-ms clock serves repeat / drag)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§5 "F5 repeat (`1` or `2 + (T − delay)/interval`, ≤ 1 step per rendered frame), F6 saturation, F7 click-or-drag … are integer comparisons"; §4 steps `[InlineArray(4)]`) — Accepted 2026-10-16
**ADR Decision Summary**: F5, F6 and F7 are integer-ms / integer-count rules in the router core on the ms clock; at most one repeat step is emitted per rendered frame and the surplus is dropped, never queued; stepped actions reach the Sim as saturating signed counts in `InputFrame.Steps`; the Sim never sees milliseconds.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: Whether a headless/real mouse wheel arrives as press + release in one event or as a smooth fractional value is spike material (story 001 item list); the router accepts both as `Wheel` records.

**Control Manifest Rules (Foundation)**:
- Required: the ms clock for repeat, drag, glyph, aim; the Sim never receives milliseconds (ADR-0012 §4, manifest Input router core)
- Forbidden: OS key-repeat for repetition (the game's own F5 is used); queued surplus steps; floats
- Guardrail: 0 B; ≤ 1 repeat step per rendered frame

---

## Acceptance Criteria

*From GDD F5 / F6 / F7 and H-F5-1 / H-F6-1 / H-F7-1, scoped to this story:*

- [ ] **H-F5-1.** `StepRepeater` with the defaults `repeatDelayMs` 300, `repeatIntervalMs` 80: `steps(T) = T < delay ? 1 : 2 + (T − delay)/interval` in integer ms — a direction held 699 ms → 6 steps, 700 ms → 7; the edge bump plays at the first edge hit, and the cross to the other panel happens at `edgeDwell` = `repeatDelay + edgeDwellExtra(150)` = 450 ms **after the first bump** (1150 ms in the GDD example), the dwell clock is **not reset** by later repeat bumps, or immediately on a **fresh push** at 710 ms; a 100 ms rendered-frame hitch emits at most one step that frame (surplus dropped, never queued); the minimum interval 40 ms gives ≤ 25 steps / s.
- [ ] **Stick as direction.** A stick acts as a digital direction when its dominant post-deadzone axis is ≥ `stickEngage` (500‰) and stops below `stickRelease` (300‰); the other axis is ignored (no diagonal steps); hysteresis test: 0.55 engages, 0.40 holds, 0.29 releases, re-engaging needs ≥ 0.5 again. The D-pad uses the same repeater (stick and D-pad feel identical).
- [ ] **H-F6-1.** `SaturatingSteps`: `stepsTick[a] = clamp(Σ notches in the tick, −cap[a], +cap[a])` with surplus **discarded, not carried**: 23 notches in one tick → hotbar +4 (slot 2 → 6); −23 → −4; the next tick with 0 notches adds 0; a stepper action (M1) caps at ±10; smooth / touchpad wheels accumulate fractional notches (stored in 1/1000 notch) and emit one at |acc| ≥ 1: 0.4 × 3 → 1 step. `cap[hotbar]` = `hotbarSize / 2` = 4 (safe 2–4, clamped). The steps are pushed with `IInputLatchWriter.PushSteps` (story 002); a wheel notch is a `Wheel` record (press+release in one event) and never held state.
- [ ] **H-F7-1.** `ClickOrDragResolver`: `placeOnRelease = maxDisp ≥ dragMinPx × uiScale OR (tRelease − tPress) ≥ dragMinSeconds`, in integer px / ms / permille `uiScale`: 180 ms / 3 px → tap-lift (the entry stays on the cursor); 120 ms / 40 px → place; 250 ms / 0 px → place, 249 ms → lift; at `uiScale` 2.0, 11 px → lift, 12 px → place (defaults `dragMinPx` 6 at 1080p, `dragMinMs` 250). `maxDisp` tracks the largest pointer distance from the press point during the press (squared-distance comparison, no sqrt on the hot path).
- [ ] **Hotbar steps.** The wheel and D-pad ←→ in World / Holding-closed push `HotbarStep` steps (F6); D-pad ↔ in Panel / Holding-open push `GridCursor` steps (consumed by story 011); neither is live while Aiming. Digit keys are not steps (story 013).

---

## Implementation Notes

*Derived from ADR-0012 §5 and GDD F5–F7:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `StepRepeater.cs` (per-direction state: `pressedAtMs`, `lastStepMs`, `bumpedAtMs`), `StickDirection.cs`, `SaturatingSteps.cs`, `ClickOrDragResolver.cs`, `InputRouter.Steps.cs`. Constants in `InputDefaults` (story 009): `stickEngagePm` 500, `stickReleasePm` 300, `edgeDwellExtraMs` 150, `dragMinPx` 6, `dragMinMs` 250.
- `repeat` honours the F5 rule "at most one step per rendered frame": the router's per-frame hook is `FlushToLatch` (one call per rendered frame) — compute steps there from the ms clock, not per record.
- OS key-repeat (`Echo`) never produces steps (G4); the grid cursor's repeat is entirely F5.
- The panel-edge *bump* and *cross* are reported as flags on the `GridCursor` step stream for story 011 (`StepResult { Moved, Bumped, Crossed }`) — define the small enum here.
- Click-or-drag applies to LMB in Panel / Holding-open only; the resulting `placeOnRelease` flag rides on the Pick/place immediate (story 013).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 011: what a cursor step *means* in the grid (origin landing, no wrap, last-cell memory). Story 013: the immediate-verb queue that carries the drag decision. M1: the split stepper, comms wheel (23 px dead zone), page / jump rows.
- Settings ranges (story 019).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/StepRepeaterTests.cs` (`[Trait("Id", "H-F5-1")]`), `tests/unit/input/SaturatingStepsTests.cs` (`[Trait("Id", "H-F6-1")]`), `tests/unit/input/ClickOrDragTests.cs` (`[Trait("Id", "H-F7-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 009 (post-deadzone stick values, defaults), 007, 008 (panel vs world routing)
- Unlocks: Stories 011, 013
