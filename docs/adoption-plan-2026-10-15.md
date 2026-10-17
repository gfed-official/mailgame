# Adoption Plan

> **Generated**: 2026-10-15
> **Project phase**: Concept
> **Engine**: Godot 4.7.1 (C#)
> **Template version**: v1.0+

Work through these steps in order. Check off each item as you complete it.
Re-run `/adopt` anytime to check remaining gaps.

**Audit finding.** `design/spec/` (chapters 00–13) and `design/vision-mail-game-doc.md`
hold substantial design, but none of it is in `design/gdd/`, the location the
template's skills read. `gdd-structure-check.sh` found 0 of the 8 required GDD
sections in every chapter (vision doc: Overview only). Chapters are organised by
topic, not by system, so they are mapped below rather than renamed.

**Gap counts**: BLOCKING 0 · HIGH 6 · MEDIUM 4 · LOW 2
**Rigor**: full — all 8 GDD sections required.

---

## Step 1: Fix Blocking Gaps

None. No existing artifact is malformed in a way that misleads a skill, and no v1.0 config migration is needed (`migrate-v1-config.sh --dry-run`: project.yaml exists and the legacy files agree).

---

## Step 2: Fix High-Priority Gaps

### 2a. No game concept document
Source material: `design/spec/00-readme.md` (pillars, scope, assumptions), `design/vision-mail-game-doc.md`.
Fix: `/brainstorm` seeded from those, producing `design/gdd/game-concept.md`; or write it from the spec with `/reverse-document`.
**Time**: 1 session
- [ ] `design/gdd/game-concept.md` exists

### 2b. No systems index
Fix: `/map-systems` — decompose the spec into systems and dependency order (candidates: run structure, world generation, mail/inventory/economy, automation, combat, multiplayer, UI/UX).
**Time**: 1 session
- [ ] `design/gdd/systems-index.md` exists, no parenthetical status values

### 2c. Seven system chapters are not GDDs
Each needs the 8 required sections. Fix per chapter: `/design-system retrofit design/spec/<file>.md`. Do not rewrite content that exists — restructure it into the template sections and fill only true gaps (Player Fantasy, Acceptance Criteria are the likeliest absent).
**Time**: ~1 session each
- [ ] 01-run-structure.md
- [ ] 02-world-generation.md
- [ ] 03-mail-inventory-economy.md
- [ ] 04-automation.md
- [ ] 05-combat.md
- [ ] 06-multiplayer-netcode.md
- [ ] 09-ui-ux.md

Reference chapters 08 (data schemas), 10 (content tables), 11 (balance baselines) are **not** GDDs. Keep them in place and link from the GDDs' Formulas / Tuning Knobs sections.

### 2d. Architecture decisions are not ADRs
Decisions live in `design/spec/07-technical-architecture.md` (Sim/Presentation split, 30 Hz fixed tick, integer money, content-as-data, one code path for host/dedicated) and `13-v1-cut-and-decisions.md §3` (high-level multiplayer API replaces the custom Net layer).
Fix: `/architecture-decision` for each, with Status, ADR Dependencies, Engine Compatibility (Godot 4.7.1 — note NOT SOURCEABLE multiplayer-API gap in `docs/engine-reference/godot/VERSION.md`), GDD Requirements Addressed.
**Time**: 1 session
- [ ] ADRs written under `docs/architecture/`

### 2e. No TR registry / 2f. No control manifest
Created in Step 3 — listed here because they gate `/create-stories`.
- [ ] see 3a, 3b

---

## Step 3: Bootstrap Infrastructure

Order matters: ADRs first, then the registry.

### 3a. Register requirements (creates tr-registry.yaml)
Run `/architecture-review` after Steps 2c and 2d.
**Time**: 1 session
- [ ] `docs/architecture/tr-registry.yaml` created

### 3b. Create control manifest
Run `/create-control-manifest`
**Time**: 30 min
- [ ] `docs/architecture/control-manifest.md` created

### 3c. Create sprint tracking file
Run `/sprint-plan` (no sprint plan exists yet, so this is a new plan, not an update). Spec 13 §6 gives the build order: M-1 greybox fun test, M0 multiplayer on greybox, M1 one seeded run, M2 automation, M3 combat, M4 rogue-lite wrapper.
**Time**: 30 min
- [ ] `production/sprint-status.yaml` created

### 3d. Set authoritative project stage
At Concept there is no gate into the phase — Concept is the default stage; nothing to run yet. After Step 2 completes, run `/gate-check` to advance to Systems Design.

---

## Step 4: Medium-Priority Gaps

- [ ] **Pillars**: extract "Design pillars" from `00-readme.md` into `design/gdd/game-pillars.md` (`/brainstorm` or by hand). **15 min**
- [ ] **Entity registry**: `/consistency-check` returns NOT ASSESSED until `design/registry/entities.yaml` and GDDs exist; populated by `/design-system` as each GDD completes. Re-run after 2c.
- [ ] **Milestones**: convert `12-milestones.md` and `13 §6` into `production/milestones/` via `/sprint-plan` / milestone template. **30 min**
- [ ] **Traceability matrix**: `docs/architecture/requirements-traceability.md`, produced by `/architecture-review`. **with 3a**

---

## Step 5: Optional Improvements

- [ ] Archive `design/vision-mail-game-doc.md` (superseded; spec 13 §5 lists corrections to it). Do not delete without confirming — file deletions need approval.
- [ ] Resolve the open questions at `13-v1-cut-and-decisions.md §7` into the relevant GDDs/ADRs.

---

## What to Expect from Existing Stories

No stories exist yet. When `/create-stories` runs they will carry TR-IDs and manifest versions from the start.

---

## Review mode

Review mode resolves to `full` — from `modes.rigor: full`, unless something pins it. Change `modes.rigor`, or pin deliberately with `/settings --local modes.review_mode=<full|lean|solo>`.

---

## Re-run

Run `/adopt` again after completing Step 3 to verify all blocking and high gaps
are resolved. The new run will reflect the current state of the project.
