# Story 021: Headless router → Sim integration (B) — the cancel-cause table, Interact resolution rows, Toggle, aim and commit end to end, request-schema checks

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M–L (~1 day; ~450 lines of table-driven tests on the story 020 harness)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-23 (dependencies re-pointed by the m1a-replan PR)

> **Dependencies re-pointed 2026-10-23 (E1, Evan 8:10 AM PT "approve all").** Story 023 is Deferred (M1) and story 022 is Ready (kept Ready by Evan 2026-10-27 9:42 PM PT); both are downstream only. Its "Stories 012–018" dependency range contains no Deferred story (011 is outside it); the H-EC-6 disconnect row that story 018 used to own is now story 031 (Deferred, M0) and is **not** asserted here — the cancel-cause table covers the focus-loss row and the Sim-originated causes.

> **Integration story (2 of 2).** Uses the `InputToSimHarness` from story 020. Asserts the rules whose truth spans the router and the Sim: every G2 cancel cause, the R4.3 resolution rows, Toggle completion with the button up, aim-release through Throwing's real sink, and the one-commit rule with the real hold counter.

## Context

**GDD**: `design/gdd/input-controls.md` (G2 cancel conditions, G7 Toggle mode, G9 commits, R4.3 Interact resolution, R10 "Server: no request carries a hold duration", Edge Cases "Simultaneous inputs"; ACs H-G2-1, H-R4-2, H-R4-5, H-G7-1 Sim half, H-G7-2, H-EC-1, H-EC-2, H-R11-2, H-R10-9)
**Requirement**: `TR-input-004`, `TR-input-023` (one-commit rule, end to end), `TR-input-013` (bypass requests carry no hold-duration or tick field) — with the Sim halves cited from Player Character 016/017 and Throwing 004/007
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§3 Toggle, one-commit gate, §6 immediates) — Accepted 2026-10-16 (primary); ADR-0014 (§3 resolution order, §4 cancel list), ADR-0015 (§1 throw payload: `ReqId`, no tick field), ADR-0011 secondary
**ADR Decision Summary**: The Sim sees only logical press / release / cancel and never branches on a mode; every G2 cause ends a hold or aim with no effect, a reason in the snapshot and in the log, and a muted button; the one-commit gate mutes a second commit with "Busy" while a hold or aim is in progress and until the host reply or 500 ms; no request carries a hold duration and the throw request has no tick field.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0014); 2026-10-16 (ADR-0015)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim + Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: the Sim never branches on a mode; the router never times holds; a commit is an Immediate verb whose request goes through the same `ClientAdapter` codec (ADR-0012; manifest Input router core)
- Forbidden: stubbing `Hold.HoldTicks`, `HoldAdvance` or Throwing's release builder in these tests
- Guardrail: tests only

---

## Acceptance Criteria

*From GDD ACs H-G2-1, H-R4-2, H-R4-5, H-G7-1, H-G7-2, H-EC-1, H-EC-2, H-R11-2, H-R10-9. Default fixture as story 020; mailbox A, B and container C registered.*

- [ ] **H-G2-1 (×12, parametrised).** A deliver hold at tick 6 (an aim for the Back row) is cancelled by each of: early release, Back, target invalid (anchor unavailable), target destroyed (unregistered), distance > 2900 mm (2900 continues, 2901 cancels), router-state change (panel opens), focus loss, device disconnect, knockdown (CarrierStatus `Dead` fixture), swim (CarrierStatus `Swimming` fixture), mount (CarrierStatus `Mounted` fixture) and a stub `ShouldCancel` reason → in every row progress = 0, **no request is sent**, a `hold_cancel{reason}` line is in `UiEventLog`, a cancel cue id is emitted and the pressed button is muted until released. **Damage** at tick 6 does not cancel: the hold completes at tick 12.
- [ ] **H-R4-2 (M-1a rows).** (a′) A teammate's lob predicted into the **focused** mailbox → E starts a deliver hold (the D3 gate; with the `ICatchPredictor` double returning a prediction whose receiver is the presented target); (b) a container → `Open` sent on the **press** tick; (c) a mailbox → a deliver hold; (d) no target → catch-armed channel while held (ends on release). Row (a) "catch" and the prompt words ("Catch", "[E] Open", "Hold [E] Deliver", which are HUD's) are M0 / HUD and not asserted.
- [ ] **H-R4-5 (cross-check).** The v1 anchors registered by Manual Delivery (Deliver = Hold), Grid Inventory (Open, Pickup = Tap) each declare exactly one verb with one grammar and every verb's grammar equals story 006's `VerbGrammarTable`; a fixture that registers a second verb on one anchor returns `DuplicateVerb`.
- [ ] **H-G7-1 (Sim half).** Deliver in Toggle mode, a 1-tick press at tick 0 → the hold completes at tick 12 with the button up and exactly one `deliver_matching` is sent; a second press at tick 8 cancels (router `Release` with `ToggleSecondPress`), nothing is sent, and that second press is **not** re-resolved as a catch.
- [ ] **H-G7-2 (full).** Aim in Toggle mode through Throwing's real `ThrowPreviewStep` and release builder: aiming from the first press with no timeout; a second press at 0.20 s ignored; at 0.30 s the throw request is sent in the same frame; Back cancels with no request.
- [ ] **H-EC-1 (end to end).** E deliver hold at tick 4, LT pressed → muted with "Busy" and its release throws nothing; Drop at tick 6 sends no `drop`; Jump at tick 6 fires and the hold still completes at tick 12; LT at tick 14 before the host reply is still muted; held on, it reads "Release to retry"; after the reply (or 500 ms) a fresh LT press works.
- [ ] **H-EC-2 (end to end).** E and pad X pressed together → one Interact press edge reaches `EdgeResolve`; the hold belongs to E; releasing E early cancels while X is still down; in Toggle mode X counts as the second press.
- [ ] **H-R11-2.** A throw release event in frame F → the throw request is in the op ring in frame F (`DrainInbound` runs after `DispatchImmediates`), not at the next tick.
- [ ] **H-R10-9.** By reflection over the wire payload types, neither `deliver_matching` (`production/epics/network-adapter-loopback/story-008-deliver-matching-wire-codec.md`, Manual Delivery `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` — Delivery-owned since the ADR-0009 erratum) nor the throw request (`production/epics/throwing-catching/story-006-throw-op-event-declarations-and-wire-codecs.md`) has a hold-duration field, and the throw request has no tick field (it carries `ReqId`, entry id, origin, direction, `units`, `side`).

---

## Implementation Notes

*Derived from ADR-0012 §3, §6 and ADR-0014 §4:*

- Files: `tests/integration/input/CancelCauseTableTests.cs`, `InteractResolutionTests.cs`, `ToggleAndCommitEndToEndTests.cs`, `RequestSchemaTests.cs`. Extend story 020's harness with `CarrierStatusScript` (set `Dead` / `Swimming` / `Mounted` at a tick), `DisconnectPad(tick)`, `LoseFocus(tick)`, and a real `ThrowReleaseBuilder` / `ThrowPreviewStep` binding.
- Rows that need a Player Character, Manual Delivery or Throwing feature that is not Done yet are skipped **by explicit `[Fact(Skip = "...")]` with the missing story's path**, never silently omitted; the story is not Done until no row is skipped.
- Damage-does-not-cancel (G2): PC 017 owns the list; the test sends a `Damage` event/condition through the carrier status double and asserts no cancel.
- The hold-as-toggle second press arrives from the router as a `Release` edge with `ToggleSecondPress`; PC 017 maps it to a cancel — do not special-case it in the harness.
- Request schemas are inspected through the codec registry (`production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md`), not by string search.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 020: grammar fire ticks and latch rules. Story 022: replay corpus. Story 031 (Deferred, M0): the `DeviceLost` cancel row. M0: catch rows, TextEntry, MP pause rows.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/input/CancelCauseTableTests.cs` (`[Trait("Id", "H-G2-1")]`), `InteractResolutionTests.cs` (`H-R4-2`, `H-R4-5`), `ToggleAndCommitEndToEndTests.cs` (`H-G7-1`, `H-G7-2`, `H-EC-1`, `H-EC-2`, `H-R11-2`), `RequestSchemaTests.cs` (`H-R10-9`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 020 (harness), Stories 012–018 (none Deferred; 018 is the focus-loss story after its 2026-10-23 split); `production/epics/throwing-catching/story-004-throw-aim-quantiser-and-preview-step.md`, `story-007-throw-release-builder-client-cooldown-reply.md`, `story-006-throw-op-event-declarations-and-wire-codecs.md`; `production/epics/manual-delivery/story-005-delivermatching-op-and-handler.md` (Delivery-owned `DeliverMatching` op + handler since the ADR-0009 erratum; formerly grid-inventory story 022), `production/epics/grid-inventory/story-011-open-set-close-intake.md`; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md`, `story-008-deliver-matching-wire-codec.md`
- Unlocks: Story 022; Deferred: 023 (M1)
