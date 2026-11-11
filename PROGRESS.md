# Performative Mail — build progress

From-scratch build on `fable52/blank`. The spec is `design/drive/` (chapters 00–12). This file tracks
what exists, what is next, and every place the docs were silent and a choice was made.

## Starting point

- Branch root commit `66c56ab` contains only `design/drive/*.md`. No code, no project, no content.

## Target: M0 — Foundation (doc 12)

Netcode, movement, hand delivery on a fixed map. Standing rule: rough playable first, harden later.

### Rough playable loop (first goal)

- [ ] Solution skeleton: `PerformativeMail.Sim` (plain C#, no Godot), Server, Client, App, xUnit tests
- [ ] Content as data (`content/`): mail kinds, mix, destinations, containers, balance, test map
- [ ] Sim: 30 Hz tick, player movement + tile collision, grid containers, mail spawn into Intake,
      mailbox acceptance / misdelivery, team wallet
- [ ] Net: binary messages, protocol hash, loopback transport, ServerRuntime / ClientRuntime
- [ ] Headless bot that walks Intake → mailbox and delivers (the smoke proof of the loop)
- [ ] Godot host window: world view, third-person camera, minimal HUD (timer, wallet, interact prompt)
- [ ] Godot headless smoke that boots the project and exercises spawn → pickup → deliver → wallet

### Hardening toward the written M0 acceptance criteria (after the rough loop)

- [ ] ENet transport (listen server + remote clients), protocol hash rejection
- [ ] Inventory panel UI, optimistic ops with rollback
- [ ] 8 clients / 10 min soak with container hash check on every version
- [ ] Prediction error < 10 cm RMS at 200 ms / 5 % loss (network conditioner)
- [ ] 10 000-op two-player chest fuzz: no duplicate / lost item
- [ ] Delivery / misdelivery pay exactly per ch. 03 §2.2 (unit tests)
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
4. **M0 has no run state machine (M1).** Stand-in: the run sits in `Delivery` of shift 1 with the
   240 s shift clock; when the clock ends it restarts (earnings reset, quota re-sampled from player
   count). Mail spawn budget per clock is `quota(1, n) × spawnOverhead`, as in ch. 01 §3.
