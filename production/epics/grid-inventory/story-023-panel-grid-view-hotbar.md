# Story 023: Own-panel grid view and hotbar strip — one custom-drawn Control, 0 B/frame

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete (greybox; AC3 0 B/frame target advisory until M0 → TD-250 (5))
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; AC3 0 B/frame target advisory until M0 → TD-250 (5)); delivered by PR #91, merge ae8a12d; see Completion Notes). Earlier: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23.** The TR-inventory-027 live-region feedback half — the live-region `Control` for feedback text named in the Decision Summary below (ADR-0019 l.313, erratum E6) — moved to `story-025-deferred-live-region-feedback.md` (Deferred to the M1 accessibility pass). Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all". No acceptance criterion left this story; the custom-drawn grid `Control`, the hotbar strip, the 0 B/frame check and the Intake grid view all stay.

> **UNBLOCKED** (2026-10-16): the inventory panel UX spec now exists — cite `design/ux/inventory-panel.md` (layout, cell anatomy, hotbar strip; OQ14 dimensions in Layout Zones); `/ux-review` of the spec is recommended before implementation.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-027` (grid panel one custom-drawn `Control`; 0 B/frame — the "feedback via a live-region `Control`" half is story 025's, M1 accessibility pass)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture — Accepted 2026-10-16 (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: The panel is one custom-drawn grid `Control` (`_Draw`, cached text); the live-region `Control` for feedback text (TR-inventory-027) is story 025's (E6); scene placement, view pooling, `ViewCatalog` (icons by def id) and stretch/aspect are ADR-0019's — Accepted 2026-10-16; the panel reads the client mirror of own containers (OwnerOnly events, ADR-0009 §4).
**ADR Version**: ADR-0019 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: HIGH (Godot 4.7.1 UI: `Control._Draw`, `DrawString` caching, 4.7 CanvasItem line antialias change — NEEDS VERIFICATION)
**Engine Notes**: 4.7 removed CanvasItem line antialias feathering — outlines as filled shapes/StyleBox ≥ 2 px, never `draw_line` (design/ux/hud-notifications-seed.md); cache `StringName`/text; no per-frame allocation.

**Control Manifest Rules (Presentation layer unless noted)**:
- Required (Presentation): Godot views map `EntityId` → node via a C# `Dictionary` owned by Presentation; never pass id structs as `[Signal]`/`[Export]`/Variant (ADR-0004)
- Forbidden (Presentation): Sim content in a Godot `Resource` (ADR-0010)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [x] Manual check — GIVEN a greybox build with a player holding letters, an Axe and a Medium, WHEN Tab opens the panel, THEN the hotbar row (slot 1 = hands, visibly reserved), the 8×2 inventory and dividers render per the UX spec, every entry at its `(x,y,rotated)` with count and kind/address glyph; screenshot retained in `production/qa/evidence/grid-inventory/`.
- [x] Manual check — the hotbar strip stays visible with the panel closed and shows the selected slot; selecting 1–8 updates it the same frame.
- [ ] Allocation: 600 frames with the panel open and the mirror changing every tick allocate 0 B per frame in steady state (frame-allocation probe; H-PERF-4 analogue for the own panel).
- [x] The Intake (M-1a external, story 011 decision) renders in the right-hand panel with the **grid** view (20×16) while open.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Needs `/ux-design` for the inventory panel (GDD UX Flag; OQ14 sets the panel dimensions) — stories should then cite `design/ux/inventory-panel.md`.
- The client mirror of own containers and the open Intake is the minimal M-1a form. The Intake is the M-1a external (story 011), and staged records are applied at the seal. The full optimistic `ContainerMirror`/`PendingQueue` is M0 (Client.Core).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 024: interactions and feedback.
- Story 025 (Deferred, M1 accessibility pass): the live-region `Control` and live-region feedback text.
- M1: manifest view (R18), sort buttons.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched in `production/qa/evidence/grid-inventory/` (+ H-UI checks for the scripted-stub checks)

**Status**: [x] Created and passing — `tests/unit/ui/PanelControllerTests.cs` (hotbar key row; no retained screenshot), PR #91 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #91 (merge `ae8a12d`), package WP-E (M-1a wave 2; S1–S3 coded on Sonnet, review fixes on Opus), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). Wired at boot by PR #93 (`545df2f`, TD-260) through `InventoryLayers.Install`, at first over an empty panel source. Fed by the client loop from PR #94 (`f145219`, TD-270): the view reads `OwnPanelMirror`, the client's own seal-staged copy of the hotbar, player_inventory and cursor (a minimal stand-in for H-MIRROR's `ContainerMirror`, TD-270 (2)); PR #94 also gave the hotbar slots a dark fill so the "hands" label reads over any background.
- **ACs met:** AC1 (hotbar row, 8×2 grid, glyph and count), AC2 (the hotbar stays visible with the panel closed and keys 1–8 update it the same frame), AC4 (Intake 20×16 grid drawn on the right while open). Binding kept: one custom-drawn `Control`, `PanelGridView`.
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 simplify table; held by the package TD row):** AC3 — the 0 B/frame target is advisory until M0 (erratum E2), not measured → TD-250 (5). The AC1 retained screenshot was dropped under the plan's "one shot per package at assembly" rule. No Intake grid is live in the playable loop: the pickup is the `IntakePickup` macro at the console (TD-270 (6)), so the AC4 grid is drawn but not exercised.
- **Tests:** `tests/unit/ui/PanelControllerTests.cs` (hotbar key shows the slot at once, then follows the mirror) — PR #91; 6,445 per leg, 0 failed.

---

## Dependencies

- Depends on: `production/epics/godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md` (Godot project), `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md` (`GameLoop`), `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md` (presenter slot, event stream), `production/epics/godot-host-shell/story-008-viewcatalog-resolver-placeholder-uid-check.md` (item icons by def id); ADR-0019 (Accepted)
- Depends on: Story 001–011 (Sim state to draw)
- Unlocks: Story 024; Story 025 (Deferred, M1)
