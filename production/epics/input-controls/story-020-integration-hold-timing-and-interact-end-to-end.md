# Story 020: Headless router → `InputLatch` → interaction integration (A) — grammar fire ticks, hold timing, latch and no-repeat rules

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M–L (~1 day; ~150 lines harness + ~400 lines of table-driven tests; no new production code beyond the harness)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-23 (dependencies re-pointed by the m1a-replan PR)

> **Dependencies re-pointed 2026-10-23 (E1 / E2, Evan 8:10 AM PT "approve all").** Stories 022 (replay corpus), 023 (fantasy-test scripts) and 024 (zero-allocation gate) are Deferred (M0 / M1 / M0); they were downstream only, so nothing this story depends on changed. The 0 B guardrail is advisory until M0 (E2).

> **Integration story (1 of 2).** Stories 007–019 test the router in isolation; the grammar rules G1–G6 are only *true* once a router edge crosses the `InputLatch` into Player Character's press resolution and hold counter (`HoldAdvance`), because ADR-0014 puts hold timing Sim-side. This story builds the headless harness and asserts those rules end to end. It is the first place the GDD's tick-numbered ACs (fire on tick 12, cancel on tick 11 / 12) can be asserted honestly.

## Context

**GDD**: `design/gdd/input-controls.md` (R3 grammar, G1 latched at press, G3 muted until released, G6 holds do not repeat, R4.3–R4.4, Edge Cases "Timing"; ACs H-R3-1, H-G1-1, H-G3-2, H-G6-1, H-EC-4, H-R4-4, H-R4-6)
**Requirement**: `TR-input-004`, `TR-input-008` (edges attached to the first catch-up tick, end to end), with the Sim half of `TR-input-011` / `TR-input-012` (cited from Player Character 011 / 017 — **not** re-implemented here)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§4 consumption, §3 latching) — Accepted 2026-10-16 (primary); ADR-0014 (§3–§4 counting convention: `heldTicks = tick − PressTick`, completes on `PressTick + HoldTicks`, edges before accrual), ADR-0016 (§2 step order), ADR-0002 (§2 `isFirstTickInBatch`) secondary
**ADR Decision Summary**: A router edge is gated at event time, delivered on the first tick of a catch-up batch and processed before hold accrual; the hold completes on tick `PressTick + HoldTicks` (12 at the default 0.4 s) and a release edge on tick 12 (processed first) cancels; a completed hold fires once and never repeats (G6); the target is the one in the presented snapshot (G1).
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0014); 2026-10-16 (ADR-0016)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim + Client.Core) | **Risk**: LOW
**Engine Notes**: None. Headless hosts have no driver: the harness calls `ClientLayer.Step(first)` and `SimWorld.Tick()` in a plain loop, or `FrameRunner` with integer timestamps (ADR-0002 §5).

**Control Manifest Rules (Foundation)**:
- Required: headless hosts call `ClientLayer.Step()` / `SimWorld.Tick()` in a plain loop; `InputLatch` never dispatches a verb; edges resolve only in `PreviewSubStep.EdgeResolve`; holds counted only by `HoldAdvance` (ADR-0016 §2, ADR-0014 §4)
- Forbidden: re-implementing `Hold.HoldTicks`, the hold counter or press resolution in the test harness (the test must exercise the real Player Character / Manual Delivery code)
- Guardrail: the harness allocates freely (tests are not tick-path code); the 0-alloc gate is story 024 (Deferred to M0; advisory until then, E2)

---

## Acceptance Criteria

*From GDD ACs H-R3-1, H-G1-1, H-G3-2, H-G6-1, H-EC-4, H-R4-4, H-R4-6. Default fixture: `holdSettingMs` 400, every class in Hold mode, 30 Hz, press on tick 0, one deliver anchor A (and a second B, and a container C), no perks.*

- [ ] **Harness.** `tests/integration/input/InputToSimHarness.cs` composes `SimComposition.Build` (profile `M1aGreybox`), the real `ClientLayer` with the story 002 `InputLatch`, the real router (stories 004–019) over a fake clock tied to the client tick (`ms = floor(tick × 1000 / 30) + subTickOffset`), the real `CarrierStatus` / `IInteractionQuery` from Player Character, Manual Delivery's deliver anchors, a `RecordingRequestSink` (`IRequestSink`) and a `CatchPredictor` null object; it exposes `Press/Release(input, tick)`, `RunTicks(n)`, `Hitch(ticksInOneFrame)` (one router flush then n ticks with `Step(first)` only on the first) and read access to the sent requests, the interaction snapshot and the `UiEventLog`.
- [ ] **H-R3-1 (grammar fire ticks, every M-1a action, parametrised on release tick 1, 6, 11, 12, 13, 60).** **Tap** fires on the press tick; **Hold (deliver)** fires on tick 12 only when no release edge falls in ticks 1–12 (edges are processed before accrual: a release on 11 or 12 cancels, on 13 or 60 the hold already fired on 12); **Aim-release** commits on the release tick if `aimHeld ≥ throwMinAim` (250 ms → tick ≥ 8), else cancels; **Held state** is true from press to release. Each row reports as its own test case.
- [ ] **H-G1-1.** A deliver hold latched on mailbox A; focus moves to mailbox B at tick 6 → tick 12 sends `deliver_matching` for **A** and the snapshot's `Latched` names A throughout; the prompt target does not re-target.
- [ ] **H-G3-2.** E held in open air (catch-armed channel with no target), the carrier walks into mailbox range → **no deliver hold starts** until E is released and pressed again (walking onto a target with the button down starts nothing).
- [ ] **H-G6-1.** A deliver hold completing at tick 12; E stays down 60 more ticks while a second matching mailbox gains focus → exactly **one** `deliver_matching` is sent; completion does not repeat.
- [ ] **H-EC-4.** A hitch of 3 catch-up ticks containing the release edge: with `heldTicks` 10 / 12 at the start of the batch → the edge lands on the **first** catch-up tick, the hold is **cancelled** (never completed) and nothing is sent; with 12 / 12 already reached before the batch the hold completed once on tick 12 and the batch changes nothing.
- [ ] **H-R4-6.** The last presented prompt "Hold [E] Deliver" names mailbox A; focus flips to container C on the press tick; E pressed → a deliver hold latches on **A** (the presented snapshot, G1).
- [ ] **H-R4-4.** A mailbox and a 1-tick E press → nothing is sent and exactly one "Hold pulse" cancel cue (the snapshot's `LastCancel = Released`, mirrored to `UiEventLog` as `hold_cancel`) is emitted; a press with no target never starts a hold.

---

## Implementation Notes

*Derived from ADR-0012 §4, ADR-0014 §3–§4 and ADR-0016 §2:*

- Files: `tests/integration/input/InputToSimHarness.cs`, `HoldTimingEndToEndTests.cs`, `InteractLatchEndToEndTests.cs`; namespace `PerformativeMail.Sim.Tests.Integration.Input`. The test project already compiles `tests/integration/**` (`tests/README.md`).
- Reuse, do not rebuild: `SimComposition.Build` (`production/epics/simulation-core/story-025-composition-root-reflection-test.md`), `FrameRunner` with integer timestamps (`story-024-framerunner.md`) if convenient, Player Character's focus / press / hold steps (`production/epics/player-character/story-014-focus-scoring-dwell-preview-binding.md`, `story-016-press-resolution-tap-catch-armed.md`, `story-017-hold-counter-cancels-channel-focus-latch.md`, `story-018-interaction-snapshot-iinteractionquery.md`, `story-020-carrierstep-clientlayer-integration.md`), Manual Delivery's anchors and press hook (`production/epics/manual-delivery/story-001-deliver-anchors-hold-ticks.md`, `story-004-press-latch-cancels-request.md`), and the street fixture `production/epics/world-stub/story-006-m1a-street-and-fixtures.md`.
- If a Player Character / Manual Delivery story is not Done when this runs, this story waits for it — **do not stub the hold counter**. A stubbed `ICatchPredictor` (`NullCatchPredictor`) is fine (catch is M0).
- Counting convention (ADR-0014 §4, which supersedes PCM-P12-1's "held tick 1"): a 12-tick hold pressed on tick 0 sends on tick 12; assert exactly that, not tick 11.
- "Hold pulse" is a cue id derived from the cancel reason; HUD content is not asserted.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 021: the cancel-cause table (H-G2-1), Interact resolution rows (H-R4-2), Toggle, aim, commit and request-schema checks. Story 022: replay corpus. Story 024 (Deferred, M0): allocation.
- M0 rows: catch (H-R4-2 (a), H-R4-3), TextEntry.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/input/HoldTimingEndToEndTests.cs` (`[Trait("Id", "H-R3-1")]`, `H-G6-1`, `H-EC-4`), `tests/integration/input/InteractLatchEndToEndTests.cs` (`[Trait("Id", "H-G1-1")]`, `H-G3-2`, `H-R4-4`, `H-R4-6`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 007, 008, 009, 012, 013, 014, 016, 017, 019 (the router as a whole); `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`, `story-024-framerunner.md`, `story-025-composition-root-reflection-test.md`; `production/epics/player-character/story-011-hold-ticks-holdeff.md`, `story-014-focus-scoring-dwell-preview-binding.md`, `story-016-press-resolution-tap-catch-armed.md`, `story-017-hold-counter-cancels-channel-focus-latch.md`, `story-018-interaction-snapshot-iinteractionquery.md`, `story-020-carrierstep-clientlayer-integration.md` (all under `production/epics/player-character/`); `production/epics/manual-delivery/story-001-deliver-anchors-hold-ticks.md`, `story-004-press-latch-cancels-request.md`; `production/epics/world-stub/story-006-m1a-street-and-fixtures.md`
- Unlocks: Stories 021, 022; Deferred: 023 (M1)
