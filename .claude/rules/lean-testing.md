---
paths:
  - "src/**"
  - "tests/**"
  - "production/epics/**"
  - ".claude/docs/director-gates/**"
---

# Lean Testing

> Evan, 2026-10-29 8:19 AM PT: "Please also write something up for claude that
> lets us avoid frivolous testing, we should be focused on writing business
> logic". This is a friends-only game: playable-first, no production hardening.
> About 59 % of the C# lines are tests and every CI leg runs ~5,650 cases.

Write the business logic first. Test only what protects behaviour a player or
another system relies on. This rule decides **whether** to write a test;
`test-standards.md` still decides **how** to write one.

## Required

- **One behaviour test per acceptance criterion**, through the system's public
  entry point (op handler, `I…Api`, `SimWorld.Tick`, a pure rule function).
- **One regression test per real bug fixed**, watched failing on the unfixed
  code first (`test-standards.md`).
- Tests where a silent break would reach players: sim determinism, rules and
  economy math, netcode / state sync, save/load and content loading.
- **Tests an Accepted ADR or `docs/architecture/control-manifest.md` requires**,
  including the 0-alloc tests (`AllocAssert`, reported not blocking until M0 per
  the 2026-10-23 E2 errata) and `[PERF]` tests with their thresholds. These and
  the other ADR/manifest tests — goldens and determinism hashes, descriptor and
  `Unsafe.SizeOf` checks (`DescriptorAssert`), `RngNames` ownership rules — are
  **never removable** under this rule; changing them needs an approved erratum.

## Don't write

- Tests of constants, enum values, counter or block counts, or config defaults.
- Tests of getters, setters, constructors, or "not null after `new`".
- Tests that restate the implementation (same formula in the assert as in the code).
- Tests for code nothing calls yet. (The story's own acceptance-criterion entry
  points are exempt: test them even before a caller exists.)
- The same behaviour tested again at another layer (unit **and** integration
  **and** composition).
- `[Theory]` rows past the boundaries: min, max, one inside, one outside, one
  representative. More rows need a named bug they would catch.
- "Pin" tests that freeze a number or list Evan did not ask to freeze
  (e.g. a length pin that every new counter has to bump).
- Tests whose only job is killing a mutant in trivial code. Mutation survivors
  matter only in determinism, money, sync or load code.

## Prefer

- A few behaviour tests through public entry points over many unit tests of internals.
- One parameterised test with boundary rows over many near-copies.
- Golden and determinism-hash tests: keep them, but **one per subsystem**, not
  one per story. Extend the existing golden instead of adding a new one.

## Before adding a test beyond Required, ask

Would a real bug here reach players **and** slip past every existing test?
If not, skip it. (Required tests above are not subject to this question.)

## Deleting tests

Removing a duplicate, hollow or dead-code test is allowed and encouraged in the
same PR that touches it — never a Required test (AC, regression, or ADR /
control-manifest). Name each deleted test and the reason in the PR body.

## Reviewers (QL-TEST-COVERAGE, `/code-review`, Mail Review)

- Judge sufficiency by AC coverage and bug risk, never by test count, line
  coverage, or mutation score on trivial code.
- A "gap" is a missing AC test, a missing regression test, or an untested path
  where a bug would reach players. Anything else is not a gap; don't ask for it.
- Flag tests from the "Don't write" list as removable.

Where an Accepted ADR or a GDD acceptance criterion explicitly mandates a test,
that still binds until Evan approves an erratum; write it as one parameterised
test, not one per item.
