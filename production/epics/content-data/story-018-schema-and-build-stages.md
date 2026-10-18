# Story 018: Schema and build stages for M-1a def files — FieldSpec tables, ids, enums, variants

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: In Progress
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: L-ish (~1 day; split the FieldSpec tables into a follow-up if over ~600 lines)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD3.3–3.5, CD4, CD5, CD7, CD8, CD10, CD21 stages 2–3; Ids & enums edge cases)
**Requirement**: `TR-content-006`, `TR-content-007`, `TR-content-011`, `TR-content-012` (loader-side parts)
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§2 FieldSpec, §3 Defs, §4 stages) (primary); ADR-0004: Identity and Id Spaces (§6)
**ADR Decision Summary**:
- **Parsing.** Each def type has a code-side `FieldSpec` table (name, unit, required/default, range, enum set or reference target) that drives parsing. Unknown fields are errors with an edit-distance ≤ 2 suggestion. `$schema` and `suppress` are the only reserved keys.
- **Stage 2 (schema)** checks fields, types, enums, the id lint, units and ranges.
- **Stage 3 (build)** runs only if stages 1–2 are clean. It sorts ids, assigns `DefIndex` and converts units.

**ADR Version**: ADR-0010 2026-10-16; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: parsing is a hand-written forward `Utf8JsonReader` walk per file, driven by a per-def-type `FieldSpec` table. Duplicate keys and unknown fields are errors (edit-distance ≤ 2 suggestion). (ADR-0010 §2)
- Required: id regex `^[a-z][a-z0-9_]{0,47}$`; defaults are neutral. (ADR-0010 §3)
- Required: `DefIndex` = ordinal in the type's `StringComparer.Ordinal`-sorted id list. (ADR-0004 §6)
- Forbidden: `Enum.Parse`; a .NET `Regex` for the id lint (its `$` would accept a trailing `\n`; the GDD requires a byte scan); culture compares. (ADR-0010; GDD edge cases)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [ ] **Id lint.** The ids `Letter`, `1abc` and a 49-character id each give one CV-GEN-02, while `ab` and a 48-character id pass. A duplicate id within one type gives CV-GEN-01, even when the two copies are in two files; both `path:line`s are named and neither def is kept. The same id in two different types passes. (CDD-ID-1)
- [ ] **Unknown and reserved fields.** `maxStak` gives CV-GEN-03 at the field's own line and column, suggesting `maxStack`. `standardIssue` on an ItemDef gives CV-GEN-03. `$schema` and `suppress` pass. (CDD-PARSE-2, unknown-field half; CDD-SCHEMA-6)
- [ ] **Required fields and defaults.** An ItemDef without `weightClass` gives exactly one CV-GEN-05; any rule that reads `weightClass`, e.g. CV-THR-08, is skipped and counted. Omitting `allowedCategories` passes, and `[]` gives CV-GRD-03. Every `FieldSpec` default is in {any, 0, false, none, 1000}. (CDD-SCHEMA-3)
- [ ] **Enums.** `weightClass` `"5"`, `"Light"` and `"LIGHT"` each give CV-GEN-08. (CDD-SCHEMA-4)
- [ ] **Variants and widths.**
  - A `pile` container with `grid` gives CV-GEN-14.
  - An integer above its field's storage width gives CV-GEN-12. A list with more than 65 535 elements, or a string longer than 65 535 UTF-8 bytes, also gives CV-GEN-12 at stage 2 (the canonical encoder's `ContentEncodingException` must be unreachable from loaded content — TD-075(1)).
  - A `FieldSpec` snapshot test matches the CD10 rows for the five M-1a def types.
  - Each type's `FieldSpec` order equals its canonical encode order from story 013.

  (CDD-SCHEMA-6, M-1a form)
- [ ] **Carried tech debt (added 2026-10-17).** TD-090: `DecimalToScaled.Convert` split below CC 10; `FieldSpecTable.SelfCheck` flags `IsDuration` with a unit outside {Seconds, Ms}, Min > Max, a Default outside [Min, Max], and a missing EnumSet/RefTarget; duration rows honour Min/Max clamped to [max(Min,0), min(Max, 86 400 000)]; `SelfCheck` runs over every table at loader construction and in a test; a field-name-suffix → `FieldUnit` mapping test; registry rows for CV-GEN-06/07/12 (constants only so far). TD-063(1): catalogue stage-2 rules registered at stage 4 (CV-GRD-03/04/07, CV-MAIL-03) are moved or explicitly documented as "2 (runs at 4)"; `allowedCategories: []` is raised by the schema stage so the def never reaches stage 4.
- [ ] **Build.** Stage 3 runs only when stages 1–2 are clean for that type. It assigns `DefIndex` ordinally (story 001), maps "too many" to CV-GEN-09, converts units (story 016) and resolves references. A wrong-type or missing reference gives CV-GEN-01 at stage 4. (CD21)

---

## Implementation Notes

*Derived from ADR-0010 §2–§4 and GDD CD3/CD5/CD10:*

- **Location:** `src/PerformativeMail.Sim/Content/Parsing/SchemaWalker.cs` and `Builder.cs`, plus `src/PerformativeMail.Sim/Content/Defs/<Def>FieldSpec.cs` (one table per def type).
- **FieldSpec tables.** Write one explicit `static readonly` `ImmutableArray<FieldSpec>` per M-1a def type (`[ImmutableTable]`). The fields are story 005's, with JSON spellings:

  | Def type | JSON field spellings |
  |---|---|
  | `ItemDef` | `id`, `name`, `category`, `grid`, `maxStack`, `weightClass`, `sellPrice`, `buyPrice`, `carryByHand`, `equipSlot`, `tags` |
  | `ContainerDef` | `id`, `layout`, `grid`, `view`, `beltAccess`, `allowedCategories`, `handCarried`, `acceptsInsert`, `reservedSlots` |
  | `MailKindDef` | `id`, `name`, `grid`, `maxStack`, `baseValue`, `weightClass`, `carryByHand`, `beltLanes`, `beltLengthMetres`, `deadlineOffsetShifts`, `acceptedBy`, `complaintOnMisdeliveryPoints`, `unlockShift`, `fallbackKind` |
  | `DestinationTypeDef` | `id`, `accepts`, `insertRate`, `manualInsertHoldSeconds`, `maxAutomatedFeeders`, `requiresUnit`, `catchVolume{widthMetres,heightMetres,depthMetres,centreHeightMetres,yawDeg}` |
  | `MailMixDef` | `id`, `shifts[{shift, shares{kind: Ratio}}]`, `batchIntervalSeconds`, `batchJitterSeconds`, `tailGuardSeconds`, `openingRampBatches`, the four `…Ratio` fields, `spawnOverhead` |

- **Shares.** Spec 08 authors `shares` as ratios such as `0.60`. They are stored as permille, so the walker reads each share value with the Ratio unit.
- **The walk.** The schema walk is not recursive, and legal content nests at most 5 deep. It walks each array element as one def object:
  - looks each key up in the `FieldSpec` with an ordinal byte compare;
  - reads values by type: int/decimal via story 016, enum via story 002's tables, id refs as strings resolved in stage 3/4, lists, and nested leaves.
- **`grid`.** It is a 2-element integer array `[w, h]`, following Grid's W×H.
- **Id lint.** A byte scan: first byte `a`–`z`, the rest `a`–`z`/`0`–`9`/`_`, length 1–48. Ids may not contain escapes; a raw escape in the token gives CV-GEN-02.
- **Suggestions.** Use a Levenshtein distance ≤ 2 over the type's field names. Break ties by `FieldSpec` order.
- **`suppress`.** Parse `suppress: [{rule, why}]` into the def's diagnostic metadata (story 003 policy). It is never stored on the gameplay record and never hashed.
- **Directories.** `content/items/*.jsonc` holds ItemDef, except `content/items/containers.jsonc`, which holds ContainerDef. `content/mail/kinds.jsonc` holds MailKindDef, `mix.jsonc` holds MailMixDef, and `destinations.jsonc` holds DestinationTypeDef (CD2 layout). Story 019's orchestrator maps each path to its def type.

---

## Out of Scope

- Story 019: balance files, the orchestrator, the shipped `content/` files and the equality test.
- Stories 009–012: stage 4–5 rule bodies.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/SchemaStageTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002, Story 005, Story 009 (CV-GEN-01/09), Story 013 (encode order), Story 016, Story 017
- Unlocks: Story 019
