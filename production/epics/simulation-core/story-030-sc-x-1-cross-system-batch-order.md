# Story 030: SC-X-1 — cross-system batch order with real Grid and Throw ops

> **Epic**: Simulation Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: S (≈ 2–4 hours once unblocked; ≈ 150 lines of test)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

> **Unblocked 2026-10-16 (story hygiene):** previously BLOCKED on stories (and on ADR-0015, now Accepted). This AC needs a **real** Grid Inventory op handler (`production/epics/grid-inventory/story-007-pick-move-ops.md`) and a **real** `throw` op handler (`production/epics/throwing-catching/story-010-throwrelease-handler-launch-commit.md`), both listed under Depends on. The Simulation Core mechanics it exercises (drain, call scopes, `OpReply` after events) are Stories 015 and 020 and are already covered there with test handlers.

## Context

**GDD**: `design/gdd/simulation-core.md` (R10 reply after handler events; N3.1 order preserved per peer)
**Requirement**: `TR-sim-020` (reply after handler events), `TR-sim-018` (drain applies handlers in `opSeq` order)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0007: Op Queue, Drain and Replies (§3 reply append) — primary; ADR-0008 (§1 order, §5 call scopes); ADR-0009 (Grid op pipeline); ADR-0015 (throw — Accepted 2026-10-16)
**ADR Decision Summary**: For every drained op, Sim Core appends the handler's `Reply` as an `OpReply` event after every event raised during that handler's call scope, so a batch always lists a system's events before its reply; composite actions (a throw) are one `OpKind` whose handler calls documented `I…Api` methods in a fixed order.
**ADR Version**: 2026-10-16 (ADR-0007, ADR-0008, ADR-0009); ADR-0015 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `OpReply` appended by Sim Core after every event the call raised; composite actions are one `OpKind` calling `I…Api`s in a fixed order; events raised only on commit.
- Forbidden: a handler appending its own `OpReply` or reordering the log.
- Guardrail: none.

---

## Acceptance Criteria

*From GDD SC-X-1, scoped to this story:*

- [ ] **AC-1 SC-X-1.** Given a real Grid Inventory op that changes a container and a real throw op, when they apply in one composed world (M1aGreybox profile), each delivered batch lists that system's events before its `OpReply`, and the throw's entries read `Removed`, `ThrowLaunched`, then the Ok `OpReply`, in that order and under one `CallSeq` for the events.

---

## Implementation Notes

- Integration test in `tests/integration/sim/CrossSystemBatchOrderTests.cs` using `SimComposition.Build(M1aGreybox)` with a recording `IEventSink`; enqueue one inventory op and one throw op as the local player (after a `PlayerJoin`), tick, and assert the entry order and `CallSeq`s.
- Event kind names (`Removed`, `ThrowLaunched`) come from the grid-inventory and throwing-catching descriptors.

---

## Out of Scope

- SC-X-2 (Manual Delivery request from the host's own client over loopback): verified in the network-adapter-loopback epic.
- SC-X-3/4/5: M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/integration/sim/CrossSystemBatchOrderTests.cs` (`[Trait("Id", "SC-X-1")]`) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 020, Story 025; `production/epics/grid-inventory/story-007-pick-move-ops.md` (first real container-op handler); `production/epics/throwing-catching/story-010-throwrelease-handler-launch-commit.md` (real `throw` handler; ADR-0015 Accepted)
- Unlocks: epic Definition of Done (last `[M-1a]` Sim Core AC)
