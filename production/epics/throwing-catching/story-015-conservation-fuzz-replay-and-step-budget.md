# Story 015: Exactly-one-terminal conservation fuzz (8 players, 18 000 ticks) and canonical dump / replay

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~0.75 day; ~300 lines of harness + tests, almost no production code)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23.** The `Throwing.Step` budget half — TC-P-2 timing report, the 0 B gate, the `tc-p-2.md` evidence file and the SC-FIX-WORST throw case (erratum E2: per-tick 0 B checks are advisory until M0) — moved verbatim to `story-026-deferred-throwing-step-budget-zero-alloc.md` (Deferred to M0, with its fixture owner simulation-core 029). Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all". What stays here is the exactly-one-terminal conservation fuzz, the T6 row coverage, the dump/replay participation, the tick-order case and the capacity burst. This story no longer depends on simulation-core 029.

> **Integration / determinism story (2 of 2).** The soak that proves the epic's second promise: *a stack in flight is never lost or duplicated*. It also puts Throwing under the same determinism harness as every other system.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 "Every flight ends in exactly one terminal state"; T11 run end; Edge Cases "Lifecycle"; AC TC-X-1)
**Requirement**: `TR-throwing-013` (exactly one terminal state per flight; conservation through flush/disconnect), `TR-throwing-006`, `TR-throwing-002` (`TR-throwing-008`, the `Throwing.Step` budget, is story 026's — M0)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-X-1 [FUZZ], SC-DET-1 / SC-X-1 participation. TC-P-2 [PERF] is story 026's.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§5 conservation, §9 dump, Validation Criteria "Every terminal row of T6 has a test …") — Accepted 2026-10-16 (primary); ADR-0006 (§4 dump, replay) and ADR-0009 (Flight ledger, Grid R19) secondary — Accepted
**ADR Decision Summary**: Units in flight are in the Flight ledger column, so Grid's conservation check covers them; Throwing's own tick-end record asserts `launched == resolved + live` and no double resolution. Throwing's `ISimStateContributor` is part of the canonical dump, so the determinism harness and replay cover it. The phase-4 budget (TC-P-2, SC-FIX-WORST, the 0 B gate) is story 026's at M0 (E2).
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: bind an `ISimStateContributor` for any state; the replay/dump harness covers every system (ADR-0006 §4)
- Forbidden: gating on wall-clock milliseconds before the reference machine exists (nothing here is timed; story 026)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-X-1) and ADR-0015 Validation Criteria, scoped to the **M-1a receivers** (ground, water, solids, mailboxes, timeout, flush). Catcher, bonk, goon and belt events join the fuzz when their stories land (the harness has a feature mask):*

- [ ] **TC-X-1 [FUZZ] (M-1a mask).** With 8 players, the recorded seeds and 18 000 ticks of random throws (all classes, stacks of 1–20, non-mail items), landings on terrain, water, solids and mailboxes (accept, misdeliver, reject), player disconnects mid-flight and a run-end flush at a random tick: every flight has **exactly one** terminal (`FlightResolved` count per `FlightId` = 1), the multiset of `mailId`s and item units is conserved (Grid's UnitLedger identity `Σ live = spawned + granted − consumed − destroyed` and the per-state counters), and the FLIGHT count is 0 after the flush.
- [ ] **Throwing's record never fires.** The tick-end check (story 008) records zero faults across the run; the `flightCapHits` counter is 0 at the generator's rate (the bots throw at ≤ 6/s per player — the rate the M0 leaky bucket, story 028, will enforce; at M-1a the generator paces itself); the candidate-overflow counter (story 003) is 0.
- [ ] **Every T6 row, at least once.** The run asserts each of GROUND (terrain), GROUND (solid), WATER, TIMEOUT, MAILBOX_ACCEPT, MAILBOX_MISDELIVER (partial and whole) and MAILBOX_REJECT (closed, locked, non-mail) occurred ≥ 20 times and that the WorldItem count never exceeds `worldItemCap` (eviction exercised).
- [ ] **Dump and replay (SC-DET-1 participation).** A throw scenario (3 players, scripted aim/release over 600 ticks, covering every M-1a terminal) is added to the determinism harness: running it twice from the recorded ops yields identical per-tick state hashes, and the in-memory recorder replay (`production/epics/simulation-core/story-026-inmemory-recorder-replay.md`) reproduces the run bit for bit including the Throwing dump section; changing one stored flight field changes the hash (the harness's "inject a difference" control).
- [ ] **Tick-order integration.** A hand deliver, a thrown landing and a placed feeder stub into one mailbox in one tick resolve hand → landing → feeder (phase order 0, 4, 5), asserted on the event log — the Throwing case for `production/epics/simulation-core/story-030-sc-x-1-cross-system-batch-order.md`'s ordering.
- [ ] **Capacity.** A burst test fills 160 live flights (injected), asserts the 161st release replies `flight_cap`, and that all 160 resolve with one terminal each.


---

## Implementation Notes

*Derived from ADR-0015 §5 / Validation and the Grid/Sim Core harness stories:*

- Files: `tests/unit/throwing/ThrowConservationFuzzTests.cs`, `tests/integration/throwing/ThrowingDeterminismTests.cs`; harness helpers shared with `production/epics/grid-inventory/story-018-conservation-fuzz-determinism.md` (reuse its action generator and ledger assertions; add throw actions to a copy, do not edit it) and `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`.
- The fuzz uses the real Throwing, Inventory, Mail (stub wallet), Players (fixture life states) and the `M1aStreet` world; players are scripted bots issuing ops, not Godot. 8 slots use the join/leave handlers (`story-022`).
- Seeds: reuse `tests/fuzz/throwing/seeds.txt` from story 014 with a second column for the scenario (`conservation`).
- No timing and no allocation assertion here: the fuzz asserts conservation only. The `Stopwatch`/`GC.GetAllocatedBytesForCurrentThread` harness and the SC-FIX-WORST throw case are story 026's (M0).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 026 (Deferred, M0): the `Throwing.Step` budget (TC-P-2 timing, 0 B gate, `tc-p-2.md`, SC-FIX-WORST). Story 014: preview = host equality. Stories 011–013: the behaviours under test.
- Catcher, bonk, goon and belt fuzz participation (M0 / M-1b; story 023/024 add catchers). Bandwidth tests TC-N-3 b (M0).
- Writing `perf-budget.md` (control manifest E-2).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/unit/throwing/ThrowConservationFuzzTests.cs`, `tests/integration/throwing/ThrowingDeterminismTests.cs` (`[Trait("Id", "TC-X-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 008, 011, 012, 013, 014; `production/epics/grid-inventory/story-018-conservation-fuzz-determinism.md`; `production/epics/simulation-core/story-026-inmemory-recorder-replay.md`; `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`; `production/epics/simulation-core/story-030-sc-x-1-cross-system-batch-order.md`
- Unlocks: Story 026 (Deferred, M0); Stories 023, 024 (their fuzz participation; Deferred to M0)
