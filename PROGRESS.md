# Performative Mail — build progress

From-scratch build on `fable52/blank`. The spec is `design/drive/` (chapters 00–12). This file tracks
what exists, what is next, and every place the docs were silent and a choice was made.

## Starting point

- Branch root commit `66c56ab` contains only `design/drive/*.md`. No code, no project, no content.

## Target: M0 — Foundation (doc 12)

Netcode, movement, hand delivery on a fixed map. Standing rule: rough playable first, harden later.

### Rough playable loop (first goal)

- [x] Solution skeleton: `PerformativeMail.Sim` (plain C#, no Godot), Server, Client, App, xUnit tests
- [x] Content as data (`content/`): mail kinds, mix, destinations, containers, balance, test map
- [x] Sim: 30 Hz tick, player movement + tile collision, grid containers, mail spawn into Intake,
      mailbox acceptance / misdelivery, team wallet
- [x] Net: binary messages, protocol hash, loopback transport, ServerRuntime / ClientRuntime
- [x] Headless bot that walks Intake → mailbox and delivers (the smoke proof of the loop)
- [x] Godot host window: world view, camera (first person since 2026-11-11), minimal HUD (timer, wallet, interact prompt)
- [x] Godot headless smoke that boots the project and exercises spawn → pickup → deliver → wallet

**The rough loop is done.** `godot --path .` hosts over loopback on the test street: walk (WASD,
mouse look, Shift, Space), open the Intake with E, click manifest rows to take mail, select a hotbar
slot (1–8 / wheel), hold E at a mailbox to deliver, F delivers every matching stack; the HUD shows
shift / phase / timer, quota, wallet, the prompt with its match mark and the event feed.
`tools/smoke.sh` proves it headless. A human playtest of `b2b9159` passed.
Since then: G starts the run / readies / continues, Esc frees the mouse (and pauses a solo run), Tab
opens the inventory — drag a stack to move it, Ctrl+drag takes half, R turns it, right-click puts it
back, a click sends it across, and the buttons sort.

### First person and the playtest nits (after the b2b9159 playtest)

- [x] First-person camera (`FirstPersonCamera`): at `SimConstants.EyeHeight` (1.6 m) on the local
      player, mouse look = yaw + pitch in `ClientInput`, local body not drawn (only the held mail),
      orbit/boom rig removed. Doc 00 "Perspective and art" updated (Evan, 2026-11-11).
- [x] Interaction ray from the camera: `ViewRay` (eyes + yaw/pitch) against an `AimBox` per
      interactable; the prompt only shows for what the crosshair is on, within interact range.
- [x] Nit 1 — boot view: the map gives `postOffice.spawnLookAt`; server and client start the player
      facing it (down the street), and the smoke checks the camera is at the eyes, outside every
      solid tile, with 8 m of clear view.
- [x] Nit 2 — match mark: the default font has no ✓ / ✗ glyphs (they drew as nothing). The mark is
      now drawn as lines (`MatchMark`) beside the words MATCH / WRONG ADDRESS, and `HotbarAssist`
      selects picked-up mail so a stack is actually selected at the mailbox. The smoke checks both
      marks appear and that no text on screen lacks a glyph (`FontCheck`).
- [x] Nit 3 — clipped top row: every HUD element keeps a 28 px safe margin; the smoke checks the
      top row lies fully inside the screen.
- [x] Smoke asserts the camera sits at the player's eye position (every frame against the local
      view, and at the end against the server position) and aims along the interact ray.

## Feature batch after the first-person pass (planned, checked off as it lands)

Order: the run / shift loop of doc 01 first (it is M1 scope, pulled forward), then the M0 hardening
list below.

### Run / shift loop (doc 01 §1–§3, §5.1, §7)

- [x] Sim: run state machine Lobby → Prep → Delivery → Payday → (next shift's Prep | Results) →
      Lobby, phase clocks from `balance.json`, host-only Start / Continue, Ready ends Prep early
      (`SimWorld.Run.cs`, `RunLoopTests`)
- [x] Sim: quota check at Payday, shift summary (earned vs quota, deliveries by kind, misdeliveries,
      late, MVP), zero-misdelivery shift bonus, run results (outcome, score, XP)
- [x] Sim: mailboxes only accept mail during Delivery; join only in Lobby / Prep; an empty server
      abandons the run after the grace period; a finished run resets for the next one
- [x] Net: requests (start, ready, continue), events (shift summary, run results), host and ready
      flags replicated, protocol schema bumped to `m1.0`
- [x] Client: phase-aware HUD (phase line, hints, ready count), Payday summary and Results panel,
      the "go" key (G: start / ready / continue), event feed lines
- [x] Bot plays whole runs (start, ready, deliver to quota, continue); Net.Tests cover a full run
      (`RunFlowTests`)
- [x] The Godot host runs the real loop (boots into the Lobby; `--quick` keeps the old straight-
      into-a-shift start), and the smoke walks Lobby → Prep → Delivery → Payday → … → Results →
      Lobby, checking the HUD and the panel at each step against the server
- [x] Postal Rank XP kept in a local MetaProfile (doc 01 §5.1, doc 08 §3.8; XP only):
      `src/Client/Meta` (`MetaProfile` JSON, `ProfileTracker`, `FileProfileStore` on
      `user://profile.json`), saved at every Payday and at Results, rank line on the HUD in the
      Lobby and on the Results panel ("RANK UP!" when it rose), `ProfileTests`, smoke checks the
      saved file
- [x] Solo pause (doc 01 §1 "Shift clock", doc 09 §2.12): the host of a solo run stops the server
      tick with Esc and resumes with Esc or a click; `SetPaused` request, `PausedChangedEvent`,
      PAUSED on the HUD, protocol `m1.1`; `RunLoopTests`, `RunFlowTests`, a pause step in the smoke

### Playtest nits on fa44a16 (`logs/fable52/nits-fa44a16.md`, 2026-11-12)

- [x] Nit 1 — huge world labels: house numbers, mailbox addresses, INTAKE, POST OFFICE and name
      tags are `SizedLabel3D`: real size from far away, but never more than 5 % of the screen height
      per line up close (`LabelSizing`, unit-tested; the smoke checks every label every frame). House
      numbers moved from above the ridge to above the wall that faces the mailbox, where the roof
      does not hide the now-small number from the street.
- [x] Nit 2 — a manifest click put a Small Package (1×2) upright into the inventory although the
      hotbar had room: quick-move tried every target unrotated before any rotated. Now the order of
      the targets wins: the hotbar is tried upright and on its side before the inventory (choice 37).
      A click on a carried stack while the Intake is open still moves it "across", back to the
      Intake — that is the rough panel's one rule (its hint line says so) until drag and drop lands.
- [x] Nit 3 — manifest rows moved under the cursor as mail arrived: `ManifestList` keeps the rows
      still while the Intake is open (address order when it opens, new addresses at the bottom, an
      emptied row stays in place greyed out; choice 38). The list widget is updated in place.
- [x] Nit 4 — an emptied hotbar slot stayed selected: `HotbarAssist` selects the next mail stack,
      or the hands (slot 1) when no mail is left.
- [x] Nit 5 — a very fast E tap could fail to open the Intake: the press is now also taken from
      the input event (a tap shorter than a frame never shows when polling), and a press is
      remembered for 0.25 s, so one made while the previous request is still unanswered counts.
- [x] Nit 6 — complaint 20 after one 4-letter misdelivery: checked, left as is. Doc 01 §3.3 says
      "Misdelivery: +5 (letters) to +20 (cargo)" per item, `kinds.json` has `complaintOnMisdelivery`
      5 for a letter, and 4 letters × 5 = 20. The docs are followed; whether a whole stack should
      count once is a design question for Evan, not a bug.

### Hardening toward the written M0 acceptance criteria (after the rough loop)

- [x] ENet transport (listen server + remote clients), protocol hash rejection (design choice 42).
      The smoke ends with a second client joining the host's socket, walking, and leaving, and a
      wrong-hash client being turned away; `tools/soak.sh` plays N processes over real sockets.
- [x] Inventory panel: drag and drop / rotate / sort, optimistic ops with rollback (design choice
      43). `InventoryPrediction` shows a Move / QuickMove / Sort at once (faint until confirmed) and
      drops it again if the server refuses; `InventoryDrag` is the stack on the cursor; the panel
      has sort buttons and shows a chest as a second grid. 9 Net.Tests (latency, refusal, split,
      chained moves, a container closing under a move, drag / rotate / half / merge) and a smoke
      stage that drives the real panel with mouse events against a chest.
- [x] 8 clients / 10 min soak with container hash check on every version (design choice 44).
      In-process (`AcceptanceTests`, 18 000 ticks, 33 ms each way, 2 % loss): 2 764 container
      versions verified, 0 desyncs, 558 stacks delivered. As eight Godot processes over ENet
      (`tools/soak.sh 8 600 1`, run twice, both SOAK OK): every process 0 desyncs and 0 bad
      packets, 286–414 versions verified each (≈ 2 800 in total), 49–80 stacks delivered per bot.
- [x] Prediction error < 10 cm RMS at 200 ms / 5 % loss (`AcceptanceTests`, ten minutes each, the
      `LoopbackHub` conditioner). 100 ms each way + 5 % loss: rms 0.23 cm, worst single correction
      24.5 cm. 200 ms each way + 5 % loss: rms 0.23 cm, worst 25.0 cm. 100 ms each way without
      loss: 0 — latency alone never corrects. A correction happens only when three input packets
      in a row are lost (each packet repeats the last three inputs). Known limits: the conditioner
      has no jitter; and after a *server* stall of a second or more (seen on this shared machine
      in the ENet soak, see the tick item below) a guest is pulled back once by up to a few
      metres, because the inputs that queued up meanwhile run into the server's speed clamp.
- [x] 10 000-op two-player chest fuzz: no duplicate / lost item (`ChestFuzzTests`, three seeds at
      0 / 67 / 167 ms of latency each way, through the real clients and the wire). Each run: two
      players at one chest send 10 000 moves, splits, merges, quick-moves, sorts and nonsense in
      ~3 500 ticks, deciding on their own predicted picture, now and then closing the chest under
      a move in flight. After every server tick: the count of every kind of thing and every piece
      of mail is what was given, no entry id in two places, no two stacks on one cell, no stack
      above its limit; at the end both mirrors hash-equal the server with 0 desyncs. About 55 %
      of the ops are accepted, 4–8 % lose a race for a stack the other took (`UnknownEntry`).
- [x] Delivery / misdelivery pay exactly per ch. 03 §2.2 (unit tests)
- [x] Server tick ≤ 2 ms with 8 players on the test map (`TickStats`, design choice 44; judged on
      mean and p99 after a 150-tick warm-up). In-process, 8 bots, 17 850 ticks: mean 0.009 ms, p99
      0.04 ms, worst 0.42–0.66 ms. Inside the Godot host with 7 guests over ENet, ten real minutes
      (18 003 ticks), two runs: mean 0.29 / 0.33 ms, p99 0.50 / 0.74 ms.
      **Not clean:** both ENet runs had a handful of very slow ticks (second run: 34 over 5 ms out
      of 18 003; the first run's log stopped at its cap of 20; the worst 2.2 s and 1.9 s). The slow-tick log of the second run says where the time
      went: all but two of them sat inside ENet's `Service` / send calls (sockets), not in the sim
      step (the two sim ones: 13.8 ms at the first Payday, 5.5 ms), and the collector accounts for
      one of them (154 ms). The machine was not idle either time — `/proc/pressure` showed IO and
      memory "full" stalls of 1–2 % and other sessions were compiling — so I read these as the
      process being stalled by the machine, and the in-process run (no sockets, same sim, worst
      tick under 1 ms) agrees. It is not proven; the soak should be repeated on a quiet machine,
      and `tools/soak.sh` prints the slow ticks so the answer is one run away.
- [x] ContentValidator CLI (`tools/ContentValidator`, `tools/validate-content.sh`, design choice
      45): "content OK: 6 mail kinds, 4 destination types, 10 containers, 11 items, 1 maps, 32
      street names; content hash b55d7575", or one `error:` line per problem and exit code 1.
      Five tests in Sim.Tests; `tools/smoke.sh` runs it before the game.

All of the hardening list is done. What is left of M0 as doc 12 writes it and is not covered by
a test here: the "2 real players" of the soak, and a tick-time run on a quiet machine.

## Feature batch from the concept video (2026-11-15)

Order given by Evan: (1) the look and the systems of the concept video, (2) playtest nits 7–11,
(3) further features from `design/drive/` in the docs' milestone order.

### 1. The concept video is the reference (`design/reference/higgsfield-01/`, nit 12)

Evan's video (`/workspace/repos/refs/higgsfield-ref.mp4`, 15 s; frames every 0.5 s are in
`design/reference/higgsfield-01/frame-001…030.jpg`, the mp4 itself is not in the repo). His look
direction: "remove the low poly, more like satisfactory". Doc 00 "Perspective and art" now says so.

What the video shows, frame by frame:

- **Frames 1–6, the town at golden hour.** First person on a dirt path between colourful clapboard
  houses (red, blue, yellow, green, magenta) with white trim, white-framed windows, dark shingle
  gable roofs, porches and glowing wall lanterns; white picket fences, red street lamps, benches,
  round bushes and round-canopy trees, boulders, flowers; townsfolk in caps. A pink-to-purple
  sunset sky with a big sun disc, bloom and haze. Smooth, rounded, saturated shapes — no facets.
- **Frames 7–12, a fight.** A helmeted dark-red raider charges; the player swings a big
  stone-headed hammer (lower right of the view), then fires a "mail cannon" (cream and red body,
  envelope emblem, glowing orange ring barrel): an orange blast, dark smoke with embers.
- **Frames 13–16, loot.** Three glowing gold coins fly to the player; a dark-blue rounded card
  panel titled "Heavy Mail" shows three item cards with $ values, the best one outlined in gold.
- **Frames 17–22, the vista.** The island from a cliff: sea, a harbour dock with crates, red and
  white mail trucks, red boats, conveyors carrying boxes up to a wooden tower.
- **Frames 23–30, the night raid.** Blue night; a spiked-log palisade with a gate gap, two roofed
  watch towers, torches; a giant horned boss with a swarm of small red creatures.
- **HUD in every frame.** Top centre: a thin orange bar, a label and a long white progress bar.
  Bottom left: red health bar with number, a 5-segment white stamina bar, money. Bottom centre: a
  hotbar of teal rounded squares (slot number top-left, count bottom-right, the selected one gold
  and glowing). Bottom right: tracker rows (icon, name, timer). A small ring crosshair.

Visuals (all built in code; there are no art assets yet):

- [x] Sky, light and atmosphere (`Atmosphere`, `Shaders.Sky`): sunset gradient sky with a sun disc
      and drifting cloud bands, warm low key light with soft shadows, glow, ambient occlusion, pink
      haze; a blue starlit night with a moon-coloured key light and warm pools under the lamps,
      porch lanterns and torches (`Atmosphere.TargetNight` blends between the two)
- [x] Town (`HouseBuilder`, `SceneryView`, `Shaders.Ground / Clapboard / Shingles / Foliage`):
      clapboard houses in the video's colours with white trim, framed windows, porches with rails,
      shingle gable roofs, chimneys and glowing lanterns; a cobbled dirt path with soft edges, grass,
      picket fences with a gate gap at each mailbox, red street lamps, benches, round bushes,
      clumped-crown trees, boulders, flowers; the Post Office with awning, sign and loading yard
- [x] The island around the street (`IslandView`): terrain falling to cliffs and the sea, a harbour
      dock with crates, mail trucks, boats, a conveyor carrying parcels up to a wooden tower; the
      spiked-log palisade with its gate, two roofed watch towers and torches at the east end of
      the street
- [x] The town is solid: trees, lamps, benches, boulders and fences stop a walker (fences can be
      jumped); `Scenery` places them from the map hash, so server, clients and bots agree without
      a byte on the wire (`SceneryTests`, choice 46)
- [x] Characters (`CharacterModel`, `PlayerView`, `EnemiesView`): smooth carriers with caps;
      raiders in helmets, the horned giant, the small red swarm — they walk, wind up, strike,
      flash when hit and fall; a health bar over each hurt enemy
- [x] First-person hammer and mail cannon (`WeaponModels`, `FirstPersonCamera`), the swing and
      the shot; blast, smoke, embers, coins flying to the carrier (`CombatEffects`)
- [x] HUD in the video's layout (`Hud`, `HudBars`, `LootPanel`, `GridView`): top-centre quota bar,
      phase line and clock bar; health with its number and five stamina blocks bottom left, the
      feed above them; the teal hotbar with a gold selected slot; tracker plates bottom right
      (wallet, quota / rank, backlog, deliveries, raid, the own death bag); ring crosshair; loot cards over the
      hotbar; a red flash when hurt, the raid banner, the DOWN countdown

Systems (server-authoritative, unit-tested):

- [x] Health, regeneration, death and respawn at the Post Office; stamina for sprinting
      (all of the fight's sim tests are in `CombatTests`)
- [x] Attack input; the hammer (melee arc) and the mail cannon (a package shot with a blast)
- [x] Enemies: raider, swarmling, horned giant — chase, attack, die; replicated in snapshots
      (`CodecTests` for the wire)
- [x] The night raid: the last part of Delivery on shifts 2–5, warning, pulses from the wave
      budget of doc 05, enemies flee when the shift ends
- [x] Loot: coins fly to whoever is near and pay the wallet; item drops go to the inventory; the
      loot cards show what a kill gave (`LootCards`, `CombatHudTests`)

The smoke plays the fight too (see "Commands"): a raider felled with the hammer, a giant shot
with the mail cannon, the loot on the cards and in the wallet; and, since the death bag
(section 3), the host going down and fetching their bag.

### 2. Playtest nits 7–11 (`logs/fable52/nits-fa44a16.md`, 2026-11-15)

- [x] Nit 7 — split stack counts are hard to read: the count is `x12` at the stack's lower right,
      14 px white on a 5 px dark rim, set over the stack's edge (`GridView.DrawStack`; checked in
      the smoke's `match.png` and `chest.png`)
- [x] Nit 8 — `slow tick 295: 6.7 ms network` when a guest joined: measured, and it is not the join
      message (124 bytes, 0.02 ms to build and write) but the first use of the ENet path in the
      process. The host now walks that path once at boot (`EnetWarmUp`: it knocks on its own port
      with a Hello the server refuses) and the start of a shift in a scratch world (`SimWarmUp`,
      which also removes the 7 ms first tick of the first Delivery). Host + late guest over ENet:
      no slow tick at the join, at the shift start or when the guest leaves
      (`WarmUpTests`; design choice 56)
- [x] Nit 9 — a chest placed in the map: `content/maps/test_street.json` has a `"chests"` list, and
      one chest stands in the Post Office yard against the hall's east wall, north of the Intake.
      It is a solid thing (collision and the bots' paths go round it), its 8×4 grid exists from the
      start of the world, "Open Chest  [E]" / "Close Chest  [E]" open and close it, walking away
      closes it, and a new run starts with it empty (`MapChestTests`,
      `ContentValidatorTests.AChestInAWallOrOfAnUnknownKindIsAProblem`,
      `InteractionTests.TheMapsChestOpensOnInteractAndClosesWhenTheCarrierWalksAway`; the smoke
      walks to it, opens it through the prompt and does its inventory gestures in it:
      `chest-prompt.png`, `chest.png`; design choice 57)
- [x] Nit 10 — playtest flags, for a host and a `--join` guest alike (they travel in the Hello):
      `--playtest-no-timer` (the shift clock stands while that player is in the game; the HUD says
      "(stopped)" and the host's go key ends the shift) and `--playtest-mailbox` (that player
      starts, and starts every shift, before a mailbox of their own, looking at it, with three
      letters for it in the hotbar). Off by default; the smoke's ENet guest joins with them
      (`PlaytestTests`, `PlaytestFlowTests`, `CodecTests`; design choice 58)
- [x] Nit 13 — the fight can be playtested live, by a host and a `--join` guest alike: `--shift=N`
      (the run starts at shift N, with its quota and its raid), `--no-quota` (a missed quota does
      not end the run: Payday says "playtest: the run goes on" and the next shift comes) and
      `--raid` (every shift's raid is announced as the shift starts and is there 5 s later; shift
      1, which has none, borrows shift 2's). Off by default, listed under "Commands".
      `tools/smoke.sh` has a second Godot run, `--smoke --shift=2 --no-quota --raid`: the host
      fights the whole scheduled raid (15 raiders in 6 pulses) through the real HUD, takes the
      loot, misses the quota and stands in shift 3's Prep (`PlaytestTests`, `PlaytestFlowTests`,
      `CodecTests`; protocol `m1.5`; design choice 59)
- [x] Nit 11 — player ids are reused: a player's id is its seat (seat 0 is Player#1), and a
      joiner takes the lowest free seat, so a guest who leaves and rejoins is Player#2 again
      (`SimWorldTests.ALeaversIdAndSeatGoToTheNextToJoin`,
      `CombatTests.WhoeverTakesALeaversId_InheritsNothingOfTheirFight`,
      `LoopbackTests.AGuestWhoLeavesAndComesBack_IsPlayerTwoAgain_OnEveryScreen`; design choice 60)

### 2b. Playtest nits 14–18 (`logs/fable52/nits-fa44a16.md`, 2026-11-17)

- [x] Nit 16 — objects leaked at exit (seen on both smoke passes here, in most runs: one `Image`
      and one `DummyTexture`, at times ENet peers too). Cause: a C# `Variant` that carries an
      object holds a reference of its own until the garbage collector finalises it, and what is
      still held that way when the game quits is let go after the renderer has shut down. Two
      places did that: the ground map texture handed to the ground shader (`Shaders.Ground`), and
      the event arrays of ENet's `Service` (every event kept its peer). Both are now disposed
      where they are used (`using`), as are the ground material's C# handle and the ENet host and
      peer on `Close`. `tools/smoke.sh` now fails on any "leaked at exit" line; four runs in a row
      were clean.

### 3. Further features from `design/drive/`, in milestone order

The order is doc 12's M1 "Systems" list (the next milestone after M0, whose hardening list is
above). A box is ticked when the whole line of the doc exists; the sub-lines say which parts do.

- [ ] World generation pipeline (all stages) for Small Island; worldHash; client-side
      regeneration; SeedViewer tool
      - [x] `worldHash` checked at connect (over the static map and the content, choice 3)
      - [ ] the generator itself, regeneration on the client, the SeedViewer
- [ ] Districts and per-shift unlock; address assignment; street names; house model variants and
      colour coding
      - [ ] all of it: the test street has one street, fixed addresses, and colours from the video
- [ ] Run state machine with all phases; quota; spawn budget and mail mix; complaint meter;
      deadlines; death and respawn; death bag
      - [x] Lobby, Prep, Delivery, Payday, Results; quota; spawn budget and mix; the complaint
            meter; deadlines and late value (the run / shift loop above)
      - [x] death and respawn (section 1, with the fight)
      - [x] **Death bag** (doc 01 §2.2 "Player death", doc 03 §3.1, doc 11): who goes down leaves
            hotbar and inventory in a bag where they fell, 30 s on the clock. Anyone in reach opens
            it like a chest ("Open your bag  [E]   ·   gone in 00:18", "Open Cass's bag …") and
            takes from it or puts into it; emptied, it is gone at once. When the time is up its
            mail is back in the Intake and the rest in its owner's pockets. On the HUD: an own-bag
            tracker plate with the countdown, feed lines, a leather satchel on the ground that
            throbs in its last 8 s. Protocol `m1.6` (`BagDropped`, `BagGone`, bags in the Welcome
            for a late joiner). `DeathBagTests` (9), `DeathBagFlowTests` (3), `CodecTests`; the
            smoke fells the host, walks them back and takes it all out again (`bag-down.png`,
            `bag-prompt.png`, `bag-open.png`); design choice 61
      - [ ] the Generating, Raid and Draft phases as phases of their own (the raid runs inside
            Delivery, choice 50; Draft waits for perks)
- [ ] Shop (blueprints, bike, consumables, refined materials); bike as first vehicle (driver
      prediction path)
- [ ] Perk system (StatSheet, modifiers, team/personal, prerequisites, exclusions) with ~12 perks
- [ ] Lobby with seed, archetype, kit; join-in-progress in Prep; disconnect grace
      - [x] a Lobby the host starts from; joining in Lobby and Prep
      - [ ] seed / archetype / kit choice in the Lobby; the 120 s disconnect grace
- [ ] Content pipeline: JSON defs, validator in CI, hot reload
      - [x] JSON defs and the validator (`tools/validate-content.sh`, run by `tools/smoke.sh`)
      - [ ] a CI job; hot reload
- [x] Results screen and MetaProfile persistence (XP only) — the run / shift loop above. The
      unlocks table is not wired yet.
- [ ] Resource nodes and harvesting (wood, fiber, stone, ore); hand-built Wooden Wall and Chest
      from harvested materials
      - [x] a chest in the world, placed by the map (nit 9)
      - [ ] nodes, harvesting, building by hand

Next in this order: the disconnect grace (it is what a leaver's bag is waiting for), then perks
with the Draft phase.

## How to build and check

See "Commands" at the bottom (filled in as the tooling lands).

## Design choices where the docs are silent or ambiguous

(Each entry: the question, then the choice.)

1. **Godot allows one C# script assembly per project; doc 07 lists Server / Client / App as separate
   modules.** Choice: `src/Sim`, `src/Server`, `src/Client` are plain class libraries with *no Godot
   references* (ServerRuntime, ClientRuntime, loopback transport, bot). Everything that needs Godot
   (ENet transports, Presentation, UI, Input, App) is compiled into the single root Godot project
   `PerformativeMail.csproj`, which references the three libraries. Folder layout still follows doc 07.
2. **Sim target framework.** Doc 07 says `netstandard2.1` for Sim and .NET 8 for the game. Sim is
   `netstandard2.1`; Server / Client / Godot project / tests are `net8.0`.
3. **M0 has no world generation (M1).** The test map is a hand-authored static file
   `content/maps/test_street.json`. Its `mapHash` (FNV-1a 64 of tile + address tables) stands in for
   `worldHash` and is checked at connect.
4. **Run state machine (doc 01 §1), replacing the old "always Delivery of shift 1" stand-in.**
   Implemented phases: Lobby → Prep → Delivery → Payday → next shift's Prep, or Results → Lobby.
   Not yet: Generating (the map is static, choice 3), Raid (M2), Draft (skipped until perks exist —
   Payday goes straight to the next Prep). Mail spawn budget per shift is still
   `quota(shift, n) × spawnOverhead`, as in ch. 01 §3. The details the docs leave open are choices
   25–36. `SimSettings.QuickStart` keeps the old behaviour for tests, soaks and bots that only care
   about the delivery loop: a run starts as soon as a player is present, Prep is skipped, Results
   returns to the Lobby by itself after `paydaySeconds`, and joining is allowed at any time.
5. **Where the transport interfaces live.** `IServerTransport` / `IClientTransport`, the message
   schemas and codecs are in `src/Sim/Net` (doc 07 lists `Net/` under Sim), so Server and Client only
   depend on Sim. `LoopbackHub` (src/Server/Transport) serves any number of in-process clients and
   doubles as the M0 network conditioner (delay in ticks, loss on the unreliable channel).
6. **Protocol hash.** FNV-1a 32 of a hand-maintained schema text in `Protocol.Schema` (one line per
   message layout) plus `ContentDb.ContentHash`. Changing a layout means changing that text.
7. **Snapshots are not delta-compressed yet.** Every snapshot carries all players (interest stub:
   everything global). Other players are quantised to 1 cm; the receiver's own movement state is sent
   as raw floats so prediction replays from exactly the server's state. The global block (wallet,
   complaint, quota progress) rides every 6th snapshot.
8. **Speed clamp (doc 06 §7).** The server simulates movement itself from inputs, so the clamp is on
   input consumption: a per-player credit refills at 1.15 inputs per tick (burst 4, max 3 per tick).
9. **Mail on a leaving player.** M0 has no death bags; a disconnecting player's mail returns to the
   Intake (or backlog) immediately instead of the 120 s freeze of doc 06 §6.
10. **Bot pathing.** `DeliveryBot` uses a breadth-first tile path (`TilePathfinder`, client side). The
    routing graph of doc 02 arrives with world generation in M1.
11. **Godot-free client logic.** The interact prompt and its requests (`InteractionController`), every
    HUD string (`HudText`) and the event feed (`EventFeed`) live in the plain `src/Client` library so
    xUnit covers them; the Godot nodes only draw them. `src/Client/{Presentation,UI,Input}` are
    excluded from that library and compiled into the root Godot project instead (choice 1).
12. **Namespace of the input layer.** The folder is `src/Client/Input` (doc 07) but the namespace is
    `PerformativeMail.Client.InputHandling`: a namespace ending in `.Input` would hide `Godot.Input`
    from every `PerformativeMail.Client.*` file.
13. **Input map.** The default bindings of doc 09 §4 are registered in code at boot
    (`InputActions.Register`), not serialized in `project.godot`; an action already present in the
    project is left alone, so rebinding can come later without a migration.
14. **How a delivery is aimed.** Doc 09 has a hold to insert. Rough loop: the stack in the *selected
    hotbar slot* is what a held E inserts (slot 1 is hands); F delivers every carried stack that
    matches the mailbox, hotbar then inventory, one per hold time. The prompt always shows the
    stack's address beside the mailbox's with ✓ or ✗ — a mismatch is never hidden, and never blocked.
15. **Intake UI.** The Intake is `view: "manifest"`: one row per (address, kind), a click quick-moves
    the oldest stack of that row. A container whose def has another `view` (the chest) is drawn as
    a grid beside the player's own; own containers are grids too (choice 43 for what the mouse does).
16. **Greybox visuals.** No art yet: boxes and capsules built in code from the `WorldMap`. A house,
    its mailbox, its manifest row and the stripe on its mail share one colour (`Palette`, by the
    destination's `variant`), as a readability aid on top of the printed address.
17. **Host loop and the smoke.** `GameSession` drives client then server from `_Process`. Real time
    uses the frame delta; the smoke sets `FixedTicksPerFrame` (3) so a run is the same every time and
    takes seconds. The bot decides at `ClientRuntime.TickStarting`. After the deliveries the smoke
    switches back to the frame clock for 1 s to check that path too.
18. **`.gdignore` in `design/` and `tests/`** keeps Godot from importing docs and test projects.
19. **First-person details (doc 00 only says "first-person").** Eye height 1.6 m; FOV 75°; pitch
    clamped to ±1.45 rad; the body yaw is the view yaw (movement is relative to it). Pitch is
    client-side only and is not sent to the server. "Hands" are reduced to the held mail item
    drawn in the lower right of the view; the local `PlayerView` has no meshes.
20. **What the interact ray can hit.** No physics engine is involved: each interactable has an
    axis-aligned `AimBox` (mailbox: 1 × 2 × 1 m around post, box and address plate; Intake: 2 m
    cube around the counter and its sign). The target is the nearest box the view ray hits, among
    interactables whose ground distance is within `interactRange` (the server's range check is
    unchanged). The ray may be `range + 1 m` long because the eyes are above the ground point the
    range is measured from. An open Intake stays targeted while in range (the mouse is on its panel).
21. **Spawn facing.** `postOffice.spawnLookAt` in the map file (default: centre of the first
    street) gives `SpawnYaw`; it is part of the map hash. Players spawn facing it.
22. **Match mark.** Drawn, not typed (see nit 2), plus the verdict in words at the start of the
    prompt line: `MATCH · Deliver …` / `WRONG ADDRESS · Deliver …`.
23. **Hotbar assist.** Mail picked up while no mail is selected becomes the selected slot, and when
    the selected stack is delivered the next mail slot is selected. Selecting "hands" by key is
    respected until one of those two things happens.
24. **UI scaling.** `window/stretch/mode = canvas_items`, aspect `expand`: the HUD is laid out for
    1280 × 720 and scales with the window (this also gives the headless smoke a real screen size).
25. **How a run ends (doc 01 has RunOver and Victory screens).** One `Results` phase plus a
    `RunOutcome` (`Victory` after shift 5's quota, `QuotaMissed` otherwise). Lobby and Results have
    no clock (`PhaseEndTick = 0`); Results waits for the host's Continue, which returns to the Lobby.
26. **Who is the host inside the sim.** The longest-connected player (`Player.JoinOrder`); if they
    leave, the role passes to the next one (on a listen server the process dies with the host
    anyway, so "the run ends when the host quits" needs no sim rule). Only the host may Start
    (Lobby) and Continue (Results). The host may start at any time — no "all ready or 10 s
    override" rule in the Lobby yet.
27. **Ready.** A per-player flag, settable in Lobby and Prep, cleared on every phase change. Prep
    ends early the moment every connected player is ready; in the Lobby it is only shown.
28. **Mailboxes outside Delivery.** A delivery request in any other phase is refused with
    `OpResult.WrongPhase`; carried mail simply stays carried into the next shift (deadlines apply).
29. **Joining.** Only in Lobby and Prep (`SimWorld.AcceptsPlayers`); later a Hello is rejected with
    `RejectReason.RunInProgress`. Doc 06's mid-run join / reconnect comes with ENet.
30. **Empty server.** If nobody is connected for `emptyServerGraceSeconds` (60) during a run, the
    run is abandoned straight to the Lobby without Results. Phases keep advancing meanwhile.
31. **Shift bonus.** +10 % (`zeroMisdeliveryBonus`) of the shift's earnings when it had no
    misdelivery and earned more than 0; paid into the wallet at Payday. It is *not* part of the
    quota check (the check uses the earnings at the end of Delivery) and counts toward the score.
32. **Quota timing.** The quota shown in Prep is a preview from the current player count; the
    binding quota is sampled when Delivery starts. Shift earnings reset when Prep starts.
33. **Score and XP.** Score = money earned over the run (stamps come later). XP per player =
    `xpPerShift` × shifts completed + `xpVictory` on a win + `xpPerDelivery` × max(1, team
    deliveries ÷ players) (doc 11's worked example), computed once at Results rather than at each
    Payday (doc 06) — the totals are the same.
34. **What resets between runs.** Wallet, complaint and counters; the Intake, every player's
    containers (mail only) and the backlog are cleared; the spawner's random streams restart, so
    the same seed deals the same mail again. Players are *not* moved back to the spawn pad (no
    respawn exists until world generation, and a teleport would show as a prediction error).
35. **How the client learns of it.** Ready is a bit in the per-player `flags` byte of snapshots;
    host and outcome ride the global block; `ShiftSummaryEvent` and `RunResultsEvent` are reliable
    events sent just before the `PhaseChangedEvent` that opens Payday / Results.
36. **Meta XP numbers** (`xpPerShift` 100, `xpVictory` 50, `xpPerDelivery` 5, `rankXpPerRank` 500)
    live in `balance.json`; doc 11 gives the formula but not a rank curve, so 500 XP per rank is a
    placeholder.
37. **Quick-move order.** Doc 03 §3.2 says auto-place tries "stack merges first, then unrotated,
    then rotated" but not how that combines with several target containers. Merges are tried in
    every target first; then each target in order (hotbar before inventory) is tried unrotated and
    rotated before the next one, so mail lands in the hotbar whenever it fits there at all.
38. **Manifest order.** Doc 03 §3.2 gives the manifest "one row per (address, kind)" and no order.
    Rows are in address order when the Intake is opened and then hold still: later arrivals for a
    new address are appended, an emptied row stays (greyed, not clickable) until the Intake is
    closed, except at the very end of the list where dropping it moves nothing.
39. **World label size.** Labels are world-sized from far away (a house number reads from across
    the map) and capped at 5 % of the screen height per line up close (36 px at 720p).
40. **MetaProfile, as far as it goes.** Doc 08 §3.8 gives the file shape and doc 07 §7.1 when it is
    written (every Payday, and Results). The rest is chosen here:
    - The profile is the *client's* (each player keeps their own file); the server only reports XP
      in `RunResults`, the client adds its own row to its profile. `profileId` is `local:<guid>`.
    - `stats.deliveries` / `misdeliveries` count the player's own events as they happen, so a
      Payday save already holds them; XP, runs, wins, best score and the `recentRuns` line are
      added once, at Results. A run abandoned before Results gives no XP (doc 01 §2: XP "at
      Results").
    - Rank = 1 + rankXp / `rankXpPerRank` (rank 1 at 0 XP).
    - `seedString` is the numeric seed until seeds can be typed (doc 01 §1 seed strings are not in
      yet); `date` is the local date.
    - `unlocks`, `cosmetics` and `settings` are written empty and not read back: nothing fills
      them yet.
    - A profile that cannot be parsed is renamed to `profile.json.bad` and a new one started,
      rather than overwritten. A failed save is kept in `ProfileTracker.SaveProblem` and retried
      at the next save; it never stops the game.
    - Writes go to `profile.json.tmp` and are moved over the file, so a crash mid-write cannot
      leave half a profile.
    - `--name=X` renames the profile; without it the stored name is used (first start: "Postie").
    - The smoke never touches the real profile: in memory by default, or the file given with
      `--profile=FILE` (which `tools/smoke.sh` points at a temp file and then checks for XP).

41. **Solo pause.** Doc 01 §1 gives the rule ("server tick freezes; a paused flag is replicated for
    UI") and doc 09 §2.12 the key (Esc opens the pause menu, which "pauses the server in solo").
    The rest is chosen here:
    - Only the host, and only while alone on the server (`OpResult.NotSolo` otherwise); any phase.
      A second player joining, or the host leaving, ends the pause.
    - A paused `SimWorld.Step` does nothing but answer pause requests: the tick counter stands, so
      every clock that is a tick (phase end, deadlines, the empty-server grace) stands with it.
      Other requests wait in the queue and are applied on the first tick after the resume.
    - The flag travels as a reliable `PausedChangedEvent` with the tick the server stands on; no
      snapshots are sent meanwhile. The client sets its clock to that tick, and neither predicts
      nor sends input while paused. A joining player needs no flag: their join ended the pause.
    - There is no pause menu yet, so the freed mouse is the menu: Esc lets the mouse go and (solo)
      pauses; Esc again, or a click into the window, takes the mouse back and resumes. With other
      players present Esc only frees the mouse. Losing window focus does not pause.
    - While paused the HUD shows PAUSED and "The clock is stopped · [Esc] resume", the interact
      prompt is hidden, and E / F / G do nothing.

42. **ENet (doc 06 §2), as far as M0 needs it.** The doc names the transport and its three channels;
    the rest is chosen here:
    - Godot's low-level `ENetConnection` / `ENetPacketPeer`, not the high-level multiplayer API: the
      game has its own messages, so ENet only carries bytes (`src/App/Net/EnetTransport.cs`, behind
      the same `IServerTransport` / `IClientTransport` as loopback).
    - A host is a listen server: its own client stays on loopback (doc 06 §1 "in-process"), remote
      clients come in over ENet, and `CompositeServerTransport` (Godot-free, unit-tested) puts both
      behind the one `ServerRuntime`.
    - Channel 0 is sent unreliable *sequenced* with `FlagUnreliableFragment`: ENet drops a late
      packet instead of delivering it out of order (the doc says "unordered"; both ends only ever
      want the newest input / snapshot), and a snapshot above the MTU is fragmented, not promoted
      to reliable. Channels 1 and 2 are reliable ordered.
    - Default UDP port 24565 (`GameSession.DefaultPort`); `--port=N` picks another, `--port=0` any
      free one, `--solo` does not listen. A host listens by default. If the port cannot be bound
      the host plays on alone with an error line — a busy port must not stop a solo game.
    - 16 ENet peers for 8 players, so a ninth client gets the "server full" answer instead of a
      timeout. A rejected client is disconnected with `PeerDisconnectLater`, so the reason arrives.
    - Both ends flush the socket at the end of every frame instead of waiting for the next poll
      (otherwise every message would sit for a frame).
    - `--join=ADDRESS[:PORT]` is the whole join UI until there is a menu (M1). Refused / no answer:
      a line in the window (headless: exit code 3). Host lost after joining (doc 06 §5): the world
      stands still under a HOST LOST banner; there is no main menu to return to yet. Reconnect and
      the 120 s freeze of a disconnected player are M1 scope and not done.
    - ENet's own timeouts (5–30 s) decide when a silent peer is gone.
    - `--bot` lets a `DeliveryBot` play the process's client (as host: `--wait-players=N` holds the
      run until N players are in the Lobby); `--quit-after=SECONDS` ends it with `BOT OK` /
      `BOT FAIL`. Bots keep their profile in memory. `tools/soak.sh` starts a host and N−1 guests
      this way — the soak over real sockets.

43. **Inventory screen: prediction and the mouse (doc 09 §2.5, doc 06 §4).** The docs give "shows
    at 60 % until confirmed, snaps back on refusal", drag, R to rotate and sort buttons; the rest
    is chosen here:
    - `ClientWorld.Containers` stay the *confirmed* mirrors (hash-checked on every version). The UI
      reads `ClientWorld.Hotbar / Inventory / OpenContainer`, which are predicted views: the mirror
      itself while nothing is pending, otherwise a clone with the pending requests replayed through
      the same `InventoryOps` the server runs. A refused request simply drops out of the replay —
      that is the snap-back. `Prediction.Revision` is the redraw key.
    - Predicted: Move, QuickMove, Sort. Not predicted: Deliver (the mailbox answers within the hold
      time anyway), open / close.
    - A predicted split needs an entry id the server has not given yet: it gets a private one from
      `0x8000_0000` up. Such a stack is shown but cannot be picked up or clicked until the server's
      delta names it.
    - No cursor container on the server. A drag is client-only state (`InventoryDrag`, Godot-free);
      the drop is exactly one `Move`. While dragging, `Aim` runs the server's rules on clones of
      the predicted views each time the cell, the rotation or the revision changes: the outline is
      green or red, and a red drop is not sent (a text notice says why in the hint line; no sound
      yet). The server still validates everything.
    - The stack is held by the cell it was grabbed at; over a different stack of the same kind the
      drop is a merge onto that stack.
    - Mouse: drag = move the whole stack; Ctrl / Cmd + drag = the bigger half ((n + 1) / 2); a
      press and release within 6 px = quick-move (as before; shift-click is not a separate
      gesture); R (`rotate` action) turns the dragged stack; right-click puts it back. The context
      menu of doc 09 is not done.
    - Sort buttons ("Sort by address", "Sort by size") on the Inventory and on an external grid.
      Not on the hotbar (its cells are the number keys) and not on the Intake (a manifest).
    - Closing the panel, or the dragged stack disappearing (someone else took it), ends the drag.
    - The test street has no chest; `SimWorld.AddWorldContainer("chest", x, z)` puts one down for
      the tests and the smoke. Placing storage in the world is M1 (building).

44. **How the M0 acceptance criteria (doc 12) are measured.** The doc gives the numbers, not the
    method:
    - *"8 clients (2 real, 6 bots) … for 10 min, hashes match on every version."* Eight bots: no
      test can supply the two real players. Held twice — in-process (`AcceptanceTests`, ten minutes
      of game time over the loopback wire with 33 ms each way and 2 % loss) and for ten real
      minutes as eight Godot processes over ENet (`tools/soak.sh 8 600`). "Every version" is
      literal: each container delta carries the server's hash of that version and the client
      compares its mirror after applying it (`ContainerVersionsVerified` / `ContainerDesyncs`).
    - *"Prediction error … 200 ms simulated latency with 5 % loss … below 10 cm RMS."* The error
      is the distance the predicted position jumps at a reconciliation, over every snapshot.
      "200 ms" is tested both as the round trip (100 ms each way) and as each direction. The
      conditioner (`LoopbackHub`) delays by whole ticks and drops unreliable packets in both
      directions; it has no jitter. *"No visible rubber-banding at 100 ms"* is held as: latency
      alone gives no correction at all (≤ 1 cm asserted, 0 measured). `TheMeasureSeesAServerSide
      Correction` shows the instrument is alive (a 1 m server-side shove reads as 1 m, once).
    - *"Server tick ≤ 2 ms with 8 players."* `ServerRuntime.TickTimes` (`TickStats`): wall time of
      a whole tick (network in, sim step, events and snapshots out), skipping the first 150 ticks
      (JIT). Judged: the mean and the 99th percentile. Printed but not judged: the worst single
      tick — one GC pause or a descheduled process is not the game's cost. Ticks over 5 ms are
      logged (`[server] slow tick …`, the first 40) with the split network in / sim / out and
      the GC pause time since the last one, so a slow tick can be told from a stalled machine.
      A bot host fails its soak if mean or p99 exceed 2 ms.
    - *"10 000 randomized ops."* Per seed, not in total; three seeds, see `ChestFuzzTests`.

45. **ContentValidator (doc 07 §6).** The rules live in `ContentDb.TryLoad` — the game refuses to
    boot on the same list — and `tools/ContentValidator` is the command around it: every problem
    on its own `error:` line, exit code 1 (2 for a wrong command line). One rule is the tool's
    own: a `.json` under `content/` that no loader read is an error (a misspelt file name would
    otherwise be silently ignored). Of doc 08 §5 only the rules for def types that exist are in
    (unique ids, references, `acceptedBy`, mix shares = 1 ± 0.001, positive grids); perks, waves,
    archetypes, recipes and buildings get theirs with their loaders. `tools/smoke.sh` runs the
    validator before the game; its tests sit in Sim.Tests.

46. **Scenery (the video's town dressing) and what is solid.** The map file still only has tiles,
    houses and mailboxes. Everything else the video shows on the street — trees, bushes, boulders,
    lamps, benches, fences, flowers — is placed by `Scenery.Generate` (Sim, no Godot) from a random
    stream seeded with the map hash, so the server, every client and the bot derive the same props
    and nothing is sent. Rules: nothing on a street tile or within reach of one, nothing in front
    of a door, a walkway or a mailbox, fences leave a gap at the walk and the mailbox.
    Trees, lamps, benches, boulders and fences are solid circles / capsules in
    `PlayerMovement.ResolveCollisions` (shared by the server and prediction); bushes by a house
    corner are solid too, flowers are not. A prop lower than the walker's feet is passed over, so
    a 0.95 m fence can be jumped (jump height 1.2 m). `WorldMap.IsNavBlocked` marks tiles a walker
    cannot cross; `TilePathfinder` moved from the client to `Sim/World` (the bot, the tests' walker
    and the enemies all use it).
47. **The look is built in code.** There are still no art assets: `MeshKit` makes rounded boxes,
    lumpy blobs and clumps with smooth normals ("remove the low poly"), `MeshBatch` turns each
    (mesh, material) pair into one MultiMesh, and five small shaders (ground, clapboard, shingles,
    foliage, water, sky) do the surfaces. The whole town is about 1.3 million triangles in ~120
    draw calls. Forward+ on a GPU; the same scene also runs in the Compatibility renderer (the
    screenshots under xvfb), which needed `limits/opengl/max_lights_per_object` raised to 32 because
    a whole-town MultiMesh is one "object" to its 8-light limit.
48. **`--low` and the smoke on a software renderer.** `WorldView.Cheap`: no lamp lights, shadows,
    glow, ambient occlusion or anti-aliasing, and `MeshKit.LowDetail` builds every shape from about
    a fifth of the triangles. On llvmpipe at 1280×720 that is 70 ms a frame instead of 560. The
    smoke turns it on by itself when the adapter is llvmpipe (otherwise its ENet step, which has
    a 10 s wall-clock limit, cannot finish); `--low` turns it on for a player with a weak GPU.
49. **`--lookdev=DIR`.** A tour of fixed viewpoints (street, house, Post Office, gate, harbour,
    from above, two at night) saved as PNGs, with the frame time printed — the way the look was
    compared with the reference frames. `--lookdev-views=name:ex,ey,ez:tx,ty,tz[:night];…` adds
    views of one's own.
50. **What the video adds to the docs' fight.** Doc 05 has health, the Mail Bat, raiders and the
    night raid; the video adds a stamina bar, coins flying out of the fallen, loot cards, a swarm
    of small red creatures and a horned giant. So: sprinting spends stamina (five blocks, 8 s of
    sprint, 5 s to refill; empty, a carrier walks until one block is back); the `swarmling` is an enemy of its own
    (video only, not in doc 05's table); loot coins go straight to the team wallet in cents but
    count for neither the shift's earnings, the quota nor the score (those stay "mail delivered").
    The hammer is doc 05's Mail Bat drawn as the video's stone-headed hammer.
51. **Weapons without slots.** Hammer on LMB / right trigger, mail cannon on RMB / left trigger,
    always at hand whatever the hotbar holds — the video shows no weapon slot and the hotbar is
    for mail. The viewmodel is drawn during a raid and for 4 s after an attack (the cannon for
    2.5 s after a shot), otherwise the hands are free for the held letter, which moved to the
    lower left of the view. The click that recaptures the mouse is not a swing. The cannon fires
    a Small Package out of the carrier's own mail (hotbar first, oldest first); the package is
    gone, with no misdelivery penalty and no complaint — the price is the delivery not made.
52. **Enemies on the wire.** The enemy list rides every second snapshot (10 Hz), 9 bytes an enemy
    (id, kind + mega, position, yaw, state, health in 255ths); clients draw them 5 ticks in the
    past between two samples. Everything else of the fight is a reliable event. A late joiner is
    told the enemies but not the loot already lying on the ground (it can still be taken).
53. **The raid.** Comes through the gate at the east end of the street, in the last part of
    Delivery on shifts 2–5; the sky follows its state (0.55 night at the warning, full night while
    it lasts). A Mega's body in the sim is capped at `WorldMap.WalkerReach` so it fits the paths;
    it is only drawn larger. Between phases `ClearCombat` removes enemies, shots and loot and puts
    downed carriers back on their feet; on the return to the Lobby the carriers' containers are
    emptied.
54. **Loot.** A bundle lies for 15 ticks before it can be taken (so the coins are seen to drop),
    then goes to the nearest living carrier within 2.5 m; items that do not fit stay on the
    ground and are tried again every second until the despawn. The card value of an item is its
    sell price; the most valuable card is the gold one; cards stay 4 s after the last pickup and
    kills within that time add to the cards on show.
55. **The HUD follows the video, not doc 09's first sketch.** Top centre: quota bar (orange,
    green once met), the phase line, the clock bar (its full length is the longest time left seen
    in the phase). Bottom left: feed, weapons line (only while there is something to fight),
    health, stamina. Bottom right: plates for wallet, quota (rank in the Lobby), backlog and
    complaint, the delivery tally, the raid. The prompt row moved up to 62 % of the height to
    clear the loot cards. The feed folds a run of kills into one line ("You felled 3 Swarmlings").
    All characters, weapons and effects are procedural rounded shapes like the town (choice 47).
56. **Nit 8 asks whether the join snapshot can be spread out or made cheaper; it is already
    small.** Measured on a host with a guest joining 8 s in: the Welcome is 124 bytes; `HandleHello`
    takes 0.7 ms the first time (add the player 0.33, first ENet send 0.31). The 5.7–7.2 ms of
    "network in" were first-use costs: Godot making the C# wrapper of the first `ENetPacketPeer`
    (3.4 ms), the connect branch through the composite transport (2.1 ms), the first `GetPacket`
    (0.5 ms). Choice: nothing is spread out; the host pays these at boot. `EnetWarmUp` connects to
    the host's own port, sends a Hello with the wrong protocol hash and pumps the transport (not
    the world) until the refusal and the disconnect have come back: connect, receive, send and
    disconnect are then compiled, no player or player id is made, and the log is attached only
    afterwards. It costs one ENet connection id (real guests start at 2) and gives up after
    250 ms. `SimWarmUp` runs 90 ticks of a quick-start shift in a scratch world (about 20 ms at
    boot) for the same reason: the first Delivery tick was 7 ms, of which 2–4 ms GC. A slow tick
    that still shows up now and then on this box (e.g. 23 ms once, no game event near it) is the
    shared machine, not the game.
57. **Nit 9: where a placed chest lives.** Doc 04 has chests as buildings a player places (M2);
    until then the map file places them: `"chests": [{ "container", "at": [x, z], "yawDegrees" }]`,
    checked by the loader (a storage grid, not hand-carried; inside the map; not in a building
    tile; not on a street, which the tile paths need clear). Both ends know the chest from the map,
    so nothing new is replicated: it is a solid prop added after the grown scenery (the scenery's
    seed is `SceneryKey`, the hash of the ground alone, so adding a chest moves no tree), its
    container is made when the world is (ids right after the Intake's), and the Welcome carries
    the chests' container ids in map order (protocol `m1.3`; a count that differs from the map's
    is a map mismatch). Its things are the run's: it is emptied on the return to the Lobby like
    the pockets. The prompt needs the look (ray on a box a little larger than the model) and the
    client's 2.5 m; the mailbox wins when both are under the crosshair. The smoke also got a
    watchdog: a minute without a frame prints the stage and exits 3 (one run sat silent until the
    shell's 300 s timeout right after a build and did not do it again in 14 runs).
58. **Nit 10: the playtest flags are asked for by the client and granted by the server.** A flag
    on the command line would otherwise only reach a host. They are a byte in the Hello (protocol
    `m1.4`), kept on the player, and unknown bits are dropped; the server's log says who joined
    with what. No build is without them and no permission is asked: this is a co-op game among
    friends and the flags are for checking it, but they are off unless a player starts the game
    with them, and they end when that player leaves. *No timer*: while such a player is in the
    game the Delivery clock stands (the phase end, and a raid that has not begun, move one tick
    later every tick, so nothing else needs to know); the clients get `clockStopped` in the global
    block and freeze their own countdown; the host's go key, which does nothing in a running
    Delivery, ends a stopped one. Prep and Payday keep their clocks (they have a ready key and are
    short). *At a mailbox*: seat k (join order) gets the map's k-th destination; the player is put
    1.3 m in front of it on a free spot when joining and at every shift start, and at shift start
    gets three pieces of mail addressed to it (ids from 0xF0000000 up, outside the shift's own
    mail; delivered, they pay like any other). The client turns the camera to the box
    when the server says it parked the player (`PlayerParked`, to that player only).
59. **Nit 13: `--shift=N`, `--no-quota`, `--raid`.** Same road as nit 10: two more bits and a
    start-shift byte in the Hello (protocol `m1.5`), kept on the player, gone when the player
    leaves; `quotaWaived` joins `clockStopped` in the global block. *Shift*: a run starts at the
    highest shift any present player asked for (never past the last one); a tester who joins in
    the Prep of an earlier shift moves the run up to theirs, from that Prep's start; a shift
    under way is never replaced. Shifts skipped are not "completed": XP counts only quotas met.
    *No quota*: after a missed quota the next shift comes; after the last shift the run ends as
    ever, a victory only if that last quota was met. *Raid*: the warning comes on the shift's
    first tick and the raid 5 s later (`PlaytestMask.RaidNowSeconds`), in every Delivery while
    the tester is there, and at once (5 s) when they join a shift under way that has no raid
    yet. It is the shift's own raid (budget, kinds, six pulses 15 s apart), only early; shift 1
    has no budget, so it takes the first later shift that has one. It stays until the shift ends,
    like any raid, and `--playtest-no-timer` does not hold it back (the clock it stops is the
    shift's). With flags, `--smoke` plays the raid playtest instead of the full run, and wants
    exactly `--shift=2 --no-quota --raid`.
60. **Nit 11: free player ids are reused.** Decided yes: with at most eight players the number
    after the name is how people tell each other apart ("Player#2", the seat colour), and it
    should not creep up over an evening of joins. The id is now derived from the seat
    (`EntityClass.Player`, seat + 1) instead of a counter, so id, seat and colour always go
    together and no free-list is needed. What made the old rule safe was that an id never came
    back; so leaving now wipes every place the sim kept that id (queued requests, enemies'
    target and last-hit, shots in flight: a kill by a departed player's blow is nobody's), the
    client drops the player on `PlayerLeft` before any `PlayerJoined` (reliable, in order), and
    `PlayersView` rebuilds a view whose carrier object changed under the same id. Join order,
    not the id, still picks the next host. Other entity classes keep never-reused ids.
61. **The death bag (first of the M1 list, section 3).** It goes first because the piece it
    completes (death and respawn) came in with the fight, and until now going down cost nothing.
    Doc 01 §2.2 says the "contents return to PO Intake", doc 11 says "mail returns to Intake";
    doc 03 §3.3 says a despawn must never lose mail. What the docs leave open, and what was
    chosen:
    - *What is in it*: hotbar and inventory, every stack in the cell it had (the bag is the 8×4
      `death_bag` of doc 03 §3.1: row 0 the hotbar, rows 1–2 the inventory). Empty pockets drop
      no bag. A carrier may have several bags lying about; the tracker shows the one with the
      least time left.
    - *Who may open it*: anyone in reach, the owner too once they are up; it is a container like
      a chest, so things can also be put in. Emptied, it is gone at once. Someone who is down
      gets no interact prompt at all.
    - *When the 30 s are up* (`deathBagDespawnSeconds`; respawn is 10 s, so about 20 s to get
      back): mail goes to the Intake (to the backlog if that is full), everything else to the
      owner's inventory, then hotbar. What does not fit there, or has no owner any more, is
      lost: the Intake is for mail, and a bag nobody fetched should cost something. A phase
      boundary returns every bag the same way (`ClearCombat`), so no bag outlives its shift.
    - *Leavers*: leaving the game still sends carried mail straight back to the Intake, as
      before, and drops no bag; a bag the leaver had already dropped stays as "an unclaimed
      bag" (nobody's, so whoever takes the reused id inherits nothing of it). Doc 06's
      "the body drops a death bag" after the 120 s disconnect grace waits for that grace.
    - *Not yet*: the `second_wind` perk (no perks yet), the "+ overflow" of doc 03 (the pockets
      are 24 cells, the bag has 32), vehicles dropping a bag.
    - *Wire*: `BagDropped` (container, owner, place, despawn tick) and `BagGone` (returned or
      emptied, how much mail went back); the Welcome lists the bags lying about. The client
      takes a bag off its list only after the feed has heard `BagGone`, so the line can still
      say whose it was. The countdown on the HUD is the despawn tick against the server clock.

## Commands

All from the repo root. `-nodeReuse:false` avoids hangs on stale MSBuild nodes on this machine.

```
~/.dotnet/dotnet build PerformativeMail.sln -nodeReuse:false
~/.dotnet/dotnet test  PerformativeMail.sln -nodeReuse:false     # Sim.Tests + Net.Tests
# the M0 acceptance tests and the chest fuzz, with their measured numbers:
~/.dotnet/dotnet test tests/Net.Tests -nodeReuse:false --filter "Acceptance|ChestFuzz" --logger "console;verbosity=detailed"

tools/validate-content.sh [CONTENT_DIR] [--quiet]   # ContentValidator: "content OK …" / "error: …" lines, exit 0 / 1

tools/smoke.sh [seed]            # build + two Godot headless smokes (the full run, the raid playtest); exit 0 and "SMOKE OK" twice
~/.local/bin/godot --headless --path . -- --smoke --seed=1      # the smoke alone (build first)
~/.local/bin/godot --headless --path . -- --smoke --seed=1 --shift=2 --no-quota --raid   # the raid playtest's smoke alone

# the smoke in a (virtual) window, saving PNGs of the boot view and each prompt:
xvfb-run -a -s "-screen 0 1280x720x24" ~/.local/bin/godot --path . --rendering-driver opengl3 -- --smoke --shots=/tmp/shots

~/.local/bin/godot --path .                                      # play: host on the test street, in the Lobby (G starts)
~/.local/bin/godot --path . -- --seed=7 --name=Ada               # user args go after "--"
~/.local/bin/godot --path . -- --quick                           # skip Lobby and Prep: straight into shift 1
~/.local/bin/godot --path . -- --profile=/tmp/p.json             # another profile file than user://profile.json
~/.local/bin/godot --path . -- --low                             # weak GPU: no lamp lights, shadows, glow, AA; coarser shapes

# pictures of the world from a tour of viewpoints (and the frame time), then quit:
xvfb-run -a -s "-screen 0 1280x720x24" ~/.local/bin/godot --path . --rendering-driver opengl3 -- --solo --lookdev=/tmp/look
#   own views: "--lookdev-views=street:17,1.6,28.5:40,2.2,27;gate:70,3,28:92,3,28:night"  (name:eye:target[:night])

# multiplayer: a host listens on UDP 24565 by default
~/.local/bin/godot --path . -- --port=24600                      # host on another port (0 = any free one; --solo = do not listen)
~/.local/bin/godot --path . -- --join=192.168.1.20               # join a host (ADDRESS or ADDRESS:PORT), in its Lobby or Prep
~/.local/bin/godot --headless --path . -- --bot --wait-players=2 # a bot hosts and starts the run once 2 players are in
~/.local/bin/godot --headless --path . -- --bot --join=127.0.0.1 --quit-after=60   # a bot guest; ends with BOT OK / BOT FAIL

# playtest flags: off by default, for a host and a --join guest alike, in force while that player is in the game
#   --playtest-no-timer   the Delivery clock stands ("(stopped)" on the HUD); the host's G ends the shift
#   --playtest-mailbox    start, and start every shift, at a mailbox of your own with three letters for it
#   --shift=N             the run starts at shift N (1-5), with its quota and its raid
#   --no-quota            a missed quota does not end the run; the next shift comes
#   --raid                every shift's raid is announced as the shift starts and comes 5 s later
~/.local/bin/godot --path . -- --shift=2 --no-quota --raid       # host: G, G, and the fight is on
~/.local/bin/godot --path . -- --join=192.168.1.20 --raid --playtest-no-timer   # a guest brings a raid and stops the clock

tools/soak.sh [players=8] [seconds=600] [seed]   # host + guests as separate headless bot processes over ENet; "SOAK OK"
                                                 # (also judges the host's tick times; give it 120 s or more — in less, a bot may not get to deliver)
```

The profile (Postal Rank XP, stats, last 10 runs) is `user://profile.json`, on Linux
`~/.local/share/godot/app_userdata/Performative Mail/profile.json`; delete it to start at rank 1.

Godot needs to find the .NET runtime: either `dotnet` on `PATH` or `DOTNET_ROOT=$HOME/.dotnet`
(`tools/smoke.sh` sets both itself).

Godot loads the assembly from `.godot/mono/temp/bin/Debug/`, so run `dotnet build` after every code
change before starting Godot from the command line.

The smoke plays one whole run inside the real Godot host (about 10 s): it boots into the Lobby
(HUD says so, no mail yet), the host presses go, Prep shows the coming quota, Ready ends Prep, then
(before that, the solo host pauses: for ten frames the server tick, the client's clock and the HUD
clock must stand, and the HUD must say PAUSED; then it resumes), then
in Delivery the close-up checks below run at 3 ticks per frame; after them the rest of the run is
fast-forwarded at 30 ticks per frame with the bot delivering, and at every Payday and at Results the
panel on screen must equal `HudText` of the server's summary / results; the host continues and the
Lobby must be clean (wallet 0, no carried mail, panel gone). Close-up checks: the player spawns on the pad; mail arrives; the Intake
prompt and manifest show; mail is carried; the mailbox prompt shows; delivery and misdelivery events
reach the client; server wallet = start + paid − penalties = client wallet = the HUD's wallet line;
no container desync or bad packet; the world, player view and camera exist; the boot view (camera
at the eyes, not in a wall, clear view along the map's spawn facing); the camera at the player's eye
position and aimed along the interact ray every frame; no local body; the ✓ and ✗ marks drawn; no
missing glyphs; the top HUD row fully on screen; the real-time loop advances the server. At Results
the profile must hold the run's XP, run count, deliveries and recent-run line, the panel must show
the rank line, and the saved profile must read back equal; `tools/smoke.sh` then also looks for the
XP in the profile file itself. Back in the Lobby, the inventory screen: the server puts a log,
fibers and stones into the host's inventory and the chest in the Post Office yard; the host walks
to the chest, looks at it ("Open Chest  [E]"), taps interact, and the smoke then feeds the
panel's grids the mouse events of a hand — drag the 1×2 log over the hotbar (red outline), R (green),
drop; Ctrl+drag half the fibers into the chest; drop stones onto logs (refused locally, nothing
sent, the hint says "No room there"); click the stones across; press both sort buttons — and after
each gesture the screen must show the result in the same frame (pending), the confirmed mirror must
not have moved yet, and a moment later server, mirror and screen must agree with no desync; a
second tap ("Close Chest  [E]") closes it. Then the
fight, through the client's own input: the server puts a raider beside the host, the smoke aims and
holds attack until it falls (three swings; the enemy drawn, the hammer in the hands, the weapons
line, the feed line), its loot must be drawn, taken, on the cards under "Raider" and in the wallet
line; then a giant down the street and one Small Package: one cannon shot, the package gone, a blast
drawn and the giant hurt; the giant's blow must show on the health number and bar; felled, its
stones are on the cards; nothing is left behind and the camera never left the eyes. Then the
death bag: the server fells the host with the loot in their pockets; down, the pockets must be
empty, one bag (the host's own) must lie where they fell on the server, in the client's list and
drawn, the tracker must count it down and there must be no prompt; back on their feet the host
walks to it, looks at it ("Open your bag  [E]   ·   gone in 00:…"), taps interact, and the panel
titled "Your bag" must hold everything they carried; stack by stack it goes back, and the emptied
bag must be gone everywhere with the same items in the pockets as before and no desync. Last, ENet: a second `ClientRuntime` connects to
the host's own UDP port, joins (both ends, the host's HUD and the host's player views count two
players), walks a second down the street (the server moves it, its prediction agrees), and leaves;
next to it a client saying Hello with a wrong protocol hash must get `ProtocolMismatch` and be
disconnected. That guest joins with every playtest flag and `--shift=3`: it must stand at its
mailbox, and the server must have the clock stopped, the quota waived and the next run at shift 3
until it leaves.

The second run, `--smoke --shift=2 --no-quota --raid` (about 15 s), is nit 13's playtest as a
player would do it: the flags must be on the host's player, go starts Prep of "Shift 2 / 5" with
shift 2's quota, Ready starts it; the raid banner and the tracker's countdown must show from the
first second and the raid must be there 5 s in; the host walks out to the street and fights with
the hammer, through the client's input, until all six pulses are in and nobody of them is left on
the server, in the client's list or drawn (every raider felled by the host, the tracker counting
them, the feed saying so); it walks over the loot (coins in the wallet line, cards shown); the
rest of the shift runs out, Payday's panel says QUOTA MISSED and the hint "playtest: the run goes
on · shift 3 in …", and the run must then be in shift 3's Prep with no Results and no raid left.
