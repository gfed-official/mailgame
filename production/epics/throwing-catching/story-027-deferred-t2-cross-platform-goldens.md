# Story 027: T2 cross-platform goldens for `LaunchMath` / `Ballistic` / `FlightSweep` (Windows + Linux)

> **Epic**: Throwing & Catching
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; split from story 014)
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S (~0.5 day; one golden file, one test class, a Windows CI leg — no production code)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E4 (ADR-0015 l.38 / l.260: the T2 Windows + Linux goldens move to M0; the 10 000-throw preview = host equality and the conservation fuzz stay)**. Story 014 keeps the 10 000-throw preview = host equality and the no-dice replay; the AC below moved verbatim from it. Re-run `/story-readiness` when M0 planning starts (it needs a Windows CI runner).

## Context

**GDD**: `design/gdd/throwing-catching.md` (T2 "same integers on every platform"; AC TC-F1-1, TC-F2-1, TC-F4-1 as T2 goldens)
**Requirement**: `TR-throwing-004` (rounding identical on every platform), `TR-throwing-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: TC-F1-1 / TC-F2-1 / TC-F4-1 T2 goldens.)*

**ADR Governing Implementation**: ADR-0015: Shared Integer Ballistic, Sweep and Flight Resolution (Verification Required "T2 on Windows + Linux (ballistic is a shared pure function, ADR-0006 §5)"; Validation Criteria "T2 golden for `Ballistic`/`FlightSweep` on Windows and Linux") — Accepted 2026-10-16 (primary); ADR-0006 (§5 T2 cross-platform goldens) secondary — Accepted
**ADR Decision Summary**: `LaunchMath`, `Ballistic` and `FlightSweep` are shared pure functions, so a text golden of their vectors and a 200-throw digest must reproduce bit for bit on every OS and TFM leg. Per E4 the cross-platform (Windows) leg is an M0 gate; at M-1a story 014 proves preview = host on Linux only.
**ADR Version**: ADR-0015 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: Goldens run on the `net10.0` leg and the `net8.0` leg (`-p:PmTestTfm=net8.0`). The Windows leg is CI-only and no Windows runner exists today (`.github/workflows/tests.yml`): report it NOT RUN locally, never as passed (control manifest Toolchain note).

**Control Manifest Rules (Feature layer)**:
- Required: T2 cross-platform goldens for shared pure functions (ADR-0006 §5); recorded seeds live in the repo

---

## Acceptance Criteria

*Moved verbatim from story 014:*

- [ ] **T2 goldens (from story 014).** A golden file `tests/unit/throwing/golden/throwing-goldens.txt` holds, as text, the F1/F2/F4 vectors (TC-F1-1, TC-F2-1 p(1)/p(20)/p(21), TC-F4-1 a/b/c/h) plus a 200-throw digest over `LaunchMath` + `Ballistic` + `FlightSweep`; the test reproduces it on the `net10.0` and `net8.0` legs; the Windows leg is declared CI-only in the report.
- [ ] **Windows leg.** The same golden file is reproduced on a Windows CI job (net8.0 and net10.0); until the runner exists the report states NOT RUN, never passed.
- [ ] **Vector reuse (from story 014 Implementation Notes).** The goldens extend the vector set Player Character story 004 keeps for `SegmentIntervals`/`HeightfieldMarch` (`tests/unit/geometry/`); reuse those tables as data instead of retyping them.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/unit/throwing/ThrowGoldenTests.cs` (`[Trait("Id", "TC-F1-1")]`) with `tests/unit/throwing/golden/throwing-goldens.txt` committed — must exist and pass on net10.0 and net8.0, Linux and Windows

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 014 (the seeds, fixtures and the functions under test are already exercised there); Stories 001, 002, 003 (the functions); a Windows CI runner (infrastructure; `devops-engineer`)
- Unlocks: —
