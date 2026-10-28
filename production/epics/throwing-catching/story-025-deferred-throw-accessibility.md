# Story 025: Throw accessibility — the polite live-region lines for the arc label, and label/marker contrast (TC-U-3 b)

> **Epic**: Throwing & Catching
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M1 or later (accessibility pass; out of the M-1a slice)
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split from stories 005 and 019 by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". The ACs below moved verbatim from stories 005 and 019. Re-run `/story-readiness` when the accessibility pass is planned; it needs the HUD live-region seam (ADR-0018, Proposed).

## Context

**GDD**: `design/gdd/throwing-catching.md` (UI Requirements; TC-U-3 b; live-region rules), `design/gdd/hud-notifications.md` (H26 muted cues)

---

## Acceptance Criteria

*Moved verbatim:*

- [ ] **Live-region line (from story 005).** The same 100 ms stability gate emits at most one live-region line per second (superseded lines dropped); the line is built from the label keys.
- [ ] **Live region (from story 019).** At most one polite live-region line per second built from the label keys, superseded lines dropped, muted cues following HUD H26; `AccessibilityLive` set with the 4.7 typed enum. Screen-reader throwing is out of scope for v1.
- [ ] **TC-U-3 b (from story 019).** Measured label contrast ≥ 4.5:1 and marker outline ≥ 3:1 (reported in the evidence file).

---

## Dependencies

- Depends on: Story 005, Story 019; ADR-0018 Accepted (live-region seam)
- Unlocks: —
