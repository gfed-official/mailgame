# Story 006: Ground contact, gravity and jump — F3, coyote time, buffered press and the extra jump

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P5 jump; F3; P4 tick phase 4 gravity; Game Feel "Jump")
**Requirement**: `TR-movement-001`, `TR-movement-002` (int64 widening, one rounding per formula), `TR-movement-005` (terrain contact is a feet-point sample of the bilinear height), `TR-movement-020` (`ExtraJumps` StatSheet stat as `CarrierInput.ExtraJumps`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P5-1, P5-2, P5-3, F3-1.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§2 vertical step F3) — Accepted 2026-10-16
**ADR Decision Summary**: Vertical integration is the trapezoid with a carried remainder: `vy' = max(vy − g/30, −terminalMmS)`, `n = vy + vy' + RemY`, `dy = FloorDiv(n, 60)`, `RemY = n − 60·dy`. `g` is a multiple of 30 (validator PC rule 10). `v0 = ISqrt(2·g·jumpHeightMm)`; the extra jump `v0Extra = MulDiv(v0, extraJumpPm, 1000)` **sets** vertical speed (never adds). Terrain contact is a feet-point sample of `WorldHeight` (the radius applies only against boxes and blocked tiles).
**ADR Version**: ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: pure `OwnedCarrier.Step`, `long` before the first multiply, one rounding per formula, vertical remainder in 1/60 mm (ADR-0013 §2)
- Required: every constant from `PlayerSettings` (`g`, `jumpHeightMm`, `extraJumpPm`, `terminalMmS`, `coyoteTicks`, `jumpBufferTicks`) (ADR-0013)
- Forbidden: `Fx16` for the carrier position; fall damage or landing slow-down (GDD P5 "a landing is a squash, never a slow-down")

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P5-1/2/3, PCM-F3-1), scoped to this story. Flat dry ground (FX-Dry) unless stated; water/swim cases are story 009.*

- [x] **PCM-F3-1.** `v0 = 7589`; apex height **1196 mm at tick 9** (1191 would mean the remainder was dropped); landing on **tick 19** (not 18 or 20); fall speed caps at 20 000 mm/s; the extra jump's `v0Extra = 6830` and rise 969 mm. The jump fires on the tick the press is consumed and that tick integrates with `vy = v0` (tick 1 of the arc).
- [x] **PCM-P5-1.** a jump press 4 ticks before landing fires on the landing tick; 5 ticks before does not; a jump pressed 4 ticks after leaving ground jumps (coyote), 5 ticks after does not (the carrier is placed in `Airborne` with `CoyoteLeft` set by the test fixture; the real ledge walk-off is exercised in story 008); at weight points P = 0 and P = 40 (`SpeedPermille` 1000 and 600) the apex is exactly 1196 mm (weight does not change jump height).
- [x] **PCM-P5-2.** with `ExtraJumps = 1`, a mid-air press at vertical speed −3000 and at −15 000 both set vertical speed to 6830; there is no third jump; the extra jump resets on landing; without the perk (`ExtraJumps = 0`) there is no mid-air jump. (Resets on belt and in water: water reset asserted in story 009; belt is M-1b.)
- [x] **PCM-P5-3.** jump is refused when `ContextFlags` has Panel or Dead, when `Loco` is Swimming or Mounted (no vy change, no buffer arm); a landing at 20 000 mm/s (a 10 m drop) leaves horizontal velocity unchanged and applies no slow-down or stun (there is no health field touched).
- [x] Gravity applies only while `Loco == Airborne`; a grounded carrier on flat ground keeps `Vy = 0`, `RemY = 0` and `Y = TerrainHeightMm(X, Z)` every tick; grounded → airborne at an edge starts `CoyoteLeft = coyoteTicks`.
- [x] A jump while sprinting keeps the Sprinting flag and horizontal velocity (air control per F2) through the arc; a jump buffered during the arc and consumed at landing keeps horizontal momentum.
- [x] 10 000 randomized jump/step sequences (seeded PCG) never leave `Y` below the terrain height (no tunnelling through the floor) and the `RemY` invariant (−59…59) holds.

---

## Implementation Notes

*Derived from ADR-0013 §2 and GDD F3:*

- Files in `src/PerformativeMail.Sim/Players/`: `CarrierJump.cs` (press/buffer/coyote/extra-jump logic, `v0` precompute via `Fix.ISqrt(2·g·jumpHeightMm)` computed once into settings-derived constants at `PlayerSettings` build — a pure function, no statics), `CarrierGravity.cs`.
- Order inside the tick (GDD P4): input/yaw → F2 velocity → [support] → **gravity (F3)** → sweep. A press edge arrives in `CarrierInput.JumpPressed` for the first tick of a catch-up batch only (ADR-0012 / sim-core story 023); the solver arms `JumpBufferLeft = jumpBufferTicks` on the press and decrements it each tick; it fires when `Loco == Grounded` (or `CoyoteLeft > 0` after leaving ground) and the context allows.
- Landing: `Loco = Grounded`, `Vy = 0`, `RemY = 0`, `ExtraJumpUsed` cleared, buffered press fires the same tick.
- `terminalMmS` caps `vy'`; at the cap the trapezoid still carries the remainder (assert the 667 mm worst-case fall displacement feeds story 007's sub-stepper).
- Vertical position source: `WorldHeight.At(env.World, X, Z)` (world-stub story 001). Ground detection in this story is terrain-only; box tops and snap-down arrive in story 008.
- Bunny Hop "extra jump" sets `Vy = MulDiv(v0, extraJumpPm, 1000)` and marks `ExtraJumpUsed`; `CarrierInput.ExtraJumps` (0–1) comes from the `ExtraJumps` stat.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007: sub-step sweep, slopes, snap-down, map clamp. Story 008: box tops, ledges, ceilings. Story 009: swimming / water resets.
- Squash / landing dip animation (story 024) and camera landing dip (Accessibility epic) — presentation only.
- "Jumping onto a belt keeps momentum" (P7, M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/JumpTests.cs` (`[Trait("Id", "PCM-F3-1")]`, P5-1/2/3) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — `tests/unit/player/JumpTests.cs` (49 tests); player namespace 261/261 on net10/net8 × Debug/Release (Debug skips 4 allocation guards)

---

## Dependencies

- Depends on: Story 005 (`OwnedCarrier.Step` skeleton, F2, `Loco`), `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`WorldHeight.At`, `IWorldQuery`)
- Unlocks: Stories 007, 008, 009, 010

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 7/7 passing (all covered by `tests/unit/player/JumpTests.cs`; none deferred)
**Deviations**: ADVISORY only — (a) `v0`/`v0Extra` computed on demand in `CarrierJump` (one ISqrt per launch tick), not cached in `PlayerSettings` (settings reflection test pins one property per balance key); (b) ground detection is terrain-only with `stepUpMm` as snap-down — on slopes grounded Y lags the terrain by one tick and snaps up without a step-up limit (story 007 replaces `CarrierGravity.ResolveGround`); (c) with `ExtraJumps = 1` a press 1–4 ticks above the ground fires the extra jump instead of buffering a ground jump (GDD open question row 27, pinned by test). Logged as TD-072.
**Test Evidence**: Logic: `tests/unit/player/JumpTests.cs` (49 tests; PCM-F3-1, P5-1/2/3, ground invariants, momentum, 10 000-sequence PCG fuzz + replay). Two further `LocomotionTests` fixtures (`F2_AirborneWithInput_DvMax625`, `P3_AlreadySprintingCarrier_KeepsSprintingWhenItBecomesAirborne`) lifted to Y = 100 000 at close (QL gap 1 — with live gravity they landed on tick 1); new `Airborne` assertions watched failing on the old fixtures.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (incl. .NET; 7 advisories), QL-TEST-COVERAGE GAPS → gap 1 fixed in-story, gaps 2–7 → TD-072. TD-071(2) (Fix counters routed to CarrierScratch) resolved here.
