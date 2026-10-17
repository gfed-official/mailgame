# Story 017: Hold counter — `heldTicks = tick − PressTick`, completion, the cancel list, catch channel and the focus latch

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
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

- [ ] **PCM-P12-1 (ADR convention).** a 400 ms hold (12 ticks) pressed on tick 0 sends exactly one request on **tick 12**, none on tick 11; a release edge on tick 12 (processed before accrual) cancels with no request; **hold-as-toggle** (logical press at tick 0, the router's second press arrives as a cancel/release edge) produces the identical request on tick 12 and a second press before then cancels. The request tick equals `PressTick + Hold.HoldTicks` for every `holdEff` row of the shared vector table (PCM-F4-2).
- [ ] **H-R3-1 (Hold row).** releases landing on ticks 1, 6, 11, 12 cancel (no request); releases on ticks 13 and 60 arrive after the request was sent on tick 12 and change nothing; a press and its release in the same frame (a 1-tick press, H-R4-4) sends nothing and reports `LastCancel = Released`.
- [ ] **PCM-P12-2 (parametrised).** with a hold in progress each of these cancels it **with no request** and records its reason: release; chest→anchor distance 2901 mm (**2900 mm keeps it** — strict `>` on squared integers; H-F2-1); the target becoming `IsAvailable == false`; the anchor being unregistered; `ShouldCancel` returning a reason (that reason is reported unchanged); context change into Panel, Mounted or Dead (`InteractionGate.ContextCancelReason`, Story 013); entering swim depth; the carrier's knockdown (life state leaves `Alive`). **Walking at full speed plus 10 points of non-lethal damage does not cancel it** (damage is Health & Damage's; the test drives carrier movement and an unrelated event and asserts the hold survives).
- [ ] **H-G1-1.** a deliver hold latched on mailbox A, focus moves to mailbox B at tick 6 → tick 12 sends the request for A (the latch's `AnchorRef` and `OwnerPayload`), and the published latched anchor names A throughout.
- [ ] **PCM-P11-2c.** during a hold or while toggle-armed, a challenger leading for 30 ticks and a 1-tick occlusion of the incumbent never change focus (`FocusState.Latched` set while state ∈ {Holding, Channel, ToggleArmed}; cleared on Complete / cancel).
- [ ] **PCM-P11-2b (completion half).** a thrown item entering the catch window at tick 6 of a latched deliver hold does not convert it: the request is sent on tick 12 and no `CatchArm` is requested.
- [ ] **One commit.** After `Complete`, further ticks never send a second request (state stays Complete until the release edge returns it to Idle); the request is built with `TryBuildRequest(latch, ctx, ref op)` using the latched `HoldTicks`/`OwnerPayload`/`ClaimedPosition`, and `IRequestSink.TrySend` false (busy) yields `LastCancel = Busy`, Idle, and **no retry**.
- [ ] **Catch channel (the only Channel use at M-1a).** A catch-armed or Catch press holds Channel state from press to release: `ICatchArmSender.Send(down: true)` on start and `Send(down: false)` on the release / cancel edge; being Channel locks focus like a hold; a Catch channel is cancelled by the same context / swim / knockdown conditions; a latched Deliver hold ignores `catchPredicted`.
- [ ] **Counters.** `holdsStarted`, `holdsCompleted`, `holdsCancelled[reason]` exist in `SimCounters`; the step allocates 0 B over 10 000 mixed press/hold/cancel sequences (seeded PCG fuzz) and a fuzz never produces two requests for one press.

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 011 (`HoldTicks`), 013 (`InteractionGate.ContextCancelReason`), 014 (`FocusState.Latched`), 015 (latched occlusion), 016 (latch, ports, `EdgeResolveStep`), 009 (`Loco == Swimming`)
- Unlocks: Story 018; manual-delivery epic (Deliver hold end to end, MD4 hooks); input-controls (cancel-range / toggle ACs against the Sim)
