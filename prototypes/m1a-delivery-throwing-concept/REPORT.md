<!-- PROTOTYPE - NOT FOR PRODUCTION -->
<!-- Question: Do players choose to throw mail (with real misdelivery risk) and enjoy it? -->
<!-- Date: 2026-10-15 -->

# Concept Prototype Report: M-1a — Delivery + Throwing greybox

> **Date**: 2026-10-15
> **Prototype Path**: Engine (Godot 4.7.1, GDScript)
> **Concept File**: design/gdd/game-concept.md
> **Milestone**: M-1a (design/spec/13-v1-cut-and-decisions.md §6; split per PR-SCOPE in design/gdd/systems-index.md)

---

## Hypothesis

If the player hand-delivers mail by matching addresses and can throw it along an arc, they will choose to throw and enjoy it — evidenced by, in a 10-minute session on one street with 10 houses, the player voluntarily throwing a meaningful share of deliveries, continuing after a misdelivery, and the owner plus two friends each asking to play again.

---

## Riskiest Assumption Tested

That throwing with an unsoftened misdelivery risk (the arc is honest; a bad throw costs money) reads as funny rather than frustrating. Tested first: throwing was the first thing built after walking.

**Outcome:** the sole tester reports throwing as "much more fun than hand delivery" and did not report the penalty as frustrating ("it was as expected"). The *funny* half is unobserved — a single developer playing alone has no audience, and the comedy of a bad throw is social (see Result).

---

## Approach

One flat street, 10 houses with numbered mailboxes (odd west, even east), a Post Office that spawns a Letter / Small / Medium item every 5 s, a capsule carrier with an 8-slot hotbar, hand delivery (hold E 0.4 s with the address-match prompt from spec 09 §3.1), and throwing (hold LMB for an arc preview coloured green / red / white per spec 13 §4.1, release to throw; speeds 14 / 14 / 10 m/s by weight class; 3 s max flight; a miss lands as a pick-up-able world item). Money with the 0.5 misdelivery penalty (spec 03 §2.2), on-screen CHA-CHING / THUD stings with a screen flash, and a live hand-vs-throw counter written to `user://playtest_stats.txt` on F2, at 10:00, or on close.

Built in one session; one parse error fixed on first headless run (type inference on a Dictionary-sourced float). A headless smoke test (`test_headless.gd`) passes: PO and mailbox focus, right and wrong hand delivery (+8 / −35), a throw landing in a mailbox (+8), a missed throw landing on the ground.

**Path chosen:** Engine
**Reason for path:** throwing feel, arc and aim are the hypothesis; browser latency would distort it.

**Shortcuts taken (intentional):**
- No grid inventory — one item per hotbar slot; no stacking.
- Three mail kinds only; spawn every 5 s instead of 15 s batches.
- Single player, no networking, no procgen, no belts, no raids, no shop, no shifts.
- Capsule art, box houses, label-only addresses, no audio assets.
- Everything built in code in one `main.gd`; hardcoded values.

---

## Result

Tester's own words (owner, solo, 2026-10-15):

- Hypothesis: "Confirm throwing is much more fun than hand delivery."
- Best moment: "When throwing the mail. While the base game as is isn't the most fun idea, adding multiplayer and our planned additions will add chaotic energy that makes it great."
- Worst moment: "It was as expected." No frustration or confusion reported.
- Surprise: "No surprises."
- Playtesters: "Just me."

Not observed: the share of deliveries thrown vs walked (no stats file was written — the session was not run to 10:00 and F2 was not pressed), behaviour after a misdelivery, and whether anyone asked to play again. No friends played.

---

## Metrics

| Metric | Value |
|--------|-------|
| Path used | Engine (Godot 4.7.1, GDScript) |
| Iterations to playable | 1 (one parse fix, headless) |
| Prototype duration | ~1 session build; playtest length not recorded |
| Playtesters | 1 internal (owner/developer) / 0 external |
| Feel assessment | Tester: throwing clearly preferred to hand delivery; no control or arc complaints. No quantitative throw share captured. |
| Hypothesis verdict | NOT FALSIFIED — preference signal only. One developer-tester prefers throwing to hand delivery; none of the three named signals (throw share, post-misdelivery behaviour, friends asking to replay) was measured. (Changed from "PARTIALLY CONFIRMED" at CD-PLAYTEST.) |

---

## Recommendation: PROCEED — accepted with Creative Director concerns

> **CD-PLAYTEST: CONCERNS** (2026-10-15). PROCEED stands as a decision to keep going, not as proof the idea works. Core verb supported; honest penalty did not frustrate. Fellowship (#1) and humour (#2) NOT ASSESSED — a solo greybox cannot contain them. The tester's caveat ("base game isn't the most fun on its own; fun comes from multiplayer") is possible drift from Pillar 5 (solo must be *worth playing*, not just winnable) and must be recorded in the concept doc as a risk. The risks below are **gate requirements**, not suggestions.

Recommendation given by the owner. The core verb the hypothesis isolated — throwing addressed mail at a mailbox with real consequences — is the part the tester enjoyed, and nothing in the build fought it. The evidence is thin (one developer, no friends, no numbers), so this PROCEED rests on the owner's judgement rather than observed external signal; the tester's own caveat that the base loop "isn't the most fun idea" on its own and depends on multiplayer chaos is recorded as a risk, not dismissed. The next gate that can actually test that claim is M0 (4 friends over the network, with the CD-SYSTEMS addition "the group spontaneously throws to and catches from each other and wants another session").

---

## If Proceeding

- **Core tuning values discovered:** launch speeds 14 / 10 m/s (light / medium) with a 6° upward loft and 9.8 m/s² gravity produced a usable arc at street scale (mailboxes ~9 m from the road centre); 0.4 s hold-to-deliver was not reported as slow; 5 s spawn cadence kept the hotbar from running empty. None of these were A/B tested — treat as starting points for Tuning Knobs, not findings.
- **Assumptions confirmed:** the arc preview colour (green / red / white) is enough aim feedback; an honest misdelivery penalty was not reported as frustrating; hand delivery works as a baseline the player will abandon for throwing when allowed.
- **Assumptions disproved:** none observed. Open: the concept doc's MVP hypothesis assumed solo delivery + throwing would be fun *in itself*; the tester says it is not, and that the fun is expected from co-op chaos and the planned additions. This moves weight onto M0 and onto Pillar 2's "friendslop surface".
- **Emergent mechanics:** none observed in a solo session.

**Gate requirements carried forward (set before anyone plays):**
1. **Two-friend greybox, before M0.** Same build, unexplained, observed silently, full 10:00 so `playtest_stats.txt` is written. Pass if: throws ≥ 50 % of deliveries; at least one misdelivery occurs and play continues within ~10 s with no quit; unprompted laughter or a callout at a bad throw (the humour signal); each friend asks to play again unprompted. Ask one question after: "What was confusing?"
2. **M0 Fellowship criterion** (already in `design/gdd/systems-index.md`): 4 networked players throw to and catch from each other spontaneously and the group asks for another session. If this fails, "the fun is social" is false and the core verb needs redesign before more systems are built on it.
3. **Solo check at M0 or later:** one solo session rated against the group sessions, so Pillar 5's solo floor is measured, not assumed.
4. **Concept doc:** record "solo loop may not carry itself; fun located in co-op chaos" as a design risk (`/design-review design/gdd/game-concept.md`).

**Next steps:**
1. `/design-review design/gdd/game-concept.md` — fold in the "fun is social" finding
2. `/gate-check` — Concept → Systems Design
3. `/design-system` for the MVP tier in index order (Grid Inventory first), using the values above in Tuning Knobs

---

## If Pivoting

Not applicable (PROCEED).

---

## If Killing

Not applicable (PROCEED).

---

## Lessons Learned

- **What assumptions were broken by actually building this?** That a solo greybox could answer the full M-1a hypothesis. It can answer "is throwing the better verb?" (yes, for one tester) but not "is this fun enough to replay?", which the tester themself located in multiplayer.
- **What surprised us that didn't show up in the brainstorm?** Nothing reported by the tester. On the build side: all three systems the greybox needed (focus/interaction, ballistic step, feedback stings) fit in one file with no engine friction, which supports the producer's "throwaway Sim Core until M0" call.
- **What would we test differently next time?** Run the full 10 minutes so the stats file is written, and get the two friends in the same room before calling the gate. Record throw share, not just a verdict.

---

> *Prototype code location: `prototypes/m1a-delivery-throwing-concept/`*
> *This code is throwaway. Never refactor into production.*
