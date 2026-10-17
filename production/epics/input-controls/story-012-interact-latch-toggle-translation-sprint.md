# Story 012: Interact — the presented-target latch, Toggle-mode translation (G7), Sprint Hold / Toggle / Auto and F8 unlatch

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R3 grammar table, G1, G2 router-origin causes, G7 Toggle mode, R4.2–R4.4, F8 sprint unlatch, R6 Sprint row; ACs H-G7-1 router half, H-G7-3 sprint half, H-F8-1, H-R4-4 / H-R4-6 router halves, H-G1-1 router half, H-A11Y-1 router half)
**Requirement**: `TR-input-004` (per-press machine incl. latched target and ordered edges); `TR-input-010` (F8 counted in client ticks, never ms)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 Toggle translation, Interact latches the presented snapshot, "Holds must not be timed in the router") — Accepted 2026-10-16 (primary); ADR-0014 (§3 press resolution, §4 hold counter, §7 `IInteractionQuery` / `InteractionSnapshot`) secondary
**ADR Decision Summary**: For a Hold or Channel action in Toggle mode the first press emits a logical `Press` and its physical release emits nothing; the second press emits a logical `Release` (consumed, never re-resolved). Back on a running toggled hold emits `Cancel(Back)` (step 3b). Sprint toggles latch the held bit and unlatch on F8 (neutral for ≥ 8 consecutive client ticks read from the ClientLayer-published tick counter), death, mount, swim or panel open. **The Sim sees only logical press / release / cancel — it never branches on a mode.** Interact latches the target from the **presented** snapshot: the press edge carries `(TargetEntity, TargetSlot, PresentedSeq)`; resolution (catch → verb → armed) and hold timing are Sim-side.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0014)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: Toggle modes are translated into logical press / release / cancel in the router; the Sim never branches on a mode; holds are not timed in the router; F8 is counted in client ticks (ADR-0012 §3, Implementation Guidelines)
- Required: the press edge carries the target of the prompt the player saw (G1), read through `IInteractionQuery` — never against a focus that changed after that frame
- Forbidden: re-resolving a toggled hold's second press as a catch (it is consumed as a cancel); the router counting ms for holds
- Guardrail: 0 B

---

## Acceptance Criteria

*From GDD G1, G7, F8 and H-G7-1 / H-G7-3 / H-F8-1 / H-R4-4 / H-R4-6 / H-G1-1, scoped to this story (router half — the hold completing at tick 12 is asserted end to end in story 021):*

- [ ] **Presented-target latch (H-R4-6 router half, H-G1-1 router half).** An Interact press edge is stamped, **at event time**, with `(TargetEntity, TargetSlot, PresentedSeq)` of the **last presented** prompt (`IPresentedPromptSource`), not the live focus: with the last presented prompt "Hold [E] Deliver" on mailbox A and the live focus flipping to a container on the press tick, the edge names A; a later focus change never rewrites an edge already queued. With no presented prompt (first frame) the edge carries `TargetEntity = 0` (the Sim treats it as "no valid target", PC 016).
- [ ] **`IPresentedPromptSource`.** A Client.Core port `{ bool TryGet(out PresentedPrompt p) }` (`AnchorRef`, `Seq`, `InteractMode`) with a `PresentedPromptLatch` implementation that `GameLoop` updates right after `Present()` (story 028). **Unowned seam:** ADR-0014 §7 says the presented snapshot "is tracked by ADR-0016", but ADR-0016 defines no mechanism and HUD has no epic — flagged in EPIC.md; this story supplies the latch.
- [ ] **Toggle translation (H-G7-1 router half).** With the Deliver hold class in Toggle mode and a presented `InteractMode.Hold` target: a 1-tick press emits `Press` at tick 0, the physical release emits **nothing**, and a second press while the snapshot's `InteractState` is `Holding` or `ToggleArmed` emits a `Release` edge (reason `ToggleSecondPress`, consumed, never resolved as a catch) — at tick 8 in the GDD example. A press when the state is `Idle` / `Complete` is a **fresh** `Press`. A second press from **any device** counts as the second press. Tap-mode targets (container, WorldItem) are never toggled: their physical release behaves as in Hold mode. In Hold mode (default) press / release map straight to `Press` / `Release`.
- [ ] **Back cancels a toggled hold (ladder step 3b hook).** The router exposes `TryCancelToggledHold()` used by story 016: while a toggled hold is running it emits `Cancel(Back)` and consumes the press (returns true), otherwise false.
- [ ] **Sprint modes.** Sprint is a held-state action with per-device mode Hold (KBM default) / Toggle (pad default) / Auto. **Hold:** the bit follows the OR of Shift / L3. **Toggle:** a press latches the bit on, the next unlatches; the physical release is ignored; it also unlatches on death (`Life` Dead), mount (`Body` Mounted), swimming and panel open (read from `CarrierStatus`), and on **F8** (below). **Auto (Player Character P14):** the bit is set whenever `|moveIntent| ≥ 0.9` (29 491 Q15, PROPOSED) and the Sprint input walks while held (clears the bit). H-A11Y-1 (sprint class): every verb in the class works under each mode.
- [ ] **H-F8-1.** `unlatch = |moveIntent| == 0 for ≥ ceil(sprintUnlatchSeconds × 30)` consecutive **client ticks** (default 0.25 s → 8): 7 neutral ticks keep the latch, the 8th unlatches; a stick reversal passing neutral for 3 ticks keeps it; a 0.4 s stop (12 ticks) drops it. Counted from `IClientTickSource.ClientTick`, never from ms.
- [ ] **H-R4-4 (router half).** A press with no target (presented prompt empty) still emits a `Press` edge so the Sim can start catch-armed (M0) or nothing; the router itself never starts or times a hold and never fires a verb — a 1-tick press on a Hold target emits `Press`+`Release` and the Sim's cancel / "Hold" pulse cue is asserted in story 020. The router **copies** the Sim's hold lines from snapshot changes into `UiEventLog` in order: `HoldStart` (Idle → Holding), `HoldComplete` (→ Complete), `HoldCancel{reason}` (→ Idle with `LastCancel`), after the raw-input and gate lines of the same press (H-R11-4 ordering clause).

---

## Implementation Notes

*Derived from ADR-0012 §3, ADR-0014 §3–§4, §7 and GDD G7:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.Hold.cs`, `ToggleTranslator.cs`, `SprintMode.cs`, `SprintLatch.cs`, `IPresentedPromptSource.cs`, `PresentedPromptLatch.cs`, `HoldLogMirror.cs` (diff of the last two snapshots → log lines).
- Mode per class comes from `InputSettings.holdMode{Deliver, ThrowAim, Sprint(kbm), Sprint(pad)}` (story 019; defaults here).
- Interact is registered with `Grammar.Hold` + `TargetDriven` (story 005). The router consults the presented `InteractMode`: `Tap` → treat the press as an immediate-fire edge pair (no toggling); `Hold` → hold-class; `Channel` (catch-armed, M0) → hold-class with held semantics.
- A held Interact press is *latched to its action and target* until release (G1): focus changes during the hold do not touch it; the Sim's `InteractionLatch` keeps the anchor (PC 017). The router's own latch is just the edge's target fields.
- Mute interplay: when the router state changes (Panel opens) the Interact machine emits `Cancel(StateChange)`; PC 017 maps it to its cancel reason.
- Do not copy the `holdEff` formula or tick-count anything for holds (`production/epics/player-character/story-011-hold-ticks-holdeff.md` and `story-017-hold-counter-cancels-channel-focus-latch.md` own them).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: Throw aim Toggle (separate rule). Story 015: the one-commit gate. Story 016: the Back ladder itself. Story 017: which device "owns" a hold (H-EC-2). Story 020/021: end-to-end tick assertions through the Sim.
- M0: catch-armed Toggle latch (G7 `catchArmSeconds`, H-G7-4), push-to-talk toggle, mic indicator.
- Player Character 016/017 (resolution, hold counter); Manual Delivery 004 (`ShouldCancel`).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/InteractLatchTests.cs` (`[Trait("Id", "H-R4-6")]`, `H-G1-1`, `H-R4-4`), `tests/unit/input/ToggleTranslationTests.cs` (`[Trait("Id", "H-G7-1")]`), `tests/unit/input/SprintModeTests.cs` (`[Trait("Id", "H-G7-3")]`, `[Trait("Id", "H-F8-1")]`, `[Trait("Id", "H-A11Y-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 008, 009 (`moveIntent` for F8 / Auto), 005; `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md` (`InteractionSnapshot`, `InteractState`, `LastCancel`; test double until Done)
- Unlocks: Stories 015, 016, 017, 020, 021; `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md` and `story-017-hold-counter-cancels-channel-focus-latch.md` (consumers of the edges), `production/epics/manual-delivery/story-004-press-latch-cancels-request.md`
