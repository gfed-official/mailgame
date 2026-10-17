# Story 001: DefIndex assignment, StringNamespace def members and DefTable — ordinal content ids

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S (~0.5 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD5, F4)
**Requirement**: `TR-content-007`, `TR-content-008`, `TR-content-009`
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A and is mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0004: Identity and Id Spaces (primary, §6); ADR-0010: Content Format, Loading, ContentSet and StatSheet (§3)
**ADR Decision Summary**: `DefIndex` is a `ushort` ordinal of the def id within its type's `StringComparer.Ordinal`-sorted id list. It uses `+1` storage, so `default` is None and `Value` reads `0xFFFF` for None. At most 65 534 defs are allowed per type. `DefIndex` is never persisted. `StringId` is `(StringNamespace ns, ushort index)`, with one namespace per def type plus `Run`.
**ADR Version**: ADR-0004 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 (story code is the Godot-free `net8.0` Sim) | **Risk**: LOW
**Engine Notes**: None. The code is plain BCL. Id structs must never cross into Godot Variants (Presentation passes `.Value`). That boundary is not touched here.

**Control Manifest Rules (Foundation)**:
- Required: `DefIndex` = ordinal of the def id in its type's `StringComparer.Ordinal`-sorted list (≤ 65 534 per type); `StringId = (StringNamespace ns, ushort index)`. Per-tick code looks up by `DefIndex` into typed tables, never by string. (ADR-0004 §6, ADR-0010 §3)
- Required: `PlayerSlot` and `DefIndex` use `+1` storage so `default` is None. `DefIndex.Value` reads `0xFFFF` for None, and `FromValue(0xFFFF)` special-cases None. (ADR-0004 §1, §6)
- Required: every id struct implements `IEquatable<T>`, `Equals(object)`, `GetHashCode()` and `==`/`!=` explicitly. `StringId` hashes as `((int)Ns << 16) | Index`. No `System.HashCode` is used. (ADR-0004)
- Forbidden: a `DefIndex` whose `default` means ordinal 0. Never persist `DefIndex`. Never order or tie-break by `GetHashCode()`. (ADR-0004 Alt 4)
- Forbidden: culture-default compares, or `ToLower`/`ToUpper`, on ids (ordinal only). (ADR-0010)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] Regression guard over the id structs owned by `production/epics/simulation-core/story-002-id-value-types.md`: `default(DefIndex)` is None, and a `DefTable<T>` indexed with an assigner-produced `DefIndex` returns the def at that ordinal. The content code never redeclares the struct. (CDD-ID-3, last clause; the primary test is sim-core story 002 AC-2.)
- [x] The ordinal assigner, given the six mail-kind ids in shuffled input order (fixed seed), yields cargo 0, large 1, letter 2, medium 3, postcard 4, small 5. Given `bike_cargo`, `bike2`, `bike`, it yields bike 0, bike2 1, bike_cargo 2. (CDD-ID-2 as a unit test. The two-OS CI form runs once the Windows job exists.)
- [x] 65 534 ids in one type are assigned 0…65 533 and `0xFFFF` is never assigned. 65 535 ids return a "too many defs" result carrying the count, and no indices are assigned. Story 018 maps this result to CV-GEN-09. (CDD-ID-3)
- [x] Namespace separation: adding the mail kind `aaa` changes no item's `DefIndex`. Two `StringId`s with equal index but different namespaces are unequal. `StringId.GetHashCode() == ((int)Ns << 16) | Index`. (CDD-ID-3, CD5.3)
- [x] `StringNamespace` gains the M-1a def-type members `Item = 1`, `Container = 2`, `MailKind = 3`, `DestinationType = 4` and `MailMix = 5`, which equal the `DefType` values; `None = 0` and `Run = 0xFF` are unchanged. `DefTable<T>` exposes `this[DefIndex]` and `Count`, and has no string-keyed member. (TR-content-009; first half of the CDD-ID-4 API-shape clause)

---

## Implementation Notes

*Derived from ADR-0004 §6 / Key Interfaces and ADR-0010 §3:*

- **Location.** `DefIndex`, `StringId` and `StringNamespace` (with `None`/`Run`) are built by `production/epics/simulation-core/story-002-id-value-types.md` in `src/PerformativeMail.Sim/Primitives/`. That story leaves "`DefIndex` assignment and `StringNamespace` def members" to this epic. This story adds the def-type members, plus `DefType`, `DefTable<T>` and the ordinal assigner in `src/PerformativeMail.Sim/Content/`.
- **`StringNamespace`** is a `byte` enum. It has one member per def type, with the same numeric value as the content `DefType` enum, plus `Run = 0xFF` and `None = 0`. `DefType` is the append-only def-type order used by the canonical encoding (F6, story 013). The M-1a members, in this order, are `Item = 1`, `Container = 2`, `MailKind = 3`, `DestinationType = 4`, `MailMix = 5`. Later types are appended and never reordered.
- **The assigner** sorts with `StringComparer.Ordinal`; ids are validated ASCII, so this equals UTF-8 byte order. It never uses a culture-default comparison. Input is a span or `ImmutableArray<string>` of ids for one type; output is the sorted ids plus their ordinals. Uniqueness (CV-GEN-01) and the id regex (CV-GEN-02) are checked before the assigner runs (stories 009 and 018). The assigner asserts (`SIM_ASSERT`) that its input is unique.
- **`DefTable<T>`** wraps an `ImmutableArray<T>` in `DefIndex` order. Hot paths use `AsSpan()`. Indexing with None is a `SIM_ASSERT` failure. Typed tables (`DefTable<ItemDef>`) are ADR-0010's choice; this story uses them.
- **Persistence.** `DefIndex` is never persisted (TR-content-008). Do not add a `System.Text.Json` converter or a serialisation attribute for `DefIndex`. ADR-0024 persists string ids.

---

## Out of Scope

- Story 018: CV-GEN-01 (duplicate ids) and CV-GEN-02 (id regex) diagnostics during the schema walk, and mapping the "too many" result to CV-GEN-09.
- Story 013: the `DefType` order inside the canonical encoding.
- Run-time string interning under `StringNamespace.Run` is owned by the system that generates run strings (World Generation / Mail). It is not part of M-1a here.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/DefIndexTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/content/DefIndexTests.cs (30 methods / 36 cases); suite 717/717 green on all 4 legs (net10/net8 × Debug/Release; Debug 5 skipped)

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (test csproj, `SIM_ASSERT`) and `production/epics/simulation-core/story-002-id-value-types.md` (`DefIndex`, `StringId`, `StringNamespace`).
- Unlocks: Story 005, Story 013, Story 018

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (CDD-ID-2 two-OS CI form deferred until a Windows CI job exists, as the story states)
**Deviations** (advisory, accepted): (1) assert codes live in `Content/ContentAssertCode.cs` block 2000–2999 (Sim < 1000, Client.Core 1000–1999), not `SimAssertCode`; (2) `DefAssignmentStatus` adds `InvalidId`/`DuplicateIds` beside `TooManyDefs` (precedence TooManyDefs → InvalidId → DuplicateIds; Debug SIM_ASSERTs) — story 018 must map each distinctly; (3) `DefTable<T>` indexer returns `ref readonly T`; Release None/out-of-range throws `IndexOutOfRangeException` (no valid ref to return) — loader/story 005 must guarantee no None reaches it; (4) extra `DefAssignment.IndexOfInput` output. Logged as TD-019.
**Test Evidence**: Logic: `tests/unit/content/DefIndexTests.cs`
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (blocking assert-code collision with `NetAssertCode` 1000 fixed → 2000 block; advisories fixed: Release null/empty-id status, one type per file, tightened reflection/JSON guards); QL-TEST-COVERAGE ADEQUATE.
