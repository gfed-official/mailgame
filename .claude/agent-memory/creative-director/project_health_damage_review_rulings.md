---
name: health-damage-review-rulings
description: Health & Damage GDD first /design-review (2026-10-16) senior rulings — verdict NEEDS REVISION, 10 merged blockers, deferred OQs, fantasy concerns
metadata:
  type: project
---

First review of design/gdd/health-damage.md (2026-10-16), verdict NEEDS REVISION (pending owner ratification per [[autonomous-mode]]).

Rulings:
- Thrown protection clear is contact-based (Throwing TC-X-4 phase 4, any HIT_ENEMY), ignores dealt; "no protection end on overkill" applies only to weapon/turret.
- Raise order = ascending phase, composition order within a phase; Defenses at its bound phase (not listed between 4 and 6).
- HitConfirm/OwnHp amount = dealt. Net stamp design (tick & 0xFF) judged broken after >127-tick silence; direction: rely on O1 reliable-ordered channel or carry a wider tick.
- AC-21 split: zero-alloc blocking, p99 advisory until Sim Core OQ6 defines reference machine.
- Deferred: deliberate empty-handed knockdown as free full heal (kill criterion into OQ-HD-1); 6-Barbarian burst vs "flip red then choose" window; hotSlots 3 vs "no heal maths"; Downed-before-allow-table order.
- Player Fantasy fix: "goons drop in a few hits" scoped to regular goons; Giants/Mega Giants are a team job.

**Why:** strict BLOCKING bar (wrong behaviour/ambiguity/untestable AC/broken contract); budget ≤5 % growth, ≤500 lines.
**How to apply:** at re-review, check these specific fixes; do not re-litigate deferred items unless playtest data arrives. Related: [[throwing-review-rulings]], [[simcore-review-rulings]].
