using System;
using System.IO;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Net;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Hot reload on a host (doc 07 §6): the server and the host's own client share the content;
    /// after a reload both step with the new numbers, so the prediction keeps with the server,
    /// and a guest whose content is the old one is turned away at the door.
    /// </summary>
    public sealed class HotReloadFlowTests : IDisposable
    {
        private readonly string _dir;

        public HotReloadFlowTests()
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            _dir = Path.Combine(Path.GetTempPath(), "pm-hotreload-" + Guid.NewGuid().ToString("N"));
            Copy(root, _dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        [Fact]
        public void AfterAReload_TheHostsClientPredictsWithTheNewNumbers_AndAGuestWithTheOldContentIsTurnedAway()
        {
            var source = new DirectoryContentSource(_dir);
            var content = ContentDb.Load(source);
            var old = ContentDb.Load(source);
            var hub = new LoopbackHub();
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, QuickStart = true, Raids = false }, hub);
            var host = new ClientRuntime(content, hub.CreateClient(), "Ada");
            void Step(int ticks, params ClientRuntime[] clients)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in clients) c.Tick();
                    server.Tick();
                }
            }
            Step(20, host);
            Assert.Equal(ClientState.Joined, host.State);
            uint hash = server.ProtocolHash;

            string balance = Path.Combine(_dir, "balance.json");
            File.WriteAllText(balance, File.ReadAllText(balance).Replace("\"walkSpeed\": 5.0", "\"walkSpeed\": 7.5"));
            Assert.True(content.ReloadFrom(source, out var problems), string.Join("\n", problems));
            server.ContentReloaded();
            host.ContentReloaded();
            Assert.NotEqual(hash, server.ProtocolHash);
            Assert.Equal(Protocol.Hash(content), server.ProtocolHash);

            // A second of walking east: at the speed of the new balance, and the prediction never off.
            var world = host.World!;
            var truth = server.World.GetPlayer(world.LocalId)!;
            host.Input.MoveX = 1f;
            host.Input.YawRadians = 0f;
            float fastest = 0f;
            for (int i = 0; i < 30; i++)
            {
                float x = truth.Move.X;
                Step(1, host);
                fastest = MathF.Max(fastest, (truth.Move.X - x) * 30f);
            }
            host.Input.MoveX = 0f;
            Step(10, host);
            Assert.Equal(7.5f, fastest, 1);
            Assert.True(host.PredictionErrorMax < 0.01, $"the prediction was off by up to {host.PredictionErrorMax} m after the reload");

            // A guest still on the old content is not let in; one with the new content is.
            var stale = new ClientRuntime(old, hub.CreateClient(), "Bo");
            Step(10, host, stale);
            Assert.Equal((ClientState.Rejected, RejectReason.ProtocolMismatch), (stale.State, stale.RejectReason));
            var fresh = new ClientRuntime(ContentDb.Load(source), hub.CreateClient(), "Cy");
            Step(20, host, fresh);
            Assert.Equal(ClientState.Joined, fresh.State);
        }
    }
}
