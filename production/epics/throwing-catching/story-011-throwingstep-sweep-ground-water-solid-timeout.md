# Story 011: `ThrowingStep` (phase 4) — the per-tick sweep, GROUND / WATER / solid / TIMEOUT terminals, WorldItem placement and `FlightResolved`

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~550 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Flight resolution story (1 of 3).** After this story every flight that does not hit a mailbox or a catcher ends correctly: it lands on the ground, floats on water, stops on a house or the map edge, or times out — exactly once, conserving its units. Mailbox landings are story 012; catchers are story 023.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T5 "The sweep"; T6 receivers and "Every flight ends in exactly one terminal state" table rows Solid, Terrain, Water, TIMEOUT; "WorldItem placement"; T11 solids and map edge; Edge Cases "If the drop point is inside a solid footprint", "If a flight reaches the map edge", "If a WATER landing is right at the shore")
**Requirement**: `TR-throwing-006` (flights in creation order; earliest contact), `TR-throwing-013` (exactly one terminal per flight; conservation), `TR-throwing-008` (phase 4, 0 alloc), `TR-throwing-005` (the per-tick sweep driver)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T6-3 a/b, TC-E-1 a, TC-T4-1 c, TC-E-7 (cap behaviour).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§4 the per-tick sweep, §5 terminal resolution, WorldItem placement) — Accepted 2026-10-16 (primary); ADR-0009 (`IInventoryApi.CreateWorldItem`, water float and return, Flight ledger), ADR-0026 (`TrySnapWalkable`, `ClampInsideMap`) and ADR-0003 (phase 4 after phase 0) secondary — Accepted
**ADR Decision Summary**: `Throwing.Step` (phase 4) processes live flights in creation order; for each flight of age `k ≥ 1` it sweeps `p(k−1) → p(k)` over the receiver view and applies the winner as exactly one terminal, synchronously. GROUND (terrain or solid) and WATER create one WorldItem (`origin = Thrown`) through `IInventoryApi`; solids drop the item straight down; water floats at its contact point. No contact by age 90 is TIMEOUT: the item drops onto the surface directly below `p(90)`. WorldItem placement snaps to walkable ground within 1000 mm, growing back along −v, else the receiver's base, clamped 1000 mm inside the map. `FlightResolved` is raised before any domain event it causes.
**ADR Version**: ADR-0015 2026-10-16; ADR-0009 2026-10-16; ADR-0026 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: flights in creation order, `FlightId` never compared; every flight ends in exactly one terminal; units leave Flight only through `IInventoryApi`/`IMailApi`; damage is queued, never applied in phase 4 (ADR-0015)
- Required: mutating `I…Api` calls inside the owner's phase with `TickGuard.AssertMutable()`; read life states at the start of the step
- Forbidden: a second sweep implementation; leaving an item on a roof or parenting it to anything; Throwing creating entries or WorldItems except through `IInventoryApi`
- Guardrail: `Throwing.Step` ≤ 0.15 ms p99 with 16 flights (measured in story 015), 0 B per tick

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T6-3, TC-E-1, TC-T4-1 c), scoped to this story. Mailbox terminals are story 012; CAUGHT/FUMBLE story 023; BONK, HIT_ENEMY, BELT are M0 / M-1b and not failed here:*

- [ ] **Order and age.** With three live flights launched on different ticks the step visits them in launch order; a flight launched this tick (age 0) is skipped; a flight of age `k` sweeps exactly the segment `Ballistic.PositionAt(k−1) → PositionAt(k)`; ages run `1 … maxFlightTicks` inclusive.
- [ ] **TC-T6-3 (parametrised): terrain.** A flight whose first contact is dry terrain → terminal GROUND, one WorldItem (`origin = Thrown`, `onWater = false`) at the contact point snapped to walkable ground; the Grid 300 s expiry starts at the landing tick.
- [ ] **TC-T6-3: solid.** Contact with a `House` or `Roof` solid or the Post Office (the Intake console at M-1a) → GROUND: the flight stops at the contact point and the item drops **straight down** onto the ground below the footprint (`SurfaceBelow`, story 003) — never left on a roof, never accepted into the Intake. The map's vertical sides behave the same, with the item placed 1 m inside the edge.
- [ ] **TC-T6-3: water.** A flight whose first contact is water → terminal WATER, one WorldItem at the contact point with `onWater = true` (no snapping to land, even at the shore); it cannot be picked up through tick 299 after landing; at tick 300 mail returns to the **back** of the Intake queue and non-mail is destroyed (Grid story 013's behaviour, asserted end to end).
- [ ] **TC-T6-3: age 90.** An injected flight whose only ground contact is segment 89 → 90 resolves GROUND (the k = 90 sweep happens); an injected flight with no contact through age 90 resolves **TIMEOUT**: the item is created on the surface directly below `p(90)`, as GROUND or WATER depending on what is below (`FlightResolved.terminal = Timeout`, `rank` of the surface).
- [ ] **TC-T6-3 b (placement).** A drop point on a roof footprint snaps via candidates back along −v (the interpretation below); if every attempt fails the item goes to the receiver's base (for solids: the nearest walkable tile from World's query); a point past the map edge is clamped 1 m inside before snapping; a WATER landing at the shore floats at its contact point.
- [ ] **TC-E-1 a.** A thrower facing a wall at arm's length (chest probe blocked, story 003): the flight launches from feet + 1000, the age-1 segment contacts the solid at f ≈ 0, exactly one WorldItem lands ≤ 1000 mm from the feet, and the cooldown has run (no refund).
- [ ] **Earliest contact wins.** A house in front of a mailbox ends the flight as GROUND (solid rank 5 after an earlier f); a mailbox volume at an earlier f than a solid is routed to the mailbox port (story 012) — the step only dispatches on the winner's kind.
- [ ] **Exactly one terminal.** Each flight leaves the live list in the step that resolves it; `FlightResolved` is raised exactly once per flight, **before** the WorldItem/domain events it causes in the same call scope; the tick-end record (story 008) never fires across a randomised run of 1 000 flights over the street fixture; no flight is processed after resolving.
- [ ] **TC-E-7.** At `worldItemCap` a throw is never refused and its landing succeeds; the Grid eviction rule (old mail ≥ 10 s first, then the oldest non-mail) is exercised once end to end through `CreateWorldItem`.
- [ ] **Ports for neighbours.** Mailbox and catcher winners are dispatched through `IMailboxLanding` (story 012) and `ICatchLanding` (story 023); a composition test asserts Throwing's production wiring binds the former; with `ICatchLanding` unbound no catcher spheres are supplied, so it is never invoked.
- [ ] **0 B.** 100 000 flight-ticks over the fixture allocate 0 B in the step (buffers preallocated).

---

## Implementation Notes

*Derived from ADR-0015 §4–§5 and the GDD T6 rows:*

- Files in `src/PerformativeMail.Sim/Throwing/`: `ThrowingStep.cs` (`ISimPhase` bound to `PhaseId.ThrowingStep`), `TerminalResolver.cs` (the winner → terminal switch; GROUND/WATER/TIMEOUT here, mailbox in 012), `WorldItemPlacement.cs` (the snapping rules, below), `IMailboxLanding.cs`, `ICatchLanding.cs`.
- **Read once at step start:** life states (for later catchers) and the armed-catcher list; nothing else is cached across flights.
- **Placement interpretation (flagged; decision logged):** T6 says "snapped to walkable ground within 1000 mm. On a solid footprint the search grows 1000 → 3000 → 6000 mm back along −v. Failing that, … the receiver's base". Implement as: attempt 0 = `TrySnapWalkable(ClampInsideMap(contact, 1000), 1000)`; then attempts at `contact − û·d` for `d` = 1000, 3000, 6000 mm (`û` = the horizontal flight direction in ‰, `Throw.RH`), each clamped and snapped with radius 1000; the first success wins. The golden vectors are TC-T6-3 b; record the exact reading in the Last Updated note if a stricter one is later ratified. Water never snaps.
- `CreateWorldItem(entry, posMm, origin = Thrown, onWater, createdTick)` is `production/epics/grid-inventory/story-013-worlditem-expiry-cap.md`'s API; the entry argument is the Flight-held handle from story 008 (`FlightEntryRef`). The water return and the 300 s expiry are Grid's, not re-implemented here.
- `FlightResolved` fields (story 006): terminal, `rank` of the winning receiver, `receiver` (destination/solid index/player slot as a raw id or 0), `contact`, `tick`, `unitsIn/unitsFumbled` (0 here), `worldItem`.
- Run-end teardown is story 013; do not add a flush shortcut here.
- Keep the step free of allocation by taking all `Span<>` scratch from `ReceiverScratch` (story 003).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 012: `MAILBOX_ACCEPT/MISDELIVER/REJECT`, the F8 drop point. Story 013: run-end flush, disconnect/pause. Story 023: CAUGHT/FUMBLE and the catcher spheres.
- BONK (M0), HIT_ENEMY and BELT (M-1b); receiver cylinders and lane faces stay empty.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowingStepTests.cs` (`[Trait("Id", "TC-T6-3")]`, `TC-E-1`, `TC-E-7`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 003, 008, 010; `production/epics/grid-inventory/story-013-worlditem-expiry-cap.md` (`CreateWorldItem`, water float/return); `production/epics/grid-inventory/story-012-intake-spawn-outfeed.md` (return to Intake); `production/epics/world-stub/story-002-tile-flags-bounds-clamp-snap.md` (`TrySnapWalkable`, `ClampInsideMap`); `production/epics/world-stub/story-006-m1a-street-and-fixtures.md`
- Unlocks: Stories 012, 013, 014, 015, 017, 020, 023
