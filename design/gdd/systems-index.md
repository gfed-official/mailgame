# Systems Index: Performative Mail (working title)

> **Status**: Draft (project stage: Systems Design as of 2026-10-15)
> **Created**: 2026-10-15
> **Last Updated**: 2026-10-15
> **Source Concept**: design/gdd/game-concept.md
> **Source Spec**: design/spec/ (chapters 01–13; chapter 13 wins on conflict)
> **Gates**: TD-SYSTEM-BOUNDARY: CONCERNS (applied, see Dependency Map) · PR-SCOPE: OPTIMISTIC (adjustments applied, see Priority Tiers) · CD-SYSTEMS: CONCERNS (revisions applied, see Priority Tiers)

---

## Overview

Performative Mail is a 1–8 player co-op logistics rogue-lite: mail spawns at a Post Office on a seeded island and must reach the right mailbox, first by hand and by throwing, later by belts, sorters and trucks, while raids threaten the constructs. Mechanically it is three stacks that meet at the mailbox: a **delivery stack** (mail, addresses, grid inventory, the carrier, throwing), an **automation stack** (building, belts and sorters, vehicles), and a **defense stack** (health and damage, enemies and waves, walls and turrets), all wrapped by a **run loop** (five shifts, quota, complaint meter, perks, stamps) and shared across players by host-authoritative networking. The pillars shape the split: *the mail must flow* (1) makes Mail & Destinations the hub every other gameplay system touches; *hands beat machines* (3) is why Manual Delivery and Vehicles' player takeover are first-class systems rather than UI; *short runs, long game* (4) separates Run Structure from Meta-Progression so nothing meta changes in-run power; *scales 1 to 8* (5) is a cross-cutting rule enforced by Run Structure's budgets.

**System-by-system rationale.** *Simulation Core* and *Content & Data Definitions* exist because spec ch. 07 mandates a plain-C# Sim assembly with a 30 Hz tick, integer money and data-driven content; Content & Data also owns the `StatSheet` so perks and stamps can modify any stat without every system depending on Perks. *Health & Damage* was added at the TD gate: damage originates in five systems (weapons, enemies, thrown items, vehicle impacts, enemies on constructs) and needed one owner. *Player Character & Movement* owns the generic interaction framework (focus, prompt, `IInteractable`) because shop, PA, vehicles, chests, catching and deconstruct all use "interact"; *Manual Delivery* is therefore only the address-match prompt and hand insertion. *Grid Inventory* is the single most depended-on gameplay system and also owns `WorldItem` (a stack outside any container). *World Generation* includes the runtime tile grid and routing graph that movement, building, vehicles and enemy pathing read; the M-1 greybox needs a hand-authored stub of that representation even though generation proper is M1. *Belt Logistics* is kept whole (belts, splitter/merger, Address Sorter, inserters, chests, depots) because the sorter is the belt's reason to exist. *Run Structure* is the orchestrator: it calls down into Waves, Economy, Mail and World Generation, and they report up via events they define — this is the rule that removes the two cycles below and deviates from spec ch. 07 §4 in two places (complaint meter owned by Run Structure, wave scheduler by Enemy AI & Waves). *Networking & Session* owns transport, lobby, join-in-progress and the JoinState envelope; every gameplay GDD carries a required **Network** section specifying its own replicated state and join-snapshot contribution, kept out of the Sim assembly. *Meta-Progression* writes the unlocked set to Persistence; Run Structure reads it at run start and passes it to Economy, Perks and Stamps so none of them depend on Meta.

**Relationships at a glance.** Mail & Destinations ← Grid Inventory ← Content & Data is the spine; Throwing sits on it and on Belt Logistics (a thrown item landing on a belt joins it); Vehicles sits on Building and Belt Logistics (depot interface) and on Health & Damage (occupied vehicles are enemy targets and deal impact damage). Economy depends on Mail (payments), Building (costs) and Harvesting (materials); Run Structure depends on Economy (quota vs wallet) and Networking (phase eligibility, join-in-progress in Prep). UI systems are thin wrappers on the gameplay systems they display.

**Working order (PR-SCOPE).** The producer's advice, adopted here: prototype M-1a before writing GDDs, then write GDDs just-in-time one milestone ahead; keep `workflow: full` for the six large systems and consider `workflow_overrides.system_overrides.<system>: standard` for the S-sized ones. Timeline: **no fixed date — as fast as possible; milestones are gates** (spec 13 §6). First shippable point is **v1-lite** (post-M2: co-op seeded run with automation, no raids). Producer reference estimate ≈ 1,100 h implementation (700–1,800), uncalibrated — re-run PR-SCOPE after M-1a with actual hours.

---

## Systems Enumeration

Names marked "(inferred)" were not explicit in the concept or spec. Milestone tags: **first slice → complete**.

| # | System Name | Category | Priority | Status | Design Doc | Depends On |
|---|-------------|----------|----------|--------|------------|------------|
| 1 | Simulation Core | Core | MVP (throwaway in M-1 → M0; GDD milestone slice: real files from M-1a) | In Review (Provisional) (/design-review 2026-10-16 MAJOR REVISION NEEDED, 11 blockers — revision applied, 1,772 → 1,046 lines, 131 → 53 ACs (25 at M-1a); algorithms/layouts/perf slices moved to docs/architecture/simulation-core-adr-inputs.md (UNREVIEWED SEED); 9 ADRs listed; re-review decides Approved; hand-backs HB1–HB12 to Grid Inventory, PC, Mail, Manual Delivery, Throwing, HUD, Content & Data, Health & Damage, Networking, Run Structure) | design/gdd/simulation-core.md | — |
| 2 | Content & Data Definitions (incl. StatSheet) | Core | MVP (hard-coded in M-1 → M0) | In Review (Provisional) (/design-review 2026-10-16 NEEDS REVISION, 14 blockers — revision applied; re-review decides Approved; GUARD overridden by owner instruction; UX Flag: content error screen, version/hash line, join-refusal messages; Rank-4 → "Second Opinion" personal perk ratified by the owner 2026-10-16; hand-backs to Sim Core, Grid, Mail, PC, Throwing, MD/Input, HUD, Persistence, Run Structure, Networking, clamp-key owners, spec 07/08) | design/gdd/content-data-definitions.md | Simulation Core |
| 3 | Input & Controls | Core | MVP (M-1a) | In Review | design/gdd/input-controls.md (UX Flag: /ux-design for Controls settings, interact prompt + hold ring, action menu) | — |
| 34 | Health & Damage (inferred) | Core | MVP (stub M-1b → M3) | In Review (Provisional) (/design-review 2026-10-16 NEEDS REVISION, 10 blockers — revision applied, 452 → 454 lines; re-review decides Approved; GUARD overridden by owner instruction 2026-10-16; hand-backs HB-HD-1…4 to Player Character, Simulation Core, Content & Data, plus SimAssert capture hook → Sim Core, flinch rate → PC) | design/gdd/health-damage.md | Simulation Core, Content & Data Definitions |
| 5 | Networking & Session | Core | Vertical Slice (M0) | Not Started | — | Simulation Core, Content & Data Definitions |
| 6 | World Generation (incl. runtime tile grid, routing graph) | Core | Vertical Slice (stub M-1a → M1) | Not Started | — | Simulation Core, Content & Data Definitions |
| 8 | Grid Inventory & Containers (incl. WorldItem) | Core | MVP (minimal M-1a → M1) | In Review (third /design-review 2026-10-16 NEEDS REVISION, 21 blocking contract/fixture items — revision applied; a targeted check decides Approved; hand-backs OQ27 → Input/HUD, Manual Delivery, Throwing, Networking) | design/gdd/grid-inventory.md | Simulation Core, Content & Data Definitions |
| 4 | Player Character & Movement (incl. interaction framework, death/respawn) | Gameplay | MVP (M-1a → M1) | In Review (design-review 2026-10-16 NEEDS REVISION — 15 blocking; revision applied, awaiting re-review; UX Flag: interaction prompt, knocked-down screen) | design/gdd/player-character-movement.md | Simulation Core, Input & Controls, World Generation, Grid Inventory, Health & Damage, Networking & Session |
| 7 | Mail & Destinations | Gameplay | MVP (M-1a → M1) | In Review (/design-review 2026-10-16 NEEDS REVISION, 18 blockers — revision applied; re-review decides Approved; UX Flag: deliver prompt, pills/feed, Prep forecast, Payday roll-call; hand-backs to Grid Inventory, Run Structure, Throwing, World Generation) | design/gdd/mail-destinations.md | Content & Data Definitions, World Generation, Grid Inventory |
| 9 | Manual Delivery | Gameplay | MVP (M-1a) | In Review (/design-review NEEDS REVISION 2026-10-16, revision applied, awaiting re-review; UX Flag: deliver prompt branches; Quick Hands = spec 10 literal, no ratification pending; hand-backs to Player Character, Input & Controls, Mail & Destinations, Grid Inventory, HUD) | design/gdd/manual-delivery.md | Input & Controls, Player Character & Movement, Mail & Destinations, Grid Inventory |
| 10 | Throwing & Catching | Gameplay | MVP (M-1a; catch M0) | In Review (/design-review 2026-10-16 NEEDS REVISION, 13 blockers — revision applied, 1,449 → 1,307 lines; re-review decides Approved; CD-SENIOR D1 no-dice/spread knob retired + D2 exact preview colour ratified by the owner 2026-10-16; UX Flag + seed design/ux/throwing-catching-seed.md; hand-backs to Mail, Grid Inventory, Player Character, Input (D3 catch gate), Simulation Core) | design/gdd/throwing-catching.md | Player Character & Movement, Mail & Destinations, Grid Inventory, Belt Logistics, Health & Damage |
| 11 | Building System | Gameplay | MVP (stub M-1b → M2) | In Review (Provisional — GUARD overridden by owner instruction 2026-10-16; CD-GDD-ALIGN REVISED; /design-review 2026-10-16 NEEDS REVISION, 14 blockers revised, re-review pending; Place/PlaceLine not G9 commits; Input asked to pull minimal Build to M1; Deconstruct = hold RMB/LT in Build (closes Input OQ-IC-13); UX Flag: build-mode HUD/flow/pad line grammar; hand-backs OQ-BLD-1…12 to Input, Player Character, World Generation, Content & Data, Health & Damage, Defenses, Belt Logistics, Enemy AI, Vehicles, Economy/spec 11, HUD) | design/gdd/building-system.md | Content & Data Definitions, World Generation |
| 12 | Belt Logistics (belts, splitter/merger, Address Sorter, inserters, chests, depots) | Gameplay | MVP (one belt M-1b → M2) | Designed (Provisional) (GUARD overridden by owner instruction 2026-10-16; CD-GDD-ALIGN CONCERNS → REVISED, 2 CD rulings pending owner ratification; pending /design-review in a fresh session; 1,048 lines, 64 ACs; UX Flag: sorter panel, blocked badge, Push prompt; answers Grid OQ16, PC OQ25, OQ-BLD-7; hand-backs OQ-BL-H1…H14) | design/gdd/belt-logistics.md | Mail & Destinations, Grid Inventory, Building System; added by its GDD: Simulation Core, Content & Data Definitions, Player Character & Movement, World Generation (provisional), Networking & Session (M0) |
| 13 | Vehicles & NPC Drivers (incl. bike) | Gameplay | Vertical Slice (bike M1 → M2) | Not Started | — | Player Character & Movement, World Generation, Mail & Destinations, Grid Inventory, Building System, Belt Logistics, Health & Damage |
| 14 | Player Combat | Gameplay | MVP (stub M-1b → M3) | Not Started | — | Content & Data Definitions, Player Character & Movement, Health & Damage |
| 15 | Enemy AI & Waves | Gameplay | MVP (scripted pulse M-1b → M3) | Not Started | — | World Generation, Building System, Health & Damage, Player Character & Movement, Vehicles & NPC Drivers |
| 16 | Defenses & Construct Damage (HP, ruins, repair) | Gameplay | MVP (one wall M-1b → M3) | Not Started | — | Building System, Health & Damage, Enemy AI & Waves |
| 17 | Resource Harvesting | Gameplay | Vertical Slice (M1) | Not Started | — | Content & Data Definitions, World Generation, Grid Inventory, Player Character & Movement |
| 18 | Economy, Wallet & Shop | Economy | Vertical Slice (M1) | Not Started | — | Content & Data Definitions, Mail & Destinations, Grid Inventory, Building System, Resource Harvesting |
| 19 | Run Structure (shifts, quota, complaint meter, end conditions) | Progression | Vertical Slice (M1) | Not Started | — | Networking & Session, World Generation, Mail & Destinations, Enemy AI & Waves, Defenses & Construct Damage, Economy, Wallet & Shop |
| 20 | Perks & Perk Draft | Progression | Vertical Slice (M1 → M4) | Not Started | — | Content & Data Definitions, Economy, Wallet & Shop, Run Structure |
| 21 | Stamps & Run Events | Progression | Alpha (M4) | Not Started | — | Content & Data Definitions, Mail & Destinations, Enemy AI & Waves, Run Structure |
| 22 | Meta-Progression (Postal Rank, unlocks, seeds, cosmetics) | Progression | Alpha (M1 XP → M4) | Not Started | — | Run Structure, Perks & Perk Draft, Profile & Settings Persistence |
| 23 | Profile & Settings Persistence | Persistence | Vertical Slice (M1) | Not Started | — | Simulation Core, Content & Data Definitions |
| 24 | HUD & Notifications (comms split to #42 Comms, 2026-10-16) | UI | MVP (M-1a timer/wallet/prompt/pills/single toast → M0 feed → M3 → M4 live region) | In Review (/design-review MAJOR REVISION NEEDED 2026-10-16, revision applied — comms moved to design/gdd/comms.md, layout/art/a11y/ADR material parked in design/ux/hud-notifications-seed.md, 1,581 → 670 lines — awaiting re-review; UX Flag: /ux-design for HUD layout, notification stack, prompt + hold ring, compass, knocked-down block) | design/gdd/hud-notifications.md | Player Character & Movement, Mail & Destinations, Run Structure, Economy, Wallet & Shop, Defenses & Construct Damage, Proximity Voice, Input & Controls, Grid Inventory, Manual Delivery, Throwing & Catching, Networking & Session, Enemy AI & Waves, Health & Damage, Belt Logistics |
| 25 | Inventory & Build UI | UI | MVP (inventory panel M-1a → M2) | Not Started | — | Grid Inventory, Building System |
| 26 | Map & Address UI (incl. sorter filter UI, route console) | UI | Vertical Slice (M2) | Not Started | — | World Generation, Mail & Destinations, Belt Logistics, Vehicles & NPC Drivers |
| 27 | Menus & Run Flow Screens | UI | Vertical Slice (M0 lobby → M4) | Not Started | — | Networking & Session, Economy, Wallet & Shop, Run Structure, Perks & Perk Draft, Meta-Progression |
| 28 | Audio | Audio | Vertical Slice (M1) | Not Started | — | Networking & Session, Enemy AI & Waves, Run Structure |
| 29 | Proximity Voice | Audio | Vertical Slice (basic positional voice M0 → PA, indicators, PTT polish Alpha) | Not Started | — | Networking & Session, Audio |
| 30 | Onboarding & Tips | Meta | Alpha (M4) | Not Started | — | Run Structure, Stamps & Run Events, HUD & Notifications |
| 31 | Accessibility | Meta | Alpha (M4) | Not Started | — | Input & Controls, HUD & Notifications, Inventory & Build UI, Audio |
| 32 | Presentation & World Dressing (owns humour & feedback mandate) | Meta | Alpha (juice slice M-1a → M4) | Not Started | — | World Generation, Building System, Belt Logistics, Vehicles & NPC Drivers, Enemy AI & Waves |
| 33 | Dev Tooling | Meta | Vertical Slice (debug slice M0 → M4) | Not Started | — | Content & Data Definitions, World Generation, Run Structure |
| 35 | Sea Path (boats, ports, NPC captain) | Gameplay | Full Vision | Not Started | — | Vehicles & NPC Drivers, World Generation |
| 36 | Pipes | Gameplay | Full Vision | Not Started | — | Belt Logistics |
| 37 | Extra Enemies (Hog Rider, Balloon, Tank) | Gameplay | Full Vision | Not Started | — | Enemy AI & Waves |
| 38 | Extra Destinations & Cargo | Gameplay | Full Vision | Not Started | — | Mail & Destinations, Belt Logistics |
| 39 | Rail | Gameplay | Full Vision | Not Started | — | Vehicles & NPC Drivers, Building System |
| 40 | Free Play (open world, saves, story) | Progression | Full Vision | Not Started | — | Run Structure, Profile & Settings Persistence, World Generation |
| 41 | Dedicated Server | Core | Full Vision | Not Started | — | Networking & Session |
| 42 | Comms (text chat, pings, emotes) — split from HUD & Notifications 2026-10-16 | UI | Vertical Slice (quick ping + chat M0 → wheel, Say, emotes M1 → map pings M2) | Draft — split from HUD (design-review CD ruling); FROZEN behind the M-1a GUARD; no /design-review until two-friend results are recorded; 16 inherited review findings (CF-1…16) open | design/gdd/comms.md | HUD & Notifications, Input & Controls, Player Character & Movement, Networking & Session, Proximity Voice |

---

## Categories

| Category | Description | Typical Systems |
|----------|-------------|-----------------|
| **Core** | Foundation systems everything depends on | Simulation core, content definitions, input, health/damage, networking, world generation, grid inventory |
| **Gameplay** | The systems that make the game fun | Mail, delivery, throwing, building, belts, vehicles, combat, enemies, defenses, harvesting |
| **Progression** | How the player grows over time | Run structure, perks, stamps and events, meta-progression |
| **Economy** | Resource creation and consumption | Wallet, income/sinks, shop and blueprints |
| **Persistence** | Save state and continuity | Profile, settings, run snapshot |
| **UI** | Player-facing information displays | HUD, inventory/build UI, map, menus |
| **Audio** | Sound and music systems | SFX/music, proximity voice |
| **Meta** | Systems outside the core game loop | Onboarding, accessibility, presentation, dev tooling |

---

## Priority Tiers

| Tier | Definition | Target Milestone | Design Urgency |
|------|------------|------------------|----------------|
| **MVP** | Required for the core loop to function. Without these, you can't test "is this fun?" | M-1a greybox (delivery + throwing), then M-1b (one belt/sorter, one raid pulse) | Design FIRST — but prototype M-1a before GDDs |
| **Vertical Slice** | Required for one complete run with friends. | M0 multiplayer → M1 seeded run → M2 automation. **v1-lite ship point after M2.** | Design SECOND, just-in-time one milestone ahead |
| **Alpha** | All v1 features present in rough form. | M3 combat → M4 rogue-lite wrapper and polish | Design THIRD |
| **Full Vision** | Deferred by spec 13 §2.2; designed in ch. 01–12, not built in v1. | Post-v1 | Design as needed |

> **Producer Note** (PR-SCOPE: OPTIMISTIC, 2026-10-15) — adjustments applied: M-1 split into M-1a/M-1b; six MVP systems are explicit greybox stubs (Health & Damage, Player Combat, Enemy AI & Waves, Defenses, Building, Inventory & Build UI); Simulation Core and Content & Data are throwaway until M0; Proximity Voice and Presentation moved VS → Alpha; a debug-tooling slice moved Alpha → VS; v1-lite (post-M2) recorded as the first shippable point. NOT ASSESSED: sprint velocity (no history) and art/audio workload (no art bible). Time-box a Steam lobby/relay/voice-from-C# spike (~1 weekend) at the start of M0.

> **Creative Director Note** (CD-SYSTEMS: CONCERNS, 2026-10-15) — revisions applied:
> - **(Vertical Slice)** Proximity Voice split: basic positional voice (open mic, 30 m falloff, same-vehicle full volume) returns to M0 on the Steam spike, serving Fellowship (aesthetic #1) at v1-lite; PA, speaking indicators and push-to-talk polish stay in Alpha.
> - **(MVP)** Humour has an owner: Presentation & World Dressing carries the humour-and-feedback mandate, with a minimal juice slice (delivery/misdelivery stings, hit reactions) in M-1a so the "want to play again" gate is not failed by silent capsule art. The GDDs for Manual Delivery, Throwing & Catching, Mail & Destinations and Run Structure (Payday summary, deadpan postal bureaucracy) each carry a **Feel & Comedy Feedback** subsection under Player Fantasy.
> - **(Vertical Slice)** Gate additions beyond spec 13 §6: **M0** also requires "the 4-player group spontaneously throws to and catches from each other and wants another session"; **M2** also requires "a player driving a truck beats the NPC driver on the same route" (pillar 3).
> - **(Vertical Slice)** Text chat, map pings and emotes are owned by Comms (#42, split from HUD & Notifications 2026-10-16 by design-review CD ruling); pings and chat ship in M0. The bike is a bike-only M1 slice of Vehicles & NPC Drivers.
> - **(Vertical Slice)** Resource Harvesting sits closest to the "not a crafting sim" anti-pillar: keep it S-sized, and its GDD must justify each material against Building costs; first candidate to simplify if playtests read it as a chore.
> - NOT ASSESSED by the CD: tone execution of the humour (no art bible or audio direction yet; `/art-bible` is open in spec 13 §7) and the enemy fiction (why they raid a post office).

---

## Dependency Map

Rule adopted at the TD gate: **Run Structure orchestrates — it calls down; lower systems report up through events they define.** Every gameplay GDD carries a required **Network** section (its replicated state and JoinState contribution), implemented in a Godot-side adapter so the Sim assembly stays free of Godot references.

### Foundation Layer (no dependencies)

1. Simulation Core — tick, RNG streams, event bus, entity ids, integer money (spec 07 §1, §4).
2. Input & Controls — action map, KBM + full gamepad (spec 09 §4).
3. Content & Data Definitions — depends on: Simulation Core. Owns typed defs, validation, and the `StatSheet` modifier stack; perks and stamps only push modifiers.

### Core Layer (depends on foundation)

1. Health & Damage — depends on: Simulation Core, Content & Data. Owns HP components, damage events, death event, resolve order.
2. Grid Inventory & Containers — depends on: Simulation Core, Content & Data. Owns `WorldItem`.
3. World Generation — depends on: Simulation Core, Content & Data. Owns the runtime tile grid and routing graph; hands Networking serialisable tables (Networking does not depend on it).
4. Networking & Session — depends on: Simulation Core, Content & Data. Owns transport, lobby, join-in-progress, JoinState envelope, RPC/synchronizer conventions.
5. Profile & Settings Persistence — depends on: Simulation Core, Content & Data.

### Feature Layer (depends on core)

1. Player Character & Movement — depends on: Simulation Core, Input, World Generation, Grid Inventory, Health & Damage, Networking. Owns interaction framework, death/respawn/death bag.
2. Mail & Destinations — depends on: Content & Data, World Generation, Grid Inventory.
3. Building System — depends on: Content & Data, World Generation. Declares HP fields on build defs only.
4. Resource Harvesting — depends on: Content & Data, World Generation, Grid Inventory, Player Character.
5. Manual Delivery — depends on: Input, Player Character, Mail & Destinations, Grid Inventory.
6. Belt Logistics — depends on: Mail & Destinations, Grid Inventory, Building.
7. Throwing & Catching — depends on: Player Character, Mail & Destinations, Grid Inventory, Belt Logistics, Health & Damage. Creates WorldItems via Grid Inventory's API.
8. Player Combat — depends on: Content & Data, Player Character, Health & Damage.
9. Vehicles & NPC Drivers — depends on: Player Character, World Generation, Mail & Destinations, Grid Inventory, Building, Belt Logistics, Health & Damage.
10. Enemy AI & Waves — depends on: World Generation, Building, Health & Damage, Player Character, Vehicles. Owns wave composition/spawning and the wave scheduler (deviation from spec 07 §4 `Run.Step`); consumes `Waves.StartRaid(budget)` from Run Structure, emits `RaidCleared`.
11. Defenses & Construct Damage — depends on: Building, Health & Damage, Enemy AI & Waves. Owns construct HP, ruins, repair; emits `PODestroyed`.
12. Economy, Wallet & Shop — depends on: Content & Data, Mail & Destinations, Grid Inventory, Building, Harvesting. Exposes wallet reads, `PaymentEvent`, `Shop.SetOpen(bool)`.
13. Run Structure — depends on: Networking, World Generation, Mail & Destinations, Enemy AI & Waves, Defenses, Economy. Orchestrator; owns phases, quota, complaint meter and its decay (deviation from spec 07 §4 `Economy.Flush`), computes raid budget, pushes district unlocks to World Generation and Mail, spawn profiles to Mail, reads the host's unlocked set from Persistence and passes it to Economy, Perks, Stamps.
14. Perks & Perk Draft — depends on: Content & Data, Economy, Run Structure. Behaviour-hook perks (e.g. Cannon Arm ×1.1 pay) are implemented by the owning system.
15. Stamps & Run Events — depends on: Content & Data, Mail & Destinations, Enemy AI & Waves, Run Structure.
16. Meta-Progression — depends on: Run Structure, Perks, Persistence. Writes unlocked set to Persistence; nothing in-run depends on it.

### Presentation Layer (depends on features)

1. HUD & Notifications — depends on: Player Character, Mail, Run Structure, Economy, Defenses, Proximity Voice; added by its GDD (2026-10-16): Input & Controls, Grid Inventory, Manual Delivery, Throwing & Catching, Networking & Session, Enemy AI & Waves, Health & Damage, Belt Logistics. Presentation side only (plain-C# Ui.Core + Godot view). Replicates nothing since the 2026-10-16 comms split; Comms (#42) depends on HUD (marker layer, compass, pipeline), Input & Controls, Player Character, Networking & Session, Proximity Voice.
2. Inventory & Build UI — depends on: Grid Inventory, Building.
3. Map & Address UI — depends on: World Generation, Mail, Belt Logistics, Vehicles.
4. Menus & Run Flow Screens — depends on: Networking, Economy, Run Structure, Perks, Meta-Progression.
5. Audio — depends on: Networking, Enemy AI & Waves, Run Structure.
6. Proximity Voice — depends on: Networking, Audio.
7. Presentation & World Dressing — depends on: World Generation, Building, Belt Logistics, Vehicles, Enemy AI & Waves.

### Polish Layer (depends on everything)

1. Onboarding & Tips — depends on: Run Structure, Stamps & Run Events, HUD.
2. Accessibility — depends on: Input, HUD, Inventory & Build UI, Audio.
3. Dev Tooling — depends on: Content & Data, World Generation, Run Structure.

### Full Vision (deferred)

Sea Path, Pipes, Extra Enemies, Extra Destinations & Cargo, Rail, Free Play, Dedicated Server — each depends on its v1 parent (see enumeration).

**Bottlenecks (high risk):** Simulation Core and Content & Data (everything), Grid Inventory (10+ dependents), World Generation (runtime grid used by movement, mail, building, vehicles, enemies, map), Networking & Session (every gameplay Network section), Health & Damage (6 dependents), Run Structure (orchestrator).
**Leaf nodes (design late):** Dev Tooling, Accessibility, Onboarding, Proximity Voice, Presentation & World Dressing, Meta-Progression.

> **Technical Director Note** (TD-SYSTEM-BOUNDARY: CONCERNS, 2026-10-15) — all ten items applied in this map: Health & Damage added; interaction framework → Player Character; orchestrator rule for cycles; StatSheet in Content & Data; Network section per GDD; unlocks via Persistence; WorldItem in Grid Inventory; missing edges added; leaf list corrected. NOT ASSESSED by the TD: per-system Sim/Presentation scope (no index existed yet) — each GDD's Dependencies section must state which side of the split it lives on. Sequencing advice: write GDDs for Simulation Core, Content & Data, Grid Inventory, Health & Damage and Player Character first, since the greybox depends on their contracts most; treat the rest as provisional until the M-1 gate.

---

## Recommended Design Order

Prototype M-1a (`/prototype`) **before** these GDDs; then design just-in-time one milestone ahead. Order within a milestone follows dependencies.

| Order | System | Priority | Layer | Agent(s) | Est. Effort |
|-------|--------|----------|-------|----------|-------------|
| 1 | Grid Inventory & Containers | MVP | Core | game-designer, systems-designer | L |
| 2 | Player Character & Movement | MVP | Feature | game-designer | M |
| 3 | Mail & Destinations | MVP | Feature | game-designer, systems-designer | M |
| 4 | Throwing & Catching | MVP | Feature | game-designer | M |
| 5 | Manual Delivery | MVP | Feature | game-designer, ux-designer | S |
| 6 | Input & Controls | MVP | Foundation | ux-designer | S |
| 7 | HUD & Notifications (comms → #42, frozen) | MVP | Presentation | ux-designer | M (design-review 2026-10-16: S was too small; M after the comms split) |
| 8 | Simulation Core | MVP (M0) | Foundation | technical-director, lead-programmer | M |
| 9 | Content & Data Definitions | MVP (M0) | Foundation | technical-director, systems-designer | M |
| 10 | Health & Damage | MVP | Core | game-designer, systems-designer | S |
| 11 | Building System | MVP | Feature | game-designer | M |
| 12 | Belt Logistics | MVP | Feature | game-designer, systems-designer | L |
| 13 | Player Combat | MVP | Feature | game-designer | S |
| 14 | Enemy AI & Waves | MVP | Feature | game-designer, ai-programmer | M |
| 15 | Defenses & Construct Damage | MVP | Feature | game-designer | S |
| 16 | Inventory & Build UI | MVP | Presentation | ux-designer | M |
| 17 | Networking & Session | Vertical Slice | Core | technical-director, network-programmer | L |
| 18 | World Generation | Vertical Slice | Core | game-designer, level-designer | L |
| 19 | Profile & Settings Persistence | Vertical Slice | Core | systems-designer | S |
| 20 | Resource Harvesting | Vertical Slice | Feature | game-designer | S |
| 21 | Economy, Wallet & Shop | Vertical Slice | Feature | economy-designer | M |
| 22 | Run Structure | Vertical Slice | Feature | game-designer, systems-designer | L |
| 23 | Perks & Perk Draft | Vertical Slice | Feature | systems-designer | M |
| 24 | Vehicles & NPC Drivers | Vertical Slice | Feature | game-designer, ai-programmer | L |
| 25 | Map & Address UI | Vertical Slice | Presentation | ux-designer | M |
| 26 | Menus & Run Flow Screens | Vertical Slice | Presentation | ux-designer | M |
| 27 | Audio | Vertical Slice | Presentation | audio-director | S |
| 28 | Proximity Voice (basic slice) | Vertical Slice | Presentation | audio-director, network-programmer | M |
| 29 | Dev Tooling | Vertical Slice | Polish | tools-programmer | M |
| 30 | Stamps & Run Events | Alpha | Feature | systems-designer | S |
| 31 | Meta-Progression | Alpha | Feature | game-designer | S |
| 32 | Presentation & World Dressing (humour & feedback) | Alpha | Presentation | art-director, technical-artist | M |
| 33 | Onboarding & Tips | Alpha | Polish | ux-designer | S |
| 34 | Accessibility | Alpha | Polish | accessibility-specialist | S |
| 35–41 | Full Vision systems | Full Vision | — | as needed | — |

Effort: S = 1 session, M = 2–3, L = 4+. Most systems are **retrofits** of existing spec chapters (`/design-system retrofit design/spec/<chapter>.md`), which should run below these estimates.

---

## Circular Dependencies

Two found in the first draft; both resolved by the orchestrator rule (Run Structure calls down, lower systems report up via events they define):

- **Run Structure ↔ Enemy AI & Waves** — Run Structure computes the raid budget (shift, complaint, stamps, player count) and calls `Waves.StartRaid(budget)`; Enemy AI & Waves owns composition and emits `RaidCleared`; `PODestroyed` comes from Defenses (edge Run Structure → Defenses added). Enemy AI & Waves does not depend on Run Structure.
- **Run Structure ↔ Economy** — Run Structure calls `Shop.SetOpen(bool)` and reads the wallet and `PaymentEvent`; Economy does not depend on Run Structure.

Latent cycles removed the same way: Mail (phase-dependent spawn, district unlock), World Generation (district roads added on unlock), Stamps (Rush Hour timing), and Meta-Progression (unlock filtering via Persistence at run start). A cycle check over this map should come back clean — verify when the first GDDs state their dependencies.

---

## High-Risk Systems

| System | Risk Type | Risk Description | Mitigation |
|--------|-----------|-----------------|------------|
| Delivery + Throwing (the hypothesis) | Design | Hand delivery and throwing may not be fun on their own | M-1a greybox gate: owner + two friends want to play again (spec 13 §6) |
| Grid Inventory & Containers | Scope / Design | 10+ dependents; rules for placement, stacking, rotate, sort, throw | Design first; keep minimal for M-1a |
| Belt Logistics | Technical / Design | Server lane events + client visual sim + resync; up to 5000 items in budget (spec 07 §8) | M-1b one belt; full design before M2; perf target in GDD |
| Networking & Session | Technical | Steam lobby/relay/voice from C# unconfirmed; GDScript transport fallback (spec 13 §3) | Time-boxed spike (~1 weekend) at start of M0; M0 gate: no dup/loss over 10 min |
| World Generation | Technical / Design | Island quality and validation; routing graph feeds five systems | SeedViewer tool early; M1 gate |
| Run Structure | Design | Orchestrator; quota and complaint curves unvalidated (solo 20–30 % / 4-player 40–60 % targets) | BalanceSim tool; tune in data only (M4 gate) |
| Health & Damage | Design | New owner for five damage sources; easy to leave gaps | Write GDD before Combat/Enemy/Defenses |
| Resource Harvesting | Design | Closest to the "not a crafting sim" anti-pillar; may read as a chore | Keep S-sized; justify each material against Building costs; first to simplify |
| Humour / tone | Design | No art bible or audio direction; comedy delivery unspecified | Presentation owns the mandate; Feel & Comedy Feedback subsections; `/art-bible` before M3 |
| Scope overall | Scope | Producer estimate ≈ 1,100 h implementation vs solo evenings/weekends | v1-lite after M2 as ship point; just-in-time GDDs; re-run PR-SCOPE after M-1a |

---

## Progress Tracker

| Metric | Count |
|--------|-------|
| Total systems identified | 42 (35 v1 + 7 deferred; #42 Comms split from HUD 2026-10-16) |
| Design docs started | 14 |
| Design docs reviewed | 4 |
| Design docs approved | 0 |
| MVP systems designed | 12/16 (Belt Logistics — Designed 2026-10-16, Provisional (GUARD overridden by owner instruction), CD-GDD-ALIGN REVISED, pending /design-review; Building System — Designed 2026-10-16, Provisional (GUARD overridden by owner instruction), CD-GDD-ALIGN REVISED, pending /design-review; Health & Damage — Designed 2026-10-16, Provisional (GUARD overridden by owner instruction), CD-GDD-ALIGN REVISED, pending /design-review; Content & Data Definitions — Designed 2026-10-16, Provisional (GUARD overridden by owner instruction), CD-GDD-ALIGN REVISED, pending /design-review; Simulation Core — Designed 2026-10-16, Provisional (GUARD overridden by owner instruction), CD-GDD-ALIGN REVISED, pending /design-review; Grid Inventory — In Review: re-revised 2026-10-16 after re-review, awaiting third review; Input & Controls — In Review: /design-review NEEDS REVISION 2026-10-16, revision applied, awaiting re-review; Player Character & Movement — In Review: /design-review NEEDS REVISION 2026-10-16, revision applied, awaiting re-review; Mail & Destinations — Designed 2026-10-16, CD-GDD-ALIGN REVISED, pending /design-review; Manual Delivery — In Review: /design-review NEEDS REVISION 2026-10-16, revision applied, awaiting re-review); HUD & Notifications — In Review: /design-review MAJOR REVISION NEEDED 2026-10-16, revision applied (comms split to #42), awaiting re-review; Throwing & Catching — Designed 2026-10-16, CD-GDD-ALIGN REVISED, pending /design-review) |
| Vertical Slice systems designed | 0/15 |

---

## Next Steps

- [x] CD-SYSTEMS creative-director review of this index (CONCERNS, applied 2026-10-15)
- [x] `/prototype` the M-1a greybox (delivery + throwing) before GDDs — done 2026-10-15 (PROCEED, preference signal only)
- [ ] **GUARD (accepted risk, gate re-run 2026-10-15, autonomous mode):** do not run `/design-system` on any system outside the M-1a set — Grid Inventory, Player Character & Movement, Mail & Destinations, Throwing & Catching, Manual Delivery, Input & Controls, HUD & Notifications — until the two-friend greybox results are recorded in `prototypes/m1a-delivery-throwing-concept/REPORT.md`. Comms (#42, split from HUD 2026-10-16) is outside the M-1a set and FROZEN behind this GUARD. *Overridden 2026-10-16 by owner instruction ("continue the rest of the planning without my input") for Simulation Core (#1) — GDD marked Provisional; logged in production/session-logs/decision-log.md.*
- [ ] Design MVP-tier systems in order above (`/design-system retrofit design/spec/03-mail-inventory-economy.md` etc.)
- [ ] Run `/design-review` on each completed GDD; `/consistency-check` after each
- [ ] Re-run PR-SCOPE (`/scope-check`) after M-1a with actual hours
- [ ] Run `/gate-check technical-setup` when MVP systems are designed
