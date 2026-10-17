# Story 007: SimCounters and SimCheckRing (always-on diagnostics)

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (≈ 0.5 day; ≈ 300 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R31 always-on checks, record ring, counters; Acceptance Criteria legend "Counters", "Records")
**Requirement**: `TR-sim-040` (always-on checks to a fixed record ring; asserts compile out of Release; `SimCounters`)
*(Requirement text: `docs/architecture/tr-registry.yaml`)*

**ADR Governing Implementation**: ADR-0001: Solution Layout, Assemblies, Build Configurations and Test Framework (§5)
**ADR Decision Summary**: Always-on checks write to a per-`SimWorld` `SimCheckRing` (capacity 256; records `{ushort code, uint tick, long a, long b}`; rate-limited to one record per code per 30 ticks with a suppressed count per code) in every build; it never throws, formats strings or allocates. `SimCounters` is a per-world `long[]` indexed by an append-only `ushort` `CounterId` enum, readable between ticks. Neither is ever static.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: `SimCheckRing` in `Core/`, per world, capacity 256, `{ushort code, uint tick, long a, long b}`, one record per code per 30 ticks + suppressed count; `SimCounters` in `Core/`, per world, `long[]` indexed by `CounterId` (`ushort`, appended, never renumbered); full-scan invariants are assert-only.
- Forbidden: an always-on check that throws, formats strings or allocates; static state; renumbering a `CounterId`.
- Guardrail: `SimCheckRing` ≈ 256 × 24 B per world; recording allocates 0 B.

---

## Acceptance Criteria

*Derived from ADR-0001 §5 and GDD R31 (the record and counter mechanics every later "counted X" / "one record" AC relies on), scoped to this story:*

- [x] **AC-1 Counters.** A new `SimCounters` reads 0 for every `CounterId`; `Add(id)` increments by exactly 1 and `Add(id, n)` by n; two `SimCounters` instances are independent; `CounterId` values are explicit and a test pins the first block (the Story 003 primitive block: `FixDivZero`, `FixClamped`, `MoneyClamped`, `MoneySaturated`, `FxClamped`, `MoneyBadPermille`) so they can never be renumbered; `SimCounters` hands out a `SimCounterSink` over that block whose increments are visible through the indexer.
- [x] **AC-2 Record ring rate limit.** Recording code C at ticks 0, 10, 29, 30, 31, 60 stores records at ticks 0, 30 and 60 only, and C's suppressed count reads 3; a different code at tick 10 is stored independently; records keep their `a`/`b` payloads.
- [x] **AC-3 Ring capacity and safety.** With capacity 256, 300 distinct (code, tick) records keep the newest 256 in order with an overwritten-count of 44; `Record` allocates 0 B after construction, never throws (including for the maximum code value), and builds no string.

---

## Implementation Notes

*Derived from ADR-0001 §5 and Key Interfaces:*

- `src/PerformativeMail.Sim/Core/SimCounters.cs`: `public sealed class SimCounters { public void Add(CounterId id, long n = 1); public long this[CounterId id] { get; } public SimCounterSink PrimitiveSink(); }` backed by `long[CounterIdCount]`. Readable between ticks; no reset API needed now.
- `src/PerformativeMail.Sim/Core/CounterId.cs`: `public enum CounterId : ushort` with explicit values. Seed it with the counters later stories need (append-only from here on): the primitive block first (values 0–5, matching Story 003's `PrimitiveCounter` — `MoneyBadPermille = 5` was appended at Story 003 close, 2026-10-16, per GDD F7; size the sink slice to `SimCounterSink.RequiredLength` (6) and add a test that `PrimitiveSink().Length >= RequiredLength`), then e.g. `BadTick`, `BusyPlayer`, `BusySystem`, `EnqueueBadState`, `EnqueueDuringFlush`, `DrainBacklog` (a level, see Story 020), `EventsDropped`, `EventsOutsideTick`, `EventsAtTickEnd`, `EventsDuringFlush`, `CoalesceLate`, `DumpDuringTick`, `IdAllocIllegal`, `RngIndexOutOfRange`, `StopOutOfPhase`. Each later story may append; none may renumber.
- `src/PerformativeMail.Sim/Core/SimCheckRing.cs`: `public sealed class SimCheckRing { public SimCheckRing(int capacity = 256, int rateLimitTicks = 30); public void Record(CheckCode code, uint tick, long a, long b); public int Count { get; } public SimCheckRecord this[int i] { get; } public uint Suppressed(CheckCode code); public uint Overwritten { get; } }`. Preallocate the record array and per-code `lastTick`/`suppressed` arrays sized to the `CheckCode` enum range. ADR-0001's sketch types `code` as `ushort`; a `CheckCode : ushort` enum is that type with names — keep the stored field `ushort`.
- `CheckCode` (append-only, explicit values): seed with `TickHitch`, `IdExhausted`, `BusySystem`, `OpSeqExhausted`, `BadTick`; later stories append.
- Rate limit: store when the code has never been recorded or `tick − lastTick ≥ 30` (compute in `long`); otherwise increment the code's suppressed count. Do not use `checked` overflow as control flow.
- Both classes are constructed by the composition root (Story 025) and injected; nothing static. `SimCounterSink` (Story 003) is a `ref struct` over the primitive block's `Span<long>`.
- Release counters vs Debug asserts: callers assert first (`SimAssert`), then count — in Release only the count happens. This story only provides the mechanism.

---

## Out of Scope

- Story 001: `SimAssert`, `Sim.AssertsEnabled`.
- Story 024: the `TickHitch` record written by `FrameRunner`.
- Dev Tooling overlay that displays counters/records (seed §3.16b) — later.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/SimCountersTests.cs`, `tests/unit/sim/Core/SimCheckRingTests.cs` — must exist and pass

**Status**: [x] Created — tests/unit/sim/Core/SimCountersTests.cs, SimCheckRingTests.cs

---

## Dependencies

- Depends on: Story 003 (`SimCounterSink`, primitive counter block)
- Unlocks: Story 009, Story 010 (hitch counters are driver-local but the record is here), Story 011 onward (every "counted"/"record" outcome)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 counters + pinned primitive block 0–5 cross-checked against `PrimitiveCounter`; AC-2 rate limit 0/30/60 stored, suppressed 3; AC-3 300 distinct (code, tick) → newest 256 in order, Overwritten 44, 0 B, max code safe)
**Values pinned**: `CounterId` 0–5 primitive block, then BadTick 6, BusyPlayer 7, BusySystem 8, EnqueueBadState 9, EnqueueDuringFlush 10, DrainBacklog 11, EventsDropped 12, EventsOutsideTick 13, EventsAtTickEnd 14, EventsDuringFlush 15, CoalesceLate 16, DumpDuringTick 17, IdAllocIllegal 18, RngIndexOutOfRange 19, StopOutOfPhase 20. `CheckCode` None 0 (reserved), TickHitch 1, IdExhausted 2, BusySystem 3, OpSeqExhausted 4, BadTick 5. `CheckCode` is a separate space from `SimAssertCode`.
**Deviations**: ADVISORY — `CheckCode.None = 0` reserved (no ADR pins values); undefined codes share one overflow rate-limit slot; `Add` saturates both ways and accepts negative n (DrainBacklog is a level); unknown `CounterId` dropped/reads 0; tick going backwards re-arms a code; indexer/constructor throw only on read/construction paths. Array sizes derive from the enum maximum in the constructor (append-safe). Risk LOW kept: Godot-free code (VERSION.md HIGH concerns Godot APIs).
**Test Evidence**: Logic — tests/unit/sim/Core/SimCountersTests.cs (15), SimCheckRingTests.cs (20); suite 369: Release 369/369, Debug 368 + 1 skipped, net10.0 and net8.0; Release build 0 warnings; independent net10.0 Release run 367/367 before revisions.
**Run result**: N/A — Godot-free diagnostics mechanism; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (revised: enum-derived sizing + guard tests; `Record` split into ShouldStore/Append); QL-TEST-COVERAGE ADEQUATE (300-record test now varies codes).
