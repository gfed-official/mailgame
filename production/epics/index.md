# Epics Index

Last Updated: 2026-10-21 (counts from the story headers on main a6dfb98 plus PR #16, `/workspace/scripts/count-story-status.sh`: 93 Complete, 1 In Progress (content-data 018), 6 Blocked, 107 Ready — 207 stories; godot-host-shell 003, grid-inventory 004/005, throwing-catching 001 and manual-delivery 001 (PR #14) Complete; grid-inventory 006 Complete, PR #16)
Engine: Godot 4.7.1 pinned (the box has Godot 4.7.2 mono at `~/.local/bin/godot` — see HANDOFF.md), C# (.NET 8 Sim / net10.0 tests)
Scope: M-1a slice (solo greybox: delivery + throwing)

| Epic | Layer | System | GDD | Stories | Status |
|------|-------|--------|-----|---------|--------|
| [simulation-core](simulation-core/EPIC.md) | Foundation | Simulation Core | design/gdd/simulation-core.md | 30 stories (28 Complete, 2 Ready) | Ready |
| [content-data](content-data/EPIC.md) | Foundation | Content & Data Definitions (incl. StatSheet) | design/gdd/content-data-definitions.md | 19 stories (17 Complete, 1 In Progress, 1 Ready) | Ready |
| [input-controls](input-controls/EPIC.md) | Foundation | Input & Controls | design/gdd/input-controls.md | 29 stories (6 Complete, 22 Ready, 1 Blocked) | Ready |
| [godot-host-shell](godot-host-shell/EPIC.md) | Foundation | Godot host shell (Presentation half of Simulation Core + Content & Data) | design/gdd/simulation-core.md, design/gdd/content-data-definitions.md | 11 stories (3 Complete, 8 Ready) | Ready |
| [grid-inventory](grid-inventory/EPIC.md) | Core | Grid Inventory & Containers (M-1a: hotbar + own panel) | design/gdd/grid-inventory.md | 24 stories (6 Complete, 17 Ready, 1 Blocked) | Ready |
| [world-stub](world-stub/EPIC.md) | Core | World Generation — M-1a hand-authored street stub (`IWorldQuery`) | none (consumer GDDs + architecture.md Sim.World) | 7 stories (7 Complete) | Complete (Provisional — no GDD; ADR-0026 Accepted) |
| [network-adapter-loopback](network-adapter-loopback/EPIC.md) | Core | Network adapter core — loopback (Networking & Session seam) | design/gdd/simulation-core.md §Network (no Networking GDD) | 9 stories (7 Complete, 2 Ready) | Ready |
| [mail-destinations](mail-destinations/EPIC.md) | Feature | Mail & Destinations (M-1a slice) | design/gdd/mail-destinations.md | 16 stories (6 Complete, 10 Ready) | Ready |
| [player-character](player-character/EPIC.md) | Feature | Player Character & Movement (M-1a slice) | design/gdd/player-character-movement.md | 27 stories (11 Complete, 16 Ready) | Ready |
| [manual-delivery](manual-delivery/EPIC.md) | Feature | Manual Delivery | design/gdd/manual-delivery.md | 11 stories (1 Complete, 8 Ready, 2 Blocked) | Ready |
| [throwing-catching](throwing-catching/EPIC.md) | Feature | Throwing & Catching (M-1a slice + Sim-side catch pull-forward) | design/gdd/throwing-catching.md | 24 stories (1 Complete, 21 Ready, 2 Blocked) | Ready |

## M-1a dependency / implementation order

> **Story-level order:** see [implementation-order.md](implementation-order.md) — a single cross-epic order in parallel waves, generated from every story's Depends on (2026-10-16; no dependency cycles). The epic-level list below is the original coarse plan.

1. `simulation-core` (+ `network-adapter-loopback` alongside — `FrameRunner` needs the loopback transport)
2. `content-data`
3. `grid-inventory` (Sim half)
4. `world-stub` → `mail-destinations`
5. `player-character` (Sim half: solver + interaction framework)
6. `manual-delivery`
7. `throwing-catching`
8. `input-controls` router core (Client.Core) — HUD core has no epic yet (Presentation layer not run)
9. `godot-host-shell`, then each epic's presentation stories (panel view, PlayerView/camera, mailbox views, aim ribbon) and the Godot input adapter

## ADRs to write before the matching `/create-stories` (untraced M-1a requirements)

| ADR (planned) | Unblocks |
|---|---|
| ADR-0012 Input router core & Godot input adapter | input-controls |
| ADR-0013 Carrier kinematic solver | player-character, world-stub |
| ADR-0014 Interaction framework & hold timing | player-character, manual-delivery, input-controls |
| ADR-0015 Ballistic, sweep & flight resolution | throwing-catching, world-stub |
| ADR-0016 ClientLayer & presentation timing | manual-delivery, throwing-catching, mail-destinations |
| ADR-0017 Mail table, registry & IWalletGate | mail-destinations |
| ADR-0019 Godot presentation scene architecture | godot-host-shell, all presentation stories |
| ADR-0026 (stub section) `IWorldQuery` | world-stub |
