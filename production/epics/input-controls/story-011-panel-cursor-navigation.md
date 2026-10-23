# Story 011: Panel navigation without a mouse (R8) — grid cursor stepping, origin landing, edge bump / cross, per-panel memory and pointer hand-off

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Deferred (Evan 2026-10-23 8:10 AM PT)
> **Deferred to**: M1 (accessibility pass) (out of the M-1a slice; was Ready)
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **DEFERRED 2026-10-23 by Evan, not deleted.** Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E1 (ADR-0012)**. Reason: R8 mouse-free panel navigation is an accessibility feature; mouse and keyboard panel use stays in input-controls 010/016. Re-run `/story-readiness` when M1 (accessibility pass) planning starts; the text below is unchanged from the last Ready/Blocked version.

## Context

**GDD**: `design/gdd/input-controls.md` (R8 Panel navigation without a mouse, R6 Grid cursor row, F5, F9 `pointerReclaimPx`; ACs H-R8-1, H-R8-2 pointer clause)
**Requirement**: no TR-ID in the registry for R8 — cited by GDD rule R8 and AC H-R8-1 / H-R8-2 (the navigation *behaviour* is Input & Controls'; the panel *screen* is Grid Inventory story 023/024)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§7 "One navigation owner per context (the custom grid consumes its own navigation)"; §5 F5 on the ms clock) — Accepted 2026-10-16 (primary); ADR-0019 (the panel is one custom-drawn `Control`; views are inert until `Initialize(deps)`) secondary
**ADR Decision Summary**: Exactly one navigation owner per context: in Panel / Holding-open the custom-drawn grid consumes its own navigation, driven by the router's `GridCursor` steps. The cursor logic is plain C# over a small port describing the grid, so it is testable headless; the Godot grid view only draws the cursor and reports the cell under the pointer.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None for the model. Pointer position comes from the grid view (Presentation) through the port; mouse-motion counts come from the router's own records.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: one navigation owner per context; game Controls take `FocusMode.None` (ADR-0012 §7)
- Forbidden: Godot `ui_*` navigation inside the game panel; wrapping at panel edges; floats
- Guardrail: 0 B per step; cursor moves render on the next frame (Game Feel)

---

## Acceptance Criteria

*From GDD R8 and H-R8-1 / H-R8-2, scoped to this story (own panel at M-1a; the external panel, LB switch (M0) and header focus (M1) are model-supported but unregistered):*

- [ ] **H-R8-1.** Over an 8-wide row containing a 2×2 item: a right step lands on the item's **origin cell only** (the other cells of a multi-cell item are skipped), never wraps, and at the edge the first push stops with a bump (`StepResult.Bumped`); a **new** push, or a push held for `edgeDwell`, crosses to the other panel (`StepResult.Crossed`) landing on the nearest row, and the last cell per panel is remembered and restored on return; stack→stack (D-pad) and cell→cell (RS, arrows) are distinct modes; in Holding-open the D-pad jumps to the next position where the held entry fits (PROPOSED) through the placement port. Test with a two-panel fake model even though only the own panel is registered at M-1a.
- [ ] **H-R8-2 (pointer clause).** With KBM, an arrow press hands the cursor to the keyboard; pointer motion of 23 px accumulated keeps it there and 24 px reclaims it for the pointer (`pointerReclaimPx` 24, raw counts from the router's mouse-motion records accumulated since the last arrow press); header focus (↑ from the top row) is **not** implemented (M1) and the model returns `NoHeader`.
- [ ] **Port.** `IPanelNavModel` (Client.Core) exposes per panel: dimensions, `TryGetOrigin(cell, out origin)`, `ItemAt(cell)`, `NextFit(shape, from, direction)` (Holding-open), `PanelCount`; the grid view in Grid Inventory story 023/024 implements it over the confirmed mirror. The cursor model owns `CursorPanel`, `CursorCell`, `LastCell[panel]`, `Owner` (Keyboard | Pointer), and never reads input directly — it consumes `GridCursor` steps (with `StepResult`) and pointer-moved notifications.
- [ ] **Same keys, one owner.** The cursor never consumes WASD (movement) — guaranteed by story 006's validator, asserted again here by feeding W / A / S / D in Panel and observing walk intent and an untouched cursor; arrows and D-pad/RS do not move the carrier.

---

## Implementation Notes

*Derived from GDD R8 and F5:*

- Files in `src/PerformativeMail.Client.Core/Input/Panel/`: `PanelCursor.cs`, `IPanelNavModel.cs`, `CursorOwner.cs`. Consumed by `production/epics/grid-inventory/story-024-panel-interactions-feedback.md` (Blocked) and `production/epics/grid-inventory/story-023-panel-grid-view-hotbar.md` (Blocked) — those stories implement the port and draw the cursor; neither is edited here.
- Cell→cell steps use RS / arrows; stack→stack uses the D-pad (jump to the next occupied origin in reading order). Both feed F5 repeat (story 010); edge crossing uses the dwell result from `StepRepeater`.
- Cursor-bump *feedback* (the "bump tick" sound / visual) is a cue id (`LogKind.Cue`); the feedback itself is Grid Inventory's M-1a cue set (story 024).
- Pointer hand-off uses accumulated `|dx|+|dy|` (or the squared distance) since the last arrow press; compare in integer px × `uiScale` permille. The 24 px figure is scaled like F7.
- The model is deliberately dumb about *what* is in a cell; the port answers it.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Drawing, hit-testing and the actual `IPanelNavModel` implementation (Grid Inventory 023/024). Pick / place / rotate semantics (Grid Inventory ops, story 013 queue). M0: LB focus switch and external panel; M1: header buttons, Home / End, PgUp / PgDn, action menu (R7).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/PanelCursorTests.cs` (`[Trait("Id", "H-R8-1")]`, `[Trait("Id", "H-R8-2")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 010 (`GridCursor` steps, `StepResult`), Story 008 (Panel routing). The pointer-travel counter is local to `PanelCursor`; story 017's glyph-flip travel accumulator is a separate, independent counter
- Unlocks: `production/epics/grid-inventory/story-024-panel-interactions-feedback.md`, `production/epics/grid-inventory/story-023-panel-grid-view-hotbar.md` (both Blocked on other grounds)
