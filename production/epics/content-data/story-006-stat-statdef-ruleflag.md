# Story 006: Stat enum, StatDef table (M-1a rows) and RuleFlag

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5–1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD11, CD12, CD18; "Which clamp bounds are data")
**Requirement**: `TR-content-014`, `TR-content-018` (enum half)
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§7 Identity)
**ADR Decision Summary**:
- `Stat` is a closed enum (PascalCase + unit suffix; generated snake_case data spelling). Each member has a code-side `StatDef` row: unit, scope (team/personal/both), qualifier kind, base source, allowed ops, Set resolve (min-wins/max-wins), rounding (HalfUp/Floor/Ceil), min and max.
- Design clamps are balance keys; only correctness bounds stay in code.
- `RuleFlag` is a closed enum for behaviour switches.

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: closed `Stat` enum with a code-side `StatDef` row. (ADR-0010 §7)
- Required (naming): `Stat` enum members are PascalCase + unit suffix. (ADR-0010 §7, Naming Conventions)
- Required: enums have explicit never-reused values and compile-time spelling tables. (ADR-0010 §2)
- Forbidden: non-immutable statics (the `StatDef` table is `static readonly` immutable, `[ImmutableTable]`). (ADR-0003 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] A snapshot test of the `StatDef` table matches the M-1a rows of CD12 in rows, units, scopes, qualifier kinds, base sources, ops, Set resolve, rounding and clamp sources. In particular, `ThrowRangePermille` is clamped 1000–1500 via `throw.rangeMinPermille`/`rangeMaxPermille`, and `MailMaxStack` 1–255 is a code correctness bound. The `Stat` ↔ snake_case map is bijective. (CDD-STAT-1)
- [x] Resolving `StatDef` bounds against a `BalanceTable` gives `min`/`max` per stat. When a stat's `min` > `max` after its balance-key bounds resolve, CV-GEN-15 is reported naming both keys. (CDD-STAT-1, last clause; F1 variables)
- [x] `RuleFlag` holds the 14 v1 flags of CD18, with explicit never-reused values and data spellings. It is covered by the same snapshot discipline as story 002. (CD18; enum half of TR-content-018)

---

## Implementation Notes

*Derived from ADR-0010 §7 and GDD CD11/CD12/CD18:*

- **Location:** `src/PerformativeMail.Sim/Content/Stats/` holds `Stat.cs`, `StatDef.cs`, `StatDefs.cs` (the table) and `RuleFlag.cs`.
- **M-1a scope** (implementation decision, logged): the enum and table hold the **first 13 CD12 rows**. These are exactly the stats that M-1a readers consume: Grid, Player Character, Manual Delivery, Mail and Throwing. Each member's value is its 1-based CD12 row position:

| Value | Stat | Unit | Scope | Qualifier | Base | Ops | Clamp (min…max) | Rounding |
|---|---|---|---|---|---|---|---|---|
| 1 | DeliverInsertPm | pm | personal | — | 0 | Add | 0…`delivery.insertPmMax` (750) | HalfUp |
| 2 | MoveSpeedAddPm | pm | personal | — | 0 | Add | 0…`player.buffCapPm` (400) | HalfUp |
| 3 | ExtraJumps | count | personal | — | 0 | Add | 0…`player.maxExtraJumps` (1) | HalfUp |
| 4 | RespawnMs | ms | personal | — | `player.respawnMs` | Set (min-wins) | `player.respawnMinMs` (1000)…base | HalfUp |
| 5 | InventoryRows | count | both | container | `ContainerDef` rows | Add, Mul | 1…`inventory.maxRows` | **Ceil** |
| 6 | MailMaxStack | count | team | mailKind | `MailKindDef.maxStack` | Mul | 1…255 (code) | **Floor** |
| 7 | WeightPenaltyPm | pm | personal | — | 1000 | Mul | 0…1000 (code) | HalfUp |
| 8 | MailValueMultPm | pm | team | mailKind | 1000 | Mul | 0…`mail.payMultMaxPm` (5000) | HalfUp |
| 9 | ThrownCorrectPayMultPm | pm | personal | — | 1000 | Mul | 0…`mail.payMultMaxPm` (5000) | HalfUp |
| 10 | LateValueRatioPm | pm | team | — | `mail.lateValueRatio` (500) | Set (max-wins) | 0…1000 (code) | HalfUp |
| 11 | MisdeliveryPenaltyMultPm | pm | team | — | 1000 | Mul | 0…`mail.penaltyMultMaxPm` (2000) | HalfUp |
| 12 | MisdeliveryComplaintMultPm | pm | team | — | 1000 | Mul | 0…`mail.penaltyMultMaxPm` (2000) | HalfUp |
| 13 | ThrowRangePermille | pm | personal | — | 1000 | Add | `throw.rangeMinPermille` (1000)…`throw.rangeMaxPermille` (1500) | HalfUp |

- **Later rows.** Rows 14+ (`BikeSpeedMultPm` …) are appended with their CD12 positions by their reading systems' epics. Their rows are provisional per OQ-CDD-16.
- **Rounding** is HalfUp where CD12 names none. The table follows CD12; the defaults are noted there.
- **`ThrownCorrectPayMultPm`'s max key.** CD12 gives 0…5000 without naming a key. Use `mail.payMultMaxPm` (CD12 note, "Which clamp bounds are data") and flag it for confirmation.
- **Clamp keys** (`delivery.insertPmMax`, `player.buffCapPm`, `player.maxExtraJumps`, `player.respawnMinMs`, `inventory.maxRows`, `throw.rangeMin/MaxPermille`, `mail.payMultMaxPm`, `mail.penaltyMultMaxPm`) are registered in the `BalanceKey` registry by this story, with owner = the reading GDD. They are **PROPOSED** until each owner lists them (OQ-CDD-18); values come from story 008.
- **Correctness bounds stay in code:** `MailMaxStack` ≤ 255 (`uint8` count); `WeightPenaltyPm` and `LateValueRatioPm` 0…1000; every `min` ≥ 0.
- **`BaseSource`** is a small discriminated leaf: `Constant(int)` | `Balance(BalanceKey)` | `ContainerRows` | `MailKindMaxStack`. Resolving a per-qualifier base needs a `ContentSet`, which happens in StatSheet build (story 014). CV-GEN-17 (base outside `[min, max]`) is story 009.
- **`RuleFlag` members** (CD18 order, values 1…14): KeepHotbarOnDeath, PresortOutfeed, InsuredRebuild, SuppressLateComplaint, PriorityMail, FreeForecast, SharedBuild, LocalKnowledgeArrow, BeltMomentum, EarlyDistrictUnlock, EnemiesMarked, PostalSprint, CursedMail, RushHour.

---

## Out of Scope

- Story 007: F1 resolution.
- Story 014: per-qualifier base resolution and sheet cells.
- Story 009: CV-GEN-17.
- CV-PRK-* rules (no PerkDef at M-1a): M1, Perks epic.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/StatDefTableTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/StatDefTableTests.cs` (37 methods / 48 cases)

---

## Dependencies

- Depends on: Story 004 (BalanceKey registry, BalanceTable)
- Unlocks: Story 007, Story 009, Story 014

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (no deferred items)
**Deviations** (all ADVISORY, logged as TD-047):
- `StatDefs` is a const `switch` with no static fields (zero static state) rather than "`static readonly` `[ImmutableTable]`" — the attribute does not exist until simulation-core story 028; `StatTypes_HoldNoStaticFields_AdrNoStaticState` locks the intent.
- Interim `BalanceKeys.CreateSimRegistry()` (sim rows only) used by `SimSettings.CreateDefault` and the Content test kit, because `BalanceTable.Build` raises CV-GEN-05 for the 11 new clamp/base keys until story 008 supplies their defaults. **Story 008 must delete it** and switch callers to `CreateRegistry()`.
- `Stat`/`RuleFlag` and their spelling tables are in namespace `PerformativeMail.Sim.Content` (beside the story 002 enums); `StatDef*`/`StatBound`/support enums are in `.Content.Stats` (keeps `StatDef` out of the `DefShapeTests` IDef scan). All files live in `Content/Stats/`.
- CV-GEN-15 `InputKey` carries one key (min key, else max key); the message text names both keys and values.
- `ThrownCorrectPayMultPm` max uses `mail.payMultMaxPm` — FLAGGED for owner confirmation (OQ-CDD-18). Clamp keys are PROPOSED; hard ranges are width/sign only.
**Test Evidence**: Logic — `tests/unit/content/StatDefTableTests.cs` (37 methods / 48 cases; all CDD-STAT-1). Four legs (net10/net8 × Debug/Release) 1,682 tests/leg green, 0 warnings (verified in a private snapshot excluding world-stub's in-flight `WorldTilesTests.cs`); Debug 12 skipped.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (9 advisories), QL-TEST-COVERAGE GAPS (minor), godot-csharp-specialist APPROVE. Bad cref in StatDef.cs fixed at close.
**Run result**: N/A — Godot-free Sim data tables; nothing rendered.
