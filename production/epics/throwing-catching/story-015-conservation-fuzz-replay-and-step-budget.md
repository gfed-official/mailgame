# Story 015: Exactly-one-terminal conservation fuzz (8 players, 18 000 ticks), canonical dump / replay and the `Throwing.Step` budget

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines of harness + tests, almost no production code)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Integration / determinism story (2 of 2).** The soak that proves the epic's second promise: *a stack in flight is never lost or duplicated*. It also puts Throwing under the same determinism harness as every other system and records its phase-4 cost.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 "Every flight ends in exactly one terminal state"; T11 run end; Edge Cases "Lifecycle"; AC TC-X-1, TC-P-2; Performance section)
**Requirement**: `TR-throwing-013` (exactly one terminal state per flight; conservation through flush/disconnect), `TR-throwing-008` (`Throwing.Step` phase 4, 0.15 ms p99, 0 alloc, 16 flights + 8 catchers), `TR-throwing-006`, `TR-throwing-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-X-1 [FUZZ], TC-P-2 [PERF] (advisory timing, blocking 0 B), SC-DET-1 / SC-X-1 participation.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§5 conservation, §9 dump, Performance Implications, Validation Criteria "Every terminal row of T6 has a test … the tick-end check never fires on SC-FIX-WORST. TC-P-2: ≤ 0.15 ms p99 (advisory until M0), 0 B per tick (blocking)") — Accepted 2026-10-16 (primary); ADR-0006 (§4 dump, §8 0-alloc gate, replay) and ADR-0009 (Flight ledger, Grid R19) secondary — Accepted
**ADR Decision Summary**: Units in flight are in the Flight ledger column, so Grid's conservation check covers them; Throwing's own tick-end record asserts `launched == resolved + live` and no double resolution. Throwing's `ISimStateContributor` is part of the canonical dump, so the determinism harness and replay cover it. Phase 4 has a 0.15 ms p99 / 0.5 ms max slice with 16 flights + 8 catchers; perf is budgeted at 16 and advisory at 60 (capacity is 160). The 0-byte gate is blocking from M-1a; the timing is advisory until a reference machine exists (Q-ARCH-10).
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Timing numbers are recorded on the dev machine and are advisory (no reference machine yet).

**Control Manifest Rules (Feature layer)**:
- Required: bind an `ISimStateContributor` for any state; the replay/dump harness covers every system; allocate 0 B in the tick path (ADR-0006 §4/§8)
- Required: `perf-budget.md` does not exist (control manifest E-2) — use TR-sim-037 plus the ADR slices; write it before any *gating* perf story
- Forbidden: gating on wall-clock milliseconds before the reference machine exists
- Guardrail: `Throwing.Step` ≤ 0.15 ms p99 / 0.5 ms max at 16 flights (advisory), ≤ 0.5 ms p99 at 60 flights (advisory), 0 B (BLOCKING)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-X-1, TC-P-2) and ADR-0015 Validation Criteria, scoped to the **M-1a receivers** (ground, water, solids, mailboxes, timeout, flush). Catcher, bonk, goon and belt events join the fuzz when their stories land (the harness has a feature mask):*

- [ ] **TC-X-1 [FUZZ] (M-1a mask).** With 8 players, the recorded seeds and 18 000 ticks of random throws (all classes, stacks of 1–20, non-mail items), landings on terrain, water, solids and mailboxes (accept, misdeliver, reject), player disconnects mid-flight and a run-end flush at a random tick: every flight has **exactly one** terminal (`FlightResolved` count per `FlightId` = 1), the multiset of `mailId`s and item units is conserved (Grid's UnitLedger identity `Σ live = spawned + granted − consumed − destroyed` and the per-state counters), and the FLIGHT count is 0 after the flush.
- [ ] **Throwing's record never fires.** The tick-end check (story 008) records zero faults across the run; the `flightCapHits` counter is 0 at the bucket-limited rates; the candidate-overflow counter (story 003) is 0.
- [ ] **Every T6 row, at least once.** The run asserts each of GROUND (terrain), GROUND (solid), WATER, TIMEOUT, MAILBOX_ACCEPT, MAILBOX_MISDELIVER (partial and whole) and MAILBOX_REJECT (closed, locked, non-mail) occurred ≥ 20 times and that the WorldItem count never exceeds `worldItemCap` (eviction exercised).
- [ ] **Dump and replay (SC-DET-1 participation).** A throw scenario (3 players, scripted aim/release over 600 ticks, covering every M-1a terminal) is added to the determinism harness: running it twice from the recorded ops yields identical per-tick state hashes, and the in-memory recorder replay (`production/epics/simulation-core/story-026-inmemory-recorder-replay.md`) reproduces the run bit for bit including the Throwing dump section; changing one stored flight field changes the hash (the harness's "inject a difference" control).
- [ ] **Tick-order integration.** A hand deliver, a thrown landing and a placed feeder stub into one mailbox in one tick resolve hand → landing → feeder (phase order 0, 4, 5), asserted on the event log — the Throwing case for `production/epics/simulation-core/story-030-sc-x-1-cross-system-batch-order.md`'s ordering.
- [ ] **TC-P-2 (M-1a form).** 16 live flights over 10 000 ticks of the street fixture (no catchers at M-1a): `Throwing.Step` is recorded at p99 and max; ≤ 0.15 ms p99 and ≤ 0.5 ms max are **ADVISORY**; with 60 flights the p99 ≤ 0.5 ms (ADVISORY); the step allocates **0 B** (`GC.GetAllocatedBytesForCurrentThread` over the run after 300 warm-up ticks) — **BLOCKING**. The 8-armed-catcher fixture is added by story 023/024.
- [ ] **Capacity.** A burst test fills 160 live flights (injected), asserts the 161st release replies `flight_cap`, and that all 160 resolve with one terminal each.
- [ ] **Report.** The timing results are written to `production/qa/evidence/throwing/tc-p-2.md` (numbers, machine, SDK, TFM), marked ADVISORY.

---

## Implementation Notes

*Derived from ADR-0015 Performance / Validation and the Grid/Sim Core harness stories:*

- Files: `tests/unit/throwing/ThrowConservationFuzzTests.cs`, `tests/integration/throwing/ThrowingDeterminismTests.cs`, `tests/unit/throwing/ThrowingStepPerfTests.cs`; harness helpers shared with `production/epics/grid-inventory/story-018-conservation-fuzz-determinism.md` (reuse its action generator and ledger assertions; add throw actions to a copy, do not edit it) and `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`.
- The fuzz uses the real Throwing, Inventory, Mail (stub wallet), Players (fixture life states) and the `M1aStreet` world; players are scripted bots issuing ops, not Godot. 8 slots use the join/leave handlers (`story-022`).
- Seeds: reuse `tests/fuzz/throwing/seeds.txt` from story 014 with a second column for the scenario (`conservation`).
- `SC-FIX-WORST` (the worst-case fixture named in ADR-0015 Validation) is the simulation-core fixture from `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md`; add a Throwing worst case (16–60 flights) to the same harness shape rather than a new mechanism.
- Timing uses `Stopwatch` in the **test project** only; the Sim reads no clock.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: preview = host equality and T2 goldens. Stories 011–013: the behaviours under test.
- Catcher, bonk, goon and belt fuzz participation (M0 / M-1b; story 023/024 add catchers). Bandwidth tests TC-N-3 b (M0).
- Writing `perf-budget.md` (control manifest E-2).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/unit/throwing/ThrowConservationFuzzTests.cs`, `tests/integration/throwing/ThrowingDeterminismTests.cs`, `tests/unit/throwing/ThrowingStepPerfTests.cs` (`[Trait("Id", "TC-X-1")]`, `TC-P-2`) — must exist and pass; timing evidence in `production/qa/evidence/throwing/tc-p-2.md`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 008, 011, 012, 013, 014; `production/epics/grid-inventory/story-018-conservation-fuzz-determinism.md`; `production/epics/simulation-core/story-026-inmemory-recorder-replay.md`; `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`; `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md`; `production/epics/simulation-core/story-030-sc-x-1-cross-system-batch-order.md`
- Unlocks: Stories 023, 024 (their fuzz participation)
