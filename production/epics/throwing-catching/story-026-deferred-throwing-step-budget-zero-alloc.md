# Story 026: `Throwing.Step` budget — TC-P-2 timing report, 0 B gate and the SC-FIX-WORST throw case

> **Epic**: Throwing & Catching
> **Status**: Deferred (Evan 2026-10-23 8:10 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; split from story 015)
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S (~0.5 day; ~150 lines of perf harness + the evidence file, no production code)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23 by Evan, not deleted.** Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E2 (per-tick / per-frame 0 B checks are advisory, reported not blocking, until M0)**; its fixture owner `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md` is Deferred to M0 with it. Story 015 keeps the exactly-one-terminal conservation fuzz and the dump/replay participation; the ACs below moved verbatim from it. Re-run `/story-readiness` when M0 planning starts (write `docs/architecture/perf-budget.md` first — control manifest E-2 — before any ms number becomes a gate).

## Context

**GDD**: `design/gdd/throwing-catching.md` (AC TC-P-2; Performance section)
**Requirement**: `TR-throwing-008` (`Throwing.Step` phase 4, 0.15 ms p99, 0 alloc, 16 flights + 8 catchers)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance id: TC-P-2 [PERF] (advisory timing; 0 B blocking from M0).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (Performance Implications; Validation Criteria "the tick-end check never fires on SC-FIX-WORST. TC-P-2: ≤ 0.15 ms p99 (advisory until M0), 0 B per tick (blocking)") — Accepted 2026-10-16 (primary); ADR-0006 (§8 0-alloc gate) secondary — Accepted
**ADR Decision Summary**: Phase 4 has a 0.15 ms p99 / 0.5 ms max slice with 16 flights + 8 catchers; perf is budgeted at 16 and advisory at 60 (capacity is 160). The timing is advisory until a reference machine exists (Q-ARCH-10); per E2 the 0-byte gate is blocking from M0, not M-1a.
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Timing numbers are recorded on the dev machine and are advisory (no reference machine yet). Timing uses `Stopwatch` in the **test project** only; the Sim reads no clock.

**Control Manifest Rules (Feature layer)**:
- Required: `perf-budget.md` does not exist (control manifest E-2) — use TR-sim-037 plus the ADR slices; write it before any *gating* perf story
- Forbidden: gating on wall-clock milliseconds before the reference machine exists
- Guardrail: `Throwing.Step` ≤ 0.15 ms p99 / 0.5 ms max at 16 flights (advisory), ≤ 0.5 ms p99 at 60 flights (advisory), 0 B (BLOCKING from M0)

---

## Acceptance Criteria

*Moved verbatim from story 015:*

- [ ] **TC-P-2 (M-1a form) (from story 015).** 16 live flights over 10 000 ticks of the street fixture (no catchers at M-1a): `Throwing.Step` is recorded at p99 and max; ≤ 0.15 ms p99 and ≤ 0.5 ms max are **ADVISORY**; with 60 flights the p99 ≤ 0.5 ms (ADVISORY); the step allocates **0 B** (`GC.GetAllocatedBytesForCurrentThread` over the run after 300 warm-up ticks) — **BLOCKING**. The 8-armed-catcher fixture is added by story 023/024.
- [ ] **Report (from story 015).** The timing results are written to `production/qa/evidence/throwing/tc-p-2.md` (numbers, machine, SDK, TFM), marked ADVISORY.
- [ ] **SC-FIX-WORST throw case (from story 015 Implementation Notes).** `SC-FIX-WORST` (the worst-case fixture named in ADR-0015 Validation) is the simulation-core fixture from `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md`; add a Throwing worst case (16–60 flights) to the same harness shape rather than a new mechanism, and assert the tick-end conservation check (story 008) never fires on it.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/unit/throwing/ThrowingStepPerfTests.cs` (`[Trait("Id", "TC-P-2")]`) — must exist and pass; timing evidence in `production/qa/evidence/throwing/tc-p-2.md`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 015 (the fuzz harness, street fixture and scripted bots this story times); `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md` (SC-FIX-WORST, the 0 B probe shape — Deferred to M0)
- Unlocks: —
