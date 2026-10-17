# Story 018: `InteractionSnapshot` publication and `IInteractionQuery`

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~250 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (8 of 9) — the `IInteractionQuery` story.** Input & Controls' router reads `IInteractionQuery.Current` (the last presented snapshot, G1) and the HUD renders the prompt, hold ring and focus bracket from it. Cite this path from `input-controls` and `manual-delivery`.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P11 "Input & Controls reads the last presented focus snapshot (anchor, verb, mode, availability, prompt data) through `IInteractionQuery`"; UI Requirements — prompt contents, "Too far" marker)
**Requirement**: `TR-movement-011` (focus result published for the router and HUD); `TR-movement-012`; serves the registry-owned `TR-input-*` snapshot consumer
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-F4-2 (`progress` 5/12 → 416 ‰), PCM-UI-1 (data half), H-G1-1.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§7 `IInteractionQuery`, `InteractionSnapshot`) — Accepted 2026-10-16 (primary); ADR-0016 (§2 4e publishes the snapshot; the `Presented` snapshot is tracked by ADR-0016) and ADR-0012 (§4 `PresentedSeq`) secondary — Accepted
**ADR Decision Summary**: At the end of `HoldAdvance` (4e) the client publishes the **interaction snapshot**: `InteractionSnapshot { uint Tick; ushort Seq; AnchorRef Focused; Verb; InteractMode; Availability (ok | reason StringId); PromptArgs (owner-supplied, 32 B); InteractState (Idle | Holding | Channel | ToggleArmed | Complete); short HeldTicks; short HoldTicks; ushort ProgressPm; AnchorRef Latched; CancelReason LastCancel; CatchPrompt (from ICatchPredictor) }`. `IInteractionQuery { ref readonly InteractionSnapshot Current { get; } }` is read-only and Sim-defined; the router and HUD consume it. `Presented` (the snapshot the Presentation last rendered) is tracked by ADR-0016, and press edges carry its `Seq`.
**ADR Version**: ADR-0014 2026-10-16; ADR-0016 2026-10-16; ADR-0012 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: the snapshot is published by Players' client steps only; the router (Client.Core) depends on the Sim-defined `IInteractionQuery` (dependency direction Client.Core → Sim, ADR-0012 §1)
- Required: `IInteractable` hooks stay pure — `WritePrompt` runs inside the publication step and must not allocate (ADR-0014 §1)
- Forbidden: Presentation reading `SimWorld` or the registry directly for prompt data (ADR-0003 §6)

---

## Acceptance Criteria

*From ADR-0014 §7 and GDD P11 / UI Requirements, scoped to this story (data only — widgets are HUD's):*

- [ ] **Fields.** After a tick with a focused, available Deliver anchor: `Focused` = its `AnchorRef`, `Verb` = Deliver, `InteractMode` = Hold, `Availability = ok`, `PromptArgs` = whatever `IInteractable.WritePrompt` wrote (32 B, copied by value), `InteractState = Idle`, `Tick` = the client tick, `Seq` incremented since the last tick whose content changed.
- [ ] **Unavailable still shows.** A focused target whose `IsAvailable` is false is still published (`Availability = reason StringId`, never cleared, never `ok`); one `reason` key only (PCM-UI-1 data half). Panel context publishes `reason.close_panel` for every focused anchor; Dead publishes no `Focused` (`default(AnchorRef)`).
- [ ] **Hold progress.** During a hold: `InteractState = Holding`, `Latched` = the latched anchor (distinct from `Focused` if focus moved), `HeldTicks = tick − PressTick`, `HoldTicks` = the latched length, `ProgressPm = Hold.ProgressPm(HeldTicks, HoldTicks)` — 5 of 12 → **416** — and `Complete` on the completion tick; after a cancel `LastCancel` holds the reason and `InteractState = Idle` (the reason persists until the next press, for the HUD's drain-at-2× / "Moved out of reach" cue).
- [ ] **Seq / change detection.** `Seq` increments only when any published field other than `Tick` changes (so a steady idle prompt keeps one `Seq` across thousands of ticks); wraps as a `ushort` with `SerialU16.IsNewer` semantics (ADR-0004 §5; `SerialU16` is a frozen interface "built by its first consumer" per sim-core story 002 — if no earlier story has built it, build it here, `unchecked`, RFC 1982); a press edge's `PresentedSeq` equal to the current `Seq` is "the prompt the player saw".
- [ ] **`IInteractionQuery`.** `Current` returns `ref readonly InteractionSnapshot`; two reads in one tick are identical; the struct is `unmanaged`, padding-free (explicit layout; no `bool`), and 0 B per tick to publish; the router never gets a mutable reference (compile-time via `ref readonly`; test via reflection that no public setter exists).
- [ ] **Too far / no target data.** The snapshot carries enough to show the "Too far" marker and the 2.5–6 m caret: `NearestBeyond` (`AnchorRef` of the nearest eligible-by-cone, unoccluded but out-of-range anchor within 6000 mm, or empty) and `NearestBeyondRangeMm` (its chest distance, or 0), so the HUD can draw the faint caret and pulse "Too far" on a press with no valid target (UI Requirements). Both fields are part of the snapshot layout (document the offsets).
- [ ] **Catch prompt.** `CatchPrompt` copies the `ICatchPredictor` result used this tick (`Predicted`, `FlightId`) so the router/HUD can show "Catch" without re-evaluating; with `NullCatchPredictor` it is empty.
- [ ] The publication runs last in `HoldAdvance` and the snapshot reflects the same tick's focus (4a), press (4d) and hold (4e) state — a ClientLayer integration test steps a press-and-hold sequence and asserts the snapshot per tick against a table.

---

## Implementation Notes

*Derived from ADR-0014 §7 and ADR-0016 §2/Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Players/`: `InteractionSnapshot.cs`, `IInteractionQuery.cs`, `InteractionSnapshotPublisher.cs` (an `IClientStep`, bound last in `HoldAdvance`, or the tail of `HoldAdvanceStep`). The double-buffer is two struct fields swapped per publication (the reader holds a `ref readonly` for at most one frame; Presentation reads between ticks).
- The "Presented" snapshot bookkeeping (what the HUD last drew) is ADR-0016/ADR-0018's; this story only guarantees a monotonically changing `Seq` the router can echo in `InputEdge.PresentedSeq`.
- `WritePrompt` is called once per tick for the focused anchor only (Manual Delivery's ≤ 26-entry scan runs in its 4b preview, not here; this story just copies the already-computed `PromptArgs`). Do not call `WritePrompt` for unfocused anchors.
- `NearestBeyond` / `NearestBeyondRangeMm`: reuse Story 014's candidate pass — track the nearest anchor that fails only the range test, within `6000 mm`, inside the cone and unoccluded; this widens the grid query to a 6 m radius (7 × 7 = 49 cells at 2 m) **only on ticks with no focus**; when something is focused no extra cells are visited. Update Story 014's `focusCellsVisited` probe to allow ≤ 49 on no-focus ticks.
- Do not expose the `AnchorRegistry` or `IInteractable` through the query; the HUD gets data only.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Rendering the prompt, hold ring, lock icon, "Too far" pulse and the focus bracket (HUD & Notifications epic — no epic yet). Story 026 supplies the projected anchor/bounds.
- Router consumption (input-controls epic: its `IInteractionQuery`-fed `holding`/`aiming` and G1 latch tests).
- Localised reject text (`reject{verb, reason}`), owner prompt content (Manual Delivery).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/InteractionSnapshotTests.cs` (`[Trait("Id", "PCM-UI-1")]` data rows, `H-G1-1`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 014, 016, 017 (state sources), 011 (`ProgressPm`), 013 (`reason.close_panel`), `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`; `production/epics/simulation-core/story-002-id-value-types.md` (declares `SerialU16` as built-by-first-consumer — see Acceptance Criteria)
- Unlocks: input-controls epic (router reads `IInteractionQuery`: `production/epics/input-controls/story-007-router-shell-press-machines-mute.md`, `production/epics/input-controls/story-012-interact-latch-toggle-translation-sprint.md`); manual-delivery epic (prompt `WritePrompt`); HUD prompt/ring/bracket (HUD epic, not yet created); Story 025
