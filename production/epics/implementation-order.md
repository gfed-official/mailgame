# Implementation Order — all epics (M-1a)

> **Generated**: 2026-10-16 (story hygiene) from every story's `## Dependencies` → `- Depends on:` line in `production/epics/*/story-*.md`.
> **Scope**: 206 stories in 11 epics — 13 Complete, 1 In Progress, 179 scheduled in 31 waves, 12 Blocked, 1 Ready-but-gated.
> **Regenerate** whenever a story's Depends on, Status or file list changes; the order is advisory — `/story-readiness` still gates each story.

## How to read this

- **Wave N** = every story whose dependencies are all in earlier waves (or Complete / In Progress). Stories inside one wave have no dependency on each other and can run in parallel.
- **No shared files inside a wave.** Each story's declared files (`src/…`, `tests/…`, `project.godot`, CI files; the `Files in dir/: A.cs, B.cs` lists in Implementation Notes and the Test Evidence paths) were compared; a story that shares a declared file with an earlier story in the same wave is pushed to the next wave. Files a story only *reads* count too (conservative). Undeclared incidental edits are not detected — they are already ordered when the editing story Depends on the file's creator.
- **Wave 0** = Complete and In Progress stories. Wave 1 items that depend on an In Progress story (`input-controls/001`) start when it closes.
- Within a wave, rows are in epic order (index.md layering), then story number. That is also the recommended serial order for a single implementer.

## Dependency-cycle check

- **Depends-on graph: 0 cycles** (Tarjan SCC over 994 edges, 206 nodes). None found.
- Two would-be cycles were removed during this hygiene pass: `grid-inventory/021` listed "Depends on: mail-destinations backlog story (not yet created)" while `mail-destinations/014` depends on `grid-inventory/021` (ADR-0017 §6 puts backlog storage and drain in Inventory → converted to Unlocks); `input-controls/008`'s Depends on line named `grid-inventory/024` as "not a dependency" (moved to Unlocks).
- **Unlocks-vs-Depends contradictions (5, informational, not cycles):** each is a runtime-consumer note where the "unlocked" story is built earlier against a test double. Build order follows Depends on.
  - `content-data/013` says it unlocks `simulation-core/019`, but `content-data/013` depends (transitively) on `simulation-core/019`.
  - `input-controls/009` says it unlocks `player-character/005`, but `input-controls/009` depends (transitively) on `player-character/005`.
  - `input-controls/012` says it unlocks `player-character/016`, but `input-controls/012` depends (transitively) on `player-character/016`.
  - `input-controls/012` says it unlocks `player-character/017`, but `input-controls/012` depends (transitively) on `player-character/017`.
  - `input-controls/018` says it unlocks `player-character/017`, but `input-controls/018` depends (transitively) on `player-character/017`.

## Waves

### Wave 1 (9)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/013](simulation-core/story-013-op-reply-types-descriptors.md) | Op and Reply value types, OpKind and op descriptors | — |
| [simulation-core/018](simulation-core/story-018-id-allocators-slottable.md) | Entity and mail id allocators, and the SlotTable | — |
| [simulation-core/023](simulation-core/story-023-clientlayer-shell-inputlatch.md) | ClientLayer shell, InputLatch and the IHostClock seam | — |
| [content-data/001](content-data/story-001-defindex-stringid-deftable.md) | DefIndex assignment, StringNamespace def members and DefTable — ordinal conte… | — |
| [content-data/002](content-data/story-002-closed-content-enums.md) | Closed content enums and their data-spelling tables | — |
| [content-data/003](content-data/story-003-diagnostics-and-rule-registry.md) | Content diagnostics and the validator rule registry core | — |
| [world-stub/001](world-stub/story-001-iworldquery-height-field-kernel.md) | `IWorldQuery` contract, geometry value types and the height-field kernel (hei… | — |
| [player-character/003](player-character/story-003-atan2bam-atanq15-table.md) | `TrigQ15.Atan2Bam` and the committed `AtanQ15` table | — |
| [input-controls/025](input-controls/story-025-ci-lint-no-direct-input-reads.md) | CI lint — no direct `Input.*` reads or `InputMap` mutation outside `src/Game/… | — |

### Wave 2 (9)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/014](simulation-core/story-014-op-ring-enqueueop.md) | Op ring and EnqueueOp (stamping, reserve, refusals) | simulation-core/013 |
| [simulation-core/024](simulation-core/story-024-framerunner.md) | FrameRunner — the per-frame pump | simulation-core/023 |
| [content-data/004](content-data/story-004-balance-registry-simsettings.md) | BalanceKey registry, BalanceTable and SimSettings | content-data/003 |
| [content-data/005](content-data/story-005-m1a-def-records.md) | M-1a def records — ItemDef, ContainerDef, MailKindDef, DestinationTypeDef, Ma… | content-data/001, content-data/002 |
| [content-data/016](content-data/story-016-decimal-to-scaled-fieldspec-units.md) | DecimalToScaled reader, FieldSpec units and ExactTickCheck | content-data/003 |
| [content-data/017](content-data/story-017-content-sources-syntax-stage.md) | IContentSource, InMemory/Directory sources and the stage-1 syntax walk | content-data/003 |
| [player-character/004](player-character/story-004-geometry-segment-intervals-heightfield-march-obb.md) | `Sim/Geometry` primitives — `SegmentIntervals`, `HeightfieldMarch` and `Obb.T… | world-stub/001 |
| [input-controls/002](input-controls/story-002-inputframe-latch-writer-routerstateinfo.md) | `InputFrame`, `InputEdge`, `IInputLatchWriter`, `RouterStateInfo` and the `In… | simulation-core/023 |
| [godot-host-shell/001](godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md) | Godot game project — `PerformativeMail.Game.csproj`, `PerformativeMail.Game.s… | — |

### Wave 3 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/015](simulation-core/story-015-event-log-call-scopes-delivery.md) | Event log, call scopes and phase-13 delivery | simulation-core/014 |
| [content-data/006](content-data/story-006-stat-statdef-ruleflag.md) | Stat enum, StatDef table (M-1a rows) and RuleFlag | content-data/004 |
| [input-controls/003](input-controls/story-003-clientcore-input-foundations-uieventlog.md) | Client.Core input foundations — `RawInputRecord`, `IInputClock`, `PhysicalInp… | input-controls/002 |
| [godot-host-shell/002](godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md) | `Main.tscn` scene root, the ADR-0019 node tree, explicit project settings and… | godot-host-shell/001 |

### Wave 4 (5)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/016](simulation-core/story-016-event-overflow-pull-readers.md) | Event overflow (single tier + masks), misuse raises and pull readers | simulation-core/015 |
| [simulation-core/019](simulation-core/story-019-canonical-state-dump.md) | Canonical state dump (CanonicalWriter, contributors, Sim Core section) | simulation-core/014, simulation-core/015, simulation-core/018 |
| [content-data/007](content-data/story-007-f1-statresolve-f5-worstcase.md) | F1 StatResolve and F5 WorstCaseStat — pure Int128 formulas | content-data/006 |
| [input-controls/004](input-controls/story-004-routerstate-predicates-reachable-states.md) | `RouterState` tuple, live predicates as data and the reachable-state golden l… | input-controls/002, input-controls/003 |
| [godot-host-shell/003](godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md) | `GameLoop` frame loop — `_Process` → `FrameRunner.RunFrame()` with a `Stopwat… | simulation-core/024, godot-host-shell/002 |

### Wave 5 (2)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/017](simulation-core/story-017-builder-validation-codes.md) | Builder validation — SimBuildException codes for ops, events and readers | simulation-core/013, simulation-core/016 |
| [input-controls/005](input-controls/story-005-action-map-builtin-m1a-defaults.md) | The action map — `ActionDef`, aliases and the M-1a `BuiltInActionMap` with it… | input-controls/002, input-controls/003, input-controls/004 |

### Wave 6 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/020](simulation-core/story-020-phase0-drain-prechecks-replies.md) | Phase-0 drain, Core pre-checks and OpReply append | simulation-core/014, simulation-core/015, simulation-core/017, simulation-core/018, simulation-core/019 |
| [world-stub/002](world-stub/story-002-tile-flags-bounds-clamp-snap.md) | Tile flags (Walkable precompute, Water hint, Blocked), map bounds, `ClampInsi… | simulation-core/017, world-stub/001 |
| [input-controls/006](input-controls/story-006-conflict-validator-chord-resolution.md) | Conflict validator over reachable states, most-specific-chord resolution and … | input-controls/004, input-controls/005 |
| [input-controls/019](input-controls/story-019-settings-input-codec-and-validation.md) | `settings.input` — integer-unit `InputSettings`, the versioned overrides-only… | content-data/004, input-controls/002, input-controls/003, input-controls/005 |

### Wave 7 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/021](simulation-core/story-021-transform-sample-ring-latch.md) | Transform sample ring, tick-boundary latch and stamping | simulation-core/019, simulation-core/020 |
| [simulation-core/022](simulation-core/story-022-playerjoin-playerleave-handlers.md) | PlayerJoin and PlayerLeave System handlers | simulation-core/018, simulation-core/020 |
| [simulation-core/025](simulation-core/story-025-composition-root-reflection-test.md) | Composition root, SystemGraph and the constructor reflection test | simulation-core/017, simulation-core/018, simulation-core/020, simulation-core/023 |
| [world-stub/003](world-stub/story-003-static-solids-4m-query.md) | World-owned static solids and the 4 m bucket-grid `QueryStaticSolids` | world-stub/001, world-stub/002 |

### Wave 8 (7)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/026](simulation-core/story-026-inmemory-recorder-replay.md) | In-memory recorder (ops and samples) and ReplayDriver | simulation-core/016, simulation-core/019, simulation-core/020, simulation-core/021, simulation-core/025 |
| [simulation-core/028](simulation-core/story-028-architecture-rules-sc-arch-1.md) | Architecture rules suite — SC-ARCH-1 | simulation-core/023, simulation-core/024, simulation-core/025 |
| [network-adapter-loopback/002](network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md) | Op wire codec registry, request envelope and the H-NET-5 fuzz test | simulation-core/013, simulation-core/017, simulation-core/022 |
| [content-data/008](content-data/story-008-contentset-builtincontent-m1a.md) | ContentSet and BuiltInContent.CreateM1a — the M-1a greybox set | simulation-core/025, content-data/004, content-data/005, content-data/006 |
| [world-stub/004](world-stub/story-004-addressid-lots-destination-ids.md) | `AddressId`, the lot/address table, destination ids in lot order, address and… | simulation-core/018, content-data/001, world-stub/001, world-stub/003 |
| [world-stub/005](world-stub/story-005-post-office-spawn-pad.md) | Post Office record and spawn-pad slots (8 × 1200 mm) with build-time validati… | world-stub/002, world-stub/003 |
| [player-character/021](player-character/story-021-camera-boom-solver-config.md) | Camera rig profiles and the boom solver against the Sim geometry (Godot-free,… | world-stub/001, world-stub/003, player-character/004 |

### Wave 9 (15)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/027](simulation-core/story-027-sc-det-1-determinism-harness.md) | SC-DET-1 — same-machine determinism harness (T1) | simulation-core/019, simulation-core/020, simulation-core/021, simulation-core/022, simulation-core/025, simulation-core/026 |
| [network-adapter-loopback/003](network-adapter-loopback/story-003-hostadapter-inbound-actor-busy.md) | HostAdapter inbound — peer table, actor stamping, EnqueueOp, busy and bad_req… | simulation-core/014, simulation-core/022, simulation-core/024, network-adapter-loopback/002 |
| [content-data/009](content-data/story-009-cv-gen-set-rules.md) | General set-level rules — CV-GEN-01, 09, 17, 18 | content-data/003, content-data/006, content-data/008 |
| [content-data/010](content-data/story-010-cv-grd-cv-mail-rules.md) | Grid and Mail content rules — CV-GRD-01…04, 06, 07 and CV-MAIL-01…06 | content-data/003, content-data/007, content-data/008 |
| [content-data/011](content-data/story-011-cv-plr-rules.md) | Player Character content rules — CV-PLR M-1a subset | content-data/003, content-data/008 |
| [content-data/012](content-data/story-012-cv-thr-rules.md) | Throwing content rules — CV-THR M-1a subset | content-data/003, content-data/007, content-data/008 |
| [content-data/013](content-data/story-013-canonical-encoding-contenthash.md) | Canonical content encoding, managed SHA-256 and ContentHash | content-data/001, content-data/008 |
| [content-data/014](content-data/story-014-statsheet-build-read-explain.md) | StatSheet.Build, cached cells, Read and Explain (team + per-slot sheets) | content-data/006, content-data/007, content-data/008 |
| [world-stub/006](world-stub/story-006-m1a-street-and-fixtures.md) | `M1aStreet` — the hand-authored 10-house street — and the `WorldTablesFixture… | world-stub/001, world-stub/002, world-stub/003, world-stub/004, world-stub/005 |
| [mail-destinations/001](mail-destinations/story-001-mail-table-paged-soa.md) | Mail table — paged SoA indexed by `mailId`, row allocation in manifest order,… | simulation-core/018, simulation-core/019, content-data/004, world-stub/004 |
| [mail-destinations/011](mail-destinations/story-011-spawn-value-f1-kind-counts-f3.md) | Spawn value (F1) and units per kind (F3) — pure manifest arithmetic | content-data/005, content-data/008 |
| [mail-destinations/012](mail-destinations/story-012-batch-schedule-f2-destinations-f4-draws.md) | Batch schedule (F2), destination and street-pair draws (F4), and the fixed RN… | content-data/005, world-stub/004 |
| [player-character/001](player-character/story-001-players-module-carrier-state-join-binding.md) | Players system skeleton — `PlayerSettings`, `CarrierState`, life-state data a… | simulation-core/018, simulation-core/019, simulation-core/022, simulation-core/025, content-data/008, world-stub/001, world-stub/005 |
| [throwing-catching/001](throwing-catching/story-001-throw-rh-launch-f1-ballistic-f2.md) | `Throw.RH`, the F1 launch (`LaunchMath`) and the closed-form F2 ballistic (`B… | content-data/008, world-stub/001 |
| [godot-host-shell/004](godot-host-shell/story-004-boot-sequence-error-screen-content-step.md) | Boot sequence — the nine ADR-0019 steps as an explicit plan, the error screen… | simulation-core/025, content-data/008, godot-host-shell/002, godot-host-shell/003 |

### Wave 10 (9)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/029](simulation-core/story-029-zero-alloc-tick-timing-advisory.md) | Zero-allocation tick probe and tick-timing report (advisory at M-1a) | simulation-core/024, simulation-core/025, simulation-core/027 |
| [network-adapter-loopback/004](network-adapter-loopback/story-004-hostadapter-event-sink-outbound-batches.md) | HostAdapter as IEventSink — copy-only Deliver, audience fan-out and ordered p… | simulation-core/015, simulation-core/016, simulation-core/020, network-adapter-loopback/003 |
| [content-data/015](content-data/story-015-statwriter-sources-lifetimes-wiring.md) | IStatWriter, source identity and versioning, lifetimes, RuleFlags, and compos… | simulation-core/025, content-data/014 |
| [content-data/018](content-data/story-018-schema-and-build-stages.md) | Schema and build stages for M-1a def files — FieldSpec tables, ids, enums, va… | content-data/001, content-data/002, content-data/005, content-data/009, content-data/013, content-data/016, content-data/017 |
| [world-stub/007](world-stub/story-007-world-dump-composition-replay-id.md) | `WorldTables` in the world — `World` dump section, composition wiring (`Const… | simulation-core/019, simulation-core/025, simulation-core/026, simulation-core/027, world-stub/006 |
| [mail-destinations/002](mail-destinations/story-002-destination-registry.md) | Destination registry built from `IWorldQuery.Lots` — lot-ordered SoA, `Destin… | content-data/005, content-data/008, world-stub/004, world-stub/006 |
| [player-character/002](player-character/story-002-tick-time-ports-carrier-env-scratch.md) | Tick-time input ports, `CarrierEnv`, `CarrierInput`, `CarrierScratch` and the… | world-stub/001, player-character/001 |
| [player-character/011](player-character/story-011-hold-ticks-holdeff.md) | `Hold.HoldTicks` — the single shared `holdEff` function | player-character/001 |
| [throwing-catching/002](throwing-catching/story-002-flightsweep-contact-ranks-winner.md) | `FlightSweep.FirstContact` — F4 swept contact, receiver ranks, descending-ent… | world-stub/001, world-stub/002, player-character/004, throwing-catching/001 |

### Wave 11 (8)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [network-adapter-loopback/005](network-adapter-loopback/story-005-reqid-dedupe-reply-cache.md) | reqId dedupe — last-16 reply cache and in-flight set | simulation-core/019, network-adapter-loopback/003, network-adapter-loopback/004 |
| [network-adapter-loopback/006](network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md) | ClientAdapter — reliable requests, batch intake for ClientLayer, IHostClock e… | simulation-core/023, network-adapter-loopback/002, network-adapter-loopback/004 |
| [content-data/019](content-data/story-019-content-loader-and-builtin-equality.md) | ContentLoader.Load — balance files, five-stage orchestration, shipped JSONC e… | content-data/004, content-data/008, content-data/010, content-data/011, content-data/012, content-data/013, content-data/018 |
| [grid-inventory/001](grid-inventory/story-001-inventory-module-containers.md) | Inventory module skeleton — container registry, M-1a containers and per-playe… | simulation-core/022, simulation-core/025, content-data/005, content-data/008, content-data/015 |
| [mail-destinations/003](mail-destinations/story-003-evaluate-and-effective-state.md) | `MailRules.Evaluate` (pure, 0-alloc) over facts structs, and the effective de… | content-data/005, world-stub/004, mail-destinations/002 |
| [player-character/005](player-character/story-005-locomotion-f1-f2-camera-relative-yaw.md) | Locomotion — F1 final speed, F2 acceleration, camera-relative heading, body y… | player-character/001, player-character/002, player-character/003 |
| [player-character/012](player-character/story-012-anchor-registry-iinteractable-spatial-grid.md) | Interaction anchor registry — `IInteractable`, `RegisterAnchor`, the verb cat… | content-data/002, content-data/003, content-data/011, world-stub/002, player-character/001, player-character/011 |
| [throwing-catching/003](throwing-catching/story-003-receiver-view-static-grid-solids-chest-probe.md) | Receiver gathering — static 4 m grid of catch volumes, `QueryStaticSolids`, m… | world-stub/002, world-stub/003, world-stub/006, mail-destinations/002, throwing-catching/001, throwing-catching/002 |

### Wave 12 (8)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [network-adapter-loopback/007](network-adapter-loopback/story-007-host-loopback-session-frame-timing.md) | Host loopback session — FrameRunner over loopback and next-tick reply timing | simulation-core/024, simulation-core/025, network-adapter-loopback/003, network-adapter-loopback/004, network-adapter-loopback/005, network-adapter-loopback/006 |
| [grid-inventory/002](grid-inventory/story-002-entry-pool-occupancy.md) | Entry pool, occupancy bitmaps and entry identity | grid-inventory/001 |
| [grid-inventory/004](grid-inventory/story-004-op-kinds-payloads-reasons.md) | Inventory op kinds, padding-free payloads, reason codes and the 24 B reply ex… | simulation-core/013, simulation-core/020, grid-inventory/001 |
| [mail-destinations/004](mail-destinations/story-004-mailpay-f5-f8-f10.md) | `MailPay` — timeliness (F5), unit pay (F6), unit penalty (F7), wallet guard (… | content-data/014, mail-destinations/003 |
| [player-character/006](player-character/story-006-ground-contact-gravity-jump-f3.md) | Ground contact, gravity and jump — F3, coyote time, buffered press and the ex… | world-stub/001, player-character/005 |
| [player-character/013](player-character/story-013-contexts-verbs-availability-gate-router-declarations.md) | Input contexts, the verb × context table (P14 router declarations) and the fr… | content-data/008, player-character/001, player-character/012 |
| [manual-delivery/001](manual-delivery/story-001-deliver-anchors-hold-ticks.md) | Deliver anchors (MD1) — one `IInteractable` per destination at run start, and… | content-data/014, mail-destinations/002, player-character/011, player-character/012 |
| [godot-host-shell/007](godot-host-shell/story-007-contenthost-minimal-iconentsource.md) | Minimal `ContentHost : IContentSource` over `res://content/` and the opt-in h… | content-data/017, content-data/019, godot-host-shell/004 |

### Wave 13 (5)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/003](grid-inventory/story-003-mailids-arena.md) | mailIds size-class arena — oldest-first take, sorted-union merge, exhaustion | grid-inventory/002 |
| [grid-inventory/006](grid-inventory/story-006-placement-stacking-whitelist.md) | Placement validity, StackKey stacking with effMax at op time, and the inserti… | content-data/014, grid-inventory/002, grid-inventory/004 |
| [player-character/007](player-character/story-007-substep-sweep-terrain-slopes-snap-map-edge.md) | Sub-step sweep over the height field — slopes, snap-down and the map-edge cla… | world-stub/001, world-stub/002, world-stub/006, player-character/002, player-character/006 |
| [player-character/014](player-character/story-014-focus-scoring-dwell-preview-binding.md) | Focus — F4 eligibility and score, tie-break, 4-tick dwell and the `PreviewSub… | simulation-core/023, player-character/001, player-character/003, player-character/012, player-character/013 |
| [throwing-catching/006](throwing-catching/story-006-throw-op-event-declarations-and-wire-codecs.md) | `ThrowRelease` OpKind, `ThrowLaunched` / `FlightResolved` events — declaratio… | simulation-core/013, simulation-core/015, network-adapter-loopback/002, network-adapter-loopback/004, network-adapter-loopback/007, throwing-catching/001 |

### Wave 14 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/005](grid-inventory/story-005-mutator-versions-events.md) | InventoryMutator, per-call versions, event records and InvVersion seals | simulation-core/015, simulation-core/020, grid-inventory/002, grid-inventory/003 |
| [player-character/008](player-character/story-008-box-colliders-stepup-slide-depenetration.md) | Static colliders — cylinder vs AABB, slides, step-up, ledges, ceilings and de… | world-stub/002, world-stub/003, player-character/002, player-character/007 |
| [player-character/015](player-character/story-015-focus-occlusion-lazy-score-order.md) | Focus occlusion — lazy, score-ordered line-of-sight against terrain and stati… | world-stub/003, player-character/004, player-character/014 |
| [player-character/016](player-character/story-016-press-resolution-tap-catch-armed.md) | Press resolution — Catch → the presented target's verb → catch-armed, the lat… | simulation-core/023, network-adapter-loopback/006, player-character/011, player-character/012, player-character/013, player-character/014 |

### Wave 15 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/009](grid-inventory/story-009-autoplace-stackkey-index.md) | AutoPlace (passes M/U/R, PassFirst/TargetFirst), StackKey non-full index and … | grid-inventory/005, grid-inventory/006 |
| [grid-inventory/011](grid-inventory/story-011-open-set-close-intake.md) | Open-set bookkeeping, Close op, leash put-away hook — and the Intake as the M… | grid-inventory/004, grid-inventory/005 |
| [grid-inventory/016](grid-inventory/story-016-weight-speed.md) | Weight → speed (R16, D1): weightPoints and speedPermille | content-data/014, grid-inventory/005 |
| [player-character/009](player-character/story-009-water-wade-swim-haulout-f7.md) | Water — F7 depth, wading, swim hysteresis, swim motion and haul-out | world-stub/001, world-stub/006, player-character/005, player-character/006, player-character/007, player-character/008 |

### Wave 16 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/007](grid-inventory/story-007-pick-move-ops.md) | Pick and Move ops — whole-entry moves, cross-container atomicity, no-op moves | grid-inventory/004, grid-inventory/005, grid-inventory/006, grid-inventory/011 |
| [grid-inventory/012](grid-inventory/story-012-intake-spawn-outfeed.md) | Spawn into the Intake (NoMerge, arrivalSeq) and the outfeed FIFO (PeekOutfeed… | simulation-core/018, grid-inventory/005, grid-inventory/009 |
| [player-character/010](player-character/story-010-golden-transform-hash-p2-1.md) | PCM-P2-1 — the 600-tick golden per-tick transform hash and the solver determi… | simulation-core/019, simulation-core/026, simulation-core/028, player-character/002, player-character/005, player-character/006, player-character/007, player-character/008, player-character/009 |
| [player-character/017](player-character/story-017-hold-counter-cancels-channel-focus-latch.md) | Hold counter — `heldTicks = tick − PressTick`, completion, the cancel list, c… | player-character/009, player-character/011, player-character/013, player-character/014, player-character/015, player-character/016 |

### Wave 17 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/008](grid-inventory/story-008-move-split-swap-merge.md) | Move split-and-place, hotbar swap from the cursor, and the Merge op | grid-inventory/003, grid-inventory/007 |
| [grid-inventory/013](grid-inventory/story-013-worlditem-expiry-cap.md) | WorldItem entity, absolute-tick expiry FIFOs, water float, cap eviction and t… | simulation-core/018, grid-inventory/005, grid-inventory/012 |
| [player-character/018](player-character/story-018-interaction-snapshot-iinteractionquery.md) | `InteractionSnapshot` publication and `IInteractionQuery` | simulation-core/023, player-character/011, player-character/013, player-character/014, player-character/016, player-character/017 |
| [player-character/019](player-character/story-019-host-checkinteraction-position-ingest.md) | Host side — `PlayersStep` (phase 3) sample ingest, `IPlayersApi.CheckInteract… | simulation-core/018, simulation-core/020, simulation-core/021, player-character/001, player-character/002, player-character/010, player-character/012, player-character/013 |

### Wave 18 (6)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/010](grid-inventory/story-010-quickmove-return-targets.md) | QuickMove (entry form), Return, and the R9 quick_move target order | grid-inventory/007, grid-inventory/008, grid-inventory/009, grid-inventory/011 |
| [grid-inventory/015](grid-inventory/story-015-inventory-api-feature-hooks.md) | IInventoryApi hooks for feature epics — ConsumeUnit, DetachForThrow, TryInser… | grid-inventory/009, grid-inventory/013 |
| [grid-inventory/020](grid-inventory/story-020-drop-op.md) | Drop op — whole entry to a WorldItem at +500 mm along facing, ground-snapped;… | grid-inventory/013, world-stub/001, world-stub/002, world-stub/003, player-character/001, player-character/009 |
| [grid-inventory/021](grid-inventory/story-021-backlog-intake-drain.md) | Backlog append and the IntakeDrain phase (≤ 32 entries/tick, idle exit O(1)) | grid-inventory/012, grid-inventory/013 |
| [player-character/020](player-character/story-020-carrierstep-clientlayer-integration.md) | Owner-role `CarrierStep` in the ClientLayer — `InputFrame` → `CarrierInput`, … | simulation-core/021, simulation-core/023, content-data/014, grid-inventory/016, world-stub/006, player-character/001, player-character/002, player-character/005, player-character/006, player-character/007, player-character/008, player-character/009, player-character/010, player-character/013, player-character/014, player-character/019 |
| [input-controls/007](input-controls/story-007-router-shell-press-machines-mute.md) | `InputRouter` shell — `Feed`, per-press machines (latch at press, mute until … | player-character/018, input-controls/002, input-controls/003, input-controls/004, input-controls/005, input-controls/006 |

### Wave 19 (5)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/014](grid-inventory/story-014-pickup-core-trycatch.md) | Pickup core (AutoPlace by the R9 pickup rows) and the TryCatch API with fumble | grid-inventory/009, grid-inventory/010, grid-inventory/013 |
| [mail-destinations/005](mail-destinations/story-005-walletgate-stubwallet-mail-system.md) | `IWalletGate` port, the M-1a `StubWallet` (`WalletChanged`, dump), and the Ma… | simulation-core/015, simulation-core/025, grid-inventory/015, world-stub/007, mail-destinations/001, mail-destinations/002 |
| [throwing-catching/008](throwing-catching/story-008-throwing-system-flight-table-dump-tickend.md) | `ThrowingSystem` shell, the `FlightTable` (SoA, 160 slots, wrap-safe `FlightI… | simulation-core/018, simulation-core/019, simulation-core/025, grid-inventory/015, throwing-catching/001 |
| [input-controls/008](input-controls/story-008-router-state-transitions-and-passthrough.md) | Router state transitions — panel open / close, `holding` and `body` from the … | player-character/013, input-controls/005, input-controls/006, input-controls/007 |
| [godot-host-shell/005](godot-host-shell/story-005-host-session-composition-ports-render-clock.md) | Host session wiring — `SimComposition.Build` over the loopback session, `Clie… | simulation-core/023, simulation-core/025, network-adapter-loopback/006, network-adapter-loopback/007, player-character/019, player-character/020, godot-host-shell/003, godot-host-shell/004 |

### Wave 20 (7)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/017](grid-inventory/story-017-unitledger-conservation.md) | UnitLedger conservation counters, tick-end check, INVENTORY_ASSERT full scan … | simulation-core/019, grid-inventory/005, grid-inventory/012, grid-inventory/013, grid-inventory/014, grid-inventory/015 |
| [grid-inventory/019](grid-inventory/story-019-range-gated-open-pickup-leash.md) | Range-gated Open and Pickup handlers and the leash auto-close (P13 chain, F5 … | grid-inventory/011, grid-inventory/014, player-character/019 |
| [mail-destinations/006](mail-destinations/story-006-complaint-buckets-f9.md) | Complaint deltas (F9) — lazy door and team token buckets, late-complaint wind… | content-data/005, mail-destinations/002, mail-destinations/005 |
| [throwing-catching/009](throwing-catching/story-009-throwrelease-validation-chain-and-leaky-bucket.md) | `ThrowRelease` host validation — the ordered check chain, claim-and-corrobora… | simulation-core/020, grid-inventory/015, player-character/001, player-character/019, throwing-catching/001, throwing-catching/006, throwing-catching/008 |
| [throwing-catching/022](throwing-catching/story-022-catcharm-op-arm-window-and-sender.md) | `CatchArm` — OpKind, host arm window (`armedUntil`), eligibility clears and t… | simulation-core/013, simulation-core/022, network-adapter-loopback/002, player-character/016, player-character/017, throwing-catching/008 |
| [input-controls/009](input-controls/story-009-analog-input-deadzone-trigger-move-look.md) | Analog input — F3 deadzone and response curve, F4 trigger hysteresis, movemen… | input-controls/003, input-controls/007, input-controls/008 |
| [godot-host-shell/006](godot-host-shell/story-006-presenters-deps-injection-event-stream.md) | Presentation plumbing — `PresenterDeps`, `Initialize(deps)` subtree injection… | network-adapter-loopback/006, network-adapter-loopback/007, world-stub/007, godot-host-shell/002, godot-host-shell/003, godot-host-shell/004, godot-host-shell/005 |

### Wave 21 (5)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [grid-inventory/018](grid-inventory/story-018-conservation-fuzz-determinism.md) | Conservation fuzz over the M-1a op set (H-R19-1 subset) and H-DET-1 two-world… | simulation-core/019, simulation-core/022, grid-inventory/007, grid-inventory/008, grid-inventory/009, grid-inventory/010, grid-inventory/011, grid-inventory/012, grid-inventory/013, grid-inventory/014, grid-inventory/015, grid-inventory/016, grid-inventory/017 |
| [mail-destinations/007](mail-destinations/story-007-consume-routine-hand-path-outcomes.md) | One consume routine (`MailConsumer.Consume`) and the hand path (`IMailApi.Con… | grid-inventory/015, mail-destinations/001, mail-destinations/002, mail-destinations/003, mail-destinations/004, mail-destinations/005, mail-destinations/006 |
| [input-controls/010](input-controls/story-010-steps-auto-repeat-drag-resolver.md) | Stepped verbs — F5 auto-repeat and stick-as-direction, F6 saturating step cou… | input-controls/007, input-controls/008, input-controls/009 |
| [input-controls/012](input-controls/story-012-interact-latch-toggle-translation-sprint.md) | Interact — the presented-target latch, Toggle-mode translation (G7), Sprint H… | player-character/018, input-controls/005, input-controls/007, input-controls/008, input-controls/009 |
| [godot-host-shell/008](godot-host-shell/story-008-viewcatalog-resolver-placeholder-uid-check.md) | `ViewCatalog` resource, `ViewCatalogResolver`, the placeholder view with a wa… | content-data/001, content-data/008, godot-host-shell/002, godot-host-shell/004, godot-host-shell/006 |

### Wave 22 (6)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [mail-destinations/008](mail-destinations/story-008-thrown-landing-and-tick-order.md) | Thrown landings (`IMailApi.ResolveThrownLanding`, R13) and the hand → landing… | mail-destinations/007 |
| [mail-destinations/009](mail-destinations/story-009-shift-stub-deliveries-state.md) | M-1a shift stub (Run Structure stand-in, phase 12) — `SetDeliveriesState`, th… | mail-destinations/005, mail-destinations/007 |
| [input-controls/011](input-controls/story-011-panel-cursor-navigation.md) | Panel navigation without a mouse (R8) — grid cursor stepping, origin landing,… | input-controls/008, input-controls/010 |
| [input-controls/013](input-controls/story-013-immediate-verbs-queue-sinks-dispatch.md) | Immediate verbs — `ImmediateAction` queue, `IImmediateActionSink` registry an… | simulation-core/013, input-controls/005, input-controls/007, input-controls/008, input-controls/010 |
| [input-controls/017](input-controls/story-017-devices-glyph-state-or-across-devices.md) | Devices — OR across keyboard / mouse / pads, hold ownership, duplicate-pad-ed… | input-controls/007, input-controls/009, input-controls/010, input-controls/012 |
| [godot-host-shell/009](godot-host-shell/story-009-entity-view-registry-pools-destination-views.md) | `EntityViewRegistry`, `EntityViewHost`, `NodePool<T>` with the ADR-0019 caps,… | world-stub/004, world-stub/006, godot-host-shell/004, godot-host-shell/005, godot-host-shell/006, godot-host-shell/008 |

### Wave 23 (5)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [mail-destinations/010](mail-destinations/story-010-client-mirrors-and-facts.md) | Client mirror parts — `DestinationMirror`, `WalletMirror`, `RunMirror` — and … | simulation-core/023, network-adapter-loopback/006, mail-destinations/002, mail-destinations/003, mail-destinations/004, mail-destinations/005, mail-destinations/009 |
| [mail-destinations/013](mail-destinations/story-013-manifest-roller-prepareshift.md) | Manifest roller — `PrepareShift(SpawnProfile)`, re-roll on input change, lock… | mail-destinations/002, mail-destinations/009, mail-destinations/011, mail-destinations/012 |
| [player-character/022](player-character/story-022-playerview-scene-render-pose.md) | `PlayerView` greybox scene and the local carrier's render pose (≤ 1-tick extr… | simulation-core/024, player-character/005, player-character/006, player-character/007, player-character/008, player-character/009, player-character/010, player-character/020, godot-host-shell/002, godot-host-shell/006, godot-host-shell/009 |
| [throwing-catching/010](throwing-catching/story-010-throwrelease-handler-launch-commit.md) | `ThrowReleaseHandler` Commit — F1 launch at the apply tick, `DetachForThrow`,… | content-data/014, grid-inventory/015, grid-inventory/017, mail-destinations/009, throwing-catching/001, throwing-catching/003, throwing-catching/006, throwing-catching/008, throwing-catching/009 |
| [godot-host-shell/010](godot-host-shell/story-010-greybox-street-terrain-view.md) | Greybox street — `TerrainView` from `IWorldQuery` (untextured ground, water p… | world-stub/001, world-stub/003, world-stub/006, godot-host-shell/002, godot-host-shell/005, godot-host-shell/006, godot-host-shell/009 |

### Wave 24 (8)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [simulation-core/030](simulation-core/story-030-sc-x-1-cross-system-batch-order.md) | SC-X-1 — cross-system batch order with real Grid and Throw ops | simulation-core/020, simulation-core/025, grid-inventory/007, throwing-catching/010 |
| [mail-destinations/014](mail-destinations/story-014-batch-firing-intake-backlog.md) | Batch firing — mailIds at the batch tick, Intake hand-off (`Spawn`, NoMerge),… | grid-inventory/012, grid-inventory/017, grid-inventory/021, mail-destinations/001, mail-destinations/005, mail-destinations/009, mail-destinations/013 |
| [player-character/023](player-character/story-023-camera-rig-node-focus-cone.md) | `CameraRig` node — follow spring, look, aim/swim profiles and the focus-cone … | simulation-core/024, player-character/020, player-character/021, player-character/022, input-controls/009, godot-host-shell/002, godot-host-shell/006 |
| [player-character/024](player-character/story-024-animationtree-greybox-rig-gait-spike.md) | Greybox rig, one `AnimationTree` state machine, gait playback and jump/land s… | content-data/011, player-character/020, player-character/022, godot-host-shell/002, godot-host-shell/009 |
| [manual-delivery/002](manual-delivery/story-002-deliver-scan-preview-f1.md) | `DeliverScan` (one scan order for host and client) and the pure deliver previ… | mail-destinations/003, mail-destinations/004, mail-destinations/010 |
| [throwing-catching/004](throwing-catching/story-004-throw-aim-quantiser-and-preview-step.md) | Throw aim quantiser and the `ThrowPreview` client step (4c) — the honest arc,… | simulation-core/023, mail-destinations/003, mail-destinations/010, player-character/014, player-character/020, throwing-catching/001, throwing-catching/002, throwing-catching/003 |
| [throwing-catching/011](throwing-catching/story-011-throwingstep-sweep-ground-water-solid-timeout.md) | `ThrowingStep` (phase 4) — the per-tick sweep, GROUND / WATER / solid / TIMEO… | grid-inventory/012, grid-inventory/013, world-stub/002, world-stub/006, throwing-catching/001, throwing-catching/002, throwing-catching/003, throwing-catching/008, throwing-catching/010 |
| [godot-host-shell/011](godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md) | Headless smoke — boot the greybox, ≥ 300 ticks, exit 0 — with the smoke wrapp… | simulation-core/024, network-adapter-loopback/007, godot-host-shell/001, godot-host-shell/002, godot-host-shell/003, godot-host-shell/004, godot-host-shell/005, godot-host-shell/006, godot-host-shell/007, godot-host-shell/008, godot-host-shell/009, godot-host-shell/010 |

### Wave 25 (8)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [mail-destinations/015](mail-destinations/story-015-mail-event-wire-codecs.md) | Wire codecs for Mail/Run/Wallet events — `DeliveryOutcome` ≤ 19 B with ordina… | network-adapter-loopback/002, network-adapter-loopback/004, network-adapter-loopback/007, mail-destinations/005, mail-destinations/006, mail-destinations/007, mail-destinations/009, mail-destinations/014 |
| [player-character/026](player-character/story-026-interaction-prompt-anchor-projection.md) | Interaction prompt anchor — projecting the focused anchor and its bounds to s… | simulation-core/024, player-character/018, player-character/022, player-character/023, godot-host-shell/003, godot-host-shell/006 |
| [manual-delivery/003](manual-delivery/story-003-availability-preview-step-tracker.md) | Availability (MD2), the `DeliverPreview` client step (4b) and the `DeliverPre… | network-adapter-loopback/006, mail-destinations/010, player-character/013, player-character/014, manual-delivery/001, manual-delivery/002 |
| [manual-delivery/005](manual-delivery/story-005-delivermatching-op-and-handler.md) | `DeliverMatching` — the Delivery-owned OpKind, descriptor, 24 B payload (24 B… | simulation-core/013, simulation-core/020, grid-inventory/015, mail-destinations/007, player-character/012, player-character/019, manual-delivery/001, manual-delivery/002 |
| [throwing-catching/005](throwing-catching/story-005-preview-label-model-f7-money-and-settle.md) | Preview label data — F7 stake money, the two-line label model, the 100 ms lab… | mail-destinations/004, mail-destinations/010, throwing-catching/004 |
| [throwing-catching/007](throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md) | `ThrowReleaseBuilder` — the release request from the last drawn preview, the … | simulation-core/023, network-adapter-loopback/006, throwing-catching/004, throwing-catching/006 |
| [throwing-catching/012](throwing-catching/story-012-mailbox-terminals-landing-handoff-f8.md) | Mailbox terminals — `ResolveThrownLanding` hand-off, MAILBOX_ACCEPT / MISDELI… | world-stub/004, mail-destinations/005, mail-destinations/008, mail-destinations/009, throwing-catching/003, throwing-catching/008, throwing-catching/010, throwing-catching/011 |
| [input-controls/014](input-controls/story-014-throw-aim-release-min-aim-toggle.md) | Throw aim-release — router-owned `aiming`, F11 minimum aim on the ms clock, T… | throwing-catching/004, input-controls/007, input-controls/008, input-controls/009, input-controls/012, input-controls/013 |

### Wave 26 (7)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [network-adapter-loopback/008](network-adapter-loopback/story-008-deliver-matching-wire-codec.md) | deliver_matching wire codec (24 B MATCH / 28 B MISDELIVER_ONE) | network-adapter-loopback/002, manual-delivery/005 |
| [mail-destinations/016](mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md) | Greybox `MailboxView` and house — flag pop and wobble outcome beats keyed fro… | world-stub/006, mail-destinations/007, mail-destinations/008, mail-destinations/015, godot-host-shell/002, godot-host-shell/006, godot-host-shell/008, godot-host-shell/009 |
| [manual-delivery/004](manual-delivery/story-004-press-latch-cancels-request.md) | Press, latch and hold (MD4) and the request (MD5, client side) — settle refus… | network-adapter-loopback/006, player-character/016, player-character/017, manual-delivery/001, manual-delivery/002, manual-delivery/003, manual-delivery/005 |
| [throwing-catching/013](throwing-catching/story-013-run-end-flush-disconnect-pause-lifecycle.md) | Flight lifecycle — run-end `FlushAllAsTimeout`, disconnect continuity, solo p… | simulation-core/022, throwing-catching/008, throwing-catching/010, throwing-catching/011, throwing-catching/012 |
| [throwing-catching/014](throwing-catching/story-014-preview-equals-host-fuzz-and-determinism-goldens.md) | Preview = host over 10 000 throws, the T2 cross-platform goldens and the no-d… | simulation-core/027, world-stub/006, mail-destinations/010, throwing-catching/001, throwing-catching/002, throwing-catching/003, throwing-catching/004, throwing-catching/010, throwing-catching/011, throwing-catching/012 |
| [throwing-catching/018](throwing-catching/story-018-aim-ribbon-view.md) | `AimRibbonView` (Godot) — the screen-space solid / dashed / dotted arc, end m… | player-character/023, player-character/026, throwing-catching/004, throwing-catching/005, godot-host-shell/002, godot-host-shell/003, godot-host-shell/006 |
| [input-controls/015](input-controls/story-015-one-commit-gate-g9.md) | The one-commit gate (G9) — in-progress / pending commits, "Busy", "Release to… | player-character/018, input-controls/012, input-controls/013, input-controls/014 |

### Wave 27 (4)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [network-adapter-loopback/009](network-adapter-loopback/story-009-sc-x-2-manual-delivery-over-loopback.md) | SC-X-2 — a real Manual Delivery request from the host's own client over loopb… | network-adapter-loopback/005, network-adapter-loopback/007, network-adapter-loopback/008, manual-delivery/004, manual-delivery/005 |
| [manual-delivery/006](manual-delivery/story-006-reply-verdict-hand-beat-keys.md) | Reply and verdict on the client (MD7) — the reply → hand-response table, the … | network-adapter-loopback/006, mail-destinations/015, manual-delivery/003, manual-delivery/004, manual-delivery/005 |
| [throwing-catching/015](throwing-catching/story-015-conservation-fuzz-replay-and-step-budget.md) | Exactly-one-terminal conservation fuzz (8 players, 18 000 ticks), canonical d… | simulation-core/026, simulation-core/027, simulation-core/029, simulation-core/030, grid-inventory/018, throwing-catching/008, throwing-catching/011, throwing-catching/012, throwing-catching/013, throwing-catching/014 |
| [input-controls/016](input-controls/story-016-back-ladder-pause-capture.md) | The Back ladder (R5), the `no_room` skip window (F13) and the Pause capture s… | input-controls/007, input-controls/008, input-controls/012, input-controls/013, input-controls/014, input-controls/015 |

### Wave 28 (6)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [manual-delivery/007](manual-delivery/story-007-integration-loopback-parity-edge-cases.md) | End-to-end over loopback — focus vs Pick-up, preview order = host order, clie… | network-adapter-loopback/005, network-adapter-loopback/007, network-adapter-loopback/008, mail-destinations/015, player-character/014, player-character/020, manual-delivery/001, manual-delivery/002, manual-delivery/003, manual-delivery/004, manual-delivery/005, manual-delivery/006 |
| [manual-delivery/011](manual-delivery/story-011-hand-presentation-flap-reach-lunge-stance.md) | Hand-side presentation — mailbox flap node, reach scrubbed by hold progress, … | mail-destinations/016, player-character/022, manual-delivery/003, manual-delivery/006, godot-host-shell/002, godot-host-shell/006, godot-host-shell/008, godot-host-shell/009 |
| [throwing-catching/017](throwing-catching/story-017-flight-mirror-timeline-and-launch-ghost-model.md) | `FlightMirror`, the flight render timeline (`hostTick − 3`, fast-forward by F… | simulation-core/023, network-adapter-loopback/006, manual-delivery/006, throwing-catching/001, throwing-catching/006, throwing-catching/007, throwing-catching/010, throwing-catching/011 |
| [throwing-catching/023](throwing-catching/story-023-catcher-receiver-contact-trycatch-terminals.md) | The armed-catcher receiver — F5 contact over the position history, rank-0 rac… | grid-inventory/014, player-character/019, throwing-catching/002, throwing-catching/003, throwing-catching/008, throwing-catching/011, throwing-catching/015, throwing-catching/022 |
| [input-controls/018](input-controls/story-018-focus-loss-and-pad-hotplug-core.md) | Focus loss and pad hot-plug in the router core — release-with-cancel, mute, s… | input-controls/007, input-controls/012, input-controls/013, input-controls/014, input-controls/016, input-controls/017 |
| [input-controls/020](input-controls/story-020-integration-hold-timing-and-interact-end-to-end.md) | Headless router → `InputLatch` → interaction integration (A) — grammar fire t… | simulation-core/023, simulation-core/024, simulation-core/025, world-stub/006, player-character/011, player-character/014, player-character/016, player-character/017, player-character/018, player-character/020, manual-delivery/001, manual-delivery/004, input-controls/002, input-controls/007, input-controls/008, input-controls/009, input-controls/012, input-controls/013, input-controls/014, input-controls/016, input-controls/017, input-controls/019 |

### Wave 29 (3)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [manual-delivery/008](manual-delivery/story-008-race-suite-bell-fuzz-soak.md) | Race suite (no misdeliver for a MATCH-latched hold), closing-bell fuzz and th… | simulation-core/026, simulation-core/027, grid-inventory/014, manual-delivery/004, manual-delivery/005, manual-delivery/007 |
| [throwing-catching/020](throwing-catching/story-020-flight-view-multimesh-trail-shadow.md) | `FlightView` (Godot) — the parcel in the air: one bundle mesh via `MultiMeshI… | world-stub/001, throwing-catching/017, throwing-catching/018, godot-host-shell/002, godot-host-shell/006, godot-host-shell/009 |
| [input-controls/021](input-controls/story-021-integration-cancels-toggle-commit-end-to-end.md) | Headless router → Sim integration (B) — the cancel-cause table, Interact reso… | network-adapter-loopback/006, network-adapter-loopback/008, grid-inventory/011, manual-delivery/005, throwing-catching/004, throwing-catching/006, throwing-catching/007, input-controls/012, input-controls/013, input-controls/014, input-controls/015, input-controls/016, input-controls/017, input-controls/018, input-controls/020 |

### Wave 30 (3)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [throwing-catching/021](throwing-catching/story-021-launch-ghost-release-snap-landing-beats.md) | Launch-ghost view, strain face and release snap, the whoosh, and the ground /… | network-adapter-loopback/007, player-character/022, player-character/024, manual-delivery/006, manual-delivery/008, throwing-catching/007, throwing-catching/017, throwing-catching/020, godot-host-shell/002, godot-host-shell/006 |
| [input-controls/022](input-controls/story-022-replay-corpus-zero-unauthored-verbs.md) | Input replay corpus — 200 seeded + 10 adversarial traces and the "zero unauth… | simulation-core/026, grid-inventory/020, manual-delivery/004, throwing-catching/007, input-controls/020, input-controls/021 |
| [input-controls/023](input-controls/story-023-fantasy-scripts-toggle-run-route-parity-a11y.md) | Fantasy-test scripts — the hold-free shift (H-FX-4), route input-count parity… | grid-inventory/014, grid-inventory/020, input-controls/006, input-controls/019, input-controls/020, input-controls/021 |

### Wave 31 (1)

| Story | Title | Open dependencies (earlier waves) |
|---|---|---|
| [input-controls/024](input-controls/story-024-router-core-zero-allocation-gate.md) | Router-core zero-allocation gate (H-PERF-3, blocking) and the advisory frame-… | simulation-core/029, input-controls/007, input-controls/008, input-controls/009, input-controls/010, input-controls/011, input-controls/012, input-controls/013, input-controls/014, input-controls/015, input-controls/016, input-controls/017, input-controls/018, input-controls/019, input-controls/022 |

## Blocked (not scheduled)

A story is Blocked only when a governing ADR is unwritten/not Accepted, a required UX spec is missing, it waits on an external spike verdict, or (grid-inventory/022) its content contradicts an Accepted ADR and must be re-scoped.

| Story | Blocker | Unblock by | Story deps (for when unblocked) |
|---|---|---|---|
| [grid-inventory/022](grid-inventory/story-022-deliver-matching-handler.md) | Story content conflicts with Accepted ADR-0017 / ADR-0009 erratum (handler now Delivery-owned) | Re-scope to Inventory primitives (match scan, `ConsumeUnit`, seals); handler + H-X-1 cases live in `manual-delivery/story-005`; then `/story-readiness` | grid-inventory/015, grid-inventory/019, mail-destinations/007, manual-delivery/005 |
| [grid-inventory/023](grid-inventory/story-023-panel-grid-view-hotbar.md) | UX spec missing: `design/ux/inventory-panel.md` | `/ux-design` + `/ux-review` (GDD OQ14) | grid-inventory/001, grid-inventory/002, grid-inventory/003, grid-inventory/004, grid-inventory/005, grid-inventory/006, grid-inventory/007, grid-inventory/008, grid-inventory/009, grid-inventory/010, grid-inventory/011, godot-host-shell/001, godot-host-shell/003, godot-host-shell/006, godot-host-shell/008 |
| [grid-inventory/024](grid-inventory/story-024-panel-interactions-feedback.md) | UX spec missing: `design/ux/inventory-panel.md`; also Story 023; HUD toast queue is ADR-0018 (not written) | `/ux-design` inventory panel; then 023 | grid-inventory/019, grid-inventory/020, grid-inventory/023, input-controls/007, input-controls/010, input-controls/011 |
| [manual-delivery/009](manual-delivery/story-009-telemetry-latched-branch-handshare.md) | ADR-0024 (persistence & telemetry) not written | `/architecture-decision` ADR-0024 → Accepted | manual-delivery/004, manual-delivery/006 |
| [manual-delivery/010](manual-delivery/story-010-prompt-content-as-data.md) | ADR-0018 (HUD composition, prompt data) not written | `/architecture-decision` ADR-0018 → Accepted | player-character/018, manual-delivery/003, manual-delivery/004 |
| [throwing-catching/016](throwing-catching/story-016-telemetry-t14-session-stats.md) | ADR-0024 (persistence & telemetry) not written | `/architecture-decision` ADR-0024 → Accepted | manual-delivery/009, throwing-catching/007, throwing-catching/010, throwing-catching/011, throwing-catching/017 |
| [throwing-catching/019](throwing-catching/story-019-arc-label-pill-and-reason-strip.md) | UX spec missing: `design/ux/throwing-catching.md`; ADR-0018 not written | `/ux-design` + `/ux-review` Throwing & Catching; ADR-0018 | throwing-catching/004, throwing-catching/005, throwing-catching/007, throwing-catching/018 |
| [throwing-catching/024](throwing-catching/story-024-catch-end-to-end-two-carriers-history-ring.md) | ADR-0020 (host position history ring) not written; Player Character ring story not yet created | `/architecture-decision` ADR-0020, then `/create-stories player-character` for the ring | network-adapter-loopback/007, player-character/020, throwing-catching/022, throwing-catching/023 |
| [input-controls/026](input-controls/story-026-godot-input-adapter-node.md) | ADR-0012 §7 — OQ-IC-9 spike verdict (`input-controls/story-001`, In Progress) | Spike `REPORT.md` GO / GO-WITH-DELTAS | input-controls/001, input-controls/003, input-controls/007, input-controls/025, godot-host-shell/001, godot-host-shell/002, godot-host-shell/003 |
| [input-controls/027](input-controls/story-027-adapter-mouse-mode-gui-focus-hotplug-project-setting.md) | ADR-0012 §7 — OQ-IC-9 spike verdict | Spike verdict; then 026 | input-controls/001, input-controls/008, input-controls/016, input-controls/018, input-controls/026, godot-host-shell/002, godot-host-shell/006 |
| [input-controls/028](input-controls/story-028-gameloop-hookup-router-boot-settings-file.md) | ADR-0012 §7 — OQ-IC-9 spike verdict | Spike verdict; then 026, 027 | simulation-core/023, simulation-core/024, input-controls/001, input-controls/012, input-controls/013, input-controls/015, input-controls/016, input-controls/019, input-controls/026, input-controls/027, godot-host-shell/002, godot-host-shell/003, godot-host-shell/004 |
| [input-controls/029](input-controls/story-029-adapter-integration-suite-and-manual-evidence.md) | ADR-0012 §7 — OQ-IC-9 spike verdict | Spike verdict; then 026–028 | input-controls/001, input-controls/026, input-controls/027, input-controls/028, godot-host-shell/002, godot-host-shell/011 |

## Ready but gated behind a Blocked story

Status stays Ready (its own ADRs and specs exist); it cannot start until the Blocked story it depends on is done.

| Story | Gated by | Other open deps |
|---|---|---|
| [player-character/025](player-character/story-025-carry-pose-weight-blend-idle-doze.md) | grid-inventory/023 | simulation-core/027, player-character/010, player-character/020, player-character/022, player-character/024 |

## Wave 0 — Complete / In Progress

- [simulation-core/001](simulation-core/story-001-build-config-sim-assert-ci-scaffold.md) — Complete
- [simulation-core/002](simulation-core/story-002-id-value-types.md) — Complete
- [simulation-core/003](simulation-core/story-003-numerics-toolkit.md) — Complete
- [simulation-core/004](simulation-core/story-004-determinism-analyzers-sc-arch-2.md) — Complete
- [simulation-core/005](simulation-core/story-005-bam-trigq15-stablesort.md) — Complete
- [simulation-core/006](simulation-core/story-006-tickrate-f1-time-conversions.md) — Complete
- [simulation-core/007](simulation-core/story-007-simcounters-simcheckring.md) — Complete
- [simulation-core/008](simulation-core/story-008-rng-primitives-golden-vectors.md) — Complete
- [simulation-core/009](simulation-core/story-009-rng-registry-owner-views.md) — Complete
- [simulation-core/010](simulation-core/story-010-tickdriver-f2-accumulator.md) — Complete
- [simulation-core/011](simulation-core/story-011-phase-table-builder-tick-loop.md) — Complete
- [simulation-core/012](simulation-core/story-012-world-lifecycle-guards-faults.md) — Complete
- [network-adapter-loopback/001](network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md) — Complete
- [input-controls/001](input-controls/story-001-oq-ic-9-input-spike.md) — In Progress

## Same-file deferrals applied

- `godot-host-shell/001` moved out of wave 1: shares `.github/workflows/tests.yml` with `input-controls/025`.
