# Story 013: Input contexts, the verb × context table (P14 router declarations) and the framework availability gate

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Interaction-framework story (3 of 9) — the router declarations.** Input & Controls' action map and `RouterState` read the `BodyContext`, the verb × context table and the published `CarrierStatus` defined here. Mounted and Dead are declared but unused at M-1a.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P14 input contexts and verbs; P6 swimming restrictions; P11 "Panel computed for display only"; P12 context-change cancels; UI Requirements — reason keys)
**Requirement**: `TR-movement-011` (framework availability rules around the registry); P14 itself has **no TR-ID in the registry** — cited by GDD rule P14 and acceptance id PCM-P14-1 (also PCM-P6-2a/b/c, PCM-P11-2d)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 context filter, §4 context-change cancels, §7 publication) — Accepted 2026-10-16 (primary); ADR-0012: Input router core (§2 `RouterState` reads `body` — OnFoot/Build/Mounted/Dead — and `holding` from the ClientLayer-published carrier status) — Accepted 2026-10-16
**ADR Decision Summary**: Grid Inventory owns World, Panel and Holding; this system **adds Mounted and Dead**. Input & Controls owns bindings, remap and thresholds; this system declares which verbs are live in which context. The router's `body` dimension and `holding` are read each frame from the ClientLayer-published carrier status (life state, mount, cursor non-empty). Focus filtering by context (Panel → display only; Dead → none; Mounted → own seat's Dismount only; Build → `BuildModeOnly` anchors only) is ADR-0014 §2.
**ADR Version**: ADR-0014 2026-10-16; ADR-0012 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: focus uses the §2 integer score; context filters are data, not branches scattered in callers (ADR-0014 §2)
- Required: the two binding rules (one meaning per binding per context; no button with both a tap and a hold verb in one context) apply unchanged (GDD P14; ADR-0012 validator)
- Required: cancels on context change into Panel, Mounted or Dead are recorded with a reason, never silent (ADR-0014 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P14-1, PCM-P6-2a/b/c, PCM-P11-2d), scoped to this story:*

- [ ] **PCM-P14-1 (table-driven).** `VerbContextTable` declares the 8 verbs (Move, Look, Sprint, Jump, Interact, Shoulder swap, Comms wheel, Respawn) × 5 contexts (World, Panel, Holding, Mounted, Dead) exactly as GDD P14: Jump disabled in Panel; Interact disabled in Panel, Dismount-only in Mounted, absent in Dead; Look, quick ping and spectate **absent** in Dead; Sprint walks (disabled) in Panel and is absent in Dead; the comms wheel is live in every context except Panel (emotes greyed in Dead); **Respawn is bound nowhere** (no context enables it); no context has an action carrying both a tap and a hold verb except Interact. A test enumerates every cell against the table in the GDD and fails on any difference.
- [ ] **Contexts.** `BodyContext { OnFoot, Build, Mounted, Dead }` and the `InputContext { World, Panel, Holding, Mounted, Dead }` mapping to input-controls' `RouterState` are defined; the Panel cannot open while Mounted or Dead (a `CanOpenPanel(body)` predicate); Build is World with only Deconstruct (`BuildModeOnly`) anchors focusable.
- [ ] **Published `CarrierStatus`.** A readonly struct `{ LifeState Life; BodyContext Body; bool Swimming; bool CursorHolding; bool Aiming; ushort Seq }` is produced by the Players client step each tick (Story 020 publishes it) and is what the router reads; this story defines the type, its builder from (`PlayerRecord`, `CarrierState`, cursor-non-empty flag) and its mapping to `RouterState.body` / `holding` (Dead ⇒ not holding, Mounted ⇒ not holding — the "auto-stow" rule stays Grid Inventory's).
- [ ] **PCM-P11-2d.** The framework availability gate `InteractionGate.Evaluate(in anchor, in carrierStatus, panelOpen)` returns: Panel → `Unavailable(reason.close_panel)` (focus is computed for display only and Interact is refused); Dead → no candidates; Mounted → only the own-seat Dismount anchor is a candidate; Build → only `BuildModeOnly` anchors; otherwise `ok` (the owner's `IsAvailable` is evaluated after, never before, the gate).
- [ ] **PCM-P6-2a/b/c.** While `Swimming`: `throw`, `build`, `deliver`, tool use and `drop` are refused (the verb-owner side asks `InteractionGate.VerbAllowedWhileSwimming(verb)` / the table), and **every anchor** returns `Unavailable(reason.dry_land)` unless it carries `AllowWhileSwimming` (v1: only Open-on-pile and Pick-up-on-Spill-heap); hotbar select, the own panel, chat, ping and look remain allowed (table rows). A swimmer cannot start a Catch.
- [ ] **Reason keys.** The framework reasons are `StringId`s registered in content: `reason.dry_land`, `reason.close_panel`; each carries its named argument slot (`{verb}`) and the `Unavailable` result holds exactly one key; banned stems (die/dying/dead/death, kill, slain, corpse, revive, respawn, drown, game over) never appear in a player-facing key text (PCM-UI-3 — the string table check belongs to content; this story adds the test over its own keys).
- [ ] **Cancel-on-context-change.** `InteractionGate.ContextCancelReason(previousStatus, currentStatus)` returns a cancel reason when a hold's context changes into Panel, Mounted or Dead, or the carrier enters swim depth, or is knocked down; walking and non-lethal damage return none. (Story 017 calls it every held tick.)

---

## Implementation Notes

*Derived from GDD P14/P6/P11 and ADR-0014 §2/§4/§7, ADR-0012 §2:*

- Files in `src/PerformativeMail.Sim/Players/`: `BodyContext.cs`, `InputContext.cs`, `VerbContextTable.cs` (a `ReadOnlySpan<byte>` literal property of the 8 × 5 matrix with `Live / Disabled / Absent / Greyed` values), `CarrierStatus.cs`, `InteractionGate.cs`, `FrameworkReasons.cs`.
- `CarrierContextFlags` (Panel, Dead, Mounted, Build) is defined once, in Story 005, for `CarrierInput.ContextFlags`; import it — do not define a second enum.
- Grid Inventory owns World / Panel / Holding semantics (its UI rules); do not redefine them — import the context enum values and add Mounted/Dead (a hand-back amendment recorded in the GDD).
- Input & Controls owns concrete bindings, remap, thresholds, glyphs and the conflict validator; this table is *data they validate against*. The input-controls epic consumes `VerbContextTable` and `CarrierStatus` — `production/epics/input-controls/story-007-router-shell-press-machines-mute.md` and `production/epics/input-controls/story-008-router-state-transitions-and-passthrough.md` (which cites this story).
- Mounted/Dead are *unused at M-1a* (no vehicles, no knockdown): the types and table exist and are tested; no code path sets `BodyContext.Mounted` or `Dead` yet except test fixtures.
- `reason.close_panel` for the Panel context is the dimmed-prompt reason ("Close panel to deliver"); the prompt text itself is HUD's. `reason.dry_land` takes `{verb}`.
- Swimming-verb refusal for non-anchor verbs (`drop`, `throw`, tool use) is enforced by *their owners' op handlers* using the shared `Swimming` flag (Grid Inventory rejects `drop` with `swimming`, Throwing refuses throws) — this table is the single declaration they cite, not their enforcement.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Router state machine, action map, glyphs, remap UI: input-controls epic. Mounting, seats, the Dismount verb: Vehicles (M1). Knockdown / Dead state entry: M-1b.
- Story 014: using the gate inside focus scoring. Story 020: producing `CarrierStatus` each tick. Story 019: the host-side `Unavailable` in `CheckInteraction`.
- Localisation text for the reason keys (HUD / content).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/player/InputContextTableTests.cs` (`[Trait("Id", "PCM-P14-1")]`), `tests/unit/player/InteractionGateTests.cs` (`PCM-P11-2d`, `PCM-P6-2a`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`LifeState`, `PlayerRecord`), Story 012 (`AnchorDef.AnchorFlags`, `Verb`), `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`StringId` reason keys in built-in content)
- Unlocks: Stories 014, 017, 019, 020; input-controls epic (router `body` dimension, action-map validator rows — `production/epics/input-controls/story-008-router-state-transitions-and-passthrough.md`, `production/epics/input-controls/story-007-router-shell-press-machines-mute.md`); grid-inventory `drop`-while-swimming reason; throwing-catching swim refusal
