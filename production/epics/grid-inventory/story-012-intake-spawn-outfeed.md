# Story 012: Spawn into the Intake (NoMerge, arrivalSeq) and the outfeed FIFO (PeekOutfeed / TakeUnit)

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M (~1 day)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-010` (outfeed by arrivalSeq; Peek/Take ≤ 4 entries amortised — pulled forward with Spawn, belts are M-1b), `TR-inventory-001` (`system` actor path via `IInventoryApi`)
*(No `docs/architecture/tr-registry.yaml` exists yet; requirement text is in `docs/architecture/architecture.md` v1.1 Appendix A, mirrored in the epic's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: `Spawn(container, units, NoMerge)`: one unit per entry, first-fit; every insertion into the Intake is NoMerge; a returned entry enters whole; each Intake entry gets a per-Intake `arrivalSeq` (u32); outfeed = an index-linked FIFO of `(arrivalSeq, entrySlot, entryId)` nodes with lazy tombstones; `PeekOutfeed` returns the lowest mailId of the lowest-`arrivalSeq` live entry (ADR-0009 §5; GDD R14).
**ADR Version**: ADR-0009 2026-10-16; ADR-0003 2026-10-16

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: None — no Godot API touched (ADR-0009 Engine Compatibility: Knowledge Risk LOW, Post-Cutoff APIs none).

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Forbidden: a binary heap for the Intake outfeed, a timing wheel for WorldItem/bag/water expiries, a per-entry `List<uint>` for `mailIds` (ADR-0009 Alts 3–5)
- Guardrail: probe counters compiled into Release, one increment per cell probe or candidate visit; `PeekOutfeed`/`TakeUnit` ≤ 4 candidates amortised; cap eviction ≤ created + 1 (BLOCKING in CI)
- Forbidden: routing automation through the inventory op FIFO; an event-per-mutation with embedded version pairs instead of the seal (ADR-0009 Alts 1–2)
- Required: in-tick callers use synchronous `IInventoryApi` in their own phase (ADR-0009 Decision; ADR-0003 §5)

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **H-R14-1** — GIVEN an empty Intake, WHEN 3 letters to #4 (ids 100–102) spawn NoMerge, THEN 3 entries of count 1 at (0,0),(1,0),(2,0) in id order; `PeekOutfeed` = 100; after `TakeUnit(FIFO)` → 101 and (0,0) is empty; GIVEN a returned letter id 50 is then inserted, THEN it is a separate count-1 entry and `PeekOutfeed` is still 101 (arrival order, not mailId).
- [ ] **H-R14-4 (whole-return half)** — GIVEN an Intake holding a 5-letter entry to #9, WHEN a 20-letter entry to #9 (ids 200–219) is returned through the return-to-Intake API, THEN the Intake gains exactly **one** new entry of count 20 with one new `arrivalSeq`, the resident entry is unchanged (NoMerge), Intake version +1.
- [ ] **H-PERF-6a** — `PeekOutfeed` and `TakeUnit(FIFO)` visit ≤ 4 entries per call amortised over 10 000 calls at n = 320 and on an 80×80 test container (n = 6 400); fails on any O(n) scan (Release probe counter).
- [ ] `SpawnResult` reports how many units were placed and returns the unplaced remainder in order (Intake full); a spawn while the caller signals a non-empty backlog places nothing ("nothing overtakes the backlog"); the backlog append/drain itself is story 021.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Intake/` — `OutfeedFifo.cs` (shared node pool, compaction when tombstones exceed live nodes), `IInventoryApi.Spawn/PeekOutfeed/TakeUnit` in `InventoryApi.cs`.
- Actor for spawns/returns is `ActorId.System`; mail ids come from Mail (`MailIds` allocator, `production/epics/simulation-core/story-018-id-allocators-slottable.md`) — Spawn receives `ReadOnlySpan<MailId>`.
- The Intake-sort FIFO rebuild and Presort street selectors are M1/M-1b; `TakeUnit(FIFO)` on non-Intake containers (min-heap, inserters) is M-1b automation.
- Intake insertion uses AutoPlace with `NoMerge` (story 009).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 021: backlog append and the `IntakeDrain` phase (Blocked on ADR-0017).
- Story 013: WorldItem expiry/water returns call the return-to-Intake API.
- M-1b: inserter/belt `TakeUnit` selectors, Presort.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/IntakeSpawnOutfeedTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 009
- Depends on: `production/epics/simulation-core/story-018-id-allocators-slottable.md`
- Unlocks: Story 013, Story 018, Story 021; mail-destinations epic (spawner)
