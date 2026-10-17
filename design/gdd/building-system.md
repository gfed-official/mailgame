# Building System

> **Provisional**: designed before the M-1a two-friend greybox (systems-index GUARD overridden by owner instruction 2026-10-16); re-validate after the greybox.

> **Status**: In Review (Provisional) — /design-review 2026-10-16 NEEDS REVISION; revision applied, re-review pending
> **Author**: owner + agents (game-designer, systems-designer, level-designer, ux-designer, network-programmer, art-director, qa-lead, creative-director)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-10-16 (CONCERNS: 4 text fixes applied — Prep anchor moment, refund-preview honesty, remover named in the feed (OQ-BLD-11 ruling), Delivery fantasy test; ratified by the owner 2026-10-16: no terrain edits vs spec 02 §4, OQ-BLD-11, 15 % Delivery threshold, OQ-BLD-12; OQ-BLD-14 spawn pads stays an open scope question)
> **Implements Pillar**: 3 (hands beat machines, until they don't — instant placement, machines that run unattended), 1 (the mail must flow), 2 (toy-set surface, routing depth); constraint: 5 (one carrier can build alone)

## Summary

Building turns materials into constructs: belts, sorters, inserters, storage, vehicle depots and walls on the 2 m tile grid. It owns the construct registry, the placement rules, the atomic material charge (pockets, plus the PO Depot near the Post Office), deconstruction with salvage (full in Prep, half in Delivery, a 3 s undo grace for the placer), and build mode, where a green or red ghost shows exactly what will land. Placement is instant with no crafting or timers. It is the gateway to the whole automation stack and the defenses, so the crew can set up a shift that runs while they run.

> **Quick reference** — Layer: `Feature` · Priority: `MVP (stub M-1b → M2)` · Key deps: `Simulation Core, Content & Data Definitions, World Generation (provisional), Grid Inventory, Health & Damage, Input & Controls, Player Character; consumers Belt Logistics, Defenses, Vehicles, Enemy AI, Economy, Run Structure, Inventory & Build UI, HUD`

## Overview

Building is how the crew turns materials into the machines and walls that move and protect the mail. It owns the **construct registry** (every placed belt, sorter, inserter, chest, depot, vehicle depot, wall, gate, turret and alarm post, plus the pre-built Post Office), the **2 m tile placement rules** that decide where a construct may go, the **atomic material charge** taken from the placer's pockets and, near the Post Office, from the PO Depot, **deconstruction and salvage**, and **build mode**, the player state in which a ghost shows what will land before it lands. Placement is instant. There are no build timers, no crafting and no upkeep (spec 08 §2.7: `craftSeconds = 0`; anti-pillar "not a crafting sim").

Players use it directly: press B / LB, pick a piece, aim a green or red ghost at a tile within reach, and click to place, or hold and drag to lay a line of belts or walls. Holding RMB / LT on a construct takes it apart and refunds its materials: all of them in Prep, half during Delivery. Without this system there is no automation stack. Pillar 3's late game ("until they don't") and the spec 11 shift-2 "you must start building" moment both need it.

*Design boundary.* Building decides **whether, where and at what cost** a construct exists. What a construct **does** belongs to its behaviour owner: Belt Logistics (belts, splitter, merger, sorter, inserters, chest, depot), Vehicles (vehicle depot) and Defenses (walls, gate, turret, alarm; HP after placement, ruins, repair, rebuild). Health & Damage owns hit points, and Building declares only the HP fields on its defs. World Generation owns the tile grid, and Economy owns blueprint purchase. *Framing: [C] Both. ADR reference: none. Algorithms (occupancy grid, line scan, validator layout) → ADR candidates (Open Questions).*

## Player Fantasy

**"It Runs While You Run"** (creative-director, recommended spine; Direct). You build so your hands are free. Drag a belt from the Intake toward the district edge and let go: the line drops into place in a ripple. The first Kraft parcel rolls out while you are already sprinting the other way with your arms full, toward the house no belt reaches. The machine is worth having because you are *not* standing next to it, which is Pillar 3's ratified reading: *automation must never remove the player's reason to move*.

The **surface** is a toy set on the lawn: chunky Postal Blue pieces on Charcoal skirts click onto the grid. The ghost says yes or no before you commit, and what lands is exactly what the ghost showed. The **Delivery beat** is a pit stop: the clock is running, the sorter is in the wrong place, and you tear it down for half its materials and put it back right in seconds. Changing your mind costs materials, never time spent waiting.

**Anchor moment.** Last seconds of Prep: hold, drag 12 tiles from the Intake, watch the ghost go green tile by tile, release. The pieces pop down. When Delivery opens, the first parcel rolls onto them while you are already out the door with your arms full.

**Pillars.** 1: every piece exists to move or protect mail. 2: goofy toy set on the surface, real routing depth underneath, and the ghost never lies. 3: the strong manual act is instant placement; the payoff is a machine that runs unattended; building far from the PO means carrying materials there, so building is itself a reason to move. 4: ruins and rebuilds keep each run's layout temporary. 5: one carrier can lay a working line alone. Anti-pillar: no recipes to craft, no timers, no upkeep.

### Feel & Comedy Feedback

| Moment | What the player sees / hears | Rule that makes it honest |
|---|---|---|
| Ghost hover | Go Green; Stamp Red with an X and a reason pill; or Chalk White with red "missing" chips | The client runs the host's own legality function (B4), so green means it will land unless a teammate beats you to the tile |
| Place | The piece drops with a squash, a rubber-stamp *thunk* and a dust puff off the skirt | Instant (B6). Materials leave the panel or the PO Depot on the same tick (B5) |
| Drag line | Ghost tiles extend with a running cost tally, then pop down in a ripple | The host places exactly the longest valid, affordable prefix the ghost showed (B7, F2) |
| Undo (3 s) | Your own fresh piece springs back into your bag with a rewind *bwip* | Full refund only for the placer, only undamaged, only inside the window (F4) |
| Deconstruct in Delivery | Hold 0.5 s; the piece unscrews and half the materials hop to you | The refund chips show the F3 count at the half ratio before the hold completes; the team rounding bank can add one unit, never take one away |
| A teammate takes yours | The piece lifts and vanishes with a pop; the event carries who did it | Anyone may deconstruct anything (spec 13 §4.3). When the remover is not the placer, HUD names the remover, never the owner, in one grouped feed line ("Dana took apart Belt ×6", OQ-BLD-11). Visibility, not locks |

Tone risks (creative-director): building as a chore (mitigated by drag lines, pipette, no upkeep; fantasy test in Game Feel); griefing by deconstruction (friends-only by design; the HUD names the remover, OQ-BLD-11; the host can kick); the half refund reading as punishment (the lost half is cartoon smoke, never Stamp Red).

## Detailed Design

### Core Rules

**B1 — Construct registry [M-1b].** Building owns one record per construct: `id` (EntityId class `construct` 0x03, Simulation Core), `defIndex`, `anchor` (min-corner tile), `rot` ∈ 0–3, `ownerSlot` (the placing player's slot, or System; **stats only**, never a permission), `placedTick` and `flags` (`ruin`, written only by Defenses through B13). Ids are never reused within a run. Behaviour state (lane items, sorter filters, routes, turret targets) is the behaviour owner's, keyed by the construct id. The pre-built **Post Office** (6×6, `deconstructable: false`) is a construct. Pre-built constructs (the PO; the M-1b authored street set) are created by System-actor `Spawn` ops drained in tick 0, phase 0, which skip cost, phase and actor checks but not tile legality (a Debug assert).

**B2 — Catalogue (v1, values owned here; behaviour params owned by each behaviour owner).** Source: spec 10 §2 minus spec 13 §2.2 deferrals (elevated belt, ramp, pipes, pump, pier, port, rowboat, spike strip). `class` drives B4's street and reservation checks; `solid` drives the body check.

| id | Footprint | HP | Cost | Blueprint | class / solid / drag | Category | First |
|---|---|---|---|---|---|---|---|
| belt_mk1 | 1 | 80 | 1 Plank, 1 Iron Ingot | — | transport / no / yes | Transport | M-1b |
| belt_mk2 | 1 | 100 | 1 Plank, 2 Iron Ingot | bp_fast_belts | transport / no / yes | Transport | M2 |
| splitter, merger | 1 | 150 | 2 Iron Ingot, 1 Plank | — | transport / no / no | Transport | M2 |
| address_sorter_mk1 | 2×2 | 500 | 4 Iron Ingot, 6 Stone | bp_sorting | solid-building / yes / no | Sorting | M-1b |
| inserter | 1 | 120 | 2 Iron Ingot, 1 Plank | — | transport / no / no | Sorting | M-1b |
| inserter_long | 1 | 120 | 3 Iron Ingot, 1 Plank | bp_sorting | transport / no / no | Sorting | M2 |
| chest | 1 | 200 | 4 Log | — | solid-building / yes / no | Storage | M1 |
| depot | 2×2 | 800 | 8 Stone, 4 Iron Ingot, 4 Plank | — | solid-building / yes / no | Storage | M2 |
| vehicle_depot | 3×3 | 1000 | 12 Plank, 8 Stone, 4 Iron Ingot | bp_truck | solid-building / yes / no | Vehicles | M2 |
| wall_wood | 1 | 300 | 3 Log | — | defense / yes / yes | Defense | M-1b |
| wall_stone | 1 | 800 | 4 Stone | bp_defense | defense / yes / yes | Defense | M3 |
| gate | 1 | 500 | 4 Log, 1 Iron Ingot | — | defense / yes / no | Defense | M3 |
| turret | 1 | 400 | 6 Iron Ingot, 2 Stone | bp_turret | defense / yes / no | Defense | M3 |
| alarm_post | 1 | 100 | 2 Log, 1 Iron Ingot | — | defense / yes / no | Defense | M3 |
| post_office | 6×6 | 3000 | — (pre-built) | — | building / yes / no | — | M-1b |

Every v1 footprint is square, so rotation never changes the occupied set. `Footprint(def, rot)` still swaps W and H on odd rotations, for later non-square defs. **HP fields on build defs only:** `hp` (base) and `armorClass`. Everything else about HP is Health & Damage's or Defenses'.

**B3 — Grid, anchor and facing [M-1b].** Constructs snap to World Generation's 2 m grid. The anchor is the footprint's min-corner tile. Rotation 0 puts the front at −Z (map north), and rotations step clockwise by 90°. For belts, splitters and inserters the front is the output; an inserter picks from the tile behind it and drops on the tile in front. Belt corner shapes are derived by Belt Logistics from neighbours, so Building stores only `rot`. **No terrain edits:** a construct sits at **base height H = the highest tile-centre height in its footprint**, with its Charcoal skirt extending down to grade (F6). This replaces spec 02 §4's terrain-flattening delta.

**B4 — Placement legality [M-1b core; M2 complete].** One pure function, `TileLegality`, runs identically on the host (authoritative) and on the client (the ghost, advisory, over its replicated mirror). It evaluates **check by check** (each check across every footprint tile before the next check) and returns the **first** failing reason with every tile that fails it. Order:

1. **Actor** (host only; the client predicts from local state): `not_alive` (knocked down or Frozen); `swimming`; `mounted`; `wrong_phase` (B10); `locked` (B9); `cap` (registry at `constructCap`).
2. **Per footprint tile**, in this order:
   - `out_of_bounds`;
   - `water` (any water tile; v1 has no piers);
   - `street` (TileType Street or Path; a Bridge tile refuses every class with this reason and is never `water`), unless the def is a belt and the Street or Path tile is a legal crossing: straight, axis perpendicular to the tile's `StreetAxis`, not an intersection tile (spec 13: belts cross streets at ground level; no other construct, walls and gates included, sits on a street or path tile);
   - `unbuildable` (World Generation flag with cause: cliff, lot, house, mailbox, PO);
   - `reserved`: PO footprint plus a 1-tile apron, and the Spawn pad ring (tiles within 2.0 m of the pad centre) refuse every class. The Intake outfeed lane (World Generation's `outfeedLaneTiles`) and every mailbox `approachTile` accept the `transport` class only, and this exemption overrides the apron;
   - `node` (an undepleted tree, boulder or ore vein);
   - `occupied` (a construct or ruin);
   - `too_steep` (F6);
   - `uneven` (F6, footprint height span).
3. **Body** (solid defs only; belts are walkable and exempt): `blocked_by_body` if the footprint box overlaps a living or Frozen carrier capsule, a goon or a vehicle, using the host's latest transform sample shrunk by `bodyEpsilonMm` (resolves Player Character OQ #18). If someone walks in after placement, the construct wins, and Player Character's depenetration moves them.
4. **Behaviour veto**: every `IPlacementRule` registered by behaviour owners (for example Belt Logistics' one-automated-feeder-per-mailbox rule, Mail & Destinations' `maxAutomatedFeeders`; Vehicles' keep-clear apron). Each returns its own reason key.
5. **Funds** (`insufficient{items}`), last, so a player is never told "Missing 2 Iron" about a tile that could not take the piece anyway.

**B5 — Material sources [M1].** A charge draws from the **panel** (hotbar, inventory, backpack; Grid Inventory R17 order). The **PO Depot** is added when F5 says the placer is eligible: within `poDepotReachMm` of the PO anchor, or anywhere while RuleFlag `SharedBuild` is on. Refined materials come from the shop and raw ones from harvesting; Building never sells, crafts or converts. Before M1 (no harvesting, no shop) only `Spawn` creates constructs, plus a Debug-only `build.costsEnabled = false` free-place path for greybox testing.

**B6 — Instant atomic placement [M1].** A `Place` request is validated by B4. On success the host, inside one phase-0 handler: computes F1 for every recipe input; runs `CountItem` over the eligible sources for each; if any input is short, replies `insufficient{item, missing}[]` and **consumes nothing**; otherwise calls `ConsumeItems` per input (inputs are distinct ids, so consumption cannot fail; asserted), allocates the id, writes the record and occupancy, registers a solid def's static AABB with Player Character (min(h) to H + `def.heightMm`), calls `Health.Register(id, Construct, def.hp, def.armorClass, 1000, ConstructHpMultPm)`, notifies the behaviour owner (`OnPlaced`) and raises `ConstructPlaced`. No reservations are held between ticks; each op sees the stock left by every op drained before it.

**B7 — Drag lines [M2].** Defs with `drag: yes` accept `PlaceLine{start, end}`. The client projects the drag onto its dominant axis (|Δx| = |Δz| → the x axis) and sends a straight line; a request whose `start` and `end` share neither row nor column, or with N = |Δ| + 1 > `maxLineTiles`, is `bad_request` with nothing placed. Each tile's `rot` is the drag direction. The host places the **longest valid, affordable prefix** (F2): the scan stops at the first tile failing B4, so a belt never has a silent gap. It charges only what it placed and replies `placed k of N` with `stopReason ∈ {None, Invalid(reason), Funds, Cap}`. `k = 0` is a rejection with nothing consumed. A one-tile drag is sent as a `Place` with the current `rot` (a `PlaceLine` with N = 1 is `bad_request`). Line tiles see the registry plus the tiles already accepted earlier in the same scan, including `IPlacementRule` vetoes.

**B8 — Deconstruct [M1].** **Binding (final; closes Input & Controls OQ-IC-13):** Deconstruct is a **Build-context command on hold RMB / hold LT**. It is not an `IInteractable` verb, and Interact is not live in Build. The target is the construct (or ruin) whose footprint contains the **build-cursor tile**. The construct id is latched at press, and moving the cursor off it cancels the hold. Hold length is Input & Controls' `holdEff` with `holdBase` 0.5 s. Deconstruct is a commit (Input G9): one `Deconstruct{constructId, targetRuin}` request on completion, pending until the reply. On the host, inside one handler:
   1. validate: the construct exists (`gone` otherwise), `deconstructable` (`not_deconstructable`: the PO), actor alive, not swimming, phase (B10); the ruin flag equals the one the client targeted (`target_changed` otherwise, in either direction); then **snapshot** `{defIndex, ownerSlot, placedTick, ruin, hp == maxHp}` (Health read, OQ-BLD-5) and compute F4's `ratioPm` — steps 2–6 use only this snapshot;
   2. collect everything the construct holds — Grid container entries, plus whatever its behaviour owner hands over in `OnRemoving` (belt lane items, an inserter's held unit, the sorter buffer) — into **one** Grid Inventory R13 spill bag at the footprint centre (no bag if empty; any open session on the container is closed);
   3. `Health.Unregister(id)`;
   4. free the occupancy, remove the Player Character AABB and delete the record;
   5. raise `ConstructRemoved{reason: Deconstruct | RuinCleared, bySlot}`;
   6. refund by F3 with the snapshot ratio via one `Grant(deconstructor, entry, SpillWorld)` per item with `refund_i > 0` (a full panel overflows into one heap at the deconstructor's feet; never into the PO Depot, so nothing teleports).
   Spill comes before refund so conservation is checkable at every step.

**B9 — Availability and blueprints [M2].** A recipe is available iff its `blueprint` is absent or owned by the team (Economy's `IBlueprintQuery.Owns`), and `unlockShift ≤` the current shift (Run Structure). A blueprint bought earlier in the same tick (lower opSeq) counts. Once bought, every player can build the recipe (spec 04 §1.3). Locked recipes stay selectable so players can plan; the ghost shows the locked state and the placement is refused with `locked`.

**B10 — Phase eligibility [M1].** Place, PlaceLine and Deconstruct are legal in **Prep** and **Delivery**. The Raid window overlaps Delivery (spec 01) and uses Delivery's rules. In every other phase (Generating, Payday, Draft, Results, Lobby) the reply is `wrong_phase`. The phase used is the one visible in phase 0 of the apply tick, never one latched at press.

**B11 — Build mode (client state) [M1 minimal: wall + chest; M2 complete].** Build is Input & Controls' body context, toggled by B / LB from OnFoot. Entering while holding auto-stows ("Stowed"; `no_room` refuses entry, Input rule 6). The panel cannot open in Build. Build auto-exits, with a toast naming why, on knockdown, mounting, swim start and Run Over; the selection is remembered for re-entry. The **build cursor** is the grid tile under the reticle, from a ground ray clamped to `buildReachMm` from the carrier. Beyond reach the ghost stays at the clamp point with a "Too far" pill and never places. Reach is a client rule only: the host does not check distance (spec 13 §3 keeps only consistency validations). **Placement grammar:** Place and PlaceLine are **not** Input G9 commits; Deconstruct is Build's only commit. A non-drag def places on the LMB / RT press (G8). A `drag` def latches the start tile on press and commits on release (release on the start tile sends `Place`; the G8 exception is handed to Input, OQ-BLD-1). The carrier may walk and sprint while Dragging, and the end tile is the clamped cursor, so long lines are drawn by walking. Each request owns its pending ghost and the cursor ghost keeps tracking, so placements are never throttled to one per round trip. Placement presses are not live while a deconstruct is in progress or pending (G9 item-moving taps). The catalogue bar groups pieces as Transport / Sorting / Storage / Vehicles / Defense, using the bindings in Input & Controls' Build table: select 1–8 / D-pad ←→, rotate R / RB, belt tier wheel / Y (cycles the def's `family`), pipette Q / D-pad ↓ (copies the hovered construct's def and rotation, and selects it even if locked or unaffordable; on a ruin it copies the ruin's def; on the PO it does nothing). Build mode is never replicated; the host sees only requests.

**B12 — Salvage remainder bank [M1].** The host keeps one **team-wide, run-scoped permille remainder per item id**. F3 adds into it and pays out whole units, so repeated half refunds of 1-count inputs return exactly ⌊Σ ratio × cost⌋ over time instead of 0 (floor) or 100 % (round-half-up). It is Sim state (dumped and hashed), host-only, reset at run start, never decayed.

**B13 — Ruins and rebuild hooks [M3].** Defenses owns what happens after `EntityDowned{Construct}`. Building exposes:
- `MarkRuin(id)`: sets the flag; the construct stays in the registry and occupies its tiles; Health is already unregistered by Defenses' reaction.
- `RestoreFromRuin(id)`: clears it and re-registers HP.
- `ChargeRecipe(actor, recipe, n, ratioPm)`: F1 over the B5 sources, all-or-nothing.

Rebuild pricing is therefore Defenses calling `ChargeRecipe` with its own `ruinRebuildRatioPm`, or ratio 0 under `InsuredRebuild` in Prep. **Deconstructing a ruin** is free, refunds nothing, does not touch the bank and discards the ruin's stored configuration. The ghost warns "Ruin — rebuild instead?" in Prep.

**B14 — Trust model.** No ownership checks (spec 13 §4.3): anyone may deconstruct anything, including a teammate's line. The host validates consistency only: tile legality, materials, phase, blueprint, cap, actor state. `ownerSlot` feeds Payday's "most built" (Run Structure) and the `bySlot` on removal events; it never gates an action.

**B15 — Milestone slices.**
- **M-1b (greybox stub):** B1 registry, occupancy and `Spawn`; B3; B4 tile checks for the authored set (unit-tested); Health registration; Debug free-place and remove. No build mode UI beyond a debug ghost.
- **M1:** B5–B6, B8, B10, B12, minimal B11 (wall_wood and chest; place, rotate, deconstruct, undo grace, cost chips).
- **M2 (complete):** full B2 catalogue, B7 lines, B9 blueprints, B11 tier and pipette, network events (Network).
- **M3:** B13 hooks and the defense pieces.

### States and Transitions

**Construct lifecycle (host).**

| State | Entered by | Exits |
|---|---|---|
| (none) | — | `Place` / `PlaceLine` / `Spawn` success → Live |
| Live | placement | `Deconstruct` → Removed; `EntityDowned` → Defenses `MarkRuin` → Ruin (M3); run end → discarded with the world (no event, refund or spill) |
| Ruin (M3) | `MarkRuin` | Defenses `RestoreFromRuin` → Live (same id, same anchor, behaviour config kept); `Deconstruct` → Removed (free, no refund) |
| Removed | — | terminal; the id is never reused |

**Player build state (client).**

| State | Entered by | Exits |
|---|---|---|
| Off | — | B / LB while OnFoot and not swimming → Browsing |
| Browsing (no piece selected) | entry; Back from Aiming | select / pipette → Aiming; B / LB or Back → Off; auto-exit → Off |
| Aiming (ghost shown) | a piece selected | LMB / RT on a non-drag def → sends `Place`, stays Aiming; press on a `drag` def → Dragging; hold RMB / LT on a construct → Deconstructing; Back → Browsing |
| Dragging (line ghost) | press on a `drag` def (start latched) | release → sends `PlaceLine` (or `Place` on the start tile), back to Aiming; Back / cancel → Aiming |
| Deconstructing (ring) | hold on a target | hold completes → Pending(deconstruct); release early or cursor leaves the target → previous state |
| Pending (deconstruct) | `Deconstruct` sent | reply or `ConstructRemoved` with our reqId → previous state; after `pendingCommitTimeout` shows "waiting", never rolls back. Placement pending ghosts resolve the same way, per request, without a state |

### Interactions with Other Systems

| System | Building receives | Building provides | Interface owner |
|---|---|---|---|
| Simulation Core | phase-0 op drain, `reqId`, `construct` id class, event log, opSeq order | three op handlers, two event kinds, the registry dump contributor | Simulation Core |
| Content & Data | `BuildingDef`, `RecipeDef`, `BuildingBehaviour` enum, Stat `ConstructHpMultPm`, RuleFlags `SharedBuild`/`InsuredRebuild`, `build.*` balance keys | the values in B2 and Tuning Knobs | Content & Data (schema); Building (values) |
| World Generation (provisional) | per-tile type, centre height (mm), buildable flag + cause, `StreetAxis`, node id, reserved mask, PO and Spawn pad, mailbox `approachTile`s | `TileLegality` as the shared definition of "buildable" for its ≥ 20 × 20 clear-area guarantee | World Generation |
| Grid Inventory | `CountItem`, `ConsumeItems`, `Grant(SpillWorld)`, R13 spill bag, container lifetime for chest/depot | the calls in B6, B8 | Grid Inventory |
| Health & Damage | hp / maxHp read (OQ-BLD-5) | `Register` at placement, `Unregister` at removal | Health & Damage |
| Input & Controls | Build context and bindings, `holdEff`, G9 commit/pending | the Deconstruct binding decision (B8) | Input & Controls |
| Player Character | alive / swimming / mounted state, host's latest transform per carrier, depenetration | static AABBs for solid constructs (registered at placement) | Player Character |
| Belt Logistics, Vehicles, Defenses | `IPlacementRule` vetoes, `OnPlaced` / `OnRemoving` handlers, items to spill | registry queries (`ConstructAt(tile)`, `TryGet(id)`), B13 hooks | each behaviour owner |
| Economy (provisional) | `IBlueprintQuery.Owns(bp)` | — | Economy |
| Run Structure (provisional) | current phase and shift; run start/end | `ConstructPlaced` / `ConstructRemoved` for Payday counts | Run Structure |
| Enemy AI & Waves | — | registry and occupancy for targeting and wall-aware pathing | Enemy AI |
| HUD, Inventory & Build UI | — | events, reason keys, cost and refund previews | UI owners |

## Formulas

All Sim math is integer (Simulation Core numerics rule). `ceilDiv(a, b) = (a + b − 1) / b` for a ≥ 0, b > 0.

**F1 — Recipe charge.** The `need` formula is defined as:

`need_i = ceilDiv(cost_i × n × ratioPm, 1000)` for every input i of the recipe; the charge succeeds iff `have_i ≥ need_i` for every i.

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| cost_i | int | 1–20 | data file (`RecipeDef.inputs[i].count`) | units of item i per construct; item ids distinct per recipe (ContentValidator) |
| n | int | 1–`maxLineTiles` | calculated | constructs charged in this op (1, or F2's k) |
| ratioPm | int | 0–1000 | constant / caller | 1000 for building; Defenses' `ruinRebuildRatioPm` for rebuild; 0 for Insured rebuild |
| have_i | int | 0–∞ | calculated (`CountItem` over the B5 sources) | spendable units at apply time |

**Output range:** `need_i` 0 … `cost_i × maxLineTiles` (0–800). Ceil means a ratio rebuild is never free unless ratio = 0: a belt rebuild at 500 ‰ costs 1 Plank + 1 Iron Ingot, while a sorter rebuild costs 2 Iron + 3 Stone.
**Example:** sorter (4 Iron, 6 Stone), n = 1, ratio 1000. The panel has 3 Iron and the PO Depot 2; the placer is 12 m from the PO, so the Depot is eligible (F5). need = 4 / 6, have = 5 / 6, so it succeeds: 3 Iron from the panel, 1 from the Depot, 6 Stone. At 25 m the Depot is not eligible, have = 3 < 4, and the reply is `insufficient{iron_ingot, 1}` with nothing moved.

**F2 — Line prefix.** The `linePrefix` formula is defined as:

`k = min(N, V, A, capLeft)`, with `A = min_i ⌊have_i / cost_i⌋`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| N | int | 1–`maxLineTiles` | request | tiles requested |
| V | int | 0–N | calculated | count of leading tiles that pass B4 steps 2–4 (the scan stops at the first failure). A step-1 (actor) failure rejects the whole op with that reason, k = 0 |
| A | int | 0–∞ | calculated | whole constructs affordable from the B5 sources |
| capLeft | int | 0–`constructCap` | calculated | free registry slots |

**Output range:** 0–N. k = 0 is a rejection. k = N reports `None`. Otherwise the stop reason is whichever term bound k (ties report the earliest of Invalid, Funds, Cap).
**Example:** N = 12, the 10th tile is a lot, so V = 9. Plank 7, Iron 20 → A = 7. capLeft 300 → k = 7, `Funds`; charge 7 Plank + 7 Iron.

**F3 — Salvage with remainder bank.** The `salvage` formula is defined as:

`x_i = cost_i × ratioPm + bank_i;  refund_i = x_i / 1000;  bank_i' = x_i mod 1000`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| cost_i | int | 1–20 | data file | the recipe input count of the removed construct's def |
| ratioPm | int | 0–1000 | calculated (F4) | refund ratio for this removal |
| bank_i | int | 0–999 | Sim state (B12) | team remainder for item i, in thousandths of a unit |

**Output range:** `refund_i` 0 … `cost_i`; `bank_i` stays in 0–999, so the team never gains more than 0.999 of a unit from rounding. With ratio 1000 the bank is unchanged; with ratio 0 (ruin) F3 is skipped entirely.
**Example:** five belt_mk1 deconstructed in Delivery (ratio 500), bank 0: refunds 0, 1, 0, 1, 0 Plank (and Iron), with the bank ending at 500. Total 2 + 2 for 5 × 0.5 = 2.5.

**F4 — Refund ratio.** The `salvageRatio` formula is defined as:

`ratioPm = 0 if ruin ∨ ownerSlot == System (pre-built: nobody paid); else 1000 if (actor == ownerSlot ∧ T − placedTick ≤ undoGraceTicks ∧ hp == maxHp); else salvageRatioPrepPm if phase == Prep; else salvageRatioDeliveryPm`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| T | uint32 | tick | Sim | apply tick of the Deconstruct op |
| placedTick | uint32 | tick | registry | tick the construct was placed |
| undoGraceTicks | int | 30–180 | data file | 90 (3 s) |
| hp, maxHp | int | — | Health & Damage (read in B8's step-1 snapshot) | an undamaged construct only |
| salvageRatioPrepPm / salvageRatioDeliveryPm | int | 0–1000 | data file | 1000 / 500 (spec 11 §5) |

**Output range:** 0, 500 or 1000 at defaults. **Example:** a wall placed at tick 1000 during Delivery and removed by its placer at tick 1080, undamaged → 1000 (full refund). Removed by a teammate at tick 1080 → 500.

**F5 — PO Depot eligibility.** The `depotEligible` formula is defined as:

`eligible = SharedBuild ∨ (dx² + dz² ≤ poDepotReachMm²)` (int64, mm, horizontal)

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| dx, dz | int | ±600 000 | calculated | placer's position as last applied on the host (Player Character) minus the **PO footprint centre** (anchor min-corner + 6 000 mm on each axis; "PO anchor centre" in this GDD means this point) |
| poDepotReachMm | int | 10 000–40 000 | data file | 20 000 (spec 04 §1.1) |
| SharedBuild | bool | — | RuleFlag (Content & Data) | Shared Build perk |

**Output range:** bool. Measured from the **player**, not the target tile: building far away needs carried materials. **Example:** player at (14 000, 0, 14 000) mm relative to the PO → 3.92 × 10⁸ ≤ 4 × 10⁸ → eligible.

**F6 — Terrain fit.** The `terrainFit` formula is defined as:

`slopePm(t) = max over the 4 neighbours n of |h(t) − h(n)| × 1000 / 2000`; neighbours outside the map are skipped, in-bounds neighbours (any type) use their centre height; legal iff `slopePm(t) ≤ def.maxSlopePm` for every footprint tile, and `max(h) − min(h) ≤ maxSpanMm` over the footprint; base height `H = max(h)`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| h(t) | int | ±100 000 | World Generation | tile-centre height, mm |
| maxSlopePm | int | 0–700 | data file (BuildingDef.placement) | tan of the limit × 1000: buildings and defenses 268 (15°, spec 04); transport 194 (≈ 11°) |
| maxSpanMm | int | 0–2000 | data file | 1000 (spec 04 "auto-level within 1 m") |

**Output range:** slopePm 0 … ~∞ (cliffs are already unbuildable at > 700). The transport limit keeps adjacent belt tops within 2000 × 0.194 = 388 mm, under Player Character's 400 mm step (PCM-P4-1b), so a rider is never stopped by a belt-to-belt step. If that step changes, this knob changes with it. **Example:** a 2×2 sorter on NW / NE / SW / SE heights 100 / 400 / 500 / 900 mm (outside neighbours level with their adjacent footprint tile): span 800 ≤ 1000; worst slope (900 − 400) × 1000 / 2000 = 250 ≤ 268 → legal, H = 900 mm, and the skirt drops 800 mm at the low corner. At 100 / 400 / 700 / 900 the NW–SW step is 600 mm = 300 pm → `too_steep`.

## Edge Cases

### Same-tick races (opSeq order decides; no reservations)

- **If two players place on the same tile in one tick**: the lower opSeq wins. The other gets `occupied` (for a line, V stops there) and is charged only for tiles actually placed.
- **If an earlier op this tick spent the materials a placement counted on**: F1 / F2 run on the stock at apply time. The result is `insufficient` or a shorter prefix; nothing is held from the client's preview.
- **If two players deconstruct the same construct**: the first succeeds and refunds once. The second gets `gone` (the UI treats it as done, with no second refund). Ids are never reused, so a stale id cannot hit a new construct on the same tile.
- **If a place and a deconstruct on the same tile arrive in one tick**: opSeq order. Deconstruct-first frees the tile and the place may succeed; place-first gets `occupied`.
- **If a blueprint is bought in the same tick as a placement that needs it**: visible if the purchase's opSeq is lower; otherwise `locked`.

### Phase, life state and timing

- **If the phase changes on the apply tick**: the phase visible in phase 0 decides both legality and F4's ratio. A Deconstruct latched in Prep and applied in the first Delivery tick refunds 500 ‰.
- **If the placer is knocked down, starts swimming or disconnects before the op applies**: `not_alive`, `swimming` or `unknown_actor` (Simulation Core); nothing changes. Build mode auto-exits on the client.
- **If a construct becomes a ruin during a deconstruct hold**: the host replies `target_changed` and changes nothing (likewise if a targeted ruin was restored to Live first). Clearing a ruin throws away its rebuild option and stored configuration, so it must always be a deliberate hold on a ruin, never a side-effect of a raid hit.
- **If a construct takes damage inside the undo window**: F4 falls through to the phase ratio. Hp == maxHp is tested at apply time, so a regen-free construct stays "damaged" until repaired.
- **If the StatSheet changes mid-hold** (e.g. `ConstructHpMultPm`): only `holdTicks` is latched at press; everything else is evaluated at apply. Health F2 keeps an undamaged construct at full HP, so a stat change alone never loses the undo refund.
- **If a run ends with live constructs**: the registry is discarded with the world. No refunds, no spills, no `ConstructRemoved` per construct.

### Space, contents and limits

- **If the deconstructor's panel is full**: F3's refund overflows into one SpillWorld heap at their feet (Grid Inventory `Grant`). It is never sent to the PO Depot, so "Depot full" cannot block a removal.
- **If a deconstructed chest or depot holds items, or a belt carries mail**: one R13 spill bag at the footprint centre holds all of it. Mail returns to the Intake when the bag expires, and non-mail becomes a Spill heap (Grid Inventory R13). Players who had it open get `container_gone` (Grid Inventory).
- **If the construct cap is reached mid-line**: F2 stops at `capLeft` with `stopReason = Cap`. Ruins count toward the cap, which is a reason to clear them.
- **If a line crosses a street correctly but ends in the middle of it**: legal. The belt simply ends there and leaks to the ground (Belt Logistics). Building checks tiles, not routes.
- **If a solid construct is placed while a WorldItem or spill heap lies on the tile**: allowed. The item stays at its position on top of the construct and can still be picked up (Grid Inventory owns WorldItem positions); no item is destroyed or moved.
- **If someone walks into a just-placed wall**: the construct wins. The host never undoes a placement, and Player Character depenetrates the carrier.
- **If walls fully seal the PO from every spawn edge**: placement is legal (walls redirect pathing, spec 05 §3.3). Enemy AI must treat walls as passable at an HP-based cost when no open route exists (hand-back, OQ-BLD-8), so a target is always reachable. Streets cannot be walled (B4), so districts and mailboxes stay reachable.
- **If a player tries to deconstruct the PO**: `not_deconstructable`; the cursor shows no ring. The M-1b authored constructs are ordinary constructs and may be deconstructed, but refund nothing (F4: `ownerSlot` System, nobody paid); contents still spill.

### Exploits considered

- **Place from the Depot with SharedBuild, then undo-grace**: a remote Depot withdrawal into the panel, bounded by recipe cost. Accepted (friends-only); measured in telemetry as `graceRefunds`.
- **Prep shuffle**: free re-layout in Prep is intended. Delivery re-layout costs half, which is the commitment.
- **Ruin clearing for refunds**: impossible (ratio 0).
- **Churn to burn ids**: bounded by `constructCap` and the 2²⁴ counter (Simulation Core). Not a practical risk in a 30-minute run.

## Dependencies

| System | Direction | Hard / Soft | Status | Interface |
|---|---|---|---|---|
| Simulation Core | upstream | Hard | In Review (Provisional) | phase 0 op handlers, `construct` id class, event log, Sim dump (registry + bank) |
| Content & Data Definitions | upstream | Hard | In Review (Provisional) | `BuildingDef` / `RecipeDef` schemas and the B2 values; `ConstructHpMultPm`; RuleFlags `SharedBuild`, `InsuredRebuild`; `build.*` keys |
| World Generation | upstream | Hard | **not written — provisional contract** (OQ-BLD-3) | tile type, centre height, buildable + cause, `StreetAxis`, node id, reserved mask, PO and Spawn pad, mailbox `approachTile`s |
| Grid Inventory & Containers | upstream | Hard | In Review | R17 `CountItem` / `ConsumeItems` / `Grant`; R13 spill bag |
| Health & Damage | upstream | Hard | Designed (Provisional) | `Register` / `Unregister` constructs |
| Input & Controls | upstream | Hard (M1) | In Review | Build context, bindings, `holdEff`, G9 |
| Player Character & Movement | upstream | Hard (M1) | In Review | life state, swimming, mounted, host transform samples, depenetration; receives construct AABBs |
| Economy, Wallet & Shop | upstream | Soft (M2) | not written | `IBlueprintQuery.Owns` |
| Run Structure | upstream | Hard (M1) | not written | phase and shift reads |
| Belt Logistics | downstream | Hard (M-1b) | not written | behaviour hooks, `IPlacementRule`, lane items to spill |
| Defenses & Construct Damage | downstream | Hard (M3) | not written | B13 hooks |
| Vehicles & NPC Drivers | downstream | Soft (M2) | not written | vehicle depot behaviour, keep-clear rule |
| Enemy AI & Waves | downstream | Hard (M3) | not written | registry and occupancy for targeting and pathing |
| Economy, Run Structure, Perks | downstream | Soft | not written | `ConstructPlaced` / `ConstructRemoved` counts; costs feed balance |
| Inventory & Build UI, HUD, Presentation & World Dressing | downstream | Soft | not written / In Review | events, reason keys, previews; crushable dressing |

**Sim / Presentation split.** **Sim** (`src/PerformativeMail.Sim/Building/`, Godot-free): registry, occupancy, `TileLegality`, F1–F6, op handlers, bank, events, B13 hooks. **Presentation / Client.Core**: build mode state, cursor raycast, catalogue bar, ghost, chips, ring and pills. The client calls the same `TileLegality` and F1 / F2 / F3 on its replicated mirror for the ghost (advisory only; the host decides). Nothing in build mode is Sim state.

## Network

v1 netcode per spec 13 §3: host-authoritative constructs, Godot high-level multiplayer through the adapter (ADR-0011), everything replicated to everyone. Godot 4.7.1 multiplayer behaviour is NOT SOURCEABLE (VERSION.md), so wire claims below are adapter contracts and ordering claims are spike-gated (architecture Q-ARCH-05).

**Requests (reliable, applied in phase 0).** `Place{reqId, defIndex u16, anchor 2×u16, rot u8}`, `PlaceLine{reqId, defIndex, start 2×u16, end 2×u16}`, `Deconstruct{reqId, constructId u32, targetRuin bool}`. All are well under the 48 B op payload. The adapter rejects malformed requests as `bad_request`: `rot` > 3, `defIndex` out of range, a line longer than `maxLineTiles`, a non-straight line.

**Events.**
- `ConstructPlaced{reqId, firstId u32, defIndex, start, end, count u8, rot, ownerSlot u8, epoch u32}` (≈ 24 B). **One event per op, run-encoded.** Ids are allocated as one contiguous block, and tiles come from the shared `LineTiles(start, end)` function. Per-construct events would break Simulation Core's per-op entry sizing and, with 64 drained ops, overflow the 4,096-entry log once lines exceed ~46 tiles.
- `ConstructRemoved{reqId, id, reason (Deconstruct | RuinCleared), bySlot, epoch}` (≈ 14 B).
- Ruin transitions and damaged HP % are Defenses' and Health's messages.

**Order within one handler (one CallSeq, one reliable channel).** Place: Grid charge events → `ConstructPlaced` → `OpReply`. Deconstruct: spill-bag events → `ConstructRemoved` → refund `Grant` events → `OpReply`. The reply is always last (ADR-0007 / 0008) and carries `{ok, reason, placedK, stopReason, resultVersion per touched container}`. Audience: construct events go to every peer; Grid events keep Grid's audiences (panel to its owner, PO Depot group counts to everyone).

**Pending ghost.** Presentation only: no predicted registry entry, no predicted material deduction. On send, a copy of the ghost freezes in the pending look per request while the cursor ghost keeps tracking. Our `ConstructPlaced` / `ConstructRemoved` (matched by reqId) solidifies or removes it, and a rejection shows its reason. Line tiles beyond `placedK` revert to the plain ghost. Timeout shows "waiting" and never rolls back; the reply is authoritative.

**JoinState / RejoinState.** A construct block sorted by id, 13 B per record (`id, defIndex, anchor, rot, ownerSlot, flags`), with a header `{epoch u32, count u16}`. Budget ≤ 16 KB at `constructCap` 1000; a typical 150 constructs is ≈ 2.0 KB. Behaviour config (filters, routes) is in its owners' blocks. The bank is host-only (not replicated, in the dump and hash); refund previews show F3 with the bank at 0, and the result may be one unit higher. Rejoin uses the same block.

**Consistency.** `epoch` increments on every Placed / Removed. A gap on a client, or a mismatch with `{constructCount u16, epoch u32}` in the low-rate global block, requests a ConstructList resync through the adapter's snapshot path (debounced 15 ticks). Construct events are never dropped by any bandwidth valve.

**Bandwidth.** A 40-tile line ≈ 24 B per peer plus the requester's charge and reply (≈ 30–60 B). Deconstruct ≈ 14 B per peer and is limited by the hold (≤ ~2.5 per second per player). JoinState is the only burst and is paced like Grid Inventory's.

**Never trusted from the client:** actor (adapter-stamped), costs, refunds, ids, `ownerSlot`, `placedK`, phase, blueprint ownership, the existence of `constructId`. **Not checked by design:** distance from the player to the target tile (spec 13 §3). **Adapter owns:** codec, structural checks, actor stamping, reqId dedupe and replay, JoinState chunking, epoch-gap resync. **Sim owns:** every semantic check, charge and refund, bank, registry, ids, Health registration, events and replies. The Sim never sees peers.

## Tuning Knobs

All in `content/balance/building.jsonc` unless noted; PROPOSED unless spec-sourced.

| Knob | Default | Safe range | Affects | Too high | Too low |
|---|---|---|---|---|---|
| `build.salvageRatioPrepPm` | 1000 (spec 11) | 800–1000 (hard ≤ 1000) | Prep re-layout cost | > 1000 is a material faucet (ContentValidator error) | Prep experimentation is punished |
| `build.salvageRatioDeliveryPm` | 500 (spec 11) | 250–750 | Delivery commitment | mid-shift re-layout is free; Prep planning matters less | emergency fixes feel punitive |
| `build.undoGraceTicks` | 90 (3 s) | 30–180 | mis-click forgiveness | probing placements mid-raid for free | useless against real mis-clicks |
| `build.poDepotReachMm` | 20 000 (spec 04) | 10 000–40 000 | where the team pool is spendable | SharedBuild perk is moot; less carrying (Pillar 3) | everything must be hauled from the Depot |
| `build.buildReachMm` (client) | 8 000 (spec 06) | 5 000–12 000 | how far the cursor reaches | builders stop walking | builders shuffle per tile |
| `build.maxLineTiles` | 40 | 16–64 | drag-line length | validation spike per op, log pressure | click-heavy belts |
| `build.constructCap` | 1000 | 600–2000 | registry size, incl. ruins | tick budget (spec 07 validated 400 belts) | walls and belts compete for slots |
| `build.bodyEpsilonMm` | 150 | 50–300 | placing next to a teammate | walls clip through capsules | walls refused beside a player |
| `BuildingDef.placement.maxSlopePm` | 268 buildings / 194 transport | 100–400 | buildable terrain | belts form rider-stopping steps (> 200) | factory space shrinks below spec 02's guarantee |
| `build.maxSpanMm` | 1000 (spec 04) | 500–1500 | uneven ground | tall skirts look odd | few legal 2×2 / 3×3 spots |
| `build.costsEnabled` (Debug only) | true (false in the M-1b greybox) | — | free placement for testing | — | — |

Referenced, not owned: `holdBase` 0.5 s for deconstruct (Input & Controls, F1 `holdEff`); `ruinRebuildRatioPm` 500 (Defenses; spec 08's per-def `ruinRebuildRatio` is dropped from `BuildingDef`, OQ-BLD-6); `ConstructHpMultPm` (Content & Data stat). **Interactions:** `poDepotReachMm` and `SharedBuild` overlap (a reach ≥ the island makes the perk inert); `undoGraceTicks` only matters while `salvageRatioDeliveryPm` < 1000; transport `maxSlopePm` is bound to Player Character's step height.

## Visual/Audio Requirements

*art-director (2026-10-16).* M-1b is capsule art only. M1 is greybox-capable, and M2 is the art pass. The art bible applies: build ghosts (§7); constructs are Tier B, ≥ 15 % Postal Blue, on a 10 cm Charcoal skirt (§6); never magenta, never district colour; Stamp Red is a fill, never text.

| Event | Visual | Audio mood (audio-director owns cue design) · caption | M |
|---|---|---|---|
| Ghost valid | Go Green 40 % fill, solid outline, Chalk White facing chevron on the top face (belts, inserters) | — | M1 |
| Ghost invalid | Stamp Red 40 % fill, **dashed** outline, X, reason pill (red fill, Chalk White Label text) | — | M1 |
| Ghost unaffordable | Chalk White 25 % fill, **solid** outline (the tile is legal), red "missing" chips (red fill, white icon and count) | — | M1 |
| Ghost locked | Chalk White 25 % fill, padlock chip "Needs Blueprint: Sorting"; catalogue slot greyed with padlock. No red: locked is not a mistake | — | M2 |
| Ghost pending | frozen valid ghost at 60 % opacity, **solid** Chalk outline, hourglass chip (dashed is reserved for invalid) | none | M1 |
| Line prefix | valid tiles green; the break tile gets the X and the single reason pill; the remainder red dashed; a "23/40" count pill at the cursor end | — | M2 |
| Place | ghost snaps to the mesh; squash 0.8 → 1.08 → 1.0 over 0.2 s; Chalk dust puff at the skirt; crushable dressing under the footprint hidden at once | chunky rubber-stamp *thunk* · [Built] | M2 (M1: instant swap; cue M1) |
| Line cascade | per-tile pop staggered 400 ms ÷ N (a 40-tile line lands in ≤ 0.4 s) | ratchet that climbs with the count · [Building line] | M2 |
| Deconstruct hold | Chalk White ring fills clockwise around the construct base over the hold; target outline Chalk White (not red); containers show a red-fill "Spills 12 items" pill | — | M1 |
| Deconstruct complete | reverse pop; refund chips (resource icons) arc to the player in ≤ 0.3 s and the hotbar bounces; contents spill as real parcels; Delivery's lost half is a cartoon smoke puff, not red | clasp-pop + clink · [Removed]; rattling parcels · [Items spilled] | M2 |
| Rejection | 0.15 s single outline flash; reason pill held 1.5 s; no cursor shake | soft cartoon *bonk*, clearly unlike the place cue · [Can't build here] | M1 |
| Uneven ground | the skirt becomes a flat-sided Charcoal prism down to grade, buried 20 cm (≤ 1.2 m tall), no texture, no outline; it stretches with the place pop and never animates separately | — | M2 (M1: box at height H) |

**Reduced motion:** pops, cascade and chip flight become 0.1 s opacity cuts. Refund becomes a "+N" chip over the construct for 0.6 s. No marching dashes. The ring fill stays, because it reads as progress, not travel. **Colour-blind redundancy:** valid / invalid / pending differ by outline style (solid / dashed / solid-plus-hourglass), the X glyph and text, never by colour alone. The force-patterns setting scales dash length and X ×1.5. Valid and rejected sounds are distinct and every cue has a caption.

📌 **Asset Spec** — after the art bible is approved, run `/asset-spec system:building-system` for the ghost material, skirt prefab, ring, chips and pills.

## Game Feel

- **The ghost tracks the cursor every rendered frame.** Validity and cost chips update on the same frame from the local mirror; `CountItem` is an index read (Grid R17).
- **Commit is instant locally:** the pending look appears on the press frame. The listen-server host sees placement in the same tick; a remote client sees it after one round trip (target ≤ 150 ms at 100 ms RTT). Nothing waits on a build timer, ever.
- **Deconstruct weight:** 0.5 s hold (15 ticks at the default setting, Input F1) is long enough to stop accidental removals while sprinting through a factory and short enough for a pit stop. Moving the cursor off the target cancels with no penalty.
- **Lines feel like one gesture:** the drag snaps to the dominant axis, the cost tally runs as tiles extend, and release lands the whole prefix in ≤ 0.4 s.
- **Rotation snaps in 90° steps** with a 0.08 s ease (instant under reduced motion). R during a drag is ignored, because the drag sets the direction.
- **Fantasy tests (M2 playtest, PROPOSED):** (a) median build-mode time per Prep ≤ 50 % of Prep (chore guard); (b) median per-player build-mode time per Delivery ≤ 15 % of Delivery, and every tester carries mail by hand during the shift-3 Delivery while the reference factory runs (Pillar 3 spine). Failing either is fixed with tooling (lines, pipette), never by adding timers.

## UI Requirements

Building owns no screen. It supplies reason keys, cost and refund previews and events. Layout belongs to **Inventory & Build UI (#25)** and **HUD & Notifications**.

- **#25:** catalogue bar (5 categories, item row 1–8 / D-pad ←→, recency order, locked items last with a padlock); selected-item card (name, one line, cost chips "Iron Ingot 3/5" with have = panel + PO Depot when F5 holds, read from Grid's group counts); tier chip; ring; refund chips ("Refund: 1 Plank · ½ in Delivery"); "Spills N items" pill; line counter; reason pill. Text scales 100–200 % and reflows. The bar must not overlap the hotbar.
- **HUD:** a Build flag that swaps the prompt slot to Build hints (Place · Rotate · Deconstruct (hold) · Pipette); toasts "Stowed", "Placed 7 of 12", "Build mode closed: swimming", plus rejections without a ghost.
- **Reason pills (deadpan; localisation keys):** `too_far` "Too far"; `out_of_bounds` "Off the map"; `water` "In the drink"; `street` "Not on the road"; `unbuildable` "Somebody's lawn" / "Too cliffy" / "That's the Post Office"; `reserved` "Keep clear"; `node` "Tree in the way" / "Rock in the way"; `occupied` "Something's already here"; `too_steep` "Too steep"; `uneven` "Too bumpy"; `blocked_by_body` "Someone's standing there"; `insufficient` "Missing 2 Iron Ingot"; `locked` "Needs Blueprint: Sorting"; `wrong_phase` "Not during Payday"; `cap` "Island construct limit"; `not_deconstructable` "Load-bearing"; `swimming` "Dry land required to build" (Player Character wording); `target_changed` "It changed — try again".
- **Accessibility:** hold-as-toggle for deconstruct (Input G7: one press starts the ring, which completes by itself; a second press cancels); full KBM and pad parity; reticle snap-to-buildable-tile assist on pad (knob, #25); no colour-only state; no flashing above 3 Hz (one flash per rejection).

> **📌 UX Flag — Building System**: this system has UI requirements. In Pre-Production, run `/ux-design` for (1) the Build HUD layout (catalogue bar, chips, pills, ring, safe areas 4:3–21:9), (2) the Build-mode flow (entry, exit, auto-exit, pending and rejection), (3) the pad aim-release line grammar (usability-test it: the riskiest control). Stories cite `design/ux/build-mode.md`, not this GDD.

## Cross-References

| GDD | Sections here | What is referenced | Status |
|---|---|---|---|
| Input & Controls | B8, B11, Game Feel | Build context and bindings; `holdEff` (deconstruct 0.5 s); G9 commit; rule 6 auto-stow; **closes OQ-IC-13** | In Review — hand-back OQ-BLD-1 |
| Player Character & Movement | B4, B8, B10, Edge Cases | P10 verb table (Deconstruct row withdrawn), P11 focus (no focus in Build), P4 colliders and 400 mm step, P6 swimming, P17 Spawn pad, OQ #18 (adopted), OQ #20 (spawn pads: out of v1) | In Review — hand-back OQ-BLD-2 |
| Grid Inventory & Containers | B5, B6, B8, F1, Edge Cases | R17 `CountItem` / `ConsumeItems` / `Grant(SpillWorld)`; R13 spill bag on deconstructing a non-empty container; R12 WorldItems | In Review — confirms R13/R17 rows |
| Health & Damage | B1, B2, B6, B8, F4 | HD1 `Register` / `Unregister`; F2 maxHp with `ConstructHpMultPm` | Designed — hand-back OQ-BLD-5 (registrant) |
| Content & Data Definitions | B2, F6, Tuning Knobs | `BuildingDef` / `RecipeDef` schema additions; `BuildingBehaviour` enum; RuleFlags | In Review — hand-back OQ-BLD-4 |
| Simulation Core | B1, Network | phase 0, op FIFO, `construct` class, event log limits | In Review |
| Mail & Destinations | B4 | `approachTile`, one automated feeder per mailbox | Designed |
| HUD & Notifications | UI Requirements | prompt slot, toasts, feed | In Review |
| World Generation, Belt Logistics, Defenses, Vehicles, Enemy AI, Economy, Run Structure, Inventory & Build UI | throughout | provisional contracts in Interactions and Open Questions | not written |

## Acceptance Criteria

*Validated by qa-lead 2026-10-16 (6 merges, 6 additions, 7 defects fixed).* Tags: **[Logic]** xUnit on the Godot-free Sim (`tests/unit/building/`); **[Integration]** Sim across two systems' public APIs, or Godot headless through the adapter; **[NET]** loopback adapter / bot clients; **[UI]** Godot test asserting the UI event log, with a retained screenshot per state signed off by the lead; **[MANUAL]** tester on a build, evidence in `production/qa/evidence/building/`. Milestone in brackets. Default fixture **FX-B**: the world is exactly 20 × 20 tiles (0–19), all buildable at height 0; the PO, its apron and the Spawn pad lie outside it; the team owns every blueprint and P1 and P2 each hold 10 of every item unless stated; P1 (slot 1) is 12 000 mm horizontally from the PO anchor centre; Prep; `build.*` defaults; bank 0; empty registry. **FX-B-L**: FX-B on 64 × 64 tiles (0–63), with the PO footprint, apron, Intake outfeed lane and Spawn pad placed inside it at tiles the AC states; used wherever an AC needs > 20 tiles or the PO area.

**Registry, footprint and legality**
- **AC-B1-1 [Logic] [M-1b]** GIVEN FX-B, WHEN System `Spawn(belt_mk1, (3,3), 0)`, `Spawn(address_sorter_mk1, (5,5), 1)` and `Spawn(wall_wood, (9,9), 0)` drain in tick 0, THEN ids are 50 331 649, …650, …651; the sorter occupies (5,5), (6,5), (5,6), (6,6); each has `ownerSlot` System, `placedTick` 0 and the given rot, and is registered with Health at its B2 HP; no material moved. WHEN the belt is Debug-removed at tick T and a belt is spawned on (3,3) at T + 1, THEN `Health.Unregister` ran, the tile was free and the new id differs from every earlier id. WHEN `Spawn` targets an occupied tile, THEN the Debug assert fires.
- **AC-B1-3 [Logic] [M1; ruin variant M3]** GIVEN `constructCap` 3 with 3 Live constructs, WHEN a wall is placed on a street tile, THEN `cap` (actor checks precede tile checks). [M3] With 2 Live + 1 ruin, THEN `cap`.
- **AC-B1-4 [Logic] [M1]** GIVEN 5 Live constructs and a bank of 500, WHEN the run ends, THEN no `ConstructRemoved`, refund, spill or Health callback fires, and the next run starts with an empty registry and every bank at 0.
- **AC-B2-1 [Logic] [M-1b]** GIVEN v1 content, WHEN `BuildingDef`s are read, THEN each B2 row matches (id, footprint, HP, cost, blueprint, class, solid, drag), and no def with a deferred behaviour (pipe, pump, pier, port, spike) is reachable.
- **AC-B3-1 [Logic] [M-1b]** GIVEN a synthetic 2×1 def at (4,4), THEN `Footprint` is 2×1 at rot 0 and 2 and 1×2 at rot 1 and 3; the sorter's occupied set is identical at all four rotations; the front vector is (0,−1), (+1,0), (0,+1), (−1,0) for rot 0–3.
- **AC-B4-1 [Logic, parametrised] [M-1b]** GIVEN FX-B with one fault per case, WHEN `TileLegality` runs for wall_wood, THEN: tile (−1,0) → `out_of_bounds`; water → `water`; street → `street`; lot → `unbuildable(lot)`; PO apron (FX-B-L) → `reserved`; tree → `node`; existing belt → `occupied`; neighbour 600 mm higher → `too_steep` (300 > 268). A sorter with 1 of 4 tiles water → `water` with that tile as the failing set; water + occupied on one tile → `water`; occupied + too_steep → `occupied`.
- **AC-B4-2 [Logic] [M2]** GIVEN a 2-tile-wide NS street, WHEN belt_mk1 is checked on a street tile at rot 1 (perpendicular) → legal; at rot 0 → `street`; on an intersection tile → `street`; wall_wood on any street tile → `street`; inserter on an `approachTile` → legal; wall_wood there → `reserved`.
- **AC-B4-3 [Logic] [M-1b]** GIVEN a Spawn pad whose ring lies on flat buildable tiles outside the PO apron, WHEN any def is checked on a tile whose centre is ≤ 2.0 m from the pad centre, THEN `reserved`; with the pad centre offset so two tile centres lie 2 000 mm and 2 001 mm from it, the first is `reserved` and the second legal.
- **AC-B4-4 [Logic] [M1]** GIVEN a carrier capsule centred on tile (3,3), WHEN wall_wood is placed there → `blocked_by_body`; belt_mk1 there → legal. GIVEN the capsule overlaps the footprint by ≤ 150 mm (`bodyEpsilonMm`), THEN wall_wood is legal.
- **AC-B4-5 [Logic] [M2]** GIVEN P1 holds nothing and a stub `IPlacementRule` vetoes (4,4) with `feeder_taken`, THEN belt_mk1 at (4,4) → `feeder_taken`, and wall_wood on a street tile → `street`, never `insufficient`. With full stock, a vetoed placement charges nothing.
- **AC-B4-6 [Logic] [M1]** GIVEN FX-B-L with an Intake outfeed lane tile inside the PO apron, THEN belt_mk1 there is legal, wall_wood there → `reserved`, and a 12-tile belt line starting on it places its first tile. wall_wood on a Path tile → `street`; belt_mk1 on a Bridge tile at any rot → `street`.
- **AC-F6-1 [Logic] [M-1b]** GIVEN a sorter on NW / NE / SW / SE heights 100 / 400 / 500 / 900 mm with level outside neighbours, THEN legal with H = 900 and worst slope 250 pm. GIVEN 0 / 500 / 500 / 1001 → `uneven`; 0 / 500 / 500 / 1000 → legal. GIVEN a flat sorter with one outside neighbour 537 mm higher → legal (268); 538 mm → `too_steep` (269). GIVEN a belt with a neighbour 389 mm higher → legal (194); 390 mm → `too_steep` (195). A belt on map-edge tile (0,5) with level in-bounds neighbours is legal (the off-map neighbour is skipped).

**Charging and placement (F1, F5)**
- **AC-B5-1 [Logic] [M-1b]** GIVEN Debug `build.costsEnabled = false`, WHEN P1 with an empty panel places a sorter, THEN it is placed and no container changes; in a Release build the knob is absent.
- **AC-B6-1 [Logic] [M1]** GIVEN P1 has 3 Iron and 6 Stone in the panel and the PO Depot has 2 Iron, WHEN a sorter is placed, THEN 3 Iron leave the panel, 1 leaves the Depot and 6 Stone leave the panel; one `ConstructPlaced` is raised before the `OpReply`.
- **AC-B6-2 [Logic] [M1]** GIVEN AC-B6-1's stock and a target tile 40 m from P1, WHEN P1 stands at (dx, dz) mm from the PO anchor centre, THEN the sorter places at (20 000, 0) and at (14 142, 14 142) with a 5 000 mm vertical offset; it fails with `insufficient{iron_ingot, 1}` at (20 001, 0) and (14 143, 14 143); at (25 000, 0) with `SharedBuild` on, it places. GIVEN 3 Iron, 4 Stone and an empty Depot, THEN `insufficient{iron_ingot 1, stone 2}`, every container version is unchanged and no id is allocated.
- **AC-B6-4 [Logic] [M1]** GIVEN P1 and P2 each place wall_wood on tile (3,3) in one tick, P1 first by opSeq, THEN P1's wall exists, P2 gets `occupied` and P2's Logs are unchanged.
- **AC-B6-5 [Logic] [M1]** GIVEN P1 has 3 Log and P1's own `drop` of 1 Log drains earlier in the same tick, WHEN wall_wood (3 Log) applies, THEN `insufficient{log, 1}` and the remaining 2 Log are untouched.

**Lines (F2)**
- **AC-B7-1 [Logic] [M2]** GIVEN N = 12 east from (0,0), tile (9,0) a lot, 7 Plank and 20 Iron, WHEN `PlaceLine(belt_mk1)`, THEN 7 belts are placed at (0..6, 0) with rot 1 and contiguous ids, exactly 7 Plank + 7 Iron are charged, the reply is `placedK 7, stopReason Funds`, and one `ConstructPlaced{count 7}` is raised. GIVEN belt_mk2 with 7 Plank, 9 Iron and 12 clear tiles, THEN k = 4, `Funds`, charge 4 Plank + 8 Iron.
- **AC-B7-2 [Logic] [M2]** GIVEN enough materials and tile (9,0) a lot, THEN k = 9, `stopReason Invalid(unbuildable)`, and tiles 10–11 are not placed (no gaps). GIVEN V = A = 7, THEN `Invalid`. GIVEN the first tile invalid or unaffordable, THEN k = 0, nothing consumed, no event. On FX-B-L, N = 40 is accepted and k = N reports `None`; a `PlaceLine` with N = 1 is `bad_request` (the client sends `Place`).
- **AC-B7-3 [Logic] [M2]** GIVEN `capLeft` 4, WHEN a 12-tile line is requested, THEN k = 4, `Cap`. GIVEN the registry already at cap, THEN the op is rejected `cap` with k = 0 (B4 step 1), not `stopReason Cap`.
- **AC-B7-4 [Logic] [M2]** GIVEN a client drag from (0,0) to (5,2), THEN the request is (0,0)→(5,0), N = 6; (0,0)→(3,3) projects to (3,0). (5,0)→(0,0) gives rot 3, (0,0)→(0,5) rot 2, (0,5)→(0,0) rot 0.

**Deconstruct and salvage (B8, F3, F4, B12)**
- **AC-B8-1 [Logic] [M1]** GIVEN a wall_wood placed by P1 in Prep at tick 100, WHEN P1 deconstructs at tick 1000, THEN 3 Log are granted to P1's panel, `Health.Unregister` is called, the tile is free and `ConstructRemoved{Deconstruct, bySlot 1}` is raised; the wall's Player Character AABB is gone, so a carrier walks through the tile.
- **AC-B8-2 [Logic] [M1]** GIVEN a wall placed by P1 at tick 1000 in Delivery, THEN F4 gives 1000 for P1 at 1090 undamaged (refund 3 Log); 500 for P1 at 1091, for P2 at 1050, for P1 at 1010 after 1 damage, 1000 for P1 at 1010 after a `ConstructHpMultPm` change on the undamaged wall (Health F2 keeps it full); 1000 for anyone in Prep; 0 for a ruin. End to end, P1's `Deconstruct` applying at 1080 grants all 3 Log, with the ratio taken from the B8 step-1 snapshot before `Unregister`.
- **AC-B12-1 [Logic] [M1]** GIVEN bank 0 and five belt_mk1 placed by P1, WHEN P2 deconstructs them one by one in Delivery, THEN Plank refunds are 0, 1, 0, 1, 0 (Iron the same), each bank ends at 500 and the state hash changes with the bank. A belt removed next by P1 inside the undo grace (ratio 1000) leaves the Plank bank at 500; a wall removed at 500 with Log bank 0 refunds 1 Log (bank 500). A new run resets every bank to 0.
- **AC-B8-4 [Logic] [M1]** GIVEN P1's panel has no room, WHEN a wall is deconstructed in Prep, THEN one SpillWorld heap with 3 Log is at P1's feet and the PO Depot is unchanged.
- **AC-B8-5 [Integration] [M1]** GIVEN a chest holding 3 letters and 10 Fiber that P2 has open, WHEN P1 deconstructs it in Prep, THEN one R13 spill bag at the chest's centre holds exactly those entries, P2 receives `container_gone`, P1 receives 4 Log, Σ units before = Σ after, and the events are ordered spill → `ConstructRemoved` → refund → `OpReply`.
- **AC-B8-6 [Logic] [M1]** GIVEN the PO, WHEN any player sends `Deconstruct`, THEN `not_deconstructable` and nothing changes. GIVEN P1 and P2 both send `Deconstruct` for one wall in one tick, THEN the first refunds once and the second gets `gone`. GIVEN a System-spawned wall in Prep, WHEN deconstructed, THEN refund 0 and every bank unchanged.
- **AC-B8-8 [Logic] [M3]** GIVEN a ruin, THEN a Place on its tiles → `occupied`; WHEN it is deconstructed with `targetRuin = true`, THEN refund 0, bank untouched, `ConstructRemoved{RuinCleared}`. GIVEN a construct that became a ruin earlier in the tick and `Deconstruct{targetRuin = false}`, THEN `target_changed`; GIVEN a ruin restored to Live earlier in the tick and `targetRuin = true`, THEN `target_changed`; nothing changes in either case.
- **AC-B13-1 [Logic] [M3]** GIVEN `ChargeRecipe(P1, recipe_address_sorter_mk1, 1, 500)` with stock 2 Iron + 3 Stone, THEN it succeeds and consumes exactly that; with 1 Iron → `insufficient`, nothing consumed. `ChargeRecipe(P1, belt_mk1, 3, 500)` needs 2 Plank + 2 Iron (n multiplies before the ceil). Ratio 0 succeeds on an empty stock and consumes nothing.
- **AC-B13-2 [Logic] [M3]** GIVEN a Live wall, WHEN `MarkRuin`, THEN the id stays, its tiles stay occupied and a Place there → `occupied`; WHEN `RestoreFromRuin`, THEN the flag clears, id and anchor are unchanged and Health is re-registered at full HP.

**Phase, actor and availability**
- **AC-B10-1 [Logic, parametrised] [M1]** GIVEN each phase, THEN Place and Deconstruct are legal in Prep, Delivery and the Raid window, and `wrong_phase` in Generating, Payday, Draft, Results and Lobby. GIVEN P1 knocked down, Frozen, swimming or (for Place) mounted, THEN `not_alive`, `not_alive`, `swimming`, `mounted`, with nothing changed. A Deconstruct pressed in Prep and applied on the first Delivery tick refunds at 500 ‰.
- **AC-B9-1 [Logic] [M2]** GIVEN the team does not own bp_sorting, WHEN a sorter is placed → `locked`; a sorter on a street tile → `locked` (actor before tile); a bp_sorting purchase with lower opSeq in the same tick → placed; with higher opSeq → `locked`. A recipe with `unlockShift` 2 is `locked` in shift 1 and legal in shift 2. wall_wood is never locked.

**Build mode (client)**
- **AC-B11-1 [UI] [M1]** GIVEN the carrier at the centre of tile (0,0) and the cursor ray hitting tile (5,0), THEN the ghost sits on (4,0) with the "Too far" pill and LMB sends no request. A raw `Place` from 40 m is accepted by the host (no range check).
- **AC-B11-2 [Integration] [M1]** GIVEN the player holds a cursor entry with room in the panel, WHEN B is pressed, THEN the entry is stowed and Build opens; with no room, Build does not open and "No room to put it away" shows. Swim start, knockdown or mounting while in Build exits it with a toast naming why.
- **AC-B11-3 [UI] [M1; locked M2]** GIVEN each ghost state (valid, invalid, unaffordable, pending, locked), THEN the UI event log reports the state and outline style (solid / dashed / solid / solid + hourglass / solid + padlock), and a screenshot of each is retained in `production/qa/evidence/building/` with lead sign-off.
- **AC-B11-4 [Integration] [M1]** GIVEN a 15-tick deconstruct hold on a wall, WHEN released at tick 14, THEN nothing is sent; WHEN completed, THEN one `Deconstruct` with the id latched at press; WHEN the cursor leaves the wall first, THEN nothing is sent. Holding on a sorter's non-anchor tile targets the sorter; holding on the PO shows no ring. With Input's hold-as-toggle on (G7), one press starts the ring, which completes by itself and sends one `Deconstruct`; a second press before completion cancels and sends nothing.
- **AC-B11-5 [Logic] [M2]** GIVEN the cursor on a sorter, WHEN Pipette, THEN the selection is address_sorter_mk1 with the sorter's rot, even if bp_sorting is not owned (ghost locked); [M3] on a ruin it selects the ruin's def; on the PO the selection is unchanged.
- **AC-B11-6 [Integration] [M1]** GIVEN a client mirror equal to the host world, WHEN `TileLegality` runs on both for 1,000 seeded-random (def, tile, rot) triples with static carriers, THEN the reason and failing tile set are identical in every case.
- **AC-B11-7 [Logic] [M1]** R ×4 gives rot 1, 2, 3, 0, and R during a drag is ignored. GIVEN 3 Iron in the panel and 2 in the Depot with P1 at 12 m, THEN the sorter's chip shows have 5 / need 4. A Delivery refund preview for a 3-Log wall reads 1 while the host grants 2 when the Log bank is 500.
- **AC-B11-8 [Integration] [M1; drag M2]** GIVEN 300 ms RTT, WHEN P1 clicks wall_wood on 5 tiles in 5 consecutive ticks, THEN 5 `Place` requests are sent, each with its own pending ghost, none muted "Busy"; a placement press during a deconstruct hold sends nothing. [M2] GIVEN a belt drag latched at (0,0) while the carrier walks east and the cursor ends on (11,0), THEN release sends one `PlaceLine` (0,0)→(11,0); a release on the start tile sends `Place`.

**Edge cases**
- **AC-EDGE-1 [Logic, parametrised] [M2]** (a) A wall placed over a WorldItem succeeds and the item keeps its position. (b) Walls on every non-street tile around the PO apron are legal. (c) A belt line ending mid-street is legal. (d) In one tick, Deconstruct then Place on one tile both succeed; Place then Deconstruct → the Place gets `occupied`.

**Network**
- **AC-NET-1 [NET] [M2]** GIVEN two clients send Place for one tile in one tick, THEN exactly one `ConstructPlaced` reaches every peer, the loser's reply is `occupied` and uncharged, and all peers' registries have equal count and epoch.
- **AC-NET-2 [NET] [M2]** GIVEN FX-B-L and a 40-tile PlaceLine with materials for 25, THEN one `ConstructPlaced{count 25}`; on one channel the order is charge events → `ConstructPlaced` → `OpReply{placedK 25, Funds}`; construct bytes per peer ≤ 64 B.
- **AC-NET-3 [NET] [M2]** GIVEN FX-B-L and JoinState with 1,000 constructs, THEN the construct block is ≤ 16 KB and the joiner's id list equals the host's. GIVEN one dropped `ConstructPlaced`, THEN the epoch gap triggers a resync within 15 ticks and the lists are equal.
- **AC-NET-4 [NET] [M2]** GIVEN a client whose mirror shows enough Iron while the host's Depot is short, WHEN it places, THEN the reply is `insufficient`, no state changes, and the pending ghost resolves to the reason within `pendingCommitTimeout` + RTT.
- **AC-NET-5 [NET] [M2]** GIVEN `rot` 4, an out-of-range `defIndex`, a 41-tile line or a non-straight line, THEN the adapter replies `bad_request`, and Sim state, ids and epoch are unchanged.

**Experience**
- **AC-EXP-1 [MANUAL] [M2]** GIVEN bp_sorting owned, the PO Depot holding exactly 44 Plank, 52 Iron Ingot and 6 Stone (OQ-BLD-10) and `SharedBuild` off, WHEN each of 3 testers builds the 40-belt, 1-sorter, 4-inserter reference factory in a fresh shift-3 Prep, THEN each completes it within one Prep + one Delivery (spec 13 M2 gate) and the median build-mode time is ≤ 50 % of the Prep timer; in the following Delivery the median build-mode time is ≤ 15 % of Delivery.
- **AC-EXP-2 [MANUAL] [M2]** GIVEN 3 testers each placing 20 constructs including 3 invalid attempts (street, occupied, insufficient), THEN 0 of 60 placements show a ghost state at press that differs from the host reply (same-tick races excluded); with a protanopia filter, the 6 invalid ghosts (street, occupied) are identified by dashed outline and X, and the 3 unaffordable ones by solid outline plus missing chips.

## Open Questions

**Hand-backs (this GDD does not edit other GDDs).**

| ID | To | Item | By |
|---|---|---|---|
| OQ-BLD-1 | Input & Controls | Close OQ-IC-13: Deconstruct = hold RMB / LT in Build, targeting the build-cursor construct; confirm the Build hold uses F1 `holdEff` and is a G9 commit; reword H-R10-3 (moot: Interact is not live in Build). Place / PlaceLine are **not** G9 commits (B11); a `drag` def commits on release (G8 exception). **Pull a minimal Build context forward to M1** (B / LB, LMB / RT, R / RB, hold RMB / LT, Back) for B15's M1 slice. Confirm the mouse wheel in Build is belt tier only (no hotbar scroll) | Input re-review |
| OQ-BLD-2 | Player Character | Remove the P10 "Deconstruct" verb row and P11's "build mode → Deconstruct only" filter (Build has no focus); amend P14's reading of Build; mark OQ #18 adopted (B4 body check, construct wins after placement); accept static construct AABBs from min(h) to H + `heightMm`, registered at placement and removed at deconstruct and OQ #20 answered (no buildable pads in v1; PO pad ring reserved) | PC re-review |
| OQ-BLD-3 | World Generation | Provisional contract: per tile `TileType` (incl. Street / Path / Bridge), centre height (int mm, the same sample PC uses), buildable flag + `UnbuildableCause`, `StreetAxis` (NS / EW / Intersection), node id; static reserved mask (PO apron, Spawn pad ring 2.0 m, Intake outfeed lane as `outfeedLaneTiles`, which may lie in the apron); Bridge is a non-water TileType; Path tiles carry a `StreetAxis`; `approachTile` buildable, node-free, `slopePm` ≤ 194, 4-adjacent to a street; the ≥ 20 × 20 clear area must pass `TileLegality` for a 2×2 sorter and a belt on every tile; remove spec 02 §4's "terrain flattening" delta | WG GDD |
| OQ-BLD-4 | Content & Data | `BuildingDef` additions: `class` (transport / solid-building / defense / building), `solid`, `drag`, `family` (belt tier cycle), `deconstructable`, `placement.maxSlopePm` (replacing `maxSlopeDeg`), `placement.onStreet: none / crossing`, `armorClass`, `heightMm` (AABB top above H); drop `ruinRebuildRatio` (Defenses' key); `BuildingBehaviour` v1 = belt, splitter, merger, sorter, inserter, container, wall, gate, turret, alarm, vehicle_depot, post_office; validator rules: recipe input ids distinct, `salvageRatio*Pm` ≤ 1000, deferred defs unreachable (CV-GEN-19). Remove "Deconstruct" from the interaction verb catalogue if it is listed there | CDD re-review |
| OQ-BLD-5 | Health & Damage | HD1 / Dependencies: Building is the construct registrant at placement (not Defenses); Defenses reacts to `EntityDowned`; expose a read query (`TryGetHp(id) → {hp, maxHp}`) for B8's step-1 snapshot | HD review |
| OQ-BLD-6 | Defenses (unwritten) | Owns `ruinRebuildRatioPm` (500) and calls `ChargeRecipe`; ceil rounding (F1) means 1-count rebuilds lose the discount — accept or choose floor; `RestoreFromRuin` keeps behaviour config | Defenses GDD |
| OQ-BLD-7 | Belt Logistics (unwritten) | `IPlacementRule` for one automated feeder per mailbox; hand lane items, inserter held unit and sorter buffer to the B8 spill bag on removal; same-class in-place replace (rotate / Mk1 → Mk2 without deconstructing) — deferred unless Belt wants it; are walkable belts passable to enemies? | Belt GDD |
| OQ-BLD-8 | Enemy AI & Waves (unwritten) | With no open route, treat walls as passable at an HP-weighted cost so a target is always reachable; Wall Breakers pick the cheapest wall on that path | Enemy AI GDD |
| OQ-BLD-9 | Vehicles (unwritten) | Vehicle depot parking apron as a `keepClear` `IPlacementRule` (proposed 3×3 in front, may overlap streets) | Vehicles GDD |
| OQ-BLD-10 | Economy / balance (spec 11 §9.1) | The reference factory (40 belts, 1 sorter, 4 inserters) costs **44 Plank, 52 Iron Ingot, 6 Stone** by the spec 10 recipes, not "~20 Plank, 12 Stone, 40 Iron". Fix spec 11 or the recipes before the M2 gate | Economy GDD |
| OQ-BLD-11 | HUD & Notifications | CD ruling (ratified by the owner 2026-10-16): a feed line names the remover, grouped per remover per def over 2 s, only when `bySlot ≠ ownerSlot`; it never names the owner. HUD's "never credit the builder" rule concerns automation and misdeliveries, not a player's own act. The remover name follows `rollCallNames` (HUD H9); add an H-ATTR exemption for this line | HUD re-review |
| OQ-BLD-12 | Spec maintenance | Spec 04 §1.1 / §2.1 elevated variant and spec 02 "buildable pier" are superseded by spec 13 for v1; spec 06 build range check deleted by spec 13 §3 — **Closed 2026-10-16 — owner ratified** (spec files not edited) | — |

**Design questions.**

| ID | Question | Owner | By |
|---|---|---|---|
| OQ-BLD-13 | **Teammate build ghosts** (deferred past v1): if wanted, 3 client-owned fields on the player synchronizer (`buildDef u16 / none, tile, rot`, ≤ 5 Hz, 600 ms expiry, ≈ 9 B; ≤ 3.6 kbps per client worst case), Postal Blue 15 % fill with **no outline** plus a hat badge, never shown invalid. Needs the client-authority synchronizer spike | owner / network-programmer | post-v1-lite |
| OQ-BLD-14 | **Buildable spawn pads** (PC OQ20): out of v1 (owner ratified 2026-10-16); post-v1 scope stays open | owner | post-v1-lite scope review |
| OQ-BLD-15 | Is 1000 constructs (ruins included) within the 8 ms tick once Belt Logistics' segment model exists? Re-measure at M2 with the reference factory ×4 | performance-analyst | M2 |
| OQ-BLD-17 | Watch item (CD): Payday's "most built" callout (fed by `ownerSlot`) rewards build volume, which pulls against a spine where machines matter because you are not next to them | Run Structure | M2 |
| OQ-BLD-16 | Does prefix placement (B7) frustrate players compared with all-or-nothing? Playtest at M2; a knob can flip it | ux-designer | M2 playtest |

**→ becomes an ADR** (implementation, not design): the occupancy grid and registry storage; `TileLegality` layout shared by host and client; the line scan and run-encoded `ConstructPlaced` with epoch and resync (extends ADR-0008 / ADR-0011); build op admission vs the 64-op drain and 4,096-entry log (amends ADR-0007); the bank in the Sim dump and hash (ADR-0006); validity under client-authoritative movement (capsule epsilon, construct-wins, depenetration — joint with Player Character's transform ADR).
