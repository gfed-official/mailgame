# Story 023: The armed-catcher receiver — F5 contact over the position history, rank-0 race rules and the CAUGHT / FUMBLE terminals through `TryCatch` (Sim-side catch, part 2)

> **Epic**: Throwing & Catching (M-1a slice) — **Sim-side catch pull-forward (optional for the M-1a gate)**
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~450 lines prod + ~400 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Catch pull-forward (2 of 3).** An armed teammate becomes the highest-ranked receiver: the earliest contact over everything they reported in the last 300 ms wins, and the whole stack goes into their hotbar. Everything here is exercised headless with simulated carriers. The production position feed is the **newest sample only** until the M0 history ring exists (ADR-0020, not written): the algorithm is built and tested against a multi-sample test double, and story 024 (Blocked) proves it against the real ring.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T6 receiver rows "Armed catcher" and CAUGHT / FUMBLE; T7 "Contact", "Two catchers", "Effect", "One press catches one flight", "Selection after a catch"; F5 contact; F4 sphere; Edge Cases "If two flights reach one catcher", "If eight armed catchers", "If the catcher's hotbar has only slot 0 free", "If a 20-letter stack meets a matching hotbar stack", "If the catcher stands inside or behind a mailbox's catch volume")
**Requirement**: `TR-throwing-012` (contact over the 9-tick history — the contact half; ADR-0021 gap, ADR-0015 §7 fixes the Sim rule), `TR-throwing-006` (rank 0, tie-breaks), `TR-throwing-005` (sphere interval), `TR-throwing-013` (CAUGHT/FUMBLE are single terminals; conservation)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T7-2 a–g, TC-F4-1 e, TC-F5-1 b/c (Sim half; M0-tagged), TC-E-4 (catcher half, partly M-1b), TC-T6-1 (catcher rank).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§7 contact: "minimum `f` over the catcher's history samples (`catchHistoryTicks` 9) within `catchHistoryMaxOffsetMm` (3D) of the newest; one press catches one flight (the arm clears on CAUGHT or FUMBLE)"; §5 `CAUGHT`/`FUMBLE` call `IInventoryApi.TryCatch(slot, payload)`) — Accepted 2026-10-16 (primary); ADR-0009 (`TryCatch`, `FlightPayload`, `CatchResult`, hotbar-first `TargetFirst`) and ADR-0014 §5 (position staleness) secondary — Accepted
**ADR Decision Summary**: An armed, eligible, non-thrower catcher is a sphere receiver of `catchRadiusMm` (1500) about feet + 1000, tested at **every** position the catcher reported in the last 9 ticks that lies within `catchHistoryMaxOffsetMm` (1500) of the newest; the earliest `f` is that catcher's contact (rank 0, so a catcher beats a mailbox, goon or teammate at equal `f`). Two catchers: earliest `f`, then nearer at contact (newest sample), then `Mix64(flightId, playerSlot)`; the loser keeps their arm. The win calls `TryCatch(slot, payload)` synchronously: mail hotbar-first (`TargetFirst`), the newest units that fit nowhere become one WorldItem at the catcher's feet; CAUGHT if any unit went in, FUMBLE only if none. The arm clears on the first CAUGHT or FUMBLE. The host never rewinds.
**ADR Version**: ADR-0015 2026-10-16; ADR-0009 2026-10-16; ADR-0014 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: catcher contact is the same `FlightSweep` (sphere interval) with the |R| ≤ 65 536 mm reject; units move only through `IInventoryApi`; flights in creation order; reads of life/arm/eligibility are taken once at the start of the step (ADR-0015 §4)
- Forbidden: a second sweep; rewinding a resolved flight; the catch going to the cursor; Throwing reading Mail on a catch (the catch ends the misdelivery risk; the `thrownPm` snapshot is discarded)
- Guardrail: ≤ 8 catchers × 16 flights × 10 history samples ≈ 77 µs (GDD estimate); 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T7-2, TC-F4-1 e, TC-F5-1 b/c, TC-T6-1) and ADR-0015 §7, scoped to the Sim-side catch. Bonk (T8), the prompt predictor (F5 prompt), relay hops and Toggle are M0 and deferred; an ineligible or unarmed player is therefore passed through at M-1a:*

- [ ] **TC-F4-1 e.** Catcher feet (0, 0, 0), P0 (−3000, 1000, 0), D (3000, 0, 0) → f = 500 (sphere r 1500 about (0, 1000, 0)).
- [ ] **TC-F5-1 b (history samples).** With a 9-sample test double, a sample 1500 mm (3D) from the newest counts toward the earliest-`f` contact; one at 1501 mm is ignored. Contact is the minimum `f` over the counted samples.
- [ ] **TC-F5-1 c (staleness).** A newest report 30 ticks old → catchable; 31 → not; no sample at all (a rejoiner or a just-teleported player) → not catchable before their first report.
- [ ] **TC-T7-2 a.** FX-L20 caught by a catcher whose hotbar letter stack to the same address holds 5 (room for 15) and nothing else is free: `CAUGHT{unitsIn 15, unitsFumbled 5}`, the **oldest** 15 merge, the newest 5 are one WorldItem (`origin = Thrown`) at the catcher's feet; the thrower's `FlightResolved` carries the counts.
- [ ] **TC-T7-2 b.** Only hotbar slot 0 free (reserved), inventory free → `CAUGHT{20, 0}` into the inventory, selection unchanged. **c** Nothing fits anywhere → `FUMBLE` (`unitsIn` 0, whole entry one WorldItem at the catcher's feet).
- [ ] **TC-T7-2 d.** A same-tick hand op that frees a hotbar slot counts for the catch (phase 0 precedes phase 4). A second flight in the same window finds the catcher already unarmed and **passes through** at M-1a (it will bonk at M0): the arm cleared on the first CAUGHT/FUMBLE, so exactly one flight is caught per press.
- [ ] **TC-T7-2 e (parametrised).** The thrower never receives their own flight (not for the flight's lifetime, including after a Second Wind respawn); an armed Holding-closed catcher is caught into the **hotbar, never the cursor**; a swimming, Dead, Frozen or mounted body is not a receiver (the flight passes through at M-1a; WATER/BONK variants are M0).
- [ ] **TC-T7-2 f (selection, OQ24).** After a catch that landed in the hotbar, the caught slot becomes selected **only if** the catcher's current selection is the hands slot or a mail entry; a selected tool or weapon is never deselected; selection never changes while Holding; units that went to the inventory or backpack leave selection unchanged. Implemented through the `IHotbarSelection` write side (the **unowned seam** of stories 004/009; test double until an owner exists). `CatchResult` must report where the units landed (hotbar index, or bag): add `LandedHotbarIndex`/`LandedInBag` to Grid's `CatchResult` additively if story 014 of grid-inventory lacks them, and log it.
- [ ] **TC-T7-2 g.** The catcher's later delivery or throw of those units uses the catcher as `actorId` and the catcher's own `thrownPm` (a following throw in the same test shows `thrownPm` read from the catcher's sheet); the original `thrownPm` snapshot is discarded. Race losers keep their arm.
- [ ] **Rank and ties (TC-T6-1 catcher rows).** At equal `f` a catcher beats a mailbox volume; a catcher *in front of* a mailbox intercepts (earlier `f`); a catcher *behind* a box is not reached if the flight enters the box first; two armed catchers: the earliest `f` wins, equal `f` goes to the nearer at contact (newest sample), then the lower `Mix64(flightId << 32 | playerSlot)`; the loser has no state change.
- [ ] **Eight catchers.** With 8 armed catchers in range of one flight the winner follows the rule above and the other seven keep their arm for their window.
- [ ] **Conservation.** Every CAUGHT/FUMBLE case leaves the Flight ledger at 0, units conserved, `FlightResolved` raised once before the Grid events, and the tick-end record green; the story-015 fuzz is re-run with catchers enabled (feature mask) with zero faults.
- [ ] **TC-P-2 (catcher fixture).** 16 flights and 8 armed catchers over 10 000 ticks: `Throwing.Step` p99 ≤ 0.15 ms and max ≤ 0.5 ms (ADVISORY), **0 B** (BLOCKING); the bounding-sphere reject is exercised (counter).

---

## Implementation Notes

*Derived from ADR-0015 §4/§7 and GDD T7 / F5:*

- Files in `src/PerformativeMail.Sim/Throwing/Catch/`: `CatcherSource.cs` (builds the `SphereReceiver` span from armed ∧ eligible ∧ non-thrower slots at the start of the step; one `SphereReceiver` per history sample with the same `receiverKey` = the slot, and a `TieCentre` = newest sample), `CatchLanding.cs` (`ICatchLanding`; builds `FlightPayload`, calls `TryCatch`, maps `CatchResult` → `Terminal.Caught`/`Fumble`, clears the arm, applies selection), `ICatcherPositionSource.cs`.
- **`ICatcherPositionSource`** (Throwing-defined): `int GetSamples(PlayerSlot s, Span<PositionSample> newestFirst, out uint newestReceivedTick)`. Production adapter at M-1a: over Player Character's `IPositionHistory` (`production/epics/player-character/story-019-host-checkinteraction-position-ingest.md`), which returns **one** (the newest) sample until ADR-0020 supplies the 9+3-tick ring; the adapter is the drop-in seam. `PositionSample` = feet `Vec3Mm` + `ReceivedTick`. Staleness uses `positionStaleTicks` (30) from the Players settings, not a local constant.
- `FlightSweep` takes multiple sphere receivers with the same `receiverKey`: the winner selection is applied per receiver key first (earliest `f` over its samples), then across receivers — implement as a loop in `CatcherSource`/`FlightSweep` without allocation; the tie `dist²` for catchers is to the **newest** sample's centre (story 002 reserves `TieCentre`).
- `FlightPayload` = entry key + units + `mailIds` handle + the catcher's newest feet (`Vec3Mm`); the producer is this story (grid-inventory story 014: "Throwing owns the call site"). The fumble WorldItem is created inside `TryCatch`, at the catcher's feet.
- The newest-5-fumble rule and hotbar-first placement are Grid's (`production/epics/grid-inventory/story-014-pickup-core-trycatch.md`); assert them end to end, do not re-implement.
- Telemetry for catches (`armedBeforeLaunch`, thrower/catcher ids) is M0 (story 016's reserved fields).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 022: the arm window and handler. Story 024: the real position-history ring and two live carriers (Blocked).
- T8 bonk (teammate receiver, M0), swimming-teammate WATER, aiming-teammate BONK, the catch prompt and `CatchPredictor` (TC-F5-1 a/d, TC-T7-3), relay hops T13, hat pins, Toggle, goon/belt interactions (M-1b), the catcher-dying death spill (TC-E-4 second half, M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/throwing/CatchTests.cs` and `tests/unit/throwing/CatcherContactTests.cs` (`[Trait("Id", "TC-T7-2")]`, `TC-F4-1e`, `TC-F5-1b`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 008, 011, 015, 022; `production/epics/grid-inventory/story-014-pickup-core-trycatch.md` (`TryCatch`); `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md` (`IPositionHistory`, host positions)
- Unlocks: Story 024
