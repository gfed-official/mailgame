# 10 — Content Tables

Initial content for Arcade v1. All numbers are baselines (chapter 11 for the balance rationale). Ids match the schema conventions in chapter 08.

> Chapter 13 §2 marks which rows ship in v1. Rows for pipes, boats, ports, oil, Hog Rider, Balloon, Tank, Cargo, and the Sea/Pipes kits are retained as designed-but-deferred.

## 1. Items

### 1.1 Materials

| id | Name | Grid | Stack | Source | Shop buy / sell (¢) |
|---|---|---|---|---|---|
| log | Log | 1x2 | 10 | Trees (axe) | 12 / 3 |
| fiber | Fiber | 1x1 | 20 | Bushes (hand) | 4 / 1 |
| rope | Rope | 1x1 | 10 | Shop | 15 / 3 |
| plank | Plank | 1x1 | 20 | Shop | 8 / 2 |
| stone | Stone | 1x1 | 20 | Boulders (pickaxe) | 8 / 2 |
| sand | Sand | 1x1 | 20 | Beach piles (shovel) | 6 / 1 |
| iron_ore | Iron Ore | 1x1 | 20 | Ore veins (pickaxe) | — / 4 |
| iron_ingot | Iron Ingot | 1x1 | 20 | Shop (also mega/tank drops) | 40 / 10 |
| glass | Glass | 1x1 | 20 | Shop | 30 / 7 |
| oil_can | Oil Can | 1x2 | 5 | Pump output or shop | 60 / 15 |
| berries | Berries | 1x1 | 10 | Bushes | — / 1 |

There is no crafting or forge. Refined materials (rope, plank, iron_ingot, glass) are bought from the shop; raw materials come from harvesting and enemy drops.

### 1.2 Tools and weapons

| id | Name | Grid | Function | Acquire |
|---|---|---|---|---|
| axe | Axe | 1x2 | Harvest wood; melee 15 | Land/Sea kit or shop 80 |
| pickaxe | Pickaxe | 1x2 | Harvest stone/ore; melee 12, 2× vs Tank | Pipes kit or shop 100 |
| shovel | Shovel | 1x2 | Harvest sand | Shop 90 |
| repair_hammer | Repair Hammer | 1x2 | Repair 50 HP/s | Shop 100 |
| mail_bat | Mail Bat | 1x2 | Melee 25, knockback | Shop 150 |
| slingshot | Slingshot | 1x1 | Ranged 12, Stone ammo | Shop 200 |
| package_cannon | Package Cannon | 2x2 | Ranged AoE 40, fires Small Packages | Shop 600 + bp_cannon |
| bandage | Bandage | 1x1 (stack 5) | Heal 50 instant | Shop 30 |
| backpack | Backpack | 2x2 (equipped, not in grid) | +2x8 rows | All kits; shop 150 |

Tools are never crafted. Kits and the shop are the only acquisition paths.

### 1.3 Vehicles

| id | Name | Cargo grid | Seats | Speed road / off / water | Fuel | HP | Acquire |
|---|---|---|---|---|---|---|---|
| bike | Bike | 2x8 | 1 | 8 / 5 / — | none | 150 | Land kit (team) or shop 120 |
| mail_truck | Mail Truck | 10x8 | 2 | 14 / 7 / — | 1 Oil Can / 5 min (none in v1) | 600 | Shop 900 + bp_truck (shift 2+) |
| rowboat | Rowboat | 2x8 | 1 | — / — / 3 | none | 120 | Sea kit blueprint + build placement (8 Log, 2 Rope), or shop 200 |
| motorboat | Motor Boat | 10x8 | 2 | — / — / 9 | 1 Oil Can / 5 min | 500 | Shop 700 + bp_motorboat (shift 3+) |

## 2. Buildings

Cost is the build recipe (materials consumed on placement); BP is the required blueprint (shop). HP as in chapters 04–05. Constructs always run at full rated speed (no power column).

### 2.1 Transport

| id | Name | Footprint | HP | Cost | BP |
|---|---|---|---|---|---|
| belt_mk1 | Conveyor Belt | 1 | 80 | 1 Plank, 1 Iron Ingot per tile | — |
| belt_mk1_ramp | Belt Ramp | 2 | 120 | 3 Plank, 2 Iron Ingot | — |
| belt_mk1_elevated | Elevated Belt | 1 | 80 | 2 Plank, 1 Iron Ingot | — |
| belt_mk2 | Fast Belt | 1 | 100 | 1 Plank, 2 Iron Ingot | bp_fast_belts |
| splitter | Splitter | 1 | 150 | 2 Iron Ingot, 1 Plank | — |
| merger | Merger | 1 | 150 | 2 Iron Ingot, 1 Plank | — |
| pipe | Pneumatic Pipe | 1 | 100 | 1 Iron Ingot, 1 Glass | bp_pipes |
| pipe_junction | Pipe Junction | 1 | 120 | 2 Iron Ingot, 1 Glass | bp_pipes |
| pipe_inlet | Pipe Inlet | 1 | 150 | 3 Iron Ingot, 1 Glass | bp_pipes |
| pipe_outlet | Pipe Outlet | 1 | 120 | 2 Iron Ingot | bp_pipes |
| pipe_underground | Underground Pipe | 1 (+span) | 200 | 2 Iron Ingot, 2 Glass | bp_pipes |

### 2.2 Sorting and storage

| id | Name | Footprint | HP | Cost | BP |
|---|---|---|---|---|---|
| address_sorter_mk1 | Address Sorter | 2x2 | 500 | 4 Iron Ingot, 6 Stone | bp_sorting |
| inserter | Inserter | 1 | 120 | 2 Iron Ingot, 1 Plank | — |
| inserter_long | Long Inserter | 1 | 120 | 3 Iron Ingot, 1 Plank | bp_sorting |
| chest | Chest | 1 | 200 | 4 Log | — |
| depot | Depot | 2x2 | 800 | 8 Stone, 4 Iron Ingot, 4 Plank | — |

### 2.3 Resource extractors

| id | Name | Footprint | HP | Cost | BP | Notes |
|---|---|---|---|---|---|---|
| pump | Oil Pump | 1 | 400 | 6 Iron Ingot, 2 Glass | bp_oil | 1 Oil Can / 45 s; no power draw; place on oil seep |

Oil Pumps produce vehicle fuel only. There are no generators, poles, or cables.

### 2.4 Vehicles infrastructure

| id | Name | Footprint | HP | Cost | BP |
|---|---|---|---|---|---|
| vehicle_depot | Vehicle Depot | 3x3 | 1000 | 12 Plank, 8 Stone, 4 Iron Ingot | bp_truck |
| pier | Pier | 1x3 shallow | 300 | 6 Log | — |
| small_port | Small Port | 3x4 (2 deep) | 1200 | 16 Plank, 10 Stone, 6 Iron Ingot | bp_motorboat |
| rowboat_build | Rowboat (place) | free | 120 | 8 Log, 2 Rope | Sea kit bp_rowboat |

### 2.5 Defense

| id | Name | Footprint | HP | Cost | BP |
|---|---|---|---|---|---|
| wall_wood | Wooden Wall | 1 | 300 | 3 Log | — |
| wall_stone | Stone Wall | 1 | 800 | 4 Stone | bp_defense |
| gate | Gate | 1 | 500 | 4 Log, 1 Iron Ingot | — |
| spike_strip | Spike Strip | 1 | 150 | 2 Iron Ingot | bp_defense |
| turret | Turret | 1 | 400 | 6 Iron Ingot, 2 Stone | bp_turret |
| alarm_post | Alarm Post | 1 | 100 | 2 Log, 1 Iron Ingot | — |

### 2.6 Pre-built at the PO

| Name | Notes |
|---|---|
| PO building | 6x6, 3000 HP, regen 5 HP/s during Prep |
| Intake | 16x20 container, outfeed face on the east side |
| Depot (PO) | 16x20 team storage |
| Shop counter | Opens the shop during Prep and Payday |
| Spawn pad | Respawn point |

There is no Forge.

## 3. Shop catalog

| id | Name | Kind | Price | From shift | Once | Notes |
|---|---|---|---|---|---|---|
| bp_sorting | Blueprint: Sorting | blueprint | 400 | 1 | yes | Sorter, long inserter |
| bp_fast_belts | Blueprint: Fast Belts | blueprint | 500 | 3 | yes | Belt Mk2 |
| bp_truck | Blueprint: Motor Pool | blueprint | 500 | 2 | yes | Mail truck, vehicle depot |
| bp_motorboat | Blueprint: Harbour | blueprint | 500 | 3 | yes | Motor boat, small port |
| bp_pipes | Blueprint: Pneumatics | blueprint | 700 | 3 | yes | Pipes (free with Pipes kit) |
| bp_oil | Blueprint: Oil | blueprint | 600 | 3 | yes | Oil pump |
| bp_defense | Blueprint: Fortify | blueprint | 300 | 2 | yes | Stone wall, spikes |
| bp_turret | Blueprint: Turret | blueprint | 600 | 3 | yes | Turret |
| bp_cannon | Blueprint: Package Cannon | blueprint | 400 | 4 | yes | |
| bp_rowboat | Blueprint: Rowboat | blueprint | — | — | kit | Granted by Sea kit; enables build placement |
| bike | Bike | vehicle | 120 | 1 | no | |
| mail_truck | Mail Truck | vehicle | 900 | 2 | no | Requires bp_truck |
| motorboat | Motor Boat | vehicle | 700 | 3 | no | Requires bp_motorboat |
| rowboat | Rowboat | vehicle | 200 | 2 | no | Alternative to building from Sea kit |
| backpack | Backpack | item | 150 | 1 | no | For late joiners |
| axe | Axe | item | 80 | 1 | no | |
| pickaxe | Pickaxe | item | 100 | 1 | no | |
| shovel | Shovel | item | 90 | 1 | no | |
| repair_hammer | Repair Hammer | item | 100 | 2 | no | |
| mail_bat | Mail Bat | item | 150 | 2 | no | |
| slingshot | Slingshot | item | 200 | 2 | no | |
| bandage_x3 | Bandages ×3 | item | 80 | 1 | no | |
| oil_can_x3 | Oil Cans ×3 | item | 160 | 2 | no | |
| iron_ingot_x10 | Iron Ingots ×10 | item | 350 | 1 | no | For teams that skip mining |
| plank_x20 | Planks ×20 | item | 140 | 1 | no | Refined materials |
| rope_x10 | Rope ×10 | item | 120 | 1 | no | Refined materials |
| glass_x10 | Glass ×10 | item | 250 | 1 | no | Refined materials |
| npc_driver | Hire NPC Driver | hire | 150 | 3 | per shift | Requires a vehicle depot |
| npc_captain | Hire NPC Captain | hire | 150 | 3 | per shift | Requires a small port |
| po_repair | Emergency PO Repair | service | 200 | 2 | no | +1000 PO HP |
| forecast | Extended Forecast | service | 100 | 1 | per shift | Shows next shift's mail by street |

Rotating specials: 2 slots per shift rolled from shop RNG among items tagged special (each item above at 20% off, or bundles such as "Belt Starter: 20 belt tiles").

## 4. Perks (initial pool of ~28)

Rarity: C common, U uncommon, R rare. Scope: T team, P personal.

### 4.1 Carrier (personal)

| id | Name | Rarity | Effect | Notes |
|---|---|---|---|---|
| long_legs | Long Legs | C | +12% move speed | maxStacks 3 |
| big_pockets | Big Pockets | C | +1 inventory row (2x8 → 3x8) | maxStacks 2 |
| quick_hands | Quick Hands | C | Mailbox insert hold −50% | |
| strong_back | Strong Back | C | Weight speed penalty −50% | |
| bunny_hop | Bunny Hop | U | Double jump; jumping onto a belt keeps momentum | |
| bike_courier | Bike Courier | U | Bike +40% speed, bike grid +1 row | Prereq: bike exists |
| second_wind | Second Wind | U | Respawn 3 s, keep hotbar on death | |
| local_knowledge | Local Knowledge | U | Mail in your inventory shows a compass arrow to its mailbox | |
| postal_sprint | Postal Sprint | R | Each correct delivery grants +30% speed for 4 s | |
| cannon_arm | Cannon Arm | U | Throw range +50%, thrown mail that lands in the right mailbox pays ×1.1 | Added by chapter 13 |

### 4.2 Facility (team)

| id | Name | Rarity | Effect | Notes |
|---|---|---|---|---|
| express_lane | Express Lane | U | Belt speed +50% | |
| extra_labels | Extra Labels | C | Sorter filter slots per output 1 → 3 | Prereq: bp_sorting |
| greased_inserters | Greased Inserters | C | Inserter rate ×2 | |
| presort | Presort | U | PO Intake outfeed groups items by street | |
| bulk_depot | Bulk Depot | C | Depot and chest grids +50% | |
| shared_build | Shared Build | C | Build from PO Depot materials anywhere on the map | |
| postmasters_eye | Postmaster's Eye | R | Sorter filter slots unlimited; sorter throughput ×2 | Prereq: bp_sorting |

Power-related perks (Copper Wire, Overclock, Pneumatic Post, Conveyor Grease) are not in Arcade v1.

### 4.3 Postal Service (team)

| id | Name | Rarity | Effect | Notes |
|---|---|---|---|---|
| union_rep | Union Rep | U | Quota −10% | maxStacks 1 |
| stamp_collector | Stamp Collector | C | Letter and postcard value +25% | |
| fragile_handling | Fragile Handling | C | Misdelivery penalty −50% and complaint −50% | |
| bulk_mailer | Bulk Mailer | U | Letter stack size 20 → 40; postcards 40 → 80 | |
| forecast_desk | Forecast Desk | C | Free Extended Forecast every shift | |
| grace_period | Grace Period | U | Late mail pays 100% (one shift late) | |
| priority_post | Priority Post | R | 10% of mail spawns as Priority: ×3 value, must be delivered within 60 s | |
| second_district | Zoning Board | R | Unlock next district one shift early (bigger quota pool, more spawn value) | Spawn value +15% |

### 4.4 Defense (team)

| id | Name | Rarity | Effect | Notes |
|---|---|---|---|---|
| reinforced | Reinforced | C | All construct HP +40% | |
| early_warning | Early Warning | C | Raid warning +20 s; enemies marked on map | |
| hot_barrels | Hot Barrels | U | Turret damage +50% | Prereq: bp_turret |
| union_muscle | Union Muscle | U | Player melee damage +50%, Mail Bat knockback ×2 | |
| insured | Insured | R | Destroyed constructs rebuild free at next Prep | |

## 5. Enemies

Summary from chapter 05 (full stats there).

| id | Cost | Unlock shift | Role |
|---|---|---|---|
| barbarian | 10 | 2 | Melee swarm |
| archer | 15 | 2 | Ranged, hunts players |
| giant | 60 | 3 | Building siege (depots, sorters) |
| wall_breaker | 12 | 3 | Suicide vs walls/belts |
| hog_rider | 25 | 3 | Wall jumper, item thief |
| balloon | 30 | 4 | Airborne, anti-turret |
| tank | 150 | 4 | Pushes through factories, targets PO |

Mega variants of each: HP ×2.5, damage ×1.5, scale ×1.4, speed ×0.9, aura +20% speed within 8 m, Lost Parcel drop (plus 2 Iron Ingots).

## 6. Run events (rolled per shift, 0–1 per shift from shift 2, waves RNG)

| id | Name | Trigger | Effect |
|---|---|---|---|
| bulk_drop | Bulk Drop | Random Delivery start | A Cargo item for every unlocked Business Dock spawns at once (value ×1.5) |
| fog | Sea Fog | Random Delivery start (Large Island) | Boat speed −30%, Balloons do not spawn |
| festival | Street Festival | Random Prep | One street is closed to vehicles this shift; letters to that street pay ×2 |
| inspector | Postal Inspector | Complaint ≥ 75 at Payday | Next shift earnings −10% |
| lost_parcels | Lost Parcels | Random Prep | 5 Lost Parcels (value ×3) scattered on the map, marked at ≤ 30 m |
| wildfire | Wildfire | Random Delivery, shift 4+ | Wooden walls and belts take 1 dmg/s for 60 s unless within 6 m of water |
| overtime | Overtime | Random Payday, if quota missed by ≤ 10% | One-time: Delivery extended 60 s; run continues if the shortfall is covered (once per run) |
| delivery_bonus | Neighbourhood Watch | Random Prep | Zero misdeliveries this shift → +25% shift earnings |
| mega_migration | Mega Migration | Random Prep, shift 3+ | Mega chance ×3 this shift; all megas drop 2 Lost Parcels |

There is no power_surge event.

## 7. Street names (excerpt of ~120)

Larch Lane, Saltmarsh Row, Pelican Drive, Foghorn Street, Kettle Court, Binnacle Way, Gullwing Avenue, Harrow Street, Tidewater Terrace, Anvil Close, Cobble Hill, Driftwood Lane, Beacon Road, Lantern Street, Sorrel Walk, Quayside, Cannery Row, Mackerel Mews, Heron Bend, Windlass Way, Slate Street, Brine Alley, Coral Court, Oyster Lane, Puffin Place, Ropewalk, Compass Close, Harbour Street, Fern Hollow, Mill Race, Tallow Lane, Orchard Row.

## 8. Cosmetics (meta unlock, no gameplay effect)

Uniform colours (8), caps (6), truck liveries (6), mailbox skins for the PO Intake (4), emotes (8). Unlocked at ranks 15+ and via achievements (first Victory, Victory with 3 stamps, 1000 lifetime deliveries, zero-misdelivery run).
