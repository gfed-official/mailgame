using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The listen server's shape: the host on one transport, everybody else on another, one ServerRuntime.</summary>
    public class CompositeTransportTests
    {
        [Fact]
        public void ClientsOfTwoTransportsShareOneServer()
        {
            var local = new LoopbackHub();
            var remote = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, new CompositeServerTransport(local, remote));
            var host = new ClientRuntime(Shared.Content, local.CreateClient(), "Host");
            // Both hubs hand out connection 1: the composite has to keep them apart.
            var guest = new ClientRuntime(Shared.Content, remote.CreateClient(), "Guest");
            var guest2 = new ClientRuntime(Shared.Content, remote.CreateClient(), "Guest 2");

            for (int i = 0; i < 20; i++)
            {
                host.Tick();
                guest.Tick();
                guest2.Tick();
                server.Tick();
            }

            Assert.Equal(3, server.ClientCount);
            Assert.Equal(ClientState.Joined, host.State);
            Assert.Equal(ClientState.Joined, guest.State);
            Assert.Equal(ClientState.Joined, guest2.State);
            Assert.NotEqual(host.World!.LocalId, guest.World!.LocalId);
            Assert.NotEqual(guest.World!.LocalId, guest2.World!.LocalId);
            Assert.Equal(3, guest.World!.Players.Count);
            Assert.Equal("Guest", guest.World!.Players[guest.World.LocalId].Name);
            Assert.True(host.World!.IsHost);
            Assert.False(guest.World!.IsHost);

            guest.Disconnect();
            for (int i = 0; i < 10; i++)
            {
                host.Tick();
                guest2.Tick();
                server.Tick();
            }
            Assert.Equal(2, server.ClientCount);
            Assert.Equal(2, host.World!.Players.Count);
            Assert.Equal(ClientState.Joined, guest2.State);
        }
    }
}
