# Consistency Failure Log

<!-- Auto-maintained by /consistency-check. Do not edit manually. -->
<!-- One entry per detected conflict, in chronological order. -->

| Date | GDD A | GDD B | Conflict Type | Status |
|------|-------|-------|---------------|--------|
| 2026-10-16 | hud-notifications | manual-delivery, throwing-catching | reason-strip lifetime | resolved |
| 2026-10-16 | hud-notifications | grid-inventory, throwing-catching | fumble string / catch routing | resolved |
| 2026-10-16 | hud-notifications | player-character-movement | prompt placement, UI scale | resolved |
| 2026-10-16 | hud-notifications | mail-destinations | pill / feed caps | resolved |
| 2026-10-16 | manual-delivery | mail-destinations | prompt order and misdeliver lines | resolved |
| 2026-10-16 | mail-destinations | manual-delivery | manualInsertHold safe range | resolved |
| 2026-10-16 | input-controls | player-character-movement | Deconstruct binding | deferred (Building GDD) |
| 2026-10-16 | content-data-definitions | player-character-movement | standardIssue flag | resolved |
| 2026-10-16 | grid-inventory | simulation-core | reqId width / dedupe | open |
| 2026-10-16 | throwing-catching | simulation-core, content-data-definitions | retired throw spread / cooldownSlackTicks | open |
| 2026-10-16 | mail-destinations | content-data-definitions | foundValueMultPm key name | open |

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: UI / notifications
**Documents involved**: hud-notifications.md vs manual-delivery.md, throwing-catching.md
**What happened**: reason-strip lifetime 0.8 s (MD `reasonStripTicks`) and 2 s (Throwing, citing retired `reasonShowTicks`) vs HUD F1 ≥ 2.5 s reading floor
**Resolution**: HUD F1 owns the lifetime; MD and Throwing cite it; `reasonStripTicks` deprecated in the registry
**Pattern**: timing for player-readable text belongs to the notification owner — feature GDDs should name the content, not the duration

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: UI strings
**Documents involved**: hud-notifications.md / throwing-catching.md vs grid-inventory.md
**What happened**: fumble cue "Fumbled" (Grid feedback table) vs "Bag full — fumbled"; Throwing routed catch results to the feed while HUD routes them to toasts / hotbar pulse
**Resolution**: Grid and Throwing adopt HUD H4's string and routing
**Pattern**: feedback tables in system GDDs drift after a HUD reconciliation — reference HUD's cue id instead of copying the literal

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: UI layout
**Documents involved**: hud-notifications.md vs player-character-movement.md, manual-delivery.md
**What happened**: world-space edge-clamped prompt and UI scale 75–150 % (PC) vs HUD's screen-fixed prompt, no leader line, 100–150 % scale
**Resolution**: PC and MD defer placement and scale to HUD H11 / H23
**Pattern**: a rejected hand-back must be applied by the non-owner in the same pass, or it lingers as a conflict

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: mail / prompt content
**Documents involved**: manual-delivery.md vs mail-destinations.md
**What happened**: Mail's prompt matrix "N stacks · M pieces" and "Wrong house — posts 1 (−N¢)" vs MD's pieces-first branch table; Mail's "feed ≤ 4 lines" vs HUD caps; MD's manualInsertHold safe range 0.3–0.6 vs Mail's 0.2–0.8
**Resolution**: Mail defers to MD's branch table and HUD's caps; MD cites Mail's safe range
**Pattern**: a referenced value restated with its range drifts — cite the owner, not the range

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: networking / ops
**Documents involved**: grid-inventory.md vs simulation-core.md
**What happened**: Grid rule 8 `reqId` u16 per connection, host dedupes via last 16 replies; Sim Core Op header `reqId` u32 and "the Sim does not deduplicate reqId"
**Resolution**: Unresolved — handed to the Sim Core reviewer (Sim Core not editable in this pass)
**Pattern**: wire fields defined in two GDDs need one owner named in both

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: throwing / data
**Documents involved**: throwing-catching.md vs simulation-core.md, content-data-definitions.md
**What happened**: retired `throwSpreadMrad` still live in CDD (unit example, CV-THR-18) and in Sim Core (mrad note, "spread seed", HB3 to-do); CDD CV-THR-16 cites retired `cooldownSlackTicks` (now `throwBurstTicks`)
**Resolution**: Unresolved — handed to the Sim Core / Content & Data authors
**Pattern**: retiring a knob must sweep validators and cross-reference tables

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: data keys
**Documents involved**: mail-destinations.md vs content-data-definitions.md
**What happened**: Mail knob `foundValueMultPm` vs CDD `foundValueMultRatio`
**Resolution**: Unresolved — handed to Content & Data
**Pattern**: key names follow one suffix convention, set by the data owner

### 2026-10-16 — /consistency-check — 🔴 CONFLICT
**Domain**: input / building
**Documents involved**: input-controls.md vs player-character-movement.md
**What happened**: PC P14 said Interact reaches Deconstruct in Build; Input binds Deconstruct to hold RMB / LT and makes Interact not live in Build
**Resolution**: PC defers the binding to Input; final call deferred to the Building GDD (OQ-IC-13)
**Pattern**: contexts (PC) and bindings (Input) must be stated separately

### 2026-10-16 — /architecture-review — 🔴 CONFLICT
**Domain**: Architecture / Data ownership (op handler) + wire layout
**Documents involved**: ADR-0009 vs ADR-0017 (also ADR-0007 §4, ADR-0014 §5); ADR-0009 §1 vs manual-delivery.md Network table
**What happened**: ADR-0009 registered `DeliverMatching` as an Inventory OpKind whose handler calls Mail, while ADR-0017 forbids Inventory → Mail and calls `ConsumeHand` from a Delivery handler. Separately, MD fixed a 12/16 B request with no claimed position, while every range-gated op must carry one.
**Resolution**: Technical-director errata (ADR-0009/0011 GDD Impact). The OpKind and handler are Delivery's (block 0x0100–0x013F). The wire is 24 B MATCH / 28 B MISDELIVER_ONE with ClaimedPosition. grid-inventory story 022 is Blocked for re-scope.
**Pattern**: An op that spans systems is owned by the system at the top of the call direction (the one allowed to call the others). Byte budgets in a GDD must be re-derived whenever a cross-cutting rule (e.g. "range-gated ops carry position") adds a field.
