# Story 005: InventoryMutator, per-call versions, event records and InvVersion seals

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: In Progress
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M–L (~1 day)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-20
> **Depends on**: Story 004 (`InvReason`, `InvRequestCheck`) — Complete

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-002` (per-container u32 version +1 per mutating op; batch bumps once), `TR-inventory-019` (event before reply on one stream)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0008: Event Log, Replication Classes and Event Delivery; ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: One `InventoryMutator` with primitives `Place/Remove/Transfer` (+ `ConsumeUnit`, `DetachForThrow`, `Spawn`, `Destroy` later) is the only writer; versions bump +1 per call per touched container, deferred to the end of the call; records (`InvInserted`, `InvRemoved`, `InvMerged`, `InvMoved`, `InvSplitFrom`, `InvCursor…`) are raised, then exactly one `InvVersion{c, version_from, version_to, actorId, cause}` seal per touched container after its records; audience resolved at raise time (panel/cursor `OwnerOnly`, external `Targeted` to subscribers); mailIds only as chunked ascending runs on `Inserted`/`Merged`; Sim Core appends the reply after every record and seal (ADR-0009 §3–§4; ADR-0008).
**ADR Version**: ADR-0009 2026-10-16; ADR-0008 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: each container `version` bumps exactly +1 per mutating call that touched it; a no-op bumps nothing; exactly one `InvVersion` seal per touched container per call, after that container's records (ADR-0009 §3–§4)
- Required: `mailIds` travel only as chunked ascending runs `(start u32, len u16)` on `Inserted`/`Merged` (`Continuation` entries) (ADR-0009 §4)
- Required (Foundation, ADR-0008): `EventEntry` 64 B, payload ≤ 48 B, padding-free, string-free, no `Int128`; per-call coalescing only
- Forbidden: bumping a version per record; applying records before the seal (ADR-0009)
- Forbidden: routing automation through the inventory op FIFO; an event-per-mutation with embedded version pairs instead of the seal (ADR-0009 Alts 1–2)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R7-1** — GIVEN chest-like container C at v10 and A's inventory at v4, players A and B subscribed to C (B cannot see A's inventory), WHEN A's move transfers entry E C → inventory, THEN C = 11, inventory = 5; A receives C's `InvRemoved{E}` + seal v10→11 (`actorId = A`) and the inventory's `InvInserted{E}` + seal v4→5; B receives only C's records + seal; WHEN A's next move is rejected, THEN versions unchanged and B receives no event. (C is a test-fixture external container; chest proper is M-1b.)
- [ ] **Seal test (ADR-0009 Validation Criteria)** — for every mutator call shape (single-container, cross-container, no-op), each touched container receives exactly one `InvVersion` after its records and before the `OpReply`; a no-op (move onto own cells) raises nothing and bumps nothing.
- [ ] A 20-unit `InvInserted` carries its mailIds as ascending runs in `Continuation` entries of the same kind and `CallSeq` (8 runs per 48 B); `InvRemoved` carries no mailIds; every payload struct is padding-free (size test).
- [ ] Panel (hotbar, player_inventory) and cursor events are `OwnerOnly` to the owning slot; an external container's events are `Targeted` to its subscriber set resolved at raise time (subscription = open set, story 011).
- [ ] **Arena exhaustion → reason 85** — when the validator's arena reservation fails (`MailIdArena.CanReserve` false; the arena's `CheckCode.InventoryArenaExhausted` = 12), the op is rejected with `capacity_exhausted` (reason 85) and nothing is mutated (assert the full container state and the arena state unchanged). Caller rule: the validator passes `CanReserve` every block the op acquires, each sized at the moment it is acquired — including a union with a none input, a `TryAppend` on a none handle (= create), and chained unions (|a|+|b|, then |a|+|b|+|c|); omitting one is a false accept. (ADR-0009 §2/§5; source: story 003 scope note, TD-102.)
  - *Clarified (Mail Lead readiness, 2026-10-20):* this story defines the **shared validation step** every handler's `Validate` calls once its plan is built: it takes the op's acquired block sizes (one entry per block the commit acquires, each sized at the moment it is acquired) and calls `MailIdArena.CanReserve(ReadOnlySpan<int>)`; on false it rejects with `InvReason.CapacityExhausted` (85) and makes no mutation. Until handlers exist (stories 007+), it is exercised through a **test-only fixture op**.
  - `CanReserve` is a dry run and writes **no** check record; the `InventoryArenaExhausted` record (12) comes only from a real acquire failure. The AC asserts on the **reply** (reason 85), not on record 12 (the test asserts the check ring is unchanged).
- [ ] **Caller rule (test, not a note)** — a validator-path test over the three shapes the arena tests use (multi-block acquire, none-input union / `TryAppend` on a none handle, chained unions |a|+|b| then |a|+|b|+|c|): with the arena at exhaustion, passing every acquired block yields reason 85 with nothing mutated, and a deliberately under-sized request (one block omitted) passes `CanReserve` — the false accept the rule exists to prevent — while the real commit then fails to acquire.
- [ ] **Never alias `out` to an input (TD-104)** — `MailIdArena.TryUnion(in a, in b, tick, out result)` guards against `result` aliasing `a` or `b` with an `INVENTORY_ASSERT` trip carrying a new `SimAssertCode`, matching the existing assert conventions; a test pins the trip; TD-104 is closed (or narrowed) with a pointer to the guard and its test.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/InventoryMutator.cs`, `Events/` (record and seal payload structs, kind registration via `BindEventKind`).
- `InvPlan` (ADR-0009 §2) is a fixed-size struct with `[InlineArray]` buffers for ≤ 4 touched containers and the placement result; this story defines it and the touched-set bookkeeping used by every handler.
- `reqId` tagging of the actor's own entries is the adapter's (`Deliver`, ADR-0008 §7 (d)) — records/seals carry no `reqId`.
- `cause` and `byActorId` for contested outcomes: plumbed here (seal field + reply extra), first exercised by stories 013/014 (tombstones, `container_gone`).
- Use Sim Core's event log writer and call scopes (`production/epics/simulation-core/story-015-event-log-call-scopes-delivery.md`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 017: the `UnitLedger` hooks inside the primitives.
- M0: `GroupCounts`, `ContainerDelta` packing (adapter), `InvReplace` (sort, M1).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/InventoryEventsTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002, Story 003
- Depends on: `production/epics/simulation-core/story-015-event-log-call-scopes-delivery.md`, `production/epics/simulation-core/story-020-phase0-drain-prechecks-replies.md` (reply after events)
- Unlocks: Story 007–018
