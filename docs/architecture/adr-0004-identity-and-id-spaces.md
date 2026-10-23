# ADR-0004: Identity and Id Spaces

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
`/architecture-decision` (autonomous mode, `--review lean`) on the owner's standing instruction to plan autonomously; engine check by `godot-specialist`. TD-ADR skipped (lean mode). Inputs: `design/gdd/simulation-core.md` R6 (`actor`), R19–R22, F4 (Provisional); `design/gdd/grid-inventory.md` R2 and Network (temp ids); `design/gdd/content-data-definitions.md` CD-ids (`DefIndex`, `StringId`); `design/gdd/throwing-catching.md` (`flightId`); `docs/architecture/simulation-core-adr-inputs.md` §1 (F4 rate correction, `(uint)class << 24`), §3.5, §3.12 (unreviewed seed, re-checked against the GDDs).

- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Seven GDDs exchange ids: entities, mail, players, inventory entries, flights, defs and strings. Each needs one width, one allocator, one "none" value and one rule for reuse and wrap, or replay and the wire drift apart. This ADR fixes one table of id spaces:
- a `uint32` `EntityId` = 8-bit class + 24-bit per-class counter (classes 0x01–0x08 shipped, 0x09–0xEF reserved, 0xF0–0xFF tests);
- separate never-reused `uint32` counters for `MailId` and `EntryId` (bit 31 reserved for client temp ids `0x8000_0000 | slot << 20 | ctr`);
- a `byte` `PlayerSlot` 0–7;
- a wrap-safe `ushort` `FlightId`;
- per-type `DefIndex` ordinals that are never persisted.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 .NET, C# |
| **Domain** | Core (plain-C# Sim data types; no engine surface) |
| **Knowledge Risk** | LOW. Every type lives in `PerformativeMail.Sim` (zero Godot references, ADR-0001). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (nothing applicable) |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None engine-side. Godot views map `EntityId` → node through a C# `Dictionary` owned by Presentation (ADR-0019); no Godot type ever holds an id's meaning. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 (types live in `PerformativeMail.Sim/Primitives` and `/Core`; `SimAssert`/`SimCheckRing` for exhaustion records) |
| **Enables** | ADR-0003 (`EntityIds`/`MailIds` in the injectable list), ADR-0005 (stream indices by id), ADR-0007 (`ActorId`, `opSeq` width), ADR-0009 (`EntryId`, temp-id namespace, `world_item`), ADR-0010 (`DefIndex`/`StringId` storage), ADR-0011 (slot ↔ peer map, narrow wire ids), ADR-0015/0021 (`FlightId`), ADR-0017 (`MailId`, destination ids), ADR-0022 (rejoin keeps `playerId`/slot), ADR-0024 (persist string ids, never `DefIndex`) |
| **Blocks** | Sim Core "ids and allocators" story; every system story that allocates or stores an id |
| **Ordering Note** | Semantics of system-local ids (when Grid mints or retires an `EntryId`, Throwing's flight lifecycle) stay with the owning GDD/ADR. This ADR fixes only width, encoding, allocator, none value, wrap and reuse. |

## Context

### Problem Statement
Spec 07 sketched an entity id. The GDDs then added classes:
- Sim Core DEV-4 adds `npc`, `destination` and `prop`;
- Grid Inventory adds `entryId` with client temp ids;
- Throwing adds a 2-byte flight id;
- Content & Data adds `DefIndex` ordinals and `StringId`.

These are spread across five documents. Without one table, two systems pick different "none" values or widths, or somebody narrows an `EntityId` to its low bits for the wire. Replay (ADR-0006) and rejoin (ADR-0022) then break silently.

### Constraints
- Integer, culture-free, deterministic ordering by raw value (Sim Core R20 tie-breaks; ADR-0006).
- Allocation-free in the tick (R32): ids are value types, and allocators are preallocated arrays.
- Spec 08 §3 examples: Player 1 = 0x01000001 = 16 777 217; Construct 1 = 0x03000001 = 50 331 649.
- Seed corrections:
  - write `(uint)cls << 24` (a byte 0xF0 shifted as `int` goes negative);
  - per-class allocation capacity is about 4 660/s for a 1 h run (2^24 / 3600), so ops are not the only allocation source but exhaustion is still unreachable.

### Requirements
- TR-sim-025: `EntityId` `uint32` = class << 24 | counter; classes 01–08; 09–EF reserved; F0–FF tests; never reused.
- TR-sim-026: `mailId` separate `uint32`; `PlayerSlot` u8 0–7; a slot freed in T is reusable from T+1.
- TR-content-007: def id regex, sorted ordinal into a `DefIndex` ushort, 0xFFFF none.
- TR-content-008: `DefIndex` never persisted; on the wire only after a content-hash match.
- TR-content-009: `StringId` = (namespace, `DefIndex`); run-time strings interned per run.
- TR-inventory-003: an entry is `uint32 entryId`, `uint8 count`, ascending `mailIds[]`; footprint w×h, rotation.
- TR-throwing-009: `FlightRecord` snapshot fields; `flightId` wrap-safe u16.

## Decision

### 1. The id-space table (authoritative)

| Id | C# type | Width | Encoding / range | None | Allocated by (where) | Reuse / wrap | Owner of semantics |
|---|---|---|---|---|---|---|---|
| `EntityId` | `readonly struct EntityId` | u32 | `((uint)cls << 24) \| counter`, counter 1 … 2^24−1 | `0` | `EntityIds.Allocate(cls)`, Sim thread inside `Tick()`; `destination`/`prop` also at world build in `Constructed` | never reused, never wraps; exhaustion → `None` | Sim Core (table); class owners (lifecycle) |
| `EntityClass` | `enum EntityClass : byte` | u8 | see §2 | `0x00` (never allocated) | — | append-only | Sim Core |
| `ActorId` | `readonly struct ActorId` | u32 | a `player` or `construct` `EntityId`; `System = 0x00000001` | `0` | adapter stamps from transport peer (never from payload) | — | Sim Core / ADR-0007 |
| `MailId` | `readonly struct MailId` | u32 | 1 … 2^32−1; lower = older | `0` | `MailIds.Allocate()`, Sim thread inside `Tick()`, in manifest order at the batch tick | never reused, never wraps; exhaustion → `0` | Mail & Destinations |
| `PlayerSlot` | `readonly struct PlayerSlot` | u8 | 0–7 | `default(PlayerSlot)`; `Value` reads `0xFF` (stored `+1`, like `DefIndex`) | assigned by Networking & Session; Sim keeps `slot ↔ playerId` | freed in tick T, reassignable from T+1 | Sim Core (table), Networking (assignment) |
| `EntryId` | `readonly struct EntryId` | u32 | real: 1 … 0x7FFF_FFFF (bit 31 clear); temp: `0x8000_0000 \| slot << 20 \| ctr`, `slot` 0–7 in bits 20–22, `ctr` 0 … 0xFFFFF in bits 0–19, bits 23–30 zero | `0` | real: Grid Inventory's run counter (Sim); temp: the client, per connection | real never reused; a retired id is never re-minted; temp namespaces reset on rejoin | Grid Inventory (ADR-0009) |
| `ContainerId`, container `version`, Intake `arrivalSeq` | `uint` / owner struct | u32 | 1 … 2^32−1 | `0` | Grid Inventory | never wrap within a run | Grid Inventory |
| `FlightId` | `readonly struct FlightId` | u16 | any value; wrap-safe (`SerialU16`) | none reserved; "no flight" is a separate bool | Throwing, creation order | **wraps**; never compared numerically; processing order = creation order (list order) | Throwing (ADR-0015/0021) |
| `ReqId` | `uint` in the op header | u32 in Sim | opaque, echoed only | — | each client | wire width/dedupe: ADR-0007 / ADR-0011 | owning protocol |
| `opSeq` | `uint` | u32 | 1 … 2^32−1 | `0` | `SimWorld.EnqueueOp` | never reused, never wraps; exhaustion → `EnqueueOp` false | ADR-0007 |
| `outcomeSeq` | `uint` | u32 | per Mail GDD | — | Mail | per Mail | Mail (ADR-0017) |
| `DefIndex` | `readonly struct DefIndex` | u16 | ordinal 0 … 65 533 in a def type's ordinal-sorted id list | `0xFFFF` (wire/canonical); `default(DefIndex)` is None | `ContentLoader` stage 3 | rebuilt per `ContentSet`; **never persisted**; on the wire only after a content-hash match | Content & Data (ADR-0010) |
| `StringId` | `readonly struct StringId` | u8 ns + u16 | `(StringNamespace ns, ushort index)`; def namespaces = def type, `Run` = per-run intern table | `ns == None` | content: loader; `Run`: the Sim's per-run interner | `Run` table lives for one run | Content & Data (content), Sim Core (`Run` table) |

### 2. `EntityClass` table (append-only; a new class is an amendment to this ADR and Sim Core R19)

| Byte | Name | Allocated by | Notes |
|---|---|---|---|
| 0x00 | `None` | never | `ActorId.System` = 0x00000001 lives here: class 0 is never allocated, so it cannot collide |
| 0x01 | `Player` | Sim Core, `System` `PlayerJoin` handler, phase 0 only | rejoin keeps the same id (R21) |
| 0x02 | `WorldItem` | Grid Inventory via Sim Core | Grid R12 |
| 0x03 | `Construct` | Building | |
| 0x04 | `Vehicle` | Vehicles | |
| 0x05 | `Enemy` | Enemy AI & Waves | |
| 0x06 | `Npc` | Vehicles & NPC Drivers | DEV-4 |
| 0x07 | `Destination` | World Generation, lot order, at world build | DEV-4; the u32 form Manual Delivery and HUD use |
| 0x08 | `Prop` | World Generation | DEV-4 |
| 0x09–0xEF | reserved | — | `Allocate` returns `None`, asserts, writes one check record |
| 0xF0–0xFF | test/debug | test fixtures only | `Allocate` succeeds only when `EntityIds` was built with `allowTestClasses: true`; a shipped composition never sets it |

- **Mail is not an entity class** (spec 07 §4.2). `MailId` is its own counter.

### 3. Allocators

- `EntityIds`:
  - one `uint[256]` of **last-issued** counters per world (0 = none issued), preallocated;
  - `Allocate(EntityClass)` → `EntityId`;
  - `Peek(EntityClass)` for tests and dump, returning the last issued counter;
  - a constructor from initial last-issued counters (Free Play save/load path, spec 07 §7.3; tests reach exhaustion without a seam). Values above 2^24−1 clamp to exhausted.
- `MailIds`: one `uint` counter holding the last issued id (0 = none), `Allocate()` → `MailId`, `Peek()` → last issued, constructor from a last-issued value (default 0, so the first id is 1).
- `SimInitialCounters` carries the same last-issued convention: `LastOpSeq`, `LastMailId`, `LastEntityCounters`.
- **Thread and phase:**
  - Allocation is legal only on the owner thread inside `Tick()`. The exception is `Destination`/`Prop` while the world is `Constructed` (world build).
  - `SIM_ASSERT` builds assert both.
  - In Release an illegal call returns `None` and counts `idAllocIllegal` (ADR-0001 `SimCounters`).
- **Exhaustion:** a class counter at 2^24−1, or `MailIds` at 2^32−1, returns `None`/`0`. It writes one `SimCheckRing` record (`IdExhausted`, a = class, b = counter) and asserts. The caller refuses the spawn. Ids never wrap or skip.
  - F4: worst case 480 allocations/s gives 9.7 h per class. Even at the seed's corrected per-class capacity (~4 660/s sustained for a 1 h run), exhaustion is unreachable in v1. Free Play must re-check.
- **Ordering and tie-breaks** use the raw `uint` ascending. Within one class that is creation order.
  - `EntityId` implements `IComparable<EntityId>` on `Value`. `MailId` likewise (lower = older).
  - **Never** order by `GetHashCode()` (ADR-0006).
- Players' "lowest entityId" rules (PC), Mail's "lower mailId is older" and Grid's "mailIds ascending" all rely on this.

### 4. Players and slots (R21)
- `PlayerSlot` (0–7) is the compact wire form: Throwing's `throwerId`, the transform index, Grid temp-id namespaces, HUD pings.
- `SlotTable` (Core) is a `PlayerSlotEntry[8]` array: `{ EntityId playerId; uint freedTick; bool occupied }`.
- A slot freed in tick T can be assigned again only when `CurrentTick ≥ T + 1`. A `PlayerJoin` for it in the same drain is rejected `slot_busy`.
- Other rejects: `slot_invalid` (≥ 8) and `duplicate_player` (present and not Frozen). Nothing is allocated on a reject.
- The Sim never sees accounts or transport peers. Networking keeps account → `playerId` (rejoin) and peer → slot.

### 5. Wire narrowing and serial helpers (`Primitives/`)
- `SerialU8`, `SerialU16`, `SerialU32` (RFC 1982):
  - `Diff(a, b)` is `(a − b) mod 2^n` read as signed;
  - `IsNewer(a, b)` ⇔ `Diff ∈ [1, 2^(n−1) − 1]`;
  - the exact half-range difference is neither newer nor older and asserts.
  - All arithmetic is `unchecked(...)` (ADR-0001 §4).
- `WireNarrow.ToU16(uint v)` etc.: assert in `SIM_ASSERT`, clamp and record in Release.
- **A narrow wire id must come from an id map, never from the low bits of an `EntityId`.** Example: Throwing's 2-byte `receiverId` comes from a per-run receiver map that Throwing/ADR-0021 owns, because low bits are not unique across classes.

### 6. Content ids (`DefIndex`, `StringId`)
- Def id strings match Content & Data's id regex (lower-case ASCII, validated). Per def type, the loader sorts them **ordinally** (byte order = UTF-8 order for validated ASCII) and numbers them from 0. That number is the `DefIndex`.
- At most 65 534 defs per type (CV-GEN-09), so ordinals run 0 … 65 533.
- **Representation.** `DefIndex` stores `_v = ordinal + 1`, so `default(DefIndex)` is None. `DefIndex.Value` returns the ordinal, or `0xFFFF` for None. Canonical dumps and the wire write `Value`, so `0xFFFF` is the single external "none" sentinel (TR-content-007).
- `DefIndex` is meaningful only with the content hash that produced it:
  - **never persisted** (profiles store the string id; ADR-0024);
  - sent on the wire only after Hello/HelloAck confirmed the hash (ADR-0011).
- `StringId` = `(StringNamespace ns, ushort index)`.
  - `StringNamespace` is a `byte` enum: one value per def type, plus `Run`.
  - Adding a mail kind never renumbers items.
  - Run-time strings (street names, addresses, player names) are interned into a per-run table under `Run` and never share numbering with defs.
- Per-tick code does **no** string compares. Lookups are by `DefIndex` into typed tables (`content.MailKinds[kind]`). Whether tables are typed (`DefIndex<T>`) is ADR-0010's choice; the representation above is fixed either way.

### Architecture Diagram

```
EntityId (u32)   [ class:8 | counter:24 ]        0 = None; class 0x00 never allocated
                 0x01 Player 0x02 WorldItem 0x03 Construct 0x04 Vehicle
                 0x05 Enemy  0x06 Npc       0x07 Destination 0x08 Prop
                 0x09–0xEF reserved · 0xF0–0xFF tests only
ActorId  (u32)   EntityId-width; System = 0x00000001
MailId   (u32)   own counter, lower = older           PlayerSlot (u8) 0–7, default = None (wire 0xFF)
EntryId  (u32)   real: [0 | counter:31]   temp: [1 | 0000 0000 | slot:3 | ctr:20]
FlightId (u16)   wraps; SerialU16 only; never numeric compare
DefIndex (u16)   ordinal per def type; 0xFFFF none; never persisted
StringId         (ns:u8, index:u16); ns = def type | Run
```

### Key Interfaces
```csharp
namespace PerformativeMail.Sim.Primitives;
public enum EntityClass : byte { None = 0, Player = 0x01, WorldItem, Construct, Vehicle, Enemy, Npc, Destination, Prop,
                                 TestFirst = 0xF0, TestLast = 0xFF }
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    public const uint CounterMask = 0xFF_FFFFu;
    public readonly uint Value;
    public static EntityId None => default;
    public EntityClass Class => (EntityClass)(Value >> 24);
    public uint Counter => Value & 0x00FF_FFFFu;
    public static EntityId Compose(EntityClass c, uint counter) => new(((uint)c << 24) | counter);
}
public readonly struct ActorId  { public readonly uint Value; public static ActorId System => new(0x0000_0001u); }
public readonly struct MailId   : IEquatable<MailId>, IComparable<MailId> { public readonly uint Value; }
public readonly struct PlayerSlot { public const int Count = 8; public const byte NoneValue = 0xFF;
                                  private readonly byte _v; public byte Value => _v == 0 ? NoneValue : (byte)(_v - 1);
                                  public bool IsNone => _v == 0; }   // +1 storage: default = None
public readonly struct EntryId  { public readonly uint Value; public bool IsTemp => (Value & 0x8000_0000u) != 0;
    public PlayerSlot TempSlot { get; } public uint TempCounter { get; }
    public static EntryId Real(uint value); public static EntryId FromValue(uint raw);   // FromValue: malformed temp → None (asserts)
    public static EntryId Temp(PlayerSlot s, uint ctr)   // asserts !s.IsNone and ctr < 2^20; s.IsNone → None (never the unguarded formula)
        => s.IsNone ? default : new(0x8000_0000u | ((uint)s.Value << 20) | (ctr & 0xF_FFFFu)); }
public readonly struct FlightId { public readonly ushort Value; }        // compare via SerialU16 only
public readonly struct DefIndex { public const ushort NoneValue = 0xFFFF; public const ushort MaxOrdinal = 0xFFFD;
                                  private readonly ushort _v; public ushort Value => _v == 0 ? NoneValue : (ushort)(_v - 1);
                                  public bool IsNone => _v == 0; }
public enum StringNamespace : byte { None = 0, /* one per def type, assigned by ADR-0010 */ Run = 0xFF }
public readonly struct StringId { public readonly StringNamespace Ns; public readonly ushort Index; }
public static class SerialU16 { public static int Diff(ushort a, ushort b); public static bool IsNewer(ushort a, ushort b); } // + U8, U32
public static class WireNarrow { public static ushort ToU16(uint v); public static byte ToU8(uint v); }
namespace PerformativeMail.Sim.Core;
public sealed class EntityIds { public EntityIds(ITickReader tick, SimCheckRing ring, SimCounters counters,
                                    ReadOnlySpan<uint> initialCounters = default, bool allowTestClasses = false, IWorldStateReader? state = null);
                                public EntityId Allocate(EntityClass c); public uint Peek(EntityClass c); }   // Peek = last issued
public sealed class MailIds   { public MailIds(ITickReader tick, SimCheckRing ring, SimCounters counters, uint lastIssued = 0);
                                public MailId Allocate(); public uint Peek(); }                               // Peek = last issued
```
(Constructors elided; exact members are finalised in the story. The layouts, none values and rules are fixed here.)

### Implementation Guidelines
- Ids **must** be the value-type wrappers above, never bare `uint`/`ushort`, in any API that crosses a system boundary. Bare `uint` is allowed only inside a struct's own storage and in the op/event payload codecs.
- Every class shift **must** be written `(uint)cls << 24`.
- `EntityIds`/`MailIds` **must** be per-world instances injected by the composition root (ADR-0003), never static.
- Allocation **must** happen on the owner thread inside `Tick()` (world-build exception for `Destination`/`Prop`). Ids **must never** be reused, wrapped or skipped.
- Code **must** order ids by raw `Value` ascending. `GetHashCode()` **must never** be used for ordering or as a tie-break.
- Narrow wire ids **must** come from an owner-maintained id map. Truncating an `EntityId` **must never** happen.
- `FlightId` **must** be compared only through `SerialU16`. Flight processing order **must** be list (creation) order.
- Real `EntryId`s **must** keep bit 31 clear. The temp layout **must** be exactly `0x8000_0000 | slot << 20 | ctr`, `ctr < 2^20`. The behaviour at temp-counter exhaustion is ADR-0009's.
- `DefIndex` **must never** be written to `user://` files, saves or telemetry. Persist the string id.
- Test classes 0xF0–0xFF **must** be used only through `EntityIds(allowTestClasses: true)` in tests.
- Every id struct **must** implement `IEquatable<T>`, `Equals(object)`, `GetHashCode()` and `==`/`!=` explicitly:
  - single-field ids hash as `(int)Value`;
  - `StringId` hashes as `((int)Ns << 16) | Index`.
  - These are plain arithmetic overrides with no `System.HashCode`. Default `ValueType.Equals` and `GetHashCode` (which box and use reflection) **must never** be reached.
  - Per ADR-0006 rule 6, Sim code **must never** call `GetHashCode` itself or iterate a hash container. Sim dictionaries keyed by ids are lookup-only. The override exists for that, and for Presentation/Client.Core maps.
- **Checked builds** (Debug, ExportDebug, ReleaseAssert run with overflow checking; ADR-0001 §4):
  - allocators **must** test for exhaustion *before* incrementing;
  - `EntityId.Compose` **must** assert `counter ≤ 0xFF_FFFF` and mask it;
  - `DefIndex.FromValue(0xFFFF)` and `PlayerSlot.FromValue(0xFF)` **must** special-case None rather than compute `v + 1`.
- **Godot boundary:** id structs are not Variant types. They **must not** be `[Signal]` parameters, `[Export]` fields, `SetMeta` values, `Godot.Collections` elements or `Callable` binds. Presentation passes `.Value` (a u32 becomes Variant `INT`/int64 losslessly), and **must** range-check it when converting back. Wrapper structs travel no further than Presentation's C# layer.
- **Wire and persistence:** network payloads carry ids in packed bytes at their tabled width (ADR-0011). Never use Variant RPC arguments, which widen everything to 64 bits. Any JSON persistence (ADR-0024) **must** use explicit converters: `System.Text.Json` ignores fields and would write `{}`.

## Alternatives Considered

### Alternative 1: One global 32-bit (or 64-bit) entity counter, class stored separately
- **Description**: `EntityId` = a global counter; the class lives in a side table.
- **Pros**: Simpler allocator; 64-bit never exhausts.
- **Cons**: Class decoding needs a lookup; spec 08's literal ids (16 777 217) no longer hold. 64-bit doubles every replicated id's bytes.
- **Rejection Reason**: Class-in-id is cheap, spec-aligned and wire-compact. F4 shows 24-bit counters suffice.

### Alternative 2: Reuse freed entity ids (free lists / generation counters)
- **Description**: `index:20 | generation:12` handles, as in ECS frameworks.
- **Pros**: Dense arrays; bounded id range.
- **Cons**: Stale-handle bugs across the wire and in replays. Tie-breaks no longer equal creation order. Rejoin and history become ambiguous.
- **Rejection Reason**: The GDDs require never-reused ids and creation-order tie-breaks (R20).

### Alternative 3: Temp entry ids as negative numbers or a separate type
- **Description**: Client temp ids in a disjoint `TempEntryId` type.
- **Pros**: The type system separates them.
- **Cons**: Every op payload that names an entry would need a union. Grid's GDD already fixed the bit-31 namespace on the wire.
- **Rejection Reason**: The bit-31 layout matches Grid Inventory's contract with one field; `IsTemp` makes it explicit.

### Alternative 4: `DefIndex` default 0 = first def
- **Description**: A plain `ushort` with 0xFFFF none, where `default` = ordinal 0.
- **Pros**: Simplest storage.
- **Cons**: A zero-initialised struct field would silently mean "the first def", which is an undetectable bug.
- **Rejection Reason**: Content & Data wants "a ushort struct whose default is None". The `+1` storage gives that while keeping 0xFFFF as the external sentinel.

## Consequences

### Positive
- One table answers width, none, wrap and owner for every id. Systems cannot drift.
- Ids are value types, so the tick stays 0-alloc and ordering is deterministic.
- The spec 08 numeric examples hold (Player 1 = 16 777 217; Construct 1 = 50 331 649).

### Negative
- Wrapper structs add ceremony at payload codecs.
- Reserved classes need an ADR amendment to use, by design.
- `DefIndex`'s `+1` storage means the raw field differs from `Value` (must be hidden behind the struct).

## Risks
- **Free Play / very long runs** could approach per-class limits. *Mitigation:* F4 re-check is listed for Free Play; the exhaustion path is safe (refuse the spawn, record).
- **Temp-id counter (2^20 per connection per run).** About a million optimistic splits; unreachable in a < 1 h run. *Mitigation:* ADR-0009 defines a fallback to non-optimistic ops on exhaustion.
- **`ReqId` width disagreement** (Sim u32 vs Grid wire u16; Q-ARCH-04). *Mitigation:* resolved in ADR-0007/0011; this ADR only fixes the Sim slot as an opaque u32.
- **Engine validation (godot-specialist, 2026-10-16): 0 blocking, 7 minor.** Folded in:
  - explicit equality and hashing on every id (default `StringId` hashing would collide per namespace);
  - checked-build overflow traps (exhaustion test before increment, `Compose` mask, None special cases);
  - `PlayerSlot` moved to `+1` storage, so `default` is None, consistent with Alternative 4;
  - the Variant/signal/`[Export]` boundary rule; packed-byte wire ids; JSON converters.
- **Cross-ADR conflict: `DefIndex` shape in ADR-0010.** ADR-0010's Key Interfaces declare `readonly record struct DefIndex(ushort Value)` with `None = new(0xFFFF)`. There `default(DefIndex)` is ordinal 0 (the first def), which contradicts §6 and the C&D GDD ("a ushort struct whose default is None"). TR-content-007's primary ADR is this one, so §6 governs. *Resolution:* ADR-0010 adopts the §6 representation (`+1` storage, `Value` reads `0xFFFF` for None). The external `0xFFFF` sentinel is unchanged, so ADR-0010's wire, qualifier and validator rules are unaffected. Logged under the planning run's residual conflicts. **Resolved 2026-10-16, before acceptance:** ADR-0010 §3 and Key Interfaces now use this struct.
- **Presentation `Dictionary<EntityId, Node>`** can hold freed nodes. *Mitigation (ADR-0019):* remove the entry on the despawn event and guard lookups with `IsInstanceValid`.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R19 / TR-sim-025: `EntityId` u32 class << 24 \| counter; classes 01–08; 09–EF reserved; F0–FF tests; never reused | §1, §2, §3 |
| simulation-core.md | R19/R21 / TR-sim-026: `mailId` separate u32; `PlayerSlot` u8 0–7; slot freed in T reusable from T+1 | §1, §4 |
| simulation-core.md | R6: `ActorId` EntityId-width; `System` = 0x00000001; stamped from the peer | §1, §2 |
| simulation-core.md | R20: allocation thread/phase, initial counters, exhaustion, tie-breaks | §3 |
| simulation-core.md | R22: system-local ids, `Serial*`, `WireNarrow`, narrow ids from maps | §1, §5 |
| content-data-definitions.md | TR-content-007: def id regex, ordinal sort into `DefIndex` ushort, 0xFFFF none | §6 |
| content-data-definitions.md | TR-content-008: `DefIndex` never persisted; on the wire only after a hash match | §6 |
| content-data-definitions.md | TR-content-009: `StringId` = (namespace, `DefIndex`); run strings interned per run | §6 |
| grid-inventory.md | TR-inventory-003 / R2: u32 `entryId` run-unique, never reused; ascending `mailIds`; temp ids `0x8000_0000 \| slot << 20 \| counter`, per-connection namespace reset on rejoin | §1 (`EntryId`), Key Interfaces |
| throwing-catching.md | TR-throwing-009: `flightId` u16 wrap-safe, creation-order processing, never compared numerically | §1, §5 |
| mail-destinations.md | lower `mailId` = older; allocated in manifest order at the batch tick | §1, §3 |

**GDD impact (no GDD edited):**
- Consistent with all five GDDs.
- One clarification for Content & Data's re-review: its "a `ushort` struct whose default is None" and "0xFFFF is the single none sentinel" are both honoured through the `+1` storage (§6). The sentinel is the external value, not the raw field.
- Sim Core R19 table names (`world_item`, …) map to PascalCase enum members (`WorldItem`, …), per the naming convention. This is not a semantic change.
GDD sync: checked 5 referenced GDDs; no renamed interfaces beyond casing.

## Performance Implications
- **CPU**: Allocation is an array increment plus a branch. Comparisons are a `uint` compare.
- **Memory**: `uint[256]` per world for entity counters; `SlotTable` 8 entries.
- **Load Time**: `DefIndex` assignment is part of the content load sort (ADR-0010).
- **Network**: Ids are u32/u16/u8 as tabled. `PlayerSlot` and `FlightId` keep hot packets compact.

## Migration Plan
New code. The Sim Core "ids" story adds the structs, `EntityIds`, `MailIds`, `SlotTable`, `Serial*` and `WireNarrow`, with tests (spec 08 examples, exhaustion via initial counters, slot T+1 reuse, the RFC 1982 half-range assert, temp-id layout).

## Validation Criteria
- `EntityId.Compose(Player, 1).Value == 16_777_217`; `Compose(Construct, 1).Value == 50_331_649`; decoding round-trips for all classes, including 0xF0 (no sign issue).
- Exhaustion: `EntityIds` built at counter 2^24−1 returns `None` and writes one check record; `MailIds` at 2^32−1 returns 0.
- Reserved class 0x09 → `None` + record; test class 0xF0 is refused unless `allowTestClasses`.
- Slot freed at T: `PlayerJoin` at T → `slot_busy`; at T+1 → accepted.
- `SerialU16`: `IsNewer(1, 65535)` true; half-range asserts.
- `DefIndex`: `default` → `IsNone`, `Value == 0xFFFF`; ordinal 65 533 round-trips. `PlayerSlot`: `default` → `IsNone`, `Value == 0xFF`. A Debug-build allocator at the exhaustion boundary returns None without an `OverflowException`.
- Architecture test: no `DefIndex` field in any type marked as persisted (ADR-0024 marker), once ADR-0024 exists.

## Related
- ADR-0001 (`Primitives/`, `SimCheckRing`), ADR-0003 (injection of `EntityIds`/`MailIds`), ADR-0005 (stream index by id), ADR-0006 (ordering rules, dump encoding), ADR-0007 (`ActorId`, `opSeq`, `ReqId` width), ADR-0009 (`EntryId` minting/retiring, temp ids), ADR-0010 (`DefIndex` assignment, typed tables), ADR-0011 (slot ↔ peer, hash gate), ADR-0021 (`FlightId`, receiver map), ADR-0024 (persist strings).
- `design/gdd/simulation-core.md` R6, R19–R22, F4; `grid-inventory.md` R2, Network; `content-data-definitions.md`; `throwing-catching.md`.

## Errata
- 2026-10-17 — §3 and Key Interfaces: allocators were described as holding "next" counters (`MailIds(uint initial = 1)`, `Peek` = next id); now they hold the **last issued** id (0 = none; `MailIds(…, uint lastIssued = 0)`; `Peek` = last issued), matching `EntityIds`, `MailIds` and `SimInitialCounters` (`LastOpSeq`, `LastMailId`, `LastEntityCounters`); source TD-043 item 1.
- 2026-10-17 — Key Interfaces: the `EntryId.Temp` sketch lacked the None-slot guard (an unguarded formula would corrupt reserved bits) and the sketch omitted `EntryId.Real/FromValue/TempSlot/TempCounter`, `PlayerSlot.Count/NoneValue`, `DefIndex.NoneValue/MaxOrdinal` and `EntityId.CounterMask`; `EntityId.None` and `ActorId.System` were `static readonly` fields and are properties; now the sketch shows the shipped members and guard; source TD-004.
- 2026-10-23 — §6 `StringNamespace`: adds **`Reason = 6`**, the namespace for framework and owner UI reason and prompt keys (`reason.*`, `deliver.*`, `throw.*`, `catch.reason.*`), alongside the per-def-type values 1–5 and `Run = 0xFF`. It is the option A2 the player-character 013 dev-story recorded (decision log 2026-10-17) and manual-delivery 010 AC-4 lands it in `src/PerformativeMail.Sim/Primitives/StringId.cs`. The content string table and key registration path stay with TD-119. Approved by Evan, Mail Lead chat, 2026-10-23 9:08 AM PT: "Approve the E1 label fix, the ADR-0018 glyph-source fix, the ADR-0004 Reason enum erratum, and the new planning defaults" (decision log 2026-10-23).
