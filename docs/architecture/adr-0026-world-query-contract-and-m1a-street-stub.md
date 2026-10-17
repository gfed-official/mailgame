# ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub — generation pipeline deferred to M1

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
Autonomous planning run (architecture-decision skill, `--review lean`: TD-ADR skipped, engine specialist consulted). Acceptance pending the project owner's batch confirmation.

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Accepted for its M-1a stub scope (§1–§9); §10's generation pipeline remains the M1 revision. Amended before acceptance: Depends On narrowed (ADR-0017 cycle) and now names ADR-0013; `GetCorners` added; §2 kernel and walkability aligned with ADR-0013.

## Summary
Player Character, Throwing and Mail all hard-depend on World Generation, which has no GDD. They need one read-only world interface at M-1a so they do not diverge on the height-field call. This ADR freezes `IWorldQuery` and specifies the M-1a hand-authored 10-house street behind it. That covers the 2 m int64 bilinear height field (one rounding rule), water from sea level 0, tile flags, world-owned static AABBs with a 4 m region query, map bounds and clamping, walkable snap, the lot/address table in ascending `AddressId` order, and the Post Office spawn pad. The generation pipeline (seeded generation, districts, routing graph, server-generates-once with JoinState tables) is **explicitly deferred** to a revision of this ADR at M1, when the World Generation GDD exists. This follows ADR-0011's precedent of "seam now, transport later".

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (.NET) |
| **Domain** | Core (Godot-free `PerformativeMail.Sim/World`) |
| **Knowledge Risk** | LOW. No Godot API is involved. Terrain views are ADR-0019's and are built from these tables. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md` (no world-related engine use) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None engine-side. Golden vectors for `TerrainHeightMm` are run on the net8.0 leg (T2, ADR-0006). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (constructor injection, read-only sync calls), ADR-0004 (`Destination`/`Prop` ids at world build, `StringId`), ADR-0006 (int64 mm, `MulDivRH`, canonical dump, T2 vectors) — all Accepted. ADR-0013 (Accepted 2026-10-16): the `Sim/Geometry` kernel `WorldHeight.Sample`/`Gradient` and the carrier's §6 consumer requirements this interface satisfies. *Not a dependency:* ADR-0017. `AddressId` is referenced here as a type (a packed u32 whose numeric order is `(district, street, number, unit)` order, ADR-0017 §1). The dependency runs the other way, because ADR-0017's registry is built from `Lots`. *(Narrowed 2026-10-16 at acceptance to break the ADR-0017 ↔ ADR-0026 dependency cycle; ADR-0017 is accepted after this ADR.)* |
| **Enables** | the ADR-0013 carrier stories (terrain, water, colliders, bounds), ADR-0015 (sweep against the height field, water plane, static solids), ADR-0017 (registry from `Lots`), ADR-0019 (terrain and mailbox views), ADR-0028 (routing graph, at M1 revision) |
| **Blocks** | world-stub epic (all stories); the second consumer story among player-character / throwing-catching / mail-destinations that touches the world |
| **Ordering Note** | **Why ADR-0026 and not a section of ADR-0013 or ADR-0016:** architecture.md reserves ADR-0026 for World Generation and already states "M1 (stub interface `IWorldQuery` at M-1a)". The epics index names "ADR-0026 (stub section) `IWorldQuery`". The interface has three consumers (PC, Throwing, Mail), so filing it under one consumer's ADR (0013) would make the others depend on a solver decision. ADR-0016 is client-side timing, unrelated. The M1 revision adds a "Generation pipeline" decision under this same number and keeps this interface (additive members only). ADR-0015 **must** use `QueryStaticSolids` for world solids rather than build a second world grid. |

## Context

### Problem Statement
Three M-1a epics need terrain height, water, solids, bounds and the address table. With no World Generation GDD, each consumer's stories would invent its own signature. That is a real risk already visible in the GDDs: PC F7 truncates the bilinear height, Throwing F4 rounds half; PC derives water from negative height, Throwing reads "water tiles". The world-stub epic asks for the contract to be fixed before the second consumer story.

### Constraints
- Integer mm, int64 for height maths, no floats (ADR-0006). Iteration in ascending ids.
- Read-only during ticks. Built in `Constructed` before `Freeze()`. Destination ids are allocated in lot order then (ADR-0004).
- Immutable after build, so it can be passed by reference to `SimWorld` systems and to `ClientLayer`, like `ContentSet` (ADR-0010), without violating "ClientLayer never reads SimWorld": the tables are not `SimWorld` state.
- No invented world design beyond what the consumer GDDs require (world-stub EPIC scope).

### Requirements
- PC P4 (colliders, walkability 700‰, map clamp 1 m inside), P6/F7 (bilinear 2 m int64 height, water depth `max(0, −h)`), P17/F8 (spawn pad 8 slots at 1200 mm, PO door fallback).
- Throwing T5/T6/T11/F4/F8 (height-field sweep sampled at `min(cellMm/2, 250)`, water receiver, house/construct AABBs, map sides as vertical solids, `clampInsideMap(D, 1000)`, `snapWalkable(…, 1000)`, 4 m static broad phase).
- Mail R3 (per lot `addressId, districtId, streetId, number, display string, mailboxTile, faceNormal, anchor, approachTile, truckStopPoint`; anchor = face + 300 mm along the normal at 1000 mm height; catch volumes axis-aligned to 90° yaw), R10 (addresses ascending).
- Consumer TRs: TR-movement-005, TR-movement-018 (height-field half), TR-throwing-005/007 (world half), TR-mail-006 (address source), TR-sim-033.

## Decision

### 1. One immutable `WorldTables` behind `IWorldQuery`

- `PerformativeMail.Sim/World/` holds `IWorldQuery` and its single implementation `WorldTables`, which is immutable after `WorldTablesBuilder.Build()`.
- At M-1a the builder is fed by `M1aStreet.Author(WorldTablesBuilder b)`, a code-authored data table in the style of `BuiltInContent`. At M1 the generator feeds the same builder.
- Consumers receive `IWorldQuery` by constructor (ADR-0003). It is passed by reference to systems and to the ClientLayer of the same process.
- At M0+ a remote client builds its own `WorldTables` from the same deterministic source. How the tables reach clients (regenerate vs JoinState tables) is the M1 revision's decision.

### 2. Height field (one rounding rule for every consumer)

- Corner heights: `int[] cornerMm` of `(TilesX + 1) × (TilesZ + 1)`, row-major, in mm. Tile size `S = 2000` mm (`TileSizeMm`). Corner heights are bounded to ±1,000,000 mm (`WorldConstants.MaxCornerHeightMm`); `SetCorners` rejects a value outside it (`SimBuildErrorCode.WorldCornerHeightOutOfRange`).
- `TerrainHeightMm(x, z)` is **bilinear over the containing tile's four corners**, computed in int64 with a **single `Fix.MulDivRH`** (round half away from zero, ADR-0006):
  `h = MulDivRH(h00·(S−u)(S−v) + h10·u(S−v) + h01·(S−u)v + h11·u·v, 1, S²)`
  where `u = x − tx·S` and `v = z − tz·S` are both in `[0, S)`. Inputs are clamped to the map. At the far map edge (x = TilesX·S or z = TilesZ·S) sampling uses the last tile with offset exactly S, returning the far-corner heights; the heightfield march follows the same rule.
- The kernel is ADR-0013 §6's `WorldHeight.Sample` (`Sim/Geometry`). `WorldTables` calls it over `GetCorners`, so there is one implementation and one T2 vector set.
- **This replaces PC F7's truncating division** (difference ≤ 1 mm; GDD impact) and matches Throwing F4's RH. Golden vectors are committed for both signs, for corners, edges and the clamp.
- `TerrainGradientPm(x, z, out gx, out gz)` is the analytic tile-local gradient in permille, using the same corners and the same rounding. It is a rounded convenience for presentation and AI. PC P4's walkability `gx² + gz² ≤ 700²` is decided on exact un-divided numerators (`WorldHeight.Gradient`, ADR-0013 §6, compared in `Int128`), both by the carrier and by the `Walkable` precompute (§4). It is never decided on the rounded permille value. *(Amended 2026-10-16 before acceptance to match ADR-0013.)*

### 3. Water: sea level 0, derived from height

- `WaterDepthMm(x, z) = max(0, −TerrainHeightMm(x, z))` (PC P6/F7).
- The water surface is the plane y = 0 wherever terrain is below it. Throwing's water receiver is that plane, restricted to points with `TerrainHeightMm < 0`.
- `TileFlags.Water` (any corner < 0) is **only a broad-phase hint**, never an authority. This reconciles PC (height-derived) with Throwing ("water tiles").

### 4. Tiles, bounds, walkability, snap

- **`TileFlags`:**
  - `Blocked`: authored or generated cliff/void.
  - `Water`: hint, per §3.
  - `Walkable`: precomputed at build. True iff not `Blocked`, the tile's maximum corner-gradient magnitude ≤ 700‰, and `WaterDepthMm` at the tile centre < 900 mm (swim enter, PC P6).
  - `Road`, `Lot`: display and spawn hints.
- **`Bounds`:** an axis-aligned map rectangle in mm, `|coord| ≤ 1,000,000` (Throwing CV 17). Map sides are vertical solids for Throwing (rank 5), which reads them from `Bounds`.
- **`ClampInsideMap(p, insetMm)`:** clamps x/z to `[Min + inset, Max − inset]` (PC P4 step 7 uses 1000; Throwing F8 uses 1000).
- **`TrySnapWalkable(p, radiusMm, out snapped)`:** finds the nearest `Walkable` tile centre within `radiusMm` by squared distance in mm. Ties break by lower `tz`, then lower `tx`. It returns `false` if none is found. Height is `TerrainHeightMm` at the snapped point.

### 5. World-owned static solids

- `StaticSolids`: a `ReadOnlySpan<StaticSolid>` in ascending index, each `{ ushort Index, EntityId Owner (Prop/Destination/None), AabbMm Box, SolidKind Kind (House, PostOffice, Prop, Roof) }`. At M-1a these are the 10 houses, their roofs and the Post Office.
- `QueryStaticSolids(in AabbMm region, Span<ushort> indices) → int count` uses a **static 4 m bucket grid built once at world build**. Results come back in ascending `Index`, deduplicated, with no allocation (the caller supplies the span; overflow is counted and truncated).
- **Player-built constructs and vehicles are not world solids.** Their owners register them in their own registries (PC P4 "registered by their owners"; Building and Vehicles ADRs). Consumers query both.

### 6. Lots, addresses, display names (Mail R3)

- `Lots`: a `ReadOnlySpan<LotRecord>` in **lot order = ascending `AddressId`**. Lot index = Mail's `DestinationOrdinal` (ADR-0017 §3). Each record carries:
  - `Destination` (`EntityId`, class `0x07`, allocated by `WorldTablesBuilder` in lot order during `Constructed`, per ADR-0004);
  - `AddressId` (ADR-0017 packing), `DistrictId`, `StreetId`, `Number`, `Display` (`StringId`);
  - `MailboxTile`;
  - `Face` (one of four axis directions, so catch volumes stay axis-aligned to 90° yaw, Throwing CV 6);
  - `Anchor` (`Vec3Mm`: face + 300 mm along the normal, y = ground + 1000 mm);
  - `ApproachTile`, `TruckStopPoint`;
  - the lot's house `StaticSolid` index.
- The builder rejects duplicate addresses, out-of-range address components (ADR-0017 §1) and mailbox spacing below Mail R3's guarantees (`SimBuildException`).
- `StreetNameAt(x, z) → StringId` returns the street a point lies on, or `StringId.None` off-street ("by the coast", PC L645, worded by Presentation).

### 7. Post Office and spawn pad

`PostOffice` is a single record `{ Vec3Mm DoorAnchor, Vec3Mm IntakeAnchor (po_intake), Vec3Mm PadCentre, int PadRingRadiusMm = 1200, byte PadSlots = 8 }`. Pad slot k sits at angle `k × 45°`, using the BAM trig LUT (ADR-0013's). The builder asserts every slot is `Walkable`, outside every `StaticSolid` and above swim depth (PC L489). If not, it fails, and the runtime fallback is `DoorAnchor` (PC F8).

An unset Post Office fails only the composition path `Build(EntityIds, int, int)` (code 37, `WorldPostOfficeNotSet`); `Build(int, int)` keeps it optional (validated when set); `Build()` rejects one (code 43, `WorldPostOfficeNeedsWalkabilityLimits`).

### 8. The M-1a street (`M1aStreet`)

- One district (1) and one street (1).
- 10 houses, numbers 1–10, alternating sides, mailbox spacing within R3 (OQ15: 6 m / 3 m).
- One Post Office with its pad.
- A beach strip at one end with at least one shallow tile (depth 0 < d < 900 mm) and one deep tile (≥ 900 mm), so FX-Beach and FX-Deep are reproducible. One authored ramp reproduces FX-Ramp.
- Map ≤ 128 × 64 tiles (256 m × 128 m).
- Exact geometry is the world-stub stories' (greybox; PC OQ23 doorway check is measured here). It is authored in code and deterministic.
- **Fixtures:** `tests/` gets a `WorldTablesFixture` helper over the same `WorldTablesBuilder`, building FX-Dry, FX-Beach, FX-Ramp, FX-Deep and FX-Pad without the street (world-stub EPIC risk 2).

### 9. Determinism and dump

- `WorldTables` is an `ISimStateContributor` (section `World`). It writes corner heights, flags, solids, lots, PO and bounds, little-endian, field by field, in index order (ADR-0006).
- It is written once per dump. It is immutable, so it is cheap to cache as bytes after build.
- Replays record the world source identity: M-1a = `M1aStreet` version constant; M1 = generator inputs.

### 10. Deferred to the M1 revision of this ADR (out of scope now)
- Seeded generation pipeline and its RNG streams; district partition and unlock topology; ≥ 3 streets per district; Lost Parcel candidate points (Mail OQ14).
- Routing graph (Enemy AI / NPC trucks, ADR-0028) and SeedViewer.
- Server-generates-once plus world tables in JoinState (spec 13 §3) vs client regeneration; table chunking (ADR-0022).
- Height-field authoring/generation resolution, cliffs, the `Blocked` source.

The M1 revision may **add** members to `IWorldQuery`. It **must not** change §2–§7 semantics without superseding this ADR.

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.World;
public interface IWorldQuery
{
    int TileSizeMm { get; }                                   // 2000
    int TilesX { get; } int TilesZ { get; }
    MapBounds Bounds { get; }                                 // mm, |coord| ≤ 1,000,000
    long TerrainHeightMm(long xMm, long zMm);                 // §2 bilinear, int64, one MulDivRH (= WorldHeight.Sample)
    void GetCorners(int tx, int tz, out HeightCorners h);     // h00,h10,h01,h11 int32 mm; indices clamped (ADR-0013 §6 kernel input)
    void TerrainGradientPm(long xMm, long zMm, out int gxPm, out int gzPm);
    int  WaterDepthMm(long xMm, long zMm);                    // max(0, −h), sea level 0
    TileFlags Tile(int tx, int tz);                           // Blocked | Water(hint) | Walkable | Road | Lot
    Vec3Mm ClampInsideMap(in Vec3Mm p, int insetMm);
    bool TrySnapWalkable(in Vec3Mm p, int radiusMm, out Vec3Mm snapped);
    ReadOnlySpan<StaticSolid> StaticSolids { get; }           // ascending Index
    int QueryStaticSolids(in AabbMm region, Span<ushort> indices); // 4 m grid, ascending, 0-alloc
    ReadOnlySpan<LotRecord> Lots { get; }                     // ascending AddressId == DestinationOrdinal
    PostOfficeRecord PostOffice { get; }
    StringId StreetNameAt(long xMm, long zMm);                // None off-street
}
[Flags] public enum TileFlags : byte { None = 0, Blocked = 1, Water = 2, Walkable = 4, Road = 8, Lot = 16 }
public readonly struct MapBounds { public readonly long MinXMm, MinZMm, MaxXMm, MaxZMm; }
public readonly struct StaticSolid { public readonly ushort Index; public readonly EntityId Owner; public readonly AabbMm Box; public readonly SolidKind Kind; }
public readonly struct LotRecord { public readonly EntityId Destination; public readonly AddressId Address; public readonly byte DistrictId, StreetId; public readonly ushort Number;
    public readonly StringId Display; public readonly TileCoord MailboxTile; public readonly FaceDir Face; public readonly Vec3Mm Anchor;
    public readonly TileCoord ApproachTile; public readonly Vec3Mm TruckStopPoint; public readonly ushort HouseSolid; }
public readonly struct PostOfficeRecord { public readonly Vec3Mm DoorAnchor, IntakeAnchor, PadCentre; public readonly int PadRingRadiusMm; public readonly byte PadSlots; }
public sealed class WorldTablesBuilder { /* SetCorners (±MaxCornerHeightMm), AddSolid, AddLot, SetPostOffice; validates §2, §4–§7 */
    public WorldTables Build();                                            // terrain-only; rejects a Post Office (code 43)
    public WorldTables Build(int slopeMaxPm, int swimEnterMm);             // Post Office optional, validated when set
    public WorldTables Build(EntityIds ids, int slopeMaxPm, int swimEnterMm); } // composition path; Post Office required (code 37)
```

### Implementation Guidelines
- Every consumer **must** read terrain, water, world solids, bounds and lots only through `IWorldQuery`. Consumers **must never** reimplement bilinear sampling or water depth.
- `TerrainHeightMm` **must** use exactly one `MulDivRH` over the int64 bilinear sum. No float and no truncating division.
- `Tile(...).Water` **must not** be used as the water authority. Use `WaterDepthMm`/`TerrainHeightMm < 0`.
- `WorldTables` **must** be immutable after `Build()`. Destination ids **must** be allocated by the builder in lot order in `Constructed`.
- `QueryStaticSolids` **must** return ascending indices and allocate nothing. ADR-0015 **must** use it for world solids.
- Lots **must** be in ascending `AddressId`.

## Alternatives Considered

### Alternative 1: Put the interface in ADR-0013 (carrier solver)
- **Pros**: The solver is the heaviest consumer.
- **Cons**: Mail and Throwing would depend on a movement ADR. Its acceptance would gate Mail's registry.
- **Rejection Reason**: Three consumers need a neutral owner. architecture.md already reserves ADR-0026.

### Alternative 2: Story-level interface, no ADR (world-stub EPIC fallback)
- **Pros**: No ADR overhead.
- **Cons**: The PC/Throwing rounding and water-source conflicts would be decided twice, by whichever story lands first.
- **Rejection Reason**: That is exactly the divergence the epic warns about.

### Alternative 3: Separate `IHeightField`, `ISolids`, `IAddressBook` interfaces
- **Pros**: Narrower dependencies per consumer.
- **Cons**: Three injections for PC and Throwing. The M1 swap touches three seams.
- **Rejection Reason**: One immutable read-only object is simpler. Consumers use only what they need.

### Alternative 4: Truncating height division (PC F7 as written)
- **Pros**: PC fixtures unchanged.
- **Cons**: Biased toward zero, asymmetric for negative (water) heights. Throwing F4 would need to change instead.
- **Rejection Reason**: RH is ADR-0006's default rounding and is symmetric around sea level. The fixture delta is ≤ 1 mm.

## Consequences

### Positive
- PC, Throwing and Mail can start against one frozen, testable contract. Fixtures share a builder.
- Two GDD conflicts (rounding, water source) are settled once.
- The M1 generator is a swap of the builder's feeder, not of consumers.

### Negative
- ADR-0026 is split across two milestones. Until the M1 revision, its title promises more than the body decides.
- PC F7 golden numbers change by ≤ 1 mm.

## Risks
- **The M1 generator needs a member semantics change** (for example, a non-grid height field). *Mitigation:* §10's "add, don't change" rule; a superseding ADR if unavoidable.
- **A 128 × 64 street plus the beach does not exercise the 4 m grid at scale.** *Mitigation:* a test fixture with ~2,000 solids for the query cost.
- **Walkable precompute at tile granularity disagrees with PC's per-contact slope test at tile edges.** *Mitigation:* `Walkable` is used only for snap and spawn validation. The solver uses the gradient directly (documented).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| player-character-movement.md | F7 / TR-movement-005: terrain from height field (2 m, bilinear, int64) | §2 |
| player-character-movement.md | P6 / TR-movement-018 (height half): water depth from height field | §3 |
| player-character-movement.md | P4: colliders, walkability 700‰, map clamp 1 m | §4, §5 |
| player-character-movement.md | P17/F8: spawn pad 8 slots × 1200 mm, PO door fallback | §7 |
| throwing-catching.md | F4 / TR-throwing-005 (world half): heightfield sweep, water receiver | §2, §3 |
| throwing-catching.md | TR-throwing-007 (world half): static solids, 4 m broad phase, bounds | §4, §5 |
| throwing-catching.md | T6/F8: `snapWalkable`, `clampInsideMap` | §4 |
| mail-destinations.md | R3 / TR-mail-006 (address source): lot table fields; addresses ascending | §6 |
| simulation-core.md | TR-sim-033: identical canonical dump per tick | §9 |

## Performance Implications
- **CPU**: height sample is O(1) (4 loads + 1 `MulDivRH`). Solid query is O(buckets touched + hits). Snap is O(tiles in radius) (radius 1 m covers ≤ 4 tiles).
- **Memory**: M-1a street ≈ 129 × 65 × 4 B corners + flags + ~25 solids + 10 lots, under 64 KB. At M1 the 1,000,000 mm extent is bounded by the generator.
- **Load Time**: build plus 4 m grid at world build, milliseconds.
- **Network**: none at M-1a (deterministic code-authored stub). JoinState tables are an M1 decision.

## Migration Plan
No code exists. world-stub stories, in order:
1. `IWorldQuery` + `WorldTablesBuilder` + height/gradient golden vectors.
2. Tiles, bounds, snap.
3. Static solids + 4 m query.
4. Lots/PO + id allocation + validations.
5. `M1aStreet` + fixtures FX-Dry/Beach/Ramp/Deep/Pad.
6. Dump contributor.

## Validation Criteria
- Golden vectors for `TerrainHeightMm`/gradient (both signs, corners, edges, clamp) on net10 and net8 legs.
- FX-Beach/FX-Deep depths. FX-Pad: all 8 slots valid on the street.
- `QueryStaticSolids` returns ascending, deduplicated results with 0 B, cross-checked against a brute-force scan on random regions.
- Lots ascending by `AddressId`. Destination ids contiguous in lot order.
- Dump T1 determinism with the `World` section.

## GDD Impact (do not edit GDDs here; reconcile at re-review)
- **player-character-movement.md F7:** "plain integer division" → one round-half-away-from-zero (`MulDivRH`). Recompute F7 examples and fixtures (≤ 1 mm).
- **throwing-catching.md F4 / L559:** "water = sea level 0 over water tiles" → water where `TerrainHeightMm < 0` (tile flag is a hint).
- **simulation-core.md R19 / mail-destinations.md R3:** World allocates destination ids in lot order; lot order = ascending `AddressId`.
- **world-stub EPIC risk 1:** resolved by this ADR (stories cite ADR-0026, not `N/A`).

## Related
- ADR-0003, 0004, 0006 (Accepted); ADR-0013 (solver), ADR-0015 (sweep/broad phase), ADR-0017 (`AddressId`, registry), ADR-0019 (terrain views), ADR-0022 (JoinState, M1), ADR-0028 (routing graph).
- `production/epics/world-stub/EPIC.md`; `design/gdd/player-character-movement.md` P4/P6/P17/F7/F8; `throwing-catching.md` T5/T6/T11/F4/F8; `mail-destinations.md` R3/R10.

## Amendments
- 2026-10-17 — accepted by owner — §2: corner heights bounded to ±1,000,000 mm (`WorldConstants.MaxCornerHeightMm`), rejected by `SetCorners` (`WorldCornerHeightOutOfRange`); source TD-028 item 4.
- 2026-10-17 — accepted by owner — §2: far-edge sampling rule — at x = TilesX·S or z = TilesZ·S the last tile is used with offset exactly S (far-corner heights); the heightfield march follows it; source TD-028 item 4.
- 2026-10-17 — accepted by owner — §7: unset Post Office fails only `Build(EntityIds, int, int)` (code 37); `Build(int, int)` keeps it optional (validated when set); `Build()` rejects one (code 43); source TD-058 item 1.
- 2026-10-17 — accepted by owner — Key Interfaces: `WorldTablesBuilder` shows the three `Build` overloads (`Build(EntityIds)` was a single parameterless-of-limits sketch); source TD-028 item 4, TD-058 item 1.
