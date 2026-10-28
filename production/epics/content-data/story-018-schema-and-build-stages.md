# Story 018: Schema and build stages for M-1a def files — FieldSpec tables, ids, enums, variants

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: L-ish (~1 day; split the FieldSpec tables into a follow-up if over ~600 lines)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-28 (/dev-story: finished on branch `story/content-data-018`; the schema walker, the stage-3 builder, the neutral-defaults sweep, `SchemaStageTests`, SelfCheck at `SchemaStage` construction and the TD-063 rename; all eight ACs ticked; Sonnet /code-review r2 APPROVED; full suite 5,560 tests per leg, 0 failed on net10 Debug and net8 Release). 2026-10-21 09:10 PT (Mail Review R1 on PR #19: the Neutral-defaults AC adds `DefShapeTests.cs` and the `ContentFixtures.Cursor` compile break, from a full grep for these test pins; BatchKnobs truncation noted). 2026-10-21 08:40 PT (Mail Review on PR #19: the Neutral-defaults AC names the tests and doc comments that change; `view` is out of the neutral-defaults set; OQ-018-1 names the at-use tick helpers). 2026-10-21 08:08 PT (owner rulings close OQ-018-1 — defs keep ms, consumers convert to ticks, so seconds → ms is stage 3's whole conversion — and OQ-018-2 — `view` required, missing → CV-GEN-05; ACs updated; status unchanged). 2026-10-21 (owner ruling on the ADR-0010 defaults: neutral, off by default; `carryByHand`/`acceptsInsert` default false — GDD CD10 note 2026-10-21; new AC "Neutral defaults"; open questions for the owner: OQ-018-1 seconds → ticks, OQ-018-2 `view`'s default; status unchanged). 2026-10-17

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD3.3–3.5, CD4, CD5, CD7, CD8, CD10, CD21 stages 2–3; Ids & enums edge cases)
**Requirement**: `TR-content-006`, `TR-content-007`, `TR-content-011`, `TR-content-012` (loader-side parts)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

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

- [x] **Id lint.** The ids `Letter`, `1abc` and a 49-character id each give one CV-GEN-02, while `ab` and a 48-character id pass. A duplicate id within one type gives CV-GEN-01, even when the two copies are in two files; both `path:line`s are named and neither def is kept. The same id in two different types passes. (CDD-ID-1)
- [x] **Unknown and reserved fields.** `maxStak` gives CV-GEN-03 at the field's own line and column, suggesting `maxStack`. `standardIssue` on an ItemDef gives CV-GEN-03. `$schema` and `suppress` pass. (CDD-PARSE-2, unknown-field half; CDD-SCHEMA-6)
- [x] **Required fields and defaults.** An ItemDef without `weightClass` gives exactly one CV-GEN-05; any rule that reads `weightClass`, e.g. CV-THR-08, is skipped and counted. Omitting `allowedCategories` passes, and `[]` gives CV-GRD-03. Every `FieldSpec` default is in {any, 0, false, none, 1000}; `view` is not in that set, because it is required and has no default (see "Neutral defaults"). (CDD-SCHEMA-3)
- [x] **Enums.** `weightClass` `"5"`, `"Light"` and `"LIGHT"` each give CV-GEN-08. (CDD-SCHEMA-4)
- [x] **Variants and widths.**
  - A `pile` container with `grid` gives CV-GEN-14.
  - An integer above its field's storage width gives CV-GEN-12. A list with more than 65 535 elements, or a string longer than 65 535 UTF-8 bytes, also gives CV-GEN-12 at stage 2 (the canonical encoder's `ContentEncodingException` must be unreachable from loaded content — TD-075(1)).
  - A `FieldSpec` snapshot test matches the CD10 rows for the five M-1a def types.
  - Each type's `FieldSpec` order equals its canonical encode order from story 013.

  (CDD-SCHEMA-6, M-1a form)
- [x] **Carried tech debt (added 2026-10-17).** TD-090: `DecimalToScaled.Convert` split below CC 10; `FieldSpecTable.SelfCheck` flags `IsDuration` with a unit outside {Seconds, Ms}, Min > Max, a Default outside [Min, Max], and a missing EnumSet/RefTarget; duration rows honour Min/Max clamped to [max(Min,0), min(Max, 86 400 000)]; `SelfCheck` runs over every table at loader construction and in a test; a field-name-suffix → `FieldUnit` mapping test; registry rows for CV-GEN-06/07/12 (constants only so far). TD-063(1): catalogue stage-2 rules registered at stage 4 (CV-GRD-03/04/07, CV-MAIL-03) are moved or explicitly documented as "2 (runs at 4)"; `allowedCategories: []` is raised by the schema stage so the def never reaches stage 4.
- [x] **Neutral defaults (owner ruling 2026-10-21; GDD CD10 note, ADR-0010 §3).** The `FieldSpec` defaults of `carryByHand` (ItemDef, MailKindDef) and `acceptsInsert` (ContainerDef) are `false`. `view` (ContainerDef) is **required** with no default: a ContainerDef without `view` gives exactly one CV-GEN-05, like the `weightClass` case above. The def records match their `FieldSpec`: the `= true` initializers on `ItemDef.CarryByHand`, `MailKindDef.CarryByHand` and `ContainerDef.AcceptsInsert` (story 005 code) become `false`, `ContainerDef.View` loses its `= ContainerView.Auto` initializer and becomes a `required` member (story 005's pattern for required fields), and the comments that state the old exception (`ItemDefFieldSpec.cs` "GDD exception to neutral defaults") are corrected. The tests and doc comments that assert the old defaults are updated in the same change: `tests/unit/content/FieldSpecTableTests.cs` (the row snapshots `carryByHand:Bool O=1` at l.128 and l.155, `acceptsInsert:Bool O=1` at l.142 and `view:Enum O=auto` at l.141, and `_gddDefaultExceptions` / `EveryDefault_IsNeutral_ExceptTheGddStatedOnes` / `RequiredRows_CarryNoDefault_AndTheGddDefaultsAreWhatCd10States` at l.271–316, which become "no exceptions" and list `view` as required); `tests/integration/content/BuiltInContentTests.cs:120` (`Assert.True(d.CarryByHand)` on a def built without the field); `tests/unit/content/ThrowingRulesTests.cs:548` and any other test that relies on the `Letter` fixture's default `CarryByHand`; `tests/unit/content/Fixtures/ContentFixtures.cs` (fixtures set the three fields explicitly: `Cursor` at l.181 has no `View` and stops compiling once `View` is required, and `Letter` at l.166 and `Plank` at l.187 rely on the `CarryByHand` default); `tests/unit/content/DefShapeTests.cs` (the defaults table pins `ItemDef.CarryByHand` `true` at l.530, `ContainerDef.View` `ContainerView.Auto` at l.536, `ContainerDef.AcceptsInsert` `true` at l.541 and `MailKindDef.CarryByHand` `true` at l.543; the member snapshot has `View:ContainerView:D` at l.402, which becomes `:R`; `ContainerDef` is built without `View` at l.493, l.505, l.506 and l.558, which stop compiling, and l.553/l.559 build `ItemDef`/`MailKindDef` on the `CarryByHand` default); and the class and member doc comments in `ItemDef.cs`, `MailKindDef.cs`, `ContainerDef.cs`, `ItemDefFieldSpec.cs`, `MailKindDefFieldSpec.cs`, `ContainerDefFieldSpec.cs` and `Enums/ContainerView.cs` that state the old `true`/`auto` defaults or the "GDD exception to neutral defaults". `BuiltInContent` sets `CarryByHand` and `AcceptsInsert` explicitly on every def with the values it has today (it already sets `View`). The loaded values and the golden content digest are therefore unchanged; a test pins the digest.
- [x] **Build.** Stage 3 runs only when stages 1–2 are clean for that type. It assigns `DefIndex` ordinally (story 001), maps "too many" to CV-GEN-09, converts units (story 016; `…Seconds` → ms is the whole time conversion — defs keep ms and add no tick-valued fields, ADR-0010 Errata 2026-10-21) and resolves references. A wrong-type or missing reference gives CV-GEN-01 at stage 4. (CD21)

---

## Implementation Notes

*Derived from ADR-0010 §2–§4 and GDD CD3/CD5/CD10:*

- **Location:** `src/PerformativeMail.Sim/Content/Parsing/SchemaWalker.cs` and `Builder.cs`, plus `src/PerformativeMail.Sim/Content/Defs/<Def>FieldSpec.cs` (one table per def type).
- **FieldSpec tables.** Write one explicit `static readonly` `ImmutableArray<FieldSpec>` per M-1a def type (`[ImmutableTable]`). The fields are story 005's, with JSON spellings:

  | Def type | JSON field spellings |
  |---|---|
  | `ItemDef` | `id`, `name`, `category`, `grid`, `maxStack`, `weightClass`, `sellPrice`, `buyPrice`, `carryByHand`, `equipSlot`, `tags` |
  | `ContainerDef` | `id`, `layout`, `grid`, `view` (required), `beltAccess`, `allowedCategories`, `handCarried`, `acceptsInsert`, `reservedSlots` |
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

## Open Questions

- **OQ-018-1 — CLOSED 2026-10-21 (owner ruling, Evan, 08:08 PT; ADR-0010 Errata 2026-10-21, GDD CD21/CD4 amended).** Content defs keep milliseconds, and each consuming system converts to ticks where it uses the value: `Hold.HoldTicks` for hold durations (via `GetHoldTicks`), `BatchKnobs.From` (truncates ms to whole seconds) + `BatchSchedule` for mail-mix timings, and Sim Core F1 `TickTime.ContentTicks` for any other content duration. Stage 3's `…Seconds` → ms conversion (story 016) is the whole conversion.
- **OQ-018-2 — CLOSED 2026-10-21 (owner ruling, Evan, 08:08 PT; GDD CD10).** `view` is a required ContainerDef field with no default; a missing `view` gives CV-GEN-05.

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

**Status**: [x] Created 2026-10-28 — `tests/unit/content/SchemaStageTests.cs` (plus `Fixtures/SchemaJson.cs` and the real-walk `SchemaRuleFixtures`). Full suite on the branch: 5,560 tests per leg (main 6757c05: 5,521), 0 failed; net10.0 Debug 5,496 passed / 64 skipped, net8.0 Release 5,553 passed / 7 skipped. CI is recorded in the PR body.
- Judgment calls are labelled "Mail Dev default, pending Evan" in code: the `path:line` form (line of the def's `{`) for file-backed findings, including both copies of a cross-file duplicate id; a duplicate whose copy also has a schema error shows only the schema error until the type is clean; CV-THR-08 declares the whole `items` table; a wrong-JSON-kind value is reported by that field kind's rule (CV-GEN-06/08/02, else CV-GEN-16); a non-object array element and an unpaired surrogate escape are CV-GEN-16; a `grid` list of other than 2 elements is CV-GEN-12; a file with a stage-1 Error is not walked at stage 2; an unresolved reference is stored as `DefIndex.None` (stage 4 may report it twice); CV-GEN-09 sits at the first def over the limit; "loader construction" for SelfCheck is the `SchemaStage` constructor.
- Left for story 019: parsed `suppress` entries are not yet passed to the stage 4–5 runner; stage-4 findings on loaded defs still carry `builtin/<folder>/<id>` locations; MailMix `shares` keep authored order in the hash (TD-075(3); CD25 "key order" wording to confirm with Evan).

---

## Dependencies

- Depends on: Story 001, Story 002, Story 005, Story 009 (CV-GEN-01/09), Story 013 (encode order), Story 016, Story 017
- Unlocks: Story 019
