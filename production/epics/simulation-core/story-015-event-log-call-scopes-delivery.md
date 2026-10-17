# Story 015: Event log, call scopes and phase-13 delivery

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 1 day; ≈ 450 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R11 per-tick event log, R12 delivery and replication classes, R13 call scopes; N3 event replication contract)
**Requirement**: `TR-sim-021` (one preallocated per-tick `EventLog`; blittable, string-free entries), `TR-sim-022` (no sync subscribers; `IEventSink.Deliver` once in phase 13, copy-only), `TR-sim-023` (replication classes Internal/Replicated/OwnerOnly/Targeted; undeclared kind fails a test)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0008: Event Log, Replication Classes and Event Delivery (§1–§3, §5 scopes, §7 delivery) — primary; ADR-0003 (§2 phase 13 is built in) — secondary
**ADR Decision Summary**: One `EventLog` of `EventEntry[eventLogCapacity]` (4 096, power of two) is preallocated at `Freeze()`, append-only, valid from phase 0 to the end of `Deliver`, and reset afterwards (Debug fills the used span with `0xDD`). `EventEntry` is 64 B (`Pack = 4`): `Ordinal u32 · Kind u16 · Class u8 · Audience u8 · CallSeq u32 · PayloadLength u8 · Flags u8 · Reserved u16 · Payload [InlineArray(48)]`. Every `EventKind` declares an `EventDescriptor(Kind, Class, PayloadLength, Coalescible, ResyncScope, WireCodecId, WireSize)`; an undeclared kind fails the enumeration test and resolves to `Internal`. Sim Core opens a call scope (fresh per-tick `CallSeq`) around every op handler and phase run; `I…Api` mutations open `log.BeginCall()`. Phase 13 calls `IEventSink.Deliver(tick, entries, overflow)` exactly once with every entry in raise order; during `Deliver` the Sim is closed.
**ADR Version**: 2026-10-16 (ADR-0008), 2026-10-16 (ADR-0003)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `EventEntry` 64 B layout exactly; payloads `unmanaged`, padding-free, ≤ 48 B, string-free, no `Int128`, unaligned, zero-filled past their length; every `EventKind` declares an `EventDescriptor` (manifest I-3: ADR-0008 names); `EventKind` mirrors `OpKind` blocks, `OpReply = 0x0001`; log preallocated at `Freeze()`, append-only, reset after `Deliver` (Debug `0xDD`); audience resolved at raise by the raiser (`OwnerOnly` exactly one bit); every `I…Api` mutation opens `using var scope = log.BeginCall();`; `Deliver` exactly once per tick, every entry in raise order, skipped when no sink is bound, not called for a faulted tick; during `Deliver` `EnqueueOp` false, raises dropped, re-entrant `Tick` counted.
- Forbidden: synchronous subscribers; a typed queue per kind or variable-length entries; Sim Core merging events; raising outside phases 0–12; a sink that calls the transport, Godot or the Sim, or keeps the span.
- Guardrail: event log 4 096 × 64 B = 256 KiB; `Events.Flush` 0.25 ms p99 (advisory until a reference machine exists); raise/scope 0 B.

---

## Acceptance Criteria

*From GDD SC-R12-2 and ADR-0008 §1–§3, §5, §7, scoped to this story:*

- [x] **AC-1 SC-R12-2.** With an `IEventSink` stub bound, when a tick runs in which phases raise a mix of `Internal`, `Replicated` and `OwnerOnly` entries: `Deliver` is called exactly once, from phase 13 (`ITickReader.Phase == EventsFlush`), with the tick number and every entry — `Internal` included — in raise order with `Ordinal` 0…n−1, each carrying its descriptor's class and the audience mask given at raise; inside `Deliver`, `EnqueueOp` returns false (counted `enqueueDuringFlush`) and a re-entrant `Tick()` runs nothing (counted `badTick`), each asserting under `SIM_ASSERT`.
- [x] **AC-2 Layout, descriptors and lifetime.** `Unsafe.SizeOf<EventEntry>() == 64` with ADR-0008 §1 offsets; raising a 12-byte payload zero-fills bytes 12–47; a kind raised without a declared descriptor is stored as `Internal` and the descriptor-enumeration test helper reports it; after `Deliver` returns the count is 0 and, under `SIM_ASSERT`, the previously used span reads `0xDD`; with no sink bound the tick completes and the log still resets; a faulted tick (Story 012) never calls `Deliver`.
- [x] **AC-3 Call scopes.** Entries raised inside a phase run carry that run's `CallSeq`; nested `BeginCall()` scopes get fresh, increasing `CallSeq` values and restore the outer one on `Dispose`; `CallSeq` restarts at 1 each tick; `BeginCall` and `TryRaise` allocate 0 B.

---

## Implementation Notes

*Derived from ADR-0008 §1–§3, §5, §7 and Key Interfaces:*

- Files in `src/PerformativeMail.Sim/Core/`: `EventEntry.cs` (`EventPayload` `[InlineArray(48)]`), `EventKind.cs` (`enum EventKind : ushort { None = 0, OpReply = 0x0001 }` — systems append in their blocks), `ReplicationClass.cs`, `EventFlags.cs` (`Continuation = 1`), `EventDescriptor.cs` (`sealed record`), `EventLog.cs`, `EventCallScope.cs` (`ref struct`, `Dispose` pops), `IEventSink.cs`, `OverflowInfo.cs` (readonly struct; filled by Story 016 — pass a default here).
- `SimWorldBuilder.BindEventKind(EventDescriptor)` and `BindEventSink(IEventSink)` (the sink is bound by the host adapter at composition; a world may have none). `Freeze()` builds a `ushort`-indexed descriptor lookup (array, not a dictionary iteration) and allocates the log.
- `bool TryRaise<T>(EventKind kind, byte audience, in T payload) where T : unmanaged`: append at `count`, stamp `Ordinal = count`, `Class` from the descriptor (unknown → `Internal`), `CallSeq` = current scope, copy payload unaligned with tail zero-fill. `Raise<T>` returning an `EventHandle` (for `Coalescible` kinds) may be added now as a thin wrapper; `Update<T>` and the nested-call **fence** are the frozen coalescing interface built by its first consumer (grid-inventory, SC-R10-2) — declare nothing you cannot test.
- Audience rules are the raiser's: `Replicated` = connected-slot mask at raise (from `SlotTable`, Story 018 — provide a helper there), `OwnerOnly` exactly one bit (assert ≥ 2 bits under `SIM_ASSERT` — 0 bits is legal and delivers to nobody, e.g. a reply to `System`, per GDD Edge Cases and ADR-0008 §3; *corrected at close 2026-10-16, was "≠ 1 bit"*; Release keeps the lowest bit — implement the normalisation in `TryRaise` for `OwnerOnly` kinds), `Internal` 0.
- Phase-run scope: in `SimWorld.Tick()` wrap each `ISimPhase.Run` in `using var s = _log.BeginCall();`. Op-handler scopes are Story 020.
- `EventsFlush` (built-in phase 13): set `Stage = InPhase, Phase = EventsFlush`, set an internal `_delivering` flag, call `_sink?.Deliver(CurrentTick, _log.Span, in overflow)` once, clear the flag, then reset (`count = 0`, `callSeq = 0`, cursors rewound — Story 016 — and, under `SIM_ASSERT`, fill the used span with `0xDD` via `Span.Fill` on the entries' bytes). Story 012's fault path must still reset the log without delivering.
- While `_delivering`: `EnqueueOp` → false + `enqueueDuringFlush`; `Tick()` → `badTick` (Story 012 guard already rejects re-entry; make sure the counter is the same); `TryWriteDump` → false (Story 019); raises → dropped (Story 016 adds the `eventsDuringFlush` + `Overflow` outcome).
- Descriptor test helper (test project): enumerate every declared `EventKind` member and assert a descriptor is bound in a full-profile world; reuse Story 013's padding-free/≤ 48 B check for payload types.

---

## Out of Scope

- Story 016: overflow admission and `OverflowInfo` masks, misuse raises (outside tick, tick end, during flush), pull readers/cursors.
- Story 020: op-handler scopes and the `OpReply` append.
- Coalescing (`Update<T>`, fence, `coalesceLate`) — frozen interface, first consumer grid-inventory (SC-R10-2, M-1b).
- Adapter-side copy/fan-out/ordering — network-adapter-loopback.
- SC-R11-1, SC-R12-3 (M-1b) — mechanics land here; ACs verified at M-1b.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/EventLogTests.cs`, `tests/unit/sim/Core/EventDeliveryTests.cs` (`[Trait("Id", "SC-R12-2")]`) — must exist and pass (Debug + Release)

**Status**: [x] Created and passing — `tests/unit/sim/Core/EventLogTests.cs` (35 + NonParallel `EventLogAllocationTests` 2), `tests/unit/sim/Core/EventDeliveryTests.cs` (15 + NonParallel `EventDeliveryAllocationTests` 1), fixture `tests/PerformativeMail.Sim.Tests/Fixtures/EventDescriptorAssert.cs`; all `[Trait("Id","SC-R12-2")]`

---

## Dependencies

- Depends on: Story 011, Story 012 (fault path, re-entrancy), Story 014 (`EnqueueOp` closure check)
- Unlocks: Story 016, Story 020, network-adapter-loopback (`HostAdapter : IEventSink`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 → EventDeliveryTests.cs:123/169/185/207/254/286; AC-2 → EventLogTests.cs:82/90/105/118/170/185/288/300/318/493 + EventDeliveryTests.cs:325/356/375/401/420; AC-3 → EventLogTests.cs:537/572/606/653/692 + EventDeliveryTests.cs:450/471/485/582)
**Verification run**: Release build 0 warnings / 0 errors; tools/ci/test-legs-loop.sh 1 — 1 232 tests/leg, 0 failed (net10.0/net8.0 × Debug/Release; Debug 6 skipped = pre-existing optimised-only alloc tests, none from this story). Re-run by the closer from disk.
**Files**: new src/PerformativeMail.Sim/Core/{EventEntry,EventKind,ReplicationClass,EventFlags,EventDescriptor (+CoreEventDescriptors.OpReply),EventLog,EventCallScope,IEventSink,OverflowInfo}.cs; modified SimWorld.cs (phase-run scopes, EventsFlush/Deliver, `_delivering`, EnqueueOp enqueueDuringFlush refusal replacing TODO(Story-015), fault-path reset), SimWorldBuilder.cs (BindEventKind/BindEventSink, ushort-indexed lookup), SimWorldParts.cs, SimBuildException.cs (codes 3–5), Primitives/SimAssertCode.cs (29–34), tests Fixtures/DescriptorAssert.cs (CheckPayload internal).
**Deviations**: ADVISORY — story Implementation Notes said "assert ≠ 1 bit" for OwnerOnly; implementation follows GDD Edge Cases (~line 698) / ADR-0008 §3 / manifest ("0 for System"): asserts on ≥ 2 bits, 0 bits legal. Story text corrected above. Implementer decisions where the story was silent (all accepted by LP): builder binds OpReply descriptor by default (OwnerOnly, Reply 36 B); EventEntry.Kind typed EventKind (same layout); EventDescriptor gains init PayloadType + For<T>; Internal audience forced to 0; payload > 48 B asserts / Release returns false; payload > descriptor PayloadLength asserts; full or closed log → false, no bookkeeping (Story 016); Deliver called every tick even when empty; throwing sink → `finally` clears `_delivering` + resets log, world Faulted; SimWorld.Events internal (Story 025 decides system access); per-phase scope-leak check (EventScopeLeak).
**Gates**: QL-TEST-COVERAGE ADEQUATE; LP-CODE-REVIEW APPROVE; godot-csharp-specialist cross-check no blockers. Advisories → TD-032/TD-033/TD-034.
**Test Evidence**: Logic: tests/unit/sim/Core/EventLogTests.cs, tests/unit/sim/Core/EventDeliveryTests.cs
**Code Review**: Complete (LP-CODE-REVIEW APPROVE)
**Run result**: N/A — Godot-free Sim logic, no observable surface; verified by the four-leg suite run above.
**Left for later**: TODO(Story-016) SimWorld.EventsFlush OverflowInfo (passes default); TODO(Story-016) scopeLeak counter in CloseLeakedScopes; Story 016 must change `Ac1_RaiseAfterDeliver_BetweenTicks_IsAcceptedAgain` when it adds misuse-raise rejection.
