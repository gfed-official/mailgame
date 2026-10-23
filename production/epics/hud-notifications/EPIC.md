# Epic: HUD & Notifications (M-1a minimal slice)

> **Layer**: UI/Presentation
> **GDD**: design/gdd/hud-notifications.md (In Review — design-review 2026-10-16 revision; comms moved to comms.md, layout parked in design/ux/hud-notifications-seed.md)
> **Architecture Module**: Client.Core.Ui (`src/PerformativeMail.Client.Core/Ui/`: `IHudState`, `IMoneyFormatter`, F3, string table) + HUD views (`src/Game/Hud/`: one `CanvasLayer` under `Main/Hud`)
> **Milestone slice**: M-1a (minimal HUD, Evan 2026-10-23 7:13 AM PT) — Mail Lead chat: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". The minimal HUD is spec 12's "Minimal HUD: timer, wallet, interact prompt with address match indicator" (`design/spec/12-milestones.md` l.24). Everything else the GDD phases at M-1a (H-WIDGET-1 l.594: focus bracket, own pills, held chip, hotbar frame, one toast slot) is deferred to the M0+ amendment of ADR-0018 (ADR-0018 §6).
> **Engine Risk**: MEDIUM — `CanvasLayer`/`Control`/`Label` under ADR-0019 §7's stretch; `tnum` via `FontVariation` VERIFY-IN-ENGINE (seed l.299). No post-cutoff API in this slice (the live-region Controls are M4).
> **Est. size**: S — 3 stories
> **Implementation priority**: after `godot-host-shell` 005/006 and the feature epics' Sim stories it reads (see Stories)
> **Status**: Blocked — ADR-0018 is Proposed, awaiting Evan's acceptance (minimal M-1a slice drafted 2026-10-23)
> **Stories**: 3 stories — 3 Blocked (counts refreshed 2026-10-23 from the story headers, m1a-replan; order in `production/epics/implementation-order.md`) — see the Stories table below

## Overview

HUD & Notifications owns every screen-space widget and the notification pipeline (GDD H1 l.84), and owns no gameplay state. At M-1a this epic ships the **minimal** slice Evan approved on 2026-10-23: three persistent widgets, as ADR-0018 (Proposed) decides. Accessibility is deferred (Evan, 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example").

**In scope (M-1a minimal):**
- **`IHudState` read model (pull half of H30)** in `PerformativeMail.Client.Core.Ui`: wallet cents from `WalletMirror`, the F3 shift timer (time-only) from `RunMirror` + `IRenderClock.RenderSimTimeMs`, and the focused deliver prompt (key + typed arguments decoded from ADR-0014's 32 B `PromptArgs` through manual-delivery story 010's `DeliverPromptArgs` helper); dirty flags; 0 B per frame in steady state (ADR-0018 §2).
- **`IMoneyFormatter`** — one call site for every money string; no "¢" literal outside the formatter (H-WIDGET-2 l.595; ADR-0018 §3.1).
- **Composition under `Main/Hud`** (ADR-0019 §2): one `CanvasLayer` `Main/Hud/Persistent`, three dirty-flagged `Control` views injected through `Initialize(deps)` (godot-host-shell 006), `FocusMode = None` (ADR-0012 l.129), no `DrawLine` (ADR-0018 §4).
- **Wallet readout and time-only shift timer views** (H10 l.176, l.179; F3 l.350–365; H23 floors l.236).
- **Deliver prompt display** with the ✓ / ✗ address-match indicator leading line 1 (`design/gdd/manual-delivery.md` l.408; spec 12 l.24), screen-fixed per H11 (l.192), rendered from manual-delivery 010's keys and args with an M-1a English string table; player-character 026's `FocusFeed` is read for `PresentedSeq` parity only (ADR-0018 §4.3–§4.4).

**Out of scope (ADR-0018 §6, the M0+ amendment — by name):** the cue record and table, the notification queue (toast slot, merge, F1 lifetimes, `IUiClock`), the event feed and `IRoster`, own and team pills with the pairing CI, MarkerLayer beyond the prompt (focus bracket, too-far caret, name tags, pings), the comms seam, HUD event-log records (`GlyphSwap`), the flash governor, history, Essentials and context modes, the hold ring, slash/lock icons, chips, hazard ring and reason strip, the panel-footer prompt relocation, the hotbar frame and held chip, H-ZONE-1, the `IHudState` event stream, the two live-region Controls and announcer, timer Amber/Red states (M1), settings from Persistence (ADR-0024).

**Why these ADRs govern it.**
- ADR-0018 (Proposed) decides the read model, the formatter, and the layer and views — the three stories.
- ADR-0019 fixes `Main/Hud` (`HudPresenter`), the World → Hud → Audio order, `Initialize(deps)` and the 1080-reference stretch (§2, §7).
- ADR-0016 fixes the sources (`WalletMirror`, `RunMirror`, `IRenderClock.RenderSimTimeMs`) and the rule that nothing predictive starts an outcome beat.
- ADR-0014 fixes the snapshot and the 32 B `PromptArgs`; ADR-0012 fixes `FocusMode = None`, the glyph source and the `UiEventLog`.

**Risks.**
- (1) **ADR-0018 is Proposed.** Nothing here may start until Evan accepts it (ADR-0018 Status). All three stories are `Blocked` on that alone.
- (2) **No `/ux-design` HUD spec exists** (GDD UX flag l.536; player-character 026 l.11 "The widget's own story must wait for `/ux-design`"). The UI stories cite the GDD's normative rules (H11, H23, UI Requirements l.531–532) and the seed's PROPOSED layout (`design/ux/hud-notifications-seed.md` l.26–32) as the interim spec for the greybox — **Mail Lead planning default, pending Evan** (recommended: proceed; the greybox widgets are three labels and a plate).
- (3) **Upstream Sim stories are Ready, not Complete** (mail-destinations 009/010, player-character 017/018/026, manual-delivery 003/010, godot-host-shell 005/006). Story order below respects them; none is blocked by this epic.
- (4) **GDD M-1a acceptance ids not met by this slice** (H-TOAST-1, H-MERGE-1/3, H-INT-1/2/3, H-EC-1, H-FT-2b, H-ZONE-1, the ring/strip rows of H-PROMPT-1) are deferred deliberately (ADR-0018 §6, GDD Impact) so `/architecture-review` sees the gap as a decision.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-0018: HUD Composition — minimal M-1a slice (**Proposed — awaiting Evan's acceptance**) | `IHudState` (wallet, F3 timer, deliver prompt), `IMoneyFormatter`, `Main/Hud/Persistent` + three dirty-flagged views; cue table / queue / feed / pills / markers / comms / accessibility deferred to the M0+ amendment | MEDIUM |
| ADR-0019: Godot presentation scene architecture | `Main/Hud` (`HudPresenter`), presenters World → Hud → Audio, views inert until `Initialize(deps)`, `canvas_items`/`keep_height` 1080-reference stretch, no `DrawLine` | MEDIUM |
| ADR-0016: ClientLayer, previews & presentation timing | `WalletMirror`/`RunMirror` mirror parts; `IRenderClock.RenderSimTimeMs`; outcome beats only from `PresentationEventStream` cursors; "HUD timers → ADR-0018" | LOW |
| ADR-0014: Interaction framework & hold timing | `IInteractionQuery.Current`, `InteractionSnapshot.PromptArgs` (owner-supplied, 32 B), `WritePrompt` | LOW |
| ADR-0012: Input router core & Godot input adapter | HUD Controls `FocusMode = None`; glyph data through the router; `UiEventLog` ring | LOW |
| ADR-0017: Mail table, registry & `IWalletGate` | `WalletChanged{balance, delta, reason}`; `Cents` | LOW |

## GDD Requirements

Source: `docs/architecture/tr-registry.yaml` (TR-hud-001…021 l.3114–3407; TR-delivery-019/020 l.2414–2441). ADR-0018 is **Proposed**, so every row below that cites it is "No Accepted ADR" until acceptance.

| TR-ID | Requirement | ADR Coverage | Slice |
|-------|-------------|--------------|-------|
| TR-hud-001 | HUD Presentation-only; no gameplay state; Sim has no reference to it | ADR-0018 §1 (Proposed) | M-1a |
| TR-hud-002 | Plain-C# UI core xUnit-testable via `IUiClock`/`IRoster`/`IHudState`/`IMoneyFormatter` + Godot view | ADR-0018 §1–§3 (Proposed) — `IHudState` + `IMoneyFormatter` half; `IUiClock`/`IRoster` deferred | M-1a (partial) |
| TR-hud-003 | HUD reads only via `IHudState` ordered by (sourceTick, opId); loopback from M-1a | ADR-0016 ✅ (Accepted) — pull half here; event stream deferred (ADR-0018 §6) | M-1a (partial) |
| TR-hud-004 | Timer from `IHostClock.renderSimTimeMs`; HUD runs no clock | ADR-0016 ✅ (`IRenderClock.RenderSimTimeMs`) | M-1a |
| TR-hud-005 | Timer monotone within (phaseId, deadlineTick); text written on change only | ADR-0018 §3.2 (Proposed) | M-1a |
| TR-hud-012 | All money text via one `IMoneyFormatter`; lint bans currency literals | ADR-0018 §3.1 (Proposed) | M-1a |
| TR-hud-015 | 1080-reference px scaled by height; text ≥ 18 px; min 1280×720 | ADR-0019 §7 ✅ (stretch) + ADR-0018 §4.3 (floors, Proposed) | M-1a |
| TR-hud-016 | 0 alloc over 10,000 idle frames; no `DrawLine`; budgets (proposed) | ADR-0018 §4.5 (Proposed) — 0 B and no-line halves; budgets ADVISORY | M-1a (partial) |
| TR-hud-017 | Two live-region Controls; schema M-1a, announcer M4 | Deferred (Evan 2026-10-23; ADR-0018 §5–§6) | M1+ |
| TR-delivery-019 | Prompt content as data from Sim; HUD renders; localisation keys, no concatenation | ADR-0014 ✅ (transport) + ADR-0018 §2.3, §4.4 (render, Proposed); data half is manual-delivery 010 | M-1a |
| TR-delivery-020 | Live-region announcements policy; `accessibility_live` API unsourced | Deferred (Evan 2026-10-23; manual-delivery story 012) | M1+ |
| TR-hud-006, 008, 009, 010, 011, 013, 014, 018, 021; TR-comms-007, 008 | cue record; pairing CI; toast lane; merge; event log; MarkerLayer; governor; history; F1; comms | ❌ deferred to the M0+ amendment of ADR-0018 (§6) | M0+ |
| TR-hud-007, 020 | cue table validated at load; `hud.tuning` | ADR-0010 ✅ — not consumed by this slice | M0+ |
| TR-hud-019 | Settings consumed from Persistence | ADR-0024 (M1 amendment) | M1 |

**Untraced (deferred by decision, not by omission):** TR-hud-006, 008–011, 013, 014, 018, 021; TR-comms-007/008 — ADR-0018 §6.

## Definition of Done

This epic's M-1a slice is complete when:
- ADR-0018 is Accepted and all three stories are implemented, reviewed and closed via `/story-done`
- The M-1a acceptance ids this slice serves pass: H-WIDGET-1 (the three widgets), H-WIDGET-2, H-F3-1 (time subset), H-PROMPT-1 (mark leads line 1; ≤ 4 rows; MISDELIVER_ONE body never hidden; anchor behind the camera leaves the prompt in place), M-UI-1 (render half: `{address}` first renders untruncated), H-PERF-3 (ADVISORY)
- Logic stories have passing test files under `tests/unit/ui/**` and `tests/unit/delivery/**` on net10.0 Debug and net8.0 Release (`.claude/rules/test-standards.md`)
- UI stories have a passing smoke scenario in `src/Game/Smoke/` and retained screenshots in `production/qa/evidence/hud/` (ADVISORY until a render rig exists, GDD l.565)

## Stories

*All three are `Blocked` for one reason only: ADR-0018 is Proposed. Acceptance alone flips them to Ready (`/story-readiness` re-run). Order 001 → 002 / 003.*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [`IHudState` read model (wallet, F3 timer, deliver prompt) + `IMoneyFormatter` + M-1a English string table (Client.Core)](story-001-hud-read-model-money-formatter.md) | Logic | Blocked (ADR-0018 Proposed) | ADR-0018 (Proposed), ADR-0016, ADR-0014 |
| 002 | [`Main/Hud/Persistent` layer, `HudLayer` refresh, `WalletView` and time-only `ShiftTimerView`](story-002-wallet-readout-shift-timer-view.md) | UI | Blocked (ADR-0018 Proposed) | ADR-0018 (Proposed), ADR-0019 |
| 003 | [`DeliverPromptView` — keys + args, ✓ / ✗ address-match indicator, H11 placement, 026 parity](story-003-deliver-prompt-display.md) | UI | Blocked (ADR-0018 Proposed) | ADR-0018 (Proposed), ADR-0019, ADR-0014 |

**Recommended order:** 001 (after mail-destinations 010 and player-character 018) → 002 (after godot-host-shell 005/006) and 003 (after manual-delivery 010, player-character 026, input-controls 005).

**Cross-epic notes:** manual-delivery story 010 (Ready, narrowed 2026-10-23) owns the `PromptArgs` layout and the `DeliverPromptArgs` helper story 003 decodes; manual-delivery story 012 (Deferred, Evan 2026-10-23) holds M-MD4-1e (the screen-reader settle gate and its seam), the `a11y.deliver.*` keys, the test locale and the no-concatenation lint; this epic has no accessibility story. player-character story 026 owns projection and `FocusFeed`; story 003 reads it for `PresentedSeq` parity only (ADR-0018 §4.3).

## Open Questions

- **OQ-HUD-1 (pending Evan):** the Mail Lead planning defaults ADR-0018 labels — the three-widget M-1a set (§6), the interim UX spec (risk 2), screen-fixed prompt placement (§4.3), the `{sign}{integer}¢` money format (§3.1), the timer hidden until `deadlineTick > 0` (§3.2), the read-only mirror interfaces and `IWorldDisplayText` (§2.2, §4.4), the layer ordinals and `PresenterDeps.HudState` (§4.1–§4.2), the glyph fallback (§4.4), the reticle at viewport centre (§4.3). Each is a one-section change if overruled. Accessibility (the M-MD4-1e seam, live regions) is not in this epic: deferred by Evan 2026-10-23 (manual-delivery 012).

## Next Step

**2026-10-23 (m1a-replan, playable-first):** Next, in `production/epics/implementation-order.md` order (track and wave in parentheses; Evan 2026-10-23 7:13 AM PT "reorder playable-first"): Blocked: 001, 002, 003. Earlier notes below are history.

Evan accepts ADR-0018 (`docs/architecture/adr-0018-hud-composition-minimal.md`; `technical-director` sets `Accepted` on his confirmation). Then `/story-readiness production/epics/hud-notifications/story-001-hud-read-model-money-formatter.md` and `/dev-story` in the recommended order once its Sim dependencies (mail-destinations 010, player-character 018) are Complete.
