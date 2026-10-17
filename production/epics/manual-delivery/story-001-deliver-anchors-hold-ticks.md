# Story 001: Deliver anchors (MD1) — one `IInteractable` per destination at run start, and `GetHoldTicks` through the shared `holdEff` with `DeliverInsertPm` (F2, MD9)

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~150 lines prod + ~200 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/manual-delivery.md` (MD1 the Deliver verb; MD9 Quick Hands; F2 deliver hold ticks)
**Requirement**: `TR-delivery-001` (one Deliver `IInteractable` per destination at run start; priority 2; never deregistered), `TR-delivery-015` (`holdEff` with Quick Hands `insertPm` 500; `DeliverInsertPm` clamp 0…750)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0014: Interaction Framework and Hold Timing (primary: §1 registry and the MD1 row, §4 `Hold.HoldTicks`); ADR-0017 (§3 destination registry, the anchor source); ADR-0010 (StatSheet `DeliverInsertPm`)
**ADR Decision Summary**: Delivery registers one `Deliver` anchor per destination whose type has `manualInsertHold > 0`, at run start (`Hold`, priority 2, `RangeMm` 2500, slot 0), and never unregisters it. Registration goes through `IPlayersApi.RegisterAnchor(in AnchorDef, IInteractable)`. A second verb on one anchor in one context returns `DuplicateVerb`. `IInteractable` hooks are pure. `holdEff` exists once, as `Hold.HoldTicks` (long, one `CeilDiv`, 6-tick floor, `holdCeil` clamp), and `GetHoldTicks(latch)` calls it with the StatSheet's `insertPm`.
**ADR Version**: ADR-0014 2026-10-16; ADR-0017 2026-10-16; ADR-0010 2026-10-16

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

- [ ] **AC-1 (M-MD1-1a)** Given 10 house destinations and one test type with `manualInsertHold` 0, run-start registration yields exactly 10 Deliver anchors: slot 0, Hold 400 ms, range 2,500, priority exactly one level above Player Character's Pick-up priority read from its data, the mailbox's only verb, with `GetHoldTicks`/`ShouldCancel` hooks present. The test type gets none, and none deregister from Prep to Results (or to the end of the M-1a stub cycle).
- [ ] **AC-2 (M-F2-1)** `GetHoldTicks` for (`holdSetting` → no perk / Quick Hands `insertPm` 500): 0.20 → 6 / 6; 0.25 → 8 / 6; 0.40 → 12 / 6; 0.60 → 18 / 9; 1.00 → 30 / 15. Each equals `Hold.HoldTicks` for the same inputs (no second formula). A 1,500 ms base at the 1.0 s setting gives 60 (ceiling).
- [ ] **AC-3 (M-MD9-1)** With `insertPm` 500, MATCH and MISDELIVER_ONE holds are both 6 ticks at the default setting and both 6 at 0.2 s. A StatSheet `DeliverInsertPm` sum of 1000 is clamped to 750. A perk or setting change after the press does not change the latched `holdTicks` (the latch keeps the press-time value).

---

## Implementation Notes

*Derived from ADR-0014 §1, §4 and Implementation Guidelines:*

- **Files:** `src/PerformativeMail.Sim/Delivery/DeliverInteractable.cs` (`IInteractable`; one instance shared by all Deliver anchors, keyed by `AnchorRef`), `Delivery/DeliverAnchors.cs` (run-start registration). `DeliverySystem` (story 005 creates the system; if this story lands first, create the system shell here and let story 005 add the op).
- **Registration:** at world build/run start (the M-1a stub has no Generating phase; register while `Constructed` or in the first tick per `production/epics/player-character/story-012-anchor-registry-iinteractable-spatial-grid.md`'s legality rule), iterate the destination registry (`production/epics/mail-destinations/story-002-destination-registry.md`) in ordinal order: `AnchorDef { AnchorRef(destId, 0), Point = interaction anchor, Verb.Deliver, InteractMode.Hold, HoldBaseMs = manualInsertHold, RangeMm = 2500 (framework default), Priority = pickUpPriority + 1 }`. A `DuplicateVerb` or `Full` result is a build error in tests.
- **Hooks in this story:** `GetHoldTicks(latch, ctx)` → `Hold.HoldTicks(holdBaseMs, insertPm, holdSettingMs, settings)`, with `insertPm` from `IStatSheet.Read(DeliverInsertPm, actor)` (the StatDef clamps 0…750; `Hold.HoldTicks` clamps defensively too). `IsAvailable`, `ShouldCancel`, `WritePrompt`, `OnPress` and `TryBuildRequest` are filled by stories 003, 004 and 010. Until then they return "unavailable / no-op" deterministically, and nothing calls them in production.
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
- Logic: `tests/unit/delivery/DeliverAnchorsTests.cs` and `tests/unit/delivery/DeliverHoldTicksTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/player-character/story-011-hold-ticks-holdeff.md`, `production/epics/player-character/story-012-anchor-registry-iinteractable-spatial-grid.md`; `production/epics/mail-destinations/story-002-destination-registry.md`; `production/epics/content-data/story-014-statsheet-build-read-explain.md`
- Unlocks: Story 003, Story 004, Story 005
