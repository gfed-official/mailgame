# Throwing & Catching

> **Status**: In Review — `/design-review` 2026-10-16 NEEDS REVISION (13 blocking); revision applied, a fresh re-review decides Approved (`design/gdd/reviews/throwing-catching-review-log.md`)
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 (bonk warning = the Catch prompt; fantasy damage line matches F6; goon stamp SIGNED FOR — RETURN TO SENDER reserved for Run Over; solo-floor replay question in fantasy test 3; milestone slice stated; Pillar 5 in the header). **CD-SENIOR (design-review) 2026-10-16** superseded two rulings, ratified by the owner 2026-10-16: the spread knob is retired (D1), and preview colour is exact every frame (D2); the "Heads up, Jules" label line is dropped (D4); the doc was ruled oversized (D6), with UI and beat detail parked in `design/ux/throwing-catching-seed.md`
> **Reconciled 2026-10-16 with sibling GDDs (hand-backs: 19 adopted, 4 adapted, 1 rejected)** — Grid Inventory OQ4/6/7/21/24; Player Character OQ16; Input OQ-IC-3 (a)–(f); Mail R9/R13/R16, OQ8 (`releaseTick`); Simulation Core HB3/HB6; HUD H11 protected rect. Rejection in OQ2; log in the review log
> **Author**: owner + agents (autonomous mode, /design-system)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Implements Pillar**: Pillar 2 (Friendslop surface, factory depth — "a bad throw is never softened"), Pillar 1 (The mail must flow), Pillar 3 (Hands beat machines), Pillar 5 (Scales from 1 to 8 — the trick shot is the solo floor); MDA Fellowship (#1)

## Summary

Throwing & Catching lets any carrier hold Throw to preview an honest arc for the selected hotbar entry, then release it. A letter or a whole 20-letter stack flies a host-authoritative gravity arc and lands wherever it really goes:

- the right mailbox (green, solid, ring ✓) pays for every piece;
- the wrong one (red, dashed, X) misdelivers every piece;
- a teammate who presses Interact catches it into their hotbar;
- a teammate who doesn't gets bonked on the hat;
- a goon takes 5 / 15 / 40 damage;
- a belt takes it onto its lane;
- anywhere else, it lands on the ground or floats in the water.

It is the game's signature chaos verb, "Heads Up!": the arc tells the truth about the town but not about your friends. It is the fastest way to move mail by hand and the main source of shared, honest failure. There are no dice in the Sim; a bad throw is never softened by the game, only by a friend who catches it.

> **Quick reference** — Layer: `Feature` · Priority: `MVP (M-1a; catch M0; goon damage and belts M-1b)` · Key deps: `Grid Inventory & Containers, Mail & Destinations, Player Character & Movement, Input & Controls, Simulation Core, Content & Data Definitions, Networking & Session (M0), World Generation; provisional: Health & Damage, Belt Logistics, Vehicles & NPC Drivers, Enemy AI & Waves`

> **Milestone slice** (CD-GDD-ALIGN 2026-10-16):
> - **M-1a (solo greybox):** T1–T6 with ground, water, solid, timeout and mailbox receivers; the T3 preview with patterns, markers, ×N tag and money label; T11; T12; T14 telemetry; the release snap, whoosh, landing squash and trail (the solo floor's feel is M-1a scope, not polish); one bundle mesh.
> - **M0:** T7 catch, T8 bonk, T13 relay and the FORWARDED stamp, hat pins, Network, live region, Toggle catch.
> - **M-1b:** goon and belt receivers, the SIGNED FOR stamp.
> - **Later:** Cannon Arm (Perks), haptics, tier meshes, reduced-motion and force-patterns settings.

## Overview

Throwing & Catching is the game's signature chaos verb (spec 13 §1, §4.1). Any carrier on foot can hold **Throw** (hold RMB / LT) to preview where the **selected hotbar entry** will land, then release to send it. One letter or a 20-letter stack goes along a gravity arc. The preview tells the truth before release. It ends **green** in the matching mailbox, **red** in a wrong one and **white** anywhere else, and it never relies on colour alone. Wherever the arc ends decides what happens:

- **A mailbox's catch volume** hands the whole entry to Mail & Destinations' one acceptance rule. That includes the user's ruling that a thrown stack in the wrong box misdelivers every piece (grid-inventory OQ4).
- **A belt** takes the item onto its lane.
- **A goon** takes damage, and the item drops.
- **A teammate pressing Interact** catches it into their hotbar, ready to throw on.
- **Anything else**: the item becomes a WorldItem where it lands.

Mechanically, this system owns Grid Inventory's **FLIGHT** location state. That covers the launch, the host-authoritative integer ballistic (30 Hz, no bounce, 3 s maximum flight), the arc preview's prediction, the order in which a flight's possible landings resolve, the catch window and its race rules, thrown damage, and the launch and landing events every peer plays the arc from. It does not own what a landing *means*: acceptance, payment and complaint belong to Mail & Destinations, placement and WorldItems to Grid Inventory, HP to Health & Damage, and lanes to Belt Logistics.

The player sees it as the fastest way to move mail and the funniest way to fail. Without it, delivery is a walk. The M-1a prototype's only tester called throwing "much more fun than hand delivery". Co-op would also lose its most social verb: the lob across the street, the catch over the hedge, the relay down a road, and the honest red arc you release anyway.

## Player Fantasy

*The framing is direct: players actively throw and catch. It was shaped with creative-director. Framing C, **"Heads Up!"**, was chosen over "Pass It On" (it has no solo floor, the "fun is social" drift that CD-PLAYTEST flagged) and over "Postal Gunslinger" (a solo power fantasy that sidelines Fellowship). The trick shot from Gunslinger is kept as the solo face of this fantasy. This system owns the flight, the release, the pass and catch, and the throw at goons. Mail & Destinations' "Every Door Is a Verdict" owns what happens at the slot.*

**The arc tells the truth about the town. It can't tell the truth about your friends.**

You hold Throw and the arc reaches across the street. It ends in a ring over 22 Elm's mailbox, so you let go. Your arm snaps through and the stack sails over a hedge with its ×20 tag flapping. On your own, that is the craft: the long lob over the bakery roof, the one you didn't walk for.

With friends, the street becomes a relay. Jules is on the corner with her hands up. You lob, she catches, the parcel squashes against her chest, and it goes out again before you've turned round. Three hops and the mail never touched the ground.

The arc goes red and you release it anyway. Everyone can see it coming, because the trail stays red and dashed all the way. The only thing that can save it is a friend stepping in to catch it, and nobody does. The flight is honest about the town, but it can't be honest about people. Dev walks straight into your perfect green arc. The parcel bonks off his hat and drops at his feet. He's fine. He's just standing there.

Then Jules's bag is full when your pass arrives. She grabs, it bounces off her chest and lands on her shoes. When the magenta sashes come over the fence, the same arm turns mail into ammunition. A heavy parcel to a Flyer Tosser's head floors him in one. Now the parcel is lying in the road, and somebody has to go and get it.

**Reference feelings.**
- *Overcooked*: throwing ingredients across a kitchen to a teammate who has to be ready. Taken: the readiness of the catch, which is opt-in. Rejected: catches that happen by accident.
- *Rocket League*: the pass that connects as the team's shared highlight. Taken: the witnessed pass. Rejected: its precision ceiling.
- *Untitled Goose Game*: the straight-faced victim. Taken: Dev's blank stare. Rejected: harm as the goal. A bonk costs seconds, never health.

**Pillars served.**
- **Pillar 2** (*Friendslop surface, factory depth*): "keep the rules honest (a bad throw is never softened)". The arc never lies, and the game never softens a red release. Only a friend can.
- **Pillar 1** (*The mail must flow*): throwing is the fastest manual way to move mail, and throws at goons protect it, at the cost of fetching it back.
- **Pillar 3** (*Hands beat machines, until they don't*): a hand feeding a belt from 10 m beats walking to it. Under the ratified reading, the machine's value is that it works unattended.
- **Pillar 5** (*Scales from 1 to 8*): the trick shot carries a solo run, and the relay is what extra players add.
- **MDA**: Fellowship (#1) is the pass and the catch, and humour (#2) is the bonk and the honest red arc.

**Tone rulings (creative-director).**
- **(a) A teammate who is not catching gets bonked; the throw does not pass through them.** This is the concept's own hook, and art bible §5 already authors "Miss: parcel bounces off the hat".
  - No HP loss, stun, knockback or input interruption.
  - The victim keeps everything they carry.
  - The thrown entry drops whole at their feet. It is not a misdelivery and costs no money or complaint, only time.
  - The hat-lift is the 15 cm shock lift, never the knockdown fly-off.
- **(b) Gag, not griefing.**
  - A per-victim **cosmetic** cooldown (~1.0 s) on the hat-lift and the bonk sound, so spam can't strobe or harass. Items still drop on every hit.
  - **No stat, Payday line, achievement or perk may reward hitting teammates.** There is no "most bonks" entry.
  - Real griefing is misdelivering team mail, which hits the team wallet and is the host-kick's job (spec 13 §4.3).

### Feel & Comedy Feedback

*Required by the systems index (CD-SYSTEMS note). The voice is deadpan postal bureaucracy: carriers are earnest and the world supplies the slapstick. Red means you got it wrong, and magenta means the rival company did it to you. Neither colour ever marks a teammate. Art bible principle 2 applies: every outcome is a physical event in the world, never only a HUD number. Mailbox landings hand off to Mail & Destinations' table (correct throw = "trick shot, witnessed"; thrown stack in the wrong house = "the pratfall, at full price"; closed; locked; refused at the floor). Specifics are under Visual/Audio Requirements.*

| Outcome | World (see / hear) | HUD adds | Comedic beat |
|---|---|---|---|
| Release | Strain wind-up (the aim hold), snap, whoosh pitched down by stack tier; the ×N tag rides the parcel | slot empties | Commitment |
| Red arc in flight | The trail keeps its launch verdict (red, dashed) on every peer's screen | none | Everyone sees it coming |
| Ground landing | Thud, squash, dust puff; lands glyph-up | none | Pending, on the lawn |
| Onto a belt | Clack; settles into the lane | none | The hand feeds the machine |
| Clean catch | Arms up, parcel squashes, body dips, soft *fwump* | slot ring pulse, "Caught ×20 — slot 3" | The pass connects |
| Relay hop | A small inked **FORWARDED** stamp on the parcel; the chime pitch steps up each hop (capped) | none | Bucket brigade |
| Catch race lost | The loser's hands clap on nothing, with a small shrug | "Jules caught it" | "Mine!" |
| Fumble (bag full) | Bounces off the chest and lands at their feet, hands flap; neutral, never red | "Bag full — fumbled" (catcher only) | Hands full, literally |
| Bonk (teammate not catching) | Woodblock *bonk*, hat lifts 15 cm, shock face, the stack drops at their feet | none | The hook |
| Goon hit | Paper-puff crunch, goon flinch, item drops at the goon's feet | goon HP sliver (Health & Damage) | Return fire |
| Goon floored by mail | A small **SIGNED FOR** stamp slams onto the goon (Chalk on Charcoal) | none | Deadpan victory: the rival signed for it |
| Water landing | Plop, foam ring, the parcel bobs for 10 s then is gone (back to the Intake) | none | Lost at sea, briefly |
| Early release | The arc vanishes, the arm drops | "Hold to aim" | The twitch that didn't happen |

**Fantasy tests.** Fail any one and the system has broken its fantasy.

1. **The pass happens (Fellowship).** At M0, in ≥ 3 of 4 four-player sessions:
   - every player makes ≥ 1 unprompted player-to-player catch per 5 minutes of Delivery ("unprompted" = the facilitator gives no catch instruction before the debrief);
   - each session contains ≥ 1 unprompted relay of ≥ 2 hops;
   - ≥ 1 catch per session was armed before the flight launched (`armedBeforeLaunch`), so the test measures the pass, not prompt-following.

   Logged from `CAUGHT` outcomes, with thrower and catcher ids and the hop chain.
2. **Laugh, not blame.** Two raters code the utterances within 5 s of every bonk, fumble and red-arc release.
   - Laugh or callout utterances outnumber blame in ≥ 3 of 4 M0 sessions.
   - There are zero throw-related host-kicks and zero "stop throwing at me" requests.
3. **Solo floor (Pillar 5).** At the two-friend greybox and at the M0 solo check:
   - throws are 50–80 % of player-driven correctly delivered **units** (Manual Delivery test 3's denominator; the floor is the prototype's gate threshold, and the ceiling is Manual Delivery's hand ≥ 20 %, the single revisit trigger shared by both GDDs);
   - ≥ 20 % of mailbox throws land from ≥ 10 m (stats file);
   - the solo player starts a second solo run unprompted (behavioural, not only asked), and rates the solo session ≥ 3 on the same 1–5 scale used for the group sessions (prototype gate 3).
4. **Stacks get thrown (OQ21).** In the first two-friend playtest, the median thrown `units` for letter and postcard stacks is > 5. If players split down to avoid whole-stack misdelivery, revisit the default. Never respond by softening the penalty.

## Detailed Design

*Specialists consulted:*
- *game-designer (primary: rules, receivers, catch, degenerate strategies)*
- *systems-designer (ballistics, sweep, catch and damage maths)*
- *gameplay-programmer (feel, preview cost, Sim/Presentation split)*
- *network-programmer (FLIGHT authority, timing, catch protocol)*
- *art-director and ux-designer (arc preview, feedback)*
- *creative-director (Player Fantasy tone rulings)*

*Decisions marked **[D]** were taken in autonomous mode by adopting the owning specialist's recommendation, or the majority view where they disagreed. All are logged in `production/session-logs/decision-log.md`. Numbers marked **PROPOSED** are new to this GDD. All Sim arithmetic follows the project convention: integer mm, mm/s, 30 Hz ticks, permille (‰) and cents.*

### Core Rules

**T1 — Who can throw, and when.**

Throw is an aim-release action (Input & Controls R3), live only in World context: body OnFoot, no overlay, not Holding, no capture (Input R2's reachable-state rule).

| Carrier state | Throw |
|---|---|
| Walking, sprinting, jumping or in the air, wading (< 900 mm) | allowed: movement never stops (Player Character P3) |
| Standing on a belt, a vehicle roof or a quay (OnSupport) | allowed: launch velocity is **not** inherited from the support **[D]** |
| Spawn-protected | allowed. Throwing mail does **not** end protection. Only a damaging action does (Player Character P17): a throw that resolves HIT_ENEMY ends the thrower's protection at that tick |
| Swimming | refused: "Can't throw while swimming" (spec 13 §4.5, Player Character P6) |
| Holding (cursor entry) | refused: "Put it away first" (Input R6 ruling 3) |
| Mounted (seat or turret), Panel, Build, Map, Station, Dead, Frozen | not live (Input R2) |

- **What you throw.** Throw sends the **selected hotbar entry**.
  - Selected slot is the hands slot (0): the press shows "Select mail to throw" and no aim starts.
  - Selected slot is empty: "Nothing to throw", and no aim starts.
- **Cooldown (leaky bucket, CD-SENIOR D5) [D].** Throw may be pressed at any time. The client checks its own `throwCooldownTicks = 5` **PROPOSED** at **release**: a release inside the cooldown is held and sent when it elapses (≤ 5 ticks). The host keeps a theoretical arrival tick `TAT` per player. On each accepted launch at tick T it sets `TAT ← max(TAT, T) + throwCooldownTicks`, and it rejects `throw_cooldown` when `T < TAT − throwBurstTicks` (5). This needs no client clock. A hitch can deliver ≤ 2 releases back-to-back; the sustained rate is ≤ 6/s per player, and the team cap is ≤ 48/s.
- **The real rate limit is the minimum aim.** `throwMinAim` is a player setting, 0.15–0.40 s, default 0.25 (Input F11 and R10, **confirmed here**, closing OQ-IC-3(b)). It gives a minimum cycle of ≈ 0.32–0.57 s (≈ 0.42 s at default, ≈ 2.4 throws/s per thrower).

**T2 — What launches.** The **whole selected entry** detaches to FLIGHT (Grid Inventory R7 `throw`, CD D-D):

- **Stacks.** One letter or a 20-letter stack. There is no auto-split and no "throw one" modifier: singles come from Split. The preview shows "×N".
- **Non-mail is throwable [D].** Tools, weapons, materials and consumables (spec 13: "any held stack"). It uses its ItemDef `weightClass` (default `light`, Grid Inventory R16). It gains no behaviour from being thrown: a thrown bandage heals nobody, a thrown axe does thrown damage, not axe damage.
- **Bulk.** Cargo is `carryByHand:false` and can never be on the hotbar (Grid Inventory R5), so it cannot be selected for a throw.

**T3 — Aim and preview (client).** While aiming:

- **Launch direction.**
  - The thrower's client computes it from the camera aim: yaw, and pitch clamped to −60°…+70°, plus a fixed `loftDeg = 6°` **PROPOSED** (the prototype's value). That gives an elevation of −54°…+76°.
  - The direction is quantised to an int16×3 unit vector (F1) **before** the preview is drawn.
  - It is not a hit point under the crosshair. A small Chalk White dot crosshair marks camera forward only **[D]**.
- **The preview is an honest forecast, from one shared function.** It runs the **same Sim function** as the host flight (F2 ballistic, F4 sweep) from the predicted origin (F1). It is honest for static geometry **provided the claimed origin is corroborated** (T4 check 9). It may skip a frame only when its whole cache key is unchanged: quantised direction, origin, world version (constructs), destination phase, wallet and teammate positions. A pad or a walking carrier changes the key every frame, so the **per-frame worst case is the budget** (Game Feel).
- **Colour and end marker come from static geometry only**: catch volumes, terrain, constructs, belts and water. The colour is Mail & Destinations' client-side `Evaluate` on the first catch volume the arc enters (Mail R13):
  - Accept → **green, solid line, ring marker with ✓**;
  - Misdeliver → **red, dashed line, X marker**;
  - everything else → **Chalk White, dotted line**: Water a wave-bottomed disc, Belt a chevron-notched disc, and Ground, Reject, Blocked and Timeout a plain disc with a reason glyph.
- **Teammates are shown, never predicted; goons are neither [D].** If the arc passes through a teammate's current catch sphere, a **hat pin** with their name marks where they are standing now. It means "in catch reach" only. It carries no ✓/✗ and no "Catch" promise: green and red are reserved for mailboxes. Goons are never pinned; a goon hit is a consequence (Pillar 2).
- **Label.** The end marker carries a two-line label: place, then "×N · money" (UI Requirements). It names the address for Accept and Misdeliver and shows the money at stake (F7) **[D]**, so a 20-stack throw is a legible choice (OQ21) without softening it.
- **No stale verdict (CD-SENIOR D2, ratified by the owner 2026-10-16).**
  - Line, marker position, colour, pattern, marker shape and glyph are exact every frame.
  - Only the label text, the live-region line and the preview sound wait 100 ms of stability, because those are what flicker unreadably. Their first appearance is immediate.
  - The accessibility setting `previewSettleMs` (0–250, default 0) shows a **neutral pending marker** during a class change: a Charcoal-outlined ring with no glyph and no colour. It never shows the previous verdict.
  - A release mid-flick is the thrower's execution: the trail carries the true class from launch.
- **Visibility.** Only the thrower sees the aim arc. Others see the flight once it launches (Visual/Audio) **[D]**. A visible aim would be a free warning before a bonk.

**T4 — Release and launch.**

- **Release.** On release with `aimHeld ≥ throwMinAim` (or the second press in Toggle mode, Input G7), the client sends `ThrowRelease{seq, entryId, units, origin, dir}` on that frame (Input R11). `origin` is the claimed **feet** (the host derives the hand point by F1), and `dir` is the quantised direction of the **last drawn** preview frame, so what you saw is what is sent. `units` lets the host refuse a stack that was merged or split after the thrower last saw it, so nobody throws a number they did not see. This is Grid Inventory's `throw {entryId}` op with a payload extension; `seq` is its `reqId`. The client also starts a **launch ghost** (Network).
- **Host validation, in `Inputs.Apply`.** Checks run in this order; the first failure is the reply:

  | # | Check | Reply on failure |
  |---|---|---|
  | 1 | malformed, or the entry is not in the sender's hotbar | `bad_request` |
  | 2 | entry unknown, no longer the selected slot, or its unit count ≠ the request's `units` | `stale_entry` |
  | 3 | actor not Alive | `dead` |
  | 4 | actor on break | `frozen` |
  | 5 | actor swimming | `swimming` |
  | 6 | actor not on foot | `unavailable` |
  | 7 | cooldown (T1) | `throw_cooldown` |
  | 8 | the direction's norm or elevation outside F1's bounds | `bad_direction` |
  | 9 | the claimed origin's feet are not corroborated by Player Character F5 (claim-and-corroborate: within `claimToleranceMm` 1500 of a sample, or of the dead-reckoned position, in the last `rangeHistoryTicks` 9, support-relative for roof riders) | the host uses the newest history sample as the feet (Input OQ-IC-3(e)); **not a rejection** **[D]**. `ThrowLaunched` sets `originClamped`, the ghost snaps, and the client counts it in telemetry |

  These are consistency checks, not anti-cheat (spec 13 §4.3).
- **Launch tick.** The host launches at its apply tick **T**, with no back-dating **[D]**. Back-dating would need the rewind spec 13 deleted, and would hide short throws from remote peers.
- **The host creates the `FlightRecord`:**

  | Field | Value |
  |---|---|
  | `flightId` | uint16, wrap-safe (processing order is creation order; ids are never compared numerically) |
  | `entry` | contents, `mailIds`, `entryId` kept |
  | `throwerId` | the thrower |
  | `classPts` | of the entry's class |
  | `origin`, `vel` | int32 mm and mm/s, F1 (speed from class × `ThrowRangePermille`, never from the client) |
  | `launchTick` | T |
  | `thrownPm` | the thrower's `ThrownCorrectPayMult`, **snapshotted** (Mail R13) |
  | `releaseTick`, `openAtRelease` | the apply tick and whether deliveries were OPEN then: Mail R4's closing grace and the timeliness shift (F7's T‰) judge a throw by when it started. A release sent 1–3 ticks before close but applied after it bounces `closed`, even though the preview showed green; this is accepted |
  | `hop` | T13 |

- **Then, in the same tick**, Grid Inventory moves the entry PLACED → FLIGHT (its `Removed` event), and `ThrowLaunched` is broadcast.
- **A release can never strand an entry.** A rejected release leaves the entry in its slot (`throw` is non-optimistic, Grid Inventory Network rule 4), and the client's ghost fades.

**T5 — Flight.**

- **No dice in the Sim [D, CD-SENIOR D1 2026-10-16, ratified by the owner 2026-10-16].** Launch is F1 on the quantised release direction, F2 and F4 are exact, and there is no spread term.
  - **Execution variance is human:**
    - the marker's sensitivity to aim grows with range (≈ range × 17 mm per degree);
    - releases happen in haste and on the move;
    - teammates, goons and vehicles cross the arc unpredicted.
  - **A throw reaches a wrong door only if the preview showed red on the release frame**, and its trail then stays red. T14 logs `redAgeAtReleaseMs` (how long the arc had been red) so playtests can tell a deliberate red release from a flick.
  - Solo, an unintended wrong door is therefore a flick or a misread under pressure, never the game's doing. Mail & Destinations adopted this as R13 and OQ8 (2026-10-16).
  - If hand share falls below 20 % (Manual Delivery test 3), revisit with non-random levers only (Open Questions 1). Never seeded spread.
- **Position.** The flight's position at age `k = tick − launchTick` is the closed-form integer ballistic F2, with gravity `g = 9800 mm/s²` **PROPOSED** (the prototype's value; the carrier's own 24 000 mm/s² would cap light throws at ≈ 8 m). There is no drag, no bounce and no spin in the Sim.
- **The sweep.**
  - Throwing's step (`Throwing.Step`, Simulation Core phase 4) runs each tick **after `Inputs.Apply`** and before automation (Mail & Destinations R6: hand ops → thrown landings → placed feeders → NPC drops). Catches and landings are synchronous `TryCatch` / Mail calls inside phase 4 (Simulation Core R9); only damage is queued, for `Combat.Resolve` (phase 10).
  - Flights are processed in creation order (`flightId` wraps and is never compared numerically).
  - Each flight sweeps the segment `p(k−1) → p(k)` for `k = 1 … maxFlightTicks` (90 = 3 s, spec 13). Age 0, the launch tick, does not sweep **[D]**.
- **Who is never a receiver.**
  - **The thrower** never receives their own flight: no self-catch, no self-bonk.
  - **Knocked-down and on-break bodies** have no collider, so a flight passes through them.
  - **Mounted carriers** are not receivers; their vehicle body is a solid (T11).

**T6 — Receivers and resolution order.**

- **Earliest contact wins.** In each tick's sweep, every receiver the segment touches yields a contact fraction `f` (0–1000 ‰, F4). The **earliest** contact wins, so nothing tunnels at up to ≈ 1.1 m per tick.
- **Tie-breaks.** Equal `f` resolves by rank: **armed catcher 0 · mailbox catch volume 1 · goon 2 · teammate (bonk) 3 · belt lane 4 · solid 5 · ground 6 · water 7**. Further ties go to the smaller distance to the receiver's centre, then the lower `hash(flightId, receiverId)`, so no player always wins a tie.
- **Every flight ends in exactly one terminal state.** This is Grid Inventory R19's conservation invariant.

| Receiver | Guard | Terminal | Effect |
|---|---|---|---|
| Armed catcher | T7 | **CAUGHT{unitsIn, unitsFumbled}** / **FUMBLE** (unitsIn = 0) | Grid Inventory `TryCatch`: mail hotbar-first (`TargetFirst`: merge into a matching hotbar stack, then the lowest free slot ≥ 1, then inventory, then backpack), non-mail by category order (R9). The **newest** units that fit nowhere become one WorldItem (`origin = Thrown`) at the catcher's feet. CAUGHT if any unit went in, FUMBLE only if none did |
| Mailbox catch volume, destination OPEN (or **open at release** and ≤ `closeGraceTicks` 90 after close — Mail R4 closing grace) | **descending entry only** (P1.y < P0.y on the swept segment; a flight already inside the volume whose next segment descends lands at f = 0); Mail R5 → Accept or Misdeliver | **MAILBOX_ACCEPT** / **MAILBOX_MISDELIVER** | `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)` (Mail R13): one op, one `DeliveryOutcome` (Mail R6, R16). Accept pays **every** unit, with Cannon Arm from `thrownPm`. Misdeliver consumes **every** unit oldest-first until the wallet floor (user decision OQ4); the unconsumed remainder becomes one WorldItem at the mailbox (F8 drop point); complaint is limited by the door and team buckets (Mail R9) |
| Mailbox catch volume, Reject | descending entry; CLOSED `closed` (released while closed, or applied > 90 ticks after close), LOCKED `not_on_route`, or non-mail `wont_fit` | **MAILBOX_REJECT** | The whole entry becomes one WorldItem at the F8 drop point: no penalty, no complaint, 300 s timer. Throwing owns this bounce |
| Goon (Enemy AI & Waves) | alive and not dying; capsule inflated by `enemyHitSlackMm` = 300 **PROPOSED** | **HIT_ENEMY** | Health & Damage `ApplyDamage(F6, source = Thrown, actor = throwerId)`, queued once per flight and applied in `Combat.Resolve`, with no pierce or knockback; the whole entry becomes one WorldItem at the goon's ground point |
| Teammate (not armed, or armed but ineligible) | Alive, non-thrower, not mounted, not position-stale (> `positionStaleTicks` 30, Player Character F5), capsule `r 300 × h 1400` at the newest reported position | **BONK** | No damage, stun, knockback or inventory change; the whole entry becomes one WorldItem at the teammate's feet; does **not** end their spawn protection (T8) |
| Belt lane top face (Belt Logistics) | lane exists | **BELT** | `TryInsert(lane, point, unit)` per unit, oldest first; units that fit join the lane; the remainder becomes one WorldItem at the landing point without blocking the belt (Grid Inventory edge case) |
| Solid: construct, wall, house, vehicle body or roof, container (chest, depot, Intake console, truck cargo face) | — | **GROUND** | The flight stops at the contact point and the entry drops straight down onto the ground below it. **Containers never accept throws [D]**: a throw is not a teleport into the Intake or a chest (Grid Inventory R12's drop reasoning). **Items are never parented to vehicles**: a moving roof leaves the item behind on the ground below (Grid Inventory edge case) |
| Terrain (walkable ground) | — | **GROUND** | One WorldItem at the contact point (`origin = Thrown`; the 300 s timer starts at landing) |
| Water or other non-ground tile | — | **WATER** | One WorldItem that floats for `waterReturnSeconds` = 10 with no pickup, then mail returns to the **back** of the Intake queue and non-mail is destroyed (Grid Inventory R12) |
| No contact by age 90 | — | **TIMEOUT** | The item drops onto the surface directly below its age-90 position, then resolves GROUND or WATER |

- **Mailbox volumes.** A rising segment passes through a catch volume as if it were absent: the mailbox body is not a solid, and only descending entry counts. A flat throw never grazes the nearer mailbox, and the first qualifying volume wins (Mail OQ8).
- **WorldItem placement.** A WorldItem created by any terminal **except WATER** (which floats at its contact point) is snapped to walkable ground within 1000 mm. On a solid footprint the search grows 1000 → 3000 → 6000 mm back along −v. Failing that, it goes at the **receiver's base**: the feet of a catcher, teammate or goon; F8's front point for a mailbox; otherwise the nearest walkable tile (World Generation's query). Positions are clamped 1 m inside the map edge before snapping.
- **Thrown items never damage anything else.** That covers constructs, vehicles and teammates (spec 05: friendly fire off; players cannot damage constructs).

**T7 — Catching.**

- **Arming.** An Interact press arms a catch when it resolves to **Catch** or to **no target**. Input & Controls R4.3 sets the order: a catchable inbound item first, then the focused target's verb, then catch-armed while held.
  - A press that latches a target verb (deliver hold, Open) never arms.
  - A latched deliver hold is never converted into a catch (Player Character P11).
  - The client sends `CatchArm{down}` on the press and `CatchArm{up}` on the release.
  - The host keeps the catcher **armed from the down's apply tick until `min(max(upApplyTick, downApplyTick + catchTapTicks), downApplyTick + 90)`**, with `catchTapMs` = 500 (15 ticks) **PROPOSED**. A press of ≤ 200 ms therefore arms exactly as long as a hold (Input H-FX-4). A held arm is capped at 3 s like Toggle, so a stuck key cannot shield goons or intercept mail indefinitely.
  - **Catch preempts a focused verb only under the D3 gate (CD-SENIOR) [D]:**
    - only while the F5 prediction is true, with no off-hysteresis for preemption;
    - never by interrupting a hold already in progress;
    - never when the flight's predicted static receiver is the focused interactable (the mailbox it is lobbed into).

    Otherwise Interact keeps the focused verb. This is a hand-back to Input R4.3.
  - **Toggle mode** (Input G7): the client sends `down` on the first press, nothing on its release, and `up` on the second press. The arm latches for `catchArmSeconds` = 3.0 s (90 ticks), so `armedUntil = min(downApplyTick + 90, upApplyTick)`.
  - **Window ends:** a catcher is armed at tick t iff `downApplyTick ≤ t < armedUntil`.
  - Death, mounting, swimming, opening the panel, starting an aim, disconnect or rejoin clears the arm. A rejoiner, and a player just teleported, has an empty position history, so they are not catchable until their first report arrives.
- **Who may catch.**

  | Carrier | Can catch? |
  |---|---|
  | Alive, on foot, in World or Holding-closed | yes |
  | Holding-closed specifically | yes; the catch goes to the hotbar, never to the cursor |
  | Swimming | **no**: "Dry land required", answering Player Character OQ16 **[D]** |
  | Mounted, in Panel, Dead or Frozen | no (Interact is not live) |
  | Aiming | no: Interact is not live while aiming (Input G1) |
  | The thrower, for their own flight | never |

- **Contact.**
  - An armed, eligible catcher is a receiver: a sphere of `catchRadiusMm` = 1500 (spec 13 §4.1) around the chest, 1000 mm above the feet.
  - Because movement is client-authoritative, the host tests the sphere at **every position that catcher reported in the last 300 ms** (Player Character F5's 9-tick history).
  - Positions more than `catchHistoryMaxOffsetMm` = 1500 **PROPOSED** from the newest one are ignored. This is a plausibility bound so a stale sample cannot reach across the street.
  - The earliest `f` over those samples is the catcher's contact (F5).
- **Two catchers** follow T6: the earliest `f` wins; at equal `f`, the nearer at contact (newest sample) wins, then the lower `hash(flightId, playerId)`. The loser gets no state change and keeps their arm (Visual/Audio: an empty clap).
- **Catch magnet.** Because history and render delay let a catch register up to ≈ 3 m from the catcher's displayed body, the caught parcel lerps into the hands over 100 ms (presentation only).
- **Effect.**
  - **Placement** is T6's CAUGHT/FUMBLE, decided by the catcher's panel **on the host at the resolution tick**. A same-tick hand op that freed a slot counts, because hand ops precede landings.
  - **Risk.** The catch ends the flight's misdelivery risk, and the `thrownPm` snapshot is discarded. Mail the catcher later delivers or throws is that catcher's (`actorId`).
- **Selection after a catch (answers Grid Inventory OQ24) [D].**
  - The caught slot becomes selected **only if** the catcher's current selection is the hands slot or mail. A tool or weapon is never deselected mid-raid.
  - Selection never changes while Holding.
  - If the units went to the inventory or backpack (no hotbar room), selection does not change and the feed reads "Caught ×20 — bag".
- **One press catches one flight.** The arm clears on the first CAUGHT or FUMBLE, so a second flight arriving in the same window treats the player as an unarmed teammate.
- **Spawn protection.** Catching does not end it, and neither does throwing mail. Only a damaging action does, including a throw that resolves HIT_ENEMY (Player Character P17).
- **Prompt.** The catcher's client flips the Interact prompt to **"Catch ×N from Jules"**, ahead of the press (Input R4.3). It does so when the deterministic prediction (F5) says a non-own flight will enter its catch sphere within `catchPredictMs` = 500 **PROPOSED**. The prediction runs on the host-tick estimate, not the render-delayed clock. The flip has hysteresis (on after 3 ticks ≈ 100 ms, off after 5 ticks ≈ 167 ms), so the prompt leads sphere entry by ≈ 400 ms (F5). Static Chalk carets on the parcel, plus a static edge chevron when the flight comes from behind or off-screen, appear with the prompt. They are also the non-audio bonk warning, because a bonk is a catch nobody took.
- **Catching stays on Interact** (answers OQ-IC-3(d)) **[D]**. A dedicated button would cost a scarce gamepad button, and the prompt has already named the verb before the press.

**T8 — Bonk.**

- **Who gets bonked.** A teammate in a flight's path who is not catching. The host tests their capsule at their **newest** reported position, not their history. The asymmetry is about stakes: a catch is intent and deserves latency tolerance, while a bonk costs only time. On a remote screen a bonk can read up to ≈ 0.7 m off; this is accepted (Open Questions 16).
- **Effect** is T6's BONK row.
- **Spawn protection.** A bonk does not end the victim's spawn protection, and it is not a damaging action for the thrower either.
- **Swimmers.** A bonk on a swimming teammate resolves as WATER at their position.
- **Presentation.** The hat-lift and sound have a cosmetic per-victim cooldown, `bonkCosmeticCooldownMs` = 1000 **PROPOSED**. Item drops are never suppressed.
- **Bonks are telemetry only (CD ruling).** They are counted for fantasy test 2 and are never shown in Payday, statistics, achievements or perks.

**T9 — Thrown damage.**

- **How much.** One hit per flight. Damage is `damagePerPoint × classPts` of the entry's class (F6: light 5 · medium 15 · heavy 40; spec 13 §4.1). It comes from `classPts` per entry, **never from `P`** or from the stack count (answers Grid Inventory OQ6). Cannon Arm does not change it.
- **Who it hurts.** Only goons. Health & Damage owns HP, resistances, death and the damage event (provisional contract).

**T10 — Cannon Arm and stats.**

- **Range.** Cannon Arm (card: "throws go ≈ 30 % farther", the flat-aim outcome via `{fx:throw.rangeRatio}`; spec 10/13's "throw range +50 %" is superseded, owner 2026-10-16) is a personal StatSheet key `ThrowRangePermille` (1500). It scales launch speed by `isqrt(ThrowRangePermille × 1000)` ‰ = 1224 ‰. Range grows ×1.24–1.47 depending on aim once hand height is counted (×1.30 at flat aim, ×1.47 at the peak; F3), and the arc's shape is unchanged.
  - **Rejected:** lowering gravity, which floats the arc and changes catch timing.
  - **Perk text (owner 2026-10-16).** The card states the computed outcome, ≈ ×1.30 = "30 % farther" at flat aim; throwing is not retuned to reach +50 % (Open Questions 3, closed).
- **Pay.** The pay half, ×1.1 on a thrown Accept, is Mail & Destinations' `ThrownCorrectPayMult`, snapshotted at launch (T4).

**T11 — Solids, roofs and run boundaries.**

- **Solids.** Constructs, walls, houses, containers and vehicles are solids (T6). Vehicles are driver-authoritative, so a moving vehicle is tested at its driver's newest reported transform.
- **Phase boundaries.** A throw is judged by when it **started** (Mail R4 closing grace).
  - A flight released while OPEN resolves as OPEN if it lands ≤ `closeGraceTicks` (90) after close. Since flights last ≤ 90 ticks, every OPEN-released flight is judged OPEN.
  - A flight released while CLOSED bounces.
  - Flights are never cancelled at phase changes.
- **Run end.** At run end (Victory or Run Over), every flight still in the air after that tick's Throwing step resolves at once as TIMEOUT, so R19 holds through teardown. A landing on the tick that triggers Victory resolves normally first. A flush never pays, even for an OPEN-released flight when Delivery's close and run end coincide.

**T12 — Degenerate strategies (rulings).**

- **Throw-ahead as transport, or to dodge the weight penalty: allowed.** Players are never propelled. With no self-catch, every leg needs a walk to the WorldItem, and moving items fast is the verb's purpose.
- **Sea "return" and "parking" at a closed box: no change.**
  - Water mail comes back after 10 s at the **back** of the queue with its deadline running (Grid Inventory R12).
  - A bounce is a drop with a 300 s timer; it extends no deadline.
- **Relays outpacing belts: intended (Pillar 3, ratified reading).** Relays need presence, aim, sorted stacks and open mailboxes. A belt still wins unattended and over distance.
  - **Levers if telemetry ever demands:** `throwMinAim` and `throwCooldownTicks`.
  - **Never:** range or penalties.
- **Arc colour as an address oracle: allowed.** It reveals only your own selected entry, exactly as Manual Delivery's ✓/✗ prompt does.
- **Throwing junk at teammates: a nuisance with no payload.** A bonk changes nothing but time, catches are opt-in, and you can only throw your own hotbar.
- **Saving a teammate's red throw by catching it: intended.** It is the only thing that can soften a bad throw, and it is a friend, not the game (Player Fantasy).
- **Sustained thrown DPS: bounded by recovery.** Each throw leaves the item on the ground, and light items do 5 (versus the Mail Bat's 25 per swing).

**T13 — Relay hops (presentation hook).**

- **Recording.** Each player records `lastCatch{mailIds or item units caught, hop, tick}`. It is keyed on the caught **units**, not the entryId, because a catch that merges into an existing hotbar stack keeps the old entryId.
- **Hop count.** A flight launched within `relayWindowTicks` = 150 (5 s) **PROPOSED** whose entry contains any of those units gets `hop = min(255, lastCatch.hop + 1)`; any other flight gets `hop = 0`.
- **Use.** `hop` drives only the FORWARDED stamp and the chime pitch (cap 5) and relay telemetry. It has no gameplay effect.

**T14 — Telemetry (instruments for the fantasy tests and OQ21).**

- **Per launch:** `units`, kind, class, `hop`, `originClamped`, and `redAgeAtReleaseMs` (how long the arc had been red at release; 0 if it was not red).
- **Per terminal:** outcome, distance from origin, flight ticks.
- **Per catch:** thrower and catcher ids, `armedBeforeLaunch`.
- **Per armed press with no catch in its window:** a cause, one of `race_lost`, `out_of_reach`, `ineligible` (swim, aim, mount), `no_flight`. This instruments feel test 3.
- **Bonks and fumbles:** counted per session.

These go to the session stats file, extending the prototype's `playtest_stats.txt`. They are never shown to players.

**T15 — What this system does not do.**

- It judges no mail (Mail & Destinations).
- It places no entries and creates no WorldItems except through Grid Inventory's API (Overview).
- It owns no HP (Health & Damage) and moves no lane items (Belt Logistics).
- It renders no door reactions (Mail & Destinations).
- It never pushes, stuns or carries a player.

### States and Transitions

**Thrower aim (owning client; this is Input & Controls' `aiming` flag).**

| State | Event | Guard | Next |
|---|---|---|---|
| Idle | Throw pressed | T1 eligible, throwable entry selected, cooldown elapsed | Aiming (entry latched, Input G1; arc fades in 0.08 s) |
| Idle | Throw pressed | hands slot / empty / swimming / Holding | Idle + T1 reason (no arc) |
| Aiming | release (or Toggle second press) | `aimHeld ≥ throwMinAim` | Released |
| Aiming | release | `aimHeld < throwMinAim` | Cancelled ("Hold to aim") |
| Aiming | Back, or any Input G2 condition (swim, mount, knockdown, panel, focus loss, device lost, entry gone or stale) | — | Cancelled ("Aim cancelled") |
| Released | — | — | Idle; `ThrowRelease` sent, ghost started |
| Cancelled | — | — | Idle; nothing sent |

**Flight (host; mirrored by every peer from events).**

| State | Entered by | Exits to |
|---|---|---|
| LAUNCHED | valid `ThrowRelease` at host tick T (Grid Inventory PLACED → FLIGHT; `ThrowLaunched`) | IN_FLIGHT at T+1 |
| IN_FLIGHT | ages 1…90, one sweep per tick | exactly one terminal (T6), or TIMEOUT at age 90 |
| CAUGHT{unitsIn, unitsFumbled} | armed catcher wins and ≥ 1 unit fits | — Grid Inventory PLACED (hotbar-first); any fumbled remainder WORLD at the catcher's feet |
| FUMBLE | armed catcher wins and no unit fits | — WORLD at the catcher's feet |
| MAILBOX_ACCEPT / MAILBOX_MISDELIVER | OPEN destination volume | — CONSUMED (+ WORLD remainder at the wallet floor) |
| MAILBOX_REJECT | closed, locked or non-mail | — WORLD at the F8 drop point |
| HIT_ENEMY | goon contact | — WORLD at the goon's ground point (damage applied) |
| BONK | teammate contact | — WORLD at their feet |
| BELT | lane contact | — BELT (remainder WORLD) |
| GROUND | terrain or solid | — WORLD |
| WATER | non-ground tile | — WORLD (floating 10 s) |
| TIMEOUT | age 90 without contact, or run end | → GROUND or WATER below |

**Catch arm (host, per player).**

| State | Event | Next |
|---|---|---|
| Unarmed | `CatchArm{down}` applied, and the player is eligible (T7) | Armed (`armedUntil = downTick + 15`) |
| Armed | `CatchArm{up}` applied | Armed until `max(upTick, downTick + 15)`, then Unarmed |
| Armed | `downTick + 90` reached (hold or Toggle cap) | Unarmed |
| Armed | catch resolves (CAUGHT / FUMBLE) | Unarmed |
| Armed | death, mount, swim, panel open, aim start, disconnect, rejoin | Unarmed |

### Interactions with Other Systems

| System | Direction | Interface / data | Owner of the interface |
|---|---|---|---|
| Grid Inventory & Containers | both | `throw {entryId}` op (+ origin, dir, seq) detaches the whole selected hotbar entry → FLIGHT; `TryCatch` (mail hotbar-first, `TargetFirst`; fumble WorldItem at the catcher's feet); WorldItem creation (`origin = Thrown`, timer from landing); water float / return; R19 one end state per flight | Grid Inventory owns placement and WorldItems; this system owns FLIGHT |
| Mail & Destinations | downstream caller | client `Evaluate` for the arc colour and label; `catchVolume` per destination; `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)`; value hint (R17) for the label's money; `DeliveryOutcome` ends the mailbox teeter | Mail owns acceptance, money and complaint; this system owns the landing test and the Reject bounce |
| Player Character & Movement | upstream | aim (camera yaw/pitch), body yaw locked to the camera while aiming (P3), aim camera profile (values here, Game Feel), reported-position history (F5), capsule, life state, swimming, mounted; spawn protection ends only on a damaging (HIT_ENEMY) throw (P17); Catch is a Channel verb resolved at press (P10, P11) | Player Character owns the body and the interaction framework |
| Input & Controls | upstream | Throw aim-release, `throwMinAim`, G1 latch, G2 cancels, G7 Toggle, G9 one-commit rule; Interact resolution order (catch → target verb → catch-armed), with T7's D3 gate on preemption (hand-back); Catch Toggle latch (`catchArmSeconds` 3.0 s, G7, also the hold cap) | Input owns grammar and thresholds; this system owns the catch window and the preview |
| Health & Damage *(provisional)* | downstream | `ApplyDamage(amount, source = Thrown, actorId)` on goons, queued in Throwing's step and applied in `Combat.Resolve`. Throwing reads life state at the start of its step, and Health & Damage must accept a Dead or Frozen `actorId` | Health & Damage |
| Belt Logistics *(provisional)* | downstream | lane top-face geometry for the sweep; `TryInsert(lane, point, unit)` per unit, partial accept; `LaneInsert` carries `flightId` | Belt Logistics |
| Vehicles & NPC Drivers *(provisional)* | upstream | vehicle bodies and roofs as solids at the driver's newest transform; no cargo acceptance | Vehicles |
| Enemy AI & Waves | upstream | goon capsules (radius, height, alive) at the current host tick | Enemy AI & Waves |
| Content & Data Definitions | upstream | throw speeds by class, `g`, loft, radii, cooldown, windows, damage per point (data); ItemDef `weightClass`; StatSheet keys `ThrowRangePermille` (personal, new) and `ThrownCorrectPayMult` (Mail's) | Content & Data loads; this system owns the values |
| Perks & Perk Draft | upstream (StatSheet) | Cannon Arm pushes `ThrowRangePermille 1500` and `ThrownCorrectPayMult 1100` | Perks push modifiers only |
| Simulation Core | upstream | 30 Hz tick, step order (phase 0 `Inputs.Apply` → phase 4 `Throwing.Step` → … → phase 10 `Combat.Resolve`), `PlayerSlot`, `flightId` (u16), event bus, op descriptor table | Simulation Core |
| Networking & Session | both | `ThrowRelease`, `CatchArm` (requests); `ThrowLaunched`, `FlightResolved` (events); `flightId` on domain events (Network section) | Networking owns transport |
| HUD & Notifications | downstream | arc label, prompt states (Catch, Catching…, reasons), result lines (HUD routes), hotbar slot pulse; the landing marker's screen rect each aiming frame (an H11 protected rect; HUD owns layer order) | HUD renders; strings here |
| Presentation & World Dressing, Audio | downstream | flight visuals, trail, beats and sounds from `ThrowLaunched` / `FlightResolved` / domain events | presentation consumers |
| Run Structure | upstream | run end (flush flights, T11); phase via Mail's destination state | Run Structure |

## Formulas

*Specialist: systems-designer. All arithmetic is integer: mm, mm/s, mm/s², ticks at 30 Hz, permille, cents, with int64 intermediates. `RH(x/d)` is round-half-up division, and for a negative `x` it is `floor((2x + d) / (2d))` with floor division, never truncation, so every peer rounds a negative velocity identically. `CeilDiv(a, b)` and `FloorDiv(a, b)` are defined for b > 0 (negate both numerator and denominator first when b < 0); C#'s truncating `/` is never used on a signed numerator. `isqrt` is the integer square root. The Sim, the preview and every peer run these exact functions. Variables whose Source is `data file` are tuning knobs (Tuning Knobs).*

### F1 — Launch

The launch formula is defined as:

```
n        = isqrt(dx² + dy² + dz²)                       dir = (dx, dy, dz), int16 ×3, scale 16384 (client-quantised)
speedPm  = isqrt(ThrowRangePermille × 1000)             1000 → 1000; Cannon Arm 1500 → 1224
v        = RH(baseSpeed[class] × speedPm / 1000)
(vx, vy, vz) = (RH(v × dx / n), RH(v × dy / n), RH(v × dz / n))
h        = isqrt(dx² + dz²)                             (if h < 2048: use the body yaw for the horizontal basis)
origin.x = feet.x + RH(fwdOffMm × dx / h) + RH(side × sideOffMm × (−dz) / h)
origin.y = feet.y + handHeightMm
origin.z = feet.z + RH(fwdOffMm × dz / h) + RH(side × sideOffMm × dx / h)
```

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| dir | int16 ×3 | each ±16384 | calculated (client) | camera forward with pitch clamped −60°…+70°, plus `loftDeg`, quantised once; the preview uses the same integers |
| loftDeg | int ° | 0–8, default 6 | data file | added elevation (prototype value) |
| baseSpeed[class] | int mm/s | light 14000 / medium 10000 / heavy 6000 | data file | spec 13 §4.1 / spec 11 §8; a stack uses its kind's class, non-mail its ItemDef class (default light) |
| ThrowRangePermille | int ‰ | 1000–1500 | StatSheet (personal) | Cannon Arm = 1500 |
| handHeightMm, fwdOffMm, sideOffMm | int mm | 1200, 300, 250 | data file | launch point above the feet, ahead of them (≥ capsule radius, so it starts outside the thrower), and to the dominant side |
| side | int | ±1 | player profile | dominant hand (+1 = right) |
| feet | int32 ×3 mm | world | host state | the claimed origin's feet if Player Character F5 corroborates them, else the newest corroborated position (T4) |

**Host bounds:** `16000 ≤ n ≤ 16768` and elevation `−876 ≤ 1000·dy/n ≤ 980` (≈ −61°…+78.5°), else `bad_direction`. Before launch, the segment from the chest (feet + 1000) to the origin is swept against solids **and the heightfield**; if it is blocked, the flight launches from the chest point, so a throw never starts inside a wall.

**Output Range:** v 6000–17136 mm/s; the horizontal origin offset is ≤ 391 mm.
**Example:** light letter, aim pitch +6° (elevation 12°), facing −Z, feet (10000, 0, 20000):
- dir = (0, 3406, −16026), n = 16383, v = 14000;
- vel = (0, 2911, −13695);
- origin = (10250, 1200, 19700).

With Cannon Arm, v = RH(14000 × 1224 / 1000) = 17136.

### F2 — Ballistic position

The flight position formula is defined as:

```
px(k) = ox + RH(vx × k / 30)
pz(k) = oz + RH(vz × k / 30)
py(k) = oy + RH(vy × k / 30) − RH(g × k² / 1800)          k = tick − launchTick, 0 … maxFlightTicks
segment(k) = p(k−1) → p(k)                                 swept for k ≥ 1 only (age 0 does not sweep)
```

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| k | int ticks | 0–90 | calculated | flight age from the shared tick counter |
| g | int mm/s² | 9800 | data file | gravity for thrown items (≠ the carrier's 24 000 jump gravity) |
| maxFlightTicks | int ticks | 90 | data file | 3 s, spec 13 §4.1; at expiry → TIMEOUT |
| o, v | int32 | F1 | FlightRecord | launch origin and velocity |

- **Closed form, no accumulation.** A late event, a rejoin catch-up or a preview frame is one O(1) evaluation, and every peer gets identical integers.
- **The chord is close enough.** It deviates from the true parabola by at most g / (8 × 30²) ≈ 1.4 mm per tick, so each tick is a straight segment.

**Output Range:**
- The largest drop by k = 90 is 44 100 mm.
- The largest per-tick step is ≈ 1.1 m (≤ 571 mm horizontal, ≤ 980 mm vertical near expiry).
- All positions fit int32.

**Example:** the F1 flight at k = 20 is at (10250, 963, 10570), 9.13 m out and 0.96 m up. It enters a mailbox catch volume (0.6–1.4 m above the base) at about 10 m, which is the prototype's street scale.

**Gravity 9800 over 24000 (recommended).** At 24 000 the light peak range is ≈ 8 m and flights last under 1 s, so 9 m mailboxes are out of reach and a 500 ms catch prediction covers half the flight. At 9800, light peaks at 21.2 m (the art bible's "~20 m"), flights last 0.6–2.8 s, and catches have hang time.

### F3 — Range, flight time and the timeout (designer table; not Sim code)

The range formula is defined as:

`R = vh × t_land` ; `t_land = (vy + √(vy² + 2·g·handHeight)) / g` ; `apex = handHeight + vy² / (2g)` (landing at feet level)

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| vh, vy | float m/s | F1 | calculated | horizontal and vertical launch speed |
| handHeight | float m | 1.2 | data file | launch height |
| t_land | float s | 0.5–3.0 | calculated | flight time to feet level (×30 = ticks) |

**Output Range:** range in metres / flight ticks, by aim pitch (elevation = aim + 6°):

| Class | Aim 0° | Aim 15° | Aim 30° | Aim 45° | Peak |
|---|---|---|---|---|---|
| Light | 9.3 / 20 | 16.0 / 37 | 20.6 / 54 | 20.5 / 70 | 21.2 m |
| Light + Cannon Arm | 12.1 / 21 | 22.8 / 43 | 30.1 / 65 | 30.3 / 84 | 31.1 m |
| Medium | 6.1 / 18 | 9.2 / 29 | 11.1 / 41 | 10.9 / 52 | 11.4 m |
| Medium + Cannon Arm | 7.8 / 19 | 12.7 / 33 | 16.0 / 49 | 15.9 / 62 | 16.4 m |
| Heavy | 3.4 / 17 | 4.3 / 23 | 4.7 / 29 | 4.4 / 35 | 4.7 m |
| Heavy + Cannon Arm | 4.2 / 17 | 5.7 / 25 | 6.6 / 33 | 6.2 / 40 | 6.6 m |

**What the table means.**
- **A mailbox about 9 m away needs:**
  - light at aim ≈ +6°;
  - medium at ≈ +23°, near its 11.4 m ceiling (a skill throw);
  - heavy cannot reach it.
- **Heavy is a short toss by design.** It saves ≈ 2 m of walking beyond the 2.5 m interact reach, clears a fence, passes to a teammate and does 40 damage. A longer heavy throw would undercut weight (Grid Inventory D1).
- **At default knobs the 90-tick timeout binds only Cannon-Arm light lobs** above aim ≈ +50°. It is a free joke, not a constraint. At `g` 8000, light speed 20 000 or `maxFlightTicks` 60 it binds normal lobs too, so re-derive this table after any such change.

**Example:** light at aim 30° → 20.6 m in 54 ticks (1.8 s).

### F4 — Swept contact per tick

The contact formula is defined as, for the segment `P0 → P1` of tick k, `D = P1 − P0`:

```
every primitive yields an interval [tEnter, tExit] in ‰ of the segment;
   hit ⇔ tEnter ≤ tExit ∧ tExit ≥ 0 ∧ tEnter ≤ 1000 ;  f = max(tEnter, 0)
slab interval, per axis a (AABB — catch volume, belt top face, construct, vehicle body):
   D_a ≠ 0: lo' = (lo_a − P0_a) × 1000, hi' = (hi_a − P0_a) × 1000 (swap if D_a < 0, then use |D_a|):
            enter_a = CeilDiv(lo', |D_a|) ; exit_a = FloorDiv(hi', |D_a|)
   D_a = 0: (−∞, +∞) if lo_a ≤ P0_a ≤ hi_a, else no hit
   tEnter = max_a enter_a ; tExit = min_a exit_a
circle (cylinder in xz) / sphere (3D), R = P0 − centre:
   A = |D|² ; B = D·R ; C = |R|² − r² ; disc = B² − A·C ; no hit if disc < 0 or A = 0 ∧ C > 0
   s = isqrt(disc) ; enter = CeilDiv(1000·(−B − s), A) ; exit = FloorDiv(1000·(−B + s), A)   (A = 0 ∧ C ≤ 0: (−∞, +∞))
cylinder (goon r + enemyHitSlackMm, EnemyDef height; teammate bonk r 300, h 1400): circle interval ∩ y-slab interval
sphere (armed catcher, r = catchRadiusMm, centre = reported feet + 1000 mm): the 3D interval alone
heightfield and water plane (H = World Generation's heightfield, integer bilinear per cell, RH; water = sea level 0 over water tiles):
   n = max(1, CeilDiv(|Dxz|, min(cellMm / 2, 250)), CeilDiv(|Dy|, 250)) ; Q_i = P0 + RH(D × i / n), i = 0…n
   gap_i = Q_i.y − H(Q_i) ; first i ≥ 1 with gap_i ≤ 0 (and gap_{i−1} > 0):
   f = CeilDiv(1000 × ((i − 1) × (gap_{i−1} − gap_i) + gap_{i−1}), n × (gap_{i−1} − gap_i))
contact point = P0 + RH(D × f / 1000)
winner = min by (f, rank, dist² from contact to receiver centre, hash(flightId, receiverId))
```

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| f | int ‰ | 0–1000 | calculated | contact fraction within the tick |
| rank | int | 0–7 | data file | armed catcher 0 · mailbox 1 · goon 2 · teammate 3 · belt 4 · solid 5 · ground 6 · water 7 (T6) |
| r, h | int mm | teammate 300 × 1400; goon per EnemyDef + 300 | registry / data file | the capsule is approximated by a flat-capped cylinder (cap error ≤ r) |
| catch volume | AABB mm | 1000 × 800 × 1000, centred 1000 above the base | registry (Mail R3) | axis-aligned to multiples of 90° of yaw |

- **Starting inside a volume** gives contact f = 0, so a point-blank throw into a mailbox is legal.
- **A receiver behind the segment** (both roots negative) gives tExit < 0: no hit. A cylinder entered above its top takes the later of the circle and y-slab entries.
- **Magnitudes:** every quadratic runs only after the broad-phase reject, which bounds |R| ≤ 65 536 mm, so A·C and B² stay below 10¹⁶, well inside int64. Without the reject, map-scale R would overflow.
- **Broad phase:** a static 4 m grid for catch volumes, belts, constructs and the heightfield. Dynamic receivers (goons, teammates, catchers) are tested by bounding-sphere reject against the segment.

**Output Range:** a contact (receiver, f, point), or none.
**Cost (estimate, to be measured):**
- A segment test costs ≈ 40–100 ns. A flight sees ≈ 20 candidates per tick, so 16 concurrent flights ≈ 50 µs per tick.
- Armed catchers add ≤ 8 catchers × 16 flights × 10 history samples × 60 ns ≈ 77 µs.
- Together ≈ 0.13 ms, inside Simulation Core's phase-4 slice (0.15 ms p99, 0.5 ms max; registry `tickBudgetSplit`), with no allocation.

**Example:**
- The F1 flight at tick 21 runs from (10250, 963, 10570) to (10250, 837, 10114).
- A mailbox centred at (10250, ·, 10000) spans z 9500–10500, and its catch volume spans y 600–1400.
- tEnter = ceil(−70 × 1000 / −456) = 154 ‰, so contact is at (10250, 944, 10500): inside, so it is a mailbox landing.

### F5 — Catch: prompt prediction and host validation

The catch formulas are defined as:

```
prompt (catcher's client, on hostTickEst): run F4 forward for P = catchPredictMs × 30 / 1000 = 15 ticks on each non-own
   flight, with this client as an armed catcher at its current position; show "Catch" iff the winning contact is this
   catcher's sphere (hysteresis: on after 3 ticks, off after 5 ticks) → the prompt leads entry by (P − 3) ticks ≈ 400 ms
armed (host):  armedUntil = min(max(upApplyTick, downApplyTick + catchTapTicks), downApplyTick + 90)    catchTapTicks = 15
candidate:     armed ∧ eligible (T7) ∧ ¬thrower
contact:       min f over the catcher's reported positions s received in the last catchHistoryTicks (9 ticks),
               skipping any s with |s − newest| > catchHistoryMaxOffsetMm (3D)
```

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| catchRadiusMm | int mm | 1500 | data file | spec 13 §4.1 |
| catchPredictMs | int ms | 500 | data file | prompt look-ahead (OQ-IC-3(a)) |
| catchTapMs | int ms | 500 | data file | a press arms at least this long; replaces the proposed `catchGraceMs` (one knob, not two) |
| catchHistoryTicks | int ticks | 9 | Player Character F5 | 300 ms of reported positions |
| catchHistoryMaxOffsetMm | int mm | 1500 (1000–2000) | data file (default = Player Character F5 `claimToleranceMm`) | plausibility bound on stale samples; effective reach = `catchRadiusMm` + this (validator 18) |

- **A full panel does not exclude a catcher.** It is resolved at the host as FUMBLE (CD D-C).
- **No rewind.** The host never rewinds a resolved flight; a late press simply misses.

**Output Range:** boolean prompt; boolean contact plus f.

**Example: a 200 ms tap at 120 ms RTT** (times relative to the host's sphere entry at 0 ms):

| Time | Event |
|---|---|
| −400 ms | the prompt appears: 500 ms prediction − 100 ms on-hysteresis (the flight became known ≥ 560 ms before entry) |
| −200 ms | the press, after 200 ms of reaction |
| −140 ms | the press applies (+60 ms one-way); the host arms until +360 ms (apply + 500 ms) |
| +60 ms | the release applies; the arm still holds |
| 0 ms | the sphere entry: **CAUGHT**; the press had 140 ms to spare |

The latest press that still catches is sent at ≈ −60 ms, so the reaction budget is ≈ 400 ms − one-way: ≈ 340 ms at 120 ms RTT and ≈ 275 ms at 250 ms RTT. That is enough for a reaction with a decision attached, but thin; if TC-FE-4 shows `no_flight` (too late) dominating, raise `catchPredictMs` toward 700.

### F6 — Thrown damage

The thrown damage formula is defined as:

`damage = damagePerPoint × weightPoints[class(entry)]` ; `hitsToKill = ceil(HP / damage)`

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| damagePerPoint | int | 5 | data file | spec 13 §4.1 |
| weightPoints | int | 1 / 3 / 8 | registry (Grid Inventory) | per **entry** by class, never `P` and never the count (OQ6) |
| HP | int | 30–400 (Mega ×2.5) | EnemyDef (Enemy AI / Health & Damage) | goon hit points |

**Output Range:** damage 5 / 15 / 40 at the default `damagePerPoint` (3–64 across its 3–8 knob range).

Hits to kill (Mega in brackets; art-bible names, spec 05 HPs):

| Damage | Runner 60 (150) | Flyer Tosser 40 (100) | Pallet Jack 400 (1000) | Rush Delivery 30 (75) |
|---|---|---|---|---|
| 5 (light) | 12 (30) | 8 (20) | 80 (200) | 6 (15) |
| 15 (medium) | 4 (10) | 3 (7) | 27 (67) | 2 (5) |
| 40 (heavy) | 2 (4) | 1 (3) | 10 (25) | 1 (2) |

**Example:** a Medium parcel at a Flyer Tosser does 15, so it takes 3 hits.

*Why count does not scale damage:* a 20-letter stack doing 100 would make letters the best weapon in the game, and it would break the per-entry weight convention.

### F7 — Preview label money

The label money formula is defined as:

```
Y (Accept)  = Σ_runs count × RH(unitSpawnValue × T‰ × MailValueMult‰[kind] × thrownPm / 10^9)
X (Misdeliver) = Σ penalty(u) over the longest oldest-first prefix of units with Σ penalty ≤ wallet − walletFloor
penalty(u)  = max(1, RH(unitSpawnValue × 500 × MisdeliveryPenaltyMult‰ / 10^6))          (Mail F7)
bounce      = N − units in that prefix
```

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| runs | list | ≤ 5 | replicated value hint (Mail R17) | `(deadlineShift, count, unitSpawnValue)` in ascending mailId order |
| T‰ | int ‰ | 0 / 500 / 1000 | calculated (Mail F5) | timeliness at the shift of the release tick (the host judges by `releaseTick`, T4). Dead letters (T = 0) show +0¢ on Accept but the full penalty on Misdeliver, deliberately |
| thrownPm | int ‰ | 1000–1100 | own StatSheet | Cannon Arm 1100 |
| wallet | int ¢ | ≥ −500 | replicated (5 Hz) | last-known team balance; the host's apply tick is authoritative |
| walletFloor | int ¢ | −500 | registry (Economy) | |

**Output Range:** int32 cents.
- The Misdeliver figure never exceeds the wallet headroom (wallet + 500).
- The label shows "to floor · B returned" when the floor caps it.
- It is a preview: the Payday ledger and `DeliveryOutcome` are authoritative.

**Example:** 20 letters, spawn value 12 ¢ (District 2, shift 3), on time:
- Wrong house: penalty 6 ¢ each → "−120¢".
- At wallet −440, the headroom is 60 → "−60¢ to floor · 10 returned".
- Right house: "+240¢", or "+260¢" with Cannon Arm (RH(12 × 1100/1000) = 13 per unit).

### F8 — Reject / remainder drop point

The drop point formula is defined as:

```
u    = (vx, vz) / |vxz| in ‰ (RH)                     |vxz| = isqrt(vx² + vz²); if |vxz| < 1000, u = −n_front (n_front = the mailbox's outward front normal)
D    = C − RH(dropPushbackMm × u / 1000)                then step 100 mm along −u until max(|D.x − V.x|, |D.z − V.z|) ≥ 600 (V = volume centre)
drop = snapWalkable(clampInsideMap(D, 1000), radius 1000)  else  the mailbox base front point
```

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| C | int32 ×3 mm | world | F4 | volume entry point |
| dropPushbackMm | int mm | 500 | data file | back along the flight |
| v | int32 mm/s | F1 | FlightRecord | flight direction |

**Applies to:** MAILBOX_REJECT, and the wallet-floor remainder of MAILBOX_MISDELIVER.

**Output Range:** always a walkable point outside the mailbox footprint, so a re-grab is a normal Pick up, never a second landing.

**Example:** C = (10250, 944, 10500), moving −Z → D = (10250, 11000) → the drop is on the ground 1 m in front of the mailbox centre.

### ContentValidator rules (this GDD)

1. `0 < heavy < medium < light` speeds, each within 3000–30000 mm/s. The per-class knob ranges overlap, so this ordering binds within them.
2. `ThrowRangePermille` data values within 1000–1500. At runtime the sum of sources is clamped, which is not a rejection.
3. `g > 0`.
4. `30 ≤ maxFlightTicks ≤ 150`.
5. The receiver rank table is a permutation of the 8 kinds.
6. Every catch volume is axis-aligned (yaw a multiple of 90°), has extents > 0, and has its bottom ≥ the mailbox base.
7. Every EnemyDef has a radius ≥ 100 and a height ≥ 500.
8. Every ItemDef has a `weightClass`, and `bulk` implies not hotbar-placeable.
9. `fwdOffMm ≥ capsuleRadiusMm`.
10. `0 < handHeightMm ≤ capsuleHeightMm`.
11. 70° + `loftDeg` ≤ 78°, so a legal maximum-pitch throw never fails the host's 78.5° bound.
12. `catchTapMs` and `catchPredictMs` are multiples of 100 ms (whole ticks at 30 Hz).
13. `catchPredictMs ≤ maxFlightTicks × 1000 / 30`.
14. `catchRadiusMm < interactRangeMm` (Player Character rule 6).
15. `damagePerPoint ≥ 1`.
16. `throwCooldownTicks ≥ 1` and `throwBurstTicks ≥ 1`.
17. Map extents ≤ 1 000 000 mm. F4's quadratics run only after the broad-phase reject (|R| ≤ 65 536 mm); slab terms and F7 products are computed in int64. Validator rule numbers are error codes `THROW-CV-nn`.
18. `catchRadiusMm + catchHistoryMaxOffsetMm ≤ 3700`, bounding effective catch reach.

## Edge Cases

*Specialist: systems-designer. Each case states the exact outcome. Cases already settled by a dependency GDD cite it rather than restate it.*

### Zero, max and out-of-range

- **If `dir` arrives with int16 extremes** (e.g. −32768, 32767, 32767): the host widens to int64 before squaring. `n` falls outside 16000–16768 → `bad_direction`, and nothing launches. Int32 squaring would wrap.
- **If a valid `dir` gives `h < 2048`**: this is unreachable, because the host's 78.5° elevation bound keeps `h ≥` ≈ 3180 (validator 11). The body-yaw branch is defence only. Throwing straight down (elevation −54° … −61°) is legal and lands ≈ 1–2 m ahead as GROUND.
- **If the chest → origin sweep is blocked** (a wall at arm's length, a ledge, the origin below sloped terrain): the flight launches from the chest point. The age-1 segment contacts the solid or terrain at f ≈ 0, and one WorldItem appears within ≈ 1 m of the feet. The cooldown still starts; there is no refund.
- **If the origin is inside a catch volume or a catcher's sphere** (point-blank): contact is f = 0 on the age-1 segment, so it resolves on tick T+1. A catch volume still needs a descending segment (T6); a point-blank upward throw passes out of the volume. A point-blank Misdeliver carries the full whole-stack risk the preview showed.
- **If the swept displacement is zero** (`A = 0` in F4): a point test is used (contact iff `C ≤ 0`), with no division. The elevation bound makes this unreachable (`vh ≥` ≈ 590 mm/s even at validator 1's slowest legal speed, 3000 mm/s).
- **If F2 / F4 / F7 magnitudes grow**:
  - F2 stays ≤ 3.2 × 10⁸ at the knob extremes (`g` 14 000, 150 ticks), which fits int32.
  - F4's quadratics run only after the broad-phase reject (|R| ≤ 65 536 mm); without it, map-scale R would overflow int64.
  - F4 slab terms `(lo − P0) × 1000` and F7's `value × 500 × mult × pm` (≈ 2.5 × 10⁹ for a Found Cursed unit) are int64.
  - Per-unit and per-label cents stay int32.
- **If `hop` would pass 255**: it saturates at 255 (uint8). The chime caps at +5 anyway.
- **If `flightId` wraps** (uint16): processing order is creation order and ids are never compared numerically. An id repeats only after 65 536 launches (≥ 22 min even at the 48/s host cap), far beyond any flight's 3 s life.
- **If the throwable entry is a single non-mail unit worth 0** (a standard-issue axe): it throws, damages a goon for 5 (light), and lands as a WorldItem. Its sell value is irrelevant.

### Simultaneous events (same tick)

- **If two receivers have equal `f`**: T6 rank decides.
  - Catcher beats mailbox, goon and teammate.
  - Goon beats teammate.
  - Teammate beats belt, solid and ground.
  - Equal rank goes to the smaller distance to centre, then the lower `hash(flightId, receiverId)`. Two touching catch volumes resolve the same way, and only the first qualifying volume counts.
  - An armed catcher standing between you and a goon shields the goon. An unarmed teammate in front of a goon is bonked.
- **If two flights reach one goon in one tick**: they resolve in creation order, and "alive and not dying" is read live. The first lethal hit applies. The second flight passes through the dying goon to its next receiver. Kill credit goes to the first thrower.
- **If a goon dies from another source before Throwing's step**: it is not a receiver. If it dies later in the tick, after the flight's contact, the flight stays HIT_ENEMY, the item drops at its feet, and the overkill is a no-op in Health & Damage.
- **If two flights reach one catcher in one tick**: the first, in creation order, is caught. The arm then clears (T7), so the second treats them as a teammate and bonks them. One press catches one flight.
- **If eight armed catchers are in range of one flight**: the earliest `f` wins, then the nearer one at contact, then the lower `hash(flightId, playerId)`. The losers stay armed for their window with no state change.
- **If two thrown Misdelivers hit one wallet in one tick**: they resolve in creation order. The second may stop at `wallet_floor` having consumed 0 units, and its whole entry becomes one WorldItem at the F8 point.
- **If a hand op, a landing and a feeder hit one mailbox in one tick**: hand → landing → feeder → NPC, sharing one door bucket and one wallet (Mail R6).
- **If the thrower is knocked down on the release's apply tick**: `Inputs.Apply` precedes damage resolution, so a `ThrowRelease` applied that tick launches. The entry is in FLIGHT, not in the pile, and the flight continues. A release applied after the knockdown gets `dead`, and the entry goes into the bag (Player Character P15).
- **If the catcher is knocked down on the resolution tick**: life state is read at the start of Throwing's step.
  - Already Dead: no collider, so the flight passes through.
  - Dying later in the tick: CAUGHT inserts into the panel and the death spill takes it 1:1.
  - R19 holds either way.
- **If a same-tick hand op frees a hotbar slot**: it counts for the catch, because hand ops precede landings (T7).

### Rule collisions

- **If the entry changed between aim and host apply**:

  | Change | Result |
  |---|---|
  | merged or split (count ≠ `units`) | `stale_entry` |
  | sorted into a new entry (id retired) | `stale_entry` |
  | selection moved off it | `stale_entry` |
  | the thrower died (bag) | `dead` |

  In every case the slot keeps its contents and the ghost fades. Client side, a Toggle aim whose latched entry changes count cancels with "Aim cancelled" (Input G2). A thrower never throws a number they did not see.
- **If the wallet is exactly −500 when a Misdeliver lands** (or the first unit's penalty would cross the floor): 0 units are consumed.
  - The whole entry becomes one WorldItem at the F8 point, and `DeliveryOutcome{units: 0, stoppedAtFloor}` is broadcast at most once per door and thrower per 30 ticks (Mail R8, R16).
  - There is no complaint and no `CursedMisdelivered`.
  - The preview label read "refused at floor · all returned" (UI Requirements).
  - Stopping at the first failing unit is deliberate: cheaper later units never skip ahead.
- **If a Cursed 20-stack is thrown into the wrong box**: one op consumes every unit until the floor and emits one `CursedMisdelivered{units}`. That event is suppressed if that door's `lastCursedTick` is under 30 s old; the money and the bucket-limited complaint still apply (Mail R14). A stack never mixes Cursed and plain units, because Cursed is in the StackKey.
- **If Found (Lost Parcel) mail is thrown**: it became ordinary mail when it was picked up (Mail R15).
  - Accept pays its ×3 value.
  - Misdeliver costs 50 % of the ×3 spawn value, and F7 shows it.
  - A bounce or landing is `origin = Thrown` with the 300 s timer.
  - Water returns it to the Intake queue.
- **If the catcher's hotbar has only slot 0 free**: slot 0 is reserved, so the hotbar counts as full. `TargetFirst` fills the inventory or backpack: CAUGHT, "Caught ×20 — bag", selection unchanged. FUMBLE happens only when nothing fits anywhere.
- **If a 20-letter stack meets a matching hotbar stack with room for 15**:
  - the oldest 15 merge;
  - the newest 5 take the next free slot ≥ 1, else the inventory, else they fumble.
  - The terminal is `CAUGHT{unitsIn: 15 or 20, unitsFumbled: 5 or 0}`.
  - The thrower's personal toast reads "Jules caught 15 · 5 fumbled" (HUD H4).
- **If the catcher stands inside or behind a mailbox's catch volume**: the earliest `f` wins. A catcher behind the box is not reached if the flight enters the box first. A catcher in front intercepts. At equal `f`, the catcher wins by rank.
- **If an armed catcher starts aiming inside their armed window**: starting an aim clears the arm (T7). The flight treats them as a teammate and bonks them.
- **If the flight lands on a belt lane that is full, refuses the category, or was deconstructed in this tick's `Inputs.Apply`**: the lane is not a receiver, and the flight continues to whatever is below. A partial `TryInsert` puts the oldest units on the belt; the rest become one WorldItem at the contact point, not carried by the belt.
- **If the drop point is inside a solid footprint** (a house roof, a construct): the walkable search grows 1000 → 3000 → 6000 mm back along −v. If that fails, the item goes to the receiver's base (T6), never to the thrower, who may be dead, disconnected or far away.
- **If a throw is released in Prep, Payday or Draft**: it is allowed in World context. Every mailbox is CLOSED, so an entry into a box bounces (`closed`) with no penalty and a 300 s timer. Belts, catches, bonks and ground work normally.
- **If a district unlocks mid-flight**: this is impossible, because unlocks happen in Prep only. A Prep flight into a newly unlocked box still reads CLOSED; only the reason string changes.
- **If the thrower rides a vehicle roof at 14 m/s**: velocity is not inherited. The preview equals the world-frame flight, and the roof drives away from it.
- **If a flight reaches the map edge**: the map rectangle's sides are vertical solids (rank 5). The item drops 1 m inside the edge.
- **If a WATER landing is right at the shore**: it floats where it touched, with no snapping to land (T6). It returns to the Intake after 10 s.

### Lifecycle

- **If the thrower disconnects before their release is applied**: FIFO order decides. A release queued before the disconnect event launches. One after it gets `frozen`, and the entry stays in the frozen panel.
- **If the thrower disconnects or dies after launch**: the flight continues on its `FlightRecord` (`throwerId`, `thrownPm`, `actorId` unchanged). They are never a receiver of their own flight, even after a Second Wind respawn within 3 s.
- **If the catcher disconnects, dies, mounts, swims or rejoins**: the arm clears and the body is not a receiver. A rejoiner's position history is empty until their first report, so they are not catchable before it.
- **If a player rejoins while flights are in the air**: no flight state is sent (0 B). They receive the resulting WorldItem or consumption events only (Network).
- **If a flight crosses the end of Delivery**: Mail R4's closing grace applies: judged at release (T11).
- **If the run ends (Victory or Run Over) with flights in the air**: all resolve immediately as TIMEOUT, in creation order. Each drops to the ground or water below its current position, and a mailbox volume below does not count. There is no `DeliveryOutcome` and no pay.
- **If the solo host pauses**: the tick stops, and flights keep their age and resume from it. Pausing while aiming cancels the aim (Input); resuming never throws.
- **If the selected entry is taken by another player's op while aiming**: this can only happen to cursor or container entries, never the hotbar, because panel containers are never in another player's open set (Grid Inventory R10). The case is not reachable.

### Network and latency

- **If the host itself throws or catches (loopback)**: there is no ghost offset, and F5 corroboration trivially passes. The host's catch uses the same 300 ms history rule, so its reach equals a remote player's.
- **If a hitch bursts releases from one player into one tick**: the leaky bucket (T1) accepts two back-to-back, and a third gets `throw_cooldown`. A rejected release does not move `TAT`.
- **If a replayed `seq` arrives after the entry went to FLIGHT**: `stale_entry`.
- **If `CatchArm{down}` arrives after the flight resolved**: arming is forward-only and the flight stays resolved. The arm may catch a different flight within its 15 ticks.
- **If a catcher moves fast** (a roof rider whose 300 ms history spans 4.2 m): only samples within 1500 mm of the newest count, about the last 3 ticks.
- **If a hat pin is shown but the teammate moves away, or someone steps into the arc**: the pin is not a forecast. The host tests each teammate's newest reported position at each sweep, so a leaver is missed and a stepper is bonked.
- **If RTT is above 150 ms**: nothing changes in the rules. A late press simply misses (no rewind), and the ghost snaps more often (Game Feel).

### Degenerate strategies (beyond T12)

- **If a player floods WorldItems with thrown junk to reach `worldItemCap`**: the throw is never refused. Eviction takes old mail (≥ 10 s) first, then the oldest non-mail, which is the junk itself. One item per ≈ 0.42 s makes this slow.
- **If a teammate camps armed beside a mailbox to intercept throws**: allowed. Catching is opt-in per press and costs only time.
- **If a player throws mail at a floor-refused or closed mailbox to dump it**: it gains nothing. The mail is on the ground with a 300 s timer, and its deadline is unchanged.

## Dependencies

**Upstream (this system depends on them).**

| System | Hard / Soft | Status | What this GDD needs |
|---|---|---|---|
| Grid Inventory & Containers | Hard | In Review | `throw` op → FLIGHT (whole selected hotbar entry, ×N); `TryCatch` mail hotbar-first / fumble (CD D-C); WorldItem creation (`origin = Thrown`, timer from landing), water float and return, R19 conservation; new reply codes for the `throw` payload extension (`dead`, `frozen`, `swimming`, `unavailable`, `throw_cooldown`, `bad_direction`) — hand-back |
| Mail & Destinations | Hard | In Review | `Evaluate` (shared client code), `catchVolume`, `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)`, value hint (R17), `DeliveryOutcome` |
| Player Character & Movement | Hard | Designed | aim yaw/pitch, body-yaw lock while aiming, reported-position history (F5), capsule, life state, swimming, mounted, spawn protection ends only on a damaging throw (P17), Catch verb routing (P10, P11) |
| Input & Controls | Hard | Designed | Throw aim-release + `throwMinAim`, G1/G2/G7/G9, Interact resolution order with the D3 gate (hand-back), Catch Toggle latch (`catchArmSeconds`) |
| Simulation Core | Hard | Designed concurrently (`simulation-core.md`, which lists Throwing at phase 4) | 30 Hz tick, phase 0 `Inputs.Apply` → phase 4 `Throwing.Step` → phase 10 `Combat.Resolve`, `PlayerSlot`, `flightId`, event bus, op descriptor table |
| Content & Data Definitions | Hard | not yet written | throw data keys, ItemDef `weightClass`, EnemyDef radius/height, StatSheet `ThrowRangePermille` (new) |
| Networking & Session | Hard (M0) | not yet written | reliable-ordered channel 1, host tick estimate, transport peer → player map (Network section) |
| Health & Damage | Soft (M-1b stub → M3) | **not yet written — provisional** | `ApplyDamage(amount, source = Thrown, actorId)` for goons; no damage to players or constructs |
| Belt Logistics | Soft (M-1b) | **not yet written — provisional** | lane top-face geometry; `TryInsert(lane, point, unit)` per unit with partial accept; `LaneInsert` carrying `flightId` |
| Vehicles & NPC Drivers | Soft (M1) | **not yet written — provisional** | vehicle bodies and roofs as solids at the driver's newest transform; no cargo acceptance |
| Enemy AI & Waves | Soft (M-1b) | not yet written | goon capsules and alive state at the host tick |
| World Generation | Hard | not yet written | heightfield, water tiles, construct and house AABBs (the M-1a stub) |

**Downstream (they depend on this system).** HUD & Notifications (arc label, prompts, feed lines), Presentation & World Dressing and Audio (flight, trail, beats), Perks & Perk Draft (Cannon Arm reads `ThrowRangePermille`), Run Structure (run-end flush; Payday reads Mail's stats, not ours), Onboarding & Tips (first-throw tips), Accessibility (preview sound, patterns), Dev Tooling (flight debug draw).

**Provisional assumptions** (re-verify when those GDDs are written):
- **Health & Damage** applies thrown damage only to goons, emits the damage event carrying `flightId`, and treats Mega HP as ×2.5 of base.
- **Belt Logistics** exposes a per-unit `TryInsert` at an arbitrary lane point that may accept part of an entry, and its client visual sim can adopt an item at a point.
- **Vehicles** expose collision boxes at the driver's transform and never accept thrown items into cargo.
- **Enemy AI & Waves** gives every EnemyDef a radius and height.

If any of these is refused, the receiver row in T6 changes, not the flight model.

**Sources** (the Cross-References table was folded here at design-review):
- spec 13 §1, §3, §4.1, §4.3, §4.5: speeds 14 / 10 / 6, no bounce, 3 s, 1.5 m catch, 5 × weight points, Cannon Arm. This GDD **supersedes** §4.1's "puts it on their cursor": a catch goes to the hotbar (CD D-C).
- spec 05 §1.2, §2: friendly fire off; "5 × weight points" is read per entry, never `P` (OQ6).
- spec 06: superseded in part by spec 13 §3.
- spec 09 §3.2, §4; spec 10 (Cannon Arm).
- spec 10 §4.1 and spec 13 §4.1 Cannon Arm "throw range +50%": **superseded** (owner 2026-10-16). The card reads the computed outcome, ≈ 30 % farther (T10); spec files not edited.
- `prototypes/m1a-delivery-throwing-concept/REPORT.md`: 14 / 10 m/s, 6° loft, g 9.8, 3 s.
- Art bible §1, §4, §5, §7 (amendments in Open Questions 9). Registry entries whose `source` is this GDD.

**Bidirectionality.**
- Grid Inventory, Mail & Destinations, Player Character, Input & Controls, HUD & Notifications, Manual Delivery and Simulation Core already list Throwing & Catching. Their hand-backs were reconciled 2026-10-16 (header); open ones are in Open Questions 2.
- Health & Damage, Belt Logistics, Vehicles and Enemy AI must list this system when written.

**Sim / Presentation split.** This system lives mainly on the **Sim** side:

| Side | What lives there |
|---|---|
| **Sim** (plain C#, no Godot references) | `FlightRecord`, F1–F2 launch and ballistic, F4 sweep and receiver priority, the catch arm and race (F5), F6 damage, T13 relay hops. The **preview's flight query is the same Sim function**: a hard rule, so the preview and the host cannot diverge |
| **Godot-side adapter** | `ThrowRelease` / `CatchArm` serialisation and sequencing, host-tick estimation, the reported-position history it feeds to the Sim, `flightId` binding on events |
| **Presentation** | the aim camera, arc and label rendering, the launch ghost and its blend, remote flight playback, bundle meshes, trail, beats, haptics and sounds |

Presentation reads Sim state and events only and never writes the Sim.

## Tuning Knobs

All values are data (`BalanceConfig` / a `ThrowDef`; spec 08 §2's `throwSpeedByWeightClass` key is this table's first row). **PROPOSED** = new to this GDD.

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `throwSpeed.light / medium / heavy` (mm/s) | 14000 / 10000 / 6000 (spec 13) | 8k–20k / 6k–14k / 4k–9k, keeping heavy < medium < light (validator 1) | range ∝ v² (F3) | mailboxes unreachable; walking wins | walking pointless; weight trivial | `g`, mailbox spacing, `catchVolume` |
| `g` (mm/s²) **PROPOSED** | 9800 | 8000–14000 | arc shape, hang time, catch time | floaty; slow relays | flat arcs; catches impossible | speeds, `catchPredictMs` |
| `loftDeg` **PROPOSED** | 6 | 0–8 | aim feel at flat pitch | everything must be lobbed | auto-lob; short throws overshoot | pitch clamp (validator 11) |
| `ThrowRangePermille` (Cannon Arm) | 1500 | 1000–1500 (validator 2) | long-range throws | perk feels dull | more TIMEOUTs; a sniping meta | `maxFlightTicks` |
| `maxFlightTicks` | 90 (spec 13) | 60–150 | high-lob cap | cuts normal Cannon arcs | long concurrent flights, F4 cost | Cannon Arm, worldItemCap |
| `throwCooldownTicks` / `throwBurstTicks` **PROPOSED** | 5 / 5 | 3–15 / 1–10 | sustained throw rate / hitch tolerance (T1 leaky bucket) | spam; litter; bandwidth (Network) | clunky relays; hitched throws rejected | `throwMinAim` (Input) |
| `handHeightMm` / `fwdOffMm` / `sideOffMm` **PROPOSED** | 1200 / 300 / 250 | 1000–1400 / 300–500 / 0–400 | launch point | launches inside the capsule | launches through thin walls (mitigated by the chest sweep) | capsule, camera shoulder |
| `catchRadiusMm` | 1500 (spec 13) | 1000–2200 | catch ease | frustrating misses | catches you didn't mean; bonks rare | bonk radius, `catchPredictMs` |
| `catchPredictMs` / `catchTapMs` **PROPOSED** | 500 / 500 | 300–800 / 300–700 | prompt lead and press tolerance | ≤ 200 ms presses miss (H-FX-4 fails) | arms that linger; auto-catch feel | RTT, `catchHistoryTicks` |
| `catchHistoryMaxOffsetMm` **PROPOSED** | 1500 | 1000–2000 (validator 18) | latency tolerance vs reach | high-RTT players can't catch | catches across the street | Player Character F5 |
| `bonkRadiusMm` | 300 (= capsule) | 250–500 | teammate hit rate | the hook never happens | lanes blocked by friends | `catchRadiusMm` |
| `bonkCosmeticCooldownMs` **PROPOSED** | 1000 | 500–2000 | hat-lift and sound spam | strobe / audio harassment | real bonks feel silent | — |
| `enemyHitSlackMm` **PROPOSED** | 300 | 0–500 | hit forgiveness on moving goons | throws whiff visibly | hits through air | EnemyDef radius |
| `damagePerPoint` | 5 (spec 13) | 3–8 | thrown damage 5/15/40 | throwing at goons pointless | mail beats weapons | EnemyDef HP, Mail Bat 25 |
| `dropPushbackMm` **PROPOSED** | 500 | 300–800 | where bounces land | re-grab lands inside the volume | the bounce ends far away | `catchVolume` |
| `previewSettleMs` (accessibility player setting) **PROPOSED** | 0 | 0–250 | neutral pending marker during a class change (T3) | — | verdicts read later | the 100 ms label wait |
| `relayWindowTicks` **PROPOSED** | 150 | 60–300 | what counts as a relay hop | relays never register | every re-throw is a "hop" | — |
| `flightRenderDelayTicks` / `ghostBlendMs` / `ghostSnapMm` / `ghostHoldMaxMs` **PROPOSED** | 3 / 100 / max(1000, 1.5 × one-way × speed) / 600 | 2–4 / 50–200 / — / 300–1000 | flight presentation (Network) | visible jitter | catches look out of reach | RTT |

**Knobs owned elsewhere and referenced here:**
- Input & Controls: `throwMinAim` (player setting 0.15–0.40 s, default 0.25), `catchArmSeconds` 3.0 s (Toggle catch latch), `aimSteadiness`.
- Mail & Destinations: `catchVolume`, `ThrownCorrectPayMult` 1.1, `doorComplaintCap`.
- Grid Inventory: `waterReturnSeconds` 10, `worldItemDespawnSeconds` 300, `worldItemCap` 512, `weightPoints`.
- Player Character: aim camera `aimCamMult` 0.6, the capsule, F5 `claimToleranceMm` 1500 (trust in the claimed origin and catch position).
- Enemy AI / Health & Damage: EnemyDef HP and radius.

**Interactions to watch:**
- Speeds and `g` together set range. Change one and re-derive F3, and re-check the catch window against flight time.
- `catchRadiusMm` and `bonkRadiusMm` trade against each other: a larger catch sphere makes accidental bonks rarer.

## Visual/Audio Requirements

*From art-director, per art bible §1, §4, §5 and §7. Door outcomes belong to Mail & Destinations and are not repeated here: flag, wobble, resident, closed / locked / refused beats, and the ≤ 150 ms teeter. This system draws the arc, the flight and its landing events. Principle 2 governs: the arc tells the truth about the town, not about your friends. Red means you got it wrong, and magenta never appears on the arc. Audio names are proposals for the audio-director.*

### Arc preview (screen-space; thrower only)

**Common spec:**
- **Core line:** 6 px with a 2 px Charcoal outline each side, constant at any distance. This is the art bible §7 outline exception, extended to "arc **and flight trail**" (amendment requested, Open Questions).
- **Markers:** ≥ 24 px, teammate pins ≥ 30 px, with a 2 px Charcoal outline. All dashes and dots are static.
- **Label:** see UI Requirements. It is a Charcoal pill with Chalk White text and a left chip carrying the glyph. Text is never red or magenta (art bible §7).

| Class | Line | End marker (shape / inner glyph) |
|---|---|---|
| Accept | Go Green, **solid** | **hollow ring** (5 px stroke) with ✓ |
| Misdeliver | Stamp Red, **dashed** 18 px on / 10 px off, square caps | **heavy X** (the X is the glyph) |
| Ground | Chalk White, **dotted** (6 px dots, 14 px pitch) | **flat disc**, no glyph |
| Reject (closed / locked / won't fit) | dotted | disc with a Charcoal bar "—" |
| Water | dotted | **wave-bottomed disc** with "≈" |
| Belt | dotted | **chevron-notched disc** with ">" |
| Timeout (3 s) | dotted, fading to 0 over the last 0.5 s | disc with a clock |
| Blocked early (wall, roof, construct) | truncates at the contact point; no continuation | disc at the contact point |
| Arc passes a teammate's catch sphere | unchanged (whatever the static outcome is) | an extra **hat pin**: their hat silhouette with tint band and four Chalk carets, at their **current** position, no ✓/✗ |

- **Green and red are only for mailboxes [D].** A pin never turns the line green or red and never says "Catch". Goons are never pinned.
- **Greyscale test (checkable).** In greyscale at 1080p, on grass, sand, road and a Chalk White wall:
  - each line is identifiable by its pattern alone (solid / dashed / dotted);
  - each marker by its silhouette alone (ring / X / wave disc / chevron disc / plain disc / pin); Ground, Reject, Timeout and Blocked share the plain disc and differ by a ≥ 12 px inner glyph (≥ 16 px under Force patterns).

  This makes art bible §4's colour-blind row testable (TC-U-3).

### Flight (every peer)

| Element | Spec |
|---|---|
| Trail | Keeps the **launch verdict's** colour and pattern, frozen at release on every peer's screen: a red-dashed trail stays red even if the box closes mid-flight. Length 0.4 s of flight fading 100 → 0 %; white trails 0.25 s. Others see the trail only, never the aim arc, end marker or label |
| Bundle tiers (match Mail's resident tiers) | 1 piece; 2–3 strapped trio; 4–9 twine brick; 10+ fat bundle, at 1.0 / 1.15 / 1.35 / 1.6 × mesh scale, one swapped mesh per tier; Kraft with a 2 px Charcoal outline |
| ×N tag | billboarded pill, Gold digits on Charcoal (Grid Inventory's stack-count voice), N ≥ 2, ≥ 14 px, 0.4 m above the parcel, hidden beyond 35 m, ≤ 8 at once |
| Spin | a pure cosmetic function of `flightId`, so all peers match (no RNG stream, Simulation Core HB3): singles 2 rev/s, bundles 1 rev/s; stops at contact |
| Blob shadow | Charcoal 40 % disc on the ground below the item, radius 0.3 m at launch, shrinking and lightening with height (screen floor 10 px) — the landing read for bystanders |

### Outcome beats owned here

Grammar: squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s; squash within 0.8–1.2, limbs stretch ≤ 1.3× (art bible §5); one-shots are never time-scaled (Player Character). The Feel & Comedy table is the authoritative list of outcomes. Load-bearing rules: the parcel leaves the hand socket on the release snap frame and is never delayed for animation; the catch squash plays only on the host's confirmation; goon hit-stop (50 ms) is on the goon's rig only; neither a bonk nor a fumble is ever red. Per-beat poses and timings are parked in `design/ux/throwing-catching-seed.md` §2 for the art bible and `/asset-spec`.

### Audio

| Cue | Space / who hears | Rate limit |
|---|---|---|
| `sfx_throw_preview_hit` / `sfx_throw_preview_miss` | 2D, thrower only; **edge-triggered** when the arc's end class changes to Accept / Misdeliver and holds 100 ms (T3); rising bright two-note vs falling buzzy two-note (timbre differs, not only pitch); `sfx_throw_preview_blocked` (neutral tick) for Reject; ground and water silent | ≥ 200 ms apart; on by default, toggle in Accessibility |
| `sfx_throw_release` | 3D, all peers; whoosh pitched 0 / −2 / −4 / −6 semitones by bundle tier | ≤ 6 voices; 80 ms per thrower |
| `sfx_throw_flight_loop` | 3D loop on the parcel, with Doppler — a bonk victim's audio warning (the visual one is T7's carets and edge chevron) | nearest 4 |
| `sfx_throw_cancel` | 2D, thrower | — |
| `sfx_land_ground_s/m/l`, `sfx_land_water`, `sfx_land_belt_clack` | 3D, all peers; played on the domain event, never before it | 4 per 250 ms, merged by position |
| `sfx_catch_fwump` (+ 2D confirm tick to the thrower) | 3D, all peers | per catch |
| `sfx_catch_forwarded_chime` | 3D; +1 semitone per hop, cap +5, resets after 3 s | per hop |
| `sfx_catch_clap_empty`, `sfx_catch_fumble` | 3D, all peers; the fumble is neutral (no descending sting) | per event |
| `sfx_bonk` (woodblock) | 3D + a 2D layer to the victim | 1.0 s per victim; suppressed hits play only the ground thud |
| `sfx_goon_hit_crunch_s/m/l`, `sfx_goon_floored_stamp` | 3D, all peers | 200 ms per goon; the stamp once per goon |

### Accessibility rows

| Setting | Behaviour |
|---|---|
| Reduced motion | Spin halves. Trails become a fixed-length, non-fading ribbon. Hat-lift and squash move to the 0.9–1.1 range. Stamps and carets cut in by opacity. Dashes were never animated. The arc fade-in becomes a 0.1 s opacity cut |
| Colour-blind (arc) | The verdict is carried by pattern (solid / dashed / dotted), marker silhouette, inner glyph, label text and the preview hit/miss sound — colour is always redundant (closes OQ-IC-3(c) with UI Requirements) |
| Preview settle | `previewSettleMs` 0–250: a neutral pending marker during a class change, never the previous verdict (T3) |
| Force patterns | Dash, dot, marker, inner-glyph and label sizes × 1.5 |
| Muted HUD | The trail, stamps and sounds still convey every outcome (Mail's fantasy test 1) |

**Art-bible amendments requested (art-director rulings, Open Questions):**
1. The Ground arc is **dotted**: the bible leaves the white pattern unspecified.
2. White keeps the **flat disc**, with wave and chevron variants for Water and Belt. The ring stays exclusive to green, so ✓ and ○ cannot be confused.
3. Extend the outline exception to the flight trail.
4. Ratify teammate hat pins as screen-space UI and the Gold ×N world tag.
5. Ratify the SIGNED FOR stamp as an in-world goon gag. RETURN TO SENDER stays reserved for the Run Over results beat (CD ruling 2026-10-16).

> 📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:throwing-catching` to produce per-asset visual descriptions, dimensions and generation prompts from this section.

## Game Feel

*From gameplay-programmer and art-director. These are design targets, not polish.*

**Weight and responsiveness profile.**
- **The throw is committed.** There is no recall once it is released, and the input never waits on animation.
- **The catch is a readiness test.** You must mean it, and a short press is enough.
- **The flight is the stage.** Everyone watches it, and its trail never lies.

| Target | Value | Note |
|---|---|---|
| Press → arc visible | ≤ 2 rendered frames (≤ 33 ms at 60 fps) | geometrically correct from frame 1; the 0.08 s fade is alpha only |
| Release → item leaves the hand | ≤ 1 frame | the launch ghost spawns on the release frame from the hand socket and lerps onto the F1 path over its first 3 ticks (socket ≤ 400 mm from the F1 origin) |
| Arc update | every rendered frame unless the whole T3 cache key is unchanged | budget is the per-frame worst case: ≤ 0.3 ms on a mid PC, hard cap 0.5 ms (H-PERF) |
| Thrower's timeline | the ghost runs on time since release, so it leads the authoritative flight (rendered at hostTickEst − 3) by ≈ one-way + 100 ms: ≈ 1.8 / 2.2 / 2.7 m for a light throw at 60 / 120 / 200 ms RTT. On `ThrowLaunched` it blends in time over max(100, 1.5 × one-way) ms. It **snaps** when the path differs: `originClamped`, or a chest-point launch. At a static contact it **holds without settling** until the outcome event (≤ `ghostHoldMaxMs` 600), then plays the beat. On rejection it fades over 150 ms where it is | no hovering parcel at the mailbox rim |
| Remote peers | flights render at hostTickEst − 3 ticks (100 ms); bodies render 160 ms behind (registry `transformSendHz`) | the 60 ms gap and the up-to-≈ 3 m history reach are hidden by T7's 100 ms catch magnet; a WorldItem spawn bound to a `flightId` stays hidden until that flight's visual lands |
| Outcome beats | never before the server's domain event; the mailbox teeter is Mail's (≤ 150 ms, holds still if later) | |
| Hit-stop | goon hit 50 ms on the goon's rig only; none on bonk, catch or the player/camera | presentation-only animation speed 0; never a Sim time-scale |
| Screen shake | none by default; optional 2–3 px / 80 ms for a heavy landing within 2 m of the local player (off under reduced motion) | |
| Haptics (pad) | aim start light tick (Input); release ~20 ms low-frequency pulse scaled by class; catch ~40 ms medium; goon hit short pulse | player setting; respects the haptics and reduced-motion toggles |

**Aim camera profile (values owned here, applied by Player Character P9) [D]:**
- Shoulder +0.75 m and boom 2.4 m, eased in over 0.1 s to match the arc fade.
- Look sensitivity × 0.6 (`aimCamMult`, Input F10).
- **FOV unchanged by default.** The −10° narrowing is an optional setting, because a narrower FOV makes depth harder to read for a lob.
- A camera-collision pull-in moves the camera, never the aim vector.
- A 4 px Chalk White crosshair dot with a Charcoal outline marks camera forward only. The arc and its marker are the target read.

**Feel tests:**
- **(1) The arc is honest.** In replay, the preview's end point and outcome class equal the host flight's for 10 000 random static-geometry throws.
- **(2) The release is never late.** The ghost leaves the hand ≤ 1 frame after the release input across the input-replay corpus.
- **(3) Every catch has a readable cause.** In M0 sessions, ≥ 90 % of "I pressed and didn't catch" reports map to a T14 cause: `race_lost`, `out_of_reach`, `ineligible` (swimming, aiming, mounted) or `no_flight` (too late).

## UI Requirements

*From ux-designer. The label catalogue, prompt-slot strings, catch-feedback table and live-region lines are parked in `design/ux/throwing-catching-seed.md` for `/ux-design` (CD scope ruling D6). This section keeps the rules they must satisfy, and with Visual/Audio's patterns and silhouettes it closes Input & Controls OQ-IC-3(c).*

- **Arc label** (thrower only, screen-space). It is a Charcoal pill beside the end marker. It never covers the marker, flips above the marker rather than meet the prompt rect (HUD H11), and stays inside a 24 px safe inset.
  - It is always two lines. Line 1 is glyph + place: `✓ 17 Elm St`, `✗ 9 Jade Close — wrong house`, `Closed — opens at Delivery`, `Ground`, `≈ Water — floats 10 s`, `> Joins belt`, `Drops (3 s)`.
  - Line 2 is `×N · money`: `×20 · +240¢`, `×20 · −120¢`, `×20 · −60¢ to floor · 10 returned`, `×20 · refused at floor · all returned`.
  - Text is Chalk White, never red or magenta; "×N" is Gold. Strings are localisation keys with arguments, never concatenated.
- **Money is shown [D]** (F7). It is the stake the player commits, which answers OQ21 without softening anything. The label uses the same client `Evaluate` and value hint as Manual Delivery's prompt.
- **Timing.** Label text, the live-region line and the preview sound wait 100 ms of stability. Everything else is exact every frame (T3).
- **Text scale** goes up to 200 %. Addresses wrap and are never truncated, the maximum width is 35 % of the screen, and a wrapped line 1 grows the pill (tested at 1280×720).
- **Prompt slot** (shared with Player Character P11):
  - Disallowed-throw and cancel reasons show dimmed with a lock icon in HUD's transient reason strip for its F1 lifetime (H11). A host rejection reads "Couldn't throw".
  - Catch states: `Catch ×N from Jules` before the press, then `Catching…` with a mitt icon for the armed window.
  - Result lines (HUD H4 routes them; a clean catch is a hotbar pulse and history, no toast): `Caught ×N — slot 3` or `Caught ×N — bag`; `Bag full — fumbled` (catcher only, neutral); `Jules caught it` to a race loser; `Dry land required` to a swimmer.
- **Live region.** Polite, ≤ 1 line per second; superseded lines are dropped and muted cues follow HUD H26. Screen-reader throwing is **out of scope for v1**: the live region serves low-vision and cognitive access.
- **World vs screen space** (art bible §7).
  - In the world: carets on inbound mail, the hands-up pose, the trail, bundles and ×N tags, and landing beats.
  - On screen: the aim arc, markers and label (the named exception), the prompt slot, feed lines and the hotbar pulse.
  - No outcome lives only in a HUD number.

> **📌 UX Flag — Throwing & Catching**: run `/ux-design` for:
> - the arc label (layout, flip, 100–200 % scale);
> - the prompt-slot state catalogue;
> - the hotbar pulse and the caught feed line;
> - the incoming-throw cue, including off-screen;
> - the live-region spec with a Brief / Full verbosity setting;
> - the hands-up pose.
>
> Seed material: `design/ux/throwing-catching-seed.md`. Stories that touch this UI cite `design/ux/[screen].md`, not this GDD.

## Network

*From network-programmer.*
- **Vocabulary:** E event, R request. **Authority:** the host owns flights, catches and every landing; the thrower's aim and origin and every player's position are client-owned (spec 13 §3). **Latency target:** 150 ms RTT, PROPOSED.
- **Status:** Godot 4.7.1 multiplayer behaviour is NOT SOURCEABLE locally (Grid Inventory OQ12), so this is a contract on the adapter; byte layouts, channels and spikes go to the flight-protocol ADR (ADR inputs, below). M-1a is single-player through a loopback adapter running the same Sim functions.

| Message | Kind | Recipients | Notes |
|---|---|---|---|
| `ThrowRelease{seq, entryId, units, origin, dir}` | R | host | Grid Inventory's `throw` op with a payload extension, in the one `Inputs.Apply` FIFO; `origin` = claimed **feet**; `dir` = the quantised direction of the **last drawn** preview frame; **no tick field** (Input R11): launch is at the host apply tick, and any future tick field is host-domain (Simulation Core N4) |
| `ThrowLaunched{flightId, throwerId, seq, launchTick, origin, dir, speed, visual}` | E | all peers | `visual` = tier, hop, verdict, `originClamped` |
| `FlightResolved{flightId, outcome, pos, receiverId, age}` | E | all peers | **presentation only**, idempotent: no money, HP, entries or counts |
| `CatchArm{down \| up}` | R | host | the host drops edges beyond 4/s per player |
| `flightId` on Grid Inventory, Mail `DeliveryOutcome`, Health & Damage and Belt `LaneInsert` events | E | per owning system | +2 B each |

- **State changes** come only from the owning system's own event. A client that sees an unknown `flightId` skips the arc visual and still applies the domain event (a rejoiner relies on this).
- **Host-only:** launch, sweep, receiver priority, catch race, cooldown, `thrownPm` snapshot, damage. **Client-predicted (cosmetic):** the aim preview, the launch ghost, the Catch prompt, the static-landing settle, the mailbox teeter. **Never predicted:** inventory, money, HP, WorldItem existence, catch success.
- **Ordering** (reliable-ordered; event before reply): for the thrower, Grid `Removed` → `ThrowLaunched` → `ok`; per tick, in flight creation order, `FlightResolved` then the owning domain events. No state depends on that order (`FlightResolved` is idempotent and cosmetic), so a failed cross-node ordering SPIKE degrades visuals only and does not block M0. The host derives the sender from the transport peer, never from a field.
- **Timing:** launch at the host apply tick, no back-dating; the first sweep is at age 1. Remotes render flights at `hostTickEst − flightRenderDelayTicks` (3, ≥ one-way at the target) and fast-forward a late event by evaluating F2 at the current age. The thrower's timeline is in Game Feel.
- **Catch protocol:** `CatchArm` edges at press and release, not a per-flight request (a request sent when the catcher *sees* contact would always lose to the host's earlier resolution). Races follow T6: earliest `f`, then nearer at contact, then `hash(flightId, playerId)`; catch versus mailbox in one tick goes to the catcher (rank 0); arming is forward-only, so "already landed" cannot happen. Fumble is decided by the catcher's panel on the host. The replicated `interacting` pose bit is cosmetic and may differ from the host's armed window.
- **Trust:** catch-position trust is **unbounded by design, accepted under spec 13 §4.3** (friends-only); `catchHistoryMaxOffsetMm` bounds only the spread of the history samples, never the newest report.
- **Moving receivers:** armed catchers against their 300 ms reported-position history (F5); bonk victims at their newest reported position (position-stale, Dead and Frozen skipped); goons at the host's current tick plus `enemyHitSlackMm`; vehicles at the driver's newest transform.
- **Join / rejoin:** flights contribute **0 B** **[D]**. They live ≤ 3 s and Grid Inventory buffers post-snapshot WorldItem and container events, so a joiner gets each flight's result and misses only the arc. The catch arm resets on rejoin; join-in-progress proper is Prep-only.
- **Disconnect:** a thrower's flight continues on its `FlightRecord` (`throwerId`, `thrownPm`, `actorId` unchanged; Mail M-R7-1, M-R16-1). A catcher's arm clears and the Frozen body is neither catchable nor bonkable (transport events apply before Throwing's step; a defensive same-tick CAUGHT would insert into the frozen panel, Grid Inventory R11). Host loss ends every flight with the session (no migration).

**Bandwidth (PROPOSED; Networking & Session confirms).** Positions are int32 × 3 (12 B). Payloads: `ThrowRelease` 25 B, `ThrowLaunched` 30 B, `FlightResolved` 18 B, `CatchArm` 1 B (registry `throwPayloadBytes`); RPC framing is assumed at +8 B (SPIKE), so each flight costs ≈ 66 B down per peer.

| Rate | Flights/s (team of 8) | Down per peer | Up per thrower |
|---|---|---|---|
| Typical raid | 4 | ≈ 2.1 kbps | ≤ 0.3 kbps |
| Honest-client maximum (`throwMinAim` 0.25 s + cooldown) | 19.2 | ≈ 10.1 kbps | ≈ 0.6 kbps |
| Host-enforced cap (T1 leaky bucket, 6/s per player) | 48 | ≈ 25.3 kbps (63 % of spec 06 §4.2's 40) | ≈ 1.6 kbps + ≤ 0.3 kbps arm edges |

With Player Character's 15 Hz transform (≈ 6.7 kbps up, registry `transformSendHz`), a thrower at the host cap totals ≈ 8.6 kbps up, ≈ 8 % over the 8 kbps budget; typical play fits. Fallbacks, in order: a per-tick `FlightBatch` per peer (≈ 8 B saved per event); then fold the GROUND, WATER, BELT, REJECT, BONK and HIT_ENEMY resolutions into their domain events.

**ADR inputs** (implementation, not design): byte layout and framing; channel and transfer mode (including whether `CatchArm` gets its own channel or rides the unreliable transform with an `armSeq`, against head-of-line blocking under loss); the cross-node RPC ordering SPIKE and its fallback; `flightId` and tick-field wrap (low 16 bits relative to `hostTickEst`); host-tick clock sync; `FlightBatch`; the T4 validation order; the catch trust surface.

## Acceptance Criteria

*Specialist: qa-lead (rewritten at design-review 2026-10-16). Tags as in `mail-destinations.md` Section H:*

| Tag | Meaning |
|---|---|
| **[UNIT]** | xUnit on the plain-C# Sim (`tests/unit/throwing/<Rule>Tests.cs`, no Godot) |
| **[INTEGRATION]** | Sim across two systems' public interfaces or stubs |
| **[FUZZ]** | randomised property tests; seeds in `tests/fuzz/throwing/seeds.txt` |
| **[PERF]** | performance measurement |
| **[UI]** | Godot-side against a scripted Sim stub, asserting the UI event log on a virtual clock |
| **[NET]** | loopback adapter or headless bots |
| **[MANUAL]** | playtest with a named owner; evidence in `production/qa/evidence/throwing/<id>.md` |

- **Gating:** non-MANUAL criteria block Done for their milestone. Wall-clock millisecond thresholds are ADVISORY until the M1 reference machine exists; tick- or virtual-clock timings are BLOCKING. Every [UI] criterion files a capture in `production/qa/evidence/throwing/<id>/`, signed off by the ux-designer (UI), art-director (visual) or gameplay-programmer (feel).
- **Milestones:** M-1a unless tagged **M0** (catch, bonk, relay, network), **M-1b** (goons, belts) or **Later** (Cannon Arm, tier meshes, reduced motion).
- **Numbers:** every vector uses integer RH (floor-based for negatives), `CeilDiv`/`FloorDiv` and `isqrt`.

**Fixtures:**

| Fixture | Contents |
|---|---|
| **FX-Flat** | terrain y = 0; mailbox MB-A at (10250, ·, 10000), yaw 0, catch volume 1000 × 800 × 1000 centred 1000 above the base (x 9750–10750, y 600–1400, z 9500–10500); MB-B identical at (10250, ·, 16000) |
| **FX-Wal** | wallet stub, floor −500 |
| **FX-L20** | 20 letters (maxStack 20), spawn 12 ¢, `MailValueMult` and `MisdeliveryPenaltyMult` 1000 ‰ (penalty 6 ¢), on time |
| **FX-T** | feet (10000, 0, 20000), facing −Z, right-handed, light, `loftDeg` 6 |

### Core rules

- **TC-T1-1 [UNIT, parametrised]** GIVEN a valid release, WHEN the carrier is walking, sprinting, airborne, wading at 899 mm entered from dry land, OnSupport (velocity not inherited) or spawn-protected, THEN it launches and protection is unchanged. Still Swimming at 800 mm (Player Character hysteresis) → `swimming`; Dead → `dead`; on break → `frozen`; Mounted → `unavailable`.
  - **b [UI]** Hands slot → "Select mail to throw"; empty slot → "Nothing to throw"; Holding → "Put it away first"; no aim starts and nothing is sent.
- **TC-T1-2 [UNIT]** GIVEN TAT 0, WHEN releases apply at ticks 100, 100 and 104, THEN the first two launch and the third gets `throw_cooldown` (entry and TAT unchanged); one at 105 launches. **b [UI]** A client release within 5 ticks of its own last launch is held and sent when the cooldown elapses.
- **TC-T2-1 [UNIT]** GIVEN FX-L20 selected, WHEN thrown, THEN one `FlightRecord` holds all 20 mailIds, the `entryId` is kept and the slot is empty. A standard-issue axe (light, value 0) throws 1 unit.
- **TC-T3-1 [UI]** GIVEN camera pitch +80° / −70°, THEN the sent elevations are +76° / −54° (`dy` 15 897 ± 2 / −13 255 ± 2) and equal the preview's integers.
  - **b** Accept = green solid, ring ✓; Misdeliver = red dashed, X; Water = wave disc; Belt = chevron disc; Ground, Reject, Timeout = white dotted plain disc with their glyph.
  - **c** An arc through a teammate's catch sphere adds a hat pin with their name; line colour unchanged, label still two lines. Goons are never pinned.
  - **d** On a class change at t0, line colour, pattern, marker shape and glyph change on the same frame; label text changes at t0 + 100 ms, not t0 + 99. With `previewSettleMs` 250 the marker is the neutral pending ring from t0 to t0 + 250, never the previous verdict. A release at t0 + 50 gives the trail the new class.
  - **e** Changing any one cache-key input (dir, origin, phase, wallet, world version, a teammate's position) re-runs the preview query.
- **TC-T4-1 [UNIT, parametrised]** GIVEN a request failing checks N and N+1 (N = 1…7), THEN the reply is check N's.
  - **b** Claimed feet 1500 mm (3D) from the nearest history sample launch from the claim; at 1501 the origin becomes the newest sample, with no rejection, and `originClamped` is set.
  - **c** Launch is at apply tick T; the first sweep is at T+1. **d** A rejected release leaves slot, count and container version unchanged.
- **TC-T5-1 [UNIT]** GIVEN one release replayed under 1 000 run seeds, THEN launches and terminals are identical and the Sim makes 0 RNG draws. **b** The thrower (even after a Second Wind respawn) never receives their own flight; Dead, Frozen and Mounted bodies are passed through.
- **TC-T6-1 [UNIT, parametrised]** GIVEN stub receivers at equal f = 500, THEN rank decides (7 adjacent pairs); equal rank → nearer centre → lower `hash(flightId, receiverId)`, so the winner varies across flightIds. **b** An earlier `f` beats rank: a mailbox at f = 300 beats a catcher at f = 400.
- **TC-T6-2 [UNIT, FX-Flat]** GIVEN MB-A, THEN (10250, 1300, 10800) → (10250, 1200, 10200) lands (descending); the same with start y 1100 (rising) or y 1000 → 1000 (flat) does not. A flight rising through MB-B and descending through MB-A lands in MB-A.
  - **b [parametrised: closed, LOCKED, non-mail]** A Reject → one WorldItem at the F8 point, 300 s timer, 0 wallet calls, 0 complaints.
- **TC-T6-3 [UNIT, parametrised]** GIVEN each receiver, THEN a chest, Intake console or truck cargo face → GROUND; a vehicle roof → the item on the ground below; water → no pickup through tick 299 after landing, and at 300 mail returns to the back of the Intake queue and non-mail is destroyed; an injected flight whose only ground contact is segment 89 → 90 → GROUND; no contact by age 90 → TIMEOUT onto the surface below.
  - **b** A drop point on a roof footprint snaps via 1000 → 3000 → 6000 mm back along −v; if all fail, it goes to the receiver's base (feet / F8 front point / nearest walkable tile). A point past the map edge is clamped 1 m inside; a WATER landing at the shore floats at its contact point.
- **TC-T7-1 [UNIT, M0]** GIVEN `down` at 100: `up` at 106 → armed at 114, not 115; `up` at 130 → armed at 129, not 130; no `up` → armed at 189, not 190 (hold cap).
  - **b** Toggle: `down` at 100, no second press → armed at 189, not 190. **c [parametrised]** Death, mount, swim, panel open, aim start, disconnect and rejoin each clear the arm.
  - **d** D3 gate, with a focused Deliver target: the prompt shows Catch only on ticks where the 15-tick prediction is true and returns to Deliver on the first false tick; a Deliver hold in progress is never converted; a flight whose static receiver is the focused mailbox never flips it.
- **TC-T7-2 [UNIT, M0]** GIVEN FX-L20 caught by a catcher whose hotbar letter stack holds 5 (room 15) and nothing else free, THEN `CAUGHT{15, 5}` with the newest 5 at their feet (`origin = Thrown`).
  - **b** Only slot 0 free on the hotbar, inventory free → `CAUGHT{20, 0}`, selection unchanged. **c** Nothing fits → `FUMBLE`.
  - **d** A same-tick hand op freeing a slot counts. A second flight in the same window bonks the now-unarmed catcher.
  - **e [parametrised]** A swimmer → WATER at their position; an aiming player → BONK; the thrower → passed through; Holding-closed → caught into the hotbar, never the cursor.
  - **f** OQ24: selection moves to the caught slot only from the hands slot or mail, never from a tool.
  - **g** The catcher's later delivery of those units has `actorId` = catcher and the catcher's own `thrownPm`. Race losers keep their arm.
  - **h [UI]** History `Caught ×20 — slot 3` / `— bag` (hotbar pulse, no toast); the thrower's toast reads "Jules caught 15 · 5 fumbled".
- **TC-T7-3 [UI, M0]** A flight predicted to enter the local catch sphere from behind or off-screen shows carets and a static edge chevron with the prompt; own throws show nothing.
- **TC-T8-1 [UNIT, M0]** GIVEN an unarmed teammate is hit, THEN their HP, inventory, position and spawn protection are unchanged and the entry is one WorldItem at their feet.
  - **b [UI]** Bonks on one victim at ticks 0 and 29 play one hat-lift, one at 30 plays another, a second victim at tick 10 gets their own; items drop every time.
  - **c** Newest sample only: a teammate whose newest sample is 301 mm clear of the segment is missed even if an older sample was on it; a position-stale teammate (> 30 ticks) is skipped.
- **TC-T9-1 [INTEGRATION, H&D stub, M-1b]** GIVEN light, medium and heavy goon hits, THEN `ApplyDamage(5 / 15 / 40, Thrown, throwerId)` is queued once per flight and applied in `Combat.Resolve`. A 20-stack, a Cannon Arm thrower and a thrown axe deal the class value only; players and constructs take 0. **b** A radius-300 goon is hit at 599 mm from its axis and missed at 601; the item drops at its ground point.
- **TC-T11-1 [INTEGRATION]** GIVEN deliveries closing at tick T, WHEN released at T−1 and landing at T+89, THEN OPEN. **b** Released at T → `closed`. **c** Released in Prep at S−10, landing at S+10 → `closed`. **d** Run end with 3 flights airborne → TIMEOUT in creation order, no `DeliveryOutcome`, no pay; a flight landing on the Victory tick resolves normally before the flush.
- **TC-T13-1 [UNIT, M0]** GIVEN a catch at tick 0, WHEN a flight containing any caught unit launches at 150 (window `age ≤ 150`, including after a merge into an older stack), THEN `hop` = prior + 1; at 151 → 0; an unrelated entry at tick 10 → 0. It saturates at 255.
- **TC-T14-1 [UNIT]** The stats file holds every T14 field (no-catch cause, `redAgeAtReleaseMs`, `armedBeforeLaunch`, `originClamped`); bonks and fumbles are counted; a schema check finds no bonk field in Payday, statistics, achievement or perk data.

### Formulas

- **TC-F1-1 [UNIT, FX-T]** GIVEN dir (0, 3406, −16026), THEN n = 16 383, vel (0, 2911, −13 695), origin (10 250, 1200, 19 700).
  - **b [Later]** `ThrowRangePermille` 1500 → speedPm 1224, v 17 136, vel (0, 3563, −16 763); `thrownPm` 1100 survives the thrower's disconnect.
  - **c** Left-handed → origin.x 9750. **d** RH(−1, 2) = 0; RH(−3, 2) = −1; CeilDiv(−7, 2) = −3; FloorDiv(−7, 2) = −4.
  - **e** dir (0, 0, 15 999) / (0, 0, 16 769) → `bad_direction`; (0, 0, 16 000) / (0, 0, 16 768) pass. Elevation `FloorDiv(1000·dy, n)` 981 / −877 rejects, 980 / −876 passes. (−32768, 32767, 32767) rejects without overflow.
- **TC-F2-1 [UNIT]** GIVEN the F1 flight, THEN p(1) = (10250, 1292, 19244), p(20) = (10250, 963, 10570), p(21) = (10250, 837, 10114), and the k = 90 drop term is 44 100. **b** A rejoiner's direct evaluation at age 20 is bit-identical to the host's age-20 segment endpoint.
- **TC-F3-1 [UNIT, ±0.1 m, ±1 tick]** The integer Sim matches the F3 float oracle: light at aim 30° = 20.55 m / 54; heavy at aim 45° = 4.4 m / 35; **[Later]** medium + Cannon Arm at aim 15° = 12.7 m / 33.
- **TC-F4-1 [UNIT, FX-Flat]** GIVEN P0 (10250, 963, 10570) → P1 (10250, 837, 10114), THEN f = 154, contact (10250, 944, 10500), MAILBOX.
  - **b** D = (0, −126, −70) → f = 1000 hit; D = (0, −126, −69) → no hit. **c** P0 (10250, 1000, 10200) inside the volume, D (0, −100, −50) → f = 0.
  - **d [M-1b]** Goon r 300 + slack 300, y 0–1800, centre (0, 0); P0 (−2000, 500, 0), D (2000, 0, 0) → f = 700. **f** The same goon behind: P0 (2000, 500, 0) → no hit. **g** Entered above its top: P0 (−700, 2400, 0), D (1000, −900, 0) → f = 667, not 100.
  - **e [M0]** Catcher feet (0, 0, 0), P0 (−3000, 1000, 0), D (3000, 0, 0) → f = 500.
  - **h** Heightfield, n = 4, first sample at or below H at i = 2 with gaps 100 and −50 → f = 417.
- **TC-F5-1 [UNIT, M0]** GIVEN a flight whose F4 contact with the catcher's sphere is 15 ticks ahead, THEN the prediction is true; 16 ticks ahead, false. True for 2 ticks → prompt off, 3 → on; then false for 4 ticks → still on, 5 → off.
  - **b** A history sample 1500 mm (3D) from the newest counts; 1501 is ignored. **c** Newest report 30 ticks old → catchable; 31 → not; empty → not.
  - **d [NET]** At 120 ms RTT the prompt appears at −400 ms host time; a press 200 ms later applies at −140 → CAUGHT; a press sent after −60 misses.
- **TC-F6-1 [UNIT, parametrised, M-1b]** GIVEN damage 5 / 15 / 40 against HP 60, 40, 400, 30, THEN hits to kill are 12/4/2, 8/3/1, 80/27/10, 6/2/1. Mega ×2.5 → 30/10/4, 20/7/3, 200/67/25, 15/5/2.
- **TC-F7-1 [UNIT, parametrised, FX-L20]** THEN label line 2:

  | Case | Label line 2 |
  |---|---|
  | Accept | `×20 · +240¢` (`+260¢` at `thrownPm` 1100, **Later**) |
  | Misdeliver at wallet −380 | `×20 · −120¢` |
  | Misdeliver at −381 / −440 / −494 | `−114¢ to floor · 1 returned` / `−60¢ to floor · 10 returned` / `−6¢ to floor · 19 returned` |
  | Misdeliver at −495 and −500 | `×20 · refused at floor · all returned` |

  - **b** 1 unit (spawn 8 ¢, penalty 4) then 2 units (9 ¢, penalty 5) at wallet −492 → −4¢, 2 returned. **c** Dead letters (T = 0): Accept +0¢; Misdeliver still shows the full penalty.
- **TC-F8-1 [UNIT]** GIVEN C (10250, 944, 10500) and vel (0, 2911, −13 695), THEN the drop is (10250, 0, 11000).
  - **b** C.z = 10 050 → pushed in 100 mm steps to z 10 650. **c** A 45° entry at the volume's corner ends with max(|dx|, |dz|) ≥ 600 from the centre. **d** |vxz| < 1000 → pushed along the outward front normal. **e** No walkable point within 1000 mm → the mailbox base front point.
- **TC-CV-1 [UNIT, parametrised]** GIVEN a data file violating exactly ContentValidator rule N, THEN it is rejected with code `THROW-CV-N`, and the shipped file passes. Violations per rule:

  | Rule | Violating values |
  |---|---|
  | 1 | heavy = medium; light 2 999 |
  | 2 | 1 501; 999 |
  | 3 | `g` = 0 |
  | 4 | 29; 151 |
  | 5 | duplicate rank |
  | 6 | yaw 45°; bottom −1 |
  | 7 | radius 99; height 499 |
  | 8 | no `weightClass` |
  | 9 | 299 |
  | 10 | 1 401 |
  | 11 | `loftDeg` 9 |
  | 12 | `catchTapMs` 450 |
  | 13 | `catchPredictMs` 3 100 |
  | 14 | `catchRadiusMm` 2 500 |
  | 15 | 0 |
  | 16 | `throwCooldownTicks` 0; `throwBurstTicks` 0 |
  | 17 | map 1 000 001 |
  | 18 | `catchRadiusMm` 2 200 with `catchHistoryMaxOffsetMm` 1 600 |

  - **b** At runtime, `ThrowRangePermille` sources summing 1 800 clamp to 1 500 (a clamp, not a rejection).

### Edge cases

- **TC-E-1 [UNIT]** GIVEN a chest → origin sweep blocked by a wall, THEN the flight launches from feet + 1000, one WorldItem lands ≤ 1000 mm from the feet, and the cooldown runs. **b** An origin inside MB-A's volume: a descending throw lands at T+1 (f = 0), and an upward one exits.
- **TC-E-2 [UNIT, M-1b]** GIVEN two flights reach one goon in one tick, THEN they resolve in creation order: the first lethal hit takes kill credit and the second passes through. A goon dead before the step is not a receiver.
- **TC-E-3 [UNIT, FX-Wal]** GIVEN the wallet at −500, WHEN a Misdeliver lands, THEN 0 units are consumed, one WorldItem appears at the F8 point, `DeliveryOutcome{units 0, stoppedAtFloor}` is sent and no complaint is raised. **b** Two Misdelivers in one tick resolve in creation order.
- **TC-E-4 [UNIT]** GIVEN a release applied on the thrower's knockdown tick, THEN it launches; one tick later → `dead`, entry in the bag. A catcher dying later in the resolution tick: CAUGHT, then the death spill takes it 1:1.
- **TC-E-5 [UNIT, parametrised]** GIVEN a merge, split, sort or selection move after aiming, THEN `stale_entry` and the slot is unchanged; a replayed `seq` after FLIGHT → `stale_entry`. **b [UI]** A Toggle aim whose latched entry changes count cancels with "Aim cancelled".
- **TC-E-6 [UNIT]** GIVEN a release queued before the thrower's disconnect, THEN it launches; after it → `frozen`; after launch the flight continues with `throwerId`, `thrownPm` and `actorId` unchanged. **b** A 100-tick solo pause leaves flight ages unchanged, and resuming never throws. **c** `flightId` wraps after 65 536 launches, still in creation order.
- **TC-E-7 [UNIT]** At `worldItemCap` a throw is never refused: eviction takes old mail (≥ 10 s), then the oldest non-mail. Mail dumped at a closed box keeps its deadline.

### Cross-system

- **TC-X-1 [FUZZ]** GIVEN 8 players, the recorded seeds and 18 000 ticks of random throws, catches, bonks, goon hits, water, belts, mailbox outcomes, disconnects and a run-end flush, THEN every flight has exactly one terminal, the multiset of mailIds and item units is conserved (Grid Inventory R19), and the FLIGHT count is 0 after the flush.
- **TC-X-2 [INTEGRATION, Mail stub, FX-L20]** THEN `ResolveThrownLanding` is called once per mailbox landing; Accept pays 240¢ (260¢ at `thrownPm` 1100, **Later**) with `actorId` = the thrower. **b** Wrong house at wallet −440 → 10 consumed (wallet −500), 10 in one WorldItem. **c** Injected `FlightRecord` with `openAtRelease` true: landing at T+90 → OPEN; T+91 → `closed`.
- **TC-X-3 [INTEGRATION, Input stub]** GIVEN `throwMinAim` 0.25 s, THEN a 249 ms aim cancels with "Hold to aim", a 250 ms aim launches, and an early Toggle second press cancels with "Hold to aim". At 0.15 s, a 150 ms aim launches.
- **TC-X-4 [INTEGRATION, Player Character stub, M-1b]** GIVEN a spawn-protected thrower, THEN protection ends on the tick a flight resolves HIT_ENEMY (Throwing's step, before `Combat.Resolve`), never for GROUND, MAILBOX, CAUGHT or BONK. A bonked victim keeps their protection.
- **TC-X-5 [INTEGRATION, Belt stub, M-1b]** GIVEN 20 units and a lane accepting 12, THEN the 12 oldest insert, `LaneInsert` carries `flightId`, and 8 become a WorldItem at the contact point. A full, category-refusing or just-deconstructed lane is not a receiver.

### Network [M0]

- **TC-N-1 [NET]** GIVEN three peer stubs, THEN the thrower gets `Removed`, `ThrowLaunched`, `ok` per launch; per tick `FlightResolved` events arrive in creation order, followed by the domain events; the sender is taken from the transport peer.
- **TC-N-2 [NET]** GIVEN `CatchArm{down}` arriving after resolution, THEN the flight stays resolved and the arm can catch another flight within 15 ticks; edges beyond 4/s are dropped. **b** A rejoin mid-flight: flight state 0 B; the resulting events apply (an unknown `flightId` draws no arc); the arm is cleared; not catchable before the first report.
- **TC-N-3 [UNIT]** GIVEN maximum field values, THEN payloads are 25 B (`ThrowRelease`), 30 B (`ThrowLaunched`), 18 B (`FlightResolved`) and 1 B (`CatchArm`) and round-trip. **b [NET]** 8 players at 4 throws/s in total for 1 800 ticks → ≤ 16 000 B down per peer, including 8 B framing per message.
- **TC-N-4 [NET]** A loopback host catch uses the same 300 ms history rule; an `originClamped` launch snaps the ghost; a ghost at a static contact holds ≤ 600 ms for its outcome event.

### Performance

- **TC-P-1 [PERF]** GIVEN 600 frames of aiming while walking with a pad (no cache hits), scope = Sim flight query + `Evaluate` + label build, THEN p95 ≤ 0.3 ms and max ≤ 0.5 ms (ADVISORY), with 0 allocations per frame (BLOCKING).
- **TC-P-2 [PERF, M0]** GIVEN 16 flights and 8 armed catchers over 10 000 ticks, THEN `Throwing.Step` is ≤ 0.15 ms p99 and ≤ 0.5 ms max (Simulation Core's phase-4 slice), and ≤ 0.5 ms p99 with 60 flights (ADVISORY), allocating 0 bytes (BLOCKING).

### UI, visual and accessibility

- **TC-U-1 [UI, parametrised]** For every class in the seed catalogue, the label is exactly two lines built from localisation keys with arguments, never red or magenta, and under pseudo-loc +40 % at 1280×720 and 200 % text scale it never truncates an address or overlaps the marker or the prompt rect.
- **TC-U-2 [UI, M0]** GIVEN an inbound flight, THEN the prompt reads `Catch ×20 from Jules` before the press and `Catching…` for the armed window; a fumble reads "Bag full — fumbled" with no red or magenta hue. **b** Several events within 1 s produce ≤ 1 polite live-region line. **c [M-1a]** Each disallowed case shows its string in the reason strip for HUD F1's lifetime ± 1 frame.
- **TC-U-3 [MANUAL, N = 5, owner ux-designer]** At 1080p and 20 m, in greyscale and protan, deutan and tritan simulation, on grass, sand, road and a Chalk White wall, and while panning at 90°/s: ≥ 4/5 testers name each of Accept, Misdeliver, Ground, Reject, Water and Belt, and 5/5 tell hit vs miss vs neutral. **b [UI]** Measured label contrast ≥ 4.5:1 and marker outline ≥ 3:1.
- **TC-VA-1 [UI, M0]** GIVEN a red-arc release whose target box closes mid-flight, THEN every peer's trail stays red and dashed. **b [Later]** Stacks of 1, 2, 3, 4, 9, 10 show tiers 1.0 / 1.15 / 1.15 / 1.35 / 1.35 / 1.6; the ×N tag shows only for N ≥ 2, at 34 m but not 36 m.
- **TC-VA-2 [UI]** GIVEN the end class changing to Accept or Misdeliver, THEN held 99 ms → no sound; 100 ms → the preview sound once, ≥ 200 ms apart, thrower only. Reject plays the neutral tick; ground and water are silent.
- **TC-VA-3 [UI, Later]** GIVEN reduced motion, THEN spin halves, the trail is a fixed ribbon, squash stays within 0.9–1.1, and no screen shake plays.

### Feel and fantasy

- **TC-FE-1 [FUZZ]** GIVEN 10 000 random static-geometry throws, THEN the preview's end point and class equal the host flight's.
- **TC-FE-2 [UI]** GIVEN the input-replay corpus `tests/replay/throwing/` (≥ 200 releases, mouse and pad), THEN the ghost leaves the hand ≤ 1 frame after the release and the arc is visible ≤ 2 frames after the press.
- **TC-FE-3 [UNIT, owner producer]** GIVEN a stats log, THEN the analyser reports throw and hand shares of player-driven correctly delivered units per shift × build bucket (Manual Delivery test 3; "insufficient" below 200 units) and the `redAgeAtReleaseMs` distribution.
- **TC-FE-4 [MANUAL, M0, owner qa-lead]** Feel test 3: ≥ 90 % of in-session "I pressed and didn't catch" reports map to a T14 cause (`race_lost`, `out_of_reach`, `ineligible`, `no_flight`).
- **TC-FA-1 [MANUAL, M0, 4 sessions × 4 players, owner game-designer]** Fantasy test 1: in ≥ 3 of 4 sessions every player makes ≥ 1 unprompted catch in every 5-minute window of Delivery, ≥ 1 catch per session has `armedBeforeLaunch`, and ≥ 1 flight has `hop` ≥ 2.
- **TC-FA-2 [MANUAL, M0, 4 sessions, owner game-designer]** Fantasy test 2: two raters code utterances within 5 s of every bonk, fumble and red-arc release, κ ≥ 0.6 (below that, a third rater re-codes); laugh and callout outnumber blame in ≥ 3 of 4 sessions; 0 throw-related host-kicks and 0 "stop throwing at me" requests.
- **TC-FA-3 [MANUAL, two-friend greybox + M0 solo, owner producer]** Fantasy test 3: throws are 50–80 % of player-driven correctly delivered units; ≥ 20 % of mailbox throws come from ≥ 10 m; the solo player starts a second solo run unprompted and rates the session ≥ 3 / 5.
- **TC-FA-4 [MANUAL, first two-friend session, owner game-designer]** Fantasy test 4: the median thrown `units` of letter and postcard stacks is > 5, with n ≥ 30.

*Not covered by an automated criterion (by design): the T12 rulings (allowances, verified by the FUZZ lane not crashing), haptics and the aim camera (UX spec), and ghost-blend tuning (Game Feel values; tune at M0).*

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | **Closed 2026-10-16 — owner ratified.** **Execution variance (CD-SENIOR D1).** No dice, ever. The `throwSpreadMrad` knob is retired: at ≤ 15 mrad it moves a landing ≤ 0.3 m at 20 m, against 1 m volumes 3–6 m apart, so it could never reach a wrong door. If the hand share of player-driven correctly delivered units falls below 20 % (Manual Delivery test 3), revisit with non-random levers only: range, catch volume, `throwMinAim`, or the game-designer's `verdictRangeM`. That lever keeps the exact line and marker beyond ≈ 12 m but withholds colour, glyph and money, which would amend spec 13 §4.1 and Mail R13's "a mismatch is never hidden", and needs the owner. Seeded spread is ruled out; any future randomness must be a registered Simulation Core stream indexed by a per-run u32 launch ordinal, never `flightId` (HB3). The ruling rests on one developer tester and a deterministic prototype with no stacks; treat it as provisional until the two-friend greybox (the < 20 % trigger above stays a standing rule). | — | closed |
| 2 | **Hand-backs (this GDD edits no other doc).** Adopted by their owners 2026-10-16: Mail R13/OQ8 wording and `releaseTick`; Grid Inventory's `throw` payload, reply codes and `TryCatch{unitsIn, unitsFumbled}`, with OQ6/7/24 closed; Player Character OQ16, the `ThrowLaunched` one-shot and the aim camera; Input OQ-IC-3 (no tick field) and the D3 gate; Simulation Core's 25 B. Still open:<br>(a) **Simulation Core:** drop the stale "Throw spread is in mrad" units row and the "spread seed" cross-reference (`throwNoDice`).<br>(b) **Content & Data — rename REJECTED:** the StatSheet key stays `ThrowRangePermille` (registry `throwRangePermille`), not FT-1's `ThrowRangePm`; the `THROW-CV-nn` rules join its validator catalogue when written. | the named GDDs | their re-reviews |
| 3 | **Closed 2026-10-16 — owner decided.** ~~Cannon Arm perk text~~: the real range gain is ×1.24–1.47 depending on aim (×1.30 at flat aim, ×1.47 at the peak). The card reads the computed outcome, ≈ "30 % farther" (`{fx:throw.rangeRatio}`, Content & Data CD27); throwing is not retuned; spec 10/13's "+50 %" is superseded (Sources). | — | closed |
| 4 | **Health & Damage contract (provisional):**<br>• `ApplyDamage(amount, source = Thrown, actorId)` applies to goons only. It is queued in Throwing's step and applied in `Combat.Resolve` (Sim Core phase 10).<br>• It accepts a Dead or Frozen `actorId`.<br>• The damage event carries `flightId`.<br>• Mega HP is ×2.5.<br>• The HIT_ENEMY spawn-protection end happens at Throwing's step (T1). | Health & Damage GDD | before M-1b |
| 5 | **Belt Logistics contract (provisional):** a per-unit `TryInsert(lane, point, unit)` with partial accept; lane top-face geometry for the sweep; `LaneInsert` carries `flightId`; the client visual sim adopts an item at a point. | Belt Logistics GDD | before M-1b |
| 6 | **Vehicles (provisional).** Vehicle bodies and roofs are solids at the driver's newest transform. Items are never parented to a roof, so a moving roof leaves them behind on the ground. Is that the desired gag, or should the truck bed accept throws later? | Vehicles & NPC Drivers GDD / game-designer | M1 |
| 7 | **Bonk vs Pillar 1.** A teammate standing in front of a mailbox blocks the throwing lane (rank 3 < mailbox in time order). Playtest before softening anything; the failure signal is ≥ 3 "you're in the way" blame utterances per session. | game-designer | two-friend greybox |
| 8 | **Whole-stack default (Grid Inventory OQ21).** Fantasy test 4: if the median thrown letter or postcard `units` is ≤ 5, revisit the default, never the penalty. The money on the label is this GDD's mitigation. | game-designer | first two-friend playtest |
| 9 | **Art-bible amendments** (art-director rulings):<br>• the white arc is dotted;<br>• white keeps the disc and the ring is green-only;<br>• Water and Belt get their own disc silhouettes;<br>• the outline exception extends to the flight trail;<br>• teammate hat pins are screen-space UI;<br>• the ×N world tag is Gold;<br>• the SIGNED FOR stamp is an in-world goon gag, and RETURN TO SENDER stays reserved for Run Over (CD ruling). | art-director / creative-director | `/art-bible` revision |
| 10 | **Audio and visual specifics:**<br>• the preview hit/miss sound is on by default for everyone (recommended yes);<br>• there is no bonk-specific pre-impact cue: a teammate's warnings are the flight sound and T7's Catch prompt, carets and edge chevron (a bonk is a catch nobody took);<br>• water items sink with bubbles at second 10;<br>• the audio cue table and accessibility rows move to the Audio and Accessibility docs once those exist. | audio-director / art-director | Audio GDD; playtest |
| 11 | **Bandwidth.** Confirm the three-tier budget (Network) and whether `FlightBatch` and resolution folding ship at M0. A thrower at the host cap is ≈ 8 % over the 8 kbps up budget. | Networking & Session | M0 spike |
| 12 | **Engine VERIFY (Godot 4.7.1, not sourceable locally):**<br>• a screen-space, 2 px-outlined, dashed or dotted arc ribbon without per-frame allocation (with near-plane clipping and arc-length dash phase);<br>• `MultiMesh` buffer updates for bundles;<br>• `Camera3D.UnprojectPosition`;<br>• `Input.StartJoyVibration`;<br>• RPC ordering across nodes on one channel from C#.<br>→ spike and ADRs (Network). | technical-director / godot-csharp-specialist | Technical Setup |
| 13 | **UX follow-ups** (UX Flag; seed `design/ux/throwing-catching-seed.md`):<br>• whether the intercept cue shows only to the predicted catcher (default yes: carets on the parcel, local only);<br>• an aim-label verbosity setting (Brief / Full) for the live region;<br>• label placement when the marker is off-screen. | ux-designer | `/ux-design` |
| 14 | **Telemetry fields** (T14): extend the prototype's `playtest_stats.txt` with throw and hand shares in units, class at release vs outcome, range, `units`, hop chains, catches (`armedBeforeLaunch`), bonks, fumbles and `redAgeAtReleaseMs`. These are the instruments for OQ1, OQ7, OQ8 and fantasy tests 1–4. | Dev Tooling / producer | before the two-friend greybox |
| 15 | **Two-friend greybox format** (CD-GDD-ALIGN caveat): the prototype REPORT does not say whether that session is networked or friends taking turns on one machine. The answer decides what fantasy tests 3 and 4 measure there; catch, bonk and relay (M0 slice) cannot be observed with players taking turns. | producer | before the two-friend session |
| 16 | **Deferred from design-review 2026-10-16:**<br>• Heavy range is 4.7 m, so the trick shot exists only for light mail: accept, or raise the heavy speed.<br>• Catch fairness at high RTT: add a dead-reckoned catch sample, and test the prompt against the catcher's own delayed position.<br>• Remote playback: hold at age 0 when `ThrowLaunched` arrives before its render time, and lerp the chord by fractional tick for high-refresh rendering.<br>• Bonks are tested at the newest position and can read up to ≈ 0.7 m off on remote screens; a `bonkLagSlackMm` knob, or accept it. | game-designer / network-programmer / gameplay-programmer | M0 |

**→ becomes an ADR** (implementation, not design):
- the shared closed-form integer ballistic and sweep used by both the preview and the host;
- the flight protocol (see Network, ADR inputs);
- catch validation under client-authoritative movement;
- the presentation timing rule "never before the server outcome", shared with Mail & Destinations;
- the preview rendering method and its budget.
