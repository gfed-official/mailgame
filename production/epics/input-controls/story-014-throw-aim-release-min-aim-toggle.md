# Story 014: Throw aim-release — router-owned `aiming`, F11 minimum aim on the ms clock, Toggle aim, aim-start refusals and the Throw commit

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (R3 Aim-release grammar, G1 "For Throw, the thrown hotbar entry is latched at aim start", G2 aim cancels, G7 aim Toggle, F11 minimum aim, R2 `aiming` layer, collision ruling 9; ACs H-F11-1, H-G7-2, H-G1-2, H-EC-14 throw half, H-R11-2 throw clause)
**Requirement**: `TR-input-004` (per-press machine — Aiming state, latch, ordered edges) and `TR-input-010` (aim measured on the ms clock, Sim never sees ms); Throw as an `Immediate` verb per `TR-input-013`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 "Aim-release (Throw) is router-owned on the ms clock (F11)", Toggle aim, §6 immediate dispatch) — Accepted 2026-10-16
**ADR Decision Summary**: Release with `aimHeld ≥ throwMinAim` commits; earlier, or a hitch-batched press + release with `aimHeld ≈ 0`, cancels with "Hold to aim" and the Sim is never asked. For Throw aim in Toggle mode the first press enters `aiming`, the second commits if `aimHeld ≥ throwMinAim`, else it is ignored (aiming continues, no timeout); Back cancels. The commit is an `Immediate` action: `DispatchImmediates()` calls Throwing's `IImmediateActionSink` in the event's frame. The router never builds the payload.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: Arrival stamps are frame-granular (no `InputEvent` timestamp, GDD Formulas → Clocks): a press and a release batched into one frame by a hitch measure `aimHeld ≈ 0` and cancel — "the aim could not be measured, so nothing commits".

**Control Manifest Rules (Foundation)**:
- Required: Throw aim (F11) and UI timing use the ms clock; the Sim never receives milliseconds; `aimHeld` does **not** scale with `holdSetting` (ADR-0012 Implementation Guidelines; GDD F11)
- Required: a commit is an Immediate verb dispatched before `RunFrame` in the same frame; a cancelled aim never throws
- Forbidden: the router knowing throw directions or payload layouts; any target-aware assist (F10 invariant)
- Guardrail: 0 B

---

## Acceptance Criteria

*From GDD F11, G1, G7 and H-F11-1 / H-G7-2 / H-G1-2 / H-EC-14, scoped to this story:*

- [ ] **Enter aiming.** A Throw press while live (World: OnFoot, no overlay, no capture, not holding, not already aiming, no commit in progress/pending — story 015) sets `aiming` (state tuple), sets the `Aim` held bit, emits an `Aim` Press edge and logs `Transition`. The Throw input is RMB (KBM) or LT through F4 hysteresis (story 009).
- [ ] **Aim-start refusals.** Through `IThrowAimView` (a Client.Core port over Throwing's `ThrowPreviewState` from `production/epics/throwing-catching/story-004-throw-aim-quantiser-and-preview-step.md`: `NotThrowable` {None, HandsSlot, EmptySlot, Swimming, …} and `EntryStale`), a Throw press with `NotThrowable ≠ None` starts **no** aim (no state change, no edge), logs a `Cue` carrying the reason id (the string is HUD's) and mutes the press until release (TC-T1-1 b "no aim starts and nothing is sent").
- [ ] **H-F11-1 (Hold mode).** `aimHeld = tRelease − tPress` in ms from arrival stamps: 249 ms → cancel with a `HoldToAim` cue and **no** Throw immediate; 250 ms → Throw immediate queued; 160 ms → cancel; 300 ms → throws; with the setting `throwMinAimMs` = 150, 160 ms throws; at `holdSettingMs` 1000, 250 ms still throws (not scaled); a press and release delivered in the same frame (`aimHeld ≈ 0`) cancels. On release the Aim bit clears and a `Release` edge (commit) or `Cancel(…)` edge (cancel) is emitted so the Sim's `CarrierInput.Aiming` ends.
- [ ] **H-EC-14 (throw half).** Throw held for 10 s throws on release (no timeout; `aimHeld ≥ min` always true).
- [ ] **H-G7-2 (Toggle).** Aim in Toggle mode: the first press enters Aiming with no timeout (still aiming at 30 s of fake time); a second press at 0.20 s is **ignored** (still aiming, no throw, no cancel); a second press at 0.30 s commits; Back cancels (story 016 calls `TryCancelAim()`); the physical release of the first press emits nothing.
- [ ] **H-G1-2 (latch by non-liveness).** While aiming, hotbar slot keys 1–8, D-pad hotbar steps, Interact (E), Tool (LMB) and Drop are **not live** (their predicates exclude `aiming`): feeding each produces no edge / immediate / step, and the release throws the entry that was selected at aim start (the selection cannot change; the Throw immediate carries no slot because Throwing's builder reads the last published preview, `production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md`). Tab / Y while aiming opens the panel and cancels the aim (story 008) — queue no Throw.
- [ ] **G2 router-origin aim cancels.** The aim cancels with **no** throw and a `Cancel(reason)` edge when: Back (story 016); any state change that makes Throw non-live (`StateChange`); focus loss (`FocusLost`, story 018); device disconnect (`DeviceLost`, story 018); `IThrowAimView.EntryStale` becomes true (`EntryStale`, "entry gone or stale"); a commit from another input becomes pending (the Throw press is muted instead, story 015). A cancelled aim's physical release is swallowed.
- [ ] **H-R11-2 (throw clause).** Feeding the release record and calling `DispatchImmediates()` in the same frame invokes Throwing's sink once, before `FlushToLatch`; a cancel invokes it zero times.

---

## Implementation Notes

*Derived from ADR-0012 §3, §6 and GDD F11:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.Aim.cs`, `AimMachine.cs`, `IThrowAimView.cs`, `AimCancelReason` mapping onto `EdgeReason` (story 002).
- `ThrowCommit` is one `ActionDef` with `AimRelease` grammar: the held-bit part is `Frame` (`FrameAction.Aim`), the commit is an `Immediate` follow-up (story 005's representation).
- `throwMinAimMs` default 250 (range 150–400, story 019). It is a twitch guard, **not** a commit hold; it is never scaled by `holdSetting` or stack size.
- After a successful commit the router marks the commit "in progress → pending" via story 015's gate; Throwing calls `CommitSent(reqId, Throw)` after the send (Throwing story 007 AC "One-commit gate hook").
- The arc, the colour and the "Hold to aim" text are Throwing / HUD; this story emits cue ids and edges only.
- The router does not know the selected hotbar slot (unowned seam, EPIC.md); it relies on non-liveness to keep the slot fixed.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 015: Busy / Release-to-retry and `CommitSent` / `ReplyObserved`. Story 016: Back ladder. Story 018: focus-loss / disconnect synthesis. Story 021: end-to-end with the real preview step and builder.
- Throwing: the arc, `ThrowReleaseBuilder`, cooldown, validation (stories 004, 007, 009, 010). F10 aim steadiness (M1). The comms-wheel aim-release grammar (M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/ThrowAimTests.cs` (`[Trait("Id", "H-F11-1")]`, `H-EC-14`, `H-R11-2`), `tests/unit/input/ToggleAimTests.cs` (`[Trait("Id", "H-G7-2")]`), `tests/unit/input/AimLatchTests.cs` (`[Trait("Id", "H-G1-2")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 008, 009 (LT hysteresis), 012 (Toggle machinery shared), 013 (immediate queue); `production/epics/throwing-catching/story-004-throw-aim-quantiser-and-preview-step.md` (`ThrowPreviewState` — a test double suffices to start)
- Unlocks: Stories 015, 016, 018, 021; `production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md` (sink target), `production/epics/throwing-catching/story-005-preview-label-model-f7-money-and-settle.md`
