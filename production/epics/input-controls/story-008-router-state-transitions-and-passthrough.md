# Story 008: Router state transitions — panel open / close, `holding` and `body` from the published carrier status, pass-through rules and the mouse-mode request

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-21 08:40 PT (new AC "Panel-open flag": this story alone appends `InputFrameFlags.PanelOpen`, sets it exactly when `IsPanel(RouterStateId)`, and updates the `InputState.Flags` doc — owner ruling, ADR-0012 erratum 2026-10-21; Mail Review on PR #19)

## Context

**GDD**: `design/gdd/input-controls.md` (R2 layering rules 1–6, "States and Transitions" table, G3 axes clause, collision rulings 5–8; ACs H-R2-3, H-R2-4, H-G3-1, H-G3-3, H-R11-1 ordering clause, H-R6-2 (f)–(h) logic halves)
**Requirement**: `TR-input-003` (the RouterState tuple in motion: transitions read `body` / `holding` from the published status); GDD rules R2 / R6 have no further TR-ID
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§2 — `capture`, `overlay`, `submenu`, `aiming` router-owned; `body` and `holding` read each frame from the ClientLayer-published carrier status) — Accepted 2026-10-16 (primary); ADR-0014 (§7 the published `CarrierStatus` / `IInteractionQuery`) secondary
**ADR Decision Summary**: The router owns four layers and *reads* two: `body` (OnFoot / Build / Mounted / Dead) and `holding` (cursor non-empty) arrive from the ClientLayer-published carrier status. `holding` survives every overlay change; closing the panel never drops or returns the cursor entry by itself. Under an overlay the body keeps only its declared pass-through actions; a higher layer can only *disable* a lower verb, never give a lower binding a second meaning.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0014)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `GameLoop` / the router read `body` and `holding` from the ClientLayer-published carrier status; the router never reads `SimWorld` (ADR-0003 §6, ADR-0012 §2)
- Required: transitions run G3 (mute) on every button that is down (GDD States and Transitions)
- Forbidden: the router mutating `holding` itself; any `SceneTree.Paused`
- Guardrail: 0 B per frame; state evaluation is mask tests

---

## Acceptance Criteria

*From GDD R2, G3 and H-R2-3 / H-R2-4 / H-G3-1 / H-G3-3 / H-R11-1, scoped to this story (M-1a: World, Panel, Holding-open, Holding-closed, Aiming; TextEntry / Map / Station / Draft / Results / History / Build / Mounted / Dead are state-space only):*

- [ ] **Ports.** `ICarrierStatusSource { ref readonly CarrierStatus Current { get; } }` (Client.Core, over Player Character's `CarrierStatus` from `production/epics/player-character/story-013-contexts-verbs-availability-gate-router-declarations.md`) and `IClientTickSource { uint ClientTick { get; } }` are declared; the router reads `Body` and `CursorHolding` from the status at event time and at every `FlushToLatch`. `CarrierStatus.Aiming` is **ignored** by the router (aiming is router-owned; the status field only echoes the frame's aim bit — note for Player Character 020).
- [ ] **Open / close panel.** Tab / Y (Open panel, live in World and Holding-closed) toggles `overlay` None ↔ Panel: World → Panel and Holding-closed → Holding-open; Tab (live in Panel / Holding-open) closes it, Holding-open → Holding-closed. Y in Panel does **not** close it (R6 ruling 6; at M-1a Y has no Panel action). **H-R2-3:** Holding-open, Tab closes then Y reopens → the cursor entry is unchanged (`holding` stays true) and no `Return` or `Drop` immediate is queued.
- [ ] **`holding` follows the status.** A pick (status `CursorHolding` false → true) moves Panel → Holding-open; place / return / drop (true → false) moves Holding-open → Panel and Holding-closed → World; the router never sets `holding` itself. `body` Mounted / Dead (test fixtures only at M-1a) map into the tuple and a Panel-open attempt while `body ≠ OnFoot` is refused (no state change; log `GateNotLive`).
- [ ] **Panel-open flag (owner ruling 2026-10-21, ADR-0012 Amendments).** This story alone appends `PanelOpen` to `InputFrameFlags` (next free bit, 16; the existing `InputFrame` shape/`sizeof` test stays green). It also updates the router-owned flags doc on `InputState.Flags` (`src/PerformativeMail.Sim/ClientLayer/InputState.cs:55`, today "`FocusLost`, `Paused`, `LookReseeded`") to name `PanelOpen`. Over the reachable-state table and the open/close-panel script above, every `InputState` that `FlushToLatch` pushes has `PanelOpen` set **exactly when** `RouterStateInfo.IsPanel(RouterStateId)` is true for that same state (one `[Theory]` over all reachable M-1a states, plus the World → Panel → Holding-open → Holding-closed sequence).
- [ ] **H-R2-4 (pass-through).** In Panel: Shift+W and L3+LS produce a walk-speed `moveIntent` (Sprint is not live — its bit stays clear), mouse and RS motion produce **no** look change (Look not live) and instead drive the grid cursor (steps, story 010), keys 1–8 fire the hotbar-slot immediates, and the D-pad produces `GridCursor` steps, **not** hotbar steps.
- [ ] **H-G3-1.** X held at a mailbox (Interact press latched), Y pressed (panel opens) → X is muted: its release produces no quick-move and is logged `GateSwallowed`; A held from a jump when the panel opens picks nothing on release (the A press was latched to Jump, now non-live).
- [ ] **H-G3-3 (axes persist).** With LS held, the state sequence World → Panel → Holding-open → (Tab) Holding-closed keeps `moveIntent` non-zero throughout (axes are state, not edges); the capture layer (Pause, story 016) zeroes it. (The TextEntry / Map / Draft / turret / Dead zeroing is M0+ and not exercised.)
- [ ] **H-R11-1 (ordering).** Jump pressed, then Tab pressed, inside one tick → the flushed batch holds a Jump Press edge gated against World (`RouterStateId` of World) **followed by** the state change; the panel opens and the Jump is not re-gated against Panel.
- [ ] **Aim cancels on transition.** Tab / Y pressed while aiming (story 014 sets `aiming`) opens the panel, clears `aiming`, emits `Cancel(StateChange)` for the Aim machine and queues **no** Throw; the physical Throw release is swallowed.
- [ ] **H-R6-2 (g)(h)(j-partial) disjoint-state table.** A → Jump in World and Holding-closed, Pick in Panel / Holding-open; LMB → Tool (stub) in World, pick in Panel, "Put it away first" cue in Holding-closed; Enter and NumpadEnter → Pick/place in Panel (TextEntry is M0). One `[Theory]` row per cell.
- [ ] **Mouse-mode request.** The router exposes `MouseModeRequest` (`Captured` in World / Holding-closed / Aiming, `Visible` in Panel / Holding-open / Pause) for the adapter to apply (story 027); it changes only on a state change.

---

## Implementation Notes

*Derived from ADR-0012 §2 and GDD R2 / States and Transitions:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `InputRouter.State.cs` (partial), `TransitionTable.cs` (data: `(trigger action, from-predicate) → overlay change`), `CarrierStatusSourceStub.cs` for tests, `MouseModeRequest.cs`.
- Transitions are data-driven: the table lists `(ActionId, predicate) → (layer, newValue)`; the router applies the first matching row after `Feed` resolves a fired action. After any change it calls `OnStateChanged(old, new)` (story 007) so G3 runs on every button that is down, and re-computes `MouseModeRequest`.
- Read the published status through `ICarrierStatusSource` (the Players client step publishes it each tick — Player Character 020); tests use a settable double. Do not subscribe or poll anything else.
- The external panel, leash auto-close, mount / Build auto-stow (R2 rule 6) and knockdown transitions are M0 / M1 / M-1b; the table has reserved rows but no registered actions.
- **Panel-open flag (owner ruling 2026-10-21, ADR-0012 Amendments; AC above).** Set it from `overlay == Panel` when building the `InputState`; Player Character 020 reads it into `CarrierStatus.PanelOpen` and does not add the member itself.
- Event-time gating: when two records arrive in one frame the state read for the *second* is the state after the first's transition (Jump then Tab → Jump gated World; a following press is gated Panel).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 016: Back ladder, Pause capture, P / Menu. Story 014: entering Aiming. Story 018: focus-loss state effects. M0: TextEntry, external panel and LB focus switch, MP Pause overlay. M1: History, action menu (submenu). M2: Map, Build. Dead/Mounted behaviour beyond the tuple (M-1b / M1).
- Applying the mouse mode in Godot (story 027).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/RouterTransitionsTests.cs` (`[Trait("Id", "H-R2-3")]`, `H-R2-4`, `H-G3-1`, `H-G3-3`, `H-R11-1`), `tests/unit/input/DisjointStateBindingsTests.cs` (`[Trait("Id", "H-R6-2")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007 (router shell), Story 005/006; `production/epics/player-character/story-013-contexts-verbs-availability-gate-router-declarations.md` (`CarrierStatus`, `VerbContextTable` — declared there, bound here)
- Unlocks: Stories 009–019; `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md` (`InputFrameFlags.PanelOpen`, set here — ADR-0012 erratum 2026-10-21); `production/epics/grid-inventory/story-024-panel-interactions-feedback.md` (consumer of Panel / Holding; Blocked on its UX spec — not a dependency of this story)
