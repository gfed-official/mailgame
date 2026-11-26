using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Run;
using Xunit;
using Xunit.Abstractions;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Doc 12 M3's acceptance criteria held in-process, with their numbers printed
    /// (`dotnet test --logger "console;verbosity=detailed"`).
    /// </summary>
    public class M3AcceptanceTests
    {
        private const int Tps = 30;
        private readonly ITestOutputHelper _output;

        public M3AcceptanceTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// "40 enemies in interest replicated to 8 clients within the 40 kbps per-client budget; 80 kbps
        /// worst case" (doc 06 §4.2: "≤ 40 kbps down, ≤ 8 kbps up"). Eight bots deliver their rounds on
        /// the test street for two minutes while forty raiders walk at them, every one within each
        /// client's interest (the street is far smaller than its 150 m). What is counted is the payload
        /// the server offers each connection (design choice 146): the mean over the two minutes is held
        /// to 40 kbps, the busiest second to 80; what each client sends, to 8. 33 ms each way, 2 % lost.
        /// </summary>
        [Fact]
        public void FortyRaidersToEightClients_StayWithinTheBandwidthBudget()
        {
            var hub = new LoopbackHub { DelayPolls = 1, UnreliableLoss = 0.02 };
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 5, QuickStart = true, Raids = false }, hub);
            var clients = new List<ClientRuntime>();
            var bots = new List<DeliveryBot>();
            for (int i = 0; i < 8; i++)
            {
                var c = new ClientRuntime(content, hub.CreateClient(), $"bot{i}", PlaytestFlags.NoTimer, 1); // the clock stands: the raiders stay the whole count
                clients.Add(c);
                bots.Add(new DeliveryBot(c));
            }
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var b in bots) b.Tick();
                    foreach (var c in clients) c.Tick();
                    server.Tick();
                }
            }
            Run(Tps * 3);
            var sim = server.World;
            Assert.Equal(8, sim.Players.Count);
            foreach (var p in sim.Players) p.MaxHp = p.Hp = 1e9f;

            // Forty raiders spread over the street around the Post Office, of the kinds that neither go off
            // (a Wall Breaker is gone with its blast) nor go for the Post Office (a Tank or a Giant fells it
            // and ends the run: this counts the raiders' bytes, not the run).
            var po = sim.Map.PostOffice;
            var kinds = new[] { "barbarian", "archer", "hog_rider", "swarmling", "balloon" }.Select(content.Enemy).ToArray();
            var rng = new Random(11);
            void TopUp()
            {
                while (sim.Enemies.Count < 40)
                {
                    float x = po.IntakeX + (float)(rng.NextDouble() * 40.0 - 20.0), z = po.IntakeZ + (float)(rng.NextDouble() * 16.0 - 8.0);
                    var e = sim.SpawnEnemy(kinds[sim.Enemies.Count % kinds.Length], x, z);
                    if (e == null) continue;
                    e.Hp = e.MaxHp = 1e9f; // nobody falls: the forty stand for the whole count
                }
            }
            TopUp();
            Run(Tps * 2);

            const int seconds = 120;
            var down0 = clients.Select((_, i) => hub.BytesDown.GetValueOrDefault(i + 1)).ToArray();
            var up0 = clients.Select((_, i) => hub.BytesUp.GetValueOrDefault(i + 1)).ToArray();
            var last = (long[])down0.Clone();
            double peak = 0;
            int fewest = 40;
            for (int s = 0; s < seconds; s++)
            {
                TopUp();
                Run(Tps);
                fewest = Math.Min(fewest, sim.Enemies.Count);
                for (int i = 0; i < clients.Count; i++)
                {
                    long now = hub.BytesDown.GetValueOrDefault(i + 1);
                    peak = Math.Max(peak, (now - last[i]) * 8 / 1000.0);
                    last[i] = now;
                }
            }
            var down = clients.Select((_, i) => (hub.BytesDown.GetValueOrDefault(i + 1) - down0[i]) * 8 / 1000.0 / seconds).ToArray();
            var up = clients.Select((_, i) => (hub.BytesUp.GetValueOrDefault(i + 1) - up0[i]) * 8 / 1000.0 / seconds).ToArray();
            int drawn = clients.Min(c => c.World!.Enemies.Count);
            _output.WriteLine($"8 clients, 40 raiders, {seconds} s: down mean {down.Average():0.0} kbps (most {down.Max():0.0}), busiest second {peak:0.0} kbps; up most {up.Max():0.00} kbps; each client drew {drawn}+ raiders, {bots.Sum(b => b.StacksDelivered)} stacks delivered");

            Assert.True(fewest >= 40, $"only {fewest} raiders stood");
            Assert.True(drawn >= 40, $"a client drew only {drawn} raiders: not all were in its interest");
            Assert.All(clients, c => Assert.Equal(0, c.ContainerDesyncs + c.BadPackets));
            // Doc 06 §4.2's deltas are against what the client acked: with 2 % lost, every snapshot that came was read.
            Assert.All(clients, c => Assert.Equal(0, c.UndecodableSnapshots));
            Assert.All(down, kbps => Assert.True(kbps <= 40.0, $"a client was sent {kbps:0.0} kbps on average"));
            Assert.True(peak <= 80.0, $"a client was sent {peak:0.0} kbps in its busiest second");
            Assert.All(up, kbps => Assert.True(kbps <= 8.0, $"a client sent {kbps:0.00} kbps"));
        }
    }
}
