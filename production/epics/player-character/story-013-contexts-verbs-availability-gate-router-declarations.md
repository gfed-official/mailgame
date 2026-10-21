# Story 013: Input contexts, the verb × context table (P14 router declarations) and the framework availability gate

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M (~1 day; ~400 lines prod + tests)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-21 (completed 2026-10-21: all ACs verified, 4 legs green; readiness fixes: status reset to Ready — no code had landed; reason-key StringId source split out as OQ-013-2 / TD-119; per-file placement, test-file map, performance note, dependencies on stories 002/005; swim-Catch ruling cross-referenced). 2026-10-20 (lead docs refresh: `PanelOpen`, life-state mapping, '—' = Absent, swim-Catch AC moved to story 016; OQ-013-1 closed by owner decision 2026-10-20 — `CarrierStatus.OccupiedSeat` per the ADR-0014 erratum 2026-10-20, Mounted branch now admits the own-seat Dismount; stamp 2026-10-17.1 — the 17.1 changes touch only the Godot input adapter and `WorldTablesBuilder`, and the ADR-0014 erratum changes no manifest rule)

> **Interaction-framework story (3 of 9) — the router declarations.** Input & Controls' action map and `RouterState` read the `BodyContext`, the verb × context table and the published `CarrierStatus` defined here. Mounted and Dead are declared but unused at M-1a.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P14 input contexts and verbs; P6 swimming restrictions; P11 "Panel computed for display only"; P12 context-change cancels; UI Requirements — reason keys)
**Requirement**: `TR-movement-011` (framework availability rules around the registry); P14 itself has **no TR-ID in the registry** — cited by GDD rule P14 and acceptance id PCM-P14-1 (also PCM-P6-2a/b/c, PCM-P11-2d)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing (§2 context filter, §4 context-change cancels, §7 publication) — Accepted 2026-10-16 (primary); ADR-0012: Input router core (§2 `RouterState` reads `body` — OnFoot/Build/Mounted/Dead — and `holding` from the ClientLayer-published carrier status) — Accepted 2026-10-16
**ADR Decision Summary**: Grid Inventory owns World, Panel and Holding; this system **adds Mounted and Dead**. Input & Controls owns bindings, remap and thresholds; this system declares which verbs are live in which context. The router's `body` dimension and `holding` are read each frame from the ClientLayer-published carrier status (life state, mount, cursor non-empty). Focus filtering by context (Panel → display only; Dead → none; Mounted → own seat's Dismount only; Build → `BuildModeOnly` anchors only) is ADR-0014 §2.
**ADR Version**: ADR-0014 2026-10-16 + 2026-10-20 erratum (§1/§2 `CarrierStatus.OccupiedSeat`, Mounted filter); ADR-0012 2026-10-16 + 2026-10-17 §7 amendment (Godot input adapter only — no effect on this story)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Feature layer)**:
- Required: focus uses the §2 integer score; context filters are data, not branches scattered in callers (ADR-0014 §2)
- Required: the two binding rules (one meaning per binding per context; no button with both a tap and a hold verb in one context) apply unchanged (GDD P14; ADR-0012 validator)
- Required: cancels on context change into Panel, Mounted or Dead are recorded with a reason, never silent (ADR-0014 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P14-1, PCM-P6-2a/b/c, PCM-P11-2d), scoped to this story:*

- [x] **PCM-P14-1 (table-driven).** `VerbContextTable` declares the 8 verbs (Move, Look, Sprint, Jump, Interact, Shoulder swap, Comms wheel, Respawn) × 5 contexts (World, Panel, Holding, Mounted, Dead) exactly as GDD P14: Jump disabled in Panel; Interact disabled in Panel, Dismount-only in Mounted, absent in Dead; Look, quick ping and spectate **absent** in Dead; Sprint is absent in Panel (GDD '—': walk; Shift is quick-move) and absent in Dead; the comms wheel is live in every context except Panel (emotes greyed in Dead); **Respawn is bound nowhere** (no context enables it); no context has an action carrying both a tap and a hold verb except Interact. A test enumerates every cell against the table in the GDD and fails on any difference. **Cell encoding (story decision, 2026-10-20):** a GDD '—' cell is `Absent`; only a cell the GDD writes as "disabled" is `Disabled` (Jump and Interact in Panel); "emotes greyed" is `Greyed` (Comms wheel in Dead); "Dismount only", "vehicle's controls", "orbit" and Look-in-Panel ("suspended while the cursor is in UI") are `Live` — the gate (Dismount) and the router (pointer/stick ownership) narrow them, not the table; Sprint in Mounted ("Vehicles may reuse") is `Absent` at M-1a.
- [x] **Contexts.** `BodyContext { OnFoot, Build, Mounted, Dead }` and the `InputContext { World, Panel, Holding, Mounted, Dead }` mapping to input-controls' `RouterState` are defined; the Panel cannot open while Mounted or Dead (a `CanOpenPanel(body)` predicate); Build is World with only Deconstruct (`BuildModeOnly`) anchors focusable.
- [x] **Published `CarrierStatus`.** A readonly struct `{ LifeState Life; BodyContext Body; bool Swimming; bool CursorHolding; bool PanelOpen; bool Aiming; AnchorRef OccupiedSeat; ushort Seq }` is produced by the Players client step each tick (Story 020 publishes it) and is what the router reads; this story defines the type, its builder from (`PlayerRecord`, `CarrierState`, cursor-non-empty flag, `panelOpen`, `occupiedSeat`) and its mapping to `RouterState.body` / `holding` (Dead ⇒ not holding, Mounted ⇒ not holding — the "auto-stow" rule stays Grid Inventory's). `PanelOpen` is an input to the builder (a flag argument); this story only defines the field — story 020 fills it (its source, the Grid Inventory panel mirror vs an `InputFrame` flag, is an open ADR-level question, ADR-0012 §4 / ADR-0014 §7; do not invent the channel here). **`OccupiedSeat` (ADR-0014 erratum 2026-10-20, owner decision):** the `AnchorRef` of the `SeatOf` `Dismount` anchor of the seat the carrier occupies; the builder forces it to `AnchorRef.None` whenever `Body != Mounted` (a test covers the normalisation). It is a builder argument: story 020 publishes the status and passes `AnchorRef.None` at M-1a; Vehicles' client-side mount state fills it at M1 (argument or port — Vehicles decides); Players never reads Vehicles state. `Aiming` is an echo of the `InputFrame` aim bit for Sim use (body-yaw lock); the router never sources `RouterState.aiming` from it, and it ignores `OccupiedSeat` likewise.
- [x] **Life state → `BodyContext` (all six ADR-0014 §6 states).** `Alive` → `OnFoot` (or `Build` / `Mounted` from the carrier's mode); `Dead` → `Dead`; `Joining`, `DeadAway`, `Frozen` and `Despawned` → `Dead` (no carrier verbs, no interaction candidates; the host's `CheckInteraction` order still reports `Dead`/`Frozen` itself). `BodyContext` keeps the 4 values of ADR-0012 §2 — no new value is added. A test covers every `LifeState` value. (Story decision, 2026-10-20: the conservative "no gameplay verbs" mapping; how HUD names the context for a joining player is HUD's.)
- [x] **PCM-P11-2d.** The framework availability gate `InteractionGate.Evaluate(in anchor, in carrierStatus)` returns: Panel (`carrierStatus.PanelOpen` — the one source of the flag; no separate `panelOpen` parameter) → `Unavailable(reason.close_panel)` (focus is computed for display only and Interact is refused); Dead → no candidates; Mounted → a candidate only if `anchor.Verb == Verb.Dismount`, `(anchor.Flags & AnchorFlags.SeatOf) != 0` and `anchor.Ref == carrierStatus.OccupiedSeat` (ADR-0014 §2 as amended 2026-10-20; OQ-013-1 closed); `OccupiedSeat == AnchorRef.None` or any mismatch → no candidates. Tests (fixtures only — Mounted is unreachable at M-1a): the own seat's `SeatOf` Dismount → candidate; **another seat's** `SeatOf` Dismount on the same vehicle → none; Mounted with `OccupiedSeat == None` → none; a `SeatOf` anchor whose verb is not Dismount (e.g. Mount) with `Ref == OccupiedSeat` → none; a Dismount anchor without `SeatOf` → none; Build → only `BuildModeOnly` anchors; otherwise `ok` (the owner's `IsAvailable` is evaluated after, never before, the gate). **Filter order (story decision 2026-10-20):** Dead → Mounted → Build → Panel → swim (`dry_land`) → `ok`; when Panel is open **and** the carrier is swimming the result is `Unavailable(reason.close_panel)`, matching Manual Delivery MD2 (row 1 `deliver.close_panel` before row 2 `deliver.dry_land`, M-MD2-1 "lower row wins"), so the framework prompt and the Deliver owner's prompt never disagree. A test covers the Panel + swimming case. **Which key a Deliver target shows in Panel (story decision 2026-10-20):** the gate runs first and short-circuits, so the focused Deliver anchor's prompt carries the framework key `reason.close_panel` (as for every focused anchor in Panel — stories 014/018); Manual Delivery's `deliver.close_panel` (MD2 row 1) stays the owner's own guard, reached only by direct `IsAvailable` calls (M-MD2-1) and never on the framework path. Both keys render the GDD text "Close panel to deliver" (MD2, M-MD2-4, HUD H-PROMPT-1, grid-inventory Panel row), so the player sees the same prompt either way.
- [x] **PCM-P6-2a/b/c.** While `Swimming`: jump, sprint, `throw`, `build`, `deliver`, tool use and `drop` are refused — the full GDD P6 / PCM-P6-2a list; `InteractionGate.VerbAllowedWhileSwimming(verb)` returns false for all seven (jump and sprint are already enforced by the solver — `CarrierJump.IsAllowed`, the swim sprint clear in `OwnedCarrier`; stories 006/009 — so the gate only declares them, with a test that pins the seven-verb list), and **every anchor** returns `Unavailable(reason.dry_land)` unless it carries `AllowWhileSwimming` (v1: only Open-on-pile and Pick-up-on-Spill-heap); hotbar select, the own panel, chat, ping and look remain allowed (table rows). ("A swimmer cannot start a Catch" moved to story 016 — Catch has no anchor, so this gate cannot express it.)
- [x] **Reason keys.** The framework has exactly two reason keys: `reason.dry_land{verb}` and `reason.close_panel` (**no argument**, per GDD PCM-UI-1; its text is "Close panel to deliver" — the GDD's Panel prompt for every anchor, HUD H-PROMPT-1 — the same text as Manual Delivery's own `deliver.close_panel`, MD2 row 1). `FrameworkReasons` is a readonly struct holding the two `StringId` values (`DryLand`, `ClosePanel`) plus the two ASCII key names as constants (`"reason.dry_land"`, `"reason.close_panel"`); the gate receives a `FrameworkReasons` value as an input (constructor or `in` argument) and never mints a `StringId` itself. **Where the two `StringId`s come from is OQ-013-2 (open; TD-119)** — ADR-0004 §6 / ADR-0010 §3 define `StringNamespace` values only per def type plus `Run`, and no namespace or registration path exists yet for framework UI reason keys; do not invent one here. Tests use two distinct fixture `StringId`s. Asserted: the `Unavailable` result holds exactly one key (`DryLand` or `ClosePanel`, never `StringId.None`); the key-name constants equal the PCM-UI-1 spellings; `DryLand != ClosePanel` is a precondition the gate asserts. The banned-stem check (PCM-UI-3) applies to player-facing **text**, and keys are exempt by the GDD's own wording; the text check is content's string-table check and moves with TD-119 — this story adds no text.
- [x] **Cancel-on-context-change.** `InteractionGate.ContextCancelReason(previousStatus, currentStatus)` returns a cancel reason when a hold's context changes into Panel (`PanelOpen` false → true), Mounted or Dead, or the carrier enters swim depth, or is knocked down; walking and non-lethal damage return none. (Story 017 calls it every held tick.)

---

## Implementation Notes

*Derived from GDD P14/P6/P11 and ADR-0014 §2/§4/§7, ADR-0012 §2:*

- Files in `src/PerformativeMail.Sim/Players/` (namespace `PerformativeMail.Sim.Players`):
  - `BodyContext.cs` — `BodyContext` and the `LifeState → BodyContext` mapping (`BodyContexts.From(LifeState, mode)`).
  - `InputContext.cs` — `InputContext`, `CanOpenPanel(body)` and the `CarrierStatus → RouterState.body / holding` mapping (as plain values; `RouterState` itself is input-controls', in Client.Core — this Sim file must not reference it).
  - `VerbContextTable.cs` — `ContextVerb` (the 8 verbs), `CellState { Live, Disabled, Absent, Greyed }` and a `ReadOnlySpan<byte>` literal property of the 8 × 5 matrix.
  - `CarrierStatus.cs` — the struct, `CarrierStatus.Build(...)` (the builder, including the `OccupiedSeat` normalisation) and `ToContextFlags()` (`CarrierStatus → CarrierContextFlags`).
  - `InteractionGate.cs` — `Evaluate`, `VerbAllowedWhileSwimming`, `ContextCancelReason`, the result type `GateResult { Ok | NoCandidate | Unavailable(StringId) }` and ADR-0014 §7's `CancelReason` enum (`byte`; not yet in the tree): this story declares `None` plus the five context-change cases it returns (`Panel`, `Mounted`, `Dead`, `Swim`, `Knockdown`); story 017 appends its own cases (release, range, unregistered, unavailable, owner `ShouldCancel`) — values are append-only.
  - `FrameworkReasons.cs` — the two-`StringId` struct and key-name constants (see the Reason keys AC; source of the ids is OQ-013-2).
  - `AnchorRef`, `AnchorDef`, `AnchorFlags`, `Verb` are imported from story 012; `LifeState`, `PlayerRecord` from story 001; nothing is redefined.
- **Performance:** `Evaluate`, `VerbAllowedWhileSwimming`, `ContextCancelReason`, the table lookup and the `CarrierStatus` builder are pure constant-time integer checks on `in` structs, with no allocation (0 B after warm-up). Story 014 calls `Evaluate` per candidate per tick, so one allocation test (NonParallel, the repo's `*AllocationTests` pattern) pins 0 B over a warm loop of `Evaluate` + `ContextCancelReason`.
- `CarrierContextFlags` (Panel, Dead, Mounted, Build) is **owned by the solver input contract** (ADR-0013 §1 and Key Interfaces `CarrierInput`; created in Story 002, bits Dead/Mounted/Build appended in Story 005, `src/PerformativeMail.Sim/Players/CarrierInput.cs`). This story imports it and does not define a second enum; it owns only `BodyContext`/`InputContext`/`CarrierStatus` and a tested mapping `CarrierStatus → CarrierContextFlags` (PanelOpen → Panel, Body Dead → Dead, Mounted → Mounted, Build → Build).
- Grid Inventory owns World / Panel / Holding semantics (its UI rules); do not redefine them — import the context enum values and add Mounted/Dead (a hand-back amendment recorded in the GDD).
- Input & Controls owns concrete bindings, remap, thresholds, glyphs and the conflict validator; this table is *data they validate against*. The input-controls epic consumes `VerbContextTable` and `CarrierStatus` — `production/epics/input-controls/story-007-router-shell-press-machines-mute.md` and `production/epics/input-controls/story-008-router-state-transitions-and-passthrough.md` (which cites this story).
- Mounted/Dead are *unused at M-1a* (no vehicles, no knockdown): the types and table exist and are tested; no code path sets `BodyContext.Mounted` or `Dead`, or a non-`None` `CarrierStatus.OccupiedSeat`, yet except test fixtures.
- `reason.close_panel` is the Panel context's dimmed-prompt reason for every focused anchor, Deliver included (the gate runs before the owner's `IsAvailable`, so `deliver.close_panel` never reaches the prompt on the framework path); its text, "Close panel to deliver", is the GDD's (HUD H-PROMPT-1, MD2) and is rendered by HUD. `reason.dry_land` takes `{verb}`; `reason.close_panel` takes none (PCM-UI-1).
- **Swimming and Catch (owner ruling, Evan 2026-10-20 22:05 PT; ADR-0014 §3 erratum):** Catch has no anchor, so this gate never sees it. The client sends the arm as on land, the host refuses it as ineligible, and the swimmer sees "Dry land required"; there is **no client-side suppression**. That is player-character story 016 (PC016-SWIM-CATCH) and throwing-catching story 022. Do not add a Catch row or a swim check for Catch here.
- Swimming-verb refusal for non-anchor verbs (jump and sprint — solver; `drop`, `throw`, tool use) is enforced by *their owners' op handlers* using the shared `Swimming` flag (Grid Inventory rejects `drop` with `swimming`, Throwing refuses throws) — this table is the single declaration they cite, not their enforcement.

---

## Open Questions

- **OQ-013-2 — OPEN (owner / ADR-0004 + ADR-0010 authors; TD-119). Not blocking this story.** Which `StringNamespace` and which registration path give the framework reason keys `reason.dry_land` / `reason.close_panel` their `StringId`s? ADR-0004 §6 and ADR-0010 §3 define namespaces only per def type plus the per-run `Run` intern table, and `WorldTables` already numbers its display strings in `Run` from 0, so a second owner interning into `Run` would need a rule the ADRs do not state. The same question applies to every owner reason key (`deliver.*`, `throw.*`, `catch.reason.*`). Options for the owner: an ADR-0004/0010 erratum adding a UI-string namespace and a content string table, or a defined `Run` interning order at composition. This story ships `FrameworkReasons` as an injected value so either answer plugs in without touching the gate; the composition wiring that supplies real ids lands with the answer (story 018/020 consume the keys at runtime).
- **Panel-open source — OPEN, deferred to story 020 (ADR-0012 §4 / ADR-0014 §7).** Grid Inventory panel mirror vs an `InputFrame` flag. This story only defines `CarrierStatus.PanelOpen` as a builder argument. Not blocking.
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
- Logic (all must exist and pass on net10.0 and net8.0, Debug and Release — `tools/ci/test-legs-loop.sh`):
  - `tests/unit/player/InputContextTableTests.cs` — `[Trait("Id", "PCM-P14-1")]` (every cell, '—' = Absent encoding, Respawn bound nowhere, the tap+hold rule) and the Contexts AC (`CanOpenPanel`, Build = World).
  - `tests/unit/player/CarrierStatusTests.cs` — the builder, `OccupiedSeat` normalisation, `Aiming` echo, `ToContextFlags`, the `RouterState.body`/`holding` mapping (Dead/Mounted ⇒ not holding) and every `LifeState` value → `BodyContext`.
  - `tests/unit/player/InteractionGateTests.cs` — `PCM-P11-2d` (filter order, the Mounted own-seat fixtures, Panel + swimming → `close_panel`), `PCM-P6-2a`/`PCM-P6-2b`/`PCM-P6-2c` (seven-verb list, `AllowWhileSwimming`, allowed rows), the Reason keys AC and `ContextCancelReason` (each cancel case; walking / non-lethal damage → none).
  - `tests/unit/player/InteractionGateAllocationTests.cs` — 0 B warm loop (NonParallel collection).

**Status**: [x] Created and passing on net10.0 and net8.0, Debug and Release (4875 total, 0 failed; skips 42 Debug / 2 Release, unchanged; 0 warnings).

**Completion notes (2026-10-21)**
- Files: `src/PerformativeMail.Sim/Players/{BodyContext,InputContext,VerbContextTable,CarrierStatus,InteractionGate,FrameworkReasons}.cs`; tests `tests/unit/player/{InputContextTableTests,CarrierStatusTests,InteractionGateTests,InteractionGateAllocationTests}.cs`.
- AC to tests: PCM-P14-1 `EveryCell_MatchesTheGddP14Table`, `Respawn_IsBoundInNoContext`, `TapAndHold_OnlyInteractCarriesBoth_InAnyContext`, `NamedCells_UseTheStoryEncoding`; Contexts `CanOpenPanel_FalseWhileMountedOrDead`, `InputContextOf_MapsBodyPanelAndCursor`; CarrierStatus `Build_OccupiedSeat_NormalisedToNoneUnlessMounted`, `Build_AimingIsEchoed`, `ToContextFlags_MapsPanelAndBody`, `RouterMapping_DeadAndMountedAreNeverHolding`; Life state `EveryLifeState_MapsToBodyContext`; PCM-P11-2d `Mounted_*`, `Build_OnlyBuildModeOnlyAnchors`, `PanelOpenAndSwimming_IsClosePanel`, `Dead_NoCandidates_BeforeEveryOtherFilter`; PCM-P6-2a/b/c `SwimRefused_IsExactlyTheSevenP6Verbs`, `Swimming_AnchorWithoutAllowFlag_IsDryLand`, `Swimming_AllowWhileSwimming_IsOk`, `SwimAllowedRows_AreAllowed`; Reason keys `ReasonKeyNames_AreThePcmUi1Spellings`, `Unavailable_HoldsExactlyOneNonNoneKey`, `Gate_RejectsEqualOrNoneReasonIds`; Cancel `ContextChange_IntoPanelMountedDeadSwimOrKnockdown_Cancels`, `NonCancellingChanges_ReturnNone`; 0 B `WarmLoop_EvaluateAndContextCancelReason_AllocatesZeroBytes` (passes in Debug too; no TD-008 skip).
- Deviations: `CarrierStatus.Build` also takes `buildMode`, `aiming`, `seq`; extra public types `SwimVerb`, `GateKind`, `VerbPress`, `BodyContexts`, `InputContexts`; gate is a class holding the injected `FrameworkReasons` and throws on None or equal ids. Carry-forwards logged as TD-122 and TD-123.
- `/code-review`: APPROVED WITH SUGGESTIONS, no blocking findings; cheap suggestions applied (doc size claim, `==` seat compare, explicit allowed-verb pattern).

---

## Dependencies

- Depends on: Story 001 (`LifeState`, `PlayerRecord`), Stories 002 and 005 (`CarrierContextFlags` in `CarrierInput.cs`, ADR-0013 §1 + Key Interfaces), Story 012 (`AnchorDef.AnchorFlags`, `Verb`), `production/epics/content-data/story-008-contentset-builtincontent-m1a.md` (`StringId` type; the reason-key **registration** is OQ-013-2 / TD-119, not a dependency). All Complete on `main`. No dependency on open PRs #7, #9 or #11 (none touches `src/PerformativeMail.Sim/Players/` or `tests/unit/player/`; #11 touches `SimComposition.cs`, which this story does not edit).
- Unlocks: Stories 014, 017, 019, 020; input-controls epic (router `body` dimension, action-map validator rows — `production/epics/input-controls/story-008-router-state-transitions-and-passthrough.md`, `production/epics/input-controls/story-007-router-shell-press-machines-mute.md`); grid-inventory `drop`-while-swimming reason; throwing-catching swim refusal
