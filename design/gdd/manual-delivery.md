# Manual Delivery

> **Status**: In Review — /design-review 2026-10-16 NEEDS REVISION; revision applied (CD rulings i–v), awaiting re-review
> **Author**: Evan Deters + agents (autonomous mode)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Reconciled 2026-10-16 with sibling GDDs (hand-backs: 6 adopted, 3 adapted, 1 rejected)** — Simulation Core HB8 and R19; HUD HN-4a and H11; Content & Data `DeliverInsertPm`; Throwing OQ1; Mail R16 per-hold pill; Player Character spare flag bit. OQ1–OQ4 closed (siblings adopted them). Rejection noted in OQ5; log: `design/gdd/reviews/manual-delivery-review-log.md`
> **Implements Pillar**: Pillar 3 — Hands beat machines, until they don't (primary); Pillar 1 — The mail must flow; Pillar 2 — Friendslop surface, factory depth
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 — 5 text fixes applied (fantasy promise wording, settle pulse, remote wallet-floor beat, MD9 pillar citation, wording OQ); accepted concern: in-world "+N" pill cut if HUD aggregates per hold (cut 2026-10-16: Mail R16 per-hold pill). The Quick Hands MATCH-only narrowing was **reversed** at /design-review (CD ruling ii: spec 10's −50 % on every deliver hold, one shared `holdEff`)

## Summary

Manual Delivery is the carrier's hand at the slot. It owns three things: the one Deliver verb on every mailbox, the address-match prompt that says exactly what one hold will do at this door, and the rule that turns a completed hold into a single `deliver_matching` request. One hold at the right door posts every matching piece you carry, from anywhere in the bag. With no match, it posts exactly one piece of the mail you are holding to the wrong house, and the prompt says so first. Otherwise nothing happens. A hold that started on a ✓ can never misdeliver, which makes the hand the certain verb beside throwing's risky one, and the manual path a machine has to beat (Pillar 3). Interaction, hold timing, the inventory op and the verdict at the door belong to Player Character, Input & Controls, Grid Inventory and Mail & Destinations. This GDD only connects them.

> **Quick reference** — Layer: `Feature` · Priority: `MVP (M-1a)` · Key deps: `Input & Controls, Player Character & Movement, Mail & Destinations, Grid Inventory & Containers` · Engine reference: no `docs/engine-reference/godot/modules/` — feasibility of the Input / UI / Networking surface **not checked** against Godot 4.7.1 (NOT SOURCEABLE items listed in Dependencies)

## Overview

Manual Delivery is the carrier's hand at the slot: the **Deliver** verb every mailbox offers, the **address-match prompt** that tells the player — before their thumb moves — exactly what one hold will do at this door, and the **hand insertion** that turns a completed hold into one `deliver_matching` request. It is deliberately small, because almost everything around it is already owned elsewhere. Player Character & Movement owns the interaction framework (focus, range, the `IInteractable` anchor, hold timing in ticks, cancellation, the host's range check); Input & Controls owns the hold grammar, `holdEff`, Toggle mode and the prompt grammar; Grid Inventory owns the `deliver_matching` op and its rule (creative-director directive **D-B**: one hold delivers every matching entry in the panel and cursor; if nothing matches, it misdelivers exactly one unit of the selected hotbar mail; otherwise nothing happens); Mail & Destinations owns `Evaluate`, payment, penalty, complaint and every outcome beat at the door.

What remains, and what this GDD owns, is five things: (1) **registering the Deliver verb** on every destination's interaction anchor, with its availability reasons; (2) the **deliver preview** — a client-side pure function that mirrors `deliver_matching`'s branch choice against the player's own panel and the mailbox's `Evaluate`, and the prompt content it produces (both addresses with ✓ / ✗, "41 pieces · 3 stacks", the predicted "+N¢" or "Wrong house — posts 1 (−N¢)", or the reason nothing will happen); (3) **delivery stance** — what holding mail on the hotbar shows the player and everyone around them; (4) the **request rule** that makes the prompt a promise: a hold that showed ✓ can never become a misdelivery; and (5) the hand-side beat between completing the hold and hearing the verdict.

For the player it is the most-repeated decision in the game: walk up, read, hold, hear the verdict. One hold at the right door empties every matching stack you carry — the payoff for casing your route — while the careless hold at the wrong door costs exactly one letter and a confused resident in pyjamas. It is the guaranteed verb beside throwing's risky one, and the hand path that a machine has to beat (Pillar 3). Without it there is no honest, readable way to post mail by hand; with it, a carrier with ten addresses in the bag clears the street in eleven inputs.

*Design boundary:* behaviour only. The prompt widget is rendered by HUD & Notifications from content supplied here; the request travels through Grid Inventory's op pipeline; the verdict comes from Mail & Destinations. Implementation questions (the preview's placement in the client, caching of `Evaluate` results) → become ADRs. *All content is M-1a scope and provisional pending the two-friend greybox result (systems-index GUARD).*

## Player Fantasy

*Framing: direct — players actively use and feel this system. Shaped with creative-director: framing A **"Certified"** chosen over "Clean Sweep" (overlaps Grid Inventory's "Case Your Route", and `take_all` merges stacks on arrival so a many-stack sweep is the exception, not the rule — kept as a beat) and "The Doorstep Ritual" (overlaps Player Character's carry poses; ceremony slows the verb that must be fastest — the stance is kept as a beat). Player Character owns "one look, one verb"; this GDD owns what the promise says. Mail & Destinations' "Every Door Is a Verdict" owns what the door does next.*

Throwing is for when you're sure. Your hand is for when you need to be.

At 17 Elm Street the parcel on your hotbar turns its label toward you. The mailbox answers before your thumb moves: **✓ 17 Elm St · 41 pieces · +86¢**. That line is a promise in writing. It counts every piece for this door wherever it is on you — the hotbar, the cursor, the bottom of the backpack under the Large you have carried since the Post Office. You hold. Your hand goes into the slot and stays there for a beat, the way you would wait for a signature, and then the whole lot is gone. Nothing to aim. Nothing left behind for this address.

A ✓ never sends a piece to the wrong house. That is the whole contract, and the carrier takes it completely seriously.

A ✗ is never hidden either. "Wrong house — posts 1 (−12¢)" is right there in the prompt. Hold anyway and you hand one letter, slowly and with great care, to a house that did not ask for it. The prompt told you; you did it anyway.

Across the street a teammate throws one stack, with reach and nerve, on an arc that might go red. You are the other half of the crew: the one who clears the doors that matter, and who walks the Cursed letter to its box because a red arc on that one brings the goons out of the hedges. Alone, the hand is your steady pace between throws. With eight players, it is the certainty the chaos runs on.

**Why the hand exists beside the throw.** The M-1a prototype found that a tester abandons hand delivery for throwing when allowed (`prototypes/m1a-delivery-throwing-concept/REPORT.md`) — but that prototype had one item per slot, no stacks and no whole-panel hold, so it measured only single-item hand delivery. Three things the throw cannot do are this system's reason to exist: a ✓ hold can never misdeliver (R5); the hold reaches the whole panel and cursor, while a throw needs the stack on the hotbar; and there is no aim cost. No economic incentive is added to the hand; the hand share is measured instead (fantasy test 3).

**Reference feelings.** *Papers, Please* — checking two documents against each other *is* the play: taken; the misery and the slow inspection: rejected (the read takes a glance). *Death Stranding* — the handover as a careful, certain act: taken; walking as attrition: rejected. *Stardew Valley*'s shipping bin — one action and everything goes: taken; the overnight wait for the result: rejected (the hold is under half a second at the default setting, and the verdict follows within 0.25 s).

**Pillars served.** Pillar 1 (*the mail must flow*): the whole-panel hold means flow never depends on staging the hotbar. Pillar 2 (*friendslop surface, factory depth* — "keep the rules honest (a bad throw is never softened)"): the prompt is the honest rule made visible, and a ✗ is never softened. Pillar 3 (ratified reading — "a manual action may be instant and strong **if the machine's value is that it works unattended**"): one hold posts any number of matching pieces in 0.4 s while a feeder posts two a second; the machine wins by working while you are elsewhere. Pillar 5 (*scales from 1 to 8*): certainty needs no audience, so a solo carrier gets the whole fantasy.

### Feel & Comedy Feedback

*Required by the systems index (CD-SYSTEMS note). Hand side only: what the carrier, the held piece and the prompt do. Everything the **door** does after the verdict — flag pop, cha-ching, wobble, the pyjama resident, REFUSED, the dead-letter moth — is Mail & Destinations' Feel & Comedy Feedback table and is not repeated here. Earnest carrier, slapstick world; art bible principle 2 (every outcome is a physical event).*

| Beat | Trigger | What the hand / world does | Honest-outcome rule |
|---|---|---|---|
| Delivery stance | mail selected on the hotbar | the piece keeps its weight-class carry pose and turns its label to the camera; the carrier's hat lifts a touch (neutral face — the strain face is reserved for throws) | the label is the selected entry's real address; selecting a non-mail item or hands gives no stance |
| The read | a mailbox gains focus | the carrier's head flicks from the label to the door number; the prompt appears | the prompt states exactly what one hold does now and updates within one tick of any change to its inputs (R3); a mismatch is never hidden |
| The reach | hold in progress | the hand travels to the slot in step with hold progress; the flap lifts at completion | the reach is the art bible's wind-up and scales with the hold setting (Toggle mode the same); nothing commits early |
| The signature beat | hold complete, awaiting the verdict | the hand rests in the slot, flap held, carrier politely still (the neutral slot-in of Mail & Destinations' Game Feel) | no flag, coin or wobble is predicted locally; on a refusal of any kind the pieces visibly come back out into the hand, then the door's row plays |
| The sweep | one hold posts 2 or more entries | the backpack flap lifts and the stacks riffle into the slot one after another | the riffle shows the real entries, up to 5 (beyond that the count is Mail R16's per-hold "+N¢ · k stacks" pill); capped at 0.3 s so it never delays the next move |
| The careless hold | a ✗ prompt held anyway | one piece handed over with exaggerated care | exactly one unit leaves (Grid Inventory OQ4); the cost was in the prompt first; the door then plays "Wrong house, hand" |
| Nothing for this address | no match and no mail selected | the carrier pats their pockets, a small shrug; no reach | nothing leaves and no hold starts |
| Closed / taped | the mailbox is CLOSED or LOCKED | the reach stops at the plate or the tape; knuckles tap twice; the door's latch rattle | the reason was in the prompt before the press; the press sends nothing |
| "Close panel to deliver" | a panel is open | the prompt only, dimmed with the reason | the press does nothing and nothing is queued |
| Hold cancelled | release, leaving 2.9 m, knockdown, swimming, a changed fallback piece | the hand withdraws and the flap drops | nothing is delivered; a partial hold never commits |

**Fantasy test** (fail any one and the system has broken its fantasy):
1. **Certified.** Zero misdeliveries from holds that started on a ✓, across every playtest log and the automated race suite (M-FX-1): own pending ops, a phase flip at the completion tick, a hotbar selection change, a catch or pickup arriving mid-hold. Each resolves to a delivery, "nothing" or "closed" — never to a wrong-house unit.
2. **A readable promise.** First-time players shown 20 recorded prompts for 2 s each predict the outcome (posts all / posts 1 to the wrong house / nothing / closed) correctly ≥ 90 % of the time.
3. **The hand has a job.** In shifts 3–5, among **player-driven** correctly delivered units with pay > 0, `handShare = hand ÷ (hand + throw)` ≥ 20 % (units, not `DeliveryOutcome` events; `via` from the outcome; dead-letter clears, feeders, belts and NPC drivers excluded — F3 shows machines out-post a walking carrier ≈ 4 : 1, so the machine share is reported on its own line, never in this denominator). Read per shift × build-level bucket with ≥ 200 units per bucket; **PROPOSED**. Below 20 % at M0 triggers a design revisit with non-economic levers only (Throwing OQ1's — range, catch volume, `throwMinAim`, `verdictRangeM` — and `manualInsertHold`) — never an economic bonus; Throwing's test 1 shares this denominator and trigger. The route input count is Grid Inventory's H-FX-1 (≤ 11 inputs for a 10-address route) and is not duplicated here.

## Detailed Design

*Specialists consulted: game-designer (primary; the "Camera, input, controls" routing row fits a single interaction verb). The first `ux-designer` and `gameplay-programmer` spawns for this section were refused by the session's concurrent-subagent limit (parallel GDD sessions); both were consulted at the second attempt (UI Requirements, Game Feel) and their findings, with systems-designer's Edge Cases pass, were folded back into MD2–MD8 before this section was finalised. Decisions marked **[D]** were taken in autonomous mode by adopting the specialist's recommended option and are logged in `production/session-logs/decision-log.md`. Numbers marked **PROPOSED** are new to this GDD.*

### Core Rules

**MD1 — The Deliver verb.** At run start, for every destination in Mail & Destinations' registry (its R3) whose `DestinationTypeDef.manualInsertHold` is > 0, this system registers **one** Player Character `IInteractable` anchor (P10): `entityId = destinationId`, `slot 0`, `anchor` = the destination's interaction anchor (the mailbox face), `verb = Deliver`, `mode = Hold(manualInsertHold)` (400 ms for `house`), `rangeMm` = the framework default (2 500), `priority = 2` — Player Character's default for Deliver (its P10 priority row: Open / Pick up / Mount 1, everything else 0), taken from the Players `VerbCatalogue` code table (Deliver = `DefaultPriority(PickUp) + 1`), not a content field (*erratum 2026-10-22, owner ruling, Evan, 2026-10-22 7:13 PM PT, Mail Dev chat: "Keep the verb catalogue as the source (recommended)"*). The one level over Pick-up is a deliberate bias, not a tie-break: 100 score points in Player Character F4 equal ≈ 625 mm of distance, so a missed throw lying at the mailbox's foot does not steal focus from the door the carrier walked up to, while looking down at the parcel still wins through F4's angle term (600 weight). **Per-press hooks** (Player Character P10 / P12): the anchor supplies `GetHoldTicks(latch)` (the shared `holdEff`, latched at press) and `ShouldCancel(latch) → reason?` (MD4's extra cancels), which P12's hold counter calls every tick; the static `Hold(manualInsertHold)` is only the base. It is the mailbox's **only** verb (Input & Controls R4.2: one verb, one grammar per target type). v1 registers only `house` mailboxes; deferred destination types join by data (a type with `manualInsertHold = 0`, such as a business dock, gets no Deliver anchor). Mailboxes are never removed during a run, so anchors never deregister.

**MD2 — Availability.** `IsAvailable(actor, context)` for the Deliver anchor returns the first matching reason, else `ok`. The same ordered list is shown in the prompt (dimmed, lock icon, reason text — Player Character P11) and is what blocks a hold from starting (Input & Controls R4.4):

| # | Condition | Reason key (player text) | Evaluated on |
|---|---|---|---|
| 1 | the actor is in **Panel** context (own panel or an external container open) | `deliver.close_panel` ("Close panel to deliver") | client |
| 2 | the actor is at swim depth | `deliver.dry_land` ("Dry land required to deliver" — Player Character's string) | client and host |
| 3 | one of the actor's own **non-optimistic** ops is awaiting its reply — `deliver_matching`, `throw`, `drop`, `take_all` or `sort` (Grid Inventory Network rule 4) **[D]** | `deliver.pending` ("Delivering…", or "Sorting…" while an own sort is pending) | client only |
| 4 | the destination is CLOSED | `mail.reject.closed` ("Closed — opens at Delivery") | client (derived from RunState), host via `Evaluate` |
| 5 | the destination is LOCKED | `mail.reject.not_on_route` ("Not on today's route") | as row 4 |
| 6 | the deliver preview (MD3) is **NOTHING** | `deliver.nothing` ("Nothing for this address" — one dim line, no lock: an ordinary state, not an error) | client |

Mounted and Dead actors never focus a mailbox (Player Character P11), so they need no reason. In Panel context focus is computed for display only (Player Character P11), so row 1's prompt shows in HUD's panel footer (H11, ring hidden) with the lock icon and **no button glyph** (in Panel, Interact means pick / place). Rows 4 and 5 use Mail & Destinations' reason keys and strings unchanged; they gate the **start** of a hold only — a hold already in progress when Delivery ends is judged by its start state (Mail & Destinations R4 closing grace, MD4). Row 3 is **derived from the pending queue**, never kept as a separate flag, so every path that clears the queue — the reply, a resync's snapshot, a rejoin snapshot — clears it; the 5 s timeout itself does **not** clear it: a timed-out entry stays pending until the resync snapshot is applied, so no press can act on a stale mirror; its dimmed look is shown only after Grid Inventory's `pendingDebounceMs` (250 ms), so a normal round trip never flickers the next door's prompt. A predicted wallet-floor refusal is **not** a row: the hold stays available with the "Can't afford a wrong house" line, because the replicated wallet can lag the host by one event and a block could stop a legitimate post, while an allowed refused hold costs nothing and plays the REFUSED beat for everyone **[D]** (game-designer and ux-designer; systems-designer proposed a block — overruled, logged).

**MD3 — The deliver preview.** A **pure function** run on the owning client: `Preview(destination, destState, panelView, cursorView, selectedSlot, runState, statSheet, wallet) → Branch`, where `destState` is the destination's OPEN / CLOSED / LOCKED state at the current tick — or, **during a hold, at the hold's start tick** (closing grace, MD4). `panelView` is the player's **displayed** own panel (Grid Inventory's confirmed mirror plus replayed pending ops — what the player sees), and the cursor entry is included whether or not the panel is open. Checked in this order, first match wins **[D]**:

1. **REJECT** — the destination is CLOSED or LOCKED (the mailbox-level steps 1–2 of Mail & Destinations' `Evaluate`, R5). No panel scan.
2. **MATCH** — at least one entry in the cursor, hotbar, inventory or backpack (that order, row-major within each — Grid Inventory's `deliver_matching` order) is mail whose `addressId` equals the destination's and whose kind the destination's type accepts (`Evaluate` = Accept). The branch carries the matching entries in order, entries `k`, units `n`, kinds present, and the predicted pay, late units and dead units (Formulas F1), plus `deadOnly` (every matching unit is a dead letter: still ✓, labelled "clears", never "+0¢"). The selected entry is scanned like any other, so a selected entry that is the only match is MATCH, never MISDELIVER_ONE.
3. **MISDELIVER_ONE** — no match, and the **selected hotbar slot** holds mail (`Evaluate` on it = Misdeliver). The branch carries that entry, its address, the predicted penalty of its **oldest** unit, `floorRefusal = (wallet − penalty < walletFloor)` (F1), and `cursed` (the entry carries the Cursed flag: the prompt adds "Cursed — brings goons", because one Cursed unit in the wrong slot fires Mail & Destinations' `CursedMisdelivered`). A selected entry the type does not accept would be `wont_fit` — dormant for houses in v1, shown as "Won't fit here" with the hold blocked.
4. **NOTHING** — otherwise (hands, a tool or a non-mail item selected, or no mail at all).

The preview is evaluated **every tick while a Deliver anchor is focused**, with no cache (a scan of ≤ 26 entries is negligible), so the prompt is current within one tick of any change; nothing is evaluated while IDLE. Test seams (compiled into test builds): an evaluation counter and a scanned-entries counter.

**Settle tracker.** A `DeliverPreviewTracker` in the same Sim-side client layer (Simulation Core's ClientLayer `Previews` phase; never Presentation) holds `(branchKey, sinceTick)`, where `branchKey = (branch kind, destinationId, selectedEntryId if MISDELIVER_ONE)`. `displayedTicks = tick − sinceTick`. Any change of `branchKey`, and any tick on which the verb is unavailable (MD2 rows 1–5), resets `sinceTick` to the current tick; payload changes (cents, `k`, `n`, `floorRefusal`, `cursed`) do not. "Displayed" means available and computed in the Sim-side layer, never "rendered", so a frame hitch or the prompt's crossfade never feeds the Sim. The tracker publishes `canStartHoldNext` (read-only) for Presentation's reach echo (Game Feel). **Per-tick order on the client:** apply own container events and replies → focus (Player Character P11) → preview and tracker update → resolve that tick's Interact press against the state at its position in the ordered input-event list (Input & Controls G4), so a press is never judged on a branch that a same-tick reply already changed. For Deliver this press-tick state **overrides** Input & Controls G1's presented-focus snapshot for the branch (the focused anchor is still G1's). Identical inputs give the identical branch on every client. **The preview is a prediction:** the host's `Evaluate`, wallet and tables decide; the cents are labelled as predicted in the prompt's live-region text and are replaced by the outcome's figure (Mail & Destinations F10 note).

**MD4 — Press, latch and hold.** Interact resolves at press (Input & Controls R4.3: an inbound catch first, else the focused anchor's verb). If the focused anchor is a Deliver anchor and the preview **recomputed in the press tick** is **MATCH** or **MISDELIVER_ONE**, the hold starts and **latches** `(destinationId, branch, selectedEntryId and its slot if MISDELIVER_ONE, holdTicks)` for its whole length (G1). `holdTicks` is Input & Controls' single shared `holdEff` (F1; Player Character F4 calls it) with the StatSheet's `insertPm`, identical for both branches (F2, MD9), supplied through `GetHoldTicks(latch)` (MD1). **A wrong-house hold must be meant:** a hold cannot start on a MISDELIVER_ONE branch while `displayedTicks < branchSettleTicks` (6 ticks, 200 ms, F5; MD3's tracker — a branch first displayed at tick T can be held from T + 6, a press held through T + 6 still needs a fresh press) **PROPOSED** **[D]**; in screen-reader mode the settle also lasts until the ✗ announcement has fired (UI Requirements). A refused press pulses the ✗ line and shows the transient reason strip "Wrong house — check, then hold again" (`deliver.cancel.settle`, F5) — so a press made on a ✓ that turned ✗ a moment earlier (a reply or a snap-back) never posts to the wrong house. The settle applies equally to Toggle mode's arming press. MATCH holds have no settle (a ✓ cannot misdeliver). During the hold the prompt's layout and branch stay frozen; its numerals may tick, nothing is re-announced.

A hold is cancelled by everything in Player Character P12 and Input & Controls G2, **plus**, checked every tick up to and including the completion tick **[D]**:
- the recomputed preview's branch **kind differs from the latched kind** (MATCH → anything else, MISDELIVER_ONE → MATCH or NOTHING; the recompute uses the destination state at the hold's start tick, so a close never yields REJECT mid-hold), with the reason "Mail changed — check the door" (`deliver.cancel.prompt_changed`) — so the request sent is always the one the player held for; a MATCH whose `k`, `n` or cents change but stays MATCH does not cancel;
- on a **MISDELIVER_ONE** hold, the selected slot or the entry in it is no longer the latched one, compared at the tick boundary (a scroll and back inside one tick is no change) — "You switched pieces" (`deliver.cancel.selection_changed`); the raised piece is the piece that would go in, so the hold follows it (Input & Controls G2); an entry id retired by a merge-away, a removal or a failed optimistic split (`stale_entry`) counts as changed;
- any MD2 row other than 4 and 5 becomes true for the latched anchor — including one of the player's own non-optimistic ops becoming pending (a throw or drop is not live during a commit, G9, but a snap-back can re-queue one).
These are returned by `ShouldCancel(latch)` (MD1); P12 / G2 cancels are Player Character's and Input & Controls'.
**Closing grace.** The destination closing mid-hold does **not** cancel it: Mail & Destinations R4 judges a hand op by the state at the tick its hold **began** and resolves it as OPEN if it applies ≤ `closeGraceTicks` (90) after close. The latched hold therefore keeps evaluating its branch with the destination state of its start tick (MD3 `destState`). (Districts never re-lock mid-run, so a hold never meets LOCKED after it starts.)
A selection change on a **MATCH** hold does nothing (the selection is irrelevant to that branch). A latched deliver hold is never converted into a catch (Player Character P11). Toggle mode (G7) changes only how the press is given: one press arms, the hold completes by itself after the same `holdTicks`, a second press or Back cancels (Back is consumed by the cancel; Pause needs a fresh Back — Input & Controls ladder step 3b). A cancel edge and the completion in the same tick: edges are processed before hold accrual (G4), so the cancel wins and nothing is sent.

**MD5 — The request, and the certified promise.** On completion the client sends exactly one request through Grid Inventory's op pipeline: `deliver_matching {mailboxId, selectedEntryId?, holdStartTick}`, where **`selectedEntryId` is included only when the latched branch is MISDELIVER_ONE**, and `holdStartTick` is the tick on which the hold began **in the host's tick domain** — Simulation Core's `IHostClock.EstimatedHostTick` (spec 06: RTT/2 ping every 2 s) at its hold-start tick, never its raw Sim tick; the host's own holds stamp the exact tick (offset 0). Mail & Destinations R4 closing grace needs it; Grid Inventory R7 carries it. On a MATCH hold `selectedEntryId` is omitted, so if the host finds no match (an own pending op the display had replayed, a phase flip) Grid Inventory's handler returns `no_target` (its H-R7-11) instead of falling back to a misdelivery. **A hold that started on a ✓ can therefore never misdeliver** — with no change to the op. The reverse drift — possible only when the host's state differs from the client's display, because a client-visible change cancels the hold (MD4) — is harmless: a MISDELIVER_ONE request whose host state holds a match delivers the match (a match always wins over the fallback). Apart from `holdStartTick`, the request carries no hold duration and no predicted counts; the host never checks hold length (Input & Controls R-rule; friends-only trust, spec 13 §4.3). After sending, the verb is unavailable with `deliver.pending` (MD2 row 3) until the reply arrives or, after Grid Inventory's `pendingReplyTimeoutSeconds` (5 s), until the resync snapshot is applied; a press in that window pulses the prompt and sends nothing. The request carries Grid Inventory's `reqId`; the host answers a duplicate `reqId` with the original reply and applies nothing (Grid Inventory Network rule 8).

**MD6 — Host handling.** The host applies the request in `Inputs.Apply` in arrival order (Grid Inventory R7's single FIFO): Player Character P13 first (alive, not frozen, target exists, `IsAvailable` rows 2, 4, 5 on host state — row 2 replies `dry_land` — range by F5 with +500 mm slack over 300 ms), then Grid Inventory's `deliver_matching` handler, which runs Mail & Destinations' consumption (R6) per entry. Manual Delivery adds one check: the `mailboxId` must name a registered Deliver anchor, else `no_target`. Lateness is read at the apply tick; the destination state is read **at `holdStartTick`** under Mail & Destinations' closing grace (R4: a hold begun while OPEN and applied ≤ 90 ticks after close resolves as OPEN; a hold begun at or after the close tick gets `closed`). Manual Delivery owns the request's consistency check (Input & Controls): `holdStartTick` is **clamped, never replaced**, to `[applyTick − (maxHoldTicks + closeGraceTicks), applyTick]`, where `maxHoldTicks = ceil(max(manualInsertHold, holdCeil) × 30)` for the destination's type (60 at the 2.0 s ceiling → bound 150; a 3.0 s content hold → 180) — consistency, not anti-cheat; the client's estimate can be off by a few ticks, which only matters for a hold begun within those ticks of the close (Edge Cases, bell fuzz); a knockdown in the same tick is ordered by Player Character's same-tick rules (its Edge Cases). Every rule about money, complaint, the wallet floor and lateness is Mail & Destinations'; Manual Delivery computes none of it on the host. The host adds **no throttle** on deliver requests (Mail & Destinations R12: hands are never throttled; the client's pending lock already serialises one player's holds, and a modded client sending holdless requests is out of scope under the friends-only model — anti-pillar "NOT hardened multiplayer").

**MD7 — Reply and verdict on the client.** The reply goes to the requester only; `DeliveryOutcome` events (one per entry consumed, Mail & Destinations R16) go to every peer. The hand-side response, by reply:

| Reply | Hand / prompt (this GDD) | Door (Mail & Destinations) |
|---|---|---|
| `ok`, `got = requested` | the signature beat ends; the sweep riffle (if `k ≥ 2`) | correct / late / dead-letter rows, per op |
| `ok`, `got < requested` | as above, plus "Posted 3 of 4 — 1 won't fit this box" (dormant for houses in v1) | per op |
| `ok`, MISDELIVER_ONE | the careless hold's single piece leaves | "Wrong house, hand" |
| `wallet_floor` | the piece comes back out into the hand | REFUSED stamp, "Can't afford a wrong house" |
| `no_target` | the pieces come back out; "Nothing for this address" | — |
| `closed` / `not_on_route` (Reject at the apply tick) | the pieces come back out; the reason | latch rattle / taped thud |
| `out_of_range` / `position_stale` | the pieces come back out; "Couldn't reach the slot — step closer" | — |
| `dry_land` (host swim check) | the pieces come back out; "Dry land required to deliver" | — |
| `dead` / `frozen` | **no hand beat** — the pieces are in the pile or on the body; Player Character's knockdown / on-break presentation wins | — |
| reply timeout (5 s) | resync (Grid Inventory rule 7); pending holds until the snapshot is applied, then clears; "Connection hiccup — bag re-checked" | — |

Reason lines appear in a **transient strip** below the prompt for HUD & Notifications' F1 reading-time lifetime (H11: ≥ 2.5 s × the duration setting); the live branch stays visible and pressable underneath, so a cancel never hides the new ✓ / ✗. A focus change to another target ends the strip early (the HUD history and the live region keep it). The hand-side beat plays **once per hold**, keyed by `(actorId, destinationId, apply tick)`, however many `DeliveryOutcome` events that hold produced; Mail & Destinations R16 applies a hold's k outcomes in one tick and groups them into one door beat and one "+N¢ · k stacks" pill on every peer. A late reply that arrives after the 1 s hand idle but before the timeout plays the verdict beat without the slot-in. If no reply has arrived 1 s after completion, the hand returns to idle and only the prompt keeps "Delivering…"; a reply for a request already cleared by a resync plays no hand beat (the door's `DeliveryOutcome` beats still play). A beat for a mailbox the carrier has already walked away from (beyond 2.9 m) is not attached to the current prompt — the HUD feed carries it.

**MD8 — Delivery stance.** While the selected hotbar slot holds mail, the carrier is in delivery stance: Player Character's carry pose for the piece's weight class is kept unchanged (P20 — one hand high, hugged, or overhead), plus — owned here — the held piece's **label turned to the local camera** over 0.2 s so its address is readable (an overhead heavy piece shows the label on its camera-facing side). Stance has **no gameplay effect** other than naming the MISDELIVER_ONE fallback: the deliver hold reaches the whole panel and cursor regardless of what is selected, and selecting mail is never required to deliver (Grid Inventory D-B, H-FX-1). Remote peers see the held piece's kind (Grid Inventory's public `PlayerState`) with a generic label; whether they should see its district swatch is an Open Question.

**MD9 — Quick Hands.** The Carrier-category perk Quick Hands (spec 10: "Mailbox insert hold −50 %") sets the deliver insert modifier `insertPm = 500` (Content & Data stat `DeliverInsertPm`) **[D]** — spec 10's value, replacing Player Character's PROPOSED 300 and its "Carrier archetype" wording (there are no archetypes; Carrier is the perk category). It applies to **every deliver hold**, ✓ and ✗ alike, through Input & Controls' one shared `holdEff` with its unchanged 6-tick floor (F2): 12 → 6 ticks at the default setting; at the 0.2 s setting the floor already binds, so the perk gives nothing there (accepted, CD ruling ii — a re-tune candidate, Open Questions 6). Spec 10 is applied literally, so no spec amendment and no user ratification is needed. A faster ✗ hold softens nothing: the 6-tick settle (MD4) and the ✗ prompt keep a wrong-house hold deliberate and it costs exactly what the prompt says (Pillar 2). Player Character F4 and Input & Controls F1 carry the same value.

**MD10 — What this system never does.** It never builds address text (World Generation's display strings, inserted through localisation arguments); never computes payment or penalty on the host (the client runs Mail & Destinations' F10 for display only); never throttles hands (Mail & Destinations R12); never reaches an external container (`deliver_matching` scans the player's own panel and cursor only — a chest or truck cargo is not on you); never delivers while Mounted; and never makes a hold re-target mid-press (G1).

### States and Transitions

Per player, on the owning client (the host keeps no Manual Delivery state between ticks).

| State | Entered by | Exits to |
|---|---|---|
| IDLE | no Deliver anchor focused | PREVIEW when a Deliver anchor gains focus (Player Character P11, 4-tick dwell for a challenger) |
| PREVIEW (available: MATCH / MISDELIVER_ONE; or unavailable: a MD2 reason) | focus on a Deliver anchor | HOLDING on a press with an available branch past its settle (MD4); IDLE on focus loss; stays PREVIEW (pulse + reason strip) on a press while unavailable or settle-refused |
| HOLDING (Toggle mode: ARMED) | press latched to Deliver (MD4) | SENT on completion; PREVIEW on any MD4 / P12 / G2 cancel (the 0.1 s withdraw and the reason strip are presentation, not states, and gate no input) |
| SENT (derived: an own `deliver_matching` is in the pending queue) | request sent (MD5) | PREVIEW on the reply (MD7 beat plays) or on the resync snapshot after the 5 s timeout; IDLE instead if focus is no longer on a Deliver anchor |
| MUTED (overlay) | the Interact button is still down after completion (G6), a cancel (G3) or a router-state change (G3) | normal handling on release; a new hold always needs a fresh press |

*Rules on the table:* MUTED overlaps PREVIEW and SENT. SENT is derived from the pending queue like MD2 row 3, never stored, and its `deliver.pending` reason holds at **every** mailbox, not just the one posted to. Focus loss during HOLDING does not cancel while the latched anchor stays within 2.9 m (G1, G2); focus may move to a neighbouring mailbox's prompt only after the hold ends.

### Interactions with Other Systems

| System | Direction | Interface / data | Owner of the interface |
|---|---|---|---|
| Player Character & Movement | upstream | `IInteractable` registration (MD1) with the `GetHoldTicks` / `ShouldCancel` hooks (P10), focus, hold timing in ticks (F4), cancel list (P12), host range check (P13/F5), carry pose (P20) | Player Character |
| Input & Controls | upstream | Interact resolution at press (R4.3), Hold grammar G1–G9, `holdEff` (F1), Toggle mode (G7), ring (F12), prompt grammar, one-commit rule | Input & Controls |
| Grid Inventory & Containers | both | reads the displayed own panel, cursor, selected slot and pending queue; sends `deliver_matching {mailboxId, selectedEntryId?, holdStartTick}`; receives the reply (`requested`/`got`, reasons) | Grid Inventory owns the op; this system owns when and with which arguments it is sent |
| Mail & Destinations | upstream | `Evaluate` (client copy), destination registry and anchors, `manualInsertHold`, value hint (R17) + F10, F7 penalty, reason keys, `DeliveryOutcome` | Mail & Destinations |
| Economy, Wallet & Shop | upstream (read) | replicated team wallet and `walletFloor` (−500 ¢) for the floor-refusal prediction | Economy |
| Run Structure | upstream (read) | RunState `phase`, `unlockedDistricts`, `currentShift` (destination state and lateness) | Run Structure |
| Perks & Perk Draft / Content & Data | upstream | StatSheet `DeliverInsertPm` (`insertPm`; Quick Hands 500, every deliver hold, MD9; StatDef range 0…750) | Perks push modifiers; Content & Data owns the key |
| Simulation Core | upstream | op queue and `opSeq` arrival order (MD6), `IHostClock` host tick domain (MD5), ClientLayer `Previews` phase (MD3), same-or-next-tick loopback reply | Simulation Core |
| HUD & Notifications | downstream | renders the prompt and ring from this system's content (UI Requirements); plays pills and feed from `DeliveryOutcome` | HUD renders; this system owns the deliver prompt's content |
| Throwing & Catching | sibling | shares `Evaluate` (arc colour) and the Interact button: a catch wins at press, a latched deliver hold is never converted | Throwing owns the catch window |
| Presentation & World Dressing, Audio | downstream | hand-side beats (Feel & Comedy Feedback, Visual/Audio) | Presentation |
| Networking & Session | both | the request/reply rides Grid Inventory's channel; no state of its own (Network) | Networking |

## Formulas

*From systems-designer. All arithmetic is integer (cents, permille, milliseconds, ticks); `RH` is Mail & Destinations' single round-half-up. This system **connects** to Mail & Destinations F5–F8/F10 and Input & Controls F1 rather than redefining them: F1 is their composition over the matching entries, F2 is Input & Controls F1 evaluated for the deliver hold.*

### F1 — Deliver preview figures

The deliverPreview formula is defined as:

`MATCH: predictedPay = Σ_entries Σ_runs count × pay(unitSpawnValue, T‰(currentShift − deadlineShift), MailValueMult‰[kind], thrownPm = 1000)` · `k = #matching entries` · `n = Σ count` · `lateUnits = Σ count where currentShift − deadlineShift = 1` · `deadUnits = Σ count where currentShift − deadlineShift ≥ deadLetterShifts`
`MISDELIVER_ONE: predictedPenalty = penalty(unitSpawnValue of run 0 of the selected entry)` · `floorRefusal = (wallet − predictedPenalty < walletFloor)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| runs `(deadlineShift, count, unitSpawnValue)` | u8, u8, u16 | 1–5 runs per entry | state (Mail & Destinations R17 value hint, inside Grid Inventory entry events) | ascending `mailId`, so run 0 holds the oldest unit — the one a misdelivery takes (Grid Inventory R6) |
| `pay(…)` | int ¢ | 0 to Mail & Destinations F6's maximum without Cannon Arm (≈ 1,478 is the thrown maximum) | calculated (Mail & Destinations F6, per unit) | per-unit rounding, exactly as F10 |
| `T‰` | int | 1000 / 500 / 0 | calculated (Mail & Destinations F5; Grace Period → 1000) | timeliness |
| `MailValueMult‰[kind]` | int | 1000–1250 | StatSheet (Stamp Collector) | team modifier |
| `thrownPm` | int | 1000 | constant | a hand delivery never gets Cannon Arm |
| `penalty(…)` | int ¢ | 1–672 | calculated (Mail & Destinations F7, on **spawn** value) | a dead letter still costs its penalty |
| `MisdeliveryPenaltyMult‰` | int | 500–1000 | StatSheet (Fragile Handling 500; Mail & Destinations F7) | team modifier inside `penalty` |
| `deadLetterShifts` | int | 2 | data file (Mail & Destinations) | |
| `wallet` | int ¢ | ≥ −500 | state (Economy, replicated team wallet) | may lag the host by one event |
| `walletFloor` | int ¢ | −500 | data file (Economy) | landing exactly on −500 is allowed |

**Output Range:** `predictedPay` ≥ 0 (0 when every unit is a dead letter — it still clears); `predictedPenalty` ≥ 1; `floorRefusal` boolean; `k` 1–≈26 (the panel's entry count), `n` ≤ the panel's units. **A prediction only**: the host's tables decide, and the `DeliveryOutcome` figure replaces it (a Draft pick or a teammate's spend can make it differ for one round trip).
**Example:** shift 3, three matching entries. A: 6 letters, value 8, deadline 3 → 6 × 8 = 48. B: 3 letters, value 8, deadline 2 (one shift late, 4 ¢ each) = 12, plus 2 letters, value 9, deadline 1 (dead) = 0. C: 5 letters, value 9, deadline 3 → 45. Result: `k = 3`, `n = 16`, **`predictedPay = 105 ¢`**, `lateUnits = 3`, `deadUnits = 2`. Floor case: no match, selected entry's run 0 has value 8 → `penalty = max(1, RH(8 × 500 × 1000 / 10⁶)) = 4 ¢`; at wallet −496 the result is −500 (allowed, `floorRefusal = false`); at −497 it is −501 (`floorRefusal = true`, the prompt adds "Can't afford a wrong house").

### F2 — Deliver hold ticks (Input & Controls `holdEff`)

The deliverHoldTicks formula is defined as:

`holdTicks = holdEff(holdBaseMs, insertPm, holdSettingMs) = clamp(⌈holdBaseMs × (1000 − insertPm) × holdSettingMs × 30 / (1000 × 400 × 1000)⌉, minHoldTicks, ⌈max(holdBaseMs, holdCeilMs) × 30 / 1000⌉)`

This is Input & Controls F1 — the one shared Sim function (Player Character F4 calls it), evaluated in int64 with a single integer ceiling and no intermediate truncation, latched at press — restated for the deliver hold, not a second copy. `insertPm` applies to both branches (MD9); this GDD adds no term.

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| `holdBaseMs` | int | 400 | data file (Mail & Destinations `manualInsertHold`) | base deliver hold |
| `holdSettingMs` | int | 200–1000, step 50 | state (player setting, Input & Controls R10) | latched at press |
| `insertPm` | int ‰ | 0 or 500; the StatSheet sum is clamped to 0…750 by Content & Data's `DeliverInsertPm` StatDef (at ≥ 1000 the product goes to 0 or negative before the clamp) | StatSheet `DeliverInsertPm` (Quick Hands, MD9) | spec 10's "−50 %" |
| `minHoldTicks` | int | 6 | constant (Input & Controls F1, Player Character F4) | the art bible's ≥ 0.2 s anticipation |
| `holdCeilMs` | int | 2000 | constant (Input & Controls `holdCeil`) | unchanged — the perk only shortens |

**Output Range:** 6–30 ticks (0.2–1.0 s) for the 0.4 s base hold; the ceiling (60 ticks) binds only for longer content holds. Intermediates ≤ 3.6 × 10⁹ for a 3.0 s content base (int64).

| `holdSetting` | No perk | Quick Hands (✓ and ✗) |
|---|---|---|
| 0.20 s | 6 | 6 (floor) |
| 0.25 s | 8 | 6 (floor; raw 3.75 → 4) |
| 0.40 s (default) | 12 | 6 |
| 0.60 s | 18 | 9 |
| 1.00 s | 30 | 15 |

**Example:** default setting, Quick Hands: `⌈400 × 500 × 400 × 30 / 4 × 10⁸⌉ = 6` → **6 ticks (0.2 s)**. At the 0.2 s setting the raw value is 3 and the 6-tick floor binds, so the perk gives nothing there (MD9, accepted). No deliver hold is shorter than 6 ticks, so Input & Controls F12's ring delay needs no change.

### F3 — Hand versus feeder throughput (reference for Pillar 3, not a rule)

The doorCycle formula is defined as:

`cycle = walk + holdEff + verdict` · `handRate = n / cycle` · `nStar = insertRate × cycle`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| `walk` | s | ≈ 5.4 | calculated (spec 11: 60 s circuit over 10 houses = 6.0 s per door, minus hold and verdict) | time between doors |
| `holdEff` | s | 0.4 | calculated (F2) | |
| `verdict` | s | ≈ 0.2 | state (Mail & Destinations Game Feel: outcome beat at `max(0.2 s, server outcome)`; unmeasured) | the next hold may start during it |
| `n` | int | 1–40 | state | pieces for this door on the carrier |
| `insertRate` | pieces/s | 2.0 | data file (Mail & Destinations R12) | one piece per 15 ticks per feeder |

**Output Range:** a ratio, not a game rule.
**Example:** at one door a present carrier posts `n` pieces in 0.6 s, so the hand beats a feeder in the window for every `n ≥ 2`. Over a whole cycle (6.0 s) one supplied feeder posts `nStar = 12` pieces — more than the walking carrier delivers to that door whenever it carries fewer than 12 per visit. Spec 11's shift-1 hand figure (220 ¢/min ÷ ≈ 8 ¢ ≈ 27 pieces/min ≈ 2.7 per door per circuit) is far below 12, so a supplied feeder out-posts a walking carrier at its door about 4 : 1. This is the ratified Pillar 3 reading: the hand is instant and strong for the stack in front of it; the machine wins by working with nobody there — if the belts keep it supplied.

### F4 — Sweep riffle timing (presentation)

The riffleTiming formula is defined as:

`shown = min(k, 5)` · `totalMs = (k ≥ 2) ? min(riffleCapMs, shown × riffleStepMs) : 0` (beyond 5 entries the count is the per-hold HUD pill's, Mail R16 — no in-world label)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| `k` | int | 1–≈26 | calculated (entries in the reply's `got`) | real entries posted |
| `riffleStepMs` | int | 60 | data file **PROPOSED** | per-stack step |
| `riffleCapMs` | int | 300 | data file **PROPOSED** | the sweep never reads as a delay |

**Output Range:** 0 ms (one entry) or 120–300 ms. Runs on the presentation's monotonic clock from the `ok` reply; it never gates input.
**Example:** `k = 2` → 120 ms; `k = 5` → 300 ms; `k = 9` → 300 ms, 5 stacks shown, the pill reads "9 stacks".

### F5 — Message timers (constants)

The reason strip's lifetime is HUD & Notifications' F1 (H11), ended early by a focus change (MD7; `reasonStripTicks` retired 2026-10-16) · `branchSettleTicks = 6` (200 ms: a MISDELIVER_ONE `branchKey` must be displayed this long before a hold can start on it, MD3–MD4; screen-reader mode adds "until the ✗ announcement has fired") · `cancelBeatTicks = 3` (0.1 s hand withdrawal, matching Input & Controls' ring drain) · reach progress `= 1000 × heldTicks / holdTicks` ‰ (the same value as the ring). The 5 s pending-reply timeout is Grid Inventory's `pendingReplyTimeoutSeconds` and is referenced, not owned.

## Edge Cases

*From systems-designer, with additions from gameplay-programmer; cross-checked against Grid Inventory, Mail & Destinations, Player Character and Input & Controls. Where a dependency rule decides the outcome it is named. Four findings changed the rules above (MD4 branch-change cancel and settle, MD3 `cursed` / `deadOnly`, MD7 dead / frozen suppression) and are listed here as cases, not repeated as rules.*

### Zero, max and out-of-range

- **If the panel and cursor hold no mail and the selected slot is hands or a tool**: the preview is NOTHING, the prompt shows "Nothing for this address" (MD2 row 6), no hold starts, and a press plays the pockets-pat and a prompt pulse (Grid Inventory edge case "no match, selected slot holds no mail").
- **If every entry the carrier holds matches (k ≈ 26, the panel's maximum)**: one MATCH hold sends one request; the host consumes in cursor → hotbar → inventory → backpack order; the reply is `requested = got = 26 entries`; the riffle shows 5 stacks within 0.3 s (F4) and the per-hold pill reads "26 stacks"; the hand beat plays once. Pay sums stay far inside int32.
- **If every matching unit is a dead letter**: MATCH with `deadOnly`: the prompt reads "✓ clears 2 dead letters" (never "+0¢"), the hold posts them for 0 ¢ and 0 complaint (Mail & Destinations R7/R9). A ✗ would mislead — clearing dead mail at its own door is correct.
- **If the selected entry is the only match**: MATCH, never MISDELIVER_ONE (MD3: the selected slot is scanned like every other).
- **If the cursor entry matches while the panel is closed (Holding context)**: it counts and is delivered first (Grid Inventory's order). **With the panel open**: MD2 row 1 blocks the hold and nothing is queued.
- **If Quick Hands applies at the 0.2 s setting**: the hold is 6 ticks, the same as without the perk (Input & Controls' floor, F2); the perk is invisible at that setting (accepted, MD9).
- **If the branch flickers ✗ → ✓ → ✗ or the player scrolls between two wrong-house pieces**: each `branchKey` change restarts the settle (MD3 tracker), so a hold needs 6 ticks after the last change; a cents or `floorRefusal` change does not restart it.

### Timing and simultaneous events

- **If a hold completes on the first tick after Delivery ends**: it began while OPEN, so it resolves normally under Mail & Destinations' closing grace (R4, ≤ 90 ticks after close) — the carrier who was already posting finishes the post. A press made at or after the close tick cannot start a hold (MD2 row 4); a lagging client's request for a hold begun at or after close is answered `closed`, the pieces come back out, nothing is consumed. Never a misdelivery.
- **If the phase flips mid-hold**: the hold continues and is judged by its start state (MD4 closing grace); the prompt keeps its latched lines and the next press shows "Closed — opens at Delivery".
- **If a request's `holdStartTick` is in the future or older than `applyTick − (maxHoldTicks + closeGraceTicks)`**: the host clamps it to the apply tick or to that bound (MD6) — never replaces it; R4's grace then decides.
- **Bell fuzz (known limit)**: `holdStartTick` is the client's estimate of the host tick (MD5), off by a few ticks; a hold begun within ≈ 3 ticks of the close can be judged on the other side of the bell. Outside that band the verdict equals the true tick's (M-MD6-4).
- **If the carrier is knocked down on the completion tick**: the request and the knockdown are ordered by Player Character's same-tick rules; if the request applies first it succeeds and the spill takes the rest; otherwise the reply is `dead` and **no** hand beat plays (MD7) — the pieces are in the pile.
- **If two players post to the same mailbox in the same tick**: each preview scanned only its own panel; the host applies both in FIFO order and both succeed (capacity is unlimited, Mail & Destinations R6). They share one door complaint bucket and one team wallet, so if both are MISDELIVER_ONE near the floor the second can get `wallet_floor` although its prompt predicted no refusal — the prompt's figures are labelled predicted, and REFUSED plays honestly.
- **If a reply or snap-back lands in the same tick as the press**: the client applies its container events and replies before resolving the press (MD3), so the press is judged on the new branch; if the new branch is MISDELIVER_ONE it has been displayed for 0 ticks and the settle rule refuses the hold and pulses the ✗ line (MD4).
- **If the hotbar is scrolled and Interact pressed in the same frame**: the press resolves at its position in the ordered event list (G4). Scroll-then-E latches the new selection — which, if it made a MISDELIVER_ONE branch, is refused by the 6-tick settle; E-then-scroll starts the hold and cancels it in the same tick with "You switched pieces" on a MISDELIVER_ONE hold, and changes nothing on a MATCH hold.
- **If a frame hitch makes several ticks catch up at once**: press and release edges are queued per tick (G4); the reach display clamps to `heldTicks / holdTicks` and never shows more progress than the Sim has counted.

### Rule collisions

- **If the team wallet changes mid-hold** (a teammate misdelivers or buys) **and `floorRefusal` flips**: the hold continues — `floorRefusal` is advisory, not a branch or an MD2 row — and the prompt's line updates within a tick; the host decides at completion (`ok` or `wallet_floor`).
- **If a matching entry appears during a MISDELIVER_ONE hold**: the recomputed branch becomes MATCH, which differs from the latched branch, so the hold cancels with "Mail changed — check the door" (MD4); the next press starts a MATCH hold. The player never completes a hold whose prompt said "Wrong house".
- **If a MATCH hold's matches vanish** (an own optimistic op snaps back) **while the selected slot holds mail**: the branch becomes MISDELIVER_ONE ≠ latched MATCH → cancel (MD4). Even without the cancel, the MATCH request carries no `selectedEntryId` and would return `no_target` (MD5).
- **If a teammate's throw arrives during the hold or at the press**: nothing enters the carrier's panel — a catch needs the carrier's own Interact press and a latched deliver hold is never converted (Player Character P11; Input & Controls R4.3). The item lands as a WorldItem; the hold is unaffected.
- **If the selected MISDELIVER_ONE entry is Cursed**: the prompt carries "Cursed — brings goons"; one Cursed unit misdelivers and fires `CursedMisdelivered` (rate-limited per destination by Mail & Destinations R14). **If that op stops at the wallet floor with 0 units consumed**: no event, no goons.
- **If a MATCH includes a Cursed entry for this address**: it is delivered as an Accept, ×1.5 value, no goons (Grid Inventory edge case on Cursed matches). A Cursed entry for another house is untouched.
- **If focus would pass to the neighbouring mailbox mid-hold**: the hold stays on the latched door while it remains within 2.9 m (G1/G2); after completion the neighbour's prompt shows "Delivering…" until the reply.
- **If a Toggle-mode hold is armed and the carrier walks beyond 2.9 m**: it cancels with the reason; nothing is sent. Between 2.5 and 2.9 m a completion is still accepted by the host (+500 mm slack over 300 ms, Player Character F5).
- **If `holdSetting` changes or Quick Hands is drafted mid-hold**: impossible in practice — settings live behind Pause and perks arrive in the Draft phase, both of which cancel the hold — and in any case `holdTicks` is latched at press (F2).
- **If a pending throw or sort is in flight when the carrier reaches a mailbox**: MD2 row 3 blocks the deliver hold until its reply; an own sort always replies (including `unchanged` and `sort_failed`, Grid Inventory rule 5), so the lock always clears, at worst by the 5 s resync.

### Lifecycle

- **If the client disconnects after sending and before the reply**: the host applies the request or the disconnect in FIFO order; a delivery keeps the player as `actorId`. On rejoin the snapshot clears the pending queue, so `deliver.pending` clears; missed `DeliveryOutcome` events are not replayed (Mail & Destinations Network).
- **If a request's reply never arrives**: the channel is reliable and ordered, so a lost reply means a lost connection; the 5 s `pendingReplyTimeoutSeconds` forces a resync (pending holds until its snapshot is applied, MD2 row 3) and "Connection hiccup — bag re-checked". If the op had applied, no hand beat plays; the door beats still play from `DeliveryOutcome`.
- **If a Dead or Frozen carrier had a Toggle-armed hold**: cancelled by the knockdown / on-break transition (Player Character P12/P19); the button is muted until released (G3); nothing fires at clock-in.
- **If the host disconnects**: the run ends (Player Character P19); there is no SENT recovery.
- **If the carrier mounts a vehicle mid-hold**: cancelled (Player Character P12: context change into Mounted).

### Network divergence

- **If the client's phase, unlocks or wallet lag the host**: the host's `Evaluate` and wallet decide; the reply (MD7) shows the truth; predicted cents are replaced by the outcome figure.
- **If the displayed panel includes a pending op the host later rejects**: a MATCH request returns `no_target`, never a misdelivery (MD5); the pieces come back out.
- **If the client's StatSheet lags a Draft pick by one event**: the prompt's predicted cents may differ from the paid amount for one round trip (Mail & Destinations F10 note); Draft is not a delivery phase, so this only affects the first hold of the next Delivery.

### Degenerate strategies

- **If a player repeatedly makes careless holds to dump unwanted mail**: each hold costs one unit at 50 % of its spawn value (no floor below 1 ¢), raw complaint per door bucket and a pyjama resident; a thrown stack dumps a whole stack in one action at the same per-unit price, so careless holds are dominated and not degenerate. Telemetry only (misdelivery ops `via = hand`).
- **If a player uses the hold to clear dead letters**: allowed — it costs the walk, pays 0 ¢, raises 0 complaint and frees bag space; Postal Rank XP counts only units with pay > 0 (Mail & Destinations OQ17), and fantasy test 3 counts only pay > 0 pieces, so it cannot farm either.
- **If a player macros the hold or sets 0.2 s**: equal to the slider minimum; the host checks no hold length (Input & Controls; friends-only trust). A modded client sending requests with no hold at all is accepted — out of scope under the anti-pillar "NOT hardened multiplayer"; no host throttle is added (MD6).
- **If a player parks at a mailbox holding Interact to post as mail arrives**: a completed hold fires once and needs a release (G6); arriving mail (only through the player's own pickups and catches, which need presses) never auto-posts.

## Dependencies

| System | Kind | GDD status | What this system needs from it | Bidirectional? |
|---|---|---|---|---|
| Player Character & Movement | Hard (upstream) | In Review | `IInteractable` registration (+ `GetHoldTicks` / `ShouldCancel` hooks, adopted), focus (P11, display-only in Panel), hold ticks (F4), cancel list (P12), host checks and range (P13/F5), carry pose (P20), Mounted / Dead contexts | yes — lists Manual Delivery (P10 verb row, Interactions, Dependencies) |
| Input & Controls | Hard (upstream) | In Review | Interact resolution at press (R4.3), Hold grammar G1–G9, `holdEff` (F1), Toggle (G7), ring delay (F12), prompt grammar, one-commit rule, ordered per-tick edges (G4) | yes — Dependencies row "Manual Delivery, Hard" |
| Grid Inventory & Containers | Hard (both) | In Review | `deliver_matching {mailboxId, selectedEntryId?, holdStartTick}` (R7, adopted) and its D-B rule, reply `requested`/`got` and reasons, displayed own panel and cursor, selected slot, pending queue, `pendingDebounceMs`, `pendingReplyTimeoutSeconds` | yes — Dependencies row "Manual Delivery, Hard" |
| Simulation Core | Hard (upstream) | Designed (pending review) | op queue and `opSeq` order, `IHostClock` (host tick domain for `holdStartTick`), ClientLayer `Previews` phase, loopback reply timing, u32 destination `EntityId` (R19) | yes — Dependencies row "Manual Delivery, Hard" (HB8 adopted) |
| Mail & Destinations | Hard (upstream) | In Review | `Evaluate` (client copy), destination registry and anchors, `manualInsertHold`, value hint (R17) and F5–F8 / F10, reason keys, `DeliveryOutcome` (grouped per hold, R16), Cursed rule (R14) | yes — Interactions and Dependencies rows "Manual Delivery" |
| Economy, Wallet & Shop | Soft (read) | Not Started | replicated team wallet; `walletFloor` −500 ¢ (prediction only — the host's wallet decides) | provisional — that GDD must list Manual Delivery as a reader |
| Run Structure | Soft (read) | Not Started | RunState `phase`, `unlockedDistricts`, `currentShift` | provisional |
| Perks & Perk Draft; Content & Data Definitions | Soft (upstream) | Perks Not Started; Content & Data In Design | StatSheet `DeliverInsertPm` (`insertPm`) with Quick Hands = 500 on every deliver hold (MD9); `manualInsertHoldSeconds` data field | Content & Data yes (reads `DeliverInsertPm`); Perks provisional |
| HUD & Notifications | Hard (downstream) | In Review | renders this system's prompt content and the ring; pills and feed from `DeliveryOutcome` | yes — H1 / H11 "deliver body → Manual Delivery" (H11 renders the branch table and the reason strip at its F1 lifetime — OQ5 closed) |
| Throwing & Catching | Sibling | In Review | shares `Evaluate` and the Interact button (catch wins at press; a deliver latch is never converted); shared hand-share trigger (fantasy test 3) | yes — lists Manual Delivery |
| Presentation & World Dressing; Audio | Soft (downstream) | Not Started | hand-side beats and cues (Visual/Audio) | provisional |
| Networking & Session | Hard (M0+) | Not Started | the request / reply ride Grid Inventory's channel; no own state (Network) | provisional |

**Sim / Presentation split** (spec 07 §4). *Sim assembly (plain C#, deterministic):* MD1 anchor registration; MD2 rows 2, 4, 5 as evaluated by the host; MD6's host check; the deliver preview MD3 and its formulas F1–F2 as a pure client-side function reusing Mail & Destinations' `Evaluate` and F10 code — it runs on the owning client's Sim-side client layer, not inside the authoritative tick, and produces data only (branch, counts, cents, reason key). *Presentation (Godot):* prompt rendering (HUD), the label turn, the pre-reach, reach and riffle animations, hand-side audio, the hold ring. *Network:* not in the Sim assembly — see the Network section. No Presentation value ever feeds the Sim.

**Engine reference.** No `docs/engine-reference/godot/modules/` directory exists, so this system's engine surface (Input event ordering, UI live-region, the multiplayer channel) was **not checked against the pinned Godot 4.7.1**: NOT SOURCEABLE — input-event ordering within a frame, display / vsync latency for the ≤ 17 ms targets, and the multiplayer API (Grid Inventory OQ12). Sourced from `breaking-changes.md`: `Control.accessibility_live` is typed `AccessibilityServer.AccessibilityLiveMode`; mouse / keyboard device ids are `InputEvent.DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD`.

## Tuning Knobs

*Knobs owned here; knobs owned elsewhere are referenced, not duplicated.*

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `insertPm` for Quick Hands (StatSheet value; key owned by Content & Data, value by Perks) **PROPOSED** | 500 | 250–750 (StatSheet clamp) | every deliver hold's length (F2) | < 250 the perk is imperceptible (≤ 3 ticks) | > 500 changes nothing at the default setting (already on the 6-tick floor); only slower settings gain | `holdSetting`, `minHoldTicks` |
| `branchSettleTicks` **PROPOSED** | 6 | 4–8 | how long a ✗ must be displayed before a wrong-house hold can start | < 4 is shorter than the prompt crossfade plus a frame — a flip the player never saw can be held | > 8 makes a deliberate careless hold feel refused | Player Character `focusDwellTicks` (4) |
| Deliver anchor `priority` | 2 (Player Character default) | Pick-up + 1 (absolute 2; Player Character range 0–3) | mailbox vs a parcel lying at its foot (F4 bias ≈ 625 mm) | equal to Pick-up (1): a bounced parcel can steal focus from the door | ≥ 3: a mailbox steals focus from a parcel you are looking at | Player Character `wAng`, `wDist` |
| `riffleStepMs` / `riffleCapMs` | 60 / 300 | 40–80 / 200–400 | sweep readability (F4) | < 150 ms total is unreadable | > 400 ms reads as a delay | — |
| Pre-reach length (Visual/Audio) | 0.2 s | 0.15–0.25 s | anticipation on focus gain | below the art bible's ≥ 0.2 s wind-up intent | sluggish focus | `minHoldTicks` |

**Referenced, owned elsewhere:** `manualInsertHold` 0.4 s (Mail & Destinations; safe 0.2–0.8 s — shorter makes the hand costless so throws lose their job, longer loses the hand to the throw, the M-1a finding); `holdSetting` 0.2–1.0 s, `holdFloor`, `holdCeil`, `minHoldTicks`, ring delay (Input & Controls); `rangeMm`, `incumbentSlackMm`, `focusDwellTicks` (Player Character); `pendingDebounceMs` 250, `pendingReplyTimeoutSeconds` 5 (Grid Inventory); `walletFloor` (Economy); reason-strip lifetime (HUD & Notifications F1).

## Visual/Audio Requirements

*From art-director; consistent with art bible §1–§5 and §7. Hand side only — the door (flag, cha-ching, wobble, resident, REFUSED, closed / taped states and their cues) is Mail & Destinations' Visual/Audio; the ring, chip, cancel drain, "Hold" pulse and slash are Input & Controls'; carry poses are Player Character's. Motion follows the bible's squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s. Cue names are proposals for audio-director.*

| Event | In-world (carrier, held piece, flap) | Screen-space | Audio |
|---|---|---|---|
| Stance on / off | the piece keeps its class carry pose and turns its label to the camera over 0.2 s; off turns it back; hands or a tool give no stance | — | `sfx_hand_stance_raise` (soft paper flip), local only |
| The read (focus gain) | head flicks from label to door number (0.1 s), then a **0.2 s pre-reach**: the hand lowers to ≈ 25 % reach — identical for ✓ and ✗, so it never prejudges the match. Item numeral readable ≤ 3 m, glyph ≤ 10 m | — (the prompt is HUD's) | — (focus tick is Player Character's) |
| Reach (hold in progress) | an upper-body layer **scrubbed by hold progress** (pose keyed to `heldTicks / holdTicks`, never a time-scaled clip) over the remaining 75 % of reach; a 6-tick perked hold shows 200 ms of motion after the pre-reach; a 30-tick hold stretches the travel and creeps the flap; arm stretch ≤ 1.3× (§5) plus a **visual-only lunge** of ≤ 0.8 m (rig root offset, never the Sim) because the anchor is up to 2.9 m away **PROPOSED** | — (the ring is Input's) | `sfx_hand_reach_rustle`, quiet, non-rising (Input owns the rising tick); skipped for holds < 8 ticks |
| Signature beat | hand rests in the slot, flap held, carrier still; ≥ 0.15 s, ≤ ≈ 0.25 s | — | `sfx_hand_slot_in` (paper slide + flap clack, ≤ 80 ms, ducks under the cha-ching) |
| Sweep riffle (k ≥ 2) | the backpack flap lifts; 1–5 stacks (existing mail meshes) feed in at 60 ms steps, 300 ms cap (F4) | — (the in-world "+N" pill is **cut**: Mail R16's per-hold "+N¢ · k stacks" pill carries the count — the CD-GDD-ALIGN condition) | `sfx_hand_riffle_step`, +1 semitone per stack, capped at +5 |
| Careless handover | one piece, both mitts, a slow tiptoe slide, held ≥ 0.15 s; no wink | — | `sfx_hand_careful_slide` (the door then plays `sfx_house_wrongdoor`) |
| Pieces come back | the piece slides out of the slot into the hand with a 0.8 squash, once per hold | reason line (HUD / Input) | `sfx_hand_return` |
| Nothing for this address | pat hips, pat chest, small shrug (≈ 0.6 s); no reach | the prompt line | `sfx_hand_pocketpat` (two soft pats) |
| Closed / taped | the reach stops at the plate or tape; two knuckle taps (0.25 s) | the prompt line | `sfx_hand_knuckle_tap`, a high wood tick — distinct from, and never doubling, Mail & Destinations' `sfx_mailbox_closed_knock` |
| Hold cancelled | the reach scrubs back over 0.1 s; the flap drops | Input's drain / dissolve | `sfx_hand_withdraw` (cloth), local only |

**Art-bible conflicts resolved:** the stance keeps §5's carry poses (no "chest height" raise); the strain face stays reserved for throws — the stance uses the neutral face and a ≤ 5 cm hat lift; the mailbox **flap** must be a separate rotating node inside the 150-tri mailbox budget (§8, technical-artist check).
**Remote players** see the stance with a generic label (whether it carries the district swatch is an Open Question); they get no reach (no progress data — Network). On a `DeliveryOutcome` within 15 m the remote carrier plays only the 0.15 s slot-in pose and the riffle (k from the op count), timed to the door beat — never predictive, no lunge; Rejects (closed, not on route, too far, nothing) produce no outcome, so remote players see nothing; a wallet-floor stop is broadcast (Mail & Destinations R16, `stoppedAtFloor`), so remote carriers within 15 m play the pieces-come-back pose under the door's REFUSED stamp — a crew too broke to be wrong is a shared joke (Fellowship); beyond 15 m the hand beat is skipped.
**Reduced motion:** stance turn and head flick are cuts; reach steps in quarters matching the ring ticks, with no lunge or overshoot; the riffle is a single 0.1 s frame (the count is the per-hold pill's); pieces-out, pockets-pat and knuckle tap become one held pose of ≥ 0.3 s. **Colour-blind:** nothing here relies on colour (✓ / ✗ are the prompt's, with shapes); every hand beat is distinguished by pose and sound.
**Audio rate limits (for audio-director to ratify):** hand cues share the mailbox's 250 ms full-cue window and the door cue wins a collision (`sfx_hand_slot_in` is dropped); the local player's own cues are never dropped; remote hand cues only within 15 m at −6 dB, ≤ 2 concurrent riffle voices, remote slot-in suppressed whenever a cha-ching plays at that door; per player: pocket-pat ≤ 1 per 1 s, knuckle tap ≤ 1 per 0.5 s and suppressed if the door's closed-knock fired in the last 250 ms, return and withdraw ≤ 1 per hold; the reach rustle starts after the ring delay.
**PROPOSED, pending art bible updates:** §5 — a progress-scrubbed interact / reach layer with the pre-reach, the visual lunge, the stance label turn, the head flick, pockets-pat / shrug, the two-mitt careful handover, the knuckle tap, and a note that the ≥ 0.2 s wind-up is satisfied by the pre-reach for repeated verbs; §3 / §8 — the mailbox flap node; the backpack flap-lift; all `sfx_hand_*` cues.

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:manual-delivery` to produce per-asset specs (reach / pre-reach layer, stance label turn, pockets-pat, knuckle tap, careful handover, flap node, nine `sfx_hand_*` cues).

## Game Feel

*From gameplay-programmer. Consistent with Mail & Destinations' Game Feel (which owns the door's timings) and Input & Controls' Game Feel. References: Overcooked 2's serving hatch (instant, consistent), Stardew's shipping bin (one action, everything goes).*

**Tick quantisation.** The Sim runs at 30 Hz, so a press can wait up to 33 ms before the Sim latches it. Mail & Destinations' "arm + ring ≤ 17 ms" is therefore met by a **visual-only reach echo**: the Presentation starts the reach on the input event when the Sim-published `canStartHoldNext` (MD3 tracker, settle included) was true on the last tick — Presentation never recomputes it — and withdraws it within 0.1 s if the Sim then refuses the press. The ring itself appears at Input & Controls' `ringDelay` (± 1 frame) with true progress, so the ≤ 17 ms target applies to the reach, not the ring.

**Input responsiveness** (frame capture at 60 fps): reach starts ≤ 17 ms after the press event; a press on an unavailable verb pulses the prompt ≤ 50 ms (1 tick + 1 frame); the prompt updates ≤ 50 ms after any input change; a release or cancel starts the withdraw ≤ 1 frame after the Sim sees it (0.1 s long); the neutral slot-in starts ≤ 33 ms after the completion tick. Wall-clock hold length is `holdTicks × 33.3 ms` plus ≤ 33 ms of press quantisation.

**Predicted vs authoritative.** *Local:* stance and label, ✓ / ✗, counts and "+N¢" (labelled predicted), reach and ring progress (from Sim ticks, `heldTicks / holdTicks`), cancel beats, the neutral slot-in. *Waits for the server:* pieces leaving the panel (container events, which arrive before the reply and are applied in one batch with it), the riffle (k from the reply's `got`), wallet, flag, coin, wobble, refusals and "pieces come back". Panel entries vanish on the same frame the riffle starts.

**Refusal timing.** The hand rests in the slot until the reply; pieces-out starts ≤ 1 frame after it and lasts ≤ 0.15 s; the door's row starts ≤ 0.1 s into the pieces-out; the verdict is visible ≤ 0.25 s after completion at 120 ms RTT and ≤ 0.2 s on the host (the loopback reply must reach the client layer in the same or next tick, with no frame-delayed queue). After 1 s without a reply the hand returns to idle (MD7).

**The sweep** (F4) runs on the Presentation clock, ≤ 0.3 s, and gates neither movement nor the next hold. Movement is never locked in any state of this system.

**Weight and responsiveness profile.** Crisp and certain: the anticipation is deliberate (the read and pre-reach), cancel and withdraw are instant, a loaded carrier is slow but never sluggish at the slot. Quick Hands holds (6 ticks, 200 ms at the default setting) match the neutral slot-in squash (0.2 s), which still hides the round trip.

**Feel acceptance criteria.** (FA-MD-1) Reach starts ≤ 17 ms after the press and the slot-in ≤ 33 ms after completion, by frame capture; the ring appears at `ringDelay` ± 1 frame with true progress. (FA-MD-2) The verdict beat starts ≤ 0.25 s after completion at 120 ms simulated RTT, ≤ 0.2 s on the host, and never before the server outcome. (FA-MD-3) On a refusal, pieces-out starts ≤ 17 ms after the reply, finishes ≤ 0.15 s, and the door row follows ≤ 0.1 s later. (FA-MD-4) With a 20-entry sweep running, a new hold's reach starts ≤ 17 ms after its press and walk-input latency is unchanged (≤ 50 ms). (FA-MD-5) Presses dropped during `deliver.pending` are logged and reported as a share of all Deliver presses (signal for the Open Question on a press buffer).

*NOT SOURCEABLE in `docs/engine-reference/godot` (no `modules/`): input-event ordering within a frame (`_Input` vs `_UnhandledInput`), display / vsync latency. The ≤ 17 ms and ≤ 33 ms targets must be measured by frame capture on the real build, not assumed.*

## UI Requirements

*From ux-designer. This system owns the deliver prompt's **content**; HUD & Notifications renders it; Input & Controls owns the grammar ("Hold [E] Deliver · object", ring, Toggle drops "Hold", ≤ 2 prompts, ≥ 18 px at 1080p, slash + reason for a disabled verb, never 60 % opacity); HUD H11 owns placement (screen-fixed under the reticle, no leader line); Player Character owns dimmed + lock + reason and the caret at 2.5–6 m. Creative-director wording rulings: "Wrong house — posts 1", never "misdeliver" on screen; lead with pieces, show stacks only when ≥ 2; predicted cents already include late and dead value.*

**Line 1 follows Input & Controls' grammar with the mark leading:** `✓ Hold [E] Deliver · D` / `✗ Hold [E] Deliver · D`, where `D` is the **mailbox's** address token (swatch-with-glyph + "17 Elm St", from World Generation) — the grammar's object slot, matching the plate on the physical mailbox. The verdict therefore sits on the first line read (CD ruling v); the mark slot before the verb is Input & Controls' prompt grammar and HUD H11's example (both adopted). `P` below is the address on the piece. **The prompt is at most 3 lines plus one chip / strip row — 4 in total.**

| Branch | Line 2 | Line 3 (optional) | Mark / chips / ring | Live-region text (separate full-sentence key, `{binding}` argument) |
|---|---|---|---|---|
| MATCH, k = 1 | 12 pieces · +86¢ | — | ✓, solid ring | "Delivers 12 letters to 17 Elm Street, about 86 cents. Hold {binding}." |
| MATCH, mixed kinds | 15 pieces · +86¢ | kind chips (✉ 12 ▣ 3) | as above | kinds spelled out: "12 letters, 3 parcels" |
| MATCH, k ≥ 2 | 41 pieces · 3 stacks · +86¢ | — | as above | adds "in 3 stacks" |
| MATCH with late units | 15 pieces · +52¢ | clock icon "3 late" | clock icon + word | "3 late, reduced pay" |
| MATCH, `deadOnly` | Clears 2 dead letters · 0¢ | — | stays ✓ | "Clears 2 dead letters, no pay." |
| MATCH while the raised piece is for another house | as MATCH | pin icon: "Raised piece stays: P" (≥ 4.5 : 1 contrast, never alpha-dimmed) | as MATCH | "Your raised piece for 9 Oak Court stays in hand." |
| MISDELIVER_ONE | Wrong house — piece is for P | Posts 1 · −4¢ | ✗ + ⚠, plain ring; the selected hotbar slot is highlighted | "Wrong house. Piece is for 14 Larch Lane. Posts one, about 4 cents lost." |
| MISDELIVER_ONE, `cursed` | as above | Posts 1 · −4¢ + skull chip | ✗ + skull chip, hazard-striped ring | adds "Cursed, brings goons." |
| MISDELIVER_ONE, `floorRefusal` | as above | Can't afford a wrong house (−4¢) | ✗, hazard-striped ring; verb stays enabled | adds "Probably refused." |
| NOTHING | — | — | **one dim line** "Nothing for D", no glyph, no lock | "Nothing for this address." |
| REJECT closed / not_on_route | Closed — opens at Delivery / Not on today's route | — | slashed glyph, lock | reason text |
| Panel open | Close panel to deliver | — | **no button glyph**, lock only | reason text |
| Swimming | Dry land required to deliver | — | slashed glyph, lock | reason text |
| Pending / SENT | Delivering… (Sorting…) | — | normal verb row; static spinner after 250 ms | "Delivering." — the final cents come from the outcome pill |
| HOLDING | the latched branch's lines, layout frozen | as latched (numerals may tick) | ring fills | silent until the outcome |
| Toggle ARMED | line 1 reads "Armed — [Esc]/[B] cancel · D" (`deliver.verb.armed`) | as latched | stop-mark chip (Input & Controls) | "Posting, press again to cancel." |
| Reply failure / cancel / settle refusal (MD4, MD7) | **transient strip** below the live lines for HUD F1's lifetime (H11); the live branch stays visible and pressable | — | lock icon + line | the line, in its own announcement category |

**Focus clarity.** Prompt placement is HUD H11's (screen-fixed, no leader line); this system asks only that the focused mailbox get Player Character's bracket outline (a HUD protected rect, OQ-IC-11); a neighbouring in-range mailbox shows only a dim caret; focus changes crossfade in ≤ 100 ms with no sliding.
**Accessibility.** Branches differ by mark shape (✓ / ✗ / lock), the ⚠ and skull chips and the signs "+" / "−"; the hazard ring is reserved for Cursed and floor refusal and is never the only cue (holds of < `ringDelay` show no ring); districts are glyph + name; late is clock icon + word. Screen reader: outcome first, then address, figure, action; cents always "about"; the binding is the `{binding}` argument, never a literal key. Announcements: a ✓ or reason prompt is announced after focus has been stable for 0.5 s and the pending announcement is replaced (never queued); a **✗ is announced immediately at assertive priority**, and in screen-reader mode the settle lasts until that announcement has fired (MD4); count or cent ticks at the same mailbox are never announced (only a branch change); outcomes and failures have their own category, never swallowed by the rate limit. Input & Controls binds "Read prompt" (M4, unbound by default). Gamepad: the device glyph set, always with its text label; glyphs are inline tokens, never hard-coded. Text 150 %: the prompt is ≤ 40 % of screen width and grows upward from its bottom anchor; an address is never truncated. **EN budgets:** line 1 ≤ 20 characters plus World Generation's longest address token; line 2 ≤ 36 including `P`; line 3 ≤ 28. **Overflow order** when the 4-line cap binds: drop the raised-piece note, then kind chips (live region keeps them), then the stacks count — never the mark, an address or the cents. Verify at 1280 × 720.
**Localisation keys** (no concatenated addresses — `{address}` is a rich token; money uses a `{money}` formatter, never a literal "¢"): `deliver.verb.hold {glyph, address}`, `deliver.verb.toggle {glyph, address}`, `deliver.verb.armed {glyph, address}`, `deliver.match.pieces {pieces}` (plural), `deliver.match.stacks {stacks}` (omitted when k < 2), `deliver.match.kind {kind, count}`, `deliver.match.pay {money}`, `deliver.match.late {n}`, `deliver.match.dead_only {n}`, `deliver.keep_raised {address}`, `deliver.mismatch.piece_for {address}`, `deliver.mismatch.post_one {money}`, `deliver.mismatch.cursed` (chip tooltip), `deliver.floor_refusal {money}`, `deliver.nothing`, `deliver.close_panel`, `deliver.dry_land`, `deliver.pending`, `deliver.sorting`, `deliver.reply.too_far`, `deliver.reply.timeout`, `deliver.cancel.selection_changed`, `deliver.cancel.prompt_changed`, `deliver.cancel.settle`; Reject reasons reuse `mail.reject.*`; live-region sentences are separate keys per variant (`a11y.deliver.match`, `.match_multi`, `.mismatch`, `.mismatch_cursed`, `.mismatch_floor`, `.nothing`, `.armed`; each takes `{binding}`) so translators never handle conditionals.

> **📌 UX Flag — Manual Delivery**: This system has UI requirements. In Phase 4 (Pre-Production), run `/ux-design` to create a UX spec for the deliver prompt (all branches above, focus clarity, live-region behaviour) **before** writing epics — it can share a spec with Input & Controls' interact prompt and hold ring. Stories that reference UI should cite `design/ux/[screen].md`, not this GDD directly.

## Network

*Spec 06 §4 vocabulary: **E** event, **R** request / validate / apply, **S** snapshot. Host-authoritative (spec 13 §3). The Godot 4.7.1 multiplayer API is NOT SOURCEABLE in the engine reference (Grid Inventory OQ12); this is a contract on the adapter, which must provide reliable ordered delivery.*

**Replicated state: none of its own.** Manual Delivery keeps no state between ticks on the host and sends no state of its own.

| Data | Strategy | Who receives | Owner / notes |
|---|---|---|---|
| `deliver_matching {mailboxId, selectedEntryId?, holdStartTick}` | R (client → host) | host | rides Grid Inventory's op channel and FIFO; no hold duration, no predicted figures. **Layout (canonical packed, transport framing excluded):** op code u8 · version + presence flags u8 · `reqId` u16 (Grid Inventory Network rule 8) · `mailboxId` u32 (destination `EntityId`, Simulation Core R19) · `holdStartTick` u32 (host tick domain, MD5) · `selectedEntryId` u32 only on a MISDELIVER_ONE hold = **12 B MATCH / 16 B MISDELIVER_ONE** (≤ 16 B). A `selectedEntryId` that is a temp id from a still-pending optimistic split resolves by Grid Inventory's `dependsOn` rule or fails `stale_entry` (pieces come back) |
| reply (`ok`, `requested` / `got`, reason) | R reply | requester only | Grid Inventory; container events arrive before the reply on the same channel |
| `DeliveryOutcome` per op | E | every peer | Mail & Destinations (≤ 19 B, its M-NET-2); the hand beat (local and remote) is keyed from it |
| deliver preview, prompt content, hold progress, stance label | not sent | — | computed locally from state every client already holds (own panel, value hints, RunState, StatSheet, wallet) |
| held item kind for stance | inside Grid Inventory's public `PlayerState` | every peer | Grid Inventory |
| remote "posting" flag | **not sent** **[D]** | — | the reach would arrive ≈ 120 ms + latency late for a 0.13–0.4 s hold, and a flag on unreliable transform packets needs expiry logic; remote peers play the slot-in on `DeliveryOutcome` instead. Player Character reserves one spare flag bit (aim-pitch byte of its 16 B transform packet) for it if playtests show friends cannot tell who is posting (Open Questions 11) |

**Bandwidth.** ≤ 1 request per hold per player; at 8 players posting at a combined ≤ 3 holds/s ≤ 48 B/s up in total — negligible against spec 06 §4.2; the reply's size is Grid Inventory's. Outcome bandwidth is Mail & Destinations'.
**JoinState / RejoinState contribution: none** (0 B). A rejoining player's pending queue is cleared by Grid Inventory's snapshot, which clears `deliver.pending` (MD2 row 3); missed outcomes are not replayed.
**Host.** The host's own holds go through the same loopback request path, so the host player gets the same MD5 guarantee and the same replies, with an exact `holdStartTick` (offset 0); its reply must reach the client layer in the same or next tick (Game Feel).

## Cross-References

| Referenced GDD / doc | Sections here | What is referenced | Status |
|---|---|---|---|
| `design/gdd/grid-inventory.md` | Overview, MD3–MD7, Edge Cases, Network | `deliver_matching` and the D-B rule (R7 table), deliver order, OQ4 one-unit hand misdelivery, contexts (World / Panel / Holding), H-R7-11 (absent `selectedEntryId` → `no_target`), Network rules 1, 4, 5, 7, `pendingDebounceMs`, H-FX-1 route input count, public `PlayerState` | In Review — reconciled (OQ4 adopted) |
| `design/gdd/mail-destinations.md` | MD1–MD3, MD6–MD7, F1, Player Fantasy, Visual/Audio, UI | R3 registry and `manualInsertHold`, R4 states and phase flip, R5 `Evaluate`, R6 tick order, R8 floor, R12 hands never throttled, R14 Cursed, R16 `DeliveryOutcome`, R17 hint, F5–F8, F10, Feel & Comedy and Visual/Audio door rows, Game Feel timings, reason strings, OQ5 Quick Hands | In Review — reconciled (OQ3 adopted) |
| `design/gdd/player-character-movement.md` | MD1, MD2, MD4, MD6, MD8, Dependencies | P10 `IInteractable` and verb row, P11 focus (display-only in Panel, catch latch), P12 cancels, P13 host checks, F4 score and hold ticks, F5 range check, P19 on break, P20 carry pose, fantasy test 1 hand-share threshold | In Review — reconciled (OQ1, OQ11 adopted) |
| `design/gdd/input-controls.md` | MD4, MD5, F2, Game Feel, UI | R3 grammar G1–G9 (latch, cancel, mute, ordered edges, one-commit, Toggle), R4.3 Interact resolution, R4.4 tap on a hold target, F1 `holdEff`, F12 ring delay, prompt grammar and live-region rules | In Review — reconciled (OQ2 adopted) |
| `design/gdd/hud-notifications.md` | UI Requirements, Dependencies | H11 renders the deliver prompt body supplied here; HN-4a "posts 1" | In Review — reconciled (OQ5 closed) |
| `design/gdd/throwing-catching.md` | Player Fantasy, Interactions | the sibling verb; catch wins at press; shared `Evaluate`; test 1 / OQ1 hand-share trigger and levers | In Review |
| `design/gdd/simulation-core.md` | MD3, MD5, MD6, Network, Dependencies | R6 ops and `opSeq`, R19 destination `EntityId`, `IHostClock`, ClientLayer `Previews`, HB8 | Designed (pending review) |
| `design/gdd/game-pillars.md` | Player Fantasy, F3 | Pillar 3 ratified reading; Pillars 1, 2, 5 | Approved |
| `design/art/art-bible.md` | Visual/Audio, Player Fantasy | §5 carry poses and wind-up, §7 motion and colour rules, §8 mailbox budget, principles 1–2 | Draft (AD revised) |
| `design/spec/09-ui-ux.md` §3.1, §5 | Overview, UI | the original manual delivery flow (its step 4 "Deliver all matching (F)" is superseded by D-B) and accessibility | spec — superseded in part |
| `design/spec/03-mail-inventory-economy.md` §2.1–§2.2, §3.4 | MD1, MD5 | hold interact 0.4 s, acceptance rules | spec |
| `design/spec/10-content-tables.md` §4.1 | MD9 | Quick Hands "Mailbox insert hold −50 %" (Carrier perk category) | spec |
| `design/spec/13-v1-cut-and-decisions.md` §2, §4.3, §6 | Overview, MD5, Network | v1 hand delivery; friends-only trust; M-1a gate | spec (wins on conflict) |
| `prototypes/m1a-delivery-throwing-concept/REPORT.md` | Player Fantasy, Tuning Knobs | 0.4 s hold not slow; throwing preferred over single-item hand delivery | prototype evidence (one tester) |

## Acceptance Criteria

*From qa-lead (2026-10-16). Tags follow `input-controls.md`: **[Logic]** = xUnit on the plain-C# Sim and client layer, no Godot (`tests/unit/manual-delivery/`); **[Integration]** = headless Godot, or two systems' public interfaces with stubs; **[UI]** = Godot against a scripted Sim stub plus a retained screenshot; **[MANUAL]** = evidence in `production/qa/evidence/`. All M-1a. At `qa.level: full` every non-MANUAL criterion blocks Done, except millisecond thresholds, which stay ADVISORY until the M1 reference machine exists. Parametrised rows report as separate cases. Tick = 1/30 s; `holdSetting` 0.4 s unless stated. qa-lead flags 1–4 (settle boundary, request-size cap, reason-line duration, evaluation-counter seam) were resolved in MD3, MD4, MD7, F5 and Network before these criteria were fixed; flag 7 (`got < requested`) is dormant for houses in v1 and has no criterion until a destination type with an accept limit exists. **Case splitting:** every lettered clause and every semicolon-separated GIVEN / THEN inside a bullet reports as its own case (`M-MD4-3.1`, `.2`, …). **BLOCKED-ON:** M-UI-3 on the unsourced Godot 4.7.1 accessibility API; FX-W on the Economy GDD (stub until then).*

**Fixtures.** **FX-MB**: house mailbox #17 at 17 Elm St, OPEN, Delivery phase, shift 3, wallet 0. **FX-L**: letter, spawn value 8 ¢, penalty 4 ¢. **FX-P**: a panel with entries placeable at cursor, hotbar, inventory and backpack. **FX-W**: stub wallet, floor −500.

### Core rules

- **M-MD1-1 [Logic]** GIVEN 10 house destinations and one test type with `manualInsertHold` 0, WHEN anchors register at run start, THEN there are exactly 10 Deliver anchors (slot 0, Hold 400 ms, range 2 500, priority exactly one level above Pick-up's, read from the Players `VerbCatalogue` code table (`VerbCatalogue.DefaultPriority(Verb.PickUp) + 1`), not a content field — *erratum 2026-10-22: was "Player Character's Pick-up priority read from its data"; owner ruling, Evan, 2026-10-22 7:13 PM PT, Mail Dev chat: "Keep the verb catalogue as the source (recommended)"; decision log 2026-10-22, "Deliver's priority comes from the Players `VerbCatalogue`"*, the mailbox's only verb, `GetHoldTicks` / `ShouldCancel` hooks present), the test type has none, and none deregister from Prep to Results. **b [Integration]** GIVEN a parcel lying at the foot of #17, WHEN the carrier faces the door from 1.5 m, THEN focus is Deliver; WHEN the carrier looks down at the parcel, THEN focus is Pick-up (positions, angles and the expected F4 scores computed from Player Character's data are asserted).
- **M-MD2-1 [Logic, 6 + 5 rows]** GIVEN each condition alone (Panel open; swim depth; an own pending `deliver_matching` / `throw` / `drop` / `take_all` / `sort`; CLOSED; LOCKED; preview NOTHING), WHEN `IsAvailable` runs, THEN it returns `deliver.close_panel`, `deliver.dry_land`, `deliver.pending` (`deliver.sorting` for sort), `mail.reject.closed`, `mail.reject.not_on_route`, `deliver.nothing` respectively; each adjacent pair (1+2, 2+3, 3+4, 4+5, 5+6) returns the lower-numbered row's key; with none, `ok`. A press in each unavailable row starts 0 holds and sends 0 requests.
- **M-MD2-2 [Logic]** GIVEN a pending own `deliver_matching`, WHEN (a) the reply arrives, or (c) a rejoin snapshot clears the queue, THEN `deliver.pending` is false on that same tick, with no separate flag in the code (the pending queue is the only source); WHEN (b) the 5 s timeout fires, THEN it stays true until the resync snapshot is applied and is false on that tick; while pending it holds at every mailbox and a press sends 0 requests.
- **M-MD2-3 [UI]** GIVEN a pending op, WHEN its reply lands at 100 ms, THEN no dimmed / lock prompt ever renders at the next door; WHEN it is still pending at 250 ms, THEN the dim shows; a press at 100 ms pulses the prompt and sends nothing.
- **M-MD2-4 [UI]** GIVEN the panel open with #17 focused, THEN the prompt reads "Close panel to deliver" with a lock icon and **no** button glyph (screenshot); WHEN Interact is pressed, THEN the op log stays empty and nothing is queued.
- **M-MD3-1 [Logic, 7 rows]** GIVEN FX-P, THEN the branch is: REJECT with 0 panel entries scanned when #17 is CLOSED and matches exist; MATCH when a 17 Elm St letter is only in the backpack; MATCH when the only match is the selected entry; MATCH when the match is the cursor entry with the panel closed; MATCH when hands are selected and a match exists; MISDELIVER_ONE when there is no match and the selected slot holds a letter for 14 Larch Lane; NOTHING when there is no match and a tool is selected.
- **M-MD3-2 [Logic]** GIVEN 2 matching letters (value 9, deadline 1, shift 3), THEN the branch is MATCH with `deadOnly` true and `predictedPay` 0; GIVEN a selected Cursed non-match, THEN `cursed` is true; a non-Cursed one gives false.
- **M-MD3-3 [Logic]** GIVEN a focused Deliver anchor for 30 ticks, THEN the evaluation counter rises by exactly 30 (no cache); IDLE evaluates 0 times; two client instances with identical inputs give identical branches; a CLOSED destination scans 0 entries (scanned-entries counter).
- **M-MD3-4 [Logic]** GIVEN a reply plus snap-back that flips MATCH to MISDELIVER_ONE and an Interact press in the same tick, WHEN the tick runs, THEN the press is judged on the new branch and no hold starts.
- **M-MD4-1 [Logic]** GIVEN MISDELIVER_ONE first displayed at tick T, WHEN Interact is pressed at T, T+5 and T+6, THEN T and T+5 pulse the ✗ line, show `deliver.cancel.settle` and start no hold, and T+6 starts one. **b** A MATCH branch displayed for 0 ticks starts a hold on the press tick. **c** GIVEN ✗ (entry A) at T, ✓ at T+2, ✗ (A) at T+3, THEN the earliest hold starts at T+9; selecting another wrong-house entry B at T+4 moves it to T+10; a `floorRefusal` or cents change does not move it. **d** Toggle mode's arming press obeys the same settle. **e** In screen-reader mode no ✗ hold starts before the ✗ announcement event has fired, even after T+6.
- **M-MD4-2 [Logic, 6 rows]** GIVEN a hold latched at tick 0, WHEN at tick 6 (a) a MATCH hold's matches vanish by snap-back (branch → MISDELIVER_ONE), (b) a matching entry appears during a MISDELIVER_ONE hold, (c) the selected slot empties (→ NOTHING), (d) the carrier enters swim depth, (e) an own throw becomes pending, THEN the hold cancels with (a–c) `deliver.cancel.prompt_changed`, (d) `deliver.dry_land`, (e) `deliver.pending`, progress is 0, and 0 requests are sent through the completion tick; WHEN (f) a MATCH hold's `k` drops from 3 to 2 and it stays MATCH, THEN it completes and sends 1 request.
- **M-MD4-3 [Logic]** GIVEN a MISDELIVER_ONE hold with slot 3 selected, THEN selecting slot 4 at tick 5 cancels with `deliver.cancel.selection_changed`; 3 → 4 → 3 inside one tick does not cancel; the entry retired by `stale_entry` cancels; on a MATCH hold a selection change does nothing.
- **M-MD4-4 [Logic]** GIVEN Toggle mode, WHEN Interact is pressed once, THEN the hold completes after the same `holdTicks` with the button released; a second press or Back cancels (Back does not also open Pause); a second press on the completion tick cancels and sends 0 requests; walking beyond 2.9 m cancels with 0 requests; a completion at 2.7 m is accepted by the host.
- **M-MD5-1 [Logic]** GIVEN a completed hold, THEN exactly one `deliver_matching` is sent; on MATCH the op log has no `selectedEntryId` field; on MISDELIVER_ONE it equals the latched entry id; `holdStartTick` equals the client's host-tick estimate on the tick the hold began (12 ticks before the send at the default setting, with a test-injected clock offset); there is no duration or count field.
- **M-MD5-2 [Integration]** GIVEN a MATCH hold, WHEN the host has already removed the matching entries (an own pending op the display had replayed), THEN the reply is `no_target`, 0 units are consumed, the wallet is unchanged and no misdeliver outcome exists. **b** GIVEN a MISDELIVER_ONE request whose host state holds a match, THEN the match is delivered and 0 penalties are charged.
- **M-MD6-1 [Integration]** GIVEN a request naming a destination with no Deliver anchor (fixture dock), THEN the reply is `no_target`; GIVEN the carrier's reported position 2 900 mm from the anchor, THEN it is accepted; at 3 200 mm it is `out_of_range`; two requests from one player one tick apart are both applied (no host throttle).
- **M-MD6-2 [Logic]** GIVEN Delivery closing at the start of tick T, WHEN a hold begun at T−12 applies at T−1, T+5 or T+90, THEN Accept; applied at T+91, `closed`; a hold begun at T (press refused by MD2 row 4 on the client; forced through a test client) → `closed`, nothing consumed, wallet unchanged; a `holdStartTick` of applyTick + 200 is clamped to the apply tick (→ `closed` after close); one of applyTick − 160 is clamped to applyTick − 150, not replaced. **c** GIVEN #17 closing at tick 6 of a 12-tick hold, THEN the hold is not cancelled and completes (closing grace, mirrors Mail & Destinations M-R4-2). **b** Two players posting to #17 in one tick both succeed, in arrival order.
- **M-MD6-3 [Integration]** GIVEN a test client at swim depth forced to send a request, THEN the reply is `dry_land`, 0 units are consumed and the wallet is unchanged.
- **M-MD6-4 [Logic]** GIVEN close at tick T, true hold starts T−10…T+10 and estimate errors −3…+3 ticks, THEN the verdict equals the true-tick verdict for every true start outside T−3…T+2 (bell fuzz, Edge Cases).
- **M-MD7-1 [Logic, 10 rows]** GIVEN each reply, THEN the hand beat is: `ok` (got = requested) → slot-in, plus the riffle if k ≥ 2; `ok` MISDELIVER_ONE → careful handover; `wallet_floor` → pieces-out + "Can't afford a wrong house"; `no_target` → pieces-out + "Nothing for this address"; `closed` / `not_on_route` → pieces-out + the `mail.reject.*` reason; `out_of_range` / `position_stale` → "Couldn't reach the slot — step closer"; `dry_land` → pieces-out + "Dry land required to deliver"; `dead` → no hand beat; `frozen` → no hand beat; 5 s timeout → pending until the resync snapshot, then "Connection hiccup — bag re-checked". Each reason strip lasts HUD F1's lifetime for its text (≥ 2 500 ms at 1×, HUD H-PROMPT-1) and the live branch stays rendered beneath it.
- **M-MD7-2 [Logic]** GIVEN a hold producing 3 `DeliveryOutcome` events, THEN exactly 1 hand beat plays; GIVEN no reply, THEN the hand is not idle at tick 29 after completion and is idle at tick 30 while the prompt keeps "Delivering…"; a reply at tick 40 plays the verdict beat with no slot-in; a reply after a resync plays 0 hand beats; a reply while the carrier is beyond 2.9 m attaches no beat to the current prompt.
- **M-MD8-1 [Logic]** GIVEN hands selected and 5 matching letters in the backpack, WHEN a hold completes, THEN all 5 are delivered. **b [MANUAL]** GIVEN letters selected, THEN the label faces the camera within 0.2 s, the carry pose is unchanged, the face is neutral, and the label shows the entry's real address; hands selected gives no stance.
- **M-MD9-1 [Logic]** GIVEN Quick Hands (`insertPm` 500), THEN MATCH and MISDELIVER_ONE holds are both 6 ticks at the default setting and both 6 at 0.2 s; a StatSheet `insertPm` sum of 1000 is clamped to 750; drafting the perk or changing the setting after the press does not change the latched `holdTicks`.
- **M-MD10-1 [Logic]** GIVEN matching mail only in an open chest, THEN the preview is NOTHING; a Mounted actor never focuses a mailbox. **b [Logic, reflection]** The Manual Delivery assembly references no wallet-mutating API and builds no address string.

### Formulas

- **M-F1-1 [Logic]** GIVEN shift 3 and entries A (6 letters, value 8, deadline 3), B (one entry, runs: 3 letters value 8 deadline 2; 2 letters value 9 deadline 1), C (5 letters, value 9, deadline 3), THEN `k` = 3, `n` = 16, `predictedPay` = 105, `lateUnits` = 3, `deadUnits` = 2. **b** Stamp Collector 1250 turns an on-time 8 into 10; Cannon Arm 1100 changes nothing.
- **M-F1-2 [Logic]** GIVEN a selected letter whose run 0 has value 8, THEN the penalty is 4 ¢; run order (value 8, value 12) still gives 4; value 1 gives 1; at wallet −496 `floorRefusal` is false (lands on −500); at −497 it is true.
- **M-F2-1 [Logic, 11 rows]** GIVEN `holdSetting` → (no perk / Quick Hands): 0.20 → 6 / 6; 0.25 → 8 / 6; 0.40 → 12 / 6; 0.60 → 18 / 9; 1.00 → 30 / 15, THEN `holdTicks` equals those values and equals Input & Controls' `holdEff` for the same inputs (one function, int64, single ceiling); a 1 500 ms base at 1.0 s gives 60 (ceiling).
- **M-F4-1 [Logic]** GIVEN `k` = 1, 2, 5, 9, 26, THEN the riffle lasts 0, 120, 300, 300, 300 ms, shows min(k, 5) stacks and draws no in-world count label. **b [UI]** Reduced motion shows one 0.1 s frame and no count label.
- *F3 is a reference, not a rule, and has no criterion; F5's constants are covered by M-EC-1, M-UI-2 and M-MD4-1.*

### Edge cases

- **M-EC-1 [Logic]** GIVEN 26 matching entries, WHEN one hold completes, THEN one request, `requested` = `got` = 26, consumption cursor → hotbar → inventory → backpack, the riffle shows 5 stacks within 300 ms, the hand beat plays once, and the 26 `DeliveryOutcome` events share one tick (Mail R16).
- **M-EC-2 [Integration]** GIVEN a knockdown on the completion tick, WHEN the request orders first, THEN it succeeds and the rest spills; WHEN the knockdown orders first, THEN the reply is `dead` and no hand beat plays (both orders run).
- **M-EC-3 [Logic]** GIVEN wallet −496 and a MISDELIVER_ONE hold, WHEN a teammate's spend drops it to −497 mid-hold, THEN the hold continues, the prompt shows `deliver.floor_refusal` within 1 tick, and the host replies `wallet_floor` with 0 consumed. **b** Two players misdeliver 4 ¢ at −496 in one tick: the first succeeds (−500), the second gets `wallet_floor`.
- **M-EC-4 [Logic]** GIVEN a teammate's throw arriving at the carrier mid-hold, THEN the panel is unchanged and the hold completes; GIVEN a 5-tick catch-up after a hitch, THEN displayed progress never exceeds `heldTicks / holdTicks`. **b** A Cursed MISDELIVER_ONE stopped at the floor with 0 consumed emits no `CursedMisdelivered`; a Cursed letter for this address is Accepted at 12 ¢ with no goons.

### Network

- **M-NET-1 [Integration]** GIVEN a hold, THEN the only message from press to completion is the request — no hold-start, progress or posting-flag packet reaches any peer; at maximum field values the serialised request is 12 B (MATCH) and 16 B (MISDELIVER_ONE) per the Network layout, transport framing excluded; the JoinState and RejoinState diff with and without this system is 0 B.
- **M-NET-2 [Integration]** GIVEN a client that disconnects after sending, WHEN it rejoins, THEN its pending queue is cleared, `deliver.pending` is false and missed outcomes are not replayed; GIVEN a duplicate `reqId`, THEN the host returns the original reply and consumes nothing more; GIVEN the host's own hold, THEN its reply reaches the client layer on the same or next tick and its `holdStartTick` is exact.

### Cross-system integration

- **M-INT-1 [Integration, Grid Inventory]** GIVEN matching entries at the cursor, hotbar slot 2, inventory (r0, c1) and the backpack, THEN the preview's entry order equals the host's `DeliveryOutcome` order: cursor → hotbar → inventory row-major → backpack.
- **M-INT-2 [Integration, Mail & Destinations]** GIVEN 1 000 seeded (destination state × unit) pairs including CLOSED, LOCKED and `wont_fit`, THEN the client `Evaluate` copy equals the host's every time; with no lag `predictedPay` equals the summed outcome pay; with a StatSheet one event stale, the outcome figure replaces the prediction.

### UI, localisation and accessibility

- **M-UI-1 [Logic + UI]** GIVEN a test locale that places `{address}` first, THEN line 1 renders it without truncation; a lint finds no code path concatenating address fragments; no key contains a literal "¢"; each `a11y.deliver.*` variant is its own key.
- **M-UI-2 [UI]** GIVEN each row of the UI Requirements branch table, THEN a retained screenshot exists at 1080p and at 150 % text on 1280 × 720; EN line 1 ≤ 20 characters plus the longest address token, line 2 ≤ 36, line 3 ≤ 28, ≤ 4 lines in every row at 150 %, no address clipped; `deadOnly` reads "clears 2 dead letters", never "+0¢"; Cursed shows the skull chip and the hazard ring, a plain ✗ shows no hazard ring; NOTHING is one dim line with no lock; `floorRefusal` shows "Can't afford a wrong house (−4¢)" with the verb enabled; "Delivering…" shows a static spinner after 250 ms; a reason strip renders below a still-visible live branch.
- **M-UI-3 [UI]** GIVEN focus on #17, THEN the live region announces a ✓ only after 0.5 s of stable focus and a ✗ on its first displayed tick at assertive priority; every `a11y.deliver.*` string renders the current binding through `{binding}`; a pay or count change at #17 is never announced; a branch change replaces the pending announcement, never queues it; outcome and failure lines use their own category and are not swallowed by the rate limit.

### Fantasy and feel

- **M-FX-1 [Integration, race suite]** (real Grid Inventory handler and host FIFO, stubbed transport) GIVEN holds latched MATCH, WHEN each scenario — (a) an own optimistic op snaps back, (b) the phase flips on the completion tick, (c) the selection changes, (d) a reply lands in the same tick, (e) a catch or pickup arrives mid-hold — is injected at each tick offset −2…+2 around completion and each simulated RTT in {0, 60, 120, 250 ms}, plus a soak of seeds 1…10 000 (PCG32, the failing seed printed), THEN no misdeliver `DeliveryOutcome` exists for any hold latched MATCH, whatever the resolution (delivered, `no_target`, `closed`, cancelled, `out_of_range`, `dead`, `frozen`, timeout). **b [Logic]** Every hold start logs `latchedBranch` to telemetry, so playtest logs can audit fantasy test 1.
- **M-FX-2 [MANUAL]** GIVEN 20 recorded prompts — ≥ 5 per answer class: posts all (incl. late, dead-only, raised piece for another house), posts 1 to the wrong house (incl. Cursed), probably refused (floor), nothing / blocked (closed, panel, swim) — shown for 2 s each to ≥ 8 first-time players, THEN correct predictions are ≥ 90 % with a participant-clustered 95 % lower bound ≥ 85 % (fantasy test 2).
- **M-FX-4 [Logic]** GIVEN a synthetic outcome stream of 60 hand units with pay > 0, 40 thrown units with pay > 0, 20 hand dead-letter units and 10 feeder units, THEN `handShare` = 0.60, the machine line reports 10 units, and a bucket under 200 units reports "insufficient sample" (fantasy test 3; the gate itself is telemetry, ADVISORY at M-1a).
- **M-FX-3 [MANUAL, ADVISORY]** FA-MD-1…4 are checked by frame capture at 60 fps, with 120 ms simulated RTT for FA-MD-2 / FA-MD-3; FA-MD-5 is verified from the press-drop log.

## Open Questions

*Hand-backs to other GDDs are listed here; those GDDs were **not** edited by this session.*

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | ~~Hand-back to Player Character~~ **Closed 2026-10-16** — adopted: Quick Hands `insertPm` 500 on every deliver hold (F4, PCM-F4-2), `GetHoldTicks` / `ShouldCancel` hooks (P10, P12), Deliver priority 2 / Pick-up 1. | — | closed |
| 2 | ~~Hand-back to Input & Controls~~ **Closed 2026-10-16** — adopted (a)–(f): shared `holdEff` unchanged, clamp at Content & Data (`DeliverInsertPm` 0…750); leading ✓ / ✗ mark slot; G2 `ShouldCancel` (latched payload); Back consumed by a Toggle cancel (ladder 3b); G1 lets a verb judge its branch at the press tick; "Read prompt" action (M4). | — | closed |
| 3 | ~~Hand-back to Mail & Destinations~~ **Closed 2026-10-16** — adopted: OQ5 answered (Quick Hands 500); "Wrong house — posts 1"; a hold's k outcomes in one tick with contiguous `outcomeSeq` and one door beat + one "+N¢ · k stacks" pill per `(actor, destination, tick)` on every peer (R6, R16) — so the in-world "+N" pill is cut; `holdStartTick` host-domain, clamped by MD6 (R4). | — | closed |
| 4 | ~~Hand-back to Grid Inventory~~ **Closed 2026-10-16** — adopted: "posts 1", pieces first, Panel lock with no glyph; `holdStartTick` u32 in R7; rule 7 pending until the resync snapshot; rule 8 `reqId` u16 wrapping, duplicate → original reply; temp ids by `dependsOn` / `stale_entry`. (Simulation Core's Edge Cases say the Sim does not deduplicate `reqId`; MD5 relies on Grid Inventory rule 8 — for `/consistency-check`.) | — | closed |
| 5 | ~~Hand-back to HUD & Notifications~~ **Closed 2026-10-16** — HN-4a ("posts 1"), panel-footer placement (MD2), the branch table with a mark-led line 1 and the transient reason strip are adopted in H11; placement is H11's (screen-fixed, no leader line). The strip's lifetime is HUD's F1 (≥ 2.5 s × duration setting), not this GDD's retired `reasonStripTicks` 0.8 s (below the reading floor; consistency pass); Throwing aligned. | — | closed |
| 6 | **Perks GDD.** Quick Hands' card should state the effective hold ("deliver holds 0.2 s at the default setting"). At the 0.2 s setting the perk gives nothing (Input & Controls' 6-tick floor, accepted by CD ruling ii); if playtests show it is a dead pick, re-tune or give it a non-time effect (range). | Perks & Perk Draft GDD | before the v1 perk pool is fixed |
| 7 | **Art / tech-art.** (a) Ratify the 0.2 s pre-reach on focus in art bible §5 (anticipation for a repeated verb; no longer a perk condition — no deliver hold is under 6 ticks). (b) Visual-only lunge ≤ 0.8 m versus a shorter mailbox interaction range — technical-artist and creative-director call. (c) The mailbox flap as a separate node inside the 150-tri budget. (d) Ratify the PROPOSED assets in Visual/Audio. | art-director / technical-artist / creative-director | before `/asset-spec` |
| 8 | **Wording.** "Wrong house — posts 1", "Close panel to deliver" ("panel" is UI-speak; "Close the bag" fails for chests and trucks), and the cancel/failure lines "Mail changed — check the door", "You switched pieces", "Connection hiccup — bag re-checked", "Wrong house — check, then hold again" — deadpan postal register. | narrative-director | before string freeze |
| 9 | **Press during `deliver.pending`.** At high RTT a press at the next door can be dropped (pulse only). Options: a 0.3 s press buffer, or allowing a hold at a different destination while a deliver is pending. Signal: FA-MD-5 dropped-press share > 5 % of Deliver presses at M0. | game-designer / Grid Inventory owner | M0 playtests |
| 10 | **Remote stance label.** Should remote peers see the held piece's district swatch (one glyph + colour, a few bytes in the public `PlayerState`)? Art-director leans yes (callouts: "you're holding a tangerine one"). | art-director / Networking & Session | M0 |
| 11 | **Remote posting flag.** Not sent (Network); Player Character reserves one spare flag bit in its transform packet (adapted from a byte, adopted 2026-10-16). Revisit if playtests show friends cannot tell who is posting. | Networking & Session | M0 playtests |
| 12 | **Hand share (fantasy test 3).** `handShare = hand ÷ (hand + throw)` over player-driven pay > 0 units ≥ 20 % is PROPOSED; instrument `DeliveryOutcome.via` per shift × build level at M0 (M-FX-4); below 20 % triggers a revisit with non-economic levers only. | game-designer / analytics | M0 |
| 13 | **Audio.** Ratify the nine `sfx_hand_*` cues and the hand-side rate limits (Visual/Audio). | audio-director | before `/asset-spec` |
| 14 | **M-1a provisional.** Everything here is provisional until the two-friend greybox result is recorded in `prototypes/m1a-delivery-throwing-concept/REPORT.md` (systems-index GUARD). The greybox needs only MD1–MD5, MD7 and F1–F2. | producer | M-1a gate |
| 15 | **Deferred network risks (friends-only scope, design-review).** (a) The host should discard FIFO requests from a dead connection on rejoin; (b) ~~all k outcomes in one tick~~ answered by Mail R6 / R16; (c) a friend spamming MISDELIVER_ONE can drain the shared wallet to the floor and trigger Cursed goons — consider a per-player misdeliver cap or logging; (d) verify reply-then-snapshot ordering on one channel against the 4.7.1 multiplayer API (ADR checkpoint). | Networking & Session | M0 |
| 16 | **Prompt noise (design-review).** "Raised piece stays" may appear on most ✓ prompts — consider progressive disclosure (first N holds) while keeping the live-region line; measure the share of ✗ holds followed by an immediate complaint as a readability signal. | ux-designer | M0 playtests |

**→ becomes an ADR** (implementation, not design): where the deliver preview lives on the client (Sim-side client layer vs Presentation) and how it shares `Evaluate` / F10 code with the host; the `DeliverPreviewTracker` and the evaluation seams; the visual reach echo's coupling between input events and the 30 Hz latch (shared with Input Router ADR); the ordered per-tick input-event list (Input & Controls' router ADR).
