# Story 029: Zero-allocation tick probe and tick-timing report (advisory at M-1a)

> **Epic**: Simulation Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (≈ 0.5–1 day; ≈ 300 lines: probe tool, CI step, no-growth test)
> **Manifest Version**: 2026-10-16
> **Last Updated**: —

## Context

**GDD**: `design/gdd/simulation-core.md` (R32 zero allocation; F3 tick budget gates)
**Requirement**: `TR-sim-036` (`Tick()` allocates 0 B after 300 warm-up ticks — **advisory at M-1a**, blocking from M0), `TR-sim-037` (host tick p99 5.0 / max 8.0 / median 3.0 ms; ClientLayer p99 0.3 ms — **advisory until a reference machine exists**; no ADR — `perf-budget.md` not written)
*(Requirement text lives in `docs/architecture/architecture.md` Appendix A — `docs/architecture/tr-registry.yaml` does not exist yet; read fresh at review time)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§8 zero-allocation tick) — primary for TR-sim-036. TR-sim-037: **no governing ADR** — its home is `docs/architecture/perf-budget.md` (not yet written, control manifest E-2); this story only *reports* against TR-sim-037's numbers and gates nothing, so it does not need that file.
**ADR Decision Summary**: After `warmupTicks = 300`, `SimWorld.Tick()` must allocate 0 B on its thread, measured with no recorder, in Release, in a dedicated process (because `GC.CollectionCount(0)` is process-wide), over 18 000 ticks with `GC.GetAllocatedBytesForCurrentThread()`, on the **net8.0** target (a net10.0 pass alone does not count — .NET 9+ escape analysis can hide allocations). Tick-path collections are pre-sized from `SimSettings`; growth is allowed only during warm-up and a test asserts none afterwards. Advisory at M-1a, blocking from M0.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free probe) | **Risk**: MEDIUM (ADR-0006: the 0-alloc claim inside an *exported* Godot build is UNVERIFIED — the runtime the 4.7.1 .NET export loads is NOT SOURCEABLE; this probe measures the CoreCLR net8.0 runtime only)
**Engine Notes**: Perf numbers from tests or the editor are not transferable to exports (ADR-0001 §7). An export smoke check of `GC.GetAllocatedBytesForCurrentThread` belongs to the Godot .NET export spike (ADR-0006 Verification Required (4)).

**Control Manifest Rules (Foundation)**:
- Required: `SimWorld.Tick()` 0 B after 300 warm-up ticks, no recorder, Release, dedicated process, net8.0, 18 000 ticks, `GC.GetAllocatedBytesForCurrentThread()` — advisory at M-1a; `[PERF]`/0-alloc tests in a non-parallel collection, the gate in a dedicated process; tick-path collections pre-sized from `SimSettings`, a test asserts no growth after warm-up; perf measured only in `Release`.
- Forbidden: in the tick path — LINQ, lambdas/closures, method-group delegates, interface-typed `foreach`, boxed enumerators, `params`, string building/logging, boxing, `Enum.ToString/GetValues`, exceptions as control flow, `async`, `yield`.
- Guardrail: host tick p99 5.0 / max 8.0 / median 3.0 ms, ClientLayer p99 0.3 ms — **reported, never enforced** at M-1a (TR-sim-037 advisory; per-phase slices TR-sim-038 await `perf-budget.md`).

---

## Acceptance Criteria

*From ADR-0006 §8 and TR-sim-036/037 (SC-R32-1 and SC-PERF-1 are [M0]; this story builds their probe in advisory mode), scoped to this story:*

- [ ] **AC-1 Advisory 0-alloc probe (TR-sim-036).** A dedicated-process probe runs a Release, net8.0 Sim world built from the most complete available profile (Sim Core + the SC-DET-1 fixture system until SC-FIX-WORST exists), with no recorder, 300 warm-up ticks then 18 000 measured ticks (a seeded op per tick, sink bound), and prints `allocatedBytes`, gen-0 collection delta and ticks; CI runs it on the net8.0 leg and publishes the numbers as a job summary/artifact. A non-zero result is **reported** (warning annotation) and does **not** fail the build at M-1a.
- [ ] **AC-2 No growth after warm-up (blocking).** A unit test asserts that the capacities of the op ring, boundary-tick array, event log, sample ring/latch, reader cursor table and check ring are identical before and after 2 000 ticks following warm-up (no collection grows), on every CI leg.
- [ ] **AC-3 Advisory timing report (TR-sim-037).** The probe also records per-tick wall time for `SimWorld.Tick()` and `ClientLayer.Step()` (timed in the probe, never in the Sim) and reports median, p99 (F3 integer rank) and max, printed next to the TR-sim-037 targets with the label "ADVISORY — no reference machine"; nothing is gated on them.

---

## Implementation Notes

*Derived from ADR-0006 §8 and ADR-0001 §7:*

- `tools/SimAllocProbe/` — `net8.0` console project (add to `PerformativeMail.sln`; references the Sim and, if needed, the test fixtures copied into a small shared fixtures project — or a fixture defined in the tool). Run as `dotnet run -c Release --project tools/SimAllocProbe -- --ticks 18000 --warmup 300`. Pin tiered compilation for the run (`DOTNET_TieredPGO=0`, `DOTNET_TC_QuickJitForLoops=1`/`DOTNET_TieredCompilation` settings as chosen and documented in the tool's README) so numbers are comparable run to run.
- Measure with `GC.GetAllocatedBytesForCurrentThread()` immediately around the 18 000-tick loop and `GC.CollectionCount(0)` before/after; the loop itself must not allocate (pre-build the op payloads).
- Timing uses `Stopwatch.GetTimestamp()` **in the tool** (the Sim's analyzer ban applies to the Sim only). Compute p99 with the F3 integer rank (`rank = ceil(0.99 × N)` over a sorted `long[]`; sorting in a tool may use `Array.Sort`).
- CI: a step on the net8.0 Release leg runs the probe, writes the numbers to `$GITHUB_STEP_SUMMARY`, and emits `::warning::` if `allocatedBytes > 0`. When M0 makes the gate blocking (SC-R32-1), flip the warning to a failure — and write `docs/architecture/perf-budget.md` first (control manifest E-2) before any ms number becomes a gate.
- AC-2's test lives in the normal test project (`tests/unit/sim/Core/NoGrowthTests.cs`), in the non-parallel `[PERF]` collection is unnecessary (it measures capacities, not time) — keep it in the default collection.
- Any allocation found is fixed in the owning story's code if trivial, otherwise recorded as tech debt with the call stack (`dotnet-counters`/allocation tick events) — the advisory mode exists so the first measurement happens early (epic Risk 3).

---

## Out of Scope

- SC-R32-1 (M0, blocking, SC-FIX-WORST, 64 MB heap) and SC-PERF-1 (M0, CI perf gate against a calibrated baseline, TR-sim-039).
- `docs/architecture/perf-budget.md` (per-phase slices TR-sim-038) — required before any perf **gate**, not for this advisory probe.
- Export-runtime measurement (Godot .NET export spike).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier (`--review lean`: QL-STORY-READY skipped); implement against the Acceptance Criteria above.*

---

## Test Evidence

**Story Type**: Logic (with advisory perf evidence)
**Required evidence**:
- `tests/unit/sim/Core/NoGrowthTests.cs` — must exist and pass (blocking)
- `tools/SimAllocProbe` CI job summary showing allocated bytes and tick timings on net8.0 Release (advisory; link the run in the story-done notes)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 024 (`FrameRunner`/`ClientLayer` path to time), Story 025 (composition), Story 027 (fixture system)
- Unlocks: M0 SC-R32-1 / SC-PERF-1 (flip to blocking after `perf-budget.md`)
