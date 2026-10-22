# Story 019: Range-gated Open and Pickup handlers and the leash auto-close (P13 chain, F5 range)

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

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
- [ ] The `Open` OpKind handler runs the P13 chain then story 011's `TryOpenExternal`; opening `po_intake` at its console anchor adds it to the open set (the M-1a Intake path).

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011, Story 014
- Depends on: `production/epics/player-character/story-019-host-checkinteraction-position-ingest.md` (`IPlayersApi.CheckInteraction`, leash query; ADR-0014 Accepted)
- Unlocks: Story 024; manual-delivery epic
