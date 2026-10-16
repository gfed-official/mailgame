# 05 — Combat

Hostile enemies attack players and their constructs in swarms. The design doc describes the roster in Clash of Clans terms (Barbarian, Archer, Giant, Wall Breaker, Hog Rider, Balloon, Tank) with randomly spawned "mega" versions. This chapter defines player combat, the enemy roster and AI, wave generation, mega variants, defenses, and construct damage.

Combat is deliberately shallow compared to logistics: it is a pressure system that punishes fragile factory layouts and rewards walls, turrets, and coordination, not a hero shooter.

> Chapter 13 ships four enemies in v1 (Barbarian, Archer, Giant, Wall Breaker) plus megas; Hog Rider, Balloon, and Tank are deferred. The roster below is the full design.

## 1. Player combat

### 1.1 Stats

| Stat | Baseline |
|---|---|
| HP | 100 |
| Regen | 2 HP/s after 5 s without damage |
| Walk / sprint | 5 / 7.5 m/s (weight-scaled, chapter 03) |
| Respawn | 10 s at the PO, inventory dropped as a death bag |

Berries heal 25 HP over 5 s. Bandage (shop) heals 50 HP instantly.

### 1.2 Weapons

Tools double as weapons; dedicated weapons come from the shop. All melee is a 0.6 s swing with a 60° arc; ranged uses server-side hitscan with client-predicted VFX.

| Weapon | Damage | Rate | Range | Notes |
|---|---|---|---|---|
| Fists | 5 | 0.5 s | 1.5 m | |
| Axe | 15 | 0.6 s | 2 m | Also harvests wood |
| Pickaxe | 12 | 0.6 s | 2 m | Also harvests stone/ore; 2× vs Tanks |
| Mail Bat | 25 | 0.7 s | 2.5 m | Shop 150 ¢; knockback 2 m |
| Slingshot | 12 | 0.8 s | 25 m | Shop 200 ¢; uses Stone as ammo (1 Stone = 5 shots) |
| Package Cannon | 40 AoE r=2 m | 2 s | 30 m | Shop 600 ¢ + blueprint; fires a Small Package from inventory (the package is destroyed; misdelivery penalty does not apply, but the mail is lost) |

Friendly fire is off. Players cannot damage constructs with weapons (deconstruct instead). Thrown items (chapter 13 §4.1) deal 5 damage × weight points on impact.

## 2. Enemy roster

Enemies are server-only agents: a character body on the server with a behaviour tree, replicated as transform + animation state. Baseline stats (tunable, chapter 11). "Target" is the acquisition priority list; enemies pick the nearest valid target of the highest priority class within aggroRange, re-evaluating every 1 s.

| Enemy | HP | Damage | Attack rate | Speed | Target priority | Behaviour |
|---|---|---|---|---|---|---|
| Barbarian | 60 | 10 | 1.0 s | 4.5 m/s | Nearest of: player, construct | Melee swarmer. Spawns in groups of 3–6. Paths on the routing graph then straight-lines the last 15 m. |
| Archer | 40 | 8 | 1.5 s | 4 m/s | Player > turret > construct | Ranged 15 m. Keeps 10 m distance; retreats from approaching players. Pairs with Barbarians. |
| Giant | 400 | 40 | 2.0 s | 3 m/s | Construct (buildings first: depots, sorters) > player | Slow siege unit. Ignores players unless attacked by one within 5 m for 3 s. |
| Wall Breaker | 30 | 150 to walls/gates/belts, 20 otherwise | Suicide | 6 m/s | Wall > gate > belt segment | Runs to the nearest wall or belt in the path to the PO and explodes (r = 1.5 m). |
| Hog Rider | 120 | 20 | 1.0 s | 7 m/s | Depot > Vehicle Depot > chest > player | Jumps over walls (1 m). Steals: each hit on a container pulls 1 random item out and drops it as a WorldItem 3 m away. |
| Balloon | 90 | 30 AoE r=2 m | 3.0 s | 2.5 m/s | Turret > depot > Vehicle Depot > sorter | Airborne (6 m). Only ranged weapons and turrets can hit it. Drops bombs on turrets and logistics hubs. |
| Tank | 900 | 60 | 3.0 s | 2 m/s | PO > depot > any construct | Shift 4–5 only. Pushes constructs it walks into (destroys belts in path). Pickaxe deals 2×. |

Enemies never attack mailboxes or resident houses. Enemies attack any construct blocking their path (walls) even if not their preferred target. An occupied vehicle counts as a "player" target (chapter 13 §4.4).

### 2.1 Mega variants

When an enemy is spawned there is a megaChance (baseline 5%, +2% per shift, ×2 with the Megamail stamp) to spawn as a Mega:

- HP ×2.5, damage ×1.5, scale ×1.4, speed ×0.9.
- Aura: nearby normal enemies of the same type gain +20% speed within 8 m.
- Guaranteed drop on death: a Lost Parcel (Medium Package with a random unlocked address, value ×3) plus 2 Iron Ingots.
- Announced on the HUD when spawned ("Mega Giant sighted, north").

### 2.2 Drops

| Enemy | Drop (chance) |
|---|---|
| Barbarian | 1 Fiber (50%) |
| Archer | 2 Stone (50%) |
| Giant | 3 Stone, 1 Iron Ore (100%) |
| Wall Breaker | nothing |
| Hog Rider | returns 1 stolen item (100%) |
| Balloon | 1 Oil Can (30%) |
| Tank | 4 Iron Ingot (100%) |

Drops are WorldItems with a 2 min despawn.

## 3. Waves

### 3.1 Schedule

- Raids occur during the last 90 s of Delivery on shifts 2–5 (and the first 60 s with Double Raids). A warning plays 15 s before the first spawn ("Raid incoming from the east") with the spawn edge(s) marked on the map and compass.
- A raid is a sequence of spawn pulses every 15 s (6 pulses in 90 s). Each pulse spends part of the wave budget.

### 3.2 Budget

```
waveBudget(shift, n) = baseBudget[shift] × waveScale(n) × complaintMult × stampMult × soloMult

baseBudget    = [0, 120, 220, 360, 560]        // shift 1 has no raid
waveScale(n)  = 1 + 0.35 × (n − 1)              // 8 players: 3.45×
complaintMult = 1 + complaint / 100             // max 2×
soloMult      = 0.8 if n <= 2 else 1.0
```

Budget is spent on enemies by cost:

| Enemy | Cost | Unlock shift | Weight by shift 2 / 3 / 4 / 5 |
|---|---|---|---|
| Barbarian | 10 | 2 | 60 / 40 / 30 / 25 |
| Archer | 15 | 2 | 30 / 30 / 25 / 20 |
| Giant | 60 | 3 | 0 / 15 / 15 / 15 |
| Wall Breaker | 12 | 3 | 0 / 10 / 15 / 10 |
| Hog Rider | 25 | 3 | 0 / 5 / 10 / 10 |
| Balloon | 30 | 4 | 0 / 0 / 5 / 10 |
| Tank | 150 | 4 | 0 / 0 / 1 (max 1) / 10 (max 3) |

Pulse composition: pick weighted enemy types until the pulse budget is spent (pulse budget = waveBudget / 6, with pulse 6 getting +50% "finale"). Spawn edges: 1 edge for pulses 1–3, 2 edges for 4–6 (chosen from edges facing unlocked districts). Shift 5 pulse 6 always includes at least one Mega.

### 3.3 Targeting the factory

Enemies use the routing graph to approach the PO, then divert to targets by priority. Constructs within 30 m of an enemy's path are "noticed". This means factories built along the road from the spawn edges to the PO are hit first; factories tucked behind the PO are safer. Walls redirect pathing: the pathfinder treats walls as impassable except for Hog Riders, and Wall Breakers seek the wall segment on the shortest blocked path.

### 3.4 End of raid

When the shift clock ends, all enemies enter Flee: they run to their spawn edge for 5 s, then despawn. Enemies never persist into Prep.

## 4. Defenses

| Construct | Footprint | HP | Cost | Function |
|---|---|---|---|---|
| Wooden Wall | 1 tile | 300 | 3 Logs | Blocks ground enemies. Hog Riders jump it. |
| Stone Wall | 1 tile | 800 | 4 Stone | As above; Wall Breaker damage reduced 50% |
| Gate | 1 tile | 500 | 4 Logs, 1 Iron Ingot | Opens for players and friendly vehicles; enemies treat as wall |
| Spike Strip | 1 tile | 150 | 2 Iron Ingot | 15 dmg/s to ground enemies standing on it; does not block |
| Turret | 1 tile | 400 | Blueprint; 6 Iron Ingot, 2 Stone | 12 dmg / 0.5 s, 18 m range; always fires at full rate; targets nearest enemy, can hit Balloons. Player-operated: 2× fire rate and manual aim |
| Alarm Post | 1 tile | 100 | 2 Logs, 1 Iron Ingot | Extends raid warning by 15 s and marks enemies within 40 m on the map |
| Repair Hammer (tool) | — | — | Shop 100 ¢ | Repairs 50 HP/s to a construct; consumes materials at 25% of build cost per full HP bar |

Turrets and spike strips do not damage players.

## 5. Construct damage

Every construct has HP (chapter 10 lists them; baseline: belt piece 80, pipe piece 100, sorter 500, inserter 120, chest 200, depot 800, vehicle depot 1000, port 1200, PO 3000). Vehicles: bike 150, truck 600, rowboat 120, motorboat 500.

- At 0 HP a construct becomes a Ruin: a non-functional ghost occupying its tiles. Items inside a destroyed container spill as WorldItems (mail returns to Intake after despawn). Belt items on a destroyed segment drop.
- A Ruin can be rebuilt during Prep for 50% of its material cost (interact with the ruin), restoring its configuration (sorter filters, route console routes). During Delivery a ruin must be deconstructed (free) and rebuilt at full cost, or left in place.
- The PO cannot become a ruin: at 0 HP the run ends. The PO regenerates 5 HP/s during Prep and can be repaired with the hammer.
- Damage numbers and a health bar appear over constructs when damaged; the HUD shows a compass marker for any construct under attack.

## 6. Cursed Mail (Postage Stamp)

When the Cursed Mail stamp is active, 10% of mail carries the Cursed flag (glowing purple label). Misdelivering a cursed item spawns a mini-raid at the mailbox: 4 Barbarians + 1 Archer (30 budget), no warning. Correct delivery pays ×1.5.

## 7. Free Play notes

In Free Play raids follow the day/night cycle (one raid per night after night 2) and the budget scales with total constructs built rather than shift number. Everything else in this chapter applies unchanged.
