# Story 002: `FlightSweep.FirstContact` — F4 swept contact, receiver ranks, descending-entry mailbox rule and the `(f, rank, dist², Mix64)` winner

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + ~400 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Primitives story (2 of 3).** The one swept-segment test used by the host's `Throwing.Step`, the aim preview and (later) the catch prompt predictor. It works on a `ReceiverView` of spans handed to it; story 003 builds that view from the world and Mail.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 receivers and resolution order; F4 swept contact; Edge Cases "Simultaneous events", "Zero, max and out-of-range")
**Requirement**: `TR-throwing-005` (swept segment per tick, ≤ 90 ticks: slab, circle, cylinder, sphere, heightfield), `TR-throwing-006` (receiver resolution ordering), `TR-throwing-007` (broad-phase bound |R| ≤ 65 536 mm; static 4 m grid — the bound and reject are here, the grid is story 003), `TR-throwing-003` (the preview calls this same function)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F4-1 a/b/c/h, TC-T6-1, TC-T6-2, TC-E-1 b.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§4 the per-tick sweep, Key Interfaces `FlightSweep.FirstContact`, `ReceiverView`) — Accepted 2026-10-16 (primary); ADR-0013 §6 (`Sim/Geometry`: `SegmentIntervals`, `HeightfieldMarch`) and ADR-0026 (`IWorldQuery` heightfield and water) secondary — Accepted
**ADR Decision Summary**: Each tick a flight sweeps the segment `p(k−1) → p(k)` against every receiver in the view; each hit yields `f` in ‰ (`f = max(tEnter, 0)`). The winner is the minimum of `(f, rank, dist²(contact, receiver centre), TieHash)` with ranks armed catcher 0 · mailbox 1 · goon 2 · teammate 3 · belt 4 · solid 5 · ground 6 · water 7 and `TieHash = SplitMix.Mix64((flightId << 32) | receiverKey)`. Mailbox volumes count only on a **descending** segment (`P1.y < P0.y`); a flight already inside whose next segment descends gets `f = 0`. Quadratic interval tests run only after the |R| ≤ 65 536 mm reject. The contact point is `P0 + RH(D·f, 1000)`.
**ADR Version**: ADR-0015 2026-10-16; ADR-0013 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Goldens run on the `net10.0` and `net8.0` legs (T2).

**Control Manifest Rules (Feature layer)**:
- Required: ties break on `(f, rank, dist², Mix64)`; `FlightId` is a hash input and is never compared numerically; quadratic tests run only after the broad-phase reject (|R| ≤ 65 536 mm) (ADR-0015, ADR-0004)
- Required: World solids come from `IWorldQuery.QueryStaticSolids` (story 003 gathers; this story only consumes the view); heightfield and water via `HeightfieldMarch` (ADR-0013 §6, ADR-0026 §5)
- Forbidden: a second ballistic or sweep implementation; a second world-solid grid (ADR-0015, ADR-0026)
- Guardrail: a segment test ≈ 40–100 ns, 0 B allocated (ADR-0015 Performance)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-F4-1, TC-T6-1, TC-T6-2, TC-E-1 b), scoped to this story. The goon (d, f, g) and catcher (e) vectors of TC-F4-1 are M-1b / story 023 and are not failed here:*

- [ ] **TC-F4-1 (FX-Flat).** P0 (10250, 963, 10570) → P1 (10250, 837, 10114) against MB-A (catch volume x 9750–10750, y 600–1400, z 9500–10500): f = 154, contact (10250, 944, 10500), kind Mailbox.
- [ ] **TC-F4-1 b.** D = (0, −126, −70) → f = 1000 hit; D = (0, −126, −69) → no hit (the exact `CeilDiv`/`FloorDiv` edge).
- [ ] **TC-F4-1 c.** P0 (10250, 1000, 10200) inside the volume with D (0, −100, −50) → f = 0.
- [ ] **TC-F4-1 h.** Heightfield march, n = 4, first sample at or below H at i = 2 with gaps 100 and −50 → f = 417.
- [ ] **TC-T6-1 (parametrised).** Stub receivers at equal f = 500: rank decides for the 7 adjacent rank pairs (catcher over mailbox, mailbox over goon, goon over teammate, teammate over belt, belt over solid, solid over ground, ground over water). Equal rank → the smaller `dist²` to the receiver centre → the lower `Mix64(flightId << 32 | receiverKey)`: across 1 000 `FlightId`s two otherwise-identical receivers each win at least once.
- [ ] **TC-T6-1 b.** An earlier f beats rank: a mailbox at f = 300 beats an armed-catcher stub at f = 400.
- [ ] **TC-T6-2 (descending only).** MB-A: (10250, 1300, 10800) → (10250, 1200, 10200) lands (descending). The same segment with start y 1100 (rising) or y 1000 → 1000 (flat) does not. A flight rising through MB-B and then descending through MB-A lands in MB-A (first qualifying volume wins).
- [ ] **TC-E-1 b (sweep half).** An origin inside MB-A's volume: a descending first segment yields f = 0 at T+1; an upward one produces no hit.
- [ ] **Terrain and water.** A segment that crosses terrain above dry land yields kind Ground at the `HeightfieldMarch` contact; a segment that reaches the water plane (y = 0) over a water tile (terrain below 0) yields Water; Water ranks 7 and Ground 6 so a shoreline tie goes to Ground.
- [ ] **Map edge.** A segment leaving `Bounds` yields kind Solid at the boundary crossing (the map's vertical sides are rank-5 solids, T6).
- [ ] **Broad phase.** Dynamic spans (sphere/cylinder candidates) are rejected by a bounding-sphere test against the segment before any quadratic form; a counted guard rejects |R| > 65 536 mm and the form is not evaluated; an int64 property test with |R| = 65 536 and |D| up to 1 200 mm never overflows.
- [ ] **A = 0.** A zero-length segment uses the point test (contact iff `C ≤ 0`) with no division.
- [ ] **Contact point.** `contact = P0 + Throw.RH(D·f, 1000)` for every kind; the result struct is `(Kind, ReceiverKey, FPm, Point, Rank)`.
- [ ] Purity: `FirstContact` is `static`, allocates 0 B over 100 000 calls, uses no `float`, and reads nothing but its arguments.

---

## Implementation Notes

*Derived from ADR-0015 §4, Key Interfaces and GDD F4:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `FlightSweep.cs` (`static bool FirstContact(in Vec3Mm p0, in Vec3Mm p1, in ReceiverView v, FlightId id, out Contact c)`), `ReceiverView.cs` (readonly ref struct of spans: `ReadOnlySpan<VolumeReceiver> Mailboxes`, `ReadOnlySpan<SolidReceiver> Solids`, `ReadOnlySpan<SphereReceiver> Catchers`, `ReadOnlySpan<CylinderReceiver> Cylinders` (goons/teammates — empty at M-1a), `ReadOnlySpan<LaneReceiver> Belts` (empty), `MapBounds Bounds`, `IWorldQuery World`, `ReceiverMask Mask`), `Contact.cs`, `ReceiverKind.cs` (`enum ReceiverKind : byte { ArmedCatcher, Mailbox, Goon, Teammate, Belt, Solid, Ground, Water }` — values are the default ranks), `ReceiverRanks.cs` (the rank table from `ThrowBalance`; THROW-CV-05 guarantees a permutation).
- Interval maths comes from `production/epics/player-character/story-004-geometry-segment-intervals-heightfield-march-obb.md` (`SegmentIntervals`, `HeightfieldMarch`). Reuse them; do not copy.
- **Rounding conflict to resolve before coding (flagged):** ADR-0015 §2 says the heightfield march samples `Q_i` and the contact point use `Throw.RH`, but PC story 004 implements `HeightfieldMarch` with `Fix.MulDivRH` (half away from zero). They differ only for negative `D` components. The preview and host share the one function either way, so determinism is safe, but the GDD vectors (TC-F4-1 h) and ADR-0015 §2 table must hold. If `HeightfieldMarch` uses `MulDivRH`, add an optional rounding delegate/overload there (a small additive change, logged in this story's Last Updated note) rather than forking the march here. Do not edit PC story 004.
- `receiverKey` for the tie hash = the receiver's `EntityId.Value` (destination `EntityId` for volumes, solid index for solids, player slot for catchers); `TieHash = SplitMix.Mix64(((ulong)flightId.Value << 32) | receiverKey)` (`production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`). Never compare `FlightId` numerically.
- `ReceiverMask` lets the preview request "static receivers only" (mailbox volumes, terrain, water, solids, map edges, belts) while the host requests everything; one code path, one flag.
- Water: the water plane is y = 0 over tiles where `IWorldQuery.TileFlags` has `Water`, but the water **authority** is `WaterDepthMm` (ADR-0026: the flag is a broad-phase hint only).
- Position integers are `Vec3Mm` (int mm); intermediates are `long`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: building the `ReceiverView` (4 m static grid over catch volumes, `QueryStaticSolids`, map-edge solids, dynamic receivers, the chest → origin probe, surface-below).
- Story 011: per-tick stepping, terminals and WorldItems. Story 023: the armed-catcher sphere gather and the F5 history contact (this story only defines the sphere interval path and the rank-0 slot).
- Goon cylinders and belt lanes are M-1b receivers (the spans exist and stay empty); teammate bonk is M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/FlightSweepTests.cs` (`[Trait("Id", "TC-F4-1")]`, `TC-T6-1`, `TC-T6-2`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; `production/epics/player-character/story-004-geometry-segment-intervals-heightfield-march-obb.md`; `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`IWorldQuery`, `WorldHeight`); `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`Bounds`, tile flags); `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md` (`SplitMix.Mix64`)
- Unlocks: Stories 003, 004, 011, 023
