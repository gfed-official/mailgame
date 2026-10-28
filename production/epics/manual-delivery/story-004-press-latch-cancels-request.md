# Story 004: Press, latch and hold (MD4) and the request (MD5, client side) — settle refusal, `ShouldCancel`, closing grace, `TryBuildRequest`

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD4 press, latch and hold; MD5 the request and the certified promise; closing grace)
**Requirement**: `TR-delivery-006` (press-tick preview recompute; the hold latches `(dest, branch, entry, holdTicks)`), `TR-delivery-007` (`ShouldCancel`/`GetHoldTicks` hooks; a branch or selection change cancels), `TR-delivery-009` (client half: `holdStartTick` in the host domain via `IHostClock`), `TR-delivery-008` (client half: MATCH sends `selectedEntryId = 0`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0014: Interaction Framework and Hold Timing (primary: §3 press resolution and `InteractionLatch` with its 16 B `OwnerPayload`; §4 cancels and completion → `TryBuildRequest`); ADR-0016 (§2 press judged in 4d on this tick's 4b state; §4 latch, and `holdStartTick = IHostClock.EstimatedHostTick` read at latch time)
**ADR Decision Summary**: In `EdgeResolve` (4d) a press on the presented Deliver target judges the owner's branch on this tick (press-tick recompute through the pure hooks). A Hold press latches `{AnchorRef, Verb, Mode, PressTick, HoldTicks, ClaimedPosition (feet at press), OwnerPayload (16 B)}`. A Deliver press starts a hold only if the tracker allowed it, and latches `(destinationId, branch, selectedEntryId + slot, holdTicks)`. `holdStartTick` is `IHostClock.EstimatedHostTick` read at latch time. Holds complete on `PressTick + HoldTicks`. Cancels are checked every held tick up to and including completion, including `ShouldCancel(latch)`. On completion exactly one request is built (`TryBuildRequest`) and sent. No request carries a duration.
**ADR Version**: ADR-0014 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: holds are counted only at `PreviewSubStep.HoldAdvance` in owner Sim ticks; edges before accrual; the host never times a hold or receives a duration (ADR-0014 §4)
- Required: range-gated payloads carry `ClaimedPosition` (12 B) (ADR-0014 §5)
- Forbidden: re-targeting a hold to the current focus (ADR-0014 Alt 3); converting a latched deliver hold into a catch; a predicted count or duration in the request
- Guardrail: 0 B per tick

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story (the Players hold counter drives the hooks):*

- [ ] **AC-1 (M-MD3-4)** A reply plus snap-back flips MATCH to MISDELIVER_ONE in the same tick as an Interact press: the press is judged on the new branch, and no hold starts (settle).
- [ ] **AC-2 (M-MD4-1 a–d)** MISDELIVER_ONE first displayed at T: presses at T and T+5 pulse the ✗ line, report `deliver.cancel.settle` and start no hold; T+6 starts one. A MATCH branch displayed for 0 ticks starts a hold on the press tick. ✗ (entry A) at T, ✓ at T+2, ✗ (A) at T+3 → earliest hold at T+9; selecting another wrong-house entry B at T+4 moves it to T+10; a `floorRefusal` or cents change does not move it. Toggle mode's arming press obeys the same settle. (M-MD4-1e, the screen-reader gate, is story 010's.)
- [ ] **AC-3 (M-MD4-2, 6 rows)** A hold latched at tick 0; at tick 6: (a) a MATCH hold's matches vanish by snap-back (→ MISDELIVER_ONE), (b) a matching entry appears during a MISDELIVER_ONE hold, (c) the selected slot empties (→ NOTHING), (d) the carrier enters swim depth, (e) an own throw becomes pending → cancels with (a–c) `deliver.cancel.prompt_changed`, (d) `deliver.dry_land`, (e) `deliver.pending`, progress 0 and 0 requests through the completion tick. (f) A MATCH hold whose `k` drops from 3 to 2 and stays MATCH completes and sends 1 request.
- [ ] **AC-4 (M-MD4-3)** On a MISDELIVER_ONE hold with slot 3 selected, selecting slot 4 at tick 5 cancels with `deliver.cancel.selection_changed`; 3 → 4 → 3 inside one tick does not cancel; the entry retired by `stale_entry` cancels; on a MATCH hold a selection change does nothing.
- [ ] **AC-5 (M-MD5-1, M-MD6-2c)** A completed hold sends exactly one `deliver_matching`. On MATCH, `selectedEntryId` = 0 (absent); on MISDELIVER_ONE it equals the latched entry id. `holdStartTick` equals the client's `IHostClock` estimate on the tick the hold began (12 ticks before the send at the default setting, with a test-injected clock offset). `ClaimedPosition` is the feet position at press. There is no duration or count field. #17 closing at tick 6 of a 12-tick hold does not cancel it, and the request completes (closing grace).

---

## Implementation Notes

*Derived from ADR-0014 §3–§4 and ADR-0016 §2/§4:*

- **Hooks on `DeliverInteractable`:** `OnPress(ctx, ref latch)` recomputes the preview on this tick (story 003's state for this tick is already computed in 4b), refuses if `!CanStartHoldNext` (the press reports `deliver.cancel.settle`; Players pulses), and fills `OwnerPayload` (16 B): branch kind u8, selected slot u8, `selectedEntryId` u32, `holdStartTick` u32 (from `IHostClock.EstimatedHostTick` read now) and the destination ordinal u16, with explicit padding. `ShouldCancel(latch, ctx)` recomputes the branch with the **hold's start-tick destination state** (closing grace: a close never yields REJECT mid-hold) and returns `prompt_changed`/`selection_changed`, or the MD2 reason for any row other than 4 and 5. `TryBuildRequest(latch, ctx, ref op)` writes the 24 B `DeliverMatching` payload (story 005's struct): `mailboxId`, `selectedEntryId` (0 on MATCH), `holdStartTick`, `ClaimedPosition = latch.ClaimedPosition`.
- **Selection compare at the tick boundary:** compare the latched slot + `entryId` with this tick's selected slot + entry. An intra-tick scroll and back is invisible by construction (only tick state is read).
- The hold counter, cancel range (2,900 mm), Toggle mode and completion timing are Players'/the router's (`production/epics/player-character/story-016-press-resolution-tap-catch-armed.md`, `story-017-hold-counter-cancels-channel-focus-latch.md`). M-MD4-4 (Toggle, 2.9 m walk-away) is asserted there and re-run end to end in story 007.
- `IHostClock` on the host's own client stamps the exact tick (offset 0, MD5). Tests inject a clock with an offset.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: the host handler and the clamp. Story 006: reply handling. Network story 008: the wire codec.
- M-MD4-1e (screen-reader settle) → story 012 (**Deferred**, Evan 2026-10-23; needs ADR-0018's announcement events).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliverPressLatchTests.cs` and `tests/unit/delivery/DeliverCancelTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003, Story 005 (payload struct); `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md`, `production/epics/player-character/story-017-hold-counter-cancels-channel-focus-latch.md`; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`IHostClock`)
- Unlocks: Story 006, Story 007, Story 008
