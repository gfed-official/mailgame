# Story 008: RNG primitives, RngStream and golden vectors

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. vector tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R23–R25 randomness; F5 stream derivation, F6 bounded draw)
**Requirement**: `TR-sim-027` (PCG32 XSH-RR; `Derive` via `Mix64`/`Fnv1a64`; all unchecked), `TR-sim-029` (`NextInt(b) = (next32·b) >> 32`, one draw per call; no float RNG), `TR-sim-030` (committed golden vectors)
*(Requirement text: `docs/architecture/tr-registry.yaml`)*

**ADR Governing Implementation**: ADR-0005: RNG Streams and Derivation
**ADR Decision Summary**: Every random draw comes from PCG32 XSH-RR seeded by `Derive(runSeed, name, index) = Mix64(Mix64(runSeed ^ Fnv1a64(name)) ^ index)`, where `Mix64` is the full SplitMix64 step (including the `+0x9E3779B97F4A7C15` gamma); `initState = Mix64(s)`, `initSeq = Mix64(s ^ 0xDA3E39CB94B95BDB)`. `NextInt(uint b)` is the multiply-shift `(uint)(((ulong)Next32() * b) >> 32)` — exactly one draw, `b = 0` returns 0 (Debug asserts). There is no float, bool-with-probability, rejection or `int` overload. Golden vectors V1–V6 are asserted verbatim on Debug and Release, net8.0 and net10.0.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None. Every line of the hash/PRNG arithmetic must be inside `unchecked(...)` — the Debug build is `checked` (Story 001), and the `(uint)(((old >> 18) ^ old) >> 27)` narrowing throws under `checked` otherwise.

**Control Manifest Rules (Foundation)**:
- Required: `Fnv1a64`, `Mix64` (full SplitMix64 step), `Derive`, PCG32 `pcg32_srandom`/`Step`/`Next32` exactly as ADR-0005 §1, every line `unchecked(...)`, rotate written `(32 - rot) & 31` with `int rot`; `NextInt(uint b)` one draw per call, `b = 0` returns 0 (Debug asserts); the `Pcg32` field inside `RngStream` is not `readonly`; each `RngStream` counts `draws` (+1 per `Next32`/`NextInt`, including `NextInt(0)`); tests inject `IRngSource` through the `RngStream` test constructor; `tests/unit/sim/Core/RngGoldenVectorTests.cs` asserts V1–V6 verbatim in Debug and Release on net8.0 and net10.0.
- Forbidden: `System.Random`/`Random.Shared`; xoshiro/xoroshiro; rejection-sampled `NextInt`; seeding directly from `Fnv1a64(name) ^ runSeed ^ index`; `NextFloat`, `NextDouble`, `NextBool(p)`, range-with-rejection, an `int` overload of `NextInt`.
- Guardrail: `Next32` ≈ 1 ns; 0 B allocated per draw.

---

## Acceptance Criteria

*From GDD SC-F5-1, SC-R25-1 and ADR-0005 Golden vectors, scoped to this story:*

- [x] **AC-1 SC-F5-1.** `pcg32_srandom(42, 54)` yields `0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293`; `Mix64(0) == 0xe220a8397b1dcdaf`; `Fnv1a64("mail") == 0x1f5964a2ce98072e`; `Derive(1, "mail", 2) == 0x120344c795c4b10e`; a stream seeded from `(1, "mail", 2)` yields `0x915ab962, 0xc3f6d39c, 0x5d7b280d` — in Debug and Release.
- [x] **AC-2 Full golden table.** Every value of ADR-0005 V1 (six outputs), V2 (three `Mix64` rows), V3 (ten `Fnv1a64` rows incl. empty string), V4 (six rows: `Derive`, state after srandom, `inc`, five `Next32`, five `NextInt(10)`; plus row 1's `initState`/`initSeq`) and V5 (after 1 000 draws `state == 0xcac3e44cb2a5fa56`, 1 001st output `0x3d6fc4ac`) is asserted verbatim, on net10.0 and net8.0, Debug and Release.
- [x] **AC-3 SC-R25-1 / V6.** With an `IRngSource` stub returning `0x80000000, 0xFFFFFFFF, 0, …`, `NextInt(10)` ×3 then `NextInt(0)` return 5, 9, 0, 0 and `Draws` increases by exactly 1 per call (`NextInt(0)` asserts under `SIM_ASSERT` after consuming its draw — the test checks both builds); V6's remaining rows hold (`0xFFFFFFFF`, `0xFFFFFFFF` → `0xFFFFFFFE`; `0x12345678`, 65 535 → 4 660).

---

## Implementation Notes

*Derived from ADR-0005 §1, §3–§4, Key Interfaces and Implementation Guidelines:*

- **Primitives** (`src/PerformativeMail.Sim/Primitives/`): `Fnv1a64Hash.Hash(ReadOnlySpan<byte> utf8)` (offset `0xCBF29CE484222325`, prime `0x100000001B3`); `SplitMix.Mix64(ulong x)`; `public struct Pcg32` with `Pcg32(ulong initState, ulong initSeq)` = `pcg32_srandom` (`state = 0; inc = (initSeq << 1) | 1; Step(); state += initState; Step()`), `Step()` = `state * 6364136223846793005 + inc`, `Next32()` per ADR-0005 §1, `State`/`Inc` getters (the dump reads them). All `unchecked(...)`.
- **Core** (`src/PerformativeMail.Sim/Core/`): `IRngSource { uint Next32(); }` (test seam); `RngDerive.Derive(ulong runSeed, ReadOnlySpan<byte> nameUtf8, uint index)` — Story 009 adds the `in StreamName` overload that uses the name's precomputed hash; `RngStream` (sealed class) holding a **non-`readonly`** `Pcg32` field, `Draws` (u64), `Next32()`, `NextInt(uint bound)`, an internal `Reseed(ulong derived)` that resets state/inc and sets `Draws = 0` (Story 009's in-place re-create), and an `internal` test constructor taking `IRngSource` (production code never sees the stub; `InternalsVisibleTo` already exists).
- `NextInt(uint b)`: draw first (`Draws++`), then `SimAssert.That(b != 0, …)`, then return `(uint)(((ulong)x * b) >> 32)` (0 when b = 0). Exactly one draw on every call — the Mail `K` draw count is a contract.
- There is **no** `int` overload: literals like `NextInt(10)` bind to `uint`; an `int` overload would route through an assert (engine validation note in ADR-0005 §4).
- Stream names in tests are `"…"u8` literals — never build names from runtime strings.
- Test file path is mandated: `tests/unit/sim/Core/RngGoldenVectorTests.cs` (namespace `PerformativeMail.Sim.Tests.Unit.Sim.Core` or per tests/README), one Theory per vector table. On a machine without the .NET 8 runtime the net8.0 leg is reported NOT RUN, never passed (here it is installed).

---

## Out of Scope

- Story 009: `StreamName`, `RngNames` (v1 registry), `RngRegistry` (preallocated handles, in-place `Create`), owner-filtered `IRngStreams`, index-range checks, the pairwise-distinct Fnv startup test (SC-R24-1).
- Story 019: dumping stream tuples.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/RngGoldenVectorTests.cs` (`[Trait("Id", "SC-F5-1")]`, `[Trait("Id", "SC-R25-1")]`) — must exist and pass on net10.0 and net8.0, Debug and Release

**Status**: [x] Created — tests/unit/sim/Core/RngGoldenVectorTests.cs

---

## Dependencies

- Depends on: Story 001 (checked Debug build, net8.0 leg), Story 004 (analyzer bans `System.Random`)
- Unlocks: Story 009

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 SC-F5-1 headline vectors; AC-2 ADR-0005 V1–V5 verbatim — QA lead cross-checked every value; AC-3 SC-R25-1/V6 stub sequence 5, 9, 0, 0 with Draws +1 per call, NextInt(0) asserts (`SimAssertCode.RngBoundZero = 13`, appended) after consuming its draw in SIM_ASSERT builds, returns 0 otherwise)
**Deviations**: None blocking. Both `RngStream` constructors internal; internal `State`/`Inc` for tests and the story-019 dump; no counter added. Carry-forwards for stories 009/019 logged as TD-009.
**Test Evidence**: Logic — tests/unit/sim/Core/RngGoldenVectorTests.cs (14 methods / 32 cases); suite 401: Release 401/401, Debug 400 + 1 skipped, net10.0 and net8.0 (independent net8.0 Debug re-run: 400 + 1 skipped); Release build 0 warnings; analyzer fixtures 80/0.
**Run result**: N/A — Godot-free PRNG primitives; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE; QL-TEST-COVERAGE ADEQUATE.
