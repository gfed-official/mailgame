# Story 017: Builder validation — SimBuildException codes for ops, events and readers

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (≈ 0.5 day; ≈ 250 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R4 binding: build-time errors throw `SimBuildException{Code}`; tables frozen at `Freeze()`)
**Requirement**: `TR-sim-012` (`SimWorldBuilder` binds phases/ops/events/readers/contributors pre-`Freeze`; violations throw `SimBuildException`)
*(Requirement text: `docs/architecture/tr-registry.yaml` TR-sim-012 "SimWorldBuilder binds phases/ops/events/readers/contributors pre-Freeze; violations throw SimBuildException")*

**ADR Governing Implementation**: ADR-0003: Phase Order, Composition Root and Cross-System Calls (§3) — primary; ADR-0007 (§4 descriptor build errors), ADR-0008 (§3–§4 descriptors, ≤ 8 readers) — secondary
**ADR Decision Summary**: `SimWorldBuilder` is usable only while `Constructed`; `Freeze()` validates every table and throws `SimBuildException { Code }` — the only place the Sim throws by design, at boot, never in a tick. Codes include `DuplicateOpHandler`, `MissingOpHandler`, `ReaderLimit`, `BindAfterFreeze`, `ReservedPhase`, `DependencyCycle`, `UndeclaredDependency`, `DuplicateSystem`, plus descriptor-validation codes ADR-0007/0008 may add.
**ADR Version**: 2026-10-16 (ADR-0003, ADR-0007, ADR-0008)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None. (The Godot `GameLoop` catches `SimBuildException` at boot — godot-host-shell epic.)

**Control Manifest Rules (Foundation)**:
- Required: `SimWorldBuilder` used only in `Constructed` (`Bind`, `BindTickEnd`, `BindOp(OpDescriptor, IOpHandler)`, `BindEventKind(EventDescriptor)`, `BindEventReader(ReaderId, EventKind[])` ≤ 8, `BindStateContributor`, `BindSnapshotSource`); build errors throw `SimBuildException { Code }` with the ADR-0003 §3 codes + descriptor codes; every Sim build-time table is frozen at `Freeze()`.
- Forbidden: throwing `SimBuildException` inside `Tick()`; runtime add/remove of handlers, kinds or readers after `Freeze()`.
- Guardrail: none (boot-time only).

---

## Acceptance Criteria

*From GDD SC-R4-1 (rows 1–4; row 5 "dependency cycle" is Story 025) and ADR-0007 §4 / ADR-0008 §3, scoped to this story:*

- [x] **AC-1 SC-R4-1 rows 1–4.** One Theory row each, each throwing `SimBuildException` with its own `Code`: a second handler bound for one `OpKind` → `DuplicateOpHandler`; a declared `OpKind` with no handler → `MissingOpHandler` (at `Freeze()`); a 9th `BindEventReader` → `ReaderLimit`; any `Bind…` call after `Freeze()` → `BindAfterFreeze`.
- [x] **AC-2 Descriptor codes.** `Freeze()` (or the bind call) rejects, each with its own code: an op or event descriptor whose `PayloadLength > 48`; an op descriptor declaring a system reason below 64; a second `EventDescriptor` for the same `EventKind`; an `OpKind`/`EventKind` of 0 (`None`). A valid build containing one op kind, one event kind, one reader and phase binders freezes without throwing, and no `SimBuildException` can be raised once the world is `Ready` (all build APIs are unreachable from the frozen world).

---

## Implementation Notes

*Derived from ADR-0003 §3, ADR-0007 §4, ADR-0008 §3–§4:*

- Extend `SimBuildErrorCode` (Story 011 seeded `ReservedPhase`, `BindAfterFreeze`) with `DuplicateOpHandler`, `MissingOpHandler`, `ReaderLimit`, `DuplicateEventKind`, `PayloadTooLarge`, `SystemReasonBelow64`, `InvalidKind`; Story 025 appends `DependencyCycle`, `UndeclaredDependency`, `DuplicateSystem`. Explicit values, append-only.
- **Representing "an `OpKind` with no handler".** ADR-0007's `BindOp(OpDescriptor, IOpHandler)` pairs them, so a missing handler needs a declaration that is not a binding. Recommended: systems (and the composition profile) *declare* the op kinds they own via `DeclareOp(OpDescriptor)`, and `BindOp(descriptor, handler)` both declares and binds; `Freeze()` throws `MissingOpHandler` for a declared kind with no handler (also for `BindOp(d, null!)`). Keep the public surface ADR-shaped; record the choice in the story's completion notes.
- `SimWorldBuilder` sets an internal `_frozen` flag in `Freeze()`; every `Bind*` checks it first (`BindAfterFreeze`). `Freeze()` returns the `SimWorld`; the builder must not be reusable to mutate the frozen tables (copy to arrays).
- Op handler lookup after freeze: a dense array indexed by `(ushort)kind` (u16 → 65 536 entries × 8 B = 512 KiB is too large) — use a sorted `ushort[]` of kinds + parallel handler/descriptor arrays with binary search, or per-block arrays. No `Dictionary` iteration; a lookup-only dictionary is permitted by ADR-0006 rule 3 but array search is preferred for 0-alloc clarity.
- The 8-reader limit is `SimCoreSettings.MaxEventReaders` (default 8).

---

## Out of Scope

- Story 011: `ReservedPhase` and `BindAfterFreeze` for phase binds (already covered; this story re-asserts `BindAfterFreeze` for every bind kind).
- Story 025: `DependencyCycle`, `UndeclaredDependency`, `DuplicateSystem` (composition graph) and SC-R4-1 row 5.
- SC-SET-1 (M-1b): `SimSettings` F8 constraint codes.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/BuilderValidationTests.cs` (`[Trait("Id", "SC-R4-1")]`) — must exist and pass

**Status**: [x] Created and passing — `tests/unit/sim/Core/BuilderValidationTests.cs` (`[Trait("Id","SC-R4-1")]` Theory 4 rows + 14 facts); `EventLogTests.Ac2_BindEventKind_InvalidDescriptors_Throw` updated

---

## Dependencies

- Depends on: Story 013 (op descriptors), Story 016 (readers)
- Unlocks: Story 020, Story 025

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 2/2 passing (AC-1 → BuilderValidationTests `Ac1_BuildViolation_ThrowsItsOwnCode` Theory [SC-R4-1] rows DuplicateOpHandler/MissingOpHandler/ReaderLimit/BindAfterFreeze + `Ac1_EveryBindSurfaceAfterFreeze_ThrowsBindAfterFreeze`, `Ac1_BindOpWithNullHandler_FreezeThrowsMissingOpHandler_AndALaterHandlerFixesIt`; AC-2 → `Ac2_OpDescriptorPayloadOver48…`, `Ac2_EventDescriptorPayloadOver48…`, `Ac2_OpDescriptorSystemReasonBelow64…`, `Ac2_SecondEventDescriptorForOneKind_ThrowsDuplicateEventKind`, `Ac2_OpKindNone…`/`Ac2_EventKindNone…`, `Ac2_ValidBuild_FreezesAndTicksWithoutThrowing`, `Ac2_FrozenWorld_ExposesNoBuildSurface`, `Ac2_SimBuildErrorCode_ValuesArePinned`)
**Verification run**: closer re-ran all four legs from disk (after the closer's BindEventReader split) — 1 459 tests/leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 8 skipped = pre-existing optimised-only alloc tests).
**Files**: SimBuildException.cs (codes 8 DuplicateOpHandler, 9 MissingOpHandler, 10 PayloadTooLarge, 11 SystemReasonBelow64, 12 InvalidKind; InvalidEventDescriptor narrowed to undefined class / resync scope > 63), SimWorldBuilder.cs (DeclareOp, BindOp, ValidateOp/EventDescriptor, FreezeOps, Freeze rewrite; BindEventReader split into ValidateReader + SortReaderFilter by the closer — Story 016 leftover over 40 lines), SimWorldParts.cs (OpKinds/OpDescriptors/OpHandlers), SimWorld.cs (internal `TryGetOp(OpKind, out OpDescriptor, out IOpHandler)` binary search, 0 B — for the Story 020 drain); tests BuilderValidationTests.cs (new), EventLogTests.cs.
**Design choice (DeclareOp)**: `DeclareOp(OpDescriptor)` declares a kind without a handler; `BindOp(d, h)` declares + binds; `BindOp(d, null!)` declares only. MissingOpHandler at `Freeze()`; Freeze validates before setting `_frozen`, so a failed freeze is retryable. DuplicateOpHandler at the bind call (also for a different descriptor instance for an already-declared kind). Descriptor validation at the bind call. Op table frozen to a sorted `ushort[]` + parallel arrays (insertion sort).
**Deviations**: ADVISORY — event kind None / payload > 48 now throw InvalidKind / PayloadTooLarge (were InvalidEventDescriptor in Story 015) to satisfy "each with its own code"; BindStateContributor/BindSnapshotSource do not exist yet (not added); no `handler.Kind == descriptor.Kind` check (no code defined → TD-042, Story 020).
**Gates**: QL-TEST-COVERAGE ADEQUATE; LP-CODE-REVIEW CONCERNS (0 blocking; 6 advisories — #4 BindEventReader length fixed by closer, rest → TD-042). LP could not run tests (global.json SDK without mise); closer ran them.
**Test Evidence**: Logic: tests/unit/sim/Core/BuilderValidationTests.cs
**Code Review**: Complete (LP-CODE-REVIEW CONCERNS, advisories logged)
**Run result**: N/A — Godot-free Sim logic, no observable surface; verified by the four-leg suite run above.
