# Story 020: Phase-0 drain, Core pre-checks and OpReply append

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M–L (≈ 1 day; ≈ 550 lines incl. tests — the largest Core story; split AC-3 into a follow-up if it runs over)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R6 actor, R8 drain and validate-then-mutate, R10 replies)
**Requirement**: `TR-sim-018` (drain ≤ 64 ops/tick by `opSeq`; validate fully before mutation; every drained op recorded), `TR-sim-020` (reply after handler events; reasons 1–63 Sim; no `reqId` dedupe), `TR-sim-016` (handler sees the adapter-stamped actor)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Op Queue, Drain and Replies (§3, §5) — primary; ADR-0008 (§3 `OpReply` audience, §5 call scopes) — secondary; ADR-0006 (§7 `IOpRecorder.OnDrained`) — secondary
**ADR Decision Summary**: At the start of phase 0 the queue captures `n = min(count, maxOpsDrainedPerTick)` (default 64) and applies those ops in ascending `opSeq`. Per op: Sim Core pre-checks in order — unregistered kind, wrong `PayloadLength` or non-zero `Reserved` → `unknown_kind` (3); actor neither `System` nor a present player, or a non-System actor on a `SystemOnly` kind → `unknown_actor` (2) — then opens a call scope and calls `IOpHandler.Apply`, then appends the returned `Reply` as an `OpReply` event **after** every event the call raised (OwnerOnly to the actor's slot; audience 0 for `System`), and calls `IOpRecorder.OnDrained(applyTick, op, reply)`. Ops beyond the cap stay queued (`drainBacklog`); System ops get no bypass; ops enqueued during the drain apply on a later tick.
**ADR Version**: 2026-10-16 (ADR-0007, ADR-0008, ADR-0006)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: drain `n = min(count, maxOpsDrainedPerTick)` ascending `opSeq`; pre-checks in ADR order; call scope; `IOpHandler.Apply`; Sim Core appends `OpReply` after every event the call raised and calls `IOpRecorder.OnDrained`; ops beyond the cap stay queued (`drainBacklog`); System ops no bypass; handlers validate fully before the first mutation, allocation, RNG draw or raise; `Reply` `Ok ⇔ Reason == 0`; `OpReply` is `OwnerOnly` to the actor's slot (0 for `System`).
- Forbidden: removing the drain cap; a handler appending its own `OpReply` or reordering the log; dedupe/compare/store of `reqId` in the Sim; reading the actor from a payload; routing in-tick effects through the FIFO.
- Guardrail: `Inputs.Apply` 0.80 ms p99, ≤ 64 ops/tick (advisory until a reference machine exists); the drain loop allocates 0 B.

---

## Acceptance Criteria

*From GDD SC-R8-1, SC-R8-2, SC-R10-1 and SC-R6-1 (handler half), scoped to this story:*

- [ ] **AC-1 SC-R8-1.** With `maxOpsDrainedPerTick = 64` and 100 queued ops (a test handler records apply order), one tick applies exactly ops 1–64 in `opSeq` order and `drainBacklog` reads 36; a test handler that enqueues an op during phase 0 sees it accepted, but it is not applied that tick; the next tick applies ops 65–100 before the hooked op, and the hooked op applies on a later tick. The handler observes `op.Actor` equal to the `EnqueueOp` actor argument (SC-R6-1 handler half).
- [ ] **AC-2 SC-R8-2.** Given a test handler that validates and rejects, when its op drains: the canonical dump hash, every id allocator's next value and every stream's `draws` are unchanged except `opSeq` (already consumed at enqueue); the only log entry that tick is its `OpReply` (Rejected, the handler's reason); and an attached `IOpRecorder` stub sees `OnDrained` exactly once with the apply tick and that reply. The same harness is exposed as a reusable test fixture so every system's handler stories can run it (ADR-0007 "SC-R8-2 harness per handler").
- [ ] **AC-3 SC-R10-1.** Given a handler that raises 2 events and commits, an op with an unregistered kind, and a `System`-actor op, when they drain: the log reads event, event, `OpReply` (OwnerOnly to the actor's slot bit, reason 0, `ReqId` echoed unchanged) for the first; the Core reject's `OpReply` carries `unknown_kind` (a reason in 1–63) and no handler ran; a wrong `PayloadLength` and a non-zero `Reserved` are also `unknown_kind`; a non-present actor and a non-System actor on a `SystemOnly` kind get `unknown_actor`; the `System` op's reply has audience 0.

---

## Implementation Notes

*Derived from ADR-0007 §3, §5, ADR-0008 §3, §5 and ADR-0006 §7:*

- Phase 0 is a built-in `ISimPhase` installed as the first entry of `_phases[0]` at `Freeze()` (ADR-0003 §2); systems never bind phase 0 — they bind `IOpHandler`s (Story 017).
- Drain loop (allocation-free):
  ```
  n = min(queue.Count, settings.MaxOpsDrainedPerTick)
  for i in 0..n-1:
     ref readonly Op op = ref queue.PeekFront(); // ascending opSeq
     Reply reply;
     if (!TryPreCheck(in op, out reply)) { /* no handler */ }
     else { using (var scope = log.BeginCall()) { reply = handler.Apply(in op, in ctx); } }
     AppendOpReply(in op, in reply);          // after every event the call raised
     recorder?.OnDrained(CurrentTick, in op, in reply);   // null-object recorder preferred over null checks
     queue.PopFront();
  counters.Set(DrainBacklog, queue.Count)     // a level, not an increment ("reads 36")
  ```
  `SimCounters` needs a `Set` (or a dedicated gauge) for `drainBacklog`; add it.
- Pre-check order exactly: (1) kind not registered → `UnknownKind`; (2) `PayloadLength != descriptor.PayloadLength` or `Reserved != 0` → `UnknownKind`; (3) actor ≠ System and not `SlotTable.IsPresent(actor)` → `UnknownActor`; (4) `descriptor.SystemOnly && actor ≠ System` → `UnknownActor`.
- `OpReply` append: `EventKind.OpReply` (0x0001) with the `Reply` struct (36 B) as payload; class `OwnerOnly`; audience = `1 << slot` of the actor's slot (0 for `System`). Register the `OpReply` `EventDescriptor` at `Freeze()` (Sim Core owns it). The reply is appended outside the handler's scope but after its entries, so order is event, event, reply.
- `IOpRecorder` interface (`Core/IOpRecorder.cs`, ADR-0006 §7): `OnEnqueued`, `OnDrained`, `OnSample`, `OnOverflow` — declare all four now; bind with `SimWorldBuilder.BindRecorder(IOpRecorder)` (optional); default to a null-object recorder. `InMemoryRecorder` is Story 026.
- SC-R8-2 fixture: put `RejectHarness` in the test project's shared fixtures (dump hash + `EntityIds.Peek` for every class + `MailIds.Peek` + every live stream's `Draws`, before and after).
- Composite actions (one `OpKind` calling several `I…Api`s) and `TickGuard.AssertMutable()` belong to the systems that publish APIs; nothing here.
- `busy` is never produced by the Sim (adapter-only); `bad_request` is the adapter's or a handler's.

---

## Out of Scope

- Story 014: enqueue and stamping; Story 017: handler binding errors.
- Story 022: `PlayerJoin`/`PlayerLeave` handlers.
- Story 026: `InMemoryRecorder`, `OnEnqueued` call site, replay.
- SC-R8-3 (M-1b, ops around a `PlayerLeave`) — the pre-checks here implement it; verified at M-1b.
- SC-X-1 (Story 030): real Grid/Throwing ops.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/OpDrainTests.cs` (`[Trait("Id", "SC-R8-1")]`, `[Trait("Id", "SC-R8-2")]`, `[Trait("Id", "SC-R10-1")]`, `[Trait("Id", "SC-R6-1")]`) and the shared `RejectHarness` fixture — must exist and pass

**Status**: [x] Created — tests/unit/sim/Core/OpDrainTests.cs (21 tests, SC-R8-1/SC-R8-2/SC-R10-1/SC-R6-1 traits) + tests/PerformativeMail.Sim.Tests/Fixtures/RejectHarness.cs

---

## Dependencies

- Depends on: Story 014, Story 015, Story 017, Story 018, Story 019 (dump hash for SC-R8-2)
- Unlocks: Story 022, Story 026, Story 027, every system's op handlers

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 3/3 passing (all covered by unit tests; no deferred items)
**Deviations**: Advisory only — (1) the drain runs from the existing `InputsDrain()` hook at the start of phase 0, not as an `ISimPhase` at `_phases[0][0]` (keeps CallSeq numbering; behaviour identical; phase-0 probe timing includes it). (2) `OpReply` raised outside the call scope → CallSeq 0 (story text; ADR-0008 §5(d) expects a CallSeq link — TD-048). (3) Reply audience resolved before the handler runs (PlayerLeave replies to the issuing slot); non-present actor → audience 0. (4) Release Result/Reason mismatch: counts `ReplyInconsistent` (25), sets Result from Reason, still appends. (5) `MaxOpsDrainedPerTick = 0` = drain nothing (legacy OpQueueTests/CanonicalDumpTests rigs). New: `SimBuildErrorCode.HandlerKindMismatch = 23`, `SimAssertCode.WrongThread = 47`, `ReplyResultReasonMismatch = 48`, `CounterId.ReplyInconsistent = 25`; `IOpRecorder` (+ internal `NullOpRecorder`, placeholder `TransformSample`), `SimWorldBuilder.BindRecorder`, `SimCounters.Set`, `OpQueueView`. Folded debt resolved: TD-031 (closed), TD-042 item 1, TD-020 item 2, TD-045 item 6. Gate advisories logged as TD-048.
**Test Evidence**: Logic — tests/unit/sim/Core/OpDrainTests.cs + Fixtures/RejectHarness.cs. Verification: the real tree's test project did not compile during closure because of another runner's in-flight tests/unit/world/WorldTilesTests.cs (missing `using PerformativeMail.Sim.Geometry;`); on an isolated copy excluding only that file, four legs (net10/net8 × Debug/Release) ran 1 682 tests, 0 Sim Core failures (the 1 failure is content-data's in-flight `ContentAssertCode` count test; Debug 12 skipped).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (0 blocking; accepted), QL-TEST-COVERAGE ADEQUATE.
