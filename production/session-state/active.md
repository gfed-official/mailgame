# Active Session State

<!-- STATUS -->
Epic: Simulation Core
Feature: Build configurations, SIM_ASSERT and the net8.0 CI leg (scaffold A)
Task: /dev-story production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md — BLOCKED (Phase 2)
<!-- /STATUS -->

<!-- CHECKPOINT -->
**Updated:** 2026-10-22
**Branch:** `feat/grid-inventory-015`
**Current task:** Mail Review R1 fixes on PR #27 (story 015), head 58325bb, uncommitted. B1 done: `DetachForThrow` now answers `stale_entry` for a once-allocated entry id (≤ `EntryPool.LastId`) that is no longer live, and `bad_request` for id 0, a temp id, an id above `LastId`, or a live entry outside the slot's hotbar (same id classification as `InvRequestCheck`). A2: `TryInsert` returns `MergeUnitInto`'s result. A5: 10 new tests (B1 split, precedence, duplicate release, rejoin seal actor, ReturnedToSender, live RebuildIndex, SimAssert 78 + MergeUnitInto invalid assert, 19→20 merge, wrap-asserting backward shift). A1 story Completion notes, A3/A6 TD-144/TD-146 text updated.
**Next step:** owner review and commit of the R1 fixes, then Mail Review round 2. A4 (decision-log entry for the Q1/Q2 defaults, Belt Logistics owner) and A7 (PR body 0 B overclaim) still open, out of this pass's scope.
**Blocked on:** nothing
**Files in progress:** src/PerformativeMail.Sim/Inventory/InventorySystem.cs, src/PerformativeMail.Sim/Inventory/IInventoryApi.cs, tests/unit/inventory/InventoryApiTests.cs, production/epics/grid-inventory/story-015-inventory-api-feature-hooks.md, docs/tech-debt-register.md
**Run result:** N/A, a Logic story in the plain-C# Sim. Debug and Release net10.0 builds have 0 warnings; 5,323 tests each, 0 failed (Debug 49 skipped, Release 3); tools/ci scripts pass. net8.0 legs were not run this pass. Reverting the stale_entry branch fails 2 of the new tests.
**Open questions:** TD-144…TD-147 (148 rows, 133×S + 15×M, unchanged); Mail Lead to fold TD-144 into the next ADR-0009 errata pass before mail-destinations 005/007
<!-- /CHECKPOINT -->

- **Task**: Systems decomposition
- **Status**: Systems index created (TD-SYSTEM-BOUNDARY: CONCERNS applied; PR-SCOPE: OPTIMISTIC applied; CD-SYSTEMS: CONCERNS applied)
- **File**: design/gdd/systems-index.md
- **Next**: Prototype M-1a greybox, then design individual system GDDs in index order (just-in-time, one milestone ahead)
- **Date**: 2026-10-15

## Prototype checkpoint (2026-10-15)
- **Concept**: Performative Mail — M-1a greybox ("delivery + throwing")
- **Hypothesis**: If the player hand-delivers mail by matching addresses and can throw it along an arc, they will choose to throw and enjoy it. True if, in a 10-minute session on one street with 10 houses, the player voluntarily throws a meaningful share of deliveries, keeps playing after a misdelivery, and the owner plus two friends each ask to play again.
- **Riskiest assumption**: throwing with an unsoftened misdelivery risk reads as funny, not frustrating (tested first).
- **Path**: Engine (Godot 4.7.1), GDScript (throwaway; no .NET SDK needed). Godot NOT detected on this machine — must be installed to run.
- **Scope**: one flat street, 10 houses/mailboxes with addresses, PO intake spawning letters/small packages, capsule carrier + hotbar, hand delivery, throwing (spec 13 §4.1 speeds, arc preview green/red/white, 3 s max flight), money counter with misdelivery penalty, on-screen delivery/misdelivery stings.
- **Cut**: networking, procgen, grid inventory UI, belts/sorters, raids, combat, building, shop, quota/shifts, menus, save, gamepad, audio assets, polish.
- **Cap**: 1 day; stop at 2 h of iteration without a playable state.
- **Current phase**: COMPLETE — PROCEED accepted with CD-PLAYTEST CONCERNS; hypothesis NOT FALSIFIED (preference signal only). Gate requirements: two-friend greybox run (thresholds in REPORT.md) before M0; M0 Fellowship criterion; solo check; concept-doc risk note. Next: /design-review design/gdd/game-concept.md, then /gate-check, then /design-system in index order. Report at prototypes/m1a-delivery-throwing-concept/REPORT.md; row in prototypes/index.md. Godot 4.7.1 standard installed at ~/.local/opt/godot-4.7.1 (symlink ~/.local/bin/godot).

## Gate check (2026-10-15)
- Concept → Systems Design: **FAIL** (CD NOT READY, AD NOT READY, TD CONCERNS, PR CONCERNS). Report: production/gate-checks/2026-10-15-systems-design.md
- Blockers: no Visual Identity Anchor in game-concept.md; enemy/tone fiction undefined.
- Next: /art-bible, confirm pillars/anti-pillars/core fantasy, then re-run /gate-check.

## Art bible (2026-10-15)
- design/art/art-bible.md: all 9 sections authored; AD-ART-BIBLE CONCERNS → REVISED (11 items applied). Open: §1 tone reconciliation flagged for creative-director.
- Visual Identity Anchor added to design/gdd/game-concept.md; enemy fiction = rival courier company (closes spec 13 §7 art/tone).
- Next: re-run /gate-check (Concept → Systems Design).

## Gate check re-run (2026-10-15)
- Concept → Systems Design: **CONCERNS**, risks accepted by the owner; stage advanced to **Systems Design** (project.yaml + production/stage.txt). Report: production/gate-checks/2026-10-15-systems-design-rerun.md
- **GUARD:** /design-system only on the M-1a set (Grid Inventory, Player Character, Mail & Destinations, Throwing, Manual Delivery, Input, HUD) until the two-friend greybox is recorded in REPORT.md.
- Open conditions: CD tone-wording fixes (Core Fantasy / MDA Fantasy rows / bible §1 line); confirm pillars + anti-pillars; Pillar 5 vs Fellowship decision before run-loop GDDs; time log; Technical Setup carry-overs (see report).
- Next: /design-system Grid Inventory (retrofit from design/spec/03-mail-inventory-economy.md §3).

## /design-system Grid Inventory (2026-10-15, autonomous mode)
- Task: Grid Inventory & Containers GDD — retrofit from design/spec/03 §3
- File: design/gdd/grid-inventory.md (skeleton created; effective tier full → all 8 sections + Summary)
- Decisions logged (autonomous, minor): skeleton created without prompt; Section A framing = [C] Both (Core category recommends [A]; overridden to [C] because the system is player-operated — inventory UI is its main surface); ADR ref = [B] No (no ADRs exist); Fantasy = [A] Yes (Core-layer, player-operated); Section B Direct/Both = [C] Both.
- Current section: Starting (specialist round 1 launching)
- log_decision: Approve Overview section — Chosen: [A] Approve — Category: minor
- log_decision: Approve Player Fantasy section — Chosen: [A] Approve (framing A "Case Your Route", CD recommendation) — Category: minor
- Current section: C Detailed Design (awaiting systems-designer, gameplay-programmer, ux-designer)
- log_decision: Approve Detailed Design section — Chosen: [A] Approve (systems-designer recommended options adopted: weightBasis PerEntry; death bag = pile; NoMerge on spawn; hotbar = slots; Intake 20×16; no swap; truck mail-only; backpack item stays on death; Strong Back clamp-then-halve; non-mail WorldItem destroyed at expiry; cursor on disconnect → AutoPlace else WorldItem; JoinState sends full Intake/Depot state; UX: throw on RMB per spec 09, sort by district index, manifest by scaled width, drag lift = scale+shadow) — Category: minor
- log_decision: Approve Formulas section — Chosen: [A] Approve (new numbers kept PROPOSED: backlogComplaintPerTick 3 → Run Structure; worldItemCap 512; non-mail weightClass light; leash 3.0 m) — Category: minor
- Registry conflict check (C, D): design/registry/entities.yaml does not exist — no conflicts possible; candidates deferred to §5b.
- Current section: E Edge Cases (systems-designer) + H Acceptance Criteria (qa-lead) launching
- log_decision: Approve Dependencies / Tuning Knobs / UI Requirements / Cross-References — Chosen: [A] Approve (optional-sections widget: "All of them" recommended → selected; Tuning Knobs derived from D tables) — Category: minor
- Current section: E, H, Visual/Audio+Game Feel pending specialists; then Open Questions, Summary, CD-GDD-ALIGN
- log_decision: Approve Visual/Audio + Game Feel — Chosen: [A] Approve (art-director draft) — Category: minor
- log_decision: Approve Edge Cases section — Chosen: [A] Approve; six C corrections applied (stale_layout baseVersion rule; split-and-place not "own cells free"; wallet_floor reason; not_allowed vs no_room; mail-first cap eviction; bag-expiry non-mail → Spill WorldItems, option B recommended) — Category: minor. Flag 5 (bag expiry) listed for creative-director confirmation in Open Questions.
- log_decision: Approve Open Questions + Summary — Chosen: [A] Approve — Category: minor; awaiting qa-lead for H
- log_decision: Approve Acceptance Criteria — Chosen: [A] Approve (qa-lead's 48 criteria; 11 flags applied to C: out_of_range/swimming codes, throw not_carryable, drop offset 500 mm, cap tiebreak, numeric manifest rule, console anchors, sort no-op semantics) — Category: minor
- log_decision: Create design/registry/entities.yaml (absent) with 1 item, 3 formulas, 9 constants (5 proposed) — Chosen: create — Category: minor (new file, not a schema change)
- log_decision: Update systems-index.md row 8 → Designed, design doc linked — Chosen: update — Category: minor
- Status: all 8 required sections + Summary + 5 optional sections written; CD-GDD-ALIGN next
- CD-GDD-ALIGN: CONCERNS → REVISED 2026-10-16 (4 fixes applied; OQ2 resolved option B + tint-heap condition; OQ15, OQ16 added; game-pillars.md Pillar 3 clarification added). log_decision: Revise flagged items — Chosen: revise (CD-specified text) — Category: minor.
- **Task**: Grid Inventory & Containers GDD — **Status: Complete, pending independent /design-review**
- **File**: design/gdd/grid-inventory.md
- **Sections authored**: Summary, Overview, Player Fantasy, Detailed Design (Core Rules R1–R19, States, Interactions), Formulas (D1–D7), Edge Cases, Dependencies, Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements, Cross-References, Acceptance Criteria (48), Open Questions (16)
- **Registry**: design/registry/entities.yaml created (1 item, 3 formulas, 9 constants, 5 proposed)
- **Next**: /design-review design/gdd/grid-inventory.md in a FRESH session; then /consistency-check; then /design-system Player Character & Movement (#2 in design order, within the M-1a guard)

## /design-system Input & Controls (2026-10-16, autonomous mode, subagent)
- **Task**: input-controls GDD — **Status: Complete (Designed), pending independent /design-review**
- **File**: design/gdd/input-controls.md
- **Sections authored**: Summary, Overview, Player Fantasy, Detailed Design (R1–R12, grammar G1–G9, States, Interactions), Formulas (F1–F13), Edge Cases, Dependencies, Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements, Cross-References, Acceptance Criteria (~99 IDs), Open Questions (OQ-IC-1…14 + Input Router ADR list)
- CD-GDD-ALIGN: CONCERNS → REVISED 2026-10-16 (7 findings applied)
- Registry: +1 formula (holdEff), +7 constants/rules; referenced_by added to deliverHoldRule, externalContainerLeashMeters
- Systems index: row 3 → Designed; progress counts updated
- Hand-backs: OQ-IC-1 → Player Character (P11 no hold→catch conversion; F4 holdScalePm 500–2500); OQ-IC-10 → Grid Inventory control table (13 items); OQ-IC-3 → Throwing (catch window, non-colour arc marker BLOCKING for Throwing)
- **Next**: /design-review design/gdd/input-controls.md in a FRESH session; then /consistency-check

## /design-system Player Character & Movement (2026-10-16, autonomous mode, subagent)
- **Task**: Player Character & Movement GDD — **Status: Complete (Designed), pending independent /design-review in a fresh session**
- **File**: design/gdd/player-character-movement.md
- **Sections authored**: Summary, Overview, Player Fantasy (+ Feel & Comedy Feedback), Detailed Design (Core Rules P1–P20, States and Transitions, Interactions), Formulas (F1–F8 + ContentValidator rules), Edge Cases, Dependencies (incl. Sim/Presentation split), Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements, Network, Cross-References, Acceptance Criteria (67), Open Questions (6 Grid Inventory hand-backs + 18)
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (6 text fixes)
- **Input & Controls hand-back OQ-IC-1 (a)–(e)**: adopted
- **Registry**: +7 formulas, +10 constants, referenced_by updated on 6 entries; 3 I&C conflict notes resolved
- **Systems index**: row 4 → Designed; tracker 3/16
- **Next**: /design-review design/gdd/player-character-movement.md in a FRESH session; /consistency-check; Grid Inventory owner to apply hand-backs 1–5 (R10 wording + position_stale, R13 bag at sea, +Mounted/+Dead contexts, OQ18 weight items, OQ23 closed)

## /design-system Mail & Destinations (2026-10-16, autonomous mode, subagent)
- **Task**: Mail & Destinations GDD (system #7, M-1a set) — **Status: Complete (Designed), pending independent /design-review in a fresh session**
- **File**: design/gdd/mail-destinations.md
- **Sections authored**: Summary, Overview, Player Fantasy (+ Feel & Comedy Feedback), Detailed Design (Core Rules R1–R18, States, Interactions), Formulas (F1–F11 + Determinism), Edge Cases, Dependencies (Sim/Presentation stated), Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements, Network, Cross-References, Acceptance Criteria (62), Open Questions (22)
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (7 items)
- **Registry**: +2 items, +6 formulas, +11 constants (6 proposed); referenced_by updated on 7 existing entries
- **Systems index**: row 7 → Designed; progress 4/16 MVP
- **Hand-backs to grid-inventory.md** (not edited): per-door complaint; Loot-mail expiry exemption (R12); D2 Intake-pressure figures wrong (8p shift 5 ≈ 1,035 cells); backlogCount → backlogUnits / per-op DeliveryOutcome
- **Next**: /design-review design/gdd/mail-destinations.md in a FRESH session; /consistency-check; batch 2 (Throwing & Catching, Manual Delivery, HUD & Notifications)

## /design-system HUD & Notifications (2026-10-16, autonomous mode, subagent)
- **Task**: HUD & Notifications GDD (system #24, M-1a set; incl. text chat, map pings, emotes) — **Status: Complete (Designed), pending independent /design-review in a FRESH session**
- **File**: design/gdd/hud-notifications.md
- **Sections authored**: Summary, Overview, Player Fantasy (+ Feel & Comedy Feedback), Detailed Design (Core Rules H1–H29, States and Transitions, Interactions), Formulas (F1–F8), Edge Cases, Dependencies (Sim/Presentation stated: Presentation-only, plain-C# Ui.Core), Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements (UX Flag), Network, Cross-References, Acceptance Criteria (~80 IDs), Open Questions (HN-1…HN-21 + ADR + engine-verify lists)
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (7 findings; rulings: "Fumbled" no exception, "dead letter" allowlisted)
- **Registry**: +5 formulas, +8 constants; referenced_by on 8 existing entries; YAML validated
- **Systems index**: row 24 → Designed; docs started 7; MVP designed 6/16; 8 dependency edges added
- **Hand-backs (not edited)**: HN-1 Input & Controls (wheel in Mounted, live-region digest, history/status bindings, tip slot); HN-2 Player Character (typing + speaking bits, shared Emote msg, pose suppression); HN-3 Grid Inventory (OQ25 answered, op-sound rate, "Fumbled", missing reason rows); HN-4 Mail/Manual Delivery ("posts 1" vs "drops 1", backlog tier vs manifest)
- **Not run**: network-programmer first spawn hit the concurrent-subagent limit (re-run successfully in round 2); no engine modules/ui.md or networking.md → feasibility not checked against pinned engine; /design-review intentionally not run (§5c)
- **Next**: /design-review design/gdd/hud-notifications.md in a FRESH session; /consistency-check
- **/design-review 2026-10-16**: MAJOR REVISION NEEDED → revision applied (HUD 1,581 → 670 lines; comms split to design/gdd/comms.md — #42, FROZEN behind the GUARD; seed design/ux/hud-notifications-seed.md). Status In Review. **Next**: re-review HUD in a FRESH session; /consistency-check

## /design-system Throwing & Catching (2026-10-16, autonomous mode, subagent)
- **Task**: Throwing & Catching GDD (system #10, M-1a set) — **Status: Complete (Designed), pending independent /design-review in a FRESH session**
- **File**: design/gdd/throwing-catching.md
- **Sections authored**: Summary (+ milestone slice), Overview, Player Fantasy ("Heads Up!" + Feel & Comedy Feedback + 4 fantasy tests), Detailed Design (Core Rules T1–T15, States and Transitions, Interactions), Formulas (F1–F8 + 18 ContentValidator rules), Edge Cases, Dependencies (Sim/Presentation split; provisional Health & Damage, Belt, Vehicles, Enemy AI), Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements, Network, Cross-References, Acceptance Criteria (TC- criteria), Open Questions (17)
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (7 findings)
- **CD ruling pending owner ratification**: execution variance = human (no dice; throwSpreadMrad 0, playtest-gated) — amends mail-destinations OQ8/R13 wording
- **Registry**: +5 formulas, +13 constants/rules; referenced_by updated on 11 entries
- **Systems index**: row 10 → Designed; MVP 7/16
- **Hand-backs** (not edited): Mail (OQ8/R13 wording, openAtRelease), Grid Inventory (throw payload + reply codes, CAUGHT{unitsIn, unitsFumbled}, close OQ6/7/24), Player Character (OQ16 answered; drop "throw release" one-shot), Input (OQ-IC-3 a–f answered; F10 spread note)
- **/design-review 2026-10-16**: NEEDS REVISION (13 blockers) → revision applied (1,449 → 1,307 lines; CD ruled the doc oversized, ≤ 0 % growth). CD-SENIOR: D1 the spread knob is retired (no dice, ever) and D2 preview colour is exact every frame, both pending owner ratification; D3 catch-preemption gate (hand-back to Input R4.3); D5 leaky-bucket cooldown. UI catalogue and beat timings moved to design/ux/throwing-catching-seed.md. Status In Review. **Next**: re-review in a FRESH session; /consistency-check

## /design-system Manual Delivery (2026-10-16, autonomous mode, subagent)
- **Task**: Manual Delivery GDD (system #9, M-1a set; S-sized: address-match prompt + hand insertion) — **Status: Complete (Designed), pending independent /design-review in a fresh session**
- **File**: design/gdd/manual-delivery.md
- **Sections authored**: Summary, Overview, Player Fantasy (+ Feel & Comedy Feedback, fantasy test), Detailed Design (Core Rules MD1–MD10, States, Interactions), Formulas (F1–F5), Edge Cases, Dependencies (Sim/Presentation stated), Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements, Network, Cross-References, Acceptance Criteria (42), Open Questions (14)
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (5 text fixes)
- **Quick Hands**: MATCH-only narrowing REVERSED at /design-review (CD ruling ii) — spec 10 "−50 %" on every deliver hold; absMinTicks retired; no user ratification pending
- **Registry**: +2 formulas, +6 constants (proposed); referenced_by on 6 entries
- **Systems index**: row 9 → Designed; tracker 5/16 MVP
- **Hand-backs (not edited)**: Player Character (Quick Hands 500 replaces "Carrier archetype" 300; spare flags byte), Input & Controls (fold insertEff/absMinTicks into shared holdEff; F12 range; G2 latched-payload cancel), Mail & Destinations (OQ5 answered; "posts 1"; per-hold pill aggregation), Grid Inventory (holdStartTick op field; strings), HUD ("posts 1"; branch table)
- **/design-review 2026-10-16**: NEEDS REVISION (6 specialists + CD) → revision applied (16 blocking + cheap recommended; host-domain holdStartTick clamp, priority 2, settle 6 ticks + tracker, mark-led prompt ≤ 4 lines, ACs rewritten); systems index → In Review; review log design/gdd/reviews/manual-delivery-review-log.md
- **Next**: re-review /design-review design/gdd/manual-delivery.md in a FRESH session; /consistency-check

## /design-system Simulation Core (2026-10-16, autonomous mode, subagent)
- **Task**: Simulation Core GDD (system #1, Core/Foundation) — **Status: Complete (Designed, Provisional), pending independent /design-review in a FRESH session**
- **GUARD**: systems-index M-1a GUARD overridden by owner instruction ("continue the rest of the planning without my input") — logged; GDD header carries the Provisional line
- **File**: design/gdd/simulation-core.md
- **Sections authored**: Summary, Overview, Player Fantasy (indirect: "Same tick, same story"), Detailed Design (Core Rules R1–R33, SimWorld + TickDriver lifecycles, Interactions, DEV-1…DEV-8, milestone slice), Formulas (F1–F8 incl. F3 per-phase tick-budget split + fixtures), Edge Cases, Dependencies (Sim/Presentation statement), Tuning Knobs, Visual/Audio Requirements, Game Feel, UI Requirements (no UX flag), Network (N1–N6), Cross-References, Acceptance Criteria (SC- criteria), Open Questions (OQ1–OQ13, hand-backs HB1–HB10, 7 ADRs)
- **Key decisions**: OQ20 resolved (own integer accumulator in a Godot-free TickDriver driven from one GameLoop _Process, cap 4 + guard, drop-not-bank, main thread; ADR + spike); 14-phase order incl. Combat.React; host tick p99 5 ms split; PCG32 + SplitMix64/FNV derivation with golden vectors; EntityId class table; determinism tiers T1/T2
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (6 text fixes)
- **Registry**: +7 formulas, +10 constants/rules; referenced_by on 7 entries; YAML validated
- **Systems index**: row 1 → Designed (Provisional); docs started 10; MVP 8/16
- **Hand-backs (not edited)**: HB1/HB2 Grid Inventory (automation not in FIFO, tick-end conservation, perf slice), HB3/HB6 Throwing (no-dice spin seed mentions, TC-P-2, clientTick vs Input R11), HB4 Mail (stream names, M-PERF-2 split, sync wallet), HB5 Player Character (P15 in Combat.React, Teleport API, PCM-PERF-1), HB7 Health & Damage, HB8 Manual Delivery + HB9 HUD (list Sim Core), HB10 Networking (no op resend)
- **Not run**: /design-review (by rule — fresh session); engine feasibility not checked against pinned engine (no docs/engine-reference/godot/modules/core.md)
- **Next**: /design-review design/gdd/simulation-core.md in a FRESH session; /consistency-check; Technical Setup spike for OQ1–OQ4

## /design-system Content & Data Definitions (2026-10-16, autonomous mode, subagent)
- **Task**: Content & Data Definitions (incl. StatSheet) GDD (system #2, Core/Foundation) — systems-index GUARD overridden by owner instruction (logged); GDD marked Provisional
- **File**: design/gdd/content-data-definitions.md (tier full: 8 sections + Summary + optional + Network)
- Sections done: Overview, Player Fantasy (FT-1..4), Detailed Design (CD1–CD28, states, interactions, catalogue 79+1 rules)
- Sections done: + Formulas F1–F6 (catalogue now 80 active + 1 retired)
- Sections done: + Edge Cases, Dependencies (Sim/Presentation stated), Tuning Knobs, Visual/Audio, Game Feel, UI (UX Flag), Network, Cross-References, Acceptance Criteria, Open Questions (OQ-CDD-1..17, spikes S1–S5), Summary
- **Status**: Complete (Designed), pending independent /design-review in a FRESH session
- **Sections authored**: Summary (+ milestone slice), Overview, Player Fantasy ("The Card Is the Contract", FT-1..FT-4), Detailed Design (Core Rules CD1–CD28, States and Transitions, Interactions, ContentValidator Rule Catalogue: 84 active + 1 retired), Formulas (F1 StatResolve, F2 DecimalToScaled, F3 ExactTickCheck, F4 DefIndex, F5 WorstCaseStat, F6 ContentHash), Edge Cases, Dependencies (Sim/Presentation stated), Tuning Knobs, Visual/Audio, Game Feel, UI Requirements (UX Flag), Network, Cross-References, Acceptance Criteria (64 CDD- criteria), Open Questions (OQ-CDD-1..17, spikes S1–S5)
- **CD-GDD-ALIGN**: CONCERNS → REVISED 2026-10-16 (6 items); Rank-4 reroll → opt-in "Second Opinion" stamp is a CD direction PENDING OWNER RATIFICATION (amends spec 11 §11)
- **Registry**: +3 formulas (statResolve, decimalToScaled, contentHash), +6 constants (statCatalogue_v1, ruleFlags_v1, contentValidatorCatalogue_v1, defIndexRule, maxMulFactors, contentFormat); referenced_by on 11 entries; conflict_note on throwSpeedByClass ("default light" vs weightClass required); YAML valid
- **Systems index**: row 2 → Designed; docs started 11; MVP 9/16
- **Next**: /design-review design/gdd/content-data-definitions.md in a FRESH session; /consistency-check; owners apply hand-backs OQ-CDD-1..12
<!-- CONSISTENCY-CHECK: 2026-10-16 | GDDs checked: 10 | Conflicts found: 19 | Log: docs/consistency-failures.md -->

## /design-review Simulation Core (2026-10-16, autonomous mode, subagent)
- **Verdict**: MAJOR REVISION NEEDED (11 blocking). Revision applied in the same session; awaiting re-review in a fresh session.
- **Files**: design/gdd/simulation-core.md (1,772 → 1,046 lines, 131 → 53 ACs); docs/architecture/simulation-core-adr-inputs.md (NEW, UNREVIEWED SEED); design/gdd/reviews/simulation-core-review-log.md (NEW, receipt f4b9959…); registry: 13 Sim Core entries updated; systems-index row 1 set to In Review (Provisional)
- **Next**: /design-review design/gdd/simulation-core.md (fresh session); hand-backs HB1–HB12 to their GDDs; TD writes the 9 ADRs from the seed; Technical Setup spike OQ1–OQ4

## /create-architecture (2026-10-16, autonomous, --review lean, subagent)
- **Artifact**: docs/architecture/architecture.md v1 (Document Status, Engine Knowledge Gap Summary, System Layer Map + physical assemblies, Module Ownership, Data Flow, API Boundaries, ADR Audit, Required ADRs, Principles, Open Questions Q-ARCH-01..12, Appendix A: 250 TRs from 10 GDDs, each mapped to one primary planned ADR)
- **Sign-off**: TD-ARCHITECTURE skipped (lean); LP-FEASIBILITY skipped (lean)
- **Layout decision**: keep /test-setup skeleton (src/PerformativeMail.Sim net8.0 incl. Content; tests/PerformativeMail.Sim.Tests net10.0 xunit.v3); add src/PerformativeMail.Client.Core (net8.0, Godot-free input router + HUD core) with the first Input/HUD story; Godot csproj at root later (src/Game/**, glob excludes, ProjectReference; linked-folder fallback). No separate Content assembly (CDD puts loader in Sim/Content).
- **ADRs required**: 31 reserved (0001–0031). Foundation-critical before sprint 1: 0001 layout/build/test, 0002 tick driver, 0003 phase order/composition, 0004 ids, 0005 RNG, 0006 numerics/determinism/dump, 0007 op queue/replies (reqId resolution), 0008 event log, 0009 InventoryOp pipeline, 0010 content/StatSheet, 0011 network adapter seam (transport half deferred to M0 spike).
- **Blockers**: none for the Godot-free Sim sprint once ADR-0001..0011 are Accepted. Godot .NET 4.7.1 build not installed → blocks OQ1/OQ4/OQ-IC-9/S1–S4 spikes and all Presentation (Q-ARCH-01).
- **Next**: /architecture-decision for ADR-0001..0011 (Foundation first) → /architecture-review → /create-control-manifest → M-1a epics/stories.

## /design-review grid-inventory.md — third review (2026-10-16, autonomous)
- Verdict: NEEDS REVISION (21 blocking contract/fixture items; prior 9 resolved) → revised same session; status In Review; CD: targeted check decides Approved, no fourth full review
- Hash as reviewed: grid-inventory 7e2267df…, entities 19b34445… (log: design/gdd/reviews/grid-inventory-review-log.md)
- Hand-backs (GI OQ27): Input & Controls/HUD toast binding; Manual Delivery claimed-position bytes; Throwing TC-E-5 wording; Networking 10 s-mean/burst/valve/ackedReqId ratification. ADR inputs touched: reqId-tagged events, ackedReqId, sort memo (version, statEpoch), phase-5 automation

## /design-system Health & Damage (2026-10-16, autonomous mode)
- Task: Designing health-damage GDD (system #34; Provisional — GUARD overridden by owner instruction 2026-10-16)
- File: design/gdd/health-damage.md (skeleton created; effective tier full → all 8 sections + Summary + Network)
- Current section: Starting (skeleton created); specialist round 1 (game-designer, systems-designer, network-programmer, creative-director) launching
- Rounds: R1 game-designer, systems-designer, network-programmer, creative-director (fantasy); R2 systems-designer (edge cases), qa-lead (ACs); gate creative-director CD-GDD-ALIGN CONCERNS → REVISED 2026-10-16
- **Task**: Health & Damage GDD — **Status: Complete (Provisional), pending independent /design-review in a fresh session**
- **File**: design/gdd/health-damage.md (452 lines, 32 ACs)
- **Sections authored**: Summary, Overview, Player Fantasy, Detailed Design (HD1–HD13, States, Interactions), Formulas (F1–F5), Edge Cases, Dependencies (incl. Sim/Presentation statement), Network, Tuning Knobs, Visual/Audio, Game Feel, UI Requirements, Cross-References, Acceptance Criteria, Open Questions (OQ-HD-1…8)
- **Registry**: +5 formulas, +4 constants; referenced_by on thrownDamage, spawnProtectMs. **Index**: row 34 Designed (Provisional)
- **Hand-backs**: HB-HD-1/3 Player Character, HB-HD-2 Simulation Core, HB-HD-4 Content & Data
- **Next**: /design-review design/gdd/health-damage.md (fresh session); /consistency-check; next index system

## /design-system Building System (2026-10-16, autonomous mode, subagent)
- Task: Designing building-system GDD (system #11; Provisional — GUARD overridden by owner instruction 2026-10-16)
- File: design/gdd/building-system.md (skeleton created; effective tier full → all 8 sections + Summary + Network; Gameplay → Visual/Audio + Game Feel required)
- Current section: Starting (skeleton created); specialist round 1 (game-designer, systems-designer, level-designer, ux-designer) launching
- Rounds: R1 game-designer, systems-designer, level-designer, ux-designer; R2 network-programmer, creative-director (fantasy), art-director; R3 qa-lead (ACs); gate creative-director CD-GDD-ALIGN CONCERNS → REVISED 2026-10-16
- **Task**: Building System GDD — **Status: Complete (Designed, Provisional), pending independent /design-review in a fresh session**
- **File**: design/gdd/building-system.md (512 lines, 46 ACs)
- **Sections authored**: Summary, Overview, Player Fantasy (+ Feel & Comedy Feedback), Detailed Design (B1–B15, States, Interactions), Formulas (F1–F6), Edge Cases, Dependencies (incl. Sim/Presentation split), Network, Tuning Knobs, Visual/Audio, Game Feel, UI Requirements (+UX Flag), Cross-References, Acceptance Criteria, Open Questions (OQ-BLD-1…17 + ADR list)
- **Key decisions**: Deconstruct = hold RMB/LT Build-context command on the build-cursor construct (closes OQ-IC-13; PC P10 row withdrawn); atomic multi-input charge over panel + PO Depot (≤ 20 m or SharedBuild); line = longest valid affordable prefix; salvage 1000/500 ‰ with team remainder bank; 3 s undo grace placer-only; refunds Grant→SpillWorld; non-empty container → one R13 spill bag; no terrain edits; no walls/gates on streets; buildable spawn pads out of v1
- **Registry**: +1 item, +6 formulas, +4 constants; referenced_by on holdEff, oneCommitRule, maxHpFormula. **Index**: row 11 Designed (Provisional); MVP 11/16
- **Owner to ratify (CD)**: no terrain edits vs spec 02 §4; OQ-BLD-11 feed names remover; Delivery fantasy test 15 %; spawn pads out of v1; spec 04/02/06 supersessions
- **Next**: /design-review design/gdd/building-system.md (fresh session); /consistency-check; hand-backs OQ-BLD-1…12

## /design-system Belt Logistics (2026-10-16, autonomous mode, subagent)
- Task: Designing belt-logistics GDD (system #12; Provisional — GUARD overridden by owner instruction 2026-10-16)
- File: design/gdd/belt-logistics.md (skeleton created; effective tier full → all 8 sections + Summary + Network; Gameplay → Visual/Audio + Game Feel required)
- Rounds: R1 game-designer, systems-designer, network-programmer, performance-analyst (done)
- Rounds: R2 creative-director (fantasy), systems-designer (edge cases + 13 consistency fixes), art-director (V/A, Game Feel); R3 qa-lead (ACs NEEDS FIXES → applied); gate creative-director CD-GDD-ALIGN CONCERNS → REVISED 2026-10-16 (C1–C7)
- **Task**: Belt Logistics GDD — **Status: Complete (Designed, Provisional), pending independent /design-review in a fresh session**
- **File**: design/gdd/belt-logistics.md (1,048 lines, 64 ACs)
- **Sections authored**: Summary, Overview, Player Fantasy ("Exactly What You Told It" + Feel & Comedy Feedback + fantasy tests 1–3), Detailed Design (BL1–BL21, States, Interactions), Formulas (F1–F6), Edge Cases, Dependencies (incl. Sim/Presentation split), Network, Tuning Knobs, Visual/Audio, Game Feel (GF1–GF7), UI Requirements (+UX Flag), Cross-References, Acceptance Criteria (BL-AC-1…64), Open Questions (OQ-BL-H1…H14, OQ-BL-1…8, ADR-0027 list)
- **Registry**: +4 formulas, +10 constants; referenced_by on 13 entries; YAML valid. **Index**: row 12 Designed (Provisional); MVP 12/16
- **Owner to ratify (CD)**: Pillar 3 reading for a solo-only "reason to move" failure (lot geography remedy); street-spread exponent ceiling e ≤ 1.0
- **Next**: /design-review design/gdd/belt-logistics.md (fresh session); /consistency-check; owners apply hand-backs OQ-BL-H1…H14; ADR-0027 before M-1b

## Session Extract — /architecture-review 2026-10-16
- Verdict: CONCERNS
- Requirements: 250 total — 203 covered, 0 partial, 47 gaps
- New TR-IDs registered: 250 (registry bootstrapped from architecture.md Appendix A; TR-delivery-008 revised)
- GDD revision flags: None (engine); GDD sync items: manual-delivery (24/28 B), grid-inventory R7 (DeliverMatching owner)
- Top ADR gaps: perf-budget.md, ADR-0022 JoinState & resync, ADR-0020 Transforms & Teleport
- Report: docs/architecture/architecture-review-2026-10-16.md
- Story status changes: network-adapter-loopback 008 payload blocker cleared (still Blocked on the Delivery-owned DeliverMatching descriptor story); grid-inventory 022 Ready → Blocked (re-scope, ADR-0009 erratum)

## UX design — inventory panel (2026-10-16, autonomous)
- **Task**: `/ux-design inventory-panel` (M-1a scope: hotbar + own panel + Intake as a plain grid), then `/ux-design throwing-catching`
- **File**: design/ux/inventory-panel.md
- **Current section**: inventory-panel — all sections written (Draft complete, awaiting /ux-review); next: throwing-catching
- **Throwing spec**: design/ux/throwing-catching.md — all sections written; lean /ux-review NEEDS REVISION → 5 blockers fixed (status In Review; confirmatory /ux-review recommended). throwing-catching story 019 → Ready; story 005 addendum (OQ-TC-4 keys); grid-inventory 024 already Ready
