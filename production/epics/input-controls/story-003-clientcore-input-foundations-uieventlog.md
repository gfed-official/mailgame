# Story 003: Client.Core input foundations — `RawInputRecord`, `IInputClock`, `PhysicalInput`, the shared `UiEventLog` ring and the fake-clock harness

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: M (~1 day; ~450 lines prod + tests)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: 2026-10-16

## Context

**GDD**: `design/gdd/input-controls.md` (R11 "Event log", Formulas "Clocks", Performance budget; AC H-R11-4 ring half, H-PERF-3 allocation clauses for the ring)
**Requirement**: `TR-input-001` (router core plain C#, fed abstract raw-input records + fake clock), `TR-input-019` (preallocated 4096-entry event-log ring shared with the UI event log), clock half of `TR-input-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0012: Input Router Core and Godot Input Adapter (§1 split, §9 event log, Key Interfaces `RawInputRecord` / `IInputClock`) — Accepted 2026-10-16 (primary); ADR-0001 (Client.Core is Godot-free `net8.0`, references only the Sim) secondary
**ADR Decision Summary**: The router core lives in `PerformativeMail.Client.Core/Input/`, plain C#, fed only `RawInputRecord`s and an injected `IInputClock` (monotonic integer ms), so every rule is testable headless with one fake clock. One preallocated `UiEventLog` ring of 4096 entries × 64 B lives in Client.Core, shared with the UI event log that Grid Inventory H-UI-1 and HUD (ADR-0018) read; this ADR makes the input router its first writer. Enum ids only, no strings; the 4097th entry overwrites the oldest.
**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Client.Core) | **Risk**: LOW
**Engine Notes**: None for this story. The Godot adapter that produces the records is stories 026–027.

**Control Manifest Rules (Foundation / Presentation)**:
- Required: the router core lives in Client.Core with no Godot reference, driven only by `RawInputRecord`s and `IInputClock`; steady state allocates 0 B (ADR-0012)
- Required: Client.Core is created by the first Input / HUD / network-seam story with `net8.0`, referencing only the Sim, and carries the same analyzers and `SIM_ASSERT` mapping (ADR-0001 §1)
- Forbidden: `using Godot` or a `Godot*` assembly in Client.Core; runtime NuGet packages; `float`/`double` in router maths; branching on `DEBUG`/`RELEASE`; static event handlers, threads or timers (ADR-0001)
- Guardrail: 0 B steady-state in the router core; each log entry ≤ 64 B; the ring allocated once

---

## Acceptance Criteria

*From GDD R11, H-R11-4 and H-PERF-3, scoped to this story:*

- [x] **AC-1 Records and clock.** In `PerformativeMail.Client.Core.Input`: `RawInputRecord` (readonly struct: `ArrivalMs` long, `Kind` `RawKind`, `Device` `DeviceClass`, `PadIndex` byte, `Input` `PhysicalInput`, `Pressed`, `Echo`, `Value`, `Value2` short — axis Q15 or mouse relative counts) exactly as ADR-0012 Key Interfaces; `RawKind` {`Key, MouseButton, MouseMotion, Wheel, PadButton, PadAxis, Synthetic`}; `DeviceClass` {`Keyboard, Mouse, Pad, Synthetic`}; `IInputClock { long NowMs { get; } }`. Tests use `FakeInputClock` with `ms = floor(tick × 1000 / 30) + subTickOffset` (GDD Formulas → Clocks), settable per tick and per sub-tick offset.
- [x] **AC-2 `PhysicalInput`.** A stable-named `PhysicalInput : ushort` enum covers every M-1a physical input used by the R6 default tables (KBM: W A S D, arrows, Shift, Space, E, Q, R, F, P, X, Tab, Esc, Enter, NumpadEnter, digits 1–8, mouse L/M/R, wheel up/down; pad: A B X Y, LB RB, L3 R3, D-pad four, Menu, View, LS x/y, RS x/y, LT, RT as axes) with a `ReadOnlySpan` name table and `TryParse(name)`. Names are the persistence format (physical keys **by name**, never Godot enum ints): a golden test pins every numeric value so a renumber is a deliberate edit, and name↔value round-trips all entries; an unknown name returns `false`.
- [x] **AC-3 `UiEventLog` ring.** One preallocated ring of 4096 entries, each a ≤ 64 B blittable struct (`Seq` u32, `TimeMs` i64, `Kind` byte, three u16 ids, reason byte, flags — enum ids only, no strings, no references), allocated once in the constructor. **H-R11-4 (ring half):** after 4097 writes `Count` is 4096, the oldest entry is gone, `OverwriteCount` is 1 and `TryGet(i)` enumerates oldest → newest in write order. `LogMotion(frame, timeMs, dx, dy)` (amended at story-done: the log has no clock, so the caller passes the time) keeps at most one mouse-motion entry per rendered frame (coalesced). A reader API (`Count`, `TryGet`, `Clear` for tests) exists for HUD / Dev Tooling / Grid Inventory H-UI-1; the `LogKind` enum covers `RawInput, GateFired, GateNotLive, GateMuted, GateSwallowed, Transition, HoldStart, HoldComplete, HoldCancel, Cue, Dropped, Config`.
- [x] **AC-4 Allocation.** After constructing the ring, 100 000 `Write`/`LogMotion` calls allocate 0 B (`GC.GetAllocatedBytesForCurrentThread()` delta, after 300 warm-up writes) and `Unsafe.SizeOf<UiEventEntry>() <= 64` (the full H-PERF-3 gate over the router is story 024).
- [x] **AC-5 Harness.** `tests/unit/input/InputTestHarness.cs` provides `Press(PhysicalInput, device)`, `Release(...)`, `Axis(...)`, `Wheel(...)`, `AdvanceTicks(n)` producing `RawInputRecord`s stamped from the fake clock, a `RecordingLatchWriter : IInputLatchWriter` and a `RecordingImmediateSink`, reused by stories 004–024.

---

## Implementation Notes

*Derived from ADR-0012 §1, §9 and Key Interfaces:*

- Files in `src/PerformativeMail.Client.Core/Input/` (namespace `PerformativeMail.Client.Core.Input`): `RawInputRecord.cs`, `RawKind.cs`, `DeviceClass.cs`, `PhysicalInput.cs`, `PhysicalInputNames.cs`, `IInputClock.cs`, `UiEventLog.cs`, `UiEventEntry.cs`, `LogKind.cs`. Tests in `tests/unit/input/` (namespace `PerformativeMail.Sim.Tests.Unit.Input`).
- The Client.Core assembly itself is created by `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` (csproj, analyzers, `AssemblyRulesTests`). If this story runs first, create the project exactly as that story's AC-1 specifies and let it adopt the file.
- The ring is a plain array with a head index; `Write` is branch-light and never grows. Reasons are `byte` enums owned by the writing story (e.g. cancel reasons from `EdgeReason` and Player Character's `CancelReason`) — the log stores the numeric id.
- `UiEventLog` is shared infrastructure: Grid Inventory H-UI-1 and HUD (ADR-0018, no epic yet) read it. Keep the public surface to *write*, *coalesced motion*, *read* and *clear*; no formatting here (the debug overlay formats).
- `FakeInputClock` is a test type; the production `IInputClock` (Stopwatch-based) lives in `src/Game/Input/` (story 026).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 004–007: router state, action map, validator, `InputRouter`. Story 019: settings. Story 026: the Godot-side clock and event conversion.
- Formatting / display of log lines (Dev Tooling, HUD).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- Logic: `tests/unit/input/UiEventLogTests.cs` (`[Trait("Id", "H-R11-4")]`), `tests/unit/input/PhysicalInputTests.cs`, `tests/unit/input/FakeClockTests.cs` — must exist and pass on net10.0 and net8.0

**Status**: [x] Created — tests/unit/input/UiEventLogTests.cs (H-R11-4), PhysicalInputTests.cs, FakeClockTests.cs, InputTestHarness.cs (all pass, four legs)

---

## Dependencies

- Depends on: Story 002 (Sim `IInputLatchWriter`, `FrameAction`), `production/epics/network-adapter-loopback/story-001-client-core-assembly-loopback-transport.md` (the Client.Core assembly and guards)
- Unlocks: Stories 004–024 (every router story), 026 (adapter conversion target)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 5/5 passing (no deferred items)
**Deviations** (advisory, logged as TD-076): AC-3 `LogMotion` takes `timeMs` (AC text amended); `RecordingImmediateSink` is a standalone recorder with no interface, because `IImmediateActionSink`/`ImmediateAction` belong to story 013; PhysicalInput names use a `Group.Item` scheme that no doc specifies yet; `None = 0` was added to `PhysicalInput` and `LogKind`; `UiEventEntry` is 24 B; AC-4 is measured with the repo's `AllocAssert.Zero` (best of 3 windows).
**Test Evidence**: Logic — tests/unit/input/UiEventLogTests.cs, PhysicalInputTests.cs, FakeClockTests.cs (+ InputTestHarness.cs)
**Code Review**: Complete — LP-CODE-REVIEW APPROVE (+ .NET pass; LP 2: harness Press/Release now throw for axis inputs, fixed in-story); QL-TEST-COVERAGE ADEQUATE (QL 1: blittable assertion added in-story). Remaining advisories go to TD-076.

