# Story 010: Grid and Mail content rules — CV-GRD-01…04, 06, 07 and CV-MAIL-01…06

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (Rule Catalogue CV-GRD and CV-MAIL rows; F5); rule owners `design/gdd/grid-inventory.md` and `design/gdd/mail-destinations.md` (M-R1-1c, Dependencies)
**Requirement**: `TR-content-022` (M-1a subset). The epic said "CV-INV"; no CV-INV id exists, and the Grid ids are CV-GRD.
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§5)
**ADR Decision Summary**: One code-side rule registry, with a violation fixture per clause and a valid-edge fixture at each limit. A rule a gameplay system relies on for correctness is always an Error.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: rules are registered as code in `Sim/Content/Rules/<Owner>.cs`, with violation and valid-edge fixtures. Errors are never suppressible; Warnings need a per-def `suppress` with `why`. (ADR-0010 §5)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] **CV-GRD-01** (E, stage 5): `maxStack` × the worst-case `MailMaxStack` Mul (F5, story 007) must be ≤ 255. At M-1a there are no sources, so the worst case is the base. Fixtures (CDD-VAL-1 normative edges):
  - maxStack 127 with one synthetic ×2.0 source → 254 passes;
  - maxStack 128 → 256 errors;
  - with no sources, maxStack 255 passes and 256 errors.
- [x] **CV-GRD-02** (E, stage 5): a `handCarried` container that could admit a `bulk`-class item errors.
- [x] **CV-GRD-03** (E, stage 2): `allowedCategories` present but empty errors.
- [x] **CV-GRD-04** (E, stage 2): a grid or slots container with W < 1 or H < 1 errors, and so does any `maxStack` < 1.
- [x] **CV-GRD-06** (W, stage 5): `capEvictMinAgeSeconds` ≥ `worldItemDespawnSeconds` warns.
- [x] **CV-GRD-07** (W, stage 2): `worldItemDespawnSeconds` < 30 s warns.
  - The run proceeds; a `suppress` with `why` silences the warning (mechanism half of CDD-RULE-1).
  - 29 s warns and 30 s passes.
- [x] **CV-MAIL-01** (E, stage 4): errors when `acceptedBy` is not a subset of the destination type ids, or when `MailKindDef.acceptedBy` does not mirror `DestinationTypeDef.accepts`.
- [x] **CV-MAIL-02** (E, stage 5): `sharesPm` must sum to exactly 1 000 per shift, and each share must be 0…600. Fixtures: sum 1 000 passes, 999 errors, and one share of 601 errors (CDD-VAL-1 normative edges).
- [x] **CV-MAIL-03** (E, stage 2): `deadLetterShifts` must be ≥ 1.
- [x] **CV-MAIL-04** (E, stage 5): every kind with share > 0 needs an accepting destination type or a `fallbackKind`, and fallback chains must resolve without cycles.
- [x] **CV-MAIL-05** (E, stage 5): `batchJitterSeconds` < `batchIntervalSeconds` / 2.
- [x] **CV-MAIL-06** (E, stage 5): `tailGuardSeconds` ≤ `batchIntervalSeconds` − `batchJitterSeconds`.
- [x] Every clause above has its violation fixture, plus a valid-edge fixture where it has a limit. `BuiltInContent.CreateM1a()` reports zero Errors and zero unsuppressed Warnings.

---

## Implementation Notes

*Derived from ADR-0010 §5; rule text is the owner GDD's (CD23):*

- **Location:** `Rules/Grid.cs` and `Rules/Mail.cs`.
- **Aliases.** Use the owner aliases from the catalogue: `Grid (R3)`, `Mail (M-R1-1c)`, and so on.
- **Owner-numbering hand-backs** (OQ-CDD-2/3): once Grid and Mail number their own rules, these rows become index rows. The ids never change.
- **CV-MAIL-04 fixture.** It needs a kind with no acceptor. Use a fixture-only kind; do not add `cargo` to `BuiltInContent`.
- **CV-GRD-05** (`deathBagDespawnSeconds` vs `player.respawnMs`) is M-1b, because the death bag is M-1b (player-character epic). Register it with that epic's content.

---

## Out of Scope

- Stories 011 and 012: CV-PLR and CV-THR.
- M-1b: CV-GRD-05.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/GridMailRulesTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/GridMailRulesTests.cs` (71 cases) + `GridMailRuleFixtures.cs`

---

## Dependencies

- Depends on: Story 003, Story 007 (F5 `WorstCase`), Story 008 (fixtures, `BuiltInContent`)
- Unlocks: grid-inventory and mail-destinations epics (they rely on these rules being Errors)

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 13/13 passing (none deferred)
**Deviations** (advisory, logged as TD-063): catalogue stage-2 rules CV-GRD-03/04/07 and CV-MAIL-03 run at stage 4 (runner executes stages 4–5 only; story 018 to move); aliases suffixed for registry uniqueness; set-level `suppress` record (`ContentValidationInput` `setSuppress`) carries suppress for the def-less CV-GRD-06/07 warnings; CV-GRD-02/04 read items without declaring the items input key; CV-GRD-04 `stackMult < 1` clause has no M-1a field; `ContentFixtures.MinimalValid()` mix is now letter 600 + 400; CV-MAIL-03 is shadowed by CV-GEN-13's hard range (fixture widens the hard min).
**Test Evidence**: Logic: `tests/unit/content/GridMailRulesTests.cs` (71 cases) + fixtures in `tests/unit/content/GridMailRuleFixtures.cs`; full suite 2 550 tests/leg green (net10/net8 × Debug/Release; Debug 16 skipped).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (blocking `Player` namespace collision resolved upstream; advisories → TD-063); QL-TEST-COVERAGE ADEQUATE (advisory gaps → TD-063(8)).
