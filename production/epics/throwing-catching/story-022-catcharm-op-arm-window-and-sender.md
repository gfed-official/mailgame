# Story 022: `CatchArm` — OpKind, host arm window (`armedUntil`), eligibility clears and the client `ICatchArmSender` (Sim-side catch, part 1)

> **Epic**: Throwing & Catching (M-1a slice) — **Sim-side catch pull-forward (optional for the M-1a gate)**
> **Status**: Deferred (Evan 2026-10-23 7:13 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; was Ready)
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **DEFERRED 2026-10-23 by Evan, not deleted.** Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example". Reason: the Sim-side catch is optional for the M-1a gate (EPIC.md l.32; solo has no catcher) and the GDD tags T7 as M0; Evan 7:13 deferred 022–024. Re-run `/story-readiness` when M0 planning starts; the text below is unchanged from the last Ready/Blocked version.

> **Catch pull-forward (1 of 3, numbered last by design).** T7 catching is an **M0** feature in the GDD; the orchestrator pulled the *Sim-side* arm/validation/`TryCatch` forward. Solo has no catcher, so nothing in stories 001–021 depends on this one. It is fully testable headless with simulated carriers. The catch **prompt** (`CatchPredictor`, carets, edge chevron), **Toggle** catch, the **bonk**, **relay hops** and the **network protocol** stay M0 (see EPIC.md deferred list).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T7 "Arming", "Who may catch"; States and Transitions "Catch arm (host, per player)"; F5 `armed (host)`; Edge Cases "If `CatchArm{down}` arrives after the flight resolved", "If an armed catcher starts aiming")
**Requirement**: `TR-throwing-012` (catch arm window is host-tick based; contact over the 9-tick history — the **arm window** half here, contact is story 023; registry maps this to ADR-0021, coverage gap, and ADR-0015 §7 fixes the Sim-side rule), `TR-throwing-010` (host validation of the request), `TR-throwing-011` (edge rate cap)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T7-1 a/b/c (M0-tagged; Sim half pulled forward), TC-N-2 a (edge cap, Sim half).)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§7 "Catch (host side of F5; Sim-side pull-forward)": `CatchArm{down|up}` = OpKind `0x0141` applied in phase 0; `armedUntil = min(max(upApplyTick, downApplyTick + catchTapTicks (15)), downApplyTick + 90)`; armed at `t` iff `downApplyTick ≤ t < armedUntil`; eligibility and clears read from state at the start of phase 4) — Accepted 2026-10-16 (primary); ADR-0014 §3 (press resolution; the framework asks Throwing to send `CatchArm`) and ADR-0007 secondary — Accepted
**ADR Decision Summary**: The host never times a hold, but it does own the arm window: `CatchArm{down}` and `{up}` are requests applied in phase 0; the window is computed in host ticks from their apply ticks, so a tap of ≤ 200 ms arms exactly as long as a hold (min 15 ticks) and no arm outlives 90 ticks. Death, mount, swim, panel open, aim start, disconnect and rejoin clear the arm, read from state at the start of phase 4 (no upward calls). The protocol under latency is ADR-0021's (M0).
**ADR Version**: ADR-0015 2026-10-16; ADR-0014 2026-10-16; ADR-0007 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Payload is one explicit `byte`; no `bool`.

**Control Manifest Rules (Feature layer)**:
- Required: arm state is Throwing's, in the dump; windows are absolute host ticks; ops validate before mutating and return exactly one `Reply` (ADR-0007, ADR-0006 §4)
- Required: the client sends `CatchArm` edges at press and release only, through `ICatchArmSender` (defined by Player Character story 016/017); no per-flight catch request
- Forbidden: the host timing a hold; a catch request sent when the catcher *sees* contact; rewinding a resolved flight (GDD F5 "No rewind")
- Guardrail: handler O(1), 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T7-1, TC-N-2 a) and ADR-0015 §7, scoped to the arm window:*

- [ ] **Op declaration.** `OpKind.CatchArm == 0x0141` with a 1-byte payload (`down u8`: 1 = down, 0 = up; any other value → `bad_request`), actor-bound, bound to its handler at phase 0; the codec `kind u16 · reqId u16 · down u8` is registered in the adapter registry (round-trips, wrong length → `bad_request`, 10 000-input fuzz via `production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md` by registration). M-1a baseline 5 B on loopback; the GDD's 1 B wire is the M0 narrowing.
- [ ] **TC-T7-1 a (window arithmetic).** `down` applied at tick 100: `up` at 106 → armed at 114 and not at 115 (`armedUntil` 115); `up` at 130 → armed at 129 and not at 130; no `up` → armed at 189 and not at 190 (the 3 s hold cap). Armed at tick `t` iff `downApplyTick ≤ t < armedUntil`; a tap shorter than 15 ticks arms for exactly 15.
- [ ] **TC-T7-1 b (Toggle arithmetic).** `down` at 100 with no second press → armed at 189, not 190; a second press `up` at 120 → `armedUntil` = max(120, 115) = 120. (The Toggle *translation* of presses to edges is the router's, M0; the host rule is identical.)
- [ ] **TC-T7-1 c (parametrised clears).** Death, mounting, swimming, panel open, aim start, disconnect and rejoin each clear an active arm: eligibility is read once at the start of the phase-4 step through the `ICatcherEligibility` port; a cleared catcher is not a receiver from that tick and the arm does not revive when the condition ends.
- [ ] **Eligibility on arming.** A `down` from a player who is not eligible (swimming, Dead, Frozen, mounted, aiming, panel open) does not arm (counted by cause: `armIneligible`), still replies `ok`; the thrower of a flight is excluded per-flight in story 023.
- [ ] **Edge rate cap (TC-N-2 a, Sim half).** More than 4 edges per second per player (a sliding window of 30 ticks) are dropped: not applied, counted (`armEdgesDropped`), reply `ok`; a normal press/release pair is never dropped.
- [ ] **Forward-only.** A `down` applied after a flight resolved changes nothing about that flight; the arm may catch a *different* flight within its 15 ticks (asserted with story 023).
- [ ] **Duplicate edges.** A second `down` while armed does not extend or restart the window; an `up` without an active arm is a no-op; an `up` earlier than the matching `down` apply tick cannot occur (op FIFO order), and a stale `up` never shortens below the 15-tick minimum.
- [ ] **State and dump.** Per slot `downTick` and `armedUntil` live in the Throwing dump's reserved block (story 008); changing either changes the hash; `PlayerLeave`/rejoin zero them.
- [ ] **Client sender.** `CatchArmSender : ICatchArmSender` (the port from `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md`) builds the op and sends through `IRequestSink.TrySend`; `Send(true)` on the press, `Send(false)` on the release/cancel; returns false (nothing sent, edge not lost from the PC channel's point of view) when the sink reports busy; Player Character story 017's Catch channel tests pass against the real sender instead of its double.
- [ ] 100 000 handler calls allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0015 §7 and the GDD T7 / F5 text:*

- Files in `src/PerformativeMail.Sim/Throwing/Catch/`: `CatchArmHandler.cs`, `ArmState.cs` (SoA per `PlayerSlot`), `ArmWindow.cs` (pure `ArmedUntil(down, up?, tapTicks, capTicks)`), `ICatcherEligibility.cs`; `src/PerformativeMail.Sim/Throwing/Client/CatchArmSender.cs`; the codec in `src/PerformativeMail.Client.Core/Net/Codecs/ThrowingCodecs.cs` (extends story 006's file).
- `catchTapTicks` = `catchTapMs` 500 → 15 and the 90-tick cap come from `ThrowBalance` (content-data story 008 registers `catchTapMs`; CV-THR-12 requires a whole-tick multiple); convert once at build with the exact-tick helper (`production/epics/simulation-core/story-006-tickrate-f1-time-conversions.md`).
- **`ICatcherEligibility` (Throwing-defined, read-only):** `bool IsEligible(PlayerSlot s)` = Alive ∧ on foot ∧ in World or Holding-closed ∧ ¬swimming ∧ ¬aiming ∧ ¬mounted. At M-1a only the first, second and fourth conjuncts have a host source (Players' life state and the stored sample's swimming bit, story 009 adds `IsSwimming`). **Unowned seam (EPIC.md):** the host has no source for *panel open* or *aiming* — those are replicated carrier flags (`animState` bits, ADR-0020/0021, M0). Until then the port's production adapter treats them as false and the tests use doubles; this is why the pull-forward is "optional for the M-1a gate". Do not invent a client-to-host aim notification.
- Rejoin and disconnect clear through the same slot hooks as story 008's `Tat` clear.
- The PC Catch **channel** (press → `Send(true)`, release → `Send(false)`, cancel on context change) is Player Character story 017; this story only supplies the sender and the host.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 023: contact, `TryCatch`, CAUGHT/FUMBLE, one-press-clears-arm. Story 024: end-to-end with real transform samples (Blocked).
- The Interact resolution order and D3 gate (`production/epics/player-character/story-016-press-resolution-tap-catch-armed.md`). `CatchPredictor`/prompt/carets/edge chevron (M0). Toggle translation in the router (M0). Network protocol and channel for `CatchArm` (ADR-0021, M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/CatchArmTests.cs` (`[Trait("Id", "TC-T7-1")]`, `TC-N-2a`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 008 (state, dump block); `production/epics/simulation-core/story-013-op-reply-types-descriptors.md`; `production/epics/network-adapter-loopback/story-002-op-codec-registry-envelope-fuzz.md`; `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md` and `story-017-hold-counter-cancels-channel-focus-latch.md` (`ICatchArmSender`, Catch channel); `production/epics/simulation-core/story-022-playerjoin-playerleave-handlers.md`
- Unlocks: Story 023
