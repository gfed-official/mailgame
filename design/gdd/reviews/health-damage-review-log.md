# Health & Damage — Review Log

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: S (single Core system, 5 formulas, provisional consumers; ADR candidate "Health component, damage queue & resolve")
Specialists: game-designer, systems-designer, network-programmer, qa-lead, creative-director (senior)
Blocking items: 10 (merged from 15) | Recommended: 12 cheap (applied) + 7 deferred to Open Questions
Summary: The rule set, the formula arithmetic (F1–F5) and the AC-03/04/11/13/14/17 counts are sound, and the direction is unchanged. The defects were a contradiction with Throwing's contract that clears spawn protection on HIT_ENEMY contact, a wrong Berries first-tick claim, a `CanHeal` that refused Bandages, and wrong or untestable ACs. Several Network throttle and ordering rules were also ambiguous: 8-bit stamp compare, Restore vs the Dead-ignore rule, the HitConfirm fold, and the budget. The creative-director flags deliberate empty-bag knockdowns (10 s vs 42.5 s regen) as the main fantasy risk and adds a kill criterion to OQ-HD-1.
Prior verdict resolved: First review
Findings:
- [BLOCKING] HD7 / Edge Cases / HD-AC-22: a thrown HIT_ENEMY clears protection in phase 4 regardless of `dealt`; the doc claimed an overkill or takenPm-0 throw did not
- [BLOCKING] HD4 step 1: illustrative raise order put Defenses between phases 4 and 6, against Dependencies contract 4 (phase 7)
- [BLOCKING] HD4 / HD-AC-15 / HD-AC-12 / registry healOverTime: Berries gain 0 at k = 1 (first +1 at k = 6); the AC-12 probe at T0+150 could not discriminate
- [BLOCKING] HD2 CanHeal: full HoT slots refused a Bandage; now `CanHeal(id, kind)`
- [BLOCKING] HD13 / ACs: Debug-assert inputs aborted tests; SimAssert capture sink (hook → Sim Core)
- [BLOCKING] HD-AC-23 / HD-AC-25: vacuous overkill and an off-by-one protection boundary; "nothing else" was false
- [BLOCKING] Network O1/O3/O4/O5, HD-AC-30: an 8-bit stamp compare dropped messages after > 127 quiet ticks; Restore could be ignored as Dead; OwnHp is owner-only
- [BLOCKING] Network OwnHp/batch throttles: no baseline for the gap or the change threshold, and no coalesced cause defined
- [BLOCKING] HitConfirm / JoinState, HD-AC-29/31: undefined bucket capacity, fold, amount semantics and id width; unbounded "about 40 B"
- [BLOCKING] HD-AC-21 / HD-AC-32: undefined reference machine (p99 now advisory); undefined worst-case script; budget terms incomplete (now ≈ 4.0 kbps)
- [RECOMMENDED] HD1/HD2: Register's initial lastDamageTick; monotonic enqueue sequence; ApplyHot implies Consumable (applied)
- [RECOMMENDED] F2/F4/F5/Tuning: 120 000 maxHp maximum; ceil for ms→ticks; hard vs safe ranges explained (applied)
- [RECOMMENDED] Network/UI: HitConfirm 7 B; maxHp from Content; Dead actor still gets HitConfirm; construct HP % final message (applied)
- [RECOMMENDED] Player Fantasy: Giants are a team job; OQ-HD-1 kill criterion; PrepRegen integer cadence; M-1b gate on HB-HD-2/4 (applied)
- [RECOMMENDED] Deferred to OQ-HD-9: hotSlots 3 vs heal maths; Downed-before-allow-table masks illegal sources; extra AC cases; HitConfirm channel/turret confirms
Reviewed-Content-Hash: design/gdd/health-damage.md 0e0977840a4fa8a9d5708a73b051d5929049c1af
Reviewed-Content-Hash: design/registry/entities.yaml a446034fb9aa252cc57c6f51b330b2d8b2c85afd

Revision applied 2026-10-16: all 10 blockers and the 12 cheap recommended items. 452 → 454 lines, +5.4 % bytes. Status set to In Review (Provisional). Registry healOverTime and the spawnProtectMs reference were synced. Re-review decides Approved.
