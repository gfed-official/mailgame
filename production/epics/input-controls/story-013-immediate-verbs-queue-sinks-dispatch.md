# Story 013: Immediate verbs — `ImmediateAction` queue, `IImmediateActionSink` registry and `DispatchImmediates`

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R1 delivery `Immediate`, R11 "What bypasses the frame", G5 consumed press, G8; ACs H-R11-2 router half, H-G5-1, H-G8-1 immediate taps, H-R6-2 (b)(c) logic halves; H-R10-9 is story 021)
**Requirement**: `TR-input-013` (server-validated verbs bypass the frame as reliable requests; no hold-duration field) — router side; the wire side is `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§6 Immediate verbs, Alternative 4 "Immediate requests sent from inside `_Input`" rejected) — Accepted 2026-10-16 (primary); ADR-0011 (same `ClientAdapter` codec) secondary
**ADR Decision Summary**: On an `Immediate` action's firing edge the router appends an `ImmediateAction { ActionId, EdgeKind, ArrivalMs, RouterStateId, PresentedSeq }` to a preallocated queue. `GameLoop._Process` calls `InputRouter.DispatchImmediates()` immediately before `FrameRunner.RunFrame()`; dispatch invokes the registered `IImmediateActionSink` for the action (the verb owner's client code: Throwing's release builder, inventory ops, ping, chat). The sink builds its request from ClientLayer's published state and sends it through `ClientAdapter` → loopback; `RunFrame` step 1 (`DrainInbound`) then enqueues it **in the same frame**. The router never builds payloads and knows nothing of throw directions or op layouts. Bypass requests are logged with their issuing tick.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: Because dispatch is explicit in `GameLoop._Process`, "sent on the event's frame" does not depend on whether Godot delivers `_Input` before `_Process` (spike item 2, story 001).

**Control Manifest Rules (Foundation)**:
- Required: immediate verbs are dispatched by `GameLoop` before `RunFrame` in their event's frame, through the same `ClientAdapter` codec; the router never builds payloads (ADR-0012 §6; manifest Input router core)
- Forbidden: sending requests from inside `_Input`; the router referencing op layouts or the `ClientAdapter`; a lost release / cancel
- Guardrail: 0 B; the queue is preallocated

---

## Acceptance Criteria

*From GDD R11, G5, G8 and H-R11-2 / H-G5-1, scoped to this story:*

- [ ] **Queue and dispatch (H-R11-2 router half).** `ImmediateAction { ActionId, EdgeKind, ArrivalMs, RouterStateId, PresentedSeq, Flags }` (`Flags` additive to ADR-0012's field list: `PlaceOnRelease` from F7) is appended at the firing edge during `Feed`; `DispatchImmediates()` invokes each action's registered `IImmediateActionSink.OnImmediate(in ImmediateAction)` in arrival order and empties the queue; a record fed in frame F and dispatched in frame F reaches the sink in frame F (test: `Feed` → `DispatchImmediates` → sink called once, before any flush). Capacity 64; if full, the newest Press is dropped (counted, logged), a Release / Cancel never.
- [ ] **Sinks.** `RegisterSink(ActionId, IImmediateActionSink)`; a missing sink logs `Dropped(no_sink)` and counts — it never throws and never blocks other actions. Sinks are registered by the verb owners at boot (Grid Inventory panel ops, Throwing's release builder, camera rig / host for local verbs); this story ships null-object sinks and a `RecordingImmediateSink` for tests.
- [ ] **Which actions are Immediate (M-1a).** Pick / place (LMB click-or-drag, Enter, A), Rotate, Return (Back ladder step 4 and RMB in Holding-open), Drop, Quick-move (chord), Throw commit (story 014), and the **local** verbs Pause, Shoulder swap and Hotbar slot 1–8. Each fires on its grammar's firing edge: Tap on press; Pick/place on press (lift) and on release (place) with `PlaceOnRelease` from `ClickOrDragResolver` (story 010).
- [ ] **H-G8-1 (immediate taps).** Extending story 007's `[Theory]`: for every `Immediate` Tap action in the M-1a map, press at tick 0 / release at tick 30 queues exactly one `ImmediateAction` at the press and nothing at the release.
- [ ] **H-G5-1 (consumed press).** Holding-open, RMB held for 2 s → exactly **one** `Return` immediate; no aim, no Throw immediate (Throw is not live there); the RMB machine is latched to Cancel until release and its release is swallowed. The same holds for any fired immediate: its follow-through cannot start a second verb.
- [ ] **H-R6-2 (b)(c) logic halves.** (b) Shift+LMB in Panel → Quick-move immediate and the carrier still walks (walk-speed `moveIntent` present). (c) RMB in Holding-closed → a `PutAwayFirst` cue (log `Cue`, no immediate, no `Return`) and LT in Holding-closed the same with the pad wording id; RMB in Holding-open → `Return`.
- [ ] **Issuing tick logged.** Every dispatched immediate writes a `UiEventLog` line with the issuing client tick (`IClientTickSource.ClientTick`), so the replay corpus (story 022, TR-input-021) includes bypass requests.

---

## Implementation Notes

*Derived from ADR-0012 §6:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `ImmediateAction.cs`, `IImmediateActionSink.cs`, `ImmediateQueue.cs`, `InputRouter.Immediate.cs`, `NullImmediateSink.cs`.
- The sinks live with the verb owners: Grid Inventory's panel-interaction client code (`production/epics/grid-inventory/story-024-panel-interactions-feedback.md`, Blocked) for pick / place / rotate / return / drop / quick-move — using `production/epics/grid-inventory/story-004-op-kinds-payloads-reasons.md` op kinds and `story-007-pick-move-ops.md`, `story-010-quickmove-return-targets.md`, `story-020-drop-op.md` handlers on the host side; Throwing's release builder (`production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md`) for Throw. The sink builds the request and calls `ClientAdapter` (`production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`); the router never does.
- **Local verbs** (Pause, Shoulder swap, Hotbar slot) use the same mechanism with sinks supplied by the host shell / camera rig; they have no request. The consumer of Hotbar slot / `HotbarStep` — the selected-slot state — is an **unowned seam** (EPIC.md; Throwing's EPIC lists it as seam 1).
- Verb owners' sinks must be 0 B; the router passes `in` structs and no delegates are allocated per call (sinks are interface references fetched from an array).
- `DispatchImmediates()` is called by `GameLoop` before `FlushToLatch` and `RunFrame` (story 028); it is not called from `Feed`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014 (Throw commit decision and aim), 015 (`CommitSent` / one-commit), 016 (Back ladder invoking `Return`), 028 (the call sites). The request builders and ops (Grid Inventory, Throwing). H-R10-9 (no hold-duration / tick fields in the payload schemas) — story 021.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/ImmediateQueueTests.cs` (`[Trait("Id", "H-R11-2")]`, `[Trait("Id", "H-G5-1")]`, `[Trait("Id", "H-G8-1")]`), `tests/unit/input/ImmediateBindingRulingsTests.cs` (`[Trait("Id", "H-R6-2")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 008, 010 (`placeOnRelease`), 005; `production/epics/simulation-core/story-013-op-reply-types-descriptors.md` (op / reply types the sinks use — consumed by the owners, not by the router)
- Unlocks: Stories 014, 015, 016, 021, 028; `production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md` and `production/epics/grid-inventory/story-024-panel-interactions-feedback.md` (sink registration)
