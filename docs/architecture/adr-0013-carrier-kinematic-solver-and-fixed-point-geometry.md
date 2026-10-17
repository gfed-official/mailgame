# ADR-0013: Carrier Kinematic Solver and Fixed-Point Geometry

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist` (Step 5.5). TD-ADR skipped (lean mode). Acceptance requires the project owner's explicit confirmation (batch request by the main session). Inputs: `design/gdd/player-character-movement.md` P1–P8, F1–F3, F6, F7, Edge Cases, Network (authority), ACs PCM-P2/P4/P7/P8/F6/F7/PERF-1; `design/gdd/throwing-catching.md` F4 (shared primitives); `design/gdd/simulation-core.md` R27–R29; `docs/architecture/architecture.md` §Sim.Players, §Required ADRs (ADR-0013), Appendix A; ADR-0003, ADR-0006 (Accepted).

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Amended before acceptance: §5/§6 `IWorldQuery` aligned to ADR-0026's member list (see §6 note). ADR-0006's Related records the `Fx16` narrowing (§2).

## Summary
Player movement is client-authoritative, yet it must be deterministic, headless-testable and bit-identical on Windows and Linux (PCM-P2-1, ADR-0006 T2), so it cannot use Godot or Jolt physics or any floating point. This ADR fixes one pure integer solver, `OwnedCarrier.Step`, in `PerformativeMail.Sim/Players/`: a vertical capsule against the World height field, blocked tiles, static AABBs and vehicle OBBs, swept in ≤ 300 mm sub-steps with ≤ 3 slides each, using exact tick-rational remainders, Q15/BAM trig with a committed atan table, and tick-time inputs only through `IRemoteCapsuleSource` / `ISupportTransformSource`. It also creates the shared `Sim/Geometry` primitives (bilinear height, segment intervals, heightfield march) that focus occlusion (ADR-0014) and the flight sweep (ADR-0015) reuse.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET, C# |
| **Domain** | Physics (Sim-side, plain C#; no engine surface) |
| **Knowledge Risk** | LOW for the decision: everything lives in `PerformativeMail.Sim` (zero Godot references, ADR-0001). Godot physics, Jolt (the 4.6+ default, `current-best-practices.md`) and physics interpolation take no part in movement. The Presentation's conversion of BAM to a node rotation is the only engine touchpoint (ADR-0019). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (4.6 Jolt default; 4.7 Jolt `WorldBoundaryShape3D` sign change — irrelevant, not used), `deprecated-apis.md` (4.5 removal of `RenderingServer.instance_set_interpolated` — engine interpolation not relied on), `current-best-practices.md` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | (1) PCM-P2-1: the 600-tick golden hash is equal on Windows and Linux CI on the net8.0 leg (ADR-0001 §7, ADR-0006 T2). (2) PCM-PERF-1 counters (sub-steps ≤ 4 per tick, slides ≤ 3 per sub-step) and 0 B per tick. (3) The `AtanQ15` table hash test (SC-DET-2). Presentation: the BAM → `Node3D.Rotation.Y` sign convention is checked by one view smoke test (ADR-0019). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Godot-free `PerformativeMail.Sim`, `Players/` and `Primitives/` folders, architecture tests); ADR-0003 (`PlayersStep` phase 3 host role, `ClientStepId.CarrierStep`, constructor injection, no statics); ADR-0004 (`EntityId` class Player, `PlayerSlot`); ADR-0006 (int mm, permille, BAM + Q15 table, `Fix.MulDiv`/`MulDivRH`/`FloorDiv`/`CeilDiv`/`ISqrt`, T2, 0-alloc); ADR-0010 (the `players` `SimSettings` sub-record and StatSheet keys `MoveSpeedAddPm`, `ExtraJumps`) |
| **Enables** | ADR-0014 (chest point, carrier state, `Geometry` occlusion primitives); ADR-0015 (`Geometry` segment intervals and heightfield march, `WorldHeight`); ADR-0016 (owner-role `CarrierStep` binding, render extrapolation clamp); ADR-0020 (transform fields, support-relative position, `Teleport` reset list); ADR-0026 (the `IWorldQuery` consumer contract in §6, which the World stub and generator implement) |
| **Blocks** | Player Character epic: solver, water, push and support stories (TR-movement-001, 004–006, 016–018); world-stub epic (height field and static collider queries); Throwing's heightfield receiver (through `Geometry`) |
| **Ordering Note** | Accept before ADR-0014 and ADR-0015, which consume `Sim/Geometry`. Belt carry (M-1b) and vehicle OBBs/roofs (M1) are specified here but their sources are empty stubs at M-1a. |

## Context

### Problem Statement
Spec 13 §3 made movement client-authoritative: the owning client steps its own carrier and nobody corrects it. That only works if the same code runs on the owning client, for host-authored Dead/Frozen bodies, in headless bot clients and in xUnit, and if recorded inputs replay bit-identically on both CI operating systems (PCM-P2-1). Godot's physics (Jolt by default since 4.6) is non-deterministic across machines, lives outside the Godot-free Sim, and its interpolation is unverified for 4.7. Player Character P4 leaves "the solver's number types and fixed-point trig" to an ADR, and ADR-0006 §6 leaves "interpolation (if any) and `atan2`" to this ADR. Throwing F4, focus occlusion (PC P11) and the carrier all need integer geometry against the same height field, and three copies would drift.

### Constraints
- Integer-only Sim (ADR-0006): int32 mm positions, int64 before the first multiply, one rounding per formula, BAM angles, the committed Q15 table, no `float`/`double`.
- No statics, constructor injection, 0 B per tick after warm-up (ADR-0003, ADR-0006 §8).
- Tick-time inputs only: the latest *received* remote snapshot, never render-time interpolation (P2).
- `ClientLayer` never reads `SimWorld` (ADR-0003 §6), so the owner-role step must take everything as explicit inputs.
- Budgets: owner `CarrierStep` + focus p99 ≤ 0.10 ms; host per-tick player work p99 ≤ 0.20 ms (PCM-PERF-1, Sim Core HB5).

### Requirements
- TR-movement-001 (deterministic kinematic capsule solver, no Godot physics, no floating point), TR-movement-004 (tick-time inputs via `IRemoteCapsuleSource`/`ISupportTransformSource`), TR-movement-005 (fixed-point trig, bilinear int64 height field at 2 m), TR-movement-006 (sub-steps ≤ 300 mm, ≤ 3 slides, step-up/snap 400 mm, slope 700 ‰, ≤ 4 sub-steps/tick), TR-movement-016 (own-capsule push, half overlap, cap 60 mm/tick), TR-movement-017 (support carry cap 12 000 mm/s, roof in vehicle-local frame), TR-movement-018 (water depth from height field, swim hysteresis 900/700).
- The GDD's exact goldens: 5000 mm/s steps 166, 167, 167 mm (F1); jump apex 1196 mm at tick 9, landing on tick 19 (F3); FX-Beach −600 mm, wade 867 ‰ (F7); F6 push vectors.

## Decision

### 1. One pure solver, two callers
- `OwnedCarrier.Step(ref CarrierState s, in CarrierInput input, in CarrierEnv env, ref CarrierScratch scratch)` is a pure, allocation-free static method in `Sim/Players/OwnedCarrier.cs`. It reads only its arguments and writes only `s` and `scratch`. It has no clock, RNG, statics (other than `ReadOnlySpan` literal tables) or I/O.
- **Owner role (every client, the host's own client included).** Players' `IClientStep` bound to `ClientStepId.CarrierStep` calls it once per client tick with the tick's `InputFrame` (ADR-0012) translated into `CarrierInput`, and the ClientLayer mirror's views as `CarrierEnv`. The result is the carrier's authoritative transform, reported through the transform stream (ADR-0020).
- **Host role (phase 3, `PlayersStep`).** The host calls the **same** function, with a zero `CarrierInput` and `CarrierInput.HostAuthored = true`, only for bodies it authors: Dead (held where it fell) and Frozen (ground drop, depenetration, P19). It never steps an Alive remote player's motion (PCM-P2-2). Host role skips player push (Dead and Frozen are non-solid, P8) and water entry checks for Dead bodies.
- Role differences are inputs, never branches on "am I the host". There is exactly one movement implementation.

### 2. State and integration (exact tick-rational remainders)
`CarrierState` (blittable, dumped by Players' `ISimStateContributor` in slot order):

| Field | Type | Meaning |
|---|---|---|
| `X, Y, Z` | `int` mm | feet position (world frame; when `OnSupport` is a vehicle, the replicated form is support-relative, §5) |
| `RemX, RemZ` | `int` | horizontal remainder in 1/30 mm (−29…29) |
| `RemY` | `int` | vertical remainder in 1/60 mm (trapezoid, F3) |
| `Vx, Vz` | `int` mm/s | own horizontal velocity (support carry excluded) |
| `Vy` | `int` mm/s | vertical velocity |
| `BodyYaw` | `ushort` BAM | facing |
| `Loco` | `byte` | Grounded / Airborne / Swimming / Mounted |
| `Flags` | `ushort` | Sprinting, OnSupport, SupportIsVehicle, ExtraJumpUsed, Wading, Ghosted mask holder … |
| `CoyoteLeft`, `JumpBufferLeft`, `HaulOutTicks` | `byte` | F3 / P6 counters |
| `SupportId` | `uint` | belt tile or vehicle `EntityId` |
| `SupportLocalX, Y, Z`, `SupportYawRef` | `int`, `ushort` | position in the vehicle's local frame (P7) |
| `Pair[7]` | `PushPairState` | per remote slot: overlap tick count, window start positions, ghosted (F6) |

- **Horizontal step.** `n = V·1 + Rem` (per axis, mm/s × 1 tick = 1/30 mm units), `d = Fix.FloorDiv(n, 30)`, `Rem = n − 30·d`. This reproduces F1's "remainder carried": 5000 mm/s gives 166, 167, 167 mm. **`Fx16` is not used for the carrier position.** `Fx16`'s `v·65536/30` truncation drifts by 1 mm at exact boundaries (tick 3 at 5000 mm/s reads 499, not 500), which breaks the GDD's goldens. The tick-rational remainder is exact, integer-only and T2-safe. This narrows ADR-0006 §1's "sub-mm integration: `Fx16`" row *for the carrier only*; see GDD/ADR Impact.
- **Vertical step (F3).** `vy' = max(vy − g/30, −terminalMmS)`; `n = vy + vy' + RemY`; `dy = FloorDiv(n, 60)`; `RemY = n − 60·dy`. Validator: `g` is a multiple of 30 (PC rule 10). `v0 = ISqrt(2·g·jumpHeightMm)`; extra jump `v0Extra = MulDiv(v0, extraJumpPm, 1000)` and **sets** vy.
- **Speed (F1).** `speedMmS = base × weightPm × buffPm × wadePm / 10^9`, `long` throughout, one `Fix.MulDiv` (truncation, as the GDD's "one division").
- **Acceleration (F2).** `dvMax = CeilDiv(max(vRef, |v|), airborne ? Tair : T)`; `Δ = vTarget − v`; if `|Δ|² ≤ dvMax²` then `v = vTarget`, else `v += (MulDiv(Δx, dvMax, |Δ|), MulDiv(Δz, dvMax, |Δ|))` with `|Δ| = CeilSqrt(Δx²+Δz²)` (`ISqrt` rounded up), so the truncated step never exceeds `dvMax`. Then a final clamp: with `limit = max(vRef, |v|)` (pre-step speed), if `|v'|² > limit²` then `v' = (MulDiv(v'x, limit, CeilSqrt(|v'|²)), MulDiv(v'z, limit, CeilSqrt(|v'|²)))`, so `|v'| ≤ max(vRef, |v|)` (`CarrierAccel.Apply`).
- **Remainders reset** on `Teleport`, mount, swim entry/exit, and support link changes (with velocity, airborne/swim state, jump buffer and any hold — PC Network).

### 3. Angles: BAM, Q15 and a committed atan table
- **Convention (Sim-wide, recorded here).** Axes: +X right, +Y up, −Z forward at yaw 0 (Godot's convention). For BAM yaw `θ`: `forward(θ) = (−sin θ, −cos θ)` in (x, z) and `right(θ) = (cos θ, −sin θ)`. Positive BAM turns counter-clockwise seen from +Y, so the Presentation sets `Rotation.Y = θ·2π/65536` with no sign flip.
- **Camera-relative input.** `InputFrame.MoveX/MoveY` are Q15 on the unit disc (x right, y forward). `wx = mx·c − my·s`, `wz = −mx·s − my·c` with `s = TrigQ15.Sin(lookYaw)`, `c = TrigQ15.Cos(lookYaw)` (`long`). `vTarget = (MulDiv(wx, vRef, 32767²), MulDiv(wz, vRef, 32767²))`.
- **No interpolation** of the Q15 sine table: 4096 entries (index `bam >> 4`, 0.088°) is finer than any visible facing error.
- **`TrigQ15.Atan2Bam(long y, long x) → ushort`.** Octant reduction (swap/negate so that `0 ≤ t = min/max ≤ 1`) plus a committed `AtanQ15` table of 1025 `ushort` BAM values for `t = i/1024`, indexed by `MulDivRH(min, 1024, max)`. `(0, 0)` returns 0 and is never used for turning. The table is generated once by `tools/GenTrigQ15` (which already generates the sine table, ADR-0006 §6), committed as a `ReadOnlySpan<ushort>` literal (`AtanQ15.g.cs`, getter marked `AggressiveOptimization`; the ADR-0006 §6 Tier-0 allocation caveat applies) and SHA-256 hash-tested in SC-DET-2.
- **Body yaw (F2).** `target = Atan2Bam(-wx, -wz)` (inverse of `forward`); `delta = (short)unchecked((ushort)(target − BodyYaw))`; `BodyYaw += clamp(delta, −yawStep, +yawStep)` with `yawStep = FloorDiv(65536 × turnDegPerSec, 360 × 30)` (4369 at 720°/s). While aiming or operating a turret `BodyYaw = lookYaw`.

### 4. Collision model and the sub-step sweep (P4)
**Shapes.** The carrier is a vertical capsule treated as a flat-capped cylinder: radius `capsuleRadiusMm` 300, height `capsuleHeightMm` 1400, feet at `(X, Y, Z)`.
- **Terrain** contact is a feet-point sample of `WorldHeight` (§6); the radius applies only against boxes and blocked tiles.
- **Slope** is the bilinear patch's analytic gradient at the feet point: walkable iff `1000²·(Gx² + Gz²) ≤ slopeMaxPm²·S⁴`, evaluated in `Int128` (`Gx`, `Gz` are the un-divided gradient numerators, S = 2000), so 700 ‰ (rise 1400/2000) walks and 702 ‰ slides (PCM-P4-1a). Steeper terrain is a wall: horizontal motion that would raise the feet onto it is resolved as a slide along the contour, and gravity applies.
- **Blocked tiles** are AABBs (tile footprint, infinite height). **Static boxes** are AABBs: world solids from `IWorldQuery.QueryStaticSolids` (houses, roofs, Post Office; ADR-0026 §5) and, from M-1b, player-built constructs, fences and nodes from their owners' registries (empty at M-1a). **Vehicles** are OBBs (yawed boxes) from `ISupportTransformSource`. Players are F6 (§5). Enemies, items and props are not colliders.
- **Cylinder vs AABB/OBB** (OBBs are first rotated into box-local coordinates with Q15): y-ranges overlap (`Y < top ∧ Y + 1400 > bottom`) and the xz circle–rectangle distance² `< r²`. The minimum translation is along the closest-point normal (or the axis of least penetration when the centre is inside).

**Per-tick order (P4).** (1) input and body yaw; (2) F2 velocity; (3) support carry (P7); (4) gravity (F3); (5) sweep; (6) player separation (F6); (7) clamp 1000 mm inside the map rectangle, zeroing the outward velocity component; (8) water state (F7), applied from the next tick.

**The sweep (step 5).**
- Displacement `D` (own + support carry + vertical) is split into `n = clamp(CeilDiv(max(|Dx|, |Dz|, |Dy|), 300), 1, 4)` sub-steps (`maxSubStepsPerTick` 4; `subStepMm` 300 = one radius). A displacement over 1200 mm is clamped to 1200 and counted `carrierStepClamped` (unreachable within the 12 000 mm/s support cap + terminal 20 000 mm/s vertical = 667 mm).
- **Why ≤ one radius is enough.** A cylinder that starts clear of a face and moves ≤ r per sub-step always overlaps any box it would cross, of any thickness, at some sub-step end, so discrete overlap tests cannot tunnel (PCM-P4-2a).
- Each sub-step: move horizontally; gather candidates once per tick from the broad phase (static boxes overlapping the tick's swept AABB, ascending collider id; vehicle OBBs; blocked tiles); resolve overlaps with **≤ 3 slide iterations** (push out by the MTV, remove the velocity component into the normal); then **step-up** (a box top ≤ `stepUpMm` 400 above the feet, with 1400 mm headroom, lifts the carrier onto it while grounded); then vertical move and ground resolution (the support height is the max of terrain and walkable box/roof tops under the footprint); then **snap-down** (grounded, drop ≤ 400 mm stays grounded; 401 goes airborne — PCM-P4-1c). A ceiling hit zeroes upward `vy`.
- **Depenetration precedence (P4).** A static box appearing over a capsule lifts it onto the top if ≤ 3000 mm up, else moves it to the nearest walkable point. A vehicle OBB overlapping it pushes it out along the nearest face normal by the whole overlap in one tick, never onto a moving roof and never into swim depth.
- **Counters (always on, `SimCounters`):** `carrierSubSteps` max per tick, `carrierSlideIterations` max per sub-step, `carrierStepClamped`, `carrierDepenetrations`. PCM-PERF-1 asserts the first two.

### 5. Supports, push and water
- **Belts (P7, M-1b).** A grounded carrier on a running belt adds the lane velocity from `ISupportTransformSource.TryGetBeltVelocity(tile)`. `|own + carry|` is capped at `supportSpeedCapMm` 12 000 by one `MulDiv` scale of the sum (PCM-P7-3: along 12 000, against 1500). A carrier pushing its own segment gets the base speed only.
- **Vehicle roofs (P7, M1).** On a roof the position is held in the vehicle's local frame (`SupportLocal*`), re-projected each tick from the vehicle pose of the latest received snapshot (`ISupportTransformSource`); the vehicle's yaw change rotates it (Q15). Roof carry is outside the cap. Jumping off keeps the support's horizontal velocity, decaying at air control.
- **Player push (F6, P8).** Each owner resolves **only its own capsule** against `IRemoteCapsuleSource` capsules with vertical overlap: `overlap = 2r − dxz`, `corr = overlap < pushDeadMm (10) ? 0 : min(MulDiv(overlap, pushSharePm 500, 1000), pushCapMm 60)` along the horizontal normal away from the other (`dxz = 0` → +x if the own `EntityId` is lower, else −x). The velocity component toward an overlapping remote is clamped to `pushCapMm` per tick. A push whose destination is at swim depth is rejected. **Ghosting:** a pair overlapping ≥ 10 mm for `ghostAfterTicks` 45 consecutive ticks while neither moved > 100 mm (measured from `PushPairState` window starts) skips F6 until its overlap reaches 0. Remote capsules that are Dead, Frozen or not solid are skipped.
- **Water (F7, P6).** `depth = max(0, −terrainH)` only over terrain (a carrier on a box or roof above water stays Grounded). `swimming' = swimming ? depth > swimExitMm (700) : depth ≥ swimEnterMm (900)`. `wadePm = swimming ? 1000 : 1000 − MulDiv(maxWadeSlowPm, min(depth, 900), 900)`. Haul-out after 15 ticks pushing at a walkable top ≤ 1200 mm above the surface. Entering deep water from the air zeroes `vy`.

### 6. World geometry consumer contract and the shared `Sim/Geometry`
**`IWorldQuery` — what the carrier needs (consumer contract). The member names are ADR-0026's Key Interfaces, which govern.** *(Amended 2026-10-16 before acceptance: the draft listing here — `GetTileFlags`, `QueryStaticBoxes(Span<StaticBox>)`, `StaticVersion`, `MapRect` — contradicted ADR-0026, which owns the interface. The carrier's requirements map onto it as follows.)*
- Tile size 2000 mm → `TileSizeMm`. Map rectangle and the 1000 mm inset clamp → `Bounds` (`MapBounds`), `ClampInsideMap`.
- A tile's four corner heights for `WorldHeight.Sample`/`Gradient` → `GetCorners(tx, tz, out HeightCorners)` (h00, h10, h01, h11 int32 mm; indices clamped; added to ADR-0026 as an additive member). Terrain height at a point → `TerrainHeightMm`, which **is** `WorldHeight.Sample` over those corners (one kernel, one T2 vector set).
- Tile flags (`Blocked`, `Water` hint) → `Tile(tx, tz)`.
- World solids (houses, roofs, Post Office) → `QueryStaticSolids(region, indices)` + `StaticSolids`: ascending `Index`, a 4 m grid owned by World, 0-alloc, overflow counted. Player-built constructs and nodes are not world solids; they come from their owners' registries (Building, M-1b+; empty at M-1a) through a tick-time port like §7's.
- No `StaticVersion` on `IWorldQuery`: `WorldTables` is immutable after build. Throwing's preview cache key uses the construct registries' version instead (ADR-0015; constant at M-1a).

`IWorldQuery` is read-only, synchronous and allocation-free. It is constructor-injected (ADR-0003). Throwing's broad phase (ADR-0015) uses the same World grid for world solids.

**`Sim/Geometry/` (pure static functions, T2 surface, unit-tested with the GDD vectors):**
- `WorldHeight.Sample(in HeightCorners h, int fx, int fz) → int`: `num = (h00·(S−fx) + h10·fx)·(S−fz) + (h01·(S−fx) + h11·fx)·fz` in `long`, divided by `S²` with **`Fix.MulDivRH` semantics (half away from zero)** — the one height function for the carrier, focus occlusion and Throwing's heightfield receiver (Throwing F4 says "RH"; PC F7 gives no rounding, so they agree). `WorldHeight.At(IWorldQuery w, int x, int z)` does the tile lookup with `FloorDiv`.
- `WorldHeight.Gradient(in HeightCorners h, int fx, int fz, out long gx, out long gz)` — un-divided numerators.
- `SegmentIntervals`: slab (AABB), circle (xz), sphere (3D), cylinder (circle ∩ y-slab), each returning `[tEnter, tExit]` in ‰ of the segment with exactly Throwing F4's `CeilDiv`/`FloorDiv` choices and its `A = 0` point test. Callers must apply their own broad-phase bound (|R| ≤ 65 536 mm) before the quadratic forms.
- `HeightfieldMarch(IWorldQuery w, P0, P1, out int fPm)` — Throwing F4's sampled march (`n = max(1, CeilDiv(|Dxz|, min(cell/2, 250)), CeilDiv(|Dy|, 250))`), shared with focus occlusion.
- `Obb.ToLocal(…)` with Q15 rotation.

ADR-0015 owns *how* the flight sweep applies these (receivers, ranks, terminals); this ADR owns the primitives so they exist once.

### 7. Tick-time input ports
```csharp
public interface IRemoteCapsuleSource { ReadOnlySpan<RemoteCapsule> Capsules { get; } }      // latest *received* snapshot
public readonly struct RemoteCapsule { public readonly EntityId Id; public readonly PlayerSlot Slot;
    public readonly int X, Y, Z; public readonly bool Solid; }                            // Solid = Alive ∧ ¬Frozen ∧ ¬Mounted
public interface ISupportTransformSource {
    ReadOnlySpan<VehiclePose> Vehicles { get; }                                            // id, pos mm, yaw BAM, half extents, roof y
    bool TryGetBeltVelocity(int tileX, int tileZ, out int vx, out int vz, out uint segmentId); }
```
- On a client they read the ClientLayer mirror (latest received transform batch); on the host they read the T−1 ingest latch (ADR-0003 §1 sample drain). Never render-time interpolation (P2).
- M-1a: `IRemoteCapsuleSource` is empty in solo; `ISupportTransformSource` returns no vehicles and no belts. Belt Logistics (M-1b) and Vehicles (M1) implement them.
- Their spans are copied into the recorder's sample log, so PCM-P2-1 replays bit-identically.

### Architecture Diagram
```
 ClientLayer.Step(tick)                                SimWorld.Tick(tick), phase 3 (host role)
  InputLatch ── InputFrame (ADR-0012)                    for slot in authored bodies (Dead/Frozen):
  CarrierStep ─► OwnedCarrier.Step(ref s, input, env)       OwnedCarrier.Step(ref s, zeroInput|HostAuthored, env)
                    │ env: IWorldQuery ─────────────┐
                    │      IRemoteCapsuleSource      │  Sim/Geometry (pure, T2):
                    │      ISupportTransformSource   ├─► WorldHeight.Sample/Gradient  (also ADR-0014 occlusion,
                    │      PlayerSettings, StatSheet │   SegmentIntervals             ADR-0015 flight sweep)
                    ▼                                │   HeightfieldMarch, Obb
                 CarrierState' ── transform report (ADR-0020) ──► host latch ──► position history
 Primitives: TrigQ15.Sin/Cos (ADR-0006), TrigQ15.Atan2Bam + AtanQ15 table (this ADR), Fix.*
```

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.Players;
public static class OwnedCarrier
{
    public static void Step(ref CarrierState s, in CarrierInput input, in CarrierEnv env, ref CarrierScratch scratch);
}
public readonly struct CarrierInput { /* MoveX, MoveY (Q15 disc), LookYaw (BAM), SprintHeld, JumpPressed (edge this tick),
    Aiming, ContextFlags (Panel → walk), HostAuthored, SpeedPermille (Grid D1, 1000 if mounted), BuffAddPm, ExtraJumps */ }
public readonly ref struct CarrierEnv { /* IWorldQuery World; IRemoteCapsuleSource Remotes; ISupportTransformSource Supports;
    in PlayerSettings Settings; EntityId Self */ }
public struct CarrierScratch { /* fixed-capacity candidate buffers (64 solid indices + construct boxes, VehiclePose refs), counters */ }
namespace PerformativeMail.Sim.Primitives;
public static partial class TrigQ15 { public static int Sin(ushort bam); public static int Cos(ushort bam);
    public static ushort Atan2Bam(long y, long x); }                  // AtanQ15: 1025-entry committed table
namespace PerformativeMail.Sim.Geometry;
public static class WorldHeight { public static int Sample(in HeightCorners h, int fx, int fz);
    public static int At(IWorldQuery w, int x, int z); public static void Gradient(in HeightCorners h, int fx, int fz, out long gx, out long gz); }
public static class SegmentIntervals { /* Slab, Circle, Sphere, Cylinder → (int enterPm, int exitPm, bool hit) per Throwing F4 */ }
public static class HeightfieldMarch { public static bool FirstContact(IWorldQuery w, in Vec3Mm p0, in Vec3Mm p1, out int fPm); }
```

### Implementation Guidelines
- Movement **must** be computed only by `OwnedCarrier.Step`. There **must never** be a Godot physics body, `CharacterBody3D`, Jolt query or engine interpolation in player movement.
- `OwnedCarrier.Step` **must** be pure: no clock, RNG, statics (except `ReadOnlySpan` literal tables), allocation or exceptions. All arithmetic **must** be integer, widened to `long` before the first multiply, with one rounding per formula.
- The host **must never** step an Alive remote carrier. Host role **must** call the same function with `HostAuthored` input.
- Other players and supports **must** be read only through `IRemoteCapsuleSource` / `ISupportTransformSource` at tick time; render-time interpolated poses **must never** enter the Sim.
- Sub-steps **must** be ≤ 300 mm and ≤ 4 per tick; slides **must** be ≤ 3 per sub-step; both are counted.
- Carrier sub-mm integration **must** use the tick-rational remainders of §2 (1/30 mm horizontal, 1/60 mm vertical); `Fx16` **must not** be used for carrier position.
- Angles **must** be BAM with the §3 axis convention; `atan2` **must** go through `TrigQ15.Atan2Bam`. Both tables **must** be committed and hash-tested.
- Height **must** come from `WorldHeight` only; slope tests **must** compare un-divided numerators in `Int128`.
- Every constant (radius, heights, speeds, slopes, push, water) **must** come from the `players` `SimSettings` record or the StatSheet, never a code literal (ContentValidator rules PC 1–12).
- The solver **must** be allocation-free: candidate buffers are fixed-capacity fields of `CarrierScratch`; overflow is counted, never grown.

## Alternatives Considered

### Alternative 1: Godot `CharacterBody3D` / Jolt for player movement
- **Description**: Use `MoveAndSlide` against engine colliders, with the Sim reading the result.
- **Pros**: Mature slide/step behaviour; less code; editor-visible colliders.
- **Cons**: Floating point and engine-version dependent (Jolt became the default in 4.6); not reproducible across machines; requires the engine for every test; puts movement outside the Godot-free Sim (ADR-0001); bot clients would need a Godot runtime.
- **Rejection Reason**: Violates TR-movement-001, PCM-P2-1 and ADR-0006 T2.

### Alternative 2: Continuous swept-capsule time-of-impact (TOI) solver
- **Description**: Compute exact first contact of the moving capsule against each collider per tick, then slide.
- **Pros**: No sub-steps; exact contact; fewer iterations for fast motion.
- **Cons**: Capsule-vs-OBB TOI in integers needs high-degree roots or iterative solves (sqrt chains) whose rounding is hard to make T2-identical and to test; more code; the GDD's 300 mm sub-step already bounds tunnelling.
- **Rejection Reason**: Complexity and determinism risk for no gameplay gain at ≤ 667 mm per tick.

### Alternative 3: `Fx16` sub-mm position (ADR-0006's default for sub-mm integration)
- **Description**: Store position as `Fx16` and add `v·65536/30` per tick.
- **Pros**: One numeric type across systems; ADR-0006's stated default.
- **Cons**: 30 does not divide 65536, so per-tick truncation drifts: 5000 mm/s reads 166, 167, 166 at tick 3 (499.99998 mm), contradicting F1's 166/167/167, and the F3 apex golden depends on exact remainder carry.
- **Rejection Reason**: Fails the GDD goldens; the tick-rational remainder is exact and equally integer-only.

### Alternative 4: Per-system geometry (solver, focus and Throwing each implement their own)
- **Description**: Leave Throwing F4's primitives and height sampling inside Throwing; the solver has its own.
- **Pros**: Fewer cross-system types.
- **Cons**: Three height functions with three rounding rules would disagree at tile edges (a throw lands where the carrier cannot stand); three test suites; more T2 surface.
- **Rejection Reason**: One tested `Sim/Geometry` keeps preview, focus and flights consistent with movement.

## Consequences

### Positive
- Movement is unit-testable with `dotnet test` and replays bit-identically from recorded inputs and snapshots; bot clients run without Godot.
- One height function and one set of segment primitives serve movement, focus occlusion and flights.
- No engine physics version risk (the Jolt default and its 4.7 changes are irrelevant).

### Negative
- A custom solver must be written and tuned (slide feel, step-up, depenetration) instead of reusing the engine's.
- Discrete sub-stepping costs up to 4 × 3 overlap passes per tick for the local carrier.
- The carrier departs from ADR-0006's `Fx16` default for sub-mm integration; reviewers must know why (§2).
- Presentation needs its own non-authoritative boom/camera collision against the same data (P9; ADR-0029).

## Risks
- **Slide feel at corners (two boxes meeting at an angle) jitters or sticks.** *Mitigation:* ≤ 3 iterations with velocity projection; PCM-P4 tests at corners; tune `slideIterations` only via the settings record.
- **Atan table resolution (≈ 0.056°) produces visible yaw stepping.** *Mitigation:* body yaw is presentation-smoothed; the Sim value is only used for drop position and pose.
- **Static-candidate overflow (`QueryStaticSolids` + construct registries) in dense build areas (> 64 candidates).** *Mitigation:* counted `carrierCandidateOverflow`; the buffer size is a settings value; the tick's swept AABB is small (≤ 1.2 m + radius).
- **Remote capsule staleness makes contact "soft" (0.75–2 m behind truth).** *Mitigation:* accepted by the GDD (F6 degenerate); ghosting after 45 ticks prevents locks.
- **Height-field index clamping at the map edge gives a flat cliff.** *Mitigation:* the 1000 mm edge clamp keeps carriers off the last tile edge; World validates edge tiles.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| player-character-movement.md | P2 / TR-movement-001: deterministic kinematic solver in the plain-C# Sim; no Godot physics; no floating point | §1 one pure integer `OwnedCarrier.Step`; Guidelines ban engine physics |
| player-character-movement.md | P2 / TR-movement-004: tick-time inputs via `IRemoteCapsuleSource` / `ISupportTransformSource` | §7 ports, latest received snapshot only, recorded for replay |
| player-character-movement.md | P4 / TR-movement-005: fixed-point trig LUT; bilinear int64 height field, 2 m | §3 BAM/Q15 + `AtanQ15`; §6 `WorldHeight` |
| player-character-movement.md | P4 / TR-movement-006: sub-steps ≤ 300 mm, ≤ 3 slides, step/snap 400, slope 700 ‰, ≤ 4 sub-steps | §4 sweep, counters, Int128 slope test |
| player-character-movement.md | P8, F6 / TR-movement-016: own capsule only, half overlap, cap 60, dead zone 10, ghost 45 | §5 push and ghosting |
| player-character-movement.md | P7 / TR-movement-017: support carry cap 12 000; roof in vehicle-local frame | §5 supports |
| player-character-movement.md | P6, F7 / TR-movement-018: depth from height field; hysteresis 900/700; wade 800–1000 ‰ | §5 water |
| player-character-movement.md | F1–F3 goldens (166/167/167; apex 1196, land 19) | §2 tick-rational remainders |
| player-character-movement.md | PCM-P2-1 T2 600-tick golden; PCM-PERF-1 budgets and counters | §1, §4 counters, Validation Criteria |
| throwing-catching.md | F4 primitives and RH heightfield shared with the preview | §6 `Sim/Geometry` (consumed by ADR-0015) |

## Performance Implications
- **CPU**: Owner role: ≤ 4 sub-steps × ≤ 3 slides × ≤ 64 candidates of integer tests; target p99 ≤ 0.10 ms for `CarrierStep` + focus (ADR-0014) on the PCM-PERF-1 fixture. Host role: only Dead/Frozen bodies (≤ 8), well inside the 0.20 ms host slice.
- **Memory**: `CarrierState` ≈ 160 B per player; `CarrierScratch` ≈ 2 KiB per stepping context; `AtanQ15` 2 KiB.
- **Load Time**: None (tables are literals).
- **Network**: None directly; transform widths are ADR-0020's (cm on the wire, PC Network).

## Migration Plan
No code exists. Stories create `Sim/Primitives/TrigQ15` atan extension + `tools/GenTrigQ15` atan output, `Sim/Geometry/*` with the Throwing F4 and PC F7 vectors, `Sim/Players/{CarrierState, OwnedCarrier, CarrierInput}`, the `IWorldQuery` stub in `Sim/World/` (world-stub epic), and the PCM-P2-1 golden script.

## Validation Criteria
- PCM-P4-1a/b/c, P4-2a/b/c/d, P7-1/2/3, P8-1/2/3, F6-1, F7-1 and PCM-FEEL-1 pass headless.
- PCM-P2-1: per-tick transform hashes of the 600-tick script equal the committed golden on Windows and Linux (net8.0 leg).
- Unit tests: F1 166/167/167 sequence; F3 apex 1196 at tick 9, landing tick 19; `Atan2Bam` octant/quadrant vectors and table hash; `WorldHeight.Sample` FX-Beach → −600.
- PCM-PERF-1 counters always within bounds; 0 B allocated per tick after warm-up.
- An architecture test fails if `PerformativeMail.Sim/Players` references any physics-named type or `Godot.*`.

## GDD Impact (no GDD edited by this ADR)
- **ADR-0006 §1 / Enables line ("`Fx16` carrier step"):** the carrier uses tick-rational remainders, not `Fx16`, for sub-mm integration (§2). Record this narrowing in ADR-0006's Related at its next revision; T2 scope is unchanged (`OwnedCarrier.Step` stays a T2 surface).
- **player-character-movement.md F7 `terrainH`:** state the rounding as RH (half away from zero), matching Throwing F4's "RH" heightfield, since both now call `WorldHeight.Sample`.
- **player-character-movement.md P4:** the BAM axis convention (−Z forward, CCW positive) and the `atan2` table are recorded here; P4's "→ ADR" items are closed.
- **World Generation (no GDD):** the §6 requirements are the consumer contract; ADR-0026's interface implements them (member names per ADR-0026).

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
**Verdict: no BLOCKING findings.** The §3 axis convention is consistent with Godot's coordinate system: right-handed, +Y up, −Z forward, positive `Rotation.Y` counter-clockwise from +Y. `forward(θ) = (−sin θ, −cos θ)`, `right(θ) = (cos θ, −sin θ)` and `Atan2Bam(−wx, −wz)` check out algebraically. Rejecting `CharacterBody3D`/Jolt for movement raises no engine problem. The pinned reference docs do not cover coordinate conventions or .NET runtime features, so these points rest on standard maths and .NET 8 behaviour.

**Minor notes, adopted:**
1. **Presentation yaw.** Smooth body yaw by shortest arc (wrap BAM deltas as `short`, or `Mathf.LerpAngle`). Convert camera yaw to BAM with rounding and an unchecked `& 0xFFFF`, because negative yaws wrap.
2. **Presentation colliders** (camera boom, VFX, picking) are a second copy of `IWorldQuery` geometry and can drift. A parity test (View bounds vs `StaticSolids`) is added to ADR-0019's scope. Engine physics interpolation stays off for nodes positioned from Sim state (the 4.7 behaviour is UNVERIFIED; `instance_set_interpolated` was removed in 4.5).
3. **Tables** are `static ReadOnlySpan<T> X => new T[] { … }` *properties* (the compiler emits `RuntimeHelpers.CreateSpan`, which does not allocate on little-endian x64/ARM64), never `static readonly` arrays. The SC-DET-2 table test also asserts 0 B allocated.
4. **`Int128`** is available on net8.0 and deterministic, but software-implemented. Its cost is measured under PCM-PERF-1, and it never appears in stored state.
5. **Hashing** is field-wise only (ADR-0006 §4); never hash a struct via `MemoryMarshal.AsBytes`, because padding is indeterminate.
6. **The net8.0 test leg** the T2 claims rely on is ADR-0001 §7 (revision r2: from the first Sim Core story). No test project exists yet, so PCM-P2-1 runs as soon as that leg is added.
7. **Unverified:** export trimming/AOT behaviour in 4.7 (the Sim is reflection-free, so it should be trim-safe), and the net8.0 minimum for 4.6/4.7 desktop builds (`VERSION.md`).

## Related
- ADR-0006 (units, Q15 table, T2 — this ADR adds `AtanQ15` and narrows `Fx16` use for the carrier); ADR-0003 (phase 3, `CarrierStep`); ADR-0014 (focus uses the chest point and `Sim/Geometry`); ADR-0015 (flight sweep uses `Sim/Geometry`); ADR-0016 (ClientLayer timing, extrapolation clamp); ADR-0020 (transforms, Teleport, position history); ADR-0026 (World implements `IWorldQuery`); ADR-0029 (camera boom, presentation-only).
- `design/gdd/player-character-movement.md`, `design/gdd/throwing-catching.md` F4.

## Errata
- 2026-10-17 — §2 F2: said `|Δ| = ISqrt(Δx²+Δz²)` and that truncation guarantees the step never exceeds `dvMax` (false with a floored `|Δ|`); now `|Δ|` is the ceiling square root and a final `|v'| ≤ max(vRef, |v|)` clamp follows, as shipped in `CarrierAccel.Apply`; source TD-071 item 1.
- 2026-10-17 — §3: the `AtanQ15` `ReadOnlySpan<ushort>` literal was presented without the `RuntimeHelpers.CreateSpan` Tier-0 allocation caveat; now notes the `AggressiveOptimization` getter and refers to ADR-0006 §6; source TD-008.
