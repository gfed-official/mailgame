# 01 — Run Structure (Arcade)

Arcade is the rogue-lite mode: an accelerated, time-boxed version of the core gameplay with a target run length of 20–30 minutes. This chapter defines the run state machine, the shift loop, quota, perks, meta-progression, difficulty modifiers, and end conditions.

## 1. Run state machine

```
Lobby -> Generating (host starts run)
Generating -> Prep (map ready, players spawned)
Prep -> Delivery (timer ends or all players Ready)
Delivery -> Raid (raid window opens, shift >= 2)
Delivery -> Payday (shift clock ends, shift 1)
Raid -> Payday (shift clock ends)
Payday -> Draft (quota met)
Payday -> RunOver (quota missed)
Delivery/Raid -> RunOver (Post Office destroyed)
Draft -> Prep (shift < 5)
Draft -> Victory (shift == 5)
RunOver / Victory -> Results -> Lobby
```

The state machine is server-only. Clients receive the current phase, the phase deadline (server tick), and phase payload (shop offers, draft cards, results) via replicated RunState (see chapter 08).

### Phase table

| Phase | Duration (tunable) | What happens | Player actions allowed |
|---|---|---|---|
| Lobby | until host starts | Seed, Postage Stamps, and starting kit chosen. Players join/leave. | Ready up, pick kit, pick cosmetics |
| Generating | < 5 s target | Server generates world from seed, spawns Post Office, players, initial resources. Clients build map from seed and receive deltas. | None (loading screen) |
| Prep | 60 s (shift 1), 90 s (shifts 2–5) | New district unlocks. Shop open at PO. Build, rearrange belts, harvest. No mail spawns. Join-in-progress allowed. | Move, build, harvest, shop, sort existing mail |
| Delivery | 240 s (shift 1), 270 s (shifts 2–4), 300 s (shift 5) | Mail spawns at PO on a schedule. Deliveries earn money. Clock visible to all. | Everything |
| Raid | last 90 s of Delivery, shifts 2–5 | Enemy wave spawns at map edge and advances on constructs. Overlaps Delivery; mail continues to spawn at reduced rate (50%, see chapter 13). | Everything |
| Payday | 20 s | Quota evaluated. Summary shown. Shop opens for a fixed window. | Shop, move |
| Draft | until all pick or 30 s | Perk draft. | Pick a card |
| Results | until host continues | Score, Postal Rank XP, unlocks. | View, return to lobby |

Total nominal run: Prep 60+90×4 = 420 s, Delivery 240+270×3+300 = 1350 s, Payday 5×20 = 100 s, Draft ≤ 150 s. Approximately 30–34 minutes worst case; typical runs finish faster because Prep and Draft end early when everyone readies.

### Shift clock

- The shift clock is a server tick countdown replicated once per second plus on phase change; clients interpolate locally.
- Prep ends early when all connected players press Ready. Draft ends early when all players have picked.
- Nothing pauses the clock in multiplayer. In a solo run the host may pause (server tick freezes; a paused flag is replicated for UI).

## 2. Shift loop in detail

### 2.1 Prep

- District unlock: the district scheduled for this shift becomes deliverable. Its houses become valid mail destinations and its road segments are added to the routing graph. Districts already unlocked stay unlocked. Shift 1 unlocks the PO district (8–12 houses); each later shift adds a district so the deliverable house count grows across the run (targets: 10 → 18 → 28 → 38 → 50 on a Small Island seed; Large Island seeds add a second town from shift 3 and end nearer 60–70).
- Shop: the PO shop sells tools, vehicles, building materials (including refined materials), and refills. Inventory is fixed per shift with rotating "special" slots rolled from the run RNG (see chapter 10 for the catalog).
- Preview: the map shows the incoming district outline and a "mail forecast" (counts by size class and district) so players can plan sorting.

### 2.2 Delivery

- Mail spawn: the PO generates mail into its Intake container (a large grid container, 16x20) on a spawn schedule. Spawn budget per shift is derived from the quota (section 3). Mail is generated in batches every 15 s (tunable) with a small random jitter so belts are not perfectly regular.
- Intake overflow: if the Intake is full, spawned mail is queued as "backlog". Backlog is not lost, but each 15 s tick of backlog above 0 adds to the complaint meter. This punishes ignoring the PO without hard-failing the run.
- Delivery: inserting mail into the correct destination pays the mail's value instantly to the team wallet and emits a delivery event (score, stats). Inserting into the wrong destination deducts misdeliveryPenaltyRatio × value (baseline 0.5) and adds to the complaint meter; the mail item is consumed (the resident keeps it) so the mistake is permanent.
- Deadlines: each mail item has deadlineShift. Delivering in a later shift pays lateValueRatio (baseline 0.5). Mail older than 2 shifts is "dead letter": worth 0 but still clears when delivered. Undelivered mail is not a fail condition; it is a wasted spawn budget.
- Player death: players who die respawn at the PO after 10 s with their inventory dropped as a lootable backpack at the death location (30 s despawn, then contents return to PO Intake). No permanent penalty; the cost is time.

### 2.3 Raid

- Raids occur during the final 90 s of Delivery on shifts 2–5. Shift 1 has no raid so new players learn delivery before defense.
- Wave composition and budget are defined in chapter 05. Budget scales with shift number, player count, complaint meter, and Postage Stamps.
- A raid ends when the shift clock ends: remaining enemies flee (despawn over 5 s, playing a retreat animation). There is no "kill everything" requirement; the raid is a time-boxed pressure event.
- Constructs destroyed during a raid leave a "ruin" ghost that can be rebuilt for 50% cost during the next Prep.

### 2.4 Payday

- Quota check: teamEarningsThisShift >= quota(shift). Earnings are gross delivery income for the shift (penalties already subtracted). Spending does not reduce earnings.
- On success the surplus is kept in the team wallet.
- On failure the run ends. The Results screen still awards Postal Rank XP for the shifts completed.
- Payday summary shows: earned vs quota, deliveries by size class, misdeliveries, late deliveries, constructs lost, MVP callouts (most deliveries, most damage, most built).

### 2.5 Draft

- The server rolls a shared offer of 3 perk cards per draft using the perks RNG stream. Rarity weights: Common 60 / Uncommon 30 / Rare 10 (tunable; Rare weight +5 per shift).
- Each player picks one card. Multiple players may pick the same personal perk. A team perk, once picked by anyone, is applied once and greys out for the others (they still pick from the remaining two).
- Cards are filtered by prerequisites (e.g. belt perks require a belt to have been built) and by exclusion tags (no two conflicting perks in the same run).
- One free reroll per player per run is granted; Postal Rank can unlock a second.

## 3. Quota and spawn budget

### 3.1 Quota formula

```
quota(shift, n) = baseQuota[shift] × playerScale(n) × stampMultiplier

playerScale(n)  = n ^ 0.65                 // sublinear: 1→1.00, 2→1.57, 4→2.46, 8→3.86
baseQuota       = [600, 1100, 1800, 2700, 4000]   // tunable, in ¢
```

playerScale is intentionally sublinear: an 8-player run should be socially busy, not 8× the volume. Mail volume scales with the same curve so per-player workload drops to about half at 8 players, and the larger group's advantage is parallelism in building rather than raw delivery. stampMultiplier here refers only to stamps that change quota (Skeleton Crew, Trainee); score multipliers are separate (§6).

### 3.2 Spawn budget

```
spawnValue(shift, n) = quota(shift, n) × spawnOverhead      // spawnOverhead baseline 1.6
```

The PO spawns mail whose summed value equals spawnValue over the Delivery phase, so a team that delivers ~63% of what spawns, correctly and on time, exactly meets quota. Mail mix (letters vs packages vs cargo) shifts toward larger items in later shifts (see chapter 03, mail mix table). Batches are sized so that the per-batch value is spawnValue / batchesPerShift.

### 3.3 Complaint meter

- Range 0–100, team-wide, decays 1 point per 10 s during Delivery.
- Misdelivery: +5 (letters) to +20 (cargo), scaled by value class. Backlog: +1 per 15 s tick while Intake is overflowing. Late delivery: +2.
- Effects: raid budget multiplier 1 + complaint/100 (max 2×); at ≥ 75 a "Postal Inspector" event is scheduled for the next Payday (a one-shift penalty perk such as "Audit: −10% earnings").
- Displayed on the HUD as a stamped envelope that fills red.

## 4. Perks

Perks are run-scoped modifiers applied to data-driven stat blocks. They never require new code paths: every perk is a list of StatModifier and/or UnlockRecipe entries (chapter 08).

### 4.1 Categories

| Category | Scope | Examples |
|---|---|---|
| Carrier | Personal | +movement speed, +inventory rows, faster mailbox insert, double jump, bike sprint |
| Facility | Team | belt speed, sorter filter slots, inserter rate, depot capacity, shared build range |
| Postal Service | Team | quota −10%, letter value +25%, bulk letter bundles, misdelivery penalty −50%, extra district preview |
| Defense | Team | wall HP, turret damage, repair speed, raid warning earlier |

### 4.2 Rules

- Personal perks stack per player (a player may hold up to 5 personal perks; the 6th pick must be a team perk if any is available).
- Team perks are unique per run.
- Rare perks may be "build-around" perks that change a rule (e.g. "Insured: destroyed constructs rebuild free at next Prep").
- Perk effects apply immediately at Draft and persist until Results.
- Full initial pool (~28 perks; no power-related perks) is in chapter 10.

## 5. Meta-progression

### 5.1 Postal Rank

- Players earn Postal Rank XP at Results: 100 × shiftsCompleted + 50 × victory + 5 × deliveries + stampBonus. XP is per player; the team shares the deliveries count.
- Rank thresholds grow linearly (500 XP per rank, tunable). Ranks unlock content, never stats.

### 5.2 Unlock tracks

| Rank | Unlock |
|---|---|
| 1 | Default: Land starter kit, Small Island archetype, base perk pool, Postage Stamps tier 1 |
| 2 | Sea starter kit (rowboat blueprint pre-unlocked, +1 rope) |
| 3 | Large Island archetype |
| 4 | Second perk reroll |
| 5 | Facility perk pool expansion (sorter perks) |
| 6 | Postage Stamps tier 2 |
| 8 | Defense perk pool expansion |
| 10 | Pipes starter kit (pipes and one pump pre-unlocked) |
| 12 | Postage Stamps tier 3 |
| 15+ | Cosmetics (uniforms, truck liveries, mailbox skins) every rank |

Unlocks are stored in the local MetaProfile (chapter 08). In a multiplayer lobby, available archetypes and Stamps are the union of the host's unlocks; starting kits are per player from their own profile.

### 5.3 Seeds

- A seed is a 32-bit integer plus the ordered list of active Postage Stamps and the archetype. Encoded as a shareable string, e.g. `PM1-SMALL-7F3A9C21-CM.DR`.
- Same seed + same stamps + same player count reproduces map, mail schedule, and perk offers. Player actions diverge outcomes.
- A "Daily Seed" (UTC date hashed) is offered in the lobby with a fixed stamp set for leaderboards (deferred: leaderboard backend).

## 6. Postage Stamps (difficulty modifiers)

Selected in the lobby by the host. Each stamp has a score multiplier and a tier requirement.

| Stamp | Tier | Effect | Score × |
|---|---|---|---|
| Rush Hour | 1 | Delivery phase −30 s | 1.10 |
| Cursed Mail | 1 | 10% of mail is Cursed: misdelivery spawns a mini-raid on the spot | 1.15 |
| Heavy Load | 1 | Package mix skews one size class larger | 1.10 |
| Double Raids | 2 | Raids also occur in the first 60 s of Delivery | 1.25 |
| No Roads | 2 | Roads are dirt paths: vehicles 40% slower off asphalt | 1.15 |
| Postal Audit | 3 | Complaint meter decays 50% slower; Inspector at ≥ 50 | 1.20 |
| Megamail | 3 | All mega enemies 2× as frequent | 1.25 |
| Skeleton Crew | 3 | Quota uses playerScale(n+2) | 1.30 |

Multipliers are multiplicative. Score is totalEarned × product(stampMultipliers) and is shown on Results. (Eight stamps; Brownout is not in Arcade v1. Chapter 13 adds a ninth, "Trainee", as a difficulty floor and trims the v1 set.)

## 7. End conditions

| Condition | Result |
|---|---|
| Quota met on shift 5 and Draft completed | Victory |
| Quota missed at any Payday | Run Over |
| Post Office HP reaches 0 | Run Over (immediate) |
| All players disconnect | Run ends without Results; server shuts down after 60 s grace |
| Host quits | Run ends for everyone (no host migration in v1). Results shown with "Run abandoned". |

Individual player death never ends the run. If every player is dead simultaneously the run continues; respawns proceed as normal.

## 8. Solo and small-group adjustments

- playerScale(1) = 1.0 is the baseline; solo is the tuning reference.
- With 1–2 players, raid budgets are reduced by 20% and enemy target selection favours constructs over players so a solo player can keep delivering.
- The NPC driver at the Vehicle Depot is available from shift 2 in solo runs (shift 3 in groups) so a solo player has an "extra pair of hands" earlier. (Chapter 13 notes this is unaffordable in practice and drops the distinction.)

## 9. Free Play differences (for reference)

Free Play uses the same state machine with: no Payday fail, no Draft (perks replaced by the tech tree), Delivery of unbounded length with a day/night cycle, raids on a night schedule, persistent saves, and the full map archetypes including Land. Everything else in this spec applies.
