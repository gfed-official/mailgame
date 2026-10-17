# Story 022: Input replay corpus — 200 seeded + 10 adversarial traces and the "zero unauthored verbs" oracle (H-FX-1)

> **Epic**: Input & Controls (M-1a slice)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day; ~400 lines of generator + oracle + trace files)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

## Context

**GDD**: `design/gdd/input-controls.md` (Player Fantasy → Fantasy test 1 "Zero unauthored verbs"; AC H-FX-1; Edge Cases; G1–G9)
**Requirement**: `TR-input-021` (replay corpus — 200 seeded + 10 adversarial — asserts zero unauthored verbs). The registry already lists `production/epics/simulation-core/story-026-inmemory-recorder-replay.md` for this TR (the recorder / replay driver); this story is the **corpus and oracle** that the recorder's "Input replay corpus (TR-input-021) … their epics" out-of-scope line points at.
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time.)*

**ADR Governing Implementation**: ADR-0006: Numerics, Determinism Rules, Canonical Dump and Replay (§7 recorder and replay — recorded ops replay bit-identically) — Accepted 2026-10-16 (primary for the replay mechanism); ADR-0012 (§6 "Bypass requests are logged with their issuing tick (H-FX-1 replay corpus)", Validation Criteria "H-FX-1 replay corpus shows zero misfires") secondary
**ADR Decision Summary**: `InMemoryRecorder` records the ordered ops (with reply), transform samples and overflow marks; `ReplayDriver` re-enqueues them and reproduces the world bit-identically. Bypass (`Immediate`) requests are ops and carry their issuing tick, so the corpus includes them. ADR-0012 §4 says "Replay records frames" but ADR-0006 §7's `IOpRecorder` has no `InputFrame` record: the corpus is therefore replayed at the **trace** level (trace → router → latch → Sim, deterministic given the trace), and the recorded ops are what the oracle and the determinism check read (gap noted in EPIC.md). The input layer's promise — every mistake is the player's — is tested by checking that nothing leaves a bag unless the player authored it.
**ADR Version**: 2026-10-16 (ADR-0006); 2026-10-16 (ADR-0012)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim + Client.Core) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation)**:
- Required: replay is deterministic on the same content hash, settings and seed; the corpus is data checked into the repo, not generated unseeded at test time (ADR-0006 §7)
- Forbidden: `System.Random` in anything that decides a trace (use a seeded `SplitMix`-style generator defined in the test project so traces are identical across .NET versions)
- Guardrail: the whole corpus runs in the default test job (< ~60 s)

---

## Acceptance Criteria

*From GDD H-FX-1, scoped to this story:*

- [ ] **Corpus.** `tests/replay/input/` holds `seeds.txt` (200 seeds) and 10 authored adversarial trace files (a small line format: `tick device input press|release`, plus `hitch N`, `focus lost|gained`, `pad unplug|plug`, `state <script>` lines). The 10 authored traces are: (1) press + release of E inside one tick at a mailbox; (2) LT twitch of 160 ms; (3) Tab pressed mid-hold; (4) focus loss mid-aim; (5) pad unplug mid-throw (Hold and Toggle); (6) a 3-tick hitch containing a hold's release at 10 / 12 and at 12 / 12; (7) E and pad X pressed together then one released; (8) a Toggle second press during a deliver hold; (9) panel opened with A and X held; (10) Pause opened while aiming, then resumed with RMB still down. A trace generator turns each seed into a trace of 600 ticks from a weighted alphabet of all M-1a inputs, state changes, hitches, focus and pad events.
- [ ] **Oracle (H-FX-1).** Replaying every trace through the story 020 harness (real router, `InputLatch`, Player Character interaction, Manual Delivery, Throwing sink, Grid Inventory drop) with a recorder attached, **every stack leaving a bag in the log** — `deliver_matching`, `throw` and `drop` requests, plus bag-departure events — is caused by exactly one of: (i) the same latched press as a **completed** deliver hold (`HoldStart` … `HoldComplete` with no intervening `HoldCancel`, same press id, target = latched target), (ii) a throw committed with `aimHeld ≥ throwMinAim` while Aiming, or (iii) a `Drop` press that fired. **Misfire count = 0** across all 210 traces; a deliberately planted bug (e.g. a router variant that fires Tool on release, or a deliver completed after a cancel) makes the oracle fail — the test includes that mutation run (only one mutation per run).
- [ ] **Bypass requests included.** The recorded log contains the `Immediate` requests with their issuing tick (story 013); a trace that only differs in an `Immediate` verb changes the log.
- [ ] **Determinism.** Each trace is replayed twice and through `ReplayDriver` (`production/epics/simulation-core/story-026-inmemory-recorder-replay.md`); the canonical state dump hashes are identical; the corpus run is identical on the net8.0 leg.
- [ ] **Reporting.** A failing trace prints its seed / file name, the tick, the offending request and the router log lines of the causing press, so it can be minimised; a `tests/replay/input/README.md` states the line format and how to add a trace.

---

## Implementation Notes

*Derived from the GDD fantasy test and ADR-0012 §6:*

- Files: `tests/replay/input/seeds.txt`, `adversarial/01-subtick-tap.trace` … `10-pause-aim.trace`, `tests/integration/input/ReplayCorpusTests.cs`, `TraceFormat.cs`, `TraceGenerator.cs`, `UnauthoredVerbOracle.cs`. The test project compiles `tests/integration/**`; load the data files by walking up to the repo root (`PerformativeMail.sln`), not by relative output path.
- The oracle reads the **router log** (`UiEventLog`: press id on `GateFired`, `HoldStart/Complete/Cancel`, `Transition`) and the recorded request stream; it does not re-derive router rules.
- Keep traces short and the alphabet weighted towards the dangerous inputs (E, RMB / LT, Q, Tab, Esc, focus loss, hitch) so 200 seeds actually hit the interesting interleavings; record the seed → coverage counters (holds started / completed / cancelled, aims committed / cancelled, drops) in the test output.
- H-FX-2 (≥ 8 sessions, MANUAL, M0) is not this story.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- The recorder / replay driver (sim-core 026). Story 020 / 021 (the harness and targeted ACs). M0: H-FX-2 manual sessions, catch traces. Playtest evidence.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/input/ReplayCorpusTests.cs` (`[Trait("Id", "H-FX-1")]`) with the data under `tests/replay/input/` — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 020, 021; `production/epics/simulation-core/story-026-inmemory-recorder-replay.md` (recorder, `ReplayDriver`); `production/epics/grid-inventory/story-020-drop-op.md` (Drop request / bag departure); `production/epics/throwing-catching/story-007-throw-release-builder-client-cooldown-reply.md`; `production/epics/manual-delivery/story-004-press-latch-cancels-request.md`
- Unlocks: Story 023; the M0 H-FX-2 manual sessions
