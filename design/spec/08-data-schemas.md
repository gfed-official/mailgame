# 08 — Data Schemas

Schemas for content definitions (authored data) and runtime state (replicated or persisted). Shown as JSON with // comments; the on-disk format may be JSON or Godot .tres Resources with identical field names. Ids are lowercase snake_case strings in content and mapped to uint16 indices at load for network use.

## 1. Conventions

- id: unique within its file type. References use the id string.
- grid: [cols, rows] (width, height).
- Money is integer ¢. Time in content is seconds (converted to ticks at load). Distances in metres.
- Optional fields are marked ?. Enums are given as string sets.
- Every def file may carry "tags": [] for filtering (perk prerequisites, shop rotation).

## 2. Content definitions

### 2.1 ItemDef (content/items/*.json)

Covers tools, materials, consumables, ammo. Mail kinds are a separate def because they carry addressing behaviour.

```jsonc
{
  "id": "axe",
  "name": "Axe",
  "category": "tool",              // tool | material | consumable | ammo | blueprint | weapon
  "grid": [1, 2],
  "maxStack": 1,
  "weightClass": "light",          // light | medium | heavy | bulk
  "sellPrice": 25,                 // ¢ at PO shop; 0 = not sellable
  "buyPrice?": 0,                  // ¢ when sold by shop; omit if not buyable
  "tool?": {
    "harvests": ["wood"],          // resource types (chapter 02)
    "yieldMultiplier": 1.0,
    "swingTime": 0.6
  },
  "weapon?": {
    "damage": 15, "rate": 0.6, "range": 2.0, "arcDeg": 60,
    "ranged?": { "ammoItem": "stone", "shotsPerAmmo": 5, "aoeRadius": 0 },
    "bonusVs?": { "tank": 2.0 }
  },
  "consumable?": { "healInstant": 0, "healOverTime": 25, "duration": 5 },
  "tags": ["starter"]
}
```

### 2.2 MailKindDef (content/mail/kinds.json)

```jsonc
{
  "id": "letter",
  "name": "Letter",
  "grid": [1, 1],
  "maxStack": 20,                  // stacks only with same addressId
  "baseValue": 8,
  "weightClass": "light",
  "carryByHand": true,             // false for cargo; always spelled out — default false (CD10, 2026-10-21)
  "beltLanes": 1,                  // 1 or 2 (cargo)
  "beltLength": 0.5,               // metres of lane occupied
  "deadlineOffsetShifts": 0,       // added to spawnShift
  "acceptedBy": ["house", "apartment", "pobox"],   // destination types
  "complaintOnMisdelivery": 5,
  "unlockShift": 1
}
```

### 2.3 MailMixDef (content/mail/mix.json)

```jsonc
{
  "shifts": [
    { "shift": 1, "shares": { "letter": 0.60, "small": 0.30, "medium": 0.10 } },
    { "shift": 2, "shares": { "postcard": 0.10, "letter": 0.40, "small": 0.30, "medium": 0.15, "large": 0.05 } }
  ],
  "streetStreakRatio": 0.30,
  "batchIntervalSeconds": 15,
  "batchJitterSeconds": 3,
  "spawnOverhead": 1.6,
  "raidSpawnRateMult": 0.5,        // chapter 13: spawn rate during the Raid window
  "distanceMultiplierPerDistrict": 0.25,
  "shiftMultiplierPerShift": 0.10,
  "lateValueRatio": 0.5,
  "misdeliveryPenaltyRatio": 0.5
}
```

### 2.4 DestinationTypeDef (content/mail/destinations.json)

```jsonc
{
  "id": "house",
  "insertRate": 2.0,               // items per second for automated feeders
  "manualInsertHold": 0.4,
  "maxAutomatedFeeders": 1,
  "requiresUnit": false,           // true for apartment, pobox (box number)
  "requiresVehicleZone": false     // true for business_dock
}
```

### 2.5 ContainerDef (content/items/containers.json)

```jsonc
{
  "id": "mail_truck_cargo",
  "grid": [8, 10],
  "view": "grid",                  // grid | manifest | auto — required, no default (CD10, 2026-10-21); auto if cells > 160
  "beltAccess": "loading_face",    // none | any_side | loading_face | outfeed_only
  "allowedCategories?": ["mail"]   // omit = any
}
```

### 2.6 BuildingDef (content/buildings/*.json)

```jsonc
{
  "id": "address_sorter_mk1",
  "name": "Address Sorter",
  "footprint": [2, 2],             // tiles
  "rotations": 4,
  "hp": 500,
  "placement": {
    "onStreet": false,
    "onWater": "none",             // none | shallow | deep | shore
    "maxSlopeDeg": 15,
    "dragLine": false
  },
  "behaviour": "sorter",           // belt | pipe | splitter | merger | sorter | inserter | container | wall | gate | spike | turret | alarm | vehicle_depot | port | pump | pier
  "params": {                      // behaviour-specific
    "throughputPerSecond": 2.0,
    "outputs": 4,
    "filterSlotsPerOutput": 1,
    "bufferGrid": [4, 2]
  },
  "container?": "sorter_buffer",   // ContainerDef id if it has an inventory
  "recipe": "recipe_address_sorter_mk1",
  "ruinRebuildRatio": 0.5,
  "tags": ["automation", "sorting"]
}
```

There are no `powerDraw` or `powerSupply` fields. Constructs always run at full rated speed.

Behaviour params by type:

| behaviour | params |
|---|---|
| belt | speed, lanes (2), elevated (bool) |
| pipe | speed, underground (bool), inlet/outlet/junction role |
| splitter / merger | ways (3), filterable (bool) |
| sorter | throughputPerSecond, outputs, filterSlotsPerOutput, bufferGrid |
| inserter | rate, reach (1 or 2), filterable |
| container | none (uses container) |
| wall / gate | blocksAir (false), wallBreakerResist (0–1) |
| spike | dps |
| turret | damage, rate, range, hitsAir, operatedRateMult |
| alarm | warningBonusSeconds, revealRadius |
| vehicle_depot / port | vehicleClass (land / water), parkingOffset, loadingFace, npcHireCost |
| pump | outputItem, rate |
| pier | none |

### 2.7 RecipeDef (content/recipes/*.json)

Recipes exist only for building placement. They never produce items and never have craftSeconds > 0.

```jsonc
{
  "id": "recipe_address_sorter_mk1",
  "produces": { "building": "address_sorter_mk1" },
  "inputs": [ { "item": "iron_ingot", "count": 4 }, { "item": "stone", "count": 6 } ],
  "blueprint?": "bp_sorting",      // ShopItem id that must be owned by the team
  "craftSeconds": 0,               // must be 0; placement is instant once materials are paid
  "unlockShift": 1
}
```

ContentValidator rejects any RecipeDef whose `produces` is an item, or whose `craftSeconds` is greater than 0.

### 2.8 ShopItemDef (content/shop/*.json)

```jsonc
{
  "id": "bp_sorting",
  "name": "Blueprint: Sorting",
  "kind": "blueprint",             // blueprint | item | vehicle | hire | service
  "price": 400,
  "grants": { "blueprint": "bp_sorting" },   // or { "item": "bandage", "count": 2 } | { "vehicle": "mail_truck" }
  "availability": { "fromShift": 2, "slot": "fixed" },   // fixed | rotating
  "oncePerRun": true,
  "tags": ["automation"]
}
```

### 2.9 PerkDef (content/perks/*.json)

```jsonc
{
  "id": "express_lane",
  "name": "Express Lane",
  "description": "Conveyor belts move 50% faster.",
  "category": "facility",          // carrier | facility | postal | defense
  "scope": "team",                 // team | personal
  "rarity": "uncommon",            // common | uncommon | rare
  "modifiers": [
    { "stat": "BeltSpeed", "op": "mul", "value": 1.5 }
  ],
  "unlocks?": [ { "recipe": "recipe_belt_mk3" } ],
  "rules?": [ "insured_rebuild" ],  // closed set of rule flags implemented in code
  "prerequisites?": { "builtAny": ["belt_mk1", "belt_mk2"], "shiftMin": 2, "rankMin": 5 },
  "excludes?": [],
  "maxStacks": 1,                  // personal perks may allow >1
  "tags": ["belt"]
}
```

stat values are a closed enum (Stat in code). op is mul or add. Stat resolution: base × Π(mul) + Σ(add). There are no power-related stats or rule flags (no pipes_no_power, no consumer draw multipliers).

### 2.10 EnemyDef (content/enemies/*.json)

```jsonc
{
  "id": "hog_rider",
  "name": "Hog Rider",
  "hp": 120,
  "damage": 20,
  "attackRate": 1.0,
  "attackRange": 1.5,
  "speed": 7.0,
  "aggroRange": 40,
  "airborne": false,
  "cost": 25,
  "unlockShift": 3,
  "targetPriority": ["depot", "vehicle_depot", "chest", "player"],   // construct behaviours or "player" | "po" | "any_construct"
  "traits": ["jumps_walls", "steals_items"],   // closed set: swarm, ranged, siege, suicide, jumps_walls, steals_items, airborne, pushes_constructs, flees_players
  "traitParams?": { "stealDropDistance": 3 },
  "drops": [ { "item": "stolen", "chance": 1.0 } ],
  "mega": { "hpMult": 2.5, "dmgMult": 1.5, "scale": 1.4, "speedMult": 0.9, "auraRadius": 8, "auraSpeedMult": 1.2 }
}
```

Balloon targetPriority is `["turret", "depot", "vehicle_depot", "sorter"]`. Giant targetPriority prefers depots and sorters (not generators).

### 2.11 WaveDef (content/waves/waves.json)

```jsonc
{
  "baseBudget": [0, 120, 220, 360, 560],
  "waveScalePerExtraPlayer": 0.35,
  "soloMult": 0.8,
  "soloMaxPlayers": 2,
  "pulses": 6,
  "pulseIntervalSeconds": 15,
  "finalePulseMult": 1.5,
  "warningSeconds": 15,
  "raidWindowSeconds": 90,
  "megaChanceBase": 0.05,
  "megaChancePerShift": 0.02,
  "weightsByShift": {
    "2": { "barbarian": 60, "archer": 30 },
    "3": { "barbarian": 40, "archer": 30, "giant": 15, "wall_breaker": 10, "hog_rider": 5 }
  },
  "caps": { "tank": { "4": 1, "5": 3 } }
}
```

### 2.12 StampDef (content/stamps/*.json)

```jsonc
{
  "id": "double_raids",
  "name": "Double Raids",
  "tier": 2,
  "scoreMult": 1.25,
  "modifiers": [ { "stat": "RaidAlsoAtStart", "op": "add", "value": 1 } ],
  "rules": ["raid_at_delivery_start"]
}
```

### 2.13 ArchetypeDef (content/world/archetypes.json)

```jsonc
{
  "id": "small_island",
  "weight": 50,
  "sizeTiles": [300, 300],
  "towns": [ { "size": "medium", "count": 1 } ],
  "districtHouseCounts": [10, 8, 10, 10, 12],
  "apartmentComplexes": { "fromDistrict": 4, "max": 1 },
  "businessDocks": { "fromDistrict": 4, "max": 1 },
  "forestRatioMin": 0.25,
  "resourceMultiplier": 3.0,
  "rankRequired": 1
}
```

### 2.14 Balance (content/balance.json)

Flat key/value file for every tunable not owned by another def: baseQuota, playerScaleExponent (0.65), spawnOverhead (1.6), prepSeconds, deliverySeconds, paydaySeconds, draftSeconds, complaintDecayPerSecond, complaintInspectorThreshold, respawnSeconds, deathBagDespawnSeconds, worldItemDespawnSeconds, interestRadius, playerHp, playerRegen, walkSpeed, sprintSpeed, swimSpeed, weightSpeedFloor, npcSpeedRatio (0.6), operatedBeltMult (1.5), salvageRatioDelivery (0.5), rerollsPerRun (1), rankXpPerRank (500), throwSpeedByWeightClass, etc. Chapter 11 lists values. There are no power-related balance keys.

### 2.15 Streets (content/streets.json), Unlocks (content/unlocks.json)

```jsonc
// streets.json
{ "names": ["Larch Lane", "Saltmarsh Row", "Pelican Drive", "..."] }

// unlocks.json
{ "ranks": [ { "rank": 2, "unlocks": [ { "kit": "sea" } ] }, { "rank": 3, "unlocks": [ { "archetype": "large_island" } ] } ] }
```

## 3. Runtime state

### 3.1 RunSettings (lobby → server; replicated in JoinState)

```jsonc
{
  "seed": 2134567890,
  "archetype": "small_island",
  "stamps": ["cursed_mail", "double_raids"],
  "maxPlayers": 8,
  "visibility": "friends",         // public | friends | invite
  "hostKit": "land",
  "protocolHash": "a91f...",
  "contentHash": "77c2..."
}
```

### 3.2 RunState (replicated as PhaseChanged payload plus 5 Hz global block)

```jsonc
{
  "phase": "delivery",             // lobby | generating | prep | delivery | raid | payday | draft | results | run_over | victory
  "shift": 3,
  "phaseDeadlineTick": 54000,
  "playerCountForScaling": 4,
  "wallet": 1820,
  "shiftEarnings": 640,
  "quota": 2214,
  "complaint": 23,
  "unlockedDistricts": [1, 2, 3],
  "teamPerks": ["express_lane"],
  "ownedBlueprints": ["bp_sorting", "bp_truck"],
  "rerollsRemaining": { "playerId": 1 },
  "payload?": {                    // per phase
    "shopOffer": ["bandage", "oil_can", "bp_motorboat"],
    "draftCards": ["express_lane", "big_pockets", "presort"],
    "results": { "score": 12345, "xpPerPlayer": {}, "stats": {} }
  }
}
```

### 3.3 PlayerState (snapshot entity)

```jsonc
{
  "entityId": 16777217,
  "profileId": "steam:7656...",
  "name": "Jules",
  "position": [0, 0, 0], "yaw": 0.0,     // quantised on wire
  "animState": "run",
  "heldSlot": 3,
  "hp": 100,
  "weightPoints": 12,
  "vehicleId?": 33554433, "seat?": 0,
  "personalPerks": ["long_legs", "long_legs"],
  "lastProcessedInputTick": 53990,      // for reconciliation, sent only to owner
  "containers": { "hotbar": 101, "inventory": 102, "backpack?": 103 }   // container ids
}
```

### 3.4 Container state and ops

```jsonc
// Full state (sent on open / join)
{
  "containerId": 102,
  "def": "player_inventory",
  "version": 87,
  "entries": [
    { "entryId": 5001, "x": 0, "y": 0, "rotated": false,
      "item": { "kind": "mail", "mailKind": "letter", "addressId": "1:4:13", "mailIds": [9001, 9002, 9003], "value": 8 } },
    { "entryId": 5002, "x": 2, "y": 0, "rotated": true,
      "item": { "kind": "item", "itemId": "log", "count": 6 } }
  ]
}

// InventoryOp request (client -> server)
{ "reqId": 771, "op": "move", "fromContainer": 102, "entryId": 5002, "toContainer": 210, "x": 4, "y": 1, "rotated": false, "count?": 3 }

// ops: move | split | merge | sort | drop | deliver | quick_move | throw

// InventoryOp event (server -> clients viewing either container)
{ "container": 210, "version": 12, "apply": [ { "add": { "entryId": 5002 } } ], "reqId?": 771, "ok": true }
```

### 3.5 ConstructState (event on place/config; HP in snapshot while damaged)

```jsonc
{
  "entityId": 50331649,
  "def": "address_sorter_mk1",
  "tile": [148, 152], "rotation": 1,
  "owner": 16777217,
  "hp": 500,
  "ruin": false,
  "config?": {
    "sorter": { "outputs": [ { "side": "north", "filters": [ { "street": 4 } ] }, { "side": "east", "filters": [ { "district": 2 } ] } ] },
    "route": { "stops": [ { "address": "1:4:13" }, { "district": 2 }, { "construct": 50331700 } ], "npcHired": true },
    "inserterFilter": { "kind": "letter" }
  }
}
```

There is no `generatorFuel` field.

### 3.6 Belt lane events

```jsonc
{ "type": "lane_insert", "segment": 300, "lane": 0, "mailId": 9004, "mailKind": "small", "addressId": "1:4:13", "pos": 0.0, "tick": 53991 }
{ "type": "lane_remove", "segment": 300, "lane": 0, "mailId": 9004, "tick": 54020 }
{ "type": "lane_checksum", "segment": 300, "lane": 0, "count": 7, "hash": 2671514308, "tick": 54030 }
{ "type": "lane_state", "segment": 300, "lane": 0, "items": [ { "mailId": 9004, "pos": 3.25 } ], "speed": 2.0, "tick": 54031 }
```

### 3.7 EnemyState (snapshot, interest-managed)

```jsonc
{ "entityId": 33554500, "def": "giant", "mega": false, "position": [0, 0, 0], "yaw": 0, "animState": "attack", "hpPct": 180, "targetId?": 50331649 }
```

### 3.8 MetaProfile (local persistence)

```jsonc
{
  "profileId": "steam:7656...",
  "displayName": "Jules",
  "rankXp": 3400,
  "unlocks": { "kits": ["land", "sea"], "archetypes": ["small_island", "large_island"], "stampTiers": 2, "perkPools": ["base", "sorting"], "cosmetics": ["cap_red"] },
  "cosmetics": { "uniform": "default", "cap": "cap_red" },
  "stats": { "runs": 14, "wins": 3, "deliveries": 2310, "misdeliveries": 88, "bestScore": 21400 },
  "recentRuns": [ { "seedString": "PM1-SMALL-7F3A9C21-CM.DR", "shiftsCompleted": 5, "victory": true, "score": 21400, "players": 3, "date": "2026-09-02" } ],
  "settings": { "sensitivity": 1.0, "invertY": false, "fov": 80 }
}
```

### 3.9 JoinState (server → joining client, channel 2, chunked)

```jsonc
{
  "settings": RunSettings,
  "worldHash": "...",
  "world?": WorldTables,             // chapter 13 §3: v1 sends generated tiles/lots/addresses instead of regenerating
  "worldDeltas": { "depletedNodes": [], "flattenedTiles": [[0, 0, 0]], "ruins": [] },
  "constructs": [ConstructState],
  "vehicles": [VehicleState],
  "players": [PlayerState],
  "runState": RunState,
  "containers": [ContainerState]   // the joining player's own containers + PO Intake/Depot summaries
}
```

## 4. Wire encoding

- Messages are binary; a hand-written BitWriter/BitReader in Sim/Net with per-message Serialize/Deserialize and a generated schema hash. Strings (addresses, names) are interned per run into uint16 ids after JoinState. (v1: Godot RPC serialisation of the same payload shapes — chapter 13 §3.)
- Snapshot entity fields use fixed quantisation: position int32 cm, yaw uint16 (0–65535 → 0–360°), HP uint8 percent, anim uint8.
- Reliable events are batched per tick into one packet on channel 1.

## 5. Validation rules (ContentValidator)

- All ids unique per def type; all references resolve.
- Every MailKindDef.acceptedBy entry is a DestinationTypeDef id.
- Every PerkDef.modifiers[].stat is in the Stat enum; every rules[] flag is in the RuleFlag enum.
- WaveDef.weightsByShift only references enemies with unlockShift <= shift.
- ArchetypeDef.districtHouseCounts sum lies within the doc's population band for the town size.
- Sum of MailMixDef.shares per shift = 1.0 ± 0.001.
- Every RecipeDef.blueprint references a ShopItemDef of kind blueprint.
- Every RecipeDef.produces a building (never an item); craftSeconds must equal 0.
- BuildingDef must not declare powerDraw, powerSupply, or behaviours generator | pole | cable.
