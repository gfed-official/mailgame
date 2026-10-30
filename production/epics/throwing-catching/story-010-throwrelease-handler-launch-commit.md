# Story 010: `ThrowReleaseHandler` Commit — F1 launch at the apply tick, `DetachForThrow`, the `FlightRecord`, `ThrowLaunched` and the `ok` reply

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines prod + ~350 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-29 — owner AC added for the `ThrowingSystem.Bind` wiring (op handler; event declarations if 006 has not added them) (docs PR `docs/next-readiness-pc021-tc003`).

> **Op/host story (4 of 5).** The phase-0 handler that turns a validated release into a flight. After this story a throw leaves the hotbar and a `FlightRecord` exists; nothing sweeps it yet (story 011).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T2 what launches; T4 "Launch tick", "The host creates the `FlightRecord`", "Then, in the same tick …", "A release can never strand an entry"; Edge Cases "If a release is applied on the thrower's knockdown tick", "If a throw is released in Prep, Payday or Draft")
**Requirement**: `TR-throwing-001` (host launches at the apply tick, no back-dating), `TR-throwing-009` (`FlightRecord` fields, snapshotted `thrownPm`), `TR-throwing-013` (the entry moves PLACED → FLIGHT through `IInventoryApi` only); `TR-throwing-011` (the `TAT` update) moved with the bucket to story 028 (Deferred, M0; erratum E5)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T2-1, TC-T4-1 c, TC-N-1 (ordering half), TC-E-4 (launch half), TC-F1-1 b (`thrownPm` snapshot, Later vector).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§1 "Launch at the apply tick `T`" order of effects) — Accepted 2026-10-16 (primary); ADR-0009 (`DetachForThrow`, Flight ledger, one version seal per touched container), ADR-0008 (event ordering: handler events precede its reply) and ADR-0010 (StatSheet latch at op start, `ThrowRangePermille`, `ThrownCorrectPayMult`) secondary — Accepted
**ADR Decision Summary**: Launch is at the apply tick `T` (no back-dating): F1 → `tat'` carried unchanged at M-1a (the `TAT ← max(TAT, T) + throwCooldownTicks` bucket update is story 028, Deferred, erratum E5) → `IInventoryApi.DetachForThrow(slot, entryId, units)` (PLACED → FLIGHT, its `Removed` event) → append the `FlightRecord` → raise `ThrowLaunched` → reply `ok`. `Validate` is pure; `Commit` never fails and never recomputes. For the thrower the log order is Grid `Removed` → `ThrowLaunched` → `ok`.
**ADR Version**: ADR-0015 2026-10-16; ADR-0009 2026-10-16; ADR-0008 2026-10-16; ADR-0010 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: every mutating call starts with `TickGuard.AssertMutable()` and opens `using var scope = log.BeginCall();` — one atomic mutation; handlers `Validate` (pure) then `Commit` (never fails) then exactly one `Reply` (ADR-0003 §5, ADR-0007, ADR-0008 §5)
- Required: inventory interaction only through `IInventoryApi`; StatSheet read at op start and snapshotted (`thrownPm`) (ADR-0009 §6, ADR-0010)
- Forbidden: back-dating the launch; any RNG draw; Throwing writing container or ledger state
- Guardrail: 0 B on the throw path

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T2-1, TC-T4-1 c, TC-N-1, TC-E-4, TC-F1-1 b), scoped to this story:*

- [ ] **TC-T2-1.** FX-L20 selected and thrown: exactly one `FlightRecord` holds all 20 `mailIds` (by reference), the `entryId` is kept, the hotbar slot is empty, the container `version` bumped by exactly 1 with one seal, and the 20 units are in the Flight ledger column. A standard-issue axe (light, value 0) throws 1 unit as non-mail with its `ItemDef` `weightClass`.
- [ ] **FX-T launch.** The FX-T release (feet (10000, 0, 20000), dir (0, 3406, −16026), right-handed, light) stores origin (10250, 1200, 19700) and vel (0, 2911, −13695); `ThrowLaunched` carries the same integers plus `launchTick` = the apply tick `T`.
- [ ] **TC-T4-1 c.** `launchTick == T`; the same tick's phase 4 does not sweep the new flight (age 0 never sweeps); the first sweep is at T+1 (asserted with story 011's step once present; here by `age == 0` at phase 4).
- [ ] **Snapshots.** `thrownPm` = `IStatSheet.Read(ThrownCorrectPayMult, thrower)` at the apply tick (default 1000; a +100 Add → 1100, the Later vector); a later change to the sheet, or the thrower disconnecting, leaves the record unchanged. `ThrowRangePermille` is read once for F1 (default 1000 → `speedPm` 1000). `classPts` is the entry class's weight points (1/3/8).
- [ ] **Release tick.** `releaseTick = T`; `openAtRelease` = whether deliveries were OPEN at `T` (Mail's deliveries state, read-only). Releases in Prep, Payday and Draft launch normally; their boxes are CLOSED at landing (story 012).
- [ ] **Claimed origin.** With a corroborated claim the flight launches from it; with `originClamped` (story 009) it launches from the newest sample and `ThrowLaunched.flags` bit0 is set. A blocked chest → origin probe (story 003) launches from `feet + (0, 1000, 0)` (TC-E-1 a) and still carries `tat'`.
- [ ] **TC-N-1 ordering (loopback).** For one launch the thrower's log order is the inventory `Removed`/`InvVersion` records, then `ThrowLaunched`, then the `ok` reply; every other peer's batch contains `ThrowLaunched` and no inventory records for the thrower's container; the actor is the transport peer's, never a payload field.
- [ ] **Cooldown carried (M-1a).** `Commit` writes the `ThrowPlan.tat'` that story 009 computed, unchanged (no bucket at M-1a, erratum E5); two valid releases by one thrower applied at ticks 100 and 101 both launch end to end through the op ring with the real handler, and no reply carries `throw_cooldown`. The bucket form of this AC moved to story 028 (Deferred to M0).
- [ ] **TC-E-4 (launch half).** A release applied on the tick the thrower is (fixture) knocked down launches if the life state is still Alive at the handler (`Inputs.Apply` precedes damage resolution); the entry is in FLIGHT, not in the pile, and the flight continues. One tick later → `dead`.
- [ ] **Commit never fails.** If `DetachForThrow` reports anything but success after a passing validation, the handler records a fault (`SimCheckRing`, counter `throwCommitFault`) and replies `bad_request` without appending a record or raising `ThrowLaunched`; a test injects a failing inventory double. In normal operation this path is unreachable.
- [ ] **Owner: binding in `ThrowingSystem.Bind`.** This story adds `builder.BindOp(ThrowRelease descriptor, handler)` for the `ThrowRelease` handler in `ThrowingSystem.Bind` (`SimWorldBuilder.BindOp`, `Core/SimWorldBuilder.cs:157`), and the `builder.BindEventKind` declarations for `ThrowLaunched`/`FlightResolved` (`:114`) if story 006 has not already added them there; one composition test shows a built world accepts a `ThrowRelease` op and declares both event kinds.
- [ ] **Hop and relay.** `hop = 0` for every flight at M-1a (T13 relay hops are M0 and deferred).
- [ ] 1 000 launches allocate 0 B in `Commit`.

---

## Implementation Notes

*Derived from ADR-0015 §1 and ADR-0009 §6:*

- Files in `src/PerformativeMail.Sim/Throwing/Ops/`: `ThrowReleaseHandler.cs` (`Apply` = story 009 `Validate` → `Commit` → `Reply`), registered on the descriptor from story 006 for phase 0 (`production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md`). `LaunchMath.Compute` is called with the real `IChestOriginProbe` from story 003.
- **What `DetachForThrow` must return** (grid-inventory story 015's `DetachResult`): the Flight-held entry handle, the unit count, the `mailIds` by-reference handle, the item/mail kind and the weight class, so `classPts`, `kindDef` and the baseSpeed class are derivable without a second inventory read. Confirm against `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` and `story-017-unitledger-conservation.md` when implementing; if a field is missing, add a read-only property additively and log it.
- **`openAtRelease`:** read-only Mail accessor for the deliveries state (`open`, `closeTick`) — Mail story 009 stores them but exposes them only through `TryGetDestinationFacts` per destination. Add an additive `IMailApi.GetDeliveriesState(out bool open, out uint closeTick)` read (or derive from any destination's facts) and log the choice. Mail judges grace itself from `releaseTick` (`production/epics/mail-destinations/story-008-thrown-landing-and-tick-order.md`), so this flag is for the record, the dump and the run-end flush rules only.
- Stat reads: `StatKey` constants from `production/epics/content-data/story-006-stat-statdef-ruleflag.md`; the latch-at-op-start rule is CD20 (`production/epics/content-data/story-014-statsheet-build-read-explain.md` note).
- `ThrowLaunched` fields (story 006): `kindDef` = the entry's def ordinal; `units` u8 (an entry never exceeds `maxStack` ≤ 255, CV-GRD-01).
- Keep `Commit` branch-free of validation; all reply codes other than `ok` come from story 009.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 009: the validation chain. Story 011: sweeping, terminals, `FlightResolved`. Story 007: the client sender. Story 017: the client mirror and ghost reconciliation.
- Spawn-protection end on a HIT_ENEMY resolution (M-1b). `hop`/relay (M0). Disconnect while the release is *queued* is covered by story 009's `frozen` case; flight continuity after disconnect is story 013.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/throwing/ThrowLaunchTests.cs` (`[Trait("Id", "TC-T2-1")]`, `TC-T4-1c`, `TC-N-1`, `TC-E-4`) — must exist and pass on net10.0 and net8.0, using a real `SimWorld` with Grid Inventory, Throwing and stubbed Players/Mail/World

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 003, 006, 008, 009 (the validation chain; its leaky-bucket cooldown moved to story 028, Deferred to M0 — so the bucket form of the "Cooldown commit" AC moved to 028 and the `TAT ← max(TAT, T) + throwCooldownTicks` step waits on it; at M-1a `CommitTat` writes `ThrowPlan.tat'` unchanged and `throw_cooldown` is never replied); `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`DetachForThrow`); `production/epics/grid-inventory/story-017-unitledger-conservation.md` (Flight ledger); `production/epics/content-data/story-014-statsheet-build-read-explain.md` (`IStatSheet`); `production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md` (deliveries state)
- Unlocks: Stories 011, 012, 013, 014, 015, 017
