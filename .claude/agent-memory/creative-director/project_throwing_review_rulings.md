---
name: throwing-review-rulings
description: CD senior-review rulings from the 2026-10-16 full design-review of throwing-catching.md (dice, debounce, catch preemption, cooldown, scope)
metadata:
  type: project
---

Verdict NEEDS REVISION (2026-10-16). Rulings, several reversing my own CD-GDD-ALIGN calls from the same day:
- D1: no dice in the Sim, and the `throwSpreadMrad` knob is RETIRED. At 0–15 mrad it can never move a throw into a wrong door (volumes 3–6 m apart). Execution variance comes from human aim and haste, with aim sensitivity growing with range, plus moving bodies. A wrong door happens only after a red release frame. Revisit with non-random levers only. `verdictRangeM` (withhold the verdict past 12 m) is deferred to OQ because it would amend the spec 13 §4.1 / Mail R13 rule that a mismatch is never hidden, so it needs the owner.
- D2: colour, pattern, marker and glyph are exact every frame. Only label text, the live region and sound debounce (100 ms). The opt-in `previewSettleMs` setting shows a neutral pending marker, never the old verdict.
- D3: catch-first Interact stays, but it is gated. It never steals a hold already in progress, and never fires when the flight's static outcome is the focused interactable.
- D4: drop the "Heads up, Jules" label line. The hat pin carries the name, and the label stays two lines.
- D5: host cooldown uses a GCRA/leaky bucket (TAT), not client clientTick gaps.
- D6: ruled OVERSIZED, with ≤ 0 % net growth. Move content only to destinations that already exist, otherwise compress it in place.

**Why:** Pillar 2 honesty means the preview on the release frame must never be stale. A knob that cannot do its job is dead weight. Fellowship #1 protects the catch.
**How to apply:** reuse for the Input, Mail and Manual Delivery re-reviews. Hand share (units, player-driven, ≥ 20 %) is the one shared revisit metric. Related: [[mail-destinations-review-rulings]] [[project-autonomous-mode]]
