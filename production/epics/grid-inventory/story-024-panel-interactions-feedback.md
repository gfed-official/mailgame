# Story 024: Panel interactions through the Panel/Holding input contexts, and the M-1a feedback cues

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M–L (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23.** The live-region arm of H-UI-1 (each feedback row's exact text in the live region; TR-inventory-027, ADR-0019 l.313, erratum E6) moved verbatim to `story-025-deferred-live-region-feedback.md` (Deferred to the M1 accessibility pass). Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all". What stays here is every interaction, every reachable rejection's visible revert, cue id and exact text in the UI event log, and the Holding rules. The dependency on input-controls 011 (mouse-free panel cursor navigation, Deferred by E1) is dropped: the M-1a panel is mouse-driven.

> **UNBLOCKED** (2026-10-16): the inventory panel UX spec now exists — cite `design/ux/inventory-panel.md` (Interaction Map and its M-1a feedback-row table, IP-AC-8…13); Story 023 remains an ordinary Depends-on.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-027` (visible revert and non-colour cue per rejection; the "feedback via live-region `Control`" half is story 025's, M1 accessibility pass); input contexts consume `TR-input-*` (router)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture — Accepted 2026-10-16 (primary); ADR-0012: Input router core & Godot input adapter — Accepted 2026-10-16; ADR-0009: InventoryOp Pipeline and Inventory Storage Layout
**ADR Decision Summary**: Pick/place/drag/rotate/return/drop/quick-move are issued as inventory ops from the Panel and Holding contexts that Input & Controls' router owns (ADR-0012, Accepted 2026-10-16); the panel scene is ADR-0019's (Accepted 2026-10-16); toasts route through HUD composition (ADR-0018, Accepted 2026-10-23; its M-1a slice defers the toast queue to the M0+ amendment, §6); every reachable rejection has a visible revert and a non-colour cue (GDD UI Requirements feedback table).
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
- [ ] **H-UI-1 (M-1a rows)** — against a scripted Sim stub, each M-1a-reachable reason (`blocked`, `stack_full`, `no_room`, `out_of_bounds`, `not_allowed`, `reserved_slot`, `cursor_busy`/`cursor_empty`, `swimming`, `not_open`/`out_of_range`, `busy`) produces its row's revert, cue id and exact text in the UI event log; `bad_request` logs only. (The live-region arm is story 025's.)
- [ ] Holding survives closing the panel (held-entry chip beside the hotbar); a drag released outside the panel is a `return`, never a drop.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Contexts: World / Panel / Holding-open / Holding-closed per GDD UI Requirements; concrete bindings, remap and the Back ladder are Input & Controls'. Mouse-free panel cursor navigation (input-controls 011) is Deferred to M0/M1 (E1): tap, drag and the keyboard verbs (R, Esc, Q, Shift-click, 1–8) are all the panel needs at M-1a, so build no cursor-navigation helper here.
- Contention cues, partial-result toasts, sort/take-all/split controls are M0/M1.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 023: the view.
- Story 025 (Deferred, M1 accessibility pass): the live-region `Control` and the live-region arm of H-UI-1.
- M0/M1: contention, manifest, sort, split, take_all controls; mouse-free panel cursor navigation (input-controls 011).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: a retained screenshot of each screen touched in `production/qa/evidence/grid-inventory/` (+ H-UI-1 stub checks against the UI event log for the scripted-stub checks)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 023, Story 019, Story 020
- Depends on: `production/epics/input-controls/story-007-router-shell-press-machines-mute.md`, `production/epics/input-controls/story-010-steps-auto-repeat-drag-resolver.md` (router, ADR-0012; input-controls 011 panel cursor navigation is Deferred and not needed for the mouse-driven M-1a panel). Not a dependency: the HUD toast queue — ADR-0018 is Accepted (2026-10-23 8:10 AM PT), but its minimal M-1a slice defers the queue to the M0+ amendment (§6), and this story's ACs do not need it.
- Unlocks: Epic DoD UI evidence; Story 025 (Deferred, M1)
