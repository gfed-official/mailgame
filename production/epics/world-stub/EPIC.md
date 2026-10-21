# Epic: World Stub — Hand-Authored Street (M-1a)

> **Layer**: Core
> **GDD**: **None — World Generation (#6) is "Not Started"** (systems-index). The contract here comes from architecture.md v1.1 (§Module Ownership / Core, row **Sim.World (stub M-1a)**; §Per-system layer assignment row 6: "stub M-1a (hand-authored street) → M1") and from the consumer GDDs' World Generation dependency rows: `player-character-movement.md` (P4, P6, F7, Dependencies), `mail-destinations.md` (R3, Dependencies), `throwing-catching.md` (T5, T6, T11, Dependencies).
> **Architecture Module**: Sim.World (`src/PerformativeMail.Sim/World/`) — read-only `IWorldQuery`
> **Milestone slice**: M-1a stub only; the generator is M1 (ADR-0026)
> **Engine Risk**: LOW (plain C#; no Godot. Terrain views are in `godot-host-shell`.)
> **Est. size**: S — 4–6 stories
> **Implementation priority**: between 3 and 4. Mail needs the address table; Player Character and Throwing need the height field, water and AABBs.
> **Status**: Complete (Provisional — no GDD)
> **Stories**: 7 stories — 7 Complete (counts refreshed 2026-10-20 from the story headers) — see the Stories table below

## Overview

Mail & Destinations, Player Character and Throwing & Catching all declare a **hard** dependency on World Generation, which has no GDD. The architecture resolves this for M-1a with a stub: a **hand-authored street of 10 houses** (systems-index; `mail-destinations.md` Dependencies) served through the same read-only `IWorldQuery` interface the M1 generator will implement. Freezing the interface now and swapping the implementation at M1 follows architecture principle 6 ("build the slice, freeze the interface").

**In scope** (each item cites the consumer that requires it; nothing is invented beyond these rows):
- **Tile grid and height field:** 2 m tiles with a bilinear int64 height sample `terrainH`, used by PC P4/F7 and Throwing's height-field sweep. Sea level is 0. Water depth is `max(0, −terrainHeight)` (PC P6/F7; Throwing water receiver), with at least one shallow tile and one deep tile, so the GDD fixtures FX-Beach and FX-Deep can be reproduced.
- **Walkability and solids:** walkability, house and construct AABBs (PC P4 colliders; Throwing T6/T11 solids, roofs and run boundaries) and run bounds.
- **Address table:** one residential lot per house, with `(addressId, districtId, streetId, number, display string, mailboxTile, faceNormal, anchor, approachTile, truckStopPoint)` per Mail R3. There is one district and one street at M-1a. Mailbox spacing must respect R3's guarantees.
- **Spawn pad and Post Office:** the Post Office spawn pad (PC FX-Pad: 8 ring slots at 1200 mm) and the Post Office location hosting `po_intake`.
- **Determinism:** the stub is authored as a data table built in code (or via `BuiltInContent`), deterministic, and contributes to the canonical dump (ADR-0006).

**Out of scope:**
- seeded generation, district partition, the routing graph and SeedViewer → M1, ADR-0026;
- server-generates-once plus JoinState tables → M0/M1.

**Risks.**
- (1) **No GDD and no ADR (ADR-0026 not written).** Stories will carry `ADR: N/A`. The interface shape (`IWorldQuery` members, height-sample signature, AABB query) is decided at story level from the three consumer GDDs. **Recommended:** a short `/architecture-decision` for the `IWorldQuery` stub contract (or an ADR-0026 stub section) before the second consumer story, so PC and Throwing do not diverge on the height-field call.
- (2) The consumers' fixtures (FX-Dry, FX-Beach, FX-Ramp, FX-Deep, FX-Pad) must be constructible from the same stub type.
- (3) The level-designer is not consulted for street geometry at M-1a. PC OQ23 (capsule vs doorway) is measured on this street at the greybox.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | `IWorldQuery` injected by constructor; read-only sync calls inside the caller's phase | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | int64 bilinear height, mm units, no floats; stub state in the canonical dump | LOW |
| ADR-0004: Identity and Id Spaces | lot, address and construct ids in the EntityId class table | LOW |
| *ADR-0026 (planned, not written)*: World generation pipeline, tile grid & routing graph | "stub interface `IWorldQuery` at M-1a" | — **missing** |

## GDD Requirements

There are no `TR-world-*` IDs (no GDD; Appendix A has none). These consumer TRs depend on this stub:

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-movement-005 | Fixed-point trig LUT; terrain from World Gen height field (2 m, bilinear, int64) | ❌ No ADR (planned ADR-0013, not written) | M-1a |
| TR-movement-018 | Water depth from height field; swim hysteresis 900/700 mm | ❌ No ADR (planned ADR-0013, not written) | M-1a (height-field half) |
| TR-throwing-005 | Swept segment test per tick per flight (≤90 ticks); slab/circle/cylinder/sphere/heightfield | ❌ No ADR (planned ADR-0015, not written) | M-1a (height-field/AABB half) |
| TR-throwing-007 | Broad phase bounds \|R\|≤65536 mm; static 4 m grid | ❌ No ADR (planned ADR-0015, not written) | M-1a (static solids source) |
| TR-mail-006 | Server-only SoA mail table keyed by mailId; packed AddressId | ❌ No ADR (planned ADR-0017, not written) | M-1a (address source) |
| TR-sim-033 | T1: identical canonical dump hash per tick given same inputs and logs | ADR-0006 ✅ | M-1a |

**Untraced:** the whole epic. It has no GDD, and ADR-0026/0013/0015/0017 are not written.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- Every World Generation row in the Dependencies sections of `player-character-movement.md`, `mail-destinations.md` and `throwing-catching.md` is satisfied by `IWorldQuery` for the M-1a street
- The consumer fixtures FX-Dry, FX-Beach, FX-Ramp, FX-Deep and FX-Pad are buildable from the stub type
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/world/**`)
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (none expected; street views belong to `godot-host-shell`)

## Stories

*ADR-0026 is now Accepted (2026-10-16), and every story cites it (risk 1 is resolved). The order follows ADR-0026's Migration Plan; Lots and the Post Office are split for size.*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [`IWorldQuery` contract, geometry value types, height-field kernel](story-001-iworldquery-height-field-kernel.md) | Logic | Complete | ADR-0026, ADR-0013 §6 |
| 002 | [Tile flags, bounds, `ClampInsideMap`, `TrySnapWalkable`](story-002-tile-flags-bounds-clamp-snap.md) | Logic | Complete | ADR-0026 |
| 003 | [Static solids + 4 m `QueryStaticSolids`](story-003-static-solids-4m-query.md) | Logic | Complete | ADR-0026 |
| 004 | [`AddressId`, lots, destination ids, spacing validation, `StreetNameAt`](story-004-addressid-lots-destination-ids.md) | Logic | Complete | ADR-0026, ADR-0017 §1 |
| 005 | [Post Office record + spawn pad](story-005-post-office-spawn-pad.md) | Logic | Complete | ADR-0026, ADR-0013 §3 |
| 006 | [`M1aStreet` + `WorldTablesFixture` (FX-Dry/Beach/Ramp/Deep/Pad)](story-006-m1a-street-and-fixtures.md) | Logic | Complete | ADR-0026 |
| 007 | [`World` dump section, composition, replay world identity](story-007-world-dump-composition-replay-id.md) | Integration | Complete | ADR-0026, ADR-0006 |

**Untraced requirements (no TR-ID in `docs/architecture/tr-registry.yaml`; flagged for the next `/architecture-review`, not added here):** map clamp and walkable snap (PC P4 step 7, Throwing T6/F8; story 002); map bounds |coord| ≤ 1,000,000 mm (Throwing CV 17; story 002); Mail R3 mailbox-spacing guarantee and `StreetNameAt` (story 004); Post Office spawn pad (PC P17/F8; story 005, which has no TR at all).

**Cross-epic ownership decided here:** story 001 creates `Sim/Geometry` `WorldHeight`/`HeightCorners` (ADR-0013 §6) and `Vec3Mm`/`AabbMm`/`TileCoord` if they are absent. Story 004 creates `Sim/Primitives/AddressId` (ADR-0017 §1). player-character, throwing-catching, mail-destinations and grid-inventory reuse them.

## Next Step

`/story-readiness production/epics/world-stub/story-001-iworldquery-height-field-kernel.md`, then `/dev-story` in order 001 → 007.
