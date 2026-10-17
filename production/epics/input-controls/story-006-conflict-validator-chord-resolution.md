# Story 006: Conflict validator over reachable states, most-specific-chord resolution and the verb-grammar table

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/input-controls.md` (R4 the two binding rules, R10 "Conflict = same physical input on two actions whose live predicates can both be true…", Edge Cases "chord and its plain key", "bind Throw and Interact to the same key"; ACs H-R4-1, H-R4-5, H-FX-3a pad-path clause, H-EC-15 logic)
**Requirement**: `TR-input-006` (conflict validator over reachable states; most-specific chord wins); R4.2 verb-grammar table has no TR-ID in the registry (cited by GDD rule R4.2 / AC H-R4-5)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§2 "Conflict validator (R10)") — Accepted 2026-10-16
**ADR Decision Summary**: For every reachable `RouterState` × physical input (modifiers included, after most-specific-chord-wins, Back counted once) there is at most one live action, and no co-live Tap + Hold on one input except Interact (H-R4-1). The validator runs at load (M-1a, defaults) and on every remap (M1), is O(reachable states × bindings), takes < 5 ms at load and sits outside the frame budget.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: Godot 4.7.1 matches modifiers non-exactly by default (GDD R10, godot-specialist) — one more reason the router resolves chords itself and never uses `InputMap`.

**Control Manifest Rules (Foundation)**:
- Required: one meaning per binding per live state; one grammar per binding per target type; the validator and runtime chord resolution use the **same** function (ADR-0012 §2, §3)
- Forbidden: Godot `InputMap` modifier matching for game verbs; allocation in `Resolve` (runtime path)
- Guardrail: validation < 5 ms at load (advisory); `Resolve` is 0 B

---

## Acceptance Criteria

*From GDD R4, R10 and H-R4-1 / H-R4-5 / H-FX-3a, scoped to this story:*

- [x] **H-R4-1 (table-driven).** For every reachable `RouterState` (story 004) × every physical input bound anywhere in the map (modifiers included, after most-specific-chord resolution, Back counted as one action), the number of live actions is ≤ 1 and no input carries a co-live Tap action and Hold action **except Interact**; each violation is reported as `(state, input, actions)`. The M-1a `BuiltInActionMap` reports zero violations. A planted fixture (Drop bound to E) reports `(World, E, [Interact, Drop])`; another (Pick/place secondary = Enter while Chat-like tap shares it in a co-live state) is reported. Shared-between-disjoint-states bindings are legal and produce an `Info` entry ("Also Pick up in Panel — they never overlap"): A = Jump in World and Pick/place in Panel, Space on both.
- [x] **Chord resolution.** `ChordResolver.Resolve(heldModifiers, pressedInput, state, map)` returns the most specific live action: Shift+Enter → quick-move beats Enter → pick/place; Shift+LMB in Panel → quick-move beats LMB → pick/place; the plain action does not also fire. A chord whose plain key is bound in the same state is **not** a conflict (resolved), but two actions bound to the same modifier + key in a co-live state are. Modifier state is read from raw held input, never from mute-filtered state (the runtime use is story 007). Holding Shift for sprint in World never blocks E (no chord shares E).
- [x] **Binding-shape rules.** Chords are modifier + one input on KBM only; a pad chord or a KBM chord of two non-modifier keys is rejected by the validator; WASD (movement) and arrows (grid cursor) never share a key; Esc is bound only to Back (and Pause as its ladder end); Menu only to Pause. (H-EC-15: Throw and Interact bound to one key → violation in World.)
- [x] **H-R4-5 (verb-grammar table).** `VerbGrammarTable` (data) maps each interactable target type to exactly **one** verb with **one** grammar — container, vehicle seat, turret, shop counter, console, construct (configure), WorldItem → Tap; mailbox / destination → Deliver hold; inbound catchable item → Catch (held state, armed) — and `Validate` rejects a fixture with two verbs for one target type, naming it. (The check against Player Character's registered v1 anchors is part of story 020.)
- [x] **H-FX-3a (pad-path clause).** Every registered action has a pad binding or a declared pad path (`PadPath`: a pad binding of another action or "action menu" for Y-menu verbs) except an explicit exemption list held as data — Chat (no pad text entry in v1, OQ-IC-5), Push-to-talk, Read prompt — none of which is registered at M-1a; the test asserts the exemption list is exactly those three and that an unregistered-but-exempt entry does not hide a missing pad binding on a registered action (fixture: remove Jump's pad binding → report).
- [x] **Report type.** `ValidationReport` carries `Errors`, `Warnings`, `Infos` (each with action ids, states, inputs and a stable code, e.g. `CONFLICT_COLIVE`, `TAP_HOLD_SHARE`, `CHORD_SHAPE`, `GRAMMAR_TARGET`, `PAD_PATH`), is serialisable to text for logs, and `Validate(map, reachable)` allocates only at load (not on the runtime `Resolve` path).

---

## Implementation Notes

*Derived from ADR-0012 §2 and GDD R10:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `ActionMapValidator.cs` (extend story 005's field checks with the reachable-state pass), `ChordResolver.cs`, `ValidationReport.cs`, `VerbGrammarTable.cs`, `PadPathTable.cs`.
- Algorithm: for each reachable state id, for each binding `(modifier?, input)` of each live action, bucket by `(modifier set, input)`; resolve chords first (a bucket with modifier ⊃ another bucket's modifier on the same input shadows it **only when both are live in the state**); then count. O(reachable × bindings); do not enumerate all 1008 tuples per binding.
- Interact is the one allowed Tap + Hold input: it is *one action* whose grammar the target decides (R4.2), so the Tap + Hold rule is checked between **different** actions, and the grammar table checks the per-target-type rule.
- Back is a single action (Esc / B), so Cancel aim / Close panel / Cancel-return / Pause-via-Esc are not separate bindings and cannot self-conflict; RMB is a **separate** Cancel action live only in Holding-open (R6) and is checked like any other.
- The remap UI messages ("E is already Interact (On foot). Swap · Replace · Cancel") are M1; this story produces the data (action names, state names) they will format. Internal state names (Dead, OnFoot, Holding-open / -closed) never reach players — keep a `PlayerFacingGroup` mapping for the M1 screen out of scope here.
- Run the validator from a test over the built-in map **and** expose it for the boot path (story 028 calls it and refuses to start on errors).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 007: using `ChordResolver` at runtime. M1: the remap screen, Swap / Replace flows, reset / undo, H-R10-* messages, profile-load collision rules (H-EC-9).
- Verifying PC's registered anchors against `VerbGrammarTable` (story 020).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/ConflictValidatorTests.cs` (`[Trait("Id", "H-R4-1")]`), `tests/unit/input/ChordResolverTests.cs`, `tests/unit/input/VerbGrammarTableTests.cs` (`[Trait("Id", "H-R4-5")]`), `tests/unit/input/PadPathTests.cs` (`[Trait("Id", "H-FX-3a")]`) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — ConflictValidatorTests, ChordResolverTests, VerbGrammarTableTests, PadPathTests (+ InputFixtures)

---

## Dependencies

- Depends on: Story 004 (reachable set, predicates), Story 005 (`ActionMap`, `BuiltInActionMap`)
- Unlocks: Stories 007 (runtime chord resolution), 023, 028 (boot validation); M1 remap screen

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 6/6 passing (all covered by automated tests; built-in M-1a map: 0 errors, 0 warnings)
**Deviations**: ADVISORY only, logged as TD-088. Decisions: Back-ladder step bindings (Pause KBM Esc, Close panel pad B, Cancel/return pad B) carry `InputBinding.WithBackLadderStep()` and count as Back; a pad ladder step on Pause is rejected (R5.6); a step owner must never be live where Back is not. Every ActionMap passes the field and reachable passes (`TryLoad`, `CreateM1a`, `ValidateForBoot`). `ChordResolver` index built once in TryLoad; `TryResolve(held, pressed, stateId, out ChordMatch)` + static `Resolve` share one 0 B routine. Extra codes: RESERVED_INPUT, MOVE_CURSOR_SHARE, LADDER_STEP, SHARED_DISJOINT (10 pinned). Extra files: ModifierMask.cs, ValidationSeverity.cs, ValidationCodes.cs, ChordMatch.cs, ActionMapValidator.Reachable.cs, InputFixtures.cs.
**Test Evidence**: Logic — tests/unit/input/ConflictValidatorTests.cs (H-R4-1), ChordResolverTests.cs, VerbGrammarTableTests.cs (H-R4-5), PadPathTests.cs (H-FX-3a). net10 Debug full suite 4,024 / 0 failed (excl. MailDestinations); net10/net8 Release and net8 Debug 3,641 / 0 failed (excl. in-flight MailDestinations + PlayerCharacter tests).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (+.NET) → fixed in-story; QL-TEST-COVERAGE GAPS → fixed in-story.
