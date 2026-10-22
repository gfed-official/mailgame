# Story 017: UnitLedger conservation counters, tick-end check, INVENTORY_ASSERT full scan and the canonical dump section

> **Epic**: Grid Inventory & Containers (M-1a slice)
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: L (~2–3 days)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-22 (readiness fixes against main c2d4a64, story 014 / PR #34 merged, checked against story 021 / PR #35 ab0ae6d: Manifest Version re-stamped; Decision Summary made precise (`spawned` / `granted`, the Cursor and holder states, sole writer); the crossing table covering 012–015 and 014's pickup/catch and 021's backlog append/drain; check record, counter, scan, IL, Recount seam, `TryInsert` duplicate (TD-145), dump field list, rejects, 0 B, determinism ACs; depends on 021; estimate M → L; Mail Lead planning defaults pending Evan, TD-160)

## Context

**GDD**: `design/gdd/grid-inventory.md`
**Requirement**: `TR-inventory-012` (O(1) conservation counters checked each tick; `INVENTORY_ASSERT` compiled out of Release)
*(Requirement text: `docs/architecture/tr-registry.yaml` (mirrored in the epic's GDD Requirements table).)*

**ADR Governing Implementation**: ADR-0009: InventoryOp Pipeline and Inventory Storage Layout (primary); ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay; ADR-0003: Phase Order, Composition Root and Cross-System Calls
**ADR Decision Summary**: A `UnitLedger` holds O(1) counters per location state (`Placed, Cursor, World, Flight, Belt, Backlog, Consumed, Destroyed`) for mail and per-itemId item units, plus `spawned`, `granted` (and `consumed`, `destroyed`); only the lowest mutator primitives touch it, and boundary crossings into Flight, Belt and Backlog happen only through `IInventoryApi` (ADR-0009 §6 l.169–170); Mail's units cross into Backlog at `IInventoryApi.AppendBacklog` (ADR-0017 §6 l.137); the tick-end check asserts `Σ live = spawned + granted − consumed − destroyed` in every build and writes an always-on record on mismatch (never throws); the full multiset scan runs behind `[Conditional("INVENTORY_ASSERT")]` (ADR-0009 §6). Inventory state is a canonical-dump section in ascending ids (ADR-0006 §4).
**ADR Version**: ADR-0009 2026-10-16 (errata through 2026-10-21); ADR-0006 2026-10-16; ADR-0003 2026-10-16; ADR-0017 2026-10-16 (§6, the Backlog crossing)

**Engine**: Godot 4.7.1 | **Risk**: LOW (plain-C# Sim; zero Godot references)
**Engine Notes**: `[Conditional("INVENTORY_ASSERT")]` binding per calling assembly in a Godot Release export is an OQ4 build-spike item (ADR-0001) — irrelevant to the Sim test project; verify in `godot-host-shell`.

**Control Manifest Rules (Core layer unless noted)**:
- Required: op handlers and `IInventoryApi` calls share one internal mutation layer, `InventoryMutator`; no system writes container, entry or ledger state directly (ADR-0009)
- Required: conservation via `UnitLedger` touched only by the lowest mutator primitives; tick-end Σ check in every build, always-on record on mismatch, never throws; full scan behind `[Conditional("INVENTORY_ASSERT")]` (ADR-0009 §6)
- Required (Foundation, ADR-0006 §4): `ISimStateContributor` writes fields one by one, little-endian, collections ascending by id, unique never-reused `SectionId`
- Required (Foundation, ADR-0001 §4): build-dependent behaviour only via `SIM_ASSERT`/`INVENTORY_ASSERT`
- Required: inventory interaction goes only through `IInventoryApi`, boundary crossings into Flight, Belt and Backlog included (ADR-0009 §6, manifest l.430); `Validate` never touches the ledger (ADR-0009 §2, manifest l.339)
- Re-checked against manifest 2026-10-17.1: current (revision .1 changes no rule this story cites).

---

## Acceptance Criteria

*From GDD `design/gdd/grid-inventory.md` (test IDs `H-…`), scoped to this story:*

- [ ] **Ledger shape** — `UnitLedger` (preallocated at world build) holds, for mail and for each itemId (array sized `DefTable<ItemDef>.Count`), count-weighted live counters `Placed, Cursor, World, Flight, Belt, Backlog`, `Consumed` split by `ConsumeReason` (3 sub-counters whose sum is Consumed), `Destroyed`, `spawned`, `granted`. The Cursor is its own state (`InventoryMutator.IsCursor`); an entry detached inside one call (mid-pickup) is in no state and is never seen at tick end. **`spawned`** counts a unit's first entry into Inventory custody: at `Spawn` placement, per placed unit, or at `AppendBacklog`, per appended unit; units `Spawn` leaves unplaced are Mail's and in no state (ADR-0017 §6 l.137). **`granted`**: no production grant exists at M-1a (Grant is M1); see the `Recount` seam AC. *(Mail Lead planning default, pending Evan, TD-160 items 2–4, 9.)*
- [ ] **Sole writer** — only `InventoryMutator` writes the ledger; holder crossings (World, Flight, Belt, Backlog) made today in `InventorySystem`, `WorldItems` and `PickupCore` go through two new mutator primitives `EnterHolder` / `LeaveHolder` (TD-160 item 1). An architecture test (`ArchitectureScan.BodyReferences` over the Sim assembly, as `tests/unit/architecture/*RulesTests.cs`) finds no ledger writer outside `InventoryMutator` and `UnitLedger`'s own test seams, with a seen-failing fixture.
- [ ] **Crossing table** — a fixed script (≤ 50 steps) exercises every row below, asserts each row's exact deltas, and asserts `Σ live = spawned + granted − consumed − destroyed` after every step for mail and every itemId:
  - spawn placement: spawned +n, Placed +n; spawn overflow left to Mail: no change; `AppendBacklog` (story 021): spawned +n, Backlog +n;
  - pick / split onto the cursor: Placed −n, Cursor +n; place / merge / move / quick_move / return / leash put-away (`PutAwayCursor`): source state −n, target state +n (Cursor → Placed, Placed → Placed); a move inside one container: no change;
  - `DetachForThrow`: Placed −n, Flight +n; `TryCatch` (story 014): Flight −unitsIn, Placed +unitsIn, and Flight −fumbled, World +fumbled; `CreateWorldItem`: World +n from Flight (Thrown) or from Placed/Cursor (Dropped, the caller's `Remove`);
  - pickup (story 014): whole — World −n, Placed +n; partial — World −moved, Placed +moved, the remainder stays World;
  - mail expiry / water return to the Intake: World −n, Placed +n; to the backlog (story 021, Intake full or backlog non-empty, also a cap-evicted mail): World −n, Backlog +n; drain (story 021): Backlog −n, Placed +n;
  - non-mail expiry / water / cap eviction (`DestroyNonMail` → `DestroyDetached`, story 013, TD-135): World −n, Destroyed +n;
  - `TakeUnit`: Placed −1, Belt +1; `TryInsert` (true only): Belt −1, Placed +1; `ConsumeUnit`: source state −1, Consumed[reason] +1.
- [ ] **H-X-2 (ledger clause, moved from story 015)** — after `DetachForThrow(slot, 88, 5)` the ledger shows Placed −5, Flight +5 for mail; after `ConsumeUnit(ref, Delivered, actor)` Placed −1, Consumed +1 and Consumed[Delivered] = 1.
- [ ] **Rejects change nothing** — a refused op, a false `IInventoryApi` call (`TryInsert`, `AppendBacklog`, `ConsumeUnit`, `DetachForThrow`), a refused `PutAwayCursor` plan, a drain skip and a refused catch (`{0, 0}`) leave every counter unchanged.
- [ ] **Recount seam** — `UnitLedger.Recount` (internal, test seam) recounts the live states from the entry pool, WorldItems, the backlog and the detached Flight entries (Belt = 0) and sets `spawned` / `granted` so Σ holds; the rigs (`InventoryEventRig`, `PlacementFixture`, …) call it after arranging entries outside the mutator, as they call `RebuildIndex` today; the existing inventory tests otherwise pass unchanged in all four legs.
- [ ] **Tick-end check (every build)** — `InventoryConservationCheck` (`ITickEndCheck`) checks, per ledger, Σ live vs `spawned + granted − consumed − destroyed` and any negative live counter. A deliberately corrupted `spawned` (test seam) makes the check write one always-on `SimCheckRing` record, new `CheckCode` 15 `InventoryConservationMismatch` (a = ledger: 0 = mail, itemId + 1, with state ordinal + 1 in bits 24–31 for a negative counter, 0 for the Σ check; b = Σ live − expected, or the negative value) and bump new `CounterId` 51 `InventoryConservationMismatches`; the tick completes with no exception in Debug and Release (the corruption touches only `spawned`, so the Debug scan does not trip). *(TD-160 items 5–6; the ring's per-code rate limit applies to the record, not the counter.)*
- [ ] **`INVENTORY_ASSERT` full scan** — `[Conditional("INVENTORY_ASSERT")]`, called from the check every tick: per-state recount from storage vs the ledger, and duplicate mailIds (live ids sorted into a preallocated scratch, adjacent equality); a finding asserts new `SimAssertCode` 84 `InventoryConservationScan`. Driven through `InventoryConservationCheck.Check` (the test assembly does not define `INVENTORY_ASSERT`): a seeded duplicated mailId and a seeded lost unit assert in Debug (`Assert.SkipUnless` Debug; each runs in the two Debug legs); one unskipped test asserts the check's body references the scan in Debug and not in Release (`ArchitectureScan.BodyReferences`, `TestBuildExpectation.SimAssertsExpected`).
- [ ] **Duplicate `TryInsert` (TD-145, narrowed)** — inserting a unit that was never taken drives Belt negative: in Release the check writes the negative-counter record; in Debug the scan names the duplicated mailId. Counters cannot refuse a per-id crossing; TD-145 is closed as narrowed (TD-160 item 8).
- [ ] **Dump** — the inventory `ISimStateContributor` section (SectionId 4, unchanged; fields appended after story 021's backlog block, so existing fixed-offset reads do not move) already writes containers (versions, `lastReplaceVersion`, `statEpoch`, `nextArrivalSeq`), WorldItems, the FIFO and the tombstone ring, and (021) the backlog; this story appends, in order: (1) the ledger — mail row, then itemId rows ascending, each live state, Consumed per reason, Destroyed, `spawned`, `granted`; (2) the entryId allocator (`EntryPool.LastId`); (3) live entries ascending by entryId (preallocated sort; pool slots are reused): entryId, container id, x, y, rotated, count, isMail, mailKind/itemId, addressId, flags, W, H, `arrivalSeq`, mailIds ascending; (4) the per-slot cursor origin (story 010: entry id, container id, x, y, rotated; 0 = none); (5) the per-slot open external (story 011, 8 × u32 in slot order, 0 = none). Not dumped (derived, rebuilt): outfeed nodes (story 012, TD-132 item 1), `PanelIds` and the open masks (TD-146). Two worlds built and driven identically produce byte-identical sections, and for each appended field class a world differing only in it produces a different section.
- [ ] **0 B, cost, determinism** — every crossing and the tick-end check allocate 0 B (`AllocAssert.Zero` plus the every-build twin, as `WorldItemAllocationTests`); each crossing is O(1), no scan in any primitive; the Release check is O(item defs), fixed at world build.

---

## Implementation Notes

*Derived from the governing ADR(s) Decision / Implementation Guidelines:*

- Location: `src/PerformativeMail.Sim/Inventory/Ledger/UnitLedger.cs`, `ConservationCheck.cs` (`ITickEndCheck`), `InventoryStateContributor.cs`.
- Counters are per location state × {mail} plus a per-itemId array sized from the ItemDef table (`DefTable<ItemDef>.Count`), preallocated.
- Retrofit: stories 005/012/013/014/015/021 call mutator primitives; this story adds the ledger calls inside those primitives (one place). The existing hook sites are marked `// story 017 ledger hook` (`InventorySystem.cs` `ConsumeUnit`, `DetachForThrow`, `CommitCatch`, `TryInsert`; on PR #35 also the drain and `TryAppendDetached`; `World/PickupCore.cs:280`) and `TODO(story-017)` (`World/WorldItems.cs` `DestroyNonMail`, TD-135); the mutator doc comments (`InventoryMutator.cs` l.34, 205, 420, 496, 541, 604) name the rest. Those stories' tests must still pass, given the `Recount` call in the rigs.
- Holder crossings move into `InventoryMutator.EnterHolder` / `LeaveHolder` (TD-160 item 1); `Place`/`Remove` pick Cursor vs Placed by `IsCursor`; each primitive branches on `e.IsMail` for the mail row vs the per-itemId row.
- `InventoryConservationCheck` (`InventoryPhases.cs`, bound with no arguments today) takes the ledger and the `SimCheckRing`; the next free `CheckCode` is 15 (`Core/CheckCode.cs`), `CounterId` 51, `SimAssertCode` 84, after story 021's 50 and 82. No new EventKind (0x008B stays free).
- §5 note (not this story's work): `TryCatch` (story 014 r1) and `AppendBacklog` (story 021) write ADR-0009 §5's always-on record on an arena refusal; `TryInsert` still refuses with only its no-fit facts (TD-154 item 7) — a code gap for a small hardening change, not 017; whether the internal `PutAwayCursor` is an "API caller" under §5 l.157 is an owner question (TD-160).
- `SimCheckRing` / always-on records: `production/epics/simulation-core/story-007-simcounters-simcheckring.md`; dump: `production/epics/simulation-core/story-019-canonical-state-dump.md`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 018: the H-R19-1 fuzz (its invariant (a) uses this story's `spawned`) and H-DET-1.
- A load path that reads the section back (TD-137, TD-146).
- A production `Grant` (M1), the item-form `TryInsert` and belts (M-1b, TD-144), death / disconnect cursor moves (M1).
- The §5 arena record in `TryInsert` / `PutAwayCursor` (TD-154 item 7, TD-160).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY 2026-10-22: GAPS, addressed by these fixes.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/inventory/UnitLedgerTests.cs` (shape, crossing table, H-X-2, rejects, check, scan, duplicate `TryInsert`), `tests/unit/inventory/InventoryStateContributorTests.cs` (dump), `tests/unit/inventory/UnitLedgerAllocationTests.cs` (0 B), and the sole-writer and IL cases in `tests/unit/architecture/` — must exist and pass in all four legs (net10.0/net8.0 × Debug/Release); a test skipped in one config runs and passes in the other (owner skip rule, decision log 2026-10-19)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005, Story 012–015 (all Complete; 014 merged in PR #34 as c2d4a64), Story 021 (PR #35, the Backlog append/drain crossings; start 017 after it merges)
- Depends on: `production/epics/simulation-core/story-007-simcounters-simcheckring.md`, `production/epics/simulation-core/story-019-canonical-state-dump.md`, `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (`INVENTORY_ASSERT`)
- Unlocks: Story 018

---

## Mail Lead planning defaults (pending Evan, TD-160)

*Not Evan's rulings; consistent with ADR-0009 §5–§6, ADR-0017 §6 and TD-150/152/153/154/155/157/158/159.*

1. Sole writer through two new mutator primitives `EnterHolder` / `LeaveHolder`; the as-built primitive list goes into the TD-144 as-implemented note (no ADR edit).
2. `spawned` = first entry into Inventory custody (`Spawn` placement or `AppendBacklog`); unplaced spawn units are Mail's.
3. `granted` and fixture-built live counts come from the `UnitLedger.Recount` test seam (no production Grant at M-1a).
4. The Cursor is its own state; a mid-call detached entry is in no state; `TryInsert` crosses Belt → Placed.
5. Check record `CheckCode` 15 with the payload above, plus `CounterId` 51; the corruption seam corrupts `spawned`.
6. Full scan every tick under `INVENTORY_ASSERT`, sort-based duplicate detection, `SimAssertCode` 84.
7. TD-145 narrowed: negative Belt in Release, the duplicate named by the Debug scan.
8. `Consumed` split by `ConsumeReason`.
9. Dump: append after 021's backlog block in the listed order; counters dumped; derived state not dumped.
10. Estimate L, dump completion kept in 017 (alternative for Evan: split entries / cursor origin / open external into a follow-on dump story).

**Owner question (not decided):** is `PutAwayCursor` an "API caller" under ADR-0009 §5 l.157? Recommended default: no (it is the leash side effect of `CloseExternal`, neither an op nor an `IInventoryApi` call; Evan's 5:11 AM PT ruling fixes only its behaviour), so §5 needs no text change; `TryInsert` gets the one-line record in a small hardening change.
