# Story 004: BalanceKey registry, BalanceTable and SimSettings

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD9, CD11 "design clamps are data"); `design/gdd/simulation-core.md` (Tuning Knobs, F8: owner of the `sim.*` keys)
**Requirement**: `TR-content-013`
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§4 Balance keys) (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4 injectable list: "the system's own settings record")
**ADR Decision Summary**:
- Balance keys are addressed `owner.key`.
- A code-side `BalanceKey` registry declares each key's owner, unit and **hard range** (correctness). The data file carries the **safe range** (taste); a value outside it is a Warning.
- Every balance key is Sim content and is hashed.
- `SimSettings` keys live in `balance/sim.jsonc`. Simulation Core owns them.
- `SimSettings` and `BalanceTable` are members of `ContentSet`.

**ADR Version**: ADR-0010 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: balance keys are addressed `owner.key`. A code-side `BalanceKey` registry declares owner, unit and hard range. The data file carries the safe range. (ADR-0010 §4)
- Required: collections are pre-sized from `SimSettings`. New collections must be sized from `SimSettings` at build. (ADR-0006 §8)
- Required: integer units only. No `float`/`double`/`decimal`/`Half` in authoritative state. (ADR-0006 §1)
- Forbidden: string lookups in per-tick code. Lookup is by registry handle. (ADR-0004 §6, CD5.4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] Building a `BalanceTable` reports exactly one CV-GEN-13 in each of these cases (CDD-SCHEMA-5):
  - `player.rangeMm` supplied twice, from two sources;
  - an unregistered key;
  - a value outside the registry hard range;
  - a key supplied under an owner/file whose prefix is not the key's registry owner.
- [x] Building a `BalanceTable` with a value outside its `safe` range reports CV-GEN-20 (Warning), and the table still builds. (CDD-SCHEMA-5)
- [x] A registered key with no supplied value reports CV-GEN-05 naming `owner.key`. There is no implicit default. (Validation edge case)
- [x] `BalanceTable.Get(BalanceKey)` is an O(1) array read by registry handle that allocates 0 B. The registry enumerates keys in ordinal `owner.key` order; story 013 hashes in that order.
- [x] `SimSettings` is an immutable composite record built from the `sim.*` keys. Its `Core` member is simulation-core's `SimCoreSettings` sub-record (`production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md`: `OpQueueCapacity` 4096, `SystemOpReserve` 64, `MaxOpsDrainedPerTick` 64, `EventLogCapacity` 4096, `EventLogReserve` 256, `MaxCatchUpTicksPerFrame` 4, `CheckRingCapacity` 256), filled from registry rows. It also holds `warmupTicks` 300 and a `Stats` sub-record (`modifierPoolPerTeam` 128, `modifierPoolPerPlayer` 64, PROPOSED). Later owners add their own capacity sub-records, e.g. Inventory's. `TickRateHz` (30) and `maxEventReaders` (8) stay code constants.

---

## Implementation Notes

*Derived from ADR-0010 §4 and ADR-0003 §4:*

- **Location:** `src/PerformativeMail.Sim/Content/Balance/` holds `BalanceKey`, `BalanceKeys` (the registry), `BalanceTable` and the typed per-owner records. `SimSettings` goes in `src/PerformativeMail.Sim/Content/SimSettings.cs`.
- **Registry.** `BalanceKey` is a small struct handle (an index into the registry table). `BalanceKeys.Sim.OpQueueCapacity`-style `static readonly` handles are immutable values, so they satisfy the no-statics rule. Each registry row is `{ owner, key, unit (FieldSpec unit enum from story 016, or a local placeholder until then), hardMin, hardMax }`.
- **Rows registered here:** only `sim.*`. Story 008 registers the `player.*`, `inventory.*`, `mail.*`, `throw.*` and `delivery.*` rows together with their M-1a values.
- **Key names.** Owner GDDs spell some keys with dots, e.g. `throwSpeed.light` and `weightPoints.light`. Register them verbatim under the owner prefix: `throw.throwSpeed.light`. The first dot separates the owner. Flag any rename to the GDD re-review; do not invent names. (Epic Risk 1)
- **Values** are `long` in the key's stored unit (ms, mm, permille, milli-points, cents; CD4). Story 016 converts JSONC decimals to these units. Here the table is fed already-scaled integers. The test feed and `BuiltInContent` both supply integers.
- **Typed owner records** (implementation decision, logged): each owning system receives its balance as an immutable record built once from the table (`SimSettings` for `sim.*`; others in story 008). This is the "system's own settings record" row of ADR-0003 §4's injectable list. Systems cache it in `readonly` fields (ADR-0010 §4).
- **CV-SIM-01…04** (SC-SET-1: power-of-two capacities, reserve ≤ capacity/4, etc.) are tagged `[M-1b]` in simulation-core.md and are not seeded here. Simulation-core's `SimWorldBuilder` `SimBuildException` remains the M-1a guard. Register the rules when SC-SET-1 lands.

---

## Out of Scope

- Story 008: the `player`/`inventory`/`mail`/`throw`/`delivery` key rows and values.
- Story 019: parsing `content/balance/*.jsonc` (owner taken from the file name; `safe: [min, max]` annotation).
- Story 013: hashing the table.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/BalanceTableTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/BalanceTableTests.cs` (BalanceTableTests + BalanceTableAllocationTests; 47 tests) + CV-GEN-05/13/20 fixtures in `tests/unit/content/ContentRuleTestKit.cs`

---

## Dependencies

- Depends on: Story 003 (diagnostics, CV-GEN-05/13/20 ids) Also `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (`SimCoreSettings`).
- Unlocks: Story 006, Story 008, Story 019

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (no deferred items; AC4 0 B allocation verified on Release legs — Debug skips per TD-008)
**Deviations**: ADVISORY (accepted) — (1) `BalanceKeys.Sim.X` static handles replaced by `BalanceKeys.Sim.Resolve(registry)` → `SimKeys` (handles carry a registry stamp; foreign/None handles fail loudly) because handle positions shift when story 008 adds owners and ADR-0003 §4 allows no non-primitive statics; (2) hard ranges are implementer-chosen correctness bounds; (3) `catchUpGuardMs` not registered (M0); stats key names PROPOSED; (4) `BalanceUnit` is a local placeholder until story 016; (5) touched story-001 `DefIndexTests` assert-code count (4→6) for new codes 2004/2005. Residual advisories logged as TD-041.
**Test Evidence**: Logic: `tests/unit/content/BalanceTableTests.cs` (47/47 pass); four legs green at 1441 tests/leg at implementer run (Debug 8 skipped = TD-008); orchestrator re-run Release: content green, 1 unrelated in-flight failure in simulation-core `EventLogTests` (story 015 track).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS → revised → APPROVE; QL-TEST-COVERAGE GAPS (advisory, TD-041); godot-csharp-specialist no BLOCKING (advisories fixed).
