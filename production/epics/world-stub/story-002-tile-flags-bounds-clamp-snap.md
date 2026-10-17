# Story 002: Tile flags (Walkable precompute, Water hint, Blocked), map bounds, `ClampInsideMap`, `TrySnapWalkable`

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: none for World Generation. Consumers: `design/gdd/player-character-movement.md` P4 (walkability 700‰; step 7 clamps 1 m inside the map rectangle), P6 (swim enter 900 mm); `design/gdd/throwing-catching.md` T6/F8 (`snapWalkable(…, 1000)`, `clampInsideMap(D, 1000)`), T11 (map sides as vertical solids).
**Requirement**: `TR-movement-006` (slope 700‰ as walkability), `TR-movement-018` (swim-enter depth 900 mm as the walkability water cut-off)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*
**Untraced (no TR-ID in the registry):** map clamp (PC P4 step 7, Throwing F8 `clampInsideMap`), walkable snap (Throwing T6/F8 `snapWalkable`) and map bounds |coord| ≤ 1,000,000 mm (Throwing CV 17). ADR-0026 §4 decides them, but `tr-registry.yaml` has no entry. Flagged for the next `/architecture-review`; this story does not add registry entries.

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary); ADR-0013 (§6 exact slope test); ADR-0006
**ADR Decision Summary**: `TileFlags` are precomputed at build. `Walkable` is true iff the tile is not `Blocked`, its maximum corner-gradient magnitude is ≤ 700‰ (exact numerator test), and `WaterDepthMm` at the tile centre is < 900 mm. `Water` (any corner < 0) is a broad-phase hint only, never the water authority. `Bounds` is an axis-aligned map rectangle with |coord| ≤ 1,000,000 mm. `ClampInsideMap(p, inset)` clamps x/z into `[Min + inset, Max − inset]`. `TrySnapWalkable(p, radius)` returns the nearest `Walkable` tile centre within the radius by squared mm distance, ties to lower `tz` then lower `tx`, with y = `TerrainHeightMm` there, or `false`.
**ADR Version**: ADR-0026 2026-10-16; ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core layer + Global)**:
- Required: water authority is `WaterDepthMm`; `TileFlags.Water` is only a broad-phase hint. Walkability is decided on exact gradient numerators (ADR-0026 §2–§4)
- Required: `WorldTables` immutable after `Build()` (ADR-0026 §1)
- Required: iteration in ascending order; integer-only (ADR-0006)
- Forbidden: deciding walkability from the rounded `TerrainGradientPm`; reading `Tile(...).Water` as "is in water"
- Guardrail: snap is O(tiles in radius), and a 1 m radius covers ≤ 4 tiles; 0 B allocated (ADR-0026 Performance Implications)

---

## Acceptance Criteria

*From ADR-0026 §3–§4 and the consumer GDD rows above, scoped to this story:*

- [x] **AC-1 Walkable precompute.** A tile rising 1400 mm over 2000 mm (700‰) is `Walkable`; 1404 mm (702‰) is not. A `Blocked` tile is never `Walkable`. A tile whose centre depth is 899 mm is `Walkable`; at 900 mm it is not. `Water` is set iff any corner < 0. A point on a `Water`-flagged tile whose `TerrainHeightMm ≥ 0` has `WaterDepthMm == 0`, so the hint is not the authority.
- [x] **AC-2 Bounds and clamp.** The builder rejects bounds with any |coord| > 1,000,000 mm, or bounds that do not contain the corner grid, with `SimBuildException`. `ClampInsideMap(p, 1000)` maps x/z into `[Min + 1000, Max − 1000]`, leaves y unchanged and returns in-range points unchanged.
- [x] **AC-3 Snap.** `TrySnapWalkable(p, 1000, out s)` returns the nearest `Walkable` tile centre within 1000 mm (squared-mm distance, ≤ comparison). An exact tie picks the lower `tz`, then the lower `tx`. `s.Y == TerrainHeightMm(s.X, s.Z)`. The call returns `false` (and `s` = default) when no `Walkable` centre is within the radius. 10,000 calls allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0026 §3–§4 and Implementation Guidelines:*

- **Builder additions:** `SetBounds(MapBounds)` (validated at `Build()`), `MarkBlocked(int tx, int tz)` and `MarkRoad`/`MarkLot` (display and spawn hints). At `Build()` compute `byte[] _flags` (one per tile, row-major) after all corners and marks are in.
- **Walkable:** for each tile, evaluate `WorldHeight.Gradient` at the four tile corners `(fx, fz) ∈ {0, S}²` (for the bilinear patch the corner gradients bound the magnitude). The tile is walkable iff every corner passes `WorldHeight.IsWalkableSlope(gx, gz, 700)` (story 001's exact `Int128` test), it is not `Blocked`, and `WaterDepthMm(centre) < 900`. The 700 and 900 are PC's `slopeMaxPm` and `swimEnterMm`. At M-1a, `WorldTablesBuilder.Build(...)` takes them as parameters from the `players` `SimSettings` record (`production/epics/content-data/story-004-balance-registry-simsettings.md`) and never hard-codes them. If that record does not exist yet, take them as constructor parameters and let the composition story (007) pass them.
- **Risk (ADR-0026 Risks):** tile-granular `Walkable` can disagree with the carrier's per-contact slope at tile edges. `Walkable` is used **only** for snap and spawn validation; the carrier uses the gradient directly. Write that on the `TileFlags.Walkable` XML doc.
- **Snap:** the radius covers tile centres in `[p − r, p + r]`. Iterate `tz` ascending, then `tx` ascending, and keep the first strict improvement. That order gives the lower-`tz`, lower-`tx` tie-break without extra comparisons. Distances are `long` squared mm.
- **Map sides as solids (Throwing rank 5)** are read by Throwing from `Bounds`. World registers nothing for them in `StaticSolids`.
- `TileFlags` values are exactly `None 0, Blocked 1, Water 2, Walkable 4, Road 8, Lot 16` (ADR-0026 Key Interfaces); the dump (story 007) writes them as bytes.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: static solids. Snap does **not** reject tile centres inside a house at M-1a, because ADR-0026 §4 defines snap on `Walkable` tiles only. The pad check against solids is story 005's.
- Story 006: authoring the street's blocked, road and lot tiles.
- player-character / throwing-catching: what callers do with a `false` snap (PC P4 depenetration, Throwing T6).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/world/WorldTilesTests.cs` and `tests/unit/world/WorldSnapClampTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/world/WorldTilesTests.cs, tests/unit/world/WorldSnapClampTests.cs

---

## Dependencies

- Depends on: Story 001 (kernel, builder, exact slope test); `production/epics/simulation-core/story-017-builder-validation-codes.md` (`SimBuildException` + codes)
- Unlocks: Story 005 (pad validation), Story 006; throwing-catching T6/F8 landing snap; player-character map clamp

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 → WorldTilesTests `Ac1_Walkable_Rise1400Over2000…`/`Rise1404…`, `SlopeThatRoundsTo700_IsStillNotWalkable`, `SteepAtExactlyOneCorner` Theory ×4, `CentreDepth899/900`, Blocked/Water-hint tests, `Ac1_Flags_NonSquareMap_UseTilesXAsTheRowStride`; AC-2 → WorldTilesTests bounds ±1,000,000 / grid-containment codes + WorldSnapClampTests clamp tests; AC-3 → WorldSnapClampTests snap tie-break/radius/false-default tests + NonParallel `WorldSnapClampAllocationTests` 10,000 calls 0 B via AllocAssert, Release legs)
**Deviations**: none blocking. Advisory: out-of-range `Tile` = `Blocked`; `ClampInsideMap` negative inset → 0, empty inset range → floor midpoint; bounds default to the corner grid; `Build(slopeMaxPm, swimEnterMm)` takes PC limits as parameters (no SimSettings keys yet; story 007 passes them) and parameterless `Build()` is kept (all-unwalkable) for older tests; scope also touched Core/SimBuildException.cs (SimBuildErrorCode 13–20 World*, TD-028 item 3), MapBounds equality (TD-028 item 2), IWorldQuery docs
**Test Evidence**: Logic: tests/unit/world/WorldTilesTests.cs, tests/unit/world/WorldSnapClampTests.cs (world filter 406/406; full suite 1739/leg, 1 unrelated failure in another runner's in-flight ContentAssertCode edit)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; godot-csharp-specialist APPROVE; QL-TEST-COVERAGE GAPS (gaps 1–2 fixed in-story: non-square stride + one-corner tests); remaining advisories → TD-046; TD-028 progress noted
**Run result**: N/A — pure Sim library (no scene, no rendering); nothing observable on screen
