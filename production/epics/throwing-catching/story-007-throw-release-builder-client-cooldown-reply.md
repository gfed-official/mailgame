# Story 007: `ThrowReleaseBuilder` — the release request from the last drawn preview, the client cooldown hold and the reply part

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Owner-side story (3 of 3).** T4 "Release". The Throw action is an *Immediate* verb: the input router commits it on the event frame and calls this story's sink before `RunFrame`, so the request enters the op ring in the same frame. **Input-controls should depend on this story** (and on story 004's `ThrowPreviewState`) for the Throw verb's request builder and the commit hand-over.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T1 cooldown "The client checks its own `throwCooldownTicks` = 5 at release"; T4 Release; Core Rules "What you throw"; Edge Cases "If the entry changed between aim and host apply"; AC TC-T1-1 b, TC-T1-2 b, TC-X-3, TC-E-5)
**Requirement**: `TR-throwing-011` (the client leaky-bucket half; the host half is story 009 and the team cap ≤ 48/s is M0), `TR-throwing-010` (claimed origin + last-drawn dir in the request), `TR-throwing-003` (what you saw is what is sent)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§6 Immediate verbs: `IImmediateActionSink`, `DispatchImmediates()` immediately before `RunFrame`; aim-release is router-owned on the ms clock, F11) — Accepted 2026-10-16 (primary for the hand-over); ADR-0015 §1 (payload, `ReqId` carries `seq`, no tick field) and ADR-0016 §1 (`IRequestSink`, `IPanelView`) secondary — Accepted
**ADR Decision Summary**: The router never builds payloads or knows throw directions. On the Throw action's firing edge it appends an `ImmediateAction`; `DispatchImmediates()` invokes the verb owner's `IImmediateActionSink` — here, Throwing's release builder — which builds the request from ClientLayer's published state and sends it through `ClientAdapter` → loopback. `RunFrame` step 1 (`DrainInbound`) then enqueues it in the same frame. A release with `aimHeld ≥ throwMinAim` commits; an earlier one cancels ("Hold to aim") and never reaches the sink. There is no tick field and no hold duration in the request.
**ADR Version**: ADR-0012 2026-10-16; ADR-0015 2026-10-16; ADR-0016 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim + Client.Core code) | **Risk**: LOW
**Engine Notes**: None for this story. The Godot `InputAdapter` (UNVERIFIED until the OQ-IC-9 spike) is input-controls'; the sink is called by Godot-free router code.

**Control Manifest Rules (Feature + Input)**:
- Required: the router never builds payloads; verb owners build them from published ClientLayer state; `ImmediateAction` requests carry the issuing tick for the replay corpus (ADR-0012 §6)
- Required: `origin` in the request is the claimed **feet**; `dir` is the last published preview's quantised integers (GDD T4); no tick field (Input R11)
- Forbidden: the Sim receiving milliseconds; re-quantising or recomputing `dir` in the builder; a verb resolved in `InputLatch`
- Guardrail: the sink allocates 0 B

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T1-1 b, TC-T1-2 b, TC-X-3, TC-E-5), scoped to this story:*

- [ ] **Request contents.** On `OnImmediate(Throw, commit)` with an active `ThrowPreviewState`, the sink builds a `ThrowRelease` whose `entryId`, `units`, `feet` (claimed origin = the published feet), `dir` (the three published int16) and `side` equal the state's integers exactly, `flags = 0`, and sends it through `IRequestSink.TrySend`. A round trip through story 006's codec and the host op ring yields identical integers.
- [ ] **TC-T1-2 b (client cooldown).** A release within `throwCooldownTicks` (5) of this client's own last *sent* release (host-tick domain via `IHostClock.EstimatedHostTick`) is **held** — the payload is captured at release time, including its `dir` — and sent on the first client tick at which the cooldown has elapsed (≤ 5 ticks later); the held request is sent exactly once; a release outside the cooldown sends in the dispatch itself. Only one release can be held; a second commit while one is held is dropped and counted (the router's one-commit gate G9 mutes it with "Busy" first in normal play).
- [ ] **Nothing sent.** No active state, `NotThrowable != None`, hands slot, empty slot, Holding, swimming, or a cancelled/too-early aim (the router never calls the sink) send nothing and start no ghost: TC-T1-1 b "no aim starts and nothing is sent".
- [ ] **TC-X-3 (sink half, Input stub).** With a router stub: a commit calls the sink once and one request is sent; a cancel ("Hold to aim" at 249 ms, early Toggle second press) calls it zero times. The F11 threshold itself (`throwMinAim` 0.25 s default, 0.15 s minimum; 249 ms cancels, 250 ms launches) is the **router's** and is verified by the input-controls epic against this sink; this story documents the contract.
- [ ] **Release record.** Each sent (or held-then-sent) release appends a `ReleaseRecord { Seq, ClientTick, LaunchPredicted (from LaunchMath on the published integers), ClassAtRelease, RedAgeAtReleaseMs, OriginClampedUnknown }` to a preallocated ring the launch ghost and trail read (story 017/020/021); `ClassAtRelease` is the class in the state at the release frame (TC-T3-1 d: a release at t0 + 50 ms gets the new class). `RedAgeAtReleaseMs` is `(clientTick − RedSinceTick) × 1000 / 30` (integer, floor) when the class is Misdeliver, else 0.
- [ ] **One-commit gate hook.** After a successful send the builder reports the commit to the router (`CommitSent(reqId, ActionId.Throw)`) so the G9 pending window (500 ms) covers the throw; the router's `ReplyObserved(reqId)` retires it. If the adapter does not expose the assigned `reqId` to the sender, record the gap in this story's Last Updated note and use the adapter's `LastReqId` accessor added by network story 006's owner.
- [ ] **Reply part.** A `ThrowReplyPart : IMirrorPart` consumes the `OpReply` for each `seq`: `ok` clears the pending record; any reason 64–71 or `bad_request` publishes `LastThrowReply { Seq, Reason }` (HUD shows "Couldn't throw", story 019) and tells the ghost model to fade (150 ms). A rejected release leaves the entry in its slot (non-optimistic: no client-side removal is ever predicted).
- [ ] **TC-E-5 (client half).** `ThrowPreviewState.EntryStale` (story 004) is the only signal; the builder does not read the mirror to cancel. A replayed `seq` after FLIGHT is the host's `stale_entry` (story 009).
- [ ] 10 000 sink calls allocate 0 B; the sink and the held-send step never reference `SimWorld`.

---

## Implementation Notes

*Derived from ADR-0012 §6, ADR-0015 §1 and ADR-0016 §1–§2:*

- Files in `src/PerformativeMail.Sim/Throwing/Client/`: `ThrowReleaseBuilder.cs` (the pure builder `(in ThrowPreviewState, side, out ThrowReleasePayload)`), `ThrowReleaseSender.cs` (holds the pending release, cooldown arithmetic in host ticks, `TrySend`), `ThrowSendStep.cs` (`IClientStep` bound at `PreviewSubStep.EdgeResolve` that flushes a held release once the cooldown has elapsed; it runs after 4c so it sees the same tick's state but sends the **captured** payload), `ReleaseRecord.cs`, `ThrowReplyPart.cs`. The `IImmediateActionSink` adapter class lives in `src/PerformativeMail.Client.Core/Throwing/ThrowImmediateSink.cs` (Client.Core references the Sim; the router is in Client.Core) and only forwards to the Sim-side sender.
- `IRequestSink.TrySend` returns false on a local "busy"; then nothing is sent, the release is *not* held, and the router's commit is reported as not sent (so G9 does not arm). Document this branch with a test.
- The cooldown uses the host-tick domain (`IHostClock.EstimatedHostTick`) so the client's check mirrors the host's `TAT` arithmetic; on loopback they agree exactly. `throwCooldownTicks` and `throwBurstTicks` come from `ThrowBalance`.
- Keep payload byte flags as `byte`; `side` from the player profile port (default +1; left-handed −1, TC-F1-1 c is story 001's vector).
- Spawn-protection, Cannon Arm and `thrownPm` are host concerns (story 010); the client sends nothing about them.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: the state and `EntryStale`. Story 009/010: host validation, cooldown bucket, launch. Story 017/021: the launch ghost that reads `ReleaseRecord`.
- Input-controls: Throw action declaration, aim state, `throwMinAim` (F11), Toggle translation, the one-commit gate and its "Busy" strings, aim-cancel on `EntryStale`.
- Story 019: the "Couldn't throw" / "Select mail to throw" / "Nothing to throw" / "Put it away first" strings.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowReleaseBuilderTests.cs` (`[Trait("Id", "TC-T1-2b")]`, `TC-X-3`, `TC-T1-1b`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004, 006; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`IRequestSink` implementation, `IHostClock`); `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`
- Unlocks: Stories 010 (end-to-end launch), 017, 021; **input-controls epic** (Throw verb request builder and `IImmediateActionSink` target)
