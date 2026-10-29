# Story 003: Receiver gathering — static 4 m grid of catch volumes, `QueryStaticSolids`, map edges, the chest → origin probe and the surface-below query

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-28 — "Map edges" AC (l.41) and its implementation note (l.57) aligned with throwing-catching 002 / PR #55: this story supplies `Bounds` in the view; the side-plane tests and rank-5 contact are `FlightSweep.FirstContact`'s (docs PR `docs/md005-cd019-readiness`).

> **Primitives story (3 of 3).** Turns the world and Mail's destination registry into the `ReceiverView` story 002 sweeps. The host (`Throwing.Step`) and the preview build their views through the same builder; only the `ReceiverMask` differs.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 receiver table and "Mailbox volumes"; T11 solids, roofs, run boundaries; F1 "Host bounds … the segment from the chest to the origin is swept against solids and the heightfield"; F4 "Broad phase"; Edge Cases "If the chest → origin sweep is blocked", "If a flight reaches the map edge")
**Requirement**: `TR-throwing-007` (broad-phase bounds |R| ≤ 65 536 mm; static 4 m grid), `TR-throwing-005` (the receivers the sweep tests), `TR-throwing-006` (static receiver set is the same for preview and host)
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

- [ ] **Static grid equals brute force.** On the `M1aStreet` fixture (`production/epics/world-stub/story-006-m1a-street-and-fixtures.md`) the 4 m grid's candidate query for 10 000 seeded random segments returns exactly the catch volumes (and only those) whose AABB overlaps the segment's bounding box, in ascending destination order, no duplicates; a segment spanning several 4 m cells returns each volume once.
- [ ] **Volumes come from Mail.** The 10 catch volumes equal Mail's registry rows (`1.0 × 0.8 × 1.0 m` centred 1.0 m above the mailbox base, axis-aligned by `Face`); the builder reads them through a Throwing port `ICatchVolumeSource` (below), never from content directly.
- [ ] **Solids via the world query only.** World solids are gathered with `IWorldQuery.QueryStaticSolids(region, Span<ushort>)`; an architecture test asserts no 4 m (or other) grid over solids exists in `Sim/Throwing/`. Overflow of the candidate buffer is counted in `SimCounters`, never grown.
- [ ] **Map edges.** This story supplies `IWorldQuery.Bounds` in the `ReceiverView` (`MapBounds Bounds`; World registers no side solids) — that is all it owns for the map sides. The four side-plane tests and their rank-5 contact at the boundary crossing live in `FlightSweep.FirstContact` (throwing-catching 002, PR #55, its "Map edge" AC); the 1 m clamp inside the edge (`ClampInsideMap`) is story 011's.
- [ ] **Static-only mask.** With `ReceiverMask.StaticOnly` the view contains mailbox volumes, solids, terrain/water and map edges and **no** dynamic receivers; with `ReceiverMask.All` it additionally includes the caller-supplied catcher spheres. Both paths call the same builder.
- [ ] **TC-E-1 a (chest → origin probe).** A wall at arm's length (a `House` solid 200 mm in front of the thrower), a ledge, and an origin below sloped terrain each make `IChestOriginProbe.IsClear` false: `LaunchMath.Compute` then returns `originFromChest = true` and `Origin = feet + (0, 1000, 0)`; the age-1 segment contacts the solid/terrain at f ≈ 0 (≤ 100 ‰) through `FlightSweep`. A clear path launches from the F1 origin.
- [ ] **Surface-below.** `SurfaceBelow(world, p)` returns Ground or Water and the surface point directly under `p` (terrain height or the water plane), used for TIMEOUT and for solid landings (T6 "drops straight down"); a point over water returns Water at y = 0; a point over a roof (solid top) returns the **ground** below the footprint, not the roof (items are never left on roofs of moving or static solids, T6 GROUND row).
- [ ] **Bounding-sphere reject.** A dynamic receiver farther than `|R| > 65 536 mm` or whose bounding sphere does not touch the segment is excluded before the interval form (counted); a stub 100 km away never reaches a quadratic.
- [ ] 100 000 view builds over the street fixture allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0015 §4 and ADR-0026 §5:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `ReceiverGrid.cs` (4 m cells over `Bounds`; `Build(ReadOnlySpan<VolumeReceiver>)`; cell index via `Fix.FloorDiv`; query writes ascending indices into a caller `Span<ushort>`), `ReceiverViewBuilder.cs` (`Build(in SegmentQuery, ReceiverMask, ReadOnlySpan<SphereReceiver> catchers, ref ReceiverScratch scratch) → ReceiverView`), `ReceiverScratch.cs` (fixed-capacity buffers; capacity from the `throwing` settings, default 64 candidates per segment), `ChestOriginProbe.cs` (implements story 001's `IChestOriginProbe` using `FlightSweep` over solids + `HeightfieldMarch`), `SurfaceBelow.cs`.
- **Port `ICatchVolumeSource`** (Throwing-defined): `ReadOnlySpan<VolumeReceiver> Volumes { get; }` with `VolumeReceiver { EntityId Destination; ushort Ordinal; AabbMm Box; }`. Mail story 002 builds the AABB (`production/epics/mail-destinations/story-002-destination-registry.md` Implementation Notes: "Throwing's landing test consumes it") but its ACs expose only `TryGetDestinationFacts`. **Unowned seam (logged in EPIC.md):** add a read-only `CatchVolumes` accessor on the Mail destination registry/`IMailApi` additively here (do not edit Mail's story files), and bind the client-side equivalent over `DestinationMirror`'s static rows (`production/epics/mail-destinations/story-010-client-mirrors-and-facts.md`) for the preview.
- The grid is built once at run start from `ICatchVolumeSource`; there is no "update on change" at M-1a (constructs and belts are M-1b), but keep `Rebuild()` so the M-1b registry versions can drive it. The preview's cache key includes a `StaticVersion` constant `0` at M-1a (ADR-0015 §8, ADR-0026: no `StaticVersion` on `IWorldQuery`).
- Map edges: the view carries `Bounds` only; the four half-space slab tests (inside → outside crossing, contact at the crossing) are `FlightSweep.FirstContact`'s (story 002); the item later drops 1 m inside the edge (`ClampInsideMap`, story 011).
- The chest probe tests the segment from `feet + (0, 1000, 0)` to the F1 origin against solids (AABB intervals) and the heightfield (`HeightfieldMarch`); "blocked" is any hit with `f ≤ 1000`.
- Keep the receiver ordering deterministic: grid hits in ascending destination ordinal, solids in ascending `Index`, catchers in ascending player slot — the winner rule does not depend on it, but replay diffs and the dump do.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 002: the contact tests and winner. Story 011: terminals, WorldItem placement, `ClampInsideMap`/`TrySnapWalkable` calls. Story 023: producing the catcher spheres.
- Belt lane faces, player-built constructs, vehicle OBBs, goon/teammate cylinders (M-1b / M1 / M0); their spans exist empty.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ReceiverViewTests.cs` (`[Trait("Id", "TC-E-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002; `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`); `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`Bounds`, `ClampInsideMap`); `production/epics/world-stub/story-006-m1a-street-and-fixtures.md` (street + `WorldTablesFixture`); `production/epics/mail-destinations/story-002-destination-registry.md` (catch volumes); `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (overflow counter)
- Unlocks: Stories 004, 011, 012, 014, 023
