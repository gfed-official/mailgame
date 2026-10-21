# Mail & Destinations

> **Status**: In Review — `/design-review` 2026-10-16 NEEDS REVISION (18 blocking); revision applied the same day (marked **[DR]**); a fresh re-review decides Approved
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 (7 items: resident reserved for misdelivery; Payday roll-call order/spread and naming; Lost Parcel row; Pillar 5 solo-margin OQ22; Payday adjustments as stamped ledger lines; resident never in distress; impossible Large-stack test fixtures)
> **Reconciled 2026-10-16 with sibling GDDs (hand-backs: 11 adopted, 2 adapted, 0 rejected)** — Manual Delivery OQ3, Throwing & Catching OQ1–2, HUD HN-4, Player Character OQ30, Simulation Core HB4; marked **[HB]**
> **Author**: user + agents (creative-director, game-designer, economy-designer, systems-designer, art-director, qa-lead)
> **Last Updated**: 2026-10-21 (owner ruling 1:21 PM PT: R11 Intake hand-off is one `Spawn` per same-key run; M-PERF-1d is an exact-count gate). 2026-10-16
> **Last Verified**: 2026-10-16
> **Implements Pillar**: Pillar 1 — The mail must flow; Pillar 2 — Friendslop surface, factory depth
> **Retrofit source**: design/spec/03-mail-inventory-economy.md §1–§2 (chapter 13 wins on conflict); design/spec/01 §2.2, §3; design/spec/11 §3, §5–§7; design/spec/08 §2.2–§2.4; design/spec/10 perks/stamps/events

## Summary

Mail & Destinations defines what every piece of mail is worth and where it is going: it spawns each shift's mail into the Post Office Intake (with a backlog when the Intake is full), keeps the server's table of addresses, values and deadlines, and owns every mailbox on the island together with the one rule that judges anything that reaches one — Reject, Accept or Misdeliver — whether it came by hand, by throw, by belt or by truck. For the player it is the verdict at the door: the right house pops its flag and pays, the wrong one wobbles and a resident in pyjamas holds your parcel, and the penalty is never softened while the complaint is capped per door so one bad throw is a gag, not a run-ender. It is the hub Pillar 1 flows into, and the place where hands, throws and machines are held to the same honest rule.

> **Quick reference** — Layer: `Feature` · Priority: `MVP (M-1a → M1)` · Key deps: `Grid Inventory & Containers, World Generation, Content & Data Definitions, Run Structure (caller), Economy (wallet port)`

## Overview

Mail & Destinations is the system that decides what the mail *is* and where it is *going*: it owns the mail kinds and their values, the per-shift spawner that fills the Post Office Intake, the server's mail table (every piece of mail's address, value, deadline and flags), the backlog queue for mail the Intake cannot hold, and every destination on the island — in v1, one mailbox per house — together with the rule that judges each piece of mail that reaches one. Every delivery path in the game ends here: a carrier's deliver hold, a thrown stack entering a mailbox, a belt end or inserter facing a mailbox, an NPC truck at a stop. Each of them asks the same question of the same pure function — *Reject, Accept or Misdeliver?* — and gets the same answer, which is why a sorter matters and why a bad throw is never softened.

For the player it is the moment of truth at the door. You read the town by colour, glyph and stripe; the prompt shows your mail's address beside the mailbox's with ✓ or ✗; at the right door the flag pops and the wallet ticks up, at the wrong one the house wobbles and a resident in pyjamas holds your parcel. Payment, penalty and complaint are computed here per piece of mail, in integer cents, and reported upward as events: Economy moves the money, Run Structure moves the complaint meter, HUD and Presentation play the result. Without this system nothing has an address, nothing pays, and Pillar 1 — *the mail must flow* — has nowhere to flow to.

*Design boundary:* this GDD specifies behaviour — kinds, values, spawn schedule, acceptance, payment, penalty and complaint deltas, backlog semantics, destination states. Grid placement, the drain and the Intake outfeed are Grid Inventory's; the ballistic and landing detection are Throwing & Catching's; the hold and prompt UX are Manual Delivery's; the wallet is Economy's; the complaint meter, quota and phase machine are Run Structure's; address assignment and lot geometry are World Generation's. Data structures and the replication adapter → become ADRs. **v1 scope (spec 13 §2.2): houses only** — Apartment Mail Rooms, PO Box Banks, Business Docks and Cargo are designed in spec 03 and deferred; the rules below stay data-driven so adding them adds data, not code. *All non-M-1a content in this GDD is provisional pending the M-1a two-friend greybox result (decision log 2026-10-16T18:07:09Z).*

## Player Fantasy

*Framing: direct — players actively feel this system. Shaped with creative-director; framing A **"Every Door Is a Verdict"** chosen over "The Town Keeps Score" (passive, NPC-sim drift) and "Postal Gunslinger" (belongs to Throwing; sidelines hand delivery). It owns the door end of the job; Grid Inventory's "Case Your Route" owns the bag.*

You read the town the way a carrier does, without a map. Tangerine triangles mean the Tangerine side of town. The deep stripe on the kerb says which street. The number on the door is the last word. The bag on your back is a promise, and every mailbox is where it gets kept or broken.

At the right door you hold, and the slot takes it with a clean *thunk*. The flag snaps up and the box gives a small, satisfied bounce. Nobody claps, because this is the job. Across the street a second mailbox waits, and you don't walk over. You throw. The arc goes green, the letters drop into the slot from six metres, and your teammate on the next road hears the flag pop.

At the wrong door the house tells you. It wobbles, the door opens, and a resident in striped pyjamas stands there holding your parcel. They look at it, then at you, for three seconds. Nobody explains and nobody forgives. A red arc mid-air can't be called back: twenty letters for Jade Close go through a letterbox on Violet Row, and the town knows who did it. Under the Cursed Mail stamp, a glowing skull-stamped letter in the wrong slot brings the rival courier's goons out of the hedges.

Alone, the route is a quiet craft: you know the town, your hands are fast, and every flag is yours. With eight players the town becomes a stage — flags pop in every street, someone's belt keeps feeding the wrong house, and at Payday the worst misdeliveries are read out by name, address and resident. That is the laugh you retell next run.

**Reference feelings.** *Paperboy* — the throw at a door as a judged act with a property that reacts: taken; the arcade score and property destruction: rejected (mailboxes are indestructible; residents are confused, never victims). *Untitled Goose Game* — the world reacts while the character stays straight-faced, so the joke is the consequence: taken; anarchy as the goal: rejected — here mischief is the failure state. *Overcooked* — shared, very visible failure that turns into Fellowship laughter: taken; failures that cascade far enough to end the run and turn laughter into blame: rejected (this is why complaint is capped per door, R9, while the money penalty is not).

**Pillars served.** Pillar 1 (*the mail must flow*): this is where flow is judged and paid. Pillar 2 (*friendslop surface, factory depth*): one honest acceptance rule for hands, throws, belts and trucks — "a bad throw is never softened" — under a goofy surface; the money penalty is per piece and uncapped. Pillar 3 (*hands beat machines, until they don't*): one hold delivers any number of matching pieces in 0.4 s, a feeder inserts two pieces a second; the machine's value is that it works unattended (ratified reading, `game-pillars.md`). Pillar 5 (*scales 1 to 8*): spawn and quota share `n^0.65`; complaint is capped per door so eight throwers do not multiply one gag into a run-ender.

### Feel & Comedy Feedback

*Required by the systems index (CD-SYSTEMS note). Voice: deadpan postal bureaucracy — carriers are earnest, the world supplies the slapstick. Red means you got it wrong; magenta means the rival company did it to you. Art bible principle 2: every outcome is a physical event in the world, never only a HUD number. Visual and audio specifics: Visual/Audio Requirements.*

| Outcome | World (see / hear) | HUD adds | Comedic beat |
|---|---|---|---|
| Correct hand delivery | Slot *thunk*, flag snaps up, box bounces, small bell | gain pill "+N¢", quota nudge | The straight man: competence |
| Correct throw | Green arc, clean drop, harder flag pop audible a street away | same pill (+ "×1.1" with Cannon Arm) | Trick shot, witnessed |
| Wrong house, hand (one piece) | House wobbles; pyjama resident holds the parcel for 3 s; a flat "Hm." | loss pill "−N¢", complaint envelope shakes once | You did that slowly, on purpose |
| Thrown stack, wrong house (whole stack) | Red arc committed; the stack bursts through the slot; hard wobble; resident buried in envelopes (three size tiers) | one loss pill "×20 −N¢", "form filed" | The pratfall, at full price |
| Late delivery | Flag rises slowly; a Chalk White clock stamp on the piece; no resident appears | pill with a Gold clock "LATE", never red | Grudging acceptance |
| Dead letter cleared | Flag stays down; the box coughs dust; a moth flies out | grey "0¢ — DEAD LETTER CLEARED", no complaint | A bureaucratic shrug; never punished |
| Mailbox closed (Prep, Payday, Draft) | Flag folded, hinged CLOSED plate; a hold gets a latch rattle; a throw bounces (white arc) | "Closed — opens at Delivery" | Posted hours |
| Locked-district mailbox | District-coloured tape X over the slot; throws bounce; at unlock the tape rips off | "Not on today's route" | Red tape, literally |
| Wallet-floor refusal | The slot snaps shut and spits the piece back; a neutral REFUSED stamp (no red — nothing was misdelivered) | wallet pulses at −5.00 | Too broke to be wrong |
| Belt misdelivering unattended | The house wobbles in rhythm; the resident reappears with a growing pile | clerk feed line "Belt 3 posted a letter for 14 Tangerine Lane to 9 Jade Close." | Sorcerer's Apprentice — and your reason to run over |
| Cursed misdelivery | Acid-green puff, skull stamp pops on the door, then goons out of the hedges | loss pill, then a magenta raid alert | Wrong door, and it bites back |
| Lost Parcel found | A ×3 tag glints under a floating Kraft balloon; pickup fanfare | marker within 30 m; "Lost Parcel" toast | Found property, back on the route |
| Payday roll-call | The ledger reads deadpan: "Letter for 14 Tangerine Lane. Delivered to 9 Jade Close. By Evan." — one pyjama snapshot per line (≤ 5 lines at 1.5 s each, then "+N more", viewable in the ledger), closing on one positive MVP line | the Payday screen itself; zero misdeliveries stamps "CLEAN SHEET" | The public reading of the minutes |

The resident appears only for misdelivery outcomes.

**Fantasy test** (fail any one and the system has broken its fantasy): (1) **Muted HUD** — with the HUD hidden and audio on, ≥ 5 observers each classify 20 recorded outcomes spanning all eight classes (correct hand, correct thrown, wrong, late, dead, closed, locked, refused) correctly ≥ 90 % of the time; (2) **Reading the town** — in ≥ 4 of 5 solo sessions a first-time player without Local Knowledge finds an assigned District-2 address within 30 s using only colour, glyph and stripe; (3) **Laugh, not blame** — from complaint 0, no single delivery op of any kind raises the meter by more than 20 (M-R9-1), and in ≥ 6 of 8 coded 8-player sessions (two raters) blame utterances at a thrown-stack misdelivery are fewer than laugh or callout utterances.

## Detailed Design

*Specialists consulted: game-designer and economy-designer (primary — no Gameplay routing row fits a mail-and-payment rule system, so the union of the Gameplay default and the Economy row is used), systems-designer (states, formulas), creative-director (fantasy, comedy rules). Decisions marked **[D]** were taken in autonomous mode by adopting the owning specialist's recommendation and are logged in `production/session-logs/decision-log.md`. Numbers marked **PROPOSED** are new to this GDD, not in the spec. **Amends** marks a deliberate change to a spec chapter or to `grid-inventory.md`; every amendment of the latter is handed back in Open Questions.*

### Core Rules

**R1 — Mail kinds (v1).** `MailKindDef` (spec 08 §2.2) is owned by this system.

| Kind | Footprint W×H | maxStack | baseValue ¢ | weightClass | complaintOnMisdelivery | unlockShift | acceptedBy | fallbackKind |
|---|---|---|---|---|---|---|---|---|
| postcard | 1×1 | 40 | 4 | light | 3 | 2 | house | — |
| letter | 1×1 | 20 | 8 | light | 5 | 1 | house | — |
| small | 1×2 | 1 | 30 | light | 8 | 1 | house | — |
| medium | 2×2 | 1 | 70 | medium | 12 | 1 | house | — |
| large | 2×4 | 1 | 160 | heavy | 16 | 2 | house | — |
| *cargo (deferred)* | 5×8 | 1 | 600 | bulk | 20 | 3 | business_dock | large |

All values are data (spec 11 §6–§7). `deadlineOffsetShifts = 0` for every v1 kind (cargo +1). `MailKindDef` gains **`fallbackKind`** **[D]**: when no destination of a kind's accepting types is unlocked, that kind's spawn share is added to its fallback (spec 03 §1.3's "Cargo share is redistributed to Large", made general). Footprints, `maxStack` and `weightClass` are read by Grid Inventory; this system reads `baseValue`, `complaintOnMisdelivery`, `unlockShift`, `acceptedBy`, `fallbackKind`.

**R2 — The mail table.** Every piece of mail is one row in a server-only table, created at spawn and never visible to clients:

| Field | Type | Meaning |
|---|---|---|
| `mailId` | uint32 | Allocated from the run's mail counter (Simulation Core) at the batch tick, in manifest order; never reused. Lower id = older (R6 of Grid Inventory relies on this). |
| `kind` | enum | R1 |
| `addressId` | AddressId | `districtId:streetId:number[:unit]` (World Generation, spec 02 §3.6) |
| `spawnValue` | uint16 ¢ | Frozen at spawn by Formula F1 |
| `spawnShift`, `deadlineShift` | uint8 | `deadlineShift = spawnShift + deadlineOffsetShifts` |
| `flags` | bitset | **Cursed** (Cursed Mail stamp), **Found** (special spawn, R15) **[D]**; Priority and Fragile reserved (deferred, spec 03 §1.2) |

`flags` is part of Grid Inventory's StackKey (its R3), so Cursed and Found mail never merge with plain mail to the same address — which keeps every unit of one stack and one spawn shift at an identical `spawnValue` (R17). A row is retired when its mail is consumed (R6); undelivered rows live until the run ends. Location is not stored here — Grid Inventory's conservation counters own "where".

**R3 — Destinations.** World Generation places one mailbox per residential lot and hands over `(lotId, addressId, mailboxTile, faceNormal, anchor, approachTile, truckStopPoint)` — anchor = mailbox face + 0.3 m along `faceNormal` at 1.0 m height; `approachTile` a buildable tile directly in front (a feeder's end); `truckStopPoint` on the street edge **[DR]**. World Generation guarantees same-side mailboxes ≥ 6 m apart and opposite-side ones staggered ≥ 3 m along the street (else two anchors sit inside one 2.5 m `rangeMm` and the prompt flips); ContentValidator checks both per lot (hand-back, OQ15). This system builds the **destination registry** from it at run start: `destinationId → (addressId, typeId, districtId, interaction anchor, catch volume, feeder slot)`, plus the reverse index `addressId → destinationId`. `DestinationTypeDef` (spec 08 §2.4) is owned here:

| Field | house (v1) | Meaning |
|---|---|---|
| `accepts` | postcard, letter, small, medium, large | kinds this type takes (`MailKindDef.acceptedBy` is validated against it) |
| `insertRate` | 2.0 /s | automated feeder rate (F11) |
| `manualInsertHold` | 0.4 s | the **base** deliver hold length; the effective hold (ticks) is Input & Controls' one shared `holdEff` (F1), with Quick Hands `insertPm` 500 on every deliver hold (Manual Delivery MD9) **[HB]** |
| `maxAutomatedFeeders` | 1 | placed feeders per destination (R12) |
| `requiresUnit` | false | apartment / PO box only (deferred) |
| `catchVolume` **PROPOSED** | box 1.0 m wide × 0.8 m tall × 1.0 m deep, centred 1.0 m above the mailbox base | the volume a thrown entry must enter to count as landing in this mailbox (Throwing & Catching tests it); deliberately generous — the arc colour already tells you the outcome |

Every house has exactly one mailbox; mailboxes are indestructible and never targeted by enemies (spec 03 §2.1). The interaction anchor is R3's `anchor`; whether a carrier is in range is Player Character's interaction range (`rangeMm`), not a number owned here. Capacity is unlimited: mail vanishes on insert.

**R4 — Destination state.** A destination is **LOCKED** while its district is not unlocked, **CLOSED** while unlocked and the phase is not Delivery or Raid, and **OPEN** otherwise. Run Structure drives both inputs (orchestrator rule): `Mail.UnlockDistrict(d)` at the Prep that unlocks `d` (districts never re-lock within a run; District 1 starts unlocked), and `Mail.SetDeliveriesOpen(bool)` — true exactly from Delivery start to the end of Delivery/Raid, false in Generating, Prep, Payday, Draft and Results **[D]** (spec 01's phase table excludes delivery from Prep and Payday). The flag for tick T is the value Run Structure set in tick T − 1 (its state machine runs last in a tick, spec 06), so it is fixed before `Inputs.Apply` and a tick is wholly open or wholly closed; deliveries close when the Delivery timer ends (the Raid window ends with it, spec 01). **Closing grace [DR, CD ruling]:** an action is judged by when it **started** — a hand op whose hold began (`holdStartTick` carried in the request, in the host tick domain, clamped by Manual Delivery MD6 **[HB]**) or a throw released (`releaseTick`, passed to `ResolveThrownLanding`, R13) while OPEN resolves as OPEN if it applies ≤ `closeGraceTicks` (90) after close; the consume routine passes `Evaluate` that start state. Feeders and NPC drops get no grace. The whole town exists from generation (spec 02 §2), so a locked mailbox is physically present — taped shut — and a thrown item bounces off it.

**R5 — Acceptance: `Evaluate(destination, unit) → Reject(reason) | Accept | Misdeliver`.** A **pure function** of the destination's type, address and state and the unit's category, kind and address — no wallet, no randomness — so the client runs the **same code** for the deliver prompt's ✓/✗ and the throw arc's colour. Checked in this order (first match wins):

1. destination CLOSED → **Reject `closed`** ("Closed — opens at Delivery").
2. destination LOCKED → **Reject `not_on_route`** ("Not on today's route") **[D]** — mail is never addressed to a locked house, so treating it as a misdelivery would only create a penalty-priced dump; the tape is the physical tell.
3. unit is not mail (a tool, a log) → **Reject `wont_fit`** ("Won't fit here").
4. unit's kind not in the type's `accepts` → **Reject `wont_fit`**. Dormant for houses in v1 (they accept every v1 kind); live for the deferred types.
5. `unit.addressId == destination.addressId` (including the unit number where `requiresUnit`) → **Accept**.
6. otherwise → **Misdeliver**.

Because every unit of one entry shares kind, address and flags, an entry evaluates to one outcome; only payment varies per unit (timeliness). Reject changes nothing anywhere. The wallet floor is **not** part of `Evaluate`: it is a guard on consumption (R8), so the prompt still shows ✗ honestly when a misdelivery would be refused.

**R6 — Consumption (deliver orchestration).** Every delivery path calls one consume routine, oldest unit first (lowest `mailId`, Grid Inventory R6); one call on **one entry** is one **delivery op** at one destination and produces one `DeliveryOutcome` event (R16) — a `deliver_matching` hold over four matching entries is four ops in that hold's order, applied in one tick with contiguous `outcomeSeq` (one request, nothing interleaves) **[HB]**:

| Path | Called by | Accept | Misdeliver | Reject |
|---|---|---|---|---|
| `deliver {entryId}` / `deliver_matching {mailboxId, selectedEntryId?}` (hand) | Grid Inventory's op handler, in `Inputs.Apply` | whole entry, every match (`deliver_matching` never misdelivers — Grid Inventory R7) | **exactly one unit** per op (user decision OQ4) | nothing changes; `deliver_matching` counts the entry as not got |
| thrown landing (descending entry) in a catch volume | Throwing & Catching (`ResolveThrownLanding`, R13) | whole entry | **whole stack**, unit by unit (OQ4) | the whole entry becomes a WorldItem at the landing point (bounce) |
| placed feeder (belt end, inserter) | Belt Logistics (`OfferUnit`, R12) | one unit | one unit | the unit stays on the feeder |
| NPC truck at a stop | Vehicles & NPC Drivers (`OfferUnit` as a transient feeder, R12) | one unit | one unit | the unit stays in the cargo |

Per consumed unit, in order: compute payment (F6) or penalty (F7); for a Misdeliver apply the wallet guard (F8) — if it fails, **stop**: that unit and everything after it in this op stay where they were (hand, feeder) or become one WorldItem at the mailbox (thrown remainder, Grid Inventory R12); then retire the unit through Grid Inventory's `Consume`, credit or debit through the wallet port (R7), and record complaint (R9). A `wallet_floor` stop raises no complaint and is **not** a misdelivery for any statistic. **Tick order** **[D]**: hand ops apply in `Inputs.Apply` in arrival order (Grid Inventory R7's one FIFO; Simulation Core's `opSeq` **[HB]**); thrown landings resolve in Throwing's step; feeder and NPC offers resolve in the Belt / Vehicle step — all after `Inputs.Apply` and in that order, so within one tick player hand ops precede landings, which precede automation (placed feeders before NPC drops; within each class by ascending construct / vehicle id). Two players holding at one mailbox in one tick resolve sequentially and both succeed against their own panels (capacity is unlimited; there is nothing to contend for). Lateness is read **at the apply tick**; phase per R4's closing grace — a hold begun or a throw released after close, or one applying > 90 ticks after close, gets `closed` / bounces.

**R7 — Payment.** Each accepted unit pays `pay(unit)` (F6) — spawn value × timeliness × pay modifiers, rounded once per unit. Timeliness (F5): on time 100 %, one shift late `lateValueRatio` (50 %), `deadLetterShifts` (2) or more late 0 % — a **dead letter** pays nothing but still clears. Pay modifiers owned and applied here: Stamp Collector (letter and postcard ×1.25, team), Cannon Arm (×1.1 on a **thrown** Accept, read from the **thrower's** personal stats), Grace Period (every late band, 1 ≤ L < `deadLetterShifts`, → 100 %). **Not** applied here **[D]**: the Postal Inspector's −10 %, Neighbourhood Watch's +25 % and the zero-misdelivery bonus +10 % are shift-level adjustments that Run Structure applies at Payday to the shift total (per-unit application would round a 4 ¢ postcard back to 4 ¢ and make per-unit and total disagree). Zoning Board's "+15 % spawn value" scales the spawn budget `V` that Run Structure supplies (more mail), not the per-unit value. **Wallet access without a cycle** **[D]**: Economy depends on this system, so this system declares the port `IWalletGate { bool TryDebitPenalty(int cents, int floor); void Credit(int cents, CreditReason r); }`, which Economy implements and registers at run start; both calls apply in the consuming tick, so a penalty and its floor check are atomic.

**R8 — Penalty and the wallet floor.** Each misdelivered unit costs `penalty(unit)` (F7) = `misdeliveryPenaltyRatio` (50 %) of its **spawn value** — not the timeliness-adjusted value, so a dead letter cannot be dumped in any mailbox for free — ×0.5 with Fragile Handling, minimum 1 ¢. It is consumed only if `wallet − penalty ≥ walletFloor` (−500 ¢; a penalty landing **exactly** on −500 is allowed — Grid Inventory OQ13, **confirmed here**); otherwise `wallet_floor` (F8) and R6's stop rule applies. Money is per unit and **never capped**: a thrown 20-letter stack costs all 20 penalties until the floor (Pillar 2 — the bad throw is never softened). The wallet itself, purchases and the floor's value are Economy's.

**R9 — Complaint deltas (per door, not per envelope).** **Amends** spec 01 §3.3 / spec 11 §7 (per misdelivery, uncapped) and `grid-inventory.md`'s edge case "20 penalties and 20 complaint increments" **[D]**. This system computes complaint *deltas* and reports them to Run Structure, which owns the meter, its decay, thresholds and the backlog rate.
- **Misdelivery:** each misdelivered unit has a raw complaint `complaintOnMisdelivery` (R1). Every destination has a **door bucket** (a token bucket, F9) holding at most `doorComplaintCap` = 20 raw complaint and refilling 20 per `doorComplaintWindowSeconds` = 30 s **PROPOSED**; the raw complaint actually raised by an op is the bucket's allowance, further limited by one **team bucket** **[DR, CD ruling]** holding `teamComplaintCap` = RH(40 × playerScale) raw (40 solo, 98 at 4, 155 at 8; `playerScale = n^0.65`, from `SpawnProfile.playerCount`, locked at Delivery start) and refilling the same amount per 30 s, then Fragile Handling's ×0.5 is applied to what was raised. The team bucket closes the many-doors hole (former OQ12): eight throwers at eight doors raise ≤ 155, not 160, and solo two stacks at two doors ≤ 40; scaling the meter's decay and thresholds by `playerScale` is Run Structure's (OQ6). This per-door cap is a creative-director ruling, ratified by the owner 2026-10-16: Pillar 2's "never softened" binds the acceptance rule and the money, both still literal (decision log). One thrown 20-letter stack → +20 (not +100); a careless carrier misdelivering one letter per hold → +5 per hold (the cap never binds); a belt misrouting 2 pieces/s into one door → ≈ +40 in the first 30 s (the full bucket's 20 plus 20 refilled), then ≈ +20 per 30 s. *Why:* one resident files one complaint form; spec-literal per-unit complaint makes one thrown stack +100, which maxes the meter, schedules the Inspector and doubles the next raid — one friend's throw would decide the run (Overcooked "blame" failure; Pillar 5, since eight players make eight times the throws). The money stays literal, so the throw is still never softened.
- **Late:** an **Accept** op that consumes ≥ 1 late unit (1 ≤ L < `deadLetterShifts`) raises `lateComplaint` = +2, at most once **per team** per `lateComplaintWindowSeconds` = 30 s **PROPOSED [DR]** (a late belt, or clearing carry-over at 30 doors, raises +2 per 30 s — below the 3-per-30 s decay). **Dead letters raise 0** (clearing them is never punished) and Grace Period suppresses late complaint entirely **[D]** — **amends** spec 01 §2.2's flat "+2 per late delivery".
- Rejects and `wallet_floor` stops raise nothing. A misdelivery whose buckets allow 0 raises no event; the resident shows an **"Already filed"** stamp instead of the shake, so a second stack at one door never reads as free (the money pill still plays) **[DR]**. Each non-zero raise (never an amount of 0) is reported as `ComplaintRaised{mailboxId, source ∈ {misdelivery, late}, amountPermille}`; complaint is carried in permille so every value stays an integer.

**R10 — The shift spawner.** At the start of each Prep (after Run Structure has applied that shift's district unlock and Draft perks) Run Structure calls `Mail.PrepareShift(SpawnProfile)` with `{shift, V (spawn budget = quota × spawnOverhead, already including player count, stamps and Zoning Board), deliverySeconds D, raidWindowSeconds (0 on shift 1), cursedShare, playerCount (R9), foundValueBudget (R15)}`. This system then rolls the shift's **manifest** — every piece of mail the shift will spawn, with its kind, address, flags, value and batch — as a **pure function** of `(run seed, shift, SpawnProfile, unlocked address list)` on the RNG stream `hash(seed, "mail", shift)` (F2–F4; draw order under Formulas). The Prep **mail forecast** (spec 01 §2.1: counts by kind and district) is the manifest's summary. If any input changes during Prep (a player joins and `V` changes) the manifest is re-rolled — identically for identical inputs — and it is **locked at Delivery start**. Batches then fire at their ticks (F2) during Delivery and Raid only; no manifest mail spawns in any other phase (`SpawnSpecial`, R15, is the only exception). The same seed, stamps and player count therefore reproduce the same mail schedule (spec 01 §5.3). An optional `openingRampBatches` knob (default 0) halves the weight of the first N batches without changing the total, reserved for a cold-start pile-up if playtests show one **[D]**.

**R11 — Intake hand-off and the backlog.** At a batch tick each manifest unit, in manifest order, gets its `mailId` and its table row; the units are handed to Grid Inventory in manifest order, one `Spawn(po_intake, mailKind, addressId, flags, ids, NoMerge)` call per maximal run of consecutive same-key units (still one unit per entry, Grid Inventory R14). If the Intake is full **or the backlog is non-empty**, units are **appended** to the **backlog** instead: the unplaced remainder `ids[Placed..]` of a run and every later unit are appended one unit at a time, in manifest order. This system owns the backlog **queue**: an ordered list of whole entries (spawned singles and returned stacks, which stay whole), its contents' mail rows, `backlogUnits` (pieces waiting) and `backlogEntries` (telemetry), and their replication. Grid Inventory owns the **drain** that places backlog entries into the Intake (≤ `backlogDrainPerTick` = 32 per tick, in append order, skipping a Large that does not fit). Returned mail (WorldItem expiry, water return, bag expiry, cap eviction) comes back through the same path with its `mailId`, value, deadline and flags unchanged, at the back of the queue. The backlog has no cap (it is bounded only by conservation, ≈ 2.8 k units per run at 8 players) and never loses mail. **Returned to sender [DR, CD ruling]:** at Payday start every unit with L ≥ `deadLetterShifts` — in any container, the backlog or the world — retires: 0 ¢, no complaint, no XP, not a misdelivery, one ledger line ("N dead letters returned to sender"); Grid Inventory performs the removal (hand-back, OQ2). This is the dead-letter sink — without it dead mail cycles Intake → backlog forever. `backlogUnits > 0` is the condition Run Structure's backlog complaint reads (`backlogComplaintPerPeriod`, owned there and **not** settled here — Grid Inventory OQ1/OQ15). *Grid Inventory calls this `backlogCount`; this GDD names it `backlogUnits` and defines it as **pieces** (units), not entries — rename requested in Open Questions.*

**R12 — Automated feeders.** A placed construct whose output faces a mailbox (a belt end, an inserter; spec 03 §2.3) registers as that destination's **feeder** when built; `maxAutomatedFeeders` = 1, so a second placement facing the same mailbox is rejected **at build time** by Building / Belt Logistics, which ask `CanRegisterFeeder(destinationId)`. Deconstruction or destruction frees the slot in the same tick; re-facing a feeder is a deregister then a register (if the new slot is taken, the end is an unfed dead end). A feeder may face a LOCKED mailbox: every offer returns `not_on_route` and the belt backs up. A feeder offers one unit at a time with `OfferUnit(destinationId, unit, feederId)`, throttled by a per-feeder credit to exactly `insertRate` (F11 — one piece every 15 ticks at 2/s). Outcomes: Accept and Misdeliver as R6 (each unit is its own op); Reject, `closed` and `wallet_floor` leave the unit on the feeder, which retries at its next credit — so a belt **holds** in Prep and Payday and flushes at Delivery start, and a misrouted belt that has driven the wallet to the floor stops and backs up, and the mailbox shows "Refused". Retries emit **no** outcome: a feeder's entry into and exit from HOLDING or REFUSED is reported once each (`FeederStateChanged{destinationId, state}`) **[DR]**. At Delivery start each feeder's first insert is delayed by `feederId mod 15` ticks, so belts never fire in lockstep. A misrouted belt is never auto-jammed (a machine does not soften its own mistakes, CD ruling); the team bucket bounds its complaint and a persistent "Belt → wrong house" alert carries the warning. **An NPC truck drop is a transient feeder** with its own credit at the same rate that is never blocked by a placed feeder **PROPOSED — amends** spec 03 §2.3's "at most one automated feeder" for vehicles only (a placed belt must not lock a mailbox out of a truck route). Hand deliveries and throws are never throttled: one 0.4 s hold delivers any number of matching pieces, a feeder delivers two a second (Pillar 3).

**R13 — Thrown landings.** When a thrown entry enters a destination's catch volume on a descending segment (Throwing's swept test; first qualifying volume wins), Throwing & Catching calls `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)` **[HB]** — `thrownPm` (the thrower's `ThrownCorrectPayMult`) and `releaseTick` are snapshotted into the FLIGHT record at the throw tick, so Cannon Arm applies even if the thrower disconnects and R4's closing grace judges the release; timeliness is read at the release shift (the apply shift — no shift boundary falls inside a flight plus grace): Reject → the whole entry becomes a WorldItem at the landing point (Throwing owns the bounce); Accept → every unit pays (Cannon Arm applies to the thrower); Misdeliver → **every unit** misdelivers, oldest first, until the wallet floor; the unconsumed remainder becomes one WorldItem at the mailbox (Grid Inventory R12). The arc colour Throwing shows is `Evaluate` on the destination predicted **at release**: Accept **green**, Misdeliver **red**, Reject or no destination **white** (spec 13 §4.1); the **outcome** is the catch volume actually entered **[DR, CD ruling]**. A mismatch is never hidden (spec 09 §3.1) — honesty covers information, not execution: there are **no dice** (Throwing CD D1, ratified by the owner 2026-10-16 **[HB]**), so a thrown stack reaches a wrong door only if the preview was red on the release frame; execution variance is human (aim and timing against an exact preview, moving receivers — OQ8), the only source of an *unintended* wrong door. Non-mail thrown into a catch volume is `wont_fit` → white, bounce.

**R14 — Cursed mail.** Only under the Cursed Mail stamp: each spawned unit is Cursed with probability `cursedShare` (0.10) (F4); its spawn value is ×1.5 (F1); Cursed is a StackKey flag (never merges with plain mail). Correct delivery pays the ×1.5 value; `deliver_matching` delivers Cursed stacks like any other match. A misdelivery op that consumes ≥ 1 Cursed unit emits **one** `CursedMisdelivered{destinationId, units}`, and at most one per destination per `doorComplaintWindowSeconds` (30 s), tracked by its own per-destination `lastCursedTick` (independent of the door bucket; an op stopped at the floor with 0 units consumed fires nothing) **[D]** — otherwise a misrouted Cursed belt (one op per unit) would spawn a mini-raid every half second. Enemy AI & Waves / Stamps & Run Events own the mini-raid itself.

**R15 — Special spawns (Lost Parcels).** `SpawnSpecial(kind, worldPosition)` creates one piece of mail **as a WorldItem** (`origin = Loot`), addressed uniformly to an unlocked house, `spawnShift = deadlineShift =` the current shift, flags `Found` (F1 multiplies its value by the single data value `foundValueMultPm` 3000, spec 10 — one value for every caller, so R17's one-value-per-StackKey-and-shift invariant holds **[DR]**). Callers: Stamps & Run Events (Lost Parcels, Mega Migration — fired in Prep) and Enemy AI & Waves (mega drops in the Raid). Allowed in Prep, Delivery and Raid (else `wrong_phase`); the address is drawn from its own stream `hash(seed, "mail.special", shift)` so a manifest re-roll never moves it; a position on water or inside geometry snaps to the nearest ground tile within 3 tiles, else `bad_position`; Found mail is never Cursed. This system enforces two caps across all callers **[DR]**: `lostParcelsPerShiftCap` = 6 **PROPOSED**, and Σ spawnValue ≤ `SpawnProfile.foundValueBudget` (Run Structure computes 15 % of `baseQuota[shift]`: 165 / 270 / 405 / 600 ¢ on shifts 2–5 solo) — five Found Mediums (231 ¢ each) would otherwise pay 105 % of the solo shift-2 quota and turn spec 11's intended hand-only shift-2 fail into a pass. Past either cap `SpawnSpecial` returns `cap_reached` and allocates no `mailId`. **Unclaimed Loot mail stays in the world until Payday**, exempt from the 300 s WorldItem expiry and from cap eviction, and returns to the Intake at the back of the queue at Payday start (it is then late at best); once picked up it is ordinary mail **[D]** — **amends** Grid Inventory R12 (hand-back) so a ×3 hunt never turns into free Intake mail mid-shift (Pillar 3: never remove the reason to move) while mail is still never lost (R19 there).

**R16 — Events and statistics.** Internally, the Sim emits one `MailConsumed{mailId, result ∈ {delivered, misdelivered}, payCents | penaltyCents, late, dead, actorId}` per unit (Grid Inventory's "one `Delivered` event per unit"). For everything outside the Sim the unit of reporting is the **op**: one `DeliveryOutcome{outcomeSeq, destinationId, actorId, via ∈ {hand, throw, feeder, npc}, result ∈ {accept, misdeliver}, kind, units, incomeCents | penaltyCents, lateUnits, deadUnits, complaintPermille, stoppedAtFloor, cursed, intendedAddress (misdeliver only)}` per op, broadcast to every peer (R16 is what HUD, Presentation, Audio, Run Structure and Economy consume). Presentation groups `via = hand` outcomes by `(actorId, destinationId, tick)`: one hold plays **one** door beat and **one** pill ("+N¢ · k stacks") on every peer **[HB]**. Rejects reply to the requester only (a thrown Reject is visible as Throwing's bounce); an op stopped at the wallet floor is broadcast like any outcome (with `units` = what was consumed, possibly 0, and `stoppedAtFloor`), so every peer sees REFUSED — but a **zero-unit** hand or throw floor stop is broadcast at most once per (destination, actor) per 30 ticks, and feeder retries only through `FeederStateChanged` (R12) **[DR]**. `actorId` follows Grid Inventory R7: the player for hand and throw (the **thrower**, not the catcher), `construct:<id>` for a feeder, the vehicle for an NPC drop. Per shift this system keeps **`MailShiftStats`**: delivered units and ops by kind, income, late units, dead units, misdelivered units and ops with `(intended address, actual address, actorId)` per op for the Payday roll-call, per-actor delivery counts for MVP callouts, and `misdeliveriesThisShift` (misdelivery ops that consumed ≥ 1 unit, from any actor — a `wallet_floor` stop does not count) for the zero-misdelivery bonus and Neighbourhood Watch. Roll-call lines are ordered by units consumed (descending), then by tick. No actor gets a second line until every actor with a misdelivery op has one. Feeder and NPC lines name the machine ('By Belt 3', 'By Truck 2'), never the player who built it. **Roll-call presentation [DR, CD ruling]:** 1.5 s per line, auto-advancing, any player may press to advance, ≤ 8 s total, overflow viewable in the ledger; a host setting `rollCallNames` = Names (default) / "A carrier" / Off, which also names the actor in HUD's clerk-voice misdelivery feed line and history ("Jules posted 2 letters for 9 Oak Row to 17 Elm Lane"; "A carrier" under both other settings; HUD H9) **[HB]**; it closes on one positive MVP line (e.g. the longest correct throw). Roll-call lines are aggregated by (actor, intended, actual) with a count.

**R17 — The value hint.** Payment is always computed from the server's table (R2); clients see a **display hint** per mail entry: a list of **runs in ascending `mailId` order**, each `(deadlineShift, count, unitSpawnValue)`. Within one StackKey (kind, address, flags) and one spawn shift every unit has the same spawn value (R2's `Found` flag guarantees it), so runs are short (≤ 5, one per spawn shift). The client computes the exact payout itself — `Σ count × pay(unitSpawnValue, timeliness(currentShift − deadlineShift), team and own modifiers)` with F6's integers — and the one-unit misdelivery penalty from the first run (the oldest unit). No refresh is needed at a shift change; the hint changes only when the entry's contents change (which already sends an event). This is Grid Inventory OQ5's "sum of per-unit values", made exact.

**R18 — Stat keys owned.** Content & Data's StatSheet carries these keys for this system (perks push modifiers only): `MailValueMult.<kind>` (team; Stamp Collector 1.25 on postcard and letter), `ThrownCorrectPayMult` (personal; Cannon Arm 1.1), `LateValueRatio` (team; Grace Period → 1.0, also suppresses late complaint), `MisdeliveryPenaltyMult` and `MisdeliveryComplaintMult` (team; Fragile Handling 0.5 each). The deliver-hold modifier (Quick Hands, "mailbox insert hold −50 %", spec 10) is **not** owned here: StatSheet `insertPm` 500 scales every deliver hold from this system's base `manualInsertHold` through Input & Controls' `holdEff` (Manual Delivery MD9 — OQ5 answered) **[HB]**. Local Knowledge (compass arrow to a carried piece's mailbox) reads the destination registry; Postal Sprint listens to `DeliveryOutcome`.

### States and Transitions

**Mail unit** (the server mail table is the source of truth; Grid Inventory owns the container states in between).

| State | Entered by | Exits to |
|---|---|---|
| MANIFESTED | the Prep roll or re-roll (R10); no `mailId` yet | SPAWNED at its batch tick |
| SPAWNED | batch tick in Delivery/Raid: `mailId` allocated, row frozen (F1) | INTAKE (Intake has room and backlog empty) / BACKLOG |
| BACKLOG | Intake full or backlog non-empty; also returned mail in that case | INTAKE (Grid Inventory drain) |
| INTAKE | NoMerge entry with an `arrivalSeq` | IN CIRCULATION (outfeed, take, pick) |
| IN CIRCULATION | Grid Inventory's PLACED / CURSOR / WORLD / FLIGHT / BELT (observed, not owned) | DELIVERED / MISDELIVERED (R6); INTAKE or BACKLOG (expiry, water, bag expiry, cap eviction, Loot at Payday — back of the queue, row unchanged) |
| DELIVERED | Accept consumed the unit; paid F6 (0 for a dead letter) | — terminal; row retired |
| MISDELIVERED | Misdeliver consumed the unit; penalty F7, complaint F9 | — terminal; row retired |

*Late* and *dead* are attributes derived from `currentShift − deadlineShift` (F5), not states. Reject and `wallet_floor` change no state.

**Destination.**

| State | Entered by | Exits to |
|---|---|---|
| LOCKED | run start, for every district except 1 | CLOSED or OPEN by phase, on `UnlockDistrict(d)` (never re-locks) |
| CLOSED | unlocked and `SetDeliveriesOpen(false)` | OPEN on `SetDeliveriesOpen(true)` (Delivery start) |
| OPEN | unlocked and `SetDeliveriesOpen(true)` | CLOSED at the end of Delivery/Raid |

`Evaluate` checks CLOSED before LOCKED (R5), so a locked mailbox reads "closed" in Prep — harmless, both are Rejects.

**Feeder slot** (per destination; placed feeders only).

| State | Entered by | Exits to |
|---|---|---|
| EMPTY | initial; feeder deconstructed or destroyed | OCCUPIED on a successful build (a second build is rejected at build time) |
| READY | credit ≥ one unit (F11) | THROTTLED on an insert; HOLDING if the destination is CLOSED; REFUSED on `wallet_floor` or Reject |
| THROTTLED | credit below one unit | READY when the credit refills |
| HOLDING | the destination is CLOSED; the unit stays on the feeder | READY at OPEN (the belt flushes) |
| REFUSED | `wallet_floor` or Reject; the unit stays; the mailbox shows "Refused" | re-evaluated at every credit; READY once accepted or misdelivered |

An NPC drop is a transient feeder with the same READY / THROTTLED / HOLDING / REFUSED states and no slot.

### Interactions with Other Systems

| System | Direction | Interface / data | Owner of the interface |
|---|---|---|---|
| Simulation Core | upstream | 30 Hz tick, mail-id counter, RNG streams (`hash(seed, "mail", shift)`), event bus, `Inputs.Apply` order | Simulation Core |
| Content & Data Definitions | upstream | `MailKindDef` (+ `fallbackKind`), `DestinationTypeDef` (+ `catchVolume`), `MailMixDef`, balance keys, StatSheet keys of R18, ContentValidator rules | this system owns the def *contents*; Content & Data owns loading and validation |
| World Generation | upstream | address table and per-lot `(addressId, districtId, streetId, mailboxTile, faceNormal, anchor, approachTile, truckStopPoint)` with R3's spacing; district partition; streets per district; address display strings | World Generation |
| Grid Inventory & Containers | both | consumes `Spawn(po_intake, …, NoMerge)`, `Consume`/`Peek` oldest-first, the drain, WorldItem return path, StackKey with `flags`; provides the backlog queue, `backlogUnits`, `deliver`/`deliver_matching` orchestration, `Evaluate`, the value hint (R17), Loot exemption (R15, amendment) | shared — contract in `grid-inventory.md` R7, R12, R14 and here |
| Run Structure | upstream caller | calls `PrepareShift(SpawnProfile)`, `UnlockDistrict(d)`, `SetDeliveriesOpen(bool)`; reads `DeliveryOutcome`, `ComplaintRaised`, `MailShiftStats`, `backlogUnits` | Run Structure calls down; this system reports up (orchestrator rule) |
| Economy, Wallet & Shop | downstream implementer of a port | implements `IWalletGate` (R7); reads `DeliveryOutcome` income/penalty | this system defines the port |
| Manual Delivery | downstream | the deliver hold and prompt: `Evaluate` (client-side), base `manualInsertHold`, the hint (R17) for "+N¢ / −N¢" | Manual Delivery owns the UX |
| Throwing & Catching | downstream | `catchVolume`, `Evaluate` for the arc colour, `ResolveThrownLanding` | Throwing owns the ballistic and landing test |
| Belt Logistics | downstream | `CanRegisterFeeder`, `OfferUnit`, feeder outcomes; sorter filters read address fields | this system owns the destination side |
| Vehicles & NPC Drivers | downstream | NPC drops via `OfferUnit` (transient feeder); route stops reference `addressId`/districts | Vehicles owns routes |
| Building System | downstream | build-time feeder check `CanRegisterFeeder` | this system |
| Enemy AI & Waves, Stamps & Run Events | downstream | `CursedMisdelivered`; `SpawnSpecial` (Lost Parcels, mega drops); `cursedShare` from the Cursed Mail stamp via `SpawnProfile` | callers own their events |
| Perks & Perk Draft | upstream (StatSheet) | R18 modifiers only | Perks push modifiers |
| Player Character & Movement | downstream | mailbox interaction anchor (range `rangeMm` is Player Character's; hold timing is Input & Controls' `holdEff` from base `manualInsertHold`); Postal Sprint starts on `DeliveryOutcome{result: accept}` (its "`Delivered`") | Player Character owns the interaction framework |
| HUD & Notifications, Map & Address UI, Menus (Payday), Presentation & World Dressing, Audio | downstream | `DeliveryOutcome`, `backlogUnits`, forecast summary, `MailShiftStats`, destination states | presentation consumers |
| Networking & Session | both | Network section: outcome events, `backlogUnits`, forecast, JoinState contribution | Networking owns transport |
| Meta-Progression | downstream | delivery counts for Postal Rank XP (count **units with pay > 0**, Open Questions) | Meta-Progression |

## Formulas

*Specialists: systems-designer (formulas, tables, determinism), economy-designer (values, rounding, faucet/sink checks). All arithmetic is integer: money in cents, multipliers in permille (‰), time in 30 Hz ticks, intermediates in 64-bit integers. `RH(x / d)` = round half up = `(x + d/2) / d` with integer division; every input is non-negative except F3's carry, which uses floor division. Variables whose Source is `data file` are tuning knobs (Section G).*

### F1 — Spawn value

The spawnValue formula is defined as:

`spawnValue = RH( baseValue × distPm × shiftPm × cursedPm × foundPm / 10^12 )`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| baseValue | int ¢ | 4 / 8 / 30 / 70 / 160 | data file (MailKindDef) | kind base value (R1) |
| distPm | int ‰ | 1000–2000 | calculated: `1000 + distanceStepPm × (district − 1)`, `distanceStepPm` 250 (data file) | the destination house's district |
| shiftPm | int ‰ | 1000–1400 | calculated: `1000 + shiftStepPm × (spawnShift − 1)`, `shiftStepPm` 100 (data file) | spawn shift |
| cursedPm | int ‰ | {1000, 1500} | data file (`cursedValueMultPm`) | 1500 if the unit is Cursed |
| foundPm | int ‰ | {1000, 3000} | data file (`foundValueMultPm` 3000) when Found | 1000 for ordinary spawns |

**Output Range:** 4 ¢ (a postcard in District 1 at shift 2: 4 × 1.1 = 4.4 → 4) to 1,344 ¢ (a Found Large, District 5, shift 5; Found mail is never Cursed, R15 — a Cursed Large tops out at 672 ¢); always fits `uint16`. **One rounding, at the end** — rounding each factor would compound error and depend on factor order; a single rounding is commutative and lets a client recompute the value exactly. Zoning Board does **not** appear here (it scales the budget `V`, R7).
**Example:** postcard, District 2, shift 2: 4 × 1250 × 1100 × 1000 × 1000 / 10^12 = 5.5 → **6 ¢**. Medium, District 4, shift 3, Cursed: 70 × 1750 × 1200 × 1500 × 1000 / 10^12 = 220.5 → **221 ¢**. Letter, District 5, shift 5: 8 × 2000 × 1400 / 10^6 = 22.4 → **22 ¢**.

### F2 — Batch schedule

The batchSchedule formula is defined as:

`nb = ceil(D / batchIntervalSeconds)` · `t_b = deliveryStartTick + 30 × batchIntervalSeconds × b + j_b` · `t_b` is clamped to `≤ deliveryStartTick + 30 × (D − tailGuardSeconds)` (`tailGuardSeconds` 9) so no batch spawns into a closed town if D is ever not a multiple of 15 · `w_b = raidSpawnRateMultPm if t_b ≥ deliveryStartTick + 30 × (D − raidWindowSeconds) and raidWindowSeconds > 0, else 1000` (and 500 for `b < openingRampBatches`) · `c_b = floor(N × cumW_b / W) − floor(N × cumW_(b−1) / W)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| D | int s | 210–300 | SpawnProfile (deliverySeconds; Rush Hour −30) | Delivery length |
| batchIntervalSeconds | int s | 15 | data file (MailMixDef) | batch spacing |
| j_b | int ticks | [−90, +90]; batch 0: [0, +90] | calculated (RNG, `batchJitterSeconds` 3) | jitter |
| raidWindowSeconds | int s | 0 (shift 1) / 90 | SpawnProfile | raid overlap at the end of Delivery |
| raidSpawnRateMultPm | int ‰ | 500 | data file (MailMixDef, spec 13 §5) | batch weight in the raid window |
| openingRampBatches | int | 0 | data file | optional cold-start ramp (R10) |
| N | int | 57–≈814 | calculated (F3; 814 with Zoning Board) | units in the shift |
| W, cumW_b | int | — | calculated | Σ w and its prefix sum up to batch b |
| c_b | int | 0–N | calculated | units in batch b |

**Output Range:** `nb` = 16 / 18 / 20 for D = 240 / 270 / 300 (14 under Rush Hour on shift 1), matching Grid Inventory D5's 15 s periods; `Σ c_b = N` exactly; batch times stay ordered (450 ticks apart > 180 ticks of jitter); the last batch fires ≥ 9 s before Delivery ends. The **total** spawn is unchanged by the raid window and by Rush Hour (spec 13 §5) — only its distribution in time changes; conserving the total in the raid window is **PROPOSED** (spec 13 says only "rate × 0.5").
**Example (jitter 0):** shift 5, D = 300, raid from 210 s: 20 batches, the last 6 (210–285 s) at weight 500 → W = 14 × 1000 + 6 × 500 = 17,000. With N = 708 (8 players) a full-weight batch carries ≈ 41.6 units and a raid batch ≈ 20.8.

### F3 — Units per kind (the manifest's counts)

The kindCounts formula is defined as:

`E_k = baseValue_k × shiftPm × meanDistPm / 10^6` (no Cursed term — `V` already carries stamps, and keeping `cursedShare` out of the counts keeps N, and so every address draw, identical with or without the Cursed Mail stamp **[DR]**) · `V_k = V × share_k‰ / 1000 (+ shares of kinds whose fallbackKind is k and that have no unlocked accepting destination)` · processing kinds in **descending baseValue** order with carry `r` (starting 0): `n_k = max(0, floorDiv(2(V_k + r) + E_k, 2E_k))` (round half up; `n_k` is never negative), `r = V_k + r − n_k × E_k` · `N = Σ n_k`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| V | int ¢ | 672–≈28,410 | SpawnProfile (`quota × spawnOverhead`, Run Structure; top end with Zoning Board) | shift spawn budget |
| share_k‰ | int | 0–600 | data file (MailMixDef, spec 03 §1.3: P/L/S/M/Lg/C — s1 0/600/300/100/0/0 · s2 100/400/300/150/50/0 · s3 100/300/250/200/100/50 · s4 100/250/200/200/150/100 · s5 100/200/200/200/150/150) | value share of kind k this shift |
| meanDistPm | rational ‰ | 1000–1530 | calculated: `1000 + 250 × Σ_houses (district − 1) / H` | mean distance multiplier over the H unlocked houses |
| E_k | rational ¢ | 4.4–343 (default knobs) | calculated | expected spawn value of one unit of kind k |
| r | rational ¢ | \|r\| ≤ ½ E of the previous (dearer) kind; the final residual ≤ ½ E_last | calculated | value carried to the next (cheaper) kind; `V × share / 1000` truncates |
| n_k | int | ≥ 0 | calculated | units of kind k this shift |

**Output Range:** the manifest's **expected** value equals `V` to within half a postcard's or letter's expected value (the last kind absorbs the carry; the table below is unrounded expectation, so it reads 83 where the rounded example gives 85); counts are deterministic for given inputs — randomness enters only through destinations and Cursed rolls (F4). Under the Cursed Mail stamp the realised value is ≈ V × 1.05 (10 % at ×1.5) — the stamp's intended uplift. *Why not Bernoulli rounding per kind:* solo shift 1 expects 1.4 Mediums, so independent rounding moves the spawned value by up to ~±7 % at solo (game-designer: a Pillar 5 risk, since quota is tuned to 1.6× spawn). The realised value still varies with the distance of the drawn destinations; BalanceSim measures it. Rational arithmetic is exact in 64-bit integers (numerators and a common denominator `H × 10^6`; pin `long`).
**Example (solo, shift 1, V = 960, 10 houses in District 1, no Cursed):** Medium E = 70, V_k = 96 → n = RH(96/70 = 1.37) = 1, r = 26; Small E = 30, (288 + 26)/30 = 10.47 → 10, r = 14; Letter E = 8, (576 + 14)/8 = 73.75 → 74, r = −2. **N = 85** (1 + 10 + 74), expected value 962 ¢ (spec 11 §3.1: "~85").

**Expected units by kind per shift** (systems-designer; Small Island, one district unlocked per shift — 10 / 18 / 28 / 38 / 50 houses, mean distance multiplier 1.000 / 1.111 / 1.250 / 1.382 / 1.530; no Cursed; Cargo's 5 / 10 / 15 % folded into Large; n = 4 and 8 scale solo by `n^0.65` = 2.46 and 3.86; ±1 in the last digit):

| Shift | n | Postcard | Letter | Small | Medium | Large | Units | Intake cells if never taken |
|---|---|---|---|---|---|---|---|---|
| 1 | 1 | 0 | 72.0 | 9.6 | 1.4 | 0 | 83 | 97 |
| 1 | 8 | 0 | 277.9 | 37.1 | 5.3 | 0 | 320 | 373 |
| 2 | 1 | 36.0 | 72.0 | 14.4 | 3.1 | 0.45 | 126 | 153 |
| 2 | 8 | 139.0 | 277.9 | 55.6 | 11.9 | 1.7 | 486 | 589 |
| 3 | 1 | 48.0 | 72.0 | 16.0 | 5.5 | 1.8 | 143 | 188 |
| 3 | 4 | 118.1 | 177.1 | 39.4 | 13.5 | 4.4 | 353 | 463 |
| 4 | 1 | 60.1 | 75.2 | 16.0 | 6.9 | 3.8 | 162 | 225 |
| 4 | 8 | 232.1 | 290.1 | 61.9 | 26.5 | 14.5 | 625 | 869 |
| 5 | 1 | 74.7 | 74.7 | 19.9 | 8.5 | 5.6 | 184 | 268 |
| 5 | 4 | 183.8 | 183.8 | 49.0 | 21.0 | 13.8 | 451 | 660 |
| 5 | 8 | 288.3 | 288.3 | 76.9 | 33.0 | 21.6 | 708 | 1035 |

**Findings handed back.** (1) Spec 11 §3.1's solo counts match for shifts 1–3 (within ~5 %) but overstate packages from shift 3 (spec: 40 / 60 / 85 packages + 1–8 Cargo; the value-share mix gives 23 / 27 / 34; six to eight shift-5 Cargo at ~1,285 ¢ each would exceed the whole 6,400 ¢ budget) — spec 11 §3.1 should be corrected to this table. (2) **Grid Inventory D2 applies solo counts to 8 players**: the correct zero-pickup Intake inflow at shift 5 is ≈ 1,035 cells at 8 players, 660 at 4, 268 solo (D2 says ≈ 450 / 290 / 150). With no pickups the 320-cell Intake fills ≈ 79 s into shift 5 at 8 players (not 214 s) and ≈ 124 s at 4 players (not "never"); solo never fills from spawn alone. Backlog is therefore routine for groups that do not keep the outfeed moving, which is what OQ15's instrument must measure before `backlogComplaintPerPeriod` is set (Open Questions).

### F4 — Destinations, streets and Cursed rolls

The destinationDraw formula is defined as:

`pairs_b = min(floor(m / 2), floor(streakPairsPm × m / 1000) + [NextInt(1000) < (streakPairsPm × m) mod 1000])` (the draw is always consumed; at m = 1 no pair can form **[DR]**) with `streakPairsPm = streetStreakRatio‰ / 2 = 150`; units `2i` and `2i+1` (i < pairs_b) of batch b are a **pair**: the first draws uniformly over unlocked addresses on streets not yet used in this batch (all addresses if none remain), the second uniformly over addresses on the first's street; every other unit draws uniformly over addresses on unused streets (all addresses if none remain); then `cursed = [NextInt(1000) < cursedShare‰]`.

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| m | int | 0–64 | calculated (c_b) | units in the batch (≈ 5 solo shift 1; ≈ 42 at 8 players shift 5; ≤ 64 on a Rush Hour raid shift with Zoning Board) |
| streetStreakRatio‰ | int | 300 | data file (MailMixDef) | share of a batch's units that share a street with another unit in it |
| cursedShare‰ | int | 0 / 100 | SpawnProfile (Cursed Mail stamp) | chance a unit is Cursed |

**Output Range:** the paired share is exactly 30 % in expectation while a batch has fewer units than unlocked streets; above that, chance collisions push it higher (unavoidable — a batch of 42 must repeat streets). With spec 02's ~2 streets per district the claim fails on shift 1 (m ≈ 5 over 2–3 streets): pairing saturates there and only governs once a district has ≥ 3 named street segments (OQ15). Each address keeps a near-uniform chance (draws are over addresses, not streets, so short streets are not over-weighted). *Why not the spec's literal follower rule ("each next item with p = 0.30 takes the street of an earlier item"):* it realises 36–46 % because both the follower and its source count as sharing (systems-designer).
**Example:** m = 5 → 150 × 5 = 750 → 0 pairs + 75 % chance of one pair → expected 1.5 of 5 units share a street = **30 %**.

### F5 — Timeliness

The timeliness formula is defined as:

`L = max(0, currentShift − deadlineShift)` · `T‰ = 1000 if L = 0; LateValueRatio‰ if 1 ≤ L < deadLetterShifts; 0 if L ≥ deadLetterShifts`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| currentShift | int | 1–5 | state (Run Structure) | shift at the apply tick (deliveries only happen in Delivery/Raid, R4) |
| deadlineShift | int | 1–5 | state (mail table) | `spawnShift` for v1 kinds |
| LateValueRatio‰ | int | 500 (1000 with Grace Period) | data file `lateValueRatio` × StatSheet | pay share one shift late |
| deadLetterShifts | int | 2 | data file | lateness at which mail pays 0 ("dead letter") |

**Output Range:** {0, 500, 1000} at the default `lateValueRatio` (1000 for every late L under Grace Period). Shift-5 mail can never be late (the run ends).
**Example:** spawned in shift 2, delivered in shift 3 → L = 1 → 500 ‰; delivered in shift 4 → L = 2 → 0 ‰ (dead letter: clears, pays 0, raises no complaint).

### F6 — Payment per unit

The unitPay formula is defined as:

`pay = RH( spawnValue × T‰ × MailValueMult‰[kind] × thrownPm / 10^9 )`, `thrownPm = ThrownCorrectPayMult‰ of the thrower` for a thrown Accept, else 1000

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| spawnValue | int ¢ | 4–1344 | state (F1, frozen) | the unit's value |
| T‰ | int | 0–1000 | calculated (F5) | timeliness |
| MailValueMult‰[kind] | int | 1000–1250 | StatSheet (Stamp Collector on postcard/letter) | team value modifier |
| thrownPm | int ‰ | 1000–1100 | StatSheet, personal (Cannon Arm) | thrown correct delivery bonus |

**Output Range:** 0 ¢ (dead letter) to ≈ 1,478 ¢ (a thrown Found Large with Cannon Arm); outcome cents fields are int32. Rounded **per unit** (a stack's payout is the sum of unit payouts, so a client can reproduce it exactly from R17's runs, and a stack stopped part-way never needs re-rounding).
**Example:** late letter, spawnValue 14, Stamp Collector: 14 × 500 × 1250 × 1000 / 10^9 = 8.75 → **9 ¢**. Thrown on-time letter, spawnValue 8, Stamp Collector and Cannon Arm: 8 × 1000 × 1250 × 1100 / 10^9 = 11 → **11 ¢**.
**Faucet check (economy-designer):** solo shift 1 spawns ≈ 962 ¢ at face value (F3 example); the last batch lands at ~225 s, so a realistic hand ceiling is ≈ 85–88 % ≈ 830 ¢ — above the 600 ¢ quota, as spec 11 §4.1 requires (its "880" is optimistic, not expected).

### F7 — Penalty per unit

The unitPenalty formula is defined as:

`penalty = max(minPenaltyCents, RH( spawnValue × misdeliveryPenaltyRatio‰ × MisdeliveryPenaltyMult‰ / 10^6 ))`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| spawnValue | int ¢ | 4–1344 | state | **spawn** value — not timeliness-adjusted, so dead letters are never free to dump |
| misdeliveryPenaltyRatio‰ | int | 500 | data file (spec 11 §5) | penalty share |
| MisdeliveryPenaltyMult‰ | int | 500–1000 | StatSheet (Fragile Handling 500) | team modifier |
| minPenaltyCents | int ¢ | 1 | data file | defensive floor (values ≥ 4 already give ≥ 1) |

**Output Range:** 1 ¢ (a 4 ¢ postcard under Fragile Handling) to 672 ¢. A misdelivery costs the penalty **plus** the forgone pay — 1.5 × value on time.
**Example — a thrown 20-letter stack into the wrong house (economy-designer):**

| Case | Penalty | Forgone pay | Swing | Swing vs solo quota |
|---|---|---|---|---|
| shift 1, District 1 (letter 8 ¢) | 20 × 4 = 80 ¢ | 160 ¢ | 240 ¢ | 40 % of 600 |
| shift 5, District 5 (letter 22 ¢) | 20 × 11 = 220 ¢ | 440 ¢ | 660 ¢ | 16.5 % of 4,000 (4.3 % of the 8-player 15,440) |
| **typical** stack: shift 1, District 1, 7 letters | 7 × 4 = 28 ¢ | 56 ¢ | 84 ¢ | 14 % of 600 |
| + the forfeited zero-misdelivery bonus (+10 % of the shift total, Run Structure) | — | ≈ 400 ¢ at solo shift 5 | ≈ 1,060 ¢ | 26.5 % of 4,000 |

20-stacks are rare: F3 gives ≈ 7 letters per address solo on shift 1 and ≈ 6 at 8 players on shift 5 (StackKey also splits by shift and flags); the 20-stack is the worst case, not the norm **[DR]**. Per-unit values do not scale with player count, so in large lobbies complaint (F9), not cents, is the stronger deterrent — the reason complaint is capped per door rather than removed.

### F8 — Wallet-floor guard

The walletGuard formula is defined as:

`consume(unit) ⇔ wallet − penalty(unit) ≥ walletFloor`; the rule is **per unit, oldest first, stopping at the first unit that fails**; `consumed = min(k, max(0, floor((wallet − walletFloor) / penalty)))` is a shortcut valid only when all k units share one spawn shift (equal penalties)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| wallet | int ¢ | ≥ walletFloor | state (Economy via `IWalletGate`) | team balance at the apply tick |
| walletFloor | int ¢ | −500 | data file (Economy-owned, spec 11 §5) | lowest balance a misdelivery may reach (exactly −500 allowed) |
| penalty | int ¢ | ≥ 1 | calculated (F7) | per unit |

**Output Range:** `consumed` ∈ 0..k. Only misdeliveries are guarded; payments always apply.
**Example:** wallet 0 ¢, a 20-letter stack at 11 ¢ each → floor(500 / 11) = 45 ≥ 20, all 20 consumed (wallet −220). After two such stacks (wallet −440) a third → floor(60 / 11) = 5 units consumed, wallet −495; the other 15 bounce out as a WorldItem at the mailbox. Hand path at wallet −496 with a 4 ¢ penalty → consumed (−500 exactly); next hold → `wallet_floor`.

### F9 — Complaint deltas

The doorComplaint formula is defined as:

for the door bucket (`cap = capPm`) and the team bucket (`cap = teamCapPm`), each stored ×window and refilled lazily: `tokens = min(cap × window, tokens + min(now − lastTick, window) × cap); lastTick = now` · `raw = Σ complaintOnMisdelivery‰(unit)` over units consumed as Misdeliver in this op · `allowed = min(raw, doorTokens / window, teamTokens / window)` · both buckets `−= allowed × window` · `misdeliveryComplaint‰ = allowed × MisdeliveryComplaintMult‰ / 1000` · `lateComplaint‰ = lateComplaintPm if the op is an Accept that consumed ≥ 1 unit with 1 ≤ L < deadLetterShifts, Grace Period is off and now − lastLateTick ≥ 30 × lateComplaintWindowSeconds (team-wide; then lastLateTick = now), else 0`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| complaintOnMisdelivery‰ | int | 3000 / 5000 / 8000 / 12000 / 16000 | data file (MailKindDef) | raw complaint per unit by kind |
| capPm | int ‰ | 20000 | data file (`doorComplaintCap` 20) **PROPOSED** | most raw complaint one door can raise in a burst |
| teamCapPm | int ‰ | 40000–155000 | `RH(teamComplaintCap 40 × playerScalePm / 1000) × 1000`, playerScale from Run Structure **PROPOSED** | most raw complaint the whole team can raise in a burst; refills the same per window |
| window | int ticks | 900 | data file (`doorComplaintWindowSeconds` 30) **PROPOSED** | time for an empty door bucket to refill fully |
| tokens, lastTick | int64 | 0–cap × window | state (per destination, plus one team bucket; all start full) | elapsed ticks are clamped to `window` before multiplying (int32 would overflow after ~107 k idle ticks) |
| lastLateTick | int | — | state (team-wide; starts at −∞, i.e. never charged) | late-complaint spacing |
| MisdeliveryComplaintMult‰ | int | 500–1000 | StatSheet (Fragile Handling 500) | applied **after** the cap, so the perk still matters for stacks |
| lateComplaintPm | int ‰ | 2000 | data file (spec 11 §7 "+2") | late complaint per charged op |
| lateComplaintWindowSeconds | int s | 30 | data file **PROPOSED** | team-wide spacing of late complaint |

**Output Range:** one op raises ≤ 20 complaint (≤ 10 with Fragile Handling); one door raises ≤ 20 per 30 s sustained plus its 20 burst; the team raises ≤ `teamComplaintCap` per 30 s plus its burst; late complaint ≤ 2 per team per 30 s. The meter's clamp at 100 is Run Structure's.
**Example (letters, raw 5 each):**

| Scenario | Spec-literal per unit | **Per-door bucket (adopted)** | Per-op max-of-kind |
|---|---|---|---|
| One thrown 20-letter stack | +100 (meter maxed; Inspector; raid ×2) | **+20** (+10 with Fragile Handling) | +5 |
| Six careless hand misdeliveries at six doors | +30 | **+30** (cap never binds) | +30 |
| Belt at 2 pieces/s into one wrong door for 60 s (wallet allowing) | +600 | **≈ +60** (20 burst + 40 refilled) | +600 (each fed piece is its own op) |
| Ten late stacks delivered one per door within 30 s | +2 × units (≈ +140) | **+2** (team window) | +20 |
| Solo: 20-stacks thrown at five doors in one burst | +500 | **+40** (team bucket) | +25 |

Only the buckets protect against the thrown-stack run-ender, the belt flood (automation misdeliveries are one-unit ops) and the many-doors burst. Token buckets, not fixed windows, so no 2× burst at a window boundary. A cap below 16 clips a single Large (raw 16).

### F10 — Value hint reconstruction (client)

The hintPayout formula is defined as:

`payout = Σ_i count_i × pay(unitSpawnValue_i, T‰(currentShift − deadlineShift_i), MailValueMult‰[kind], thrownPm)` · `oneUnitPenalty = penalty(unitSpawnValue_0)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| (deadlineShift_i, count_i, unitSpawnValue_i) | u8, u8, u16 | 1–5 runs per entry | state (R17, replicated per entry) | runs in ascending mailId order |
| currentShift, modifiers | int | — | state the client already holds (RunState, StatSheet) | same integers as F5/F6 |

**Output Range:** ≤ 5 runs × 4 B per entry; exact (F6 is per unit with the same integers). The client's modifiers can lag a Draft pick by one event, so the prompt may differ from the paid amount for one round trip after a perk lands; the server's amount is always what is paid and shown in the outcome pill.
**Example (District 1):** 3 letters (value 8, deadline 1) then 2 letters (value 9, deadline 2), currentShift 2: 3 × pay(8, 500‰) + 2 × pay(9, 1000‰) = 3 × 4 + 2 × 9 = **30 ¢**; a one-unit misdelivery would cost penalty(8) = **4 ¢**.

### F11 — Feeder throttle

The feederCredit formula is defined as:

each tick: if `credit ≥ 30000` and a piece is offered, insert and `credit −= 30000`; then `credit = min(credit + insertRateMilli, 30000)` (test, then add, then cap — so a full credit inserts on ticks 0, 15, 30 and the long-run rate is exact **[DR]**)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| insertRateMilli | int | 1000–4000 | data file (`insertRate` 2.0/s → 2000) | feeder pieces per second × 1000 (per tick: 30 ticks × 1000 = one piece) |
| credit | int | 0–30000 | state (per feeder; starts at 30000) | one piece = 30000 |

**Output Range:** an exact long-run rate with no burst (credit is capped at one piece). Hands and throws bypass the credit; the hand hold's length (12 ticks at the 0.4 s base) is Player Character's F4.
**Example:** 2000 → one insert every 15 ticks (0.5 s); 1700 → intervals alternate 17 and 18 ticks. A 20-letter stack takes a feeder 10 s and a hand 0.4 s (Pillar 3).

### Determinism (manifest reproducibility)

One RNG stream per shift, `hash(seed, "mail", shift)`; `NextInt(b) = (next32 × b) >> 32` (no rejection loop, so the number of draws is fixed: **K = 2·nb + 2N + max(0, N − 1)** — per batch one jitter and one pair-rounding draw, N − 1 for the shuffle (0 when N = 0), and per unit one address draw and one Cursed roll **[DR]**). Draws happen unconditionally in this order: (1) one jitter per batch (F2); (2) Fisher–Yates shuffle of the kind-ordered unit list (F3 has no draws); (3) split by `cumW` (no draws); (4) per batch: one pair-rounding draw, then per unit in manifest order its address draw(s) and its Cursed roll (F4). The unlocked address list is sorted ascending by `(district, street, number)` before indexing. Inputs: seed, shift, `SpawnProfile` (which carries player count, stamps and perks through `V`, `D` and `cursedShare`) and the unlocked set. `mailId`s are allocated at the batch tick in manifest order.

## Edge Cases

*Specialist: systems-designer, from the rule and formula space of Sections C and D. Inventory-only cases (wallet-floor remainder placement, water return, Intake sort with a backlog, cap eviction) live in `grid-inventory.md` Edge Cases and are referenced, not repeated. Fourteen under-specified points it found were fixed in R4, R6, R12, R14–R16 and F1–F3, F8, F9 before this section was written.*

### Zero, max and out-of-range
- **If `V` yields `n_k = 0` for a kind** (solo shift 1 Large, or Medium when `V_k + r < 35`): the kind is absent from the manifest and the forecast, and its whole `V_k` carries to the next cheaper kind through `r` (F3). No RNG draw depends on it.
- **If `N = 0` for a whole shift** (only reachable with a test `V = 0`; the real minimum, Trainee solo shift 1, `V = 672`, gives N = 57): every `c_b = 0`, Delivery runs with no spawns, income is 0; the run does not abort.
- **If a batch has `c_b = 0`**: no batch event fires and nothing reaches the Intake, but the batch's jitter and pair-rounding draws are still consumed (F4 with m = 0 → 0 pairs), so the draw sequence depends only on `nb` and `N`, never on Intake state or on which batches happen to be empty.
- **If `cursedShare = 0`**: the Cursed roll is still drawn per unit and is always false, so adding or removing the stamp never reshuffles addresses.
- **If `deadLetterShifts = 1`** (knob extreme): every late unit is a dead letter — the 50 % band is empty, Grace Period and late complaint never apply. ContentValidator rejects `deadLetterShifts < 1` (0 would make every unit dead on arrival).
- **If the wallet is already below the floor** (a future non-misdelivery debit put it at −600): every Misdeliver returns `wallet_floor`; F8 clamps with `max(0, …)` and never uses a negative quotient. **If the wallet is exactly −500**: even a 1 ¢ penalty fails, so every misdelivery is refused.
- **If zero houses are unlocked** (a World Generation bug): `PrepareShift` fails with a content error and the run does not start — F3's mean distance divides by `H`.
- **If the largest v1 entry (40 postcards; `maxStack` ≤ 40) is consumed in one op**: income ≤ 40 × 1,478 ¢ is far inside the int32 cents fields; complaint fits u16 permille on the wire (≤ 20,000).

### Phase and shift boundaries
- **If a deliver hold completes, or a thrown stack enters a catch volume, after Delivery ends**: if the hold began or the throw was released while OPEN and it applies ≤ 90 ticks after close, it resolves normally (R4 closing grace); otherwise the hold gets `closed` (progress lost) and the throw bounces as a WorldItem with a 300 s timer — never delivered, never lost. The flag for a tick is fixed before `Inputs.Apply` (R4), so a tick is wholly open or wholly closed.
- **If a feeder holds a piece when Delivery ends**: the feeder goes HOLDING, its credit fills to its cap, and at the next Delivery start it inserts one piece after its `feederId mod 15` stagger and two a second after that. Pieces held over a shift arrive late (dead after two shifts).
- **If a district could unlock while mail is in flight to it**: it cannot. Unlocks happen only in Prep, when nothing spawns, and the manifest is rolled after the unlock; mail is only ever addressed to unlocked houses, so `not_on_route` arises only when a player carries mail to a house on a not-yet-unlocked street. Districts never re-lock, so returned mail always has an open destination.
- **If Rush Hour shortens shift 1 Delivery to 210 s**: 14 batches, the last at 195 s ± 3 s, so ≥ 12 s of play remain after it. On shifts with a raid, a batch whose jittered tick falls exactly at the raid boundary takes the weight its jittered tick implies (jitter is drawn before weights, so this is deterministic).
- **If the solo host pauses**: the Sim tick stops, so batches, door buckets, late windows, feeder credit and Loot exemptions all freeze; nothing here reads wall-clock time.
- **If a misdelivered op also contains one-shift-late units**: no late complaint — late complaint is for Accepts only (F9); each late unit still pays its penalty on full spawn value.

### Simultaneous ops and tick order
- **If a hand op, a thrown landing and a feeder hit one mailbox in one tick**: they resolve in that order (R6), each as its own op, sharing one door bucket and one wallet.
- **If two 20-letter stacks are misdelivered at one door in one tick**: the first raises +20 and empties the bucket, the second raises +0; money is charged for both, until the floor.
- **If two misdeliveries race for the last cents above the floor**: arrival order decides; the first is consumed (e.g. wallet → −496), the second returns `wallet_floor`.
- **If an NPC drop and a placed belt feed one mailbox in one tick**: both resolve, belt first then vehicle (R6), sharing the wallet and the bucket.
- **If a perk changes a modifier while a stack is in flight**: impossible within a shift — perks are drafted in the Draft phase, before Prep. Cannon Arm is read from a snapshot of the thrower's `ThrownCorrectPayMult` taken at the throw tick, so it still applies if the thrower disconnects before the landing.
- **If Fragile Handling is drafted**: it applies from the next shift's first op; the door bucket holds raw complaint and the ×0.5 is applied after the cap, so existing bucket state is unaffected.

### Rule collisions
- **If one deliver hold covers a dead-letter entry and a late entry for the same address**: two ops. The dead entry pays 0 ¢ and raises nothing (it does not start the late window); the late entry pays 50 % and raises +2 (0 under Grace Period); a second late entry within 30 s anywhere raises nothing (team window). Neither is ever a misdelivery.
- **If a thrown stack mixing older (late) and newer units is misdelivered near the floor**: units go oldest first; their spawn values differ (shift multiplier), so penalties differ and F8 is evaluated per unit, stopping at the first unit that fails; the raw complaint sums only consumed units; the remainder becomes one WorldItem at the mailbox.
- **If a Cursed stack stops at the floor with 0 units consumed**: no `CursedMisdelivered`, no complaint, no throttle timer; one broadcast `DeliveryOutcome{units: 0, stoppedAtFloor: true}`. **If partly consumed**: one `CursedMisdelivered{units: consumed}`, subject to the destination's `lastCursedTick`.
- **If Grace Period meets a dead letter**: it still pays 0 — Grace only changes the one-shift-late band.
- **If Fragile Handling meets a 4 ¢ postcard**: penalty = max(1, RH(1.0)) = 1 ¢; complaint 3 × 0.5 = 1.5 (1500 ‰).
- **If Stamp Collector is held when a letter is misdelivered**: the penalty is unchanged — it uses frozen spawn value, never `MailValueMult`.
- **If a mailbox's prompt showed ✓ but the server returns Reject** (phase flipped, district state, a race): the server's outcome stands, the requester's toast reads the server's reason. Because `Evaluate` is pure and addresses never change, a stale ✓ can come only from phase or lock state, never from an address mismatch; a stale modifier changes only the predicted amount (the pill shows the server's number).

### Automation (feeders, NPC drops)
- **If two feeders are placed facing one mailbox in one tick**: build ops apply in FIFO; the first registers, the second is refused with `feeder_slot_taken` and spends no materials. A green build ghost is not a guarantee.
- **If a feeder is destroyed in the tick it would insert**: inserts are atomic within the Belt step. Destroyed before that step → no offer, its piece goes to the construct's spill; after → the piece was already consumed. The slot is EMPTY the same tick.
- **If a feeder is rebuilt**: its credit starts full (one immediate piece); the rebuild cost dwarfs that.
- **If a belt carries dead letters**: they use feeder slots at 2/s and delay live mail behind them; each pays 0 and clears.
- **If a mis-sorted belt meets the floor**: each correct delivery elsewhere lifts the wallet above the floor and the belt spends it back immediately, so the wallet sits near −500 until someone fixes the sorter — intended pressure, and the Sorcerer's Apprentice beat.
- **If an NPC truck leaves a stop mid-stack**: the undelivered pieces stay in its cargo; its transient credit starts full at the next stop.

### Spawner and backlog
- **If a batch of 60 meets one free Intake cell**: the first unit enters, the other 59 append to the backlog, which is now non-empty, so every later spawn and return also appends until it drains (≤ 32 entries per tick, Grid Inventory R14).
- **If a Large waits in the backlog behind fragmented space**: the drain scans past it; it can wait while smaller entries fill holes, and its deadline keeps running (Grid Inventory D5 / OQ15).
- **If a player joins in Prep after the forecast was shown**: `PrepareShift` re-rolls and the new forecast replaces the old one (identical inputs give identical manifests); several joins in one tick cause one re-roll at the end of that tick. **A profile change arriving on or after the Delivery-start tick** is ignored for this shift (mid-Delivery joiners never rescale `V`).

### Special spawns
- **If `lostParcelsPerShiftCap` (6) or the shift's `foundValueBudget` is reached**: `SpawnSpecial` returns `cap_reached`, creates nothing and allocates no `mailId`; a mega drop then drops no parcel.
- **If `SpawnSpecial` is called in Payday, Draft, Generating or Results**: `wrong_phase`, nothing created. Prep is allowed (the Lost Parcels event fires in Prep); a Prep parcel's exemption runs until that shift's Payday.
- **If Loot mail is returned at Payday while the Intake is full**: it appends to the backlog in ascending `worldItemId` order; its deadline is the shift it spawned in, so next shift it pays 50 % of its ×3 value and is dead the shift after.
- **If Loot mail is picked up and then dropped or thrown**: its exemption ended at pickup; it is ordinary mail (300 s timer, then the Intake).

### Network, join and disconnect
- **If a player joins mid-Delivery** (a rejoin within 120 s; spec 06 §6): JoinState carries this system's contribution (Network) — destination states derive from phase and unlocked districts the joiner already receives; the mail table, door buckets and the `MailShiftStats` lines stay server-only (its counters ride JoinState).
- **If a player disconnects with a stack in flight**: it lands normally; the outcome keeps their `actorId`, and the Payday roll-call keeps their name.
- **If a peer misses `DeliveryOutcome` events while disconnected**: they are not replayed; the Payday roll-call is built from the server's `MailShiftStats`, so it is complete for everyone.

### Degenerate strategies
- **If a team dumps dead letters or surplus mail at wrong doors to clear the Intake**: each unit costs 50 % of its spawn value plus complaint (capped per door). Per stack this is strictly worse than delivering correctly. It is allowed as a priced way to clear space, not a free sink, and is tracked in telemetry. Dead letters need no dumping: they are returned to sender at Payday start (R11).
- **If a griefer spreads 20-stacks across five doors**: complaint is bounded by the team bucket (+40 solo, ≤ +155 at 8 players, per 30 s burst) while every penalty is charged — the money loss is real (F9).
- **If a player throws mail at a closed or locked mailbox to "park" it**: Reject, with no penalty and no complaint. The stack becomes a WorldItem with a 300 s timer, exactly like a drop, and no deadline is extended.
- **If a player holds mail back to protect the zero-misdelivery bonus**: undelivered mail does not spoil the bonus; it only forgoes pay, and late pay halves it. With Neighbourhood Watch (+35 % total) withholding beyond ~26 % of value still loses (economy-designer). Accepted.
- **If a team drives the wallet to the floor to block shop purchases**: floor stops raise no complaint and are not misdeliveries, and the wallet cannot be driven lower. Purchase rules are Economy's.
- **If a player farms Postal Rank XP by splitting a stack and delivering one piece at a time**: Meta-Progression must count delivered **units with pay > 0** (Open Questions), so dead letters and misdeliveries give nothing. Supply is bounded by the manifest plus at most 6 Lost Parcels per shift, and returning mail never re-spawns it.

## Dependencies

*Hard = the system cannot function without it; Soft = enhanced by it. Bidirectional consistency: each listed system's GDD must name Mail & Destinations in return — status in the GDD column.*

**Sim / Presentation side (TD gate requirement).** Everything in Core Rules and Formulas lives on the **Sim side** — the plain-C# `PerformativeMail.Sim` assembly with no Godot references: the mail table, spawner and manifest, destination registry and states, `Evaluate`, the consume routine, payment / penalty / complaint deltas, door buckets, feeder credits, the backlog queue, `MailShiftStats`. `Evaluate` and the F6/F7/F10 arithmetic are also compiled into the client (same assembly) for the prompt, the arc colour and the hint. The **Presentation side** (Godot): mailbox and house nodes (flag, shutter, tape, wobble, resident), label rendering, the PO chute and sack piles, stings and pills — owned by Presentation & World Dressing, HUD and Audio, driven by `DeliveryOutcome` and destination state. The replication adapter (Network section) is Godot-side.

**Upstream (this system depends on):**

| System | Hard/Soft | GDD | Interface this system consumes |
|---|---|---|---|
| Simulation Core | Hard | `simulation-core.md` (In Design, provisional) — HB4 adapted (OQ27) | 30 Hz tick; mail-id counter; RNG streams `hash(seed, "mail", shift)` and `hash(seed, "mail.special", shift)`; event bus; `Inputs.Apply` and step order (R6) |
| Content & Data Definitions | Hard | not yet written | loading and validating `MailKindDef` (+ `fallbackKind`), `DestinationTypeDef` (+ `catchVolume`), `MailMixDef`, balance keys; StatSheet keys of R18; validator rules (`acceptedBy` ⊆ destination types, shares sum to 1.0, `deadLetterShifts ≥ 1`, every kind with share > 0 has an accepting type or a `fallbackKind`, `batchJitterSeconds < batchIntervalSeconds / 2`, `tailGuardSeconds ≤ batchIntervalSeconds − batchJitterSeconds`) |
| World Generation | Hard | not yet written | the address table: per residential lot `(addressId, districtId, streetId, number, display string, mailboxTile, faceNormal, anchor, approachTile, truckStopPoint)` and R3's spacing guarantees; district partition; streets per district (F4 pairing quality depends on it). M-1a uses a hand-authored street of 10 houses (systems index) |
| Grid Inventory & Containers | Hard | `grid-inventory.md` (In Review) — names this system | `Spawn(po_intake, …, NoMerge)`, `Consume`/`Peek` oldest-first, the backlog drain, the return path, StackKey including `flags`, WorldItem creation for bounces and remainders |
| Run Structure | Hard (caller) | not yet written | calls `PrepareShift(SpawnProfile)` (with `V`, `D`, raid window, `cursedShare`, `playerCount`, `foundValueBudget`), `UnlockDistrict`, `SetDeliveriesOpen`; supplies `currentShift` and `playerScale`. Run Structure depends on this system; this system never calls up (orchestrator rule) |
| Perks & Perk Draft | Soft | not yet written | StatSheet modifiers for R18 keys only |
| Networking & Session | Hard (M0+) | not yet written | event delivery, JoinState envelope (Network section). M-1a runs single-player through a loopback |

**Downstream (depend on this system):**

| System | Hard/Soft | GDD | What they consume |
|---|---|---|---|
| Grid Inventory & Containers | Hard | names this system (R7, R14, Dependencies) | the backlog queue, `deliver`/`deliver_matching` orchestration, per-unit value / penalty / rounding, `Evaluate`, the value hint (R17), the Loot exemption (R15) and Payday dead-letter removal (R11) — amendments pending there |
| Manual Delivery | Hard | `manual-delivery.md` (In Review) — names this system | `Evaluate` for the prompt, base `manualInsertHold`, the hint for the "+N¢ / −N¢" preview, `DeliveryOutcome` (grouped per hold, R16) |
| Throwing & Catching | Hard | `throwing-catching.md` (In Review) — names this system | `catchVolume`, `Evaluate` for the arc colour, `ResolveThrownLanding(…, releaseTick)` (whole-stack misdelivery, OQ4) |
| Economy, Wallet & Shop | Hard | not yet written | implements `IWalletGate` (R7); `DeliveryOutcome` income and penalty |
| Run Structure | Hard | not yet written | `DeliveryOutcome`, `ComplaintRaised`, `MailShiftStats` (incl. `misdeliveriesThisShift`), `backlogUnits`, forecast summary |
| Belt Logistics | Hard | not yet written | `CanRegisterFeeder`, `OfferUnit`, feeder outcomes; address fields for sorter filters |
| Vehicles & NPC Drivers | Hard | not yet written | NPC drops via `OfferUnit` (transient feeder); addresses for route stops |
| Building System | Soft | not yet written | build-time `CanRegisterFeeder` |
| Stamps & Run Events, Enemy AI & Waves | Soft | not yet written | `SpawnSpecial` (Lost Parcels, mega drops), `CursedMisdelivered` |
| Player Character & Movement | Soft | `player-character-movement.md` (In Review) — names this system | mailbox interaction anchor; `DeliveryOutcome{accept}` starts Postal Sprint |
| Input & Controls | Soft | `input-controls.md` (In Review) — names this system | base `manualInsertHold` for its shared `holdEff` (F1) |
| HUD & Notifications, Map & Address UI, Menus & Run Flow Screens, Presentation & World Dressing, Audio | Hard (presentation) | `hud-notifications.md` (In Review, names this system); others not yet written | `DeliveryOutcome`, destination states, `backlogUnits`, forecast, `MailShiftStats` (Payday roll-call) |
| Meta-Progression | Soft | not yet written | delivered units with pay > 0 for Postal Rank XP |
| Extra Destinations & Cargo (Full Vision) | Hard | deferred | data-driven destination types and `fallbackKind` — no rule change needed |

Provisional assumptions (re-verify when those GDDs exist): Run Structure computes `V` (including Zoning Board and Trainee) and applies Inspector, Neighbourhood Watch and the zero-misdelivery bonus at Payday; Economy implements `IWalletGate` and owns the floor's value; Throwing & Catching owns landing detection against `catchVolume`; World Generation supplies streets per district large enough for F4's pairing (≥ 3 per district, Open Questions).

## Tuning Knobs

*Derived from the Formulas variable tables (systems-designer, economy-designer). Every knob is a data-file value (`MailKindDef`, `DestinationTypeDef`, `MailMixDef` or `balance.json`, spec 08), never a code constant.*

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `baseValue` postcard / letter / small / medium / large | 4 / 8 / 30 / 70 / 160 ¢ | ±25 % (retune quota with it) | unit value (F1), counts (F3) | income starves; automation pressure too early | hand delivery carries too long | `baseQuota`, `spawnOverhead` (Run Structure) |
| `distanceStepPm` | 250 | 150–350 | far-district value (F1) | far districts not worth reaching | District 5 letters swamp the economy | district unlock order |
| `shiftStepPm` | 100 | 50–150 | late-shift value | flat curve; penalties never matter late | values outrun quota | `baseQuota` |
| `cursedValueMultPm` | 1500 | 1250–2000 | Cursed pay and penalty | stamp not scary | mini-raids too lucrative to avoid | `cursedShare` |
| `cursedShare` (Cursed Mail stamp) | 10 % | 5–20 % | Cursed frequency (F4) | stamp invisible | every stack cursed; mini-raid chains | R14 throttle |
| `share_k` per shift (MailMixDef) | spec 03 §1.3 | each shift sums to 1.0 (validator) | kind mix (F3), Intake pressure | — | Large-heavy mixes fragment the Intake | `fallbackKind`, Intake 320 cells |
| `batchIntervalSeconds` / `batchJitterSeconds` | 15 / 3 | 10–30 / 0–5 (jitter < interval / 2) | spawn rhythm (F2) | Intake churns every few seconds | belts idle between lumps | Grid Inventory D5 period (15 s) |
| `raidSpawnRateMultPm` | 500 | 250–1000 | spawn share during the raid window | raids starve delivery | raids add delivery pressure on top of defence | total conserved (F2) |
| `streetStreakRatio` | 30 % | 10–50 % | street pairing (F4) | sort-by-street pays little | the island feels like three streets | streets per district (World Gen) |
| `openingRampBatches` | 0 | 0–3 | cold-start pile-up (R10) | — | first minute feels empty | — |
| `misdeliveryPenaltyRatio` | 0.5 | 0.3–0.75 | penalty (F7) | misdelivery nearly free | the floor trips constantly | `walletFloor` |
| `lateValueRatio` | 0.5 | 0.25–0.75 | late pay (F5) | carry-over feels hopeless | lateness meaningless | Grace Period |
| `deadLetterShifts` | 2 | 1–3 (validator ≥ 1) | when mail stops paying (F5) | no late band | dead mail clogs the Intake longer | Intake pressure |
| `minPenaltyCents` | 1 | 1–2 | smallest penalty (F7) | Fragile postcards free | — | Fragile Handling |
| `complaintOnMisdelivery` P/L/S/M/Lg | 3 / 5 / 8 / 12 / 16 | ±30 % | raw complaint per unit (F9) | meter never moves from misdelivery | one slip schedules the Inspector | door bucket |
| `doorComplaintCap` **PROPOSED** | 20 | 16–30 | complaint per op and per door burst (F9) | thrown stacks trivial | approaches the per-unit run-ender | `doorComplaintWindowSeconds`, Inspector 75 |
| `doorComplaintWindowSeconds` **PROPOSED** | 30 | 15–60 | door bucket refill; Cursed throttle | belt floods escalate fast | a sloppy belt is cheap | `insertRate` |
| `lateComplaint` | 2 | 1–3 | late complaint (F9) | lateness free | carry-over triggers the Inspector | `lateComplaintWindowSeconds` |
| `teamComplaintCap` **PROPOSED** | 40 × playerScale per 30 s | 30–60 (× playerScale) | team-wide complaint burst and rate (F9) | stacks at different doors stop counting | the many-doors burst maxes the meter | `doorComplaintCap`, meter thresholds (Run Structure) |
| `lateComplaintWindowSeconds` **PROPOSED** | 30 (team-wide) | 20–60 | spacing of late complaint | late belts out-raise the decay | late complaint invisible | decay 3 per 30 s |
| `closeGraceTicks` **PROPOSED** | 90 | 30–150 | closing grace for holds/throws started while OPEN (R4) | shift-end holds feel robbed | mail delivered well after the bell | Delivery timer |
| `insertRate` (house) | 2.0 /s | 1.0–4.0 | feeder speed (F11) | belts lose to walking everywhere | feeders rival a hand hold (Pillar 3) | Belt Mk1/Mk2 speeds |
| `maxAutomatedFeeders` | 1 | 1 (2+ needs a design pass) | feeders per mailbox | — | belts out-deliver hands at one door | Pillar 3 |
| `manualInsertHold` (base) | 0.4 s | 0.2–0.8 | base deliver hold (scaled by the StatSheet `insertPm` through `holdEff`) | hold indistinguishable from a tap | deliveries feel sluggish | Input & Controls F1 |
| `catchVolume` **PROPOSED** | 1.0 × 0.8 × 1.0 m | 0.6–1.4 m per axis | how forgiving a throw into a mailbox is | throws feel unfair | throwing trivialises delivery | throw speeds (Throwing) |
| `foundValueMultPm` (Lost Parcels) | 3000 | 2000–4000 | Loot value (F1) | event ignored | windfall pays the quota | `lostParcelsPerShiftCap` |
| `lostParcelsPerShiftCap` **PROPOSED** | 6 | 3–12 | Loot supply per shift (R15; also capped by `foundValueBudget`, Run Structure) | mega drops feel empty | Loot pays the quota | Lost Parcels, Mega Migration |
| `tailGuardSeconds` | 9 | 5–15 | last batch's distance from Delivery end (F2) | mail spawns into a closing town | late batches lost | `batchIntervalSeconds` |

Knobs owned elsewhere and referenced here: `walletFloor` −500 (Economy), `spawnOverhead` 1.6, `baseQuota`, `playerScale` exponent 0.65, `deliverySeconds`, `raidWindowSeconds`, `complaintDecayPerSecond`, Inspector / Neighbourhood Watch / zero-misdelivery bonus, `backlogComplaintPerPeriod`, `foundValueBudget` (Run Structure); `backlogDrainPerTick`, `worldItemDespawnSeconds`, `intakeSpawnMergePolicy` (Grid Inventory); `insertPm`, `holdMs`, `rangeMm` (Player Character); throw speeds and catch window (Throwing & Catching); Stamp Collector / Cannon Arm / Grace Period / Fragile Handling magnitudes (Perks — data in PerkDef, applied by R18).

## Visual/Audio Requirements

*From art-director, consistent with art bible §1–§5 and §7; required for Gameplay systems. All reactions use the bible's squash 0.2 s → pop ≤ 0.1 s → hold ≥ 0.15 s. A wallet **pill** is a fill carrying a sign, a glyph and a number in Chalk White: gain = Go Green fill, ✓, "+"; loss = Stamp Red fill, ✗, "−" (red and magenta are never text colours, bible §7). Principle 2 (honest outcomes are physical) governs every row; principle 1 (address before ornament) governs labels and mailbox faces — no state may hide a glyph. Inventory-panel feedback is Grid Inventory's and is not repeated. Cue names are proposals for audio-director.*

| Event | In-world | HUD / screen-space | Audio |
|---|---|---|---|
| Correct, hand | piece slots in during the squash, flag pops, box does a satisfied bounce, carrier grins | gain pill "+N¢"; wallet bumps once | `sfx_mailbox_chaching` — a stamp thunk plus one dry bell, not a cash register ("nobody claps") |
| Correct, thrown | piece teeters on the slot lip (≤ 150 ms) until the outcome arrives, then drops in; flag pops; the landing ring flashes once; Cannon Arm adds a Gold sparkle | same pill, "×1.1" tag | `sfx_mailbox_swish_chaching` + a 2D confirm for the thrower |
| Misdelivery, hand (one piece) | the wrong house wobbles; a pyjama resident holds the piece at the door for 3 s, its label (intended address) readable ≤ 3 m | loss pill; complaint envelope shakes once; clerk feed line "Sam posted a postcard for 9 Oak Row to 17 Elm Lane" (actor per `rollCallNames`, R16) | `sfx_house_wrongdoor` |
| Misdelivery, thrown stack | same gag in three tiers by units: 1–3 one piece; 4–9 a flurry from the slot; 10+ the resident buried to the waist in an envelope heap (≤ 12 prop instances, non-pickable, vanish in a Chalk White puff); duration stays 3 s. At every tier the resident keeps the flat 'Hm.' expression. No pain, distress or injury pose. The heap is the joke, not the person. | one loss pill "×20 −N¢"; one envelope shake; "form filed" | `sfx_house_wrongdoor_stack_s/m/l`, one per op |
| Late | normal pop with a Chalk White clock stamp on the piece; flag rises slowly | pill with a Gold clock "+N¢ LATE" — never red | `sfx_mailbox_chaching_late` (flat clock tick, not a descending tail) |
| Dead letter cleared | flag stays down, the box coughs dust, a moth flies out; the piece dissolves in a puff | grey pill "0¢ — DEAD LETTER CLEARED" with a strike band | `sfx_mailbox_deadletter_clear` (soft, no sting) |
| Closed (Prep, Payday, Draft) | shutter down, flag folded flat, hinged CLOSED plate; a hold rattles the latch once; a throw bounces with a 0.8 squash | prompt "Closed — opens at Delivery" | `sfx_mailbox_closed_knock` |
| Locked district | two Kraft tape strips in an X over the slot, glyph in Chalk White outline; at unlock (Prep) the tape rips off and the glyph fills with the district colour | prompt "Not on today's route"; unlock toast shows the glyph | `sfx_mailbox_taped_thud`, `sfx_mailbox_tape_rip` |
| Wallet-floor refusal | the slot clamps and spits the piece back to the hand (thrown: the remainder bounces out as a real, outlined WorldItem); a **neutral** Chalk White "REFUSED" stamp on Charcoal for 1.5 s — no red, nothing was misdelivered | prompt "Can't afford a wrong house"; wallet outline flashes once at −5.00 | `sfx_mailbox_refused_stamp` (rubber stamp plus a sigh; never Grid Inventory's panel-full buzz) |
| "Won't fit here" (non-mail) | hatch snaps shut with a clack; the item is pushed away; white arc | prompt "Won't fit here" | `sfx_mailbox_wontfit_clack` |
| Feeder (belt / inserter) | flag stays up while pieces flow, drops 1 s after idle; per-piece pop suppressed; a refused feeder shows the existing blocked-belt X panel (persistent while REFUSED) | wallet pills aggregate per second across all feeders "+16¢ ×2" | reject cues (`closed_knock`, `refused_stamp`) play once on entering HOLDING / REFUSED, never per retry; first piece after ≥ 2 s idle: a quiet `sfx_mailbox_chaching`; then `sfx_feed_tick`, pitch-stepped up to +5 semitones, reset after 1.5 s |
| Misrouted feeder | the house wobbles in rhythm; the resident reappears with a growing pile | clerk feed "Belt 3 posted 4 letters for … to 9 Jade Close." (≤ 1 line per door per 10 s) | wrong-door cue ≤ once per 2 s per house |
| NPC truck drop | the truck parks at the kerb; the driver tosses from the window on a green arc | pill attributed to the truck | `sfx_truck_dropoff_toot`, then the feeder rule |
| Cursed misdelivery | acid-green #B5E61D puff; a skull stamp pops on the door; the resident holds a glowing parcel; goons follow (Enemy AI) | skull toast per `CursedMisdelivered`; then the magenta raid alert | `sfx_cursed_sting`, keyed to `CursedMisdelivered` (R14), not to ops |
| Lost Parcel | the parcel carries a floating Kraft balloon; ×3 tag glints | screen marker ≤ 30 m with a Gold "×3" and a compass echo (never a district colour) | `sfx_lostparcel_chirp` (3D, one per 2 s), `sfx_lostparcel_get` |
| Batch at the Intake | parcels pop from the PO dock chute within 1 s | Intake manifest rows tick up (Grid Inventory) | `sfx_po_batch_chute` + desk bell |
| Backlog | static Kraft sack piles on the PO dock and steps in three tiers by `backlogUnits` (1–40, 41–160, 161+ units; knobs — unrelated to Grid Inventory's 160-cell manifest threshold, HUD HN-4d **[HB]**); no ambient motion | manifest backlog rows (Grid Inventory) | `sfx_po_backlog_groan` once per tier crossing |
| Payday roll-call | one stamped line per aggregated misdelivery (order, spread and presentation per R16: 1.5 s per line, press to advance, ≤ 8 s, names per `rollCallNames`): offender's name in Bold beside the hat chip; one resident snapshot per line; intended and actual swatch-glyph pair, ✗ stamp; ≤ 5 lines then "+N more" (viewable); closing MVP line; zero misdeliveries → Gold "CLEAN SHEET" | the Payday tally (Menus & Run Flow) | `sfx_payday_stamp_wrong`, `sfx_payday_stamp_clean` |

**Mail item label (in world).** Oversized: swatch-with-glyph top-left (glyph ≥ 0.2 m), patterned street stripe along the bottom, Bold house number (cap height ≥ 6 cm, readable ≤ 3 m), Cursed skull stamp and Found balloon tag where flagged. **Flag for the art-bible owner (Open Questions):** at 20 m a 1080p pixel is ≈ 3 cm, so a 12 px glyph needs ≈ 0.35 m — no letter can carry it; honour the 20 m read on mailbox faces, roofs and the landing marker, and set the **item** glyph's guaranteed read at ≤ 10 m.
**Mailbox face.** Glyph on top, numeral ≥ 20 cm below; flag states: down (open, idle), up (just delivered / feeding), folded with CLOSED plate (closed), taped X (locked), REFUSED stamp (floor). Unlit glyph material so it survives the raid dim. Mailbox budget is 150 tris (bible §8): shutter, tape and refused panel are texture/vertex swaps and a decal quad, not new meshes.
**PROPOSED, pending art bible §3/§5 updates:** closed shutter and taped states with the tape-rip; the refused stamp; the parcel lip-teeter; resident-buried-in-envelopes pose and heap prop; late clock stamp and dead-letter strike band; PO sack-pile tiers and dock chute with desk bell; the Lost Parcel balloon; the Cursed skull pop on a door.
**Reduced motion:** flag pop → 0.1 s cut to flag-up, held 1.5 s; wobble and rhythmic belt wobble → one 0.1 s Chalk White rim flash on the mailbox (a **PROPOSED** exception to the no-environment-outline rule); the resident cuts in and out and still holds 3 s; tape-rip and shutter → frame swaps; Cursed glow and puff static; envelope shake, wallet bump/pulse and outline flash → a static highlight; landing-ring flash → solid ring; Lost Parcel balloon bob → static; Payday stamps → opacity cuts. **Photosensitivity:** any flash, from any number of sources, ≤ 3 per second on screen **[DR]**. **Global presentation caps [DR]:** ≤ 8 resident instances and ≤ 48 pooled envelope props island-wide (oldest recycled, distance-culled), including misrouted-feeder piles. **Colour-blind:** arc ring vs X vs disc (Throwing); pills carry ✓/✗ and +/− and distinct sounds; late = Gold clock icon; refused = stamp + buzz + half-shaking flag; Payday rows = check vs X stamps; dead letter = strike band; Cursed = skull.

**Audio rate limits (all numbers PROPOSED for audio-director to ratify; no audio bible exists yet):** precedence first — the **local player's own** `DeliveryOutcome` bypasses every cap below on a reserved voice; only other actors' cues are limited: per mailbox one full cue per 250 ms, extras use the tick; ≤ 4 simultaneous cha-ching voices; wrong-door ≤ once per 2 s per house and ≤ 3 at once island-wide; `sfx_feed_tick` ≤ 6 voices (nearest first), its pitch ladder on correct units only; Lost Parcel chirp ≤ one per 2 s per parcel, ≤ 3 audible; ≤ 16 mail voices in total with priority stealing (local > wrong-door/Cursed > money > ambient ticks); distance falloff by curve plus low-pass (no flat −6 dB). Outcome classes are distinguishable by timbre family — bell = money, wood/paper = rejects, rubber/voice = wrong door. Missing cues to add: a local neutral slot-in thunk and hold-start.

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:mail-destinations` to produce per-asset specs (mail item labels per kind, mailbox face and its five states, resident reaction set, envelope heap, PO chute and sack tiers, Lost Parcel balloon, ~20 audio cues).

## Game Feel

*From art-director. References: Overcooked 2's serving hatch (instant, loud, consistent), Untitled Goose Game's NPC reactions (the wrong-house gag), Satisfactory's belts (a quiet continuous feed).*

**Input responsiveness.** The deliver hold's start shows an arm extension and the progress ring within one rendered frame (≤ 17 ms); an early release retracts within 0.1 s and sends nothing; hold completion plays a local, **neutral** slot-in (no flag, no money) within 2 frames (≤ 33 ms). The **outcome beat** (flag pop or wobble) starts at `max(0.2 s, server outcome)` after completion — ≤ 0.25 s at 120 ms RTT, ≤ 0.2 s on the host; the 0.2 s slot-in squash hides the latency.

**Predicted vs authoritative.** Predicted locally (Evaluate is pure): the prompt's ✓/✗, the predicted "+N¢ / −N¢" (F10), the arc colour, the neutral slot-in. Waits for the server (`deliver` is non-optimistic, Grid Inventory Network rule 4): flag, cha-ching, wallet, wobble, resident, complaint. If the server contradicts the prompt (a phase flip, the wallet floor), the Refused or Closed beat plays — never a silent revert.

**Landing.** The flight is predicted by Throwing; the piece teeters on the slot lip until the outcome arrives (no timeout pop); for the thrower the outcome beat starts ≤ RTT + 60 ms after predicted contact (≤ 120 ms at 60 ms RTT, ≤ 180 ms at 120 ms RTT — server contact lags by RTT/2 and the outcome returns RTT/2 later) and **never before** the server's outcome **[DR]**.

**Animation feel targets.** Mailbox: snappy, deliberate hold, no input lock while animating — the next hold may start while the previous outcome is still playing. Resident: 3 s hold, cut-in/cut-out, never blocks the door or the carrier. Feeder: continuous, quiet, readable as a rhythm.

**Impact moments (the three biggest).** (1) The cha-ching door — flag pop, bounce, coin ring. (2) The wrong-house reaction — wobble, then the resident at the door with your parcel. (3) The thrown-stack disaster — the envelope flurry or burial with one loss pill.

**Weight and responsiveness profile.** Hand delivery is crisp and certain (one hold, one verdict); throwing is committed (no recall once released); automation is ambient (it should be heard as a rhythm and only demand attention when it goes wrong).

**Feel acceptance criteria.** (FA-1) Hold-start visual ≤ 17 ms and slot-in ≤ 33 ms, by frame capture. (FA-2) At 120 ms simulated RTT the outcome beat starts ≤ 250 ms after hold completion; on the host ≤ 200 ms. (FA-3) A landing outcome never plays before the server event and starts ≤ RTT + 60 ms after predicted contact. (FA-4) A 20-stack misdelivery plays exactly one `sfx_house_wrongdoor_stack_l`, one pill and one complaint shake, within the 12-instance prop cap. (FA-5) A feeder at 2 pieces/s plays ≤ 1 full cha-ching per 2 s, and ≤ 4 cha-ching voices play at once with 8 players.

## UI Requirements

*This system owns the content and rules; the screens are owned by Manual Delivery (prompt), HUD & Notifications (pills, feed, envelope), Map & Address UI (forecast, address search), Menus & Run Flow Screens (Payday roll-call). Visual language: art bible §7.*

| Surface | Content this system supplies | Rule |
|---|---|---|
| Deliver prompt (Manual Delivery) | both addresses (swatch-with-glyph + "14 Larch Lane"), ✓/✗ from `Evaluate`, predicted "+N¢" or "Posts 1 · −N¢" from F10 (Manual Delivery's wording **[HB]**), Reject reason text (`closed`, `not_on_route`, `wont_fit`) | the game never hides a mismatch (spec 09 §3.1); the ✗ shows even when the floor would refuse, then reads "Can't afford a wrong house" when the replicated wallet predicts the floor. **Prompt matrix [DR]** (the one hold is `deliver_matching`, Grid Inventory R7 / line "Delivery from the panel"): any match in panel or cursor → ✓ "M pieces · N stacks" (stacks only when ≥ 2), the hold delivers every match; no match and the selected hotbar slot holds mail → ✗ with "Wrong house — piece is for P" (that entry's address) and "Posts 1 · −N¢", the same hold misdelivers one unit, ring drawn **dashed** (not colour only); no match and no mail selected → "Nothing for this address", the hold does nothing; panel open → "Close panel to deliver" |
| Throw landing marker (Throwing) | `Evaluate` + F10 on the predicted destination | red marker labelled "×N −N¢" before release, so the whole-stack cost is shown like the hand's "posts 1" **[DR]** |
| Wallet pills and event feed (HUD) | `DeliveryOutcome` per op: income, penalty, units, kind, late / dead counts, actor name, intended vs actual address (`intendedAddress`) | one pill per op for the local player, except one pill per hand hold (k ops grouped by actor, destination and tick, "+N¢ · k stacks", R16); pill and feed caps, merge windows and styling are HUD's (H4 / H8); feed lines in clerk voice (actor per `rollCallNames`), misroute ≤ 1 line per door per 10 s |
| Complaint envelope (HUD, Run Structure's meter) | `ComplaintRaised` per op | one shake per op, never per unit |
| Item tooltip (Inventory UI) | full address, district, value from the hint (F10) with LATE / DEAD tags | value is the payout now, not the spawn value |
| Prep forecast (Map & Address UI) | units by kind × district for the coming shift (the manifest summary) | labelled "forecast"; may change if a player joins during Prep |
| Backlog indicator (HUD / Intake manifest) | `backlogUnits` | shown whenever > 0, Stamp Red fill behind Chalk White numerals |
| Payday roll-call (Menus) | `MailShiftStats`: delivered by kind, late, dead, misdeliveries with intended / actual / actor, CLEAN SHEET flag | deadpan ledger voice; ≤ 5 lines then "+N more" |
| Lost Parcel marker (HUD) | Loot WorldItems within 30 m | Gold "×3", never a district colour |

Text strings (proposed, deadpan): "Closed — opens at Delivery", "Not on today's route", "Won't fit here", "Can't afford a wrong house", "Wrong house — posts 1", "DEAD LETTER CLEARED" (allowed, CD ruling via HUD HN-4c), "form filed", "CLEAN SHEET". All are localisation keys with named slots and plural categories (ICU style — "posts {n}", "+{n} more", roll-call lines); address strings come from World Generation's address template; currency through one locale-aware formatter (no mix of "¢" and "−5.00").

> **📌 UX Flag — Mail & Destinations**: This system has UI requirements. In Phase 4 (Pre-Production), run `/ux-design` to create a UX spec for each screen or HUD element this system contributes to (deliver prompt, wallet pills and feed, Prep forecast, Payday roll-call) **before** writing epics. Stories that reference UI should cite `design/ux/[screen].md`, not the GDD directly.

## Network

*Spec 06 §4 vocabulary: **E** event, **R** request/validate/apply, **S** snapshot. The host is authoritative for mail, destinations, payment and complaint (spec 13 §3 "Unchanged"). The wire format and Godot 4.7.1 multiplayer behaviour are NOT SOURCEABLE in the engine reference (Grid Inventory OQ12); this is a contract on the adapter, which must provide reliable ordered delivery.*

**Replicated state.**

| State | Strategy | Who receives | Notes |
|---|---|---|---|
| Mail table, manifest, door buckets, feeder credits, `lastLateTick` / `lastCursedTick` | none (server-only) | — | clients never need them; payout and penalty are reconstructed from the hint (F10) |
| Value hint per mail entry | inside Grid Inventory's entry events | that entry's subscribers | runs `(deadlineShift u8, count u8, unitSpawnValue u16)` in mailId order, ≤ 5 runs, ≈ 4–20 B per entry (R17) |
| Destination state | derived from a reliable ordered event | every peer | from Run Structure's `DeliveriesState{deliveriesOpen, closeTick, unlockedDistricts}`, sent reliable-ordered ahead of the first affected tick — never from the 5 Hz RunState snapshot (a ~260 ms stale ✓ at shift end) **[DR]** — plus the World Generation tables |
| Feeder HOLDING / REFUSED | E `FeederStateChanged` on entry and exit only (R12) | every peer | the mailbox's persistent "Refused"; a 50-bit refused bitset (7 B) in JoinState |
| Feeder registration | inside ConstructState (Building) | every peer | the mailbox shows "fed by" |
| `DeliveryOutcome` | E, one per **op**, inside Grid Inventory's per-peer per-tick event batch, in-stream after the entry's Removed / GroupCounts and before the reply | every peer | ≤ 19 B max, ≈ 9 B typical: `outcomeSeq` u16, destination u16, actor u8 tag + u16, flags u8, units u8, one varint for income or penalty (≤ 3 B), late/dead u8 each, complaint u16, `intendedAddress` u16 (misdeliver only) **[DR]**; the meter in RunState is authoritative |
| Reject reply | R reply | requester only | a thrown Reject is visible as Throwing's bounce |
| `ComplaintRaised` | not sent | — | Run Structure replicates the meter in RunState (spec 08 §3.2) |
| `backlogUnits` | E on change | every peer | uint16 |
| Forecast summary | E in Prep (and on re-roll) | every peer | 5 kinds × 5 districts × uint16 = 50 B |
| `MailShiftStats` | sent once at Payday inside Run Structure's Payday payload, paced ≤ 800 B/tick | every peer | ≤ 2 KB (aggregated lines with a resident appearance id; ≈ 150–250 B typical) |
| Batch arrival | E `BatchArrived{units}` | every peer | chute cue; 4 B |

**Bandwidth (PROPOSED — Networking & Session confirms).** Sustained outcomes are **supply-bounded**: every Accept / Misdeliver consumes ≥ 1 unit and a shift supplies ≤ ≈ 814 units, so the shift average is ≈ 3 ops/s. Spikes are not: at shift 5 with 8 players and 8 feeders ≈ 19 ops/s × 19 B ≈ 12 B/tick (**≤ 16 B/tick**, typical); the worst case — 50 feeders, 4 trucks and 8 players — coalesces feeder and NPC outcomes per (destination, actor, via, result) over 30 ticks and stays **≤ 40 B/tick** per peer. Zero-unit floor stops and feeder retries are bounded by R12/R16 (edge-triggered, ≤ 1 per door and actor per 30 ticks). Gates count wire bytes including framing. Forecast and Payday stats are bursts of ≤ 2 KB, once per phase.

**JoinState / RejoinState contribution.** `backlogUnits` (u16); the current forecast (in Prep, 50 B); `misdeliveriesThisShift` and the shift's delivered / misdelivered unit counts (u16 each) for the HUD; the refused bitset (7 B); the current `outcomeSeq` (u16) — ≤ 67 B. Destination state comes from the joiner's `DeliveriesState`; hints arrive with the joiner's own containers (Grid Inventory's block — up to 20 B per entry, to be added to its JoinState budget, OQ4). Budget ≤ 72 B. Missed `DeliveryOutcome` events are not replayed; a rejoining peer drops outcomes with `outcomeSeq` ≤ its JoinState's, so counters are never double-counted.

**Host.** The host's ops go through the same loopback path (Grid Inventory Network). Payment, penalty and complaint are computed only on the host; a client's predicted amount (F10) is display-only and is replaced by the outcome's figure. `thrownPm`, addresses, kinds and values are never client-sourced; the server rate-limits each peer's mail ops (friends-only trust, spec 13 §4.3).

## Cross-References

| This GDD references | In | What | Status |
|---|---|---|---|
| Grid Inventory & Containers (`grid-inventory.md`) | R2, R5, R6, R11, R13, R15, R16, F3, Edge Cases, Network | `Spawn(NoMerge)`, Intake outfeed `arrivalSeq`, the drain (`backlogDrainPerTick` 32), oldest-first `Consume`, `deliver` / `deliver_matching` (D-B), one-unit hand vs whole-stack thrown misdelivery (OQ4), wallet floor exactly −500 (OQ13 — confirmed here), WorldItem return and remainder rules (R12), StackKey with `flags`, H-X-1; **amended here**: complaint per door (vs its "20 complaint increments"), Loot-mail expiry exemption (vs R12), D2's Intake-pressure figures, `backlogCount` → `backlogUnits`, per-op vs per-unit `Delivered` | In Review — hand-backs in Open Questions |
| Player Character & Movement (`player-character-movement.md`) | R3, R18, Interactions | interaction framework, `rangeMm`; hold counting (P12) of Input & Controls' `holdEff` from base `manualInsertHold`; Postal Sprint on `Delivered` (= `DeliveryOutcome{accept}`) | In Review — Quick Hands settled by Manual Delivery MD9 (OQ5); OQ30 hand-only share → OQ26 |
| Input & Controls (`input-controls.md`) | R3, Tuning Knobs | its F1 `holdEff` uses base `manualInsertHold`; deliver hold is the mailbox's single verb | In Review — no hand-back to this GDD |
| HUD & Notifications (`hud-notifications.md`) | R16, UI Requirements, Visual/Audio | clerk-voice feed line, `rollCallNames` in feed and history (H9), backlog tiers | In Review — HN-4 adopted |
| Simulation Core (`simulation-core.md`) | R6, Determinism, M-PERF-2 | `opSeq`, streams `mail` / `mail.special`, phase budgets | In Design — HB4 adapted (OQ27) |
| Run Structure | R4, R7, R9, R10, R11, F3 | `PrepareShift(SpawnProfile)`, `UnlockDistrict`, `SetDeliveriesOpen`; `V`, D, raid window; complaint meter, decay, Inspector at 75, `backlogComplaintPerPeriod` (not settled here); Payday adjustments | not yet written — provisional |
| Economy, Wallet & Shop | R7, R8, F8 | `IWalletGate`; `walletFloor` −500 | not yet written — provisional |
| Throwing & Catching (`throwing-catching.md`) | R6, R13, F6 | `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)`, `catchVolume`, arc colour via `Evaluate`; no dice (D1) | In Review — hand-backs adopted (OQ8) |
| Manual Delivery (`manual-delivery.md`) | R3, R4, R5, R16, UI Requirements | deliver prompt content ("posts 1"), predicted amounts (F10), one-tick grouping per hold | In Review — OQ3 adopted |
| Belt Logistics, Building System, Vehicles & NPC Drivers | R12, F11 | feeder slot, `OfferUnit`, build-time check, NPC transient feeder | not yet written — provisional |
| Stamps & Run Events, Enemy AI & Waves | R14, R15 | `cursedShare`, `CursedMisdelivered`, `SpawnSpecial`, Lost Parcel cap | not yet written — provisional |
| World Generation | R3, F3, F4 | address table, district partition, streets per district, mailbox transforms | not yet written — provisional |
| Content & Data Definitions | R1, R3, R18 | `MailKindDef` (+ `fallbackKind`), `DestinationTypeDef` (+ `catchVolume`), validator rules, StatSheet keys | not yet written — provisional |
| Meta-Progression | Interactions, Edge Cases | XP counts delivered units with pay > 0 | not yet written |
| Networking & Session | Network | outcome events, JoinState contribution, bandwidth share (PROPOSED) | not yet written |
| `design/gdd/game-pillars.md` | Player Fantasy, R12 | Pillars 1, 2, 3 (ratified narrow reading), 5 | draft (Pillar 3 clause ratified) |
| `design/art/art-bible.md` §1–§5, §7, §8 | Feel & Comedy Feedback, Visual/Audio, Game Feel | principles 1–3, district colour + glyph, Cursed #B5E61D, residents, mailbox proportions and tri budget, animation timings, HUD colour rules | approved (REVISED); PROPOSED additions listed in Visual/Audio |
| `design/spec/03` §1–§2, `01` §2–§3, `08` §2.2–§2.4, `10`, `11` §1–§7, `13` §2–§5 | throughout | retrofit sources; spec 13 wins. **Amended here:** spec 01 §3.3 / spec 11 §7 complaint per misdelivery (→ per door, R9) and late +2 (→ once per team per 30 s, 0 for dead letters and under Grace Period); dead letters returned to sender at Payday (R11); spec 03 §2.3 single feeder (→ NPC transient feeder exempt, PROPOSED); spec 11 §3.1 package counts (→ F3 table); spec 13 "raid spawn rate ×0.5" read as conserving the shift total (PROPOSED) | spec |
| `prototypes/m1a-delivery-throwing-concept/REPORT.md` | Overview | 0.4 s hold not reported slow; misdelivery penalty not reported as frustrating (one tester); this GDD is provisional pending the two-friend greybox | report |
| `production/session-logs/decision-log.md` | **[D]** markers | autonomous decisions for this GDD (2026-10-16T18:33 – 19:25) | log |

## Acceptance Criteria

*Specialist: qa-lead. Tags and BLOCKING / ADVISORY rules follow `grid-inventory.md`'s Acceptance Criteria intro: **[UNIT]** = xUnit on the plain-C# Sim (no Godot), **[INTEGRATION]** = across two systems' public interfaces with stubs, **[FUZZ]** / **[PERF]** = deterministic counts and bytes BLOCKING, milliseconds ADVISORY until the M1 reference machine exists, **[UI]** = Godot-side against a scripted Sim stub, **[MANUAL]** = evidence in `production/qa/evidence/`. `qa.level: full` — every non-MANUAL criterion is BLOCKING for Done except ms thresholds. IDs `M-<rule>-<n>`; lettered sub-IDs are separate tests sharing a fixture; suggested files `tests/unit/mail/<Rule>Tests.cs`. Integer cents and permille; tick = 1/30 s.*

**Shared fixtures.** **FX-W** stub `IWalletGate` (records calls, settable wallet, floor −500, `TryDebitPenalty` true iff `wallet − cents ≥ floor`). **FX-D1** 10 District-1 houses on 3 streets, shift-1 mix. **FX-I50** 50 houses, 10/8/10/10/12 per district, ≥ 3 streets each, seed `0x5EED`. **FX-L** letter, spawn value 8 ¢, penalty 4 ¢ (as Grid Inventory H-X-1).

### Core rules
- **M-R1-1 [UNIT, per kind]** GIVEN v1 content, WHEN each kind is read, THEN (footprint, maxStack, baseValue, complaint, unlockShift) = postcard 1×1/40/4/3/2, letter 1×1/20/8/5/1, small 1×2/1/30/8/1, medium 2×2/1/70/12/1, large 2×4/1/160/16/2, every `deadlineOffsetShifts` = 0. **b** GIVEN a test `cargo` kind (acceptedBy `business_dock`, fallback `large`, share 100 ‰) and no unlocked dock, THEN `V_large` gains `V × 100 ‰` and cargo `n = 0`; with a dock unlocked nothing moves. **c** The validator rejects: shares ≠ 1.0, `deadLetterShifts = 0`, `acceptedBy` not a destination type, a share > 0 with no accepting type and no fallback.
- **M-R2-1 [UNIT]** GIVEN the mail counter at 41 and a 3-unit batch, WHEN it fires, THEN mailIds are 42, 43, 44 in manifest order and `deadlineShift = spawnShift`. **b** `spawnValue` is unchanged by later StatSheet changes. **c** The row is retired on consume. **d** Undelivered rows survive Payday. **e** No id repeats over 10,000 allocations.
- **M-R3-1 [UNIT]** GIVEN 10 lots, WHEN the registry is built, THEN exactly 10 destinations exist, `addressId → destinationId` round-trips, and each house has the R3 field values (accepts 5 kinds, insertRate 2000 milli/s, hold 0.4 s, maxFeeders 1, requiresUnit false, catchVolume 1.0 × 0.8 × 1.0 m centred 1.0 m above the base). **b** ContentValidator rejects a lot set with same-side mailboxes < 6 m apart, opposite-side mailboxes staggered < 3 m, or a lot without a buildable `approachTile`.
- **M-R4-1 [UNIT, matrix]** GIVEN D1 unlocked and D2 not, WHEN phases cycle Generating → Prep → Delivery → Raid → Payday → Draft → Results, THEN D1 is OPEN only in Delivery and Raid, CLOSED otherwise; D2 is LOCKED throughout. **b** After `UnlockDistrict(2)` in Prep, D2 is CLOSED, OPEN at Delivery start, CLOSED at Delivery end. **c** No district ever re-locks.
- **M-R4-2 [UNIT, phase boundary]** GIVEN deliveries closing at the start of tick T (set by Run Structure in T − 1), WHEN a hold completes at T − 1, THEN Accept; **b** a hold begun at ≥ T → `closed`, progress lost; **c** a hold begun before T and applied at T + 90 → Accept, at T + 91 → `closed`; **d** a throw released before T landing at T + 90 resolves; one released at ≥ T bounces as a WorldItem with a 300 s timer, undelivered; **e** a feeder offer at T → `closed` (no grace); **f** at Delivery-start tick S an op at S is accepted; **g** a Raid tick is OPEN.
- **M-R5-1 [UNIT, parametrised]** GIVEN `Evaluate(dest, unit)`, THEN CLOSED → Reject `closed`; LOCKED in Delivery → `not_on_route`; CLOSED and LOCKED → `closed`; a tool or log → `wont_fit`; a kind outside `accepts` (fixture type) → `wont_fit`; same address → Accept; any other address → Misdeliver. **b** Purity: `Evaluate` takes no wallet or RNG; 1,000 repeated calls give identical results; at wallet −500 it still returns Misdeliver. **c** With `requiresUnit` (fixture type) a unit-number mismatch → Misdeliver, a match → Accept.
- **M-R6-1 [UNIT, per path]** GIVEN a 20-letter wrong-house entry, THEN consumed units are: hand `deliver` 1 (oldest), throw 20, feeder 1, NPC 1. **b** `deliver_matching` over 4 matching entries → 4 ops, 4 `DeliveryOutcome`s in hold order, all in one tick with contiguous `outcomeSeq`, 0 misdeliveries. **c** Hand Accept / Misdeliver / floor through Grid Inventory: covered by H-X-1, re-asserted on `MailConsumed` / `DeliveryOutcome` fields until Grid Inventory adopts them (OQ4).
- **M-R6-2 [UNIT]** GIVEN a Reject (`closed`), THEN hand: nothing changes, no version bump, `deliver_matching` counts the entry as not got; thrown: the whole entry becomes one WorldItem at the landing point; feeder and NPC: the unit stays; on every path no wallet call, no complaint, no stats change.
- **M-R6-3 [INTEGRATION, tick order]** Each sub-test on a fresh fixture. GIVEN one mailbox receiving a hand op, a thrown landing, a placed-feeder offer and an NPC offer in one tick, all Misdeliver at 4 ¢, wallet −496, THEN they resolve hand → landing → placed feeder → NPC and only the hand op consumes (wallet −500), the others `wallet_floor`; **b** the same at wallet −496 without the hand op → the landing consumes; **c** feeders at two different mailboxes resolve by ascending construct id, NPCs by ascending vehicle id; **d** at wallet 0, four 1-letter Misdeliver ops at one door in one tick debit the shared door bucket by 5,000 ‰ each, in order, and the shared wallet by 4 ¢ each.
- **M-R6-4 [UNIT]** GIVEN two players holding at one mailbox in one tick, THEN both succeed sequentially in arrival order, each against their own panel.
- **M-R7-1 [UNIT]** GIVEN on-time units, THEN Stamp Collector (1250 ‰) applies to postcard and letter only (letter 8 → 10, small 30 → 30); Cannon Arm (1100 ‰) applies only to a thrown Accept, from the `thrownPm` snapshot passed to `ResolveThrownLanding`, also after the thrower disconnects. **b** Grace Period makes one-shift-late pay 100 % and leaves dead letters at 0.
- **M-R7-2 [UNIT]** GIVEN StatSheet entries for Inspector, Neighbourhood Watch and the zero-misdelivery bonus, WHEN a unit is paid, THEN pay equals the baseline (they are Run Structure's, at Payday). **b** Zoning Board changes `V` and `N`, never `spawnValue`. **c** A failed `TryDebitPenalty` leaves the wallet unchanged. **d** The Mail assembly has no reference to the Economy assembly (reflection check).
- **M-R8-1 [UNIT]** GIVEN a dead letter (spawn 8 ¢, L = 2) misdelivered, THEN penalty 4 ¢ (spawn-based), pay 0. **b** FX-W at wallet 0, a thrown 20-letter stack at 4 ¢ costs exactly 80 ¢ (no money cap). **c** A penalty landing exactly on −500 is consumed. **d** Stamp Collector does not change the penalty.
- **M-R9-1 [UNIT]** GIVEN a fresh door bucket, WHEN any single op misdelivers an entry of any kind and size, THEN its `ComplaintRaised.amountPermille ≤ 20,000` (≤ 10,000 with Fragile Handling); a 40-postcard stack (raw 120) → 20,000. (Fantasy test 3's automated half.)
- **M-R9-2 [UNIT]** GIVEN a Reject, a `wallet_floor` stop with 0 units, or a dead-letter Accept, THEN no `ComplaintRaised` event is emitted. **b** A floor stop does not count in `misdeliveriesThisShift`. **c** Fragile Handling applies after the cap: the bucket is debited the full 20,000 raw while 10,000 is raised.
- **M-R10-1 [UNIT]** GIVEN Prep, Payday, Draft and Generating each run 100 ticks with no `SpawnSpecial` call, THEN no mailId is allocated (a Prep `SpawnSpecial` allocates exactly one, M-R15-2). **b** Changing `V` and restoring it re-rolls to a manifest hash identical to the first; three joins in one tick cause exactly one re-roll. **c** A profile change on or after the Delivery-start tick is ignored. **d** The forecast equals the manifest's counts by kind × district and sums to N. **e** Batches fire only in Delivery and Raid. **f** With `openingRampBatches = 2` batches 0–1 carry half the weight of batch 2 (±1 unit) and Σ c_b = N.
- **M-R11-1 [UNIT]** GIVEN an Intake with one free cell and a 60-unit batch, THEN 1 unit enters and 59 append (`backlogUnits = 59`). **b** A later spawn appends while the backlog is non-empty even if a cell is free. **c** A returned 20-letter stack enters as one whole entry at the back with mailIds, values, deadline and flags unchanged; `backlogUnits` rises by 20. **d** Conservation: spawned = Intake + backlog + circulation + consumed.
- **M-R11-2 [INTEGRATION, Grid Inventory stub]** GIVEN at Payday start 3 dead letters (L = 2) in a carrier's panel, 2 in the backlog and 1 WorldItem, and 4 late (L = 1) letters, THEN the 6 dead units retire as returned to sender (0 ¢, no `ComplaintRaised`, not in `misdeliveriesThisShift`, one ledger line "6 dead letters returned to sender") and the 4 late units remain.
- **M-R12-1 [INTEGRATION, Building stub]** GIVEN two feeder builds facing one mailbox in one tick, THEN the first registers and the second gets `feeder_slot_taken` with 0 materials spent. **b** Deconstruction frees the slot the same tick. **c** Re-facing is deregister then register; a taken slot leaves an unfed dead end. **d** A feeder destroyed before the Belt step makes no offer; after it, its piece is already consumed.
- **M-R12-2 [UNIT]** GIVEN a feeder holding a piece in Prep, WHEN it offers, THEN Reject `closed`, the piece stays, state HOLDING, and exactly one `FeederStateChanged(HOLDING)` is emitted over 100 retries; after 10,000 idle ticks credit is exactly 30,000. **b** From Delivery-start tick S a feeder with `feederId mod 15 = 0` inserts at S, S+15, S+30 — never two at S; one with `feederId mod 15 = 7` first inserts at S + 7. **c** A feeder facing a LOCKED mailbox gets `not_on_route` every offer and keeps its piece. **d** An NPC transient feeder inserts while a placed feeder holds the slot, with its own full credit, after the belt in order. **e** A rebuilt feeder starts with full credit; an NPC leaving mid-stack keeps the remainder in cargo.
- **M-R14-1 [UNIT]** GIVEN `cursedShare = 100 ‰`, WHEN the stub `NextInt(1000)` returns 99, THEN the unit is Cursed (letter D1 s1: 8 → 12); 100 → plain. **b** At `cursedShare = 0` the roll is still drawn and always false. **c** Found mail is never Cursed. **d** `deliver_matching` pays 12 per Cursed letter.
- **M-R14-2 [UNIT, belt throttle]** GIVEN Cursed letters misrouted by a feeder every 15 ticks for 1,800 ticks (wallet 1,000), THEN exactly 2 `CursedMisdelivered` (ticks 0 and 900; the window is inclusive at 900), each `units = 1`, none at tick 885. **b** With an empty door bucket the first Cursed op still emits and raises 0 complaint. **c** A Cursed stack stopped at the floor with 0 consumed emits nothing and starts no timer. **d** A partly consumed stack emits `units = consumed`.
- **M-R15-1 [INTEGRATION, Loot; Grid Inventory R12 stub]** GIVEN `SpawnSpecial(medium, pos)` in Prep of shift 2 to a District-1 house, THEN a WorldItem `origin = Loot`, flag Found, `spawnShift = deadlineShift = 2`, spawnValue 231, addressed to an unlocked house; after 10,000 ticks and under cap pressure it is neither expired nor evicted. **b** At Payday start it joins the back of the queue (backlog if the Intake is full, ascending `worldItemId`), deadline unchanged; in shift 3 it pays 116 ¢, and at shift 4's Payday start it is returned to sender. **c** Picked up and dropped, it gets the ordinary 300 s timer.
- **M-R15-2 [UNIT]** GIVEN 6 `SpawnSpecial` calls this shift, THEN the 7th returns `cap_reached`, the mailId counter is unchanged, nothing is created; **b** with `foundValueBudget` 270, a call whose spawnValue would push Σ above 270 returns `cap_reached`. **c** Payday, Draft, Generating, Results → `wrong_phase`; Prep, Delivery, Raid succeed. **d** A water or geometry position snaps to ground within 3 tiles, else `bad_position`. **e** A manifest re-roll never changes a special's address. **f** A special consumes 0 draws from the `"mail"` stream.
- **M-R16-1 [UNIT]** GIVEN a 20-letter thrown misdelivery, THEN 20 internal `MailConsumed` and exactly 1 `DeliveryOutcome` with `units` 20, `result` misdeliver, `via` throw, `intendedAddress` set and `outcomeSeq` = previous + 1. **b** `actorId`: hand = the player, throw = the thrower (not a catcher), feeder = `construct:<id>`, NPC = the vehicle; a disconnected thrower keeps their name. **c** `MailShiftStats` holds (intended, actual, actor, count) per aggregated line and per-actor counts. **d** `misdeliveriesThisShift` counts ops that consumed ≥ 1 unit. **e** Roll-call: lines ordered by units descending then tick; no actor gets a second line before every actor has one; feeder lines read "By Belt N"; zero misdeliveries sets CLEAN SHEET; `rollCallNames = Off` emits no names.
- **R13** is covered by M-X-2; **R17** by M-F10-1; **R18** by the StatSheet-stub sub-tests M-R7-1, M-F5-1b, M-F7-1d and M-F9-1g.

### Formulas
- **M-F1-1 [UNIT, parametrised]** spawn value, one rounding: **a** postcard D2 s2 → 6; **b** Medium D4 s3 Cursed → 221 (per-factor rounding would give 222); **c** letter D5 s5 → 22; **d** postcard D1 s2 → 4; **e** Found Large D5 s5 → 1,344; **f** Cursed Large D5 s5 → 672; **g** Found Medium D1 s2 → 231.
- **M-F2-1 [UNIT]** GIVEN shift 5, D = 300, raid 90, jitter forced to 0, N = 708, THEN nb = 20, W = 17,000, Σ c_b = 708; batches 0–13 carry 41 or 42 (583 total), batches 14–19 carry 20 or 21 (125). **b** Batch 14 jittered to start + 6,299 has weight 1000 (W = 17,500); at start + 6,300 it has 500.
- **M-F2-2 [UNIT]** nb = 16 / 18 / 20 for D = 240 / 270 / 300 and 14 for D = 210. **b** With jitter forced to +90 the last batch is ≥ 270 ticks before Delivery ends. **c** For D = 250, nb = 17 and the last batch is clamped to start + 7,230. **d** Batch 0's jitter is ≥ 0 and batch times are strictly ordered. **e** Σ c_b = N with the raid window on or off and with `openingRampBatches = 2`.
- **M-F3-1 [UNIT]** GIVEN V = 960 on FX-D1, no Cursed, THEN Medium 1 (r = 26), Small 10 (r = 14), Letter 74 (r = −2); N = 85, expected value 962. **b** |Σ n_k E_k − V| ≤ E_last / 2.
- **M-F3-2 [UNIT]** GIVEN FX-I50, shift 5, V = 24,704, THEN exactly Postcard 288, Letter 289, Small 77, Medium 32, Large 22, N = 708. **b** V = 672 solo shift 1 → exactly Medium 1, Small 7, Letter 49, N = 57. **c** A kind with `V_k + r < E_k / 2` gets n = 0 and carries everything on (V_k 4, r −5, E 8 → n 0, r −1). **d** V = 0 → N = 0, no spawns, no abort. **e** Zero unlocked houses → `PrepareShift` content error. **f** `cursedShare` 0 vs 100 gives identical counts.
- **M-F4-1 [UNIT, stub RNG]** `NextInt(1000)`: next32 0 → 0, 0x80000000 → 500, 0xFFFFFFFF → 999. **b** m = 5 (750): a draw of 749 → 1 pair, 750 → 0. **c** m = 7 (1,050): one pair always, a second if the draw is < 50. **d** m = 0 → 0 pairs, one draw still consumed. **e** A pair's second unit is on its first's street; other units use unused streets, falling back to all addresses once every street is used. **f** m = 1 → 0 pairs whatever the draw, one draw consumed. **g [FUZZ]** Over 10,000 m = 5 batches on ≥ 5 streets the paired share is 30 % ± 1.5 pp.
- **M-F5-1 [UNIT]** (spawn, current) → ‰: (2,2) 1000, (2,3) 500, (2,4) 0. **b** Grace Period: (2,3) 1000, (2,4) 0. **c** `deadLetterShifts = 1`: (2,3) 0.
- **M-F6-1 [UNIT]** late letter (14) with Stamp Collector → 9; thrown on-time letter (8) with Stamp Collector and Cannon Arm → 11. **b** A dead letter pays 0 under any modifiers. **c** A 40-unit entry of the largest per-unit pay fits int32 (synthetic 255-unit case kept for the wire encoder only).
- **M-F7-1 [UNIT]** letter 8 → 4; **b** letter 22 → 11 (a 20-stack: 220 ¢ penalty + 440 ¢ forgone = 660 ¢; at shift 1, 240 ¢); **c** value 5 → 3 (half up); **d** Fragile Handling: postcard 4 → 1, value 9 → 2, a test value 1 → 1 via `minPenaltyCents`; postcard complaint 3 × 0.5 → 1,500 ‰.
- **M-F8-1 [UNIT]** wallet 0, 20-letter stack at 11 ¢ → 20 consumed, wallet −220. **b** wallet −440 → 5 consumed, −495, the other 15 become one WorldItem at the mailbox. **c** hand at −496 with 4 ¢ → −500; the next hold `wallet_floor`. **d** wallets −600 and −500 refuse even a 1 ¢ penalty with no negative quotient.
- **M-F8-2 [UNIT, mixed spawn shifts]** GIVEN one unit (spawn 8, penalty 4) then two (spawn 9, penalty 5) thrown at wallet −492, THEN 1 consumed (−496), the second fails (−501), 2 units remain in one WorldItem (the equal-penalty shortcut would wrongly give 2); raw complaint counts only the consumed unit (5,000).
- **M-F9-1 [UNIT]** letters (5,000 raw), fresh bucket: **a** one 20-stack → 20,000 (10,000 Fragile); **b** six hand letters at six doors → 5,000 each; **c** two 20-stacks at one door in one tick → 20,000 then 0, both penalised; **d** a 40-postcard stack (raw 120) → 20,000; **e** emptied at T: T + 450 allows 10,000, T + 900 allows 20,000; after 200,000 idle ticks the bucket is exactly full with no overflow; **f** one letter per 30 s → 5,000 each; **g** `MisdeliveryComplaintMult` 500 halves after the cap.
- **M-F9-2 [UNIT]** GIVEN n = 1, a belt misdelivering one letter every 15 ticks for 1,800 ticks (last op at 1,785), unlimited wallet, fresh buckets, THEN Σ complaint = 59,666 ‰ ± 2 (20 burst + refill to tick 1,785) and ticks 0–899 sum to 39,666 ‰ ± 2.
- **M-F9-3 [UNIT, late window]** GIVEN an Accept consuming ≥ 1 late unit at tick T, THEN +2,000 ‰; at T + 899 on any door 0; at T + 900 +2,000 (team-wide window). **b** Grace Period → 0. **c** A dead-letter-only Accept → 0 and does not set `lastLateTick`. **d** A Misdeliver op with late units → 0 late complaint. **e** Two late entries in one hold → +2,000 once.
- **M-F9-4 [UNIT, team bucket]** GIVEN fresh buckets, WHEN eight 20-letter stacks are misdelivered at eight doors in one tick, THEN Σ raised ≤ 155,000 ‰ at n = 8 and ≤ 40,000 ‰ at n = 1 (e.g. 20,000 + 20,000 + 0 …), every penalty still charged; **b** the team bucket refills 40,000 ‰ per 900 ticks at n = 1.
- **M-F10-1 [UNIT]** GIVEN District-1 letters, 3 at (value 8, deadline 1) then 2 at (value 9, deadline 2), currentShift 2, THEN the client payout is 30 ¢ and the one-unit penalty 4 ¢. **b [FUZZ]** On 1,000 seeded entries with ≤ 5 runs, client payout = server Σ pay and the first-unit penalty matches. **c** A shift change needs no hint refresh. **d** Units of one StackKey and spawn shift always share a spawn value.
- **M-F11-1 [UNIT]** GIVEN 2000 milli/s and a feeder always offering, THEN inserts land at ticks 0, 15, 30, … and the 20th at tick 285 (≈ 10 s), while a hand op consumes a 20-stack in one tick. **b** At 1700, intervals are 17 or 18 ticks and 1,700 ± 1 inserts occur in 30,000 ticks.

### Determinism and lifecycle
- **M-DET-1 [UNIT]** GIVEN a seed, shift 3, profile P and FX-I50, WHEN rolled twice (the second time with the address list passed in shuffled order), THEN the hash over (batch, tick, kind, addressId, flags, spawnValue) and the forecast are identical. **b** A different player count (V) or shift gives a different hash. **c** `cursedShare` 0 vs 100 gives identical addresses and different flags. **d** mailIds are identical from the same counter start.
- **M-DET-2 [UNIT, stub RNG]** The total draw count is exactly K = 2·nb + 2N + max(0, N − 1) (2,163 for N = 708, nb = 20; 2·nb for N = 0), identical for `cursedShare` 0 and 100, and an empty batch still consumes its jitter and pair draws.
- **M-E-1 [UNIT]** The Sim assembly references no wall-clock API (`DateTime`, `Stopwatch`, `Environment.TickCount`); with zero ticks run, door buckets, late windows, feeder credit and Loot exemptions are unchanged (solo pause).

### Feel and fantasy
- **M-FA-1 [UI]** = Game Feel FA-1: hold-start visual ≤ 1 rendered frame, neutral slot-in ≤ 2 frames (frame counts BLOCKING); an early release retracts ≤ 0.1 s and sends nothing. Lead sign-off screenshots are [MANUAL] evidence.
- **M-FA-2 [UI]** = FA-2: on a virtual clock at 120 ms RTT the outcome beat starts ≤ 250 ms after hold completion; on the host ≤ 200 ms.
- **M-FA-3 [UI]** = FA-3: **a** (count, BLOCKING) a landing outcome never plays before the server event; **b** (virtual clock, BLOCKING) it starts ≤ RTT + 60 ms after predicted contact — ≤ 120 ms at 60 ms RTT, ≤ 180 ms at 120 ms; the teeter lasts until the outcome with no timeout pop.
- **M-FA-4 [UI]** = FA-4: a 20-stack misdelivery logs exactly one sting, one pill, one complaint shake, ≤ 12 prop instances.
- **M-FA-5 [UI]** = FA-5: a feeder at 2 pieces/s over 10 s plays ≤ 5 full cha-chings; 8 players trigger ≤ 4 simultaneous cha-ching voices.
- **M-FT-1 [MANUAL, playtest]** Fantasy test 1 (muted HUD, audio on, ≥ 5 observers, each ≥ 90 % of 20 outcomes covering all eight classes). Owner qa-lead; protocol and results in `production/qa/evidence/mail/ft1-muted-hud.md`.
- **M-FT-2 [MANUAL, playtest, M1]** Fantasy test 2: ≥ 4 of 5 solo sessions by players new to the game, on shift 2 with District 2 freshly unlocked, reach the assigned District-2 mailbox (prompt shown) within 30 s without Local Knowledge. M-1a has no District 2, so this runs at M1. Owner qa-lead; `production/qa/evidence/mail/ft2-reading-the-town.md`.
- **M-FT-3 [UNIT + MANUAL]** Fantasy test 3: automated half = M-R9-1 and M-F9-4; the manual half codes utterances in the 10 s after each thrown-stack misdelivery and during the Payday roll-call as blame / laugh / callout, two raters, Cohen's κ ≥ 0.6, every session seeded with ≥ 1 thrown-stack misdelivery. Owner qa-lead; `production/qa/evidence/mail/ft3-laugh-not-blame.md`.

### Network
- **M-NET-1 [INTEGRATION, three peer stubs]** GIVEN each op type, THEN exactly one `DeliveryOutcome` reaches all three peers; a floor stop with `units = 0` is broadcast with `stoppedAtFloor`, at most once per (door, actor) per 30 ticks; a Reject replies to the requester only and a thrown Reject broadcasts nothing from this system. **b** `deliver_matching` over 4 entries sends 4 events in hold order, after the entries' Removed events, in one tick; each peer's presentation stub plays one door beat and one "+N¢ · 4 stacks" pill. **c** A feeder stuck at the floor for 300 ticks emits one `FeederStateChanged(REFUSED)` and no outcomes.
- **M-NET-2 [UNIT, stub serializer]** The canonical `DeliveryOutcome` encoding is ≤ 19 B at maximum field values (units 255, max varint cents, `intendedAddress` set) and round-trips.
- **M-NET-3 [UNIT]** The JoinState contribution is ≤ 67 B in Prep (forecast 50 B, `backlogUnits` and counts u16, refused bitset 7 B, `outcomeSeq`) and excludes the mail table, door buckets and `MailShiftStats` lines. **b** A mid-Delivery rejoin carries no forecast. **c** Outcomes with `outcomeSeq` ≤ the JoinState's are dropped.
- **M-NET-4 [PERF, bytes BLOCKING]** GIVEN the seeded shift-5 fixture (8 players, 3 hand/throw ops/s, 8 feeders at 2/s), WHEN 1,800 ticks run, THEN total mail wire bytes including framing ≤ 28,800 (≤ 16 B/tick). **b** Worst case (50 feeders, 4 trucks, 8 players, coalescing on): ≤ 40 B/tick. **c [FUZZ]** Client and server `Evaluate` / F10 agree on 10,000 fuzzed inputs, and the displayed amount after the outcome is always the server's.

### Performance
- **M-PERF-1 [PERF, counts BLOCKING]** `Evaluate` allocates 0 bytes (packed `AddressId`), performs ≤ 6 predicate checks and 0 registry scans. **b** Consuming a k-unit entry makes exactly k `Consume` calls, ≤ k wallet calls and 1 outcome; the hand, throw and feeder paths allocate 0 bytes. **c** An idle tick with 50 destinations and 50 feeders visits 0 mail rows and 0 buckets, updates exactly 50 credits, allocates 0 bytes. **d** A c_b-unit batch tick makes exactly g `Spawn` calls, where g is the number of maximal same-key runs handed over while the backlog is empty (g ≤ c_b), plus one `AppendBacklog` per remaining unit; 0 Intake probes on append to a non-empty backlog. **e** A shift-5 manifest roll makes exactly K draws (M-DET-2).
- **M-PERF-2 [PERF, ms ADVISORY; timed in the harness, outside the Sim]** Mail work ≤ 1.0 ms p99 and ≤ 2 ms max per tick on the combined worst-tick fixture (64-unit batch, 8 players' ops, 50-feeder spike, 100 expiring WorldItems); manifest roll ≤ 0.5 ms; a 40-unit op ≤ 0.2 ms.

### Cross-system (stubs)
- **M-X-1 [INTEGRATION, Grid Inventory]** Hand deliver, `deliver_matching`, floor and Reject: covered by H-X-1. **b** `Spawn(po_intake, …, NoMerge)` produces one entry per unit. **c** Consume is oldest-first by mailId. **d** 40 backlog entries against a stub drain of 32 per tick leave `backlogUnits` reflecting the 8 left.
- **M-X-2 [INTEGRATION, Throwing stub; covers R13]** `ResolveThrownLanding(…, releaseTick)` at a wrong house, wallet −440, 20 letters at 11 ¢ → 5 consumed, the remainder one WorldItem at the mailbox. **b** Accept pays every unit with the thrower's `thrownPm`. **c** Reject returns the whole entry to Throwing. **d** Arc colour from the destination predicted at release: Accept green, Misdeliver red, Reject or no destination white; non-mail white; a stack that enters a different catch volume than predicted resolves against the volume entered.
- **M-X-3 [INTEGRATION, Belt stub]** `OfferUnit` returns Accept / Misdeliver / Reject; `wallet_floor` leaves the unit on the feeder, which retries at its next credit; a misrouted belt at the floor stops and the destination reports "Refused".
- **M-X-4 [INTEGRATION, Economy stub]** The registered `IWalletGate` receives one `TryDebitPenalty` per misdelivered unit and one `Credit` per paid unit, all in the consuming tick.
- **M-X-5 [INTEGRATION, Run Structure caller stub]** The sequence `UnlockDistrict` → `PrepareShift` → `SetDeliveriesOpen(true)` → … → `SetDeliveriesOpen(false)` → read `MailShiftStats` at Payday reproduces M-R4-1 and M-R10-1; `ComplaintRaised` and `backlogUnits` are readable without this system calling Run Structure.

**Count:** 64 top-level IDs (≈ 200 lettered assertions, which share their parent's fixture); 2 pure [MANUAL] (M-FT-1, M-FT-2). Recount after any edit with `grep -cE '^- \*\*M-' design/gdd/mail-destinations.md`.

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 1 | **Hand-back to Grid Inventory — complaint per door.** Its edge case "a thrown 20-stack … 20 penalties and 20 complaint increments" should read "20 penalties; complaint per R9 of `mail-destinations.md` (door bucket, ≤ 20 per op)". Money stays per unit (OQ4 unchanged). | Grid Inventory GDD | its next revision / re-review |
| 2 | **Hand-back to Grid Inventory — Loot mail (R15) and dead letters (R11).** R12 must exempt `origin = Loot` mail from the 300 s expiry and from cap-eviction candidacy until Payday start, then return it to the Intake at the back of the queue (creative-director: a ×3 hunt must not become free Intake mail mid-shift; conservation is kept). At Payday start it must also remove every unit with L ≥ `deadLetterShifts` from every container, the backlog and the world as "returned to sender" (R11 — the dead-letter sink). | Grid Inventory GDD | its next revision / re-review |
| 3 | **Hand-back to Grid Inventory — D2 Intake pressure is wrong.** D2 applies solo spawn counts to 8 players; F3 here gives zero-pickup shift-5 inflow ≈ 1,035 cells at 8 players (Intake full ≈ 79 s in), 660 at 4 (≈ 124 s), 268 solo (never). Backlog is therefore routine for groups, which raises the urgency of OQ15's instrument there and must be reflected before `backlogComplaintPerPeriod` is set (still Run Structure's, still unsettled). | Grid Inventory GDD / Run Structure | before Run Structure GDD approval |
| 4 | **Hand-back to Grid Inventory — names, events, budget.** Adopt `backlogUnits` (pieces) for its `backlogCount`; its "one `Delivered` event per unit" is this GDD's internal `MailConsumed`, while the replicated event is one `DeliveryOutcome` per op (R16) — H-X-1 should assert those; add the value hint (≤ 20 B per mail entry, R17) to its JoinState budget; carry `DeliveryOutcome` in its per-peer per-tick event batch. | Grid Inventory GDD | same |
| 5 | **Resolved [HB]** — Quick Hands is `insertPm` 500 (spec 10's −50 %) on every deliver hold through Input & Controls' one shared `holdEff`, applied to the base `manualInsertHold` (Manual Delivery MD9 / OQ3a, CD ruling ii). | — | closed |
| 6 | **Run Structure contract.** Confirm: `PrepareShift(SpawnProfile)` at each Prep with `V` already including player count, Trainee and Zoning Board (+15 % budget, not per-unit value), plus `playerCount` and `foundValueBudget` (15 % of the shift quota); the meter's decay and thresholds scale by `playerScale` (the team bucket, R9, already does); the deliveries flag for tick T is set in T − 1 and sent as a reliable ordered `DeliveriesState` event (R4, Network); Inspector −10 %, Neighbourhood Watch +25 % and the zero-misdelivery +10 % are applied at Payday, credit the wallet only and are **outside** the quota test, Inspector's on the next shift (spec 10); consider a graded clean-sheet bonus (10 % at 0 ops, 5 % at ≤ ⌈n/2⌉, 0 above). Dead letters are returned to sender at Payday (R11 — the former collection question). Each Payday adjustment appears in the ledger as its own stamped line with its cents (art bible principle 2). CLEAN SHEET is the stamp for the zero-misdelivery bonus. | Run Structure GDD | before Run Structure GDD approval |
| 7 | **Economy contract.** Implement `IWalletGate` (R7). OQ13 is confirmed here (exactly −500 allowed). Note the griefing case: a misrouted belt can pin the wallet near −500 and block purchases until the sorter is fixed — accepted under the friends-only model unless Economy wants a purchase-side rule. | Economy GDD | before Economy GDD approval |
| 8 | **Throwing contract — hand-backs adopted 2026-10-16 [HB].** `ResolveThrownLanding(destinationId, entry, throwerId, thrownPm, releaseTick)` with `thrownPm` and `releaseTick` snapshotted into the FLIGHT record (R4 closing grace; timeliness at the release shift); the arc colour is `Evaluate` at release, the outcome is the volume entered (R13). **Execution variance is human, never random:** aim and timing against an exact preview whose aim sensitivity grows with range, haste and motion, and unpredicted receivers in the arc (Throwing T5, CD-SENIOR D1, ratified by the owner 2026-10-16); a wrong door requires a red preview on the release frame. Answered by Throwing T6: swept landing test through the volume, descending entry only, first qualifying volume wins. Still open: the marker "×N −N¢"; `catchVolume` 1.0 × 0.8 × 1.0 m **PROPOSED** — tune against the greybox throw speeds; arc white for every Reject; if hand share < 20 % (Manual Delivery OQ12) only non-random levers (Throwing OQ1 — `verdictRangeM` would amend R13's "never hidden" and needs the owner). | Throwing & Catching GDD / owner | two-friend greybox (D1 closed 2026-10-16 — owner ratified) |
| 9 | **NPC transient feeder** (R12) amends spec 03 §2.3's single feeder for vehicles only. Confirm in the Vehicles GDD that a truck stop at a belt-fed mailbox is valid and that its drop rate is the destination's `insertRate`. | Vehicles & NPC Drivers GDD | before M2 |
| 10 | **Raid-window spawn conservation** (F2): spec 13 only says "rate × 0.5"; this GDD conserves the shift total (like Rush Hour). Verify with BalanceSim that raid shifts stay winnable at the solo floor (spec 13 M1 gate: solo wins shift 1, fails shift 2 by hand). | systems-designer / Run Structure | M1 BalanceSim |
| 11 | **Spec 11 §3.1 correction.** Its package and Cargo counts contradict the value-share mix (shift-5 solo: 85 packages + 6–8 Cargo vs 34 packages from the mix; 6–8 Cargo alone would exceed the budget). Replace with F3's table. | spec maintainer (systems-designer) | before BalanceSim |
| 12 | **Resolved [DR]** — the many-doors hole is closed by the team bucket (R9, F9: RH(40 × playerScale) per 30 s). Keep the M1 telemetry signal (one actor ≥ 60 complaint in 30 s) to tune `teamComplaintCap`; never respond with a softer money penalty. | game-designer / Run Structure | M1 playtests |
| 13 | **Bucket, grace and cap values** (`doorComplaintCap` 20, `teamComplaintCap` 40, `doorComplaintWindowSeconds` 30, `lateComplaintWindowSeconds` 30, `closeGraceTicks` 90, `catchVolume`, `lostParcelsPerShiftCap` 6) are **PROPOSED**. Failure signal for the cap: the OQ21 signal in Grid Inventory (players never throw stacks > ~5) still appears with the cap in place → lower the cap; players ignore misrouted belts → tighten the window. The per-door cap itself (a creative-director ruling, Pillar 2 vs Pillars 1/5) is **closed 2026-10-16 — owner ratified**, team cap included; the values above stay open. | game-designer / economy-designer | first two-friend playtest (cap), M2 (belts) |
| 14 | **Lost Parcels content and placement.** Economy-designer recommends Letter/Small kinds until shift 3 (Medium later), no event on shift 1, ≤ 15–25 budget for the Cursed mini-raid. Placement (level-designer): World Generation supplies candidate points; snap to the nearest PO-reachable walkable tile free of constructs and lots within 10 tiles; guaranteed drops never fail; ≥ 40 m from the PO, ≥ 20 m apart, not within 3 m of the addressee. | Stamps & Run Events GDD / World Generation | before M4 |
| 15 | **Streets and spatial contract (World Generation).** Define a "street" as a named block segment, ≥ 3 per district (spec 02's ~2 streets per district saturates F4); split streets only at intersections, with a kerb colour change at district boundaries; emit R3's per-lot `(mailboxTile, faceNormal, anchor, approachTile, truckStopPoint)` with the 6 m / 3 m spacing; mean PO path distance strictly increasing with district id. Deferred to M1 (level-designer / game-designer): per-shift hot-street weighting (×2 on one street per district, metric ≥ 1.8 units per street over the last 4 batches), and a BalanceSim target of D5 ¢ per player-second within ±15 % of D1. | World Generation GDD / level-designer | before M1 |
| 16 | **Item-glyph read distance.** Art bible principle 1 asks for glyphs readable at ~20 m; a letter-sized label cannot carry a 20 m glyph at 1080p. Proposal: 20 m guaranteed on mailbox faces, roofs and the landing marker; ≤ 10 m on items. Also ratify the PROPOSED assets and the reduced-motion rim-flash exception (Visual/Audio). | art-director | before `/asset-spec` |
| 17 | **Postal Rank XP** must count delivered **units with pay > 0** (not ops), so splitting a dead-letter stack cannot farm XP (spec 11's "5 XP per delivery"); economy-designer proposes 1 XP per postcard/letter, 5 per small or larger, ≤ 20 per op. | Meta-Progression GDD | before M4 |
| 18 | **Content & Data additions:** `MailKindDef.fallbackKind`, `DestinationTypeDef.catchVolume`, `MailMixDef.openingRampBatches` / `tailGuardSeconds`, `foundValueMultPm`, `teamComplaintCap`, `closeGraceTicks`, `rollCallNames` (session setting), the R18 StatSheet keys, validator rules (Dependencies, incl. jitter and tail-guard). Spec 08 §2.2–§2.4 needs the same correction. | Content & Data Definitions GDD | before M0 |
| 19 | **Bandwidth share** (Network) is **PROPOSED**: ≤ 16 B/tick typical and ≤ 40 B/tick worst case per peer, wire bytes including framing; feeder/NPC coalescing per (destination, actor, via, result) over 30 ticks. | Networking & Session GDD | before M0 |
| 20 | **Priority and Fragile flags** stay deferred (spec 03 §1.2); the Priority Post perk is therefore out of the v1 perk pool unless this GDD gains a timed-delivery rule. | Perks GDD / producer | before the v1 perk pool is fixed |
| 21 | **M-1a provisional.** Everything beyond hand delivery, throwing and payment (spawner details, feeders, Cursed, Loot) is provisional until the two-friend greybox result is recorded (decision log 2026-10-16T18:07:09Z). The greybox needs only: `Evaluate`, payment and penalty (F5–F8), the door bucket (F9), a fixed spawn cadence. Gate metric added: the share of misdeliveries the actor did not intend (from a post-session tag, with Throwing T14's `redAgeAtReleaseMs`). | producer | M-1a gate |
| 22 | **Solo shift-1 sensitivity to one bad throw** (CD-GDD-ALIGN). One 20-letter thrown misdelivery on shift 1 is a 240 ¢ swing against a ≈ 230 ¢ solo margin (F6, F7), so one red-arc throw can lose solo shift 1 — informed (the arc is red before release) and honest, but in tension with spec 13's M1 gate. If BalanceSim or solo playtests show a single thrown-stack misdelivery as the sole cause of a shift-1 solo loss in more than 25 % of losses, respond through the solo quota margin or onboarding (Run Structure). Never respond with a per-unit money cap or a softer thrown penalty (Pillar 2). | Run Structure / systems-designer | M1 BalanceSim |
| 23 | **Balance follow-ups (BalanceSim).** Final-30 s batches cannot be delivered on time across 50 houses → consider `deadlineOffsetShifts` +1 for them (systems-designer / Run Structure); an all-faucet build (carry-over at 50 %, Grace Period, Zoning Board, Stamp Collector, bonuses) must still require ≥ 50 % delivery, else `spawnOverhead` → 1.4; Cannon Arm `maxStacks` 1 (Perks); a sink-capacity statement per shift (Economy); a misrouted belt above the floor drains ≈ 2,000 ¢/min from shift 3 — watch for a feeder-jam need (game-designer, M2; no auto-jam now, CD ruling). | systems-designer / Run Structure / Economy / Perks | M1 BalanceSim |
| 24 | **Cursed is risk-free to a careful player** (the ✗ prevents the trigger). Tie the mini-raid to something failable (Cursed mail going late or expiring) — never hide ✓/✗ (spec 09 §3.1, CD ruling). | Stamps & Run Events GDD | before M4 |
| 25 | **Audio / UX ratification.** No audio bible exists: the cue names, timbre families, voice caps and precedence rule (Visual/Audio) are PROPOSED for audio-director; an audio-only distinguishability test over the eight outcome classes; whether "Hm." is voice or instrument (narrative-director); captions for teammate outcomes and raid alerts, the accessibility tier, the in-grid LATE/DEAD badge and the Gold overload (ux-designer, in `/ux-design`). | audio-director / ux-designer | before `/asset-spec` and `/ux-design` |
| 26 | **Hand-only mail share (Player Character OQ30) — adapted [HB].** Player Character asks this GDD to derive, before approval, the share of mail only hands can deliver, since late-shift machine capacity is ≈ 2× demand. It cannot be derived without BalanceSim, and v1 adds no hand-only kind (Priority stays deferred, OQ20), so approval is not gated on it. Instrument: Manual Delivery OQ12's `handShare` from `DeliveryOutcome.via`. Below 20 %, the levers this GDD owns are a feeder-refused Priority kind (OQ20) and hot-street weighting (OQ15); World Generation's off-belt lots and Throwing OQ1's non-random levers are the others; never a money lever. | game-designer / systems-designer | M1 BalanceSim |
| 27 | **Simulation Core HB4 — adapted [HB].** Adopted: streams `mail` / `mail.special` indexed by shift (its F5); "arrival order" = `opSeq` (R6); hand ops count in phase 0, feeders in phase 5, NPC drops in phase 6 (R6's order). Held until Simulation Core is reviewed (In Design, provisional): the M-PERF-2 split (Mail.Step 0.25 / 1.0 ms; ms stay ADVISORY), and the wallet port — a synchronous `IEconomyApi` debit matches R7's same-tick contract, but it must stay a port this assembly owns or a core interface, never a Mail → Economy reference (M-R7-2d). | Simulation Core GDD / lead-programmer (ADR) | Simulation Core review |

**→ becomes an ADR** (implementation, not design): the mail table's storage (struct-of-arrays keyed by mailId, retirement); the destination registry and the `addressId` packing (uint32 vs interned string); the `IWalletGate` port and run-start registration (dependency inversion across Sim modules); the backlog queue's structure shared with Grid Inventory's drain; the per-shift manifest representation and the RNG stream derivation (shared with Simulation Core); the `DeliveryOutcome` canonical encoding and coalescing (blocked on Grid Inventory OQ12, the multiplayer spike).
