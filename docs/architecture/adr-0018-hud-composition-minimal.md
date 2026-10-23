# ADR-0018: HUD Composition — the minimal M-1a slice (`IHudState` read model, `IMoneyFormatter`, `Hud` composition) — cue table, notification queue, feed, pills, markers, comms and every accessibility surface deferred

## Status
**Proposed — awaiting Evan's acceptance.**

Nothing built on this ADR may start until Evan accepts it. The three `production/epics/hud-notifications/` stories cite it and are `Blocked` until then. `production/epics/manual-delivery/story-012-deferred-prompt-accessibility-localisation.md` also cites it and is `Deferred`. Who may move this field to `Accepted`: Evan, or `technical-director` on Evan's explicit confirmation (`.claude/docs/templates/architecture-decision-record.md` l.7–17). An agent that believes it is ready says so and escalates; it does not edit this field.

## Date
2026-10-23

## Last Verified
2026-10-23

## Decision Makers
Mail Lead (draft, docs-only worktree `docs/m1a-replan`). Scope set by Evan (project owner), Mail Lead chat, 2026-10-23 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". The "minimal HUD" is spec 12's: "Minimal HUD: timer, wallet, interact prompt with address match indicator" (`design/spec/12-milestones.md` l.24). Accessibility is out of this slice on Evan's 7:15 AM PT steer: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". Acceptance of the ADR itself is still pending.

## Summary
The M-1a greybox needs three on-screen facts (wallet, shift time remaining, the focused deliver prompt with its ✓ / ✗ match indicator) and nothing else, and the full ADR-0018 that `docs/architecture/architecture.md` l.622 plans (`IHudState` / `IUiClock` / `IRoster` / `IMoneyFormatter`, cue record and table, CanvasLayer stack and MarkerLayer, two live-region Controls, dirty flags and pooling) would take weeks the fun test does not need. This ADR is the **minimal M-1a slice** of that row: (a) a Godot-free `IHudState` read model in `PerformativeMail.Client.Core.Ui` holding wallet cents, the F3 shift timer and the focused deliver prompt (key + typed arguments copied from ADR-0014's 32 B `PromptArgs`); (b) one `IMoneyFormatter` call site with no literal "¢" in keys; and (c) three dirty-flagged `Control` views on one `CanvasLayer` under ADR-0019's `Main/Hud` presenter, injected through `Initialize(deps)`, 0 B per frame in steady state (advisory). The screen-reader announcement seam (M-MD4-1e) and every live-region surface are **not** in this slice: Evan deferred accessibility on 2026-10-23 (7:15 AM PT), and manual-delivery story 012 (Deferred) holds the gate. Everything else the planned ADR-0018 owns is **deferred by name** to the M0+ amendment of this same number (§6).

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (.NET) |
| **Domain** | UI (Godot `CanvasLayer`, `Control`, `Label`) over a Godot-free Client.Core read model |
| **Layer** | Presentation |
| **Knowledge Risk** | MEDIUM. The engine surface is `CanvasLayer`, `Control`, `Label`, `Control.FocusMode` — all pre-4.0 APIs — plus two 4.7 facts already sourced by Accepted ADRs: CanvasItem lines lost antialias feathering in 4.7, so no `DrawLine`/`DrawPolyline` (`docs/architecture/adr-0019-godot-scene-architecture.md` l.187, l.262), and `Control.AccessibilityLive` is typed `AccessibilityServer.AccessibilityLiveMode` (`docs/architecture/architecture.md` l.243) — the latter is not used by this slice (announcer M4). Tabular figures through `FontVariation` (`tnum`) are VERIFY-IN-ENGINE (`design/ux/hud-notifications-seed.md` l.299). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`; ADR-0019 §6–§7 (sourced 4.7 CanvasItem and stretch facts); `design/ux/hud-notifications-seed.md` §5 engine verification list (l.293–300) |
| **Post-Cutoff APIs Used** | None in this slice. (`Control.AccessibilityLive` / `AccessibilityServer.AccessibilityLiveMode` are the M4 amendment's.) |
| **Verification Required** | (1) Under ADR-0019 §7's `canvas_items` / `keep_height` stretch, a `CanvasLayer` child of `Main/Hud` composites above the 3D viewport at the 1080-reference size (ADR-0019 l.199–200). (2) Setting `Label.Text` to an equal string is guarded in C# (the view compares before writing), so an idle frame writes 0 text properties — probe in the smoke harness. (3) `tnum` via `FontVariation` for the timer digits (H23 "tabular figures", `design/gdd/hud-notifications.md` l.236). |

> **`Layer` is the ADR's own layer, not the referencing epic's.** This ADR is Presentation-layer (non-critical for `/create-stories`' Foundation check). Its only Sim-side addition is the read-only interfaces of §2.2, added additively.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (Client.Core is Godot-free; `net8.0`), ADR-0003 (constructor injection, no locators), ADR-0004 (`StringId`), ADR-0006 (`Cents`), ADR-0012 (`FocusMode = None` for HUD Controls l.129; `UiEventLog` l.142; Client.Core → Sim dependency direction l.65), ADR-0014 (§7 `InteractionSnapshot.PromptArgs` 32 B l.115; `IInteractionQuery` l.157), ADR-0016 (§1 `WalletMirror` / `RunMirror` l.86–87; §5 predictive vs outcome beats l.128–155; §6 `IRenderClock.RenderSimTimeMs` l.167 and the "HUD timers → ADR-0018" row l.183), ADR-0017 (§8 `WalletChanged{balance, delta, reason}` l.164; `Cents` not `int` l.160), ADR-0019 (§1 "HUD's notification queue … is a node under `Main/Hud`" l.91; §2 `Hud (Node · HudPresenter)` l.104–105 and `Present` order World → Hud → Audio l.111; §7 stretch l.192–205; guidelines l.257, l.261–262), ADR-0026 (`LotRecord.Display` `StringId` l.103; world string table, world-stub story 004) — all Accepted. |
| **Enables** | the M0+ amendment of this ADR (§6), which also defines the announcement seam that `production/epics/manual-delivery/story-012-deferred-prompt-accessibility-localisation.md` (Deferred) needs. |
| **Blocks** | `production/epics/hud-notifications/story-001-hud-read-model-money-formatter.md`, `story-002-wallet-readout-shift-timer-view.md`, `story-003-deliver-prompt-display.md` — all `Blocked` until this ADR is Accepted. |
| **Ordering Note** | **Why a slice under the planned number and not a new ADR:** `docs/architecture/architecture.md` l.568 already assigns TR-delivery-019/020 and TR-hud-001/002/005/006/008–018/021 to "ADR-0018 HUD composition", and l.622 plans the row as "M-1a (minimal)". ADR-0026 set the precedent of accepting a numbered ADR for its M-1a stub scope and revising it later under the same number (`docs/architecture/adr-0026-world-query-contract-and-m1a-street-stub.md` l.15, l.134–140). This ADR **decides** the TRs in the GDD Requirements Addressed table and **defers** the rest (§6); the M0+ amendment may add members and sections and must not change §1–§6 semantics without superseding. Stories 001 → 002/003 in `production/epics/hud-notifications/`. |

## Context

### Problem Statement
Three greybox facts have no home. The wallet exists in `WalletMirror` and the shift deadline in `RunMirror` (ADR-0016 l.86–87; `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md` AC-1), the deliver prompt's content is written into `PromptArgs` by manual-delivery story 010 and copied into the snapshot by player-character story 018, and player-character story 026 publishes the focused anchor's projection — but nothing draws any of it. The HUD GDD flags that its implementation patterns go to "ADR 'HUD Composition & Notification Queue'" (`design/gdd/hud-notifications.md` l.36, l.669), and the manual-delivery epic names the absence of a HUD epic as risk 2 (`production/epics/manual-delivery/EPIC.md` l.49). Evan's 2026-10-23 approval resolves the scope question: a minimal HUD now, the rest later. Deciding nothing means the greybox fun test (hand vs throw share) runs with no wallet and no match indicator, which spec 12 l.24 lists as the M-1a HUD.

### Constraints
- HUD lives entirely on the Presentation side; the plain-C# `PerformativeMail.Sim` holds no reference to it (`design/gdd/hud-notifications.md` l.440; TR-hud-001). Client.Core may use Sim types read-only (`docs/architecture/architecture.md` l.146; ADR-0012 l.65).
- The HUD runs no clock of its own: timers derive from `IRenderClock.RenderSimTimeMs` (ADR-0016 l.167, l.183; TR-hud-004 is ADR-0016's, Accepted).
- Views are inert until `GameLoop` injects dependencies through `Initialize(deps)`; presenters run World → Hud → Audio from `PresentationRoot.Present` (ADR-0019 l.111, l.257; `docs/architecture/control-manifest.md` l.236).
- No `DrawLine`/`DrawPolyline` in the HUD (ADR-0019 l.262; control manifest l.526). HUD Controls use `FocusMode = None` (ADR-0012 l.129).
- Outcome beats play only from `PresentationEventStream` cursors; predictive components never start one (ADR-0016 l.135–155; control manifest l.218).
- Prompt content is the owner's (`PromptData`, HUD H1 l.84); the Sim never formats strings (manual-delivery MD10 l.136); no concatenated addresses, money through a `{money}` formatter, never a literal "¢" (manual-delivery l.432; HUD H2 l.89, H-WIDGET-2 l.595).
- No new TD rows and no edits outside this ADR and the hud-notifications epic (this worktree's remit).

### Requirements
- TR-hud-001 (Presentation-only, Sim has no reference, `docs/architecture/tr-registry.yaml` l.3114), TR-hud-002 (plain-C# UI core, xUnit-testable, l.3128 — the `IHudState` + `IMoneyFormatter` half), TR-hud-005 (timer monotone within `(phaseId, deadlineTick)`, text written on change only, l.3170), TR-hud-012 (all money through one `IMoneyFormatter`, lint bans currency literals, l.3268), TR-hud-015 (1080-reference px scaled by height, ≥ 18 px, min 1280×720, l.3310 — the stretch half is ADR-0019 §7's), TR-hud-016 (0 alloc over idle frames, no `DrawLine`, l.3324 — the 0 B and no-line halves; the budget is advisory), TR-delivery-019 (prompt content as data, HUD renders, l.2414 — the render half).
- GDD acceptance ids this slice serves: H-WIDGET-1 (M-1a row, narrowed — §2.1), H-WIDGET-2, H-F3-1 (time subset), H-PROMPT-1 (the rows §4 lists), H-PERF-3 (ADVISORY at M-1a), spec 12 l.24.

## Decision

### 1. Where it lives: `PerformativeMail.Client.Core.Ui` + `src/Game/Hud/`

- The Godot-free part is the namespace `PerformativeMail.Client.Core.Ui` in `src/PerformativeMail.Client.Core/Ui/` (`docs/architecture/architecture.md` l.146 "HUD/UI core (`Ui/`…)", l.161 "The HUD seed's `Ui.Core` becomes the namespace `PerformativeMail.Client.Core.Ui`"). It holds `IHudState` and its implementation `HudState`, `IMoneyFormatter` and `EnMoneyFormatter`, the F3 timer function, the prompt decoder and the M-1a English string table. It references the Sim read-only (`Cents`, `StringId`, `IInteractionQuery`, `InteractionSnapshot`, `PromptArgs`, the `DeliverPromptArgs` helper of manual-delivery story 010, the mirror read interfaces of §2) and nothing from Godot (ADR-0001; control manifest l.244).
- The Godot part is `src/Game/Hud/` (the folder player-character story 026 already uses for `FocusFeed.cs`, `production/epics/player-character/story-026-interaction-prompt-anchor-projection.md` l.51): the `Persistent` CanvasLayer script and the three views (§4).
- Tests: `tests/unit/ui/**` for the Client.Core part (the test project already references Client.Core, `tests/PerformativeMail.Sim.Tests/*.csproj` l.42); smoke scenarios in `src/Game/Smoke/` (godot-host-shell story 002's harness) for the Godot part.

### 2. The M-1a `IHudState` read model (pull values only)

H30 defines `IHudState` as pull values plus one ordered event stream (`design/gdd/hud-notifications.md` l.266). **This slice implements the pull-value half for three values and defers the event stream** (§6). From M-1a the HUD reads only through `IHudState` (H30; TR-hud-003 is ADR-0016's and is unchanged: no HUD code reads a mirror, the snapshot or the Sim directly except through this one object).

**2.1 Members.**

| Member | Source | Rule |
|---|---|---|
| `Cents WalletCents` | `WalletMirror` (ADR-0016 l.86; mail-destinations story 010 AC-1 "reports the last `WalletChanged.balance`") | a value display, never formatted here; it is **not** an `[OutcomeBeat]` — the mirror is written by `MirrorDrain` at the client tick after the batch arrives, so it is never ahead of the domain event (ADR-0016 l.96, l.131). The wallet *bump* and the own pill are outcome beats and are the M0+ amendment's. |
| `ShiftTimer Timer` | `RunMirror` `phase`, `deadlineTick` (ADR-0016 l.87; mail-destinations story 009 AC-3 "`deadlineTick` is set at Delivery start (the HUD timer reads it through `RunMirror`, story 010)") + `IRenderClock.RenderSimTimeMs` (ADR-0016 l.167) | F3 time-only (§3). |
| `DeliverPrompt Prompt` | `IInteractionQuery.Current` (ADR-0014 l.115, l.157): `Focused`, `Verb`, `InteractMode`, `Availability`, `PromptArgs` (decoded with manual-delivery story 010's `DeliverPromptArgs` helper), `InteractState`, `ProgressPm`, `Seq` | content only; placement and visibility rules are §4.3. `ProgressPm` and `InteractState` are exposed for the M0+ hold ring; this slice draws no ring. |
| `HudDirty Dirty` | computed by `Refresh` | flags `{ Wallet, Timer, Prompt }` set iff the member changed in this `Refresh`; views write text only when their flag is set, and once unconditionally at `Initialize` (so a view registered later still shows the current value). |
| `void Refresh()` | called once per frame by the `Persistent` layer node before any view presents (§4.2) | reads the three sources, recomputes F3, decodes the prompt, sets `Dirty`; allocates 0 B when nothing changed. |

**2.2 Reading the mirrors.** `WalletMirror` and `RunMirror` are Sim-side mirror parts (`Sim/Economy/Client/`, `Sim/Run/Client/`, mail-destinations story 010). `HudState` takes them by constructor through read-only interfaces `IWalletRead { Cents Balance { get; } }` and `IRunRead { byte Phase { get; } uint DeadlineTick { get; } uint CloseTick { get; } }`. If story 010 has not exposed such interfaces when story 001 starts, story 001 adds them additively beside the mirrors (the shape player-character story 026 uses for `IAnchorPoints`, l.51 "Keep this additive and tell the HUD owner") and records it. *(Mail Lead planning default, pending Evan.)*

**2.3 Prompt content, not presentation.** The read model copies the 32 B `PromptArgs` by value from the snapshot (ADR-0014 l.115 "owner-supplied, 32 B"; player-character story 018 AC "Fields": "`PromptArgs` = whatever `IInteractable.WritePrompt` wrote (32 B, copied by value)") and decodes it through the single `DeliverPromptArgs` helper manual-delivery story 010 defines in `Sim/Delivery` (its Implementation Notes: branch u8 at 0, flags u8 at 1, key `StringId` at 2–5, address `StringId` at 6–9, unit count u16 at 10–11, predicted pay i32 at 12–15, predicted penalty i32 at 16–19). There is one layout definition; the HUD never re-declares offsets. A snapshot whose `Verb` is not Deliver yields `Prompt.Branch = None` and the framework reason key only (`FrameworkReasons`, `src/PerformativeMail.Sim/Players/FrameworkReasons.cs`).

### 3. Money formatter and the shift timer function (both pure, Client.Core)

**3.1 `IMoneyFormatter`.** `string Format(Cents amount, MoneySign sign)` with `MoneySign { Balance, Delta }`. `Balance` shows a minus only when negative; `Delta` shows "+" or "−" and no sign for zero (the GDD's rows "+86¢", "−4¢", "0¢": `design/gdd/manual-delivery.md` l.412, l.416, l.418). **Every wallet and prompt money string is produced by this one call site** (H-WIDGET-2 l.595; TR-hud-012). The M-1a `EnMoneyFormatter` renders `{sign}{integer}¢` — integer cents, no grouping — because that is the only money format any GDD shows and the format itself is Economy's (HN-6, `design/gdd/hud-notifications.md` l.648, "before M-1a strings freeze"). The "¢" (U+00A2) and the minus (U+2212, as the GDD prints it; the formatter owns the choice) exist **only** inside the formatter. A fake formatter in tests records its calls (H-WIDGET-2's "(−500)" for the floor). A lint over `src/Game/Hud/**` and `Client.Core/Ui/**` (excluding the formatter file) fails on a "¢" or a currency literal (H-WIDGET-2; TR-hud-012). *(Mail Lead planning default, pending Evan: the `{sign}{integer}¢` format and the `Balance`/`Delta` sign rule.)*

**3.2 Shift timer, F3 time subset.** `ShiftTimerF3.Step(in ShiftTimer prev, byte phase, uint deadlineTick, long renderSimTimeMs) → ShiftTimer` implements `design/gdd/hud-notifications.md` F3 (l.350–365) exactly:
- `deadlineMs = deadlineTick × 1000 / TickRate` (integer; `TickRate` from simulation-core story 006, equal to the GDD's 30), `remMs = max(0, deadlineMs − renderSimTimeMs)`, `disp = ceil(remMs / 1000)`;
- monotone guard: if `key == prevKey` then `disp = min(prevDisp, disp)`, `key = (phase, deadlineTick)` (the stub's `phase` byte is the GDD's `phaseId`; story 009's `DeliveriesState{phase, …, deadlineTick}`);
- "00:00" only when `remMs == 0`; a 1–999 ms remainder shows "00:01" (l.363); text is produced only when `disp` changes (TR-hud-005);
- `Visible = deadlineTick > 0` — before the stub has set a deadline the timer is hidden; after the deadline it shows "00:00" (F3: never earlier). *(Mail Lead planning default, pending Evan — the stub's Prep deadline is not in a GDD.)*
- Text is `mm:ss` from `disp` (the digits of "Shift 3 / 5 · DELIVERY 02:14", l.176, whose prefix is M1), minutes not clamped. Amber / Red states and the icon are M1 (l.284, H-F3-1 l.613 "(M1) states").

### 4. Composition under `Main/Hud` (Godot)

**4.1 One layer.** `Main/Hud` is `HudPresenter` (ADR-0019 l.104). This slice adds one `CanvasLayer` child, `Main/Hud/Persistent`, registered with `HudPresenter.AddLayer(layer, order)` (godot-host-shell story 006 "Present order and seams"). Orders are the positions in ADR-0019 l.104–105's seed list (MarkerLayer 0 → persistent 1 → notifications 2 → panels 3 → chat/wheel 4 → banners 5 → pause 6 → debug 7); only `Persistent` (1) exists at M-1a, and throwing-catching 018's `AimLayer` sits below it. The prompt therefore draws above every marker (H12 l.203 "the landing marker (Throwing) and prompt above every marker"). *(Mail Lead planning default, pending Evan: the ordinal assignment.)*

**4.2 Refresh before views.** The `Persistent` layer's script (`HudLayer.cs`) implements `IInjectable<PresenterDeps>` and `IPresentedView` (story 006) and is first in tree order under `Main/Hud`; `HudPresenter` registers descendants in tree order and presents them in registration order (story 006), so `HudLayer.Present` calls `IHudState.Refresh()` and every view under it presents afterwards in the same frame. `HudPresenter` itself is not edited. `IHudState` reaches the views through `PresenterDeps` (story 002 adds a `HudState` field if absent and records it, the "if absent" shape story 006 itself uses). *(Mail Lead planning default, pending Evan.)*

**4.3 The three views** (`src/Game/Hud/WalletView.cs`, `ShiftTimerView.cs`, `DeliverPromptView.cs`), each a `Control` with `FocusMode = None` (ADR-0012 l.129), inert until `Initialize(deps)` (ADR-0019 l.257), animating nothing (persistent widgets are quiet: `design/gdd/hud-notifications.md` l.505 "What never moves: timer digits … address text"; l.522 "Persistent widgets are quiet"):

| View | Writes | When | Layout (1080-ref px; ADR-0019 §7 makes ref px = design px) |
|---|---|---|---|
| `WalletView` | one `Label` ← `IMoneyFormatter.Format(WalletCents, Balance)` | `Dirty.Wallet` | top-right column, inside the 48 px side / 32 px top safe area (`design/gdd/hud-notifications.md` l.531–532; seed l.28 PROPOSED); text ≥ 20 ref px (H23 l.236) |
| `ShiftTimerView` | one `Label` ← `mm:ss`; hidden while `!Timer.Visible` | `Dirty.Timer` | top-left, y 32 (seed l.26 PROPOSED); digits ≥ 28 ref px, tabular figures (H23 l.236; `tnum` VERIFY-IN-ENGINE) |
| `DeliverPromptView` | ≤ 4 `Label` rows from the string table (§4.4) | `Dirty.Prompt` | **screen-fixed**, centred, top edge 96 ref px under the reticle, max 720 wide, text ≥ 18 ref px on a backing plate, no leader line (H11 l.192); reticle = viewport centre at M-1a *(planning default, pending Evan, until a camera story publishes a reticle point)* |

**Prompt visibility and what the view adds on top of player-character 026.** Story 026 owns projection: `FocusFeed { Visible, Kind, ScreenPos, ScreenBounds, PresentedSeq, … }` (its AC "`FocusFeed` contract"). This ADR does **not** re-project and does **not** position the prompt from `ScreenPos`: HUD H11 is a logged decision that the prompt is screen-fixed (l.192 "[D, reconciliation]"), H12 says "The prompt is screen-fixed (H11) and unaffected; only the bracket disappears" (l.203), H-PROMPT-1 (M-1a) requires "an anchor behind the camera leaves the prompt in place with no bracket" (l.597), and story 026 itself says the widget is the HUD's and the prompt is screen-fixed (l.11, l.54). So the prompt is shown iff the snapshot has a focused anchor (`Focused != default`) and hidden otherwise; the feed's `Visible`/`ScreenBounds` drive only the bracket and caret, which are deferred (§6; story 026's debug overlay stands in at the greybox). What the HUD prompt view **adds**: decoding the key and arguments to English rows, the ✓ / ✗ mark, H11 placement, dirty-flagged `Label` writes, and a parity check that `feed.PresentedSeq == Prompt.Seq` in the frame (both are built after the last client tick of the frame, World before Hud — ADR-0019 l.111; story 026 "Allocation and order") — the feed's `PresentedSeq` remains the one the router echoes (story 026; ADR-0014 l.115); the HUD publishes no second one. *(Mail Lead planning default, pending Evan: screen-fixed per H11 rather than world-anchored via 026's `ScreenPos`.)*

**4.4 The M-1a English string table and the match indicator.** `HudStringsEn` (Client.Core.Ui, a code table like `BuiltInActionMap`, ADR-0012 l.70) maps each prompt key `StringId` (`StringNamespace.Reason`, decision log 2026-10-22; the key spellings of `design/gdd/manual-delivery.md` l.432 and PCM-UI-1's `reason.*`) to a format with rich tokens `{glyph}`, `{address}`, `{money}`, `{pieces}`, `{n}`; and each `Verb` to its English name. Line 1 follows Input & Controls' grammar with the mark leading — `✓ Hold [E] Deliver · 17 Elm St` / `✗ Hold [E] Deliver · 17 Elm St` (manual-delivery l.408; H11 l.193). The mark is the **address-match indicator** spec 12 l.24 names: ✓ for branch MATCH, ✗ for MISDELIVER_ONE, from the decoded branch byte — never from focus (story 026 "the feed never invents a verdict (no green/red — focus must not pre-announce a match)"), and a MISDELIVER_ONE body ("Wrong house — piece is for P" / "Posts 1 · −N¢", l.418) is never hidden (H-PROMPT-1 l.597). REJECT, NOTHING and the framework reasons render as the GDD's single lines (l.421–424) without icon assets at M-1a; lock and slash icons, chips, the hazard ring and the reason strip are deferred (§6). `{address}` resolves through `IWorldDisplayText.TryGetDisplayText(StringId, out string)` — the existing `WorldTables.TryGetDisplayText` (`src/PerformativeMail.Sim/World/WorldTables.cs` l.405–415) behind a read-only interface added by story 001 *(planning default, pending Evan; `IWorldQuery` is not widened)* — and is never truncated or concatenated (l.431–432). `{money}` is §3.1's formatter. `{glyph}` is the Interact binding's physical-input name from `BuiltInActionMap` (input-controls story 005, Complete; ADR-0012 l.136 names the physical name as the fallback) until input-controls story 017's `router.Glyphs` lands; the `GlyphSwap` log record (H-INT-3) is deferred with the event log (§6). *(Planning default, pending Evan.)*

**4.5 Frame hygiene.** Every widget is dirty-flagged; text is set only when its value changes (the timer once per second, the wallet on change) and formatted strings are cached (seed H29 l.250; H-PERF-3 l.631). Steady state: `Refresh` + three `Present`s allocate 0 B and write 0 text properties (ADVISORY at M-1a, BLOCKING from M0 — H-PERF-3). No `DrawLine`/`DrawPolyline`; the prompt plate is a `StyleBox` (ADR-0019 l.262; `design/gdd/hud-notifications.md` l.504). No per-frame `GetChildren()`/LINQ (story 006 engine note).

### 5. Accessibility: not in this slice

The screen-reader settle gate (manual-delivery MD4 / F5 / M-MD4-1e), its announcement-fired seam, the live-region Controls and the announcer are **deferred**, not decided here. Evan, 2026-10-23 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". The gate and the old story 010 AC-2 now live in `production/epics/manual-delivery/story-012-deferred-prompt-accessibility-localisation.md` (Deferred). The amendment that un-defers it defines the seam (§6).

### 6. Deferred — the M0+ amendment of this ADR (not decided here)

Everything the planned ADR-0018 row owns (`docs/architecture/architecture.md` l.622; `design/gdd/hud-notifications.md` l.669; seed §5 l.287–291) and this slice does not, **by name**:

- the **cue record and cue table** (H2; TR-hud-006; the table's load-time validation stays ADR-0010's TR-hud-007 and is simply not consumed yet);
- the **notification queue**: toast lane and the one M-1a toast slot, merge predicate, eviction, F1 reading-time lifetimes, `IUiClock` (TR-hud-009, 010, 021; H-TOAST-1, H-MERGE-1, H-F1-1/2, H-EC-1);
- the **event feed** (M0) and `IRoster`;
- **pills** — team pills (M0) and the M-1a **own pills** with the cue-pairing CI (H4/H8, H-MERGE-3, H-INT-1, H-FT-2b; TR-hud-008);
- **MarkerLayer beyond the prompt**: the focus bracket and too-far caret (H12; story 026's debug overlay stands in), name tags, pings, pile beacons, Lost Parcel markers (TR-hud-013);
- the **comms seam** (TR-comms-007/008);
- the **UI event log** records for HUD decisions (H25; TR-hud-011 — the `UiEventLog` ring itself exists under ADR-0012 §9 l.142), including `GlyphSwap` (H-INT-3);
- the **flash governor** and animator (TR-hud-014), **history** (TR-hud-018), the **Essentials** preference, context modes, Compact layout, settings consumed from Persistence (TR-hud-019, ADR-0024);
- the **hold ring**, slash overlay, lock icon, chips, hazard ring and the **reason strip** (H11; H-PROMPT-1's ring and strip rows); the panel-footer relocation of the prompt (H11 "Panel open"); the **hotbar frame** and **held-entry chip** (H-WIDGET-1 M-1a row; Grid Inventory R11); H-ZONE-1;
- the `IHudState` **ordered event stream** half (H30) and every other pull value (quota, complaint, `backlogUnits`, roster);
- the **two live-region Controls**, the announcer, F4, the announcement policy **and the announcement-fired seam for M-MD4-1e** (H26; TR-hud-017; TR-delivery-020; M-UI-3; deferred by Evan 2026-10-23, §5); timer Amber / Red states (M1).

**Why the M-1a widget set is narrower than H-WIDGET-1's M-1a row** (l.594: prompt, focus bracket, wallet, own pills, held chip, hotbar frame, one toast slot, time-only timer): Evan's "add a minimal HUD" is spec 12's three widgets (l.24). The other five are deferred to the amendment, with story 026's debug overlay covering the bracket and the manual-delivery hand reach echo (manual-delivery story 011; ADR-0016 l.150) giving hold feedback in-world. *(Mail Lead planning default, pending Evan.)*

The amendment may **add** members, views, layers and sections. It **must not** change §2–§4 semantics without superseding this ADR.

### Architecture

```
 Sim (ClientLayer, per client tick)                Client.Core.Ui (per frame)            src/Game (per frame, Present: World → Hud → Audio)
  WalletMirror ──IWalletRead──────────┐
  RunMirror ─────IRunRead─────────────┼──► HudState.Refresh()  ──Dirty──►  Main/Hud/Persistent (CanvasLayer, HudLayer: refresh first)
  IInteractionQuery.Current ──────────┤      ├ ShiftTimerF3 (F3)                 ├─ WalletView        (Label ← IMoneyFormatter)
  (PromptArgs 32 B, DeliverPromptArgs)│      ├ DeliverPromptArgs decode          ├─ ShiftTimerView    (Label mm:ss)
  IRenderClock.RenderSimTimeMs ───────┘      └ HudStringsEn + EnMoneyFormatter    └─ DeliverPromptView (≤ 4 Labels; ✓/✗ mark; H11 fixed)
  WorldTables ──IWorldDisplayText──► {address}                                     FocusFeed (story 026): PresentedSeq parity only
```

### Key Interfaces

```csharp
namespace PerformativeMail.Client.Core.Ui;
public interface IHudState                         // M-1a members; H30's remaining values and the event stream are the M0+ amendment's
{
    Cents WalletCents { get; }
    ShiftTimer Timer { get; }                      // { bool Visible; int DisplaySeconds; byte Phase; uint DeadlineTick; }
    DeliverPrompt Prompt { get; }                  // { bool Shown; Verb Verb; InteractMode Mode; StringId Key; DeliverBranch Branch (None|Match|MisdeliverOne|Reject|Nothing|Unavailable);
                                                   //   StringId Address; ushort Units; Cents PredictedPay; Cents PredictedPenalty; byte Flags; InteractState State; ushort ProgressPm; ushort Seq; }
    HudDirty Dirty { get; }                        // [Flags] { None = 0, Wallet = 1, Timer = 2, Prompt = 4 } — set by Refresh for this frame only
    void Refresh();                                // once per frame, before any view presents; 0 B when nothing changed
}
public interface IMoneyFormatter { string Format(Cents amount, MoneySign sign); }
public enum MoneySign : byte { Balance, Delta }
public static class ShiftTimerF3 { public static ShiftTimer Step(in ShiftTimer prev, byte phase, uint deadlineTick, long renderSimTimeMs); }
public static class HudStringsEn { public static string Format(StringId key); public static string VerbName(Verb v); }   // code table, M-1a English

namespace PerformativeMail.Sim.Economy.Client;  public interface IWalletRead { Cents Balance { get; } }                        // added if mail-destinations 010 has none
namespace PerformativeMail.Sim.Run.Client;      public interface IRunRead { byte Phase { get; } uint DeadlineTick { get; } uint CloseTick { get; } }
namespace PerformativeMail.Sim.World;           public interface IWorldDisplayText { bool TryGetDisplayText(StringId id, out string text); } // WorldTables implements


// src/Game/Hud (Godot)
public partial class HudLayer : CanvasLayer, IInjectable<PresenterDeps>, IPresentedView { /* first in tree order; Present → IHudState.Refresh() */ }
public partial class WalletView : Control, IInjectable<PresenterDeps>, IPresentedView { }         // FocusMode = None
public partial class ShiftTimerView : Control, IInjectable<PresenterDeps>, IPresentedView { }
public partial class DeliverPromptView : Control, IInjectable<PresenterDeps>, IPresentedView { }  // reads IHudFocusFeed (story 026) for PresentedSeq parity only
```

### Implementation Guidelines
- HUD code **must** read wallet, timer and prompt only through `IHudState`; it **must never** read `SimWorld`, `ClientLayer`, a mirror part or `IInteractionQuery` directly (H30; ADR-0003 §6; control manifest l.522).
- Every money string **must** come from `IMoneyFormatter.Format`; no "¢" or currency literal **may** appear outside `EnMoneyFormatter` (lint).
- The timer **must** derive from `IRenderClock.RenderSimTimeMs`; `delta`, Godot `Timer` and `Time.GetTicksMsec` are banned (ADR-0016 l.243; control manifest l.511). The F3 monotone guard **must** be keyed on `(phase, deadlineTick)`.
- The prompt **must** be screen-fixed per H11 and shown iff the snapshot has a focused anchor; the ✓ / ✗ mark **must** come from the decoded branch byte, never from focus; a MISDELIVER_ONE body **must never** be hidden.
- `PromptArgs` **must** be decoded only through manual-delivery story 010's `DeliverPromptArgs` helper; the HUD **must not** declare offsets.
- Views **must** be inert until `Initialize(deps)`, use `FocusMode = None`, write text only on a dirty flag (plus once at `Initialize`), and allocate 0 B in steady state. No `DrawLine`/`DrawPolyline`.
- Nothing in this slice **may** start an outcome beat (ADR-0016 l.135–155); the wallet readout is a value, not a beat.

## Alternatives Considered

### Alternative 1: Write the full ADR-0018 now (cue table, queue, markers, live region)
- **Pros**: One decision; stories for pills and toasts could be written at once.
- **Cons**: Weeks of design against systems with no GDD (Economy, Run Structure, Comms frozen) for a greybox whose fun test needs three facts. Contradicts Evan's "minimal HUD" scope.
- **Rejection Reason**: Spec 12 l.24 and the 2026-10-23 approval define the slice; ADR-0026 l.15 is the precedent for a sliced acceptance.

### Alternative 2: Draw the three widgets directly from the mirrors and the snapshot in Godot (no Client.Core read model)
- **Pros**: Fewer types; fastest to a screenshot.
- **Cons**: Breaks H30 "the HUD reads only through `IHudState`" and TR-hud-002's headless-testable UI core; F3 and the formatter would be untestable without Godot; the M0+ amendment would be a rewrite (H30 l.266 "so M0 is not a rewrite").
- **Rejection Reason**: The read model is small (three values) and is the seam the amendment extends.

### Alternative 3: World-anchored prompt positioned from story 026's `ScreenPos`
- **Pros**: Matches a literal reading of "positioned via 026's projection"; the prompt visibly follows the mailbox.
- **Cons**: Contradicts HUD H11 [D] (screen-fixed, l.192), H12 (l.203), H-PROMPT-1's behind-the-camera row (l.597) and story 026's own scope note (l.11, l.54); edge-clamping and a leader line would follow.
- **Rejection Reason**: The GDD decision is logged and story 026 defers to it. Recorded as a planning default so Evan can overrule.

## Consequences

### Positive
- The greybox gets wallet, shift time and the ✓ / ✗ prompt behind the seams the full HUD will keep (`IHudState`, `IMoneyFormatter`, `Main/Hud/Persistent`).
- Manual-delivery story 010's prompt data (Ready) gets its renderer.
- No Accepted ADR is amended: ADR-0016's `ClientPorts`, `IHostClock` and `IRenderClock`, ADR-0019's tree and order, ADR-0014's snapshot and ADR-0012's log ring are used as written.

### Negative
- Until the amendment the greybox has no toasts, pills, bracket, hold ring, hotbar frame or held chip — five of H-WIDGET-1's M-1a widgets and several M-1a GDD acceptance ids (H-TOAST-1, H-MERGE-1/3, H-INT-1/2/3, H-EC-1, H-FT-2b, H-ZONE-1, the ring and strip rows of H-PROMPT-1) are not met by this slice. They are listed in §6 so `/architecture-review` sees the gap as deliberate.
- The English string table is code, not content; localisation (manual-delivery story 012, Deferred) replaces it later.

### Neutral
- `IHudState` grows additively at M0+; the three views stay.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| mail-destinations 010 lands without read-only mirror interfaces | Medium | Low | story 001 adds `IWalletRead` / `IRunRead` additively and records it (§2.2) |
| `Label.Text` setter allocates or re-lays out on an equal string | Medium | Low (H-PERF-3 is ADVISORY at M-1a) | the view compares before writing; smoke probe counts text writes |
| The prompt's string table drifts from manual-delivery's key set | Medium | Medium | one `DeliverPromptArgs` helper and a table-driven test that every key story 010 writes has a `HudStringsEn` row (story 003) |
| Evan overrules a planning default (placement, format, widget set) | Medium | Low | each default is labelled in the text and in the stories; changing one touches one section |

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| hud-notifications.md | H30 / TR-hud-003 (ADR-0016): HUD reads only through `IHudState` | §2 (pull half) |
| hud-notifications.md | H-WIDGET-1 (M-1a row, narrowed), spec 12 l.24 "Minimal HUD" | §4.3, §6 |
| hud-notifications.md | H-WIDGET-2 / TR-hud-012: one `IMoneyFormatter` call site; no "¢" literal | §3.1 |
| hud-notifications.md | F3, H-F3-1 (time subset), TR-hud-005: monotone timer, text on change | §3.2 |
| hud-notifications.md | TR-hud-004 (ADR-0016): timer from `RenderSimTimeMs`, no HUD clock | §3.2 |
| hud-notifications.md | H11 placement, H12 "prompt screen-fixed", H-PROMPT-1 (mark leads, ≤ 4 rows, ✗ never hidden, behind-camera row) | §4.3–§4.4 |
| hud-notifications.md | H23 floors (18 / 28 / 20 ref px), TR-hud-015 (stretch via ADR-0019 §7) | §4.3 |
| hud-notifications.md | H29 / H-PERF-3 / TR-hud-016: dirty flags, 0 B idle, no `DrawLine` | §4.5 |
| hud-notifications.md | TR-hud-001, TR-hud-002: Presentation-only, Godot-free testable core | §1 |
| manual-delivery.md | UI Requirements l.408–432, M-UI-1 (render half of TR-delivery-019): keys + args, `{address}`, `{money}`, no concatenation | §2.3, §4.4 |
| player-character-movement.md | UI Requirements "Interaction prompt", PCM-UI-1 (render half): framework reason lines | §2.3, §4.4 |

## Performance Implications
- **CPU**: `Refresh` is three struct reads, one F3 step and one 32 B decode per frame; views write only on change. Seed budget "Persistent widgets (dirty-flagged) 0.05 ms" (l.266, PROPOSED, unmeasured) is the advisory target.
- **Memory**: three `Label`s, one `StyleBox`, cached strings for the last timer / wallet / prompt texts. No pools at this slice.
- **Load Time**: none beyond scene instantiation.
- **Network**: none.

## Migration Plan
No HUD code exists. hud-notifications stories, in order:
1. story 001 — `IHudState`, `HudState`, `IMoneyFormatter`, `EnMoneyFormatter`, `ShiftTimerF3`, `HudStringsEn`, mirror read interfaces, `IWorldDisplayText` (Client.Core + additive Sim interfaces).
2. story 002 — `Main/Hud/Persistent`, `HudLayer`, `WalletView`, `ShiftTimerView`.
3. story 003 — `DeliverPromptView` with the ✓ / ✗ indicator over manual-delivery 010's keys.

**Rollback plan**: the three views and `HudLayer` are leaf nodes under `Main/Hud`; removing them and `Client.Core/Ui/` leaves every other system untouched.

## Validation Criteria
- [ ] `tests/unit/ui/`: F3 rows (1 ms → "00:01"; 0 → "00:00"; 134 s → "02:14"; same-key backward step never raises; key change may; frozen clock leaves `disp`) pass on net10.0 and net8.0.
- [ ] Fake `IMoneyFormatter` records every wallet and prompt money call; the lint finds no "¢" outside `EnMoneyFormatter`.
- [ ] 10,000 `Refresh` calls with unchanged inputs allocate 0 B and set no dirty flag (ADVISORY at M-1a; the 0 B test runs in Release, skips in Debug — the owner skip rule, `production/epics/player-character/story-016-press-resolution-tap-catch-armed.md` l.94).
- [ ] Smoke: `Main.tscn` boots with `Main/Hud/Persistent` and the three views, all `FocusMode.None`, inert before boot step 7 and initialised once after it (story 006's injection scenario extended).
- [ ] For every row of `design/gdd/manual-delivery.md` l.410–428 that story 010 writes, the prompt renders the mark, ≤ 4 rows, the address untruncated, and a MISDELIVER_ONE body is never hidden.

## GDD Impact (do not edit GDDs here; reconcile at re-review)
- **hud-notifications.md H-WIDGET-1 (l.594):** the M-1a row is read as the three spec-12 widgets now and the other five at the M0+ amendment (planning default, pending Evan).
- **hud-notifications.md H-PROMPT-1 (l.597):** the ring, strip and panel-footer rows are met at the amendment, not at M-1a.
- **hud-notifications.md H-INT-3, H-ZONE-1, H-TOAST-1, H-MERGE-1/3, H-INT-1/2, H-EC-1, H-FT-2b (M-1a set, l.568):** deferred with their surfaces (§6).
- **manual-delivery.md UI Requirements (l.406):** "HUD H11 owns placement" is applied literally (screen-fixed); the GDD's UX flag (l.434) still asks for `/ux-design` — the stories cite the GDD's normative rules and the seed's PROPOSED layout as the interim spec for the greybox (planning default, pending Evan).

## Related
- ADR-0012 (`FocusMode`, `UiEventLog`, glyphs), ADR-0014 (snapshot, `PromptArgs`), ADR-0016 (mirrors, render clock, outcome beats), ADR-0017 (`WalletChanged`, `Cents`), ADR-0019 (`Main/Hud`, stretch), ADR-0024 (settings consumed, M1), ADR-0026 (`Display` strings).
- `production/epics/hud-notifications/EPIC.md`; `production/epics/manual-delivery/story-010-prompt-content-as-data.md` (the `PromptArgs` layout), `story-012-deferred-prompt-accessibility-localisation.md`; `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md`, `story-026-interaction-prompt-anchor-projection.md`; `production/epics/mail-destinations/story-009-shift-stub-deliveries-state.md`, `story-010-client-mirrors-and-facts.md`; `production/epics/godot-host-shell/story-006-presenters-deps-injection-event-stream.md`.
- `design/gdd/hud-notifications.md` H1, H2, H10–H12, H23, H26, H30, F3, UI Requirements, Acceptance Criteria; `design/ux/hud-notifications-seed.md` §1, §5; `design/gdd/manual-delivery.md` MD4, F5, UI Requirements, M-MD4-1, M-UI-1; `design/spec/12-milestones.md` l.24.

## Amendments
- *(none — the M0+ amendment is planned in §6)*
