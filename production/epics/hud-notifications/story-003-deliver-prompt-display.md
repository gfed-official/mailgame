# Story 003: `DeliverPromptView` — the deliver prompt rendered from manual-delivery 010's key + args with the M-1a English string table, the ✓ / ✗ address-match indicator leading line 1, H11 screen-fixed placement, and `PresentedSeq` parity with player-character 026's `FocusFeed`

> **Epic**: HUD & Notifications (M-1a minimal slice)
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: M (~1 day; ~350 lines prod (row builder in Client.Core + Godot view) + tests + smoke + evidence)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (created by the m1a-replan docs PR against ADR-0018, Accepted 2026-10-23 8:10 AM PT; named by `production/epics/manual-delivery/story-010-prompt-content-as-data.md` as its consumer; Mail Lead planning defaults, approved by Evan 2026-10-23 8:10 AM PT (TD-173) are labelled inline)

> **UNBLOCKED 2026-10-23 8:10 AM PT: ADR-0018 is Accepted** (Evan, Mail Lead chat, 2026-10-23 8:10 AM PT: "approve all", replying to the widget whose first option was "accept ADR-0018 (minimal HUD)"). The same reply approved the Mail Lead planning defaults labelled inline (TD-173). `/story-readiness` re-run in the m1a-replan PR.

> **Scope note — what this view adds on top of player-character 026.** Story 026 owns projection and `FocusFeed` (`Visible`, `Kind`, `ScreenPos`, `ScreenBounds`, `PresentedSeq`). This story does **not** re-project and does **not** position the prompt from `ScreenPos`: the prompt is **screen-fixed** (HUD H11 [D] l.192; H12 "The prompt is screen-fixed (H11) and unaffected; only the bracket disappears" l.203; H-PROMPT-1 "an anchor behind the camera leaves the prompt in place with no bracket" l.597; story 026 l.11, l.54). It adds: decoding the key and arguments to ≤ 4 English rows, the ✓ / ✗ mark, H11 placement, dirty-flagged `Label` writes, and a per-frame parity check `feed.PresentedSeq == Prompt.Seq`. The bracket and too-far caret drawn from the feed are the M0+ amendment's (ADR-0018 §6); story 026's debug overlay stands in at the greybox. *(Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173): screen-fixed per H11 rather than world-anchored.)*

## Context

**GDD**: `design/gdd/manual-delivery.md` (UI Requirements: grammar with the mark leading l.408; branch table l.410–428; ≤ 3 lines plus one chip / strip row l.408; focus clarity l.430; "an address is never truncated" l.431 (the rest of that accessibility paragraph — EN budgets, 150 % growth, overflow order — is deferred with accessibility, Evan 2026-10-23 7:15 AM PT); localisation keys l.432; MD10 "never builds address text" l.136); `design/gdd/hud-notifications.md` (H11 placement, deliver body, limits l.191–197; H12 l.203; H1 `PromptData` l.84; H23 reading text ≥ 18 ref px l.236; Visual "address text never moves" l.505); `design/gdd/player-character-movement.md` (UI Requirements "Interaction prompt", PCM-UI-1 render half); `design/spec/12-milestones.md` l.24 ("interact prompt with address match indicator")
**Requirement**: `TR-delivery-019` (prompt content as data; HUD renders; no concatenation — the render half), `TR-movement-011` (focus published for the prompt — the render consumer), `TR-hud-015`/`TR-hud-016` (floors; 0 B; no `DrawLine` — view half)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: H-PROMPT-1 (M-1a rows: mark leads line 1, ≤ 4 rows, MISDELIVER_ONE body never hidden, anchor behind the camera leaves the prompt in place), M-UI-1 (render half), M-UI-2 (the greybox subset: branch screenshots at 1080p; EN line budgets), PCM-UI-1 (render half), H-WIDGET-1 (prompt), H-PERF-3 (ADVISORY).)*

**ADR Governing Implementation**: ADR-0018: HUD Composition — minimal M-1a slice (§2.3 content from the snapshot through `DeliverPromptArgs`; §4.3 `DeliverPromptView` placement, visibility, what it adds on top of 026; §4.4 `HudStringsEn`, the match indicator, `{address}`/`{money}`/`{glyph}` sources; §4.5 hygiene) — **Accepted 2026-10-23 8:10 AM PT** (primary); ADR-0014 (§7 `InteractionSnapshot` fields l.115; `PromptArgs` owner-supplied 32 B; `WritePrompt` l.144) — Accepted; ADR-0019 (§2 `Hud` l.104, `Present` order l.111; §7 stretch; guidelines l.257, l.261–262) — Accepted; ADR-0012 (`FocusMode = None` l.129; glyph data through the router l.189; physical name fallback l.136) — Accepted
**ADR Decision Summary**: The read model (story 001) exposes `Prompt { Shown, Verb, Mode, Key, Branch, Address, Units, PredictedPay, PredictedPenalty, Flags, State, ProgressPm, Seq }`, decoded only through manual-delivery 010's `DeliverPromptArgs`. A Godot-free `DeliverPromptRows` builder (Client.Core.Ui) turns it into ≤ 4 rows using `HudStringsEn`, `IMoneyFormatter.Format(·, Delta)`, `IWorldDisplayText` for `{address}` and the Interact binding's physical-input name for `{glyph}`; line 1 is `✓ Hold [E] Deliver · 17 Elm St` / `✗ Hold [E] Deliver · 17 Elm St` with the mark from the **branch byte, never from focus**. The Godot `DeliverPromptView` (under `Main/Hud/Persistent`, `FocusMode = None`, inert until `Initialize(deps)`) is shown iff the snapshot has a focused anchor, is screen-fixed per H11 (centred, top edge 96 ref px under the reticle = viewport centre at M-1a, max 720 wide, text ≥ 18 ref px on a `StyleBox` plate, no leader line), writes `Label`s only on `Dirty.Prompt`, freezes layout and branch while `State == Holding` (numerals may tick), and asserts `feed.PresentedSeq == Prompt.Seq` each frame. A MISDELIVER_ONE body is never hidden. No ring, no bracket, no strip, no icons at M-1a.
**ADR Version**: ADR-0018 2026-10-23 (Accepted 8:10 AM PT); ADR-0014 2026-10-16 + errata 2026-10-20/21/22; ADR-0019 2026-10-16 + erratum 2026-10-17; ADR-0012 2026-10-16 + amendment 2026-10-17

**Engine**: Godot 4.7.1 .NET | **Risk**: MEDIUM
**Engine Notes**: `Label` with `AutowrapMode` for the address wrap ("addresses wrap, never truncate", H11 l.195) and a `StyleBox` plate (filled shapes / borders ≥ 2 ref px — never `DrawLine`, ADR-0019 l.262). Glyph characters ✓ (U+2713) / ✗ (U+2717) / − (U+2212) must exist in the greybox font — record in the evidence doc; no icon assets at M-1a.

**Control Manifest Rules (Presentation)**:
- Required: views inert until `Initialize(deps)`; presenters World → Hud → Audio (`docs/architecture/control-manifest.md` l.236); presentation time from `IRenderClock` (l.511); HUD and in-world Controls use `FocusMode = None` (ADR-0012 l.129)
- Forbidden: Presentation reading `SimWorld`, the anchor registry or `IInteractionQuery` directly (l.514, l.522 — the view reads only `IHudState` and `IHudFocusFeed`); `DrawLine`/`DrawPolyline` in the HUD (l.526); reimplementing a Sim rule — the match verdict is the Sim's branch, never recomputed here (l.526); feeding a Presentation value into the Sim (l.526)
- Guardrail: 0 B per frame in steady state (H-PERF-3, ADVISORY at M-1a)

---

## Acceptance Criteria

*From manual-delivery UI Requirements l.406–432, HUD H11/H12, H-PROMPT-1 (M-1a rows), M-UI-1/M-UI-2 (render subset) and ADR-0018 §4.3–§4.4, scoped to this view:*

- [ ] **AC-1 Rows from keys (M-UI-1 render half; manual-delivery l.408, l.410–428).** `DeliverPromptRows.Build(in DeliverPrompt, …)` (Client.Core, Godot-free) is table-driven over every row manual-delivery story 010 writes, with the fixture addresses of the GDD table: MATCH k = 1 → line 1 `✓ Hold [E] Deliver · 17 Elm St`, line 2 `12 pieces · +86¢`; MATCH `deadOnly` → `Clears 2 dead letters · 0¢` (never "+0¢", l.416); MISDELIVER_ONE → `✗ Hold [E] Deliver · 17 Elm St` / `Wrong house — piece is for 14 Larch Lane` / `Posts 1 · −4¢` (l.418); MISDELIVER_ONE `floorRefusal` → line 3 `Can't afford a wrong house (−4¢)` with the verb row kept (l.420); NOTHING → one dim line `Nothing for 17 Elm St`, no mark, no glyph (l.421); REJECT closed / not_on_route → `Closed — opens at Delivery` / `Not on today's route` (l.422); Panel open → `Close panel to deliver` with no button glyph (l.423); Swimming → `Dry land required to deliver` (l.424); Pending → `Delivering…` (l.425); a focused non-Deliver verb → line 1 in the grammar table's form for that verb and mode (`VerbGrammarTable`, input-controls 006) with no mark. Every row set has ≤ 4 rows (l.408); `{address}` text comes from `IWorldDisplayText` and is never built from fragments (MD10 l.136; M-UI-1 lint row reused from story 001); `{money}` is the one formatter call with `MoneySign.Delta`; `{glyph}` is keyboard text — the Interact binding's physical-input name from `BuiltInActionMap` (input-controls 005; ADR-0012 l.136 fallback) — the GDD examples print `[E]`; `router.Glyphs` is input-controls 030 (Deferred, M1), ADR-0018 erratum 2026-10-23 (Evan 9:08 AM PT).
- [ ] **AC-2 The match indicator is the Sim's verdict, never focus (spec 12 l.24; H-PROMPT-1 "MISDELIVER_ONE body … never hidden"; story 026 "the feed never invents a verdict").** The ✓ / ✗ mark is chosen from `Prompt.Branch` (MATCH → ✓, MISDELIVER_ONE → ✗) and from nothing else: a `FocusFeed` with any `Kind`/`Visible` and a `Branch = None` prompt shows no mark; a MISDELIVER_ONE prompt shows ✗ and `Posts 1 · −N¢` in every HUD state the greybox has (panel closed, holding, pending-free), and no code path hides it. Lock and slash icons, the ⚠ / skull chips, the hazard ring and the hold ring are **not** drawn at M-1a (ADR-0018 §6) — the rows carry the text only.
- [ ] **AC-3 Visibility, placement and freezing (H11 l.192–195; H12 l.203; H-PROMPT-1 behind-camera row; manual-delivery l.426 HOLDING).** The view is shown iff `Prompt.Shown` (snapshot `Focused != default`) and hidden otherwise; a feed reporting the anchor behind the camera (`Visible = false`) leaves the prompt in place. Placement is screen-fixed: centred horizontally, top edge 96 ref px under the reticle (viewport centre at M-1a — planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173), until a camera story publishes a reticle point), max 720 ref px wide, reading text ≥ 18 ref px on a backing plate, no leader line; the address wraps and is never truncated. While `State == Holding` the row layout and the mark are frozen and only numerals update (l.426 "the latched branch's lines, layout frozen; numerals may tick"); a cancel (`State` back to Idle with `LastCancel`) unfreezes on the next `Dirty.Prompt`. The reason strip is not drawn (ADR-0018 §6).
- [ ] **AC-4 Parity with story 026 (ADR-0018 §4.3; story 026 "`FocusFeed` contract").** Each presented frame the view reads `IHudFocusFeed.Current.PresentedSeq` and compares it with `Prompt.Seq`; a smoke scenario (`hud-prompt-seq-parity`) over ≥ 300 frames with scripted focus changes records 0 mismatches (both are built after the frame's last client tick, World before Hud — ADR-0019 l.111). The view publishes no second `PresentedSeq`; the feed's is the one the router echoes (story 026; ADR-0014 l.115).
- [ ] **AC-5 Hygiene (H29; ADR-0019 l.262).** `Label`s are written only when `Dirty.Prompt` is set (and once at `Initialize`); 600 frames with an unchanged prompt write 0 text properties (`hud-idle` extended); `FocusMode = None`; no `_Process`, no `delta`; the `DrawLine` check (story 002) covers this file.
- *Advisory, not an AC (lighter-process proposal, decision log 2026-10-23; H-PERF-3 is ADVISORY at M-1a):* managed bytes in `Present` may be logged by the same scenario; non-zero is a completion note, not a failure.
- [ ] **AC-6 Evidence (M-UI-2 greybox subset — ADVISORY until a render rig exists, GDD l.565).** `production/qa/evidence/hud/deliver-prompt-evidence.md` with retained 1920×1080 screenshots of MATCH, MATCH `deadOnly`, MISDELIVER_ONE, MISDELIVER_ONE `floorRefusal`, NOTHING, REJECT closed, Panel open, Swimming, Pending and a non-Deliver verb, plus one at 1280×720, the `SMOKE … PASS` lines of AC-4/AC-5, and a note on the font's ✓ / ✗ / − glyph coverage.

---

## Implementation Notes

*Derived from ADR-0018 §2.3, §4.3–§4.5; manual-delivery UI Requirements; story 026 Implementation Notes:*

- **Files:** `src/PerformativeMail.Client.Core/Ui/DeliverPromptRows.cs` (the Godot-free row builder: ≤ 4 cached strings, rebuilt only on `Dirty.Prompt`); `src/Game/Hud/DeliverPromptView.cs` (+ `.uid`), `src/Game/Main.tscn` (node under `Main/Hud/Persistent`); `src/Game/Smoke/HudPromptSmoke.cs`; tests `tests/unit/ui/DeliverPromptRowsTests.cs`; evidence doc above.
- **Decode once:** the view never touches `PromptArgs`; it reads `IHudState.Prompt` (story 001) which decoded through manual-delivery 010's `DeliverPromptArgs`. If story 010's helper exposes fields this story needs that story 001 did not copy (for example a `deadOnly` or `wontFit` flag bit), extend `DeliverPrompt` additively and record it.
- **Row 1 grammar:** `{mark} {grammar(verb, mode, glyph)} · {address}` — the grammar form comes from `VerbGrammarTable` (input-controls 006, Complete) and `HudStringsEn.VerbName`; the mark slot is empty for `Branch = None`, NOTHING and the reason rows (l.421–424). Toggle mode's `deliver.verb.toggle`/`.armed` rows (l.427) render from their keys when story 010 writes them; Toggle translation is the router's.
- **Address:** `IWorldDisplayText.TryGetDisplayText(Prompt.Address, out text)`; on `false` render the key's row without the address token and count it (a mismatch between the world string table and the prompt args is a bug, not a display case).
- **Hold freeze:** keep the row strings and mark while `State == Holding`; rebuild only the numeric tokens (`{pieces}`, `{money}`) when `Units`/`PredictedPay` change. A branch change during a hold cancels the hold in the Sim (MD4), so the frozen rows never contradict the latched branch.
- **Reticle:** viewport centre until player-character 023's `CameraRig` publishes a reticle point; then read it from `PresenterDeps` (additive) — record the switch.
- **Debug:** story 026's `Debug/FocusDebugOverlay` keeps drawing the bracket and caret in debug builds; this view draws neither.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Writing `PromptArgs` (manual-delivery 010); the snapshot (player-character 018); projection, `FocusFeed`, the debug overlay (player-character 026).
- The hold ring, lock / slash icons, chips, hazard ring, reason strip, panel-footer relocation, the focus bracket and too-far caret, `GlyphSwap` log records and device-flip glyph swap (H-INT-3), the live-region announcement (M-UI-3): ADR-0018 §6 (M0+ amendment) and manual-delivery story 012 (Deferred).
- Localisation beyond the English code table; the test locale and concatenation lint (manual-delivery story 012, Deferred — the render-side lint row of story 001 stays).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: UI
**Required evidence**:
- Logic half: `tests/unit/ui/DeliverPromptRowsTests.cs` (`[Trait("Id", "M-UI-1")]` and `H-PROMPT-1` rows for AC-1/AC-2 — must exist and pass on net10.0 Debug and net8.0 Release
- UI: smoke scenarios `hud-prompt-rows`, `hud-prompt-seq-parity`, `hud-prompt-freeze` in `src/Game/Smoke/HudPromptSmoke.cs` (`SMOKE … PASS`) — must pass; retained screenshots and sign-off in `production/qa/evidence/hud/deliver-prompt-evidence.md` (ADVISORY until a render rig exists)

- **Screenshot ruling:** the retained screenshots are **required** for `/story-done` (one per row, 1920×1080 only; the 1280×720 shot is optional). "ADVISORY until a render rig exists" applies only to pixel-floor measurement.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-0018 Accepted (`docs/architecture/adr-0018-hud-composition-minimal.md`); Story 001 (`IHudState.Prompt`, `HudStringsEn`, `IMoneyFormatter`, `IWorldDisplayText`); Story 002 (`Main/Hud/Persistent`, `HudLayer`, `PresenterDeps.HudState`); `production/epics/manual-delivery/story-010-prompt-content-as-data.md` (keys, args, `DeliverPromptArgs`); `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md` (snapshot `Seq`, `InteractState`); `production/epics/player-character/story-026-interaction-prompt-anchor-projection.md` (`IHudFocusFeed`, `PresentedSeq`); `production/epics/input-controls/story-005-action-map-builtin-m1a-defaults.md` (Interact binding physical name for `{glyph}`); `production/epics/input-controls/story-006-conflict-validator-chord-resolution.md` (`VerbGrammarTable`); `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md`
- Unlocks: `production/epics/manual-delivery/story-012-deferred-prompt-accessibility-localisation.md` (Deferred); the M0+ amendment's ring, bracket, strip and `GlyphSwap` (input-controls 030's `router.Glyphs`, Deferred to M1, replaces the keyboard-text `{glyph}` then; ADR-0018 erratum 2026-10-23, Evan 9:08 AM PT)
