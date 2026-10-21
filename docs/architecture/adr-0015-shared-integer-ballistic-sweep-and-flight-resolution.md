# ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist` (Step 5.5). TD-ADR skipped (lean mode). Acceptance requires the project owner's explicit confirmation (batch request by the main session). Inputs: `design/gdd/throwing-catching.md` T1–T15, F1–F5, F8, ContentValidator rules 1–18, Edge Cases, ACs TC-F1-1, TC-F2-1, TC-F4-1, TC-F5-1, TC-P-1, TC-P-2; `design/gdd/grid-inventory.md` R19 (via ADR-0009); `design/gdd/player-character-movement.md` F5; `docs/architecture/architecture.md` §Sim.Throwing, Appendix A; ADR-0003–0009 (Accepted); ADR-0013, ADR-0014 (Proposed).

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Amended before acceptance: world solids, the cache key and the dependency on ADR-0026 now follow ADR-0026 (`QueryStaticSolids`, no `StaticVersion`). The stale ADR-0017 note is refreshed, and the Ordering Note states the M-1a scope (ADR-0020/0021 are M0 follow-ons, not dependencies).

## Summary
Throws must land identically for the thrower's preview, the host and every peer, with no dice (ratified D1), and every flight must end in exactly one terminal state so inventory conservation holds. This ADR fixes the Sim-side throw pipeline in `PerformativeMail.Sim/Throwing/`:
- host validation of a release and the integer launch F1;
- the closed-form ballistic F2, evaluated O(1) at any age;
- a per-tick swept-segment test F4 over a static 4 m broad phase and bounded dynamic receivers, built on ADR-0013's `Sim/Geometry`;
- earliest-contact resolution with a fixed rank table and a `Mix64` tie-break;
- exactly one terminal per flight, resolved synchronously in phase 4;
- host catch validation over position history;
- a preview that calls the very same functions.

Throwing's "RH" is defined once, as floor-based round-half-up.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET, C# |
| **Domain** | Physics (Sim-side, plain C#; no engine surface) |
| **Knowledge Risk** | LOW. Everything is integer code in `PerformativeMail.Sim` (ADR-0001). The aim arc, trail and ghost are Presentation (ADR-0016/0019) and only *call* these functions. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (Jolt default is presentation-only; nothing applicable) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None engine-side. TR-throwing-003's 10 000-throw preview/host equality and TC-F1/F2/F4 vectors run under xUnit; T2 on Windows + Linux (ballistic is a shared pure function, ADR-0006 §5). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (phase 4 `ThrowingStep`, phase 0 handlers, `I…Api` rules, Run as orchestrator, `ITickEndCheck`); ADR-0004 (`FlightId` u16 wrap-safe, creation-order processing, `PlayerSlot`, receiver id maps); ADR-0005 (no Throwing stream; `SplitMix.Mix64` as a pure hash); ADR-0006 (integer maths, `FloorDiv`/`CeilDiv`/`ISqrt`, "ADR-0015 records which RH form each Throwing formula uses", dump, 0-alloc, T2); ADR-0007 (Throwing OpKind block `0x0140–0x017F`, reasons ≥ 64, validate-then-mutate); ADR-0008 (events ≤ 48 B, `Replicated`, `ResyncScope` Throwing); ADR-0009 (`IInventoryApi.DetachForThrow`, `TryCatch`, `TryInsert`, `ConsumeUnit`, WorldItem creation, Flight ledger boundary); ADR-0010 (`ThrowDef`/settings, StatSheet `ThrowRangePermille`, `ThrownCorrectPayMult`); **ADR-0013** (`Sim/Geometry` intervals and heightfield march); **ADR-0026** (`IWorldQuery`: height field, water plane, `QueryStaticSolids`, `Bounds`); **ADR-0014** (`ICatchPredictor` port, Interact resolution that arms catches, `ClaimedPosition`, `IPlayersApi`) |
| **Enables** | ADR-0016 (preview placement and launch ghost use these functions); ADR-0021 (wire layouts, channels, ghost and catch protocol at M0 build on §1, §7, §9); ADR-0017 (`ResolveThrownLanding` call contract) |
| **Blocks** | Throwing & Catching epic: launch, ballistic, sweep, receivers, terminals, preview equality, Sim-side catch (TR-throwing-001, 003, 005–008, 013); world-stub (heightfield and solids as receivers) |
| **Ordering Note** | Mailbox landings call Mail's `ResolveThrownLanding` (ADR-0017, Accepted 2026-10-16). Teammate and catcher receivers read Player Character's F5 history (ADR-0020, M0; Sim-side ring pulled forward for the M-1a catch stories). **Scope:** this decision covers M-1a offline / host-local flight on the loopback seam. ADR-0020 and ADR-0021 (M0) build on §1, §7 and §9 and are not dependencies of it. Goons (M-1b), belts (M-1b) and vehicles (M1) are receivers whose sources are empty at M-1a. |

## Context

### Problem Statement
The throw is the game's comedy verb and its main source of disputes: a preview that says green must land green, every peer must see the same landing, and a stack in flight must never be lost or duplicated (Grid R19). Throwing's GDD specifies integer F1–F5 with floor-division rules, ranks and a no-dice ruling, but no module shape, receiver gathering, call order, capacity or RH definition. ADR-0006 explicitly defers "which RH form each Throwing formula uses" to this ADR. The prototype used floats and Godot physics, which are not portable to a host-authoritative, headless Sim.

### Constraints
- Host-authoritative flights; the preview is a forecast that must equal the host for static geometry (T3).
- Phase 4 runs after `Inputs.Apply` (hand ops) and before automation (Mail R6: hand → thrown landings → feeders → NPC drops); catches and landings are synchronous `I…Api` calls inside phase 4; damage is queued for phase 10 (ADR-0003).
- No RNG (ADR-0005), integer only (ADR-0006), `FlightId` never compared numerically (ADR-0004).
- Phase 4 slice: 0.15 ms p99 / 0.5 ms max with 16 flights + 8 catchers, 0 B (TC-P-2).

### Requirements
- TR-throwing-001 (host-authoritative closed-form integer ballistic, O(1) `p(k)`), TR-throwing-003 (preview calls the same Sim function; identical over 10 000 throws), TR-throwing-005 (swept segment per tick per flight ≤ 90 ticks: slab, circle, cylinder, sphere, heightfield), TR-throwing-006 (receiver ordering; flights in creation order), TR-throwing-007 (broad phase bound |R| ≤ 65 536 mm; static 4 m grid), TR-throwing-008 (phase 4, 0.15 ms p99, 0 alloc, 16 flights + 8 catchers), TR-throwing-013 (exactly one terminal per flight; conservation through flush and disconnect).

## Decision

### 1. Release → host validation → launch (phase 0)
- **Op.** `ThrowRelease` = OpKind `0x0140` (Throwing block, ADR-0007). `ReqId` carries the GDD's `seq`. Payload (28 B, no padding, read with `MemoryMarshal.Read`): `entryId u32, units u8, side i8 (±1), flags u8, reserved u8, feet 3×i32 (claimed origin, P13 layout), dir 3×i16 (scale 16384), pad u16`. There is no tick field and no hold duration (Input R11, H-R10-9). `CatchArm` = `0x0141` `{down u8}` (§7).
- **Ownership split with ADR-0021.** This ADR fixes the Sim handler semantics and payload that M-1a solo play needs over loopback. ADR-0021 (M0) owns wire encoding, channels, the launch ghost protocol and catch timing under latency, and may extend these payloads by amendment. ADR-0009 names ADR-0021 for this OpKind; see GDD/ADR Impact.
- **Validation chain** (first failure is the reply; reasons keyed by `(ThrowRelease, Reason)`):

  | # | Check | Reply |
  |---|---|---|
  | 1 | malformed, or the entry is not in the sender's hotbar | `bad_request` (core 8) |
  | 2 | entry unknown, not the selected slot, or `count ≠ units` | `stale_entry` 64 |
  | 3 | actor not Alive | `dead` 65 |
  | 4 | actor Frozen | `frozen` 66 |
  | 5 | swimming | `swimming` 67 |
  | 6 | not on foot | `unavailable` 68 |
  | 7 | leaky bucket: `T < TAT − throwBurstTicks` | `throw_cooldown` 69 |
  | 8 | `16000 ≤ n ≤ 16768` and `−876 ≤ 1000·dy/n ≤ 980` (n via `ISqrt`, components widened to `long`) | `bad_direction` 70 |
  | 9 | claimed feet corroborated by F5 (`IPlayersApi` history) | **not a rejection**: use the newest sample, set `originClamped` |
  | 10 | `liveFlights == maxFlights` | `flight_cap` 71 (counted; unreachable under the bucket) |

- **Launch at the apply tick `T`** (no back-dating): F1 below; `TAT ← max(TAT, T) + throwCooldownTicks`; `IInventoryApi.DetachForThrow(slot, entryId, units)` (PLACED → FLIGHT, its `Removed` event); append the `FlightRecord`; raise `ThrowLaunched`; reply `ok`.

### 2. Throwing's rounding: one definition of "RH"
`Throw.RH(long n, long d)` = **`Fix.FloorDiv(2·n + d, 2·d)` for `d > 0`** (round half toward +∞, floor-based, as the GDD defines it: "for a negative x it is floor((2x + d)/(2d))"). It is **not** `Fix.MulDivRH` (half away from zero). The GDD's own golden needs it: TC-F2-1's `p(1).z = 19700 + RH(−13695/30) = 19700 + RH(−456.5) = 19244`, while half-away-from-zero would give 19243.

| Formula | Rounding |
|---|---|
| F1 `v`, velocity components, origin offsets | `Throw.RH` |
| F2 `px, py, pz` (both terms) | `Throw.RH` |
| F4 slab/circle/sphere intervals | `CeilDiv` (enter) / `FloorDiv` (exit), via ADR-0013 `SegmentIntervals` |
| F4 heightfield samples `Q_i`, contact point | `Throw.RH` (the height value itself is `WorldHeight.Sample`, ADR-0013) |
| F5 prediction | same as F2/F4 |
| F7 preview label money | Mail's rounding: `Fix.MulDivRH` / `Cents.MulPermille` (must equal Mail's host figures, ADR-0017) |
| F8 drop point | `Throw.RH` |

### 3. Launch (F1) and the ballistic (F2)
- **F1.** `n = ISqrt(dx²+dy²+dz²)`; `speedPm = ISqrt(ThrowRangePermille × 1000)` (StatSheet, personal; 1000 → 1000, 1500 → 1224); `v = RH(baseSpeed[class] × speedPm, 1000)`; `vel = (RH(v·dx, n), RH(v·dy, n), RH(v·dz, n))`; `h = ISqrt(dx²+dz²)` (if `h < 2048`, use body yaw for the horizontal basis); `origin = feet + (RH(fwdOffMm·dx, h) + RH(side·sideOffMm·(−dz), h), handHeightMm, RH(fwdOffMm·dz, h) + RH(side·sideOffMm·dx, h))`. Before launch, the chest→origin segment is tested against solids and the heightfield; if blocked, the origin is the chest point.
- **F2 (closed form, no accumulation).** `Ballistic.PositionAt(in Launch l, int k)`: `px = ox + RH(vx·k, 30)`, `pz = oz + RH(vz·k, 30)`, `py = oy + RH(vy·k, 30) − RH(g·k², 1800)` for `k = 0 … maxFlightTicks` (90). Any age is one O(1) call, so a late event, rejoin catch-up or preview frame gets identical integers. All values fit `int` (≤ 3.2 × 10⁸ at knob extremes); products are `long`.

### 4. The per-tick sweep (phase 4, `Throwing.Step`)
- **Order.** Live flights are processed in **creation order** (an array kept in launch order, compacted stably at the end of the step). For each flight with age `k ≥ 1` (age 0, the launch tick, never sweeps), the segment `P0 = p(k−1) → P1 = p(k)` is tested.
- **Read state at the start of the step**: life states, armed catchers, goon liveness (read live for "alive and not dying" between flights, T6 edge case).
- **Receivers gathered per segment** (all positions integer mm):
  - **Static (from a static 4 m grid built at run start and updated on change):** mailbox catch volumes (AABB from Mail's registry; descending entry only: counted only if `P1.y < P0.y`; a flight already inside whose next segment descends gets `f = 0`); belt-lane top faces (M-1b; a full/refusing/just-deconstructed lane is not a receiver); world solids from `IWorldQuery.QueryStaticSolids` (ADR-0026 §5: houses, roofs, Post Office; no second world grid) and, from M-1b, player-built constructs from their owners' registries (`Ground` terminal); map-edge vertical planes (solid rank).
  - **Heightfield and water:** `HeightfieldMarch` (ADR-0013) with the water plane `y = 0` over water tiles.
  - **Dynamic (bounding-sphere reject first; then |R| ≤ 65 536 mm holds for the quadratics):** armed catchers (sphere `catchRadiusMm` about feet + 1000, tested at every F5 history sample within `catchHistoryMaxOffsetMm` of the newest; earliest `f`); teammates (cylinder r 300 × h 1400 at the **newest** reported position; Alive, non-thrower, not mounted, not position-stale); goons (cylinder EnemyDef radius + `enemyHitSlackMm`, height; M-1b); vehicles (OBB solids at the driver's newest transform; M1).
  - **Never receivers:** the thrower (own flight), Dead and Frozen bodies, mounted carriers (their vehicle body is a solid).
- **Winner.** Each hit yields `f` ‰ (`f = max(tEnter, 0)`). The winner is the minimum of `(f, rank, dist²(contact, receiver centre), TieHash)` with ranks **armed catcher 0 · mailbox 1 · goon 2 · teammate 3 · belt 4 · solid 5 · ground 6 · water 7** (content table, validator rule 5) and `TieHash = SplitMix.Mix64(((ulong)flightId.Value << 32) | receiverKey)` (`receiverKey` = the receiver's `EntityId.Value`; destinations for volumes). `FlightId` is a hash input only, never compared. The contact point is `P0 + RH(D·f, 1000)`.
- **No contact by age 90** → `TIMEOUT`: drop onto the surface directly below `p(90)` → `GROUND` or `WATER`.

### 5. Terminal resolution: exactly one per flight (synchronous, phase 4)

| Terminal | Synchronous call(s), inside phase 4 |
|---|---|
| `CAUGHT{unitsIn, unitsFumbled}` / `FUMBLE` | `IInventoryApi.TryCatch(slot, payload)` (TargetFirst hotbar-first); fumbled newest units → one WorldItem at the catcher's feet; arm cleared |
| `MAILBOX_ACCEPT` / `MAILBOX_MISDELIVER` / `MAILBOX_REJECT` | `IMailApi.ResolveThrownLanding(dest, entry, thrower, thrownPm, releaseTick)` (ADR-0017) decides; a Reject or the wallet-floor remainder → one WorldItem at the F8 drop point (Throwing owns the bounce) |
| `HIT_ENEMY` | `ApplyDamage(F6, Thrown, thrower)` **queued** for phase 10; entry → one WorldItem at the goon's ground point |
| `BONK` | entry → one WorldItem at the teammate's feet (water → `WATER`) |
| `BELT` | `TryInsert(lane, point, unit)` oldest first; remainder → one WorldItem at the contact point |
| `GROUND` (terrain or solid) / `WATER` | one WorldItem (`origin = Thrown`); solids drop straight down; water floats |

- WorldItem placement follows T6 (snap to walkable within 1000 mm, growing back along −v, else the receiver's base, clamped 1000 mm inside the map), through `IInventoryApi` only.
- **Conservation.** A flight leaves the live list only by resolving; `ITickEndCheck` asserts `launched == resolved + live` and that no `FlightId` resolved twice (always-on record, never throws). Units in flight are in the Flight ledger column (ADR-0009), so Grid's conservation check covers them.
- **Run end.** Run Structure (the orchestrator) calls `IThrowingApi.FlushAllAsTimeout()` in phase 12 when Victory or Run Over latches: every live flight resolves at once as `TIMEOUT` in creation order, with no mailbox and no pay. A landing on the triggering tick resolved normally first, in phase 4.
- **Disconnects** never cancel a flight: `throwerId`, `thrownPm` and `actorId` stay on the record.
- **Event.** `FlightResolved` (one per flight) is raised before any domain events it causes (`DeliveryOutcome` etc.) in the same call.

### 6. Leaky-bucket cooldown (T1)
Per slot `TAT` (absolute tick). Accept at `T` iff `T ≥ TAT − throwBurstTicks`; then `TAT ← max(TAT, T) + throwCooldownTicks`. A rejected release does not move `TAT`. Sustained ≤ 6 throws/s per player; ≤ 2 back-to-back after a hitch.

### 7. Catch (host side of F5; Sim-side pull-forward)
- `CatchArm{down|up}` (Interact resolution, ADR-0014 §3) is applied in phase 0. `armedUntil = min(max(upApplyTick, downApplyTick + catchTapTicks (15)), downApplyTick + 90)`; Toggle sends `up` on the second press. Armed at tick `t` iff `downApplyTick ≤ t < armedUntil`.
- Eligibility: Alive, on foot, World or Holding-closed, not swimming, not aiming, not the thrower. Death, mount, swim, panel open, aim start, disconnect or rejoin clears the arm (read from state at the start of phase 4; no upward calls).
- Contact: minimum `f` over the catcher's history samples (`catchHistoryTicks` 9) within `catchHistoryMaxOffsetMm` (3D) of the newest. One press catches one flight (the arm clears on CAUGHT or FUMBLE).
- The protocol under latency (timing, ghosts, rejoin) is ADR-0021's at M0.

### 8. The preview and `ICatchPredictor` (same functions, read-only)
- **Throw preview** = `Launch.Compute` (F1 from the predicted feet and the quantised dir) → `Ballistic.PositionAt` → `FlightSweep.FirstContact` with a **static-only receiver view** (catch volumes with Mail's client `Evaluate` for colour, terrain/water, solids, belts) plus teammate hat pins (current catch sphere, display only — never ✓/✗, never predicted). The preview never sees goons. Identical integers to the host for static geometry by construction; the cache key is (dir, origin, the construct registries' version (`WorldTables` is immutable, ADR-0026; constant at M-1a), destination phase, wallet, teammate positions). *Where* it runs (per rendered frame vs per tick) and the launch ghost are ADR-0016's.
- **`ICatchPredictor.Evaluate`** (implements ADR-0014's port, client side): runs F4 forward for `P = catchPredictMs·30/1000` (15) ticks on each non-own mirrored flight with this client as an armed catcher at its current position; returns `{Predicted, FlightId, Thrower, Units, PredictedStaticReceiver}`. The prompt hysteresis (on after 3, off after 5) lives in Throwing's `Previews` binder; press resolution uses the raw per-tick prediction (D3).
- **TR-throwing-003 test:** 10 000 seeded throws over a fixture street — preview contact (receiver, f, point) equals the host's for static receivers, bit for bit.

### 9. State, events, capacity, dump
- **`FlightRecord`** (SoA arrays, preallocated): `FlightId`, entry ref + `mailIds` by reference (ADR-0009 Flight ledger), thrower slot, `classPts`, origin, vel, `launchTick`, `thrownPm` (snapshotted), `releaseTick`, `openAtRelease`, `hop` (T13), `originClamped`. **Capacity `maxFlights` = 160** (8 players × 6/s × 3 s = 144, plus slack); perf is budgeted at 16 and advisory at 60 (TC-P-2). Per slot: `TAT`, arm state, `lastCatch` (T13).
- **Events (Throwing block, `Replicated`, ResyncScope Throwing, ≤ 48 B):** `ThrowLaunched {flightId u16, thrower u8, flags u8 (originClamped), entryId u32, units u8, hop u8, kindDef u16, origin 3×i32, vel 3×i32, launchTick u32}` = 40 B (enough for any peer to evaluate F2 at any age); `FlightResolved {flightId u16, terminal u8, rank u8, receiver u32, contact 3×i32, tick u32, unitsIn u8, unitsFumbled u8, worldItem u32}` = 32 B.
- **Dump:** Throwing's `ISimStateContributor` writes live flights in creation order, the `FlightId` serial, per-slot `TAT`, arm state and `lastCatch`.

### Architecture Diagram
```
 client (aiming)                          host SimWorld.Tick(T)
  Preview: Launch.Compute ─┐               phase 0  ThrowRelease handler: chain 1–10 → F1 → DetachForThrow
           Ballistic.PositionAt            │                            → FlightRecord → ThrowLaunched → reply
           FlightSweep.FirstContact(static)│        CatchArm handler → arm window
  ICatchPredictor.Evaluate (F4 × 15 ticks) │ phase 4  Throwing.Step: flights in creation order, k ≥ 1:
  release → ThrowRelease{feet, dir, units} ┘            P0=p(k−1) → P1=p(k) ── Sim/Geometry intervals (ADR-0013)
                                                        receivers: static 4 m grid │ heightfield/water │ dynamic
                                                        winner (f, rank, dist², Mix64) → one terminal:
                                                        TryCatch │ ResolveThrownLanding │ queue ApplyDamage │
                                                        TryInsert │ WorldItem (IInventoryApi) → FlightResolved
                                           phase 12 Run → IThrowingApi.FlushAllAsTimeout (run end)
                                           tick end launched == resolved + live
```

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.ThrowingCatching;   // erratum 2026-10-20: was PerformativeMail.Sim.Throwing — see Errata (folder stays Sim/Throwing/)
public readonly struct Launch { public readonly Vec3Mm Origin; public readonly Vec3Mm Vel; public readonly int G; }
public static class Throw      { public static long RH(long n, long d); }                     // FloorDiv(2n + d, 2d), d > 0
public static class LaunchMath { public static bool Compute(in LaunchInput i, out Launch l, out bool originFromChest); } // F1
public static class Ballistic  { public static Vec3Mm PositionAt(in Launch l, int k); }       // F2, O(1)
public static class FlightSweep { public static bool FirstContact(in Vec3Mm p0, in Vec3Mm p1, in ReceiverView v,
                                      FlightId id, out Contact c); }                            // F4 + ranking + TieHash
public interface IThrowingApi { void FlushAllAsTimeout(); int LiveFlights { get; } }           // Run Structure only (phase 12)
public sealed class CatchPredictor : ICatchPredictor { /* client side; pure over mirrored flights */ }
public enum ThrowReason : ushort { StaleEntry = 64, Dead, Frozen, Swimming, Unavailable, ThrowCooldown, BadDirection, FlightCap }
public enum Terminal : byte { Caught, Fumble, MailboxAccept, MailboxMisdeliver, MailboxReject, HitEnemy, Bonk, Belt, Ground, Water, Timeout }
```

### Implementation Guidelines
- The host, the preview and every peer **must** call the same `LaunchMath`, `Ballistic` and `FlightSweep` functions; there **must never** be a second ballistic implementation (Presentation included).
- Throwing's RH **must** be `Throw.RH` (floor-based) everywhere §2 says so; C#'s truncating `/` **must never** be applied to a signed numerator.
- Flights **must** be processed in creation order; `FlightId` **must never** be compared numerically; ties **must** use `(f, rank, dist², Mix64)`.
- Quadratic interval tests **must** run only after the broad-phase/bounding-sphere reject (|R| ≤ 65 536 mm).
- Every flight **must** end in exactly one terminal; units **must** leave Flight only through `IInventoryApi`/`IMailApi`; damage **must** be queued, never applied in phase 4.
- Throwing **must not** hold or draw any RNG stream.
- `Throwing.Step` **must** allocate 0 B; buffers are preallocated to `maxFlights` and fixed candidate capacities (overflow counted).
- Run end **must** flush via `IThrowingApi.FlushAllAsTimeout` from phase 12 only.

## Alternatives Considered

### Alternative 1: Euler integration (`v += g·dt; p += v·dt`) per tick
- **Description**: Step each flight's velocity and position every tick.
- **Pros**: Familiar; trivially supports drag or wind later.
- **Cons**: Late events, rejoin catch-up and previews must replay every tick; accumulated rounding differs between a preview evaluated at age 20 and a host stepped 20 times; not O(1).
- **Rejection Reason**: Violates TR-throwing-001 (O(1) `p(k)`) and makes preview equality fragile.

### Alternative 2: Godot physics bodies for flights (`RigidBody3D` / ray casts)
- **Description**: Launch a physics body and read collisions.
- **Pros**: Free bounce, spin, collision shapes.
- **Cons**: Floating point, engine-dependent, client/host divergence, outside the Godot-free Sim; the prototype's approach.
- **Rejection Reason**: Incompatible with host authority, preview equality and headless tests.

### Alternative 3: Half-away-from-zero RH (`Fix.MulDivRH`) for Throwing too
- **Description**: Use ADR-0006's general RH everywhere.
- **Pros**: One rounding function in the codebase.
- **Cons**: Contradicts the GDD's explicit floor-based definition and its golden (`p(1).z` 19244 vs 19243).
- **Rejection Reason**: The GDD vectors are normative; ADR-0006 explicitly delegated this choice here.

### Alternative 4: Capacity 16 flights (architecture.md sketch)
- **Description**: Size the flight table to the perf fixture.
- **Pros**: Smaller memory.
- **Cons**: The leaky bucket allows up to 144 concurrent flights from 8 players; a cap of 16 would reject legal throws and turn a perf fixture into a rule.
- **Rejection Reason**: Rules come from the bucket; the fixture sizes the budget, not the table.

## Consequences

### Positive
- A preview that is provably the host's computation for static geometry; peers evaluate any age in O(1) from `ThrowLaunched` (no flight streaming).
- One geometry implementation shared with movement and focus; one terminal per flight keeps conservation provable.
- No RNG or floats in Throwing, so T2 covers it with the other shared pure functions.

### Negative
- Two RH definitions exist in the Sim (general half-away-from-zero, Throwing floor-based); reviewers must use the right one (§2 table).
- Mailbox landings depend on ADR-0017 and catch latency semantics on ADR-0021/0020, so some stories wait on unwritten ADRs.
- 160-entry SoA tables are mostly empty.

## Risks
- **Two RH functions get mixed up.** *Mitigation:* `Throw.RH` is the only rounding call in `Sim/Throwing` except F7; an analyzer-free architecture test greps `Sim/Throwing` for `MulDivRH` outside the F7 label code.
- **Phase-4 budget exceeded with many catchers × history samples.** *Mitigation:* bounding-sphere reject before per-sample tests; TC-P-2 measured from M-1a (advisory), blocking from M0.
- **Heightfield march misses a thin ridge between samples** (≤ 250 mm step). *Mitigation:* accepted by the GDD (chord error ≤ 1.4 mm; ridges thinner than 250 mm are not authored); World validates.
- **ADR-0009/0021 ownership confusion over `ThrowRelease`.** *Mitigation:* the GDD/ADR Impact entry; ADR-0021 must cite this ADR's payload as its M-1a baseline.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| throwing-catching.md | T5, F2 / TR-throwing-001: host-authoritative closed-form integer ballistic, O(1) `p(k)` | §3 `Ballistic.PositionAt`; host launches at apply tick |
| throwing-catching.md | T3 / TR-throwing-003: preview calls the same function; identical over 10 000 throws | §8 |
| throwing-catching.md | F4 / TR-throwing-005: per-tick swept segment ≤ 90 ticks; slab/circle/cylinder/sphere/heightfield | §4 via ADR-0013 `Sim/Geometry` |
| throwing-catching.md | T6 / TR-throwing-006: earliest contact, rank table, tie-breaks; creation order | §4 |
| throwing-catching.md | F4 / TR-throwing-007: |R| ≤ 65 536 bound; static 4 m grid | §4 |
| throwing-catching.md | TR-throwing-008: phase 4, 0.15 ms p99, 0 alloc, 16 flights + 8 catchers | §4, §9, Performance |
| throwing-catching.md | T6, T11 / TR-throwing-013: exactly one terminal; conservation through flush and disconnect | §5 |
| throwing-catching.md | T1 leaky bucket; T4 validation chain and no back-dating; F1 launch | §1, §3, §6 |
| throwing-catching.md | T7, F5 host catch window and history contact | §7 |
| throwing-catching.md | D1 no dice | §2–§4 have no spread term; ADR-0005 no stream |

## Performance Implications
- **CPU**: ≈ 20 candidates × 16 flights × 40–100 ns ≈ 50 µs, plus catchers ≤ 77 µs ≈ 0.13 ms worst (GDD estimate) inside the 0.15 ms p99 slice. The preview (TC-P-1) ≤ 0.3 ms p95 per frame, 0 B.
- **Memory**: 160 flights × ≈ 64 B ≈ 10 KiB; static receiver grid ≈ map cells × 4 B (shared index with World for solids).
- **Load Time**: Static grid built at run start (mailboxes from Mail's registry).
- **Network**: `ThrowLaunched` 40 B, `FlightResolved` 32 B per throw; no per-tick flight traffic.

## Migration Plan
No code exists. Stories create `Sim/Throwing/{Throw, LaunchMath, Ballistic, FlightSweep, FlightTable, ThrowReleaseHandler, CatchArmHandler, ThrowingStep, CatchPredictor}`, the TC-F1/F2/F4 vector tests, the preview-equality test and the conservation tick-end check.

## Validation Criteria
- TC-F1-1, TC-F2-1 (incl. `p(1) = (10250, 1292, 19244)` and the rejoin age-20 equality), TC-F4-1 (`f = 154`), TC-F5-1 pass.
- 10 000-throw preview/host equality passes; T2 golden for `Ballistic`/`FlightSweep` on Windows and Linux.
- Every terminal row of T6 has a test producing exactly one terminal and the matching WorldItem/inventory/mail effect; the tick-end check never fires on SC-FIX-WORST.
- TC-P-2: ≤ 0.15 ms p99 (advisory until M0), 0 B per tick (blocking).

## GDD Impact (no GDD or accepted ADR edited by this ADR)
- **ADR-0009 §Op kinds ("`throw` is Throwing's `ThrowRelease` OpKind (ADR-0021)"):** the Sim-side OpKind (`0x0140`), payload and handler chain are fixed here for M-1a; ADR-0021 keeps wire/channel/ghost/catch-latency at M0 and may extend by amendment.
- **architecture.md §Sim.Throwing "`FlightRecord`s (≤16)":** capacity is 160 (§9); 16 is the perf fixture.
- **input-controls.md R11 throw-release fields `{reqId, entryId, originClient, dir}`:** Throwing T4 adds `units` (and `side`); the payload follows Throwing.
- **throwing-catching.md T6 / F4 `hash(flightId, receiverId)`:** defined as `SplitMix.Mix64(flightId << 32 | receiverKey)` (ADR-0005 primitive, no stream).
- **simulation-core.md R27 "Throw spread is in mrad"** stays stale (already listed by ADR-0006).

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
**Verdict: no BLOCKING findings; no engine surface.** Minor notes, adopted:
1. `MemoryMarshal.Read<T>` throws on a short span and is native-endian. Payload reads are guarded by ADR-0007's drain pre-check (`PayloadLength == sizeof(T)`, else `unknown_kind`) and by an explicit length check in the handler (`bad_request`, never an exception). Payload structs use explicit sequential layout and `byte` flags only (no `bool`), with no padding.
2. The aim arc, trail and ghost (Presentation) must call these functions and must not use engine physics or engine interpolation for flight positions. Engine interpolation stays off for Sim-positioned nodes (ADR-0019).
3. The net8.0 minimum for 4.6/4.7 desktop builds is UNVERIFIED (`VERSION.md`).

## Related
- ADR-0013 (`Sim/Geometry`); ADR-0026 (`IWorldQuery`, `QueryStaticSolids`); ADR-0014 (`ICatchPredictor`, Interact resolution, `CheckInteraction`); ADR-0016 (preview placement, launch ghost, outcome-after-event); ADR-0017 (`ResolveThrownLanding`, `Evaluate`); ADR-0020 (F5 history); ADR-0021 (flight protocol, catch under latency); ADR-0009 (`DetachForThrow`, `TryCatch`, Flight ledger); ADR-0005 (no stream; `Mix64`); ADR-0006 (rounding delegation).
- `design/gdd/throwing-catching.md`.

## Errata
- 2026-10-20 — Key Interfaces: the namespace was `PerformativeMail.Sim.Throwing`; now **`PerformativeMail.Sim.ThrowingCatching`** (tests `PerformativeMail.Sim.Tests.Unit.ThrowingCatching`), because a `Sim.Throwing` namespace shadows `Content.Rules.Throwing` (`Content/Validation/RuleRegistry.cs:70`, CS0234) and breaks the build — the same pattern as `Sim/Mail/` → `PerformativeMail.Sim.MailDestinations` (TD-074). The folder stays `src/PerformativeMail.Sim/Throwing/` (Migration Plan and stories unchanged), as implemented in PR #7 (throwing-catching story 001, open at the time of writing). **Accepted by Evan 2026-10-20** (owner ruling in chat, 22:31 PT); source TD-107 item 1 (added by PR #7).
