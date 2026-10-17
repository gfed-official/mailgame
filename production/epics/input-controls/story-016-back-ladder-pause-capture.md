# Story 016: The Back ladder (R5), the `no_room` skip window (F13) and the Pause capture state

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R5 The Back ladder, F13 Back skip window, R2 layering rule 1 + capture Pause, States and Transitions "any non-capture → Pause", Edge Cases "Back hits `no_room`"; ACs H-R5-1, H-R5-2, H-F13-1, H-R6-2 (a) Menu half)
**Requirement**: `TR-input-014` (MP pause is a local overlay, never `SceneTree.Paused`; the Sim keeps ticking — the M-1a router half: Pause is a capture state that zeroes intent and never touches the tree; the multiplayer behaviour is M0); the ladder itself has no TR-ID in the registry — cited by GDD R5 / ACs H-R5-1, H-R5-2, H-F13-1
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 "Back ladder (R5) is one router action with ordered handlers; the first applicable handler consumes the press", §7 focus / pause) — Accepted 2026-10-16 (primary); ADR-0002 (§4 `TickDriver.Pause`; `SceneTree.Paused` never) secondary
**ADR Decision Summary**: Back is a single router-owned action with ordered handlers; one press, one step. Pause is a `capture` layer value: it swallows every game action (push-to-talk excepted, M0), zeroes the local player's movement intent and flushes unconsumed edges; solo pause goes through `TickDriver.Pause()` (requested from the host shell), multiplayer pause is a local overlay and the Sim keeps ticking. `SceneTree.Paused` is never used.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0002)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None in the router; `TickDriver.Pause()` is called by `GameLoop` in response to a `PauseRequested` signal (story 028). `tools/ci/check-no-scenetree-paused.sh` already guards the Sim and `src/Game/Hosting`.

**Control Manifest Rules (Foundation)**:
- Required: Pause is a `TickDriver` state only (solo); multiplayer never pauses the Sim; entering any capture flushes unconsumed edges and mutes held buttons (ADR-0002 §4, GDD R2 rule 1)
- Forbidden: `SceneTree.Paused`; Back dropping the held entry; B ever pausing (gamepad pause is Menu)
- Guardrail: 0 B

---

## Acceptance Criteria

*From GDD R5, F13 and H-R5-1 / H-R5-2 / H-F13-1, scoped to this story (ladder steps 3, 3b, 4, 5, 6; step 1 TextEntry (M0) and step 2 action menu / stepper (M1) are declared slots with no handler):*

- [ ] **H-R5-1 (parametrised, M-1a rows).** One Back press with: (b) **aiming** → the aim cancels with no throw (`Cancel(Back)` edge, `TryCancelAim()` from story 014); (c) **Holding-open with `return` ok** → a `Return` immediate is queued and the panel stays open; (d) **Panel** (not holding) → the panel closes; (e) **World** → Esc opens Pause, B does nothing; **Menu**, and **P** on KBM, open Pause directly from every non-capture state. (Row (a) action menu is M1.) Step 3b: a running Toggle-mode hold → `TryCancelToggledHold()` (story 012) emits `Cancel(Back)`, the press is consumed and does **not** open Pause (a fresh Back is needed).
- [ ] **Step 4 and the `no_room` skip (F13).** Holding-closed, Back → a `Return` immediate; when the grid's reply reports `no_room` (`InputRouter.OnReturnResult(ReturnResult.NoRoom)` — see Implementation Notes) the press **stops there**: a `Cue(NoRoomPutAway)` is logged with the variant `EscAgain` (KBM) or `MenuPause` (pad), the entry stays held, and while the message is visible a **second Back skips step 4** and continues to step 5 / 6 with the entry still held. **H-F13-1:** Esc → message with "[Esc] again to skip"; Esc again 1.9 s later → Pause opens, the Large stays on the cursor; after a WASD press clears the message (and ≥ 2.0 s has passed), Esc shows the message again; the skip window is at least `backSkipMs` (default 2000; 1500–4000), ends on the next non-Back input or any router-state change, and is measured on the ms clock. **Pad:** B shows the message naming [Menu] and a second B does **nothing** (B has no step after 4 in Holding-closed); Menu opens Pause.
- [ ] **H-R5-2.** Across every H-R5-1 and H-F13-1 fixture, Back issues **zero** `Drop` immediates; Back never drops the held entry.
- [ ] **Step 5.** Back closes the panel (Panel → World, Holding-open → Holding-closed with the cursor entry kept); in Build (M2) it would deselect the tool, then exit Build — the slot is declared, no handler.
- [ ] **Pause capture.** Entering Pause (Esc at the end of the ladder, P, Menu) sets `capture = Pause`: every held press is muted (hold / aim machines emit `Cancel(StateChange)` first), unconsumed edges are flushed, `moveIntent` is zeroed, `Flags.Paused` is set on the next `SetState`, and the router raises `PauseRequested(solo)` for the host to call `TickDriver.Pause()`; Esc / B / Menu / "Resume" exits (`ResumeRequested`), after which held buttons stay muted until released (G3). While in Pause no game action fires (push-to-talk is M0). **Pause during aiming cancels with no throw and resuming never throws** (H-EC-7 Pause-while-aiming clause, router half); the multiplayer overlay variant (`Sim keeps ticking with neutral frames`) is M0 and untested here — the router must not assume solo.
- [ ] **H-R6-2 (a) Menu half.** Menu in World → Pause only (no chat / emote action exists at M-1a); P pauses directly and is remappable (a flag, not a code branch).

---

## Implementation Notes

*Derived from ADR-0012 §3 and GDD R5, F13:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `BackLadder.cs` (ordered `IBackHandler[]` of fixed size — no LINQ, no allocation), `InputRouter.Back.cs`, `PauseState.cs`, `IPauseRequestSink.cs` (`RequestPause(bool solo)`, `RequestResume()`), `ReturnResult.cs` {`Ok, NoRoom, Other`}.
- **`no_room` source (unowned seam, EPIC.md):** the `Return` op's reply (`no_room`) arrives in the Grid Inventory sink / reply path. Define `InputRouter.OnReturnResult(ReturnResult)` and have `production/epics/grid-inventory/story-024-panel-interactions-feedback.md` (Blocked) call it from its reply handling; until then tests call it directly. The message text and the "[Esc] again" / "· [Menu] Pause" wording are HUD / Grid's (Visual/Audio rows).
- `ReplyObserved` (story 015) is **not** the carrier of `no_room`; keep the two ports separate.
- P (KBM) and Menu (pad) are separate actions from Back, both reserved-or-remappable per story 005's flags.
- The solo pause itself is `TickDriver.Pause()` (ADR-0002 §4) — allowed only when Networking reports no remote peers; the router only *requests*.
- Do not implement the TextEntry cancel (step 1) or action-menu close (step 2): register empty handler slots so M0 / M1 add rows without reordering.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 018: focus-loss auto-pause. Story 028: calling `TickDriver.Pause` / `Resume`. M0: MP overlay + `H-R2-2`, TextEntry, push-to-talk under Pause. M1: action menu (R7), History, Pause › Settings screen.
- Prompt/message rendering (HUD).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/BackLadderTests.cs` (`[Trait("Id", "H-R5-1")]`, `H-R5-2`, `H-F13-1`, `H-R6-2`), `tests/unit/input/PauseCaptureTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 008, 012 (`TryCancelToggledHold`), 013 (`Return` immediate), 014 (`TryCancelAim`), 015 (commit state ignored by Back), 007
- Unlocks: Stories 018, 021, 028; `production/epics/grid-inventory/story-024-panel-interactions-feedback.md` (`OnReturnResult`)
