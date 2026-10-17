# Story 001: `IWorldQuery` contract, geometry value types and the height-field kernel (height, gradient, water)

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day; ~350 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: none for World Generation. The contract comes from ADR-0026 and the consumer GDDs: `design/gdd/player-character-movement.md` P4, P6, F7 and fixture table; `design/gdd/throwing-catching.md` F4/T5.
**Requirement**: `TR-movement-005` (terrain from the 2 m bilinear int64 height field), `TR-movement-018` (height-field half: water depth from height), `TR-throwing-005` (world half: the heightfield receiver samples this kernel)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary); ADR-0013: Carrier Kinematic Solver and Fixed-Point Geometry (§6 `Sim/Geometry` kernel); ADR-0006: Numerics, Determinism, Dump and Replay
**ADR Decision Summary**: One read-only `IWorldQuery`, implemented by an immutable `WorldTables` built by `WorldTablesBuilder`, serves terrain, water, solids, bounds and lots to every consumer. Terrain height is bilinear over the containing 2000 mm tile's four corner heights, summed in int64 and divided by `S²` with a **single `Fix.MulDivRH`** (round half away from zero). That kernel is ADR-0013's `WorldHeight.Sample` in `Sim/Geometry`, so there is one implementation and one T2 vector set. Water depth is `max(0, −TerrainHeightMm)` with sea level 0.
**ADR Version**: ADR-0026 2026-10-16; ADR-0013 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None. Godot-free `PerformativeMail.Sim/World` and `Sim/Geometry`. Golden vectors run on both the net10.0 and net8.0 test legs (T2, ADR-0006).

**Control Manifest Rules (Core layer + Global)**:
- Required: `PerformativeMail.Sim/World/` holds `IWorldQuery` and its single implementation `WorldTables`, immutable after `WorldTablesBuilder.Build()` (ADR-0026 §1, §8)
- Required: every consumer reads terrain and water only through `IWorldQuery`; nobody reimplements bilinear sampling or water depth. `TerrainHeightMm` is ADR-0013's `WorldHeight.Sample` over `GetCorners` (one `MulDivRH`) (ADR-0026 §2)
- Required: water authority is `WaterDepthMm = max(0, −TerrainHeightMm)`; walkability is decided on exact gradient numerators; `TerrainGradientPm` is a rounded convenience (ADR-0026 §2–§4)
- Required: integer-only, culture-free, statics-free determinism; int64 before the first multiply; one rounding per formula (ADR-0006 checklist)
- Forbidden: float, `Math.Round`, truncating `/` on a signed height sum; any `IWorldQuery` member beyond ADR-0026's Key Interfaces (§10: members may only be added by the M1 revision)
- Guardrail: height sample is O(1) (4 loads + 1 `MulDivRH`) and allocates nothing (ADR-0026 Performance Implications)

---

## Acceptance Criteria

*From ADR-0026 Validation Criteria and PC GDD F7 / PCM-F7-1 (fixture FX-Beach), scoped to this story:*

- [x] **AC-1 FX-Beach golden values.** With one 2000 mm tile whose corners are h00 = h10 = −1000, h01 = h11 = −200: `TerrainHeightMm` at (fx 500, fz 1000) is exactly −600 and `WaterDepthMm` is 600; at (fx 500, fz 1375) height is −450 and depth 450. `WorldHeight.Sample` called directly on the same `HeightCorners` returns the same values.
- [x] **AC-2 One rounding rule (RH), both signs.** A committed golden-vector table covers tile corners (fx, fz ∈ {0, 1999}), edges, the tile centre, exact-half cases of both signs (e.g. corners (2, 0, 0, 0) at (1000, 1000) → +1 and (−2, 0, 0, 0) → −1, where truncation would give 0) and inputs outside the map (clamped to the map). Every vector passes on the net10.0 and net8.0 legs.
- [x] **AC-3 Gradient.** `WorldHeight.Gradient` returns the un-divided numerators; `TerrainGradientPm` returns permille rounded once with `MulDivRH`. A tile rising 1400 mm over 2000 mm along +X gives `gxPm = 700, gzPm = 0`; 1404 mm gives 702. Exact walkability (gx² + gz² ≤ 700² on numerators in `Int128`) accepts 1400 and rejects 1404.
- [x] **AC-4 Water is derived, never stored.** `WaterDepthMm` equals `max(0, −TerrainHeightMm)` for every golden vector, is 0 on dry land and at height exactly 0, and no water-depth field exists on `WorldTables`.
- [x] **AC-5 Allocation-free.** 10,000 calls each to `TerrainHeightMm`, `GetCorners`, `TerrainGradientPm` and `WaterDepthMm` on a built `WorldTables` allocate 0 B (`GC.GetAllocatedBytesForCurrentThread`).

---

## Implementation Notes

*Derived from ADR-0026 §1–§3, Key Interfaces, Implementation Guidelines and ADR-0013 §6:*

- **Files (create):**
  - `src/PerformativeMail.Sim/Geometry/HeightCorners.cs`: `readonly struct HeightCorners { int H00, H10, H01, H11; }` (int32 mm).
  - `src/PerformativeMail.Sim/Geometry/WorldHeight.cs`: `static int Sample(in HeightCorners h, int fx, int fz)`, `static void Gradient(in HeightCorners h, int fx, int fz, out long gx, out long gz)` and `static int At(IWorldQuery w, int x, int z)` (tile lookup with `Fix.FloorDiv`). ADR-0013 §6 owns these signatures. The player-character and throwing-catching epics reuse them and must not re-create them.
  - `src/PerformativeMail.Sim/Geometry/Vec3Mm.cs`, `AabbMm.cs`, `TileCoord.cs`: plain `readonly struct`s with explicit `IEquatable<T>`, `Equals(object)`, `GetHashCode()`, `==`/`!=` (ADR-0004 rule; control manifest I-6 forbids `record struct` for anything hashed or ordered). **Create these only if they are absent.** `production/epics/grid-inventory/story-013-worlditem-expiry-cap.md` also stores a `Vec3Mm`; whichever story lands first creates the type in `Sim/Geometry`, and the other reuses it.
  - `src/PerformativeMail.Sim/World/IWorldQuery.cs`: the full ADR-0026 Key Interfaces listing (all members declared now, so the contract is frozen once). `TileFlags`, `MapBounds`, `StaticSolid`, `SolidKind`, `LotRecord`, `FaceDir` and `PostOfficeRecord` are declared here too. Members whose data arrives in later stories (tiles and snap: story 002; solids: story 003; lots: story 004; Post Office: story 005) are implemented by those stories.
  - `src/PerformativeMail.Sim/World/WorldTables.cs` and `WorldTablesBuilder.cs`: this story adds `SetCorners(int tilesX, int tilesZ, ReadOnlySpan<int> cornerMm)` (row-major, `(TilesX + 1) × (TilesZ + 1)`) and `Build(...)`. The tables hold `int[] _cornerMm`; `TileSizeMm` = 2000 is a code constant (`WorldConstants.TileSizeMm`). It is not a balance key: ADR-0026 §2 fixes it.
- **Kernel (ADR-0026 §2, exact):** `u = x − tx·S`, `v = z − tz·S`, both in `[0, S)`; `num = h00·(S−u)(S−v) + h10·u(S−v) + h01·(S−u)·v + h11·u·v` in `long`; `h = Fix.MulDivRH(num, 1, S²)`. Widen to `long` before the first multiply. The extreme corner heights times `S²` = 4·10⁶ fit comfortably in int64.
- **Clamp:** `TerrainHeightMm(x, z)` clamps x/z into the map before the tile lookup, and `GetCorners` clamps tile indices to `[0, TilesX − 1]` / `[0, TilesZ − 1]`. On the far map edge (x = MaxX), use the last tile with `u = S − 1`, or document the rule chosen and test it (AC-2's clamp vectors).
- **Gradient (ADR-0013 §6):** numerators `gx = (h10 − h00)(S − fz) + (h11 − h01)·fz`, `gz = (h01 − h00)(S − fx) + (h11 − h10)·fx` (units mm·mm). The slope is `g / S²`; `TerrainGradientPm = MulDivRH(g, 1000, S²)`. Exact walkability compares `(gx² + gz²)·1000² ≤ 700²·S⁴` in `Int128` (ADR-0013 Implementation Guidelines). This story provides the helper (`WorldHeight.IsWalkableSlope(gx, gz, slopeMaxPm)`); `slopeMaxPm` comes from the caller (PC's `players` `SimSettings` record), never a literal in `Sim/Geometry`.
- `Bounds` at this story is derived from the corner grid: `[0, TilesX·S] × [0, TilesZ·S]` mm, unless story 002 adds an explicit bounds setter. Story 002 owns the bounds rules.
- No statics except `ReadOnlySpan` literal tables (ADR-0006). `WorldTables` is a sealed class with readonly fields; nothing mutates after `Build()`.
- Uses `Fix.MulDivRH` and `Fix.FloorDiv` from `production/epics/simulation-core/story-003-numerics-toolkit.md`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: `TileFlags` precompute (Walkable/Water/Blocked), explicit `Bounds`, `ClampInsideMap`, `TrySnapWalkable`.
- Story 003: `StaticSolids` and `QueryStaticSolids`.
- Stories 004–005: `Lots`, `AddressId`, `PostOffice`, `StreetNameAt`.
- Story 006: `M1aStreet` and `WorldTablesFixture` (FX-Dry/Beach/Ramp/Deep/Pad). This story's tests build their tiles directly through `WorldTablesBuilder`.
- player-character epic: swim hysteresis and wade slow (PC F7's `swimming'`/`wadePm`). Only depth is World's.
- `SegmentIntervals`, `HeightfieldMarch` and `Obb` (ADR-0013 §6). These belong to the player-character and throwing-catching epics.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/world/WorldHeightTests.cs` (kernel + golden vectors) and `tests/unit/world/WorldTablesHeightTests.cs` (through `IWorldQuery`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — `tests/unit/world/WorldHeightTests.cs` (22 methods), `tests/unit/world/WorldTablesHeightTests.cs` (38 methods incl. 4 AC-5 alloc tests in `WorldTablesAllocationTests`), shared table `tests/unit/world/HeightGoldenVectors.cs` (46 rows), plus `GeometryValueTypesTests.cs`; green on net10.0/net8.0 × Debug/Release

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (net8.0 leg), `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.MulDivRH`, `FloorDiv`), `production/epics/simulation-core/story-004-determinism-analyzers-sc-arch-2.md` (float ban active on new Sim code)
- Unlocks: Story 002, Story 003, Story 006; player-character carrier stories (terrain, water); throwing-catching heightfield receiver

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (none deferred)
**Deviations**: Advisory only — (a) far-edge rule uses the last tile with u = S (story allowed a documented + tested alternative to u = S−1); (b) corner heights bounded to ±1,000,000 mm in `SetCorners` (int-cast overflow guard, not in ADR-0026); (c) contract types split one file per type instead of all in `IWorldQuery.cs`; (d) `Build()` parameterless until story 004 adds `EntityIds`; (e) builder errors use `SimBuildException` default code; (f) minimal `Primitives/AddressId.cs` created (needed by `LotRecord`; extended by the ADR-0017 owner story). Logged as TD-028 (architecture carry-forwards) and TD-029 (test gaps).
**Test Evidence**: Logic — `tests/unit/world/WorldHeightTests.cs`, `tests/unit/world/WorldTablesHeightTests.cs` (+ `HeightGoldenVectors.cs`, `GeometryValueTypesTests.cs`); suite 1115 tests/leg, 0 failed on net10.0/net8.0 × Debug/Release (Debug: 5 pre-existing TD-008 skips, none in world tests)
**Code Review**: Complete — godot-csharp-specialist (CHANGES MADE: corner bound + 2 tests), LP-CODE-REVIEW APPROVE, QL-TEST-COVERAGE ADEQUATE
