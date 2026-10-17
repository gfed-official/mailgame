# Story 014: Op ring and EnqueueOp (stamping, reserve, refusals)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R6 actor/opSeq, R7 queue capacity and `busy`, R20 exhaustion; F8)
**Requirement**: `TR-sim-016` (actor stamped from the peer; `System = 0x00000001`; `opSeq` monotonic, never reused), `TR-sim-017` (ring 4096 pow2; full → false, no `opSeq` consumed; `systemOpReserve` 64)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Op Queue, Drain and Replies (§2)
**ADR Decision Summary**: One preallocated ring `Op[opQueueCapacity]` (default 4096, power of two) plus a parallel `uint[]` of enqueue-boundary ticks (the `CurrentTick` at enqueue = first tick that could apply it). `bool SimWorld.EnqueueOp(ActorId actor, in Op op)` copies the op in, **overwrites** `Actor` from the parameter and stamps the next `OpSeq`. Refusals return `false`, consume no `opSeq`, record nothing in the recorder, and count: `busyPlayer` (`count ≥ capacity − systemOpReserve`, non-System actor), `busySystem` (ring completely full; plus one always-on record), `opSeq` exhausted (plus one record), `enqueueBadState` (world not `Ready`/`Running`). Owner-thread asserted in Debug.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: one preallocated ring `Op[opQueueCapacity]` + parallel `uint[]` boundary ticks; `EnqueueOp(ActorId actor, in Op op)` overwrites `Actor` and stamps `OpSeq` (manifest I-4: follow ADR-0007's two-argument signature); refusals return false, consume no `opSeq`, record nothing, and count `busyPlayer` / `busySystem` (+ record) / opSeq exhausted (+ record) / `enqueueBadState`; the ring and boundary array allocate nothing after construction.
- Forbidden: reading the actor from a payload; deduping `reqId` in the Sim; representing `busy` as an event-log entry; calling `EnqueueOp` from a transport callback (adapter rule).
- Guardrail: op ring 4096 × 64 B = 256 KiB + 16 KiB boundary ticks; `EnqueueOp` 0 B.

---

## Acceptance Criteria

*From GDD SC-R6-1 (enqueue half) and ADR-0007 §2, scoped to this story:*

- [x] **AC-1 SC-R6-1 (enqueue half).** Across 1 000 `EnqueueOp` calls where every 10th is made while the ring is held full (small test capacity), and one op's payload names another player's id: accepted ops carry strictly increasing `OpSeq` values starting at 1 with no reuse; refused calls return `false` and consume no `OpSeq` (the next accepted op gets the next value); every queued op's `Actor` equals the `actor` argument, never the payload's id or the op's incoming `Actor` field; `ActorId.System.Value == 0x00000001`. (The handler-side half — "the handler sees the stamped actor" — is asserted in Story 020.)
- [x] **AC-2 Reserve and refusals.** With capacity C and reserve R: a non-System enqueue at `count == C − R` returns false and counts `busyPlayer`; a System enqueue at that count is accepted; a System enqueue at `count == C` returns false, counts `busySystem` and writes one `BusySystem` check record; a world built with the `opSeq` counter at 2^32 − 1 refuses the next enqueue, counts it and writes one `OpSeqExhausted` record without wrapping; `EnqueueOp` on a `Constructed`, `Stopped` or `Faulted` world returns false and counts `enqueueBadState`; on a `Ready` world it is accepted with boundary tick 0.
- [x] **AC-3 Boundary ticks and allocation.** Each accepted op's boundary tick equals `CurrentTick` at the time of the call; 10 000 enqueue/dequeue cycles through the ring allocate 0 B after construction.

---

## Implementation Notes

*Derived from ADR-0007 §2 and Implementation Guidelines:*

- `src/PerformativeMail.Sim/Core/OpQueue.cs` (internal to Core, owned by `SimWorld`): `Op[]` and `uint[]` sized from `SimCoreSettings.OpQueueCapacity` (assert power of two), head/count indices with mask arithmetic, `SystemOpReserve` from settings. Expose internal read access (`PeekAt(i)`, `Count`, `BoundaryTick(i)`) for the drain (Story 020), the dump (Story 019) and tests.
- `SimWorld.EnqueueOp(ActorId actor, in Op op)`: assert owner thread (Story 012 helper); check state (`Ready`/`Running` only → else `enqueueBadState`); check capacity/reserve; check `opSeq` exhaustion **before** incrementing; copy, then set `Actor = actor.Value`, `OpSeq = next`; store boundary tick = `CurrentTick`. The recorder hook `IOpRecorder.OnEnqueued(boundaryTick, in op)` is added in Story 026 — leave a clearly named call site.
- `opSeq` starts at 1 (0 = none, ADR-0004 §1). The initial value comes from `SimInitialCounters` (ADR-0003 §4 `SimBuildArgs`) so tests and Free Play can start near exhaustion without a seam; for this story accept it via the builder/world constructor.
- Refusal while the Sim is closed during `Deliver` (`enqueueDuringFlush`) is ADR-0008 §7 — Story 015 adds that check.
- `busy` replies are the **adapter's** job (network-adapter-loopback): the Sim only returns `false`.
- No LINQ, no resizing, no exceptions on refusal.

---

## Out of Scope

- Story 015: `enqueueDuringFlush` refusal during `Deliver`.
- Story 019: dumping queued ops field by field with their boundary ticks.
- Story 020: draining (`maxOpsDrainedPerTick`), pre-checks, replies, handler-sees-actor.
- Story 026: `IOpRecorder.OnEnqueued`.
- SC-R7-1 (M-1b, 4 096/64 at full scale) and SC-R20-1 (M-1b) — mechanics land here; those ACs are verified at M-1b.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/OpQueueTests.cs` (`[Trait("Id", "SC-R6-1")]`) — must exist and pass

**Status**: [x] Created and passing — `tests/unit/sim/Core/OpQueueTests.cs` (`OpQueueTests` + NonParallel `OpQueueAllocationTests`, `[Trait("Id","SC-R6-1")]`)

---

## Dependencies

- Depends on: Story 007, Story 012 (lifecycle states, owner thread), Story 013 (`Op`)
- Unlocks: Story 019, Story 020, network-adapter-loopback (`HostAdapter` → `EnqueueOp`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 → OpQueueTests.cs:62-173; AC-2 → :177-362; AC-3 → :402-443 + OpQueueAllocationTests :706/:729 via AllocAssert)
**Verification run**: tools/ci/test-legs-loop.sh 1 — 1 115 tests/leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 5 skipped). Earlier 20/20 flakiness loop green (TD-026 closed).
**Deviations**: ADVISORY — GDD R20 (design/gdd/simulation-core.md:289) says opSeq exhaustion "asserts in Debug"; ADR-0007 §2/Risks and AC-2 say return false + one record, and the code (SimWorld.cs EnqueueOp) follows the ADR (no assert; OpQueueTests.cs:273 expects no throw). Reconcile the GDD wording, not the code → TD-030.
**Gates**: QL-TEST-COVERAGE ADEQUATE (advisory: no test that bad state wins over full ring/exhaustion); LP-CODE-REVIEW APPROVE (advisories: SimWorld.Ops exposes OpQueue.Push; EnqueueOp/Stop reuse SimAssertCode.TickWrongThread; AllocAssert min-of-3 trade-off acceptable). Both gates ran in the earlier interrupted close; verdicts relayed by the coordinator and not re-run. godot-csharp-specialist cross-check: no blockers (TD-027).
**Test Evidence**: Logic: tests/unit/sim/Core/OpQueueTests.cs
**Code Review**: Complete (LP-CODE-REVIEW APPROVE)
**Tech debt**: TD-027 (review carry-forwards, Story 017), TD-030 (R20 wording), TD-031 (Ops/Push exposure, assert-code name, bad-state precedence test)
**Run result**: N/A — Godot-free Sim logic, no observable surface; verified by the four-leg suite run above.
