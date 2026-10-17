# Story 006: `M1aStreet` — the hand-authored 10-house street — and the `WorldTablesFixture` consumer fixtures (FX-Dry, FX-Beach, FX-Ramp, FX-Deep, FX-Pad)

> **Epic**: World Stub — Hand-Authored Street (M-1a)
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines authoring + fixture helper, ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: none for World Generation. Consumers: `design/gdd/mail-destinations.md` R3 and Dependencies (hand-authored street of 10 houses); `design/gdd/player-character-movement.md` fixture table (FX-Dry, FX-Beach, FX-Ramp, FX-Deep, FX-Pad), PCM-P4-1a, PCM-F7-1, PCM-P17-2, OQ23 (capsule vs doorway, measured on this street); `design/gdd/throwing-catching.md` T5/T11.
**Requirement**: `TR-movement-005` (height field the street is authored in), `TR-movement-018` (beach shallow and deep tiles), `TR-mail-006` (the 10-address source)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0026: World Query Contract (`IWorldQuery`) and the M-1a Street Stub (primary, §8)
**ADR Decision Summary**: `M1aStreet.Author(WorldTablesBuilder b)` is a deterministic, code-authored data table in the style of `BuiltInContent`. It has one district (1) and one street (1); 10 houses numbered 1–10 on alternating sides, with mailbox spacing within Mail R3 (6 m same side, 3 m opposite stagger); one Post Office with its pad; a beach strip at one end with at least one shallow tile (0 < depth < 900 mm) and one deep tile (≥ 900 mm); and one authored ramp reproducing FX-Ramp. The map is ≤ 128 × 64 tiles. Tests get a `WorldTablesFixture` helper over the same builder that builds FX-Dry, FX-Beach, FX-Ramp, FX-Deep and FX-Pad without the street.
**ADR Version**: ADR-0026 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None. Terrain and house views are `godot-host-shell` / ADR-0019's, built from these tables.

**Control Manifest Rules (Core layer + Global)**:
- Required: M-1a content is `M1aStreet.Author(builder)` (code-authored); tests use `WorldTablesFixture` over the same builder (ADR-0026 §1, §8)
- Required: replays record the world source identity (`M1aStreet` version constant) (ADR-0026 §9)
- Required: no invented world design beyond what the consumer GDDs require (ADR-0026 Constraints)
- Forbidden: statics other than `ReadOnlySpan` literal tables; floats; culture-sensitive formatting (ADR-0006)

---

## Acceptance Criteria

*From ADR-0026 §8 and Validation Criteria, scoped to this story:*

- [x] **AC-1 Street contents.** `M1aStreet.Author` + `Build()` succeeds (all story 002–005 validations pass) and yields: a map ≤ 128 × 64 tiles; one street (district 1, street 1) with 10 lots numbered 1–10 in ascending `AddressId`, with odd and even numbers on opposite sides; one `House` solid and one `Roof` solid per lot plus one `PostOffice` solid; one Post Office whose 8 pad slots all validate (FX-Pad holds on the street).
- [x] **AC-2 Beach and ramp.** The street contains a point with 0 < `WaterDepthMm` < 900 (shallow), a point with `WaterDepthMm` ≥ 900 (deep) and one authored tile rising exactly 1400 mm over 2000 mm (700‰, `Walkable`). Tests locate them through `M1aStreet` named constants, not magic coordinates.
- [x] **AC-3 Determinism.** Two independent `M1aStreet.Author` + `Build()` runs produce element-wise identical corner, flag, solid, lot and Post Office tables. `M1aStreet.Version` is a public `const` (starting at 1).
- [x] **AC-4 Consumer fixtures.** `WorldTablesFixture` builds, without the street: FX-Dry (flat +1000 mm, map 0…128,000 mm on x and z); FX-Beach (one tile with h00 = h10 = −1000, h01 = h11 = −200, so `TerrainHeightMm` at (500, 1000) = −600); FX-Ramp(riseMm) (one tile rising by `riseMm` over 2000 mm along +X; 1400 → `Walkable`, 1404 → not); FX-Deep (flat −2000 mm, depth 2000); FX-Pad (pad centre at (0, 0) on flat dry ground, 8 valid slots at 1200 mm). Each fixture's values match the PC GDD fixture table when read through `IWorldQuery`.

---

## Implementation Notes

*Derived from ADR-0026 §8 and the consumer fixture table:*

- **Files:** `src/PerformativeMail.Sim/World/M1aStreet.cs` (`public static class M1aStreet { public const int Version = 1; public static void Author(WorldTablesBuilder b); }` plus named public constants for the beach and ramp probe points). `tests/PerformativeMail.Sim.Tests/World/WorldTablesFixture.cs` (test helper: `static WorldTables Dry()`, `Beach()`, `Ramp(int riseMm)`, `Deep()`, `Pad()`). Each fixture also needs a test `EntityIds` and the `players` thresholds; put those in a shared fixture-settings helper.
- **Layout (greybox; exact geometry is this story's, ADR-0026 §8):** a straight street along X; houses 1, 3, 5, 7, 9 on one side and 2, 4, 6, 8, 10 on the other; same-side mailbox pitch ≥ 6 m (e.g. 12 m house plots); opposite-side stagger ≥ 3 m (offset the even side by half a plot). The Post Office and pad sit at one end, the beach strip at the other. Keep the house AABBs inside their lots and the anchors on the street side. Leave a doorway-sized gap, so PC OQ23 (capsule vs doorway) can be measured at the greybox. That is a manual observation for the player-character epic, not an AC here.
- **Authoring as a data table:** express lots, solids and the corner-height bands as `ReadOnlySpan<int>`/`ReadOnlySpan<…>` literals or small loops over constants. No RNG, no file IO. The same source code yields the same tables (T1), and the version constant changes whenever the authored data changes, so recorded replays notice (story 007).
- **Display strings:** address display text and the street name go into the world string table of story 004 (e.g. "1 <street>"). The street's name is a placeholder until the narrative or level team names it. Mark it `// PLACEHOLDER (no World Generation GDD)`.
- **FX-Dry map extent:** 0…128,000 mm is 64 tiles at 2 m; the fixture uses a 64 × 64 grid.
- **Dependency note:** the `players` thresholds (700‰ slope, 900 mm swim enter) come from content-data's `players` `SimSettings` record. If `production/epics/content-data/story-004-balance-registry-simsettings.md` is done, read the built-in values. Otherwise pass the PC GDD defaults through the shared fixture-settings helper, with one source in tests.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007: the `World` dump section, composition wiring and recording `M1aStreet.Version` in the replay header.
- godot-host-shell / ADR-0019: terrain, house and mailbox views.
- M1 (ADR-0026 §10): seeded generation, districts, the routing graph, Lost Parcel points.
- player-character / throwing-catching: the behaviour tests that use these fixtures (PCM-P4-1a, PCM-F7-1, PCM-P17-2, Throwing landing). This story only proves the fixtures' world values.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/world/M1aStreetTests.cs` and `tests/unit/world/WorldTablesFixtureTests.cs` — must exist and pass

**Status**: [x] Created and passing — `tests/unit/world/M1aStreetTests.cs` (20 tests), `tests/unit/world/WorldTablesFixtureTests.cs` (17 cases incl. theory rows); helper `tests/unit/world/WorldTablesFixture.cs`

---

## Dependencies

- Depends on: Stories 001–005
- Unlocks: Story 007; mail-destinations registry tests on the real street; player-character and throwing-catching fixture-based tests

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (none deferred)
**Deviations** (all ADVISORY, accepted; carried to TD-060):
- AC-1: the street builds via `Build(EntityIds, 700, 900)` + `SetPostOffice`, not the literal `Build()` — `Build()` rejects a Post Office (code 43; story 005 / TD-058 item 1). AC wording to be amended with the ADR-0026 §7 amendment.
- AC-4: FX-Pad centre is (8000, 1000, 8000), not (0, 0) — a slot at −1200 would be off the corner grid; slot offsets from the centre are the GDD ring. PCM-P17-2 must read `PostOffice.PadCentre`; the PC GDD fixture table should say "pad-relative".
- FX-Ramp is an 8×1 strip; the street ramp is a two-corner ridge at the north edge whose warped row-1 tiles (20,1)/(21,1) are not Walkable (pinned by test; 26 m off the street).
- Walkability limits pinned in `WorldTablesFixture` (700‰ / 900 mm); `swimEnterMm` drift-guarded against `BuiltInContent`; no `slopeMaxPm` content key exists yet (TD-060 item 1).
- Street name "Placeholder Lane" (PLACEHOLDER); Post Office door y = ground, intake y = ground + 1000 — greybox guesses, now named constants, flagged for review.
- Test files live under `tests/unit/world/` (per Test Evidence), not the Implementation Notes' `tests/PerformativeMail.Sim.Tests/World/`.
**Test Evidence**: Logic — `tests/unit/world/M1aStreetTests.cs`, `tests/unit/world/WorldTablesFixtureTests.cs`; the story's world + pad tests ran 74/74 on all four legs (net10/net8 × Debug/Release, /tmp rsync copy). The full suite had 9 foreign Content failures from another runner's in-flight content-data edits (none in this story's files).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (7 advisory; named constants, `AuthorLot` split and ridge test applied in-story); QL-TEST-COVERAGE GAPS → gaps 1–4 fixed in-story, gap 5 (Version golden hash) → story 007 / TD-060.
