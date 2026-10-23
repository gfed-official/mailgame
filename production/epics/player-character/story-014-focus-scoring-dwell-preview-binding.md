# Story 014: Focus — F4 eligibility and score, tie-break, 4-tick dwell and the `PreviewSubStep.Focus` binding

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1.5 days; ~700 lines prod + tests — readiness 2026-10-22: the port, the `BindPreview` builder change, a registry overload, two counters and an architecture rule)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (D1–D6 and the ADR-0014 l.66 erratum Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), "Approve the ADR-0014 l.66 erratum and all six 014 defaults as recommended"; TD-170 stays pending Evan). Before that: 2026-10-22 (readiness fixes on main eda1efd: names matched to the code, the carrier-source port instead of reading story 020's output, `BindPreview` made unconditional, latch semantics, counters, determinism / 0 B / architecture ACs; Mail Lead planning defaults in TD-164)

> **Interaction-framework story (4 of 9).** Produces the focused anchor that Input & Controls latches at press (its G1) and Manual Delivery previews against.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 focus; F4 `interactFocus`; Edge Cases "if two players request the same seat")
**Requirement**: `TR-movement-011` (integer focus, 4-tick dwell), `TR-movement-002` (int64 dot product, integer score)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P11-1 (without the occlusion rows), PCM-P11-2a, PCM-F4-1.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 Focus) — Accepted 2026-10-16 (primary); ADR-0016: ClientLayer previews and presentation timing (§2 `Previews` sub-order, 4a `Focus`) — Accepted 2026-10-16
**ADR Decision Summary**: Focus is computed once per client tick for the local carrier, at `PreviewSubStep.Focus` (4a) after `OwnedCarrier.Step`. `chest = feet + (0, chestMm 1000, 0)`; `d = ISqrt(Δx²+Δy²+Δz²)`, `dh = ISqrt(Δx²+Δz²)`; `cosPm = dh < nearAxisMm ? 1000 : MulDiv(dot(fwdQ10, Δxz), 1000, 1024·dh)` with `fwdQ10` from `InputFrame.LookYaw` (Q15 → Q10). Eligible iff `d ≤ RangeMm (+ incumbentSlackMm if incumbent)` ∧ (`cosPm ≥ InputFrame.FocusCosMinPm` ∨ `d ≤ nearMm`) ∧ context filter ∧ not occluded. `score = (wDist·d·1000/RangeMm + wAng·(1000 − cosPm))/1000 − 100·Priority − (incumbent ? stickyPm : 0)`; lowest wins; ties → lowest `EntityId`, then lowest `Slot`; a challenger takes focus on its **4th** consecutive leading tick (immediately with no incumbent). The cone threshold comes from the `InputFrame`, never from a Godot call.
**ADR Version**: ADR-0014 2026-10-16 (errata 2026-10-20 `CarrierStatus.OccupiedSeat`, 2026-10-21 `CarrierStatus.PanelOpen`, 2026-10-22 §2 l.66 focus at `PreviewSubStep.Focus`); ADR-0016 2026-10-16

> **ADR text note (resolved):** ADR-0014 §2 l.66 said focus is computed "at the end of Players' `CarrierStep` binder"; ADR-0014 l.114 (§7, amended before acceptance) and ADR-0016 §2 l.99 place it at `PreviewSubStep.Focus` (4a). This story follows l.114 and ADR-0016. The text-only ADR-0014 l.66 erratum is Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat) ("Approve the ADR-0014 l.66 erratum and all six 014 defaults as recommended") and applied: ADR-0014 l.66 and the control-manifest Errata 2026-10-22 line.

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
- [ ] **Tie-break.** Equal scores resolve to the lowest `EntityId`, then the lowest `Slot` (`AnchorRef.CompareTo`, `Players/AnchorRef.cs`). Shuffle test (no FsCheck in the repo): 8 equal-score anchors registered in 32 seeded `Pcg32` shuffles give the same winner. A catch candidate bypasses scoring entirely (the `ICatchPredictor` resolution happens at press, Story 016 — focus itself never returns a catch).
- [ ] **Context filter** via `InteractionGate.Evaluate(in AnchorDef, in CarrierStatus) → GateResult` (Story 013; it already implements the 2026-10-20 / 2026-10-21 errata): `GateKind.NoCandidate` excludes the anchor (Dead → no focus; Mounted → only the Dismount anchor whose `Ref == CarrierStatus.OccupiedSeat`, `None` → no candidates; Build → only `BuildModeOnly` anchors); `Unavailable(reason)` stays a candidate and can win focus, shown dimmed with its reason, never hidden (`CarrierStatus.PanelOpen` → `reason.close_panel`; swimming without `AllowWhileSwimming` → `DryLand`). Candidates also filter on the registry context mask: `AnchorRegistry.ContextMaskAt(i)` must be `ContextBuild` when `Status.Body == Build`, else `ContextWorld`. `IsAvailable(in InteractionContext, out StringId)` is called for the winner only (hold fields zero, `IsHost = 0`).
- [ ] **Latch hook** (ADR-0014 l.71: "no challenger takes it and occlusion does not drop it; only §4's cancel list ends it"; semantics are D2, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164). This story provides `FocusState.Latched` (set and cleared by Story 017; Story 015 consumes it). While it is true, 4a does not re-evaluate the incumbent's eligibility at all (range, cone, context, availability and occlusion failures are all overridden; ending the latch is §4's cancel list at 4e, Story 017), `IsAvailable` is still called for the prompt, and the dwell counters are held at zero; when it clears, the dwell state resets (a challenger needs 4 fresh leading ticks) and the next 4a drops an ineligible incumbent at once. An unregistered latched anchor is kept as a ref and never dereferenced. Tests: a latch blocks a 30-tick challenger lead; a latched incumbent at 2901 mm stays focused; a latched incumbent with `Body = Dead` stays focused for that tick.
- [ ] **Integration with the ClientLayer.** Absent on main, so this story adds them additively (ADR-0016 Key Interfaces l.214, l.224; `ClientLayerBuilder.cs` l.10 already names this story): `enum PreviewSubStep : byte { Focus, DeliverPreview, ThrowPreview, EdgeResolve, HoldAdvance }` in `ClientLayer/PreviewSubStep.cs` and `ClientLayerBuilder.BindPreview(PreviewSubStep, IClientStep)`, which throws `SimBuildException` `BindAfterFreeze` after `Build` (like `Bind`), `ArgumentNullException` for a null step and `ArgumentOutOfRangeException` for an undefined sub-step. `Previews` runs its five sub-rows in enum order, each in bind order; plain `Bind(ClientStepId.Previews, …)` stays legal and runs after `HoldAdvance` in bind order (D4, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164), so `ClientLayerStepOrderTests` keeps passing unchanged. `FocusStep` is bound at `Focus`, and a ClientLayer test shows the sub-order Focus → (stub 4b) → (stub 4c) → EdgeResolve → HoldAdvance for one `Step`. No new top-level `ClientStepId` (manifest l.240).
- [ ] **Budget probes** (D3, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164). Fixture: 2 000 anchors on a 1 m lattice, every `RangeMm` 2500 (so `AnchorRegistry.QueryRadiusCells` = `CeilDiv(2500 + 400, 2000)` = 2, 5×5 cells of 2 m), carrier at the lattice centre. One focus pass visits ≤ 25 cells (≤ 49 on a no-focus tick once Story 018 adds the 6 m "beyond range" scan), scores only anchors in range, and leaves `CounterId.AnchorQueryOverflow` unchanged. A per-tick `FocusCounters { int Candidates; int CellsVisited }` is the test seam (Story 015 takes `ref FocusCounters`), flushed into the ClientLayer's `SimCounters` under new `CounterId.FocusCandidates = 52` and `CounterId.FocusCellsVisited = 53` (append-only). `AnchorRegistry` gains an additive `QueryCells(in Vec3Mm, Span<int>, out int cellsVisited)` overload (the existing overload is unchanged). The candidate span is `stackalloc int[128]`; truncation is counted by the existing `AnchorQueryOverflow` and focus runs over the kept prefix (deterministic: cell-ascending order). The p99 ≤ 0.10 ms figure stays a guardrail, not an AC (PCM-PERF-1 is blocked on GDD OQ6).
- [ ] **Allocation.** `FocusStep.Step` allocates 0 B after 300 warm-up ticks (`AllocAssert.Zero`, the `InteractionGateAllocationTests` pattern), with 2 000 anchors registered.
- [ ] **Determinism.** Two fresh `FocusStep` instances over the same 300-tick scripted source give identical `Focused`, `LeaderRef` and `LeadTicks` per tick (the `CarrierPortsTests` `ScriptedSources_TwoRuns_ProduceIdenticalReadsPerTick` shape).
- [ ] **Carrier source port** (D1, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164; ADR-0016 l.238: client steps read only mirrors or ports, never `SimWorld`). `FocusStep` takes by constructor a Sim-defined port declared here in `Players/Client/ICarrierFocusSource.cs`: `ICarrierFocusSource { Vec3Mm Feet { get; } ref readonly CarrierStatus Status { get; } }`; tests supply a scripted implementation; Story 020's `CarrierView` implements it additively and wires the real producer. `FocusStep` also takes the `AnchorRegistry` (`PlayersSystem.Anchors`, read-only), an `InteractionGate` and `PlayerSettings` by constructor; production wiring is composition's, outside this story.
- [ ] **Architecture rule** (D5, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164; ADR-0016 l.332 "the arch test confirms no `Sim/*/Client` type references `SimWorld`", which nothing checks today). SC-ARCH-1 (`tests/unit/architecture/ClientLayerRulesTests.cs`) gains a rule for namespaces ending in `.Client`: no `SimWorld` in signatures or method bodies, with a seen-failing fixture; the gameplay-type signature ban is not applied to them (`FocusStep` legitimately holds `AnchorRegistry`).

---

## Implementation Notes

*Derived from ADR-0014 §2 and GDD F4:*

- Files in `src/PerformativeMail.Sim/Players/Client/` (client steps live in `<System>/Client/` per ADR-0016 §1): `FocusStep.cs` (`IClientStep`), `FocusScore.cs` (pure: eligibility + score on `long`), `FocusState.cs`; in `src/PerformativeMail.Sim/ClientLayer/`: `PreviewSubStep.cs` + the `BindPreview` additive builder method.
- Files also: `Players/Client/FocusCounters.cs`, `Players/Client/ICarrierFocusSource.cs`; `ClientLayer/ClientLayerBuilder.cs`, `ClientLayer/ClientLayer.cs` (sub-rows); `Players/AnchorRegistry.cs` / `AnchorGrid.cs` (the `cellsVisited` overload); `Core/CounterId.cs` (52, 53); `tests/unit/architecture/ClientLayerRulesTests.cs`.
- The step reads: the carrier's feet and `CarrierStatus` through `ICarrierFocusSource` (not story 020's `CarrierState` — 020 depends on this story and implements the port later), the `InputFrame` through `ctx.Input` (`LookYaw` ushort BAM, `FocusCosMinPm` short — fixed by input-controls 002), the `AnchorRegistry` (Story 012: `QueryCells(in Vec3Mm, Span<int>)`, `DefAt`, `ImplAt`, `ContextMaskAt`), the `InteractionGate` (Story 013), `PlayerSettings` (`ChestMm`, `IncumbentSlackMm`, `NearMm`, `NearAxisMm`, `WDist`, `WAng`, `StickyPm`, `FocusDwellTicks` — never constants), and the winner's `IsAvailable` (pure). It never reads `SimWorld`. On the host's own ClientLayer the registry is the same object the host systems register into (read-only from the client step).
- `fwdQ10`: `forward(θ) = (−TrigQ15.Sin θ, −TrigQ15.Cos θ)` in Q15 (ADR-0013 §3, the `CarrierHeading.ToWorld` convention), shifted `>> 5` to Q10. `cosPm = Fix.MulDiv(dot, 1000, 1024·dh, ref sink)` with `dot = fwdQ10x·Δx + fwdQ10z·Δz` in `long`; `d`, `dh` via `Fix.ISqrt(ulong)`. After the Q15 → Q10 shift `|fwd|` is not exactly 1024, so clamp `cosPm` to [−1000, 1000] (D6, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164); `Fix.MulDiv` truncates toward zero for a negative `dot`, which the clamp and the cone test tolerate.
- Score divisions follow the GDD formula literally (floor division of non-negative terms): `t1 = wDist·d·1000 / RangeMm`, `score = (t1 + wAng·(1000 − cosPm)) / 1000 − 100·Priority − sticky`. Use `RangeMm` of the anchor (default 2500).
- Dwell state: `leaderRef`, `leadTicks`. Each tick compute the best eligible candidate `B`; if no incumbent → focus `B` now; else if `B == incumbent` → `leadTicks = 0`; else if `B == leaderRef` → `leadTicks++` else `leaderRef = B; leadTicks = 1`; when `leadTicks == focusDwellTicks` (4) → swap. The incumbent's own score includes `stickyPm` and its eligibility range includes `incumbentSlackMm`.
- `ClientLayer` is a sim-core story-023 deliverable with four top-level steps; ADR-0016 adds the `Previews` sub-order. No other epic owns `BindPreview` yet — this is the first binder (Focus 4a), so it lands here. Manual Delivery adds 4b, Throwing 4c.
- The cone-cos conversion from FOV/aspect is the Presentation's (player-character Story 023, `CameraRig`, via `IFocusConeSink`), delivered via the router in `InputFrame.FocusCosMinPm`.

- **D1–D6, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat)** ("Approve the ADR-0014 l.66 erratum and all six 014 defaults as recommended"; TD-164; formerly Mail Lead planning defaults; none contradicts an Accepted ADR): D1 the `ICarrierFocusSource` port; D2 the latch semantics above; D3 `FocusCounters` + `CounterId` 52/53 + the `QueryCells` overload + the 128-slot candidate span; D4 `Previews` sub-rows with plain `Bind(Previews)` running after `HoldAdvance`; D5 the `.Client` namespace architecture rule; D6 the `cosPm` clamp.
- **Build calls on top of D1–D6** (TD-170, Mail Lead planning defaults, pending Evan): the `SimCounters` constructor argument, running-maximum focus counters, gate-reason-first availability (also under the latch), the winner's `InteractionContext` fields, the latched-incumbent `TryGet` fallback, and `BindPreview` flattened into the `Previews` row at `Build`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 015: occlusion (lazy, score-ordered). Story 016: press resolution. Story 017: hold/channel and setting the latch. Story 018: publishing the snapshot.
- Prompt widget, focus bracket drawing (HUD epic); `FocusCosMinPm` computation from FOV (Story 023).
- `ICatchPredictor` evaluation (Throwing, Story 016 consumes it).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* QL-STORY-READY (qa-lead, 2026-10-22 readiness on main eda1efd): GAPS — folded into the ACs above (port, latch, counters, budget fixture, determinism, 0 B, shuffle test, architecture rule).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/FocusScoreTests.cs` (`[Trait("Id", "PCM-F4-1")]`, `PCM-P11-1`, `PCM-P11-2a`), `tests/unit/player/FocusStepTests.cs` (dwell, latch, context filter, tie-break shuffle, determinism, budget), `tests/unit/player/FocusAllocationTests.cs`, and `tests/unit/sim/ClientLayer/PreviewSubStepOrderTests.cs` (sub-order, `BindPreview` guards) plus the `ClientLayerRulesTests` `.Client` rule — must exist and pass on net10.0 and net8.0, Debug and Release. Owner skip rule (decision log 2026-10-19): a test skipped in one configuration must run and pass in the other.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 012 (registry), 013 (gate, `CarrierStatus`), 001 (`CarrierState`), 003 (shared forward-vector convention), `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` (`ClientLayer`, `InputFrame` — fields `LookYaw` ushort BAM and `FocusCosMinPm` short, fixed by input-controls 002, Complete). All Complete. Not a dependency: Story 020 (it depends on this story and implements `ICarrierFocusSource`).
- Unlocks: Stories 015, 016, 017, 018, 020 (implements `ICarrierFocusSource`); manual-delivery epic (4b preview binding); throwing-catching (4c); input-controls (presented snapshot source)
