# Story 015: Focus occlusion — lazy, score-ordered line-of-sight against terrain and static solids

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~250 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (5 of 9).**

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 "not occluded — the chest-to-anchor segment must not cross terrain or a solid box other than the target's own (client-side only; the host has no line-of-sight check; occlusion is tested lazily in score order until one candidate passes)"; "occlusion does not drop a latched target")
**Requirement**: `TR-movement-011` (integer focus; occlusion is part of eligibility); PCM-PERF-1 probe "occlusion tests ≤ candidates in range" (acceptance id, no separate TR-ID — `TR-movement-021` is the advisory budget row)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P11-1 (occlusion rows), PCM-P11-2c (1-tick occlusion), PCM-PERF-1 counter.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 occlusion) — Accepted 2026-10-16; ADR-0013 (§6 `HeightfieldMarch`, `SegmentIntervals.Slab`) secondary
**ADR Decision Summary**: Occlusion is tested lazily in score order until one candidate passes: the chest→anchor segment against `HeightfieldMarch` and static boxes other than the anchor entity's own (`SegmentIntervals.Slab`). It is client-side only. Focus is latched while interacting — occlusion does not drop it.
**ADR Version**: ADR-0014 2026-10-16; ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: world solids come from `IWorldQuery.QueryStaticSolids`; shared geometry from `Sim/Geometry` — no second implementation (ADR-0015, ADR-0026, ADR-0013 §6)
- Guardrail: occlusion tests ≤ candidates in range (counted); 0 B per tick (PCM-PERF-1)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md`, scoped to this story:*

- [ ] **PCM-P11-1 (occlusion rows).** A chest segment that crosses a solid box other than the target's own makes the candidate **not eligible**; the same segment crossing only the target entity's own box (e.g. a mailbox/house solid that `Owner` equals the anchor entity) is eligible; a segment that dips below terrain (a hill between carrier and anchor) via `HeightfieldMarch.FirstContact` is occluded; a segment grazing exactly the box's face at `t = 1000` follows `SegmentIntervals.Slab`'s hit rule (document and test the boundary).
- [ ] **Lazy order.** Candidates are tested in ascending score order and testing stops at the first that passes; with 5 candidates in range whose best is clear, exactly 1 occlusion test runs; if the best two are occluded, 3 run; the counter `focusOcclusionTests` never exceeds `focusCandidates` (PCM-PERF-1).
- [ ] **PCM-P11-2c.** While the interaction state is Holding / Channel / ToggleArmed (the `FocusState.Latched` flag from Story 014), a 1-tick occlusion of the incumbent never changes focus or drops it, and the occlusion test is skipped for it (counter unchanged).
- [ ] **Incumbent occlusion without a latch** drops focus on the tick the incumbent becomes occluded (no dwell on loss) and the next-best unoccluded candidate is focused that tick.
- [ ] **No host line-of-sight.** `CheckInteraction` (Story 019) never calls occlusion code (architecture test: the Players host-side assembly folder has no reference to `FocusOcclusion`).
- [ ] Solid candidates for a segment are gathered with one `QueryStaticSolids` call over the segment's AABB expanded by 0 mm (results ascending, ≤ `maxCarrierCandidates`-sized scratch); allocation 0 B over 10 000 passes; blocked tiles under the segment also occlude (treated as infinite-height AABBs, as for the carrier).

---

## Implementation Notes

*Derived from ADR-0014 §2, ADR-0013 §6:*

- File: `src/PerformativeMail.Sim/Players/Client/FocusOcclusion.cs` — `bool IsOccluded(IWorldQuery w, in Vec3Mm chest, in Vec3Mm anchor, EntityId anchorEntity, Span<ushort> scratch, ref FocusCounters c)`. Terrain: `HeightfieldMarch.FirstContact(w, chest, anchor, out f)` (Story 004); boxes: `SegmentIntervals.Slab` per `StaticSolid` whose `Owner != anchorEntity`.
- Hand the ordered candidate list from Story 014 to `FocusOcclusion` through a small `ReadOnlySpan<Candidate>` (pre-sorted by score, tie-break applied); the dwell logic then operates on the unoccluded winner. Keep the sort allocation-free (insertion sort over ≤ 64, using sim-core `StableSort` with a comparer ending in the id tie-break).
- Player-built constructs (M-1b) will add their AABBs through the same tick-time box-source extension point as Story 008; leave it unused.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: scoring, dwell, context filter. Story 016–018: press / hold / snapshot.
- Host-side range validation (Story 019).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/FocusOcclusionTests.cs` (`[Trait("Id", "PCM-P11-1")]`, `PCM-P11-2c`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 014 (focus pipeline), Story 004 (`SegmentIntervals`, `HeightfieldMarch`), `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`StableSort`)
- Unlocks: Stories 017 (latched focus is exercised end to end), 018
