# Story 013: Manifest roller — `PrepareShift(SpawnProfile)`, re-roll on input change, lock at Delivery start, forecast

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/mail-destinations.md` (R10 the shift spawner; §Determinism). OQ21: provisional until the greybox.
**Requirement**: `TR-mail-002` (the per-shift manifest is a pure function of seed, shift, profile and addresses), `TR-mail-003` (fixed draw count), `TR-mail-019` (manifest roll ≤ 0.5 ms advisory; 0 alloc)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0017: Mail Table, Destination Registry and Wallet Port (primary, §10); ADR-0005 (stream per shift, fixed draws); ADR-0003 (Run calls down into Mail; flags set in T effective from T + 1)
**ADR Decision Summary**: The manifest roller uses the stream `RngDerive(runSeed, "mail", shift)` and draws exactly `K = 2·nb + 2N + max(0, N−1)` over the ascending `AddressId` list from the registry. It runs in phase 8 at the batch tick within ≤ 0.5 ms. Run Structure (the M-1a stub) calls `PrepareShift`. The same inputs reproduce the same manifest.
**ADR Version**: ADR-0017 2026-10-16; ADR-0005 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: use only Mail's streams (`mail` by shift) (ADR-0005 §2)
- Required: size every collection from `SimSettings`; 0 B in the tick path (ADR-0006 §8)
- Forbidden: re-rolling after the lock; any mailId allocation outside a batch tick (R10, M-R10-1a)
- Guardrail: manifest roll ≤ 0.5 ms (advisory); exactly K draws (M-PERF-1e)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to this story (FX-I50 for determinism; the M-1a street for the stub):*

- [ ] **AC-1 (M-DET-1)** Rolling seed S, shift 3, profile P on FX-I50 twice (the second time with the address list passed in shuffled order) gives an identical hash over (batch, tick, kind, addressId, flags, spawnValue) and an identical forecast. **b** A different V or shift gives a different hash. **c** `cursedShare` 0 vs 100 gives identical addresses and different flags. **d** mailIds are identical from the same counter start (once fired, story 014).
- [ ] **AC-2 (M-R10-1b, c, d)** Changing V and restoring it re-rolls to a manifest hash identical to the first; three profile changes in one tick cause exactly one re-roll. A profile change on or after the Delivery-start tick is ignored (locked). The forecast equals the manifest's counts by kind × district and sums to N.
- [ ] **AC-3 (M-R10-1a, M-PERF-1e)** `PrepareShift` and re-rolls allocate no mailId (the counter is unchanged after 100 Prep ticks with no special spawns). A shift-5 roll makes exactly K draws (story 012's counter) and allocates 0 B after warm-up (the manifest buffers are preallocated from `SimSettings`).
- [ ] **AC-4 Stub wiring.** The M-1a shift stub (story 009) calls `PrepareShift` in Prep with `{shift 1, V, D, raidWindowSeconds 0, cursedShare 0, playerCount 1}`. On the M-1a street, the roll produces a non-empty manifest with every address in District 1 and every batch tick inside Delivery.

---

## Implementation Notes

*Derived from ADR-0017 §10 and GDD R10:*

- **Files:** `src/PerformativeMail.Sim/Mail/Manifest/ShiftManifest.cs` (preallocated arrays: per unit kind, `AddressId`, flags, spawnValue, batch index; per batch `tick`, `count`; plus the forecast table), `Manifest/ManifestRoller.cs` (pure: inputs → fills a `ShiftManifest`), `Manifest/SpawnProfile.cs` (readonly struct `{shift, V, deliverySeconds, raidWindowSeconds, cursedShare, playerCount, playerScalePm, foundValueBudget}`; `foundValueBudget` is unused at M-1a).
- **`IMailApi.PrepareShift(in SpawnProfile p)`** (Run only): stores the profile and marks the manifest dirty. The roll happens once in Mail's phase 8 of the same tick if dirty and not locked, which gives "three joins in one tick → one re-roll". The lock is set when the stub opens deliveries (`SetDeliveriesState(open: true)` at Delivery start S is applied from S; a `PrepareShift` at or after S is ignored and counted).
- **Composition of the roll:** F3 counts (story 011) → a kind-ordered unit list → story 012's shuffle, schedule and draws → F1 values per unit (story 011, using the drawn address's district). `deliveryStartTick` is the stub's Delivery start (known at Prep in the stub's settings). The address list is the ascending `AddressId` list of unlocked destinations from the registry (story 002); sorting is a plain `uint` order.
- **Profile at M-1a:** V and D come from the stub settings (`ShiftStubSettings`, story 009): flag the V default PLACEHOLDER (the GDD's F3 solo shift-1 example uses V = 960; spec 11 quota × `spawnOverhead` is Run Structure's). `playerScalePm` is also stored here for story 006's team cap.
- Dump the manifest (or its inputs + lock flag, which reproduce it) in the `Mail` section, so T1 holds across Prep.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 014: firing batches at their ticks, allocating mailIds and handing units to Inventory.
- District unlocks across shifts, Payday and the Prep forecast UI (M1).
- `SpawnSpecial` (R15, later).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/mail/ManifestRollerTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002, Story 009, Story 011, Story 012
- Unlocks: Story 014
