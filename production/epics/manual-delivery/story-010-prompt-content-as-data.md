# Story 010: Deliver prompt content as data — `WritePrompt` fills ADR-0014's 32 B `PromptArgs` (prompt key + typed arguments)

> **Epic**: Manual Delivery
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S (~0.5 day; ~120 lines prod + ~150 lines tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23. Narrowed and unblocked by Evan. 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". The story keeps only the prompt-data half, whose transport ADR-0014 already fixes (owner-supplied 32 B `PromptArgs`, l.115, `WritePrompt` l.144). The screen-reader settle gate (M-MD4-1e), the localisation test locale, the no-concatenation lint and the `a11y.deliver.*` keys moved to `story-012-deferred-prompt-accessibility-localisation.md`, which is **Deferred**. The HUD prompt display is `production/epics/hud-notifications/story-003-deliver-prompt-display.md`.

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

- The keys are the GDD's (for example `deliver.close_panel`, `deliver.dry_land`, `deliver.pending`, `mail.reject.closed`, `mail.reject.not_on_route`, `deliver.nothing`). Declare them as `StringId`s next to the existing `FrameworkReasons` pattern (decision log 2026-10-22, `StringNamespace.Reason`). The values come from story 002's preview and story 003's availability.
- Consumer: `production/epics/hud-notifications/story-003-deliver-prompt-display.md` decodes this layout. It and this story share one `DeliverPromptArgs` read/write helper in `Sim/Delivery`, so the layout has a single definition.

---

## Out of Scope

- The screen-reader settle gate M-MD4-1e, the localisation test locale (`{address}` first), the concatenation lint and the `a11y.deliver.*` variant keys: `story-012-deferred-prompt-accessibility-localisation.md` (**Deferred**, Evan 2026-10-23).
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
