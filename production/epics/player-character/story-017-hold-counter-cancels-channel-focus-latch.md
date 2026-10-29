# Story 017: Hold counter — `heldTicks = tick − PressTick`, completion, the cancel list, catch channel and the focus latch

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (7 of 9).** The one place a hold is timed (owner Sim ticks, no host timer). Manual Delivery's `ShouldCancel` / `GetHoldTicks` hooks and Input & Controls' cancel-range / toggle behaviour are exercised through here.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P12 press, hold, channel — **counting convention superseded by ADR-0014, see below**; P11 "focus is latched while Interacting"; Game Feel "holds feel committed but forgiving"); `design/gdd/input-controls.md` (R3, R11, F2 `holdCancelRange`, H-R3-1, H-F2-1, H-G1-1, H-R4-4)
**Requirement**: `TR-movement-012` (hold counter in owner Sim ticks; one request at the held tick; no host hold timer), `TR-movement-011` (focus latched while interacting); serves `TR-input-012` (cancel range 2900 mm, hysteresis = `incumbentSlackMm`), registry-owned by input-controls
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P12-1, PCM-P12-2, PCM-P11-2c, H-R3-1 (Hold row), H-F2-1, H-G1-1.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§4 the hold counter, cancels; §3 release/cancel edges) — Accepted 2026-10-16 (primary); ADR-0016 (§2 `PreviewSubStep.HoldAdvance`) secondary — Accepted
**ADR Decision Summary**: Counting convention (decided in ADR-0014): `heldTicks = tick − PressTick`, evaluated after the tick's edges; the hold **completes on tick `PressTick + HoldTicks`** — a 12-tick hold pressed on tick 0 sends on tick 12 and a release edge on tick 12 (processed first) cancels. **This supersedes GDD PCM-P12-1's "press tick = held tick 1" (completion one tick earlier).** On completion exactly one request is built (`TryBuildRequest`) and sent; the hold enters `Complete` and never repeats (G6); no request carries a duration and the host has no hold timer. Cancels are checked every held tick up to and including the completion tick (after edges, before completion): release / cancel edge; chest→anchor distance² **strictly greater** than `(RangeMm + incumbentSlackMm)²` (2900 continues, 2901 cancels); anchor unregistered or `IsAvailable` false; `ShouldCancel(latch)` returning a reason; context change into Panel, Mounted or Dead; swim depth; knockdown. Walking and non-lethal damage never cancel. Each cancel records its reason in the snapshot.
**ADR Version**: ADR-0014 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: holds are counted only at `PreviewSubStep.HoldAdvance` in owner Sim ticks, with `heldTicks = tick − PressTick`; edges are processed before accrual; the cancel range is `RangeMm + incumbentSlackMm`, compared strictly on squared distances; the host never times a hold or receives a duration (ADR-0014 §4)
- Forbidden: re-targeting a hold to the current focus; timing holds on the host (ADR-0014 Alts 1, 3)

---

## Acceptance Criteria

*From GDD PCM-P12-1/2, PCM-P11-2c and input-controls H-R3-1 (Hold row), H-F2-1, H-G1-1, H-R4-4, scoped to this story. The GDD's PCM-P12-1 wording ("held tick 12", completing one tick earlier) is replaced by the ADR-0014 convention below — recorded as GDD Impact in ADR-0014; do not "fix" the code to the GDD text.*

- [x] **PCM-P12-1 (ADR convention).** a 400 ms hold (12 ticks) pressed on tick 0 sends exactly one request on **tick 12**, none on tick 11; a release edge on tick 12 (processed before accrual) cancels with no request; **hold-as-toggle** (logical press at tick 0, the router's second press arrives as a cancel/release edge) produces the identical request on tick 12 and a second press before then cancels. The request tick equals `PressTick + Hold.HoldTicks` for every `holdEff` row of the shared vector table (PCM-F4-2).
- [x] **H-R3-1 (Hold row).** releases landing on ticks 1, 6, 11, 12 cancel (no request); releases on ticks 13 and 60 arrive after the request was sent on tick 12 and change nothing; a press and its release in the same frame (a 1-tick press, H-R4-4) sends nothing and reports `LastCancel = Released`.
- [x] **PCM-P12-2 (parametrised).** with a hold in progress each of these cancels it **with no request** and records its reason: release; chest→anchor distance 2901 mm (**2900 mm keeps it** — strict `>` on squared integers; H-F2-1); the target becoming `IsAvailable == false`; the anchor being unregistered; `ShouldCancel` returning a reason (that reason is reported unchanged); context change into Panel, Mounted or Dead (`InteractionGate.ContextCancelReason`, Story 013); entering swim depth; the carrier's knockdown (life state leaves `Alive`). **Walking at full speed plus 10 points of non-lethal damage does not cancel it** (damage is Health & Damage's; the test drives carrier movement and an unrelated event and asserts the hold survives).
- [x] **H-G1-1.** a deliver hold latched on mailbox A, focus moves to mailbox B at tick 6 → tick 12 sends the request for A (the latch's `AnchorRef` and `OwnerPayload`), and the published latched anchor names A throughout.
- [x] **PCM-P11-2c.** during a hold or while toggle-armed, a challenger leading for 30 ticks and a 1-tick occlusion of the incumbent never change focus (`FocusState.Latched` set while state ∈ {Holding, Channel, ToggleArmed}; cleared on Complete / cancel).
- [x] **PCM-P11-2b (completion half).** a thrown item entering the catch window at tick 6 of a latched deliver hold does not convert it: the request is sent on tick 12 and no `CatchArm` is requested.
- [x] **One commit.** After `Complete`, further ticks never send a second request (state stays Complete until the release edge returns it to Idle); the request is built with `TryBuildRequest(latch, ctx, ref op)` using the latched `HoldTicks`/`OwnerPayload`/`ClaimedPosition`, and `IRequestSink.TrySend` false (busy) yields `LastCancel = Busy`, Idle, and **no retry**.
- [x] **Catch channel (the only Channel use at M-1a).** A catch-armed or Catch press holds Channel state from press to release: `ICatchArmSender.Send(down: true)` on start and `Send(down: false)` on the release / cancel edge; being Channel locks focus like a hold; a Catch channel is cancelled by the same context / swim / knockdown conditions; a latched Deliver hold ignores `catchPredicted`.
- [x] **Counters.** `holdsStarted`, `holdsCompleted`, `holdsCancelled[reason]` exist in `SimCounters`; the step allocates 0 B over 10 000 mixed press/hold/cancel sequences (seeded PCG fuzz) and a fuzz never produces two requests for one press.

---

## Implementation Notes

*Derived from ADR-0014 §3–§4:*

- Files in `src/PerformativeMail.Sim/Players/Client/`: `HoldAdvanceStep.cs` (`IClientStep` bound at `PreviewSubStep.HoldAdvance`), `HoldCounter.cs` (pure state transition: `(state, tick, edges, ctx) → (state', action)`), `CancelReason.cs` (closed enum incl. `Released, OutOfRange, Unavailable, Unregistered, OwnerCancel(StringId), ContextChanged, SwimDepth, Knockdown, Busy`). Release/Cancel edges are read from the same `InputFrame.Edges` as Story 016 but processed *here*, after press resolution and before accrual — keep the two steps' edge cursor consistent (shared `EdgeCursor` in `ClientStepContext` or a per-tick scratch).
- Per held tick, in this order: (1) apply this tick's release/cancel edges; (2) evaluate cancel conditions: distance² via chest point of the *current* `CarrierState` vs `(RangeMm + incumbentSlackMm)²` in `long`, then registry/availability, `ShouldCancel`, context/swim/life; (3) if not cancelled and `tick == PressTick + HoldTicks` → complete (build + send exactly once); (4) otherwise leave Holding. Progress = `Hold.ProgressPm(tick − PressTick, HoldTicks)` published by Story 018.
- Channel anchors owned by other systems (Belt `Push`, M-1b) need a start/stop request builder that `IInteractable` (ADR-0014 Key Interfaces) does not declare — **ADR gap flagged**: at M-1a only the Catch channel (through `ICatchArmSender`) is implemented; a Channel-mode anchor press latches the state but sends nothing, with a `SimCheckRing` record. File an ADR-0014 amendment before Push is implemented.
- Busy sink: ADR-0014 is silent on a `TrySend == false` at completion; the conservative choice (cancel with `Busy`, no retry, so one press never double-sends) is recorded in Last-Updated and in the decision log.
- `SwimDepth` cancel uses `CarrierState.Loco == Swimming` (Story 009), not a depth re-sample.
- Hold-ring drain at 2× on cancel is a Presentation effect driven by `LastCancel` (Story 026 / HUD), not Sim state.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 016: press resolution and latching. Story 018: the `InteractionSnapshot` and `IInteractionQuery`. Story 019: host validation.
- Anchor-owned Channel verbs (Push, M-1b); the Deliver `ShouldCancel` rules themselves (Manual Delivery MD4); Quick Hands `insertPm` (Manual Delivery supplies it to `GetHoldTicks`).
- The router's toggle machine and one-commit gate (input-controls epic).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/HoldCounterTests.cs` (`[Trait("Id", "PCM-P12-1")]`, `PCM-P12-2`, `H-R3-1`, `H-F2-1`, `H-G1-1`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created 2026-10-29 (dev-story, branch `story/player-character-017`; built to Mail Lead's readiness fixes in docs PR branch `docs/pc017-md006-pc019-readiness` and the five TD-186 defaults, Approved by Evan 2026-10-29 7:41 AM PT: "Approve all five TD-186 defaults for player-character 017"). Full legs 2026-10-29 (rebased onto main aa89548, PR #58 merged): net10.0 Debug 5,698 total, 5,633 passed, 0 failed, 65 skipped; net8.0 Release 5,698 total, 5,691 passed, 0 failed, 7 skipped. Per class, net10.0 Debug: `HoldCounterTests` 42/42, `HoldAdvanceAllocationTests` 1 skipped (Debug; passes on net8.0 Release, 1/1), `PressResolutionTests` 44/44, `SimCountersTests` 14/14, `InteractionGateTests` 65/65.
- `tests/unit/player/HoldCounterTests.cs` — `HoldCounterTests` (42 cases, on the PressRig):
  - PCM-P12-1 (`[Trait("Id", "PCM-P12-1")]`, `PC017-ONE-COMMIT`): `Hold_PressedOnTick0_SendsOneRequestOnTick12_NoneOnTick11_AndCompleteNeverSendsAgain`; `Release_BeforeOrOnTheCompletionTick_Cancels_AfterItChangesNothing` (7 rows, the toggle second press as a logical Release included; also `H-R3-1`); `RequestTick_IsPressTickPlusHoldTicks_ForEveryHoldEffRow` (7 `holdEff` rows; `PCM-F4-2`)
  - H-R3-1 / H-R4-4 (`H-R3-1`, `H-R4-4`): the release theory above (ticks 1, 6, 11, 12 cancel; 13 and 60 change nothing) and `PressAndReleaseInOneFrame_SendsNothing_AndReportsReleased`
  - PCM-P12-2 / H-F2-1 (`PCM-P12-2`, `H-F2-1`): `HoldInProgress_EachCancelEndsItWithNoRequestAndItsReason_WalkingDoesNot` (12 rows: release, 2901 mm cancels, 2900 mm keeps, `IsAvailable` false, unregistered, `ShouldCancel`, Panel, Mounted, Dead, swim depth, knockdown, walking at full speed with life state `Alive` keeps it)
  - H-G1-1 (`H-G1-1`, `PCM-P11-2c`): `HoldLatchedOnA_FocusChallengerBFromTick6_SendsForA_AndFocusStaysOnA`
  - PCM-P11-2c (`PCM-P11-2c`): the H-G1-1 test and `Channel_LatchesFocusFromPressToRelease_AChallengerLeadingFor30TicksNeverTakesIt` (`FocusState.Latched` set while Holding or Channel, cleared on Complete / cancel); the 1-tick occlusion half stays pinned by `FocusOcclusionTests` (`Step_LatchedIncumbentOccludedForOneTick_KeepsFocusAndSkipsTheTest`) with `Latched` driven directly
  - PCM-P11-2b (`PCM-P11-2b`): `CatchEnteringTheWindowAtTick6_OfALatchedDeliverHold_DoesNotConvertIt`
  - One commit (`PC017-ONE-COMMIT`): `Completion_BuildsFromTheLatch_ClaimIsThePressFeet_NotTheCompletionFeet`, `RefusedSendAtCompletion_IsBusy_Idle_AndNeverRetried` (TD-186 item 2), `LastCancel_ClearsOnTheNextLatch` (TD-186 item 5), and `PressResolutionTests.PressReleasePress_InOneFrame_EndsTheHoldBeforeTheSecondPressResolves` (2 rows; `H-R4-6`; TD-171 A9, TD-186 item 1)
  - Catch channel (`PC017-CATCH-CHANNEL`): `CatchChannel_SendsDownOnStart_AndUpOnTheReleaseOrAContextCancel` (5 rows), `RefusedCatchArmDown_NeverEntersChannel_SoTheReleaseOwesNoUp`, `ChannelAnchorPress_AtM1a_LatchesSendsNothing_AndRecordsChannelAnchorNoBuilder` (`CheckCode.ChannelAnchorNoBuilder = 16`, TD-186 item 3)
  - Counters (`PC017-COUNTERS`, `PC017-ONE-COMMIT`): `Fuzz_10000MixedPresses_NeverSendTwiceForOnePress_AndTheCountersBalance` (seeded PCG; flat `HoldsStarted`/`HoldsCompleted`/`HoldsCancelled` = 58/59/60, TD-186 item 4)
- `tests/unit/player/HoldAdvanceAllocationTests.cs` (`PC017-COUNTERS`): `Step_Over10000FuzzedPresses_AllocatesZeroBytesAfterWarmUp` — 0 B, advisory until M0 (ADR-0014 erratum E2); skipped on Debug, passes on Release
- Edited pins: `PressResolutionTests.OnlyInteractPressEdgesResolve_ReleaseCancelAndOtherActionsResolveNothing` gains the Hold row (`PCM-P12-2`); `SimCountersTests` length 58 → 61 and the 58–60 id pins; `InteractionGateTests` `CancelReason` value pins 6–11; `ClientLayerRulesTests` covers `HoldAdvanceStep`. `EdgeResolveAllocationTests` (0 B) and the `PressResolutionTests` `Latched == false` pin stay green.
- A mutation check during dev-story (the strict `>` range compare flipped; the 4d release handling removed) failed 19 tests, including the 2900 mm keep row and the A9 case.
- Eight further as-built calls are labelled 'Mail Dev default, pending Evan (TD-191 item N)' (register row TD-191, open).

---

## Dependencies

- Depends on: Stories 011 (`HoldTicks`), 013 (`InteractionGate.ContextCancelReason`), 014 (`FocusState.Latched`), 015 (latched occlusion), 016 (latch, ports, `EdgeResolveStep`), 009 (`Loco == Swimming`)
- Unlocks: Story 018; manual-delivery epic (Deliver hold end to end, MD4 hooks); input-controls (cancel-range / toggle ACs against the Sim)
