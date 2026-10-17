# Review Log — Mail & Destinations (`design/gdd/mail-destinations.md`)

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: XL
Specialists: game-designer, systems-designer, economy-designer, qa-lead, network-programmer, level-designer, ux-designer, performance-analyst, audio-director, creative-director (senior)
Blocking items: 18 | Recommended: 14 (applied) + 12 deferred to Open Questions
Summary: The core holds — pure `Evaluate`, one consume routine for hands/throws/belts/trucks, literal per-unit money, deterministic manifest. Problems were arithmetic/fixture errors plus design gaps: no source of accidental misdelivery, complaint not scaling 1→8 (per-door only, late complaint above decay), no dead-letter sink, Cursed stamp reshuffling addresses, unbounded zero-unit broadcasts, Lost Parcels capped by count not value, missing World Generation spatial contract. CD rulings: per-door cap stands (Pillar 2 binds rule and money) pending owner ratification; team ceiling RH(40 × n^0.65)/30 s; closing grace judged by action start (90 ticks); never hide a mismatch — execution variance belongs to Throwing; no feeder auto-jam; roll-call 1.5 s/line ≤ 8 s with Names/"A carrier"/Off.
Prior verdict resolved: First review
Findings:
- [BLOCKING] R9/F9: late +2 per 15 s exceeds the 1.5/15 s decay → 30 s team-wide window
- [BLOCKING] R9/F9: complaint scales with doors, not n^0.65 → team bucket RH(40 × playerScale) per 30 s (closes OQ12)
- [BLOCKING] R11: no dead-letter sink → returned to sender at Payday start (Grid Inventory hand-back)
- [BLOCKING] R13: arc colour at release, outcome = volume entered; Throwing owns execution variance (OQ8, by M-1a)
- [BLOCKING] R10: "no mail spawns outside Delivery/Raid" vs SpawnSpecial in Prep → manifest-only wording
- [BLOCKING] F3/Determinism: cursedMeanPm in E_k changes N and reshuffles addresses → removed
- [BLOCKING] Determinism: K wrong at N = 0 → 2·nb + 2N + max(0, N−1)
- [BLOCKING] F4: a pair at m = 1 → pairs ≤ floor(m/2)
- [BLOCKING] F3/Edge Cases/ACs: N = 57 at V 672 (not 59); exact shift-5 counts; ranges incl. Zoning Board
- [BLOCKING] F10/M-F10-1: unreachable fixture values → deadline 1/2 at shift 2
- [BLOCKING] F11 + ACs: off-by-one insert ticks; M-F7-1d 9 → 2; M-F9-2 59,666; M-R14-2 tick 885
- [BLOCKING] R15: Lost Parcels count-capped (5 Found Mediums = 105 % solo s2 quota) → cap 6 + foundValueBudget 15 % of quota; single foundValueMultPm
- [BLOCKING] R3: no spatial contract → mailboxTile/faceNormal/anchor/approachTile/truckStopPoint, 6 m / 3 m spacing
- [BLOCKING] Network: zero-unit floor-stop flood, missing intendedAddress/outcomeSeq/refused state, 24 B overrun, phase on 5 Hz snapshot, FA-3 infeasible
- [BLOCKING] UI Requirements: prompt never says which verb is live → prompt matrix, dashed ✗ ring
- [BLOCKING] Performance: no per-tick mail CPU share; batch Spawn gated by call count only; 255-unit fixtures impossible
- [BLOCKING] Visual/Audio: rate-limit precedence, per-retry reject cues, Cursed sting per op
- [BLOCKING] Acceptance Criteria: M-R6-3 dependent fixtures and impossible two-feeder case; H-X-1 events; M-FT-1/2/3 lack protocol/owner/evidence
- [RECOMMENDED] R4: flag for tick T set in T−1 (spec 06 order); "Already filed" stamp at an exhausted bucket; typical-stack and forfeited-bonus rows in F7; "Tangerine side"; doorComplaintCap ≥ 16; Grace covers the whole late band; F9 lastTick = now; validator jitter/tail-guard; cue re-timbres; reduced-motion list and ≤ 3 flashes/s; landing marker "×N −N¢"; feeder credit stagger; global resident/prop caps
- [DEFERRED] Open Questions 6, 8, 13–15, 17, 23–25: hot-street weighting, final-batch deadlines, Payday bonus/quota ordering, Cursed failable trigger, XP weights, Cannon Arm maxStacks, feeder-jam signal, D5 vs D1 ¢/s, swept catch test, Lost Parcel placement, ICU localisation, sink capacity, audio/UX ratification
Receipt: not stamped. The revision was applied in the same session before the pre-revision bytes were hashed; stamping the revised bytes under this NEEDS REVISION verdict would make the next /design-review wrongly read the document as "unchanged since it failed review". The next run reads the document as NEW and does a full re-review (the safe direction).

### Revision applied — 2026-10-16 (same session, autonomous mode)
All 18 blocking items and the 14 cheap recommended items were applied to `design/gdd/mail-destinations.md` as targeted edits marked **[DR]** (≈ +8 % size); 12 deferred items were added to Open Questions (rows 6, 8, 13–15, 17, 19, 21 rewritten; 12 resolved; 23–25 new). ACs 62 → 64 top-level IDs (new M-R11-2, M-F9-4). The team cap at 8 players is 155, not the 154 the CD quoted (RH(40 × 3.864)). Registry: mailKindCounts, mailBatchSchedule, mailUnitPay, doorComplaint, destination_house_v1, mailboxPhaseRule, lateComplaintWindowSeconds (15 → 30), lostParcelsPerShiftCap (12 → 6) updated; teamComplaintCap, closeGraceTicks, deadLetterReturnToSender, mailValueKnobs added. Hand-backs: Grid Inventory (OQ1–4: complaint per door, Loot exemption, dead-letter removal, D2, names/events/JoinState budget); Run Structure (OQ6); Throwing (OQ8); World Generation (OQ15); Stamps (OQ14, OQ24). Status → In Review; a fresh `/design-review design/gdd/mail-destinations.md` decides Approved.

### Hand-back reconciliation — 2026-10-16
Autonomous planning run (Phase 2). Hand-backs addressed to this GDD were collected from the Open Questions of grid-inventory, player-character-movement, manual-delivery, throwing-catching, hud-notifications, input-controls and simulation-core, and from `production/session-state/planning-run-2026-10-16.md`. Edits marked **[HB]**; Status stays **In Review**. Lines 847 → 852. Score: **11 adopted, 2 adapted, 0 rejected**.
- **Adopted:**
  - Manual Delivery OQ3a: Quick Hands `insertPm` 500 on every deliver hold, through Input & Controls' shared `holdEff`. OQ5 is closed, R3 and R18 are reworded, and the stale references to Player Character's F4 are removed.
  - Manual Delivery OQ3b and HUD HN-4a: "Wrong house — posts 1".
  - Manual Delivery OQ3c (BLOCKING for the remote hand beat): a hold's k outcomes apply in one tick with contiguous `outcomeSeq` (R6). R16, UI Requirements, M-R6-1b and M-NET-1b now require one door beat and one "+N¢ · k stacks" pill per `(actor, destination, tick)` for hand outcomes.
  - Manual Delivery OQ3d: `holdStartTick` is in the host tick domain, clamped by MD6 (R4).
  - Throwing D1 "no dice, ever" (pending owner ratification): R13 and OQ8 are rewritten, and a wrong door requires a red preview on the release frame.
  - Throwing OQ2a: `ResolveThrownLanding(…, releaseTick)`, with timeliness read at the release shift (the same as the apply shift).
  - Throwing T6: swept landing test, descending entry only, first qualifying volume wins (OQ8 part answered).
  - HUD HN-4b: the clerk-voice feed line, with `rollCallNames` governing feed and history (R16, Visual/Audio, Feel table).
  - HUD HN-4c: "DEAD LETTER" is allowed.
  - HUD HN-4d: the backlog tiers count units and are unrelated to the 160-cell threshold.
- **Adapted:**
  - Player Character OQ30 (hand-only share) → OQ26. The share cannot be derived without BalanceSim and v1 adds no hand-only kind, so approval is not gated on it and it is tracked through Manual Delivery OQ12's `handShare` instrument.
  - Simulation Core HB4 → OQ27. The stream names, `opSeq` and phase attribution are adopted. The M-PERF-2 split and the `IEconomyApi` wallet port are held until Simulation Core (In Design) is reviewed, so that Mail never takes a reference to Economy.
- **Already satisfied:** Grid Inventory OQ5 (R17) and OQ13 (R8). Input & Controls addresses no hand-back here.
- **No conflicts needing a creative-director ruling.** The per-door complaint cap (OQ13) and Throwing D1 remain current defaults, pending owner ratification.
- **Registry:**
  - updated: `holdEff`, `mailKindCounts`, `deliverHoldRule`, `mailboxPhaseRule`, `backlogUnits`, `throwNoDice`;
  - removed from `mailKindCounts`: the stale `cursedMeanPm` variable;
  - added: `rollCallNames`.
- A fresh `/design-review design/gdd/mail-destinations.md` still decides Approved.
- Consistency pass — 2026-10-16: prompt matrix now leads with pieces ("M pieces · N stacks") and uses Manual Delivery's misdeliver lines ("Wrong house — piece is for P" / "Posts 1 · −N¢"); pill / feed caps and merge windows deferred to HUD H4 / H8 ("feed ≤ 4 lines", per-second others' pills removed); manualInsertHold knob wording names the StatSheet insertPm via holdEff, not Player Character.
