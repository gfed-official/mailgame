using System;
using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    public enum ClientState : byte
    {
        Connecting,
        /// <summary>Hello sent, waiting for the join state.</summary>
        Joining,
        Joined,
        Rejected,
        Disconnected,
    }

    /// <summary>What the local player wants this tick; filled by the input layer or a bot.</summary>
    public struct ClientInput
    {
        /// <summary>World-space move direction, each axis -1..1.</summary>
        public float MoveX, MoveZ;
        /// <summary>Where the player looks: yaw 0 faces −Z. The body faces the same way (first person).</summary>
        public float YawRadians;
        /// <summary>Look pitch, positive up. Client-side only: it aims the camera and the interact ray.</summary>
        public float PitchRadians;
        public InputButtons Buttons;
        public byte HeldSlot;
    }

    /// <summary>
    /// Client side of the connection (doc 07 §3): keeps <see cref="ClientWorld"/> up to date from
    /// snapshots and events, predicts the local player and turns UI actions into requests.
    /// </summary>
    public sealed class ClientRuntime : IClientTransportHandler
    {
        /// <summary>Remote players are rendered this far in the past (doc 06 §3: 100 ms).</summary>
        public const double InterpolationDelayTicks = 3.0;
        private const int MaxPendingInputs = 120;

        public ClientState State { get; private set; } = ClientState.Connecting;
        public RejectReason RejectReason { get; private set; }
        public string? DisconnectDetail { get; private set; }
        public ClientWorld? World { get; private set; }
        public string Name { get; }
        /// <summary>What this client asks the server for when it joins (playtest nits 10 and 13).</summary>
        public PlaytestFlags Playtest { get; }
        /// <summary>The shift this client asks runs to start at, 0 = the first.</summary>
        public int StartShift { get; }

        /// <summary>Set by the input layer; sampled once per client tick.</summary>
        public ClientInput Input;

        /// <summary>Raised for every reliable event after it has been applied to <see cref="World"/>.</summary>
        public event Action<SimEvent>? EventReceived;
        /// <summary>Raised with the request that was answered and the server's verdict.</summary>
        public event Action<SimRequest, OpResult>? RequestCompleted;
        /// <summary>Raised at the start of every client tick, before <see cref="Input"/> is sampled (bots decide here).</summary>
        public event Action? TickStarting;

        // Diagnostics (M0 acceptance: container hashes match on every version; prediction error).
        public int ContainerVersionsVerified { get; private set; }
        public int ContainerDesyncs { get; private set; }
        public int SnapshotsReceived { get; private set; }
        public int BadPackets { get; private set; }
        public double PredictionErrorRms => _errorSamples == 0 ? 0.0 : Math.Sqrt(_errorSumSq / _errorSamples);
        public double PredictionErrorMax { get; private set; }
        public int PendingRequestCount => _pendingRequests.Count;
        /// <summary>Hammer swings and cannon shots the local player has started; the view model plays each one.</summary>
        public int LocalHammerSwings { get; private set; }
        public int LocalCannonShots { get; private set; }

        private readonly ContentDb _content;
        private readonly IClientTransport _transport;
        private readonly MoveConstants _move;
        private readonly NetWriter _writer = new NetWriter(256);
        private readonly List<PlayerInput> _pendingInputs = new List<PlayerInput>();
        private readonly InputsMsg _inputsMsg = new InputsMsg();
        private readonly Dictionary<uint, SimRequest> _pendingRequests = new Dictionary<uint, SimRequest>();
        private readonly List<ushort> _enemyScratch = new List<ushort>();
        private readonly uint _hammerCooldownTicks, _cannonCooldownTicks;
        private uint _hammerReadySeq, _cannonReadySeq;
        private uint _nextSeq = 1;
        private uint _nextRequestId = 1;
        private uint _lastSnapshotTick;
        private double _accumulator;
        private double _errorSumSq;
        private long _errorSamples;

        public ClientRuntime(ContentDb content, IClientTransport transport, string name, PlaytestFlags playtest = PlaytestFlags.None, int startShift = 0)
        {
            Playtest = playtest;
            StartShift = Math.Clamp(startShift, 0, byte.MaxValue);
            _content = content;
            _transport = transport;
            _move = MoveConstants.From(content.Balance);
            _hammerCooldownTicks = SimConstants.SecondsToTicks(content.Balance.GetDouble("hammerCooldownSeconds"));
            _cannonCooldownTicks = SimConstants.SecondsToTicks(content.Balance.GetDouble("cannonCooldownSeconds"));
            Name = name;
        }

        /// <summary>Real-time driver: polls the network once, then runs the fixed client ticks the elapsed time covers.</summary>
        public void Update(double deltaSeconds)
        {
            _transport.Poll(this);
            _accumulator += deltaSeconds;
            int ticks = 0;
            while (_accumulator >= SimConstants.TickDt)
            {
                _accumulator -= SimConstants.TickDt;
                if (ticks++ < 5) StepInput();
            }
        }

        /// <summary>One client tick with a network poll first (bots and tests drive this directly).</summary>
        public void Tick()
        {
            _transport.Poll(this);
            StepInput();
        }

        public void Disconnect()
        {
            if (State == ClientState.Disconnected) return;
            _transport.Close();
            State = ClientState.Disconnected;
        }

        /// <summary>Fraction of the current client tick already elapsed, for render smoothing of the local player.</summary>
        public float TickFraction => (float)(_accumulator / SimConstants.TickDt);

        // --- requests ---------------------------------------------------------------------------

        /// <summary>Sends a request; the id is assigned here and returned. The answer arrives via <see cref="RequestCompleted"/>.</summary>
        public uint Send(SimRequest request)
        {
            if (State != ClientState.Joined) return 0;
            request.RequestId = _nextRequestId++;
            _pendingRequests[request.RequestId] = request;
            if (InventoryPrediction.Predicts(request.Kind)) World!.Prediction.Add(request);
            _writer.Reset();
            RequestMsg.Write(_writer, request);
            _transport.Send(NetChannel.Reliable, _writer.ToArray());
            return request.RequestId;
        }

        public uint OpenContainer(uint containerId) => Send(SimRequest.Open(0, containerId));
        public uint CloseContainer() => World == null || World.OpenContainerId == 0 ? 0 : Send(SimRequest.Close(0, World.OpenContainerId));
        public uint QuickMove(uint containerId, uint entryId) => Send(SimRequest.QuickMove(0, containerId, entryId));
        /// <summary>Drag and drop: the entry (or <paramref name="count"/> units of it; 0 = all) to a cell of a container.</summary>
        public uint Move(uint fromContainer, uint entryId, uint toContainer, int x, int y, bool rotated = false, int count = 0) =>
            Send(SimRequest.Move(0, fromContainer, entryId, toContainer, x, y, rotated, count));
        public uint Sort(uint containerId, SortKey key) => Send(SimRequest.Sort(0, containerId, key));
        public uint Deliver(uint containerId, uint entryId, int destId) => Send(SimRequest.Deliver(0, containerId, entryId, destId));
        public uint StartRun() => Send(SimRequest.StartRun(0));
        public uint SetReady(bool ready) => Send(SimRequest.SetReady(0, ready));
        public uint ContinueRun() => Send(SimRequest.ContinueRun(0));
        public uint SetPaused(bool paused) => Send(SimRequest.SetPaused(0, paused));

        /// <summary>
        /// The pause key (doc 09 §2.12): resumes a paused run; pauses one this client hosts alone.
        /// Returns the request id, 0 where the key does not pause (multiplayer never pauses).
        /// </summary>
        public uint TogglePause()
        {
            var world = World;
            if (State != ClientState.Joined || world == null) return 0;
            if (world.Paused) return SetPaused(false);
            return world.CanPause ? SetPaused(true) : 0;
        }

        /// <summary>
        /// The one "go" key: the host starts the run in the Lobby and leaves Results; everyone else
        /// in the Lobby, and everyone in Prep, toggles Ready. Returns the request id, 0 if the key
        /// does nothing right now.
        /// </summary>
        public uint PressGo()
        {
            var world = World;
            if (State != ClientState.Joined || world == null || !world.HasLocal) return 0;
            switch (world.Global.Phase)
            {
                case RunPhase.Lobby: return world.IsHost ? StartRun() : SetReady(!world.LocalReady);
                case RunPhase.Prep: return SetReady(!world.LocalReady);
                // A stopped clock never runs out, so the host ends the shift by hand.
                case RunPhase.Delivery: return world.ClockStopped && world.IsHost ? ContinueRun() : 0;
                case RunPhase.Results: return world.IsHost ? ContinueRun() : 0;
                default: return 0;
            }
        }

        // --- local tick -------------------------------------------------------------------------

        private void StepInput()
        {
            TickStarting?.Invoke();
            var world = World;
            if (State != ClientState.Joined || world == null) return;
            // Paused: the server simulates no input, so none is predicted or sent, and its clock stands.
            if (world.Paused) return;
            world.ServerTick += 1.0;
            if (!world.HasLocal) return;

            var input = new PlayerInput
            {
                Seq = _nextSeq++,
                MoveX = PlayerInput.QuantizeAxis(Input.MoveX),
                MoveZ = PlayerInput.QuantizeAxis(Input.MoveZ),
                Yaw = PlayerInput.QuantizeYaw(Input.YawRadians),
                Buttons = Input.Buttons,
                HeldSlot = Input.HeldSlot,
            };
            PlayerMovement.Step(ref world.Local, input, world.Map, world.LocalSpeedMult, _move);
            _pendingInputs.Add(input);
            if (_pendingInputs.Count > MaxPendingInputs) _pendingInputs.RemoveAt(0);
            PredictAttacks(world, input);

            _inputsMsg.Inputs.Clear();
            for (int i = Math.Max(0, _pendingInputs.Count - InputsMsg.MaxInputs); i < _pendingInputs.Count; i++)
                _inputsMsg.Inputs.Add(_pendingInputs[i]);
            _writer.Reset();
            _inputsMsg.Write(_writer);
            _transport.Send(NetChannel.Unreliable, _writer.ToArray());
        }

        /// <summary>
        /// The swing and the shot are shown the moment the button goes down, on the same cooldowns
        /// the server keeps; what they hit is the server's to say.
        /// </summary>
        private void PredictAttacks(ClientWorld world, in PlayerInput input)
        {
            if (world.Local.Dead) return;
            if (input.Has(InputButtons.Attack) && input.Seq >= _hammerReadySeq)
            {
                _hammerReadySeq = input.Seq + _hammerCooldownTicks;
                LocalHammerSwings++;
            }
            if (input.Has(InputButtons.AltAttack) && input.Seq >= _cannonReadySeq && CannonAmmo > 0)
            {
                _cannonReadySeq = input.Seq + _cannonCooldownTicks;
                LocalCannonShots++;
            }
        }

        /// <summary>Packages the local player could fire from the mail cannon.</summary>
        public int CannonAmmo
        {
            get
            {
                var world = World;
                if (world == null || world.HotbarId == 0) return 0;
                return CountAmmo(world.Hotbar) + CountAmmo(world.Inventory);
            }
        }

        private int CountAmmo(GridContainer container)
        {
            int n = 0;
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Mail && _content.MailKinds[e.Stack.DefIndex].CannonAmmo) n += e.Stack.Count;
            return n;
        }

        // --- transport callbacks ----------------------------------------------------------------

        public void OnConnected()
        {
            if (State != ClientState.Connecting) return;
            _writer.Reset();
            new HelloMsg { ProtocolHash = Protocol.Hash(_content), Name = Name, Playtest = Playtest, StartShift = (byte)StartShift }.Write(_writer);
            _transport.Send(NetChannel.Reliable, _writer.ToArray());
            State = ClientState.Joining;
        }

        public void OnDisconnected()
        {
            if (State != ClientState.Rejected) State = ClientState.Disconnected;
        }

        public void OnData(NetChannel channel, byte[] data)
        {
            try
            {
                var r = new NetReader(data);
                var type = (MsgType)r.U8();
                switch (type)
                {
                    case MsgType.Welcome: HandleWelcome(WelcomeMsg.Read(r)); break;
                    case MsgType.Reject:
                        RejectReason = RejectMsg.Read(r);
                        State = ClientState.Rejected;
                        break;
                    case MsgType.Snapshot: HandleSnapshot(SnapshotMsg.Read(r)); break;
                    case MsgType.Events:
                        foreach (var ev in EventsMsg.Read(r)) HandleEvent(ev);
                        break;
                    default: throw new NetFormatException($"unexpected message type {(byte)type}");
                }
            }
            catch (NetFormatException)
            {
                BadPackets++;
            }
        }

        private void HandleWelcome(WelcomeMsg m)
        {
            if (State != ClientState.Joining) return;
            // World check (doc 06 §5 step 5): the client builds the map itself and compares hashes.
            if (!_content.Maps.TryGetValue(m.MapId, out var map) || map.MapHash != m.MapHash || m.ChestContainerIds.Count != map.Chests.Count)
            {
                DisconnectDetail = $"map '{m.MapId}' does not match the server's";
                _transport.Close();
                State = ClientState.Disconnected;
                return;
            }
            var world = new ClientWorld(_content, map)
            {
                LocalId = m.PlayerId,
                Seed = m.Seed,
                IntakeContainerId = m.IntakeContainerId,
                ChestContainerIds = m.ChestContainerIds.ToArray(),
                Global = m.Global,
                GlobalTick = m.Tick,
                ServerTick = m.Tick,
            };
            foreach (var p in m.Players) world.Players[p.Id] = new ClientPlayer(p.Id, p.Name, p.Slot);
            World = world;
            Input.YawRadians = map.PostOffice.SpawnYaw;
            Input.PitchRadians = 0f;
            world.Local.Yaw = PlayerInput.QuantizeYaw(map.PostOffice.SpawnYaw);
            world.HotbarId = m.Hotbar.ContainerId;
            world.InventoryId = m.Inventory.ContainerId;
            LoadContainer(m.Hotbar);
            LoadContainer(m.Inventory);
            State = ClientState.Joined;
            // The server has parked this player already; its event may have come before the Welcome.
            if ((Playtest & PlaytestFlags.AtMailbox) != 0 && world.Players.TryGetValue(m.PlayerId, out var self))
                FaceMailbox(world, PlaytestSpot.Mailbox(map, self.Slot));
        }

        /// <summary>
        /// The server has put the local player before a mailbox (<see cref="PlaytestFlags.AtMailbox"/>):
        /// stand there at once and look at the box, so that its prompt is up without a mouse move.
        /// </summary>
        private void FaceMailbox(ClientWorld world, DestinationDef dest)
        {
            PlaytestSpot.Stand(world.Map, dest, _move.Radius, out float x, out float z);
            var feet = new PlayerMoveState { X = x, Z = z, Y = world.Map.HeightAt(x, z), Grounded = true };
            var box = AimBox.Mailbox(world.Map, dest);
            ViewRay.Aim(feet, box.X, box.Y, box.Z, out Input.YawRadians, out Input.PitchRadians);
            feet.Yaw = PlayerInput.QuantizeYaw(Input.YawRadians);
            // Inputs sent from the old place would only walk the server's copy away from the box.
            _pendingInputs.Clear();
            world.Local = feet;
            // The mail for the box in hand, when there is some: holding the key is all that is left to do.
            var hotbar = world.Hotbar;
            for (int slot = 1; slot < hotbar.Cols; slot++)
            {
                var entry = hotbar.EntryAt(slot, 0);
                if (entry == null || entry.Stack.Kind != StackKind.Mail || entry.Stack.Address != dest.Address) continue;
                Input.HeldSlot = (byte)slot;
                break;
            }
        }

        private void HandleSnapshot(SnapshotMsg m)
        {
            var world = World;
            if (world == null || State != ClientState.Joined) return;
            if (SnapshotsReceived > 0 && m.Tick <= _lastSnapshotTick) return; // stale or duplicate
            _lastSnapshotTick = m.Tick;
            SnapshotsReceived++;

            // Clock: follow the newest snapshot, snapping only when the estimate has drifted far.
            double drift = m.Tick - world.ServerTick;
            if (Math.Abs(drift) > 6.0) world.ServerTick = m.Tick;
            else world.ServerTick += drift * 0.1;

            foreach (var snap in m.Players)
                if (world.Players.TryGetValue(snap.Id, out var player)) player.Push(m.Tick, snap);
            if (m.HasEnemies) ApplyEnemies(world, m);
            if (m.HasGlobal)
            {
                world.Global = m.Global;
                world.GlobalTick = m.Tick;
            }
            if (m.HasOwn) Reconcile(world, m);
        }

        /// <summary>The enemy list is complete: whoever is not in it has fallen or fled.</summary>
        private void ApplyEnemies(ClientWorld world, SnapshotMsg m)
        {
            foreach (var snap in m.Enemies)
            {
                if (snap.Kind >= _content.Enemies.Count) continue;
                // Ids are 16 bits of a counter: after a wrap the same number can be somebody else.
                if (!world.Enemies.TryGetValue(snap.Id, out var enemy) || enemy.Def.Index != snap.Kind || enemy.Mega != snap.Mega)
                    world.Enemies[snap.Id] = enemy = new ClientEnemy(snap.Id, _content.Enemies[snap.Kind], snap.Mega);
                enemy.Push(m.Tick, snap);
            }
            if (world.Enemies.Count == m.Enemies.Count) return;
            _enemyScratch.Clear();
            foreach (var e in world.Enemies.Values) if (e.LastTick != m.Tick) _enemyScratch.Add(e.Id);
            foreach (ushort id in _enemyScratch) world.Enemies.Remove(id);
        }

        /// <summary>
        /// Prediction reconciliation (doc 06 §4): take the server's state, drop the inputs it has
        /// simulated, replay the rest. The distance the predicted position moves is the prediction error.
        /// </summary>
        private void Reconcile(ClientWorld world, SnapshotMsg m)
        {
            float beforeX = world.Local.X, beforeZ = world.Local.Z;
            bool hadLocal = world.HasLocal;
            ushort yaw = world.Local.Yaw;

            int acked = 0;
            while (acked < _pendingInputs.Count && _pendingInputs[acked].Seq <= m.AckSeq) acked++;
            if (acked > 0) _pendingInputs.RemoveRange(0, acked);

            world.Local = m.Own;
            world.Local.Yaw = yaw;
            world.LocalSpeedMult = m.OwnSpeedMult;
            if (m.OwnHp < world.LocalHp) world.LocalHurtTick = m.Tick;
            world.LocalHp = m.OwnHp;
            world.HasLocal = true;
            foreach (var input in _pendingInputs)
                PlayerMovement.Step(ref world.Local, input, world.Map, world.LocalSpeedMult, _move);

            if (!hadLocal) return;
            double dx = world.Local.X - beforeX, dz = world.Local.Z - beforeZ;
            double errSq = dx * dx + dz * dz;
            _errorSumSq += errSq;
            _errorSamples++;
            double err = Math.Sqrt(errSq);
            if (err > PredictionErrorMax) PredictionErrorMax = err;
        }

        private void HandleEvent(SimEvent ev)
        {
            var world = World;
            if (world == null) return;
            switch (ev)
            {
                case PlayerJoinedEvent e:
                    if (!world.Players.ContainsKey(e.Player)) world.Players[e.Player] = new ClientPlayer(e.Player, e.Name, e.Slot);
                    break;
                case PlayerLeftEvent e:
                    world.Players.Remove(e.Player);
                    break;
                case ContainerStateEvent e:
                    if (LoadContainer(e.State) != null && e.State.ContainerId != world.HotbarId && e.State.ContainerId != world.InventoryId)
                    {
                        if (world.OpenContainerId != 0 && world.OpenContainerId != e.State.ContainerId)
                            world.Containers.Remove(world.OpenContainerId);
                        world.OpenContainerId = e.State.ContainerId;
                        world.Prediction.Invalidate();
                    }
                    break;
                case ContainerDeltaEvent e:
                    ApplyDelta(world, e.Delta);
                    break;
                case ContainerClosedEvent e:
                    DropExternal(world, e.ContainerId);
                    break;
                case RequestResultEvent e:
                    if (_pendingRequests.TryGetValue(e.RequestId, out var request))
                    {
                        _pendingRequests.Remove(e.RequestId);
                        // Either way the guess ends here: an accepted move is in the mirrors by now
                        // (its deltas come first), a refused one falls back to what the server has.
                        world.Prediction.Resolve(e.RequestId);
                        if (request.Kind == RequestKind.CloseContainer && e.Result == OpResult.Ok) DropExternal(world, request.Container);
                        RequestCompleted?.Invoke(request, e.Result);
                    }
                    break;
                case PhaseChangedEvent e:
                {
                    var g = world.Global;
                    g.Shift = e.Shift; g.Phase = e.Phase; g.PhaseEndTick = e.PhaseEndTick; g.Quota = e.Quota; g.Outcome = e.Outcome;
                    world.Global = g;
                    world.GlobalTick = e.Tick;
                    // Every phase change clears the Ready flags (the snapshots confirm it a moment later).
                    foreach (var p in world.Players.Values) p.Ready = false;
                    if (e.Phase == RunPhase.Lobby)
                    {
                        world.LastSummary = null;
                        world.LocalKills = 0;
                        world.LocalLootCoins = 0;
                    }
                    if (e.Phase != RunPhase.Results) world.Results = null;
                    break;
                }
                case PausedChangedEvent e:
                    world.Paused = e.Paused;
                    if (e.Paused) world.ServerTick = e.Tick;
                    break;
                case ShiftSummaryEvent e:
                    world.LastSummary = e.Summary;
                    break;
                case RunResultsEvent e:
                    world.Results = e.Results;
                    break;
                case AttackEvent e:
                    if (world.Players.TryGetValue(e.Player, out var attacker))
                    {
                        attacker.AttackTick = (uint)Math.Max(0.0, world.ServerTick);
                        attacker.AttackWeapon = e.Weapon;
                    }
                    break;
                case EnemyDiedEvent e:
                    world.Enemies.Remove((ushort)e.Enemy.Counter);
                    if (e.Killer == world.LocalId) world.LocalKills++;
                    break;
                case LootDroppedEvent e:
                {
                    var loot = new ClientLoot
                    {
                        Id = e.LootId, X = e.X, Z = e.Z, EnemyKind = e.EnemyKind, Mega = e.Mega, Coins = e.Coins, DroppedTick = world.ServerTick,
                    };
                    loot.Items.AddRange(e.Items);
                    world.Loot[e.LootId] = loot;
                    break;
                }
                case LootTakenEvent e:
                    if (e.Player == world.LocalId) world.LocalLootCoins += e.Coins;
                    if (e.Gone) world.Loot.Remove(e.LootId);
                    else if (world.Loot.TryGetValue(e.LootId, out var left))
                    {
                        left.Coins = Math.Max(0, left.Coins - e.Coins);
                        foreach (var taken in e.Items)
                        {
                            int at = left.Items.FindIndex(i => i.Item == taken.Item);
                            if (at < 0) continue;
                            int rest = left.Items[at].Count - taken.Count;
                            if (rest <= 0) left.Items.RemoveAt(at);
                            else left.Items[at] = new LootItem { Item = taken.Item, Count = (byte)rest };
                        }
                    }
                    break;
                case LootGoneEvent e:
                    world.Loot.Remove(e.LootId);
                    break;
                case RaidChangedEvent e:
                {
                    var g = world.Global;
                    g.Raid = e.Raid;
                    world.Global = g;
                    world.RaidTick = e.Tick;
                    if (e.Raid == RaidState.None) world.Enemies.Clear();
                    break;
                }
                case PlayerDiedEvent e:
                    if (world.Players.TryGetValue(e.Player, out var fallen)) fallen.RespawnTick = e.RespawnTick;
                    if (e.Player == world.LocalId) world.LocalRespawnTick = e.RespawnTick;
                    break;
                case PlayerParkedEvent e:
                    if (e.Destination < world.Map.Destinations.Count) FaceMailbox(world, world.Map.Destinations[e.Destination]);
                    break;
            }
            EventReceived?.Invoke(ev);
        }

        private GridContainer? LoadContainer(ContainerState state)
        {
            var world = World!;
            if (state.DefIndex >= _content.Containers.Count) { ContainerDesyncs++; return null; }
            var mirror = new GridContainer(state.ContainerId, _content.Containers[state.DefIndex]);
            mirror.Load(state);
            world.Containers[state.ContainerId] = mirror;
            world.Prediction.Invalidate();
            VerifyHash(mirror, state.Hash);
            return mirror;
        }

        private void ApplyDelta(ClientWorld world, ContainerDelta delta)
        {
            // A delta for a container we no longer view was already in flight when we closed it.
            if (!world.Containers.TryGetValue(delta.ContainerId, out var mirror)) return;
            world.Prediction.Invalidate();
            if (!mirror.Apply(delta)) { ContainerDesyncs++; return; }
            VerifyHash(mirror, delta.Hash);
        }

        private void VerifyHash(GridContainer mirror, uint serverHash)
        {
            if (mirror.ComputeHash() == serverHash) ContainerVersionsVerified++;
            else ContainerDesyncs++;
        }

        private static void DropExternal(ClientWorld world, uint containerId)
        {
            if (world.OpenContainerId != containerId) return;
            world.Containers.Remove(containerId);
            world.OpenContainerId = 0;
            world.Prediction.Invalidate();
        }
    }
}
