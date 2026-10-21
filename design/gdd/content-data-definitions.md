# Content & Data Definitions (incl. StatSheet)

> **Status**: In Review (Provisional) — /design-review 2026-10-16 NEEDS REVISION (14 blockers); revision applied 2026-10-16, re-review decides Approved (log: `design/gdd/reviews/content-data-definitions-review-log.md`)
> **Author**: owner + agents (autonomous /design-system run)
> **Last Updated**: 2026-10-21 (CD10 defaults of `carryByHand`, `acceptsInsert` and `view` amended per the owner's ruling of 2026-10-21 — see the note under the CD10 table)
> **Last Verified**: 2026-10-16
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-10-16 — CONCERNS (6 items) applied: FT-3 scope + Rank-4 reroll direction (ratified by the owner 2026-10-16), design clamps as data, FT-4 iteration loop, placeholder views for greybox defs, trust-model note + handshake-only content check, M0/M4 tooling split
> **Implements Pillar**: 2 (honest rules under the slapstick), 4 (no meta power — FT-3), 5 (every lobby reads the same tables); infrastructure for 1 and 3

> **Provisional**: designed before the M-1a two-friend greybox (systems-index GUARD overridden by owner instruction 2026-10-16); re-validate after the greybox.

## Summary

Content & Data Definitions turns every authored table and balance number into one immutable, integer-only, validated `ContentSet` built at boot and handed to each run's simulation, and it owns the `StatSheet` through which perks, stamps and run events change stats without touching other systems. Content is JSONC parsed exactly (decimal text to ms/mm/permille/cents, never floats), ids become ordinal-sorted `DefIndex` ordinals, a ContentValidator whose code registry is the single source of every load-time rule other GDDs depend on runs at boot and in CI, and a SHA-256 content hash lets a host refuse a joiner whose numbers differ.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP (hard-coded in M-1 → real in M0)` · Key deps: `Simulation Core` · Depended on by: every gameplay system (hard: Grid Inventory, Mail & Destinations, Player Character, Manual Delivery, Throwing & Catching, Perks, Stamps & Run Events, Run Structure, Networking) · Engine feasibility: no `docs/engine-reference/godot/modules/core.md` exists — Core-domain feasibility not checked against 4.7.1 beyond the VERIFIED 4.5 `duplicate_deep` and 4.6 `FileAccess` entries; spikes S1, S2, S4, S5 open.

**Milestone slice.** M-1a/M-1b: hard-coded constants in the throwaway greybox (no loader). M0: loader, ContentValidator CLI in CI (`text|github`, `--strict`, `--print-hash`, `--write-manifest`, `--list-rules --format md`), content hash in the join handshake, the F6 key as reload-and-restart, StatSheet with no v1 perks yet. M1: ~12 perks, stamps scaffold, `balance/*.jsonc` complete. M4: full perk pool (~20–28), three stamps, three events, card placeholders (FT-1).

## Overview

Content & Data Definitions is the layer that turns the game's authored numbers and tables — mail kinds, items, containers, destinations, buildings, recipes, shop items, perks, stamps, enemies, waves, island archetypes, street names, unlock tracks and every tunable balance key — into one **immutable, validated, integer-only `ContentSet`** that the Simulation Core is built from at the start of each run (spec 07 §1.3 "content is data", §6; spec 08 §2, §5). It owns four things and nothing else: the **typed def schemas** (field names, types, units, defaults, closed enums) of every content table, while each gameplay GDD owns the *values* in its own tables; the **loader**, which parses authored data exactly (decimal text to integer milliseconds, millimetres, permille and cents — never through floating point) and assigns stable sorted-id indices; the **ContentValidator**, one rule catalogue run at boot, at run start and as a CI command-line tool, which rejects any data file that breaks a rule another system relies on; and the **`StatSheet`**, the modifier stack through which perks, stamps and run events change stats. Perks and stamps never reach into another system: they push modifiers onto a closed set of stat keys, and the owning system reads the resolved value when it needs it. The system also produces the **content hash** a host and a joining client compare so that two machines never run one session on different numbers.

Players never see this system directly. They feel what it guarantees: a perk does exactly what its card says on every machine in the lobby, a balance change is a data edit rather than a code change (spec 13 §6 M4 gate: "tuning done in data only"), and a broken data file stops the build in CI instead of surfacing as a mid-run bug. Without it, every system would hard-code its own numbers, perks would need code in each system they touch, and the 1–8 player co-op promise (Pillar 5) would rest on machines that merely hope they agree. It is designed as a Foundation layer on top of Simulation Core and below every gameplay system; its internal structure (the loader pipeline, the C# record layout) is described here only at the behaviour level, and becomes an ADR before implementation.

## Player Fantasy

**Framing: indirect.** Players never touch this system; they experience what it guarantees. (Creative-director framing "The Card Is the Contract", with the cross-machine sentence of "Same Mail on Every Doorstep" folded in; "Regulations Are Regulations" was rejected because it describes the Stamps and Run Structure GDDs more than this one.)

### The Card Is the Contract

The anchor moment is the Draft. Someone reads a perk card aloud — *"Cannon Arm: throws go 30 % farther"* — the crew argues, someone takes it, and thirty seconds later the parcel lands exactly where the card promised, on all eight screens at once. Every number on a card is computed from what the simulation uses, on every machine in the lobby — the 30 % comes from Throwing's own range function, not a hand-typed "50 %". The Post Office's regulations are absurd, but they are enforced to the letter, and that deadpan honesty is both the joke and the reason the slapstick works: Pillar 2 needs the rules straight so the chaos is funny (*"keep the rules honest (a bad throw is never softened)"*). When a run goes badly, players should blame each other and laugh — never blame the game, and never wonder whose machine is right.

This serves **Fellowship** (aesthetic #1) by making shared outcomes undisputed, **Pillar 2** (*"open-ended logistics systems … with real optimization space"* — optimisation needs numbers that mean what they say), **Pillar 4** (*"nothing carried between runs makes a run easier by default"* — enforced here, see FT-3) and **Pillar 5** (a 1-player and an 8-player lobby read the same tables).

### Fantasy tests

Each test is pass/fail and is checked by a design decision in this GDD:

- **FT-1 Card honesty.** Every number shown on a perk or stamp card is rendered from the same def and `StatSheet` values the simulation resolves; where the stat is an input to an owner formula (throw range), the card shows the owner's computed outcome (`{fx:…}`, CD27), never the raw stat. No hand-typed number appears in card text. When stacking hits a clamp (e.g. `DeliverInsertPm` ≤ 750, `ThrowRangePermille` 1000–1500), the card or tooltip shows the clamped result, never the naive sum. (Rules CD19, CD27; verified in the Perks & Perk Draft UI.)
- **FT-2 Cross-machine agreement.** A client whose content hash differs from the host's is refused at join with a plain message naming both versions; a content mismatch never surfaces mid-run as a desync. (Network section.)
- **FT-3 No meta power (Pillar 4).** `StatSheet` modifiers come only from in-run sources: perks, stamps and run events. MetaProfile and Postal Rank may unlock *which defs are available*; they can never push a modifier (CV-PRK-09). FT-3 covers the StatSheet only. Meta effects that bypass it (Draft rerolls, kit grants, starting money) are held to the same test by Meta-Progression and Run Structure: a rank-gated option is a sidegrade or an opt-in with a cost, never a default advantage (OQ-CDD-13).
- **FT-4 Broken data never reaches a run.** A rule a gameplay system relies on is never downgraded to a warning or replaced by a silent default; it fails at boot, at run start, or in CI. Warnings exist only for authoring-taste checks (e.g. death-bag timing versus respawn timing). FT-4 governs what reaches a run, not the editing loop: in a debug build the F6 key keeps the last good set on error, so a typo costs one keypress, never a restart.

No **Feel & Comedy Feedback** subsection: this system has nothing on screen, no sound and no timing of its own; comedy is delivered by the systems that read its values (Throwing, Mail & Destinations, HUD), and card wording belongs to Perks & Perk Draft and HUD.

## Detailed Design

### Core Rules

Specialists consulted: systems-designer (primary), gameplay-programmer, engine-programmer, tools-programmer, godot-csharp-specialist, network-programmer. Where they disagreed, the choice and the rejected option are stated in the rule. Godot claims are labelled **VERIFIED** (in `docs/engine-reference/godot/`) or **UNVERIFIED** (needs a spike on 4.7.1).

#### A. What is content, and how it is written

**CD1 — Three data classes, one owner each.**

| Class | Examples | Loaded by | In the content hash | Schema owner |
|---|---|---|---|---|
| **Sim content** | every def table (spec 08 §2), balance keys, `SimSettings` values | `ContentLoader` (Sim assembly) at boot | **Yes** | This GDD (schema); each gameplay GDD owns its tables' *values* |
| **Presentation content** | view catalogue (def id → scene, icon, sound), HUD cue table, `hud.tuning`, localisation, Comms canned lines | Same parser where JSON; Godot-side host | No | The presenting GDD (HUD, Comms, Presentation); validation library shared |
| **Player data** | MetaProfile, settings, input bindings, `user://input.json` | Profile & Settings Persistence / Input & Controls | No | Persistence (MetaProfile), Input & Controls (`settings.input`) |

Player data is **not content**. This answers input-controls OQ-IC-2 as far as this GDD goes: Content & Data does not own the MetaProfile or settings schema; Persistence does, and stored content ids in a profile are plain strings resolved against the current `ContentSet` on load (unknown ids dropped and logged — Persistence's rule). Spec 07 §6 "content is data" applies to Sim content only.

**CD2 — Format and layout.** All Sim content and JSON Presentation content is **JSONC** (`.jsonc`: JSON with `//` comments and trailing commas) under `res://content/` (repo `content/`). YAML is rejected (needs a runtime package, which Sim Core R33 forbids); `.tres` is rejected for Sim content (CD6); CSV is deferred until a table outgrows hand editing (> ~100 rows). Layout: one directory per def type, several array files per directory split by theme to reduce merge conflicts — `content/items/*.jsonc`, `content/items/containers.jsonc`, `content/mail/{kinds,mix,destinations}.jsonc`, `content/perks/{carrier,facility,postal,defense}.jsonc`, `content/{buildings,recipes,shop,enemies,waves,stamps,events,kits,world}/`, `content/unlocks.jsonc`, `content/streets.jsonc`, `content/balance/<owner>.jsonc`, `content/presentation/…`. **`content/manifest.txt`** (every content path, lowercase ASCII, ordinal-sorted) is **generated, never committed**: `ContentValidator --write-manifest` writes it as an export step, and only a PCK boot reads it. A `DirectorySource` (CLI, tests, the F6 key) enumerates and ordinal-sorts the directory itself. File order never affects the hash (F6 is in `DefIndex` order). Everything outside `content/presentation/` is Sim content and hashed. (Enumerating `res://` inside an exported PCK with `DirAccess` is **UNVERIFIED**; the export filter for non-resource files is **UNVERIFIED** — spike S1 decides PCK + manifest versus `EmbeddedResource`.)

**CD3 — Parsing.** The loader walks each file with a hand-written `Utf8JsonReader` pass (`System.Text.Json`, part of the .NET 8 shared framework — a framework dependency, not a package; trimming/AOT behaviour of Godot desktop exports is **UNVERIFIED**, and the hand-written walk uses no reflection so it is safe either way). Reflection `JsonSerializer` binding is rejected: it cannot express "decimal seconds with at most 3 fractional digits", silently ignores unknown keys, and is last-wins on duplicate keys. Parser rules:
1. Comments skipped; trailing commas allowed; a UTF-8 BOM at offset 0 is stripped by the loader before the reader sees the bytes; CR and LF are both whitespace (so `git autocrlf` never matters). `Utf8JsonReader` is fail-fast: a syntax error ends that file, so stage 1 reports at most one syntax error per file (every file is still attempted).
2. Bytes are read, never text (`Godot.FileAccess.GetFileAsBytes`, fully qualified to avoid the `System.IO.FileAccess` clash; `get_as_text` lost `skip_cr` in 4.6 — **VERIFIED**). An empty result is checked against `FileAccess.GetOpenError()` so a missing file (CV-GEN-10) is never reported as an empty one (CV-GEN-16).
3. A **duplicate key** in one object is an error (CV-GEN-04). An **unknown field** is an error with a "did you mean" suggestion at edit distance ≤ 2 (CV-GEN-03). Only `$schema` and `suppress` are reserved keys.
4. Numbers are read from the raw token bytes (`ValueSpan`), **never** via `GetDouble`. Exponents (`e`, `E`) and `-0` are rejected; integer-typed fields reject a `.`; decimal fields accept at most 3 fractional digits (F2). Ids must not contain escape sequences.
5. Every error carries `path:line:column`, the rule id, the offending value and the allowed range or set. Stage 1 computes the position from the failing token; the schema walk records each def's and field's start offset in a side table, so stage 2–5 errors name the field. Line counts LF; column is the 1-based UTF-8 byte offset in the line (a tab counts 1).

**CD4 — Units: a field's declared unit decides how it is written and stored.** Each def type has a code-side `FieldSpec` table (name, unit, required/default, range, enum set or reference target) that drives parsing, unknown-field detection, range checks, reference resolution and the optional generated JSON Schema for editor autocomplete. New fields follow the suffix convention below; legacy spec 08 names (`baseValue`, `price`, `hp`, `grid`) keep their names and declare their unit in the `FieldSpec`.

| Unit / suffix | Authored as | Stored as (Sim) | Example |
|---|---|---|---|
| `…Seconds` | decimal, ≤ 3 fractional digits | `int` ms, then ticks via Sim Core F1 where the field is a duration the Sim waits on | `batchIntervalSeconds: 15` → 15 000 ms → 450 ticks |
| `…Metres` | decimal, ≤ 3 fractional digits | `int` mm | `catchRadiusMetres: 1.5` → 1 500 |
| `…Ratio` / perk `"value"` on a `Mul` | decimal, ≤ 3 fractional digits | `int` permille | `lateValueRatio: 0.5` → 500; `"value": 1.5` → 1 500 |
| `…Points` | decimal, ≤ 3 fractional digits | `int` milli-points | `complaintOnMisdeliveryPoints: 5` → 5 000 |
| `…Deg` | decimal, ≤ 3 fractional digits | `int` milli-degrees (converted to BAM by the consuming system) | `loftDeg: 6` → 6 000 |
| `…Ms`, `…Mm`, `…Pm` (or `…Permille`), `…Ticks` | integer | same | `catchTapMs: 500` |
| money (`baseValue`, `price`, `…Cents`) | integer | `long` cents (`Cents`) | `price: 400` |
| counts, grid dims, HP | integer | `int` / `ushort` | `grid: [8, 2]` |

A numeric field with no declared unit is a load error (CV-GEN-06). Conversions are exact text-to-integer (F2); no `float`, `double` or `decimal` value is ever stored in a def the Sim reads (Sim Core R27). Where a GDD already authors in integer units (Player Character's `rangeMm`, Throwing's `catchTapMs`) those names stand; nothing has to be renamed to decimals. A balance key named `…Seconds` is a decimal field stored in ms — never an integer read at scale 1.

**CD5 — Ids, ordinals and strings.**
1. Every def id matches `^[a-z][a-z0-9_]{0,47}$` (CV-GEN-02), is unique within its def type (CV-GEN-01), and is the only form in which content, saves, profiles, replays and logs refer to a def.
2. At build, each def type's ids are sorted by **ordinal comparison** (`StringComparer.Ordinal`; ids are validated ASCII, so this equals UTF-8 byte order) and numbered from 0: that number is the def's **`DefIndex`** (a `ushort` struct whose default is None), used by every system at runtime and on the wire (Sim Core R28.10; F4). `0xFFFF` is the single "none" sentinel in every table; a def type may hold at most 65 534 defs (CV-GEN-09). A `DefIndex` is meaningful only together with the content hash that produced it, so it is never persisted.
3. **`StringId` contract** (closes the contract Sim Core promises): a content `StringId` is a `(namespace, DefIndex)` pair, where the namespace is the def type — adding a mail kind therefore never renumbers items. Run-time strings (generated street names, addresses, player names) are interned per run by the Sim into a separate run table (spec 08 §4); they never share a numbering space with def ids.
4. Systems look defs up by `DefIndex` (`content.MailKinds[kind]`); string lookups exist only at load, in the validator and in tools. Per-tick code performs no string compares.
5. **No culture-sensitive compare anywhere.** Every sort or compare of ids, keys and paths is ordinal; the Sim and tools projects ban culture-default compares, `ToLower`/`ToUpper` and culture `StartsWith`/`EndsWith` by analyzer and build with `InvariantGlobalization` (ICU and NLS order `_` against digits differently, which would change `DefIndex` and the hash across OSes).

**CD6 — Defs are immutable plain C#; no Godot Resource holds Sim content.** Defs are `sealed record class` types with init-only `int`/`long`/`ushort`/`bool`/enum fields, living in `PerformativeMail.Sim/Content`; small leaf values (a modifier) may be `readonly record struct`. Lists are `ImmutableArray<T>` (hot paths use `AsSpan()`); no def holds a `T[]` (a record with an array field is only shallowly immutable). A reflection test asserts no def type — including nested types and generic arguments — has a `T[]`, `float`, `double`, `decimal` or `Half` member. A Godot `[GlobalClass]` Resource cannot live in the Godot-free Sim assembly, would need a mirror type (two sources of truth), cannot be read by the CI validator without the engine, produces noisy `.tres` diffs, and is shared by path so one accidental write leaks into every user. Consequently nothing on the content path calls `Resource.Duplicate` or `DuplicateDeep` (the 4.5 `duplicate(true)` behaviour change — **VERIFIED** — is irrelevant to defs). Synthesised record equality and `with` are not relied on; defs are compared by `DefIndex` and hashed through the canonical encoder.

**CD7 — Required and defaulted fields.** Every field is either **required** or **defaulted**, and a default is always the *neutral* value (omit = any / 0 / false / none / 1000 permille). A gameplay-relevant field never gets a silent non-neutral default (FT-4). Two cross-GDD conflicts are resolved by this rule:
- **`weightClass` is required** on every ItemDef and MailKindDef (Throwing rule 8 wins over grid-inventory OQ9's "default light"; a silent default would let a forgotten field make an item throwable at light speed). Grid's assumption survives as authored data: every v1 non-mail item is authored with an explicit class, `light` unless the item's owning GDD says otherwise. Answers grid-inventory OQ9.
- **`allowedCategories` omitted = any; present but empty = error** (spec 08 "omit = any" and Grid's "empty is an error" are both kept; CV-GRD-03).

**CD8 — Closed enums are hosted here; their members are owned by the domain GDD.** Every closed set the content refers to is a C# enum with **explicit numeric values that are never reused**, parsed from its snake_case data spelling through a lookup table generated at compile time from the enum declaration (source generators are allowed — they run at build time; runtime reflection discovery is not), never `Enum.Parse`, which accepts `"5"` and case variants. Adding a member is a code change made by the owning GDD's system.

| Enum | Members owned by | v1 members (data spelling) |
|---|---|---|
| `Stat` | this GDD (keys), requested by the reading system | CD12 |
| `RuleFlag` | the system that implements the behaviour | CD18 |
| `ItemCategory` | Grid Inventory | tool, material, consumable, ammo, blueprint, weapon (+ `mail` for containers) |
| `WeightClass` | Grid Inventory | light, medium, heavy, bulk |
| `ContainerLayout`, `BeltAccess`, `ContainerView` | Grid Inventory | grid, slots, pile · none, any_side, loading_face, outfeed_only, inserter_only · grid, manifest, auto |
| `Verb` | Player Character & Movement | the P10 catalogue (deliver, open, pick_up, mount, catch, push, operate, configure, shop, announce, dismount, harvest, rebuild, deconstruct) |
| `BuildingBehaviour` | Building System | spec 08 §2.6 list minus deferred ones |
| `EnemyTrait`, `TargetClass` | Enemy AI & Waves | spec 08 §2.10 |
| `ShopItemKind`, `PerkCategory`, `PerkScope`, `Rarity`, `PerkPool` | Economy / Perks & Perk Draft | spec 08 §2.8–2.9; `PerkPool` base, sorting (spec 08 §3.8 `perkPools`) |
| `RunEventTrigger`, `StampParam` | Stamps & Run Events | CD10 |

**CD9 — Balance keys: one namespace, one owner per key.** Spec 08 §2.14's flat `balance.json` becomes `content/balance/<owner>.jsonc`, one file per owning GDD (`core`, `sim`, `player`, `inventory`, `mail`, `throw`, `run`, `economy`, `waves`, …). A key is addressed `owner.key` (`player.rangeMm`, `throw.g`). A code-side **`BalanceKey` registry** declares each key's owner GDD, unit and **hard range** (correctness: width, sign, proofs — CV-GEN-13, Error). The **safe range** (taste) is a `safe: [min, max]` annotation beside the value in the data file; leaving it is CV-GEN-20 (Warning), so exploring past a safe range never needs a code change. Every balance key is Sim content and hashed; there is no "state-affecting" flag to forget. The same key in two files, a key absent from the registry, and a value outside the registry hard range are all errors. Hand-backs: Player Character's `player.yaml` becomes `content/balance/player.jsonc`; Throwing's "BalanceConfig / ThrowDef" becomes `content/balance/throw.jsonc`; HUD's `hud.tuning` becomes `content/presentation/hud.tuning.jsonc` (Presentation class — not hashed). `SimSettings` values live in `content/balance/sim.jsonc`; **Simulation Core owns those keys** (its Tuning Knobs), this GDD only loads and validates them.

**CD10 — Def schema changes accepted from dependent GDDs.** Spec 08 §2 remains the base schema; these deltas are adopted (each owning GDD keeps its values; spec 08 needs the same corrections — hand-back in Open Questions).

| Def | Field | Type / values | Default | Requested by | Note |
|---|---|---|---|---|---|
| ItemDef | `weightClass` | WeightClass | **required** | Throwing rule 8, Grid OQ9 | CD7 |
| ItemDef | `carryByHand` | bool | false *(amended 2026-10-21; was true)* | Grid R5 | was MailKindDef-only in spec 08; the MailKindDef field defaults to false too (see the note below the table) |
| ItemDef | `equipSlot` | none \| back | none | Grid R1 (backpack) | |
| ItemDef | `tool` + `weapon` blocks | both may be present | — | Player Character P18 (axe is tool and melee weapon) | |
| ItemDef | `standardIssue` | **not adopted** | — | Player Character OQ8 | a "standard-issue copy, sell value 0" is a property of one *granted entry*, not of the item type: modelled as `KitDef.standardIssueItem` + an entry flag owned by Grid Inventory (hand-back) |
| KitDef (**new**) | `id`, `grants[{item,count}]`, `standardIssueItem?`, `rankRequired` | — | — | Player Character P18, spec 01 kits | Land kit: axe, backpack |
| ContainerDef | `layout` | grid \| slots \| pile | **required** | Grid R1 | |
| ContainerDef | `grid` | [cols, rows] | required for grid/slots, forbidden for pile | Grid R1 | |
| ContainerDef | `handCarried` | bool | false | Grid R5 | |
| ContainerDef | `acceptsInsert` | bool | false *(amended 2026-10-21; was true)* | Grid R1 | `false` = no player, inserter or belt op may add an entry; only the owning system's internal API can (death bag, spill heap). Hand-back to Grid to confirm |
| ContainerDef | `beltAccess` | + `inserter_only` | none | Grid R1 (chest) | |
| ContainerDef | `reservedSlots` | int list (slots layout only) | empty | Grid R1/R4 (hotbar slot 0) | |
| ContainerDef | `view` | grid \| manifest \| auto | *not `auto` (amended 2026-10-21); replacement open — see the note below the table* | Grid D7 | runtime D7 rule decides `auto` |
| MailKindDef | `fallbackKind` | MailKind id? | none | Mail OQ18 | cycle-free (CV-MAIL-04) |
| MailKindDef | `complaintOnMisdeliveryPoints` | Points | required | Mail R1/F9 | authored 3/5/8/12/16, stored 3 000…16 000 (one stored unit) |
| MailKindDef | `beltLengthMetres` | Metres | required | spec 08 `beltLength` renamed | |
| MailMixDef | `sharesPm` per shift | int permille | required | Mail F1 | sum exactly 1 000 (CV-MAIL-02 supersedes spec "1.0 ± 0.001") |
| MailMixDef | `openingRampBatches`, `tailGuardSeconds` | int, Seconds | 0, required | Mail OQ18 | |
| MailMixDef | ratios (`streetStreakRatio`, `raidSpawnRateRatio`, `distanceStepRatio`, `shiftStepRatio`) | Ratio | required | Mail tuning | spec names `raidSpawnRateMult`, `distanceMultiplierPerDistrict`, `shiftMultiplierPerShift` renamed |
| DestinationTypeDef | `accepts` | MailKind id list | required | Mail R3 | must mirror `MailKindDef.acceptedBy` (CV-MAIL-01) |
| DestinationTypeDef | `catchVolume` | {`widthMetres`,`heightMetres`,`depthMetres`,`centreHeightMetres`,`yawDeg`} | required | Mail R3, Throwing T-rules | |
| DestinationTypeDef | `manualInsertHoldSeconds` | Seconds | required | spec `manualInsertHold` renamed | `0` = no Deliver anchor (Manual Delivery MD1) |
| EnemyDef | `radiusMetres`, `heightMetres` | Metres | required | Throwing rule 7 | |
| PerkDef | `pool` | PerkPool | base | spec 08 §3.8 `perkPools` | |
| PerkDef | `modifiers[].qualifiers` | id list | none (= every qualifier) | CD11 | loader expands to one modifier per qualifier |
| PerkDef | `prerequisites.ownsBlueprint`, `prerequisites.vehicleExists` | ShopItem id, vehicle id | none | spec 10 ("Prereq: bp_sorting", "bike exists") | |
| PerkDef / StampDef | `description` | localisation key with `{stat:…}` / `{param:…}` placeholders | required | FT-1, CD27 | |
| StampDef | `scoreMultRatio` | Ratio | 1.0 | spec 08 `scoreMult` renamed | consumed by Run Structure, not a stat |
| StampDef | `params` | object, keys from closed `StampParam` | none | Mail (Cursed Mail `cursedShareRatio` 0.10) | no free-form bags |
| RunEventDef (**new**) | `id`, `trigger` (RunEventTrigger), `fromShift`, `modifiers`, `rules`, `params`, `lifetime` | — | — | spec 10 §6, Stamps & Run Events | spec 08 has no event def |

**CD10 defaults amended 2026-10-21 (owner ruling, Evan, 07:42 PT).** The `true` defaults of `carryByHand` (ItemDef and MailKindDef) and `acceptsInsert` (ContainerDef), and the `auto` default of `view` (ContainerDef), broke CD7's neutral-default rule. ADR-0010 §3 (Accepted) says "defaults are always neutral", and it stays as written. **Ruling: off by default.** `carryByHand` and `acceptsInsert` default to `false`. Shipped content files spell out all three fields on every def that has them, so no def depends on a default. `ContainerView` (grid, manifest, auto) has no off member, so what replaces `view`'s `auto` default is open: make `view` required, count `auto` as neutral ("the runtime decides"), or name another value (content-data story 018, OQ-018-2). Shipped content spells it out either way. This supersedes content-data story 005's reading [A] of 2026-10-17 (true on a permissive flag treated as the "any" value; decision log 2026-10-17). Content values are unchanged: a def that should be hand-carried or accept inserts now says so.

Mail OQ18's scalar knobs (`foundValueMultPm`, `teamComplaintCap`, `closeGraceTicks`, door-complaint keys) go in `content/balance/mail.jsonc`, not in a def; `rollCallNames` is a session setting, not content (Run Structure / Networking).

#### B. The StatSheet

**CD11 — Stat identity.** A stat is a member of the closed C# enum **`Stat`** (PascalCase with a unit suffix: `DeliverInsertPm`), written in data as its mechanical snake_case form (`deliver_insert_pm`) — one mapping, generated, never hand-maintained. A stat's value is always the **effective value** the consumer uses (e.g. `MailMaxStack`, not a "stack multiplier" the consumer must apply). Each stat has a code-side **`StatDef`** row: `unit`, `scope` (team | personal | both), `qualifierKind` (none | mailKind | container | building | item), `baseSource` (a constant, a balance key or a def field), allowed ops, `setResolve` (min-wins | max-wins), `rounding` (HalfUp | Floor | Ceil), `min`, `max`. A stat with a qualifier is parameterised by a content id — `MailValueMultPm[letter]` — which keeps the enum closed (adding a stat is a code change) while letting a perk target any existing kind or building in data (adding such a perk is not). A modifier with no qualifier on a qualified stat applies to every qualifier. Rejected: one enum member per qualifier (a code change for every new mail kind). **Design clamps are data.** A `StatDef` `min`/`max` that exists for a design reason is a balance key in the reading system's `content/balance/<owner>.jsonc`. Only bounds that protect correctness stay in code: storage width, F1's Int128 proof, the `0xFFFF` sentinel. A `BalanceKey` registry range is a correctness bound, not a taste bound. Retuning a number never requires a code change (spec 13 §6 M4).

**CD12 — v1 stat catalogue.** Old names used by other GDDs are kept as aliases here only; each owning GDD adopts the new name (hand-backs).

| Stat (data key) | Aliases in other GDDs | Unit | Scope | Qualifier | Base | Ops | Clamp / rounding | v1 sources |
|---|---|---|---|---|---|---|---|---|
| `DeliverInsertPm` | `insertPm`, `deliver_insert_permille` | pm | personal | — | 0 | Add | 0…750, HalfUp | Quick Hands +500 |
| `MoveSpeedAddPm` | `move_speed_add_permille` | pm | personal | — | 0 | Add | 0…`player.buffCapPm` (400) | Long Legs +120/stack; Postal Sprint +300 as a **transient term** (CD18) |
| `ExtraJumps` | `extra_jumps` | count | personal | — | 0 | Add | 0…`player.maxExtraJumps` (1) | Bunny Hop +1 |
| `RespawnMs` | `respawnMs`; `respawnSeconds` (retired) | ms | personal | — | `player.respawnMs` (10 000) | Set (min-wins) | 1 000…base | Second Wind Set 3 000 (the perk def is the only source; PC's `secondWindMs` retires) |
| `InventoryRows` | `inventory_rows` | count | both | container | `ContainerDef.grid` rows | Add, Mul | 1…`inventory.maxRows`, **Ceil** | Big Pockets +1/stack (player_inventory); Bike Courier +1 (bike_cargo); Bulk Depot ×1.5 (chest, depot) |
| `MailMaxStack` | `stack_mult` | count | team | mailKind | `MailKindDef.maxStack` | Mul | 1…255, Floor | Bulk Mailer ×2 (postcard, letter) |
| `WeightPenaltyPm` | `weight_penalty_mult`, `strongBackPermille` (= 1000 − this) | pm | personal | — | 1000 | Mul | 0…1000 | Strong Back ×0.5 |
| `MailValueMultPm` | `MailValueMult.<kind>` | pm | team | mailKind | 1000 | Mul | 0…5000 | Stamp Collector ×1.25 (postcard, letter) |
| `ThrownCorrectPayMultPm` | `ThrownCorrectPayMult` | pm | personal | — | 1000 | Mul | 0…5000 | Cannon Arm ×1.1 (`maxStacks` 1) |
| `LateValueRatioPm` | `LateValueRatio` | pm | team | — | `mail.lateValueRatio` (500) | Set (max-wins) | 0…1000 | Grace Period Set 1000 (+ RuleFlag `SuppressLateComplaint`) |
| `MisdeliveryPenaltyMultPm` | `MisdeliveryPenaltyMult` | pm | team | — | 1000 | Mul | 0…2000 | Fragile Handling ×0.5 |
| `MisdeliveryComplaintMultPm` | `MisdeliveryComplaintMult` | pm | team | — | 1000 | Mul | 0…2000 | Fragile Handling ×0.5 |
| `ThrowRangePermille` | FT-1's `ThrowRangePm` (withdrawn) | pm | personal | — | 1000 | Add | 1000…1500 | Cannon Arm +500 (card via `{fx:throw.rangeRatio}`, CD27) |
| `BikeSpeedMultPm` | — | pm | personal | — | 1000 | Mul | 500…2000 | Bike Courier ×1.4 |
| `BeltSpeedMultPm` | spec `BeltSpeed` | pm | team | building | 1000 | Mul | 500…3000 | Express Lane ×1.5 |
| `SorterFilterSlots` | spec `SorterFilterSlots` | count | team | building | `BuildingDef.params.filterSlotsPerOutput` | Add, Set (max-wins) | 1…`automation.maxFilterSlots` (64) | Extra Labels +2; Postmaster's Eye Set 64 |
| `SorterThroughputMultPm` | — | pm | team | building | 1000 | Mul | 500…4000 | Postmaster's Eye ×2 |
| `InserterRateMultPm` | — | pm | team | building | 1000 | Mul | 500…4000 | Greased Inserters ×2 |
| `ConstructHpMultPm` | — | pm | team | building | 1000 | Mul | 500…3000 | Reinforced ×1.4 |
| `TurretDamageMultPm` | — | pm | team | building | 1000 | Mul | 500…3000 | Hot Barrels ×1.5 |
| `MeleeDamageMultPm` | — | pm | team | item | 1000 | Mul | 500…3000 | Union Muscle ×1.5 |
| `MeleeKnockbackMultPm` | — | pm | team | item | 1000 | Mul | 500…4000 | Union Muscle ×2 (mail_bat) |
| `RaidWarningMs` | — | ms | team | — | `waves.warningMs` (15 000) | Add | 0…`waves.maxWarningMs` (45 000) | Early Warning +20 000 |
| `QuotaMultPm` | — | pm | team | — | 1000 | Mul | 300…2000 | Union Rep ×0.9; Trainee stamp ×0.7 |
| `RaidBudgetMultPm` | — | pm | team | — | 1000 | Mul | 300…3000 | Trainee stamp ×0.7 |
| `SpawnBudgetMultPm` | — | pm | team | — | 1000 | Mul | 500…2000 | Zoning Board ×1.15 |
| `ShiftEarningsMultPm` | Mail M-R7-2 "Inspector −10 %" | pm | team | — | 1000 | Mul | 500…1500 | Postal Inspector event ×0.9 (lifetime Shift) |
| `ZeroMisdeliveryBonusPm` | Mail M-R7-2 "zero-misdelivery +10 %, Watch +25 %" | pm | team | — | `run.zeroMisdeliveryBonusPm` (100) | Add | 0…1000 | Neighbourhood Watch +250 (lifetime Shift); Run Structure applies it only on a zero-misdelivery shift |
| `DraftRerolls` | — | count | personal | — | `perks.rerollsPerPlayer` (1) | Add | 0…`perks.maxRerollsPerPlayer` (3) | Second Opinion +1 (Rank-4 unlock, OQ-CDD-13; provisional — Perks) |

**Which clamp bounds are data.** Every design bound in the Clamp column is a balance key owned by the reading system (CD11) — **PROPOSED** until each owner lists the key in its Tuning Knobs (hand-back OQ-CDD-18): `delivery.insertPmMax` 750 (Manual Delivery), `player.buffCapPm` 400, `player.maxExtraJumps` 1, `player.respawnMinMs` 1 000, `inventory.maxRows`, `throw.rangeMinPermille` / `throw.rangeMaxPermille` 1 000 / 1 500 (Throwing; CV-THR-02), `mail.payMultMaxPm` 5 000 and `mail.penaltyMultMaxPm` 2 000 (Mail), `automation.maxFilterSlots` 64, `waves.maxWarningMs` 45 000, and the remaining `…MultPm` bounds as `<owner>.<stat>MinPm/MaxPm` in the owning GDD's file. Correctness bounds stay in code: `MailMaxStack` ≤ 255 (entry `count` is `uint8`), `WeightPenaltyPm` and `LateValueRatioPm` 0…1 000 (a ratio above 1.0 has no meaning), every `min` ≥ 0 (F1/Cents assume non-negative permille).

Readers own the meaning: Grid Inventory reads `InventoryRows`/`MailMaxStack`/`WeightPenaltyPm`; Player Character reads the movement, jump and respawn stats; Manual Delivery reads `DeliverInsertPm`; Mail reads the pay and complaint stats; Throwing reads `ThrowRangePermille` and `ThrownCorrectPayMultPm`; Run Structure reads quota, budget and Payday stats; Building, Belt Logistics, Defenses, Player Combat, Enemy AI & Waves and Vehicles read theirs when their GDDs are written (those rows are **provisional** until then). A stamp's `scoreMultRatio` is a StampDef field read by Run Structure, not a stat.

**CD13 — Modifiers.** A modifier is `{sourceKind (Perk | Stamp | RunEvent), sourceId (DefIndex), stackIndex, ownerSlot (PlayerSlot, or Team), stat, qualifier (DefIndex or 0xFFFF), op (Add | Mul | Set), value (int, in the stat's unit; Mul in permille), lifetime}`. Ops: **Add** sums in the stat's unit; **Mul** multiplies in permille; **Set** replaces the base-derived value, resolved min-wins or max-wins per `StatDef`. Resolution order is fixed and every op is commutative within its class, so **push order never changes a result** (F1). Two consequences designers must expect: **Adds are not multiplied** (depot rows base 10, ×1.5 and +1 → 16, not 17), and a **Set replaces base and discards every Add and Mul**; min-wins/max-wins chooses between Sets only, so a Set may sit on either side of base (`RespawnMs` is guarded by CV-PLR-05; OQ-CDD-14 asks for a general warning). A personal-scope stat rejects a team-scoped source and vice versa unless the `StatDef` scope is `both` (CV-PRK-03).

**CD14 — Only in-run sources push; everyone else reads (FT-3).** The sheet exposes two faces: `IStatSheet` (read-only, injected into any system that reads a stat) and `IStatWriter` (push/remove), injected **only** into Perks & Perk Draft, and Stamps & Run Events. Meta-Progression, Persistence, Economy, kits and every gameplay system hold the read face at most. `unlocks.jsonc` entries may reference defs (making them *available*) but may never carry a modifier or a rule flag (CV-PRK-09). This is the rule that keeps Pillar 4: Postal Rank unlocks content, it never pushes a stat.

**CD15 — Sheets are derived, rebuilt from authoritative source lists.** There is one **team sheet** and one **personal sheet per PlayerSlot**. A read of a personal or `both` stat merges the team modifiers and that player's personal modifiers in one pass; a team-only stat ignores the slot. The sheets are a **cache**, not authoritative state: the authority is the list of sources — team perk picks, each player's personal picks (keyed by `profileId` for the whole run), active stamps (RunSettings), and active run events — all already replicated (spec 08 §3.1–3.3; `RunState.activeEvents` is a hand-back). `StatSheet.Build(content, sources)` is a **pure Sim function** callable without a `SimWorld` (clients and Presentation use it too); the sheet is rebuilt immediately whenever a source is added or removed, which only happens at a phase boundary or inside the op/phase that applies a pick, so all reads within one tick see one sheet version. Rejected: replicating individual modifier events (more messages, and a missed event diverges silently).

**CD16 — Lifetimes.** `Run` (perks, stamps; until the run ends) and `Shift` (cleared at the next Payday settlement after it was pushed — Postal Inspector, Neighbourhood Watch). There is no read-consumed lifetime: a `Read` never mutates the sheet. Expiry is applied by the pushing system at the phase boundary through `IStatWriter.Remove(source)`; the sheet has no timers.

**CD17 — Stacks.** Picking a perk N times pushes N copies of its modifier list (`stackIndex` 0…N−1). `maxStacks` ≥ 1; team perks have `maxStacks` 1; a personal perk's `maxStacks` cannot exceed the personal perk cap (`perks.personalPerkCap` 5, spec 11 §11; CV-PRK-06). The cap itself is enforced by Perks & Perk Draft at draft time.

**CD18 — Behaviour switches and timed effects are not modifiers.** A perk effect that changes *behaviour* rather than a number is a **`RuleFlag`** (closed enum; on while at least one live source holds it; no stack count); the owning system checks it. v1 flags: `KeepHotbarOnDeath` (Second Wind), `PresortOutfeed` (Presort), `InsuredRebuild` (Insured), `SuppressLateComplaint` (Grace Period), `PriorityMail` (Priority Post), `FreeForecast` (Forecast Desk), `SharedBuild` (Shared Build), `LocalKnowledgeArrow` (Local Knowledge), `BeltMomentum` (Bunny Hop), `EarlyDistrictUnlock` (Zoning Board), `EnemiesMarked` (Early Warning), `PostalSprint` (Postal Sprint), `CursedMail` (Cursed Mail stamp), `RushHour` (Rush Hour stamp). A **timed effect** (Postal Sprint: +300 for 4 s after each correct delivery) depends on sim state and is owned by the system that tracks the trigger: the owner passes its live term into the read — `Read(stat, qualifier, slot, transientAdd)` — and the term enters *before* the clamp, so `buffCapPm` still bounds Long Legs plus Sprint. Rejected: expiry ticks inside the sheet (the sheet would mutate every tick and stop being a pure function of its sources). This matches the systems-index rule that behaviour-hook perks are implemented by their owning system.

**CD19 — Clamp transparency (FT-1).** `Read` returns the resolved value; `Explain(stat, qualifier, slot)` returns `{resolved, unclamped, clampedAt (none | min | max), contributing sources}` for UI and debug. The Perks & Perk Draft card and tooltip render the *resolved* value (and may show "(capped)" when `clampedAt` ≠ none), never the naive sum.

**CD20 — When a stat is read.** General rule: **continuous systems read every tick** (movement, belts, sorters — through the cached sheet, one array load); **discrete operations read once when they start and latch the value** for their duration. The exceptions owners already chose stand: Grid Inventory evaluates `MailMaxStack`/`InventoryRows` at op time; Input & Controls / Manual Delivery latch the deliver hold at press; Throwing snapshots `ThrowRangePermille` and `ThrownCorrectPayMultPm` into the flight record at the throw tick; Mail reads `MailValueMultPm` at the settle tick; Run Structure reads Payday stats at settlement. A source removed mid-run never evicts content: a container whose `InventoryRows` shrinks keeps its entries until they drain (Grid Inventory owns that rule — hand-back; v1 perks only grow).

#### C. Loading, validation, hashing

**CD21 — Load pipeline and lifetime.** At **boot**, behind the loading screen, the Godot-side `ContentHost` reads `manifest.txt` and each listed file as bytes and hands them to the Sim's `ContentLoader` through an `IContentSource` (the CLI and tests supply a `DirectorySource` / `InMemorySource` — one code path everywhere). The loader runs five stages: **(1) syntax** (parse, duplicate keys, positions) → **(2) schema** (unknown fields, types, enums, id lint, units, ranges) → **(3) build** (sort ids, assign `DefIndex`, convert units, F1 ticks) — only if 1–2 are clean → **(4) references and cross-def rules** → **(5) whole-set rules** (shares sum, worst-case stat bounds). Then it emits the canonical encoding and the host computes the content hash (formula F6). The result is one immutable `ContentSet` (with `SimSettings` and the balance table as members) held for the **process lifetime**. At **run start** the same `ContentSet` instance is passed by reference into `new SimWorld(settings, content, runSeed)`; there is no re-parse. Systems receive it by constructor injection (`ContentSet`, `IStatSheet`, and — for Perks and Stamps only — `IStatWriter` join Sim Core R4's injectable list: hand-back) and may cache sub-tables in `readonly` fields.

**CD22 — Severity policy.** **Error**: the `ContentSet` is not built, the game shows an error screen listing every error, networking and hosting are disabled, and the CLI exits 1. **Warning**: logged, the run proceeds; CI runs `--strict`, which fails on any warning not waived by a `"suppress": [{"rule": "…", "why": "…"}]` entry on the def (`why` mandatory). Errors can never be suppressed: a `suppress` naming an Error rule or an unknown rule id is itself an error, and a `suppress` that matches nothing is counted in the summary only. A rule that a gameplay system relies on for correctness is always an Error (FT-4). "Clamp" is not a validator severity — clamping player settings belongs to the Settings / Input loaders.

**CD23 — One rule registry.** Every rule has a permanent id **`CV-<AREA>-NN`** (never renumbered or reused; retired rules stay listed as retired), an owner GDD, the owner's own code as an alias, a severity, a stage and its required inputs. The failure message prints both: `CV-THR-12 (THROW-CV-12)`, `CV-PLR-07 (PC rule 7)`. The **code registry is the single source** (`Sim/Content/Rules/<Owner>.cs`, an explicit list — no runtime reflection discovery); `--list-rules --format md` generates `docs/content-rules.md` and CI fails if the checked-in copy differs. The owning GDD's rule text is the contract; this GDD holds the mechanism, its own rules and the rows of owners without numbered rule tables (catalogue below). A new rule is a row in its owner's rule table, a registry entry and a violation fixture per clause (CD24).

**CD24 — The ContentValidator CLI.** `tools/ContentValidator` is a plain `dotnet` console project referencing the Sim assembly (no Godot). It runs the same pipeline as boot over a `DirectorySource`, reports **all** errors it can reach (a rule whose declared inputs failed an earlier stage is skipped; "N skipped" counts (rule, def) pairs and names the blocking field), sorts output by (path ordinal, line, column, rule id) so it is deterministic, and supports `--strict`, `--format text|github|json`, `--list-rules --format md`, `--write-manifest`, `--print-hash` and `--emit-schema`. Paths print `/`-separated relative to the content root. Exit codes: 0 clean, 1 errors (or warnings under `--strict`), 2 usage/IO, 3 internal crash (a top-level catch maps every unhandled exception to 3). Every rule **clause** has a **violation fixture** (a minimal valid tree plus an id-addressed overlay; it must yield exactly one diagnostic with that id at the rule's severity, and any other id it trips is listed in the fixture's reviewed `alsoTrips`) and, where it has a limit, a **valid-edge fixture** at the limit; a meta-test iterates the registry and fails if any clause lacks a fixture or its fixture does not trip it (satisfies Throwing TC-CV-1 through the alias and Player Character PCM-F8-3). The shipped `content/` must pass `--strict`. BalanceSim and SeedViewer load content through the same loader (`--content <dir>`). **Milestone split:** M0 needs `--format text|github`, `--strict`, `--print-hash`, `--write-manifest` and `--list-rules --format md`; `--emit-schema` lands at M1 (perk authoring) and `--format json` at M4.

**CD25 — Content hash.** The content hash is **SHA-256 over the canonical encoding** of the built `ContentSet` (formula F6): a schema-version byte, every `SimSettings` and balance value, then every def table in `DefIndex` order with integer fields in fixed-width little-endian and ids as length-prefixed UTF-8. It is computed over *loaded values*, not file bytes, so comments, whitespace, line endings and key order never change it, while any value change does. The canonical encoder and a small **managed SHA-256** (~60 lines, NIST known-answer vectors in its tests) both live in the Sim, so the game, a headless host and every tool compute the hash with no platform crypto (.NET on Linux would otherwise need the system OpenSSL). The trust model needs accident detection only (Network 1). Presentation content and player data are not hashed; the `Stat`/`RuleFlag` enums and validator code are covered by the game version string.

**CD26 — Debug hot reload (the F6 key).** Debug builds only (`OS.IsDebugBuild()`). The source directory is `--content-dir <path>` or the `PM_CONTENT_DIR` environment variable; in an editor run (`OS.HasFeature("editor")`) it defaults to `res://content`, which is the disk copy. An exported debug build with neither has no source tree, so the F6 key is disabled there. Pressing the F6 key re-reads that directory through `DirectorySource`, runs the full pipeline, and on any error **keeps the old `ContentSet`** and shows the diagnostics overlay — never a half-applied set. On success the new set replaces the process set and the current run **restarts from its seed** (reload-and-restart; the run is marked **tainted** — not recorded, not replayable). There is no in-place swap into a running `SimWorld`: systems cache sub-tables in `readonly` fields (CD21), so a swap could not reach them (in-place swap and a host-broadcast reload: OQ-CDD-19). Hot reload is refused on a client whenever connected and on a host while any remote peer is connected ("Disconnect peers first"); force-disconnect is rejected as too surprising.

**CD27 — Card numbers come from data (FT-1).** A PerkDef or StampDef `description` is a localisation key whose text may show numbers only through placeholders (`{stat:move_speed_add_pm}`, `{param:cursed_share_ratio}`) resolved from the def's own modifiers or params at display time — the placeholder shows the modifier's value, and the card adds the resolved total with "(capped)" from `Explain` when the picker's sheet would clamp it (CD19; mandatory when the pick's whole contribution is clamped away). Where the stat feeds an owner formula, the text uses `{fx:<owner>.<fn>}`: a pure, integer function the owning system registers (inputs: the def's values and the picker's resolved sheet; output: the player-facing figure), so the card states the outcome — Cannon Arm shows Throwing's flat-aim range ratio via `{fx:throw.rangeRatio}` (hand-back OQ-CDD-5); a literal digit in the English text of a perk or stamp description is an error (CV-PRK-11). Rendering is Perks & Perk Draft's UI; the rule lives here because the guarantee is this system's.

**CD28 — Presentation lookups stay out of the Sim.** The Godot side keeps a typed **`ViewCatalog`** resource (`.tres`, `[Export] PackedScene`/`Texture2D`/audio entries keyed by def id string) so the exporter's dependency tracking sees the referenced scenes (whether plain path strings are missed by export dependency scans is **UNVERIFIED**). At boot it is resolved into arrays indexed by `DefIndex`. A def with no view entry gets the generic placeholder view and a warning in every build (greybox content needs no art). A release-export check makes it an error. A view entry with no def is a warning. Neither affects the content hash. Presentation never mutates shared resources from the catalogue; a per-entity copy uses `Duplicate(true)`, which since 4.5 copies internal sub-resources only (**VERIFIED**) — never `DEEP_DUPLICATE_ALL`, which would also copy shared textures and scenes. Splitting the catalogue per def type (`Array<ViewEntry>`, one file per type, avoiding a single merge hotspot) is an ADR question.

### States and Transitions

**ContentHost (process lifetime, Godot side).**

| State | Entered when | Behaviour | Exits to |
|---|---|---|---|
| `Unloaded` | process start | nothing readable | `Loading` (boot) |
| `Loading` | boot begins | stages 1–5, encode, hash; budget in Tuning Knobs | `Valid` (no errors) · `Failed` (≥ 1 error) |
| `Valid` | a `ContentSet` is built | hash shown in main menu/lobby (8 hex chars); runs may start; host/join enabled | `Reloading` (F6 key, debug) |
| `Failed` | ≥ 1 error at boot | error screen lists every error; no run, no host, no join; only Quit and Retry (re-runs `Loading`) | `Loading` (Retry) |
| `Reloading` | F6 key in a debug build with a source directory (CD26) | peers check first; builds a candidate set beside the current one | `Valid` (new set, run restarts from its seed, tainted) · `Valid` (old set kept, overlay shows errors or "no changes") |

**StatSheet (per run, per peer).** `Empty` (run start, no sources) → `Current(version n)` on every source add/remove (version n+1, rebuilt immediately) → discarded at run end. There is no "dirty" state visible to readers: a rebuild completes inside the op or phase that changed the source.

### Interactions with Other Systems

| System | Direction | Data | Interface owner |
|---|---|---|---|
| Simulation Core (#1) | upstream | Provides F1 `contentTicks`, Cents/permille primitives, `RngStream` (`perks`, `shop`, `events`), the composition root and lifecycle (`ContentSet` injected before `Freeze()`). Receives `ContentSet` (with `SimSettings` + balance), `StatSheet` faces, and the `StringId` contract (CD5.3) | Sim Core owns `SimSettings` keys and R4; this GDD owns defs, loader, validator, StatSheet |
| Grid Inventory | downstream | ItemDef, MailKindDef, ContainerDef (CD10 deltas); reads `InventoryRows`, `MailMaxStack`, `WeightPenaltyPm`; RuleFlag `KeepHotbarOnDeath`, `PresortOutfeed` | Grid owns values and the rules CV-GRD-*; this GDD the schema and loader |
| Mail & Destinations | downstream | MailKindDef, MailMixDef, DestinationTypeDef, `balance/mail`; reads pay and complaint stats, `LateValueRatioPm`; RuleFlags `SuppressLateComplaint`, `CursedMail`, `PriorityMail` | Mail owns values (R3: "Content & Data owns loading and validation") |
| Player Character & Movement | downstream | `balance/player`, Verb enum, KitDef; reads `MoveSpeedAddPm` (+ transient Postal Sprint), `ExtraJumps`, `RespawnMs` | PC owns values and CV-PLR-* |
| Manual Delivery | downstream | `manualInsertHoldSeconds`; reads `DeliverInsertPm` (clamp ≤ 750 is the `StatDef` max — closes MD's hand-back) | MD owns its formula |
| Throwing & Catching | downstream | `balance/throw`, catch volumes, EnemyDef radius/height; reads `ThrowRangePermille`, `ThrownCorrectPayMultPm` | Throwing owns values and CV-THR-* |
| Input & Controls | downstream (soft) | `manualInsertHoldSeconds` as hold base; its action map and settings are its own data (CD1) | Input |
| HUD & Notifications, Comms | downstream (Presentation) | cue table, `hud.tuning`, canned lines are Presentation content validated by the shared library (CV-HUD-*); HUD reads stats only via the client sheet for previews | HUD / Comms |
| Perks & Perk Draft | downstream | PerkDef; holds `IStatWriter`; renders cards via CD19/CD27 | Perks owns picks, draft, the personal cap |
| Stamps & Run Events | downstream | StampDef, RunEventDef; holds `IStatWriter` | Stamps & Run Events |
| Run Structure | downstream | `balance/run`; reads `QuotaMultPm`, `RaidBudgetMultPm`, `SpawnBudgetMultPm`, `ShiftEarningsMultPm`, `ZeroMisdeliveryBonusPm`, StampDef `scoreMultRatio`; passes the unlocked set to Perks/Stamps/Economy | Run Structure |
| Networking & Session | downstream | content hash, `Hello`/`HelloAck` reason codes, source lists in JoinState (Network section) | Networking owns transport and handshake mechanics |
| Economy, Building, Belt Logistics, Vehicles, Player Combat, Enemy AI & Waves, Defenses, World Generation, Resource Harvesting | downstream (provisional — GDDs not written) | their def tables (spec 08) and stat rows in CD12 | each owner |
| Profile & Settings Persistence, Meta-Progression | downstream | resolve stored content ids by string; `unlocks.jsonc`, `KitDef.rankRequired`, `PerkDef.pool`; read-only, never push modifiers (CD14) | Persistence / Meta |
| Dev Tooling | downstream | ContentValidator CLI, BalanceSim, SeedViewer, the F6-key hot reload, debug `Explain` | Dev Tooling |
| Presentation & World Dressing | downstream | `ViewCatalog` keyed by def id (CD28) | Presentation |

### ContentValidator Rule Catalogue (consolidated)

Severity: **E** = error, **W** = warning (CD22). Stage per CD21. "Owner" is the GDD whose contract the rule protects; its own code is the alias. The code registry is the single source and `docs/content-rules.md` its generated listing (CD23). Full rows are kept for this GDD's rules, for owners whose GDD is not written yet, and for Mail and Grid until they number their rules (OQ-CDD-2/3); owners with numbered rule tables have one **index row** and their text is authoritative.

| Id | Owner (alias) | Rule | Sev | Stage |
|---|---|---|---|---|
| CV-GEN-01 | C&D (spec 08 §5) | Ids unique per def type (checked at the start of stage 3, before F4 runs); every reference resolves to a def of the declared type (stage 4; a wrong-type reference counts as unresolved) | E | 3–4 |
| CV-GEN-02 | C&D | Id matches `^[a-z][a-z0-9_]{0,47}$`; no escapes | E | 2 |
| CV-GEN-03 | C&D | Unknown field (suggestion at edit distance ≤ 2) | E | 2 |
| CV-GEN-04 | C&D | Duplicate key in one object | E | 1 |
| CV-GEN-05 | C&D | Required field missing | E | 2 |
| CV-GEN-06 | C&D / Sim Core F1 | Numeric format: integer fields reject `.`; decimal fields ≤ 3 fractional digits; no exponent, no `-0`; no numeric field without a declared unit | E | 2 |
| CV-GEN-07 | Sim Core (F1 cap) | Any duration 0…86 400 000 ms | E | 2 |
| CV-GEN-08 | C&D | Enum string not in its closed set | E | 2 |
| CV-GEN-09 | C&D | ≤ 65 534 defs per type | E | 3 |
| CV-GEN-10 | C&D | Generated `manifest.txt` well-formed (lowercase ASCII, ordinal-sorted, no duplicates, no `..`) and every listed file present (PCK boot) | E | 1 |
| CV-GEN-11 | C&D | Def never referenced by shop, recipe, pool, kit, mix or wave (tag `debug` exempt) | W | 5 |
| CV-GEN-12 | C&D | Integer value exceeds its field's storage width | E | 2 |
| CV-GEN-13 | C&D | Balance key: duplicated across files, absent from the `BalanceKey` registry, or outside its registry range | E | 2 |
| CV-GEN-14 | C&D | Field forbidden for this variant present (e.g. `grid` on a `pile` container) | E | 2 |
| CV-GEN-15 | C&D (F1) | A `StatDef` whose `min` > `max` after its balance-key bounds resolve | E | 3 |
| CV-GEN-16 | C&D | Syntax / shape: invalid UTF-8, a BOM other than at offset 0, an empty or comment-only file (`[]` is the valid empty file), nesting deeper than 32, a def file whose top level is not an array (balance: not an object) | E | 1 |
| CV-GEN-17 | C&D (F1) | A stat's base (per qualifier) lies outside its own `[min, max]` — the value would be silently clamped | E | 5 |
| CV-GEN-18 | C&D | A table the Sim indexes unconditionally is empty (MailKindDef, MailMixDef, DestinationTypeDef, ContainerDef, ArchetypeDef, streets) | E | 5 |
| CV-GEN-19 | C&D (spec 13 §2.2) | A def tagged `deferred` (cargo, hog_rider, balloon, tank, pipes, boats, oil…) is reachable: share > 0, wave weight > 0, shop, recipe, kit or unlock entry (qualifier and prerequisite references may name it) | E | 4 |
| CV-GEN-20 | C&D | Balance value outside its data-file `safe` range (CD9) | W | 2 |
| CV-PRK-01 | C&D (spec 08 §5) | Every `modifiers[].stat` is a `Stat`; every `rules[]` is a `RuleFlag` (PerkDef, StampDef, RunEventDef) | E | 2 |
| CV-PRK-02 | C&D | Op allowed by the stat's `StatDef` | E | 4 |
| CV-PRK-03 | C&D | Source scope allowed by the stat's scope (team source on personal-only stat or reverse) | E | 4 |
| CV-PRK-04 | C&D | Qualifier kind matches `StatDef.qualifierKind`; qualifier ids resolve; `qualifiers` present-but-empty or listing an id twice | E | 4 |
| CV-PRK-05 | C&D | Single modifier value within the op's range: Mul 0…10 000 pm; Add is a delta, \|A\| ≤ `max` − `min`; Set within `[min, max]` | E | 4 |
| CV-PRK-06 | Perks (spec 11 §11) | `maxStacks` ≥ 1; team perks `maxStacks` = 1; personal `maxStacks` ≤ `perks.personalPerkCap` | E | 4 |
| CV-PRK-07 | Perks | Prerequisite ids resolve; `excludes` symmetric; no prerequisite cycle | E | 4 |
| CV-PRK-08 | C&D | Worst case of all sources for a (stat, qualifier) passes its `max` (F5 high) or its `min` (F5 low) — dead content, e.g. Long Legs ×3 + Postal Sprint vs `buffCapPm` | W | 5 |
| CV-PRK-09 | C&D (FT-3) | `unlocks.jsonc` or KitDef carries a modifier or rule flag | E | 2 |
| CV-PRK-10 | C&D (F1 bound) | More than `stats.maxMulFactors` (6) Mul modifiers can be live on one (stat, qualifier) at worst case (honouring `maxStacks` and `excludes`) | E | 5 |
| CV-PRK-11 | C&D (FT-1) | Literal digit in a perk/stamp description's English text; a placeholder naming a stat/param the def does not carry, or a stat the def modifies twice with different values (ambiguous) | E | 4 |
| CV-STP-01 | Stamps | `scoreMultRatio` 0.5…2.0; `params` keys in `StampParam`, values in range (`cursedShareRatio` 0.05…0.20) | E | 2 |
| CV-MAIL-01 | Mail (spec 08 §5; M-R1-1c) | `acceptedBy` ⊆ destination type ids, and `MailKindDef.acceptedBy` mirrors `DestinationTypeDef.accepts` | E | 4 |
| CV-MAIL-02 | Mail (M-R1-1c) | `sharesPm` sum exactly 1 000 per shift; each 0…600 | E | 5 |
| CV-MAIL-03 | Mail (M-R1-1c) | `deadLetterShifts` ≥ 1 | E | 2 |
| CV-MAIL-04 | Mail (M-R1-1c) | Every kind with share > 0 has an accepting destination type or a `fallbackKind`; fallback chains resolve without cycles | E | 5 |
| CV-MAIL-05 | Mail (Dependencies) | `batchJitterSeconds` < `batchIntervalSeconds` / 2 | E | 5 |
| CV-MAIL-06 | Mail (Dependencies) | `tailGuardSeconds` ≤ `batchIntervalSeconds` − `batchJitterSeconds` | E | 5 |
| CV-WAV-01 | Enemy AI & Waves (spec 08 §5) | `weightsByShift` references only enemies with `unlockShift` ≤ shift | E | 4 |
| CV-ARC-01 | World Generation (spec 08 §5) | `districtHouseCounts` sum within the town-size population band | E | 5 |
| CV-RCP-01 | Building / Economy (spec 08 §5) | Every `RecipeDef.blueprint` references a ShopItemDef of kind blueprint | E | 4 |
| CV-RCP-02 | Building (spec 08 §2.7, §5) | `produces` is a building, never an item; `craftSeconds` = 0 | E | 4 |
| CV-BLD-01 | Building (spec 08 §5) | No `powerDraw`, `powerSupply`, or behaviour generator \| pole \| cable | E | 2 |
| CV-GRD-01 | Grid (R3) | `maxStack` × worst-case `MailMaxStack` Mul for that kind ≤ 255 (F5) | E | 5 |
| CV-GRD-02 | Grid (R5/D1) | A `handCarried` container could admit a `bulk` item | E | 5 |
| CV-GRD-03 | Grid (Dependencies) | `allowedCategories` present but empty | E | 2 |
| CV-GRD-04 | Grid (edge cases) | Grid/slots container with W < 1 or H < 1; any `maxStack` < 1 | E | 2 |
| CV-GRD-05 | Grid (D6) + PC (D6) | `deathBagDespawnSeconds` × 1 000 ≤ `player.respawnMs` (window 0; message names both keys) | W | 5 |
| CV-GRD-06 | Grid (R12) | `capEvictMinAgeSeconds` ≥ `worldItemDespawnSeconds` (nothing evictable) | W | 5 |
| CV-GRD-07 | Grid (R12) | `worldItemDespawnSeconds` < 30 (thrown mail vanishes before it can be caught) | W | 2 |
| CV-PLR-01…13 | Player Character (PC validator rules 1–12; 13 = PCM-P10-1) | Index row — rule text, clauses and limits in PC's rule table (rule 6 = Throwing rule 14) | E | 2/5 per rule |
| CV-THR-01…18 | Throwing (alias `THROW-CV-nn`, validator rules 1–18) | Index row — rule text in Throwing's rule table (current: 16 `throwCooldownTicks`, `throwBurstTicks` ≥ 1; 18 `catchRadiusMm + catchHistoryMaxOffsetMm` ≤ 3 700); CV-THR-14 retired into CV-PLR-06 | E | 2/4/5 per rule |
| CV-HUD-01…05 | HUD (H2, H-ROUTE-1, H-ROUTE-6, H-FT-2b) | Index row — cue-table rules: 01 cue ids resolve + loc key; 02 `spokenKey` for ¢ × − ✓ → ·; 03 class fields + paired world cue; 04 args ids/integers, no `surface`; 05 banned words / "!" lint. Presentation scope, not hashed | E | 2/4 |
| CV-SIM-01…04 | Simulation Core (F8, SC-SET-1; HB11) | Index row — `SimSettings` capacities: power of two; reserve ≤ capacity / 4; drain cap ≥ 32; cap × 4 + 570 ≤ capacity − reserve; checked at the 8-player worst case | E | 5 |

Totals (v1 registry): **89 active rules** — 83 errors, 6 warnings (CV-GEN-11, CV-GEN-20, CV-PRK-08, CV-GRD-05, CV-GRD-06, CV-GRD-07) — plus 1 retired (CV-THR-14); the generated `docs/content-rules.md` is authoritative for counts. Rules not adopted as content rules, with where they live: Input & Controls R10 settings validation and the remap-conflict check (Input's own loader); Input's CI lint on raw `Input.*` reads (Input); Mail's per-lot mailbox spacing (M-R3-1b) runs on World Generation output, not on content files (World Generation's world validator); Mail's "zero unlocked houses → content error" is a runtime guard in `PrepareShift` (Mail).

## Formulas

Specialist: systems-designer. All formulas are integer-only (no `float`, `double` or `decimal`; Sim Core R27). Sim Core F1 `contentTicks` is used, never redefined. "Formula F6" (ContentHash) is unrelated to the F6 hot-reload key.

**Rule-to-formula map:** CV-GEN-06, CV-GEN-07, CV-GEN-12 → F2 · CV-THR-12 → F3 · CV-GEN-01, CV-GEN-09, CD5 → F4 · CV-PRK-05, CV-GEN-15, CV-GEN-17 → F1 ranges · CV-PRK-08, CV-PRK-10, CV-GRD-01, CV-THR-02 → F5 · CD25, `--print-hash`, the join check → F6 · runtime `Read`/`Explain` (CD19) → F1.

### F1 StatResolve

The StatResolve formula is defined as:

`StatResolve = clamp( X, min_s, max_s )`, where `X = S` if any Set is live, otherwise `X = R_r(N, D)`; `N = base × P + 1000^k × (Σ A_i + transientAdd)`; `D = 1000^k`; `P = Π M_i`

`transientAdd` (CD18) is an Add, so it enters ΣA — before the Set and before the clamp: Add is commutative so order never matters, a Set means "exactly this value" and overrides every Add, and `buffCapPm` must bound Long Legs plus Postal Sprint, which only works if the term enters before the clamp. This adopts spec 08 §2.9's order (`base × Π(mul) + Σ(add)`); spec 07 §6's "base × Σmultipliers" is withdrawn (ratified by the owner 2026-10-16; spec text update is a hand-back). `R_r` is the single rounding step: **HalfUp** `sign(N) × ((2|N| + D) / (2D))` (symmetric on magnitude, matching Sim Core F7) · **Floor** `floorDiv(N, D)` · **Ceil** `−floorDiv(−N, D)` (for N ≥ 0: `(N + D − 1) / D`). `floorDiv` is true floor division (Int128 `/` truncates toward zero, so it is written explicitly: Floor(−1, 1000) = −1, Ceil(−1, 1000) = 0). An empty product is P = 1, k = 0. The one division happens in Int128 and the clamp is applied in Int128 before narrowing to `int`; `StatSheet.Build` asserts k ≤ `stats.maxMulFactors` (the proof's premise, otherwise enforced only by CV-PRK-10).

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| base | int32 | −2^31 … 2^31−1 | data file (def field or balance key per `StatDef.baseSource`) | Stat value before any modifier |
| M_i | int32 | 0 … 10 000 pm | data file (Mul `value`) | One Mul modifier in permille (CV-PRK-05) |
| k | int | 0 … 6 | calculated (live Mul count); limit constant `stats.maxMulFactors` | Mul factor count (CV-PRK-10) |
| A_i | int32 | −2^31 … 2^31−1 | data file (Add `value`) | One Add modifier, in the stat's unit |
| transientAdd | int32 | 0 … 2^31−1 | calculated (owning system, CD18) | Live timed term; 0 if none |
| S_j | int32 | stat's unit | data file (Set `value`) | Set values; S = min S_j (min-wins) or max S_j (max-wins) per `StatDef.setResolve` |
| r | enum | HalfUp, Floor, Ceil | constant (`StatDef.rounding`) | Rounding mode |
| min_s, max_s | int32 | min_s ≤ max_s (CV-GEN-15) | constant or balance key (`StatDef`) | Clamp bounds |
| N, D | Int128 | see bound below | calculated | Numerator, denominator |

**Output Range:** always within [min_s, max_s]; never negative for v1 stats (all have min ≥ 0). A Set bypasses Add and Mul but not the clamp (`RespawnMs` Set 500 would resolve to 1 000). **Int128 bound** (k ≤ 6, M ≤ 10^4, |base| ≤ 2^31, ≤ 65 535 live modifiers): |P| ≤ 10^24; |base × P| ≤ 2.15×10^33; |ΣA| < 2^47 so |1000^k × ΣA| ≤ 1.41×10^32; |N| ≤ 2.29×10^33 and the largest intermediate `2|N| + D` < 4.6×10^33 — below Int128.MaxValue (1.70×10^38) by a factor > 3.7×10^4. `Explain` reports the unclamped X saturated to int64.

**Example:**
- `InventoryRows`, depot, Bulk Depot (Ceil): base 10, M = [1500], k = 1 → N = 15 000, D = 1 000 → (15 000 + 999) / 1 000 = **15**, matching Grid's (10×3+1)/2. Chest base 4 → N = 6 000 → **6**; base 5 → 7 500 → **8** = (5×3+1)/2.
- `MoveSpeedAddPm`: Long Legs ×3 = 360, transient Postal Sprint 300 → ΣA = 660, k = 0, D = 1 → HalfUp 660 → clamp 0…400 → **400** (`Explain`: unclamped 660, clampedAt max). Without Sprint: **360**.
- `LateValueRatioPm`: base 500, Grace Period Set 1 000 (max-wins) → X = 1 000 → **1 000**; no Set → **500**.
- `DeliverInsertPm`: Quick Hands A = 500 → **500** (clamp 0…750).
- `MailMaxStack` letter (Floor): base 20, M = [2000] → N = 40 000, D = 1 000 → **40**.
- `QuotaMultPm`: base 1 000, M = [900, 700], k = 2 → P = 630 000, N = 630 000 000, D = 10^6 → HalfUp (1 260 000 000 + 1 000 000) / 2 000 000 = **630**.
- Sign: N = −1 500, D = 1 000 → −((3 000 + 1 000) / 2 000) = **−2**.

### F2 DecimalToScaled

The DecimalToScaled formula is defined as:

`DecimalToScaled = sign × (intPart × scale + fracDigits × 10^(3 − fracLen))`, with `scale` = 1000 for decimal fields and 1 for integer fields (no fraction allowed)

The token must match `-?(0|[1-9][0-9]*)(\.[0-9]{1,3})?`, read from raw bytes (CD3.4). It is rejected (CV-GEN-06) for more than 3 fractional digits (trailing zeros count: `0.1000` fails), an exponent, a bare or trailing `.`, a leading `+`, or a negative zero (`-0`, `-0.0`, `-0.00`, `-0.000`). Integer-typed fields use scale 1 and reject any `.`. No division; accumulate in Int64, stop as soon as `intPart > fieldMax / scale`, then range-check the assembled value against `[fieldMin, fieldMax]` (CV-GEN-12; the message says "outside the field's range", which also covers −2^31).

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| intPart | int64 | 0 … fieldMax / scale | calculated (token digits) | Digits before the point |
| scale | int | 1, 1000 | constant (`FieldSpec` unit) | 1000 for decimal units, 1 for integer units |
| fracDigits, fracLen | int, int | 0…999, 0…3 | calculated (token digits) | Fraction digits as an integer, and their count |
| sign | int | −1, +1 | calculated | From a leading `-` |
| fieldMax | int64 | 2^31−1 for int fields | constant (`FieldSpec` storage width) | Overflow limit |
| result | int32 / int64 | −fieldMax … fieldMax | calculated | Value at the field's scale (ms, mm, pm, milli-points, milli-degrees, or the integer itself) |

**Output Range:** −fieldMax … fieldMax; beyond is CV-GEN-12. Durations are further limited to 0 … 86 400 000 ms (CV-GEN-07); each field's own range check runs after.

**Example:** `"0.4"` → 0×1000 + 4×10² = **400** · `"1.5"` → **1 500** · `"15"` → **15 000** · `"0.05"` → 5×10 = **50** · `"-2.25"` → −(2 000 + 250) = **−2 250** · `"0.1234"` → CV-GEN-06 · `"2147483.648"` → 2 147 483 648 > 2^31−1 → CV-GEN-12 · integer field `2147483647` → passes (scale 1) · `"1e3"`, `"-0"`, `"-0.00"` → CV-GEN-06.

### F3 ExactTickCheck

The ExactTickCheck formula is defined as:

`ExactTickCheck = ((ms × 30) mod 1000 = 0)`

Equivalent to `ms mod 100 = 0` (gcd(30, 1000) = 10): at 30 Hz only multiples of 100 ms are whole ticks. When it holds, `ms × 30 / 1000` equals Sim Core F1 `contentTicks(ms) = (ms × 30 + 999) / 1000`. F1 itself rounds up for fields that need not be whole ticks; F3 is only for fields that must be (CV-THR-12). Throwing rule 12's "multiples of 100/3 ms" therefore means multiples of 100 ms in integer milliseconds (hand-back wording note).

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| ms | int32 | 0 … 86 400 000 | data file (`catchTapMs`, `catchPredictMs`) | Duration in milliseconds |
| 30 | int | 30 | constant (`TickRateHz`, Sim Core R1) | Ticks per second |
| ms × 30 | int64 | 0 … 2 592 000 000 | calculated | Exceeds int32 above 71 582 788 ms, so computed in `long` |

**Output Range:** bool; when true, the tick count is 0 … 2 592 000.

**Example:** 500 ms → 15 000 mod 1 000 = 0 → valid, 15 ticks · 100 ms → 3 000 → valid, 3 ticks · 501 ms → 15 030 → 30 → invalid · 50 ms → 1 500 → 500 → invalid · cross-check Sim Core F1: 400 ms → 12 ticks; 150 ms → (4 500 + 999)/1 000 = 5 ticks (rounded up; F3 would reject 150 for a whole-tick field).

### F4 DefIndex

The DefIndex formula is defined as:

`DefIndex(id) = |{ j ∈ ids_T : bytes(j) <_ordinal bytes(id) }|`

Comparison is bytewise lexicographic over UTF-8, a proper prefix sorting first; ids are unique per type (CV-GEN-01). Implemented as a sort with `StringComparer.Ordinal` (CD5.5), never a culture-default sort.

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| ids_T | set of string | 0 … 65 534 ids | data file | All ids of def type T, each matching `^[a-z][a-z0-9_]{0,47}$` |
| id | string | ∈ ids_T | data file | The id being indexed |
| DefIndex | ushort | 0 … n−1 | calculated | The def's index; `0xFFFF` = none |

**Output Range:** 0 … n−1, n ≤ 65 534 (CV-GEN-09), so a real index never equals the sentinel.

**Example:** mail kinds → cargo 0, large 1, letter 2, medium 3, postcard 4, small 5 (`large` < `letter` since 'a' < 'e'). Ordinal edge: `bike` 0, `bike2` 1, `bike_cargo` 2 ('2' = 0x32 < '_' = 0x5F; the prefix `bike` first).

### F5 WorstCaseStat

The WorstCaseStat formula is defined as:

`WorstCase(s, q) = max( R_r(N⁺, 1000^k⁺), max_{j ∈ U_S} S_j )`, where `N⁺ = base_q × Π_{U_M} M_i + 1000^k⁺ × (Σ_{U_A} A_i + T_s)`

A validator bound per (stat s, qualifier q): one F1 evaluation without the clamp over the *raising* units of every source that could be live at once. A modifier with no qualifier applies to every q. Each perk contributes `maxStacks` copies; personal units are limited to `perks.personalPerkCap` per player (a per-player total). `U_M` = Mul units with M > 1000, `U_A` = Add units with A > 0 (each the top `min(cap, count)` for personal sources, chosen separately — a sound upper bound, loose only for mixed personal Mul+Add); `U_S` = every Set value (counted only when it raises); `k⁺ = |U_M|`. For CV-PRK-10, `k_worst` = all Mul units that can be live together (team plus `min(cap, personal)`, each perk at most `maxStacks`, at most one member of an `excludes` group); the rule fails if `k_worst > 6`, and the value check is then skipped. Single-source mode (U = one modifier) is used by CV-THR-02. **F5 low** (CV-PRK-08 floor) is the mirror over the *lowering* units (M < 1000, A < 0, every Set that lowers): `WorstCaseLow < min_s` warns.

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| base_q | int32 | per stat | data file (e.g. `MailKindDef.maxStack`) | Base for qualifier q |
| M_i, A_i, S_j | int32 | as F1 | data file | Raising units, expanded by `maxStacks` |
| T_s | int32 | 0 … 2^31−1 | constant (balance key, e.g. `player.postalPm` for `MoveSpeedAddPm`; else 0) | Largest transient term the stat's owner can pass |
| personalPerkCap | int | 1 … 10 | data file (`perks.personalPerkCap`, 5) | Personal units per player |
| max_s | int32 | per `StatDef` | constant | Clamp maximum |
| WorstCase | int64 | ≥ base_q | calculated | Unclamped worst case |

**Output Range:** ≥ base_q for non-negative bases; a violation is `WorstCase > max_s` — an Error for CV-GRD-01, CV-PRK-10, CV-THR-02 and a Warning for CV-PRK-08. Computed in Int128 like F1.

**Example:** CV-GRD-01 postcard: maxStack 40 × Bulk Mailer 2 000 → N⁺ = 80 000 → Floor **80** ≤ 255 OK; letter 20 → **40** OK; a hypothetical maxStack 200 → **400** > 255 → Error. CV-PRK-08 `MoveSpeedAddPm`: U_A = {120 × 3 = 360}, T_s = 300 → **660** > 400 → Warning — this fires on shipped v1 content **by design** (Player Character F1 intends `buffCapPm` to bound Long Legs + Postal Sprint), so `long_legs` carries `suppress: [{rule: CV-PRK-08, why: "buffCapPm intentionally caps Long Legs + Postal Sprint (PC F1)"}]`. CV-THR-02: base 1 000 + Cannon Arm 500 = **1 500** OK; a single +501 → 1 501 → Error (its fixture also trips CV-PRK-05, |A| > max − min = 500 — listed in `alsoTrips`). F5 low: `QuotaMultPm` Union Rep 900 × Trainee 700 × a hypothetical 450 → 283.5 → 284 < 300 → Warning.

### F6 ContentHash

The ContentHash formula is defined as:

`ContentHash = SHA-256( canonical )`, where `canonical = hashSchemaVersion[u8] ‖ BalanceBlock ‖ DefBlocks`

- **BalanceBlock:** `u16 keyCount`, then every `SimSettings` / balance key (all Sim-class keys — no opt-in flag) in ordinal key order as `u16 keyLen, UTF-8 key, i64 LE value`.
- **DefBlocks:** each def type in the fixed `DefType` table order (types are appended, never reordered): `u16 count`, then each def in DefIndex order (F4): `u8 idLen, UTF-8 id`, then its fields in `FieldSpec` order as fixed-width little-endian values — bool u8; enum u16 (explicit numeric value, CD8); DefIndex reference u16 (`0xFFFF` none); int/long/ushort at declared width; list = `u16 count` + items (set-valued lists — `acceptedBy`, `accepts`, `qualifiers`, `allowedCategories` — sorted by value first); nested object = its fields in order; string field (localisation key) = `u16 len` + UTF-8; an **optional or variant field** (no default — `prerequisites`, `catchVolume`, `grid` on a `pile`) = `u8` presence flag (0 absent, 1 present + value); a keyed bag (`StampDef.params`) = `u16 count` + (`u16` enum value, `i64` value) pairs in enum order. Defaulted fields are written with their loaded value. Lists and strings above 65 535 entries or bytes are CV-GEN-12.

Computed over loaded values, not file bytes. The join check compares all 32 bytes; the display form is the first 4 bytes as 8 lowercase hex characters.

**Variables:**

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| hashSchemaVersion | u8 | 1 … 255 | constant | Bumped whenever the encoding changes |
| balance entries | (string, i64) | ≤ 65 535 keys | data file (`content/balance/*.jsonc`, every key) | Sorted by key |
| defs per type | list | ≤ 65 534 | data file | In DefIndex order |
| canonical | byte[] | variable (≈ tens of KB for v1) | calculated | The encoded stream |
| ContentHash | byte[32] | 32 bytes | calculated | SHA-256 digest |

**Output Range:** exactly 32 bytes; display 8 hex characters.

**Example (illustrative layout, not a real digest — the digest of the shipped set is golden-tested):** a MailKindDef `letter` at DefIndex 2 of 6 with weightClass 1, maxStack 20, baseValue 400, fallbackKind none, acceptedBy [1, 3]; the type block opens `06 00` (count 6) and the `letter` entry is `06 6C 65 74 74 65 72` | `01 00` | `14 00` | `90 01 00 00 00 00 00 00` | `FF FF` | `02 00 01 00 03 00` — 27 bytes: id length + id (7), weightClass (2), maxStack (2), baseValue (8), fallbackKind (2), list (6).

### Determinism and rounding

Every formula is integer-only with at most one division (F1 and F5 one each; F2 and F6 none; F3 and F4 no rounding). No result depends on modifier push order, file read order or key order: Add and Mul are commutative, and F4 and F6 sort first. HalfUp is symmetric on magnitude (Sim Core F7); Floor and Ceil are true floor and ceiling; no intermediate narrows below Int128.

## Edge Cases

Specialist: systems-designer. Its five contradiction findings were applied to Detailed Design and the catalogue (CV-PRK-05 Add-as-delta, CV-GEN-01 stage, CV-GEN-10 boot/CI split, CD27 placeholder wording, new CV-GEN-16…19).

**Loading & parsing**

- **If a def type has zero defs (directory absent or file is `[]`)**: valid; the table is empty and F6 writes `00 00`. Tables the Sim indexes unconditionally (MailKindDef, MailMixDef, DestinationTypeDef, ContainerDef, ArchetypeDef, streets) need at least one def, else CV-GEN-18. CV-MAIL-02 passes vacuously over an empty mix (CV-GEN-18 already fails it).
- **If a file starts with a UTF-8 BOM**: stripped. A BOM anywhere else, or invalid UTF-8, is CV-GEN-16 at the byte position.
- **If a file uses CRLF, LF or mixed line endings**: all are whitespace; line numbers count LF only, so CRLF and LF checkouts report identical `path:line:column` and the same hash.
- **If a file is 0 bytes or comment-only**: CV-GEN-16 "no JSON value" — `[]` is the valid empty file; a silent empty read would hide a truncated checkout.
- **If a file is listed in the generated manifest but missing from the PCK**: CV-GEN-10 at boot, naming the manifest line; every other file is still parsed. Unsorted, upper-case, duplicated or `..` manifest lines are also CV-GEN-10. A `DirectorySource` run has no manifest, so a newly added file simply loads.
- **If a file has a syntax error**: CV-GEN-16 (or CV-GEN-04/06 where the walk reaches the token) at the failing position; the rest of that file is not read (the reader is fail-fast), every other file still is.
- **If JSON nesting exceeds depth 32**: CV-GEN-16 at the opening token (`MaxDepth` set below the .NET default of 64; legal content nests at most 5 deep and the schema walk is not recursive, so there is no stack risk).
- **If a def file's top level is not an array (balance file: not an object)**: CV-GEN-16.
- **If the same id appears in two files of one def type**: CV-GEN-01 naming both `path:line`; neither def is kept.
- **If an id has a non-ASCII byte, an upper-case letter or 49+ characters**: CV-GEN-02. The check is a byte scan, not a .NET `Regex` (whose `$` would accept a trailing `\n`).

**Ids & enums**

- **If a def type has exactly 65 534 defs**: valid; indices 0…65 533, `0xFFFF` stays free.
- **If a def type has 65 535 defs**: CV-GEN-09 (counted per type across files); stage 3 aborts for that type and its dependent rules are skipped and counted, because index 65 535 would equal the sentinel.
- **If an enum field holds `"5"`, `"Letter"` or an unknown spelling**: CV-GEN-08 (hand-written table, never `Enum.Parse`).
- **If a reference resolves to an id of the wrong def type**: CV-GEN-01, treated as unresolved.

**StatSheet**

- **If a sheet has no sources**: `Read` returns clamp(base) with P = 1, k = 0 (F1); `Explain` reports `clampedAt = none` (CV-GEN-17 guarantees base is in range).
- **If a base lies outside its own `[min, max]`** (e.g. a container whose rows exceed `inventory.maxRows`): CV-GEN-17 at load — otherwise `Read` would silently shrink content.
- **If a Set and an Add (or a `transientAdd`) hit one stat**: the Set wins and the Adds are ignored (F1); `Explain` lists the Adds as overridden. Allowed; no rule fires.
- **If a min-wins Set exceeds base** (`RespawnMs` Set 12 000, base 10 000): CV-PLR-05 rejects it at load; were it to reach F1, the clamp `1 000…base` returns 10 000.
- **If a max-wins Set is below base** (`LateValueRatioPm` Set 300, base 500): the Set lowers the stat to 300 — base is not a participant in a Set (F1). Allowed; v1 content has no lowering Set (see Open Questions for a possible warning).
- **If a clamp's `min` exceeds the base while `max` ≥ `min`**: CV-GEN-17 at load; at runtime F1 would return `min`. If `max` < `min` after balance keys resolve: CV-GEN-15.
- **If `IStatWriter.Remove` names a source never pushed**: no-op, returns false, sheet version unchanged — the authority is the source list (CD15).
- **If a team perk is pushed twice, or a `stackIndex` ≥ `maxStacks`**: `Push` returns false and the sheet is unchanged; identity is (sourceKind, sourceId, stackIndex, ownerSlot). Perks & Perk Draft must also not offer it.
- **If a player holding personal perks disconnects and rejoins**: the picks stay in the source list keyed by `profileId` and still count toward `personalPerkCap`; the sheet is rebuilt with `ownerSlot` re-derived from the profileId → slot map, so a different slot on rejoin works. While the player is absent the picks apply to nobody.
- **If a modifier's `qualifiers` is omitted**: it applies to every qualifier. **If it is present but empty, or lists an id twice**: CV-PRK-04 (a duplicate would double-count a Mul after expansion).
- **If a qualified modifier targets a not-yet-unlocked qualifier** (a mail kind with `unlockShift` 2): valid; it binds to the `DefIndex` and is simply not read until the kind appears.
- **If a personal stat is read for a PlayerSlot with no player (valid index)**: team modifiers plus base, no personal terms. **If the slot is out of range or the Team sentinel**: `SimAssert` in debug/test; release returns the team-only value.
- **If a qualified stat is read with qualifier `0xFFFF` or a wrong-kind id**: `SimAssert` in debug/test (the base depends on the qualifier); release returns clamp(min).
- **If `transientAdd` is negative, or is passed to a stat whose `StatDef` disallows Add**: `SimAssert` in debug/test; release treats it as 0 — identical on every machine.
- **If a Mul value is 0**: allowed (CV-PRK-05 range 0…10 000); the product is 0 and the clamp decides — 0 for `WeightPenaltyPm` and `MailValueMultPm`, the stat's `min` elsewhere. No warning.
- **If the worst case has more than 6 live Mul factors on one (stat, qualifier)**: CV-PRK-10 error at stage 5; the value check is skipped, so F1's Int128 bound always holds at runtime.

**Validation**

- **If a registered balance key is absent from every file**: CV-GEN-05 naming `owner.key`; there is no default. A value outside its `safe` range loads with CV-GEN-20 (Warning); outside the registry hard range it is CV-GEN-13.
- **If `unlocks.jsonc` or a KitDef carries `modifiers` or `rules`**: CV-PRK-09. These two keys are recognised-but-forbidden there (not CV-GEN-03), so the FT-3 rule is reachable and named.
- **If a def suppresses CV-PRK-08**: the finding is attributed to the contributing def with the largest raising sum (tie: lower `DefIndex`), so `long_legs` holds the suppress. A suppress without `why` is CV-GEN-05; suppressing an Error rule or an unknown rule id is an error (CD22).
- **If a rule's inputs failed an earlier stage**: the rule is skipped for those defs, the summary prints "N rules skipped", and the exit code is 1 anyway.
- **If a description placeholder names a stat or param the def does not carry**, or a stat the def modifies twice with different values: CV-PRK-11.
- **If a deferred def (`cargo`, `hog_rider`, …) is referenced by a share > 0, a wave weight > 0, a shop, recipe, kit or unlock entry**: CV-GEN-19. Deferred defs may stay in the files: they are loaded and hashed, tagged `deferred`, exempt from CV-GEN-11 and unreachable. A deferred *enum member* (a `pipe` building behaviour) does not exist in v1 code, so it is CV-GEN-08.

**Hashing & join**

- **If the same content is hashed on Windows and Linux**: the digests are identical — fixed-width LE writes via `BinaryPrimitives`, ordinal sorts, UTF-8 with no normalisation, escapes decoded first; a golden digest is asserted on both CI operating systems, and the managed SHA-256 passes the NIST vectors on both.
- **If two defs differ only in localisation text**: the `description` *key* is hashed, the localised *text* is Presentation and is not, so peers with different English wording still share a hash. `suppress`, `$schema` and comments are not hashed.
- **If a client boots with content errors**: `Failed`; no hash exists; join and host are disabled. A client never sends a zero or placeholder hash.
- **If the host's content changes between runs**: only possible after a process restart (the set lives for the process); every join re-compares the full 32 bytes, nothing caches "compatible".
- **If a replay's recorded hash differs from the loaded set**: the loader refuses and names both 8-hex hashes; no override in release.

**Hot reload (the F6 key)**

- **If a reload yields an identical hash**: no restart, not tainted; the overlay says "no changes". Presentation content still reloads (unhashed).
- **If a reload removes a currently picked perk, or lowers `maxStacks` or the personal cap below a live pick**: the restarted run starts from its seed with no picks, so nothing dangles.
- **If a run was tainted and a later reload restores the original hash**: the restarted run is still tainted (the process has run modified content).
- **If no source directory is configured** (exported debug build without `--content-dir`/`PM_CONTENT_DIR`): the F6 key shows "No content directory" and does nothing.
- **If the F6 key is pressed in `Failed`**: ignored; only Retry applies.

**Cross-GDD**

- **If the ViewCatalog lacks a def**: in every running build, a generic placeholder view plus one warning line per id (greybox content boots without art); the release-export check fails the export. A view with no def is a warning. The hash is unaffected.
- **If a MetaProfile names a removed perk id**: Persistence drops and logs that id; other unlocks and rank are untouched. `unlocks.jsonc` naming a removed id is CV-GEN-01.

## Dependencies

**Sim / Presentation split.** The loader, `FieldSpec` tables, def records, `ContentSet`, canonical encoder (F6 bytes), `StatSheet` (`Build`, `Read`, `Explain`, both faces), `RuleFlag` set and the ContentValidator rule library live in the **Sim assembly** (`PerformativeMail.Sim/Content`, net8.0, zero Godot references — Sim Core R33). The **Godot side** holds only `ContentHost` (manifest + `FileAccess` byte reads, SHA-256 call, Failed/Valid screens, the F6 key), the `ViewCatalog` and the Presentation-content loaders. `tools/ContentValidator`, BalanceSim and SeedViewer reference the Sim assembly directly. Nothing in the Sim side reads `res://`.

**Upstream (this system depends on):**

| System | Hard / soft | What this GDD needs | Status |
|---|---|---|---|
| Simulation Core (#1) | Hard | F1 `contentTicks`; Cents / permille / Int128 conventions (R27, F7); composition root and `SimWorld` lifecycle (R4, `Freeze()` after content load); R28.10 sorted-id rule; `SimSettings` key list (its Tuning Knobs) | GDD **in design** — its Dependencies, Tuning Knobs and Open Questions are still stubs, so the reverse entry is not yet written (hand-back OQ-CDD-1) |

**Downstream (depend on this system):**

| System | Hard / soft | Interface | Reverse entry in its GDD |
|---|---|---|---|
| Grid Inventory & Containers | Hard | ItemDef, MailKindDef, ContainerDef (CD10); `InventoryRows`, `MailMaxStack`, `WeightPenaltyPm`; RuleFlags | Yes (Dependencies) — renamed stat keys are a hand-back |
| Mail & Destinations | Hard | MailKindDef, MailMixDef, DestinationTypeDef, `balance/mail`; pay/complaint stats | Yes (R3, R18, OQ18) |
| Player Character & Movement | Hard | `balance/player`, Verb enum, KitDef; movement/jump/respawn stats | Yes (`player.yaml` → JSONC hand-back) |
| Manual Delivery | Hard | `manualInsertHoldSeconds`; `DeliverInsertPm` | Yes |
| Throwing & Catching | Hard | `balance/throw`, catch volumes, EnemyDef radius/height; `ThrowRangePermille`, `ThrownCorrectPayMultPm` | Yes |
| Input & Controls | Soft | hold base from DestinationTypeDef | Indirect (via Manual Delivery); hand-back to cite this GDD |
| HUD & Notifications, Comms | Soft (Presentation) | shared validation library for cue tables (CV-HUD-*), client sheet previews | No — hand-back |
| Health & Damage, Networking & Session, World Generation, Building, Belt Logistics, Vehicles, Player Combat, Enemy AI & Waves, Defenses, Resource Harvesting, Economy, Run Structure, Perks & Perk Draft, Stamps & Run Events, Meta-Progression, Profile & Settings Persistence, Dev Tooling, Presentation & World Dressing | Hard (Dev Tooling, Perks, Stamps, Run Structure, Networking); Soft (rest) | their def tables, stat rows, `IStatWriter` (Perks, Stamps), content hash (Networking), ContentValidator CLI (Dev Tooling) | GDDs not written — they must list this dependency (systems-index already does) |

No circular dependency: this system calls nothing downstream; readers pull values, and the only writers (Perks, Stamps & Run Events) push through `IStatWriter`, which they receive by injection.

## Tuning Knobs

This system owns few gameplay values — the balance numbers belong to the GDDs whose tables they are in (CD9). Its own knobs are pipeline limits and the `StatDef` bounds.

| Knob | Default | Safe range | Owner | Affects / breaks if too high · too low |
|---|---|---|---|---|
| `stats.maxMulFactors` | 6 | 4–8 | this GDD | Bound in F1's Int128 proof · > 8 needs the bound re-derived · < 4 rejects legitimate stacking (Union Rep × Trainee already uses 2) |
| `stats.modifierPoolPerTeam` / `PerPlayer` | 128 / 64 (PROPOSED) | 64–512 / 32–256 | requested `SimSettings` key — Simulation Core owns | Pre-sized pool (Sim Core R32, allocation-free) · too low refuses a push (counted) · memory only if high |
| `content.maxJsonDepth` | 32 | 16–64 | this GDD | CV-GEN-16 · > 64 exceeds the reader default · < 8 rejects nested catch volumes |
| `content.idMaxLength` | 48 | 32–64 | this GDD | CV-GEN-02 · longer ids bloat F6 bytes and UI · shorter forces cryptic ids |
| `content.unknownFieldSuggestDistance` | 2 | 1–3 | this GDD | Suggestion quality only |
| `content.hashDisplayChars` | 8 | 8–16 | this GDD | Lobby readability vs collision visibility (the join check always uses 32 bytes) |
| `content.bootLoadCpuMs` / `bootLoadWallMs` | 100 / 200 (PROPOSED) | — | this GDD (budget) | CDD-PERF-1 · exceeding it lengthens the boot screen |
| `content.runStartBindMs` | 25 (PROPOSED) | ≤ 50 | this GDD (budget) | Counts inside spec 07 §8's 3 s / 5 s generation budget |
| `content.statRebuildMs` | 0.2 (PROPOSED; 8 players × 5 perks) | ≤ 1 | this GDD (budget) | A Draft pick must not hitch the tick |
| `StatDef` clamp bounds | per CD12 | per CD12 | this GDD owns the stat keys; every design bound is a balance key owned by the reading GDD (CD11, list under CD12); only correctness bounds stay in code | Raising a `max` lets stacking go further (card text follows automatically, FT-1); lowering below a shipped source's value triggers CV-PRK-08/CV-THR-02 |
| `perks.personalPerkCap` | 5 (spec 11 §11) | 3–8 | Perks & Perk Draft | F5 personal selection; CV-PRK-06 |
| Content duration cap | 86 400 000 ms | — | Simulation Core (F1, PROPOSED) | CV-GEN-07 |

Knob interactions: lowering `stats.maxMulFactors` can turn previously valid content into CV-PRK-10 errors; raising a `StatDef` `max` silently un-caps existing content (CV-PRK-08 warnings disappear) — review the perk pool when changing a clamp.

## Visual/Audio Requirements

None of its own (Core category; optional section). This system has no world presence, VFX or sound. Its only visual obligations are carried by other systems: the `ViewCatalog` (CD28) maps def ids to the art the Presentation & World Dressing and art-bible pipelines produce, and every v1 def must have a catalogue entry before a release export (the export check fails otherwise; running builds use a placeholder). No asset spec is needed for this GDD.

## Game Feel

No direct feel surface. The player-facing guarantee is FT-1 (card numbers equal simulated numbers, clamps shown), verified in the Perks & Perk Draft UI; the only timing this system can affect is boot and run-start latency, bounded in Tuning Knobs (`content.bootLoadWallMs` 200, `content.runStartBindMs` 25) so content never adds a perceptible pause between the lobby and Prep.

## UI Requirements

| Surface | Requirement | Owner of the screen |
|---|---|---|
| Content error screen (`Failed`) | Lists every error as `path:line:column  CV-ID (owner alias)  message`, scrollable, copyable to clipboard; buttons Retry and Quit only; host/join hidden | Menus & Run Flow Screens |
| Version + hash display | Main menu and lobby show `version · content xxxxxxxx` (8 lowercase hex, `content.hashDisplayChars`); a debug build appends `+dev` after a hot reload | Menus & Run Flow Screens |
| Join refusal | Plain sentence per `HelloAck.reason` (Network): "Different game version: you have X, host has Y." / "Same version, different content files: yours xxxxxxxx, host's yyyyyyyy." / "Host's content failed to load." / "Run in progress — join during Prep." | Menus & Run Flow Screens, Networking & Session |
| Perk / stamp card | Numbers rendered from placeholders (CD27); resolved total and "(capped)" from `Explain` (CD19) | Perks & Perk Draft |
| Debug overlay | Hot-reload result (errors, "no changes", "run restarted", "Disconnect peers first", "No content directory", tainted badge); `Explain` inspector for any (stat, qualifier, slot) | Dev Tooling |

All strings are localisation keys (no hard-coded English), follow HUD's banned-word lint (CV-HUD-05), and the error list is readable at the Accessibility text-scale settings.

> **📌 UX Flag — Content & Data Definitions**: This system has UI requirements (content error screen, version/hash line, join-refusal messages, debug overlay). In Pre-Production, run `/ux-design` for the main-menu/lobby error and refusal states (Menus & Run Flow Screens) before writing epics; stories should cite `design/ux/[screen].md`, not this GDD.

## Network

Specialist: network-programmer. Godot high-level multiplayer specifics (SceneMultiplayer authentication hooks, RPC ordering across methods, Variant integer widths, Steam lobby metadata limits) are **NOT SOURCEABLE** in this repo for 4.4–4.7 and are marked **UNVERIFIED**; Networking & Session owns the mechanics, this section owns the contract.

1. **Scope and trust model.** Friends-only (spec 13 §4.3): content is **never transferred**. Every peer loads its own files; the content hash is a *mismatch detector*, not a sync. The host's content is never pushed to clients. The hash is a mismatch detector between honest friends, not a security boundary. A modified client can report any hash, and that is accepted (anti-pillar: NOT hardened multiplayer; the host's remedy is kick).
2. **What is hashed.** Formula F6 over everything the Sim reads — all def tables, every balance key (including `player`, `throw`, `mail`, `sim`) and `SimSettings` values. Not hashed: Presentation content (`hud.tuning`, cue table, localisation, ViewCatalog), player data, and code-defined enums/validator rules (covered by the version string). Computed once at boot (`Valid` state); constant for the process except after a debug hot reload.
3. **Compatibility gate.**
   - *Lobby advertisement* (hint only): `gameVersion` + 8-hex `contentHashShort`; the browser greys out incompatible lobbies (Steam metadata limits UNVERIFIED).
   - *Handshake* before the peer is admitted and before any ordinal-bearing message: client → `Hello { gameVersion: string, contentHash: byte[32], profileId, displayName, kit }`; host → `HelloAck { ok: bool, reason: HelloRefusal, hostVersion: string, hostHashShort: string }`. Preferred mechanism: SceneMultiplayer authentication callbacks (UNVERIFIED for 4.7); fallback: a first-RPC gate that accepts nothing else until `HelloAck`.
   - `HelloRefusal` precedence (first match wins): `ContentLoadFailed` (host is in `Failed` — cannot actually host, kept for robustness) → `VersionMismatch` (hash not compared) → `ContentMismatch` (versions equal, any byte of the 32 differs) → `RunInProgress` (outside Prep; spec 13 join-in-progress rule, owned by Networking).
   - A client in `Failed` never sends `Hello`; it has no hash and never sends a placeholder.
   - The handshake is the only content check. `Ready` and `StartRun` carry no hash: the set is fixed for the process, and CD26 refuses hot reload while peers are connected.
4. **Def references on the wire.** `(defType implied by field, DefIndex: u16)`; valid only because the hash matched. Receivers range-check every index against their table and drop + log (rate-limited) an out-of-range message. Run-time strings use the per-run interning table (CD5.3) — never mixed with `DefIndex`.
5. **StatSheet replication.** Sheets are not replicated; **source lists** are: `RunSettings.stamps`, `RunState.teamPerks`, `PlayerState.personalPerks`, and a new `RunState.activeEvents[{eventIndex, startTick, endTick}]` (spec 08 §3.2 gap — hand-back to Run Structure / Data Schemas). Mid-run changes arrive as reliable-ordered events `PerkPicked { scope, playerProfileId?, perkIndex, serverTick }`, `StampActivated`, `RunEventStarted/Ended { eventIndex, tick }`, all through one RPC node on one channel (cross-method ordering UNVERIFIED; if not guaranteed, add `seq: u32` and buffer). Clients apply on receipt and rebuild with `StatSheet.Build` (CD15). Convergence follows from identical content + identical source multisets + order-independent F1. **Authority split:** the host's sheet is authoritative for everything it simulates (flight-record snapshots, pay, complaint, containers, Payday); the **owning client's own personal sheet is gameplay-relevant** for its client-authoritative movement (`MoveSpeedAddPm`, `ExtraJumps`, spec 13 §3) and its throw-arc preview — a one-event lag (~1 RTT) after a Draft pick is accepted. Optional debug-only `sheetChecksum: u32` in the 5 Hz block; on mismatch the client logs and the host resends the source lists.
6. **JoinState contribution.** Only the source lists above; no defs, no sheets.
7. **Rejoin.** The host keeps a run roster keyed by `profileId` holding `personalPerks` (and kit) for the whole run; a known `profileId` rejoining restores its picks; a new one inherits nothing (spec 06 §6); a kicked player cannot rejoin (spec 13 §4.3). Mechanics: Networking & Session and Perks (hand-back).
8. **Hot reload.** Refused on a connected client and on a host with any remote peer (CD26); a solo or peer-less host may reload, and the run becomes tainted.
9. **Replay header.** `gameVersion`, `hashSchemaVersion`, `contentHash[32]`, `runSeed`, `RunSettings`. The replay loader refuses a hash mismatch (Sim Core) and, by default, a version mismatch (debug override only).
10. **Bandwidth.** Negligible: 32 B in `Hello`, ~8 B per pick/stamp/event message; no per-tick cost.

Hand-backs: spec 08 §3.1 `protocolHash` → `gameVersion` (spec 13 §3 superseded the schema hash); dev builds must stamp the git short SHA into the version string so two different code builds with identical content cannot pass the version check (devops / lead-programmer).

## Cross-References

| This GDD | References | Kind |
|---|---|---|
| CD4, F3, CD21 | Simulation Core F1 `contentTicks`, R15 | Formula reuse |
| F1 rounding, CD4 | Simulation Core R27, F7 (`Cents.MulPermille`, `MulDivRH`, Int128) | Convention |
| CD5.2, F4 | Simulation Core R28.10 (sorted-key ids) | Rule |
| CD21 | Simulation Core R4 (composition root), R33 (assembly), lifecycle `Freeze()` | Contract (amendment requested) |
| CD25, Network 9 | Simulation Core R29/R30 (replay refuses hash mismatch; SHA-256 in tools) | Contract |
| CD10, CD12, CV-GRD-* | Grid Inventory R1, R3, R5, D1, D2, D6, D7, R12, OQ9 | Schema, stats, rules |
| CD10, CD12, CV-MAIL-* | Mail & Destinations R1, R3, R18, F1, F5–F9, OQ18, M-R1-1c | Schema, stats, rules |
| CD8, CD10, CD12, CV-PLR-* | Player Character P10, P18, F1, D6, validator rules 1–12, OQ8, OQ12 | Enum, kit, stats, rules |
| CD12 (`DeliverInsertPm` clamp 750) | Manual Delivery F2, MD9, M-MD9-1, OQ2(a) | Stat clamp |
| CD10, CD12, CV-THR-*, F3 | Throwing & Catching validator rules 1–18, F1, TC-CV-1 | Schema, stats, rules |
| CD1, CV-PLR-09 | Input & Controls R10, F2, H-F2-1, OQ-IC-2 | Ownership boundary, rule |
| CV-HUD-* | HUD & Notifications H2, H-ROUTE-1, H-ROUTE-6, H-FT-2b | Presentation rules |
| CD12 stat rows | Spec 10 §4 perks, §6 events; spec 13 §4.6 Trainee | Content source |
| F1 order | Spec 08 §2.9 (adopted) vs spec 07 §6 (withdrawn; ratified by the owner 2026-10-16) | Spec conflict resolved |
| CD27, Player Fantasy (Cannon Arm card) | Spec 10 §4.1 and spec 13 §4.1 "throw range +50%" — **superseded** (owner 2026-10-16): the card reads ≈ "30 % farther" (Throwing T10 flat-aim ratio); spec files not edited | Spec text superseded |
| OQ-CDD-13, `DraftRerolls` | Spec 01 §5.1 and spec 11 §11 Rank 4 default reroll — amended (owner ratified 2026-10-16): Rank 4 unlocks the "Second Opinion" perk; spec files not edited | Spec amendment |
| CD2, CD3, CD25, CD28 | `docs/engine-reference/godot/` (4.5 `duplicate_deep`, 4.6 `FileAccess`) | Engine facts (VERIFIED) |

## Acceptance Criteria

Specialist: qa-lead (revised after /design-review). Tags: **[Unit]**, **[Integration]**, **[CI]** (pipeline job), **[Manual]** (retained evidence: screenshots/logs); milestone **[M0]** unless marked. PROPOSED = numbers needing owner confirmation. The F1–F5 Example lines are **normative test vectors**. Traceability: CD1–CD28 and F1–F6 each map to at least one criterion below; CD5.4 is checked through allocation and API-shape proxies, FT-1's on-screen render belongs to the Perks GDD, and the PERF budgets need a min-spec machine.

### Load, parse, units, ids

- **CDD-LOAD-1** [Integration + Manual] (CD21, CD2) **GIVEN** the shipped `content/` **WHEN** `ContentHost` boots **THEN** stages 1–5 run in order, exactly one `ContentSet` is built, the state is `Valid`, and the main menu and lobby show 8 lowercase hex characters equal to the first 8 characters `ContentValidator --print-hash` prints (it prints the 64-hex digest on one line); a screenshot is retained.
- **CDD-LOAD-2** [Integration + Manual] (CD21, CD22) **GIVEN** a generated manifest naming a missing file plus one invalid value elsewhere **WHEN** boot loads **THEN** the state is `Failed`, the error screen lists both errors, host and join are disabled (no `Hello` is ever sent), only Quit and Retry show, and Retry after fixing both reaches `Valid`; a screenshot is retained.
- **CDD-LOAD-3** [Unit] (CD21, CD24) **GIVEN** a stage-1 syntax error in one file and a CV-GRD-01 input in it **WHEN** the loader runs **THEN** stages 3–5 do not run, no `ContentSet` is returned, the result lists ≥ 1 skipped (rule, def) pair naming the blocking file, and a second, clean file is still parsed.
- **CDD-LOAD-4** [Integration] (CD21) **GIVEN** a booted `ContentSet` **WHEN** two runs start in succession **THEN** both `SimWorld`s hold the same instance (`ReferenceEquals`) and `ContentLoader.ParseCount` stays at 1.
- **CDD-LOAD-5** [CI] (CD2) **WHEN** `--write-manifest` runs over the shipped tree **THEN** the manifest is lowercase ASCII and ordinal-sorted and lists every file; a `DirectorySource` run over the same tree yields the same hash as the PCK boot.
- **CDD-LOAD-6** [Manual] (CD2, CD25) **GIVEN** exported Windows and Linux builds of the pinned engine **WHEN** launched **THEN** every content file loads (PCK or the S1 fallback) and the golden-fixture digest equals CI's (closes spike S1).
- **CDD-PARSE-1** [Unit] (CD3.1–2) **GIVEN** one def saved with LF, CRLF, a leading BOM, `//` comments and trailing commas **WHEN** each variant is loaded **THEN** all variants yield the same content hash; a missing file and a 0-byte file give CV-GEN-10 and CV-GEN-16 respectively.
- **CDD-PARSE-2** [Unit] (CD3.3, CD3.5) **GIVEN** `{"id":"a","id":"b"}` starting at line 7 column 14 **WHEN** parsed **THEN** exactly one CV-GEN-04 reports `path:7:14`, the offending value and the allowed form; `maxStak` gives CV-GEN-03 suggesting `maxStack` at the field's own line and column; `$schema` and `suppress` keys pass.
- **CDD-PARSE-3** [Unit] (CD3.4, F2) **PARAMETRISED**: `1e3`, `-0`, `-0.00`, `0.1000` on a decimal field and `1.0` on an integer field each give exactly one CV-GEN-06; `5.` and `+5` (invalid JSON) each give exactly one CV-GEN-16.
- **CDD-PARSE-4** [CI] (CD3.4, CD5.5, CD8) **WHEN** the Sim and tools projects build **THEN** the banned-API analyzer rejects a seeded use of each of `GetDouble`, `Enum.Parse`, `Enum.TryParse`, a culture-default string sort or compare, and `ToLower`/`ToUpper`; both projects set `InvariantGlobalization` (Godot APIs are already banned by Sim Core SC-ARCH).
- **CDD-UNIT-1** [Unit] (CD4) **PARAMETRISED**: `batchIntervalSeconds: 15` → 15 000 ms (450 ticks), `catchRadiusMetres: 1.5` → 1 500, `lateValueRatio: 0.5` → 500, `complaintOnMisdeliveryPoints: 5` → 5 000, `loftDeg: 6` → 6 000, `price: 400` → 400 cents; a numeric field with no declared unit gives CV-GEN-06.
- **CDD-UNIT-2** [Unit] (F2) **PARAMETRISED**: `0.4` → 400, `1.5` → 1 500, `15` → 15 000, `0.05` → 50, `-2.25` → −2 250; `0.1234` → CV-GEN-06; decimal `2147483.648` → CV-GEN-12 and `2147483.647` passes; integer field `2147483647` passes and `2147483648` → CV-GEN-12; duration `86400.001` s → CV-GEN-07 and `86400` passes.
- **CDD-UNIT-3** [Unit] (F3) **PARAMETRISED**: 500 → valid (15 ticks), 100 → valid (3), 501, 50, 150 → invalid (Sim Core F1 gives 5 for 150); property: for every ms in 0…86 400 000, F3 is true iff `ms % 100 == 0`.
- **CDD-ID-1** [Unit] (CD5.1) **GIVEN** ids `Letter`, `1abc`, a 49-character id and `ab` **WHEN** loaded **THEN** the first three give CV-GEN-02 each and a 48-character id passes; a duplicate within one type gives CV-GEN-01; the same id in two different types passes.
- **CDD-ID-2** [CI] (CD5.2, CD5.5, F4) **GIVEN** the six mail kinds in shuffled file order (fixed seed) **WHEN** built on the Windows and Linux CI jobs **THEN** cargo 0, large 1, letter 2, medium 3, postcard 4, small 5; and `bike` 0, `bike2` 1, `bike_cargo` 2 on both.
- **CDD-ID-3** [Unit] (CD5.2–3) **GIVEN** 65 534 defs in one type **WHEN** built **THEN** the maximum `DefIndex` is 65 533 and `0xFFFF` is never assigned; 65 535 defs give CV-GEN-09; adding a mail kind `aaa` changes no item's `DefIndex`; `default(DefIndex)` is None.
- **CDD-ID-4** [Unit] (CD5.4, CD15) **GIVEN** a warmed-up 1 000-tick scripted run and 1 000 000 `Read` calls **WHEN** measured with `GC.GetAllocatedBytesForCurrentThread` **THEN** both allocate 0 bytes; an architecture test finds no `string` parameter on `ContentSet`/`IStatSheet` lookup members and no `Dictionary<string, …>` field in any Sim system.

### Schema and enums

- **CDD-SCHEMA-1** [Unit] (CD1) **GIVEN** a booted set **WHEN** a `hud.tuning` value changes **THEN** the content hash is unchanged; an architecture test finds no MetaProfile, settings or input-binding type in the Sim content assembly's `FieldSpec` set.
- **CDD-SCHEMA-2** [Unit] (CD6) **WHEN** reflection scans every def type, nested type and generic argument **THEN** there is no `T[]`, `float`, `double`, `decimal` or `Half` member, lists are `ImmutableArray<T>`, and the Sim assembly references no Godot assembly.
- **CDD-SCHEMA-3** [Unit] (CD7) **GIVEN** an ItemDef without `weightClass` **WHEN** loaded **THEN** exactly one CV-GEN-05 is reported (CV-THR-08 skipped); `allowedCategories` omitted passes; `[]` gives CV-GRD-03; every `FieldSpec` default ∈ {any, 0, false, none, 1000}.
- **CDD-SCHEMA-4** [Unit] (CD8) **GIVEN** `weightClass` `"5"`, `"Light"` or `"LIGHT"` **WHEN** loaded **THEN** CV-GEN-08 each; a snapshot test fails if any enum member's numeric value changes or is reused, or a member lacks a data spelling.
- **CDD-SCHEMA-5** [Unit] (CD9) **GIVEN** `player.rangeMm` in two files, an unregistered key, a value outside the hard range, or a key in a file whose prefix is not its registry owner **WHEN** loaded **THEN** one CV-GEN-13 each; a value outside its `safe` range gives CV-GEN-20 (Warning) and loads.
- **CDD-SCHEMA-6** [Unit] (CD10) **GIVEN** a `pile` container with `grid` **WHEN** loaded **THEN** CV-GEN-14; `standardIssue` on an ItemDef gives CV-GEN-03; a valid KitDef and RunEventDef load and enter the hash; a `FieldSpec` snapshot matches the CD10 table.
- **CDD-SCHEMA-7** [Integration] (CD28) **GIVEN** a def with no ViewCatalog entry **WHEN** booted **THEN** debug and release boots both reach `Valid` with the placeholder view and one warning; the release-export check fails naming the def; a view entry with no def warns; the content hash is unchanged in all cases.

### StatSheet

- **CDD-STAT-1** [Unit] (CD11, CD12) **WHEN** the `StatDef` table is enumerated **THEN** it matches a snapshot of the CD12 table (rows, units, scopes, qualifiers, ops, clamps — `ThrowRangePermille` 1000–1500, `MailMaxStack` 1–255), the enum ↔ snake_case map is bijective, and a `StatDef` with `min` > `max` gives CV-GEN-15.
- **CDD-STAT-2** [Unit] (CD11) **GIVEN** `MailValueMultPm` Mul 1250 qualified [postcard, letter] **WHEN** loaded **THEN** it expands to two modifiers; an unqualified modifier applies to all kinds; a qualifier on an unqualified stat gives CV-PRK-04.
- **CDD-STAT-3** [Unit] (CD13) **GIVEN** a team perk on personal-only `DeliverInsertPm` **WHEN** loaded **THEN** CV-PRK-03; a Mul on it gives CV-PRK-02; Mul 10 001 gives CV-PRK-05 and 10 000 passes; Cannon Arm's Add +500 on `ThrowRangePermille` (|A| ≤ 500) passes; depot rows base 10 with ×1.5 and +1 resolve to 16.
- **CDD-STAT-4** [Unit] (CD14, FT-3) **WHEN** the architecture test scans constructors **THEN** `IStatWriter` appears only in Perks & Perk Draft and Stamps & Run Events, `StatSheet.Build` takes no profile or rank input, and the only source kinds are Perk, Stamp and RunEvent; a KitDef or `unlocks.jsonc` entry carrying a modifier or rule flag gives CV-PRK-09.
- **CDD-STAT-5** [Unit] (CD15) **GIVEN** identical source lists **WHEN** sheets are built from scratch, incrementally, and client-side without a `SimWorld` **THEN** every (stat, qualifier, slot) read is equal, and the version rises by exactly 1 per add/remove.
- **CDD-STAT-6** [Unit] (CD16) **GIVEN** Postal Inspector pushed (`ShiftEarningsMultPm` 900, lifetime Shift) **WHEN** read before and after its `Remove` at Payday **THEN** 900 then 1 000; with no `Remove`, a read 10 000 ticks later is still 900, and no `Read` ever changes the sheet version.
- **CDD-STAT-7** [Unit] (CD17) **GIVEN** Long Legs picked 3 times **WHEN** resolved **THEN** `stackIndex` 0, 1, 2 exist and `MoveSpeedAddPm` = 360; a team perk with `maxStacks` 2 gives CV-PRK-06; a personal perk with `maxStacks` 6 gives CV-PRK-06 and 5 passes.
- **CDD-STAT-8** [Unit] (CD18, CD19) **GIVEN** Long Legs ×3 and `transientAdd` 300 **WHEN** read **THEN** `Read` = 400 and `Explain` = unclamped 660, `clampedAt` max; with `transientAdd` 0 it is 360; a RuleFlag held by two sources stays on after one is removed and is off after both.
- **CDD-STAT-9** [Integration] (CD20) **GIVEN** a throw at tick t with `ThrowRangePermille` 1 500 **WHEN** Cannon Arm's source is removed at t+1 **THEN** the in-flight record keeps 1 500.
- **CDD-STAT-10** [Unit] (F1) **PARAMETRISED** over the F1 Example lines plus `RespawnMs` Set 500 → 1 000 and Set 3 000 → 3 000, Floor(−1, 1000) = −1 and Ceil(−1, 1000) = 0, two max-wins Sets 700/900 → 900; and a pure `Resolve` on a synthetic `StatDef` (min 0, max `int.MaxValue`) with base 2^31−1, six Mul 10 000 and 65 535 Adds of 2^31−1 returns `unclamped` equal to a `BigInteger` reference and `Read` = max.
- **CDD-STAT-11** [Unit] (F5) **WHEN** validated **THEN** postcard 40 × 2 000 = 80 and letter 20 → 40 pass CV-GRD-01 while maxStack 200 → 400 errors; `MoveSpeedAddPm` 360 + T 300 = 660 > 400 warns CV-PRK-08 (suppressed on `long_legs`); the F5-low quota example (284 < 300) warns CV-PRK-08; `ThrowRangePermille` +500 passes and +501 gives CV-THR-02 with CV-PRK-05 in `alsoTrips`; CV-PRK-10 ignores a perk beyond its `maxStacks` and the second member of an `excludes` pair.
- **CDD-STAT-12** [Integration] (CD20) **GIVEN** a discrete op that latched stat S at its start **WHEN** S's source is removed mid-op **THEN** the op completes with the latched value, and a continuous reader (`MoveSpeedAddPm`) sees the change on the next tick.
- **CDD-STAT-13** [Unit] (CD15, Network 7) **GIVEN** a source list with a personal pick by `profileId` P **WHEN** P's slot mapping changes **THEN** the rebuilt sheet applies the pick to the new slot and the old slot reads team + base only.
- **CDD-STAT-14** [Unit] (CD13, determinism) **GIVEN** 1 000 random modifier sets (seed logged) **WHEN** each is pushed in random orders **THEN** every `Read` equals a `BigInteger` reference implementation of F1.

### Validator and CLI

- **CDD-VAL-1** [CI] (CD23, CD24, CD22) **PARAMETRISED** over every rule clause in the registry: **GIVEN** the clause's violation fixture **WHEN** validated **THEN** exactly one diagnostic with that id at the rule's severity is reported and any other id is in the fixture's reviewed `alsoTrips`; the clause's valid-edge fixture passes (normative edges: CV-GRD-01 maxStack 127 → 254 passes, 128 → 256 errors; CV-THR-12 `catchTapMs` 500 and 100 pass, 501 errors; CV-MAIL-02 sum 1 000 passes, 999 errors, one share 601 errors); the shipped `content/` passes `--strict`; the meta-test iterates the registry (no hard-coded count). Satisfies Throwing TC-CV-1 through the alias (RULE-2) and Player Character PCM-F8-3.
- **CDD-RULE-1** [Unit] (CD22, FT-4) **GIVEN** `worldItemDespawnSeconds` 29 **WHEN** validated **THEN** CV-GRD-07 warns, the run proceeds and the CLI exits 0; with `--strict` it exits 1; with a `suppress` plus `why` it exits 0; a `suppress` without `why` is an error; a `suppress` naming CV-GEN-04 is an error; the registry's warning set equals CD22's list and no Error rule is suppressible.
- **CDD-RULE-2** [Unit] (CD23) **WHEN** a rule fails **THEN** the message prints the id and alias (`CV-THR-12 (THROW-CV-12)`); `THROW-CV-12` resolves to CV-THR-12, `PC rule 7` to CV-PLR-07 and `Throwing rule 14` to CV-PLR-06; a snapshot test fails if any id is removed or renumbered; CV-THR-14 stays listed as retired.
- **CDD-CLI-1** [CI] (CD24) **WHEN** the CLI runs **THEN** it exits 0 on clean content, 1 on errors, 2 on a missing `--content` directory, 3 when a test-only registry rule throws; two runs on a 5-error fixture give byte-identical output sorted by (path, line, column, rule) with `/` paths; `--format github` emits one escaped `::error file=…,line=…,col=…::` line per error; `--list-rules --format md` equals the checked-in `docs/content-rules.md`.

### Hash, network, hot reload

- **CDD-HASH-1** [Unit] (CD25, F6) **GIVEN** a fixture **WHEN** comments, whitespace, line endings, key order or the order of a set-valued list change **THEN** the hash is unchanged; **WHEN** `price` 400 → 401, any balance key, or an optional field's presence changes **THEN** it changes.
- **CDD-HASH-2** [Unit] (F6) **GIVEN** the F6 `letter` fixture **WHEN** encoded by the encoder and by an independent test encoder **THEN** both produce exactly the 27 bytes shown in F6, the type block opens `06 00`; the managed SHA-256 passes the NIST known-answer vectors.
- **CDD-HASH-3** [CI] (F6) **GIVEN** the golden fixture **WHEN** hashed on the Windows and Linux CI jobs **THEN** both equal the checked-in digest.
- **CDD-NET-1** [Integration] (Network 3) **GIVEN** a `Hello` whose version and hash both differ **WHEN** the host answers **THEN** `HelloAck{ok:false, reason: VersionMismatch, hostVersion}`, and the hash is not compared.
- **CDD-NET-2** [Integration] (FT-2) **GIVEN** the same version and a hash differing in byte 0 **WHEN** the client joins **THEN** `ContentMismatch` with `hostHashShort`; the client message names both versions and both short hashes; no tick runs on the client; no message carries def data.
- **CDD-NET-3** [Integration] (Network 3) **PARAMETRISED** over the six pairs of refusal conditions (host `Failed`, version, content, mid-run) **WHEN** a client sends `Hello` **THEN** the reason follows ContentLoadFailed → VersionMismatch → ContentMismatch → RunInProgress. (CDD-NET-4 retired: merged here.)
- **CDD-NET-5** [Integration] (CD15, Network 5) **GIVEN** 8 peers and a late joiner **WHEN** sheets are rebuilt from `teamPerks`, `personalPerks`, `stamps`, `activeEvents` **THEN** every (stat, qualifier, slot) read is equal on all peers.
- **CDD-NET-6** [Integration] (Network 7; needs OQ-CDD-11) **GIVEN** a player with 3 personal perks disconnects **WHEN** the same `profileId` rejoins **THEN** picks and sheet are identical to before; a different `profileId` gets none.
- **CDD-NET-7** [Integration] (Network 9) **WHEN** a replay is written **THEN** its header holds `gameVersion`, `hashSchemaVersion`, `contentHash`; a replay with a mismatched hash is refused naming both short hashes.
- **CDD-HOT-1** [Integration] (CD26) **GIVEN** an editor run and an invalid edit **WHEN** the F6 key is pressed **THEN** the old set and hash stay, the overlay lists the errors, and the run is not tainted.
- **CDD-HOT-2** [Integration] (CD26) **GIVEN** an editor run (or `--content-dir`) and a valid edit, no peers **WHEN** the F6 key is pressed **THEN** the new hash is computed, the run restarts from its seed, and it is tainted and not recorded; an exported debug build with no content directory shows "No content directory"; a release build has no F6 handler.
- **CDD-HOT-3** [Integration] (CD26, Network 8) **GIVEN** a host with a remote peer, or any connected client **WHEN** the F6 key is pressed **THEN** it is refused with "Disconnect peers first"; peers stay connected; the set is unchanged.

### Performance (PROPOSED; advisory until technical-director names the min-spec machine)

- **CDD-PERF-1** [CI, advisory] **GIVEN** v1 content **WHEN** booted 10 times in fresh processes (cold JIT) **THEN** median parse + validate + hash is reported against a stored baseline (fails above 2× baseline); the absolute ≤ 100 ms CPU / ≤ 200 ms wall budget is verified [Manual] on the min-spec machine.
- **CDD-PERF-2** [CI, advisory] **WHEN** a run starts **THEN** content binding (target 25 ms) and an 8-player × 5-perk sheet rebuild (target 0.2 ms) are reported against the baseline; `Read` does not iterate modifiers (structural test: its cost is independent of live modifier count by construction).

### Fantasy tests

- **CDD-FT-1** [Unit] (CD19, CD27) **GIVEN** the pure placeholder resolver **WHEN** two +500 `DeliverInsertPm` sources render **THEN** the text shows 750 "(capped)"; `{fx:throw.rangeRatio}` for Cannon Arm renders the registered function's output; a literal digit in English description text, a placeholder naming a stat the def does not carry, or a stat modified twice with different values each give CV-PRK-11. (FT-2 is verified by CDD-NET-1/2; FT-3 by CDD-STAT-4; FT-4 by CDD-RULE-1; the on-screen card belongs to the Perks GDD.)

## Open Questions

### Hand-backs to other GDDs (this GDD does not edit them)

| ID | To | Ask | Target |
|---|---|---|---|
| OQ-CDD-1 | Simulation Core | (a) Add `ContentSet` (with `SimSettings` as a member), `IStatSheet`, and — for Perks and Stamps & Run Events only — `IStatWriter` to R4's injectable list and its reflection test; (b) write the reverse Dependencies entry; (c) own the `stats.modifierPoolPerTeam/PerPlayer` `SimSettings` keys; (d) `StringId` contract = CD5.3 (namespace = def type); (e) confirm the canonical encoder lives in the Sim and SHA-256 outside it (CD25); (f) HB11 accepted: the F8 `SimSettings` constraints run as CV-SIM-01…04 (index row; Sim Core's SC-SET-1 text is authoritative) | Before Sim Core is approved |
| OQ-CDD-2 | Grid Inventory | Adopt stat names `InventoryRows`, `MailMaxStack` (replaces `stack_mult`; `effMax = Read(MailMaxStack, kind)`), `WeightPenaltyPm` (`strongBackPermille` = 1000 − it); confirm `acceptsInsert` semantics (CD10); OQ9 answered — `weightClass` required, v1 items authored explicitly; add `KitDef.standardIssueItem` entry flag for the granted copy; rule for a container whose rows shrink (never evict); number Grid's validator rules (then CV-GRD-* collapses to an index row); CV-GRD-05 now reads `player.respawnMs`; adopt the former CDD-STAT-13 as a Grid AC (Bulk Mailer → letter 40, postcard 80) | Grid re-review |
| OQ-CDD-3 | Mail & Destinations | OQ18 answered (CD10 + `balance/mail`); adopt `…Pm` stat names (`MailValueMultPm[kind]`, `ThrownCorrectPayMultPm`, `LateValueRatioPm` via Set + RuleFlag `SuppressLateComplaint`, `Misdelivery…MultPm`); `complaintOnMisdeliveryPoints` single stored unit (milli-points); `catchVolume` field shape; `rollCallNames` is a session setting; key name `foundValueMultPm` adopted here; number Mail's validator rules (then CV-MAIL-* collapses to an index row) | Mail re-review |
| OQ-CDD-4 | Player Character & Movement | `player.yaml` → `content/balance/player.jsonc`; stat names `MoveSpeedAddPm`, `ExtraJumps`, `RespawnMs`; Postal Sprint passes `transientAdd` (CD18); OQ8 standard-issue = `KitDef.standardIssueItem` + Grid entry flag, not an ItemDef flag; Verb enum hosted here, members PC's; respawn key is `player.respawnMs` everywhere (not Run Structure's `respawnSeconds`); retire the `secondWindMs` knob — the Second Wind perk def's Set value is the only source | PC re-review |
| OQ-CDD-5 | Throwing & Catching | **Cannon Arm card: closed 2026-10-16 — owner decided** the card reads the computed outcome, ≈ ×1.30 = "30 % farther"; no retune; spec 10/13's "+50 %" superseded (Cross-References). Still open: "BalanceConfig / ThrowDef" → `content/balance/throw.jsonc`; rename withdrawn — the stat stays `ThrowRangePermille` (CD12), Cannon Arm = Add +500 (not "pushes 1500"); codes `THROW-CV-nn` are the alias of CV-THR-nn (CD23); register the pure `{fx:throw.rangeRatio}` function (flat-aim range ratio) for the card (CD27); list `throw.rangeMinPermille`/`rangeMaxPermille` in Tuning Knobs; rule 12 wording "multiples of 100/3 ms" → "multiples of 100 ms" (F3); registry `throwSpeedByClass` "default light" superseded by CD7 | /design-review of Throwing |
| OQ-CDD-6 | Manual Delivery, Input & Controls | MD's clamp hand-back closed (`DeliverInsertPm` `StatDef` max 750); Input cites this GDD for the hold base and acknowledges CD1 (action map and `settings.input` are Input's own data); Manual Delivery lists `delivery.insertPmMax` (750) in its Tuning Knobs and adopts the former CDD-STAT-12 (two +500 sources → `Read` 750 → F2 uses 750) as its own AC | Re-reviews |
| OQ-CDD-7 | HUD & Notifications | `hud.tuning` → `content/presentation/hud.tuning.jsonc`; cue-table rules registered as CV-HUD-01…05 in the shared library | HUD re-review |
| OQ-CDD-8 | Profile & Settings Persistence (+ Input, technical-director) | Input OQ-IC-2: MetaProfile/settings schema is Persistence's (CD1), with `settings.input` owned by Input; stored content ids resolved by string, unknown ids dropped and logged | M1 |
| OQ-CDD-9 | Run Structure / Data Schemas (spec 08) | Add `RunState.activeEvents`; `protocolHash` → `gameVersion`; confirm `ShiftEarningsMultPm` and `ZeroMisdeliveryBonusPm` as the Payday stats; StampDef `scoreMultRatio` read at Results; state whether `ZeroMisdeliveryBonusPm` is added to the earnings multiplier or to earnings, and the order against `ShiftEarningsMultPm` (and drop its 1 500 ceiling or add a raising source); `waves.warningMs` replaces `warningSeconds` | Run Structure GDD |
| OQ-CDD-10 | Spec 07 / spec 08 (owner) | **Product form: closed 2026-10-16 — owner ratified.** Spec 07 §6 "base × Σmultipliers + Σadders" withdrawn in favour of spec 08 §2.9's product form (F1). Still open (spec text): spec 08 §2 schema deltas (CD10), `balance.json` → per-owner files (CD9), shares as permille | Next spec pass |
| OQ-CDD-11 | Networking & Session, Perks & Perk Draft | Run roster keyed by `profileId` keeping personal picks for the whole run; `HelloRefusal` precedence; one RPC channel for source-list events (or `seq`) | Networking GDD (M0) |
| OQ-CDD-12 | devops-engineer / lead-programmer | Dev builds stamp the git short SHA into the version string | M0 |
| OQ-CDD-18 | Every owner of a CD12 design-clamp key (Manual Delivery, Player Character, Grid, Throwing, Mail, Automation, Waves, Run Structure) | List the key in your Tuning Knobs with a safe range (`delivery.insertPmMax`, `player.buffCapPm`/`maxExtraJumps`/`respawnMinMs`, `inventory.maxRows`, `throw.rangeMin/MaxPermille`, `mail.payMultMaxPm`/`penaltyMultMaxPm`, `automation.maxFilterSlots`, `waves.maxWarningMs`, `run.zeroMisdeliveryBonusPm`); until then each is PROPOSED | Each owner's re-review |

### Open design questions

| ID | Question | Owner | Target |
|---|---|---|---|
| OQ-CDD-13 | Postal Rank 4's default second Draft reroll (spec 01 §5.1 table, spec 11 §11) is meta power by default (Pillar 4; spec 01 "ranks unlock content, never stats"). **Rank 4: closed 2026-10-16 — owner ratified.** **CD ruling (/design-review 2026-10-16, ratified by the owner 2026-10-16; supersedes the CD-GDD-ALIGN stamp direction; amends spec 01 §5.1 / spec 11 §11):** Rank 4 unlocks a **personal draft perk "Second Opinion"** (`DraftRerolls` +1, CD12) that costs a pick slot (counts toward `perks.personalPerkCap`). A stamp was rejected: stamps are lobby-wide, a reroll count is neither a Stat nor a RuleFlag, and the per-stamp XP bonus would make it a net buff. Fallback if a stamp is insisted on: a per-player run-start option, `scoreMultRatio` ≈ 0.95, no stamp XP bonus. The same test applies to rank-gated KitDefs (Sea, Pipes): each must give something up — a kit value budget (warning when a kit exceeds the Land kit) is the proposed check (still open). | game-designer / Meta-Progression / Perks | Meta-Progression GDD (kit budget only) |
| OQ-CDD-14 | Should a max-wins Set that *lowers* a stat below its base be a warning (no v1 content does this)? | systems-designer | Perks GDD |
| OQ-CDD-15 | `both`-scope stats on team-owned containers and vehicles: a shared chest, depot or `bike_cargo` — team sheet only (a team-only `Read`), or the rider's/opener's personal sheet? Should unqualified personal Adds on `both` stats be rejected? | Vehicles & NPC Drivers / Grid / Perks | Vehicles GDD |
| OQ-CDD-16 | CD12 stat rows for Building, Belt, Defenses, Combat, Enemy AI, Vehicles and Run Structure are provisional until those GDDs exist; each may rename or re-bound its rows | each owner | per GDD |
| OQ-CDD-17 | `inventory.maxRows` default (CV-GEN-17, `InventoryRows` clamp) not yet set | Grid Inventory | Grid re-review |
| OQ-CDD-19 | In-place hot swap (structurally identical sets) and a host-broadcast "reload all" for networked tuning playtests — needs an indirection for systems' cached sub-tables (CD21); build only if reload-and-restart proves too slow | Dev Tooling / technical-director | M4 |
| OQ-CDD-20 | CV-PRK-11 needs English description text, but localisation is Presentation content with no CLI-readable format; define a JSON strings file and whether a localisation typo may block hosting | Localisation / Perks | M1 |
| OQ-CDD-21 | Economy checks: hand-vs-throw pay parity (Stamp Collector × Cannon Arm ≈ ×1.375 thrown vs ×1.25 by hand) as a BalanceSim acceptance check; a `StatDef.intentionalCap` flag instead of the `long_legs` CV-PRK-08 suppress; Watch alias "+25 %" vs Mail's "+35 % total" | economy-designer / Perks / Mail | Perks GDD |

### Spikes (UNVERIFIED engine behaviour)

- **S1** — Godot 4.7.1 export filter includes `*.jsonc` and `manifest.txt` in the PCK, and `Godot.FileAccess.GetFileAsBytes` reads them in an exported build (Windows + Linux). Also: does `DirAccess` enumerate non-resource files inside a PCK (would make the manifest unnecessary)? Fallback, possibly primary: `EmbeddedResource` in the assembly (loses hot reload in exports).
- **S2** — Smoke test only: `Utf8JsonReader` and `ImmutableArray` (no reflection) run in a release desktop export; whether 4.7.1 desktop exports trim at all is **UNVERIFIED**.
- **S3** — Retired: the managed SHA-256 in the Sim (CD25) removes the OpenSSL dependency.
- **S4** — `[Export] PackedScene` arrays in a C# `ViewCatalog` resource keep export dependency tracking and survive renames via `uid://`.
- **S5** (Networking) — SceneMultiplayer authentication callbacks for the `Hello`/`HelloAck` gate; RPC ordering across methods on one channel.

These spikes, the manifest generator, the loader/pipeline shape, the `ContentHost` shape (autoload vs static; async load marshalled back with `CallDeferred`), the ViewCatalog split (CD28) and an Int64 fast path for `Read` (Int128 is software-emulated) are **ADR candidates** (technical-director) before implementation.
