# Story 005: Locomotion — F1 final speed, F2 acceleration, camera-relative heading, body yaw and sprint rules

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P3 locomotion; F1 final speed; F2 acceleration, deceleration, turning; Game Feel "Ramp")
**Requirement**: `TR-movement-001` (pure integer solver — the horizontal half), `TR-movement-002` (mm / mm/s / permille / ticks; int64 before the first multiply; one final division), `TR-movement-005` (fixed-point trig for camera-relative input and yaw), `TR-movement-020` (stat inputs `MoveSpeedAddPm` as `BuffAddPm`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P3-1, P3-2, P3-3, F1-1, F1-2, F2-1, FEEL-1.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§1 pure `OwnedCarrier.Step`, §2 state/horizontal step/F1/F2, §3 angles) — Accepted 2026-10-16 (primary); ADR-0006 (Fix helpers, one rounding per formula) secondary
**ADR Decision Summary**: `speedMmS = base × weightPm × buffPm × wadePm / 10^9` in `long` with one `Fix.MulDiv` (truncation). Acceleration: `dvMax = CeilDiv(max(vRef, |v|), airborne ? Tair : T)`; if `|Δ|² ≤ dvMax²` then `v = vTarget` else `v += MulDiv(Δ, dvMax, |Δ|)` (truncation guarantees the step never exceeds `dvMax`). Horizontal step uses an exact 1/30 mm remainder: `n = V + Rem; d = FloorDiv(n, 30); Rem = n − 30·d`, so 5000 mm/s steps 166, 167, 167. Camera-relative input: `wx = mx·c − my·s`, `wz = −mx·s − my·c`; `vTarget = MulDiv(w, vRef, 32767²)`. Body yaw: `target = Atan2Bam(−wx, −wz)`, advance by at most `yawStep = FloorDiv(65536 × turnDegPerSec, 360 × 30)` (4369); while aiming `BodyYaw = lookYaw`.
**ADR Version**: ADR-0013 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Do not use `Fx16` for the carrier position (ADR-0013 §2; it drifts by 1 mm at exact boundaries).

**Control Manifest Rules (Feature layer)**:
- Required: movement is computed only by the pure `OwnedCarrier.Step` — no clock, RNG, statics (except `ReadOnlySpan` literal tables), allocation or exceptions; integer maths widened to `long` before the first multiply (ADR-0013)
- Required: carrier sub-mm integration uses the tick-rational remainders (1/30 mm horizontal), never `Fx16`; every constant comes from `PlayerSettings` (ADR-0013)
- Forbidden: floats, Godot physics bodies, engine interpolation for player movement (ADR-0006, ADR-0013)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P3-1/2/3, PCM-F1-1/2, PCM-F2-1, PCM-FEEL-1), scoped to this story. Flat, dry, collision-free ground (a test world with no solids); `wadePm` is the constant 1000 here (story 009 supplies it).*

- [x] **PCM-F1-1 (parametrised).** walk, weight 1000 ‰, no buffs → 5000; walk at the floor (600 ‰) → 3000; sprint at the floor → 4500; swim at the floor → 1200; swim +400 → 2800; sprint +400 → 10 500; sprint P = 20 (800 ‰) with Long Legs ×2 (240) + Postal 300 (540), capped at 400 → 8400; walk at the floor in the deepest wade (`wadePm` 800) → 2400.
- [x] **PCM-F1-2.** `longLegsStacks` 5 and `addPm` −50 clamp to 3 and 0 before use; Long Legs ×1 + Postal (420) equals +400; 5000 mm/s steps 166, 167, 167 mm (30 ticks total exactly 5000); sprint at weight 805 ‰ and buff 1400 ‰ gives 8452 (one division), not 8451 (two).
- [x] **PCM-F2-1.** walk 5000: `dvMax` 1250, full speed on tick 4; sprint at weight 600 (4500): `dvMax` 1125, tick 4; stop from 5000 in 3 ticks (1667) and from 7500 (sprint flag cleared, vRef 5000) in 4 ticks (5000, 3333, 1666, 0); a 10 500 mm/s sprinter whose cap drops to 2000 (swim cap-drop, driven here by setting `vRef`) gets `dvMax` 2625 on the first tick and reaches 2000 on tick 6 (7875, 5906, 4429, 3321, 2490, 2000) *(corrected 2026-10-16: per-tick dvMax per ADR-0013 §2 — game-designer ruling, GDD PCM-F2-1 amended)*; airborne without input: −834 per tick, 0 on tick 6; airborne with input: `dvMax` 625; a full reversal at walk takes 8 ticks; `yawStep` = 4369.
- [x] **PCM-P3-1.** weight 1000 ‰, no buffs: steady speed is walk 5000, sprint 7500; sprint never decays over 3600 ticks (no stamina). (Swim 2000 is asserted in story 009.)
- [x] **PCM-P3-2.** sprint held with no move input → velocity goes to 0; sprint pressed while airborne does not start (it starts on landing); an already-sprinting carrier that becomes airborne keeps sprinting; sprint is ignored while `Loco` is Swimming or Mounted, when `ContextFlags` has Dead, and in Panel context (the carrier walks with a panel open); sprint input never reaches the carrier's speed in those states.
- [x] **PCM-P3-3.** camera yaw 90° (BAM 16384) with forward input → velocity points along camera forward within ±1°; a 180° turn of the move direction completes body yaw in **8 ticks, not 7**; with `Aiming` set, body yaw equals camera yaw that tick; the body yaw never rotates the long way round (shortest signed arc).
- [x] **PCM-FEEL-1.** a walk input present from tick N (carrier at rest) has moved the body 41 ± 1 mm by the end of tick N (first stepped tick) — `dvMax` 1250 mm/s ÷ 30.
- [x] A 1000-tick fuzz with random inputs (seeded PCG) never produces `|v|` above `max(vRef, previous |v|)` and the per-tick horizontal displacement never exceeds 350 mm self-propelled *(clarified 2026-10-16: per axis; the Euclidean step can reach 350.17 mm on a diagonal at 10 500 mm/s because each axis floors its 1/30 mm remainder independently — bound < 351)*.
- [x] `OwnedCarrier.Step` (this story creates the skeleton with the documented phase order; stories 006–009 fill the rest) allocates 0 B over 10 000 calls.

---

## Implementation Notes

*Derived from ADR-0013 §1–§3 and GDD F1/F2:*

- Files in `src/PerformativeMail.Sim/Players/`: `OwnedCarrier.cs` (public static `Step`; internal phase helpers in the GDD P4 order: (1) input + body yaw, (2) F2 velocity, (3) support carry [no-op at M-1a], (4) gravity [story 006], (5) sweep [stories 007/008], (6) separation [no-op, out of M-1a scope], (7) map clamp [story 007], (8) water [story 009]), `CarrierSpeed.cs` (F1), `CarrierAccel.cs` (F2), `CarrierHeading.cs` (camera-relative + yaw).
- F1: `buffPm = 1000 + min(max(BuffAddPm, 0), buffCapPm)`; `speedMmS = Fix.MulDiv(base × weightPm × buffPm × wadePm, 1, 10⁹)` in `long` (max product 1.05 × 10¹³). `weightPm` is `CarrierInput.SpeedPermille` (the caller passes 1000 when mounted). The stat clamps (`longLegsStacks` ≤ 3, `addPm` ≥ 0) are enforced where the caller builds `BuffAddPm` (story 020) **and** defensively here (counted), so F1's 1200 mm/s minimum cannot be broken by a bad StatSheet read.
- F2: `T = hasMoveInput ? ticksAccel : ticksDecel`; `Tair = CeilDiv(T × 1000, airControlPm)`; `dvMax = CeilDiv(max(vRef, |v|), airborne ? Tair : T)`; `|Δ|` via `Fix.ISqrt`. "Own horizontal velocity" excludes support carry.
- The horizontal step: `n = Vx + RemX`, `d = Fix.FloorDiv(n, 30)`, `RemX = n − 30·d` (same for Z). Story 007 replaces the free move with the sub-step sweep but keeps these remainders.
- `Loco` values: Grounded / Airborne / Swimming / Mounted. Sprinting is a `Flags` bit, replicated as its own bit (GDD "Sprinting (input state, not speed)"); sprint requires move input and `Loco == Grounded` to *start*, persists while Airborne, clears on Swimming/Mounted/Panel/Dead or when move input ends.
- Camera-relative input uses `TrigQ15.Sin/Cos` (sim-core story 005) and `Atan2Bam` (Story 003); `vTarget = MulDiv(w, vRef, 32767²)`. A zero input vector keeps `BodyYaw` unchanged (never calls `Atan2Bam(0, 0)` for turning).
- **Yaw round-trip tolerance (added 2026-10-16, from Story 003 AC-4 revision):** `Sin`/`Cos` ignore the low 4 BAM bits (ADR-0006 §6), so any test that builds a move vector from an angle θ and checks the resulting `BodyYaw`/target must allow 25 BAM vs raw θ (or ≤ 10 BAM vs `θ & ~15`); never assert exact equality with θ through a Q15 round trip. See `production/session-logs/decision-log.md` (2026-10-17T01:59:27Z orchestration entry).
- `CarrierContextFlags` (Panel, Dead, Mounted, Build) is owned by the solver input contract (ADR-0013 §7): Story 002 created it, this story appended Dead/Mounted/Build. Story 013 imports it and maps its own `CarrierStatus` onto it; it does not define the bits. *(Ownership wording clarified 2026-10-20, docs only.)*

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 006: gravity, jump, coyote, buffer. Stories 007/008: collision, slopes, sub-step sweep, map clamp. Story 009: wading, swimming, `wadePm`.
- Story 020: building `CarrierInput` from the `InputFrame`, `SpeedPermille` from inventory weight, `BuffAddPm` from the StatSheet.
- Support carry (P7), player push (P8/F6): out of M-1a scope.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/LocomotionTests.cs` (F1/F2/P3/FEEL-1, `[Trait("Id", "PCM-F1-1")]` etc.) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/player/LocomotionTests.cs (LocomotionTests + LocomotionAllocationTests)

---

## Dependencies

- Depends on: Story 001 (`PlayerSettings`, `CarrierState`), Story 002 (`CarrierInput`, `CarrierEnv`, `CarrierScratch`), Story 003 (`Atan2Bam`), `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.*`), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`TrigQ15`)
- Unlocks: Stories 006, 007, 009, 020; grid-inventory `speedPermille` has a consumer

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 9/9 passing — PCM-F1-1, F1-2, F2-1 (as corrected 2026-10-16: stop from 7500 in 4 ticks, cap-drop reaches 2000 on tick 6 — game-designer ruling, GDD amended), P3-1, P3-2, P3-3, FEEL-1, 1000-tick fuzz (per-axis ≤ 350, Euclidean < 351), 0 B over 10 000 Step calls (optimised legs)
**Deviations** (advisory, TD-071): F2 uses ceil-sqrt |Δ| + final max(vRef,|v|) clamp (ADR-0013 §2 says ISqrt — amendment owed); F1 defensive clamps not counted and Fix counters in Step discarded; `CarrierSpeed.MaxAddPm` 100 000 overflow guard; `BuffAddPm` takes Long Legs/Postal limits as parameters; input clamped to unit disc (32767), vTarget clamped to vRef; sprint blocked by Swimming/Mounted (Loco or context flag)/Dead/Panel/no input; exact-180° tie-break turns negative; `CarrierContextFlags` Dead=2/Mounted=4/Build=8 appended (story 013 imports).
**Test Evidence**: Logic — tests/unit/player/LocomotionTests.cs (46 tests); four legs green (net10/net8 × Debug/Release); final re-verification net8 Release 2 966/0 failed.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (incl. godot-csharp .NET secondary), QL-TEST-COVERAGE GAPS (advisory; QL 1–6 + LP 2/3/4/5/7 fixed in-story); rest TD-071.
