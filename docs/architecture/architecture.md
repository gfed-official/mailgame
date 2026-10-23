# Performative Mail — Master Architecture

## Document Status

- **Version:** 1.2
- **Last Updated:** 2026-10-16
- **Changelog:**
  - 1.2 (2026-10-16): ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026 Accepted (owner-confirmed batch; `technical-director` acceptance). ADR-0012's adapter half stays spike-gated on OQ-IC-9, and ADR-0026 covers the M-1a `IWorldQuery` stub only. Amended to match: `IWorldQuery` member list owned by ADR-0026 (ADR-0013 §6 states the carrier's requirements; `TerrainHeightMm` = `WorldHeight.Sample`, `QueryStaticSolids`, no `StaticVersion`); `Sim/Geometry` primitives are ADR-0013's (not ADR-0015's); ClientLayer `Previews` sub-steps 4a Focus → 4b DeliverPreview → 4c ThrowPreview → 4d EdgeResolve → 4e HoldAdvance (ADR-0016 §2; ADR-0014 §7 amended); `FlightRecord` capacity 160 (16 = perf fixture); backlog storage and drain are Inventory's, and Mail appends (ADR-0017 §6); `AddressId` packed u32 in `Sim/Primitives`; `IHostClock` unchanged, with render time from Client.Core `IRenderClock` (ADR-0016); the router pushes into `InputLatch` (ADR-0012 §4). Errata recorded on ADR-0006 (carrier does not use `Fx16`) and ADR-0009 (`ThrowRelease` Sim OpKind fixed by ADR-0015).
  - 1.1 (2026-10-16): ADR-0001…0011 Accepted (owner-confirmed batch). Amended: ADR-0025 scope narrowed by ADR-0009 (TR-inventory-007 → 0009); network adapter core moved to Godot-free `Client.Core/Net` (ADR-0011); `IStatSheet.Read` sketch → `int` + `ushort` qualifier `0xFFFF` (ADR-0010); `net8.0` CI test leg from M-1a (ADR-0001 §7, required by ADR-0005/0006); `PerformativeMail.Game.csproj`; `TickDriver.Advance(now)` (ADR-0002); linked-folder fallback dropped; Q-ARCH-02/03 answered by the spike; TR-content-024 (SHA-256 in the Sim) and TR-content-027 (ViewCatalog implementation → ADR-0019).
- **Author:** `/create-architecture full --review lean` run in autonomous mode. Decisions are logged in `production/session-logs/decision-log.md` under the `create-architecture` entries.
- **Engine:** Godot 4.7.1, C# (.NET 8 minimum for desktop).
  - The machine has the Godot standard build at `~/.local/bin/godot` and (since 2026-10-16) the Godot 4.7.1 **.NET build** at `~/.local/bin/godot-mono` (SHA-512 verified).
  - The .NET SDK is 10.0.402 via `mise exec dotnet@10.0.402 -- dotnet …`; the .NET 8.0.426 SDK / 8.0.32 runtime is also installed, so the `net8.0` test leg can run locally.
- **Stage:** Technical Setup.
- **GDDs Covered** (10 of 10 system GDDs):
  - grid-inventory, player-character-movement, input-controls, mail-destinations, manual-delivery, throwing-catching, hud-notifications. These seven M-1a GDDs are In Review: reviewed, revised and reconciled.
  - simulation-core and content-data-definitions are both Provisional and mid-review.
  - comms is a FROZEN draft and contributes headline requirements only.
  - game-concept.md, game-pillars.md and systems-index.md were read for context.
- **Spec chapters used:**
  - 06 (netcode, superseded in part by 13 §3);
  - 07 (technical architecture, superseded in part by Sim Core DEV-1..8 and 13 §3);
  - 08 (schemas, narrowed by Content & Data);
  - 13 §3 (v1 netcode simplification).
- **Seed:** `docs/architecture/simulation-core-adr-inputs.md` (unreviewed TD seed). Where it conflicts with `simulation-core.md`, the GDD wins.
- **ADRs Referenced:** ADR-0001…ADR-0011 are **Accepted** (2026-10-16). 31 are listed as required in §Required ADRs; the remaining 20 are not yet written.
- **TD-ARCHITECTURE:** 2026-10-16, skipped (lean mode).
- **LP-FEASIBILITY:** 2026-10-16, skipped (lean mode).

> **How to read this document.**
> - It is the whole-system blueprint. Individual decisions are recorded, with their alternatives, in the ADRs listed in §Required ADRs.
> - When this document and an Accepted ADR disagree, the ADR wins and this document is amended.
> - Interfaces in §API Boundaries are **contracts to be finalised by the named ADR**. They are not final signatures.

---

## Engine Knowledge Gap Summary

| Field | Value |
|---|---|
| **Engine** | Godot 4.7.1 (pinned 2026-10-15; 4.7.2 exists, released 2026-08-18) |
| **LLM training covers** | Up to about 4.3 |
| **Post-cutoff versions** | 4.4, 4.5, 4.6, 4.7, 4.7.1 |
| **Sources** | The only sources are `docs/engine-reference/godot/` (`VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md`). No `modules/` directory exists. |

**Verification labels.** Every Godot API named below carries one of two labels. Neither label ever stands for "from memory".
- **VERIFIED (ref)**: the API is stated in those reference files.
- **NEEDS VERIFICATION**: the API is assumed, and a spike must confirm it before an ADR relies on it.

### HIGH RISK domains (verify before deciding)

| Domain | What the reference says | What is unknown | Systems touching it |
|---|---|---|---|
| **Networking / multiplayer** | 4.5: `Node.get_rpc_config` → `get_node_rpc_config`; `JSONRPC.set_scope` → `set_method`. 4.6: `StreamPeerTCP` members moved to `StreamPeerSocket`; `TCPServer` members moved to `SocketServer`. | NOT SOURCEABLE for 4.4–4.7: `MultiplayerAPI` / `SceneMultiplayer` / ENet behaviour, which thread RPCs arrive on, ordering across RPC methods and channels, auth callbacks, `GetRemoteSenderId` on loopback/Steam, fragmentation, `MultiplayerSynchronizer`. The Steam path from C# is unproven (spec 13 §3). | Networking & Session; every GDD's Network section; Grid Inventory (op/reply ordering); Throwing (flight protocol); PC (transforms, Teleport); Comms (channel 3) |
| **C# / .NET integration** | Desktop needs .NET 8 minimum (4.5 page). Android needs .NET 9 (not relevant). | NOT SOURCEABLE: .NET SDK minimum for 4.6/4.7; whether the Godot.NET.Sdk accepts a `ProjectReference` to a plain `net8.0` library; default compile-glob leakage; Export Debug/Release → MSBuild property mapping; trimming/AOT in export; which runtime the exported game loads; `System.Text.Json`, `System.Collections.Immutable`, `SHA256.HashData` in a Linux export; `GC.GetAllocatedBytesForCurrentThread` in export. **The Godot .NET build is not installed**, so none of this can be checked yet. | Simulation Core (OQ4), Content & Data (S1–S4), every Presentation module |
| **Input** | 4.7: compare `InputEvent.Device` against `InputEvent.DEVICE_ID_MOUSE` / `DEVICE_ID_KEYBOARD`, not `0` (VERIFIED). The Input GDD's headless probe of the 4.7.1 standard build found that `InputEvent` has no timestamp. | NEEDS VERIFICATION: `_Input` vs `_Process` order within a frame, `use_accumulated_input`, `joy_connection_changed`, `keyboard_get_label_from_physical`, the C# names of all of the above. | Input & Controls, Manual Delivery (ordered edges), Throwing (aim-release) |
| **UI / Accessibility** | 4.7: `Control.accessibility_live` is typed `AccessibilityServer.AccessibilityLiveMode` (C# break, VERIFIED). CanvasItem lines are no longer antialias-feathered (VERIFIED). New projects default to stretch `canvas_items` / aspect `expand`, existing projects unaffected (VERIFIED). `RichTextLabel` image `width_unit`/`height_unit` (VERIFIED). | NEEDS VERIFICATION: AccessKit tree behaviour of invisible Controls, CanvasLayer compositing above post-processing, draw-call counters. | HUD & Notifications, Inventory UI, Comms, Accessibility |

### MEDIUM RISK domains (verify key APIs)

| Domain | Reference facts | Systems |
|---|---|---|
| **Rendering** | 4.5: `RenderingServer.instance_set_interpolated` / `instance_reset_physics_interpolation` removed. 4.6: glow default blend is Screen, `glow_intensity` 0.8→0.3, volumetric fog brighter, D3D12 default on new Windows projects. 4.7: `LinearToSRGB` no longer clamps. | PC remote interpolation (must be our own, not engine physics interpolation); Throwing ribbon/MultiMesh; Presentation |
| **Animation** | 4.6: `AnimationPlayer.assigned_animation` / `current_animation` / `autoplay` are `StringName`. 4.7: `Animation.length` is `double`. | PC rig, Manual Delivery reach layer |
| **Core / file I/O** | 4.4: `FileAccess.store_*` return `bool`. 4.6: `get_as_text` lost `skip_cr`. 4.5: `Resource.duplicate(true)` is shallow for external sub-resources; use `duplicate_deep(DEEP_DUPLICATE_ALL)`. | Content host (`ContentHost` reads bytes), Persistence |
| **Scenes / VCS** | 4.6 stops writing `load_steps` and saves unique node IDs. Run Project > Tools > Upgrade Project Files once. | All scenes |
| **Audio** | 4.7: `AudioStreamPlayer.area_mask` default is `0`. | Audio, Mail stings |

### LOW RISK domains

| Domain | Why low risk |
|---|---|
| **Physics (Jolt)** | Jolt is the default 3D engine since 4.6 (VERIFIED), with 4.7 Jolt changes to `WorldBoundaryShape3D` / `SoftBody3D`. **The Sim uses its own integer capsule-vs-tile solver**, so Godot physics is not on any authoritative path. The client may use none at all. |
| **Navigation** | Not used. The Sim owns the routing graph (World Generation). |

### Systems → risk

| System | Domain | Risk |
|---|---|---|
| Simulation Core (Hosting / GameLoop only) | Core, C#/.NET | HIGH. The Sim itself is engine-free; only the `GameLoop` host node touches Godot. |
| Content & Data (ContentHost, ViewCatalog) | C#/.NET, Core file I/O | HIGH (S1–S4) |
| Networking & Session | Networking | HIGH |
| Input & Controls (Godot adapter) | Input | HIGH |
| HUD, Inventory UI, Comms | UI / Accessibility | HIGH |
| Player Character (views, camera, rig) | Rendering, Animation | MEDIUM |
| Throwing (preview ribbon, MultiMesh) | Rendering | MEDIUM |
| Grid Inventory, Mail, Manual Delivery, Throwing, PC (Sim parts) | none (plain C#) | LOW: no engine surface |

**Consequence for sequencing:**
- The first implementation sprint is entirely inside the LOW-risk plain-C# Sim, so it is not blocked by engine risk.
- Every HIGH-risk boundary has a named spike, and the ADR that depends on it stays **Proposed** until the spike lands. The spikes:
  - OQ1 tick-loop spike;
  - OQ4 / Grid OQ20 build spike;
  - S1–S5 content export spikes;
  - OQ-IC-9 input spike;
  - Grid OQ12 / S5 / comms multiplayer spike.

---

## System Layer Map

The layering is the TD-gated one in `design/gdd/systems-index.md` §Dependency Map. Two adjustments:
- a **Platform** layer is added below it;
- its **Polish** layer is folded into Presentation and Tooling.

Two rules sit across all layers:
- **The Sim/Presentation split (spec 07 §1).** Every system that decides game state has its authoritative part in the Godot-free Sim assembly. Godot code renders, gathers input and moves bytes.
- **The orchestrator rule (TD gate).** Run Structure calls down. Lower systems report up through events they define.

```
┌───────────────────────────────────────────────────────────────────────────────────┐
│ PRESENTATION  (Godot: src/Game/**  +  Godot-free: PerformativeMail.Client.Core)   │
│  HUD & Notifications · Inventory & Build UI · Map & Address UI · Menus & Run Flow │
│  Audio · Proximity Voice · Presentation & World Dressing · Onboarding & Tips      │
│  Accessibility · Comms (UI half) · entity views, camera, rigs, VFX                │
├───────────────────────────────────────────────────────────────────────────────────┤
│ FEATURE  (Sim: PerformativeMail.Sim/<System>/  — authoritative rules)             │
│  Player Character & Movement (+ interaction framework) · Mail & Destinations      │
│  Manual Delivery · Throwing & Catching · Building · Belt Logistics · Vehicles     │
│  Player Combat · Enemy AI & Waves · Defenses · Resource Harvesting                │
│  Economy, Wallet & Shop · Run Structure (orchestrator) · Perks · Stamps & Events  │
│  Meta-Progression · Comms (message model, host relay)                             │
├───────────────────────────────────────────────────────────────────────────────────┤
│ CORE  (Sim + Godot-side adapters)                                                 │
│  Grid Inventory & Containers (+ WorldItem) · Health & Damage · World Generation   │
│  (tile grid, height field, routing graph) · Networking & Session (Godot adapter)  │
│  Profile & Settings Persistence (Godot-side store, Sim-free)                      │
├───────────────────────────────────────────────────────────────────────────────────┤
│ FOUNDATION                                                                        │
│  Simulation Core (Primitives, Core, Hosting, Composition, ClientLayer shell)      │
│  Content & Data Definitions (Sim/Content: defs, loader, validator, StatSheet)     │
│  Input & Controls (router core in Client.Core + Godot input adapter)              │
│  Godot host shell (GameLoop node, ContentHost, boot)                              │
├───────────────────────────────────────────────────────────────────────────────────┤
│ PLATFORM                                                                          │
│  Godot 4.7.1 .NET build · .NET 8+ runtime · SceneMultiplayer/ENet (+ Steam relay) │
│  OS (Windows, Linux; Linux headless server post-v1) · user:// file system         │
└───────────────────────────────────────────────────────────────────────────────────┘
```

### Physical assemblies (the layer map made concrete)

| Assembly / project | Path | Target | May reference | Must never reference | Holds |
|---|---|---|---|---|---|
| `PerformativeMail.Sim` | `src/PerformativeMail.Sim/` | `net8.0` | BCL only. Analyzers with `PrivateAssets=all`. | Godot, `Client.Core`, any package at runtime | Primitives, Core, Hosting, Composition, ClientLayer, Content, one folder per system (`Inventory/`, `Players/`, `Mail/`, `Delivery/`, `Throwing/`, `World/`, `Health/`, `Building/`, `Automation/`, `Vehicles/`, `Combat/`, `Run/`, `Economy/`, `Perks/`, `Meta/`, `Comms/`) |
| `PerformativeMail.Client.Core` | `src/PerformativeMail.Client.Core/` (created by the first Input, HUD or network-seam story) | `net8.0` | `PerformativeMail.Sim` (read-only use of Primitives, ids, event payloads, `InputFrame`) | Godot | Network adapter core (`Net/`: `HostAdapter`, `ClientAdapter`, codecs, dedupe, `LoopbackTransport`; ADR-0011), Input router core (`Input/`), HUD/UI core (`Ui/`: cue pipeline, governor, event log, `IHudState`, `IUiClock`), Comms sanitiser |
| Godot game project | `PerformativeMail.Game.csproj` at repo root (assembly `PerformativeMail.Game`; hand-written; kept out of the Godot-free `PerformativeMail.sln`) + `src/Game/**` | `net8.0`, `Godot.NET.Sdk/4.7.1` (spike-verified, ADR-0001) | Sim, Client.Core (`ProjectReference`) | n/a | `GameLoop`, `ContentHost`, `ViewCatalog`, `ITransport` implementations (`src/Game/Net/`; the adapter core is in Client.Core, ADR-0011), input adapter, views, HUD views, audio |
| `PerformativeMail.Sim.Tests` | `tests/PerformativeMail.Sim.Tests/`, which compiles `tests/unit/**` and `tests/integration/**` | `net10.0`; plus a CI `net8.0` leg (`-p:PmTestTfm=net8.0`) from the first Sim Core story (M-1a) for ADR-0005 golden vectors, ADR-0006 T2 and the 0-alloc gate; `windows-latest` at M0 (ADR-0001 §7) | Sim (+ Client.Core once it exists) | Godot | xUnit v3 unit, integration, determinism and golden-vector tests |
| `tools/ContentValidator` (M0), `tools/BalanceSim` (M1), `tools/BotClient` (M0+) | `tools/<Name>/` | `net8.0` console | Sim | Godot | CI tooling |

**Why there is no separate `PerformativeMail.Content` assembly.**
- Content & Data (CD6, CD21) puts the immutable def records and the loader in `PerformativeMail.Sim/Content`, reading through an `IContentSource` seam. No Godot types and no package dependencies are involved.
- A separate assembly would add a boundary around `ContentSet` (which every system consumes) with no isolation gain.
- The tools reference the Sim either way.
- If a later need arises (for example, presentation-only content), a split is a mechanical move.

**Why there is a separate `Client.Core` assembly.**
- Input & Controls (TR-input-001) and HUD (TR-hud-002) both require Godot-free, headless-testable client logic.
- That logic may use injected wall clocks (ms repeat timers, UI clock, flash governor). Wall clocks are banned in the Sim (TR-sim-032).
- Neither GDD may be referenced by the Sim (TR-hud-001).
- The HUD seed's `Ui.Core` becomes the namespace `PerformativeMail.Client.Core.Ui`.

**The Godot game project glob.**
- The project at the repo root excludes `src/PerformativeMail.*/**`, `tests/**`, `tools/**` and `**/obj/**` from its default compile glob, and references the two libraries once by `ProjectReference`. This is the grid-inventory / godot-specialist finding, and Sim Core R33.
- *(Amended 2026-10-16, ADR-0001–0011 Accepted.)* **No linked-folder fallback is needed:** the OQ4 spike (F1) verified the `ProjectReference` from `Godot.NET.Sdk/4.7.1` to the plain net8.0 Sim (ADR-0001).
- **`Directory.Build.props` is imported by the Godot csproj too.** It sets `TreatWarningsAsErrors`, `Nullable`, `InvariantGlobalization` and `EnforceCodeStyleInBuild`. Answered by the spike (F7; ADR-0001): the root props build the Godot project warning-free, except `Environment` ambiguity under `ImplicitUsings`; `InvariantGlobalization` is not in force in editor runs (F4), so culture-free APIs + the analyzer are the guarantee.

### Per-system layer assignment and split

| # | System | Layer | Sim part (authoritative / shared pure functions) | Presentation part | Milestone |
|---|---|---|---|---|---|
| 1 | Simulation Core | Foundation | everything except `GameLoop` | `GameLoop` node, `IMonotonicClock`/`IHostClock`/`IPhaseProbe` impls | M-1a |
| 2 | Content & Data (incl. StatSheet) | Foundation | defs, loader, validator rules, StatSheet, ContentHash encoder | `ContentHost` (`IContentSource` over `res://`), `ViewCatalog` | M-1a built-in constants → M0 loader |
| 3 | Input & Controls | Foundation | `InputFrame` struct, `holdEff` (Sim) | router core (Client.Core), Godot adapter (`_Input` gather) | M-1a |
| 34 | Health & Damage | Core | HP components, damage queue, death events | HP widgets | stub M-1b → M3 |
| 8 | Grid Inventory & Containers | Core | containers, ops, WorldItem, conservation | grid panel Control, drag visuals | M-1a minimal → M1 |
| 6 | World Generation | Core | tile grid, height field, routing graph, addresses | terrain/lot views | stub M-1a (hand-authored street) → M1 |
| 5 | Networking & Session | Core | none: Sim knows no peers (TR-sim-042) | transport, op encoder, `IEventSink`, JoinState, lobby, loopback | loopback M-1a → M0 |
| 23 | Profile & Settings Persistence | Core | none | `user://` stores, schema migration | M-1a `user://input.json` → M1 |
| 4 | Player Character & Movement | Feature | solver, life states, interaction framework, focus, hold counter, host position ring | camera boom, rig, interpolation, prompt anchor | M-1a → M1 |
| 7 | Mail & Destinations | Feature | mail table, registry, `Evaluate`, payment, complaint buckets | mailbox/house views, stings | M-1a → M1 |
| 9 | Manual Delivery | Feature | anchor registration, availability, preview (ClientLayer) | prompt render, reach/riffle | M-1a |
| 10 | Throwing & Catching | Feature | ballistic, sweep, flights, catch | aim ribbon, ghost, bundle meshes | M-1a (catch M0) |
| 11–22 | Building, Belts, Vehicles, Combat, Enemy AI, Defenses, Harvesting, Economy, Run Structure, Perks, Stamps, Meta | Feature | per GDD (not yet written) | per GDD | M-1b → M4 |
| 42 | Comms | Feature/Presentation | message model, host relay ordering | chat/ping/emote UI, sanitiser (Client.Core) | M0 (frozen) |
| 24 | HUD & Notifications | Presentation | none (TR-hud-001) | Client.Core.Ui core + Godot views | M-1a → M4 |
| 25–33 | Inventory & Build UI, Map UI, Menus, Audio, Voice, World Dressing, Onboarding, Accessibility, Dev Tooling | Presentation / Tooling | none (Dev Tooling: Sim counters and recorder) | all | M0 → M4 |

**Engine awareness: Foundation and Core.** Only the Godot-side halves touch engine domains.

| Module | Domain | Risk | Reference excerpt or status |
|---|---|---|---|
| `GameLoop` (Sim Core) | `Node._Process`, `ProcessMode` | HIGH | NEEDS VERIFICATION (OQ1 spike). The reference only says to avoid `SceneTree.Paused` coupling by design. |
| `ContentHost` | `Godot.FileAccess.GetFileAsBytes`, PCK export filter | MEDIUM/HIGH | "4.6 `get_as_text` lost `skip_cr`" (VERIFIED, so read bytes, not text). PCK filter for `*.jsonc` NEEDS VERIFICATION (S1). |
| Input adapter | `InputEvent.Device`, `DEVICE_ID_*` | HIGH | "Compare `InputEvent.device` against the constants — affects gamepad/KBM switching" (VERIFIED, 4.7). |
| Network adapter | SceneMultiplayer, ENet, `[Rpc]` | HIGH | NOT SOURCEABLE (VERSION.md). |
| Persistence | `user://`, `FileAccess.store_*` | MEDIUM | "`store_*` return `bool`" (VERIFIED, 4.4). Check every write. |

---

## Module Ownership

### Foundation

| Module | Owns (exclusively) | Exposes | Consumes | Engine APIs |
|---|---|---|---|---|
| **Sim.Primitives** | `Cents`, `Fx16`, BAM angles + Q15 table, `Mm`/`Pm` helpers, `RH`/`CeilDiv`/`FloorDiv`/`isqrt`, PCG32, SplitMix64/FNV-1a, canonical LE writers | pure value types and functions | nothing (no dependency on Core) | none |
| **Sim.Core** | tick counter, `PhaseId` table (14), op ring + drain, reply records, per-tick `EventLog`, id allocators (`EntityId`, `mailId`, `PlayerSlot`), `RngNames` registry and stream handles, `SimCounters` + record ring, lifecycle (Constructed→Ready→Running→Stopped/Faulted) | `SimWorld.Tick()`, `EnqueueOp`, `IEventSink` delivery, `ISimStateContributor` dump, `IRunControl` | `ContentSet`, `SimSettings` | none |
| **Sim.Composition** | `SimWorldBuilder`, `SimComposition.Build(profile)`, binding of phases/ops/event kinds/readers/contributors | a frozen `SimWorld` / `ClientLayer` | every system's registration method | none |
| **Sim.Hosting** | `TickDriver` (integer accumulator), `FrameRunner` (per-frame: drain transport queue, advance, tick, deliver events) | `TickDriver.Advance(now)` (frequency fixed at construction; ADR-0002) returning the count of ticks to run; `FrameRunner.RunFrame()` | `IMonotonicClock` (injected), `SimWorld`, `ClientLayer` | none. The timestamp comes from the host. |
| **Sim.ClientLayer** | client shell: `InputLatch`, local `CarrierStep`, mirror of replicated state, `Previews` phase (Manual Delivery preview, throw preview), `IHostClock` consumer | per-tick client step, published prompt data (`PromptData`) | `InputFrame`, mirror batches, shared pure functions (`Evaluate`, ballistic F2/F4, `holdEff`) | none |
| **Sim.Content** | immutable def records, `SimSettings`, balance table, `DefIndex`/`StringId`, ContentValidator rule catalogue (84 active), `ContentHash` canonical encoder, `StatSheet` / `StatResolve`, `BuiltInContent` (M-1a) | `ContentSet` (by reference, process lifetime), `IStatSheet` (read, anywhere), `IStatWriter` (Perks, Stamps only) | `IContentSource` | none |
| **Client.Core.Input** | router state machine `RouterState`, action map + alias table, conflict validator, grammar machines (G1–G9), deadzone/trigger/repeat math, input event-log ring (4096) | `InputFrame` per tick, immediate requests (server-validated verbs), prompt glyph data | abstract raw-input records, `IUiClock`/monotonic ms, `IInteractionQuery` (PC) | none |
| **Godot host shell** (`src/Game/`) | `GameLoop` node (one per process), boot order, `ContentHost`, `ViewCatalog` | n/a | Sim, Client.Core | `Node._Process` (NEEDS VERIFICATION), `Godot.FileAccess.GetFileAsBytes` (NEEDS VERIFICATION in export, S1), `[Export] PackedScene[]` (S4) |
| **Godot input adapter** | `_Input` gathering, arrival stamping, device identity, mouse mode, focus-loss synthesis | raw-input records to the router | Godot input events | `_Input`, `InputEvent.Device` + `DEVICE_ID_MOUSE`/`DEVICE_ID_KEYBOARD` (VERIFIED 4.7), `Input.MouseMode`, `joy_connection_changed`, `ignore_joypad_on_unfocused_application` (NEEDS VERIFICATION) |

### Core

| Module | Owns | Exposes | Consumes | Engine APIs |
|---|---|---|---|---|
| **Sim.Inventory** | `ContainerState` (+ uint32 `version`), entries (`entryId`, `count`, ascending `mailIds`), StackKey, WorldItems (cap 512, expiry FIFOs), tombstones, conservation counters | ops `pick/move/merge/split/quick_move/take_all/return/sort/drop/deliver/deliver_matching/throw/pickup` through the op FIFO; sync `IInventoryApi` (`Spawn`, `TryInsert`, `TryExtract`, `TakeUnit`, `PeekOutfeed`, `Grant`, `CountItem`, `ConsumeItems`, `SpillToBag`, `TryCatch`, `AppendBacklog` (ADR-0017 §6: backlog storage and phase-2 drain are Inventory's)); events `Inserted/Removed/Merged/ContainerDelta/GroupCounts/UnitsRemoved/ContainerChecksum/ItemsLost` | `ContentSet` (ContainerDef, ItemDef), `IStatSheet` (`inventory_rows`, `stack_mult`, `weight_penalty_mult`), PC range/position (F5) | none |
| **Sim.Health** (stub M-1b) | HP components, damage queue (phase 10), `PlayerDied` (one per carrier per tick) | `ApplyDamage(amount, source, actorId)` (queued) | content | none |
| **Sim.World** (stub M-1a) | tile grid (2 m), height field, walkability, lots/addresses, routing graph, spawn pad | read-only `IWorldQuery` (ADR-0026 owns the member list: `TerrainHeightMm` = ADR-0013's `WorldHeight.Sample`, `GetCorners`, `WaterDepthMm`, `Tile`, `Bounds`, `QueryStaticSolids`, `Lots` in ascending `AddressId`, Post Office pad; M-1a source `M1aStreet`) | seed, content | none |
| **Network adapter** (core in Godot-free `Client.Core/Net/`; only `ITransport` implementations in `src/Game/Net/`; ADR-0011) | transport peers, peer→account→`PlayerSlot` map, op wire encoder/decoder per OpKind descriptor, reqId u16 dedupe + last-16 reply replay, JoinState chunking, `IEventSink` impl, `IHostClock` estimator, loopback | `IEventSink`, `IHostClock`, `EnqueueOp` calls (on main thread only) | `SimWorld` event batches, snapshot sources | SceneMultiplayer, ENet peer, `[Rpc]` (all NOT SOURCEABLE). Superseded APIs to avoid: `JSONRPC.set_scope`, `StreamPeerTCP.get_status` (VERIFIED renames). |
| **Godot persistence store** | `user://input.json` (M-1a), `user://profile.json` (M1), schema versions + migration | load/save of `settings.*` and MetaProfile (content ids as strings) | Client.Core settings shapes | `FileAccess` (store_* returns bool, VERIFIED 4.4) |

### Feature (Sim halves; M-1a set)

| Module | Owns | Exposes | Consumes |
|---|---|---|---|
| **Sim.Players** | carrier kinematic state (mm, mm/s), life states + absolute-tick timers, `IInteractable` registry + 2 m spatial hash, focus scoring, hold counter (P12), host position-history ring (9+3 ticks), knockdown chain (phase 11) | `Players.Step` (phase 3, host role for Dead/Frozen; owner role in ClientLayer: `CarrierStep` = `OwnedCarrier.Step`, then focus / press / hold in `Previews` 4a / 4d / 4e, ADR-0014 §7 + ADR-0016 §2), shared `Sim/Geometry` primitives (ADR-0013 §6), `IInteractionQuery`, `IPlayersApi.Teleport`, events `CarrierKnockedDown`, `PlayerFrozen`, `PlayerDespawned` | `InputFrame`, `IWorldQuery`, `IInventoryApi` (weightPoints, `SpillToBag`), `IRemoteCapsuleSource`/`ISupportTransformSource`, `PlayerDied` |
| **Sim.Mail** | mail table (SoA, `mailId`), destination registry, manifest, door/team complaint buckets, feeder credits, backlog feed (appends via `IInventoryApi.AppendBacklog`; storage/drain are Inventory's, ADR-0017 §6), `AddressId` (packed u32, `Sim/Primitives`), `MailShiftStats` | `Evaluate(dest, unit)` (pure, shared with client), `ResolveThrownLanding`, `OfferUnit`, `PrepareShift`, `UnlockDistrict`, `SetDeliveriesOpen`, `SpawnSpecial`; events `DeliveryOutcome`, `ComplaintRaised`, `DeliveriesState`, `BatchArrived`; port `IWalletGate` (defined here, implemented by Economy) | `IInventoryApi`, streams `mail`/`mail.special`, content (MailKindDef, DestinationTypeDef, MailMixDef), `IStatSheet` |
| **Sim.Delivery** | Deliver `IInteractable` per destination, availability rows, `DeliverPreviewTracker` (ClientLayer), branch model | preview `PromptData` (ClientLayer), `deliver_matching` request shape | `Evaluate`, `holdEff`, `IHostClock.EstimatedHostTick`, Inventory pending queue |
| **Sim.Throwing** | `FlightRecord`s (capacity 160; 16 is the perf fixture), integer ballistic F2, swept segment test F4 (on ADR-0013's `Sim/Geometry` primitives), catch arm state, leaky-bucket cooldown, static 4 m receiver grid (catch volumes, belts; world solids via `IWorldQuery.QueryStaticSolids`, ADR-0026) | `Throwing.Step` (phase 4), shared `Ballistic`/`Sweep` (used by preview), events `ThrowLaunched`, `FlightResolved` | `IInventoryApi` (`throw`, `TryCatch`), `Mail.ResolveThrownLanding`, PC position history, `IWorldQuery`, `ThrowDef` |

Features after M-1a (Building, Belt Logistics, Vehicles, Combat, Enemy AI, Defenses, Harvesting, Economy, Run Structure, Perks, Stamps, Meta) get their ownership rows when their GDDs are written. Their **phase slots are already reserved** in the 14-phase order, and their **ids are reserved** in the EntityId class table.

### Presentation

| Module | Owns | Consumes | Engine APIs (all NEEDS VERIFICATION unless marked) |
|---|---|---|---|
| **Client.Core.Ui** (HUD core) | cue pipeline, merge/evict, toast lane, flash governor, UI event log, money formatting call site, live-region schema | `IHudState` (pull values + ordered events by `(sourceTick, opId)`), `IUiClock`, `IRoster`, `IMoneyFormatter`, `IHostClock.renderSimTimeMs`, cue table (validated by Content's validator library) | none |
| **HUD views** | CanvasLayer stack, MarkerLayer, widgets, two live-region Controls | Client.Core.Ui | `CanvasLayer`, `Control`, `Control.AccessibilityLive` typed `AccessibilityServer.AccessibilityLiveMode` (VERIFIED type move 4.7). No `DrawLine`/`DrawPolyline`, because line AA was removed in 4.7 (VERIFIED). |
| **Inventory panel view** | one custom-drawn grid Control, focus navigation | Inventory mirror, router | `Control._Draw`, cached `DrawString` |
| **Entity views** | PlayerView (rig, AnimationTree), MailboxView, WorldItemView, bundle MultiMesh, aim ribbon, camera boom | ClientLayer mirror + events | `AnimationTree`; `AnimationPlayer` StringName members (VERIFIED 4.6); `Animation.length` double (VERIFIED 4.7); `MultiMeshInstance3D`; `Camera3D.UnprojectPosition` |
| **Audio** | buses, voice stealing (≤16 mail voices) | events | `AudioStreamPlayer(3D)`, `area_mask` default 0 (VERIFIED 4.7) |

### Dependency diagram (compile-time and call direction)

```
                 Godot game project (src/Game/**)
        GameLoop · ContentHost · Net adapter · Input adapter · Views · HUD views
                 │ ProjectReference                 │ ProjectReference
                 ▼                                  ▼
     PerformativeMail.Client.Core  ───────────►  PerformativeMail.Sim
     (Input router core, Ui core)   read-only    ┌──────────────────────────────┐
                                                 │ Hosting → Core → Primitives   │
                                                 │ Composition → (all systems)   │
                                                 │ ClientLayer → systems' pure fns│
                                                 │ Content → Primitives          │
                                                 │ Feature systems → Core systems│
                                                 │   (sync I…Api, own phase only)│
                                                 └──────────────────────────────┘
     tests/PerformativeMail.Sim.Tests ─► Sim (+ Client.Core)      tools/* ─► Sim
     Nothing in Sim or Client.Core references Godot (CI: arch test + `using Godot` grep).
```

Inside the Sim, call direction follows the systems-index layers:
- Feature → Core → Foundation.
- Run Structure calls down into Mail, Waves and World. Everything reports up by events.
- A same-layer peer is called only through its documented `I…Api` during the caller's own phase. Throwing calls `Mail.ResolveThrownLanding` and `Inventory.TryCatch` this way.
- Cross-system calls into another system's internals are forbidden (spec 07 §4, Sim Core R4/R9).

---

## Data Flow

### 1. Frame update path (host process, M-1a solo over loopback)

All of this runs on the Godot main thread (Sim Core R18). Nothing crosses a thread boundary.

```
Godot frame
 │ _Input(e)  [Input adapter]  ── arrival-stamp, device id ──► raw-input record ring
 │
 │ GameLoop._Process(delta /*unused*/)
 │   FrameRunner.RunFrame()
 │     1. transport drain: loopback/remote ops ──EnqueueOp──► op ring (FIFO, opSeq)
 │     2. n = TickDriver.Advance(clock.Timestamp)                      // 0..4, drop-not-bank
 │     3. repeat n:
 │          InputLatch emits InputFrame (router flushed once before RunFrame, ADR-0012 §4)
 │          ClientLayer.Step(tick, frame): InputLatch → CarrierStep
 │                                         → Previews (4a Focus → 4b MD preview → 4c throw preview
 │                                                     → 4d EdgeResolve → 4e HoldAdvance; ADR-0016 §2)
 │          (immediate verbs → op encoder → loopback → op ring)            [same codec as remote]
 │          SimWorld.Tick():                                               [host role]
 │            0 Inputs.Apply   (drain ≤64 ops by opSeq; transform samples)
 │            1 Items.Expire   2 Intake.Drain   3 Players.Step   4 Throwing.Step
 │            5 Automation.Step 6 Vehicles.Step 7 Agents.Step   8 Mail.Step
 │            9 Economy.Flush  10 Combat.Resolve 11 Combat.React 12 Run.Step
 │              ITickEndCheck (conservation etc.)
 │           13 Events.Flush ──IEventSink.Deliver(tick, span, overflowInfo)──► Net adapter
 │     4. Net adapter: loopback batch ──► ClientLayer mirror (+ remote peers at M0)
 │ Views/HUD _Process: read ClientLayer mirror + IHudState events; interpolate; draw
```

| Data | Producer → Consumer | Mechanism |
|---|---|---|
| Raw input records | Godot input adapter → router core | shared ring, same thread |
| `InputFrame` | router → ClientLayer `InputLatch` | sync value (struct) per tick |
| Requests (deliver_matching, throw, inventory ops) | ClientLayer / UI → net adapter → op ring | sync call; loopback uses the remote codec (spec 07 §3) |
| Op | op ring → owning `IOpHandler` in phase 0 | FIFO drain by `opSeq`, cap 64/tick |
| In-tick cross-system effects | caller → `I…Api` of callee | sync call inside caller's phase |
| `SimEvent`s | systems → `EventLog` → `IEventSink` | append in-tick; delivered once in phase 13 (copy-only) |
| Replies | handler → `EventLog` (after the handler's events) | same stream, so event-before-reply ordering holds |
| Presentation state | mirror / `IHudState` → views | pull per frame |

M-1a stubs:
- phases 0–4, 8 and 12 are bound (12 is a shift-timer stub);
- the other phase ids exist but are no-ops;
- the ClientLayer and SimWorld run in one process, connected by loopback.

### 2. Event / signal path

- **Inside the Sim** there is no observer pattern.
  - Systems append blittable `SimEvent`s to one preallocated per-tick `EventLog`: cap 4096, reserve 256, entries carrying `kind`, `replicationClass`, `audienceMask` and a payload with no strings.
  - In-Sim consumers read through **pull cursors** (≤8 readers, for example `Combat.React`). There are no synchronous subscribers (R12).
- **Sim → Godot.** `IEventSink.Deliver` is called once per tick in phase 13. The sink copies the span. It never calls back into the Sim.
- **Replication classes.** `Internal`, `Replicated`, `OwnerOnly` and `Targeted` decide what the network adapter sends. An undeclared kind defaults to `Internal`, and an enumeration test fails on it.
- **Overflow.** Overflow sets `overflowInfo{overflow, droppedAudienceMask, droppedKindsMask}`. The adapter resyncs exactly those audiences and kinds.
- **Godot side.** Godot signals (C# `[Signal]` delegates) are used **only inside Presentation** (view ↔ view). They never carry gameplay decisions. HUD consumes a single ordered event stream through `IHudState`.
- **Presentation timing rule.** Outcome beats never play before the domain event. The rule is shared by Mail, Throwing, Manual Delivery and HUD. Predictions appear only in prompts and previews.

### 3. Save / load path

| State | Owner of serialisation | Format | When | Milestone |
|---|---|---|---|---|
| `settings.input` | Persistence (Godot store); shape owned by Input | JSON, versioned, overrides-only | on change | M-1a (`user://input.json`) → M1 |
| MetaProfile + other settings | Profile & Settings Persistence | `user://profile.json` (+ Steam Cloud). Content ids as strings, never `DefIndex`. | Payday, Results | M1 |
| Run state (Arcade) | not saved mid-run (spec 07 §7.2) | rolling in-memory RunSnapshot at Payday | Payday | M1+ |
| Canonical state dump | each `ISimStateContributor` (fixed order, ascending ids, LE) | binary; SHA-256 in tests only | between ticks | M-1a (tests) |
| Replay recording | `IOpRecorder` (ops + transform samples) | in-memory (M-1a), file writer (M5) | per tick | M-1a / M5 |
| Free Play save | deferred (spec 07 §7.3) | same contributors as the dump | — | post-v1 |
| JoinState / RejoinState | network adapter envelope; each system contributes a block (≤64 KB chunks, channel "bulk") | adapter wire format | join, rejoin | M0 |

### 4. Initialisation order

```
Process start
 1. GameLoop._Ready (single autoload or root node; NEEDS VERIFICATION of autoload order)
 2. ContentHost → IContentSource → ContentLoader (5 stages: syntax, schema, build,
    references, whole-set) → ContentSet (+ SimSettings, balance)   [M-1a: BuiltInContent]
    → ContentHash (managed SHA-256 in the Sim) failure → content error screen; no Sim
 3. Persistence: load settings.input / profile (unknown ids dropped + logged)
 4. Input router: action map + overrides → validator (reachable states)
 5. Session: solo → loopback adapter; host/join (M0) → transport + Hello/HelloAck
    (ContentLoadFailed > VersionMismatch > ContentMismatch > RunInProgress)
 6. SimComposition.Build(profile) → SimWorldBuilder binds phases/ops/events/readers
    → Freeze() (captures owner thread) → SimWorld Ready; ClientLayer composed alongside
 7. World stub / World Gen → destinations registered → Deliver anchors registered (MD1)
 8. Views: ViewCatalog resolves def ids → DefIndex arrays; scene instantiation
 9. TickDriver reset → Running (first Tick() processes tick 0)
```

---

## API Boundaries

The language is C# (net8.0, nullable, warnings-as-errors). These are **contracts for the ADRs to finalise**:
- names follow `.claude/docs/technical-preferences.md` (PascalCase types and members, `_camelCase` private fields);
- field widths come from the GDDs;
- no Godot types appear in any boundary below the Godot host shell.

### Sim.Hosting — tick driver (ADR-0002)

```csharp
public sealed class TickDriver            // Godot-free; never reads a clock
{
    public TickDriver(SimSettings s);     // TickRateHz = 30 (code constant); MaxCatchUpTicksPerFrame = 4
    // Returns ticks to run this frame (0..cap). Elapsed clamped to 1 s; excess beyond cap is DROPPED (counted), never banked.
    // Invariant F2: (ran + dropped) * freq + acc30 == Σ min(Δ, freq) * 30. Backwards clock counted, never negative.
    public int Advance(long now);         // ADR-0002: frequency (10^3..10^12) and maxCatchUp are constructor arguments
    public void DropRemaining();          // M0 wall-clock guard (catchUpGuardMs = 10)
    public void Pause(); public void Resume(); // solo pause: Advance returns 0; Resume clears the accumulator
    public TickDriverCounters Counters { get; }
}
public interface IMonotonicClock { long Timestamp { get; } long Frequency { get; } } // host-implemented (Stopwatch)
public sealed class FrameRunner { public void RunFrame(); }   // called by exactly one GameLoop._Process
```
**Invariants.** Main thread only. Asserted in Debug in `Tick`, `EnqueueOp` and the sample drain. ≥1 tick always runs once the guard is active. `SceneTree.Paused` is never used.

### Sim.Core — world, phases, ops, events (ADR-0003, 0007, 0008)

```csharp
public enum PhaseId : byte { InputsApply, ItemsExpire, IntakeDrain, PlayersStep, ThrowingStep,
    AutomationStep, VehiclesStep, AgentsStep, MailStep, EconomyFlush, CombatResolve,
    CombatReact, RunStep, EventsFlush }                         // exactly 14; ITickEndCheck runs between 12 and 13
public interface ISimPhase { PhaseId Phase { get; } void Run(in TickContext ctx); }
public interface ITickEndCheck { void Check(in TickContext ctx); }
public interface IOpHandler { OpKind Kind { get; } void Handle(in Op op, in TickContext ctx); } // validate fully, then mutate; reject mutates nothing

[StructLayout(LayoutKind.Sequential)]
public struct Op { public uint OpSeq; public uint Actor; public ushort Kind; ushort _pad; public uint ReqId; /* + ≤48 B payload union */ } // 64 B blittable
public sealed class SimWorld
{
    public bool EnqueueOp(in Op op);      // false when ring full (cap 4096, systemOpReserve 64): no opSeq consumed; adapter replies `busy`
    public void Tick();                   // phases 0..13 in order
    public uint CurrentTick { get; }
}
public interface IEventSink { void Deliver(uint tick, ReadOnlySpan<EventEntry> events, OverflowInfo overflow); } // copy-only, no re-entry
public enum ReplicationClass : byte { Internal, Replicated, OwnerOnly, Targeted }
public readonly record struct Reply(uint ReqId, ReplyResult Result, ushort Reason /*1–63 Sim, ≥64 systems*/ /* + ≤24 B extra */);
public interface ISimStateContributor { void Write(ref CanonicalWriter w); } // fixed registration order; valid only between ticks
public interface IRunControl { void RequestStop(StopReason r); }            // phase 12 only
```
**Guarantees.**
- **Actor.** The adapter stamps `Actor` from the transport peer, never from the payload. `System` is 0x00000001.
- **Ordering.** Ops drain in ascending `opSeq`. Every drained op is recorded. A reply follows its handler's events in the same stream.
- **No dedupe in the Sim.** Duplicate handling is the adapter's job (ADR-0011).

### Ids, RNG, numerics (ADR-0004, 0005, 0006)

```csharp
public readonly record struct EntityId(uint Value)                 // class << 24 | counter, never reused in a run
{ public EntityClass Class => (EntityClass)(Value >> 24); }
public enum EntityClass : byte { Player = 0x01, WorldItem, Construct, Vehicle, Enemy, Npc, Destination, Prop /* 0x09–0xEF reserved, 0xF0–0xFF tests */ }
public readonly record struct MailId(uint Value);  public readonly record struct PlayerSlot(byte Value); // 0–7
public readonly record struct FlightId(ushort Value);   // wrap-safe, creation order, never compared numerically
public readonly record struct EntryId(uint Value);      // run-unique, inventory-owned
public sealed class RngStream { public uint Next32(); public uint NextInt(uint bound); } // (next32*b)>>32, one draw per call, PCG32 XSH-RR
public static class RngDerive { public static ulong Derive(ulong runSeed, StreamName name, uint index); } // Mix64(Mix64(seed ^ Fnv1a64(name)) ^ index)
public readonly record struct Cents(long Value);        // ±2^62; float/double/decimal banned assembly-wide (analyzer)
```
Golden vectors are committed as tests (TR-sim-030).

### Content (ADR-0010)

```csharp
public interface IContentSource { IEnumerable<string> ListManifest(); ReadOnlyMemory<byte> Read(string path); } // ContentHost / DirectorySource / InMemorySource
public sealed class ContentSet { public SimSettings Settings { get; } public BalanceTable Balance { get; }
    public DefTable<ItemDef> Items { get; } public DefTable<MailKindDef> MailKinds { get; } /* … */ public byte[] CanonicalEncoding(); }
public static class ContentLoader { public static ContentLoadResult Load(IContentSource src); } // 5 stages; errors unsuppressible
public interface IStatSheet { int Read(Stat stat, ushort qualifier = 0xFFFF, PlayerSlot? slot = null, int transientAdd = 0); // 0-alloc; qualifier 0xFFFF = none (ADR-0010)
    StatExplain Explain(Stat stat, ushort qualifier = 0xFFFF, PlayerSlot? slot = null); bool Has(RuleFlag flag, PlayerSlot? slot = null); }
public interface IStatWriter { /* push/remove modifiers by source; injected into Perks and Stamps only */ }
```
**Invariants.**
- `ContentSet` is immutable and lives for the process. It is passed by reference into `new SimWorld(settings, content, runSeed)`.
- The Sim never reads `res://`.
- `DefIndex` is never persisted.

### Input (ADR-0012)

```csharp
public struct InputFrame { public uint Tick; public short MoveX, MoveY; /* int16 disc */ public ushort LookYaw; public short LookPitch;
    public ulong HeldBits; public EdgeList Edges; /* ordered, each with RouterStateId */ public StepList Steps; } // Sim struct
public interface IInputRouter { void Feed(in RawInputRecord r); InputFrame BuildFrame(uint tick); RouterState State { get; } } // Client.Core
public static class Hold { public static int HoldEff(int baseTicks, long insertPm /* StatSheet, clamped */); } // Sim, shared; 6-tick floor
```
**Rule.** Gameplay never calls `Input.IsAction*` outside the adapter. A CI lint enforces this (TR-input-002).

*(v1.2: ADR-0012 Key Interfaces supersede this sketch. The router pushes edges into ClientLayer's `InputLatch` through `FlushToLatch(IInputLatchWriter)`, called once before `RunFrame`. `InputLatch` emits one `InputFrame` per tick, with the edges on the batch's first tick.)*

### Interaction, inventory, mail, throwing (ADR-0014, 0009, 0017, 0015)

```csharp
public interface IInteractable { EntityId Entity { get; } byte Slot { get; } Verb Verb { get; } int RangeMm { get; } int Priority { get; }
    bool IsAvailable(in InteractionContext c, out ReasonKey why); int GetHoldTicks(in InteractionContext c); bool ShouldCancel(in HoldLatch l); }
public interface IInventoryApi { SpawnResult Spawn(ContainerRef c, ReadOnlySpan<MailId> units, MergePolicy p);
    bool TryInsert(ContainerRef c, in UnitRef u); bool TakeUnit(ContainerRef c, Selector s, out UnitRef u);
    CatchResult TryCatch(PlayerSlot p, in FlightPayload f); void SpillToBag(PlayerSlot p, Vec3Mm pos); /* … */ }
public interface IMailApi { DeliverEval Evaluate(EntityId dest, in UnitRef unit); /* pure, 0-alloc, shared with client */
    LandingResult ResolveThrownLanding(EntityId dest, in EntryRef e, PlayerSlot thrower, int thrownPm, uint releaseTick); }
public interface IWalletGate { bool TryDebitPenalty(Cents amount, Cents floor); void Credit(Cents amount, CreditReason r); } // defined by Mail, implemented by Economy
public static class Ballistic { public static Vec3Mm PositionAt(in Launch l, int k); } // closed-form integer; preview == host
```
*(v1.2: the Accepted ADRs' Key Interfaces supersede these sketches. See ADR-0014 (`IInteractable`, `IPlayersApi.CheckInteraction`, `ClaimedPosition`), ADR-0017 (`IMailApi`, `IWalletGate` in `Cents` with `CreditReason.DeliveryPay`, `IInventoryApi.AppendBacklog`), ADR-0015 (`LaunchMath`/`Ballistic`/`FlightSweep`) and ADR-0026 (`IWorldQuery`).)*

### Presentation seams (ADR-0016, 0018)

```csharp
public interface IHostClock { uint EstimatedHostTick { get; } bool IsValid { get; } } // Sim-defined; adapter implements; unchanged (ADR-0011)
public interface IRenderClock { long RenderSimTimeMs { get; } } // Client.Core (ADR-0016): (tick·1000 + AlphaPermille)/30
public interface IHudState { /* pull values */ HudValues Values { get; } bool TryReadEvent(out HudEvent e); } // ordered by (sourceTick, opId)
public interface IUiClock { long NowMs { get; } } public interface IRoster { /* names, hats, slots */ } public interface IMoneyFormatter { /* one call site */ }
```

### Network adapter boundary (ADR-0011, Accepted; transport half deferred to the M0 multiplayer spike)

*(Amended 2026-10-16, ADR-0001–0011 Accepted.)* The adapter core (`HostAdapter`, `ClientAdapter`, codecs, dedupe, `LoopbackTransport`) is Godot-free in `PerformativeMail.Client.Core/Net/`; only `ITransport` implementations live in the Godot project.

**The Sim exposes:**
- `EnqueueOp`;
- `IEventSink` delivery;
- `ISnapshotSource` for JoinState blocks;
- `ISimStateContributor`;
- `IHostClock` as an interface.

**The Sim knows nothing of:** peers, accounts, channels, RPC names, wall clock or RTT (TR-sim-042). All ticks in client requests are in the host tick domain.

**The adapter owns:**
- peer → slot mapping;
- the op codec, defined per OpKind descriptor;
- reqId u16 dedupe, with the last 16 replies replayed per connection;
- channel mapping (reliable-ordered for events, replies and requests; unreliable for transforms; bulk for JoinState);
- comms channel 3 (if the spike allows it);
- the content-hash handshake.

**Loopback** uses the same codec as remote (spec 07 §3).

---

## ADR Audit

### Existing ADRs

*(v1.2 note: this audit predates the ADRs. As of 2026-10-16, ADR-0001…0017, 0019 and 0026 exist and are all Accepted. Re-run `/architecture-review` to refresh this section and the traceability table.)*

The scan was `Grep "^## (Status|Summary)"` over `docs/architecture/adr-*.md`. It found **no ADRs**. Nothing exists to check for Engine Compatibility, GDD linkage or conflicts.

| ADR | Engine Compat | Version | GDD Linkage | Conflicts | Valid |
|-----|--------------|---------|-------------|-----------|-------|
| none | — | — | — | — | — |

There is one pre-ADR artefact: `docs/architecture/simulation-core-adr-inputs.md`, an **unreviewed** TD seed holding 9 Sim Core ADR drafts. It is the input material for ADR-0001…0008 and ADR-0006, but it is not itself an ADR.
- Where it disagrees with `simulation-core.md`, the GDD wins.
- **Known stale points the ADRs must not copy:**
  - op queue without a drain cap;
  - `busy` outside the event log;
  - per-phase coalescing;
  - `Deliver` without `overflowInfo`;
  - an `IHostClock` slew rule that differs from the GDD's "≤3 ticks p95 at 150 ms RTT";
  - worst-case fixture every 30th tick (the TD ruled every 150th tick, 8×2 ops);
  - "throw spread in mrad".
- **Gap:** the seed's §3.2 lacks the event-entry byte layout and payload ceiling. ADR-0008 must define them.

### Traceability coverage

The Technical Requirements Baseline (Appendix A) holds **250 requirements from 10 GDDs**.

| Covered by an Accepted ADR | Partial | Gaps | Deferred |
|---|---|---|---|
| **204** | **1** | **44** | **1** |

*Updated 2026-10-23 (m1a-replan, PR #47) from `docs/architecture/tr-registry.yaml`: 250 = 204 covered + 1 partial + 44 gap + 1 deferred. Accepted ADRs are 0001–0017, 0019, 0026 and the ADR-0024 M-1a stats-file slice (Evan 2026-10-23); ADR-0018's minimal slice is Proposed. The 44 gaps belong to ADR-0018 (19 rows, Proposed or deferred by its slice), 0020, 0021, 0022, 0024 (TR-hud-019, M1), 0025, 0029 and `perf-budget.md`. The partial row is TR-delivery-019 (data half via ADR-0014). The deferred row is TR-delivery-021 (ADR-0006; manual-delivery 008 Deferred to M0). The matrix is `docs/architecture/requirements-traceability.md`. Originally (2026-10-16, `/architecture-review`): 203 covered, 47 gaps.*

Every requirement is mapped below to a planned ADR, so no requirement is orphaned. Each gap closes when its ADR is written and Accepted.

| Planned ADR | Primary TRs (from Appendix A) | Count |
|---|---|---|
| ADR-0001 Solution layout & build | TR-sim-040; TR-movement-022 | 2 |
| ADR-0002 Tick driver & main thread | TR-sim-001..009; TR-input-014; TR-movement-007 | 11 |
| ADR-0003 Phase order & composition | TR-sim-010..014, 019, 041, 044 | 8 |
| ADR-0004 Identity & id spaces | TR-sim-025..026; TR-content-007..009; TR-inventory-003; TR-throwing-009 | 7 |
| ADR-0005 RNG streams | TR-sim-027..030; TR-mail-002..004; TR-throwing-002 | 8 |
| ADR-0006 Numerics, determinism, dump, replay | TR-sim-031..036, 045; TR-inventory-015, 017, 028; TR-input-021; TR-movement-002; TR-delivery-021; TR-mail-001, 012; TR-throwing-004 | 16 |
| ADR-0007 Op queue & replies | TR-sim-015..018, 020; TR-inventory-025; TR-delivery-011 | 7 |
| ADR-0008 Event log & replication classes | TR-sim-021..024; TR-mail-015 | 5 |
| ADR-0009 InventoryOp pipeline | TR-inventory-001..002, 004..007, 010..014, 019, 022 | 13 |
| ADR-0010 Content & StatSheet | TR-content-001..006, 010..030; TR-inventory-026; TR-movement-019..020; TR-mail-018; TR-throwing-019; TR-hud-007, 020 | 34 |
| ADR-0011 Network adapter boundary | TR-sim-042..043; TR-inventory-020..021; TR-input-013; TR-delivery-008..010, 016; TR-mail-014, 016..017, 021, 023; TR-comms-001..006 | 20 |
| ADR-0012 Input router | TR-input-001..010, 015..020, 022..024 | 19 |
| ADR-0013 Carrier solver | TR-movement-001, 004..006, 016..018 | 7 |
| ADR-0014 Interaction & hold | TR-input-011..012; TR-movement-011..014; TR-delivery-001..002, 007, 015 | 10 |
| ADR-0015 Ballistic & flights | TR-throwing-001, 003, 005..008, 013 | 7 |
| ADR-0016 ClientLayer & presentation timing | TR-delivery-003..006, 012..014, 017..018; TR-mail-005; TR-throwing-016, 018; TR-hud-003..004 | 14 |
| ADR-0017 Mail storage & wallet port | TR-mail-006..011, 013, 019..020 | 9 |
| ADR-0018 HUD composition | TR-delivery-019..020; TR-hud-001..002, 005..006, 008..018, 021; TR-comms-007..008 | 20 |
| ADR-0019 Godot scene architecture | TR-inventory-027; TR-delivery-023..024; TR-mail-022; TR-throwing-017, 021 | 6 |
| ADR-0020 Transforms & Teleport | TR-movement-003, 008..010, 015, 023, 026 | 7 |
| ADR-0021 Flight protocol & catch | TR-throwing-010..012, 014..015 | 5 |
| ADR-0022 JoinState & resync | TR-inventory-023..024 | 2 |
| ADR-0024 Persistence | TR-delivery-022; TR-throwing-020; TR-hud-019 | 3 |
| ADR-0025 Inventory indexing | TR-inventory-008..009, 016, 018 (TR-inventory-007 → ADR-0009) | 4 |
| ADR-0029 Animation & camera | TR-movement-024..025 | 2 |
| perf-budget.md (not an ADR) | TR-sim-037..039; TR-movement-021 | 4 |
| **Total** | | **250** |

ADR-0023, 0026–0028 and 0030–0031 have no primary TR yet: their GDDs (Networking & Session, World Generation, Belt Logistics, Enemy AI, Audio, Accessibility) are not written. Their TRs join the baseline when `/architecture-review` runs after those GDDs exist.

This table is generated from Appendix A: each TR is counted once, under its primary ADR. Decisions shared across ADRs (for example `holdEff` used by ADR-0012 and ADR-0016, or the height-field contract shared by ADR-0013 and ADR-0026) are cross-referenced inside the ADRs.
**Every TR ID in Appendix A carries exactly one primary ADR.**

---

## Required ADRs

Numbers are **reservations**. `/architecture-decision` may renumber them if it allocates sequentially, but the titles and priorities stand.

Status on creation is **Proposed**. An ADR marked "spike-gated" stays Proposed until its spike is recorded under `prototypes/`.

### Must have before the first implementation sprint (Foundation-critical)

The first sprint is Godot-free: the plain C# Sim plus xUnit, built and tested with `mise exec dotnet@10.0.402 -- dotnet test`.

| # | Title | Decides | Covers / closes | Spike-gated? |
|---|---|---|---|---|
| **ADR-0001** | Solution layout, assemblies, build configs & test framework | **Assemblies:** `PerformativeMail.Sim` (net8.0, Godot-free, no runtime package refs, float/double/decimal analyzer); `PerformativeMail.Client.Core` (net8.0, Godot-free); `PerformativeMail.Sim.Tests` (net10.0, xunit.v3, compiles `tests/unit/**` + `tests/integration/**`). **Builds:** `Directory.Build.props`; asserts keyed on `$(Optimize)` or a custom property, never `Configuration==Release`; no trimming. **Godot project:** `PerformativeMail.Game.csproj`, glob exclusions + `ProjectReference` (spike-verified; no fallback). **CI guards:** `using Godot` grep, architecture test. | SC OQ4, DEV-7, Grid OQ20; TR-sim-040, TR-movement-022 (+ the architecture-test half of TR-sim-013) | The Godot half is gated on OQ4 (needs the Godot .NET build). The Sim/test half can be Accepted now. |
| **ADR-0002** | Tick driver, FrameRunner & main-thread Sim | Godot-free integer accumulator `TickDriver.Advance(now)`; 30 Hz; ≤4 ticks/frame; drop-not-bank; Δ clamp 1 s; M0 guard 10 ms; solo pause in the driver (never `SceneTree.Paused`); one `GameLoop` node's `_Process` calls `FrameRunner.RunFrame()`; main thread only; transport callbacks only enqueue; `_PhysicsProcess` and threaded timers rejected | SC OQ1, R16–R18; TR-sim-001..009 | The `GameLoop` integration is gated on OQ1. The driver and its F2 invariant are testable now. |
| **ADR-0003** | Phase order, composition root & cross-system calls | Exactly 14 PhaseIds; `ITickEndCheck` between 12 and 13; `SimWorldBuilder` + `SimComposition.Build`; constructor injection; no statics or service locator; sync `I…Api` only inside the caller's phase; player op beats inserter; orchestrator rule; `ContentSet`/`IStatSheet`/`IStatWriter` in the injectable list (OQ-CDD-1) | DEV-1/2/3/6/8; TR-sim-010..014, 019, 041, 044 | No |
| **ADR-0004** | Identity & id spaces | EntityId class table (01–08, reserved and test ranges); `mailId` separate counter; `PlayerSlot` u8; `entryId` u32 run-unique; `flightId` u16 wrap-safe; `DefIndex` / `StringId` never persisted; temp client ids `0x8000_0000\|slot<<20\|ctr` | DEV-4; TR-sim-025..026, TR-content-007..009 | No |
| **ADR-0005** | RNG streams & derivation | PCG32 XSH-RR; `Derive = Mix64(Mix64(seed ^ Fnv1a64(name)) ^ index)`; compile-time `RngNames`; `NextInt` single draw; owner-only handles; committed golden vectors; Throwing has no RNG | SC OQ3, DEV-5; TR-sim-027..030, TR-mail-002..004 | No |
| **ADR-0006** | Numerics, determinism rules, canonical dump & replay | Integer-only units (mm, permille, ticks, `Cents`, `Fx16`, BAM + Q15 table); float/double/decimal banned by analyzer; determinism checklist (no Dictionary iteration, stable sort, no `GetHashCode`, no clocks or statics, culture-free text); tiers T1/T2; `ISimStateContributor` dump; in-memory recorder; 0-alloc tick after warm-up | SC OQ10; TR-sim-031..036, 045 | No |
| **ADR-0007** | Op queue, drain & replies | 64 B blittable `Op` (16 B header + ≤48 B payload); ring 4096 + reserve 64; drain ≤64/tick by `opSeq`; validate-then-mutate; reply layout + reason namespace (1–63 Sim, ≥64 systems); `busy` on full ring. **reqId resolution:** u32 header slot, u16 on the wire, dedupe + last-16 replay in the adapter, none in the Sim. | TR-sim-015..018, 020; consistency conflict "reqId width" | No |
| **ADR-0008** | Event log, replication classes & event delivery | Per-tick `EventLog` (cap 4096, reserve 256); **entry byte layout + payload ceiling (missing from the seed)**; pull cursors (≤8); `IEventSink.Deliver` once in phase 13; replication classes; two-tier overflow + masks; coalescing per call | TR-sim-021..024 | No |
| **ADR-0009** | InventoryOp pipeline | Op kinds; validation precedence; per-container `version`; batch bumps; event-before-reply on one stream; oldest-first unit removal; conservation check in `ITickEndCheck`; `INVENTORY_ASSERT`. Optimistic client (pending queue 16, `baseVersion`/`stale_layout`, 5 s timeout) is specified now and built at M0. | TR-inventory-001..006, 010..014, 019, 022 | No |
| **ADR-0010** | Content format, loading, ContentSet & StatSheet | JSONC under `content/` + generated `manifest.txt`; hand-written `Utf8JsonReader` parser; unit-suffix convention + `FieldSpec`; immutable `sealed record` defs in `Sim/Content`; 5-stage loader via `IContentSource`; `ContentHash` canonical encoding; StatSheet F1 (Int128, one rounding); `IStatWriter` for Perks/Stamps only; **M-1a `BuiltInContent` factory in the same types**; loader + validator CLI at M0 | TR-content-*, OQ-CDD-1; spikes S1–S4 for the Godot `ContentHost` | The `ContentHost` half is gated on S1–S4. The Sim half can be Accepted now. |
| **ADR-0011** | Network adapter boundary (Sim ↔ Godot multiplayer) | **Now (needed at M-1a for loopback):** the seam (`EnqueueOp`, `IEventSink`, `ISnapshotSource`, `IHostClock`, op codec per OpKind descriptor, loopback = remote codec, the Sim knows no peers). **Deferred to M0:** transport (SceneMultiplayer/ENet, channels, RPC ordering, auth `Hello`/`HelloAck`, comms channel 3). | SC OQ2, Grid OQ12, CDD S5; TR-sim-042..043 + all Network-section TRs | **Yes, transport half.** The seam half can be Accepted for M-1a. |

### Should have before the relevant system is built

**Status (v1.2, 2026-10-16):** ADR-0012, 0013, 0014, 0015, 0016, 0017, 0019 and 0026 are **Accepted**. ADR-0012's router core is accepted outright, and its Godot adapter rows stay spike-gated on OQ-IC-9. ADR-0026 is accepted for the M-1a `IWorldQuery` + street stub; its generation pipeline is the M1 revision. ADR-0018 and ADR-0020–0031 remain to be written.

| # | Title | Milestone | Decides | Gate |
|---|---|---|---|---|
| ADR-0012 | Input router core & Godot input adapter | M-1a | router in Client.Core; adapter stamps `_Input` arrivals; `InputFrame` Sim struct; `ui_*` never mutated; `FocusMode.None`; `DEVICE_ID_*` constants; CI lint on `Input.IsAction*`; two clocks | OQ-IC-9 input spike (needs the Godot .NET build) |
| ADR-0013 | Carrier kinematic solver & fixed-point geometry | M-1a | capsule-vs-tile/AABB solver; sub-steps ≤300 mm; trig LUT/BAM; tick-time input via `IRemoteCapsuleSource`/`ISupportTransformSource`; no Godot physics | — |
| ADR-0014 | Interaction framework & hold timing | M-1a | `IInteractable` registry + 2 m spatial hash; integer focus scoring + 4-tick dwell; hold counter in owner ticks; shared `holdEff`; cancel range + hysteresis; one-commit rule | — |
| ADR-0015 | Shared integer ballistic, sweep & flight resolution | M-1a | closed-form F2; sweep F4 on ADR-0013's `Sim/Geometry` primitives; static 4 m receiver grid (world solids via ADR-0026 `QueryStaticSolids`); receiver ordering; terminal states; preview calls the same function | — |
| ADR-0016 | ClientLayer, shared pure functions & presentation timing | M-1a | ClientLayer steps (`InputLatch`, `CarrierStep`, `Previews` with sub-steps Focus → DeliverPreview → ThrowPreview → EdgeResolve → HoldAdvance); `Evaluate`/F10/ballistic compiled once and used by host and client; "outcome never before domain event"; launch ghost; reach echo from `canStartHoldNext` | — |
| ADR-0017 | Mail table, destination registry & `IWalletGate` | M-1a | SoA mail table, `AddressId` packing, Mail-owned wallet port (implemented by Economy, a stub at M-1a), backlog queue shared with Inventory's drain | — |
| ADR-0018 | HUD composition, Client.Core.Ui seams & notification queue | M-1a (minimal) — **minimal slice drafted 2026-10-23: `docs/architecture/adr-0018-hud-composition-minimal.md`, Proposed, awaiting Evan** (wallet, time-only timer, deliver prompt; the rest and all accessibility deferred, Evan 2026-10-23) | `IHudState`/`IUiClock`/`IRoster`/`IMoneyFormatter`; cue record + table validated at load; CanvasLayer stack + MarkerLayer; two live-region Controls; dirty flags / pooling; no `DrawLine` | AccessKit checks (M4); Godot .NET build |
| ADR-0019 | Godot presentation scene architecture | M-1a (after the Godot .NET build) | `GameLoop` placement (autoload vs root), `ViewCatalog` keyed by def id, view-per-entity pooling, stretch mode/aspect + min window 1280×720 (HUD HN-18), Upgrade Project Files policy | OQ1, S4 |
| ADR-0020 | Transform replication, Teleport & host position history | M0 | client-authoritative movement; 15 Hz batched `Transforms[]`; `Teleport{seq}` reliable + ack; 9+3 tick ring; claim-and-corroborate; remote interpolation delay of 160 ms | multiplayer spike |
| ADR-0021 | Flight protocol & catch validation | M0 | `ThrowRelease`/`ThrowLaunched`/`FlightResolved`/`CatchArm` layouts and channels; `flightId` wrap; catch under client-authoritative movement; `FlightBatch` decision | multiplayer spike |
| ADR-0022 | JoinState, rejoin & resync | M0 | envelope, ≤64 KB chunks, per-system blocks, `ContainerChecksum` (order-independent, never `GetHashCode`), resync-pending high-water, `outcomeSeq` drop rule | multiplayer spike |
| ADR-0023 | Steam integration path | M0 | C# (Steamworks.NET / Facepunch) vs GDScript transport layer (spec 13 §3), lobby, relay, voice | Steam spike |
| ADR-0024 | Profile & settings persistence | M1 (minimal `user://input.json` at M-1a) — **M-1a session stats file slice Accepted 2026-10-23 by Evan: `docs/architecture/adr-0024-session-stats-file-m1a.md`** (the rest stays M1) | `user://` stores, schema versions + migration, ids as strings, input settings per machine vs roaming (OQ-IC-2), session stats/telemetry file | — |
| ADR-0025 | Inventory sort & backlog structures (narrowed by ADR-0009) | M1 | D3 sort algorithm, hints and memo; backlog per-footprint sub-queues; perf-budget verification. *(`mailIds` arena, outfeed head index, StackKey index and WorldItem expiry FIFOs — replacing the timing wheel — are decided in ADR-0009.)* | — |
| ADR-0026 | World generation pipeline, tile grid & routing graph | M1 (stub interface `IWorldQuery` + `M1aStreet` at M-1a: **Accepted** 2026-10-16) | server-generates-once + tables in JoinState (spec 13 §3), height field, lots/addresses | — |

### Can defer to implementation (record when the system is designed)

| # | Title | When |
|---|---|---|
| ADR-0027 | Belt lane model & replication (lane events + client visual sim + LaneChecksum) | M-1b |
| ADR-0028 | Enemy AI, routing graph use & wave scheduler | M-1b (scripted pulse) |
| ADR-0029 | Animation LOD driver & camera boom solver | M1 |
| ADR-0030 | Audio architecture (buses, voice stealing, positional vs 2D) | M1 |
| ADR-0031 | Accessibility live-region announcer (AccessKit) | M4 |

**Not an ADR:** the performance budget lives in `docs/architecture/perf-budget.md` (SC OQ5; to be written from TR-sim-037/038). It holds the per-phase p99 slices and the per-GDD budgets, which stay ADVISORY until a reference machine exists.

---

## Architecture Principles

**1. The Sim is the game; Godot is a view.**
- Every authoritative rule lives in `PerformativeMail.Sim`, which has zero Godot references.
- Presentation reads mirrors and events and sends requests. It never mutates the Sim.
- This keeps the host honest, a dedicated server possible and every rule unit-testable with `dotnet test`, today, without an engine.
- Dependencies point one way: Godot → Client.Core → Sim.

**2. Determinism by construction, not by testing alone.**
- **Integer math only:** mm, permille, ticks, cents.
- **Ordering:** explicit (opSeq, phase order, ascending ids).
- **Randomness:** named RNG streams.
- **Forbidden:** clocks, statics, hash-code ordering.
- **Enforcement:** analyzers and architecture tests, backed by golden vectors and canonical dumps (T1 same machine, T2 Windows + Linux).

**3. One code path.**
- Loopback uses the remote codec.
- The host and the client run the same pure functions (`Evaluate`, ballistic, `holdEff`, StatResolve), so a preview equals the outcome.
- Solo is a one-player co-op session.

**4. Content is data; the content set is immutable.**
- Defs and balance come from JSONC through one loader and one validator catalogue, into a process-lifetime `ContentSet` identified by its hash.
- A new perk or enemy needs no code unless it adds a behaviour primitive.
- Until the loader lands at M0, M-1a's hard-coded values live in the same types.

**5. Fixed budgets, zero allocation, and verify the engine before trusting it.**
- The tick has a phase-sliced budget and allocates 0 B after warm-up. Presentation has per-frame budgets.
- Any Godot 4.4–4.7 behaviour not in `docs/engine-reference/godot/` gets a spike before an ADR depends on it.

**6. Build the slice, freeze the interface.**
- Each milestone builds only its slice: M-1a binds phases 0–4, 8 and 12.
- Interfaces (all 14 phase ids, id classes, event classes, the adapter seam) exist from day one, so later systems slot in without rework.

---

## Open Questions

| ID | Question | Blocks | Resolution path |
|---|---|---|---|
| Q-ARCH-01 | **Godot .NET (mono) 4.7.1 build not installed.** It blocks OQ1 (GameLoop), OQ4 (ProjectReference / glob / export mapping), OQ-IC-9 (input), S1–S4 (content export) and all Presentation. | Presentation work; ADR-0001 Godot half, 0002 GameLoop half, 0012, 0018, 0019 | Install the Godot 4.7.1 .NET build (user-level, like the Godot standard build) before the first Presentation story; run the spikes; record them under `prototypes/` |
| Q-ARCH-02 | Which .NET runtime the Godot 4.7.1 .NET export loads, and the SDK minimum for 4.6/4.7 desktop builds. Both are NOT SOURCEABLE. Tests run on .NET 10, while the game may run on 8 or another version: low risk for the integer Sim, but perf numbers will not transfer. | perf gates | **Answered (ADR-0001, spike F3–F5):** editor/headless runs use the newest installed runtime; a self-contained ExportRelease bundles .NET 8.0.32 (partly verified). The export path itself (no templates) remains open → ADR-0019. |
| Q-ARCH-03 | `Directory.Build.props` (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `InvariantGlobalization`) will also apply to the Godot-generated csproj. Note that `InvariantGlobalization` only affects executables, so the Sim must still use culture-free APIs (analyzer). | Godot project generation | **Answered (ADR-0001, spike F4/F7).** |
| Q-ARCH-04 | reqId width / dedupe ownership. **Recommended:** a u32 slot in the Sim header, u16 on the wire, dedupe + last-16 reply replay in the adapter. Needs a one-line change to `simulation-core.md` R6/R10 and a note in Grid rule 8 at re-review. | ADR-0007, ADR-0011 | ADR-0007 records it; GDD owners align at re-review |
| Q-ARCH-05 | Godot 4.7.1 multiplayer behaviour (RPC thread, cross-method ordering on one channel, channel count, auth callbacks, `GetRemoteSenderId` on loopback/Steam, fragmentation). | ADR-0011 transport half, 0020–0023, Comms | Time-boxed multiplayer spike at the start of M0 (Grid OQ12, SC OQ2, CDD S5, comms spike list) |
| Q-ARCH-06 | Upload bandwidth exceeds spec 06's 8 kbps/client. Transforms alone are ~12% over, a thrower at the cap is ~8% over, and comms adds 0.25–7 kbps. Comms asks for ≥12 kbps. | Networking & Session GDD (M0) | Re-baseline in the Networking GDD; ADR-0020/0021 |
| Q-ARCH-07 | Content/Throwing leftovers: `throwSpreadMrad` (CD4 example, CV-THR-18), `cooldownSlackTicks` (CV-THR-16); `foundValueMultRatio` vs `foundValueMultPm`. | ADR-0010 field names | Content & Data re-review (hand-backs); not sprint-blocking |
| Q-ARCH-08 | Owner ratifications pending (no-dice throwing D1, exact preview colour D2, Rank-4 reroll stamp, per-door complaint cap). They do not change the architecture, but D1 removes all RNG from Throwing (ADR-0005 assumes no Throwing stream). | none architectural | Owner review of the decision log |
| Q-ARCH-09 | Where the Steam transport lives: C# or GDScript (spec 13 §3). | ADR-0023, ADR-0011 | Steam spike (M0) |
| Q-ARCH-10 | Reference machine for the perf gates. All ms budgets are ADVISORY until it exists (SC OQ6, HUD HN-19). | blocking perf gates at M0 | Name the machine in `perf-budget.md` before M0 |
| Q-ARCH-11 | GDDs not yet written whose interfaces are assumed: Run Structure, Economy, World Generation, Networking & Session, Health & Damage, Belt, Vehicles, Enemy AI, Persistence. M-1a uses stubs (`IWalletGate` stub, World stub street, `Run.Step` shift timer, Health stub at M-1b). | their epics | Just-in-time GDDs, one milestone ahead |
| Q-ARCH-12 | Content & Data and Simulation Core are Provisional and mid-review. Re-review could change ADR-0003/0007/0008/0010 inputs. | ADR acceptance | Write the ADRs from the current GDD text, then re-check them against the re-reviewed GDDs with `/propagate-design-change` |

---

## Appendix A — Technical Requirements Baseline

The baseline was extracted from 10 GDDs and holds **250 requirements**:

| GDD | TRs |
|---|---|
| inventory | 28 |
| input | 24 |
| movement | 26 |
| delivery | 24 |
| mail | 23 |
| throwing | 21 |
| hud | 21 |
| comms | 8 (frozen draft, headline only) |
| sim | 45 |
| content | 30 |

**Columns:**
- **Side:** S = Sim, P = Presentation, B = both.
- **ADR:** the primary planned ADR.
- **Status:** ❌ GAP for every row until that ADR is Accepted.

The IDs follow the TR-registry format (`TR-<system>-NNN`). `/architecture-review` imports them into the registry.

| Req ID | Requirement | Domain | Side | ADR |
|---|---|---|---|---|
| TR-sim-001 | Whole fixed ticks of 1/30 s; TickRateHz=30 code constant; no variable step | Core | S | 0002 |
| TR-sim-002 | Tick is uint32 from 0; differences in int64; first Tick() after Ready is tick 0 | Core | S | 0002 |
| TR-sim-003 | TickDriver is a Godot-free integer accumulator in Hosting/; takes timestamps, never reads a clock | Core | S | 0002 |
| TR-sim-004 | Catch-up cap maxCatchUpTicksPerFrame=4 (safe 3–8); excess dropped, never banked | Core | S | 0002 |
| TR-sim-005 | M0 wall-clock guard catchUpGuardMs=10 re-read after each tick; ≥1 tick always runs | Core | S | 0002 |
| TR-sim-006 | F2 accumulator invariant; Δ clamped to 1 s; freq 10^3..10^12; backwards clock counted | Core | S | 0002 |
| TR-sim-007 | Sim on Godot main thread; owner thread captured at Freeze(); Debug asserts | Core | S | 0002 |
| TR-sim-008 | Transport callbacks only enqueue; nothing applied inside a callback | Networking | S | 0002 |
| TR-sim-009 | Pause is a driver state; solo pause → Advance returns 0; SceneTree.Paused never used | Core | S | 0002 |
| TR-sim-010 | Exactly 14 PhaseIds 0–13 in fixed order; ITickEndCheck between 12 and 13 | Core | S | 0003 |
| TR-sim-011 | Unbound phase is a no-op; same-phase binders in composition order; new phases need R3 amendment | Core | S | 0003 |
| TR-sim-012 | SimWorldBuilder binds phases/ops/events/readers/contributors pre-Freeze; violations throw SimBuildException | Core | S | 0003 |
| TR-sim-013 | Composition root in Composition/; constructor injection only; reflection test on deps | Core | S | 0003 |
| TR-sim-014 | SimWorld (host) and ClientLayer (client shell) separate; host loopback uses same encoder | Core | S | 0003 |
| TR-sim-015 | Op = 64 B blittable: 16 B header (opSeq, actor, kind, reqId) + ≤48 B payload | Networking | S | 0007 |
| TR-sim-016 | Actor stamped by adapter from peer; System=0x00000001; opSeq monotonic, never reused | Networking | S | 0007 |
| TR-sim-017 | Op ring 4096 (pow2); full → false, no opSeq consumed, adapter replies busy; systemOpReserve 64 | Networking | S | 0007 |
| TR-sim-018 | Drain ≤64 ops/tick by opSeq; validate fully before mutation; every drained op recorded | Core | S | 0007 |
| TR-sim-019 | Only player/System requests use the FIFO; in-tick callers use sync I…Api in own phase | Core | S | 0003 |
| TR-sim-020 | Reply {reqId,result,reason u16,extra ≤24 B} after handler events; reasons 1–63 Sim; no reqId dedupe | Networking | S | 0007 |
| TR-sim-021 | One preallocated per-tick EventLog (4096, reserve 256); blittable no-string entries | Core | S | 0008 |
| TR-sim-022 | No sync subscribers; pull cursors ≤8; IEventSink.Deliver once in phase 13, copy-only | Core | S | 0008 |
| TR-sim-023 | Replication classes Internal/Replicated/OwnerOnly/Targeted; undeclared kind fails test | Networking | S | 0008 |
| TR-sim-024 | Two-tier overflow admission; overflowInfo masks; adapter resyncs exactly those | Networking | S | 0008 |
| TR-sim-025 | EntityId uint32 class<<24\|counter; classes 01–08; 09–EF reserved; F0–FF tests; never reused | Core | S | 0004 |
| TR-sim-026 | mailId separate uint32; PlayerSlot u8 0–7; slot freed in T reusable from T+1 | Core | S | 0004 |
| TR-sim-027 | PCG32 XSH-RR; Derive via Mix64/Fnv1a64; all unchecked | Core | S | 0005 |
| TR-sim-028 | Compile-time RngNames registry; v1 stream list; mail/mail.special/events indexed per shift | Core | S | 0005 |
| TR-sim-029 | NextInt(b)=(next32*b)>>32, one draw per call; no float RNG; owner-only handles | Core | S | 0005 |
| TR-sim-030 | Committed golden vectors (pcg32, Mix64, Fnv1a64, Derive) | Core | S | 0005 |
| TR-sim-031 | Integer-only numerics (mm, permille, ticks, Cents, Fx16, BAM+Q15); float/double/decimal banned | Core | S | 0006 |
| TR-sim-032 | Determinism checklist: no Dictionary iteration, stable sort, no Random/Guid/hash codes/time/threads/statics | Core | S | 0006 |
| TR-sim-033 | T1: identical canonical dump hash per tick given same inputs and logs | Save | S | 0006 |
| TR-sim-034 | T2: Windows+Linux x64 bit-identical carrier step, RNG, hashes, shared pure functions | Core | S | 0006 |
| TR-sim-035 | Canonical dump via ISimStateContributor (fixed order, LE); SHA-256 in tests only | Save | S | 0006 |
| TR-sim-036 | Tick() allocates 0 B after 300 warm-up ticks; blocking from M0 | Core | S | 0006 |
| TR-sim-037 | Host tick p99 5.0 / max 8.0 / median 3.0 ms; ClientLayer p99 0.3 ms; advisory until reference machine | Core | S | perf-budget |
| TR-sim-038 | Per-phase p99 slices summing 4.50 ms + 0.50 reserve | Core | S | perf-budget |
| TR-sim-039 | CI perf gate: p99 rank, N≥18,000, fail at 2× calibrated baseline | Core | S | perf-budget |
| TR-sim-040 | Always-on checks to fixed record ring; asserts compile out of Release; SimCounters | Core | S | 0001 |
| TR-sim-041 | Port interfaces (ISimPhase, IOpHandler, IEventSink, IHostClock, IMonotonicClock, IRngSource …) | Core | S | 0003 |
| TR-sim-042 | Sim knows no peers/accounts/channels/RPC/wall clock/RTT/Godot types; request ticks in host domain | Networking | S | 0011 |
| TR-sim-043 | Join snapshot after Deliver of joinTick; joiner buffers later batches | Networking | S | 0011 |
| TR-sim-044 | Lifecycle Constructed→Ready→Running→Stopped/Faulted; RequestStop via IRunControl in phase 12 | Core | S | 0003 |
| TR-sim-045 | Recorder stores ops + transform samples (in-memory M-1a, file M5) | Save | S | 0006 |
| TR-content-001 | Sim content is JSONC under content/, one dir per def type; YAML and .tres rejected | Data | S | 0010 |
| TR-content-002 | Generated content/manifest.txt drives loading; CI fails on drift | Data | S | 0010 |
| TR-content-003 | Hand-written Utf8JsonReader walk; duplicate/unknown keys, exponents, -0 rejected | Data | S | 0010 |
| TR-content-004 | Three data classes: Sim content (hashed), Presentation content, Player data (Persistence) | Data | S | 0010 |
| TR-content-005 | Unit-suffix convention; decimals stored as ms/mm/permille; money integer cents | Data | S | 0010 |
| TR-content-006 | Every numeric field declares a unit via code-side FieldSpec | Data | S | 0010 |
| TR-content-007 | Def id regex; sorted ordinal into DefIndex ushort (0xFFFF none) | Data | S | 0004 |
| TR-content-008 | DefIndex never persisted; on the wire only after hash match | Save | S | 0004 |
| TR-content-009 | StringId = (namespace, DefIndex); run-time strings interned per run | Data | S | 0004 |
| TR-content-010 | Defs immutable sealed record class in Sim/Content; no floats; no Godot Resource | Data | S | 0010 |
| TR-content-011 | Defaults always neutral; weightClass required on ItemDef and MailKindDef | Data | S | 0010 |
| TR-content-012 | Closed enums with explicit never-reused values; hand-written parse tables | Data | S | 0010 |
| TR-content-013 | Balance keys in content/balance/<owner>.jsonc; BalanceKey registry; SimSettings in balance/sim.jsonc | Data | S | 0010 |
| TR-content-014 | Stat closed enum with StatDef rows; design clamps are balance keys | Data | S | 0010 |
| TR-content-015 | StatResolve F1 in Int128, one rounding, order-independent, k ≤ 6 | Core | S | 0010 |
| TR-content-016 | IStatSheet read anywhere; IStatWriter only Perks and Stamps; meta never pushes modifiers | Core | S | 0010 |
| TR-content-017 | Sheets are derived caches; team + per-slot sheets; source lists replicated | Networking | S | 0010 |
| TR-content-018 | RuleFlag enum; timed effects via transientAdd; sheet has no timers | Core | S | 0010 |
| TR-content-019 | Explain() returns resolved/unclamped/sources; card numbers from placeholders | Data | S | 0010 |
| TR-content-020 | 5-stage load pipeline → one immutable ContentSet passed into new SimWorld | Core | S | 0010 |
| TR-content-021 | Loader reads via IContentSource; Sim never reads res:// | Core | S | 0010 |
| TR-content-022 | Validator catalogue 84 active rules (CV-<AREA>-NN); errors block the build | Data | S | 0010 |
| TR-content-023 | tools/ContentValidator console; flags, exit codes 0–3, fixture per rule | Data | S | 0010 |
| TR-content-024 | ContentHash = SHA-256 over canonical encoding; encoder and managed SHA-256 both in the Sim, at load time (ADR-0010 §6, ADR-0006) | Save | S | 0010 |
| TR-content-025 | Join handshake compares 32-byte hash with refusal precedence; content never transferred | Networking | B | 0010 |
| TR-content-026 | Debug hot reload (F6): errors keep old set; M0 reload-and-restart; in-place swap M4 | Data | B | 0010 |
| TR-content-027 | Presentation ViewCatalog keyed by def id → DefIndex arrays; placeholder on missing view | Data | P | 0010 (contract) / 0019 (implementation) |
| TR-content-028 | Boot parse+validate+hash ≤100 ms CPU; bind ≤25 ms; sheet rebuild ≤0.2 ms; Read 0 B | Core | S | 0010 |
| TR-content-029 | No mod support; hash is a mismatch detector, not security | Data | S | 0010 |
| TR-content-030 | MetaProfile/settings owned by Persistence; content ids as strings; unknown dropped + logged | Save | P | 0010 |
| TR-inventory-001 | All ops (player and automation requests) enter one FIFO drained in Inputs.Apply; loopback same path | Core | S | 0009 |
| TR-inventory-002 | Per-container uint32 version +1 per mutating op; batch bumps once per container | Core | S | 0009 |
| TR-inventory-003 | Entry: uint32 entryId, uint8 count, ascending mailIds[]; footprint w×h, rotation | Data | S | 0004 |
| TR-inventory-004 | StackKey=(mailKind,addressId,flags); effMax evaluated at op time; ≤255 | Data | S | 0009 |
| TR-inventory-005 | Take-k removes lowest mailIds (oldest-first); payment per mailId | Core | S | 0009 |
| TR-inventory-006 | Fixed validation precedence chain of reason codes | Core | S | 0009 |
| TR-inventory-007 | AutoPlace passes M/U/R; amortised O(1) non-full-stack index | Core | S | 0009 |
| TR-inventory-008 | Deterministic consolidating first-fit-decreasing sort, total-order comparator | Core | S | 0025 |
| TR-inventory-009 | Intake sort ≤8,500 probes; memoised repeat O(1) | Core | S | 0025 |
| TR-inventory-010 | Intake outfeed by arrivalSeq; PeekOutfeed/TakeUnit ≤4 entries amortised | Core | S | 0009 |
| TR-inventory-011 | Backlog drain ≤32 entries/tick, ≤8,000 probes, 0 when idle | Core | S | 0009 |
| TR-inventory-012 | O(1) conservation counters checked each tick; INVENTORY_ASSERT compiled out of Release | Core | S | 0009 |
| TR-inventory-013 | WorldItem cap 512, expiry FIFOs, lossless mail eviction | Core | S | 0009 |
| TR-inventory-014 | Timers as absolute ticks (bag 900, WorldItem 9000, water 300); expiry after Inputs.Apply | Core | S | 0009 |
| TR-inventory-015 | Integer-only weight→speed (permille, one division); weightPoints int16 | Core | S | 0006 |
| TR-inventory-016 | Inventory tick share ≤1.0 ms median / 2.0 p99 / 3.0 worst (advisory) | Core | S | 0025 |
| TR-inventory-017 | Zero managed allocation on automation path | Core | S | 0006 |
| TR-inventory-018 | Managed heap ≤1 MB for containers + 512 WorldItems; 2,800-entry backlog ≤150 KB | Core | S | 0025 |
| TR-inventory-019 | Reliable ordered delivery; events, replies, snapshots on one channel, event before reply | Networking | S | 0009 |
| TR-inventory-020 | Intake/Depot non-openers get GroupCounts only; ≤40 B/tick/peer | Networking | S | 0011 |
| TR-inventory-021 | Bursts ≤12 KB paced ≤800 B/tick; 24 KB high-water → resync-pending | Networking | S | 0011 |
| TR-inventory-022 | Optimistic client: pending queue 16, temp ids, dependsOn, baseVersion, 5 s timeout | Networking | S | 0009 |
| TR-inventory-023 | ContainerChecksum every 2 s, order-independent, never GetHashCode | Networking | S | 0022 |
| TR-inventory-024 | JoinState/RejoinState inventory block sizes; ≤64 KB chunks on bulk channel | Networking | S | 0022 |
| TR-inventory-025 | Sender from transport peer, never client playerId; malformed → bad_request, no exception | Networking | S | 0007 |
| TR-inventory-026 | ContainerDef/ItemDef fields and StatSheet keys for inventory | Data | S | 0010 |
| TR-inventory-027 | Grid panel one custom-drawn Control; 0 B/frame; feedback via live-region Control | UI | P | 0019 |
| TR-inventory-028 | SHA-256 state dump equal after 50,000-op log on two Sim instances | Core | S | 0006 |
| TR-input-001 | Router core plain C#, fed abstract raw-input records + fake clock; Godot adapter is Presentation | Input | B | 0012 |
| TR-input-002 | All gameplay reads via router; direct Input.IsAction* outside adapter is a CI lint failure | Input | P | 0012 |
| TR-input-003 | RouterState tuple; live predicates as data; reachable-state golden list | Input | B | 0012 |
| TR-input-004 | Per-press state machine: latch at press, mute until release on state change, ordered edges | Input | B | 0012 |
| TR-input-005 | Action map: stable ids, grammar, predicate, defaults, flags, owner; alias table | Data | B | 0012 |
| TR-input-006 | Conflict validator over reachable states; most-specific chord wins | Input | B | 0012 |
| TR-input-007 | ui_* InputMap never mutated; game Controls FocusMode=None; router consumes in _Input | Input | P | 0012 |
| TR-input-008 | Events arrival-stamped in _Input (no InputEvent timestamp); edges attached to first catch-up tick | Input | P | 0012 |
| TR-input-009 | InputFrame Sim struct: tick, int16 move, absolute look, held bits, ordered edges, steps | Input | S | 0012 |
| TR-input-010 | Two clocks: Sim ticks for holds/range; monotonic integer ms for repeat/drag/glyph/aim | Core | B | 0012 |
| TR-input-011 | holdEff single shared int64 Sim function, 6-tick floor, holdCeil clamp | Core | S | 0014 |
| TR-input-012 | Hold cancel range 2900 mm with hysteresis = PC incumbentSlackMm | Core | S | 0014 |
| TR-input-013 | Server-validated verbs bypass the frame as reliable requests; no hold-duration field | Networking | B | 0011 |
| TR-input-014 | MP pause is a local overlay, never SceneTree.Paused; Sim keeps ticking | Core | B | 0002 |
| TR-input-015 | KBM identified by DEVICE_ID_KEYBOARD/MOUSE; hot-plug via joy_connection_changed (UNVERIFIED) | Input | P | 0012 |
| TR-input-016 | ignore_joypad_on_unfocused_application=true; per-device axis cache | Input | P | 0012 |
| TR-input-017 | Deadzone/trigger/repeat/step caps in integer math; one step per rendered frame | Input | B | 0012 |
| TR-input-018 | Router+gatherer ≤0.1 ms/frame, ≤0.3 ms at 1000 Hz burst, 0 B steady | Core | P | 0012 |
| TR-input-019 | Preallocated 4096-entry event-log ring shared with UI event log | Core | B | 0012 |
| TR-input-020 | settings.input schema versioned, overrides-only; M-1a user://input.json | Save | P | 0012 |
| TR-input-021 | Replay corpus (200 seeded + 10 adversarial) asserts zero unauthored verbs | Core | S | 0006 |
| TR-input-022 | Focus loss: release-with-cancel, zero movement, mute, swallow recapture click, solo auto-pause | Input | P | 0012 |
| TR-input-023 | One-commit rule for deliver/deconstruct/throw; pendingCommitTimeout 500 ms | Input | S | 0012 |
| TR-input-024 | One local carrier per machine; all devices OR-ed; duplicate pad edges within 20 ms collapse | Input | P | 0012 |
| TR-movement-001 | Deterministic kinematic capsule solver in Sim; no Godot physics; no floating point | Physics | S | 0013 |
| TR-movement-002 | mm int32, mm/s, permille, ticks; int64 before first multiply; one final division | Core | S | 0006 |
| TR-movement-003 | Client-authoritative movement; host authors Dead/Frozen bodies only | Networking | S | 0020 |
| TR-movement-004 | Tick-time inputs only via IRemoteCapsuleSource/ISupportTransformSource | Core | S | 0013 |
| TR-movement-005 | Fixed-point trig LUT; terrain from World Gen height field (2 m, bilinear, int64) | Physics | S | 0013 |
| TR-movement-006 | Sub-steps ≤300 mm, ≤3 slides, step-up/snap 400 mm, slope 700‰, ≤4 sub-steps/tick | Physics | S | 0013 |
| TR-movement-007 | Fixed-step driver with catch-up cap; local extrapolation ≤1 tick; no engine physics interpolation | Core | B | 0002 |
| TR-movement-008 | Transforms 16 B up, 17 B/entry batched down; 15 Hz/4 Hz; ≤19 kbps down, ≤6.7 kbps up | Networking | B | 0020 |
| TR-movement-009 | Teleport reliable with wrap-safe teleportSeq, ack/resend | Networking | B | 0020 |
| TR-movement-010 | Host range check via 9+3 tick history ring, claim at press, position_stale at 30 ticks | Networking | S | 0020 |
| TR-movement-011 | IInteractable registry with 2 m spatial hash; one verb per anchor; integer focus, 4-tick dwell | Core | S | 0014 |
| TR-movement-012 | Hold counter in owner Sim ticks; one request at held tick; no host hold timer | Core | S | 0014 |
| TR-movement-013 | Life states host-owned with absolute-tick timers | Core | S | 0014 |
| TR-movement-014 | Knockdown chain in phase 11 in fixed order, emits CarrierKnockedDown | Core | S | 0014 |
| TR-movement-015 | Frozen on disconnect 120 s; reconnect via account→peer map | Networking | S | 0020 |
| TR-movement-016 | PvP push: own capsule only, half overlap, cap 60 mm/tick | Physics | S | 0013 |
| TR-movement-017 | Support carry ≤12,000 mm/s; roof position in vehicle-local frame | Physics | S | 0013 |
| TR-movement-018 | Water depth from height field; swim hysteresis 900/700 mm | Physics | S | 0013 |
| TR-movement-019 | ContentValidator enforces 12 cross-key movement rules | Data | S | 0010 |
| TR-movement-020 | Balance keys and StatSheet stats for movement; KitDef.standardIssueItem | Data | S | 0010 |
| TR-movement-021 | Owner CarrierStep+focus p99 ≤0.10 ms; host ≤0.20 ms/tick; 0 B/tick | Core | S | perf-budget |
| TR-movement-022 | Golden per-tick transform hash equal on Windows and Linux; Sim references no Godot | Core | S | 0001 |
| TR-movement-023 | Remote interpolation 160 ms delay, 100 ms extrapolation cap, snap on teleport/6 m | Rendering | P | 0020 |
| TR-movement-024 | Camera boom solved in C# against Sim height field/AABBs; dither fade | Rendering | P | 0029 |
| TR-movement-025 | One AnimationTree per character, ≤4 layers, 48 characters ≤1.0 ms | Rendering | P | 0029 |
| TR-movement-026 | animState uint8 bit layout; JoinState public block ≈30 B/player | Networking | B | 0020 |
| TR-delivery-001 | One Deliver IInteractable per destination at run start; priority 2; never deregistered | Core | S | 0014 |
| TR-delivery-002 | Ordered 6-reason availability; deliver.pending derived from pending queue | Core | S | 0014 |
| TR-delivery-003 | Deliver preview pure function every focused tick in ClientLayer Previews phase | Core | S | 0016 |
| TR-delivery-004 | DeliverPreviewTracker 6-tick settle counts Sim ticks | Core | S | 0016 |
| TR-delivery-005 | Per-tick client order: events/replies → focus → preview → resolve Interact edge | Core | S | 0016 |
| TR-delivery-006 | Press-tick preview recompute; hold latches (dest,branch,entry,holdTicks) | Core | S | 0016 |
| TR-delivery-007 | ShouldCancel/GetHoldTicks hooks; branch or selection change cancels | Core | S | 0014 |
| TR-delivery-008 | deliver_matching: MATCH omits selectedEntryId; 24/28 B packed incl. claimed position (revised 2026-10-16, ADR-0011 erratum) | Networking | S | 0011 |
| TR-delivery-009 | holdStartTick in host domain via IHostClock; host clamps window | Networking | S | 0011 |
| TR-delivery-010 | Host check: mailboxId is a registered Deliver anchor; no throttle | Networking | S | 0011 |
| TR-delivery-011 | Idempotent reqId u16; duplicate returns original reply | Networking | S | 0007 |
| TR-delivery-012 | Client Evaluate/F10 equal host results over 1,000 seeded pairs | Core | S | 0016 |
| TR-delivery-013 | Preview money in integer cents, single RH per unit | Core | S | 0016 |
| TR-delivery-014 | Entry scan order cursor→hotbar→inventory→backpack equals host consumption | Core | S | 0016 |
| TR-delivery-015 | holdEff with Quick Hands insertPm 500; DeliverInsertPm clamp 0…750 | Core | S | 0014 |
| TR-delivery-016 | No host state, no replicated state; JoinState 0 B | Networking | S | 0011 |
| TR-delivery-017 | Hand beat once per hold keyed (actor,dest,applyTick) from DeliveryOutcome | Networking | P | 0016 |
| TR-delivery-018 | Reach echo starts only when Sim-published canStartHoldNext was true | Rendering | P | 0016 |
| TR-delivery-019 | Prompt content as data from Sim; HUD renders; localisation keys, no concatenation | UI | B | 0018 |
| TR-delivery-020 | Live-region announcements policy; accessibility_live API unsourced | UI | P | 0018 |
| TR-delivery-021 | Race suite: no misdeliver for MATCH-latched hold across ticks ±2 and RTTs; 10,000-seed soak | Core | S | 0006 |
| TR-delivery-022 | Telemetry latchedBranch; handShare ≥20% | Data | S | 0024 |
| TR-delivery-023 | Mailbox flap separate node within 150-tri budget; reach layer scrubbed by hold progress | Rendering | P | 0019 |
| TR-delivery-024 | Visual-only lunge ≤0.8 m via rig offset, never feeds Sim | Rendering | P | 0019 |
| TR-mail-001 | All money/permille/tick math integer; int64 intermediates; one RH per formula | Core | S | 0006 |
| TR-mail-002 | Per-shift manifest pure function of (seed, shift, profile, addresses); stream mail | Core | S | 0005 |
| TR-mail-003 | Fixed RNG draw count; NextInt without rejection loop | Core | S | 0005 |
| TR-mail-004 | Separate mail.special stream so re-rolls never move Lost Parcels | Core | S | 0005 |
| TR-mail-005 | Evaluate pure, 0-alloc, same code compiled into client for prompt and arc colour | Core | S | 0016 |
| TR-mail-006 | Server-only SoA mail table keyed by mailId; packed AddressId | Data | S | 0017 |
| TR-mail-007 | Wallet via Mail-owned IWalletGate; no Mail→Economy assembly/namespace reference | Core | S | 0017 |
| TR-mail-008 | One consume routine for hand/throw/feeder/NPC in fixed tick order | Core | S | 0017 |
| TR-mail-009 | Backlog ordered queue O(1) append; drain owned by Inventory; backlogUnits u16 | Data | S | 0017 |
| TR-mail-010 | Complaint token buckets int64 ticks×permille, elapsed clamped | Core | S | 0017 |
| TR-mail-011 | Feeder throttle integer credit; stagger feederId mod 15 | Core | S | 0017 |
| TR-mail-012 | No wall-clock reads; pause freezes buckets/windows/credits | Core | S | 0006 |
| TR-mail-013 | Closing grace by action start tick (host domain); closeGraceTicks 90 | Core | S | 0017 |
| TR-mail-014 | DeliveriesState reliable-ordered event, not on 5 Hz snapshot | Networking | S | 0011 |
| TR-mail-015 | DeliveryOutcome one event per op ≤19 B, canonical encoding; ≤16 B/tick typical | Networking | S | 0008 |
| TR-mail-016 | Value hint ≤5 runs per entry; client recomputes payout | Networking | S | 0011 |
| TR-mail-017 | JoinState ≤72 B; outcomeSeq drop rule on rejoin | Networking | S | 0011 |
| TR-mail-018 | Mail content defs loaded and validated | Data | S | 0010 |
| TR-mail-019 | Mail ≤1.0 ms p99 / 2 ms max; manifest ≤0.5 ms; 0 alloc hot paths (advisory) | Core | S | 0017 |
| TR-mail-020 | Idle tick touches only feeder credits; scans no rows | Core | S | 0017 |
| TR-mail-021 | MailShiftStats ≤2 KB at Payday, paced ≤800 B/tick | Networking | S | 0011 |
| TR-mail-022 | Presentation caps: residents, envelope pool, flashes, audio voices | Rendering | P | 0019 |
| TR-mail-023 | Host-authoritative payment/complaint; client F10 display-only | Networking | S | 0011 |
| TR-throwing-001 | Host-authoritative closed-form integer ballistic; O(1) p(k) | Physics | S | 0015 |
| TR-throwing-002 | No RNG in throwing; future randomness via registered stream by launch ordinal | Core | S | 0005 |
| TR-throwing-003 | Preview calls same Sim function as host; identical over 10,000 throws | Core | S | 0015 |
| TR-throwing-004 | Floor-based RH, CeilDiv/FloorDiv, isqrt; never truncating / on signed values | Core | S | 0006 |
| TR-throwing-005 | Swept segment test per tick per flight (≤90 ticks); slab/circle/cylinder/sphere/heightfield | Physics | S | 0015 |
| TR-throwing-006 | Receiver resolution ordering; flights in creation order | Physics | S | 0015 |
| TR-throwing-007 | Broad phase bounds \|R\|≤65536 mm; static 4 m grid | Physics | S | 0015 |
| TR-throwing-008 | Throwing.Step phase 4; 0.15 ms p99; 0 alloc; 16 flights + 8 catchers | Core | S | 0015 |
| TR-throwing-009 | FlightRecord snapshot fields; flightId wrap-safe u16 | Data | S | 0004 |
| TR-throwing-010 | Fixed 9-check host validation; claim-and-corroborate origin | Networking | S | 0021 |
| TR-throwing-011 | Leaky-bucket cooldown; team cap ≤48 flights/s | Networking | S | 0021 |
| TR-throwing-012 | Catch arm window host-tick based; contact over 9-tick history | Networking | S | 0021 |
| TR-throwing-013 | Exactly one terminal state per flight; conservation through flush/disconnect | Core | S | 0015 |
| TR-throwing-014 | Message sizes ThrowRelease 25 B, ThrowLaunched 30 B, FlightResolved 18 B, CatchArm 1 B | Networking | S | 0021 |
| TR-throwing-015 | ≈66 B per flight per peer; budget pressure at cap | Networking | S | 0021 |
| TR-throwing-016 | Flights 0 B JoinState; remotes render at hostTickEst−3, fast-forward via F2 | Networking | P | 0016 |
| TR-throwing-017 | Aim preview ≤0.3 ms/frame, 0 alloc, screen-space dashed ribbon | Rendering | P | 0019 |
| TR-throwing-018 | Launch ghost on release frame; outcome beats never before domain event | Rendering | P | 0016 |
| TR-throwing-019 | ThrowDef/BalanceConfig knobs with validator rules | Data | S | 0010 |
| TR-throwing-020 | Telemetry to session stats file only | Save | S | 0024 |
| TR-throwing-021 | Spin from flightId; MultiMesh bundles; hidden beyond 35 m | Rendering | P | 0019 |
| TR-hud-001 | HUD Presentation-only; no gameplay state; Sim has no reference to it | UI | P | 0018 |
| TR-hud-002 | Plain-C# UI core xUnit-testable via IUiClock/IRoster/IHudState/IMoneyFormatter + Godot view | UI | P | 0018 |
| TR-hud-003 | HUD reads only via IHudState ordered by (sourceTick, opId); loopback from M-1a | UI | P | 0016 |
| TR-hud-004 | Timer from IHostClock.renderSimTimeMs; HUD runs no clock | Core | P | 0016 |
| TR-hud-005 | Timer monotone within (phaseId, deadlineTick); text written on change only | UI | P | 0018 |
| TR-hud-006 | Every notification is a cue record with id/int args, never preformatted strings | UI | P | 0018 |
| TR-hud-007 | Cue table validated at load (CV-HUD-01..05); missing key is load error | Data | P | 0010 |
| TR-hud-008 | CI: every pill/toast cue has a paired world cue | UI | P | 0018 |
| TR-hud-009 | Toast lane: M-1a 1 slot; M0+ 2 visible + 4 queued, deterministic eviction | UI | P | 0018 |
| TR-hud-010 | Merge predicate on UI-clock windows; raw events always logged | UI | P | 0018 |
| TR-hud-011 | UI event log ring 1024 (M0), plain list M-1a; shared records | UI | P | 0018 |
| TR-hud-012 | All money text via one IMoneyFormatter; lint bans currency literals | UI | P | 0018 |
| TR-hud-013 | Screen-space MarkerLayer above post-processing; 41 markers at roster 8 | Rendering | P | 0018 |
| TR-hud-014 | Flash governor ≤3/s real clock, ≤2 Hz per element; one animator | UI | P | 0018 |
| TR-hud-015 | 1080-reference px scaled by height; text ≥18 px up to 200%; min 1280×720; stretch undecided | UI | P | 0018 |
| TR-hud-016 | 0 alloc over 10,000 idle frames, pooled rows, atlas, no DrawLine; ≤0.8 ms, ≤60 draws (proposed) | Rendering | P | 0018 |
| TR-hud-017 | Two live-region Controls; schema M-1a, announcer M4 | UI | P | 0018 |
| TR-hud-018 | History ring 50 raw events with snapshots | Save | P | 0018 |
| TR-hud-019 | Settings consumed from Persistence; HUD owns no persistence | Save | P | 0024 |
| TR-hud-020 | hud.tuning data, hot-reload in debug; cue table in shared validator library | Data | P | 0010 |
| TR-hud-021 | Deterministic reading-time lifetime F1 incl. no-space scripts | UI | P | 0018 |
| TR-comms-001 | Dedicated reliable-ordered channel 3 (channel count fixed at connect; spike) | Networking | S | 0011 |
| TR-comms-002 | ChatMessage/Ping/Emote/PingPurge families; host-originated relay | Networking | S | 0011 |
| TR-comms-003 | Host derives senderSlot from RPC peer; shape-only validation; drop not kick | Networking | S | 0011 |
| TR-comms-004 | ≤10 B/tick/peer sustained; 32 KB high-water drop-oldest; commsSeq gaps | Networking | S | 0011 |
| TR-comms-005 | Host resolves ping cap/refresh deterministically; emits pingId + expiresTick | Core | S | 0011 |
| TR-comms-006 | JoinState replays chat by byte budget + ≤16 pings; buffer above snapshot | Networking | S | 0011 |
| TR-comms-007 | Ingress sanitiser (NFC, strip controls/bidi, grapheme caps) with fixed test vectors | Core | P | 0018 |
| TR-comms-008 | Local per-player mute by account id; TypingState on ch3 | Data | P | 0018 |

**Requirement counts by domain:** Core 105 · Networking 48 · Data 34 · UI 17 · Input 14 · Rendering 12 · Physics 10 · Save 10 (total 250).

**Requirement counts by side:** Sim 185 · Presentation 48 · Both 17.
