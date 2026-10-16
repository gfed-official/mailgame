# Performative Mail — Implementation Specification

Co-op (1–8 player) rogue-lite logistics game. Players run a post office on a procedurally generated island, deliver letters and packages to addressed destinations, and scale from hand delivery to automated sorting, vehicles, ships, and trains while defending their constructs from raiding enemies.

This specification is derived from the "Performative Mail" design document ("Mail Game Doc"). Where that document leaves numbers, structure, or technology open, this spec fills the gap with explicit defaults. Every number in this spec is a first-pass baseline and is expected to be tuned.

> **Read chapter 13 first.** Chapters 01–12 describe the full Arcade design. Chapter 13 records the v1 scope cut and the decisions made for a solo/friend-sized team. Where 13 conflicts with an earlier chapter, 13 wins.

## Chapters

| # | Document | Contents |
|---|---|---|
| 00 | README | This index: pillars, scope, assumptions, glossary |
| 01 | Run Structure | Arcade rogue-lite loop, quota, perks, meta-progression, difficulty modifiers, win/lose |
| 02 | World Generation | Seeded generation pipeline, starting areas, towns, addresses, roads, resources |
| 03 | Mail, Inventory, Economy | Mail types, destinations, grid inventory rules, money |
| 04 | Automation | Conveyors, pipes, sorters, depots, ports, rail, NPC drivers |
| 05 | Combat | Enemy roster, waves, mega variants, defenses, construct HP |
| 06 | Multiplayer and Netcode | Authority model, tick/snapshot model, per-system replication, lobby |
| 07 | Technical Architecture | Module layout, simulation loop, data-driven content, save and run state |
| 08 | Data Schemas | Schemas for items, mail, buildings, perks, enemies, run state |
| 09 | UI and UX | HUD, grid inventory UI, build mode, map/address, shop/draft, lobby |
| 10 | Content Tables | Initial items, containers, buildings, perks, enemies, events |
| 11 | Balance | Baseline numbers: quota curve, prices, speeds, HP |
| 12 | Milestones | Build order M0–M5 with acceptance criteria and risks |
| 13 | v1 Cut and Decisions | Scope cut for a solo/friend team, netcode simplification, additions (throwing, voice, water), spec fixes, revised build order |

## Design pillars

1. **The mail must flow.** Every system exists to move addressed items from the Post Office to the right mailbox. Automation, vehicles, and combat are all in service of throughput and accuracy.
2. **Friendslop surface, factory depth.** The game reads as a silly co-op delivery sim. Under it are open-ended logistics systems (sorting, routing, transport networks) with real optimization space.
3. **Hands beat machines, until they don't.** Autonomous systems work without players, but a player operating a machine or vehicle is always faster. Players should never feel that automation removed their reason to move.
4. **Short runs, long game.** Arcade runs are 20–30 minutes and always end. Meta-progression, seeds, and modifiers make each run different; nothing carried between runs makes a run easier by default.
5. **Scales from 1 to 8.** Mail volume, quota, and raid size scale with player count sublinearly so a solo run is winnable and an 8-player run is busy rather than trivial.

## Scope

### In scope (Arcade v1)

- Arcade mode: 5-shift rogue-lite run, seeded, 1–8 players, online co-op.
- One-island maps (Small Island and Large Island archetypes) with districts that unlock during the run.
- Hand delivery, grid inventory, backpack, bike, mail truck.
- Conveyors, pipes, splitters/mergers, address sorters, inserters, depots, vehicle depot with NPC drivers. Constructs run without a power/energy system.
- Sea path: small and medium boats, small port.
- Combat: 7 enemy types plus mega variants, night raids, walls, gates, turrets, repair.
- Run-scoped perks, shop, meta-progression (Postal Rank), difficulty modifiers ("Postage Stamps").
- Listen-server hosting plus headless dedicated server export.
- Materials from harvesting and the shop. No crafting / forge loop. Building placement still consumes material recipes.

### Deferred (Free Play and later Arcade updates)

- Free Play open world, persistent saves, optional story.
- Land archetype (10–20 towns, 1–3 cities), cities with apartment complexes at full scale.
- Mail semi, large boat, deep water port, rail network.
- Space tech tree (convergence of land and sea).
- Host migration, cross-platform play, mod support.
- Crafting / refining loops and power / wattage energy management (explicitly out of v1).

Every system is designed so Free Play reuses it unchanged: Free Play is "Arcade with no shift clock, no quota, a larger map, and a full tech tree".

## Assumptions

These are decisions the design document left open. They are stated once here and treated as fixed elsewhere in the spec.

| Topic | Decision | Rationale and alternative |
|---|---|---|
| Engine | Godot 4.x, C# (.NET) | Open source, headless server export, high-level multiplayer API (RPCs, MultiplayerSpawner, MultiplayerSynchronizer), Steam for relay/lobbies. Alternative: Unity with Netcode for GameObjects and Unity Transport/Relay. The architecture in chapter 07 is engine-neutral apart from named nodes. See chapter 13 §3: v1 uses the high-level API rather than the custom wire stack described in chapters 06–08. |
| Perspective and art | 3D, third-person, stylized low-poly | Matches the references (Satisfactory, Raft, Muck, Crab Game). Low-poly keeps 8-player scenes and belt-heavy factories cheap. |
| Network authority | Server-authoritative, listen server or dedicated | Doc lists 1–8 players and "no idea how netcode works". Authoritative server is the simplest correct model for shared inventories and factories. No host migration in v1. |
| Session model | Online co-op, lobby-based, join-in-progress during Prep phase only | Joining mid-shift would break quota scaling and spawn budgets. |
| Money | Team-shared in Arcade | Quota is a team goal. Free Play may add a personal wallet toggle. |
| Starting-area weights | Small Island 50%, Large Island 30%, Land 20% | Doc gives only the 50%. Land archetype is Free Play only in v1, so Arcade rolls Small 62.5% / Large 37.5% (same ratio). |
| Physical units | 1 unit = 1 metre, 1 grid tile (building) = 2 m | Buildings, belts, and roads snap to a 2 m grid; character movement is free. |
| Fixed simulation tick | 30 Hz server simulation, 20 Hz snapshot send rate | See chapter 06. |
| Crafting | None in Arcade v1 | Refined materials and tools come from kits, the shop, and enemy drops. Building placement still consumes material recipes. |
| Power / energy | None in Arcade v1 | Belts, pipes, sorters, inserters, and turrets always run at full rated speed. No generators, cables, poles, wattage, or brownout. |

## Glossary

| Term | Meaning |
|---|---|
| Run | One complete Arcade session, from seed to Victory or Run Over. |
| Shift | One "day" of a run. A run has 5 shifts. Each shift is Prep → Delivery → Raid → Payday → Draft. |
| Post Office (PO) | The team's central building. Mail spawns here. If it is destroyed the run ends. |
| Mail | Any deliverable item: Letter, Postcard, Small/Medium/Large Package, Cargo. Each has exactly one destination address. |
| Destination | A building that accepts mail: House (one mailbox), Apartment Complex (mail room with slots), PO Box Bank, Business Dock. |
| Address | Human-readable identifier `<number> <street>, <district>` (plus Unit N for apartments). Unique per map. |
| District | A contiguous set of streets. Arcade unlocks one district per shift. |
| Quota | Money the team must earn during a shift to continue. |
| Complaint meter | Team-wide penalty gauge raised by misdelivery and late mail. High values increase raid size. |
| Perk | Run-scoped upgrade drafted after each successful shift. Discarded when the run ends. |
| Postal Rank | Meta-progression level. Unlocks content, never raises player power inside a run. |
| Postage Stamp | Optional difficulty modifier selected in the lobby. Increases score multiplier. |
| Construct | Any player-built object: belt, pipe, wall, depot, turret, etc. Constructs have HP. |
| Carrier | A player character. |
| Container | Anything with a grid inventory: player inventory, backpack, bike, truck, boat, depot, chest. |
| Server | The authoritative simulation. Either the hosting player's process (listen server) or a headless process. |
| Client | A player's process that renders and sends inputs. The host is also a client. |
| Build recipe | Material cost consumed when placing a construct. Not a crafting loop: there is no forge or handcrafting station. |

## Conventions used in this spec

- **Tunable**: any value marked *tunable* lives in a data file, not code, and is expected to change during balancing. Chapter 11 collects the baselines.
- **Server-only / Client-only / Shared**: labels on systems and fields indicating where the code runs. See chapter 06.
- Schemas in chapter 08 are given as JSON with comments; the actual on-disk format may be Godot .tres Resources with identical field names.
- Distances in metres, time in seconds, speeds in m/s, money in "¢" (a single integer currency; display as $ with two decimals).
