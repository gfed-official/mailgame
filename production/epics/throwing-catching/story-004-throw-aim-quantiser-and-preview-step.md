# Story 004: Throw aim quantiser and the `ThrowPreview` client step (4c) — the honest arc, its end class and the published `ThrowPreviewState`

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Owner-side story (1 of 3).** T3 "Aim and preview (client)". Everything the player sees while aiming is computed here, in the Sim assembly's `ClientLayer`, from the **same** `LaunchMath` / `Ballistic` / `FlightSweep` the host runs. The ribbon view (story 018) and label (stories 005, 019) only draw what this step publishes.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T3 aim and preview; T1 "What you throw"; F1 `dir` quantisation; Game Feel "Press → arc visible ≤ 2 rendered frames", "Arc update"; Edge Cases "If the entry changed between aim and host apply")
**Requirement**: `TR-throwing-003` (preview calls the same Sim function as the host; identical over 10 000 throws — the equality fuzz is story 014), `TR-throwing-017` (aim preview ≤ 0.3 ms/frame, 0 alloc — the data half; the ribbon draw is story 018), `TR-throwing-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-T3-1 a/e, TC-T1-1 b (state half), TC-P-1 (0 B half).)*

**ADR Governing Implementation**: ADR-0016: ClientLayer Previews and Presentation Timing (§2 `PreviewSubStep.ThrowPreview` = 4c, §3 shared functions over facts, §7 budgets) — Accepted 2026-10-16 (primary); ADR-0015 §8 (the preview and its cache key), ADR-0017 (`MailRules.Evaluate`, facts structs) and ADR-0014 (`ICarrierView`/`CarrierStatus.Aiming` via Player Character) secondary — Accepted
**ADR Decision Summary**: The throw preview runs in `Previews` sub-step 4c, after focus (4a) and the deliver preview (4b) and before edge resolution (4d), once per client tick. It is `LaunchMath.Compute` (from the predicted feet and the quantised dir) → `Ballistic.PositionAt` → `FlightSweep.FirstContact` over a **static-only** receiver view, with the arc colour from Mail's client `MailRules.Evaluate` on the first catch volume entered. 4c recomputes only when its quantised cache key (dir, origin, destination phase, wallet, static version) changes. It publishes read-only state; Presentation never recomputes it.
**ADR Version**: ADR-0016 2026-10-16; ADR-0015 2026-10-16; ADR-0017 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None. Explicit sequential layout and `byte` flags (not `bool`) in the published state struct.

**Control Manifest Rules (Feature layer + ClientLayer)**:
- Required: client steps live in `Sim/<System>/Client/`, read only facts built from mirror parts or ports, and never reference `SimWorld` (ADR-0016)
- Required: the preview runs the same `LaunchMath`/`Ballistic`/`FlightSweep`; shared functions are static, pure, 0-alloc over facts (ADR-0016 §3, ADR-0015 §8)
- Forbidden: a Presentation- or Client.Core-side ballistic or sweep; the preview seeing goons (ADR-0015 §8); the preview ever showing a verdict from a previous class (T3 "No stale verdict")
- Guardrail: `ClientLayer.Step` ≤ 0.3 ms p99 (advisory until Q-ARCH-10); 0 B per `Step` after warm-up (ADR-0016 §7)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-T3-1 a/e, TC-T1-1 b state half) and ADR-0016 §2/§7, scoped to this story:*

- [ ] **TC-T3-1 a (quantiser).** Camera pitch +80° and −70° (past the camera clamp) quantise to elevations +76° and −54° (pitch clamped to −60°…+70°, plus `loftDeg` 6°): `dy` = 15 897 ± 2 and −13 255 ± 2. Pitch +6° → dir (0, 3406, −16 026) when facing −Z (the FX-T vector). Every quantised dir has `16000 ≤ n ≤ 16768` and passes the host bounds of story 001 over 10 000 random yaw/pitch pairs.
- [ ] **Same integers.** The `dir` published in `ThrowPreviewState` is exactly the integers `LaunchMath.Compute` consumed for the drawn arc; `ThrowReleaseBuilder` (story 007) sends those integers (no re-quantisation anywhere).
- [ ] **Arc and end class.** For the FX-Flat fixture the published polyline is `Ballistic.PositionAt(l, k)` for `k = 0 … endAge`, truncated at the first contact (the contact point replaces the last point). End classes: **Accept** (descending entry into the destination whose `Evaluate` = Accept), **Misdeliver**, **Reject** (reason Closed / Locked / WontFit from `Evaluate`), **Ground**, **Water**, **Blocked** (solid / roof / map edge), **Belt** (reserved, M-1b), **Timeout** (no contact by `maxFlightTicks`).
- [ ] **Colour from Evaluate only.** The class for a mailbox contact is `MailRules.Evaluate(DestinationFacts, UnitFacts)` built from `DestinationMirror` and the selected entry (`production/epics/mail-destinations/story-010-client-mirrors-and-facts.md` facts constructors); no wallet is read for the class; a closed or locked box shows Reject, not Accept. Closing grace is judged as the host will (Mail's effective-state helper at the current client tick).
- [ ] **Static receivers only.** The query uses `ReceiverMask.StaticOnly` (story 003); an armed catcher or any dynamic receiver never changes the class; goons are never considered.
- [ ] **TC-T3-1 e (cache key).** Changing any one of dir, origin (feet), destination phase / open state, wallet, or the static version re-runs the query (an evaluation counter test seam, as ADR-0016 §7 requires); an unchanged key re-runs nothing and republishes the same state with `Seq` unchanged. A walking carrier with a pad changes the key every tick (worst case is the budget).
- [ ] **Class tracking for downstream.** The state carries `ClassSeq` (increments on each end-class change, never on a pure geometry change within a class), `ClassSinceTick` (client tick the current class began) and `RedSinceTick` (the tick the class last became Misdeliver; 0 when not Misdeliver) so the label settle (story 005), the preview sound edge (story 005) and `redAgeAtReleaseMs` telemetry (story 016) need no extra computation.
- [ ] **TC-T1-1 b (state half).** With the hands slot (0) selected the state is inactive with `NotThrowable = HandsSlot`; with an empty selected slot `NotThrowable = EmptySlot`; swimming → inactive with `NotThrowable = Swimming` (the strings are story 019's); no polyline in any of them. With a throwable mail entry or non-mail item selected and `CarrierStatus.Aiming` true the state is active.
- [ ] **Entry latch.** The state publishes the latched `EntryId` and `Units` (what the thrower sees) and `EntryStale = 1` when the selected entry's id or count differs from the entry latched at aim start (or the entry vanished) — the router consumes it for Input G2's "entry gone or stale" cancel (input-controls epic).
- [ ] **Predicted origin.** The feet come from the client `ICarrierView` (Player Character story 020), i.e. the same position that `ThrowRelease` claims; `side` (dominant hand) comes from the player profile via a port with a default of +1.
- [ ] **0 B.** 600 ticks of aiming while walking (key changes every tick) allocate 0 B in 4c; polyline buffers are preallocated at `maxFlightTicks + 1` points.
- [ ] **Sub-step order.** A composition test asserts 4c is bound at `PreviewSubStep.ThrowPreview` and runs after 4b and before 4d.

---

## Implementation Notes

*Derived from ADR-0016 §2–§3, §7 and ADR-0015 §8:*

- Files in `src/PerformativeMail.Sim/Throwing/Client/`: `ThrowAim.cs` (pure: `Quantise(ushort yawBam, short pitchBam, int loftMilliDeg, out short dx, out short dy, out short dz)` using the Q15 trig table from `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md`; pitch clamp −60°…+70° then `+loftDeg`; use the Player Character yaw convention from `production/epics/player-character/story-005-locomotion-f1-f2-camera-relative-yaw.md`), `ThrowPreviewStep.cs` (`IClientStep`), `ThrowPreviewState.cs` (+ `IThrowPreviewRead { ref readonly ThrowPreviewState Current { get; } }`, the ADR-0016 `IDeliverPreviewRead` pattern), `PreviewCacheKey.cs`, `ISelectedEntryView.cs`, `EndClass.cs`, `NotThrowable.cs`.
- **Port `ISelectedEntryView`** (Throwing-defined): `bool TryGet(out SelectedEntryFacts f)` with `SelectedEntryFacts { byte HotbarIndex; EntryId Entry; byte Units; bool IsMail; UnitFacts Unit; byte WeightClass; DefIndex Item; }`. **Unowned seam (EPIC.md):** nothing in grid-inventory or input-controls yet owns the *selected hotbar slot* state or the adapter from the own-container mirror (`IPanelView`, declared by `production/epics/manual-delivery/story-002-deliver-scan-preview-f1.md`) to the selected entry; the host needs the same fact for ThrowRelease check 2 (story 009, `IHotbarSelection`). Implement the port with a test double here and flag the production adapter; Player Character story 025 raised the same gap (`IHeldItemView`) — share one adapter if it lands first.
- Aim state: `CarrierStatus.Aiming` (`production/epics/player-character/story-013-contexts-verbs-availability-gate-router-declarations.md`, published by story 020). The preview does **not** time the aim; `throwMinAim` and the early-release cancel are the router's (ADR-0012 §6, ms clock).
- Per-tick vs per-frame: the GDD says "every rendered frame"; ADR-0016 runs 4c once per client tick. **ADR followed.** The ribbon view re-projects the last published 3D polyline to the current camera every rendered frame (story 018), so the line moves with the camera at frame rate while the class and `dir` are tick-exact; the release sends the last published `dir`, which is exactly "the last drawn preview" (T4). Record this in the story's Last Updated note.
- Teammate hat pins (T3) need other carriers' positions and are M0: leave a `HatPins` span in the state, always empty at M-1a (deferred list in EPIC.md). The preview never sees goons.
- Money, label text and the settle timing are story 005; the cache key here includes the wallet so a wallet change re-runs the query (the label depends on it), but the **class** never reads the wallet.
- Keep the polyline coarse enough for the view: publish every tick's position (≤ 91 points); the view subdivides in screen space.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 005: label model, F7 money, 100 ms settle, `previewSettleMs` pending marker, preview sound edge. Story 018: ribbon rendering, patterns, markers. Story 019: label pill and reason strip.
- Story 007: building and sending `ThrowRelease`, the client cooldown hold. Story 014: preview = host equality over 10 000 throws.
- Hat pins for teammates (M0), goons (never), belts (M-1b).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowAimTests.cs` and `tests/unit/throwing/ThrowPreviewStepTests.cs` (`[Trait("Id", "TC-T3-1")]`, `TC-T1-1b`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 003; `production/epics/simulation-core/story-023-clientlayer-shell-inputlatch.md` (`ClientLayer`, `IClientStep`); `production/epics/player-character/story-014-focus-scoring-dwell-preview-binding.md` (defines `PreviewSubStep`/`BindPreview` if absent); `production/epics/player-character/story-020-carrierstep-clientlayer-integration.md` (`ICarrierView`, aim yaw/pitch, `CarrierStatus`); `production/epics/mail-destinations/story-003-evaluate-and-effective-state.md` and `story-010-client-mirrors-and-facts.md` (`Evaluate`, `DestinationMirror`, facts); `production/epics/simulation-core/story-005-bam-trigq15-stablesort.md`
- Unlocks: Stories 005, 007, 014, 016, 018, 019; **input-controls epic** (consumes `ThrowPreviewState.EntryStale` for G2 and the aim-start refusals — see EPIC.md)
