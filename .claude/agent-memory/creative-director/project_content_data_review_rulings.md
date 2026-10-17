---
name: content-data-review-rulings
description: Content & Data Definitions rulings — CD-GDD-ALIGN (2026-10-16) and full design-review senior rulings (2026-10-16): catalogue scope, OQ-CDD-13 vehicle reversed to personal perk, ThrowRangePermille kept
metadata:
  type: project
---

CD-GDD-ALIGN on design/gdd/content-data-definitions.md (2026-10-16): verdict CONCERNS.
- FT-3 only guards the StatSheet; non-stat meta channels (Draft rerolls, kits, starting cash) must meet the same Pillar 4 test: rank-gated = sidegrade or opt-in with a cost.
- Design clamps belong in balance data; only correctness bounds stay in code (spec 13 M4 "tuning in data only").
- Missing ViewCatalog entry = placeholder + warning; error only in release-export check.
- Content hash is a mismatch detector, not security.

Full /design-review senior verdict NEEDS REVISION (2026-10-16). Rulings:
- OQ-CDD-13 vehicle REVERSED (my earlier "Second Opinion stamp" was wrong: stamps are lobby-wide, rerolls per-player, stamp XP bonus inverts the cost). Now: Rank 4 unlocks personal draft perk "Second Opinion" → new personal Stat `DraftRerolls` (Add, base 1, clamp 0..3), costs a pick slot. Pending owner ratification (amends spec 11 §11).
- Catalogue scope: code registry is single source; GDD keeps full rows only for C&D-owned rules and owners lacking numbered rule tables (Mail, Grid, unwritten GDDs); PC/Throwing/HUD/Sim Core collapse to one index row each. Duplicated rule text caused all drift.
- `ThrowRangePermille` kept (owner Throwing rejected `…Pm`); `Permille` suffix accepted as equivalent. Validator alias for Throwing printed as `THROW-CV-nn`.
- Hot reload v1 = reload-and-restart only, refused with peers; in-place swap and networked reload deferred to OQ.
- Manifest generated at export, not committed; source runs enumerate + ordinal sort.
- Source generators allowed (compile-time); runtime reflection discovery banned.
- CV-PRK-10 stays Error (Int128 overflow correctness), bound honours maxStacks/excludes.
- Cannon Arm: spec 10/13 promise "range +50%" but Throwing delivers ~×1.30 → owner ratification; cards must show outcomes, not raw stats.
- Respawn key = `player.respawnMs` (PC owner); Second Wind value lives in the perk def only.

**Why:** solo dev + friends; Pillar 4 anti-pillar "Postal Rank never makes a run easier"; Pillar 2 honesty (card = outcome).
**How to apply:** check Meta-Progression, Perks, Stamps, Throwing re-reviews against these; related [[project-autonomous-mode]] [[throwing-review-rulings]].
