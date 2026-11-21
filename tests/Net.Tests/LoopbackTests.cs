using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Net;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>ServerRuntime + ClientRuntime over the loopback transport: the whole M0 loop through real serialization.</summary>
    public class LoopbackTests
    {
        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly List<DeliveryBot> Bots = new List<DeliveryBot>();

            public Session(uint seed = 3, bool quickStart = true, string mapId = "test_street")
            {
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed, QuickStart = quickStart, MapId = mapId }, Hub);
            }

            public ClientRuntime AddClient(string name, bool bot = true)
            {
                var client = new ClientRuntime(Shared.Content, Hub.CreateClient(), name);
                Clients.Add(client);
                if (bot) Bots.Add(new DeliveryBot(client));
                return client;
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var b in Bots) b.Tick();
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }
        }

        [Fact]
        public void OnAGeneratedTown_TheClientBuildsItFromTheSeed_AndTheBotsDeliver()
        {
            // Doc 02 §2: nothing of the town goes over the wire but its id, the seed and the hash.
            var s = new Session(seed: 17, mapId: PerformativeMail.Sim.World.WorldGenerator.SmallIsland);
            var ada = s.AddClient("Ada");
            var bo = s.AddClient("Bo");
            s.Run(30 * 60);
            foreach (var c in new[] { ada, bo })
            {
                Assert.Equal(ClientState.Joined, c.State);
                var map = c.World!.Map;
                Assert.NotSame(s.Server.World.Map, map);
                Assert.Equal((s.Server.World.Map.MapHash, s.Server.World.Map.Destinations.Count), (map.MapHash, map.Destinations.Count));
            }
            Assert.True(s.Bots.Sum(b => b.StacksDelivered) >= 4, $"{s.Bots.Sum(b => b.StacksDelivered)} stacks delivered on the generated town");
            Assert.Equal(0, s.Bots.Sum(b => b.StacksMisdelivered));
        }

        [Fact]
        public void ClientJoinsAndReceivesItsState()
        {
            var s = new Session();
            var client = s.AddClient("Ada", bot: false);
            s.Run(10);

            Assert.Equal(ClientState.Joined, client.State);
            var world = client.World!;
            Assert.Equal(s.Server.World.Players[0].Id, world.LocalId);
            Assert.True(world.HasLocal);
            Assert.Equal("Ada", world.Players[world.LocalId].Name);
            Assert.Equal(s.Server.World.Intake.Id, world.IntakeContainerId);
            Assert.Equal(s.Server.World.Players[0].Hotbar.Id, world.Hotbar.Id);
            Assert.Equal(600, world.Global.Quota);
            Assert.True(client.SnapshotsReceived > 0);
            Assert.InRange(world.PhaseSecondsLeft, 238.0, 240.0);
        }

        [Fact]
        public void WrongProtocolHashIsRejected()
        {
            var s = new Session();
            var transport = s.Hub.CreateClient();
            var handler = new RawClient();
            var w = new NetWriter();
            new HelloMsg { ProtocolHash = s.Server.ProtocolHash ^ 1, Name = "old build" }.Write(w);
            transport.Send(NetChannel.Reliable, w.ToArray());
            s.Run(2);
            transport.Poll(handler);

            Assert.Equal(RejectReason.ProtocolMismatch, handler.Rejected);
            Assert.True(handler.Disconnected);
            Assert.Empty(s.Server.World.Players);
            Assert.Equal(0, s.Server.ClientCount);
        }

        [Fact]
        public void NinthPlayerIsRejectedAsServerFull()
        {
            var s = new Session();
            for (int i = 0; i < 8; i++) s.AddClient($"p{i}", bot: false);
            var ninth = s.AddClient("late", bot: false);
            s.Run(5);
            Assert.Equal(8, s.Server.World.Players.Count);
            Assert.Equal(ClientState.Rejected, ninth.State);
            Assert.Equal(RejectReason.ServerFull, ninth.RejectReason);
        }

        [Fact]
        public void MalformedPacketsAreDroppedWithoutTouchingTheSim()
        {
            var s = new Session();
            var good = s.AddClient("Ada", bot: false);
            var raw = s.Hub.CreateClient();
            raw.Send(NetChannel.Reliable, new byte[] { (byte)MsgType.Hello, 1, 2 });
            raw.Send(NetChannel.Reliable, new byte[] { 250 });
            raw.Send(NetChannel.Reliable, new byte[] { (byte)MsgType.Request, 0 });
            s.Run(5);
            Assert.Equal(ClientState.Joined, good.State);
            Assert.Single(s.Server.World.Players);
        }

        [Fact]
        public void PredictionMatchesTheServerExactlyOnACleanLink()
        {
            var s = new Session();
            var client = s.AddClient("Ada", bot: false);
            s.Run(5);
            client.Input.MoveX = 1f;
            client.Input.MoveZ = 0.3f;
            s.Run(60);
            client.Input.MoveX = 0f;
            client.Input.MoveZ = 0f;
            s.Run(10);

            var server = s.Server.World.Players[0];
            var world = client.World!;
            Assert.True(world.Local.X > 20f, "the player should have walked");
            Assert.Equal(server.Move.X, world.Local.X, 4);
            Assert.Equal(server.Move.Z, world.Local.Z, 4);
            Assert.True(client.PredictionErrorMax < 1e-3, $"prediction error {client.PredictionErrorMax}");
        }

        [Fact]
        public void RemotePlayersAreInterpolatedFromSnapshots()
        {
            var s = new Session();
            var a = s.AddClient("Ada", bot: false);
            var b = s.AddClient("Bo", bot: false);
            s.Run(5);
            a.Input.MoveX = 1f;
            s.Run(45);

            var seen = b.World!.Players[a.World!.LocalId];
            Assert.True(seen.Interpolate(b.World.ServerTick - ClientRuntime.InterpolationDelayTicks, out float x, out _, out float z, out _));
            var server = s.Server.World.GetPlayer(a.World.LocalId)!;
            // 100 ms behind a 5 m/s walker is about half a metre.
            Assert.InRange(server.Move.X - x, 0.0f, 1.2f);
            Assert.Equal(server.Move.Z, z, 1);
        }

        [Fact]
        public void BotRunsTheWholeLoop_SpawnPickupWalkDeliverWallet()
        {
            var s = new Session();
            var client = s.AddClient("Bot");
            var bot = s.Bots[0];
            int delivered = 0;
            client.EventReceived += e => { if (e is DeliveredEvent) delivered++; };

            s.Run(30 * 90);

            Assert.True(bot.StacksDelivered >= 3, $"bot delivered {bot.StacksDelivered} stacks (last rejection {bot.LastRejection})");
            Assert.Equal(0, bot.RequestsRejected);
            Assert.True(s.Server.World.Economy.Wallet > 0);
            Assert.Equal(s.Server.World.Economy.TotalDeliveries, delivered);
            Assert.Equal(0, s.Server.World.Economy.TotalMisdeliveries);
            // The 5 Hz global block lags by at most a few ticks; let it settle before comparing.
            s.Bots.Clear();
            s.Run(15);
            Assert.Equal(s.Server.World.Economy.Wallet, client.World!.Global.Wallet);
            Assert.Equal(0, client.ContainerDesyncs);
            Assert.True(client.ContainerVersionsVerified > 10);
            Assert.Equal(0, client.BadPackets);
        }

        [Fact]
        public void MisdeliveringBotIsCharged()
        {
            var s = new Session();
            s.AddClient("Clumsy");
            s.Bots[0].MisdeliverEvery = 1;
            s.Run(30 * 60);
            Assert.True(s.Bots[0].StacksMisdelivered >= 1);
            Assert.True(s.Server.World.Economy.Wallet < 0);
            Assert.True(s.Server.World.Economy.Complaint > 0);
        }

        [Fact]
        public void EightBotsShareTheMapWithoutContainerDesync()
        {
            var s = new Session(seed: 11);
            for (int i = 0; i < 8; i++) s.AddClient($"bot{i}");
            s.Run(30 * 120);

            Assert.Equal(8, s.Server.World.Players.Count);
            int stacks = 0;
            foreach (var bot in s.Bots) stacks += bot.StacksDelivered;
            Assert.True(stacks >= 16, $"8 bots delivered only {stacks} stacks");
            foreach (var c in s.Clients)
            {
                Assert.Equal(ClientState.Joined, c.State);
                Assert.Equal(0, c.ContainerDesyncs);
                Assert.Equal(0, c.BadPackets);
            }
            Assert.True(s.Server.World.Economy.Wallet > 0);
        }

        [Fact]
        public void LoopSurvivesLatencyAndLoss()
        {
            var s = new Session();
            s.Hub.DelayPolls = 3;          // 100 ms each way
            s.Hub.UnreliableLoss = 0.05;
            var client = s.AddClient("Laggy");
            s.Run(30 * 120);

            Assert.True(s.Bots[0].StacksDelivered >= 3, $"delivered {s.Bots[0].StacksDelivered} (last rejection {s.Bots[0].LastRejection})");
            Assert.Equal(0, client.ContainerDesyncs);
            Assert.True(client.PredictionErrorRms < 0.10, $"prediction error RMS {client.PredictionErrorRms:F3} m");
        }

        [Fact]
        public void DisconnectInTheLobbyRemovesThePlayer()
        {
            var s = new Session(quickStart: false);
            var a = s.AddClient("Ada", bot: false);
            var b = s.AddClient("Bo", bot: false);
            s.Run(5);
            Assert.Equal(2, b.World!.Players.Count);
            a.Disconnect();
            s.Run(5);
            Assert.Single(s.Server.World.Players);
            Assert.Single(b.World.Players);
        }

        [Fact]
        public void AGuestWhoLeavesAndComesBack_IsPlayerTwoAgain_OnEveryScreen()
        {
            // Playtest nit 11.
            var s = new Session(quickStart: false);
            var log = new List<string>();
            s.Server.Log = log.Add;
            var host = s.AddClient("Ada", bot: false);
            var guest = s.AddClient("Bo", bot: false);
            s.Run(10);
            var first = guest.World!.LocalId;
            Assert.Equal("Player#2", first.ToString());
            Assert.Equal("Bo", host.World!.Players[first].Name);
            var seen = host.World.Players[first];

            guest.Disconnect();
            s.Clients.Remove(guest);
            s.Run(10);
            Assert.False(host.World.Players.ContainsKey(first));

            var back = s.AddClient("Bo", bot: false);
            s.Run(10);
            Assert.Equal(ClientState.Joined, back.State);
            Assert.Equal(first, back.World!.LocalId);
            Assert.Equal(2, log.Count(line => line == "Bo joined as Player#2"));
            Assert.Equal(1, back.World.Players[first].Slot);
            // The host knows a new carrier under the old number, where the server has them.
            var now = host.World.Players[first];
            Assert.NotSame(seen, now);
            Assert.Equal(("Bo", (byte)1), (now.Name, now.Slot));
            var truth = s.Server.World.GetPlayer(first)!;
            Assert.True(now.Interpolate(host.World.ServerTick, out float x, out _, out float z, out _));
            Assert.InRange(System.MathF.Abs(x - truth.Move.X) + System.MathF.Abs(z - truth.Move.Z), 0f, 0.05f);

            // Somebody else may take the number in the very tick it comes free.
            back.Disconnect();
            s.Clients.Remove(back);
            var other = s.AddClient("Cy", bot: false);
            s.Run(10);
            Assert.Equal(first, other.World!.LocalId);
            Assert.Equal("Cy", host.World.Players[first].Name);
            Assert.Equal(2, host.World.Players.Count);
            Assert.Equal(2, s.Server.World.Players.Count);
            Assert.Equal(0, host.BadPackets + other.BadPackets + host.ContainerDesyncs + other.ContainerDesyncs);
        }

        private sealed class RawClient : IClientTransportHandler
        {
            public RejectReason? Rejected;
            public bool Disconnected;

            public void OnConnected() { }
            public void OnDisconnected() => Disconnected = true;

            public void OnData(NetChannel channel, byte[] data)
            {
                var r = new NetReader(data);
                if ((MsgType)r.U8() == MsgType.Reject) Rejected = RejectMsg.Read(r);
            }
        }
    }
}
