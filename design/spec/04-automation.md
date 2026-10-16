# 04 — Automation

Mail delivery is automated by building constructs and networks. Conveyor belts and pipes move items; car-like vehicles and ships can be driven by NPCs at reduced speed; trains (deferred) use tracks and can be NPC-driven. Constructs always run at full rated speed — there is no power or energy system in Arcade v1. Many systems run autonomously but are faster when a player operates them. This chapter specifies the building system, item transport, sorting, storage, vehicles and NPC drivers, ports, and rail.

> Chapter 13 defers pipes, the sea path (§3.3 and boats), elevated belts/ramps, and truck fuel from v1.

## 1. Building system

### 1.1 Placement

- Constructs snap to the 2 m tile grid and 4 cardinal rotations (belts and pipes additionally support corner pieces). Vehicles are not constructs and are free-placed.
- Build mode shows a ghost preview with validity colouring. Validity requires: tiles buildable, not a street tile (except belt/pipe crossings, which are allowed on streets via an elevated variant), not overlapping another construct, slope < 15° (buildings auto-level the terrain under them by up to 1 m).
- Placing consumes the recipe's materials from the player's inventory, or from the PO Depot if the player is within 20 m of the PO ("shared build" perk widens this). Build recipes are placement costs only — there is no crafting or forge loop.
- Drag-placement for belts, pipes, walls: hold and drag to lay a straight line; the server receives one PlaceLine request and validates each tile.
- Deconstruct returns 100% materials during Prep, 50% during Delivery.

### 1.2 Construct properties (shared by all)

```
Construct {
  id, typeId, tile(x, y), rotation, ownerPlayerId (for stats)
  hp, maxHp
  state : per-type
}
```

### 1.3 Blueprints

Some recipes are locked behind a blueprint bought once per run from the PO Shop (chapter 10). Blueprints replace the Free Play tech tree in Arcade. Once bought, every player can build the recipe.

## 2. Item transport

### 2.1 Conveyor belts

- One direction. Two lanes (left/right), each a sequence of items with 0.5 m spacing minimum (so 4 items per lane per tile). Items on a belt are simulated as a lane position (float, metres from lane start) in a per-lane array; the belt network is compiled into segments (maximal straight/curved runs between splitters, sorters, and endpoints) so a 40-tile belt is one segment, not 40 nodes.
- Speed tiers: Mk1 2 m/s, Mk2 4 m/s (blueprint), Mk3 6 m/s (perk). Belts always run at full rated speed.
- Item size on belts: every mail item occupies one lane slot regardless of grid size except Cargo, which occupies both lanes and 2 m of length (so Cargo needs a clear belt).
- Player operation: a player standing on a belt is carried (fun, and a valid fast-travel). A player "pushing" a belt (interact with a belt segment) gives that segment +50% speed while held — the player-interaction incentive from the doc.
- Endpoints: a belt ending at a mailbox, container, inserter, sorter, vehicle loading zone, or another belt transfers items; a belt ending in air drops items onto the ground as WorldItems (these despawn back to Intake after 5 min, so leaking mail is wasteful but not catastrophic).
- Elevated variant: belts may be raised one level (1 m) to cross streets and other belts. Ramps are 2 tiles long.

### 2.2 Pipes

- Move items in arbitrary directions (pneumatic tube). A pipe network is a graph of pipe pieces, junctions, inlets, and outlets. Items travel as capsules, one item per capsule, 1 m spacing, 5 m/s.
- Routing: each capsule carries a target outlet chosen at the inlet. Inlets accept a filter (address prefix or kind) mapping to an outlet id; unmatched items go to the default outlet. Because pipes route to a specific outlet, a pipe network is effectively a distributed sorter, hence pipes cost more materials and are locked behind the Pipes blueprint (or Pipes starter kit).
- Pipes always run at full rated speed.
- Pipes can go vertical (up buildings) and under streets (underground piece costs 2×).

### 2.3 Splitter and merger

- Splitter: 1 input → up to 3 outputs, round-robin, skipping blocked outputs. Optional per-output filter by kind.
- Merger: up to 3 inputs → 1 output, round-robin.

### 2.4 Address Sorter

The key mid-game construct. 2 × 2 tiles, 1 input, 4 outputs (one per side except input, plus an "overflow" output that is the same side as the input, offset by one tile).

- Each output has a filter: any combination of district, street, numberRange, kind, unit. Filters are chosen in a UI listing only unlocked addresses. A filter of "Larch Lane" catches all mail for that street.
- Items entering are examined at 1 item / 0.5 s (Mk1) and sent to the first matching output; unmatched go to overflow.
- Filter slots per output: 1 (Mk1), 3 (perk "Extra Labels"), unlimited (Rare perk "Postmaster's Eye").
- Sorters have a 2x4 internal buffer so short bursts do not block the input belt.
- Player operation: a player at the sorter's console can hand-sort (drag items from the buffer to outputs) at UI speed, effectively unbounded throughput while they stand there.

### 2.5 Inserter

- 1 tile, takes from the construct/container behind it and places into the one in front (mailbox, belt, chest, vehicle loading zone, sorter). 1 item / 0.8 s (Mk1). Optional kind filter.
- Long inserter (2-tile reach) via blueprint.

### 2.6 Storage constructs

| Construct | Grid | Belt access | Notes |
|---|---|---|---|
| Chest | 4x8 | Via inserter only | 4 Logs |
| Depot | 10x16 | Belts can feed directly on any side; inserters pull from any side | Stone + Iron |
| PO Depot | 16x20 | Same as Depot | Pre-built |
| PO Intake | 16x20 | Output side only: belts and inserters can pull from the Intake's "outfeed" face | Pre-built; the start of every automated line |

The Intake outfeed pops items in spawn order (FIFO). A perk ("Presort") makes the Intake outfeed group by street.

## 3. Vehicles and NPC drivers

### 3.1 Vehicles

| Vehicle | Grid | Speed on road / off road / water | Seats | Acquire | Notes |
|---|---|---|---|---|---|
| Bike | 2x8 | 8 / 5 m/s | 1 | Land kit or shop 120 ¢ | No fuel; parked only, not carried |
| Mail Truck | 10x8 | 14 / 7 m/s | 2 | Shop 900 ¢ + Truck blueprint | Fuel: 1 Oil Can per 5 min driving (removed in v1, ch. 13); loading zone at rear |
| Rowboat (Small boat) | 2x8 | 3 m/s water | 1 | Sea kit blueprint + build placement, or shop | Player rows; NPC cannot operate |
| Motor Boat (Medium boat) | 10x8 | 9 m/s water | 2 | Shop 700 ¢ + Motorboat blueprint | Fuel like truck; needs Small Port to dock/load by belt |
| Mail Semi | 40x100 | 12 / 4 | 2 | Deferred | |
| Large Boat | 40x100 | 7 water | 4 | Deferred | |
| Train | 20x40 per car | 20 on rails | 1 | Deferred | |

- Vehicles are server-simulated rigid bodies with a simplified arcade controller (no realistic suspension). The driving client owns the input; the server owns the position (chapter 06). (v1: driving client owns the position — chapter 13 §3.)
- Vehicles have HP (chapter 05) and can be destroyed; a destroyed vehicle drops its inventory as a death bag and leaves a wreck that can be repaired for 50% cost.
- Loading zones: trucks and motorboats have a loading face; a belt end or inserter facing it transfers into the vehicle's grid when it is parked (speed < 0.1 m/s) within the zone. Depots and Ports provide marked parking zones.
- Vehicles vs enemies and water: see chapter 13 §4.4–4.5.

### 3.2 Vehicle Depot and NPC drivers

- Vehicle Depot (3 × 3 tiles, blueprint, shift 2+): one parking zone, one loading face, one Route Console.
- A route is an ordered list of stops, each either a destination address, a "deliver everything in this district" macro, or a construct (Depot, Port). The route runs on the routing graph (chapter 02 §3.8). Players draw routes on the map UI.
- Assigning an NPC driver (hired at the depot for 150 ¢ per shift, max 1 per depot, one depot per 2 players rounded up) makes the vehicle run the route autonomously at 60% of player speed (doc: reduced speed). At each stop the NPC delivers all items in the vehicle addressed to that stop (or to any address in the district macro) at the mailbox insert rate, then continues. Items not matching any stop remain in the vehicle and return to the depot.
- NPC drivers obey the same acceptance rules as players, so if a player loads mis-sorted mail the NPC misdelivers only if a stop's address matches an item's address — it never guesses. NPC drivers never misdeliver on their own.
- Player take-over: a player may enter the driver seat at any time; the NPC moves to passenger and the vehicle runs at full player speed. Leaving hands control back. This is the doc's incentive for player interaction.
- NPC drivers flee raids: if enemies are within 20 m of the route, the vehicle returns to the depot.

### 3.3 Ports

| Port | Footprint | Requires | Function |
|---|---|---|---|
| Pier | 1 × 3 over shallow water | 6 Logs | Rowboat mooring; player loading only |
| Small Port | 3 × 4 with 2 tiles over deep water | Blueprint, Stone + Iron | Motorboat parking zone with loading face; belts and inserters connect. Route Console for NPC boat captains. |
| Deep Water Port | 6 × 8 | Deferred | Large Boat |

NPC captains behave like NPC drivers on water routes (routing graph includes ferry lanes and a coarse water navmesh generated per seed).

## 4. Rail (deferred, specified for Free Play)

- Rails are 1-tile pieces on the grid with straight, curve, and switch pieces. Stations (2 × 6) have loading faces for each car position. (No power requirement; rails run when placed.)
- Trains: 1 engine + up to 4 cars (20x40 each). Speed 20 m/s. Signals are simplified: one train per track block between stations (blocks derived automatically).
- NPC engineers run station-to-station schedules. Player-driven trains skip the "wait for full load" timer.

## 5. Throughput reference

For balance, the reference values used in chapter 11:

| Method | Items per minute (letters) | Notes |
|---|---|---|
| Hand delivery, compact district | 5 | Walk 20 m, insert, return |
| Bike, compact district | 9 | |
| Truck, one player, presorted | 25 | 10x8 grid, sorted by street |
| NPC truck route | 12 | 60% speed, route wait |
| Mk1 belt lane to a mailbox | 120 | Limited by insert rate 1/0.5 s |
| Address Sorter Mk1 | 120 | 1 item / 0.5 s |
| Pipe outlet | 120 | Same insert rate |

The belt numbers are why belts to mailboxes are the end-state; the cost is material scaled by the distance to each house, which is why districts far from the PO push players toward vehicles and boats.
