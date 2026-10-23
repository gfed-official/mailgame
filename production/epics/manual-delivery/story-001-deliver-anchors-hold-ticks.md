# Story 001: Deliver anchors (MD1) — one `IInteractable` per destination at run start, and `GetHoldTicks` through the shared `holdEff` with `DeliverInsertPm` (F2, MD9)

> **Epic**: Manual Delivery
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~150 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (story-done: Complete — implementation, tests and Completion Notes). 2026-10-21 (review fixes on PR #12: pointers to story 005 AC-7, story 007 AC-7 and player-character 016 M-MD9-1 made exact). 2026-10-21 (readiness fixes: stamp 2026-10-17.1 — the 17.1 changes touch only the Godot input adapter, `WorldTablesBuilder` and numerics/geometry errata, none of which this story uses; real `IStatSheet.Read` signature and stat-sheet injection; priority via `AnchorDef.For` / `VerbCatalogue`; namespaces; system-shell scope moved to story 005; AC-3 latch clause pointed at player-character 016; test legs and performance note)

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD1 the Deliver verb; MD9 Quick Hands; F2 deliver hold ticks)
**Requirement**: `TR-delivery-001` (one Deliver `IInteractable` per destination at run start; priority 2; never deregistered), `TR-delivery-015` (`holdEff` with Quick Hands `insertPm` 500; `DeliverInsertPm` clamp 0…750)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0014: Interaction Framework and Hold Timing (primary: §1 registry and the MD1 row, §4 `Hold.HoldTicks`); ADR-0017 (§3 destination registry, the anchor source); ADR-0010 (StatSheet `DeliverInsertPm`)
**ADR Decision Summary**: Delivery registers one `Deliver` anchor per destination whose type has `manualInsertHold > 0`, at run start (`Hold`, priority 2, `RangeMm` 2500, slot 0), and never unregisters it. Registration goes through `IPlayersApi.RegisterAnchor(in AnchorDef, IInteractable)`. A second verb on one anchor in one context returns `DuplicateVerb`. `IInteractable` hooks are pure. `holdEff` exists once, as `Hold.HoldTicks` (long, one `CeilDiv`, 6-tick floor, `holdCeil` clamp), and `GetHoldTicks(latch)` calls it with the StatSheet's `insertPm`.
**ADR Version**: ADR-0014 2026-10-16 + 2026-10-20 errata (Mounted own-seat filter; swimmer Catch press — neither touches Deliver anchors, `GetHoldTicks` or `Hold.HoldTicks`); ADR-0017 2026-10-16 (its prose still says `PerformativeMail.Sim.Mail`; the code namespace is `PerformativeMail.Sim.MailDestinations`, which the open docs erratum PR #8 records — follow the code); ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: every interactable verb registers through `IPlayersApi.RegisterAnchor` (2 m dense grid; one verb per anchor per context); `IInteractable` hooks are pure (ADR-0014 §1)
- Required: `holdEff` exists once, as `Hold.HoldTicks`, and every caller uses it (ADR-0014)
- Required: read stats through `IStatSheet.Read` (ADR-0010 §7)
- Forbidden: a second hold formula; unregistering a Deliver anchor during a run; a code-literal priority (priority is read relative to Player Character's Pick-up priority from data)

---

## Acceptance Criteria

*From GDD `design/gdd/manual-delivery.md`, scoped to this story:*

- [x] **AC-1 (M-MD1-1a)** (a) Given a registry of 10 lots (built-in `house` def, `ManualInsertHoldMs` 400), `DeliverAnchors.RegisterAll` yields exactly 10 Deliver anchors (each `RegisterResult.Ok`): `AnchorRef(destId, 0)`, `Verb.Deliver`, `InteractMode.Hold`, `HoldBaseMs` 400 (the `house` type's `ManualInsertHoldMs`), range 2,500 (the framework default, `RangeMm = 0` in `AnchorDef`), the mailbox's only verb, with `GetHoldTicks`/`ShouldCancel` hooks present. Priority: the test asserts `Priority == VerbCatalogue.DefaultPriority(Verb.PickUp) + 1` (not a literal `2`); the code takes it from `AnchorDef.For(..., Verb.Deliver)`, i.e. Player Character's catalogue, never a Delivery literal. (b) The "test type with `manualInsertHold` 0 gets none" leg: at M-1a the registry types every lot as `house` (`DestinationRegistry` resolves one `house` def; there is no per-lot type yet), so the test builds a second registry over a content set whose `house` def has `ManualInsertHoldMs` 0 and asserts 0 anchors registered and `RegisterResult.Ok` returned. **Never deregistered:** Delivery has no call to `IPlayersApi.UnregisterAnchor` or `MoveAnchor` (an architecture test scans `src/PerformativeMail.Sim/Delivery/`), and all 10 anchors are still registered after 600 ticks of a Players system with no other writer; the run-cycle persistence leg (Prep → Results; at M-1a the shift stub's Prep → Delivery → Closed) is story 007's AC-7 (`tests/integration/delivery/DeliverAnchorLifetimeTests.cs`), on the world story 005 composes.
- [x] **AC-2 (M-F2-1)** `GetHoldTicks` for (`holdSetting` → no perk / Quick Hands `insertPm` 500): 0.20 → 6 / 6; 0.25 → 8 / 6; 0.40 → 12 / 6; 0.60 → 18 / 9; 1.00 → 30 / 15. Each equals `Hold.HoldTicks` for the same inputs (no second formula). A 1,500 ms base at the 1.0 s setting gives 60 (ceiling).
- [x] **AC-3 (M-MD9-1)** With `insertPm` 500, MATCH and MISDELIVER_ONE holds are both 6 ticks at the default setting and both 6 at 0.2 s. A StatSheet `DeliverInsertPm` sum of 1000 is clamped to 750. `GetHoldTicks` is a pure function of its inputs and the stat sheet's current value: a changed `DeliverInsertPm` between two calls gives the new result and nothing is cached in `DeliverInteractable`. *(That a perk or setting change after the press leaves the latched `holdTicks` unchanged is the latch's rule — `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md` "Latch contents": `HoldTicks` evaluated once and never recomputed — and is asserted there as M-MD9-1 in `PressResolutionTests.cs`, not here.)*

---

## Implementation Notes

*Derived from ADR-0014 §1, §4 and Implementation Guidelines:*

- **Files (new folder `src/PerformativeMail.Sim/Delivery/`, namespace `PerformativeMail.Sim.Delivery`, ADR-0001 folder rule):** `DeliverInteractable.cs` (`internal sealed`, `IInteractable`; one instance shared by all Deliver anchors, keyed by `AnchorRef`; takes `IStatSheet` through its constructor — `InteractionContext` carries no stat sheet), `DeliverAnchors.cs` (`internal static`, `RegisterAll(IPlayersApi players, DestinationRegistry registry, ContentSet content, DeliverInteractable impl)`). `DestinationRegistry` is `internal sealed` in `PerformativeMail.Sim.MailDestinations` (`src/PerformativeMail.Sim/Mail/DestinationRegistry.cs`); Delivery is in the same assembly.
- **No system shell and no composition edit in this story.** `SystemId.ManualDelivery` is already in `SimComposition.ShippedOrder`, `SystemGraph` (depends on PlayerCharacter, MailDestinations, GridInventory) and `SimProfile.M1aGreybox`, and `ConstructShipped` returns `null` for it. The registry reaches Delivery only once the Mail system shell exists (`production/epics/mail-destinations/story-005-walletgate-stubwallet-mail-system.md`, Ready). So the `[SimSystem(SystemId.ManualDelivery)]` `DeliverySystem` class and its `ConstructShipped` case — which calls `DeliverAnchors.RegisterAll` while the world is `Constructed` and throws `SimBuildException` on a non-`Ok` result — belong to story 005 (its AC-7; after mail-destinations 005; registry source OQ-MD005-1). This story's code is exercised from tests with a test-built `DestinationRegistry` and a real `PlayersSystem`.
- **Registration:** at world build/run start (the M-1a stub has no Generating phase; register while `Constructed` or in the first tick per `production/epics/player-character/story-012-anchor-registry-iinteractable-spatial-grid.md`'s legality rule), iterate the destination registry (`production/epics/mail-destinations/story-002-destination-registry.md`) in ordinal order `i = 0 … Count − 1` with `var o = new DestinationOrdinal((ushort)i)` and `var type = content.DestinationTypes[registry.TypeOf(o)]` (`DefTable<DestinationTypeDef>` indexed by `DefIndex`), skipping a type whose `ManualInsertHoldMs` is 0: `var def = AnchorDef.For(new AnchorRef(registry.EntityIdOf(o), 0), registry.AnchorOf(o), Verb.Deliver); def.HoldBaseMs = type.ManualInsertHoldMs;` (mode Hold, priority and `RangeMm = 0` = framework default 2,500 come from the catalogue via `AnchorDef.For`). `RegisterAll` returns the first result other than `RegisterResult.Ok` (`DuplicateVerb`, `Full`, …) and stops; the tests fail on it, and how the composed build reports it is story 005's (it owns the composition case).
- **Hooks in this story:** `GetHoldTicks(in latch, in ctx)` → `Hold.HoldTicks(ctx.HoldBaseMs, insertPm, ctx.HoldSettingMs, in settings)` (copy `ctx.HoldSettings`, a property, to a local first, as the `IInteractable` default does), with `insertPm = _stats.Read(Stat.DeliverInsertPm, StatSheet.NoQualifier, ctx.Actor)` (`IStatSheet.Read(Stat, ushort qualifier = NoQualifier, PlayerSlot? slot = null, int transientAdd = 0)`; the stat is personal, so the slot is required; the StatDef clamps 0…750 through its balance key and `Hold.HoldTicks` clamps defensively too). `IsAvailable`, `ShouldCancel`, `WritePrompt`, `OnPress` and `TryBuildRequest` are filled by stories 003, 004 and 010. Until then they return deterministically: `IsAvailable` → `false` with `reason = StringId.None`; `ShouldCancel` → `false`; `OnPress` / `TryBuildRequest` → no-op / `false`; `WritePrompt` → writes nothing. Nothing calls them in production (no Delivery system is composed until story 005).
- **Performance:** registration is once per run (one `DeliverInteractable` and the registry's own entries; no per-tick work). `GetHoldTicks` runs once per press: integer maths and one stat read, 0 B. No tick-loop allocation is added.
- `Hold.HoldTicks` is `production/epics/player-character/story-011-hold-ticks-holdeff.md`'s. Do not re-create it. M-F2-1 here asserts equality through `GetHoldTicks`.
- The Quick Hands perk itself (granting `insertPm` 500) is M1. Tests push the stat through a stub `IStatSheet` or `IStatWriter` test source.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 003: `IsAvailable` (MD2). Story 004: `OnPress`, `ShouldCancel`, `TryBuildRequest`. Story 010: `WritePrompt` content.
- M-MD1-1b (focus Deliver vs Pick-up): story 007 (integration with `production/epics/player-character/story-014-focus-scoring-dwell-preview-binding.md`).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliverAnchorsTests.cs` (AC-1, including the never-deregistered scan) and `tests/unit/delivery/DeliverHoldTicksTests.cs` (AC-2, AC-3; a stub `IStatSheet` supplies `DeliverInsertPm`; the 1000 → 750 clamp goes through the real `StatSheet` build) — must exist and pass on net10.0 and net8.0, Debug and Release (`tools/ci/test-legs-loop.sh`). `tests/unit/delivery/` is new; the test project globs `tests/unit/**`, so no csproj edit.

**Status**: [x] Created — `tests/unit/delivery/DeliverAnchorsTests.cs` (AC-1) and `tests/unit/delivery/DeliverHoldTicksTests.cs` (AC-2, AC-3); pass on net10.0/net8.0 × Debug/Release

---

## Dependencies

- Depends on: `production/epics/player-character/story-011-hold-ticks-holdeff.md`, `production/epics/player-character/story-012-anchor-registry-iinteractable-spatial-grid.md`; `production/epics/mail-destinations/story-002-destination-registry.md`; `production/epics/content-data/story-014-statsheet-build-read-explain.md`
- No dependency on open PRs #7, #9 or #11: none touches `Sim/Delivery`, `Sim/Players`, `Sim/Mail` or `Content/Stats`. (#11 edits `SimComposition.ConstructShipped`, which this story deliberately does not touch.) PR #8 is docs-only (ADR-0017 namespace erratum) and does not gate this story.
- Unlocks: Story 003, Story 004, Story 005 (which also takes the `DeliverySystem` shell and composition case; it then also depends on `production/epics/mail-destinations/story-005-walletgate-stubwallet-mail-system.md`)

---

## Completion Notes

**Completed**: 2026-10-21
**Criteria**: 3/3 passing (no deferred items)
- AC-1 (M-MD1-1a), `DeliverAnchorsTests`: `RegisterAll_TenLotStreet_RegistersExactlyTenDeliverAnchors_WithTheCatalogueDefaultsAndTheHouseHoldBase`, `RegisterAll_HouseTypeHoldOtherThanTheCatalogueDefault_IsTheStoredHoldBase`, `RegisterAll_FirstFailure_StopsAndReturnsIt`, `RegisterAll_EachMailbox_HasDeliverAsItsOnlyVerb_AndASecondVerbIsDuplicateVerb`, `DeliverInteractable_DeclaresItsOwnGetHoldTicksAndShouldCancelHooks`, `RegisterAll_HouseTypeWithManualInsertHoldZero_RegistersNoAnchor_AndReturnsOk`, `DeliverySource_NeverCallsUnregisterAnchorOrMoveAnchor_AndHasNoPriorityAssignment`, `DeliverySourceScan_MatchesEachForbiddenCall`, `DeliverySourceScan_IgnoresComparisonsAndCatalogueReads`, `RegisterAll_TenAnchors_AreStillRegisteredAfter600TicksOfAComposedPlayersSystem`; stub hooks: `DeliverHoldTicksTests.StubHooks_AreDeterministic_UntilStories003_004_010`.
- AC-2 (M-F2-1), `DeliverHoldTicksTests`: `GetHoldTicks_HouseBaseBySetting_MatchesTheF2Table_AndEqualsHoldHoldTicks` (5 rows), `GetHoldTicks_1500MsBaseAtTheOneSecondSetting_Is60Ticks`, `GetHoldTicks_ReadsDeliverInsertPm_ForTheActorsSlot`.
- AC-3 (M-MD9-1), `DeliverHoldTicksTests`: `GetHoldTicks_QuickHands_MatchAndMisdeliverOneHolds_AreBothSixTicks` (default 400 ms and 200 ms), `StatSheetBuild_TwoQuickHandsSumming1000_ClampsDeliverInsertPmTo750_AndGetHoldTicksUsesIt`, `StatSheetBuild_NoQuickHands_ReadsZero_AndAnotherSlotsPerkDoesNotApply`, `GetHoldTicks_StatChangedBetweenTwoCalls_ReturnsTheNewResult`, `DeliverInteractable_HoldsOnlyTheStatSheet_SoNothingIsCached`.

**Deviations** (advisory):
- Out-of-scope files: the `PerformativeMail.Sim.Delivery` namespace shadows the `PerformativeMail.Client.Core.Input.Delivery` enum inside `PerformativeMail.Sim.*` namespaces, so `tests/unit/input/ActionMapTests.cs`, `DefaultBindingsTests.cs` and `InputFixtures.cs` each gained one namespace-scoped alias `using Delivery = PerformativeMail.Client.Core.Input.Delivery;` (plus a two-line comment) to keep compiling — TD-124.
- Test namespace is `PerformativeMail.Sim.Tests.Unit.ManualDelivery` (folder `tests/unit/delivery/`), following the epic-named test namespaces (`Unit.MailDestinations`, `Unit.PlayerCharacter`) and avoiding the same shadowing.
- The MATCH / MISDELIVER_ONE leg of M-MD9-1 uses two destinations of the same type: the latch carries no outcome until story 004's `OnPress` payload (TD-125 item 4).
- Test-hardening suggestions from review (formula sweep, non-default `HoldSettings`, defensive-clamp stub case, stub-hook test replacement, scan-regex breadth) — TD-125.

**Test Evidence**: Logic — `tests/unit/delivery/DeliverAnchorsTests.cs`, `tests/unit/delivery/DeliverHoldTicksTests.cs`. Full suite 4,899 tests per leg: net10.0 Debug 4,857 passed / 42 skipped / 0 failed; net10.0 Release 4,897 / 2 / 0; net8.0 Debug 4,857 / 42 / 0; net8.0 Release 4,897 / 2 / 0; 0 build warnings. `tools/ci` guards (each with `--self-check`): no-scenetree-paused, build-config, sim-access, gdignore, scene-rules, analyzer-fixtures, game-build (`PerformativeMail.Game.sln` Debug + ExportRelease) — all PASS. No `using Godot` in Sim or Client.Core. Godot smoke not required (Godot-free Sim code).
**Run result**: N/A — Godot-free Sim code with no scene, node or on-screen output; nothing is composed in production until story 005, so there is nothing to launch and observe.
**Code Review**: Complete — `/code-review` (Sonnet reviewers): APPROVED WITH SUGGESTIONS; QL-TEST-COVERAGE: ADEQUATE (advisory gaps); LP-CODE-REVIEW: APPROVE.
**OQ-MD001-1 — Resolved 2026-10-22** (Evan, 2026-10-22 7:13 PM PT, Mail Dev chat: "Keep the verb catalogue as the source (recommended)"; decision log 2026-10-22, "Deliver's priority comes from the Players `VerbCatalogue`"): the catalogue is the source; the anchor registration and test stand as built. Originally: does "priority read from Player Character's data" (GDD / manifest) mean the Players `VerbCatalogue` code table? No content field holds a priority, so this story takes it from the catalogue via `AnchorDef.For(..., Verb.Deliver)` and the test asserts `Priority == VerbCatalogue.DefaultPriority(Verb.PickUp) + 1`. It is built that way for now; Mail Lead is asking Evan to confirm.
