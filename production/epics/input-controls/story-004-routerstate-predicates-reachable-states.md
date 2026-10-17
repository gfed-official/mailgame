# Story 004: `RouterState` tuple, live predicates as data and the reachable-state golden list

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~300 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/input-controls.md` (R2 contexts, "Reachable states", named shorthands World / Panel / Holding-open / Holding-closed; AC H-R2-8)
**Requirement**: `TR-input-003` (RouterState tuple; live predicates as data; reachable-state golden list)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§2 router state and the action map) — Accepted 2026-10-16
**ADR Decision Summary**: `RouterState = (capture, overlay, body, holding, aiming, submenu)` packed into a `ushort RouterStateId` (3 × 7 × 4 × 2 × 2 × 3 = 1008 values). Live predicates are data — one bitmask per layer, an action is live iff the current value's bit is set in **every** mask. Reachable states are data too — the R2 clauses as a list of `(antecedent masks → consequent masks)` rules — and the conflict validator and the H-R2-8 golden list read the same data.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: live predicates and reachable states are **data**, never prose or scattered branches; the validator and the golden test read the same instance (ADR-0012 §2)
- Forbidden: Godot types, floats, allocation after construction in predicate evaluation
- Guardrail: `IsLive` is a handful of mask tests, 0 B

---

## Acceptance Criteria

*From GDD R2 and H-R2-8, scoped to this story:*

- [x] **H-R2-8 (golden list).** Enumerating all 3 × 7 × 4 × 2 × 2 × 3 = 1008 tuples through the reachable-state rules yields a set equal to the committed golden file `tests/unit/input/ReachableStates.golden` (one canonical line per reachable `RouterStateId`, sorted); regenerating the file is an explicit opt-in (`PM_UPDATE_GOLDEN=1`), never the default, and a count mismatch prints both sets' difference.
- [x] **H-R2-8 (one negative fixture per clause).** Each clause of GDD R2 "Reachable states" is a data rule with a rejecting fixture: `aiming ⇒ body OnFoot ∧ no overlay ∧ ¬holding ∧ no capture` (fixture: aiming with Panel); `submenu ≠ none ⇒ overlay Panel`; `holding ⇒ body OnFoot` (fixture: holding while Mounted); `overlay Panel ⇒ body OnFoot` (fixture: Panel while Mounted); `overlay History ⇒ body ∈ {OnFoot, Dead}` (fixture: History while Mounted); `body Dead ⇒ overlay ∈ {none, Draft, Results, History} ∧ ¬holding ∧ ¬aiming` (fixture: Dead with Map); `overlay ∈ {Map, Station, Draft, Results, History} ⇒ ¬aiming`. A mutated rule list (one clause removed) changes the reachable set and fails the golden test.
- [x] **Predicates.** `LivePredicate` holds six `ushort` masks (`CaptureMask, OverlayMask, BodyMask, HoldingMask, AimingMask, SubmenuMask`); `IsLive(in LivePredicate, RouterStateId)` is true iff the state's value bit is set in all six; the named shorthands exist as predicates: **World** (no capture, no overlay, OnFoot, ¬holding — includes the aiming sub-state; `WorldNotAiming` is the exclusion), **Panel** (overlay Panel, ¬holding), **Holding-open** (Panel, holding), **Holding-closed** (no overlay, OnFoot, holding). Table-driven test: each shorthand accepts/rejects exactly the expected tuples among the reachable set.
- [x] **State value type.** `RouterState` (readonly struct of the six layers, using the Sim enums from story 002) converts to/from `RouterStateId` via the Sim's `RouterStateInfo`; an unreachable id is representable but `IsReachable(id)` is false; `RouterStateId` equality and a `ToDebugString` for the debug overlay exist.

---

## Implementation Notes

*Derived from ADR-0012 §2 and GDD R2:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `RouterState.cs`, `LivePredicate.cs`, `LayerMasks.cs` (one `const ushort` per layer value, e.g. `OverlayMask.Panel`), `ReachableRules.cs` (an immutable array of `ReachabilityRule { LivePredicate Antecedent; LivePredicate Consequent; }` — an antecedent matching a state requires the consequent to match too), `NamedStates.cs`.
- A rule reads "if the antecedent holds, the consequent must hold". `ReachableSet` is built once at startup (a `bool[1008]` or `ulong[16]` bitset) and used by the validator (story 006) and the router (to assert it never produces an unreachable state under `SIM_ASSERT`).
- Capture-layer and overlay values for later milestones (TextEntry, Map, Station, Draft, Results, History, Build, Mounted, Dead) are part of the **state space and the golden list** now, so the machinery never changes when they gain actions; they simply have no registered action at M-1a.
- The golden file lives next to the tests (`tests/unit/input/ReachableStates.golden`); the test project already compiles `tests/unit/**` — copy the file to the output or locate it from the test assembly's repo root (see how `tests/README.md` describes fixtures).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: `ActionDef` (which carries a `LivePredicate`). Story 006: the conflict validator that iterates the reachable set. Story 008: transitions between states.
- `RouterStateId` packing and `RouterStateInfo` (story 002, Sim side).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/ReachableStatesTests.cs` (`[Trait("Id", "H-R2-8")]`) + `tests/unit/input/ReachableStates.golden`, `tests/unit/input/LivePredicateTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/input/ReachableStatesTests.cs (H-R2-8) + ReachableStates.golden (97 reachable of 1008), LivePredicateTests.cs, RouterStateTests.cs (all pass)

---

## Dependencies

- Depends on: Story 002 (`RouterStateInfo`, layer enums), Story 003 (Client.Core input folder, harness)
- Unlocks: Stories 005, 006, 007, 008

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/4 passing (no deferred items)
**Deviations** (advisory, logged as TD-077): `IsLive` takes `ushort` ids (no RouterStateId type exists); extra file `ReachableSet.cs` (shared `ReachableSet.Default`); the shorthands constrain only the layers the GDD names, so they also match captured states and stories 005/006 must decide capture gating; clause 7 is implied by clause 1, so removing it alone cannot change the set (pinned by a test); `FirstViolation` now throws for ids ≥ 1008 and for a default rule list (LP 1, fixed in-story).
**Test Evidence**: Logic — tests/unit/input/ReachableStatesTests.cs, ReachableStates.golden, LivePredicateTests.cs, RouterStateTests.cs
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (+ .NET pass, 5 advisories; LP 1 fixed); QL-TEST-COVERAGE ADEQUATE (5 low). Remaining advisories go to TD-077.

