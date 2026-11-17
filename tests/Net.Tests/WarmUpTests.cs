using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Net;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>What a host does at boot so that the first join and the first shift cost no slow tick (nit 8) must leave the game as it was.</summary>
    public class WarmUpTests
    {
        private sealed class Quiet : IClientTransportHandler
        {
            public bool Refused, Gone;
            public void OnConnected() { }
            public void OnDisconnected() => Gone = true;
            public void OnData(NetChannel channel, byte[] data) => Refused = data.Length > 0 && data[0] == (byte)MsgType.Reject;
        }

        [Fact]
        public void TheKnockIsRefusedWithoutATickAndLeavesNothingBehind()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, hub);
            int logged = 0;

            // The warm-up of the ENet path: a Hello the server is sure to refuse, pumped without ticking the world.
            var knock = hub.CreateClient();
            var w = new NetWriter();
            new HelloMsg { ProtocolHash = ~server.ProtocolHash, Name = "warm-up" }.Write(w);
            knock.Send(NetChannel.Reliable, w.ToArray());
            hub.Poll(server);
            var heard = new Quiet();
            knock.Poll(heard);
            server.Log = _ => logged++;

            Assert.True(heard.Refused);
            Assert.True(heard.Gone);
            Assert.Equal(0, server.ClientCount);
            Assert.Empty(server.World.Players);
            Assert.Equal(0u, server.World.Tick);

            // The first real player is who they would have been without the knock.
            var plain = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, new LoopbackHub());
            var expected = plain.World.AddPlayer("Ada")!;
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            for (int i = 0; i < 5; i++)
            {
                client.Tick();
                server.Tick();
            }
            Assert.Equal(ClientState.Joined, client.State);
            Assert.Equal(expected.Id, client.World!.LocalId);
            Assert.Equal(expected.Slot, server.World.Players[0].Slot);
            Assert.Equal(1, logged); // "Ada joined", and no word of the knock
        }

        [Fact]
        public void TheSimWarmUpDoesNotChangeTheGameThatFollows()
        {
            var cold = Play(warm: false);
            var warmed = Play(warm: true);

            Assert.Equal(cold.World.Tick, warmed.World.Tick);
            Assert.Equal(cold.World.Run.Phase, warmed.World.Run.Phase);
            Assert.Equal(cold.World.Economy.Wallet, warmed.World.Economy.Wallet);
            Assert.Equal(cold.World.Players[0].Id, warmed.World.Players[0].Id);
            Assert.Equal(cold.World.Players[0].Move.X, warmed.World.Players[0].Move.X);
            Assert.Equal(cold.World.Players[0].Move.Z, warmed.World.Players[0].Move.Z);
            Assert.Equal(cold.World.Intake.Snapshot().Entries.Count, warmed.World.Intake.Snapshot().Entries.Count);
            Assert.Equal(cold.World.Enemies.Count, warmed.World.Enemies.Count);
        }

        private static ServerRuntime Play(bool warm)
        {
            if (warm) SimWarmUp.Run(Shared.Content, 3);
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, QuickStart = true }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            var bot = new DeliveryBot(client);
            for (int i = 0; i < 900; i++)
            {
                bot.Tick();
                client.Tick();
                server.Tick();
            }
            return server;
        }
    }
}
