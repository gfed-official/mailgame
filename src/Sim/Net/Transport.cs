namespace PerformativeMail.Sim.Net
{
    /// <summary>Doc 06 §2 channels.</summary>
    public enum NetChannel : byte
    {
        /// <summary>Unreliable, unordered: inputs and snapshots.</summary>
        Unreliable = 0,
        /// <summary>Reliable, ordered: events and requests.</summary>
        Reliable = 1,
        /// <summary>Reliable, ordered, bulk: join state.</summary>
        Bulk = 2,
    }

    public interface IServerTransportHandler
    {
        void OnClientConnected(int connectionId);
        void OnClientDisconnected(int connectionId);
        void OnClientData(int connectionId, NetChannel channel, byte[] data);
    }

    /// <summary>Server end of a transport (loopback, ENet, ...). Connection ids are never reused within a session.</summary>
    public interface IServerTransport
    {
        /// <summary>Delivers everything that arrived since the last poll, in arrival order.</summary>
        void Poll(IServerTransportHandler handler);
        void Send(int connectionId, NetChannel channel, byte[] data);
        void Disconnect(int connectionId);
    }

    public interface IClientTransportHandler
    {
        void OnConnected();
        void OnDisconnected();
        void OnData(NetChannel channel, byte[] data);
    }

    public interface IClientTransport
    {
        void Poll(IClientTransportHandler handler);
        void Send(NetChannel channel, byte[] data);
        void Close();
    }
}
