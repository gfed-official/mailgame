# Story 011: `Hold.HoldTicks` — the single shared `holdEff` function

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: In Progress
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~150 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-17

> **Interaction-framework story (1 of 9).** Input & Controls (`holdEff`), Manual Delivery (F2 restatement, `GetHoldTicks`) and the HUD hold ring all call this function; none may re-create it. Ships first, per ADR-0014's Migration Plan.

## Context

**GDD**: `design/gdd/player-character-movement.md` (F4 `holdTicks` / `progress`; ContentValidator rule 4); `design/gdd/input-controls.md` (F1 `holdEff`, vectors H-F1-1 — the shared table)
**Requirement**: `TR-movement-012` (hold counter in owner Sim ticks — the length function), `TR-movement-002`; also serves `TR-input-011` (`holdEff` one shared int64 Sim function, 6-tick floor, `holdCeil` clamp) and `TR-delivery-015` (`holdEff` with `insertPm` 500; `DeliverInsertPm` clamp 0…750), both registry-owned by other epics
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-F4-2, input-controls H-F1-1.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§4 `Hold.HoldTicks`) — Accepted 2026-10-16
**ADR Decision Summary**: `holdEff` exists once, as `Hold.HoldTicks` in `Sim/Players/Hold.cs`: pure, T2, evaluated in `long` with one `CeilDiv`. `insertPm` is clamped to 0…750, `holdSettingMs` to 200…1000 (multiples of 50), `num = holdBaseMs × (1000 − insertPm) × settingMs × 30`, `den = 1000 × holdDefaultMs (400) × 1000`, `raw = CeilDiv(num, den)`, `ceil = CeilDiv(max(holdBaseMs, holdCeilMs 2000) × 30, 1000)`, result `clamp(raw, minHoldTicks 6, ceil)`. `GetHoldTicks(latch)` defaults to it and the result is latched at press. Player Character F4's own copy is retired.
**ADR Version**: ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: `holdEff` exists once, as `Hold.HoldTicks` (`long`, one `CeilDiv`), and every caller uses it (ADR-0014)
- Required: pure shared functions live in the Sim assembly and are called by both `SimWorld` and `ClientLayer` (ADR-0003 §6)
- Forbidden: a second hold formula in Player Character, Manual Delivery, the HUD or tests (GDD F4 `[D-2]`, ADR-0014)

---

## Acceptance Criteria

*From GDD PCM-F4-2 and input-controls H-F1-1 (shared parametrised vector table), scoped to this story:*

- [ ] **PCM-F4-2.** `holdTicks` for a 400 ms base at the default 0.4 s setting: **12**; `insertPm` 500 (Quick Hands) → **6**, and **15** at the 1.0 s setting; setting 0.2 s → **6**; 1.0 s → **30**; `insertPm` 1000 (clamped to 750 internally) → **6** (the `minHoldTicks` floor); `holdBaseMs` 2000 at the 1.0 s setting → **60** (clamped to `ceil`, not 150); progress `1000 × 5 / 12` = **416 ‰** (floor) — `Hold.ProgressPm(heldTicks, holdTicks)`.
- [ ] **H-F1-1 (input-controls).** every row of the input-controls H-F1-1 table passes against this function (default deliver 0.4 s → 12; Quick Hands at 0.4 → 6, at 0.6 → 9; deconstruct at 1.0 → 38; deliver base 0.8 s at 1.0 → 60; Quick Hands at 0.2 → 6, at 0.45 → 7; `insertPm` 750 at 1.0 → 8) and, over the full grid setting 200–1000 step 50 × `insertPm` {0, 500, 750} × base 100–2000 ms step 100, the result equals the exact rational `clamp(ceil(base × (1 − insertPm/1000) × (setting/400) × 30 / 1000), minHoldTicks, ceil)` computed with `BigInteger` in the test.
- [ ] Out-of-range inputs are clamped, never thrown: `insertPm` −5 → 0 and 900 → 750 (each counted in `SimCounters.holdInputClamped`), `holdSettingMs` 150 → 200 and 1200 → 1000, a non-multiple of 50 (e.g. 430) is rounded to the nearest 50 by the caller-visible rule documented in the XML doc and tested.
- [ ] `Hold.HoldTicks` is `static`, allocation-free (10 000 calls = 0 B), uses no `float`/`double`, and overflows nowhere over the full int32 input domain after clamping (max product `2000 × 1000 × 1000 × 30` fits int64).
- [ ] `HoldSettings` (`holdDefaultMs` 400, `holdCeilMs` 2000, `minHoldTicks`, `maxInsertPm` 750) is a readonly struct built from `PlayerSettings` (`minHoldTicks`, from `PlayerBalance`) plus the two Input constants; `minHoldTicks < 6` is a content-data validator error (CV-PLR-04) and is not re-checked here.

---

## Implementation Notes

*Derived from ADR-0014 §4:*

- File: `src/PerformativeMail.Sim/Players/Hold.cs` — `public static class Hold { public static int HoldTicks(int holdBaseMs, int insertPm, int holdSettingMs, in HoldSettings s); public static int ProgressPm(int heldTicks, int holdTicks); }` and `HoldSettings.cs`. `Fix.CeilDiv` from sim-core story 003.
- `holdSetting` is a *player setting* (`settings.input`, content-data story 008 decision) — this function receives it as an argument; it does not read settings itself. `holdCeil` and `holdDefault` are Input constants (PROPOSED in input-controls) carried in `HoldSettings`.
- content-data story 008 notes "the Sim-side `holdEff` function belongs to the input-controls epic and waits for ADR-0014". ADR-0014 (Accepted) places it here in `Sim/Players/Hold.cs`; **the input-controls epic must reference this story rather than create a second implementation.** (Cross-epic note recorded in EPIC.md.)
- Do not add a `GetHoldTicks` call site here — the `IInteractable.GetHoldTicks` default in Story 012 calls this function.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 017: the hold counter that consumes `HoldTicks` and completes at `PressTick + HoldTicks`.
- Story 018: publishing `ProgressPm` in the interaction snapshot.
- Manual Delivery's `GetHoldTicks` override (Quick Hands `insertPm`) and the HUD hold-ring widget.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/HoldTicksTests.cs` (`[Trait("Id", "PCM-F4-2")]`, `H-F1-1`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-003-numerics-toolkit.md` (`Fix.CeilDiv`), `production/epics/simulation-core/story-007-simcounters-simcheckring.md` (clamp counter), Story 001 (`PlayerSettings.minHoldTicks`)
- Unlocks: Stories 012 (`GetHoldTicks` default), 017 (hold counter), 018 (progress); input-controls epic (`holdEff` consumer, router F1 vectors); manual-delivery epic (hold length); HUD hold ring
