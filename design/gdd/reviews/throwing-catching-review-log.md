# Throwing & Catching — Review Log

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: game-designer, systems-designer, gameplay-programmer, network-programmer, qa-lead, ux-designer, creative-director (senior)
Blocking items: 13 | Recommended: 17 (applied) + 9 deferred to Open Questions
Summary: The fantasy, the host-authoritative flight model and the core verbs hold; F4/F5 maths, rate and bandwidth figures, and several honesty claims were wrong. The creative-director reversed two of its own CD-GDD-ALIGN rulings: the spread knob is retired (≤ 15 mrad can never reach a wrong door; no dice, non-random levers only) and the preview's colour, pattern and glyph are exact every frame. The doc is ruled oversized (1,449 lines): net growth ≤ 0 %, cuts to pointers now.
Prior verdict resolved: First review
Findings:
- [BLOCKING] F4: cylinder/sphere behind the flight gives negative f that wins; no exit root or y-slab intersection; integer rounding and heightfield sampling undefined
- [BLOCKING] F5: prompt lead overstated (≈ −400 ms, not −660); example sign-inverted; registry catchWindow repeats it
- [BLOCKING] T5: false execution-variance sources; spread knob cannot reach a wrong door (D1: knob retired)
- [BLOCKING] T3: 100 ms colour debounce shows a stale verdict on a moved marker (D2)
- [BLOCKING] T7 / Input R4.3: catch-first Interact steals Deliver/Pick up near any inbound arc (D3)
- [BLOCKING] T1/Network: host slack allows 10/s/player (80/s team ≈ 40 kbps); stale bandwidth; origin 10 B undefined; clientTick unused (D5 leaky bucket)
- [BLOCKING] Game Feel/Network: ghost lead wrong; "never a path offset" false; ghost hovers at static contact
- [BLOCKING] T3/TC-P-1: preview invalidation key incomplete; "0 Sim queries when still" unreachable
- [BLOCKING] T6/F8/Edge Cases: three WorldItem fallbacks; F8 Euclidean push-out lands inside the box; T7 catcher tie contradicts T6
- [BLOCKING] Network: catch trust "bounded by catchHistoryMaxOffsetMm" is false
- [BLOCKING] Player Fantasy test 3 vs Manual Delivery test 3: different metric and denominator
- [BLOCKING] Acceptance Criteria: no milestone tags, no evidence ACs, ~30 boundary/value rewrites, ~20 uncovered rules
- [BLOCKING] Dependencies: Simulation Core marked "not yet written" (now designed; phase 4 Throwing.Step, phase 10 Combat.Resolve)
- [RECOMMENDED] T10 Cannon Arm ×1.24–1.47 (×1.30 flat); validators (reach cap, speed ranges, broad-phase int64, 100 ms multiples); render delay 3 ticks; hold-arm cap; CatchArm rate limit; unknown flightId skips visual only; originClamped flag; client cooldown at release; catch magnet lerp; H&D step order; run-end vs Victory tick; Belt/Water silhouettes; prompt slot 2 s; floor strings; screen-reader throwing out of scope v1; off-screen bonk chevron; armedBeforeLaunch; hat pin = in catch reach; drop label third line (D4)
- [DEFERRED] verdictRangeM; heavy 4.7 m range; dead-reckoned catch sample and delayed-own-position prompt; remote playback negative age; audio cues / accessibility rows / ContentValidator destination docs
Reviewed-Content-Hash: design/gdd/throwing-catching.md 646891c99fe9b2279568bcdeef323e664f3f912a
Reviewed-Content-Hash: design/registry/entities.yaml 28e04a7e25136d840795880509d04e7591ffd98b

### Revision applied — 2026-10-16 (same session, autonomous mode)
All 13 blocking items and the cheap recommended items were applied as targeted edits. The doc went from 1,449 to 1,307 lines (−9.8 %, inside the CD's ≤ 0 % net-growth cap, D6).
- **Rules:** F4 was rewritten as entry/exit intervals with CeilDiv/FloorDiv, a cylinder = circle ∩ y-slab, and deterministic heightfield sub-sampling. The F5 timeline was corrected (the prompt leads by ≈ 400 ms; reaction budget ≈ 340 ms at 120 ms RTT).
- **CD rulings:** T5 no dice per D1 (spread knob, zone display and spread trigger deleted). T3 per D2 (exact colour, pattern and glyph; only label text and sound wait 100 ms; `previewSettleMs` neutral marker). D3 catch-preemption gate. D5 leaky-bucket cooldown (`throwBurstTicks` replaces `cooldownSlackTicks`).
- **Network:** three bandwidth ceilings (2.1 / 10.1 / 25.3 kbps); positions 12 B (payloads 25 / 30 / 18 / 1 B); `clientTick` dropped; catch trust stated as unbounded by design. The thrower timeline (ghost lead, snap, hold at static contact) and the per-frame preview budget were fixed.
- **Consistency:** a single receiver's-base fallback; F8 Chebyshev push-out along −u or the outward normal; hash tie-breaks; the metric unified with Manual Delivery (throws 50–80 % of player-driven units). Sim Core is now listed as designed (phase 4 / phase 10).
- **ACs:** rewritten with milestone tags, evidence rules and boundary fixes; new TC-T7-3, TC-E-7, TC-N-4, TC-F4-1f/g/h.
- **Scope cuts (D6):** Cross-References folded into Dependencies "Sources". Network compressed with "ADR inputs". The UI catalogue and outcome-beat timings were moved verbatim to `design/ux/throwing-catching-seed.md`, with review corrections. Hand-back OQs merged (17 → 16 rows; OQ2 carries all hand-backs, including Simulation Core's 25 B).
- **Registry:** throwBallistic, catchWindow, throwLabelMoney, throwCooldownTicks, catchPredictAndTapMs, throwReceiverOrder, throwNoDice, throwRangePermille and throwPayloadBytes were updated, plus a referenced_by note on interactResolutionOrder. YAML validated.
- **Pending owner ratification:** D1 (spread knob retired; amends Mail R13/OQ8 text) and D2 (reverses the CD's logged debounce ruling).

Status → In Review. A fresh `/design-review design/gdd/throwing-catching.md` decides Approved. The receipt above stamps the pre-revision bytes, so the next run reads the doc as CHANGED and performs a full review.

### Hand-back reconciliation — 2026-10-16
Autonomous planning run (Phase 2). Hand-backs addressed to this GDD were collected from the Open Questions and contract tables of grid-inventory, player-character-movement, input-controls, mail-destinations, manual-delivery, hud-notifications, simulation-core and content-data-definitions, and from `production/session-state/planning-run-2026-10-16.md`. Status stays **In Review**. Lines 1,307 → 1,307 (bytes −0.2 %). Score: **19 adopted, 4 adapted, 1 rejected**.
- **Adopted (already satisfied, or now stated):** Grid Inventory OQ4 (whole-stack thrown misdelivery), OQ6, OQ7, OQ21, OQ24 (closed there); Player Character OQ16 (closed there); Input OQ-IC-3 (b), (d), (f); Mail T6 swept descending-entry test, `ResolveThrownLanding(…, releaseTick)` (T6, Interactions, Dependencies), door and team complaint buckets (R9), one landing = one op and one `DeliveryOutcome` plus the zero-unit floor-stop rate limit (R16), OQ8 residuals (×N money label, white Reject); Simulation Core HB3 (spin is a cosmetic `flightId` function; future randomness on a u32 launch ordinal, OQ1), HB6 (TC-P-2 0.15 / 0.5 ms; synchronous phase-4 calls in T5; no tick field on `ThrowRelease`, Network row); HUD H11 (the landing-marker rect is published to HUD as a protected rect).
- **Adapted:** OQ-IC-3 (a) `catchGraceMs` 120 → one knob `catchTapMs` 500; (c) ✓ ring / X / disc, with the ring green-only (art-director), instead of ✓ / ✗ / ○; (e) no `clientTick`, `units` added, an uncorroborated origin is replaced, not rejected; Mail OQ8 "supply execution variance by M-1a" → human variance only (CD D1, pending owner ratification).
- **Rejected:** Content & Data FT-1's `ThrowRangePm` spelling. The key stays `ThrowRangePermille` (registry); noted in OQ2(b).
- **Conflicts:** Simulation Core still carries "Throw spread is in mrad" and a "spread seed" cross-reference (hand-back OQ2(a)). Simulation Core is unreviewed; its phase-4 slice was adopted because the registry `tickBudgetSplit` already carries it and F4's ≈ 0.13 ms estimate fits. No creative-director ruling was needed: D1 and D2 stand, pending owner ratification.
- **OQ2** was rewritten from a hand-back list into "adopted by owners" plus two open items. A one-line tuning row (`claimToleranceMm`, not owned here) was folded into "Knobs owned elsewhere" to hold line count.
- **Registry:** `tickBudgetSplit` (referenced_by TC-P-2), `throwNoDice` (updated: HB3), `throwRangePermille` (C&D spelling note), `throwPayloadBytes` (referenced_by Input R11). YAML validated.
- Consistency pass — 2026-10-16: reason dimming "2 s (reasonShowTicks)" → HUD transient strip at its F1 lifetime (UI, TC-U-2c); catch results routed per HUD H4 (clean catch = hotbar pulse + history; thrower's "Jules caught 15 · 5 fumbled" = personal toast, not feed) in Edge Cases, TC-T7-2h, UI, Interactions.
