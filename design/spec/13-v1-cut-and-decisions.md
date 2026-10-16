# 13 — v1 Cut and Decisions

Chapters 01–12 describe the full Arcade design as derived from the original Mail Game Doc. This chapter records the decisions made on 2026-10-15 to size that design for the team actually building it, and it takes precedence over earlier chapters wherever they conflict.

## 1. Context and decisions

| Question | Decision |
|---|---|
| Team | Solo developer plus friends. Not a studio. Scope must fit evenings-and-weekends over roughly 6–12 months for a playable v1. |
| Signature chaos mechanic | **Throwing** (§4.1). The one "friendslop" mechanic that touches delivery, co-op, and combat at once. |
| Voice | **Proximity voice chat**, open mic by default (§4.2). |
| Trust model | **Friends only.** No anti-cheat, no rate limits, no griefing protections. Host can kick. (§4.3) |
| Original vision doc | Preserved at `design/vision-mail-game-doc.md`. Free Play (open world, land/sea/space tech trees, optional story) remains the long-term intent; **Arcade is v1**. |

The full design is not wrong; it is a 5-person-year plan. Everything cut here is cut from *v1*, not from the game. Chapters 01–12 stay as the reference for what comes after.

## 2. Scope cut

### 2.1 Keep — the v1 spine

- **Map:** Small Island only. One town grown to ~50 houses across 5 districts.
- **Delivery:** hand delivery, throwing, grid inventory, backpack, bike.
- **Automation:** Belt Mk1/Mk2, splitter, merger, Address Sorter, inserter (plus long inserter), chest, depot. Belts may cross streets at ground level; vehicles drive over them.
- **Vehicles:** Mail Truck, Vehicle Depot, NPC driver (one vehicle path, fully featured). No fuel.
- **Run loop:** 5 shifts, quota, complaint meter, shop with blueprints, perk draft (~12 perks at first, pool grows), Postal Rank XP and a short unlock track.
- **Combat:** Barbarian, Archer, Giant, Wall Breaker, plus mega variants of each. Wooden/stone walls, gate, turret, alarm post, repair hammer, mail bat, slingshot.
- **Stamps:** Trainee (new, §4.6), Rush Hour, Cursed Mail. Three total.
- **Events:** Lost Parcels, Neighbourhood Watch, Postal Inspector. Three total.
- **Network:** listen server, Steam lobby + relay, 1–8 players, join-in-progress in Prep.

### 2.2 Defer — designed, not built in v1

| Item | Why | Where specified |
|---|---|---|
| Sea path: rowboat, motorboat, pier, small port, NPC captain, water navmesh, Sea kit | A second vehicle system with its own pathing and placement rules. | ch. 04 §3.1, §3.3 |
| Pipes and the Pipes kit | The spec calls them "a distributed sorter". Belts + sorters already own that design space. | ch. 04 §2.2 |
| Hog Rider, Balloon, Tank | Each needs a unique behaviour primitive (wall-jump + steal, airborne + ranged-only, push constructs). Add one at a time when raids feel stale. | ch. 05 §2 |
| Apartment Complex, PO Box Bank, Business Dock, Cargo | Houses only. Cargo drags in both-lane belts and docks. | ch. 02 §3.5, ch. 03 §1.1 |
| Oil pump, oil cans, truck fuel | A chore loop, not a decision loop. Trucks run free. | ch. 04 §3.1, ch. 10 §2.3 |
| Elevated belts and ramps | Replaced by ground-level street crossings. | ch. 04 §2.1 |
| Large Island archetype | Needs the sea path. | ch. 02 §1 |
| Dedicated headless server | Listen server only. | ch. 06, ch. 12 M5 |
| Package Cannon | Throwing covers it; re-add as "throw, but further" if wanted. | ch. 05 §1.2 |
| 5 of 8 stamps, 6 of 9 events, daily seed, cosmetics beyond a few uniform colours | Content breadth. | ch. 01 §6, ch. 10 §6, §8 |

Everything else in the Free Play "Deferred" list in chapter 00 stays deferred.

## 3. Netcode simplification

Chapters 06–08 specify a studio-grade stack. The friends-only trust model removes the reason for most of it. v1 decisions:

| Full design (ch. 06–08) | v1 decision | What it deletes |
|---|---|---|
| Server-authoritative player movement with client prediction and reconciliation | **Client-authoritative movement, aim, and vehicle driving.** Each client owns its own transform and replicates it; others interpolate. | Prediction, reconciliation, input redundancy, movement clamps |
| 250 ms rewind buffer for melee lag compensation | Attacker's client reports hits; server applies damage. | Rewind buffer |
| Deterministic cross-platform worldgen; clients regenerate from seed and verify worldHash | **Server generates once and sends the tables** (tiles, lots, addresses, routing graph) in JoinState. ~300×300 tiles compress to well under 200 KB. | Fixed-point noise, worldHash, cross-platform CI hash test, client regeneration |
| Hand-written BitWriter/BitReader, schema hash, quantised snapshots, delta compression | **Godot high-level multiplayer**: RPCs for events and requests, MultiplayerSynchronizer for transforms and small state. Protocol compatibility is the game version string. | `Sim/Net` serialisation layer, `Net.Tests` |
| Interest management (150 m radius, per-segment belt interest) | Replicate everything to everyone. A 600 m island with ≤ 8 players and ≤ 60 enemies fits comfortably. | Interest sets, enter/leave full-state sends |
| Anti-cheat validation and rate limits | Keep only the validations that protect *consistency*: inventory placement legality, build materials and tile legality, wallet balance, phase eligibility. | Movement clamps, range checks, RPC/chat/ping rate limits |

**Unchanged:** the server (host) remains authoritative for inventories, wallet, mail, belts, sorters, enemies, waves, constructs, and the run state machine. The belt model (server lane events + client visual sim + periodic resync) is kept; it is the one piece of the custom design that earns its cost. The Sim/Presentation split in chapter 07 is kept for testability.

**Steam path to validate first:** GodotSteam is GDScript-first. Before committing to C#, confirm a working lobby + relay + voice path from C# (Steamworks.NET or Facepunch.Steamworks). If it is painful, GDScript for the Steam/transport layer with C# for the Sim is acceptable.

## 4. Additions

### 4.1 Throwing

- Any held stack can be thrown. Hold the throw input to show an arc; release to throw. The arc is coloured by outcome: green if it ends inside a matching mailbox volume, red if inside a non-matching one, white otherwise.
- Launch speed by weight class: light 14 m/s, medium 10 m/s, heavy 6 m/s. Bulk (Cargo) cannot be thrown. Stacks are thrown whole.
- A thrown item entering a mailbox volume follows the acceptance rules in chapter 03 §2.2 **including misdelivery**. The game never softens a bad throw; that risk is the joke.
- Teammates can catch: holding interact while the item is within 1.5 m of the player puts it on their cursor / into inventory. Items landing on a belt join the belt. Otherwise the item becomes a WorldItem.
- Thrown items deal 5 × weight points on impact with enemies (light 5, medium 15, heavy 40) and are then dropped as WorldItems, not destroyed.
- Simulation: the server owns the ballistic (simple gravity, no bounce, 3 s max flight); clients play the arc from the launch event.
- New perk: **Cannon Arm** (personal, uncommon): throw range +50%, thrown mail that lands in the right mailbox pays ×1.1.
- New inventory op: `throw`.

### 4.2 Proximity voice

- Steam Voice (capture and decode via Steamworks), open mic by default, optional push-to-talk (V).
- Playback is positional with a 30 m falloff and full volume inside 8 m. Players in the same vehicle hear each other at full volume regardless of seat.
- The Post Office has a "PA" interaction: a player at the shop counter is heard island-wide for up to 10 s (cooldown 30 s). Cheap, funny, and a real coordination tool.
- Speaking indicator on the compass and over the player's head.
- Text chat and pings remain as specified in chapter 09.

### 4.3 Trust model

- No movement validation, no rate limits, no ownership checks on constructs. Anyone can spend the team wallet, deconstruct anything, and drive any vehicle.
- Host can kick from the pause menu. A kicked player cannot rejoin that run.
- Server-side validation exists only for consistency (see §3).

### 4.4 Vehicles vs enemies

- An occupied vehicle is a valid "player" target for any enemy whose priority list includes players. Damage to an occupied vehicle goes to the vehicle's HP, not the occupants.
- Vehicles deal impact damage to enemies: 10 × (speed / 5 m/s), minimum speed 4 m/s to register. A truck at 14 m/s does 28 per hit. Enemies hit are knocked back 2 m.
- Vehicles do not damage players or constructs.

### 4.5 Water, falling, and other holes

- **Swimming:** players swim at 2 m/s, weight penalty applies, no drowning. Players cannot build, throw, or deliver while swimming. Deep water is unbuildable.
- **Vehicles in water:** a land vehicle that enters water stalls; after 5 s it is teleported to the nearest Vehicle Depot (or the PO if none), cargo intact.
- **Fall damage:** none.
- **Player–player collision:** players are solid to each other and can be pushed. Standing on a moving truck roof carries the player.
- **Raid presentation:** during the raid window the sky dims and the shift timer turns red. No day/night cycle in Arcade.

### 4.6 Difficulty floor

- New stamp **Trainee** (tier 1): quota ×0.7, raid budget ×0.7, score ×0.8. Shown first in the stamp list and suggested in the lobby when the host's Postal Rank is 1.

## 5. Spec corrections

| Chapter | Issue | Resolution |
|---|---|---|
| 00 | README justifies Godot by the high-level multiplayer API while 06–08 roll a custom stack | v1 uses the high-level API (§3). README updated. |
| 06 §2 | "Steam Datagram Relay wraps ENet" | SDR is a separate MultiplayerPeer; ENet cannot be routed through it. Text corrected. |
| 01 §1, 03 §3.1, 10 §2.6 | Intake grid 20×16 vs 16×20 | 16×20 everywhere. |
| 03 §1.1 | Large Package "carried at 85% speed" vs weight formula giving 92% | Formula wins: 92%. |
| 01 §1, §2.3 | "Mail continues to spawn at reduced rate" during Raid, never quantified | `raidSpawnRateMult = 0.5`. Added to MailMixDef and balance. |
| 01 §8, 10 §3 | Solo NPC driver "from shift 2" | Unaffordable by shift 2 on a solo surplus (~280 ¢ vs ~1700 ¢ for bp_truck + truck + depot + hire). Distinction dropped; hire from shift 3 for all. |
| 01 §6 | Rush Hour: unclear whether spawn budget shrinks with the shorter Delivery | Spawn budget is unchanged; the stamp compresses the same mail into less time. That is the difficulty. |
| 01 §6 | Double Raids budget split | 40% of the wave budget in the opening 60 s window, 60% in the closing 90 s window. (Deferred stamp; recorded for later.) |
| 01 §6 | Heavy Load unquantified | Each item's size class is promoted one step with probability 0.5 (letter → small, small → medium, …). (Deferred stamp.) |

## 6. Revised build order

Replaces chapter 12. Milestones are gates, not dates.

### M-1 — Greybox fun test (first; target 1–2 weeks)

Single player, no networking, no procgen. One hand-authored street with 10 houses and a PO. Hand delivery with the address match prompt, grid inventory, **throwing**, one belt + one sorter + inserter to two mailboxes, one scripted raid pulse of Barbarians against a wooden wall. Capsule art.

**Gate:** you and at least two friends (taking turns, local) each want to play it again. If delivering and throwing mail is not fun here, nothing downstream fixes it.

### M0 — Multiplayer on the greybox

Godot high-level multiplayer, Steam lobby + relay from C# (or GDScript transport layer — decide here), client-authoritative movement, server-authoritative inventory/economy, proximity voice. Belt lane events + client visual sim.

**Gate:** 4 friends over the internet complete the greybox street together; items are never duplicated or lost in a 10-minute session of deliberately fighting over one chest.

### M1 — One full seeded run

Small Island generation (server sends tables), districts, addresses, run state machine, quota, complaint meter, shop, bike, ~12 perks, results and XP, swimming, harvesting, wooden wall and chest.

**Gate:** a 4-player group finishes a 5-shift run (no raids) in under 32 minutes; a solo player wins shift 1 and fails shift 2 by hand.

### M2 — Automation

Build mode, belts Mk1/Mk2, splitter, merger, Address Sorter with filter UI, inserters, depot, Mail Truck, Vehicle Depot, route console, NPC driver with takeover, map screen.

**Gate:** the chapter 11 §9.1 reference factory is buildable in one Prep + one Delivery; an NPC truck completes a 3-stop route with zero misdeliveries.

### M3 — Combat

Four enemies plus megas, wave scheduler, construct damage and ruins, walls/gate/turret/alarm/repair hammer, vehicles vs enemies, raid presentation.

**Gate:** chapter 11 §10.1 solo shift-2 sanity check holds in play (6 walls → no belt losses).

### M4 — Rogue-lite wrapper and polish

Perk pool to ~20, three stamps, three events, Postal Rank unlocks, onboarding tips, practice seed, Payday summary, accessibility basics (colour-blind patterns, text scale, toggle interact), gamepad pass.

**Gate:** ≥ 20 full runs with friends; solo win rate roughly 20–30%, 4-player roughly 40–60%; tuning done in data only.

### Later — the deferred list (§2.2), dedicated server, Large Island, Free Play

## 7. Open questions

Not blocking M-1. Answer by M1.

- **Title.** "Performative Mail" is the working title; the repo is `mailgame`. Decide before any public page.
- **Art and tone.** The spec says only "stylized low-poly". Needs a short art bible: palette, character silhouette, house kit style, what the enemies *are* (the Clash of Clans names are placeholders) and why they raid a post office. Use `/art-bible`.
- **Audio.** Three sounds are specified. Needs a music direction and a bark list.
- **Platform.** Steam is assumed. Confirm Steam-only for v1 and whether there is a price.
