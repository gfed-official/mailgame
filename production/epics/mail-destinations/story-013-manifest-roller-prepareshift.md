# Story 013: Manifest roller — `PrepareShift(SpawnProfile)`, re-roll on input change, lock at Delivery start, forecast

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete (greybox; re-roll and cursed rows dropped, 0 B advisory → TD-220 (16); IMailApi.PrepareShift not in the ADR-0017 Key Interfaces sketch — pending Evan, TD-220 (17) / TD-274 (b))
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~250 lines prod + ~250 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; re-roll and cursed rows dropped, 0 B advisory → TD-220 (16); IMailApi.PrepareShift not in the ADR-0017 Key Interfaces sketch — pending Evan, TD-220 (17) / TD-274 (b)); delivered by PR #88, merge 7cd3756; see Completion Notes). Earlier: 2026-11-04 (branch `docs/rulings-td208-td209`): AC-5 added — TD-208 item 8, approved by Evan 2026-11-04 9:23 AM PT by card click (card t271s1234, listing t271s1233); AC-6 added — TD-208 item 10, approved 9:56 AM PT (card t271s1241).

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
- [x] **AC-4 Stub wiring.** The M-1a shift stub (story 009) calls `PrepareShift` in Prep with `{shift 1, V, D, raidWindowSeconds 0, cursedShare 0, playerCount 1}`. On the M-1a street, the roll produces a non-empty manifest with every address in District 1 and every batch tick inside Delivery.
- [x] **AC-5 Current shift (from mail-destinations 007, TD-208 item 8).** At shift start `PrepareShift` sets Mail's current shift: after `PrepareShift` with profile shift N, Mail's current shift is N. *TD-208 item 8, approved by Evan 2026-11-04 9:23 AM PT by card click (card 'Stories 007 and 018: which of the items listed above do you approve?', card t271s1234, listing message t271s1233): "Approve TD-208 item 8 (mail code keeps its own shift number starting at 1; adds a step to story 013), as listed in your message above". In message t271s1233's words: TD-208 item 8: which shift it is, for now. Pay depends on the current shift, but nothing tells the mail code the shift number yet. What would land: the mail code keeps its own shift number, starting at 1. It isn't saved, and tests can set it. For example, in this milestone every delivery counts as shift 1, so nothing is late until real shifts arrive. This adds a step to story 013: set the current shift at shift start. Neither story 013 nor 009 has that step today. Uncredited detail (Mail Lead planning default, TD-208 item 8): doing it in `PrepareShift`; it writes story 007's `internal int CurrentShift` on `MailSystem` (starts at 1, not dumped) from `SpawnProfile`'s shift; whichever of story 007 and this story lands first adds the property.*
- [x] **AC-6 Wrong-delivery count reset (from mail-destinations 007, TD-208 item 10).** At shift start `PrepareShift` zeroes Mail's wrong-delivery count for the shift: after some misdeliveries, `PrepareShift` for the next shift leaves the count at 0. *TD-208 item 10, approved by Evan 2026-11-04 9:56 AM PT by card click (card 'Stories 007 and 018: how do you want the remaining items settled?', card t271s1241, values as listed in message t271s1233): "Approve TD-208 item 10 as listed, including the story 013 zeroing step". In message t271s1233's words: TD-208 item 10: delivery numbering, the "refused" message limit, and the wrong-delivery count. Every delivery message gets a running number, starting at 1. When a player keeps trying to deliver at the wallet floor, the "refused" message goes out at most once per house per player every 30 ticks (1 second). The 30 is fixed in code, not a tuning setting. The wrong-delivery count for the shift goes up by 1 for each delivery that used at least one wrong-house letter. It stops at 65,535 and resets to 0 at each shift start. All three are saved after the complaint data: the number first, then the floor times, then the count. For example, a player mashing Deliver at the floor at one house sends at most one "refused" message per second. This adds a step to story 013: zero the wrong-delivery count at shift start. Uncredited detail (Mail Lead planning default, TD-208 item 10): doing it in `PrepareShift`; it zeroes story 007's `misdeliveriesThisShift` on `MailSystem`; whichever of story 007 and this story lands first adds the field.*

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

**Status**: [x] Created and passing — `tests/integration/mail/ManifestRollerTests.cs`, PR #88 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #88 (merge `7cd3756`), package WP-C (M-1a wave 1), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). PR #95 (`eedc110`, TD-271 (1)) later let the debug playtest flags scale `ShiftStubSettings`' Delivery length and spawn budget; the roller is unchanged.
- **Built:** `ShiftManifest`, `ManifestRoller`, `SpawnProfile` (`Sim/Mail/Manifest`), `IMailApi.PrepareShift` (appended at the end of the interface), the lock at Delivery start, the current shift (AC-5) and the misdelivery reset (AC-6). As built (TD-220 (16), uncredited): batch ticks are offsets from the Delivery start recorded at the lock; the stub calls `PrepareShift` every Prep tick and an unchanged profile is ignored; `ShiftManifest.MaxUnits` 4096 is a code constant; missed batches are skipped, not deferred; the Mail dump appends a 53 B manifest-inputs block; `SpawnBudgetCents` 960 is a PLACEHOLDER on `ShiftStubSettings`.
- **ACs met:** AC-4 (stub wiring: a non-empty manifest with every address in District 1 and every batch tick inside Delivery), AC-5 (current shift set at `PrepareShift`), AC-6 (wrong-delivery count zeroed at shift start).
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 "drop re-roll and cursed variants"; held by the package TD row):** AC-1 — determinism of the roll is covered (same inputs, same manifest; fixed K draws stays binding, ADR-0005); the shuffled-address-order, V/shift difference and `cursedShare` rows were dropped → TD-220 (16). AC-2 — re-roll on a profile change and the three-changes-one-roll row dropped; the lock is built. AC-3 — no mailId is allocated in Prep; the 0 B assertion is advisory until M0 (erratum E2).
- **Open question for Evan (pending Evan, not approved):** `IMailApi.PrepareShift` is not in ADR-0017's Key Interfaces sketch; a text-only sync is an ADR change for Evan (TD-220 (17), TD-274 (b)). No ADR text was edited and nothing is blocked on it.
- **Tests:** `ManifestRollerTests`; PR #88 reports 6,256 tests, 0 failed on all four legs.

---

## Dependencies

- Depends on: Story 002, Story 009, Story 011, Story 012
- Unlocks: Story 014
