# Story 009: `ThrowRelease` host validation — the ordered check chain and claim-and-corroborate origin

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **Split 2026-10-23.** The leaky-bucket cooldown (`TAT`; ADR-0015 §6, erratum E5) — the TC-T1-2 bucket AC, `ThrowCooldown.TryAccept`/`CommitTat`, the `throwCooldownTicks`/`throwBurstTicks` balance reads and the N = 7 precedence row — moved verbatim to `story-028-deferred-leaky-bucket-cooldown-tat.md` (Deferred to M0). Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all". What stays here is the ordered validation chain (check 7 keeps its slot and its `throw_cooldown` reason code but accepts unconditionally at M-1a) and claim-and-corroborate origin.

> **Op/host story (3 of 5).** The pure `Validate` half of the `ThrowRelease` handler: first failing check wins, nothing mutates, no id is allocated and no event is raised. These are consistency checks, not anti-cheat (spec 13 §4.3).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T1 who can throw and when; T4 "Host validation" table; Edge Cases "If a replayed `seq` arrives", "If the entry changed between aim and host apply", "If the thrower disconnects before their release is applied")
**Requirement**: `TR-throwing-010` (fixed host validation chain; claim-and-corroborate origin — registry maps this to ADR-0021, coverage gap; ADR-0015 §1 fixes the chain for M-1a, which this story implements). `TR-throwing-011` (leaky-bucket cooldown) is story 028's (Deferred, M0; the team cap ≤ 48 flights/s is ADR-0021's).
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T1-1 a, TC-T4-1 a/b/d, TC-F1-1 e (host-bounds half is story 001's), TC-E-5 a, TC-E-6 a. TC-T1-2 a is story 028's.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§1 validation chain; §6 leaky bucket is story 028's) — Accepted 2026-10-16 (primary); ADR-0007 (validate-then-mutate, `Reply`, reasons ≥ 64), ADR-0014 §5 (claim tolerance, position staleness, `ClaimedPosition`) and ADR-0009 (`DetachForThrow` semantics) secondary — Accepted
**ADR Decision Summary**: The first failing check is the reply: 1 malformed or entry not in the sender's hotbar → `bad_request`; 2 entry unknown, not the selected slot, or `count ≠ units` → `stale_entry`; 3 not Alive → `dead`; 4 Frozen → `frozen`; 5 swimming → `swimming`; 6 not on foot → `unavailable`; 7 leaky bucket `T < TAT − throwBurstTicks` → `throw_cooldown`; 8 direction norm or elevation out of bounds → `bad_direction`; 9 claimed feet not corroborated → **not a rejection**: use the newest sample and set `originClamped`; 10 `liveFlights == maxFlights` → `flight_cap` (counted; unreachable once the M0 bucket exists). At M-1a check 7 is an always-accept stub (E5): the `TAT` arithmetic and `throw_cooldown` rejections are story 028's.
**ADR Version**: ADR-0015 2026-10-16; ADR-0007 2026-10-16; ADR-0014 2026-10-16; ADR-0009 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Payload read with `MemoryMarshal.Read` after an explicit length check (`bad_request`, never an exception); `byte` flags only.

**Control Manifest Rules (Feature layer)**:
- Required: op handlers validate fully before mutating, allocating ids, drawing RNG or raising events; reasons keyed `(OpKind, Reason)`; every reason precedence chain is one ordered sequence covered by a parametrised "violate every later rule too" test (ADR-0007, ADR-0009 Implementation Guidelines)
- Required: ints widened to `long` before squaring (host bounds); the actor is taken from the transport peer (`Op.Actor`), never from the payload
- Forbidden: validation that allocates ids or touches the ledger
- Guardrail: `Validate` O(1), 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T1-1, TC-T4-1, TC-E-5, TC-E-6), scoped to this story:*

- [ ] **TC-T4-1 (precedence, parametrised).** For N = 1…6, a request violating check N **and** check N+1 gets check N's reply (`bad_request`, `stale_entry`, `dead`, `frozen`, `swimming`, `unavailable`); a request violating only 8 gets `bad_direction`; only 10 gets `flight_cap`. Check 7 (`throw_cooldown`) keeps its position and reason code and accepts every request at M-1a; the parametrised test is written so the N = 7 row is one added `[InlineData]` when story 028 (M0) lands.
- [ ] **TC-T1-1 (parametrised).** A valid release validates for a walking, sprinting, airborne, wading (899 mm, entered from dry land), OnSupport (velocity not inherited — no support data is read) or spawn-protected carrier, and the validator **never touches** spawn protection. Swimming (e.g. still swimming at 800 mm per the Player Character hysteresis) → `swimming`; Dead → `dead`; Frozen / on break → `frozen`; Mounted → `unavailable`.
- [ ] **TC-E-5 a (entry changed after aiming, parametrised).** merged or split (count ≠ `units`), sorted into a new entry (id retired), selection moved off it → `stale_entry`; a replayed `seq` after the entry went to FLIGHT (entry unknown) → `stale_entry`; the cursor entry, a container entry, or an entry that is not in the sender's hotbar → `bad_request`; in every case the slot keeps its contents and a counter ticks.
- [ ] **TC-E-5 / TC-E-4 (liveness).** A release applied while the thrower is Alive validates; the same release one tick after a (fixture) death gets `dead`. (The knockdown chain itself is Player Character / M-1b; a fixture sets the life state.) A release queued before the thrower's disconnect validates; one after it gets `frozen` (TC-E-6 a validator half).
- [ ] **TC-T4-1 b (claim-and-corroborate).** Claimed feet within `claimToleranceMm` (1500, 3D) of the nearest history sample validate with `feetUsed = claim`; 1500 mm (3D) passes; 1501 mm validates with `feetUsed = newest sample`, `originClamped = true` and **no rejection**. With no history sample at all (empty history / position stale beyond 30 ticks) the release validates with the host's last known position and `originClamped = true`; a thrower who has never reported is never rejected on this check.
- [ ] **TC-T4-1 d (purity).** After any rejection a Grid snapshot (slot, count, container version) and the Throwing dump hash are byte-identical to before the call; the validator allocates 0 B, raises no event and draws no RNG.
- [ ] **Output plan.** A passing validation returns a stack-only `ThrowPlan { entry, units, feetUsed, originClamped, dir, side, tat' }` that story 010 commits without recomputation (`Commit` never fails and never re-validates). At M-1a `tat'` equals the slot's current `TAT` (story 028 fills it).
- [ ] **`liveFlights`.** `flight_cap` is returned when `FlightTable` is full; the counter `flightCapHits` increments; a test fills 160 and asserts the 161st release is the only one refused.

---

## Implementation Notes

*Derived from ADR-0015 §1, ADR-0014 §5 and ADR-0007:*

- Files in `src/PerformativeMail.Sim/Throwing/Ops/`: `ThrowValidator.cs` (pure `static ThrowCheck Validate(in ThrowReleasePayload, ActorId actor, uint applyTick, in ThrowHostFacts facts, out ThrowPlan plan)`), `ThrowHostFacts.cs` + ports. Check 7 is a named always-accept stub inside the validator (keep the slot, the reason code and the `[InlineData]` shape so story 028's `ThrowCooldown.cs` drops in without reordering).
- **Ports** (Throwing-defined, implemented by adapters over the owning systems; all read-only):
  - `IHotbarSelection { byte SelectedIndex(PlayerSlot) }` — **unowned seam** (see story 004 and EPIC.md): the host needs the selected hotbar slot; no grid-inventory/input-controls story owns the state or an op that sets it. Until one does, only the test double exists; production wiring is not available and check 2's "not the selected slot" arm cannot be exercised end to end.
  - Inventory read: a pure `CheckDetachForThrow(PlayerSlot, EntryId, byte units) → {Ok, BadRequest, Stale}` — **additive read-only member on `IInventoryApi`** that is the *same* decision function `DetachForThrow` uses internally (ADR-0009 requires a pure `Validate`/`Commit` split, so it exists inside the mutator). Grid story 015 exposes only the mutating `DetachForThrow`; add the read-only wrapper in the grid-inventory code additively and log it (do not edit grid-inventory story files). Without it the chain order cannot be honoured, because checks 3–10 are Throwing's and must run after 1–2 without mutating.
  - Player facts: `IPlayersApi.GetLifeState` exists (`production/epics/player-character/story-001-players-module-carrier-state-join-binding.md`); `IsSwimming(slot)` and `IsMounted(slot)` read-only members are **not yet on `IPlayersApi`** — add them additively (swimming bit from the stored host sample, `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md`; mounted is always false at M-1a) and log. "On break" is `LifeState.Frozen`.
  - Corroboration: `IClaimCorroborator { Corroboration Check(PlayerSlot, in Vec3Mm claim, int toleranceMm, out Vec3Mm newest, out bool haveHistory) }` as an adapter over Player Character's `IPositionHistory` seam (story 019: newest-sample implementation at M-1a; the 9+3-tick ring is ADR-0020/M0). With the newest-only seam, "nearest history sample" is the newest sample; the test uses a multi-sample double.
- Reply mapping: `ThrowReason` (story 006) → `Reply` with the descriptor's reason; success returns the plan, the handler (story 010) builds `ok`.
- The host bounds check (8) is story 001's `LaunchMath.ValidateDir`; call it, do not duplicate. `n` via `ISqrt` with long components.
- `claimToleranceMm` from the Players settings record (do not copy the number). `T` is the apply tick from `TickContext`/the op's apply tick. `throwCooldownTicks`/`throwBurstTicks` are not read here (story 028).
- Do **not** read `IStatSheet` here: `ThrowRangePermille` and `ThrownCorrectPayMult` are read at commit (story 010).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 028 (Deferred, M0): the leaky-bucket cooldown — TC-T1-2, `ThrowCooldown`, the `TAT` arithmetic and `throw_cooldown` rejections. Story 010: the commit (F1, detach, record, event, reply `ok`) and handler registration. Story 001: dir bounds. Story 007: the client-side cooldown hold.
- The team flight cap ≤ 48/s and `FlightBatch` (M0, ADR-0021). Spawn-protection ending on HIT_ENEMY (M-1b, TC-X-4).
- Knockdown, on-break and rejoin state transitions (Player Character P15–P19, M-1b/M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowValidationTests.cs` (`[Trait("Id", "TC-T4-1")]`, `TC-T1-1`, `TC-E-5`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 006, 008; `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md` (host position table, `IPositionHistory`); `production/epics/player-character/story-001-players-module-carrier-state-join-binding.md` (`GetLifeState`); `production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md` (`DetachForThrow`, hotbar entry rules); `production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md` (actor stamping, `Reply`)
- Unlocks: Story 010; Story 028 (Deferred, M0)
