# Story 024: Panel interactions through the Panel/Holding input contexts, and the M-1a feedback cues

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M–L (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

> **UNBLOCKED** (2026-10-16): the inventory panel UX spec now exists — cite `design/ux/inventory-panel.md` (Interaction Map and its M-1a feedback-row table, IP-AC-8…13); Story 023 remains an ordinary Depends-on.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-027` (feedback via live-region `Control`); input contexts consume `TR-input-*` (router)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture — Accepted 2026-10-16 (primary); ADR-0012: Input router core & Godot input adapter — Accepted 2026-10-16; ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: Pick/place/drag/rotate/return/drop/quick-move are issued as inventory ops from the Panel and Holding contexts that Input & Controls' router owns (ADR-0012, Accepted 2026-10-16); the panel scene is ADR-0019's (Accepted 2026-10-16); toasts route through HUD composition (ADR-0018, not written); every reachable rejection has a visible revert and a non-colour cue (GDD UI Requirements feedback table).
**ADR Version**: ADR-0019 2026-10-16; ADR-0012 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: HIGH (Godot 4.7.1 input + UI; NEEDS VERIFICATION)
**Engine Notes**: Input contexts and `_Input` handling per ADR-0012 once written.

**Control Manifest Rules (Presentation layer unless noted)**:
- Required (Presentation): the host's own client uses the loopback adapter like a remote peer — no "is host" shortcut (ADR-0011)
- Forbidden: calling the Sim directly from Godot UI (ADR-0011 Alt 2)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] Manual check — tap-to-lift / tap-to-place and drag both move an entry; R rotates while Holding; Esc/RMB returns (`return`); Shift-click quick-moves own panel ↔ hotbar and Intake → panel; Q drops the focused/held entry; slot 1 refuses with "Slot 1 is your hands"; screenshots retained.
- [ ] **H-UI-1 (M-1a rows)** — against a scripted Sim stub, each M-1a-reachable reason (`blocked`, `stack_full`, `no_room`, `out_of_bounds`, `not_allowed`, `reserved_slot`, `cursor_busy`/`cursor_empty`, `swimming`, `not_open`/`out_of_range`, `busy`) produces its row's revert, cue id and exact text in the UI event log and the live region; `bad_request` logs only.
- [ ] Holding survives closing the panel (held-entry chip beside the hotbar); a drag released outside the panel is a `return`, never a drop.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Contexts: World / Panel / Holding-open / Holding-closed per GDD UI Requirements; concrete bindings, remap and the Back ladder are Input & Controls'.
- Contention cues, partial-result toasts, sort/take-all/split controls are M0/M1.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 023: the view.
- M0/M1: contention, manifest, sort, split, take_all controls.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched in `production/qa/evidence/grid-inventory/` (+ H-UI-1 stub checks for the scripted-stub checks)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 023, Story 019, Story 020
- Depends on: `production/epics/input-controls/story-007-router-shell-press-machines-mute.md`, `production/epics/input-controls/story-010-steps-auto-repeat-drag-resolver.md`, `production/epics/input-controls/story-011-panel-cursor-navigation.md` (router, ADR-0012); HUD toast queue (ADR-0018 — not written; no HUD epic exists)
- Unlocks: Epic DoD UI evidence
