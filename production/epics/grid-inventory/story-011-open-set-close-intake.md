# Story 011: Open-set bookkeeping, Close op, leash put-away hook — and the Intake as the M-1a external

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-001` (Close through the FIFO); open-set consistency rule of GDD R10
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0008: Event Log, Replication Classes and Event Delivery
**ADR Decision Summary**: A player's open set = own panel + cursor + at most one external container; opening another closes the first; ops on containers outside the set → `not_open` (decided during validation); external containers' records are `Targeted` to their subscriber set (= the players that have them open) resolved at raise time (ADR-0009 §2, §4 rule 1; GDD R10).
**ADR Version**: ADR-0009 2026-10-16; ADR-0008 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: every handler is `Apply = Validate (pure; writes only a stack-allocated InvPlan) → Commit (never fails, never recomputes) → Reply`; `Validate` never allocates ids, draws RNG, raises events or touches the ledger (ADR-0009 §2)
- Required: audience resolved at raise time from the container's subscriber set; panel and cursor `OwnerOnly` (ADR-0009 §4)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R10-2** — GIVEN A has external C1 open, WHEN A's open set gains C2 (through the internal `TryOpenExternal(slot, c)` used by the range-gated `Open` handler of story 019), THEN C1 is closed for A (a later op on C1 → `not_open`) and B, who also has C1 open, is unaffected (`ok`); WHEN B sends `move {from: A.inventory}` → `not_open`.
- [ ] `Close {container}` op removes the external from the actor's set (no range gate); closing a container not open → `ok:true`, no-op.
- [ ] **Leash put-away hook (H-R10-1 put-away clause)** — `CloseExternal(slot, cause: leash)` closes the external and, in the same call, puts a cursor entry away into the panel by the R9 cursor row (AutoPlace); with a full panel it stays on the cursor. (The leash *distance* trigger is story 019.)
- [ ] Subscriber sets drive event audiences: after A opens the Intake, a spawn into the Intake reaches A's records; after A closes it, A receives no Intake records (group counts for non-openers are M0).

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/OpenSet.cs` (per-slot external `containerId` or none; per-container subscriber bitmask of 8 slots — preallocated).
- **Decision (recorded in `production/session-logs/decision-log.md`) — M-1a Intake access:** `po_intake` is the solo greybox's **one external container** under GDD R10 (its console is the interaction anchor). At M-1a the entry-level ops apply to it unchanged: `pick`, `move`, `merge`, `quick_move {entryId}`, `return`. It is **drawn with the grid view** until the manifest view lands. D7 would flip it to manifest (320 > 160 cells), but R18 is M1 and the panel stories are Blocked anyway. Still deferred:
  - `MoveGroup`/`QuickMoveGroup`/`TakeAll` (M1, R18);
  - `GroupCounts` for non-openers (M0);
  - contention UI (M0).

  This follows R10 (any external, the Intake named) and R18 ("any op still requires `open`"). It adds no new mechanic. The UI phase tag "external containers M0+" is pulled forward for the Intake only.
- The range-gated `Open` OpKind handler (P13 chain, 2.5 m + 0.5 m slack) and the 3.0 m leash trigger need Player Character's interaction framework (ADR-0014, not written) → story 019 (Blocked). This story exposes `TryOpenExternal` and `CloseExternal` for it; tests call them directly.
- Mounted cargo as an open external is M-1b (vehicles).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 019: range-gated `Open`/`Pickup` handlers and the leash distance check (Blocked).
- M0: `GroupCounts`, contention chips, optimistic client.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/OpenSetTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 005
- Unlocks: Story 007, Story 010, Story 019, Story 024
