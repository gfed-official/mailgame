# Story 015: Focus occlusion — lazy, score-ordered line-of-sight against terrain and static solids

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~250 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-28 (checklist readiness on main 6757c05: manifest re-check (the 17.1 delta does not touch this story), code names verified, the ordered-candidate refactor, counter id, world port, incumbent test, terrain endpoint and blocked-tile rules stated; TD-175 items 1–4 were pending Evan). 2026-10-28 (docs PR `docs/md005-cd019-readiness`: TD-175 items 1–4 **Approved by Evan 2026-10-28 7:26 AM PT**, in his own words in the Mail Lead chat, card 'Two approvals for sprint 001': "Approve all four TD-175 defaults for player-character 015: the extra incumbent occlusion test, the FocusOcclusionTests counter (55), the terrain-endpoint rule for ground-level anchors, and IWorldQuery on FocusStep"; TD-175 closed, labels relabelled; no AC change)

> **Interaction-framework story (5 of 9).**

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 "not occluded — the chest-to-anchor segment must not cross terrain or a solid box other than the target's own (client-side only; the host has no line-of-sight check; occlusion is tested lazily in score order until one candidate passes)"; "occlusion does not drop a latched target")
**Requirement**: `TR-movement-011` (integer focus; occlusion is part of eligibility); PCM-PERF-1 probe "occlusion tests ≤ candidates in range" (acceptance id, no separate TR-ID — `TR-movement-021` is the advisory budget row)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P11-1 (occlusion rows), PCM-P11-2c (1-tick occlusion), PCM-PERF-1 counter.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 occlusion) — Accepted 2026-10-16; ADR-0013 (§6 `HeightfieldMarch`, `SegmentIntervals.Slab`) secondary
**ADR Decision Summary**: Occlusion is tested lazily in score order until one candidate passes: the chest→anchor segment against `HeightfieldMarch` and static boxes other than the anchor entity's own (`SegmentIntervals.Slab`). It is client-side only. Focus is latched while interacting — occlusion does not drop it.
**ADR Version**: ADR-0014 2026-10-16 (erratum E2 2026-10-23: 0 B checks are advisory until M0); ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: world solids come from `IWorldQuery.QueryStaticSolids`; shared geometry from `Sim/Geometry` — no second implementation (ADR-0015, ADR-0026, ADR-0013 §6)
- Guardrail: occlusion tests ≤ candidates in range (counted); 0 B per tick (PCM-PERF-1; reported, not blocking, until M0 — ADR-0014 erratum E2)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md`, scoped to this story:*

- [x] **PCM-P11-1 (occlusion rows).** A chest segment that crosses a solid box other than the target's own makes the candidate **not eligible**; the same segment crossing only the target entity's own box (e.g. a mailbox/house solid that `Owner` equals the anchor entity) is eligible; a segment that dips below terrain (a hill between carrier and anchor) via `HeightfieldMarch.FirstContact` is occluded; a segment grazing exactly the box's face at `t = 1000` follows `SegmentIntervals.Slab`'s hit rule (`SegmentInterval.IsHit`: `enter ≤ exit ∧ exit ≥ 0 ∧ enter ≤ 1000`, so a touch at `t = 1000` **is** a hit; document and test the boundary). Terrain: a `HeightfieldMarch.FirstContact` hit occludes unless its first contact is at the anchor end (`fPm = 1000`), so an anchor resting on the ground is not occluded by the ground under it (Mail Lead planning default, Approved by Evan 2026-10-28 7:26 AM PT (TD-175 item 3)).
- [x] **Lazy order.** Candidates are tested in ascending score order and testing stops at the first that passes. With no incumbent (or the incumbent is the best), 5 candidates in range and the best clear, exactly 1 occlusion test runs; if the best two are occluded, 3 run. An unlatched incumbent that is not the first passing candidate is also tested that tick (at most one extra test), so AC "Incumbent occlusion without a latch" can drop it (Mail Lead planning default, Approved by Evan 2026-10-28 7:26 AM PT (TD-175 item 1)). Per tick, `FocusStep.Probes.OcclusionTests ≤ Probes.Candidates`; the counter `CounterId.FocusOcclusionTests` (key `focusOcclusionTests`) is a running maximum like `focusCandidates` and never exceeds it (PCM-PERF-1; Mail Lead planning default, Approved by Evan 2026-10-28 7:26 AM PT (TD-175 item 2)). Tests assert the per-tick probe, not the maximum.
- [x] **PCM-P11-2c.** While the interaction state is Holding / Channel / ToggleArmed (the `FocusState.Latched` flag from Story 014), a 1-tick occlusion of the incumbent never changes focus or drops it, and the occlusion test is skipped for it (counter unchanged).
- [x] **Incumbent occlusion without a latch** drops focus on the tick the incumbent becomes occluded (no dwell on loss) and the next-best unoccluded candidate is focused that tick.
- [x] **No host line-of-sight.** Host code never calls occlusion code: an architecture test asserts that no type in `PerformativeMail.Sim.Players` outside the `PerformativeMail.Sim.Players.Client` namespace references `FocusOcclusion` in a signature or method body (`ArchitectureScan`, the `ClientLayerRulesTests` pattern). Story 019's `CheckInteraction` (not yet built) inherits the rule.
- [x] Solid candidates for a segment are gathered with one `QueryStaticSolids` call over the segment's AABB expanded by 0 mm (results ascending, ≤ `maxCarrierCandidates`-sized scratch); allocation 0 B over 10 000 passes (advisory until M0, ADR-0014 erratum E2: reported, never failing); blocked tiles under the segment also occlude: each blocked tile (`IWorldQuery.Tile`) under the segment's x/z AABB is tested with `SegmentIntervals.Slab` as an `AabbMm` over the tile footprint with `Y` from `int.MinValue` to `int.MaxValue` (the carrier's `ColliderBox.Tile` is `long`-bounded and is not reused).

---

## Implementation Notes

*Derived from ADR-0014 §2, ADR-0013 §6:*

- **World port.** `FocusStep` has no world today (`FocusStep.cs:47`). It gains an `IWorldQuery` constructor parameter, a read-only port (the `.Client` rule bans only `SimWorld`); every existing `FocusStep` constructor call gains the argument — `tests/unit/player/FocusStepTests.cs:77` (its rig builds a small `WorldTablesBuilder` world), `tests/unit/player/PressResolutionTests.cs:202` and the `<example>` in `FocusStep.cs:22`'s doc comment; composition wiring stays story 020's (Mail Lead planning default, Approved by Evan 2026-10-28 7:26 AM PT (TD-175 item 4)).
- File: `src/PerformativeMail.Sim/Players/Client/FocusOcclusion.cs` — `bool IsOccluded(IWorldQuery w, in Vec3Mm chest, in Vec3Mm anchor, EntityId anchorEntity, Span<ushort> scratch, ref FocusCounters c)`. Terrain: `HeightfieldMarch.FirstContact(w, chest, anchor, out f)` (Story 004); boxes: `SegmentIntervals.Slab` per `StaticSolid` whose `Owner != anchorEntity`.
- **Ordered candidates.** Story 014's `FocusStep` keeps one running best (`FocusStep.cs:85-131`); there is no `Candidate` type or ordered list yet. Restructure it to collect the scored eligible candidates into a `stackalloc` span of `FocusStep.CandidateCapacity` (128) plus an equal-size scratch span, sort with sim-core `StableSort.Sort(data, scratch, comparer)` (comparer: score ascending, then `AnchorRef.CompareTo`, the existing tie-break), then scan lazily; the dwell logic then operates on the unoccluded winner. Story 014's tests must still pass.
- **Counter.** Append `CounterId.FocusOcclusionTests = 55` (key `focusOcclusionTests`; `SimCountersTests`' length pin 55 → 56); `FocusCounters` gains `int OcclusionTests`, raised by `RaiseInto` like the other two (Mail Lead planning default, Approved by Evan 2026-10-28 7:26 AM PT (TD-175 item 2)).
- Player-built constructs (M-1b) will add their AABBs through the same tick-time box-source extension point as Story 008; leave it unused.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: scoring, dwell, context filter. Story 016–018: press / hold / snapshot.
- Host-side range validation (Story 019).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (Checklist readiness 2026-10-28, lighter process for S stories, Evan 2026-10-23 8:10 AM PT.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/FocusOcclusionTests.cs` (`[Trait("Id", "PCM-P11-1")]`, `PCM-P11-2c`; the t = 1000 boundary and both `Owner` branches each have a test) and the architecture rule in `tests/unit/architecture/` — must exist and pass on net10.0 and net8.0 (Debug and Release)

**Status**: [x] Created 2026-10-28 (dev-story; net10.0 Debug, story classes only; the full net10.0 Debug + net8.0 Release legs run at close)
- `tests/unit/player/FocusOcclusionTests.cs` — `FocusOcclusionTests` (33 cases: 15 at close, plus 18 from the test-gap follow-up, branch `tests/pc015-test-gaps`, 2026-10-28) and `FocusOcclusionAllocationTests` (1, advisory):
  - PCM-P11-1 occlusion rows: `IsOccluded_SolidOnSegment_OccludesUnlessOwnedByTheAnchorEntity` (both `Owner` branches), `IsOccluded_BoxFaceAtTheAnchorEnd_FollowsTheSlabHitRule` (t = 1000 hit, 1 mm short is a miss), `IsOccluded_Terrain_OccludesUnlessTheContactIsAtTheAnchorEnd` (ridge, anchor on the ground at fPm = 1000, anchor 10 mm under it)
  - Lazy order (PCM-PERF-1): `Step_NoIncumbent_TestsInScoreOrderAndStopsAtTheFirstClear` (5 candidates: 1 test, or 3 when the best two are occluded), `Step_IncumbentIsTheBestAndClear_RunsOneTest`
  - Incumbent occlusion without a latch: `Step_UnlatchedIncumbentBecomesOccluded_DropsItAndFocusesTheNextClearThatTick` (incumbent still best, and incumbent behind the winner via the one extra test)
  - PCM-P11-2c: `Step_LatchedIncumbentOccludedForOneTick_KeepsFocusAndSkipsTheTest`
  - Solids, tiles and allocation: `IsOccluded_ClearSegment_QueriesSolidsOnceOverTheSegmentAabbIntoTheScratch`, `IsOccluded_BlockedTileUnderTheSegment_OccludesAtAnyHeight`, `FocusOcclusionAllocationTests.IsOccluded_10000Passes_ReportsAllocatedBytes` (reported, never failing, ADR-0014 E2)
  - Test-gap follow-up (branch `tests/pc015-test-gaps`, Review PR #56 advisories; tests only, each new case seen failing against its mutant): `Step_TwoOcclusionTests_RaisesTheFocusOcclusionTestsCounterToTheProbe` (A2, counter 55 raised to the probe), `IsOccluded_BlockedTileFarFromYZero_OccludesOnlyWhenBlocked` (A3, 4 rows), `IsOccluded_SegmentRunningOffTheMap_IsNotOccludedByOffMapTiles` (A4, TD-182 item 1, 4 rows), `IsOccluded_OccluderPastTheScratch_IsNotTested` (A4, TD-182 item 2, 2 rows), a fPm = 999 row in `IsOccluded_Terrain_OccludesUnlessTheContactIsAtTheAnchorEnd` (A5), `IsOccluded_OffDiagonalBlockedTile_HitsItsClosedFootprintOnly` (A3 mutants: z bounds, edge touch; 5 rows), `QueryStaticSolids_SegmentRegion_WritesOnlyIndicesIntoStaticSolids` (A9 precondition)
- `tests/unit/architecture/HostLineOfSightRulesTests.cs` — No host line-of-sight: `SimAssembly_PlayersTypesOutsideClient_NeverReferenceFocusOcclusion`; seen failing on the real caller `RuleEngine_FocusStep_IsReportedForItsBodyCalls`
- Every new test was seen failing under a targeted mutation of the production code (no extra incumbent test, no fPm = 1000 rule, no `Owner` check, no tile check, no lazy stop, a latched incumbent tested)

---

## Dependencies

- Depends on: Story 014 (focus pipeline), Story 004 (`SegmentIntervals`, `HeightfieldMarch`), `production/epics/world-stub/story-003-static-solids-4m-query.md` (`QueryStaticSolids`), `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md` (`StableSort`)
- Unlocks: Stories 017 (latched focus is exercised end to end), 018
