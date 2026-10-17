# Review Log: grid-inventory.md

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, ux-designer, network-programmer, performance-analyst, godot-specialist, creative-director (senior)
Blocking items: 10 | Recommended: 14
Summary: The core model (W×H footprints, StackKey, oldest-first, one server-validated op FIFO, the conservation invariant) holds and no specialist asked for a redesign. Three things are missing or wrong: hand delivery from a sorted bag is never designed (deliver acts only on a hotbar slot, 7 slots shared with tools, no swap, quick_move routes mail away from the hotbar, "Deliver all" has no op); the Player Fantasy compaction beat is factually wrong (quick_move merges on pickup, so only the Intake compacts and it renders as a manifest); and there is no Network section while ~10 acceptance criteria carry wrong expected values. The Pillar 3 clarification written into game-pillars.md must be re-marked PROPOSED pending the user's ratification.
Prior verdict resolved: First review
Findings:
- [BLOCKING] Detailed Design R7/R9, UI Requirements: hand delivery from a sorted route is undesigned; add deliver_matching / deliver from any panel entry, swap on slots, defined "Deliver all" scope and partial failure, resolve OQ4 per-op cap now
- [BLOCKING] Player Fantasy: compaction beat unreachable on the hand path (quick_move merges); rewrite the paragraph and the Fantasy test; unify spawn-NoMerge vs return-merge in R14
- [BLOCKING] UI Requirements: gamepad/KBM context conflicts (A=pick+jump, X=quick-move+deliver, Menu/View stolen, RMB=cancel+throw with a surviving cursor); add an input-context table, a `return` op, Drop/Put-away binds
- [BLOCKING] Acceptance Criteria: wrong expected values in H-R4-1, H-D1-2, H-R16-1, H-R4-2, H-R6-1, H-R7-1, H-OPT-1, H-X-1/2, H-X-5, H-PERF-2; header count 59 not 48; `no_target` undefined; 300 s vs 270 s shift
- [BLOCKING] Player Fantasy / game-pillars.md: Pillar 3 "presence not speed" clarification is unratified; mark PROPOSED and get the user's decision before Belt Logistics builds on it
- [BLOCKING] Missing Network section: subscription model (conflicts with H-X-5), reply vs event ordering, one join budget on a defined encoding (25 KB vs 200 KB/2 MB), public vs owner-only PlayerState, reconnect/OQ3, own-sort-then-move stale_layout bug
- [BLOCKING] Detailed Design R7 / UI: contested-grab attribution needs actorId on events and stale_entry; 150 ms confirm window vs real RTT
- [BLOCKING] Formulas D3: step-4 retry ill-defined and address-mode FFD fails on valid packs; consolidation reuses entryIds so stale non-positional ops act on changed contents
- [BLOCKING] Detailed Design R14/R8 + Acceptance H-PERF-1: unbounded backlog drain, O(entries) PeekOutfeed/TakeUnit, uncoalesced replace events, "0 bytes/tick" undefined
- [BLOCKING] Detailed Design R12/R13: one wipe spills more than worldItemCap's floor; R12 lacks the tint/group fields R13 requires; water landing is a free undo (return at the FIFO tail after a delay)
- [RECOMMENDED] Formulas D5: rename per-period, justify 3 against a target time-to-Inspector, express the safe range relative to decay
- [RECOMMENDED] Tuning Knobs: manifestCellThreshold consistent range 160–239; externalLeash min ≥ 2.75; deathBag min as respawn+10; add ptsPermille/strongBackPermille
- [RECOMMENDED] Formulas D1/D2/D4/D6: P max 96 default; swim base 2000 mm/s into D1; classPts[bulk]; effMax up to 240 under the knob range; depot/PO spill page counts; clamp retrievalRadius
- [RECOMMENDED] Edge Cases: worldItemCap min-age exemption; same-tick pickup vs expiry ordering; pickup proximity; malformed-op rejection
- [RECOMMENDED] Acceptance Criteria: footprint fixture table; split bundled IDs; CI time budget and seed list for H-R19-1; hash-based pass for H-R19-2; add untested rules (Take all, fromGroup vs oldest-first, leash mid-drag, disconnect with split, Presort, ToDepot, Bike Courier)
- [RECOMMENDED] Visual/Audio / Game Feel: carry poses contradict art bible §5 (Large hugged vs Heavy overhead); cadence/waddle/stumble not in §5; squash on address text — to art-director
- [RECOMMENDED] UI Requirements: reason-code → message/sound/icon table; non-blocking animation; dashed outline carries four meanings; reduced motion in-world; height clause for text scale; screen reader tier; manifest paging on gamepad
- [RECOMMENDED] Player Fantasy / Acceptance: experiential criteria for the Fantasy test (route ≤ N inputs; 100 ms-RTT grab); solo Case-Your-Route gate at M1; D2 spawn scaling per player count
- [RECOMMENDED] Open Questions: OQ15 needs a concrete N and the "Sort while backlogCount > 0" instrumentation; OQ3 resolve before M0 (spill like death)
- [RECOMMENDED] Network: send deterministic Sorted{hash} instead of replace once cross-client determinism is shown; host loopback through the same adapter; WorldItem spawn/despawn events with pooled visuals
- [RECOMMENDED] Performance: [Conditional] R19 assert + O(1) release counter; itemId index for CountItem; manifest ≤ 1 rebuild/frame; memory budget
- [RECOMMENDED] Engine (godot-specialist, UNVERIFIED): adapter must require reliable ordered delivery; build spike for Sim + xUnit on 4.7.1 before M-1a code; tick thread; text scale as game-owned scalar; one _Draw grid; duplicate_deep for def cloning
- [RECOMMENDED] Formulas D3: probe counts overstated (naive ≈ 205 k, hinted ≈ 8 k); state the monotone-hint invariant
- [RECOMMENDED] Detailed Design R13: owner-priority window on spilled tools
Reviewed-Content-Hash: design/gdd/grid-inventory.md 9fb39ea26c4f16923217a2b267f041143533c6a7
Reviewed-Content-Hash: design/registry/entities.yaml 2aceaadb966c5e084d480a7680368d4801d66e10

### Revision applied — 2026-10-16 (same session, autonomous mode)
All 10 blocking items and 22 of 24 recommended items were applied to `design/gdd/grid-inventory.md`; `design/gdd/game-pillars.md`'s Pillar 3 clarification was re-marked PROPOSED pending user ratification (not reverted, not ratified). Not applied: the screen-reader tier (now OQ19, project-wide) and the art-director pass on carry poses (now OQ18). Status → In Review; a fresh `/design-review design/gdd/grid-inventory.md` decides Approved. The content hash above is the pre-revision receipt; the next review writes a new one.
User decisions 2026-10-16: OQ17 — Pillar 3 narrow wording ratified (game-pillars.md updated; binding on Belt Logistics). OQ4 — hand delivery misdelivers one unit per hold; a thrown stack landing in the wrong mailbox misdelivers the whole stack (owned by Throwing & Catching / Mail & Destinations).

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, ux-designer, network-programmer, performance-analyst, godot-specialist, creative-director (senior)
Blocking items: 9 | Recommended: 5 (bundled)
Summary: Re-review confirms the core model and that all 10 prior blockers were addressed. Nine remain: the Fantasy over-promises (Sort is cosmetic in a hand carrier's bag; hold vs "Deliver all [E again]"), three rules work against Fellowship (catch routing away from the hotbar, bag grace = respawn, silent partial/contested results), and the Network contract has internal ordering contradictions plus a bandwidth target ~24× spec 06 §4.2. CD directives: D-A re-anchor Sort on space/legibility (+H-FX-4); D-B one hold delivers every match; D-C air/ground mail → hotbar, full-panel catch = fumble; D-D whole-stack throw stays with ×N preview; D-E cut bagOwnerGraceSeconds. Engine/ADR/binding items moved to Technical Setup (OQ12/OQ20), ADRs and Input & Controls.
Prior verdict resolved: Yes (all 10 prior blockers addressed; new blockers are revision-introduced or newly visible)
Findings:
- [BLOCKING] Player Fantasy: Sort/casing cosmetic for a hand carrier; re-anchor on space + legibility + Intake compaction; add H-FX-4
- [BLOCKING] Player Fantasy / UI Requirements: hold E delivers first entry vs "every stack for that door"; one hold runs deliver_matching; H-FX-1 ≤ 11
- [BLOCKING] R9 / States: catch → inventory breaks catch-and-throw relay; mail from air/ground → hotbar first; full-panel catch = fumble WorldItem
- [BLOCKING] R13 / Tuning Knobs: bagOwnerGraceSeconds 10 = respawnSeconds 10; cut
- [BLOCKING] R7 / UI Requirements: partial and contested results silent; requested-vs-got + byActorId/cause; tombstones; H-FX-3
- [BLOCKING] R6 / R14: mailId vs arrivalSeq order after Intake consolidation; per-entry arrivalSeq = max of parts; returned entries enter whole (NoMerge = no merge into residents)
- [BLOCKING] UI Requirements: drag-out drop vs never-drops; mount auto-open → Panel context; dead prompt with panel open; Tab collision; Holding X/Shift-click; silent throw block; keyboard-only nav; reachable "unreachable" codes
- [BLOCKING] Network: bandwidth vs spec 06 §4.2 (group-count deltas to non-openers, valve); resync drop vs buffer + channel ordering; sort coalescing vs baseVersion (version_from..to, release on reply); WorldItem seq + tombstones; checksum semantics; per-container resultVersion
- [BLOCKING] Acceptance Criteria: H-R7-8, H-R13-2, H-R18-2a, H-FX-1 wrong; H-R18-2b phase-gate; PERF fixtures + gate level + probe gates; missing never-merge / no-op / self-merge / UI reason-code ACs; stale "18 k probes" and "ascending-mailId"
- [RECOMMENDED] Formulas / R2: uint32 widths; maxStack×stackMult ≤ 255 validator; D3 minting order; D5 fixed-point, 51 not 54, safe-range bound
- [RECOMMENDED] Edge Cases: take_all skip-and-continue; leash close puts away held entry; wallet_floor thrown remainder path; backlog append rule; cap with nothing evictable; R4 cursor onto full same-key slot
- [RECOMMENDED] Network / Performance: reply timeout; temp-id slot index; drain early exit; memoised sort; probe-count gates
- [RECOMMENDED] UI Requirements: per-cell numeral/glyph; toast cap; spec 09 binding correction note
- [RECOMMENDED] Open Questions: OQ19 partly answerable (accessibility_live VERIFIED in 4.7); OQ12/OQ20 absorb godot-specialist spike items
Reviewed-Content-Hash: design/gdd/grid-inventory.md 59a44df1d5ec1661425bd59124f60dacc5d2bf94
Reviewed-Content-Hash: design/registry/entities.yaml 6442f0f23999cf349370748b325ef4690d0d9f4f

### Revision applied — 2026-10-16 (re-review follow-up, autonomous mode)
All 9 blockers and CD directives D-A..D-E applied; most bundled recommended items applied (uint32 widths, stack ≤ 255 validator, D3 minting order, D5 integer/51 fix, take_all skip-and-continue, leash puts away, wallet_floor remainder, backlog append, cap fallback, reply timeout, drain early exit, memoised sort); UX polish deferred to OQ25; Godot spike items into OQ12/OQ20; new OQ21–OQ24. ACs 83 → 92 (2 MANUAL, 1 UI). Bandwidth PROPOSED: ≤ 40 B/tick/peer sustained (24 % of spec 06 §4.2 client budget), bursts ≤ 12 KB at ≤ 800 B/tick, 24 KB high-water → resync-pending (owner Networking & Session). Known residue: X/E is tap at a container and hold at a mailbox in World context (one verb per target type) — Input & Controls (OQ22) may rebind; gamepad column provisional. Registry: bagOwnerGraceSeconds retired; intakeOutfeedOrder updated; deliverHoldRule and airMailRouting added. Status → In Review; re-review scheduled after the M-1a GDD set is drafted (their contracts feed back into this one).

### Hand-back reconciliation — 2026-10-16
Sources: Open Questions of player-character-movement (1–6, 24), mail-destinations (1–4; OQ5/OQ13 answers in R17/R8), input-controls (OQ-IC-10, 14 items), manual-delivery (4), throwing-catching (2b), hud-notifications (HN-3), comms (none), and the planning-run ledger. 46 hand-backs: **43 adopted, 2 adapted, 1 rejected**. Status stays In Review; size 890 → 900 lines (+4.5 % bytes).
- **R7:** `drop {entryId}` whole entry, focused-cell atomic lift + drop; `deliver_matching` gains `holdStartTick` (u32, host domain); per-unit `MailConsumed` + one `DeliveryOutcome` per delivered entry; `throw {entryId, units, origin, dir}` with Throwing's reply codes; `position_stale` and the P13 precedence added to the reason list.
- **R9/R10/R11/R12/R13:** partial catch `{unitsIn, unitsFumbled}` (newest fumble); F5 claim-and-corroborate range, leash never fires when stale, leash floor 3.0 m (ADAPT), mount/Build while Holding auto-returns; Frozen body not openable (REJECT of "teammates may Open", new OQ26); Loot mail exempt from expiry and cap eviction until Payday; Payday start dead-letter sweep (returned to sender, CONSUMED) then Loot return; bag at sea → nearest dry point ≤ 6 m, floating piles keep their timers (ADAPT), Open / heap pickup allowed while swimming.
- **D2/D5/OQ15:** Intake pressure from Mail F3 (8 players ≈ 1,035 cells, full ≈ 79 s into shift 5; 4 players ≈ 124 s; solo never); `backlogCount` → `backlogUnits` (pieces) everywhere; H-PERF-3/5/6c backlog 1,200 → 2,800 entries.
- **Edge cases:** thrown 20-stack complaint per Mail R9 door/team buckets (money still per unit); belt landing per-unit insert with remainder WorldItem; partial catch.
- **UI Requirements:** binding rule 2 "for the same target"; Mounted and Dead contexts; Panel walk-only (no sprint/look); Holding: RMB Cancel only with the panel open, Tool/Throw show "Put it away first", Jump/Sprint live; Back ladder from Input R5; control table (action menu Y / F / Shift+F10 / RMB-on-cell, "Close panel, keep holding", Y no longer closes, Sort by address off View, pad Drop of the cursor entry, D-pad next-fit while Holding); "41 pieces · 3 stacks", "posts 1", lock without glyph; others' op sounds 1 per 250 ms per container + 6/s; tips ≥ 18 px; feedback rows for `position_stale`, `not_carryable`, `unsplittable`, split `no_target`, "Fumbled" without "!", `wallet_floor` → "Can't afford a wrong house", PC/Throwing codes marked not-ours; panel vs hotbar/held-chip ownership (HN-3e).
- **Game Feel:** weight pose per Player Character's art-director ruling (held-item class, stride ∝ speed, `min(P,40)/40` body load).
- **Network:** F5 validation wording; `DeliveryOutcome` in the per-peer batch; JoinState + ≤ 20 B value hint per own mail entry (≈ 71 KB worst case); temp `selectedEntryId` via `dependsOn`; rule 7 timed-out entries stay pending until the snapshot; rule 8 `reqId` u16 and duplicate `reqId` → original reply.
- **ACs (92 → 93):** H-R10-1 (F5 thresholds, stale, mount auto-return), H-R9-2e partial catch, H-X-1 events, new H-R12-4 (Loot, Payday sweep), H-UI-1 scope.
- **Open Questions:** closed OQ5, 6, 7, 13, 22, 23, 24; narrowed OQ18 and OQ25; OQ15 urgency; OQ20 notes PC's assumption; OQ12 absorbs PC/Comms spike items; new OQ26.
- **Dependencies / Cross-References:** six consumers marked In Review; provisional assumptions verified except Belt Logistics.
- **Knock-ons for other owners (not edited here):** HUD H-INT-2 still lists "Wallet is empty — can't afford another mistake"; Mail's prompt string still reads "drops 1".
- Consistency pass — 2026-10-16: catch-fumble feedback row now "Bag full — fumbled" (string and routing HUD H4), unowned catcher string "Caught ×15 — 5 fumbled" dropped; Dependencies row aligned. No rule changes.

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, network-programmer, creative-director (senior)
Blocking items: 21 | Recommended: 18
Summary: Convergence re-review. All nine second-review blockers are resolved in substance; D1–D7 and the worked AC values re-derive correctly and the 93 count holds. What remained was contract precision against the now-written Simulation Core and Content & Data GDDs, five network-contract holes in the optimistic client (own-sort hold, double render, resync queue clear, valve vs paced snapshots, byte arithmetic), and fixtures/ACs contradicting their own rules. CD: every fix is local; a targeted check of these items decides Approved, no fourth full review. CD rulings 1–4 (toast dry-run and target, own-sort hold, 10 s-mean budget reading, rule 8 over TC-E-5) are pending owner ratification.
Prior verdict resolved: Yes (9/9 in substance; residues listed below)
Findings:
- [BLOCKING] UI Requirements / Player Fantasy: "Make space — Sort?" gated on free area (unsound) with no target container → dry-run (Sort by size then AutoPlace) per R9 target, toast carries containerId, binding handed back
- [BLOCKING] Acceptance H-X-5d / Network Subscription: fromGroup "without an Intake open" contradicts R10 not_open
- [BLOCKING] Acceptance H-X-5c / Network JoinState: ≤ 70 KB gate below the ≈ 71 KB worst case; Bulk Mailer ≈ 98 KB
- [BLOCKING] Acceptance H-R9-2 d/e: preamble's mergeable inventory stack absorbs the catch
- [BLOCKING] Acceptance H-FX-1 / Fantasy test 2: Intake `open` uncounted, no single district, package sizes unstated (≤ 11 → ≤ 12)
- [BLOCKING] Acceptance H-R14-2 / Edge Cases: drain runs in the sort's own tick (Simulation Core phase 2), not "next tick"
- [BLOCKING] R14 / H-R14-3: "no unit leaves earlier than unsorted" false → "arrivalSeq never decreases"; cost into OQ15
- [BLOCKING] UI Requirements contexts/control table: D-pad L/R double meaning in Holding → Input & Controls' Holding-open / Holding-closed
- [BLOCKING] R7 / Interactions / Edge Cases / H-R7-10: automation is phase 5 synchronous (Simulation Core R9/HB1), player beats inserter on the same tick; `busy`; per-call coalescing
- [BLOCKING] R7 sort: memo keyed on version only ignores Bulk Mailer → (version, statEpoch)
- [BLOCKING] R1/R3/Interactions/Dependencies: Content & Data OQ-CDD-2/17 unanswered — InventoryRows / MailMaxStack / WeightPenaltyPm, inventory.maxRows 16, never-evict on shrink, acceptsInsert
- [BLOCKING] Network optimistic rule 5: held positional ops released into a layout the player never saw
- [BLOCKING] Network ordering: own op rendered twice between event and reply → events carry the causing reqId
- [BLOCKING] Network rule 7: snapshot clears the whole pending queue → `ackedReqId`
- [BLOCKING] Network valve: paced snapshots ≥ 24 KB loop the valve; replies discarded → excluded / never discarded
- [BLOCKING] Network bandwidth / H-NET-7: per-op bytes ignore mailIds; 800 B/tick peak vs 80 kbps; mean gated excluding bursts → formula, 10 s-mean reading, burst interval
- [BLOCKING] R7 reason codes / R12: unknown id bad_request vs stale_entry; WorldItem tombstone lacks byActorId → split by allocator high-water, one tombstone table
- [BLOCKING] R7 pickup: entryIndex on a mutating heap → entryId
- [BLOCKING] R7: range-gated ops must carry the claimed position (Player Character P13) → hand-back to Manual Delivery layout
- [BLOCKING] Network rule 8 vs Throwing TC-E-5 → cache hit returns the original reply; outside the cache stale_entry; TC-E-5 wording handed back
- [BLOCKING] Acceptance H-FX-2/3/4 tagging / H-UI-1 `out_of_bounds` text: UI event log defined as a plain-C# feedback presenter; "Doesn't fit there"
- [RECOMMENDED] Player Fantasy test 1 wording ("at most one non-full stack per key") — applied
- [RECOMMENDED] H-PERF-6c 8,000 → 8,500 — applied; OPEN guard F5 slack — applied; floating pickup → `unavailable` — applied
- [RECOMMENDED] States: WORLD/CONSUMED entries missing (disconnect, Grant SpillWorld, Loot, Payday sweep, mail cap eviction) — deferred
- [RECOMMENDED] Network: Closed{containerId} server event; Depot `replace` ≈ 29 KB paced; GroupCounts buffering during open; requested/got u16; backlogUnits saturation; temp-id map cap — deferred (ADR / Networking)
- [RECOMMENDED] Acceptance: split bundled ACs (H-R10-1, H-R12-3, H-R17-1, H-R7-4); H-R7-7 cursor + Reject entry; wallet-floor remainder, bag at sea, rule-8 dedupe ACs; H-PERF-5 gate level; H-R19-2 BLOCKED on OQ12; textScale permille — deferred
- [RECOMMENDED] Player Fantasy: Intake Sort's visible payoff is weak; hotbar-first catch can crowd the hotbar; partial quick_move/pickup lack requested/got — deferred to OQ15 / playtest
- [RECOMMENDED] OQ20 closed by Simulation Core; Simulation Core HB2 perf slices; HUD F7 constant tied to default knobs — deferred
Reviewed-Content-Hash: design/gdd/grid-inventory.md 7e2267df1f23fe2c8e6870f5cb40a092c5f38b4f
Reviewed-Content-Hash: design/registry/entities.yaml 19b344454406c6b39d75cd0d2027f6236bdcdb7b

### Revision applied — 2026-10-16 (third review follow-up, autonomous mode)
All 21 blockers applied as ruled by the creative-director (targeted edits; 187,311 → 193,285 bytes, +3.2 %; ACs stay 93 — fixes folded into existing IDs: H-R7-4, H-R7-10, H-R9-2, H-R14-2/3, H-R15-1, H-FX-1, H-FX-4 negative case, H-X-5c/d, H-OPT-1c, H-OPT-3, H-NET-1, H-NET-7). One correction to CD ruling 1 while applying it: the toast's dry-run is "Sort by size, then AutoPlace the incoming entry" (what the press and retried pickup actually do) — a dry-run that packs the incoming entry with the residents can succeed where the real sequence fails (verified on H-FX-4's negative fixture). Registry: `stackKey` (MailMaxStack), `sortPacking` (statEpoch memo) updated; `inventoryMaxRows` 16 added (PROPOSED). Hand-backs to Input & Controls / HUD, Manual Delivery, Throwing & Catching and Networking & Session are in new OQ27. Status stays In Review; a targeted check of the 21 items decides Approved (CD: no fourth full review).
