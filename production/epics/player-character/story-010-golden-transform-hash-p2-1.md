# Story 010: PCM-P2-1 — the 600-tick golden per-tick transform hash (single-platform Linux CI regression guard)

> **Epic**: Player Character & Movement (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S–M (~0.75 day; ~300 lines tests/tooling + a committed golden file)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-27 (readiness pass: manifest re-check, mechanical AC fixes; the Replay AC is open for Evan)

> **Split 2026-10-23.** The cross-OS half of PCM-P2-1 (Windows + Linux equality, the Windows CI job; ADR-0013 l.29 (1), erratum E3) and the blocking PCM-PERF-1 counters / 0 B gate (ADR-0013 l.29 (2), erratum E2) moved verbatim to `story-028-deferred-golden-cross-os-equality-and-blocking-counters.md` (Deferred to M0). Evan, Mail Lead chat 2026-10-23 8:10 AM PT, approving errata E1–E6 and E9: "approve all". What stays here is the 600-tick golden on the CI's Linux leg as a single-platform regression guard, and the golden script that stories 019, 020, 022 and 025 reuse.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P2; Acceptance Criteria PCM-P2-1, PCM-PERF-1 counters, Game Feel "Performance budget")
**Requirement**: `TR-movement-022` (golden per-tick transform hash — the single-platform Linux half and "Sim references no Godot"; the Windows + Linux equality half is story 028, M0), `TR-movement-001`, `TR-movement-004` (recorded snapshot script through stubbed sources), `TR-movement-021` (owner `CarrierStep` p99 ≤ 0.10 ms — advisory; registry shows a coverage gap, no ADR; the 0 B/tick gate is story 028)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P2-1 (single-platform), PCM-PERF-1 (wall-clock ADVISORY; counters and 0 B are story 028's).)*

**ADR Governing Implementation**: ADR-0001: Solution layout, build configurations and test framework (§7 the net8.0 test leg) — Accepted (primary for TR-movement-022); ADR-0013 (§1, §4 counters, Validation Criteria — the 600-tick script), ADR-0006 (§4 canonical dump, field-by-field hashing; T2) secondary
**ADR Decision Summary**: `OwnedCarrier.Step` is a T2 surface: the same recorded input + snapshot script must produce identical per-tick transform hashes on every OS and on the net8.0 leg — at M-1a this story pins the hash on the Linux CI legs only (E3 defers the cross-OS proof to story 028, M0). Hashing is field-wise only (never `MemoryMarshal.AsBytes` on a struct — padding is indeterminate). PCM-PERF-1's counter and 0 B gates (sub-steps ≤ 4, slides ≤ 3, 0 B per tick) are story 028's (E2); here they are only reported.
**ADR Version**: ADR-0001 2026-10-16; ADR-0013 2026-10-16 (errata 2026-10-17 and E2/E3 2026-10-23); ADR-0006 2026-10-16 (errata 2026-10-17; none changes this story's rules); ADR-0026 2026-10-16 (manifest 2026-10-17.1 rules below)

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: CI today runs ubuntu-latest only, on the net10.0 and net8.0 test legs × Debug/Release (`.github/workflows/tests.yml`). This story claims Linux only; the Windows leg and the "equal on Windows and Linux" claim are story 028's (M0). Do not claim Windows verification here.

**Control Manifest Rules (Feature layer)**:
- Required: Sim references no Godot assembly; the net8.0 test leg is mandatory for T2 claims (ADR-0001 §7)
- Required: hashing is field-by-field, never a struct blit (ADR-0006 §4)
- Required (ADR-0026 §2, manifest 2026-10-17.1): corner heights lie within ±1,000,000 mm (`WorldConstants.MaxCornerHeightMm`; `SetCorners` rejects others). At the far map edge (x = TilesX·S or z = TilesZ·S) sampling uses the last tile with offset exactly S — the script's map-edge clamp leg runs against this rule
- Required (ADR-0026 §7, manifest 2026-10-17.1): FX-Golden has no Post Office, so it is built with `WorldTablesBuilder.Build(slopeMaxPm, swimEnterMm)` (the `Build(int, int)` overload, as `WorldTablesFixture` does), never `Build(EntityIds, …)` and never `Build()`
- Required (ADR-0001 §7): perf-trait tests run in the non-parallel collection (`[Collection(NonParallelCollection.Name)]`)
- Guardrail: owner `CarrierStep` + focus p99 ≤ 0.10 ms (advisory M-1a); 0 B per tick after 300 warm-up ticks and the probe counters are **reported, not gated** at M-1a (E2; the blocking form is story 028)

---

## Acceptance Criteria

*From GDD `design/gdd/player-character-movement.md` (PCM-P2-1, PCM-PERF-1), scoped to this story:*

- [ ] **PCM-P2-1 (single-platform).** Given a committed 600-tick script (move/look/sprint/jump/aim inputs per tick plus per-tick remote-capsule and support snapshots through `ScriptedRemoteCapsuleSource` / `ScriptedSupportSource`, which are empty or inert at M-1a) run through `OwnedCarrier.Step` over the `FX-Golden` world, the per-tick transform hash chain equals the committed golden file on the net10.0 and net8.0 legs, Debug and Release, on the CI's Linux runner (the Windows leg is story 028).
- [ ] **Script coverage.** The script exercises every solver path built by stories 005–009: walk, sprint, stop, 180° turn, jump (plain, buffered, coyote off a ledge, extra jump), landing, a 700 ‰ ramp climb and a 702 ‰ slide, a 400 mm step-up and a 401 mm block, a ceiling hit, a thin-wall tunnel attempt, wading, swim entry/exit and haul-out, and the map-edge clamp. A coverage assertion counts each path over the run and fails if any count is zero, so the golden cannot silently stop exercising a path. Observables: solver counters from `CarrierScratch` / `CarrierCounterFlush` (`carrierDepenetrations`, `carrierStepClamped`; `carrierSubSteps` and `carrierSlideIterations` are per-tick maxima, so assert a tick where each exceeds 1); `Loco` transitions (`Grounded`→`Airborne` for each jump, `Airborne`→`Grounded` for landing, entry to and exit from `Swimming`); `CarrierFlags` bits (`ExtraJumpUsed` set, `Wading` set, `Sprinting` set); `HaulOutTicks` > 0 for haul-out; and, for paths with no counter or flag (coyote jump off a ledge, ceiling hit, 401 mm block, thin-wall tunnel attempt, map-edge clamp), a per-path state assertion on the scripted tick named in the test (for example the clamped position at the edge, `Vy` ≤ 0 on the tick after the ceiling contact).
- [ ] **Hash definition.** Per tick: SHA-256 over `X, Y, Z, RemX, RemZ, RemY, Vx, Vz, Vy, BodyYaw, Loco, Flags, CoyoteLeft, JumpBufferLeft, HaulOutTicks` written little-endian field by field with `CanonicalWriter`, in the order and widths of the Players dump section (`PlayersStateContributor.WriteCarrier`: `X, Y, Z, RemX, RemZ, RemY, Vx, Vz, Vy` i32, `BodyYaw` u16, `Loco` u8, `Flags` u16, `CoyoteLeft, JumpBufferLeft, HaulOutTicks` u8) — the transform subset only (support, push-pair and `WadeSlowPm` fields are not part of the transform hash); SHA-256 is the Sim's `Sha256` (`PerformativeMail.Sim/Content/Hashing`); chain `h_t = SHA-256(h_{t−1} ‖ tickHash_t)`. The golden file stores the chain value at ticks 1, 50, 100, …, 600 and the final value, plus the tick-600 raw state for human diffing.
- [ ] **Mutation check.** Each of at least three named single-unit perturbations, each on a tick where that input is read (for example `moveX` +1 on a walking tick, `lookYaw` +1 on a tick with move input, `JumpPressed` added on a grounded tick), changes the final hash; the test lists them. (Input clamping, mid-air jump presses with no extra jump left and look yaw without move input are not read, so "any input" is not claimed; no rounding test seam is added.)
- [ ] **PCM-PERF-1 microbenchmark (ADVISORY).** A `[Trait("Category","Perf")]` test in `[Collection(NonParallelCollection.Name)]` runs the worst-case fixture (1 carrier, 4 sub-steps, 64 candidates) 10 000× in Release and reports p99 per call; it logs, never fails, until the M1 reference machine exists (TR-movement-021 has no ADR coverage). The 8-player / 2 000-anchor fixture is owned by Story 018/020 (focus) and stays BLOCKED on the tick-driver spike (GDD OQ6).
- [ ] **No Godot.** The Sim assembly references no `Godot.*` assembly and `PerformativeMail.Sim/Players` references no physics-named type — a type whose name contains `CharacterBody`, `PhysicsBody`, `PhysicsServer`, `RigidBody`, `CollisionShape` or `Jolt` (architecture test, extending sim-core story 028's SC-ARCH-1 rule set; the Godot-assembly half already exists in `AssemblyRulesTests` / `CarrierPurityTests`; the name list is a Mail Lead planning default, pending Evan).
- [ ] **Replay.** *(OPEN — needs Evan, readiness 2026-10-27: sim-core 026 is Complete, but `ReplayDriver` replays `SimWorld` ops and transform samples, and `OwnedCarrier.Step` has no production caller until story 020 binds `CarrierStep`, so this AC cannot pass or skip as written. Do not start this AC until Evan rules.)* The same script replayed through the sim-core recorder/replay path (`production/epics/simulation-core/story-026-inmemory-recorder-replay.md`) yields the identical chain (skip with a clear `Skip` reason only if story 026 is not yet implemented — never silently pass).

---

## Implementation Notes

*Derived from ADR-0013 §1/§4, ADR-0006 §4/§7, ADR-0001 §7:*

- `FX-Golden` is a small world built with `WorldTablesBuilder` in test code (documented in the test file; it extends the GDD's FX-Dry, and the test header says so): a 128 000 mm dry plane at +1000 mm (FX-Dry), a 1400/1404 mm ramp tile pair (FX-Ramp), an FX-Beach/FX-Deep strip, and a handful of AABBs (a 400 mm step, a 401 mm block, a thin wall, a 1.5 m ceiling slab, a 1200 mm water ledge). `WorldTablesFixture` (tests/unit/world, internal, same test assembly) only builds single-feature worlds without solids, so compose FX-Golden directly (`SetCorners`, `AddSolid`, `Build(SlopeMaxPm, SwimEnterMm)`), reusing the fixture's constants; do not duplicate fixture geometry beyond what the script needs.
- The golden file lives at `tests/unit/player/golden/carrier-600.golden.txt` (the path story 028 also cites; the test in `tests/integration/player/` reads it relative to the repo root, since the test project copies no data files) (text, LF endings; committed). The test regenerates and diffs; a `--update-golden` style switch is **not** allowed to run in CI (guard on an environment variable, fail if set in CI). Document the regeneration procedure in the test file header.
- Log the solver counters (`carrierSubSteps`, `carrierSlideIterations`, `carrierCandidateOverflow`) and the post-warm-up allocated bytes in the test output so story 028 (M0) can flip them to blocking without re-instrumenting; nothing fails on them here.
- Player push (F6) and support carry (P7) are out of M-1a scope, so the scripted capsule/support snapshots do not change the result; **re-record the golden when push lands in M0**. State this in the test header.
- Keep script data in a compact committed text/CSV form under `tests/unit/player/golden/carrier-600.script.csv` (tick, moveX, moveY, lookYaw, flags). No `System.Random`; any derived data uses the Sim's `Pcg32` (`PerformativeMail.Sim/Primitives`) with a fixed seed.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 028 (Deferred, M0): the Windows CI job and the Windows + Linux golden equality; the blocking PCM-PERF-1 counters / 0 B gate. Full-tick `SimWorld` determinism (SC-DET-1): `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`.
- The PCM-PERF-1 8-player fixture and host-side budget (needs stories 018–020 and the tick-driver spike).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/player/CarrierGoldenHashTests.cs` (`[Trait("Id", "PCM-P2-1")]`) with `tests/unit/player/golden/carrier-600.golden.txt` and `carrier-600.script.csv` committed — must exist and pass on net10.0 and net8.0 (Debug + Release) on the Linux CI runner; counters and allocated bytes appear in the test output (advisory)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 005, 006, 007, 008, 009 (all solver paths. Story 027's movement rulings are **Deferred** to M1 by Evan 2026-10-23 7:13 AM PT, so capture the goldens on the current solver and re-capture them, as a reviewed golden change, when 027 lands), Story 002 (scripted sources), `production/epics/simulation-core/story-019-canonical-state-dump.md` (`CanonicalWriter`), `production/epics/simulation-core/story-001-build-config-sim-assert-ci-scaffold.md` (net8.0 leg), `production/epics/simulation-core/story-028-architecture-rules-sc-arch-1.md` (arch-test rule set), `production/epics/simulation-core/story-026-inmemory-recorder-replay.md` (replay check)
- Unlocks: Story 020 (the client step may be integrated against a locked solver); Stories 019, 022, 025 (the golden script); `/gate-check` evidence for TR-movement-022 (single-platform half); Story 028 (Deferred, M0)
