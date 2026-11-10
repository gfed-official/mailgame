# Story 007: `ThrowReleaseBuilder` — the release request from the last drawn preview, the client cooldown hold and the reply part

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Complete (greybox; the cooldown drops the release instead of holding it → TD-230 (7); ThrowImmediateSink and the G9 CommitSent hook not built — WP-F's ThrowReleaseSender commits (TD-270 (7)))
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; the cooldown drops the release instead of holding it → TD-230 (7); ThrowImmediateSink and the G9 CommitSent hook not built — WP-F's ThrowReleaseSender commits (TD-270 (7))); delivered by PR #87, merge 0490e0f; wired as the Throw sink by PR #94, merge f145219; see Completion Notes).

> **Owner-side story (3 of 3).** T4 "Release". The Throw action is an *Immediate* verb: the input router commits it on the event frame and calls this story's sink before `RunFrame`, so the request enters the op ring in the same frame. **Input-controls should depend on this story** (and on story 004's `ThrowPreviewState`) for the Throw verb's request builder and the commit hand-over.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T1 cooldown "The client checks its own `throwCooldownTicks` = 5 at release"; T4 Release; Core Rules "What you throw"; Edge Cases "If the entry changed between aim and host apply"; AC TC-T1-1 b, TC-T1-2 b, TC-X-3, TC-E-5)
**Requirement**: `TR-throwing-011` (the client cooldown hold; the host leaky-bucket half is story 028, Deferred to M0, and the team cap ≤ 48/s is M0), `TR-throwing-010` (claimed origin + last-drawn dir in the request), `TR-throwing-003` (what you saw is what is sent)
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

- [x] **Request contents.** On `OnImmediate(Throw, commit)` with an active `ThrowPreviewState`, the sink builds a `ThrowRelease` whose `entryId`, `units`, `feet` (claimed origin = the published feet), `dir` (the three published int16) and `side` equal the state's integers exactly, `flags = 0`, and sends it through `IRequestSink.TrySend`. A round trip through story 006's codec and the host op ring yields identical integers.
- [ ] **TC-T1-2 b (client cooldown).** A release within `throwCooldownTicks` (5) of this client's own last *sent* release (host-tick domain via `IHostClock.EstimatedHostTick`) is **held** — the payload is captured at release time, including its `dir` — and sent on the first client tick at which the cooldown has elapsed (≤ 5 ticks later); the held request is sent exactly once; a release outside the cooldown sends in the dispatch itself. Only one release can be held; a second commit while one is held is dropped and counted (the router's one-commit gate G9 mutes it with "Busy" first in normal play).
- [x] **Nothing sent.** No active state, `NotThrowable != None`, hands slot, empty slot, Holding, swimming, or a cancelled/too-early aim (the router never calls the sink) send nothing and start no ghost: TC-T1-1 b "no aim starts and nothing is sent".
- [ ] **TC-X-3 (sink half, Input stub).** With a router stub: a commit calls the sink once and one request is sent; a cancel ("Hold to aim" at 249 ms, early Toggle second press) calls it zero times. The F11 threshold itself (`throwMinAim` 0.25 s default, 0.15 s minimum; 249 ms cancels, 250 ms launches) is the **router's** and is verified by the input-controls epic against this sink; this story documents the contract.
- [x] **Release record.** Each sent (or held-then-sent) release appends a `ReleaseRecord { Seq, ClientTick, LaunchPredicted (from LaunchMath on the published integers), ClassAtRelease, RedAgeAtReleaseMs, OriginClampedUnknown }` to a preallocated ring the launch ghost and trail read (story 017/020/021); `ClassAtRelease` is the class in the state at the release frame (TC-T3-1 d: a release at t0 + 50 ms gets the new class). `RedAgeAtReleaseMs` is `(clientTick − RedSinceTick) × 1000 / 30` (integer, floor) when the class is Misdeliver, else 0.
- [ ] **One-commit gate hook.** After a successful send the builder reports the commit to the router (`CommitSent(reqId, ActionId.Throw)`) so the G9 pending window (500 ms) covers the throw; the router's `ReplyObserved(reqId)` retires it. If the adapter does not expose the assigned `reqId` to the sender, record the gap in this story's Last Updated note and use the adapter's `LastReqId` accessor added by network story 006's owner.
- [ ] **Reply part.** A `ThrowReplyPart : IMirrorPart` consumes the `OpReply` for each `seq`: `ok` clears the pending record; any reason 64–71 or `bad_request` publishes `LastThrowReply { Seq, Reason }` (HUD shows "Couldn't throw", story 019) and tells the ghost model to fade (150 ms). A rejected release leaves the entry in its slot (non-optimistic: no client-side removal is ever predicted).
- [x] **TC-E-5 (client half).** `ThrowPreviewState.EntryStale` (story 004) is the only signal; the builder does not read the mirror to cancel. A replayed `seq` after FLIGHT is the host's `stale_entry` (story 009).
- [ ] 10 000 sink calls allocate 0 B; the sink and the held-send step never reference `SimWorld`.

---

## Implementation Notes

*Derived from ADR-0012 §6, ADR-0015 §1 and ADR-0016 §1–§2:*

- Files in `src/PerformativeMail.Sim/Throwing/Client/`: `ThrowReleaseBuilder.cs` (the pure builder `(in ThrowPreviewState, side, out ThrowReleasePayload)`), `ThrowReleaseSender.cs` (holds the pending release, cooldown arithmetic in host ticks, `TrySend`), `ThrowSendStep.cs` (`IClientStep` bound at `PreviewSubStep.EdgeResolve` that flushes a held release once the cooldown has elapsed; it runs after 4c so it sees the same tick's state but sends the **captured** payload), `ReleaseRecord.cs`, `ThrowReplyPart.cs`. The `IImmediateActionSink` adapter class lives in `src/PerformativeMail.Client.Core/Throwing/ThrowImmediateSink.cs` (Client.Core references the Sim; the router is in Client.Core) and only forwards to the Sim-side sender.
- `IRequestSink.TrySend` returns false on a local "busy"; then nothing is sent, the release is *not* held, and the router's commit is reported as not sent (so G9 does not arm). Document this branch with a test.
- The cooldown uses the host-tick domain (`IHostClock.EstimatedHostTick`) so the client's check mirrors the host's `TAT` arithmetic once story 028 (M0) lands; on loopback they will agree exactly. At M-1a the host has no bucket (story 009 check 7 always accepts), so the hold is purely client-side pacing and never produces a `throw_cooldown` reply. `throwCooldownTicks` and `throwBurstTicks` come from `ThrowBalance`.
- Keep payload byte flags as `byte`; `side` from the player profile port (default +1; left-handed −1, TC-F1-1 c is story 001's vector).
- Spawn-protection, Cannon Arm and `thrownPm` are host concerns (story 010); the client sends nothing about them.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004: the state and `EntryStale`. Story 009/010: host validation, launch. Story 028 (Deferred, M0): the host cooldown bucket. Story 017/021: the launch ghost that reads `ReleaseRecord`.
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

**Status**: [x] Created and passing — `tests/unit/throwing/ThrowReleaseBuilderTests.cs` (request contents through the codec, nothing-sent rows, cooldown drop, busy sink, release record, reply part), PR #87 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #87 (merge `0490e0f`), package WP-D (M-1a wave 1), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). PR #94 (`f145219`, WP-F) registers `ThrowReleaseSender` as the router's Throw commit sink (`ClientLoop`, TD-270 (7)) and notes own releases on the flight mirror; PR #96 (`b1a3e8d`) and PR #97 (`8c77595`) added the `[Throw] aim-start / release / result / outcome` log lines (TD-272 (d), TD-273 (2)).
- **Built:** `ThrowReleaseBuilder` (pure), `ThrowReleaseSender` (`TrySend`, cooldown in host ticks, `ThrowSendResult`), `ReleaseRecord`, `ThrowReplyPart` (publishes any non-ok `ThrowRelease` reply) in `Sim/Throwing/Client`.
- **ACs met:** Request contents (the published integers, `flags = 0`, round trip through story 006's codec), Nothing sent (every refusal row), Release record (`ClassAtRelease`, `RedAgeAtReleaseMs`), TC-E-5 client half (`EntryStale` is the only signal).
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 "cooldown hold (drop the release instead)"; held by the package TD row):** TC-T1-2 b — a release inside the client cooldown is dropped and counted (`ThrowSendResult.Cooldown`), not held and sent later; no `ThrowSendStep` → TD-230 (7). TC-X-3 sink half — `Client.Core/Throwing/ThrowImmediateSink` was not built; WP-F's `ThrowReleaseSender` is the `IImmediateActionSink` (TD-270 (7)). One-commit gate hook — `CommitSent` / `ReplyObserved` not wired (input-controls 015 harden later; `LastReqId` not exposed). Reply part — built and unit-tested; its production `BindMirrorPart` binding is not visible in `src/Game` (the ghost fade and the "Couldn't throw" string are story 021 / 019, harden later). 0 B — advisory until M0.
- **Tests:** `ThrowReleaseBuilderTests`, `ThrowLaunchTests.HotbarSelectThenRelease_…`; four legs green (0 failed).

---

## Dependencies

- Depends on: Stories 004, 006; `production/epics/network-adapter-loopback/story-006-clientadapter-requests-inbox-hostclock.md` (`IRequestSink` implementation, `IHostClock`); `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md`
- Unlocks: Stories 010 (end-to-end launch), 017, 021; **input-controls epic** (Throw verb request builder and `IImmediateActionSink` target)
