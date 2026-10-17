# Story 007: `WorldTables` in the world — `World` dump section, composition wiring (`Constructed`), replay world-source identity

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Estimate**: S–M (~0.5–1 day; ~200 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (T1 determinism; R19 destination ids at world build); ADR-0026 §9.
**Requirement**: `TR-sim-033` (T1: identical canonical dump hash per tick given the same inputs and logs, with the `World` section included)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary, §1, §9); ADR-0006 (canonical dump, replay header); ADR-0003 (constructor injection, composition root)
**ADR Decision Summary**: `WorldTables` is an `ISimStateContributor` (section `World`). It writes corner heights, flags, solids, lots, the Post Office and bounds little-endian, field by field, in index order. It is written once per dump; because it is immutable it may cache its bytes after build. Replays record the world source identity (M-1a: the `M1aStreet` version constant). The tables are built by `WorldTablesBuilder` while the world is `Constructed` (destination ids allocated then) and injected as `IWorldQuery` into consumers by constructor. The same instance may be passed to the `ClientLayer` of the same process: it is not `SimWorld` state.
**ADR Version**: ADR-0026 2026-10-16; ADR-0006 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Core + Foundation + Global)**:
- Required: `WorldTables` is an `ISimStateContributor` (section `World`); replays record the world source identity (`M1aStreet` version constant) (ADR-0026 §9)
- Required: `ISimStateContributor` with a unique, never-reused `SectionId`; fields one by one, LE; collections `u32 count` + ascending order; no strings (ADR-0006 §4; `production/epics/simulation-core/story-019-canonical-state-dump.md`)
- Required: services reach systems by constructor injection from the composition root; no system holds `SimWorld` (ADR-0003)
- Forbidden: a struct blit or `MemoryMarshal.AsBytes` over `LotRecord`/`StaticSolid` for the dump; writing strings into the dump (`StringId` as `ns u8/u16 + index u16`)

---

## Acceptance Criteria

*From ADR-0026 §9 and Validation Criteria, scoped to this story:*

- [ ] **AC-1 `World` section.** With `M1aStreet` bound, a dump contains one `World` section (new `SectionIds.World` constant, never reused). Its payload is, in order: bounds (4 × i64); `TilesX`, `TilesZ` (i32); corner heights (u32 count + i32 each, row-major); flags (u32 count + u8 each); solids (u32 count + per solid `Index u16, Owner u32, Box 6 × i32 (AabbMm is int32 mm, like Vec3Mm), Kind u8`); lots (u32 count + every `LotRecord` field in declaration order); the Post Office record. A test decodes the section back and compares it field by field with `IWorldQuery`.
- [ ] **AC-2 T1 with the World section.** Two worlds built from `M1aStreet` and run for 300 identical ticks have identical dump SHA-256 every tick. Changing one corner height, one solid box coordinate or one lot's `TruckStopPoint` in a test-only authored variant changes the hash.
- [ ] **AC-3 Composition and ids.** The composition root builds `WorldTables` while the world is `Constructed`, before `Freeze()`. After `M1aStreet`, the dump's Sim Core section shows the `Destination` (0x07) class counter advanced by exactly 10. `IWorldQuery` reaches a consumer only through its constructor (the composition reflection test of `production/epics/simulation-core/story-025-composition-root-reflection-test.md` passes with World bound).
- [ ] **AC-4 Replay world identity.** A recording made by `production/epics/simulation-core/story-026-inmemory-recorder-replay.md`'s recorder carries the world source identity (`M1aStreet`, `Version`). Replaying it against the same source reproduces the per-tick hashes. A recording whose stored version differs from `M1aStreet.Version` is refused with a distinct reason before tick 0, not replayed into a hash mismatch.

---

## Implementation Notes

*Derived from ADR-0026 §1, §9 and ADR-0006:*

- `WorldTables : IWorldQuery, ISimStateContributor`. `Write(ref CanonicalWriter w)` writes the cached payload bytes (`w.WriteBytes(_dumpCache)`), produced once at the end of `Build()` with the same field order. A test asserts that the cache equals a fresh field-by-field write (guards against stale caches).
- Bind order: World is a Core-layer contributor. Register it with the builder before the Feature systems that consume it, so the section order is stable (sections appear in registration order, sim-core story 019).
- **Composition:** add a `World` step to the M-1a composition profile (`production/epics/simulation-core/story-025-composition-root-reflection-test.md`). Get the `EntityIds` from the builder, run `M1aStreet.Author` and `Build(ids, playersThresholds)`, then hand the resulting `IWorldQuery` to the constructors of the systems that need it. At M-1a nothing consumes it yet inside sim-core, so the test consumer is a stub system that takes `IWorldQuery` in its constructor. Feature epics add the real consumers.
- **Replay header:** ADR-0006 §7 defines the recorder header. Add a `worldSource` field (u16 kind: 1 = `M1aStreet`; u32 version). If the header has no slot for it, extend it with sim-core's recorder story owner. **Do not edit that story file** (another agent owns simulation-core): add the field from this story's code and note it in the story's completion notes. "Refused with a distinct reason" means a typed result or reason from the replay entry point, never an exception inside a tick.
- `StringId` in lots dumps as its namespace byte + `u16` index. Display text never enters the dump.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- How tables reach remote clients (regenerate vs JoinState tables): the M1 revision of ADR-0026 (§10), ADR-0022.
- Mail's registry built from `Lots` (mail-destinations epic).
- SC-DET-1 harness itself (`production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`). This story reuses it.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/world/WorldDumpReplayTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006; `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-025-composition-root-reflection-test.md`, `production/epics/simulation-core/story-026-inmemory-recorder-replay.md`, `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`
- Unlocks: mail-destinations (registry from `Lots` inside a composed world); player-character and throwing-catching composition; world-stub epic Definition of Done
