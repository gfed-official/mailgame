# Story 014: Focus — F4 eligibility and score, tie-break, 4-tick dwell and the `PreviewSubStep.Focus` binding

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (4 of 9).** Produces the focused anchor that Input & Controls latches at press (its G1) and Manual Delivery previews against.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 focus; F4 `interactFocus`; Edge Cases "if two players request the same seat")
**Requirement**: `TR-movement-011` (integer focus, 4-tick dwell), `TR-movement-002` (int64 dot product, integer score)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P11-1 (without the occlusion rows), PCM-P11-2a, PCM-F4-1.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 Focus) — Accepted 2026-10-16 (primary); ADR-0016: ClientLayer previews and presentation timing (§2 `Previews` sub-order, 4a `Focus`) — Accepted 2026-10-16
**ADR Decision Summary**: Focus is computed once per client tick for the local carrier, at `PreviewSubStep.Focus` (4a) after `OwnedCarrier.Step`. `chest = feet + (0, chestMm 1000, 0)`; `d = ISqrt(Δx²+Δy²+Δz²)`, `dh = ISqrt(Δx²+Δz²)`; `cosPm = dh < nearAxisMm ? 1000 : MulDiv(dot(fwdQ10, Δxz), 1000, 1024·dh)` with `fwdQ10` from `InputFrame.LookYaw` (Q15 → Q10). Eligible iff `d ≤ RangeMm (+ incumbentSlackMm if incumbent)` ∧ (`cosPm ≥ InputFrame.FocusCosMinPm` ∨ `d ≤ nearMm`) ∧ context filter ∧ not occluded. `score = (wDist·d·1000/RangeMm + wAng·(1000 − cosPm))/1000 − 100·Priority − (incumbent ? stickyPm : 0)`; lowest wins; ties → lowest `EntityId`, then lowest `Slot`; a challenger takes focus on its **4th** consecutive leading tick (immediately with no incumbent). The cone threshold comes from the `InputFrame`, never from a Godot call.
**ADR Version**: ADR-0014 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: focus uses the §2 integer score, the total tie-break (`EntityId`, then `Slot`) and the 4-tick dwell; the cone comes from `InputFrame.FocusCosMinPm`. Focus is latched while interacting (ADR-0014 §2)
- Required: edges are resolved only in `PreviewSubStep.EdgeResolve`, after 4a–4c of the same tick; `ClientLayer` steps live in the Sim and read only mirrors/ports, never `SimWorld` (ADR-0016)
- Guardrail: `CarrierStep` + focus p99 ≤ 0.10 ms with 2 000 anchors; 0 B per tick (advisory)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P11-1 non-occlusion rows, PCM-P11-2a, PCM-F4-1), scoped to this story. Occlusion rows (a chest segment crossing a solid box) are Story 015:*

- [ ] **PCM-P11-1.** Given anchors and `FocusCosMinPm = 635` (FOV 80, 16:9): **eligible** at chest distance 2500; at cos 635; at `d` 800 directly behind. **Not eligible** at 2501; at cos 634 when `d > 800`. With `FocusCosMinPm = 526` (FOV 100) the limit is cos 526. An incumbent stays focused at 2900 and loses eligibility at 2901.
- [ ] **PCM-F4-1.** anchor A (1500 mm, cos 950) scores **270**; B (900 mm, cos 600) scores **384**; A wins; A as incumbent scores **210**; priority 3 + incumbent at `d` 0, cos 1000 → **−360**; `d` 800, cos −1000 → **1328**; `dh` 149 → `cosPm` forced to 1000, `dh` 150 → computed (no division by zero, no sign flip under an overhead anchor).
- [ ] **PCM-P11-2a.** a lower-scoring challenger takes focus on its **4th** consecutive leading tick, **not the 3rd**; the lead counter resets if the leader changes or the challenger stops leading; with no incumbent the best candidate is focused on its **first** tick; an incumbent that becomes ineligible is dropped at once (no dwell on loss) and the best remaining candidate is focused on that tick.
- [ ] **Tie-break.** Equal scores resolve to the lowest `EntityId`, then the lowest `Slot` (property test: shuffling registration order never changes the winner); a catch candidate bypasses scoring entirely (the `ICatchPredictor` resolution happens at press, Story 016 — focus itself never returns a catch).
- [ ] **Context filter** via `InteractionGate` (Story 013): Panel → focus is still computed for display with `reason.close_panel`; Dead → no focus; Mounted → only the own-seat Dismount; Build → only `BuildModeOnly` anchors; an unavailable-but-eligible target still wins focus (shown dimmed with its reason, never hidden).
- [ ] **Latch hook.** When the interaction state is not Idle (a hold, channel or toggle-armed — set by Story 017), no challenger can take focus and the incumbent is kept even if its eligibility fails the occlusion test (Story 015 consumes this flag). This story provides `FocusState.Latched` and tests that setting it blocks a 30-tick challenger lead.
- [ ] **Integration with the ClientLayer.** `PreviewSubStep` (Focus, DeliverPreview, ThrowPreview, EdgeResolve, HoldAdvance) and `ClientLayerBuilder.BindPreview(PreviewSubStep, IClientStep)` exist (ADR-0016 Key Interfaces; if absent from sim-core story 023's `ClientLayer`, this story adds them additively), the Players focus step is bound at `Focus`, and a ClientLayer test shows the sub-order Focus → (stub 4b) → (stub 4c) → EdgeResolve → HoldAdvance for one `Step`.
- [ ] **Budget probes.** With 2 000 registered anchors and the carrier mid-grid, one focus pass visits ≤ 25 cells (≤ 49 on a no-focus tick once Story 018 adds the 6 m "beyond range" scan), scores only anchors in range, allocates 0 B (after warm-up) and exposes counters `focusCandidates` and `focusCellsVisited` (PCM-PERF-1 seam).

---

## Implementation Notes

*Derived from ADR-0014 §2 and GDD F4:*

- Files in `src/PerformativeMail.Sim/Players/Client/` (client steps live in `<System>/Client/` per ADR-0016 §1): `FocusStep.cs` (`IClientStep`), `FocusScore.cs` (pure: eligibility + score on `long`), `FocusState.cs`; in `src/PerformativeMail.Sim/ClientLayer/`: `PreviewSubStep.cs` + the `BindPreview` additive builder method.
- The step reads: the local `CarrierState` (chest point; produced by Story 020's `CarrierStep` earlier in the same `Step`), the `InputFrame` (`LookYaw`, `FocusCosMinPm`), `CarrierStatus` (Story 013), the `AnchorRegistry` (Story 012), and the owner hooks' `IsAvailable` (pure). It never reads `SimWorld`. On the host's own ClientLayer the registry is the same object the host systems register into (read-only from the client step).
- `fwdQ10`: `forward(θ) = (−Sin θ, −Cos θ)` in Q15 (ADR-0013 §3), shifted `>> 5` to Q10. `cosPm = Fix.MulDiv(dot, 1000, 1024·dh)` with `dot = fwdQ10x·Δx + fwdQ10z·Δz` in `long`.
- Score divisions follow the GDD formula literally (floor division of non-negative terms): `t1 = wDist·d·1000 / RangeMm`, `score = (t1 + wAng·(1000 − cosPm)) / 1000 − 100·Priority − sticky`. Use `RangeMm` of the anchor (default 2500).
- Dwell state: `leaderRef`, `leadTicks`. Each tick compute the best eligible candidate `B`; if no incumbent → focus `B` now; else if `B == incumbent` → `leadTicks = 0`; else if `B == leaderRef` → `leadTicks++` else `leaderRef = B; leadTicks = 1`; when `leadTicks == focusDwellTicks` (4) → swap. The incumbent's own score includes `stickyPm` and its eligibility range includes `incumbentSlackMm`.
- `ClientLayer` is a sim-core story-023 deliverable with four top-level steps; ADR-0016 adds the `Previews` sub-order. No other epic owns `BindPreview` yet — this is the first binder (Focus 4a), so it lands here. Manual Delivery adds 4b, Throwing 4c.
- The cone-cos conversion from FOV/aspect is the Presentation's (Story 022), delivered via the router in `InputFrame.FocusCosMinPm`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 015: occlusion (lazy, score-ordered). Story 016: press resolution. Story 017: hold/channel and setting the latch. Story 018: publishing the snapshot.
- Prompt widget, focus bracket drawing (HUD epic); `FocusCosMinPm` computation from FOV (Story 022).
- `ICatchPredictor` evaluation (Throwing, Story 016 consumes it).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/FocusScoreTests.cs` (`[Trait("Id", "PCM-F4-1")]`, `PCM-P11-1`, `PCM-P11-2a`) and `tests/unit/player/FocusPreviewOrderTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 012 (registry), 013 (gate, `CarrierStatus`), 001 (`CarrierState`), 003 (shared forward-vector convention), `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` (`ClientLayer`, `InputFrame` — fields `LookYaw`, `FocusCosMinPm` per ADR-0012; the provisional `InputFrame` there must carry them: input-controls epic finalises widths)
- Unlocks: Stories 015, 016, 017, 018; manual-delivery epic (4b preview binding); throwing-catching (4c); input-controls (presented snapshot source)
