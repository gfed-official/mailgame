using System;
using System.Collections.Generic;
using System.Diagnostics;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Server
{
    /// <summary>
    /// Owns the SimWorld and a transport (doc 07 §3): drains inputs and requests into the sim, runs
    /// the fixed 30 Hz tick, routes sim events and sends per-client snapshots. The same object serves
    /// a listen server (loopback) and a dedicated one.
    /// </summary>
    public sealed class ServerRuntime : IServerTransportHandler
    {
        /// <summary>Doc 06 §3: the global block rides every 6th snapshot (≈ 3.3 Hz at 20 Hz).</summary>
        private const int GlobalEverySnapshots = 6;
        /// <summary>
        /// Enemies ride every other snapshot: 10 Hz, one list every 3 ticks. Forty of them at 9
        /// bytes are then 29 kbps, inside the 40 kbps of doc 06 §8; clients interpolate.
        /// </summary>
        private const int EnemiesEverySnapshots = 2;
        private const int MaxCatchUpTicks = 5;
        private const int MaxNameLength = 24;

        private sealed class Client
        {
            public int Connection;
            public Player? Player;
            public int SnapshotCount;
            /// <summary>The wallet in the last global block sent to this client.</summary>
            public int WalletSent = int.MinValue;
            public readonly List<SimEvent> Outbox = new List<SimEvent>();
        }

        public SimWorld World { get; }
        public uint ProtocolHash { get; private set; }
        /// <summary>
        /// After a hot reload of the content this server runs on (doc 07 §6): the sim reads its
        /// numbers again, and from now on only a client with the new content may join.
        /// </summary>
        public void ContentReloaded()
        {
            ProtocolHash = Protocol.Hash(World.Content);
            World.ContentReloaded();
        }

        public int ClientCount => _clients.Count;
        /// <summary>CPU time of the most recent tick, in milliseconds (sim step + replication).</summary>
        public double LastTickMs { get; private set; }
        public double MaxTickMs { get; private set; }
        /// <summary>Tick times after the warm-up: what the 2 ms budget of doc 12 is held against.</summary>
        public TickStats TickTimes { get; } = new TickStats();
        public Action<string>? Log;

        private readonly IServerTransport _transport;
        private readonly Dictionary<int, Client> _clients = new Dictionary<int, Client>();
        private readonly List<Client> _clientList = new List<Client>();
        /// <summary>Who lost their connection and may still come back, for the log's last word on them.</summary>
        private readonly List<Player> _away = new List<Player>();
        private readonly NetWriter _writer = new NetWriter(1024);
        private readonly SnapshotMsg _snapshot = new SnapshotMsg();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private double _accumulator;

        /// <summary>A tick this slow after the warm-up is worth a log line (the first few only).</summary>
        private const double SlowTickMs = 5.0;
        private const int MaxSlowTicksLogged = 40;
        private int _slowTicksLogged;
        private TimeSpan _gcPauseAtLastSlowTick;

        public ServerRuntime(ContentDb content, SimSettings settings, IServerTransport transport)
        {
            World = new SimWorld(content, settings);
            ProtocolHash = Protocol.Hash(content);
            _transport = transport;
        }

        /// <summary>Real-time driver: runs as many fixed ticks as the elapsed time covers.</summary>
        public void Update(double deltaSeconds)
        {
            _accumulator += deltaSeconds;
            int ticks = 0;
            while (_accumulator >= SimConstants.TickDt)
            {
                _accumulator -= SimConstants.TickDt;
                if (ticks++ < MaxCatchUpTicks) Tick();
            }
        }

        /// <summary>One server tick: network in → sim step → events and snapshots out.</summary>
        public void Tick()
        {
            _stopwatch.Restart();
            _transport.Poll(this);
            double polled = _stopwatch.Elapsed.TotalMilliseconds;
            World.Step();
            double stepped = _stopwatch.Elapsed.TotalMilliseconds;
            if (_away.Count > 0) LogWhoDidNotComeBack();
            RouteEvents();
            // 20 Hz from a 30 Hz tick: two ticks out of three (doc 06 §3 "alternates 1/2 tick gaps").
            // A paused world has nothing new to show; the pause itself travels as a reliable event.
            if (!World.Paused && World.Tick % 3 != 0) SendSnapshots();
            LastTickMs = _stopwatch.Elapsed.TotalMilliseconds;
            if (LastTickMs > MaxTickMs) MaxTickMs = LastTickMs;
            TickTimes.Add(LastTickMs);
            if (LastTickMs > SlowTickMs && TickTimes.Count > 0 && _slowTicksLogged < MaxSlowTicksLogged)
            {
                // Where the time went, and how much of it the collector took: enough to tell the game's cost from the machine's.
                double gcMs = (GC.GetTotalPauseDuration() - _gcPauseAtLastSlowTick).TotalMilliseconds;
                _slowTicksLogged++;
                Log?.Invoke(FormattableString.Invariant($"slow tick {World.Tick}: {LastTickMs:0.0} ms = network in {polled:0.0} + sim {stepped - polled:0.0} + out {LastTickMs - stepped:0.0}")
                    + FormattableString.Invariant($" ({World.Run.Phase}, {_clients.Count} clients, GC pauses since the last slow tick {gcMs:0.0} ms)"));
            }
            if (LastTickMs > SlowTickMs) _gcPauseAtLastSlowTick = GC.GetTotalPauseDuration();
        }

        // --- transport callbacks ----------------------------------------------------------------

        public void OnClientConnected(int connectionId)
        {
            var client = new Client { Connection = connectionId };
            _clients[connectionId] = client;
            _clientList.Add(client);
        }

        public void OnClientDisconnected(int connectionId)
        {
            if (!_clients.TryGetValue(connectionId, out var client)) return;
            _clients.Remove(connectionId);
            _clientList.Remove(client);
            if (client.Player == null) return;
            // Doc 06 §6: out of a run under way nobody drops for good at once.
            if (World.Disconnect(client.Player.Id))
            {
                double seconds = SimConstants.TicksToSeconds((long)client.Player.AwayUntilTick - World.Tick);
                Log?.Invoke(FormattableString.Invariant($"{client.Player.Name} lost the connection: {seconds:0} s to come back as {client.Player.Id}"));
                if (!_away.Contains(client.Player)) _away.Add(client.Player);
            }
            else Log?.Invoke($"{client.Player.Name} left");
        }

        private void LogWhoDidNotComeBack()
        {
            for (int i = _away.Count - 1; i >= 0; i--)
            {
                var p = _away[i];
                bool gone = World.GetPlayer(p.Id) != p;
                if (!gone && p.Away) continue;
                _away.RemoveAt(i);
                if (gone) Log?.Invoke($"{p.Name} did not come back");
            }
        }

        public void OnClientData(int connectionId, NetChannel channel, byte[] data)
        {
            if (!_clients.TryGetValue(connectionId, out var client)) return;
            try
            {
                var r = new NetReader(data);
                var type = (MsgType)r.U8();
                switch (type)
                {
                    case MsgType.Hello:
                        HandleHello(client, HelloMsg.Read(r));
                        break;
                    case MsgType.Inputs:
                        if (client.Player == null) break;
                        foreach (var input in InputsMsg.Read(r).Inputs) World.QueueInput(client.Player.Id, input);
                        break;
                    case MsgType.Request:
                        if (client.Player == null || channel == NetChannel.Unreliable) break;
                        World.Submit(client.Player.Id, RequestMsg.Read(r));
                        break;
                    default:
                        throw new NetFormatException($"unexpected message type {(byte)type}");
                }
            }
            catch (NetFormatException e)
            {
                // A malformed packet is dropped; it never reaches the sim.
                Log?.Invoke($"dropped bad packet from connection {connectionId}: {e.Message}");
            }
        }

        private void HandleHello(Client client, HelloMsg hello)
        {
            if (client.Player != null) return;
            if (hello.ProtocolHash != ProtocolHash) { Reject(client, RejectReason.ProtocolMismatch); return; }
            string name = hello.Name.Trim();
            if (name.Length == 0) { Reject(client, RejectReason.BadHello); return; }
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            // Somebody who lost their connection in this run is let back into any phase of it (doc 06 §6).
            var player = World.FindAway(hello.Account);
            bool back = player != null;
            if (player != null) World.Reconnect(player);
            else
            {
                if (!World.AcceptsPlayers) { Reject(client, RejectReason.RunInProgress); return; }
                player = World.AddPlayer(name, hello.Account);
                if (player == null) { Reject(client, RejectReason.ServerFull); return; }
            }
            client.Player = player;
            // Before the Welcome, so its containers and global state already show what was asked.
            World.SetPlaytest(player, hello.Playtest, hello.StartShift, (int)Math.Min(hello.StartWallet, (uint)PlaytestMask.MaxWallet));
            World.SetRank(player, hello.Rank);

            var welcome = new WelcomeMsg
            {
                PlayerId = player.Id,
                Tick = World.Tick,
                Seed = World.Settings.Seed,
                MapId = World.Map.Id,
                MapHash = World.Map.MapHash,
                IntakeContainerId = World.Intake.Id,
                RaidTick = World.RaidTick,
                RespawnTick = player.Dead ? player.RespawnTick : 0,
                ProtectedUntilTick = World.Protected(player) ? player.ProtectedUntilTick : 0,
                Global = World.GetGlobalState(),
                Hotbar = player.Hotbar.Snapshot(),
                Inventory = player.Inventory.Snapshot(),
            };
            foreach (var chest in World.MapChests) welcome.ChestContainerIds.Add(chest.Id);
            foreach (var bag in World.Bags)
                welcome.Bags.Add(new BagInfo { ContainerId = bag.Id, Owner = bag.Owner, X = bag.X, Z = bag.Z, DespawnTick = bag.DespawnTick });
            foreach (var bundle in World.Loot)
            {
                var loot = new LootDroppedEvent
                {
                    LootId = bundle.Id, X = bundle.X, Z = bundle.Z, EnemyKind = bundle.EnemyKind, Mega = bundle.Mega,
                    Coins = (ushort)Math.Min(ushort.MaxValue, bundle.Coins),
                };
                loot.Items.AddRange(bundle.Items);
                welcome.Loot.Add(loot);
            }
            foreach (var p in World.Players)
                welcome.Players.Add(new PlayerInfo { Id = p.Id, Name = p.Name, Slot = p.Slot });
            foreach (var perk in World.TeamPerks) welcome.Perks.Add(new PerkInfo { Perk = perk.Index });
            foreach (var p in World.Players)
            {
                foreach (var perk in p.Perks) welcome.Perks.Add(new PerkInfo { Holder = p.Id, Perk = perk.Index });
                if (p.DraftPick >= 0) welcome.DraftPicks.Add(new DraftPickInfo { Player = p.Id, Card = (byte)p.DraftPick });
            }
            foreach (var perk in World.DraftOffer) welcome.DraftOffer.Add(perk.Index);
            foreach (var entry in World.ShopSpecials) welcome.ShopSpecials.Add(entry.Index);
            foreach (var entry in World.ShopOwned) welcome.ShopOwned.Add(entry.Index);
            for (int i = 0; i < World.Nodes.Count; i++)
                if (World.Nodes.Touched(i)) welcome.Nodes.Add(new NodeInfo { Node = (ushort)i, HitsLeft = (byte)World.Nodes.HitsLeft(i) });
            foreach (var built in World.Constructs.All)
            {
                welcome.Constructs.Add(new ConstructInfo
                {
                    Building = built.Def.Index, X = (byte)built.Tx, Y = (byte)built.Tz, Rotation = built.Rotation,
                    Hp = (ushort)built.Hp, ContainerId = built.ContainerId,
                });
            }
            foreach (var v in World.Vehicles)
            {
                welcome.Vehicles.Add(new VehicleInfo
                {
                    Id = v.Id, Def = v.Def.Index, X = v.X, Z = v.Z, Heading = v.Heading, CargoContainer = v.Cargo.Id, Rider = v.Rider,
                });
            }
            _writer.Reset();
            welcome.Write(_writer);
            _transport.Send(client.Connection, NetChannel.Bulk, _writer.ToArray());
            Log?.Invoke(back ? $"{player.Name} is back as {player.Id}{PlaytestNote(player)}" : $"{name} joined as {player.Id}{PlaytestNote(player)}");
        }

        /// <summary>What the server granted of a Hello's playtest wishes, for the log; empty for a player without any.</summary>
        private static string PlaytestNote(Player player)
        {
            if (player.Playtest == PlaytestFlags.None && player.PlaytestShift == 0 && player.PlaytestWallet == 0) return "";
            string shift = player.PlaytestShift == 0 ? "" : $"shift {player.PlaytestShift}";
            if (player.PlaytestWallet != 0) shift += $"{(shift.Length > 0 ? ", " : "")}wallet {player.PlaytestWallet} cents";
            string flags = player.Playtest == PlaytestFlags.None ? "" : player.Playtest.ToString();
            return $" (playtest: {flags}{(flags.Length > 0 && shift.Length > 0 ? ", " : "")}{shift})";
        }

        private void Reject(Client client, RejectReason reason)
        {
            _writer.Reset();
            RejectMsg.Write(_writer, reason);
            _transport.Send(client.Connection, NetChannel.Reliable, _writer.ToArray());
            _transport.Disconnect(client.Connection);
            _clients.Remove(client.Connection);
            _clientList.Remove(client);
            Log?.Invoke($"rejected connection {client.Connection}: {reason}");
        }

        // --- replication ------------------------------------------------------------------------

        private void RouteEvents()
        {
            var events = World.Events;
            if (events.Count == 0) return;
            foreach (var ev in events)
            {
                foreach (var c in _clientList)
                {
                    if (c.Player == null) continue;
                    if (ev.Audience.IsNone || ev.Audience == c.Player.Id) c.Outbox.Add(ev);
                }
            }
            events.Clear();
            foreach (var c in _clientList)
            {
                if (c.Outbox.Count == 0) continue;
                _writer.Reset();
                EventsMsg.Write(_writer, c.Outbox);
                _transport.Send(c.Connection, NetChannel.Reliable, _writer.ToArray());
                c.Outbox.Clear();
            }
        }

        private void SendSnapshots()
        {
            if (_clientList.Count == 0) return;
            // Interest management stub (M0): every player is global, so the player list is shared.
            _snapshot.Players.Clear();
            foreach (var p in World.Players)
            {
                _snapshot.Players.Add(new PlayerSnap
                {
                    Id = p.Id, X = p.Move.X, Y = p.Move.Y, Z = p.Move.Z, Yaw = p.Move.Yaw, HeldSlot = p.HeldSlot, HeldTool = World.HeldTool(p), Ready = p.Ready,
                    Hp = p.HpByte, Protected = World.Protected(p), Away = p.Away,
                    Ride = p.Move.Ride, Heading = Sim.Vehicles.Vehicle.YawOf(p.Move.HeadX, p.Move.HeadZ, 0f), Kit = p.Kit?.Index ?? 0,
                });
            }
            _snapshot.Enemies.Clear();
            foreach (var e in World.Enemies)
            {
                _snapshot.Enemies.Add(new EnemySnap
                {
                    Id = (ushort)e.Id.Counter, Kind = e.Def.Index, Mega = e.Mega, State = e.State, X = e.X, Z = e.Z, Yaw = e.Yaw, Hp = e.HpByte,
                });
            }
            _snapshot.Tick = World.Tick;
            var global = World.GetGlobalState();

            foreach (var c in _clientList)
            {
                var player = c.Player;
                if (player == null) continue;
                _snapshot.AckSeq = player.LastProcessedSeq;
                _snapshot.HasOwn = true;
                _snapshot.Own = player.Move;
                _snapshot.OwnSpeedMult = player.SpeedMult;
                _snapshot.OwnHp = player.Hp;
                _snapshot.HasEnemies = c.SnapshotCount % EnemiesEverySnapshots == 0;
                // Money spent or taken at the shop's counter is on the cards at once, not a third of a second later.
                _snapshot.HasGlobal = c.SnapshotCount++ % GlobalEverySnapshots == 0 || global.Wallet != c.WalletSent;
                _snapshot.Global = global;
                if (_snapshot.HasGlobal) c.WalletSent = global.Wallet;
                _writer.Reset();
                _snapshot.Write(_writer);
                _transport.Send(c.Connection, NetChannel.Unreliable, _writer.ToArray());
            }
        }
    }
}
