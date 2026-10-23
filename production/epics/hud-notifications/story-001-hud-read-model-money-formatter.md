# Story 001: `IHudState` read model — wallet cents, F3 time-only shift timer, focused deliver prompt (key + typed args) — plus `IMoneyFormatter` and the M-1a English string table (Client.Core)

> **Epic**: HUD & Notifications (M-1a minimal slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + ~400 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (created by the m1a-replan docs PR against ADR-0018, Accepted 2026-10-23 8:10 AM PT; Mail Lead planning defaults, approved by Evan 2026-10-23 8:10 AM PT (TD-173) are labelled inline)

> **UNBLOCKED 2026-10-23 8:10 AM PT: ADR-0018 is Accepted** (Evan, Mail Lead chat, 2026-10-23 8:10 AM PT: "approve all", replying to the widget whose first option was "accept ADR-0018 (minimal HUD)"). The same reply approved the Mail Lead planning defaults labelled inline (TD-173). `/story-readiness` re-run in the m1a-replan PR.

## Context

**GDD**: `design/gdd/hud-notifications.md` (H30 `IHudState` pull values l.266; H10 widget table — timer l.176, wallet l.179; F3 l.350–365; H2 "All money renders through one `IMoneyFormatter`" l.89; H1 `PromptData` l.84); `design/gdd/manual-delivery.md` (UI Requirements l.408–432: keys, `{address}`, `{money}`; MD10 l.136); `design/spec/12-milestones.md` l.24 ("Minimal HUD: timer, wallet, interact prompt with address match indicator")
**Requirement**: `TR-hud-002` (plain-C# UI core testable via `IHudState`/`IMoneyFormatter` — this half), `TR-hud-005` (timer monotone within `(phaseId, deadlineTick)`; text on change only), `TR-hud-012` (all money via one `IMoneyFormatter`; lint), `TR-hud-001` (Presentation-only; Sim has no reference), `TR-delivery-019` (render half: the HUD decodes keys + args, never concatenates)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: H-F3-1 (time subset), H-WIDGET-2, H-PERF-3 (ADVISORY at M-1a), M-UI-1 (render half), PCM-UI-1 (data rows consumed).)*

**ADR Governing Implementation**: ADR-0018: HUD Composition — minimal M-1a slice (§1 placement in `PerformativeMail.Client.Core.Ui`; §2 `IHudState` members, sources and dirty flags; §3 `IMoneyFormatter` and `ShiftTimerF3`; §4.4 `HudStringsEn`) — **Accepted 2026-10-23 8:10 AM PT** (primary); ADR-0016 (§1 `WalletMirror`/`RunMirror` l.86–87; §6 `IRenderClock.RenderSimTimeMs` l.167, "HUD timers → ADR-0018" l.183) — Accepted; ADR-0014 (§7 `InteractionSnapshot.PromptArgs` 32 B l.115; `IInteractionQuery` l.157) — Accepted; ADR-0017 (§8 `Cents`, `WalletChanged` l.160–164) — Accepted
**ADR Decision Summary**: From M-1a the HUD reads only through `IHudState` (H30). This slice implements the pull half for three values: `WalletCents` from `WalletMirror`, `Timer` from `RunMirror` + `IRenderClock.RenderSimTimeMs` through the F3 function (monotone guard keyed `(phase, deadlineTick)`, "00:00" only at 0, hidden while `deadlineTick == 0`), and `Prompt` decoded from `IInteractionQuery.Current.PromptArgs` through manual-delivery story 010's `DeliverPromptArgs` helper. `Refresh()` runs once per frame, sets `HudDirty` flags only for members that changed, and allocates 0 B when nothing changed. One `IMoneyFormatter.Format(Cents, MoneySign)` call site produces every money string; "¢" exists only inside `EnMoneyFormatter`. `HudStringsEn` is a code table (like `BuiltInActionMap`) from key `StringId`s to English formats with rich tokens. The read model is not an `[OutcomeBeat]` and starts none.
**ADR Version**: ADR-0018 2026-10-23 (Accepted 8:10 AM PT); ADR-0016 2026-10-16; ADR-0014 2026-10-16 + errata 2026-10-20/21/22; ADR-0017 2026-10-16 + errata 2026-10-20

**Engine**: Godot 4.7.1 (.NET) — no Godot API used (Client.Core is Godot-free, ADR-0001) | **Risk**: LOW
**Engine Notes**: None. The `tnum` and `Label` concerns are story 002's.

**Control Manifest Rules (Presentation + Global)**:
- Required: never reference a `Godot*` assembly in Sim or Client.Core; never let the Sim reference Client.Core (`docs/architecture/control-manifest.md` l.244)
- Required: presentation time comes from `IRenderClock`; `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned (l.511)
- Forbidden: `ClientLayer` or Presentation reading `SimWorld` directly (l.522); reimplementing a Sim rule or formula in Client.Core (l.526) — the HUD decodes `PromptArgs` only through the Sim-side `DeliverPromptArgs` helper
- Forbidden: a predictive component starting an outcome beat (l.218)
- Guardrail: 0 B per frame in steady state (H-PERF-3, ADVISORY at M-1a)

---

## Acceptance Criteria

*From ADR-0018 §2–§3, GDD H30/F3/H-WIDGET-2 and manual-delivery UI Requirements, scoped to the Godot-free read model (views are stories 002/003):*

- [ ] **AC-1 Shape and sources (H30 pull half; TR-hud-001/002).** `PerformativeMail.Client.Core.Ui` contains `IHudState { Cents WalletCents; ShiftTimer Timer; DeliverPrompt Prompt; HudDirty Dirty; void Refresh(); }` and `HudState`, constructed (ADR-0003 constructor injection) from `IWalletRead`, `IRunRead`, `IInteractionQuery` and `IRenderClock`. With a stub `IWalletRead` whose `Balance` follows a recorded `WalletChanged` sequence (0, +9, −4; the values of mail-destinations story 005 AC-1), `WalletCents` equals the stub's `Balance` after each `Refresh`. An architecture test asserts the `Ui` namespace references no `Godot*` type and no `SimWorld`, `WalletMirror`, `RunMirror` or `AnchorRegistry` type directly (only the read interfaces and the snapshot).
- [ ] **AC-2 F3 time subset (H-F3-1; TR-hud-005).** `ShiftTimerF3.Step` and `HudStringsEn.TimerText`: `remMs` 1 → `DisplaySeconds` 1, text "00:01"; "00:00" only when `remMs == 0`; 134 s → "02:14"; `deadlineTick` 4020 with render 120300 ms → 14 (F3's example, l.365); a same-key backward render step never raises `DisplaySeconds`; a key change (`phase` or `deadlineTick`) may raise it; a frozen render clock leaves it unchanged; `deadlineTick == 0` → `Visible == false`; after the deadline `Visible == true` and "00:00". `deadlineMs = deadlineTick × 1000 / TickRate` uses simulation-core story 006's `TickRate` (30), never a literal 30 in this story. Advancing a scripted clock by 10 000 ms in 1 ms steps produces exactly 10 timer text changes (H-PERF-3 "advancing the clock 10 s writes the timer exactly 10 times", l.631).
- [ ] **AC-3 Money formatter (H-WIDGET-2; TR-hud-012).** `EnMoneyFormatter.Format`: `(86, Delta)` → "+86¢"; `(−4, Delta)` → "−4¢"; `(0, Delta)` → "0¢"; `(−500, Balance)` → "−500¢" (the floor, `design/gdd/hud-notifications.md` l.428); `(9, Balance)` → "9¢". A fake `IMoneyFormatter` records every call with its `(Cents, MoneySign)`. A source lint (test over `src/PerformativeMail.Client.Core/Ui/**` and `src/Game/Hud/**`, excluding `EnMoneyFormatter.cs`) fails on a "¢" character or a currency literal; a planted violation fixture proves the lint fires (the shape of ADR-0012 §8's lint, H-R1-2).
- [ ] **AC-4 Prompt decode (TR-delivery-019 render half; PCM-UI-1 data rows).** For every branch row manual-delivery story 010's test table writes (MATCH, MISDELIVER_ONE, REJECT, NOTHING, UNAVAILABLE rows), a scripted `IInteractionQuery` carrying that `PromptArgs` yields `Prompt { Shown = true, Verb = Deliver, Branch, Key, Address, Units, PredictedPay, PredictedPenalty, Flags, State, ProgressPm, Seq }` equal to what was written, decoded **only** through the `DeliverPromptArgs` helper in `Sim/Delivery` (a source lint finds no `PromptArgs` index or offset constant in `Client.Core/Ui/**`). A focused non-Deliver verb (for example PickUp) yields `Branch = None` and `Key = Availability` reason (`FrameworkReasons`' `reason.*` ids, `src/PerformativeMail.Sim/Players/FrameworkReasons.cs`); no focus (`Focused == default`) yields `Shown = false`. `Seq` is copied from the snapshot.
- [ ] **AC-5 Dirty flags (H29).** A `Refresh` after only the wallet changed sets `Dirty == Wallet`; after nothing changed `Dirty == None`; after `DisplaySeconds` changed `Dirty == Timer`; after any `Prompt` field changed `Dirty == Prompt`. 10 000 `Refresh` calls with unchanged inputs set no flag.
- *Advisory, not an AC (lighter-process proposal, decision log 2026-10-23; H-PERF-3 is ADVISORY at M-1a):* if cheap, also report the bytes those 10 000 calls allocate (`GC.GetAllocatedBytesForCurrentThread`, Release only). A non-zero number is a note in the completion notes, not a failure.
- [ ] **AC-6 String table (M-UI-1 render half; H11 l.193).** `HudStringsEn.Format(StringId)` has a row for every key story 010 writes (`deliver.verb.hold`, `deliver.match.pieces`, `deliver.match.stacks`, `deliver.match.pay`, `deliver.match.dead_only`, `deliver.mismatch.piece_for`, `deliver.mismatch.post_one`, `deliver.floor_refusal`, `deliver.nothing`, `deliver.close_panel`, `deliver.dry_land`, `deliver.pending`, `mail.reject.closed`, `mail.reject.not_on_route` — the spellings of `design/gdd/manual-delivery.md` l.432) and for `FrameworkReasons`' two keys; a test cross-checks story 010's key list against the table and fails on a missing row. Formats use only the tokens `{glyph}`, `{address}`, `{money}`, `{pieces}`, `{n}`, `{stacks}` (`deliver.match.stacks`, GDD l.432); a format that places `{address}` first renders the full address text without truncation in the Godot-free row builder (M-UI-1's "test locale" row, render half). `HudStringsEn.VerbName(Verb)` covers every `Verb` the M-1a catalogue registers (player-character story 012).
- [ ] **AC-7 Additive Sim read interfaces (ADR-0018 §2.2, §4.4 — Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)).** `IWalletRead { Cents Balance }` (`Sim/Economy/Client/`, implemented by `WalletMirror`), `IRunRead { byte Phase; uint DeadlineTick; uint CloseTick }` (`Sim/Run/Client/`, implemented by `RunMirror`) and `IWorldDisplayText { bool TryGetDisplayText(StringId, out string) }` (`Sim/World/`, implemented by `WorldTables` over its existing method, `src/PerformativeMail.Sim/World/WorldTables.cs` l.405–415) exist. If mail-destinations story 010 or world-stub has already exposed an equivalent, use it and add nothing; record which case applied in Last Updated. `IWorldQuery` is not widened.

---

## Implementation Notes

*Derived from ADR-0018 §1–§3, §4.4; ADR-0016 §6; manual-delivery story 010 Implementation Notes:*

- **Files:** `src/PerformativeMail.Client.Core/Ui/IHudState.cs`, `HudState.cs`, `HudDirty.cs`, `ShiftTimer.cs`, `ShiftTimerF3.cs`, `DeliverPrompt.cs`, `DeliverBranch.cs`, `IMoneyFormatter.cs`, `MoneySign.cs`, `EnMoneyFormatter.cs`, `HudStringsEn.cs`; the three read interfaces of AC-7 beside their implementations. Tests: `tests/unit/ui/HudStateTests.cs`, `ShiftTimerF3Tests.cs`, `MoneyFormatterTests.cs`, `HudStringsEnTests.cs`, `HudUiRulesTests.cs` (the two lints + the no-Godot/no-Sim-internals architecture rows, in the style of `tests/unit/architecture/ClientLayerRulesTests.cs`).
- **F3 as written:** `remMs = max(0, deadlineMs − renderSimTimeMs)`, `disp = ceil(remMs / 1000)` in integer maths; the guard `if key == prevKey: disp = min(prevDisp, disp)` (l.352–353). `RenderSimTimeMs` is ADR-0016 l.167's floor value sampled once per frame by the host shell (godot-host-shell 005); `HudState` never samples a clock itself.
- **Prompt decode:** call `DeliverPromptArgs.TryRead(in PromptArgs, out …)` (the helper's name is story 010's; adapt to what it ships). Branch byte 0 or a non-Deliver `Verb` → `Branch = None`. Copy `InteractState`, `ProgressPm` and `Seq` as data for the views; this story draws nothing and the hold ring is deferred (ADR-0018 §6).
- **Money format:** `{sign}{integer}¢` with the minus as U+2212 (as the GDD prints it) — Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173); the formatter is the one place the character lives. No grouping, no decimals (no GDD shows another form; HN-6 makes the format Economy's).
- **Dirty computation:** compare the new `ShiftTimer`/`DeliverPrompt` structs field-wise against the previous frame's copies (plain `readonly struct` equality, no boxing). Strings are produced only by the views on a dirty flag (stories 002/003) and by `HudStringsEn.TimerText` on a `DisplaySeconds` change.
- **Determinism:** nothing here feeds the Sim; `HudState` is read-only over published state (ADR-0016 Implementation Guidelines).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- The Godot layer and views (`Main/Hud/Persistent`, `WalletView`, `ShiftTimerView`): story 002. The prompt rows and view: story 003. Accessibility (screen-reader gate, live regions): Deferred, manual-delivery story 012.
- Writing `PromptArgs` (manual-delivery story 010) and copying it into the snapshot (player-character story 018).
- The `IHudState` event stream, `IUiClock`, `IRoster`, pills, toasts, cue table, event-log records (ADR-0018 §6, M0+ amendment). Localisation beyond the English code table (manual-delivery story 012, Deferred).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/ui/HudStateTests.cs`, `tests/unit/ui/ShiftTimerF3Tests.cs` (`[Trait("Id", "H-F3-1")]`), `tests/unit/ui/MoneyFormatterTests.cs` (`H-WIDGET-2`), `tests/unit/ui/HudStringsEnTests.cs` (`M-UI-1`), `tests/unit/ui/HudUiRulesTests.cs` (lints, planted fixtures) — must exist and pass on net10.0 Debug and net8.0 Release; the AC-5 0 B test runs in Release only
- Test policy (`.claude/rules/test-standards.md`): during development run only this story's classes via `tools/ci/test-quiet.sh`; at close run the full suite once on net10.0 Debug and once on net8.0 Release; CI green on all four legs

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0018 Accepted (`docs/architecture/adr-0018-hud-composition-minimal.md`); `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md` (`WalletMirror`, `RunMirror`); `production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md` (`deadlineTick` in `DeliveriesState`); `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md` (`IInteractionQuery`, `PromptArgs` in the snapshot); `production/epics/manual-delivery/story-010-prompt-content-as-data.md` (`DeliverPromptArgs` helper and key set); `production/epics/godot-host-shell/story-005-host-session-composition-ports-render-clock.md` (`RenderClock` sampled per frame); `production/epics/simulation-core/story-006-tickrate-f1-time-conversions.md` (`TickRate`); `production/epics/world-stub/story-004-addressid-lots-destination-ids.md` (world string table); `production/epics/player-character/story-012-anchor-registry-iinteractable-spatial-grid.md` (verb catalogue)
- Unlocks: Story 002, Story 003
