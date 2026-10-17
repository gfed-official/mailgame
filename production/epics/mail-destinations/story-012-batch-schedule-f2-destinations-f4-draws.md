# Story 012: Batch schedule (F2), destination and street-pair draws (F4), and the fixed RNG draw count on the `mail` stream

> **Epic**: Mail & Destinations (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~250 lines prod + ~300 lines tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/mail-destinations.md` (R10 shift spawner; F2, F4; §Determinism draw order). OQ21: provisional until the greybox.
**Requirement**: `TR-mail-002` (per-shift manifest a pure function of seed/shift/profile/addresses; stream `mail`), `TR-mail-003` (fixed RNG draw count; `NextInt` without a rejection loop), `TR-mail-004` (separate `mail.special` stream, so re-rolls never move Lost Parcels)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: RNG Streams and Derivation (primary: per-shift indexed `mail`/`mail.special` streams, single-draw `NextInt`, fixed draw count); ADR-0017 §10 (`RngDerive(runSeed, "mail", shift)`; exactly `K = 2·nb + 2N + max(0, N−1)` draws over the ascending `AddressId` list)
**ADR Decision Summary**: Mail draws spawn timing and addresses from its own `mail` stream, indexed by shift, in a fixed order with a fixed count. `NextInt(n)` is a single draw (`next32 × n >> 32`), never a rejection loop. Specials use `mail.special`, so a manifest re-roll never moves them. The retired streams `mail.spawn`/`mail.address` must not be used.
**ADR Version**: ADR-0005 2026-10-16; ADR-0017 2026-10-16

**Engine**: Godot 4.7.1 (.NET) — no Godot API used | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer + Global)**:
- Required: use only the RNG streams your system owns (Mail: `mail`, `mail.special` by shift); Mail draws spawn and address from `mail` in a fixed order (ADR-0005 §2)
- Required: hash, RNG and serial arithmetic inside `unchecked(...)` (Global)
- Forbidden: the retired streams `mail.spawn`/`mail.address`; `System.Random`; rejection sampling; a draw count that depends on outcomes
- Guardrail: a shift-5 roll makes exactly K draws (M-PERF-1e)

---

## Acceptance Criteria

*From GDD `design/gdd/mail-destinations.md`, scoped to this story:*

- [x] **AC-1 (M-F2-1)** Shift 5, D = 300, raid 90, jitter forced to 0, N = 708 → nb = 20, W = 17,000, Σ c_b = 708; batches 0–13 carry 41 or 42 (583 total), batches 14–19 carry 20 or 21 (125). **b** Batch 14 jittered to start + 6,299 has weight 1000 (W = 17,500); at start + 6,300 it has 500.
- [x] **AC-2 (M-F2-2)** nb = 16 / 18 / 20 for D = 240 / 270 / 300 and 14 for D = 210. **b** With jitter forced to +90, the last batch is ≥ 270 ticks before Delivery ends. **c** D = 250 → nb = 17, last batch clamped to start + 7,230. **d** Batch 0's jitter is ≥ 0, and batch times are strictly ordered. **e** Σ c_b = N with the raid window on or off and with `openingRampBatches = 2` (and batches 0–1 then carry half the weight of batch 2 ± 1 unit, M-R10-1f).
- [x] **AC-3 (M-F4-1)** With a stub `next32`: `NextInt(1000)` of 0 → 0, 0x80000000 → 500, 0xFFFFFFFF → 999. **b** m = 5 (750): a draw of 749 → 1 pair, 750 → 0. **c** m = 7 (1,050): one pair always, a second if the draw is < 50. **d** m = 0 → 0 pairs, one draw still consumed. **e** A pair's second unit is on its first's street; other units use unused streets, falling back to all addresses once every street is used. **f** m = 1 → 0 pairs whatever the draw, one draw consumed. **g [FUZZ]** Over 10,000 m = 5 batches on ≥ 5 streets, the paired share is 30 % ± 1.5 pp.
- [x] **AC-4 (M-DET-2)** The total draw count is exactly K = 2·nb + 2N + max(0, N − 1) (2,163 for N = 708, nb = 20; 2·nb for N = 0), identical for `cursedShare` 0 and 100; an empty batch still consumes its jitter and pair draws. The roller never touches `mail.special` (a spy stream records 0 draws).

---

## Implementation Notes

*Derived from ADR-0005 §2, ADR-0017 §10 and the GDD F2/F4/Determinism blocks (read them; do not paraphrase):*

- **Files:** `src/PerformativeMail.Sim/Mail/Manifest/BatchSchedule.cs`, `Manifest/DestinationDraw.cs`. Both take an `IRngStream`-like value (`ref` struct view) so tests can inject a stub `next32`. The production stream is `RngDerive(runSeed, "mail", shift)` from `RngNames` (`production/epics/simulation-core/story-009-rng-registry-owner-views.md`; primitives in `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`).
- **Draw order is the GDD's** (§Determinism under Formulas), unconditionally: (1) one jitter per batch (F2); (2) a Fisher–Yates shuffle of the kind-ordered unit list (N − 1 draws, 0 when N = 0; F3 itself has no draws); (3) split by `cumW` (no draws); (4) per batch, one pair-rounding draw, then per unit in manifest order its address draw and its Cursed roll (F4; the roll is drawn even at `cursedShare = 0`, M-R14-1b). This gives K = 2·nb + 2N + max(0, N − 1). **This story owns the shuffle** (`Manifest/UnitShuffle.cs`) because it sits inside the draw sequence. Assert per-category draw counts in a test, so a reorder fails loudly.
- **Address lists:** draws are over the ascending `AddressId` list of unlocked addresses (ADR-0017 §10; world-stub `Lots` order). Streets come from `AddressId.Street`. "Unused streets" is tracked per batch in a fixed-size `stackalloc` bitset (≤ 255 streets per district).
- **Jitter:** `batchJitterSeconds` 3 → ±90 ticks, batch 0 in [0, +90]. Clamp `t_b ≤ deliveryStartTick + 30 × (D − tailGuardSeconds)`. Knobs `batchIntervalSeconds`, `raidSpawnRateMultPm`, `openingRampBatches`, `streetStreakRatio` and `tailGuardSeconds` come from `MailMixDef`/balance keys.
- `c_b = floor(N × cumW_b / W) − floor(N × cumW_(b−1) / W)`, in `long`.
- At M-1a the greybox uses shift 1 and District 1 only (ADR-0017 Ordering Note; the stub supplies the profile). The functions are general, and the tests use the GDD fixtures above.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 011: F1/F3. Story 013: assembling `ShiftManifest`, `PrepareShift`, re-roll and lock, forecast. Story 014: firing batches.
- `SpawnSpecial` on `mail.special` (R15, later).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/mail/BatchScheduleTests.cs`, `tests/unit/mail/DestinationDrawTests.cs`, `tests/unit/mail/ManifestDrawCountTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/mail/BatchScheduleTests.cs, DestinationDrawTests.cs, ManifestDrawCountTests.cs, ManifestDrawAllocationTests.cs (NonParallel), DrawTestRig.cs

---

## Dependencies

- Depends on: `production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`, `production/epics/simulation-core/story-009-rng-registry-owner-views.md`; `production/epics/world-stub/story-004-addressid-lots-destination-ids.md`; `production/epics/content-data/story-005-m1a-def-records.md` (`MailMixDef`)
- Unlocks: Story 013

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 4/4 passing (all automated; none deferred)
**Deviations** (advisory, TD-089): functions take the existing `RngStream` class (stub via internal `RngStream(IRngSource)`); used-street bitset keyed on (district, street) (2 KB stackalloc); one address draw per unit (K formula; GDD says "draw(s)"); ramp ∩ raid halves the raid weight (GDD silent); `ZeroWeight` detected after jitter draws; knobs read from `MailMixDef` ms fields via `BatchKnobs.From` (no balance keys).
**Implementation**: `src/PerformativeMail.Sim/Mail/Manifest/{BatchKnobs,BatchSchedule,DestinationDraw,UnitShuffle,ManifestDraws}.cs`. Production stream `registry.ViewFor(SystemId.MailDestinations).Create(in RngNames.Mail, shift)`; `ManifestDraws.Run` = jitters → Fisher–Yates → layout → per batch pair draw + per unit address + Cursed roll; `ExpectedDraws` = K (2,163 at N=708, nb=20). Story 013 passes the kind-ordered unit list and the ascending unlocked address list.
**Test Evidence**: Logic — 4 required test files present (31 new tests; MailDestinations namespace 124). Full suite 4,205/leg, 0 failed on net10 Release and net8 Debug/Release; net10 Debug showed 5 transient PlayerCharacter failures from a concurrent runner's in-flight Players edits (not Mail; did not recur on later legs).
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (+ C#-specialist pass); QL-TEST-COVERAGE GAPS (none critical) → TD-089. Closed without a revision round at owner wrap-up order.
