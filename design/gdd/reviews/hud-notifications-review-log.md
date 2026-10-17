# HUD & Notifications — Review Log

## Review — 2026-10-16 — Verdict: MAJOR REVISION NEEDED
Scope signal: L as written (HUD + a second comms system + netcode); M for HUD alone after the split
Specialists: ux-designer, ui-programmer, accessibility-specialist, qa-lead, network-programmer, game-designer; senior: creative-director
Blocking items: 30 | Recommended: 22
Summary: The M-1a core's maths is sound (F1, F3, F5, F6, F7 and the H-INT-2 lifetimes re-verified), but the document was the largest GDD in the project for a system sized S. It contained two systems: comms, which all six specialists flagged, and HUD. Its own fantasy example failed: a feed line had no street and the compass had no distance. Queue, protected-zone, contrast, live-region and flash rules contradicted each other or WCAG. The creative-director ruled a structural split. Comms moves verbatim to design/gdd/comms.md, frozen behind the M-1a GUARD. Layout, art, accessibility-policy, audio and ADR material is parked in design/ux/hud-notifications-seed.md. The target is ≤ 800 lines with net growth ≤ 0 %, and a 16–17-AC M-1a slice.
Prior verdict resolved: First review
Findings:
- [BLOCKING] Scope: comms (H13–H19, Network, comms ACs) is a second system; layout / art / a11y policy / impl detail belong to /ux-design, art bible, Accessibility GDD, ADR [all specialists, CD]
- [BLOCKING] Player Fantasy / H8: "Jules delivered 12 letters" cannot say where Jules is — no street in the feed line, no distance on the compass [game-designer]
- [BLOCKING] H8 / GF-2: 8-player feed evicts others' P3 lines before the 6 s floor [game-designer]
- [BLOCKING] H4 / H8: a 4th pill folds into the oldest; teammates' pills swallow own feedback [ux-designer, game-designer]
- [BLOCKING] H6: "nothing evictable" contradicts the overflow rule and H-QUEUE-1 [main, ui-programmer, qa-lead]
- [BLOCKING] H6 / F1: "Until dismissed" starves the toast queue; no dismiss binding exists [main, ui-programmer, qa-lead]
- [BLOCKING] H-QUEUE-2(b): own rejection replacing a P1 contradicts "P1 never evicted" [qa-lead]
- [BLOCKING] H2 / H-WIDGET-2 / H-MERGE-3: wallet "−5.00" vs pill "−N¢" contradicts Mail & Destinations' one-formatter rule [main, qa-lead]
- [BLOCKING] H11 / UI Requirements: prompt zone runs into the toast lane; "gutter" claim false; prompt inside the panel band [ux-designer]
- [BLOCKING] H11: own-verdict toasts fade to 25 % over the landing marker, defeating H3; fade without duration is flash-class [ux-designer]
- [BLOCKING] H3 / H20: Hidden / Essentials hide own verdicts [ux-designer]
- [BLOCKING] H20: context modes and preference tiers conflated; combinations undefined [ux-designer]
- [BLOCKING] F3: HUD-only slew integrator duplicates ClientWorld's render clock [ui-programmer]
- [BLOCKING] IHudState contract and prediction reconciliation (opId dedupe, `Revert`) undefined [ui-programmer]
- [BLOCKING] H12: behind-camera behaviour and marker z-order ownership unspecified [ui-programmer]
- [BLOCKING] ACs: screenshot / draw-counter / 540-case sweep ACs cannot gate; H-FT-3b is geometry [ui-programmer, qa-lead]
- [BLOCKING] Visual/Audio: Chalk on Go Green ≈ 2.1:1, Chalk on Stamp Red ≈ 4.4:1 (SC 1.4.3); #5A5E6B outline ≈ 2.4:1 (SC 1.4.11); chat 50 % floor [accessibility]
- [BLOCKING] H26: one Control cannot be assertive and polite; rendered text breaks TTS on symbols; "nothing dropped" lags minutes [accessibility]
- [BLOCKING] H28 / F2: any opacity step counted as a flash (Reduced Flash denies every cut-in); no area test [accessibility, qa-lead]
- [BLOCKING] H23: physical-px floors without a stretch mode shrink at 1440p / 4K [accessibility]
- [BLOCKING] H11: notifications may cover the focus ring in overlays (SC 2.4.11) [accessibility]
- [BLOCKING] H-QUEUE-1 / H-ROUTE-3 fixtures ambiguous; H-F2-1 example double-denied; H-PERF-3 contradicts the 1/s timer; `draw_line` is GDScript; H-MARKER-1 needs 8 teammates at roster 8 [qa-lead]
- [BLOCKING] H7: no rule behind same-tick "phase never shown"; pending banner can pin forever [qa-lead]
- [BLOCKING] H-INT-2: not one row per Grid outcome; "Wallet is empty" is a prompt; Grid still says "Fumbled!" [qa-lead]
- [BLOCKING] Network: commsSeq global vs per-peer; typing bit cannot fit PC's transform; ping refresh / ack not deterministic across peers; JoinState 4.4 KB false (~16 KB); Say size / no type byte; host-stamped ordering claim false; channel count; joiner gating [network-programmer] — recorded in comms.md CF-1…8, not fixed (frozen)
- [BLOCKING] H9 / anti-goal 2: history category filter = on-demand tally; feed ignores `rollCallNames`; misdelivery line undefined [game-designer]
- [BLOCKING] H13 / H15: quick-ping failures and ack override [game-designer, ux-designer] — comms.md CF-9 / CF-10
- [BLOCKING] H18: chat freezes the carrier up to 20 s [game-designer] — comms.md CF-11
- [BLOCKING] Fantasy tests: no baseline; distance-less compass; test 3 duplicates Mail's M-FT-3 [game-designer]
- [BLOCKING] M-1a slice not minimal (~20 ACs vs spec 12 "timer, wallet, prompt"); spec-12 timer had no M-1a AC [all specialists, qa-lead]
- [RECOMMENDED] Say page Yes / No; fixed default wheel page; list alternative own action; pad paths for history / +N chip [ux-designer, accessibility] — comms.md / OQ
- [RECOMMENDED] "Make space — Sort?" 2.7 s actionable window (SC 2.2.1) [accessibility] — HN-22
- [RECOMMENDED] Numeric: EC "+6" vs H-MERGE-2 "+7"; "bartholomew… 2"; int32-fitting overflow vector [qa-lead]
- [RECOMMENDED] Compass ±120° vs spec 09 360° undeclared [game-designer]
- [RECOMMENDED] Banned-word lint on `en` source only; virtualised chat list; F8 numbers PROPOSED; one animation driver [ui-programmer]
Reviewed-Content-Hash: design/gdd/hud-notifications.md af6010057863dfcc0d1b5a46778abd8cd57d4012
Reviewed-Content-Hash: design/registry/entities.yaml 28e04a7e25136d840795880509d04e7591ffd98b

### Revision applied — 2026-10-16 (same session, autonomous [A] Revise now)
- **Scope (CD S3 + P1):** comms (H13–H19, H24 sanitiser, F5, ping replacement, comms states / edge cases / knobs / ping-type table / Network / comms ACs) moved close to verbatim to `design/gdd/comms.md` (510 lines, Draft, FROZEN behind the M-1a GUARD, inherited findings CF-1…16). Layout, widget styling, Compact, settings, a11y policy, H27 audio limits, F8 frame budget, H25 ring detail, H29 and engine-verify list parked in `design/ux/hud-notifications-seed.md` (302 lines, "UNREVIEWED SEED — not authoritative"). HUD 1,581 → **670 lines**. Total across the three files 1,482 (−6.3 %; ≤ 0 % net growth met).
- **Fixed in HUD:**
  - H6 one total-order overflow rule (P1 never evicted; the own rejection never replaces a P1); M-1a single slot.
  - "Until dismissed" cut (1× / 1.5× / 2× / 3×; history is the untimed equivalent).
  - Own pill slot plus one team key.
  - Feed line carries the latest street; crew-line collapse at 3 actors.
  - Compass teammate distance labels; ±120° stated as an interpretation of spec 09.
  - Clerk-voice misdelivery line honouring `rollCallNames`; history has no per-actor filter.
  - Single `IMoneyFormatter` (format → Economy HN-6).
  - H11 toast lane anchored under the prompt with an alternate anchor. Toasts never fade; markers fade ≥ 250 ms. Panel-footer prompt; SC 2.4.11 rule.
  - H20 split into context modes × the Essentials preference (Hidden cut); Essentials never hides own verdicts.
  - H23 1080-reference units scaled by height, plus a normative contrast rule.
  - H26 two live Controls, `spokenKey`, 10 s age collapse; a11y schema fields at M-1a, announcer at M4.
  - H28 / F2 flash = opposing luminance pair over 10 % area; one animator; world effects request tokens from M1.
  - F3 slew removed (reads `renderSimTimeMs`).
  - H30 `IHudState` + opId prediction reconciliation (`Revert` defined).
  - H12 behind-camera and z-order.
  - H7 P0-only pending, pending TTL 5000 ms.
  - Per-category mutes and the 1.25 × quota track cut.
  - Speaking-bit design removed (Proximity Voice).
  - ACs 87 → 47, rewritten per qa-lead; M-1a set of 17 including a timer AC; screenshot ACs ADVISORY; H-FT-3b → H-ZONE-1 geometry.
- **Deferred (Open Questions):** HN-1…HN-24 incl. new HN-22 (Sort? window), HN-23 (off-screen teammates), HN-24 (crew threshold); all network blockers as comms.md CF-1…8.
- **Registry:** 19 targeted updates — `hudToastLifetime`, `hudFlashGovernor`, `hudTimerDisplay`, `hudNotificationCaps`, `hudBannerPriority`, `hudTextFloors`, `hudScreenProjectedMarkers` revised; `hudCommsCourtesy`, `hudPingBookkeeping`, `pingLifetimeMs`, `chatMaxChars`, `commsTrustModel`, `commsChannelBudget` re-sourced to comms.md; 5 referenced_by lines updated. YAML valid.
- **Status:** systems-index row 24 → In Review; new row 42 Comms (frozen). A fresh-session re-review decides Approved.

### Hand-back reconciliation — 2026-10-16
Sources: Input & Controls (control table, OQ-IC-12/19, UI Requirements), Player Character OQ28 / OQ31, Mail & Destinations R16 and Visual/Audio, Manual Delivery OQ5 and UI Requirements, Throwing & Catching UI Requirements, Simulation Core HB9, Content & Data CV-HUD-01…05, Grid Inventory feedback table and review-log knock-ons, Comms (none new), and the planning-run ledger. 35 hand-backs: **22 adopted, 10 adapted, 3 rejected**. Status stays In Review; 670 → 669 lines.
- **Adopted:** History KBM L / Pause › History (no pad default, View = Map); Status read KBM I (+ the focused prompt's live sentence); live-region digest confirmed; 200 % reading text / 150 % panels; `PromptData` with one reason key; caret contrast; pile beacon "· 80 m"; pile-marker declutter confirmed; timer from `IHostClock` (HB9; Sim Core and Content & Data added to Dependencies); "posts 1"; clerk line and `rollCallNames` confirmed; one "+N¢ · k stacks" pill per hand hold; backlog tiers in pieces; Manual Delivery branch table (✓ / ✗ leads line 1, ≤ 4 rows, skull chip); ✗ prompt assertive; landing marker + arc label as one protected rect; CV-HUD validation; Grid rows ("Nothing to take", "Stack of 1 can't split", "Too bulky to carry", "Lost track of you — try again") and "Can't afford a wrong house" — H-INT-2 unblocked.
- **Adapted:** tip slot (first client Input's first-pad line; before M4 the glyph swap carries it); reason precedence stays with the owner; prompt screen-fixed, no leader line; "Inventory safe" as a name-tag suffix; H-ROUTE-6 lint uses Player Character's vocabulary stems (with the "dead letter" allowlist); reason strip lifetime = F1 (supersedes 0.8 s / 2 s); catch routing; one fumble cue "Bag full — fumbled"; state-line supersession in the live region; prompt growth direction (UX seed).
- **Rejected (Open Questions):** UI scale 75 %; in-world "+N" sweep pill; Mail's feed ≤ 4 / per-second team pills.
- **Closed:** HN-1, HN-2, HN-3, HN-4. Still open: HN-5…HN-11, HN-13…HN-15, HN-17…HN-19, HN-22…HN-24 (HN-12, HN-16, HN-20, HN-21 live in comms.md).
- **Registry:** `hudToastLifetime` (reason strips), `hudTimerDisplay` (variables fixed, `IHostClock`, referenced_by), `hudNotificationCaps` (per-hold pill, catch routing, referenced_by), `hudTextFloors` (100 % floor), `hudScreenProjectedMarkers` (arc label, screen-fixed prompt). YAML valid.
- **Knock-ons for other owners (not edited):** Manual Delivery `reasonStripTicks` and Throwing's 2 s dimming are superseded; Grid's "Fumbled" row → "Bag full — fumbled"; Player Character UI Requirements "world-space, clamped" prompt and Manual Delivery's leader line are stale; Mail Visual/Audio "feed ≤ 4 lines" is stale.
