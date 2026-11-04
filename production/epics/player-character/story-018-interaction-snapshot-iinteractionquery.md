# Story 018: `InteractionSnapshot` publication and `IInteractionQuery`

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~250 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-11-04 (branch `docs/rulings-td208-td209`): TD-209 items 2–9 approved by Evan 9:23 AM PT by card click (card t271s1234, listing t271s1233). Earlier, same day (PR #77): TD-209 F1 approved by Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213), ADR-0014 erratum entered; F2 / item 3 settled by ADR-0015 §8 l.142 and ADR-0014 §7 l.114; Out of Scope's HUD cites corrected to M0+. Before that, same day: readiness against PR #60's API, now on main as 84ff4ee; cites re-checked on main 579319a. Earlier: 2026-11-01 (readiness against PR #60's API)

> **Interaction-framework story (8 of 9) — the `IInteractionQuery` story.** Input & Controls' router reads `IInteractionQuery.Current` (the latest published snapshot; the *presented* copy is input-controls 012's `PresentedPromptLatch`, G1) and the HUD renders the prompt, hold ring and focus bracket from it. Cite this path from `input-controls` and `manual-delivery`.

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

> **Mostly resolved 2026-11-04 (TD-209):** the ADR questions are settled — F1 (A5) approved by Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213; ADR-0014 erratum of that date), and F2 / item 3 (A6) settled by Accepted ADR-0015 §8 l.142 and ADR-0014 §7 l.114, not an Evan ruling. Item 10 (no cancel-reason key in the snapshot) is settled by Accepted ADR-0014 l.115, not an Evan item. Items 2–9 Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): "Approve Part 2 items 1-14 (TD-208 items 4, 5, 6, 7, 13, 14 and TD-209 items 2-9), as listed in your message above" (see the ACs and Implementation Notes). **Still pending Evan — do not build until resolved:** TD-209 item 1 (the byte offsets inside the approved 84 B snapshot) and listing t271s1233's open points (ii) (the too-far target on a tick that starts with something focused) and (iii) (which catch prediction wins when two presses land in one tick); not selected on card t271s1234 (not approved, not declined). *Facts on what depends on them (no decision): the `IInteractionQuery` AC requires an explicit layout and an `Unsafe.SizeOf` + offsets test, so the offsets test needs item 1 (or a layout the implementer picks, which would then be uncredited until Evan approves it); the size (84 B) is already approved; approved items 3 and 7 quote byte numbers (38, 44; 6–83) that assume item 1's layout, while their rules do not; (ii) covers every tick that starts with something focused, so the 6 m pass needs some answer for it before it can be coded; (iii) arises only if two presses land in one tick and, with M-1a's `NullCatchPredictor`, every prediction is empty either way.*

*From ADR-0014 §7 and GDD P11 / UI Requirements, scoped to this story (data only — widgets are HUD's):*

- [ ] **Fields.** After a tick with a focused, available Deliver anchor: `Focused` = its `AnchorRef`, `Verb` = Deliver, `InteractMode` = Hold, `Availability = ok`, `PromptArgs` = whatever `IInteractable.WritePrompt` wrote (32 B, copied by value), `InteractState = Idle`, `Tick` = the client tick, `Seq` incremented since the last tick whose content changed.
- [ ] **Unavailable still shows.** A focused target whose `IsAvailable` is false is still published (`Availability = reason StringId`, never cleared, never `ok`); one `reason` key only (PCM-UI-1 data half). Panel context publishes `reason.close_panel` for every focused anchor; Dead publishes no `Focused` (`default(AnchorRef)`). *TD-209 item 6, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): on any tick you're dead, the snapshot shows no target and no reason, the same tick. Uncredited detail: TD-209 item 6.*
- [ ] **Hold progress.** During a hold: `InteractState = Holding`, `Latched` = the latched anchor (distinct from `Focused` if focus moved), `HeldTicks = tick − PressTick`, `HoldTicks` = the latched length, `ProgressPm = Hold.ProgressPm(HeldTicks, HoldTicks)` — 5 of 12 → **416** — and `Complete` on the completion tick; after a cancel `LastCancel` holds the reason and `InteractState = Idle` (`LastCancel` stays until you next successfully start a hold, a catch or another untimed held action such as a push, or until a tap action is actually sent; a newer cancel replaces it: a press refused as unusable or busy, a hold breaking, or letting go of a hold before it finishes; a press ignored mid-hold, or made while dead, in a menu or mounted, leaves it as it was — for the HUD's drain-at-2× / "Moved out of reach" cue). *TD-209 item 8, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): "why your hold broke" stays until you next successfully start a hold, a catch or another untimed held action such as a push, or until a tap action is actually sent; a newer cancel replaces it: a press refused as unusable or busy, a hold breaking, or letting go of a hold before it finishes; a press ignored mid-hold, or made while dead, in a menu or mounted, leaves it as it was. Uncredited detail: the `LastCancel` field name, the HUD cue wording and the rest of TD-209 item 8.* *TD-209 item 5, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): the held target is filled while you hold, while you do an untimed held action such as a push, and after a hold finishes until you let go; it's empty when idle, and during a catch, which has no target; once complete, the ring stays full (1000) until you let go; hold counts are capped at 32,767. Uncredited detail: TD-209 item 5.*
- [ ] **Seq / change detection.** `Seq` increments only when any published field other than `Tick` changes (so a steady idle prompt keeps one `Seq` across thousands of ticks); wraps as a `ushort` with `SerialU16.IsNewer` semantics (ADR-0004 §5; `SerialU16.IsNewer` exists at `src/PerformativeMail.Sim/Primitives/SerialU16.cs`, half-range assert `SimAssertCode.SerialHalfRange`); a press edge's `PresentedSeq` equal to the current `Seq` is "the prompt the player saw". *TD-209 item 7, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): it goes up by 1 (wrapping after 65,535) only when anything other than the tick changed (bytes 6–83); it starts at 0; an idle player looking at nothing stays at 0. (The byte range assumes item 1's layout, still pending.) Uncredited detail: TD-209 item 7.*
- [ ] **`IInteractionQuery`.** `Current` returns `ref readonly InteractionSnapshot`; two reads in one tick are identical; the struct is `unmanaged`, padding-free (explicit layout; no `bool`), and 0 B per tick to publish; the router never gets a mutable reference (compile-time via `ref readonly`; one `Unsafe.SizeOf` + offsets test covers the layout; no reflection setter test (lean-testing: no getter/setter tests)).
- [ ] **Too far / no target data.** The snapshot carries enough to show the "Too far" marker and the 2.5–6 m caret: `NearestBeyond` (`AnchorRef` of the nearest eligible-by-cone, unoccluded but out-of-range anchor within 6000 mm, or empty) and `NearestBeyondRangeMm` (its chest distance, or 0), so player-character 026 can feed the faint marker at 2.5–6 m and the "Too far" pulse on a press with no valid target (UI Requirements; the HUD's own marker waits for M0). Both fields are part of the snapshot layout (document the offsets). *Approved by Evan 2026-11-04 6:47 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t265s1214, listing message t265s1213): "Approve A5 (TD-209 F1): add the two Too-far fields with an ADR-0014 erratum, as listed in your message above" — as listed: two new interaction-snapshot fields, the nearest visible out-of-reach target and its distance in mm capped at 6000 (0 if none); planned snapshot 84 bytes; ADR-0014 erratum 2026-11-04 (l.115).*
- [ ] **Catch prompt.** `CatchPrompt` copies the `ICatchPredictor` result used this tick (`Predicted`, `FlightId`) so the router/HUD can show "Catch" without re-evaluating; with `NullCatchPredictor` it is empty. *TD-209 item 3, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): the catch guess made when you press Interact is kept and copied into bytes 38 and 44; it's empty on any tick without a press, and on a press that was refused, ignored or made while mounted (the guess isn't made then); adds a small change to the press code (`PressResolver`), which today throws the guess away; nothing visible until the real catch predictor arrives in the next milestone (M0). (Bytes 38 and 44 assume item 1's layout, still pending; which press wins when two land in one tick is open point (iii), pending.) Uncredited detail: TD-209 item 3.* *Settled by Accepted ADRs (TD-209 F2 / item 3 option (b), A6; not an Evan ruling): the predictor is evaluated only on a press tick, so the snapshot copies only the press-tick prediction and is empty on other ticks; the lasting Catch prompt is Throwing's (ADR-0015 §8 l.142; ADR-0014 §7 l.114).*
- [ ] The publication runs last in `HoldAdvance` and the snapshot reflects the same tick's focus (4a), press (4d) and hold (4e) state — a ClientLayer integration test steps a press-and-hold sequence and asserts the snapshot per tick against a table.

---

## Implementation Notes

*Derived from ADR-0014 §7 and ADR-0016 §2/Key Interfaces:*

- Files: `InteractionSnapshot.cs`, `IInteractionQuery.cs` in `src/PerformativeMail.Sim/Players/`; `InteractionSnapshotPublisher.cs` in `Players/Client/` (ADR-0016 l.238; `ClientLayerRulesTests.cs:55-57` must list it). *TD-209 item 9, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): a separate small step right after the hold step at the end of each tick; the hold step isn't changed, and the test rig and architecture test list include the new step. Uncredited detail: TD-209 item 9.* The double-buffer is two struct fields swapped per publication (the reader holds a `ref readonly` for at most one frame; Presentation reads between ticks).
- The "Presented" snapshot bookkeeping (what the HUD last drew) is ADR-0016/ADR-0018's; this story only guarantees a monotonically changing `Seq` the router can echo in `InputEdge.PresentedSeq`.
- `WritePrompt` is called once per tick for the focused anchor only (Manual Delivery's ≤ 26-entry scan runs in its 4b preview, not here; this story just copies the already-computed `PromptArgs`). Do not call `WritePrompt` for unfocused anchors. *TD-209 item 2, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): once per tick, only for the focused target, usable or not, into a fresh 32-byte block; focus remembers the target's list position, so the game skips a slow search; nothing is written when nothing is focused, when the focused target has been removed, or doesn't exist in your current mode (both can only happen mid-hold; it still shows as focused but unusable), or on a tick where you're dead. Uncredited detail: TD-209 item 2.*
- `NearestBeyond` / `NearestBeyondRangeMm`: reuse Story 014's candidate pass — track the nearest anchor that fails only the range test, within `6000 mm`, inside the cone and unoccluded; this widens the grid query to a 6 m radius (7 × 7 = 49 cells at 2 m) **only on ticks with no focus**; when something is focused no extra cells are visited. Update Story 014's `focusCellsVisited` probe to allow ≤ 49 on no-focus ticks. *Per the A5 listing (Evan 2026-11-04 6:47 AM PT, card click): on a tick starting with nothing in focus the search covers 49 cells instead of 25 with at most 4 extra line-of-sight checks; the candidate cap goes 128 → 256 every tick (ADR-0014 erratum 2026-11-04, l.237).* *TD-209 item 4, Approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): on a tick that starts with nothing in focus, the search reaches 3 map squares (2 m each) in every direction, or the normal reach if that's ever bigger; on other ticks it uses the normal reach; the 6 m limit is a fixed number in code, not a game setting; the pick is the closest target that is allowed here, in your view cone, beyond its own reach and within 6 m; line of sight is tested nearest first, and the result is empty if none pass; story 014's budget test expects 49 squares on the first tick and 25 on the second. (What the too-far target holds on a tick that starts with something focused is open point (ii), pending.) Uncredited detail: TD-209 item 4.*
- Do not expose the `AnchorRegistry` or `IInteractable` through the query; the HUD gets data only.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Rendering the prompt (`production/epics/hud-notifications/`, stories 001 AC-4 and 003). The hold ring, lock icon, "Too far" pulse and caret, and the focus bracket are deferred to M0+ (ADR-0018 §6 l.130, l.134; hud-notifications 003 l.68 excludes them); until then story 026's feed (`TooFarCaret`, `TooFarPulse`) and its debug overlay stand in. Story 026 supplies the projected anchor/bounds.
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
- Integration (same file): `Publish_PressAndHold_MatchesTheTickTable` on the `PressRig` with the publisher bound; coverage target: one named test per AC (lean-testing), not a line percentage.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 014, 016, 017 (state sources), 011 (`ProgressPm`, `Players/Hold.cs:84`), 013 (`reason.close_panel`), `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`; 017 is Complete (PR #60, merged as main 84ff4ee): `HoldAdvanceStep`, `EdgeResolveStep.Interaction`, `CancelReason` 8–11
- Unlocks: input-controls epic (router reads `IInteractionQuery`: `production/epics/input-controls/story-007-router-shell-press-machines-mute.md`, `production/epics/input-controls/story-012-interact-latch-toggle-translation-sprint.md`); manual-delivery epic (prompt `WritePrompt`); HUD prompt (hud-notifications 001, 003; the ring, bracket and too-far caret are M0+, ADR-0018 §6 l.130, l.134); Story 025
