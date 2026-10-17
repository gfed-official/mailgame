# Story 009: Water — F7 depth, wading, swim hysteresis, swim motion and haul-out

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P6 water; F7; States and Transitions — locomotion state; Edge Cases "if a swimmer is enclosed")
**Requirement**: `TR-movement-018` (water depth from the height field; swim hysteresis 900 / 700 mm), `TR-movement-005` (terrain from the World height field), `TR-movement-001`, `TR-movement-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P6-1, PCM-P6-3, PCM-F7-1, PCM-P3-1 (swim 2000), PCM-F2-1 swim cap-drop, PCM-P5-2 water reset.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§5 water) — Accepted 2026-10-16; ADR-0026 (§3 water is derived from height; `TileFlags.Water` is only a hint) secondary
**ADR Decision Summary**: `depth = max(0, −terrainH)` only when the carrier is over terrain (a carrier on a box or roof above water stays Grounded). `swimming' = swimming ? depth > swimExitMm (700) : depth ≥ swimEnterMm (900)`. `wadePm = swimming ? 1000 : 1000 − MulDiv(maxWadeSlowPm, min(depth, 900), 900)`. Haul-out lifts a swimmer pushing at a walkable top ≤ 1200 mm above the surface after 15 consecutive ticks. Entering deep water from the air zeroes `vy`. Water state is evaluated at step 8 and applies from the next tick; remainders reset on swim entry/exit.
**ADR Version**: ADR-0013 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: water depth from `IWorldQuery.WaterDepthMm` / `TerrainHeightMm` only; `TileFlags.Water` must not be used as the water authority (ADR-0026 §3 / Guidelines)
- Required: constants (`swimEnterMm`, `swimExitMm`, `maxWadeSlowPm`, `haulOutMm`, `haulOutTicks`) from `PlayerSettings` (ADR-0013)
- Required: validator PC rules 2 and 8 (`swimExitMm ≤ swimEnterMm − 100`, `haulOutMm ≥ stepUpMm`) are content-data story 011's — assume they hold (ADR-0010)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-F7-1, PCM-P6-1, PCM-P6-3), scoped to this story. Fixtures FX-Beach, FX-Deep, FX-Dry:*

- [x] **PCM-F7-1 (parametrised).** FX-Beach at fx 500: fz 1000 → terrain height exactly −600, `wadePm` 867, walk speed 4335; fz 1375 → depth 450, `wadePm` 900, walk 4500; FX-Dry → `wadePm` 1000; swimming → `wadePm` 1000. Dry carrier: fz 252 (depth 899) wades (not swimming), fz 250 (depth 900) swims. Swimming carrier: fz 747 (depth 701) stays swimming, fz 750 (depth 700) exits to Grounded.
- [x] **PCM-P6-1.** a carrier standing on a box top above 2000 mm of water (FX-Deep + a static box) stays Grounded with `wadePm` 1000 (no wade slow, no swim); 3600 ticks of swimming on FX-Deep leave the state well-formed (no exception, `Y` constant, remainders in range) — no health field exists on the carrier to change.
- [x] **Swim motion.** While swimming: steady speed is 2000 mm/s (PCM-P3-1 swim); the feet are held at the water plane `Y = 0` (surface-only, no diving — see Implementation Notes); jump and sprint are refused (jump buffer not armed; Sprinting flag cleared); a 10 500 mm/s sprinter entering swim depth is at 2000 mm/s on tick 6 (`dvMax` 2625 per tick; PCM-F2-1 as amended 2026-10-16 — was "tick 4") and the extra-jump flag resets on entering water.
- [x] **Entry from the air.** An airborne carrier entering depth ≥ 900 mm becomes Swimming with `Vy = 0` and `RemY = 0`; horizontal velocity is kept subject to the cap-drop; no damage concept exists.
- [x] **Swim transitions reset remainders** (`RemX`, `RemZ`, `RemY`) on entry and exit; the exit tick places the feet on terrain (or the support top) at `terrainH`.
- [x] **PCM-P6-3 (haul-out).** a swimmer pushing at a walkable ledge whose top is 1200 mm above the water surface is standing on the ledge top on **tick 15, not tick 14**; releasing input at tick 10 resets `HaulOutTicks`; a 1201 mm ledge never lifts; a 1200 mm ledge reached by diagonal input still counts only while the push direction points into the ledge face.
- [x] Water state is evaluated at the end of the tick and applied from the next tick (a carrier crossing the 900 mm line on tick N still uses `wadePm` from tick N−1 for N's speed; test with FX-Beach).
- [x] Walking into water over a 700 ‰ beach slope ends with the carrier swimming in ≤ 2 ticks of crossing depth 900 and never alternates Grounded/Swimming (no flicker) while the depth oscillates ±100 mm around 800 mm (hysteresis).



> **Moved out 2026-10-16 (owner wrap-up):** the game-designer ruling ACs (PCM-P5-4 press resolution / `landProbeMm`, PCM-P5-2, PCM-P5-5 `maxExtraJumps` 0–1, PCM-P4-1d steep-slope slide + `player.slideRunMaxMmS`) were briefly added here and are carried forward as TD-091 / a follow-up movement story — not part of this story.

---

## Implementation Notes

*Derived from ADR-0013 §5 and GDD F7:*

- Files in `src/PerformativeMail.Sim/Players/`: `CarrierWater.cs` (depth sample via `IWorldQuery.TerrainHeightMm`, state transition, `WadePm`), `CarrierHaulOut.cs`. Story 005's `OwnedCarrier.Step` reads `WadePm` from the previous tick's water result (store it in `Flags`/a `byte` wade field; `CarrierState` already has the `Wading` flag — add a `WadePmQ` field only if the dump layout remains stable; story 001 owns the layout, so add the field there via this story's PR and note it in Story 001's dump test).
- "Over terrain" means the support under the feet is terrain, not a box top or roof (use Story 008's `GroundResolve` result). Depth is sampled at the feet point.
- **Feet Y while swimming (GDD silent — decision recorded here):** feet are held at the water plane, `Y = 0`, consistent with GDD P9 "swimming lowers the pivot 0.5 m (PCM-P9-1: pivot 1100) and the camera never goes below the water plane". If a different convention is wanted, change it here and in Story 021's swim pivot; log the change.
- Haul-out: while Swimming, count consecutive ticks where the move direction points into a walkable surface (terrain, box top) whose top is within `haulOutMm` above the water plane and within reach (capsule radius + 100 mm of the face); on `haulOutTicks` lift onto the top, set Grounded, reset the counter. Reset on any tick the condition fails.
- F7 height division uses `WorldHeight.Sample`'s `MulDivRH` (ADR-0026 §2 supersedes the GDD's truncating wording) — the FX-Beach golden values are exact either way.
- Verb restrictions while swimming (Throwing, build, deliver, tool use, drop) are *availability reasons* in Story 013, not solver behaviour. The unstuck request for an enclosed swimmer (GDD Edge Case, open predicate OQ27) is deferred to M0 with `Teleport`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 013: swimming blocks `throw`, `build`, `deliver`, `drop` and gives `reason.dry_land`; `allowWhileSwimming` anchors.
- Story 021: swim camera pivot. Story 024: swim animation state.
- Unstuck / enclosed-swimmer `Teleport` (M0, OQ27); mounted-exit-into-water (Vehicles, M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/WaterTests.cs` (`[Trait("Id", "PCM-F7-1")]`, `PCM-P6-1`, `PCM-P6-3`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/player/WaterTests.cs (PCM-F7-1 / PCM-P6-1 / PCM-P6-3 traits); player namespace 440/0 on four legs

---

## Dependencies

- Depends on: Story 008 (box tops / `GroundResolve`, ledge for haul-out), Story 007, Story 006, Story 005, `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`TerrainHeightMm`, `WaterDepthMm`), `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (FX-Beach / FX-Deep via `WorldTablesFixture`, or direct `WorldTablesBuilder` tiles)
- Unlocks: Story 010; Story 013 (swim availability), throwing-catching (refuses throws while swimming)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 8/8 passing (all automated; none deferred). The swim AC's "tick 4" was corrected to tick 6 to match the amended GDD PCM-F2-1, which the test asserts.
**Deviations**: Advisory only; LP accepted all five. (1) Tick 6 cap-drop, per the GDD. (2) Swim speed includes buffs (2800 at +400), per GDD P3/PCM-F1-1. (3) `ReachSlackMm` 100 is a named const that mirrors the GDD's fixed +100. (4) Depth is 0 on any box top, even a submerged one; a game-designer ruling is owed. (5) `CarrierSweep.Run` plain-moves swimmers, but OwnedCarrier routes them to `SwimMove`. A new state field, `ushort WadeSlowPm`, sits in the old padding (struct still 148 B) and is appended to the dump after the pairs. Follow-ups are in TD-091.
**Scope**: The game-designer ruling ACs (PCM-P5-2/4/5, PCM-P4-1d) were added and then moved out under the owner's wrap-up order. They are carried forward in TD-091.
**Test Evidence**: Logic — tests/unit/player/WaterTests.cs, including the 0 B test. Player namespace 440/0 on all four legs (net8 runs on the net10 runtime). Full suite on net10 Release: 4205/0 (implementer copy). Existing tests adjusted: JumpTests Swimming row moved to WaterTests; 3 LocomotionTests swimmer tests moved to FX-Deep; SweepTerrainTests plateau raised 3000 mm, because its ground was now deep water.
**Code Review**: Complete. LP-CODE-REVIEW gave APPROVE (.NET pass included; 5 low findings → TD-091). QL-TEST-COVERAGE gave GAPS (4 advisory → TD-091); no time to fix them in-story because of the owner wrap-up.
