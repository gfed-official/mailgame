# Story 012: Deliver prompt accessibility and localisation — the screen-reader settle gate (M-MD4-1e), test-locale rendering, the no-concatenation lint and the `a11y.deliver.*` keys

> **Epic**: Manual Delivery
> **Status**: Deferred (Mail Lead planning default, pending Evan, 2026-10-23)
> **Deferred to**: M1 or later (accessibility and localisation pass; out of the M-1a slice)
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day once ADR-0018 is Accepted and the M4 announcer exists)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split from story 010 by the m1a-replan PR)

> **DEFERRED 2026-10-23 by Mail Lead, not deleted (planning default, pending Evan: this story is not named in his approvals; Mail Review PR #47 B2 is with him).** Context only, not an approval of this story: Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". These ACs are the accessibility and localisation half of the old story 010. Re-run `/story-readiness` when the accessibility pass is planned. This story also needs ADR-0018's announcement-fired seam, which the minimal ADR-0018 draft of 2026-10-23 defers (its §5–§6), and the M4 announcer.

## Context

**GDD**: `design/gdd/manual-delivery.md` (M-UI-1 localisation half; M-MD4-1e)
**Requirement**: `TR-delivery-019` (the localisation half: keys, no concatenation), `TR-delivery-020` (the live-region announcement policy; `accessibility_live` API unsourced — schema at M-1a, announcer M4)

**ADR Governing Implementation**: ADR-0018 (Proposed 2026-10-23, minimal; the announcement seam is deferred to its amendment) — not Accepted. ADR-0014 (`PromptArgs` transport, Accepted).

---

## Acceptance Criteria

*Moved verbatim from story 010 (2026-10-16 text):*

- [ ] **Localisation (from old AC-1).** A test locale that places `{address}` first renders it without truncation in a scripted HUD stub. A lint finds no code path concatenating address fragments. Each `a11y.deliver.*` variant is its own key.
- [ ] **AC-2 (M-MD4-1e).** In screen-reader mode no ✗ hold starts before the ✗ announcement event has fired, even after T+6 (the gate reads an announcement-fired signal whose source ADR-0018 defines).

---

## Dependencies

- Depends on: Story 010 (prompt data), ADR-0018 Accepted, `production/epics/hud-notifications/story-003-deliver-prompt-display.md`
- Unlocks: —
