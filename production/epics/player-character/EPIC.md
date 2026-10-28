# Epic: Player Character & Movement (M-1a slice)

> **Layer**: Feature
> **GDD**: design/gdd/player-character-movement.md (In Review — revision applied, awaiting re-review)
> **Architecture Module**: Sim.Players (`src/PerformativeMail.Sim/Players/`; owner-role `CarrierStep` and focus in Sim.ClientLayer) + PlayerView, camera boom and prompt anchor (Godot)
> **Milestone slice**: M-1a → M1 (GDD Quick reference). The M-1a set is chosen from the rule list (see Overview).
> **Engine Risk**: LOW (Sim solver, plain C#). MEDIUM for the presentation (AnimationTree, `AnimationPlayer` StringName members, `Animation.length` double; remote interpolation must be our own).
> **Est. size**: L — 28 stories (15 Complete, 11 Ready, 2 Deferred; refreshed 2026-10-27, story 010 Complete, PR #50)
> **Implementation priority**: 5 of 9
> **Status**: Ready
> **Stories**: 28 stories — 15 Complete, 11 Ready, 2 Deferred (counts refreshed 2026-10-27 from the story headers, story 010 Complete, PR #50; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

Player Character & Movement is the carrier every player drives. It also hosts the **single interaction framework** behind every "press E" in the game. The body shows the real weight, the prompt shows the truth before you press, and the hand path stays fast and honest. For the M-1a solo greybox the carrier is a deterministic, Godot-free kinematic capsule. The owning client steps it (client-authoritative movement, P2), against `world-stub`'s height field and AABBs, in integer mm and mm/s with a fixed-point trig LUT.

**In scope at M-1a:**
- **Body and locomotion:**
  - the body (P1): capsule 300 × 1400 mm, `EntityId` class `player`, `PlayerSlot` via the `PlayerJoin` op;
  - owner-role stepping in ClientLayer `CarrierStep`; host role only for the solo loopback mirror (P2);
  - locomotion (P3): walk and sprint with no stamina, and Grid Inventory D1's weight → speed;
  - ground, slopes, steps and colliders (P4): sub-steps ≤300 mm, ≤3 slides, step-up and snap 400 mm, slope 700 ‰;
  - jump (P5): 1.2 m, 24 000 mm/s², buffered press;
  - water (P6, F7): wading, swimming and the 900/700 mm hysteresis, which matters because Throwing refuses throws while swimming;
  - formulas F1–F3 (final speed, acceleration and turning, jump).
- **Camera** (P9, Presentation): a third-person boom with the pivot 1.6 m above the feet, solved in C# against the Sim height field. Dither fade is M1.
- **Interaction framework** (P10–P13, F4):
  - the `IInteractable` registry with a 2 m spatial hash and one verb per anchor;
  - integer focus scoring with a 4-tick dwell;
  - the press/hold/channel counter in owner Sim ticks, with one request at the held tick and no host hold timer;
  - hold cancel range with hysteresis (`incumbentSlackMm`);
  - host-side validation in `InputsApply`;
  - the `IInteractionQuery` consumed by the input router.
- **Router declarations** (P14): the contexts and verbs this system declares for Input & Controls. Mounted and Dead are declared but unused at M-1a.
- **PlayerView:** a greybox rig with one AnimationTree, carry pose and idle doze (P20, presentation only).

**Out of scope:**
- supports on belts and vehicle roofs (P7) → M-1b/M1;
- PvP push (P8, F6), transform replication, Teleport, the host position-history ring and range check F5 → M0, ADR-0020 (story 019 implements only the **M-1a minimal** F5 predicate, see Notes);
- knockdown, death bag, clock-in and standard issue (P15–P18, F8) → M-1b with Health & Damage;
- on break / disconnect (P19) → M0;
- animation LOD → M1.

**Why these ADRs govern it.**
- ADR-0006's integer rules make the per-tick transform hash equal on Windows and Linux (TR-movement-022 via ADR-0001's net8.0 leg), the basis for later client-authoritative trust.
- ADR-0002 forbids engine physics interpolation and gives the fixed step with a catch-up cap.
- ADR-0010 provides the movement balance keys and the 12 validator rules.
- ADR-0003 places `PlayersStep` (phase 3) and knockdown (phase 11).
- ADR-0004 gives player ids and slots.
- ADR-0013 and ADR-0014 (**Accepted 2026-10-16**) fix the carrier solver and the interaction framework; ADR-0016 places focus/press/hold in the `Previews` sub-steps; ADR-0026 supplies `IWorldQuery`; ADR-0019 supplies the scene layout for PlayerView and the camera.

**Risks.**
- (1) ~~The solver (ADR-0013) and the interaction framework (ADR-0014) are not written.~~ **Resolved:** both Accepted 2026-10-16. ADR-0029 (animation and camera) and ADR-0020 (transforms/Teleport, F5 ring) are still unwritten; the camera and rig stories (021–025) are M-1a greybox and feed ADR-0029, and the AnimationTree story (024) is the cost spike the GDD requires.
- (2) The capsule size versus doorway and mailbox geometry is unverified (GDD OQ23). It is measured on the `world-stub` street in story 020.
- (3) The GDD awaits re-review after 15 blockers were revised; several GDD wordings are superseded by Accepted ADRs (see Notes — GDD/ADR conflicts).
- (4) Owner-role stepping in ClientLayer versus host mirroring on loopback must not create two sources of truth. ADR-0016 (Accepted) decides the ClientLayer phases; the host learns the owner's position only through the transform-sample sink (story 020) and the phase-3 ingest (story 019).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0013: Carrier kinematic solver & fixed-point geometry (**Accepted 2026-10-16**) | pure `OwnedCarrier.Step`; tick-rational remainders; BAM/Q15 + `AtanQ15`; sub-step sweep; `Sim/Geometry`; `IRemoteCapsuleSource`/`ISupportTransformSource` | LOW |
| ADR-0014: Interaction framework & hold timing (**Accepted 2026-10-16**) | registry + 2 m dense grid, focus + dwell, press resolution, `Hold.HoldTicks`, strict cancel range, `CheckInteraction`, life states | LOW |
| ADR-0016: ClientLayer previews and presentation timing (Accepted 2026-10-16) | `Previews` sub-order (Focus → … → EdgeResolve → HoldAdvance), ports, `IRenderClock`, own-carrier timeline | LOW |
| ADR-0026: World query contract and M-1a street stub (Accepted 2026-10-16) | `IWorldQuery`, height field RH, `QueryStaticSolids`, Post Office pad | LOW |
| ADR-0019: Godot scene architecture (Accepted 2026-10-16) | `World/Entities` PlayerView, `CameraRig → Boom → Camera3D`, `RigOffset`, `Present(alpha)` | MEDIUM |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | mm int32, int64 before the first multiply, one final division; T2 transform hash | LOW |
| ADR-0002: Tick Driver, FrameRunner and Main-Thread Sim | fixed step with catch-up cap; local extrapolation ≤1 tick; no engine physics interpolation | LOW (driver) |
| ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework | Sim references no Godot; net8.0 leg for the T2 golden transform hash | LOW (Sim half) |
| ADR-0010: Content Format, Loading, ContentSet and StatSheet | movement balance keys, StatSheet stats, `KitDef.standardIssueItem`, 12 cross-key validator rules | LOW |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | `PlayersStep` phase 3 (host role for Dead/Frozen), `CombatReact` phase 11; sync `IInventoryApi`/`IWorldQuery` | LOW |
| ADR-0004: Identity and Id Spaces | `EntityId` class `player`, `PlayerSlot` u8, slot reuse from T+1 | LOW |
| ADR-0011: Network Adapter Boundary | `IHostClock`; interaction requests as reliable ops; sample ring | LOW (seam) |
| *ADR-0020 (M0)*, *ADR-0029 (M1)* | transforms/Teleport/F5 ring; animation LOD and camera boom — **not written** | — later |

## GDD Requirements

Source: `docs/architecture/tr-registry.yaml` (`id: TR-movement-*`). 16 of 26 covered by an Accepted ADR, 10 coverage gaps (ADR-0020 ×7, ADR-0029 ×2, `perf-budget.md` ×1).

| TR-ID | Requirement | ADR Coverage | Slice | Stories |
|-------|-------------|--------------|-------|---------|
| TR-movement-001 | Deterministic kinematic capsule solver in Sim; no Godot physics; no floating point | ADR-0013 ✅ | M-1a | 001, 002, 004, 005, 006, 007, 008, 009 |
| TR-movement-002 | mm int32, mm/s, permille, ticks; int64 before first multiply; one final division | ADR-0006 ✅ | M-1a | 003, 005, 006, 009, 011 |
| TR-movement-003 | Client-authoritative movement; host authors Dead/Frozen bodies only | ❌ ADR-0020 (planned) | M-1a solo owner-step; host Dead/Frozen authoring M-1b/M0 | 019, 020 (owner-step + host never steps Alive) |
| TR-movement-004 | Tick-time inputs only via IRemoteCapsuleSource/ISupportTransformSource | ADR-0013 ✅ | M-1a | 002, 008, 010, 020 |
| TR-movement-005 | Fixed-point trig LUT; terrain from World Gen height field (2 m, bilinear, int64) | ADR-0013 ✅ (+ADR-0026) | M-1a | 003, 004, 005, 006, 007, 009 |
| TR-movement-006 | Sub-steps ≤300 mm, ≤3 slides, step-up/snap 400 mm, slope 700‰, ≤4 sub-steps/tick | ADR-0013 ✅ | M-1a | 007, 008 |
| TR-movement-007 | Fixed-step driver with catch-up cap; local extrapolation ≤1 tick; no engine physics interpolation | ADR-0002 ✅ | M-1a | 020, 022, 023 |
| TR-movement-008 | Transforms 16 B up, 17 B/entry batched down; 15 Hz/4 Hz; ≤19 kbps down, ≤6.7 kbps up | ❌ ADR-0020 (planned) | M0 | — deferred |
| TR-movement-009 | Teleport reliable with wrap-safe teleportSeq, ack/resend | ❌ ADR-0020 (planned) | M0 | — deferred |
| TR-movement-010 | Host range check via 9+3 tick history ring, claim at press, position_stale at 30 ticks | ❌ ADR-0020 (planned) | M0 (**minimal subset pulled forward**, see Notes) | 019 (subset) |
| TR-movement-011 | IInteractable registry with 2 m spatial hash; one verb per anchor; integer focus, 4-tick dwell | ADR-0014 ✅ | M-1a | 004, 012, 013, 014, 015, 016, 018, 019, 023 (cone), 026 |
| TR-movement-012 | Hold counter in owner Sim ticks; one request at held tick; no host hold timer | ADR-0014 ✅ | M-1a | 011, 016, 017, 018, 019 |
| TR-movement-013 | Life states host-owned with absolute-tick timers | ADR-0014 ✅ | M-1a data model; timers/transitions M-1b | 001 |
| TR-movement-014 | Knockdown chain in phase 11 in fixed order, emits CarrierKnockedDown | ADR-0014 ✅ | M-1b (with Health & Damage) | — deferred |
| TR-movement-015 | Frozen on disconnect 120 s; reconnect via account→peer map | ❌ ADR-0020 (planned) | M0 | — deferred |
| TR-movement-016 | PvP push: own capsule only, half overlap, cap 60 mm/tick | ADR-0013 ✅ | M0 | — deferred |
| TR-movement-017 | Support carry ≤12,000 mm/s; roof position in vehicle-local frame | ADR-0013 ✅ | M-1b/M1 (belts, vehicles) | — deferred |
| TR-movement-018 | Water depth from height field; swim hysteresis 900/700 mm | ADR-0013 ✅ (+ADR-0026) | M-1a | 009 |
| TR-movement-019 | ContentValidator enforces 12 cross-key movement rules | ADR-0010 ✅ | M-1a subset | content-data story 011 (+ CV-PLR-13 in story 012) |
| TR-movement-020 | Balance keys and StatSheet stats for movement; KitDef.standardIssueItem | ADR-0010 ✅ | M-1a (no KitDef) | 001, 005, 006, 020 |
| TR-movement-021 | Owner CarrierStep+focus p99 ≤0.10 ms; host ≤0.20 ms/tick; 0 B/tick | ❌ `perf-budget.md` (not written) | advisory | 010, 019, 020 (advisory logging) |
| TR-movement-022 | Golden per-tick transform hash equal on Windows and Linux; Sim references no Godot | ADR-0001 ✅ | M-1a | 010 |
| TR-movement-023 | Remote interpolation 160 ms delay, 100 ms extrapolation cap, snap on teleport/6 m | ❌ ADR-0020 (planned) | M0 | — deferred |
| TR-movement-024 | Camera boom solved in C# against Sim height field/AABBs; dither fade | ❌ ADR-0029 (planned) | M-1a minimal boom; dither fade M1 | 021, 023 |
| TR-movement-025 | One AnimationTree per character, ≤4 layers, 48 characters ≤1.0 ms | ❌ ADR-0029 (planned) | M-1a greybox rig; LOD budget M1 | 022, 024, 025 |
| TR-movement-026 | animState uint8 bit layout; JoinState public block ≈30 B/player | ❌ ADR-0020 (planned) | M0 | — deferred |

**Requirements with no TR-ID in the registry** (cited by GDD rule / acceptance id in the stories): P14 router declarations (PCM-P14-1, story 013), P6 swimming verb restrictions (PCM-P6-2, story 013), P20 presentation-only behaviours (PCM-P20-1/2, story 025), the focus-anchor screen-projection feed for the HUD (story 026), P1 (PCM-P1-1, story 001).

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Players system skeleton — `PlayerSettings`, `CarrierState`, life-state data, `PlayerJoin` binding](story-001-players-module-carrier-state-join-binding.md) | Logic | Complete | ADR-0013 |
| 002 | [Tick-time input ports, `CarrierEnv`, `CarrierScratch` and solver counters](story-002-tick-time-ports-carrier-env-scratch.md) | Logic | Complete | ADR-0013 |
| 003 | [`TrigQ15.Atan2Bam` and the committed `AtanQ15` table](story-003-atan2bam-atanq15-table.md) | Logic | Complete | ADR-0013 |
| 004 | [`Sim/Geometry`: `SegmentIntervals`, `HeightfieldMarch`, `Obb.ToLocal`](story-004-geometry-segment-intervals-heightfield-march-obb.md) | Logic | Complete | ADR-0013 |
| 005 | [Locomotion — F1 speed, F2 acceleration, camera-relative heading, body yaw, sprint rules](story-005-locomotion-f1-f2-camera-relative-yaw.md) | Logic | Complete | ADR-0013 |
| 006 | [Ground contact, gravity and jump — F3, coyote, buffer, extra jump](story-006-ground-contact-gravity-jump-f3.md) | Logic | Complete | ADR-0013 |
| 007 | [Sub-step sweep over the height field — slopes, snap-down, map-edge clamp](story-007-substep-sweep-terrain-slopes-snap-map-edge.md) | Logic | Complete | ADR-0013 |
| 008 | [Static colliders — cylinder vs AABB, slides, step-up, ledges, ceilings, depenetration](story-008-box-colliders-stepup-slide-depenetration.md) | Logic | Complete | ADR-0013 |
| 009 | [Water — F7 depth, wading, swim hysteresis, swim motion, haul-out](story-009-water-wade-swim-haulout-f7.md) | Logic | Complete | ADR-0013 |
| 010 | [PCM-P2-1 — 600-tick golden transform hash and solver determinism gates](story-010-golden-transform-hash-p2-1.md) | Integration | Complete | ADR-0001 |
| 011 | [`Hold.HoldTicks` — the single shared `holdEff`](story-011-hold-ticks-holdeff.md) | Logic | Complete | ADR-0014 |
| 012 | [Anchor registry — `IInteractable`, `RegisterAnchor`, verb catalogue, dense 2 m grid](story-012-anchor-registry-iinteractable-spatial-grid.md) | Logic | Complete | ADR-0014 |
| 013 | [Input contexts, verb × context table (P14 router declarations), framework availability gate](story-013-contexts-verbs-availability-gate-router-declarations.md) | Logic | Complete | ADR-0014 |
| 014 | [Focus — F4 score, tie-break, 4-tick dwell, `PreviewSubStep.Focus` binding](story-014-focus-scoring-dwell-preview-binding.md) | Logic | Complete | ADR-0014, ADR-0016 |
| 015 | [Focus occlusion — lazy, score-ordered](story-015-focus-occlusion-lazy-score-order.md) | Logic | Ready | ADR-0014 |
| 016 | [Press resolution — Catch → presented target verb → catch-armed, latch, Tap requests](story-016-press-resolution-tap-catch-armed.md) | Logic | Complete | ADR-0014 |
| 017 | [Hold counter — completion, cancel list, catch channel, focus latch](story-017-hold-counter-cancels-channel-focus-latch.md) | Logic | Ready | ADR-0014 |
| 018 | [`InteractionSnapshot` publication and `IInteractionQuery`](story-018-interaction-snapshot-iinteractionquery.md) | Logic | Ready | ADR-0014 |
| 019 | [Host `PlayersStep` ingest, `IPlayersApi.CheckInteraction` and the leash query](story-019-host-checkinteraction-position-ingest.md) | Logic | Ready | ADR-0014 |
| 020 | [Owner-role `CarrierStep` in the ClientLayer — frame → input, sample emission, published state](story-020-carrierstep-clientlayer-integration.md) | Integration | Ready | ADR-0016 |
| 021 | [Camera profiles and boom solver against the Sim geometry (`Client.Core`)](story-021-camera-boom-solver-config.md) | Logic | Ready | ADR-0019 |
| 022 | [`PlayerView` greybox scene and the local carrier's render pose](story-022-playerview-scene-render-pose.md) | Visual/Feel | Ready | ADR-0019 |
| 023 | [`CameraRig` node — follow spring, look, aim/swim profiles, focus-cone cosine](story-023-camera-rig-node-focus-cone.md) | Visual/Feel | Ready | ADR-0019 |
| 024 | [Greybox rig, `AnimationTree` state machine, gait playback, squash — and the AnimationTree spike](story-024-animationtree-greybox-rig-gait-spike.md) | Visual/Feel | Ready | ADR-0019 |
| 025 | [Carry pose by held item class, whole-body load blend, idle doze](story-025-carry-pose-weight-blend-idle-doze.md) | Visual/Feel | Ready | ADR-0019 |
| 026 | [Interaction prompt anchor — projected anchor and bounds, HUD focus feed](story-026-interaction-prompt-anchor-projection.md) | Integration | Ready | ADR-0019 |
| 027 | [Movement rulings — extra-jump press resolution, `maxExtraJumps` 0–1, steep-slope slide, story 009 carry-forwards (TD-091)](story-027-movement-rulings-extra-jump-slide-td-091.md) | Logic | Deferred (Evan 2026-10-23) | ADR-0013 |
| 028 | [PCM-P2-1 cross-OS golden equality (Windows + Linux) and the blocking PCM-PERF-1 counters /](story-028-deferred-golden-cross-os-equality-and-blocking-counters.md) | Integration | Deferred (Evan 2026-10-23) | — |

**28 stories: 20 Logic, 4 Integration, 4 Visual/Feel — 15 Complete, 11 Ready, 2 Deferred (refreshed 2026-10-27 from the story headers, story 010 Complete, PR #50).**

**Suggested order:** 001–010 (solver, in this order; 003 and 004 can run in parallel with 002/005) → 011–019 (interaction framework; 011, then 012/013, then 014–018, 019 any time after 012/013) → 020 → 021 → 022–026 (Godot-side, after the godot-host-shell epic's scene/view-registry stories).

**Interaction-framework stories (cite these from other epics):**
- `story-011-hold-ticks-holdeff.md` — `Hold.HoldTicks` (the only `holdEff`)
- `story-012-anchor-registry-iinteractable-spatial-grid.md` — `IPlayersApi.RegisterAnchor`, `IInteractable`, `Verb` catalogue (manual-delivery MD1, grid-inventory Open/Pickup anchors)
- `story-013-contexts-verbs-availability-gate-router-declarations.md` — **P14 router declarations** (`VerbContextTable`, `BodyContext`, `CarrierStatus`, framework reasons)
- `story-014-focus-scoring-dwell-preview-binding.md`, `story-015-focus-occlusion-lazy-score-order.md`, `story-016-press-resolution-tap-catch-armed.md`, `story-017-hold-counter-cancels-channel-focus-latch.md`
- `story-018-interaction-snapshot-iinteractionquery.md` — **`IInteractionQuery`** (input-controls router, HUD)
- `story-019-host-checkinteraction-position-ingest.md` — **`IPlayersApi.CheckInteraction`** and the leash query (grid-inventory story 019, manual-delivery story 005)

## Notes (decisions, deferrals, gaps)

**Sizing.** `story_granularity: fine` (one AC per story) was overridden by the orchestrator rule: each story is one `/dev-story` session (≤ ~1 day, ≤ ~600 lines of production code and tests), 1–9 closely related ACs. Same decision as content-data and simulation-core.

**Deferred out-of-milestone rules (no stories; listed so nothing is silently dropped):**
- P7 supports (belts, vehicle roofs) and TR-movement-017 → M-1b/M1; the `ISupportTransformSource` port exists (story 002) with an empty M-1a implementation, and `CarrierState` declares the support fields.
- P8 player push / ghosting (F6) and TR-movement-016 → M0; `CarrierState.Pair[7]` is declared, unused. The golden hash (story 010) must be re-recorded when push lands.
- P15–P19 and F8 (knockdown, clock-in, standard issue, on break, rejoin), TR-movement-014/015 → M-1b / M0. Story 001 holds the life-state data model only.
- Transform replication, Teleport, host position-history ring and dead-reckoning (F5 beyond the minimal subset), remote interpolation, `animState` bit layout (TR-movement-008, 009, 010-full, 023, 026) → M0 / ADR-0020.
- Animation LOD, dither fade, comfort options, Mounted camera, knocked-down orbit camera → M1 / M-1b / Accessibility.
- Mounted context (P14) and the own-seat Dismount gate → M1 (Vehicles): `CarrierStatus.OccupiedSeat` is `AnchorRef.None` at M-1a (ADR-0014 erratum 2026-10-20, owner decision); Vehicles fills it, registers one `SeatOf` Dismount anchor per seat under that ref and chooses the Mount/Dismount slot scheme or a Mounted registration context (the registry has only World/Build masks). No Vehicles epic or GDD exists yet.
- CV-PLR-07 (`pushCapMm`) → M-1b (content-data story 011 defers it to this epic's M-1b work).

**M-1a minimal F5 (scope decision).** Story 019 implements the ADR-0014 §5 predicate and chain order, staleness (30 ticks) and the leash with corroboration against the *newest* ingested sample behind an `IPositionHistory` seam. The 9+3 ring and dead-reckoning are the M0 (ADR-0020) implementation of that seam.

**GDD/ADR conflicts found (ADR wins; GDD sync at re-review):**
1. PCM-P12-1 hold completion tick: GDD "press tick = held tick 1" completes at `PressTick + HoldTicks − 1`; ADR-0014 §4 completes at `PressTick + HoldTicks` (tick 12 for 12 ticks; release on tick 12 cancels). Story 017 follows the ADR.
2. GDD F5 `r = 3000` for Deliver (per-verb host range) vs ADR-0014 §5 single `RangeMm` + `rangeSlackMm` (Deliver 2500 + 500 = 3000). Story 019 follows the ADR; the "Deliver accepts at 3500" row of PCM-F5-1 is superseded.
3. GDD F7 terrain division (truncating) vs ADR-0026 §2 / ADR-0013 §6 (`MulDivRH`). Stories follow the ADRs; FX-Beach goldens are identical.
4. GDD P2 "extrapolate ≤ 1 tick, clamped to the swept result" vs ADR-0016 §6 "≤ 1-tick interpolation by `AlphaPermille`". Story 022 reconciles both with a one-tick same-input probe (blend toward the predicted next tick).
5. content-data story 008 says the Sim-side `holdEff` "belongs to the input-controls epic"; ADR-0014 puts `Hold.HoldTicks` in `Sim/Players/Hold.cs`. Story 011 owns it; **input-controls must cite story 011, not create a second one.**
6. GDD F8 pad slot 0 = (1200, 0) vs world-stub story 005 slot 0 = (0, −1200) (ADR-0013 §3 convention). Harmless for M-1a spawn (story 001 uses `PadSlots.Position`); reconcile in the M-1b clock-in work.
7. GDD swimming feet height unspecified: story 009 holds the feet at the water plane (y = 0), consistent with the swim camera pivot 1100 mm.

**Cross-epic gaps surfaced (owners must pick these up; no other epic's story files were edited):**
- `IRequestSink`, `ClientPorts`, `ClientLayer.Attach`, `ClientLayerBuilder.BindPreview/BindMirrorPart` (ADR-0016 additive API) are owned by no existing story (sim-core 023 predates ADR-0016). Stories 014 and 016 add `PreviewSubStep`/`BindPreview` and `IRequestSink` if absent; the loopback `ClientAdapter` (story 006) should implement `IRequestSink`.
- Client-side weight (`ICarrierLoadSource`: `SpeedPermille`, `WeightPoints`) and the selected hotbar item's class (`IHeldItemView`) have no grid-inventory story; stories 020 and 025 define the ports and flag the adapters.
- Wiring `ITransformSampleSink` to `SimWorld.TryWriteSample` for solo M-1a (host shell / loopback session) is unowned; without it story 019's `CheckInteraction` reports `position_stale` after 30 ticks. [Owned since 2026-10-17: `production/epics/godot-host-shell/story-005-host-session-composition-ports-render-clock.md`.]
- `TransformSample` (sim-core 021) and `InputFrame` (sim-core 023) are PROVISIONAL; they must carry a swimming/flags bit, `LookYaw`, `FocusCosMinPm` and a Panel-state indicator (`InputFrameFlags.PanelOpen`, owner ruling 2026-10-21, ADR-0012/ADR-0014 errata; `RouterStateId` stays informational) — input-controls/ADR-0012 widths.
- ADR-0014 gap: `IInteractable` has no start/stop request builder for anchor-owned Channel verbs (Belt `Push`, M-1b); only the Catch channel works at M-1a (story 017). Needs an ADR-0014 amendment before Push.
- `grid-inventory/story-019-range-gated-open-pickup-leash.md` still says the interaction-framework story is "not yet created"; it is `story-019-host-checkinteraction-position-ingest.md` here (plus 012 for anchors).
- CI has no Windows runner: TR-movement-022 "equal on Windows and Linux" is verified indirectly via the net8.0/net10.0 legs until a Windows job exists (story 010).
- No HUD & Notifications epic and no interaction-prompt UX spec yet: story 026 supplies the data feed only; the prompt/ring/bracket widget needs `/ux-design` first.
- ADR-0029 (animation & camera) is unwritten; stories 021–025 are the greybox that feeds it, and story 024's spike report (`prototypes/spike-animationtree/REPORT.md`) is the evidence ADR-0029 needs.
- Godot-side stories (022–026) depend on godot-host-shell stories (`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`, `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md`, `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md`, `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md`) (scene root, `World/Entities`, `EntityViewRegistry`, `PresentationRoot`/`Hud` presenter slots, `Initialize(deps)`); they are Ready because the dependency is a story, not a missing decision.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- The `design/gdd/player-character-movement.md` acceptance criteria for P1–P6, P9–P14 and F1–F4/F7 are verified with fixtures FX-Dry, FX-Beach, FX-Wt, FX-Deep and FX-Ramp. The criteria for P7, P8 and P15–P19 stay open on their milestones
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/player/**`), including the golden per-tick transform hash on the net8.0 leg
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (locomotion, carry pose, interaction prompt anchor), with a lead sign-off for Visual/Feel

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): Track A (playable greybox): 010 (A1; Complete 2026-10-27, PR #50), 015 (A1), 021 (A1), 017 (A2), 019 (A2), 018 (A3), 020 (A6), 022 (A11), 023 (A12), 024 (A12), 025 (A13), 026 (A13); Deferred by Evan 2026-10-23 (not scheduled): 027, 028. Earlier notes below are history.

**2026-10-23:** 001–009, 011–014 and 016 are Complete (014: PR #40, merged; 016: PR #42, merged as main c201e87). Next: story 015 (occlusion) and 017 → 018; 020 after 019 (→ 010 → 027) and input-controls 008; 021; 010 waits on 027 (TD-091). Originally: run `/story-readiness production/epics/player-character/story-001-players-module-carrier-state-join-binding.md`, then `/dev-story`. Work the Sim stories in order; start the Godot-side stories (022–026) after the `godot-host-shell` epic's scene/view-registry stories exist (`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`, `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md`).
