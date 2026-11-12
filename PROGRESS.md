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
- [ ] Postal Rank XP kept in a local MetaProfile (doc 01 §5.1, doc 08 §3.8; XP only)
- [ ] Solo pause (doc 01 §1 "Shift clock")

### Playtest nits on fa44a16 (`logs/fable52/nits-fa44a16.md`, 2026-11-12)

- [x] Nit 1 — huge world labels: house numbers, mailbox addresses, INTAKE, POST OFFICE and name
      tags are `SizedLabel3D`: real size from far away, but never more than 5 % of the screen height
      per line up close (`LabelSizing`, unit-tested; the smoke checks every label every frame). House
      numbers moved from above the ridge to above the wall that faces the mailbox, where the roof
      does not hide the now-small number from the street.
- [ ] Nit 2 — second manifest click put a package into the inventory, double height
- [ ] Nit 3 — manifest rows reorder under the cursor
- [ ] Nit 4 — an emptied hotbar slot stays selected
- [ ] Nit 5 — a very fast E tap can fail to open the Intake
- [ ] Nit 6 — complaint 20 after one 4-letter misdelivery (design check only)

### Hardening toward the written M0 acceptance criteria (after the rough loop)

- [ ] ENet transport (listen server + remote clients), protocol hash rejection
- [ ] Inventory panel: drag and drop / rotate / sort, optimistic ops with rollback (the rough panel
      is click = quick-move, server-confirmed only)
- [ ] 8 clients / 10 min soak with container hash check on every version
- [ ] Prediction error < 10 cm RMS at 200 ms / 5 % loss (network conditioner)
- [ ] 10 000-op two-player chest fuzz: no duplicate / lost item
- [x] Delivery / misdelivery pay exactly per ch. 03 §2.2 (unit tests)
- [ ] Server tick ≤ 2 ms with 8 players on the test map
- [ ] ContentValidator CLI

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
    the oldest stack of that row. Any external container is shown this way for now (the Intake is
    the only one in M0). Own containers are drawn as grids; a click quick-moves.
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

## Commands

All from the repo root. `-nodeReuse:false` avoids hangs on stale MSBuild nodes on this machine.

```
~/.dotnet/dotnet build PerformativeMail.sln -nodeReuse:false
~/.dotnet/dotnet test  PerformativeMail.sln -nodeReuse:false     # Sim.Tests + Net.Tests

tools/smoke.sh [seed]            # build + Godot headless smoke; exit 0 and "SMOKE OK" on success
~/.local/bin/godot --headless --path . -- --smoke --seed=1      # the smoke alone (build first)

# the smoke in a (virtual) window, saving PNGs of the boot view and each prompt:
xvfb-run -a -s "-screen 0 1280x720x24" ~/.local/bin/godot --path . --rendering-driver opengl3 -- --smoke --shots=/tmp/shots

~/.local/bin/godot --path .                                      # play: host on the test street, in the Lobby (G starts)
~/.local/bin/godot --path . -- --seed=7 --name=Ada               # user args go after "--"
~/.local/bin/godot --path . -- --quick                           # skip Lobby and Prep: straight into shift 1
```

Godot needs to find the .NET runtime: either `dotnet` on `PATH` or `DOTNET_ROOT=$HOME/.dotnet`
(`tools/smoke.sh` sets both itself).

Godot loads the assembly from `.godot/mono/temp/bin/Debug/`, so run `dotnet build` after every code
change before starting Godot from the command line.

The smoke plays one whole run inside the real Godot host (about 10 s): it boots into the Lobby
(HUD says so, no mail yet), the host presses go, Prep shows the coming quota, Ready ends Prep, then
in Delivery the close-up checks below run at 3 ticks per frame; after them the rest of the run is
fast-forwarded at 30 ticks per frame with the bot delivering, and at every Payday and at Results the
panel on screen must equal `HudText` of the server's summary / results; the host continues and the
Lobby must be clean (wallet 0, no carried mail, panel gone). Close-up checks: the player spawns on the pad; mail arrives; the Intake
prompt and manifest show; mail is carried; the mailbox prompt shows; delivery and misdelivery events
reach the client; server wallet = start + paid − penalties = client wallet = the HUD's wallet line;
no container desync or bad packet; the world, player view and camera exist; the boot view (camera
at the eyes, not in a wall, clear view along the map's spawn facing); the camera at the player's eye
position and aimed along the interact ray every frame; no local body; the ✓ and ✗ marks drawn; no
missing glyphs; the top HUD row fully on screen; the real-time loop advances the server.
