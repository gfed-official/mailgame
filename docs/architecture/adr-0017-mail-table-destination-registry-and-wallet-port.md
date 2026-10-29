# ADR-0017: Mail Table, Destination Registry and the `IWalletGate` Port

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
Autonomous planning run (architecture-decision skill, `--review lean`: TD-ADR skipped, engine specialist consulted). Acceptance pending the project owner's batch confirmation.

**Accepted 2026-10-16** by `technical-director` on the project owner's explicit confirmation (owner answer "Accept all 8 (Recommended)" to the main session's batch request for ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026; acceptance mode `.claude/skills/architecture-decision/references/0-accept.md`, dependency order 0013 → 0014 → 0012 → 0026 → 0017 → 0015 → 0016 → 0019). Amended before acceptance: `AddressId` moved to `Sim/Primitives` (§1).

## Summary
Mail & Destinations needs server-only storage for every live mail unit, a registry of destinations built from the world, one consume routine for every delivery path, and a way to pay and fine the team without depending on the undesigned Economy system. This ADR fixes:
- a **paged, mailId-indexed struct-of-arrays mail table**;
- a **packed 32-bit `AddressId`** whose numeric order is the manifest's address order;
- a **lot-ordered destination registry** with lazily-refilled complaint buckets and feeder credits;
- **one consume routine** whose path order falls out of phase order;
- a backlog stored in Inventory and fed by Mail;
- a Mail-owned **`IWalletGate` port**, taking `Cents`, implemented by Economy (a state-dumped stub at M-1a).

It defines the port, not Economy.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (.NET) |
| **Domain** | Core (Godot-free `PerformativeMail.Sim/Mail`, `Sim/Economy` stub) |
| **Knowledge Risk** | LOW. No Godot API. Everything is plain C# in the Sim assembly, under ADR-0006's analyzers. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (nothing in this domain) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Mail hot paths at 0 B on the net8.0 leg; the M-PERF-2 timing on the reference machine (Q-ARCH-10). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (phase order, `I…Api` calls in the caller's phase, composition injection), ADR-0004 (`MailId` lower = older, `Destination` ids allocated in lot order at world build, `outcomeSeq` u32), ADR-0005 (`mail` / `mail.special` streams per shift), ADR-0006 (`Cents`, `MulDivRH`, canonical dump), ADR-0007 (op handler contract for `deliver_matching`), ADR-0008 (event payload ≤ 48 B, replication classes), ADR-0009 (`IInventoryApi`, `UnitLedger` `Backlog` state, phase-2 drain), ADR-0010 (`MailKindDef`, `DestinationTypeDef`, StatSheet) — all Accepted. ADR-0026 (`IWorldQuery` `Lots` table) — Accepted 2026-10-16. |
| **Enables** | ADR-0016 (`Evaluate` and F6/F7/F10 as shared facts functions), ADR-0018 (HUD wallet/backlog/complaint values), ADR-0021 (thrown landing consume), ADR-0022 (Mail JoinState block: registry state, `outcomeSeq`), the future Economy ADR (implements `IWalletGate`) |
| **Blocks** | mail-destinations epic (all Sim stories); manual-delivery `deliver_matching` consume story; throwing-catching landing story |
| **Ordering Note** | At M-1a, Run Structure is a shift-timer stub (phase 12) that pushes `DeliveriesState` into Mail. Economy is `StubWallet`. Feeders (M-1b) and NPC drops (later) use reserved entry points with no implementation yet. |

## Context

### Problem Statement
Mail decides every payment, penalty and complaint, at four call sites (hand, throw, feeder, NPC) in different phases, and every client must preview the same answer (ADR-0016). The GDD lists as ADR items the SoA table, the `AddressId` encoding (u32 vs interned string, L852), the wallet port and the backlog structure. Economy, Run Structure and World Generation have no GDDs yet, so Mail must depend only on ports it owns or stubs behind the final interfaces.

### Constraints
- The Sim is integer-only, 0 B on hot paths, iterates ascending ids, and is dumped field by field (ADR-0006).
- No Mail → Economy assembly or namespace reference (TR-mail-007, M-R7-2d).
- Calls go down the layer map. Same-layer peers are called via `I…Api` inside the caller's phase. No upward callbacks (ADR-0003).
- Destination `EntityId`s are allocated by World Generation in lot order at world build (ADR-0004 class `0x07`).
- An event payload is ≤ 48 B (ADR-0008). Narrow wire ids come from id maps, never truncation (ADR-0004, Sim Core R22).

### Requirements
- TR-mail-006..011, 013, 019, 020 (primary here).
- M-PERF-1/2: hand, throw and feeder paths allocate 0 B. An idle tick visits 0 rows and 0 buckets and updates exactly the feeder credits. ≤ 1.0 ms p99 / 2 ms max per tick; manifest roll ≤ 0.5 ms; a 40-unit op ≤ 0.2 ms.

## Decision

### 1. `AddressId`: packed u32, order = manifest order

```
bit 31........26 25........18 17................6 5......0
    districtId(6)  streetId(8)     number(12)       unit(6)   unit 0 = none
```
- `readonly struct AddressId(uint Value)` lives in `Sim/Primitives` (amended 2026-10-16 before acceptance: World's `LotRecord` (ADR-0026) and Inventory's `StackKey` (ADR-0009) both reference it, and both sit below Mail in ADR-0003's dependency direction; the packing and order rules here are unchanged), with accessors `District`, `Street`, `Number`, `Unit`. `0` is None (district ids start at 1).
- Numeric order of `Value` is lexicographic `(district, street, number, unit)`. Sorting the address list "by (district, street, number)" (R10) is therefore a plain `uint` sort with no comparer, and the manifest roller consumes it directly.
- Widths: 63 districts, 255 streets per district, 4,095 numbers, 63 units. All are above OQ15's ranges. The world builder rejects a lot outside them (`SimBuildException(AddressOutOfRange)`).
- ADR-0009's `addressId u32` in `StackKey` is this type. Interned strings are rejected (L852): they need a table lookup inside `Evaluate`, which would break its "0 registry scans" budget.
- **Wire:** events carry the full u32 in the Sim. The adapter codec narrows it to an `AddressOrdinal u16` (index in the run's ascending address table, identical on every peer from the world tables) through an id map. This is what Mail's 19 B wire figure (L694) assumes.

### 2. Mail table: paged SoA, indexed by `MailId`

- **Key.** `MailId`s are allocated only by Mail, in manifest order, and lower = older (ADR-0004). Row address = `(mailId − 1) >> 10` (page) and `(mailId − 1) & 1023` (slot). There is no hash map.
- **Columns per page** (each a preallocated array of 1,024):

  | Column | Type | Notes |
  |---|---|---|
  | `kind` | `DefIndex` (u16 + 1 storage) | `MailKindDef` |
  | `addressId` | `AddressId` u32 | |
  | `spawnValue` | `ushort` cents | F1 |
  | `spawnShift`, `deadlineShift` | `byte` | F5 |
  | `flags` | `byte` | Cursed, Found; Priority and Fragile reserved |
  | `live` | bit in a `ulong[16]` bitmap | |

  There is no location column: where a unit is belongs to Inventory's `UnitLedger` (ADR-0009). A row is retired (live bit cleared, page `liveCount − 1`) on consume or destroy.
- **Pages** come from a pool preallocated at world build. Its size is `SimSettings.mailTablePages` (default 64, so 65,536 live rows). A page returns to the pool when its slots are all allocated and its `liveCount` reaches 0. Pool exhaustion grows the pool, records `SimCheck(MailTableGrow)` and allocates; this is the only allocating path, and the default size makes it a test failure.
- **Server-only.** The table never replicates. Clients learn a unit's facts from the inventory records that carry its `mailId` runs plus `MailUnitFacts` in their mirror (ADR-0016) — inventory records carry `StackKey (kind, addressId, …)` per ADR-0009.
- **Dump:** live rows in ascending `mailId`, field by field (ADR-0006).

### 3. Destination registry (R3)

- **Built once** in `Constructed`, after World Generation (the M-1a stub, ADR-0026) has allocated destination `EntityId`s in lot order. Mail reads `IWorldQuery.Lots` and builds its rows in that order. **World allocates the ids; Mail builds the registry.** This reconciles Sim Core R19 with Mail R3.
- **Dense index** `DestinationOrdinal` (u16) = lot order. It is also the id map for the wire's `destination u16`.
- **SoA columns:** `EntityId`, `AddressId`, `TypeIndex` (`DestinationTypeDef`), `DistrictId`, interaction anchor (`Vec3Mm`), catch volume (axis-aligned box, mm), `feederSlot`; plus per-door complaint state `tokensPm` (long) and `lastTick` (long).
- **Reverse map** `AddressId → ordinal`: a sorted `uint[]` + `ushort[]` pair with binary search (≤ 10 probes at 1,000 lots). It is used only for misdelivery attribution, never in `Evaluate`.
- **State.** `Evaluate` needs OPEN / CLOSED / LOCKED. LOCKED = `districtId ∉ unlockedDistricts`. CLOSED comes from `deliveriesOpen`. Both arrive from Run Structure through a downward call `IMailApi.SetDeliveriesState(open, closeTick, unlockedMask)` (Run is the orchestrator). Mail re-raises nothing; Run's `DeliveriesState` event is the replicated one.

### 4. `Evaluate` and the shared arithmetic

- `MailRules.Evaluate(in DestinationFacts d, in UnitFacts u) → DeliverEval` is `static`, pure and 0-alloc.
- Checks, in order: CLOSED `closed` → LOCKED `not_on_route` → non-mail `wont_fit` → kind not accepted (a bitmask on the type def) `wont_fit` → `u.Address == d.Address` Accept → Misdeliver. That is ≤ 6 checks and no registry access.
- `MailPay.Timeliness` (F5), `UnitPay` (F6), `UnitPenalty` (F7), `HintPayout`/`HintPenalty` (F10) and `WalletGuard` (F8) are static functions over `Cents`/permille, with one `MulDivRH` per unit.
- The client calls the same functions (ADR-0016 §3). Facts constructors exist on both sides and are covered by M-INT-2.

### 5. One consume routine (R6, TR-mail-008)

All paths enter `MailConsumer.Consume(in ConsumeCall c)` (internal to `Sim/Mail`), through `IMailApi` entry points called by the owning system **in its own phase**:

| Path | Entry point | Phase (ADR-0003) | `actionStartTick` |
|---|---|---|---|
| hand | `ConsumeHand(dest, ReadOnlySpan<EntryRef> inScanOrder, actor, holdStartTick)` from Delivery's `deliver_matching` handler | 0 `InputsApply` (opSeq order) | `holdStartTick` |
| throw | `ResolveThrownLanding(dest, in EntryRef, thrower, thrownPm, releaseTick)` | 4 `ThrowingStep` (flight order) | `releaseTick` |
| feeder | `ConsumeFed(dest, feederId, in UnitRef)` (M-1b) | 5 `AutomationStep` (ascending construct id) | none, so no grace |
| NPC | `ConsumeNpcDrop(dest, vehicleId, …)` (later) | 6 `VehiclesStep` (ascending vehicle id) | none |

R6's fixed tick order (hand → thrown → feeders → NPC) is **phase order**, with no extra queue. Lateness is read at the apply tick.

Per unit, oldest first, inside the caller's phase:
1. Build the facts: effective state = OPEN, or within closing grace (`actionStartTick < closeTick && applyTick ≤ closeTick + closeGraceTicks`, 90; TR-mail-013).
2. Run `Evaluate`.
3. **Accept:** `IInventoryApi.ConsumeUnit`, then `IWalletGate.Credit(pay, CreditReason.DeliveryPay)`, then retire the row.
4. **Misdeliver:** `TryDebitPenalty(penalty, walletFloor)`. On `false`, stop the loop and set `stoppedAtFloor` (F8, exactly −500 ¢ allowed). Otherwise consume, update the complaint buckets (F9), and raise `CursedMisdelivered` if the unit is cursed.
5. **Reject:** nothing is consumed.

`MailConsumed` (Internal) is raised per unit. One `DeliveryOutcome` (Replicated) is raised per call per result group. The k outcomes of one hold get contiguous `outcomeSeq` values.

### 6. Backlog (R11, TR-mail-009): Mail feeds, Inventory stores and drains

- The queue's **storage** is Inventory's (`Sim/Inventory/Backlog`): an append-ordered ring of whole entries, O(1) append, with ADR-0025's per-footprint sub-queues inside once that ADR is written; M-1a ships one ring with the drain's skip rule (erratum 2026-10-22).
- Mail appends whole entries in manifest order through a downward call, `IInventoryApi.AppendBacklog(in BacklogEntry)`, in phase 8 at the batch tick, for units that did not fit the Intake. Units cross into the ledger's `Backlog` state there (ADR-0009).
- The drain is Inventory's phase 2 (≤ 32 per tick, append order, 0 probes when idle; ADR-0009).
- Mail reads `BacklogUnits`/`BacklogEntries` through `IInventoryApi` and publishes `backlogUnits` (u16, saturating) in `MailShiftStats` for HUD.
- **Why not literally "Mail owns the queue"?** A Mail-held queue drained in phase 2 would need Inventory (Core) to call up into Mail (Feature), which ADR-0003 forbids. Mail keeps everything the GDD gives it: what enters, in what order, when, and the counters HUD sees.

### 7. Complaint buckets and feeder credits (F9, F11; TR-mail-010/011/020)

- **Buckets.** Per door and team-wide: `tokensPm` and `lastTick` as `long`. Refill is **lazy, on touch only**: `tokens = min(capPm × window, tokens + min(now − lastTick, window) × capPm)`, elapsed clamped to `window` (900). Nothing iterates buckets per tick. `lastLateTick` is team-wide.
- **Feeder credits.** An `int[]` per placed feeder, in ascending construct id: test, then add `insertRateMilli`, then cap at 30,000. `Mail.Step` (phase 8) updates every credit each tick and touches nothing else when idle. The first insert waits `feederId mod 15` ticks (R12). Arrays exist at M-1a with 0 feeders.

### 8. `IWalletGate`: the Mail-owned port (R7, TR-mail-007)

```csharp
namespace PerformativeMail.Sim.MailDestinations.Ports;   // erratum 2026-10-20: was PerformativeMail.Sim.Mail.Ports — see Errata (folder Sim/Mail/)
public enum CreditReason : byte { None = 0, DeliveryPay = 1 /* 2–31 reserved for Mail; Economy defines its own reasons elsewhere */ }
public interface IWalletGate
{
    // Applies immediately iff balance − amount ≥ floor (equality allowed); returns false and changes nothing otherwise.
    bool TryDebitPenalty(Cents amount, Cents floor);
    void Credit(Cents amount, CreditReason reason);   // amount ≥ 0; applied immediately
}
```
- **Mail owns the interface.** Economy implements it in `Sim/Economy/`; the implementation is constructed before Mail — at M-1a the `StubWallet` is constructed in `SimComposition.Build`, after `BindWorld` and before the systems loop. The composition root injects the port into Mail's constructor (`SimSystemContext.WalletGate`; ADR-0003 §4 injectable row, Mail & Destinations only); `SystemGraph` is unchanged — the port is not a graph edge. *(Erratum 2026-10-28: was "is constructed before Mail. The composition root injects it into Mail's constructor; `SystemGraph` declares Mail → `IWalletGate`" — see Errata.)*
- **Types:** `Cents` (ADR-0006), not `int`. The architecture sketch already uses `Cents`; the GDD's `int cents` is synced (GDD impact).
- **Synchronous, in the caller's phase.** The next unit's floor check must see the previous unit's debit (F8, per unit). Phase 9 `EconomyFlush` is for Economy's own aggregation only, never for applying Mail's calls.
- **Mail never reads the balance.** `Evaluate` uses no wallet. The client preview's `floorRefusal` reads ADR-0016's `WalletMirror`, fed by Economy's replicated `WalletChanged` event.
- **One call per unit** (M-X-4): one `TryDebitPenalty` per misdelivered unit, one `Credit` per paid unit.
- **M-1a `StubWallet`** (`Sim/Economy/StubWallet.cs`): a `Cents` balance, starting at `SimSettings.startingWalletCents`. It implements `IWalletGate` and `ISimStateContributor`, raises `WalletChanged{balance, delta, reason}` (Replicated, ≤ 24 B), and binds no phase. The future Economy ADR replaces it behind the same port.
- Sim Core R3's "synchronous `IEconomyApi`" is Economy's API for *other* callers (shop, payday). **Mail never references it** (OQ27).

### 9. Events (≤ 48 B each, ADR-0008)

| Event | Class | Payload (Sim widths) |
|---|---|---|
| `DeliveryOutcome` | Replicated | `outcomeSeq u32, destination EntityId u32, actor PlayerSlot u8, via u8 {hand,throw,feeder,npc}, result u8, kind DefIndex u16, units u16, cents long (income or penalty), lateUnits u16, deadUnits u16, complaintPermille u16, flags u8 {stoppedAtFloor, cursed}, intendedAddress AddressId u32` = 34 B |
| `MailConsumed` | Internal | `mailId, result, cents, late, dead, actor` |
| `ComplaintRaised` | Replicated | `destination EntityId, source u8, amountPermille u16` (GDD's `mailboxId` → `destination`) |
| `CursedMisdelivered` | Replicated | `destination, units` |
| `FeederStateChanged` | Replicated | `destination, state` (M-1b) |
| `BatchArrived` | Replicated | `units u16` |
| `MailShiftStats` | Replicated | shift totals + `backlogUnits u16` |

`outcomeSeq` is u32 in the Sim and u16 on the wire (serial arithmetic). A rejoiner drops outcomes with `outcomeSeq ≤` its JoinState's (ADR-0022).

### 10. Manifest roller (R10)
Stream `RngDerive(runSeed, "mail", shift)` (ADR-0005). It draws exactly `K = 2·nb + 2N + max(0, N−1)` over the ascending `AddressId` list from §3. Specials use `mail.special`. It runs in phase 8 at the batch tick, within ≤ 0.5 ms.

### Architecture Diagram

```
           Run (phase 12) ──SetDeliveriesState──┐            Economy (StubWallet at M-1a)
                                                ▼                 ▲ implements
 Delivery op handler (ph 0) ─ConsumeHand──►  ┌──────────────── Mail ─────────────────┐
 Throwing.Step (ph 4) ─ResolveThrownLanding─►│ MailConsumer.Consume (one routine)     │──IWalletGate──┘
 Automation (ph 5) ─ConsumeFed (M-1b)──────► │  Evaluate · F5–F8 · F9 buckets (lazy)  │
                                             │ MailTable (paged SoA by mailId)        │──IInventoryApi──► Inventory
 IWorldQuery.Lots (build) ─────────────────► │ DestinationRegistry (lot order, SoA)   │   ConsumeUnit / AppendBacklog
                                             │ Mail.Step (ph 8): manifest, feeders    │   (drain: Inventory ph 2)
                                             └── events: DeliveryOutcome … ───────────┘
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.MailDestinations;   // erratum 2026-10-20: was PerformativeMail.Sim.Mail — see Errata (folder Sim/Mail/)
public readonly struct AddressId { public readonly uint Value; public int District { get; } public int Street { get; } public int Number { get; } public int Unit { get; }
    public static AddressId Pack(int district, int street, int number, int unit = 0); }
public readonly struct DestinationFacts { public readonly EntityId Id; public readonly AddressId Address; public readonly DefIndex Type; public readonly DestState State;
    public readonly ulong AcceptsMask;   // bit k = mail kind with DefIndex.Value k accepted (kinds ≥ 64 never set)
    public readonly bool RequiresUnit;   // DestinationTypeDef.RequiresUnit
    public DestinationFacts(EntityId id, AddressId address, DefIndex type, DestState state, ulong acceptsMask, bool requiresUnit); }
public readonly struct UnitFacts { public readonly DefIndex Kind; public readonly ItemCategory Category; public readonly AddressId Address; public readonly ushort SpawnValue; public readonly byte DeadlineShift; public readonly MailFlags Flags; }
public static class MailRules { public static DeliverEval Evaluate(in DestinationFacts d, in UnitFacts u); }
public static class DeliveriesStateRules { public static DestState Effective(bool districtUnlocked, bool openNow, uint closeTick,
                                                                          uint? actionStartTick, uint applyTick, int closeGraceTicks); }
public readonly struct HintRun { public readonly byte DeadlineShift; public readonly byte Count; public readonly ushort UnitSpawnValue; }
public static class MailPay {                                            // ref SimCounterSink is last on every money function
    public static int Timeliness(int currentShift, int deadlineShift, int lateValueRatioPm, int deadLetterShifts);
    public static int LateValueRatioPm(IStatSheet s);
    public static Cents UnitPay(in UnitFacts u, int timelinessPm, int thrownPm, IStatSheet s, PlayerSlot? p, ref SimCounterSink sink);
    public static Cents UnitPenalty(in UnitFacts u, int misdeliveryPenaltyRatioPm, int minPenaltyCents, IStatSheet s, ref SimCounterSink sink); // ratio/min from MailBalance
    public static bool WalletGuard(Cents balance, Cents penalty, Cents floor);   // floor passed as Cents (no Economy walletFloor key yet)
    public static Cents HintPayout(ReadOnlySpan<HintRun> runs, int currentShift, DefIndex kind, int lateValueRatioPm, int deadLetterShifts,
                                   int thrownPm, IStatSheet s, PlayerSlot? p, ref SimCounterSink sink);
    public static Cents HintPenalty(ReadOnlySpan<HintRun> runs, DefIndex kind, int misdeliveryPenaltyRatioPm, int minPenaltyCents,
                                    IStatSheet s, ref SimCounterSink sink); }
public interface IMailApi
{
    void ConsumeHand(EntityId dest, ReadOnlySpan<EntryRef> inScanOrder, PlayerSlot actor, uint holdStartTick);
    LandingResult ResolveThrownLanding(EntityId dest, in EntryRef e, PlayerSlot thrower, int thrownPm, uint releaseTick);
    void ConsumeFed(EntityId dest, EntityId feeder, in UnitRef u);                 // M-1b
    void SetDeliveriesState(bool open, uint closeTick, ulong unlockedDistrictMask); // Run only
    bool TryGetDestinationFacts(EntityId dest, out DestinationFacts f);
}

// Inventory side of §6 (namespace PerformativeMail.Sim.Inventory, members of ADR-0009's IInventoryApi); erratum 2026-10-22
public readonly ref struct BacklogEntry      // 1–255 strictly ascending ids of one mail kind, address and flags
{ public BacklogEntry(DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids); }
//  bool AppendBacklog(in BacklogEntry e);   // phase 8; false + nothing mutated on malformed input, out of phase, or a pool/arena refusal (units stay Mail's)
//  int BacklogUnits { get; }                // units in the backlog
//  int BacklogEntries { get; }              // entries in the backlog
```

### Implementation Guidelines
- Mail **must** keep the mail table server-only and **must never** add a location column. Location is the `UnitLedger`'s.
- Every consume path **must** go through `MailConsumer.Consume`. There is no second payment or penalty path.
- `Evaluate` and `MailPay` **must** be static, pure and 0-alloc over facts structs (ADR-0016).
- `PerformativeMail.Sim.MailDestinations` (folder `Sim/Mail/`; erratum 2026-10-20) **must never** reference `PerformativeMail.Sim.Economy` (arch test). Wallet access is only `IWalletGate`.
- Complaint buckets **must** refill lazily on touch. `Mail.Step` **must** touch only feeder credits when idle.
- Mail **must** append backlog entries through `IInventoryApi`. Inventory **must never** call Mail.
- Wire narrowing of `AddressId`/destination **must** use the ordinal id maps (§1, §3).

## Alternatives Considered

### Alternative 1: `Dictionary<MailId, MailRow>` (AoS)
- **Pros**: Simple. Sparse-friendly.
- **Cons**: Hash ordering is banned for iteration (ADR-0006). Allocates on growth. Worse cache behaviour on manifest and dump walks.
- **Rejection Reason**: Monotonic, never-reused ids make direct paging strictly better.

### Alternative 2: Interned address strings (L852 option)
- **Pros**: Human-readable ids.
- **Cons**: A lookup inside `Evaluate`. Strings in the Sim hot path. No free ordering.
- **Rejection Reason**: Breaks the 0-scan, 0-alloc `Evaluate` budget.

### Alternative 3: Mail-owned backlog queue drained by Inventory through a port
- **Pros**: Matches GDD R11's wording.
- **Cons**: Inventory (Core) would call up into Mail (Feature) in phase 2, a callback ADR-0003 forbids. The ledger's `Backlog` state would live apart from its storage.
- **Rejection Reason**: Layer violation. §6 gives Mail the same control through downward calls.

### Alternative 4: Mail calls Economy's `IEconomyApi` directly
- **Pros**: One wallet API.
- **Cons**: A Mail → Economy reference (TR-mail-007 forbids it). Mail would be blocked on an undesigned system.
- **Rejection Reason**: The port keeps Mail buildable now and Economy free later.

## Consequences

### Positive
- `Evaluate` is O(1) with no lookups. The manifest walks an already-sorted `uint[]`.
- Idle cost is near zero: no row or bucket walks.
- Mail ships at M-1a against `StubWallet` and the world stub, and swaps implementations later without touching Mail.

### Negative
- Page pool sizing is a tuning knob. A long-lived unit (Lost Parcel) pins its page.
- Address widths cap the world at 63 districts × 255 streets × 4,095 numbers (ample, but a hard limit).
- Backlog ownership is split: Mail controls entry, Inventory stores.

## Risks
- **Economy's real implementation needs deferred application** (for example, for Perk interest). *Mitigation:* the port semantics (immediate, per unit) are fixed here; Economy can aggregate in phase 9 for its own reporting only.
- **The page pool is exhausted in a long Free Play run.** *Mitigation:* the counted grow path, plus a soak test asserting no grow in a 30-shift run.
- **Grace logic differs between host facts and client facts.** *Mitigation:* `DestState` effective-state helper shared by both (ADR-0016 facts), plus M-INT-2 includes CLOSED-with-grace pairs.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| mail-destinations.md | R2 / TR-mail-006: server-only SoA table keyed by mailId; packed AddressId | §1, §2 |
| mail-destinations.md | R7 / TR-mail-007: wallet via Mail-owned `IWalletGate`; no Mail→Economy reference | §8 |
| mail-destinations.md | R6 / TR-mail-008: one consume routine, fixed tick order | §5 (order = phase order) |
| mail-destinations.md | R11 / TR-mail-009: backlog ordered O(1) append; drain owned by Inventory; `backlogUnits` u16 | §6 |
| mail-destinations.md | F9 / TR-mail-010: token buckets int64, elapsed clamped | §7 |
| mail-destinations.md | F11, R12 / TR-mail-011: integer feeder credit; stagger `feederId mod 15` | §7 |
| mail-destinations.md | R4 / TR-mail-013: closing grace by action start tick, 90 | §5 step 1 |
| mail-destinations.md | M-PERF-2 / TR-mail-019: ≤ 1.0 ms p99 / 2 ms; manifest ≤ 0.5 ms; 0 alloc | §2, §7, §10; Validation |
| mail-destinations.md | M-PERF-1 / TR-mail-020: idle tick touches only feeder credits | §7 |

## Performance Implications
- **CPU**: `Evaluate` ≤ 6 checks. Consume is O(units). Manifest is O(addresses). Idle cost is O(feeders).
- **Memory**: one page = 1,024 × ~11 B + bitmap ≈ 11 KB; 64 pages ≈ 0.7 MB preallocated. Registry ≈ 64 B × lots.
- **Load Time**: registry build at world build, O(lots log lots).
- **Network**: `DeliveryOutcome` ≤ 19 B on the wire with ordinal narrowing (§1, §3).

## Migration Plan
No code exists. M-1a order:
1. `AddressId` + `MailTable` + dump contributor.
2. `DestinationRegistry` from the ADR-0026 stub.
3. `Evaluate`/`MailPay` + golden tests.
4. `StubWallet` + `IWalletGate`.
5. `MailConsumer` hand and throw paths.
6. Manifest roller + `AppendBacklog`.

Feeders at M-1b; Economy replaces the stub when its GDD and ADR land.

## Validation Criteria
- `AddressId` pack/unpack round-trip, and order equality with `(district, street, number, unit)` tuple order over random sets.
- M-PERF-1: 0 B on hand/throw/feeder paths after warm-up; an idle tick visits 0 rows and 0 buckets (counters).
- M-PERF-2 timing (advisory until Q-ARCH-10).
- M-X-4: exactly one wallet call per unit; F8 stops at the first failure; −500 ¢ exactly is allowed.
- Arch test: no `Sim.Mail` type (namespace `PerformativeMail.Sim.MailDestinations*`, erratum 2026-10-20) references `Sim.Economy`.
- Contiguous `outcomeSeq` for a hold's k outcomes. Dump determinism (T1) with the mail table and registry included.

## GDD Impact (do not edit GDDs here; reconcile at re-review)
- **mail-destinations.md R7:** `IWalletGate(int cents, int floor)` → `Cents`. `CreditReason` is defined here (`DeliveryPay`).
- **mail-destinations.md R11:** "Mail owns the queue" → Mail owns entry policy, order and counters; storage and drain are Inventory's (§6).
- **mail-destinations.md L852:** `AddressId` decided: packed u32.
- **mail-destinations.md L142:** `ComplaintRaised{mailboxId}` → `destination`.
- **mail-destinations.md M-PERF-2 vs simulation-core.md HB4/OQ27:** 1.0 ms p99 (GDD) is adopted as advisory. The 0.25/1.0 split belongs in `perf-budget.md`.
- **simulation-core.md R3 phase 9:** "synchronous `IEconomyApi`" is not Mail's path. Mail uses `IWalletGate` only.
- **simulation-core.md R19 vs mail-destinations.md R3:** reconciled as World allocates ids, Mail builds the registry (§3). No text change is strictly needed; a clarifying note is advised.
- **player-character-movement.md L208:** `Delivered{actorPlayerId}` → `DeliveryOutcome{actor}`.

## Related
- ADR-0003, 0004, 0005, 0006, 0007, 0008, 0009, 0010 (Accepted); ADR-0016 (shared functions, client mirrors), ADR-0026 (world query), ADR-0025 (backlog sub-queues), ADR-0022 (JoinState Mail block); future Economy ADR.
- `design/gdd/mail-destinations.md` R2–R16, F1–F11, M-PERF-*, OQ7/OQ27, L852.

## Errata
- 2026-10-17 — Key Interfaces: `MailPay.UnitPay` was `(u, timelinessPm, s, p)`; now `(in UnitFacts, int timelinessPm, int thrownPm, IStatSheet, PlayerSlot?, ref SimCounterSink)` — `thrownPm` is a parameter and the sink is last; source TD-096 item 1.
- 2026-10-17 — Key Interfaces: `MailPay.UnitPenalty` was `(u, s)`; now `(in UnitFacts, int misdeliveryPenaltyRatioPm, int minPenaltyCents, IStatSheet, ref SimCounterSink)`, the ratio and minimum coming from `MailBalance`; source TD-096 item 1.
- 2026-10-17 — Key Interfaces: `MailPay.WalletGuard(Cents balance, Cents penalty, Cents floor)` unchanged, now annotated that the floor is a `Cents` parameter because no Economy `walletFloor` key exists yet; source TD-096 item 1.
- 2026-10-17 — Key Interfaces and §4 helper list: the sketch omitted `MailPay.Timeliness`, `LateValueRatioPm`, `HintPayout`, `HintPenalty` and the `HintRun` struct; now listed with their shipped signatures; source TD-096 item 1.
- 2026-10-17 — Key Interfaces: `DestinationFacts` lacked `ulong AcceptsMask` and `bool RequiresUnit` (and its public ctor); `UnitFacts.Flags` was `byte`, shipped as `MailFlags`; `DeliveriesStateRules.Effective(districtUnlocked, openNow, closeTick, actionStartTick?, applyTick, closeGraceTicks)` was missing; now listed. `MailRules.Evaluate` already matched; source TD-096, TD-092.
- 2026-10-20 — Key Interfaces and Implementation Guidelines: the namespace was `PerformativeMail.Sim.Mail` (and `PerformativeMail.Sim.Mail.Ports`); now **`PerformativeMail.Sim.MailDestinations`** (tests `PerformativeMail.Sim.Tests.Unit.MailDestinations`), as shipped by mail-destinations story 001, because a `Sim.Mail` namespace shadows `Content.Rules.Mail` / `BalanceKeys.Mail`; the folder stays `src/PerformativeMail.Sim/Mail/`. The not-yet-built ports namespace follows suit: `PerformativeMail.Sim.MailDestinations.Ports` (mail-destinations story 005). The "Mail never references Economy" arch test keys on `PerformativeMail.Sim.MailDestinations*` (or the `Mail/` folder) so it cannot pass vacuously. **Accepted by Evan 2026-10-20** (owner ruling in chat, 22:49 PT); source TD-074 item 1.
- 2026-10-22 — §6 and Key Interfaces (owner-approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT — "to all recommended decisions", then "Yes, approve all of it including the ADR text changes"; text only, no Decision change, ADR stays Accepted): §6 l.136 "with ADR-0025's per-footprint sub-queues inside" now reads "… inside once that ADR is written; M-1a ships one ring with the drain's skip rule". Key Interfaces gain the backlog shapes §6 names but never defined, as built by grid-inventory 021: `readonly ref struct BacklogEntry(DefIndex MailKind, uint AddressId, byte Flags, ReadOnlySpan<MailId> Ids)` (1–255 strictly ascending ids), `bool AppendBacklog(in BacklogEntry)`, `int BacklogUnits`, `int BacklogEntries` (members of ADR-0009's `IInventoryApi`); source TD-157 items 1–2.
- 2026-10-28 — §8 l.159: read "Economy implements it in `Sim/Economy/` and is constructed before Mail. The composition root injects it into Mail's constructor; `SystemGraph` declares Mail → `IWalletGate`." A Mail → Economy edge is impossible as shipped: `SystemGraph` already has Economy → MailDestinations (`SystemGraph.cs:61`), so `Build` would throw `DependencyCycle`; `ShippedOrder` builds Economy after Mail and `SimProfile.M1aGreybox` has no Economy. Now: the composition root injects the port (`SimSystemContext.WalletGate`); `SystemGraph` is unchanged; and "constructed before Mail" refers to the stub's construction in `SimComposition.Build` (after `BindWorld`, before the systems loop), bound as a state contributor (`SectionIds.Wallet`). ADR-0003 §4's injectable table gains the matching `IWalletGate` row (Mail & Destinations only) by its own erratum of the same date. Approved by Evan 2026-10-28 7:26 AM PT, in his own words in the Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve the ADR-0003 §4 and ADR-0017 §8 errata: the composition root injects IWalletGate into Mail only, and SystemGraph is unchanged"; source: mail-destinations 005 readiness (gap 1 item 6), TD-177. Text only; the Decision is unchanged and the ADR stays Accepted.
- 2026-10-23 — erratum **E2** (l.321): "per-tick and per-frame zero-allocation checks are reported, not blocking, until M0." Approved by Evan, Mail Lead chat, 2026-10-23 8:10 AM PT: "approve all" (decision log 2026-10-23).
