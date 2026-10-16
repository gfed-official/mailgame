# 11 — Balance Baselines

Every number here is a starting point for tuning, stored in content/balance.json or the relevant def file (chapter 08). This chapter collects them in one place, explains the reasoning, and defines the throughput models that tools/BalanceSim uses to check that the quota curve is winnable and that automation becomes necessary.

## 1. Run timing

| Key | Value | Rationale |
|---|---|---|
| prepSeconds | [60, 90, 90, 90, 90] | Shift 1 is short: nothing to build yet |
| deliverySeconds | [240, 270, 270, 270, 300] | Shift 5 is longer to make the finale feel bigger |
| raidWindowSeconds | 90 | Overlaps the end of Delivery |
| raidSpawnRateMult | 0.5 | Mail spawn rate during the Raid window (chapter 13) |
| paydaySeconds | 20 | Long enough to read the summary and buy |
| draftSeconds | 30 | Ends early when everyone has picked |
| Nominal run | 26–31 min | Within the doc's 20–30 min target when Prep and Draft end early |

## 2. Player-count scaling

```
playerScale(n) = n ^ 0.65
```

| n | playerScale | Per-player share |
|---|---|---|
| 1 | 1.00 | 1.00 |
| 2 | 1.57 | 0.78 |
| 3 | 2.04 | 0.68 |
| 4 | 2.46 | 0.62 |
| 6 | 3.20 | 0.53 |
| 8 | 3.86 | 0.48 |

A power law is used rather than a decaying exponential because it stays monotonic (an 8-player quota is always higher than a 6-player quota). Per-player workload falls to about half at 8 players, which is intentional: large groups spend proportionally more time building and coordinating, and the map (a single island) cannot physically absorb 8× the foot traffic.

Wave scaling uses a steeper linear curve (1 + 0.35 × (n − 1), 8 players → 3.45×) because combat parallelises better than delivery: 8 players with axes clear enemies much faster than 1.

## 3. Quota and spawn

| Shift | baseQuota (¢) | Solo spawn value (×1.6) | 4-player quota | 8-player quota |
|---|---|---|---|---|
| 1 | 600 | 960 | 1476 | 2316 |
| 2 | 1100 | 1760 | 2706 | 4246 |
| 3 | 1800 | 2880 | 4428 | 6948 |
| 4 | 2700 | 4320 | 6642 | 10422 |
| 5 | 4000 | 6400 | 9840 | 15440 |

spawnOverhead 1.6 means a team must deliver ~63% of spawned value, correctly and on time, to meet quota. Typical waste sources: undelivered backlog, late mail (50%), misdelivery (−50% and item lost).

### 3.1 Item counts implied (solo)

Using the mail mix (chapter 03) and base values with district/shift multipliers averaged:

| Shift | Approx. items spawned | Of which letters/postcards | Packages | Cargo |
|---|---|---|---|---|
| 1 | ~85 | ~72 | ~13 | 0 |
| 2 | ~120 | ~95 | ~25 | 0 |
| 3 | ~150 | ~105 | ~40 | 1–2 |
| 4 | ~190 | ~120 | ~60 | 3–4 |
| 5 | ~250 | ~150 | ~85 | 6–8 |

Letters stack per address (~7 per house on shift 1), so the number of stacks to move is much lower than the item count.

## 4. Throughput models (BalanceSim)

BalanceSim simulates a shift with abstract "delivery agents" against the spawn schedule. Each agent model is a rate of value delivered per minute under stated conditions.

| Agent | Value/min (shift 1 mix) | Value/min (shift 3 mix) | Conditions |
|---|---|---|---|
| Hand, district 1 | 220 | 180 | 60 s circuits over 10 houses; letters stacked; ~30% of time sorting at the Intake |
| Hand, districts 1–3 | — | 110 | 150 s circuits, 28 houses, weight penalties from packages |
| Bike, districts 1–3 | — | 200 | Circuit 90 s; grid 2x8 extra |
| Truck, presorted, 1 driver | — | 520 | Chapter 04 reference of 25 items/min mixed value |
| NPC truck, one route | — | 250 | 60% speed, waits at stops |
| Belt line + sorter to 8 mailboxes | — | 900 (cap: spawn rate) | Limited by what the Intake receives |
| Motorboat to second town | — | 380 | Large Island only |

### 4.1 Viability checks

| Case | Expected result |
|---|---|
| Solo, hand only, shift 1 | 220 × 4 min ≈ 880 ≥ 600 → pass with margin |
| Solo, hand only, shift 2 | 180 × 4.5 ≈ 810 < 1100 → fail; bike (120 ¢) alone gives 200 × 4.5 = 900, still short; hand + bike + one belt from Intake to the nearest 2 houses ≈ 1150 → marginal pass. This is the intended "you must start building" moment |
| Solo, shift 3 | Requires sorter or truck; truck alone ≈ 520 × 4.5 = 2340 ≥ 1800 → pass |
| Solo, shift 5 | 4000 over 5 min = 800/min: truck + belt line + NPC driver ≈ 1600/min capacity → pass if the factory survives raids |
| 4 players, hand only, shift 1 | 4 × 220 × 4 = 3520 ≥ 1476 → easy (by design; groups should be able to skip early automation and invest in it during shift 1–2) |
| 8 players, shift 5 | Quota 15440 over 5 min = 3088/min; requires ≥ 2 belt lines + trucks; feasible with ~4 players building and 4 driving |

BalanceSim runs these as regression tests: a content change that makes "solo, shift 1 hand only" fail or "8 players, shift 5 full factory" fail by more than 20% breaks CI.

## 5. Economy

| Key | Value |
|---|---|
| misdeliveryPenaltyRatio | 0.5 |
| lateValueRatio | 0.5 |
| deadLetterShifts | 2 |
| walletFloor | −500 |
| zeroMisdeliveryBonus | +10% shift earnings |
| salvageRatioPrep / salvageRatioDelivery | 1.0 / 0.5 |
| ruinRebuildRatio | 0.5 |
| sellPriceRatio | 0.25 of buy price |
| npcHireCost | 150 per shift |

Blueprint prices (300–700 ¢) are sized so that a solo player can afford bp_sorting (400) plus a bike (120) from shift 1 surplus (~880 − 600 = 280 surplus + wallet carry) by early shift 2 only if they over-deliver; a 4-player team can afford two blueprints after shift 1. Groups therefore reach automation one shift earlier than solo, which is compensated by the higher quota.

## 6. Mail values and multipliers

| Key | Value |
|---|---|
| Postcard / Letter / Small / Medium / Large / Cargo base value | 4 / 8 / 30 / 70 / 160 / 600 |
| distanceMultiplierPerDistrict | +0.25 per district beyond the first |
| shiftMultiplierPerShift | +0.10 per shift beyond the first |
| streetStreakRatio | 0.30 |
| batchIntervalSeconds / jitter | 15 / ±3 |
| Cursed value mult | 1.5 |
| Priority (perk) value mult / time limit | 3.0 / 60 s |
| Lost Parcel value mult | 3.0 |

## 7. Complaint meter

| Key | Value |
|---|---|
| Range | 0–100 |
| Decay | 0.1 per second during Delivery (1 per 10 s) |
| Misdelivery increments | Postcard 3, Letter 5, Small 8, Medium 12, Large 16, Cargo 20 |
| Backlog increment | +1 per 15 s while Intake is full |
| Late delivery | +2 |
| Inspector threshold | 75 (50 with Postal Audit stamp) |
| Raid multiplier | 1 + complaint/100 |

## 8. Player

| Key | Value |
|---|---|
| HP / regen delay / regen | 100 / 5 s / 2 HP/s |
| Walk / sprint / swim | 5.0 / 7.5 / 2.0 m/s |
| Jump height | 1.2 m |
| Weight points: light / medium / heavy | 1 / 3 / 8 |
| Weight speed multiplier | clamp(1 − 0.01 × points, 0.6, 1.0) |
| Throw launch speed: light / medium / heavy | 14 / 10 / 6 m/s (bulk cannot be thrown) |
| Interact range | 2.5 m |
| Mailbox hold | 0.4 s |
| Respawn | 10 s |
| Death bag despawn | 30 s (mail returns to Intake) |
| World item despawn | 300 s |

## 9. Automation

| Key | Value |
|---|---|
| Belt Mk1 / Mk2 / Mk3 speed | 2 / 4 / 6 m/s |
| Belt item spacing | 0.5 m per lane |
| Pipe speed / spacing | 5 m/s / 1 m |
| Sorter Mk1 throughput | 2 items/s |
| Inserter rate | 1.25 items/s |
| Destination insert rate | 2 items/s |
| Operated belt multiplier | 1.5× |
| Operated turret fire rate | 2× |
| NPC speed ratio | 0.6 |
| Pump output | 1 Oil Can per 45 s |

Constructs always run at full rated speed. There are no wattage, brownout, or generator fuel numbers.

### 9.1 Reference factory (shift 3 target)

40 belt tiles, 1 Address Sorter, 4 Inserters. Materials: ~20 Plank (shop or harvest→shop), 12 Stone, 40 Iron Ingot (shop bundles and/or harvest ore sold/replaced by shop ingots). Resource placement guarantees enough raw harvestables on a Small Island (resourceMultiplier 3.0) to fund this via shop conversion; the shop's 10-ingot bundle (350 ¢) is the money-for-time alternative. No generators, poles, or power math.

## 10. Combat

| Enemy | HP | Dmg | Rate (s) | Speed | Cost |
|---|---|---|---|---|---|
| Barbarian | 60 | 10 | 1.0 | 4.5 | 10 |
| Archer | 40 | 8 | 1.5 | 4.0 | 15 |
| Giant | 400 | 40 | 2.0 | 3.0 | 60 |
| Wall Breaker | 30 | 150 (walls/belts) / 20 | — | 6.0 | 12 |
| Hog Rider | 120 | 20 | 1.0 | 7.0 | 25 |
| Balloon | 90 | 30 AoE r2 | 3.0 | 2.5 | 30 |
| Tank | 900 | 60 | 3.0 | 2.0 | 150 |

| Key | Value |
|---|---|
| baseBudget | [0, 120, 220, 360, 560] |
| waveScalePerExtraPlayer | 0.35 |
| soloMult (≤ 2 players) | 0.8 |
| Pulses / interval / finale mult | 6 / 15 s / 1.5 |
| megaChanceBase / per shift | 0.05 / +0.02 |
| Mega HP / dmg / scale / speed | ×2.5 / ×1.5 / ×1.4 / ×0.9 |
| Warning | 15 s (+20 s with Early Warning, +15 s per Alarm Post in range, max 45 s) |
| Vehicle impact damage | 10 × (speed / 5 m/s) to enemies, minimum speed 4 m/s (chapter 13) |

### 10.1 Sanity checks

- Solo shift 2: budget 96 → ~6 Barbarians + 2 Archers over 6 pulses. An axe (25 DPS) kills a Barbarian in 2.4 s. A single Wooden Wall (300 HP) holds 3 Barbarians for 10 s. Expected outcome: a solo player loses 0–2 belt tiles if undefended and nothing if they built 6 walls.
- 4 players shift 4: budget 360 × 2.05 = 738 → e.g. 1 Tank, 2 Giants, 3 Hog Riders, 2 Balloons, 8 Barbarians, 6 Archers. Requires at least one Turret or two players on defense.
- 8 players shift 5 finale pulse: ~480 budget in one pulse → 3 Tanks capped, plus mixed swarm and ≥ 1 Mega. Intended to be the run's peak moment.

Construct HP (chapter 05 §5) is set so that a Giant (20 DPS) destroys a belt tile in 4 s, a Depot in 40 s, and the PO in 150 s of uninterrupted attack; the PO cannot fall to a single shift-3 wave if any player responds.

## 11. Meta-progression

| Key | Value |
|---|---|
| XP per shift completed / victory / delivery | 100 / 50 / 5 (team deliveries ÷ players, min 1 per player) |
| Stamp XP bonus | +10% per active stamp |
| rankXpPerRank | 500 |
| Rerolls per run | 1 (2 at Rank 4) |
| Personal perk cap | 5 |

A 5-shift solo Victory with ~400 deliveries yields ~2550 XP ≈ 5 ranks, so the Sea kit (Rank 2) and Large Island (Rank 3) unlock after the first successful run or two or three failed ones.

## 12. Tuning process

1. Every value above is loaded from data; the debug overlay exposes a live editor for balance.json in dev builds.
2. tools/BalanceSim runs the viability checks in §4 on every content change (CI).
3. Telemetry (opt-in, local file in v1): per run, per shift: earned, quota, deliveries by kind, misdeliveries, constructs built/lost, deaths, perks picked, time in each phase. Used to re-derive the throughput models from real play.
4. Target outcomes after tuning: solo win rate ~25% for a Rank 5 player without stamps; 4-player win rate ~50%; median run length 26 min; ≤ 10% of runs end at shift 1.
