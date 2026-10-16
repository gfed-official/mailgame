# Mail Game Doc (original vision document)

Imported verbatim from the Google Doc "Copy of Mail Game Doc". This is the source the implementation spec in `design/spec/` was derived from. It is preserved as the statement of intent for the full game; `design/spec/13-v1-cut-and-decisions.md` records how v1 narrows it.

---

## Disclaimer

Everything in this document is subject to change. Numbers are arbitrary and provided more so as a way to estimate gameplay. Ideally at least 1-8 players, I have no idea how netcode works.

## Overview

The core of the game is delivering mail.

Performative Mail is inspired by games like Satisfactory, Raft, Sons of the Forest, and Minecraft's Create: Above and Beyond modpack.

The game features distinctive progression, automation, building, exploration, and conquest.

On the surface the game looks like an innocent friendslop mail delivery sim, but has significant depth and replayability due to its open-ended game systems.

### Game Modes

#### Free Play

The full, intended Core Gameplay of Performative Mail in an open-world, procedurally generated, randomly-seeded world. Deliver letters and packages to your neighbors next door all the way up to your intergalactic neighbors across the universe while collecting resources to build up your automation to scale. There will be a rough story that gets explained as you progress, but it is optional.

#### Arcade

Arcade Mode is an accelerated, rogue-lite version of the game, taking inspiration from games like Plate Up, Crab Game, and Muck.

The mode can range in difficulty but is intended to take around 20-30 minutes on average, but possibly longer depending on how the run is going.

## Core Gameplay

You and your friends start on an island town. Your sole purpose in the town is to deliver mail and packages to the other residents all over the island. Each piece of mail has a specific address it needs to go to. You start off on one of 3 starting areas:

- Small island (50%): 1 medium sized town
- Large island: 2-3 towns, with the possibility of them being a medium sized town
- Land: very expansive section of land, 10-20 towns + 1-3 cities

There are two main technology trees: land and sea. They combine in the late portion of the game as space.

Although you may start in an environment that favors one, it should be possible to migrate to the other without a ton of difficulty (usually requiring a small boat to cross the water).

The early game usually constitutes delivering mail by hand directly to mailboxes. At the successful (and correct) delivery of mail, it will immediately earn money. Delivering mail to the wrong house will subtract money, proportional to the value of the mail. This is relevant for the automation section of the game.

There will be collectible/harvestable resources around that you can use to build tools and contraptions. Some early game items usually will be a backpack, a bike, and an axe. You will also be able to build buildings.

As you progress and build more resources for very intuitive things (e.g. storage, speed, efficiency), you will begin to generate enough money and raw material for advanced constructs like conveyor belts, pipes, vehicle depots, small ports, deep water ports, railways, and more. You can combine them to create sorting systems, automated delivery, and farms.

In addition, there may be hostile enemies that will try to attack you and your constructs that you just defeat or defend against.

## Gameplay Systems

### Inventory System

The inventory system uses "size" and "stackables" like in a grid system. Items will be described by their grid size (n x m) and max stack limit.

#### Items

| Item | Grid Size | Max stack |
|---|---|---|
| Letter | 1x1 | 20 |
| Small Package | 1x2 | 1 |
| Medium Package | 2x2 | 1 |
| Large package | 2x4 | 1 |
| Cargo | 5x8 | 1 |
| More | | |

#### Carrying Devices

| Name | Capacity |
|---|---|
| Hotbar | 1x8 |
| Default inventory size | 2x8 |
| Backpack | 2x8 |
| Bike | 2x8 |
| Mail truck | 10x8 |
| Mail semi | 40x100 |
| Small boat | 2x8 |
| Medium boat | 10x8 |
| Large boat | 40x100 |
| Train car | 20x40 |
| More | |

### Mail and Packages

The core objective of the Performative Mail is to deliver mail and packages to Destinations.

### Destinations

Destinations are where mail goes to. There are many different kinds of Destinations.

- House: has a mail box to deliver mail to. Each house can only have 1 mail box
- Apartment complex:

### Population

There are several possible populations of which you will deliver mail to.

- Small towns: 8-12 houses
- Medium towns: 13-25 houses
- Large towns: 26-50 houses
- Cities: 100-200 houses, 10-20 apartment buildings

### Combat

There will be hostile enemies that try to attack players and their constructs. They range in strength and abilities focusing on swarming you. The enemy types can be described roughly like Clash of Clans units:

1. Barbarian
2. Archer
3. Giant
4. Wall breaker
5. Hog rider
6. Balloon
7. Tanks

There could also be randomly spawned "mega" versions which spawn with extra stats.

### Automation

Mail delivery can be automated via progressing through the technology trees.

Many systems work autonomously but are actually more efficient/faster when controlled via a player to have an incentive for player interaction.

| Automation Tool | Description |
|---|---|
| Conveyor belts | Requires power, automatically move items in a single direction |
| Pipes | Requires power, automatically move items in arbitrary directions |
| Car like vehicles | Can be driven by NPCs at a reduced speed |
| Ships | Can be driven by NPCs at a reduced speed |
| Trains | Requires power, requires tracks, can be driven by NPCs |

*Note: the implementation spec removed the power requirement for Arcade v1 (see `spec/00-readme.md`, Assumptions).*
