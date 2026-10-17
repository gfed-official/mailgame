# Story 015: The one-commit gate (G9) — in-progress / pending commits, "Busy", "Release to retry" and `pendingCommitTimeout`

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~0.5–1 day; ~350 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (G9 Commits, Edge Cases "two different commits are started on different inputs", Visual/Audio "One-commit Busy"; AC H-EC-1)
**Requirement**: `TR-input-023` (one-commit rule for deliver / deconstruct / throw; `pendingCommitTimeout` 500 ms)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 "One-commit gate (G9, TR-input-023)") — Accepted 2026-10-16 (primary); ADR-0011 (reply events retire pending ops) secondary
**ADR Decision Summary**: A commit (deliver hold, deconstruct, throw) is *in progress* from its press (router `aiming`, or the interaction snapshot's `Holding` state) and *pending* from send until its reply (`ReplyObserved(reqId)` from the ClientAdapter) or `pendingCommitTimeout` (500 ms, ms clock). Meanwhile a second commit press is muted with "Busy" (then "Release to retry"), and item-moving taps (Drop, quick-move) are not live. Movement, Jump, ping, the comms wheel and push-to-talk are never gated.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: the pending window uses the ms clock; replies retire pending ops (`ReplyObserved`); the gate never blocks communication or movement (ADR-0012 §3, GDD G9)
- Forbidden: a second commit racing the first; a "full ring followed by nothing" (a rejection must surface its reason in the prompt slot — HUD renders it)
- Guardrail: 0 B

---

## Acceptance Criteria

*From GDD G9 and H-EC-1, scoped to this story (router half; the end-to-end run with the real hold counter is story 021):*

- [ ] **Gate states.** `CommitGate` has `None`, `InProgress(source)` and `Pending(sentMs, reqId?)`. It enters `InProgress` when the router starts aiming (story 014) or when the presented interaction snapshot reports `InteractState.Holding` for a **Hold-mode deliver** target; it enters `Pending` when the aim commits (Throw immediate queued) or the snapshot reports `Complete` for a hold; it returns to `None` on `ReplyObserved(reqId)` (or **any** reply when no `reqId` is known), on `pendingCommitTimeout` (default 500 ms, range 300–1000) measured on the ms clock, or when the in-progress commit is cancelled.
- [ ] **Busy.** While the gate is not `None`, a press of a *different* commit action (LT Throw while an E deliver hold runs; E deliver while aiming) is **muted**: no edge, no immediate, a `Cue(Busy)` log line, and its release is swallowed — **LT's release throws nothing**. Item-moving taps (Drop, quick-move) are **not live** during the commit (no `drop` immediate, no `QuickMove`). Movement, Jump and the `CommitGated = false` actions are never gated and show no "Busy" (comms wheel, quick ping and push-to-talk are M0 / M1 and carry the same flag when registered).
- [ ] **H-EC-1 (router half).** E deliver hold at tick 4 (snapshot `Holding`), then LT pressed at tick 5 → LT muted with `Busy`; Drop (Q / D-pad ↓) at tick 6 → nothing, no `drop`; Space (Jump) at tick 6 → the Jump edge fires; the hold completes at tick 12 (snapshot `Complete` → `Pending`); LT pressed at tick 14, before the host reply, is **still muted** (pending); if that button is still down when the commit ends, the cue reads `ReleaseToRetry` (once), and a fresh press then works.
- [ ] **`CommitSent` / `ReplyObserved`.** `CommitSent(uint reqId, ActionId a)` (called by Throwing's sender after a successful send — `production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md`) binds the pending commit to the `reqId`; `ReplyObserved(uint reqId)` retires it. A `CommitSent` for a request that the local sink refused (`IRequestSink.TrySend` false) is **not** called, so G9 does not arm (Throwing 007). A `ReplyObserved` for an unknown `reqId` is ignored and counted.
- [ ] **Timeout.** With no reply, the gate clears at exactly `sentMs + pendingCommitTimeoutMs` (499 ms → still muted, 500 ms → clear) and the Busy cue stops; a late reply after the timeout is ignored.
- [ ] **Rejection visibility hook.** `ReplyObserved(reqId, reasonId)` records the reply reason id in the log (`Cue(RequestRejected)`) so HUD can show it in the prompt slot ("a full ring followed by nothing is never allowed"); the router does not format text.

---

## Implementation Notes

*Derived from ADR-0012 §3 and GDD G9:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `CommitGate.cs`, `InputRouter.Commit.cs`, `CommitKind.cs`. `ActionDef` gains `CommitGated` (item-moving taps and commit starters) — additive field on story 005's type; update its validator.
- **Deliver has no `reqId` at the router**: the hold completes inside the Sim's `HoldAdvance` step, which sends `deliver_matching` through `IRequestSink` itself, so the router infers `Pending` from the snapshot's `Complete` and clears on any reply observed, the explicit `reqId` when provided, or the timeout. This works without a ClientAdapter change; the explicit-`reqId` path (needed for Throw) depends on the **unowned seam** "reqId exposure from the `ClientAdapter`" (Throwing EPIC seam 9) — until it exists use the adapter's `LastReqId` accessor proposed in Throwing 007.
- **Who calls `ReplyObserved`?** The reply path of `ClientAdapter` / `MirrorDrain` (network-adapter-loopback story 006). No existing story wires it to the router; story 028 adds the wiring in `GameLoop` (observer registered at boot) and EPIC.md lists it.
- The `Busy` / `ReleaseToRetry` strings are HUD's; the router emits cue ids on the prompt-slot channel (`LogKind.Cue`) which the HUD reads.
- Deconstruct (M2) uses the same gate; no code path at M-1a.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014 (aim), 012 (Interact latch). Story 021 (end-to-end with `HoldAdvance`). Story 028 (reply wiring). M2: deconstruct commit. Prompt text rendering (HUD, no epic).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/CommitGateTests.cs` (`[Trait("Id", "H-EC-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 012 (hold state from the snapshot), 013, 014; `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md` (`InteractState`)
- Unlocks: Stories 016, 021; `production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md` (`CommitSent`), `production/epics/manual-delivery/story-004-press-latch-cancels-request.md` (commit visible to Busy)
