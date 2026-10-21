# Story 011: Player Character content rules — CV-PLR M-1a subset

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (Rule Catalogue index row CV-PLR-01…13); rule text is authoritative in `design/gdd/player-character-movement.md` ("ContentValidator rules (this GDD)", ~line 450) and its AC PCM-F8-3.
**Requirement**: `TR-content-022` (M-1a subset). The epic said "CV-MOV"; no CV-MOV id exists, and the ids are CV-PLR-nn = PC rule nn.
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§5)
**ADR Decision Summary**: One code-side registry. The owner GDD's numbered rule table is the contract, and the registry row carries its alias (`PC rule 7`). Every clause has a violation fixture.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: rules are registered as code in `Sim/Content/Rules/<Owner>.cs`, with violation and valid-edge fixtures. Errors are never suppressible. (ADR-0010 §5)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-F8-3), scoped to the M-1a subset:*

Each check is an Error. Each violating fixture yields exactly one diagnostic of its id, and `BuiltInContent.CreateM1a()` passes all of them. (PCM-F8-3, M-1a rows)

- [x] **CV-PLR-01:** errors when `player.rangeMm + player.rangeSlackMm > inventory.externalLeashMeters` (as mm). The edge 2 500 + 500 = 3 000 passes.
- [x] **CV-PLR-02:** errors when `swimExitMm > swimEnterMm − 100`.
- [x] **CV-PLR-04:** errors when `minHoldTicks < 6`. A value of 6 passes.
- [x] **CV-PLR-06:** errors when the catch range (`throw.catchRadiusMm`) is ≥ `player.rangeMm`. It is also reachable by its alias `Throwing rule 14` (CDD-RULE-2).
- [x] **CV-PLR-08:** errors when `haulOutMm < stepUpMm`.
- [x] **CV-PLR-09:** errors when `incumbentSlackMm + 100 > rangeSlackMm`.
- [x] **CV-PLR-10:** errors when any of `ticksAccel`, `ticksDecel`, `airControlPm` or `rangeMm` is ≤ 0, or when `player.g` is not a multiple of 30. 24 000 passes and 24 001 errors.
- [x] **CV-PLR-11:** errors when `positionStaleTicks < 19`. A value of 19 passes.

---

## Implementation Notes

*Derived from ADR-0010 §5; text from PC's rule table:*

- **Location:** `Rules/Player.cs`. The alias format is `PC rule N`.
- **Deferred rules.** These stay registered as ids but are not implemented at M-1a; mark each row "inputs not present at M-1a" so the fixture meta-test skips it explicitly:
  - CV-PLR-03 needs `gaitPlaybackMax`, a presentation constant, and Postal Sprint, which is M1.
  - CV-PLR-05: `secondWindMs` is retired and the rule is restated as "a min-wins Set exceeds base" (CDD Edge Cases). It needs PerkDef, which is M1.
  - CV-PLR-07 (`pushCapMm`) is M-1b.
  - CV-PLR-12 needs vehicles, which are later.
  - CV-PLR-13 (= PCM-P10-1, unknown verb) needs the `Verb` enum and interaction catalogue, which are blocked on ADR-0014. Pick it up with the player-character interaction story.
- **Cross-owner rules.** Rules 1 and 6 read keys from two owner files: `player` + `inventory`, and `player` + `throw`. Their stage is 5, and the declared inputs list both keys, so if either key failed the rule is skipped and counted.
- **Leash key name.** PC says `leashMm` and `externalContainerLeashMeters`; Grid's knob is `externalLeashMeters`. Use Grid's registered key and flag the naming to the PC/Grid re-review.

---

## Out of Scope

- Story 012: CV-THR rules.
- M1 (Perks epic): CV-PLR-03 and CV-PLR-05.
- M-1b (player-character epic): CV-PLR-07.
- Player-character interaction story (blocked on ADR-0014): CV-PLR-13.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/PlayerRulesTests.cs` — must exist and pass

**Status**: [x] Created — `tests/unit/content/PlayerRulesTests.cs` (52 cases) + `PlayerRuleFixtures.cs`

---

## Dependencies

- Depends on: Story 003, Story 008
- Unlocks: player-character epic (solver stories rely on these invariants)

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 8/8 passing (deferred rule ids CV-PLR-03/05/07/12/13 registered as `RuleStatus.Deferred`, out of scope by design)
**Deviations** (advisory, logged as TD-064): new append-only `RuleStatus.Deferred = 3` + `RuleSpec.Defer` (registry had no provisional pattern for the meta-test skip); single-owner rules catalogued at stage 2 run at stage 4 (TD-063(1)); CV-PLR-13 alias `PCM-P10-1`; leash key naming (Grid `inventory.externalLeashMeters`, mm) flagged for the PC/Grid re-review; negative CV-PLR-10 inputs and `g ≤ 0` are shadowed by CV-GEN-13 hard ranges (pinned by tests).
**Test Evidence**: Logic: `tests/unit/content/PlayerRulesTests.cs` (52 cases) + `tests/unit/content/PlayerRuleFixtures.cs`; registry tests in `RuleRegistryTests.cs`; four legs green (net10/net8 × Debug 2 677 / Release 2 688, 0 failed; Debug 17 skipped).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (advisories → TD-064); QL-TEST-COVERAGE GAPS → gaps 1/2/4 fixed in one revision round, rest advisory (TD-064(6)).
