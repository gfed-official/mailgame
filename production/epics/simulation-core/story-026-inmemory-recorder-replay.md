# Story 026: In-memory recorder (ops and samples) and ReplayDriver

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M–L (≈ 1 day; ≈ 500 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R29 determinism tiers, R5 sample recording; Edge Cases "replay header mismatch → no world is built")
**Requirement**: `TR-sim-045` (recorder stores ops + transform samples; in-memory at M-1a, file at M5), `TR-sim-018` (every drained op recorded)
*(Requirement text: `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§7) — primary; ADR-0007 (§2–§3 `OnEnqueued`/`OnDrained` call points) — secondary
**ADR Decision Summary**: `IOpRecorder` (optional, injected; none in perf runs) receives `OnEnqueued(boundaryTick, op)` after `opSeq` is stamped, `OnDrained(applyTick, op, reply)` for every drained op, `OnSample(receivedTick, slot, sample)` at the latch drain and `OnOverflow(tick)`. The M-1a `InMemoryRecorder` is preallocated and grows only outside the tick; its header holds `formatVersion`, the `ContentSet` hash, the SHA-256 of the `SimSettings` canonical encoding, `runSeed` and the initial counters. `ReplayDriver` builds a world from the header and refuses on any mismatch (no world built), re-enqueues each op immediately before the tick numbered its boundary tick, re-latches samples at their `receivedTick`, ticks, asserts each op's recomputed apply tick equals the recorded one, and optionally compares per-tick dump hashes. A recording that saw `OnOverflow` is reported non-reproducible, not failed. The file writer is M5.
**ADR Version**: 2026-10-16 (ADR-0006, ADR-0007)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `IOpRecorder` (`OnEnqueued`, `OnDrained`, `OnSample`, `OnOverflow`) optional and injected; `InMemoryRecorder` grows only outside the tick; header = `formatVersion`, `ContentSet` hash, SHA-256 of the `SimSettings` canonical encoding, `runSeed`, initial counters; `ReplayDriver` refuses on any header mismatch; a recording that saw `OnOverflow` is reported non-reproducible; determinism tests apply one op per tick when they need per-op hashes.
- Forbidden: a test seam in production code for per-op hashing; hashing in the tick path; growth of tick-path collections after warm-up (the recorder is exempt only because the 0-alloc gate runs with no recorder).
- Guardrail: none on the gate (recorder is excluded from SC-R32-1); recording overhead is reported only.

---

## Acceptance Criteria

*Derived from ADR-0006 §7 (TR-sim-045; SC-DET-1 in Story 027 is its main consumer), scoped to this story:*

- [x] **AC-1 Recording.** With an `InMemoryRecorder` bound, a session of enqueues (between ticks and from a phase-0 hook), rejected and accepted drains, and latched samples produces, in order: one `Enqueued` record per accepted enqueue (boundary tick, full op after stamping) and none for refused enqueues; one `Drained` record per drained op (apply tick, op, reply), accepted or rejected; one `Sample` record per latched sample; and an `Overflow` record when the event log overflowed. The header holds `formatVersion = 1`, the 32-byte `ContentSet` hash and settings hash supplied by the `ContentSet`, `runSeed` and the initial counters.
- [x] **AC-2 Replay.** `ReplayDriver.Run(recording)` on the same `ContentSet` rebuilds the world, re-enqueues each op before the tick numbered its boundary tick and re-latches each sample at its `receivedTick`; every recomputed apply tick equals the recorded one; and when the recording carries a per-tick dump-hash track, every replayed hash matches.
- [x] **AC-3 Refusals and non-reproducible.** A recording whose header differs from the replay inputs in `ContentSet` hash, settings hash, `runSeed` or initial counters is refused with a reason naming the field, and no world is built; a recording that contains an `Overflow` record replays to completion and reports `NonReproducible` instead of failing.

---

## Implementation Notes

*Derived from ADR-0006 §7 and ADR-0007 §2–§3:*

- Files in `src/PerformativeMail.Sim/Core/Replay/` (or `Recording/`): `InMemoryRecorder.cs`, `RecordingHeader.cs`, `RecordedOp`/`RecordedDrain`/`RecordedSample` record structs (blittable values — no strings), `ReplayDriver.cs`, `ReplayResult.cs`. `IOpRecorder` was declared in Story 020.
- Wire the remaining call sites: `OnEnqueued` in `SimWorld.EnqueueOp` after stamping (Story 014 left the site); `OnSample` in the prologue (Story 021); `OnOverflow` from the flush when `OverflowInfo.Overflow` is set (Story 016). Use a null-object recorder by default — no per-call null checks on the tick path.
- **Growth outside the tick.** Preallocate generous capacities from a constructor argument; if a record would exceed capacity during a tick, write into a small fixed overflow scratch and grow (copy) at the next between-ticks hook (e.g. a `Flush()` the test or `FrameRunner` calls), or simply size for the session in tests. Never allocate inside `Tick()` when a capacity is respected.
- **Header hashes.** The `ContentSet` hash and the `SimSettings` canonical-encoding hash are produced by content-data (ADR-0010 §6: canonical encoder + hand-written managed SHA-256; manifest I-5 — no `System.Security.Cryptography` in the Sim). The recorder reads them from the `ContentSet` it was built with; with Story 025's shell they are zeros, and tests vary them through test `ContentSet` instances. Do not hash anything in this story.
- `ReplayDriver` is a headless host: it calls `SimComposition.Build` (Story 025) with a `SimBuildArgs` reconstructed from the header (profile supplied by the caller), then loops `Tick()` — no `FrameRunner` (ADR-0002 §5). It enqueues as `System` or the recorded actor using the recorded `Actor` (replay bypasses the adapter; the actor in the recorded op is already stamped).
- The M5 file format will serialise the same records with `CanonicalWriter` — keep records field-addressable, nothing more.

---

## Out of Scope

- Story 027: SC-DET-1 (two worlds, 50 000 ops) — uses this recorder's logs.
- M5 recording file writer and loader.
- Input replay corpus (TR-input-021) and the Manual Delivery race soak — their epics.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/RecorderTests.cs`, `tests/unit/sim/Core/ReplayDriverTests.cs` — must exist and pass

**Status**: [x] Created and passing — RecorderTests + ReplayDriverTests (68 tests, net10 Release re-run at close 68/0)

---

## Dependencies

- Depends on: Story 016 (overflow), Story 019 (dump hashes), Story 020 (`IOpRecorder`, drain), Story 021 (samples), Story 025 (composition for replay builds)
- Unlocks: Story 027, grid-inventory H-DET-1, manual-delivery race soak

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (all automated)
**Traceability**: AC-1 → RecorderTests (Header_Create_*, AcceptedEnqueues_*, RefusedEnqueues_*, EnqueueFromThePhaseZeroHook_*, Drains_*, Samples_*, Overflow_*, MixedSession_*); AC-2 → ReplayDriverTests (RoundTrip_*, Replay_EnqueueFromPhaseZeroHook_*, Tampered_* incl. SampleMismatches); AC-3 → ReplayDriverTests (Refused_When* naming ContentHash/SettingsHash/RunSeed/InitialCounters/FormatVersion, RecordingThatSawOverflow_*, RecordingWithLostRecords_*). All COVERED.
**Files**: src/PerformativeMail.Sim/Core/Replay/{InMemoryRecorder,RecordedEntries,RecordingHeader,RecordList,ReplayDriver,ReplayEnums,ReplayResult}.cs; call sites in Core/IOpRecorder.cs, SimWorld.cs, SimWorld.Drain.cs; tests/unit/sim/Core/RecorderTests.cs, ReplayDriverTests.cs.
**Deviations** (advisory, logged as TD-080): end-to-end "unexpected enqueue" replay variant needs a production seam (forbidden), so the divergence path is tested via the internal Observer only; ADR-0003 §4 erratum (`SimBuildArgs.Recorder`) and ADR-0006 §7 initial-tick header field still to be written; recording overhead not hashed in tick path (compliant).
**Test Evidence**: Logic — tests/unit/sim/Core/RecorderTests.cs, tests/unit/sim/Core/ReplayDriverTests.cs. Four legs (net10/net8 × Debug/Release) 3,797 tests, 0 failed after the fix round; recorder/replay subset re-run at close on net10 Release 68/0.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (blocking LP1 Replay split into ReplaySamples/ReplayOps, LP2 armed observer, LP3 ReplayResult doc — fixed in-story; LP 4–7 advisory → TD-080); QL-TEST-COVERAGE GAPS (QL1 phase-0-hook replay test via PrologueHookRecorder, QL2 SampleMismatches + tamper tests — fixed in-story; QL 3–4 advisory → TD-080).
**Notes for 027**: enqueue between ticks or from a phase-0 hook (in-tick enqueues replay with a differing dump hash for that tick); call `Flush(ticks)` so trailing ops at/after TickCount are covered.
