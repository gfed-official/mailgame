# Epic: Simulation Core

> **Layer**: Foundation
> **GDD**: design/gdd/simulation-core.md (In Review, Provisional)
> **Architecture Module**: Sim.Primitives · Sim.Core · Sim.Composition · Sim.Hosting · Sim.ClientLayer shell (`src/PerformativeMail.Sim/`) — architecture.md v1.1 §Module Ownership / Foundation
> **Milestone slice**: M-1a (GDD §Interactions "Milestone slice": real files for every contract; frozen interfaces for the rest)
> **Engine Risk**: LOW for this epic's scope (Godot-free). The HIGH halves of ADR-0001/0002 (Godot csproj, `GameLoop`) are in `godot-host-shell`.
> **Est. size**: L — 16–20 stories (`story_granularity: fine`)
> **Implementation priority**: 1 of 9 (first; everything else builds on it)
> **Status**: Ready
> **Stories**: 30 stories — 28 Complete, 2 Ready (counts refreshed 2026-10-23 from the story headers, m1a-replan; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

This epic builds the Godot-free heart of the game. Every gameplay system depends on it: the fixed 30 Hz tick, the 14-phase pipeline, the op FIFO, the per-tick event log, the id allocators, the seeded RNG streams, integer money and fixed-point numerics, and the canonical state dump with its recorder. All of it lives in the plain `net8.0` `PerformativeMail.Sim` assembly. The GDD's milestone ruling (lead-programmer and technical-director) is that M-1a writes the **real** files for every contract that seven GDDs consume. Interfaces whose behaviour has no consumer yet are frozen now and implemented by their first consumer: the probe, coalescing, two-tier admission, `Serial*`/`WireNarrow`, solo pause, `ISnapshotSource`, the 10 ms guard and the blocking 0-alloc gate.

**In scope at M-1a** (GDD "Real at M-1a" list; 25 ACs tagged `[M-1a]`):
- **Primitives:**
  - `Cents` (±2^62), `Fx16`, BAM angles plus the Q15 table, and the `Mm`/`Pm` helpers;
  - the rounding toolkit: `RH`, `MulPermille`, `MulDiv`, `MulDivRH`, `CeilDiv`/`FloorDiv`, `isqrt`;
  - PCG32 XSH-RR, SplitMix64/`Mix64`, `Fnv1a64`;
  - canonical little-endian writers.
- **Core:**
  - the tick counter and the lifecycle (Constructed → Ready → Running → Stopped/Faulted);
  - the `PhaseId` table (14) and the `ITickEndCheck` hook;
  - the 4096-slot op ring with `systemOpReserve` 64 and a drain of ≤64 per tick in `opSeq` order;
  - `OpReply` records and the reason namespace;
  - the per-tick `EventLog` (64 B entries, 48 B payload ceiling, ≤8 pull cursors, a single-tier overflow flag and masks) and `IEventSink.Deliver` once in phase 13;
  - `EntityId`/`MailId`/`EntryId`/`PlayerSlot`/`FlightId`, the `RngNames` registry and owner-only stream handles;
  - `SimCounters`, `SimCheckRing` and `SimAssert`.
- **Composition:** `SimWorldBuilder` (`SimBuildException` codes), `SimComposition.Build(profile)`, constructor injection from the closed injectable list, and the reflection test.
- **Hosting:** `TickDriver.Advance(now)` (an integer accumulator, cap 4, drop-not-bank, Δ clamp 1 s; frequency fixed at construction) and `FrameRunner.RunFrame()` (ClientLayer step before each `Tick()`, edges at i = 0, transport flush after the last tick). There is no M0 guard.
- **ClientLayer shell:** `InputLatch`, the per-tick client step order, the `IHostClock` consumer seam and the published-prompt slot. Feature epics fill the `CarrierStep`, focus and `Previews` phases.
- **Determinism:**
  - the `ISimStateContributor` canonical dump (SHA-256 in tests only);
  - the in-memory recorder of drained ops **and** ingested transform samples, with replay;
  - SC-DET-1 (two worlds, 50 000 ops, per-tick hash equality);
  - the analyzer bans (`float`/`double`/`decimal`, `System.Random`, `Guid.NewGuid`, `GetHashCode`, `DateTime`, `Stopwatch`) and the architecture tests (no `Godot*` reference, no `using Godot`, no static mutable fields, no clock in `Hosting/`);
  - the `net8.0` CI test leg for the ADR-0005 golden vectors and ADR-0006 T2 (ADR-0001 §7).

**Out of scope**:
- the Godot `GameLoop` node → `godot-host-shell`;
- the loopback `HostAdapter`/`LoopbackTransport` (the Godot-free adapter core in `Client.Core/Net`) → `network-adapter-loopback`;
- the 10 ms wall-clock guard, join snapshots and the CI perf gate → M0;
- the per-phase budget slices → `perf-budget.md`, not yet written.

**Why these ADRs govern it.**
- ADR-0002 makes the driver a pure function of injected timestamps, so the cadence is unit-testable without an engine.
- ADR-0003 fixes "what runs before what" and bans statics and service locators, so determinism is structural.
- ADR-0004, 0005 and 0006 make every id, draw and number bit-reproducible across Windows and Linux.
- ADR-0007 and 0008 define the only two crossing points for state change and observation (the op ring in, the event log out).
- ADR-0011 fixes the seam so the Sim never knows about peers.
- ADR-0001 fixes where the code lives and how Debug, Release and Export builds map to `SIM_ASSERT`.

**Risks.**
- (1) The GDD is Provisional and mid-review. A re-review could shift R-numbered details that ADR-0003/0007/0008 encode (architecture.md Q-ARCH-12). Mitigation: stories cite ADR sections, and `/propagate-design-change` runs after the re-review.
- (2) The seed document `simulation-core-adr-inputs.md` is unreviewed and has known stale points (architecture.md §ADR Audit). Stories must take values from the GDD and the ADRs, never from the seed.
- (3) The 0-alloc tick gate is advisory until M0. Measuring it from the first story avoids a late retrofit.
- (4) Three TRs (tick budget, phase slices, CI perf gate) trace to `perf-budget.md`, which does not exist. They stay advisory at M-1a.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework | Godot-free net8.0 Sim, xUnit v3 tests on net10.0 plus a net8.0 leg, `SIM_ASSERT`/`Optimize` mapping, CI guards (`using Godot` grep, architecture test) | HIGH (Godot half, spike-verified) / LOW (Sim half, this epic) |
| ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim | Integer accumulator `TickDriver.Advance(now)`, cap 4, drop-not-bank, Δ clamp 1 s, solo pause as a driver state; `FrameRunner` pump; main thread only | HIGH (`GameLoop` half) / LOW (driver, this epic) |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | Exactly 14 `PhaseId`s, `ITickEndCheck` between 12 and 13, `SimWorldBuilder` + `SimComposition.Build`, constructor injection, sync `I…Api` only in the caller's phase | LOW |
| ADR-0004: Identity and Id Spaces | `EntityId` = 8-bit class + 24-bit counter; separate `MailId`/`EntryId` counters; `PlayerSlot` u8; wrap-safe `FlightId` u16; `DefIndex` never persisted | LOW |
| ADR-0005: RNG Streams and Derivation | PCG32 XSH-RR; `Derive = Mix64(Mix64(seed ^ Fnv1a64(name)) ^ index)`; single-draw `NextInt`; compile-time `RngNames`; golden vectors | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | Integer-only units, one rounding toolkit, analyzer-enforced checklist, LE canonical dump, in-memory recorder, 0-alloc tick | LOW (Sim) / MEDIUM (export runtime) |
| ADR-0007: Op Queue, Drain and Replies | 64 B blittable `Op`, ring 4096 + reserve 64, drain ≤64/tick by `opSeq`, validate-then-mutate, reply after events, u32 `reqId` slot (dedupe in the adapter) | LOW (Sim) |
| ADR-0008: Event Log, Replication Classes and Event Delivery | Per-tick `EventLog` 4096 × 64 B (48 B payload), pull cursors ≤8, `Deliver` once in phase 13, four replication classes, overflow masks | LOW (Sim) |
| ADR-0011: Network Adapter Boundary | The Sim knows no peers; crossing points `EnqueueOp`, sample ring, `IEventSink`, `ISnapshotSource`, `IHostClock` | LOW (seam) |

## GDD Requirements

Source: architecture.md v1.1 Appendix A (the TR baseline, now registered in `docs/architecture/tr-registry.yaml`). **42 / 45 traced** to an Accepted ADR.

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-sim-001 | Whole fixed ticks of 1/30 s; TickRateHz=30 code constant; no variable step | ADR-0002 ✅ | M-1a |
| TR-sim-002 | Tick is uint32 from 0; differences in int64; first Tick() after Ready is tick 0 | ADR-0002 ✅ | M-1a |
| TR-sim-003 | TickDriver is a Godot-free integer accumulator in Hosting/; takes timestamps, never reads a clock | ADR-0002 ✅ | M-1a |
| TR-sim-004 | Catch-up cap maxCatchUpTicksPerFrame=4 (safe 3–8); excess dropped, never banked | ADR-0002 ✅ | M-1a |
| TR-sim-005 | M0 wall-clock guard catchUpGuardMs=10 re-read after each tick; ≥1 tick always runs | ADR-0002 ✅ | M0 (interface frozen at M-1a) |
| TR-sim-006 | F2 accumulator invariant; Δ clamped to 1 s; freq 10^3..10^12; backwards clock counted | ADR-0002 ✅ | M-1a |
| TR-sim-007 | Sim on Godot main thread; owner thread captured at Freeze(); Debug asserts | ADR-0002 ✅ | M-1a |
| TR-sim-008 | Transport callbacks only enqueue; nothing applied inside a callback | ADR-0002 ✅ | M-1a |
| TR-sim-009 | Pause is a driver state; solo pause → Advance returns 0; SceneTree.Paused never used | ADR-0002 ✅ | M-1a |
| TR-sim-010 | Exactly 14 PhaseIds 0–13 in fixed order; ITickEndCheck between 12 and 13 | ADR-0003 ✅ | M-1a |
| TR-sim-011 | Unbound phase is a no-op; same-phase binders in composition order; new phases need R3 amendment | ADR-0003 ✅ | M-1a |
| TR-sim-012 | SimWorldBuilder binds phases/ops/events/readers/contributors pre-Freeze; violations throw SimBuildException | ADR-0003 ✅ | M-1a |
| TR-sim-013 | Composition root in Composition/; constructor injection only; reflection test on deps | ADR-0003 ✅ | M-1a |
| TR-sim-014 | SimWorld (host) and ClientLayer (client shell) separate; host loopback uses same encoder | ADR-0003 ✅ | M-1a |
| TR-sim-015 | Op = 64 B blittable: 16 B header (opSeq, actor, kind, reqId) + ≤48 B payload | ADR-0007 ✅ | M-1a |
| TR-sim-016 | Actor stamped by adapter from peer; System=0x00000001; opSeq monotonic, never reused | ADR-0007 ✅ | M-1a |
| TR-sim-017 | Op ring 4096 (pow2); full → false, no opSeq consumed, adapter replies busy; systemOpReserve 64 | ADR-0007 ✅ | M-1a |
| TR-sim-018 | Drain ≤64 ops/tick by opSeq; validate fully before mutation; every drained op recorded | ADR-0007 ✅ | M-1a |
| TR-sim-019 | Only player/System requests use the FIFO; in-tick callers use sync I…Api in own phase | ADR-0003 ✅ | M-1a |
| TR-sim-020 | Reply {reqId,result,reason u16,extra ≤24 B} after handler events; reasons 1–63 Sim; no reqId dedupe | ADR-0007 ✅ | M-1a |
| TR-sim-021 | One preallocated per-tick EventLog (4096, reserve 256); blittable no-string entries | ADR-0008 ✅ | M-1a |
| TR-sim-022 | No sync subscribers; pull cursors ≤8; IEventSink.Deliver once in phase 13, copy-only | ADR-0008 ✅ | M-1a |
| TR-sim-023 | Replication classes Internal/Replicated/OwnerOnly/Targeted; undeclared kind fails test | ADR-0008 ✅ | M-1a |
| TR-sim-024 | Two-tier overflow admission; overflowInfo masks; adapter resyncs exactly those | ADR-0008 ✅ | M-1a single-tier flag + masks; two-tier admission at first consumer |
| TR-sim-025 | EntityId uint32 class<<24\|counter; classes 01–08; 09–EF reserved; F0–FF tests; never reused | ADR-0004 ✅ | M-1a |
| TR-sim-026 | mailId separate uint32; PlayerSlot u8 0–7; slot freed in T reusable from T+1 | ADR-0004 ✅ | M-1a |
| TR-sim-027 | PCG32 XSH-RR; Derive via Mix64/Fnv1a64; all unchecked | ADR-0005 ✅ | M-1a |
| TR-sim-028 | Compile-time RngNames registry; v1 stream list; mail/mail.special/events indexed per shift | ADR-0005 ✅ | M-1a |
| TR-sim-029 | NextInt(b)=(next32*b)>>32, one draw per call; no float RNG; owner-only handles | ADR-0005 ✅ | M-1a |
| TR-sim-030 | Committed golden vectors (pcg32, Mix64, Fnv1a64, Derive) | ADR-0005 ✅ | M-1a |
| TR-sim-031 | Integer-only numerics (mm, permille, ticks, Cents, Fx16, BAM+Q15); float/double/decimal banned | ADR-0006 ✅ | M-1a |
| TR-sim-032 | Determinism checklist: no Dictionary iteration, stable sort, no Random/Guid/hash codes/time/threads/statics | ADR-0006 ✅ | M-1a |
| TR-sim-033 | T1: identical canonical dump hash per tick given same inputs and logs | ADR-0006 ✅ | M-1a |
| TR-sim-034 | T2: Windows+Linux x64 bit-identical carrier step, RNG, hashes, shared pure functions | ADR-0006 ✅ | M-1a |
| TR-sim-035 | Canonical dump via ISimStateContributor (fixed order, LE); SHA-256 in tests only | ADR-0006 ✅ | M-1a |
| TR-sim-036 | Tick() allocates 0 B after 300 warm-up ticks; blocking from M0 | ADR-0006 ✅ | M-1a advisory; blocking from M0 |
| TR-sim-037 | Host tick p99 5.0 / max 8.0 / median 3.0 ms; ClientLayer p99 0.3 ms; advisory until reference machine | ❌ No ADR — `perf-budget.md` (not written) | advisory |
| TR-sim-038 | Per-phase p99 slices summing 4.50 ms + 0.50 reserve | ❌ No ADR — `perf-budget.md` (not written) | advisory |
| TR-sim-039 | CI perf gate: p99 rank, N≥18,000, fail at 2× calibrated baseline | ❌ No ADR — `perf-budget.md` (not written) | M0 |
| TR-sim-040 | Always-on checks to fixed record ring; asserts compile out of Release; SimCounters | ADR-0001 ✅ | M-1a |
| TR-sim-041 | Port interfaces (ISimPhase, IOpHandler, IEventSink, IHostClock, IMonotonicClock, IRngSource …) | ADR-0003 ✅ | M-1a |
| TR-sim-042 | Sim knows no peers/accounts/channels/RPC/wall clock/RTT/Godot types; request ticks in host domain | ADR-0011 ✅ | M-1a |
| TR-sim-043 | Join snapshot after Deliver of joinTick; joiner buffers later batches | ADR-0011 ✅ | M0 |
| TR-sim-044 | Lifecycle Constructed→Ready→Running→Stopped/Faulted; RequestStop via IRunControl in phase 12 | ADR-0003 ✅ | M-1a |
| TR-sim-045 | Recorder stores ops + transform samples (in-memory M-1a, file M5) | ADR-0006 ✅ | M-1a |

**Untraced:** TR-sim-037, 038 and 039 (perf budgets) have no ADR. By design they belong to `docs/architecture/perf-budget.md`, which is not written. They stay ADVISORY at M-1a (architecture.md Q-ARCH-10).

## Stories

Implementation order = story number (each story's `Depends on:` is authoritative). Sizing: one `/dev-story` session each (≈ ≤ 1 day, ≤ ~600 lines incl. tests).

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Build configurations, SIM_ASSERT and the net8.0 CI leg (scaffold A)](story-001-build-config-sim-assert-ci-scaffold.md) | Logic | Complete | ADR-0001 |
| 002 | [Core primitives I — id value types](story-002-id-value-types.md) | Logic | Complete | ADR-0004 |
| 003 | [Core primitives II — numerics toolkit (Cents, Fx16, Fix)](story-003-numerics-toolkit.md) | Logic | Complete | ADR-0006 |
| 004 | [Determinism analyzers and SC-ARCH-2 fixtures (scaffold B)](story-004-determinism-analyzers-sc-arch-2.md) | Logic | Complete | ADR-0006, ADR-0001 |
| 005 | [Core primitives III — BAM angles, Q15 trig table and StableSort](story-005-bam-trigq15-stablesort.md) | Logic | Complete | ADR-0006, ADR-0003 |
| 006 | [TickRate and F1 tick-time conversions](story-006-tickrate-f1-time-conversions.md) | Logic | Complete | ADR-0002, ADR-0006 |
| 007 | [SimCounters and SimCheckRing (always-on diagnostics)](story-007-simcounters-simcheckring.md) | Logic | Complete | ADR-0001 |
| 008 | [RNG primitives, RngStream and golden vectors](story-008-rng-primitives-golden-vectors.md) | Logic | Complete | ADR-0005 |
| 009 | [RNG stream registry (RngNames, RngRegistry) and owner-filtered views](story-009-rng-registry-owner-views.md) | Logic | Complete | ADR-0005, ADR-0003 |
| 010 | [TickDriver — the F2 integer accumulator](story-010-tickdriver-f2-accumulator.md) | Logic | Complete | ADR-0002 |
| 011 | [Phase table, builder binds and the tick loop](story-011-phase-table-builder-tick-loop.md) | Logic | Complete | ADR-0003 |
| 012 | [World lifecycle, tick guards, owner thread and faults](story-012-world-lifecycle-guards-faults.md) | Logic | Complete | ADR-0003, ADR-0002 |
| 013 | [Op and Reply value types, OpKind and op descriptors](story-013-op-reply-types-descriptors.md) | Logic | Complete | ADR-0007 |
| 014 | [Op ring and EnqueueOp (stamping, reserve, refusals)](story-014-op-ring-enqueueop.md) | Logic | Complete | ADR-0007 |
| 015 | [Event log, call scopes and phase-13 delivery](story-015-event-log-call-scopes-delivery.md) | Logic | Complete | ADR-0008, ADR-0003 |
| 016 | [Event overflow (single tier + masks), misuse raises and pull readers](story-016-event-overflow-pull-readers.md) | Logic | Complete | ADR-0008 |
| 017 | [Builder validation — SimBuildException codes for ops, events and readers](story-017-builder-validation-codes.md) | Logic | Complete | ADR-0003, ADR-0007, ADR-0008 |
| 018 | [Entity and mail id allocators, and the SlotTable](story-018-id-allocators-slottable.md) | Logic | Complete | ADR-0004 |
| 019 | [Canonical state dump (CanonicalWriter, contributors, Sim Core section)](story-019-canonical-state-dump.md) | Logic | Complete | ADR-0006 |
| 020 | [Phase-0 drain, Core pre-checks and OpReply append](story-020-phase0-drain-prechecks-replies.md) | Logic | Complete | ADR-0007, ADR-0008, ADR-0006 |
| 021 | [Transform sample ring, tick-boundary latch and stamping](story-021-transform-sample-ring-latch.md) | Logic | Complete | ADR-0003, ADR-0011, ADR-0006 |
| 022 | [PlayerJoin and PlayerLeave System handlers](story-022-playerjoin-playerleave-handlers.md) | Logic | Complete | ADR-0004, ADR-0007 |
| 023 | [ClientLayer shell, InputLatch and the IHostClock seam](story-023-clientlayer-shell-inputlatch.md) | Logic | Complete | ADR-0003, ADR-0002, ADR-0011 |
| 024 | [FrameRunner — the per-frame pump](story-024-framerunner.md) | Logic | Complete | ADR-0002, ADR-0011 |
| 025 | [Composition root, SystemGraph and the constructor reflection test](story-025-composition-root-reflection-test.md) | Logic | Complete | ADR-0003, ADR-0010 |
| 026 | [In-memory recorder (ops and samples) and ReplayDriver](story-026-inmemory-recorder-replay.md) | Logic | Complete | ADR-0006, ADR-0007 |
| 027 | [SC-DET-1 — same-machine determinism harness (T1)](story-027-sc-det-1-determinism-harness.md) | Logic | Complete | ADR-0006 |
| 028 | [Architecture rules suite — SC-ARCH-1](story-028-architecture-rules-sc-arch-1.md) | Logic | Complete | ADR-0003, ADR-0001, ADR-0002, ADR-0005 |
| 029 | [Zero-allocation tick probe and tick-timing report (advisory at M-1a)](story-029-zero-alloc-tick-timing-advisory.md) | Logic | Ready | ADR-0006 (TR-sim-037: none — advisory) |
| 030 | [SC-X-1 — cross-system batch order with real Grid and Throw ops](story-030-sc-x-1-cross-system-batch-order.md) | Integration | Ready | ADR-0007, ADR-0008 (+ ADR-0015 Accepted 2026-10-16) |

**Formerly blocked:** 030 is Ready (2026-10-16 story hygiene). It depends on `production/epics/grid-inventory/story-007-pick-move-ops.md` (first real container-op handler) and `production/epics/throwing-catching/story-010-throwrelease-handler-launch-commit.md` (real `throw` handler); ADR-0015 is Accepted.

**M-1a AC coverage:** SC-R1-1 (011), SC-R2-1 (024 stub transport; loopback half in network-adapter-loopback), SC-R3-1 (011), SC-R4-1 (017 + 025), SC-ARCH-1 (001, 025, 028), SC-ARCH-2 (004), SC-R6-1 (014 + 020), SC-R8-1/SC-R8-2/SC-R10-1 (020), SC-R12-2 (015), SC-R14-1 (016), SC-R17-1 (023), SC-F2-1 (010), SC-R19-1 (002 + 018), SC-R21-1 (022), SC-R24-1 (009 + 019), SC-R25-1/SC-F5-1 (008), SC-F1-1 (006), SC-F7-1 (003), SC-DET-1 (027), SC-ST-1 (012), SC-X-1 (030, Blocked), SC-X-2 (network-adapter-loopback epic). Perf TR-sim-036/037 advisory (029); TR-sim-038/039 wait for `perf-budget.md` (M0).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All `[M-1a]` acceptance criteria from `design/gdd/simulation-core.md` are verified (25 ACs; `[M-1b]`/`[M0]` ACs stay open on their milestones)
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/sim/**`, `tests/integration/**`), green on both the net10.0 and the net8.0 legs
- SC-ARCH-1 and SC-ARCH-2 run in CI and fail the build on violation
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (none are expected in this epic)

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): then Track B: 029 (B1), 030 (B1). Earlier notes below are history.

Run `/story-readiness production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md`, then `/dev-story` on it. Work through stories in order — each story's `Depends on:` field tells you what must be DONE first.
