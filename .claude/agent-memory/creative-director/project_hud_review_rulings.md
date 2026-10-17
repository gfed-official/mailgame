---
name: hud-review-rulings
description: CD rulings from the 2026-10-16 /design-review of hud-notifications.md (comms split frozen behind GUARD, seed file, M-1a slice, Until-dismissed cut, Essentials only)
metadata:
  type: project
---

Verdict MAJOR REVISION NEEDED (2026-10-16). Rulings:
- Comms (ping/chat/emote/wheel, comms network) moved verbatim to design/gdd/comms.md, status frozen until the M-1a two-friend GUARD clears; no /design-review of comms before then. Network blockers recorded there, not fixed.
- Non-comms MOVE content parked in one seed file (design/ux/hud-notifications-seed.md, marked unreviewed) with one-line pointers; HUD target ≤ 800 lines.
- M-1a HUD: time-only timer, wallet via one money formatter, prompt + bracket, pills with reserved own slot, single replace-latest toast, hotbar frame + held chip (stays in HUD, not Inventory UI), cue schema with a11y fields (no live-region stub until M4).
- Cut: "Until dismissed" (history is the untimed equivalent), ping ack, Hidden mode (keep Essentials, never hides own verdicts), 1.25x quota track, per-category mutes. Kept duplicate-name suffixing (non-visual attribution).

**Why:** S-sized UI system; comms is a second system whose value depends on the unrun M0 Fellowship test.
**How to apply:** precedent for other oversized GDDs: split-and-freeze rather than fix speculative later-milestone content. Related: [[project-autonomous-mode]], [[mail-destinations-review-rulings]]
