# Story 008: Static colliders — cylinder vs AABB, slides, step-up, ledges, ceilings and depenetration

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: L (~1 day; ~600 lines prod + tests — the largest solver story; split ceilings/depenetration into a follow-up only if it overruns)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P4 colliders, step-up, snap, depenetration precedence; Edge Cases "if one tick's displacement exceeds 300 mm", "if a construct is placed over a living capsule")
**Requirement**: `TR-movement-006` (sub-steps ≤ 300 mm, ≤ 3 slides, step-up / snap 400 mm), `TR-movement-001`, `TR-movement-004` (tick-time collider candidates; no render-time data)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P4-1b, P4-1c, P4-2a, P4-2c (static half), P4-2d.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§4 collision model and sweep, depenetration precedence) — Accepted 2026-10-16; ADR-0026 (§5 `QueryStaticSolids`, §4 `TileFlags.Blocked`) secondary
**ADR Decision Summary**: The carrier is a flat-capped vertical cylinder (radius 300, height 1400). Colliders are blocked tiles (AABB, infinite height), static AABBs from `IWorldQuery.QueryStaticSolids` (houses, roofs, Post Office at M-1a; player constructs are M-1b+) — gathered once per tick from the swept AABB in ascending collider id. Cylinder-vs-AABB: y-ranges overlap and the xz circle–rectangle distance² `< r²`, MTV along the closest-point normal (least-penetration axis when the centre is inside). Each sub-step: move horizontally, resolve overlaps with ≤ 3 slide iterations (push out by MTV, remove the velocity component into the normal), step-up (box top ≤ 400 mm above the feet with 1400 mm headroom) while grounded, vertical move and ground resolution (max of terrain and walkable box tops under the footprint), snap-down (drop ≤ 400 stays Grounded; 401 goes Airborne). A ceiling hit zeroes upward `vy`. A static box appearing over a capsule lifts it onto the top if ≤ 3000 mm up, else moves it to the nearest walkable point.
**ADR Version**: ADR-0013 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: ≤ 3 slides per sub-step and ≤ 4 sub-steps per tick, counted; candidate buffers fixed-capacity in `CarrierScratch`, overflow counted as `carrierCandidateOverflow`, never grown (ADR-0013)
- Required: world solids come from `IWorldQuery.QueryStaticSolids` — never a second world grid (ADR-0026, ADR-0015)
- Forbidden: Godot physics bodies / `CharacterBody3D` / Jolt for player collision (ADR-0013)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md`, scoped to this story (static boxes and blocked tiles; vehicle OBBs and player capsules are out of M-1a scope):*

- [x] **PCM-P4-1b.** box ledges of 400 mm and 401 mm: a grounded carrier walking into the 400 mm ledge climbs it without jumping (feet end on the box top); the 401 mm ledge blocks it (it slides along the face). Headroom: a 400 mm step under a low roof (clearance < 1400 mm) is refused.
- [x] **PCM-P4-1c.** walking off a ledge: a 400 mm drop stays Grounded at that sub-step (snap-down), a 401 mm drop goes Airborne with `CoyoteLeft = coyoteTicks` (a jump pressed 4 ticks later jumps, 5 does not — closes story 006's fixture-only coyote case).
- [x] **PCM-P4-2a.** a 650 mm single-tick displacement toward a 100 mm-thick wall 300 mm ahead, and a 667 mm fall onto a 50 mm-thick slab, **never tunnel**; slide iterations stay ≤ 3 per sub-step. Property test: 10 000 random starts/velocities (seeded PCG) ≤ 1200 mm per tick against random thin boxes never end with the cylinder overlapping a box (> 0 mm penetration after the tick) and never on the far side of a box it started clear of.
- [x] **PCM-P4-2c (static).** a static box placed over a standing carrier (appears in the next tick's candidate set) leaves the carrier **on top of it** if the top is ≤ 3000 mm above the feet, x/z unchanged; if the top is higher, the carrier moves to the nearest walkable point (`TrySnapWalkable`); `Depenetrations` is counted.
- [x] **PCM-P4-2d.** a ceiling hit (jumping under a 1.5 m-high roof slab) zeroes upward `vy` the tick of contact and the carrier falls; `Y` never ends above the slab bottom minus the capsule height.
- [x] **Blocked tiles** (`TileFlags.Blocked`) behave as infinite-height AABBs of the tile footprint: a carrier cannot enter one and slides along its edge.
- [x] **Corners.** Two boxes meeting at 90° and at an acute angle: a carrier pushing diagonally into the corner for 120 ticks does not jitter (|ΔX|,|ΔZ| ≤ 1 mm per tick after settling) and is not frozen (turning the input 90° frees it within 3 ticks).
- [x] **Candidate gather.** Candidates are gathered once per tick, in ascending `StaticSolid.Index`, for the tick's swept AABB (≤ 1.2 m + radius); more than `maxCarrierCandidates` candidates increments `CandidateOverflow` and the lowest ids are kept (deterministic).
- [x] Walkable box tops: standing on a house roof/box top is Grounded with `Y` = the top; the support height is the max of terrain and walkable tops under the footprint.

---

## Implementation Notes

*Derived from ADR-0013 §4:*

- Files in `src/PerformativeMail.Sim/Players/`: `CarrierColliders.cs` (candidate gather into `CarrierScratch`), `CylinderVsBox.cs` (overlap + MTV, all `long`), `CarrierSlide.cs`, `CarrierStepUp.cs`, `CarrierDepenetrate.cs`. They extend story 007's `CarrierSweep` per-sub-step body; keep the shared `GroundResolve` routine (terrain + walkable box tops).
- Candidate source at M-1a: `IWorldQuery.QueryStaticSolids(in AabbMm region, Span<ushort> indices)` (world-stub story 003) plus blocked tiles from `IWorldQuery.Tile(tx, tz)` for the tiles under the swept AABB. Player-built constructs, fences, nodes come from their owners' registries through a tick-time port in M-1b — leave a documented extension point (`ICarrierBoxSource`, empty at M-1a) rather than a hard-coded list.
- MTV when the centre is inside the box: least-penetration axis; ties broken by axis order x, z, y (deterministic). All comparisons squared, in `long`.
- Step-up requires `grounded ∧ top − feet ≤ stepUpMm ∧ top + 1400 clear of any other candidate's bottom`. Step-up never triggers while Airborne.
- Slide: push out by the MTV, then remove the velocity component into the normal (`v −= n·(v·n)` using `MulDiv` on a Q15 normal or exact axis-aligned normals; for corner-contact normals use the closest-point direction normalised with `ISqrt`). Axis-aligned faces use exact axis normals (no rounding).
- Depenetration precedence is *static box → lift up if ≤ 3000 else nearest walkable*. The vehicle-OBB push-out branch (nearest face, whole overlap, never onto a moving roof) is specified by ADR-0013 but unreachable until Vehicles (M1); leave a stub branch guarded by the empty `ISupportTransformSource`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007: terrain slopes, sub-step splitting, map-edge clamp. Story 009: water and haul-out onto a ledge (consumes this story's ground resolve).
- Vehicle OBB collision and push-out, belts, roofs (P7): M-1b / M1. Player-vs-player separation F6 and ghosting (P8): M0 (no story this epic).
- Door/mailbox geometry check (GDD OQ23): measured on the world-stub street in story 020's greybox pass.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/ColliderTests.cs` (`[Trait("Id", "PCM-P4-1b")]` … `PCM-P4-2d`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/player/ColliderTests.cs (PCM-P4-1b/1c/2a/2c/2d traits, ≈ 55 tests); player namespace 411/0 on four legs

---

## Dependencies

- Depends on: Story 007 (sweep loop, `GroundResolve`), Story 002 (`CarrierScratch`), `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`), `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`Tile`, `TrySnapWalkable`)
- Unlocks: Stories 009 (haul-out ledge, box-over-water), 010

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 9/9 passing (all automated; none deferred)
**Deviations**: Advisory only, logged as TD-084. LP ruled that all 10 self-reported deviations are acceptable. Items (1), (2), (7) and (8) need an ADR-0013 §4 as-implemented note:
- (1) each sub-step's horizontal move is replayed in increments of Euclidean length ≤ r (a 424 mm diagonal sub-step could tunnel), and the move is undone when the slide budget is spent while still overlapping;
- (2) the push-out side for an inside-centre MTV comes from the pre-move centre;
- (7) LiftMaxMm 3000, SnapSearchTiles 2 and MaxIncrements 64 are code constants with no balance key;
- (8) the walkable-point snap probes from just outside the nearest face, with a raw-terrain fallback.
The remaining deviations were acceptable without a note: (3) a fixed gather region ±(1200 + r); (4) off-map tiles left to the map clamp; (6) CandidateOverflow counted per gather or per dropped tile; (9) a re-gather after depenetration; (10) the "acute corner" modelled as a wall face plus a box edge.
**Test Evidence**: Logic — tests/unit/player/ColliderTests.cs. Player namespace 411/0 on all four legs (net8 runs on the net10 runtime via DOTNET_ROLL_FORWARD=Major). Full-suite failures are foreign to this story: in-flight Content RuleFlagSet / ContentEnumTests.
**Code Review**: Complete. LP-CODE-REVIEW gave APPROVE (including the .NET pass; 7 advisories, of which LP 2 re-gather-after-lift and LP 5 index guard were fixed in-story). QL-TEST-COVERAGE gave GAPS; all 5 were fixed in-story: depenetration fallbacks, MTV tie order, buffered landing jump on a box top after the extra jump (closes TD-072(6)), Fix counters asserted (closes TD-078(9) for the corner path), and far-side checks on the extra box.
**Delivery**: two sequential implementers (Part A collider core and TD-078(1)–(3); Part B step-up, ceilings, depenetration and corners) because of the 20-turn limit.
