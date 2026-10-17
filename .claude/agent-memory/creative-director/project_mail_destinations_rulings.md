---
name: mail-destinations-review-rulings
description: CD rulings from the 2026-10-16 full design-review of mail-destinations.md (complaint scaling, honesty vs execution, grace, roll-call, feeder jam)
metadata:
  type: project
---

Rulings (2026-10-16, verdict NEEDS REVISION):
- Honesty = information, not execution: never hide an address mismatch (spec 09 §3.1); the throw arc predicts the aim at release, execution variance is Throwing's job. No "?" prompts for Cursed.
- Complaint: per-door bucket stays (money stays literal = Pillar 2); add team ceiling RH(40 × n^0.65) per 30 s; meter normalisation by n^0.65 is Run Structure's.
- Machines never soften their own mistakes: no auto-jam for misrouting feeders; bound via complaint ceiling + persistent alert.
- Closing time: ops started while OPEN resolve as OPEN within 90 ticks; feeders get no grace.
- Payday roll-call: ~1.5 s/line, press to advance, ≤ 8 s, host naming setting, ends on a positive line.
- Dead letters retire at Payday start as "returned to sender" (0 ¢, no complaint).

**Why:** Fellowship-first (laugh not blame) + Pillar 2 honesty + Pillar 5 scaling.
**How to apply:** reuse these precedents for Throwing, Run Structure, Belt Logistics, Economy GDD reviews. Related: [[project-autonomous-mode]]
