# Story 008: ContentSet and BuiltInContent.CreateM1a — the M-1a greybox set

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; mostly data)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD9, CD21); values belong to the owner GDDs' Tuning Knobs and tables: `grid-inventory.md` (R1, D1, Tuning Knobs, AC H-R1-1 and its fixture table), `mail-destinations.md` (R1, R3, F3, Tuning Knobs), `player-character-movement.md` (Tuning Knobs), `throwing-catching.md` (Tuning Knobs), `simulation-core.md` (Tuning Knobs).
**Requirement**: `TR-content-020` (`ContentSet` half; loader half in story 019), `TR-content-013` (owner key rows)
*(No `docs/architecture/tr-registry.yaml` exists yet. Requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§4) (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4 composition root)
**ADR Decision Summary**:
- There is one immutable `ContentSet` (`SimSettings`, `BalanceTable`, a `DefTable<T>` per def type). It is held for the process lifetime and passed by reference in `SimBuildArgs` to `SimComposition.Build`.
- M-1a builds it with `BuiltInContent.CreateM1a()`. This code factory constructs the same types the JSONC loader will produce and runs stages 4–5 over them.
- Systems receive `ContentSet` (or a sub-table) by constructor and may cache sub-tables in `readonly` fields.

**ADR Version**: ADR-0010 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: None. The Godot `ContentHost` that will call this at boot belongs to `godot-host-shell`.

**Control Manifest Rules (Foundation)**:
- Required: M-1a uses `BuiltInContent.CreateM1a()` building the same `ContentSet` types (running stages 4–5). At M0 the JSONC loader replaces it, and a test asserts identical canonical encoding for the M-1a subset. (ADR-0010 §4)
- Required: one composition root `SimComposition.Build(in SimBuildArgs args)`, with `SimBuildArgs = { SimProfile, ContentSet, ulong runSeed, SimInitialCounters? }`. (ADR-0003 §4)
- Forbidden: swapping a new `ContentSet` into a running `SimWorld` in place. (ADR-0010 §4)
- Forbidden: statics holding the set. It is passed by reference, not reached through a static. (ADR-0003 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [x] `BuiltInContent.CreateM1a()` returns a `ContentSet` with:
  - Containers: `cursor`, `hotbar`, `player_inventory`, `po_intake`;
  - Items: `axe`, `fiber`, `log`, `plank`;
  - MailKinds: `large`, `letter`, `medium`, `postcard`, `small`;
  - DestinationTypes: `house`;
  - one MailMix.

  Each table is in ordinal `DefIndex` order, with the authored values listed in the Implementation Notes. All references (`acceptedBy`, `accepts`, shares) resolve to `DefIndex`es. (CD21, CD10)
- [x] The factory runs the stage 4–5 rule runner (story 003) over its set and returns zero Errors with every rule registered at that point. A test asserts this and re-runs automatically as stories 009–012 add rules.
- [x] `ContentSet` and everything reachable from it is immutable (no public setters, no mutable collections). The factory is deterministic: two calls produce field-for-field equal tables, compared by a test helper that walks the tables (not by record equality).
- [ ] DEFERRED (simulation-core 025 — carried there) — Two `SimComposition.Build` calls with the same `ContentSet` give two `SimWorld`s that hold the same instance (`ReferenceEquals`). The factory or loader is invoked once per process (counter `ContentBuildCount` stays 1). (CDD-LOAD-4, M-1a form)
- [x] A minimal-valid factory (`ContentFixtures.MinimalValid()`) plus an id-addressed overlay helper (replace one def or balance value by id/key) exists for the validator fixtures of stories 009–012.

---

## Implementation Notes

*Derived from ADR-0010 §4 and the owner GDDs' tables:*

- **Location:**
  - `src/PerformativeMail.Sim/Content/ContentSet.cs`
  - `src/PerformativeMail.Sim/Content/BuiltInContent.cs`
  - balance rows in `src/PerformativeMail.Sim/Content/Balance/BalanceKeys.<Owner>.cs`
  - test fixtures in `tests/unit/content/Fixtures/`
- **The M-1a instances.** Values are as stored. The owner GDD's table is authoritative; if this list disagrees with it, follow the GDD and note the difference.

  **Containers**:

  | Id | Layout | Grid | Other fields |
  |---|---|---|---|
  | `hotbar` | slots | 8×1 | `reservedSlots [0]`, `handCarried` true |
  | `player_inventory` | grid | 8×2 | `handCarried` true |
  | `po_intake` | grid | 20×16 | `view` manifest, `beltAccess` outfeed_only, `allowedCategories [mail]` |
  | `cursor` | slots | 1×1 | — |

  The remaining fields come from AC H-R1-1.

  **Items** (fixture table, grid-inventory.md):

  | Id | Category | Footprint | maxStack | Weight class |
  |---|---|---|---|---|
  | `axe` | tool | 1×2 | 1 | light |
  | `plank` | material | 1×1 | 20 | light |
  | `fiber` | material | 1×1 | 20 | light |
  | `log` | material | 1×2 | 10 | light |

  **Mail kinds** (Mail R1 table):

  | Id | Footprint | maxStack | Value (cents) | Weight class | Complaint (milli-points) | unlockShift |
  |---|---|---|---|---|---|---|
  | `postcard` | 1×1 | 40 | 4 | light | 3 000 | 2 |
  | `letter` | 1×1 | 20 | 8 | light | 5 000 | 1 |
  | `small` | 1×2 | 1 | 30 | light | 8 000 | 1 |
  | `medium` | 2×2 | 1 | 70 | medium | 12 000 | 1 |
  | `large` | 2×4 | 1 | 160 | heavy | 16 000 | 2 |

  All five kinds have `acceptedBy [house]` and deadline offset 0. `cargo` is **not** included: it is deferred, and the `deferred` tag and CV-GEN-19 are M0.

  **`house`**:
  - `accepts` all five kinds;
  - `manualInsertHoldMs` 400;
  - insert rate 2000 milli/s;
  - `maxAutomatedFeeders` 1;
  - catch volume 1000 × 800 × 1000 mm, centre 1000 mm.

  **MailMix:**
  - shift 1: letter 600 / small 300 / medium 100;
  - shift 2: postcard 100 / letter 400 / small 300 / medium 150 / large 50;
  - interval 15 000 ms, jitter 3 000 ms, tail guard 9 000 ms, ramp 0;
  - `streetStreakRatio` 300; the other ratios and `spawnOverhead` per spec 08 §2.3.
- **Balance rows and values registered here** (key names as the owner GDDs spell them; dotted sub-keys verbatim, per story 004):
  - **`player.*`:**
    - movement, jump, body/ground, water, interact and host-range keys from PC Tuning Knobs (`ticksAccel` 4, `ticksDecel` 3, `airControlPm` 500, `turnDegPerSec` 720, `g` 24 000, `jumpHeightMm` 1 200, … `rangeMm` 2 500, `minHoldTicks` 6, `rangeSlackMm` 500, `positionStaleTicks` 30);
    - the walk/sprint/swim speeds 5 000/7 500/2 000 mm/s;
    - `buffCapPm` 400, `maxExtraJumps` 1, `respawnMs` 10 000, `respawnMinMs` 1 000.
  - **`inventory.*`:** `weightPoints.light/medium/heavy` 1/3/8, `ptsPermille` 10, `weightSpeedFloor` 600 pm, `maxRows` 16, `worldItemDespawnSeconds` 300 000 ms, `worldItemCap` 512, `capEvictMinAgeSeconds` 10 000 ms, `waterReturnSeconds` 10 000 ms, `externalLeashMeters` 3 000 mm, `backlogDrainPerTick` 32, `manifestCellThreshold` 160.
  - **`mail.*`:** the M-1a Tuning Knobs (`lateValueRatio` 500, `misdeliveryPenaltyRatio` 500, `deadLetterShifts` 2, `minPenaltyCents` 1, door/late complaint keys, `closeGraceTicks` 90, `distanceStepPm` 250, `shiftStepPm` 100), plus `payMultMaxPm` 5 000 and `penaltyMultMaxPm` 2 000.
  - **`throw.*`:** `throwSpeed.light/medium/heavy` 14 000/10 000/6 000, `g` 9 800, `loftDeg` 6 000 milli-deg, `maxFlightTicks` 90, `throwCooldownTicks` 5, `throwBurstTicks` 5, `handHeightMm` 1 200, `fwdOffMm` 300, `sideOffMm` 250, `damagePerPoint` 5, `dropPushbackMm` 500, `enemyHitSlackMm` 300, `previewSettleMs` 0, `rangeMinPermille` 1 000, `rangeMaxPermille` 1 500. Add the catch keys (`catchRadiusMm` 1 500, `catchPredictMs` 500, `catchTapMs` 500, `catchHistoryMaxOffsetMm` 1 500, `bonkRadiusMm` 300, `relayWindowTicks` 150) and the receiver rank table, because the throwing epic pulls the Sim-side catch forward.
  - **`delivery.*`:** `insertPmMax` 750.
- **Owner records.** Build typed owner records (`PlayerBalance`, `InventoryBalance`, `MailBalance`, `ThrowBalance`, `SimSettings`) once from the `BalanceTable`. A system receives only its own record (ADR-0003 §4, "the system's own settings record").
  - **`ThrowBalance` is the epic's "`ThrowDef`"** (decision logged; CD9 / OQ-CDD-5).
  - **`throw.g` and `player.g` stay distinct keys.**
- **Input hold keys** (decision logged): the epic listed `holdSetting`, `holdFloor` and `holdCeil`, but input-controls.md makes them something other than Sim content:
  - `holdSetting` is a player setting in `settings.input` (CD1 Player data);
  - `holdFloor` is derived from `player.minHoldTicks`;
  - `holdCeil` and `holdDefault` are Input constants (PROPOSED).

  No `input.*` balance rows are added. The Sim-side `holdEff` function belongs to the input-controls epic and waits for ADR-0014.
- **`KitDef` is not built** (story 005 decision b).
- **Values and units.** All values are stored as already-scaled integers (CD4). `…Seconds` keys hold ms, and conversion to ticks happens in the consuming system via Sim Core F1 `contentTicks`, unless the owner record exposes ticks.

---

## Out of Scope

- Stories 009–012: the validation rules themselves.
- Story 013: `WriteCanonical` and `ContentHash`.
- Story 019: the JSONC files under `content/` that will mirror this set.
- `godot-host-shell` epic: Godot boot wiring.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/content/BuiltInContentTests.cs` — must exist and pass

**Status**: [x] Created (partial: AC4 deferred) — `tests/integration/content/BuiltInContentTests.cs` + fixtures `tests/unit/content/Fixtures/{ContentFixtures,ContentWalk}.cs`

---

## Dependencies

- Depends on: Story 004, Story 005, Story 006. Also `production/epics/simulation-core/story-025-composition-root-reflection-test.md` (`SimComposition.Build`, `SimBuildArgs`). The `ReferenceEquals` criterion may land as soon as that story is Done; the rest of this story does not wait for it.
- Unlocks: Story 009, Story 010, Story 011, Story 012, Story 013, Story 014, Story 019; every M-1a gameplay epic's content reads

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 4/5 passing; AC4 (ReferenceEquals across two `SimComposition.Build` calls + `ContentBuildCount`) DEFERRED — simulation-core story 025 (SimComposition/SimBuildArgs) not done; carried into that story's "Carried-over obligations" section as permitted by this story's Dependencies line.
**Deviations** (all ADVISORY, logged as TD-055):
- GDD over story table: cursor `HandCarried` true; hotbar/player_inventory `View` Grid; mail kinds `BeltLanes` 1 / `BeltLengthMm` 500 (spec 04).
- Implementer-chosen key names now frozen for story 019: `player.walkMmPerSec`/`sprintMmPerSec`/`swimMmPerSec`, `mail.doorComplaintCap`/`doorComplaintWindowSeconds`/`lateComplaint`/`lateComplaintWindowSeconds`/`teamComplaintCap`, `throw.rank.{armedCatcher,mailbox,goon,teammate,belt,solid,ground,water}` (0..7). `…Seconds` keys hold ms; `throw.loftDeg` holds milli-degrees. `inventory.maxRows` 16 is a story value (not yet in Grid Inventory Tuning Knobs); clamp keys PROPOSED (OQ-CDD-18).
- `BalanceUnit` appended 8–11; interim `BalanceKeys.CreateSimRegistry()` (story 006) deleted — TD-047 item 2 and TD-037 item 3 resolved.
- Rules take the public `ContentValidationInput` adapter (internal `Candidate`); the set escapes only via `ValidationResult.AcceptedSet`.
**Test Evidence**: Integration — `tests/integration/content/BuiltInContentTests.cs` (~31 tests incl. a 112-row pinned default snapshot, stage 4–5 runner with per-owner rule-table completeness guard, immutability scan + self-test, ContentWalk determinism + self-test, MinimalValid/overlay helpers); fixtures `tests/unit/content/Fixtures/{ContentFixtures,ContentWalk}.cs`. Four legs (net10/net8 × Debug/Release) 2,101 tests/leg green in the real tree, 0 warnings, Debug 15 skipped.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; QL-TEST-COVERAGE GAPS → required gap (pinned defaults) and gaps 2–5 fixed in one revision round; godot-csharp-specialist APPROVE (enum-concat and IndexOf findings fixed).
**Run result**: N/A — Godot-free Sim content factory; nothing rendered.
