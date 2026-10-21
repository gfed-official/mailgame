# Story 013: Input contexts, the verb × context table (P14 router declarations) and the framework availability gate

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: In Progress
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-20 (lead docs refresh: `PanelOpen`, life-state mapping, '—' = Absent, swim-Catch AC moved to story 016; OQ-013-1 closed by owner decision 2026-10-20 — `CarrierStatus.OccupiedSeat` per the ADR-0014 erratum 2026-10-20, Mounted branch now admits the own-seat Dismount; stamp 2026-10-17.1 — the 17.1 changes touch only the Godot input adapter and `WorldTablesBuilder`, and the ADR-0014 erratum changes no manifest rule)

> **Interaction-framework story (3 of 9) — the router declarations.** Input & Controls' action map and `RouterState` read the `BodyContext`, the verb × context table and the published `CarrierStatus` defined here. Mounted and Dead are declared but unused at M-1a.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P14 input contexts and verbs; P6 swimming restrictions; P11 "Panel computed for display only"; P12 context-change cancels; UI Requirements — reason keys)
**Requirement**: `TR-movement-011` (framework availability rules around the registry); P14 itself has **no TR-ID in the registry** — cited by GDD rule P14 and acceptance id PCM-P14-1 (also PCM-P6-2a/b/c, PCM-P11-2d)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 context filter, §4 context-change cancels, §7 publication) — Accepted 2026-10-16 (primary); ADR-0012: Input router core (§2 `RouterState` reads `body` — OnFoot/Build/Mounted/Dead — and `holding` from the ClientLayer-published carrier status) — Accepted 2026-10-16
**ADR Decision Summary**: Grid Inventory owns World, Panel and Holding; this system **adds Mounted and Dead**. Input & Controls owns bindings, remap and thresholds; this system declares which verbs are live in which context. The router's `body` dimension and `holding` are read each frame from the ClientLayer-published carrier status (life state, mount, cursor non-empty). Focus filtering by context (Panel → display only; Dead → none; Mounted → own seat's Dismount only; Build → `BuildModeOnly` anchors only) is ADR-0014 §2.
**ADR Version**: ADR-0014 2026-10-16 + 2026-10-20 erratum (§1/§2 `CarrierStatus.OccupiedSeat`, Mounted filter; §3 swim note); ADR-0012 2026-10-16 + 2026-10-17 §7 amendment (Godot input adapter only — no effect on this story)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: focus uses the §2 integer score; context filters are data, not branches scattered in callers (ADR-0014 §2)
- Required: the two binding rules (one meaning per binding per context; no button with both a tap and a hold verb in one context) apply unchanged (GDD P14; ADR-0012 validator)
- Required: cancels on context change into Panel, Mounted or Dead are recorded with a reason, never silent (ADR-0014 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P14-1, PCM-P6-2a/b/c, PCM-P11-2d), scoped to this story:*

- [ ] **PCM-P14-1 (table-driven).** `VerbContextTable` declares the 8 verbs (Move, Look, Sprint, Jump, Interact, Shoulder swap, Comms wheel, Respawn) × 5 contexts (World, Panel, Holding, Mounted, Dead) exactly as GDD P14: Jump disabled in Panel; Interact disabled in Panel, Dismount-only in Mounted, absent in Dead; Look, quick ping and spectate **absent** in Dead; Sprint is absent in Panel (GDD '—': walk; Shift is quick-move) and absent in Dead; the comms wheel is live in every context except Panel (emotes greyed in Dead); **Respawn is bound nowhere** (no context enables it); no context has an action carrying both a tap and a hold verb except Interact. A test enumerates every cell against the table in the GDD and fails on any difference. **Cell encoding (story decision, 2026-10-20):** a GDD '—' cell is `Absent`; only a cell the GDD writes as "disabled" is `Disabled` (Jump and Interact in Panel); "emotes greyed" is `Greyed` (Comms wheel in Dead); "Dismount only", "vehicle's controls", "orbit" and Look-in-Panel ("suspended while the cursor is in UI") are `Live` — the gate (Dismount) and the router (pointer/stick ownership) narrow them, not the table; Sprint in Mounted ("Vehicles may reuse") is `Absent` at M-1a.
- [ ] **Contexts.** `BodyContext { OnFoot, Build, Mounted, Dead }` and the `InputContext { World, Panel, Holding, Mounted, Dead }` mapping to input-controls' `RouterState` are defined; the Panel cannot open while Mounted or Dead (a `CanOpenPanel(body)` predicate); Build is World with only Deconstruct (`BuildModeOnly`) anchors focusable.
- [ ] **Published `CarrierStatus`.** A readonly struct `{ LifeState Life; BodyContext Body; bool Swimming; bool CursorHolding; bool PanelOpen; bool Aiming; AnchorRef OccupiedSeat; ushort Seq }` is produced by the Players client step each tick (Story 020 publishes it) and is what the router reads; this story defines the type, its builder from (`PlayerRecord`, `CarrierState`, cursor-non-empty flag, `panelOpen`, `occupiedSeat`) and its mapping to `RouterState.body` / `holding` (Dead ⇒ not holding, Mounted ⇒ not holding — the "auto-stow" rule stays Grid Inventory's). `PanelOpen` is an input to the builder (a flag argument); this story only defines the field — story 020 fills it (its source, the Grid Inventory panel mirror vs an `InputFrame` flag, is an open ADR-level question, ADR-0012 §4 / ADR-0014 §7; do not invent the channel here). **`OccupiedSeat` (ADR-0014 erratum 2026-10-20, owner decision):** the `AnchorRef` of the `SeatOf` `Dismount` anchor of the seat the carrier occupies; the builder forces it to `AnchorRef.None` whenever `Body != Mounted` (a test covers the normalisation). It is a builder argument: story 020 publishes the status and passes `AnchorRef.None` at M-1a; Vehicles' client-side mount state fills it at M1 (argument or port — Vehicles decides); Players never reads Vehicles state. `Aiming` is an echo of the `InputFrame` aim bit for Sim use (body-yaw lock); the router never sources `RouterState.aiming` from it, and it ignores `OccupiedSeat` likewise.
- [ ] **Life state → `BodyContext` (all six ADR-0014 §6 states).** `Alive` → `OnFoot` (or `Build` / `Mounted` from the carrier's mode); `Dead` → `Dead`; `Joining`, `DeadAway`, `Frozen` and `Despawned` → `Dead` (no carrier verbs, no interaction candidates; the host's `CheckInteraction` order still reports `Dead`/`Frozen` itself). `BodyContext` keeps the 4 values of ADR-0012 §2 — no new value is added. A test covers every `LifeState` value. (Story decision, 2026-10-20: the conservative "no gameplay verbs" mapping; how HUD names the context for a joining player is HUD's.)
- [ ] **PCM-P11-2d.** The framework availability gate `InteractionGate.Evaluate(in anchor, in carrierStatus)` returns: Panel (`carrierStatus.PanelOpen` — the one source of the flag; no separate `panelOpen` parameter) → `Unavailable(reason.close_panel)` (focus is computed for display only and Interact is refused); Dead → no candidates; Mounted → a candidate only if `anchor.Verb == Verb.Dismount`, `(anchor.Flags & AnchorFlags.SeatOf) != 0` and `anchor.Ref == carrierStatus.OccupiedSeat` (ADR-0014 §2 as amended 2026-10-20; OQ-013-1 closed); `OccupiedSeat == AnchorRef.None` or any mismatch → no candidates. Tests (fixtures only — Mounted is unreachable at M-1a): the own seat's `SeatOf` Dismount → candidate; **another seat's** `SeatOf` Dismount on the same vehicle → none; Mounted with `OccupiedSeat == None` → none; a `SeatOf` anchor whose verb is not Dismount (e.g. Mount) with `Ref == OccupiedSeat` → none; a Dismount anchor without `SeatOf` → none; Build → only `BuildModeOnly` anchors; otherwise `ok` (the owner's `IsAvailable` is evaluated after, never before, the gate). **Filter order (story decision 2026-10-20):** Dead → Mounted → Build → Panel → swim (`dry_land`) → `ok`; when Panel is open **and** the carrier is swimming the result is `Unavailable(reason.close_panel)`, matching Manual Delivery MD2 (row 1 `deliver.close_panel` before row 2 `deliver.dry_land`, M-MD2-1 "lower row wins"), so the framework prompt and the Deliver owner's prompt never disagree. A test covers the Panel + swimming case. **Which key a Deliver target shows in Panel (story decision 2026-10-20):** the gate runs first and short-circuits, so the focused Deliver anchor's prompt carries the framework key `reason.close_panel` (as for every focused anchor in Panel — stories 014/018); Manual Delivery's `deliver.close_panel` (MD2 row 1) stays the owner's own guard, reached only by direct `IsAvailable` calls (M-MD2-1) and never on the framework path. Both keys render the GDD text "Close panel to deliver" (MD2, M-MD2-4, HUD H-PROMPT-1, grid-inventory Panel row), so the player sees the same prompt either way.
- [ ] **PCM-P6-2a/b/c.** While `Swimming`: jump, sprint, `throw`, `build`, `deliver`, tool use and `drop` are refused — the full GDD P6 / PCM-P6-2a list; `InteractionGate.VerbAllowedWhileSwimming(verb)` returns false for all seven (jump and sprint are already enforced by the solver — `CarrierJump.IsAllowed`, the swim sprint clear in `OwnedCarrier`; stories 006/009 — so the gate only declares them, with a test that pins the seven-verb list), and **every anchor** returns `Unavailable(reason.dry_land)` unless it carries `AllowWhileSwimming` (v1: only Open-on-pile and Pick-up-on-Spill-heap); hotbar select, the own panel, chat, ping and look remain allowed (table rows). ("A swimmer cannot start a Catch" moved to story 016 — Catch has no anchor, so this gate cannot express it.)
- [ ] **Reason keys.** The framework reasons are `StringId`s registered in content: `reason.dry_land{verb}` and `reason.close_panel` (**no argument**, per GDD PCM-UI-1; its text is "Close panel to deliver" — the GDD's Panel prompt for every anchor, HUD H-PROMPT-1 — the same text as Manual Delivery's own `deliver.close_panel`, MD2 row 1); the `Unavailable` result holds exactly one key; banned stems (die/dying/dead/death, kill, slain, corpse, revive, respawn, drown, game over) never appear in a player-facing key text (PCM-UI-3 — the string table check belongs to content; this story adds the test over its own keys).
- [ ] **Cancel-on-context-change.** `InteractionGate.ContextCancelReason(previousStatus, currentStatus)` returns a cancel reason when a hold's context changes into Panel (`PanelOpen` false → true), Mounted or Dead, or the carrier enters swim depth, or is knocked down; walking and non-lethal damage return none. (Story 017 calls it every held tick.)

---

## Implementation Notes

*Derived from GDD P14/P6/P11 and ADR-0014 §2/§4/§7, ADR-0012 §2:*

- Files in `src/PerformativeMail.Sim/Players/`: `BodyContext.cs`, `InputContext.cs`, `VerbContextTable.cs` (a `ReadOnlySpan<byte>` literal property of the 8 × 5 matrix with `Live / Disabled / Absent / Greyed` values), `CarrierStatus.cs`, `InteractionGate.cs`, `FrameworkReasons.cs`.
- `CarrierContextFlags` (Panel, Dead, Mounted, Build) is **owned by the solver input contract** (ADR-0013 §1 and Key Interfaces `CarrierInput`; created in Story 002, bits Dead/Mounted/Build appended in Story 005, `src/PerformativeMail.Sim/Players/CarrierInput.cs`). This story imports it and does not define a second enum; it owns only `BodyContext`/`InputContext`/`CarrierStatus` and a tested mapping `CarrierStatus → CarrierContextFlags` (PanelOpen → Panel, Body Dead → Dead, Mounted → Mounted, Build → Build).
- Grid Inventory owns World / Panel / Holding semantics (its UI rules); do not redefine them — import the context enum values and add Mounted/Dead (a hand-back amendment recorded in the GDD).
- Input & Controls owns concrete bindings, remap, thresholds, glyphs and the conflict validator; this table is *data they validate against*. The input-controls epic consumes `VerbContextTable` and `CarrierStatus` — `production/epics/input-controls/story-007-router-shell-press-machines-mute.md` and `production/epics/input-controls/story-008-router-state-transitions-and-passthrough.md` (which cites this story).
- Mounted/Dead are *unused at M-1a* (no vehicles, no knockdown): the types and table exist and are tested; no code path sets `BodyContext.Mounted` or `Dead`, or a non-`None` `CarrierStatus.OccupiedSeat`, yet except test fixtures.
- `reason.close_panel` is the Panel context's dimmed-prompt reason for every focused anchor, Deliver included (the gate runs before the owner's `IsAvailable`, so `deliver.close_panel` never reaches the prompt on the framework path); its text, "Close panel to deliver", is the GDD's (HUD H-PROMPT-1, MD2) and is rendered by HUD. `reason.dry_land` takes `{verb}`; `reason.close_panel` takes none (PCM-UI-1).
- Swimming-verb refusal for non-anchor verbs (jump and sprint — solver; `drop`, `throw`, tool use) is enforced by *their owners' op handlers* using the shared `Swimming` flag (Grid Inventory rejects `drop` with `swimming`, Throwing refuses throws) — this table is the single declaration they cite, not their enforcement.

---

## Open Questions

- **OQ-013-1 — CLOSED 2026-10-20 (owner decision, Evan; ADR-0014 erratum 2026-10-20 under GDD Impact; TD-106 resolved).** The carrier's own seat is `CarrierStatus.OccupiedSeat`, an `AnchorRef` equal to the `SeatOf` `Dismount` anchor's `AnchorDef.Ref`, and `AnchorRef.None` when not Mounted; the Mounted gate admits only that anchor. Story 020 publishes `None` at M-1a; Vehicles (M1) fills it and chooses the Mount/Dismount slot scheme, since the registry has only World/Build context masks and one verb per `(AnchorRef, context)` (`AnchorRegistry.Register`), so a seat's Mount and Dismount cannot share one ref today.

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
