using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Net;

namespace PerformativeMail.Server.Transport
{
    /// <summary>
    /// In-process transport (doc 07 §3): the host's client, tests and bots talk to the server through
    /// it with the same serialized bytes a socket would carry. Optional latency / loss on the
    /// unreliable channel makes it the M0 network conditioner as well.
    /// </summary>
    public sealed class LoopbackHub : IServerTransport
    {
        private enum Kind : byte { Connect, Disconnect, Data }

        private struct Packet
        {
            public Kind Kind;
            public int Connection;
            public NetChannel Channel;
            public byte[] Data;
            /// <summary>Hub time (in polls of the receiving side) at which the packet may be delivered.</summary>
            public long DueAt;
        }

        private readonly List<Packet> _toServer = new List<Packet>();
        private readonly Dictionary<int, LoopbackClient> _clients = new Dictionary<int, LoopbackClient>();
        private readonly Random _rng;
        private int _nextConnection = 1;
        private long _serverPolls;

        /// <summary>Extra one-way delay, counted in polls of the receiving end (one poll = one 30 Hz tick in tests).</summary>
        public int DelayPolls;
        /// <summary>Loss probability for the unreliable channel, 0..1. Reliable channels never drop.</summary>
        public double UnreliableLoss;

        public LoopbackHub(int conditionerSeed = 1) { _rng = new Random(conditionerSeed); }

        /// <summary>Opens a new client connection; the server sees it on its next poll.</summary>
        public IClientTransport CreateClient()
        {
            var client = new LoopbackClient(this, _nextConnection++);
            _clients[client.ConnectionId] = client;
            _toServer.Add(new Packet { Kind = Kind.Connect, Connection = client.ConnectionId, DueAt = _serverPolls });
            client.Enqueue(Kind.Connect, default, Array.Empty<byte>(), 0);
            return client;
        }

        public void Poll(IServerTransportHandler handler)
        {
            _serverPolls++;
            // Handlers may send or disconnect while we iterate, so walk a stable prefix by index.
            int i = 0;
            while (i < _toServer.Count)
            {
                var p = _toServer[i];
                if (p.DueAt > _serverPolls) { i++; continue; }
                _toServer.RemoveAt(i);
                switch (p.Kind)
                {
                    case Kind.Connect: handler.OnClientConnected(p.Connection); break;
                    case Kind.Disconnect: handler.OnClientDisconnected(p.Connection); break;
                    default: handler.OnClientData(p.Connection, p.Channel, p.Data); break;
                }
            }
        }

        public void Send(int connectionId, NetChannel channel, byte[] data)
        {
            if (!_clients.TryGetValue(connectionId, out var client)) return;
            if (Dropped(channel)) return;
            client.Enqueue(Kind.Data, channel, data, DelayPolls);
        }

        public void Disconnect(int connectionId)
        {
            if (!_clients.TryGetValue(connectionId, out var client)) return;
            _clients.Remove(connectionId);
            client.Enqueue(Kind.Disconnect, default, Array.Empty<byte>(), 0);
        }

        private bool Dropped(NetChannel channel) =>
            channel == NetChannel.Unreliable && UnreliableLoss > 0 && _rng.NextDouble() < UnreliableLoss;

        private void FromClient(Kind kind, int connection, NetChannel channel, byte[] data)
        {
            if (kind == Kind.Data && Dropped(channel)) return;
            _toServer.Add(new Packet
            {
                Kind = kind, Connection = connection, Channel = channel, Data = data,
                DueAt = _serverPolls + (kind == Kind.Data ? DelayPolls : 0),
            });
        }

        private sealed class LoopbackClient : IClientTransport
        {
            private readonly LoopbackHub _hub;
            private readonly List<Packet> _inbox = new List<Packet>();
            private long _polls;
            private bool _closed;

            public int ConnectionId { get; }

            public LoopbackClient(LoopbackHub hub, int connectionId)
            {
                _hub = hub;
                ConnectionId = connectionId;
            }

            public void Enqueue(Kind kind, NetChannel channel, byte[] data, int delay) =>
                _inbox.Add(new Packet { Kind = kind, Channel = channel, Data = data, DueAt = _polls + delay });

            public void Poll(IClientTransportHandler handler)
            {
                _polls++;
                int i = 0;
                while (i < _inbox.Count)
                {
                    var p = _inbox[i];
                    if (p.DueAt > _polls) { i++; continue; }
                    _inbox.RemoveAt(i);
                    switch (p.Kind)
                    {
                        case Kind.Connect: handler.OnConnected(); break;
                        case Kind.Disconnect: _closed = true; handler.OnDisconnected(); break;
                        default: handler.OnData(p.Channel, p.Data); break;
                    }
                }
            }

            public void Send(NetChannel channel, byte[] data)
            {
                if (_closed) return;
                _hub.FromClient(Kind.Data, ConnectionId, channel, data);
            }

            public void Close()
            {
                if (_closed) return;
                _closed = true;
                _hub._clients.Remove(ConnectionId);
                _hub.FromClient(Kind.Disconnect, ConnectionId, default, Array.Empty<byte>());
            }
        }
    }
}
