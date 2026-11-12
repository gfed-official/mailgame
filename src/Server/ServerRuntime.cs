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
        private const int MaxCatchUpTicks = 5;
        private const int MaxNameLength = 24;

        private sealed class Client
        {
            public int Connection;
            public Player? Player;
            public int SnapshotCount;
            public readonly List<SimEvent> Outbox = new List<SimEvent>();
        }

        public SimWorld World { get; }
        public uint ProtocolHash { get; }
        public int ClientCount => _clients.Count;
        /// <summary>CPU time of the most recent tick, in milliseconds (sim step + replication).</summary>
        public double LastTickMs { get; private set; }
        public double MaxTickMs { get; private set; }
        public Action<string>? Log;

        private readonly IServerTransport _transport;
        private readonly Dictionary<int, Client> _clients = new Dictionary<int, Client>();
        private readonly List<Client> _clientList = new List<Client>();
        private readonly NetWriter _writer = new NetWriter(1024);
        private readonly SnapshotMsg _snapshot = new SnapshotMsg();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private double _accumulator;

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
            World.Step();
            RouteEvents();
            // 20 Hz from a 30 Hz tick: two ticks out of three (doc 06 §3 "alternates 1/2 tick gaps").
            if (World.Tick % 3 != 0) SendSnapshots();
            LastTickMs = _stopwatch.Elapsed.TotalMilliseconds;
            if (LastTickMs > MaxTickMs) MaxTickMs = LastTickMs;
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
            if (client.Player != null)
            {
                Log?.Invoke($"{client.Player.Name} left");
                World.RemovePlayer(client.Player.Id);
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
            if (!World.AcceptsPlayers) { Reject(client, RejectReason.RunInProgress); return; }

            var player = World.AddPlayer(name);
            if (player == null) { Reject(client, RejectReason.ServerFull); return; }
            client.Player = player;

            var welcome = new WelcomeMsg
            {
                PlayerId = player.Id,
                Tick = World.Tick,
                Seed = World.Settings.Seed,
                MapId = World.Map.Id,
                MapHash = World.Map.MapHash,
                IntakeContainerId = World.Intake.Id,
                Global = World.GetGlobalState(),
                Hotbar = player.Hotbar.Snapshot(),
                Inventory = player.Inventory.Snapshot(),
            };
            foreach (var p in World.Players)
                welcome.Players.Add(new PlayerInfo { Id = p.Id, Name = p.Name, Slot = p.Slot });
            _writer.Reset();
            welcome.Write(_writer);
            _transport.Send(client.Connection, NetChannel.Bulk, _writer.ToArray());
            Log?.Invoke($"{name} joined as {player.Id}");
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
                    Id = p.Id, X = p.Move.X, Y = p.Move.Y, Z = p.Move.Z, Yaw = p.Move.Yaw, HeldSlot = p.HeldSlot, Ready = p.Ready,
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
                _snapshot.HasGlobal = c.SnapshotCount++ % GlobalEverySnapshots == 0;
                _snapshot.Global = global;
                _writer.Reset();
                _snapshot.Write(_writer);
                _transport.Send(c.Connection, NetChannel.Unreliable, _writer.ToArray());
            }
        }
    }
}
