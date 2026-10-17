# Content & Data Definitions — Review Log

## Review — 2026-10-16 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: systems-designer, tools-programmer, godot-csharp-specialist, qa-lead, economy-designer; senior: creative-director
Blocking items: 14 | Recommended: 16
Summary: The architecture holds: a pure StatSheet rebuilt from source lists, read and write faces, a staged loader, a value-level hash, and FT-3 enforced in code. The failures are (a) an 84-row duplicated validator catalogue that has drifted from its owner GDDs; (b) mechanisms that cannot be built as specified: ReadOnlySpan records, a hot-reload source path that does not exist in exports, the "all errors" promise, F2 scale and F6 optional-field presence; and (c) acceptance criteria that cannot pass as written. Creative-director scope ruling: the code registry is the single source and owner-numbered rule sets collapse to index rows. The CD reversed its own Rank-4 stamp direction in favour of a personal "Second Opinion" perk with a `DraftRerolls` stat.
Prior verdict resolved: First review
Findings:
- [BLOCKING] Rule Catalogue / CD23: 84-row catalogue duplicates owner rule text and has drifted; collapse PC/Throwing/HUD/Sim Core to index rows; aliases printed `CV-THR-12 (THROW-CV-12)`
- [BLOCKING] CD12 / CV-THR-02 / OQ-CDD-5: `ThrowRangePm` rename was rejected by Throwing (registry throwRangePermille); keep `ThrowRangePermille`
- [BLOCKING] CD4 / CV-THR-16 / CV-THR-18: retired `throwSpreadMrad` and `cooldownSlackTicks` still live
- [BLOCKING] CD10 note / Catalogue: `foundValueMultRatio` vs Mail `foundValueMultPm`; Sim Core HB11 F8 SimSettings constraints missing (CV-SIM)
- [BLOCKING] CD12 / CV-GRD-05: respawn key has three names and two owners; Second Wind duplicated by PC `secondWindMs`
- [BLOCKING] Player Fantasy / FT-1 / CD27: Cannon Arm "50 % farther" vs real ×1.24–1.47 — the card number is the stat, not the outcome
- [BLOCKING] F2: early-stop `intPart > fieldMax/1000` ignores scale (valid int32 2147483647 rejected); `-0.00` unlisted
- [BLOCKING] F6: no presence rule for optional/variant fields, `params` bag, list/string length caps; opt-in "state-affecting" hashing risks silent desync
- [BLOCKING] CD5 / F4: culture-sensitive default string sorting can change DefIndex across OSes; mandate ordinal
- [BLOCKING] CD25: S3 fallback SHA-256 placed in tools assembly, not the shipped game
- [BLOCKING] CD26: exported debug builds have no source tree; in-place swap contradicts CD21 readonly caches
- [BLOCKING] CD6: ReadOnlySpan record properties unworkable; use ImmutableArray
- [BLOCKING] CD3.5 / CD24: line:column only for stage 1; Utf8JsonReader fail-fast; "N rules skipped" undefined
- [BLOCKING] Acceptance Criteria: VAL-1 "exactly one error" impossible; STAT-10, PARSE-3, CLI-1, PERF, ID-4, FT-1 defective; traceability claim false; CD16 `UntilPayday` contradicts a pure sheet
- [RECOMMENDED] CD2 / CV-GEN-10: committed generated manifest is a merge hotspot and breaks the F6 loop; generate at export
- [RECOMMENDED] CD23: "no source generators" is a category error (compile-time); CD8/CD11 hand-written vs generated inconsistent
- [RECOMMENDED] CD9 / CD11: BalanceKey registry needs hard range (Error) + safe range (Warning)
- [RECOMMENDED] CD11/CD12: "design clamps are data" keys unacknowledged by owners
- [RECOMMENDED] F1 / CD13: Set vs base semantics, Add-after-Mul surprise, floorDiv unstated
- [RECOMMENDED] F5 / CV-PRK-08: upper bound only; floors silently dead; CV-PRK-10 ignores maxStacks/excludes
- [RECOMMENDED] CD12: `RaidWarningMs` base in seconds (unit trap); Cannon Arm maxStacks unspecified
- [RECOMMENDED] CD15: `both`-scope stats on team-owned containers — whose personal sheet
- [RECOMMENDED] CD28: single ViewCatalog .tres merge hotspot; DuplicateDeep(All) copies external resources
- [RECOMMENDED] Open Questions OQ-CDD-13: stamp vehicle wrong (lobby-wide, not Stat/RuleFlag, stamp XP bonus) — personal perk + `DraftRerolls`
- [RECOMMENDED] Economy: throw-vs-hand pay parity; ZeroMisdelivery vs ShiftEarnings order; kit value budget
- [RECOMMENDED] Document length 823 > 800 (M target)
Reviewed-Content-Hash: design/gdd/content-data-definitions.md 8a83f521832120c7ce15e5d1c5e4784a3b420755
Reviewed-Content-Hash: design/registry/entities.yaml 19b344454406c6b39d75cd0d2027f6236bdcdb7b

### Revision applied — 2026-10-16 (autonomous, [A] Revise now)
All 14 blockers applied, plus the CD's cheap recommended items; 823 → 790 lines; 64 → 57 ACs.
- Catalogue: the code registry is the single source and `docs/content-rules.md` is generated at M0 with a CI diff. PC, Throwing (alias `THROW-CV-nn`) and HUD are now index rows, and CV-SIM-01…04 (Sim Core F8, HB11) is added. New CV-GEN-20 (safe range, W). CV-GEN-10 trimmed to the PCK manifest. CV-PRK-08 gains a floor check (F5 low), and CV-PRK-10 honours `maxStacks`/`excludes`. CV-GRD-05 now reads `player.respawnMs`. 89 active rules (83 E / 6 W) + 1 retired.
- Drift: `ThrowRangePermille` kept (rename withdrawn); `throwSpreadMrad` and `cooldownSlackTicks` gone; `foundValueMultPm`; the respawn key is `player.respawnMs`; the Second Wind perk def is the only source; `waves.warningMs`.
- FT-1 / CD27: cards show owner outcomes through `{fx:…}`; Cannon Arm reads "30 % farther" from Throwing's range function; "(capped)" is mandatory when a pick is fully clamped.
- F1: `floorDiv` stated, and Build asserts k ≤ 6.
- F2: `scale` variable plus a final range check; all negative-zero forms rejected.
- F5: low bound added.
- F6: presence flags, params pairs, length caps, sorted set lists; every balance key is hashed (no state-affecting flag).
- CD5.5 ordinal/analyzer/InvariantGlobalization; CD6 ImmutableArray; CD8 compile-time generators allowed; CD9 hard + safe range.
- CD25: managed SHA-256 in the Sim (S3 retired).
- CD26: reload-and-restart only, with `--content-dir`/`PM_CONTENT_DIR`; the in-place swap is deleted.
- CD3.5 / CD24: field position side table; fail-fast reader; (rule, def) skip counts; per-clause fixtures with `alsoTrips`.
- CD16: `UntilPayday` deleted.
- CD2: manifest generated at export and not committed.
- ACs rewritten per qa-lead and CD:
  - VAL-1 is per clause, with VAL-2/3/4 folded in.
  - STAT-10 uses a BigInteger reference.
  - PARSE-3 now expects CV-GEN-16 for invalid JSON.
  - CLI-1 is M0 with github format.
  - HOT-2 is restart-from-seed.
  - PERF timing is advisory; allocation checks are deterministic (ID-4).
  - FT-1 is a Unit-level placeholder resolver test.
  - New latch, rejoin-slot and alias criteria.
  - Former STAT-12/13 handed to Manual Delivery and Grid; NET-4 retired.
- OQ-CDD-13 rewritten to the CD ruling: Rank 4 unlocks the personal perk "Second Opinion" with a `DraftRerolls` stat, pending owner ratification. Added OQ-CDD-18 (clamp-key owners), 19 (in-place or networked reload), 20 (CV-PRK-11 strings format) and 21 (economy checks).
- Registry: decimalToScaled, contentHash, throwRangePermille, statCatalogue_v1 (29 stats), contentValidatorCatalogue_v1, defIndexRule and contentFormat updated; respawnSeconds conflict_note added.
- Deferred to Open Questions or ADRs: ViewCatalog split, ContentHost shape, Int64 `Read` fast path, `intentionalCap`, `$schema` globs, the `both`-scope container rule (OQ-CDD-15), Payday order, throw-vs-hand parity, kit value budget.
