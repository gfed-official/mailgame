# Story 014: Preview = host over 10 000 throws, the T2 cross-platform goldens and the no-dice replay

> **Epic**: Throwing & Catching (M-1a slice)
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M (~1 day; ~350 lines of harness + tests, almost no production code)
> **Manifest Version**: 2026-10-16.2
> **Last Updated**: —

> **Integration / determinism story (1 of 2).** The proof of the epic's central promise: *the arc is honest*. Nothing here adds behaviour; it pins the preview and the host to the same integers and fails loudly if they ever drift.

## Context

**GDD**: `design/gdd/throwing-catching.md` (Game Feel "Feel tests (1) The arc is honest"; T3 "The preview is an honest forecast, from one shared function"; T5 "No dice in the Sim"; AC TC-FE-1, TC-T5-1, TC-F2-1 b)
**Requirement**: `TR-throwing-003` (preview calls the same Sim function as the host; identical over 10 000 throws), `TR-throwing-002` (no RNG in throwing), `TR-throwing-001`, `TR-throwing-004` (rounding identical on every platform)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-FE-1 [FUZZ], TC-T5-1 a, TC-F2-1 b, TC-F1-1/TC-F2-1 T2 goldens.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (§8 "TR-throwing-003 test: 10 000 seeded throws over a fixture street — preview contact (receiver, f, point) equals the host's for static receivers, bit for bit"; Validation Criteria) — Accepted 2026-10-16 (primary); ADR-0006 (§5 T2 cross-platform goldens, replay) and ADR-0005 (no Throwing stream) secondary — Accepted
**ADR Decision Summary**: The preview and the host call the very same `LaunchMath`, `Ballistic` and `FlightSweep` functions, so their integers are identical for static geometry by construction; the validation criteria require the 10 000-throw equality, the T2 golden for `Ballistic`/`FlightSweep` on Windows and Linux, and every T6 terminal row exercised. Throwing draws no RNG.
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16; ADR-0005 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Goldens run on the `net10.0` leg and the `net8.0` leg (`-p:PmTestTfm=net8.0`; the .NET 8 runtime is installed locally). The Windows leg is CI-only: report it NOT RUN locally, never as passed (control manifest Toolchain note).

**Control Manifest Rules (Feature layer)**:
- Required: host, preview and every peer call the same `LaunchMath`, `Ballistic`, `FlightSweep` — no second implementation anywhere (ADR-0015, ADR-0016 §3)
- Required: T2 cross-platform goldens for shared pure functions (ADR-0006 §5); recorded seeds live in the repo
- Forbidden: a float oracle inside the Sim; RNG in Throwing
- Guardrail: the whole fuzz ≤ ~60 s on the dev machine (shrink the per-seed tick work, not the 10 000)

---

## Acceptance Criteria

*From GDD `design/gdd/throwing-catching.md` (TC-FE-1, TC-T5-1, TC-F2-1 b) and ADR-0015 Validation Criteria, scoped to this story:*

- [ ] **TC-FE-1 [FUZZ].** Over 10 000 seeded random static-geometry throws on the `M1aStreet` fixture — random feet on walkable tiles, random yaw and pitch (clamped as the camera), all three classes, random deliveries state (open / closed / locked / within grace), matching and non-matching addresses — the preview's published end class, end point and contact `(receiver, f, point)` equal the host flight's first contact, bit for bit, and the class maps to the host terminal: Accept → MAILBOX_ACCEPT, Misdeliver → MAILBOX_MISDELIVER, Reject → MAILBOX_REJECT, Ground and Blocked → GROUND, Water → WATER, Timeout → TIMEOUT. The host side is the **real** pipeline: a `ThrowRelease` through the op ring, then phase-4 steps to the terminal.
- [ ] **Seeds are recorded.** The seeds live in `tests/fuzz/throwing/seeds.txt` (one per line, the GDD path); the test project copies the file to its output as content; a failure prints the seed and the first differing field. Adding a regression seed is a one-line change.
- [ ] **The comparator can fail.** A negative-control test perturbs the preview by 1 mm in origin x (and, separately, switches one RH call to `Fix.MulDivRH`) and asserts the fuzz reports a mismatch within the first 1 000 seeds. (A gate that has never been seen to fail is not a gate.)
- [ ] **Coverage of terminals.** The fuzz run records how many seeds hit each terminal and each Reject reason and fails if any of MAILBOX_ACCEPT, MAILBOX_MISDELIVER, MAILBOX_REJECT (closed and locked), GROUND on terrain, GROUND on a solid, WATER and TIMEOUT has fewer than 20 hits (so a degenerate generator cannot pass vacuously). The seed generator is documented.
- [ ] **Chest-probe and clamp cases.** A seed subset places the thrower at arm's length from a house wall (chest probe blocked) and with a 1501 mm claim error (`originClamped`): preview and host agree when the preview uses the host-chosen origin; the mismatch case (preview used the claim, host used the newest sample) is reported by the harness as the *known, expected* divergence and counted, never silently equal.
- [ ] **T2 goldens.** A golden file `tests/unit/throwing/golden/throwing-goldens.txt` holds, as text, the F1/F2/F4 vectors (TC-F1-1, TC-F2-1 p(1)/p(20)/p(21), TC-F4-1 a/b/c/h) plus a 200-throw digest over `LaunchMath` + `Ballistic` + `FlightSweep`; the test reproduces it on the `net10.0` and `net8.0` legs; the Windows leg is declared CI-only in the report.
- [ ] **TC-T5-1 a.** One release replayed under 1 000 run seeds (`SimBuildArgs.runSeed`) produces identical launches, terminals and Throwing dump hashes, and the RNG draw counters show **0** draws attributable to Throwing (Throwing's constructor has no `IRngStreams` parameter — an architecture test).
- [ ] **TC-F2-1 b (rejoiner).** For random flights, `Ballistic.PositionAt(launch, k)` evaluated from the `ThrowLaunched` event integers alone (as a joining peer would) equals the host's age-k segment endpoint for every k in 1…90.

---

## Implementation Notes

*Derived from ADR-0015 §8 and Validation Criteria:*

- Files: `tests/unit/throwing/PreviewHostEqualityFuzzTests.cs`, `tests/unit/throwing/ThrowGoldenTests.cs`, `tests/fuzz/throwing/seeds.txt`, `tests/unit/throwing/golden/throwing-goldens.txt`; fixtures reuse `WorldTablesFixture`/`M1aStreet` (`production/epics/world-stub/story-006-m1a-street-and-fixtures.md`) and the Mail/Inventory test builders of stories 010–012.
- Drive the preview through the **real** `ThrowPreviewStep` with test ports (carrier view, selected entry, mirrors) rather than calling `FlightSweep` directly, so the test covers the facts-building path; drive the host through `SimWorld.Tick()` with the real Throwing, Inventory and Mail systems and a stub wallet.
- Seed generator: a seeded `SplitMix64`/PCG from the test project (`production/epics/simulation-core/story-008-rng-primitives-golden-vectors.md`), never `System.Random`. Document the distributions (feet near mailboxes 50 %, uniform otherwise; pitch uniform −60°…+70°).
- The goldens extend the vector set Player Character story 004 keeps for `SegmentIntervals`/`HeightfieldMarch` (`tests/unit/geometry/`); reuse those tables as data instead of retyping them.
- This story is deliberately test-only; if the fuzz exposes a drift, fix the production story that owns the function (001–004, 010–012) and note it in this story's Last Updated.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- Story 015: conservation fuzz, canonical dump/replay in the SC-DET-1 harness, performance. Story 004: the preview itself. Stories 001–003, 010–012: the functions under test.
- Catcher and teammate receivers (M0 / story 023): the equality claim is for **static** geometry only, as the GDD scopes it.

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/unit/throwing/PreviewHostEqualityFuzzTests.cs` (`[Trait("Id", "TC-FE-1")]`), `tests/unit/throwing/ThrowGoldenTests.cs` (`[Trait("Id", "TC-T5-1")]`) — must exist and pass on net10.0 and net8.0

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 003, 004, 010, 011, 012; `production/epics/world-stub/story-006-m1a-street-and-fixtures.md`; `production/epics/mail-destinations/story-010-client-mirrors-and-facts.md`; `production/epics/simulation-core/story-009-rng-registry-owner-views.md` (draw counters); `production/epics/simulation-core/story-027-sc-det-1-determinism-harness.md`
- Unlocks: Story 015
