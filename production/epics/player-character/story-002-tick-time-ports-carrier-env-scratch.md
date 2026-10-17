# Story 002: Tick-time input ports, `CarrierEnv`, `CarrierInput`, `CarrierScratch` and the solver counters

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~250 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/player-character-movement.md` (P2 "Tick-time inputs only"; P4 sub-step bounds; PCM-PERF-1 probe counters)
**Requirement**: `TR-movement-004` (tick-time inputs only via `IRemoteCapsuleSource` / `ISupportTransformSource`), `TR-movement-001` (pure, allocation-free solver surface)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P2-1 (replay inputs), PCM-PERF-1 counters.)*

**ADR Governing Implementation**: ADR-0013: Carrier kinematic solver & fixed-point geometry (§7 tick-time input ports, §1, §4 counters) — Accepted 2026-10-16
**ADR Decision Summary**: `OwnedCarrier.Step(ref CarrierState, in CarrierInput, in CarrierEnv, ref CarrierScratch)` reads only its arguments. Other players and supports reach it only through `IRemoteCapsuleSource` (latest *received* snapshot) and `ISupportTransformSource`, never render-time poses. At M-1a the remote-capsule source is empty (solo) and the support source returns no vehicles and no belts. Candidate buffers are fixed-capacity `CarrierScratch` fields; overflow is counted, never grown.
**ADR Version**: ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. `CarrierEnv` is a `ref struct` (it holds `ReadOnlySpan`s / interface refs); keep it out of lambdas and async code.

**Control Manifest Rules (Feature layer)**:
- Required: other players and supports are read only through `IRemoteCapsuleSource` / `ISupportTransformSource` at tick time; render-time poses never enter the Sim (ADR-0013 §7)
- Required: sub-steps ≤ 300 mm and ≤ 4 per tick, ≤ 3 slides per sub-step (counted); candidate buffers are fixed-capacity `CarrierScratch` fields, overflow counted (ADR-0013)
- Forbidden: statics (other than `ReadOnlySpan` literal tables), clocks, RNG, allocation in the solver (ADR-0013, ADR-0006)

---

## Acceptance Criteria

*From ADR-0013 §7 / §4 and GDD PCM-P2-1 / PCM-PERF-1, scoped to this story:*

- [x] The ports exist exactly as ADR-0013 §7 declares them: `IRemoteCapsuleSource { ReadOnlySpan<RemoteCapsule> Capsules { get; } }`, `RemoteCapsule { EntityId Id; PlayerSlot Slot; int X, Y, Z; bool Solid }`, `ISupportTransformSource { ReadOnlySpan<VehiclePose> Vehicles { get; }; bool TryGetBeltVelocity(int tileX, int tileZ, out int vx, out int vz, out uint segmentId) }`, `VehiclePose` (id, position mm, yaw BAM, half extents, roof y).
- [x] M-1a implementations `EmptyRemoteCapsuleSource` and `EmptySupportTransformSource` return an empty span / `false`; they are the defaults in `CarrierEnv` for the `M1aGreybox` profile.
- [x] `CarrierInput` (MoveX/MoveY Q15 on the unit disc, `LookYaw` BAM, `SprintHeld`, `JumpPressed` edge this tick, `Aiming`, `ContextFlags` (Panel → walk), `HostAuthored`, `SpeedPermille`, `BuffAddPm`, `ExtraJumps`) and `CarrierEnv` (`IWorldQuery World`, `IRemoteCapsuleSource Remotes`, `ISupportTransformSource Supports`, `in PlayerSettings Settings`, `EntityId Self`) exist with `unmanaged` / readonly layouts and **no** `float`, `double`, `SimWorld` or Godot-typed member (reflection test).
- [x] `CarrierScratch` has fixed-capacity candidate buffers (`maxCarrierCandidates` from settings, default 64 static-solid indices plus construct boxes and `VehiclePose` refs) and counters `SubStepsMaxThisTick`, `SlideIterationsMax`, `StepClamped`, `Depenetrations`, `CandidateOverflow`. Adding a 65th candidate leaves the buffer length unchanged and increments `CandidateOverflow` (never grows, never throws).
- [x] A `CarrierCounterFlush` helper adds the scratch counters into `SimCounters` keys `carrierSubSteps` (max), `carrierSlideIterations` (max), `carrierStepClamped`, `carrierDepenetrations`, `carrierCandidateOverflow` after a step; the always-on `SimCounters` rules of sim-core story 007 are respected (no allocation).
- [x] Scripted test doubles `ScriptedRemoteCapsuleSource` / `ScriptedSupportSource` (in `tests/`) can feed a per-tick script so Story 010's replay is bit-identical; the doubles copy their spans into a fixed backing array on `SetTick(n)`.
- [x] Constructing and clearing `CarrierScratch` and calling the flush 10 000 times allocates 0 B (`GC.GetAllocatedBytesForCurrentThread`).

---

## Implementation Notes

*Derived from ADR-0013 §1, §4, §7 and Implementation Guidelines:*

- Files in `src/PerformativeMail.Sim/Players/`: `IRemoteCapsuleSource.cs`, `ISupportTransformSource.cs`, `RemoteCapsule.cs`, `VehiclePose.cs`, `CarrierInput.cs`, `CarrierEnv.cs`, `CarrierScratch.cs`, `CarrierCounterFlush.cs`, `EmptySources.cs`.
- On a client the real sources read the ClientLayer mirror's latest received transform batch; on the host they read the T−1 ingest latch (sim-core story 021). Both arrive at M0; this story defines the seam only. Never expose interpolated poses.
- The pure solver cannot write `SimCounters` (no statics, no injected services): it writes the scratch fields, and the *caller* (`CarrierStep` in story 020, `PlayersStep` in story 019) calls `CarrierCounterFlush`.
- `bool` inside blittable structs: ADR-0014's engine note says use explicit `byte` flags in payload/latch structs; `RemoteCapsule.Solid` may stay `bool` (not serialized) but keep `unmanaged`.
- Keep `CarrierScratch` ≈ 2 KiB per stepping context; allocate once per context at build, not per tick.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005–010: the step itself (`OwnedCarrier.Step`).
- Real remote-capsule / vehicle / belt sources: M0 (transform replication), M-1b (Belt Logistics), M1 (Vehicles).
- Player push F6 and vehicle roofs P7 (out of M-1a scope; sources stay empty).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/CarrierPortsTests.cs`, `tests/unit/architecture/CarrierPurityTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/player/CarrierPortsTests.cs (+ ScriptedSources.cs), tests/unit/architecture/CarrierPurityTests.cs

---

## Dependencies

- Depends on: Story 001 (`PlayerSettings`, `CarrierState`), `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (`SimCounters`), `production/epics/world-stub/story-001-iworldquery-height-field-kernel.md` (`IWorldQuery`)
- Unlocks: Stories 005–010 (all solver stories), 019, 020

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 7/7 passing — all covered by automated tests (CarrierPortsTests, CarrierPurityTests, SimCountersTests pins)
**Deviations** (advisory, TD-067): `CarrierEnv` is a `readonly ref struct` holding interface refs (not unmanaged); empty sources are instances owned by `PlayersSystem` (`CreateCarrierEnv(self)` / `CarrierEnv.ForM1a`), no statics; `CarrierScratch` inline buffers 64 solid indices / 32 boxes / 8 vehicles (~0.95 KiB), limit = min(settings, capacity); `maxCarrierCandidates` knob hard max lowered 4096 → 64; `CarrierCounterFlush` max for subSteps/slideIterations (read+Set), add for the rest, then zeroes scratch counters; CounterId 31–35 appended; `VehiclePose` field set chosen from ADR-0013 §7.
**Test Evidence**: Logic — tests/unit/player/CarrierPortsTests.cs, tests/unit/architecture/CarrierPurityTests.cs; 2 688 tests/leg green on net10/net8 × Debug/Release (Debug 17 skipped, optimised-only alloc guards); re-verified net8 Debug 2 704/0 failed after other runners' additions.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (incl. godot-csharp .NET secondary), QL-TEST-COVERAGE GAPS (advisory; gaps 1, 3–6 + LP 1–4/6 fixed in-story); rest TD-067.
