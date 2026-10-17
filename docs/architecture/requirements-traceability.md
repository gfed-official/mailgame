# Architecture Traceability Index
Last Updated: 2026-10-16
Engine: Godot 4.7.1 (.NET / C#)
Source: generated from `docs/architecture/tr-registry.yaml` (/architecture-review, lean, 2026-10-16)

## Coverage Summary
- Total requirements: 250 (10 GDDs; belt-logistics, building-system, health-damage not yet extracted)
- Covered: 203 (81%)
- Partial: 0
- Gaps: 47
- TR-IDs cited by stories: 95 (all resolve in the registry)

## Per-system coverage

| System | GDD | Total | Covered | Gaps |
|---|---|---|---|---|
| sim | design/gdd/simulation-core.md | 45 | 42 | 3 |
| content | design/gdd/content-data-definitions.md | 30 | 30 | 0 |
| inventory | design/gdd/grid-inventory.md | 28 | 22 | 6 |
| input | design/gdd/input-controls.md | 24 | 24 | 0 |
| movement | design/gdd/player-character-movement.md | 26 | 16 | 10 |
| delivery | design/gdd/manual-delivery.md | 24 | 21 | 3 |
| mail | design/gdd/mail-destinations.md | 23 | 23 | 0 |
| throwing | design/gdd/throwing-catching.md | 21 | 15 | 6 |
| hud | design/gdd/hud-notifications.md | 21 | 4 | 17 |
| comms | design/gdd/comms.md | 8 | 6 | 2 |

## Full Matrix

| Requirement ID | GDD | Requirement | ADR Coverage | Status | Stories |
|---|---|---|---|---|---|
| TR-sim-001 | simulation-core.md | Whole fixed ticks of 1/30 s; TickRateHz=30 code constant; no variable step | ADR-0002 | ✅ | 1 |
| TR-sim-002 | simulation-core.md | Tick is uint32 from 0; differences in int64; first Tick() after Ready is tick 0 | ADR-0002 | ✅ | 1 |
| TR-sim-003 | simulation-core.md | TickDriver is a Godot-free integer accumulator in Hosting/; takes timestamps, never reads a clock | ADR-0002 | ✅ | 2 |
| TR-sim-004 | simulation-core.md | Catch-up cap maxCatchUpTicksPerFrame=4 (safe 3–8); excess dropped, never banked | ADR-0002 | ✅ | 1 |
| TR-sim-005 | simulation-core.md | M0 wall-clock guard catchUpGuardMs=10 re-read after each tick; ≥1 tick always runs | ADR-0002 | ✅ | 0 |
| TR-sim-006 | simulation-core.md | F2 accumulator invariant; Δ clamped to 1 s; freq 10^3..10^12; backwards clock counted | ADR-0002 | ✅ | 1 |
| TR-sim-007 | simulation-core.md | Sim on Godot main thread; owner thread captured at Freeze(); Debug asserts | ADR-0002 | ✅ | 1 |
| TR-sim-008 | simulation-core.md | Transport callbacks only enqueue; nothing applied inside a callback | ADR-0002 | ✅ | 5 |
| TR-sim-009 | simulation-core.md | Pause is a driver state; solo pause → Advance returns 0; SceneTree.Paused never used | ADR-0002 | ✅ | 1 |
| TR-sim-010 | simulation-core.md | Exactly 14 PhaseIds 0–13 in fixed order; ITickEndCheck between 12 and 13 | ADR-0003 | ✅ | 1 |
| TR-sim-011 | simulation-core.md | Unbound phase is a no-op; same-phase binders in composition order; new phases need R3 amendment | ADR-0003 | ✅ | 1 |
| TR-sim-012 | simulation-core.md | SimWorldBuilder binds phases/ops/events/readers/contributors pre-Freeze; violations throw SimBuildException | ADR-0003 | ✅ | 2 |
| TR-sim-013 | simulation-core.md | Composition root in Composition/; constructor injection only; reflection test on deps | ADR-0003 | ✅ | 2 |
| TR-sim-014 | simulation-core.md | SimWorld (host) and ClientLayer (client shell) separate; host loopback uses same encoder | ADR-0003 | ✅ | 6 |
| TR-sim-015 | simulation-core.md | Op = 64 B blittable: 16 B header (opSeq, actor, kind, reqId) + ≤48 B payload | ADR-0007 | ✅ | 1 |
| TR-sim-016 | simulation-core.md | Actor stamped by adapter from peer; System=0x00000001; opSeq monotonic, never reused | ADR-0007 | ✅ | 3 |
| TR-sim-017 | simulation-core.md | Op ring 4096 (pow2); full → false, no opSeq consumed, adapter replies busy; systemOpReserve 64 | ADR-0007 | ✅ | 3 |
| TR-sim-018 | simulation-core.md | Drain ≤64 ops/tick by opSeq; validate fully before mutation; every drained op recorded | ADR-0007 | ✅ | 3 |
| TR-sim-019 | simulation-core.md | Only player/System requests use the FIFO; in-tick callers use sync I…Api in own phase | ADR-0003 | ✅ | 0 |
| TR-sim-020 | simulation-core.md | Reply {reqId,result,reason u16,extra ≤24 B} after handler events; reasons 1–63 Sim; no reqId dedupe | ADR-0007 | ✅ | 3 |
| TR-sim-021 | simulation-core.md | One preallocated per-tick EventLog (4096, reserve 256); blittable no-string entries | ADR-0008 | ✅ | 1 |
| TR-sim-022 | simulation-core.md | No sync subscribers; pull cursors ≤8; IEventSink.Deliver once in phase 13, copy-only | ADR-0008 | ✅ | 3 |
| TR-sim-023 | simulation-core.md | Replication classes Internal/Replicated/OwnerOnly/Targeted; undeclared kind fails test | ADR-0008 | ✅ | 1 |
| TR-sim-024 | simulation-core.md | Two-tier overflow admission; overflowInfo masks; adapter resyncs exactly those | ADR-0008 | ✅ | 1 |
| TR-sim-025 | simulation-core.md | EntityId uint32 class<<24\|counter; classes 01–08; 09–EF reserved; F0–FF tests; never reused | ADR-0004 | ✅ | 3 |
| TR-sim-026 | simulation-core.md | mailId separate uint32; PlayerSlot u8 0–7; slot freed in T reusable from T+1 | ADR-0004 | ✅ | 3 |
| TR-sim-027 | simulation-core.md | PCG32 XSH-RR; Derive via Mix64/Fnv1a64; all unchecked | ADR-0005 | ✅ | 1 |
| TR-sim-028 | simulation-core.md | Compile-time RngNames registry; v1 stream list; mail/mail.special/events indexed per shift | ADR-0005 | ✅ | 1 |
| TR-sim-029 | simulation-core.md | NextInt(b)=(next32*b)>>32, one draw per call; no float RNG; owner-only handles | ADR-0005 | ✅ | 3 |
| TR-sim-030 | simulation-core.md | Committed golden vectors (pcg32, Mix64, Fnv1a64, Derive) | ADR-0005 | ✅ | 1 |
| TR-sim-031 | simulation-core.md | Integer-only numerics (mm, permille, ticks, Cents, Fx16, BAM+Q15); float/double/decimal banned | ADR-0006 | ✅ | 3 |
| TR-sim-032 | simulation-core.md | Determinism checklist: no Dictionary iteration, stable sort, no Random/Guid/hash codes/time/threads/statics | ADR-0006 | ✅ | 4 |
| TR-sim-033 | simulation-core.md | T1: identical canonical dump hash per tick given same inputs and logs | ADR-0006 | ✅ | 1 |
| TR-sim-034 | simulation-core.md | T2: Windows+Linux x64 bit-identical carrier step, RNG, hashes, shared pure functions | ADR-0006 | ✅ | 3 |
| TR-sim-035 | simulation-core.md | Canonical dump via ISimStateContributor (fixed order, LE); SHA-256 in tests only | ADR-0006 | ✅ | 1 |
| TR-sim-036 | simulation-core.md | Tick() allocates 0 B after 300 warm-up ticks; blocking from M0 | ADR-0006 | ✅ | 1 |
| TR-sim-037 | simulation-core.md | Host tick p99 5.0 / max 8.0 / median 3.0 ms; ClientLayer p99 0.3 ms; advisory until reference machine | perf-budget.md (not an ADR) | ❌ GAP | 1 |
| TR-sim-038 | simulation-core.md | Per-phase p99 slices summing 4.50 ms + 0.50 reserve | perf-budget.md (not an ADR) | ❌ GAP | 1 |
| TR-sim-039 | simulation-core.md | CI perf gate: p99 rank, N≥18,000, fail at 2× calibrated baseline | perf-budget.md (not an ADR) | ❌ GAP | 1 |
| TR-sim-040 | simulation-core.md | Always-on checks to fixed record ring; asserts compile out of Release; SimCounters | ADR-0001 | ✅ | 2 |
| TR-sim-041 | simulation-core.md | Port interfaces (ISimPhase, IOpHandler, IEventSink, IHostClock, IMonotonicClock, IRngSource …) | ADR-0003 | ✅ | 2 |
| TR-sim-042 | simulation-core.md | Sim knows no peers/accounts/channels/RPC/wall clock/RTT/Godot types; request ticks in host domain | ADR-0011 | ✅ | 5 |
| TR-sim-043 | simulation-core.md | Join snapshot after Deliver of joinTick; joiner buffers later batches | ADR-0011 | ✅ | 0 |
| TR-sim-044 | simulation-core.md | Lifecycle Constructed→Ready→Running→Stopped/Faulted; RequestStop via IRunControl in phase 12 | ADR-0003 | ✅ | 1 |
| TR-sim-045 | simulation-core.md | Recorder stores ops + transform samples (in-memory M-1a, file M5) | ADR-0006 | ✅ | 2 |
| TR-content-001 | content-data-definitions.md | Sim content is JSONC under content/, one dir per def type; YAML and .tres rejected | ADR-0010 | ✅ | 1 |
| TR-content-002 | content-data-definitions.md | Generated content/manifest.txt drives loading; CI fails on drift | ADR-0010 | ✅ | 2 |
| TR-content-003 | content-data-definitions.md | Hand-written Utf8JsonReader walk; duplicate/unknown keys, exponents, -0 rejected | ADR-0010 | ✅ | 1 |
| TR-content-004 | content-data-definitions.md | Three data classes: Sim content (hashed), Presentation content, Player data (Persistence) | ADR-0010 | ✅ | 1 |
| TR-content-005 | content-data-definitions.md | Unit-suffix convention; decimals stored as ms/mm/permille; money integer cents | ADR-0010 | ✅ | 1 |
| TR-content-006 | content-data-definitions.md | Every numeric field declares a unit via code-side FieldSpec | ADR-0010 | ✅ | 2 |
| TR-content-007 | content-data-definitions.md | Def id regex; sorted ordinal into DefIndex ushort (0xFFFF none) | ADR-0004 | ✅ | 2 |
| TR-content-008 | content-data-definitions.md | DefIndex never persisted; on the wire only after hash match | ADR-0004 | ✅ | 1 |
| TR-content-009 | content-data-definitions.md | StringId = (namespace, DefIndex); run-time strings interned per run | ADR-0004 | ✅ | 1 |
| TR-content-010 | content-data-definitions.md | Defs immutable sealed record class in Sim/Content; no floats; no Godot Resource | ADR-0010 | ✅ | 1 |
| TR-content-011 | content-data-definitions.md | Defaults always neutral; weightClass required on ItemDef and MailKindDef | ADR-0010 | ✅ | 2 |
| TR-content-012 | content-data-definitions.md | Closed enums with explicit never-reused values; hand-written parse tables | ADR-0010 | ✅ | 2 |
| TR-content-013 | content-data-definitions.md | Balance keys in content/balance/<owner>.jsonc; BalanceKey registry; SimSettings in balance/sim.jsonc | ADR-0010 | ✅ | 3 |
| TR-content-014 | content-data-definitions.md | Stat closed enum with StatDef rows; design clamps are balance keys | ADR-0010 | ✅ | 1 |
| TR-content-015 | content-data-definitions.md | StatResolve F1 in Int128, one rounding, order-independent, k ≤ 6 | ADR-0010 | ✅ | 1 |
| TR-content-016 | content-data-definitions.md | IStatSheet read anywhere; IStatWriter only Perks and Stamps; meta never pushes modifiers | ADR-0010 | ✅ | 1 |
| TR-content-017 | content-data-definitions.md | Sheets are derived caches; team + per-slot sheets; source lists replicated | ADR-0010 | ✅ | 2 |
| TR-content-018 | content-data-definitions.md | RuleFlag enum; timed effects via transientAdd; sheet has no timers | ADR-0010 | ✅ | 3 |
| TR-content-019 | content-data-definitions.md | Explain() returns resolved/unclamped/sources; card numbers from placeholders | ADR-0010 | ✅ | 1 |
| TR-content-020 | content-data-definitions.md | 5-stage load pipeline → one immutable ContentSet passed into new SimWorld | ADR-0010 | ✅ | 2 |
| TR-content-021 | content-data-definitions.md | Loader reads via IContentSource; Sim never reads res:// | ADR-0010 | ✅ | 1 |
| TR-content-022 | content-data-definitions.md | Validator catalogue 84 active rules (CV-<AREA>-NN); errors block the build | ADR-0010 | ✅ | 5 |
| TR-content-023 | content-data-definitions.md | tools/ContentValidator console; flags, exit codes 0–3, fixture per rule | ADR-0010 | ✅ | 2 |
| TR-content-024 | content-data-definitions.md | ContentHash = SHA-256 over canonical encoding; encoder and managed SHA-256 both in the Sim, at load time (ADR-0010 §6, ADR-0006) | ADR-0010 | ✅ | 1 |
| TR-content-025 | content-data-definitions.md | Join handshake compares 32-byte hash with refusal precedence; content never transferred | ADR-0010 | ✅ | 2 |
| TR-content-026 | content-data-definitions.md | Debug hot reload (F6): errors keep old set; M0 reload-and-restart; in-place swap M4 | ADR-0010 | ✅ | 1 |
| TR-content-027 | content-data-definitions.md | Presentation ViewCatalog keyed by def id → DefIndex arrays; placeholder on missing view | ADR-0010 + ADR-0019 | ✅ | 0 |
| TR-content-028 | content-data-definitions.md | Boot parse+validate+hash ≤100 ms CPU; bind ≤25 ms; sheet rebuild ≤0.2 ms; Read 0 B | ADR-0010 | ✅ | 1 |
| TR-content-029 | content-data-definitions.md | No mod support; hash is a mismatch detector, not security | ADR-0010 | ✅ | 0 |
| TR-content-030 | content-data-definitions.md | MetaProfile/settings owned by Persistence; content ids as strings; unknown dropped + logged | ADR-0010 | ✅ | 0 |
| TR-inventory-001 | grid-inventory.md | All ops (player and automation requests) enter one FIFO drained in Inputs.Apply; loopback same path | ADR-0009 | ✅ | 7 |
| TR-inventory-002 | grid-inventory.md | Per-container uint32 version +1 per mutating op; batch bumps once per container | ADR-0009 | ✅ | 3 |
| TR-inventory-003 | grid-inventory.md | Entry: uint32 entryId, uint8 count, ascending mailIds[]; footprint w×h, rotation | ADR-0004 | ✅ | 2 |
| TR-inventory-004 | grid-inventory.md | StackKey=(mailKind,addressId,flags); effMax evaluated at op time; ≤255 | ADR-0009 | ✅ | 2 |
| TR-inventory-005 | grid-inventory.md | Take-k removes lowest mailIds (oldest-first); payment per mailId | ADR-0009 | ✅ | 5 |
| TR-inventory-006 | grid-inventory.md | Fixed validation precedence chain of reason codes | ADR-0009 | ✅ | 3 |
| TR-inventory-007 | grid-inventory.md | AutoPlace passes M/U/R; amortised O(1) non-full-stack index | ADR-0009 | ✅ | 3 |
| TR-inventory-008 | grid-inventory.md | Deterministic consolidating first-fit-decreasing sort, total-order comparator | ADR-0025 | ❌ GAP | 0 |
| TR-inventory-009 | grid-inventory.md | Intake sort ≤8,500 probes; memoised repeat O(1) | ADR-0025 | ❌ GAP | 0 |
| TR-inventory-010 | grid-inventory.md | Intake outfeed by arrivalSeq; PeekOutfeed/TakeUnit ≤4 entries amortised | ADR-0009 | ✅ | 1 |
| TR-inventory-011 | grid-inventory.md | Backlog drain ≤32 entries/tick, ≤8,000 probes, 0 when idle | ADR-0009 | ✅ | 1 |
| TR-inventory-012 | grid-inventory.md | O(1) conservation counters checked each tick; INVENTORY_ASSERT compiled out of Release | ADR-0009 | ✅ | 2 |
| TR-inventory-013 | grid-inventory.md | WorldItem cap 512, expiry FIFOs, lossless mail eviction | ADR-0009 | ✅ | 3 |
| TR-inventory-014 | grid-inventory.md | Timers as absolute ticks (bag 900, WorldItem 9000, water 300); expiry after Inputs.Apply | ADR-0009 | ✅ | 1 |
| TR-inventory-015 | grid-inventory.md | Integer-only weight→speed (permille, one division); weightPoints int16 | ADR-0006 | ✅ | 1 |
| TR-inventory-016 | grid-inventory.md | Inventory tick share ≤1.0 ms median / 2.0 p99 / 3.0 worst (advisory) | ADR-0025 | ❌ GAP | 0 |
| TR-inventory-017 | grid-inventory.md | Zero managed allocation on automation path | ADR-0006 | ✅ | 0 |
| TR-inventory-018 | grid-inventory.md | Managed heap ≤1 MB for containers + 512 WorldItems; 2,800-entry backlog ≤150 KB | ADR-0025 | ❌ GAP | 0 |
| TR-inventory-019 | grid-inventory.md | Reliable ordered delivery; events, replies, snapshots on one channel, event before reply | ADR-0009 | ✅ | 2 |
| TR-inventory-020 | grid-inventory.md | Intake/Depot non-openers get GroupCounts only; ≤40 B/tick/peer | ADR-0011 | ✅ | 0 |
| TR-inventory-021 | grid-inventory.md | Bursts ≤12 KB paced ≤800 B/tick; 24 KB high-water → resync-pending | ADR-0011 | ✅ | 0 |
| TR-inventory-022 | grid-inventory.md | Optimistic client: pending queue 16, temp ids, dependsOn, baseVersion, 5 s timeout | ADR-0009 | ✅ | 0 |
| TR-inventory-023 | grid-inventory.md | ContainerChecksum every 2 s, order-independent, never GetHashCode | ADR-0022 | ❌ GAP | 0 |
| TR-inventory-024 | grid-inventory.md | JoinState/RejoinState inventory block sizes; ≤64 KB chunks on bulk channel | ADR-0022 | ❌ GAP | 0 |
| TR-inventory-025 | grid-inventory.md | Sender from transport peer, never client playerId; malformed → bad_request, no exception | ADR-0007 | ✅ | 3 |
| TR-inventory-026 | grid-inventory.md | ContainerDef/ItemDef fields and StatSheet keys for inventory | ADR-0010 | ✅ | 1 |
| TR-inventory-027 | grid-inventory.md | Grid panel one custom-drawn Control; 0 B/frame; feedback via live-region Control | ADR-0019 | ✅ | 2 |
| TR-inventory-028 | grid-inventory.md | SHA-256 state dump equal after 50,000-op log on two Sim instances | ADR-0006 | ✅ | 2 |
| TR-input-001 | input-controls.md | Router core plain C#, fed abstract raw-input records + fake clock; Godot adapter is Presentation | ADR-0012 | ✅ | 0 |
| TR-input-002 | input-controls.md | All gameplay reads via router; direct Input.IsAction* outside adapter is a CI lint failure | ADR-0012 | ✅ | 0 |
| TR-input-003 | input-controls.md | RouterState tuple; live predicates as data; reachable-state golden list | ADR-0012 | ✅ | 0 |
| TR-input-004 | input-controls.md | Per-press state machine: latch at press, mute until release on state change, ordered edges | ADR-0012 | ✅ | 0 |
| TR-input-005 | input-controls.md | Action map: stable ids, grammar, predicate, defaults, flags, owner; alias table | ADR-0012 | ✅ | 0 |
| TR-input-006 | input-controls.md | Conflict validator over reachable states; most-specific chord wins | ADR-0012 | ✅ | 0 |
| TR-input-007 | input-controls.md | ui_* InputMap never mutated; game Controls FocusMode=None; router consumes in _Input | ADR-0012 | ✅ | 0 |
| TR-input-008 | input-controls.md | Events arrival-stamped in _Input (no InputEvent timestamp); edges attached to first catch-up tick | ADR-0012 | ✅ | 0 |
| TR-input-009 | input-controls.md | InputFrame Sim struct: tick, int16 move, absolute look, held bits, ordered edges, steps | ADR-0012 | ✅ | 0 |
| TR-input-010 | input-controls.md | Two clocks: Sim ticks for holds/range; monotonic integer ms for repeat/drag/glyph/aim | ADR-0012 | ✅ | 0 |
| TR-input-011 | input-controls.md | holdEff single shared int64 Sim function, 6-tick floor, holdCeil clamp | ADR-0014 | ✅ | 0 |
| TR-input-012 | input-controls.md | Hold cancel range 2900 mm with hysteresis = PC incumbentSlackMm | ADR-0014 | ✅ | 0 |
| TR-input-013 | input-controls.md | Server-validated verbs bypass the frame as reliable requests; no hold-duration field | ADR-0011 | ✅ | 1 |
| TR-input-014 | input-controls.md | MP pause is a local overlay, never SceneTree.Paused; Sim keeps ticking | ADR-0002 | ✅ | 0 |
| TR-input-015 | input-controls.md | KBM identified by DEVICE_ID_KEYBOARD/MOUSE; hot-plug via joy_connection_changed (UNVERIFIED) | ADR-0012 | ✅ | 0 |
| TR-input-016 | input-controls.md | ignore_joypad_on_unfocused_application=true; per-device axis cache | ADR-0012 | ✅ | 0 |
| TR-input-017 | input-controls.md | Deadzone/trigger/repeat/step caps in integer math; one step per rendered frame | ADR-0012 | ✅ | 0 |
| TR-input-018 | input-controls.md | Router+gatherer ≤0.1 ms/frame, ≤0.3 ms at 1000 Hz burst, 0 B steady | ADR-0012 | ✅ | 0 |
| TR-input-019 | input-controls.md | Preallocated 4096-entry event-log ring shared with UI event log | ADR-0012 | ✅ | 0 |
| TR-input-020 | input-controls.md | settings.input schema versioned, overrides-only; M-1a user://input.json | ADR-0012 | ✅ | 0 |
| TR-input-021 | input-controls.md | Replay corpus (200 seeded + 10 adversarial) asserts zero unauthored verbs | ADR-0006 | ✅ | 1 |
| TR-input-022 | input-controls.md | Focus loss: release-with-cancel, zero movement, mute, swallow recapture click, solo auto-pause | ADR-0012 | ✅ | 0 |
| TR-input-023 | input-controls.md | One-commit rule for deliver/deconstruct/throw; pendingCommitTimeout 500 ms | ADR-0012 | ✅ | 0 |
| TR-input-024 | input-controls.md | One local carrier per machine; all devices OR-ed; duplicate pad edges within 20 ms collapse | ADR-0012 | ✅ | 0 |
| TR-movement-001 | player-character-movement.md | Deterministic kinematic capsule solver in Sim; no Godot physics; no floating point | ADR-0013 | ✅ | 0 |
| TR-movement-002 | player-character-movement.md | mm int32, mm/s, permille, ticks; int64 before first multiply; one final division | ADR-0006 | ✅ | 0 |
| TR-movement-003 | player-character-movement.md | Client-authoritative movement; host authors Dead/Frozen bodies only | ADR-0020 | ❌ GAP | 0 |
| TR-movement-004 | player-character-movement.md | Tick-time inputs only via IRemoteCapsuleSource/ISupportTransformSource | ADR-0013 | ✅ | 0 |
| TR-movement-005 | player-character-movement.md | Fixed-point trig LUT; terrain from World Gen height field (2 m, bilinear, int64) | ADR-0013 | ✅ | 0 |
| TR-movement-006 | player-character-movement.md | Sub-steps ≤300 mm, ≤3 slides, step-up/snap 400 mm, slope 700‰, ≤4 sub-steps/tick | ADR-0013 | ✅ | 0 |
| TR-movement-007 | player-character-movement.md | Fixed-step driver with catch-up cap; local extrapolation ≤1 tick; no engine physics interpolation | ADR-0002 | ✅ | 0 |
| TR-movement-008 | player-character-movement.md | Transforms 16 B up, 17 B/entry batched down; 15 Hz/4 Hz; ≤19 kbps down, ≤6.7 kbps up | ADR-0020 | ❌ GAP | 0 |
| TR-movement-009 | player-character-movement.md | Teleport reliable with wrap-safe teleportSeq, ack/resend | ADR-0020 | ❌ GAP | 0 |
| TR-movement-010 | player-character-movement.md | Host range check via 9+3 tick history ring, claim at press, position_stale at 30 ticks | ADR-0020 | ❌ GAP | 0 |
| TR-movement-011 | player-character-movement.md | IInteractable registry with 2 m spatial hash; one verb per anchor; integer focus, 4-tick dwell | ADR-0014 | ✅ | 1 |
| TR-movement-012 | player-character-movement.md | Hold counter in owner Sim ticks; one request at held tick; no host hold timer | ADR-0014 | ✅ | 0 |
| TR-movement-013 | player-character-movement.md | Life states host-owned with absolute-tick timers | ADR-0014 | ✅ | 0 |
| TR-movement-014 | player-character-movement.md | Knockdown chain in phase 11 in fixed order, emits CarrierKnockedDown | ADR-0014 | ✅ | 0 |
| TR-movement-015 | player-character-movement.md | Frozen on disconnect 120 s; reconnect via account→peer map | ADR-0020 | ❌ GAP | 0 |
| TR-movement-016 | player-character-movement.md | PvP push: own capsule only, half overlap, cap 60 mm/tick | ADR-0013 | ✅ | 0 |
| TR-movement-017 | player-character-movement.md | Support carry ≤12,000 mm/s; roof position in vehicle-local frame | ADR-0013 | ✅ | 0 |
| TR-movement-018 | player-character-movement.md | Water depth from height field; swim hysteresis 900/700 mm | ADR-0013 | ✅ | 0 |
| TR-movement-019 | player-character-movement.md | ContentValidator enforces 12 cross-key movement rules | ADR-0010 | ✅ | 0 |
| TR-movement-020 | player-character-movement.md | Balance keys and StatSheet stats for movement; KitDef.standardIssueItem | ADR-0010 | ✅ | 0 |
| TR-movement-021 | player-character-movement.md | Owner CarrierStep+focus p99 ≤0.10 ms; host ≤0.20 ms/tick; 0 B/tick | perf-budget.md (not an ADR) | ❌ GAP | 0 |
| TR-movement-022 | player-character-movement.md | Golden per-tick transform hash equal on Windows and Linux; Sim references no Godot | ADR-0001 | ✅ | 0 |
| TR-movement-023 | player-character-movement.md | Remote interpolation 160 ms delay, 100 ms extrapolation cap, snap on teleport/6 m | ADR-0020 | ❌ GAP | 0 |
| TR-movement-024 | player-character-movement.md | Camera boom solved in C# against Sim height field/AABBs; dither fade | ADR-0029 | ❌ GAP | 0 |
| TR-movement-025 | player-character-movement.md | One AnimationTree per character, ≤4 layers, 48 characters ≤1.0 ms | ADR-0029 | ❌ GAP | 0 |
| TR-movement-026 | player-character-movement.md | animState uint8 bit layout; JoinState public block ≈30 B/player | ADR-0020 | ❌ GAP | 0 |
| TR-delivery-001 | manual-delivery.md | One Deliver IInteractable per destination at run start; priority 2; never deregistered | ADR-0014 | ✅ | 0 |
| TR-delivery-002 | manual-delivery.md | Ordered 6-reason availability; deliver.pending derived from pending queue | ADR-0014 | ✅ | 0 |
| TR-delivery-003 | manual-delivery.md | Deliver preview pure function every focused tick in ClientLayer Previews phase | ADR-0016 | ✅ | 0 |
| TR-delivery-004 | manual-delivery.md | DeliverPreviewTracker 6-tick settle counts Sim ticks | ADR-0016 | ✅ | 0 |
| TR-delivery-005 | manual-delivery.md | Per-tick client order: events/replies → focus → preview → resolve Interact edge | ADR-0016 | ✅ | 0 |
| TR-delivery-006 | manual-delivery.md | Press-tick preview recompute; hold latches (dest,branch,entry,holdTicks) | ADR-0016 | ✅ | 0 |
| TR-delivery-007 | manual-delivery.md | ShouldCancel/GetHoldTicks hooks; branch or selection change cancels | ADR-0014 | ✅ | 0 |
| TR-delivery-008 | manual-delivery.md | deliver_matching wire: MATCH omits selectedEntryId; envelope (kind u16, reqId u16) + mailboxId u32 + holdStartTick u32 + ClaimedPosition 12 B (P13 feet x,y,z mm) [+ selectedEntryId u32 on MISDELIVER_ONE] = 24 B MATCH / 28 B MISDELIVER_ONE packed, transport framing excluded; any other length -> bad_request | ADR-0011 | ✅ | 2 |
| TR-delivery-009 | manual-delivery.md | holdStartTick in host domain via IHostClock; host clamps window | ADR-0011 | ✅ | 3 |
| TR-delivery-010 | manual-delivery.md | Host check: mailboxId is a registered Deliver anchor; no throttle | ADR-0011 | ✅ | 0 |
| TR-delivery-011 | manual-delivery.md | Idempotent reqId u16; duplicate returns original reply | ADR-0007 | ✅ | 3 |
| TR-delivery-012 | manual-delivery.md | Client Evaluate/F10 equal host results over 1,000 seeded pairs | ADR-0016 | ✅ | 0 |
| TR-delivery-013 | manual-delivery.md | Preview money in integer cents, single RH per unit | ADR-0016 | ✅ | 0 |
| TR-delivery-014 | manual-delivery.md | Entry scan order cursor→hotbar→inventory→backpack equals host consumption | ADR-0016 | ✅ | 0 |
| TR-delivery-015 | manual-delivery.md | holdEff with Quick Hands insertPm 500; DeliverInsertPm clamp 0…750 | ADR-0014 | ✅ | 0 |
| TR-delivery-016 | manual-delivery.md | No host state, no replicated state; JoinState 0 B | ADR-0011 | ✅ | 0 |
| TR-delivery-017 | manual-delivery.md | Hand beat once per hold keyed (actor,dest,applyTick) from DeliveryOutcome | ADR-0016 | ✅ | 0 |
| TR-delivery-018 | manual-delivery.md | Reach echo starts only when Sim-published canStartHoldNext was true | ADR-0016 | ✅ | 0 |
| TR-delivery-019 | manual-delivery.md | Prompt content as data from Sim; HUD renders; localisation keys, no concatenation | ADR-0018 | ❌ GAP | 0 |
| TR-delivery-020 | manual-delivery.md | Live-region announcements policy; accessibility_live API unsourced | ADR-0018 | ❌ GAP | 0 |
| TR-delivery-021 | manual-delivery.md | Race suite: no misdeliver for MATCH-latched hold across ticks ±2 and RTTs; 10,000-seed soak | ADR-0006 | ✅ | 0 |
| TR-delivery-022 | manual-delivery.md | Telemetry latchedBranch; handShare ≥20% | ADR-0024 | ❌ GAP | 0 |
| TR-delivery-023 | manual-delivery.md | Mailbox flap separate node within 150-tri budget; reach layer scrubbed by hold progress | ADR-0019 | ✅ | 0 |
| TR-delivery-024 | manual-delivery.md | Visual-only lunge ≤0.8 m via rig offset, never feeds Sim | ADR-0019 | ✅ | 0 |
| TR-mail-001 | mail-destinations.md | All money/permille/tick math integer; int64 intermediates; one RH per formula | ADR-0006 | ✅ | 0 |
| TR-mail-002 | mail-destinations.md | Per-shift manifest pure function of (seed, shift, profile, addresses); stream mail | ADR-0005 | ✅ | 0 |
| TR-mail-003 | mail-destinations.md | Fixed RNG draw count; NextInt without rejection loop | ADR-0005 | ✅ | 0 |
| TR-mail-004 | mail-destinations.md | Separate mail.special stream so re-rolls never move Lost Parcels | ADR-0005 | ✅ | 0 |
| TR-mail-005 | mail-destinations.md | Evaluate pure, 0-alloc, same code compiled into client for prompt and arc colour | ADR-0016 | ✅ | 0 |
| TR-mail-006 | mail-destinations.md | Server-only SoA mail table keyed by mailId; packed AddressId | ADR-0017 | ✅ | 0 |
| TR-mail-007 | mail-destinations.md | Wallet via Mail-owned IWalletGate; no Mail→Economy assembly/namespace reference | ADR-0017 | ✅ | 0 |
| TR-mail-008 | mail-destinations.md | One consume routine for hand/throw/feeder/NPC in fixed tick order | ADR-0017 | ✅ | 0 |
| TR-mail-009 | mail-destinations.md | Backlog ordered queue O(1) append; drain owned by Inventory; backlogUnits u16 | ADR-0017 | ✅ | 0 |
| TR-mail-010 | mail-destinations.md | Complaint token buckets int64 ticks×permille, elapsed clamped | ADR-0017 | ✅ | 0 |
| TR-mail-011 | mail-destinations.md | Feeder throttle integer credit; stagger feederId mod 15 | ADR-0017 | ✅ | 0 |
| TR-mail-012 | mail-destinations.md | No wall-clock reads; pause freezes buckets/windows/credits | ADR-0006 | ✅ | 0 |
| TR-mail-013 | mail-destinations.md | Closing grace by action start tick (host domain); closeGraceTicks 90 | ADR-0017 | ✅ | 0 |
| TR-mail-014 | mail-destinations.md | DeliveriesState reliable-ordered event, not on 5 Hz snapshot | ADR-0011 | ✅ | 1 |
| TR-mail-015 | mail-destinations.md | DeliveryOutcome one event per op ≤19 B, canonical encoding; ≤16 B/tick typical | ADR-0008 | ✅ | 0 |
| TR-mail-016 | mail-destinations.md | Value hint ≤5 runs per entry; client recomputes payout | ADR-0011 | ✅ | 0 |
| TR-mail-017 | mail-destinations.md | JoinState ≤72 B; outcomeSeq drop rule on rejoin | ADR-0011 | ✅ | 0 |
| TR-mail-018 | mail-destinations.md | Mail content defs loaded and validated | ADR-0010 | ✅ | 0 |
| TR-mail-019 | mail-destinations.md | Mail ≤1.0 ms p99 / 2 ms max; manifest ≤0.5 ms; 0 alloc hot paths (advisory) | ADR-0017 | ✅ | 0 |
| TR-mail-020 | mail-destinations.md | Idle tick touches only feeder credits; scans no rows | ADR-0017 | ✅ | 0 |
| TR-mail-021 | mail-destinations.md | MailShiftStats ≤2 KB at Payday, paced ≤800 B/tick | ADR-0011 | ✅ | 0 |
| TR-mail-022 | mail-destinations.md | Presentation caps: residents, envelope pool, flashes, audio voices | ADR-0019 | ✅ | 0 |
| TR-mail-023 | mail-destinations.md | Host-authoritative payment/complaint; client F10 display-only | ADR-0011 | ✅ | 0 |
| TR-throwing-001 | throwing-catching.md | Host-authoritative closed-form integer ballistic; O(1) p(k) | ADR-0015 | ✅ | 0 |
| TR-throwing-002 | throwing-catching.md | No RNG in throwing; future randomness via registered stream by launch ordinal | ADR-0005 | ✅ | 0 |
| TR-throwing-003 | throwing-catching.md | Preview calls same Sim function as host; identical over 10,000 throws | ADR-0015 | ✅ | 0 |
| TR-throwing-004 | throwing-catching.md | Floor-based RH, CeilDiv/FloorDiv, isqrt; never truncating / on signed values | ADR-0006 | ✅ | 1 |
| TR-throwing-005 | throwing-catching.md | Swept segment test per tick per flight (≤90 ticks); slab/circle/cylinder/sphere/heightfield | ADR-0015 | ✅ | 0 |
| TR-throwing-006 | throwing-catching.md | Receiver resolution ordering; flights in creation order | ADR-0015 | ✅ | 0 |
| TR-throwing-007 | throwing-catching.md | Broad phase bounds \|R\|≤65536 mm; static 4 m grid | ADR-0015 | ✅ | 0 |
| TR-throwing-008 | throwing-catching.md | Throwing.Step phase 4; 0.15 ms p99; 0 alloc; 16 flights + 8 catchers | ADR-0015 | ✅ | 0 |
| TR-throwing-009 | throwing-catching.md | FlightRecord snapshot fields; flightId wrap-safe u16 | ADR-0004 | ✅ | 0 |
| TR-throwing-010 | throwing-catching.md | Fixed 9-check host validation; claim-and-corroborate origin | ADR-0021 | ❌ GAP | 0 |
| TR-throwing-011 | throwing-catching.md | Leaky-bucket cooldown; team cap ≤48 flights/s | ADR-0021 | ❌ GAP | 0 |
| TR-throwing-012 | throwing-catching.md | Catch arm window host-tick based; contact over 9-tick history | ADR-0021 | ❌ GAP | 0 |
| TR-throwing-013 | throwing-catching.md | Exactly one terminal state per flight; conservation through flush/disconnect | ADR-0015 | ✅ | 0 |
| TR-throwing-014 | throwing-catching.md | Message sizes ThrowRelease 25 B, ThrowLaunched 30 B, FlightResolved 18 B, CatchArm 1 B | ADR-0021 | ❌ GAP | 0 |
| TR-throwing-015 | throwing-catching.md | ≈66 B per flight per peer; budget pressure at cap | ADR-0021 | ❌ GAP | 0 |
| TR-throwing-016 | throwing-catching.md | Flights 0 B JoinState; remotes render at hostTickEst−3, fast-forward via F2 | ADR-0016 | ✅ | 0 |
| TR-throwing-017 | throwing-catching.md | Aim preview ≤0.3 ms/frame, 0 alloc, screen-space dashed ribbon | ADR-0019 | ✅ | 0 |
| TR-throwing-018 | throwing-catching.md | Launch ghost on release frame; outcome beats never before domain event | ADR-0016 | ✅ | 0 |
| TR-throwing-019 | throwing-catching.md | ThrowDef/BalanceConfig knobs with validator rules | ADR-0010 | ✅ | 0 |
| TR-throwing-020 | throwing-catching.md | Telemetry to session stats file only | ADR-0024 | ❌ GAP | 0 |
| TR-throwing-021 | throwing-catching.md | Spin from flightId; MultiMesh bundles; hidden beyond 35 m | ADR-0019 | ✅ | 0 |
| TR-hud-001 | hud-notifications.md | HUD Presentation-only; no gameplay state; Sim has no reference to it | ADR-0018 | ❌ GAP | 0 |
| TR-hud-002 | hud-notifications.md | Plain-C# UI core xUnit-testable via IUiClock/IRoster/IHudState/IMoneyFormatter + Godot view | ADR-0018 | ❌ GAP | 0 |
| TR-hud-003 | hud-notifications.md | HUD reads only via IHudState ordered by (sourceTick, opId); loopback from M-1a | ADR-0016 | ✅ | 0 |
| TR-hud-004 | hud-notifications.md | Timer from IHostClock.renderSimTimeMs; HUD runs no clock | ADR-0016 | ✅ | 0 |
| TR-hud-005 | hud-notifications.md | Timer monotone within (phaseId, deadlineTick); text written on change only | ADR-0018 | ❌ GAP | 0 |
| TR-hud-006 | hud-notifications.md | Every notification is a cue record with id/int args, never preformatted strings | ADR-0018 | ❌ GAP | 0 |
| TR-hud-007 | hud-notifications.md | Cue table validated at load (CV-HUD-01..05); missing key is load error | ADR-0010 | ✅ | 0 |
| TR-hud-008 | hud-notifications.md | CI: every pill/toast cue has a paired world cue | ADR-0018 | ❌ GAP | 0 |
| TR-hud-009 | hud-notifications.md | Toast lane: M-1a 1 slot; M0+ 2 visible + 4 queued, deterministic eviction | ADR-0018 | ❌ GAP | 0 |
| TR-hud-010 | hud-notifications.md | Merge predicate on UI-clock windows; raw events always logged | ADR-0018 | ❌ GAP | 0 |
| TR-hud-011 | hud-notifications.md | UI event log ring 1024 (M0), plain list M-1a; shared records | ADR-0018 | ❌ GAP | 0 |
| TR-hud-012 | hud-notifications.md | All money text via one IMoneyFormatter; lint bans currency literals | ADR-0018 | ❌ GAP | 0 |
| TR-hud-013 | hud-notifications.md | Screen-space MarkerLayer above post-processing; 41 markers at roster 8 | ADR-0018 | ❌ GAP | 0 |
| TR-hud-014 | hud-notifications.md | Flash governor ≤3/s real clock, ≤2 Hz per element; one animator | ADR-0018 | ❌ GAP | 0 |
| TR-hud-015 | hud-notifications.md | 1080-reference px scaled by height; text ≥18 px up to 200%; min 1280×720; stretch undecided | ADR-0018 | ❌ GAP | 0 |
| TR-hud-016 | hud-notifications.md | 0 alloc over 10,000 idle frames, pooled rows, atlas, no DrawLine; ≤0.8 ms, ≤60 draws (proposed) | ADR-0018 | ❌ GAP | 0 |
| TR-hud-017 | hud-notifications.md | Two live-region Controls; schema M-1a, announcer M4 | ADR-0018 | ❌ GAP | 0 |
| TR-hud-018 | hud-notifications.md | History ring 50 raw events with snapshots | ADR-0018 | ❌ GAP | 0 |
| TR-hud-019 | hud-notifications.md | Settings consumed from Persistence; HUD owns no persistence | ADR-0024 | ❌ GAP | 0 |
| TR-hud-020 | hud-notifications.md | hud.tuning data, hot-reload in debug; cue table in shared validator library | ADR-0010 | ✅ | 0 |
| TR-hud-021 | hud-notifications.md | Deterministic reading-time lifetime F1 incl. no-space scripts | ADR-0018 | ❌ GAP | 0 |
| TR-comms-001 | comms.md | Dedicated reliable-ordered channel 3 (channel count fixed at connect; spike) | ADR-0011 | ✅ | 0 |
| TR-comms-002 | comms.md | ChatMessage/Ping/Emote/PingPurge families; host-originated relay | ADR-0011 | ✅ | 0 |
| TR-comms-003 | comms.md | Host derives senderSlot from RPC peer; shape-only validation; drop not kick | ADR-0011 | ✅ | 0 |
| TR-comms-004 | comms.md | ≤10 B/tick/peer sustained; 32 KB high-water drop-oldest; commsSeq gaps | ADR-0011 | ✅ | 0 |
| TR-comms-005 | comms.md | Host resolves ping cap/refresh deterministically; emits pingId + expiresTick | ADR-0011 | ✅ | 0 |
| TR-comms-006 | comms.md | JoinState replays chat by byte budget + ≤16 pings; buffer above snapshot | ADR-0011 | ✅ | 0 |
| TR-comms-007 | comms.md | Ingress sanitiser (NFC, strip controls/bidi, grapheme caps) with fixed test vectors | ADR-0018 | ❌ GAP | 0 |
| TR-comms-008 | comms.md | Local per-player mute by account id; TypingState on ch3 | ADR-0018 | ❌ GAP | 0 |

## Known Gaps

| Planned ADR | TR-IDs | Suggested action |
|---|---|---|
| ADR-0018 | TR-delivery-019, TR-delivery-020, TR-hud-001, TR-hud-002, TR-hud-005, TR-hud-006, TR-hud-008, TR-hud-009, TR-hud-010, TR-hud-011, TR-hud-012, TR-hud-013, TR-hud-014, TR-hud-015, TR-hud-016, TR-hud-017, TR-hud-018, TR-hud-021, TR-comms-007, TR-comms-008 | `/architecture-decision HUD composition` |
| ADR-0020 | TR-movement-003, TR-movement-008, TR-movement-009, TR-movement-010, TR-movement-015, TR-movement-023, TR-movement-026 | `/architecture-decision Transforms & Teleport` |
| ADR-0021 | TR-throwing-010, TR-throwing-011, TR-throwing-012, TR-throwing-014, TR-throwing-015 | `/architecture-decision Flight protocol & catch` |
| ADR-0022 | TR-inventory-023, TR-inventory-024 | `/architecture-decision JoinState & resync` |
| ADR-0024 | TR-delivery-022, TR-throwing-020, TR-hud-019 | `/architecture-decision Persistence` |
| ADR-0025 | TR-inventory-008, TR-inventory-009, TR-inventory-016, TR-inventory-018 | `/architecture-decision Inventory indexing` |
| ADR-0029 | TR-movement-024, TR-movement-025 | `/architecture-decision Animation & camera` |
| perf-budget.md (not an ADR) | TR-sim-037, TR-sim-038, TR-sim-039, TR-movement-021 | `author docs/architecture/perf-budget.md` |

## Superseded Requirements

- **TR-delivery-008** (revised 2026-10-16): the GDD's 12/16 B packed layout is superseded by the ADR-0011 erratum (24 B MATCH / 28 B MISDELIVER_ONE, with the claimed position). manual-delivery.md Network table and M-NET-1 await GDD sync.
