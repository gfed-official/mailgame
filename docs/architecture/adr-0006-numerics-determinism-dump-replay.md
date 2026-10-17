# ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
- Author: `/architecture-decision` (autonomous run, `--review lean`), on behalf of technical-director.
- Inputs: Simulation Core GDD (R27–R32, F7), the TD seed `simulation-core-adr-inputs.md` §1 (review corrections), §3.6–§3.9 and §3.14, the registry (`moneyRounding`, `determinismTiers`), Grid Inventory (H-DET-1/2, R19), and the Mail, Throwing, Movement and Input determinism TRs.
- Engine validation: godot-specialist (Step 5.5). TD-ADR skipped (lean mode).
- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
The Sim must give identical state for identical inputs in three settings:
- two instances on one machine (T1);
- Windows and Linux for shared pure functions (T2);
- Debug and Release.

It must also allocate nothing per tick. This ADR fixes:
- the integer unit system (mm, permille, ticks, `Cents` ±2^62, `Fx16`, BAM + Q15);
- one rounding toolkit (`MulPermille` ≡ `MulDivRH(c, pm, 1000)` clamped; `MulDiv` truncating; an overflow-free `MulDivRH`);
- the checked/unchecked rules;
- the determinism checklist and its analyzer;
- the canonical little-endian state dump hashed with SHA-256 in tests;
- the in-memory op/sample recorder and replay;
- the zero-allocation tick gate.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Core (Godot-free `PerformativeMail.Sim` assembly) |
| **Knowledge Risk** | LOW for the Sim: pure .NET `long`/`Int128`/`UInt128` arithmetic and `BinaryPrimitives`, with no Godot API. **MEDIUM** for the 0-alloc and T2 claims inside an exported Godot build, because the runtime the 4.7.1 .NET export loads is NOT SOURCEABLE (Q-ARCH-02). |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md` (4.4→4.5: .NET 8 minimum on desktop), `deprecated-apis.md`, `current-best-practices.md` (C# practices for 4.7 NOT SOURCEABLE). No `modules/core.md` exists. |
| **Post-Cutoff APIs Used** | None. `Int128`/`UInt128` (.NET 7+) and `[InlineArray]` (.NET 8 / C# 12) are .NET APIs available on the net8.0 target, not Godot APIs. |
| **Verification Required** | (1) The SC-F7-1 and N-vectors below pass in Debug (checked) and Release. (2) SC-DET-1 (T1) at M-1a. (3) SC-DET-2 (T2) on Windows and Linux CI at M0. (4) SC-R32-1 (0 B/tick) at M0 under the test runner. In the Godot .NET export spike, a smoke check of `GC.GetAllocatedBytesForCurrentThread` and of one dump hash (both CoreCLR behaviours, UNVERIFIED under the export). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0005 (RNG streams). The dump records its stream tuple, and T2 cites its golden vectors. |
| **Enables** | ADR-0007 (the op recorder hook, and `checked` semantics for handler maths), ADR-0008 (the event payload encoding rules), ADR-0009/0017 (money and weight maths, conservation dump), ADR-0010 (StatSheet F1 uses `MulDivRH` and `Int128`), ADR-0013 (the BAM/Q15 table and `Fx16` carrier step; T2 PCM-P2-1), ADR-0015 (Throwing's floor-based RH, `CeilDiv`/`FloorDiv`, `isqrt`), ADR-0024 (persistence reuses `CanonicalWriter`). |
| **Blocks** | Sim Core "numerics primitives", "canonical dump", "recorder" and "0-alloc gate" stories (TR-sim-031..036, 045). The Grid Inventory H-DET-1 story (TR-inventory-028). The Mail money story (TR-mail-001). |
| **Ordering Note** | ADR-0001 implements this ADR's build-level mandates: Debug sets `CheckForOverflowUnderflow`, the Sim references BannedApiAnalyzers with the symbol list in §3, and no trimming. ADR-0003's builder must register `ISimStateContributor`s in the order §4 fixes. Neither ADR is a prerequisite for accepting this decision: the rules are stated here, and they apply. |

## Context

### Problem Statement
Spec 07 only says "fixed-point helpers" and "replay". The reviewed GDD fixes behaviour (R27–R32, F7), but leaves the mechanics to this ADR:
- the exact rounding algorithms;
- the overflow semantics under Debug `checked`;
- the dump encoding;
- the recorder format;
- the analyzer scope;
- the allocation measurement.

Without them, three things break:
- Debug and Release can produce different state: wrapping in one, throwing in the other;
- the 50 000-op determinism harnesses (H-DET-1, SC-DET-1) have nothing canonical to hash;
- money, weight and ballistic formulas in five GDDs can each round differently.

The review also found that the seed's `MulDivRH` overflows `Int128` and that `Math.Abs(long.MinValue)` throws. Both must be fixed before any code is written.

### Constraints
- **Assembly.** The Sim targets net8.0 with no Godot reference (ADR-0001). Tests run on .NET 10 (xUnit v3).
- **Floats.** `float`, `double` and `decimal` are banned in authoritative state and in anything that orders events (R27). They are allowed in Presentation and tools.
- **Overflow checks.** `checked` arithmetic fires at ±2^63, not at the money bound ±2^62.
- **Debug contexts.** A project-wide Debug `CheckForOverflowUnderflow` affects every non-`unchecked` expression.
- **Concurrency.** Two `SimWorld`s must coexist in one process (parallel xUnit, H-DET-1).
- **Allocation.** Zero allocation per tick after 300 warm-up ticks (R32), blocking from M0.

### Requirements
- **TR-sim-031.** Integer-only numerics (mm, permille, ticks, `Cents`, `Fx16`, BAM + Q15). `float`/`double`/`decimal` banned.
- **TR-sim-032.** The determinism checklist: no Dictionary iteration, stable sort, no Random/Guid/hash codes/time/threads/statics.
- **TR-sim-033.** T1: identical canonical dump hash per tick given the same inputs and logs.
- **TR-sim-034.** T2: Windows and Linux x64 bit-identical carrier step, RNG, hashes and shared pure functions.
- **TR-sim-035.** Canonical dump via `ISimStateContributor` (fixed order, little-endian); SHA-256 in tests only.
- **TR-sim-036.** `Tick()` allocates 0 B after 300 warm-up ticks; blocking from M0.
- **TR-sim-045.** The recorder stores ops and transform samples (in memory at M-1a, to a file at M5).
- **TR-inventory-015.** Integer weight→speed: permille, one division. `weightPoints` is int16.
- **TR-inventory-017.** Zero allocation on the automation path.
- **TR-inventory-028.** SHA-256 dumps are equal after a 50 000-op log on two instances.
- **TR-input-021.** The replay corpus (200 seeded + 10 adversarial) asserts zero unauthored verbs.
- **TR-movement-002.** mm int32, mm/s, permille, ticks; int64 before the first multiply; one final division.
- **TR-delivery-021.** Race suite and 10 000-seed soak.
- **TR-mail-001.** All money, permille and tick maths is integer, with int64 intermediates and one RH per formula.
- **TR-mail-012.** No wall-clock reads; pause freezes buckets.
- **TR-throwing-004.** Floor-based RH, `CeilDiv`/`FloorDiv` and `isqrt`; never truncating `/` on signed values.

## Decision

### 1. Units and types in authoritative state

| Quantity | Type | Range and rule |
|---|---|---|
| Money | `readonly struct Cents` (a plain struct with a private constructor and `FromCents` factory that range-checks, **not** a `record struct`, because `init` and `with` would bypass the bound) | **±2^62** (±4 611 686 018 427 387 904 ¢). Add and Sub are explicitly range-checked: Debug asserts; Release saturates to ±2^62 and counts `moneySaturated`. Negative balances inside the range are legal (Mail `wallet_floor` −500). The only rounding entry point is `Cents.MulPermille`. |
| Ratios | `int` permille (‰) | 0–10^6 for multipliers (a negative value asserts in Debug and is treated as 0 and counted in Release). Signed where a GDD defines a signed ratio. |
| Positions and distances | `int` millimetres | Stored and on the wire. Products widen to `long` before the first multiply (TR-movement-002). |
| Sub-mm integration | `readonly struct Fx16` (`long Raw`, private ctor) | 16 fractional bits. Domain **[−2^47, 2^47 − 65 536]**, so `ToMm` always fits `int` (engine validation: +2^47 would give `ToMm` = 2^31, which overflows `int`). `ToMm = (int)(Raw >> 16)` (arithmetic shift = floor), never `/ 65536`. `FromMm(int mm) = (long)mm << 16`, widened **before** the shift. Out-of-domain results are clamped to the domain and counted `fxClamped`. |
| Angles | `ushort` BAM (65 536 = 360°) | BAM addition and subtraction **wrap by design** (overflow class 1, written `unchecked((ushort)(a + b))`). Trig goes through a committed Q15 table (§6). |
| Ticks | `uint` tick; differences `long` | Absolute deadlines (R15). |
| Weight | `short weightPoints` | Grid Inventory F: weight→speed in permille with one division (TR-inventory-015). |
| Ids, counters | `uint` / `ushort` | ADR-0004; wrap-safe serial helpers for narrow ids. |

`Throwing`'s former "spread in mrad" unit is **retired** (`throwNoDice`). Sim Core R27's "Throw spread is in mrad" note is stale (GDD impact).

### 2. Rounding toolkit (`Sim/Primitives/Fix.cs`, `Cents.cs`, `Fx16.cs`)

All of these are pure, allocation-free and non-throwing.
- **Faults.** A zero divisor is a fault: the helper returns 0 and counts `fixDivZero`, and Debug asserts.
- **Clamps.** A result outside the domain is clamped and counted (`fixClamped`, `moneyClamped`, `fxClamped`).
- **Counters.** They are reached through an injected `SimCounters` reference passed by the caller, or a `ref` overload in tests. There are no statics.

| Helper | Definition | Domain clamp |
|---|---|---|
| `Fix.MulDiv(a, b, c)` | `trunc((Int128)a·b / c)` (truncation toward zero; `weightSpeed` keeps this) | ±(2^63 − 1) |
| `Fix.MulDivRH(a, b, c)` | `a·b/c` rounded **half away from zero**, by the **overflow-free algorithm** below | ±(2^63 − 1); typed wrappers clamp narrower |
| `Fix.FloorDiv(a, b)`, `Fix.CeilDiv(a, b)` | Floor and ceiling on signed `long` (Throwing TR-throwing-004) | n/a |
| `Fix.ISqrt(ulong n)` | Integer floor square root (bitwise Newton, no float) | n/a |
| `Cents.MulPermille(c, pm)` | `≡ clamp(MulDivRH(c.Value, pm, 1000), ±2^62)`; clamp counted `moneyClamped` | ±2^62 |
| `Fx16.ToMm(fx)` | `fx.Raw >> 16` (floor) | n/a |

**Overflow-free `MulDivRH`** (seed §1 correction, normative):
```
if c == 0: count fixDivZero; return 0
neg = (a < 0) ^ (b < 0) ^ (c < 0)
ua, ub, d = |a|, |b|, |c| as UInt128      // |long.MinValue| computed as (UInt128)(ulong)(-(v+1)) + 1 — never Math.Abs
n = ua * ub                               // < 2^126, cannot overflow UInt128
q = n / d;  r = n % d
if r >= d - r: q += 1                     // "2r ≥ d" without overflow
q = min(q, limit)  (count if clamped)     // limit = 2^63−1, or the typed domain
return neg ? -(long)q : (long)q
```

**"RH" everywhere in the GDDs means this function.** That covers:
- Mail `mailUnitPay`, `mailSpawnValue` and `misdeliveryPenalty` ("round half up on magnitude" in the registry = half away from zero);
- StatSheet F1's single rounding;
- Throwing's floor-based RH, written `FloorDiv(2·a·b + c, 2·c)` for positive divisors where the GDD says floor-based. ADR-0015 records which form each Throwing formula uses.

**One rounding per formula.** Multi-factor numerators accumulate in `Int128` and divide **once** (TR-mail-001, TR-movement-002, StatSheet F1).

**Numeric golden vectors (N-vectors).** These were computed 2026-10-16 with a C# `UInt128` implementation, identical in Debug (checked) and Release. `MulPermille` was also checked equal to `clamp(MulDivRH(c, pm, 1000), ±2^62)` over 10^6 random `(c ∈ ±2^62, pm ∈ 0..10^6)` pairs, with 0 mismatches.

| Call | Result | Counted |
|---|---|---|
| `MulPermille(15, 1500)` / `(−15, 1500)` | 23 / −23 | — |
| `MulPermille(1, 500)` / `(−1, 500)` / `(1, 499)` | 1 / −1 / 0 | — |
| `MulPermille(336, 1100)` | 370 | — |
| `MulPermille(2^62, 10^6)` | 2^62 | `moneyClamped` +1 |
| `MulPermille(−2^62, 1000)` | −2^62 | — (exactly on the bound) |
| `MulDiv(7, 1, 2)` / `(−7, 1, 2)` | 3 / −3 | — |
| `MulDiv(5, 5, 0)` | 0 | `fixDivZero` +1 |
| `MulDivRH(7, 1, 2)` / `(−7, 1, 2)` | 4 / −4 | — |
| `MulDivRH(5, 1, 3)` / `(4, 1, 3)` / `(−5, 1, −3)` | 2 / 1 / 2 | — |
| `MulDivRH(long.MinValue, long.MinValue, 1)` | 2^63 − 1 | `fixClamped` +1, no throw |
| `MulDivRH(long.MinValue, 1, −1)` | 2^63 − 1 | `fixClamped` +1 |
| `MulDivRH(long.MinValue, 1, 1)` | −(2^63 − 1) | `fixClamped` +1 |
| `MulDivRH(long.MinValue, long.MinValue, long.MinValue)` | −(2^63 − 1) | `fixClamped` +1 |
| `Fx16.ToMm(98 304)` / `(−98 304)` / `(−1)` | 1 / −2 / −1 | — |
| `Cents(2^62 − 1) + Cents(2)` | Debug: assert; Release: 2^62 | `moneySaturated` +1 |

### 3. Checked/unchecked rules and the determinism checklist

**Build mandate (implemented by ADR-0001).**
- Debug builds of the Sim compile with `CheckForOverflowUnderflow=true`.
- Release builds compile with it false, and they are the only configuration perf is measured in.
- `ReleaseAssert` is Release with `SIM_ASSERT` defined.
- **Godot configuration names.** The installed Godot.NET.Sdk 4.7.1 declares `Debug;ExportDebug;ExportRelease`, and a referenced plain-SDK Sim is built under the game's configuration name. `Directory.Build.props` must therefore key on those names:
  - `Debug` and `ExportDebug` → checked, with asserts;
  - `Release` and `ExportRelease` → unchecked, `Optimize=true`, no asserts.

  Asserts stay keyed on a custom property, never on `Configuration == Release` (ADR-0001). This was found by engine validation from the installed `Sdk.props`.

**Overflow classes (R28 rule 11):**
1. **Wrapping by design.** Hash, PRNG, serial-number (RFC 1982), checksum and **BAM angle** arithmetic. It **must** be inside `unchecked(...)`. This covers ADR-0005's primitives, `Serial*`, BAM add/sub, and `CanonicalHash64` when it is built.
2. **Domain-bounded quantities.** Money, `Fx16` and mm positions. These use an **explicit range check** (clamp and count, or saturate and count) through the typed helper. They never rely on `checked`, because the domains (±2^62, ±2^47) are narrower than `long`.
3. **Everything else.** This is the default context. An overflow is a bug: Debug's `checked` surfaces it as an exception (the world becomes `Faulted`, SC-ST-3), and Release wraps. Therefore **Debug and Release dumps are identical whenever no Debug check fires**, and the Debug CI suite is the overflow detector.
4. **Narrowing.** A narrowing cast to a wire or storage width goes through `WireNarrow.ToU16/ToU8/…`, which clamps and writes one record. Never a raw cast. The one exception is a value proven in range by its type. `(uint)class << 24` is written with the cast first.

**Determinism checklist (binding on all code in `PerformativeMail.Sim` that affects state, event order or the dump):**
1. No `float`, `double` or `decimal`.
2. Widen before multiplying. Ratios go through §2's helpers with the documented rounding, and there is one rounding per formula.
3. No iteration over `Dictionary`/`HashSet` (including `Keys`, `Values` and LINQ over them). They are lookup-only. Ordered iteration uses arrays or lists in ascending id or `opSeq`.
4. Removal from ordered lists is a stable compaction. No swap-remove where order matters.
5. No `Array.Sort`, `List.Sort`, `Span.Sort` or LINQ `OrderBy`. Use `Sim.Primitives.StableSort` (an insertion/merge sort with preallocated scratch), and every comparer ends with an `EntityId` or `opSeq` tie-break.
6. No `System.Random`, `Guid`, `string.GetHashCode`, `object.GetHashCode`, `HashCode` or `RuntimeHelpers.GetHashCode` in state or ordering.
7. No time source (`DateTime*`, `Stopwatch`, `Environment.TickCount*`, `TimeProvider`) and no `Thread`, `Task`, `Parallel`, `ThreadPool`, `Timer` or `async` in the Sim. Timing goes only through the injected `IPhaseProbe` (null by default, never read by logic). The Debug owner-thread assert (ADR-0002) uses `Environment.CurrentManagedThreadId`, which is not banned, rather than `Thread.CurrentThread`.
8. No static mutable state; only `static readonly` immutable tables. A reflection test asserts no non-readonly static field in the Sim assembly.
9. No culture-dependent text in state or the dump. All encoding uses explicit little-endian `BinaryPrimitives`.
10. Content def ids come from a sorted-key table at load (ADR-0010).
11. Overflow classes as above.

**Enforcement.** This section was revised after engine validation found the first draft overstated what BannedApiAnalyzers can catch. Two tools enforce the rules at build time.

- **Primary for rule 1: a small custom Roslyn analyzer** (`PerformativeMail.Sim.Analyzers`, `PrivateAssets=all`). It reports any operation, declaration, local, field, parameter, return type, cast or literal whose type's `SpecialType` is `System_Single`, `System_Double` or `System_Decimal`, plus `System.Half`.
  - It catches `var x = a * 1.5` and the `double` keyword, which symbol bans do not reliably catch.
  - It also hosts the rule-3 check (`foreach`, `GetEnumerator`, `Keys` or `Values` on `Dictionary`/`HashSet`, including when passed as `IEnumerable`) once written (GDD OQ10).
  - Until that rule-3 check ships, rule 3 is a review rule plus SC-DET-1.
- **`Microsoft.CodeAnalysis.BannedApiAnalyzers`** (RS0030) with `src/PerformativeMail.Sim/BannedSymbols.txt`. It uses **documentation-comment IDs without wildcards**: name-only member IDs cover every overload. The list:
  - `T:System.Random`;
  - `M:System.Guid.NewGuid`;
  - `M:System.Object.GetHashCode`, `M:System.String.GetHashCode` and `T:System.HashCode`;
  - `T:System.DateTime`, `T:System.DateTimeOffset`, `T:System.Diagnostics.Stopwatch`, `P:System.Environment.TickCount` and `P:System.Environment.TickCount64`;
  - `T:System.Threading.Thread`, `T:System.Threading.Tasks.Task`, `T:System.Threading.Tasks.Parallel` and `T:System.Threading.Timer`;
  - `M:System.Array.Sort`, `M:System.Collections.Generic.List`1.Sort` and `M:System.MemoryExtensions.Sort`;
  - `M:System.Linq.Enumerable.OrderBy`, `M:System.Linq.Enumerable.OrderByDescending`, `M:System.Linq.Enumerable.ThenBy` and `M:System.Linq.Enumerable.ThenByDescending`.

  RS0030 is made an error explicitly (`dotnet_diagnostic.RS0030.severity = error` in `.editorconfig`). It does not rely only on `TreatWarningsAsErrors`.
- **Every banned item has a fixture** under SC-ARCH-2 that must be **seen failing** before the gate counts. The fixtures include:
  - `var x = 1.5;`
  - a `double` field;
  - an explicit `GetHashCode()` call on a type that overrides it (whether RS0030 matches overrides is NOT ASSESSED, so the fixture decides);
  - a `readonly record struct` declared in the Sim.

  Synthesised record members have no syntax, so they are expected **not** to trip RS0030, and their bodies use `EqualityComparer<T>.Default`. That is why `Cents` and `Fx16` are plain structs, and `record struct` is allowed only for ids that never appear in ordering or hashing.
- **What is caught where.** Rules 1, 5, 6 and 7 fail the build once their fixtures pass. Rules 2, 3 (until the custom check), 4, 8, 9 and 10 are enforced by review, SC-DET-1 and the static-state reflection test.

### 4. Canonical state dump

- **Interface.** Every system with state binds `ISimStateContributor { ushort SectionId { get; } void Write(ref CanonicalWriter w); }` at build time.
- **Writer.** `CanonicalWriter` is a forward-only little-endian writer. It **buffers each section internally** and flushes `sectionId`, `byteLength` and the payload to the caller's `IBufferWriter<byte>` at `EndSection`, because an `IBufferWriter` cannot be patched after `Advance`. The internal buffer is reusable, and the dump is never on the tick path.
- **Encoding (format version 1):**
  - **File header.** `"PMSD"` (u32 LE `0x44534D50`), `formatVersion` u16 = 1.
  - **Sections** in **builder registration order** (ADR-0003 must preserve it). Each section is `sectionId` u16, `byteLength` u32, then the payload. `SectionId`s are declared constants, unique, and never reused.
  - **Fields** are written one by one at fixed width: `WriteU8/U16/U32/U64/I32/I64/Bool(u8 0/1)`. **Never** a raw struct blit, because padding and union tails would leak. Op and event payload bytes are written as their 48 declared bytes, which ADR-0007/0008 require to be zero-initialised.
  - **Collections** are a `u32 count` followed by elements in **ascending id** (or `opSeq` / slot order where the collection has no id). Insertion order must never leak (SC-R30-1).
  - **No strings.** `StringId` is a u32. Content is identified by the `ContentSet` hash in the recorder header, never dumped.
- **Sim Core section (`SectionId` 1) contents:**
  - `CurrentTick`;
  - every `EntityId` class counter (the 0x01–0x08 classes and any test class in use, ascending class);
  - the `mailId` counter and `opSeq`;
  - every live RNG stream (ADR-0005: `nameOrdinal` u16, `index` u32, `state` u64, `inc` u64, `draws` u64, in `RngNames` order then index);
  - the op queue contents (each queued op's 64 B field by field, plus its enqueue boundary tick);
  - the transform-sample latch;
  - the `slot ↔ playerId` table.
- **Excluded:** perf counters, `SimCounters`, the record ring, caches, the event log (empty between ticks), and anything derivable.
- **Validity.** `bool SimWorld.TryWriteDump(IBufferWriter<byte>)` succeeds only **between ticks** (after `Tick()` returns) on a `Ready`, `Running` or `Stopped` world. Inside `Tick()` (including from the sink) it returns false and counts `dumpDuringTick`. A `Faulted` world returns false.
- **Hashing.** Tests and tools take SHA-256 of the **dump**. The Sim never hashes its own state, and nothing in the tick path hashes. `CanonicalHash64` is the exception, once a cross-machine checksum needs it.
  - **Cross-ADR reconciliation.** ADR-0010 computes `ContentHash` with the BCL's managed SHA-256 inside the Sim, at **load time** (outside any `SimWorld`). This is permitted: the determinism concern is state hashing in the tick, not the BCL reference. GDD R30's "the Sim has no cryptography reference" is therefore stale; see GDD Impact.
- **`CanonicalHash64`.** (`Mix64` folded over 8-byte LE words) is built only when a cross-machine checksum first needs it.
- **Separate artefacts.** JoinState and RejoinState (ADR-0022) and the Free Play save (ADR-0024) are separate artefacts that may reuse `CanonicalWriter`.

### 5. Determinism tiers

| Tier | Guarantee | Proof (milestone) |
|---|---|---|
| **T1, same machine** | Two `SimWorld`s with the same `ContentSet` hash, `SimSettings` canonical encoding, `runSeed`, applied-op log and ingested-sample log give identical dumps after **every tick**. Harnesses needing "after each op" apply one op per tick; there is no test seam in production code. | SC-DET-1 (M-1a; two worlds in parallel, 50 000 ops; a third world with another seed diverges); Grid H-DET-1 (TR-inventory-028); Input replay corpus (TR-input-021); Manual Delivery race soak (TR-delivery-021) |
| **T2, cross-platform (Windows and Linux x64)** | Bit-identical: ADR-0005 primitives, §2 helpers, the Q15 table, `OwnedCarrier.Step` (ADR-0013), shared pure functions (`Evaluate`, ballistic, `holdEff`, StatResolve) and the canonical encoding of any checksum both sides compute. | SC-DET-2 (M0 CI: ADR-0005 V1–V6, N-vectors, PCM-P2-1 600-tick script, Q15 table hash), run on the **net8.0** target on both OSes; net10.0 is an extra leg |
| **Not required** | Clients never lockstep or re-simulate the world. World generation is not regenerated on clients (spec 13 §3). | — |

### 6. BAM and the Q15 trig table
- **Table.** `Sim/Primitives/TrigQ15.cs` holds a committed `static readonly short[4096]` sine table, indexed by `bam >> 4`, with values `round-half-away(32767 · sin(2π·i/4096))`. It is generated **once** by `tools/GenTrigQ15` (tools may use `double`) and committed as source.
- **Cosine** is `Sin(bam + 16384)`. Interpolation (if any) and `atan2` belong to ADR-0013.
- **Hash test.** A test asserts the SHA-256 of the table's LE bytes equals the value recorded in the generating story, and it runs in SC-DET-2.

### 7. Recorder and replay
- **Interface.** `IOpRecorder` (optional, injected; none in perf runs):
  - `OnEnqueued(uint boundaryTick, in Op op)`, after `opSeq` is stamped;
  - `OnDrained(uint applyTick, in Op op, in Reply reply)` for every drained op, accepted or rejected (ADR-0007);
  - `OnSample(uint receivedTick, PlayerSlot slot, in TransformSample s)` at the latch drain;
  - `OnOverflow(uint tick)`.
- **M-1a `InMemoryRecorder`.** Preallocated, growable **only outside the tick** (it is not under the 0-alloc gate, which runs with no recorder, R32).
  - **Header:** `formatVersion` u16; `ContentSet` hash (32 B); the SHA-256 of the `SimSettings` canonical encoding (which includes `maxOpsDrainedPerTick`, capacities and reserves); `runSeed` u64; initial counters (tick, `opSeq`, class counters, `mailId`).
  - **Body:** ordered records.
- **Replay.** `ReplayDriver` builds a world from the header and refuses on any mismatch (no world is built; GDD edge case). It then:
  - re-enqueues each op immediately before the tick numbered its `boundaryTick`;
  - re-latches samples at their `receivedTick`;
  - ticks;
  - asserts each op's recomputed apply tick equals the recorded one;
  - optionally compares per-tick dump hashes with a recorded hash track.
- **Overflow.** A recording that saw `OnOverflow` is marked **non-reproducible** (capacity knobs decide which events drop), and the replay reports it rather than failing.
- **File format.** The file writer is deferred to **M5** (spec 06 §9). It will serialise the same records with `CanonicalWriter`.

### 8. Zero-allocation tick
- **Gate.** After `warmupTicks` = 300, `SimWorld.Tick()` allocates **0 B** on its thread, measured with no recorder (R32, SC-R32-1).
- **Banned in the tick path:**
  - LINQ, lambdas and closures, method-group delegate creation;
  - `foreach` over interface-typed collections, struct enumerators boxed as `IEnumerable`;
  - `params`, string concatenation or interpolation (including logging), boxing, `Enum.ToString`/`Enum.GetValues`;
  - exceptions as control flow, `async`, `yield`.
- **Collections** are pre-sized from `SimSettings`. Growth is allowed only during warm-up, and a test asserts no growth afterwards.
- **Measurement.** `GC.GetAllocatedBytesForCurrentThread()` around 18 000 ticks in Release, in a **dedicated process** (because `GC.CollectionCount(0)` is process-wide), with tiered compilation settings pinned for the run.
- **Milestones.** Advisory at M-1a; **blocking from M0**.
- **Runtime.** The gate runs on the **net8.0** target, the runtime the Sim ships on. .NET 9 and later do more JIT escape-analysis stack allocation, so a net10.0 run can report 0 B where net8.0 allocates. A net10.0 pass alone does **not** satisfy the gate. Locally (only .NET 10 runtimes installed) the net8.0 leg is reported **NOT RUN**, and CI installs the .NET 8 runtime. ADR-0001 adds `net8.0` to the test project's target frameworks.

### Architecture Diagram

```
             ┌──────────── PerformativeMail.Sim (net8.0, no Godot) ────────────┐
 Primitives: │ Cents  Fx16  Fix(MulDiv, MulDivRH, Floor/CeilDiv, ISqrt)        │
             │ TrigQ15  StableSort  Serial*  WireNarrow  CanonicalWriter       │
 Core:       │ SimWorld ──► contributors (registration order) ──► TryWriteDump │
             │    │ IOpRecorder (optional) ◄── EnqueueOp / drain / latch       │
             └────┼────────────────────────────────────────────────────────────┘
                  ▼
 Tests (net10.0): SHA-256(dump) per tick · ReplayDriver · golden vectors · 0-alloc (own process)
 Analyzer: BannedSymbols.txt (build fails) · reflection test (no static mutable)
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.Primitives;
public readonly struct Cents {                                         // plain struct: no init/with bypass
    public long Value { get; } private Cents(long v);
    public static Cents FromCents(long v, ref SimCounterSink c);       // range-checked
    public const long Max = 1L << 62;
    public static Cents Add(Cents a, Cents b, ref SimCounterSink c);   // range-checked, saturating in Release
    public static Cents Sub(Cents a, Cents b, ref SimCounterSink c);
    public static Cents MulPermille(Cents c, int pm, ref SimCounterSink k); // == clamp(Fix.MulDivRH(c, pm, 1000), ±2^62)
}
public readonly struct Fx16 { public long Raw { get; } public int ToMm(); public static Fx16 FromMm(int mm); } // domain [−2^47, 2^47−65536]
public static class Fix {
    public static long MulDiv(long a, long b, long c, ref SimCounterSink k);
    public static long MulDivRH(long a, long b, long c, ref SimCounterSink k);
    public static long FloorDiv(long a, long b); public static long CeilDiv(long a, long b);
    public static ulong ISqrt(ulong n);
}
public ref struct CanonicalWriter { public void WriteU8(byte v); public void WriteU16(ushort v); public void WriteU32(uint v);
    public void WriteU64(ulong v); public void WriteI32(int v); public void WriteI64(long v); public void WriteBool(bool v);
    public void WriteBytes(ReadOnlySpan<byte> v); public void BeginSection(ushort id); public void EndSection(); }

namespace PerformativeMail.Sim.Core;
public interface ISimStateContributor { ushort SectionId { get; } void Write(ref CanonicalWriter w); } // between ticks only
public interface IOpRecorder {
    void OnEnqueued(uint boundaryTick, in Op op);
    void OnDrained(uint applyTick, in Op op, in Reply reply);
    void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample);
    void OnOverflow(uint tick);
}
public interface IPhaseProbe { void Enter(ProbeScope s); void Exit(ProbeScope s); }      // null object by default
// SimWorld: public bool TryWriteDump(IBufferWriter<byte> output);
```
`SimCounterSink` is a thin `ref`-passed handle onto the world's `SimCounters` (no statics). Its exact shape is settled in the primitives story.

### Implementation Guidelines
- Authoritative code **must never** use `float`, `double`, `decimal` or `Half`. The analyzer fails the build.
- Every money multiply **must** go through `Cents.MulPermille`. Every ratio **must** go through `Fix.MulDiv` (truncate) or `Fix.MulDivRH` (half away from zero) as the owning GDD's formula states. There is **one** division per formula, over an `Int128` numerator.
- `Math.Abs(long)` **must never** be applied to a value that can be `long.MinValue`. Use the `UInt128` magnitude form.
- `Cents` add and sub **must** use the range-checked helpers, never raw `+`.
- Hash, PRNG and serial arithmetic **must** be `unchecked(...)`. All other arithmetic **must** stay in the default context, so Debug catches overflow.
- `Fx16 → mm` **must** be `>> 16`. Signed division **must** use `FloorDiv`/`CeilDiv` where a GDD says floor or ceiling.
- Contributors **must** write fields one by one, little-endian, with collections in ascending id. They **must never** blit structs or depend on insertion order.
- A dump **must only** be requested between ticks.
- Tick-path code **must** follow §8. New collections **must** be sized from `SimSettings` at build.
- Determinism tests **must** apply one op per tick when they need per-op hashes.

## Alternatives Considered

### Alternative 1: `decimal` or `double` for money and ratios
- **Description:** Use `decimal` for cents and `double` for permille.
- **Pros:** Familiar. Fewer helper functions.
- **Cons:** `double` is not bit-reproducible across compilers and JIT paths under every circumstance, and `decimal` is slow, allocation-free but 16 B, and still needs rounding rules. Both invite mixed-mode maths in ordering code.
- **Rejection Reason:** It violates R27 and T2, and the GDDs' money examples are integer RH.

### Alternative 2: Project-wide `checked` in all configurations
- **Description:** Make overflow always throw.
- **Pros:** Overflow is never silent.
- **Cons:** It throws inside the tick in Release (R31 forbids that). Hash and PRNG code would need `unchecked` anyway. It still does not catch the ±2^62 money bound. It has a perf cost on the hot path.
- **Rejection Reason:** The three-class rule gives Debug detection and Release continuity, with identical dumps when no check fires.

### Alternative 3: Fixed-point Q32.32 everywhere instead of mm/permille/`Fx16`
- **Description:** One universal fixed-point type.
- **Pros:** Uniform.
- **Cons:** Wire sizes double. GDD formulas and registry values are already expressed in mm, permille and cents. Rounding points would multiply.
- **Rejection Reason:** It mismatches every reviewed GDD formula.

### Alternative 4: Hash state with `CanonicalHash64` (non-crypto) in the Sim instead of SHA-256 in tests
- **Description:** An in-Sim fast hash.
- **Pros:** Usable at runtime.
- **Cons:** No current runtime consumer. It adds code under T2 before it is needed. It is weaker at catching collisions in test diffs.
- **Rejection Reason:** It is deferred to its first consumer (ADR-0005/OQ3 ruling). SHA-256 in tests needs no Sim reference.

### Alternative 5: Binary-blit dump (`MemoryMarshal.AsBytes` over state arrays)
- **Description:** Dump raw memory.
- **Pros:** Fast and short to write.
- **Cons:** Padding and union tails leak uninitialised or stale bytes, so T1 breaks. The layout depends on the runtime. Insertion order leaks.
- **Rejection Reason:** The seed review flagged it explicitly. Field-by-field writing is required.

## Consequences

### Positive
- **Shared rounding.** One rounding toolkit with golden vectors means Mail, StatSheet, Throwing, Movement and Inventory cannot drift.
- **Debug and Release agree.** Debug catches real overflow. Release never throws in the tick, and the dumps match.
- **Canonical, order-independent dump.** It enables H-DET-1, SC-DET-1, the replay corpus and Manual Delivery's soak with no test seams.
- **T2 is scoped.** It is limited to small pure surfaces, so it is achievable without a deterministic-float stack.
- **Build-time enforcement.** The analyzer turns most of the checklist into build errors, so review effort shifts to rules 2, 3 and 4.

### Negative
- **Explicit maths.** Every formula must be written with the helpers. This is more verbose than `*` and `/`.
- **Dictionary iteration.** It stays a review rule until the custom analyzer exists.
- **Contributor upkeep.** Each system's `ISimStateContributor` is extra code that must stay in sync with its state. A forgotten field silently weakens T1. *Mitigation:* SC-R30-1 mutation tests per included item.
- **Dedicated process.** The 0-alloc gate needs its own test process, which adds CI time.

## Risks
- **Risk: BannedApiAnalyzers does not flag C# keyword uses (`double x`) of banned types.** *Mitigation (applied after engine validation):* the custom `SpecialType` analyzer is the primary rule-1 check (§3), and SC-ARCH-2 ships a fixture per banned item that must be seen failing.
- **Risk: `Int128` and `UInt128` division performance on the hot path.** *Mitigation:* the helpers are used per formula, not per inner loop. Profile under SC-PERF-1, and add a `long` fast path where `|a·b| < 2^63` is provable.
- **Risk: the Godot export's runtime differs from the test runtime** (Q-ARCH-02, NOT SOURCEABLE). *Mitigation:* integer semantics are runtime-independent. Add one dump-hash smoke check to the export spike (ADR-0001).
- **Risk: replay header drift as `SimSettings` grows.** *Mitigation:* the header hashes the canonical `SimSettings` encoding, not a field list.
- **Risk: Q15 table regeneration on a different machine changes rounding.** *Mitigation:* the table is committed source and hash-tested. The generator is run once.
- **Risk: the Godot editor reloads the game assembly in a collectible `AssemblyLoadContext`** (engine-specialist recall, not sourced in `docs/engine-reference`). Statics and retained handles die with it. *Mitigation:* rule 8 (no static mutable state), no threads or timers in the Sim. Confirm in the ADR-0001 Godot .NET spike.
- **Risk: the test runtime hides allocations or behaviour of the shipping runtime** (net10 vs net8). *Mitigation:* the gates run on the net8.0 target (§5, §8). A missing .NET 8 runtime is reported NOT RUN, never as passed.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R27 numeric types; F7 rounding; SC-F7-1 | §1 units, §2 toolkit, N-vectors (TR-sim-031) |
| simulation-core.md | R28 determinism rules 1–11; SC-ARCH-1/2 | §3 checklist, overflow classes, analyzer list (TR-sim-032) |
| simulation-core.md | R29 T1/T2; SC-DET-1, SC-DET-2 | §5 (TR-sim-033, 034) |
| simulation-core.md | R30 canonical dump; SC-R30-1 | §4 (TR-sim-035) |
| simulation-core.md | R32 allocation-free tick; SC-R32-1 | §8 (TR-sim-036) |
| simulation-core.md | R8 recording; R29 sample log | §7 recorder (TR-sim-045) |
| grid-inventory.md | Integer weight→speed (one division); zero alloc on the automation path; H-DET-1 SHA-256 after 50 000 ops; H-DET-2 comparator laws | §1 `weightPoints` short, §2 `MulDiv`, §8, §4/§5, §3 rule 5 (TR-inventory-015, 017, 028) |
| input-controls.md | Replay corpus asserts zero unauthored verbs | §7 replay over recorded ops and samples (TR-input-021) |
| player-character-movement.md | mm int32, int64 before the first multiply, one final division; PCM-P2-1 T2 | §1, §2, §5 T2 (TR-movement-002) |
| manual-delivery.md | Race suite plus 10 000-seed soak | §5 T1 harness and §7 replay (TR-delivery-021) |
| mail-destinations.md | Integer money, int64 intermediates, one RH per formula; no wall clock | §2 `MulPermille`/`MulDivRH`, §3 rule 7 (TR-mail-001, 012) |
| throwing-catching.md | Floor-based RH, `CeilDiv`/`FloorDiv`, `isqrt`; never truncating `/` on signed values | §2 helpers (TR-throwing-004) |

## Performance Implications
- **CPU:** `MulDivRH` costs about 10–30 ns (a `UInt128` multiply and divide). It is called per formula evaluation, not per entity per tick in hot loops. The dump is tests and tools only, never in a shipped tick. The recorder is off in perf runs.
- **Memory:** the Q15 table is 8 KiB. The recorder is about 80 B per op (64 B op + ticks + reply) ≈ 4 MB per 50 000 ops, test-only at M-1a.
- **Load Time:** None.
- **Network:** None directly. The wire codecs are ADR-0011's. The units here fix wire widths (mm int32, BAM u16).

## Migration Plan
No code exists. Primitives stories create `src/PerformativeMail.Sim/Primitives/{Cents,Fx16,Fix,TrigQ15,StableSort,Serial,WireNarrow,CanonicalWriter}.cs` `src/PerformativeMail.Sim/BannedSymbols.txt` and the `PerformativeMail.Sim.Analyzers` project. ADR-0001's build story wires `CheckForOverflowUnderflow` (Debug), the analyzer and no-trim.

## Validation Criteria
- SC-F7-1 and every N-vector row pass in Debug and Release (M-1a).
- SC-ARCH-2: each banned-symbol fixture is seen failing the build. A grep finds no `SceneTree.Paused` in the Sim or `GameLoop`.
- SC-DET-1 (T1) passes at M-1a. SC-DET-2 (T2) passes on Windows and Linux CI at M0.
- SC-R30-1: every included item changes the hash, and no excluded item or insertion reordering does. An in-tick request returns false and counts.
- SC-R32-1: 0 B over 18 000 ticks in Release in a dedicated process, from M0.
- A replay of a recorded 50 000-op session reproduces every apply tick and dump hash (story test, M-1a in memory).

## GDD Impact (no GDD edited by this ADR)
- **simulation-core.md R27** "Throw spread is in mrad": stale since `throwNoDice` retired spread (already listed in planning-run residual conflicts, Sim Core ↔ Throwing).
- **simulation-core.md F7 / registry `moneyRounding`.** "RH = round half up on magnitude" and "half away from zero" are the same function. This ADR makes `MulPermille ≡ clamp(MulDivRH(c, pm, 1000), ±2^62)` normative.
- **Seed §3.14** wording ("a result outside `long` is clamped") is superseded by the domain clamps in §2.
- **simulation-core.md R30** ("The Sim has no cryptography reference") conflicts with ADR-0010's load-time managed SHA-256 `ContentHash`. This ADR rules that the constraint is "no state hashing in the tick path". R30's wording should change at re-review.
- **simulation-core.md F7 / R27** "`Fx16` range ±2^47" is narrowed to [−2^47, 2^47 − 65 536] so `ToMm` fits `int`. No GDD example changes.

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
- **BLOCKING 1, applied.** The 0-alloc gate and T2 must run on the net8.0 shipping runtime (§5, §8).
- **BLOCKING 2, applied.** The analyzer claims were overstated.
  - The custom `SpecialType` analyzer is now primary for float/double/decimal.
  - `BannedSymbols.txt` uses wildcard-free IDs and adds `MemoryExtensions.Sort`.
  - Every banned item gets a fixture that must be seen failing.
  - RS0030 is set to error in `.editorconfig`.
- **Minor findings, applied:**
  - `Cents` and `Fx16` are plain structs, not records;
  - the `Fx16` domain and `FromMm` widening;
  - BAM is in overflow class 1;
  - section-buffered `CanonicalWriter`;
  - `Environment.CurrentManagedThreadId` for the owner-thread assert;
  - the `BannedSymbols.txt` path is `src/PerformativeMail.Sim/`;
  - Godot `ExportDebug`/`ExportRelease` configuration mapping, from the installed Godot.NET.Sdk 4.7.1 `Sdk.props`;
  - the collectible-ALC risk.
- **Not assessed:**
  - whether RS0030 matches calls to overriding `GetHashCode` (a fixture decides);
  - Godot export runtime behaviour (Q-ARCH-02).
- **TD-ADR:** skipped (lean mode).

## Related
- ADR-0005 (RNG): the stream dump tuple and the T2 vectors.
- ADR-0001 (build): implements the Debug `checked`, analyzer and no-trim mandates.
- ADR-0003 (composition): contributor registration order.
- ADR-0007 (ops): the recorder hooks and the zero-initialised op payload.
- ADR-0008 (events): the zero-initialised entry payload.
- ADR-0013 (carrier): the Q15 table consumer and the `OwnedCarrier.Step` T2 surface.
  - **Erratum (recorded 2026-10-16 on ADR-0013's acceptance):** the carrier uses exact tick-rational remainders, not `Fx16` (ADR-0013 §2), because `Fx16` cannot reproduce PC F1's 166/167/167 or F3's apex golden. The Enables entry "`Fx16` carrier step" reads accordingly. §1's `Fx16` row stands for every other sub-mm use; T2 is unchanged.
- `design/gdd/simulation-core.md` R27–R32, F7. Seed §1, §3.6–§3.9, §3.14 (consumed by this ADR).
