# Story 019: Range-gated Open and Pickup handlers and the leash auto-close (P13 chain, F5 range)

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Complete (greybox; Open only — Pickup and the leash auto-close deferred → TD-230 (1))
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-11-09 (M-1a package cleanup: Complete (greybox; Open only — Pickup and the leash auto-close deferred → TD-230 (1)); delivered by PR #87, merge 0490e0f; see Completion Notes).

> **Unblocked 2026-10-16**: ADR-0014 and ADR-0013 are Accepted (owner-confirmed batch; `technical-director` acceptance). Re-derive the ADR notes below from the Accepted text at `/story-readiness`. The range gate is `IPlayersApi.CheckInteraction` (ADR-0014 §5), and `ClaimedPosition` (12 B) rides in the payload.

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-001`; GDD R10 open/range/leash; consumes `TR-movement-011..014` (interaction framework)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0014: Interaction framework & hold timing — Accepted 2026-10-16 (primary); ADR-0009: InventoryOp Pipeline and Inventory Storage Layout; ADR-0013: Carrier kinematic solver & fixed-point geometry — Accepted 2026-10-16
**ADR Decision Summary**: Range-gated ops (`Open`, `Pickup`) run Player Character P13's chain first (`dead → frozen → no_target → unavailable → out_of_range / position_stale`) via `IPlayersApi`, then their own chain (ADR-0009 §2); the interaction framework, anchor registry, range/leash measurement and hold timing are ADR-0014's; carrier positions are ADR-0013's — both Accepted 2026-10-16.
**ADR Version**: ADR-0014 2026-10-16; ADR-0009 2026-10-16; ADR-0013 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (Sim half) — re-assess when ADR-0014/0013 are written
**Engine Notes**: Re-derive when ADR-0014 lands.

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required (Feature, ADR-0003 §5): `IPlayersApi` only as a declared `SystemGraph` dependency

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R10-1** — chest-point claim 3000 mm from the anchor (2.5 m + 0.5 m slack) → `open` ok; 3001 mm → `out_of_range`; no transform for 30 ticks → `position_stale`; open and newest chest point at 2.99 m → stays open; at 3.01 m → closed that tick (story 011's `CloseExternal(leash)`, cursor put away), next op `not_open`; a stale position never closes it; the Intake console anchor at 2.0 m while 9 m from the building centre → `ok`. (Mounted-cargo clauses: M-1b.)
- [ ] The `Pickup` OpKind handler runs the P13 chain then story 014's `PickupCore`; `pickup` within 2.5 m (+ slack) succeeds, beyond → `out_of_range`; a floating item → `unavailable` (allowed while swimming), mapped in the P13 chain **before** the core runs (the core checks floating only after `container_gone` / `stale_entry`; TD-154 item 4).
- [x] The `Open` OpKind handler runs the P13 chain then story 011's `TryOpenExternal`; opening `po_intake` at its console anchor adds it to the open set (the M-1a Intake path).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- When ADR-0014 is Accepted: bind `IPlayersApi` (declared dependency), map its range verdict codes into the inventory reply (P13 codes are declared on Player Character's descriptors, ADR-0009 §2), and add the per-tick leash check (newest sample, never on a stale position) in the phase ADR-0014 assigns.
- Claim-and-corroborate (F5 history ring, 9+3 ticks) is ADR-0020 (M0); at M-1a solo the claim is the newest local position — confirm against ADR-0014's M-1a scope.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 011: open-set bookkeeping (Ready).
- Story 014: pickup core (Ready).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/RangeGatedOpsTests.cs` — must exist and pass

**Status**: [x] Created and passing — `tests/unit/player/OpenTests.cs` (the handler lives in Players, so not `tests/unit/inventory/RangeGatedOpsTests.cs`), PR #87 (2026-11-09)

---

## Completion Notes

*M-1a package delivery, recorded by Mail Lead's cleanup pass 2026-11-09 (owner ruling 2026-11-08 10:18 PM PT, card t299s1348: "Yes, switch to one PR per package and update the story files in one pass at the end"). Uncredited Mail Dev detail unless a quote says otherwise.*

- **Delivered by:** PR #87 (merge `0490e0f`), package WP-D (M-1a wave 1), merged 2026-11-09 under the merge rule "Merge on Review READY plus green CI, and only flag ADR changes to me" (Evan 2026-11-08 8:52 PM PT, card t292s1310). `OpenInteractable` was made public and reused by the client's own anchor registry in PR #94 (`f145219`, `ClientInteractionBinder`, TD-270 (1)); the live Intake pickup sends Open → `take_all` → Close from the console (TD-270 (6)).
- **ADR (the only ADR change in WP-D):** ADR-0009 erratum 2026-11-08, `Open` ownership — approved by Evan 2026-11-08 9:51 PM PT by card click on Mail Lead's card 'Grid-inventory 019: approve the ADR-0009 change that moves Open into the player code, like Drop?' (card t295s1326; listing message t295s1325): "Approve the ADR-0009 erratum for grid-inventory 019: the Open handler lives in Players and Inventory exposes primitives, like the Drop erratum". The range-gated `Open` handler lives in `Sim/Players/Ops` and Inventory exposes `ValidateOpenExternal` / `CommitOpenExternal` primitives; the story's "bind `IPlayersApi`" note is superseded. The id `0x0041` (`0x008C` retired), reason codes 86–88, the chain `bad_request → P13 → container_gone` and the `po_intake` anchor registration in `Bind` are uncredited Mail Dev detail (TD-230 (1)); `InvMalformedOpTests.GoldenFuzzHash` re-pinned because Open left the inventory fuzz kinds.
- **ACs met:** AC-3 (the `Open` handler runs the P13 chain then `TryOpenExternal`; opening `po_intake` at its console anchor adds it to the open set).
- **Deferred or partly met (greybox trims, Mail Lead planning default, M-1a regroup plan §3 "Open only, no leash"; held by the package TD row):** AC-1 — the 3000 / 3001 mm range gate and `position_stale` run through `CheckInteraction`; the per-tick leash auto-close (`CloseExternal(leash)`) is not built → TD-230 (1). AC-2 — the `Pickup` handler is not built (Drop is Blocked story 020) → TD-230 (1).
- **Unowned seam built in the same PR:** the `HotbarSelect` op (`0x0042`, 4 B) and the host selected slot (TD-230 (2)).
- **Tests:** `tests/unit/player/OpenTests.cs`, `tests/unit/player/HotbarSelectTests.cs`; four legs green (0 failed).

---

## Dependencies

- Depends on: Story 011, Story 014
- Depends on: `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md` (`IPlayersApi.CheckInteraction`, leash query; ADR-0014 Accepted)
- Unlocks: Story 024; manual-delivery epic
