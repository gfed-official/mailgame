# Story 009: General set-level rules — CV-GEN-01, 09, 17, 18

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (ContentValidator Rule Catalogue: CV-GEN rows; Edge Cases "Loading & parsing", "Ids & enums", "StatSheet")
**Requirement**: `TR-content-022` (M-1a subset)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§5)
**ADR Decision Summary**: One code-side rule registry is the single source of load-time rules. Each rule clause ships a violation fixture, plus a valid-edge fixture where it has a limit. Errors block the build.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: rules are registered as code in `Sim/Content/Rules/<Owner>.cs`. Each clause ships a violation fixture, plus a valid-edge fixture where it has a limit. (ADR-0010 §5)
- Required: ordinal compares only for ids. (ADR-0010)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] **CV-GEN-01**:
  - The same id twice within one def type is one Error naming both locations, and neither def is kept. The same id in two different types passes.
  - A reference to a missing id is an Error. So is a reference to an id of the wrong def type, which counts as unresolved.

  (CDD-ID-1 duplicate clause; Ids & enums edge case)
- [x] **CV-GEN-09:** 65 535 defs in one type is an Error at stage 3. That type's dependent rules are skipped and counted. 65 534 defs pass, which is the valid-edge fixture. (CDD-ID-3)
- [x] **CV-GEN-17:** a stat's per-qualifier base outside its own `[min, max]` is an Error at stage 5. Example: a container whose rows exceed `inventory.maxRows`. A container at exactly `inventory.maxRows` passes. (StatSheet edge case)
- [x] **CV-GEN-18:** an empty MailKindDef, MailMixDef, DestinationTypeDef or ContainerDef table is an Error at stage 5, one per table. An empty ItemDef table passes. (Loading edge case)
- [x] Each rule is registered in `Rules/General.cs` with severity E and the catalogue stage, and has its violation fixture. `BuiltInContent.CreateM1a()` still validates with zero Errors.

---

## Implementation Notes

*Derived from ADR-0010 §5 and the GDD catalogue:*

- **Fixtures.** Each violation fixture is `ContentFixtures.MinimalValid()` plus one overlay (story 008). Each must produce exactly one diagnostic of that id; other ids go in a reviewed `alsoTrips` list.
- **CV-GEN-01 timing.** Uniqueness is checked at the start of stage 3, before F4 assigns indices. Reference resolution is stage 4.
  - For `BuiltInContent`, the factory builds by id string and then resolves. The check runs inside the same "build then validate" path, so in-code duplicates are caught too.
- **CV-GEN-09 input.** The count comes from story 001's assigner "too many" result.
- **CV-GEN-17** resolves each stat's base for each qualifier:
  - container rows for `InventoryRows`;
  - `MailKindDef.maxStack` for `MailMaxStack`;
  - the balance key or constant for the rest.

  It uses the `StatDef` `BaseSource` (story 006) and the resolved bounds.
- **Out of M-1a by design:**
  - CV-GEN-02, 03, 04, 05 (fields), 06, 08, 12, 14 and 16 are schema/syntax rules, emitted by the loader stories 016–019. CV-GEN-05/13/20 for balance keys are already in story 004.
  - CV-GEN-07 (duration cap) is part of F2 (story 016).
  - CV-GEN-10 (PCK manifest), CV-GEN-11 (unreferenced def warning) and CV-GEN-19 (deferred defs) are M0: manifest and PCK boot, shop/recipe/kit references, and the `deferred` tag.

---

## Out of Scope

- Story 010: CV-GRD and CV-MAIL rules.
- Stories 011 and 012: CV-PLR and CV-THR.
- Story 006: CV-GEN-15.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/GeneralRulesTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/GeneralRulesTests.cs` + `tests/unit/content/GeneralRuleFixtures.cs` (15 fixtures in RuleFixtureCatalogue.Real())

---

## Dependencies

- Depends on: Story 003 (registry), Story 006 (`StatDef` bounds), Story 008 (fixtures, `BuiltInContent`)
- Unlocks: Story 018 (re-uses CV-GEN-01/09 from the loader path)

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (no deferred items)
**Deviations** (all ADVISORY, logged as TD-059):
- `ContentPipeline.Build` now reports duplicate ids as CV-GEN-01 (one Error naming every location; every copy dropped; the type becomes a failed input so dependents are skipped and counted, not cascaded) instead of throwing; empty ids still throw (CV-GEN-02 is story 018's).
- CV-GEN-01 registry row is stage References (4) with its `duplicate_id` clause emitted at stage 3; extra `unresolved_index` clause for built-in index references; by-id references via new `PendingReference` (pending refs into a failed Container/Item table are skipped silently — documented).
- Stat-level input key (TD-047 item 4) not adopted — the runner skips whole rules on `Def.None` failures, so table keys (`ContentInputKeys`) are declared.
- Rule bodies are instance methods on a stateless sealed `GeneralSetChecks` (the no-static-state test rejects compiler delegate caches); runner-run tests over `TestInput` use `TestRules.RunnableRegistry()`.
**Test Evidence**: Logic — `tests/unit/content/GeneralRulesTests.cs` (per-type Theories for CV-GEN-01/09 wiring, cascade suppression, CV-GEN-17 container/max-stack/balance/constant bases with edges, CV-GEN-18 per table, registration, CreateM1a zero Errors/Warnings) + `GeneralRuleFixtures.cs` (15 violation/edge fixtures checked by the RuleRegistryTests meta-test). Four legs (net10/net8 × Debug/Release) 2,235 tests/leg green in the real tree, 0 warnings, Debug 15 skipped.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (1 blocking: CheckReferences length/CC) resolved by extraction (all methods ≤ 38 lines); QL-TEST-COVERAGE GAPS → gaps 1–4 fixed in revision; godot-csharp-specialist APPROVE (advisories 1–5, 8 fixed).
**Run result**: N/A — Godot-free Sim validation rules; nothing rendered.
