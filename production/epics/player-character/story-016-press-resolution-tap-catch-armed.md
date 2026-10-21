# Story 016: Press resolution — Catch → the presented target's verb → catch-armed, the latch and Tap requests

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-20 (docs: PC016-SWIM-CATCH AC added — moved from story 013; stamp 2026-10-17.1 — the 17.1 changes touch only the Godot input adapter and `WorldTablesBuilder`)

> **Interaction-framework story (6 of 9).** The Sim-side half of Input & Controls R4.3 / G1: what a latched Interact edge *does*. Input & Controls latches the edge and the presented target; this story resolves it.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 "Catch at press … latched at press"; P12 press, hold, channel; P10 tap/hold/channel modes); `design/gdd/input-controls.md` (R4.3 `interactResolutionOrder`, G1 latch, H-R4-2, H-R4-4, H-R4-6)
**Requirement**: `TR-movement-012` (one request at the held tick; the press → latch half), `TR-movement-011`; serves `TR-input-*` resolution order owned by input-controls (cited by acceptance ids H-R4-2/4/6)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P11-2b, PCM-P12-1 (Tap row), H-R4-2, H-R4-4, H-R4-6.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§3 press resolution, the latch) — Accepted 2026-10-16 (primary); ADR-0016 (§2 `PreviewSubStep.EdgeResolve`, `IRequestSink`) and ADR-0012 (§4 `InputEdge` carries `TargetEntity`, `TargetSlot`, `PresentedSeq`) secondary — Accepted
**ADR Decision Summary**: In `EdgeResolve` (4d), after focus (4a) and the owners' previews (4b–4c), the tick's ordered `Interact` edges are applied in order (edges before hold accrual). A **press** resolves, first match wins: (1) **Catch** iff `ICatchPredictor.Evaluate` predicts a non-own flight entering this carrier's catch sphere on this tick **and** no hold is in progress **and** the flight's predicted static receiver is not the presented target anchor (D3 gate) → `Channel` start, the framework asks Throwing to send `CatchArm{down}`; (2) the **presented target's single verb** — the anchor named by the edge's presented-snapshot fields, if still registered, within `RangeMm` (the start range, not the incumbent range) and `IsAvailable` on this tick: `Tap` → build and send the request this tick; `Hold` → latch; `Channel` → start; (3) **no valid target** → catch-armed while held. A press on an unavailable target fires nothing (prompt pulse). The latch is `InteractionLatch = { AnchorRef, Verb, Mode, PressTick, HoldTicks, ClaimedPosition (feet at press), OwnerPayload (16 B) }`; a latched deliver hold is never converted into a catch.
**ADR Version**: ADR-0014 2026-10-16; ADR-0016 2026-10-16; ADR-0012 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Explicit sequential layout and `byte` flags (not `bool`) in `InteractionLatch` and request structs (ADR-0014 engine note 2).

**Control Manifest Rules (Feature layer)**:
- Required: press resolution order Catch (`ICatchPredictor`, D3 gate) → the presented target's single verb (in `RangeMm`, `IsAvailable` now) → catch-armed (ADR-0014 §3)
- Required: edges are resolved only in `PreviewSubStep.EdgeResolve`; `InputLatch` never dispatches verbs (ADR-0016)
- Forbidden: re-targeting a hold to the current focus; timing holds on the host (ADR-0014 Alts 1, 3)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P11-2b, PCM-P12-1, plus the story-local PC016-SWIM-CATCH from P6) and `design/gdd/input-controls.md` (H-R4-2 Sim half, H-R4-4, H-R4-6), scoped to this story:*

- [ ] **H-R4-2 (Sim half, parametrised).** (a) a predicted lob that reaches this carrier but not the focused mailbox → **Catch** (Channel start; `CatchArm{down}` requested through the Throwing port); (a′) the same lob predicted into the focused mailbox (`PredictedStaticReceiver == presented anchor`) → a **deliver hold** latches (D3 gate; no catch); (b) a container anchor → its `Open` **Tap** request is built and sent on the press tick; (c) a mailbox → a deliver hold latches; (d) no target → catch-armed while held (Channel start without an anchor; `CatchArm{down}`).
- [ ] **H-R4-6.** the last presented prompt names mailbox A and focus flips to a container on the press tick → the press latches a **hold on A** (the edge's presented snapshot, G1), not the container.
- [ ] **Start range (H-F2-1 start half).** a press whose presented target is 2501 mm from the chest never starts a hold or Tap and falls to rule (3); at 2500 mm it starts. The start range is the anchor's `RangeMm`; the incumbent slack does not apply.
- [ ] **Unavailable.** A press on a registered, in-range but `IsAvailable == false` target sends nothing, starts no hold, records `LastCancel = Unavailable(reason)` for the prompt pulse (Story 018 publishes it) and does **not** fall through to catch-armed.
- [ ] **PCM-P11-2b.** a press while a catchable item is inbound latches Catch; a thrown item arriving **during** a latched Deliver hold does not convert it (the hold completes on its tick — Story 017 asserts the tick-12 completion); a second Interact press edge while a hold or channel is in progress is ignored (no second latch, counted).
- [ ] **PCM-P12-1 (Tap).** a Tap verb sends exactly one request on the press tick (via `IInteractable.TryBuildRequest` and `IRequestSink.TrySend`); `TrySend` returning false (busy) sends nothing, leaves the interaction Idle and reports `busy` to the snapshot; the built request carries `ClaimedPosition` = the carrier's feet `(X, Y, Z)` at press (12 B, P13 layout).
- [ ] **Latch contents.** On a Hold press the latch holds `AnchorRef`, `Verb`, `Mode`, `PressTick` (= this tick), `HoldTicks` = `IInteractable.GetHoldTicks(latch, ctx)` evaluated **once** and never recomputed, `ClaimedPosition`, and `OwnerPayload` as filled by `IInteractable.OnPress` (pure with respect to owner state — Manual Delivery's press-tick branch).
- [ ] **Edge order.** Multiple Interact edges in one `InputFrame` are applied in arrival order, each resolving against this tick's focus state; a press followed by its release in the same frame is handled by Story 017 (this story only latches).
- [ ] **PC016-SWIM-CATCH — No Catch while swimming (story-local id; from GDD P6 "a swimmer cannot catch"; moved from story 013 on 2026-10-20).** While `CarrierStatus.Swimming`, a press never resolves to Catch or catch-armed: no `CatchArm{down}` is sent, rule (a)/(d) fall through to nothing (counted), and a press on an anchor follows story 013's swim gate (`Unavailable(reason.dry_land)` unless `AllowWhileSwimming`). The host-side arm refusal is throwing-catching story 022's (eligibility on arming); this story owns the client press half.
- [ ] **No-op on Dead / Panel.** Interact edges are refused (no latch, no request) when the gate (Story 013) says Panel or Dead; Mounted follows story 013's gate (no candidates until OQ-013-1 is ruled; TD-106).

---

## Implementation Notes

*Derived from ADR-0014 §3 and Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Players/Client/`: `EdgeResolveStep.cs` (`IClientStep`, bound at `PreviewSubStep.EdgeResolve`), `PressResolver.cs` (pure: `(edge, focusState, predictor result, registry, gate) → Resolution`), `InteractionLatch.cs`/`InteractionState.cs` (Idle | Holding | Channel | ToggleArmed | Complete — `ToggleArmed` is entered by the router's logical press/release, so this layer only sees press/release).
- **Ports defined here (PROVISIONAL — shapes owned by the throwing-catching epic / ADR-0015 §8 and the loopback adapter):** `ICatchPredictor { CatchPrediction Evaluate(in CatchQuery q); }` with `CatchPrediction { byte Predicted; ushort FlightId; PlayerSlot Thrower; byte Units; AnchorRef PredictedStaticReceiver }` (ADR-0015 §8 return fields); a default `NullCatchPredictor` (never predicts); `ICatchArmSender { bool Send(bool down); }` implemented by Throwing's client code (builds the `CatchArm{down|up}` op, ADR-0015 §7). `IRequestSink { bool TrySend(in Op op); }` is the ADR-0016 §1 port in `Sim/ClientLayer/Ports/`; no existing story defines it (sim-core 023 predates ADR-0016) — add it here if absent, and let `network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` implement it. `ClientLayer.Attach(ClientPorts)` is also unowned: add it if absent, additively, and tell the loopback owner (cross-epic gap logged in EPIC.md).
- The presented target comes from the `InputEdge` fields `(TargetEntity, TargetSlot, PresentedSeq)` (ADR-0012 §4). `PresentedSeq` is for the router/snapshot bookkeeping (Story 018); resolution uses `(TargetEntity, TargetSlot)` only.
- D3 gate: `catch = predictor.Predicted ∧ ¬holdInProgress ∧ predictor.PredictedStaticReceiver ≠ presentedAnchor`. No off-hysteresis preemption.
- `ctx.Tick` is the client tick (`ClientStepContext.ClientTick`, sim-core story 023); `PressTick` is that tick, which is also the tick the router attached the edge to (first tick of a catch-up batch).
- The hook calls use only `in`/value params; a `PureHookGuard` test decorator from Story 012 wraps test owners.
- Ambiguity flagged for the implementer (confirm against input-controls R4.4 before coding): a presented target that is *unregistered* or *out of start range* is treated as "no valid target" (rule 3, catch-armed); a registered, in-range, *unavailable* target fires nothing. Record the decision in the story's Last-Updated note.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 017: the hold counter, release/cancel edges, completion, cancels, channel stop, focus latching. Story 018: snapshot publication.
- Manual Delivery's `IInteractable` implementation (branch table, `deliver_matching` request) and Throwing's `CatchPredictor` / `CatchArm` handler — their epics.
- The router's per-press state machine, Toggle translation, one-commit gate (input-controls epic).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/PressResolutionTests.cs` (`[Trait("Id", "PCM-P11-2b")]`, `H-R4-2`, `H-R4-6`, `PC016-SWIM-CATCH`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 011, 012, 013, 014; `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` (`ClientLayer`, `InputFrame` edges, `ClientStepContext`); `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`IRequestSink` implementation)
- Unlocks: Stories 017, 018; manual-delivery epic (its `IInteractable.TryBuildRequest` / `OnPress` are called here); throwing-catching (`ICatchPredictor`, `ICatchArmSender` implementations); input-controls epic (G1 latch consumer)
