# Story 019: Host side — `PlayersStep` (phase 3) sample ingest, `IPlayersApi.CheckInteraction` and the leash query

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (9 of 9) — host validation.** `IPlayersApi.CheckInteraction` is the range gate every range-gated op handler calls first: Grid Inventory `Open` / `Pickup` (`production/epics/grid-inventory/story-019-range-gated-open-pickup-leash.md`), Manual Delivery `DeliverMatching`, later Mount / Shop. The leash query serves the Grid Inventory auto-close.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P13 host-side validation; F5 host range check; P2 "the host never steps an Alive remote player's motion"; Edge Cases "knockdown and a completed hold on the same tick")
**Requirement**: `TR-movement-011`/`012` (no host hold timer — the host only validates), `TR-movement-003` (client-authoritative movement; host authors Dead/Frozen bodies only — M-1a: ingest, store and never step an Alive owner; the Dead/Frozen path is exercised by tests only), `TR-movement-010` (host range check — **M-1a minimal subset only**; the registry maps it to ADR-0020, M0, coverage gap)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P13-1, PCM-F5-1 (M-1a rows), PCM-P2-2.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§5 host validation chain; §6 phase 3) — Accepted 2026-10-16 (primary); ADR-0013 (§1 host role calls the same `OwnedCarrier.Step` with `HostAuthored`), ADR-0003 (phase 3 `PlayersStep`, sample drain), ADR-0009 (range-gated ops call `IPlayersApi`) secondary — Accepted
**ADR Decision Summary**: `IPlayersApi.CheckInteraction(ActorId, AnchorRef, in ClaimedPosition) → InteractionCheck` returns the **first** failure in the fixed order `Dead → Frozen → NoTarget (anchor unknown) → Unavailable(reasonKey) (IsAvailable on host state) → PositionStale / OutOfRange` (F5: the claim's chest within `RangeMm + rangeSlackMm` of the anchor **and** the claim within `claimToleranceMm` of a history sample or the dead-reckoned sample; stale if nothing received for `positionStaleTicks`). It is read-only and legal in any phase 0–12. The host range is the anchor's single `RangeMm` plus `rangeSlackMm` — no per-verb host range. `ClaimedPosition` is feet `int x, y, z` (12 B) carried in every range-gated payload. The F5 ring and dead-reckoning are ADR-0020's (M0); the predicate and its order are fixed here. Phase 3 (`PlayersStep`, host role) ingests the T−1 sample latch and runs the host-role solver only for host-authored (Dead / Frozen) bodies.
**ADR Version**: ADR-0014 2026-10-16; ADR-0013 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Use `byte` flags, not `bool`, in the 12 B `ClaimedPosition` carrier struct and results.

**Control Manifest Rules (Feature layer)**:
- Required: range-gated op handlers call `IPlayersApi.CheckInteraction(actor, anchor, ClaimedPosition)` first and map its result; the chain is `Dead → Frozen → NoTarget → Unavailable → PositionStale/OutOfRange`, the host range is `RangeMm + rangeSlackMm`, and `ClaimedPosition` (12 B) rides in every range-gated payload (ADR-0014 §5)
- Required: the host never steps an Alive remote carrier; the host role calls the same `OwnedCarrier.Step` with `HostAuthored` input (ADR-0013 §1)
- Required: life-state deadlines are absolute ticks; Players never calls dependants upward (ADR-0014 §6)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P13-1, PCM-F5-1, PCM-P2-2), scoped to **M-1a**. Deferred to M0 (ADR-0020, no story this epic): the 9+3-tick position-history ring, dead-reckoning corroboration, `Teleport` history clearing and `seq` discard — see Implementation Notes. The GDD's "Deliver accepts at 3500 (per-verb r = 3000)" row is superseded by ADR-0014 §5 (single `RangeMm`).*

- [ ] **PCM-P13-1 (parametrised).** a request failing two checks reports the **first** in `dead → frozen → no_target → unavailable → out_of_range / position_stale` (e.g. Dead + unknown anchor → `Dead`; Frozen + unavailable → `Frozen`; unknown anchor + out of range → `NoTarget`; unavailable + stale → `Unavailable`); `CheckInteraction` is callable in phases 0–12 without `TickGuard.AssertMutable`, mutates nothing and raises no event (a test snapshots the Players dump hash before/after); the reply routing "to the requester only" is the op layer's.
- [ ] **PCM-F5-1 (M-1a rows).** Open/Pickup-style anchor `RangeMm` 2500: a claim whose chest point is 3000 mm from the anchor point **accepts** (`2500 + rangeSlackMm 500`), 3001 → `OutOfRange`; the claim within `claimToleranceMm` (1500 mm, 3D) of the player's newest ingested sample accepts, 1501 → `OutOfRange`; an anchor 2000 mm above the feet at a 2500 mm client chest distance accepts (3D distance, chest = claim + `(0, chestMm, 0)`); a Deliver anchor (`RangeMm` 2500) accepts at 3000 and rejects at 3001; **30 silent ticks** still pass, **31** → `PositionStale` (and open panels are kept — the leash below never fires on a stale position).
- [ ] **Leash query.** `IPlayersApi.LeashExceeded(PlayerSlot, in Vec3Mm anchorPoint, int leashMm)` is true iff the position is **not stale** and `|chest(newest) − anchor| > leashMm` (strict): false at exactly 3000 mm, true at 3001; always false when the position is stale (no transform for > `positionStaleTicks`); Grid Inventory story 019's H-R10-1 leash clauses (2.99 m stays open, 3.01 m closes, stale never closes) pass against it.
- [ ] **Seeding.** `PlayerJoin` seeds the host position record at the spawn position with `LastReceivedTick = join tick` (Story 001's record), so an `Open` at the spawn point on the next tick passes (no `position_stale` right after joining).
- [ ] **PCM-P2-2.** `PlayersStep` (phase 3) ingests the tick's latched `TransformSample`s in slot order then sequence order for **Alive** players: the newest becomes the stored host position (feet X, Y, Z; yaw; swimming bit if the sample carries one), `LastReceivedTick` = the sample's `ReceivedTick`; over 100 ticks of an Alive remote player's reports the host calls `OwnedCarrier.Step` **zero times** for it (spy counter `hostCarrierSteps`); for a body whose life state is **Dead or Frozen** (set directly by the test fixture — knockdown/disconnect are M-1b/M0) the host authors the transform with `HostAuthored` input, ground-drops it, and **ignores the owner's samples**.
- [ ] **Unavailable on host state.** `Unavailable(reasonKey)` comes from `IInteractable.IsAvailable` evaluated with the host-side `InteractionContext` (host state); the framework gate's swimming reason applies when the stored sample marks the player swimming and the anchor lacks `AllowWhileSwimming`; Panel is client-only and never evaluated on the host.
- [ ] **Counters / budget.** `CheckInteraction` is O(1) and allocates 0 B; `PlayersStep` for 8 players with 2 000 registered anchors (none stepped) stays within a recorded, advisory `players.hostStep` budget (≤ 0.20 ms p99 target; logged only — TR-movement-021 has no ADR coverage).

---

## Implementation Notes

*Derived from ADR-0014 §5–§6, ADR-0013 §1, GDD F5/P13:*

- Files in `src/PerformativeMail.Sim/Players/`: `PlayersStep.cs` (phase-3 binder: `Bind(PhaseId.PlayersStep, …)` in composition order), `HostPositionTable.cs` (per slot: newest feet `Vec3Mm`, yaw, flags, `LastReceivedTick`), `IPositionHistory.cs` + `NewestSamplePositionHistory.cs` (the M-1a corroboration: newest sample only — the seam ADR-0020 fills with the 9+3 ring and dead-reckoning), `InteractionCheck.cs` (`Kind` + `StringId Reason`), `ClaimedPosition.cs`, `CheckInteraction` on `PlayersApi`, `LeashExceeded`.
- Samples arrive via `ISampleLatch` (sim-core story 021: `Count`, `this[i]`, `ReceivedTick`, `LastSeq(slot)`), injected into Players by composition. `TransformSample` is **PROVISIONAL** there (position mm ×3, yaw/pitch BAM, flags byte); if its flags byte has no Swimming bit, define the bit in the Players-side reader (`SampleFlags`) and coordinate with the sim-core owner — **do not edit sim-core story files**. ADR-0020 (M0) owns the final layout.
- Who writes the samples at M-1a (the host's own client in solo): Story 020's `ITransformSampleSink`, wired to `SimWorld.TryWriteSample` by the host shell (`production/epics/godot-host-shell/story-005-host-session-composition-ports-render-clock.md`). Without that wiring `CheckInteraction` fails `PositionStale` after 30 ticks; Story 020 documents the wiring requirement and a test double covers this story.
- **Scope decision (logged):** the epic's out-of-scope list defers "the host position-history ring and range check F5" to M0; ADR-0014 §5 fixes the predicate and its order and its Risks section says "M-1a solo runs through loopback; the Sim-side ring is pulled forward in the architecture.md shape". This story implements the predicate, the order, staleness and the leash with corroboration against the *newest* sample behind `IPositionHistory`; the ring (`rangeHistoryTicks` 9 + 3) and dead-reckoning are one drop-in implementation of that seam at M0. No M-1a acceptance criterion needs the ring.
- `ActorId` → `PlayerSlot` via the read-only `SlotTable` (ADR-0004); an actor that is not a present player is `NoTarget`-adjacent but is rejected earlier by the Core pre-check (`unknown_actor`, sim-core story 020).
- The Dead/Frozen host-role step uses a zero `CarrierInput` with `HostAuthored = true`: skips water-entry checks for Dead bodies and player push (push is out of scope anyway) and ground-drops. Only a test fixture sets these life states at M-1a.
- `rangeSlackMm` ≥ `incumbentSlackMm + 100` and `rangeMm + rangeSlackMm ≤ leashMm` are content-data validator rules (CV-PLR-01/09, content-data story 011) — assumed to hold.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- The position-history ring, dead-reckoning, `Teleport`/`TeleportAck`, `seq` discard, transform replication (ADR-0020, M0). Knockdown chain, clock-in, standard issue, on-break (P15–P19, M-1b/M0).
- The op handlers that *call* `CheckInteraction` (Grid Inventory story 019; Manual Delivery) and their reply mapping.
- Mounted-carrier seat positions (Vehicles, M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/CheckInteractionTests.cs` (`[Trait("Id", "PCM-P13-1")]`, `PCM-F5-1`), `tests/unit/player/PlayersStepTests.cs` (`PCM-P2-2`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001 (`PlayerRecord`, `LifeState`), 002, 012 (registry, `IInteractable`), 013 (swim reason), 010 (solver locked for the host-role step), `production/epics/simulation-core/story-021-transform-sample-ring-latch.md` (`ISampleLatch`), `production/epics/simulation-core/story-011-phase-table-builder-tick-loop.md` (phase 3 binding), `production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md` (`unknown_actor`), `production/epics/simulation-core/story-018-id-allocators-slottable.md`
- Unlocks: `production/epics/grid-inventory/story-019-range-gated-open-pickup-leash.md` (explicitly waits on this story); manual-delivery epic (`DeliverMatching` range gate); Story 020
