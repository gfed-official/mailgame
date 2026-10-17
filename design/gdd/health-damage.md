# Health & Damage

> **Provisional**: designed before the M-1a two-friend greybox (systems-index GUARD overridden by owner instruction 2026-10-16); re-validate after the greybox.

> **Status**: In Review (Provisional) — /design-review 2026-10-16 NEEDS REVISION (10 blockers), revision applied; re-review decides Approved
> **Author**: owner + agents (game-designer, systems-designer, network-programmer, qa-lead, creative-director)
> **Last Updated**: 2026-10-16
> **Last Verified**: 2026-10-16
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-10-16 (CONCERNS: 2 Player Fantasy/knob wording fixes + OQ-HD-1 sharpened, applied; observation for Run Structure: the Payday "most damage" award leans competitive)
> **Implements Pillar**: 1 (the mail must flow — a knockdown costs mail, not a life), 2 (friendslop surface — readable slapstick damage); constraint: 5 (scales 1 to 8)

## Summary

Health & Damage is the Sim's single owner of hit points. It holds one HP component per carrier, goon, construct and vehicle; collects every damage and heal request into one pending list; resolves them in a published order in Simulation Core phase 10; and raises exactly one death event per entity (`PlayerDied` for carriers, `EntityDowned` otherwise) for other systems to react to. It exists so five damage sources share one rule set (who can hurt whom, how resistances apply, who goes down first), keeping combat a deterministic, readable pressure system where a knockdown costs mail, not the run.

> **Quick reference** — Layer: `Core` · Priority: `MVP (stub M-1b → M3)` · Key deps: `Simulation Core, Content & Data Definitions; consumers Player Character & Movement, Throwing & Catching, Player Combat, Enemy AI & Waves, Defenses & Construct Damage, Vehicles & NPC Drivers, Run Structure, HUD & Notifications`

## Overview

Health & Damage is the single owner of **hit points** in the Sim. Every damageable thing is registered with it: carriers, goons, constructs and vehicles. Health keeps one HP component per entity, collects every damage and heal request raised during the tick into one pending list, and resolves that list in Simulation Core phase 10 (`Combat.Resolve`) in a published order. It also applies carrier regen and heal-over-time, and raises exactly one death event per entity: `PlayerDied` for a carrier, `EntityDowned` for anything else. Five systems originate damage: Player Combat (weapons), Throwing (thrown items), Enemy AI (goon attacks), Defenses (turrets, spikes) and Vehicles (impacts). Without one owner, each would grow its own HP rules. This system makes "who can hurt whom, by how much, and who goes down first" one rule set that runs the same way every time.

Players never operate the system directly. They feel it through the HP bar, the hit flinch, being Knocked down and goons and walls going down. What follows a death belongs to other systems: the knockdown and clock-in (Player Character), ruins (Defenses), drops (Enemy AI), wrecks (Vehicles) and Run Over (Run Structure). The weapons, attack timing and knockback belong to the damage sources.

## Player Fantasy

**"Pratfall with a price tag"** (creative-director, recommended framing; indirect). Getting walloped is funny, and the bill is your bag. The HP bar is not a life counter. It counts how close you are to dropping everything you carry in front of the whole crew. You hit back with fists, a Mail Bat or a well-aimed heavy parcel, and every goon you flatten is one more route kept open. On the construct side the tone is **"hold the line"**: a wall's falling HP is a countdown that buys the Repair Hammer time.

**Anchor moment.** You cross the yard with a full bag. A Barbarian clips you and the bar flips to Stamp Red. Do you sprint for the mailbox or back off and let the bar refill? You gamble, get Knocked down, and your parcels spray across the grass. Ten seconds later you clock back in and find a teammate has already scooped your pile and finished the drop.

**It must never feel like:** (1) managing attrition: no heal maths, no hoarding Bandages, no babysitting the bar between fights (one glance tells you whether to press on or back off); (2) grinding health sponges: regular goons drop in a few satisfying hits, and a Giant is a team job; (3) sitting in a penalty box: the real loss is the spilled bag, not the wait.

**Pillars.** Pillar 1, *"Automation, vehicles, and combat are all in service of throughput and accuracy"*: a knockdown costs mail, not a life. Pillar 2, *"The game reads as a silly co-op delivery sim"*: damage is deterministic, readable slapstick, with no crits and no random rolls. Pillar 5, *"a solo run is winnable and an 8-player run is busy rather than trivial"*: HP and regen are identical at every player count, and scaling happens on the enemy side (raid budget, Enemy AI / Run Structure), never through player HP. A solo carrier has no one to cover a retreat (regen from 25 to full takes ≈ 42 s), so solo knockdown frequency is a greybox playtest question (OQ-HD-1).

## Detailed Design

### Core Rules

**HD1 — HP components.** Every damageable entity has exactly one HP component held by Health: `hp` and `maxHp` (int32 HP), `registeredSeq`, `kind` ∈ {Carrier, Enemy, Construct, Vehicle}, `armorClass` (data id), life ∈ {Up, Downed}, an **invulnerability reason mask**, `lastDamageTick`, and for carriers up to `health.hotSlots` heal-over-time slots. Owners register and unregister their entities through Health's API (`Register(id, kind, baseHp, armorClass, maxHpMultPm, hpStat?)`, `Unregister(id)`); `Register` sets `lastDamageTick = T − regenDelayTicks`:
- Player Character registers carriers on join.
- Enemy AI registers goons at spawn, passing a Mega multiplier.
- Building and Defenses register constructs at placement; the PO is registered at run start.
- Vehicles registers vehicles at spawn.

Anything without a component cannot be damaged: mailboxes, houses, WorldItems, mail, and items on belts. That is how "goons never attack mailboxes" is guaranteed by construction.

An entry applies only if it was enqueued after the target's current `Register` (both stamped from one monotonic counter, not the tick). An id that is unregistered and re-registered in one tick (a slot reused by leave then join) never receives the previous occupant's queued hits; they drop as `dropGone`. Entries where the leaver is the *actor* still apply. Run Structure maps slot → account for credit.

**HD2 — Damage and heals are queued; no pending-list entry mutates HP before phase 10.**
- **Who calls and when.** Any system may call these from its own phase 0–9 (Sim Core R9: synchronous `IHealthApi` inside the caller's phase): `ApplyDamage(targetId, amount, damageTag, sourceKind, actorId, causeRef)`, `ApplyHeal(targetId, amount, sourceKind, actorId)` and `ApplyHot(targetId, total, durationTicks, actorId)` (sourceKind `Consumable` implied).
- **Validation at enqueue.** An `amount ≤ 0` is rejected and asserted in Debug.
- **Fields.** `actorId` may name a Dead or Frozen carrier; Throwing requires this. `causeRef` holds the `flightId` or the weapon def.
- **Late calls.** A call from phase 11 onward joins the T+1 list and is counted as `lateDamage` (Sim Core R3).
- **Synchronous exceptions.** `Restore` (HD11), `SetInvulnerable` (HD6) and `Register`/`Unregister` act immediately in the caller's phase.
- **Read-only queries** may be made at any time: `Hp`, `MaxHp`, `HpPct` (F5), `IsDowned`, and `CanHeal(id, kind ∈ {Instant, HoT})`. It is false when the target is Downed, Frozen or at full HP; for `HoT` also when every HoT slot is full (full slots never refuse a Bandage).

**HD3 — Who may damage whom (allow-table, code constant, default-deny).**

| Target kind | Damage accepted from `sourceKind` | Heals accepted from |
|---|---|---|
| Carrier | `EnemyAttack` only | `Consumable` |
| Enemy | `PlayerWeapon`, `Thrown`, `Turret`, `Spike`, `VehicleImpact` | none |
| Construct (incl. PO) | `EnemyAttack` only | `Repair`, `PrepRegen` |
| Vehicle | `EnemyAttack` only | `Repair` (provisional, Vehicles) |

- **Rejected entries.** Any other pair is dropped, counted as `dropIllegal` and asserted in Debug, as is any **damage** entry where `actorId == targetId`. A self-heal (Bandage on yourself) is legal.
- **What the table encodes:**
  - friendly fire is off;
  - thrown items, turrets, spikes and vehicles never hurt players (Throwing's bonk is never a damage call);
  - player weapons never hurt constructs;
  - goons never hurt goons;
  - there is no self-damage, fall damage or drowning damage (spec 13).
- **Tuning boundary.** The table is a design rule, not a tuning value, so it is not in data.

**HD4 — Resolve (phase 10).** Health runs these steps once per tick:
- **Step 0 — max HP refresh.** If the StatSheet version changed, recompute `maxHp` for every component bound to a Stat, in ascending id (F2).
- **Step 1 — the pending list.** Entries resolve in **append order**: late entries from T−1 first, then entries in raise order: ascending phase, composition order within a phase, then each owner's internal order (0 hit reports, 4 thrown, 6 vehicle impacts, 7 goon attacks; Defenses at its bound phase, Dependencies contract 4). The list is never sorted by target, because "first lethal" means first in time. Heals share the list, so a Bandage used in phase 0 lands before a goon hit raised in phase 7. For each entry:
  1. **Target unregistered** (or registered after the entry): damage drops as `dropGone`, a heal as `dropHeal`.
  2. **Target Downed:** damage drops as `overkill` (the second-lethal-hit case, HB7); a heal drops as `dropHeal`.
  3. **Allow-table fails:** drop, counted as `dropIllegal` (HD3).
  4. **Damage on an invulnerable target:** drop, counted as `dropInvuln`. A Protected carrier also gets one cosmetic `DamageBlocked` notice (Network).
  5. **Heal or HoT on a Frozen target:** drop, counted as `dropHeal`.
  6. **Damage otherwise:** `final = F1`; if 0, no-op. Else `hp = max(0, hp − final)`, `lastDamageTick = T`, and raise Internal `DamageApplied{target, actor, sourceKind, damageTag, causeRef, raw, dealt, killed}`. `dealt` is the HP actually removed.
  7. **Heal otherwise:** `hp = min(maxHp, hp + amount)`. `ApplyHot` occupies a free slot; k = 1 is evaluated in step 2 of the same tick (Berries gain 0 then, first +1 at k = 6). With no free slot it drops as `dropHotFull` (the item was already consumed: accepted).
  8. **Death event.** If an entry takes `hp` to 0: set life to Downed, clear the HoT slots, and raise the death event inline, at that entry's position in the log (HD5).
- **Step 2 — regen and HoT.** Apply regen (F3) and HoT ticks (F4) to Up carriers that are not Frozen.
- **Step 3 — HP changes.** Raise one Internal `HpChanged{id, hp, maxHp}` per component whose HP changed or that `Restore` marked dirty this tick, including components that went Downed (Network O1 depends on it). Ids unregistered before phase 10 are skipped. The replicators (Network) and HUD read these.

**HD5 — Death events (HB7).** Exactly one per Up → Downed transition. The first lethal entry is the cause.
- **Carriers:** `PlayerDied{playerId, causeActorId, causeKind, causeDefId}`. `causeDefId` is the goon's EnemyDef, or 0, for the HUD feed's goon type. This drops the locked `position` field (hand-back HB-HD-1).
- **Everything else:** `EntityDowned{targetId, targetKind, causeActorId, causeKind, causeRef}`.
- **Reactions run in phase 11** (`Combat.React`), and Health does none of them: PC knocks the carrier down (P15); Defenses makes a ruin, or reports that the PO fell; Enemy AI rolls drops on the `drops` stream and despawns; Vehicles wrecks and spills. Each owner unregisters its entity in phase 11. **Health is RNG-free** (hand-back HB-HD-2 to Simulation Core).

**HD6 — Invulnerability is a reason mask set by its owners.**

| Reason | Set and cleared by | Meaning |
|---|---|---|
| `Protected` | Player Character | spawn protection after clock-in, `player.spawnProtectMs` 3000 (confirmed; PC owns the timer), or after rejoin, 1500 |
| `Frozen` | Player Character | on break (disconnected) |
| `Sheltered` | Vehicles (provisional) | the carrier is in a vehicle seat; goons must target the vehicle (spec 13 §4.4). Turret operators are **not** sheltered |

- **What any set reason does:** it voids damage. Health honours the mask and owns no timer. Every flag is read at resolve (phase 10), not at enqueue, so owners toggle them in a phase where that is what they mean. Vehicles toggles `Sheltered` in phase ≤ 6 or in phase 11. PC and Vehicles depend on Health, so they push the flags down (`SetInvulnerable(id, reason, on)`); Health never reads their state.
- **What only `Frozen` (and Downed) does:** it also pauses regen and voids heals. Setting `Frozen` clears the HoT slots inside the `SetInvulnerable` call. F3's `k` keeps counting while Frozen, so regen resumes at once on thaw if the delay has already passed.
- **What `Protected` does not do:** it does not stop regen.
- **Enemy targeting** of protected carriers is Enemy AI's call (PC OQ15). If goons swing anyway, they "boing" (`DamageBlocked`).

**HD7 — Spawn protection ends on a damaging action.**
- **What counts.** For weapon and turret hits: a `DamageApplied` with `actor` equal to the protected carrier and `dealt > 0` (a turret's actor is its operator). Thrown hits: below.
- **What does not count:** a whiff, a dropped entry, a weapon or turret overkill on a goon already down, a bonk, or throwing mail.
- **How it ends.** For weapon and turret hits, PC reads `DamageApplied` in phase 11 and clears `Protected`. The flag is therefore constant throughout phase 10, and the order of entries cannot change who gets hurt.
- **Thrown hits are contact-based.** Throwing's locked contract clears protection in phase 4 on any HIT_ENEMY, whatever Health later deals: an overkill or `takenPm 0` throw still ends it. Goon hits on the thrower later in the same tick land. Clearing twice is harmless.
- **Contract wording.** This settles PC P17's "an attack" as "a weapon or turret hit with `dealt > 0`, or a HIT_ENEMY throw" (HB-HD-3).

**HD8 — Knockdown is HP 0, nothing else.** There is no downed or revive state, no non-lethal knockdown and no stun-to-floor. Answers PC OQ7: a solo carrier cannot be revived, and the cost of a knockdown is time plus the pile (PC P15).

**HD9 — Regen.**
- **Who regenerates.** Only carriers that are Up and not Frozen, at `player.regenHpPerS` (2), once `player.regenDelayMs` (5000) has passed since the last `dealt > 0` hit (F3).
- **What does not reset the delay:** heals, dropped entries and blocked hits.
- **Protection.** Regen runs while Protected.
- **Everything else.** Goons, constructs and vehicles do not regenerate; the Repair Hammer is the construct loop. The PO's 5 HP/s during Prep is Defenses calling `ApplyHeal(PrepRegen)`, gated by Run Structure's phase.

**HD10 — Healing items.**
- **Who does what.** The item-use owner (Player Combat, provisional) checks `CanHeal`, consumes the item, then calls `ApplyHeal` for a Bandage (50) or `ApplyHot` for Berries (25 over 5 s). Content provides `consumable.healInstant`, `healOverTime` and `duration`.
- **Refusals.** When `CanHeal` is false the use is refused and the item is not consumed.
- **Partial overheal.** It consumes the item and clamps to `maxHp`.
- **HoTs.** Up to `health.hotSlots` (3) run in parallel, and each slot heals independently. Damage does not cancel a HoT. A knockdown or Frozen clears every slot.

**HD11 — Restore.** PC calls `Restore(playerId)` at clock-in and at a fresh join, never at a rejoin, which resumes the frozen HP. It is synchronous, inside PC's phase, because it is not damage. It sets `hp = maxHp`, life Up, clears the HoT slots and sets `lastDamageTick = T − regenDelayTicks`, so regen is already due. Calling it on a non-carrier is rejected (Debug assert). It flags the component so replication sends full HP that tick (Network O3). Restore is the only Downed → Up path, and only carriers have it.

**HD12 — Attribution and counters.**
- **Payday "most damage"** (spec 01) is Run Structure summing `dealt` per actor account from `DamageApplied`. Overkill cannot inflate it, and a Frozen actor still scores.
- **Telemetry** reads `raw` and `dealt`.
- **Counters** (Sim Core R31): `dropGone`, `dropIllegal`, `dropInvuln`, `dropHeal`, `dropHotFull`, `overkill`, `pendingOverflow`. `lateDamage` is Sim Core's.

**HD13 — Bounds and determinism.**
- **Pending-list cap.** The list holds `health.pendingCap` (1024) entries per tick, late T−1 entries included. Overflow drops the entry, counts `pendingOverflow` and asserts in Debug. The tick never allocates (Sim Core R32).
- **Asserts in tests.** Debug asserts go through `SimAssert`; tests that feed rejected input install a capture sink that counts asserts instead of aborting (hook: hand-back to Sim Core, ADR-0001).
- **No randomness.** There are no critical hits, no random damage and no damage over time on carriers. The same inputs always give the same HP. The only rounding is half-up integer division (F1's `Pm.Scale`, F2's single int64 expression).

### States and Transitions

| State | Entered by | Event / guard | Next |
|---|---|---|---|
| (none) | — | `Register` | Up, `hp = maxHp` |
| Up | Register, Restore | an entry takes `hp` to 0 (not invulnerable, allow-table passes) | Downed + one death event (HD5) |
| Up | — | damage while any invulnerability reason is set | Up (dropped; `DamageBlocked` if Protected) |
| Downed | lethal entry | further damage or heal | Downed (dropped; `overkill` for damage, `dropHeal` for heals) |
| Downed (carrier) | — | `Restore` (PC clock-in) | Up, full HP |
| Downed (other kinds) | — | owner's `Unregister` in phase 11 | (none) |
| any | — | `Unregister` (despawn, deconstruct, leave, Run Over) | (none); queued entries drop as `dropGone` |

Overlays on Up: the invulnerability mask (HD6), regen-eligible once the delay has passed (HD9), and the occupied HoT slots.

### Interactions with Other Systems

| System | In → Health | Health → out |
|---|---|---|
| Simulation Core | phase 10 binding; event log; counters; Stat version | `DamageApplied`, `HpChanged`, `PlayerDied`, `EntityDowned` (Internal unless the Network section says otherwise) |
| Content & Data | `player.*` and `health.*` balance keys; `EnemyDef.hp`, `ConstructDef.hp`, `VehicleDef.hp`; `armorClass`, `DamageTagDef`, the `damageTakenPm` matrix; consumable fields; Stat `ConstructHpMultPm` | — |
| Player Character | `Register`/`Unregister` carrier, `Restore`, `SetInvulnerable(Protected/Frozen)` | `PlayerDied` (P15); `DamageApplied` (protection end HD7, flinch) |
| Throwing & Catching | `ApplyDamage(F6 amount, tag thrown, Thrown, thrower, flightId)` in phase 4 | `DamageApplied` / `HitConfirm` carrying `flightId` |
| Player Combat *(provisional)* | `ApplyDamage(weapon damage already × MeleeDamageMultPm, tag, PlayerWeapon, attacker, weaponDef)` from validated hit-report ops in phase 0; `CanHeal` + `ApplyHeal`/`ApplyHot` for consumables | `DamageApplied` (hit confirm) |
| Enemy AI & Waves *(provisional)* | `Register(Mega ×2.5)`; `ApplyDamage(def damage already × Mega 1.5, tag, EnemyAttack, goon, defId)` in phase 7, already split per target (e.g. Wall Breaker 150 vs walls, gates and belts, 20 otherwise) | `EntityDowned` → drops, despawn |
| Defenses & Construct Damage *(provisional)* | `Register` constructs (`hpStat = ConstructHpMultPm`); turret and spike `ApplyDamage` (actor = operator, or none); `ApplyHeal(Repair/PrepRegen)` | `EntityDowned` → ruin, spill, or "PO fell" (`PODestroyed` is Defenses' event) |
| Vehicles *(provisional)* | `Register` vehicles; `ApplyDamage(VehicleImpact)` on goons; `SetInvulnerable(Sheltered)` | `EntityDowned` → wreck spill (Grid Inventory) |
| Run Structure *(provisional)* | Prep phase flag for `PrepRegen` (via Defenses) | `DamageApplied` → Payday "most damage"; `EntityDowned` (PO) → Run Over |
| HUD & Notifications | — | own `hp`/`maxHp` (OwnHp) and teammates' HP % (Network) — answers HN-7's HP item |

## Formulas

*Specialist: systems-designer. Integer-only (ADR-0006). `Pm.Scale(x, pm) = (x × pm + 500) / 1000`, computed in int64 and rounded half-up, is the one shared rounding primitive: sources use it for attacker-side multipliers and Health uses it in F1. F2 applies the same half-up rule once over a single int64 product.*

**F1 — Final damage.** The `finalDamage` formula is defined as:

`finalDamage = takenPm == 0 ? 0 : max(health.minDamage, Pm.Scale(amount, takenPm))`, where `takenPm = damageTakenPm[damageTag][armorClass]` (default 1000)

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| amount | int32 | 1–100 000 | calculated by the source | attacker-scaled damage: weapon/EnemyDef/turret value × the source's own multipliers (MeleeDamageMultPm, TurretDamageMultPm, Mega 1.5×), already rounded by `Pm.Scale` |
| damageTag | id | data set | data file (`DamageTagDef`) | e.g. `blunt`, `blade`, `pick`, `thrown`, `breach`, `projectile` |
| armorClass | id | data set | data file (EnemyDef / ConstructDef / VehicleDef) | e.g. `goon`, `wood_wall`, `stone_wall`, `belt` |
| takenPm | int | 0–4000 | data file (sparse matrix) | target-side resistance (< 1000) or vulnerability (> 1000); 0 = immune |
| health.minDamage | int | 0–1 | data file (PROPOSED 1) | a positive hit never rounds to 0 |

**Output range:** 0 to 400 000. Normal play gives 5 (Fists) to 113 (Mega Wall Breaker vs Stone Wall). `dealt = min(finalDamage, hp)`.
**Examples:**
- Wall Breaker vs Stone Wall: `Scale(150, 500) = 75` (spec 05 §4's −50 %).
- Mega Barbarian: Enemy AI submits `Scale(10, 1500) = 15`, and the matrix default leaves it at 15.
- Axe with Union Muscle: `Scale(15, 1500) = 23`.
- Pickaxe vs Tank: `Scale(12, 2000) = 24` (Tank is post-v1).
- Thrown heavy: Throwing F6 gives 40, and Health leaves it at 40.

**F2 — Max HP.** The `maxHp` formula is defined as:

`maxHp = (baseHp × maxHpMultPm × statPm + 500 000) / 1 000 000` (int64, one half-up rounding), where `statPm = hpStat ? StatSheet[hpStat] : 1000`

Mid-run change (Stat version bump, HD4 step 0): `hp' = clamp(hp + (maxHp' − maxHp), 1, maxHp')` for Up components (keep-damage-taken). For Downed components `maxHp` updates and `hp` stays 0.

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| baseHp | int32 | 1–10 000 | data file | `player.maxHp` 100; EnemyDef.hp 30–400; ConstructDef.hp 80–3000; VehicleDef.hp 120–600 |
| maxHpMultPm | int | 1000–4000 | registrant | Mega `enemy.megaHpMultPm` 2500 (Enemy AI's key); otherwise 1000 |
| statPm | int | 500–3000 | StatSheet | `ConstructHpMultPm` for constructs (Reinforced 1400); 1000 otherwise |

**Output range:** 30 (Wall Breaker) to 4200 (PO with Reinforced) in play; hard maximum 120 000 (int64 intermediate). Carriers are fixed at 100, since there is no MaxHp Stat in v1.
**Examples:**
- Mega Giant: `Scale(400, 2500) = 1000`.
- Wooden Wall: 300 → 420 under Reinforced. A wall at 100/300 when Reinforced is drafted becomes 220/420: the perk is also a free partial repair (flag to Defenses).
- A decrease never kills: the clamp floor is 1.

**F3 — Carrier regen.** The `regenGain` formula is defined as:

`regenGain(T) = ⌊r × k / 30⌋ − ⌊r × (k − 1) / 30⌋`, where `k = T − start + 1`, `start = lastDamageTick + regenDelayTicks`, valid when `T ≥ start`; `regenDelayTicks = ⌈player.regenDelayMs × 30 / 1000⌉`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| r | int | 0–10 | data file `player.regenHpPerS` (spec 2) | HP per second |
| player.regenDelayMs | int | 1000–15 000 | data file (spec 5000) | quiet time after the last `dealt > 0` hit |
| k | int | ≥ 1 | calculated | ticks since regen started |

**Output range:** 0 or 1 HP per tick (for r ≤ 30), exactly `r` HP per 30 ticks long-run, clamped at `maxHp`.
**Example:** at r = 2, +1 HP every 15 ticks. The first point lands at `lastDamageTick + 164` (≈ 5.5 s), and 25 → 100 takes 5 s + 37.5 s ≈ 42.5 s. A carrier that joins or clocks in has `start` already due.

**F4 — Heal over time.** The `hotGain` formula is defined as:

`hotGain(k) = ⌊H × k / D⌋ − ⌊H × (k − 1) / D⌋`, for k = 1…D, summed across occupied slots, then clamped at `maxHp`

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| H | int | 1–200 | data file (`consumable.healOverTime`; Berries 25) | total heal |
| D | int | 1–900 | data file (`consumable.duration` → ticks, ceil as in F3; Berries 150) | duration in ticks |
| k | int | 1…D | calculated | ticks since the slot started |

**Output range:** exactly H per slot over D ticks. Overheal is lost.
**Example:** Berries give +1 HP every 6 ticks, 25 in total. Two Berries eaten together give +2 every 6 ticks (50 over 5 s).

**F5 — HP percent (replicated).** The `hpPct` formula is defined as:

`hpPct = hp == 0 ? 0 : max(1, (100 × hp + maxHp − 1) / maxHp)` (ceil, uint8, ≤ 100)

| Variable | Type | Range | Source | Description |
|---|---|---|---|---|
| hp, maxHp | int32 | 0–maxHp, 1–120 000 (F2's maximum) | Health | current and maximum HP |

**Output range:** 0–100. A living entity never shows 0 %, and only `hp == 0` shows 0. At carrier `maxHp` 100 it equals HP exactly.
**Example:** Giant at 3/400 → 1 %; wall at 299/300 → 100 %.

**Referenced, not owned:** thrown damage (Throwing F6: 5 × classPts = 5/15/40); weapon and goon damage values (spec 05, owned by Player Combat and Enemy AI); vehicle impact `10 × speed/5 m/s` (Vehicles); turret and spike rates (Defenses). Spikes submit whole hits of at least 1 HP (e.g. 15 per 30 ticks), never per-tick fractions, which `minDamage` would inflate.

## Edge Cases

### Same-tick ordering
- **If two lethal entries hit one carrier in a tick**: the first in append order raises the single `PlayerDied` and takes the kill credit; later ones count as `overkill` (HB7). The same applies to every kind.
- **If a Bandage (phase 0) and a lethal goon hit (phase 7) land in one tick**: the heal applies first, because the list is FIFO. A carrier at 10 HP who uses a Bandage survives a 40-damage Giant hit at 20 HP.
- **If a lethal hit and a heal raised later in the same tick target one carrier**: the target is Downed when the heal is reached, so the heal drops. The item was already consumed by its owner (accepted: the order is published).
- **If a goon dies earlier in the list and a thrown hit on it comes later**: the thrown hit is `overkill` and the flight stays HIT_ENEMY (Throwing). No credit (`dealt` 0), but the thrower's protection already ended on contact in phase 4 (HD7).
- **If damage lands on the tick regen would start**: step 1 runs before step 2, so the reset wins and no regen point is added.
- **If a protected carrier's own weapon hit and a goon hit on them land in one tick**: the goon hit is dropped, because a weapon hit clears protection only in phase 11 (HD7), whatever the list order. If the carrier's hit was a HIT_ENEMY **throw**, protection was already cleared in phase 4 and the goon hit lands.
- **If a `Combat.React` reaction raises damage** (e.g. a future wreck blast): it joins T+1's list (Sim Core R3, `lateDamage`).

### Zero, caps and odd inputs
- **If `amount ≤ 0` is submitted**: it is rejected at enqueue, nothing is queued, and Debug asserts.
- **If `takenPm` is 0**: `finalDamage` is 0. No `DamageApplied` is raised, the regen delay is not reset, and a weapon or turret hit does not end protection (a throw already did, HD7).
- **If the pending list is full**: the new entry is dropped, `pendingOverflow` is counted and Debug asserts. Earlier entries are unaffected.
- **If `ConstructHpMultPm` decreases** (a stamp or perk expiring): F2's delta rule applies (400/420 → 280/300), with a floor of 1, so a Stat change never destroys a construct.
- **If a heal or HoT targets a carrier at full HP**: `CanHeal` is false and the owner refuses the use without consuming the item. If the HP is already queued to change this tick, `CanHeal` reads the start-of-tick HP and the extra heal clamps (accepted).
- **If all HoT slots are full**: `CanHeal(id, HoT)` is false and Berries are refused; a Bandage still applies. If several carriers feed one target Berries in the same tick, past the free slots, the extra `ApplyHot` entries drop as `dropHotFull` and those items are lost (accepted).

### Life-state interactions
- **If an entry targets an entity unregistered earlier in the tick** (despawned or deconstructed in phase 0): it is dropped as `dropGone`.
- **If a carrier disconnects (Frozen) with damage queued that tick**: PC's flag is set in phase 0, so the damage is dropped. HoTs are cleared and regen pauses until Alive. A reconnect resumes the HP at its frozen value (no `Restore` on a rejoin).
- **If a carrier is Frozen or Dead as the `actorId`**: the hit still applies and counts toward their Payday damage. A Frozen actor's `HitConfirm` reaches no peer; a Dead, connected actor still gets it.
- **If a carrier mounts a vehicle seat with a goon swing queued**: Vehicles sets `Sheltered` in phase 6, before resolve, and the swing on the carrier is dropped. Enemy AI must re-target the vehicle (spec 13 §4.4). A wreck ejects the occupant and clears `Sheltered` in phase 11, so T+1 hits land.
- **If a slot is reused in one tick** (leave then join): the joiner never receives the leaver's queued hits (HD1 `registeredSeq`).
- **If a damaged construct shows 100 %** (299/300 under F5's ceiling): it is still "damaged" for replication, which keys on `hp < maxHp`, never on `hpPct`.
- **If the PO reaches 0**: it raises `EntityDowned{Construct}` like any construct. Defenses reports the PO fallen instead of making a ruin, and Run Structure ends the run. Health has no special case.
- **If a carrier stands in a Wall Breaker blast to get sent home** (deliberate knockdown): allowed; Health cannot tell intent. The time-saving cost model is PC OQ26 (`respawnMs`, the pile).

## Dependencies

**Sim/Presentation split.** Health & Damage lives **entirely in the Sim** (`PerformativeMail.Sim/Health/`, Core layer, plain C#, no Godot). It has no Presentation part of its own: hit sparks, flinches, damage numbers, HP bars and the knockdown beat are rendered by the HUD and by the Godot views of Player Character, Enemy AI and Defenses, from the events and replicated values below.

| System | Direction | Hard/Soft | Status | Interface |
|---|---|---|---|---|
| Simulation Core | upstream | Hard | In Review (Provisional) | phase 10 `Combat.Resolve`, phase-11 cursor readers, event log, counters, R9 sync API rule, HB7 |
| Content & Data Definitions | upstream | Hard | In Review (Provisional) | balance keys, `hp` fields, `armorClass`/`DamageTagDef`/`damageTakenPm` (new — HB-HD-4), consumable fields, `ConstructHpMultPm` |
| Player Character & Movement | downstream | Hard (M-1b stub) | In Review | HD1, HD5, HD6, HD7, HD11; consumes `PlayerDied` |
| Throwing & Catching | downstream | Soft (M-1b) | In Review | `ApplyDamage(Thrown)` + `flightId` |
| Player Combat | downstream | Hard for M3 | not written — provisional | hit reports → `ApplyDamage(PlayerWeapon)`; consumables |
| Enemy AI & Waves | downstream | Hard for M3 | not written — provisional | register goons, `ApplyDamage(EnemyAttack)`, `EntityDowned` → drops |
| Defenses & Construct Damage | downstream | Hard for M3 | not written — provisional | register constructs, turrets, spikes, repair; `EntityDowned` → ruin/PO |
| Vehicles & NPC Drivers | downstream | Soft (VS) | not written — provisional | register vehicles, impacts, `Sheltered` |
| Run Structure | downstream | Soft | not written — provisional | Payday damage tally, PO-down → Run Over, Prep flag |
| HUD & Notifications | downstream | Soft (M3) | In Review | own HP and teammates' HP % (HN-7) |
| Grid Inventory | indirect | — | In Review | death and wreck spills are called by PC, Defenses and Vehicles, never by Health |

**Provisional contracts for undesigned consumers:**
1. Sources submit attacker-scaled amounts and Health never applies attacker perks.
2. Each owner registers and unregisters its own entities.
3. Every death reaction runs in phase 11.
4. Defenses binds a phase from 0 to 9 for turrets and spikes (recommended: phase 7 after Enemy AI in composition order, since it needs current goon positions; Defenses' GDD decides).
5. Melee validation (range, cooldown, target alive) belongs to Player Combat's op handler, not Health.

## Network

*Specialist: network-programmer. Vocabulary is spec 06 §4's: **E** reliable event, **S** unreliable snapshot. Godot 4.7.1 high-level multiplayer behaviour is NOT SOURCEABLE in `docs/engine-reference/godot/`, so this section is a contract on the adapter (ADR-0011), and the items marked SPIKE need the pre-M0 multiplayer spike.*

**Authority.** The host computes all HP, regen, HoT, invulnerability and resolution. A melee hit report from the owning client (Player Combat's op) carries `{seq, weaponSlot, targetId}` and **no damage value**; the host derives the amount (spec 13: client-reported hits, no rewind buffer). Health sends no client → host traffic.

**Replicated state.** Health raises at most one entry per (target, tick) and per (attacker, target, tick). It never broadcasts per hit.

| State | Strategy | Audience | Rate | Size |
|---|---|---|---|---|
| `OwnHp{delta i16, hp u16, causeKind u8, srcYaw u8, stamp u8}` | E | the carrier only | one net-delta accumulator, sent when now − lastSend ≥ gap (`ownNoticeGapTicks` 4 if it holds damage, else 15), so a hit after a quiet spell goes at once; cause and `srcYaw` from the largest `dealt` (sentinel if none) | 7 B |
| `PlayerHpBatch[{idx, hpPct u8, stamp u8}]` | E, one message per flush | everyone | every `hpBatchTicks` 15 (2 Hz), dirty players only, change ≥ `hpPctStep` 5 from the last value sent, or crossing 25 % (`sent ≤ 25 ≠ now ≤ 25`) or 100; Restore, heal-to-full and reaching 0 bypass the gap (extra message) | 8 + 3n B |
| `HitConfirm{targetId u16 (Networking id map, Sim Core R22), amount u16 (= `dealt`, saturating), flags u8 (killed, aoe, capped), flightId u16}` | E | the attacker only | ≤ `hitConfirmPerSec` 8 per fixed 30-tick window; the rest fold into one `capped` message at window end (targetId 0, summed amount, killed if any) | 7 B |
| `DamageBlocked{causeKind, srcYaw}` (the "boing") | E | the protected carrier | ≤ 2 Hz while Protected | 2 B |
| Goon HP % | S, inside Enemy AI's snapshot (F5) | everyone | Enemy AI's rate | 1 B per goon |
| Construct HP % | inside Defenses' messages, while damaged plus one on return to full | everyone | ≤ 4 Hz per construct | 1 B + id |
| `DamageApplied`, `HpChanged`, `PlayerDied`, `EntityDowned` | Internal (not sent) | — | — | — |

- **Deaths reach clients through their owners' events:** PC's `CarrierKnockedDown`, Enemy AI's despawn, Defenses' ruin.
- **Flinch.** PC's existing 2 B flinch event, raised from `DamageApplied`.
- **Damage numbers** go to the attacker only; turrets and spikes show none.
- **Goon flinch** is derived client-side from the snapshot's HP % drop.

**Budget (per client down, +8 B framing assumed — SPIKE).** Own HP ≈ 0.9 kbps, `HitConfirm` ≈ 1.0, flinch ≈ 1.3 (PC's rate, ≈ 16/s assumed), HP batch ≈ 0.64, `DamageBlocked` ≈ 0.16: **worst case ≈ 4.0 kbps** (10 % of spec 06 §4.2's 40 kbps), typical raid ≈ 1.5. Goon and construct HP % count in Enemy AI's and Defenses' budgets. Cut order if Networking & Session needs room: flinch gap → `hpPctStep` 10 → `hitConfirmPerSec` 4. Own HP and knockdowns are never cut.

**Ordering.**
- **O1.** Health, PC and Throwing E messages share one reliable-ordered channel (SPIKE), so the final `OwnHp{hp 0}` precedes `CarrierKnockedDown`, and PC's clock-in event precedes the same tick's `Restore` messages.
- **O2.** On death the trailing own notice is flushed into that final message. Batches read the current HP when built and never replay a queued value.
- **O3.** `Restore` forces an `OwnHp` and batch entry that tick.
- **O4.** Clients apply HP messages in O1 arrival order and never compare stamps (8 bits cannot order a > 127-tick silence). `stamp = tick & 0xFF` only discards messages built before the client's JoinState tick, within 127 ticks of joining.
- **O5.** A client ignores an hp > 0 batch entry for a player it shows as Dead.

**JoinState / RejoinState.** Health owns no block of its own, only encodings inside the owners' blocks: own exact `hp u16` in the self block, teammates' `hpPct` in PC's public block, goons' in Enemy AI's, and damaged constructs `{id, hpPct}` in Defenses' — 2 B + 1 B per teammate + 1 B per goon + (id + 1) B per damaged construct (ids from the Networking id map). Regen timers, HoT slots and `lastDamageTick` are host-only. Clients reset a slot's HP view on PC's join and leave events.

**Disconnect / host loss.**
- **Frozen:** invulnerable from the tick PC sets it. The transport's disconnect-detection delay leaves a lagging peer hittable until then (SPIKE).
- **Host loss:** no migration, so Health has nothing to hand over.

**SPIKE items:**
- Cross-kind ordering on one reliable channel.
- Head-of-line blocking at 5 % loss: if it is bad, moving flinch, `HitConfirm` and `DamageBlocked` to unreliable-with-stamps deviates from spec 06's "damage events reliable" and needs an owner call.
- The +8 B framing.
- One batched reliable packet per peer per tick.
- The disconnect-detection timeout.

## Tuning Knobs

Balance keys use Content's camelCase convention (`player.respawnMs`). Safe range here is Content's data-file Warning range; the Formulas tables give the hard (Error) range. "Spec" means the value comes from design/spec; PROPOSED values are new.

| Knob | Default | Safe range | Affects | Too low | Too high | Owner |
|---|---|---|---|---|---|---|
| `player.maxHp` | 100 (spec) | 50–200 | carrier survivability | knockdowns from 3–4 Barbarian hits, solo unwinnable | raids stop threatening carriers (Pillar 1 pressure lost) | Health |
| `player.regenDelayMs` | 5000 (spec) | 2000–10 000 | how long a carrier must break contact | regen out-heals Barbarians in melee | retreating becomes attrition management (anti-goal 1) | Health |
| `player.regenHpPerS` | 2 (spec) | 0–6 | recovery time (≈ 42 s from 25 to full) | Bandages become mandatory (attrition) | consumables irrelevant | Health |
| `player.spawnProtectMs` / `…RejoinMs` | 3000 / 1500 (PROPOSED, confirmed here) | 1500–4000 / 500–3000 | anti spawn-camping at the PO pad | knocked down on arrival | raid camping on the pad from inside the bubble | Player Character (key); value confirmed by Health |
| `health.hotSlots` | 3 (PROPOSED) | 1–4 | Berry stacking | wasted items, frustration | burst heal from eating 4+ at once | Health |
| `health.minDamage` | 1 (PROPOSED) | 0–1 | rounding floor | a resisted chip hit does nothing | — | Health |
| `damageTakenPm[breach][stone_wall]` | 500 (spec) | 250–1000 | Stone Wall vs Wall Breaker | Wall Breakers irrelevant | Stone Wall pointless over Wooden | Health matrix (values with Defenses) |
| `damageTakenPm[pick][tank]` | 2000 (spec; Tank post-v1) | 1000–3000 | Pickaxe vs Tank niche | — | Tank trivial | Health matrix |
| `health.pendingCap` | 1024 (PROPOSED) | 256–4096 | correctness bound, not balance | overflow in a big raid | memory | Health |
| Network knobs | see Network | — | bandwidth | — | — | Health |

Not owned here: `respawnMs` (PC / Content), Mega multipliers (Enemy AI), `ConstructHpMultPm`, `MeleeDamageMultPm` and `TurretDamageMultPm` (Content Stats, applied by the source), and every weapon, goon, turret and construct HP or damage value (their owners). **Interaction:** `regenDelayMs` and the goon attack rate interact. If the delay drops below a goon's attack interval (Giant 2 s), carriers can regen while in melee with it.

## Visual/Audio Requirements

*Category Core: this section is optional, and `art-director` was not consulted (autonomous run, Core category). Review before `/asset-spec`.* Health renders nothing; it supplies the events each view needs:

| Event | Presentation owner | Intent (from the art bible, PC and HUD) |
|---|---|---|
| `DamageApplied` on a carrier | Player Character (flinch one-shot), Audio | a squash flinch and a paper-thud. The cause-direction `srcYaw` drives a screen-edge hint (HUD). No blood, no gore |
| `DamageBlocked` | Player Character | the bubble-wrap shell "boings" (PC Visual/Audio proposal) |
| `PlayerDied` → `CarrierKnockedDown` | Player Character, HUD, Audio | the knockdown beat (PC P15: starfish flop, hat arcs off, pile bursts) |
| goon HP % drop / `EntityDowned{Enemy}` | Enemy AI views | a flinch from the HP % drop; a cartoon poof and drops on down |
| construct HP % / `EntityDowned{Construct}` | Defenses views, HUD | a cracking-plank tint while damaged; a ruin ghost on down (spec 05 §5) |
| `HitConfirm` | Player Combat view, HUD | an attacker-only damage number and hit-spark confirmation |

## Game Feel

- **Own hit feedback.** The local flinch and HP-bar drop play when `OwnHp` arrives, ≈ 1 tick plus one-way latency (≈ 110 ms at 150 ms RTT). Damage is never predicted on the client, because goons are host-only.
- **Dealing a hit.** A cosmetic local spark plays at swing contact on the attacker's client. The number waits for `HitConfirm` (≈ 150 ms). A miss-then-confirm reads as "late number", never "phantom hit".
- **Readability over variance.** Damage is deterministic (HD13), so players learn "three Axe swings fell an Archer" and "a heavy parcel nearly halves a Barbarian".
- **Breaking contact is a readable choice.** Regen starts at 5.5 s, and the bar shows it with a steady fill.

## UI Requirements

Health owns no screen. The HUD's HP bar (HUD & Notifications, M3) consumes `OwnHp` (exact `hp`; `maxHp` is `player.maxHp` from Content; numeral always shown, Stamp Red at ≤ 25 %) and teammates' `hpPct`. The knocked-down screen is Player Character's. The construct-under-attack marker belongs to Defenses and the HUD (HN-7). This answers HUD HN-7's "own HP" item. No UX flag is raised here, because the HP bar's UX spec belongs to HUD's `/ux-design` pass.

## Cross-References

| This GDD references | In | What | Status |
|---|---|---|---|
| Simulation Core | HD2, HD4, HD5, HD13, Edge Cases | R3 phases 10–11, R9 sync calls, R12 cursors, R31 counters, R32 zero-alloc, HB7 | In Review (Provisional). Hand-back HB-HD-2: phase-10 wording; `drops` stream owner → Enemy AI |
| Player Character & Movement | HD1, HD5–HD8, HD11, Network | P15 `PlayerDied` (HB-HD-1: drop `position`, add `causeDefId`), P17 protection end (HB-HD-3), `spawnProtectMs` 3000/1500 confirmed, OQ7 answered, OQ15, OQ26 | In Review |
| Throwing & Catching | HD2, HD7, F1, Edge Cases | F6 thrown damage, `flightId`, Dead/Frozen actor accepted, overkill no-op, phase-4 protection clear | In Review: its OQ4 contract is honoured |
| Grid Inventory | Dependencies | `SpillToBag` / spill bags are called by PC, Defenses and Vehicles, never by Health | In Review |
| Content & Data Definitions | HD1, F1, F2, Tuning Knobs | balance keys, `hp` fields, `ConstructHpMultPm`; new `armorClass`, `DamageTagDef`, `damageTakenPm` (HB-HD-4) | In Review (Provisional) |
| HUD & Notifications | UI, Network | HP bar inputs; HN-7 own-HP item answered | In Review |
| design/spec/05, 01, 06, 11, 13 | throughout | baselines: HP 100, regen 2 HP/s after 5 s, heals, friendly fire off, hit reports, reliable damage events, uint8 HP % | spec |

## Acceptance Criteria

*Specialist: qa-lead. Tags as in Player Character's Section H. Cases expecting a Debug assert run with the `SimAssert` capture sink (HD13) and check the captured count.*

- **HD-AC-01 [UNIT][M-1b]** GIVEN `Register(c1,Carrier,100,…)` and `Register(g1,Enemy,60,…)` WHEN queried THEN `Hp==MaxHp` (100, 60) and `IsDowned==false`. GIVEN an id never registered (mailbox), unregistered earlier in the tick, or unregistered and re-registered in the same tick WHEN a previously queued `ApplyDamage(id,10)` resolves THEN no HP change, no `DamageApplied`, `dropGone==1` (HD1).
- **HD-AC-02 [UNIT][M-1b]** GIVEN carrier hp 100 WHEN a phase-0 `ApplyDamage(30,EnemyAttack)` is called THEN `Hp==100` until `Combat.Resolve` and 70 after. `amount` 0 and −5 are rejected (pending count unchanged; Debug asserts). A call from phase 11 applies at T+1 with `lateDamage==1` (HD2).
- **HD-AC-03 [UNIT][M-1b]** GIVEN one target per kind at partial HP WHEN each of 6 damage `sourceKind`s submits `ApplyDamage(10)` (24 pairs) THEN exactly 8 apply (the HD3 table) and 16 count `dropIllegal`. Damage with `actorId==targetId` is `dropIllegal`, while a self-Bandage applies. Of the 12 heal pairs, exactly Carrier/Consumable, Construct/Repair, Construct/PrepRegen and Vehicle/Repair apply (HD3).
- **HD-AC-04 [UNIT][M-1b]** GIVEN one tick with entries (a) unregistered id, (b) legal hit on a Downed goon, (c) illegal pair on a Downed goon, (d) illegal pair on a Protected carrier, (e) legal hit on a Protected carrier, (f) legal hit on a Frozen carrier, (g) heal on a Frozen carrier WHEN resolved THEN `dropGone==1`, `overkill==2` (Downed is checked before the allow-table), `dropIllegal==1`, `dropInvuln==2`, `dropHeal==1`, zero HP changes, and exactly one `DamageBlocked` (from (e)) (HD4).
- **HD-AC-05 [UNIT][M-1b]** GIVEN carrier hp 10 and the list [Bandage 50 (phase 0), Giant 40 (phase 7)] THEN hp 20 and Up. The reversed list gives Downed with the heal dropped. GIVEN goon hp 5 hit with raw 15 THEN `DamageApplied{raw 15, dealt 5, killed}`. Three hits of 10 in one tick give 3 `DamageApplied` in append order and exactly one `HpChanged{hp 70}`; a tick with no change raises none (HD4).
- **HD-AC-06 [UNIT][M-1b]** GIVEN carrier hp 15 and entries [g1 40 defId 7, g2 40] THEN exactly one `PlayerDied{causeActorId g1, causeKind EnemyAttack, causeDefId 7}`, placed after g1's `DamageApplied`, with `overkill==1` and the HoT slots cleared. GIVEN a goon felled by four Axe hits of 15 THEN exactly one `EntityDowned{Enemy}` and no `PlayerDied`; the goon stays Downed until its owner unregisters it, and every RNG stream's draw counter is unchanged (HD5).
- **HD-AC-07 [UNIT][M-1b]** GIVEN masks Protected / Sheltered / Frozen WHEN legal damage arrives THEN each drops as `dropInvuln`, and only Protected also yields `DamageBlocked`. A Protected carrier at 50/100 still regens on schedule. Setting Frozen clears the HoT slots, and a Frozen carrier gains no regen. With {Protected, Sheltered}, clearing Protected keeps it invulnerable; clearing both lets the next hit apply (HD6).
- **HD-AC-08 [UNIT][M-1b]** GIVEN a Protected carrier P WHEN (a) P's hit deals 5, (b) P hits a Downed goon, (c) P's hit has `takenPm 0`, (d) P throws mail or bonks a teammate, (e) P's operated turret deals 5 THEN `DamageApplied{actor P, dealt>0}` is raised for (a) and (e) only, and a PC-stub phase-11 reader clears `Protected` for exactly those two (HD7).
- **HD-AC-09 [INTEGRATION][M-1b]** GIVEN P Protected, goon G attacking P, and P's weapon hitting G in one tick WHEN both append orders run THEN G's hit is dropped in both, `Protected` is unchanged through phase 10, the PC stub clears it in phase 11, and G's hit at T+1 applies. With a Throwing stub, P's HIT_ENEMY throw clears it in phase 4, so G's same-tick hit applies (HD7).
- **HD-AC-10 [UNIT][M-1b]** GIVEN a scripted 10 000-op sequence (damage, heal, HoT, Restore, Frozen toggles, Unregister) THEN for every component `IsDowned ⇔ hp==0` and `0≤hp≤maxHp` throughout. `Restore` on a Downed carrier gives hp 100, Up, empty slots and an `HpChanged` that tick. `Restore` on a goon is rejected and it stays Downed (HD8, HD11).
- **HD-AC-11 [UNIT][M-1b]** GIVEN carrier 25/100, last `dealt>0` hit at T0, r = 2, delay 5000 ms THEN `regenDelayTicks==150` (151 for 5001 ms). HP is 25 through T0+163, 26 at T0+164, then +1 every 15 ticks; 100 is first reached at T0+1274, with no gain after. For r ∈ {1, 2, 3, 10}, any later 30-tick window gains exactly r (F3).
- **HD-AC-12 [UNIT][M-1b]** GIVEN carrier 50/100, last `dealt>0` hit at T0, WHEN a heal, a dropped entry, a blocked hit or a `takenPm 0` hit occurs THEN the regen cadence is unchanged. A 10-damage hit at T0+164 (the first gain tick) gives hp 40, not 41, and the next gain at T0+328. A goon, construct and vehicle at partial HP gain 0 over 3000 ticks without a heal (HD9).
- **HD-AC-13 [UNIT][M-1b]** GIVEN F1 `(amount, takenPm)` THEN (150,500)→75, (15,1500)→23, (10,1500)→15, (12,2000)→24, (40,1000)→40, (3,500)→2 (half-up), (1,400)→1 with `minDamage 1` and →0 with `minDamage 0`, (10,0)→0 with no `DamageApplied`, a missing matrix entry → 1000, (100000,4000)→400000, and `dealt==min(final,hp)` (F1).
- **HD-AC-14 [UNIT][M3]** GIVEN F2 THEN Mega Giant →1000, Wall Breaker →30, Wooden Wall with Stat 1400 →420, PO →4200, carrier →100. GIVEN a wall at 100/300 WHEN the Stat version bumps to 1400 THEN step 0 gives 220/420 before that tick's entries. Reverting gives 100/300; 50/420 → 1/300 with no `EntityDowned`; a Downed component keeps hp 0 (F2).
- **HD-AC-15 [UNIT][M3]** GIVEN Berries (H 25, D 150) THEN k = 1 is evaluated in step 2 of the eating tick (gain 0), the first +1 lands at k = 6, then +1 every 6th tick, 25 in total at k = 150. Two slots give 50 and three give 75. 90 + Berries clamps at 100. Damage does not cancel a slot; a knockdown clears all. H ∈ {1, 7, 25, 200} × D ∈ {1, 6, 150, 900} each sums to exactly H (F4).
- **HD-AC-16 [INTEGRATION][M3]** GIVEN a consumable-owner stub WHEN a Bandage is used at 80/100 THEN `CanHeal` is true, hp 100, item −1. At 100/100, Downed or Frozen, `CanHeal` is false and the count is unchanged. A 4th Berry with 3 slots full is refused, while a Bandage at 80/100 with 3 slots full applies. Four carriers feeding one target Berries in one tick with 1 free slot give one slot and `dropHotFull==3` (HD10).
- **HD-AC-17 [UNIT][M-1b]** GIVEN `maxHp` ∈ {30, 100, 300, 4200, 120000} WHEN every hp from 0 to maxHp is evaluated THEN `hpPct==0 ⇔ hp==0`, the result is ≤ 100 and non-decreasing, and 3/400→1, 299/300→100, 25/100→25 (F5).
- **HD-AC-18 [INTEGRATION][M-1b]** GIVEN a Run Structure stub summing `dealt` per actor and a goon at hp 5 WHEN [A raw 15, B raw 15] resolve THEN A = 5 and B = 0. A Frozen or Dead actor still scores, telemetry sees raw 15 / dealt 5, and after a scripted tick all seven counters equal the script (HD12).
- **HD-AC-19 [UNIT][M-1b]** GIVEN `pendingCap` 1024 WHEN 1024 entries (late T−1 entries included) are queued THEN all resolve with `pendingOverflow==0`. With 1030, `pendingOverflow==6`, the first 1024 results are identical to a 1024-only run, and Debug asserts (HD13).
- **HD-AC-20 [UNIT][M-1b]** GIVEN a recorded 300-tick script (10 000 mixed entries) WHEN replayed in 100 fresh Sim instances, in Debug and Release THEN the state-dump hash (every component's hp, maxHp, life, mask, `lastDamageTick`, slots, counters and the event-log bytes) is identical, with 0 RNG draws (HD13).
- **HD-AC-21 [PERF][M-1b]** GIVEN 256 entities and 1024 pending entries (mixed, 25 % illegal) WHEN `Combat.Resolve` runs 1000 ticks after 100 warmup ticks in Release THEN allocated bytes are exactly 0 (blocking). p99 ≤ 0.3 ms is ADVISORY until Sim Core OQ6 defines the reference machine (PROPOSED: ≈ 1 % of the tick).
- **HD-AC-22 [INTEGRATION][M-1b]** GIVEN a Throwing stub WHEN a heavy 40 hits a 60-HP Barbarian THEN hp 20 with `causeRef==flightId`. A Dead or Frozen thrower still applies and is credited. Thrown at a carrier or construct → `dropIllegal`. A goon felled earlier in the tick's list → `overkill`, no `DamageApplied`, yet a Protected thrower's protection was cleared in phase 4 (HD7). A Mega Barbarian (150) goes down on the 4th heavy.
- **HD-AC-23 [INTEGRATION][M-1b]** GIVEN a PC stub and a carrier at 100 WHEN 11 Barbarian hits of 10 land in one tick THEN one `PlayerDied` and `overkill==1`. After the stubbed clock-in at tick C, `Restore` + `Protected` give hp 100; with the stub clearing the mask at C+90, a hit at C+89 drops and one at C+90 applies. A disconnect (Frozen in phase 0) drops that tick's damage, and a rejoin keeps the frozen HP (no `Restore`) with 45 ticks of protection (HD6, HD11).
- **HD-AC-24 [INTEGRATION][M3]** GIVEN an Enemy AI stub WHEN a Mega Giant registers THEN `MaxHp==1000`. A submitted Mega hit of 15 removes exactly 15 (no re-scaling). Wall Breaker submissions of 150 (wall) and 20 (carrier) apply as given. Goon-on-goon → `dropIllegal`. After a down, the stub unregisters the goon in phase 11 and a T+1 entry gives `dropGone==1`.
- **HD-AC-25 [INTEGRATION][M3]** GIVEN Defenses and Vehicles stubs WHEN a Wall Breaker deals 150 to a Stone Wall at 800 (matrix 500) THEN hp 725. Turret 12, spike 15 and `VehicleImpact` apply to goons and are `dropIllegal` on carriers. The PO (4200 with Reinforced) at 0 raises exactly one `EntityDowned{Construct}`, no `PlayerDied` and no other Health special case. Repair and PrepRegen clamp at max. A Sheltered carrier's hit is dropped.
- **HD-AC-26 [NET][M0]** GIVEN a host and 8 headless clients on loopback WHEN a client sends a melee hit report `{seq, weaponSlot, targetId}` THEN the wire struct has no damage field, the host applies the def amount (Axe on Barbarian −15), and Health sends 0 client → host messages over 1000 combat ticks.
- **HD-AC-27 [NET][M0]** GIVEN a carrier quiet for ≥ 15 ticks, then hit on ticks 0–11 THEN the owner gets `OwnHp` (7 B) at ticks 0, 4 and 8, and a trailing flush at 12. The deltas sum to total `dealt`, and the last `hp` equals the host's. Regen sends ≤ 1 per 15 ticks. Dropped, blocked and overkill entries send none, and the other 7 clients get 0 `OwnHp`.
- **HD-AC-28 [NET][M0]** GIVEN a carrier at 100 WHEN it drops to 97, then to 94 THEN there is no batch entry for 97 and one `{idx, hpPct 94}` at the next 15-tick flush; a crossing of 25, 0 or 100 also sends. The message size is 8 + 3n B. `Restore` and heal-to-full bypass the gap. All clients get the same batch.
- **HD-AC-29 [NET][M0]** GIVEN an attacker landing 20 `dealt>0` hits inside one fixed 30-tick window THEN exactly 8 `HitConfirm`s go to the attacker only, plus one `capped` message (targetId 0) at window end whose amount equals the folded `dealt` sum. `killed` is set on the lethal hit and `flightId` is carried for thrown hits. A Protected carrier hit 10 times in 30 ticks gets ≤ 2 `DamageBlocked`.
- **HD-AC-30 [NET][M0]** GIVEN 5 % loss and jitter WHEN 1000 knockdowns occur THEN the owner sees `OwnHp{hp 0}` before `CarrierKnockedDown` and no `OwnHp hp>0` until `Restore`; every observer gets the hp-0 batch entry at once (gap bypass) and the Restore entry after PC's clock-in event. After 300 quiet ticks a new message still applies (no stamp compare), and a hp > 0 batch entry for a player shown Dead is ignored (O1–O5).
- **HD-AC-31 [NET][M0]** GIVEN a mid-fight join and a rejoin THEN the self block carries the exact `hp` (u16), teammates' and goons' `hpPct == F5(host hp)`, only damaged constructs (`hp < maxHp`) carry `{id, hpPct}`, Health's bytes equal 2 + teammates + goons + (idBytes + 1) × damaged constructs, and regen timers and HoT slots are absent.
- **HD-AC-32 [NET][M0]** GIVEN a scripted worst-case 8-player raid on loopback (each carrier hit every 4 ticks, each attacker ≥ 8 hits/s, every batch entry dirty each flush, one Protected carrier hit at ≥ 2 Hz) WHEN measured for 60 s THEN `OwnHp`, batch, `HitConfirm`, `DamageBlocked` and flinch bytes per client down, with framing, total ≤ 4.0 kbps. BLOCKED until the multiplayer spike re-baselines framing.

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| OQ-HD-1 | Solo knockdown frequency (Pillar 5): with HP 100 and regen ≈ 42 s from 25 → full and no one to cover, is solo raid pressure fair? Scale on the enemy side (raid budget) if not, never player HP. Also verify the anchor moment: with a full bag, is retreat-and-regen ever chosen over the gamble? (Empty-handed, a 10 s knockdown beats a 42 s regen by design; see PC OQ26.) Kill criterion: if > 20 % of greybox knockdowns are deliberate with an empty bag (OQ-HD-8), clock-in restores partial HP. Also check a 6-Barbarian pack (60 HP/s) leaves a "flip red, then choose" window. | game-designer / QA | M-1b greybox, then M3 playtests |
| OQ-HD-2 | **HB-HD-1 → Player Character:** `PlayerDied` drops `position` (PC reads its own host view in P15; nothing moves a carrier between phases 10 and 11) and adds `causeDefId`; rejoin must not call `Restore`; P15 reads `DamageApplied` for the flinch and the protection end. | Player Character GDD | before PC re-review |
| OQ-HD-3 | **HB-HD-2 → Simulation Core:** R3 phase 10 should read "pending damage and heals, death events (`PlayerDied`, `EntityDowned`)". Ruins, drops and wreck spills are phase-11 reactions, and the `drops` stream owner becomes Enemy AI (Health is RNG-free). The architecture event list gains `DamageApplied`, `HpChanged`, `EntityDowned` and `DamageBlocked`. | Simulation Core / technical-director | before Sim Core re-review |
| OQ-HD-4 | **HB-HD-3 → Player Character P17:** "an attack" becomes "a weapon or turret hit with `dealt > 0`, or a HIT_ENEMY throw (contact)"; a whiff never ends protection. **HB-HD-4 → Content & Data:** add `armorClass` (EnemyDef, ConstructDef, VehicleDef), `DamageTagDef` and the sparse `damageTakenPm` matrix plus validator rows (tags and classes exist; values 0–4000); add the `player.maxHp`, `player.regenDelayMs`, `player.regenHpPerS` and `health.*` keys. **→ Sim Core:** a `SimAssert` capture hook for tests (HD13). **→ PC:** the flinch event's rate. HB-HD-2 and HB-HD-4 applied is an M-1b gate. | PC / Content & Data / Sim Core | before those re-reviews; M-1b |
| OQ-HD-5 | Provisional contracts for Player Combat, Enemy AI, Defenses, Vehicles and Run Structure (Dependencies list): attacker-scaled amounts, per-owner registration, phase-11 reactions, the Defenses phase binding (phase 7 recommended), `Sheltered` timing, vehicle repair, and PO Prep regen via `ApplyHeal(PrepRegen)` at an integer cadence (e.g. 5 HP per 30 ticks). Reinforced acts as a free partial repair (F2): Defenses to accept or reject. Run Structure: revisit the Payday "most damage" award, which the creative-director observes leans competitive (Fellowship, Pillar 1). | those GDDs | when each is written |
| OQ-HD-6 | Should a Protected carrier be skipped by goon targeting (PC OQ15)? Health supports both (`DamageBlocked`). | Enemy AI & Waves | before that GDD |
| OQ-HD-7 | SPIKE: one reliable-ordered channel across message kinds, head-of-line blocking at 5 % loss (moving cosmetic messages to unreliable deviates from spec 06), +8 B framing, the disconnect-detection delay before Frozen. | network-programmer / technical-director | pre-M0 multiplayer spike |
| OQ-HD-8 | Telemetry: per-shift knockdowns by cause, `dropIllegal` > 0 in any build (a source bug), deliberate knockdowns near the PO (feeds PC OQ26 cost tuning). | analytics / QA | M1 |
| OQ-HD-9 | Deferred by review: `hotSlots` 3 (15 HP/s) vs "no heal maths"; Downed-before-allow-table masks illegal sources as `overkill`; extra AC cases (AC-01 converse, AC-07 Sheltered, AC-09 late order, AC-16 start-of-tick). | game-designer / qa-lead | re-review |

**→ becomes an ADR** (implementation, not design): the pending-list layout and zero-alloc ring; the HoT slot storage; the `damageTakenPm` matrix layout and lookup; the Stat-version change pass; `Pm.Scale` as a shared primitive (with ADR-0006); the Health replication messages and throttles (with ADR-0011/0022). Candidate: **ADR "Health component, damage queue & resolve"** (M-1b).
