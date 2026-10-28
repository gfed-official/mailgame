# Story 012: Throwing content rules — CV-THR M-1a subset

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (Rule Catalogue index row CV-THR-01…18; F3; F5 single-source mode); rule text is authoritative in `design/gdd/throwing-catching.md` ("ContentValidator rules (this GDD)", ~line 716) and its AC TC-CV-1 (~line 1205).
**Requirement**: `TR-content-022` (M-1a subset)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§5)
**ADR Decision Summary**: One code-side registry. `THROW-CV-nn` is the alias of `CV-THR-nn`, and every clause has a violation fixture.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: rules are registered as code in `Sim/Content/Rules/<Owner>.cs`, with violation and valid-edge fixtures. Errors are never suppressible. (ADR-0010 §5)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-CV-1) and `content-data-definitions.md` (F3, CDD-STAT-11), scoped to the M-1a subset:*

- [x] **Registration and fixtures.** These rules are registered as Errors with alias `THROW-CV-nn`: CV-THR-01, 02, 03, 04, 05, 06, 08, 09, 10, 11, 12, 13, 15, 16, 17 and 18. CV-THR-17 (map extents ≤ 1 000 000 mm) is registered as an Error with `RuleStatus.Deferred` — map extents are world-generation output (ADR-0026 `Bounds` contract), not content, so no M-1a content input exists (amended 2026-10-16, decision log; TC-CV-1 row 17 carried by TD-069(1)). Each live rule's violation fixture yields exactly one diagnostic of its id; the message prints `CV-THR-nn (THROW-CV-nn)`. `BuiltInContent.CreateM1a()` passes all of them. (TC-CV-1 for the M-1a rows; CDD-RULE-2)
- [x] **CV-THR-02 edge (F5 single-source):** `ThrowRangePermille` base 1 000 + a synthetic Add of 500 = 1 500 passes. A single +501 gives 1 501, which errors. (CDD-STAT-11 clause)
  - The fixture also lists CV-PRK-05 in its `alsoTrips`. Because CV-PRK-05 is not registered at M-1a, the meta-test accepts its absence, and a comment cites M1.
- [x] **CV-THR-12 edge (F3 ExactTickCheck):** `catchTapMs` 500 and 100 pass; 501 errors. (CDD-VAL-1 normative edges)
- [x] **CV-THR-16:** `throwCooldownTicks` 0 errors and 1 passes; likewise for `throwBurstTicks`.
- [x] **CV-THR-18:** `catchRadiusMm + catchHistoryMaxOffsetMm` = 3 700 passes, and 3 701 errors.
- [x] **CV-THR-14** stays listed as retired, pointing to CV-PLR-06.

---

## Implementation Notes

*Derived from ADR-0010 §5; rule text from Throwing's rule table (authoritative):*

- **Location:** `Rules/Throwing.cs`.
- **Rule inputs** come from the `throw` balance owner record (`ThrowBalance`, story 008), the `house` `DestinationTypeDef.CatchVolume` and `ItemDef`/`MailKindDef` `weightClass`. In brief:
  - Rule 1: speed ordering heavy < medium < light, each in its range.
  - Rule 5: the receiver rank table is a permutation of the 8 kinds.
  - Rule 6: catch volumes are axis-aligned with extents > 0.
  - Rule 8: every item has a `weightClass`, and `bulk` is not hotbar-placeable.
  - Rules 9 and 10 read `player.capsuleRadiusMm` and `player.capsuleHeightMm`. These cross-owner rules are stage 5 and declare both inputs.
  - Rule 11: 70° + `loftDeg` ≤ 78°.
  - Rule 13: `catchPredictMs` ≤ `maxFlightTicks` × 1000/30.
- **F3** (`(ms × 30) mod 1000 == 0`, computed in `long`) lives in story 016's unit helpers. If story 016 has not landed, implement F3 here as a pure helper and let story 016 reuse it.
- **CV-THR-07** (`EnemyDef` radius/height) has no M-1a input, because there are no enemies. Mark it "inputs not present at M-1a".
- **Stale names.** Do not register stale names: `cooldownSlackTicks` was replaced by rule 16's `throwCooldownTicks`/`throwBurstTicks`, and `throwSpreadMrad` is retired (Throwing OQ1). Flag them to the GDD re-review (epic Risk 1).

---

## Out of Scope

- Story 011: CV-PLR-06, the former rule 14.
- Later (Enemy epic): CV-THR-07.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/ThrowingRulesTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/ThrowingRulesTests.cs` (98 cases) + `ThrowingRuleFixtures.cs`

---

## Dependencies

- Depends on: Story 003, Story 007 (F5 single-source mode), Story 008
- Unlocks: throwing-catching epic (flight and catch stories rely on these invariants)

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 6/6 passing (AC 1 amended: CV-THR-17 registered Error + `RuleStatus.Deferred` — map extents are world-gen output; CV-THR-07 deferred per Out of Scope)
**Deviations** (advisory, logged as TD-069): CV-THR-08 declares only `mail_kinds` (CDD-SCHEMA-3 skip on failed item table pending loader); hotbar-placeable read as `CarryByHand`; catalogue stage-2 rules run at stage 4 (TD-063(1)); CV-THR-02 has two Add-only clauses (vacuous in production until M1 perks); F3 `ExactTickCheck.IsWholeTicks` added in `Sim/Content/ExactTickCheck.cs` for story 016 to reuse; `RuleFixture` gained a forward-reference allow-list + append-only `FixtureProblemKind.UnregisteredAlsoTrips = 10`; CV-PLR-06 fixtures now zero `catchHistoryMaxOffsetMm` (stay single-diagnostic with CV-THR-18); CV-GRD-02 `anvil` fixture `CarryByHand = false`.
**Test Evidence**: Logic: `tests/unit/content/ThrowingRulesTests.cs` (122 cases) + `tests/unit/content/ThrowingRuleFixtures.cs`; four legs green (2 963 tests/leg, 0 failed; Debug 18 skipped).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (two process blockers resolved: AC amended + TD-069; advisories fixed or logged); QL-TEST-COVERAGE GAPS → G1 resolved by amendment, G3/G4/G5/G7 fixed in one revision round, G2/G6 logged (TD-069).
