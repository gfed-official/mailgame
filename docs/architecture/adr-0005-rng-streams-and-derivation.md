# ADR-0005: RNG Streams and Derivation

## Status
Accepted

## Date
2026-10-16

## Last Verified
2026-10-16

## Decision Makers
- Author: `/architecture-decision` (autonomous run, `--review lean`), on behalf of technical-director.
- Inputs: Simulation Core GDD (R23–R26, F5, F6), the TD seed `simulation-core-adr-inputs.md` §1 and §3.13, Mail & Destinations, Throwing & Catching, and the registry.
- Engine validation: godot-specialist (Step 5.5). TD-ADR skipped (lean mode).
- **Acceptance (2026-10-16):** Accepted by technical-director on the project owner's explicit confirmation, given to the main session on 2026-10-16 via AskUserQuestion (answer: "Accept all 11 (Recommended)", batch ADR-0001…0011), per the acceptance authority in `.claude/skills/architecture-decision/CONTRACT.md`. Dependency check passed (every Depends On ADR Accepted first); no stories were waiting on it (`production/epics/` does not exist yet).

## Summary
The Sim needs reproducible, independent random streams that are bit-identical on Windows and Linux and in Debug and Release. One per-owner stream must never perturb another (for example, a Mail re-roll must never move a Lost Parcel).

This ADR fixes the following:
- the generator: PCG32 XSH-RR;
- the seeding: `Derive = Mix64(Mix64(runSeed ^ Fnv1a64(name)) ^ index)`, with `Mix64` = the full SplitMix64 step;
- a single-draw bounded integer;
- a compile-time stream-name registry;
- owner-only handles;
- committed golden vectors, which the first RNG story asserts verbatim.

Throwing has no stream.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Godot 4.7.1 (C#, .NET 8 minimum for desktop) |
| **Domain** | Core (Godot-free `PerformativeMail.Sim` assembly) |
| **Knowledge Risk** | LOW for this decision. It uses only integer arithmetic in plain .NET and touches no Godot API. The engine as a whole is HIGH risk (VERSION.md: 4.4–4.7 are post-cutoff), but nothing here depends on it. |
| **References Consulted** | `docs/engine-reference/godot/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md`. None lists a change relevant to integer maths or `System.*` numeric types. No `modules/core.md` exists. |
| **Post-Cutoff APIs Used** | None. |
| **Verification Required** | (1) SC-F5-1 golden vectors pass in Debug (`CheckForOverflowUnderflow=true`) and Release, on both the net8.0 (shipping) and net10.0 runtimes. (2) SC-DET-2 repeats them on Windows and Linux x64 CI. (3) Once the Godot .NET export spike exists (Q-ARCH-02), one smoke run of the vectors inside an exported build: the runtime the export loads is NOT SOURCEABLE. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None. The primitives (`Fnv1a64`, `Mix64`, `Pcg32`, `RngDerive`, `RngStream`) are self-contained integer code in `Sim/Primitives` and `Sim/Core`, and they can be implemented and golden-tested with no other ADR in place. |
| **Enables** | ADR-0006: its canonical dump records every live stream as `(name, index, state, inc, draws)` and its T2 tier cites these vectors. ADR-0017 (Mail storage) and the Mail manifest roller (TR-mail-002..004). Future World Generation, Enemy AI, Perks, Shop and Stamps streams. |
| **Blocks** | The Sim Core "RNG primitives" story (TR-sim-027..030) and the Mail manifest story (TR-mail-002..004). |
| **Ordering Note** | Two pieces are implemented under other ADRs and are not prerequisites for this one: owner-only hand-out of a stream handle is wired by the composition root (ADR-0003), and the dump format for stream state is ADR-0006's. This ADR states the rule each of them must follow. |

## Context

### Problem Statement
Several systems need randomness:
- Mail's per-shift manifest and its Lost Parcels;
- world generation stages;
- waves and mega drops;
- loot drops;
- perk drafts;
- the shop;
- run events.

The spec (07 §4.3) only says "RngStream(seed, name) wrappers over PCG32". It names `mail.spawn` / `mail.address` streams that Mail no longer uses, and it fixes no seeding, no bounded-draw method, and no test vectors.

Without one fixed derivation, three things break:
- two machines (T2) or two builds (Debug `checked` vs Release) can disagree;
- a change to one system's draw count can shift another system's results;
- Mail's contract that a Prep re-roll reproduces the same manifest, and never moves a Lost Parcel, cannot be tested.

The decision is needed now: it is Foundation-critical for the first Godot-free sprint (architecture §Required ADRs), and Mail's M-1a manifest roller depends on it.

### Constraints
- **Integer only.** `float`, `double` and `decimal` are banned in the Sim (ADR-0006), so there is no float RNG.
- **Overflow context.** Debug builds compile the Sim with `CheckForOverflowUnderflow=true` (ADR-0006, seed §1). Hash and PRNG arithmetic wraps by design, so it must be explicitly `unchecked`, or Debug throws and the Debug and Release dumps differ.
- **Forbidden sources.** No `System.Random`, `GetHashCode` or `Guid` (Sim Core R23, R28 rule 6).
- **No allocation.** A draw must be allocation-free (R32).
- **Separate instances.** No static mutable state: two `SimWorld`s in one process must have independent streams (R28 rule 8).
- **Player-text safety.** Stream names must never come from player text (R23).

### Requirements
- **TR-sim-027.** PCG32 XSH-RR; `Derive` via `Mix64`/`Fnv1a64`; all of it `unchecked`.
- **TR-sim-028.** A compile-time `RngNames` registry and the v1 stream list. `mail`, `mail.special` and `events` are indexed per shift.
- **TR-sim-029.** `NextInt(b) = (next32·b) >> 32`, one draw per call. No float RNG. Owner-only handles.
- **TR-sim-030.** Committed golden vectors for `pcg32`, `Mix64`, `Fnv1a64` and `Derive`.
- **TR-mail-002/003/004.** The manifest is a pure function of `(seed, shift, profile, addresses)` on stream `mail`. The draw count is fixed: `K = 2·nb + 2N + max(0, N−1)`, with no rejection loop. `mail.special` is a separate stream, so a re-roll never moves a Lost Parcel.
- **TR-throwing-002.** Throwing uses no RNG. Any future randomness in Throwing uses a registered stream indexed by a u32 launch ordinal, never by `flightId`.
- **Recreation.** Re-creating `(name, index)` reproduces the stream. One live handle per key.

## Decision

### 1. Primitives (bit-exact definitions)

All arithmetic is mod 2^64 (or mod 2^32 where typed `uint`). All of it is written inside `unchecked(...)`.

```
Fnv1a64(name):  h = 0xCBF29CE484222325
                for each UTF-8 byte b of name: h = (h ^ b) * 0x100000001B3
Mix64(x):       z = x + 0x9E3779B97F4A7C15                       // SplitMix64 gamma — part of Mix64
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
                z = (z ^ (z >> 27)) * 0x94D049BB133111EB
                return z ^ (z >> 31)
Derive(runSeed, name, index) = Mix64(Mix64(runSeed ^ Fnv1a64(name)) ^ (ulong)index)
initState = Mix64(s);  initSeq = Mix64(s ^ 0xDA3E39CB94B95BDB)     // s = Derive(...)
pcg32_srandom(initState, initSeq):
                state = 0; inc = (initSeq << 1) | 1; Step(); state += initState; Step()
Step():         state = state * 6364136223846793005 + inc
Next32():       old = state; Step()
                xs  = (uint)(((old >> 18) ^ old) >> 27)
                rot = (int)(old >> 59)
                return (xs >> rot) | (xs << ((32 - rot) & 31))
NextInt(b):     return (uint)(((ulong)Next32() * b) >> 32)       // exactly one draw; b = 0 → 0
```

**`Mix64` naming note.** The GDD and the registry call `Mix64` "the SplitMix64 finaliser". In fact it is the **full SplitMix64 output step, including the `+ 0x9E3779B97F4A7C15` gamma**. This is the formula in seed §3.13, and the GDD's own golden value `Mix64(0) = 0xe220a8397b1dcdaf` requires it: the bare finaliser maps 0 to 0. The definition above is normative.

### 2. Stream registry (`RngNames`)

`StreamName` is a `readonly struct` with a private constructor. The only instances are `static readonly` fields of `RngNames`, which is the single registry. Each entry declares the following:
- its ASCII name (lowercase, dot-separated);
- its owner (a system id used by the composition-root check);
- its allowed index range;
- its precomputed `Fnv1a64`, computed once at type initialisation from a constant string, never at draw time.

**v1 registry:**

| Name | Index range | Owner | Lifetime |
|---|---|---|---|
| `worldgen.<stage>` (one entry per stage, e.g. `worldgen.terrain`) | 0 | World Generation | Per stage |
| `mail` | 1–5 (shift) | Mail & Destinations | Recreated on demand; pure per shift |
| `mail.special` | 1–5 (shift) | Mail & Destinations | Recreated on demand |
| `waves` | 0 | Enemy AI & Waves | Run |
| `mega` | 0 | Enemy AI & Waves | Run |
| `drops` | 0 | Health & Damage / Enemy AI | Run |
| `perks` | 0 | Perks & Perk Draft | Run |
| `shop` | 0 | Economy, Wallet & Shop | Run |
| `events` | 1–5 (shift) | Stamps & Run Events | Recreated on demand |

Spec 07 §4.3's `mail.spawn` and `mail.address` are **retired**: Mail draws both from `mail` in a fixed order (DEV-5). Throwing has **no** entry (`throwNoDice`). If Throwing ever needs randomness, it adds an entry indexed by a per-run u32 launch ordinal, never by `flightId`; that is an amendment to this table.

Adding a stream is an amendment to this table plus one `RngNames` field. It never changes `Derive`.

### 3. Stream lifetime and ownership

- **Factory.** `RngRegistry` is per `SimWorld`, constructed at composition. It is the only factory: `RngStream RngRegistry.Create(StreamName name, uint index)`.
  - It keeps **one live handle per `(name, index)`**.
  - A second `Create` for the same key **re-seeds that same handle in place**: the state and inc are reset and `draws` is set to 0. It allocates nothing, and any older reference to the handle now draws the fresh sequence.
  - Streams are pre-allocated at composition for every registered `(name, index)` pair: 9 names, 30 keys in v1. This keeps `Create` allocation-free inside the tick.
- **Index range.** An out-of-range index (`mail` with shift 0 or 6) asserts in Debug. Release derives it anyway and counts `rngIndexOutOfRange`, using a spare slot (SC-R24-1).
- **Owner-only handles.** The composition root (ADR-0003) passes each system an `IRngStreams` view filtered to the names that system owns. Holding the view is the ownership check. An architecture test (SC-ARCH-1) asserts that no system's constructor receives a view containing another owner's names, and that Throwing receives none.
- **Draw counting.** Each `RngStream` counts `draws` (u64): +1 per `Next32` and per `NextInt`, including `NextInt(0)`.
- **Dump.** The dump (ADR-0006) records every live stream as `(name ordinal, index, state, inc, draws)`, in `RngNames` declaration order and then ascending index. A stream that was never created is recorded as absent.
- **Test seam.** Tests inject an `IRngSource` stub (`uint Next32()`) through the `RngStream` test constructor (Mail M-F4-1, SC-R25-1). Production code never sees the stub type.
- **Presentation.** Cosmetic randomness on clients (particles, flight spin wobble) uses a Presentation-side generator and never feeds the Sim. Flight spin is a pure function of `flightId` (Throwing HB3) and draws nothing.

### 4. Bounded draw contract

- **`NextInt(uint b)`** is the multiply-shift above. It consumes exactly one draw on every call, including `b = 0`, which returns 0 and asserts in Debug (R25).
  - The relative bias is at most b/2^32 (about 2.3 × 10^-9 for b = 10). This is accepted, because a fixed draw count is a contract (Mail `K`) and a rejection loop would break it.
- **There is no `int` overload.** GDD F6 mentions "an int overload asserts b ≥ 0", but engine validation found that literal arguments such as `NextInt(10)` would bind to an `int` overload and route every call through the assert. Integer constants convert implicitly to `uint`. A caller holding an `int` count writes `NextInt((uint)count)` after its own `count ≥ 0` guarantee; `List.Count` and array lengths are never negative.
- There is **no** `NextFloat`, `NextDouble`, `NextBool(p)` or range-with-rejection method.
  - A Bernoulli trial with probability p‰ is written `NextInt(1000) < p` (Mail's Cursed roll, `pairs_b` rounding).
  - A uniform choice over a list is written `list[NextInt((uint)list.Count)]`.

### Architecture Diagram

```
 SimSettings.RunSeed ──┐
                       ▼
 RngNames (static readonly, compile-time) ──► RngRegistry (per SimWorld, preallocated 30 handles)
   name, owner, index range, Fnv1a64            │ Create(name, idx): Derive → Mix64 → pcg32_srandom
                                                │ one live handle per key; re-create = in-place reset
                                                ▼
 Composition root (ADR-0003) ── IRngStreams view (owner-filtered) ──► Mail / Waves / Perks / …
                                                                      (Throwing: none)
 ADR-0006 dump ◄── (name ordinal, index, state, inc, draws) for each live stream
```

### Key Interfaces

```csharp
namespace PerformativeMail.Sim.Primitives;
public static class Fnv1a64Hash { public static ulong Hash(ReadOnlySpan<byte> utf8); }
public static class SplitMix  { public static ulong Mix64(ulong x); }          // full SplitMix64 step
public struct Pcg32 {                                                          // value type, no allocation
    public Pcg32(ulong initState, ulong initSeq);                              // pcg32_srandom
    public ulong State { get; } public ulong Inc { get; }
    public uint Next32();
}

namespace PerformativeMail.Sim.Core;
public readonly struct StreamName { /* private ctor; Ordinal, Name (UTF-8), Fnv, Owner, MinIndex, MaxIndex */ }
public static class RngNames { public static readonly StreamName Mail, MailSpecial, Waves, Mega, Drops, Perks, Shop, Events /*, Worldgen* */; }
public static class RngDerive { public static ulong Derive(ulong runSeed, in StreamName name, uint index); }
public interface IRngSource { uint Next32(); }                                 // test seam
public sealed class RngStream {                                                // one per (name, index), preallocated
    public StreamName Name { get; } public uint Index { get; } public ulong Draws { get; }
    public uint Next32();
    public uint NextInt(uint bound);   // one draw; bound 0 → 0 (Debug assert); no int overload
}
public interface IRngStreams { RngStream Create(in StreamName name, uint index); } // owner-filtered view
```

### Implementation Guidelines
- Every line of `Fnv1a64`, `Mix64`, `Pcg32.Step` and `Next32` **must** be inside `unchecked(...)`. That explicitly includes the `(uint)(((old >> 18) ^ old) >> 27)` narrowing, which truncates 37 bits and throws under Debug `checked`. The rotate **must** be written `(32 - rot) & 31`, with `rot` an `int`.
- The `Pcg32` field inside `RngStream` **must not** be `readonly`. A `readonly` field of a mutable struct makes each call operate on a defensive copy, so the state would never advance.
- `RngStream` **must** never allocate after composition. `Create` re-seeds a preallocated handle.
- `Fnv1a64` of a name **must** be computed once, when `RngNames` initialises, from a `"…"u8` literal. Names **must** never be built from runtime strings or player text.
- A startup test **must** assert that the `Fnv1a64` values of all `RngNames` entries are pairwise distinct (v1: verified distinct, see vectors). A seed-level collision across names is about C(50,2)/2^64 ≈ 7 × 10^-17 per run, which is accepted.
- A handler **must** draw only after validation passes (ADR-0007 validate-then-mutate). A rejected op changes no stream's `state` or `draws`.
- Systems **must** never hold a stream across a `Create` of the same key expecting the old sequence. Re-creation resets it.
- `System.Random`, `Random.Shared`, `GetHashCode`, `HashCode` and `Guid` **must** never be used for randomness in the Sim. The analyzer bans them (ADR-0006).
- The golden-vector test **must** live in `tests/unit/sim/Core/RngGoldenVectorTests.cs`, which `tests/PerformativeMail.Sim.Tests` compiles. It **must** assert every value in the table below verbatim, and **must** run in both Debug and Release.
- The test **must** also run on the **net8.0** runtime the Sim ships on, not only on the net10.0 test runtime. CI installs the .NET 8 runtime, and the test project gains a `net8.0` target (ADR-0001 implements this).
  - Locally only .NET 10 runtimes are installed (10.0.12/10.0.13). Without the .NET 8 runtime, the net8.0 leg is reported **NOT RUN**, never counted as passed.

### Golden vectors (committed; the implementation test asserts these verbatim)

All five values in Sim Core SC-F5-1 were **recomputed independently on 2026-10-16** and matched exactly. Two implementations were used:
- a Python reference (`python3 -I`, arbitrary-precision ints masked to 64 bits);
- a C# implementation run under .NET 10.0.402, in Debug with `CheckForOverflowUnderflow=true` (with `unchecked` blocks) and in Release.

The Debug and Release outputs were byte-identical. The vectors also match seed §3.13 and the registry entries `rngStreamDerivation` and `nextIntBounded`.

**V1 — PCG32 core** (`pcg32_srandom(42, 54)`, the PCG reference demo values):
`0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e`

**V2 — `Mix64`:**

| x | Mix64(x) |
|---|---|
| `0x0000000000000000` | `0xe220a8397b1dcdaf` |
| `0x0000000000000001` | `0x910a2dec89025cc1` |
| `0xffffffffffffffff` | `0xe4d971771b652c20` |

**V3 — `Fnv1a64`** (all v1 names pairwise distinct):

| Name | Fnv1a64 |
|---|---|
| `""` (empty) | `0xcbf29ce484222325` |
| `mail` | `0x1f5964a2ce98072e` |
| `mail.special` | `0x67c7e88750f3703b` |
| `waves` | `0xe5f7284c8d6e5825` |
| `mega` | `0x42dfd9a2e2f6fa13` |
| `drops` | `0x6cbe52c93e8b1bed` |
| `perks` | `0x4b06f5847096e54a` |
| `shop` | `0x46dc5e18e28e2409` |
| `events` | `0xdd63d0cec6bb7d04` |
| `worldgen.terrain` | `0xa4c307e2ae7e967c` |

**V4 — `Derive`, seeding, and the first five draws of `Create(runSeed, name, index)`:**

| runSeed, name, index | Derive | PCG state after srandom | inc | Next32 ×5 | NextInt(10) on those draws |
|---|---|---|---|---|---|
| 1, `mail`, 2 | `0x120344c795c4b10e` | `0x7572d87e0532cc6e` | `0xb564478f6f06f42b` | `0x915ab962, 0xc3f6d39c, 0x5d7b280d, 0x37138e0e, 0x1bf738c4` | 5, 7, 3, 2, 1 |
| 1, `mail`, 1 | `0x71928a09cee23ed8` | `0xb36582a2df730e15` | `0x3a5a6685d7fc12e3` | `0xd73dddb2, 0x5db76833, 0xbf9328bd, 0x09777055, 0x30103e80` | 8, 3, 7, 0, 1 |
| 1, `mail.special`, 2 | `0x569a97cfc8b20ef4` | `0x0f50dae756575348` | `0xd313692e46864483` | `0x750d9336, 0x666b05a2, 0xd99f6ef9, 0x46e06292, 0x0a60bcd6` | 4, 4, 8, 2, 0 |
| 0, `perks`, 0 | `0x717d64703c6111fe` | `0x9df2891a81d4a474` | `0x657fd083a7a917d7` | `0xb99897ca, 0x9f395f21, 0x20feb700, 0x180af528, 0x3ebc0465` | 7, 6, 1, 0, 2 |
| `0xdeadbeefcafef00d`, `waves`, 0 | `0xd89cad0d820c7269` | `0xdc009665b53eddaa` | `0x6777a27c2a10746f` | `0x02859650, 0xaecda658, 0x2c4199a8, 0xce36ca27, 0x9e237e0c` | 0, 6, 1, 8, 6 |
| 1, `events`, 5 | `0x95b59375007b3007` | `0xfc16d19ae40cf14a` | `0x0ea626dc54adc689` | `0x05bba7d5, 0x54c6df50, 0x0c424a61, 0xa19bb66d, 0x73e8a584` | 0, 3, 0, 6, 4 |

The `initState`/`initSeq` for row 1 are `0x56c4414fc45fe704` and `0xdab223c7b7837a15`.

**V5 — long run:** `Create(1, "mail", 2)` after 1 000 `Next32` calls has `state = 0xcac3e44cb2a5fa56`, and its 1 001st output is `0x3d6fc4ac`.

**V6 — `NextInt`** (from SC-R25-1 and F6):

| next32 | b | result |
|---|---|---|
| `0x80000000` | 10 | 5 |
| `0xFFFFFFFF` | 10 | 9 |
| `0x00000000` | 10 | 0 |
| `0xFFFFFFFF` | 0 | 0 (one draw consumed; Debug asserts) |
| `0xFFFFFFFF` | `0xFFFFFFFF` | `0xFFFFFFFE` |
| `0x12345678` | 65 535 | 4 660 |

## Alternatives Considered

### Alternative 1: `System.Random` (seeded) per stream
- **Description:** `new Random(seed)` per system.
- **Pros:** Zero code to write.
- **Cons:** The algorithm is not specified across .NET versions: the seeded legacy algorithm is kept for compatibility, but it is an implementation detail. It allocates a class. Its `NextDouble` and float paths invite floats. No fixed draw count for ranges (`Next(int)` behaviour is undocumented). Cross-version T2 is not guaranteed.
- **Rejection Reason:** It violates R23 and the T2 tier, and it cannot be golden-tested against an independent spec.

### Alternative 2: xoshiro256** / xoroshiro128+
- **Description:** A modern, faster generator with a 256-bit state.
- **Pros:** Excellent statistical quality. Fast.
- **Cons:** 4× the state in every dump record. No published stream-selection (`inc`) parameter, so independent streams need jump functions. Spec 07 already names PCG32, and the GDD's vectors are PCG32.
- **Rejection Reason:** It brings no benefit at our draw volume (hundreds of draws per shift), and it would contradict spec 07 and the reviewed GDD vectors.

### Alternative 3: One run-wide stream for everything
- **Description:** One PCG32 for the whole Sim.
- **Pros:** Simplest. One dump record.
- **Cons:** Any change to one system's draw count shifts every other system's results. A Mail re-roll in Prep would move waves and perks. Mail's purity per shift becomes impossible.
- **Rejection Reason:** It breaks TR-mail-002/004 and spec 07 §4.3's per-owner independence.

### Alternative 4: Rejection-sampled unbiased `NextInt`
- **Description:** Lemire's method with a rejection loop.
- **Pros:** Exactly unbiased.
- **Cons:** The draw count becomes data-dependent, which breaks Mail's `K` contract (TR-mail-003) and makes `draws` unpredictable in tests.
- **Rejection Reason:** A bias of at most b/2^32 is far below anything a player could notice. A fixed draw count is a contract.

### Alternative 5: Seed the stream directly from `Fnv1a64(name) ^ runSeed ^ index`
- **Description:** Skip the `Mix64` layers.
- **Pros:** Cheaper.
- **Cons:** Nearby indices give correlated PCG seeds, and XOR of structured inputs can collide (for example `seed ^ index` pairs).
- **Rejection Reason:** `Mix64` is a bijection, so distinct indices under one name always give distinct seeds, and the cost is a one-off at stream creation.

## Consequences

### Positive
- **One spec, three proofs.** A one-page bit-exact spec gives T2 by construction. The Python, C# Debug and C# Release outputs are identical today, and CI re-proves this on both OSes (SC-DET-2).
- **Independence.** Streams are independent by owner and by shift. A Mail re-roll is reproducible and cannot move Lost Parcels (`mail.special`), waves or perks.
- **Testable draw counts.** Mail's `K` and SC-R25-1 can be asserted exactly.
- **Compile-time ownership.** Stream names are compile-time, so a typo or player-text name cannot compile, and the ownership audit is an architecture test rather than a convention.
- **No allocation, no static mutable state.** Two `SimWorld`s in one process have independent registries.

### Negative
- **Bias.** `NextInt` carries a tiny bias, at most b/2^32. This is accepted and documented.
- **Re-creation semantics.** Re-creating a key resets the shared handle in place, so a system that cached the handle sees a reset. This is intentional, but it must be understood (Implementation Guidelines).
- **Preallocated keys.** Preallocating every `(name, index)` key costs 30 small objects in v1. Each new indexed name adds `(max − min + 1)` handles.
- **Naming discrepancy.** The `Mix64` name is "finaliser" in the GDD and registry text but includes the gamma add. The normative text here and the vectors settle it; the GDD wording is a GDD-impact note.

## Risks
- **Risk: a contributor writes `Mix64` as the bare finaliser** (following the GDD wording). *Mitigation:* V2 `Mix64(0) = 0xe220a8397b1dcdaf` fails immediately, because the bare finaliser returns 0.
- **Risk: a Debug build without `unchecked` throws `OverflowException` in `Step`.** *Mitigation:* V1 and V5 run in Debug CI with `CheckForOverflowUnderflow=true`.
- **Risk: the Godot .NET export loads a different runtime than the tests** (Q-ARCH-02, NOT SOURCEABLE). *Mitigation:* the arithmetic is defined by the C# spec, not the runtime. Add one smoke run of V4 row 1 to the export spike checklist (ADR-0001).
- **Risk: future streams get added without an index range,** which makes out-of-range draws silently "valid". *Mitigation:* the `StreamName` constructor requires `MinIndex` and `MaxIndex`, and an enumeration test checks every entry against this ADR's table.
- **Risk: a re-created stream must not shift draws between Prep re-rolls.** *Mitigation:* this is guaranteed by the reset semantics and tested by SC-R24-1.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| simulation-core.md | R23: one derivation, compile-time `RngNames`, no `System.Random`/hash/float | §1 primitives, §2 registry with a private-ctor `StreamName` (TR-sim-027, 028) |
| simulation-core.md | R24: v1 stream registry (DEV-5) | §2 table; spec 07 `mail.spawn`/`mail.address` retired |
| simulation-core.md | R25: `Next32`, single-draw `NextInt`, `b = 0` → 0, `IRngSource` stub | §4 and the Key Interfaces (TR-sim-029) |
| simulation-core.md | R26: owner-only handles, `draws`, dump record | §3 (the dump format is ADR-0006's) |
| simulation-core.md | F5, F6; SC-F5-1, SC-R24-1, SC-R25-1, SC-R26-1, SC-DET-2 | Golden vectors V1–V6 (TR-sim-030) |
| mail-destinations.md | R10/F4: manifest pure in `(seed, shift, profile, addresses)` on `mail`; `K = 2·nb + 2N + max(0, N−1)`; R15 `mail.special` | Per-shift indexed streams; a fixed one-draw `NextInt`; a separate `mail.special` key (TR-mail-002, 003, 004) |
| throwing-catching.md | `throwNoDice`; HB3: any future randomness via a registered stream by launch ordinal | No Throwing entry; amendment rule in §2; Throwing receives no `IRngStreams` (TR-throwing-002) |

## Performance Implications
- **CPU:** `Next32` is about 1 ns (one 64-bit multiply-add plus a shift and rotate). `Create` does four `Mix64` calls plus two PCG steps, and it is called a few times per shift. Negligible against the 5 ms tick budget.
- **Memory:** about 48 B per stream handle × 30 preallocated keys, under 2 KB.
- **Load Time:** None. `Fnv1a64` of the names runs at type init, over 10 short strings.
- **Network:** None. Clients never need the world RNG (Sim Core N6). Manifests, waves and drops arrive as events or state.

## Migration Plan
No code exists yet. The first Sim Core RNG story creates:
- `Sim/Primitives/Fnv1a64Hash.cs`, `SplitMix.cs` and `Pcg32.cs`;
- `Sim/Core/RngNames.cs`, `RngDerive.cs`, `RngStream.cs` and `RngRegistry.cs`;
- `RngGoldenVectorTests.cs`.

Spec 07 §4.3's stream list is superseded by §2 here (DEV-5).

## Validation Criteria
- SC-F5-1 and V1–V6 pass verbatim in Debug and Release (M-1a).
- SC-R24-1 passes: shifts differ, re-creation is identical and resets `draws`, and an out-of-range index asserts in Debug and is counted in Release.
- SC-R25-1 passes: the stub gives 5, 9, 0, 0, with exactly +1 `draws` per call.
- SC-R26-1 passes: the dump records `(name, index, state, inc, draws)`, Throwing holds no handle, and 100 throws leave the total `draws` unchanged.
- SC-ARCH-1 passes the owner-only handle check. The startup test finds all `RngNames` FNV values distinct.
- SC-DET-2 passes: V1–V6 are identical on Windows and Linux x64 CI (M0).

## GDD Impact (no GDD edited by this ADR)
- **simulation-core.md F5 and registry `rngStreamDerivation`:** "`Mix64` is the SplitMix64 finaliser" should read "the SplitMix64 output step (gamma add + finaliser)". The values are unchanged. This is a wording fix at the next re-review.
- **mail-destinations.md** writes the stream as `hash(seed, "mail", shift)`. That is informal notation for `RngStream.Create(RngNames.Mail, shift)` / `Derive(runSeed, "mail", shift)`, so no behavioural conflict exists.
- **spec 07 §4.3:** `mail.spawn` and `mail.address` are superseded (DEV-5, already recorded in Sim Core R24).
- **simulation-core.md F6** ("an int overload asserts b ≥ 0") and SC-F6 rows: the `int` overload is dropped (§4). The behaviour for valid bounds is unchanged. A negative bound is now impossible to express rather than asserted.

## Engine Validation (Step 5.5, godot-specialist, 2026-10-16)
- **Verdict:** no blocking findings.
- **Minor findings, all applied:**
  - the 37-bit `(uint)` narrowing must be `unchecked`;
  - the `Pcg32` field must not be `readonly`;
  - the `int` overload is dropped, because literals would bind to it;
  - golden vectors also run on net8.0.
- **Sources.** The engine reference has no row affecting this ADR, and nothing was sourced from memory for Godot APIs.
- **TD-ADR:** skipped (lean mode).

## Related
- ADR-0006 (numerics, determinism, dump and replay) consumes the stream record and the T2 vectors.
- ADR-0003 (composition root) wires owner-filtered `IRngStreams`.
- ADR-0007 (validate-then-mutate) guarantees that a reject draws nothing.
- `design/gdd/simulation-core.md` R23–R26, F5, F6. `docs/architecture/simulation-core-adr-inputs.md` §1 (unchecked rules), §3.13 (consumed by this ADR).
