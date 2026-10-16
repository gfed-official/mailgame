# 02 — World Generation

The world is procedurally generated from a seed on the server and reproduced on every client from the same seed. Only post-generation deltas (player constructs, damage, harvested nodes) are replicated. This chapter defines the archetypes, the generation pipeline, towns and addresses, roads, resources, and the guarantees the rest of the game depends on.

> Chapter 13 §3 changes the distribution model for v1: the server generates once and sends the result; clients do not regenerate. The cross-platform determinism requirements in §2 are therefore relaxed. The pipeline itself is unchanged.

## 1. Archetypes

| Archetype | Doc weight | Arcade v1 | Land area | Towns | Cities | Notes |
|---|---|---|---|---|---|---|
| Small Island | 50% | Yes (62.5% when Land excluded) | ~600 × 600 m | 1 medium town (13–25 houses, grown to ~50 via district unlocks) | 0 | Default; single landmass, PO near centre |
| Large Island | 30% (assumed) | Yes (37.5%) — deferred by ch. 13 | ~1000 × 1000 m | 2–3 towns, at least one medium | 0 | Towns separated by water inlets or ridges; Sea path is strongly favoured |
| Land | 20% (assumed) | No (Free Play) | 3000 × 3000 m + | 10–20 towns | 1–3 cities (100–200 houses, 10–20 apartment complexes) | Requires rail and semi for reasonable throughput |

The doc's population bands are used verbatim:

| Settlement | Houses | Apartment complexes |
|---|---|---|
| Small town | 8–12 | 0 |
| Medium town | 13–25 | 0–1 |
| Large town | 26–50 | 0–2 |
| City | 100–200 | 10–20 |

In Arcade, "district unlocks" (chapter 01) grow the deliverable set each shift. Generation produces the full final town up front; districts are a partition of it, so the map does not change shape mid-run.

## 2. Determinism and RNG streams

- Generation uses a counter-based PRNG (PCG32 or xoshiro128**) seeded from seed. Each pipeline stage derives its own stream: hash(seed, "heightmap"), hash(seed, "towns"), hash(seed, "roads"), hash(seed, "addresses"), hash(seed, "resources"), hash(seed, "spawns").
- Stages consume their stream in a fixed order and never read from another stage's stream, so a change to one stage (e.g. a new resource type) does not reshuffle towns on existing seeds.
- Floating-point: all generation math runs in integer-quantised form (heights stored as int16 centimetres, positions on a 0.5 m lattice) so results are bit-identical across platforms. No engine physics or noise nodes are used in generation; noise is an in-repo implementation (OpenSimplex2 in fixed point).
- The server computes a worldHash (64-bit FNV of the final tile and address tables) and sends it to clients; a mismatch disconnects the client with a "version mismatch" error.

## 3. Pipeline

```
Seed and archetype
  -> Heightmap and coastline
  -> Biomes and water classification
  -> Settlement placement
  -> Street graph and lots
  -> Building placement and destinations
  -> Address assignment
  -> District partition
  -> Road and ferry connectivity
  -> Resource nodes
  -> Enemy spawn edges
  -> Post Office and player spawn
  -> Validation and worldHash
```

### 3.1 Heightmap and coastline

- Grid of 2 m tiles (Small Island: 300 × 300 tiles). Height from layered fixed-point OpenSimplex2 with a radial falloff for islands. Sea level = 0.
- Coastline is smoothed so every beach tile has at least 2 walkable neighbours; single-tile spits are removed.
- Slopes above 35° are flagged unbuildable (cliffs). Buildable flat area is guaranteed ≥ 30% of land tiles; if not, the falloff radius grows and the stage reruns (bounded to 8 attempts, then the seed is marked invalid and the lobby rerolls).

### 3.2 Biomes and water

| Biome | Rule | Resources |
|---|---|---|
| Beach | land tiles within 3 tiles of water | Sand, Driftwood |
| Grassland | default lowland | Fiber, Berries |
| Forest | noise mask on grassland, ≥ 25% of land | Wood (trees), Fiber |
| Rocky | elevation above 60% of max or slope > 20° | Stone, Iron ore |
| Shallow water | depth < 2 m | Rowboat only; buildable pier |
| Deep water | depth ≥ 2 m | Medium boat and up; Small Port needs adjacent deep water |
| Marsh (Large Island) | low flat land near inlets | Oil seep |

### 3.3 Settlement placement

- Candidate sites: connected regions of flat (slope < 10°) grassland/beach of at least minTownTiles (small 120, medium 260, large 520). Sites are scored by flatness, coast proximity (towns like the coast), and distance to other towns (≥ 150 m).
- Small Island: exactly one town, medium size, expanded to large-town house counts through districts. Large Island: 2–3 towns, one guaranteed medium; the others small or medium.
- The Post Office site is chosen inside the first town on the flattest 6 × 6 tile patch nearest the town centroid.

### 3.4 Street graph and lots

- Streets form a grid with jitter: a main street through the town centroid along the longest flat axis, cross streets every 10–14 tiles, with 20% of intersections removed and 15% of segments bent by one tile to avoid a pure grid.
- Street width: 2 tiles (4 m) in towns, 1 tile (2 m) for dirt paths between towns. Vehicles receive an on-road speed bonus (chapter 04).
- Lots are 4 × 4 or 4 × 6 tile rectangles along street frontages. Corner lots are reserved for shops/landmarks (non-destinations, purely visual).
- Every street has a name drawn from a curated list (streets.json, ~120 names, e.g. "Larch Lane", "Saltmarsh Row"). Names are unique per map. Streets are colour-coded on the map and on street signs (see chapter 09).

### 3.5 Building placement and destinations

| Destination type | Placement | Arcade v1 |
|---|---|---|
| House | one per residential lot; mailbox at the street edge of the lot, facing the street | Yes |
| Apartment Complex | 4 × 6 lot; one Mail Room entrance with units slots (6–12) | Yes, at most 1 per medium town, appears in shift 4–5 districts (deferred by ch. 13) |
| PO Box Bank | inside PO district; 8–16 boxes; accepts letters only | Yes (deferred by ch. 13) |
| Business Dock | on a coastal lot with deep water; accepts Cargo only | Yes on Large Island; Small Island gets one from shift 4 (deferred by ch. 13) |

Every house is a distinct model variant (roof colour, wall colour, shape from a 12-variant kit) with the mailbox model matching the house colour, so players can visually identify houses at a distance.

### 3.6 Address assignment

- Each street numbers its lots from the town-centre end outward, odd on the left, even on the right, step 2 (so "14 Larch Lane" is roughly across from "13 Larch Lane").
- Apartment units are Unit 1..N at the complex address.
- Address string format: `{number} {street}` for delivery labels; district shown separately on the map. Full canonical id: `{districtId}:{streetId}:{number}[:{unit}]`.
- Every address is unique per map; validation stage asserts this.

### 3.7 District partition

- Districts are grown from the PO outward using street-graph BFS. District 1 contains the PO and the 8–12 nearest houses. Subsequent districts each add 8–12 (Small Island) or 10–14 (Large Island) houses, following whole streets where possible so a district boundary is a street, not a mid-street cut.
- On Large Island the town(s) other than the PO town are assigned to districts 3–5 in distance order, forcing a water or long-road crossing by shift 3.
- District id is stored on each street segment and lot. District boundaries are rendered as coloured curbs.

### 3.8 Road and ferry connectivity

- All towns are connected by dirt paths (A* over tiles with slope cost). If a path is impossible (water), a ferry lane is marked between the two nearest beaches; the Sea kit / pier + rowboat are always available so this is never a soft lock.
- Bridges: if a water gap is ≤ 3 tiles, a wooden bridge is generated.
- The routing graph (nodes at intersections, edges with length and surface type) is exported for NPC drivers and the map UI.

### 3.9 Resource nodes

| Resource | Node model | Yield per hit | Node HP (hits) | Respawn | Tools |
|---|---|---|---|---|---|
| Wood | Tree | 2 Log | 5 | Never in Arcade (stump remains) | Axe (hand: 50% yield) |
| Fiber | Bush | 3 Fiber | 3 | Regrows in 1 shift | Hand |
| Stone | Boulder | 3 Stone | 6 | Never | Pickaxe |
| Iron ore | Ore vein (rocky) | 2 Iron Ore | 8 | Never | Pickaxe |
| Sand | Sand pile (beach) | 4 Sand | 4 | Regrows in 1 shift | Shovel |
| Oil | Seep (marsh) | Requires Pump | — | — | Pump building |
| Berries | Bush | 2 Berries (food, heals) | 2 | Regrows in 1 shift | Hand |

Node density is set so a Small Island contains at least 3× the raw material needed to fund the "reference factory" (chapter 11) via harvest and shop conversion, guaranteeing the run is never resource-locked. Nodes are placed only outside lots and streets, biased away from the PO so the early game involves walking out and back.

### 3.10 Enemy spawn edges

- Spawn edges are sets of land tiles on the map perimeter (or water tiles for Balloons) at least 120 m from the PO and 40 m from any house. Each district unlock adds spawn edges facing the newly unlocked area.
- Each edge has a precomputed path to the PO over the routing graph plus a straight-line fallback (enemies can leave roads).

### 3.11 Post Office and player spawn

- PO footprint 6 × 6 tiles: Intake container, Shop counter, Depot (team storage 16 × 20), spawn pad. PO starts with 3000 HP (chapter 05).
- Players spawn on the pad. Initial resources for the starting kit are placed in the Depot.

### 3.12 Validation

The stage asserts and otherwise rerolls (bounded) or fails the seed:

- All destinations reachable from the PO by walking, or by walking plus a marked ferry lane.
- Address uniqueness.
- Buildable area around the PO ≥ 20 × 20 tiles clear of lots (room for a factory).
- At least one deep-water tile within 200 m of the PO (for the Sea path).
- Resource minimums met.
- worldHash computed.

## 4. Runtime world representation

- Tile grid (server and client): TileType, height, slope, districtId, lotId, buildable flags. Memory: 300 × 300 × 8 bytes ≈ 720 KB.
- Lots and destinations: array indexed by lotId with model variant, address id, and mailbox transform.
- Routing graph: adjacency list; used by NPC drivers, enemies, and the map.
- Deltas (replicated, chapter 06): construct placements, node depletion, terrain flattening under buildings (buildings auto-level within 1 m), ruin markers.

Clients regenerate everything above from the seed at load time (target < 3 s on a mid-range machine), then apply deltas received on join. (v1: clients receive the generated tables instead — chapter 13 §3.)

## 5. Visual dressing (client-only)

Trees, rocks, fences, street lamps, benches, and props are placed by a client-only pass from a separate stream hash(seed, "dressing"). Dressing is non-blocking and non-colliding except trees and boulders which are gameplay nodes (server-placed). This keeps the server world lean and lets clients scale prop density with graphics settings without desync.
