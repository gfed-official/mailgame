# 06 — Multiplayer and Netcode

1–8 players, online co-op, server-authoritative. This chapter defines the authority model, transport, the tick and snapshot model, per-system replication strategy, lobby and session flow, join-in-progress, disconnect handling, and bandwidth budgets.

> **Superseded in part by chapter 13 §3.** This chapter describes a studio-grade stack (client prediction with reconciliation, rewind lag compensation, delta-compressed snapshots, interest management, hand-written serialisation). For a friends-only v1 the decision is: server-authoritative *state* (inventory, economy, belts, enemies, run), **client-authoritative movement and aim**, Godot's high-level multiplayer API, no interest management, no delta compression, no anti-cheat. The sections below remain as the reference design for a later hardening pass.

## 1. Authority model

```
Clients (1–8)                          Authoritative Server

  predicted local movement   --inputs 30 Hz, action RPCs-->   input and action validation

  snapshot interpolation     <--snapshots 20 Hz, interest---   fixed tick sim 30 Hz --> world state

  UI / inventory requests    <--reliable events--------------  belt insert/remove, deliveries, damage, phase

  visual belt sim
```

- One authoritative simulation runs on the server: either the hosting player's process (listen server; the host's client talks to it in-process) or a headless dedicated export.
- Clients send inputs (movement, look, action buttons) every tick and action requests (move item, place construct, buy, pick perk) as reliable RPCs.
- The server owns every gameplay value: positions of players and enemies, inventories, wallet, construct state, belt contents, run phase, RNG.
- Clients predict only their own character movement and a few cosmetic effects. Everything else is interpolated from server state or applied from server events.
- The host has no gameplay advantage beyond zero latency; the listen server does not trust the host's client any more than others (same validation path) so the code stays honest and a dedicated server is a drop-in.

## 2. Transport

- Godot ENet (reliable UDP with channels) as the primary transport. Channels: 0 unreliable-unordered (inputs, snapshots), 1 reliable-ordered (events, RPCs), 2 reliable-ordered bulk (join state, world deltas).
- Steam Datagram Relay for the Steam build, giving NAT traversal and lobby discovery. This is a separate MultiplayerPeer implementation, not a wrapper around ENet (ENet cannot be routed through SDR). Direct IP is supported for LAN and dedicated servers.
- Protocol version is a 32-bit hash of the message schema plus the content data hash; mismatch is rejected at connect.
- Max players 8 plus 1 reserved slot for a spectator/dedicated-server admin (deferred).

## 3. Tick and time

| Parameter | Value |
|---|---|
| Server simulation tick | 30 Hz (33.3 ms), fixed step |
| Client input send rate | 30 Hz, each packet contains the last 3 inputs (redundancy) |
| Snapshot send rate | 20 Hz per client (every 1.5 ticks; alternates 1/2 tick gaps) |
| Client render | Unlocked; interpolation buffer 100 ms (3 snapshots) |
| Clock sync | Client estimates server tick via RTT/2 ping every 2 s; run clock UI derives from server tick |

Simulation order per server tick: apply inputs → player movement → vehicles → enemies and NPC drivers → automation (belts, pipes, sorters, inserters) → mail spawn → destinations/economy → combat resolution → run state machine → build snapshot and flush events.

## 4. Replication strategy by system

Each system chooses one of four strategies:

- **S** Snapshot: state fields serialized at 20 Hz for entities within interest; delta-compressed against the last acked snapshot; unreliable.
- **E** Event: reliable-ordered message emitted when something happens; clients apply deterministically.
- **R** Request/validate/apply: client sends a request, server validates and applies, resulting state arrives via S or E. Client may show an optimistic preview tagged with the request id and roll back on rejection.
- **D** Derived from seed: computed locally from the seed and never sent.

| System | Strategy | Detail |
|---|---|---|
| World terrain, lots, addresses, roads, resources | D | Seed + archetype + worldHash check. Deltas (node depletion, terrain flattening) are E. |
| Player movement | S with local prediction | Client predicts from its own inputs; server sends authoritative position + last processed input tick; client reconciles and replays. Others are interpolated. Position quantised to 1 cm, yaw to 0.5°. |
| Player animation, held item, stance | S | Small enum fields in the player snapshot. |
| Vehicle movement | S with driver prediction | Driving client predicts; server simulates and corrects. Non-drivers interpolate. Server clamps speed to vehicle max × 1.1 to bound cheating. |
| Enemies | S (interest-managed) | Position, yaw, anim state, HP as uint8 percent. No prediction. Spawn/despawn/death are E. |
| NPC drivers and boats | S (interest-managed) | Same as vehicles without driver prediction. |
| Inventories (all containers) | E + R | Every container mutation is an InventoryOp event (place, remove, move, stack, split, sort-result) with a container version counter. Clients only receive events for containers they have open or own (player containers, plus the external container they are viewing). Opening a container triggers a full state send. |
| Wallet, complaint meter, quota progress | S (global, low rate 5 Hz) | Tiny global block in every 6th snapshot. |
| Constructs (placement, removal, config) | E + R | PlaceConstruct, RemoveConstruct, ConfigureConstruct (sorter filters, routes) are requests; server emits the confirmed event to all clients. Construct HP is S (uint8 percent) only while damaged and within interest; ruin transitions are E. |
| Belts and pipes (items in transit) | E + client visual sim | Server sends LaneInsert(segmentId, lane, itemKind, addressColour, positionAtTick) and LaneRemove events. Clients advance items locally at the replicated segment speed. Every 2 s per visible segment the server sends a LaneChecksum (item count and a hash of positions quantised to 0.25 m); mismatch triggers a full LaneState resend for that segment. Belt contents are cosmetic on the client: only the server decides when an item reaches an endpoint. |
| Sorter/inserter internal buffers | E when viewing | Sent as a container (see inventories) only while a player has the construct's UI open. |
| Mail spawn | E | MailSpawned batch event into the Intake container (which follows inventory rules). Mail ids are server-allocated sequentially so clients can pre-render labels. |
| Deliveries and misdeliveries | E | Delivered(mailId, destId, paid), Misdelivered(mailId, destId, penalty); drives VFX, sounds, feed, stats. |
| Combat hits | E + S | Damage events reliable; HP percent in snapshot. Ranged shots are client-predicted VFX, server-validated hitscan. |
| Run state (phase, deadline tick, shift) | E | PhaseChanged with payload (shop offer, draft cards, results). Also included in the join state. |
| Chat, pings, emotes | E | Text chat limited to 200 chars, rate-limited 3/s. Map pings are E with a 1/s rate limit. |
| Voice | — | Proximity voice chat via Steam Voice; see chapter 13 §4.2. |

There is no Power grid replication. Constructs always run at full rated speed on the server; clients do not receive supply/demand or brownout status.

### 4.1 Interest management

- Each client has an interest set: entities within 150 m of its player (or vehicle) plus everything flagged global (players, vehicles with a player inside, constructs under attack, the PO).
- Enemies and NPC vehicles outside interest are not replicated at all; when they enter interest the server sends a full entity state (E) before including them in snapshots.
- Belt segments are replicated only when the client has any part of the segment within interest. Entering interest triggers a LaneState full send for that segment.
- Interest updates are evaluated every 0.5 s per client.

### 4.2 Delta compression and bandwidth

- Snapshots are delta-compressed against the last acked snapshot per client (bitmask of changed fields per entity), then packed. Player entity ≈ 18 bytes changed per tick typical; enemy ≈ 12 bytes.
- Target budget per client at 8 players with a raid of 40 enemies in interest and 30 belt segments: ≤ 40 kbps down, ≤ 8 kbps up. Worst case (all 8 players in one factory during a shift-5 finale) ≤ 80 kbps down.
- Dedicated server budget: 8 × 80 kbps = 640 kbps up worst case.

### 4.3 Lag compensation

- Melee: server checks the target within an expanded hit volume (+0.3 m) using the target's position at serverTick − attackerRTT/2 (rewind buffer of 250 ms).
- Mailbox interact: the 0.4 s hold is timed on the client and confirmed by the server; the server accepts the delivery if the player was within 3 m of the mailbox at any tick during the last 300 ms.
- Item moves: optimistic UI; failure reverts within one RTT with a soft "clunk" sound rather than an error dialog.

## 5. Lobby and session flow

1. Host creates lobby (archetype, stamps, seed, maxPlayers).
2. Client joins (protocol hash, profile summary, kit choice); server replies with LobbyState (players, settings, availableUnlocks).
3. Host starts run; server generates world.
4. Server sends JoinState (seed, worldHash, deltas, run state, containers).
5. Client generates world locally, verifies hash, replies Ready.
6. Server sends PhaseChanged(Prep).

- Lobby state: player list (id, name, kit, cosmetics, ready), settings (archetype, stamps, seed, friends-only/public), host id.
- Only the host may change settings and start. Available archetypes and stamps are the union of the host's unlocks; each player's kit choice is limited by their own profile.
- JoinState is sent on channel 2 in chunks (≤ 64 KB each): seed and settings, construct list, node depletion list, all open-container states for the joining player, run state and the active perk list. Typical size after 3 shifts: 50–200 KB.

## 6. Join-in-progress and disconnects

- Join-in-progress is allowed only during Prep and Lobby. A player joining during Prep spawns at the PO with the starting kit's personal items and inherits nothing else. Quota and wave scaling use the player count at the start of Delivery, sampled once per shift.
- Disconnect during a run: the player's character is frozen and made invulnerable for 120 s; their inventory stays on the body. If they reconnect (same account id) within 120 s they resume. Otherwise the body drops a death bag and despawns. Player count for scaling is not recalculated mid-shift; it drops at the next Delivery start.
- Host disconnect: no host migration in v1. Clients show "Host lost" and return to the main menu; the run counts as abandoned for Postal Rank XP (shifts completed still award XP because XP is granted at each Payday, not only at Results).
- Dedicated server: continues while any player is connected; shuts down 60 s after the last player leaves.

## 7. Anti-cheat and validation

Cheating in co-op is low-stakes, but validation keeps the simulation consistent:

- Movement: server clamps displacement per tick to maxSpeed × 1.15 × dt; excess is rejected (client is corrected).
- Inventory ops: validated for ownership (player may only move items in containers they have open), range (≤ 3 m to the container), and placement legality.
- Build: validated for materials, tile legality, range (≤ 8 m), and blueprint ownership.
- Shop and draft: validated for wallet, phase, and eligibility.
- Rate limits per client: 60 action RPCs/s, 3 chat/s, 1 ping/s.

(v1 keeps only the validations that protect consistency — placement legality, materials, wallet, phase. Movement clamps and rate limits are dropped; chapter 13 §4.3.)

## 8. Determinism scope

Only world generation is required to be deterministic across machines (chapter 02). Gameplay simulation is server-only and does not need cross-machine determinism, which is why the belt visual sim is corrected by checksums rather than lockstepped. This keeps the netcode simple and lets clients run at any frame rate.

## 9. Testing requirements

- A headless bot client can join a server and run scripted inputs (walk, deliver, build). Used to load-test 8 clients and to soak-test belt replication.
- A network conditioner (latency, jitter, loss) is built into the debug menu for both server and client.
- Replay: the server can record inputs and events per run to a file for bug reproduction (deferred to M5).
