---
name: building-review-rulings
description: Building System first /design-review synthesis (2026-10-16) — NEEDS REVISION; placement is not a G9 commit, B8 snapshot-first, large fixture, Spawned constructs refund 0
metadata:
  type: project
---

Building System GDD first review 2026-10-16, CD synthesis verdict: NEEDS REVISION (core design sound; ~14 local text/AC fixes + hand-backs).

- Place/PlaceLine are NOT Input G9 commits (concurrent reqIds; ghost keeps tracking); Deconstruct stays a commit. Hand-back via OQ-BLD-1.
- Non-drag defs place on press (G8); drag defs commit on release.
- Drag: start tile latched in world, carrier may walk, end = clamped cursor.
- B8: step 1 snapshots record + HP and computes ratioPm before Unregister/delete; Health read query hand-back.
- Minimal Build bindings must be M1 (hand-back to Input, which lists them M2).
- Fixture FX-B-L 64x64 for line/net/cap ACs; H-R10-3 moot is RECOMMENDED only.
- Demoted: WG 20x20 clear-area definition (already defined via TileLegality in OQ-BLD-3); water/street neighbour heights (WG contract gives every tile a height).
- Apply-now RECOMMENDED: Spawned (System-owned) constructs refund 0 (faucet).

Owner-ratified, not to re-litigate: no terrain editing; remover named in feed; build ≤15 % of Delivery; no buildable spawn pads v1.

**Why:** strict BLOCKING bar = wrong behaviour / ambiguity / bad AC / broken cross-GDD contract.
**How to apply:** re-review checks these items; related [[input-controls]] G7–G9, [[health-damage-review-rulings]], [[project-autonomous-mode]].
