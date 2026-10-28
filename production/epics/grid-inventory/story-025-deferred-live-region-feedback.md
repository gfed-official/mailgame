# Story 025: Inventory panel live-region feedback (TR-inventory-027) — the live-region `Control` and the H-UI-1 live-region arm

> **Epic**: Grid Inventory & Containers
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M1 accessibility pass (out of the M-1a slice; split from stories 023 and 024)
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E6 (ADR-0019 l.313: the TR-inventory-027 live-region feedback moves to the M1 accessibility pass)**. Stories 023 (the custom-drawn grid view, 0 B/frame) and 024 (panel interactions, visible revert and cue per rejection) keep everything else; the AC below moved verbatim from story 024 and the live-region `Control` seam from story 023's Decision Summary. Re-run `/story-readiness` when the M1 accessibility pass is planned (share the live-region conventions with `production/epics/throwing-catching/story-025-deferred-throw-accessibility.md`).

## Context

**GDD**: `design/gdd/grid-inventory.md` (UI Requirements feedback table; L535/L543)
**Requirement**: `TR-inventory-027` (feedback via a live-region `Control` — this half; "one custom-drawn `Control`; 0 B/frame" stays in story 023)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0019: Godot presentation scene architecture (§6 grid panel; l.313 "one custom-drawn Control; 0 B/frame; live-region feedback") — Accepted 2026-10-16
**ADR Decision Summary**: The panel is one custom-drawn grid `Control` (story 023) **plus a live-region `Control` for feedback text (TR-inventory-027)** — this story adds the live-region `Control` and routes every M-1a feedback row's exact text through it.
**ADR Version**: ADR-0019 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: HIGH (Godot 4.7.1 UI; the 4.7 typed `AccessibilityLive` enum — NEEDS VERIFICATION)

---

## Acceptance Criteria

*Moved verbatim:*

- [ ] **Live-region `Control` (from story 023 Decision Summary).** The panel scene has a live-region `Control` for feedback text (TR-inventory-027) beside the custom-drawn grid `Control`; it allocates 0 B per frame when idle.
- [ ] **H-UI-1 (M-1a rows, live-region arm) (from story 024).** Against a scripted Sim stub, each M-1a-reachable reason (`blocked`, `stack_full`, `no_room`, `out_of_bounds`, `not_allowed`, `reserved_slot`, `cursor_busy`/`cursor_empty`, `swimming`, `not_open`/`out_of_range`, `busy`) produces its row's exact text in the live region; `bad_request` logs only.

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- UI: H-UI-1 stub checks extended to the live region; a retained screenshot in `production/qa/evidence/grid-inventory/`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 023 (the panel scene), Story 024 (the feedback rows, reverts and cue ids this story mirrors into the live region); the M1 accessibility pass (live-region conventions shared with throwing-catching 025)
- Unlocks: —
