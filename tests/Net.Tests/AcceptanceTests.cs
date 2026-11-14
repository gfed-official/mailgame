using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using Xunit;
using Xunit.Abstractions;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The M0 acceptance criteria of doc 12 that can be held in-process, with their numbers printed
    /// (`dotnet test --logger "console;verbosity=detailed"`). The chest fuzz is <see cref="ChestFuzzTests"/>,
    /// the pay rules are Sim.Tests' DeliveryRulesTests, and `tools/soak.sh` repeats the first and the
    /// last over real sockets and in real time.
    /// </summary>
    public class AcceptanceTests
    {
        private readonly ITestOutputHelper _output;

        public AcceptanceTests(ITestOutputHelper output) => _output = output;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly List<DeliveryBot> Bots = new List<DeliveryBot>();

            public Session(uint seed, int bots, int delayPolls, double loss)
            {
                Hub.DelayPolls = delayPolls;
                Hub.UnreliableLoss = loss;
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed, QuickStart = true }, Hub);
                for (int i = 0; i < bots; i++)
                {
                    var client = new ClientRuntime(Shared.Content, Hub.CreateClient(), $"bot{i}");
                    Clients.Add(client);
                    Bots.Add(new DeliveryBot(client));
                }
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

        /// <summary>
        /// "8 clients connected to a listen server for 10 min with no desync in inventories (server
        /// and client container hashes match on every version)" and "server tick ≤ 2 ms with 8
        /// players on the test map": ten minutes of game time, eight bots delivering through whole
        /// runs, behind 33 ms each way and 2 % loss.
        /// </summary>
        [Fact]
        public void EightClientsForTenMinutes_NoContainerDesync_TicksUnderTwoMilliseconds()
        {
            var s = new Session(seed: 5, bots: 8, delayPolls: 1, loss: 0.02);
            s.Run(30 * 600);

            Assert.Equal(8, s.Server.World.Players.Count);
            int versions = 0, stacks = 0;
            foreach (var c in s.Clients)
            {
                Assert.Equal(ClientState.Joined, c.State);
                Assert.Equal(0, c.ContainerDesyncs);
                Assert.Equal(0, c.BadPackets);
                // Every delta a client is sent carries the server's hash of that version; each one matched.
                Assert.True(c.ContainerVersionsVerified > 100, $"{c.Name} verified only {c.ContainerVersionsVerified} container versions");
                versions += c.ContainerVersionsVerified;
                foreach (var mirror in c.World!.Containers.Values)
                    Assert.Equal(s.Server.World.GetContainer(mirror.Id)!.Version, mirror.Version);
            }
            foreach (var b in s.Bots)
            {
                Assert.True(b.StacksDelivered > 0, "a bot delivered nothing in ten minutes");
                stacks += b.StacksDelivered;
            }

            var ticks = s.Server.TickTimes;
            _output.WriteLine($"8 clients, 10 min: {versions} container versions verified, 0 desyncs, {stacks} stacks delivered; server tick {ticks}");
            Assert.Equal(30 * 600 - TickStats.WarmUpTicks, ticks.Count);
            Assert.True(ticks.MeanMs <= 2.0, $"server tick {ticks}");
            Assert.True(ticks.PercentileMs(0.99) <= 2.0, $"server tick {ticks}");
        }

        /// <summary>
        /// "Prediction error under 200 ms simulated latency with 5 % loss stays below 10 cm RMS for
        /// the local player; no visible rubber-banding at 100 ms." A bot walks its rounds for ten
        /// minutes. "200 ms" is read both ways — as the round trip and as each direction — and both
        /// must hold. Latency alone must cost nothing at all (the replay is exact): that is "no
        /// rubber-banding at 100 ms". With loss, an input is only gone when the three packets that
        /// carried it are all lost, and the correction is then the step or two that input was worth.
        /// </summary>
        [Theory]
        [InlineData(3, 0.05, 0.10, 0.50)] // 100 ms each way: 200 ms round trip
        [InlineData(6, 0.05, 0.10, 0.50)] // 200 ms each way
        [InlineData(3, 0.00, 0.01, 0.01)] // 100 ms each way, nothing lost
        public void PredictionErrorStaysSmallBehindLatencyAndLoss(int delayPolls, double loss, double maxRms, double maxSingle)
        {
            var s = new Session(seed: 3, bots: 1, delayPolls: delayPolls, loss: loss);
            s.Run(30 * 600);

            var client = s.Clients[0];
            _output.WriteLine($"{delayPolls * 1000 / 30} ms each way, {loss:P0} loss: prediction error rms {client.PredictionErrorRms * 100:0.00} cm, "
                + $"max {client.PredictionErrorMax * 100:0.0} cm; {s.Bots[0].StacksDelivered} stacks delivered");
            Assert.True(s.Bots[0].StacksDelivered >= 2, $"the bot delivered {s.Bots[0].StacksDelivered} stacks (last rejection {s.Bots[0].LastRejection})");
            Assert.Equal(0, client.ContainerDesyncs);
            Assert.True(client.PredictionErrorRms < maxRms, $"prediction error RMS {client.PredictionErrorRms:F3} m");
            Assert.True(client.PredictionErrorMax < maxSingle, $"largest single correction {client.PredictionErrorMax:F3} m");
        }

        /// <summary>The instrument itself: a player the server moves by a metre is a one-metre prediction error, once.</summary>
        [Fact]
        public void TheMeasureSeesAServerSideCorrection()
        {
            var s = new Session(seed: 3, bots: 1, delayPolls: 3, loss: 0.05);
            s.Run(30 * 20);
            Assert.True(s.Clients[0].PredictionErrorMax < 0.01);

            s.Server.World.Players[0].Move.X += 1f;
            s.Run(30);

            Assert.InRange(s.Clients[0].PredictionErrorMax, 0.9, 1.1);
            Assert.True(s.Clients[0].PredictionErrorRms > 0.01);
            s.Run(30 * 20);
            Assert.InRange(s.Clients[0].PredictionErrorMax, 0.9, 1.1); // and it was corrected once, not again and again
        }
    }
}
