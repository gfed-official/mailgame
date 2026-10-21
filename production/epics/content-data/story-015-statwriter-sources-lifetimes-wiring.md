# Story 015: IStatWriter, source identity and versioning, lifetimes, RuleFlags, and composition wiring

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD14, CD15, CD16, CD17, CD18; StatSheet edge cases; States "StatSheet (per run, per peer)")
**Requirement**: `TR-content-016`, `TR-content-017` (rebuild-on-source-change), `TR-content-018` (`RuleFlag` on while ≥ 1 source)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§7 Faces, Derived cache) (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4 injectable list)
**ADR Decision Summary**:
- **Faces.** `IStatSheet` is injectable anywhere. `IStatWriter` (push/remove by source) is constructor-injected **only** into Perks & Perk Draft and Stamps & Run Events; a reflection test enforces this. Meta-progression never pushes modifiers.
- **Rebuild.** The sheet is rebuilt whenever a source is added or removed, so every read in a tick sees one sheet version.

**ADR Version**: ADR-0010 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `IStatWriter` is constructor-injected only into Perks & Perk Draft and Stamps & Run Events (reflection test). Meta-progression never pushes modifiers. (ADR-0010 §7, ADR-0003 §4)
- Required: collections are pre-sized from `SimSettings`. The modifier pools come from `stats.modifierPoolPerTeam`/`PerPlayer`, and there is no growth after warm-up. (ADR-0006 §8, ADR-0010 §7)
- Required: a system's public constructor takes only types from the closed injectable list. `IStatSheet` is available to any system that reads stats. (ADR-0003 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] **Source identity and versioning.**
  - **Identity** is `(sourceKind, sourceId, stackIndex, ownerSlot)`.
  - **Push** of a duplicate identity, or of a `stackIndex` ≥ the source's `maxStacks`, returns false and leaves the sheet and its version unchanged.
  - **Remove** of a never-pushed source returns false and leaves the version unchanged.
  - **Version** rises by exactly 1 per successful add or remove. No `Read` ever changes it.

  (CDD-STAT-5 versioning; StatSheet edge cases)
- [x] **Three build paths agree.** For identical source lists, sheets built from scratch, built incrementally through `IStatWriter`, and built client-side via the pure `StatSheet.Build` without a `SimWorld` return equal `Read`s for every (stat, qualifier, slot). (CDD-STAT-5)
- [x] **Shift-lifetime source.** A Shift-lifetime source (synthetic `MailValueMultPm` Mul 900, team) reads 900 before its `Remove` and 1 000 after it. With no `Remove`, a read 10 000 ticks later still returns 900: the sheet has no timers. (CDD-STAT-6, adapted to an M-1a stat because `ShiftEarningsMultPm` is not in the M-1a enum)
- [x] **Stacks and RuleFlags.** Three stacks (`stackIndex` 0, 1, 2) of a personal Add 120 on `MoveSpeedAddPm` read 360 (CDD-STAT-7, sheet half). A `RuleFlag` held by two sources stays on after one is removed and turns off after both are removed (CDD-STAT-8, flag half).
- [x] **Profile remap.** A personal pick keyed by `profileId` P applies to P's new slot after the `profileId → slot` map changes. The old slot then reads team + base only. (CDD-STAT-13)
- [x] **Composition and architecture tests.**
  - `SimComposition.Build` creates one sheet per world, sized from `SimSettings`, and passes `IStatSheet` to systems that declare it.
  - In an M-1a world every stat reads its base.
  - An architecture test finds `IStatWriter` in no `[SimSystem]` constructor other than Perks or Stamps; at M-1a that means none.
  - `StatSheet.Build` takes no profile, rank or meta input.
  - The only `SourceKind` members are Perk, Stamp and RunEvent.

  (CDD-STAT-4, M-1a form)

---

## Implementation Notes

*Derived from ADR-0010 §7 and ADR-0003 §4:*

- **`IStatWriter`**:
  - `void Push(in ModifierSource src, ReadOnlySpan<Modifier> mods)`
  - `void Remove(in ModifierSource src)`

  Return `bool` for the refusal cases in the criteria; the exact shapes are finalised in review. `ModifierSource` carries `maxStacks` because no PerkDef exists at M-1a. When PerkDef lands (M1), `maxStacks` is read from the def.
- **Pools.** Use fixed-capacity pools sized from `SimSettings.Stats.ModifierPoolPerTeam`/`PerPlayer` (128/64, PROPOSED). A push beyond capacity is refused and counted in `SimCounters`; Sim Core R32 forbids allocating on the hot path.
- **Rebuild.**
  - Rebuild happens immediately inside `Push`/`Remove`. It resolves only the affected (stat, qualifier) cells, or the whole sheet if simpler; either way it stays within the 0.2 ms advisory budget.
  - It completes before `Push` returns, so readers never see a dirty state.
- **RuleFlags.** Keep a per-sheet count of live sources holding each flag. `Has` returns count > 0.
- **Lifetimes.** `Run` and `Shift` are tags only. The pushing system removes Shift sources at Payday (CD16). There is no expiry logic in the sheet.
- **Composition.** The sheet instance is per-world and is created in `SimComposition`; no statics. `IStatSheet` is on ADR-0003 §4's injectable list, and the simulation-core composition reflection test already checks that list. This story adds the `IStatWriter`-specific assertion.

---

## Out of Scope

- Perks & Perk Draft and Stamps & Run Events epics (M1): the real writers and PerkDef `maxStacks`.
- M0 (network epic): source-list replication and late-join rebuild (CDD-NET-5/6).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/content/StatSheetWriterTests.cs` — must exist and pass. The architecture assertion goes in `tests/unit/architecture/` alongside the simulation-core composition tests.

**Status**: [x] Created — tests/integration/content/StatSheetWriterTests.cs (42 methods), tests/unit/architecture/StatWriterRulesTests.cs (8); passing on four legs

---

## Dependencies

- Depends on: Story 014. Also `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SimComposition`, `[SimSystem]` reflection test) and `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (`SimCounters`).
- Unlocks: every M-1a stat reader (grid-inventory, player-character, manual-delivery, mail-destinations, throwing-catching), which receives `IStatSheet`

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 6/6 passing (none deferred)
**Deviations**: Advisory, accepted by LP — whole-sheet rebuild per Push/Remove swapping an immutable `StatSheetCache` into internal `LiveStatSheet` (pools fixed, rebuild allocates, Read 0 B); `IStatSheet` gained `Has` + `uint Version` (0 for `Build`-made sheets); `IStatWriter.Push/Remove` return bool; `ModifierSource` carries `MaxStacks`, `Lifetime`, `RuleFlagSet`; `ModifierOwner` (`Team`/`ForSlot`, default = unowned → refused — resolves TD-082(1)); `StatSources.FromProfilePicks` drops unmapped/absent-profile picks; `ProfileId(ulong)` UNVERIFIED placeholder; `ContentAssertCode` 2019 appended. Out-of-scope touches (append-only/additive): `Core/CounterId.cs` 37 StatPoolFull / 38 StatWriteRefused; `Composition/{SimComposition,SimHost,SimSystemContext}.cs`; `tests/unit/sim/Core/SimCountersTests.cs`, `tests/unit/content/{DefIndexTests,StatSheetReadTests}.cs`. Remaining advisories → TD-086 (incl. should-fix-before-M1 Mul-cap pre-check in Push).
**Test Evidence**: Integration: tests/integration/content/StatSheetWriterTests.cs + tests/unit/architecture/StatWriterRulesTests.cs. Four legs green on the real tree (net10/net8 × Debug/Release: 4 035 tests/leg, 0 failed; Debug 31 skipped per TD-008).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (no blocking; closed with tech debt TD-086); QL-TEST-COVERAGE ADEQUATE. C#-specialist folded into the LP gate.
