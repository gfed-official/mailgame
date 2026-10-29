# Story 002: `FlightSweep.FirstContact` — F4 swept contact, receiver ranks, descending-entry mailbox rule and the `(f, rank, dist², Mix64)` winner

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + ~400 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-28 (Complete, PR #55: all 15 ACs met, Sonnet code review r2 APPROVED; full suites 5,602 per leg, 0 failed. Before that: readiness doc fixes, `/workspace/repos/logs/tc002-handoff.md`: manifest stamp re-checked against 2026-10-17.1 (ADR-0026 far-edge sampling, which the march follows); Evan's three defaults and the ADR-0015 §2/§4 errata, Approved by Evan 2026-10-28 7:24 AM PT (Mail Lead chat): "Approve all three throwing-catching 002 defaults and the ADR-0015 §2 and §4 errata: keep Fix.MulDivRH, an uncounted far-segment guard, and fixed receiver keys" (TD-179); "counted" dropped from Broad phase; Terrain and water vectors; TC-T6-2's last clause as two calls; a `ReceiverMask` AC; the 0 B clause advisory per E2; namespace, `Fix` restriction, story 023 Deferred, `IWorldQuery.Tile`)

> **Primitives story (2 of 3).** The one swept-segment test used by the host's `Throwing.Step`, the aim preview and (later) the catch prompt predictor. It works on a `ReceiverView` of spans handed to it; story 003 builds that view from the world and Mail.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 receivers and resolution order; F4 swept contact; Edge Cases "Simultaneous events", "Zero, max and out-of-range")
**Requirement**: `TR-throwing-005` (swept segment per tick, ≤ 90 ticks: slab, circle, cylinder, sphere, heightfield), `TR-throwing-006` (receiver resolution ordering), `TR-throwing-007` (broad-phase bound |R| ≤ 65 536 mm; static 4 m grid — the bound and reject are here, the grid is story 003), `TR-throwing-003` (the preview calls this same function)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F4-1 a/b/c/h, TC-T6-1, TC-T6-2, TC-E-1 b.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§4 the per-tick sweep, Key Interfaces `FlightSweep.FirstContact`, `ReceiverView`) — Accepted 2026-10-16 (primary); ADR-0013 §6 (`Sim/Geometry`: `SegmentIntervals`, `HeightfieldMarch`) and ADR-0026 (`IWorldQuery` heightfield and water) secondary — Accepted
**ADR Decision Summary**: Each tick a flight sweeps the segment `p(k−1) → p(k)` against every receiver in the view; each hit yields `f` in ‰ (`f = max(tEnter, 0)`). The winner is the minimum of `(f, rank, dist²(contact, receiver centre), TieHash)` with ranks armed catcher 0 · mailbox 1 · goon 2 · teammate 3 · belt 4 · solid 5 · ground 6 · water 7 and `TieHash = SplitMix.Mix64((flightId << 32) | receiverKey)`. Mailbox volumes count only on a **descending** segment (`P1.y < P0.y`); a flight already inside whose next segment descends gets `f = 0`. Quadratic interval tests run only after the |R| ≤ 65 536 mm reject. The contact point is `P0 + RH(D·f, 1000)`.
**ADR Version**: ADR-0015 2026-10-16; ADR-0013 2026-10-16; ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Goldens run on the `net10.0` and `net8.0` legs.

**Control Manifest Rules (Feature layer)**:
- Required: ties break on `(f, rank, dist², Mix64)`; `FlightId` is a hash input and is never compared numerically; quadratic tests run only after the broad-phase reject (|R| ≤ 65 536 mm) (ADR-0015, ADR-0004)
- Required: World solids come from `IWorldQuery.QueryStaticSolids` (story 003 gathers; this story only consumes the view); heightfield and water via `HeightfieldMarch` (ADR-0013 §6, ADR-0026 §5)
- Forbidden: a second ballistic or sweep implementation; a second world-solid grid (ADR-0015, ADR-0026)
- Guardrail: a segment test ≈ 40–100 ns, 0 B allocated (ADR-0015 Performance)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-F4-1, TC-T6-1, TC-T6-2, TC-E-1 b), scoped to this story. The goon (d, f, g) and catcher (e) vectors of TC-F4-1 are M-1b / story 023 and are not failed here:*

- [x] **TC-F4-1 (FX-Flat).** P0 (10250, 963, 10570) → P1 (10250, 837, 10114) against MB-A (catch volume x 9750–10750, y 600–1400, z 9500–10500): f = 154, contact (10250, 944, 10500), kind Mailbox.
- [x] **TC-F4-1 b.** From the TC-F4-1 P0 (10250, 963, 10570): D = (0, −126, −70) → f = 1000 hit; D = (0, −126, −69) → no hit (the exact `CeilDiv`/`FloorDiv` edge).
- [x] **TC-F4-1 c.** P0 (10250, 1000, 10200) inside the volume with D (0, −100, −50) → f = 0.
- [x] **TC-F4-1 h.** Heightfield march, n = 4, first sample at or below H at i = 2 with gaps 100 and −50 → f = 417.
- [x] **TC-T6-1 (parametrised).** Stub receivers at equal f = 500: rank decides for the 7 adjacent rank pairs (catcher over mailbox, mailbox over goon, goon over teammate, teammate over belt, belt over solid, solid over ground, ground over water). Equal rank → the smaller `dist²` to the receiver centre → the lower `Mix64(flightId << 32 | receiverKey)`: across 1 000 `FlightId`s two otherwise-identical receivers each win at least once.
- [x] **TC-T6-1 b.** An earlier f beats rank: a mailbox at f = 300 beats an armed-catcher stub at f = 400.
- [x] **TC-T6-2 (descending only).** MB-A: (10250, 1300, 10800) → (10250, 1200, 10200) lands (descending). The same segment with start y 1100 (rising) or y 1000 → 1000 (flat) does not. First qualifying volume wins, as two `FirstContact` calls on one view holding MB-A and MB-B (FX-Flat, GDD `throwing-catching.md` l.1132; MB-B is at z 16000, so the full flight is multi-tick stepping, story 011): a rising segment inside MB-B gives no hit, and a descending segment into MB-A returns kind Mailbox with MB-A's key.
- [x] **TC-E-1 b (sweep half).** An origin inside MB-A's volume: a descending first segment yields f = 0 at T+1; an upward one produces no hit.
- [x] **Terrain and water.** A segment that crosses terrain above dry land yields kind Ground at the `HeightfieldMarch` contact. The water plane is a slab against y = 0 where `TerrainHeightMm < 0`: the y = 0 crossing fraction is computed, and `WaterDepthMm` at the contact is the authority (`Tile(tx, tz).Water` is only a broad-phase hint, ADR-0026 §3); it yields Water. Water ranks 7 and Ground 6, so a shoreline tie goes to Ground. Vectors (a `WorldTablesBuilder` world): with flat terrain at −500 mm, P0 (5000, 300, 5000) → P1 (5000, −300, 5000) gives Water at f = 500, contact (5000, 0, 5000); over flat terrain at +100 mm the same segment gives Ground via `HeightfieldMarch`.
- [x] **Map edge.** A segment leaving `Bounds` yields kind Solid at the boundary crossing (the map's vertical sides are rank-5 solids, T6). `FirstContact` tests the four side planes of `v.Bounds` itself; story 003 only supplies the value.
- [x] **Broad phase.** Dynamic spans (sphere/cylinder candidates) are rejected by a bounding-sphere test against the segment before any quadratic form; a guard rejects |R| > 65 536 mm: `SegmentIntervals` returns `SegmentInterval.None`, the form is not evaluated, and `SimAssertCode.GeometryBroadPhase` (40) fires under `SIM_ASSERT` (no counter; `FirstContact` takes no counter sink — Evan 2026-10-28 7:24 AM PT, TD-179); an int64 property test with |R| = 65 536 and |D| up to 1 200 mm never overflows.
- [x] **A = 0.** A zero-length segment uses the point test (contact iff `C ≤ 0`) with no division.
- [x] **Contact point.** `contact = P0 + Throw.RH(D·f, 1000)` for every kind; the result struct is `(Kind, ReceiverKey, FPm, Point, Rank)`.
- [x] **ReceiverMask.** With `ReceiverMask.StaticOnly`, Catchers and Cylinders are skipped (belts stay: they are static receivers in the preview's view, ADR-0015 §8, GDD l.167), and the static results equal the `All` result on the same view. *(Corrected 2026-10-28: the readiness handoff listed Belts among the skipped spans, contradicting ADR-0015 §8 and this story's ReceiverMask note.)*
- [x] Purity: `FirstContact` is `static`, uses no `float`, and reads nothing but its arguments (an architecture test: no `float`/`double` token and no static fields in `FlightSweep.cs`); it allocates 0 B over 100 000 calls, measured by the `GC.GetAllocatedBytesForCurrentThread()` delta — **advisory, not blocking, until M0 (ADR-0015 erratum E2)**.

Coverage target: every AC bullet maps to at least one `[Trait("Id", …)]` test.

---

## Implementation Notes

*Derived from ADR-0015 §4, Key Interfaces and GDD F4:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `FlightSweep.cs` (`static bool FirstContact(in Vec3Mm p0, in Vec3Mm p1, in ReceiverView v, FlightId id, out Contact c)`), `ReceiverView.cs` (readonly ref struct of spans: `ReadOnlySpan<VolumeReceiver> Mailboxes`, `ReadOnlySpan<SolidReceiver> Solids`, `ReadOnlySpan<SphereReceiver> Catchers`, `ReadOnlySpan<CylinderReceiver> Cylinders` (goons/teammates — empty at M-1a), `ReadOnlySpan<LaneReceiver> Belts` (empty), `MapBounds Bounds`, `IWorldQuery World`, `ReceiverMask Mask`), `Contact.cs`, `ReceiverKind.cs` (`enum ReceiverKind : byte { ArmedCatcher, Mailbox, Goon, Teammate, Belt, Solid, Ground, Water }` — values are the default ranks), `ReceiverRanks.cs` (the rank table from `ThrowBalance`; THROW-CV-05 guarantees a permutation).
- Interval maths comes from `production/epics/player-character/story-004-geometry-segment-intervals-heightfield-march-obb.md` (`SegmentIntervals`, `HeightfieldMarch`). Reuse them; do not copy.
- **Namespace:** `PerformativeMail.Sim.ThrowingCatching` (ADR-0015 erratum 2026-10-20; the folder stays `src/PerformativeMail.Sim/Throwing/`); tests in `PerformativeMail.Sim.Tests.Unit.ThrowingCatching`. `tests/unit/architecture/ThrowingRulesTests.cs:126` forbids Throwing code from calling any `Fix` helper other than `FloorDiv` and `ISqrt`, so all rounding goes through `Throw.RH`, `SegmentIntervals` and `HeightfieldMarch`.
- **Rounding (resolved, Evan 2026-10-28 7:24 AM PT, TD-179; ADR-0015 §2 erratum 2026-10-28):** the heightfield samples `Q_i` stay on `Fix.MulDivRH` inside the shipped `HeightfieldMarch` (ADR-0013 §6; player-character 004 pins it, focus occlusion shares it); the contact point stays `Throw.RH`. No overload, no delegate, no change to `HeightfieldMarch`; TC-F4-1 h is unaffected. Do not edit PC story 004.
- `receiverKey` (Evan 2026-10-28 7:24 AM PT, TD-179; ADR-0015 §4 erratum 2026-10-28): every receiver struct carries `uint Key`, which `FirstContact` hashes verbatim: `TieHash = SplitMix.Mix64(((ulong)flightId.Value << 32) | Key)` (`production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`). The view builder (story 003) assigns the keys: volumes the destination `EntityId.Value`; world solids `StaticSolid.Owner.Value`, or `0x8000_0000u | Index` when `Owner` is None; map sides `0xC000_0000u | side` (0 −X, 1 +X, 2 −Z, 3 +Z); catchers the player's `EntityId.Value` (story 023, Deferred); terrain and water singletons with Key 0. Receivers without a centre (map sides, terrain, water) use `dist² = 0`. Never compare `FlightId` numerically.
- `ReceiverMask` lets the preview request "static receivers only" (mailbox volumes, terrain, water, solids, map edges, belts) while the host requests everything; one code path, one flag.
- Water: the water plane is y = 0 over tiles where `IWorldQuery.Tile(tx, tz)` has `Water`, but the water **authority** is `WaterDepthMm` (ADR-0026 §3: the flag is a broad-phase hint only).
- Position integers are `Vec3Mm` (int mm); intermediates are `long`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: building the `ReceiverView` (4 m static grid over catch volumes, `QueryStaticSolids`, dynamic receivers, the chest → origin probe, surface-below). Story 003 supplies `Bounds` in the view; the four side-plane tests live in `FirstContact` (this story).
- Story 011: per-tick stepping, terminals and WorldItems. Story 023 (Deferred, Evan 2026-10-23; ADR-0015 erratum E9 moves the catcher rows to M0): the armed-catcher sphere gather and the F5 history contact (this story only defines the sphere interval path and the rank-0 slot).
- Goon cylinders and belt lanes are M-1b receivers (the spans exist and stay empty); teammate bonk is M0.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/FlightSweepTests.cs` (`[Trait("Id", "TC-F4-1")]`, `TC-T6-1`, `TC-T6-2`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — `tests/unit/throwing/FlightSweepTests.cs` (18 test methods, 41 cases); passing; full suites run 2026-10-28 at story close: 5,602 tests per leg (5,561 + 41), 0 failed — net10.0 Debug 5,538 passed / 64 skipped, net8.0 Release 5,595 passed / 7 skipped; CI on the pushed head is recorded in the PR #55 body. The purity 0 B probe is advisory until M0 (E2).

---

## Dependencies

- Depends on: Story 001; `production/epics/player-character/story-004-geometry-segment-intervals-heightfield-march-obb.md`; `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`IWorldQuery`, `WorldHeight`); `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`Bounds`, tile flags); `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md` (`SplitMix.Mix64`)
- Unlocks: Stories 003, 004, 011, 023
