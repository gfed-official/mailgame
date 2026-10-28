# ADR-0007: Op Queue, Drain and Replies

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
- Author: `/architecture-decision` (autonomous run, `--review lean`), on behalf of technical-director.
- Inputs: Simulation Core GDD (R6–R10, F8), the TD seed §1 and §3.1, Grid Inventory (R7, Network rule 8, H-NET-1), Manual Delivery (MD5, M-NET-2), and the `create-architecture` decision of 2026-10-16T20:59:45Z (reqId resolution [A]).
- Engine validation: godot-specialist (Step 5.5). TD-ADR skipped (lean mode).
- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Every player and `System` request reaches the Sim as a fixed 64-byte blittable `Op` through one preallocated ring. The ring is drained in phase 0, at most 64 per tick, in `opSeq` order. Handlers validate before mutating, and each handler's reply is appended to the event log after its events.

The ADR also settles the `reqId` conflict:
- the Sim header keeps an opaque **u32** `reqId` slot;
- the wire uses **u16** per connection (Grid rule 8);
- duplicate detection and replay of the last 16 replies live in the **Godot-side network adapter**;
- the Sim never deduplicates.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Core and Networking boundary. The Sim side is Godot-free. |
| **Knowledge Risk** | LOW for the Sim queue (plain .NET structs and arrays). **HIGH** for the adapter half (ADR-0011): Godot 4.4–4.7 high-level multiplayer behaviour (RPC thread, ordering, `GetRemoteSenderId`) is NOT SOURCEABLE (VERSION.md). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md` (multiplayer NOT SOURCEABLE), `breaking-changes.md` (4.4→4.5 `Node.get_rpc_config` → `get_node_rpc_config`, C#-compatible; 4.5→4.6 `StreamPeerTCP` → `StreamPeerSocket`), `deprecated-apis.md` (`JSONRPC.set_scope`). None is used here. |
| **Post-Cutoff APIs Used** | None in the Sim. The adapter's transport APIs are ADR-0011's and are spike-gated. |
| **Verification Required** | (1) `Unsafe.SizeOf<Op>() == 64`, and `Unsafe.SizeOf<T>() ≤ 48` for every registered payload, in Debug and Release. (2) The M0 multiplayer spike confirms transport callbacks can be marshalled to enqueue on the main thread (ADR-0002/0011; Q-ARCH-05). (3) The adapter dedupe is tested over loopback at M-1a and over ENet at M0. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (phase 0 `Inputs.Apply`, the builder binding of `IOpHandler`s and op descriptors, `TickContext`). ADR-0004 (`ActorId` = `EntityId` width, `System` = `0x00000001`, `PlayerSlot`). ADR-0006 (the `IOpRecorder` hooks, overflow classes, zero-initialised payloads, the dump of queued ops). |
| **Enables** | ADR-0008 (the `OpReply` entry), ADR-0009 (InventoryOp kinds and reason codes ≥ 64), ADR-0011 (the adapter: wire codec per `OpKind` descriptor, `busy`, dedupe cache), ADR-0014, ADR-0017, ADR-0021, ADR-0022. |
| **Blocks** | The Sim Core "op ring, drain and replies" story (TR-sim-015..018, 020). The Grid Inventory op handler stories (TR-inventory-025). The Manual Delivery request story (TR-delivery-011). |
| **Ordering Note** | Accept ADR-0003, ADR-0004 and ADR-0006 first. ADR-0011 must adopt §6 (the adapter dedupe) verbatim. |

## Context

### Problem Statement
Two reviewed GDDs disagree on the `reqId` width and on who deduplicates:
- **Sim Core** (R6, R10): the `reqId` is opaque in a u32 header, and the Sim has no generic dedupe; a handler promising idempotence owns that state (HB1(i) puts Grid's cache in its dump contributor).
- **Grid Inventory rule 8:** the `reqId` is a u16 per connection, wrapping, and the host "network adapter, keyed `(peer, reqId)`" keeps the last 16 replies and answers duplicates with the original reply.
- **Manual Delivery** (MD5, M-NET-2) relies on Grid's promise.

The planning run logged this as an owner-level residual conflict. The architecture blueprint recommends resolution [A] (Q-ARCH-04).

Separately, the seed's verbatim op section has known defects the ADR must not copy (architecture §ADR Audit):
- no drain cap;
- `busy` sent outside the event log with no channel rule;
- a payload union that would make `Sim/Core` reference every system's payload types;
- no way for the Sim to detect a payload whose length does not match its descriptor, which SC-R8-3 requires.

### Constraints
- **Peers.** The Sim knows no peers, accounts or channels (Sim Core N1). Only `PlayerSlot` and `playerId` exist inside it.
- **Thread and timing.** The Sim is main-thread only. Transport callbacks only enqueue (ADR-0002, R18).
- **Allocation.** Zero allocation per tick (ADR-0006 §8). The ring and per-op storage are preallocated.
- **Atomicity.** A reject mutates nothing, allocates no id and draws no RNG (R8).
- **Wire budget.** Manual Delivery's request is 12–16 B with a u16 `reqId`.

### Requirements
- **TR-sim-015.** Op = 64 B blittable: a 16 B header (`opSeq`, `actor`, `kind`, `reqId`) plus a payload of at most 48 B.
- **TR-sim-016.** `actor` is stamped by the adapter from the peer. `System` = `0x00000001`. `opSeq` is monotonic and never reused.
- **TR-sim-017.** Ring of 4096 (a power of two). Full → `false`, no `opSeq` consumed, the adapter replies `busy`. `systemOpReserve` 64.
- **TR-sim-018.** Drain at most 64 ops per tick by `opSeq`. Validate fully before any mutation. Record every drained op.
- **TR-sim-020.** Reply `{reqId, result, reason u16, extra ≤ 24 B}` after the handler's events. Reasons 1–63 belong to Sim Core. No `reqId` dedupe.
- **TR-inventory-025.** The sender comes from the transport peer, never from a client-supplied `playerId`. A malformed request gets `bad_request` and throws no exception.
- **TR-delivery-011.** Idempotent u16 `reqId`: a duplicate returns the original reply.

## Decision

### 1. The `Op` struct (64 B, blittable)

```
offset size field            notes
0      4    OpSeq  u32       stamped by SimWorld.EnqueueOp; 1..2^32−1; never reused or wrapped
4      4    Actor  u32       ActorId (EntityId width); written by EnqueueOp from its actor parameter
8      2    Kind   u16       OpKind
10     1    PayloadLength u8 bytes of Payload actually used (0..48); set by the adapter codec
11     1    Reserved u8      must be 0
12     4    ReqId  u32       opaque; echoed in the reply; never interpreted by the Sim
16     48   Payload          [InlineArray(48)] byte buffer; bytes ≥ PayloadLength are zero
```

- **Header field.** The seed's 2-byte padding becomes `PayloadLength` plus a zero `Reserved` byte. This lets the drain reject a payload whose length does not match its descriptor (SC-R8-3) without a codec in the Sim.
- **Payload buffer.** The payload is an **opaque 48-byte inline array**, not an explicit-layout union of every payload type. A union would force `Sim/Core` to reference every system's payload types and break the layer rule (architecture §Module Ownership).
  - Each system defines its own `unmanaged` payload struct and reads it with `op.PayloadAs<T>()`, which is `MemoryMarshal.Read<T>` / `Unsafe.ReadUnaligned<T>` over the buffer, **never `Unsafe.As`**. The payload sits at offset 16 and is only 4-byte aligned.
  - Writes go through `Unsafe.WriteUnaligned` after zero-filling the buffer.
  - **Payload structs must be padding-free.** The descriptor test asserts the sum of field sizes equals `Unsafe.SizeOf<T>()`. Otherwise padding bytes are not guaranteed zero, and the dumped 48 bytes and replays could differ. This rule came from engine validation.
  - A descriptor test asserts `Unsafe.SizeOf<T>() ≤ 48` and `== descriptor.PayloadLength`.
  - `Int128` and references are forbidden in payloads (16 B alignment, and not blittable).
- **Zeroing.** Unused payload bytes and `Reserved` **must** be zero. The adapter codec zero-initialises the whole struct before decoding, so the dump (ADR-0006 §4) and replays are canonical.

### 2. The ring and `EnqueueOp`

- **Storage.** One preallocated ring, `Op[opQueueCapacity]` (default 4096, a power of two), plus a parallel `uint[]` of **enqueue boundary ticks** (the value of `CurrentTick` when the op was enqueued, i.e. the first tick that could apply it). The boundary ticks are dumped (ADR-0006) and recorded (`IOpRecorder.OnEnqueued`).
- **Signature.** `bool SimWorld.EnqueueOp(ActorId actor, in Op op)` copies the op into the ring and **overwrites** `Actor` with the parameter and `OpSeq` with the next value. The adapter derives `actor` from the transport peer through its peer → `playerId` map, never from a payload field (TR-inventory-025). The loopback adapter passes the host player's id.
- **Refusals.** `EnqueueOp` returns `false`, consumes no `opSeq`, records nothing and counts the reason in these cases:
  - `count ≥ opQueueCapacity − systemOpReserve` and `actor ≠ System` → `busyPlayer`;
  - `count == opQueueCapacity` → `busySystem`, plus one always-on record (R31), because a lost `System` op is a bug;
  - `opSeq` exhausted (R20) → one record;
  - the world is not `Ready` or `Running` → `enqueueBadState`.
- **Thread.** `EnqueueOp` asserts the owner thread in Debug (ADR-0002).
- **`busy`.** On `false`, the **adapter** answers the requester at once with a reply carrying the Sim Core reason `busy` (1). It sends it on the **same reliable channel and stream as that peer's replies** (ADR-0011), matched by `reqId`. A `busy` never enters the event log, because the op never entered the Sim.

### 3. The drain (phase 0, `Inputs.Apply`)

At the start of phase 0, the queue captures `n = min(count, maxOpsDrainedPerTick)`. `maxOpsDrainedPerTick` is in `SimSettings`: default **64**, safe range 32–256, and it is in the replay header. The queue then applies those `n` ops in ascending `opSeq` (ring order). For each op:

1. **Pre-checks by Sim Core**, in this order. The first failure produces a Core-reason reply, and no handler runs.
   - `Kind` is not registered → `unknown_kind` (3).
   - `PayloadLength` ≠ the descriptor's length, or `Reserved` ≠ 0 → `unknown_kind` (3). This matches GDD R8: a wrong payload is the same failure as an unregistered kind.
   - `Actor` is not `System` and not a present player (via the players table, ADR-0004) → `unknown_actor` (2).
   - The descriptor declares the op `SystemOnly` (for example `PlayerJoin`, `PlayerLeave`, `FreezePlayer`) and `Actor ≠ System` → `unknown_actor` (2). A client must never be able to forge a `System` op.
2. **Handler call.** Sim Core opens an event call scope (ADR-0008 §5) and calls `Reply IOpHandler.Apply(in Op op, in TickContext ctx)`.
   - The handler **validates fully before** its first mutation, id allocation or RNG draw.
   - It raises events only on commit.
   - It returns a `Reply`.
   - A reject mutates nothing and raises nothing.
3. **Reply append.** Sim Core appends the reply to the event log as kind `OpReply` **after every event raised during that call** (ADR-0008). It then calls `IOpRecorder.OnDrained(applyTick, op, reply)` if a recorder is attached. Sim Core guarantees its own part (`opSeq` consumed, recorder called, reply appended) for **every** drained op, accepted or rejected.

**After the drain:**
- Ops beyond the cap stay queued in order. `drainBacklog` counts how many remain.
- `System` ops get **no bypass**: they keep their `opSeq` position. Their protection is the enqueue reserve, not the drain.
- Ops enqueued during the drain, or between ticks, apply on a later tick (SC-R8-1).

**Composite actions** are one `OpKind` whose handler calls documented `I…Api` methods in a fixed order (a throw; Player Character P13 then `deliver_matching`). Clients never rely on two ops applying together.

**Only requests use the FIFO.** Automation, expiry, landings, `Mail.Step` and `Combat.React` call synchronous `I…Api` methods in their own phase, with no reply (Sim Core R9, ADR-0003). They never enqueue ops.

### 4. `OpKind` descriptors

- **Numbering.** `OpKind` is a `u16` enum in `Sim/Core`. It is append-only, explicit values only, and 0 is invalid. Values are allocated in per-system blocks, so a new system never renumbers another:

  | Range | Owner |
  |---|---|
  | `0x0001`–`0x003F` | Sim Core (`PlayerJoin`, `PlayerLeave`, `FreezePlayer`, `TeleportAck`) |
  | `0x0040`–`0x007F` | Players and movement |
  | `0x0080`–`0x00FF` | Inventory |
  | `0x0100`–`0x013F` | Mail and Delivery |
  | `0x0140`–`0x017F` | Throwing |
  | `0x0180`–`0x01BF` | Economy (`OpKindBlock.Economy`; amendment 2026-10-28 — first kind `WalletChanged` 0x0180, mail-destinations 005) |
  | `0x01C0`+ | Later systems, 64-wide blocks, allocated by amending this table |

- **Descriptor contents.** Each kind's descriptor (`OpDescriptor`, registered at build by the owning system, ADR-0003) declares:
  - `PayloadLength`;
  - the `unmanaged` payload type;
  - `SystemOnly`;
  - the reason codes it may return (name and value);
  - its **wire codec id and wire size**, used by ADR-0011's codec.
- **Protocol check.** A hash of the descriptor table is the protocol-version check (Sim Core N4). Two builds with different tables refuse to connect (ADR-0011).
- **Build errors.** A second handler for one kind, or a kind with no handler, throws `SimBuildException` (SC-R4-1, ADR-0003).

### 5. Replies and the reason namespace

```
Reply (value type returned by a handler; serialised into the OpReply entry payload by ADR-0008):
  ReqId   u32   echoed unchanged from the op
  Kind    u16   the OpKind replied to (reason codes are keyed by (Kind, Reason))
  Reason  u16   0 = ok; 1–63 Sim Core (global, every kind); ≥ 64 per-kind system codes
  Result  u8    Ok = 0, Rejected = 1   (Ok ⇔ Reason == 0, asserted)
  _pad    3 B   zero
  Extra   24 B  [InlineArray(24)], zero-filled; per-kind meaning (e.g. Grid resultVersion ×2 + byActorId)
  = 36 B ≤ the 48 B event payload ceiling
```

- **Partial results.** A "partial ok" (Grid `take_all` with `requested`/`got`, `unchanged:true`) is `Ok` with the details in `Extra`, never a reason.

**Sim Core reason codes (1–63).** These are global: every client decodes them identically for every kind.

| Code | Name | Produced by |
|---|---|---|
| 1 | `busy` | The adapter, on `EnqueueOp == false` (§2). Never by the Sim. |
| 2 | `unknown_actor` | The drain pre-check (§3) |
| 3 | `unknown_kind` | The drain pre-check: unregistered kind, or wrong payload length or reserved byte |
| 4 | `slot_invalid` | The `PlayerJoin` handler (R21) |
| 5 | `slot_busy` | The `PlayerJoin` handler (R21) |
| 6 | `duplicate_player` | The `PlayerJoin` handler (R21) |
| 7 | `join_closed` | The `PlayerJoin` handler (R21) |
| 8 | `bad_request` | The adapter codec (an undecodable or out-of-range wire frame, so the op never enters the Sim), **or** any handler for a request that is structurally invalid for its kind (TR-inventory-025, Grid's `bad_request` row) |
| 9–63 | reserved | Amendment to this table only |

- **System codes.** Each system's codes start at **64** and are keyed by `(Kind, Reason)`, so Inventory and Mail may both use 64+. They are declared in the descriptor. A unit test enumerates every descriptor and asserts that no system code is below 64.
- **Routing.** A reply is `OwnerOnly` to the actor's slot (ADR-0008). A reply to `System` has an empty audience and never leaves the process.

> **As implemented (2026-10-16, network-adapter-loopback story-004):** a successful `PlayerJoin` reply carries the allocated player id in `Extra[0..4)` (u32 LE), so the System op's issuer (the network adapter) learns the id from the reply delivered through `IEventSink.Deliver`; rejections carry no extra. The `OpReply` entry's `CallSeq` is the handler's call scope (0 for a pre-check rejection; ADR-0008 §1 note).

### 6. `reqId`: width, ownership and deduplication (resolves the reqId-width conflict; Q-ARCH-04)

- **In the Sim.** `Op.ReqId` is a **u32 slot**, opaque and echoed unchanged in the reply. The Sim never compares, orders or deduplicates `reqId`s, and it holds **no dedupe state**. Nothing about `reqId` is in a handler's dump contributor.
- **On the wire.** `reqId` is **u16 per connection**, wrapping (Grid rule 8; `pendingQueueMax` 16 keeps live ids distinct). The adapter **zero-extends** it into the u32 slot on decode and **truncates** the echoed value back to u16 on encode. The loopback adapter uses the same codec, so host and remote paths are identical. The upper 16 bits are reserved: 0 on every path today.
- **Deduplication** lives **in the Godot-side network adapter** (ADR-0011), keyed `(peer, reqId u16)`. Per connection the adapter keeps:
  - a **reply cache of the last 16 replies** (`reqId` → the encoded reply bytes, copied from `Deliver`);
  - an **in-flight set** of `reqId`s it has enqueued whose reply has not yet been delivered.

  On receiving a request:

  | Case | Action |
  |---|---|
  | `reqId` is in the reply cache | Resend the cached **original reply**. Enqueue nothing. No `opSeq` is consumed, so the op log stays free of duplicates. |
  | `reqId` is in the in-flight set | Drop it silently. The original reply will follow. |
  | Otherwise | Decode, `EnqueueOp`, and add it to the in-flight set. On `false`, reply `busy`, and do not cache it: a later retry is a new attempt. |

  - **Reset.** The cache, the in-flight set and the expected `reqId` counter reset on rejoin (Grid rule 8, Grid H-NET-4).
  - **Ownership.** The cache is adapter memory, outside the Sim dump. A Sim handler never implements `reqId` idempotence.
- **Why the adapter.**
  - Duplicate detection needs the peer identity, which the Sim must not know (N1).
  - Answering a duplicate before `EnqueueOp` means a duplicate never reaches a handler, never consumes `opSeq` and never perturbs T1 replays.
  - It satisfies Grid rule 8 and Manual Delivery M-NET-2 ("returns the original reply and consumes nothing more") exactly.
- **Defence in depth.** Networking never resends an op the host may have drained (HB10). Duplicates are therefore a transport anomaly, not a protocol path.

### Architecture Diagram

```
 Godot side (ADR-0011)                          │  PerformativeMail.Sim (Godot-free)
 transport callback ─► decode (codec per kind,  │
   zero-init, PayloadLength, reqId u16→u32)     │
   ├─ dedupe (peer,reqId): cache16 / in-flight ─┼─► (duplicate answered here; nothing enqueued)
   └─ EnqueueOp(actor from peer, in op) ────────┼─► ring[4096] (+boundary tick) ── false ─► adapter replies busy(1)
                                                │        │ phase 0: drain min(count, 64) by opSeq
                                                │        ▼
                                                │   pre-check (kind, length, actor, SystemOnly) ─► Core reject reply
                                                │   IOpHandler.Apply (validate → mutate → raise) ─► Reply
                                                │   EventLog: [events of this call…] then OpReply ; IOpRecorder.OnDrained
 Deliver copy ◄── phase 13 (ADR-0008) ◄─────────┤
   └─ reply cache update (per peer, last 16)    │
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.Core;
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Op {
    public uint OpSeq; public uint Actor; public ushort Kind; public byte PayloadLength; public byte Reserved;
    public uint ReqId; public OpPayload Payload;                       // OpPayload = [InlineArray(48)] struct of byte
    public readonly T PayloadAs<T>() where T : unmanaged;              // MemoryMarshal.Read (unaligned); asserts sizeof(T) == PayloadLength
    public void SetPayload<T>(in T value) where T : unmanaged;         // zero-fills the tail
}
public enum OpKind : ushort { None = 0, PlayerJoin = 0x0001, PlayerLeave = 0x0002, FreezePlayer = 0x0003, TeleportAck = 0x0004 /* … per-system blocks */ }
public enum ReplyResult : byte { Ok = 0, Rejected = 1 }
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reply { public uint ReqId; public ushort Kind; public ushort Reason; public ReplyResult Result;
    private byte _pad0, _pad1, _pad2;                                  // explicit, zero; Extra at offset 12
    public ReplyExtra Extra; }                                         // Extra = [InlineArray(24)]; SizeOf == 36 (asserted)
public static class CoreReason { public const ushort Ok = 0, Busy = 1, UnknownActor = 2, UnknownKind = 3, SlotInvalid = 4,
    SlotBusy = 5, DuplicatePlayer = 6, JoinClosed = 7, BadRequest = 8, FirstSystemReason = 64; }
public interface IOpHandler { OpKind Kind { get; } Reply Apply(in Op op, in TickContext ctx); }
public sealed record OpDescriptor(OpKind Kind, byte PayloadLength, bool SystemOnly, ushort WireCodecId, byte WireSize, ReasonDef[] Reasons);
// SimWorld:
public bool EnqueueOp(ActorId actor, in Op op);                         // false: busy / bad state / exhausted; no opSeq consumed
```
`TickContext` is defined by ADR-0003. It carries `CurrentTick`, the `EventLog` writer (ADR-0008) and counters.

### Implementation Guidelines
- The Sim **must never** read the actor from a payload. `EnqueueOp` overwrites `Op.Actor` from its parameter.
- The adapter **must** zero-initialise every `Op` before decoding, and **must** set `PayloadLength` from the descriptor.
- A handler **must** perform all validation before its first mutation, id allocation, RNG draw or event raise. A reject **must** leave state, allocators and RNG untouched (SC-R8-2 harness per handler).
- A handler **must** return exactly one `Reply`. It **must never** append its own `OpReply` or reorder the log.
- Handler code **must never** deduplicate or compare `reqId`s, and **must never** store them in dumped state.
- A `System`-only kind **must** be declared `SystemOnly`.
- Reason codes below 64 **must** come from `CoreReason`. System codes **must** be at least 64 and declared in the kind's descriptor.
- In-tick system callers **must** use `I…Api` calls, **never** `EnqueueOp`.
- The ring, boundary-tick array and drain loop **must** allocate nothing after construction.

## Alternatives Considered

### Alternative 1: Dedupe inside the Sim (a per-handler cache in the dump; Sim Core HB1(i))
- **Description:** Grid's handler keeps a `(slot, reqId)` → reply cache as dumped handler state.
- **Pros:** The duplicate behaviour is replayable and part of T1.
- **Cons:**
  - The Sim would need connection identity and rejoin resets: `PlayerSlot` survives reconnect while Grid resets the cache on rejoin, so the Sim would need a "connection epoch" input.
  - Every duplicate would consume an `opSeq` and appear in the op log.
  - Each idempotent system would re-implement it.
  - The cache would bloat the dump.
- **Rejection Reason:** It contradicts N1 (the Sim knows no peers). The adapter gives the same player-visible guarantee with none of these costs.

### Alternative 2: u32 `reqId` on the wire
- **Description:** Make the wire width match the header.
- **Pros:** No widening or narrowing.
- **Cons:** 2 extra bytes on every request. It breaks Grid rule 8 and Manual Delivery's 12/16 B layout. Live ids are already bounded by `pendingQueueMax` 16.
- **Rejection Reason:** It wastes bandwidth with no benefit.

### Alternative 3: Explicit-layout union payload in `Sim/Core` (seed §3.1)
- **Description:** One `[StructLayout(Explicit)]` union with a field per payload type.
- **Pros:** Typed access without `Unsafe`.
- **Cons:** `Sim/Core` would reference every system's payload types, inverting the dependency direction. Adding a system edits Core.
- **Rejection Reason:** It violates the module ownership rules. An opaque inline buffer plus a descriptor size test gives the same safety.

### Alternative 4: No drain cap (seed §3.1)
- **Description:** Drain everything queued.
- **Pros:** Lowest latency under load.
- **Cons:** A burst (up to 4 096 ops) lands in one tick. That blows the tick budget and makes event-log overflow reachable (F8 relies on cap × 4 entries).
- **Rejection Reason:** The review rejected it explicitly (architecture §ADR Audit, "op queue without a drain cap").

### Alternative 5: `busy` as an event-log entry
- **Description:** Enqueue a synthetic `busy` reply into the log.
- **Pros:** One reply path.
- **Cons:** The op never entered the Sim, so there is no tick to attribute it to. It would delay the reply to the next flush, and it would mutate the log from outside a tick (R14 forbids that).
- **Rejection Reason:** The adapter answers at once on the same reply stream (GDD R7).

## Consequences

### Positive
- **reqId resolved.** The `reqId` conflict is settled with no change to any GDD's player-visible behaviour. Grid rule 8, Manual Delivery MD5 and M-NET-2 hold as written.
- **Clean op log.** It contains only real requests, and T1 replays are free of transport artefacts.
- **Ordering by construction.** A fixed 64 B op, a preallocated ring and an `opSeq`-ordered capped drain give deterministic order, a bounded per-tick cost and no allocation.
- **No impersonation.** Actor stamping by parameter, plus `SystemOnly`, means a client cannot pose as another player or as `System`.
- **Unified reasons.** Core reasons are global, so a client decodes `busy` and `bad_request` identically for every kind.

### Negative
- **Adapter state.** The adapter now holds protocol state (a 16-reply cache plus an in-flight set per peer). It must be specified and tested in ADR-0011, and loopback must exercise it.
- **No replay of duplicates.** Duplicate handling is outside T1 replay. A duplicate bug reproduces only in adapter tests, not in Sim replays.
- **Field change.** The seed's padding field becomes `PayloadLength` + `Reserved`. This is a small change, but any early prototype code would need to follow it.
- **Hidden cost of Core rejects.** A `PayloadLength` mismatch is folded into `unknown_kind`, matching the GDD, which is less precise for debugging. A `SimCounters` entry (`opBadLength`) separates them.

## Risks
- **Risk: Godot multiplayer delivers callbacks off the main thread, or reorders across methods** (NOT SOURCEABLE; Q-ARCH-05). *Mitigation:* the adapter marshals to the main thread before `EnqueueOp` (ADR-0002, ADR-0011). The M0 multiplayer spike verifies this. The Sim asserts the owner thread.
- **Risk: the in-flight set grows if replies are lost** (they cannot be, on a reliable channel; a resync handles a timeout). *Mitigation:* bound it at 16 per peer (`pendingQueueMax`). On overflow, evict the oldest and count it. Reset on rejoin.
- **Risk: `opSeq` exhaustion** (2^32 ops). It is unreachable in a run: about 4 × 10^7 s at the cap. `EnqueueOp` returns `false` and records it (R20).
- **Risk: a descriptor's `PayloadLength` drifts from its struct size.** *Mitigation:* the enumeration test over all descriptors runs in Debug and Release.
- **Risk: inline-array layout differs on some runtime.** *Mitigation:* `Unsafe.SizeOf<Op>() == 64` and `Unsafe.SizeOf<Reply>() == 36` are asserted on both the net8.0 and net10.0 test targets. The Godot export runtime is checked in the ADR-0001 spike. Payload access uses unaligned reads and writes only.
- **Risk: engine auto-poll timing of the multiplayer peer delivers requests at an unpredictable point in the frame** (NOT SOURCEABLE). *Mitigation (engine-specialist recommendation for ADR-0011):* the adapter polls the transport itself from `FrameRunner`, before `Tick()`, rather than relying on engine auto-poll. If ADR-0011 uses raw TCP, breaking-changes.md 4.5→4.6 applies (`StreamPeerTCP` → `StreamPeerSocket`, `SocketServer`).

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
- **Verdict:** no blocking findings. `[InlineArray(48)]` inside a sequential struct gives `SizeOf<Op>() == 64`.
- **Minor findings, applied:**
  - padding-free payload structs;
  - unaligned `MemoryMarshal.Read` / `Unsafe.ReadUnaligned` instead of `Unsafe.As`;
  - explicit `Reply` padding;
  - transport polling from `FrameRunner`;
  - the breaking-changes.md 4.5→4.6 networking rows noted for ADR-0011.
- **Not assessed:** multiplayer thread and ordering behaviour (not sourceable; M0 spike).
- **TD-ADR:** skipped (lean mode).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R6 op shape, actor stamping, `opSeq`, opaque `reqId` | §1, §2, §6 (TR-sim-015, 016) |
| simulation-core.md | R7 ring, full → `busy` on the reply channel, `systemOpReserve` | §2 (TR-sim-017) |
| simulation-core.md | R8 drain cap, pre-checks, atomicity, recording; SC-R8-1/2/3 | §3 (TR-sim-018) |
| simulation-core.md | R10 replies, 1–63 Core reasons, reply after events, no Sim dedupe; SC-R10-1 | §5, §6 (TR-sim-020) |
| simulation-core.md | F8 `opQueueCapacity` 4096, `systemOpReserve` 64, `maxOpsDrainedPerTick` 64 | §2, §3 defaults; `SimSettings` validation (SC-SET-1) |
| grid-inventory.md | R7 ops atomic in one FIFO, sender from the transport peer, malformed → `bad_request` | §2 actor stamping, §5 code 8 (TR-inventory-025) |
| grid-inventory.md | Network rule 8: `reqId` u16 per connection, last-16 reply cache in the adapter keyed `(peer, reqId)`, reset on rejoin | §6 |
| manual-delivery.md | MD5 and M-NET-2: a duplicate `reqId` gets the original reply and consumes nothing | §6 (TR-delivery-011) |

## Performance Implications
- **CPU:** At most 64 ops per tick through a direct descriptor-table dispatch (array index by kind). The Core pre-checks are O(1). The perf budget slice for `Inputs.Apply` (0.80 ms p99) is owned by perf-budget.md.
- **Memory:** The ring is 4 096 × 64 B = 256 KiB, plus 16 KiB of boundary ticks. The adapter cache is 16 replies × ≤ 48 B per peer, under 1 KiB per peer.
- **Load Time:** None.
- **Network:** The request `reqId` stays u16. `busy` and cached-reply resends use the reply stream, and the duplicate path costs one reply.

## Migration Plan
No code exists.
- The Sim Core op story creates `Sim/Core/{Op,OpKind,OpDescriptor,OpRing,Reply,CoreReason,IOpHandler}.cs`.
- ADR-0011's seam story creates the adapter codec and the dedupe cache. M-1a uses loopback through the same codec.

## Validation Criteria
- SC-R6-1, SC-R7-1, SC-R8-1, SC-R8-2, SC-R8-3 and SC-R10-1 pass (M-1a/M-1b per the GDD tags).
- A descriptor enumeration test confirms every payload is at most 48 B and equals its declared `PayloadLength`, every system reason is at least 64, and every `System` kind is `SystemOnly`.
- Adapter tests (ADR-0011, M-1a loopback):
  - a duplicate inside the cache returns byte-identical reply bytes, and `opSeq` is unchanged;
  - a duplicate while in flight is dropped;
  - after rejoin the cache is empty;
  - a malformed frame gets `bad_request` and enqueues nothing.
- Grid H-NET-1 (reply after event) and Manual Delivery M-NET-2 pass end to end over loopback.

## GDD Impact (no GDD edited by this ADR)
- **simulation-core.md R10 "Duplicates" and HB1(i)** ("the rule (8) dedupe cache is handler state in its dump contributor") are **superseded**. The cache is adapter state, outside the dump, and no Sim handler implements `reqId` idempotence. R6's "its wire width belongs to the owning protocol" stays true. Recommended one-line amendment to R10 at re-review: "Grid Inventory rule 8's 16-reply cache lives in the network adapter (ADR-0007 §6)". The Edge Case "If a client resends a `reqId`… unless its handler is idempotent" changes accordingly.
- **grid-inventory.md rule 8** already places the cache in the adapter, so it is consistent. Grid's `bad_request` maps to Core reason 8.
- **manual-delivery.md** open question 4's parenthetical (Sim Core says the Sim does not deduplicate; MD5 relies on Grid) is now consistent: both hold.
- **The architecture blueprint's** `IOpHandler.Handle(in Op, in TickContext)` returning `void` is amended to `Reply Apply(...)`, as in GDD R8. The architecture doc is amended when this ADR is Accepted (its "ADR wins" rule).

## Related
- ADR-0003 (phase 0, builder, `TickContext`).
- ADR-0004 (`ActorId`, `PlayerSlot`).
- ADR-0006 (recorder, zeroing, overflow classes).
- ADR-0008 (`OpReply` entry and ordering).
- ADR-0011 (adapter codec, `busy`, dedupe; must adopt §6).
- ADR-0009 (InventoryOp kinds and codes).
- `design/gdd/simulation-core.md` R6–R10, F8. `design/gdd/grid-inventory.md` R7 and Network rule 8. `design/gdd/manual-delivery.md` MD5 and M-NET-2. Seed §1 and §3.1 (consumed by this ADR).

## Errata
- 2026-10-28 — §4 block table amendment: the Economy block `0x0180`–`0x01BF` is allocated (`OpKindBlock.Economy`, appended to `OpKindBlockTable.Blocks`; the "later systems" row now starts at `0x01C0`). The block table is shared by `OpKind` and `EventKind` descriptors; its first kind is the `WalletChanged` event, `EconEventKinds.WalletChanged = (EventKind)0x0180`, raised by the M-1a `StubWallet` (mail-destinations story 005; ADR-0017 §8). Approved by Evan 2026-10-28 7:26 AM PT, in his own words in the Mail Lead chat, card 'Mail-destinations 005 decisions': "Approve all six TD-177 defaults for mail-destinations 005, including the ADR-0007 §4 economy event block row"; source TD-177 item 3. Table amendment only; the Decision is unchanged and the ADR stays Accepted.
