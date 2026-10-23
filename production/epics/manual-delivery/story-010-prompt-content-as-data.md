# Story 010: Deliver prompt content as data — `WritePrompt` fills ADR-0014's 32 B `PromptArgs` (prompt key + typed arguments)

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~120 lines prod + ~150 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23. Narrowed and unblocked by Evan. 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". The story keeps only the prompt-data half, whose transport ADR-0014 already fixes (owner-supplied 32 B `PromptArgs`, l.115, `WritePrompt` l.144). The screen-reader settle gate (M-MD4-1e), the localisation test locale, the no-concatenation lint and the `a11y.deliver.*` keys moved to `story-012-deferred-prompt-accessibility-localisation.md`, which is **Deferred**. The HUD prompt display is `production/epics/hud-notifications/story-003-deliver-prompt-display.md`.
> Mail Review PR #47 B4 fix 2026-10-23: `StringNamespace.Reason` is not in the code (`StringNamespace` has `None, Item, Container, MailKind, DestinationType, MailMix, Run`; TD-119 Open) — AC-4 now adds the minimal constant and the `DeliverPromptKeys` table this story needs, and the Implementation Notes cite it instead of the unapplied decision-log entry. Second pass (same day, audit of the rewrite): the decision-log entry is dated 2026-10-17, not 2026-10-22 (ADR-0018 §4.4 carries the wrong date; ADRs are on hold, so it is noted here rather than fixed there).

> **History — BLOCKED 2026-10-16 to 2026-10-23: ADR-0018 not written.** The undecided parts were the HUD-facing key set, the argument layout, the strip lifetime and the announcement events. The first two are Manual Delivery's to fix, because ADR-0014 makes `PromptArgs` owner-supplied; they are below, as Mail Lead planning defaults, approved by Evan 2026-10-23 8:10 AM PT (TD-173). The last two are HUD or deferred work.

## Context

**GDD**: `design/gdd/manual-delivery.md` (UI Requirements branch table l.79–86; MD10 never builds address text; M-UI-1 data half)
**Requirement**: `TR-delivery-019` (prompt content as data from the Sim; HUD renders) — the data half
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-0014: Interaction Framework and Hold Timing — Accepted 2026-10-16 (primary: §7 `InteractionSnapshot.PromptArgs` "owner-supplied, 32 B" l.115; `IInteractable.WritePrompt(in InteractionContext, ref PromptArgs)` l.144); ADR-0004 (`StringId`)
**ADR Decision Summary**: The owner writes up to 32 B of prompt arguments per tick for the focused anchor. Player Character copies them into `InteractionSnapshot` (player-character story 018), and the HUD renders them. The Sim never formats a string.
**ADR Version**: ADR-0014 2026-10-16 + errata 2026-10-20/21/22; ADR-0004 2026-10-16

**Engine**: Godot 4.7.1 (none — Godot-free Sim) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature + Global)**:
- Required: prompt text is data (`StringId` keys + arguments); the Sim never formats strings (Global; MD10)
- Forbidden: concatenating address fragments; a literal "¢" in keys

---

## Acceptance Criteria

- [ ] **AC-1 (M-UI-1, data half).** For each branch row of the UI Requirements table (rows 1–6 plus MATCH, MISDELIVER_ONE and REJECT), `DeliverInteractable.WritePrompt` writes the row's key (`StringId`) and its typed arguments into `PromptArgs` in the layout below. The arguments are the address as a world `StringId`, counts, cents as integers and flags. A table-driven test decodes every row back to (key, args). No key contains a literal "¢".
- [ ] **AC-3 (one layout definition).** `DeliverPromptArgs.Write` and `DeliverPromptArgs.TryRead` in `Sim/Delivery` are the only code that reads or writes `PromptArgs` offsets for Deliver. A round-trip test writes every AC-1 row and reads back an equal value, and hud-notifications story 001 decodes through this helper rather than its own offsets.
- [ ] **AC-2 (size and purity).** Every write fits `PromptArgs.Size` (32), and the unused bytes are zero. `WritePrompt` is pure with respect to owner state (the `PureHookGuard` from `tests/unit/player/PlayersTestKit.cs`). After warm-up it allocates 0 B (this is ADR-0014 l.250's per-tick 0 B rule; not a new gate).
- [ ] **AC-4 (the key namespace — `StringNamespace.Reason` does not exist; TD-119).** `StringNamespace` (`src/PerformativeMail.Sim/Primitives/StringId.cs` l.7–29) holds `None, Item = 1, Container, MailKind, DestinationType, MailMix = 5, Run = 0xFF` and nothing for UI keys; the decision log's option A2 ("append `StringNamespace.Reason = 6`", `production/session-logs/decision-log.md` l.5849–5852, entry dated **2026-10-17**, dev-story player-character 013; there is no 2026-10-22 entry on this — ADR-0018 §4.4 cites that date in error), which ADR-0018 §4.4 (Accepted) relies on, was never applied, which is why `FrameworkReasons` (`src/PerformativeMail.Sim/Players/FrameworkReasons.cs`) takes its two ids injected. This story adds the minimal constant: `Reason = 6` appended to `StringNamespace` (append-only; no existing value moves) and `DeliverPromptKeys` in `src/PerformativeMail.Sim/Delivery/DeliverPromptKeys.cs` — a static class in the `FrameworkReasons` shape: for each key this story writes, one `static readonly StringId` built as `new StringId(StringNamespace.Reason, index)` plus its ASCII spelling constant (for example `MatchPiecesKey = "deliver.match.pieces"`). Indices 0–15 are reserved for the framework's `reason.*` keys (composition mints `FrameworkReasons` from them when TD-119's string-table half lands; this story does not touch `FrameworkReasons`); Deliver's keys take 16 upward in the order of GDD l.432 and are the only ids `WritePrompt` writes. A test asserts every `DeliverPromptKeys` id has `Ns == StringNamespace.Reason`, the indices are unique and ≥ 16, and the spellings equal the GDD's (none contains "¢"); hud-notifications story 001's `HudStringsEn` keys its formats on these ids. The content string table, localisation and the `FrameworkReasons` ids stay with **TD-119 (Open)**; this AC is the "UI-string namespace" half TD-119 names, and the ADR-0004 §6 erratum adding `Reason = 6` landed 2026-10-23 (Approved by Evan 9:08 AM PT). *(Mail Lead planning default, Approved by Evan 2026-10-23 9:08 AM PT.)*

---

## Implementation Notes

- **`PromptArgs` layout (Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)).** All values are little-endian; bytes not listed are 0.

  | Offset | Size | Field |
  |---|---|---|
  | 0 | u8 | branch: 1 MATCH, 2 MISDELIVER_ONE, 3 REJECT, 4 NOTHING, 5 UNAVAILABLE |
  | 1 | u8 | flags: bit0 `floorRefusal`, bit1 `wontFit`, bit2 `settling` |
  | 2–5 | `StringId` | key: namespace u8, reserved u8, index u16 |
  | 6–9 | `StringId` | address (world namespace) |
  | 10–11 | u16 | unit count |
  | 12–15 | i32 | predicted pay (cents) |
  | 16–19 | i32 | predicted penalty (cents) |
  | 20–31 | | reserved |

- The keys are the GDD's (for example `deliver.close_panel`, `deliver.dry_land`, `deliver.pending`, `mail.reject.closed`, `mail.reject.not_on_route`, `deliver.nothing`). They are declared by AC-4's `DeliverPromptKeys` (`src/PerformativeMail.Sim/Delivery/DeliverPromptKeys.cs`) as `StringId`s in the `StringNamespace.Reason` namespace that AC-4 appends to `src/PerformativeMail.Sim/Primitives/StringId.cs` (the decision log's 2026-10-17 option A2, l.5849–5852, never applied; TD-119). Spellings are the GDD's **Localisation keys** line, `design/gdd/manual-delivery.md` l.432. The values come from story 002's preview and story 003's availability.
- Consumer: `production/epics/hud-notifications/story-003-deliver-prompt-display.md` decodes this layout. It and this story share one `DeliverPromptArgs` read/write helper in `Sim/Delivery`, so the layout has a single definition.

---

## Out of Scope

- The screen-reader settle gate M-MD4-1e, the localisation test locale (`{address}` first), the concatenation lint and the `a11y.deliver.*` variant keys: `story-012-deferred-prompt-accessibility-localisation.md` (**Deferred**; the move of AC-1's localisation into 012 was approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)").
- Drawing the prompt: hud-notifications story 003. Anchor projection: player-character story 026.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* QL-STORY-READY: `/story-readiness` 2026-10-23 (m1a-replan).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/delivery/DeliverPromptDataTests.cs` must exist and pass.
- Test policy (`.claude/rules/test-standards.md`): during development run only this story's test classes on net10.0 Debug. At close, run the full suite once on net10.0 Debug and once on net8.0 Release. CI must be green on all four legs.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (preview), Story 003 (availability), Story 004 (`DeliverInteractable` press hooks); `production/epics/player-character/story-018-interaction-snapshot-iinteractionquery.md` (copies `PromptArgs` into the snapshot)
- Unlocks: `production/epics/hud-notifications/story-003-deliver-prompt-display.md`
