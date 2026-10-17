# Story 024: Router-core zero-allocation gate (H-PERF-3, blocking) and the advisory frame-time probe

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: S–M (~0.5–1 day; ~300 lines probe + tests + CI step)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (Performance budget; ACs H-PERF-3 (BLOCKING, M-1a), H-PERF-1 / H-PERF-2 (ADVISORY, M1), OQ-IC-17)
**Requirement**: `TR-input-018` (router + gatherer ≤ 0.1 ms / frame, ≤ 0.3 ms at a 1000 Hz burst, 0 B steady). The registry/EPIC slice column marks the TR *advisory*; the **0 B clause is a BLOCKING M-1a gate** (GDD H-PERF-3, ADR-0012 §9) — this story implements that clause and the advisory timing probe.
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§9 Performance — "Core: no allocation after warm-up … H-PERF-3 BLOCKING at M-1a; ≤ 0.1 ms per frame typical and ≤ 0.3 ms at a 1000 Hz burst — ADVISORY. Adapter allocations from C# `InputEvent` wrappers are measured by the spike before the gate is extended to it") — Accepted 2026-10-16 (primary); ADR-0001 (§7 the 0-alloc gate runs in a dedicated process; perf measured only in `Release`) and ADR-0006 (§8 method) secondary
**ADR Decision Summary**: The router core is allocation-free after warm-up (preallocated rings, machine tables, `ReadOnlySpan` action names). The gate measures `GC.GetAllocatedBytesForCurrentThread()` over a long trace through the core in a dedicated Release process on the `net8.0` target. Time budgets stay advisory until a reference machine exists. The adapter's wrapper allocations are not gated here.
**ADR Version**: 2026-10-16 (ADR-0012); 2026-10-16 (ADR-0001)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: The adapter's per-event allocation is measured by story 001 and bounded in story 026; it is deliberately **outside** this gate.

**Control Manifest Rules (Foundation)**:
- Required: router-core steady state allocates 0 B (ADR-0012 Implementation Guidelines); perf is measured only in `Release`; the 0-alloc gate runs in a dedicated process (ADR-0001 §7)
- Forbidden: LINQ, lambdas / closures, delegate creation, boxing, `params`, string formatting, `foreach` over interface-typed collections in `Feed` / `FlushToLatch` / `DispatchImmediates`
- Guardrail: 0 B over 10 000 trace frames after 300 warm-up frames; each log entry ≤ 64 B; the 4096-entry ring allocated once

---

## Acceptance Criteria

*From GDD H-PERF-3 (blocking) and H-PERF-1 / H-PERF-2 (advisory), scoped to this story:*

- [ ] **H-PERF-3 (BLOCKING).** After 300 warm-up frames, `GC.GetAllocatedBytesForCurrentThread()` delta is **0** over 10 000 trace frames driven through the router **core** (`Feed`, `FlushToLatch`, `DispatchImmediates`, settings applied, glyph tracking, the Back ladder, aim, Toggle, focus-loss/regain every 500 frames, state changes, hitches) against a recording-free `IInputLatchWriter` and null sinks; each `UiEventLog` entry is ≤ 64 B (`Unsafe.SizeOf`), and the 4096-entry ring and all queues keep their identity and capacity after the run (allocated once). Measured on the **net8.0** target, `Release`, in a dedicated process: `tools/InputAllocProbe` (`net8.0` console in `PerformativeMail.sln`) prints `allocatedBytes`, gen-0 collection delta and frames, exits non-zero if `allocatedBytes > 0`, and the net8.0 Release CI leg runs it as a **failing** step. A convenience xUnit test in the non-parallel `[PERF]` collection runs the same loop and is skipped in `Debug` with an explicit reason string.
- [ ] **No growth.** A unit test asserts every router collection (edge ring, immediate queue, press-machine table, held table, step accumulators, log ring, validation scratch) has identical capacity before and after 2 000 frames following warm-up, on every CI leg.
- [ ] **Advisory timing (H-PERF-1 / H-PERF-2 shape, M1 thresholds).** The probe also times the loop with `Stopwatch.GetTimestamp()` **inside the probe** (never in Client.Core): typical 60 fps trace and a 1000 Hz mouse burst (16 motion records per frame for 600 frames, accumulated input off) and prints median / p95 / p99 (integer-rank) next to the targets (≤ 0.1 ms, ≤ 0.3 ms) labelled "ADVISORY — no reference machine"; nothing is gated on them at M-1a. The adapter side of H-PERF-1/2 (headless Godot) is M1.
- [ ] **Coverage of the trace.** The probe's trace generator reuses story 022's alphabet so the 0 B result covers the dangerous paths (cancel, mute, overflow drop, commit gate), and prints counters (edges, immediates, drops, glyph flips) to prove those paths ran.

---

## Implementation Notes

*Derived from ADR-0012 §9, ADR-0006 §8 and the sim-core 029 probe:*

- Mirror `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md` (`tools/SimAllocProbe`): same tiered-compilation pinning, same job-summary output, same `GC.CollectionCount(0)` bracketing. **Difference:** that probe is advisory at M-1a; this one is a **hard failure** because ADR-0012 §9 and H-PERF-3 make the router-core 0 B blocking now.
- Files: `tools/InputAllocProbe/InputAllocProbe.csproj` + `Program.cs`, `tests/unit/input/RouterAllocationTests.cs`, `tests/unit/input/NoGrowthTests.cs`, a step in `.github/workflows/tests.yml` on the net8.0 Release leg.
- Pre-build the trace (events as a flat `RawInputRecord[]` and tick markers) outside the measured loop; the loop must not allocate. Any allocation found is fixed in the owning router story's code or recorded as debt with the call stack.
- Interfaces the router calls (`IInteractionQuery`, `ICarrierStatusSource`, `IThrowAimView`, `IPresentedPromptSource`, sinks) are implemented by pre-built, non-allocating doubles in the probe.
- OQ-IC-17 (ring sizing under 1000 Hz input; relative perf gate ≤ 2× baseline) is an M0 follow-up recorded in EPIC.md.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Adapter allocations (story 001 measures; M1 gate). Frame-time gates (H-PERF-1/2 are M1 advisory; H-PERF-4 latency is M0). `docs/architecture/perf-budget.md` (required before any ms number becomes a gate — not written).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/RouterAllocationTests.cs` (`[Trait("Id", "H-PERF-3")]`), `tests/unit/input/NoGrowthTests.cs`; the `tools/InputAllocProbe` CI step output on the net8.0 Release leg — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007–019 (the router as built), 022 (trace alphabet); `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (Release / net8.0 CI legs, Done), `story-029-zero-alloc-tick-timing-advisory.md` (probe pattern)
- Unlocks: M1 adapter budget gate (after story 001's numbers)
