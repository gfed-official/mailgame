using System;
using PerformativeMail.Sim.Net;

namespace PerformativeMail.Server.Transport
{
    /// <summary>
    /// Several transports behind one server (doc 06 §1 listen server): the host's own client comes
    /// in over loopback, everybody else over ENet, and the ServerRuntime sees one set of
    /// connections. A child's connection id <c>c</c> on child <c>i</c> of <c>n</c> becomes
    /// <c>c * n + i</c>, which stays unique and never reused as long as the child's ids are.
    /// </summary>
    public sealed class CompositeServerTransport : IServerTransport
    {
        private readonly IServerTransport[] _children;
        private readonly Relay[] _relays;

        public CompositeServerTransport(params IServerTransport[] children)
        {
            if (children.Length == 0) throw new ArgumentException("at least one transport", nameof(children));
            _children = children;
            _relays = new Relay[children.Length];
            for (int i = 0; i < children.Length; i++) _relays[i] = new Relay(i, children.Length);
        }

        public void Poll(IServerTransportHandler handler)
        {
            for (int i = 0; i < _children.Length; i++)
            {
                _relays[i].Handler = handler;
                _children[i].Poll(_relays[i]);
            }
        }

        public void Send(int connectionId, NetChannel channel, byte[] data) =>
            _children[connectionId % _children.Length].Send(connectionId / _children.Length, channel, data);

        public void Disconnect(int connectionId) =>
            _children[connectionId % _children.Length].Disconnect(connectionId / _children.Length);

        private sealed class Relay : IServerTransportHandler
        {
            private readonly int _index;
            private readonly int _count;
            public IServerTransportHandler Handler = null!;

            public Relay(int index, int count)
            {
                _index = index;
                _count = count;
            }

            public void OnClientConnected(int connectionId) => Handler.OnClientConnected(connectionId * _count + _index);
            public void OnClientDisconnected(int connectionId) => Handler.OnClientDisconnected(connectionId * _count + _index);
            public void OnClientData(int connectionId, NetChannel channel, byte[] data) =>
                Handler.OnClientData(connectionId * _count + _index, channel, data);
        }
    }
}
