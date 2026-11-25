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
      in a row are lost (each packet repeats the last three inputs). *Since the weight carried
      slows a carrier* (re-measured 2026-11-24): without loss the worst correction is 1.0–2.5 cm,
      each where the speed multiplier changed (0.96 → 0.95) and the client learnt it a round trip
      after the server moved by it; the no-loss bound is now 5 cm, RMS still under 0.01 cm. Known limits: the conditioner
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

- [x] Nit 14 — the shift's line lay on the white clock bar and a name tag lay on both: the top
      group has room now (quota bar, 4 px, the line in a 36 px row, 4 px, clock bar; the hint
      under it) and stands on a dark plate of its own, and no world label is drawn where it would
      lie on the plate, the hint or the banner (`Hud.KeepClear`, `LabelDeclutterer`). The smoke
      checks that the four rows do not touch, that they are on the plate, that the line fits it,
      and every frame that no drawn label lies on the HUD (design choice 62)
- [x] Nit 15 — far address labels piled up on each other and on the crosshair: a mailbox's
      address thins out from 9 m and is gone at 15 m, a house number from 34 m to 46 m (INTAKE
      30–42 m; POST OFFICE and name tags are read from anywhere), and where two labels would
      still cover each other only one is drawn: the higher rank (name tag, then address and
      sign, then house number), then the nearer. Fades take a fifth of a second.
      `LabelDeclutter` is Godot-free (`LabelDeclutterTests`, 7); the smoke checks every frame that
      no label is drawn past its distance (design choice 62)
- [x] Nit 16 — objects leaked at exit (seen on both smoke passes here, in most runs: one `Image`
      and one `DummyTexture`, at times ENet peers too). Cause: a C# `Variant` that carries an
      object holds a reference of its own until the garbage collector finalises it, and what is
      still held that way when the game quits is let go after the renderer has shut down. Two
      places did that: the ground map texture handed to the ground shader (`Shaders.Ground`), and
      the event arrays of ENet's `Service` (every event kept its peer). Both are now disposed
      where they are used (`using`), as are the ground material's C# handle and the ENet host and
      peer on `Close`. `tools/smoke.sh` now fails on any "leaked at exit" line; four runs in a row
      were clean.
- [x] Nit 17 — the raid camped the spawn pad: every raider walked at the nearest carrier from
      anywhere on the map, so the whole wave ended up on the pad and whoever stood up there was
      down again. Five rules now (design choice 63; all numbers in `balance.json`):
      - a raider goes for the nearest carrier within its `aggroRange` (15 m, doc 05 §2) and
        lets go of one who got 1.5× that far from where it last had them in range;
      - a blow brings the one who was hit over from any distance for 6 s (no free cannon shots
        at the line);
      - with nobody to go for, the raiders walk to **their line** and hold it: across the
        street 19 m from the pad toward the gate (x ≈ 31.7 on Test Street, four lanes, six
        ranks), facing the Post Office. Pad and Intake are farther from it than any aggro
        range, so a carrier can stand up, fetch a package at the Intake and shoot from the
        yard; the street to the mailboxes is what the raid holds;
      - back on their feet a carrier is **protected** for 5 s — not hurt, not gone for — until
        they strike a blow of their own (hammer or cannon). The HUD banner says "PROTECTED · 4
        s, or until you strike" in green. A shift boundary's respawn gives none (what ends it
        and how long it holds in the yard changed in 2c (d));
      - at most four raiders close in on one carrier; the others wait 4 m off until there is
        room, so the mob no longer covers the camera.
      `CombatTests` (9 new, among them the playtest's raid with two carriers: on the pad both
      are left alone; the one who walks into the line and goes down stands up on a pad no
      raider is within aggro range of, and opens the Intake unhurt), `CombatHudTests`, `CodecTests`;
      the smoke's fallen host is checked for the banner, and the raid smoke's host now goes at
      the raiders on their line. Protocol `m1.7` (snapshot player flag `protected`,
      `PlayerRespawned.protectedUntil`)
- [x] Nit 18 — `--playtest-package`: that player has four Small Packages (what the mail cannon
      fires, RMB) when a shift's Delivery starts, when they join one under way and whenever they
      are back on their feet — topped up to four, never more. Host and `--join` guest alike (a
      fifth bit in the Hello's playtest byte), off by default, listed under "Commands". It goes
      together with `--raid` and with `--playtest-mailbox` (letters for the mailbox and packages
      for the cannon side by side). The raid smoke is now run with it: the host fires all four
      at the raid before the hammer comes out (`PlaytestTests` 4 new, `PlaytestFlowTests` — a
      guest fires through its own input —, `CodecTests`; protocol `m1.8`; design choice 64)

### 2c. Playtest issues of the a5d987e report (`mailgame-harness/results/20261117-235926-a5d987e/report.md`, 2026-11-18)

The report tested a5d987e, before nits 14–18. Each issue was looked at again on e7577cc; what
was still there is fixed, what was already gone is pinned by a check that would catch it.

- [x] (a) The Guest's nameplate over the HUD's top bar — **gone on e7577cc** (nit 14: no world
      label is drawn on the top plate, the hint or the banner), but gone altogether: with the
      guest right before the host, as in the report's picture, there was no name at all. Now a
      carrier's name that would stand on the HUD is **drawn under it** instead (at most 240 px
      lower; past that, with its owner in your face, it is left out), and takes its room there.
      `LabelDeclutterTests` (3 new); the smoke puts the returned guest 1 m before the host and
      checks that the name would stand on the top lines, is drawn in full, lower, clear of the
      HUD (`nametag.png` with `--shots`; design choice 67)
- [x] (b) Distant address labels stack — **gone on e7577cc** (nit 15: addresses thin out from
      9 m and are gone at 15 m, house numbers from 34 m to 46 m, and of two that would cover
      each other one is drawn). Checked on the boot view (`boot.png`: "1 Larch Lane", "2",
      "4", nothing else) and with a guest in the Lobby. The smoke now also checks every frame
      that no two drawn labels lie on each other, and that it has seen a street's worth of them
- [x] (c) Cannon shots never hit raiders — two causes. The one of the report ("every package
      went into the death bag at the first down") went with nit 18. The other was still there:
      a package only went off when it touched a raider (0.75 m to either side of its line) and
      otherwise flew on to the end of its range, 30 m off. Now it has a **fuse**: it goes off at
      the point where it is nearest to a raider if that is within `cannonFuseRadius` (1.2 m) of
      the raider's body, and the blast (2 m) has them. Aim is what it was: where the camera
      looks (checked: the shot's yaw is the view's). `CombatTests` (6 new cases: aimed 1.2 m beside a
      raider it hits, 3.5 m beside it flies on; from the pad four packages aimed 2–3° beside the
      raid on its line all hit); the raid smoke counts the blasts that hit and fails under
      three of four (4 of 4 here) (design choice 68)
- [x] (d) The death-bag re-down loop at spawn — still there in two ways: the 5 s protection
      ended with the first swing at the air (and who stands up in a fight is hammering the
      button), and once it had run out a fight that had come to the pad went on on top of
      them. Now **only a blow that hurts a raider ends the protection**, and **in the yard**
      (`respawnYardMetres`, 8 m around the pad: pad, Intake, chest — and the bag of who fell
      there) it holds past the 5 s for as long as raiders are about and they stay in it. The
      HUD says which: "PROTECTED · 4 s, or until you hit a raider", then "PROTECTED in the yard
      · until you leave it or hit a raider". `CombatTests` (6 new or rewritten, among them the
      playtest's case: eight raiders on the pad, a second carrier fighting them; who falls
      there stands up, is not turned on for eight seconds, walks to the bag and takes every
      item back), `CombatHudTests` (1 new) (design choice 69)
- [x] (e) Shift 3 sends 32 raiders and cannot be won — measured on e7577cc with two carriers:
      20–24 raiders in shift 2, 34–35 in shift 3, 45–50 in shift 4, and a team that plays it
      plainly was down 20–50 times in shifts 4 and 5. `waves.json` `baseBudget` is now
      120 / 150 / 185 / 225 for shifts 2–5 (was 120 / 220 / 360 / 560): a quarter more from
      shift to shift instead of nearly twice. Two carriers now meet 24–27 raiders in shift 3,
      31–34 in shift 4, 33–44 in shift 5. `RaidFightTests` (9 cases) plays whole raids with
      clumsy bots (`RaidBot`: straight at the nearest raider, 25° off, a blow every 1.2 s):
      the budget grows by 15–34 % a shift; shift 3 sends at most half again as many as shift 2;
      one, two and four carriers win the raid of every shift within four minutes and are down
      at most eight times a head; two carriers in shift 3 are down four times at most between
      them, never within 8 s of standing up (design choice 70)

### 2d. Playtest issues of the 25fbca5 report (`mailgame-harness/results/20261119-082044-25fbca5/report.md`, 2026-11-19)

The report tested 25fbca5, one commit before the harvesting UI. Each issue was looked at again
on b44a177; what was still there is fixed, what was already gone is pinned by a check.

- [x] (a) Payday: the counter is a walk away inside the 20 s window, and the host stood facing
      the Intake — **still there**, and worse than reported: a Payday finds the carriers out on
      their rounds, up to 70 m from the Post Office, and nobody walks that in 20 s. Now **at
      Payday the counter serves the team where it stands**: the server asks for no reach in
      that phase (buying and selling), the new shop key **C** puts the cards up anywhere, and
      the Payday hint names it ("Quota met — shift 2 in 00:17 · [C] shop"). In Prep the same
      key opens the counter from within its reach without any aim. `paydaySeconds` stays 20 (doc
      11). `ShopTests` (1 new, 1 widened: host and guest buy and sell from across the map at
      Payday, `OutOfRange` again in the next Prep), `ShopFlowTests` (1 new: the key in Prep,
      Delivery and Payday, bought and sold 10 m and more from the counter, interact or the key
      takes the cards down); the smoke puts the host at the mailbox farthest from the counter
      (70 m) at the first Payday, reads the hint, presses the key, buys the cheapest card with
      its number, sells it back and checks the wallet on both ends and that the summary is back
      (`payday-shop.png` with `--shots`; design choice 73)
- [x] (b) The shop prompt wants about 1 m and an exact aim at the counter's body — **still
      there**: the reach was the mailboxes' 2.5 m measured from the wall behind the counter,
      and the box to aim at was the counter's body, 1.8 m wide. Now the counter has **a reach
      of its own** (`shopRange` 4 m, the server's `serverShopRange` 4.5 m: every seat of the
      spawn pad is inside it), **a box to aim at 2.6 m wide and 2.5 m high** (counter, window
      and sign), and where the crosshair is on nothing at all **facing a counter is enough**:
      within 40° to either side of it, whatever the pitch, the prompt is the Intake's or the
      shop's, whichever is faced more squarely. What the crosshair is on still comes first.
      `ShopFlowTests` (1 new: from each of the four seats, turned to the counter, at eye level,
      looking up at the sign and down at the ground, 34° off to either side — the prompt; with
      the back to it — none; the report's seat facing the Intake gets the Intake, turned to the
      counter the shop and E opens it; a shut counter only answers to the crosshair)
      (design choice 74)
- [x] (c) The mouse is not recaptured after the Draft or after closing the shop — **still
      there** in two ways: whether the mouse was the view's hung on three flags spread over
      the input node (a pause wish, a "UI open" setter, the last mode set), written once on the
      change and never looked at again, so a grab lost to the window manager or to an Esc with
      nothing open stayed lost without a word on screen; and the Draft's cards could only be
      taken by key, so a carrier who went for the pointer in the Draft pressed Esc for it and
      came out of the Draft with a free mouse. Now one rule, `MouseCapture` (Godot-free): the
      mouse is the view's unless a panel is open or Esc let it go; **closing a panel always
      gives it back**, whatever was pressed before; the mode is set anew every frame and
      grabbed again when the window gets the focus back. **The Draft is a panel**: while a card
      is still to be taken the pointer is free and a click on a card takes it (the keys still
      do), Esc sets the cards aside for a look around, and with the pick the view is the
      mouse's again. With the mouse let go outside a panel the HUD says so ("Mouse let go · a
      click or [Esc] takes the view back"), and that click is not a swing. `MouseCaptureTests`
      (4), `DraftFlowTests` (`ChoosingCard`); the smoke checks the rule after every panel it
      opens and shuts (shop by E, by C, at Payday, the Draft by key in the full run and by a
      click on the card in the raid run) and the HUD's note. Checked in a window with real X
      input (Xvfb, xdotool): the view turns with the mouse in Prep, not with the shop open,
      again right after C closes it, a real click on a Draft card takes it and the view turns
      in the next Prep, Esc frees the pointer and a click takes it back (design choice 75)
- [x] (d) The Guest's nameplate over a corner of the Draft panel — **still there**: the world's
      labels kept clear of the HUD's plates, prompt and hint, but not of its panels. The
      Draft's cards, the Payday / Results panel and the inventory screen (with chest and shop)
      are now in `Hud.KeepClear()`: a name over a panel slides under its edge if that is near
      (240 px), else it is left out; signs and house numbers behind a panel are not drawn.
      `LabelDeclutterTests` (1 new: a name over a corner, over the middle, beside the panel);
      in every Draft the smoke turns the host to a label of the world so that it lies under
      the cards and checks that none is drawn on them (design choice 76)
- [x] (e) Harvesting has no client UI — **gone on b44a177** (the report tested the commit
      before): prompt with yield and hits left, the hold bar, the swing, the feed's count, bare
      and full nodes, berries and ore — "The nodes on screen" in section 3. Pinned by
      `HarvestFlowTests` (4) and the smoke's harvest steps; looked at again on the current tree
      in a window.

### 2e. A read-through of Build mode and Fortify on c4d1d4d (2026-11-20)

No playtest report on c4d1d4d; the unit tests and `tools/smoke.sh` were green on it. A read of
the building code against the server's rules found no break in the codec, the mirror, the
prediction, the spikes, the blueprint or the refunds, and three smaller faults, fixed:

- [x] A **Mega raider's body** is wider than its kind's (×1.4, to the props' 0.5 m limit) on
      the server; the client's ghost tested the kind's radius, so beside a Mega it could be
      green and the server answered "Occupied". Both now use `WaveDef.BodyRadius`.
      `BuildFlowTests.AMegaRaidersBodyKeepsTheGhostRed_AsTheServerSeesIt`
- [x] The right button **held on a shut chest with something in it** asked to take it down
      twice a second, each refused with a feed line: the client cannot see into a shut chest.
      A refused take-down is not asked again until the button is let go or the crosshair moves.
      `BuildFlowTests.TakingDownAShutChestWithSomethingInIt_IsAskedOnce_UntilTheButtonIsLetGo`
- [x] **On a gamepad** Build mode could take down (left trigger held) but not build or turn:
      the right trigger now builds as the left mouse button does, the right shoulder turns.

One smoke run on the way timed out in its ENet walk (step 1 has 10 s of wall clock for ~2 s of
walking) while the shared machine was busy; the run after it passed. Left as it is.

### 2f. A read-through of Sand, the Shovel, tools in the hand, the Gate and the Alarm Post on c59cf39 (2026-11-20)

No playtest report on c59cf39; the unit tests and `tools/smoke.sh` were green on it. A read of
the three commits against the server's rules (the gate in movement, prediction and the bot's
paths, the warning moved by posts and frozen once announced, the held-tool byte, the sand
piles' regrowth, what a late joiner is told) found no break, and one wrong line, fixed:

- [x] With the playtest's `--raid` (the raid called at once, 5 s of warning) an Alarm Post in
      range was still credited in the raid's feed line ("… 5 s of warning with the Alarm
      Post"). The posts are named only when the warning is longer than the team's own without
      them. `CombatHudTests.TheRaidsLine_NamesTheAlarmPost_OnlyWhenItRangTheRaidInEarly`

Trees on the beach's corner are not grown there at all (scenery only grows on grass); the
others keep their places, as the commit said. Left as it is.

### 2g. A check of the bike, the generated town and the district unlocks on 189065e (2026-11-20)

No playtest report on 189065e; the unit tests (338 + 154) and `tools/smoke.sh` were green on it.
The bike and the raid had only been tried on the test street, so both were tried on the seed's
town; no break found, the two checks stay:

- [x] `VehicleTests.OnTheGeneratedTown_TheBikeStandsByTheCounter_AndRunsTheMainStreetAt8`
      (seeds 1, 4, 9: bought at the town's counter, parked on an open tile, 8 m/s down the main
      street from its west end)
- [x] `RaidFightTests.OnTheGeneratedTown_TheRaidersFindTheirWayToThePostOffice` (seeds 2, 13:
      in from the main street's east end, down to their line before the Post Office)

### 2h. Playtest issues of the c59d497 report (`mailgame-harness/results/20261121-064749-c59d497/report.md`, 2026-11-21)

Each one checked on 7e68047 first; all four were still there.

- [x] (a) A guest's name tag showed through the map (its back is 92 % opaque, the big name in
      the view behind it). While the map is up its panel is one of the places world labels keep
      off (`MapPanel.PanelArea`, `Hud.KeepClear`); a name tag cannot slide 400 px down, so it is
      not drawn. `LabelDeclutterTests.ANameOnTheOpenMapIsNotDrawn_ItCannotSlideFarEnough`; the
      smoke's map step checks that no world label under the panel is drawn ("18 world labels
      under it kept off").
- [x] (b) `--playtest-mailbox`'s park at the shift's start took a rider off the bike (it wrote a
      new move state with no ride) and left the bike at the Post Office. A rider is now parked
      in the saddle: the bike comes along, its cargo with it, standing, facing the box, and the
      spot is worked out for the bike's radius. `PlaytestTests.AtMailbox_ARiderIsParkedInTheSaddle_TheBikeComesAlong`.
      The "wedged" part was checked on the island: every seat's spot on 12 seeds is a street
      tile nothing pushes a carrier or a bike out of
      (`AtMailbox_OnTheIsland_EverySeatStandsFree_OnFootAndOnABike`); being stuck came from
      getting off the bike there, which no longer happens.
- [x] (c) The Fortify Stone Wall had no smoke check. The smoke's Fortify visit now also builds
      one in Build mode from four Stone: its key, a green ghost and "Build Stone Wall", built on
      both ends, blocking, 800 HP, four Stone gone, drawn (`fortify-stone-ghost`,
      `fortify-stone-wall` shots); `tools/smoke.sh` greps for it.
- [x] (d) Bandage and Berry healing, never seen live: a smoke stage of its own
      (`SmokeDriver.Heal.cs`) in the first Prep after the shop. Hurt by 60 with a Bandage in the
      hand, the use key gives 50 HP at once; with Berries, part of their 25 HP halfway through
      their 5 s and all of it at the end; the HUD's number and bar follow the server each time;
      at full health the use key uses nothing. The carrier's own mending is held off while it
      measures. Nothing was broken; the check stays (`heal-bandage`, `heal-berries` shots).
- [x] `--playtest-wallet=N` (the report's issue 3): runs start with $N in the team's wallet ("5"
      or "4.50"; capped at $1000). Carried in the Hello like `--shift` (protocol `m1.17`), the
      most any player in the game asked for counts; asked for in a run under way, the wallet is
      topped up to it at once. Topped up, never added to, and not income: the quota, the score
      and the run's totals do not see it (design choice 88). `PlaytestTests.Wallet_*`,
      `PlaytestFlowTests.AGuestWithAWallet_*`, `CodecTests`.

The report's issue 1 (input lag at 8–13 FPS eating the 20 s Payday window) is the box's software
rendering; issue 6 (the island's north hedge with trees behind it) is the island's edge as
generated. Both left as they are.

### 2i. A check of spawn edges, settlement placement and Generating on 6290846 (2026-11-21)

No playtest report on 6290846. `tools/smoke.sh` and the unit tests were green; the SeedViewer ran
over seeds 1–220 with every town valid.

- [x] The settlement search said "within 80 m of the middle" but tried every site of a ±40-tile
      *square*: seeds 21–24, 219 and others put the town 88–112 m off the middle. A site now
      counts only within 40 tiles all round (`IslandTerrain.PlaceTown`); over seeds 1–220 none is
      further than 80 m. `IslandTerrainTests.TheTownsSite_*` checks it on 30 seeds (four of them
      failed before). World hashes of those seeds changed; nothing pins them.
- [x] Generating: the client's own "my world is drawn" was a plain Ready, and the server took it
      in Prep too. A slow client whose word came just after the ten seconds (or a joiner's, after
      the last other client's) was then Ready for Prep without pressing anything, and the others'
      Ready ended Prep under them. It is now `SimRequest.WorldDrawn` (a SetReady with count 2),
      refused outside Generating. `GeneratingTests.AWorldDrawnThatArrivesInPrep_IsNoReadyForPrep`.
      The spawn edges were read through for the same; nothing found.

### 2j. A check of belts, the Intake outfeed, splitters and mergers on 789d49f (2026-11-22)

No playtest report on 789d49f. The unit tests were green; `tools/smoke.sh` was red twice in a row.

- [x] The smoke's fight failed "the hammer never showed in the player's hands" although the raider
      fell: the hammer comes up in real time (a tenth of a second to half way), the smoke runs
      the fight at three ticks a frame, and headless frames quick enough let the raider fall
      first. After the fall the smoke now gives it half a second of real time before it checks.
- [x] Belts: whenever the network was compiled again (any belt, splitter or merger built, turned
      or taken down anywhere), mail put on a head less than a length ago — its rear still behind
      the head, e.g. the Intake's outfeed's newest letter every quarter second — counted as not
      fitting and went back into the Intake. Only mail whose *front* is pushed off the head goes
      now. `BeltTests.MailJustPutOn_StaysOnTheBelt_WhenABeltIsBuiltElsewhere` (failed before);
      `BeltFuzzTests` builds random networks of belts, splitters and mergers round a mailbox,
      rebuilds them under the mail for 3000 ticks on 8 seeds, and checks that no mail is made,
      lost or doubled, every lane keeps its order and spacing, and a client's mirror moved by
      the hand-offs alone has the same mail lane by lane on every segment (it found the bug).
- The island bot's 2 ms tick budget missed once at p99 2.16 ms (mean 0.12 ms) under the box's
  load; three runs on their own passed.

### 2k. Playtest issues of the df77576 report (`mailgame-harness/results/20261122-132422-df77576/report.md`, 2026-11-22)

Checked on 1ac7271; all three were still there.

- [x] `--raid` with `--playtest-mailbox`: the raid holds its line 19 m from the Post Office
      (`raidStandoffMetres`), on the test street a few metres past the mailboxes of seats 0 and
      1, so its 16–21 raiders found the parked carriers within 10 s and downed them (a repro
      without input: both down 26 s in). A carrier parked in a shift with a raid (to come or
      under way, also by a Hello in the middle of one) is now let be at the mailbox — not hurt,
      not gone for — until they walk 4 m off the spot (`PlaytestSpot.GuardRadius`) or hurt a
      raider (`Player.ProtectedAtPark`, as the yard keeps who stood up in it); the banner says
      "PROTECTED at the mailbox · until you walk off or hit a raider" (design choice 100).
      `PlaytestTests.AtMailboxWithRaidNow_*` (two carriers through all six pulses, then a blow
      and a walk off), `AtMailboxWithoutARaid_NoGuard`, `AtMailbox_AskedForInARaidUnderWay_*`,
      `CombatHudTests.ParkedAtTheMailboxWithARaid_*`
- [x] C late in Payday: the shop's cards came down under the Draft's and the next number key
      took a card. The number keys are the shop's first while its cards are up, and for 1.5 s
      after the Draft took them down the Draft's keys take no card (a click does;
      `ClientRuntime.DraftKeysHeld`, design choice 101). `DraftFlowTests.AShopKeyPressedAsTheDraftTakesOver_TakesNoCard`,
      `WithoutTheShopUp_TheDraftsKeysAreLiveAtOnce`
- [x] "Nothing back" for a belt in Delivery: the 50 % of doc 04 §1.4 was rounded down input by
      input, so anything of one Plank and one Iron Ingot gave nothing. It is now half of the
      materials as a whole, rounded down, the dearer first (`BuildRules.RefundCounts`; design
      choice 102): a belt gives its Iron Ingot back, a splitter still one Ingot, Prep still all.
      `BuildTests.TheRefundIsHalfOfTheMaterialsAsAWhole_*`, `ABeltTakenDownInDelivery_*`
- Belt lines from the outfeed into a mailbox backing up on mixed Intake mail is choice 94 as
  meant; the Address Sorter (below) is what sorts it.

### 2l. A check of belts, the Address Sorter, the Inserter and the Depot on 387171e (2026-11-23)

No playtest on 387171e. Build, Sim.Tests (443), Net.Tests (181) and both smokes were green; nothing
was found broken.

- [x] The belt fuzz did not have the Depot in its mix: `BeltFuzzTests` now builds depots too (with
      a container id, as the server does), its sink stores every third piece that reaches one, and
      the count of mail made must equal what rides, waits in a sorter, was delivered, **was stored**
      or went back to the Intake; the client's mirror takes stored mail off as the server's
      `BeltItemRemoved` would. The fuzz area grew from 8 × 8 to 10 × 10 tiles (2 × 2 depots and
      sorters crowded the lines out), and `RandomNetworks_FeedDepots` checks over four seeds that
      depots really were fed. All invariants held on every seed

### 2m. A check of pipes, the Mail Truck, the Long Inserter, belts, the sorter, the inserter and the Depot on 136f335 (2026-11-24)

No playtest on 136f335. Build, Sim.Tests (459), Net.Tests (183) and both smokes were green on it;
nothing was found broken in those systems, so nothing needed fixing before the next features.

### 2n. A check of the Vehicle Depot on 55889d6, before NPC drivers (2026-11-24)

No playtest. Build, Sim.Tests (469), Net.Tests (185) and both smokes were green; the island bot
failed once on its 2 ms tick budget (p99 2.02 ms, one 6.2 ms tick with a GC pause while the box
was loaded) and passed twice when run again: load on the shared machine, not a regression.
Nothing in pipes, the truck, the inserters, belts, the sorter or the depots needed fixing.
Later in the batch (on the sea path) one `tools/smoke.sh` hung in the first smoke after the hot
reload and one island bot run timed out right after joining, both while other sessions loaded the
box; each passed alone at once and the whole script passed again after. Watch for it.

### 2o. A check of the sea path, the depots, drivers, the truck, pipes, inserters and belts on 346eb3e (2026-11-24)

No playtest on 346eb3e. Build, Sim.Tests (479), Net.Tests (186) and `tools/smoke.sh` were green.
A read of the sea path found two ways to be stuck in the basin, both fixed (design choice 113):

- [x] A carrier who went down in a boat on open water left their bag on the water, where no one
      can walk, and their body thrashing in it until the respawn. They and the bag now wash up on
      the nearest shore or deck. `BoatTests.GoneDownInABoatOnOpenWater_*`
- [x] A Motor Boat run dry set its skipper down into the sea, stuck. It now drifts to a stop with
      them aboard (`Becalmed`, protocol `m2.9`), and an Oil Can from the seat fills it.
      `PortTests.AMotorBoatRunDryOnTheWater_*`, `CodecTests` (the move flag)

Nothing in the Vehicle Depot, drivers, the truck, pipes, inserters or belts needed fixing.
Later in the batch the rendered smoke (`--shots` at 1280 × 720) found the shop's cards running off
the screen once Blueprint: Oil made 21 (choice 118); after the fix it passed whole (SMOKE OK), with
shots of the dock, the port's console, the pump, the ping wheel and the pings. One rendered run
before that had the ENet guest time out joining while the box was loaded; the next passed.

### 2p. The debug world, and the 346eb3e playtest's Route Console and truck view (2026-11-25)

- [x] **`--debug-world`** (`content/maps/debug_world.json`; design choice 119). A bare 60 × 40 map
      (no scenery, no nodes; `"bare": true`) with the sea along its south edge, whose `debug` block
      (`WorldMap.Debug`, `DebugWorldDef`) puts every construct there is up and running as each run
      starts (`SimWorld.BuildShowcase`, at the end of `SetUpRun`; the host's `--debug-world` is a
      quick start, so the run is in Delivery of shift 5 from the first tick): the Intake's outfeed into
      a belt line up a Belt Ramp, over two Elevated Belts, down a ramp and along three Fast Belts into a
      Splitter whose three ways meet again in a Merger; an Address Sorter (front: Anvil Close by a
      belt into its mailbox; left: Cargo, by a belt into the Small Port's loading face; right: Beacon
      Road, by a belt into the Vehicle Depot's loading face; overflow: a belt into a Pipe Inlet);
      pipes (straight, buried under Beacon Road, two junctions) to three outlets — one before Fern
      Hollow's mailbox, one into a Chest that an Inserter empties into Cobble Hill's, one into a Depot
      that a Long Inserter empties into Ropewalk's; the Vehicle Depot with a Mail Truck in its bay and
      a hired driver (stops: Beacon Road's three houses); the Small Port with a Motor Boat and a hired
      captain (stop: the Business Dock "1 Quayside"); and an Oil Pump on a seep beside the depot's bay
      that fills the truck (which starts with 200 s of fuel). Instead of the spawner's batches the
      Intake gets one piece a second, the addresses in turn, each in turn of the kinds it takes
      (`MailSpawner.SpawnOne`). Toggles, each in the file: **god mode** (`DamagePlayer` does nothing),
      **unlimited** (the wallet topped up to $1000 every tick, every blueprint granted at the start,
      building takes no materials — on the server and in the client's `RecipeState`/`LineStates`),
      **the clock stopped** (`ClockStopped`, shown "(stopped)"), the quota waived, and **no raids**
      unless a player asks with `--raid`. `ShowcaseProbe` (`SimWorld.Showcase`) watches each of the
      ten **stations**' proofs from the sim's own events: mail onto the belt over a tile, a machine's
      delivery (to an address), a can pumped. All ten come true in 45 s of sim time; over five
      minutes nothing jams and nothing is misdelivered. *In the client* (`DebugWorldController`): F1–F5
      and F7–F11 put a **free camera** at the stations' bookmarks (F6 stays the hot reload), F12 gives
      the view back, the backquote key frees the camera where it is (WASD flies it the way it looks,
      Space and Ctrl lift and lower, Shift is faster, the mouse turns it, the carrier stands still:
      `PlayerInputController.Suspended`), and comma / period / slash set the **time scale** to 0.25×,
      1× and 4× (`GameSession.TimeScale`, both ends of a host); three amber lines at the top left say
      what is on. `--debug-world --shots=DIR` (under xvfb) waits for the flow at 4× and saves
      `debug-01-intake-and-belt-line.png` … `debug-10-long-inserter-and-depot.png`, one per bookmark,
      then "SHOTS OK". `DebugWorldTests` (6), `DebugWorldFlowTests` (a client mirrors the showcase,
      belts item for item, no desync, every recipe open to empty pockets); **`tools/smoke.sh` runs
      `--smoke --debug-world` third** (`DebugWorldDriver`): Delivery of shift 5, the clock stopped and
      the wallet full on the HUD, every station's proofs (fast-forwarded), the client's constructs,
      vehicles and crews equal to the server's with mail drawn on the belts and the pump nodding, no
      damage, a Stone Wall out of empty pockets, the Route Console taps below, each bookmark key (the
      camera there, looking at its station, the station in view, its name on the line), the free
      camera flown 8 m while the carrier stood, and the three time scales measured (0.26 / 0.99 /
      4.0 in the last run)
- [x] **The Route Console missed a quick tap of Esc, E or Tab** (playtest 346eb3e issue 1). While a
      filter panel or console was up, `ClientRuntime.PressFilterKey` took *every* key (a key that is
      no number came to it as a "digit" far out of range), so the key branch of
      `PlayerInputController` swallowed Esc, Tab and E before their actions were looked at; only E
      held a quarter second got through, by polling. Now only the number keys are the panel's (both
      ends: the controller makes any other key −1, `PressFilterKey` refuses one out of 0–11). The
      debug-world smoke opens the depot's console and taps each key for less than a frame through the
      real controller; without the fix all three taps leave it up
- [x] **The Mail Truck's body blocked the driving view** (issue 2). The eyes sat in the middle of
      the truck at 1.6 m — inside the cargo box. At the wheel of a long land vehicle they are now in
      the cab, 1.15 m ahead of the middle and 1.8 m up (`FirstPersonCamera.EyePosition(feet, world)`,
      `CabEyeForward`, `CabEyeHeight`; design choice 120); the smoke checks it as the host gets in.
      The interact ray stays at the body's eyes
- Issues 3 and 4 of that report (a driver in Waiting cannot be taken over; "Something stands
  there" on one's own tile) are as designed; nothing in the Map pings, the Oil Pump, the sea path,
  the Vehicle Depot, drivers, the truck, pipes, inserters or belts needed fixing beyond these.

### 3. Further features from `design/drive/`, in milestone order

The order is doc 12's M1 "Systems" list (the next milestone after M0, whose hardening list is
above). A box is ticked when the whole line of the doc exists; the sub-lines say which parts do.

- [ ] World generation pipeline (all stages) for Small Island; worldHash; client-side
      regeneration; SeedViewer tool
      - [x] `worldHash` checked at connect (over the static map and the content, choice 3)
      - [x] **The Small Island town from a seed** (doc 02 §2–§3.7, §3.11–§3.12; design choice
            83). `WorldGenerator` (map id `small_island`, `--world=island` on a host) builds the
            seed's town in integer tiles from streams of its own ("towns", "roads",
            "addresses"): 76–88 × 60 tiles, a main street from shore to shore past the Post
            Office (the raiders' way in at its east end), a back street to either side tied to
            it by cross streets every 12–16 tiles (some to one side only), 38–50 houses on 4 × 4
            lots along the frontages with the mailbox at the verge and neighbours in different
            colours, a beach along the south shore with its sand piles, the scenery and the
            resource nodes grown as on the test street (~350 nodes). Street names from
            streets.json, unique per street; numbers from the west end out, odd north, even
            south. **Districts** of 8–12 houses grown from the Post Office out, street segment
            by street segment (up to five, district N unlocks at shift N in the map's data;
            the remainder goes to the last). Validated — every mailbox walked to from the pad,
            unique addresses, 400 open tiles by the Post Office, no empty district — and drawn
            again otherwise (eight tries). The map is not sent: the Welcome carries its id, the
            seed and the world hash, and **the client builds the town itself** and refuses a
            hash that differs. **`tools/SeedViewer`** (`SeedViewer [SEED[-LAST]] [--map]`): a
            line of figures per seed and the town in letters. Off the map only a street that
            leaves it runs on to the island's backdrop. `WorldGeneratorTests` (5: the same
            seed to the tile, 50 seeds valid, districts from the Post Office out, numbering, a
            run played and a letter delivered at the farthest house), `LoopbackTests` (1: two
            clients build the town from the seed, their bots deliver); `tools/smoke.sh` runs
            the SeedViewer over seeds 1–20 and a bot on the smoke seed's town to "BOT OK"
      - [x] **The island: heightmap, coastline and biomes in the sim** (doc 02 §2, §3.1–§3.2,
            §3.9, §3.12; design choice 84). `IslandTerrain`: 300 × 300 tiles of 2 m (the doc's
            600 × 600 m) round the town, heights in whole centimetres from `FixedNoise` (value
            noise on an integer lattice, smoothstep in 16.16 fixed point — no floats, so every
            machine builds the same island), from the seed's own "heightmap" and "biomes"
            streams; a radial falloff with a noisy coast, the town levelled to 1.5 m above the
            sea with the land blending up from it over 26 tiles, and a **bay** cut into the south
            shore right below the town's beach. The coast is smoothed (no land tile with fewer
            than two land neighbours). **Biomes** by the doc's table: deep / shallow water by
            depth (2 m), beach within 3 tiles of the water (in town: the town's own beach rows),
            rocky above 60 % of the highest ground or steeper than 20°, forest on the grassland
            where a noise mask is highest — 30 % of the land —, grassland the rest. Retried with
            a wider radius while less than 30 % of the land can be built on (35°) or there is no
            deep water within 200 m of the pad. The heights and biomes are in the world hash.
            **The town's scenery grows by its biome** (`Scenery.Odds`: woods in the forest,
            boulders — and their ore veins — on rocky ground, bushes and flowers on the
            grassland); the test street keeps its mix to the tree. Validation has **resource
            minimums** (30 trees, 15 boulders, 60 bushes, 20 sand piles). `SeedViewer` prints the
            biome shares and the node counts, `--island` draws the island. *On screen* the
            backdrop of a generated town is the sim's island: its heights and coast, the sea at
            its level, woods on its forest, boulders on its rocky ground, the ground shader's
            beach, forest floor and rock from a biome texture, and the harbour's deck and jetties
            in the bay under the town's beach (the test street keeps its drawn island).
            `IslandTerrainTests` (4)
      - [x] **The routing graph** (doc 02 §3.8 and §4; design choice 85). `RoutingGraph`, built
            from every map's streets on the server and the clients alike: a node where streets
            meet, end, or leave the map (the main street's two ways in from the island), an edge
            along each stretch of street centre line between two, with its length, its surface
            (paved; dirt paths come with towns of their own) and its street. Routes from any
            point to any point: on to the nearest stretch, Dijkstra along the graph, off it to the
            goal. Validation: the streets are one network and every mailbox stands by one.
            `SeedViewer` prints its nodes, edges and metres. `RoutingGraphTests` (4: the test
            street is one edge from edge to edge; 40 towns are one network with every street tile
            on it; routes from the pad to every house run on streets, no shorter than the crow
            flies; the same seed, the same graph)
      - [x] **The map, M** (doc 09 §2.6, §4; its first user). An overlay under the HUD's top
            plate and above the event feed — the clock stays in view, the mouse stays the view's,
            the walk goes on under it: the town from above out of the tile grid (streets, lots in
            their district's colour, houses in their own, the Post Office, the beach), street
            names along the streets, house numbers where there is room, districts not open yet
            greyed and hatched with "Opens in shift N" on their houses, the other carriers as
            dots with names, yourself as an arrow the way you look, a white ring round every house
            you carry mail for, and **the route to the house of the mail in the hand** (the held
            slot's, else the first mail in the hotbar) along the routing graph in amber, with the
            line "To 8 Larch Lane · 64 m by the streets · 00:13 on foot, 00:09 running" (by bike
            when riding). M or Esc puts it down; the pad's D-pad down too (doc 09's View button is
            the go key here). The interact prompt keeps off it while it is up. `MapGuide`,
            `MapPanel`; `MapFlowTests` (2: test street and generated town); the smoke puts it up
            in the first Delivery once the bot carries mail and checks the route, the line and
            the layout (`map.png`)
      - [x] the archetype choice in the Lobby — see the Lobby line below (Small Island is the
            one archetype built; Large Island is M4 in doc 12)
      - [x] **Enemy spawn edges** (doc 02 §3.10, doc 05 §3.1–§3.4; design choice 92).
            `SpawnEdges.Build`, on the server and every client from the map alone: the first
            way in is the east end of the first street, open from shift 1 (on the test street
            the only one, where the raiders always came from); every district that opens later
            adds the way in facing it — on the side of the map (never the beach's) it lies
            towards from the Post Office, at a street that leaves the map near there, else at
            the nearest open tile of that edge the pad can be walked to from —, at least 40 m
            from the pad, shared with a way in already within 30 m. Each has its compass point,
            its street, its unlock shift and its path to the pad over the routing graph. A
            raid takes its ways in from the seed's "spawns" stream among those open: pulses 1–3
            by one, 4–6 by two in turns (doc 05 §3.2); each raider holds its line on its own way
            in's path (raidStandoffMetres from the pad, lanes across it) and flees by the way it
            came. An Alarm Post rings early near any way in open by the shift. Protocol `m1.19`
            (the raid's two ways in in the global state and the raid event). Banner "Raid
            incoming from the east and the north", feed "Raid incoming from the east, along
            Harbour Road, later also the north"; the map (M) marks each open way in with a red
            arrowhead, the raid's bright with "RAID" and its path dashed. `SeedViewer` prints
            "raiders from east@1 north@2 west@4" and draws '@'. `SpawnEdgeTests` (6),
            `SpawnEdgeFlowTests` (1), `CombatHudTests`; the smoke checks the map marks the test
            street's one way in
      - [x] **Settlement placement searched for** (doc 02 §3.3; design choice 84, amended).
            `IslandTerrain` now draws the natural ground first, then tries the town's grid
            every 4 tiles within 40 tiles (80 m) of the island's middle: a site must be ≥ 97 %
            land with land 8 tiles beyond its west, north and east edges (where the streets
            leave and the raiders come in); it scores by flat ground (under 10°) ×2, less the
            mean levelling in cm, less 5 a tile of bay to the sea and 5 a tile from the middle
            ("towns like the coast", "PO near centre"); the best is levelled and the bay cut
            below it. 30 seeds give 25+ different sites. `SeedViewer` prints "town at 148,156
            (flat 96% levelled 455 cm, 98 m from the sea, 106 m off the middle)". The island
            view draws the terrain and the islets round the island's middle, not the town's.
            `IslandTerrainTests` (1 new)
      - [x] **The Generating phase with its progress bar** (doc 01 §1; design choice 93).
            The host's start puts the run into `RunPhase.Generating` (`SimSettings.Generating`,
            on in the game, off in tests that start straight into Prep): the run is set up at
            once (wallet, perks, shop, nodes, constructs, vehicles, kits) and the server waits
            until every carrier here has said their world is built and drawn — their Ready —, or
            `generatingSeconds` (10) are up; then the first shift's Prep. A player may join while
            it lasts (their `--shift` counts). The client sends that Ready itself
            (`ClientRuntime.WorldDrawn`: the screen has drawn the world two frames since it was
            last built; a headless client at once). The loading panel (`GeneratingPanel`):
            "GENERATING", the world and its seed string with houses and districts, a bar that
            fills with the share of carriers ready, a line each ("Bo · building the world…" /
            "Ada (you) · ready"), "The first Prep starts when everyone has the world, in 00:08 at
            the latest"; the phase line "GENERATING", the hint "Building Small Island" / "waiting
            for 1 of 2"; world labels keep off the panel. Protocol `m1.20`. `GeneratingTests`
            (4), `GeneratingFlowTests` (1); in the smoke the host's screen holds back its Ready
            for a few frames and the panel must read as `HudText.GeneratingLines` says, with its
            bar, before Prep (`generating.png`)
      - [x] Checked along the way: the rider's prediction on the generated town keeps with the
            server tick by tick (`VehicleFlowTests.OnTheGeneratedTown_TheRidersPredictionKeepsWithTheServer`)
- [x] Districts and per-shift unlock; address assignment; street names; house model variants and
      colour coding (on the generated town; the test street keeps its one district)
      - [x] on the generated town: districts, address assignment, street names, colour coding
            (the house variants of the test street, neighbours never alike) — see above
      - [x] **The per-shift unlock** (doc 01 §1.2 "district unlock", doc 09 §2.6 "locked ones …
            'Unlocks shift N'"). The spawner has addressed mail only to districts open by the
            shift since M0; on the generated town that now means district N's houses get mail
            from shift N on. On screen: a house whose district is not open yet carries "opens in
            shift N" in grey under its address, until the shift comes; as a Prep begins the feed
            says which district opens ("District 2 opens: 11 more houses on Pelican Drive and
            Mackerel Mews · 20 in all"; in shift 1 "Post Office District: 9 houses on … take
            mail · another district opens every shift"); a one-district map (the test street)
            says nothing of it. `WorldGeneratorTests.MailGoesOnlyToTheDistrictsOpenByTheShift`
            (3), `CombatHudTests` (1)
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
      - [x] **The Draft phase** (doc 01 §1 and §2.5): after every Payday the run survives, but the
            last, three cards of one shared offer lie open for 30 s; see the perk line below
      - [x] the Generating phase (see the world generation line above)
      - [ ] the Raid phase as a phase of its own (the raid runs inside Delivery, choice 50)
- [ ] Shop (blueprints, bike, consumables, refined materials); bike as first vehicle (driver
      prediction path)
      - [x] **The counter in the sim** (doc 01 §2.4 and §3.4, doc 08 §2.10, doc 10 §3; design
            choice 71). `content/shop/shop.json` (validated: unknown item, kind or category, a
            price under the sell-back value, a blueprint that is not once-per-run, kinds that
            cannot be honoured yet), seven entries: Bandages ×3, Planks ×20, Rope ×10, Glass ×10,
            Iron Ingots ×10, Oil Cans ×3 (from shift 2), and the First-Aid Kit that only comes as
            a special. `Buy` takes the price out of the team wallet and puts the stack in the
            buyer's pockets — whole or not at all —, `Sell` pays an item's `sellPrice` per unit,
            both only at the counter (the shop stands 4 m along the Post Office front from the
            Intake) and only in Prep and at Payday. Two specials a shift, rolled when the shift
            begins: 20 % off. A once-per-run entry (blueprints) is the team's until the run ends.
            `Use`: a Bandage gives 50 HP at once, Berries 25 HP over 5 s, refused at full health.
            Protocol `m1.11` (`Buy` / `Sell` / `Use` requests, `ShopOffer`, `ShopBought`,
            `ShopSold`, specials and owned entries in the Welcome). `ShopTests` (19), `CodecTests`
      - [x] **The counter on screen** (doc 09 §2.8). A wooden counter with a green top and its
            wares under a SHOP sign; "Open Shop  [E]" puts the cards up on the right of the
            inventory screen — key, name ×count, price ("SPECIAL · $2.80 (was $3.50)", framed
            amber), what it is, and what stands in the way in red ("Not enough money", "From shift
            2", "The team owns it", "No room in your pockets"), greyed when it cannot
            be bought; "SHOP · team wallet $6.56" over them. A number key or a click buys; a
            click on a stack of your own sells it, Ctrl+click one of it; E or Esc closes, and so
            does walking off, going down, or the shift starting. Outside Prep and Payday the
            counter says "Shop · closed — the counter serves in Prep and at Payday". The stack in
            the hand is named over the hotbar ("2 Bandages · [Q] use"), Q uses it. Feed lines for
            everybody ("On special at the shop: …", "Bo bought Planks ×20 for $1.40", "You sold 1
            Bandage  +$0.07"). The wallet's change reaches every screen with the next snapshot.
            `ShopFlowTests` (4); the smoke goes shopping in the first Prep the wallet allows
            (`shop-counter.png`, `shop.png`)
      - [x] the tools: the Axe ($0.80) and the Pickaxe ($1.00) are on the counter from shift 1
            (nine cards now), never as specials — see harvesting below
      - [x] **The first blueprint: Fortify** (doc 10 §3 bp_defense, doc 04 §1.3, doc 05 §4;
            design choice 79). "Blueprint: Fortify", $3.00 from shift 2, once per run and the
            whole team's: nothing lands in the pockets, the card says "Once per run, for the
            whole team" and then "The team owns it", and the feed "Blueprint: Fortify: the team
            can build Stone Wall and Spike Strip now · [B]". The **Stone Wall** (4 Stone, 800
            HP, a dry-stone wall that joins the wooden ones at a corner) and the **Spike Strip**
            (2 Iron Ingots, 150 HP, a plate of iron spikes: in nobody's way — carriers walk over
            it, the raiders' paths go across it — and 15 HP a second off every raider standing
            on its tile, never off a carrier; content's new `spikes` behaviour with
            `params.damagePerSecond`, validated). Without the blueprint their ghosts say "Needs
            the Blueprint: Fortify from the shop". The tenth card of the shop has the key 0.
            `FortifyTests` (5), `BuildFlowTests` (1 new: Ada buys it with the card's key, Bo
            builds with it and walks over the strip), `ContentValidatorTests` (1 new); the smoke
            buys it in shift 2 and builds a strip (`fortify-*.png`)
      - [x] **The bike, the first vehicle** (doc 04 §3.1, doc 06 §3 "S with driver prediction",
            doc 10 §1.3 and §3; design choice 82)
            - [x] *In the sim.* `content/vehicles` (validated: unknown or hand-carried cargo,
                  seats other than 1, speeds, rates, radius) has the **Bike**: 8 m/s on the road
                  (the street and the Post Office yard), 5 off it, 150 HP, the `bike_cargo` rack
                  of 2x8. The shop sells it (`kind: vehicle`, $1.20 from shift 1, the card after
                  the Shovel; the blueprint's key moves one on, to the **equals key** in a shift
                  with the First-Aid Kit on special): one for each carrier
                  present, parked on the free tile nearest the counter. `Mount` (in reach,
                  nobody on it) puts the carrier into the saddle where the bike stood; from then
                  on the bike is part of the rider's movement state (`PlayerMoveState.Ride`,
                  `RideSpeed`, `HeadX/Z`) and `PlayerMovement.Step` rides it — the front follows
                  the steer at 200°/s, the speed comes up at 5 m/s², hard back brakes at 10, let
                  go it rolls out at 3, a wall takes the speed it cannot make good, the pockets'
                  weight does not slow it —, the same step on the server and in the rider's
                  prediction. The **jump key gets off** (predicted too), and so do going down and
                  losing the connection; the bike stays parked where it was. The rack is the
                  rider's to open, from the saddle anywhere. Bikes are the team's until the run
                  ends. Protocol `m1.15` (the `Mount` request; `VehicleAdded` / `Mounted` /
                  `Parked` / `VehiclesCleared`; the vehicles in the Welcome; the ride, its speed
                  and heading in the own snapshot, the ride and its heading per player).
                  `VehicleTests` (11), `CodecTests`, `ShopTests`, `ContentValidatorTests`
            - [x] *On screen* (doc 09 §2.4 prompts, §3 "vehicles"). A postman's bike: a red
                  diamond frame, black tyres on spoked wheels that turn with the ground covered,
                  a sprung leather saddle, swept bars with a lamp, and a rack over the rear wheel
                  with the canvas mail satchel that is its cargo; parked where it was left, under
                  its rider while ridden (the local rider's where the prediction has them, the
                  others' where their body is drawn, up on the saddle and turned the way the bike
                  points). Beside a parked one, looking at it: "Ride Bike  [E]", a tap gets on.
                  In the saddle with nothing else in view the prompt is "Open Bike cargo  [E]   ·
                  [Space] get off": E opens the rack on the right of the inventory screen, Space
                  gets off. The card says "One for each carrier · parked by the counter", greyed
                  "A Bike for each of you already". Feed lines ("The Bike stands by the counter ·
                  look at it and [E] to ride", "You are on the Bike · [Space] gets off · [E]
                  opens its rack", "Ada got on a Bike", "You got off the Bike", and why a saddle
                  was refused). The tracker plates step aside while the shop's cards are up (the
                  twelfth card sits in their corner; the wallet is on the cards' title).
                  `VehicleFlowTests` (1: bought with the card's key, the prompt, E, 8 m/s down the
                  street with the prediction within a tick of the server every tick, the heading
                  the others see, the rack from the saddle, the jump key off on the rider's end
                  at once, a late joiner finds it parked); in the smoke the host buys one in
                  shift 2's Prep, gets on through its prompt, rides the street at 8 m/s, opens the
                  rack, gets off, and in the Lobby no bike is left on either end (`bike-*.png`)
      - [ ] the other blueprints (they need belts, sorters, vehicles, turrets), the other
            vehicles, hires and services
- [x] **Perk system** (StatSheet, modifiers, team/personal, prerequisites, exclusions) with 11
      perks (doc 01 §2.5 and §4, doc 07 §6, doc 08 §2.9, doc 09 §2.10; design choice 66)
      - [x] *The stat sheet.* `content/perks/perks.json` (validated: unknown stat, op, category,
            rarity, scope, excluded perk; a team perk that stacks; a personal perk on a team
            stat), a closed `Stat` enum of fifteen numbers, `StatSheet` (base × every mul + every
            add). A team perk is held once by the team, a personal one by its holder up to
            `maxStacks`; exclusions hold in both directions. Eleven perks, each acting in the sim:
            Long Legs, Quick Hands, Strong Back, Second Wind, Postal Sprint (personal); Union Rep,
            Stamp Collector, Fragile Handling, Grace Period, Early Warning, Union Muscle (team).
            `PerkTests` (23)
      - [x] *The Draft.* Each card's rarity is rolled first (60 / 30 / 10, Rare +5 per shift),
            then a perk of it that somebody here can still take; one card each (`PickPerk`), the
            perk is theirs at once; a team card is gone for the others once taken; the Draft ends
            as soon as everybody present has chosen, and at 30 s the clock gives the first open
            card to whoever has not. Who is away gets none; who comes back mid-Draft finds the
            table as it stands. Perks end with the run. Protocol `m1.10` (`DraftOffer`,
            `PerkPicked`, the `PickPerk` request; perks, offer and picks in the Welcome).
            `DraftTests` (14), `DraftFlowTests` (6), `CodecTests`
      - [x] *On screen.* The three cards in the middle of the view — key, name, category badge, a
            frame in the rarity's colour, what it does, "Team perk" / "Personal perk · stacks to
            3 · never with …", and who took it as soon as they did; a card not open to you is
            greyed and says why ("The team has it", "Not with Heavy Boots", "You hold all you can
            of it"), your own is framed green. Keys 1–3 take a card (and are the hotbar's again
            once you have chosen). Under the cards the 30 s as a bar and the run's perks so far;
            the hint under the top plate says "Take a perk: [1] [2] [3] · 1 / 2 have chosen",
            then "You took Union Rep · waiting for 1 more"; a "Perks · team 1 · yours 2" tracker
            plate for the rest of the run; feed lines ("Bo took Union Rep for the team", "You
            were given Long Legs"). The smoke reads the cards off the screen against the
            server's, presses a card's key and finds the perk on both ends (`draft1.png`)
      - [x] **Rerolls** (doc 01 §2.5 "one free reroll per player per run", §5.2 a second from
            Postal Rank 4, doc 09 §2.10 "reroll button with remaining count", doc 11 §10; design
            choice 91). Before choosing, R or the button under the cards draws three new cards
            of the player's own — none of those they had, all open to them, from the Draft's perk
            stream — and the others keep the shared ones. `rerollsPerRun` (1) and
            `rerollSecondRank` (4) in the balance; the rerolls come back with a new run. The card
            taken, the clock's pick and the Welcome of who comes back mid-Draft all go by the
            player's own cards; a pick is shown on others' screens by its perk, not its number
            ("Taken by Ada" only on the card of the same perk). `NoRerolls` / `NoNewCards`
            refusals, feed lines ("You drew new cards · 0 rerolls left this run"), the hint's
            "[R] new cards". Protocol `m1.18` (the `Reroll` request; the offer event to each
            player with `reroll` and `rerollsLeft`; rerolls and the pick's perk in the Welcome).
            `DraftTests` (+4), `DraftFlowTests` (+2), `CodecTests`; the smoke's first Draft
            clicks the button and checks the new cards on both ends (`draft-reroll` shot)
      - [ ] the personal-perk cap (M4), the remaining perks of the doc's list (they need
            buildings, vehicles, the shop)
- [x] Lobby with seed, archetype, kit; join-in-progress in Prep; disconnect grace
      - [x] a Lobby the host starts from; joining in Lobby and Prep
      - [x] **The 120 s disconnect grace** (doc 06 §6): who loses the connection outside the
            Lobby stays where they stood — "Bo (away)" over their head, not moving, not hurt, not
            hunted, pockets on them, seat held — and the feed says "Bo lost the connection — 02:00
            to come back". The same account back within the two minutes is the same player (id,
            body, pockets), in any phase, told what they missed ("Bo is back"); otherwise the
            pockets lie in a bag where they stood and they are gone ("Bo did not come back"). Prep
            does not wait for them, the next shift counts without them, the host role passes on.
            Protocol `m1.9` (`PlayerAway`, `PlayerBack`, an `away` flag per player, an account in
            the Hello, the raid / respawn / protection ticks and the loot on the ground in the
            Welcome). `AwayTests` (18), `AwayFlowTests` (9), `CodecTests`, `ProfileTests`; the
            smoke drops an ENet guest in Prep, reads the name tag and the feed on the host's
            screen and brings them back as Player#2; design choice 65
      - [x] **The starting kit, picked in the Lobby** (doc 03 §4.4, doc 01 §5.2, doc 06 §5–§6;
            design choice 86). `content/kits` (validated: unknown item or vehicle, more than one
            stack's worth, a rank under 1, a default that is not open from rank 1): **Land**
            (rank 1, the default: Axe, Fiber ×4; the team Log ×10 and a Bike), **Sea** (rank 2:
            Axe, Rope ×2; the team Log ×10), **Pipes** (rank 10: Pickaxe; the team Iron Ingot
            ×10). The Hello carries the player's Postal Rank from their profile; `SetKit` in the
            Lobby takes a kit up to it (`RankTooLow` otherwise, `WrongPhase` once the run is on).
            As the run starts every player present gets their kit's personal items in the
            inventory; the host's kit gives the team its items in the chest by the Post Office
            (the map's Depot) and its vehicles parked by the counter; who joins in Prep gets
            theirs on the way in, who comes back after losing the connection not twice; every
            run anew. On screen: **K** picks the next kit open at your rank, the Lobby's hint ends
            "[K] kit: Land", the feed "Your kit: Sea — Axe, Rope ×2 for you · Log ×10 for the
            team", "Your Land kit is in your pockets: Axe, Fiber ×4", "The team's kit (Ada's Land
            kit): Log ×10 in the chest by the Post Office · a Bike by the counter". Every
            player's kit is in the snapshot. Protocol `m1.16` (the Hello's rank, the `SetKit`
            request, `KitGiven`, a kit byte per player). `KitTests` (4), `KitFlowTests` (1),
            `CodecTests`, `ContentValidatorTests` (1 new); the smoke checks the Land kit at the
            first Prep, then walks to the chest and puts the Axe and Fiber in with the Logs, and
            rides the kit's bike in shift 2 (its card says "A Bike for each of you already")
      - [x] **The world and its seed, picked by the host in the Lobby** (doc 01 §1 and §5.3,
            doc 06 §5, doc 09 §2.2; design choice 87). `SimWorld.WorldChoices`: the test street
            and the seed's Small Island. `SetWorld` (host, Lobby: `NotHost` / `WrongPhase`
            otherwise) builds the map and swaps it in where the old one stood — the Intake stays
            the same container, the map's chests are new ones, nodes, what is built, perks and the
            shop start afresh, the mail and the raids follow the new seed, every carrier stands on
            the new pad — and sends one reliable `WorldChanged` (id, seed, hash, the chests'
            containers): every client builds the map itself, checks the hash as at the Welcome
            (a mismatch ends the connection) and swaps its world, and the screen is built anew
            for it (out of the tree, as the first time). Who joins later is welcomed into it. On
            screen in the Lobby, three plates where the shift's are: the world and its seed as a
            string to share ("Small Island · PM1-SMALL-2A94F8CE", doc 01 §5.3 without stamps),
            the kit ("Kit: Land · [K]"), and for the host "[V] world · [N] new seed"; **V**
            steps through the worlds from the same seed, **N** draws a new one; the feed "Ada
            chose Small Island · PM1-SMALL-00000003 · 46 houses, 5 districts". Protocol `m1.16`
            (the `SetWorld` request, `WorldChanged`). `WorldChoiceTests` (2), `WorldFlowTests`
            (1), `CodecTests`; the smoke, back in the Lobby after its run, reads the plates, goes
            to the island with V (the screen rebuilt, the host on its pad, out of the saddle),
            draws a new seed with N, and goes back to the test street from its own seed for the
            stages that follow (`lobby-island.png`)
- [x] Content pipeline: JSON defs, validator in CI, hot reload
      - [x] JSON defs and the validator (`tools/validate-content.sh`, run by `tools/smoke.sh`)
      - [x] **The CI job** (doc 07 §10; design choice 89). `.github/workflows/ci.yml`: three jobs
            on every push and pull request — the content validator, the Sim and Net tests, and
            the headless smoke (after the validator; Godot .NET 4.7.2, the csproj's SDK, fetched
            from the release page and cached, the project imported once). Each job is a stage
            of `tools/ci.sh` (`content`, `test`, `smoke`; all three without arguments), so the
            box runs exactly what CI runs. The content stage also proves the validator still
            fails: a copy of the content with a recipe for an unknown item must be turned away,
            naming it. Not run on GitHub from here (nothing is pushed); every stage passed
            locally. The export of the dedicated server and clients (doc 07 §10's second line)
            waits for export presets.
      - [x] **Hot reload, F6 in a debug build** (doc 07 §6; design choice 90).
            `ContentDb.ReloadFrom` loads and validates the files as at boot; if every table
            still has the same ids in the same order and the maps and street names are the same,
            the new values are copied into the defs already loaded (their defs inside — a
            recipe's Log, a bike's cargo grid — stay the loaded ones), the balance table is
            swapped in place and the content hash is the new one. Then the server reads its
            numbers again and applies the perks taken again over them (a carrier's health keeps
            its share of a new maximum), the host's client predicts with the new numbers, the
            screen's shots and swings read theirs, and only a guest with the new content gets in
            (`ServerRuntime.ContentReloaded`, `ClientRuntime.ContentReloaded`). An id added,
            removed or moved, a changed map or broken content is refused, nothing changes, and
            the feed says what was in the way ("Content not reloaded: …"; the log has all of it).
            Only a host alone in the game may: a guest's content could not follow.
            `ContentReloadTests` (6), `HotReloadFlowTests` (the prediction keeps with the
            server at a new walk speed; a guest on the old content is turned away); the smoke's
            first Prep reloads through `res://content` with the walk speed changed, then a
            broken balance.json, then the files as they are, and checks each.
- [x] Results screen and MetaProfile persistence (XP only) — the run / shift loop above. The
      unlocks table is not wired yet.
- [ ] Resource nodes and harvesting (wood, fiber, stone, ore); hand-built Wooden Wall and Chest
      from harvested materials
      - [x] a chest in the world, placed by the map (nit 9)
      - [x] **The nodes in the sim** (doc 02 §3.9, doc 08 §2.3 and §2.8; design choice 72).
            `content/nodes/nodes.json` (validated: unknown prop, item or tool rule, a prop variant
            claimed twice, a node no tool can work, a tool for what no node gives) makes the
            map's own scenery
            give: every Tree is wood (Logs, 5 hits, 2 a hit with the Axe, 1 by hand), a Bush
            Fiber (3 hits of 3) or Berries (2 hits of 2), a Boulder Stone (6 hits of 3) or, one in
            three, an Ore Vein (Iron Ore, 8 hits of 2) — the last two only with the Pickaxe in the
            hand. 92 nodes on the test street. `Harvest` is one hit: in reach, in Prep or
            Delivery, not faster than a swing (0.6 s), and only if what it gives fits in the
            pockets (a hit that does not fit is not spent). A node worked out stays where it is —
            a stump, a bare twig, rubble — and gives nothing; the bushes are full again when the
            next shift begins, everything with the next run. Protocol `m1.12` (the `Harvest`
            request, `NodeHarvested`, `NodesRegrown`, the worked nodes in the Welcome).
            `HarvestTests` (11), `CodecTests`, `ContentValidatorTests`
      - [x] **The nodes on screen** (doc 09 §2.4, the interact prompt's "harvest"). Looked at from beside it a node has a
            prompt — "Pick Bush · Fiber x3 a hit · 3 hits left [hold E]", "Chop Tree · Log x1 a
            hit (more with an Axe) · 5 hits left [hold E]", in red with the ✗ "Boulder · needs a
            Pickaxe in the hand" or "Berry Bush · no room in your pockets for Berries x2". E held
            fills the bar under the prompt once a swing; at its end the hit is asked for and the
            hammer swings in the hands (the others see the swing on the carrier's body). The feed
            counts a run of hits in one line ("You gathered Fiber x9", "Ada gathered Log x4") and
            says "The bushes have grown back". Berry bushes carry red berries, an Ore Vein rust-
            red lumps in the rock; the nodes are their own mesh batch, drawn anew only when one
            runs out or grows back. `HarvestFlowTests` (4); in the smoke the host picks a bush
            clean, is refused at a boulder, chops a tree down by hand, looks at a Berry Bush and
            an Ore Vein, and sells the lot at the counter (`harvest-*.png`)
      - [x] **Building by hand, in the sim** (doc 04 §1, doc 08 §2.6–§2.7; design choice 77).
            `content/buildings` and `content/recipes` (validated: a behaviour nothing honours
            yet, a footprint other than one tile, a recipe that makes an item, takes time, names
            an unknown input or a card that is no blueprint, a building without its recipe) give
            the Wooden Wall (3 Logs, 300 HP, turned two ways) and the Chest (4 Logs, 200 HP, a
            chest's 8×4 grid). `PlaceConstruct` puts one on a tile within 8 m in Prep or
            Delivery — open ground only: not the street, not where a house, tree, fence, mailbox,
            the Intake, the counter or the pad is, not onto a carrier or a raider — out of the
            Logs in the pockets (inventory first). `RemoveConstruct` takes it down for all of
            its Logs in Prep, half in Delivery, less for what it has lost of its HP; a chest
            only once it is empty. Walls stand in everybody's way across their tile and close
            the corner where two meet; a chest is hopped over. The raiders path round what is
            built; shut out, they break the wall in their way (a blow of their damage every
            attackRate: three Raiders are through a Wooden Wall in about 10 s), and a blow at a
            carrier right behind a wall lands on the wall. A broken chest spills into a bag.
            What is built stands from shift to shift and is gone with the run. Protocol `m1.13`
            (the two requests, `ConstructPlaced` / `Removed` / `Damaged` / `ConstructsCleared`,
            what stands in the Welcome). `BuildTests` (29), `CodecTests`, `ContentValidatorTests`
      - [x] **Building by hand, on screen** (doc 09 §2.7 Build mode, doc 09 §4 "B"; design
            choice 78). **B** puts Build mode up in Prep and Delivery ("Building is done in
            Prep and during Delivery" in the feed otherwise, and it comes down by itself with
            the phase or a fall). In the hotbar's place (two rows at most, clear of the feed
            and the trackers — the smoke checks) the recipe bar — "[1] Wooden Wall · Log x3
            [2] Chest · Log x4", the chosen one green (amber when the pockets cannot pay for
            it), the others red when they cannot — and the keys under it ("[LMB] build [R] turn
            [hold RMB] take down [Q] pick [B] done"). The ghost stands on the tile of the ground
            the crosshair is on (up to 14 m), a see-through palisade or chest and the tile it
            takes, **green where it can be built and red where not**, and the prompt line says
            why: "Wooden Wall · Missing 3 Log", "On the street: it stays open for the mail",
            "Something stands there", "Somebody is in the way", "Too far away", "From shift N",
            "Needs the … from the shop". The number keys choose, R turns, the left button builds
            on green; on something built the prompt is "Take down Wooden Wall · Log x3 back
            [hold RMB]" (less with lost HP, "· 180/300 HP"; "empty it first" for a chest with
            something in it) and the right button held 0.5 s fills the bar and takes it down;
            Q picks its recipe and turn. Esc puts the mode down before it lets the mouse go.
            Built chests open with E like the map's. In the world a Wooden Wall is a palisade
            of pointed logs on two rails along its arms — two walls that meet close their
            corner on screen as in the sim —, darker and leaning under half its HP; a Chest
            is the map's banded chest. Feed lines "Ada built a Wooden Wall", "You took down a
            Chest", "The raiders broke a Wooden Wall". The client mirrors what stands (Welcome
            and the four events), walks against it in its own prediction, the delivery bot
            paths round it. `BuildFlowTests` (4); in the smoke the host builds a turned wall
            with the tree's Logs, sees the ghost red out of reach, takes the wall down, builds,
            opens, closes and takes down a Chest, builds the wall again and leaves it for the
            run — gone on both ends in the Lobby (`build-*.png`)
      - [x] **Sand and the Shovel; tools drawn in the hand** (doc 02 §3.2 and §3.9, doc 10
            §1.1–§1.2 and §3; design choice 80). The test street has a **beach**: the map's new
            optional `beaches` (tile rects, `TileType.Beach`, walkable open ground that can be
            built on) lays sand in its south-west corner, towards the harbour. On it lie **Sand
            Piles** (`PropKind.SandPile`, grown from a random stream of their own, so no tree of
            the street moves: the scenery key reads a beach as grass) — `sand_pile` in
            content/nodes: 4 Sand a hit, 4 hits, full again with the next shift, only with the
            **Shovel** (content/items/tools, harvests `sand`), $0.90 on the counter from shift 1:
            the eleventh card, on the **minus key** ("[-]" — the blueprint moved there, the
            Shovel stands with the other tools). A heap of pale sand with shells and pebbles, a
            low ring round a hollow when dug out; the ground shader draws the beach's tiles as
            rippled sand. **Tools in the hand**: an Axe, a Pickaxe or a Shovel in the held slot
            is drawn in the right hand of the first-person view for as long as it is held, and
            it is what swings — at a node and at a raider (the blow is still the hammer's) —;
            the cannon still comes up for a shot. The others see it in the carrier's fist: the
            snapshot carries a `HeldTool` byte per player (protocol `m1.14`). `HarvestTests` (2
            new: the pile wants a Shovel and regrows; the beach is open ground and moves no
            tree), `HarvestFlowTests` (1 new: the prompt, the dig, the tool seen by the other
            client), `CodecTests`, `ShopTests`, `ContentValidatorTests`; in the smoke the host is
            refused at a pile with bare hands, is handed a Shovel, holds it in view and digs the
            pile out, and sells the Sand (`harvest-sand_pile-*.png`)

- [x] **The Gate and the Alarm Post** (doc 05 §4, doc 08 §2.6, doc 10 §2, doc 11 §10; design
      choice 81), the two defences without a blueprint
      - [x] *In the sim.* The **Gate** (`gate`, behaviour `gate`: 4 Logs and an Iron Ingot, 500
            HP, turned two ways) joins the walls in a line; carriers walk through it — their
            movement, the client's prediction and the delivery bot's tile paths
            (`TilePathfinder.Find(carrier: true)`) — and the raiders treat it as a wall: their
            paths keep off it, shut out they strike it, and it is cover. The **Alarm Post**
            (`alarm_post`, behaviour `alarm`: 2 Logs and an Iron Ingot, 100 HP, params
            `warningBonusSeconds` 15 and `revealRadius` 40, validated) stands in the way like a
            chest; every post within 40 m of the raiders' way in rings the raid in 15 s earlier,
            to 45 s in all (`waves.json` `maxWarningSeconds`, perks included) — built or taken
            down in Prep or in Delivery before the warning, it moves the warning; once the
            raid is announced it stays. A raider within 40 m of any post is marked
            (`RaidRules.Revealed`, `ClientWorld.MarkedEnemies`). Build prompts say "rings the
            raid in 15 s early" or "too far from the raiders' way in to ring early", the raid's
            feed line "… · 30 s of warning with the Alarm Post", the raid tracker "· 3 by the
            alarm". `GateAndAlarmTests` (6), `BuildFlowTests` (1 new), `BuildTests`,
            `ContentValidatorTests`
      - [x] *On screen.* A Gate is two squared posts with pointed caps under a beam, in the
            palisade's line (what joins it from the sides is palisade), and two plank leaves
            with battens and iron straps that swing open while a carrier is within 2.6 m and
            shut behind them. An Alarm Post is a tall post on a footing with a little red roof,
            a brass bell on a crossbar and a lamp on top that flashes red while a raider is
            within its 40 m. A raider within reach of a post wears a red arrowhead over its
            head, seen through walls. Build mode's recipe bar now has six cards: three rows of
            the smaller font between the feed and the bottom edge, the keys line over them (the
            smoke checks every card against the bar, the margin, the keys and the feed). In the
            smoke the host builds a Gate turned across the way, walks through it as it swings
            open, builds an Alarm Post in range (its prompt says so), and in the shift's raid the
            feed names the post's warning, the lamp flashes and the raiders are marked
            (`gate-*.png`, `alarm-*.png`)

Next in this order: the other blueprints and vehicles wait for belts, sorters and turrets (later milestones).

### 4. M2 — Automation (doc 12 M2, doc 04), in the docs' order

Doc 12's M2 "Systems" list, the parts of it in doc 04's order (§1 building, §2 item transport,
§2.3–§2.6 splitters, sorters, inserters, storage, §3 vehicles, §3.3 ports). A box is ticked when
the whole line exists; the sub-lines say which parts do.

- [ ] Build mode with ghost validity, drag lines, deconstruct, pipette; construct registry and
      placement validation; terrain flattening deltas
      - [x] Ghost validity, deconstruct, pipette (M1, above)
      - [x] **Drag lines** (doc 04 §1.1, doc 09 §2.7). For belts and walls (`BuildingDef.Drags`)
            the attack button pressed on a tile begins a line, held it runs along the row or the
            column the crosshair is further along (up to 16 tiles, `BuildRules.MaxLine`), and let
            go it is laid: one `PlaceLine` request (protocol `m2.0`), every tile judged by itself
            on the server, built from the start of the line on until the materials run out. A
            belt faces the way the line was drawn, a wall stands along it; a press let go where it
            began builds one tile as before. The ghost is every tile of the line, green or red
            each (`ClientWorld.LineStates` does the server's sums: tiles past what the pockets
            hold are "Missing"), the prompt "Lay 5 Conveyor Belts · Plank x5, Iron Ingot x5 [let
            go]", the feed one line ("You built 5 Conveyor Belts"). `BeltTests`,
            `BeltFlowTests` (the gesture through `BuildController`, a wall down a column)
      - [ ] Terrain flattening deltas
- [ ] Belt segments (compiled runs, two lanes, ramps, elevated), splitters, mergers; Intake
      outfeed; endpoints into mailboxes, containers, vehicles
      - [x] **Conveyor Belt Mk1** (doc 04 §2.1, doc 10 §2.1, doc 07 §8; design choice 94).
            `belt_mk1` (behaviour `belt`, 80 HP, a Plank and an Iron Ingot, 2 m/s, four ways,
            flat: walked over and laid under the feet). `Automation/BeltNetwork` compiles the
            belts into **segments** — from a head with no single feeder behind it, tile after tile
            while each is fed by the one before it alone (corners included), so a merge starts a
            segment and a loop is one segment feeding itself — named by their head tile, so the
            server and every client compiling the same tiles agree. **Two lanes**, 0.5 m apart
            (Cargo takes both and 2 m: `kinds.json` beltLength 2.0), advanced in one pass a
            segment, front first, each item up to the one ahead in its lane. **Endpoints**: the
            next segment's head (straight on an item keeps its lane, from the side it takes the
            lane of that side), a **mailbox** (the tile it faces holds one: in Delivery, at the
            destination type's insert rate, by the hand's rules; mail it would not take as a
            match waits at the end and the line backs up), otherwise the end holds what comes.
            Taking a belt down (or losing it) puts the mail on it back into the Intake; the rest
            keep their place by tile. A belt's delivery pays as a hand's, credited to nobody ("A
            belt delivered 1 Letter to 4 Larch Lane"). *On screen*: a steel frame on legs,
            yellow-capped rails, a dark rubber belt with pale chevrons, a corner as an L
            (`ConstructsView.BeltTile`); the mail riding it as one MultiMesh (`BeltItemsView`),
            an envelope flat and a parcel standing, in the colour of its house. `BeltTests` (7),
            `BeltFlowTests` (4), `CodecTests`, `BuildTests`, `ContentValidatorTests`; in the smoke
            the host drags five belts to a mailbox, three letters ride to its end and wait (Prep),
            the same on both ends and drawn, and in Delivery the mailbox takes them
            (`belt-drag`, `belt-line`, `belt-mail` shots)
      - [x] **Intake outfeed** (doc 04 §2.6; design choice 95). A belt on the tile north of the
            Intake's, facing north (`BeltNetwork.OutfeedTile`, `OutfeedRotation`), is fed by
            the Intake in Prep and in Delivery: the oldest mail there first (spawn order, FIFO by
            mail id), one piece a tick while the belt's head has room, the Intake's grid and
            backlog following. A chute out of the counter's north end and a yellow arrow painted
            on the tile show where; a belt ghost there says "fed by the Intake" (or "[R] to face
            away from the Intake"). The map chest moved a tile up and over on the test street and
            in the generated towns, out of the outfeed's way. `BeltTests` (FIFO, only the belt
            facing away; on 20 island seeds the outfeed tile takes a belt and leads somewhere); in
            the smoke the host drags two belts north out of the outfeed, the Intake's oldest mail
            rides them the same on both ends (`outfeed-drag`, `outfeed-mail`), and taken down they
            give it back to the Intake. The Presort perk (by street) comes with the perk pool
      - [x] **Splitters and mergers** (doc 04 §2.3, doc 10 §2.1; design choice 96). `splitter`
            and `merger` (150 HP, two Iron Ingots and a Plank, four ways, low enough to walk
            over). Each is a one-tile segment of its own in the belt network, so mail crosses it
            as it crosses a belt and the clients follow it by the same events. A **splitter**
            takes mail from behind only and hands what reaches its end to front, left and right
            in turn, skipping a way with no room; with every way full it backs up. A **merger**
            takes from back, left and right in turn — an input whose turn it is holds the others
            back only while it has mail waiting — onto its one output. Drawn as the belt under a
            steel housing with yellow arrows on its roof, out for a splitter and in for a merger.
            The recipe bar takes a fourth row (the feed goes up 18 px while Build mode is up).
            `BeltTests` (splitter round-robin and skipping a full way, merger in turn),
            `BeltFlowTests` (a client's mirror through a split-and-merge loop), `BuildTests`,
            `ContentValidatorTests`; in the smoke a Splitter is built by its key (`splitter`
            shot). The optional per-output kind filter waits for the filter panel the Address
            Sorter brings
      - [x] **Fast Belt** (doc 04 §2.1 Mk2, doc 10 §2.1, §3; design choice 97). `belt_mk2` (100 HP,
            a Plank and two Iron Ingots, 4 m/s) behind `bp_fast_belts` (Blueprint: Fast Belts,
            $5.00, from shift 3, once a run for the team). It shares the Conveyor Belt's key of
            Build mode: recipes now have a `group`, the wheel steps through a group while Build mode
            is up, the key remembers the choice (`BuildController.Cycle`, `Chosen`,
            `ContentDb.RecipeKeys`). Drawn with blue rail caps and doubled chevrons.
            `FastBeltTests` (content, refused without the blueprint, bought in shift 3 and laid
            after two Conveyor Belts: a segment of its own at 4 m/s), `BeltFlowTests` (the wheel and
            the key on a client), shop tests; in the smoke the wheel on the belt key shows "Fast
            Belt 2/2", red for want of its blueprint, and back
      - [x] **A carrier carried along, pushing a belt** (doc 04 §2.1 "player operation", doc 11
            operated multiplier 1.5; design choice 98). Standing on a belt, a splitter or a merger
            (grounded, alive) a carrier is moved the way it runs at its rated speed on top of their
            own walk, and stands on its running surface (`BeltNetwork.SurfaceHeight`, 0.36 m) —
            in `PlayerMovement.Step`, so a client predicts it exactly. Held interact at a belt
            (the new prompt "Push the Conveyor Belt · ×1.5 while held [hold E]", "Pushing the
            Conveyor Belt · 3.0 m/s") sends `PushBelt` every 0.4 s (protocol `m2.1`); the server
            gives the segment 0.7 s of push (`beltPushMultiplier`, `beltPushSeconds` in
            balance.json) and tells everybody (`BeltPushedEvent`), each side counting it down in
            its own steps. `BeltRideTests` (carried at 2 m/s and set down off the end, predicted
            exactly, a push at 3 m/s that runs out, refused out of reach or off a belt),
            `BeltFlowTests` (the prompt, the push on both ends, the ride with nothing to correct),
            `CodecTests`; in the smoke the host pushes the line from beside it and rides it 2 m in
            a second (`belt-push` shot)
      - [x] **Ramps and elevated belts** (doc 04 §2.1 elevated variant, §1.1 street crossings,
            doc 10 §2.1; design choice 99). `belt_mk1_elevated` (Elevated Belt, 80 HP, two Planks
            and an Iron Ingot) runs one level (2.2 m) up on two posts and may stand on a street
            (`BuildingDef.OnStreet`; a ground belt there is still "OnStreet"). `belt_mk1_ramp` (Belt
            Ramp, 120 HP, three Planks and two Iron Ingots) is the first construct of two tiles
            (`BuildingDef.Length`, footprint [1, 2]): built on a tile it takes the one in front too,
            both must be open (`BuildRules.CheckPlacement`), one `Construct` stands on both, taken
            down by either; R turns it through 8 ways, four up and four down (the prompt says "up a
            level the way it faces" / "down …"). In the belt network a tile has a height mail comes
            in and goes out at (`InLevel`, `OutLevel`, in half levels); mail is handed on only where
            they meet, a ramp takes mail from behind only, a mailbox only from a belt on the ground
            and the outfeed feeds only one. Mail is drawn at its height along the slope
            (`BeltNetwork.HeightAt`). Neither carries a walker: under an elevated belt the street
            stays open. Both are on the belt's key (the wheel: 3/4 and 4/4). `RampTests` (content;
            two tiles, refused onto the street, taken down by the second; a line over Larch Lane up
            a ramp, over two elevated belts and down another with a letter riding it; no hand-off
            where heights differ, none into a mailbox from above; walking under), `BeltFuzzTests`
            with ramps, elevated and fast belts in the mix, `BeltFlowTests` (a client builds the
            crossing, the same segments, mail and checksums on both ends, a late joiner's ramps
            on two tiles); in the smoke the wheel builds a Belt Ramp (`ramp` shot) and an
            Elevated Belt on the street (`elevated` shot). Crossing another belt (two constructs
            on one tile) waits for a construct layer over the ground one
      - [x] The keys line of Build mode had grown into the tracker plates at the bottom right
            (with "[wheel] tier" it ran to x 1010 of 1280; before it, to about 967): it is
            shorter and drawn at 14 px while Build mode is up, and the smoke measures its words
            against the recipe bar's span
      - [x] Endpoints into containers: the Depot (see the Depot under M2's sorting line); vehicles wait for the Mail Truck
- [ ] Belt replication: LaneInsert/Remove/Checksum/State; client visual sim; interest management for segments
      - [x] Events `BeltState` (a segment whole: when compiled anew, to a joiner after the
            Welcome, on request), `BeltItemAdded`, `BeltItemMoved` (end to the next head),
            `BeltItemRemoved`, `BeltChecksum` (every 2 s a segment: count and a hash of the mail
            lane by lane in order); a client whose checksum is off asks `BeltResync` and gets the
            segment whole. The client runs the same `BeltNetwork` each tick without endpoints, so
            nothing is drawn past an end before the server says so
      - [ ] Interest management for segments (everything is global, as for the rest so far);
            the M2 bandwidth and 5000-item acceptance figures
- [x] Address Sorter with filter UI; Inserters (and the Long Inserter); Depot
      - [x] **Address Sorter and its filter panel** (doc 04 §2.4, doc 09 §2.7, doc 10 §2.2–§3,
            doc 06 §3 ConfigureConstruct; design choice 103). `address_sorter_mk1` (behaviour
            `sorter`, 500 HP, four Iron Ingots and six Stones, footprint [2, 2]) behind
            `bp_sorting` (Blueprint: Sorting, $4.00, from shift 1). A construct may now be two
            tiles wide (`BuildingDef.Width`, `TileCount`; `Construct.TileOf`, `CentreX/Z`; a solid one
            pushes walkers from the middle of its tiles). In the belt network a sorter is a
            `SorterNode`: a belt facing into the tile it was built on, from behind, ends in its
            buffer (8); it looks at one piece every 0.5 s (`throughputPerSecond` 2) and puts it on the
            head of the belt out of its front, left or right by the first of those whose filter
            catches it, else out of its overflow (beside the input, going back); with no belt there,
            or no room, it waits and the line backs up. Filters (`MailFilter`: street, district,
            kind; one slot an output) live on the construct; a sorter taken down puts its buffer back
            into the Intake. **The splitter's kind filter**: an output with a kind takes that kind,
            the rest go round the outputs without one (each its own turn). The **filter panel** on
            the right of the inventory screen (`UI/FilterPanel`): E at a sorter, or at a splitter in
            Build mode ("[E] filters"), puts it up; a card for each way out with its key, its filter
            and how much of the Intake's mail it would take now (`SorterStatusEvent` every 0.5 s to
            whoever has it up), the sorter's buffer and counts over them; a click or the output's
            number key steps the filter on through nothing, the streets and districts open by the
            shift and the kinds, a right click back. `ConfigureConstruct` (open, close, set; protocol
            `m2.2`), `ConstructConfiguredEvent` to everybody, filters in the Welcome. Drawn as a
            grey steel machine on a base plate with an orange band, a lit console and framed mouths
            (green in, amber out, red overflow). Build mode's recipe bar takes a fifth row (the feed
            goes up 36 px). `SorterTests` (content, four tiles turned, **1000 pieces turned all four
            ways with no misroute and the overflow exactly the unmatched**, rate and buffer, the
            splitter's filter, the server's requests and status, mail out of a line and back to the
            Intake), `BeltFuzzTests` with sorters and filters in the mix, `SorterFlowTests` (E, the
            panel's text, key 1 on both ends, a late joiner, walking off; the splitter in Build mode),
            `CodecTests`; in the smoke the blueprint is bought by its card's key, the sorter built by
            key 0 (`sorter-ghost`, `sorter`), its filters put up by E and the front set by key 1
            (`sorter-filters`)
      - [x] **Inserter** (doc 04 §2.5, doc 10 §2.2, doc 11 "inserter rate 1.25"; design choice
            104). `inserter` (120 HP, two Iron Ingots and a Plank, no blueprint) on the sorter's key
            of Build mode (recipe group "sorting", doc 09 §2.7's Sorting category; the wheel steps
            between them, so Build mode keeps ten keys). In the tick after the belts
            (`SimWorld.StepInserters`), at its rate, it moves one piece of mail from behind it — a
            chest's or depot's grid in order, the Intake's oldest piece (on the outfeed's tile, facing
            away), or the piece waiting at the end of a belt running into it — to the front: a
            mailbox (in Delivery, by the rules a belt goes by), a chest, a sorter's input, or the head of a
            belt (which it starts, as a sorter's port does). It takes only what the front will take
            and its kind filter lets through (the filter panel, one card "Takes", by E), so nothing
            waits in its arm. A delivery by it is the team's and the feed says "An inserter
            delivered…" (`DeliveredEvent.Machine`; protocol `m2.4`); `InserterSwungEvent` swings its
            arm on every client (a boom with a grab turning from back to front with the piece, and
            back). Drawn as a steel post on a plate with an orange collar and a yellow arrow.
            Build mode lifts the feed 36 px and holds its oldest line back while up (the recipe bar's
            fifth row). `InserterTests` (content, chest into a mailbox at its rate and only its own
            mail, the Intake oldest first onto a belt and a belt into a chest, the kind filter into a
            sorter), `InserterFlowTests` (E and the panel, swings and the feed on a client),
            `BeltFuzzTests` with inserters, `CodecTests`; in the smoke an Inserter on the outfeed by
            the wheel on key 0 puts the Intake's mail on a belt, its arm swinging (`inserter`).
            The Long Inserter: see below
      - [x] **Long Inserter** (doc 04 §2.5, doc 08 §2.6 "reach (1 or 2)", doc 10 §2.2; design
            choice 106). `inserter_long` (120 HP, three Iron Ingots and a Plank, behind
            `bp_sorting`) is an inserter with `params.reach` 2 (`BuildingDef.Reach`, validated 1
            or 2 and only on an inserter): from two tiles behind (a chest's or depot's grid, the
            Intake, the end of a belt running into the tile between) into two tiles in front (a
            mailbox, a chest or depot, a sorter's input, a belt's head, which it starts), over
            whatever stands between (`BeltNetwork.FedBy` / `EndingAt` take a reach). On the
            sorter's key, third on the wheel. Drawn taller, with a boom a tile longer, blue bands and
            a counterweight (`ConstructsView.ArmLength`, `ArmHeight`). `InserterTests` (content and
            the blueprint; a chest into a mailbox over a belt that is not fed; the Intake over a belt
            on the outfeed onto a belt two on, off its end over an empty tile into a chest),
            `BeltFuzzTests` with it, the content pins; in the smoke the wheel on key 0 builds one a
            tile past the outfeed, it puts the Intake's mail on a belt two tiles on, its arm
            swinging with the grab 3.02 m out (`long-inserter`)
      - [x] **Depot** (doc 04 §2.6, doc 10 §2.2; design choice 105). `depot` (behaviour
            `container`, 800 HP, footprint [2, 2], eight Stones and four Iron Ingots, no
            blueprint) with the `depot` grid of 16 × 10 (`beltAccess: any_side`), on the chest's
            key of Build mode (recipe group "storage", the wheel steps between them). A belt on the
            ground running into any of its four tiles from any side ends in it
            (`BeltEndKind.Container`, `IBeltSink.TryStore`): the piece goes into its grid while
            there is room, else it waits and the line backs up; an inserter pulls from it as from a
            chest. A chest is still fed by an inserter only. Its grid is reached from round all its
            tiles (the container sits at its middle with `Reach` its half width, on the server and
            in the client's prompt); it is not taken down with anything in it. Drawn as a ribbed
            steel store between orange corner posts with a roll-up door and the belts' amber band.
            `DepotTests` (content, belts from the west and the south into it, opened from beside it,
            refused removal, an inserter pulling it onto a belt; a chest holds the line); in the
            smoke the wheel on the chest's key builds one (ghost prompt "Build Depot"), E opens it
            with its grid on screen (`depot-open`, `depot`), and it is taken down again
- [x] Pipes (inlet filters, junctions, outlets, underground) behind bp_pipes
      - [x] **Pneumatic pipes** (doc 04 §2.2, doc 10 §2.1, doc 11 "pipe speed 5 m/s, spacing 1 m";
            design choice 107). Behaviour `pipe` with a role (`BuildingDef.PipeRole`): `pipe`
            (Pneumatic Pipe, 100 HP, an Iron Ingot and a Glass; straight, open behind and in front),
            `pipe_underground` (shown as Buried Pipe, 200 HP, twice the materials; straight, on a
            street too, walked and driven over), `pipe_junction` (120 HP; open on all four sides),
            `pipe_inlet` (150 HP; takes from behind, opens in front) and `pipe_outlet` (120 HP; opens
            behind, puts out in front), all behind `bp_pipes` (Blueprint: Pneumatics, $7.00, from
            shift 3) on one key of Build mode (the wheel steps through the five; the Splitter and the
            Merger now share a key, doc 09 §2.7's Transport, so Build mode keeps ten). Straight pipes
            and buried ones are dragged in lines and walked over; the boxes stand. `Automation/
            PipeNetwork`: two tiles are joined where both open towards each other; per outlet a
            breadth-first count of tiles to it; a capsule (one piece of mail) runs the shortest way
            (the lowest direction first on a tie) at 5 m/s. **Routing at the inlet**: the nearest
            outlet it reaches whose filter catches the piece (an outlet with no filter standing
            before a mailbox catches that house's mail), else the nearest default (no filter, no
            mailbox), of those with room (8 sent or waiting, `pipeOutletCapacity`); one a filter
            catches but none with room makes it wait, never a default. An inlet sends a capsule a
            metre's spacing apart (0.2 s) and takes a piece only when it can send it: from the end of
            a belt running into it (`BeltEndKind.Inlet`), from an inserter in front of it, or, on
            the outfeed's tile facing away from the counter, the oldest piece of the Intake it can
            route. An outlet puts its oldest piece out at 2 a second (a mailbox's insert rate if
            slower) into a mailbox (in Delivery; "A pipe delivered…", `DeliveryMachine.Pipe`), a
            chest or depot, a sorter's input, an inlet or a belt's head (which it starts), as an
            inserter does (`SimWorld.TargetOf` shared). A pipe taken down under capsules sends them
            the shortest way left from where they are, or back to the Intake; an outlet taken down
            gives its queue back. Its filter (street, district or kind, as a sorter's) is set on the
            filter panel by E ("PIPE OUTLET · what the inlets send it", "Takes", "this house's mail"
            / "whatever no other outlet takes"). Net: `PipeCapsuleEvent` (the mail, from a tile to
            an outlet: sent, sent round, or to a joiner from where it is), `PipeCapsuleRemovedEvent`
            (protocol `m2.5`); the client runs the same `PipeNetwork` without arrivals.
            *On screen*: a glass tube on a steel stand with orange collars, the capsules (in the
            colour of their house) seen through it (`PipeCapsulesView`), a junction a steel hub with
            four stubs, an inlet a box with a green mouth behind and an outlet one with an amber
            mouth in front, a buried pipe a flush hatch with yellow chevrons (none drawn under it).
            Build mode's recipe cards are now each half the bar wide (ellipsis as a last resort), at
            12 px with a tighter " · ", and the smoke measures every recipe of every key against
            that width. `PipeTests` (content and keys; the blueprint and the street; the Intake by
            filter and default, one capsule each 0.2 s; 5 m/s by the shortest way; a full outlet
            holding the inlet, an outlet and a pipe taken down under capsules; inserter → inlet,
            outlet → belt → inlet, outlet → chest; an outlet before a mailbox in Prep and in
            Delivery), `PipeFlowTests` (a client's capsules on the server's ways, a joiner's, the
            outlet's panel), `CodecTests`, the shop and content pins; in the smoke the server grants
            the blueprint, the wheel on key 0 builds a Pipe Inlet on the outfeed, pipes lead to an
            outlet, the Intake's letters ride drawn in capsules (`pipes`), wait in the outlet, and
            go back to the Intake when the pipes are taken down. Vertical pipes (up buildings) wait:
            nothing is built up a building yet
- [x] Mail Truck; Vehicle Depot with route console; NPC driver behaviour; player takeover
      - [x] **Mail Truck** (doc 04 §3.1, doc 10 §1.3 and §3; design choice 108). `mail_truck`
            (600 HP, 14 m/s on the road and 7 off it, two seats of which the driver's is used, a
            cargo bay `mail_truck_cargo` of 8 × 10 for mail) sold at the counter for $9.00 from shift
            2 once the team owns `bp_truck` (Blueprint: Motor Pool, $5.00, shift 2; shop entries may
            now `require` a blueprint, `ShopItemDef.Requires`, refused `NeedsBlueprint`); a carrier
            may have one of each kind of vehicle (a bike and a truck). It drives on the bike's
            physics, predicted the same, but its body is 3.4 m long: it bumps with a circle at its
            nose and one at its tail (`VehicleDef.Length`, `BodyOffset`). **Fuel**: an Oil Can
            drives it 5 minutes (`fuelSecondsPerCan` 300, tank 600); bought with one can's worth;
            it burns while it rolls, everybody hears the tank every second it is driven
            (`VehicleFuelEvent`, to a joiner too), and run dry it stops and sets its driver down; empty
            it is not driven ("out of fuel — an Oil Can in the hand fills it", `NoFuel`). With an Oil
            Can in the hand, E at it refuels it (`Refuel` request, protocol `m2.6`; `TankFull`,
            `NoOilCan`). The prompts: "Drive the Mail Truck [E] · fuel 05:00 of 10:00", "Refuel the
            Mail Truck with an Oil Can [E] · …", the fuel on the cargo prompt while driving.
            **Loading face**: standing parked (nobody in it), a belt on the ground ending into the
            tile its rear is on loads mail into its bay while there is room (`IBeltSink.TryLoad`), and
            an inserter facing that tile does too; ridden, the line waits. Drawn as a chunky cream
            van after the reference's: dark chassis and bumpers, a cab with dark glass and lit lamps,
            a taller cargo box with the post's red band, an amber roll-up door at the rear, four fat
            wheels turning. `SimWorld.RemoveVehicle` (its mail back to the Intake,
            `VehicleRemovedEvent`) for the smoke and tests. The shop's descriptions of the
            blueprints were shortened so the 17 cards stay one height and inside a 720 p view.
            `TruckTests` (content; sold with the blueprint beside a bike; 14 m/s, burning, dry,
            refused, refuelled and full; the nose bumping into a house; loaded by a belt and by an
            inserter, not while ridden), `TruckFlowTests` (the prompt, the drive predicted, the fuel
            on both ends, refuelled by E, a joiner), `CodecTests`, shop and content pins; in the
            smoke the server grants the blueprint and parks a truck with its rear at the end of two
            belts out of the outfeed, the Intake's mail rides into its bay, the prompt reads with its
            fuel, the host gets in, and it is taken away again (`truck` shot). The Vehicle Depot,
            the route console and NPC drivers are next; passengers wait for them
      - [x] **Vehicle Depot and its Route Console** (doc 04 §3.2, doc 10 §2.2, doc 09 §2.7; design
            choice 109). `vehicle_depot` (behaviour `vehicle_depot`, footprint [3, 3], 1000 HP, 12
            Planks, 8 Stones and 4 Iron Ingots, behind `bp_truck`) on the chest's key (the wheel's
            3/3: Build mode keeps ten keys); one for every two carriers in the game, rounded up
            (`DepotLimit`, the ghost says so). It is a pad: walked and driven over. Its **bay** is the
            middle of its nine tiles, facing the way it does (`SimWorld.Bay`); a truck got off with
            its middle on the pad is set into the bay if that is free (`SnapIntoBay`), and its rear
            then stands on the back middle tile, its **loading face**: a belt ending into that tile
            or an inserter facing it loads the truck. Its **Route Console** stands at the left of
            the bay (`SimWorld.Console`, the aim box there): E opens it on the filter panel — six
            stop cards, each stepped by its number key or a click (right click back) through the
            houses open by this shift, their streets and their districts (`FilterKind.Address`, a
            house by its destination index in 15 bits of the filter's 16); each card says how many
            pieces in the truck in the bay are for it, the line under the title how many houses the
            route visits and its round trip by the streets at a driver's pace
            (`Vehicles/RoutePlan`: the houses of each stop nearest first, each once; the server's
            driver will plan by the same code). Stops are kept as the construct's filters, so they
            travel by `ConstructConfigured` and the Welcome; protocol `m2.7`. Drawn as a concrete
            pad with the bay painted in yellow, a ribbed steel canopy on four orange posts, the
            loading face hatched with an amber arrow, and a grey console with a lit screen. A
            recipe card of three materials names two and "+1" (the prompt names all).
            `VehicleDepotTests` (content; the blueprint and the limit; a truck got off crooked set
            into the bay, a belt and an inserter loading it there; walked over; the console's stops,
            refusals and counts, the plan), shop and content pins; in the smoke the wheel on the
            chest's key builds one (`vehicle-depot-ghost`), a truck stands in its bay with two
            letters, E at the console puts it up and key 1 makes stop 1 the first house with "2 for
            it in the truck" (`vehicle-depot-console`, `vehicle-depot`); it now stays for the driver
      - [x] **NPC drivers and player takeover** (doc 04 §3.2, doc 10 §3 npc_driver, doc 11; design
            choice 110). The console's seventh card is its **driver**: key 7 or a click hires one
            for the shift ($1.50, `npcHireCost`; from shift 2 for a solo carrier, 3 for a group;
            one a depot, `AlreadyHired`), in Prep or Delivery (`ConfigureHire`, protocol `m2.8`).
            In Delivery the driver takes the truck standing in the bay out once it holds mail for
            the route's houses and nothing more has been loaded for 4 s (`npcDepartWaitSeconds`);
            it drives along the routing graph at 60 % of the truck's speed for the ground
            (`npcSpeedRatio`), burning fuel, to the houses of the stops nearest first
            (`RoutePlan`), and at each puts the mail for it into the box at the box's insert rate by
            the belts' rules (it never guesses), then comes back into the bay; what matched no
            stop rides home. Raiders within 20 m of the truck or the way still to go send it home
            (Fleeing); run dry it is Stranded until an Oil Can fills it. **Takeover**: a carrier
            may get in at any time ("Take the wheel of the Mail Truck [E] · the driver moves
            over"); the truck then runs at full speed under them, and the driver hands the mail out
            wherever it stands still within 3.5 m of a box of the route; getting off hands the
            wheel back in the same tick (left in the bay, the driver is home). At Payday the hire
            ends and the truck is put back into its bay if that is free. Every change is a
            `DriverEvent` (to a joiner too); the snapshot carries where each NPC-driven truck is
            (13 bytes each, only while there are any; doc 06 "without driver prediction"), eased
            between snapshots on the client. Drawn: a carrier in the post's blue with a cap in the
            cab, at the wheel or beside the carrier, the window beside them rolled down. The feed
            says when it is hired, sets out, turns back, runs dry, is back and is let go, and a
            delivery is "A driver delivered …"; the console's card says what it does ("out to 1
            Larch Lane (1 of 2)"). `DriverTests` (the hire's rules; a three-stop route delivering
            only its stops' mail at 60 % and back; takeover and hand-back within a tick with mail
            handed out meanwhile; raiders; Payday), `DriverFlowTests` (key 7, the truck followed
            on a client, the wheel offered), `CodecTests` (the snapshot's block); in the smoke key
            7 hires the driver in shift 2's Prep, and in Delivery it takes the truck's two letters
            to the first house, the truck drawn where the server has it with its driver aboard
- [x] Sea path: rowboat, pier, motorboat, small port, NPC captain; water navmesh
      - [x] **Water, the Pier and the Rowboat** (doc 04 §3.1 and §3.3, doc 02 §3.2, doc 10 §1.3, §2
            and §3; design choice 111). The map now has **water**: `TileType.Water` (shallow) and
            `DeepWater`, from a map file's optional `water` / `deepWater` rects; the test street
            grew from 26 to 34 rows, a harbour basin of eight rows along its south edge — shallow
            for three rows under the beach's west, deep along the rest of the quay — and the
            generated island's map gets six rows of the same under its beach (`WorldGenerator`
            `ShallowRows`, `DeepRows`); the scenery key and the sand piles' hash are the land's, so
            no tree or pile moved. Nobody walks into water and raiders do not path over it (it is
            nav-blocked), except onto a deck. The **Pier** (`pier`, 1 × 3, 300 HP, 6 Logs, on the
            chest's key) goes over shallow water only, built out from the shore or another pier
            (`NotOnWater`, `NotFromShore`); its deck is walked on and is in a boat's way. The
            **Rowboat** (`rowboat`: 120 HP, one seat, a 2 × 8 locker, 3 m/s on the water and
            nowhere else, `VehicleDef.WaterSpeed`) is placed in Build mode from `rowboat_build`
            (8 Logs, 2 Rope, behind `bp_rowboat`; one of each kind a carrier) — `bp_rowboat` is a
            blueprint the counter never sells (availability slot `kit`) and the **Sea kit** gives it
            to the team (`teamBlueprints`) — or bought for $2.00 from shift 2, moored on the free
            water nearest the counter. It is rowed on the bike's physics with the water as its only
            ground, predicted the same; the jump key gets the rower out onto the nearest point of a
            pier's deck or the shore within 2.6 m (`PlayerMovement.Landing`), and out on open water
            does nothing ("row to a pier or the shore to get out"). Prompts "Get into the Rowboat
            [E]", the feed "You are in the Rowboat · [Space] by a pier or the shore gets out".
            Drawn: the basin with a stone quay and sea walls, sand under the shallows and a dark
            bed under the deep, the water at 0.45 m below the quay; the pier a deck of weathered
            planks on posts with iron bollards; the rowboat a blue clinker hull with a cream
            gunwale, thwarts and two oars that sweep as it moves, rocking on the water; the map
            screen shows the water. `BoatTests` (content; nobody walks in; the pier's rules and its
            deck; the kit's blueprint, placing, 3 m/s, held to the water; out onto the pier, never
            into open water; bought and moored), `BoatFlowTests` (the prompt, rowing predicted, the
            sea wall, the hint, out on the quay on both ends), content and shop pins; in the smoke
            the host builds a pier from the beach, places a rowboat, gets in by its prompt, rows it
            out (`rowboat-prompt`, `rowboat-row`), fails to get out in the basin, and steps out onto
            the pier's end (`pier`)
      - [x] **Motor Boat and Small Port** (doc 04 §3.1 and §3.3, doc 10 §1.3, §2 and §3; design
            choice 112). `bp_motorboat` ("Blueprint: Harbour", $5.00, shift 3) opens the **Motor
            Boat** at the counter ($7.00, shift 3: 500 HP, two seats, 9 m/s on the water, an 8 × 10
            hold for mail, an Oil Can a 5 minutes as the truck's, moored on the free water nearest
            the counter) and the **Small Port** (`small_port`, 3 × 4 tiles, 1200 HP, 16 Planks, 10
            Stones, 6 Iron Ingots, on the chest's key): its back two rows a quay on open ground, its
            front two over deep water (`BuildRules.CheckPort`, `NotOnWater`). The quay is walked
            on; between its two pontoons the middle column of water is the **bay** (boats go there
            and nowhere else of the port), and a Motor Boat left with its middle on the port's water
            is set into it, its stern at the quay's edge (`SimWorld.Bay`, `SnapIntoBay`). The
            **loading face** is the quay's back middle tile (`PortLoadingTile`): a belt ending into
            it from behind the port, or an inserter facing it, loads the boat in the bay while
            nobody is in it. Getting out of a boat in the bay steps onto the quay. Drawn: a
            concrete quay with an orange rail and iron bollards either side of the bay, the loading
            face hatched with a roller chute down to the boat, a derrick with its hook over the bay,
            and two floating pontoons; the boat a white workboat with the post's red band, a
            wheelhouse with dark glass and a mast lamp, a hold aft with an amber roll-up door, an
            outboard. `PortTests` (content; bought with the blueprint, 9 m/s on fuel; the port's
            placement, its quay, bay and pontoons; a boat left crooked set into the bay, out onto
            the quay, loaded from a belt, not while ridden), shop and content pins; in the smoke the
            host builds a Small Port on the deep quay, a Motor Boat lies in its bay, and a letter
            rides a belt into its hold (`small-port`). NPC captains (and the water routes and
            navmesh they need) wait for a second harbour to sail to — a Business Dock or another
            town
      - [x] **The Business Dock: somewhere to sail to** (doc 02 §3.5, doc 03 §1.1 and §2.1, doc 08
            destination types; design choice 114). `business_dock` destinations (in
            `destinations.json` since M0) now stand in the world: a destination may open later than
            its district (`unlockShift`, `WorldMap.OpensAt`); the test street's is the eleventh,
            "11 Larch Lane", at the east end of the quay from shift 3 (lot 4 × 6, a 3 × 3 warehouse,
            its unloading zone's middle at the quay's edge, deep water in front), and the generator
            gives every Small Island one on the beach's east end over deep water from shift 4
            (`WorldGenerator.DockShift`), on the nearest street open by then. Cargo (5 × 8, not
            carried by hand) is addressed to it. In Delivery a vehicle standing in its zone — a truck
            on its yard or a boat at its quay, within `dockZoneReach` 4 m, parked or held still —
            has the Cargo for it taken out at the dock's insert rate (2 a second) by the belts'
            rules (`SimWorld.StepDocks`, `DeliveryMachine.Dock`: "The dock at 11 Larch Lane took 1
            Cargo +$7.20"); a belt ending into the zone's tile feeds it as a mailbox; the hand
            cannot (no prompt; `WontFitHere`). A kind whose share of a whole shift buys not one
            piece hands it to the largest kind as one without a destination does, so a solo run's
            Cargo first comes in shift 5 and a group's earlier. The town's scenery grows as if the
            dock were not there and its lot is cleared after, so no tree of the street moved.
            Drawn: a concrete yard, a ribbed steel warehouse with an amber roll-up door under a red
            band and a lamp, BUSINESS DOCK over the door, pallets of crates, the zone framed and
            hatched in yellow with bollards and a yellow jib crane, its address "· Cargo" in yellow
            (greyed with "opens in shift 3" until then); the map screen shows it as DOCK. `Houses`
            on the map lists the others (the bot, the playtest mailbox, tests). `DockTests`
            (Cargo for it from the shift it opens; a truck in the zone unloaded at the rate and
            nothing else; outside it or rolling through, nothing; a boat at its quay; a belt; not the
            hand), `WorldGeneratorTests` (fifty seeds: one dock on the beach over deep water, walked
            to, east of the Post Office, from shift 4), `ContentTests`, `IslandTerrainTests`; the
            smoke checks its sign. Nothing but belts may be built on a dock's yard (a port tried
            to take its quay in the smoke)
      - [x] **The water navmesh and the NPC captain** (doc 04 §3.3 "NPC captains behave like NPC
            drivers on water routes … a coarse water navmesh generated per seed", doc 10 §3
            npc_captain; design choice 115). `World/WaterNav`: every tile a boat may lie on (water
            with nothing over it but a port's bay) is a cell joined to its eight neighbours (a
            corner only past two open sides); A* over the cells, then the way pulled tight to the
            farthest point a hull sails straight to; read off the map and what is built when asked,
            so a pier across a lane is sailed round at once. `Berth`: the open cell nearest a
            destination's mailbox within the dock zone's reach (a dock has one, a house none). The
            Small Port now has a **Route Console** (`routeStops` 6, `npcHireCost` 150 like the
            depot's; `BuildingDef.HasConsole`) on its quay left of the bay: its stops are the docks
            open by this shift that a boat can lie at; key 7 hires a **captain** from shift 3
            (`npcCaptainHireShift`), $1.50 for the shift, one a port. The depot's driver code runs
            it: in Delivery the captain takes the Motor Boat out of the bay once it holds Cargo for
            the route and nothing more came for 4 s, sails the navmesh at 60 % of 9 m/s burning
            fuel, unloads at each dock's berth at its rate (`DeliveryMachine.Captain`, "A captain
            delivered 1 Cargo to 11 Larch Lane"), and sails home into the bay; raiders, a dry tank,
            a carrier at the wheel ("the captain stands by") and Payday as for a driver; a way cut
            off leaves the boat where it is. The console's line is the round trip over the water at
            a captain's pace ("1 dock · 160 m round trip over the water, about 00:30 …"); its cards
            say "for it in the boat"; the feed "You hired a captain for the Small Port", "The
            captain sets out with the boat: 1 dock", "The captain is back at the Small Port". Drawn:
            the port's console (as the depot's) and the captain in navy with a white peaked cap in
            the wheelhouse while out. `CaptainTests` (round a pier; no way onto land; berths;
            hire from shift 3; the crate sailed to the dock over water only at 60 % and the boat
            back in the bay, the letter riding home; Payday), `CaptainFlowTests` (the console's
            choices, line, cards and hint, key 7, the boat followed here, the feed); in the smoke
            E at the port's console puts it up in shift 2: no dock open yet, the captain "from
            shift 3" (`small-port-console`). The main smoke run ends in shift 2, so the sailing
            itself is proved by the tests
- [x] Oil Pump for vehicle fuel
      - [x] **Oil seeps and the Oil Pump** (doc 10 §2.3, doc 11 "Pump output 1 Oil Can per 45 s",
            doc 02 §3.9 "Oil · Seep (marsh) · Requires Pump", doc 12 M2; design choice 116). The
            map has **oil seeps** (`WorldMap.OilSeeps`): the test street's two from its file
            (`oilSeeps`, (28, 22) and (34, 23) on the grass between the houses and the quay), every
            Small Island three from a stream of its own (`WorldGenerator.SeepCount`) on open grass
            14 tiles or more from the Post Office and 10 from one another; nothing of the scenery
            stands within reach of one (grown first, cleared after, so nothing else moved). The
            **Oil Pump** (`oil_pump`, 1 tile, 400 HP, 6 Iron Ingots and 2 Glass, behind
            **Blueprint: Oil** — `bp_oil`, $6.00 from shift 3 at the counter; on the chest's key, as
            the bar holds ten) stands on a seep only (`NotOnSeep`: "An Oil Pump stands on an oil
            seep"). In Prep and Delivery it fills an Oil Can into its tank (`pump_tank`, 2 × 2: ten
            cans) every 45 s of work (`cycleSeconds`), stopping while the tank is full; no power is
            drawn. E opens the tank as a chest's ("Open Oil Pump [E]"). Its forecourt: a fuelled
            vehicle held still within 3.2 m (plus a truck's half length) whose tank takes a whole
            can is filled from the pump's tank, a can a second (`VehicleFuelEvent`; a becalmed
            boat's skipper sails again). Drawn: a pumpjack — a concrete pad, an orange A-frame, the
            gearbox with its crank discs, the wellhead in a stain of oil, a red can tank, and a
            walking beam with its horse head and counterweight that nods while the run works
            (`ConstructsView.PumpsNodding`); a seep is a black glossy pool with bubbles in a ring of
            stained ground, labelled "Oil seep", and a black drop on the map screen. `PumpTests`
            (content; seeps on the street and on thirty islands, none nav-blocked; the blueprint,
            on a seep only; a can in 45 s, the tank full at ten, cans taken out; nothing at Payday;
            a truck beside it filled), content, shop and key pins; in the smoke the host, given
            Blueprint: Oil in shift 2's Prep, is refused off the seep, builds a pump on it, and it
            nods and works, "Open Oil Pump [E]" (`oil-pump`)
- [x] Map screen with layers, filter chips, pings, route editor (all four below)
      - [x] **Pings** (doc 09 §2.6 "Ping: click to place a world ping visible to teammates (compass
            icon + 3D marker, 10 s). Hold for ping wheel", §4 middle mouse / RS click; design choice
            117). `Ping` request (protocol `m2.10`): a point of the map and one of five kinds; the
            server checks it (on the map, a kind it knows, two a second a carrier) and tells
            everyone a `PingEvent` with the tick it is gone by (10 s). `PingController`: a tap of the
            middle button pings "Look here" where the crosshair meets the ground (within 120 m;
            looking up, none); held 0.25 s the **ping wheel** comes up round the crosshair and the
            mouse moves its pointer instead of the view — up "Deliver here", right "Build here", down
            "Need materials", left "Danger", the middle "Look here" — and letting go pings the word
            where the crosshair was when the button went down. The client keeps three pings a
            carrier (a fourth replaces the oldest) and drops them at their tick. Drawn: a beam of
            light in the ping's colour from a ring on the ground, a turning diamond and "Ada ·
            Danger" over it, readable from anywhere, fading in its last two seconds (`PingsView`);
            on the map a ring and the same words; the feed "Ada pinged: Danger · 24 m away" (red
            for danger), "You pinged: …". `PingTests`, `PingFlowTests` (a tap and a held wheel
            through the real controller, the other carrier's world and feed, gone after 10 s, none
            at the sky). The compass strip of doc 09 §2.2 does not exist yet; the pings stand in the
            world and on the map
      - [x] **Layers** (doc 09 §2.6 "resources, constructs (belts as coloured lines with flow arrows),
            teammates, vehicles, NPC routes, enemies within any Alarm Post radius"; design choice
            121). `MapLayers` (Client, no Godot) works them out while the map is up and `MapPanel`
            draws them over the tiles: every belt and pipe tile as a line the way it carries with a
            flow arrow (amber, a Fast Belt blue, ramps and elevated pale yellow, pipes thin grey), the
            other constructs as their tiles in a colour by kind (walls, storage, sorters, inserters,
            the pump, depots and ports), the vehicles as little bodies pointing their way, the hired
            crews' routes dashed — a driver's by the streets round its stops' houses and home, a
            captain's over the water to its docks' berths (planned by `RoutePlan`, `WaterNav`, as the
            consoles do, once a second) — with numbered stops, the one under way white, and the
            raiders an Alarm Post reveals as red dots
      - [x] **Filter chips** (doc 09 §2.6 "Mail, Routes, Resources"): pills under the map's line,
            lit while on, on Z, X and V while the map is up (doc 09 has no keys for them; V is the
            Lobby's world key only with the map down). **Mail** rings the houses with mail waiting
            and how much: the server's new `MailWaitingEvent` (protocol `m2.11`; when it changes, at
            most once a second, and to a joiner) counts the Intake, the map's chests and the Chests
            and Depots built, and the client adds its own pockets. **Routes** shows the crews'
            routes, **Resources** the nodes with something left in their resource's colour. Mail
            and Routes start on, Resources off. `MapLayersFlowTests` (2: the debug world's belts,
            machines, vehicles, both crews' routes, the server's counts; the test street's nodes, a
            parcel in the map chest told within a second, a letter in the pockets), `CodecTests`;
            the debug-world smoke opens the map and checks every belt, machine, vehicle and both
            routes drawn and the chips by their keys through the real controller (`map-layers`
            shot); the main smoke's map step checks the Resources chip and the Mail ring
      - [x] **The route editor** (doc 09 §2.6 "when a Vehicle Depot console is open: click stops in
            order; district macro by clicking the district label; drag to reorder; estimated
            round-trip time shown from the routing graph"; design choice 122). With a depot's or a
            port's console up, M puts the map over it as the editor ("ROUTE · Vehicle Depot   [M]
            back to the console"; the console's cards wait under it, the pointer stays free, the
            map takes the clicks): a click on a house (its building or mailbox) makes it the next
            stop after the last there is, a click on a district's name (drawn over the middle of its
            houses) the district; a stop's numbered marker dragged onto another's moves it there,
            the ones between moving over; a right click on a marker takes the stop off and the later
            ones move up. A depot's driver calls at houses only, a port's captain at docks only. The
            line under the title is the console's round trip at the crew's pace, and the edited
            route is drawn as it stands, every frame. Each change is the console's own `SetFilter`
            requests slot by slot (`RouteEditor`, Client), so the server judges it as a key at the
            console; Esc takes the editor and the console down together (`GameScreen.Cancel`).
            The consoles' hints say "[M] route on the map". `RouteEditorFlowTests` (houses and a
            district clicked in, a drag, a removal, the server's filters, the planned route, the
            port's docks only); in the debug-world smoke the depot's console goes up, M, a click on
            1 Anvil Close's mailbox on the map makes it stop 2, its marker dragged onto stop 1 swaps
            them, a right click takes it off — each on the server — and Esc through the real
            controller takes both down (`route-editor` shot)

### 5. M3 — Combat (doc 12 M3, doc 05), in the docs' order

Picking up after what was already on main from M1 (raids and their waves, the Wooden and Stone
Walls, the Spike Strip, the Gate, the Alarm Post, the Repair Hammer, the mail cannon, death bags).
Doc 12's M3 "Defenses" line is the first with a piece still missing.

- [ ] Defenses: walls, gate, spikes, turret (auto and operated), alarm post, repair hammer
      - [x] Walls, the Gate, spikes, the Alarm Post, the hammer (M1, above)
      - [x] **The Turret, automatic** (doc 05 §4 "1 tile, 400 HP, Blueprint; 6 Iron Ingot, 2
            Stone; 12 dmg / 0.5 s, 18 m range; always fires at full rate; targets nearest enemy";
            design choice 123). `turret` (behaviour `turret`, params `shotDamage` 12, `shotSeconds`
            0.5, `range` 18; the validator wants all three on a turret and none elsewhere) behind
            **Blueprint: Turret** (`bp_turret`, $6.00 from shift 3 at the counter), on the Alarm
            Post's key (the towers, `group: "tower"`). `SimWorld.StepTurrets`, after the raiders
            move: a turret whose gun is ready shoots the nearest raider within 18 m that is not
            fleeing, and hits; nobody's kill (its loot lies for anybody); never a carrier.
            `TurretFiredEvent` (protocol `m2.12`) tells the clients where it aimed. Drawn
            (`ConstructsView.TurretBase`/`TurretHead`): a concrete pad, a steel ring with orange
            trim on bolted feet, a pedestal, and a gun head that turns (6 rad/s) on the raider it
            last shot or the nearest in range — a sloped housing, twin barrels in shrouds, an ammo
            drum, a sight — with a muzzle flash and a tracer to the raider for a tenth of a second;
            red on the map screen. `TurretTests` (content; no blueprint, no turret; the nearest
            first, a shot every 15 ticks, five shots fell a raider and the gun goes on to the next;
            nothing beyond 18 m, at a fleeing raider or at the carrier beside it), content, shop,
            key and codec pins. **The debug world's turret range**: a turret at (54, 10) and a
            practice raider stood up at (57, 2), 17 m off, 4 s after the last one fell (`debug.targets`,
            raids or not; the toggle test now allows only it), an eleventh station "Turret range"
            on **Home** with the proof `{"fired": [54, 10]}`; `DebugWorldTests` shoots two down in
            20 s with nothing of the showcase hurt; the debug-world smoke checks the shots reach the
            client and the view takes them up
      - [ ] The turret operated by a carrier (2× the rate, manual aim)

Next in this order: the operated turret, then doc 05's construct damage (ruins and rebuilding,
item spill), then the enemy traits.

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
   Not yet: Raid (M2); Generating came with choice 93. Draft came with the perks
   (choice 66); before them Payday went straight to the next Prep. Mail spawn budget per shift is still
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
      "the body drops a death bag" after the 120 s disconnect grace came with that grace
      (choice 65): only a leave in the Lobby is still a plain leave.
    - *Not yet*: the `second_wind` perk (no perks yet), the "+ overflow" of doc 03 (the pockets
      are 24 cells, the bag has 32), vehicles dropping a bag.
    - *Wire*: `BagDropped` (container, owner, place, despawn tick) and `BagGone` (returned or
      emptied, how much mail went back); the Welcome lists the bags lying about. The client
      takes a bag off its list only after the feed has heard `BagGone`, so the line can still
      say whose it was. The countdown on the HUD is the despawn tick against the server clock.

62. **Nits 14 and 15: which world labels are drawn.** The docs have no rule for world labels
    (doc 09 only lists the HUD). Chosen: one pass a frame over all `SizedLabel3D`s
    (`LabelDeclutterer`): each has a box on the screen (its capped height, its text's width, 6 px
    of air), a distance fade and a rank; by rank, then nearest first, a label is drawn unless it
    has faded out, lies on the HUD's keep-clear boxes or on a label already drawn. The distances
    follow what each label is for: the address on a mailbox is for the last steps to it (the
    prompt names it too), the number over a house is what is read from down the street, and
    beyond half the street nothing is worth reading. The ring in the middle is not kept clear:
    the address of the mailbox one looks at stands right over it. The top of the HUD got a plate
    (as the trackers have) instead of more outline: the sky behind it goes from white sun to
    night, and outlined text alone lost against both the bar and the world.

63. **Nit 17: who the raiders go for, and where they stand otherwise.** Doc 05 §2 gives every
    enemy an `aggroRange` and says "nearest valid target within aggroRange, re-evaluated every
    0.5 s"; the code had ignored the range, which is what made the whole wave walk to the pad.
    Doc 05 §5 has the raid's real objective — the Post Office with HP, sieged — but that is an
    M2 line (PO HP, repair, the run lost with the PO); until then the docs do not say what a
    raider with no target does. Chosen:
    - *The line.* They walk to a line across the street, `raidStandoffMetres` (19) from the
      pad on the gate's side, and hold it in ranks, facing the Post Office. 19 m keeps the pad
      (and the Intake, 3.6 m from it) outside every aggro range in the content (15 m at most),
      and leaves the first mailboxes (x 24) on the carriers' side of the line: a delivery run
      down the street is what crosses it. When the PO gets HP the same line is where the siege
      starts from.
    - *The leash* is measured from where the raider last had its target in range, not from
      where it is now: a raider on its way round a house walks away from its target for a
      while (`RaidersFindTheirWayAroundAHouse` caught that) and must not lose it there.
    - *Provoked.* Without it the line would be a shooting gallery from 16 m: a raider that is
      hit goes for whoever hit it for `enemyProvokedSeconds` (6), from any distance.
    - *Protection* rather than a sanctuary: a no-raider zone round the pad was rejected because
      a carrier could fight from inside it for free. 5 s of not being hurt or targeted, gone
      with their own first blow, is the usual rule and has no such corner. It is on the
      snapshot (one flag bit per player) so that the HUD's banner is the server's word.
    - *Four at a time.* `maxAttackersPerCarrier` (4): the nearest four close in, the others
      stand `attackerWaitMetres` (4) off. It keeps the view clear and makes a wave of fifteen
      survivable for one carrier with a hammer, which the playtest said it was not. Rejected:
      pushing bodies apart harder (giants then bulldoze the others onto the carrier).

64. **Nit 18: `--playtest-package`.** The nit offered two roads (a flag of its own, or making
    `--playtest-mailbox` work with `--raid`); both are true now, and the flag of its own is the
    one to use, because the mailbox flag also moves the player and hands out letters, which the
    cannon cannot fire. Chosen: *four* packages, not one — one shot every two seconds is over
    too soon to judge the cannon by, and four still leave most of the pockets free (three lie
    in the hotbar, one in the inventory). *Topped up*, not handed out: at every shift's start
    and on standing up (the ones carried lie in the bag where the carrier fell, and the point of
    the flag is not to have to walk for ammunition), never above four. They are real mail with
    an address (the mailbox of the player's seat if it takes packages), so delivering one pays
    as any package does — as the mailbox flag's letters do. Nothing in the Lobby or Prep: there
    is no mail outside a shift (doc 01 §1).

65. **The 120 s disconnect grace.** Doc 06 §6 has the rule in four sentences (frozen and
    invulnerable for 120 s, inventory on the body, the same account id resumes, otherwise a
    death bag; the player count for scaling drops at the next Delivery start). What they leave
    open, and what was chosen:
    - *Which disconnects.* Every one outside the Lobby, a clean quit too: the transport does not
      tell a quit from a pulled cable, and somebody who quit by mistake wants the same two
      minutes. In the Lobby there is no run to come back to, so a leave is a leave. `--quick`
      servers follow the same rule.
    - *Away means*: stepped with an empty input (they come to rest, they do not hang in the
      air), not hurt, not targeted (a raider that had them lets go), no loot picked up, the
      chest they had open closed, what they had asked for forgotten, not ready, their pause
      ended. One who was down still stands up at the pad on the respawn clock. The seat is
      held: a full server stays full.
    - *Who is counted.* Present players only for "everyone ready", the Prep hint, the quota
      preview and the head count taken at Delivery start. Within a shift nothing is recounted:
      the raid's budget now reads that shift-start count (it read the live list before, which
      the doc's last sentence forbids).
    - *Playtest flags* of one who is away rest (no stopped clock for an empty chair) and count
      again on their return; the Hello of the return applies them afresh.
    - *The host* is whoever has been present longest: the role passes on when the host goes
      away and is not taken back on return. With nobody present it stays where it was.
    - *Nobody present*: doc 01 §7's empty-server rule wins over the 120 s — after
      `emptyServerGraceSeconds` (60) the run is given up, the away players are removed without
      bags and the server is back in the Lobby. A dedicated server must not sit in a dead run.
    - *Pause*: only a server with exactly one seat taken pauses, so not while a seat is held.
    - *Not back in time*: the pockets become a death bag where they stood (nobody's — "an
      unclaimed bag" — with the usual 30 s; empty pockets leave none), then they are removed
      as any leaver. From then on the same account is a stranger and joins as one.
    - *Coming back*: the server looks the Hello's account up among the away players only, in
      any phase, before the join-in-progress rule; a stranger mid-Delivery is still turned
      away with `RunInProgress`, and so is an account that is present (two windows on one
      machine share a profile). In Payday or Results the returner is sent the summary or the
      results they missed. Input sequence numbers start afresh.
    - *The account id* is the FNV-64 of the profile's id (never 0; 0 means "none" and is never
      matched) until a platform gives a real one (doc 06 §2, Steam, is M3). It is what the
      client says it is: on a listen server among friends that is enough, and it is not
      authentication. A new profile is now written to disk at once, so that the id of the
      first session is the id of the second.
    - *What a mid-shift Welcome must carry.* Joining had only ever happened in the Lobby and in
      Prep; a returner can land in a raid. The Welcome gained the raid's start tick, their own
      respawn and protection ticks and the loot lying about; and since the Welcome travels on
      the bulk channel and events on the reliable one, the client now keeps events that
      overtake the Welcome and plays them after it (`WhatOvertakesTheWelcomeIsNotLost`).
    - *On screen*: "(away)" behind the name over their head; feed lines for away, back, and
      "did not come back" (against "left" for a Lobby leave); their bag reads "Bo's bag lies
      where they stood". No countdown over the head: the feed line says the two minutes once,
      and the name tag stays a name.
    - *Not yet*: a "reconnecting…" screen on the returner's side (they see the normal join),
      vehicles (doc 06: a driver who drops leaves the seat), authentication of the account.

66. **Perks and the Draft.** Doc 01 §2.5 and §4, doc 08 §2.9, doc 09 §2.10 and doc 10 §4 give
    the rules and the list. What they leave open, and what was chosen:
    - *Stats.* A closed `Stat` enum of 15 and a `StatSheet` (base × every multiplier + every
      addend); a perk is a list of modifiers on them and nothing else. A team stat can only be
      changed by a team perk. No rule flag and no recipe exists yet, so a perk naming either
      fails validation; the eleven perks in `content/perks` are those of doc 10's list that
      need no building, vehicle or shop.
    - *Rounding and floors.* What paper mail pays, what a misdelivery costs and the complaint
      it raises are rounded to whole numbers, a half away from zero, and never fall below 0;
      nor does the quota.
    - *Grace Period*: mail one shift late pays in full but still counts as late (the
      complaint stands).
    - *Union Rep* lowers the quota that is shown and that binds; the mail spawn budget is still
      worked out from the unmodified quota, so the perk makes the shift easier, not emptier.
    - *Prerequisites.* `shiftMin` is checked against the shift that comes after the Draft.
      `rankMin` above 1 is never met: the server is not told anybody's Postal Rank, so
      everyone counts as rank 1.
    - *Exclusions.* A team perk is checked against the team's perks and everybody's personal
      ones, a personal perk against the team's and the picker's own.
    - *Dealing.* Per card the rarity is rolled first (over the rarities that still have a
      candidate), then one perk of it with equal chance. A candidate is a perk somebody
      present could take and that does not conflict with a card already on the table. "Rare
      +5 per shift" is read as +5 × (coming shift − 2): nothing extra in the first Draft. With
      no candidate at all there is no Draft. The perks RNG stream starts afresh with every run.
    - *When.* After every Payday the run survives, except the last: doc 01's "Draft → Victory"
      would deal perks nobody can use. `Run.Shift` stays the shift just paid, and the HUD's
      phase line says so ("Shift 1 / 5   DRAFT 00:30"). `--quick` servers and
      `SimSettings.Draft = false` go from Payday straight to the next shift.
    - *Ending.* As soon as everybody present has chosen or has no open card; otherwise at 30 s
      the clock gives each of them the first card still open to them (the feed says "was
      given"). Who is away gets nothing; who comes back mid-Draft finds the table as it
      stands, in the Welcome. Whatever fight was left is cleared when the Draft begins.
    - *Taking a card*: keys 1–3, no mouse (the cursor is captured in play and the cards are
      three). While a card is open to you the keys are the cards' — the key of one that is
      not open does nothing — and once you have chosen they are the hotbar's again. You can
      walk about during the Draft.
    - *On the card* instead of doc 09's "teammate avatars": "Taken by Bo", "Taken for the team
      by you". There are no avatars.
    - *Where.* The panel stands in the middle of the view and draws over the crosshair ring;
      the Payday summary is gone by then. The "Perks" tracker plate took slot 4 of the right
      column (raid 5, bag 6).
    - *Not yet*: rerolls and the cap of five personal perks (doc 12: M4), the other perks of
      the list, a perks page outside the Draft (the tracker plate only counts them).

67. **Playtest issue (a): a name on the HUD is drawn under it.** Choice 62 leaves out any
    world label that would lie on the HUD. For a carrier's name that is the wrong answer in
    the one case where it happens — somebody standing right before you —, so name tags
    (`SizedLabel3D.Slides`) are moved down on the screen to under every HUD box in their way
    and take their room there (rank and distance decide as before who is drawn where two
    meet). The label itself stays over the head; only its text is drawn lower (`Label3D.Offset`).
    More than 240 px (`LabelDeclutter.MaxSlide`) and it is left out: its owner is in your face.
    Addresses, signs and house numbers are still left out rather than moved.

68. **Playtest issue (c): the mail cannon's fuse.** Doc 05 gives the cannon a blast radius and
    says nothing of what sets a package off. Touching a raider or a wall did; now it also goes
    off beside a raider: at the point of its flight nearest to one, if that is within
    `cannonFuseRadius` (1.2 m) of the raider's body. Not "on coming within 1.2 m" — that would
    go off in front of a pack and reach fewer of them, and at the muzzle if a raider stands
    close. With the 2 m blast, anything aimed within about 1.8 m of a raider hurts it; a
    package aimed 3.5 m wide flies on. Walls and range are as before.

69. **Playtest issue (d): what ends the protection, and the yard.** Choice 63 ended it with the
    carrier's first blow, landed or not. Now:
    - only a blow that takes health from a raider ends it (hammer or blast; the bags of loot
      are not raiders);
    - who was put back on their feet by the respawn timer stays protected past the 5 s for as
      long as they stay within `respawnYardMetres` (8 m) of the spawn pad **and** there is a
      raider anywhere. Leaving the yard after the 5 s ends it for good (coming back does not
      bring it back), as does the last raider's fall. Leaving it within the 5 s keeps the
      rest of the 5 s;
    - it cannot be used to fight from: the first blow that lands ends it, and a blow brings
      the raider over from anywhere (choice 63). It can be used to sit a raid out in the yard
      once fallen; the raid then waits on its line, and the shift clock runs.
    Nothing new on the wire: the snapshot's `protected` flag and `PlayerRespawned.protectedUntil`
    say it all, and the HUD reads "in the yard" from protected-with-no-seconds-left.

70. **Playtest issue (e): the wave budget.** Doc 05 §3.2's table (120 / 220 / 360 / 560) is
    written for a game with walls, turrets, a shop and perks to answer it with. None of the
    first three exists yet, and with hammer and cannon alone shift 3 was the wall the playtest
    hit. `baseBudget` is 120 / 150 / 185 / 225 until they do; the formula around it (players,
    complaint, the 0.8 for one or two) is the doc's. To be raised again when defences land —
    `RaidFightTests` is the measure: its bots must still get through.

71. **The shop.** Doc 01 §2.4 / §3.4, doc 08 §2.10, doc 09 §2.8 and doc 10 §3 give the list, the
    prices and "a grid of cards". What they leave open, and what was chosen:
    - *Where and when.* A counter of the Post Office (`postOffice.shop` in the map), reached
      like the Intake (server range 3 m). It serves in Prep and at Payday (doc 01: "Prep" and
      "Payday (+ shop)"); in the Lobby, Delivery, the Draft and Results a request is `WrongPhase`.
    - *A purchase* is one catalog entry: its stack, whole or not at all (`DoesNotFit` costs
      nothing). Consumables and tools go to the hotbar first, everything else to the inventory
      first. The buyer carries it; the money is the team's, and anybody may spend it.
    - *Selling* pays the item's `sellPrice` per unit into the wallet. It is team money but not
      shift earnings: the quota is met with mail. Mail cannot be sold. The validator refuses a
      catalog entry that costs less per unit than it sells back for.
    - *Specials.* Two a shift, rolled from the shop's own RNG stream when the shift begins, so
      Prep and that shift's Payday show the same two. A fixed entry on special is 20 % off
      (`shopSpecialDiscount`); a `rotating` entry (the First-Aid Kit) is only on the counter
      when it is a special, at its list price. An entry the team already owns, or one that is
      not yet unlocked (`fromShift`), is not put on special.
    - *Once per run.* Blueprint entries are bought once and are the team's until the run ends
      (`AlreadyOwned` after that); a late joiner gets the list in the Welcome. No blueprint is
      in the real catalog yet — there is nothing to build — but the rule is tested. Entry kinds
      `vehicle`, `hire` and `service` fail validation until the sim can honour them.
    - *Use.* Doc 09 §4 has no key for a consumable and both mouse buttons are weapons: Q (joypad
      B) uses the stack in the hand. Bandage: 50 HP at once; Berries: 25 HP over 5 s; refused at
      full health ("You are at full health" in the feed) and while down; works anywhere, in
      any phase.
    - *On screen.* The cards are the right side of the inventory screen, where an open
      container would be; the shop being open is the client's own state (the server only
      checks reach and phase per request), so opening it needs no round trip. Opening the shop
      shuts an open container and the other way round. The cards come down when the player is
      more than reach + 0.4 m away, goes down, or the phase closes the counter. While they are
      up the number keys are the cards' (1–9), not the hotbar's; a click on a card buys as
      well. A click on an own stack sells all of it, Ctrl+click one. More than six cards are
      laid out three abreast. At Payday the summary panel steps aside while the cards are up.
    - *The wallet is not predicted.* A card whose purchase is on its way is greyed ("On its
      way"); the server puts the global block into the very next snapshot whenever the wallet
      changed (it otherwise rides every sixth), so the cards follow within a tick or two.
    - *A closed counter still answers*: the prompt says when it serves, and a press on it is
      spent — it does not open the cards a moment later when the phase changes.
    - *Not yet*: the bike and the other vehicles, hires, services, the Tailor.

72. **Resource nodes and harvesting.** Doc 02 §3.9 has the generator scatter nodes by biome;
    the map is still the static test street (choice 3). What was chosen until it is not:
    - *The nodes are the scenery.* `content/nodes` tells which of the map's props give
      something, by kind and variant: every Tree wood; a Bush of variant 0 or 1 Fiber, of
      variant 2 Berries; a Boulder of variant 0 or 1 Stone, of variant 2 Iron Ore. No new
      roll, so the map hash and the look of the street are as before — and the bushes at the
      house corners can be picked. A node's id on the wire is its index in `map.Props`. Sand
      and the Shovel wait for a biome that has sand.
    - *A hit.* One request, one hit: the node loses a hit and its yield is in the pockets, or
      nothing happens (`Depleted`, `NeedsTool`, `DoesNotFit`, `OutOfRange` at more than reach
      + the prop's radius, `WrongPhase` outside Prep and Delivery, `TooFast` under a swing
      less 0.1 s). The slot of the hand rides the request — the server's idea of the held slot
      lags the scroll wheel. Berries go to the hotbar first, materials to the inventory first.
    - *Tools.* A node is `none` (hands), `optional` (hands give `handYield` of it: a tree half)
      or `required`. The tool is whatever item `harvests` the node's resource: Axe wood,
      Pickaxe stone and ore. Both are bought at the counter ($0.80, $1.00; doc 10's prices);
      the kits that would start with one are not in yet. No durability, and as a weapon (doc
      10: "melee 15") they wait for the weapon slots.
    - *Regrowth.* Bushes are full again when a shift begins (doc 02 §3.9: "Regrows in 1
      shift"); trees and rock stay as they were left until the run ends. A node worked out
      stays solid — stump, twig, rubble — so no path opens mid-run and no navigation changes.
    - *The key.* Doc 09 §2.4 names "harvest" among the interact prompts, so it is E, held: every
      0.6 s of holding is a swing, the hit lands at its end. The bar fills on while the last
      answer is on its way, but the next hit is not asked for before it is here. Nothing is
      predicted: the pockets and the node follow the server's answer.
    - *What is drawn.* The hammer's swing stands in for the tool (there are no tool models
      yet); the others see a hammer swing. Nodes are drawn by `NodesView` in one batch of their
      own, rebuilt when the set of worked-out nodes changes (twice a shift at most, plus once
      per node felled), not per hit.

73. **At Payday the shop comes to the carrier; the shop key is C** (playtest 25fbca5, issue a).
    Doc 01's phase table gives Payday 20 s and "Shop opens for a fixed window"; doc 09 §2.8
    puts the shop "at PO counter during Prep and Payday". Both cannot hold for a carrier whose
    shift ended at a mailbox 70 m away, so the phase table wins: at Payday `BuyOp` and `SellOp`
    ask for no reach (`ShopRules.ServesAnywhere`), in Prep the counter is a counter. The
    window was not widened: `paydaySeconds` is doc 11's 20, and a longer Payday is a longer
    wait for everybody who does not shop. The key: doc 09 §4 has none for the shop and gives B
    to Build mode, so it is **C** (the counter; D-pad up on a pad), an action of its own —
    `shop` — rather than a second meaning of E, which at Payday may well have a mailbox or the
    Intake under the crosshair. C toggles; E and Esc still close. The cards opened at Payday
    stay up while the carrier walks; they come down with the phase unless the carrier is at
    the counter when Prep begins.
74. **The counter's reach and aim are its own** (playtest 25fbca5, issue b). `shopRange` 4 m
    for the client, `serverShopRange` 4.5 m for the server (the same half metre of slack as
    `interactRange` 2.5 / `serverInteractRange` 3), measured as before from the map's `shop`
    point on the wall. Doc 09 §3.1's 2.5 m is the reach of a mailbox, a thing one walks up to;
    the counter is a place one stands before, and the farthest seat of the spawn pad is 3.5 m
    from it. The facing rule
    (`InteractionController.FacingCone`, 0.7 rad) applies to the Intake and to a serving shop
    counter only, and only when the view ray hits no interactable at all — so a chest, a bag,
    a mailbox or a node under the crosshair is never taken over by a counter beside it. A shut
    counter (Delivery, Lobby) answers "closed" to the crosshair only: nobody walking past the
    Post Office with mail in hand is told about the shop.
75. **Whose the mouse is; the Draft is a panel with cards to click** (playtest 25fbca5, issue
    c). Doc 09 §2.10 draws the Draft's cards and says nothing of how one is taken; the keys
    1–3 were the only way. A carrier fresh from the shop's clickable cards reaches for the
    pointer, so the Draft now frees it — only for who still has a card to take
    (`ClientWorld.ChoosingCard`); who has chosen, or was given a card by the clock, looks
    around again while the others think. Esc in the Draft sets the cards aside (they stay on
    screen, keys still work) rather than releasing the mouse a second way. Esc with nothing
    open still lets the mouse go — a solo host's pause, and in multiplayer the way to the
    desktop — but the HUD now says so, and the first click into the view only takes the mouse
    back. `MouseCapture` lives in `src/Client` without Godot so the rule has unit tests; the
    input node applies it every frame, which costs a property read.
76. **Panels are kept clear of the world's labels** (playtest 25fbca5, issue d). A panel's
    ground is translucent — the street stays in sight behind the cards — and a bright name
    tag, drawn without depth test to be seen through walls, shows through it as if it lay on
    top of the cards' text. Rather than making the panels opaque, the declutterer's keep-clear
    list (HUD plates, prompt, hint) takes the visible panels too; a tag there slides under the
    panel's edge or is left out, like under any plate.
77. **Building by hand.** Doc 04 §1 gives the grid (2 m tiles, one building a tile), the
    phases, the refund and "not on streets"; doc 08 §2.6 the building's fields. What was
    chosen where they stop:
    - *What a tile takes.* Open ground only (`BuildRules.CheckTile`): never a Street tile
      (`OnStreet` — the mail and the raid both need it, and the test street has one); not a
      tile the tile paths already keep off (a house, a tree, a fence: `Blocked`); not a tile
      with a fixture that has to stay in reach (a mailbox, the Intake, the counter, a map
      chest, the pad and its eight seats); not onto a body (`Occupied`: a carrier's or a
      raider's circle touching the tile). Range is to the middle of the tile, `buildRange` 8 m
      for the client and `serverBuildRange` 8.5 m against the carrier's last ticks.
    - *The shape.* A wall is not a filled tile: two arms of `params.radius` (0.2 m) from the
      middle to the tile's edges along the way it was turned, and one more towards a
      neighbouring wall that runs at it, so an L or a T is shut without a corner piece. A
      single wall is walked round at its ends, where the next tile begins. A
      chest is the map chest's body (0.45 m, 0.8 m high: a jump clears it). For the raiders'
      tile paths every built tile is closed, whichever way the wall is turned.
    - *The refund scales with what is left.* floor(count × phase ratio × hp / maxHp): a wall
      at half its HP gives 1 Log in Prep, none in Delivery. Without it a wall about to fall
      would be pulled for full price under the raiders' hands. The refund has to fit the
      pockets (`DoesNotFit`, nothing happens), and a chest has to be empty (`NotEmpty`).
      Anybody takes down what anybody built: the base is the team's.
    - *Raiders and walls* (doc 05 §4 "structures take damage"). One rule: a raider walking at
      its target that is up against something built (within 0.3 m, ahead of it) stands and
      strikes it, its own damage every attackRate. Its path keeps off built tiles, so that
      only happens where there is no way round — then it takes the way there would be
      without the walls — or where its target stands right behind one. A blow aimed at a
      carrier with a wall on the line between them lands on the wall (chests are no cover).
      The hammer and the mail cannon are not stopped by walls yet. At 0 HP a construct is
      gone (ruins and rebuilding are doc 12's M3); a chest's contents lie in a bag that is
      nobody's, mail and all, with a death bag's time to live.
    - *From shift to shift.* What is built stands until the run is over; then it is cleared,
      chests with what was in them (a new run starts from the kit).
    - *Recipes.* `craftSeconds` has to be 0 and `produces` a building (doc 08 §2.7: no item
      crafting). `unlockShift` and `blueprint` are read and enforced (`RecipeLocked`,
      `NeedsBlueprint`); the two M1 recipes need neither.

78. **Build mode's keys and the ghost** (doc 09 §2.7 and §4). Doc 09 gives B, R, Q and "hold
    on a construct for 0.5 s" but no buttons for placing and taking down; the attack button
    builds and the alt button held takes down, since in Build mode the hammer and the cannon
    are put away (no swing, no shot). The number keys are the recipes while the mode is up,
    as they are the shop's cards at the counter. Placing is one click per construct: drag
    lines (doc 12's M2 "drag lines") wait for belts. The ghost follows the view ray down to
    the ground and is shown up to 14 m away even where the hands do not reach (8 m), red
    with "Too far away", so the player sees where they are pointing; looking up shows none.
    What is built in front of the ghost's tile is looked at instead (taking down, pipette).
    A built chest is opened by the interact key in or out of Build mode. The mode is not a
    panel: the view keeps the mouse and the carrier walks.

79. **Fortify, the first blueprint.** Doc 10 §3 has bp_defense unlock "Stone wall, spikes"
    from shift 2 at 300 ¢; doc 05 §4 gives the two buildings' HP, costs and what spikes do.
    Where they stop: a blueprint is not an item (it is the team's the moment it is bought,
    held in the shop's once-per-run set the server already had); the Spike Strip covers its
    whole tile, hurts a raider whose middle stands on the tile (its 15 HP a second in 1/30
    steps, the kill nobody's but whoever hit it last), is not struck by raiders (it is not in
    their way) and so never wears; it does not hurt carriers ("Turrets and spike strips do not
    damage players"); nothing else is built on its tile. The Stone Wall's "Wall Breaker damage
    reduced 50 %" waits for the Wall Breaker (no such raider yet). The smoke hands the host
    the two Iron Ingots for the strip: the run has no money for the shop's ten after the
    blueprint, and no refining (doc 03: there is none).

80. **Sand, the Shovel, and what the hand holds.** Doc 02 §3.2 puts sand on the Beach biome
    ("land tiles within 3 tiles of water"); the test street is still static (choice 3) and its
    water lies outside the map (`IslandView`), so the map names its beach itself: `beaches` in
    the map JSON, a strip in the south-west corner on the side of the harbour. It is laid
    before the streets, lots and the Post Office, so they stay what they are. The scenery key
    treats a beach tile as grass (and the map hash does not), so the street's trees and bushes
    stand where they stood; the grid's trees, bushes and boulders keep off the sand, which
    takes 6 of the 92 nodes away. Sand piles come from a stream of their own seeded by the map
    hash: 45 % of 4 m cells on the beach, kept a walker's width from the map's edge. A pile is
    solid but low (0.45 m × scale: a jump clears it), like a boulder for paths. Doc 10 gives
    the Shovel's price (90 ¢) and grid; its sell price is 22 ¢ (the other tools' 25 ¢ scaled).
    The shop has eleven cards now; the eleventh takes the key right of 0, minus. *The tool in
    the hand*: doc 10 §1.2 gives the tools melee damage, which waits for the weapon slots;
    until then a tool in the held slot is only drawn — in the hammer's place, swung by the
    same swing — so the hand shows what the carrier will harvest with. Mail and materials in
    the held slot are not drawn as tools (a letter is still the letter). The others learn the
    tool from one byte per player in the snapshot (item index + 1, 0 = none) rather than from
    the hotbar contents, which only their owner is sent.

81. **The Gate and the Alarm Post.** Doc 05 §4: a gate "opens for players and friendly
    vehicles; enemies treat as wall", an alarm post "extends raid warning by 15 s and marks
    enemies within 40 m on the map"; doc 11 §10: "+15 s per Alarm Post in range, max 45 s".
    Where they stop: *in range* is in range of the raiders' way in (the east end of the street,
    where they come from, `RaidRules.Gate`): a post is a lookout on the road the raid takes,
    and one by the Post Office rings nothing early — the build prompt says which before it is
    built. The 40 m of the doc's marking radius is the range for both. The cap is on the whole
    warning (with Early Warning's 20 s too), and never below what the perks alone give. The
    warning is the shift's when Delivery begins, moved by every post built or lost until the
    announcement; after that the clock stands. *Marks on the map*: there is no map screen yet
    (doc 09 §2.6 comes with M2's UI), so a marked raider wears a red mark over its head that
    shows through walls, and the raid tracker counts them. The gate is not drawn open by the
    sim: it opens on screen for a carrier near it; raiders never pass, so there is nothing to
    hold open. Friendly vehicles wait for the bike.
82. **The bike.** Doc 04 §3.1 gives 2x8, 8 / 5 m/s, one seat, "Land kit or shop 120 ¢", "no
    fuel; parked only, not carried"; doc 06 §3 "driving client predicts; server simulates and
    corrects"; doc 07 §5 a "2.5D arcade model (heading, speed, grip …)". Where they stop: the
    *road* is paved ground — the street's and the Post Office yard's tiles —, everything else
    is off road. The arcade model is a heading that follows the steer at a turn rate and a
    speed that comes up at an acceleration, with braking and rolling out; no grip, no slope
    (the map is flat). Riding is part of the rider's own movement state, so the prediction
    path is the players' own with no second one to keep in step; the server's +15 % input
    clamp holds for it as for walking. Getting on is a request (it moves the carrier onto the
    bike, the server's to say); getting off is the **jump key**, because a bike does not
    jump and E is the mailbox's and the chest's — so it is predicted like any step. Who gets
    off stands where the bike was; the bike is not solid (carriers and raiders pass it, as
    they pass a bag), so nobody is pushed off it. The pockets' weight does not slow a bike
    (it carries the load) and perks of walking speed do not speed it (Bike Courier will).
    *One for each carrier present*: one seat each, so a team of four buys at most four. The
    Land kit has no kits to come with yet (no Lobby choice), so the shop is the only way in.
    A bike's 150 HP is kept but nothing hurts it yet (doc 05 §2 targets constructs; the
    raiders' priority lists name no vehicles); deliveries and swings from the saddle are
    allowed — the rack is the bike's point.
83. **The generated town.** Doc 02 describes the whole island (300 × 300 tiles of fixed-point
    noise, biomes, settlement sites); doc 12 M1 asks for "all stages". What is built: the
    town's stages — street graph and lots, buildings and destinations, addresses, districts,
    the Post Office, validation, worldHash, client-side regeneration and the SeedViewer — on
    a town-sized grid (76–88 × 60 tiles, ~160 × 120 m); the island around it stays the
    backdrop the test street already has (flat in the sim, as the sim's ground is flat
    everywhere so far). The street graph is a main street, two back streets and cross
    streets: a grid with removed links (a cross street may reach one side only), no bent
    segments. Lots are 4 × 4 only (no 4 × 6, no corner landmarks, no apartments or PO Box
    Bank yet — those destination types come with the later shifts' content). A street is cut
    into segments at its crossings, each a `StreetDef` of its own with the street's name, so
    that a district can take whole segments ("following whole streets where possible");
    numbers run along the whole street. District growth orders the segments by distance from
    the Post Office along the streets (Manhattan, across-block weighted), closes a district at
    8–12 houses, at most five; district N unlocks at shift N. "Towns like the coast" is a
    beach along the south shore. The test street stays the default map (the smoke's checks
    are made for its layout); `--world=island` plays the generated town, and the smoke plays
    it with a bot.

84. **The island round the town.** Doc 02 §3.1 asks for layered fixed-point OpenSimplex2; the
    noise is value noise on an integer lattice with a smoothstep in 16.16 fixed point, four
    octaves (`FixedNoise`) — integer-only as the doc wants, simpler than simplex, and pinned by
    a test. The sim walks the town flat as before (`WorldMap.HeightAt` stays 0): the town grid
    is levelled to 1.5 m over the sea ("buildings auto-level" taken for the whole town site),
    and the relief is the island's round it, which nobody walks yet. Settlement placement is
    searched for (since the batch after 941ca96): the one town's grid goes to the best-scoring
    all-land site near the middle (doc 02 §3.3's flatness and coast, doc 02 §1's "PO near
    centre"); its candidates are placements of the town's fixed-size grid rather than flat
    regions of minTownTiles, as the grid is the town. "Towns like the coast" stays a bay cut
    from the south shore to the town's beach (shorter the nearer the coast the site is), so the
    beach of design choice 83 is a real shore and deep water lies within 200 m. Rocky
    ground inside the town is where the levelled-out natural ground would have stood above 60 %
    of the top (slope cannot make it rocky there). Forest is the top 30 % of a noise mask over
    the grassland, so the doc's "≥ 25 % of land" always holds. Driftwood on the beach and the
    marsh (Large Island) are not made. The resource minimums are counts of nodes on the town
    grid (the island outside it is scenery only), set under the least of 50 seeds.

85. **The routing graph and the map.** The graph is built from the `StreetDef`s, not from the
    tiles: a street is a straight 2-tile strip, its centre line an edge, and a point within
    2.5 m of another street's end or crossing is the same node — enough for the grid towns there
    are. Every map has one (the test street's is a single edge), so the map works on both. The
    map is the doc's first layer set without zoom, filter chips, pings or the route editor
    (they need pings, depots and NPC drivers): it shows the route of the mail in the hand,
    which is what a carrier wants of it now. It is drawn under the HUD (the clock, the plates,
    the hotbar and the feed stay on top) and does not take the mouse, so it can be kept up
    while walking. Doc 09 §4 gives the pad's View button for the map; here that button is the
    go key, so the map is on the D-pad's down.

86. **Starting kits.** Doc 03 §4.4's table is followed where the items exist: there is no
    Backpack yet (every kit's), no Rowboat blueprint (Sea), no pipes or pump (Pipes); those
    come with the systems they belong to. The Depot is not built yet, so the team's items go
    into the map's chest by the Post Office (into the host's pockets on a map without one).
    The rank is the profile's at connect time, sent in the Hello and trusted like the rest of
    the local profile (doc 01 §5.2 "starting kits are per player from their own profile"); a
    rank-up during the session opens the next kit from the next connect. Kits are a
    `SimSettings.StartingKits` switch the game turns on and tests leave off, so the many tests
    that count pockets and bikes stay as they were. K picks the kit (doc 09 §4 has no key;
    the doc's Lobby screen with per-player dropdowns comes with a Lobby screen of its own).

87. **The world in the Lobby.** The doc's lobby picks the archetype and the seed before
    "Generating"; here the world is generated the moment the host picks it (a town takes
    ~60 ms), so the Lobby already stands in it and the Generating phase is not needed for
    it. The choices are the test street (the M0 map, kept as a practice street) and Small
    Island; Large Island and Land are later milestones, the Daily Seed needs the leaderboard
    backend (deferred by the doc). The seed string is doc 01 §5.3's without the stamps
    (there are none yet). A new seed for a map from a file keeps the map and changes the
    mail, the raids and the shop. Without a Lobby screen of its own (doc 09 §2.2's three
    columns) the choices are keys and HUD plates, as the kit's are. The host's choice is not
    rank-locked: both worlds are open from rank 1.
88. **`--playtest-wallet`.** A playtest flag, not a game rule, so it follows the others: any
    player may ask (the most asked counts, like `--shift`), and it goes when they go. It sets
    the wallet as the run starts rather than adding loot, so a re-ask cannot print money and
    the run's summary (earned, loot, spent) stays the game's own.

89. **CI.** Doc 07 §10 asks for the tests, the validator and a smoke that "boots, hosts, and
    connects a bot for 60 s"; `tools/smoke.sh` is that and more (two runs, an ENet guest, the
    island bot), so CI runs it rather than a second, smaller smoke. The workflow sits in the
    repo but is unproven until the repo is pushed to a host that runs it.
90. **Hot reload in place.** Doc 07 §6 says "reloads content and re-applies perks". The defs
    are immutable to the game, but everything holds them by reference (constructs, vehicles,
    stacks, perks taken, the shop's shelf), so the new values are copied into the loaded defs
    by reflection rather than a new ContentDb swapped in, and anything whose index or id would
    move needs a restart. What was sampled stays sampled (a Delivery's quota, a raid already
    scheduled, a construct's HP), as with a perk taken mid-shift. F6 does nothing in a release
    build.
91. **Rerolls are personal.** Doc 01 §2.5's offer is shared, but the reroll is "per player":
    rerolling the shared cards would take them from teammates who are still reading them, so a
    reroll deals the player a table of their own and leaves everybody else's alone. A team perk
    on a personal table is still the team's once. Rank 4's second reroll comes from the rank in
    the Hello (the server has it since the kits); the perk pool itself is not rank-locked yet.
92. **Spawn edges on a town grid.** Doc 02 §3.10 asks for mouths 120 m from the PO and 40 m
    from any house; the sim's walkable world is the town grid (~170 × 120 m, houses up to its
    edges), so no tile of it is that far. The mouths are on the grid's edge, at least 40 m from
    the pad, and raiders are taken to walk in from the island beyond. "Facing the newly unlocked
    area" is the side of the map the district's mailboxes lie towards from the pad. The east end
    of the first street is always the first way in, open from shift 1, so the test street and
    the doc's example ("Raid incoming from the east") keep it. Alarm Posts ring early near any
    way in open by the shift, not only the raid's: the raid's ways in are drawn as Delivery
    starts, and a post built in Prep should know whether it is any use. Water edges for
    Balloons come with Balloons.
93. **What Generating waits for.** The world is generated when the host picks it in the Lobby
    (choice 87: a town takes ~60 ms on the server and each client), so Generating does not
    generate it again; it is the run's set-up (doc 01: "spawns Post Office, players, initial
    resources") and the wait for every client to have that world built and drawn, which is what
    a guest who joined a moment after the host's V or N needs. Its bar therefore fills with the
    carriers whose screen is ready, not with generation stages. The Ready of Generating is the
    client's own, not a key; the feed says nothing of it (its five lines are kept for the kits
    handed out at the same moment).
94. **Belts: what the docs leave open.** (a) A belt ending at a mailbox puts in only mail the
    box takes as a match; anything else waits at the end and the line backs up behind it. Doc 04
    §3.2 has NPC drivers "never guess", and a belt that misdelivered every stray letter would
    make one mis-sort cost the whole line; a jam is visible and is what the Address Sorter is
    for. (b) A belt ending in the open holds its items at the end: there are no loose
    WorldItems for mail yet (doc 04 §2.1's 5-minute despawn back to the Intake comes with them);
    a chest is fed by an inserter only (doc 04 §2.6), so it holds too. (c) Mail coming onto a belt
    from the side goes into the lane on that side, straight on it keeps its lane (the usual
    side-loading rule; doc 04 is silent). (d) What lay on a belt that is taken down or broken
    goes back to the Intake at once, as mail never is lost (doc 03 §3.3). (e) Segments are named
    by their head tile instead of a server id, since every client compiles the same tiles. (f)
    The checksum hashes which mail is where and in what order, not positions: a client lags the
    server's tick by its latency, so positions differ by design; only the order and set of mail
    show a real desync. (g) A belt delivery is the team's, credited to nobody: it counts for the
    quota and the shift's deliveries, not for anybody's MVP line or Postal Rank deliveries.
95. **Where the Intake's outfeed opens.** Doc 10 §3 has the outfeed face "on the east side". On
    both maps the Intake is a counter against the hall's east wall, and east of it is where the
    carriers stand to use it, with the spawn pad's seats on those tiles (fixtures nothing is built
    on). So the outfeed opens at the counter's north end: a belt on the tile north of the Intake's,
    facing north, away from it. The map chest, which stood on that tile's north neighbour on both
    maps and boxed it in, moved one tile up and east (still in the Post Office's yard). The
    outfeed gives one piece a tick at most; the belt's head room is what limits it (8 a second on
    two lanes of a Mk1 belt), so no rate of its own is invented.
96. **Splitters and mergers.** Doc 04 §2.3 gives their ways and round-robin, not the rest. A
    splitter takes mail only from the belt behind it (a side belt would be a fourth way, and its
    left and right are outputs); a merger takes from anything facing into it but its front.
    Round-robin is per item handed on; a merger's turn passes over an input with nothing waiting,
    so an idle input never stalls the others. Both move mail at the Mk1 belt speed across their
    tile (params.speed 2). Like belts they lie low (0.7 m) and are walked over rather than walked
    round, which keeps them out of the raiders' tile paths until M3 decides what raiders do to
    belts.
97. **The Fast Belt and the keys of Build mode.** Doc 10 gives belt_mk2 (100 HP, a Plank and two
    Iron Ingots) and bp_fast_belts (500 ¢, shift 3, "Belt Mk2"), doc 11 its 4 m/s; doc 09 §2.7
    "scroll switches belt tier". With it Build mode would have had 12 recipes for 12 number keys
    and no key left for ramps or sorters, so a recipe may name a `group`: the recipes of a group
    share one key (the place of the group's first recipe), the wheel steps through them while
    Build mode is up (the hotbar is down then), and a key brings back the group's last choice.
    The belts are one group ("belt"); the content validator refuses more than 12 keys. The card
    says "[7] Fast Belt 2/2", the keys line "[wheel] belt tier". A Fast Belt is drawn as the
    Conveyor Belt with blue rail caps and two chevrons a half; a change of tier starts a segment.
98. **Riding and pushing belts.** Doc 04 §2.1 says a carrier on a belt is carried and one pushing a
    segment gives it +50 % "while held". The ride is at the belt's *rated* speed, push or not: a
    push is server state the rider's prediction would only learn late, and the ride is the fast
    travel, the push is for the mail. A carrier on a belt stands on its top (0.36 m), a step up
    onto it and down off it with no climb. "While held" is a 0.7 s push renewed every 0.4 s by a
    client holding E, so a dropped packet or a let-go key ends it within a second and the server
    keeps no per-player hold state. Splitters and mergers carry and are pushed like belts.
99. **Ramps and elevated belts.** Doc 04 §2.1 has belts "raised one level (1 m) to cross streets
    and other belts" and "ramps 2 tiles long". A level is 2.2 m here, not 1 m: the point of the
    crossing is a street that stays open, and 1 m would stop every carrier and bike at it; a
    4 m ramp up 2.2 m is steep, as factory belts are. A ramp faces the way the mail goes and
    runs up (turned 0–3) or down (4–7), so one recipe serves both ends of a crossing. Mail moves
    between tiles only at the same height; a ramp is fed from behind only (from the side it
    would come in half way up). Only the elevated belt is built on a street; ramps stand on
    the verges. Neither is solid (the posts are walked through, as belts are walked over) and
    neither carries a walker. Crossing another belt needs two constructs on one tile and is
    left for later.

100. **A playtest carrier parked into a raid.** `--raid --playtest-mailbox` parked the carrier at
    a mailbox the raid holds its line beside. Rather than move the mailbox (on the test street
    every one is within the raiders' reach of the line) or thin the raid (it is the raid's
    playtest), the parked carrier is protected the way a respawned one is in the yard: for as
    long as they stay within 4 m of the spot and have not hurt a raider. The fight starts when
    they choose. A park in a shift with no raid gives no protection.
101. **Shop keys and the Draft.** The Draft follows Payday at once, and the shop's cards (open
    anywhere at Payday) come down under it. A number key is the shop's while its cards are up;
    for 1.5 s after the Draft took them down the number keys take no card and select no slot,
    which covers a key pressed in the second the screen changed. A click on a card counts at once.
102. **What comes back of a construct.** Doc 04 §1.4: 100 % in Prep, 50 % in Delivery. The part is
    of the construct's materials as a whole, rounded down (and scaled by the HP it has left, as
    before): each input its own share rounded down, the rest of the whole one unit each to the
    inputs with most left over, the dearer item (sell price) on a tie. Rounded input by input,
    any construct of single units (a belt: a Plank and an Iron Ingot) gave nothing at all.

103. **The Address Sorter.** Doc 04 §2.4 gives the ways out, not their places: the input comes into
    the tile it was built on from behind; front and left leave the tile ahead of it, right the
    tile beside that, the overflow the tile beside the input, going back (doc: "the same side as
    the input, offset by one tile"). The sorter is no segment: mail goes into its buffer and out
    onto the heads of the belts its ports face (a port's belt counts as fed, so it starts a
    segment). An output with no filter takes nothing; one whose belt is missing is passed over;
    a piece with nowhere to go waits at the front of the buffer. Filters are by street, district
    and kind (doc's number ranges and units wait for a map that has them), one a slot, one slot an
    output (Extra Labels comes with that perk); the server takes any street or district of the
    map, the panel offers those open by the shift. The splitter's filter (doc 04 §2.3) is a kind
    only; its filtered outputs take what they catch, its unfiltered ones the rest, each in its
    own turn. The panel is up for one carrier at a time per client and asks the server nothing but
    open/close/set; "matches N in the Intake" is the server's count of where the Intake's mail would
    go by the filters alone. E at a sorter configures it; at a splitter E pushes, so its filters are
    E in Build mode (doc 09 §2.7 puts "configure" there).

104. **Inserters.** Doc 04 §2.5 says what an inserter takes from and puts into, not when. It
    moves a piece only when the front takes it then (a mailbox by the rules of design choice
    94, a grid with room, a sorter's buffer with room, a belt head with room in a lane), so no
    piece is ever held in the arm or dropped; from a grid it takes the first such piece in the
    grid's order, from the Intake the oldest, from a belt only the piece waiting at the very
    end. It works in Prep and Delivery, like the belts' outfeed, and moves mail only (doc 04's
    examples are all mail; materials stay in the pockets and chests). Its kind filter is the
    splitter's kind; it is configured by E as a sorter is. It stands in the way (it is a machine
    with a post, unlike a belt). It shares the sorter's key: with it Build mode would have had 11
    keys, and doc 09 §2.7 groups them as "Sorting".
105. **The Depot.** Doc 04 §2.6 gives a grid of 10 × 16 and "belts feed directly on any side"
    but no footprint or HP. It stands on 2 × 2 tiles like the sorter (the store of the
    reference's yard), 800 HP, and holds the grid 16 wide and 10 high so it fits the inventory
    screen beside the pockets. Only a belt on the ground feeds it (an elevated belt runs over),
    any piece of mail it has room for, in Prep and Delivery alike; nothing else of a belt's
    goes in. It shares the chest's key (group "storage") so Build mode keeps ten keys, and it
    needs no blueprint (doc 04 lists none for it). Its grid is reached within the interact
    range of its sides, not of its middle.
106. **The Long Inserter.** Doc 04 §2.5 says only "2-tile reach", doc 08 "reach (1 or 2)",
    doc 10 its cost and bp_sorting. It takes from the tile two behind and puts into the tile two
    in front, reaching over the tile between whatever stands there (a belt there is neither fed
    nor emptied); from a belt it takes the piece waiting at the end of a belt two behind that
    runs into the tile between (which must not hold a belt that carries it on). Everything else
    is the Inserter's: rate 1.25, the kind filter, the targets. It shares the sorter's key (the
    wheel: Address Sorter, Inserter, Long Inserter). Drawn with a taller post, a boom a tile
    longer (3.02 m to the grab) at 1.25 m, blue bands (the second tier's colour, as the Fast
    Belt's caps) and a counterweight.
107. **Pneumatic pipes.** Doc 04 §2.2 gives capsules at 5 m/s, 1 m apart, a target outlet chosen
    at the inlet by "a filter (address prefix or kind) mapping to an outlet id", unmatched to the
    default outlet, and an underground piece under streets at twice the cost; doc 10 the five
    pieces and costs. Choices: (a) the filter lives on the **outlet** (one card "Takes", street,
    district or kind, as a sorter's) rather than as a table on the inlet: the inlet sends a piece
    to the nearest reachable outlet whose filter catches it, else to the nearest one without a
    filter (the default); the routing is still decided at the inlet and every capsule carries its
    outlet. An outlet without a filter that stands before a mailbox takes that house's mail, so a
    house fed by pipe needs no setting. (b) Nothing ever collides in a pipe: an inlet sends no
    more to an outlet than it holds (8, sent or waiting) and no faster than one capsule a metre
    apart; a piece some outlet catches waits while those are full rather than going to a default.
    (c) A straight pipe opens behind and in front, a junction on all sides (corners, tees and
    crosses are junctions), an inlet only in front, an outlet only behind. Straight and buried
    pipes are walked over (not solid, like belts); junctions, inlets and outlets stand. (d) The
    underground pipe is one tile a piece, straight, allowed on a street (doc 10's "+span" read as
    a piece per tile), shown as "Buried Pipe" so its recipe card fits Build mode's bar. (e)
    Outlets put out at 2 pieces a second ("same insert rate"). (f) bp_pipes is $7.00 from shift 3
    (doc 10: 700, shift 3). (g) Splitter and Merger share a key of Build mode now so the pipes
    get one and Build mode keeps ten keys. (h) Vertical pipes up buildings wait for something to
    build up.
108. **The Mail Truck.** Doc 04 §3.1 gives 10x8, 14 / 7 m/s, two seats, 900 ¢ with the truck
    blueprint, "1 Oil Can per 5 min driving" and a loading zone at the rear; doc 10 600 HP and
    bp_truck (500, shift 2). Choices: (a) the bay is the existing `mail_truck_cargo` (8 wide, 10
    high, mail only) and is opened from the driver's seat as a bike's rack is; (b) the second
    seat is not used until NPC drivers and passengers come; (c) its body is two 0.5 m circles
    1.2 m before and behind its middle (the prop index is built for bodies of 0.5 m, so no
    wider), so its nose and tail do not go through walls; its width overhangs a little; (d) the
    tank holds two cans (10 minutes) and a bought truck comes with one; it burns only while it
    rolls faster than 0.1 m/s, and run dry it stops and its driver is set down; an Oil Can in
    the hand and E at it fills it (one can a press); (e) "parked" is "nobody in it": then a belt
    on the ground ending into the tile under the middle of its rear, or an inserter facing that
    tile, loads it; (f) a carrier may own one vehicle of each kind (a bike and a truck), the
    counter's old "one for each carrier" now per kind; (g) a free entry (a grant) reads "You were
    given …" in the feed.

109. **The Vehicle Depot.** Doc 04 §3.2 gives 3 × 3 tiles, "one parking zone, one loading face,
    one Route Console", stops of an address, a district macro or a construct, and one depot per
    two players rounded up; doc 10 its HP, cost and blueprint. Choices: (a) it is a pad that
    walkers and vehicles cross, its canopy drawn high over a truck; (b) the bay is the middle
    tile, the loading face the back middle tile (where a truck's rear stands in the bay), and a
    truck got off with its middle on the pad is set straight into the bay, so belts meet its rear;
    (c) six stops, each a house, a street or a district — a street is a macro like a district;
    construct stops (a Depot, a Port) wait for the ports; (d) a stop is set on the console's cards
    as a sorter's filter is, stepping through houses, then streets, then districts open by the
    shift: the map's route editor of doc 09 §2.6 waits for the map screen's layers; (e) the
    recipe is open from shift 1 and its blueprint (sold from shift 2) is what holds it back; (f) it
    shares the chest's key of Build mode (doc 09 groups it under Vehicles, but Build mode has ten
    keys and every group but two is used).

110. **NPC drivers.** Doc 04 §3.2 gives 150 ¢ a shift, one a depot, 60 % of player speed, the
    stops' mail at the box's insert rate, never guessing, fleeing raiders within 20 m, and the
    takeover. Choices: (a) the driver is hired on the console's seventh card and its hire ends at
    Payday; (b) it sets out with the truck in the bay once that holds mail for a stop and 4 s went
    by with nothing more loaded (doc 11's "waits at stops"), never with an empty truck; (c) it
    drives the routing graph's way kinematically (not the carriers' physics), at 0.6 of the
    vehicle's road or off-road speed, and stops within 3.5 m of a box; (d) "the route" for fleeing
    is the truck, the way still to go and the houses still to come; (e) while a carrier drives,
    the driver hands mail out wherever the truck stands still by a box of the route; (f) the
    server sends NPC-driven trucks' places in every snapshot while any is driven, eased on the
    client; (g) drivers do not fight and cannot be hurt (vehicle HP waits for wrecks).

111. **Water and the Rowboat.** Doc 02 §3.2 has shallow and deep water, doc 04 §3.3 a 1 × 3 pier
    "over shallow water" for "rowboat mooring", doc 10 the rowboat's stats, its build placement
    from the Sea kit's blueprint and the shop's 200 ¢ from shift 2. Choices: (a) the sim's water
    is tiles of the map (a basin inside the map's edge on the test street, closed by a sea wall);
    walkers cannot enter it, and there is no swimming; (b) its surface is drawn 0.45 m under the
    quay and the sim stays flat, so a rower's eyes are where a walker's are; (c) a pier must start
    from walkable ground or another pier, so it is always reachable; (d) out of a boat one steps to
    the nearest walkable point within 2.2 m, else not at all; (e) the placed rowboat is no construct
    but a vehicle (a Build-mode recipe with behaviour `boat`), on the chest's key with the pier
    (Build mode keeps ten keys); (f) one boat of a kind per carrier, as with bikes and trucks; (g) a
    bought rowboat is moored on the free water nearest the counter, with water all round it; (h) a
    kit-only blueprint is a shop entry of slot `kit`, never on the counter; (i) the basin shelves:
    shallow under the beach's west (piers), deep along the rest of the quay (ports); getting out
    reaches 2.6 m.

112. **The Small Port and the Motor Boat.** Doc 04 §3.3 gives "3 × 4 with 2 tiles over deep water",
    a parking zone with a loading face, belts and inserters, a Route Console for captains; doc 10
    its HP, cost and the Harbour blueprint. Choices: (a) the back two rows are a quay on land, the
    front two over deep water; (b) the bay is the middle column of the water rows, the boat's
    stern at the quay's edge, and the side columns hold pontoons boats cannot pass; (c) the loading
    face is the back middle tile of the quay (a belt reaches it from behind the port, as a depot's),
    drawn with a chute down to the boat; (d) the Route Console and NPC captains wait: on one island
    with one basin there is no second harbour to run a water route to; (e) the Motor Boat drives
    on the truck's fuel rules and is moored like a bought rowboat.

113. **Going down and running dry on the water.** The docs say a dry vehicle stops (doc 04 §3.1)
    and a carrier who goes down leaves a bag where they fell (doc 05 §1.1), and nothing about the
    water. Set down into the basin, nobody can walk out (water is solid to feet) or reach a bag.
    Choices: (a) a carrier who goes down in a boat on the water lies, with their bag, at the
    nearest point of shore or deck (`PlayerMovement.Ashore`), "washed up"; (b) a boat run dry
    keeps its skipper aboard and drifts to a stop (`PlayerMoveState.Becalmed`, set by the server
    from the tank, a move flag in the snapshot so prediction agrees, protocol `m2.9`); an Oil Can
    from the seat fills it ("Refuel … [E]" in place of the locker's prompt), and the jump key
    still gets out by a pier or the shore. A truck run dry still sets its driver down.

114. **The Business Dock.** Doc 02 §3.5 puts one "on a coastal lot with deep water; accepts Cargo
    only", the Small Island's "from shift 4"; doc 03 its acceptance "Boat or vehicle unloading
    zone, belt end, or crane (deferred) · Instant" and Cargo "Cannot be carried by hand". Choices:
    (a) a destination like a house, with its own `unlockShift`; on the test street (a test map,
    whose boats come at shift 3) it opens with the Motor Boat in shift 3; (b) its "mailbox" is the
    middle of its unloading zone at the quay's edge, reached by a truck from land and a boat from
    the water; (c) "Instant" is read as automatic: a vehicle held still in the zone is unloaded
    without a key, at the dock's insert rate, by the rules automation goes by (it never takes what
    would be a misdelivery), whoever drives it; an NPC driver or captain unloads by themselves;
    (d) by doc 11's numbers a crate (600 ¢ and the shift's multiplier) is more than Cargo's share of
    a solo shift 3 or 4 (5 % / 10 % of 1.6 × the quota), and the spawner's per-shift accrual would
    lose that share: a kind that cannot make one piece in a shift hands its share to the largest
    kind, as Cargo did while no dock stood; (e) the scenery grows as if the dock were not there and
    its lot is cleared after (the stream's draws depend on what fits), so adding a dock to a map
    moves no tree elsewhere; (f) a dock's address is "11 Larch Lane" like any; it is named on its
    sign and the map.

115. **The water navmesh and the captain.** Doc 04 §3.3 says captains "behave like NPC drivers on
    water routes (routing graph includes ferry lanes and a coarse water navmesh generated per
    seed)"; doc 10 §3 hires one for 150 ¢ a shift from shift 3 at a Small Port. Choices: (a) the
    navmesh is the grid of open water tiles (8-connected, no corner cut past land or a pier) with
    A* and string pulling, computed on demand from the map and what stands on it rather than
    baked, since piers and ports change it; ferry lanes wait for the Large Island; (b) the
    captain is the depot driver's state machine with the water's way, speed (60 % of the boat's
    9 m/s) and berths in place of streets and mailboxes; (c) a port's stops are docks only (only
    a destination with a berth within the dock zone's reach can be served from a boat), by
    address; (d) the hire shift is 3 for a solo carrier and a group alike (doc 10 gives one
    number); (e) a carrier who boards the captain's boat takes the wheel and the captain stands by
    and unloads at the route's docks, as a driver does; (f) a way cut off (a pier built across)
    leaves the boat standing where it is until the shift ends.

116. **Oil seeps and the Oil Pump.** Doc 02 §3.9 puts oil in seeps of the marsh, a Large Island
    biome; doc 10 gives the pump (1 tile, 400 HP, 6 Iron Ingots, 2 Glass, bp_oil, "1 Oil Can /
    45 s; no power draw; place on oil seep") and the blueprint ($6.00, shift 3), and says nothing
    of where the cans go. Choices: (a) a Small Island has no marsh, so it gets three seeps on
    open grass out of town from a stream of their own (and the test street two in its file), or
    the pump could not be built at all before M3; (b) the cans go into a small tank on the pump
    (ten cans), taken out by hand as from a chest — belts and inserters carry mail only; (c) it
    pumps in Prep and Delivery, the run's working phases (as belts run), and stops while the tank
    is full; (d) a forecourt: a vehicle held still beside it is filled straight from the tank, a
    whole can at a time, so a truck or a boat parked by a pump never runs dry; (e) the doc's id
    `pump` is `oil_pump` here; it shares the chest's key of Build mode (the recipe bar holds ten
    keys, and doc 09 §2.7's "Extractors" category has only this one).

117. **Pings and the wheel.** Doc 09 §2.6 and §4 give a click to ping, a hold for a wheel of four
    words, 10 s, a compass icon and a 3D marker; nothing on how the wheel is steered with a
    captured mouse. Choices: (a) the ping goes where the crosshair meets the ground (the map
    screen does not take the mouse; pinging on it waits for the map's mouse mode); (b) while the
    wheel is up the mouse moves the wheel's pointer and not the view, and letting go in its
    middle pings "Look here"; (c) at most two pings a second a carrier on the server, three
    standing a carrier on the screen; (d) a ping in the feed for everyone, red for danger; (e)
    until the compass strip exists, the beam (7 m of light) shows the way from afar.

118. **A crowded counter.** With Blueprint: Oil the shelf holds 21 cards in shift 1 and 22 in
    shift 2; seven rows of three do not fit at 1280 × 720 (the rendered smoke caught it; 20 did).
    Choice: above 20 cards the counter lays them four abreast and narrower, without the line saying what each is
    (name, price and note stay; `HudText.ShopCompact`).
119. **A debug world.** Nothing in the docs; asked for to see and photograph every construct at work
    without building it. Choice: a map file of its own whose `debug` block lists the showcase by
    building ids, tiles and turns, as a carrier would build it, and is built through the same
    placement rules (a layout that breaks a rule throws, and the tests build it). Its mail is one
    piece a second for the addresses in turn instead of the spawner's batches, so every station sees
    mail at a steady pace; the run is shift 5 so every blueprint, the captain and Cargo are out. It
    is not one of the Lobby's worlds (`WorldChoices`). The toggles live in the file, not on keys, so
    a guest's client knows them from the map it builds; only the camera and the time scale are keys.
    The sea lies along the south edge as on the other maps, because the basin and its backdrop are
    drawn there (`IslandView.Basin`).
120. **Where the eyes are at a truck's wheel.** The docs say only "first person". Choice: in the cab
    over the bonnet (1.15 m ahead of the middle, 1.8 m up), seeing past the cab's own walls; a bike
    and the boats keep the eyes over the seat.
121. **The map's layers and chips.** Doc 09 §2.6 lists them without keys, defaults or the source of
    "mail in the Intake/Depot". Choice: the layers that cost nothing to read are always drawn;
    the three chips start Mail and Routes on, Resources off (the nodes crowd a town); keys Z, X, V
    while the map is up. The Intake's and the storage's contents are the server's (a client sees
    a container only while it has it open), so it tells everybody a per-house count when it
    changes, at most once a second.
122. **The route editor's gestures.** Doc 09 §2.6 says click, click a district's label, drag. Choice:
    the map over an open console is the editor (M toggles it there as anywhere); a house click
    fills the stop after the last one (a route has no holes), a district needs its name drawn to
    be clicked so the editor draws each district's name; drag drops a stop onto another's place;
    a right click takes one off (as a right click steps a card back at the console). The edits
    are the console's requests, nothing new on the wire.
123. **The turret's sums.** Doc 05 §4 gives damage, rate and range; it is silent on whether a shot
    can miss, whom a turret's kill belongs to, whether it fires at a raider running away, and where
    it sits on the build bar. Choice: hitscan, it never misses (as the hammer's blow); the kill is
    nobody's, the loot lies for anybody; a fleeing raider is let go (the raid is over for it); on
    the Alarm Post's key as the second tower.
    The debug world gets a turret range of its own so the turret is seen working without a raid.

## Commands

All from the repo root. `-nodeReuse:false` avoids hangs on stale MSBuild nodes on this machine.

```
~/.dotnet/dotnet build PerformativeMail.sln -nodeReuse:false
~/.dotnet/dotnet test  PerformativeMail.sln -nodeReuse:false     # Sim.Tests + Net.Tests
# the M0 acceptance tests and the chest fuzz, with their measured numbers:
~/.dotnet/dotnet test tests/Net.Tests -nodeReuse:false --filter "Acceptance|ChestFuzz" --logger "console;verbosity=detailed"

tools/validate-content.sh [CONTENT_DIR] [--quiet]   # ContentValidator: "content OK …" / "error: …" lines, exit 0 / 1
~/.dotnet/dotnet tools/SeedViewer/bin/Debug/net8.0/SeedViewer.dll 1-20 [--map] [--island]   # the generated town of each seed: figures, the town in letters, the island by biome

tools/smoke.sh [seed]            # build + three Godot headless smokes (the full run, the raid playtest, the debug world); exit 0 and "SMOKE OK" three times
~/.local/bin/godot --headless --path . -- --smoke --seed=1      # the smoke alone (build first)
~/.local/bin/godot --headless --path . -- --smoke --seed=1 --shift=2 --no-quota --raid --playtest-package   # the raid playtest's smoke alone

# the smoke in a (virtual) window, saving PNGs of the boot view and each prompt:
xvfb-run -a -s "-screen 0 1280x720x24" ~/.local/bin/godot --path . --rendering-driver opengl3 -- --smoke --shots=/tmp/shots

~/.local/bin/godot --path .                                      # play: host on the test street, in the Lobby (G starts)
~/.local/bin/godot --path . -- --seed=7 --name=Ada               # user args go after "--"
~/.local/bin/godot --path . -- --quick                           # skip Lobby, Prep and the Drafts: straight into shift 1
~/.local/bin/godot --path . -- --debug-world                     # the showcase: every construct built and running (design choice 119)
#   F1-F5, F7-F11 station bookmarks · F12 back to the carrier · ` free camera (WASD, Space/Ctrl, Shift) · , . / time 0.25x 1x 4x
#   god mode, unlimited money/materials/blueprints, the clock stopped; no raid unless --raid
~/.local/bin/godot --headless --path . -- --smoke --debug-world  # its smoke alone ("SMOKE OK")
xvfb-run -a -s "-screen 0 1280x720x24" ~/.local/bin/godot --path . --rendering-driver opengl3 -- --debug-world --solo --shots=/tmp/dbg   # a PNG per bookmark, "SHOTS OK"
~/.local/bin/godot --path . -- --world=island --seed=7           # host the seed's generated Small Island town instead of the test street
~/.local/bin/godot --path . -- --profile=/tmp/p.json             # another profile file than user://profile.json
~/.local/bin/godot --path . -- --low                             # weak GPU: no lamp lights, shadows, glow, AA; coarser shapes
# in the Lobby: K picks your starting kit (Sea from Postal Rank 2); the host's V steps through the worlds, N draws a new seed
# in the Draft: 1-3 take a card, R (or the button) draws new cards of your own, once a run (twice from Postal Rank 4)
# in a debug build: F6 reloads content/ (a host alone in the game; the feed says whether it took)
# in a run: M puts up the map with the way to the house of the mail in your hand; Z / X / V toggle its Mail / Routes / Resources chips; at a depot's or port's console, M edits its route on the map

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
#   --playtest-package    four Small Packages for the mail cannon (RMB) when a shift starts and when back on your feet
#   --playtest-wallet=N   runs start with $N in the team's wallet (e.g. 5 or 4.50); asked in a run, topped up to it at once
~/.local/bin/godot --path . -- --shift=2 --no-quota --raid       # host: G, G, and the fight is on
~/.local/bin/godot --path . -- --raid --playtest-package         # host: G, G, and the cannon is loaded when the raiders come
~/.local/bin/godot --path . -- --join=192.168.1.20 --raid --playtest-package --playtest-mailbox   # a guest: all three go together
~/.local/bin/godot --path . -- --join=192.168.1.20 --raid --playtest-no-timer   # a guest brings a raid and stops the clock

tools/ci.sh [content] [test] [smoke]   # what CI runs (.github/workflows/ci.yml), stage by stage; "CI OK"
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
panel on screen must equal `HudText` of the server's summary / results; in the Draft between the
shifts the cards on screen must be the server's and read as `HudText.DraftText` says, none greyed,
inside the view and under the top lines, with the hint naming the keys; the host is turned so
that a label of the world lies under the cards, and none may be drawn on them; the pointer must
be free while the cards are open and the view's again once the card is taken (as after every
panel the smoke opens and shuts); the smoke presses a card's
key (in the raid run it clicks the card), and in the next Prep the perk must be held on the server and the client, counted on the
"Perks" plate and named in the feed; in the first Prep the counter serves the host first works
the land at a tick a frame: beside a Bush the prompt must read as `HudText.Prompt` says, E held
must fill the bar and take the three hits in three swings and no fewer than 51 ticks, the Fiber
must be in the pockets and in the feed, the bush bare on both ends and drawn bare after one
redraw of the nodes; a Boulder must ask for a Pickaxe in red and take no swing; a Tree is chopped
down by hand the same way; a Berry Bush and an Ore Vein are looked at; then the host goes to the
shop (at a player's pace): "Open Shop  [E]" at the counter under its sign, a press, and the cards
on screen must read as `HudText.ShopText` says — the server's specials framed, the cards that
cannot be bought greyed, all inside the view; a click on a greyed card sends nothing, the key of
the Bandages buys them (wallet, hotbar and feed checked on both ends), one is sold back, one is
used at 40 HP from the hand ("… · [Q] use" over the hotbar), the last is sold, the Fiber and the
Logs go over the counter at their sell price, and a second press
takes the cards down; at the first Payday the host is put at the mailbox farthest from the counter:
the hint must end in "[C] shop", the shop key must put the cards up there with the summary out
of the way, a number key buys the cheapest card, it is sold back, the wallet is right on both
ends and the summary is back once the key took the cards down; the host continues and the
Lobby must be clean (wallet 0, no carried mail, no perks, panel gone, every node whole again). Close-up checks: the player spawns on the pad; mail arrives; the Intake
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

The second run, `--smoke --shift=2 --no-quota --raid --playtest-package` (about 15 s), is the
playtest of nits 13 and 18 as a player would do it: the flags must be on the host's player, go
starts Prep of "Shift 2 / 5" with shift 2's quota (and no package yet), Ready starts it; the raid
banner and the tracker's countdown must show from the first second, with four packages in the
pockets and "mail cannon x4" on the weapons line, and the raid must be there 5 s in; the host
walks out to the street and fights through the client's input — the four packages out of the
cannon at raiders 5–24 m down the street (four shots, four blasts, none left), then the hammer,
going at raiders who hold their line — until all six pulses are in and nobody of them is left on
the server, in the client's list or drawn (every raider felled by the host, the tracker counting
them, the feed saying so); it walks over the loot (coins in the wallet line, cards shown); the
rest of the shift runs out, Payday's panel says QUOTA MISSED and the hint "playtest: the run goes
on · shift 3 in …", the Draft follows (the smoke takes the first open card), and the run must
then be in shift 3's Prep with no Results and no raid left.
