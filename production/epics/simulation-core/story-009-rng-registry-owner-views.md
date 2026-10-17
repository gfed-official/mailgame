# Story 009: RNG stream registry (RngNames, RngRegistry) and owner-filtered views

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 350 lines incl. tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R24 stream registry and lifetime, R26 dump of streams; DEV-5)
**Requirement**: `TR-sim-028` (compile-time `RngNames` registry; v1 stream list; `mail`/`mail.special`/`events` indexed per shift), `TR-sim-029` (owner-only handles)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0005: RNG Streams and Derivation (§2–§3) — primary; ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4 injectable list: owner-filtered `IRngStreams`) — secondary
**ADR Decision Summary**: `StreamName` is a `readonly struct` with a private constructor whose only instances are `static readonly` fields of `RngNames` (name, owner, index range, `Fnv1a64` precomputed once from a `"…"u8` literal). `RngRegistry` is per `SimWorld`, pre-allocates a handle for every registered `(name, index)`, and `Create` re-seeds the existing handle in place (state, inc reset, `draws = 0`) allocating nothing. Out-of-range indices assert in Debug and, in Release, derive anyway on a spare slot and count `rngIndexOutOfRange`. Each system receives an `IRngStreams` view filtered to the names it owns; Throwing receives none.
**ADR Version**: 2026-10-16 (ADR-0005), 2026-10-16 (ADR-0003)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: streams only from `RngNames` static readonly `StreamName` fields (v1: `worldgen.<stage>`, `mail` 1–5, `mail.special` 1–5, `waves`, `mega`, `drops`, `perks`, `shop`, `events` 1–5); `Fnv1a64` computed once at type init from a `"…"u8` literal; `RngRegistry` per world, preallocates every handle, `Create` re-seeds in place allocating nothing; out-of-range index asserts in Debug, counts `rngIndexOutOfRange` in Release; owner-filtered `IRngStreams` views (Throwing gets none); a startup test asserts all `RngNames` `Fnv1a64` values are pairwise distinct.
- Forbidden: stream names built from runtime strings or player text; holding a stream across a `Create` of the same key expecting the old sequence; static mutable state (`static readonly StreamName` fields are immutable values — allowed).
- Guardrail: ≈ 48 B × handles preallocated (< 2 KB); `Create` 0 B.

---

## Acceptance Criteria

*From GDD SC-R24-1 (registry half) and ADR-0005 §2–§3, scoped to this story:*

- [x] **AC-1 SC-R24-1.** For one `runSeed`: `mail` shift 1 and shift 2 produce different sequences; `Create(mail, 1)` called twice returns the **same handle**, the second call resets `Draws` to 0 and replays the first call's sequence from the start, and the registry reports exactly one live entry for `(mail, 1)`; `Create(mail, 0)` throws `SimAssertException` under `SIM_ASSERT`, while without it the stream is derived on a spare slot and `rngIndexOutOfRange` is counted once. `Create` allocates 0 B after composition.
- [x] **AC-2 Registry contents.** `RngNames` declares exactly ADR-0005 §2's v1 table (names, index ranges, owners); each `StreamName.Fnv` equals ADR-0005 V3's value for that name; all `Fnv` values are pairwise distinct (startup test); `RngDerive.Derive(seed, in StreamName, index)` equals the Story 008 byte-span overload for every name.
- [x] **AC-3 Owner views.** `RngRegistry.ViewFor(owner)` returns an `IRngStreams` whose `Create` succeeds only for names that owner owns (another owner's name asserts in Debug / is refused and counted in Release); the view for Throwing (and any owner with no names) exposes no stream; live streams can be enumerated in `RngNames` declaration order then ascending index (the order Story 019's dump uses).

---

## Implementation Notes

*Derived from ADR-0005 §2–§3 and Key Interfaces:*

- `src/PerformativeMail.Sim/Core/StreamName.cs`: `readonly struct StreamName` — private ctor; `Ordinal` (u16, declaration order), `Name` (`ReadOnlySpan<byte>` via a literal-backed property or stored `byte[]` wrapped as immutable — no `static readonly byte[]`), `Fnv` (u64), `Owner` (`SystemId`), `MinIndex`, `MaxIndex`.
- `src/PerformativeMail.Sim/Core/RngNames.cs`: `public static class RngNames` with `public static readonly StreamName Mail, MailSpecial, Waves, Mega, Drops, Perks, Shop, Events, WorldgenTerrain …` per the ADR table. `worldgen.<stage>`: declare `worldgen.terrain` now (V3 lists it); the world-generation/world-stub epic appends its other stages (each an amendment to ADR-0005's table + one field). Also expose a `ReadOnlySpan`-style ordered list (or a switch on ordinal) — not a `static readonly StreamName[]` (ADR-0003 static-state rule).
- **Owners.** `SystemId` is the composition identity (ADR-0003 `SimSystemAttribute(SystemId)`). Define `enum SystemId : byte` in `Core/` (Composition, Story 025, references it — Core must not depend on Composition). Owners per ADR-0005 §2: World Generation, Mail & Destinations, Enemy AI & Waves (`waves`, `mega`), Perks, Economy (`shop`), Stamps & Run Events (`events`). `drops` lists "Health & Damage / Enemy AI": register it to Health & Damage (first listed) and note the ambiguity for an ADR-0005 erratum (decision-log entry). Throwing owns nothing.
- `src/PerformativeMail.Sim/Core/RngRegistry.cs`: constructed with `runSeed` and `SimCounters`; preallocates one `RngStream` per registered key (sum of index-range sizes) plus a small spare pool for Release out-of-range keys; `Create(in StreamName, uint index)` looks up the key's preallocated handle (index arithmetic, no dictionary needed), derives, and calls `RngStream.Reseed` — same object every time. Track `IsLive` per handle for the dump ("a stream that was never created is recorded as absent").
- `IRngStreams { RngStream Create(in StreamName name, uint index); }` — the owner-filtered view is a small sealed class holding the registry and the owner's `SystemId`. The SC-ARCH-1 constructor check ("no system receives another owner's view; Throwing receives none") is asserted structurally in Story 028 once systems exist; here AC-3 tests the view behaviour.
- Draw-after-validation (handlers draw only after validating) is a handler rule tested per handler (SC-R8-2 harness, Story 020); nothing to build here.

---

## Out of Scope

- Story 008: PCG32/Mix64/Fnv1a64/`Derive` primitives and golden vectors.
- Story 019: writing `(name ordinal, index, state, inc, draws)` into the canonical dump (the dump half of SC-R24-1 is re-asserted there).
- Story 025: composition root passing views to systems; Story 028: SC-ARCH-1 owner check.
- SC-R26-1 (M-1b): Throwing holds no handle across 100 throws — throwing-catching epic.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Core/RngRegistryTests.cs` (`[Trait("Id", "SC-R24-1")]`) — must exist and pass (Debug + Release)

**Status**: [x] Created — tests/unit/sim/Core/RngRegistryTests.cs (+ TD-009 additions in RngGoldenVectorTests.cs, SimCountersTests.cs)

---

## Dependencies

- Depends on: Story 007 (`SimCounters`), Story 008 (RNG primitives, `RngStream`)
- Unlocks: Story 019 (dump), Story 025 (composition), mail-destinations (`mail`/`mail.special`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 SC-R24-1 same handle/reset/replay, one live entry, Create(mail,0) asserts under SIM_ASSERT else spare slot + `RngIndexOutOfRange` once, Create 0 B in Release; AC-2 v1 table + ADR-0005 V3 Fnv verbatim, pairwise distinct, `Derive(in StreamName)` == byte-span overload; AC-3 owner views refuse other owners' names (assert / `RngRefused` counter 21), Throwing/None views expose nothing, `CopyLive` in declaration order then index)
**Deviations**: None blocking. Advisory (ADR-0005 errata, TD-010): v1 table yields 21 keys, not the "30" in ADR-0005/manifest guardrail; `StreamName` ctor `internal` (C# cannot grant a private ctor to `RngNames`); `RngIndexOutOfRange`/`RngRefused` counted in Debug too (before the throw); `drops` owned by `HealthDamage` (ADR lists "Health & Damage / Enemy AI"). Forward-looking hardening logged as TD-011. TD-009 items 1/3/4/6 resolved here (RngStream `Name`/`Index` ctor; hash `Derive`/`Seed` internal; `default(Pcg32)` doc; NextInt(1)/non-ASCII Fnv tests); item 2 deferred to story 019, item 5 open.
**Test Evidence**: Logic — tests/unit/sim/Core/RngRegistryTests.cs (44 cases); suite 535/leg (incl. concurrent Client.Core track): Release 535/535, Debug 534 + 1 skipped (TrigQ15 0-B, TD-008), net10.0 and net8.0.
**Run result**: N/A — Godot-free Sim RNG registry; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (5 non-blocking, folded godot-csharp-specialist .NET review); QL-TEST-COVERAGE ADEQUATE.
