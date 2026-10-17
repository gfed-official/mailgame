# Story 017: `FlightMirror`, the flight render timeline (`hostTick − 3`, fast-forward by F2) and the launch-ghost reconciliation model

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Presentation (Godot-free: `Sim/Throwing/Client` and `Client.Core/Throwing`)
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Presentation data story (1 of 4).** Everything the flight view and the launch ghost need that is *not* Godot: which flights exist, where each one is drawn this frame, and how the thrower's instant local parcel reconciles with the host's authoritative one. The views (stories 020, 021) only place meshes at the numbers produced here.

## Context

**GDD**: `design/gdd/throwing-catching.md` (Game Feel "Release → item leaves the hand", "Thrower's timeline", "Remote peers"; Network "Timing", "Join / rejoin"; Visual/Audio "Flight (every peer)" spin and trail rules; Tuning Knobs `flightRenderDelayTicks` / `ghostBlendMs` / `ghostSnapMm` / `ghostHoldMaxMs`)
**Requirement**: `TR-throwing-018` (launch ghost on the release frame; outcome beats never before the domain event), `TR-throwing-016` (flights 0 B JoinState; remotes render at `hostTickEst − 3`, fast-forward via F2 — the **client timeline** half; the remote protocol and rejoin are M0), `TR-throwing-021` (spin from `flightId` — the pure function half)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F2-1 b, TC-N-4 (ghost half, on a virtual clock), TC-FE-2 (data half).)*

**ADR Governing Implementation**: ADR-0016: ClientLayer Previews and Presentation Timing (§1 `FlightMirror`, §5 "Launch ghost: cosmetic … lerp over 3 ticks, blend, snap on `originClamped`, hold ≤ 600 ms. It never produces a landing beat; only `FlightResolved` does", §6 timeline registry "Flights: `HostTick − flightRenderDelayTicks` (3) + alpha; a late `ThrowLaunched` is fast-forwarded by `Ballistic.PositionAt(age)`; the own ghost leads") — Accepted 2026-10-16 (primary); ADR-0015 (§9 events, F2) and ADR-0005 §3 (flight spin is a pure function of `flightId`) secondary — Accepted
**ADR Decision Summary**: All presentation time comes from `IRenderClock`. Flights (all, including the thrower's own after reconcile) render at `HostTick − 3` plus alpha; a late `ThrowLaunched` is fast-forwarded by evaluating F2 at the current age. The thrower's local ghost leads the authoritative flight and reconciles to `ThrowLaunched` by blending in time, snapping when the path differs, and holding at a static contact until the outcome event. The ghost is cosmetic and never plays a landing beat. `FlightMirror` is rebuilt only from events: a joiner does not see flights already in the air.
**ADR Version**: ADR-0016 2026-10-16; ADR-0015 2026-10-16; ADR-0005 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code; floats allowed here — Presentation layer) | **Risk**: LOW
**Engine Notes**: None. No Godot API; the views that consume this are stories 020 and 021.

**Control Manifest Rules (Presentation + ClientLayer)**:
- Required: presentation time comes from `IRenderClock`; `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned for gameplay-facing timing; outcome beats play only from `PresentationEventStream`; flight spin is a pure function of `flightId` (ADR-0016, ADR-0005 §3)
- Required: the mirror calls `Ballistic.PositionAt`; no second ballistic anywhere (ADR-0015)
- Forbidden: the ghost producing a landing beat; any Presentation value fed back into the Sim; id structs through Godot signals
- Guardrail: 0 B per frame; ≤ 160 mirrored flights (the host cap)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (Game Feel, Network "Timing", TC-F2-1 b, TC-N-4, TC-FE-2) and ADR-0016 §5–§6, scoped to this story:*

- [ ] **Mirror from events.** `FlightMirror` (an `IMirrorPart` bound for `ThrowLaunched` and `FlightResolved` through `BindMirrorPart`) adds a flight on `ThrowLaunched` and marks it resolved on `FlightResolved` (keeping it until its visual has landed); a `FlightResolved` for an unknown `FlightId` is ignored for the arc (a rejoiner draws no arc) without error; a duplicate `FlightResolved` is idempotent.
- [ ] **Timeline.** At host tick `H` and `AlphaPermille` a, a flight launched at `L` renders at age `H − 3 − L` plus a/1000 of a tick: the view receives the integer segment `(p(k), p(k+1))` and a, with `k` clamped to `[0, resolvedAge]`; before `renderTick < L` it is hidden (OQ16's hold-at-age-0 is deferred); after reaching its resolution age it reports `Landed` with the `FlightResolved.contact` point, not a `p(k)` beyond it.
- [ ] **TC-F2-1 b / fast-forward.** A `ThrowLaunched` event that arrives 6 ticks late renders at the correct current age on the same frame (one `PositionAt` call, no replay); for 1 000 random flights the mirror's position at every integer age equals the host's segment endpoint from `ThrowLaunched` integers alone.
- [ ] **Spin.** `Spin(flightId, bundle) → angle` is a pure function of `flightId` and elapsed render ticks (singles 2 rev/s, bundles 1 rev/s; stops at contact); two peers' values are equal; no RNG type is referenced.
- [ ] **Ghost spawn (data half of TC-FE-2).** A `ReleaseRecord` (story 007) with release frame `f` yields a ghost whose spawn is stamped with the same render frame/ms and position = the hand socket supplied by the view; over a 200-release replay corpus (`tests/replay/throwing/`, mouse and pad) the spawn frame minus the release frame is ≤ 1 frame for every release (virtual clock).
- [ ] **Ghost path.** The ghost lerps from the hand socket onto the F1 predicted path over its first 3 ticks (a socket ≤ 400 mm from the F1 origin closes to 0 by tick 3), then follows `Ballistic.PositionAt` of the locally predicted launch on **time since release**, so it leads the authoritative flight by ≈ one-way + 100 ms: for a light level-ish throw the lead distance is 1.8 / 2.2 / 2.7 m (±0.1 m) at 60 / 120 / 200 ms RTT (virtual clock, synthetic one-way delays).
- [ ] **Reconcile.** On the matching `ThrowLaunched` (by `seq`) the ghost blends into the authoritative timeline over `max(100, 1.5 × oneWay)` ms; it **snaps** (no blend) when `originClamped` is set or when the event's origin/velocity differ from the local prediction by any mm; after the blend the ghost despawns and the flight view owns the parcel.
- [ ] **Hold and fade.** If the ghost reaches a predicted static contact before the outcome event it **holds at the contact point without settling** until the matching `FlightResolved` (≤ `ghostHoldMaxMs` = 600 ms, then fades over 150 ms); on a rejected release (`OpReply` reason) it fades over 150 ms where it is. TC-N-4's "a ghost at a static contact holds ≤ 600 ms for its outcome event" passes on a virtual clock with a delayed event stream (3–10 tick delays through the loopback `DelayTransport` harness of `production/epics/manual-delivery/story-008-race-suite-bell-fuzz-soak.md` when available).
- [ ] **Never a beat.** The ghost and mirror types are not tagged `[OutcomeBeat]` and expose no landing callback; an architecture test asserts that `[OutcomeBeat]` types receive data only from `PresentationEventStream` cursors (ADR-0016 enforcement test) and that none of this story's types are such sinks.
- [ ] **Trail verdict.** The mirror records the **launch verdict** (class at release from `ThrowLaunched` visual data on remote peers, from `ReleaseRecord.ClassAtRelease` locally) per flight and never changes it afterwards (a box that closes mid-flight does not recolour the trail; TC-VA-1's Sim half).
- [ ] 0 B per frame over 600 frames with 60 mirrored flights.

---

## Implementation Notes

*Derived from ADR-0016 §5–§6 and GDD Game Feel / Network:*

- Files: `src/PerformativeMail.Sim/Throwing/Client/FlightMirror.cs` (+ `IFlightRead { int Count; ref readonly FlightRenderState this[int]; }`), `FlightTimeline.cs` (static, ints), `src/PerformativeMail.Client.Core/Throwing/LaunchGhost.cs` (state machine, `Update(long renderMs, ...)`), `GhostSettings.cs` (`ghostBlendMs` 100, `ghostSnapMm` max(1000, 1.5 × oneWay × speed), `ghostHoldMaxMs` 600, `flightRenderDelayTicks` 3 — **data**, via the `throw.*` balance rows; content-data story 008 registers `previewSettleMs`, not necessarily the ghost knobs: add the rows additively and log), `FlightSpin.cs`.
- Visual verdict on remote peers: `ThrowLaunched` carries no class in the ADR-0015 §9 payload (the GDD's `visual` = tier, hop, verdict, `originClamped`). **Open point (flagged for ADR-0021 / M0):** at M-1a, with only the host's own client, the verdict comes from `ReleaseRecord`; for remote peers the verdict must be derivable — either add it to `ThrowLaunched.flags` bits 1–3 (an amendment Story 006 reserved bits for) or recompute the class client-side from static geometry + `Evaluate` at the launch tick. Do not decide for M0 here; implement the local path and a `LaunchVerdict` field that remote peers will fill.
- `oneWay` comes from the Networking estimator (`IHostClock`/adapter RTT); loopback reports 0, so blend = 100 ms and lead = 100 ms × speed. The 60/120/200 ms vectors use a test clock.
- Hand socket position and bundle mesh tier come from the view/`ItemDef`; this story takes them as arguments. Tier (1.0/1.15/1.35/1.6×) is Later; the model carries `Units` so the Later view can pick a tier.
- `IRenderClock` (`production/epics/throwing-catching/story-005-preview-label-model-f7-money-and-settle.md` adds it if absent; ADR-0016 §6) provides `HostTick`, `AlphaPermille`, `RenderSimTimeMs`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 020: the MultiMesh flight view, ×N tag, blob shadow, trail drawing. Story 021: ghost mesh, release snap pose, landing beats and sfx.
- Remote peers' wire timeline, clock sync, hold-at-age-0, fractional-tick chord lerp (M0, ADR-0021, GDD OQ16). Join/rejoin behaviour (0 B).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/FlightMirrorTests.cs`, `tests/unit/throwing/LaunchGhostTests.cs` (`[Trait("Id", "TC-F2-1b")]`, `TC-N-4`, `TC-FE-2`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 006, 007, 010, 011; `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`IHostClock`); `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md`; `production/epics/manual-delivery/story-006-reply-verdict-hand-beat-keys.md` (`PresentationEventStream` / `[OutcomeBeat]`, added there if absent)
- Unlocks: Stories 016, 020, 021
