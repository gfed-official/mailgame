# Story 023: Own-panel grid view and hotbar strip — one custom-drawn Control, 0 B/frame

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

> **UNBLOCKED** (2026-10-16): the inventory panel UX spec now exists — cite `design/ux/inventory-panel.md` (layout, cell anatomy, hotbar strip; OQ14 dimensions in Layout Zones); `/ux-review` of the spec is recommended before implementation.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-027` (grid panel one custom-drawn `Control`; 0 B/frame; feedback via a live-region `Control`)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture — Accepted 2026-10-16 (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: The panel is one custom-drawn grid `Control` (`_Draw`, cached text) plus a live-region `Control` for feedback text (TR-inventory-027); scene placement, view pooling, `ViewCatalog` (icons by def id) and stretch/aspect are ADR-0019's — Accepted 2026-10-16; the panel reads the client mirror of own containers (OwnerOnly events, ADR-0009 §4).
**ADR Version**: ADR-0019 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: HIGH (Godot 4.7.1 UI: `Control._Draw`, `DrawString` caching, 4.7 CanvasItem line antialias change — NEEDS VERIFICATION)
**Engine Notes**: 4.7 removed CanvasItem line antialias feathering — outlines as filled shapes/StyleBox ≥ 2 px, never `draw_line` (design/ux/hud-notifications-seed.md); cache `StringName`/text; no per-frame allocation.

**Control Manifest Rules (Presentation layer unless noted)**:
- Required (Presentation): Godot views map `EntityId` → node via a C# `Dictionary` owned by Presentation; never pass id structs as `[Signal]`/`[Export]`/Variant (ADR-0004)
- Forbidden (Presentation): Sim content in a Godot `Resource` (ADR-0010)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] Manual check — GIVEN a greybox build with a player holding letters, an Axe and a Medium, WHEN Tab opens the panel, THEN the hotbar row (slot 1 = hands, visibly reserved), the 8×2 inventory and dividers render per the UX spec, every entry at its `(x,y,rotated)` with count and kind/address glyph; screenshot retained in `production/qa/evidence/grid-inventory/`.
- [ ] Manual check — the hotbar strip stays visible with the panel closed and shows the selected slot; selecting 1–8 updates it the same frame.
- [ ] Allocation: 600 frames with the panel open and the mirror changing every tick allocate 0 B per frame in steady state (frame-allocation probe; H-PERF-4 analogue for the own panel).
- [ ] The Intake (M-1a external, story 011 decision) renders in the right-hand panel with the **grid** view (20×16) while open.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Needs `/ux-design` for the inventory panel (GDD UX Flag; OQ14 sets the panel dimensions) — stories should then cite `design/ux/inventory-panel.md`.
- The client mirror of own containers (staged records applied at the seal) is the minimal M-1a form; the full optimistic `ContainerMirror`/`PendingQueue` is M0 (Client.Core).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 024: interactions and feedback.
- M1: manifest view (R18), sort buttons.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched in `production/qa/evidence/grid-inventory/` (+ H-UI checks for the scripted-stub checks)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md` (Godot project), `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` (`GameLoop`), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (presenter slot, event stream), `production/epics/godot-host-shell/story-008-viewcatalog-resolver-placeholder-uid-check.md` (item icons by def id); ADR-0019 (Accepted)
- Depends on: Story 001–011 (Sim state to draw)
- Unlocks: Story 024
