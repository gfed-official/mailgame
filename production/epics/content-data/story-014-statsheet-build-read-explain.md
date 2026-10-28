# Story 014: StatSheet.Build, cached cells, Read and Explain (team + per-slot sheets)

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD13, CD15, CD18 transient terms, CD19, CD20; StatSheet edge cases)
**Requirement**: `TR-content-017` (derived caches; replication is M0), `TR-content-018` (`transientAdd`), `TR-content-019` (`Explain`), `TR-content-028` (`Read` 0 B, advisory)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§7 Faces, Derived cache, Read cost)
**ADR Decision Summary**:
- **Sheets.** There is one team sheet and one personal sheet per `PlayerSlot`. `StatSheet.Build(content, sources)` is a pure function that clients and Presentation can call without a `SimWorld`.
- **Cell cache.** Each `(stat, qualifier, slot)` cell caches its resolved value plus its pre-rounding components.
- **`Read`.**
  - With `transientAdd = 0`, it is one array load.
  - With a transient, it does one Int128 F1 evaluation from the cached components.
  - It never walks modifiers and allocates 0 B.
- **`Explain`** returns `{resolved, unclamped, clampedAt, sources}`.

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `StatSheet.Build(content, sources)` is a pure function. Sheets are rebuilt when a source is added or removed and cache resolved values plus pre-rounding components. `IStatSheet.Read` is one array load (or one `Int128` F1 with a transient) and allocates 0 B. An Int64 fast path is built only if CDD-PERF-2 demands it, and must be proven bit-identical. (ADR-0010 §7)
- Forbidden: letting `IStatSheet.Read` iterate modifiers or allocate. (ADR-0010)
- Forbidden: replicating resolved sheets or modifier events; source lists are replicated instead. (ADR-0010 Alt 6)
- Guardrail: sheet rebuild ≤ 0.2 ms (8 players × 5 perks) and `IStatSheet.Read` 0 B. (Performance Guardrails, advisory)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] With no sources, `Read` returns `clamp(base)` for every M-1a stat and qualifier, and `Explain` reports `clampedAt = none` on `BuiltInContent`. Examples: `MailMaxStack[letter]` = 20, `InventoryRows[player_inventory]` = 2, `LateValueRatioPm` = 500. (StatSheet edge case "no sources")
- [x] Long Legs-shaped synthetic sources (three personal Adds of 120 on `MoveSpeedAddPm`) behave as follows:
  - `Read(…, transientAdd: 300)` = 400, and `Explain` = unclamped 660 with `clampedAt` max;
  - with `transientAdd` 0 the value is 360.

  (CDD-STAT-8, numeric half)
- [x] Scope:
  - a personal or `both` read merges the team modifiers with that slot's modifiers;
  - a team-only stat ignores the slot;
  - a slot with no player reads team + base only;
  - an unqualified modifier on a qualified stat applies to every qualifier;
  - a qualified modifier applies only to its `DefIndex`.

  (CD11, CD15; StatSheet edge cases)
- [x] 1 000 000 `Read` calls allocate 0 bytes (`GC.GetAllocatedBytesForCurrentThread`), both with and without a transient. A structural test asserts that `Read`'s implementation type holds no reference to the modifier pool, so its cost is independent of the live modifier count. (CDD-ID-4 `Read` half; CDD-PERF-2 structural clause)
- [x] Invalid reads trip `SIM_ASSERT` in Debug and return a deterministic value in Release:

  | Read | Release returns |
  |---|---|
  | Qualified stat with qualifier `0xFFFF` or a wrong-kind id | `clamp(min)` |
  | Slot out of range | the team-only value |
  | Negative `transientAdd`, or a transient on a stat that disallows Add | the value with the transient treated as 0 |

  (StatSheet edge cases)

---

## Implementation Notes

*Derived from ADR-0010 §7 and Key Interfaces:*

- **Location:** `src/PerformativeMail.Sim/Content/Stats/`. Files: `IStatSheet.cs`, `StatSheet.cs` (static `Build`), `StatSheetCache.cs`, `StatSources.cs`, `Modifier.cs` (`readonly record struct`) and `StatExplain.cs`.
- **`IStatSheet`**:
  - `int Read(Stat stat, ushort qualifier = 0xFFFF, PlayerSlot? slot = null, int transientAdd = 0)`
  - `StatExplain Explain(...)`
  - `bool Has(RuleFlag flag, PlayerSlot? slot = null)`

  `Has` is wired in story 015. The exact parameter shapes are finalised in code review; the return type is `int` (ADR-0010 note).
- **Cells.**
  - A flat array is indexed by `(stat, qualifierOrdinal, sheet)`, where sheet = team plus 8 personal sheets. The array is sized at build from `|Stat|`, the qualifier table sizes and 9 sheets.
  - Each cell stores `resolved`, `baseTimesP` (Int128), `k`, `sumA`, `hasSet`/`setValue`, `min`/`max` and `rounding`. These are the components from story 007's resolver.
  - A transient read re-runs F1 from these components only.
- **Sources (`StatSources`).**
  - The input is the authoritative source list: team list + personal lists keyed by `profileId`, with a `profileId → slot` map.
  - At M-1a nothing writes sources, so tests build synthetic sources. Each `Modifier` is `{sourceKind, sourceId (DefIndex), stackIndex, ownerSlot|Team, stat, qualifier|0xFFFF, op, value, lifetime}` (CD13).
- **`Explain`** lists the contributing sources. `StatExplain` is a struct, and the source list is exposed as a span over a cache-owned buffer, so it allocates nothing on the hot path. Explain is a UI/debug path, not per tick, but keep it allocation-light.
- **No Int64 fast path** (ADR-0010 §7).

---

## Out of Scope

- Story 015: `IStatWriter`, source identity and versioning, lifetimes, `RuleFlag` `Has`, profile→slot remap, and composition wiring.
- M0 (network epic): replication of source lists (TR-content-017).
- The latch-at-op-start behaviour (CDD-STAT-9/12) is a property of the reading systems' stories (Throwing, Manual Delivery, Grid). They cite CD20.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/content/StatSheetReadTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/content/StatSheetReadTests.cs (45 methods / ~53 cases), passing on four legs

---

## Dependencies

- Depends on: Story 006 (`StatDef`), Story 007 (F1 components), Story 008 (`ContentSet` for base resolution). Also `production/epics/simulation-core/story-002-id-value-types.md` (`PlayerSlot`).
- Unlocks: Story 015

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (none deferred)
**Deviations**: Advisory, accepted by LP — `Has(RuleFlag)` omitted from `IStatSheet` (story 015); `StatSources` slot-keyed (`OwnerSlot`, `PlayerSlot.None` = team; profile→slot map in 015); `StatContribution` struct + `StatExplain.TransientAdd`; `Build` asserts and skips inapplicable modifiers (2017); qualifier on an unqualified stat asserts in Debug (2018), ignored in Release; `StatResolve` gained `ResolveWithTransient` (shared private `Finish`); `ContentAssertCode` 2012–2018 appended. Out-of-scope touch: `tests/unit/content/DefIndexTests.cs` (assert-code count 12→19). Remaining advisories → TD-082.
**Test Evidence**: Logic: tests/unit/content/StatSheetReadTests.cs. Four legs green (net10/net8 × Debug/Release: 3 856 tests/leg, 0 failed; Debug 30 skipped per TD-008).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS → blocking `Locate` length/CC split + advisories 3/5/6/7 fixed in one revision round; QL-TEST-COVERAGE ADEQUATE (gaps 1/2 fixed). C#-specialist folded into the LP gate.
