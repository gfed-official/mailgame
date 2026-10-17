# Gate Check: Concept → Systems Design

**Date**: 2026-10-15
**Checked by**: gate-check skill
**Config**: workflow: full · review_mode: full · qa.level: full (no test items at this gate) · team.size: studio

### Required Artifacts: 2/3 present
- [x] design/gdd/game-concept.md — exists, 14 KB, real content (compiled from design/spec; `INTENT UNKNOWN` markers remain)
- [x] Game pillars — design/gdd/game-pillars.md, 4 KB — **Status: Draft, creative-director approval pending**
- [ ] **Visual Identity Anchor** section in game-concept.md — **MISSING** (0 matches; only "3D, third-person, stylized low-poly" under Technical Considerations)

### Recommended
- [x] Concept prototype with PROCEED — prototypes/m1a-delivery-throwing-concept/REPORT.md (PROCEED accepted with CD-PLAYTEST concerns; hypothesis NOT FALSIFIED — preference signal only, 1 internal tester)
- [ ] Art bible design/art/art-bible.md — absent. Note: .claude/docs/workflow-catalog.yaml marks it required in the Concept phase; references/gate-systems-design.md does not. Reported as CONCERNS; the inconsistency should be reconciled.

### Quality Checks: 2/4 passing
- [x] Core loop described — Moment-to-Moment / Short-Term / Session / Long-Term all present
- [x] Target audience identified — friend groups of 2–8 on Discord/stream (age range `INTENT UNKNOWN`)
- [~] Concept reviewed — /design-review 2026-10-15 verdict NOT ASSESSED (the 8-section GDD checklist does not apply to a concept doc); concept-level findings 1–5 applied; review receipt hash matches the current file. Not MAJOR REVISION NEEDED, not a pass.
- [ ] Visual Identity Anchor content (one-line rule + ≥2 principles) — cannot be checked; section absent

### Artifact scan (artifact-check.sh --phase concept)
engine-setup PRESENT · game-concept PRESENT · map-systems PRESENT · art-bible ABSENT · brainstorm NO_CHECK · design-review-concept NO_CHECK (resolved manually above)

## Director Panel Assessment (4 of 4 — workflow: full)

Creative Director:  NOT READY
  Blocker: Visual Identity Anchor missing. Concerns: pillars v0.1 unapproved; anti-pillars still "proposed"; core fantasy inferred (`INTENT UNKNOWN`); combat/enemy fiction undefined; Pillar 5 (solo worth playing) vs Fellowship #1 tension undecided; concept review NOT ASSESSED rather than passed; catalog/gate art-bible inconsistency.

Technical Director: CONCERNS
  No technical blocker; concept feasible on Godot 4.7.1 (prototype runs). Carry to Technical Setup: installed engine is the standard build, no .NET build and no .NET SDK (commands.test "dotnet test" cannot run); high-level multiplayer API NOT SOURCEABLE in docs/engine-reference/godot/; Steam-from-C# spike unscheduled; project.godot lacks the Jolt physics setting declared in project.yaml; 4.7.1 vs 4.7.2 pin decision; VERSION.md "Installed at pin time" row stale; performance budgets in spec 07 written for the dedicated-server design, re-check against the v1 listen-server cut.

Producer:           CONCERNS
  All other required/quality items present. Run the two-friend greybox early in Systems Design, before any GDD beyond the M-1a set; limit the first GDD batch to Grid Inventory, Player Character, Mail, Throwing, Manual Delivery, Input, HUD; start logging hours now so PR-SCOPE can be re-run after M-1a; art workload remains unestimated.

Art Director:       NOT READY
  Visual Identity Anchor missing; nothing visual stands behind Pillar 2; enemy-identity/tone question (spec 13 §7) is past its latest responsible moment; CD-SYSTEMS and CD-PLAYTEST both left tone NOT ASSESSED for lack of it. /art-bible clears both this and the CD's blocker.

### Blockers
1. **No Visual Identity Anchor in design/gdd/game-concept.md.** Add a section with one visual rule and at least two supporting principles (one on mail/delivery-state readability, one on how the "played straight, silly surface" tone looks). `/art-bible` produces it together with the full bible; a short owner-written anchor also satisfies the gate.
2. **Combat/enemy fiction undefined** (spec 13 §7). Both NOT READY directors tied it to the anchor. A one-line answer is needed before combat/enemy GDDs.

### Recommendations
- Confirm the five pillar design tests, the anti-pillars and the inferred core fantasy; move game-pillars.md past Draft (closes review finding 6).
- Decide Pillar 5 vs Fellowship #1 before the run-loop and scaling GDDs.
- Two-friend greybox run before GDDs past M-1a (thresholds in the prototype REPORT.md).
- Technical Setup prep: Godot .NET build + .NET 8 SDK; update VERSION.md install row; decide the 4.7.2 pin; schedule the Steam C# spike before M0; add physics/3d/physics_engine to project.godot.
- Reconcile workflow-catalog.yaml vs gate-systems-design.md on the art bible.

### Verdict: FAIL
Two directors NOT READY on a required artifact that is absent. Stage remains **Concept**. GDD work on the M-1a set may proceed meanwhile; the stage advances only after the anchor exists and the gate is re-run.

Chain-of-Verification: 5 questions checked — verdict unchanged. [TOOL ACTION] grep for the anchor in game-concept.md: 0 hits. [TOOL ACTION] review-receipts.sh check: concept UNCHANGED since its review (applied fixes are what was reviewed). Pillars status re-read: Draft, pending. Minimal path to PASS: (1) Visual Identity Anchor, (2) enemy one-liner, (3) owner confirms pillars/anti-pillars/core fantasy — then re-run /gate-check.
