# Manual Delivery — Review Log

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: S
Specialists: game-designer, ux-designer, qa-lead, gameplay-programmer, network-programmer, systems-designer; senior: creative-director
Blocking items: 16 | Recommended: 9
Summary: The verb, the four branches and the certified-✓ guarantee (MATCH omits `selectedEntryId` → host `no_target`) hold up and serve Pillars 2 and 5; the failures are precision. The time-domain contract (hold-start plausibility, client vs host tick, in-hold recompute) contradicts Mail & Destinations' R4 closing grace, and the prompt — the "readable promise" — buries or hides its verdict. Creative-director reversed the Quick Hands MATCH-only narrowing (spec 10's −50 % on all holds, one shared `holdEff`) and kept MISDELIVER_ONE (CD D-B, user OQ4).
Prior verdict resolved: First review
Findings:
- [BLOCKING] MD6: plausibility window applyTick − (60 + 30) shrinks the 90-tick closing grace; M-MD6-2 (T−12 → T+90 Accept) fails; maxHoldTicks hard-coded 60
- [BLOCKING] MD5 / MD6: `holdStartTick` is a client tick compared with the host apply tick; no clock domain defined
- [BLOCKING] MD1: Deliver priority 1 assumes Pick-up 0; Player Character sets Deliver 2 / Pick-up 1 → zero bias; knob range 0–1 cannot reach 2
- [BLOCKING] MD4 / F5: `displayedTicks` (settle) has no owner, reset identity or tick order; 133 ms settle shorter than the crossfade
- [BLOCKING] MD3 / MD4: in-hold recompute returns REJECT after a phase flip, cancelling holds the closing grace protects; branch diff not limited to kind
- [BLOCKING] MD3: skip-if-unchanged stamp conflicts with the press-tick recompute and omits Panel / swim / debounce inputs
- [BLOCKING] MD1 / MD4: no `IInteractable` interface for per-press hold ticks and extra cancel guards (Player Character hand-back)
- [BLOCKING] MD2 row 3 / MD7: the 5 s resync clears the pending lock before the snapshot lands → duplicate request on a stale mirror
- [BLOCKING] Network: "≤ 16 B" unverified, no field widths; `DeliveryOutcome` ≈ 24 B vs Mail & Destinations ≤ 19 B
- [BLOCKING] UI Requirements: line-1 layout differs from Input & Controls / HUD grammar; verdict not on line 1
- [BLOCKING] MD7 / F5: reason line replaces lines 2–3 for 2 s and hides the new ✓ / ✗ after a cancel
- [BLOCKING] UI Requirements: character budgets fail on the doc's own examples; worst case 6–7 lines at 150 %
- [BLOCKING] UI Requirements: screen-reader user can complete a ✗ hold before the 0.5 s announcement; "Hold E" hard-coded
- [BLOCKING] MD7: no reply row for the host's swim check (`dry_land`)
- [BLOCKING] Acceptance Criteria: M-FX-1 undefined (variation, oracle, tag, resolution list); M-FX-2 underpowered; fantasy test 3 has no AC
- [BLOCKING] Player Fantasy: fantasy test 3 (≥ 20 % of all pieces by hand) fails by construction against F3's 4 : 1 feeders
- [RECOMMENDED] MD9 / F2: Quick Hands MATCH-only buys ~3 % of a door cycle for a specialised formula and a spec amendment (CD reversed)
- [RECOMMENDED] F2: intermediate truncation, dead `floorMs`, `insertPm` ≥ 1000 degenerate, knob threshold "≥ 800" wrong
- [RECOMMENDED] UI Requirements: plain ✗ hazard ring is noise; NOTHING with lock reads as an error; line 4 on most ✓ prompts
- [RECOMMENDED] States: table incomplete; Toggle ARMED shares the SENT string; Esc vs Pause; reach echo should read a Sim-published flag
- [RECOMMENDED] Network: stale FIFO requests on rejoin; remote beat needs all k outcomes in one tick; misdeliver spam by a friend
- [RECOMMENDED] Acceptance Criteria: compound ACs, MD2 adjacent-pair ordering, BLOCKED-ON tags, scan / beat seams
- [RECOMMENDED] MD4: state that it overrides Input & Controls G1's presented-snapshot rule for Deliver
- [RECOMMENDED] F1: pay range cites Cannon Arm; `MisdeliveryPenaltyMult` missing from the variable table
- [RECOMMENDED] MD3 / MISDELIVER_ONE: could be cut (game-designer) — CD kept it (D-B, OQ4)
Reviewed-Content-Hash: design/gdd/manual-delivery.md 91e513324bb466f9a4d7f485b9ee0631564d250c
Reviewed-Content-Hash: design/registry/entities.yaml ca4269eb8e970c021066c46f93739caf58de72e2

### Revision applied — 2026-10-16 (same session, autonomous mode)
All 16 blocking items applied to `design/gdd/manual-delivery.md` under creative-director rulings (i, amended) host-domain `holdStartTick`, (ii) Quick Hands = spec 10 literal, (iii) 6-tick settle with visible refusal, (iv) MISDELIVER_ONE kept, (v) Input & Controls line-1 grammar with the mark leading. Key changes: MD1 priority 2 (Player Character default, one level over Pick-up) plus `GetHoldTicks` / `ShouldCancel` hooks (hand-back); MD2 row 3 pending until the resync snapshot; NOTHING is one dim line; MD3 evaluates every tick (no cache), `destState` at hold start during a hold, `DeliverPreviewTracker` (`branchKey`, `sinceTick`, `canStartHoldNext`) and per-tick order, press-tick branch overrides G1; MD4 kind-only branch diff, settle 6 ticks (screen-reader waits for the ✗ announcement), Toggle cancel ordering and Back vs Pause; MD5 `holdStartTick` in the host tick domain, duplicate `reqId` idempotent; MD6 clamp to [applyTick − (maxHoldTicks + closeGraceTicks), applyTick] with content-derived `maxHoldTicks`; MD7 `dry_land` row, transient reason strip (`reasonStripTicks` 0.8 s × text scale); MD9 / F2 collapsed onto the shared `holdEff` (absMinTicks retired, `insertPm` clamp ≤ 750); States table completed; F1 Cannon-Arm range and `MisdeliveryPenaltyMult` row; UI branch table rewritten (mark-led line 1, ≤ 4 lines, skull chip, hazard ring only for Cursed / floor, `{binding}` in a11y keys, immediate assertive ✗ announcement, address-based budgets and overflow order); Network byte layout 10 B / 14 B and `DeliveryOutcome` ≤ 19 B; fantasy test 3 redefined as hand ÷ (hand + throw); ACs: case-splitting and BLOCKED-ON rules, M-FX-1 defined (Integration, scenarios × offsets × RTT, oracle), M-FX-2 ≥ 8 players with clustered bound, new M-FX-4, M-MD6-3 (`dry_land`), M-MD6-4 (bell fuzz), MD2 adjacent pairs, settle flicker / Toggle / screen-reader rows. Deferred to Open Questions 15–16 (rejoin FIFO purge, remote k-outcome tick guarantee, misdeliver spam cap, channel-ordering ADR checkpoint, line-4 progressive disclosure); hand-backs 1–6 rewritten. Registry: deliverHoldTicks, quickHandsInsertPm, certifiedDeliverRequest, deliverAvailabilityOrder, branchSettleTicks (6), deliverAnchorPriority (2) updated; absMinTicks removed; holdEff and closeGraceTicks referenced_by updated (YAML validated). Net growth ≈ 9.2 % (103.4 → 112.9 KB). Status: In Review — a fresh-session re-review decides Approved.

### Hand-back reconciliation — 2026-10-16 (autonomous planning run, Phase 2)
Sources: Open Questions / hand-back sections of grid-inventory, player-character-movement, input-controls, mail-destinations, throwing-catching, hud-notifications, simulation-core, content-data-definitions, and `production/session-state/planning-run-2026-10-16.md`. Result: **6 adopted, 3 adapted, 1 rejected**; Status stays In Review.
- Adopted: Simulation Core HB8 (Dependencies / Interactions / Cross-Ref rows; `IHostClock.EstimatedHostTick`; ClientLayer `Previews`); Sim Core R19 u32 destination id (request 12 B MATCH / 16 B MISDELIVER_ONE; 48 B/s); HUD HN-4a "posts 1" (no change); HUD H11 panel-footer placement (MD2); Content & Data `DeliverInsertPm` 0…750 as the clamp owner (MD9, F2); Throwing OQ1 non-random levers and shared hand-share trigger (fantasy test 3).
- Adapted: Mail R16 per-hold "+N¢ · k stacks" pill → in-world "+N" pill cut (CD-GDD-ALIGN condition met; F4, Visual/Audio, M-F4-1, M-EC-1); HUD H11 prompt placement → deferred to HUD / Player Character, bracket outline kept; Player Character spare flag **bit** (not byte) for the posting flag (OQ11).
- Rejected: HUD H11 (and Throwing UI) `reasonShowTicks` (60) replacing body lines 2–3 — retired at design-review; transient strip with `reasonStripTicks` kept (OQ5 note).
- OQs closed: OQ1 (Player Character), OQ2 (Input & Controls), OQ3 (Mail & Destinations), OQ4 (Grid Inventory) — all adopted by their owners; OQ15(b) answered by Mail R6 / R16. OQ5 narrowed; OQ11 reworded. BLOCKED-ON `holdStartTick` removed from the ACs.
- Conflicts for `/consistency-check` (other GDDs, not edited): Sim Core "no `reqId` dedupe" vs Grid Inventory rule 8; Player Character OQ12 still quotes `insertPm = 300`; Mail's prompt matrix "N stacks · M pieces" order; HUD H11 example line and screen-centred vs Player Character world-space placement.
- Registry: `deliverHoldTicks` (stale variables removed, Content & Data ref), `quickHandsInsertPm` (stat id, refs), `certifiedDeliverRequest` (Sim Core ref, wire sizes, dedupe conflict), new `reasonStripTicks`. YAML validated.
- Size: 565 → 569 lines (+0.7 %; bytes +0.8 %).
- Consistency pass — 2026-10-16: reasonStripTicks (0.8 s) retired — reason-strip lifetime is HUD F1 (H11, ≥ 2.5 s × duration setting) in MD7, F5, UI table, M-MD7-1; Tuning Knobs row removed; prompt placement deferred to HUD H11 (screen-fixed, no leader line); manualInsertHold safe range corrected to Mail's 0.2–0.8 s; OQ5 closed; stale OQ1 / OQ3 notes trimmed. 569 → 568 lines.
