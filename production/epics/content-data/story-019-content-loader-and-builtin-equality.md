# Story 019: ContentLoader.Load — balance files, five-stage orchestration, shipped JSONC equals BuiltInContent

> **Epic**: Content & Data Definitions (incl. StatSheet)
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Estimate**: M (~1 day, including authoring `content/**/*.jsonc` for the M-1a set)
> **Manifest Version**: 2026-10-16
> **Last Updated**: 2026-10-28 (PR #53 Mail Review r1 B1: the hand-offs from story 018 recorded in Implementation Notes; the `shares` half of the Hash-invariance key-order variant marked blocked on Evan (TD-075(3)); AC text otherwise unchanged). 2026-10-21 08:08 PT (Implementation Notes: `view` required, so the explicit-fields rule is a load requirement for it; time fields stay ms — owner rulings 08:08 PT, ADR-0010 Errata 2026-10-21; no AC changes). 2026-10-21 (Implementation Note: shipped files and clean fixtures spell out `carryByHand`, `acceptsInsert` and `view` — owner ruling on the ADR-0010 defaults, GDD CD10 note 2026-10-21; no AC changes)

## Context

**GDD**: `design/gdd/content-data-definitions.md` (CD1, CD2, CD9, CD21, CD25; Loading edge cases)
**Requirement**: `TR-content-020`, `TR-content-004`, `TR-content-013` (file half)
*(Requirement text: `docs/architecture/tr-registry.yaml`.)*

**ADR Governing Implementation**: ADR-0010: Content Format, Loading, ContentSet and StatSheet (§4 pipeline, §1 layout, §6 hash)
**ADR Decision Summary**:
- **Pipeline.** `ContentLoader.Load(IContentSource) → ContentLoadResult` runs five stages: syntax, schema, build, references, whole-set. It reports every reachable error and skips (and counts) rules whose inputs failed. The output is one immutable `ContentSet`.
- **Layout.** Balance lives in `content/balance/<owner>.jsonc`. Presentation data lives under `content/presentation/` and is not hashed.
- **Equality test.** ADR-0010 schedules a test asserting that the JSONC files and `BuiltInContent` produce identical canonical bytes for the M-1a subset at M0. **The epic pulls it forward to M-1a** (decision logged at /create-epics).

**ADR Version**: 2026-10-16

**Engine**: Godot 4.7.1 (Godot-free Sim code) | **Risk**: LOW
**Engine Notes**: Packaged (PCK) boot, `manifest.txt` and `ContentHost` are deferred: spike S1 and ADR-0019 (not written). At M-1a the loader runs over `DirectorySource`/`InMemorySource` only, in tests and tools.

**Control Manifest Rules (Foundation)**:
- Required: `ContentLoader.Load(IContentSource)` runs five stages (syntax, schema, build, references, whole-set), reports all reachable errors, and produces one immutable `ContentSet`. That set is held for the process lifetime and passed by reference to `SimComposition.Build`. (ADR-0010 §4)
- Required: all Sim content is JSONC under `content/`, one directory per def type. Balance goes in `content/balance/<owner>.jsonc` (`SimSettings` keys in `balance/sim.jsonc`). Presentation data under `content/presentation/` is not hashed. (ADR-0010 §1)
- Required: `content/manifest.txt` is never committed. (ADR-0010 §1)
- Forbidden: swapping a new `ContentSet` into a running `SimWorld` in place. (ADR-0010 §4)

---

## Acceptance Criteria

*From GDD `design/gdd/content-data-definitions.md`, scoped to this story:*

- [ ] **Shipped files load.** `ContentLoader.Load(new DirectorySource("content/"))` over the shipped M-1a files returns zero Errors and a `ContentSet`. That set's canonical bytes (story 013) are **identical** to `BuiltInContent.CreateM1a()`'s. (Epic DoD equality test; ADR-0010 Validation Criteria, M-1a form)
- [ ] **Stage gating.** A stage-1 syntax error in one file stops stages 3–5 and returns no `ContentSet`. The fixture file also holds a CV-GRD-01 input. The result lists at least one skipped (rule, def) pair naming the blocking file, and a second, clean file is still parsed. (CDD-LOAD-3)
- [ ] **Hash invariance.** One fixture is varied by:
  - line endings (LF, CRLF), a BOM, comments, whitespace;
  - key order within an object (**the `shares` map part is blocked on Evan (TD-075(3))**: story 018 hashes MailMix `shares` in authored order, so swapping two `shares` keys changes the hash today; this variant can't be written for `shares` until Evan rules whether map-key order counts as "key order" — field-key order is unaffected);
  - the order of a set-valued list.

  Every variant yields the same `ContentHash`. Changing `price` 400 → 401, any balance key, or an optional field's presence changes it. (CDD-PARSE-1, CDD-HASH-1)
- [ ] **Balance files.**
  - Each `content/balance/<owner>.jsonc` is an object of `key: value` or `key: { value, safe: [min, max] }`.
  - The owner is the file name, so `player.jsonc` holds `player.*`.
  - The story 004 checks apply to parsed files: a key in the wrong owner's file, a key in two files, or an unregistered key each give one CV-GEN-13, and a value outside `safe` gives CV-GEN-20.

  (CDD-SCHEMA-5, file form)
- [ ] **Presentation exclusion.** Files under `content/presentation/` are excluded from Sim loading and from the hash; changing a value there leaves `ContentHash` unchanged. No player-data type (MetaProfile, settings, input bindings) appears in any `FieldSpec` set (architecture test). (CDD-SCHEMA-1)

---

## Implementation Notes

*Derived from ADR-0010 §1, §4, §6:*

- **Location:** `src/PerformativeMail.Sim/Content/ContentLoader.cs` and `ContentLoadResult.cs` (diagnostics, skipped count, `ContentSet?`). The shipped data is `content/` at the repo root, which is `res://content/` because the Godot project root is the repo root.
- **Path → def type mapping** is a fixed ordinal table (CD2):
  - `items/containers.jsonc` → ContainerDef; other `items/*.jsonc` → ItemDef;
  - `mail/kinds.jsonc` → MailKindDef, `mail/mix.jsonc` → MailMixDef, `mail/destinations.jsonc` → DestinationTypeDef;
  - `balance/*.jsonc` → balance;
  - `presentation/**` → skipped.

  Any other `.jsonc` path is CV-GEN-16, an unknown content file. This is an implementation choice; flag it for the GDD re-review.
- **Stage order.** Run stages 1–2 per file. Run stage 3 per def type only if that type's files are clean. Run stages 4–5 through the story 003 runner, the same one `BuiltInContent` uses.
- **Hand-off from story 018 (story 018 l.110; PR #53 Mail Review r1 B1).**
  - **`suppress` reaches stages 4–5.** Story 018's walker parses each def's `suppress: [{rule, why}]` into `ParsedDef.Suppress`, but `BuiltDefs` does not carry it and `ContentValidationInput.BuildRecords` sets every def's suppress to empty. The loader must pass the parsed entries to the story 003 runner so a loaded def can waive a Warning (never hashed, never on the record). See also TD-063(3) (set-level suppress).
  - **File locations for stage 4–5 findings.** Stage 4–5 findings on loaded defs still carry the synthesised `builtin/<folder>/<id>` locations, which breaks GDD `content-data-definitions.md:72` ("Every error carries `path:line:column`") until this story replaces them with each def's real `SourceLocation` (stage 1–3 findings already use `path:line`, TD-176 item 1).
  - **`shares` order (TD-075(3), pending Evan).** MailMix `shares` keep authored order into the canonical bytes (`DefCanonical.cs:139-141`); the Hash-invariance AC's key-order variant is blocked on Evan for `shares` (see the AC).
  - **SelfCheck at loader construction.** `ContentLoader` must construct `SchemaStage`; its constructor is where every `FieldSpec` table's `SelfCheck` runs (TD-090; TD-176 item 10, Mail Dev default, pending Evan).
- **`ContentSet` construction.** Build via the same constructor that `BuiltInContent` uses, so equal inputs give equal canonical bytes.
- **Authoring `content/`.**
  - Author the JSONC files to mirror story 008's values in **authored units**: seconds, metres and ratios as decimals where the key's unit is decimal.
  - Keep `cargo` out (deferred, M0).
  - **Explicit fields (owner ruling 2026-10-21, GDD CD10 note).** Every ItemDef and MailKindDef spells out `carryByHand`, and every ContainerDef spells out `acceptsInsert` and `view`, even when the value equals the default. This applies to the shipped files and to every fixture meant to load clean. `BuiltInContent` sets the same fields explicitly (story 018), so the equality test compares authored values, not defaults. `view` is required (a missing one is CV-GEN-05), so a fixture without it does not load clean.
  - **Time fields (owner ruling 2026-10-21 08:08 PT, ADR-0010 Errata).** `…Seconds` values load as ms and stay ms in the `ContentSet`; the loader adds no tick conversion, so the equality test compares ms values with `BuiltInContent`'s.
  - Comments may cite the owner GDD.
  - Add a `content/presentation/.gitkeep`-style placeholder only if a test needs the directory.
- **Then and later.** `BuiltInContent` stays the M-1a runtime source of truth, per the epic decision. Once this story is Done, the loader is a tested peer. **Switching the boot source to the loader is a `godot-host-shell`/M0 decision, not this story.**
- **`ParseCount`.** Expose a process-level `ContentLoader.ParseCount` for CDD-LOAD-4, implemented without a mutable static: counted through an injected `SimCounters`-like sink, or the result object. Story 008's composition test uses the same count.

---

## Out of Scope

- M0 (`tools/ContentValidator`): the validator CLI and exit codes (TR-content-023); `manifest.txt` and CV-GEN-10 for PCK boot (TR-content-002).
- `godot-host-shell` epic: `ContentHost`, the Failed/Valid screens, and hot reload (TR-content-026, M0).

---

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above.* (QL-STORY-READY skipped — Lean mode.)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- Integration: `tests/integration/content/ContentLoaderTests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 008, Story 010, Story 011, Story 012, Story 013, Story 018
- Unlocks: M0 `tools/ContentValidator`; `godot-host-shell` ContentHost wiring
