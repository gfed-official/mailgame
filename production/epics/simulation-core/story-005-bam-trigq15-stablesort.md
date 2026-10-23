# Story 005: Core primitives III — BAM angles, Q15 trig table and StableSort

> **Epic**: Simulation Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (≈ 0.5 day; ≈ 300 lines + the generated table)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/simulation-core.md` (R27 angles in BAM + Q15; R28 checklist rule 5 stable sort)
**Requirement**: `TR-sim-031` (BAM + Q15 integer trig), `TR-sim-032` (stable sort, no `Array.Sort`), `TR-sim-034` (Q15 table on the T2 list)
*(Requirement text: `docs/architecture/tr-registry.yaml`)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§1 angles, §3 rules 4–5, §6 Q15 table) — primary; ADR-0003: Phase Order, Composition Root and Cross-System Calls (§4 static-state rule) — secondary
**ADR Decision Summary**: Angles are `ushort` BAM (65 536 = 360°) whose add/sub wrap by design inside `unchecked`. Trig reads a committed 4096-entry Q15 sine table indexed `bam >> 4`, values `round-half-away(32767·sin(2π·i/4096))`, generated once by `tools/GenTrigQ15` and pinned by a SHA-256 test; cosine is `Sin(bam + 16384)`. All ordering-sensitive sorts use `Sim.Primitives.StableSort` with preallocated scratch and a comparer that ends in an id/`opSeq` tie-break.
**ADR Version**: 2026-10-16 (ADR-0006), 2026-10-16 (ADR-0003)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` code) | **Risk**: LOW
**Engine Notes**: None. `tools/GenTrigQ15` is a dev-time console tool and may use `double`; the Sim never does.

**Control Manifest Rules (Foundation)**:
- Required: Q15 trig = committed sine table in `Sim/Primitives/TrigQ15.cs`, indexed `bam >> 4`, generated once by `tools/GenTrigQ15`; cosine = `Sin(bam + 16384)`; a test asserts the table's SHA-256; BAM add/sub inside `unchecked((ushort)(a + b))`; sorting via `Sim.Primitives.StableSort` with preallocated scratch and a comparer ending in an `EntityId`/`opSeq` tie-break.
- Forbidden: `Array.Sort`, `List.Sort`, `MemoryExtensions.Sort`, LINQ `OrderBy`; static mutable state — a `static readonly T[]` counts as **mutable** (ADR-0003 §4); `float`/`double` in the Sim.
- Guardrail: Q15 table 8 KiB; `StableSort` allocates 0 B (scratch is caller-provided).

---

## Acceptance Criteria

*Derived from ADR-0006 §1, §3 rule 5, §6 (no GDD AC targets these primitives directly at M-1a; they are "Real at M-1a" per the GDD milestone slice), scoped to this story:*

- [x] **AC-1 Q15 table.** `TrigQ15.Sin(0) == 0`, `Sin(16384) == 32767`, `Sin(32768) == 0`, `Sin(49152) == −32767`; `Cos(bam) == Sin(unchecked((ushort)(bam + 16384)))` for all 65 536 inputs; the SHA-256 of the table's 8 192 little-endian bytes equals the digest recorded in this story's Implementation Notes at generation time (the T2 pin, re-run by SC-DET-2 at M0).
- [x] **AC-2 BAM wrap.** `Bam.Add(65 000, 1 000) == 464` and `Bam.Sub(10, 20) == 65 526` with no exception in the Debug (checked) build.
- [x] **AC-3 StableSort.** Sorting 1 000 seeded records with many equal primary keys preserves input order among equals and matches a reference merge sort; the call allocates 0 B (measured with `GC.GetAllocatedBytesForCurrentThread` around a warmed call) and reads no `GetHashCode`.

---

## Implementation Notes

*Derived from ADR-0006 §1, §3, §6 and ADR-0003 §4:*

- **Table storage — cross-ADR note.** ADR-0006 §6 says "a committed `static readonly short[4096]`", but ADR-0003 §4's static-state test treats any `static readonly T[]` as mutable and requires constant tables to be `ReadOnlySpan<T>` literal properties or `[ImmutableTable]` wrappers. Follow ADR-0003 (the stricter, test-enforced rule): emit `private static ReadOnlySpan<short> Table => new short[] { … };` (on net8.0 the compiler binds this to `RuntimeHelpers.CreateSpan` — no allocation, data in the PE image). This preserves ADR-0006's content and indexing exactly. (Logged in the decision log; erratum candidate for ADR-0006 §6.)
- **`tools/GenTrigQ15/`** — `net8.0` console project (add to `PerformativeMail.sln`; references nothing in the Sim). Computes `round-half-away-from-zero(32767 * Math.Sin(2π·i/4096))` for i = 0…4095 and writes `src/PerformativeMail.Sim/Primitives/TrigQ15.g.cs` plus prints the SHA-256 of the LE bytes. Run once, commit the output, and paste the digest here: `TrigQ15 SHA-256: 70A520195643F9EC9AA892EC529C410634068E1FFD18FA327C73B91D04261BC7`. The generator is not run in CI; the test pins the committed table.
- **API** (`Primitives/TrigQ15.cs`, `Primitives/Bam.cs`): `public static short Sin(ushort bam) => Table[bam >> 4];` `Cos(ushort bam) => Sin(unchecked((ushort)(bam + 16384)));` `Bam.Add/Sub` are `unchecked((ushort)(a ± b))` (overflow class 1). Interpolation and `atan2` belong to ADR-0013 (player-character) — not here.
- **`StableSort`** (`Primitives/StableSort.cs`): a stable insertion sort for n ≤ 16 and bottom-up merge sort above, over `Span<T>` with a caller-provided `Span<T>` scratch of equal length; the comparer is a generic struct constraint (`where TCmp : struct, IComparer<T>`, passed `ref`/`in`) so no delegate or boxing occurs. Document in XML that every comparer must end with an `EntityId` or `opSeq` tie-break (rule 5); the sort itself cannot enforce that.
- Hash in the test uses `System.Security.Cryptography.SHA256` **in the test project** — tests may hash; the Sim never does (ADR-0006 §4).

---

## Out of Scope

- Story 003: `Cents`, `Fx16`, `Fix`.
- Atan2, interpolated trig, carrier geometry: ADR-0013 / player-character epic.
- SC-DET-2 cross-OS CI run of the table hash: M0 (`windows-latest` leg).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/unit/sim/Primitives/TrigQ15Tests.cs`, `tests/unit/sim/Primitives/StableSortTests.cs` — must exist and pass (Debug + Release; net10.0 + net8.0)

**Status**: [x] Created — tests/unit/sim/Primitives/TrigQ15Tests.cs, StableSortTests.cs

---

## Dependencies

- Depends on: Story 001 (build/CI), Story 004 (analyzer active, so the Sim-side table code is checked float-free)
- Unlocks: player-character (BAM/Q15), any Sim code that sorts

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 3/3 passing (AC-1 anchors + Cos/Sin all 65 536 + SHA-256 pin `70A52019…1BC7`; AC-2 BAM wrap in checked Debug; AC-3 1 000-record stability vs reference merge sort, 0 B warmed, GetHashCode trap struct)
**Deviations**: ADVISORY — (1) table stored as `ReadOnlySpan<short>` property per ADR-0003 §4 (story-planned; ADR-0006 §6 erratum candidate); (2) `RuntimeHelpers.CreateSpan` allocates (~64 B) at Tier-0 / in unoptimised builds — generated getter uses `[MethodImpl(AggressiveOptimization)]`; TrigQ15 0-B test skips on Debug legs (TD-008); (3) `StableSort.Sort` throws `ArgumentException` on short/overlapping scratch; extra constants `Bam.Turn/Quarter/Half/ThreeQuarters`, `TrigQ15.TableLength/One` (accepted by review).
**Test Evidence**: Logic — tests/unit/sim/Primitives/TrigQ15Tests.cs (14 methods / 23 cases), StableSortTests.cs (20 methods / 39 cases); suite 288: net10.0/net8.0 Release 288/288, Debug 287 + 1 skipped; Release build 0 warnings; analyzer fixtures 80/0.
**Run result**: N/A — Godot-free Sim primitives; nothing renders.
**Code Review**: Complete — LP-CODE-REVIEW CONCERNS (revised: Sort split ≤ 40 lines/CC ≤ 10; Debug skip made visible); QL-TEST-COVERAGE ADEQUATE.
