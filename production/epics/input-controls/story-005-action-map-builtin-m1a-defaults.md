# Story 005: The action map — `ActionDef`, aliases and the M-1a `BuiltInActionMap` with its default bindings

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~500 lines prod + tests, mostly table data)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/input-controls.md` (R1 action map, R3 grammar table, R6 default bindings + collision rulings, R10 reserved / required flags; ACs H-R1-1, H-R6-1)
**Requirement**: `TR-input-005` (action map: stable ids, grammar, predicate, defaults, flags, owner; alias table)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§2 `ActionDef`, `BuiltInActionMap`) — Accepted 2026-10-16
**ADR Decision Summary**: `ActionDef` = stable `ActionId` (u16) + string id + grammar + `LivePredicate` + `Delivery` (Frame / Immediate) + `FrameAction` bit (if Frame) + default KBM ×2 / pad ×2 bindings + Reserved + Required + owner GDD + alias ids. M-1a ships a code-built `BuiltInActionMap` (like `BuiltInContent`), validated at boot by the same validator. Bindings are Client.Core `PhysicalInput`s; **Godot's `InputMap` is not used for game actions at all**.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: bindings are Client.Core `PhysicalInput` names (stable, persisted by name); the action map is code-built data validated at boot (ADR-0012 §2)
- Forbidden: Godot `InputMap` actions for game verbs; a rename without an alias; any `Input.*` read
- Guardrail: action names are cached `ReadOnlySpan` literals; lookup by `ActionId` is an array index

---

## Acceptance Criteria

*From GDD R1, R3, R6 and H-R1-1 / H-R6-1, scoped to this story (M-1a rows only):*

- [x] **H-R1-1.** Loading an `ActionMap` from a set of `ActionDef`s validates: unique `ActionId` and string id, exactly one grammar, a live predicate, KBM and pad defaults (or an explicit `Unbound`), reserved / required flags and an owner GDD; a fixture missing any one field fails to load with a `ValidationReport` entry **naming the action id** (one fixture per field).
- [x] **H-R6-1 (M-1a rows, one test case per row).** `BuiltInActionMap.CreateM1a()` equals the GDD R6 table for each row — KBM binding(s), pad binding(s), grammar, live-in predicate, delivery, required/reserved flags: **On foot / carrying:** Move (axis; WASD / LS), Look (axis; mouse / RS; not live in Panel), Sprint (held state; Shift / L3; World + Holding-closed; not Panel), Jump (tap; Space / A), Interact (per target; E / X; World not aiming + Holding-closed), Throw (aim-release; RMB / LT; World only), Tool (tap; LMB / RT; World not aiming, not Holding-closed — stub), Hotbar select (tap / stepped; 1–8 + wheel / D-pad ←→; World not aiming, Holding-closed; digits also Panel), Drop (tap; Q / D-pad ↓; World not aiming + Holding-closed), the two "Throw / Tool pressed while holding" message actions (RMB, LMB / LT, RT; Holding-closed), Open panel (tap; Tab / Y; World + Holding-closed), Shoulder swap (tap; X / RB), Pause (tap; Esc, P / Menu; reserved Esc + Menu), Back (tap; Esc / B; reserved Esc; one action — Cancel aim, Close panel, Cancel/return and Esc-Pause are *ladder steps*, not separate bindings). **Panel / Holding-open:** Grid cursor (stepped; mouse, arrows / RS + D-pad), Pick-up / place (click-or-drag / tap; LMB, Enter / A), Rotate (tap; R / RB; Holding-open), Cancel/return (tap; RMB / B; Holding-open — RMB is a separate Cancel action live only here), Quick-move (chord; Shift+LMB, Shift+Enter / X; Panel), Close panel (Tab / B), Hotbar select 1–8 in Panel.
- [x] **Required / reserved.** Required (must stay bound per device): Move, Interact, Throw, Open panel, Back, Pick/place. Reserved (cannot be removed): Esc on Back/Pause, Menu on Pause. Flags are asserted per action.
- [x] **Deferred rows are absent, not hidden.** A test lists the R6 rows that are **not** M-1a (quick ping, comms wheel, chat, push-to-talk, history overlay, status read, action menu, external-panel focus switch, split / sort / take-all / page / jump / header rows, all "Later contexts") and asserts none is registered by `CreateM1a()`; the list is data, so a later milestone moves rows from "deferred" to "registered" deliberately.
- [x] **Aliases.** `ActionDef.Aliases` exists and `ActionMap.Resolve(string id)` returns the action for its id **or** any alias; two actions claiming the same alias, or an alias equal to another action's id, fail validation. (Applying aliased overrides at profile load is H-R1-3, M1.)
- [x] **Frame bits and delivery.** Each `Frame` action maps to a distinct `FrameAction` bit from story 002 (Interact, Jump, Sprint, Aim and the hotbar-step action); each `Immediate` action (Throw commit, inventory ops: pick/place, rotate, return, drop, quick-move; local verbs: Pause, shoulder swap, hotbar slot 1–8) has no frame bit. No action has both.

---

## Implementation Notes

*Derived from ADR-0012 §2 and GDD R6:*

- Files in `src/PerformativeMail.Client.Core/Input/`: `ActionId.cs` (stable `ushort`; append-only; never renumbered), `ActionDef.cs`, `Grammar.cs` {`Tap, Hold, AimRelease, HeldState, ClickOrDrag, Axis`}, `Delivery.cs`, `ActionMap.cs` (array indexed by id + id→def dictionary used **only for lookup, never iterated in ordering code**), `ActionMapValidator.cs` (the field checks here; the reachable-state conflict checks are story 006), `BuiltInActionMap.cs` (a static builder returning the immutable map).
- **Grammar for Interact:** every action has exactly one grammar (H-R1-1), and the target decides what Interact does (R4.2). Register Interact as `Grammar.Hold` with `TargetDriven = true`; story 012 reads the presented snapshot's `InteractMode` and treats a Tap target as an immediate-fire press. Record the choice in Last Updated.
- **Throw** is `AimRelease`. It contributes **two** things: the router-owned aiming state with an `Aim` held bit (Frame, so Player Character's `CarrierInput.Aiming` works) and an `Immediate` commit on release (story 014). Model as one `ActionDef` with `Delivery.Frame` for the held bit and a `CommitImmediate` follow-up flag — decide the exact representation here and document it.
- **Hotbar select:** digits 1–8 are eight `Immediate` local actions (`HotbarSlot1..8`); the wheel and D-pad ←→ are one stepped `Frame` action (`HotbarStep`, story 010). The consumer of both (the selected-slot state) is an **unowned seam** — see EPIC.md.
- **Drop / quick-move** are `Immediate` with sinks owned by Grid Inventory story 024 (Blocked); a missing sink is logged, not thrown (story 013).
- Every default binding uses story 003's `PhysicalInput`. KBM keys are *physical positions*; the label shown to the player comes from the layout API (adapter / HUD).
- `NumpadEnter` is an alias of the default Enter binding and is **not** stored in the secondary slot (Edge Cases); implement as a binding-level alias flag.
- Required-action and reserved flags are read by the Controls screen at M1; M-1a asserts them in tests only.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 006: conflict / chord validation over reachable states and the pad-path completeness check (H-FX-3a). Story 007+: runtime behaviour of the actions. Story 019: user overrides.
- M0 / M1 / M2 action rows (listed as deferred in EPIC.md).
- Glyph rendering of bindings (HUD).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/ActionMapTests.cs` (`[Trait("Id", "H-R1-1")]`), `tests/unit/input/DefaultBindingsTests.cs` (`[Trait("Id", "H-R6-1")]`, `[Theory]` over the table rows so a failure names one row) — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/input/ActionMapTests.cs, DefaultBindingsTests.cs

---

## Dependencies

- Depends on: Story 003 (`PhysicalInput`), Story 004 (`LivePredicate`, named states), Story 002 (`FrameAction`)
- Unlocks: Stories 006, 007, 009–019; `production/epics/grid-inventory/story-024-panel-interactions-feedback.md` (sinks register against these `ActionId`s)

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 6/6 passing (all covered by automated tests)
**Deviations**: ADVISORY only, logged as TD-081. Decisions: capture is data (only Pause and Back are live in captured states; TD-077(1) resolved for the map); `LiveIn` is a predicate union; Throw = one AimRelease Frame def (Aim bit) + `CommitOnRelease`; Interact = Hold + `TargetDriven`; Grammar gains Stepped; FrameAction appends HotbarStep = 4; PhysicalInput appends MouseMotion 0x0205 (`Mouse.Motion`); HotbarStep binds `InputBinding.StepPair(previous, next)`. Out-of-list files: InputBinding.cs, ValidationReport.cs, ValidationFields.cs, PhysicalInputs.cs (new); PhysicalInput.cs, PhysicalInputNames.cs, Sim/ClientLayer/FrameAction.cs, tests/unit/input/PhysicalInputTests.cs (append-only).
**Test Evidence**: Logic — tests/unit/input/ActionMapTests.cs (H-R1-1), tests/unit/input/DefaultBindingsTests.cs (H-R6-1); Input filter 509/509 net10 Debug; full suite excluding the in-flight MailDestinations tests 3,783 / 0 failed on all four legs (net10/net8 × Debug/Release).
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (+.NET) → LP 1/2/4/5 fixed in-story; QL-TEST-COVERAGE GAPS → gaps 1–4 fixed in-story.
