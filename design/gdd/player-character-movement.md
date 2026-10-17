# Player Character & Movement

> **Status**: In Review (revised 2026-10-16 after `/design-review` NEEDS REVISION — 15 blocking; creative directives D-1..D-6; awaiting re-review)
> **Design Review (2026-10-16)**: NEEDS REVISION, revised the same day — log `design/gdd/reviews/player-character-movement-review-log.md`.
> **Reconciled 2026-10-16 with sibling GDDs (hand-backs: 16 adopted, 5 adapted, 2 rejected)** — Input & Controls OQ-IC-1 (f)–(l), R2, R11; Manual Delivery OQ1, OQ11; Mail & Destinations OQ5; Throwing & Catching OQ2(c); HUD HN-2 / Comms CQ-2; Simulation Core HB5. Rejections in OQ17 / OQ31.
> **Author**: user + agents
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Creative Director Review (CD-GDD-ALIGN)**: CONCERNS → REVISED 2026-10-16 (6 text fixes applied: internal `Dead` label, fantasy test 1 re-anchored on hand delivery, OQ13 ruled intended, "jammed belt" promise removed, OQ24 on-break inventory, OQ12 dead draft picks)
> **Implements Pillar**: Pillar 3 — Hands beat machines, until they don't ("automation must never remove the player's reason to move"); Pillar 1 — The mail must flow; Pillar 5 — Scales from 1 to 8
> **Retrofit sources**: design/spec/11 §8, 05 §1, 01 §2.2/§7, 04 §2–3, 06 §6, 07 §3–4, 09 §2–5, 10 §2.6/§4, 13 §3–§4.5 (chapter 13 wins on conflict); upstream contract design/gdd/grid-inventory.md

## Summary

Player Character & Movement is the carrier every player drives: how it walks, sprints, jumps, swims, rides belts and truck roofs and shoulders past teammates; the single interaction framework behind every "press E" in the game (mailboxes, chests, seats, the shop, catching); and what happens when goons knock a carrier down (a pile drops where they fell, they clock back in at the Post Office 10 s later) or a connection drops (the body goes "on break" for 120 s). It exists so the hand path for the mail stays fast, honest and worth taking all run long — the body shows the real weight, the prompt shows the truth before you press, and a knockdown costs time, never the run.

> **Quick reference** — Layer: `Feature` · Priority: `MVP (M-1a → M1)` · Key deps: `Grid Inventory & Containers, Health & Damage, Input & Controls, World Generation, Simulation Core, Networking & Session (M0+)`

## Overview

Mechanically the carrier is a deterministic kinematic capsule in the plain-C# Sim (spec 07 §4; no physics engine) over the World Generation height field and construct/vehicle boxes, stepped at the 30 Hz tick by the **owning client** — v1 movement is client-authoritative (spec 13 §3), so the client owns its transform and the host only checks positions for consistency and authors bodies whose owner cannot (knocked down, on break). Speed is the locked walk / sprint / swim base (5.0 / 7.5 / 2.0 m/s, spec 11 §8) scaled by Grid Inventory D1 and by perks. The interaction framework (`IInteractable`: anchor, one verb, tap / hold / channel, availability with a reason) picks one focused target, times holds and sends one validated request; every "press E" system plugs into it. A knockdown calls Grid Inventory's `SpillToBag` and clocks the carrier back in at the Post Office's Spawn pad 10 s later; a disconnect freezes the body for 120 s.

For the player it is the thing they touch every second: the parcel held high while sprinting down a street, the prompt that tells the truth before the thumb moves, the scrum at the Intake door, the hat flying off when a goon catches them. Without it there is no hand path for the mail (Pillar 1), nothing for automation to be measured against (Pillar 3: *automation must never remove the player's reason to move*), and no shared physical space for eight friends to be funny in (Fellowship, MDA #1).

*Design boundary:* this GDD specifies behaviour — speeds, states, focus and hold rules, the lifecycle and its network contract. The controller's code structure, the tick driver, the input router and the replication adapter are implementation (→ become ADRs). No ADR exists yet.

## Player Fantasy

*Framing: direct — the player operates the carrier every second. Shaped with creative-director: framing A "The Route Runner" is the spine, with "Clocking Back In" as its death beat and the doorway crowd as its Fellowship beat (candidates B and C, folded in). Tone checked with narrative-director: earnest carrier, deadpan postal copy, slapstick world.*

You are the only thing on the island that can go anywhere with an address in hand. The truck is faster on a road, the belt never sleeps, the sorter never misreads — but none of them can cut across a lawn, hop a fence, take the wheel from a dawdling NPC driver, or stand on a belt and push it faster. The carrier is the last ten metres of every route and the first one there when a machine stops. They take this completely seriously: parcel held up like a waiter's tray, chin up, sprinting down Elm Street.

The body tells the truth about the load. A letter rides one hand high, a medium parcel is hugged to the chest, a heavy one goes overhead and the knees bend — and the carrier moves at exactly the speed the bag's weight gives (Grid Inventory D1). The animation never shows a speed the Sim does not give.

Every interaction is one look and one verb. Face a mailbox and the prompt shows the truth before your thumb moves: both addresses, ✓ or ✗. Face a chest and it opens; face a truck seat and you drive.

Then the goons catch you. The carrier goes flat, the hat sails off and lands on a pile wearing your tint strap, exactly where you fell, holding everything you carried — none of it insured. Ten seconds later you are **clocking back in**: a Kraft crate thumps onto the Post Office's Spawn pad, the lid pops, the hat drops back onto your head and you are already running. The pile lasts 20 s more, and 100 m is what your legs cover in that time. Knockdowns cost time, never the run.

With eight players the Intake door becomes a scrum: bodies are solid, carriers shoulder past each other with stacks raised, someone gets nudged off the truck roof. The crowd is a spice, not the spine — every push resolves, nobody is pinned in a doorway, and a solo carrier gets the whole fantasy alone.

**Reference feelings.** PlateUp's held items — taken: cargo is oversized and visible past the hat. Muck / Crab Game body comedy — the knockdown as a show for the friends watching is taken; elimination that ends your night is rejected. Overcooked's crowded kitchen — the bump is taken; teammates blocking each other as a designed obstacle is rejected. Deep Rock Galactic's team traversal — riding roofs and belts together is taken; skill-gated platforming is rejected (this is not a platformer).

**Pillars served.** Pillar 1 (*the mail must flow*): the carrier is the mail's last mover. **Pillar 3** (*hands beat machines, until they don't* — ratified reading: *"automation must never remove the player's reason to move"*) is this GDD's hardest constraint, and movement stays worth it late in a run because **the body reaches what the network cannot** — lawns, fences, the house no belt serves, a belt broken in a raid — and because **presence makes machines faster**: a player in the driver seat runs a vehicle at full speed (spec 04). The held belt push (+50 %) and Postal Sprint are secondary — a push is a stationary channel and Postal Sprint is a reward, not a reason — and neither may become the job (Push leverage is a Belt Logistics hand-back, OQ25). Perks may make the carrier faster; nothing may make the carrier unnecessary. Pillar 2 (*friendslop surface, factory depth*): earnest carrier, slapstick world, outcomes never softened. Pillar 5 (*scales from 1 to 8*): the pile's window is sized for one carrier walking back from the Spawn pad with nobody to fetch it, and a run continues even when every carrier is knocked down (spec 01).

### Feel & Comedy Feedback

*Required by the systems index (CD-SYSTEMS note) for player-facing delivery-stack GDDs. Earnest carrier, slapstick world: each beat shows what really happened, in the world, never a softer version (art bible principle 2). Player-facing words follow narrative-director's vocabulary: "knocked down", "clocking back in", "pile", "on break" — never died, killed, corpse, revived, respawned, drowned or game over.*

| Beat | Trigger | What the world does | Honest-outcome rule |
|---|---|---|---|
| Heavy carry | a heavy entry is the held (selected) item; body load from `weightPoints` | overhead carry, knees bend with load, backpack sags; the carrier gives a grim nod and keeps going | stride rate is proportional to the real speed (D1 × buffs); the body never animates faster than it moves |
| Doorway bump | two carriers' capsules overlap | both squash and wobble, stacks sway, a comic "oof"; parcels stay held | the push really moves you (F6); you never pass through a friend; a bump never drops an item |
| Knockdown | Health & Damage's death event | shock face, starfish flop, dizzy face, hat arcs off and lands on the pile, pile bursts and parcels hop | the pile lands at the knockdown spot with every entry 1:1 (Grid Inventory R13); nothing is recovered automatically; the feed names the goon type |
| Clocking back in | respawn timer expires | a tinted Kraft crate lands on the Spawn pad, lid pops, hat drops on, the carrier tips it and jogs off (*proposed — art-director*) | the countdown shown is the real one; the carrier is controllable from the lid pop; the pile's remaining seconds are on screen |
| Swim | water deep enough to swim | bobbing, hat floating alongside, wet face, parcel held overhead | blocked verbs say why ("Dry land required to throw, build or deliver"); no drowning, no parcel damage |
| Belt / roof ride | standing on a running belt or a moving truck roof | arms out for balance on a belt; crouched, one hand on the hat, on a roof | the carrier moves at exactly the belt's or vehicle's speed plus their own input |
| On break | the player's connection drops | the body sits, hat tipped over the eyes, wrapped in bubble wrap, a clock icon fills over the head | the countdown is the real 120 s; at zero the pile drops where the body sat |

**Fantasy test** (failing any one means movement has broken the fantasy):
1. **Still worth moving.** In the last shift of a run with the full v1 construct set built: no player stands still without input for more than 30 s outside the inventory panel; at least 20 % of correct deliveries are made by hand (the Deliver verb, or a thrown item accepted by a mailbox); and no player spends more than half the shift in one Push channel (all **PROPOSED** telemetry thresholds; the 20 % is a target to be derived from the BalanceSim's late-shift machine capacity, OQ22). No tester reports "nothing to do but watch".
2. **The body tells the truth.** On the local carrier, carry pose and stride change on the same tick the held item or speed changes, and measured ground speed equals F1 within 1 mm/s per tick; a tester who sees the carrier only from behind ranks light, medium and heavy held loads correctly.
3. **A knockdown is a gag, not a punishment.** The pile is always at the knockdown spot with every entry; a solo carrier walking from the Spawn pad reaches a pile 80 m away (straight line, F8: 4 s to spare) before it vanishes; in playtests the knockdown draws laughter more often than frustration.

## Detailed Design

*Specialists consulted: game-designer, systems-designer, gameplay-programmer, network-programmer, ux-designer, art-director, technical-artist, narrative-director; revised after `/design-review` (2026-10-16). **[D]** = decided in autonomous mode (logged in `production/session-logs/decision-log.md`); **[D-n]** = creative-director review directive. **PROPOSED** = new to this GDD, not in the spec. Units: Sim positions in mm (int32), speeds in mm/s, multipliers in permille (‰), time in 30 Hz ticks; the wire uses cm (Network).**[D]** were taken in autonomous mode by adopting the recommended option, or arbitrating between specialists; each is in `production/session-logs/decision-log.md`. Numbers marked **PROPOSED** are new to this GDD, not in the spec. Units: Sim positions in mm (int32), speeds in mm/s, multipliers in permille (‰), time in 30 Hz ticks; the wire uses cm (Network).*

### Core Rules

**P1 — The body.** Each player is one Sim entity (EntityId class `player`) with a vertical capsule, radius `capsuleRadiusMm = 300`, height `capsuleHeightMm = 1400` **PROPOSED** (art bible §5: 1.4 m, 2.5 heads; the M-1a prototype's 1.8 m capsule is superseded). It has a position (feet, mm), a **body yaw** (where the carrier faces; drop position and carry pose use it) and an **aim** (camera yaw and pitch, used by focus and Throwing). Hat, face, squash and carry pose are presentation.

**P2 — Authority and stepping (spec 13 §3) [D].** Movement is **client-authoritative**: the owning client steps its own carrier and replicates the transform; nobody corrects it. The controller is a deterministic kinematic solver in the plain-C# Sim (`Players.Step`, spec 07 §3–4): capsule against the World Generation height field (2 m tiles, bilinear height), static boxes and vehicle OBBs (P4) — **no Godot physics body**, so the same code drives the owning client, the host's own carrier, headless bot clients and xUnit tests. The owning client steps it once per 30 Hz tick with that tick's input; the host **never steps an Alive remote player's motion** — it stores the last reported transform (P13, Network). **Carve-out:** the host authors and steps bodies whose owner cannot — **Dead** (held where it fell) and **Frozen** (ground drop, depenetration, P19 step 3) — and broadcasts them in its transform batch, so no authority reassignment is needed; the host-role `Players.Step` runs in Simulation Core phase 3. **Tick-time inputs only:** `Players.Step` reads other players' capsules and vehicle poses through injected `IRemoteCapsuleSource` / `ISupportTransformSource` (latest *received* snapshot — on the host, the T−1 ingest — never render-time interpolation), so a recorded script replays bit-identically (PCM-P2-1). The Presentation renders the local carrier from the latest tick, **extrapolated horizontally only** by elapsed frame time × velocity for at most one tick and clamped to that tick's swept result (never into a wall or below ground; vertical is not extrapolated), and applies camera look every rendered frame, so look latency is one frame and move latency is ≤ 1 tick + 1 frame (Game Feel). **Input is latched per edge** by Input & Controls' router (R11: ordered edges, consumed once by the next tick, attached to the first tick of a catch-up batch), so a sub-tick tap is never lost. **Tick driver** (Grid Inventory OQ20 → ADR): a custom fixed-step accumulator with a per-frame catch-up cap; engine physics interpolation is UNVERIFIED for 4.7 and not relied on (4.5 removed `RenderingServer.instance_set_interpolated` — `deprecated-apis.md`). Godot physics, Jolt included, plays no part in player movement.

**P3 — Locomotion.** Three gaits: **walk** (default), **sprint** (held; no stamina — weight is the cost of moving, and a stamina bar would be the upkeep the anti-pillars forbid **[D]**) and **swim** (P6). Final speed is F1: the locked base (walk 5000, sprint 7500, swim 2000 mm/s) × Grid Inventory D1's `speedPermille` (1000 while mounted) × a perk buff permille, one division at the end. Velocity approaches the target by F2 (ground 4 ticks to full speed, 3 to stop; half control in the air). Sprint needs a move input, cannot start in the air but persists through a jump, and is ignored while swimming, mounted or dead, **and in Panel context** (Shift is the quick-move modifier there — Input & Controls OQ-IC-1(e); the carrier walks with a panel open, raid-escape risk tracked as Input & Controls OQ-IC-4); Input & Controls offers Hold, Toggle and Auto sprint modes. Movement input is **camera-relative**. The body yaw turns toward the move direction at `turnDegPerSec = 720`; while Throwing's aim is held or a turret is operated, body yaw locks to camera yaw. **Movement never stops in the World, Panel or Holding contexts** (Grid Inventory UI Requirements).

**P4 — Ground, slopes, steps and colliders.** *Terrain contact* is a feet-point sample of the bilinear height (F7's `terrainH`); the capsule radius applies only against boxes and blocked tiles. *Slope* is the bilinear patch's analytic gradient at the feet point, in permille: terrain is walkable iff `gxPm² + gzPm² ≤ slopeMaxPm²` with `slopeMaxPm = 700` (tan 35° ≈ 0.700 — World Generation's cliff threshold, so solver and buildability agree); steeper terrain is a wall the capsule slides along and down at the F3 **steep-slope slide** rate (ruling 2026-10-16). *Colliders:* World Generation's blocked-tile flags, and static AABBs registered by their owners — constructs, houses, fences, resource nodes; vehicles are **OBBs** (yawed boxes); players are F6; enemies, items and props are not colliders. The carrier steps up box tops ≤ `stepUpMm = 400` without jumping (belts, kerbs, low constructs — terrain itself is ramps) and, **per sub-step**, snaps down drops ≤ 400 mm while grounded. Box and vehicle tops are walkable. **Tick phase order:** (1) input and body yaw; (2) F2 velocity; (3) support carry (P7); (4) gravity (F3); (5) sweep the displacement in **sub-steps of ≤ 300 mm** (one capsule radius; ≤ 3 slide iterations per sub-step), with step-up and snap-down in each; a ceiling hit zeroes upward vy; (6) player separation (F6); (7) clamp 1 m inside the map rectangle (outward velocity zeroed); (8) water state (F7, applies from the next tick). **Depenetration precedence:** a *static* box that appears over a capsule (a placement, a rebuilt wall) lifts the carrier onto its top if ≤ 3 m up, else moves it to the nearest walkable point; a *vehicle* OBB overlapping a capsule pushes it out along the nearest face normal by the whole overlap that tick (never onto a moving roof, never into swim depth). Building should still refuse placements over a living capsule (OQ18). Fixed-point trig (the yaw LUT and Q-format for camera-relative input and F4's forward vector) and the solver's number types → ADR; the Sim uses no floating-point maths.

**P5 — Jump.** Jump height 1.2 m (spec 11 §8, locked) under one "game gravity" of 24 000 mm/s² (F3): apex in 9 ticks, 19 ticks in the air. A buffered press (≤ 4 ticks before landing) fires on the landing tick; coyote time lets a jump start up to 4 ticks after leaving an edge. **Bunny Hop** (perk) grants one extra mid-air jump at 900 ‰ launch speed (≈ 0.97 m) — it **sets** vertical speed (never adds), so it is identical at any fall speed — reset on any landing, belt or water. **`maxExtraJumps` is capped at 0–1 for v1** (ruling 2026-10-16, TD-072(2)): one `ExtraJumpUsed` flag is the whole state; a counter is built only when a GDD change grants more than one. **Press resolution (ruling 2026-10-16, OQ27):** an allowed press resolves in this order — (1) Grounded, or Airborne with coyote left → ground jump at v0; (2) Airborne, extra jump available and **not near support** → the extra jump fires on the press tick; (3) otherwise the press arms the buffer, and a landing inside the window fires a ground jump at v0 (the extra jump stays unused and resets on that landing). *Near support* means GroundResolve at the press tick's start position (X, Y, Z) reports **walkable** support no more than F3's `landProbeMm` below the feet; steep terrain is never near support. **Expiry fallback:** if a press deferred by (3) while the extra jump was available reaches buffer expiry still Airborne (it drifted off the support, or the support moved), the extra jump fires as the buffer ages 1 → 0 at the end of the 4th tick after the press (Vy = v0Extra, RemY 0, arc from the next tick). No extra state is needed: a buffer expiring Airborne with the extra jump still available can only be a deferred press. Weight does not change jump height (speed is already the weight cost). Jump is live in World and Holding; it is disabled in Panel (Grid Inventory), while swimming, mounted or dead. **No fall damage** (spec 13 §4.5): a landing is a squash, never a slow-down or a stun **[D]**.

**P6 — Water.** Water depth at the feet is `max(0, −terrainHeight)` (sea level 0), evaluated **only when the carrier is over terrain** — a carrier standing on a construct, quay or vehicle roof above water stays Grounded. Below `swimEnterMm = 900` the carrier **wades**, slowed linearly to at most 200 ‰ (F7); at ≥ 900 mm it **swims**, and it leaves swimming only at ≤ `swimExitMm = 700` (hysteresis, so a shoreline never flickers). Swimming is surface-only (no diving), weight-scaled (Grid Inventory R16), with **no drowning** (spec 13 §4.5). While swimming the carrier cannot jump, sprint, build, throw, deliver, use a tool or weapon, or `drop` (Grid Inventory rejects it with `swimming`); every world interaction is unavailable with the reason "Dry land required" unless its `IInteractable` declares `allowWhileSwimming` — in v1 only **Open on a pile** and **Pick up on a Spill heap** do, because a knockdown at sea can leave a pile on the water surface (P15); a swimmer cannot **catch** (Throwing & Catching: "Dry land required"). Hotbar select, the own panel, chat, ping and look stay live. **Haul-out:** a swimmer pushing toward a walkable surface whose top is ≤ `haulOutMm = 1200` above the water surface for 15 consecutive ticks is lifted onto it, so a cliffed coast or a quay never traps anyone.

**P7 — Supports: belts and vehicle roofs.** A grounded carrier standing on a running belt adds the belt lane's velocity (2 / 4 / 6 m/s by tier, plus +50 % on a segment someone is pushing, spec 04) to its own ground velocity and can still walk; this is a legitimate fast-travel, but the **sum of belt carry and own velocity is capped at `supportSpeedCapMm = 12 000` mm/s** **PROPOSED** (below the truck's road speed), so a pushed T3 belt plus a buffed sprint (19.5 m/s) never outruns the vehicles (Pillar 3). On a moving vehicle roof the carrier's position is held **in the vehicle's local frame** (the vehicle's yaw change rotates it), computed from the vehicle pose of the latest received snapshot (`ISupportTransformSource`, P2) and replicated as support-relative (Network); the roof carries the rider at the vehicle's speed, outside the cap. Jumping off any support keeps the support's horizontal velocity in the air (decaying at air control). Landing on a support normally re-bases horizontal velocity to "support + input" on the first grounded tick; **Bunny Hop's "jumping onto a belt keeps momentum"** is read as: with the perk, that re-base decays over the deceleration time instead of applying at once. Vehicles never damage players (spec 13 §4.4); a carrier overlapping a vehicle's body is pushed clear by the whole overlap in one tick (P4 precedence).

**P8 — Players are solid (spec 13 §4.5) [D].** Each owning client resolves **only its own capsule** against the other players' latest received capsules (`IRemoteCapsuleSource`, F6): each side moves itself away by half the overlap, capped at 60 mm per tick, with a 10 mm dead zone so a doorway crowd settles instead of jittering. **Nobody is pinned:** two capsules that stay overlapping by more than the dead zone for `ghostAfterTicks = 45` (1.5 s) while neither moves more than 100 mm become mutually non-solid until they fully separate (a friend being shoved along is moving, so a shove never ghosts), so a head-on meeting in a 1 m door or a teammate parked in the Intake doorway always resolves. No client ever writes another player's transform, so a push needs no extra message (a "Shove" RPC was rejected). A push may move a carrier off a ledge — there is no fall damage, that is the gag — but **never into water at swim depth**. Dead carriers have no collider; **On-break (Frozen) bodies are non-solid** (an explicit exception to spec 13 §4.5, so a disconnected friend never blocks a mailbox for two minutes **[D]**); a mounted carrier is part of its vehicle. A bump never drops, damages or moves an item.

**P9 — Camera (Presentation only, never replicated).** Third-person, over the shoulder, behind your own carrier (art bible §5): pivot 1.6 m above the feet, boom 3.0 m, shoulder offset +0.6 m (swappable), pitch −60°…+70°, vertical FOV 80° by default (settings 60–100°, spec 08 profile `fov`), sensitivity and invert per device. Position follows with a ≈ 0.08 s critically damped spring; rotation is never smoothed; Sim-side look writes (`Teleport` yaw, mount, dismount) re-seed Input & Controls' look adapter through `SetLook`. While Throwing's aim is held, Throwing's aim profile applies (values owned there): shoulder +0.75 m and boom 2.4 m eased in over 0.1 s, FOV unchanged by default (−10° is an optional setting), look sensitivity × 0.6 (`aimCamMult`); a camera pull-in never moves the aim vector. The boom is solved in C# against the same height field and construct AABBs the Sim uses (no terrain colliders), radius 0.25 m, pulling in within 0.05 s and easing out over 0.4 s; it ignores players, items and props. Below 0.8 m from the head the carrier dither-fades instead of clipping; houses and tall constructs between camera and carrier screen-door dither (technical-artist; → ADR, VERIFY-IN-ENGINE). Swimming lowers the pivot 0.5 m and the camera never goes below the water plane. Mounted uses a per-vehicle orbit profile (Vehicles). Comfort options: sprint FOV kick (+4°, can be turned off), landing dip 0–100 %, no head bob by default, reduced shake (art bible §7).

**P10 — The interaction framework: `IInteractable`.** Anything a carrier can "press E on" registers one or more **interaction anchors** in the Sim:

| Field | Meaning |
|---|---|
| `entityId`, `slot` | the owner entity and which anchor on it (a truck has seat 0, seat 1 and its cargo face) |
| `anchor` | a point in mm; range is measured to it, never to a mesh hit point (Grid Inventory R10's anchors: a construct's tile centre, the Intake / PO Depot consoles) |
| `verb` | one value from the closed verb catalogue below |
| `mode` | `Tap` (fires on press) · `Hold(holdMs)` (fires when the hold completes) · `Channel` (active while held, e.g. belt push, catch) |
| `rangeMm` | default 2 500 (spec 11 §8) |
| `priority` | 0–3 focus bias (F4; default Deliver 2, Open / Pick up / Mount 1, Push / Harvest / everything else 0, so a belt beside a mailbox never steals the deliver); catch is above everything (P11) |
| `IsAvailable(actor, context)` | `ok`, or a reason key the prompt shows ("Close panel to deliver", "Dry land required", "Shop opens at Prep") |
| `GetHoldTicks(latch)`, `ShouldCancel(latch) → reason?` | optional per-press hooks for Hold verbs: the hold length latched at press (default `holdEff(holdMs, insertPm, holdSetting)`) and owner-specific cancels, both called by P12's hold counter (Manual Delivery MD1, MD4) |
| prompt data | verb, target name key and arguments supplied by the **owning system** (Manual Delivery supplies both addresses with ✓/✗; this system never builds address text) |

**One anchor offers one verb in a given context** — Grid Inventory's rule "World Interact is one verb per target type" is enforced at registration (it fails with `duplicate_verb` in every build configuration), so a target never needs a tap-or-hold guess and the press is classified before it happens. The Sim interface returns data only; the Presentation turns it into prompt text and glyphs.

**v1 verb catalogue** (owners implement `IsAvailable` and the effect; this GDD owns only the routing):

| Verb | Target | Mode | Owner of the effect |
|---|---|---|---|
| Deliver | mailbox, mail room, PO box bank | Hold 400 ms (F4; Quick Hands faster) | Manual Delivery → Mail & Destinations (`deliver_matching`, Grid Inventory D-B) |
| Open | chest, depot, Intake / PO Depot console, a pile (bag), parked vehicle cargo face | Tap | Grid Inventory `open` |
| Pick up | WorldItem, Spill heap | Tap | Grid Inventory `pickup` |
| Catch | an incoming thrown item ≤ 1.5 m | Channel (the Interact hold) | Throwing & Catching |
| Mount | a vehicle seat (driver or passenger) | Tap | Vehicles & NPC Drivers (a player taking a driver seat moves the NPC to passenger, spec 04) |
| Operate | turret | Tap | Defenses / Player Combat |
| Configure | Address Sorter console, depot route console | Tap | Belt Logistics / Vehicles (opens a panel → Panel context) |
| Shop | PO shop counter | Tap | Economy, Wallet & Shop |
| Announce | PO PA microphone (its own anchor ≈ 1 m from the counter) | Tap | Proximity Voice (10 s island-wide, 30 s cooldown) |
| Push | a belt segment | Channel | Belt Logistics (+50 % while held) |
| Harvest | resource node | per Resource Harvesting | Resource Harvesting |
| Rebuild | a ruin (Prep only) | Hold (Defenses sets the time) | Defenses & Construct Damage |
| Deconstruct | a construct, **in build mode only** (focus filter switches to constructs) | Hold (Building sets the time) | Building System |
| Dismount | your own seat or turret, while Mounted | Tap | this system + Vehicles |

There is no revive verb (a knockdown is not a downed state). A new verb is added by the system that owns its effect, by adding a row here (→ Content & Data's verb enum).

**P11 — Focus.** Each tick the owning client picks **at most one focused anchor** from a 2 m spatial hash of anchors: candidates within `rangeMm` of the carrier's **chest point** (feet + 1000 mm — the same point the host measures from, F5; an incumbent keeps focus out to range + 400 mm), inside a horizontal cone of **0.9 × the camera's horizontal half-FOV** (cos 635 ≈ ±50° at the default FOV 80 and 16:9 — never wider than the screen) or closer than 800 mm, and not occluded — the chest-to-anchor segment must not cross terrain or a solid box other than the target's own (client-side only; the host has no line-of-sight check; occlusion is tested lazily in score order until one candidate passes). The lowest F4 score wins; an incumbent gets a small stickiness bonus and a challenger takes focus on the 4th consecutive tick it leads (immediately when there is no incumbent), so focus does not flicker between two mailboxes. Input & Controls reads the **last presented focus snapshot** (anchor, verb, mode, availability, prompt data) through `IInteractionQuery` and latches it at press (its G1). **Focus is latched while Interacting** (a hold or channel in progress, or toggle-armed): no challenger can take it and occlusion does not drop it; only P12's cancel list ends the hold, so the prompt you hold is always the mailbox you deliver to. **Catch at press:** the verb is resolved and **latched at press** (Input & Controls `interactResolutionOrder`): if a catchable item is inbound in Throwing's catch window, the press is a Catch; else it is the focused anchor's verb. **A latched deliver hold is never converted to a catch mid-hold** (honest latch — the item lands normally; Input & Controls OQ-IC-1(a), adopted 2026-10-16). Throwing & Catching owns what a catch does. In Panel context focus is still computed **for display only** — the prompt shows dimmed with "Close panel to deliver" and Interact is refused; focus is cleared while Dead; while Mounted the only candidate is your own seat's Dismount; in build mode only Deconstruct anchors are candidates. A focused target that is unavailable still shows its prompt, dimmed with a lock icon and the reason — never hidden, never colour-only.

**P12 — Press, hold, channel.** A Tap verb sends its request on press. A Hold verb fills from press — the press tick counts as held tick 1 — one step per tick, and sends **one request on the tick held ticks reach `holdTicks`** (the anchor's `GetHoldTicks(latch)`, by default Input & Controls `holdEff`, the single source; latched at press) — P12 is the single owner of the hold counter; the hold is timed by the client, as spec 06 §4.3 already had it; there is no host-side hold timer **[D]**. A Channel verb sends start and stop. A hold **is cancelled by**: release; the focused anchor leaving the incumbent range (2.9 m — Input & Controls `holdCancelRange`, whose `cancelHysteresis` is `incumbentSlackMm`); the target becoming unavailable; the anchor's `ShouldCancel(latch)` returning a reason (checked every held tick); a context change into Panel, Mounted or Dead; entering swim depth; the carrier's knockdown. A hold is **not** cancelled by walking (movement never stops) or by taking non-lethal damage **[D]**. Accessibility: Input & Controls owns "hold-to-interact as toggle" (spec 09 §5 — a press arms the hold, a second press or leaving range cancels) and the hold-length setting (0.2–1.0 s, default 0.4, creative-director directive), both inside `holdEff`; the request sent is identical. Input & Controls calls a Channel verb a "Held state" — same thing.

**P13 — Host-side validation (consistency, not anti-cheat; spec 13 §4.3).** Every interaction request is queued and applied in `Inputs.Apply` (one order with all other ops). The host checks, in order: the actor is Alive (`dead`), not on break (`frozen`), the target exists (`no_target`), its `IsAvailable` on the host's state (`unavailable` + the reason key), and range by **F5** — the host's view of a player's position is **the last transform that player reported**, kept in a short per-player history, and every range-gated request carries the **position the client claimed at press**; it passes if the claim's chest point is within `rangeMm + 500 mm` of the anchor *and* the claim lies within 1.5 m of a reported or dead-reckoned sample from the last 300 ms (so lost packets on an approaching sprinter never reject an honest press), and is rejected `position_stale` if nothing has arrived for 1 s. This replaces Grid Inventory R10's "client-predicted, server-corrected position (ch06 §4)", which spec 13 §3 superseded — **hand-back to Grid Inventory** (Open Questions). The 3.0 m leash uses the newest reported position only, and does not fire while that position is stale. Mounted carriers use their seat's position. The reply goes to the requester only; a rejected Tap shows "Couldn't [verb] — [reason]" (one localisation key per verb × reason, named arguments) and resets the prompt.

**P14 — Input contexts and verbs this system declares (for Input & Controls).** Grid Inventory owns World, Panel and Holding; this GDD **adds two** — **Mounted** (in a vehicle seat or on a turret) and **Dead** (between knockdown and clock-in) — a hand-back amendment to Grid Inventory's context list. The two binding rules (one meaning per binding per context; no button with both a tap and a hold verb in one context) apply unchanged. Input & Controls owns concrete bindings, remap, thresholds, toggles and glyphs. The **Dead** state keeps only Pause, Back, push-to-talk, chat and the comms wheel **[D-3]** — Input & Controls R2 rule 5's Look, quick ping and Spectate while knocked down are not adopted (OQ17). The Panel cannot open while Mounted or Dead (Input & Controls R2 rule 5). Input & Controls' **Build** body state is World with only Deconstruct anchors focusable (P11); Deconstruct's binding is Input & Controls' (hold RMB / LT, OQ-IC-13 — Building confirms).

| Verb | World | Panel | Holding | Mounted | Dead | Notes |
|---|---|---|---|---|---|---|
| Move | ✓ | ✓ | ✓ | vehicle's controls (Vehicles) | — | camera-relative |
| Look | ✓ | mouse: suspended while the cursor is in UI; gamepad: right stick belongs to the grid | ✓ | orbit | — (camera auto-orbits your pile) | |
| Sprint | ✓ | — (walk; Shift is quick-move) | ✓ | Vehicles may reuse (bike sprint) | — | Hold / Toggle / Auto modes |
| Jump | ✓ | disabled | ✓ | — | — | no-op while swimming |
| Interact (tap / hold / channel by target) | ✓ | disabled | ✓ (panel closed) | Dismount only | — | mode comes from the focused anchor (P10) |
| Shoulder swap | ✓ | — | ✓ | ✓ | — | tap (X / RB; Input & Controls) |
| Comms wheel — pings, Say, emotes (Input & Controls; Comms owns the set; list alternative Pause › Comms) | ✓ | — | ✓ | ✓ | ✓ (pings anchor at your pile, Say; emotes greyed) | movement input cancels a full-body emote; "get my pile" is a ping |
| Respawn | — | — | — | — | — | automatic, never a press **[D]** |

**P15 — Knockdown (death).** Health & Damage owns HP, damage and the death event; this system owns what follows. On `PlayerDied{playerId, position, causeActorId, causeKind}`, read as a cursor reader in Simulation Core phase 11 (`Combat.React`), in the same tick and in this order: (1) cancel any hold or channel; (2) close the carrier's external container and panel (Grid Inventory R10); (3) eject from any seat or turret to the seat's exit point — Vehicles reassigns an NPC driver; (4) call Grid Inventory **`SpillToBag(player, bagPosition)`** — the cursor entry, hotbar (unless Second Wind), inventory and backpack go 1:1 into a pile bag; the bag position is the knockdown position, or, if that point is at swim depth, the nearest walkable dry point within 6 m, else the water surface (**amendment to Grid Inventory R13's "at the death location"** — hand-back); (5) set life state **Dead**, start the clock-in timer (F8: 10 s, 3 s with Second Wind), turn the collider off, clear Postal Sprint; (6) emit `CarrierKnockedDown{playerId, position, teleportSeq, causeKind, hasPile, bagId, bagPosition, bagExpiresTick}` for HUD, Audio and telemetry. `position` is the **host's view** at knockdown (the pile's origin) and the event bumps `teleportSeq`: every client, the owner included, snaps the body there, and the host authors the Dead body from then on (P2). An unknown or absent `causeKind` reads as "knocked down" with no cause. **Individual knockdowns never end the run, and if every carrier is down the run continues and timers proceed** (spec 01). An empty spill creates no bag (Grid Inventory R13).

**P16 — While knocked down.** The player keeps the Dead state's verbs (P14 **[D-3]**): chat, push-to-talk and the comms wheel ("get my pile" is a ping), plus Pause and Back. The camera shows the knockdown for 1.5 s, then auto-orbits the player's own pile (no spectate cycling in v1 **[D-3]**; with no pile, the knockdown spot). The HUD shows "Clocking back in… 7" (seconds rounded up) with a bar, and the pile readout **"Your pile · 27 s · 80 m"** — its remaining seconds and the straight-line distance from the Spawn pad, both from the `CarrierKnockedDown` bag fields — with no reachability verdict (a straight line over water or houses cannot honestly promise "reachable" **[D-2]**). After clock-in the readout switches to the live distance from the carrier. The carrier cannot be damaged, targeted, pushed or interacted with.

**P17 — Clocking back in (respawn).** When the timer expires the host: (1) picks the respawn point — **v1 has exactly one, the Post Office's pre-built Spawn pad** (spec 10 §2.6); a buildable pad is not in the v1 construct list and is not added here **[D]** (Open Questions); (2) picks the first of 8 ring slots 1.2 m around the pad that no living capsule overlaps, starting from a rotating index so simultaneous clock-ins fan out (F8); (3) sets Alive, asks Health & Damage to restore full HP, and starts **spawn protection** — invulnerable for `spawnProtectMs = 3000` **PROPOSED**, ended early only by a **damaging action** (an attack, or a throw that deals damage) — throwing mail never ends it — readable as a faint bubble-wrap shell (art-director proposal); (4) sends the owner `Teleport{seq, position, yaw, reason: Respawn}` (Network) — the carrier is controllable from the tick the owner applies it (movement axes at once; buttons held through the knockdown stay muted until released, Input & Controls G3), and the crate-and-lid presentation never gates input; (5) runs the standard-issue grant (P18). The hat returns with the carrier. Join-in-progress (Lobby and Prep only, spec 06 §6) spawns through the same path with the starting kit.

**P18 — Standard-issue grant (answers Grid Inventory OQ23) [D, PROPOSED].** At clock-in, if the carrier's hotbar, inventory and backpack hold **no** item of category `weapon` and no `tool` that is also a melee weapon, Grid Inventory `Grant(player, standardIssueCopy(kitMeleeItem), overflowPolicy: Reject)` gives one **standard-issue** copy of the melee item from the player's starting kit (the Land kit's Axe), hotbar-first. Standard-issue items are ordinary in every way except **sell value 0** (no farming), and at most one is granted per clock-in. If the kit has no melee item, nothing is granted (Fists remain, spec 05). Reasoning: a solo carrier (Pillar 5) must not need a perk (Second Wind) to defend the walk back; the grant restores the baseline only, so the cost of a knockdown stays what spec 01 says — time, plus any upgraded kit and ammunition stranded in the pile. Owners to confirm: Player Combat (which item), Economy (sell value 0), Content & Data (`KitDef.standardIssueItem`).

**P19 — On break (disconnect) (spec 06 §6).** When the transport reports a player gone during a run, the host takes over the body: (1) cancel holds and channels, close the external container, handle the cursor by Grid Inventory R11 (AutoPlace into the panel, else a WorldItem at the body); (2) eject from any seat beside the vehicle (Vehicles stops a vehicle the player was driving); (3) if the body is on a belt or a vehicle roof, move it to the nearest walkable ground within 3 m; (4) set life state **Frozen** for `frozenSeconds = 120`: invulnerable, **non-solid** (P8), skipped by enemy targeting, not interactable, inventory still on the body. A reconnect from the same account within 120 s (new transport peer, mapped by account, never by a client-supplied id) resumes the same entity: RejoinState (Grid Inventory) plus `Teleport` to the body; the body stays Frozen through the transfer and becomes Alive with 1.5 s of spawn protection on `JoinStateApplied` (the 120 s deadline is checked at reconnect, not at transfer end). At 120 s the body calls `SpillToBag` exactly as a knockdown (Grid Inventory R11/R13) and despawns; that player can come back as a normal join at the next Prep (spec 06 §6 — mid-Delivery entry after despawn is an Open Question for Networking & Session). A player who disconnects **while Dead** keeps the clock-in timer running: if they reconnect before it expires they resume Dead with the remaining time; if it expires first they clock in **Frozen** at the Spawn pad with the 120 s measured from the disconnect. A **kicked** player (spec 13 §4.3) and a player who **quits through the menu** spill and despawn at once, with no 120 s window (a quitter may rejoin at the next Prep; a kicked one may not). A disconnect while **Joining** or in the Lobby removes the player with nothing to spill (no body exists yet). If the host disconnects the run ends (no migration); at **Run Over** every life state, timer and pile is discarded with the run.

**P20 — Presentation-only behaviours.** Idle: after 20 s without input the carrier dozes (art bible face cell "asleep"); any input or the raid horn wakes it instantly. Emotes are upper-body one-shots this system's animation layer plays on Comms' `Emote` message (Comms owns the set and the message — one message, no second one-shot); the pose is suppressed while Mounted, swimming, Interacting or holding a medium or heavy item (both arms busy), and Comms' glyph bubble always shows; with a light held item the free arm emotes; movement cancels a full-body emote. Carry pose follows the **held** (selected) item's class; the whole-body load (knee bend, lean, backpack sag) follows `weightPoints` continuously (Visual/Audio — resolves Grid Inventory OQ18's weight items). None of these change the Sim.

### States and Transitions

The carrier has two independent state variables: a **life state** owned by the host and a **locomotion state** owned by the owning client (meaningful only while Alive). Overlays (Interacting, Protected, OnSupport, Sprinting) combine with them.

**Life state (host-owned).**

| State | Entered by | Event / guard | Next |
|---|---|---|---|
| Joining | lobby or Prep join; rejoin after despawn at the next Prep | JoinState applied; `Teleport{reason: Join}` acked | Alive (+ starting kit) |
| Joining | — | transport disconnect | removed (no body, nothing spills) |
| Alive | Joining; clock-in; resume from on break | `PlayerDied` (guard: not Protected — Health & Damage applies invulnerability) | Dead |
| Alive | — | transport disconnect | Frozen (120 s) |
| Alive / Frozen | — | kicked, or quit through the menu | Despawned (spill now) |
| Dead / Dead-and-away | — | kicked or quit | Despawned (the pile already exists; nothing more spills) |
| Dead | knockdown | timer reaches 0 (300 ticks; 90 with Second Wind) | Alive + Protected (90 ticks) via `Teleport{reason: Respawn}` |
| Dead | — | transport disconnect | Dead-and-away (timer keeps running) → at 0: Frozen at the Spawn pad, 120 s from the disconnect |
| Dead-and-away | — | same-account reconnect before the timer expires | Dead (remaining time) |
| Dead | — | a second `PlayerDied` | ignored (only Alive can be knocked down) |
| Frozen | disconnect | same-account reconnect within 120 s | Alive + Protected (45 ticks) via `Teleport{reason: Rejoin}` |
| Frozen | — | 120 s elapsed | Despawned (`SpillToBag`, then removed) |
| Despawned | — | next Prep join | Joining |
| any | host lost, or Run Over | — | run ends (spec 06 §6); life states, timers and piles are discarded. Phase changes (Prep, Payday, Draft) do not interrupt Dead or Frozen timers (absolute ticks) |

**Locomotion state (owning client; valid only while Alive).**

| State | Event / guard | Next |
|---|---|---|
| Grounded (Idle / Walk / Sprint) | jump pressed (or buffered) and context allows; or ground lost beyond the 400 mm snap (coyote 4 ticks still allows a jump) | Airborne |
| Grounded | depth ≥ 900 mm | Swimming |
| Grounded | Mount / Operate request accepted | Mounted |
| Airborne | lands on a walkable surface | Grounded (squash) |
| Airborne | double-jump (perk, once per airborne period) | Airborne |
| Airborne | enters water at depth ≥ 900 mm (vertical velocity zeroed, no damage) | Swimming |
| Swimming | depth ≤ 700 mm, or haul-out completes (15 ticks pushing at a surface ≤ 1200 mm above the water) | Grounded |
| Mounted | Dismount tap; vehicle destroyed or stalled in water (Vehicles); knockdown or disconnect | Grounded at the seat exit (or Dead / Frozen) |
| Mounted | exit point is at swim depth | Swimming (weight scaling applies again) |
| Swimming | Mount / Operate pressed | stays Swimming — `unavailable`, "Dry land required" |

**Overlays.** *Interacting* (a hold or channel in progress; cancel rules P12). *Protected* (host-owned, 90 ticks after clock-in, 45 after a rejoin; ends early only on a damaging action). *OnSupport* (standing on a belt or vehicle roof; position replicated support-relative). *Sprinting* (input state, not speed — a loaded sprint can be slower than an empty walk, so it is replicated as its own bit).

### Interactions with Other Systems

| System | Direction | Interface / data |
|---|---|---|
| Simulation Core | upstream | 30 Hz tick, entity ids (class `player`), event bus; interaction requests queued into `Inputs.Apply`; per-player position history kept by the host; host-role step in phase 3, knockdown chain in phase 11, `IPlayersApi.Teleport` and the `TeleportAck` OpKind (HB5); tick driver and thread → Grid Inventory OQ20 |
| Input & Controls | upstream | action states per context for the verbs in P14; hold-as-toggle and hold-time scale; sprint toggle/auto; device glyphs. This GDD declares verbs and the Mounted / Dead contexts; Input & Controls binds them |
| World Generation | upstream | runtime height field (2 m tiles, bilinear, sea level 0), tile walkability and slope flags, the PO Spawn pad position; the routing graph is not used for player movement |
| Grid Inventory & Containers | both | reads `weightPoints` → D1 `speedPermille` (1000 when mounted); calls `open` (Tap on containers and piles), `pickup`, `SpillToBag(player, bagPosition)` on knockdown and at on-break expiry, `Grant` for the standard-issue item; supplies the host's position for R10 range and leash (F5) and the swimming flag for `drop`. Amends R10 wording, R13 bag position on water, the context list (+Mounted, +Dead) and adds reason `position_stale` (hand-backs in Open Questions) |
| Health & Damage | upstream | HP 100, regen and damage are owned there; this system consumes `PlayerDied`, requests full HP on clock-in, and sets the Protected and Frozen flags that make the carrier invulnerable (the invulnerability itself is applied by Health & Damage); `spawnProtectMs` value is proposed here for Health & Damage to confirm |
| Networking & Session | upstream | transport, account → peer mapping, disconnect detection, JoinState / RejoinState envelope, the adapter for unreliable transform streams and reliable events (Network section) |
| Content & Data Definitions | upstream | the `player.yaml` balance keys (Tuning Knobs), the verb enum, `KitDef.standardIssueItem` (with Grid Inventory's entry flag, P18); stats `MoveSpeedAddPm`, `ExtraJumps`, `DeliverInsertPm`, `RespawnMs` (CD11) |
| Perks & Perk Draft | upstream (soft) | Long Legs, Postal Sprint, Bunny Hop, Second Wind, Strong Back (via D1), Quick Hands (`insertPm` 500) — as StatSheet modifiers only |
| Manual Delivery | downstream | registers the Deliver verb on mailboxes with the `GetHoldTicks` / `ShouldCancel` hooks and supplies the prompt (both addresses, ✓/✗); receives the completed hold as one request; the swimming and Panel availability reasons come from this framework |
| Mail & Destinations | downstream (via Manual Delivery) | `deliver_matching` orchestration; `DeliveryOutcome{result: accept}` with `actorPlayerId` = this player (its Deliver verb or its accepted throw) starts that player's Postal Sprint — belt, NPC and teammates' deliveries never do |
| Throwing & Catching | downstream | reads aim (camera yaw/pitch) and body position for the launch; Catch is a Channel verb resolved at press, never converting a latched hold (P11); swimming blocks Throw and Catch; catches are tested against F5's history; owns the aim camera values (P9) and `ThrowLaunched` |
| Vehicles & NPC Drivers | downstream | Mount / Dismount verbs, seat exit points, the Mounted context and camera profile, vehicle AABBs and roofs as supports, ejection on knockdown/disconnect, water-stall teleport uses the `Teleport` path |
| Player Combat | downstream | attack/tool use is live only when Alive, grounded or airborne and not swimming; a damaging action ends spawn protection; the standard-issue melee item |
| Enemy AI & Waves | downstream | player targets: Alive and not Frozen (Protected may be skipped — their call); positions read from the host's last reported transforms |
| Resource Harvesting, Building System, Belt Logistics, Defenses, Economy, Proximity Voice | downstream | register their verbs (Harvest, Deconstruct, Push, Configure, Rebuild, Operate, Shop, Announce) through `IInteractable` |
| HUD & Notifications, Comms | downstream | interaction prompt, hold ring, focus bracket (this system supplies anchor and bounds), clock-in countdown, pile marker and distance, on-break name tags, knockdown feed lines; Comms owns the wheel set and the `Emote` message (P20) |
| Audio, Presentation & World Dressing | downstream | animation states and one-shot events (Visual/Audio Requirements) |

## Formulas

*Specialist: systems-designer (structure and derivations), adjusted where the main session arbitrated (logged): Long Legs uses spec 10's +12 %, the buff cap is 400 ‰ to match the animation playback clamp, accel ticks 4/3, air control 500 ‰. All arithmetic is integer; every product is cast to **int64 before its first multiplication** (F1; F4's dot product) and composed multipliers take **one** final division (Grid Inventory D1's rule); distances are compared squared where only a comparison is needed. Movement formulas run on the owning client; F5 runs on the host. Variables marked `data file` are tuning knobs (`player.yaml`, Section G).*

### F1 — Final speed

The `carrierSpeed` formula is defined as:

```
weightPm  = mounted ? 1000 : speedPermille                          (Grid Inventory D1, 600–1000)
addPm     = longLegsPm × longLegsStacks + (postalTicksLeft > 0 ? postalPm : 0)
buffPm    = 1000 + min(addPm, buffCapPm)
wadePm    = F7 (1000 unless wading)
speedMmS  = base × weightPm × buffPm × wadePm / 1_000_000_000         (int64; one division)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| base | int | 5000 / 7500 / 2000 | data file (spec 11 §8, locked) | walk / sprint / swim base, mm/s |
| speedPermille | int | 600–1000 (800–1000 Strong Back) | calculated (Grid Inventory D1) | weight multiplier; not applied while mounted |
| longLegsPm | int | 120 | data file (spec 10: +12 % per stack) | Long Legs, per stack |
| longLegsStacks | int | 0–3 | perk state (spec 10 maxStacks 3) | stacks held |
| postalPm | int | 0 or 300 | data file (spec 10: +30 %) | Postal Sprint while active |
| postalTicksLeft | int | 0–120 | calculated | set to 120 (4 s) by each of this player's own accepted deliveries; refreshes, never stacks; cleared on knockdown |
| buffCapPm | int | 400 | data file **PROPOSED** | cap on the summed buffs (max 1.4×) |
| wadePm | int | 800–1000 | calculated (F7) | wading slow; 1000 on dry ground and while swimming |

**Output Range:** walk 3000 (floor weight, no buffs) – 7000 mm/s (2400 at the floor weight in the deepest wade); sprint 4500–10 500; swim 1200–2800. Max self-propelled step 350 mm per tick (support carry and falls add to it — swept in sub-steps, P4). Buffs apply to walk, sprint and swim alike; mounted riders use the vehicle's speed (Vehicles), not F1.
**Example:** sprint, P = 20 → weightPm 800; Long Legs ×2 = 240 + Postal Sprint 300 = 540 → capped 400 → buffPm 1400 → `7500 × 800 × 1400 × 1000 / 10⁹ = 8400 mm/s`.
**Degenerate:** summed buffs above the cap are wasted — already one Long Legs stack plus Postal Sprint (420) exceeds 400, so the Perks GDD must say so in the card text (Open Questions); StatSheet inputs are clamped (`longLegsStacks` ≤ 3, `addPm` ≥ 0); Postal Sprint's timer still runs while capped. A per-tick step is `speedMmS / 30` with the remainder carried to the next tick so integer division never drifts (5000 mm/s = 166, 167, 167 mm per tick). Max product 7500 × 1000 × 1400 × 1000 = 1.05 × 10¹³, inside int64.

### F2 — Acceleration, deceleration, turning

The `carrierAccel` formula is defined as:

```
T        = hasMoveInput ? ticksAccel : ticksDecel
Tair     = ceil(T × 1000 / airControlPm)                       (used while airborne)
dvMax    = ceil(max(vRef, |v|) / (airborne ? Tair : T))        (mm/s per tick; |v| = own horizontal velocity, support carry excluded)
v'       = v + clampLength(vTarget − v, dvMax)                 (one clamped horizontal vector per tick; with input present T = ticksAccel, so a full reversal takes 2 × ticksAccel = 8 ticks)
yawStep  = floor(65536 × turnDegPerSec / (360 × 30))           (uint16 yaw units per tick; 4369 at 720°/s)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| ticksAccel | int | 4 | data file **PROPOSED** | ticks from 0 to full speed (0.13 s) |
| ticksDecel | int | 3 | data file **PROPOSED** | ticks from full speed to 0 (0.10 s) |
| airControlPm | int | 500 | data file **PROPOSED** | air control relative to ground |
| vRef | int | 0–10 500 | calculated (F1 for the current gait) | the speed cap acceleration is measured against |
| vTarget | vec | \|v\| ≤ vRef | calculated | camera-relative input direction × vRef (zero without input) |
| turnDegPerSec | int | 720 | data file **PROPOSED** | body yaw turn rate toward the move direction |

**Output Range:** own input never raises \|v\| above max(vRef, previous \|v\|) — a cap drop (heavier bag, deep water) is reached without a skid but takes more than T ticks, because dvMax is recomputed each tick from the shrinking \|v\|; ground: full speed in 4 ticks, stop in 3 from any speed ≤ vRef (a sprinter stopping from 7500 takes 4: 5000, 3333, 1666, 0, since the sprint flag clears with the input and vRef falls to the walk 5000); air: 8 and 6. (corrected 2026-10-16: per-tick dvMax, see ADR-0013 §2)
**Example:** walk 5000 → dvMax = 1250 mm/s per tick → full speed on tick 4. Sprint at weight 600 (4500) → dvMax 1125 → also 4 ticks: weight lowers the cap, never the responsiveness. Turning: 720°/s = 24° per tick, 180° in 8 ticks.
**Degenerate:** vRef = 0 (impossible; F1's minimum is 1200) still stops the carrier in T ticks because dvMax uses \|v\|; dvMax = 0 only when both are 0. Airborne without input, horizontal velocity decays to 0 over Tair = 6 ticks. Example of the cap drop: a 10 500 mm/s sprinter entering swim depth (vRef 2000, T = 4) gets dvMax 2625, 1969, 1477, 1108, 831 and reaches 2000 on tick 6 (speeds 7875, 5906, 4429, 3321, 2490, 2000). (corrected 2026-10-16: per-tick dvMax, see ADR-0013 §2)

### F3 — Jump

The `carrierJump` formula is defined as:

```
v0      = isqrt(2 × g × jumpHeightMm)
v0Extra = v0 × extraJumpPm / 1000                     (Bunny Hop mid-air jump)
per tick: vy' = max(vy − g / 30, −terminalMmS);  n = vy + vy' + rem;  dy = n / 60 (floor);  rem = n − 60 × dy     (trapezoid with the remainder carried, as F1, so no per-tick truncation drift)
apexTick = floor(v0 / (g/30));  airTicks ≈ ceil(2 × v0 / (g/30))  (flat ground)
landProbeMm = max(0, (n² × (g/30) − 2 × n × vy) / 60),  n = jumpBufferTicks + 1     (P5 "near support": the F3 trapezoid fall over the press tick plus the buffer window from vy at tick start; terminal clamp and RemY ignored, so at terminal speed it over-reaches by ≈ 333 mm — the expiry fallback covers that)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| jumpHeightMm | int | 1200 | data file (spec 11 §8, locked) | jump height |
| g | int | 24 000 mm/s² | data file **PROPOSED** | "game gravity" ≈ 2.45 g for a snappy arc |
| extraJumpPm | int | 900 | data file **PROPOSED** | mid-air jump launch speed |
| extraJumps | int | 0–1 | StatSheet (Bunny Hop) | mid-air jumps per airborne period |
| maxExtraJumps | int | 0–1 (default 1) | data file; ContentValidator **error** above 1 (ruling 2026-10-16) | cap applied as `min(extraJumps, maxExtraJumps)`; any stat ≥ 1 grants exactly one mid-air jump |
| terminalMmS | int | 20 000 | data file **PROPOSED** | fall speed cap |
| coyoteTicks | int | 4 | data file **PROPOSED** | jump still allowed this long after leaving ground |
| jumpBufferTicks | int | 4 | data file **PROPOSED** | a press this early before landing fires on landing |

**Output Range:** v0 = 7589 mm/s; apex **1196 mm** at tick 9 (exact value 1196.7, floored; per-tick truncation without the remainder would give 1191); lands on tick 19 (0.63 s). Mid-air jump v0Extra = 6830 → 969 mm more. `landProbeMm` (n = 5, g/30 = 800): vy 0 → 333; vy −3000 → 833; vy −7589 → 1598; vy −15 000 → 2833; any vy ≥ +2000 → 0 (a press while rising always takes the extra jump).
**Example:** a carrier running at 5 m/s covers 19 × 167 ≈ 3.2 m in one jump — clears a 2 m tile gap or a belt line, not a street.
**Degenerate:** raising `jumpHeightMm` without raising `g` lengthens air time past 0.7 s (floaty); `g` below ~18 000 reads as moon gravity. A buffered press on the same tick as landing fires that tick (no lost frame). The jump rejects in Panel, while swimming, mounted or dead (P5).

**Steep-slope slide (P4; ruling 2026-10-16, TD-078(10a) — replaces the uncited "Vy zeroed, run = −g·depth·S²/(g·g)" rule, which gave a constant ≈ 400 mm/s descent: ≈ 570 mm/s across a 702 ‰ face, ≈ 80 mm/s down a 5000 ‰ cliff — sticky, and slowest exactly where a drop should be fastest).** When a fall presses the feet into non-walkable terrain, the carrier stays Airborne on the surface and slides down the fall line under gravity projected onto the face, capped so its horizontal skid never exceeds `slideRunMaxMmS`:
```
S2     = gxPm² + gzPm²                                   (at the contact point; > slopeMaxPm² by definition)
S      = isqrt(S2)                                       (permille)
aSlide = g × S2 / (S2 + 1 000 000)                       (= g·sin²θ, mm/s², one floor division, 128-bit intermediate)
vyCap  = min(terminalMmS, slideRunMaxMmS × S / 1000)
vy'    = max(min(vy, 0) − aSlide / 30, −vyCap)           (vy = Vy at tick start; replaces phase 4's vy' on a contact tick)
drop   = min(sunk depth, F3 trapezoid of (min(vy, 0), vy') with RemY carried)
run    = CarrierTerrain.DownhillRun(drop)                (horizontal = drop × 1000 / S along the downhill gradient)
```
`Vy` keeps `vy'` (it is no longer zeroed), so the slide builds tick to tick and carries into the air if the face ends in a drop. Air control is unchanged; input into the face is projected along the contour (P4). Landing at the foot is an ordinary landing — the run is displacement, not horizontal velocity, so nothing is carried.
**Output Range:** 702 ‰ (S2 492 804, S 702): aSlide 7922, −264 mm/s per tick, cap −2808 reached on tick 11 (0.37 s), then 93–94 mm down and 133–134 mm across per tick (4000 mm/s horizontal, ≈ 4.9 m/s along the face). 1000 ‰: aSlide 12 000, cap −4000 on tick 10. 3000 ‰: cap −12 000. ≥ 5000 ‰: cap = terminal −20 000 (a cliff is a fall).
**Example:** a 3 m-high 702 ‰ hillside (4.3 m across) is cleared in ≈ 38 ticks (1.3 s); the pre-ruling rule took ≈ 7.5 s.
**Degenerate:** `slideRunMaxMmS` above walk base (5000) makes a steep face a faster route than its walkable path and reads as a launch; below ~2500 the carrier sticks to faces. A slider is Airborne: it can use the extra jump (steep terrain is never "near support", P5) or a live coyote jump, never a ground jump.

### F4 — Focus score and hold length

The `interactFocus` formula is defined as:

```
d        = isqrt(Δx² + Δy² + Δz²)                       (carrier chest → anchor, mm)
dh       = isqrt(Δx² + Δz²)                                     (horizontal distance)
cosPm    = dh < nearAxisMm ? 1000 : dot(fwdQ10, Δxz) × 1000 / (1024 × dh)   (int64; horizontal camera forward, Q10; an anchor almost straight above or below counts as dead ahead)
cosMinPm = cos(focusConePm / 1000 × horizontal half-FOV)   (computed by the Presentation on FOV / aspect change, passed to the Sim as an integer)
eligible = d ≤ rangeMm (+ incumbentSlackMm if incumbent)  AND  (cosPm ≥ cosMinPm OR d ≤ nearMm)  AND  not occluded
score    = (wDist × d × 1000 / rangeMm + wAng × (1000 − cosPm)) / 1000 − 100 × priority − (incumbent ? stickyPm : 0)
winner   = lowest score; ties → lowest entityId, then lowest slot; a challenger must lead for focusDwellTicks
holdTicks = anchor GetHoldTicks(latch), default Input & Controls holdEff(holdMs, insertPm, holdSetting)   (registered formula, the single source; latched at press)
progress  = 1000 × heldTicks / holdTicks   (‰, for the hold ring)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| rangeMm | int | 2500 | data file (spec 11 §8, locked) / per `IInteractable` | interaction range to the anchor |
| incumbentSlackMm | int | 400 | data file **PROPOSED** | the focused target keeps focus to 2.9 m (also the hold-cancel range; Input & Controls' `cancelHysteresis` is this value) |
| focusConePm | int | 900 | data file **PROPOSED** | focus cone as a share of the horizontal half-FOV → cosMinPm 635 (±50°) at FOV 80 / 16:9; 526 at FOV 100, 752 at FOV 60, 549 at 21:9 |
| nearAxisMm | int | 150 | data file **PROPOSED** | below this horizontal distance cosPm is 1000 (no sign flip under an overhead anchor) |
| nearMm | int | 800 | data file **PROPOSED** | always eligible inside this distance (behind you is fine at arm's length) |
| wDist / wAng | int | 400 / 600 | data file **PROPOSED** | distance vs angle weights |
| priority | int | 0–3 | `IInteractable` | tie-breaker, 100 points per level |
| stickyPm | int | 60 | data file **PROPOSED** | incumbent bonus (small: a target dead ahead beats an incumbent ≥ 30° off-centre at equal distance) |
| focusDwellTicks | int | 4 | data file **PROPOSED** | ticks a challenger must lead to take focus |
| holdMs | int | 400 (mailbox) | data file (spec 11 §8) / per verb | hold length |
| holdSetting | s | 0.2–1.0 | player setting (Input & Controls `holdEff`) | shorter or longer holds by preference or need |
| insertPm | int | 0 or 500 | StatSheet (Quick Hands, spec 10 "−50 %"; Manual Delivery MD9; sum clamped ≤ 750) | every deliver hold, Deliver verb only; an input to `holdEff` |
| minHoldTicks | int | 6 | data file **PROPOSED** (`holdEff`'s floor) | art bible's ≥ 0.2 s anticipation |

**Output Range:** score −360 (priority 3, incumbent, dead ahead at 0 mm) to ≈ 1330 (a target behind you inside 800 mm) — lower wins. Hold ticks come from `holdEff` (one shared Sim function; floor 6 ticks, ceiling `holdCeil` 2.0 s = 60 ticks, owned by Input & Controls): mailbox 12 ticks (0.4 s), 6 with Quick Hands, 6 at the 0.2 s setting, 30 at the 1.0 s setting; a 2 s Rebuild hold at the 1.0 s setting is 60, not 150. This GDD keeps no second hold formula **[D-2]**.
**Example:** mailbox A at 1500 mm, cos 950 → `(400 × 600 + 600 × 50)/1000 = 270`; mailbox B at 900 mm, cos 600 → `(400 × 360 + 600 × 400)/1000 = 384` → A wins; once A is focused it scores 210, so B must beat that for 4 ticks to take over; a B dead ahead at A's distance (240) would not, but one 600 mm nearer (144) does.
**Degenerate:** dh < 150 mm → cosPm forced to 1000 (no division by zero, no ±1200 score swing under an overhead anchor; a roof seat directly overhead is focusable). No eligible candidate → no prompt. A catch candidate bypasses the score entirely (P11). `insertPm` near 1000 would make the hold instant — `holdEff`'s floor keeps ≥ 6 ticks.

### F5 — Host-side range check (replaces "server-corrected position")

The `hostRangeCheck` formula is defined as:

```
chest(q)   = q + (0, chestMm, 0)                      (the point F4 measures from on the client — both sides use it)
history[p] = ring of rangeHistoryTicks + 3 reported feet positions, one sample-and-hold per tick, stamped with the host tick
stale(p)   = nowTick − lastReceivedTick[p] > positionStaleTicks
dr(p)      = newest + velocity(last two samples) × min(nowTick − newest.tick, rangeHistoryTicks) / 30     (dead-reckoned sample)
inRange(p, claim, anchor, r) = NOT stale(p)
             AND |chest(claim) − anchor|² ≤ (r + rangeSlackMm)²                       (claim = the client's feet position at press)
             AND ∃ s ∈ {history[p] in the last rangeHistoryTicks} ∪ {dr(p)} : |claim − s|² ≤ claimToleranceMm²
leash(p, anchor) = NOT stale(p) AND |chest(newest(p)) − anchor| > leashMm             (Grid Inventory R10 auto-close)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| chestMm | int | 1000 | data file **PROPOSED** | height of the focus / range point above the feet |
| rangeHistoryTicks | int | 9 (300 ms) | data file (spec 06 §4.3's 300 ms) | how far back a claim may be corroborated; the ring holds `rangeHistoryTicks + 3` samples |
| claimToleranceMm | int | 1500 | data file **PROPOSED** | how far a claimed position may sit from every corroborating sample (≈ 3 lost 15 Hz packets at sprint) |
| rangeSlackMm | int | 500 | data file **PROPOSED** | margin over `r` for the claim; must exceed the client's hold-cancel slack (validator rule 9) |
| positionStaleTicks | int | 30 (1 s) | data file **PROPOSED** | silence after which range-gated requests fail `position_stale` (a still carrier still sends a 4 Hz keepalive) |
| r | int | 2500 (open, pickup, interact), 3000 (deliver, spec 06 §4.3) | per verb | |
| leashMm | int | 3000 | Grid Inventory `externalLeashMeters` | auto-close distance |

**Output Range:** boolean. A request is accepted if its claim is within `r + 500` mm of the anchor and within 1.5 m of a sample from the last 9 ticks or the dead-reckoned position. This is a consistency rule, not anti-cheat (spec 13 §4.3).
**Example:** a carrier sprinting *toward* a chest presses Open at 2.4 m; two transforms were lost, so the host's newest sample is 3.4 m out, but the dead-reckoned sample is 0.4 m from the claim → accepted (the old "any sample within range + 0.5 m" rule would have rejected it). Sprinting *past*, the claim at 2.3 m is accepted and the leash closes the panel the same tick because the newest sample is > 3.0 m — "Out of reach", which is honest. A claim 2 m from every sample fails `out_of_range`.
**Degenerate:** a lag spike > 1 s rejects new requests with `position_stale` but never auto-closes an open panel. Mounted players use the seat position. Rule 1 (`rangeMm + rangeSlackMm ≤ leashMm`) applies to container verbs, so an open never succeeds outside the leash. Every `Teleport` clears the history, **seeds one sample at the destination and sets `lastReceivedTick` to now** (Network), so no request passes on a pre-teleport position and none fails `position_stale` after a clock-in. Moving anchors (a seat on a driving truck) are a Vehicles hand-back (OQ14).

### F6 — Player push (separation)

The `carrierPush` formula is defined as:

```
dxz      = horizontal distance between two capsule axes (mm), vertical overlap required
overlap  = 2 × capsuleRadiusMm − dxz
corr     = overlap < pushDeadMm ? 0 : min(overlap × pushSharePm / 1000, pushCapMm)
move self by corr along the horizontal normal away from the other (dxz = 0 → +x if own entityId is lower, else −x)
reject the step if the destination is at swim depth (P8); ledges are allowed
mover: the velocity component toward an overlapping remote capsule is clamped to pushCapMm per tick (you push, you never pass through)
ghost: a pair overlapping ≥ pushDeadMm for ghostAfterTicks consecutive ticks, neither moving > 100 mm in that time, skips F6 until its overlap reaches 0 (P8)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| capsuleRadiusMm | int | 300 | data file **PROPOSED** | carrier radius |
| pushDeadMm | int | 10 | data file **PROPOSED** | no correction below this overlap (no jitter) |
| pushSharePm | int | 500 | data file **PROPOSED** | each side corrects half — the pair separates fully with no cross-client write |
| pushCapMm | int | 60 | data file **PROPOSED** | per tick (1.8 m/s) |
| ghostAfterTicks | int | 45 | data file **PROPOSED** | 1.5 s of unresolved overlap before a pair passes through each other |

**Output Range:** 0–60 mm per tick per carrier. Walking into a standing friend settles at ≈ 60 mm overlap with both moving ≈ 30 mm per tick (0.9 m/s) — a shove, not a pass-through.
**Example:** two carriers 500 mm apart: overlap 100 → each moves itself 50 mm away → separated after one tick on each client.
**Degenerate:** a remote capsule is the latest received sample (≈ one 15 Hz interval plus transit, ≈ 100–300 ms old), so a sprinter's view of a teammate can be ≈ 0.75–2 m behind the truth — contact feels "soft", accepted as a friendslop push; a head-on pair that never resolves ghosts after 1.5 s. Steady state with no lag (PCM-F6-1): walking into a standing friend converges to 60 mm overlap with both moving 30 mm per tick (`o' = o + 60 − min(o, 120)`). Eight carriers at a 1 m door slide past each other rather than locking, because every correction is capped and sideways components are kept.

### F7 — Water: depth, wading, swim hysteresis

The `waterState` formula is defined as:

```
terrainH = ((h00×(S−fx) + h10×fx)×(S−fz) + (h01×(S−fx) + h11×fx)×fz) / S²     (int64, S = 2000 mm tile)
depth    = max(0, 0 − terrainH)                                                   (sea level 0)
swimming' = swimming ? depth > swimExitMm : depth ≥ swimEnterMm
wadePm   = swimming ? 1000 : 1000 − maxWadeSlowPm × min(depth, swimEnterMm) / swimEnterMm
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| h00…h11 | int | mm | World Generation height field | heights at the tile's four corners |
| fx, fz | int | 0–1999 | calculated | position inside the tile |
| swimEnterMm | int | 900 | data file **PROPOSED** | depth that starts swimming |
| swimExitMm | int | 700 | data file **PROPOSED** | depth that ends swimming |
| maxWadeSlowPm | int | 200 | data file **PROPOSED** | slow at the deepest wade |
| haulOutMm | int | 1200 | data file **PROPOSED** | max ledge height a swimmer can climb onto (P6) |
| haulOutTicks | int | 15 | data file **PROPOSED** | ticks pushing into the ledge |

**Output Range:** wadePm 800–1000.
**Example:** depth 600 mm → `1000 − 200 × 600 / 900 = 867` → walk `5000 × 1000 × 1000 × 867 / 10⁹ = 4335 mm/s`.
**Degenerate:** the 200 mm band between exit and enter stops flicker on a shelving beach; an airborne carrier entering deep water zeroes vertical speed without damage.

### F8 — Clock-in timing, retrieval window, spawn slot

The `clockIn` formula is defined as:

```
respawnTicks     = ceil((secondWind ? secondWindMs : respawnMs) × 30 / 1000)
protectTicks     = ceil(spawnProtectMs × 30 / 1000)
retrievalTicks   = max(0, deathBagDespawnSeconds × 30 − respawnTicks)                  (Grid Inventory D6)
retrievalRadiusMm = walkMmS × retrievalTicks / 30          (straight-line distance from the Spawn pad — an upper bound: streets, water and goons cut it; empty-handed walk = 5000)
slotIndex        = (clockInsAtPadInLast60Ticks + i) mod 8 for i = 0..7, first slot with no living capsule overlap
slotOffsetMm[k]  = round(1200 × (cos 45k°, sin 45k°)) = (1200,0) (849,849) (0,1200) (−849,849) (−1200,0) (−849,−849) (0,−1200) (849,−849)
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| respawnMs | int | 10 000 | data file (spec 11 §8 `respawnSeconds`; owner Run Structure per Grid Inventory) | clock-in delay |
| secondWindMs | int | 3000 | data file (spec 10 Second Wind) | with the perk |
| spawnProtectMs | int | 3000 | data file **PROPOSED** (Health & Damage confirms) | invulnerable after clock-in |
| rejoinProtectMs | int | 1500 | data file **PROPOSED** | invulnerable after resuming from on break |
| deathBagDespawnSeconds | int | 30 | Grid Inventory (registered) | pile lifetime |
| frozenSeconds | int | 120 | data file (spec 06 §6) | on-break window |

**Output Range:** respawn 300 ticks (90 with Second Wind); protection 90 ticks; retrieval window 600 ticks / 20 s (810 / 27 s with Second Wind) → 100 m walking, 150 m sprinting, 135 m / 202 m with Second Wind.
**Example:** knocked down 80 m from the PO, nothing carried after the spill → clock-in at 10 s, sprint 7.5 m/s → back at the pile 10.7 s later, 9.3 s to spare; a solo carrier walking (5 m/s) arrives with 4 s to spare.
**Degenerate:** if all 8 slots overlap a living carrier, the first slot in rotation is used and P4's depenetration separates them. `deathBagDespawnSeconds ≤ respawnSeconds` → window 0 (ContentValidator warning, Grid Inventory D6). If the Spawn pad were ever missing the PO's door anchor is the fallback (cannot happen in v1 — it is pre-built and the PO falling is Run Over).
**Cost model (knockdowns as a cost).** A knockdown costs `respawnMs` plus the trip back to the pile, but it also moves the carrier to the Spawn pad, so it *saves* time whenever the carrier would otherwise walk home farther than `respawnMs × speed` (50 m walking, 75 m sprinting) with nothing worth fetching — far beyond that, the pile's mail returns to the back of the Intake queue and the kit is what is lost. Accepted for v1 and measured: telemetry flags `knockdownSavedTime` (knocked down > 75 m from the PO, pile never visited); tuning `respawnMs` against the median delivery distance is a Run Structure / Health & Damage hand-back (OQ26).

**ContentValidator rules (this GDD):** (1) `rangeMm (2500) + rangeSlackMm (500) ≤ leashMm (3000)` for container verbs; (2) `swimExitMm ≤ swimEnterMm − 100`; (3) `buffCapPm ≥ postalPm` and `buffCapPm ≤ 1000`, and `1000 + buffCapPm ≤ 1000 × gaitPlaybackMax` (animation clamp, Visual/Audio); (4) `minHoldTicks ≥ 6`; (5) `secondWindMs < respawnMs`; (6) catch range (1500, Throwing) < `rangeMm`; (7) `2 × pushCapMm ≤ capsuleRadiusMm` (push overlap stays under half a radius); (8) `haulOutMm ≥ stepUpMm`; (9) `incumbentSlackMm + 100 ≤ rangeSlackMm` (a hold the client completes is never rejected for range — Input & Controls `cancelHysteresis < host slack`); (10) `ticksAccel`, `ticksDecel`, `airControlPm`, `rangeMm` > 0 and `g` a multiple of 30 (no division by zero, no `g / 30` truncation); (11) `positionStaleTicks ≥ 19` (2.5 keepalive intervals at 4 Hz); (12) `supportSpeedCapMm` < the mail truck's road speed (Vehicles).

## Edge Cases

*Specialist: systems-designer, from the rule and formula space of Sections C and D.*

### Zero, max and out-of-range

- **If one tick's displacement exceeds 300 mm** (a belt rider at the 12 m/s support cap = 400 mm, a 15 m/s vehicle push-out, or a terminal-speed fall = 667 mm): the solver sweeps it in sub-steps of ≤ 300 mm against terrain, construct AABBs and remote capsules (P4), so nothing tunnels.
- **If a carrier reaches the map edge** (swimming out, a belt end, a push, a vehicle exit): position clamps 1 m inside the map rectangle and the outward velocity component is zeroed; height lookups clamp their corner indices.
- **If the StatSheet hands F1 an out-of-range input** (`longLegsStacks` > 3, a negative `addPm`; `holdSetting` is clamped by `holdEff`): each is clamped to its range before use; F1's 1200 mm/s minimum depends on it.
- **If a swimmer is enclosed** (a coast or wall taller than the 1200 mm haul-out): the owning client, which steps the swimmer, requests an unstuck and the host issues `Teleport{reason: Unstuck}` to the nearest walkable dry point — free, no penalty. The trigger predicate is open (OQ27): "no walkable ground for 600 ticks" alone would also fire on a legitimate 40 m swim.

### Simultaneous events (same tick)

- **If a knockdown and a completed hold arrive on the same tick**: requests stamped on or before that tick are applied in `Inputs.Apply` before damage resolution, so the delivery succeeds and the spill takes what is left; requests applied after the knockdown fail `dead`. P15 step 1 only cancels holds still in progress.
- **If two players request the same seat or turret in one tick**: host FIFO order; the loser gets `unavailable: "Seat taken"` and their focus falls to the next anchor.
- **If damage resolves on the clock-in tick**: Alive and Protected are set atomically, so it is ignored; a second `PlayerDied` for a Dead carrier is dropped.
- **If the owner disconnects with a `Teleport` unacknowledged**: the host already set its stored position to the destination when it sent the teleport, cleared the F5 history and discards older-`seq` transforms; the Frozen body sits at the destination and a reconnect gets a new `seq`.
- **If a thrown item enters the catch window while a deliver hold is in progress**: the hold continues and completes; the item lands normally (honest latch — Input & Controls `interactResolutionOrder`). A teammate's throws can never reset someone's hold.
- **If a Dead player's disconnect and their clock-in timer land on the same tick**: transport events are processed before timers — they clock in Frozen, 120 s counted from that tick.
- **If a reconnect arrives on the 120 s tick**: resume is accepted through `freezeTick + 3600` inclusive and the spill happens at `+ 3601`.

### Rule collisions

- **If a land vehicle stalls in water with riders** (spec 13 §4.5): riders are ejected to their seat exits on the stall tick (they are not carried along by the 5 s teleport); an exit at swim depth makes them swim, weight-scaled again; a stalled vehicle's seats are `unavailable`.
- **If a knockdown happens at sea and there is no dry point within 6 m**: the pile floats at the water surface; Open on a pile and Pick up on a Spill heap are the two `allowWhileSwimming` verbs (P6), so it stays reachable.
- **If a carrier is knocked down with the panel open or an entry on the cursor**: P15 step 2 closes the panel, the cursor entry goes into the pile (Grid Inventory R11/R13); Tab and Interact are no-ops while Dead; the orbit camera shows the pile's position, not its contents.
- **If a Protected carrier disconnects**: Frozen replaces Protected and the remainder is discarded; a resume grants a fresh 45 ticks, never summed.
- **If a push would move a carrier off a moving roof**: allowed (the gag); the carrier keeps the roof's velocity in the air and lands unhurt. A push into a solid AABB is blocked by the solver. At 1.8 m/s a rider can always walk back from an edge.
- **If a belt carries a carrier into deep water or off its end**: normal transitions — Swimming without damage, or Grounded with the belt's velocity dropped at once. A carrier riding the segment they are pushing gets the belt's base speed only, not the +50 % (it would stack the push onto themselves).
- **If a construct is placed over a living or Frozen capsule** (the host sees positions up to 300 ms old): P4 depenetrates upward up to 3 m, else sideways to the nearest walkable point; a Frozen body is moved by the host and a resume uses `Teleport`.
- **If a carrier is on a belt or roof when they swim, mount or are knocked down**: the support link is dropped on that tick; the knockdown position is the carrier's world position, and the pile does not ride the belt.

### Lifecycle

- **If a second connection claims an account that still has a live peer**: the newest peer wins and the old one is dropped (half-open sockets after a Wi-Fi drop).
- **If Second Wind keeps the hotbar and it holds a weapon**: P18 sees it and grants nothing. The grant is evaluated on the tick the body becomes Alive, including a resume.
- **If standard-issue copies accumulate** (a carrier is knocked down repeatedly and never fetches the pile): harmless — at most one per clock-in, sell value 0, and Grid Inventory R13 turns them into a Spill heap that expires in 300 s like any non-mail.
- **If all 8 Spawn pad slots are unusable** (walls built around the pad, a teammate in each): each slot must be walkable, not inside an AABB and not at swim depth; if none qualifies the carrier appears at the pad centre and P4 depenetrates upward.
- **If a Frozen body is airborne, or the construct under it is destroyed**: the host drops it to the ground below and zeroes its velocity; it re-checks ground every tick while Frozen; no damage.
- **If a carrier reaches the pile on its expiry tick**: requests in `Inputs.Apply` run before expiry (Grid Inventory R12's same-tick rule), so an Open or take arriving on tick 900 succeeds; the pile expires after.
- **If a knockdown occurs while Postal Sprint is active**: `postalTicksLeft` is cleared; it is paused (not cleared) while Frozen.

### Degenerate strategies

- **If a player gets knocked down on purpose far away to send mail home** (pile expiry returns mail to the Intake): accepted — it needs goons to cooperate and costs 10 s plus the walk; non-mail never teleports (Grid Inventory R13). Likewise standing still to be "sent home" to the Spawn pad is a 10 s fast travel that costs everything carried — accepted and measured (F8 cost model, `knockdownSavedTime`).
- **If belts are used as fast travel** (loops, a T3 belt with a pusher): accepted — belts exist only where the team built them, which is Pillar 3's intent; the carry is the lane speed only, a rider gets no push bonus from their own segment, and belt carry plus own speed is capped at 12 m/s (P7). Push leverage itself (+50 % on a whole segment from one stationary pusher) is Belt Logistics' to bound (OQ25).
- **If a teammate sprint-bumps another off a roof or out of a hold's range**: allowed by design; never into water (P8), at most 1.8 m/s; a hold broken by a push shows "Moved out of reach"; the host's kick is the social remedy (friends-only anti-pillar).
- **If the 500 mm / 300 ms range slack is used for dash-by interactions or by a modified client**: accepted — it is a consistency rule, not anti-cheat (spec 13 §4.3); `position_stale` after 1 s stays.
- **If a player disconnects mid-raid to make their body invulnerable**: a menu quit spills at once (P19); a pulled cable is accepted — the team is a carrier short for up to 120 s and the body can do nothing; each disconnect restarts the 120 s window from scratch (there is no banking); telemetry counts Frozen entries within 5 s of taking damage (`frozenAfterDamage`) so M1 playtests can see whether it happens.
- **If Postal Sprint is chained across a dense street**: intended — near-100 % uptime is the reward for correct deliveries; it refreshes, never stacks, and only this player's own accepted delivery (`DeliveryOutcome{result: accept}` with `actorPlayerId` = self) starts it; a misdelivery, a belt, an NPC or a teammate does not.

## Dependencies

*Hard = the system cannot function without it; Soft = enhanced by it. Bidirectional: each listed system's GDD must name Player Character & Movement in return. Grid Inventory, Input & Controls, Simulation Core, Mail & Destinations, Manual Delivery, Throwing & Catching, HUD & Notifications and Comms exist and name this system; their hand-backs to it were reconciled 2026-10-16, and the amendments this GDD needs from Grid Inventory are hand-backs in Open Questions.*

**Sim / Presentation split (systems-index TD note).** This system lives **on both sides**, with the line drawn so every rule is testable headless:
- **Sim (plain C#, `PerformativeMail.Sim`, no Godot references):** the kinematic solver `Players.Step` (F1–F3, F6, F7, P4–P8), life state and its timers (P15–P19, F8), the `IInteractable` registry, focus selection and hold timing (P10–P12, F4 — computed on the owning client's Sim instance), host-side validation and the per-player position history (P13, F5), the standard-issue grant call; other players' capsules and support poses arrive only through `IRemoteCapsuleSource` / `ISupportTransformSource` (P2).
- **Presentation (Godot C#):** camera rig and boom (P9), animation state machine and rig layers, remote-player interpolation and snapping, the clamped local extrapolation, prompt rendering, the crate/hat/pile VFX, input edge latching (through Input & Controls' router), dither fades.
- **Godot-side adapter (Network):** the transform stream, `Teleport` RPC, life-state events, JoinState/RejoinState blocks — kept out of the Sim per the systems index.

Interfaces are in the Interactions table (Detailed Design); this table records hardness and GDD status only.

| Upstream (this system depends on) | Hard/Soft | GDD |
|---|---|---|
| Simulation Core | Hard | written — HB5 adopted (tick driver: Grid Inventory OQ20) |
| Input & Controls | Hard | In Review |
| World Generation | Hard | not yet written (M-1a uses a hand-authored stub of the height field) |
| Grid Inventory & Containers | Hard | In Review |
| Health & Damage | Hard (stub in M-1b) | not yet written |
| Networking & Session | Hard (M0+; M-1a runs through a loopback) | not yet written |
| Content & Data Definitions | Hard | not yet written |
| Perks & Perk Draft | Soft (StatSheet modifiers only) | not yet written |

| Downstream (depend on this system) | Hard/Soft | GDD |
|---|---|---|
| Manual Delivery; Throwing & Catching; HUD & Notifications (with Comms) | Hard | In Review / designed |
| Vehicles & NPC Drivers; Player Combat; Enemy AI & Waves | Hard | not yet written |
| Mail & Destinations | Soft (via Manual Delivery) | In Review |
| Resource Harvesting, Building System, Belt Logistics, Defenses, Economy, Proximity Voice (verbs via `IInteractable`) | Soft | not yet written |
| Audio; Presentation & World Dressing; Accessibility | Soft | not yet written |
| Run Structure (owns `respawnSeconds`; reads `CarrierKnockedDown` for telemetry, spec 11 §10) | Soft | not yet written |

Provisional assumptions (re-verify when those GDDs exist): Health & Damage emits one `PlayerDied` per knockdown and applies invulnerability from flags this system sets; Vehicles owns seat exit points and NPC reassignment; Input & Controls can express a per-context Interact whose mode (tap/hold/channel) is supplied at runtime by the focused target.

## Tuning Knobs

*Derived from the Formulas variable tables (systems-designer). Every knob is a data-file value in `player.yaml` (spec 08 §2.14 balance keys), never a code constant. Knobs owned elsewhere are listed at the end and not duplicated.*

| Knob | Default | Safe range | Affects | Too low | Too high | Interacts with |
|---|---|---|---|---|---|---|
| `walk / sprint / swim base` | 5000 / 7500 / 2000 mm/s | locked (spec 11 §8); ±20 % in a balance pass | every route time, retrieval radius | routes drag; vehicles mandatory | vehicles pointless (Pillar 3) | D1, F8, vehicle speeds |
| `buffCapPm` **PROPOSED** | 400 | 300–600 (above 400 needs a faster gait clip) | max perk speed (1.4×) | stacked perks feel dead | hand beats the bike; foot-slide | `longLegsPm`, `postalPm`, `gaitPlaybackMax` |
| `longLegsPm` | 120 | 80–150 | Long Legs per stack | perk invisible | caps in 3 stacks alone | `buffCapPm` |
| `postalPm` / `postalTicks` | 300 / 120 | 150–400 / 60–180 | Postal Sprint | no reward | outruns the cap | `buffCapPm`, delivery density |
| `ticksAccel` / `ticksDecel` **PROPOSED** | 4 / 3 | 3–8 / 2–6 | responsiveness | twitchy, icy-free but harsh | sluggish, skating | — |
| `airControlPm` **PROPOSED** | 500 | 300–700 | air steering | rigid jumps | platforming trivial | F3 |
| `turnDegPerSec` **PROPOSED** | 720 | 360–1440 | body facing (drop point, carry pose) | carrier lags the stick | snaps unnaturally | — |
| `g` **PROPOSED** | 24 000 mm/s² | 18 000–30 000 | jump arc and air time | floaty | unreadable hop | `jumpHeightMm` |
| `jumpHeightMm` | 1200 | locked (spec 11 §8) | what can be hopped (belts, low walls) | can't clear a belt | walls stop nothing | Defenses wall height |
| `extraJumpPm` **PROPOSED** | 900 | 800–1000 | Bunny Hop mid-air jump | perk useless | ignores its cost | — |
| `maxExtraJumps` | 1 | 0–1 (v1 cap; above 1 needs a GDD change and a counter) | mid-air jumps the perk can grant | perk disabled | (not allowed) | Bunny Hop, `extraJumps` |
| `slideRunMaxMmS` **PROPOSED** | 4000 mm/s | 2500–5000 (feel) | horizontal skid speed down steep terrain (F3 steep-slope slide) | sticks to faces, slow off hills | steep face beats the walkable path; reads as a launch | `g`, `slopeMaxPm`, walk base, `terminalMmS` |
| `coyoteTicks` / `jumpBufferTicks` **PROPOSED** | 4 / 4 | 3–6 | forgiveness | dropped jumps | ghost jumps | — |
| `stepUpMm` **PROPOSED** | 400 | 250–500 | auto step height | snags on kerbs | climbs walls | Defenses wall height (1 m) |
| `capsuleRadiusMm` / `capsuleHeightMm` **PROPOSED** | 300 / 1400 | 250–400 / 1300–1600 | doorways, push, camera | clipping into props | jams at doors | art bible §5 body |
| `swimEnterMm` / `swimExitMm` **PROPOSED** | 900 / 700 | 700–1100 / 500–swimEnter−100 | where swimming starts/ends | swim in puddles | wade too deep | `maxWadeSlowPm` |
| `maxWadeSlowPm` **PROPOSED** | 200 | 0–400 | shallow-water drag | water irrelevant | beaches are mud | — |
| `haulOutMm` / `haulOutTicks` **PROPOSED** | 1200 / 15 | 800–1500 / 10–30 | climbing out of water | coasts trap swimmers | swimmers climb walls | `stepUpMm` |
| `rangeMm` | 2500 | locked (spec 11 §8) | interaction reach | fiddly targeting | interact through walls / across a street | Grid Inventory leash 3.0 m |
| `incumbentSlackMm` **PROPOSED** | 400 | 200 – rangeSlack − 100 (rule 9) | focus keep & hold-cancel range | holds break on a step | holds survive walking away | `rangeMm` |
| `focusConePm` / `nearMm` / `nearAxisMm` **PROPOSED** | 900 / 800 / 150 | 700–1000 / 0–1500 / 100–300 | focus cone (share of the horizontal half-FOV) | hard to target | focus lands off-screen | FOV setting, `wAng` |
| `wDist` / `wAng` / `stickyPm` / `focusDwellTicks` **PROPOSED** | 400 / 600 / 60 / 4 | 200–600 / 400–800 / 30–100 / 2–8 | which target wins focus, flicker | flicker between neighbours | wrong target stays locked | each other |
| `minHoldTicks` **PROPOSED** | 6 | 6–9 | floor on any hold | violates 0.2 s anticipation | slow | `insertPm` |
| `insertPm` (Quick Hands; value owned by Manual Delivery MD9 / Perks) | 500 | 0–750 (StatSheet sum clamp) | faster mailbox insert | perk invisible | hold hits the floor | `holdMs`, `minHoldTicks` |
| `rangeSlackMm` / `rangeHistoryTicks` / `positionStaleTicks` / `claimToleranceMm` **PROPOSED** | 500 / 9 / 30 / 1500 | incumbentSlack + 100 – 500 / 6–12 / 19–60 / 1000–2500 | false "out of reach" on the host | sprinting opens rejected | opens succeed past the leash | leash 3.0 m, `incumbentSlackMm` (rules 1, 9, 11) |
| `supportSpeedCapMm` **PROPOSED** | 12 000 | 9000 – truck road speed | belt fast-travel ceiling | belts feel slow to ride | belts outrun the truck (Pillar 3) | belt speeds, vehicles (rule 12) |
| `pushCapMm` / `pushSharePm` / `pushDeadMm` / `ghostAfterTicks` **PROPOSED** | 60 / 500 / 10 / 45 | 30–90 / 400–500 / 5–30 / 30–90 | how hard carriers shove; when a stuck pair passes through | door jams | shove across gaps; griefers pin doors longer | `capsuleRadiusMm` |
| `respawnMs` | 10 000 | 6000–15 000 | cost of a knockdown (time) | knockdowns free | frustration | `deathBagDespawnSeconds`, F8 |
| `secondWindMs` | 3000 | 2000–5000 | Second Wind value | — | perk worthless | `respawnMs` |
| `spawnProtectMs` **PROPOSED** (Health & Damage confirms) | 3000 | 1500–4000 | anti spawn-camping | knocked down on arrival | raid camping on the pad | Enemy AI targeting |
| `rejoinProtectMs` **PROPOSED** | 1500 | 1000–3000 | grace after resuming | knocked down while loading | — | — |
| `frozenSeconds` | 120 | 60–180 (spec 06 §6) | on-break window | Wi-Fi blips lose your bag | absent body lingers | Grid Inventory R11 |
| `unstuckTicks` **PROPOSED** | 600 | 300–1800 | trapped-swimmer rescue | free teleports | players stuck | `haulOutMm` |
| `standardIssueGrant` **PROPOSED** | on | on/off | OQ23 answer | walk back unarmed (Fists only) | — | Player Combat, Economy (sell value 0) |

Knobs owned elsewhere and referenced here: `weightPoints`, `ptsPermille`, `weightSpeedFloor`, `strongBackPermille` (Grid Inventory D1); `deathBagDespawnSeconds`, `externalLeashMeters` (Grid Inventory); `respawnSeconds` key name (Run Structure owns it per Grid Inventory — this GDD uses the same value as `respawnMs`); belt speeds (Belt Logistics); vehicle speeds and seat profiles (Vehicles); catch range 1.5 m (Throwing & Catching); camera comfort defaults (Accessibility); network rates (Network section, owned with Networking & Session).

## Visual/Audio Requirements

*Gameplay category — required. From art-director (pose language, events, focus highlight) and technical-artist (rig, blend, LOD, camera cost), consistent with art bible §1, §3, §5, §7, §8. Audio cells are mood notes for the audio-director, not specs. Items marked **proposal** are not yet in the art bible and need art-director ratification there (Open Questions).*

**Principles that govern this system.** Principle 2 (*honest outcomes are physical*): weight, invulnerability, the pile and a focus that never prejudges a match are shown in the world. Principle 3 (*chunky silhouettes*): knees, sag and the hat must read at 30 px (LOD2). §5: one rig for all 48 humans, hat first, emotion on hat and body because the camera sits behind you. §1: the carrier is earnest; the world supplies the slapstick.

**Locomotion is exempt from anticipation → snap → hold (proposal).** The Sim moves the capsule on the input tick; the rig never gates movement. Anticipation, snap and hold play as overlapped secondary motion on the hat, backpack and squash bones. One-shots (throw, catch, interact completion, knockdown) keep §5's timings exactly and are never time-scaled.

**Pose language.**
- **Idle**: weight shifts, blinks, hat bob; after 20 s the asleep face, hat slid over the eyes; any input or the raid horn wakes it with a 0.1 s hat-pop.
- **Walk**: bouncy hop-step. **Sprint**: 15° lean, pumping arms, hat band streaming, backpack bounce. Postal Sprint adds Chalk White speed streaks (never Gold — Gold is HUD-only).
- **Jump / land**: 1.2 stretch on takeoff as velocity starts; airborne arms up, legs tucked, the parcel stays at shoulder height; landing squash 0.8 → 1.0 snap ≤ 0.1 s, hold ≥ 0.15 s, dust ring, hat spring ≤ 15 cm; depth scales with drop, never below 0.8. The mid-air jump adds a spin flick and puff ring.
- **Wade**: high-knee steps, splash rings. **Swim**: chest-deep paddle, wet face, hat floating beside the body, parcel overhead; a heavy load sits lower in the water.
- **Belt**: arms out for balance, feet planted, still steerable. **Truck roof**: crouched, one hand on the hat. **Pushed**: additive lean and shuffle away, hat tilts.
- **Mounted**: seated, hat visible at the window (principle 3), hands on the wheel, no weight sag (riders are not weight-scaled). **Turret**: hands on the tripod, 0.9 recoil squash per shot, plainly exposed.
- **Interact hold**: upper body only — the arm extends toward the anchor and pushes the parcel toward the slot while the legs keep walking; the hold itself is the anticipation, snap ≤ 0.1 s on completion.
- **Knockdown**: shock face 0.1 s, starfish flop, dizzy face, stars; the hat arcs off and lands **on the pile** as its owner marker with the tint strap (pile identity per Grid Inventory R13 — hat icon, name and tint, never tint alone). This one legitimately gates input: the carrier is down.
- **Clocking back in (proposal)**: a sealed Kraft crate wearing the owner's tint strap sits on the Spawn pad and bounces once a second through the last 3 s; the lid pops, the carrier is **controllable from the pop** while climbing out, the hat drops onto the head, the carrier tips it once and goes; a Chalk White cancellation stamp thumps onto the lid. Second Wind: same read, 3 s.
- **On break (proposal)**: seated, hat tipped over the eyes (distinct from the asleep idle), wrapped in faint bubble wrap; a Chalk White clock icon over the head fills across 120 s in the name-tag slot. A knocked-down carrier lies flat; an on-break one sits — "away" never reads as "down".
- **Spawn protection (proposal)**: the same faint Chalk White bubble-wrap shell at ≈ 30 % opacity, no new hue; goon hits visibly boing off it; it pops with a squash on expiry.

**Weight on the body (resolves Grid Inventory OQ18's weight items — art-director ruling).** Hand pose follows the **held (selected) item's** class (light aloft, medium hugged, heavy overhead — §5); a heavy parcel buried in the backpack does not put the carrier into the overhead pose. **Stride rate is proportional to real speed** (no cadence thresholds — they created foot-slide cliffs). Whole-body load is a continuous blend of `min(P, 40) / 40` from the replicated `weightPoints`: knee bend 0–25°, forward lean ≤ 10°, backpack sag 0–12 cm; at P ≥ 40 (the D1 floor) the heavy waddle, with hysteresis. Strain face and hat flatten stay **reserved for the throw wind-up** so a loaded carrier's wind-up still reads. Heavy-overhead stays offset to the dominant side so it never hides the throw-arc origin.

**Rig and blend constraints (technical-artist).** One AnimationTree per character with a state machine (Ground, Airborne, Swim, Seated, Knockdown, Frozen); ≤ 4 layers at LOD0: base locomotion, upper-body carry pose (bone-filtered, chosen by transition), upper-body one-shot, additive hat/squash (C# critically damped springs, not clips); ≤ 24 bones, no facial bones (face = UV offset). **Gait clips, not a speed blend**: walk authored at 5.0 m/s, sprint at 7.5 m/s, swim at 2.0 m/s, playback rate = ground speed / clip speed clamped to `[0.6, gaitPlaybackMax = 1.4]` — matching F1's floor (0.6) and buff cap (1.4); a buff beyond 1.4 needs a new clip (validator rule 3). Blend speed is **self-propelled, support-relative** velocity, so belts and roofs never make a carrier sprint in place. Animation LOD in a C# driver: LOD0 ≤ 10 m full rate; LOD1 10–35 m every 2nd frame, layers 3–4 off, hat spring kept; LOD2 > 35 m 15 Hz locomotion + carry only; off-screen characters do not evaluate. Budget **PROPOSED**: ≤ 1.0 ms CPU for 48 characters (≈ 6 / 14 / 28 by LOD), ≈ 4–6 draws per human at LOD0 (body, hull, hat, hat hull, carried item, its hull) — **UNVERIFIED for 4.7** (AnimationTree manual advance for the LOD rates, its per-character cost, C# interop): a SPIKE in the M-1a greybox gates the AnimationTree ADR; cache `StringName` parameter paths, allocate nothing per frame. Knockdown is **authored, not ragdoll** (a Jolt ragdoll would diverge per client and the Sim has no physics world); the hat's flight is an analytic arc seeded from `entityId` and the knockdown tick, landing at the event's `bagPosition` (P15), so every client draws the same arc with zero replication. `MeshInstance3D.skeleton` must be set explicitly on every LOD and hat mesh (4.6 default changed to `""`); `AnimationPlayer` names are `StringName` and `Animation.length` is `double` in C# (breaking-changes.md).

**Focus highlight = HUD's focus bracket.** This system supplies only the focused anchor and its bounds; HUD & Notifications draws the bracket — ≥ 24 px Chalk corner carets, a screen-space widget projected from the bounds (never a 3D billboard), static under reduced motion, cut on focus change — and it replaces any world-side outline. Never green or red: focus must not pre-announce a match (the ✓/✗ is the prompt's job). The 2 px Charcoal outline stays exclusive to characters, carried items and the arc; no environment emissive (§6). The focused object may "perk up" (a mailbox flag twitches 5°).

| Event | In-world | VFX | Audio (mood) | Reduced motion |
|---|---|---|---|---|
| Jump / land | stretch, squash | dust ring | rubbery boing, soft thud | squash 0.9–1.1 |
| Swim enter / exit | wet face, hat detaches / returns | foam ring | cheerful splash | hat cuts |
| Focus gained | carets, object perks up | — | soft tick | static carets |
| Interact hold | arm reach, hold ring (HUD) | — | rising anticipation | unchanged |
| Hold completes / cancels | snap / arm drops | — | owner system's cue / soft release | unchanged |
| Pushed / bump | lean, shuffle, both squash | — | comic "oof" | lean halved |
| Knockdown | dizzy flop, hat arcs to the pile | stars | slide-whistle fall | hat cuts to pile, static stars |
| Pile drop | bag pops, parcels hop | puff | pop and scatter (Grid Inventory) | single outline flash |
| Clocking back in | crate bounce, lid pop, hat drop, stamp | dust, stamp | rubber-stamp thunk, postal-horn toot | opacity cut, no bounce |
| Protection / on break | bubble-wrap shell, clock icon | shimmer | faint crinkle | static shell |
| Idle asleep | hat over eyes, snore bubbles | — | soft snore (local only) | static pose |

**Reduced motion for characters (proposal — §7 only defines it for UI):** keep gait and pose, cut secondary jiggle and squash overshoot, turn the hat flight into a cut, shakes become one outline flash.

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:player-character-movement` to produce per-asset specs (gait and carry clips, knockdown set, crate, bubble-wrap shell, clock icon, the 11 audio cues).

## Game Feel

*Required for Gameplay. From gameplay-programmer and art-director; numbers are targets the M-1a greybox must measure.*

- **Input to motion:** ≤ 1 tick + 1 frame (≤ 50 ms at 60 fps) for movement; look is per-frame (one frame). There is no prediction layer to blame — anything slower on the local carrier is a bug.
- **Ramp:** full walk speed in 4 ticks (0.13 s), stop in 3 (0.10 s); weight lowers the cap, never the responsiveness — a loaded carrier is slow, not sluggish.
- **Jump:** apex at 0.3 s, landing at 0.63 s, coyote and buffer 4 ticks each; the arc is snappy rather than floaty and clears a belt line, not a street.
- **The rig never gates movement** — only a knockdown takes control away; clocking back in returns control on the lid pop, not after the animation.
- **Honest weight:** the step rhythm is the speed (stride ∝ F1); at the D1 floor the waddle is unmistakable from behind at 30 m.
- **Crowds are soft, never sticky:** a doorway of eight carriers drains by sliding; contact against a remote teammate feels like a shove (≈ 0.9 m/s), with ≈ 100–300 ms of remote position lag accepted (friendslop); a pair that cannot resolve ghosts after 1.5 s.
- **Holds feel committed but forgiving:** walking during a 0.4 s deliver hold is fine; only release, leaving 2.9 m, or the world changing (target gone, swim, knockdown) breaks it, and a broken hold drains its ring at 2× speed instead of snapping.
- **Performance budget (PROPOSED, advisory until the M1 reference machine):** owning-client `ClientLayer` `CarrierStep` + focus ≤ 0.10 ms per tick; host per-tick player work (8 players: phase-3 step of host-authored bodies, position history, validation, timers) ≤ 0.20 ms — Simulation Core's slices of spec 07 §8's 8 ms tick (HB5, PCM-PERF-1); animation ≤ 1.0 ms CPU per frame for 48 humans (UNVERIFIED, SPIKE-gated). Anchors live in a 2 m spatial hash; solver iterations are capped (P4); no per-tick or per-frame allocation.

## UI Requirements

*From ux-designer. Bindings, remap, thresholds and glyphs are Input & Controls'; layout and visual language follow art bible §7; HUD & Notifications renders.*

**Interaction prompt.** Placed by HUD & Notifications H11 (screen-fixed under the reticle, no leader line; its H12 bracket marks the anchor this system supplies): `[device glyph] Verb · Target name`, with a hold ring around the glyph for Hold verbs. The owning system fills the body (Manual Delivery: both addresses with ✓/✗ — never hides a mismatch). Unavailable: the prompt stays, dimmed, with a lock icon and the reason ("Dry land required to deliver", "Close panel to deliver", "Seat taken", "Shop opens at Prep", Mail & Destinations' "Closed — opens at Delivery") — text plus icon, never colour alone; reasons are localisation keys with named arguments (`reason.dry_land{verb}`, …), never concatenated English. At 2.5–6 m a focusable target shows a faint caret marker with no text; pressing Interact at it pulses the marker with "Too far" and a soft sound; pressing with nothing near plays a small hand-wave, no text. A host rejection shows "Couldn't [verb] — [reason]". Text size, scale and contrast are HUD H23's.

**Knocked down (10 s).** Top-centre "Clocking back in… 7" with a numeral, a bar and ticks in the last 3 s ("Priority clock-in… 3" with Second Wind). A pile beacon through geometry: "Your pile · 27 s · 80 m" (seconds left and straight-line metres from the Clock-In Pad, P16) with a mail-count icon; no pile → "Nothing dropped". Mild desaturation only, no flash. The camera auto-orbits the pile (no spectate controls in v1). Chat, the comms wheel and push-to-talk stay live.

**Teammates' view of a knockdown.** Feed line `Sam was knocked down by a Runner · Elm St` (goon type from `causeKind`); a compass icon in Sam's hat icon and name; a world marker "Sam's pile · 24 s" (hat icon, name, tint strap); a spatial thud within ≈ 30 m with a caption; `Sam clocked back in` on return. If everyone is down: `All carriers clocking back in. Mail waits for no one.`

**On break.** Name tag `Sam · On break 1:43` with a plug icon, then `Dropping out in 0:12` under 15 s; feed lines `Sam lost connection · on break 2:00`, `Sam is back`, `Sam timed out · pile at Elm St` (street name from the World Generation address lookup; off-street: "by the coast"). A frozen body shows a tooltip "Inventory safe" and offers no interaction.

**Vocabulary (narrative-director).** Player-facing text uses knocked down, clocking back in, pile, on break, Clock-In Pad (the Spawn pad's player-facing name). The life state and input context named `Dead` (P14, States) are internal identifiers. Wherever Input & Controls shows a context name (remap screen, glyph hints) it reads **Knocked down**. Banned (stem match: die/dying/dead/death, kill, slain, corpse, revive, respawn, drown, game over).

**Accessibility requirements this system imposes.** Every verb in P14 remappable, including the Mounted and Dead verbs; keyboard-only and gamepad-only complete (the comms wheel has a list alternative, Pause › Comms); hold-as-toggle for every Hold and Channel verb, hold-length setting 0.2–1.0 s (Input & Controls), sprint toggle and auto-sprint; camera sensitivity and invert per axis, FOV 60–100, sprint FOV kick off, head bob off, reduced shake and flash (≤ 3 flashes/s anywhere in knockdown/clock-in); identity always hat icon + name + tint; every audio cue in this system (knockdown thud, clock-in stamp, on-break) has a caption; rumble scalable to off; reduced motion as in Visual/Audio.

Spec 09's gamepad Menu collision (emote radial vs pause) is resolved by Input & Controls: emotes live on the comms wheel and Menu is Pause.

> **📌 UX Flag — Player Character & Movement**: this system has UI requirements (interaction prompt, knocked-down screen, pile marker, on-break tags, camera settings). In Pre-Production, run `/ux-design` for the interaction prompt and the knocked-down screen before writing epics; stories should cite `design/ux/[screen].md`, not this GDD.

## Network

*From network-programmer; vocabulary from spec 06 §4 (E event, R request, S snapshot), as in Grid Inventory's Network section. Godot 4.7.1 high-level multiplayer behaviour is NOT SOURCEABLE in `docs/engine-reference/godot/`: everything here is a design contract on the adapter, and items marked **SPIKE** must be proven in the pre-M0 multiplayer spike (Grid Inventory OQ12).*

**Authority (spec 13 §3).**

| Owned by the owning client (reported, never corrected) | Owned by the host |
|---|---|
| position, body yaw, aim yaw/pitch; locomotion state and overlays (sprint, interacting); jump, swim and push resolution; support link (belt / vehicle roof); hold timers; focus (local only) | life state (Alive / Dead / Frozen) and the **transforms of Dead and Frozen bodies** (P2 carve-out), the clock-in and on-break timers, the respawn position and slot, spawn protection, HP (Health & Damage), `weightPoints` (Grid Inventory), interaction validation (P13), seat assignment, `teleportSeq` |

**Host-initiated moves: `Teleport`.** Clock-in, join spawn, rejoin, the vehicle water-stall relocation and the unstuck rescue all use one path: the host sends `Teleport{seq, position, yaw, reason}` (an `IPlayersApi` call; `TeleportAck` is an `OpKind` — Simulation Core HB5) reliably to the owner and **at once** sets its own view of that player to the destination, clears their F5 history, **seeds one sample at the destination, sets `lastReceivedTick` to now** and puts a synthetic destination transform with the new `seq` into its next batch, so observers snap at once. The owner applies a Teleport **only if its `seq` is newer** (uint8, wrap-safe) — resetting velocity, support link, airborne/swim state, jump buffer and any hold — adopts `teleportSeq = seq` and answers `TeleportAck{seq}`; a duplicate is re-acked, never re-applied, so a lost ack never yanks a player who has walked on. No ack in 2 s → resend, at most 3 times, then the peer is treated as lost (P19). Every transform carries `teleportSeq`; the host discards and never relays a transform with an older `seq`; remote clients treat a `seq` change as a snap. `CarrierKnockedDown` also bumps `seq` (P15). While a player is Dead or Frozen the host ignores their transforms and authors the body itself.

**Replicated state.**

| State | Strategy | Who receives | Rate | Size |
|---|---|---|---|---|
| Transform: x, z int32 cm; y int16 cm; body yaw uint16; aim pitch 7 bits (≈ 1°, presentation only — throws carry their own `dir`) + 1 spare flag bit reserved for Manual Delivery's "posting" flag (its OQ11); `animState` uint8; `teleportSeq` uint8; `pktSeq` uint8; support id (when OnSupport, position is support-relative) | **S**, unreliable: owner → host `ReportTransform`; the host sends each peer **one batched `Transforms[]` packet per send tick** (all other players, host-authored bodies included) | everyone | **15 Hz** (every 2nd tick) while moving, 4 Hz keepalive when still (always sent, not change-only) | 16 B up; 17 B per entry in the batch (1-byte player index); +4 B on a vehicle roof |
| `animState` layout | inside the transform | — | — | bits 0–3 state (Ground, Airborne, Swim, Seated, Operating, Knockdown, Frozen, Emote, …), bits 4–5 held carry class (none/light/medium/heavy), bit 6 sprint, bit 7 interacting |
| One-shots (jump, land, catch, delivered grin, flinch); throws play on Throwing's `ThrowLaunched` and emotes on Comms' `Emote` — one message each, never a second one-shot | **E**, reliable | everyone | on event | 2 B |
| Held slot and held-item kind | E, reliable | everyone | on change | 2 B |
| `weightPoints` (Grid Inventory, public PlayerState) | E from host | everyone | on change, ≤ 4 Hz | 2 B |
| HP % (Health & Damage) | E from host | everyone | on change, ≤ 2 Hz | 1 B |
| Life state + `respawnRemainingMs` / `frozenRemainingMs` + protection flag | E, reliable | everyone | on transition (clients count down locally) | 5 B |
| `vehicleId`, seat | E, reliable | everyone | on mount/dismount | 3 B |
| Focus target, hold progress | not replicated (the `interacting` bit drives others' animation) | — | — | 0 |

**The host's view of position** is the last transform received from the owner, stamped `receivedTick = CurrentTick` (Simulation Core HB5), in a per-tick sample-and-hold ring (F5); ingress positions are clamped to the map rectangle, and seq discards, stale rejections, resends and implied speeds above `supportSpeedCapMm` (not on a vehicle roof, no `Teleport`) are counted (anomaly telemetry, not anti-cheat — never a clamp, spec 13 §3). Range-gated requests carry the claimed position at press and pass by F5; the 3.0 m leash uses the newest sample and does not fire when stale; no packet for 1 s → `position_stale` for new requests (open panels are not auto-closed by a lag spike). **Replacement wording for Grid Inventory R10 (hand-back):** *"Range is measured on the host from the chest point of the position the client claimed at press, which must lie within 1.5 m of a position the owning client reported (or its dead-reckoned position) in the last 300 ms; it passes within range plus 0.5 m (spec 13 §3, client-authoritative). This is a consistency rule, not anti-cheat. It replaces 'client-predicted, server-corrected (ch06 §4)', which spec 13 §3 superseded."* Spec 08 §3.3's `lastProcessedInputTick` is dead under client authority (no reconciliation) — correction for spec 08.

**Remote interpolation (Presentation only).** `interpDelayMs = 160` (2.4 send intervals at 15 Hz, so one lost packet still leaves a bracket), `extrapolateCapMs = 100` of linear extrapolation then hold, out-of-order packets dropped by `pktSeq`; a snap (buffer cleared, no sweep; Presentation may fade, reduced motion cuts) on a `teleportSeq` change or a jump > `snapDistanceM = 6`; after a loss longer than the cap the body resumes with a 150 ms blend. Rendered roof riders use the vehicle's interpolated render-time transform so rider and truck stay coherent; the Sim uses the latest received vehicle pose (P2); a vehicle pose history for range checks on roofs is a Vehicles hand-back (OQ14; → ADR, **SPIKE**).

**Bandwidth (PROPOSED — Networking & Session confirms) [D-1].** Assuming ≈ 40 B per-packet overhead (IP/UDP/ENet; **SPIKE**) and the batched contract above: **down per client**, 7 others all moving: `(40 + 7 × 17) × 15 Hz ≈ 2.4 kB/s ≈ 19 kbps` — 48 % of spec 06 §4.2's 40 kbps budget; **up per client** while moving: `(40 + 16) × 15 ≈ 6.7 kbps` — inside the 8 kbps budget (a still carrier ≈ 1.8 kbps). The host relays ≈ 135 kbps up worst case. Unbatched per-player packets would cost ≈ 48 kbps down (`7 × (40 + 17) × 15`), which is why batching is part of the contract, not an optimisation. `transformSendHz = 20` (≈ 25 kbps down, 9.0 kbps up — 12 % over the up budget, and aliased 33/67 ms against the 30 Hz tick) is a knob that requires Networking & Session to re-baseline §4.2 first (OQ9). With interest management cut (spec 13 §3), transforms plus Grid Inventory's ≈ 9.6 kbps leave ≈ 11 kbps for enemies and belts.

**JoinState / RejoinState contribution.** A public block per player (≈ 30 B each, ≈ 240 B for 8): player index, account id, transform, `animState`, held slot and kind, `weightPoints`, HP %, life state with remaining timers and protection, `vehicleId`/seat, `teleportSeq`. The joiner's own spawn arrives as `self.position` at a Spawn pad slot with `teleportSeq = 1` (Lobby/Prep only, spec 06 §6). A rejoiner receives the body's position with `teleportSeq` incremented and their remaining life-state timer, plus Grid Inventory's owner-only panel block. Ops and interaction requests are refused until the last JoinState chunk is applied.

**Disconnect and host loss.** Peer loss → host sets Frozen, takes the body, applies P19, broadcasts `PlayerFrozen{id, 120000}`; reconnect maps the new transport peer by account (never a client-supplied id; newest peer wins); expiry → `SpillToBag`, `PlayerDespawned{id}`. No authority reassignment is needed: the host simply starts authoring the body in its batch. **SPIKE:** the transport's disconnect-detection timeout (a long default delays Frozen by seconds). Host loss: no migration, clients return to the menu (spec 06 §6).

**Network knobs:** `transformSendHz = 15` (20 only after the §4.2 re-baseline), `keepaliveHz = 4`, `interpDelayMs = 160`, `extrapolateCapMs = 100`, `snapDistanceM = 6`, `teleportAckTimeoutMs = 2000`, `teleportMaxResends = 3`, `claimToleranceMm = 1500`, plus F5's `rangeHistoryTicks`, `rangeSlackMm`, `positionStaleTicks`.

## Cross-References

| This GDD references | In | What | Status |
|---|---|---|---|
| Grid Inventory & Containers | P3, P6, P13, P15, P18, P19, P20, F1, F5, F8, Network, Open Questions | D1 `speedPermille`, R10 range/leash (wording replaced), R11 cursor on disconnect, R13 `SpillToBag` (bag position on water amended), `Grant`, R16 swimming weight, `drop` rejected while swimming, input contexts (+Mounted, +Dead), OQ18 (weight pose — resolved here), OQ20 (tick driver), OQ23 (answered by P18), reason `position_stale` | In Review — hand-backs listed |
| Input & Controls | P2, P3, P11, P12, P14, P16, F4 | router states (Dead, Mounted, Build), `holdEff` (F4's hold source), `holdCancelRange`, `interactResolutionOrder`, `IInteractionQuery`, `SetLook`, R11 edge latching, G3 mute, comms wheel | In Review — OQ-IC-1 reconciled 2026-10-16 (OQ17) |
| Every other system GDD (Health & Damage, Manual Delivery, Mail & Destinations, Throwing, Vehicles, Combat, Enemy AI, Defenses, World Generation, Networking & Session, Content & Data, Perks, HUD, Accessibility, Audio, Presentation) | as listed in Dependencies | the interfaces in the Dependencies tables | Simulation Core, Mail, Manual Delivery, Throwing, HUD, Comms written — hand-backs reconciled 2026-10-16; others not yet written — provisional |
| design/gdd/game-pillars.md | Player Fantasy | Pillar 3 ratified reading ("automation must never remove the player's reason to move") | ratified |
| design/art/art-bible.md §1, §3, §5, §6, §7, §8 | Visual/Audio, Game Feel, Player Fantasy | one rig, hat-first identity, face atlas, pose timings, carry poses, outline rule, LOD and draw budgets; proposals listed in Open Questions | approved (REVISED) |
| design/spec/01 §2.2 & §7, 04 §2–3, 05 §1, 06 §3–§7, 07 §3–4, 08 §3.3, 09 §2.4/§3/§4/§5, 10 §2.6/§4, 11 §8, 13 §3–§4.5 | throughout | retrofit sources; spec 13 wins on conflict. Superseded here: spec 06's prediction/reconciliation, 20 Hz snapshots and movement clamps (by 13 §3), spec 08 §3.3 `lastProcessedInputTick`, spec 09's "Deliver all matching" (by Grid Inventory D-B), spec 13 §4.5 "players are solid" for on-break bodies (P8) | spec |
| prototypes/m1a-delivery-throwing-concept/REPORT.md | P1, Game Feel | capsule carrier, 2.5 m / 0.4 s deliver values validated informally; its 1.8 m capsule and instant velocity superseded | throwaway |

## Acceptance Criteria

*Specialist: qa-lead. Tags as in Grid Inventory Section H: **[UNIT]** xUnit on the plain-C# Sim (`tests/unit/player/<Rule>Tests.cs`, no Godot); **[INTEGRATION]** Sim across two systems' public interfaces; **[PERF]**; **[UI]** Godot-side against a scripted Sim stub, asserting the UI event log; **[NET]** loopback adapter or headless bot clients with injected latency and loss; **[MANUAL]** tester on a build, evidence in `production/qa/evidence/`. `qa.level: full`: every non-MANUAL criterion is BLOCKING except [PERF] wall-clock thresholds (ADVISORY until the M1 reference machine). Units mm, mm/s, 30 Hz ticks; "tick N" counts from the triggering event. Tolerances marked **(QA-proposed)** are not in the rules above and are owned by QA. **Gating:** a criterion that needs an unwritten GDD's interface (Health & Damage, Vehicles, Networking & Session, Throwing) runs against a stub and reports **BLOCKED, not failed**, until that GDD is approved; **PENDING** criteria (PCM-GI-1, PCM-P15-2 — this GDD's amended Grid Inventory behaviour; PCM-P18-1) are excluded from CI until their hand-back or Open Question closes.*

| Fixture | Definition |
|---|---|
| FX-Dry | flat dry terrain at +1000 mm, map 0…128 000 mm |
| FX-Beach | one 2000 mm tile: h00 = h10 = −1000, h01 = h11 = −200 (depth 600 at fx 500, fz 1000) |
| FX-Wt | Grid Inventory weights 1/3/8, 10 ‰ per point, floor 600: P = 20 → 800 ‰, P = 40 → 600 ‰ |
| FX-Pad | Spawn pad at (0,0), 8 ring slots at 1200 mm; FX-Kit: Land kit with an Axe (melee tool) |
| FX-Deep / FX-Ramp | flat terrain at −2000 mm (swim) / one tile rising by the stated rise over 2000 mm |

### Core rules
- **PCM-P1-1 [UNIT]** GIVEN a spawned player, THEN its EntityId class is `player`, capsule radius 300, height 1400, and body yaw and aim are separate fields.
- **PCM-P2-1 [UNIT]** GIVEN FX-Dry and a recorded 600-tick input + snapshot script (remote capsules and support poses through stubbed `IRemoteCapsuleSource` / `ISupportTransformSource`), THEN per-tick transform hashes equal a committed golden hash on Windows and Linux CI, and the Sim assembly references no Godot assembly. **PCM-P2-2 [INTEGRATION]** WHEN an Alive remote player reports transforms for 100 ticks, THEN the host never calls `Players.Step` for it and stores its last reported transform; WHEN it is Dead or Frozen, THEN the host authors its transform and ignores the owner's. **PCM-P2-3 [UI]** the rendered local carrier is never below the latest tick's ground height nor past its swept position.
- **PCM-P3-1 [UNIT]** GIVEN weight 1000 ‰ and no buffs (FX-Dry, FX-Deep), THEN steady speed is walk 5000, sprint 7500, swim 2000, and sprint never decays over 3600 ticks (no stamina).
- **PCM-P3-2 [UNIT]** GIVEN sprint held with no move input, THEN velocity goes to 0; sprint pressed while airborne starts on landing; sprint persists through a jump; sprint is ignored while swimming, mounted, dead and in Panel, where walking continues.
- **PCM-P3-3 [UNIT]** GIVEN camera yaw 90° and forward input, THEN velocity points along camera forward ±1°; a 180° turn completes body yaw in 8 ticks, not 7; with Throwing's aim held, body yaw equals camera yaw that tick.
- **PCM-P4-1a [UNIT]** FX-Ramp rises of 1400 / 1404 mm over 2000 mm (700 / 702 ‰) are walked / slid along and down. **PCM-P4-1b** box ledges of 400 / 401 mm are climbed / blocked. **PCM-P4-1c** a 400 mm drop stays Grounded that sub-step, a 401 mm drop goes Airborne. **PCM-P4-1d** (steep-slope slide, F3) from rest on a 702 ‰ face Vy is −264 after tick 1, −2640 after tick 10 and −2808 from tick 11 on, with 133–134 mm horizontal per capped tick down the gradient; on 1000 ‰ the cap −4000 is reached on tick 10; on 5000 ‰ the cap is −20 000; reaching walkable ground at the foot lands with horizontal velocity unchanged by the slide.
- **PCM-P4-2a [UNIT]** a 650 mm single-tick displacement toward a 100 mm wall 300 mm ahead, and a 667 mm fall onto a 50 mm slab, never tunnel, with ≤ 3 slide iterations per sub-step. **PCM-P4-2b** at the map edge the carrier rests ≥ 1000 mm inside with outward velocity 0. **PCM-P4-2c** a static box placed over a standing carrier leaves it on top (≤ 3000 mm up, x/z unchanged); a vehicle OBB overlapping a carrier pushes it out along the nearest face by the whole overlap in one tick. **PCM-P4-2d** a ceiling hit zeroes upward vy.
- **PCM-P5-1 [UNIT]** GIVEN a jump press 4 / 5 ticks before landing, THEN it fires on the landing tick / does not; pressed 4 / 5 ticks after leaving an edge, it jumps / does not; at P = 0 and P = 40 the apex is exactly 1196 mm.
- **PCM-P5-2 [UNIT]** GIVEN one extra jump, pressed at vertical speed −3000 and at −15 000 with the feet more than `landProbeMm` (833 / 2833 mm) above support, THEN vertical speed is set to 6830 both times; no third jump; it resets on landing, on a belt and in water; without the perk there is no mid-air jump.
- **PCM-P5-4 [UNIT] (press resolution, OQ27)** GIVEN one extra jump, Vy −3000 at tick start over flat walkable ground: feet 800 mm up → the press arms the buffer, no mid-air jump, and the landing tick sets Vy 7589; feet 900 mm up → Vy 6830 on the press tick; feet 800 mm up but the support removed after the press → Vy 6830 at the end of the 4th tick after the press; a press while rising (Vy +2000, 100 mm up) → 6830 at once; a press with coyote left → 7589; over a 702 ‰ face 100 mm below → 6830 at once.
- **PCM-P5-5 [UNIT]** `maxExtraJumps: 2` in `player.yaml` is a ContentValidator error; with `maxExtraJumps 1` an `ExtraJumps` stat of 3 grants exactly one mid-air jump; with `maxExtraJumps 0` none.
- **PCM-P5-3 [UNIT]** GIVEN Panel, swimming, mounted or dead, THEN Jump is refused; a landing at 20 000 mm/s (a 10 m drop) leaves HP and horizontal velocity unchanged.
- **PCM-P6-1 [UNIT]** GIVEN a carrier on a construct above 2000 mm of water, THEN it stays Grounded with no wade slow; 3600 ticks of swimming (FX-Deep) leave HP unchanged.
- **PCM-P6-2a [UNIT]** swimming refuses jump, sprint, build, throw, deliver, tool use and `drop`. **PCM-P6-2b** hotbar select, own panel, chat, ping and look work. **PCM-P6-2c** only Open-on-pile and Pick-up-on-Spill-heap are available; every other verb returns `unavailable` with `reason.dry_land`.
- **PCM-P6-3 [UNIT]** GIVEN a swimmer pushing at a ledge 1200 mm above the water, THEN it stands on the ledge top on tick 15, not 14; releasing at tick 10 resets the count; a 1201 mm ledge never lifts.
- **PCM-P7-1 [UNIT]** GIVEN a stationary carrier on belt tiers 1/2/3, THEN drift is 2000/4000/6000 mm/s (± 34 mm over 30 ticks); on a pushed segment 3000/6000/9000; on the segment the carrier itself pushes, the base speed only.
- **PCM-P7-2a [UNIT]** a jump off a 4000 mm/s belt with no input: horizontal speed 4000 on tick 1, then 3166, 2332, 1498, 664, 0. **PCM-P7-2b** landing re-bases to support + input on the first grounded tick (over 3 ticks with Bunny Hop). **PCM-P7-2c** a roof rider keeps its local offset when the vehicle yaws 90°; vehicles do no damage. **PCM-P7-3** on a 9000 mm/s pushed belt, a 10 500 mm/s sprint along it gives 12 000 (cap), against it 1500.
- **PCM-P8-1 [INTEGRATION]** GIVEN two overlapping carriers owned by separate clients, THEN each client changes only its own transform; Dead and Frozen bodies have no collision; a bump changes no inventory entry or container version.
- **PCM-P8-2 [UNIT]** GIVEN a push whose destination is at depth ≥ 900 mm, THEN the step is rejected; a push off a 2000 mm ledge falls with no damage; a push off a moving roof keeps the roof's velocity. **PCM-P8-3 [UNIT]** two capsules walking head-on into each other, neither moving > 100 mm, pass through on tick 45, not 44, and are solid again once separated; a shoved friend moving 30 mm per tick never ghosts.
- **PCM-P9-1 [UNIT]** GIVEN default settings, THEN the camera rig config gives pivot 1600, boom 3000, shoulder +600, pitch clamped −60°…+70°, FOV 80 (59 → 60, 101 → 100); aim held: shoulder +750, boom 2400, sensitivity × 0.6, FOV unchanged by default; swimming: pivot 1100 and camera y ≥ 0.
- **PCM-P10-1 [UNIT]** GIVEN an anchor, THEN `rangeMm` defaults to 2500; registering a second verb on one anchor in one context fails with `duplicate_verb` in Release; `IsAvailable` returns a reason key; the catalogue has no revive verb and an unknown verb fails ContentValidator. **PCM-P10-2 [UNIT, parametrised]** modes: Deliver Hold 400 ms; Open, Pick up, Mount, Operate, Configure, Shop, Announce, Dismount Tap; Catch and Push Channel; Rebuild and Deconstruct Hold; default priorities Deliver 2, Open / Pick up / Mount 1, others 0.
- **PCM-P11-1 [UNIT]** GIVEN anchors (FOV 80, 16:9), THEN eligible: chest distance 2500; cos 635; d 800 directly behind. Not eligible: 2501; cos 634 at d > 800; a chest segment crossing a solid box other than the target's own. At FOV 100 the limit is cos 526. An incumbent stays focused at 2900, not 2901.
- **PCM-P11-2a [UNIT]** a lower-scoring challenger takes focus on its 4th consecutive leading tick, not the 3rd; with no incumbent the best candidate is focused on its first tick. **PCM-P11-2b** a press while a catchable item is inbound latches Catch; a thrown item arriving during a latched Deliver hold does not convert it (the hold completes on tick 12). **PCM-P11-2c** during a hold or while toggle-armed, a challenger leading for 30 ticks and a 1-tick occlusion never change focus. **PCM-P11-2d** Panel → focus computed with `reason.close_panel`, Interact refused; Dead → no focus; Mounted → Dismount only; build mode → Deconstruct only.
- **PCM-P12-1 [UNIT]** GIVEN a Tap verb, THEN one request on press; counting the press tick as held tick 1, a 400 ms Hold sends one request on held tick 12, none on 11; hold-as-toggle sends the identical request and a second press cancels; request ticks equal Input & Controls `holdEff` on the shared vector table (PCM-F4-2).
- **PCM-P12-2 [UNIT, parametrised]** GIVEN a hold in progress, THEN it cancels with no request on: release; target at 2901 mm (2900 keeps it); target unavailable; the anchor's `ShouldCancel` returning a reason (that reason is reported); context to Panel, Mounted or Dead; swim depth; knockdown. Walking plus 10 points of non-lethal damage does not cancel it.
- **PCM-P13-1 [UNIT, parametrised]** GIVEN a request failing two checks, THEN the reason is the first in `dead → frozen → no_target → unavailable → out_of_range / position_stale`; the reply goes only to the requester; a request stamped T is applied before damage resolved on T.
- **PCM-P14-1 [UNIT]** GIVEN the 8 verbs × 5 contexts table, cell by cell against Input & Controls' router, THEN Jump is disabled in Panel; Interact is disabled in Panel, Dismount-only in Mounted, absent in Dead; Look, quick ping and spectate are absent in Dead; the Panel cannot open while Mounted or Dead; the comms wheel is live in every context but Panel (emotes greyed in Dead); Respawn is bound nowhere; no context has a button carrying both a tap and a hold verb.
- **PCM-P15-1a [UNIT]** GIVEN `PlayerDied` during a deliver hold with a chest open, THEN in one tick, in order: hold cancelled, panel closed, `SpillToBag`, Dead with timer 300 ticks (90 with Second Wind), collider off, Postal Sprint cleared, `CarrierKnockedDown` with `hasPile`, bag fields and `teleportSeq + 1`; later transforms from the Dead player are ignored. **PCM-P15-1b** GIVEN `PlayerDied` while seated, THEN ejection precedes `SpillToBag`.
- **PCM-P15-2 [UNIT, PENDING hand-back 2]** GIVEN a knockdown at swim depth with dry ground 4000 mm away, THEN the bag is at that dry point; with none within 6000 mm, at the water surface (y = 0), where a swimmer can Open it; an empty spill creates no bag and `hasPile` is false.
- **PCM-P15-3a [UNIT]** a second `PlayerDied` for a Dead player: no second spill, no timer restart. **PCM-P15-3b** a hold completed on tick T with the knockdown on T delivers and the pile holds the rest; a request stamped T+1 returns `dead`. **PCM-P15-3c** with all 8 players Dead the run is not over and timers run.
- **PCM-P16-1 [UNIT]** GIVEN Dead, THEN damage, targeting, push and interaction requests have no effect, and the router is in its Dead state. **PCM-P16-2 [UI]** the camera shows the carrier for 45 ticks, then orbits the pile (the knockdown spot when `hasPile` is false); no spectate input exists.
- **PCM-P17-1 [UNIT]** GIVEN a knockdown at tick 0, THEN Alive on tick 300 (90 with Second Wind) with full HP, protected for 90 ticks — a damaging attack ends it early, a mail throw does not; the owner receives `Teleport{reason: Respawn}`, the move axis acts on the apply tick, and a button held through the knockdown fires nothing until released.
- **PCM-P17-2 [UNIT]** GIVEN FX-Pad and 3 clock-ins within 60 ticks, THEN they take slots 0, 1, 2; slot 0 occupied → slot 1; all 8 occupied → the first slot in rotation, depenetrated; none usable → the pad centre.
- **PCM-P18-1 [INTEGRATION, PENDING OQ8]** GIVEN FX-Kit and no weapon or melee tool on the body, WHEN clocking in, THEN exactly one Axe lands in the first free hotbar slot with sell value 0; any weapon present (including one in a Second Wind hotbar), or a kit without a melee item, grants nothing; no room with `Reject` → `ok:false` and no WorldItem; 3 clock-ins grant one each.
- **PCM-P19-1a [UNIT]** GIVEN a disconnect with a chest open and a cursor entry, THEN the chest closes and the cursor is AutoPlaced (else a WorldItem at the body). **PCM-P19-1b** during a deliver hold → cancelled; seated → ejected beside the vehicle; on a belt → moved to ground within 3000 mm. **PCM-P19-1c** Frozen = invulnerable, non-solid, untargeted, inventory kept, transform authored by the host.
- **PCM-P19-2 [UNIT]** GIVEN Frozen at tick F, THEN a same-account reconnect at F+3600 resumes (Alive, protected 45 ticks from `JoinStateApplied`, `Teleport`), and one at F+3601 finds the body spilled and despawned.
- **PCM-P19-3 [NET]** GIVEN a reconnect claiming another player's id, THEN mapping is by account and the claim is ignored; a second live connection for one account drops the older peer.
- **PCM-P19-4 [UNIT]** GIVEN Dead with 100 ticks left and a disconnect at D, THEN a reconnect before D+100 resumes Dead with the remaining time; otherwise Frozen at the pad from D+100, spilling at D+3600; a kick or menu quit while Alive or Frozen spills at once, while Dead spills nothing more. **PCM-P19-5 [UNIT]** a disconnect while Joining removes the player with no spill; Run Over discards life states and piles with no spill.
- **PCM-P20-1 [UI]** with an injected clock, no input for 20.0 s fires the doze, 19.97 s does not, and any input wakes it. **PCM-P20-2 [UNIT]** idle, emote and pose changes leave the Sim state hash unchanged.

### Formulas (test vectors)
- **PCM-F1-1 [UNIT, parametrised]** walk P = 0 → 5000; walk at the floor (600 ‰) → 3000; sprint at the floor → 4500; swim at the floor → 1200; swim +400 → 2800; sprint +400 → 10 500; sprint P = 20 (800 ‰) with Long Legs ×2 (240) + Postal 300 (540), capped 400 → 8400; walk at the floor in the deepest wade (800 ‰) → 2400.
- **PCM-F1-2 [UNIT]** stacks 5 and `addPm` −50 clamp to 3 and 0; Long Legs ×1 + Postal (420) equals +400; 5000 mm/s steps 166, 167, 167 (30 ticks total exactly 5000); sprint at weight 805 ‰ and buff 1400 ‰ gives 8452 (one division), not 8451 (two).
- **PCM-F2-1 [UNIT]** walk 5000: dvMax 1250, full speed on tick 4; sprint at 4500: 1125, tick 4; stop from 5000 in 3 ticks (1667) and from 7500 (sprint flag cleared, vRef 5000) in 4 ticks (5000, 3333, 1666, 0); a 10 500 mm/s sprinter entering swim depth swims at 2000 on tick 6 (dvMax 2625, 1969, 1477, 1108, 831; speeds 7875, 5906, 4429, 3321, 2490, 2000) (corrected 2026-10-16: per-tick dvMax, see ADR-0013 §2); airborne without input: −834 per tick, 0 on tick 6; airborne with input: dvMax 625; a full reversal at walk takes 8 ticks; yawStep = 4369.
- **PCM-F3-1 [UNIT]** v0 = 7589; apex on tick 9 at 1196 mm (1191 would mean the remainder was dropped); landing on tick 19 (not 18 or 20); fall speed caps at 20 000; extra jump v0 6830, rise 969 mm.
- **PCM-F4-1 [UNIT]** A (1500 mm, cos 950) scores 270; B (900 mm, cos 600) 384; A wins; A as incumbent scores 210; priority 3 + incumbent at d 0, cos 1000 → −360; d 800, cos −1000 → 1328; dh 149 → cos 1000, dh 150 → computed. **PCM-F4-2 [UNIT, parametrised, shared with Input & Controls]** `holdTicks` for 400 ms: 12; insert 500 (Quick Hands) → 6, and 15 at the 1.0 s setting; setting 0.2 s → 6; 1.0 s → 30; insert 1000 → 6 (floor); holdBase 2000 ms at 1.0 s → 60; progress 5/12 → 416 ‰.
- **PCM-F5-1 [UNIT, parametrised]** range 2500: a corroborated claim whose chest point is 3000 from the anchor accepts, 3001 rejects; a claim 1500 from the nearest sample or dead-reckoned position accepts, 1501 rejects `out_of_range`; an anchor 2000 mm above the feet at a 2500 mm client chest distance accepts; Deliver (3000) accepts at 3500; a 9-tick-old sample corroborates, a 10-tick-old one does not; the leash closes when the newest chest point is > 3000, not at 3000; 30 silent ticks still pass, 31 give `position_stale` and open panels are kept; after a `Teleport` an Open at the destination passes on the next tick.
- **PCM-F6-1 [UNIT, parametrised]** axes 500 apart (overlap 100): each moves 50; overlap 9 → 0, 10 → 5, 200 → 60; dxz 0 → lower entityId +x, the other −x; no vertical overlap → no push; with no lag, walking into a standing capsule converges within 30 ticks to 60 ± 10 mm overlap with both moving 30 ± 5 mm per tick, never a pass-through.
- **PCM-F7-1 [UNIT, parametrised]** FX-Beach at fx 500: fz 1000 → height exactly −600, wadePm 867, walk 4335; fz 1375 → depth 450, 900, 4500; FX-Dry → 1000; swimming → 1000; dry: fz 252 (depth 899) wades, fz 250 (900) swims; swimming: fz 747 (701) stays, fz 750 (700) exits.
- **PCM-F8-1 [UNIT]** respawn 300 ticks (90), protection 90, rejoin protection 45, retrieval 600 ticks (810), radius walking 100 000 mm (135 000), sprinting 150 000 (202 500). **PCM-F8-2 [INTEGRATION]** a pile 80 000 mm straight-line from the pad: the readout shows 80 m; a carrier clocking in reaches 2500 mm of it 10.4 ± 0.4 s later sprinting and 15.6 ± 0.4 s walking **(QA-proposed tolerance)** — both inside the 20 s window.
- **PCM-F8-3 [UNIT, parametrised]** each ContentValidator rule rejects a violating data file: range + slack > leash; swimExit > swimEnter − 100; buffCap < postal, > 1000, or 1000 + cap > playback max × 1000; minHoldTicks < 6; secondWind ≥ respawn; catch range ≥ 2500; 2 × pushCap > radius; haulOut < stepUp; incumbentSlack + 100 > rangeSlack; a zero divisor or `g` not a multiple of 30; positionStaleTicks < 19; supportSpeedCap ≥ truck road speed. The shipped `player.yaml` passes.

### Edge cases, Grid Inventory, network
- **PCM-E-1a [UNIT]** two players requesting one seat in one tick: FIFO, the loser gets `unavailable` (`reason.seat_taken`). **PCM-E-1b [UNIT]** an Open on a pile arriving on its expiry tick succeeds.
- **PCM-GI-1 [INTEGRATION, PENDING hand-back 1]** P = 20 / 40 give walk 4000 / 3000; `open` with a corroborated claim at 2500 / 2501 mm chest distance gives `ok` / `out_of_range`; `drop` while swimming gives `ok:false, swimming`; a knockdown puts cursor, hotbar, inventory and backpack entries into one pile with their original entryIds, counts and mailIds.
- **PCM-NET-1 [NET]** GIVEN a `Teleport` with `seq` 5, WHEN a transform with `seq` 4 arrives, THEN it is discarded and not relayed (`seq` 0 after 255 counts as newer); a duplicate `seq` 5 after apply is acked, not re-applied; no ack within 2000 ms → resend, at most 3.
- **PCM-NET-2 [NET]** GIVEN 150 ms latency and a seeded 5 % loss pattern **(QA-proposed)**, WHEN 1000 sprint-past and sprint-toward Opens are pressed within 2500 mm, THEN ≤ 1 % fail range **(QA-proposed)**, none passes with a claim beyond 3000 mm, and 31 silent ticks give `position_stale`.
- **PCM-NET-3 [NET]** GIVEN a bot dropped, THEN all peers receive `PlayerFrozen{id, 120000}`; a same-account reconnect within 3600 ticks receives `teleportSeq + 1` and its remaining timer. **PCM-NET-4 [NET]** a carrier knocked down at T who disconnects and reconnects at T + 4000 ms receives `respawnRemainingMs` 6000 ± 100 **(QA-proposed)** and its countdown ends within ± 100 ms of the host's Alive.
- **PCM-NET-5 [UNIT/NET, ADVISORY until OQ9]** a transform packet is 16 B up and 17 B per batched entry (+4 on a roof); 8 moving bots at 15 Hz with 40 B overhead measure one batched packet per peer per send tick, ≤ 20 kbps down and ≤ 7.0 kbps up per client.
- **PCM-NET-6 [NET]** after a clock-in `Teleport`, observers snap to the pad within one send interval; `CarrierKnockedDown` snaps every client's body, the owner's included, to the host's position.

### Feel, performance, UI, fantasy
- **PCM-FEEL-1 [UNIT]** walk input sampled at tick N has moved the body 41 ± 1 mm by tick N+1. **PCM-FEEL-2 [PERF, ADVISORY]** input to the first moved frame ≤ 50 ms at 60 fps.
- **PCM-PERF-1 [PERF, BLOCKED until the tick-driver spike — OQ6]** Release build, 600-tick warm-up, worst-case fixture (8 players, one riding at the 12 m/s cap, 8 capsules in a 1 m door, 20 mailboxes, 2 000 registered anchors): owning-client `ClientLayer` `CarrierStep` + focus p99 ≤ 0.10 ms and host per-tick player work p99 ≤ 0.20 ms (Simulation Core HB5 slices of spec 07 §8's 8 ms tick; thresholds ADVISORY until the M1 reference machine); 0 B allocated per tick; probe counters (BLOCKING): sub-steps ≤ 4 per tick, slide iterations ≤ 3 per sub-step, occlusion tests ≤ candidates in range.
- **PCM-UI-1 [UI]** an unavailable target shows a dimmed prompt, a lock icon and exactly one reason key with its arguments (`reason.dry_land{verb}`, `reason.close_panel`, `reason.seat_taken`, `reason.shop_closed{phase}`, `reason.mailbox_closed`); a host rejection uses `reject{verb, reason}`; pressing Interact at a target 2.5–6 m away pulses the `too_far` marker.
- **PCM-UI-2 [UI]** with an injected clock, tick 90 after a knockdown shows "Clocking back in… 7" with numeral and bar and "Your pile · 27 s · 80 m"; tick 180 shows 4 and 24 s; `hasPile` false shows "Nothing dropped"; the feed shows `Sam was knocked down by a Runner · Elm St`; on break shows `Sam · On break 1:43` (seconds rounded up), then `Dropping out in 0:12` below 15 s left.
- **PCM-UI-3 [UNIT]** no player-facing value in this system's string table, or in Input & Controls' and Accessibility's labels for its verbs and contexts, stem-matches (case-insensitive) die / dying / dead / death, kill, slain, corpse, revive, respawn, drown or game over; identifiers and keys (`PlayerDied`, the `Dead` context) are exempt.
- **PCM-FX-1 [MANUAL]** (still worth moving; M1 playtests with the full construct set, 1 and 8 players, ≥ 3 shifts each, evidence in `production/qa/evidence/`) in the last shift: no player is idle > 30 s outside the panel, ≥ 20 % of correct deliveries are by hand (Deliver verb or an accepted throw), and no player spends > 50 % of the shift in one Push channel (PROPOSED thresholds, from telemetry); 0 of ≥ 6 testers **(QA-proposed N)** say "nothing to do but watch". **PCM-FX-2 [MANUAL]** (the body tells the truth) viewed from behind, ≥ 5 of 6 testers rank light / medium / heavy held loads, and two carriers' total loads, correctly. **PCM-FX-3 [MANUAL]** (a knockdown is a gag) the pile is at the spot with every entry in every observed knockdown, and ≥ 60 % of knockdowns are rated funny by the knocked-down player on a 1–5 questionnaire (4 or 5) **(QA-proposed)**.

**Count:** 92 criteria in 60 bullets — 84 BLOCKING automated (PCM-PERF-1 BLOCKED until its spike, wall-clock thresholds advisory), 2 ADVISORY (PCM-FEEL-2, PCM-NET-5), 3 PENDING (PCM-GI-1, PCM-P15-2, PCM-P18-1), 3 [MANUAL] (PCM-FX-1…3, sign-off). Recount IDs with `grep -oE '\*\*PCM-[A-Za-z0-9-]+'` over this section (bullets: `grep -cE '^\s*- \*\*PCM-'`). Camera, animation and caption *visuals* are VERIFY-IN-ENGINE; only their events are asserted here.

## Open Questions

**Hand-backs to Grid Inventory & Containers** (this GDD does not edit `grid-inventory.md`; its owner applies these at the next revision):

| # | Hand-back | Where in grid-inventory.md | Resolve by |
|---|---|---|---|
| 1 | **R10 position wording.** Replace "client-predicted, server-corrected position (ch06 §4)" with the F5 rule (Network wording): range measured from the chest point of the position claimed at press, corroborated within 1.5 m by a reported or dead-reckoned sample from the last 300 ms, passing within range + 0.5 m; the leash uses the newest sample and never fires on a stale position. Add reason code **`position_stale`** to R7's list and the UI feedback table ("Lost track of you — try again"). The registered `externalContainerLeashMeters` value is unchanged. | R10, R7 reason codes, Network "Validation", UI feedback table | Grid Inventory third review |
| 2 | **R13 bag position at sea.** A knockdown at swim depth places the pile at the nearest walkable dry point within 6 m, else on the water surface; Open on a pile and Pick up on a Spill heap are `allowWhileSwimming`. | R13, R12 (Spill heap), Edge Cases | same |
| 3 | **Input contexts.** Add **Mounted** and **Dead** to the context table (P14); the two binding rules are unchanged; mounting still never enters Panel. | UI Requirements "Input contexts" | same |
| 4 | **OQ18 weight items resolved** by art-director here (Visual/Audio): hand pose by the *held* item's class, stride proportional to real speed (no 0.9× / 0.6× cadence thresholds), body load from `min(P, 40)/40`, strain face and hat flatten reserved for throws. Update Game Feel's "Weight on the carrier" bullet; OQ18's remaining items (stumble, hat-lift reuse, outline thickness) stay open there. | Game Feel, OQ18 | same |
| 5 | **OQ23 answered** by P18 (standard-issue melee grant at clock-in, sell value 0) — close it, pending the owners in row 8 below. | OQ23 | same |
| 6 | **OQ20 (tick driver, thread model)** is a hard input to P2's stepping model and Game Feel's latency target; this GDD assumes a fixed 30 Hz Sim step on the main thread with the local carrier extrapolated ≤ 1 tick by the Presentation. | OQ20 | Technical Setup |

**Other open questions:**

| # | Question | Owner | Resolve by |
|---|---|---|---|
| 7 | **Health & Damage contract:** one `PlayerDied` per knockdown carrying `causeKind`; invulnerability honoured for the Protected and Frozen flags; `spawnProtectMs = 3000` (PROPOSED) and whether attacking ends it; there is **no non-lethal knockdown** (the art bible's "hat flies off on knockdown" is the death beat). | Health & Damage GDD | before Health & Damage is approved |
| 8 | **Standard-issue grant (P18, OQ23):** which item (the kit's Axe?), `KitDef.standardIssueItem` + Grid entry flag (Content & Data — not an ItemDef flag), sell value 0. Is a granted Axe (also a harvest tool) acceptable, or should a dedicated weapon be authored? | Player Combat / Economy / Content & Data | before those GDDs |
| 9 | **Bandwidth re-baseline [D-1]:** the default is now `transformSendHz = 15` with host-batched `Transforms[]` (≈ 6.7 kbps up, ≈ 19 kbps down — inside spec 06 §4.2); Networking & Session confirms the batching contract and the ≈ 40 B overhead, and re-baselines §4.2 only if 20 Hz is wanted (≈ 9.0 up / 25 down). With interest management cut, ≈ 11 kbps down remains for enemies and belts. | Networking & Session | before M0 |
| 10 | **Mid-Delivery rejoin after despawn:** spec 06 §6 allows join-in-progress only in Lobby/Prep, so a player whose 120 s expired waits for the next Prep. Allow a same-account re-entry as Dead with a 10 s timer? | Networking & Session / Run Structure | before M0 |
| 11 | **Multiplayer SPIKE items** (adds to Grid Inventory OQ12; all UNVERIFIED for 4.7): an unreliable `ReportTransform` and one batched `Transforms[]` per peer through the RPC layer (byte-array payloads, no change-only synchroniser); per-packet overhead; disconnect-detection timeout; support-relative (roof-rider) transforms. Host-authored Dead/Frozen bodies remove the authority-reassignment and client→client relay items. Engine spikes: the fixed-step accumulator and catch-up cap (OQ6, engine physics interpolation not relied on), AnimationTree manual advance and the 48-character ≤ 1.0 ms budget. | technical-director / network-programmer / godot-specialist | pre-M0 spike (engine items: M-1a) |
| 12 | **Perk numbers:** `buffCapPm = 400`. While Postal Sprint is active every Long Legs stack past the first adds nothing (×1 + Postal = 420 already exceeds the cap; ×2 wastes 140). Card text must say "up to +40 % total", and the draft must never offer a Long Legs stack whose whole value is capped away — either exclude Postal Sprint from the cap or raise the cap with a faster gait clip (validator rule 3); limiting Long Legs to one stack is **rejected** (spec 10 maxStacks 3). The "Carrier archetype" (`carrierPm` 100, an extra jump, `insertPm` 300) is retired: spec 01 §4.1's Carrier row lists perk-category examples, which spec 10 realises as Long Legs, Quick Hands (`insertPm` 500, Manual Delivery MD9) and Bunny Hop; Bunny Hop's "keeps momentum" is read as a delayed re-base (P7). | Perks & Perk Draft GDD | before Perks GDD approval |
| 13 | **Hands vs bike (Pillar 3) — ruled intended by creative-director, 2026-10-16.** A buffed sprinter overtaking a bike is earned (Postal Sprint needs correct deliveries), lasts one run only (Pillar 4), and is a good gag. The bike's niche is load: it ignores weight, so it beats a fully buffed sprint whenever the carrier is below 762 ‰ (`7500 × 1.4 × speedPermille < 8000`), about 24 weight points at D1's current rate. Vehicles' design test: at the median late-shift bag weight (telemetry), the bike is faster than the buffed sprinter; if it is not, raise the bike's speed — never lower `buffCapPm` below 400. | Vehicles & NPC Drivers | before the bike slice (M1) |
| 14 | **Vehicles contract:** seat exit points; ejection on knockdown, disconnect and water stall; stopping a vehicle whose driver disconnects; roof support and its replication; Mounted camera profiles; which vehicle verbs exist (Mount per seat, cargo Open); a vehicle pose history so F5 can check range on roofs and to a moving seat (a Mount on a 12 m/s truck is ≈ 2.4 m off with latency); `supportSpeedCapMm` < truck road speed (rule 12). | Vehicles & NPC Drivers GDD | before that GDD |
| 15 | **Enemy targeting:** skip Frozen carriers (required); skip Protected carriers or let goons boing off the bubble wrap? Hit time base: the host sees a client ≈ 100–150 ms late and the client sees goons ≈ 200 ms late — expose `HostView.Get(player, Newest | Extrapolated)` and decide wind-up length and when the hit volume is sampled (network-programmer proposes ≥ 450 ms, sampled at the end). | Enemy AI & Waves GDD | before that GDD |
| 16 | ~~Throwing contract~~ **Answered by Throwing & Catching (2026-10-16):** swimmers cannot catch; catches are tested against F5's 9-tick history within `catchHistoryMaxOffsetMm` 1500 (= `claimToleranceMm`); Catch resolves at press only; its aim profile is applied in P9 and `ThrowLaunched` replaces the "throw release" one-shot. | — | closed |
| 17 | **Input & Controls — reconciled 2026-10-16.** OQ-IC-1 (a)–(i) and (k) adopted; (j) adapted (Menu = Pause, Shoulder swap X / RB, Sprint Auto; no Spectate binds); (l) adapted (`SetLook` on Teleport, mount and dismount; no spectate). Runtime interact mode (R4.2), hold-as-toggle and per-edge latching (R11) are Input's and closed here. **Rejected:** R2 rule 5 / H-R2-5b keep Look (orbit / spectate), quick ping and Spectate previous / next while knocked down — that contradicts D-3, the later creative-director directive that Comms H14 follows; Input & Controls has dropped them (R2 rule 5, H-R2-5b). Look-orbit and spectate are post-v1. | — | closed |
| 18 | **Building:** refuse a placement whose footprint overlaps a living or Frozen capsule (P4 depenetration is the fallback, not the rule). | Building System GDD | before that GDD |
| 19 | **Art-bible proposals from this GDD** (art-director to ratify into §5/§7): locomotion exempt from anticipation-snap-hold; the clock-in crate (and whether it counts as an outlined carried item); bubble-wrap shell for protection and on break; the seated "on break" pose and clock icon; weight body-load blend; character reduced-motion definition; gait playback clamp 0.6–1.4. | art-director | before `/asset-spec` for characters |
| 20 | **Buildable Spawn pads** (game-designer, systems-designer and ux-designer all recommended "nearest pad to the knockdown"): not in the v1 construct list (spec 10 §2.6 has one pre-built pad), so adding one is a **scope change for the owner**; recorded here, not designed. | owner / producer | post-v1-lite scope review |
| 21 | **Spec corrections:** spec 08 §3.3 `lastProcessedInputTick` is dead under client authority; spec 06 §3/§4/§7's prediction, reconciliation, 20 Hz snapshots and movement clamps are superseded by spec 13 §3; spec 13 §4.5 "players are solid" now excepts on-break bodies (P8). | design owner (spec maintenance) | next spec pass |
| 22 | **Fantasy-test thresholds:** idle > 30 s, hand-delivery share ≥ 20 % and ≤ 50 % of a shift in one Push channel (PCM-FX-1), N ≥ 6 testers, ≥ 5/6 load ranking, ≥ 60 % "funny" are PROPOSED; telemetry `frozenAfterDamage` (Edge Cases) and per-shift knockdowns (spec 11 §10) feed them. | game-designer / QA | M1 playtests |
| 23 | **Capsule size** 300 × 1400 mm vs doorway and mailbox geometry (house door 1.3× human scale, art bible §3) — verify carriers fit doors and that 8 carriers can drain a 1 m door by sliding (F6). | level-designer / technical-artist | M-1a greybox |
| 24 | **On-break inventory vs Pillar 1.** A Frozen body keeps its mail for up to 120 s and nobody can take it — the one owner-locked container in the game (cf. Grid Inventory D-E). Accepted for v1 so a Wi-Fi blip never costs a friend their bag. Telemetry `mailUnitsOnFrozenBodies` per run at M1; if disconnects strand meaningful mail, let teammates Open the body (no ownership checks, R10) rather than shorten `frozenSeconds` — game-designer recommends doing this in v1, since a pulled cable is strictly better than a knockdown (hand-back to Grid Inventory R11). A menu quit already spills at once (P19). | creative-director / Networking & Session | M1 playtests |
| 25 | **Push leverage (Pillar 3):** one stationary pusher (+50 % over a whole compiled segment, and hold-as-toggle makes it AFK) is a dominant late-run job; bound the boost to a few tiles around the pusher and check the toggle case. | Belt Logistics GDD | before that GDD |
| 26 | **Knockdown cost tuning:** a knockdown saves time beyond 50 m walking / 75 m sprinting from the PO (F8 cost model); tune `respawnMs` against the median delivery distance using `knockdownSavedTime`; consider clearing more than Postal Sprint. | Run Structure / Health & Damage | M1 playtests |
| 27 | **Solver and feel details:** unstuck predicate (no haul-out target within N m, not merely 600 ticks without ground); the deepest wade at the floor (2400 mm/s = 0.48) is below the 0.6 gait clamp — floor the wade or add a slow clip plus a lower-bound validator rule; ~~jump buffer vs mid-air jump (ground buffer wins within 4 ticks?)~~ **answered 2026-10-16 (P5 press resolution + F3 `landProbeMm`; also `maxExtraJumps` capped 0–1 and the F3 steep-slope slide)**; push-only against remote capsules older than ≈ 150 ms; 8-tick reversal (use `ticksDecel` while input opposes?). | game-designer / gameplay-programmer | M-1a greybox |
| 28 | **HUD contract:** a `PromptData` interface (keys, args, availability, mode, hold progress, toggle state) for [UI] stubs; reason-key catalogue and precedence ("Busy", "Closed — opens at Delivery"); ~~UI scale 75–150 %~~ (HUD H23: 100–150 %, reading text to 200 %); ~~prompts for anchors behind the camera~~ (HUD: the prompt stays, only the bracket goes); caret contrast; pile-marker declutter; ~~timer time base~~ (HUD F3); "Inventory safe" trigger on gamepad. | HUD & Notifications / ux-designer | `/ux-design` (prompt, knocked-down screen) |
| 29 | **Art and performance budgets:** LOD0 cap and an all-48-at-LOD0 test; ≈ 288 draws for 48 humans vs the art bible's 100–150 hull draws (hull shadows off); dither through global shader parameters, never cloned materials; pin the 4.5 glTF/FBX naming version for bone-filter paths. | art-director / technical-artist / performance-analyst | before `/asset-spec` for characters |
| 30 | **Hand-only mail share:** spec 11 puts late-shift machine capacity at ≈ 2× demand, so the 20 % hand-delivery target (fantasy test 1) needs mail or addresses the network cannot serve (off-belt houses, express) — derive the share in the BalanceSim. | Mail & Destinations / game-designer | before Mail & Destinations approval |
| 31 | **Rejected hand-back (2026-10-16):** HUD HN-2 / Comms CQ-2(a) typing and speaking bits in the transform — withdrawn by Comms CF-2 (all `animState` bits used; typing is a ch3 `TypingState`, speaking is Proximity Voice's). The Dead-context rejection is in OQ17. | — | closed |

**→ becomes an ADR** (implementation, not design): the kinematic solver's structure, number types, fixed-point trig LUT and sub-stepping (P2, P4); the Sim's tick-time input interfaces (`IRemoteCapsuleSource`, `ISupportTransformSource`) and per-edge input latching (P2); the local-carrier render extrapolation and the 30 Hz tick driver (OQ20); the transform replication adapter, `Teleport`/`teleportSeq` and support-relative transforms (Network); the host's per-player position ring (F5); the `IInteractable` registry and its spatial index (P10–P11); the input router for contexts and runtime interact modes (with Input & Controls); the camera boom solver and occlusion dither (P9); the animation LOD driver and AnimationTree layout (Visual/Audio).
