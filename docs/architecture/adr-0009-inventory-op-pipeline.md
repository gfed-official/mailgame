# ADR-0009: InventoryOp Pipeline and Inventory Storage Layout

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist`. TD-ADR skipped (lean mode). Inputs: grid-inventory.md (In Review), simulation-core.md (Provisional), architecture.md v1.

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Grid Inventory owns the most replicated, most contended state in the game, and its GDD hands the request pipeline and the storage structures to an ADR. This ADR fixes how an inventory request is decoded, validated in a fixed precedence chain, committed atomically across one or more containers, versioned, announced as per-container event records sealed by a version record, replied to after its events, and checked by O(1) conservation counters; and it fixes the Sim-side storage: struct-array entry pool, per-container occupancy bitmaps, a size-class arena for `mailIds`, index-linked outfeed FIFOs with lazy tombstones, per-duration-class WorldItem expiry FIFOs, and a per-container StackKey → non-full-stacks index.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Core (plain-C# Sim; no engine surface) |
| **Knowledge Risk** | LOW for this ADR. The engine version is HIGH risk (4.4–4.7 post-cutoff) but nothing here touches a Godot API: the pipeline lives in `PerformativeMail.Sim` (zero Godot references) and the optimistic client in `PerformativeMail.Client.Core` (Godot-free). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (no `modules/` directory exists) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | (1) The transport guarantee this ADR *requires* (reliable, ordered delivery of one peer's event records and replies on one channel) is NOT SOURCEABLE for Godot 4.7.1 and is verified by the ADR-0011 transport spike (Grid OQ12) before M0. (2) `[Conditional("INVENTORY_ASSERT")]` binding per calling assembly in a Godot Release export: OQ4 build spike (ADR-0001). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (phase order, `IOpHandler`/`ITickEndCheck` binding, composition root, `I…Api` rule); ADR-0004 (`EntryId`, `MailId`, `EntityId` class `world_item`, `PlayerSlot`, temp-id namespace); ADR-0006 (integer-only, determinism checklist, canonical dump, 0-alloc tick); ADR-0007 (64 B `Op`, ≤ 48 B payload, `Reply` extra ≤ 24 B, reason namespace ≥ 64, validate-then-mutate); ADR-0008 (event entry layout and payload ceiling, `Targeted`/`OwnerOnly` classes, raise-time `audienceMask`, per-call coalescing, large payloads by reference); ADR-0010 (`ContainerDef`/`ItemDef`/`MailKindDef` fields, `IStatSheet` reads of `MailMaxStack`, `InventoryRows`, `WeightPenaltyPm`) |
| **Enables** | ADR-0017 (mail table, backlog queue shared with the drain), ADR-0021 (flight protocol: `throw` detach / `TryCatch`), ADR-0022 (JoinState inventory block, `ContainerChecksum`), ADR-0025 (narrowed: sort algorithm, backlog sub-queues, perf verification) |
| **Blocks** | Grid Inventory epic (all Sim stories); Manual Delivery (`deliver_matching`); Throwing & Catching (`throw`, `TryCatch`); Mail & Destinations spawn/backlog stories |
| **Ordering Note** | ADR-0011 (adapter seam) is a sibling, not a prerequisite: this ADR states the ordering contract the adapter must honour; ADR-0011 enforces it. The optimistic client (Client.Core) is specified here and built at M0. |

## Context

### Problem Statement
Every inventory interaction — a drag, a split, a sort, an inserter pull, a catch, a deliver hold — mutates containers that up to eight peers may be watching, while the clients render optimistically. Without one fixed pipeline the classic failures appear: double-applied ops, phantom entries after a reply overtakes its event, a move landing at a spot the player never saw after someone else's sort, a unit duplicated or lost across a cross-container move, and an O(n) scan hidden in a per-tick endpoint call. The GDD (grid-inventory.md, "→ becomes an ADR") explicitly defers the pipeline and its storage to this ADR, and the first implementation sprint (M-1a) builds Grid Inventory on it.

### Constraints
- The Sim is plain C# (`net8.0`), integer-only, deterministic, and allocates 0 B per tick after warm-up (SC R27–R32).
- One op is the atomic unit; a handler validates fully before its first mutation; a reject changes nothing but its reply (SC R8).
- Player ops arrive through the op FIFO in phase 0; automation and other in-tick callers use synchronous `IInventoryApi` calls in their own phase (SC R9). A player op beats an inserter on a contested cell.
- Event entries are fixed-size (ADR-0008); a `Removed` of a 255-unit stack carries 1 KB of `mailIds`, so large payloads must travel by reference.
- `Reply.extra` is ≤ 24 B (ADR-0007).
- Perf: inventory share ≤ 1.0 ms median / 2.0 ms p99 (advisory); probe counts and allocation bytes are BLOCKING in CI now (H-PERF-1, H-PERF-6).
- Godot multiplayer behaviour is NOT SOURCEABLE; nothing here may depend on it.

### Requirements
- R7 op set and semantics, R4/R7 fixed reason precedence, R2/R3 entry and StackKey shape, R6 oldest-first, R8 AutoPlace, R12 WorldItem cap and expiry, R14 outfeed/backlog/drain, R17 `CountItem`, R19 conservation.
- Network section: events before replies on one stream; per-container versions with `version_from..version_to`; cross-container ops as one event per container; `baseVersion`/`lastReplaceVersion`; temp ids; `reqId` tagging of own events.
- H-PERF-6: outfeed ≤ 4 entries visited amortised (n = 320 and 6,400), sort ≤ 8,500 probes and 0 on a memoised repeat, drain ≤ 8,500 probes and 0 when idle, cap eviction ≤ created + 1 candidates.
- H-PERF-5: ≤ 1 MB managed heap for all v1 containers + 512 WorldItems.

## Decision

The inventory system is one Sim module, `PerformativeMail.Sim/Inventory/`, composed by the composition root (ADR-0003). It binds:

| Binding | What |
|---|---|
| `BindOp` × N | one `OpKindDescriptor` + `IOpHandler` per inventory op (table below) |
| `Bind(PhaseId.ItemsExpire, …)` | WorldItem expiry + cap eviction (phase 1) |
| `Bind(PhaseId.IntakeDrain, …)` | backlog drain (phase 2) |
| `BindTickEnd(…)` | conservation counter check (R19) |
| `BindEventKind` × M | the inventory record and seal kinds (below) |
| `BindStateContributor` | canonical dump section (ascending ids) |
| `BindSnapshotSource` | per-container snapshot writer used by subscribe/open/resync and JoinState (envelope: ADR-0022) |

It publishes `IInventoryApi` (Spawn, TryInsert, TakeUnit, PeekOutfeed, TryCatch, DetachForThrow, ConsumeUnit, Grant, CountItem, ConsumeItems, SpillToBag, CreateWorldItem, …) for in-tick callers. **Op handlers and API calls share one internal mutation layer** (`InventoryMutator`), so an op and an API call that do the same thing produce identical state, versions and events — only the reply differs.

### 1. Op kinds and payloads

Each GDD op is its own `OpKind` (u16, inventory block `0x0080–0x00FF` per ADR-0007 §4, values never reused). Payloads are explicit-layout structs ≤ 32 B (unit test per kind, ADR-0007's 48 B ceiling). The `reqId` lives in the `Op` header, not the payload. `dependsOn` is **client-only** and never on the wire: a dependent op names the temp id its parent would mint, and the server answers `stale_entry` when that temp id was never mapped (GDD rule 3 holds without a wire field).

| OpKind | Payload (bytes) |
|---|---|
| `Pick` | from u32, entryId u32 (8) |
| `Move` | from u32, entryId u32, to u32, x i16, y i16, rotated u8, count u8 (0 = whole), baseVersion u32 (22) |
| `MoveGroup` (manifest) | from u32, groupKey (mailKind u16, addressId u32, flags u8), to u32, x i16, y i16, rotated u8, count u16, baseVersion u32 (26) |
| `Merge` | from u32, entryId u32, to u32, targetEntryId u32 (16) |
| `Split` | entryId u32, k u8, tempId u32 (9) |
| `QuickMove` | from u32, entryId u32, count u8 (13) / `QuickMoveGroup`: from u32, groupKey, count u16 (15). *Erratum 2026-10-20 (/architecture-review; **Accepted by Evan 2026-10-20**): the field sums are 9 B / 13 B, not 13 / 15; the shipped `QuickMove` payload is 12 B incl. 3 B zeroed pad — see GDD Impact.* |
| `TakeAll` | container u32, filterKind u8, filterValue u32 (9) |
| `Return` | — (0) |
| `Sort` | container u32, mode u8 (5) |
| `Drop` | entryId u32 (4) |
| `DeliverMatching` | mailboxId u32, selectedEntryId u32 (0 = absent), holdStartTick u32, claimed position (Player Character P13 layout: feet x, y, z i32 mm) (24). *Erratum 2026-10-16 (/architecture-review): wire form is ADR-0011's — 24 B MATCH / 28 B MISDELIVER_ONE incl. the 4 B envelope; the `OpKind` and handler belong to Delivery (block `0x0100–0x013F`), not Inventory — see GDD Impact.* |
| `Pickup` | worldItemId u32, entryId u32 (0 = default) + claimed position |
| `Open` / `Close` | container u32 (+ claimed position for `Open`) |

`deliver {entryId}` is not a client op: it is the internal fallback branch of `DeliverMatching` and the `IInventoryApi.ConsumeUnit` primitive Throwing and Mail call. `throw` is Throwing's `ThrowRelease` OpKind (erratum 2026-10-16: the Sim OpKind `0x0140`, its 28 B payload and its handler chain are fixed by ADR-0015 §1 for M-1a; ADR-0021 (M0) keeps the wire, channels, ghost and catch protocol); its handler calls `IInventoryApi.DetachForThrow` after Throwing's own checks, so inventory never owns a second throw op.

**Containers on the wire** are `containerId` u32 (system-local id, SC R22) — a player's panel containers and cursor get ids at `PlayerJoin`, so no op ever names "my inventory" symbolically.

### 2. Validation: one ordered chain, validate-then-commit

Every inventory handler implements ADR-0007's `Reply IOpHandler.Apply(in Op op, in TickContext ctx)` as two internal functions over a stack-allocated plan (`Apply` = `Validate`, then on 0 `Commit`, then build the `Reply`):

```csharp
// pure: reads state, writes only the plan; returns 0 (ok) or a reason
ushort Validate(in Op op, ref InvPlan plan, in TickContext ctx);
// mutates; never fails; raises records + seals; returns the reply extra
void   Commit(ref InvPlan plan, in TickContext ctx, ref InvReplyExtra extra);
```

`InvPlan` is a fixed-size struct (`[InlineArray]` buffers for up to 4 touched containers and the placement result) — no allocation, and **commit never recomputes what validation decided** (the fit found for a cross-container move is the one committed). The chain is fixed per op and is the GDD's, verbatim:

- **Placement ops** (`Move`, `MoveGroup`, `Return` target, swap): `bad_request → not_open → stale_entry → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full`.
- **`Split`**: `bad_request → not_open → stale_entry → cursor_busy → unsplittable → bad_count`.
- **Range-gated** (`Open`, `Pickup`, `DeliverMatching`): Player Character P13's chain first (`dead → frozen → no_target → unavailable → out_of_range / position_stale`), via `IPlayersApi`, then the op's own chain.
- `container_gone` is decided during id resolution: an id that is not live is looked up in the **tombstone table** (§6) before falling back to `stale_entry`.
- `bad_request` covers what only the Sim can judge (an id at or above an allocator's high-water and outside the actor's temp namespace; `rotated` on a slots layout; `throw` naming a non-hotbar entry). Byte-level malformation is rejected by the codec before enqueue (ADR-0011).

**Batch ops** (`TakeAll`, `DeliverMatching`, `QuickMoveGroup`): the op-level checks (`no_target`, `not_open`, range) are validated before any mutation; then each entry is validated and committed atomically in the GDD's order (oldest-first by mailId / cursor→hotbar→inventory→backpack). A skipped entry is reported, never partially applied. A batch that commits nothing replies `ok:false` with the first op-level reason — so SC R8's "a reject mutates nothing" holds, and a partial batch is a success with `requested`/`got`.

**Reason codes** (u16, keyed by `(OpKind, Reason)` and declared in each inventory descriptor, never reused): `bad_request` is Sim Core's **global code 8** (ADR-0007 §5 — returned both by the adapter codec and by inventory handlers for structurally invalid requests); inventory's own codes are `not_open 64, out_of_range 65, position_stale 66, stale_entry 67, stale_layout 68, cursor_busy 69, cursor_empty 70, blocked 71, out_of_bounds 72, not_allowed 73, not_carryable 74, swimming 75, stack_full 76, no_room 77, bad_count 78, unsplittable 79, reserved_slot 80, sort_failed 81, container_gone 82, no_target 83, wallet_floor 84, capacity_exhausted 85`. Player Character and Throwing codes are declared on their own kinds' descriptors. `busy` (1, ring full) is Sim Core's and produced by the adapter.

### 3. Versions, `baseVersion`, `lastReplaceVersion`, `statEpoch`

- Each container has `version` (u32, starts 0, never wraps in a run). **One mutating op or API call bumps each container it touched by exactly 1** — a batch op bumps each touched container once; a no-op (`sort` unchanged, a move onto its own cells) bumps nothing. Bumps are deferred to the end of the call: `InvPlan` records the touched set, and the seal for each touched container is raised once with `version_from = v`, `version_to = v + 1`.
- `lastReplaceVersion` (u32) is set to the new version by a `sort` that changed the layout. A positional op carries `baseVersion` and is rejected `stale_layout` iff `baseVersion < lastReplaceVersion` of its destination. Entry-naming ops carry no `baseVersion` (a consolidating sort retires the entry's id, so they fail `stale_entry`).
- `statEpoch` (u32) bumps when a `MailMaxStack` or `InventoryRows` change affects the container; `lastSorted[mode] = (version, statEpoch)` memoises sort idempotence, so a repeat sort is O(1) with 0 probes.
- A rejected op never changes a version; its reply's `resultVersion` is the current version.

### 4. Events: per-container records sealed by a version record, then the reply

Inventory raises two families of event kinds (ADR-0008 entries, fixed size):

- **Records** (each ≤ 48 B payload, ADR-0008) — `InvInserted{c, entryId, x, y, rotated, count, key}` + mailId runs, `InvRemoved{c, entryId, count}` (no mailIds: the viewer already holds the entry and take-k removes the k lowest, R6), `InvMerged{c, targetEntryId, addedCount}` + mailId runs, `InvMoved{c, entryId, x, y, rotated}`, `InvSplitFrom{c, entryId, newEntryId, count}`, `InvReplace{c, version_from, version_to}` (layout by reference), `InvCursor…` (owner-only), `WorldItem{Spawned, Despawned, EntriesChanged}` (with `worldSeq`), `ItemsLost{itemDef, count}`, `BacklogUnits`, `GroupCounts{c, …}` (chunked).
- **Seal** — `InvVersion{c, version_from, version_to, actorId, cause}`, raised **once per touched container, after that container's records** for the call.

Rules:
1. **Audience.** Every record and seal of container `c` is `Targeted` with the audience resolved **at raise time** from `c`'s subscriber set (entry-level subscribers; GDD subscription table). Panel and cursor containers are `OwnerOnly`. Intake/Depot non-openers receive only `GroupCounts` (ADR-0011 packs; this ADR raises them, one per changed container per call).
2. **Cross-container ops** raise each container's records and seal separately, each with its own version — a viewer of only the source sees `InvRemoved` + seal; a viewer of only the target sees `InvInserted`/`InvMerged` + seal. The version *pair* is never assumed by a client.
3. **Client application unit = the seal.** A client stages a container's records and applies them atomically when that container's seal arrives, after its gap check (`version_from == mirror.version`, else resync). This is what makes a batch op's single bump and multi-record change one atomic visible step.
4. **`reqId` tagging (ADR-0008 §7 (d)).** Records and seals carry no `reqId`. Inside `Deliver` the adapter tags the same-`CallSeq` entries it sends to the actor's peer with the `OpReply`'s `reqId`, so the requester retires the pending op from its replay when the seal applies; other peers see `actorId` only.
5. **Reply last.** Sim Core appends the `OpReply` after every record and seal the handler raised (SC R10), so "event before reply" holds by construction. The adapter must deliver one peer's records, seals and replies **in log order on one reliable-ordered channel** (contract on ADR-0011; spike-verified).
6. **Large payloads (ADR-0008 §1.3).** *mailIds* travel by **chunking** (option b): the record is followed by `Continuation` entries of the same kind and `CallSeq` holding the added mailIds as ascending **runs** `(start u32, len u16)` — 8 runs per 48 B entry; spawned mail is mostly consecutive, so a 20-stack is typically one run. They are sent only where the viewer cannot derive them (`Inserted`, `Merged`). *Sort `replace`* goes **by reference** (option a): the `InvReplace` entry names the container and its version pair; the adapter pulls a keyed container snapshot through `ISnapshotSource` between ticks, sends it in-stream at the `InvReplace` position tagged with the container's end-of-tick version, and the client applies it like any version-tagged snapshot (discarding later records/seals with `version_to ≤` the snapshot version — GDD Network buffering rule). One `InvReplace` per sort call (SC R13); merging a container's replaces of a tick is the adapter's call under ADR-0008 §7 (i)–(iii). `GroupCounts` lists are chunked.
7. **Coalescing.** Only within one call (SC R13): e.g. automation `TakeUnit`s in one call may rewrite one `InvRemoved` in place; across calls a new record is raised.

**Reply extra (24 B, inventory layout):** `resultVersionA u32, resultVersionB u32, byActorId u32, requestedUnits u16, gotUnits u16, requestedEntries u16, gotEntries u16, skippedEntries u16, causeAndFlags u8 (cause 3 bits ∈ {taken, sorted, consumed, expired, destroyed, system}; flags: unchanged, appliedModeSize), skipReason u8`. A and B are the op's primary source and destination containers; a batch op that touched more containers reports the rest through their seals (already applied before the reply arrives). Unit counts saturate at 65,535 (display only; conservation never reads them).

### 5. Storage layout (Sim side)

All storage is preallocated from `SimSettings` capacities at world build and never grows after warm-up (0-alloc tick). Nothing is iterated through a `Dictionary`; id lookups use preallocated open-addressed integer maps that are never enumerated; every ordered walk is over arrays in ascending id or row-major order.

| Structure | Decision |
|---|---|
| **Containers** | `ContainerHeader` struct array (slot-indexed; `containerId → slot` open-addressed map): `defIndex`, `w`, `h`, `layout`, `version`, `lastReplaceVersion`, `statEpoch`, `lastSorted[2]`, `freeCells`, `entryCount`, outfeed/heap handles. Dims replicate (perks grow rows, R15). |
| **Occupancy** | One `ulong` per row per container (v1 max width 20 ≤ 64), row-major bit = column: a footprint test is `h` mask-and-compares. Plus a `cellOwner` u16 array (cell → entry slot, `0xFFFF` free) for O(1) "what is under this cell" (merge-target and `blocked` decisions). |
| **Entries** | Global `EntryRecord` struct pool with a free-slot stack: `entryId u32, container u16 slot, x u8, y u8, w u8, h u8, rotated, count u8, isMail, key (mailKind u16, addressId u32, flags u8 \| itemId), arrivalSeq u32, mailIds handle`. `entryId → slot` map. ≤ 40 B per record. Ids are never reused (ADR-0004); slots are. |
| **`mailIds` arena** | Size classes of 1, 2, 4, 8, 16, 32, 64, 128, 256 `u32`s, one preallocated slab + free-index stack per class. A handle is `(class, block, start, count)`: **take-k-oldest advances `start`** (O(1), R6); a merge is a two-pointer sorted union into a block of the target size class; a block whose live count falls to ≤ ¼ of its class is re-homed on its next mutation (hysteresis, no thrash). **Exhaustion policy:** a class that is empty borrows from the next larger class; if every larger class is empty, validation fails the op with `capacity_exhausted` (nothing mutated) and an API caller gets `false` plus an always-on record. Capacities are sized so the H-R19-1 fuzz fixture peaks below 50 % of each class (asserted by a test). |
| **Outfeed head index (Intake)** | An **index-linked FIFO** of `(arrivalSeq, entrySlot, entryId)` nodes in a shared node pool, appended on every Intake insert (arrivalSeq is a per-Intake counter, so append order is sorted order). Removal (taken, consolidated, sorted away) leaves the node as a **lazy tombstone**: `PeekOutfeed` pops heads whose `(entrySlot, entryId, arrivalSeq)` no longer match a live entry. A consolidating Intake `sort` gives the new entry the max `arrivalSeq` of its parts (R14), which can land mid-queue, so **the Intake sort rebuilds the FIFO** from its live entries (stable by `arrivalSeq`) inside the sort's own bounded cost. Presort: one FIFO per street in the same node pool, rebuilt likewise; Belt owns the selector, inventory exposes `TakeUnit(container, selector)`. Compaction when tombstones exceed live nodes. |
| **FIFO selector on other containers** | Lowest mailId (R14): a per-container binary min-heap of `(lowestMailId, entryId)` with lazy invalidation, built the first time a FIFO selector targets the container (endpoint-attached containers only), capacity 2 × cells with rebuild on fill. Probe accounting (H-PERF-6a) counts candidate entries inspected (heap top + stale pops), not sift comparisons. |
| **WorldItem expiry** | **One FIFO per duration class**, appended at creation (each class has one constant duration, so append order is `expiresTick` order and ties fall to ascending `worldItemId`): `mailWorldItem` (9000), `nonMailWorldItem` and `spillHeap` (9000), `waterFloat` (300), `bag` (900). Loot mail is kept off every FIFO until Payday (R12). Phase 1 pops heads while `expiresTick ≤ now`, skipping lazy tombstones (picked-up items). **Cap eviction** takes the `mailWorldItem` head if it is older than `capEvictMinAgeSeconds` (the head is the oldest, so if it is too young every mail item is), else the lower `(expiresTick, id)` of the two non-mail heads; the newly created item is excluded by construction. This replaces the GDD's "timing wheel for bags and water returns": with constant durations a FIFO is exact and O(1). *(Erratum 2026-10-21, text only — no Decision change: `waterFloat` is two FIFOs, floating mail and floating non-mail (300 each), so there are five FIFOs — `mailWorldItem`, `nonMailWorldItem` (which `spillHeap` shares at M-1b), `waterFloat` mail, `waterFloat` non-mail, `bag`; the non-mail cap candidate is the lower `(expiresTick, id)` of the `nonMailWorldItem` and floating non-mail heads, and floating mail is never a cap candidate; "older than" is strictly greater. H-PERF-6d visit: one FIFO node read on the cap-eviction path; a head already checked and the reads of a queue clean-out (compaction) are not visits; the gate stays ≤ items created + 1. See Errata below.)* |
| **Tombstone table (R7)** | 256-entry ring of `(id, byActorId, cause, worldSeq, tick)` with an open-addressed id map; entries older than 30 s are dead. Feeds `container_gone` with who. |
| **StackKey → non-full stacks** | Per container, an open-addressed map `StackKey (packed u64) → head` of an intrusive list of non-full entries of that key, **kept in row-major order** (PASS M walks stacks in row-major order). Maintained in `InventoryMutator` on insert, remove, count change and move. Lists are short (1–3 typical); insertion is O(list length). |
| **`itemId → units`** | Per container, an open-addressed `itemId → units` map maintained on every item mutation (R17 `CountItem` never scans). |
| **Negative-fit cache** | Per container, `(footprintShape, rotated) → versionNoFit`: PASS U/R skip a shape that failed at the current version. |
| **Temp-id map** | Per `PlayerSlot`, a 32-entry ring of `(tempId, realEntryId, opSeq)`; a `Split` that succeeds maps its payload `tempId`; an op naming an unmapped temp id gets `stale_entry`. Reset when the slot's `PlayerJoin` (rejoin) applies. The map is Sim state, so replays resolve temp ids identically (it is in the canonical dump). |

### 6. Conservation (R19)

- A `UnitLedger` holds O(1) counters per location state (`Placed, Cursor, World, Flight, Belt, Backlog, Consumed, Destroyed`) for mail units and per-itemId item units, plus `spawned`, `granted`.
- **Only the lowest mutation primitives** (`InventoryMutator.Place/Remove/Transfer`, `ConsumeUnit`, `DetachForThrow`, `Spawn`, `Destroy`) touch the ledger; boundary crossings into Flight, Belt and Backlog happen only through `IInventoryApi`, so another system can never move a unit without the ledger seeing it.
- The tick-end check (`ITickEndCheck`) asserts `Σ live = spawned + granted − consumed − destroyed` in every build and writes an always-on record on mismatch (never throws). The full multiset scan runs behind `[Conditional("INVENTORY_ASSERT")]`, compiled out of Release.

### Architecture Diagram

```
 adapter (ADR-0011)                    Sim (this ADR)                                 adapter
 ──────────────────     ─────────────────────────────────────────────────────────    ────────────
 decode (codec) ──► EnqueueOp ─► phase 0 drain ─► IOpHandler.Validate ─► Commit ─┐
   bad bytes → bad_request                         (fixed chain)        (InventoryMutator)
                                                                                  ├─► records(c1) seal(c1)
 automation (phase 5) ─► IInventoryApi ───────────────────────────────► Commit ─┤   records(c2) seal(c2)
 Items.Expire (1) / Intake.Drain (2) ───────────────────────────────────► Commit ─┘   OpReply (after events)
                                                                                      │
                     UnitLedger ◄── every primitive          tick end: Σ check        ▼
                     storage: EntryPool · bitmaps · mailIds arena · FIFOs · indices   Events.Flush ─► IEventSink
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.Inventory;

public readonly record struct ContainerId(uint Value);
public enum InvReason : ushort { NotOpen = 64 /* bad_request = CoreReason.BadRequest (8) */, OutOfRange, PositionStale, StaleEntry, StaleLayout,
    CursorBusy, CursorEmpty, Blocked, OutOfBounds, NotAllowed, NotCarryable, Swimming, StackFull, NoRoom,
    BadCount, Unsplittable, ReservedSlot, SortFailed, ContainerGone, NoTarget, WalletFloor, CapacityExhausted }
public enum MergePolicy : byte { Merge, NoMerge }
public enum Selector : byte { Fifo, Street /* + street id */ }

public interface IInventoryApi            // synchronous; in-tick callers only; no reply, same versions/events as ops
{
    SpawnResult Spawn(ContainerId c, ReadOnlySpan<MailId> units, MergePolicy p);
    bool TryInsert(ContainerId c, in UnitRef u, ActorId actor);
    bool PeekOutfeed(ContainerId c, Selector s, out UnitRef u);
    bool TakeUnit(ContainerId c, Selector s, ActorId actor, out UnitRef u);
    CatchResult TryCatch(PlayerSlot p, in FlightPayload f);          // {unitsIn, unitsFumbled}
    DetachResult DetachForThrow(PlayerSlot p, EntryId e, byte units); // whole entry → Flight
    bool ConsumeUnit(EntryRef e, ConsumeReason r, ActorId actor);    // oldest unit; Mail/Throwing call per unit
    GrantResult Grant(PlayerSlot p, in EntrySpec e, OverflowPolicy o);
    int CountItem(ReadOnlySpan<ContainerId> cs, ItemDefIndex id);
    bool ConsumeItems(ReadOnlySpan<ContainerId> cs, ItemDefIndex id, int n); // all-or-nothing
    void SpillToBag(PlayerSlot p, Vec3Mm pos);
}

[StructLayout(LayoutKind.Explicit, Size = 24)] // FieldOffsets 0,4,8,12,14,16,18,20,22,23; sizeof test
public struct InvReplyExtra // exactly 24 B, written into ADR-0007's Reply.Extra [InlineArray(24)]; explicit offsets per Risks
{ public uint ResultVersionA, ResultVersionB, ByActorId;
  public ushort RequestedUnits, GotUnits, RequestedEntries, GotEntries, SkippedEntries;
  public byte CauseAndFlags, SkipReason; }
```

Client side (`PerformativeMail.Client.Core/Inventory/`, built at M0, Godot-free): `ContainerMirror` (confirmed state + version + staged records), `PendingQueue` (`pendingQueueMax = 16`, `(reqId, op, dependsOn?)`), `TempIdAllocator` (`0x8000_0000 | slot << 20 | ctr`), and the rules 1–8 of the GDD Network section (retire only on reply; own-sort hold; foreign `replace` dooms positional ops; 5 s reply timeout → resync with `ackedReqId`).

### Implementation Guidelines
- Every inventory handler **must** be split into a pure `Validate` and a never-failing `Commit`; `Validate` must not allocate ids, draw RNG, raise events or touch the ledger.
- The reason precedence chains in §2 **must** be implemented as one ordered sequence per op kind and covered by a parametrised test that sends a request violating every later rule too and asserts the earliest code.
- Ops and `IInventoryApi` calls **must** go through the same `InventoryMutator` primitives; no system may write container, entry or ledger state directly.
- Every touched container **must** receive exactly one `InvVersion` seal per call, after its records; the bump **must** be +1 per call, never per record.
- Every inventory op and event payload struct **must** be padding-free (ADR-0007 §1, ADR-0008 §1): the descriptor test asserts the sum of field sizes equals `Unsafe.SizeOf<T>()`. Where natural alignment would insert padding (e.g. `Move`'s 22 B), reorder fields or declare explicit zeroed pad fields and count them in the descriptor's `PayloadLength`; payloads are still read and written unaligned only.
- Events **must** respect ADR-0008's 48 B payload ceiling: `mailIds` only as chunked ascending runs on `Inserted`/`Merged`, sort layouts only by reference through `ISnapshotSource`.
- Storage **must** be preallocated from `SimSettings`; no `List<T>.Add` growth, LINQ, boxing or `Dictionary` enumeration in any tick path.
- Probe counters (H-PERF-6) **must** be compiled into Release and incremented once per cell probe or candidate visit. *(Wording clarified 2026-10-21, no decision change — owner ruling 11:43 AM PT, decision log: a placement scan counts **per cell** — each footprint cell it examines, never one per anchor; "candidate visit" is an entry, list node or FIFO node inspected, as Risks defines for H-PERF-6a.)* *(Erratum 2026-10-21, owner ruling 7:22 PM PT, text only: for the WorldItem expiry FIFOs (H-PERF-6d) a visit is one FIFO node read; a head already checked (the head memo) and queue clean-out reads are not visits; the gate stays ≤ created + 1.)*
- The client **must never** re-validate a pending op to decide it failed; only a reply retires it.
- `dependsOn` **must never** be sent on the wire.

## Alternatives Considered

### Alternative 1: Event per mutation with an embedded version pair (no seal)
- **Description**: Each record carries `version_from..version_to`; a batch op that touches a container k times raises k records sharing one pair.
- **Pros**: One fewer entry per container per call.
- **Cons**: The client cannot tell when a batch's records for a container are complete, so it either applies a half-batch (visible tearing, wrong gap checks) or needs a record count that the raising code does not know until the call ends.
- **Rejection Reason**: The seal gives an exact atomic application unit at a cost of one 64 B entry per touched container per call, well inside the event log budget.

### Alternative 2: Automation through the op FIFO (spec 07's original)
- **Description**: Inserters, belts and NPC drivers enqueue ops like players.
- **Pros**: One entry path for everything.
- **Cons**: Adds a tick of latency to every inserter, breaks Mail R6's phase order and makes "hands beat machines" depend on enqueue timing.
- **Rejection Reason**: Superseded by SC R9 / DEV-6; this ADR keeps a shared mutation layer instead of a shared queue.

### Alternative 3: Binary heap for the Intake outfeed
- **Description**: A min-heap keyed by `arrivalSeq`.
- **Pros**: Handles mid-queue insertion after a consolidating sort without a rebuild.
- **Cons**: O(log n) per take with more candidate visits than H-PERF-6a's ≤ 4 at n = 6,400; heap state is harder to dump canonically.
- **Rejection Reason**: Appends are already in order, so a FIFO with lazy tombstones is O(1) amortised; the one out-of-order case (sort) rebuilds inside the sort's existing bounded cost.

### Alternative 4: Timing wheel for all WorldItem/bag/water expiries
- **Description**: A hashed timing wheel keyed by `expiresTick`.
- **Pros**: Handles arbitrary durations.
- **Cons**: More code and memory; every v1 timer class has one constant duration, so a FIFO is already exact; cap eviction needs "lowest `expiresTick` per class", which a wheel does not give in O(1).
- **Rejection Reason**: Per-class FIFOs are exact and O(1); revisit in ADR-0025 only if a variable-duration timer class appears.

### Alternative 5: Per-entry `List<uint>` for `mailIds`
- **Description**: Each entry owns a managed list.
- **Pros**: Simplest code.
- **Cons**: Allocates on growth and on split; 0-alloc tick fails; GC pressure scales with mail flow.
- **Rejection Reason**: Violates SC R32 / H-PERF-1's 0-byte automation path.

### Alternative 6: Dedupe duplicate `reqId`s inside the inventory handlers (SC R10 reading)
- **Description**: The handler keeps the last 16 replies per player and answers duplicates.
- **Pros**: Idempotence travels with the op semantics.
- **Cons**: Puts a per-connection concept into the Sim, and a resent op would still consume an `opSeq` and be recorded.
- **Rejection Reason**: Grid rule 8 already places the cache in the network adapter keyed `(peer, reqId)`; architecture Q-ARCH-04 recommends the same; decided in ADR-0011 (and ADR-0007). The Sim never dedupes.

## Consequences

### Positive
- Double-apply and phantom entries are prevented structurally: the mirror changes only on seals, replies only retire pending ops, and replies always follow their events.
- Cross-container atomicity is a property of `InvPlan` (fit computed once, committed once), not of handler discipline.
- Conservation is checked every tick in every build at O(1), with the full scan in assert builds and the fuzz test.
- Every hot query (outfeed peek, AutoPlace merge pass, `CountItem`, cap eviction) is O(1) amortised, so the BLOCKING probe gates are met by construction.
- Ops and automation share one mutation layer, so a bug fixed for one is fixed for both.

### Negative
- One extra event (the seal) per touched container per call.
- The Intake FIFO rebuild makes an Intake sort slightly more expensive (bounded inside the sort's 8,500-probe budget).
- Many preallocated capacities (`SimSettings`) must be sized and kept honest by tests.
- The client mirror must stage records until seals; a client that forgets the seal rule renders torn batches.

## Risks
- **The transport cannot guarantee in-order delivery of records and replies on one channel** (Godot 4.7.1 NOT SOURCEABLE). Mitigation: ADR-0011 spike before M0; if it fails, the version-gap resync path becomes normal (GDD Network) and this ADR is revisited.
- **Event-log pressure from chunked mailIds** (a `take_all` moving many non-consecutive stacks raises several continuation entries per moved entry). Mitigation: run encoding, `Removed` without mailIds, `replace` by reference; SC F8 / ADR-0008 log sizing must include inventory's worst-fixture chunk count; overflow is recoverable (scoped resync, ADR-0008 §6).
- **Arena or pool capacity exhaustion in a long session.** Mitigation: `capacity_exhausted` rejects without mutation; headroom test at 50 % of the fuzz peak; counters in the developer overlay.
- **Probe accounting disputes** (what counts as a "visit" in H-PERF-6a). Mitigation: defined here (candidate entries inspected, not sift comparisons); the test asserts this definition.
- **Reply `extra` grows past 24 B** if a later op needs more fields. Mitigation: batch details already go through seals; any further field goes into the event stream, not the reply.
- *Engine-specialist notes (godot-specialist, 2026-10-16, APPROVE WITH NOTES, no blocking issue):*
  - `[Conditional("INVENTORY_ASSERT")]` is evaluated in the **calling** assembly's compilation; the symbol must be set for the Sim (and test) projects through `Directory.Build.props`, never assumed from the Godot-generated csproj's `DEBUG`/`TOOLS` (OQ4 / ADR-0001).
  - `[InlineArray]` needs C# 12 / .NET 8: pin `LangVersion` and `TargetFramework` in `Directory.Build.props`; the Godot 4.7.1 SDK defaults are UNVERIFIED; add a build-smoke test of the Godot project referencing the Sim.
  - `InvReplyExtra` must use `LayoutKind.Explicit` with fixed `FieldOffset`s plus a `sizeof == 24` test rather than `Pack = 1`, and its fields are never passed by `ref` (avoids unaligned refs if an ARM export is ever targeted). The Key Interfaces sketch is superseded on this point.
  - Whether a 4.7.1 export copies the plain sibling Sim assembly through the `ProjectReference` is UNVERIFIED (OQ4 / S2).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| grid-inventory.md | R7 / TR-inventory-001: all ops in one FIFO drained in `Inputs.Apply`; loopback same path | One `OpKind` per op through `EnqueueOp`; host uses the loopback adapter (ADR-0011) |
| grid-inventory.md | R7, D7 / TR-inventory-002: per-container u32 version +1 per mutating op; batch bumps once per container | §3: deferred bump, one seal per touched container per call |
| grid-inventory.md | R3 / TR-inventory-004: StackKey, `effMax` at op time, ≤ 255 | `EntryRecord.key`; `effMax` read from `IStatSheet` in `Validate` |
| grid-inventory.md | R6 / TR-inventory-005: take-k removes lowest mailIds | arena handle `start` advance; sorted union on merge |
| grid-inventory.md | R4, R7 / TR-inventory-006: fixed reason precedence | §2 chains + parametrised precedence test |
| grid-inventory.md | R8 / TR-inventory-007: AutoPlace M/U/R with O(1) non-full-stack lookup | §5 StackKey index (row-major lists) + negative-fit cache |
| grid-inventory.md | R14 / TR-inventory-010: outfeed by `arrivalSeq`, ≤ 4 visits amortised | §5 index-linked FIFO + lazy tombstones; sort rebuild |
| grid-inventory.md | R14 / TR-inventory-011: drain ≤ 32/tick, 0 probes when idle | phase-2 binding; drain gating per R14 using container `version` and negative-fit cache |
| grid-inventory.md | R19 / TR-inventory-012: O(1) counters each tick; `INVENTORY_ASSERT` | §6 `UnitLedger` + `ITickEndCheck` |
| grid-inventory.md | R12 / TR-inventory-013: cap 512, expiry FIFOs, lossless mail eviction | §5 per-duration-class FIFOs + eviction rule |
| grid-inventory.md | D6 / TR-inventory-014: absolute-tick timers; expiry after `Inputs.Apply` | phase 1 binding; `expiresTick` absolute |
| grid-inventory.md | Network / TR-inventory-019: events, replies, snapshots in-stream, event before reply | §4 records → seal → reply; ordering contract on ADR-0011 |
| grid-inventory.md | Network / TR-inventory-022: optimistic client (pending 16, temp ids, `dependsOn`, `baseVersion`, 5 s) | Client.Core classes specified; temp-id map in the Sim; `dependsOn` client-only |
| grid-inventory.md | R17: `CountItem` via index, never a scan | §5 `itemId → units` map |
| grid-inventory.md | "→ becomes an ADR": mailIds arena + exhaustion policy, struct arrays, occupancy bitmaps, negative-fit cache | §5 |
| simulation-core.md | R8 atomicity, R9 in-tick callers, R10 reply after events, R13 per-call coalescing | §2, §4, shared `InventoryMutator` |

## Performance Implications
- **CPU**: Peek/take O(1) amortised; AutoPlace merge O(1) + footprint tests O(h) per probe; validation is a fixed chain of O(1) checks except placement fits. Inventory share target 1.0 ms median / 2.0 ms p99 (advisory) is expected to have large headroom; the blocking gates are probe counts.
  - Probe unit: per-cell per Evan 2026-10-21 — one AutoPlace footprint test at an anchor counts `fw × fh` cell probes (H-PERF-6; `CounterId.InventoryPlacementProbes`), not one per anchor; the 8,500 gate is unchanged.
- **Memory**: `EntryRecord` ≤ 40 B × capacity (e.g. 8,192 → 320 KB), arena ≈ 2 × peak units × 4 B, bitmaps and indices < 64 KB for all v1 containers: inside H-PERF-5's 1 MB with 512 WorldItems; capacities in `SimSettings`.
- **Load Time**: Preallocation at world build, < 1 ms.
- **Network**: One 64 B-entry seal per touched container per call inside the Sim; on the wire the adapter packs records + seal into `ContainerDelta` (GDD budget ≤ 40 B/tick/peer sustained — ADR-0011 / Networking).

## Migration Plan
No code exists (Technical Setup). M-1a builds the Sim pipeline, storage and ledger; the optimistic client classes in Client.Core are built at M0 with the transport.

## Validation Criteria
- Grid Inventory ACs H-R7-1…11, H-R8-*, H-R12-*, H-R14-*, H-R17-1, H-R19-1 (fuzz, invariants a–i), H-DET-1 pass on the plain-C# Sim.
- H-PERF-6 a–d probe counts and H-PERF-1's 0-byte automation path pass in CI.
- A precedence test per op kind (§2) and a seal test: for every op kind, each touched container receives exactly one seal after its records and before the reply.
- H-NET-1 / H-OPT-1…3 pass against Client.Core with a loopback adapter at M0.

## GDD Impact (not edited here — owners apply at re-review)
- **⚠️ GDD SYNC (names):** grid-inventory.md R7 table → this ADR: `move {fromGroup}` / `quick_move {fromGroup}` → separate OpKinds `MoveGroup` / `QuickMoveGroup`; `deliver {entryId}` is not a client OpKind — it is the `DeliverMatching` fallback branch and `IInventoryApi.ConsumeUnit`; "calls `Consume` per unit here" (Throwing/Mail) → `ConsumeUnit`; the `throw` detach → `IInventoryApi.DetachForThrow` called from Throwing's `ThrowRelease` handler; new seal event `InvVersion` (the GDD's per-event `version_from..version_to` lives on the seal); `dependsOn` is client-only (never on the wire). Semantics are unchanged.
- **grid-inventory.md R7** "Every reply carries `resultVersion` per touched container": narrowed — the reply carries the primary source/destination versions; other touched containers' versions arrive in their seals (which precede the reply).
- **grid-inventory.md R12** "a timing wheel for bags and water returns": replaced by per-duration-class FIFOs (§5).
- **grid-inventory.md R14** `PeekOutfeed` "FIFO ring vs heap → ADR": decided — index-linked FIFO with lazy tombstones, rebuilt on Intake sort.
- **simulation-core.md R10** "Duplicates … A handler that promises idempotence implements it itself … Grid Inventory rule 8 does": contradicts Grid rule 8 (adapter-keyed cache) and Q-ARCH-04; this ADR follows Grid rule 8 — the Sim never dedupes (recorded in ADR-0011).
- **architecture.md**: ADR-0025's scope narrows (TR-inventory-007 moves to ADR-0009 as primary; ADR-0025 keeps the D3 sort algorithm, hints and memo, backlog per-footprint sub-queues, and perf-budget verification, TR-inventory-008/009/016/018).
- **Erratum 2026-10-16 (/architecture-review, technical-director) — `DeliverMatching` wire layout.** Range-gated ops carry the claimed position (grid-inventory.md R7 "Range-gated ops (`open`, `pickup`, `deliver_matching`) carry the position the client claimed at press"; ADR-0014 §5). The Sim payload stays fixed at 24 B (`mailboxId u32 · selectedEntryId u32 (0 = absent) · holdStartTick u32 · ClaimedPosition x,y,z i32`). The wire form (ADR-0011 §3 codec) is envelope `kind u16 · reqId u16` + `mailboxId u32 · holdStartTick u32 · ClaimedPosition 12 B` [+ `selectedEntryId u32` on MISDELIVER_ONE only] = **24 B MATCH / 28 B MISDELIVER_ONE**, transport framing excluded; presence is implied by length, and any other length is `bad_request`. **manual-delivery.md Network table and M-NET-1 ("12 B MATCH / 16 B MISDELIVER_ONE", "op code u8 · version + presence flags u8" header) are superseded** — owner applies at MD re-review; TR-delivery-008 is revised in `docs/architecture/tr-registry.yaml`.
- **Erratum 2026-10-16 (/architecture-review, technical-director) — `DeliverMatching` ownership.** §1/§2 listed `DeliverMatching` among Inventory's OpKinds (block `0x0080–0x00FF`), with an Inventory-owned handler that would call Mail's `Evaluate`/consume. That contradicts ADR-0017 §5/Key Interfaces ("Inventory **must never** call Mail"; `ConsumeHand` is called "from Delivery's `deliver_matching` handler") and ADR-0003's call direction, and it would make this ADR depend on ADR-0017, a cycle. **Resolution: ADR-0017 governs.** `DeliverMatching` is a Delivery `OpKind` in the Mail/Delivery block `0x0100–0x013F` (ADR-0007 §4), with its handler in `Sim/Delivery`. The handler runs `IPlayersApi.CheckInteraction` → anchor check → `IMailApi.ConsumeHand`. Inventory supplies only primitives: the read-only match scan in cursor → hotbar → inventory → backpack row-major order, `ConsumeUnit`, version bumps and seals. The D-B branch rule and the 24 B payload are unchanged. Delivery declares its own reasons (`no_target`, `wallet_floor`, …). grid-inventory story 022 must be re-scoped to the Inventory primitives.
- **Erratum 2026-10-20 (/architecture-review on `claude-fable-5-1`, drafted by the planning lead) — `QuickMove` / `QuickMoveGroup` payload sizes. Status: Accepted by Evan 2026-10-20 (owner acceptance in chat, 21:46 PT). Must land before grid-inventory story 010.** §1's byte counts do not match the fields it lists. `QuickMove` = `from u32 · entryId u32 · count u8 (0 = whole)` = **9 B**, not 13; `QuickMoveGroup` = `from u32 · groupKey (mailKind u16 · addressId u32 · flags u8) · count u16` = **13 B**, not 15. No field is missing: grid-inventory.md R7 `quick_move {from, entryId | fromGroup, count?}` has no destination (R9 picks the targets), and §3 says entry-naming ops carry no `baseVersion`; the cause of the 13 / 15 figures is unknown. Every other §1 row sums correctly. **Shipped layout (PR #4, grid-inventory story 004):** payloads are `Pack = 1` structs with explicit zeroed pad fields counted in `PayloadLength`, padded to a multiple of 4 B (a non-zero pad byte is `bad_request`); `QuickMove` (`0x0085`) is **12 B** = 9 B fields + 3 B pad (Move is 24 = 22 + 2). `QuickMoveGroup` (`0x0086`) is only reserved (M1); under the same rule it would be 16 B (13 + 3). §1's sizes are field sums before padding. Not changed here: the GDD vs ADR disagreement on `count = 0` and on a QuickMove count above the entry total (PR #4's TD-105 item 3, a design ruling before grid-inventory stories 007, 008 and 010 — 010 is the QuickMove story).
- **As-implemented note 2026-10-21 (grid-inventory story 012, planning lead; text only — no Decision change, ADR stays Accepted; TD-012/TD-116 pattern).** The Key Interfaces sketch `SpawnResult Spawn(ContainerId c, ReadOnlySpan<MailId> units, MergePolicy p)` cannot be built under ADR-0017 §5 (Inventory never reads Mail's table, so ids alone cannot form an entry's StackKey). As implemented: `SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p)` — one StackKey per call, ids ascending, 1..255, one entry per id under NoMerge, one seal per call; `SpawnResult { ushort Placed; ushort Unplaced; }` with the remainder `ids[Placed..]`. `UnitRef` is `readonly record struct UnitRef(MailId Mail, EntryId Entry, DefIndex MailKind, uint AddressId, byte Flags)` so a belt can re-insert a unit without asking Mail; `Selector` is `{ Fifo }` at M-1a (`Street` M-1b). Return-to-Intake is the internal `InventorySystem.ReturnToIntake(detachedEntrySlot, actor, cause)` (callers 013/021), not on `IInventoryApi` until M1. `TakeUnit` uses a new count-change primitive `InventoryMutator.TakeOldest`, shared later by `ConsumeUnit`. A GDD/ADR text re-review may fold this into Key Interfaces.
- **Erratum 2026-10-21 (planning lead; text only — no Decision change, ADR stays Accepted) — probe-counter unit.** Implementation Guidelines "incremented once per cell probe or candidate visit" is clarified inline per the owner ruling of 11:43 AM PT (decision log 2026-10-21): a placement scan counts **per cell** — each footprint cell it examines, never one per anchor; a candidate visit is an entry, list node or FIFO node inspected (Risks, H-PERF-6a). Rule unchanged.
- **Erratum 2026-10-21 (grid-inventory story 013, PR #26; owner-approved by Evan 8:11 PM PT; text only — no Decision change, ADR stays Accepted) — WorldItem expiry FIFOs, `ItemsLost`, world records and the H-PERF-6d visit.** (1) §5 "one FIFO per duration class": `waterFloat` is implemented as **two** FIFOs, floating mail and floating non-mail (300 ticks each), so there are **five** FIFOs including `bag` (900): `mailWorldItem` (9000), `nonMailWorldItem` (9000; `spillHeap` shares it at M-1b), `waterFloat` mail (300), `waterFloat` non-mail (300), `bag` (900). Each still holds one constant duration, so append order stays `expiresTick` order. (2) Cap eviction: the non-mail candidate is the lower `(expiresTick, id)` of the `nonMailWorldItem` and floating non-mail heads, so a floating plank can be evicted (GDD R12 "the non-mail WorldItem with the lowest `expiresTick`"); floating mail is never a cap candidate (it returns to the Intake at its 300-tick float); "older than `capEvictMinAgeSeconds`" is strictly greater (age > 300 ticks; owner ruling 8:01 PM PT). (3) §4 records list gains `ItemsLost{itemDef, count}` (GDD R12). (4) Event kinds and sizes (as built, ≤ 48 B, padding-free): `0x0086 WorldItemSpawned` 44 B, `0x0087 WorldItemDespawned` 16 B, `0x0088 WorldItemEntriesChanged` 16 B, `0x0089 ItemsLost` 8 B (`worldItemId u32 · itemId u16 · count u8 · cause u8`). Unlike container records (§4 rule 1, `Targeted`), these four world records are **`Replicated`** (ADR-0008) to connected slots; wire codecs are the network epic's (TD-140). (5) H-PERF-6d visit (owner ruling 7:22 PM PT, confirmed 7:43 PM PT; decision log 2026-10-21): a candidate visit is **one FIFO node read** on the cap-eviction path; a head already checked (the per-FIFO head memo, cleared on every head change) and the reads of a queue clean-out (full-ring compaction on append) are **not** visits; the gate stays **≤ items created + 1**. This narrows the Implementation Guidelines' "FIFO node inspected" for the WorldItem FIFOs only.

## Related
- ADR-0003, 0004, 0006, 0007, 0008 (Foundation dependencies); ADR-0010 (defs, StatSheet); ADR-0011 (adapter seam, `reqId` dedupe, ordering); ADR-0017, 0021, 0022, 0025.
- `design/gdd/grid-inventory.md` (R1–R19, Network, H-* ACs, OQ12, OQ20), `design/gdd/simulation-core.md` (R8–R14, R19–R22, R31–R32), `docs/architecture/architecture.md` §API Boundaries.
