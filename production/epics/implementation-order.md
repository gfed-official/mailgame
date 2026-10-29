# Implementation Order — all epics (M-1a, playable-first)

> **Generated**: 2026-10-23 (m1a-replan) by `python3 tools/planning/gen_impl_order.py .` from every story's `## Dependencies` → `- Depends on:` line in `production/epics/*/story-*.md`.
> **Scope**: 220 stories in 12 epics — 117 Complete, 84 Ready, 19 Deferred.
> **Authority for the playable-first order and the deferrals**: Evan, Mail Lead chat, 2026-10-23 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"; 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". See `production/session-logs/decision-log.md` 2026-10-23.
> **Regenerate** whenever a story's Depends on or Status changes. The order is advisory; `/story-readiness` still gates each story.

## How to read this

- **Track A — playable greybox first.** Every not-yet-done story that the visible greybox path needs: the Godot host shell, input wiring, the player view and camera, deliver, throw, the mailbox view and the minimal HUD. It is the transitive dependency closure of these leaves (Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)): `godot-host-shell/010`, `godot-host-shell/009`, `input-controls/028`, `player-character/022`, `player-character/023`, `player-character/024`, `player-character/026`, `mail-destinations/016`, `manual-delivery/011`, `manual-delivery/010`, `throwing-catching/018`, `throwing-catching/019`, `throwing-catching/020`, `throwing-catching/021`, `grid-inventory/023`, `grid-inventory/019`, `grid-inventory/020`, `player-character/025`, `throwing-catching/012`, `throwing-catching/013`, `hud-notifications/001`, `hud-notifications/002`, `hud-notifications/003`.
- **Track B — everything else that is still M-1a.** Infrastructure-only, test-only and ADR-required hardening stories that the playable path does not need. They start after Track A, or in parallel when an implementer is idle.
- **Wave N** = stories whose dependencies are all in earlier waves (or Complete). An In Progress story sits in its wave with Status In Progress, and its dependants wait for it. Stories inside one wave can run in parallel; check declared files for overlaps before running two at once.
- **Gated** = Ready or In Progress, but depends (transitively) on a Blocked story. **Deferred** = moved out of M-1a by Evan on 2026-10-23; not scheduled.

## Dependency-cycle check

- **Depends-on graph: 0 cycles** (Tarjan SCC over 1064 edges, 220 nodes). None found.
- **Ready stories depending on a Deferred story: 0.**

## Track A — playable greybox (69 stories, 14 waves)

### A wave 1 (10)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/019](input-controls/story-019-settings-input-codec-and-validation.md) | `settings.input` — integer-unit `InputSettings`, defaults, the `user://input.… | Ready | — |
| [godot-host-shell/007](godot-host-shell/story-007-contenthost-minimal-iconentsource.md) | Minimal `ContentHost : IContentSource` over `res://content/` and the opt-in h… | Ready | — |
| [grid-inventory/019](grid-inventory/story-019-range-gated-open-pickup-leash.md) | Range-gated Open and Pickup handlers and the leash auto-close (P13 chain, F5 … | Ready | — |
| [grid-inventory/020](grid-inventory/story-020-drop-op.md) | Drop op — whole entry to a WorldItem at +500 mm along facing, ground-snapped;… | Ready | — |
| [mail-destinations/006](mail-destinations/story-006-complaint-buckets-f9.md) | Complaint deltas (F9) — lazy door and team token buckets, late-complaint wind… | Ready | — |
| [player-character/018](player-character/story-018-interaction-snapshot-iinteractionquery.md) | `InteractionSnapshot` publication and `IInteractionQuery` | Ready | — |
| [player-character/021](player-character/story-021-camera-boom-solver-config.md) | Camera rig profiles and the boom solver against the Sim geometry (Godot-free,… | Ready | — |
| [throwing-catching/003](throwing-catching/story-003-receiver-view-static-grid-solids-chest-probe.md) | Receiver gathering — static 4 m grid of catch volumes, `QueryStaticSolids`, m… | Ready | — |
| [throwing-catching/006](throwing-catching/story-006-throw-op-event-declarations-and-wire-codecs.md) | `ThrowRelease` OpKind, `ThrowLaunched` / `FlightResolved` events — declaratio… | Ready | — |
| [throwing-catching/008](throwing-catching/story-008-throwing-system-flight-table-dump-tickend.md) | `ThrowingSystem` shell, the `FlightTable` (SoA, 160 slots, wrap-safe `FlightI… | Ready | — |

### A wave 2 (3)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/007](input-controls/story-007-router-shell-press-machines-mute.md) | `InputRouter` shell — `Feed`, per-press machines (latch at press, mute until … | Ready | player-character/018 |
| [mail-destinations/007](mail-destinations/story-007-consume-routine-hand-path-outcomes.md) | One consume routine (`MailConsumer.Consume`) and the hand path (`IMailApi.Con… | Ready | mail-destinations/006 |
| [throwing-catching/009](throwing-catching/story-009-throwrelease-validation-chain-and-leaky-bucket.md) | `ThrowRelease` host validation — the ordered check chain and claim-and-corrob… | Ready | throwing-catching/006, throwing-catching/008 |

### A wave 3 (3)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/008](input-controls/story-008-router-state-transitions-and-passthrough.md) | Router state transitions — panel open / close, `holding` and `body` from the … | Ready | input-controls/007 |
| [mail-destinations/008](mail-destinations/story-008-thrown-landing-and-tick-order.md) | Thrown landings (`IMailApi.ResolveThrownLanding`, R13) and the hand → landing… | Ready | mail-destinations/007 |
| [mail-destinations/009](mail-destinations/story-009-shift-stub-deliveries-state.md) | M-1a shift stub (Run Structure stand-in, phase 12) — `SetDeliveriesState`, th… | Ready | mail-destinations/007 |

### A wave 4 (5)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/009](input-controls/story-009-analog-input-deadzone-trigger-move-look.md) | Analog input — F3 deadzone and response curve, F4 trigger hysteresis, movemen… | Ready | input-controls/007, input-controls/008 |
| [mail-destinations/010](mail-destinations/story-010-client-mirrors-and-facts.md) | Client mirror parts — `DestinationMirror`, `WalletMirror`, `RunMirror` — and … | Ready | mail-destinations/009 |
| [mail-destinations/013](mail-destinations/story-013-manifest-roller-prepareshift.md) | Manifest roller — `PrepareShift(SpawnProfile)`, re-roll on input change, lock… | Ready | mail-destinations/009 |
| [player-character/020](player-character/story-020-carrierstep-clientlayer-integration.md) | Owner-role `CarrierStep` in the ClientLayer — `InputFrame` → `CarrierInput`, … | Ready | input-controls/008 |
| [throwing-catching/010](throwing-catching/story-010-throwrelease-handler-launch-commit.md) | `ThrowReleaseHandler` Commit — F1 launch at the apply tick, `DetachForThrow`,… | Ready | mail-destinations/009, throwing-catching/003, throwing-catching/006, throwing-catching/008, throwing-catching/009 |

### A wave 5 (8)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/010](input-controls/story-010-steps-auto-repeat-drag-resolver.md) | Stepped verbs — F5 auto-repeat and stick-as-direction, F6 saturating step cou… | Ready | input-controls/007, input-controls/008, input-controls/009 |
| [input-controls/012](input-controls/story-012-interact-latch-toggle-translation-sprint.md) | Interact — the presented-target latch, Toggle-mode translation (G7), Sprint H… | Ready | input-controls/007, input-controls/008, input-controls/009, player-character/018 |
| [input-controls/026](input-controls/story-026-godot-input-adapter-node.md) | The Godot `InputAdapter` node — `_Input` gathering, arrival stamping, `DEVICE… | Ready | input-controls/007, input-controls/009 |
| [godot-host-shell/005](godot-host-shell/story-005-host-session-composition-ports-render-clock.md) | Host session wiring — `SimComposition.Build` over the loopback session, `Clie… | Ready | player-character/020 |
| [mail-destinations/014](mail-destinations/story-014-batch-firing-intake-backlog.md) | Batch firing — mailIds at the batch tick, Intake hand-off (`Spawn`, NoMerge),… | Ready | mail-destinations/009, mail-destinations/013 |
| [manual-delivery/002](manual-delivery/story-002-deliver-scan-preview-f1.md) | `DeliverScan` (one scan order for host and client) and the pure deliver previ… | Ready | mail-destinations/010 |
| [throwing-catching/004](throwing-catching/story-004-throw-aim-quantiser-and-preview-step.md) | Throw aim quantiser and the `ThrowPreview` client step (4c) — the honest arc,… | Ready | mail-destinations/010, player-character/020, throwing-catching/003 |
| [throwing-catching/011](throwing-catching/story-011-throwingstep-sweep-ground-water-solid-timeout.md) | `ThrowingStep` (phase 4) — the per-tick sweep, GROUND / WATER / solid / TIMEO… | Ready | throwing-catching/003, throwing-catching/008, throwing-catching/010 |

### A wave 6 (9)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/013](input-controls/story-013-immediate-verbs-queue-sinks-dispatch.md) | Immediate verbs — `ImmediateAction` queue, `IImmediateActionSink` registry an… | Ready | input-controls/007, input-controls/008, input-controls/010 |
| [input-controls/017](input-controls/story-017-devices-glyph-state-or-across-devices.md) | Devices — OR across keyboard / mouse / pads, hold ownership and duplicate-pad… | Ready | input-controls/007, input-controls/010, input-controls/012 |
| [godot-host-shell/006](godot-host-shell/story-006-presenters-deps-injection-event-stream.md) | Presentation plumbing — `PresenterDeps`, `Initialize(deps)` subtree injection… | Ready | godot-host-shell/005 |
| [grid-inventory/022](grid-inventory/story-022-deliver-matching-handler.md) | Host panel view — Inventory's read-only scan primitive for `deliver_matching`… | Ready | manual-delivery/002 |
| [mail-destinations/015](mail-destinations/story-015-mail-event-wire-codecs.md) | Wire codecs for Mail/Run/Wallet events — `DeliveryOutcome` ≤ 19 B with ordina… | Ready | mail-destinations/006, mail-destinations/007, mail-destinations/009, mail-destinations/014 |
| [manual-delivery/003](manual-delivery/story-003-availability-preview-step-tracker.md) | Availability (MD2), the `DeliverPreview` client step (4b) and the `DeliverPre… | Ready | mail-destinations/010, manual-delivery/002 |
| [throwing-catching/005](throwing-catching/story-005-preview-label-model-f7-money-and-settle.md) | Preview label data — F7 stake money, the two-line label model, the 100 ms lab… | Ready | mail-destinations/010, throwing-catching/004 |
| [throwing-catching/007](throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md) | `ThrowReleaseBuilder` — the release request from the last drawn preview, the … | Ready | throwing-catching/004, throwing-catching/006 |
| [throwing-catching/012](throwing-catching/story-012-mailbox-terminals-landing-handoff-f8.md) | Mailbox terminals — `ResolveThrownLanding` hand-off, MAILBOX_ACCEPT / MISDELI… | Ready | mail-destinations/008, mail-destinations/009, throwing-catching/003, throwing-catching/008, throwing-catching/010, throwing-catching/011 |

### A wave 7 (4)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/014](input-controls/story-014-throw-aim-release-min-aim-toggle.md) | Throw aim-release — router-owned `aiming`, F11 minimum aim on the ms clock, T… | Ready | input-controls/007, input-controls/008, input-controls/009, input-controls/012, input-controls/013, throwing-catching/004 |
| [godot-host-shell/008](godot-host-shell/story-008-viewcatalog-resolver-placeholder-uid-check.md) | `ViewCatalog` resource, `ViewCatalogResolver`, the placeholder view with a wa… | Ready | godot-host-shell/006 |
| [manual-delivery/005](manual-delivery/story-005-delivermatching-op-and-handler.md) | `DeliverMatching` — the Delivery-owned OpKind, descriptor, 24 B payload (24 B… | Ready | grid-inventory/022, mail-destinations/007, manual-delivery/002 |
| [throwing-catching/013](throwing-catching/story-013-run-end-flush-disconnect-pause-lifecycle.md) | Flight lifecycle — run-end `FlushAllAsTimeout`, disconnect continuity, solo p… | Ready | throwing-catching/008, throwing-catching/010, throwing-catching/011, throwing-catching/012 |

### A wave 8 (4)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/015](input-controls/story-015-one-commit-gate-g9.md) | The one-commit gate (G9) — in-progress / pending commits, "Busy", "Release to… | Ready | input-controls/012, input-controls/013, input-controls/014, player-character/018 |
| [godot-host-shell/009](godot-host-shell/story-009-entity-view-registry-pools-destination-views.md) | `EntityViewRegistry`, `EntityViewHost`, `NodePool<T>` with the ADR-0019 caps,… | Ready | godot-host-shell/005, godot-host-shell/006, godot-host-shell/008 |
| [grid-inventory/023](grid-inventory/story-023-panel-grid-view-hotbar.md) | Own-panel grid view and hotbar strip — one custom-drawn Control, 0 B/frame | Ready | godot-host-shell/006, godot-host-shell/008 |
| [manual-delivery/004](manual-delivery/story-004-press-latch-cancels-request.md) | Press, latch and hold (MD4) and the request (MD5, client side) — settle refus… | Ready | manual-delivery/002, manual-delivery/003, manual-delivery/005 |

### A wave 9 (6)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/016](input-controls/story-016-back-ladder-pause-capture.md) | The Back ladder (R5), the `no_room` skip window (F13) and the Pause capture s… | Ready | input-controls/007, input-controls/008, input-controls/012, input-controls/013, input-controls/014, input-controls/015 |
| [godot-host-shell/010](godot-host-shell/story-010-greybox-street-terrain-view.md) | Greybox street — `TerrainView` from `IWorldQuery` (untextured ground, water p… | Ready | godot-host-shell/005, godot-host-shell/006, godot-host-shell/009 |
| [mail-destinations/016](mail-destinations/story-016-mailbox-greybox-view-outcome-beats.md) | Greybox `MailboxView` and house — flag pop and wobble outcome beats keyed fro… | Ready | godot-host-shell/006, godot-host-shell/008, godot-host-shell/009, mail-destinations/007, mail-destinations/008, mail-destinations/015 |
| [player-character/022](player-character/story-022-playerview-scene-render-pose.md) | `PlayerView` greybox scene and the local carrier's render pose (≤ 1-tick extr… | Ready | godot-host-shell/006, godot-host-shell/009, player-character/020 |
| [manual-delivery/006](manual-delivery/story-006-reply-verdict-hand-beat-keys.md) | Reply and verdict on the client (MD7) — the reply → hand-response table, the … | Ready | mail-destinations/015, manual-delivery/003, manual-delivery/004, manual-delivery/005 |
| [manual-delivery/010](manual-delivery/story-010-prompt-content-as-data.md) | Deliver prompt content as data — `WritePrompt` fills ADR-0014's 32 B `PromptA… | Ready | manual-delivery/002, manual-delivery/003, manual-delivery/004, player-character/018 |

### A wave 10 (7)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/018](input-controls/story-018-focus-loss-and-pad-hotplug-core.md) | Focus loss in the router core — release-with-cancel, mute, swallowed recaptur… | Ready | input-controls/007, input-controls/012, input-controls/013, input-controls/014, input-controls/016, input-controls/017 |
| [godot-host-shell/011](godot-host-shell/story-011-headless-smoke-300-ticks-ci-critical-paths.md) | Headless smoke — boot the greybox, ≥ 300 ticks, exit 0 — with the smoke wrapp… | Ready | godot-host-shell/005, godot-host-shell/006, godot-host-shell/007, godot-host-shell/008, godot-host-shell/009, godot-host-shell/010 |
| [player-character/023](player-character/story-023-camera-rig-node-focus-cone.md) | `CameraRig` node — follow spring, look, aim/swim profiles and the focus-cone … | Ready | godot-host-shell/006, input-controls/009, player-character/020, player-character/021, player-character/022 |
| [player-character/024](player-character/story-024-animationtree-greybox-rig-gait-spike.md) | Greybox rig, one `AnimationTree` state machine, gait playback and jump/land s… | Ready | godot-host-shell/009, player-character/020, player-character/022 |
| [manual-delivery/011](manual-delivery/story-011-hand-presentation-flap-reach-lunge-stance.md) | Hand-side presentation — mailbox flap node, reach scrubbed by hold progress, … | Ready | godot-host-shell/006, godot-host-shell/008, godot-host-shell/009, mail-destinations/016, manual-delivery/003, manual-delivery/006, player-character/022 |
| [throwing-catching/017](throwing-catching/story-017-flight-mirror-timeline-and-launch-ghost-model.md) | `FlightMirror`, the flight render timeline (`hostTick − 3`, fast-forward by F… | Ready | manual-delivery/006, throwing-catching/006, throwing-catching/007, throwing-catching/010, throwing-catching/011 |
| [hud-notifications/001](hud-notifications/story-001-hud-read-model-money-formatter.md) | `IHudState` read model — wallet cents, F3 time-only shift timer, focused deli… | Ready | godot-host-shell/005, mail-destinations/009, mail-destinations/010, manual-delivery/010, player-character/018 |

### A wave 11 (4)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/027](input-controls/story-027-adapter-mouse-mode-gui-focus-hotplug-project-setting.md) | Adapter — mouse mode, GUI-focus policy, focus-loss delivery and the joypad pr… | Ready | godot-host-shell/006, input-controls/008, input-controls/016, input-controls/018, input-controls/026 |
| [player-character/025](player-character/story-025-carry-pose-weight-blend-idle-doze.md) | Carry pose by held item class, whole-body load blend and idle doze (P20) | Ready | grid-inventory/023, player-character/020, player-character/022, player-character/024 |
| [player-character/026](player-character/story-026-interaction-prompt-anchor-projection.md) | Interaction prompt anchor — projecting the focused anchor and its bounds to s… | Ready | godot-host-shell/006, player-character/018, player-character/022, player-character/023 |
| [hud-notifications/002](hud-notifications/story-002-wallet-readout-shift-timer-view.md) | `Main/Hud/Persistent` layer, `HudLayer` refresh, the wallet readout (`WalletV… | Ready | godot-host-shell/005, godot-host-shell/006, godot-host-shell/011, hud-notifications/001 |

### A wave 12 (3)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/028](input-controls/story-028-gameloop-hookup-router-boot-settings-file.md) | `GameLoop` hook-up — router construction at boot step 4, the per-frame call o… | Ready | input-controls/012, input-controls/013, input-controls/015, input-controls/016, input-controls/019, input-controls/026, input-controls/027 |
| [throwing-catching/018](throwing-catching/story-018-aim-ribbon-view.md) | `AimRibbonView` (Godot) — the screen-space solid / dashed / dotted arc, end m… | Ready | godot-host-shell/006, player-character/023, player-character/026, throwing-catching/004, throwing-catching/005 |
| [hud-notifications/003](hud-notifications/story-003-deliver-prompt-display.md) | `DeliverPromptView` — the deliver prompt rendered from manual-delivery 010's … | Ready | godot-host-shell/006, hud-notifications/001, hud-notifications/002, manual-delivery/010, player-character/018, player-character/026 |

### A wave 13 (2)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [throwing-catching/019](throwing-catching/story-019-arc-label-pill-and-reason-strip.md) | Arc label pill and the throw reason strip lines (UI) | Ready | throwing-catching/004, throwing-catching/005, throwing-catching/007, throwing-catching/018 |
| [throwing-catching/020](throwing-catching/story-020-flight-view-multimesh-trail-shadow.md) | `FlightView` (Godot) — the parcel in the air: one bundle mesh via `MultiMeshI… | Ready | godot-host-shell/006, godot-host-shell/009, throwing-catching/017, throwing-catching/018 |

### A wave 14 (1)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [throwing-catching/021](throwing-catching/story-021-launch-ghost-release-snap-landing-beats.md) | Launch-ghost view, strain face and release snap, the whoosh, and the ground /… | Ready | godot-host-shell/006, manual-delivery/006, player-character/022, player-character/024, throwing-catching/007, throwing-catching/017, throwing-catching/020 |

## Track B — remaining M-1a (15 stories, 3 waves)

### B wave 1 (8)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [simulation-core/030](simulation-core/story-030-sc-x-1-cross-system-batch-order.md) | SC-X-1 — cross-system batch order with real Grid and Throw ops | Ready | throwing-catching/010 |
| [input-controls/011](input-controls/story-011-panel-cursor-navigation.md) | Panel navigation without a mouse (R8) — grid cursor stepping, origin landing,… | Ready | input-controls/008, input-controls/010, input-controls/017 |
| [input-controls/020](input-controls/story-020-integration-hold-timing-and-interact-end-to-end.md) | Headless router → `InputLatch` → interaction integration (A) — grammar fire t… | Ready | input-controls/007, input-controls/008, input-controls/009, input-controls/012, input-controls/013, input-controls/014, input-controls/016, input-controls/017, input-controls/019, manual-delivery/004, player-character/018, player-character/020 |
| [input-controls/029](input-controls/story-029-adapter-integration-suite-and-manual-evidence.md) | Godot adapter integration suite and the MANUAL evidence runs (real pad, focus… | Ready | godot-host-shell/011, input-controls/026, input-controls/027, input-controls/028 |
| [grid-inventory/018](grid-inventory/story-018-conservation-fuzz-determinism.md) | Conservation fuzz over the M-1a op set (H-R19-1 subset) and H-DET-1 two-world… | Ready | — |
| [network-adapter-loopback/008](network-adapter-loopback/story-008-deliver-matching-wire-codec.md) | deliver_matching wire codec (24 B MATCH / 28 B MISDELIVER_ONE) | Ready | manual-delivery/005 |
| [manual-delivery/009](manual-delivery/story-009-telemetry-latched-branch-handshare.md) | Telemetry — `latchedBranch` per hold start and the `handShare` metric (sessio… | Ready | godot-host-shell/005, godot-host-shell/006, mail-destinations/007, manual-delivery/004, manual-delivery/006 |
| [throwing-catching/014](throwing-catching/story-014-preview-equals-host-fuzz-and-determinism-goldens.md) | Preview = host over 10 000 throws and the no-dice replay | Ready | mail-destinations/010, throwing-catching/003, throwing-catching/004, throwing-catching/010, throwing-catching/011, throwing-catching/012 |

### B wave 2 (6)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/021](input-controls/story-021-integration-cancels-toggle-commit-end-to-end.md) | Headless router → Sim integration (B) — the cancel-cause table, Interact reso… | Ready | input-controls/012, input-controls/013, input-controls/014, input-controls/015, input-controls/016, input-controls/017, input-controls/018, input-controls/020, manual-delivery/005, network-adapter-loopback/008, throwing-catching/004, throwing-catching/006, throwing-catching/007 |
| [grid-inventory/024](grid-inventory/story-024-panel-interactions-feedback.md) | Panel interactions through the Panel/Holding input contexts, and the M-1a fee… | Ready | grid-inventory/019, grid-inventory/020, grid-inventory/023, input-controls/007, input-controls/010, input-controls/011 |
| [network-adapter-loopback/009](network-adapter-loopback/story-009-sc-x-2-manual-delivery-over-loopback.md) | SC-X-2 — a real Manual Delivery request from the host's own client over loopb… | Ready | manual-delivery/004, manual-delivery/005, network-adapter-loopback/008 |
| [manual-delivery/007](manual-delivery/story-007-integration-loopback-parity-edge-cases.md) | End-to-end over loopback — focus vs Pick-up, preview order = host order, clie… | Ready | mail-destinations/009, mail-destinations/015, manual-delivery/002, manual-delivery/003, manual-delivery/004, manual-delivery/005, manual-delivery/006, network-adapter-loopback/008, player-character/020 |
| [throwing-catching/015](throwing-catching/story-015-conservation-fuzz-replay-and-step-budget.md) | Exactly-one-terminal conservation fuzz (8 players, 18 000 ticks) and canonica… | Ready | grid-inventory/018, simulation-core/030, throwing-catching/008, throwing-catching/011, throwing-catching/012, throwing-catching/013, throwing-catching/014 |
| [throwing-catching/016](throwing-catching/story-016-telemetry-t14-session-stats.md) | T14 telemetry — per-launch and per-terminal records, `redAgeAtReleaseMs` and … | Ready | manual-delivery/009, throwing-catching/007, throwing-catching/010, throwing-catching/011, throwing-catching/017 |

### B wave 3 (1)

| Story | Title | Status | Open dependencies (earlier waves) |
|---|---|---|---|
| [input-controls/022](input-controls/story-022-replay-corpus-zero-unauthored-verbs.md) | Input replay corpus — 200 seeded + 10 adversarial traces and the "zero unauth… | Ready | grid-inventory/020, input-controls/020, input-controls/021, manual-delivery/004, throwing-catching/007 |

## Blocked (0)

| Story | Title | Story deps |
|---|---|---|

## Ready but gated behind a Blocked story (0)

| Story | Track | Gated by |
|---|---|---|

## Deferred — out of the M-1a slice (19)

Each story file carries the "Deferred (Evan 2026-10-23 7:13 AM PT)" status, both of Evan's quotes and its target milestone. Nothing was deleted.

| Story | Title | Deferred to |
|---|---|---|
| [simulation-core/029](simulation-core/story-029-zero-alloc-tick-timing-advisory.md) | Zero-allocation tick probe and tick-timing report (advisory at M-1a) | M0 (out of the M-1a slice; was Ready) |
| [input-controls/023](input-controls/story-023-fantasy-scripts-toggle-run-route-parity-a11y.md) | Fantasy-test scripts — the hold-free shift (H-FX-4), route input-count parity… | M1 (accessibility pass) (out of the M-1a slice; was Ready) |
| [input-controls/024](input-controls/story-024-router-core-zero-allocation-gate.md) | Router-core zero-allocation gate (H-PERF-3, blocking) and the advisory frame-… | M0 (out of the M-1a slice; was Ready) |
| [input-controls/025](input-controls/story-025-ci-lint-no-direct-input-reads.md) | CI lint — no direct `Input.*` reads or `InputMap` mutation outside `src/Game/… | M0 (out of the M-1a slice; was Ready) |
| [input-controls/030](input-controls/story-030-glyph-state-last-used-device.md) | Glyph state — the last-used device, F9 flip rules and `router.Glyphs` (H-F9-1… | M1 (glyph sets, Glyph style and prompt rendering) (out of the M-1a slice; split from story 017) |
| [input-controls/031](input-controls/story-031-pad-hotplug-router-and-adapter-delivery.md) | Pad hot-plug — router `OnPadConnection` (cancel-never-commit, reconnect mute,… | M0 (out of the M-1a slice; split from stories 018 and 027) |
| [input-controls/032](input-controls/story-032-settings-input-overrides-writer-bindings-migrations.md) | `settings.input` rebinding support — the overrides-only writer, the `bindings… | M1 (Controls settings screen and remap) (out of the M-1a slice; split from story 019) |
| [grid-inventory/025](grid-inventory/story-025-deferred-live-region-feedback.md) | Inventory panel live-region feedback (TR-inventory-027) — the live-region `Co… | M1 accessibility pass (out of the M-1a slice; split from stories 023 and 024) |
| [player-character/027](player-character/story-027-movement-rulings-extra-jump-slide-td-091.md) | Movement rulings — extra-jump press resolution, `maxExtraJumps` 0–1, steep-sl… | M1 (out of the M-1a slice; was Ready) |
| [player-character/028](player-character/story-028-deferred-golden-cross-os-equality-and-blocking-counters.md) | PCM-P2-1 cross-OS golden equality (Windows + Linux) and the blocking PCM-PERF… | M0 (out of the M-1a slice; split from story 010) |
| [manual-delivery/008](manual-delivery/story-008-race-suite-bell-fuzz-soak.md) | Race suite (no misdeliver for a MATCH-latched hold), closing-bell fuzz and th… | M0 (out of the M-1a slice; was Ready) |
| [manual-delivery/012](manual-delivery/story-012-deferred-prompt-accessibility-localisation.md) | Deliver prompt accessibility and localisation — the screen-reader settle gate… | M1 or later (accessibility and localisation pass; out of the M-1a slice) |
| [throwing-catching/022](throwing-catching/story-022-catcharm-op-arm-window-and-sender.md) | `CatchArm` — OpKind, host arm window (`armedUntil`), eligibility clears and t… | M0 (out of the M-1a slice; was Ready) |
| [throwing-catching/023](throwing-catching/story-023-catcher-receiver-contact-trycatch-terminals.md) | The armed-catcher receiver — F5 contact over the position history, rank-0 rac… | M0 (out of the M-1a slice; was Ready) |
| [throwing-catching/024](throwing-catching/story-024-catch-end-to-end-two-carriers-history-ring.md) | Catch end to end — two simulated carriers over the real host position-history… | M0 (out of the M-1a slice; was Blocked) |
| [throwing-catching/025](throwing-catching/story-025-deferred-throw-accessibility.md) | Throw accessibility — the polite live-region lines for the arc label, and lab… | M1 or later (accessibility pass; out of the M-1a slice) |
| [throwing-catching/026](throwing-catching/story-026-deferred-throwing-step-budget-zero-alloc.md) | `Throwing.Step` budget — TC-P-2 timing report, 0 B gate and the SC-FIX-WORST … | M0 (out of the M-1a slice; split from story 015) |
| [throwing-catching/027](throwing-catching/story-027-deferred-t2-cross-platform-goldens.md) | T2 cross-platform goldens for `LaunchMath` / `Ballistic` / `FlightSweep` (Win… | M0 (out of the M-1a slice; split from story 014) |
| [throwing-catching/028](throwing-catching/story-028-deferred-leaky-bucket-cooldown-tat.md) | The host leaky-bucket throw cooldown (`TAT`) — check 7 `throw_cooldown` and t… | M0 (out of the M-1a slice; split from story 009) |

## Wave 0 — Complete (117)

[simulation-core/001](simulation-core/story-001-build-config-sim-assert-ci-scaffold.md), [simulation-core/002](simulation-core/story-002-id-value-types.md), [simulation-core/003](simulation-core/story-003-numerics-toolkit.md), [simulation-core/004](simulation-core/story-004-determinism-analyzers-sc-arch-2.md), [simulation-core/005](simulation-core/story-005-bam-trigq15-stablesort.md), [simulation-core/006](simulation-core/story-006-tickrate-f1-time-conversions.md), [simulation-core/007](simulation-core/story-007-simcounters-simcheckring.md), [simulation-core/008](simulation-core/story-008-rng-primitives-golden-vectors.md), [simulation-core/009](simulation-core/story-009-rng-registry-owner-views.md), [simulation-core/010](simulation-core/story-010-tickdriver-f2-accumulator.md), [simulation-core/011](simulation-core/story-011-phase-table-builder-tick-loop.md), [simulation-core/012](simulation-core/story-012-world-lifecycle-guards-faults.md), [simulation-core/013](simulation-core/story-013-op-reply-types-descriptors.md), [simulation-core/014](simulation-core/story-014-op-ring-enqueueop.md), [simulation-core/015](simulation-core/story-015-event-log-call-scopes-delivery.md), [simulation-core/016](simulation-core/story-016-event-overflow-pull-readers.md), [simulation-core/017](simulation-core/story-017-builder-validation-codes.md), [simulation-core/018](simulation-core/story-018-id-allocators-slottable.md), [simulation-core/019](simulation-core/story-019-canonical-state-dump.md), [simulation-core/020](simulation-core/story-020-phase0-drain-prechecks-replies.md), [simulation-core/021](simulation-core/story-021-transform-sample-ring-latch.md), [simulation-core/022](simulation-core/story-022-playerjoin-playerleave-handlers.md), [simulation-core/023](simulation-core/story-023-clientlayer-shell-inputlatch.md), [simulation-core/024](simulation-core/story-024-framerunner.md), [simulation-core/025](simulation-core/story-025-composition-root-reflection-test.md), [simulation-core/026](simulation-core/story-026-inmemory-recorder-replay.md), [simulation-core/027](simulation-core/story-027-sc-det-1-determinism-harness.md), [simulation-core/028](simulation-core/story-028-architecture-rules-sc-arch-1.md), [content-data/001](content-data/story-001-defindex-stringid-deftable.md), [content-data/002](content-data/story-002-closed-content-enums.md), [content-data/003](content-data/story-003-diagnostics-and-rule-registry.md), [content-data/004](content-data/story-004-balance-registry-simsettings.md), [content-data/005](content-data/story-005-m1a-def-records.md), [content-data/006](content-data/story-006-stat-statdef-ruleflag.md), [content-data/007](content-data/story-007-f1-statresolve-f5-worstcase.md), [content-data/008](content-data/story-008-contentset-builtincontent-m1a.md), [content-data/009](content-data/story-009-cv-gen-set-rules.md), [content-data/010](content-data/story-010-cv-grd-cv-mail-rules.md), [content-data/011](content-data/story-011-cv-plr-rules.md), [content-data/012](content-data/story-012-cv-thr-rules.md), [content-data/013](content-data/story-013-canonical-encoding-contenthash.md), [content-data/014](content-data/story-014-statsheet-build-read-explain.md), [content-data/015](content-data/story-015-statwriter-sources-lifetimes-wiring.md), [content-data/016](content-data/story-016-decimal-to-scaled-fieldspec-units.md), [content-data/017](content-data/story-017-content-sources-syntax-stage.md), [content-data/018](content-data/story-018-schema-and-build-stages.md), [content-data/019](content-data/story-019-content-loader-and-builtin-equality.md), [input-controls/001](input-controls/story-001-oq-ic-9-input-spike.md), [input-controls/002](input-controls/story-002-inputframe-latch-writer-routerstateinfo.md), [input-controls/003](input-controls/story-003-clientcore-input-foundations-uieventlog.md), [input-controls/004](input-controls/story-004-routerstate-predicates-reachable-states.md), [input-controls/005](input-controls/story-005-action-map-builtin-m1a-defaults.md), [input-controls/006](input-controls/story-006-conflict-validator-chord-resolution.md), [godot-host-shell/001](godot-host-shell/story-001-game-csproj-solution-gdignore-build-mapping.md), [godot-host-shell/002](godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md), [godot-host-shell/003](godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md), [godot-host-shell/004](godot-host-shell/story-004-boot-sequence-error-screen-content-step.md), [grid-inventory/001](grid-inventory/story-001-inventory-module-containers.md), [grid-inventory/002](grid-inventory/story-002-entry-pool-occupancy.md), [grid-inventory/003](grid-inventory/story-003-mailids-arena.md), [grid-inventory/004](grid-inventory/story-004-op-kinds-payloads-reasons.md), [grid-inventory/005](grid-inventory/story-005-mutator-versions-events.md), [grid-inventory/006](grid-inventory/story-006-placement-stacking-whitelist.md), [grid-inventory/007](grid-inventory/story-007-pick-move-ops.md), [grid-inventory/008](grid-inventory/story-008-move-split-swap-merge.md), [grid-inventory/009](grid-inventory/story-009-autoplace-stackkey-index.md), [grid-inventory/010](grid-inventory/story-010-quickmove-return-targets.md), [grid-inventory/011](grid-inventory/story-011-open-set-close-intake.md), [grid-inventory/012](grid-inventory/story-012-intake-spawn-outfeed.md), [grid-inventory/013](grid-inventory/story-013-worlditem-expiry-cap.md), [grid-inventory/014](grid-inventory/story-014-pickup-core-trycatch.md), [grid-inventory/015](grid-inventory/story-015-inventory-api-feature-hooks.md), [grid-inventory/016](grid-inventory/story-016-weight-speed.md), [grid-inventory/017](grid-inventory/story-017-unitledger-conservation.md), [grid-inventory/021](grid-inventory/story-021-backlog-intake-drain.md), [world-stub/001](world-stub/story-001-iworldquery-height-field-kernel.md), [world-stub/002](world-stub/story-002-tile-flags-bounds-clamp-snap.md), [world-stub/003](world-stub/story-003-static-solids-4m-query.md), [world-stub/004](world-stub/story-004-addressid-lots-destination-ids.md), [world-stub/005](world-stub/story-005-post-office-spawn-pad.md), [world-stub/006](world-stub/story-006-m1a-street-and-fixtures.md), [world-stub/007](world-stub/story-007-world-dump-composition-replay-id.md), [network-adapter-loopback/001](network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md), [network-adapter-loopback/002](network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md), [network-adapter-loopback/003](network-adapter-loopback/story-003-hostadapter-inbound-actor-busy.md), [network-adapter-loopback/004](network-adapter-loopback/story-004-hostadapter-event-sink-outbound-batches.md), [network-adapter-loopback/005](network-adapter-loopback/story-005-reqid-dedupe-reply-cache.md), [network-adapter-loopback/006](network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md), [network-adapter-loopback/007](network-adapter-loopback/story-007-host-loopback-session-frame-timing.md), [mail-destinations/001](mail-destinations/story-001-mail-table-paged-soa.md), [mail-destinations/002](mail-destinations/story-002-destination-registry.md), [mail-destinations/003](mail-destinations/story-003-evaluate-and-effective-state.md), [mail-destinations/004](mail-destinations/story-004-mailpay-f5-f8-f10.md), [mail-destinations/005](mail-destinations/story-005-walletgate-stubwallet-mail-system.md), [mail-destinations/011](mail-destinations/story-011-spawn-value-f1-kind-counts-f3.md), [mail-destinations/012](mail-destinations/story-012-batch-schedule-f2-destinations-f4-draws.md), [player-character/001](player-character/story-001-players-module-carrier-state-join-binding.md), [player-character/002](player-character/story-002-tick-time-ports-carrier-env-scratch.md), [player-character/003](player-character/story-003-atan2bam-atanq15-table.md), [player-character/004](player-character/story-004-geometry-segment-intervals-heightfield-march-obb.md), [player-character/005](player-character/story-005-locomotion-f1-f2-camera-relative-yaw.md), [player-character/006](player-character/story-006-ground-contact-gravity-jump-f3.md), [player-character/007](player-character/story-007-substep-sweep-terrain-slopes-snap-map-edge.md), [player-character/008](player-character/story-008-box-colliders-stepup-slide-depenetration.md), [player-character/009](player-character/story-009-water-wade-swim-haulout-f7.md), [player-character/010](player-character/story-010-golden-transform-hash-p2-1.md), [player-character/011](player-character/story-011-hold-ticks-holdeff.md), [player-character/012](player-character/story-012-anchor-registry-iinteractable-spatial-grid.md), [player-character/013](player-character/story-013-contexts-verbs-availability-gate-router-declarations.md), [player-character/014](player-character/story-014-focus-scoring-dwell-preview-binding.md), [player-character/015](player-character/story-015-focus-occlusion-lazy-score-order.md), [player-character/016](player-character/story-016-press-resolution-tap-catch-armed.md), [player-character/017](player-character/story-017-hold-counter-cancels-channel-focus-latch.md), [player-character/019](player-character/story-019-host-checkinteraction-position-ingest.md), [manual-delivery/001](manual-delivery/story-001-deliver-anchors-hold-ticks.md), [throwing-catching/001](throwing-catching/story-001-throw-rh-launch-f1-ballistic-f2.md), [throwing-catching/002](throwing-catching/story-002-flightsweep-contact-ranks-winner.md)
