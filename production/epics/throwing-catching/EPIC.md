# Epic: Throwing & Catching (M-1a slice)

> **Layer**: Feature
> **GDD**: design/gdd/throwing-catching.md (In Review)
> **Architecture Module**: Sim.Throwing (`src/PerformativeMail.Sim/Throwing/`; throw preview in Sim.ClientLayer `Previews`) + aim ribbon, launch ghost and bundle mesh (Godot)
> **Milestone slice**: M-1a per the GDD's milestone slice (T1–T6 with ground, water, solid, timeout and mailbox receivers; T3 preview; T11; T12; T14; the release snap, whoosh, landing squash and trail; one bundle mesh). The **Sim-side catch** (T7, F5) is pulled forward from M0 at the orchestrator's request (see Overview).
> **Engine Risk**: LOW (Sim ballistic). MEDIUM for the presentation (screen-space dashed ribbon with no `DrawLine`, since line AA was removed in 4.7; `MultiMeshInstance3D`).
> **Est. size**: L — 28 stories (2 Complete, 19 Ready, 7 Deferred; refreshed 2026-10-28, throwing-catching 002 Complete, PR #55)
> **Implementation priority**: 7 of 9
> **Status**: Ready
> **Stories**: 28 stories — 2 Complete, 19 Ready, 7 Deferred (counts refreshed 2026-10-28 from the story headers, throwing-catching 002 Complete, PR #55; before that 1 / 20 / 7, 2026-10-23, m1a-replan; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

Throwing is the game's signature chaos verb, "Heads Up!". Hold Throw to preview an **honest** arc for the selected hotbar entry, then release. The whole entry, a single letter or a 20-letter stack, flies a host-authoritative integer gravity arc and lands wherever it really goes:
- the right mailbox pays for every piece;
- the wrong one misdelivers every piece;
- otherwise it lands on the ground, floats in water or stops on a solid.

There are no dice in the Sim (owner-ratified D1). The preview calls the **same** Sim function as the host (D2: an exact preview every frame), so the arc tells the truth about the town.

**In scope at M-1a:**
- **T1–T2:** who can throw and when, including the release-time leaky-bucket cooldown (`throwCooldownTicks` 5) and refusals while swimming or Holding. The whole selected entry detaches to FLIGHT through Grid Inventory's `throw` op.
- **T3 — aim and preview** (client, ClientLayer `Previews`): patterns, markers, the ×N tag and the money label (F7, using Mail's `Evaluate`/F10).
- **T4–T5 — release, launch and flight:** F1 launch; the F2 closed-form integer ballistic `p(k)`, O(1) per tick; `FlightRecord`s (≤16, `flightId` u16 wrap-safe).
- **T6 — receivers and resolution order:** the F4 swept segment test against height field, water, solids, mailbox catch volumes and the timeout. Flights resolve in creation order with exactly one terminal state each, conserving units through `Mail.ResolveThrownLanding` and `IInventoryApi`. F8 handles the reject and remainder drop point.
- **T11–T12:** solids, roofs and run boundaries; the degenerate-strategy rulings.
- **T14:** telemetry, including `redAgeAtReleaseMs` for the Mail gate metric.
- **Engine-side constraints:** `Throwing.Step` in phase 4; static 4 m broad-phase grid; 0 allocation.
- **Presentation (M-1a feel scope):** release snap, whoosh, landing squash, trail, launch ghost on the release frame (outcome beats never before the domain event) and one bundle mesh.

**Catch pull-forward (decision).** The GDD tags T7 catch as M0. The orchestrator's priority list says "ballistic + catch". The **Sim-side** catch is included as the last stories of this epic: the catch arm state (host-tick window), the F5 host validation, contact over the 9-tick position history, and `IInventoryApi.TryCatch` with TargetFirst AutoPlace into the hotbar. It is tested headless with two simulated carriers. These stories are optional for the M-1a gate: solo has no catcher. The catch prompt, T8 bonk, T13 relay, the network protocol and Toggle catch stay M0. This is logged in the decision log.

**Out of scope:**
- goon and belt receivers, T9 thrown damage and SIGNED FOR → M-1b;
- T10 Cannon Arm → Perks;
- the flight protocol and wire sizes (ADR-0021) → M0;
- haptics, tier meshes and reduced motion → later.

**Why these ADRs govern it.**
- ADR-0005 rules that Throwing receives no RNG stream. Any future randomness must come from a registered stream by launch ordinal.
- ADR-0006 provides floor-based RH, `CeilDiv`/`FloorDiv` and `isqrt`, with no truncating `/` on signed values. That is what makes the preview and the host bit-identical.
- ADR-0004 fixes `flightId` wrap-safety.
- ADR-0009 owns the `throw` op (selected hotbar entry only; other sources → `bad_request`) and `TryCatch`.
- ADR-0010 provides `ThrowDef` and the balance knobs.
- ADR-0003 orders `ThrowingStep` after `PlayersStep`.

**Risks.**
- (1) ~~The ballistic and flight core (ADR-0015) is not written.~~ **Resolved 2026-10-16:** ADR-0015 and ADR-0016 are Accepted (with ADR-0014, 0017, 0019, 0026), so 17 of 21 TRs are now covered. Still uncovered: ADR-0021 (flight protocol, M0), ADR-0024 (telemetry file) and ADR-0020 (position-history ring, M0).
- (2) Field-name leftovers in content (`throwSpreadMrad` retired by D1, `cooldownSlackTicks`; Q-ARCH-07). Content-data story 012 flags them; stories here follow ADR-0010 names.
- (3) The catch pull-forward needs PC's position history ring, which is an M0 item under ADR-0020 (not written). **Handled:** stories 022–023 build the arm window and the F5 contact against a multi-sample test double behind `ICatcherPositionSource`; story 024 (end to end over the real ring) is **Blocked** on ADR-0020.
- (4) Several read-only accessors and ports the Throwing stories need are owned by no existing story (hotbar selection, `IPlayersApi.IsSwimming`/`IsMounted`, `IInventoryApi.CheckDetachForThrow`, Mail catch-volume and deliveries-state reads, a greybox `WorldItemView`, the caller of `FlushAllAsTimeout`). See `## Notes` "Unowned seams".

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0005: RNG Streams and Derivation | no Throwing stream (no dice); future randomness via a registered stream by launch ordinal | LOW |
| ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay | floor-based RH, `CeilDiv`/`FloorDiv`, `isqrt`; T1/T2 preview = host | LOW |
| ADR-0004: Identity and Id Spaces | `FlightId` u16 wrap-safe, creation-order processing | LOW |
| ADR-0009: InventoryOp Pipeline and Inventory Storage Layout | `throw` op (selected hotbar entry), `TryCatch`, conservation through FLIGHT | LOW |
| ADR-0010: Content Format, Loading, ContentSet and StatSheet | `ThrowDef`/BalanceConfig knobs + validator rules | LOW |
| ADR-0003: Phase Order, Composition Root and Cross-System Calls | `ThrowingStep` phase 4 after `PlayersStep`; sync calls to `Mail.ResolveThrownLanding`/`Inventory.TryCatch` | LOW |
| ADR-0008: Event Log, Replication Classes and Event Delivery | `ThrowLaunched`/`FlightResolved` events; `Removed` → `ThrowLaunched` → ok reply ordering (SC-X-1) | LOW (Sim) |
| ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (**Accepted 2026-10-16**, primary) | `Throw.RH`, closed-form F2, swept F4, static 4 m broad phase, receiver ranks + `Mix64` tie-break, exactly-one-terminal, `ThrowRelease` op `0x0140` and its validation chain, leaky bucket, Sim-side catch, `FlushAllAsTimeout` | LOW |
| ADR-0016: ClientLayer Previews and Presentation Timing (**Accepted 2026-10-16**) | throw preview = sub-step 4c, `FlightMirror`, flight timeline (`hostTick − 3`), launch ghost, outcome beats only from `PresentationEventStream` | LOW |
| ADR-0019: Godot Scene Architecture (**Accepted 2026-10-16**) | `AimRibbonView` (`DrawPrimitive` quads on a `CanvasLayer`), MultiMesh flights, ×N tag pool, pools | MEDIUM |
| *ADR-0012 / 0014 / 0013 / 0017 / 0026 (Accepted 2026-10-16)* | Immediate Throw verb sink; `ICatchPredictor`/Catch channel/`IPositionHistory` seam; `Sim/Geometry`; `ResolveThrownLanding`, `Evaluate`, `MailPay`; `IWorldQuery` (`QueryStaticSolids`, `Bounds`, snap/clamp) | LOW |
| *ADR-0020 / 0021 / 0024 (planned, **not written**)* | position-history ring (M0); flight protocol & wire narrowing (M0); telemetry file | — missing |

## GDD Requirements

Source: architecture.md v1.1 Appendix A; coverage per `docs/architecture/tr-registry.yaml` (verified 2026-10-16). **15 / 21 covered by an Accepted ADR; 6 are registry gaps: TR-throwing-010, 011, 012, 014, 015 (ADR-0021, planned, M0) and TR-throwing-020 (ADR-0024, planned).** ADR-0015 §1/§6/§7 nonetheless fix the M-1a host validation chain, the per-player leaky bucket and the Sim-side catch, so 010/011/012 have stories; 014/015 are M0 deferrals (story 006 carries the M-1a Sim-size baseline); 020 is the one story-blocking gap (story 016). Every TR below has a story, a content-data owner or a named deferral.

| TR-ID | Requirement | ADR Coverage | Slice | Stories |
|-------|-------------|--------------|-------|---------|
| TR-throwing-001 | Host-authoritative closed-form integer ballistic; O(1) p(k) | ADR-0015 ✅ | M-1a | 001, 004, 010, 011, 014 |
| TR-throwing-002 | No RNG in throwing; future randomness via registered stream by launch ordinal | ADR-0005 ✅ | M-1a | 001, 013, 014 (absence tests) |
| TR-throwing-003 | Preview calls same Sim function as host; identical over 10,000 throws | ADR-0015 ✅ | M-1a | 002, 004, 005, 007, 014 |
| TR-throwing-004 | Floor-based RH, CeilDiv/FloorDiv, isqrt; never truncating / on signed values | ADR-0006 ✅ | M-1a | 001 |
| TR-throwing-005 | Swept segment test per tick per flight (≤90 ticks); slab/circle/cylinder/sphere/heightfield | ADR-0015 ✅ (+ADR-0026) | M-1a | 002, 003, 011, 023 |
| TR-throwing-006 | Receiver resolution ordering; flights in creation order | ADR-0015 ✅ | M-1a | 002, 011, 012, 023 |
| TR-throwing-007 | Broad phase bounds \|R\|≤65536 mm; static 4 m grid | ADR-0015 ✅ (+ADR-0026) | M-1a | 002, 003 |
| TR-throwing-008 | Throwing.Step phase 4; 0.15 ms p99; 0 alloc; 16 flights + 8 catchers | ADR-0015 ✅ | M-1a (timing advisory; 0 B blocking) | 008, 011, 015, 023 |
| TR-throwing-009 | FlightRecord snapshot fields; flightId wrap-safe u16 | ADR-0004 ✅ | M-1a | 006, 008, 010 |
| TR-throwing-010 | Fixed 9-check host validation; claim-and-corroborate origin | ❌ registry: ADR-0021 (planned); **ADR-0015 §1 fixes the chain for M-1a** | M0 per registry; M-1a chain implemented | 007 (client half), 009 |
| TR-throwing-011 | Leaky-bucket cooldown; team cap ≤48 flights/s | ❌ registry: ADR-0021 (planned); ADR-0015 §6 fixes the per-player bucket | M-1a cooldown; team cap M0 | 007, 009, 010 (team cap deferred) |
| TR-throwing-012 | Catch arm window host-tick based; contact over 9-tick history | ❌ registry: ADR-0021 (planned); ADR-0015 §7 fixes the Sim rule | M0 (Sim-side arm pulled forward) | 022, 023; end-to-end 024 (**Blocked**, ADR-0020) |
| TR-throwing-013 | Exactly one terminal state per flight; conservation through flush/disconnect | ADR-0015 ✅ | M-1a | 008, 011, 012, 013, 015, 023 |
| TR-throwing-014 | Message sizes ThrowRelease 25 B, ThrowLaunched 30 B, FlightResolved 18 B, CatchArm 1 B | ❌ ADR-0021 (planned) | M0 | 006 (M-1a Sim-size baseline 28/40/32 B); narrowing deferred |
| TR-throwing-015 | ≈66 B per flight per peer; budget pressure at cap | ❌ ADR-0021 (planned) | M0 | — deferred |
| TR-throwing-016 | Flights 0 B JoinState; remotes render at hostTickEst−3, fast-forward via F2 | ADR-0016 ✅ | M0 (client timeline built at M-1a) | 017, 020 (remote protocol deferred) |
| TR-throwing-017 | Aim preview ≤0.3 ms/frame, 0 alloc, screen-space dashed ribbon | ADR-0019 ✅ | M-1a | 004, 005, 018, 019 |
| TR-throwing-018 | Launch ghost on release frame; outcome beats never before domain event | ADR-0016 ✅ | M-1a | 007, 017, 021 |
| TR-throwing-019 | ThrowDef/BalanceConfig knobs with validator rules | ADR-0010 ✅ | M-1a | content-data 008 and 012 (not this epic); consumed by 001 |
| TR-throwing-020 | Telemetry to session stats file only | ❌ ADR-0024 (planned, not written) | M-1a | 016 (**Blocked**) |
| TR-throwing-021 | Spin from flightId; MultiMesh bundles; hidden beyond 35 m | ADR-0019 ✅ | M-1a | 017, 020 |

**Requirements with no TR-ID in the registry** (cited by GDD rule / acceptance id in the stories): the label text, money and settle rules (T3 "Label", "No stale verdict", F7; stories 005, 019 — AC TC-F7-1, TC-T3-1 d, TC-VA-2), the throw reason strings (T1; AC TC-U-2 c; story 019), the preview sound edge (Visual/Audio Audio; TC-VA-2; story 005), the release snap / whoosh / landing beats (Feel & Comedy; stories 021), F8 reject / remainder drop point (stories 012; TC-F8-1), T11 run-end flush (story 013; TC-T11-1), the Mail-gate metric `redAgeAtReleaseMs` (T14; story 016), and the receiver rank table as data (THROW-CV-05; content-data story 012).

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [`Throw.RH`, the F1 launch (`LaunchMath`) and the closed-form F2 ballistic (`Ballistic.PositionAt`)](story-001-throw-rh-launch-f1-ballistic-f2.md) | Logic | Complete | ADR-0015 |
| 002 | [`FlightSweep.FirstContact` — F4 swept contact, receiver ranks, descending-entry mailbox rule, `(f, rank, dist², Mix64)` winner](story-002-flightsweep-contact-ranks-winner.md) | Logic | Complete | ADR-0015 |
| 003 | [Receiver gathering — static 4 m grid, `QueryStaticSolids`, map edges, chest → origin probe, surface-below](story-003-receiver-view-static-grid-solids-chest-probe.md) | Logic | Ready | ADR-0015 |
| 004 | [Throw aim quantiser and the `ThrowPreview` client step (4c): the honest arc, end class, published `ThrowPreviewState`](story-004-throw-aim-quantiser-and-preview-step.md) | Logic | Ready | ADR-0016 |
| 005 | [Preview label data — F7 stake money, two-line label model, 100 ms settle, `previewSettleMs`, preview sound edge](story-005-preview-label-model-f7-money-and-settle.md) | Logic | Ready | ADR-0016 |
| 006 | [`ThrowRelease` OpKind, `ThrowLaunched` / `FlightResolved` events — declarations, reasons and loopback wire codecs](story-006-throw-op-event-declarations-and-wire-codecs.md) | Integration | Ready | ADR-0015 |
| 007 | [`ThrowReleaseBuilder` — request from the last drawn preview, client cooldown hold, reply part](story-007-throw-release-builder-client-cooldown-reply.md) | Logic | Ready | ADR-0012 |
| 008 | [`ThrowingSystem` shell, `FlightTable` (160 slots, wrap-safe `FlightId`), dump section, tick-end conservation record](story-008-throwing-system-flight-table-dump-tickend.md) | Logic | Ready | ADR-0015 |
| 009 | [`ThrowRelease` host validation — ordered checks, claim-and-corroborate origin, leaky bucket (`TAT`)](story-009-throwrelease-validation-chain-and-leaky-bucket.md) | Logic | Ready | ADR-0015 |
| 010 | [`ThrowReleaseHandler` Commit — F1 launch at the apply tick, `DetachForThrow`, `FlightRecord`, `ThrowLaunched`, `ok`](story-010-throwrelease-handler-launch-commit.md) | Integration | Ready | ADR-0015 |
| 011 | [`ThrowingStep` (phase 4) — sweep, GROUND / WATER / solid / TIMEOUT terminals, WorldItem placement, `FlightResolved`](story-011-throwingstep-sweep-ground-water-solid-timeout.md) | Logic | Ready | ADR-0015 |
| 012 | [Mailbox terminals — `ResolveThrownLanding` hand-off, ACCEPT / MISDELIVER / REJECT, F8 drop point](story-012-mailbox-terminals-landing-handoff-f8.md) | Integration | Ready | ADR-0015 |
| 013 | [Flight lifecycle — run-end `FlushAllAsTimeout`, disconnect continuity, solo pause](story-013-run-end-flush-disconnect-pause-lifecycle.md) | Integration | Ready | ADR-0015 |
| 014 | [Preview = host over 10 000 throws, T2 goldens and the no-dice replay](story-014-preview-equals-host-fuzz-and-determinism-goldens.md) | Integration | Ready | ADR-0015 |
| 015 | [Exactly-one-terminal conservation fuzz (8 players, 18 000 ticks), dump / replay and the `Throwing.Step` budget](story-015-conservation-fuzz-replay-and-step-budget.md) | Integration | Ready | ADR-0015 |
| 016 | [T14 telemetry — per-launch / per-terminal records, `redAgeAtReleaseMs`, throw-share analyser (session stats file)](story-016-telemetry-t14-session-stats.md) | Logic | Ready | ADR-0024 |
| 017 | [`FlightMirror`, flight render timeline (`hostTick − 3`) and the launch-ghost reconciliation model](story-017-flight-mirror-timeline-and-launch-ghost-model.md) | Logic | Ready | ADR-0016 |
| 018 | [`AimRibbonView` (Godot) — solid / dashed / dotted arc, end markers, crosshair](story-018-aim-ribbon-view.md) | Visual/Feel | Ready | ADR-0019 |
| 019 | [Arc label pill and throw reason strip lines (UI)](story-019-arc-label-pill-and-reason-strip.md) | UI | Ready (UX spec `design/ux/throwing-catching.md`, 2026-10-16) | ADR-0019 |
| 020 | [`FlightView` (Godot) — MultiMesh bundle, spin, ×N tag, blob shadow, launch-verdict trail](story-020-flight-view-multimesh-trail-shadow.md) | Visual/Feel | Ready | ADR-0019 |
| 021 | [Launch-ghost view, strain face and release snap, whoosh, ground / water landing beats (`[OutcomeBeat]`)](story-021-launch-ghost-release-snap-landing-beats.md) | Visual/Feel | Ready | ADR-0016 |
| 022 | [`CatchArm` — OpKind, host arm window, eligibility clears, client `ICatchArmSender` (Sim-side catch 1/3)](story-022-catcharm-op-arm-window-and-sender.md) | Logic | Deferred (Evan 2026-10-23) | ADR-0015 |
| 023 | [Armed-catcher receiver — F5 contact over position history, rank 0, CAUGHT / FUMBLE via `TryCatch` (Sim-side catch 2/3)](story-023-catcher-receiver-contact-trycatch-terminals.md) | Integration | Deferred (Evan 2026-10-23) | ADR-0015 |
| 024 | [Catch end to end — two carriers over the real host position-history ring (Sim-side catch 3/3)](story-024-catch-end-to-end-two-carriers-history-ring.md) | Integration | Deferred (Evan 2026-10-23) | ADR-0015 |
| 025 | [Throw accessibility — the polite live-region lines for the arc label, and label/marker con](story-025-deferred-throw-accessibility.md) | UI | Deferred (Evan 2026-10-23 8:54 AM PT) | — |
| 026 | [`Throwing.Step` budget — TC-P-2 timing report, 0 B gate and the SC-FIX-WORST throw case](story-026-deferred-throwing-step-budget-zero-alloc.md) | Integration | Deferred (Evan 2026-10-23) | — |
| 027 | [T2 cross-platform goldens for `LaunchMath` / `Ballistic` / `FlightSweep` (Windows + Linux)](story-027-deferred-t2-cross-platform-goldens.md) | Integration | Deferred (Evan 2026-10-23) | — |
| 028 | [The host leaky-bucket throw cooldown (`TAT`) — check 7 `throw_cooldown` and the `TAT` comm](story-028-deferred-leaky-bucket-cooldown-tat.md) | Logic | Deferred (Evan 2026-10-23) | — |

**28 stories: 13 Logic, 10 Integration, 3 Visual/Feel, 2 UI — 1 Complete, 20 Ready, 7 Deferred (refreshed 2026-10-23 from the story headers, m1a-replan).**

**Suggested order:** 001 → 002 → 003 (primitives; 002 needs 001, 003 needs both) → 004, 005 (preview, owner side) in parallel with 006, 008 (declarations, state) → 007 (needs 004, 006) → 009 → 010 → 011 → 012 → 013 → 014 → 015 (host and resolution) → 017 → 018, 020, 021 (Godot-side; after `production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`, the scene-root story) → 022 → 023 (catch pull-forward, last; optional for the M-1a gate). Blocked: 016 (ADR-0024), 024 (ADR-0020). 019 (UI) is Ready since 2026-10-16 — after 005, 007, 018.

**Stories other epics should cite:**
- **input-controls epic** — `story-007-throw-release-builder-client-cooldown-reply.md` (the `IImmediateActionSink` target for the Throw verb; the request builder and the client cooldown), `story-004-throw-aim-quantiser-and-preview-step.md` (`ThrowPreviewState`: `NotThrowable` aim-start refusals and `EntryStale` for Input G2's cancel), `story-005-preview-label-model-f7-money-and-settle.md` (`previewSettleMs`, preview sound toggle), `story-022-catcharm-op-arm-window-and-sender.md` (`CatchArmSender` for the Catch channel; M0 Toggle translation). There is no charge input: speed is fixed by class, so the only throw input is the aim-hold and release (`throwMinAim` is the router's).
- **player-character epic** — stories 016/017 define `ICatchArmSender`, `ICatchPredictor` and the Catch channel; story 022 here implements the sender; the predictor stays `NullCatchPredictor` until M0.
- **mail-destinations / grid-inventory epics** — story 012 (landing hand-off, F8 drop point), story 010 (`DetachForThrow` caller), story 023 (`TryCatch` caller).
- **godot-host-shell epic** — stories 018, 020, 021 need its scene root, `PresentationRoot.Present` wiring, `Flights` node, `World/Entities`, an audio-presenter port and a `CanvasLayer` stack (`production/epics/godot-host-shell/story-002-main-scene-tree-project-settings-smoke-harness.md`, `production/epics/godot-host-shell/story-003-gameloop-frame-loop-clock-pause-fault.md`, `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md`, `production/epics/godot-host-shell/story-009-entity-view-registry-pools-destination-views.md`).

## Notes (decisions, deferrals, gaps)

**Sizing.** `story_granularity: fine` (one AC per story) was overridden by the orchestrator rule: each story is one `/dev-story` session (≤ ~1 day, ≤ ~600 lines of production code and tests), 1–10 closely related ACs. Same decision as the other epics. QL-STORY-READY was skipped (Lean mode); every `## QA Test Cases` reads "N/A — no qa-lead specs at this tier".

**M-1a scope and the catch.** Stories follow the GDD's milestone slice: T1–T6 with ground, water, solid, timeout and mailbox receivers; T3 preview with patterns, markers, ×N tag and money label; T11; T12 (verified by the fuzz lane not crashing); T14; release snap, whoosh, landing squash, trail, one bundle mesh. The Sim-side catch is **numbered last** (022–024) and is optional for the gate: 022 (arm window) and 023 (contact + `TryCatch`) are Ready and testable headless against a multi-sample test double; 024 (end to end over the real 9-tick ring) is **Blocked** on ADR-0020, which is not written (the Player Character epic deferred the ring to M0 with no story and kept only a newest-sample `IPositionHistory`).

**Deferred out-of-milestone rules (no stories; nothing is silently dropped):**
- *M0:* the catch prompt and `CatchPredictor` (TC-F5-1 a/d), carets and edge chevron (TC-T7-3), Toggle catch translation (router), T8 bonk with its cosmetic cooldown (TC-T8-1; so at M-1a an unarmed or ineligible player is **passed through**, not bonked), swimmer-WATER and aiming-BONK cases, T13 relay hops, FORWARDED stamp and chime (TC-T13-1), hat pins (T3), catch result lines and caught feed (TC-U-2 a/b), the remote-trail closing-box case (TC-VA-1 a), flight protocol and wire narrowing to 25/30/18/1 B (TR-014, TR-015, TC-N-1…N-4 beyond loopback), team cap ≤ 48 flights/s (TR-011), remote playback refinements (GDD OQ16), catch telemetry fields (T14 per-catch / no-catch cause / bonks / fumbles), manual fantasy tests TC-FA-1, TC-FA-2, TC-FE-4.
- *M-1b:* goon receiver and HIT_ENEMY with thrown damage F6 (TC-T9-1, TC-F6-1, TC-F4-1 d/f/g, TC-E-2, TC-X-4), belt receiver and `LaneInsert` (TC-X-5), the SIGNED FOR stamp, player-built constructs as solids; spawn-protection ending on a HIT_ENEMY throw.
- *M1:* vehicle bodies and roofs as OBB solids (T11 vehicle clauses).
- *Later:* Cannon Arm (T10: the formula supports `ThrowRangePermille` and `thrownPm`; the vectors TC-F1-1 b and `+260¢` are asserted non-gating), tier meshes and the 34 m / 36 m tag check (TC-VA-1 b), reduced motion (TC-VA-3), Force patterns, haptics, optional heavy-landing shake, optional aim-FOV narrowing.
- *Playtest-only (no code):* TC-U-3 (partly story 018 evidence), TC-FA-3, TC-FA-4.

**TR gaps.** TR-throwing-020 (ADR-0024, not written) blocks story 016. TR-throwing-010/011/012 are mapped to ADR-0021 in the registry but ADR-0015 §1/§6/§7 fixes their M-1a rules, so stories 009, 007, 022, 023 implement them and cite ADR-0015; the registry should be re-pointed at the next `/architecture-review` (not edited here). TR-throwing-014/015 are M0. Requirements with no TR-ID are listed under the TR table and cited by GDD rule/AC id in the stories.

**Unowned seams (cross-epic gaps; no other epic's story files were edited).** Each is flagged in the story that needs it:
1. **Hotbar selected-slot state** (host `IHotbarSelection` for ThrowRelease check 2 and the post-catch selection rule; client `ISelectedEntryView`): no grid-inventory, input-controls or player-character story owns it. Production wiring does not exist; stories 004, 009, 023 use ports + test doubles. (Player Character story 025 raised the sibling gap `IHeldItemView`.)
2. **`IInventoryApi` additions:** a read-only `CheckDetachForThrow` (the same decision `DetachForThrow` uses internally) so the validation order can be honoured without mutating (story 009); `DetachResult` fields (kind/def, weight class, units, `mailIds` handle) and `CatchResult` landing info (hotbar index or bag) (stories 010, 023); the Flight-held entry handle form (`FlightEntryRef`) is not pinned between grid-inventory 015/017 and mail 008 (stories 008, 012).
3. **`IPlayersApi` read accessors** `IsSwimming(slot)` and `IsMounted(slot)` (story 009), and a multi-sample `IPositionHistory` (ADR-0020, M0; story 023 uses `ICatcherPositionSource`).
4. **Mail reads:** a catch-volume accessor on the destination registry (story 003; client side via `DestinationMirror`) and a deliveries-state read for `openAtRelease` (story 010).
5. **Caller of `IThrowingApi.FlushAllAsTimeout`:** Run Structure has no epic and `ShiftStub` (mail 009) does not latch Victory / Run Over (story 013).
6. **Greybox `WorldItemView`** with a flight-bound hide-until-landed (ADR-0019 §2 lists it; no story builds it) — stories 020, 021 expose `IFlightLandedQuery`.
7. **Host knowledge of "aiming" and "panel open"** for catch eligibility (replicated carrier flags, ADR-0020/0021, M0) — story 022 uses `ICatcherEligibility` with doubles.
8. **Audio presenter and HUD:** `IAudioCueSink` (story 021), the HUD reason strip and protected rect (stories 018, 019; ADR-0018 not written).
9. **Reply `reqId` exposure** from the `ClientAdapter` for the router's `CommitSent` (story 007); **`IRenderClock`**, **`PresentationEventStream` / `[OutcomeBeat]`** and the **`DelayTransport`** harness are owned by network-adapter-loopback / manual-delivery stories 006 and 008 (stories 005, 017, 021 add them only if absent).
10. **Launch verdict on remote peers:** `ThrowLaunched` (ADR-0015 §9) carries no class; remote trails need it (ADR-0021, M0). Story 017 implements the local path only.
11. **Additive `ThrowBalance` keys** `maxFlights`, ghost knobs (`ghostBlendMs`, `ghostSnapMm`, `ghostHoldMaxMs`, `flightRenderDelayTicks`): content-data story 008 registers the other `throw.*` rows.

**GDD / ADR conflicts found (ADR followed; GDD sync at re-review):**
1. Message sizes: GDD `ThrowRelease` 25 B, `ThrowLaunched` 30 B, `FlightResolved` 18 B, `CatchArm` 1 B vs ADR-0015 §1/§9 Sim payloads 28 / 40 / 32 B (+ 1 B). Stories 006 and 022 implement the ADR sizes; narrowing is ADR-0021 (M0).
2. Flight capacity: architecture sketch / GDD "≤ 16" vs ADR-0015 §9 `maxFlights` = 160 (16 is the perf fixture). Story 008 uses 160.
3. Preview cadence: GDD "every rendered frame" vs ADR-0016 §2 sub-step 4c once per client tick. Story 004 follows the ADR; story 018 re-projects the published 3D polyline every frame.
4. Names: ADR-0016 §3 says `FlightQuery.Sweep`; ADR-0015 (owner) says `FlightSweep.FirstContact`. The manifest follows ADR-0015; stories use it.
5. Rounding: ADR-0015 §2 says the heightfield march uses `Throw.RH`; Player Character story 004 builds `HeightfieldMarch` with `Fix.MulDivRH`. Flagged in story 002 (additive overload, determinism is safe either way).
6. Validation chain: GDD T4 lists 9 checks; ADR-0015 adds check 10 `flight_cap` (counted, unreachable under the bucket). "On break" = `Frozen`.
7. `hash(flightId, receiverId)` (GDD) = `SplitMix.Mix64((flightId << 32) | receiverKey)` (ADR-0015).
8. `loftDeg`: GDD integer degrees vs content-data story 008 milli-degrees; story 004 converts.
9. TC-P-2: GDD tags it M0; ADR-0015 Validation makes the 0 B gate blocking and timing advisory from M-1a. Story 015 follows the ADR.
10. Catch prompt: ADR-0015 §8 assigns `CatchPredictor` to Throwing; the epic overview defers the prompt to M0. Player Character ships `NullCatchPredictor`.

**Interpretations recorded (decision log):** WorldItem placement search = attempt at the contact then back along −v by 1000 / 3000 / 6000 mm (story 011); the label/sound 100 ms settle runs in Client.Core on `IRenderClock` because the Sim never sees milliseconds (story 005); arming from an ineligible player replies `ok` and does not arm (story 022); `FlightResolved.unitsIn/unitsFumbled` are 0 except for CAUGHT/FUMBLE; the CatchArm edge cap drops silently with an `ok` reply.

**Godot-side stories** (018, 020, 021) are Ready per the orchestrator rule even though no godot-host-shell stories existed yet (they cited "godot-host-shell epic — <capability> story (not yet created)"; the stories now exist: `production/epics/godot-host-shell/EPIC.md` `## Stories`); the sibling manual-delivery story 011 chose `Blocked` for the same reason — reconcile at `/story-readiness` if the policy is made uniform. Evidence screenshots need a display or xvfb; without one they are recorded NOT RUN, never passed.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- The acceptance criteria from `design/gdd/throwing-catching.md` that carry no M0/M-1b/Later tag (the GDD's rule: "M-1a unless tagged") are verified. If the catch pull-forward lands, the Sim-side T7/F5 criteria are verified too
- All Logic and Integration stories have passing test files in `tests/` (`tests/unit/throwing/**`), including preview = host over 10 000 throws and exactly-one-terminal-state conservation
- All Visual/Feel and UI stories have retained screenshots in `production/qa/evidence/` (aim ribbon green/red patterns, ×N tag, landing squash), with a lead sign-off for Visual/Feel

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): Track A (playable greybox): 002 (A1), 006 (A1), 008 (A1), 003 (A2), 009 (A3), 010 (A5), 011 (A6), 004 (A7), 012 (A7), 005 (A8), 007 (A8), 013 (A8), 017 (A11), 018 (A14), 019 (A15), 020 (A15), 021 (A16); then Track B: 014 (B1), 015 (B2), 016 (B2); Deferred by Evan 2026-10-23 (not scheduled): 022, 023, 024, 025, 026, 027, 028. Earlier notes below are history.

Run `/story-readiness production/epics/throwing-catching/story-001-throw-rh-launch-f1-ballistic-f2.md`, then `/dev-story`. Work the stories in the suggested order; each story's `Depends on:` says what must be DONE first. Unblock 016 (ADR-0024) and 024 (ADR-0020 + a Player Character ring story) when their inputs exist.
