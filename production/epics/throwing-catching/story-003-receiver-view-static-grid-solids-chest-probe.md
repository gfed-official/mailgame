# Story 003: Receiver gathering — static 4 m grid of catch volumes, `QueryStaticSolids`, map edges, the chest → origin probe and the surface-below query

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-29 — readiness pass (Mail Lead): builder signature takes `ReceiverRanks`; `ICatchVolumeSource` fills story 002's `VolumeReceiver`; `IsBlocked` (not `IsClear`); bounding-sphere AC handed back to story 002 (uncounted, TD-179); tests trimmed to one per AC; planning defaults batched in TD-199 (pending Evan). Earlier: 2026-10-28 — "Map edges" AC (l.41) and its implementation note (l.57) aligned with throwing-catching 002 / PR #55: this story supplies `Bounds` in the view; the side-plane tests and rank-5 contact are `FlightSweep.FirstContact`'s (docs PR `docs/md005-cd019-readiness`).

> **Primitives story (3 of 3).** Turns the world and Mail's destination registry into the `ReceiverView` story 002 sweeps. The host (`Throwing.Step`) and the preview build their views through the same builder; only the `ReceiverMask` differs.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 receiver table and "Mailbox volumes"; T11 solids, roofs, run boundaries; F1 "Host bounds … the segment from the chest to the origin is swept against solids and the heightfield"; F4 "Broad phase"; Edge Cases "If the chest → origin sweep is blocked", "If a flight reaches the map edge")
**Requirement**: `TR-throwing-007` (broad-phase bounds |R| ≤ 65 536 mm; static 4 m grid), `TR-throwing-005` (the receivers the sweep tests), `TR-throwing-003` (preview and host build the same static view), `TR-throwing-006` (deterministic receiver ordering, Implementation Notes)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-E-1 a/b, TC-T6-3 b (surface-below half), TC-F4-1 broad-phase clause.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§4 receivers gathered per segment; §3 chest → origin probe) — Accepted 2026-10-16 (primary); ADR-0026: World Query Contract (`QueryStaticSolids`, `Bounds`, `ClampInsideMap`, `TrySnapWalkable`) and ADR-0017 (catch volumes from the destination registry) secondary — Accepted
**ADR Decision Summary**: Static receivers come from a **static 4 m grid** built at run start (mailbox catch volumes from Mail's registry; belt-lane faces and player-built constructs join at M-1b) plus world solids from `IWorldQuery.QueryStaticSolids` (houses, roofs, Post Office — there is **no second world grid**) and the map-edge vertical planes derived from `Bounds`. Dynamic receivers (catchers, later goons/teammates) are passed in by the caller and rejected by a bounding-sphere test first. Before launch the chest (feet + 1000) → origin segment is swept against solids **and** the heightfield; if blocked the flight launches from the chest point.
**ADR Version**: ADR-0015 2026-10-16; ADR-0026 2026-10-16; ADR-0017 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: world solids come only from `IWorldQuery.QueryStaticSolids`; the preview cache key uses the construct registries' version because `WorldTables` is immutable (ADR-0015, ADR-0026 §5)
- Required: candidate buffers are preallocated, fixed-capacity, overflow counted never grown; `Throwing.Step` allocates 0 B (ADR-0015 Implementation Guidelines)
- Forbidden: a second world-solid grid; a second ballistic/sweep implementation (ADR-0015, ADR-0026)
- Guardrail: ≈ 20 candidates per segment per flight (GDD F4 cost estimate)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-E-1, TC-T6-3 b, TC-F4-1 broad phase) and ADR-0015 §4, scoped to this story:*

- [ ] **Static grid equals brute force.** On the `M1aStreet` fixture (`production/epics/world-stub/story-006-m1a-street-and-fixtures.md`) one parameterised test with boundary rows — a segment inside one cell, one spanning several cells (each volume once), one aligned on a cell edge, one touching a box face (closed overlap), one with no overlap, one at the map corner — shows the 4 m grid's candidate query returns exactly the catch volumes whose AABB overlaps the segment's bounding box, equal to a brute-force overlap scan, in ascending `Key`, no duplicates.
- [ ] **Volumes come from Mail.** Every registry row's catch box equals the builder's `VolumeReceiver.Box`, and its `Key` equals the destination's `EntityId.Value`; the builder reads them through a Throwing port `ICatchVolumeSource` (below), never from content directly.
- [ ] **Solids via the world query only.** World solids are gathered with `IWorldQuery.QueryStaticSolids(region, Span<ushort>)`; a source test over `Sim/Throwing/*.cs` (the `ThrowingRulesTests` pattern, `tests/unit/architecture/ThrowingRulesTests.cs:30`) asserts the only type that stores receiver indices per cell is `ReceiverGrid`, and `ReceiverGrid.Build` takes `ReadOnlySpan<VolumeReceiver>` only (no `StaticSolid` overload). Overflow of the solids span is World's `WorldSolidQueryOverflow` (26); overflow of the catch-volume span is `CounterId.ThrowingCandidateOverflow` (66); neither buffer grows (Mail Lead planning default, pending Evan (TD-199 item 2)).
- [ ] **Map edges.** This story supplies `IWorldQuery.Bounds` in the `ReceiverView` (`MapBounds Bounds`; World registers no side solids) — that is all it owns for the map sides. The four side-plane tests and their rank-5 contact at the boundary crossing live in `FlightSweep.FirstContact` (throwing-catching 002, PR #55, its "Map edge" AC); the 1 m clamp inside the edge (`ClampInsideMap`) is story 011's.
- [ ] **Static-only mask.** `World` is set and `Bounds == world.Bounds`; `Catchers` is empty under `ReceiverMask.StaticOnly` and equals the caller's span under `ReceiverMask.All`. Both paths call the same builder; one builder test, no re-test of the sweep (`FlightSweepTests.cs:456-479` covers `StaticOnly` skipping).
- [ ] **TC-E-1 a (chest → origin probe).** A wall at arm's length (a `House` solid 200 mm in front of the thrower), a ledge, and an origin below sloped terrain each make `IChestOriginProbe.IsBlocked` true: `LaunchMath.Compute` then returns `originFromChest = true` and `Origin = feet + (0, 1000, 0)`; the age-1 segment contacts the solid/terrain at f ≈ 0 (≤ 100 ‰) through `FlightSweep`. A clear path launches from the F1 origin. One parameterised test (wall, ledge, origin below slope, clear) asserting `originFromChest` and `Origin`; one row also checks the age-1 contact (`Ballistic.PositionAt(l, 1)` + `FlightSweep.FirstContact`).
- [ ] **Surface-below.** `SurfaceBelow.At(IWorldQuery w, in Vec3Mm p, out Vec3Mm surface) → ReceiverKind` returns `Water` with `y = 0` when `WaterDepthMm(p.X, p.Z) > 0`, else `Ground` with `y = TerrainHeightMm(p.X, p.Z)` — the surface point directly under `p`, used for TIMEOUT and for solid landings (T6 "drops straight down"); a point over water returns Water at y = 0; a point over a roof (solid top) returns the **ground** below the footprint, not the roof (items are never left on roofs of moving or static solids, T6 GROUND row).
- [ ] **Bounding-sphere reject (inherited, no new test).** The builder passes caller-supplied catcher spheres through unchanged; the bounding-sphere reject and the |R| ≤ 65 536 mm guard are `FlightSweep.FirstContact`'s (story 002, uncounted per TD-179).
- [ ] **Allocation (advisory until M0, erratum E2).** One `AllocAssert.Zero` window over a representative view build on the street fixture reports 0 B.

---

## Implementation Notes

*Derived from ADR-0015 §4 and ADR-0026 §5:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `ReceiverGrid.cs` (4 m cells over `Bounds`; `Build(ReadOnlySpan<VolumeReceiver>)`; cell index via `Fix.FloorDiv`; query writes ascending indices into a caller `Span<ushort>`), `ReceiverViewBuilder.cs` (`Build(in Vec3Mm p0, in Vec3Mm p1, ReceiverMask mask, ReceiverRanks ranks, ReadOnlySpan<SphereReceiver> catchers, ReceiverScratch scratch) → ReceiverView`; the query region is the closed AABB of the segment, `min(p0,p1)`…`max(p0,p1)`; `ReceiverView`'s constructor needs `ReceiverRanks`, `ReceiverView.cs:31`), `ReceiverScratch.cs` (a sealed class holding preallocated arrays so the returned view's spans are heap-backed; capacity 64 candidates per segment, a fixed constant on `ThrowSettings`, not a balance key — Mail Lead planning default, pending Evan (TD-199 item 2)), `ChestOriginProbe.cs` (implements story 001's `IChestOriginProbe` using `FlightSweep` over solids + `HeightfieldMarch`), `SurfaceBelow.cs`.
- **Port `ICatchVolumeSource`** (Throwing-defined): `int Count { get; }`, `EntityId DestinationAt(int ordinal)`, `AabbMm CatchVolumeAt(int ordinal)`; the builder fills story 002's `VolumeReceiver(box, destination.Value)` (`Throwing/VolumeReceiver.cs`); no new receiver struct. Ascending ordinal equals ascending `Key` because World allocates destination ids contiguously in lot order (`Mail/DestinationRegistry.cs:11-13, 20-21`). The Sim adapter reads Mail through three read-only `IMailApi` members that return registry columns (destination count, destination entity at an ordinal, catch box at an ordinal), allocating nothing; nothing new is injected by the composition root and ADR-0003 §4's injectable table is untouched (Mail Lead planning default, pending Evan (TD-199 item 1); Throwing → Mail is a declared edge, `Composition/SystemGraph.cs:53`). The client-side `ICatchVolumeSource` over `DestinationMirror` is story 004's, after `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md` lands; this story ships the Sim adapter only.
- The grid is built once at run start from `ICatchVolumeSource` (the host wiring in `ThrowingSystem` is story 011's, or lands here only after PR #66 merges); there is no "update on change" at M-1a (constructs and belts are M-1b), but keep `Rebuild()` so the M-1b registry versions can drive it. The preview's cache key includes a `StaticVersion` constant `0` at M-1a (ADR-0015 §8, ADR-0026: no `StaticVersion` on `IWorldQuery`).
- Map edges: the view carries `Bounds` only; the four half-space slab tests (inside → outside crossing, contact at the crossing) are `FlightSweep.FirstContact`'s (story 002); the item later drops 1 m inside the edge (`ClampInsideMap`, story 011).
- The chest probe tests the segment from `feet + (0, 1000, 0)` to the F1 origin against solids (AABB intervals) and the heightfield (`HeightfieldMarch`); "blocked" is any hit with `f ≤ 1000`.
- Every new file obeys `ThrowingRulesTests` (`tests/unit/architecture/ThrowingRulesTests.cs:30-105`): no `float`/`double`, `/` or `%`, and no `Fix` helper other than `FloorDiv`/`ISqrt`; cell indexing goes through `Fix.FloorDiv(…, ref sink)`.
- Keep the receiver ordering deterministic: grid hits in ascending destination ordinal, solids in ascending `Index`, catchers in ascending player slot — the winner rule does not depend on it, but replay diffs and the dump do.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: the contact tests and winner. Story 011: terminals, WorldItem placement, `ClampInsideMap`/`TrySnapWalkable` calls. Story 023: producing the catcher spheres.
- Belt lane faces, player-built constructs, vehicle OBBs, goon/teammate cylinders (M-1b / M1 / M0); their spans exist empty.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-29: GAPS, folded into the ACs above.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ReceiverViewTests.cs` (`[Trait("Id", "TC-E-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002; `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`); `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`Bounds`, `ClampInsideMap`); `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (street + `WorldTablesFixture`); `production/epics/mail-destinations/story-002-destination-registry.md` (catch volumes); `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (overflow counter) — all Complete on main; no dependency on open PRs #60/#63/#66/#67.
- Unlocks: Stories 004, 011, 012, 014, 023
