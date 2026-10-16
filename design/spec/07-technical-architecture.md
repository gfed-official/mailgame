# 07 — Technical Architecture

Engine: Godot 4.x with C# (.NET 8). This chapter defines the project layout, module boundaries, the simulation loop, the separation between simulation and presentation, data-driven content loading, save and run state, performance targets, and the tooling required. The architecture is engine-neutral except where Godot node types are named.

> Chapter 13 §3 replaces the custom `Net/` serialisation layer with Godot's high-level multiplayer API and drops cross-platform deterministic worldgen for v1. The Sim/Presentation split, data-driven content, and the simulation loop are unchanged.

## 1. Guiding constraints

1. **Simulation is plain C#, presentation is Godot.** Everything the server decides (inventories, belts, enemies, economy, run state) lives in a PerformativeMail.Sim assembly with no Godot dependencies. Godot nodes render, animate, and gather input. This lets the headless server run without a scene tree for most systems, makes unit tests trivial, and keeps the client from accidentally becoming authoritative.
2. **One code path for host and dedicated.** The listen server is the same ServerRuntime object in the host process with a loopback transport.
3. **Content is data.** Items, mail kinds, buildings, recipes, perks, enemies, waves, shop, stamps, street names, and balance constants are loaded from data files (chapter 08). Adding a perk or enemy must not require a code change unless it introduces a new behaviour primitive. Recipes in Arcade v1 are build-placement costs only (no item crafting).
4. **Fixed 2 m grid, 30 Hz tick, integer money.** No floating-point money, no variable-step simulation. No power / energy simulation.

## 2. Solution layout

```
PerformativeMail/
  project.godot
  PerformativeMail.sln
  src/
    Sim/                       # PerformativeMail.Sim (netstandard2.1, no Godot refs)
      Core/                    # Tick, RNG streams, event bus, ids, fixed-point helpers
      Content/                 # Loaders + typed defs (ItemDef, BuildingDef, PerkDef, ...)
      World/                   # Generation pipeline, tile grid, routing graph, districts
      Mail/                    # MailItem, spawn scheduler, destinations, acceptance
      Inventory/               # GridContainer, placement, stacking, ops, sort packer
      Building/                # Construct registry, placement validation, ruins
      Automation/              # Belt segments, pipes, sorters, inserters, depots
      Vehicles/                # Vehicle sim, routes, NPC drivers/captains
      Combat/                  # Enemy agents, behaviour trees, waves, damage, defenses
      Run/                     # Run state machine, quota, complaint, perks, shop, stamps
      Meta/                    # MetaProfile, Postal Rank, unlock tables
      Net/                     # Message schemas, serializers, snapshot/delta, interest
      SimWorld.cs              # Root: owns all systems, Tick(dt), ApplyInput, ApplyRequest
    Server/                    # PerformativeMail.Server (Godot, headless-capable)
      ServerRuntime.cs         # Owns SimWorld, transport, replication, per-client state
      Transport/               # ENet, Steam relay, loopback
    Client/                    # PerformativeMail.Client (Godot)
      ClientRuntime.cs         # Connection, prediction, interpolation, event application
      Presentation/            # Entity views, VFX, audio, world dressing
      UI/                      # HUD, inventory, build mode, map, shop, draft, lobby
      Input/                   # Input map, controller support
    App/                       # Boot, main menu, settings, profile storage, mode selection
  content/                     # Data files (JSON or .tres), see chapter 08
    items/  mail/  buildings/  recipes/  perks/  enemies/  waves/  shop/  stamps/
    streets.json  balance.json  unlocks.json
  scenes/                      # Godot scenes for views, UI, world
  assets/                      # Models, textures, audio
  tools/                       # Content validator, seed viewer, balance sim, bot client
  tests/
    Sim.Tests/                 # xUnit tests for the Sim assembly
    Net.Tests/                 # Serializer round-trips, snapshot deltas
```

There is no `Power/` module. Constructs under Automation (and Combat turrets) always run at full rated speed.

## 3. Runtime composition

- Host process: ServerRuntime + ClientRuntime connected by a loopback transport.
- Remote client process: ClientRuntime connected over ENet or Steam relay.
- Dedicated process: ServerRuntime only, headless.
- ServerRuntime: constructs SimWorld from settings + seed, runs the 30 Hz tick on a fixed timer, drains inputs and requests from the transport into the sim, collects sim events, builds per-client snapshots, sends.
- ClientRuntime: maintains a presentation model (ClientWorld) updated from snapshots and events, runs local prediction for its own player, drives Godot views, and turns UI actions into requests.
- Host process runs both; the host's client uses a loopback transport with zero latency but the same serialization (so bugs in serialization show up for the host too).

## 4. Simulation loop

```
SimWorld.Tick(tick):
  Inputs.Apply(tick)                  // per player: movement intent, look, actions
  Players.Step()                      // kinematic movement, weight, interact timers
  Vehicles.Step()                     // arcade vehicle model, driver/NPC intent
  Agents.Step()                       // enemies, NPC drivers: BT tick, pathing, attacks
  Automation.Step()                   // belts, pipes, sorters, inserters, depots
  Mail.Step()                         // spawn scheduler, backlog, deadlines
  Economy.Flush()                     // apply pending payments/penalties, complaint decay
  Combat.Resolve()                    // pending damage, deaths, ruins, drops
  Run.Step()                          // phase timers, quota checks, wave scheduler
  Events.Flush() -> ServerRuntime     // ordered event list for replication
```

- Systems communicate through an in-process event bus (SimEvent structs) and shared registries (entities by id). No system calls another system's internals directly except through documented APIs (Inventory.TryMove, Destinations.TryDeliver, Constructs.TryPlace).
- Movement collision uses a lightweight capsule-vs-tile + capsule-vs-construct AABB solver in the Sim (not Godot physics) so the server needs no physics world. Terrain height comes from the tile grid with bilinear interpolation. Enemies and NPCs use the routing graph plus a local steering step.
- Vehicles use a 2.5D arcade model (heading, speed, grip, terrain slope) on the same tile height field. Water vehicles use the water tile mask.

### 4.1 Time and ticks

- Tick is a uint32 counter from run start. All timers are stored as tick numbers, never seconds, and converted for display.
- The run clock in the UI is derived from phaseDeadlineTick − currentTick.

### 4.2 Entity ids

- EntityId = uint32; high byte is the entity class (player, vehicle, enemy, construct, world item, mail), low 24 bits a per-class counter. Ids are allocated on the server and never reused within a run.
- Mail ids are separate uint32 counters because mail can be inside containers rather than in the world.

### 4.3 RNG

- RngStream(seed, name) wrappers over PCG32. Streams: worldgen.* (per stage), mail.spawn, mail.address, waves, perks, shop, drops, mega. Each is advanced only by its owning system so seeds reproduce independent of player timing where possible (mail spawn and perks are fully reproducible; drops and waves depend on complaint meter and thus on player behaviour).

## 5. Presentation layer (client)

- ClientWorld mirrors the subset of sim state the client knows: entities by id with interpolation buffers, containers the player has open, constructs, belt lane visual state, run state.
- Godot views are thin: PlayerView, EnemyView, VehicleView, ConstructView (one scene per building type), BeltSegmentView (instances item meshes via MultiMesh along the segment path), WorldItemView, MailboxView.
- Belt visuals: BeltSegmentView advances items locally each frame at the replicated speed and reconciles on LaneChecksum mismatch by lerping to the resent positions over 200 ms.
- Labels: mail addresses are rendered as texture-atlas text on the item mesh; the atlas is generated once per map from the address table (≤ 300 addresses).
- LOD: houses and props use 3 LODs; belts beyond 60 m switch to a static "busy belt" scrolling texture with item meshes hidden.
- Audio: delivery "cha-ching", misdelivery "thud", raid horn. Positional for constructs, 2D for run events.

## 6. Data-driven content

- All content files are validated at boot (and in CI by tools/ContentValidator) against the schemas in chapter 08: unique ids, referenced ids exist, grid sizes positive, perk modifiers reference known stat keys, recipes reference known items and produce buildings only (craftSeconds must be 0).
- Content is loaded into immutable Def records. Runtime state never mutates a Def; perks apply via StatSheet overlays (baseValue × Σmultipliers + Σadders per stat key).
- Stat keys are a closed enum defined in code (Stat.PlayerSpeed, Stat.BeltSpeedMk1, Stat.SorterFilterSlots, ...). Adding a new stat is a code change; adding a perk that uses an existing stat is not.
- Balance constants (content/balance.json) cover every number tagged *tunable* in this spec.
- Hot reload in the editor: pressing F6 in a debug build reloads content and re-applies perks for rapid balancing.

## 7. Persistence

### 7.1 MetaProfile (local, per player)

Stored in user://profile.json (plus Steam Cloud when available). Contents: profile id, display name, Postal Rank XP, unlocks, cosmetics, settings, stats (lifetime deliveries, runs, wins), last 10 run summaries. Written at every Payday and at Results.

### 7.2 Run state (Arcade)

Arcade runs are not saved mid-run in v1; a run is lost if the server exits. The server keeps a rolling in-memory RunSnapshot at each Payday so that a dedicated server crash can be investigated, and this snapshot is the same format Free Play uses for saves, so save/load support is a Free Play deliverable rather than new plumbing.

### 7.3 Free Play save (deferred)

SaveGame = settings + seed + tick + full serialized SimWorld state (constructs, containers, vehicles, world deltas, players' inventories keyed by profile id, economy). Written on the server on a timer and on shutdown; players joining load from the server as in join-in-progress.

## 8. Performance targets

| Target | Value |
|---|---|
| Server tick CPU (8 players, 400 belt tiles, 40 enemies) | ≤ 8 ms per tick on a 4-core desktop |
| Dedicated server memory | ≤ 512 MB |
| Client frame time (mid-range GPU, 1080p) | ≤ 16.6 ms in a shift-5 raid |
| World generation | ≤ 3 s client, ≤ 5 s server |
| Join state download | ≤ 200 KB typical, ≤ 2 MB worst |
| Belt items in world | up to 5000 simulated server-side without exceeding tick budget |

Belt simulation cost is bounded by storing lane items in contiguous arrays per segment and advancing with a single pass; sorters and endpoints only inspect the head item.

## 9. Tooling

| Tool | Purpose |
|---|---|
| tools/ContentValidator | CLI; fails CI on schema or reference errors |
| tools/SeedViewer | Godot tool scene: renders a seed's map top-down with districts, addresses, resources, spawn edges |
| tools/BalanceSim | Headless: simulates a run with scripted player throughput models to check quota curve viability (chapter 11) |
| tools/BotClient | Headless client with scripted behaviours for load testing |
| Debug overlay | In-game: tick timings, bandwidth per system, interest set, enemy targets, network conditioner |

## 10. Build and CI

- GitHub Actions (or equivalent): dotnet test on the Sim and Net test projects, ContentValidator, Godot headless export of the dedicated server and a client smoke test that boots, hosts, and connects a bot for 60 s.
- Exports: Windows and Linux client, Linux headless server. macOS client deferred.
- Version string major.minor.patch+protocolHash shown in the main menu and checked at connect.
