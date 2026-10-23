# Story 028: PCM-P2-1 cross-OS golden equality (Windows + Linux) and the blocking PCM-PERF-1 counters / 0 B gate

> **Epic**: Player Character & Movement
> **Status**: Deferred (Evan 2026-10-23 8:54 AM PT)
> **Deferred to**: M0 (out of the M-1a slice; split from story 010)
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S (~0.5 day; a Windows CI leg for the existing golden test plus the counter/0 B assertions — no new production code)
> **Manifest Version**: 2026-10-17.1
> **Last Updated**: 2026-10-23 (split by the m1a-replan PR)

> **DEFERRED 2026-10-23, not deleted. Approved by Evan 2026-10-23 8:54 AM PT, Mail Lead chat: "Approve the ADR-0024 additions and the extra deferrals (md-008, tc-025, md-010 localisation to md-012, and the split-off hardening stories)".** Earlier context (not the approval of this story): Mail Lead chat, 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024". 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it 'production ready' is not needed. Accessibility for example". 8:10 AM PT, approving errata E1–E6 and E9 (decision log 2026-10-23): "approve all". This story is freed by **E3 (ADR-0013 l.29 (1): the PCM-P2-1 cross-OS Windows + Linux golden moves to M0)** and **E2 (ADR-0013 l.29 (2): per-tick 0 B / counter checks are advisory, reported not blocking, until M0)**. Story 010 keeps the 600-tick golden as a single-platform (Linux CI) regression guard and the golden script that stories 019, 020, 022 and 025 reuse; the ACs below moved verbatim from it. Re-run `/story-readiness` when M0 planning starts.

## Context

**GDD**: `design/gdd/player-character-movement.md` (P2; Acceptance Criteria PCM-P2-1 "equal on Windows and Linux", PCM-PERF-1 counters, Game Feel "Performance budget")
**Requirement**: `TR-movement-022` (golden per-tick transform hash **equal on Windows and Linux**; the single-platform golden and "Sim references no Godot" stay in story 010), `TR-movement-021` (owner `CarrierStep` p99 ≤ 0.10 ms, 0 B/tick — registry shows a coverage gap, no ADR)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time. Acceptance ids: PCM-P2-1 (cross-OS half), PCM-PERF-1 (counters BLOCKING from M0).)*

**ADR Governing Implementation**: ADR-0001: Solution layout, build configurations and test framework (§7 the net8.0 test leg) — Accepted; ADR-0013 (§4 counters; Verification Required (1) "the 600-tick golden hash is equal on Windows and Linux CI on the net8.0 leg", (2) "PCM-PERF-1 counters (sub-steps ≤ 4 per tick, slides ≤ 3 per sub-step) and 0 B per tick") — Accepted; ADR-0006 (§5 T2 cross-platform goldens) secondary
**ADR Decision Summary**: `OwnedCarrier.Step` is a T2 surface: the same recorded input + snapshot script must produce identical per-tick transform hashes on every OS. PCM-PERF-1 asserts sub-steps ≤ 4 per tick and slides ≤ 3 per sub-step (always-on counters) and 0 B per tick after warm-up. Per E2/E3 both are M0 gates; at M-1a story 010 verifies the golden on Linux only and reports counters without gating.
**ADR Version**: ADR-0001 2026-10-16; ADR-0013 2026-10-16; ADR-0006 2026-10-16

**Engine**: Godot 4.7.1 .NET (Godot-free `net8.0` Sim code) | **Risk**: LOW
**Engine Notes**: CI today runs ubuntu-latest only (`.github/workflows/tests.yml`). There is **no Windows runner**; this story needs one (infrastructure; `devops-engineer`). Until it exists, "equal on Windows and Linux" is verified indirectly (both TFM legs equal one committed golden file) — record this as a known gap, do not claim Windows verification.

---

## Acceptance Criteria

*Moved verbatim from story 010 (ADR-0013 Verification Required (1) and (2)):*

- [ ] **PCM-P2-1 cross-OS equality (from story 010 Engine Notes / Implementation Notes; ADR-0013 l.29 (1)).** The 600-tick golden hash chain of story 010 (`tests/unit/player/golden/carrier-600.golden.txt`) is reproduced on a Windows CI job and on the Linux job, on the net8.0 leg (and net10.0), Debug and Release — the same committed golden file, no per-OS variant. Windows-vs-Linux: also record the final chain in `tests/unit/player/golden/` so a future Windows job compares against the same file. Add a TODO note in the story's Test Evidence, not a silent claim.
- [ ] **Counters (BLOCKING) (from story 010).** Across the script: `carrierSubSteps` max ≤ 4, `carrierSlideIterations` max ≤ 3, `carrierCandidateOverflow` = 0; after a 300-tick warm-up, 600 further `Step` calls allocate 0 B.
- [ ] **Windows CI job (from story 010 Out of Scope).** A Windows CI job (infrastructure; flag to `devops-engineer`) runs the story 010 golden test; its absence may no longer be reported as a known gap once this story is done.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/player/CarrierGoldenHashTests.cs` (`[Trait("Id", "PCM-P2-1")]`) passing on a Windows runner and on Linux against the same `carrier-600.golden.txt`; the counter / 0 B assertions (`[Trait("Id", "PCM-PERF-1")]`) flipped from advisory to blocking — must exist and pass on net10.0 and net8.0 (Debug + Release)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 010 (the golden test, script and golden file; this story adds the second OS and flips the counter gate); a Windows CI runner (infrastructure); `production/epics/simulation-core/story-029-zero-alloc-tick-timing-advisory.md` (the shared 0 B probe shape, Deferred to M0 with this story)
- Unlocks: —
