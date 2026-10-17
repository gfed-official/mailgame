# HUD & Notifications

> **Status**: In Review (/design-review MAJOR REVISION NEEDED 2026-10-16; revision applied, awaiting re-review)
> **Author**: Evan Deters + agents (autonomous /design-system; revised by /design-review)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Implements Pillar**: Pillar 1 (The mail must flow — the shared shift picture; Pillar 1 information wins the screen), Pillar 5 (Scales from 1 to 8 — busy, never buried), Pillar 3 (Hands beat machines — calls to run over); MDA Fellowship (#1)
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-10-16 — verdict CONCERNS; 7 findings applied; rulings: "Fumbled" no exception, "dead letter" allowed as a postal term of art
> **Design review 2026-10-16 (creative-director scope ruling)**: comms (chat, pings, wheel, emotes) **moved** to `design/gdd/comms.md` (frozen behind the M-1a GUARD); layout, widget styling, accessibility policy, audio limits and implementation detail **parked** in `design/ux/hud-notifications-seed.md` (unreviewed seed). Rule ids are kept; moved rules leave a one-line pointer.
> **Reconciled 2026-10-16 with sibling GDDs (hand-backs: 22 adopted, 10 adapted, 3 rejected)** — Input & Controls, Player Character OQ28, Mail & Destinations R16, Manual Delivery OQ5, Throwing & Catching, Simulation Core HB9, Content & Data CV-HUD, Grid Inventory; HN-1…HN-4 closed. Rejections in Open Questions.

## Summary

HUD & Notifications is everything a carrier reads on screen during a run:
- **the shift picture** — timer, quota, complaint, team wallet with money pills, compass, hotbar and held item, backlog;
- **one disciplined notification pipeline** — toasts, event feed, banners, captions, history and a screen-reader live region, so eight players and a belt factory never bury the prompt or the throw arc;
- **the surfaces comms draws on** — the screen-projected marker layer, the compass and name tags. The comms model itself is `design/gdd/comms.md`.

It must always answer *"where is my crew, and what did we just do?"* — in deadpan clerk's text, never ranking players, and never as the only report of an outcome.

> **Quick reference** — Layer: `Presentation` · Priority: `MVP (M-1a timer/wallet/prompt/pills/held chip/single toast → M0 feed, name tags, toast queue → M1 full HUD, history, banners → M2 machine badge → M3 raid/HP → M4 live region, Compact)` · Key deps: `Input & Controls, Player Character & Movement, Grid Inventory, Mail & Destinations, Manual Delivery, Throwing & Catching (In Review); Simulation Core, Content & Data (soft); Run Structure, Economy Wallet & Shop, Defenses, Enemy AI & Waves, Health & Damage, Networking & Session (provisional); Comms (sibling, frozen)`

## Overview

HUD & Notifications is the **screen-space presentation layer of a run**. It has two parts:

1. **The persistent HUD** — widgets about the team's shift (art bible §7's diegetic rule): shift / phase timer, quota bar, complaint envelope, team wallet with gain / loss **pills**, compass strip, hotbar frame and **held-entry chip**, HP bar, weight icon, backlog chip, name tags. It also **renders** the interaction prompt and hold ring, whose *content* is supplied by the system that owns the focused target (Manual Delivery, Player Character, Input & Controls).
2. **The notification pipeline** — one set of rules for everything transient: **toasts** (personal verdicts), the **event feed** (team news), **banners** (phase, raid, controller disconnected, host lost), **captions**, the **history** log and the **live-region** mirror. Other systems emit *cues*; this system decides whether, where, how long and in what order they show.

**Data layer.** HUD owns no gameplay state, makes no gameplay decision and **replicates nothing**. It reads replicated values through the `IHudState` facade (H30) and consumes per-op events (`DeliveryOutcome`, inventory op replies with `byActorId`, `CarrierKnockedDown`, raid and construct events). Comms messages are Comms' (`design/gdd/comms.md`).

**Player impact.** The player reads this system passively, at a glance. Without it **Pillar 1** loses the shared shift picture, and art bible principle 2 (*honest outcomes are physical*) loses its second half: the world shows *what happened*, the HUD names *who, how much, and what it cost*. Every pill and toast has a world event beside it.

**Phasing.** **M-1a** (greybox, solo — spec 12 "Minimal HUD: timer, wallet, interact prompt with address match indicator"): time-only timer, wallet, prompt and hold ring with ✓ / ✗ and predicted ¢, focus bracket, own pills, one toast slot, hotbar frame and held chip (Grid Inventory R11 needs them), the cue table with its accessibility fields, the cue-pairing table. **M0**: event feed, team pills, name tags, the 2-slot toast queue, the event log ring. **M1**: full timer, quota, complaint, compass, backlog, history, banners, knocked-down block, Essentials. **M2**: blocked-machine badge, sorter counter. **M3**: raid banner and caption, raid / construct compass icons, HP. **M4**: live-region announcer, Compact layout, accessibility settings.

Implementation patterns (CanvasLayer stack, queue as autoload vs node, announcer Control, pooling, fonts) → **ADR "HUD Composition & Notification Queue"** (seed §5).

## Player Fantasy

*Framing: **Direct**. Shaped by creative-director (2026-10-16): **A "Within Shouting Distance"** is the spine; **B "The Office Keeps the Minutes"** is the HUD's written voice; **C "Performative Mail"** is the carriers' gestures (Comms).*

**Within Shouting Distance.** The island is 600 m across, and proximity voice carries about a street (30 m, spec 13 §4.2). The HUD closes the rest of the gap. You are alone on Violet Row, without a headset. The feed reads "Jules delivered 12 letters on Elm St", and the compass shows Jules' hat at 140 m east — you know where Jules is without looking. A ping lands on your compass: *Need materials*, from Sam. Every widget answers one question: **where is my crew, and what did we just do?** The HUD is the crew's noticeboard. It is never a scoreboard.

The emotion is **relatedness at a distance**. It carries weight because of the concept doc's open M-1a question — *"the fun may be social, not mechanical"* — which the M0 Fellowship playtest must answer before comms hardens.

**Pillars served.**
- **Pillar 1 — *The mail must flow*:** the timer, quota and backlog say *how much is left*. When a notification competes with an address, the prompt or the throw arc's landing marker, Pillar 1 information wins the screen.
- **Pillar 3 — *Hands beat machines, until they don't*:** the blocked-machine badge and "Belt → 9 Jade Close" are calls to run over, not status reports.
- **Pillar 5 — *Scales from 1 to 8*:** busy, never buried — merge rules, the crew line and caps keep eight players legible.

**The written voice is the clerk (B).** Every sentence the HUD writes is deadpan postal record: subject, verb, object, past tense, a name and an address — *"Jules delivered 20 letters to 9 Jade Close."* No "!", puns, "Oops" or "Nice!". The text plays the straight man.

### Feel & Comedy Feedback

*Rule: **the words are the clerk; the widgets are the slapstick.***

| Register | Where it lives | Rule |
|---|---|---|
| **Deadpan text** | feed, toasts, banners, captions, announcements | subject–verb–object, past tense, names and addresses. Never jokes, softens or mocks |
| **Loud UI** | pills bounce, the complaint envelope shakes, the raid banner bursts once | amplifies a world event and lands *just after* it (art bible principle 2) |
| **Player voice** | chat, ping labels, Say lines, emotes (Comms) | the HUD never styles, rewrites or jokes over a player's words |
| **Colour meaning** | every widget | red = you got it wrong; magenta = the rival did it to you; gold = money; late is gold, never red. Never the only channel (H23) |

**Anti-goals — the HUD must never:**
1. Be the only report of an outcome, or soften a bad one, or laugh at the player.
2. **Rank players mid-run.** No per-player earnings, misdelivery counts or MVP badges on any surface during a shift, including history (H21). *Naming* who is fine; *ranking* is not. Payday's roll-call (Mail & Destinations R16) is the only public reading, and the feed honours its `rollCallNames` setting (H9).
3. Cover the landing marker, the interact prompt or an address read (H11).
4. Make voice users or text users second-class (Comms).
5. Use banned vocabulary (Player Character UI Requirements) or break the fourth wall.

**Fantasy tests** (thresholds **PROPOSED**, HN-17):
1. **Heard without voice** — owned by Comms (H-FT-1, with a no-ping baseline: the answer rate must beat the rate at which a teammate comes within 20 m in matched 30 s windows with no ping).
2. **The two-second read.** ≥ 5 testers, 2 s glimpse of a mid-Delivery 8-player HUD: ≥ 90 % correct on "on quota pace?", "time left ± 30 s?" and "where is the nearest teammate (direction and ± 50 m)?" — read from the compass distance label. Automated half: every pill or toast cue has a paired world cue (H-FT-2b).
3. **Laughter, not noise.** Runs inside Mail & Destinations' M-FT-3 sessions and coding scheme (blame / laugh / callout, two raters, κ ≥ 0.6). HUD adds: ≤ 1 in 5 testers says the HUD hid something they needed; no per-player ranking appears in any recording.

## Detailed Design

*Specialists (2026-10-16): game-designer, ux-designer, ui-programmer, art-director, technical-artist, accessibility-specialist; design-review revision by the same plus qa-lead and network-programmer under the creative-director's rulings. **[D]** = logged decision. Unsourced numbers are **PROPOSED**. Milestones **[M-1a]**…**[M4]**.*

### Core Rules

#### A. Ownership and the cue contract

**H1 — What this system owns.** The layout, rendering and lifetime of every screen-space widget and screen-projected marker; the notification pipeline (classification, queue, merge, eviction, history, live-region mirror). It owns **no gameplay state, no gameplay decision and no network message**. Content belongs to the emitter: deliver prompt body → Manual Delivery; reason text → Grid Inventory; knockdown strings → Player Character; catch and arc-label strings → Throwing & Catching; prompt grammar and glyphs → Input & Controls; comms lines → Comms. Prompt content arrives as one **`PromptData`** per prompt (keys, args, availability, mode, hold progress, toggle state, **at most one reason key** — precedence is the owner's; Player Character OQ28). Values belong to their replicator: phase, quota, complaint → Run Structure; wallet → Economy; `backlogUnits` → Mail & Destinations.

**H2 — The cue contract.** Every notification enters as a **cue**: `{cueId, audience, severity, mergeKey, actorId, targetId, args, sourceTick, opId}`.
- `audience` ∈ {`personal`, `team`}; `severity` ∈ P0–P3 (H5).
- The **cue table** row for each `cueId` holds: localisation key, default surface, `spokenKey` (TTS text with no symbols — "cents", "times", "minus"; required whenever the display text contains ¢ × − ✓ → ·), `politeness` (assertive / polite), `rateClass` (Audio), `captionClass` (none or a caption id), `flashClass` (none / governed), and the paired world-cue id for outcome cues (H3). These fields exist and are validated from **M-1a** (Content & Data's shared library, CV-HUD-01…05); the announcer that reads them ships at M4 [D, CD ruling a].
- **Emitters never choose the surface.** Names, addresses and numbers travel as `args` (ids and integers), never preformatted strings. **All money renders through one `IMoneyFormatter`** (Mail & Destinations: one locale-aware formatter, no mix of formats); the format itself is Economy's (HN-6).
- A cue id with no table row is a load-time validation error, never a silent drop.

**H3 — Never silent, never alone.**
- **Never silent:** every rejection or outcome an emitting GDD lists reaches at least one visible surface **in every HUD mode and preference** (H20: Essentials never hides own verdict toasts, own pills, the prompt, timer, wallet or P0 banners), the event log (H25; history from M1) and, from M4, the live region (H26). This holds when a toast is merged or evicted.
- **Never alone (art bible principle 2):** a pill or toast reporting an *outcome* must have a paired world cue declared by its emitter, in a static cue-pairing table checked in CI (H-FT-2b, M-1a). Prompts and banners are exempt.

#### B. Notification surfaces

**H4 — One event, one primary surface.**

| Surface | Carries | Audience | Visible at once | Lifetime | Milestone |
|---|---|---|---|---|---|
| **Prompt** | the focused target's verb, state and reason — anything knowable before the press | local | ≤ 2 (Input & Controls) | while focused | M-1a |
| **Pill** | money deltas only, under the wallet; slot 1 reserved for **own** pills | team | 3 (1 own + 2 team) | 2.5 s × duration setting | M-1a own · M0 team |
| **Toast** | the verdict on *your own* action ("**Jules** got it first", "No room there", "Make space — Sort?") | personal | M-1a: 1 · M0+: 2 (1 at 200 % text) | F1 | M-1a |
| **Event feed** | team news: others' deliveries (merged, with street), anyone's misdeliveries, knockdowns, clock-ins, connection lines, belt misroutes, raid and construct lines, comms log lines Comms routes here | team | 5 (3 at 200 %) | F1, never under 6 s (spec 09 §2.4) | M0 |
| **Banner** | phase change, raid warning, all carriers down, controller disconnected, host lost | team (disconnect: local) | 1 + 1 pending | H7 | M0 (disconnect) · M1 · M3 (raid) |
| **Tip slot** | one onboarding tip (Onboarding & Tips owns text and triggers; Input & Controls' first-pad-input line "Controls: {Pause} › Settings" is its first client — before M4 the glyph swap alone carries it) | personal | 1 | until met or dismissed | M4 |
| **Caption** | essential sounds (`captionClass` ≠ none: raid horn, knockdown thud, clock-in stamp, on-break, controller two-note) | local | 3 lines | ≥ 3 s × duration setting | M1 (M3 raid) |

- **Routing:** your own routine success → own pill + world cue, **no feed line**; your misdelivery → own pill and (M0+) a feed line, because the team wallet paid for it, no toast; others' routine deliveries → feed (merged, H8) and the team pill, never a toast.
- **Catches (Throwing & Catching) [D, reconciliation]:** the catcher's clean catch → the hotbar slot pulse (its visible surface) and history, no toast; "Bag full — fumbled" (one cue — Grid Inventory's "Fumbled" row maps to it) and the race loser's "**Jules** caught it" (contention key, H8) → the catcher's toasts; the thrower's "**Jules** caught 15 · 5 fumbled" → a personal P3 toast, not a feed line (it concerns one player).

**H5 — Severity classes.**

| Class | Members | Rules |
|---|---|---|
| **P0 Critical** | raid warning, all carriers down, controller disconnected, host lost, own knockdown, "Dropping out in 0:15" | never dropped, merged away or muted; banners; assertive (H26) |
| **P1 State** | phase change, construct under attack, quota met, timer at 60 s / 15 s, the actionable "Make space — Sort?" | never evicted (by anything); phase may take the banner slot |
| **P2 Outcome** | rejections, contention, partial results, delivery and misdelivery outcomes | default for toasts |
| **P3 Ambient** | others' routine deliveries, feeder aggregates, comms log lines | first to be evicted; collapse into the crew line (H8) |

**H6 — Toast lane [D, design-review].**
- **M-1a — one slot, no queue.** A new toast replaces the visible one at once (same frame); the replaced toast logs `Evict` (and goes to history from M1). Merge (H8) is tried first.
- **M0+ — 2 visible, 4 queued** (`toastVisibleMax`, `toastQueueCap`). Toasts carry P1–P3; P0 items are banners. Resolution order for a new toast cue:
  1. **merge** if H8 matches;
  2. take a free slot;
  3. a P2 rejection of *your own last input* **replaces the oldest visible P2 / P3 toast** by `firstShownAt` (replaced → history), so the press you just made gets feedback in the reply's frame. It **never** replaces a P1: if every visible toast is P1, it queues at the head;
  4. queue.
- **Queue overflow — one total order.** When the queue is full, the victim is the **lowest-priority** queued item (P3 before P2), **oldest first**, among items of **equal or lower** priority than the incoming one. P1 items are never victims. If no item qualifies (every queued item outranks the incoming one, or all are P1), the incoming item goes to history as `Drop: overflow`. The feed-pending queue (H8) uses the same rule.
- Evicted rejections increment a "+N" chip on the toast stack, which opens history (M1). There is no pause-on-hover and **no "Until dismissed"** setting [D, CD ruling c]: the duration setting (1× / 1.5× / 2× / 3×) and the untimed history log (H21) satisfy WCAG 2.2.1.

**H7 — Banners.**
- One slot plus at most one pending (art bible §7). Priority: **host lost > raid > controller disconnected > all carriers down > phase**.
- A higher class pre-empts the visible banner; a pre-empted or displaced banner goes to **feed and history**.
- **Only P0 banners wait pending.** A phase banner that cannot show at once (slot held by a higher class) goes straight to feed and history — so phase and raid on one tick show only the raid.
- A newer pending banner of the same class replaces the older pending one; a P0 lower than both visible and pending goes to feed and history.
- **Pending TTL:** a pending banner older than `bannerPendingTtlMs` = 5000 is demoted to feed and history (P0 feed lines are never evicted; its caption still plays).

| Banner | Duration |
|---|---|
| Phase | 3 s |
| Raid warning | pinned for the 15 s warning (spec 09 §3.5) |
| Controller disconnected | until reconnect or dismissal (Input & Controls) |
| Host lost | until acknowledged; then return to menu (spec 06 §6) |

The eight-point burst is reserved for the raid and Mega alerts (art-director).

**H8 — Merge and coalesce [D].** A cue merges into an item only if the item is **Queued or Visible** (never Fading or Gone) **and** the cue arrives within the window (F8). Exact key first, then the contention key; otherwise a new item.

| Surface | Merge key | Window | Effect |
|---|---|---|---|
| Toast | `(cueId, actorId)` | 3 s sliding | "×N"; latest args; lifetime restarts, capped at `toastMaxMs × m` (F1) |
| Contention toast, **different** winners | `cueId` | 1 s sliding | "**Jules** +2 got it first" — N = distinct winners − 1; latest winner named |
| Pill | `(sign, source ∈ {own, team})`; teammates, feeders and constructs all share `team` | 500 ms sliding | a hand hold's k outcomes (same actor, destination and tick — Mail & Destinations R16) are **one** pill "+N¢ · k stacks" before any merge; amounts sum via the formatter; feeder pills aggregate per second. A 4th distinct pill folds into the oldest **team** pill; the own slot never folds |
| Feed line | `(actorId, kind, result)` | open–extend–commit: `commitAt = min(open + 8 s, lastEvent + 2 s)` | "Jules delivered 12 letters on Elm St" updates in place; the street is the latest event's |
| Feed — crew line | routine P3 delivery lines | while ≥ `feedCrewThreshold` = 3 distinct actors' lines would be visible | they collapse into one line "Crew delivered 31 letters" that updates in place; misdelivery, knockdown and P0–P2 lines never collapse |
| Feed — belt misroute | `destinationId` | 10 s tumbling from the first event | ≤ 1 line per door per 10 s (Mail & Destinations) |
| Complaint envelope | per op | — | one shake per `ComplaintRaised` op, never per unit |

- Every **raw** event enters the event log and history unmerged. **Feed overflow:** a new line at the visible cap evicts per H6's total order (P3 then P2, oldest first; P0 / P1 never evicted by lower). If every visible line outranks it, it waits in a **feed-pending queue** (cap 4, H6 overflow rule) and enters when a line expires — bounded by the 6 s floor. A new P0 evicts the oldest visible P1, then the oldest P0. History and the live region are written at emit time.

**H9 — Attribution.** Any outcome caused by another actor names them.
- Identity order is **hat icon → Bold name → 4 px tint strap** (art bible principle 3); tint is never identity on its own.
- Constructs read "The inserter", "Belt 3", "Truck 2", never their builder (Mail & Destinations R16). Unknown or departed actors read "Someone".
- **Misdelivery feed line** (clerk voice): "**Jules** posted 2 letters for 9 Oak Row to 17 Elm Lane." The actor follows Mail & Destinations' host setting `rollCallNames`: Names → the name; "A carrier" → "A carrier"; Off → "A carrier". The same rule applies to history (adopted by Mail & Destinations R16).
- Display names are sanitised at ingress (Comms H24 rule set — the HUD calls the same sanitiser) and truncated to `nameDisplayMax` = 12 graphemes (first 11 + "…"). Whitespace-only or empty names display "Carrier N" (1-based slot). Duplicate sanitised, invariant-case-folded, **truncated** names get " 2", " 3"… on the higher slot, appended after truncation.
- **Transient** surfaces resolve names at render time from the roster; **persistent rows** (history entries) snapshot name and hat at receipt.
- **No ranking (anti-goal 2):** no surface shows a per-player money total, misdelivery count or "best carrier", and history has **no per-actor filter**.

#### C. Persistent widgets and markers

**H10 — The persistent widget set.** Each widget binds to one `IHudState` value; changes are dirty-flagged; nothing re-renders per frame. Styling: seed §2 (art bible §7 amendments).

| Widget | Shows | Reads (owner) | Milestone |
|---|---|---|---|
| **Shift / phase timer** | "Shift 3 / 5 · DELIVERY 02:14"; Amber at 60 s, Red at 15 s; raid red fill with a ≤ 1 Hz pulse; state also changes an icon and outline thickness | `phase`, `deadlineTick`, `shift`, `renderSimTimeMs` (Run Structure; M-1a stub) | M-1a time only → M1 |
| **Quota bar** | "640 / 2214": Gold fill to the quota; on reaching quota a ✓ icon and "+surplus" label (formatter) | `shiftEarnings`, `quota` (Run Structure, provisional) | M1 |
| **Complaint envelope** | fills Stamp Red from the bottom; numeral on a chip; threshold ticks; one shake per op | complaint value, max, thresholds, `ComplaintRaised` (Run Structure, provisional) | M1 |
| **Team wallet** | Gold coin glyph and Gold numerals via the formatter; pending purchases greyed until confirmed (spec 09 §2.4); outline flash at the floor (governed) | wallet (Economy, provisional; M-1a stub) | M-1a |
| **Pill lane** | H4 / H8 | `DeliveryOutcome` (authoritative only, H30), purchase replies | M-1a own · M0 team |
| **Compass strip** | ±120° window over all bearings (spec 09 §2.4's 360° strip, read as every bearing shown with edge clamping — an interpretation, not a deviation); district bands with numerals; **teammate icons carry a distance label** ("140 m"); declutter **raid edge > construct under attack > ping > teammate > blocked-machine badge** (F6) | camera yaw, positions | M1 (bands, teammates, pings) → M2 → M3 |
| **Hotbar frame** | 8 slots; slot 1 = hands; selected-slot ring (thickness, never colour) | own panel (Grid Inventory) | M-1a |
| **Held-entry chip** | while the cursor holds an entry with the panel closed (Grid Inventory R11): swatch-with-glyph tab with district numeral, stripe, Bold house number, Gold count, skull if Cursed. Inventory & Build UI owns the panel only (Grid Inventory, adopted) | cursor (Grid Inventory) | M-1a |
| **Weight icon** | sack silhouette filled to `min(P, 40) / 40` (F7); no colour, no motion | `P` (Grid Inventory D1) | M1 |
| **HP bar** | Go Green fill, Stamp Red at ≤ 25 %, ticks at 50 / 25 %, numeral always shown | own HP (Health & Damage, provisional) | M3 |
| **Backlog chip** | Kraft sack icon and count when `backlogUnits` > 0; tiers 1–40 / 41–160 / 161+ pieces (Mail knobs); tier 3 on a Stamp Red chip | `backlogUnits` (Mail & Destinations) | M1 |
| **Lost Parcel marker** | Gold "×3" on Charcoal, ≤ 30 m, plus a compass echo | Loot WorldItems (Mail & Destinations) | M1 |
| **Name tags** | per teammate within `nameTagRangeM` = 10: hat, name, strap; state suffixes supplied by others ("· On break 1:43 · Inventory safe" Player Character — a suffix, not a hover tooltip, so every device reads it; "Typing…" Comms); speaking glyph (Proximity Voice) — shown or hidden, never animated | roster, flags | M0 |
| **Sorter overflow counter** | "Unmatched → overflow: N" on the open sorter's anchor (spec 09 §3.4) | Belt Logistics (provisional) | M2 |

**H11 — The interaction prompt and the protected zone.** This system draws the prompt; content comes from the owners in H1.
- **Placement [D, reconciliation]:** **screen-fixed**, centred, top edge 96 ref px under the reticle, max 720 wide (37.5 % at 1080p — inside Manual Delivery's ≤ 40 %), text ≥ 18 ref px on a backing plate — not world-space or edge-clamped, and no leader line: the focus bracket (H12) ties it to the target. Input & Controls' grammar, live glyphs, the hold ring after `ringDelay`, the slash overlay for disabled verbs, "Busy".
- **Deliver body:** Manual Delivery's branch table as written — the ✓ / ✗ mark leads line 1 ("✓ Hold [E] Deliver · 17 Elm St"), ≤ 3 lines plus one chip / strip row, "Wrong house — piece is for P" / "Posts 1 · −N¢", skull chip and hazard ring for Cursed and floor refusal. The game never hides a mismatch.
- **Reason strip [D, reconciliation]:** any owner's cancel or refusal reason shows in a transient strip below the live lines, which stay visible and pressable. Its lifetime is F1 (≥ 2.5 s × duration setting), ended early by a focus change — not Manual Delivery's 0.8 s `reasonStripTicks` or Throwing's 2 s (below the reading floor).
- **Limits:** ≤ 2 prompts; at 200 % one prompt; addresses wrap, never truncate.
- **Panel open:** the central band (y 14–67 %) belongs to the panel. The prompt reads "Close panel to deliver", hides the ring and moves to the **panel footer** strip directly below the panel.
- **Toast lane is anchored to the prompt rect's bottom edge** + 16 ref px, recomputed at every text scale and prompt height, so prompt and toasts never overlap. Its **alternate anchor** is the right gutter above the feed.
- **Protected rects:** the prompt rect, the throw arc's landing marker together with its arc label (Throwing publishes their screen rect each aiming frame) and the focused target's bracket rect. **Toasts and feed lines never fade or draw over them**: while the primary toast lane intersects the landing marker or bracket rect, toasts lay out at the alternate anchor. **Screen-projected markers** (pings, name tags, emote bubbles, Lost Parcel markers) that intersect a protected rect fade to ≤ 25 % opacity over ≥ 250 ms (not flash-class, H28).
- **Focus not obscured (SC 2.4.11):** while a focusable overlay is open (history, pause list, wheel list), no notification rect intersects its focused control's rect: toasts use the alternate anchor, else wait queued; P0 banners excepted.

**H12 — Screen-projected world markers [D].** Every marker about a point in the world is a **screen-space widget projected from a world anchor**, not a 3D billboard: it composites above post-processing, reads through geometry without raycasts, honours the text floors and is trivially edge-clamped.
- **Markers:** pings (Comms), name tags, pile beacons ("Your pile · 24 s · 80 m", Player Character), Lost Parcel markers, the focus bracket, the prompt anchor.
- **Behind the camera or off-screen:** the marker is not drawn; its compass echo carries it (ping edge arrows: Comms). The prompt is screen-fixed (H11) and unaffected; only the bracket disappears.
- **Draw order (HUD owns the layer order):** MarkerLayer below the persistent HUD; the landing marker (Throwing) and prompt above every marker (seed §5 layer list).
- **Caps** (F6): name tags within `nameTagRangeM`; pings 16 total; pile beacons 8; Lost Parcel markers 8; prompt 1; focus bracket 1. Emote bubbles (≤ 8, Comms) are counted separately.
- **Focus bracket (answers OQ-IC-11):** a ≥ 24 ref px Chalk corner-caret bracket on the focused interactable's bounds; at 2.5–6 m a single 16 px caret; both ≥ 3 : 1 against their backing (H23). Player Character supplies anchor and bounds.

#### D. Comms seam

**H13–H19 — moved to `design/gdd/comms.md`** (quick ping, comms wheel, ping lifetime, emotes, trust model, text chat, voice indicators; frozen). HUD provides Comms: the MarkerLayer (H12), the compass ping class (F6), name-tag suffix slots (H10), feed routing for comms log lines (P3), and the pipeline. Per-player mutes live in Comms.

#### E. Modes, history, accessibility

**H20 — Context modes × one preference [D, CD ruling e].**

*Context modes* (game-driven; exactly one applies; precedence **Knocked down > Map open > Panel open > Payday overlay > Normal**; pause is orthogonal):

| Context | Shows | Hides / changes |
|---|---|---|
| **Normal** | everything that applies | — |
| **Panel open** | top bar, pills, toasts, banners, feed | prompt in the panel footer reading "Close panel to deliver"; ring hidden |
| **Map open** | top bar, feed, toasts, banners (spec 09 §1: never block the clock) | compass, prompt, hotbar, HP |
| **Knocked down** | the knocked-down block "Clocking back in… 7" with the pile beacon — its own tier-A widget directly under the banner slot, outside the H7 queue; compass; feed | hotbar, held chip, HP, weight |
| **Payday overlay** (spec 09 §2.9; Menus & Run Flow) | wallet, top bar, toasts, banners, feed | compass and hotbar dimmed (plates only; text keeps contrast) |

*Pause:* multiplayer — HUD unchanged, run continues; solo — UI lifetimes freeze (the UI clock follows the Sim clock).

*Preference — **Essentials** (on / off, M1):* hides the feed and hides hotbar, HP and weight after 4 s idle (they return on change). It **never** hides tier A (prompt and ring, P0 banners, knocked-down block, captions), the timer, wallet, own pills or own verdict toasts (H3). The former Hidden mode and HUD-opacity reductions of text are cut.

**H21 — History log [M1].** The last `historyMax` = 50 raw toasts, feed lines, banners and captions (plus comms echoes routed by Comms; never free chat text), each with a shift-clock timestamp and name / hat snapshot. Essentials never stops an event reaching history. Non-pausing overlay; filter by **category only** (no per-actor filter, anti-goal 2); unread badge on the toast stack; reached from the "+N" chip, KBM **L** or Pause › History (no default pad bind — View is Map; Input & Controls). It is the untimed equivalent for WCAG 2.2.1.

**H22 — (cut.)** Per-category mutes are cut [D, CD]; per-player mutes are Comms'.

**H23 — Text sizes, scale and colour [D].**
- **Units:** all HUD sizes are **1080-reference px**, scaled by window height / 1080 (18 ref px = 18 px at 1080p, 24 at 1440p, 36 at 2160p). The stretch / scale mechanism → ADR (HN-18). Minimum window 1280×720.
- **Floors:** reading text (prompt, toasts, feed, captions) ≥ 18 ref px at 100 %; timer digits ≥ 28 with tabular figures; wallet and quota ≥ 20. The 14 px Micro tier is decoration only.
- **Scale:** 100–150 % for the whole HUD and **up to 200 % for reading text** (WCAG 1.4.4; spec 09 §5 amendment, HN-14). 200 % layout: 1 prompt, 1 toast, 3 feed lines; numerals wrap, never truncate. Compact layout (small windows) → M4 (seed §1).
- **Contrast (normative):** text ≥ 4.5 : 1 (≥ 3 : 1 at ≥ 18.66 px Bold or 24 px), non-text UI and outlines of interactive elements ≥ 3 : 1, against their plate. Corrected palette pairs: seed header.
- **Colour is never the only channel:** quota — numeral + ✓; complaint — numeral + ticks; pills — sign + ✓ / ✗; timer — icon + outline; compass bands — numeral; rival magenta — rival icon.

**H24 — moved to Comms** (ingress sanitisation; HUD calls it for display names, H9).

**H25 — The UI event log.** Every pipeline decision is appended to a log shared with Grid Inventory's H-UI-1 and the Input router. Record kinds: `Enqueue`, `Merge`, `Drop`, `Show`, `Expire`, `Fade`, `Gone`, `Evict`, `Announce`, `Revert` (a predicted item replaced or removed by its authoritative event, H30), `GlyphSwap`. Each record carries `cueId`, `mergeKey`, `actorId`, `args`, `queueDepth`, `dropReason` and the localisation key; tests assert keys and args. M-1a: a plain list; M0: a 1024-record ring allocated once (seed §5).

**H26 — Live-region mirror [schema M-1a, announcer M4] [D].** Every notification, prompt reason, banner and caption is also announced as text.
- **Two dedicated Controls**, one assertive and one polite (`Control.accessibility_live` is typed `AccessibilityServer.AccessibilityLiveMode` in 4.7 — VERIFIED; whether an invisible Control survives the AccessKit tree is VERIFY-IN-ENGINE, an M4 spike).
- **Spoken text** comes from the cue's `spokenKey`, not the rendered string.
- **Assertive:** P0 items, the timer reaching 15 s and a prompt turning ✗ (Manual Delivery). **Polite:** everything else, digested per category after `announceDigestMs` = 1000 ms (F4).
- **Never stale:** polite items older than `announceMaxAgeMs` = 10000 when they would be spoken collapse into "N earlier updates". Nothing is dropped from **history** (adopted by Input & Controls). **State lines** (prompt branch, aim label — Manual Delivery, Throwing) are superseded, never queued; outcome cues are never dropped.
- **Status action** (KBM **I**, Pause › Status; Input & Controls): announces phase, time left, quota, wallet and complaint, then the focused prompt's live sentence; the timer never announces every second.
- Tier policy (v1 = announcements only; store statement) → Accessibility GDD (seed §3).

**H27 — moved to seed §4** (UI sound rate limits, proposals for audio-director). The HUD emits `rateClass` per cue; a merged toast emits one sound cue; own rejections carry `alwaysPlay`.

**H28 — The flash governor [D, design-review].** One governor (F2) for the HUD enforces **≤ 3 flashes in any rolling 1 s** (WCAG 2.3.1) and **≤ 2 Hz per governed element**.
- **What is a flash:** a **pair of opposing luminance changes** on one element (or one effect) within 1 s whose area is ≥ `flashAreaMinPermille` = 100 (10 % of the screen — conservative against WCAG's ≈ 11 % small-area threshold). Governed: banners, the raid fill and burst, full-width effects, any element over the threshold. Small cut-ins, slides and fades (toasts, pills, feed rows) are **not** flashes and are never denied — so Reduced Motion's 0.1 s cut and Reduced Flash coexist.
- **Raid pulse** (≤ 1 Hz, 100 → 80 % opacity) reserves 1 token and runs on one shared pulse clock locked to raid start.
- **Denied** requests fall back to a static thickness change. **No saturated-red flash ever:** Stamp Red appears only as a held fill or the ≤ 1 Hz pulse.
- **Reduced Flash** (separate from Reduced Motion) sets the governed budget to 0: the raid timer is a static red fill with a thicker outline; no burst.
- **Animation driver:** one governor-owned animator drives every HUD tween and its reduced-motion path; widgets never create their own.
- **Cross-system:** world and post-process effects (raid haze, knockdown desaturation) request tokens from this governor — a Presentation constraint from **M1** (HN-11).

**H29 — moved to seed §5** (frame hygiene: dirty flags, no per-frame allocation, pooling, no `DrawLine` strokes, budgets).

**H30 — `IHudState` and prediction reconciliation [D, design-review].**
- `IHudState` exposes **pull values** (wallet, `phase`, `phaseId`, `deadlineTick`, `shift`, `quota`, `shiftEarnings`, complaint value / max / thresholds, `backlogUnits`, `renderSimTimeMs` — from Simulation Core's `IHostClock`, exact Sim time on the host — and the roster) and **one ordered event stream** (by `sourceTick`, then `opId`). From M-1a the HUD reads only through it, with a loopback client in solo, so M0 is not a rewrite.
- **Predictions live only in the prompt** (Manual Delivery's "+N¢" / ✗). Pills and toasts come only from authoritative events. If an emitter ever tags a cue `predicted`, the authoritative cue with the same `opId` replaces it in place (`Revert` record if it differs or is rejected). An `opId` already rendered is never rendered twice.

### States and Transitions

**Notification lifecycle.**

| State | Entered when | Exits to |
|---|---|---|
| Emitted | an owner fires a cue (H2) | Merged (H8) · Queued (surface full) · Visible (slot free) · Drop (H6 overflow) |
| Queued | the surface is at its visible cap | Visible (slot frees; priority then age) · Evicted (H6 overflow; never P1) |
| Visible | it takes a slot | Merged (lifetime restarts, capped) · Fading (lifetime ends) · Evicted (own-rejection replace, banner pre-emption) |
| Fading | lifetime ends | Gone after 0.25 s (0.1 s cut under Reduced Motion); **no merge** |
| Evicted | overflow or replacement | Gone; "+N" chip incremented if it was a rejection |
| Gone | — | terminal; event log and history entries persist |

Every transition writes an event-log record (H25). P0 and P1 items have no Evicted edge from overflow.

**Timer display state.** Normal → Amber (≤ 60 s) → Red (≤ 15 s) → 00:00 at the deadline (never earlier). Raid adds the red fill and pulse. A change of `(phaseId, deadlineTick)` resets the state (F3 monotone guard).

### Interactions with Other Systems

| System | Data in (to HUD) | Data out | Interface owner |
|---|---|---|---|
| **Input & Controls** | bindings, glyphs, prompt grammar, hold-ring progress, disabled-verb reasons, History (L) / Status read (I) actions, the first-pad tip line, controller-disconnected event | live-region text for its rows | Input owns bindings and grammar |
| **Player Character** | focus anchor and bounds, generic `PromptData` and reason keys, `CarrierKnockedDown` / clock-in / on-break events and timers, pile positions and distance | — | Player Character owns strings |
| **Grid Inventory** | op replies (reason codes, `byActorId`, `cause`), cursor (held chip), own panel (hotbar), `P` | — | Grid owns the reason table |
| **Mail & Destinations** | `DeliveryOutcome` per op (incl. `destinationId`, `intendedAddress`), `backlogUnits`, Lost Parcel WorldItems, `rollCallNames` | — | Mail owns content |
| **Manual Delivery** | deliver `PromptData` (branch table: mark, addresses, predicted ±N¢, chips, reasons, live sentences) | — | Manual Delivery |
| **Throwing & Catching** | landing-marker + arc-label rect (protected), catch-prompt states, catch result and fumble cues | — | Throwing renders the arc and label |
| **Run Structure** (provisional) | `phase`, `phaseId`, `deadlineTick`, `shift`, `quota`, `shiftEarnings`, complaint, `ComplaintRaised`, `PhaseChanged` | — | Run Structure |
| **Economy** (provisional) | wallet, purchase pending / confirmed / rejected, the money format | — | Economy |
| **Defenses / Enemy AI / Health** (provisional, M3) | `ConstructUnderAttack`, `PODestroyed`, raid warning `{edgeBearing, etaTicks}`, Mega spawn, own HP | — | owners |
| **Comms** (frozen) | comms cues (log lines, markers), "Typing…" flag | MarkerLayer, compass ping class, name-tag slots, pipeline | Comms owns the model |
| **Networking & Session** | roster (slot → account id, name, hat, tint), host lost, join / leave | — | Networking |
| **Audio** | — | cue ids with `rateClass` | Audio owns mixing |
| **Onboarding & Tips** (M4) | tip text and triggers | tip-slot rules | Onboarding |
| **Accessibility** (M4) | tier, settings | live-region mirror, governor, floors | Accessibility owns the tier |
| **Menus & Run Flow** | Payday overlay state, map open state | — | screen owners |

## Formulas

*Specialist: systems-designer (2026-10-16); revised in design-review. **Every value is PROPOSED.***

**Conventions.** Durations and thresholds are `int` ms; absolute times `long`; percentages permille ints; pixel positions round half away from zero. The **UI clock** follows the Sim clock and freezes in a solo pause (F1, F8). The **real clock** never freezes (F2). Each peer computes F1–F8 locally from replicated integers; reproducibility under a fake clock on one machine is enough.

### F1 — Reading-time lifetime (toasts, feed lines, pills, captions)

`words = Σ over tokens [ ceil(2 × nNS / 5) + other ]`
`lifeMs = m × clamp(1500 + 400 × words, 2500, 8000) / 1000`
`feedLifeMs = max(6000, lifeMs)`
`expiresAt′ (on merge) = min(now + lifeMs(latest args), firstShownAt + toastMaxMs × m / 1000)`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| token | string | — | calculated | tokens of the **rendered, locale-resolved** string split on Unicode White_Space; only tokens with a letter or digit count (an em dash is 0); a player name is one token; icons and "×N" count 0 |
| nNS | int | 0+ | calculated | grapheme clusters from no-space scripts (Han, Kana, Thai, Lao, Khmer, Myanmar) |
| other | int | 0 / 1 | calculated | 1 if the token contains any other grapheme cluster |
| m | permille | {1000, 1500, 2000, 3000} | setting | duration setting (WCAG 2.2.1); also scales pills (2500 × m) and captions (3000 × m) |
| toastMaxMs | int | 8000 | data file | age cap for a merging toast |

**Output Range:** `lifeMs` 2500–8000 at 1× (ceiling at ≥ 17 words), 7500–24000 at 3×. Feed lines ≥ 6000 ms. A setting change applies to new items and merges only. Empty render → 2500. A missing localisation key is a load-time error.

**Example:** "Jules got it first" = 4 words → **3100 ms**; 1.5× 4650; 2× 6200; 3× 9300. Merges at 2, 4, 6 s restart expiry to 5.1, 7.1 and min(9.1, 8.0) = **8.0 s**. "ジュールズが先に受け取った" = 13 no-space clusters → ceil(26 / 5) = 6 → 3900 ms.

### F2 — Flash governor (sliding window)

`cap = reducedFlash ? 0 : 3 − raidPulseActive`
`grant(e, now) = cap > 0 ∧ |{ g ∈ log : now − g < 1000 }| < cap ∧ now − lastGrant[e] ≥ 500`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| log | long[] | ≤ 3 | calculated | real-clock times of granted governed flashes |
| raidPulseActive | int | 0 / 1 | replicated (phase) | reserves one flash per second for the raid pulse |
| reducedFlash | bool | — | setting | governed budget 0 |
| lastGrant[e] | long | ms | calculated | last grant for element e |
| flashWindowMs / flashMinGapMs / flashAreaMinPermille | int | 1000 / 500 / 100 | constant | window; per-element gap (≤ 2 Hz); governed-area threshold (H28) |

Only `flashClass = governed` requests query F2; a denied request returns `StaticThickness` and consumes no token. The raid pulse runs on its own clock and is not queried.

**Output Range:** governed cap 3, 2 during a raid, 0 under Reduced Flash; ≤ 3 per rolling second. A token bucket (capacity 3, refill 3/s) was rejected: it admits 6 in one second.

**Example:** cap 3 — A, B, C granted at 0 / 100 / 200; D at 300 denied; D at 1000 granted (1000 − 0 is not < 1000). Per-element: A at 0 granted, A at 400 denied by the 500 ms gap alone, A at 500 granted. Raid (cap 2): A at 0 and B at 300 granted; C at 600 denied; C at 1000 granted.

### F3 — Shift timer display

`remMs = max(0, deadlineMs − renderSimTimeMs)`, `deadlineMs = deadlineTick × 1000 / 30`
`disp = ceil(remMs / 1000)`; `if key == prevKey: disp = min(prevDisp, disp)`
`state = disp ≤ 15 ? Red : disp ≤ 60 ? Amber : Normal`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| renderSimTimeMs | long | ms | `IHudState`, fed by Simulation Core's `IHostClock` (Networking implements; host: exact Sim time) | the HUD runs **no clock of its own** — no slew, no step [D, design-review; Sim Core HB9]. On tick boundaries `disp` equals Sim Core F1 `displaySeconds` |
| deadlineTick | uint32 | — | replicated (Run Structure) | tick at which the phase ends |
| key | (phaseId, deadlineTick) | — | replicated | the monotone guard resets when it changes |
| timerAmberSeconds / timerRedSeconds | int | 60 / 15 | data file | spec 09 §2.4 |

**Output Range:** `disp` ≥ 0, and 0 only when `remMs == 0`; a 1–999 ms remainder shows 00:01, so **00:00 never appears early**. A backward step of the render clock within a key never raises the number; a forward step may skip seconds. Text is written only when `disp` changes.

**Example:** `deadlineTick` 4020 → 134000 ms; render 120300 → remMs 13700 → **disp 14, Red**.

### F4 — Live-region polite digest (M4)

`flush(c) at t0(c) + announceDigestMs`; `text = join(". ", dedupe(buffer[c]))`, split into announcements of ≤ `announceMaxChars` = 240 graphemes at item boundaries; items older than `announceMaxAgeMs` at flush collapse into "N earlier updates".

`t0(c)` = time of the first item since the last flush (not reset by later items). Same-mergeKey items collapse to "text, N times". Assertive items speak immediately and pre-empt polite speech; polite buffers keep their timers. **Output:** polite latency ≤ 1000 ms when the reader keeps up; never stale beyond 10 s; nothing truncated (overflow → consecutive announcements).

**Example:** items at 0 ("Jules delivered 12 letters on Elm St") and 700 ("Sam delivered 4 letters on Oak Row") flush at 1000 as one announcement. A raid warning at 500 speaks at 500.

### F5 — moved to Comms (sender courtesy limits).

### F6 — Compass mapping, declutter and marker caps

`b = wrap180(bearingDeg − yawDeg)` (range (−180°, +180°]; ±180° maps to +180°); `x = cx + (clamp(b, −120°, 120°) / 120°) × halfW`
`markerCount ≤ 16 pings + 8 piles + 8 Lost Parcels + (roster − 1) name tags + 1 prompt + 1 focus bracket = 41 at roster 8` (emote bubbles counted separately)

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| b | int (0.1°) | −1800…1800 | calculated | relative bearing |
| cx, halfW | int ref px | 640 wide (halfW 320) | layout | strip centre and half-width |
| minSpacingPx | int | 28 × textScale | constant | minimum icon spacing |
| distLabel | int m | rounded to 10 m | calculated | teammate icon distance label (3D distance) |

**Declutter:** icons are placed greedily by class — raid edge > construct under attack > ping > teammate > blocked-machine badge — within a class by world distance, icons placed last frame first; an icon is placed only if ≥ `minSpacingPx` from every placed icon. On collision a same-class construct, teammate or badge merges into the winner as "+N" (the winner keeps its distance label); a lower class drops from the strip (its world marker and history remain); raid-edge icons are never dropped; of two colliding pings the nearer stays; edge-clamped icons merge per edge and class. Beyond the pile and Lost Parcel caps, the 8 nearest are kept.

**Output Range:** x ∈ [cx − halfW, cx + halfW]; at most ⌊width / minSpacingPx⌋ icons (22 at 100 %, 15 at 150 %).

**Example:** yaw 90°, ping at bearing 150° → b = 60° → **cx + 160**. b = 180° clamps to the right edge.

### F7 — Widget fill mappings

`weightFill‰ = 25 × min(P, 40)`
`quotaFill‰ = (quota > 0) ? clamp(1000 × shiftEarnings / quota, 0, 1000) : hidden` (computed in `long`); `surplus = max(0, shiftEarnings − quota)`
`complaintFill‰ = (complaintMax > 0) ? clamp(1000 × complaint / complaintMax, 0, 1000) : hidden`
`hpLow = 4 × hp ≤ maxHp`; `backlogTier = u == 0 ? 0 : u ≤ 40 ? 1 : u ≤ 160 ? 2 : 3`

Variables: `P` (Grid Inventory D1; speed floor at 40), `shiftEarnings` / `quota` int ¢ (Run Structure), `complaint` / `complaintMax` (Run Structure), `hp` / `maxHp` (Health & Damage), `u` = `backlogUnits` uint16 (thresholds are Mail's knobs). The 1.25 × quota track is cut [D, CD]: the bar fills to quota, then shows ✓ and "+surplus".

**Output Range:** all fills 0–1000‰; undefined quota or complaintMax hides the widget (never a division by zero); negative earnings clamp to 0.

**Example:** P = 26 → 650‰. 640 / 2214 → **289‰**. 2500 / 2214 → 1000‰, ✓, surplus 286. Complaint 30 / 120 → 250‰. u = 100 → tier 2.

### F8 — Merge predicate

`merge(e → i) ⇔ i.state ∈ {Queued, Visible} ∧ key(e) == key(i) ∧ inWindow(mode, W, e.t, i)`

| Variable | Type | Default | Description |
|---|---|---|---|
| toastMergeMs | int | 3000 | sliding from the item's last event |
| contentionMergeMs | int | 1000 | sliding; key `cueId` |
| pillMergeMs | int | 500 | sliding; key `(sign, own / team)` |
| feedExtendMs / feedCommitMs | int | 2000 / 8000 | `commitAt = min(open + 8000, lastEvent + 2000)` |
| misrouteWindowMs | int | 10000 | **tumbling** from the first event (Mail & Destinations) |

**Precedence:** exact key, then contention key, then a new item (H6). A continuous deliverer produces at most one new feed line per 8 s. **Example:** feed events at 0, 1.5, 3.4 and 9.0 s → one line committing at 5.4 s, then a new line at 9.0 s. The HUD frame budget (CPU ≤ 0.8 ms, ≤ 60 draws, VRAM) is PROPOSED and parked in seed §5 for the ADR.

## Edge Cases

*Comms edge cases moved to `comms.md`. Behaviour already stated in a rule is not repeated.*

- **Unknown cue id at runtime** (mismatched build or modded host): "Notice received." by audience (personal → toast, team → feed) at P2, to history and the live region, logged `Drop: unknown_cue`; at most one such toast per cue id per session; debug builds assert.
- **Quota 0 or undefined:** quota bar hidden; no ✓, no "quota met", no surplus. **complaintMax 0:** envelope hidden; `ComplaintRaised` still logged. Complaint over max: fill clamps, numeral shows the true value.
- **Wallet negative** (floor −500 ¢, registry `walletFloor`): rendered by the formatter; the floor outline flash is governed and a static thickness change under Reduced Flash.
- **Deadline changes mid-display:** the F3 key changes; the guard resets; Red back to Amber is legal; each threshold re-arms and announces once per key; a shortening below 15 s goes Red with one assertive announcement.
- **Duplicate `DeliveryOutcome`** (resend after reconnect): same `opId` renders nothing new (H30).
- **8 contention cues in one frame** (7 players + a construct): the first takes a slot or replaces the oldest visible P2 / P3; the rest merge by `cueId` into "**Sam** +6 got it first"; all raw cues reach history.
- **Text scale changes mid-toast:** visible toasts re-lay out with unchanged lifetimes; a drop from 2 slots to 1 evicts nothing (the excess drains by expiry). Same for the feed.
- **Active device flips mid-prompt:** glyphs swap in place — no lifetime restart, merge or re-announcement; live text names actions, not glyphs.
- **Knocked down under a visible banner:** both show (the block is outside the H7 queue).
- **Host lost:** the Host lost banner pre-empts everything; the timer freezes at its last value with no more threshold announcements; comms behaviour per Comms.
- **Disconnected teammate frozen on break** (120 s, Player Character): their name tag shows "· On break 1:43"; feed lines and history keep their snapshot name.
- **Landing marker sweeps across the toast lane mid-throw:** visible toasts move to the alternate anchor (≥ 0.25 s, cut under Reduced Motion) and return when it clears; lifetimes unchanged.
- **Solo (roster 1):** no team pills, no feed lines about others, no name tags; the feed still carries own misdeliveries from M0.

## Dependencies

**Sim / Presentation side (TD gate).** HUD lives entirely on the Presentation side; the plain-C# `PerformativeMail.Sim` assembly holds no reference to it. A plain-C# UI core (pipeline H2–H9, F1, F4, F8; governor F2; timer F3; event log H25), driven by injected `IUiClock` (UI and real), `IRoster` and `IHudState`, so every rule is xUnit-testable headless; a Godot view layer for CanvasLayers, MarkerLayer, widgets and the live-region Controls. Assembly placement → ADR.

**Upstream.**

| System | Type | Status | Interface | Bidirectional? |
|---|---|---|---|---|
| Input & Controls | Hard | In Review | bindings and glyphs; prompt grammar; hold ring and `ringDelay`; disabled-verb reasons; History / Status read actions; controller-disconnected event | yes |
| Player Character & Movement | Hard | In Review | focus anchor and bounds; generic `PromptData`; knockdown / clock-in / on-break; pile positions; vocabulary list | yes |
| Grid Inventory & Containers | Hard | In Review | reason codes and feedback table, `byActorId` / `cause`; cursor; own panel; `P` | yes |
| Mail & Destinations | Hard | In Review | `DeliveryOutcome` (R16, per-hold grouping); `backlogUnits`; Lost Parcel WorldItems; `rollCallNames` | yes |
| Manual Delivery | Hard | In Review | deliver `PromptData` (branch table, reason strip content) | yes |
| Throwing & Catching | Hard | In Review | landing-marker + arc-label rect; catch-prompt states; catch result cues | yes |
| Simulation Core | Soft (Presentation-only; no assembly reference) | In Design (provisional) | `IHostClock` for `renderSimTimeMs`; F1 `displaySeconds`; event batches via ClientWorld (HB9) | yes |
| Content & Data Definitions | Soft | In Design (provisional) | shared validator library for the cue table and `hud.tuning` (CV-HUD-01…05) | yes |
| Run Structure | Hard | **provisional** | `phase`, `phaseId`, `deadlineTick`, `shift`, `quota`, `shiftEarnings`, complaint fields, `ComplaintRaised`, `PhaseChanged`, `QuotaMet` | hand-off HN-5 |
| Economy, Wallet & Shop | Hard | **provisional** | wallet (int ¢, may reach `walletFloor`); purchase replies; money format | hand-off HN-6 |
| Defenses / Enemy AI / Health & Damage | Soft (M3) | provisional | construct, raid, Mega, HP events | hand-off HN-7 |
| Networking & Session | Hard (M0) | Not designed | roster, host lost, join / leave | hand-off HN-9 |
| Belt Logistics | Soft (M2) | Not designed | blocked-machine state; sorter overflow count | hand-off |
| World Generation | Soft | Not designed | address and district tables (feed street, compass bands) | — |

**Downstream.** Comms (Hard — MarkerLayer, compass ping class, name-tag slots, pipeline); Onboarding & Tips (Hard — tip slot); Accessibility (Hard — live region, governor, floors, history); Map & Address UI (Soft); Menus & Run Flow (Soft — Payday coexistence, settings screens); Audio (Soft — `rateClass`).

**Provisional assumptions.** (1) Run Structure replicates `deadlineTick` as an absolute Sim tick and emits `ComplaintRaised` per op. (2) The complaint meter has a finite max and named thresholds; if unbounded, the envelope shows a numeral only. (3) The wallet is one team-shared integer in cents, down to −500. (4) Purchases have a pending state. (5) Defenses emits `ConstructUnderAttack` ≤ 1 per construct per 5 s; the compass icon stays 5 s after the last event.

## Tuning Knobs

*All **PROPOSED**; HUD data file `hud.tuning` (hot-reload in debug) except **settings** (Profile & Settings Persistence). Comms knobs live in `comms.md`.*

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `toastVisibleMax` / `toastQueueCap` | 2 / 4 (M-1a 1 / 0) | 1–3 / 2–8 | toast lane | verdicts replace each other unread | stale verdicts; lane hits the protected zone | 200 % forces 1 |
| `toastBaseMs` / `toastPerWordMs` | 1500 / 400 | 1000–2000 / 300–500 | F1 | lines vanish unread | toasts queue | `toastMaxMs` |
| `toastMinMs` / `toastMaxMs` | 2500 / 8000 | 2000–3000 / 6000–10000 | F1 clamp, merge age cap | short lines flash past | merged toasts block the slot | `toastMergeMs` |
| `feedMinMs` | 6000 | 5000–8000 | feed floor (spec 09 §2.4) | team news missed | feed saturates | `feedVisibleMax` |
| `feedVisibleMax` | 5 | 3–6 | feed lines | news falls to history | covers the hotbar column | feed-pending 4 |
| `feedCrewThreshold` | 3 | 2–5 | crew-line collapse | names vanish into "Crew" | 8-player feed evicts before the floor | `feedVisibleMax` |
| `toastMergeMs` / `contentionMergeMs` / `pillMergeMs` | 3000 / 1000 / 500 | 1500–5000 / 500–2000 / 250–1000 | F8 | duplicates | unrelated events fused | keep `toastMergeMs` ≤ `toastMinMs` + ~1 s |
| `feedExtendMs` / `feedCommitMs` | 2000 / 8000 | 1000–3000 / 5000–12000 | feed coalescing | one line per delivery | a line never settles | `feedMinMs` |
| `bannerPhaseMs` / `bannerPendingTtlMs` | 3000 / 5000 | 2000–4000 / 3000–10000 | banners | missed phase / stale demotions | stale pending banner | priority |
| `flashWindowMs` / `flashCapPerWindow` / `flashMinGapMs` / `flashAreaMinPermille` | 1000 / 3 / 500 / 100 | **WCAG 2.3.1 — may only be tightened** | F2 | — | photosensitivity risk | raid reservation |
| `timerAmberSeconds` / `timerRedSeconds` | 60 / 15 | 30–90 / 10–30 | F3 states | warning too late | constant alarm | assertive at Red |
| `announceDigestMs` / `announceMaxAgeMs` | 1000 / 10000 | 500–2000 / 5000–20000 | F4 | announcement spam | lag / stale speech | — |
| `nameTagRangeM` | 10 | 6–20 | name tags | anonymous teammates | marker budget | F6 |
| `nameDisplayMax` | 12 graphemes | 10–16 | truncation | unreadable names | row wrap | layout |
| `historyMax` | 50 | 30–100 | history ring | events lost | long scroll | — |
| `minSpacingPx` | 28 × textScale | 24–40 | compass declutter | overlap | over-dropping | strip width |
| `toastDuration` (setting) | 1× | 1× / 1.5× / 2× / 3× | F1 m (toasts, feed, pills, captions) | — | — | — |
| `textScale` (setting) | 100 % | 100–150 % HUD, 100–200 % reading text | H23 | — | layout | 200 % rules |
| `reducedFlash`, `reducedMotion`, `essentials` (settings) | off | — | H20, H28 | — | — | F2 |

**Owned elsewhere (read, not duplicated):** backlog tiers 40 / 160 pieces (Mail); `pendingDebounceMs` (Grid Inventory); `ringDelay`, `holdSetting` (Input & Controls); misroute window 10 s (Mail); `respawnSeconds` (Run Structure / Player Character); all comms knobs (Comms).

## Visual/Audio Requirements

*REQUIRED for UI. Art bible §1, §2, §4, §5, §7 lock the visual language. Full per-widget styling, the feedback-per-event table, render constraints, audio intent and the 6 PROPOSED art-bible amendments are parked in `design/ux/hud-notifications-seed.md` §2 for the art-director (HN-15).*

**Normative rules kept here:**
- **Text colour:** Chalk White on Charcoal. Stamp Red and Rival Magenta are fills behind text, never text. **Gold fills never carry Chalk text** (≈ 1.7 : 1); Gold is text, glyph or outline on Charcoal. Go Green fills carry **Charcoal** numerals (Chalk on Go Green ≈ 2.1 : 1 fails).
- **Contrast** per H23; interactive outlines ≥ 3 : 1 (the #5A5E6B outline fails on Charcoal — art-director to lighten, HN-15).
- **Identity:** hat icon → Bold name → 4 px tint strap in every attributed row; tint is never text or a type code.
- **Outlines** are filled shapes or StyleBox borders ≥ 2 ref px — never `DrawLine` (4.7 CanvasItem lines lost antialias feathering, sourced in `breaking-changes.md`).
- **Never on HUD widgets:** perforation; district colours except compass bands and address labels; Rival Magenta on anything a player authors.
- **What never moves:** timer digits, compass bands, quota outline, address text, name tags, HP and weight outlines, the hotbar frame. No idle HUD motion during Delivery (bible §2). All motion follows the bible (squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s) through the governor's animator (H28).
- **Exempt from post-processing:** every HUD layer composites above the raid dim, haze and Run Over desaturation (VERIFY-IN-ENGINE; seed §2 render constraints).

📌 **Asset Spec** — after the art bible amendments land, run `/asset-spec system:hud-notifications` (widget sheet, hold ring / slash / bracket, banner bands and burst, plates, UI cues).

## Game Feel

*References: Overcooked 2's order rail — transient, never in the way of the counter. Apex's ping and DRG's callouts inform Comms.*

| Event | Target | How measured |
|---|---|---|
| Own action reply (op reply, `DeliveryOutcome`) → pill or toast | **the same rendered frame** the event is processed (H6 step 3 never queues the newest own rejection behind older toasts) | event log vs frame index |
| Timer | text changes only on whole-second boundaries; never runs backward within a key (F3) | Logic test |
| Hold ring | Input & Controls' `ringDelay` and progress exactly; HUD adds no lag | I&C fixtures |

**Weight and snap.**
- **Outcome beats land after the world:** the pill's squash starts no earlier than the outcome event that triggers the flag pop (Mail & Destinations Game Feel). The HUD never runs ahead of the world.
- **Persistent widgets are quiet:** one bump per change, digits dead still; the eye learns the layout by shift 2.
- **The loudest HUD moment is the raid banner:** one burst, one scale-in, one horn. Second-loudest is the complaint shake — one per op, never per unit.

**Feel targets.** (GF-1) own-rejection toast on the reply's frame (H-QUEUE-2); (GF-2) an 8-player shift-5 recording never shows more than 5 feed lines + 2 toasts + 3 pills, and every visible P3 line lives ≥ 6 s (H-FEED-1); (GF-3) ≤ 3 governed flashes in every rolling second during a scripted 8-player flood (H-F2-1); (GF-4) no toast or feed rect over a protected rect (H-ZONE-1).

## UI Requirements

*This section fixes **screen rules**; screens are specified by `/ux-design`. The full PROPOSED layout table, coexistence notes, settings list, history overlay and streaming notes are parked in seed §1.*

- **Safe area:** 48 ref px sides, 32 top and bottom; ultrawide anchors to a centred 16:9 frame.
- **Zones:** top bar (timer, complaint; compass top-centre; quota, wallet, pills, backlog top-right) never gives way; the **central band** (y 14–67 %) is reserved for panels and overlays — no persistent widget; the prompt and the toast lane under it follow H11; the **event feed is right** and chat left [D, deviation from spec 09 §2.4's bottom-left feed, which collides with chat and the own inventory panel].
- **Coexistence:** transient layers (pills, toasts, banners) draw above panels except over a focused control (H11); the map draws under the top bar, feed, toasts and banners.
- **Settings consumed** (screens: Menus & Run Flow; persistence: Profile & Settings): duration (1× / 1.5× / 2× / 3×), text scale, Essentials, Reduced Motion, Reduced Flash, caption size and background, hide name tags.

> **📌 UX Flag — HUD & Notifications**: run `/ux-design` before writing epics, for the in-game HUD layout (zones, 200 % variant, context modes, Essentials), the notification stack (toasts, feed, pills, banners, history overlay), the interaction prompt and hold ring (shared with Input & Controls, Player Character, Manual Delivery), the compass, and the knocked-down block. Seed: `design/ux/hud-notifications-seed.md` §1. Stories cite `design/ux/[screen].md`, not this GDD.

## Network

HUD & Notifications **replicates nothing**. Everything it shows is read from other systems' replicated state or events, whose Network sections own them. The comms messages (`ChatMessage`, `Ping`, `Emote`, `PingPurge`), channel 3, the host relay and JoinState comms block moved to `design/gdd/comms.md`.

## Cross-References

| Document | Sections used | Takes / gives | Status |
|---|---|---|---|
| `design/gdd/input-controls.md` | R2–R10, G9, F9, F12, control table, OQ-IC-11/12/19 | prompt grammar, glyphs, History / Status binds; **answers** OQ-IC-11 and OQ-IC-12 | In Review |
| `design/gdd/player-character-movement.md` | P10–P11, P15–P16, UI Requirements, OQ28 | focus anchor, `PromptData`, knockdown block, vocabulary | In Review |
| `design/gdd/grid-inventory.md` | R7, R11, feedback table, D1, OQ18, OQ19, OQ25 | toast content, held chip; **answers** OQ25 | In Review |
| `design/gdd/mail-destinations.md` | R16, Feel & Comedy, Visual/Audio, M-FT-3 | pills (per-hold grouping), feed, backlog chip, Lost Parcel, `rollCallNames` | In Review |
| `design/gdd/manual-delivery.md` | MD2–MD7, UI Requirements branch table, OQ5 | deliver prompt body, reason strip | In Review |
| `design/gdd/throwing-catching.md` | UI Requirements (arc label, prompt slot, live region) | protected rect, catch cues | In Review |
| `design/gdd/simulation-core.md`, `content-data-definitions.md` | F1, `IHostClock`, HB9; CV-HUD-01…05 | timer clock; cue-table validation | In Design |
| `design/gdd/comms.md` | all | sibling split from this GDD | Draft (frozen) |
| `design/ux/hud-notifications-seed.md` | §1–§5 | parked layout, art, accessibility, audio, ADR material | unreviewed seed |
| `design/art/art-bible.md` | §1, §2, §5, §7, §8 | visual language (locked); amendments HN-15 | approved |
| `design/spec/09-ui-ux.md` | §1, §2.4, §2.9, §3.5, §5, §6 | HUD contents; deviations: feed right; 200 % reading text | spec |
| `design/spec/12-milestones.md` | M-1a "Minimal HUD" | M-1a scope | spec |
| `docs/engine-reference/godot/breaking-changes.md` | `accessibility_live` type, CanvasItem lines, stretch defaults | sourced engine facts | verified 2026-10-15 |
| `design/registry/entities.yaml` | `walletFloor`, `weightSpeed`, `holdEff`, `inputBindingRules` | locked values | — |

## Acceptance Criteria

*From qa-lead (2026-10-16), rewritten in design-review. Comms ACs (H-PING, H-WHEEL, H-CHAT, H-EMOTE, H-SAN, H-NET, H-FT-1) moved to `comms.md`; H-A11Y-1/2/4 and H-PERF-1 to the seed.*

**Tags:** **[Logic]** xUnit on the UI core with a fake `IUiClock` (UI and real clocks advanced separately) and stub `IRoster` / `IHudState` / `IMoneyFormatter`; **[Integration]** Godot headless through the adapter; **[UI]** Godot test with a retained screenshot — **ADVISORY until a render rig exists**; **[MANUAL]** tester on a build, evidence in `production/qa/evidence/`.
**Default fixture:** 30 Hz Sim, roster 8 (local Sam slot 0, Jules slot 1), `en`, 1920×1080, text 100 %, duration 1×; letter 8¢, misdelivery penalty 4¢. Logic and Integration are BLOCKING (`qa.level` unset in `project.yaml` — confirm). Parametrised rows report separately.

**M-1a set (17):** H-ROUTE-1, H-ROUTE-2 (a, b, d-pill), H-TOAST-1, H-MERGE-1, H-MERGE-3 (own rows), H-F1-1 (1×), H-F1-2 (en), H-F3-1 (time subset), H-WIDGET-1 (M-1a row), H-WIDGET-2, H-PROMPT-1, H-INT-1 (pill / opId rows), H-INT-2 (own rows), H-INT-3, H-EC-1, H-FT-2b, H-ZONE-1 (1080p / 100 %).

### Routing and pipeline
- **H-ROUTE-1 [Logic] M-1a** GIVEN the cue table, WHEN validated at load, THEN a cue with no localisation key fails naming the cueId; a non-id / non-int `args` value fails naming the arg; the schema has no surface field; a cue whose display text contains ¢ × − ✓ → · without a `spokenKey` fails; every row has `politeness`, `rateClass`, `captionClass`, `flashClass`; every outcome cue has a world-cue id. (M4) one cue renders in two locales.
- **H-ROUTE-2 [Logic, parametrised] M-1a a, b, d-pill · M0 c, d-feed · M1 e · M3 f** (a) own accept +8¢ → own Pill only; (b) own `stale_entry` → Toast; (c) Jules accept → Feed + team Pill; (d) own misdeliver → own Pill + Feed, no Toast; (e) phase change → Banner; (f) raid horn → Caption.
- **H-ROUTE-4 [Logic] M1** GIVEN Essentials on, THEN an own rejection toast, an own pill, the prompt, timer, wallet and a P0 banner all render; a Jules feed line does not render and its history entry exists.
- **H-ROUTE-6 [Logic, CI lint] M0** GIVEN every `en` source string rendered by the HUD, THEN none contains a banned word (Player Character's Vocabulary stems: die / dying / dead / death, kill, slain, corpse, revive, respawn, drown, "game over"; case-insensitive) or "!". Allowlist: "dead letter(s)" (two-word match; CD ruling). A planted banned string fails.

### Toast lane (H6)
- **H-TOAST-1 [Logic] M-1a** GIVEN toast A visible, WHEN a non-merging toast B arrives, THEN B is visible in the same frame, A logs `Evict`, and exactly one toast is visible.
- **H-QUEUE-1 [Logic, parametrised] M0** GIVEN 2 visible toasts and the queue [P2 a, P3 b, P2 c, P1 d] (all non-own), THEN (1) non-own P2 e evicts b; a following non-own P2 f evicts a; both log `Evict`; d is never evicted; (2) an own-rejection P2 g replaces the oldest visible toast by `firstShownAt` in the same frame and the queue is unchanged; (3) a P3 h arriving at a full queue holding only P2 / P1 items goes to history as `Drop: overflow`; (4) the "+N" chip counts only evicted rejections.
- **H-QUEUE-2 [Logic, parametrised] M0** GIVEN an own-rejection P2, THEN (a) both visible P2 → replaces the oldest by `firstShownAt` in the same frame, replaced → history; (b) 1-slot layout showing a P1 → queues at the head; (c) 2 slots both P1 → queues at the head; (d) a P0 cue never becomes a toast.
- **H-QUEUE-3 [Logic] M0** GIVEN "Jules got it first" (3100 ms) shown at 0 and merged at 2000, THEN the log reads Enqueue / Show @0, Merge @2000, Expire @5100, Fade, Gone @5350 (@5200 under Reduced Motion); history entries persist.

### Merge, feed and banners (H7–H8)
- **H-MERGE-1 [Logic] M-1a** GIVEN own `cant_drop_swimming` toasts (3100 ms) at 0, 2900 and 5800, THEN one item reads "×3" and expires at 8000; a 4th at 9000 is a new item; with a single prior event at t, an event at t + 3000 merges and t + 3001 does not; a Fading or Gone item never merges; the ×3 item emitted exactly one sound cue.
- **H-MERGE-2 [Logic] M0** GIVEN contention winners A @0, B @400, C @900, THEN one toast names C with "+2". Seven distinct winners in one frame give one toast "+6" and 7 history entries. The exact key is tried before the contention key.
- **H-MERGE-3 [Logic] M-1a own · M0 team** GIVEN own +8¢ outcomes at 0, 400, 800, THEN one own pill is rendered from `IMoneyFormatter(+24)` with "×3"; one hand hold's 3 outcomes in one tick at one door render one pill "+N¢ · 3 stacks" (no "×3"); an own −4¢ is a separate pill; (M0) teammate and feeder gains merge into one team pill; a 4th distinct pill folds into the oldest team pill and the own slot never folds; pill lifetime 2500 ms at 1×.
- **H-MERGE-4 [Logic] M0** Feed events for Jules at 0 (Elm St), 1500, 3400 (Oak Row) and 9000 give one line committed at 5400 reading "on Oak Row", then a new line at 9000; a 1/s deliverer for 20 s opens lines at 0, 9000 and 18000. Belt misroute to door 17 at 0, 4000 and 9900 gives 1 line; 10100 a new one. One `ComplaintRaised` op covering 20 units gives 1 shake.
- **H-FEED-1 [Logic] M0** GIVEN the Max-8P fixture (7 teammates delivering 1/s, one misdelivery every 10 s), THEN routine lines collapse into one crew line, misdelivery lines never collapse, every visible P3 line lives ≥ 6000 ms, and ≤ 5 lines are visible. GIVEN all 5 visible lines P0 / P1 and a P2 arrival, THEN it waits in feed-pending (cap 4) and enters when a line expires.
- **H-BANNER-1 [Logic, parametrised: 10 class pairs × both orders] M1 (controller M0, raid M3)** THEN a higher class pre-empts and the pre-empted banner goes to feed + history; a phase banner never waits pending (phase + raid on one tick → only the raid shows); a same-class pending replaces the older pending; a pending banner older than 5000 ms is demoted to feed + history; durations: phase 3000, raid pinned 15000, controller until reconnect / dismissal, host lost until acknowledged; only raid and Mega emit the burst.

### Attribution, widgets, prompt, zones, markers (H9–H12)
- **H-ATTR-1 [Logic, parametrised] M0** "Bartholomew1" (12) unchanged; "Bartholomew Jr" → "Bartholomew…"; a family-emoji cluster counts 1; empty / whitespace / fully stripped name in slot 0 → "Carrier 1"; unknown id → "Someone"; "Sam" / "sam" / "SAM" in slots 2 / 5 / 6 → "Sam" / "sam 2" / "SAM 3"; "Bartholomew Sr" / "bartholomew sr" → "Bartholomew…" / "bartholomew… 2".
- **H-ATTR-2 [Logic] M0** GIVEN a toast naming slot 3, WHEN slot 3 leaves, THEN it renders "Someone"; history rows keep the receipt-time name and hat after a new player reuses slot 3.
- **H-ATTR-3 [Logic] M0** Constructs render "The inserter" / "Belt 3" / "Truck 2", never the builder. A static scan of the cue schema, widget bindings and history filters finds no per-actor money total, misdelivery count or per-actor filter. With `rollCallNames` = "A carrier" or Off, a misdelivery feed line reads "A carrier posted 2 letters for 9 Oak Row to 17 Elm Lane."
- **H-WIDGET-1 [Logic] M-1a…M3** `WidgetSet(milestone)`: M-1a = prompt, focus bracket, wallet, own pills, held chip, hotbar frame, one toast slot, time-only timer; M0 adds feed, team pills, name tags; M1 quota, complaint, compass, weight, backlog, history, banners, knocked-down block; M2 sorter counter, blocked badge; M3 HP, raid / construct icons; nothing early.
- **H-WIDGET-2 [Logic] M-1a** Every wallet and pill string is produced by one `IMoneyFormatter` call site (fake formatter records `(−500)` for the floor; a lint finds no currency literal or "¢" in HUD view code). (M1) a pending purchase is greyed until confirmed; the floor outline request goes through the governor.
- **H-WIDGET-3 [UI, ADVISORY] M1** Greyscale screenshots: timer Normal / Amber / Red distinguishable by icon and outline; hotbar selection thickness-only; held chip shows numeral, house number, count and skull.
- **H-PROMPT-1 [Logic + UI] M-1a** ≤ 2 prompts; panel open → "Close panel to deliver" in the panel footer, ring hidden; a 0.4 s hold shows the ring at 0.10 s with progress 3/12; for each Manual Delivery branch-table row the mark leads line 1 and the prompt has ≤ 4 rows; a cancel reason ("You switched pieces", 3 words) shows in the strip below the live lines for 2700 ms at 1× while the live branch stays pressable, and a focus change ends it early; at 200 % one prompt; a MISDELIVER_ONE body shows ✗ and "Posts 1 · −N¢" and is never hidden; an anchor behind the camera leaves the prompt in place with no bracket.
- **H-ZONE-1 [Logic, geometry] M-1a 1080p / 100 % · M1 full sweep** GIVEN layout rects at 1920×1080 (M1: also 1280×720, 2560×1440, 3840×2160) and text 100 % (M1: 150 / 200 %), with the landing marker and bracket rect swept over a 9×5 grid, THEN the prompt rect and toast lane never intersect; no toast or feed rect intersects the landing marker, bracket or prompt (alternate anchor used); any marker rect intersecting them has target opacity ≤ 25 % reached by a ≥ 250 ms fade; with a focusable overlay open, no notification rect intersects its focus rect.
- **H-MARKER-1 [Logic] M1** GIVEN 16 pings, 8 piles, 8 Lost Parcels, 6 teammates within 10 m and 1 at 10.1 m, prompt and bracket, THEN exactly 41 markers are projected and the 10.1 m teammate has no name tag; emote bubbles are counted outside the 41; an anchor behind the camera draws no marker and keeps its compass echo; the landing marker and prompt layers are above the MarkerLayer; a 9th pile beacon keeps the 8 nearest.
- **H-TAG-1 [Logic] M0 (speaking clause BLOCKED on Proximity Voice)** Name tags show hat, name, strap and the suffixes supplied by Player Character ("· On break 1:43") and Comms ("Typing…"); the speaking glyph is shown or hidden from `IVoiceState`, never animated.

### Modes, history, accessibility, log (H20–H30)
- **H-MODE-1 [Logic, parametrised] M1** Context precedence Knocked down > Map > Panel > Payday > Normal; each context's visible set equals its H20 row; Essentials composes with every context and never hides tier A, timer, wallet, own pills or own verdicts; solo pause freezes UI-clock lifetimes, multiplayer pause does not.
- **H-SCALE-1 [Logic] M1** 18 ref px renders 18 / 24 / 36 px at 1080 / 1440 / 2160 height; at 200 % reading text the layout is 1 prompt, 1 toast, 3 feed lines, and numerals wrap, never truncate.
- **H-HIST-1 [Logic] M1** 51 raw events → the oldest ages out of the 50 ring; the badge counts entries present; entries carry shift-clock timestamps and snapshots; filters are by category only; Essentials-hidden events are stored.
- **H-A11Y-3 [Logic + Integration] M4** P0 items, the 15 s timer mark and a prompt turning ✗ go to the assertive Control immediately; a newer prompt-state line replaces a pending one while outcome cues are all spoken; others to the polite Control, digested; announced text equals the `spokenKey` rendering, not the display string; a polite item older than 10000 ms at flush is folded into "N earlier updates" while history keeps it; the status action announces phase, time, quota, wallet, complaint; the view test asserts `accessibility_live` on both Controls (behaviour VERIFY-IN-ENGINE).
- **H-LOG-1 [Logic] M0** The H-QUEUE-3 scenario produces the golden sequence in a 1024-record ring allocated once; each record carries `cueId`, `mergeKey`, `actorId`, `args`, `queueDepth`, `dropReason`, localisation key; a predicted cue replaced by a differing authoritative cue logs `Revert`; a device flip logs `GlyphSwap`.

### Formulas
- **H-F1-1 [Logic] M-1a 1× · M1 settings** "Jules got it first" → 3100 ms; merges at 2000 / 4000 / 6000 restart expiry to 5100 / 7100 / 8000. (M1) 1.5× 4650, 2× 6200, 3× 9300; the same text as a feed line lasts 6000 at 1× and 9300 at 3×; a setting change does not alter existing items.
- **H-F1-2 [Logic, parametrised] M-1a en · M4 locales** words 0 → 2500; 2 → 2500; 16 → 7900; 17 → 8000; "Make space — Sort?" → 2700 (em dash 0); a name counts 1; icons and "×N" count 0. (M4) "ジュールズが先に受け取った" → 3900; Thai 40 clusters → 7900.
- **H-F2-1 [Logic, real clock] M1 (raid M3)** Cap 3: A, B, C granted at 0 / 100 / 200; D denied at 300 and 999 (StaticThickness, no token); D granted at 1000. Per-element: A @0 granted, A @400 denied (gap only, log size 1), A @500 granted. Raid cap 2: A @0, B @300 granted; C @600 denied; C @1000 granted. Reduced Flash denies all governed requests while a toast cut-in (`flashClass` none) still shows. Freezing the UI clock does not stop ageing. Lint: no `CreateTween` / `AnimationPlayer` outside the governor's animator folder; the raid pulse uses no log token.
- **H-F3-1 [Logic] M-1a time subset · M1 states** remMs 1 → "00:01"; "00:00" only at 0; 134 s → "02:14"; (M1) `deadlineTick` 4020, render 120300 → 14, Red; 60000 → 60 Amber; 60001 → 61 Normal; 15000 → 15 Red; 15001 → 16 Amber; a same-key backward render step never raises disp; a key change (deadline +30 s) may raise disp and Red → Amber, thresholds re-arm and announce once; a shortening to 10 s goes Red with one assertive announcement; a frozen render clock leaves disp unchanged.
- **H-F4-1 [Logic] M4** Items at 0 and 700 flush at 1000 as one announcement; an item at 1100 flushes at 2100; an assertive cue at 500 speaks at 500 and polite timers are kept; three 100-grapheme items give two announcements, nothing truncated.
- **H-F6-1 [Logic] M1** Yaw 90°, ping at 150° → b = 60°, x = cx + 160; b = 180° and −180° → cx + 320; the strip holds 22 icons at 100 % and 15 at 150 %; a teammate 143 m away is labelled "140 m".
- **H-F6-2 [Logic] M1 (raid / construct M3)** A teammate icon 20 px from a ping is dropped from the strip while its world marker and history remain; two teammates 20 px apart merge "+1"; two colliding pings keep the nearer; a raid-edge icon within 28 px of a ping is kept and the ping dropped.
- **H-F7-1 [Logic, parametrised] M1 (HP M3)** Weight P 0 / 26 / 40 / 55 → 0 / 650 / 1000 / 1000‰. Quota 640 / 2214 → 289‰; 2214 → 1000‰ with ✓; 2500 → 1000‰ with surplus 286 via the formatter; 3,000,000 / 1,000,000 → 1000‰ (computed in `long`); quota 0 → hidden, never met. Complaint 30 / 120 → 250‰; 150 / 120 → 1000‰ with numeral 150; complaintMax 0 → hidden. HP 25 / 100 low, 26 / 100 not. Backlog u 0 / 1 / 40 / 41 / 160 / 161 → tier 0 / 1 / 1 / 2 / 2 / 3; tier 0 hides the chip; tier 3 uses the Stamp Red chip.

### Cross-system
- **H-INT-1 [Integration] M-1a pill / opId · M0 feed** GIVEN `DeliveryOutcome` fixtures, THEN own accept of 3 letters → one own pill from the formatter(+24), no feed line; the same `opId` delivered twice → no second pill; (M0) Jules accept of 12 at Elm St → feed "Jules delivered 12 letters on Elm St" + team pill; own misdeliver of 2 → own pill (−8) + misdelivery feed line, no toast; feeder → team pill per second + feed "Belt 3 …"; a zero-unit `stoppedAtFloor` → one REFUSED cue and no pill; every outcome cue has a paired world-cue id.
- **H-INT-2 [Logic, table-driven] M-1a own rows · M0 named rows** For each Grid Inventory feedback-table toast row (one row per outcome, HN-3 closed), the cue maps to (surface, severity, lifeMs under F1), the log records key and args, and a history entry exists (M1). Expected lifetimes: 2500 "Make space", "Nothing held", "Mail only", "Can't rotate", "Can't tidy"; 2700 "No room there", "Stack is full", "Out of reach", "Nothing to take", "Bag full — fumbled" (the fumble cue), "**Jules** broke it", "Make space — Sort?" (P1); 3100 "**Jules** got it first", "Won't go in here", "Can't drop while swimming", "Too bulky to carry"; 3500 "The inserter got it first", "Slot 1 is your hands", "Someone sorted this — try again", "Hands full — place it first", "The stack changed — pick again", "Stack of 1 can't split"; 3900 "Lost track of you — try again"; 4300 "Got 4 of 10 — **Jules** took 6", "Took 9 of 10 — 1 didn't fit"; 5100 "Delivered 3 of 4 — 1 won't fit this box", "Tidied by size — no room to sort by street". Prompt rows (no toast): "Put it away first", "Close panel to deliver", "Can't afford a wrong house", "Nothing for this address". `bad_request` → log line only.
- **H-INT-3 [Logic] M-1a** GIVEN a "Hold [E] Deliver" prompt 1 s in, WHEN the device flips to pad, THEN one `GlyphSwap` record, no Enqueue / Merge / Show / Announce, no lifetime restart, unchanged spoken text.
- **H-INT-4 [Logic] M1** GIVEN a local `CarrierKnockedDown` with a 10 s clock-in, THEN at +90 ticks the block reads "Clocking back in… 7" with the pile beacon "Your pile · 27 s · 80 m"; at +180, "4" and "24 s"; hotbar, held chip, HP and weight hidden; a simultaneous raid banner leaves the block visible; it is assertive; teammates' feeds read "Sam was knocked down by a Runner · Elm St".
- **H-EC-1 [Logic] M-1a** An unknown runtime cue id shows "Notice received." (P2, by audience), logs `Drop: unknown_cue`, reaches the event log; a second occurrence in the session gives no second toast.

### Fantasy and performance
- **H-FT-2a [MANUAL, playtest] M1** ≥ 5 testers, 2 s glimpse of a scripted 8-player HUD with an answer key: ≥ 90 % correct on "on quota pace?" (ground truth `shiftEarnings / quota ≥ elapsed / phaseDuration`), "time left ± 30 s?" and "nearest teammate direction and distance ± 50 m?".
- **H-FT-2b [Logic, CI] M-1a** 100 % of cue ids rendering a pill or toast have a paired world-cue id in the cue-pairing table; a planted unpaired cue fails.
- **H-FT-3a [MANUAL, playtest] M1** Within Mail & Destinations' M-FT-3 sessions: ≤ 1 in 5 testers says the HUD hid something; no per-player ranking appears in any recording.
- **H-PERF-2 [Logic] M1** The UI atlas is 2048² with no mipmaps; no full-screen translucent HUD layer exists. Draw count ≤ 60 is ADVISORY until the 4.7.1 draw-counter spike.
- **H-PERF-3 [Logic] M0 (ADVISORY at M-1a)** With the UI clock frozen, 10,000 idle core frames allocate 0 bytes (`GC.GetAllocatedBytesForCurrentThread`) and write 0 text properties; advancing the clock 10 s writes the timer exactly 10 times; row pools never grow under flood; a lint finds no `DrawLine` / `DrawPolyline` in the HUD view.

**Untestable as written — deferred:** numeric sound limits (Audio, seed §4); the shared world / post-process flash budget (Presentation, PEAT); render rig, reference machine, screen-reader rig and 8-human playtests do not exist yet; Godot / .NET are NOT DETERMINED on this machine; 4.7.1 draw-counter and live-region behaviour are NOT SOURCEABLE.

## Open Questions

**Hand-backs closed 2026-10-16.** HN-1 (Input & Controls: digest, L / I binds, tip slot, OQ-IC-11/12), HN-2 (Player Character: focus bracket; typing / speaking bits withdrawn, PC OQ31), HN-3 (Grid Inventory: all of a–e) and HN-4 (Mail & Destinations / Manual Delivery: "posts 1", clerk line, `rollCallNames`, "DEAD LETTER", tier 3 unrelated) were adopted by their owners. **Rejected hand-backs and notes to siblings** (not edited here):
- *Rejected* — Player Character OQ28 UI scale 75–150 %: below the 18 ref px reading floor; H23 stands (100–150 %, reading text to 200 %).
- *Rejected* — Manual Delivery's in-world "+N" sweep pill: redundant now that a hand hold is one "+N¢ · k stacks" pill (its own cut condition, OQ3c); the riffle stays.
- *Rejected* — Mail & Destinations Visual/Audio "feed ≤ 4 lines" and "others' pills per second": H4 / H8 own the caps (5 lines; 500 ms team pill window; feeders per second).
- *Notes* — Manual Delivery `reasonStripTicks` and Throwing's 2 s reason dimming are superseded by H11's F1 strip lifetime; Grid Inventory's "Fumbled" row maps to Throwing's "Bag full — fumbled"; prompt placement is screen-fixed with no leader line (Manual Delivery, Player Character "world-space, clamped").

**Hand-offs to undesigned systems.**

| # | Question | Owner | Resolve by |
|---|---|---|---|
| HN-5 | Confirm `phase`, `phaseId`, `deadlineTick`, `shift`, `quota`, `shiftEarnings`, complaint fields, per-op `ComplaintRaised`, `QuotaMet`; list HUD as a Hard dependent. | Run Structure | M1 design |
| HN-6 | Wallet as one replicated team int (¢, to −500); purchase pending / confirmed / rejected; **the one money format** behind `IMoneyFormatter` (wallet and pills). | Economy, Wallet & Shop | before M-1a strings freeze |
| HN-7 | `ConstructUnderAttack` rate, `PODestroyed`; raid warning `{edgeBearing, etaTicks}`; Mega spawn; own HP. | Defenses, Enemy AI, Health & Damage | M3 design |
| HN-8 | Speaking state readable on every peer beyond 30 m (the speaking-bit design was removed from HUD; Proximity Voice owns encoding). | Proximity Voice | M0 spike |
| HN-9 | Roster (slot → account id, name, hat, tint), host lost, join / leave events for `IHudState`; comms transport items → `comms.md`. | Networking & Session | M0 spike |
| HN-10 | UI sound rate limits (seed §4) and mixing. | audio-director | M1 |
| HN-11 | Announcement tier and store statement; **world / post-process effects request flash tokens from the HUD governor from M1** (H28). | Accessibility, Presentation, art-director | M1 (flash) / M4 |
| HN-13 | Settings screens (H20, H23 settings) and the Pause player list. | Menus & Run Flow; Profile & Settings | M1 |

**Design questions.**

| # | Question | Owner | Resolve by |
|---|---|---|---|
| HN-14 | Spec 09 §5 amendment: text scale to 200 % for HUD reading text (WCAG 1.4.4), 150 % for layout-bound panels (Input & Controls adopted it, OQ-IC-12; only the spec text is pending). | ux-designer / producer | `/ux-design` HUD |
| HN-15 | Art-bible amendments (seed §2: Gold-fill text rule, Charcoal numerals on Go Green, lighter interactive outline, ring / slash / bracket specs, 18 px reading floor, HUD VRAM). | art-director | `/ux-design` HUD |
| HN-17 | Fantasy-test thresholds (≥ 90 % on the 2 s read; ≤ 1 in 5 "hid something") are PROPOSED. | game-designer / QA | M1 playtests |
| HN-18 | Stretch mode, aspect and the height / 1080 scale mechanism; minimum window size — `project.godot` sets neither. | technical-director | ADR (HUD Composition) |
| HN-19 | Reference hardware for the parked frame budget (seed §5). | technical-director / performance-analyst | Technical Setup |
| HN-22 | The actionable "Make space — Sort?" lives 2.7 s (P1 toast); accessibility asks for it on the Prompt surface or persistent until acted on. | ux-designer, Grid Inventory | `/ux-design` HUD |
| HN-23 | Behind-camera teammates: does a compass echo suffice, or is an off-screen edge indicator needed beyond pings? | ux-designer | M1 playtest |
| HN-24 | `feedCrewThreshold` = 3 and the crew-line wording are PROPOSED; verify the 8-player feed still answers "where is my crew" with the compass distance labels. | game-designer | M1 playtest |

**→ becomes an ADR** ("HUD Composition & Notification Queue"): UI-core assembly boundary and the `IUiClock` / `IRoster` / `IHudState` / `IMoneyFormatter` seams; CanvasLayer stack and MarkerLayer; stretch, aspect, safe area; pooling, dirty flags, string caches; font pipeline; the two live-region Controls; the governor's animator and shared pulse clock. Engine verification items: seed §5.
