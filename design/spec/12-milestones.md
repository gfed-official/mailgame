# 12 — Milestones, Acceptance Criteria, and Risks

Build order for Arcade v1. Milestones are ordered by dependency and by risk: netcode and the delivery loop first (the unknowns), automation and combat next (the depth), rogue-lite wrapper and polish last (the retention). Each milestone lists the systems touched and the acceptance criteria that gate the next one. No calendar estimates are given; difficulty is characterised by the components involved.

> **Superseded by chapter 13 §6**, which prepends an M-1 greybox prototype, simplifies M0, and trims M2–M5 to the v1 cut. This chapter is kept as the full-scope reference.

```
M0 Foundation -> M1 Vertical slice run -> M2 Automation -> M4 Rogue-lite depth -> M5 Hardening
                                         -> M3 Combat  -> M4
```

## M0 — Foundation: netcode, movement, hand delivery on a fixed map

Scope: chapters 03 (inventory, destinations), 06 (netcode core), 07 (architecture skeleton).

Systems:

- Solution layout with Sim (no Godot refs), Server, Client, App; ContentValidator skeleton; xUnit projects.
- ServerRuntime / ClientRuntime with loopback and ENet transports; protocol hash check.
- Fixed 30 Hz tick; input packets; player movement with client prediction and reconciliation; snapshot interpolation for remote players; interest management stub (everything global).
- Grid inventory (GridContainer): placement, rotation, stacking by address, quick-move, sort; InventoryOp request/validate/apply with versioning and optimistic UI rollback.
- Hand-authored test map (one street, 10 houses, PO with Intake) loaded from a static file.
- Mail spawn into Intake on a timer; mailbox acceptance rules; wallet; misdelivery penalty.
- Minimal HUD: timer, wallet, interact prompt with address match indicator; inventory panel.
- Headless bot client that walks to a mailbox and delivers.

Acceptance criteria:

- 8 clients (2 real, 6 bots) connected to a listen server for 10 min with no desync in inventories (server and client container hashes match on every version).
- Prediction error under 200 ms simulated latency with 5% loss stays below 10 cm RMS for the local player; no visible rubber-banding at 100 ms.
- Two players moving items between the same chest concurrently never produce a duplicate or lost item across 10 000 randomized ops (fuzz test).
- Delivery and misdelivery pay/deduct exactly per §2.2 of chapter 03 in unit tests.
- Server tick ≤ 2 ms with 8 players on the test map.

## M1 — Vertical slice: one full seeded Arcade run (hand delivery, shop, perks)

Scope: chapters 01, 02, 03, 08 (content loading), 09 (lobby, HUD, payday, draft, results).

Systems:

- World generation pipeline (all stages) for Small Island; worldHash; client-side regeneration; SeedViewer tool.
- Districts and per-shift unlock; address assignment; street names; house model variants and colour coding.
- Run state machine with all phases; quota; spawn budget and mail mix; complaint meter; deadlines; death and respawn; death bag.
- Shop (blueprints, bike, consumables, refined materials); bike as first vehicle (driver prediction path).
- Perk system (StatSheet, modifiers, team/personal, prerequisites, exclusions) with ~12 perks.
- Lobby with seed, archetype, kit; join-in-progress in Prep; disconnect grace.
- Content pipeline: JSON defs, validator in CI, hot reload.
- Results screen and MetaProfile persistence (XP only; unlocks table wired but shallow).
- Resource nodes and harvesting (wood, fiber, stone, ore); hand-built Wooden Wall and Chest from harvested materials so building exists before automation. No forge crafting.

Acceptance criteria:

- Same seed and settings produce bit-identical worldHash on Windows and Linux clients.
- A solo tester can win shift 1 and fail shift 2 by hand delivery alone; with a bike and quick play, shift 2 is winnable (BalanceSim and one human confirmation).
- A 4-player session completes a 5-shift run (no raids yet) in under 32 minutes with quota met each shift using bikes and hand sorting.
- Join state ≤ 200 KB after 3 shifts; a player joining in Prep of shift 3 sees identical world and containers.
- Generation ≤ 3 s on a mid-range laptop; 100 random seeds pass validation without manual intervention (≤ 5% reroll rate).

## M2 — Automation: belts, sorters, inserters, depots, trucks, NPC drivers, boats

Scope: chapter 04, netcode §4 (belt replication), UI (build mode, filter panel, route editor, map).

Systems:

- Build mode with ghost validity, drag lines, deconstruct, pipette; construct registry and placement validation; terrain flattening deltas.
- Belt segments (compiled runs, two lanes, ramps, elevated), splitters, mergers; Intake outfeed; endpoints into mailboxes, containers, vehicles. Constructs always run at full rated speed (no power grids).
- Belt replication: LaneInsert/Remove/Checksum/State; client visual sim; interest management for segments.
- Address Sorter with filter UI; Inserters; Depot.
- Pipes (inlet filters, junctions, outlets, underground) behind bp_pipes.
- Mail Truck; Vehicle Depot with route console; NPC driver behaviour (routing graph, stops, delivery, return, flee stub); player takeover.
- Sea path: rowboat (Sea kit blueprint + build placement), pier, motorboat, small port, NPC captain; water navmesh.
- Oil Pump for vehicle fuel (no power draw).
- Map screen with layers, filter chips, pings, route editor.

Acceptance criteria:

- 5000 belt items simulated server-side within the 8 ms tick budget; 400 belt tiles visible on a client at ≤ 60 fps mid-range.
- Belt visual desync: after 10 min of a 30-segment factory under 100 ms / 2% loss, checksum resends ≤ 1 per segment per minute and no item is ever rendered at an endpoint before the server confirms.
- Reference factory (chapter 11 §9.1) built by a tester from harvested/shop materials within one Prep + one Delivery on a Small Island seed.
- A sorter with per-street filters routes 1000 generated items with zero misroutes (unit test); overflow receives exactly the unmatched items.
- NPC truck completes a 3-stop route and delivers only matching mail (no misdeliveries) in an integration test; player takeover and hand-back within one tick.
- Solo shift 3 and shift 5 viability checks pass in BalanceSim with the automation agents.

## M3 — Combat: enemies, waves, defenses, construct damage

Scope: chapter 05, enemy replication, defense UI.

Systems:

- Enemy agents: server-side movement on routing graph plus steering; behaviour trees per trait (swarm, ranged, siege, suicide, jumps_walls, steals_items, airborne, pushes_constructs, flees_players); target priority.
- Wave scheduler: budget, pulses, spawn edges, weights, caps, mega rolls, warning, end-of-raid flee.
- Damage model, construct HP replication while damaged, ruins and rebuild, item spill, PO destruction ends run.
- Player combat: melee arc, hitscan, lag compensation rewind buffer; weapons and bandages; Package Cannon.
- Defenses: walls, gate, spikes, turret (auto and operated), alarm post, repair hammer.
- Drops, Lost Parcels, Cursed Mail flag support.
- HUD: raid warning, compass markers, under-attack indicators, health bars.

Acceptance criteria:

- 40 enemies in interest replicated to 8 clients within the 40 kbps per-client budget; 80 kbps worst case.
- Enemy pathing never stalls: a fuzz test spawning waves against 200 random wall layouts finds a path or a wall to break within 2 s for every enemy.
- Wall Breakers hit walls, Hog Riders hop them and steal from depots, Balloons target turrets (not power infrastructure), Tanks push belts: each verified in scripted scenario tests.
- Sanity checks from chapter 11 §10.1 reproduced in BalanceSim with combat agents (a solo player with 6 walls loses no belts on shift 2).
- No friendly fire; no enemy damage to mailboxes or houses (assertion tests).

## M4 — Rogue-lite depth: full perk pool, stamps, events, meta unlocks, Large Island

Scope: chapters 01 (stamps, events, unlocks), 02 (Large Island), 10 (full content).

Systems:

- Full ~28-perk pool including rule-flag perks (Insured, Priority Post); rerolls; personal cap. No power-related perks.
- Postage Stamps (all 8) with score multipliers and rule flags (Double Raids, No Roads surface speeds, Postal Audit, Megamail, Skeleton Crew, Cursed Mail mini-raids). No Brownout stamp.
- Run events (9) with scheduling and UI. No power_surge.
- Postal Rank unlock tracks, cosmetics, kits (Sea, Pipes), practice seed, daily seed string.
- Large Island archetype: multiple towns, inlets, ferry lanes, Business Docks, Cargo, Apartment Complexes with unit slots, PO Box Bank.
- Payday MVP callouts, forecast, extended results, seed copy.
- Onboarding tips.

Acceptance criteria:

- Every perk and stamp is applied purely through data plus the closed Stat/RuleFlag enums (code review: no perk-specific branches outside rule flag handlers).
- 100 Large Island seeds validate (all destinations reachable by walk or ferry lane; ≥ 1 Business Dock).
- Same seed + stamps + player count reproduces the mail schedule and perk offers exactly (replay test).
- Telemetry shows target outcomes trending toward chapter 11 §12 (solo win ~25%, 4-player ~50%) across an internal playtest of ≥ 40 runs; tuning changes made only in data.

## M5 — Hardening and release: dedicated server, performance, accessibility, Free Play toggle

Scope: chapters 06 (dedicated, testing), 07 (performance, tooling), 09 (accessibility).

Systems:

- Headless Linux dedicated server export with CLI settings, auto-shutdown, crash RunSnapshot dump; Steam relay path and direct IP both exercised.
- Network conditioner and bot load tests in CI (8 bots, 15 min, shift-5 factory).
- Server replay recording; performance passes to hit chapter 07 §8 targets; LOD and belt far-rendering.
- Accessibility: colour-blind patterns, text scale, toggle interact, captions, reduced motion. Full gamepad pass.
- Free Play toggle (no quota, no shift clock, persistent save via SaveGame) as a hidden/experimental mode to validate that the systems generalise; not marketed in v1.
- Localisation scaffolding (string tables; addresses and street names remain English in v1).

Acceptance criteria:

- 8-player, 30-minute run on a dedicated server with 100 ms / 2% loss on all clients: zero desync incidents, per-client bandwidth within budget, server tick p99 ≤ 8 ms.
- Client 1080p mid-range: p99 frame time ≤ 20 ms during a shift-5 finale with a 400-tile factory.
- Crash-free rate ≥ 99.5% of sessions in the final internal test cycle.
- All UI flows completable on gamepad; accessibility settings verified by checklist.

## Cross-cutting risks

| Risk | Impact | Mitigation |
|---|---|---|
| Belt replication cost or visible desync | Factories feel janky; bandwidth blowout at 8 players | Event + checksum model (chapter 06 §4); segment compilation; interest management for segments; M2 acceptance tests gate on it; fallback is lowering visual item density beyond 30 m |
| 8-player quota and raid tuning | Groups find runs trivial or impossible | playerScale power law and separate wave scaling; BalanceSim regression tests; telemetry-driven tuning in M4 |
| Host quality dependence (listen server) | Poor host ruins the session | Dedicated server from M5; host bandwidth check in lobby (warn below 2 Mbps up); no host migration accepted as a v1 limitation |
| World generation determinism across platforms | Hash mismatch disconnects | Integer-quantised generation, in-repo noise, cross-platform hash test in CI from M1 |
| Grid inventory UX at scale (trucks, depots) | Tedium instead of depth | Manifest view for large containers, Sort buttons, "Take all for district" actions; the manual sort is intentionally strong |
| Combat overshadowing logistics | Game becomes a tower defense | Raids time-boxed to 90 s; enemies target constructs over players in small groups; defenses are cheap; no kill requirement |
| Content sprawl | Delayed v1 | Content is data; the initial tables in chapter 10 are the v1 cut; Land archetype, semi, large boat, rail, space deferred explicitly |

## Definition of done for Arcade v1

- All M0–M5 acceptance criteria met.
- Content tables in chapter 10 implemented and validated.
- Balance targets in chapter 11 §12 approached within the internal playtest.
- A new player can host, invite friends, and complete a run without documentation beyond the onboarding tips.
- Every deferred item in the README "Scope" section is either untouched or hidden behind a feature flag.
