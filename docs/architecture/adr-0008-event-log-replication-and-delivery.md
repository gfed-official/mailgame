# ADR-0008: Event Log, Replication Classes and Event Delivery

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
- Author: `/architecture-decision` (autonomous run, `--review lean`), on behalf of technical-director.
- Inputs: Simulation Core GDD (R11–R14, F8, N1, N3), the TD seed §3.2 (a stale verbatim block; see Context), the registry (`eventLogCapacity`, `eventOrderRule`), Grid Inventory (event/reply ordering, per-call `replace`, version rules), and Mail & Destinations (`DeliveryOutcome`).
- Engine validation: godot-specialist (Step 5.5). TD-ADR skipped (lean mode).
- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
Everything the Sim says during a tick goes into one preallocated, append-only, per-tick `EventLog`: 4 096 entries of 64 B each, with a 256-entry reserve. The log keeps raise order and resolves each entry's audience at raise time. It has no synchronous subscribers; later phases read through pull cursors. It is handed to the adapter **once**, in phase 13, through a copy-only `IEventSink.Deliver`, and transport sends happen only after `Tick()` returns.

This ADR defines what the seed was missing:
- the 64 B entry layout and the **48 B payload ceiling**;
- the four replication classes;
- in-place merging that is legal only within the raising handler or API call;
- two-tier overflow admission with an `overflowInfo` report (overflow flag, dropped audience mask, dropped kinds mask).

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Core and Networking boundary. The Sim side is Godot-free. |
| **Knowledge Risk** | LOW for the Sim log (plain .NET structs and spans). **HIGH** for the adapter's transport half (ADR-0011), because Godot 4.4–4.7 multiplayer is NOT SOURCEABLE. This ADR only requires the adapter to copy inside `Deliver` and send after `Tick()`. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md`. There are no relevant entries. Multiplayer is NOT SOURCEABLE. |
| **Post-Cutoff APIs Used** | None. |
| **Verification Required** | (1) `Unsafe.SizeOf<EventEntry>() == 64` and every payload is ≤ 48 B. (2) SC-R12-2: `Deliver` is called exactly once per tick, from phase 13, with no re-entry. (3) In the M0 multiplayer spike: the adapter's per-peer send after `Tick()` preserves the order of entries for one peer on one reliable channel (Q-ARCH-05). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0003 (phase 13 `EventsFlush`, the tick-end check slot, reader and descriptor binding in `SimWorldBuilder`, `TickContext`). ADR-0004 (`PlayerSlot` 0–7 → the 8-bit audience mask; the `slot ↔ playerId` table). ADR-0006 (zero-initialised payloads, overflow classes, 0-alloc). ADR-0007 (`Reply` → the `OpReply` entry; the per-op call scope). |
| **Enables** | ADR-0009 (Inventory events, `replace` coalescing, reqId-tagged events through `CallSeq`), ADR-0011 (adapter packing, per-peer filtering, resync on `overflowInfo`), ADR-0016 (ClientLayer mirror drain), ADR-0017 (`DeliveryOutcome`), ADR-0021, ADR-0022 (JoinState after `Deliver` of `joinTick`). |
| **Blocks** | The Sim Core "event log, cursors and flush" story (TR-sim-021..024). The Mail `DeliveryOutcome` story (TR-mail-015). |
| **Ordering Note** | Accept ADR-0003, ADR-0004, ADR-0006 and ADR-0007 first. ADR-0011 must honour §7 (adapter rules). |

## Context

### Problem Statement
Spec 07 sketches "an in-process event bus (SimEvent structs)" and an `Events.Flush() → ServerRuntime`. The reviewed Sim Core GDD fixes the behaviour (R11–R14): one ordered log per tick, pull cursors, copy-only delivery, replication classes and two-tier overflow. It defers the **entry byte layout and payload ceiling** to this ADR.

The seed's verbatim §3.2 also carries defects this ADR must not copy (architecture §ADR Audit):
- per-**phase** coalescing (the GDD narrowed it to per **call**);
- `Deliver` without `overflowInfo`;
- an overflow that "resyncs every audience" instead of exactly the dropped one.

The decision is Foundation-critical: every system raises events, and M-1a's loopback ClientLayer consumes them.

### Constraints
- **Allocation.** Zero allocation per tick: entries are blittable structs in a preallocated array, with no strings (ADR-0006 §8).
- **Peers.** The Sim knows no peers. Audiences are `PlayerSlot` bitmasks: 8 slots, so a u8 mask.
- **No re-entry.** No synchronous subscribers, so no hidden ordering (R12).
- **Wire budget.** `DeliveryOutcome` is ≤ 19 B. Inventory sustains ≤ 40 B per peer per tick. Large state (a 320-entry `replace`, ≤ 10 KB) cannot fit in one entry.
- **Ordering.** Grid Inventory's client correctness relies on **event-before-reply on one stream** and on per-container version continuity.

### Requirements
- **TR-sim-021.** One preallocated per-tick `EventLog` (4096, reserve 256), with blittable entries that hold no strings.
- **TR-sim-022.** No synchronous subscribers. Pull cursors, at most 8. `IEventSink.Deliver` is called once in phase 13 and is copy-only.
- **TR-sim-023.** Replication classes `Internal`, `Replicated`, `OwnerOnly` and `Targeted`. An undeclared kind fails a test.
- **TR-sim-024.** Two-tier overflow admission. `overflowInfo` carries masks, and the adapter resyncs exactly those.
- **TR-mail-015.** `DeliveryOutcome` is one event per op, ≤ 19 B, with a canonical encoding.

## Decision

### 1. Entry layout (64 B, blittable) and payload ceiling

```
offset size field             notes
0      4    Ordinal  u32      0..count−1 within the tick, = array index
4      2    Kind     u16      EventKind (OpReply = 0x0001)
6      1    Class    u8       ReplicationClass, copied from the kind's descriptor at raise
7      1    Audience u8       PlayerSlot bitmask, resolved at raise time (§3)
8      4    CallSeq  u32      the handler/API call scope that raised it (§5); 0 = no scope
12     1    PayloadLength u8  0..48, = descriptor length (or ≤ it for Continuation chunks)
13     1    Flags    u8       bit0 Continuation (owner chunking, §1.3); bits1–7 zero
14     2    Reserved u16      zero
16     48   Payload           [InlineArray(48)] bytes; tail beyond PayloadLength is zero
```

> **As implemented (2026-10-16, network-adapter-loopback story-004; TD-048 item 1):** for `OpReply`, `CallSeq` is the CallSeq of the call scope it answers (0 when rejected by a pre-check). The phase-0 drain raises the reply through the Sim-internal `EventLog.TryRaiseWithCallSeq`, still outside and after the call's scope, so the adapter links a reply to its call's events by `CallSeq` (§5, §7 (d)).

1. **Payload ceiling: 48 B per entry.** Each `EventKind` declares an `unmanaged` payload struct. The descriptor test asserts `Unsafe.SizeOf<T>() ≤ 48`.
   - Payloads hold no references, no strings (strings are `StringId` u32) and no `Int128`.
   - Payload structs are **padding-free**: the descriptor test checks that the sum of field sizes equals `SizeOf`.
   - Payloads are written and read with unaligned access only, as in ADR-0007 §1.
2. **`OpReply` payload** (from ADR-0007's `Reply`):
   - `ReqId` u32, `Kind` u16, `Reason` u16, `Result` u8;
   - 3 B of explicit zero pad fields (`_pad0`–`_pad2`, ADR-0007), so the struct stays padding-free under the rule above;
   - `Extra` 24 B.

   That is 36 B.
3. **Large state is never inflated into one entry.** The owner does one of two things:
   - **(a) By reference.** It raises a small entry naming a snapshot block that the adapter pulls between ticks through `ISnapshotSource` (ADR-0011). Example: Grid's `replace` carries the container id and `version_from`/`version_to`, and the layout is fetched by reference.
   - **(b) Chunking.** It emits a run of entries of the same kind and `CallSeq`, where every entry after the first sets `Continuation`. The adapter reassembles them in order. Chunked runs are contiguous by construction (one call).

   Which owner uses which is that owner's ADR (ADR-0009 for `replace`).
4. **`DeliveryOutcome`** (Mail, ≤ 19 B canonical) fits in one entry: one per op, raised before that op's `OpReply` (TR-mail-015).

### 2. The log

- **Storage.** `EventLog` holds `EventEntry[eventLogCapacity]` (default 4 096, a power of two) plus `count`, `callSeq`, the scope stack (§5) and the `OverflowInfo` accumulator. It is preallocated at `Freeze()` and owned by `SimWorld`. 4 096 × 64 B = 256 KiB.
- **`SimSettings` validation** (F8, SC-SET-1) rejects:
  - a non-power-of-two capacity;
  - `eventLogReserve > capacity / 4`;
  - `maxOpsDrainedPerTick × 4 + 1.5 × systemEventsPerTick > capacity − reserve`.

  The defaults give 826 ≤ 3 840.
- **Raising.** `bool TryRaise<T>(EventKind kind, byte audience, in T payload)` and `EventHandle Raise<T>(…)` (the latter for `Coalescible` kinds) append at `count`, stamp `Ordinal`, `Class` and `CallSeq`, and copy the payload, zero-filling the tail.
- **Append-only.** Entries never move. Ordinals equal positions. No API removes or reorders an entry.
- **Lifetime.** The log is valid from the start of phase 0 to the end of `Deliver` in phase 13. After `Deliver` returns:
  - `count`, `callSeq` and the overflow accumulator are reset, and cursors rewind;
  - **Debug builds fill the used span with `0xDD`**, so a sink that kept the span reads garbage (SC-R11-1).

  Events never survive a tick. A system needing something on T+1 stores state.
- **Dump.** The log is not dumped: it is empty between ticks (ADR-0006 §4).

### 3. Replication classes and audience (resolved at raise time)

Every `EventKind` declares exactly one class in its descriptor. An undeclared kind **fails the enumeration test** and resolves to `Internal` at runtime (fail-closed).

| Class | Audience written at raise | Delivered to |
|---|---|---|
| `Internal` | 0 | Never leaves the process. In-process tools and tests still see it in `Deliver`. |
| `Replicated` | The **connected-slot mask at raise time**, from the Sim's slot table | Those slots' peers |
| `OwnerOnly` | Exactly one bit. 0 bits delivers to nobody. 2 or more bits asserts in Debug, and Release keeps the lowest bit. | That slot's peer |
| `Targeted` | The owner's subscription query result at raise (for example Grid's container viewers) | Those slots' peers |

- **Replicated masks.** Writing the raise-time connected mask (rather than "all peers at send time") means a player who connects later in the same tick never receives earlier events. It also means the overflow audience (§6) is computed uniformly from masks.
- **`OpReply`** is `OwnerOnly` to the actor's slot. A reply to `System` has audience 0.

### 4. Pull cursors (no synchronous subscribers)

- **Declaration.** A reader is declared at build: `builder.BindEventReader(ReaderId, EventKind[] filter)`. There are at most `maxEventReaders` = 8; a 9th throws `SimBuildException` (ADR-0003). The filter is stored as a small sorted `ushort[]`.
- **Reading.** `bool EventLog.Read(ref EventCursor cursor, out EventRef entry)` returns the next entry at a position ≥ `cursor.Position` whose kind matches, and advances. It only ever sees entries already in the log at the moment of the call, so never one from a later phase.
- **Order.** Events a reader raises append after everything already in the log. For a knockdown the order is `PlayerDied`, container close, `Removed`/spill, `CarrierKnockedDown`.
- **Reset.** Cursors are rewound to 0 at the start of each tick by Sim Core.
- **Reserve admission.** A kind with at least one bound reader is flagged `HasReader` at `Freeze()`. That flag decides reserve admission (§6).

### 5. Merging in place ("coalescing") is per handler or API call

- **Scopes.**
  - Sim Core opens a **call scope** around every op handler invocation (ADR-0007 §3).
  - Every public `I…Api` **mutation** entry point opens one with `using var scope = log.BeginCall();`. The scope is a `ref struct`, so this is allocation-free.
  - Each `BeginCall` takes a **fresh `CallSeq`** (a per-tick counter starting at 1) and pushes it. `Dispose` pops back to the outer scope.
  - If the inner scope appended any entry, the pop also records a **fence** at the current `count` on the outer frame.
  - Raises made outside any scope (phase step code that is not an API call) get the phase-run scope Sim Core opens around each `ISimPhase.Run`.
- **Updating.** Only kinds declared `Coalescible` may be rewritten, through `ref T EventLog.Update<T>(EventHandle h)`. The update is legal **only if** both hold:
  - `h.CallSeq == current CallSeq`, i.e. the same call that raised it;
  - `h.Index ≥ current frame's fence`, i.e. no nested call has appended entries after it.

  The entry keeps its original position, so a reply that follows it (appended when the call ends) still follows it.
- **Illegal update.** Debug asserts. Release returns a ref to a **scratch entry**: the write is lost and counted `coalesceLate` (SC-R10-2).
- **No automatic merging.** Sim Core never merges events on its own. "One `replace` per container per tick" becomes **one per call** (HB1(g)).

### 6. Overflow (two-tier admission) and misuse

| Situation | Admission |
|---|---|
| `count == capacity` | Every kind is dropped |
| `count ≥ capacity − reserve` and the kind is neither `HasReader` nor `OpReply` | Dropped |
| Otherwise | Appended |

For every dropped entry:
- `eventsDropped` is incremented;
- `overflowInfo.Overflow` is set to true;
- the entry's raise-time audience is ORed into `DroppedAudienceMask` (a `Replicated` entry contributes the connected mask);
- `1UL << descriptor.ResyncScope` is ORed into `DroppedKindsMask`.

Every `EventKind` declares a `ResyncScope` (0–63: Players, Inventory, Mail, Throwing, Run, Combat, …; the table is in the descriptor file), so a dropped `OpReply` sets the scope of the op kind it answers.

- State is **not** rolled back, because the handler already committed. **No entry is ever reordered.**
- A recording that saw an overflow is marked non-reproducible (ADR-0006 §7, `IOpRecorder.OnOverflow`).

**Raises outside the legal window:**

| Where | Outcome |
|---|---|
| Outside `Tick()` | Debug asserts. Release drops it and counts `eventsOutsideTick`. |
| In an `ITickEndCheck` | Debug asserts. Release drops it, counts `eventsAtTickEnd` and **sets `Overflow`** (a missing event desyncs clients). |
| During phase 13 / `Deliver` | Debug asserts. Release drops it, counts `eventsDuringFlush` and **sets `Overflow`**. |

**Sizing.** With the drain cap (ADR-0007), F8 guarantees that the log cannot overflow on the worst fixture tick (SC-R8-4, M0). Overflow is a defended failure, not a design path.

### 7. Delivery: once, copy-only, inside phase 13; sending happens after `Tick()`

- **The single output point.** `EventsFlush` (phase 13, after the tick-end checks) calls `IEventSink.Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)` **exactly once**, with every entry including `Internal` ones, in raise order. With no sink bound (headless tests, BalanceSim) the call is skipped. This is the Sim's **only** output point.
- **During `Deliver`, the Sim is closed:**
  - `EnqueueOp` returns false and counts `enqueueDuringFlush`;
  - `TryWriteDump` returns false;
  - a raise is dropped (§6);
  - a re-entrant `Tick()` counts `badTick`;
  - Debug asserts on each.
- **What the adapter may do inside `Deliver`.** It only:
  - (a) copies the entries it needs into its own **preallocated** buffers;
  - (b) resolves each entry's audience mask to a **peer set** through its slot → peer map, so a queued batch holds peers, never slots, and a slot reused at T+1 never receives a leaver's events (SC-X-4);
  - (c) updates its per-peer reply cache from `OpReply` entries (ADR-0007 §6);
  - (d) for each `OpReply`, may tag same-`CallSeq` events going to that actor's peer with the reply's `reqId` (Grid's reqId-tagged events), applied by ADR-0009/0011.

  It makes **no transport call, no Godot call and no Sim call**, and it must not keep the span.
- **Sending.** `FrameRunner` (ADR-0002) sends the adapter's queued batches **after `Tick()` returns**. The loopback ClientLayer receives its batch then, and `MirrorDrain` applies it at the next client tick (Sim Core N2).
- **Faulted tick.** If a phase faults (SC-ST-3), `Deliver` is not called for that tick.

**Adapter ordering rule (binding on ADR-0011):**
- For any one peer, the adapter must preserve the **relative order of the entries it sends** (Sim Core N3.1). An event therefore always precedes the `OpReply` that follows it in the log.
- **Merging entries across calls on the wire** (Grid's "the adapter merges one container's replaces of a tick at flush") is permitted **only** under all three conditions below; anything else is a reorder and is forbidden:
  - (i) the merged entries are the same `Coalescible` kind for the same subject (container);
  - (ii) the merged entry takes the position of the **earliest** of them;
  - (iii) **no other entry about the same subject** lies between them in the log.

  Under these conditions every reply still arrives after the state it reports, which satisfies Grid's "a reply never arrives for a version the mirror has not applied". ADR-0011 owns the decision to merge at all, and proves it with Grid H-NET-1 and the version-gap tests.

### Architecture Diagram

```
 Tick T:  phase 0 ─ op call scopes ─┐  phases 1–12 ─ API call scopes / phase scopes ─┐
                                    ▼                                                  ▼
          EventLog[4096×64B]: ord 0 … n−1  (raise order; Class+Audience at raise; CallSeq)
             ▲ readers (≤8) Read(ref cursor) — earlier entries only
             ▲ Update(handle) — same CallSeq & past the fence only (Coalescible)
          tick-end checks (raise → dropped + overflow)
          phase 13: Deliver(T, span[0..n), overflowInfo)  ── copy + mask→peers + reply cache ──► adapter buffers
          (Debug: span ← 0xDD; count = 0)
 After Tick() returns: FrameRunner → adapter sends per peer, order preserved; loopback → ClientLayer inbox
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.Core;
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct EventEntry { public uint Ordinal; public ushort Kind; public ReplicationClass Class; public byte Audience;
    public uint CallSeq; public byte PayloadLength; public EventFlags Flags; public ushort Reserved; public EventPayload Payload; } // 64 B; EventPayload = [InlineArray(48)]
public enum ReplicationClass : byte { Internal = 0, Replicated = 1, OwnerOnly = 2, Targeted = 3 }
[Flags] public enum EventFlags : byte { None = 0, Continuation = 1 }
public enum EventKind : ushort { None = 0, OpReply = 0x0001 /* per-system blocks, mirrors OpKind ranges */ }
public sealed record EventDescriptor(EventKind Kind, ReplicationClass Class, byte PayloadLength, bool Coalescible,
    byte ResyncScope, ushort WireCodecId, byte WireSize);
public readonly struct OverflowInfo { public bool Overflow { get; } public byte DroppedAudienceMask { get; }
    public ulong DroppedKindsMask { get; } /* bit = ResyncScope */ public uint DroppedCount { get; } }
public readonly struct EventHandle { public int Index { get; } public uint CallSeq { get; } }
public ref struct EventCallScope { public void Dispose(); }
public struct EventCursor { /* ReaderId, Position */ }
public sealed class EventLog {
    public EventCallScope BeginCall();
    public bool TryRaise<T>(EventKind kind, byte audience, in T payload) where T : unmanaged;
    public EventHandle Raise<T>(EventKind kind, byte audience, in T payload) where T : unmanaged; // Coalescible kinds
    public ref T Update<T>(EventHandle h) where T : unmanaged;     // same call, past the fence; else scratch + counted
    public bool Read(ref EventCursor cursor, out EventRef entry);   // declared readers only
}
public interface IEventSink { void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow); } // copy-only
```

### Implementation Guidelines
- Systems **must** raise events only on commit, inside a handler or API call scope, in phases 0–12. They **must never** raise in tick-end checks or during flush.
- Every event kind **must** declare a class, a payload of at most 48 B, a `ResyncScope` and (if it is rewritten) `Coalescible`. An undeclared kind fails CI.
- Payloads **must** be `unmanaged`, string-free and zero-filled beyond their length.
- Audience **must** be resolved at raise time by the raising system. `OwnerOnly` **must** carry exactly one bit.
- Public `I…Api` mutation methods **must** open `log.BeginCall()` at entry.
- `Update` **must** only target an entry of the current call with no nested-call entry after it.
- `IEventSink.Deliver` implementations **must** copy into preallocated buffers, resolve masks to peers inside the call, and **must never** call the transport, Godot or the Sim, or retain the span.
- The adapter **must** send only after `Tick()` returns, and **must** preserve per-peer entry order, except the conditioned merge in §7.
- No system **must ever** subscribe synchronously. Later-phase consumers declare a reader.

## Alternatives Considered

### Alternative 1: C# events / delegates (synchronous subscribers)
- **Description:** `event Action<T>` per kind.
- **Pros:** Familiar and immediate.
- **Cons:** Re-entrancy, hidden ordering by subscription order, allocation (delegates and closures), and cross-system calls in the wrong phase.
- **Rejection Reason:** R12 forbids it, and it breaks the deterministic total order (phase → composition → owner).

### Alternative 2: A typed queue per event kind
- **Description:** A separate ring per kind.
- **Pros:** Type-safe access. No payload ceiling per kind.
- **Cons:** The global raise order is lost, so "event before reply" and knockdown ordering would need a merge by sequence number. There are many preallocated rings. Overflow is per kind.
- **Rejection Reason:** One log gives causal order for free, and the clients' correctness relies on it.

### Alternative 3: Variable-length entries in a byte arena
- **Description:** Pack entries tightly.
- **Pros:** Less memory and more events per KiB.
- **Cons:** Cursor and update by index become offset arithmetic. Large payloads would invite unbounded entries. Harder dump and debug.
- **Rejection Reason:** 256 KiB is trivial against the 64 MB Sim ceiling. A fixed 64 B entry makes ordinals indices and keeps everything O(1).

### Alternative 4: Per-phase coalescing (seed §3.2)
- **Description:** An owner may rewrite its entry anywhere in the phase that raised it.
- **Pros:** Fewer `replace` entries.
- **Cons:** A rewrite could move state past a reply that already follows it, so the client applies an insert twice. This is the exact defect the review found.
- **Rejection Reason:** The GDD narrowed it to per call (R13). The fence rule makes "per call" safe against nested calls too.

### Alternative 5: Resync every audience in an overflowed batch (seed §3.2)
- **Description:** On any overflow, resync everyone.
- **Pros:** Simple.
- **Cons:** One noisy subsystem would force full resyncs for all 8 players.
- **Rejection Reason:** The masks let Networking resync exactly the affected slots and scopes (R14, HB10).

## Consequences

### Positive
- **Causal order by construction.** Event-before-reply, knockdown ordering and per-call merging are properties of the log, not adapter discipline.
- **Bounded cost.** Zero allocation, O(1) raise, and 256 KiB fixed.
- **Hardened boundary.** Copy-only delivery plus send-after-tick keeps transport, Godot and threads out of `Tick()`, and slot reuse cannot leak a leaver's events.
- **Targeted recovery.** Overflow is visible, scoped and recoverable: a resync of exactly the dropped audience and scopes.
- **CallSeq reuse.** `CallSeq` gives the adapter a free op-to-events link, which Grid's reqId-tagged events can use.

### Negative
- **Large state needs extra machinery.** The 48 B ceiling means large state needs snapshot references or chunking, which is extra machinery in ADR-0009/0011.
- **API boilerplate.** Every public mutation API must open a call scope. Forgetting it degrades merge safety only for that API, because it falls back to the phase scope. A reflection or review checklist item covers it.
- **Raise-time masks.** `Replicated` entries carry raise-time masks: a player who connects mid-tick misses that tick's events, and JoinState covers them (N3.5).
- **Stricter than the GDD.** The fence rule refuses some updates the GDD wording would allow. Those were unsafe anyway.

## Risks
- **Risk: `systemEventsPerTick` (≈ 380) is an estimate** (GDD OQ9). *Mitigation:* SC-R8-4 at M0 measures the high-water mark on SC-FIX-WORST. Capacity is a `SimSettings` knob (safe 2 048–16 384).
- **Risk: an adapter merge breaks Grid's version continuity.** *Mitigation:* §7 conditions (i)–(iii), plus H-NET-1 and the version-gap tests in ADR-0011. Absent proof, the adapter sends entries unmerged.
- **Risk: `log.BeginCall();` written as a discarded statement** compiles without a warning and never pops. *Mitigation:* Sim Core asserts in Debug that the scope depth is 0 at the end of every phase and op call. A Release mismatch is counted `scopeLeak`, and the stack is reset at phase end.
- **Risk: `1UL << ResyncScope` masks the shift count to 6 bits,** so scope 64 would silently alias bit 0. *Mitigation:* the descriptor test asserts `ResyncScope ≤ 63`.
- **Risk: sink implementations retain the span.** *Mitigation:* the Debug `0xDD` fill (SC-R11-1), plus review.
- **Risk: Godot transport does not preserve order across RPC methods on one channel** (NOT SOURCEABLE; Q-ARCH-05). *Mitigation:* send each peer's tick batch as one ordered message (or one RPC method) on one reliable channel. This is verified in the M0 multiplayer spike.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R11: one ordered log per tick; ordinal, kind, class, raise-time audience, blittable string-free payload; layout and ceiling deferred to the ADR | §1 layout (64 B, 48 B ceiling), §2 (TR-sim-021) |
| simulation-core.md | R12: no synchronous subscribers, ≤ 8 cursors, `Deliver` once in phase 13, copy-only, masks → peers in the call, classes, `Internal` fail-closed; SC-R12-1/2/3, SC-X-4 | §3, §4, §7 (TR-sim-022, 023) |
| simulation-core.md | R13: coalescing only within the raising handler or API call; SC-R10-2 | §5 call scopes and fence |
| simulation-core.md | R14: misuse, two-tier admission, `overflowInfo` masks, no rollback or reorder, non-reproducible recording; SC-R14-1/2, SC-MIS-1 | §6 (TR-sim-024) |
| simulation-core.md | F8 `eventLogCapacity` 4096, reserve 256, constraint; SC-SET-1, SC-R8-4 | §2 validation |
| simulation-core.md | N1, N3: the boundary, one batch per tick in raise order, replies after events, the resync audience | §7 |
| grid-inventory.md | Event-first-then-reply on one stream; one `replace` per call (HB1g); reqId-tagged events | §5, §7 (`CallSeq` link; the merge rule) |
| mail-destinations.md | `DeliveryOutcome`: one per op, ≤ 19 B, before the reply | §1 item 4 (TR-mail-015) |

## Performance Implications
- **CPU:** A raise is a bounds check plus a 64 B copy. A read is a linear scan from the cursor with a ≤ 4-kind filter. `Deliver` is one call. The `Events.Flush` slice (0.25 ms p99) is owned by perf-budget.md.
- **Memory:** 256 KiB for the log, plus the adapter's copy buffers (ADR-0011).
- **Load Time:** None.
- **Network:** Packing, per-peer filtering and pacing belong to ADR-0011. The Sim's empty-tick output is 0 B (N5).

## Migration Plan
No code exists. The Sim Core story creates `Sim/Core/{EventEntry,EventKind,EventDescriptor,EventLog,EventCursor,EventCallScope,OverflowInfo,IEventSink}.cs`. A recording `IEventSink` stub for tests is in the test project.

## Validation Criteria
- SC-R11-1, SC-R12-1, SC-R12-2, SC-R12-3, SC-R14-1, SC-R14-2, SC-R10-2 and SC-MIS-1 (event rows) pass.
- The SC-X-1 integration test (Grid op, throw ordering) passes at M-1a. SC-X-4 and SC-R8-4 pass at M0.
- The descriptor enumeration test confirms every kind declares a class, a `ResyncScope` and a payload of at most 48 B, and that `Unsafe.SizeOf<EventEntry>() == 64`.
- A nested-call test shows that an `Update` after a nested API call appended entries is refused (Debug assert; Release scratch plus `coalesceLate`).

## GDD Impact (no GDD edited by this ADR)
- **grid-inventory.md, Network section "Sort `replace`"** ("the adapter merges one container's replaces of a tick at flush, ahead of that tick's replies") **conflicts in wording** with Sim Core N3.1 ("never change the relative order of two entries delivered to the same peer"). This ADR rules that the merge is legal only under §7 (i)–(iii), with ADR-0011 owning whether to do it. Logged as a residual cross-GDD conflict.
- **simulation-core.md R14** names the mask `droppedKindsMask`. This ADR defines its bits as each kind's declared `ResyncScope`, not one bit per kind (there are more than 64 kinds). The name is kept.
- **Seed §3.2's** per-phase coalescing, "resync every audience" and `Deliver` without `overflowInfo` are superseded, as the GDD already did.

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
- **Verdict:** no blocking findings. `ref struct EventCallScope` with `using var` is valid. The mutable scope stack lives in `EventLog` (a class), not in the scope struct.
- **Minor findings, applied:**
  - a scope-depth assert per phase;
  - `ResyncScope ≤ 63`;
  - padding-free payloads with unaligned access.
- **Not assessed:** transport ordering across RPC methods on one channel (not sourceable). The one-ordered-message-per-peer-per-tick mitigation was judged sound.
- **TD-ADR:** skipped (lean mode).

## Related
- ADR-0003 (phase 13, builder, readers).
- ADR-0004 (`PlayerSlot`).
- ADR-0006 (zeroing, dump exclusion, recorder `OnOverflow`).
- ADR-0007 (`Reply`, call scope per op).
- ADR-0009 (Inventory events and `replace`).
- ADR-0011 (adapter sink, packing, resync, the merge decision).
- ADR-0016 (ClientLayer `MirrorDrain`).
- ADR-0022 (JoinState after `Deliver` of `joinTick`).
- `design/gdd/simulation-core.md` R11–R14, N1, N3, F8. Seed §3.2 (consumed, with its stale points not copied).
