# Belt Logistics (belts, splitter/merger, Address Sorter, inserters, chests, depots)

> **Provisional**: designed before the M-1a two-friend greybox (systems-index GUARD overridden by owner instruction 2026-10-16); re-validate after the greybox.

> **Status**: Designed (Provisional) — pending independent /design-review in a fresh session
> **Author**: owner + agents (autonomous /design-system run)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Implements Pillar**: 1 (The mail must flow), 3 (Hands beat machines, until they don't — ratified narrow reading), 2, 5
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 (C1–C7 applied: n^e lever, signal grammar, solo geography remedy, Vehicles load-bearing, OQ16 close hand-back, push-test wording, throw Feel row; C1 ceiling and C3 Pillar 3 reading pending owner ratification)

## Summary

Belt Logistics is what the automation stack does once Building places it. Two-lane belts carry single mail units from the Post Office Intake. Address Sorters route them by labelled district, street, number range or kind. Inserters, splitters and mergers move them between belts, containers and mailboxes, where Mail & Destinations judges each piece at 2 per second. The machine is literal: it works while nobody stands next to it and never softens a mislabel. Presence still pays: riding, a bounded Push, hand-routing at the sorter console, and clearing jams and leaks. It is host-authoritative with a cosmetic client visual sim, and it is budgeted for 5 000 units within Simulation Core's 1.20 ms Automation slice.

> **Quick reference** — Layer: `Feature` · Priority: `MVP (one belt M-1b → M2)` · Key deps: `Simulation Core, Content & Data Definitions, Building System, Grid Inventory, Mail & Destinations, Player Character, World Generation (provisional), Networking & Session (M0); consumers Throwing, Vehicles, Enemy AI, HUD, Map & Address UI, Presentation`

## Overview

Belt Logistics is the behaviour owner of every construct that moves mail without a carrier: conveyor belts (Mk1, Mk2), splitters and mergers, the Address Sorter, inserters (short and long), and the belt-facing sides of chests, depots, the PO Depot and the PO Intake's outfeed face. Building decides **whether, where and at what cost** these constructs exist (building-system.md B1–B8). This system decides **what they do once placed**: how mail units ride two-lane belts, where a sorter sends each piece, when an inserter swings, where a belt ending in the air drops its mail, and what a player standing on, pushing or operating a machine changes.

**Data layer.** Each belt is a set of lanes holding single mail units at integer-mm positions, compiled into segments, and advanced once per tick in Simulation Core's phase 5 `Automation.Step` (simulation-core.md R3; ADR-0003). Every unit that enters or leaves a lane crosses into or out of Grid Inventory's `BELT` location state through `IInventoryApi`: `TakeUnit`, `PeekOutfeed`, `TryInsert`, `CreateWorldItem` (grid-inventory.md R14, R19; ADR-0009). A unit reaching a mailbox goes through Mail & Destinations' feeder contract (`OfferUnit`, 2 pieces/s, one feeder per mailbox; mail-destinations.md R12, F11). The host decides every insert, transfer and endpoint. Clients run a cosmetic visual sim from lane events and periodic checksums (spec 06 §4, kept by spec 13 §3). See **Network**.

**Player layer.** Players lay a belt from the Intake toward a street, label a sorter's outputs ("Larch Lane", "Elm 1–9", "Parcels"), aim an inserter at a mailbox, and leave. The machine's value is that it **works while nobody stands next to it** (Pillar 3, ratified reading). Presence still pays: a carrier rides a belt, pushes a stretch of it 50 % faster, takes the sorter console and routes a jam by hand, clears a leak pile, or sprints the parcel the belt cannot reach. Without this system the late game has no automation stack. Spec 11's "you must start building" moment in shift 2 and the M2 gate both depend on it.

**Milestones.**
- **M-1b**: one belt, one sorter and inserters to two mailboxes. Solo, scene-spawned, loopback only.
- **M0**: lane events and the client visual sim.
- **M2**: the complete set.

Elements are tagged **[M-1b] / [M0] / [M1] / [M2] / [M4]**.

*Out of v1 (spec 13 §2.2):* pipes, elevated belts and ramps, two-lane Cargo, and the apartment `unit` filter. Implementation (lane storage, traversal, codecs) is in **ADR candidates** (Open Questions), mainly ADR-0027.

## Player Fantasy

**"Exactly What You Told It"** (creative-director, recommended framing; Direct). The belt line is a clerk who takes everything literally. It never tires, never lies, and is exactly as smart as the labels you gave it.

You tap the sorter, stamp "Larch Lane" on Left, and sprint for the hill with a Large parcel. Two streets away you hear the Larch flags pop in a steady two-a-second rhythm, and you are not there. Then a house on Elm starts wobbling in time with them: you put "Elm 1–9" on Front, and this house is number 12. The machine does not stop and does not apologise. You run back, take the console, fling the stack where it belongs at four times the machine's pace, fix the label and leave again.

**The machine matters because you are elsewhere. You matter because the machine is literal.** This is Pillar 3's ratified reading: the sorter's value is that it works unattended, hand Sort stays instant, and automation never removes the reason to move. It is also Pillar 2's honest rule under a goofy surface.

**Solo.** The line is your second pair of hands. You label the two near streets and carry the far one yourself. The thump of the tile seams is your only coworker, and a misroute is a private, quick fix.

**Eight players.** The line is a shared instrument. One player labels, one rides it to the far district, one pushes the last stretch, one holds the console. Nobody owns a label, so a misroute is "whose is that?" shouted over voice. The feed blames the machine ("Belt 3 posted…"), never the last editor (HUD H-ATTR; hand-back OQ-BL-H6 for the Payday roll-call).

**Rejected framings.**
- "Label It, Leave It, Lap It" repeats Building's "It Runs While You Run" and has no comedy engine.
- "The Rube-Goldberg Mailroom" risks a factory screensaver: watching instead of Fellowship.

### Feel & Comedy Feedback

| Moment | What the player sees / hears | Rule it honours |
|---|---|---|
| First parcel off the Intake | The hatch flaps and a letter hops onto the left lane, the next onto the right, *thunk-thunk* over each tile seam | Belt-pull 1.0/s, alternating lanes (BL13, BL6) |
| Labelling a sorter | A tag snaps onto the port with a rubber-stamp *thunk*: the street's swatch and glyph. An empty slot is a blank tag, "matches nothing" | No catch-alls; unlocked addresses only; live on the next credit (BL10) |
| Jam / back-up | Mail bunches nose-to-tail, the strip goes static, the motor strains, and a badge names the cause and the door | The head waits at the gate; STALLED never falls through to Overflow (BL5, BL10) |
| Misrouted belt (Sorcerer's Apprentice) | The house wobbles at feeder rhythm and the resident's pile grows; "Belt 3 posted…" | Never auto-jammed (BL8; Mail R12) |
| Push | The carrier leans on the rail and a 7-tile window visibly speeds up. On a jammed belt the carrier strains and the prompt names the jam | Latency, never throughput; windows union (BL14, F5) |
| Riding | Arms folded, poker-faced, carried like luggage | Rider carry, 12 m/s cap, no self-boost (BL14, P7) |
| Console Route | You drag a stack and it fires out at ≈ 8/s while the machine keeps working around you; a second opener sees your hat on the busy plate | One operator; nothing softened (BL11) |
| Leak spray | A cut end fans letters across the lawn, then chokes with a cough after six. You scoop the pile and toss it back onto a lane | Bounded leak, wasteful never lossy (BL15, BL16) |
| Throw onto a belt | A tossed stack snaps into the lane one letter at a time, *thup-thup-thup*, and the overflow flops beside the rail | Partial accept, oldest first; the rest becomes one WorldItem (BL16) |

### Fantasy tests (PROPOSED thresholds, M2 telemetry)

1. **Works unattended.** Lobbies n ≥ 2, shifts 4–5: ≥ 60 % of feeder Accepts happen with no carrier within 20 m of that feeder.
2. **Still a reason to move** (every n, solo included). The median carrier's distance per Delivery minute in shift 5 is ≥ 70 % of their shift-2 figure. No session has ≥ half the team within 15 m of the PO for ≥ 60 % of shift-5 Delivery.
3. **Presence pays, visibly** (n ≥ 2). When a sorter STALLS, or a feeder misdelivers ≥ 3 units, a carrier is within 3 m of the cause within 30 s in ≥ 60 % of cases, and a console Route clears a stall in ≤ 10 s. In side-by-side clips, ≥ 4 of 5 testers correctly identify which belt is being pushed.

## Detailed Design

### Core Rules

**BL1 — Scope and ownership [M-1b].** This system owns behaviour state keyed by Building's construct id (building-system.md B1):
- lane contents;
- segment table;
- sorter filters, round-robin pointers and Halt flag;
- inserter credit and arm;
- push windows;
- leak counters.

Defs it drives (`BuildingBehaviour`, CDD OQ-BLD-4): `belt` (belt_mk1, belt_mk2), `splitter`, `merger`, `sorter` (address_sorter_mk1, container `sorter_buffer`), `inserter` (inserter, inserter_long). It also drives the **endpoint faces** of `container` defs (chest, depot) and of the pre-built PO (Intake outfeed face, PO Depot).

Other systems own the rest:
- **Building**: placement, cost, ids, occupancy, deconstruct.
- **Grid Inventory**: containers, buffers, WorldItems and conservation.
- **Mail & Destinations**: acceptance, payment and the feeder throttle.
- **Player Character**: riders' movement.

**BL2 — Mail only [M-1b] [D].** In v1, belts, splitters, mergers, sorters and inserters carry **mail units only**, one unit per lane slot (spec 04 §2.1; Cargo deferred).
- Non-mail is never picked or accepted: an inserter's container pick uses a mail-only selector (Grid R14 `Fifo` = lowest mailId).
- A lane is **not a receiver** for a thrown non-mail entry (Throwing T6 continues the flight).
- Materials move by hand, chest and depot.

**BL3 — Lanes [M-1b].** Every belt tile is 2000 mm long and has two lanes, **left and right relative to travel**. The labels survive turns.
- A unit's position is the integer-mm distance of its centre from the start of its segment's lane.
- Valid positions run from `beltEntryMm` (250) to `L − 250`, where `L` = 2000 × tiles.
- Neighbours in one lane are at least `beltSpacingMm` (500) apart, so a lane holds 4 units per tile and a tile holds 8.
- Units in a lane never reorder.
- A curve has the same 2000 mm length in both lanes, so turning changes neither speed nor capacity. The arc is Presentation's.
- **Tile shape** comes from its neighbours (building-system.md B3: Building stores only `rot`):
  - one belt feeding from a side and none from behind → a **curve**;
  - a feeder from behind plus one from a side → a **side-load** (BL6).
- A unit on a lane or an inserter arm is in Grid's **BELT** state. It enters and leaves BELT only through `IInventoryApi` calls or Mail's `OfferUnit`, so the UnitLedger sees every crossing (ADR-0009 §6).

**BL4 — Segments [M-1b].** A **segment** is a maximal run of same-tier belt tiles linked front-to-back with no node between them.
- Segment boundaries:
  - a splitter, merger or sorter;
  - an endpoint (BL7);
  - a side-load point;
  - a tier change (Mk1 ↔ Mk2), which acts as a node.
- An inserter picking from or placing onto a tile does **not** split a segment.
- The host assigns segment ids. Placing or removing a belt-affecting construct recompiles only the touched segments, and never creates, deletes or reorders a unit.
- A removed tile's units go to the removal spill (BL18). The run is split: units upstream now face the new end, and units downstream keep their order and positions.
- 40 tiles is the longest segment a single drag makes (`maxLineTiles`). A segment may be longer when lines join.

**BL5 — Advance and hand-off [M-1b].** Each tick every lane advances by **F1**:
- A unit's new position is `min(p + A, leader′ − 500)`. The head stops at the **gate** `L − 250` unless its exit accepts it this tick.
- A unit hands off **at most once per tick**. It enters the next segment at `beltEntryMm`, or at the side-load point; the unused advance is discarded, never banked.
- A blocked head stays at the gate, and the lane compresses behind it to 500 mm spacing. This is the visible **back-up**.

**BL6 — Lane choice by source [M-1b; side-load M2].**

| Source | Lane |
|---|---|
| Straight belt → belt, splitter, merger | same lane (L→L, R→R) |
| Side-load (belt feeding a belt's side) | target's **near** lane, at the target tile's midpoint. The feeding belt's own two lanes alternate (1 bit). Enters only if no target unit is within 500 mm, so through-traffic wins and the side belt backs up. |
| Sorter output, Intake belt-pull | alternates L/R per output (1 bit, starts L); into a perpendicular belt's side, the side-load rule applies |
| Inserter placing onto a belt | the lane **farther** from the inserter; for an inline inserter (front = belt direction), the lane with a free entry slot, ties L |
| Thrown unit (BL16) | the lane under the landing point |

**BL7 — Endpoints [M-1b core; M2 complete].** A segment's last tile looks at the tile in front of it:

| In front | Behaviour | Milestone |
|---|---|---|
| belt, same axis | same segment (BL4) | M-1b |
| belt side (perpendicular) | side-load (BL6) | M2 |
| splitter / merger input, sorter input cell | node input (BL9, BL10) | M-1b sorter; M2 others |
| a mailbox, from that lot's `approachTile`, facing its `mailboxTile` | **feeder** (BL8) | M-1b |
| depot, PO Depot (any side) | `TryInsert(container, unit, construct)` (Grid R8 merges) | M2 (PO Depot M1) |
| chest, PO Intake, the sorter's sides other than input, any solid construct, house, wall, PO body | **dead end**: the head waits and the belt backs up (`inserter_only` / `outfeed_only`, Grid R1) | M-1b |
| a parked vehicle's loading face (Vehicles' zone, speed < 0.1 m/s) | `TryInsert` into its cargo (mail_truck_cargo) | M2 (provisional, Vehicles) |
| nothing (open ground, water, a street-crossing end) | **leak** (BL15) | M-1b |
| outside the map | dead end | M-1b |

**Only belt ends leak.** A splitter, merger or sorter output, or an inserter, facing nothing is a dead end. `node_busy` means a node's input cannot take a unit this tick (round-robin turn, its own output blocked).

**Retry [D].** A head refused by a Grid container retries only when that container's `version` differs from its version at the refusal (wake-on-version). It never polls an unchanged container. A feeder head offers every tick its head is at the gate; Mail's credit decides (F11).

**BL8 — Feeders [M-1b].** A belt tile or inserter **on a lot's `approachTile` whose output faces its `mailboxTile`** is that destination's feeder (mail-destinations.md R3, R12).
- **Placement rule.** This system registers Building's `IPlacementRule`, which asks `CanRegisterFeeder(destinationId)` and vetoes with `feeder_slot_taken` (resolves OQ-BLD-7(a)).
- **Lifecycle.** The feeder registers on `OnPlaced` and deregisters on `OnRemoving`. Re-facing a feeder is a deregister followed by a register.
- **Offers.** The head unit is offered with `OfferUnit(destinationId, unit, feederId)`. Mail throttles at `insertRate` 2.0/s and returns Accept / Misdeliver (unit consumed) or Reject / `closed` / `wallet_floor` (unit stays, belt holds).
- **Order.** Belt feeders offer in BL20 step 2, ascending construct id (simulation-core.md R3). An inserter feeder offers when it places, in step 5.
- **Approach tile only.** A construct whose output reaches a `mailboxTile` from any other tile (a long inserter two tiles out, a belt end beside the lot) is refused at placement with `feeder_not_on_approach`, through the same `IPlacementRule`.
- **No softening.** A misrouted feeder is never auto-jammed (Mail R12, CD ruling).

**BL9 — Splitter and merger [M2].**
- **Splitter**: 1 input (back), up to 3 outputs (front, left, right). Each output may carry one **kind filter**.
  - **Filtered outputs.** If any output's filter names the unit's kind, only those outputs are eligible. They take turns round-robin, and the unit waits if all are blocked.
  - **Otherwise** the unfiltered outputs are eligible. The first non-blocked output from the pointer `rr` (order Front, Left, Right; starts Front) takes the unit, and `rr` advances past it.
  - At most one unit per input lane per tick; lanes are preserved.
- **Merger**: up to 3 inputs (back, left, right), 1 output (front). It takes inputs round-robin (order Back, Left, Right; starts Back), skipping empty ones, and preserves lanes. No filters.
- Neither changes a unit's address or order within a lane.

**BL10 — Address Sorter [M-1b core; M2 complete].** 2×2, solid (B2). Cells in the sorter's own frame (rot 0, front = north): NW, NE, SW, SE.
- **Ports.**
  - **Input** enters SW from the back.
  - **Overflow** exits SE to the back (spec 04 §2.4: same side as the input, offset by one tile).
  - **Front** output exits NW north, **Left** exits NW west, **Right** exits NE east.
  - Ports rotate with `rot`.
  - An output port with no belt in front is a dead end.
- **Buffer.** The input head calls `TryInsert(sorter_buffer, unit)`. The buffer is a 4×2 Grid container, so AutoPlace merges same-StackKey units (Grid R1, R8).
  - Up to 8 address stacks of letters fit, 2 Mediums, or 1 Large (2×4, which fits only rotated into an empty buffer).
  - **Big parcels drain the buffer before they enter.** This is intended and documented: the UI shows "Waiting for space".
  - A full buffer refuses and the input belt backs up.
- **Examine.** At the F3 rate (`sorterBaseMilli` 2000 × `SorterThroughputMultPm`), the sorter takes `PeekOutfeed(sorter_buffer, Fifo)`, the lowest mailId in the buffer, and routes it to the **first matching output in the order Front, Left, Right**.
  - No match → **Overflow**.
  - The routed unit is `TakeUnit`-ed onto that output's lane (alternating L/R).
- **Blocked output [D].** The unit stays in the buffer and the sorter **stalls** on it (head-of-line). Routing is re-evaluated at every attempt, so a filter edit or a cleared belt takes effect on the next credit. Blocked mail never falls through to Overflow: Overflow is for **unmatched** mail only.
- **Filters.** Each filtered output holds up to `SorterFilterSlots` **slots** (1 base; Extra Labels +2; Postmaster's Eye 64 — CDD CD12).
  - A slot is an **AND-tuple** of optional fields: `district`, `street`, `numberRange` (inclusive house numbers; requires `street`), `kind`. A missing field is a wildcard.
  - An output matches if **any** of its slots matches.
  - An all-wildcard slot matches **nothing**, so there are no accidental catch-alls.
  - The `flags` (Cursed) filter is not in v1 (OQ-BL-5).
  - Filters name only unlocked addresses, through Map & Address UI.
- **Edits [D].** Filter edits are `ConfigureSorter{constructId, output, slots[]}` requests applied in phase 0. The host validates them against unlocked districts and streets.
  - The **only** client surface that sends one is the sorter's own Configure panel (BL11, open within 2.5 m). There is **no remote relabelling**: running back to the sorter is the point (creative-director).
  - Map & Address UI supplies the address picker inside that panel.
  - The host's missing distance check is the friends-only trust model (spec 13 §3), not a feature.
- **Halt [M2].** A console toggle that stops examining. The input still fills the buffer.

**BL11 — Sorter console [M2].** **Configure** (Tap, priority 1, `rangeMm` 2500) opens the sorter panel: filters, a buffer view, Halt, and a per-output and overflow counter "Unmatched → overflow: N" (HUD H-WIDGET M2).
- **Route** is owned by **one operator** at a time: the first `open` holds it, and later openers are read-only (`console_busy`). Grid R10's open set and 3.0 m leash apply.
- **Route** drags one buffer entry (a whole stack) onto any output, including Overflow or a non-matching output. Nothing is softened: the downstream decides.
- A Route is a **persisted order** `(entryId, output)`, accepted in phase 0. Its units leave lowest mailId first, so a stalled head unit leaves first.
- Routed units bypass the examine credit and leave as fast as the output's entry slots free (up to one unit per lane per tick). This is the **"hands beat machines"** speed: ≈ 8/s on Mk1 against the machine's 2/s.
- **Order and conflicts.** A Route runs before auto-examine on the same buffer. A blocked output returns `output_blocked` and the order waits.
- The order ends when the entry is empty, or on close, leash, death, disconnect, a new Route, or the sorter's removal.
- Automatic routing continues around the operator.
- The buffer view is a Grid container, replicated only to viewers (Network).

**BL12 — Inserter [M-1b; filter, long M2].**
- **Reach.** An inserter picks from the tile **behind** it and places on the tile **in front** (B3). The long inserter's reach is 2 tiles each way and the timing is the same.
- **Pick.** When its F3 credit (`inserterBaseMilli` 1250 × `InserterRateMultPm`) is ready, it picks **one** unit:
  - from a belt tile: the unit nearest that tile's exit, either lane, ties L;
  - from a chest, depot or PO Depot: `TakeUnit(c, Fifo, construct)`;
  - from the Intake outfeed face: the BL13 selector.
- **Hold and place.** The unit is held on the **arm** (BELT state) and placed `inserterPlaceDelayTicks` (6) later.
  - Targets: a belt lane (BL6), a container (`TryInsert`), the sorter input, a feeder offer (BL8), or a parked vehicle loading face (M2).
  - A refused place keeps the unit on the arm and retries by BL7.
  - Credit is spent only on a successful pick.
- **Races.** A player op that wins a contested unit in phase 0 leaves the inserter `no_unit`. It retries next tick with its credit unspent (Grid R7, Sim Core R9).
- **Kind filter [M2].** From a belt the filter is Belt's own check. From a container it needs Grid `Selector.Kind(k)` (hand-back OQ-BL-H2). Until that exists, a filtered container pick inspects only the `Fifo` head and waits.

**Inserter legality.**

| Construct | Inserter picks from | Inserter places into | Belt end into it |
|---|---|---|---|
| Belt | yes | yes | — |
| Chest | yes | yes | no (dead end) |
| Depot, PO Depot | yes | yes | yes |
| PO Intake | outfeed face only | no | no (dead end); belt-pull **from** its outfeed face, BL13 |
| Sorter | no (buffer internal) | yes (input cell) | yes (input cell) |
| Mailbox | no | yes, as a feeder | yes, as a feeder |
| Parked vehicle loading face (M2) | no in v1 | yes | yes |

**BL13 — Intake outfeed and Presort [M-1b; Presort M4].** The PO Intake's `outfeedLaneTiles` (World Generation OQ-BLD-3) form its outfeed face. Each pull point has its own F3 credit:
- a **belt** whose back faces it pulls at `outfeedPullMilli` (1000, 1.0/s);
- an **inserter** whose back faces it pulls at the inserter rate (1.25/s base; Greased 2.5/s).

A raw belt is the cheap, slow tap.
- **Selector.** Pulls call `TakeUnit(po_intake, selector, construct)`. The selector is `Fifo` (ascending `arrivalSeq`, Grid R14) unless RuleFlag `PresortOutfeed` is on.
- **Presort.** Under Presort, each pull point keeps its **current street** until that street is empty or `presortRunMax` (12) units have gone. It then takes the street holding the lowest mailId, preferring a different street when one exists. A flag change resets the run.
- Belt owns this policy. Grid owns the per-street head index (R14, ADR-0009).

**BL14 — Push [M-1b].** A belt tile registers a **Push** anchor (Channel, priority 0; player-character-movement.md P10). While a player pushes:
- **Window.** Units whose centre is inside a **window of ±`pushReachTiles` (3) tiles along the anchor's segment** at tick start advance at `operatedBeltMultPm` (1500) × lane speed (F5). The window is clipped to the segment.
- **Overlap.** Overlapping windows take the **union**, never the product. One push per player.
- **Validity** is re-checked by the host every tick:
  - the pusher is Alive, not mounted or swimming;
  - its host-known chest point is within `rangeMm` (2 500, the Push anchor's) + 500 of the anchor (Player Character F5).

  The push ends on release, knockdown, disconnect, the tile's removal, or failing the check. In Input's hold-as-toggle mode it also auto-ends after `pushToggleMaxSeconds` (6).
- **Riders.** A rider inside a window is carried at the boosted speed. The pusher on their own segment gets base speed only (Player Character P7). The 12 m/s support cap applies.
- **Purpose.** Push buys **latency**, never throughput: lane capacity already exceeds every consumer (F2). That is why an AFK push is not a dominant job (closes PC OQ25).
- **Blocked segment.** On a BLOCKED segment the push changes nothing. The prompt names the head's reason and location instead ("Held: 9 Jade Close refused"), and the carrier plays the strain pose. This points the player at the real fix.

**BL15 — Leaks [M-1b].** A belt end facing nothing (BL7) emits at most one unit per `beltLeakIntervalTicks` (15) per end, staggered by `constructId mod 15`.
- **Emission.** Each emitted unit becomes one `CreateWorldItem(origin = BeltLeak)` at the front tile's centre, with no RNG.
- **Pause.** Leaking pauses, and the belt backs up, while that end has ≥ `beltLeakPerEndMax` (6) live BeltLeak WorldItems it created, or the island has ≥ `beltLeakGlobalMax` (64).
- **Recovery.** Picking up the pile frees the end. Leaked mail follows Grid R12 (300 s, then back of the Intake queue), so a leak is wasteful, never lossy.
- This bounds WorldItem creation to ≤ 1 per leaking end per tick and makes Grid's "~400 WorldItems in one tick" case unreachable (hand-back OQ-BL-H3).

**BL16 — Thrown units landing on a lane [M-1b].** Throwing calls `TryInsert(lane, point, unit)` per unit, oldest first, in phase 4 (throwing-catching.md T6).
- The lane is the one under the landing point.
- The first unit snaps to the nearest free position in that lane that is legally spaced at 500 mm, within ±`throwSnapMm` (1000). Each further unit steps upstream by 500 mm.
- Units that fit join the lane, in BELT state, and the `LaneInsert` carries `flightId`. The rest is refused, and Throwing makes the remainder one WorldItem.
- The upstream steps never cross a segment boundary.
- Under the cap (BL17) units are accepted one by one until the count reaches it. The reason is `belt_full` only if no unit was accepted.
- A lane is not a receiver if it is full at the point, or refuses the category (BL2).
- A lane's top face is the tile's base height H (B3) + `beltTopMm` (Presentation geometry, ADR-0015 broad phase).

**BL17 — Belt item cap [M-1b].** At most `beltItemCap` (5 000) units are on lanes and arms island-wide.
- At the cap these new entries onto belts are refused with `belt_full`, and the source keeps its unit: Intake belt-pulls, inserter picks from containers, sorter routes out of a buffer, and thrown landings.
- Transfers between lanes never change the count and are never refused by the cap.
- Real supply stays far below the cap (≈ 1 000 units in an 8-player shift 5, spec 11 §3). The cap bounds performance and JoinState, not play.

**BL18 — Removal and destruction [M-1b].** On Building's `OnRemoving` (deconstruct, B8 step 2) this system hands over, for **one** spill bag at the footprint centre:
- the removed tile's lane units;
- an inserter's arm unit;
- the sorter buffer's entries.

On `EntityDowned{Construct}` it hands the same set to Defenses' ruin or spill flow (M3). Then:
- any push on the tile ends;
- a feeder deregisters;
- the touched segments recompile (BL4).

A unit is never in two states. Within one tick, a removal in phase 0 runs before Automation.Step.

**BL19 — Stats and flags [M-1b reads; perks M4].**
- `BeltSpeedMultPm`, `SorterFilterSlots`, `SorterThroughputMultPm` and `InserterRateMultPm` are read once at the start of each Automation.Step (CDD CD20, continuous systems).
- RuleFlag `PresortOutfeed` is read per pull.
- Express Lane (×1.5) turns Mk1 into 3 m/s and Mk2 into 6 m/s. That 6 m/s is spec 04's and spec 11 §9's "Mk3"; **no Mk3 construct exists in v1** (hand-back OQ-BL-H9 for Player Character P7's "2 / 4 / 6 by tier").
- `InserterRateMultPm` is clamped to ≤ 4000. That gives 5 units/s, which the 6-tick place-then-pick cycle supports.
- `BeltMomentum` (Bunny Hop) is Player Character's.
- A stat change re-times only future ticks. F1 is stateless, so no unit teleports.

**BL20 — Phase-5 order [M-1b].** Inside `Automation.Step`, every tick:
1. Read stats and validate pushes.
2. Belt-feeder offers, ascending construct id.
3. Container endpoints and leaks, ascending construct id.
4. Sorters, splitters and mergers in **topological order, downstream first**. Ties go to ascending id, and loops are cut at the lowest id. A sorter **routes before it accepts its input**, so a buffer emptied this tick can take a Large in the same tick.
5. Inserters, ascending id: **place, then pick**. A place onto a mailbox is that inserter's feeder offer.
6. Sources (Intake belt-pulls), ascending id.
7. Lane advance (F1).

**Observable contract.**
- Steps 2–6 see head positions from the end of the previous tick, so every unit placed this tick first advances next tick.
- When two machines want one lane entry slot, the lower step wins, then the lower construct id. The loser retries next tick with its credit unspent.
- A route or pick checks the destination slot **before** taking the unit, in one call, so a unit is never between states.
- A unit hands off at most once per tick.
- A player op in phase 0 beats every automation call.

The traversal itself is ADR-0027's. Each refused head's saved container `version` (BL7) and each inserter's arm are Sim state, kept in the dump and hash.

**BL21 — Pillar 3 contract (grid-inventory.md OQ16) [M2 telemetry].** Hand Sort stays instant and unchanged. The sorter's value is that **sorted streams arrive with nobody present**: at doors through feeders, or at an NPC truck's loading face, so the truck reloads on return with no shuttle job.
- **Signal** (M2 telemetry, lobbies n ≥ 2 only; ≥ 6 sessions per lobby bucket, else NOT ASSESSED). `sorterShare` is the share of delivered units that passed a sorter. The signal fires when, in the median session, hand sorts of truck cargo or the Intake are ≥ 6 per shift, **and** either:
  - (a) the median session's shift-5 `sorterShare` is < 30 %, or
  - (b) ≥ 50 % of sessions place no sorter by the end of shift 3.
- **Response.** **Raise the cost of being present.** The lever: concurrent streets per batch, `streetsPerBatch(n) = round(S1 × n^e)`, `e` default 0.5, safe range 0.5–1.0 (Mail owns it; e = 1.0 is a ceiling, never the default — CD ruling pending owner ratification). Since 1^e = 1, solo is unchanged (Pillar 5). If the signal persists into the next M2 batch, `e` steps up by 0.25. Mail & Destinations owns the manifest lever and Run Structure the district pool (hand-back OQ-BL-H1). The response **never weakens Sort**.
- **Symmetric signal.** Fantasy test 2 checks "automation removed the reason to move". At n ≥ 2 the response is more demand that belts cannot serve (district spread, far lots), owned by Mail and Run Structure. If only solo fails, the response is lot geography (World Generation: one far lot per batch beyond the reference factory's reach), never quota or volume (CD reading of Pillar 3, pending owner ratification). No response weakens Push, Route or Sort.

Presence is otherwise rewarded inside this system: Push (BL14), console Route (BL11), clearing leak piles (BL15), and fixing a REFUSED feeder.

### States and Transitions

**Unit (this system's view of Grid's BELT state).**

| State | Entered by | Exits to |
|---|---|---|
| ON_LANE | Intake/container pull, inserter place, sorter route, splitter/merger hand-off, thrown landing, side-load | ON_LANE (next segment), ARM (inserter pick), PLACED (container or sorter buffer `TryInsert`), CONSUMED (feeder Accept or Misdeliver), WORLD (leak), PLACED-in-bag (removal) |
| ARM | inserter pick | ON_LANE, PLACED, CONSUMED (feeder), PLACED-in-bag (removal) |
| (in sorter buffer) | Grid PLACED in `sorter_buffer` | ON_LANE (route or Route) |

**Lane head.**

| State | Condition | Next |
|---|---|---|
| FLOWING | head below gate | AT_GATE when it reaches `L − 250` |
| AT_GATE | head at gate this tick | FLOWING (handed off), BLOCKED (exit refused) |
| BLOCKED(reason ∈ `dead_end`, `container_full`, `feeder_holding`, `feeder_refused`, `sorter_full`, `node_busy`, `leak_paused`, `belt_full`) | exit refused | AT_GATE retry by BL7 (wake-on-version, credit, or node) |

**Address Sorter.**

| State | Condition | Next |
|---|---|---|
| STARVED | buffer empty | RUNNING on first insert |
| RUNNING | credit cycles | STALLED(output) when the routed output is blocked; HALTED on Halt |
| STALLED(o) | head unit's output o blocked | RUNNING when o accepts, or after a filter edit or Route empties the stalled entry |
| HALTED [M2] | console Halt on | previous state when Halt is off |

**Inserter.**

| State | Next |
|---|---|
| WAITING (credit below one piece, or nothing pickable) | HOLDING on a pick |
| HOLDING (arm has a unit; `inserterPlaceDelayTicks` counting down) | PLACING |
| PLACING | WAITING on success; stays PLACING on refusal (BL7 retry) |

**Push.** NONE → ACTIVE(slot, window) on a valid Push channel start; ACTIVE → NONE on release, toggle timeout, invalidity or tile removal (BL14). A segment recompile re-anchors the window to the anchor tile's new segment.

**Leak end.** LEAKING ⇄ PAUSED. The end pauses at the per-end or global cap and resumes when picked-up or expired items drop it below the cap.

**Feeder slot.** Owned by Mail & Destinations (EMPTY / READY / THROTTLED / HOLDING / REFUSED). This system reads `FeederStateChanged` for the head's BLOCKED reason.

### Interactions with Other Systems

| System | Direction | Interface | Owner |
|---|---|---|---|
| Simulation Core | upstream | phase 5 `Automation.Step` binding; `construct` ids; event log (ADR-0008); run tick for F1; Sim dump of lane state | Sim Core |
| Content & Data Definitions | upstream | `BuildingDef.params` (belt `speedMmS`; sorter `throughputMilli`, `outputs`, `filterSlotsPerOutput`, `bufferGrid`; inserter `rateMilli`, `reach`, `filterable`; splitter/merger `ways`, `filterable`); stats `BeltSpeedMultPm`, `SorterFilterSlots`, `SorterThroughputMultPm`, `InserterRateMultPm`; RuleFlag `PresortOutfeed`; `automation.*` balance keys | CDD (schema), this GDD (values) |
| Building System | upstream | `OnPlaced` / `OnRemoving` hooks, `IPlacementRule` (feeder slot), registry queries `ConstructAt(tile)`, `rot`, `ConstructPlaced/Removed` + `epoch` | Building |
| Grid Inventory & Containers | both | `TakeUnit(c, selector, construct)`, `PeekOutfeed`, `TryInsert`, `CreateWorldItem(BeltLeak)`; `sorter_buffer` 4×2; container `version` for wake-on-version; BELT ledger state; spill-bag handover | Grid (API), this GDD (selector policy, BL13) |
| Mail & Destinations | downstream | `CanRegisterFeeder`, `OfferUnit`, `FeederStateChanged`; address fields `(district, street, number, kind)` for sorter filters | Mail |
| Throwing & Catching | upstream caller | `TryInsert(lane, point, unit)` per unit, partial accept; lane top-face geometry; `LaneInsert.flightId` | this GDD (lane), Throwing (flight) |
| Player Character & Movement | both | Push anchor (`IInteractable`, Channel); host-known chest point for push validity; lane velocity + push windows per tile for rider carry (P7); Configure anchor | PC (framework, riders), this GDD (verbs) |
| Health & Damage / Defenses | upstream (M3) | `EntityDowned{Construct}` → BL18 handover; belts 80 HP (B2) | Health / Defenses |
| Vehicles & NPC Drivers | downstream (M2) | parked loading-face query (zone, speed < 0.1 m/s) → `TryInsert` into cargo; NPC drops are Mail's transient feeders, not belts | Vehicles |
| Enemy AI & Waves | downstream (M3) | belts are walkable and passable to enemies (OQ-BLD-7(d)); Wall Breakers target belts (spec 11 §10) | Enemy AI |
| Perks & Perk Draft | upstream (M4) | Express Lane, Extra Labels, Greased Inserters, Presort, Postmaster's Eye, Bulk Depot (Grid) — via StatSheet / RuleFlags only | Perks |
| Map & Address UI | downstream (M2) | sorter filter editing UI (unlocked addresses only), `ConfigureSorter` request | Map & Address UI |
| HUD & Notifications | downstream (M2) | blocked-machine badge from BLOCKED / STALLED reasons; sorter overflow counter; "Belt → wrong house" from Mail's events | HUD |
| Networking & Session | both (M0) | lane events, checksum tiers, `LaneState` snapshot path, JoinState block (Network) | Networking (transport), this GDD (content) |
| Presentation & World Dressing | downstream | `BeltView` visual sim, curves, item meshes, LOD valve | Presentation |

## Formulas

Every value below is integer arithmetic in the Sim (ADR-0006). Run-tick `T` is Simulation Core's tick counter. All keys live in `content/balance/automation.jsonc` unless marked spec-locked. New numbers are **PROPOSED**.

### F1 — Lane advance

The laneAdvance formula is defined as:

`v = speedMmS × BeltSpeedMultPm × (inWindow ? operatedBeltMultPm : 1000) / 1 000 000` (one floor division; int64)
`A(v, T) = ⌊v × (T + 1) / 30⌋ − ⌊v × T / 30⌋` (int64)
`p′ = min(p + A, p_leader′ − beltSpacingMm)`; the head is capped at `gate = L − 250` unless its exit accepts.

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| speedMmS | int | 2000 (Mk1), 4000 (Mk2); validator: multiple of 1000 | data file (`BuildingDef.params.speedMmS`, spec 11 §9) | tier speed |
| BeltSpeedMultPm | int | 500–3000 | calculated (StatSheet; Express Lane 1500) | team stat |
| operatedBeltMultPm | int | 1000–2000; spec 1500 | data file (spec 11 §9 "operated belt multiplier") | push boost inside a window |
| v | int | 500–36 000 mm/s at knob extremes; v1 content max 9 000 (Mk2 × Express × push) | calculated | effective speed for this unit this tick |
| T | int64 | 0 … run length | Sim Core tick | run tick |
| A | int | 16–1 200 mm (v1 content: 66–300) | calculated | advance this tick |
| beltSpacingMm | int | 500 | constant (spec 11 §9) | slot pitch |

**Output Range:** the long-run speed is exactly `v` mm/s with no per-segment state, so a speed change or window entry never drifts or teleports a unit. A ≤ 1 200 mm < one tile, so a unit crosses at most one boundary per tick.
**Example:** Mk1 at 1000 ‰ gives A = 66, 67, 67 repeating (200 mm per 3 ticks). Mk2 gives 133, 133, 134. Mk1 + Express Lane gives 100 every tick. Mk2 + Express Lane, pushed, is 9 000 mm/s = 300 per tick.

### F2 — Capacity

The laneCapacity formula is defined as:

`laneRate = v / beltSpacingMm` (units/s) · `beltRate = 2 × laneRate` · `tileSlots = 2 × 2000 / beltSpacingMm = 8`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| v | int | 1 000–18 000 mm/s | F1 | lane speed |
| beltSpacingMm | int | 500 | constant | slot pitch |

**Output Range:** Mk1 4/s per lane (8/s per belt), Mk2 8/16, Mk2 + Express Lane 12/24.
**Example:** a Mk1 lane carries 240 units/min, twice a mailbox feeder (120/min, Mail F11). Belts are never the bottleneck of a single door: speed buys latency and rider travel.

### F3 — Credit gate (sorter, inserter, Intake belt-pull)

The creditGate formula is defined as:

each tick: `if credit ≥ 30000 and ready: act; credit −= 30000`, then `credit = min(credit + rateMilli, 30000 + rateMilli − 1)`
`rateMilli = baseMilli × multPm / 1000` (one floor division)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| baseMilli | int | sorter 2000; inserter 1250; belt-pull 1000 | data file (spec 11 §9; `outfeedPullMilli` PROPOSED) | pieces/s × 1000 |
| multPm | int | 500–4000 | calculated (`SorterThroughputMultPm`, `InserterRateMultPm`; the belt-pull is not scaled) | stat |
| credit | int | 0 … 30000 + rateMilli − 1 | state, per construct (starts at 30000) | one piece = 30000 |

**Output Range:** exact long-run rate for **any** `rateMilli`: the remainder is kept and never truncated. There is no burst, because a blocked actor stores at most one piece. Credit is spent only on a successful act, so a blocked actor fires on its first free tick.
**Example:** inserter 1250 acts every 24 ticks; Greased (×2, 2500) every 12. A sorter with Postmaster's Eye (4000) acts on intervals of 7 and 8 ticks, 4 pieces per 30 ticks exactly. *Mail F11 caps at 30000 and so loses the remainder for rates that do not divide 30000 (1700, 4000). Hand-back OQ-BL-H4.*

### F4 — Line throughput (bottleneck chain)

The lineThroughput formula is defined as:

`sysRate = min(supply, Σ outfeed pulls, laneRate × lanesUsed, Σ sorter rates on the path, Σ final-stage rates)` (units/s)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| supply | float (BalanceSim only) | ≈ 0.5–3.2 /s | calculated (spec 11 §3, × playerScale) | mail arriving at the Intake |
| outfeed pulls | int | 1.0 per belt-pull; 1.25 per inserter × `InserterRateMultPm` | F3 | Intake taps |
| sorter rate | int | 2.0 × `SorterThroughputMultPm` | F3 | per sorter on the path |
| final stage | int | 2.0 per feeder (Mail `insertRate`); container `TryInsert` unbounded | Mail F11 | doors fed |

**Output Range:** BalanceSim agent rate (spec 11 §4). This is not a Sim formula.
**Example:**
- **Reference factory** (40 tiles, 1 sorter, 4 inserters, one at the Intake): `min(supply, 1.25, 8, 2.0, …)`. The limit is the Intake inserter at 75 units/min. At the shift-3 mix (≈ 19.2 ¢/unit) that is ≈ 1 440 ¢/min.
- **Solo shift-3 supply** is ≈ 33/min (≈ 634 ¢/min), so the factory runs at ≈ 44 %, as spec 11's "cap: spawn rate" intends.
- **8 players, shift 5**: ≈ 3.2 /s exceeds one sorter (2.0 /s), which confirms spec 11's "≥ 2 belt lines".
- A 2×2 sorter has **3** filtered outputs, so spec 11's "sorter to 8 mailboxes" needs ≥ 3 sorters chained through Overflow (hand-back OQ-BL-H5).

### F5 — Push window

The pushWindow formula is defined as:

`inWindow(u) = ∃ active push P on u's segment: |tileIndex(u) − tileIndex(P.anchor)| ≤ pushReachTiles` (evaluated at tick start)
`latencySaved = windowLen × (1 − 1000 / operatedBeltMultPm) / v`, where `windowLen = (2 × pushReachTiles + 1) × 2000`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| pushReachTiles | int | 1–5 | data file (PROPOSED 3) | half-window in tiles |
| operatedBeltMultPm | int | 1000–2000 | data file (spec 1500) | boost |
| pushToggleMaxSeconds | int | 3–15 | data file (PROPOSED 6) | toggle-mode auto-end |

**Output Range:** the window is ≤ 7 tiles (14 m) per pusher, and overlapping windows are unioned. Steady throughput is unchanged, because the unboosted rest of the line still limits it.
**Example:** a Mk1 window is 14 m. A unit crosses it in 7.0 s unpushed and 4.67 s pushed, saving 2.33 s of latency. A rider in the window moves at 3 m/s plus their own walk, capped at 12 m/s (P7).

### F6 — Leak and population bounds

The leakBound formula is defined as:

`leakRatePerEnd ≤ 30 / beltLeakIntervalTicks` (units/s) · `liveLeaks ≤ min(beltLeakPerEndMax × leakingEnds, beltLeakGlobalMax)` · `beltUnits ≤ min(Σ tiles × 8, beltItemCap)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| beltLeakIntervalTicks | int | 6–30 | data file (PROPOSED 15) | ticks between leaks per end |
| beltLeakPerEndMax | int | 3–12 | data file (PROPOSED 6) | live BeltLeak WorldItems per end before the end pauses |
| beltLeakGlobalMax | int | 32–128 | data file (PROPOSED 64) | island-wide live BeltLeak cap (≤ ⅛ of `worldItemCap` 512) |
| beltItemCap | int | 3 200–8 000 | data file (PROPOSED 5 000, spec 07 §8) | units on lanes + arms |

**Output Range:** leaks are ≤ 2 /s per end and ≤ 64 live. WorldItem creation from leaks is ≤ 1 per leaking end per tick.
**Example:** a cut 40-tile Mk1 belt sprays 6 letters over 3 s, then backs up behind a "jammed" end. 400 tiles hold ≤ 3 200 units, so 5 000 units need ≥ 625 tiles (fixture, Acceptance Criteria).

## Edge Cases

### Same-tick races

- **If a player takes a leak pile in phase 0 and its end is due to leak in phase 5**: the pickup has already freed the per-end count, so the end may leak this tick if its stagger allows. The count is a synchronous Grid query by `sourceConstructId` (hand-back OQ-BL-H3), never an event cursor.
- **If a thrown stack lands on a lane in phase 4 and an inserter or sorter wants the same entry slot in phase 5**: the thrown units keep their slots, and the machine retries next tick with its credit unspent. Throwing runs first (Sim Core R3).
- **If a player's `quick_move` and an inserter's pick contest one unit or cell**: the player wins in phase 0. The inserter gets `no_unit` or `no_room` and retries next tick with its credit unspent; the player's UI may read "The inserter got it first" only when the inserter won on an earlier tick (Grid R7).
- **If a console Route and auto-examine target one buffer in one tick**: the Route order runs first, and auto-examine takes the lowest remaining mailId.
- **If a feeder tile is deconstructed in phase 0 with a unit at its gate**: no offer is made and the unit goes to the removal bag (Mail: "destroyed before the Belt step").
- **If a machine is destroyed in phase 10**: the BL18 handover completes in phase 11 of the same tick (Defenses' reaction), so a ruin's units never advance another tick.

### Zero, max and limits

- **If a full line meets CLOSED doors at Prep (feeders HOLDING)**: Intake pulls stop because the lane entry is occupied, and the Intake keeps its mail. At OPEN each feeder drains at 2/s, so a full 40-tile line (320 units) empties in ≈ 160 s. Nothing is lost; stale units delay live mail behind them (intended pressure).
- **If units on lanes or arms become dead letters at Payday start**: they are swept like any other location. Belt hands them to Grid's sweep as returned-to-sender (hand-back OQ-BL-H3: Grid R12's sweep list gains BELT).
- **If an end reaches `beltLeakPerEndMax` (6), or the island reaches `beltLeakGlobalMax` (64)**: that end pauses (BLOCKED `leak_paused`) and the belt backs up. Nothing is destroyed.
- **If a leak end faces water**: piles float 10 s and return to the back of the Intake queue (Grid R12), counting against the caps while alive. The end recycles at most 6 units per 10 s, with no deadline benefit.
- **If `beltItemCap` is reached**: Intake belt-pulls, inserter picks from containers and sorter routes get `belt_full`. Lane-to-lane hops and lane-to-arm picks are never refused, and the source keeps its unit.
- **If `SorterThroughputMultPm` is at its minimum (500)**: one examine per 30 ticks. The buffer fills and the input backs up as normal.
- **If a sorter has no filters set**: every unit goes to Overflow. With no belt on Overflow, the buffer fills and the input backs up; the sorter is STALLED on Overflow.
- **If a Large reaches a sorter whose buffer is not empty**: `TryInsert` is refused (it fits only rotated into an empty 4×2), the belt backs up, and the Large enters on the tick the buffer empties (route-before-accept, BL20). If the sorter is also STALLED, the Large waits until the stall clears or a Route clears it. No deadlock outlives an operator.
- **If many belt-pulls line the Intake face**: each pulls 1.0/s, so N pull points give N/s. The face's width (`outfeedLaneTiles`, World Generation) bounds N. Cheap and many tiles against fewer, faster inserters is an accepted trade.

### Topology edits

- **If a tile is removed mid-segment**: its units go to one spill bag. The upstream part keeps its positions and now faces a new end (a leak, if it faces nothing). The downstream part re-bases by subtracting the upstream length plus the removed tile's 2 000 mm (BL-AC-5).
- **If a new tile joins two segments**: downstream positions re-base by adding the length of everything upstream of them, the new tile included. The gap stays ≥ 500 mm (head ≤ L − 250, tail ≥ 250), and no unit moves.
- **If a push anchor's segment recompiles**: the window re-anchors to the anchor tile's new index and clips to the new segment from the next tick.
- **If a sorter is deconstructed**: removal is phase 0, so no route is half-done. Buffer entries go to one bag, any Route order ends, the console closes, and the input belt now faces a new end and leaks. Intended; the deconstruct prompt warns "Buffer: N units".
- **If a belt forms a closed ring**: the lowest-id tile is the cut. Units circulate and count toward the cap, and a fully packed ring stalls with nothing lost. A ring through a sorter's Overflow spends the sorter's rate on unmatched mail. Both are accepted degenerate builds, and there is no placement guard.
- **If a belt crosses a street and a truck drives over it**: vehicles drive over belts (spec 13). The truck is not a receiver unless it is parked in a loading zone in front of a belt end. Riders and units are unaffected.
- **If a parked truck leaves while a belt end is feeding it**: the next attempt sees speed ≥ 0.1 m/s and fails, and the end becomes a leak or dead end per BL7.

### Stats, flags and phases

- **If `BeltSpeedMultPm` changes mid-run**: only A changes from the next tick (F1 is stateless). No drift, no teleport.
- **If Presort toggles, or the current street exists only in the backlog**: each pull point's run resets. "Empty" means empty in the Intake's street index. The run moves to the street holding the lowest mailId, and returned mail inside a street follows Grid's street-index order (hand-back OQ-BL-H2 to state it).
- **If a filter is edited while the sorter is STALLED**: routing is re-evaluated at the next credit, so a stalled unit may now match another output and move.
- **If the solo host pauses**: no ticks run. Credits, windows, leak timers and F1 positions freeze.
- **If a Push toggle reaches 6 s, or the pusher walks beyond the interaction leash**: `PushEnd` is raised that tick, and riders drop to the base speed from the next tick.

### Degenerate strategies

- **Belt hoards** (a 700-tile ring holding 5 000 units outside the Intake): accepted. Deadlines still run, so the mail goes late and dead. The HUD shows belt population (OQ-BL-6).
- **Eight pushers in a row** (windows union to 56 tiles): latency over that stretch drops by ≈ 33 %, and throughput is unchanged. Accepted.
- **Belts as fast travel**: capped at 12 m/s, and the pusher gets no self-boost (Player Character P7). Accepted.
- **Belts as walls**: belts are walkable and not solid (Building B2), so they cannot block goons. Wall Breakers deal 150 to an 80 HP belt (spec 11).
- **Mis-sort floods**: bounded by Mail's door and team buckets and the wallet floor, and visible through the "Belt → wrong house" alert, the per-output counters and Halt.

## Dependencies

| System | Direction | Hard / Soft | Status | Interface |
|---|---|---|---|---|
| Simulation Core | upstream | Hard | In Review (Provisional) | phase 5 binding, run tick, `construct` ids, event log, dump/hash of lane state |
| Content & Data Definitions | upstream | Hard | In Review (Provisional) | `BuildingDef.params` per behaviour; CD12 stats; RuleFlag `PresortOutfeed`; `automation.*` keys (values here) |
| Building System | upstream | Hard (M-1b) | In Review (Provisional) | `OnPlaced` / `OnRemoving`, `IPlacementRule`, registry queries, `epoch`; answers OQ-BLD-7 |
| Grid Inventory & Containers | upstream | Hard (M-1b) | In Review | `TakeUnit` / `PeekOutfeed` / `TryInsert` / `CreateWorldItem`, `sorter_buffer`, container `version`, BELT ledger, spill bag |
| Mail & Destinations | upstream | Hard (M-1b) | In Review | `CanRegisterFeeder`, `OfferUnit`, `FeederStateChanged`, address fields |
| World Generation | upstream | Hard (M1; stub M-1b) | **not written — provisional** | `outfeedLaneTiles`, lot `approachTile` / `mailboxTile`, tile heights; M-1b uses the hand-authored street |
| Player Character & Movement | both | Hard (M-1b) | In Review | `IInteractable` (Push, Configure); host chest point (F5); rider carry reads lane speed + windows (P7) |
| Throwing & Catching | downstream caller | Soft (M-1b) | In Review | `TryInsert(lane, point, unit)`; `flightId` on `LaneInsert` |
| Input & Controls | upstream | Soft (M2) | In Review | Channel / hold-as-toggle grammar (G7) for Push; Station overlay for the console |
| Health & Damage, Defenses | upstream | Hard (M3) | Designed (Provisional) / not written | `EntityDowned{Construct}` → BL18 |
| Vehicles & NPC Drivers | downstream | Soft (M2); **pillar-load-bearing for BL21**: if Vehicles does not adopt the loading face, Grid OQ16 reopens | not written | parked loading-face query |
| Enemy AI & Waves | downstream | Soft (M3) | not written | belts passable, targetable |
| Perks & Perk Draft | upstream | Soft (M4) | not written | StatSheet modifiers, RuleFlags only |
| Networking & Session | both | Hard (M0) | not written | transport, snapshot path, JoinState envelope (Network) |
| Map & Address UI, HUD & Notifications, Presentation & World Dressing | downstream | Soft | not written / In Review | filter picker; blocked badge and counter; `BeltView` |

**Bidirectionality.** These GDDs already name this system:
- Grid Inventory, Mail & Destinations, Throwing, Player Character, Building and HUD;
- the systems index, which lists Vehicles, Map & Address UI and Presentation.

Content & Data lists Belt among its downstream owners. Health & Damage does not name Belt (belts register through Building): no change is needed.

**Sim / Presentation split [D].** **Sim** (`src/PerformativeMail.Sim/Automation/`, Godot-free) holds every rule in BL1–BL21 and F1–F6:
- lanes, segments, nodes, sorter routing, inserter arms, push windows, leaks and the item cap;
- event raising and the checksum computation.

`LaneStep`, `TransferRule` and the checksum are **shared pure functions** compiled once and used by the host Sim and by the client's visual sim (ADR-0016 pattern). The client never runs authoritative belt state.

**Presentation / Client.Core** holds:
- `BeltView` (the visual-sim journal, render clamp, LOD valve, MultiMesh);
- the plates, the console panel and the Push prompt;
- the rider carry source handed to Player Character.

Nothing in Presentation decides an insert, transfer or endpoint.

## Network

**Model.** v1 netcode follows spec 13 §3:
- belts and sorters are host-authoritative;
- Godot high-level multiplayer runs behind the adapter (ADR-0011);
- there is **no interest management**: everything goes to everyone.

Godot 4.4–4.7 multiplayer behaviour is NOT SOURCEABLE (VERSION.md), so byte sizes are adapter contracts and items marked **[SPIKE]** are gated on the M0 multiplayer spike.

The spec 06 §4 model is kept: server lane events, a client visual sim, and periodic resync. Two parts of it are **amended** (OQ-BL-H8):
- **Checksum.** It is root plus detail on exact positions, replacing per-segment checksums quantised to 0.25 m. Quantised hashes still flip at bucket edges, and per-segment cadence costs as much as the traffic itself.
- **`positionAtTick`** is replaced by the event's tick tag.

**Stance.** The client's belt state at tick R is a pure function of:
- the shared integer `LaneStep`;
- the replicated construct geometry (Building's events);
- the tick-tagged belt events up to R.

The host decides every insert, remove, transfer and endpoint.

**Events [M-1b in the log; M0 on the wire].** Sizes are body bytes and include a 1 B type. All events are `Replicated` (ADR-0008) and flushed at the shared 15 Hz cadence.

| Event | Bytes | Notes |
|---|---|---|
| `LaneInsert{seg u16, lane+kind u8, district u8, [pos u24], [flightId u16]}` | 5 at the lane entry; 8 mid-lane; 10 with flightId | sources, inserter places, thrown landings |
| `LaneRemove{seg, lane+reason u8, [seq u16]}` | 4 at the head; 6 mid-lane | reasons: Consumed, Leak, ToContainer, SorterIn, InserterPick, Removed. Lanes are FIFO, so a head removal needs no id; mid-lane picks use `seq`, a per-lane insert counter both sides advance implicitly |
| `LaneTransfer{srcSeg, dstSeg, flags u8}` | 6 | splitter, merger and side-load hops, and sorter routes. The destination position is the rule's (BL5, BL6), never sent |
| `PushStart{slot u8, seg, fromTile u16, toTile u16}` / `PushEnd{slot}` | 8 / 2 | tick-tagged, so rider carry and `LaneStep` agree; ≤ 4 per second per player |
| `SegmentMap{changed[{segId u16, headConstructId u32}]}` | 1 + 6 each, ≤ 7 per entry (ADR-0008 continuation chunks) | after any belt-affecting `ConstructPlaced` / `ConstructRemoved`. The client derives the partition with the shared `BeltGraph`, and the host names the ids |
| `SorterConfig{constructId u32, output u8, slots…}` | ≤ 40 | `ConfigureSorter` result. Filters are also in JoinState |
| `LaneChecksumRoot{tick u32, segCount u16, hash u32, epochLow u16}` | 13 | every 2 s |

**Requests.**
- `ConfigureSorter{reqId, constructId, output, slots[]}`
- `SorterHalt{reqId, constructId, on}`
- `SorterRoute{reqId, constructId, entryId, output}`, as a Grid-style op on the open buffer
- `BeltDetailRequest{tick}`
- `LaneStateRequest{segs[]}`

Push uses Player Character's channel verb. **Up-rate ≤ 0.2 kbps.**

**Order.** Within one Automation.Step the order is BL20's. Building's `ConstructRemoved` (and the spill-bag events) precede the `LaneRemove{Removed}` and the `SegmentMap` of the same handler. `OpReply` is last (ADR-0007/0008).

**Client visual sim [M0].**
- **Time domain.** The client advances belts at `RenderTick = hostTickEstimate − beltRenderDelayTicks`, default 6 ticks (200 ms). This is the remote-entity interpolation domain, so `flightId` adoption lines up with the flight.
- **Applying events.** Events apply when RenderTick reaches their tick, and advance is whole `LaneStep` ticks with a render-side lerp.
- **Stalls.** The client's head parks at the gate exactly like the host's. It does not need to know why. Only an event removes or transfers a unit.
- **Render clamp.** Units on endpoint-terminated segments are drawn no further than `L − 350` mm, so a unit is **never rendered at an endpoint before the host confirms** (spec 12 M2 gate).
- **Late events.** Under loss, the client keeps per-segment checkpoints every 8 ticks and a `beltRewindTicks` (30) journal. A late event rewinds to the checkpoint, replays, and eases the visual offset over ≈ 150 ms. Anything older than the window is a gap.

**Checksum and resync [M0].**
- **Hashes.**
  - Segment hash: count, then lane, kind, district and exact position per unit, taken at the end of Automation.Step of tick T.
  - Root: the segment hashes folded in `segId` order.
  - The hash function and byte order are ADR-0027's, platform-independent, and never `GetHashCode`.
- **Root mismatch.** `BeltDetailRequest(T)` returns per-segment hashes (≈ 8 B each). `LaneStateRequest` then fetches the divergent segments, rate-limited to one per segment per 5 s per peer.
- **`LaneState`** (8 + 5 B per unit) travels the adapter **snapshot path**, not the event log (ADR-0008's 48 B ceiling). It is tagged with T and enqueued on the event channel after every event with tick ≤ T **[SPIKE]**. The client replaces the segment at T and replays journal events after T.
- **Gaps.** A gap in the event stream, an unknown `segId` or `seq`, or an `epochLow` mismatch with Building's epoch marks the belt network suspect. The client then runs a ConstructList resync first and a paced all-segment `LaneState` after it (debounce 15 ticks).

**Riders [M0].** The owning client computes carry (Player Character P7) from:
- each tile's def and rotation (Building's events);
- `BeltSpeedMultPm` (low-rate global block);
- the push windows from `PushStart` / `PushEnd`.

It samples the latest received state, not the delayed domain. The host tolerates the difference with `carryGraceTicks` (10) in Player Character's consistency check (hand-back OQ-BL-H9). Push response is predicted locally (GF1).

**JoinState / RejoinState [M0].**
- **Header:** `{buildingEpoch u32, tick u32, speedMultPm u16, segCount u16}`.
- **Per segment:** `{segId u16, count u16, flags u8}` plus `count × 5 B` units (`pos u24, lane+kind u8, district u8`) in head order.
- **Then:** push windows (≤ 8 × 8 B), sorter configs (≤ 40 B each), Halt flags and inserter arm units (5 B each).
- **Size.** The worst case of 5 000 units and 300 segments, with sorter configs per output, is ≤ 30 KB: one chunk under 64 KB. Typical ≈ 2 KB.
- Sorter buffers ride Grid's container rules (viewers only).
- Rejoin uses the same block.

**Bandwidth (per client, down; the 10 s-mean reading of spec 06 §4.2 per Grid OQ27).**
- **Typical**, 30 segments and 6 units/s at ≈ 33 B per unit path, plus root checksum and headers: ≈ **1.9 kbps**.
- **Shift-5 worst**, 80 units/s with explicit events: ≈ 31 kbps. Link-indexed 4 B transfers: ≈ 20 kbps. Client-derived transfers (V3): ≈ 8 kbps.
- **Proposed Belt share: typical ≤ 2.5 kbps, worst ≤ 10 kbps** inside the 80 kbps worst envelope. That sustains ≈ 26 units/s with explicit events. Real 8-player supply is ≤ ≈ 3.2 units/s, so the 80/s case is a stress bound.
- The running total of the other GDDs' typical shares (≈ 35 kbps against 40) is already oversubscribed. Networking & Session must re-baseline it (hand-back OQ-BL-H10).

**Valve order (cheapest loss first).**
1. Checksum cadence 2 s → 4 s.
2. Flush 15 → 10 Hz (tick tags make this lossless).
3. Link-indexed 4 B transfers.
4. Per-peer `beltHighWaterBytes` (16 KB): discard that peer's queued belt events and send a paced all-segment `LaneState`.
5. V3 client-derived splitter, merger and sorter hops (the client never decides an endpoint), only if the M2 measurement demands it.

Insert, Remove and Transfer are never dropped except by step 4's resync. Far-visual density reduction saves render cost, not bandwidth.

**Never trusted from the client:**
- anything about units (kind, district, position, `seq`, existence);
- `segId` (the client names constructs; the host resolves);
- push slot, window and duration;
- sorter filter values (host range-checked);
- endpoint arrival, transfer outcome and flight adoption.

The client never sends checksum results, only requests, which are rate- and byte-capped. **Not checked by design:** distance to a belt or sorter (spec 13 §3; the console is the only UI path, BL10).

**Ownership.**
- **Adapter owns:** codec, flush cadence, per-peer queues, the high-water valve, `LaneState` / JoinState chunking and ordering, request dedupe and rate limits, the RenderTick clock.
- **Sim owns:** lane state, `LaneStep`, `TransferRule`, endpoints, segment ids and `SegmentMap`, push validity, hash content.
- The Sim never sees peers.

## Tuning Knobs

Keys live in `content/balance/automation.jsonc` unless marked BuildingDef. PROPOSED unless spec-sourced. **Not knobs here:** the feeder rate and slot (`insertRate`, `maxAutomatedFeeders`: Mail), build costs and HP (Building B2), `worldItemCap` (Grid), `supportSpeedCapMm` (Player Character), and perk values (Perks / CDD CD12).

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `belt_mk1/mk2.params.speedMmS` (BuildingDef) | 2000 / 4000 (spec) | 1000–6000, multiple of 1000 | latency, rider speed | belts feel inert; walking wins every trip | riders approach the 12 m/s cap; visual sim cost per frame rises | `supportSpeedCapMm`, `operatedBeltMultPm` |
| `beltSpacingMm` | 500 (spec) | 400–1000 | lane capacity (F2), units per tile | dense clutter, more units for the item cap | lane rate drops below a feeder (2/s) at Mk1 when > 1000 | `beltItemCap`, Mail `insertRate` |
| `sorter.params.throughputMilli` (BuildingDef) | 2000 (spec) | 1000–4000 | sorter rate (F3) | one sorter starves a street | sorters out-run feeders, so more doors per sorter | `insertRate`, `SorterThroughputMultPm` |
| `inserter.params.rateMilli` (BuildingDef) | 1250 (spec) | 800–2000 | Intake tap and container I/O | Intake taps bottleneck everything | inserter equals a belt-pull, so a belt-pull is pointless | `outfeedPullMilli` (must stay below) |
| `inserterPlaceDelayTicks` | 6 | 3–12, ≤ 30000 / max rate | arm hold time, readability | swing unreadable (GF2) | caps the Greased rate | `InserterRateMultPm` clamp |
| `outfeedPullMilli` | 1000 | 500–1250, < inserter base | raw belt tap from the Intake | the cheap tap is useless | inserters pointless at the Intake | `outfeedLaneTiles` |
| `presortRunMax` | 12 | 4–40 | Presort street batching | streets interleave and the perk feels weak | one street starves the others | Presort perk |
| `operatedBeltMultPm` | 1500 (spec) | 1000–2000 | push boost | push feels placebo (fantasy test 3) | push approaches dominant-job territory | `pushReachTiles` |
| `pushReachTiles` | 3 | 1–5 | push window (F5) | no visible effect | AFK pusher becomes a job (PC OQ25) | `operatedBeltMultPm` |
| `pushToggleMaxSeconds` | 6 | 3–15 | toggle-mode push | toggle users re-press constantly | AFK push via toggle | Input G7 |
| `throwSnapMm` | 1000 | 500–2000 | thrown landing tolerance on a lane | near-miss throws bounce off a belt | units teleport visibly along the lane | Throwing preview |
| `beltLeakIntervalTicks` | 15 | 6–30 | leak spray rate | WorldItem churn | a cut belt looks like a clog, not a spray | `beltLeakPerEndMax` |
| `beltLeakPerEndMax` | 6 | 3–12 | pile per end before jam | jams before players notice the spray | piles clutter; pick-up chores | `beltLeakGlobalMax` |
| `beltLeakGlobalMax` | 64 | 32–128 (≤ ⅛ `worldItemCap`) | island-wide leak WorldItems | leaks pause everywhere at once | crowds out throws and spills under the 512 cap | Grid `worldItemCap` |
| `beltItemCap` | 5000 (spec 07 §8) | 3 200–8 000 | perf ceiling, JoinState | hoards hit `belt_full` early | tick and JoinState budgets break | perf fixture |
| `beltRenderDelayTicks` (client) | 6 | 3–10 | visual sim latency | late-event rewinds visible | push and throw adoption feel laggy | ADR-0020 interpolation delay |
| `beltRewindTicks` (client) | 30 | 15–60 | late-event tolerance | more full resyncs | client memory/CPU | checksum cadence |
| `beltChecksumSeconds` | 2 (spec 06) | 2–4 (valve) | divergence detection | bandwidth | slow correction | valve ladder |
| `beltHighWaterBytes` (adapter) | 16 KB | 8–32 KB | per-peer valve | frequent full resyncs | memory, stale views | Networking |
| `visualItemCap` (client) | 2500 | 1500–3200 | far-LOD instances | sparse-looking belts | frame budget | LOD distances 30 / 60 m |

**Coupled rule (ContentValidator, hand-back to CDD):** `outfeedPullMilli < inserter rateMilli` and `30 / beltLeakIntervalTicks × beltLeakGlobalMax ≤ worldItemCap / 4`.

## Visual/Audio Requirements

*From art-director (2026-10-16). Cue names are proposals for audio-director. Art-bible principles applied:*
- **§1**: glyph + colour, never colour alone.
- **§2**: blocked and leak states are physical.
- **§3**: the state palette appears only on emissive strips.
- **§4**: Tier B constructs, ≥ 15 % Postal Blue, Charcoal skirt.

**Display rules.**
- Belt units carry **kind + district only**. No address text appears on belt items, so sorter correctness is read from the output plates.
- **Render clamp:** the client never draws a unit's centre beyond `L − 350` mm until the host confirms the hand-off (Network). Nothing appears at an endpoint early.
- Feeder arrival, door beats and pills are **Mail's** cues (mail-destinations.md Visual/Audio). The belt side adds none.

| Event / state | Visual | Audio | M |
|---|---|---|---|
| Belt surface (Mk1 / Mk2 / Express) | Charcoal frame. Roller bands at 0.5 m pitch (ΔL* ≤ 8) scroll at exactly the lane speed. Mk2 adds raised rails and a second Postal Blue cap stripe. A 25 cm centre ridge splits the lanes. Go Green emissive chevron strip at 1.0 m pitch, scroll ≤ 4 Hz. Express is the same mesh moving faster | one loop per network, `sfx_belt_loop_mk1/mk2`; Express +2 semitones | M-1b flat box + strip; M2 rollers, rails |
| Units | Letter flat rect, parcel cube, Large tall box (≤ 0.45 m along travel), Kraft body. District-colour band on top and long sides; district glyph ≥ 0.18 m on top. Glyph to 10 m, band to 30 m, silhouette beyond | none per unit | M-1b capsules by kind; M1 glyph |
| Curves | Quarter arcs; units yaw to the tangent. Inner/outer apparent speed differs ≤ 25 % (cosmetic; Sim lengths are equal, BL3) | — | M2 |
| Splitter / merger | Diverter paddle flips to the chosen output in 0.1 s and holds; merger tongue alternates | `sfx_node_flip` ≤ 3 voices | M2 |
| Sorter plates | One plate per output plus the overflow tray: swatch-with-glyph ≥ 0.25 m and a patterned stripe, readable at 10 m (text only within 3 m). Multi-slot: 3 stacked swatches, then "+N". Empty slot: blank Chalk plate with a dash | low hum | M-1b plates; M2 stripes |
| Sorter decision | On the route event, the matched plate pops (squash 0.9, ≤ 0.1 s, hold 0.15 s) and the unit leaves that chute. Above 3 routes/s, one hopper shudder replaces the pops | `sfx_sorter_route`, one timbre per output, ≤ 3 voices | M-1b |
| Inserter | Bulbous arm: snap pick ≤ 0.1 s, 200 ms swing (= `inserterPlaceDelayTicks`), release, return. The unit is visible on the arm. Above 3 cycles/s the swing shortens, never below 120 ms | `sfx_ins_pick`, `sfx_ins_place` | M-1b |
| Healthy back-up | Units compress to 500 mm; chevrons freeze **static green**. `feeder_holding` / throttled never goes red | none | M-1b |
| Blocked | After **2.0 s** of continuous refusal (`dead_end`, `container_full`, `feeder_refused`, `sorter_full`, `leak_paused`): the head tile's strip turns **Stamp Red, static, with an X**, and upstream stays static green. HUD owns the badge and its "names the door" text | `sfx_blocked_tick` ≤ 1 per 2 s per head, nearest 2, ≤ 25 m | M-1b |
| Sorter stalled / Halt | Stalled: the blocked output's plate takes an X stamp and the lid sits half-open. Halt: lid down, Chalk stop paddle, pause glyph, and the buffer fills without red | tick / `sfx_sorter_halt` | M2 |
| Leak | Each unit hops off the end in a 0.3 s arc into an outlined WorldItem with a Chalk puff. On pause the end tile shows a red X and units compress. Piles are never red | `sfx_leak_flop` ≤ 2 voices; `sfx_leak_jam` once on pause | M-1b |
| Push | Pusher braces (15° lean, hands on rail). The window shows a Chalk 25 % floor band and 1.5× chevrons, visible to all. On a blocked segment: strain pose and the prompt names the jam (BL14) | `sfx_push_strain` loop, local reserved voice | M-1b band; M2 pose |
| Riding | 0.9 squash on step-on; feet synced to speed, no slide | `sfx_belt_scuff` ≤ 2 voices | M2 |
| Console Route | Operator visible with hands on the lid; chute flaps at ≤ 8/s; `output_blocked` → plate X and a bonk | `sfx_route_ticker` 1 voice | M2 |
| `belt_full` | Neutral Chalk "FULL" stamp on the source for 1.5 s. Not red: it is not the player's mistake | `sfx_belt_full_bonk` ≤ 1 per 5 s island-wide | M-1b |
| Far LOD (visual only) | ≤ 30 m full. 30–60 m every 2nd unit by lane-order parity, so it never shimmers. > 60 m units hidden; a 3-state busy texture (empty / sparse / dense) from lane count. ±3 m hysteresis, 0.3 s dither. `visualItemCap` 2 500 drops the farthest first | — | M2 |

**Audio caps.**
- Belt voices total ≤ 12, below Mail's priority inside the 16-voice mail budget.
- Per category: loops 3 (merged per chunk), inserter 4, sorter 3, leak 2, blocked 2.
- Merging: 4 per 250 ms, by position.
- No per-unit sounds.
- The local player's own Push and Route use a reserved voice.

**Reduced motion.**
- Strip and roller scroll become static printed arrows. Units still move, because they are the truth.
- The inserter becomes a 2-pose cut.
- Plate pops become a single 0.1 s Chalk rim flash.
- The push band is static.
- Leaks cut to the pile.
- The busy texture is static.

**Photosensitivity.** Belt sources together make ≤ 3 flashes/s on screen. The strip's temporal frequency is ≤ 4 Hz. X pops are debounced by 2 s.

**Colour-blind redundancy.** Moving / static / X, the audible blocked tick, and plate glyph + stripe.

**Art-bible amendments (PROPOSED, hand-back OQ-BL-H7):**
- **A1:** static green = "backed up, healthy"; red X only for a refusal needing action, after 2 s.
- **A2:** server-driven animations snap → hold with no anticipation (the inserter is exempt).
- **A3:** belt units count as carried items for the outline within 20 m.
- **A4:** the Chalk push band and the plate rim flash.
- **A5:** the 3-state busy-belt texture.

> **📌 Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:belt-logistics` to produce per-asset visual descriptions, dimensions, and generation prompts from this section.

## Game Feel

Targets are PROPOSED and measured in a Release export.

| # | Target | Number |
|---|---|---|
| GF1 | Push response | Pose and window band ≤ 2 frames (33 ms) after press, predicted locally. Units visibly accelerate ≤ 100 ms after press on a host (≤ RTT + 100 ms on a client), with a 0.1 s ramp to 1.5×. Reconciliation never moves a unit backward. |
| GF2 | Inserter readability | ≥ 120° sweep over ≥ 6 frames at the base rate. The unit is on the arm from the pick frame and leaves within ±1 tick of the place event. |
| GF3 | Spacing rhythm | Free-flow neighbour gap 500 ± 20 mm on screen. A fixed point sees 4 units/s per lane on Mk1. A checksum correction moves a unit ≤ 20 mm per frame, eased over ≤ 0.5 s. |
| GF4 | Rider snap | Lane-velocity error ≤ 5 % within 3 ticks of step-on (Player Character P7 owns the carry, this GDD the lane speed). |
| GF5 | Sorter tell | The plate pop starts ≤ 1 frame after the route event. ≥ 90 % of testers name the output from 10 m. |
| GF6 | Blocked tell | X appears ≤ 0.1 s after the 2.0 s debounce and clears ≤ 0.15 s after the exit accepts. Zero X events from `feeder_holding` / throttle across a 5-minute 2/s feeder fixture. |
| GF7 | Frame cost | BeltView CPU ≤ 1.0 ms + belt GPU ≤ 1.5 ms (≤ 2.5 ms) of the 16.6 ms frame at 400 tiles / 3 200 units in view (BL-AC-60). |

## UI Requirements

This GDD defines the information and verbs. Layout belongs to `/ux-design`. Screens and widgets are owned by Map & Address UI (filter picker), HUD (badge, counter), Inventory & Build UI (buffer grid) and Input & Controls (bindings).

| Surface | Contents | Owner | M |
|---|---|---|---|
| **Sorter panel** (Station overlay, opened by Configure ≤ 2.5 m) | 3 output cards (Front / Left / Right) plus Overflow, in the evaluation order with order numbers. Each card has up to `SorterFilterSlots` slots. A slot editor offers district → street → number range (lo–hi) → kind, picking only unlocked addresses via swatch + glyph, with no free text. Per-output routed counters and "Unmatched → overflow: N". The 4×2 buffer as a Grid view with Route drag targets per output. Halt toggle. Operator chip with hat icon; read-only state "Routing: [Name]" for others | Map & Address UI (picker), Inventory & Build UI (buffer), this GDD (rules) | M-1b plates and fixed filters (debug); M2 panel |
| **Push prompt** | "Push" channel ring. On a BLOCKED segment: the reason and location ("Held: 9 Jade Close refused", "Sorter full", "Dead end") | Player Character prompt, this GDD text | M-1b |
| **Blocked-machine badge** | After 2.0 s BLOCKED / STALLED: world badge plus compass echo, naming the cause and door. One per machine, merged per network | HUD (H-WIDGET M2) | M2 |
| **Splitter / inserter config** | Tap Configure: one kind filter per output / per inserter | this GDD (verbs), Inventory & Build UI (widget) | M2 |
| **Build ghost additions** | Feeder target highlight on an `approachTile`. Veto reasons `feeder_slot_taken` / `feeder_not_on_approach`. Sorter port arrows (input, 3 outputs, overflow) on the ghost | Building B11 ghost, this GDD reasons | M2 |
| **Deconstruct warning** | "Buffer: N units / Lane: N units → spill bag" on hold-start over a loaded machine | Building B8 ring, this GDD text | M2 |
| **Toasts** | `belt_full` (once per 5 s island-wide), `console_busy`, `output_blocked` | HUD pipeline | M2 |

**Accessibility.**
- Plates and slot chips use glyph + stripe, never colour alone.
- Every console action is gamepad-reachable (Input R8).
- Route has a non-drag path: select an entry, then an output.
- Blocked states have an audible tick and a text reason.

> **📌 UX Flag — Belt Logistics**: This system has UI requirements. In Phase 4 (Pre-Production), run `/ux-design` for the **sorter panel** (with Map & Address UI's picker), the **blocked-machine badge**, and the **Push prompt's blocked text** before writing epics. Stories that reference UI should cite `design/ux/[screen].md`, not this GDD directly.

## Cross-References

| Document | Sections used | What this GDD relies on / changes | Status |
|---|---|---|---|
| `grid-inventory.md` | R1, R5, R7, R8, R10, R12, R13, R14, R19; OQ15–16 | `TakeUnit(selector)` / `PeekOutfeed` / `TryInsert`; Intake `arrivalSeq` outfeed; Presort per-street head index; `sorter_buffer` 4×2 under grid rules; chest `inserter_only`; BeltLeak WorldItems; player op beats inserter. **Amends** the ~400-leaks-per-tick edge case (BL15). **Answers** OQ16 (BL21) | In Review |
| `mail-destinations.md` | R3, R6, R12, F11, Visual/Audio feeder rows | feeder = construct on `approachTile`; `CanRegisterFeeder` / `OfferUnit`; 2 pieces/s; one feeder per mailbox; NPC truck transient feeder exempt (not a belt); no auto-jam. **Flags** F11's lost remainder (OQ-BL-H4) | In Review |
| `throwing-catching.md` | T6, OQ5, TC-X-5 | per-unit `TryInsert(lane, point, unit)`, partial accept, `flightId` on `LaneInsert`; rank 4 belt lane. **Answers** Throwing OQ5 | In Review |
| `player-character-movement.md` | P7, P10, P11, F5; OQ25 | rider carry from lane speed + push windows; 12 000 mm/s cap; Push and Configure anchors. **Answers** OQ25 (BL14) | In Review |
| `building-system.md` | B1–B4, B7, B8, B11, B15; OQ-BLD-7, OQ-BLD-10, OQ-BLD-15 | registry, `rot`, `OnPlaced` / `OnRemoving`, `IPlacementRule`, spill bag, `maxLineTiles` 40, belt HP 80. **Answers** OQ-BLD-7 (feeder rule; handover; in-place replace deferred; belts passable) and OQ-BLD-15 provisionally (BL-AC-59) | In Review (Provisional) |
| `simulation-core.md`, ADR-0003, ADR-0006, ADR-0008, ADR-0009 | R3 phase 5, R9, F3, F8, OQ9 | `Automation.Step` slice 1.20 ms incl. endpoint calls ≤ 0.25 ms per 400; integer math; 64 B events, 48 B payloads; `IInventoryApi` crossings. **Re-derives** OQ9 (Automation ≤ 320 events per tick; `systemEventsPerTick` ≈ 480) | In Review / Accepted |
| `content-data-definitions.md` | CD10, CD12, CD18, CD20 | stats `BeltSpeedMultPm`, `SorterFilterSlots`, `SorterThroughputMultPm`, `InserterRateMultPm`; RuleFlag `PresortOutfeed`; read-every-tick rule | In Review (Provisional) |
| `health-damage.md` | HD1, `EntityDowned` | constructs registered by Building; units on belts have no HP | Designed (Provisional) |
| `hud-notifications.md` | H-WIDGET-1, H-ATTR-3, feed belt-misroute merge | sorter counter and blocked badge (M2); machines named "Belt 3", never the builder | In Review |
| `game-pillars.md` | Pillar 3 ratified clarification | binding: machine value = unattended; raise the cost of presence, never weaken the manual action, not on solo | Ratified |
| spec 04 §2, 06 §4, 07 §8, 10, 11 §4 / §9, 12 M2, 13 §2–§3 / §6 | throughout | source values. **Amended here:** spec 04 §2.1 push "on that segment" → ±3-tile window; spec 04 "Mk3" → Mk2 + Express Lane; spec 06 §4 checksum → root + detail on exact positions, `positionAtTick` → tick tag; spec 07 §8 / SC-FIX-WORST "400 tiles, 5 000 items" → 700 tiles (BL-P1 fixtures); spec 11 §4 "sorter to 8 mailboxes" → ≥ 3 sorters. Spec files are not edited | — |

## Acceptance Criteria

*Validated by qa-lead 2026-10-16 (NEEDS FIXES → fixes applied: 9 added, 20 rewritten, numbers corrected).*

**Fixtures.** Seeded and Sim-only (xUnit, `dotnet test`) unless tagged. Tiles in a segment are indexed from 0; tile k covers `[2000k, 2000k + 2000)`.
- **FX-BL** (M-1b): the hand-authored street. The PO Intake has 2 `outfeedLaneTiles` (A, B). 10 houses on "Elm St" (numbers 1–10, district 1).
- Constructs are System-`Spawn`ed in tick 0 (Spawn bypasses `IPlacementRule`):
  - a 12-tile Mk1 belt from outfeed tile A to sorter S (rot 0) at its SW input;
  - S filters: Front = {Elm, 1–1}, Left = {Elm, 2–2}, Right unset;
  - Front → a 6-tile belt → an inserter on mailbox 1's `approachTile`;
  - Left → a 6-tile belt ending on mailbox 2's `approachTile` (belt feeder);
  - Overflow → a 4-tile belt ending on open ground.
- Delivery phase OPEN, wallet 0, stats at base.
- **Stub Mail** applies F11 (one insert per 15 ticks per feeder) and returns Accept unless a test scripts another outcome.

Evidence for [UI] / [MANUAL] goes to `production/qa/evidence/belt-logistics/` with lead sign-off.

### Lanes and advance (BL3–BL6, F1, F2)
- **BL-AC-1 [Logic] [M-1b] (F1)** GIVEN one unit at 250 on an empty 12-tile Mk1 segment, WHEN 30 ticks run from T = 0, THEN per-tick advances are 66, 67, 67 repeating and the position is 2 250. Mk2 gives 133, 133, 134 repeating. `BeltSpeedMultPm` 1500 on Mk1 gives 100 every tick. GIVEN the stat changes to 1500 and is first read by the Step at T = 7, THEN after 30 ticks the position is 250 + 466 + 2 300 = 3 016.
- **BL-AC-2 [Logic] [M-1b] (BL5)** GIVEN a segment whose exit refuses, WHEN 40 units are pulled in over time, THEN the head stops at `L − 250`, neighbours sit exactly 500 apart behind it, no unit passes the gate, order is unchanged, and ≤ 4 units per tile per lane. WHEN the exit then accepts every tick, THEN ≤ 1 unit per lane hands off per tick.
- **BL-AC-3 [Logic] [M-1b] (BL3)** GIVEN tiles (0,0)–(2,0) east, a curve at (3,0), then (3,1)–(3,2) north, THEN the segment is 12 000 mm in both lanes and tile (3,0) is classified `curve`. A unit placed in the left lane at 250 reaches 11 750 still in the left lane.
- **BL-AC-4 [Logic] [M2] (BL6)** GIVEN belt B feeding the side of belt A, THEN B's units enter A's near lane at the tile midpoint, alternating B's source lanes, and only when no A unit is within 500 mm. With A saturated, B backs up and A's flow is unchanged.
- **BL-AC-5 [Logic] [M-1b] (BL4)** GIVEN a 12-tile segment whose left lane holds 20 units at 7 250, 7 750 … 16 750, WHEN tile 5 (10 000–12 000) is deconstructed in phase 0, THEN its 4 units go to one spill bag. The 6 upstream units keep their positions, with the head at 9 750 facing a leak end. The 10 downstream units sit at 250 … 4 750. Lanes + bag = 20. WHEN the tile is re-placed, THEN one segment results, with downstream units back at 12 250 … 16 750.
- **BL-AC-6 [Logic] [M-1b] (BL4, BL6)** GIVEN Mk1 tiles 0–2, Mk2 tiles 3–5, and an inserter beside tile 1, THEN there are 2 segments split at the tier change and the inserter adds none. Placing a belt elsewhere keeps both segment ids. Lanes are preserved across belt → belt.
- **BL-AC-7 [Logic] [M-1b] (F2)** GIVEN a saturated, unblocked lane, WHEN 300 ticks run, THEN head hand-offs per lane are 40 (Mk1), 80 (Mk2) and 120 (Mk2 + Express Lane), ± 1.

### Endpoints, feeders, retry (BL7, BL8)
- **BL-AC-8 [Logic] (BL7)** Parametrised, GIVEN a head at the gate facing each target:
  - **[M-1b]** chest, PO Intake, a sorter side other than input, a wall, the map edge → dead end: one Grid call at most, no WorldItem;
  - **[M-1b]** open ground → leak;
  - **[M-1b]** a splitter / sorter output port or an inserter facing nothing → dead end, never a leak;
  - **[M2]** depot / PO Depot → `TryInsert`, unit PLACED (merged).

  Grid R19's tick check passes in every case.
- **BL-AC-9 [Logic] [M-1b] (retry)** GIVEN S's buffer full with 8 distinct addresses and Front blocked, WHEN 100 ticks pass, THEN the input head made exactly 1 `TryInsert`. WHEN a test removes one buffer entry via `IInventoryApi` (version + 1), THEN the head retries on the next Step and succeeds.
- **BL-AC-10 [Integration] [M-1b] (BL8)** In FX-BL, mailbox 2's belt feeder offers via `OfferUnit` with its construct id. Accept and Misdeliver consume the unit (CONSUMED). WHEN the stub returns `closed`, THEN the unit stays at the gate, the belt backs up and nothing is lost. At OPEN the flush resumes at one unit per 15 ticks.
- **BL-AC-11 [Logic] [M2] (BL8 rule)** GIVEN a feeder on mailbox 3's `approachTile`, WHEN a second construct is placed facing mailbox 3 from that tile, THEN the placement is refused `feeder_slot_taken` and nothing is charged. A long inserter whose drop tile is a `mailboxTile` from 2 tiles out → `feeder_not_on_approach`. Deconstructing the first feeder frees the slot in the same tick. Rotating a feeder away and back deregisters, then re-registers.

### Splitter and merger (BL9)
- **BL-AC-12 [Logic] [M2]** GIVEN an unfiltered splitter (pointer starts Front, order Front, Left, Right) with 3 free outputs, WHEN 9 units pass, THEN outputs receive F, L, R, F, L, R, F, L, R. WHEN Right is blocked, THEN F, L, F, L … Lanes are preserved.
- **BL-AC-13 [Logic] [M2]** GIVEN Left filtered `kind = medium` and Front/Right unfiltered, THEN every Medium goes Left (waiting while Left is blocked, never Front) and letters go round-robin Front/Right.
- **BL-AC-14 [Logic] [M2]** GIVEN a merger (pointer starts Back, order Back, Left, Right) with units on 3 inputs, THEN it takes B, L, R, B …, skips empty inputs, preserves lanes, and never reorders within an input lane.

### Address Sorter (BL10, BL11)
- **BL-AC-15 [Logic] [M-1b; 1 000-unit gate M2] (first match, spec 12)**
  - **[M-1b]** FX-BL routes Elm 1 Front, Elm 2 Left, and all others to Overflow.
  - **[M2]** GIVEN Front = {Elm St}, Left = {Oak Row, 1–9}, Right = {kind = large}, WHEN 1 000 seeded units (mixed streets, numbers 1–20, all kinds) pass with free outputs, THEN 0 units go to a non-matching output, Overflow receives exactly the units matching no slot, and a Large on Elm goes Front.
- **BL-AC-16 [Logic] [M-1b] (filters)** An all-wildcard slot matches nothing. A unit matching two slots of one output is routed once. With `SorterFilterSlots` 3, an output with slots {Elm}, {Oak 1–4} matches both. [M2] `ConfigureSorter` rejects a `numberRange` without `street`, and a locked district.
- **BL-AC-17 [Logic] [M-1b] (ports)** For rot 0–3, input, Overflow and the Front/Left/Right exits are at the BL10 cells rotated clockwise. With no filters, every unit goes to Overflow. With no belt on an output, that output is a dead end (no leak) and the sorter is STALLED on it.
- **BL-AC-18 [Logic] [M-1b] (rate, F3)** GIVEN a full buffer and free outputs, THEN exactly one route every 15 ticks. At `SorterThroughputMultPm` 2000, intervals alternate 7 and 8 ticks, 4 per 30 ticks; at 500, one per 30. Sorter outputs alternate lanes L, R, L … starting L.
- **BL-AC-19 [Logic] [M-1b] (buffer)** GIVEN an empty buffer, WHEN letters for 9 distinct addresses arrive, THEN the 9th is refused and the input backs up; a letter for a buffered address merges. GIVEN 1 Medium buffered, a Large waits, then enters on the tick the buffer empties (route-before-accept).
- **BL-AC-20 [Logic] [M-1b] (stall)** GIVEN Front blocked and the buffer's lowest mailId matching Front, THEN the sorter is STALLED, routes nothing (not even Left matches), and never sends that unit to Overflow. WHEN the filters are edited so it matches Left, THEN it routes Left at the next credit.
- **BL-AC-21 [Integration] [M2] (Route)** GIVEN an operator with the console open, the sorter STALLED on a 6-letter entry, and Overflow free, WHEN they Route it to Overflow, THEN:
  - the 6 units leave lowest mailId first as 3 L/R pairs ≥ 500 mm apart, all gone within 20 ticks (auto-examine would need 75);
  - the sorter returns to RUNNING;
  - the order ends when the entry is empty.

  A second opener gets `console_busy` for Route but sees the panel. Walking past 3.0 m ends an order.
- **BL-AC-22 [Integration] [M2] (no remote relabel)** The client has exactly one `ConfigureSorter` call site (SorterPanel). The panel opens at 2 500 mm and not at 2 501.
- **BL-AC-23 [Logic] [M2] (Halt)** GIVEN Halt on, THEN no auto-routes occur, the buffer still fills, and Route still works. Halt off resumes.

### Inserter (BL12)
- **BL-AC-24 [Logic] [M-1b]** GIVEN an inserter between a stocked chest and a free belt, THEN:
  - it picks every 24 ticks;
  - it places each unit 6 ticks after its pick, on the lane farther from it;
  - it holds the unit on the arm meanwhile (BELT in the ledger).

  At `InserterRateMultPm` 4000 it cycles every 6 ticks (5/s); 4001 is clamped to 4000. An inline inserter with both entry slots free uses L.
- **BL-AC-25 [Logic] [M-1b] (pick rules, BL2)** A belt pick takes the unit nearest the tile exit, ties L. A chest holding a Log and a letter yields only the letter. [M2] A long inserter picks and places 2 tiles away on the same 24-tick cycle.
- **BL-AC-26 [Logic] [M-1b] (race)** GIVEN a player `quick_move` and the inserter both targeting the chest's last unit on tick T, THEN the player gets it, and the inserter gets `no_unit` and picks on T + 1 if anything remains, with its credit unspent.
- **BL-AC-27 [Logic] [M-1b] (feeder)** GIVEN mailbox 1's inserter and the stub returning `wallet_floor` for ticks 6–44, then Accept, THEN the unit stays on the arm (BELT) throughout, no new pick occurs, and it is Accepted on tick 45.
- **BL-AC-28 [Logic] [M-1b] (F3 no burst)** GIVEN an inserter blocked for 100 ticks, WHEN freed, THEN it acts on the first free tick and next acts exactly 24 ticks later.
- **BL-AC-29 [Logic] [M2] (filter)** A `kind = letter` inserter over a belt takes only letters; others pass under it. Picking from a container (until `Selector.Kind` exists) it takes the `Fifo` head only if it is a letter, else waits.

### Intake outfeed, Presort, throughput (BL13, F4)
- **BL-AC-30 [Logic] [M-1b]** GIVEN 50 units in the Intake, THEN:
  - a belt on outfeed tile A pulls 1 per 30 ticks in ascending `arrivalSeq`, alternating lanes;
  - an inserter on tile B pulls 1 per 24 ticks;
  - a unit returned to the Intake mid-run leaves after all earlier arrivals.
- **BL-AC-31 [Logic] [M-1b] (F4)** In FX-BL with an inserter on tile B (tile A empty) and 200 units supplied, exactly 75 units pass it in 1 800 ticks (acts at ticks 0, 24 … 1 776). Neither the sorter nor the feeders limit the line.
- **BL-AC-32 [Logic] [M4] (Presort)** GIVEN `PresortOutfeed` on, Elm mailIds {4, 7, 9, 12, 15}, Oak {1, 2, 3, 5, 6, 8, 10, 11, 13, 14, 16–25}, Ash {26–28}, THEN one pull point takes Oak ×12 (1 … 17), Elm ×5, Oak ×8, Ash ×3. Toggling the flag after 5 pulls restarts at the lowest remaining mailId's street.

### Push (BL14, F5)
- **BL-AC-33 [Logic] [M-1b] (window)** GIVEN a 20-tile Mk1 segment and a push anchored at tile 10, THEN:
  - units whose centres lie in [14 000, 28 000) at tick start advance at 3 000 mm/s, and all others at 2 000 (13 999 is outside, 14 000 inside, 27 999 inside, 28 000 outside);
  - a unit crossing the window takes 140 ticks pushed vs 210 unpushed;
  - a second pusher at tile 12 extends the window to tiles 7–15 at 3 000, not 4 500;
  - an anchor at tile 1 clips the window to tiles 0–4;
  - one player cannot hold two pushes;
  - units/s leaving the segment is unchanged.
- **BL-AC-34 [Logic] [M-1b] (validity, parametrised)** The push ends, raising exactly one `PushEnd`, on: release; knockdown; mount; swim; disconnect; the host chest point at 3 001 mm from the anchor (3 000 stays valid; `rangeMm` 2500 + 500); the tile's removal; and, in toggle mode, 180 ticks. After a recompile that keeps the anchor tile, the window re-anchors to its new index.
- **BL-AC-35 [Logic] [M-1b] (blocked)** GIVEN a BLOCKED segment, WHEN Push starts, THEN no unit moves.
- **BL-AC-36 [UI] [M2] (blocked prompt)** GIVEN a feeder REFUSED at "9 Jade Close", WHEN Push starts on its segment, THEN the prompt reads "Held: 9 Jade Close refused" and the strain pose plays (screenshot evidence).
- **BL-AC-37 [Integration] [M-1b] (rider)** GIVEN a carrier in a push window on Mk2 + Express Lane, THEN carry is 9 000 mm/s, and carry + own velocity never exceeds 12 000 mm/s (Player Character P7). A pusher standing in their own window gets 6 000 mm/s.

### Leaks, throws, cap (BL15–BL17)
- **BL-AC-38 [Logic] [M-1b] (leak)** GIVEN an end with construct id ≡ 4 (mod 15) facing open ground and a saturated lane, THEN it emits BeltLeak WorldItems at the front tile centre on ticks 4, 19, 34, 49, 64, 79, then pauses. A pile picked up in phase 0 lets the end emit again at its next stagger tick. With 11 leaking ends, live BeltLeak items never exceed 64 and every end pauses once 64 is reached. No RNG is drawn.
- **BL-AC-39 [Logic] [M-1b] (leak conservation)** GIVEN a leak pile left 300 s, THEN its mail returns to the back of the Intake queue (Grid R12). A leak into water floats 10 s, then returns the same way. The R19 counters balance every tick.
- **BL-AC-40 [Integration] [M-1b] (Throwing TC-X-5)** GIVEN 20 units thrown to land at 5 750 on an empty lane, THEN 12 insert at 5 750, 5 250 … 250, oldest first, with each `LaneInsert` carrying the `flightId`. The remaining 8 become one WorldItem, and no step crosses the segment start. A landing 1 000 mm from the nearest free slot snaps; 1 001 mm is refused. Non-mail passes through.
- **BL-AC-41 [Logic] [M-1b] (cap)** GIVEN `beltItemCap` reduced to 100 with 99 units on lanes and 1 on an arm, THEN:
  - Intake pulls, container picks and sorter routes return `belt_full` and the sources are unchanged;
  - lane-to-lane hops and a belt-to-arm pick still succeed;
  - a thrown 5-stack inserts 0 with `belt_full`.

  With 98 on belts the thrown 5-stack inserts 2 and the rest becomes a WorldItem.

### Removal, order, determinism (BL18–BL20)
- **BL-AC-42 [Logic] [M-1b] (removal)** GIVEN a sorter with 5 buffered units, an inserter holding 1, and a belt tile holding 3, WHEN each is deconstructed, THEN:
  - each produces exactly one spill bag holding exactly those units;
  - any Route order ends and the console closes;
  - feeders deregister in the same tick.
- **BL-AC-43 [Logic] [M3] (destruction)** GIVEN `EntityDowned{Construct}` for a loaded belt in phase 10, THEN its units are handed over by the end of phase 11 and none advances on the next tick.
- **BL-AC-44 [Logic] [M-1b] (order)** In one tick:
  - two inserters placing onto one lane entry slot → the lower id places, the other retries next tick with its credit unspent;
  - a step-4 sorter route beats a step-5 inserter place for one slot;
  - a phase-4 thrown landing beats both;
  - a unit pulled in step 6 first advances on the next tick.

  Spawning the same constructs in two orders with explicit ids gives identical state hashes.
- **BL-AC-45 [Logic] [M-1b] (determinism)** GIVEN FX-BL and a recorded op log, WHEN replayed twice and across a dump/restore at tick 900, THEN state hashes match every tick. The hash covers lane positions, segment table, arms, credits, sorter filters, `rr` pointers, Halt flags, Route orders, push windows, leak counts and wake-on-version state.
- **BL-AC-46 [Logic] [M-1b] (conservation fuzz)** GIVEN 18 000 ticks of random belt placements and removals, throws onto lanes, inserter races, sorter edits, pushes and leaks, THEN Grid R19 holds every tick and every mailId is in exactly one location state.
- **BL-AC-47 [Logic] [M2] (ring)** GIVEN a saturated 8-tile ring, THEN it stalls with the lowest id as the cut, nothing is lost or reordered, and the hash is stable for 600 ticks.

### Pillar 3 and fantasy (BL21, Player Fantasy)
- **BL-AC-48 [Integration] [M2] (telemetry)** Telemetry records per shift:
  - `sorterShare`;
  - hand sorts by container class;
  - feeder Accepts with the nearest carrier's distance;
  - carrier distance per Delivery minute;
  - stall → nearest-carrier time;
  - Route clear time.

  The BL21 trigger fires at 29 % and not at 30 %. It excludes n = 1, and with 5 sessions in a bucket it reports NOT ASSESSED.
- **BL-AC-49 [MANUAL] [M2] (fantasy tests)** In M2 playtests (≥ 6 sessions with n ≥ 2, plus ≥ 3 solo), the following hold, or the failure is logged against OQ-BL-1 / OQ-BL-H1 with the BL21 response named:
  - ≥ 60 % of feeder Accepts happen with no carrier within 20 m;
  - median carrier distance per Delivery minute in shift 5 is ≥ 70 % of shift 2;
  - a carrier reaches a stall within 30 s in ≥ 60 % of cases, and Route clears it in ≤ 10 s;
  - ≥ 4 of 5 testers correctly identify which belt is being pushed.

### Network (M0 unless tagged)
- **BL-AC-50 [Integration] [M-1b] (events)** In loopback, every unit entering, crossing or leaving a lane raises exactly one `LaneInsert`, `LaneTransfer` or `LaneRemove`, in BL20 order. `ConstructRemoved` precedes `LaneRemove{Removed}` and `SegmentMap` in one handler. Every payload is ≤ 48 B; `SegmentMap` chunks at ≤ 7 entries per entry (ADR-0008 continuation).
- **BL-AC-51 [Integration] (visual sim)** GIVEN a client fed the host's events with 200 ms delay, THEN at every RenderTick its lane state equals the host's at that tick, with bit-exact positions. No unit's drawn centre exceeds `L − 350` mm on an endpoint segment before its `LaneRemove` / `LaneTransfer` applies.
- **BL-AC-52 [Integration] (resync)** GIVEN one segment's client state corrupted, THEN the next root checksum mismatches, `BeltDetailRequest` identifies only that segment, one `LaneState` restores it, and requests respect 1 per segment per 5 s.
- **BL-AC-53 [Integration] [M2] (spec 12 gate)** GIVEN a 30-segment factory for 10 min under 100 ms RTT and 2 % loss (netem), THEN `LaneState` resends are ≤ 1 per segment per minute and the render-clamp assertion never fires.
- **BL-AC-54 [Integration] (JoinState)** GIVEN 5 000 units on 300 segments with 24 sorters (configs per output), THEN the belt block is ≤ 30 KB and fits one chunk, and a joiner's first root checksum matches. Typical (30 segments, 300 units) ≤ 2.5 KB.
- **BL-AC-55 [PERF] [M2] (bandwidth)** GIVEN the 30-segment typical factory at 6 units/s, THEN the belt down-rate per client is ≤ 2.5 kbps (10 s mean). GIVEN the shift-5 fixture at 26 units/s, THEN ≤ 10 kbps. Wire bytes are counted deterministically in CI.
- **BL-AC-56 [Integration] (riders, push)** GIVEN a rider on a client and a push started by another player, THEN the rider's carry switches on the `PushStart` tick, and the host's consistency check never rejects a rider within `carryGraceTicks`.

### Performance
*Method: Release, warm-up 300 ticks discarded, one Stopwatch read per phase per tick. ms gates are ADVISORY until the reference machine exists; counts, bytes and allocation are BLOCKING in CI.*

**BL-P1 fixtures.**
- **SC-FIX-WORST belts:** 700 belt tiles (Mk1 500, Mk2 200), 200 segments, 5 000 units, 50 feeders, 24 sorters, 40 inserters, 36 splitters/mergers, 50 source lanes, 8 pushes, 4 leak ends; 1 000 constructs in total (≈ 150 inert walls and ruins).
- **STEADY:** 400 tiles, 110 segments, 2 500 units.
- **Coincident tick:** every 30th tick, all feeders, sorters and inserters fire.

- **BL-AC-57 [PERF, BLOCKING counts] [M2]** GIVEN WORST for 18 000 ticks, THEN on every tick:
  - units visited ≤ units on belts;
  - head inspections ≤ 2 × segments, and non-head units inspected by any endpoint = 0;
  - Grid endpoint calls ≤ 400;
  - sorter filter compares ≤ 16 per examine;
  - automation events ≤ 320;
  - 0 B allocated after warm-up, including recompiles and leaks.
- **BL-AC-58 [PERF, ADVISORY] [M2]** GIVEN WORST, THEN `Automation.Step` is ≤ 0.50 ms median, ≤ 1.20 ms p99 and ≤ 2.5 ms max. The sub-budget allocates ≤ 1.04 ms: advance 0.15, heads 0.10, Grid calls 0.25, sorters 0.08, inserters 0.06, nodes 0.05, feeders 0.15, events 0.10, overhead 0.10. STEADY p99 ≤ 0.60 ms. Any sub-scope above 2× its CI baseline is BLOCKING.
- **BL-AC-59 [PERF] [M2] (scaling, OQ-BLD-15)** GIVEN WORST's graph at 1 250 / 2 500 / 5 000 units, THEN units visited scale exactly linearly (BLOCKING) and the 5 000 / 2 500 time ratio is ≤ 2.2 (ADVISORY). GIVEN +300 inert walls, THEN Automation counters are identical (BLOCKING) and time differs ≤ 5 %.
- **BL-AC-60 [PERF, client] [M2]** GIVEN a Release export on the reference device (OQ-BL-3) with 400 tiles and 3 200 units in view, WHEN a 600-frame orbit runs, THEN:
  - frame p99 ≤ 16.6 ms (ADVISORY);
  - BeltView CPU ≤ 1.0 ms p99 and belt GPU ≤ 1.5 ms (ADVISORY; GF7);
  - belt draw calls ≤ 40, instances ≤ `visualItemCap`, and 0 B managed allocation per frame (BLOCKING);
  - server belt heap ≤ 512 KB at `beltItemCap` (BLOCKING) and client belt memory ≤ 4 MB (ADVISORY).

### Presentation and UI
- **BL-AC-61 [UI] [M2]** Sorter plates: ≥ 90 % of 10 testers name each output's district/street from 10 m, in greyscale and in protan / deutan / tritan simulation. Reduced motion makes strips and rollers static arrows while units still move. Measured belt flashes are ≤ 3/s on screen in the WORST visual fixture.
- **BL-AC-62 [UI] [M-1b] (GF6)** The blocked X appears on a head tile after 2.0 s of refusal and clears ≤ 0.15 s after the exit accepts. It never appears during a 5-minute `feeder_holding` fixture.
- **BL-AC-63 [MANUAL] [M-1b] (GF2)** An inserter swing spans ≥ 120° over ≥ 6 frames at the base rate, and the unit is visible on the arm from the pick frame (≥ 120 fps capture).
- **BL-AC-64 [MANUAL] [M2] (GF1)** On a host, the Push pose and window band appear ≤ 33 ms after press, and units visibly accelerate ≤ 100 ms after press (≥ 120 fps capture).

## Open Questions

**Hand-backs (this GDD does not edit other GDDs).**

| ID | To | Item | By |
|---|---|---|---|
| OQ-BL-H1 | Mail & Destinations / Run Structure | OQ16 lever: `streetsPerBatch(n) = round(S1 × n^e)`, add knob `e` (default 0.5, range 0.5–1.0, +0.25 per M2 batch while the signal persists; n = 1 unchanged), fired only by BL21's signal. Also the symmetric "reason to move" response (more demand belts cannot serve; solo-only failure → World Generation lot geography). Never weaken Sort | before M2 playtests |
| OQ-BL-H2 | Grid Inventory | `Selector.Kind(k)` for filtered inserter picks from containers; state Presort's within-street order (lowest mailId by street index); confirm that `TakeUnit` on non-Intake containers is mail-only under `Fifo` | Grid targeted check |
| OQ-BL-H3 | Grid Inventory | replace the "~400 WorldItems in one tick" belt-leak edge case with BL15's bound; BeltLeak WorldItems carry `sourceConstructId` (per-end count query); Payday dead-letter sweep includes BELT (lanes, arms); a ledger crossing for FLIGHT → BELT (Throwing landings) in `IInventoryApi`; mark OQ16 answered by belt-logistics.md BL21 (lever owned by OQ-BL-H1) | Grid targeted check |
| OQ-BL-H4 | Mail & Destinations | F11 caps credit at 30000 and loses the remainder for rates that do not divide 30000 (1700, 4000), so M-F11-1b's "17/18 alternation" fails. Adopt F3's form `min(credit + rate, 30000 + rate − 1)` | Mail re-review |
| OQ-BL-H5 | Spec maintenance / Economy | spec 11 §4 "sorter to 8 mailboxes" needs ≥ 3 sorters; the reference factory cost conflict stays OQ-BLD-10 | Economy GDD |
| OQ-BL-H6 | Mail & Destinations / Run Structure | Payday roll-call names the machine ("By Belt 3") for feeder misdeliveries, never the last label editor (creative-director) | Run Structure GDD |
| OQ-BL-H7 | art-director | art-bible amendments A1–A5 (Visual/Audio) | `/art-bible` revision |
| OQ-BL-H8 | Networking & Session | adopt the belt event set, root + detail checksum, `LaneState` on the snapshot path ordered after events ≤ T, JoinState belt block, Belt share (≤ 2.5 kbps typical / ≤ 10 worst) | Networking GDD (M0) |
| OQ-BL-H9 | Player Character | P7 "2 / 4 / 6 by tier" → Mk1 / Mk2 plus Express Lane (no Mk3 construct); rider carry reads push windows from `PushStart` / `PushEnd`; host consistency tolerance `carryGraceTicks` (10) for belt carry | PC re-review |
| OQ-BL-H10 | Networking & Session / technical-director | the typical down-budget running total (≈ 35 kbps of 40 before enemies) is oversubscribed; re-baseline spec 06 §4.2 shares | before M0 |
| OQ-BL-H11 | Simulation Core / technical-director | SC-FIX-WORST belt counts → BL-P1 (700 tiles, 200 segments, 5 000 units, 1 000 constructs); STEADY 400 tiles / 2 500 units; Automation events ≤ 320 per tick → `systemEventsPerTick` ≈ 480 (F8: 64 × 4 + 1.5 × 480 = 976 ≤ 3 840); Automation sub-budget split (BL-P2) | perf-budget.md |
| OQ-BL-H12 | Enemy AI & Waves | belts are walkable and passable to enemies, and targetable (Wall Breaker 150 vs belts); low aggro priority proposed | Enemy AI GDD |
| OQ-BL-H13 | Vehicles & NPC Drivers | loading-face query `IsParkedAt(face) → cargoContainerId` (speed < 0.1 m/s), checked on every attempt; sorter output → truck loading face is BL21's unattended path. BL21's truck argument depends on this | Vehicles GDD |
| OQ-BL-H14 | Content & Data | BuildingDef params (belt `speedMmS`; sorter `throughputMilli`, `outputs` 4, `filterSlotsPerOutput`, `bufferGrid`; inserter `rateMilli`, `reach`, `filterable`; splitter/merger `ways` 3, `filterable`); `InserterRateMultPm` clamp ≤ 4000; validator rules in Tuning Knobs; `automation.*` keys | CDD re-review |

**Design questions.**

| ID | Question | Owner | By |
|---|---|---|---|
| OQ-BL-1 | Sorter blocked-output lookahead (route the next buffer entry around a stalled one) instead of strict stall? Playtest signal: stalls cleared by Route < 50 % within 30 s (fantasy test 3) | game-designer | M2 playtest |
| OQ-BL-2 | 5 000 `beltItemCap` vs real supply ≈ 1 000: keep the stress gate, or lower to 3 200 at 400 tiles? (technical-director) | technical-director | M2 perf run |
| OQ-BL-3 | Name the mid-range reference device for BL-P5 / GF7 (client gates stay advisory until then) | technical-director | M1 reference machine |
| OQ-BL-4 | Push feel: belt speed-up arrives after RTT + render delay on clients (loopback hides it). M0 feel check | ux / network-programmer | M0 |
| OQ-BL-5 | A `flags` (Cursed) sorter filter and a Cursed mark on belt units (currently not shown: lane events carry kind + district only) | creative-director / owner | before M4 (Cursed Mail stamp) |
| OQ-BL-6 | HUD belt population readout (hoards, dead-end storage) — needed? | ux-designer | `/ux-design` HUD |
| OQ-BL-7 | Same-class in-place replace (rotate, Mk1 → Mk2 without deconstruct): deferred past M2 unless rebuilds read as a chore | game-designer | M2 playtest |
| OQ-BL-8 | Belt dead-end storage vs Intake pressure (OQ15's instrument should count units on belts) | Mail / Run Structure | M1 telemetry |

**→ becomes ADR-0027** (implementation, not design):
1. lane storage and item record;
2. native position unit and wire width (u24 mm);
3. segment compilation, incremental recompile and `segId` identity;
4. phase-5 traversal (topological order, loop cut) and the wake-on-version endpoint protocol;
5. event schema, encodings and their fit to ADR-0008;
6. checksum hash and tiers;
7. `LaneState` / JoinState layout and channel ordering;
8. client visual sim (time domain, checkpoints, rewind, smoothing, render clamp);
9. push window piecewise advance;
10. `beltItemCap` back-pressure;
11. MultiMesh batching and far-LOD — engine spike: no Godot 4.7.1 rendering reference exists;
12. Automation perf probes and counters.

Spike list (M0): Godot 4.7.1 snapshot-vs-event channel ordering, head-of-line blocking at 2 % loss, and per-packet framing.
