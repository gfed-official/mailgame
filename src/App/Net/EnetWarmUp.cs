using Godot;
using PerformativeMail.Sim.Net;

namespace PerformativeMail.App.Net
{
    /// <summary>
    /// The first ENet connection a process handles costs the server 6-7 ms of one tick: Godot makes
    /// its C# wrappers and the runtime compiles the connect, receive, send and disconnect paths. The
    /// Welcome itself is 124 bytes and 0.02 ms. So a host knocks on its own door once at boot, with
    /// a Hello it is sure to refuse, and the first real guest finds every path already walked.
    /// </summary>
    internal static class EnetWarmUp
    {
        /// <summary>The knock is over loopback and takes a few milliseconds; a machine this stuck starts cold instead.</summary>
        private const ulong TimeoutMsec = 250;

        private sealed class Knock : IClientTransportHandler
        {
            private readonly EnetClientTransport _client;
            private readonly uint _hash;
            public bool Refused;
            public bool Over;

            public Knock(EnetClientTransport client, uint hash)
            {
                _client = client;
                _hash = hash;
            }

            public void OnConnected()
            {
                var w = new NetWriter(16);
                new HelloMsg { ProtocolHash = _hash, Name = "warm-up" }.Write(w);
                _client.Send(NetChannel.Reliable, w.ToArray());
            }

            public void OnData(NetChannel channel, byte[] data) => Refused = true;
            public void OnDisconnected() => Over = true;
        }

        /// <summary>
        /// Connects to <paramref name="enet"/> from this process, says Hello with a hash that is not
        /// <paramref name="protocolHash"/>, and pumps <paramref name="transport"/> into
        /// <paramref name="server"/> until the refusal has come back and the connection is gone.
        /// No player is made. False when the knock did not finish in time.
        /// </summary>
        public static bool Run(EnetServerTransport enet, IServerTransport transport, IServerTransportHandler server, uint protocolHash)
        {
            var client = EnetClientTransport.Connect("127.0.0.1", enet.Port, out _);
            if (client == null) return false;
            var knock = new Knock(client, ~protocolHash);
            ulong deadline = Time.GetTicksMsec() + TimeoutMsec;
            while (!knock.Over && Time.GetTicksMsec() < deadline)
            {
                client.Poll(knock);
                client.Flush();
                transport.Poll(server);
                enet.Flush();
                OS.DelayMsec(1);
            }
            client.Close();
            // The server hears the goodbye too, so nothing of the knock is left on its side.
            transport.Poll(server);
            return knock.Refused && knock.Over;
        }
    }
}
