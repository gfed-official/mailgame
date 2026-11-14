using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Net;

namespace PerformativeMail.App.Net
{
    /// <summary>What both ENet ends share (doc 06 §2): three channels and the packet flags of each.</summary>
    internal static class Enet
    {
        public const int Channels = 3;
        /// <summary>Doc 06 §2: 8 players; the rest of the peers are there so a ninth is told "server full" instead of timing out.</summary>
        public const int MaxPeers = 16;

        /// <summary>
        /// Channel 0 is unreliable; a snapshot above the MTU goes out as unreliable fragments rather
        /// than being promoted to reliable. ENet sequences it, so a late packet is dropped, not
        /// delivered out of order. Channels 1 and 2 are reliable and ordered.
        /// </summary>
        public static int Flags(NetChannel channel) =>
            channel == NetChannel.Unreliable ? (int)ENetPacketPeer.FlagUnreliableFragment : (int)ENetPacketPeer.FlagReliable;

        public static bool IsOpen(ENetPacketPeer peer) => peer.GetState() == ENetPacketPeer.PeerState.Connected;
    }

    /// <summary>The listen server's socket (doc 06 §2): remote clients connect to it over ENet.</summary>
    public sealed class EnetServerTransport : IServerTransport
    {
        private readonly ENetConnection _host;
        private readonly Dictionary<ulong, int> _ids = new Dictionary<ulong, int>();
        private readonly Dictionary<int, ENetPacketPeer> _peers = new Dictionary<int, ENetPacketPeer>();
        private int _nextConnection = 1;
        private bool _closed;

        /// <summary>The UDP port the server listens on (the one the OS picked when asked for port 0).</summary>
        public int Port { get; }

        private EnetServerTransport(ENetConnection host)
        {
            _host = host;
            Port = host.GetLocalPort();
        }

        /// <summary>Binds <paramref name="port"/> on every interface (0 = any free port). Null when the port cannot be bound.</summary>
        public static EnetServerTransport? Listen(int port, out Error error)
        {
            var host = new ENetConnection();
            error = host.CreateHostBound("*", port, Enet.MaxPeers, Enet.Channels);
            return error == Error.Ok ? new EnetServerTransport(host) : null;
        }

        public void Poll(IServerTransportHandler handler)
        {
            if (_closed) return;
            while (true)
            {
                var ev = _host.Service(0);
                var type = (ENetConnection.EventType)ev[0].AsInt32();
                if (type == ENetConnection.EventType.None || type == ENetConnection.EventType.Error) return;
                var peer = ev[1].As<ENetPacketPeer>();
                ulong key = peer.GetInstanceId();
                switch (type)
                {
                    case ENetConnection.EventType.Connect:
                    {
                        int id = _nextConnection++;
                        _ids[key] = id;
                        _peers[id] = peer;
                        handler.OnClientConnected(id);
                        break;
                    }
                    case ENetConnection.EventType.Disconnect:
                    {
                        // A peer this side dropped is already forgotten; only a client that left or timed out is news.
                        if (!_ids.Remove(key, out int id)) break;
                        _peers.Remove(id);
                        handler.OnClientDisconnected(id);
                        break;
                    }
                    case ENetConnection.EventType.Receive:
                    {
                        byte[] data = peer.GetPacket();
                        int channel = ev[3].AsInt32();
                        if (channel < Enet.Channels && _ids.TryGetValue(key, out int id))
                            handler.OnClientData(id, (NetChannel)channel, data);
                        break;
                    }
                }
            }
        }

        public void Send(int connectionId, NetChannel channel, byte[] data)
        {
            if (_closed || !_peers.TryGetValue(connectionId, out var peer) || !Enet.IsOpen(peer)) return;
            peer.Send((int)channel, data, Enet.Flags(channel));
        }

        /// <summary>Drops a client once what is already queued for it (the reason) has gone out.</summary>
        public void Disconnect(int connectionId)
        {
            if (_closed || !_peers.Remove(connectionId, out var peer)) return;
            _ids.Remove(peer.GetInstanceId());
            peer.PeerDisconnectLater();
        }

        /// <summary>Puts everything queued this frame on the wire now instead of at the next poll.</summary>
        public void Flush()
        {
            if (!_closed) _host.Flush();
        }

        /// <summary>The host is leaving: every client is told at once, then the socket goes.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            foreach (var peer in _peers.Values) peer.PeerDisconnectNow();
            _peers.Clear();
            _ids.Clear();
            _host.Destroy();
        }
    }

    /// <summary>A remote client's connection to a listen server (doc 06 §2).</summary>
    public sealed class EnetClientTransport : IClientTransport
    {
        /// <summary>How long a host may take to answer the first knock.</summary>
        private const ulong ConnectTimeoutMsec = 5000;

        private readonly ENetConnection _host;
        private readonly ENetPacketPeer _peer;
        private readonly ulong _connectDeadline;
        private bool _connected;
        private bool _closed;
        private bool _reported;

        private EnetClientTransport(ENetConnection host, ENetPacketPeer peer)
        {
            _host = host;
            _peer = peer;
            _connectDeadline = Time.GetTicksMsec() + ConnectTimeoutMsec;
        }

        /// <summary>Starts connecting; the handler hears OnConnected, or OnDisconnected when nobody answers. Null when no socket can be made.</summary>
        public static EnetClientTransport? Connect(string address, int port, out Error error)
        {
            var host = new ENetConnection();
            error = host.CreateHost(1, Enet.Channels);
            if (error != Error.Ok) return null;
            var peer = host.ConnectToHost(address, port, Enet.Channels);
            if (peer == null)
            {
                host.Destroy();
                error = Error.CantConnect;
                return null;
            }
            return new EnetClientTransport(host, peer);
        }

        public void Poll(IClientTransportHandler handler)
        {
            if (_closed) return;
            while (true)
            {
                var ev = _host.Service(0);
                var type = (ENetConnection.EventType)ev[0].AsInt32();
                if (type == ENetConnection.EventType.None)
                {
                    // ENet itself keeps knocking for up to half a minute; nobody waits that long for a join.
                    if (_connected || Time.GetTicksMsec() < _connectDeadline) return;
                    _peer.Reset();
                    type = ENetConnection.EventType.Disconnect;
                }
                switch (type)
                {
                    case ENetConnection.EventType.Connect:
                        _connected = true;
                        handler.OnConnected();
                        break;
                    case ENetConnection.EventType.Receive:
                    {
                        byte[] data = ev[1].As<ENetPacketPeer>().GetPacket();
                        int channel = ev[3].AsInt32();
                        if (channel < Enet.Channels) handler.OnData((NetChannel)channel, data);
                        break;
                    }
                    default:
                        // Disconnect or a socket error: either way the server is gone.
                        Shut();
                        if (!_reported) handler.OnDisconnected();
                        _reported = true;
                        return;
                }
            }
        }

        public void Send(NetChannel channel, byte[] data)
        {
            if (_closed || !Enet.IsOpen(_peer)) return;
            _peer.Send((int)channel, data, Enet.Flags(channel));
        }

        /// <summary>Puts everything queued this frame on the wire now instead of at the next poll.</summary>
        public void Flush()
        {
            if (!_closed) _host.Flush();
        }

        public void Close()
        {
            if (_closed) return;
            if (_peer.GetState() != ENetPacketPeer.PeerState.Disconnected) _peer.PeerDisconnectNow();
            Shut();
        }

        private void Shut()
        {
            if (_closed) return;
            _closed = true;
            _host.Destroy();
        }
    }
}
