# Story 003: World-owned static solids and the 4 m bucket-grid `QueryStaticSolids`

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: none for World Generation. Consumers: `design/gdd/player-character-movement.md` P4 (colliders: houses and static AABBs registered by their owners); `design/gdd/throwing-catching.md` T5/T6/T11 (house AABBs, roofs, 4 m static broad phase).
**Requirement**: `TR-throwing-007` (world half: static solids source for the 4 m static grid), `TR-throwing-005` (world half: AABB solids the sweep tests against), `TR-movement-006` (world-solid colliders the carrier sweeps against)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary); ADR-0015 (consumer: must use `QueryStaticSolids` for world solids); ADR-0013 §6 (consumer contract)
**ADR Decision Summary**: `StaticSolids` is a `ReadOnlySpan<StaticSolid>` in ascending `Index`, each `{ Index, Owner (Prop/Destination/None), AabbMm Box, SolidKind (House, PostOffice, Prop, Roof) }`. At M-1a these are the 10 houses, their roofs and the Post Office. `QueryStaticSolids(region, Span<ushort>)` uses a static 4 m bucket grid built once at world build and returns ascending, deduplicated indices with no allocation; overflow is counted and truncated. Player-built constructs and vehicles are not world solids.
**ADR Version**: ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core layer + Global)**:
- Required: world solids come only from `QueryStaticSolids(region, Span<ushort>)`: static 4 m grid, ascending deduplicated indices, 0-alloc, overflow counted. Player-built constructs and vehicles are not world solids (ADR-0026 §5)
- Required: build-time tables are frozen at `Freeze()`; nothing grows after warm-up (Global)
- Forbidden: a second world grid in any consumer (ADR-0026 Implementation Guidelines: ADR-0015 must use this query); `List<T>` growth, LINQ, boxing or `Dictionary` enumeration in the query path
- Guardrail: query is O(buckets touched + hits); 0 B allocated (ADR-0026 Performance Implications)

---

## Acceptance Criteria

*From ADR-0026 §5 and Validation Criteria, scoped to this story:*

- [x] **AC-1 Solid table.** `AddSolid(owner, box, kind)` assigns `Index` in add order from 0. `StaticSolids` is in ascending `Index`. The builder rejects a box with `Min ≥ Max` on any axis, or a box outside `Bounds`, with `SimBuildException`. More than 65,535 solids also fails the build.
- [x] **AC-2 Query equals brute force.** On a fixture of ~2,000 random solids (the ADR-0026 Risks mitigation) and on a small hand-built set, `QueryStaticSolids` over 10,000 seeded random regions returns exactly the indices of solids whose AABB overlaps the region (closed-interval test, written once and shared with the brute-force oracle), in ascending order, with no duplicates, including solids spanning several 4 m buckets.
- [x] **AC-3 Overflow and allocation.** With a caller span shorter than the hit count, the call writes the lowest-index hits up to the span length, returns that count and increments the overflow counter once per truncating call. 10,000 queries allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0026 §5, Performance Implications and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/World/StaticSolidGrid.cs` (internal, built in `WorldTablesBuilder.Build()`); `AddSolid` on the builder. Bucket size is 4000 mm, a code constant (ADR-0026 §5; TR-throwing-007).
- **Grid layout (suggested, 0-alloc):** a CSR layout, with `int[] bucketStart` of length buckets + 1 and `ushort[] bucketItems` holding each bucket's solid indices **in ascending order** (they are filled in index order). A query collects the touched bucket range (≤ a few buckets for consumer regions) and k-way merges their ascending lists into the caller span, skipping equal heads. Ascending and dedupe then come free, and nothing is sorted at query time. Keep the per-query bucket cursors in a fixed-size `stackalloc` buffer. If a region touches more buckets than that buffer holds, fall back to a stamp-free linear scan of `StaticSolids` (still ascending), and count it.
- **Overflow semantics (story-level decision, ADR-0026 says only "counted and truncated"):** return the number written; count truncating calls in a `worldSolidQueryOverflow` counter, wired through the counter sink pattern of `production/epics/simulation-core/story-007-simcounters-simcheckring.md`. Never throw and never grow.
- **Region inclusivity:** treat AABBs as closed intervals for overlap. Touching faces count as a hit; the precise contact test is the consumer's. Document this on the method.
- `Owner` is `EntityId.None` for houses until story 004 links each lot's house solid to the lot's `Destination` id. Roofs and the Post Office use `None` or a `Prop` id per story 006's authoring.
- A mismatch between this grid and `Bounds` is impossible by construction (boxes inside bounds, AC-1). The grid origin is `Bounds.Min`, and bucket indices come from `Fix.FloorDiv`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: linking a lot's house solid (`LotRecord.HouseSolid`, `Owner = Destination`).
- Story 006: authoring the street's houses, roofs and Post Office boxes.
- Construct and vehicle registries (Building M-1b, Vehicles M1). They are empty at M-1a and are not world solids.
- Throwing's broad-phase |R| ≤ 65,536 mm bound and contact tests (throwing-catching epic, ADR-0015).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/world/StaticSolidQueryTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/world/StaticSolidQueryTests.cs

---

## Dependencies

- Depends on: Story 001 (`AabbMm`, builder), Story 002 (`Bounds`); `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (counter sink); `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md` (seeded PCG for the random-region test, or a test-local seeded generator)
- Unlocks: Story 004, Story 005, Story 006; throwing-catching broad phase; player-character carrier colliders

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 → StaticSolidQueryTests index order / degenerate box per axis / outside Bounds / 65,535 vs 65,536 / grid-too-large / pinned codes; AC-2 → 2 seeds × 2,000 random solids × 10,000 regions vs brute force via the shared `StaticSolidGrid.Overlaps` (pinned by its own closed-interval theory), hand-built set, multi-bucket spanners, touching faces, > 32-bucket fallback; AC-3 → lowest-index prefix + overflow counted once per truncating call (exact fit not counted) + NonParallel `StaticSolidQueryAllocationTests` 10,000 queries 0 B via AllocAssert)
**Deviations**: none blocking. Advisory: counters bound via optional `WorldTablesBuilder.SetCounters` (story 007 must bind); extra `WorldSolidQueryFallback` counter; 2^24 bucket-entry cap (code 27); outside-Bounds checked at Build incl. |y| ≤ 1,000,000; `SolidKind` not validated. Enums appended: SimBuildErrorCode 24–27, CounterId 26–27. Scope also touched Core/CounterId.cs, Core/SimBuildException.cs, tests/unit/sim/Core/SimCountersTests.cs, StaticSolid equality (TD-028 item 2)
**Test Evidence**: Logic: tests/unit/world/StaticSolidQueryTests.cs — full suite 1 891/leg, 0 failed (net10/net8 × Debug/Release; Debug 14 skipped)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; godot-csharp-specialist APPROVE; QL-TEST-COVERAGE ADEQUATE; advisories → TD-051 (incl. throwing |R| region ≈ 289 buckets → fallback path)
**Run result**: N/A — pure Sim library (no scene, no rendering); nothing observable on screen
