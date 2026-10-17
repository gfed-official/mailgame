# Story 009: Analog input — F3 deadzone and response curve, F4 trigger hysteresis, movement across devices and absolute look

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (Formulas F3, F4; R9 "movement takes the axis with the largest magnitude"; R11 input frame — `moveIntent`, `lookYaw` / `lookPitch` absolute, `SetLook`; ACs H-F3-1, H-F4-1, H-R9-1 movement half, H-R11-1 look clause)
**Requirement**: `TR-input-017` (deadzone / trigger / repeat / step caps in integer math; one step per rendered frame — this story: deadzone and trigger)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§5 integer maths — Q15 axis values with `ISqrt` magnitude, 1024-entry Q15 power table per curve rebuilt on settings change, look accumulation with carried remainder, `SetLook`) — Accepted 2026-10-16 (primary); ADR-0006 (integer-only, BAM angles) secondary
**ADR Decision Summary**: Settings are stored in integer units (permille, ms, counts, px). F3's radial deadzone works on Q15 axis values with `ISqrt` magnitude; the look response `n^curve` uses a 1024-entry Q15 power table per curve value, rebuilt on settings change outside the frame. F4 is an integer comparison with hysteresis. Look accumulates mouse counts × sensitivity into BAM with a carried remainder; `SetLook(yaw, pitch)` re-seeds the router's absolute look on Teleport, mount and dismount.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0006)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: Joypad axes arrive raw, one axis per event, only on change (ADR-0012 Step 5.5 note 5) — the *caching* is the adapter's (story 026); this story consumes cached values handed to `Feed` as `PadAxis` records.

**Control Manifest Rules (Foundation)**:
- Required: integer maths only in the router core (Q15 axes, permille settings, BAM angles); every constant is data in `InputDefaults` / settings, never a literal (GDD Formulas preamble)
- Forbidden: `float` / `double` / `Math.Pow` / `MathF` in Client.Core; allocation in the per-event path; any world query in the look path (F10 invariant, M1 setting)
- Guardrail: 0 B; the power table is built outside the frame

---

## Acceptance Criteria

*From GDD F3, F4 and H-F3-1 / H-F4-1 / H-R9-1 / H-R11-1, scoped to this story:*

- [ ] **H-F3-1 (function).** `Deadzone.Apply(sx, sy, dzInnerPm, dzOuterPm, curvePm)` on Q15 inputs: s = (0.6, 0) with dz 0.15 / 0.95 → move magnitude 0.5625 (Q15 18432 ± 1) and look 0.5625^1.5 ≈ 0.42 (Q15 13824 ± 2, move stick curve fixed at 1.0); s = (0.14, 0) → 0; s = (0.5, 0.5) keeps its direction (the output vector's angle equals the input's, no cardinal snap); the result is clamped to the unit disc; `n = clamp((|s| − dzInner)/(dzOuter − dzInner), 0, 1)`; magnitude via `Fix.ISqrt` on the squared Q15 sum. (The load-time clamp "`dzInner` 0.9 → 0.40, `dzOuter` unchanged" is story 019's.)
- [ ] **Power table.** A 1024-entry Q15 table of `n^curve` per curve value (1.0–3.0, 100‰ steps or the exact settings value) is built by integer-only code outside the frame (e.g. fixed-point log / exp, or a generated committed table like `tools/GenTrigQ15` — implementer's choice, recorded in Last Updated), cached per `curvePm`, rebuilt only on a settings change; monotonic non-decreasing in `n`, `t[0] = 0`, `t[1023] = 32767`, and spot values (0.5625^1.5) within ± 2 LSB.
- [ ] **H-F4-1.** `TriggerHysteresis` per trigger: `pressed_t = pressed_(t−1) ? v ≥ max(trigOn − trigHyst, 0.05) : v ≥ trigOn` in integer permille with `trigHyst` = 150: LT rising to 0.55 → pressed; 0.40 → still pressed; 0.34 → released; with `trigOn` 0.15 the release point is 0.05; a digital trigger (0 / 1) works at any setting; the pad's Throw (LT) and Tool (RT) presses reach the router as logical button presses through this function.
- [ ] **H-R9-1 (movement half).** Movement across devices takes the axis with the largest magnitude: LS at 0.4 and W held (digital, magnitude 1.0, diagonals normalised to the unit disc via the same `ISqrt` path: W+D → (23170, 23170) ± 1) → `moveIntent` is W at magnitude 1.0; releasing W returns LS's 0.4; `moveIntent` is Q15 on the unit disc, x right, y forward, **camera-relative** conversion is *not* done here (Player Character 005 does it from `LookYaw`).
- [ ] **H-R11-1 (look half).** Mouse counts × sensitivity accumulate into `lookYaw` (ushort BAM, wraps) and `lookPitch` (short BAM, clamped to the camera owner's limits) as **absolute** values with a carried sub-BAM remainder: a scripted 90° (0x4000 BAM) of mouse motion over 3 ticks yields absolute yaw values at each flush (no deltas); right-stick look integrates `rate × dtMs / 1000` per rendered frame with a carried remainder; look is **not live** in Panel / Pause (input ignored, value held); `SetLook(yaw, pitch)` re-seeds the absolute look, sets `LookReseeded` for one flush and the next mouse motion continues from the new value (no jump back).
- [ ] **View parameters.** `IFocusConeSink { void SetFocusCosMinPm(int pm); }` is declared in Client.Core and implemented by `InputRouter` (`SetViewParams(short focusCosMinPm)` is the ADR-named method; the sink is the one-method interface Player Character 023 asked for); the value reaches `InputFrame.FocusCosMinPm` via `SetState`, unchanged between calls. `SetPitchLimits(short minBam, short maxBam)` (additive to ADR-0012's signature, flagged in EPIC.md) is pushed by the camera owner; default −60° / +70° per Player Character story 021.

---

## Implementation Notes

*Derived from ADR-0012 §5 and GDD F3 / F4:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `Deadzone.cs`, `ResponseCurveTable.cs`, `TriggerHysteresis.cs`, `MoveAxes.cs` (per-device raw axis store + digital-key contribution + largest-magnitude selection), `LookState.cs`, `IFocusConeSink.cs`, `InputRouter.Analog.cs`, `InputDefaults.cs` (PROPOSED constants: `dzInnerPm` 150, `dzOuterPm` 950, `curvePm` 1500, `trigOnPm` 500, `trigHystPm` 150, `trigFloorPm` 50, `bamPerCountQ16`, stick look rate).
- Mouse look consumes `MouseMotion` records with `Value / Value2` counts taken from `ScreenRelative` by the adapter (ADR-0012 Step 5.5 note 2) — the router does not care about stretch.
- Digital `look_*` actions (bindable to keys / buttons, one-stick play) are M1 remap fixtures; no default binding.
- Sensitivity, invert and rumble are settings (story 019); use the defaults here and read `InputSettings` fields so story 019 only changes the source.
- `moveIntent` for the frame is computed at `FlushToLatch` from the current axis state; it is **state**, not an edge (G3), and is zeroed by the capture layer / Map / Draft / turret seat / Dead (those zeroing sources beyond Pause are M0+).
- Auto-sprint (F10-adjacent, Player Character P14) reads `moveIntent` magnitude — story 012.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 010: stick-as-digital-direction (F5), steps, drag. Story 012: sprint modes and F8. Story 019: setting ranges and clamping. Story 017: glyph-flip use of `stickOut`.
- Camera-relative movement and body yaw (Player Character 005); applying look to the camera every rendered frame (Player Character 023).
- Aim steadiness F10 (M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/DeadzoneTests.cs` (`[Trait("Id", "H-F3-1")]`), `tests/unit/input/TriggerHysteresisTests.cs` (`[Trait("Id", "H-F4-1")]`), `tests/unit/input/MoveLookTests.cs` (`[Trait("Id", "H-R9-1")]`, `[Trait("Id", "H-R11-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 008 (look not live in Panel), 003; `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.ISqrt`), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (BAM)
- Unlocks: Stories 010, 012, 017; `production/epics/player-character/story-023-camera-rig-node-focus-cone.md` (`IFocusConeSink`, look source), `production/epics/player-character/story-005-locomotion-f1-f2-camera-relative-yaw.md` (move / look inputs)
