# Story 003: Content diagnostics and the validator rule registry core

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD3.5, CD22, CD23)
**Requirement**: `TR-content-022` (M-1a subset: the registry mechanism; the full 84/89-rule catalogue is M0)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§5 Validator, §2 diagnostics)
**ADR Decision Summary**:
- Every rule has a permanent id `CV-<AREA>-NN` that is never reused. Each rule also carries an owner GDD, an owner alias, a severity (Error/Warning), a stage and its declared inputs.
- The registry is code: explicit lists in `Sim/Content/Rules/<Owner>.cs`, with no reflection discovery.
- Errors block the `ContentSet` and can never be suppressed. Warnings can be waived per def with `suppress: [{rule, why}]`.
- Every diagnostic carries `path:line:column`, the rule id, the value and the allowed range or set.

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: validator rules have permanent ids `CV-<AREA>-NN` and are registered as code in `Sim/Content/Rules/<Owner>.cs`. Each clause ships a violation fixture, plus a valid-edge fixture where it has a limit. Errors are never suppressible. (ADR-0010 §5)
- Required: every diagnostic carries `path:line:column` (1-based UTF-8 byte column), the rule id, the value and the allowed range/set. (ADR-0010 §2)
- Forbidden: runtime reflection discovery. Rule registration is an explicit list. (ADR-0010 §5)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] A `ContentDiagnostic` carries the rule id, severity, `path:line:column` (LF line counting, 1-based UTF-8 byte column; `0:0` where the source is `BuiltInContent`), the offending value and the allowed range or set. A diagnostic list sorts deterministically by (path ordinal, line, column, rule id). (CD3.5, CD24 ordering)
- [x] The rule registry exposes each rule's id, owner, alias, severity, stage and declared inputs. A message prints both the id and the alias (`CV-THR-12 (THROW-CV-12)`). Resolving an alias (`THROW-CV-12`, `PC rule 7`, `Throwing rule 14`) returns the canonical id. A retired rule (CV-THR-14) stays listed as retired. (CDD-RULE-2)
- [x] Severity policy, using test-only rules:
  - A Warning is reported and the set still builds.
  - A `suppress` entry with `rule` + `why` on the offending def silences that Warning.
  - A `suppress` without `why` is CV-GEN-05.
  - A `suppress` naming an Error rule or an unknown rule id is an error.
  - A `suppress` that matches nothing is only counted.
  - An Error always prevents the set from being built.

  (CD22; mechanism half of CDD-RULE-1)
- [x] Skip accounting: a rule whose declared inputs failed an earlier stage is skipped for those defs. The result counts the skipped (rule, def) pairs and names the blocking field or file. (CD24; supports CDD-LOAD-3)
- [x] A snapshot test of registered rule ids fails if an id is removed or renumbered. A fixture meta-test iterates the registry with no hard-coded count, and fails if any registered clause lacks a violation fixture that trips it. (CDD-RULE-2, CDD-VAL-1 meta-test)

---

## Implementation Notes

*Derived from ADR-0010 §5 / Implementation Guidelines and GDD CD22–CD24:*

- **Location:** `src/PerformativeMail.Sim/Content/Validation/`. Rule tables go in `src/PerformativeMail.Sim/Content/Rules/<Owner>.cs`: `General.cs` (CV-GEN), `Grid.cs` (CV-GRD), `Mail.cs` (CV-MAIL), `Player.cs` (CV-PLR), `Throwing.cs` (CV-THR). This story creates the mechanism plus one test-only rule set. Stories 009–012 register the real rules.
- **Rule shape:** `{ string Id, string Owner, string Alias, Severity Severity, Stage Stage, inputs, check delegate }`.
  - The check is a static method over the stage's input (a built `ContentSet` for stages 4–5; parse state for stages 1–3). It appends to a diagnostics buffer.
  - **No reflection discovery.** `General.Rules` and the other lists are explicit `ImmutableArray`s.
- **Stages:** 1 syntax, 2 schema, 3 build, 4 references/cross-def, 5 whole-set (ADR-0010 §4). The runner for stages 4–5 runs over a built `ContentSet`. Both `BuiltInContent` and the loader call it (story 008, story 019).
- **Fixtures:**
  - **Violation fixture** = a minimal valid content tree plus an id-addressed overlay. It must yield exactly one diagnostic of that id at that severity. Any other id it trips is listed in a reviewed `alsoTrips`.
  - **At M-1a the "tree"** is a `ContentSet` built in code from a minimal-valid factory (story 008 provides it), and the overlay replaces one def or balance value.
  - **The JSONC-file form** of the fixtures arrives with the M0 CLI.
- **`suppress`** is a per-def list of `{rule, why}`. At M-1a it is carried on the def's diagnostic metadata, not as a gameplay field. It is not hashed (GDD Hashing edge case).
- **Deferred to M0:** `docs/content-rules.md` generation (`--list-rules --format md`) and the CI diff.

---

## Out of Scope

- Stories 009–012: the real CV-GEN, CV-GRD, CV-MAIL, CV-PLR and CV-THR rules.
- M0 (not this epic's M-1a slice): CLI exit codes, `--strict`, `--format`, and the `docs/content-rules.md` generator (TR-content-023).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/RuleRegistryTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/RuleRegistryTests.cs` (55 tests) + `tests/unit/content/ContentRuleTestKit.cs` (test-only CV-TST rules, fixture catalogue)

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (test project).
- Unlocks: Story 004, Story 009, Story 017

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (no deferred items)
**Deviations**: ADVISORY (accepted) — (1) provisional rule id CV-GEN-21 for "suppress names an Error/unknown rule" (CD22 gives no id; GDD catalogue amendment pending); (2) CV-GEN-05 also covers suppress-without-why/rule (clause `suppress_why`; story 018 adds `required_field`); (3) CV-GEN-13/20 left to story 004; (4) CV-THR-14 registered retired → CV-PLR-06 (unresolved until story 011; a test activates once any CV-PLR rule is registered); (5) owner `Rules` are static properties (ADR-0003 §4), not static fields. Residual review advisories logged as TD-037.
**Test Evidence**: Logic: `tests/unit/content/RuleRegistryTests.cs` (55/55 pass); four legs green at 1279 tests/leg (net10/net8 × Debug/Release; Debug 6 skipped from earlier stories); snapshot + both meta-tests seen failing by mutation.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS → revised twice (all findings resolved); QL-TEST-COVERAGE GAPS → items 1–3 added, 4–6 in TD-037; godot-csharp-specialist no BLOCKING (advisories fixed).
