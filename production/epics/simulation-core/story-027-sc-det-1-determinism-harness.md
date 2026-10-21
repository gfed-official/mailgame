# Story 027: SC-DET-1 — same-machine determinism harness (T1)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines, mostly test fixtures)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R29 determinism tiers — T1)
**Requirement**: `TR-sim-033` (T1: identical canonical dump hash per tick given the same inputs and logs), `TR-sim-032` (determinism checklist — exercised end to end)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§5 tiers, §4 dump, §7 recorder)
**ADR Decision Summary**: T1 — two `SimWorld`s with the same `ContentSet` hash, `SimSettings` canonical encoding, `runSeed`, applied-op log and ingested-sample log give identical canonical dumps after every tick. Harnesses that need "after each op" apply one op per tick; there is no test seam in production code. SC-DET-1 runs two worlds in parallel over a 50 000-op log; a third world with another seed diverges. Tests take SHA-256 of the dump.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None. Runs on both the net10.0 and net8.0 legs.

**Control Manifest Rules (Foundation)**:
- Required: T1 = identical dumps after every tick (SC-DET-1, M-1a); determinism tests apply one op per tick when they need per-op hashes; tests and tools take SHA-256 of the dump; two `SimWorld`s must coexist in one process (no statics).
- Forbidden: a test seam in production code; static mutable state (two parallel worlds would share it); `Dictionary` iteration, unstable sorts, `GetHashCode`, clocks in state code.
- Guardrail: none (functional determinism test; mark it slow if it exceeds the default test budget).

---

## Acceptance Criteria

*From GDD SC-DET-1, scoped to this story:*

- [x] **AC-1 SC-DET-1.** Given two `SimWorld`s built in one process — each on its own thread, running in parallel — with the same `ContentSet`, `SimSettings` and `runSeed`, a recorded 50 000-op log (accepted and rejected ops, id allocations, RNG draws, events, at least one join/leave) and its ingested-sample log: when the logs are applied one op per tick and the SHA-256 of the canonical dump is taken after every tick, all 50 000+ per-tick hash pairs match. A third world built with a different `runSeed` produces a different hash on the first tick whose state depends on an RNG draw.

---

## Implementation Notes

*Derived from ADR-0006 §5 and Implementation Guidelines:*

- **Fixture system (test project only).** Add a `DeterminismFixtureSystem` under `tests/PerformativeMail.Sim.Tests/Fixtures/` bound through a per-test `SimProfile`: it registers 2–3 op kinds in a test-only `OpKind` range (use values reserved for tests — coordinate with ADR-0007's block table; e.g. a high block documented as test-only in the fixture, never shipped), validates then mutates a small keyed state table (ascending-id collection), allocates `EntityIds` (test class 0xF0 via `allowTestClasses`), draws from an RNG stream, raises events, and binds an `ISimStateContributor` writing its state in ascending key order. It also reads the sample latch in phase 3 and folds samples into its state so the sample log matters.
- **RNG for the fixture.** `RngNames` is the compile-time v1 registry and must not gain a test name (that would amend ADR-0005's table). Have the fixture obtain a real v1 stream (e.g. `waves`) through an internal test accessor on `RngRegistry` rather than an owner view; the owner-view rule applies to `[SimSystem]` types in the Sim assembly (Story 025/028), and the fixture lives in the test assembly.
- **Log generation.** World R (recording) is driven by a seeded generator in the test (not `System.Random` — use `Pcg32` from Story 008) for 50 000 ticks with one enqueue per tick plus seeded samples; `InMemoryRecorder` (Story 026) captures the op and sample logs. Worlds A and B are fresh builds that replay the logs; each records `SHA-256(dump)` after every tick into a preallocated `byte[50_001][]`-style buffer (allocate outside the loop); compare at the end. World C replays with a different `runSeed`.
- **Parallelism.** Build and tick each world on its own dedicated thread (`new Thread` in the test project — allowed in tests; the Sim never starts threads). Each world's owner thread is the thread that froze it (Story 012).
- Hash helper: Story 019's `DumpHash.Sha256` (test side).
- If runtime exceeds the default test budget, tag `[Trait("Category", "Slow")]` and keep it in the default CI run (it is an M-1a gate).

---

## Out of Scope

- SC-DET-2 (M0): Windows + Linux T2 run of vectors and the PCM-P2-1 script.
- Grid H-DET-1 (TR-inventory-028), input replay corpus, delivery race soak — their epics, on top of this harness.
- SC-R30-1 (M-1b) include/exclude matrix.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/integration/sim/DeterminismT1Tests.cs` (`[Trait("Id", "SC-DET-1")]`) and `tests/PerformativeMail.Sim.Tests/Fixtures/DeterminismFixtureSystem.cs` — must exist and pass on net10.0 and net8.0, Debug and Release

**Status**: [x] Created and passing — DeterminismT1Tests (10 tests, ~1.5 s, no Slow trait) + DeterminismFixtureSystem; four legs green

---

## Dependencies

- Depends on: Story 019 (dump), Story 020 (drain), Story 021 (samples), Story 022 (join/leave in the log), Story 025 (composition, per-test profile), Story 026 (recorder/replay)
- Unlocks: epic Definition of Done (SC-DET-1), grid-inventory H-DET-1

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 1/1 passing (automated)
**Traceability**: AC-1 → tests/integration/sim/DeterminismT1Tests.cs: TwoParallelWorlds_SameLogs_HaveIdenticalDumpHashAfterEveryTick (A=B=R over 50 001 hashes), TwoParallelWorlds_RanOnDistinctThreads_AndAppliedTheSameWork, WorldWithOtherRunSeed_DivergesOnFirstTickWithAnRngDraw (exact index FirstDrawTick+1), WorldWithOtherRunSeed_StaysDivergentAfterTheFirstDraw; non-vacuity RecordedLog_HasAcceptedAndRejectedOps_Joins_Leaves_AndSamples, RecordedLog_ExercisesAllocationsRngDrawsEventsAndFoldedSamples, PerTickHashes_AreSensitiveToEveryTick_NotConstant; tamper OneChangedOp_*, OneDroppedSample_*, OneOpMovedToTheNextTick_Diverges (tightened at close to the exact index). COVERED.
**As shipped (supersedes the Implementation Notes where they differ)**: ids in shipped `EntityClass.WorldItem`, not test class 0xF0 (composition builds `EntityIds` with `allowTestClasses: false`; 0xF0 would need a production seam); reuses shipped `OpKind.TeleportAck` (player verbs Set/Add/Remove/Draw) and `OpKind.FreezePlayer` (System-only Alloc/Bump) — ADR-0007 has no test-reserved range; RNG via composing the fixture as `SystemId.EnemyAiWaves` so the owner view yields the real v1 `waves` stream (no `RngRegistry` accessor, `RngNames` unchanged); own one-op-per-tick replay loop (ReplayDriver has no per-tick hash output) with the 026 recorder capturing world R; per-tick SHA-256 via `SHA256.HashData` in the test project into a preallocated buffer, cross-checked with `DumpHash.Sha256` at the end. No production code changed.
**Deviations**: the four above (advisory, accepted by LP); logged with the gate advisories as TD-083.
**Test Evidence**: Logic — tests/integration/sim/DeterminismT1Tests.cs, tests/PerformativeMail.Sim.Tests/Fixtures/DeterminismFixtureSystem.cs. Implementer four legs: Release net10/net8 3,866/0; Debug net10/net8 3,836/0/30 skipped (none SC-DET-1). At close: class re-run net10 Release 10/0 in-repo, net8 Debug 10/0 on a /tmp copy (in-repo net8 build blocked by another runner's in-flight tests/unit/player/ColliderTests.cs CS1503).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (+.NET; 7 advisories, LP 4 fixed in-story); QL-TEST-COVERAGE ADEQUATE (5 advisories, QL 9 = LP 4 fixed).
