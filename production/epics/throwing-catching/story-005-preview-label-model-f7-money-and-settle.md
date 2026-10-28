# Story 005: Preview label data — F7 stake money, the two-line label model, the 100 ms label settle, `previewSettleMs` and the preview sound edge

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **2026-10-23 (m1a-replan, Evan 7:13/7:15 AM PT):** the live-region line AC moved to `story-025-deferred-throw-accessibility.md` (**Deferred**). Evan, 7:15: "This is a game among friends, so a lot of the extra work to make it “production ready” is not needed. Accessibility for example".

> **Owner-side story (2 of 3).** The *data* behind the arc label: what it says, what the throw is worth, and when it may change. The Charcoal pill that draws it is story 019 (UX spec `design/ux/throwing-catching.md`); this story is complete and testable without any UI.

> **UX spec addendum (2026-10-16):** `design/ux/throwing-catching.md` (label catalogue, OQ-TC-4) adds line-1 keys `throw.label.locked`, `throw.label.wontfit`, `throw.label.blocked`, `throw.label.pending` and line-2 keys `throw.label.money.accept_dead`, `throw.label.count` — the "every end class maps to a line-1 key" test must include them.

## Context

**GDD**: `design/gdd/throwing-catching.md` (T3 "Label" and "No stale verdict"; F7 preview label money; UI Requirements "Arc label", "Money is shown", "Timing"; Visual/Audio "Audio" preview rows; Tuning Knobs `previewSettleMs`)
**Requirement**: `TR-throwing-003` (the label money is Mail's `Evaluate`/pay functions, identical to the host's), `TR-throwing-017` (≤ 0.3 ms/frame, 0 alloc for query + `Evaluate` + label build)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F7-1 a/b/c, TC-T3-1 d, TC-VA-2, TC-P-1; TC-U-1 is the label-structure half only.)*

**ADR Governing Implementation**: ADR-0016: ClientLayer Previews and Presentation Timing (§3 facts and "Preview money is Σ over units of `MailPay.UnitPay`, rounded once per unit", §6 `IRenderClock`) — Accepted 2026-10-16 (primary); ADR-0017 (`MailPay`, `HintPayout`, `WalletGuard`, `Cents`) and ADR-0015 §2 (F7 uses Mail's rounding, not `Throw.RH`) secondary — Accepted
**ADR Decision Summary**: The client builds `DestinationFacts`/`UnitFacts` from its mirrors and calls Mail's static pure `Evaluate`/`MailPay` functions; preview money is the sum over units of the per-unit pay (or penalty), each rounded once exactly as Mail's host path (F10), never one rounding over a total. Presentation time comes only from `IRenderClock` (`RenderSimTimeMs`); `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned for gameplay-facing timing.
**ADR Version**: ADR-0016 2026-10-16; ADR-0017 2026-10-16; ADR-0015 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim and Client.Core code) | **Risk**: LOW
**Engine Notes**: None. The settle tracker and sound edge live in Client.Core, which has no Godot reference.

**Control Manifest Rules (Feature + Presentation timing)**:
- Required: shared rule functions are static, pure, 0-alloc over facts; Client.Core and Godot never reimplement a rule or a rounding (ADR-0016)
- Required: presentation time comes from `IRenderClock`; strings are localisation keys with arguments, never concatenated (GDD UI Requirements)
- Forbidden: showing the previous class's verdict during a class change; label text in red or magenta (a data flag `ForbidWarmHues` is asserted by the view story); any wallet write or `IWalletGate` reference on the client path (Mail story 010 AC-3)
- Guardrail: query + `Evaluate` + label build p95 ≤ 0.3 ms, max ≤ 0.5 ms (ADVISORY until the M1 reference machine exists); 0 B per frame (BLOCKING)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-F7-1, TC-T3-1 d, TC-VA-2, TC-P-1, TC-U-1 structure), scoped to this story:*

- [ ] **TC-F7-1 (FX-L20, parametrised).** 20 letters, spawn 12 ¢, `MailValueMult` and `MisdeliveryPenaltyMult` 1000 ‰ (penalty 6 ¢), on time. Label line 2: Accept → `×20 · +240¢`; Misdeliver at wallet −380 → `×20 · −120¢`; at −381 / −440 / −494 → `−114¢ to floor · 1 returned` / `−60¢ to floor · 10 returned` / `−6¢ to floor · 19 returned`; at −495 and −500 → `×20 · refused at floor · all returned`. (`+260¢` at `thrownPm` 1100 is **Later** — the argument is plumbed, the vector is non-gating.)
- [ ] **TC-F7-1 b.** Value-hint runs [(1 unit, spawn 8 ¢, penalty 4), (2 units, spawn 9 ¢, penalty 5)] at wallet −492 → `−4¢ to floor · 2 returned` (oldest-first prefix, stop at the first unit that would cross the floor; cheaper later units never skip ahead).
- [ ] **TC-F7-1 c.** Dead letters (T‰ = 0): Accept shows `+0¢`; Misdeliver still shows the full penalty.
- [ ] **Equals the host.** For every FX-L20 case the label's money equals what story 012's host landing produces against the same wallet (`ResolveThrownLanding` consumed units and wallet delta); the label uses `MailPay.UnitPay`/`UnitPenalty`/`HintPayout`/`WalletGuard` — an architecture test finds no money arithmetic in `Sim/Throwing/Client/` other than calls to those functions.
- [ ] **Label structure (data half of TC-U-1).** The model has exactly two lines. Line 1 = glyph id + place key with arguments (`ok.address`, `wrong.address + wrong-house`, `closed.opens-at-delivery`, `ground`, `water.floats`, `belt.joins`, `timeout.drops`) — localisation keys with args, never concatenated strings. Line 2 = `×N` (N ≥ 1) + the money/state key. Every end class from story 004 maps to a line-1 key; a table-driven test asserts none is missing.
- [ ] **TC-T3-1 d (settle, virtual clock).** On a class change at render time t0, the model's *line style / marker / glyph* output switches on the same frame, while the label **text** switches at t0 + 100 ms and not at t0 + 99. The label's first appearance (aim start) is immediate. With `previewSettleMs` = 250 the model reports `PendingMarker = true` from t0 to t0 + 250 (never the previous verdict), then the new class. A release at t0 + 50 reads the **new** class from the state (story 007 gives the trail the new class).
- [ ] **TC-VA-2 (sound edge).** The class becoming Accept or Misdeliver and holding 100 ms emits exactly one `PreviewSoundCue` (`Hit` / `Miss`); 99 ms emits none; cues are ≥ 200 ms apart (a flick back and forth emits at most one per 200 ms); Reject emits the neutral `Blocked` cue; Ground and Water emit none; the cue is thrower-only data (no event reaches other peers) and the setting `previewSoundEnabled` (default on) suppresses it.
- [ ] **TC-P-1.** 600 ticks of aiming while walking with a pad (no cache hits), measured scope = story 004's query + `Evaluate` + label build: p95 ≤ 0.3 ms and max ≤ 0.5 ms (recorded, ADVISORY); 0 allocations per tick (BLOCKING).

---

## Implementation Notes

*Derived from ADR-0016 §3/§6 and GDD F7, T3, UI Requirements:*

- Files: `src/PerformativeMail.Sim/Throwing/Client/ThrowLabelModel.cs` (pure: `(in ThrowPreviewState, in SelectedEntryFacts, in HintRuns, Cents wallet, Cents walletFloor, int thrownPm) → ThrowLabel`; `ThrowLabel` is a fixed-size readonly struct of `StringId` keys and int args), `LabelMoney.cs` (F7; calls Mail), and `src/PerformativeMail.Client.Core/Throwing/PreviewSettle.cs` (`Update(long renderMs, in ThrowPreviewState s, in ThrowLabel l) → PreviewDisplay { bool PendingMarker; ThrowLabel DisplayedLabel; PreviewSoundCue Cue; bool LiveRegionDue }`; settings via `IPreviewSettings { int PreviewSettleMs; bool PreviewSoundEnabled; }`, ADR-0012 stores `previewSettleMs` in its integer-ms settings).
- The Sim never sees milliseconds (ADR-0012, ADR-0006), so the settle/sound timing lives in Client.Core on `IRenderClock.RenderSimTimeMs` (`production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` creates the assembly; `IRenderClock` is ADR-0016 §6 — if no story has added it yet, add it here and log it). The tracker keys on `ThrowPreviewState.ClassSeq`.
- F7 inputs: the replicated value hint (Mail R17, ≤ 5 runs `(deadlineShift, count, unitSpawnValue)` in ascending mailId order) comes from the own-container mirror entry (`IPanelView`, `production/epics/manual-delivery/story-002-deliver-scan-preview-f1.md`); T‰ is Mail F5 at the shift of the **current** client tick (the host judges by `releaseTick`, which equals the apply tick within the grace rules); wallet from `WalletMirror`; floor from the registry. All `Cents` are int32.
- Misdeliver "prefix" rule uses `MailPay.WalletGuard` per unit exactly as the host loop; "returned" = N − units in the prefix.
- Do not put colour, hue or font data in the model; the view story owns it. Keep a `bool UsesWarmHue = false` constant-asserting field only if the view needs it (GDD: text is never red or magenta).
- Teammate-pin and "Catch" text never appear in the label (T3).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 019: the pill layout, flip rules, 200 % text scale, localisation table content (Blocked on the UX spec). Story 018: line/marker drawing. Audio playback of the cue (`AudioPresenter` sink seam: `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md`; the mix is the Audio epic's).
- Story 004: the class and polyline. Story 016: `redAgeAtReleaseMs` telemetry.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/throwing/ThrowLabelMoneyTests.cs`, `tests/unit/throwing/PreviewSettleTests.cs` (`[Trait("Id", "TC-F7-1")]`, `TC-T3-1d`, `TC-VA-2`, `TC-P-1`) — must exist and pass on net10.0 and net8.0 (the TC-P-1 timing numbers are recorded, not gated)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004; `production/epics/mail-destinations/story-004-mailpay-f5-f8-f10.md` (`UnitPay`, `UnitPenalty`, `HintPayout`, `WalletGuard`); `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md` (`WalletMirror`, facts); `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` (Client.Core assembly)
- Unlocks: Stories 016, 018, 019
