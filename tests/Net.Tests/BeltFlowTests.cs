using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Conveyor belts over the loopback transport (doc 06 §4 belt replication): a drag line asked
    /// for by a client, the client's own belt sim moving what the server's events put on, hand on
    /// and take off; the checksums agreeing; whoever joins late seeing what rides; and a belt's
    /// delivery in the feed.
    /// </summary>
    public class BeltFlowTests
    {
        private const int Row = 12;

        private sealed class Session
        {
            public readonly LoopbackHub Hub = new LoopbackHub();
            public readonly ServerRuntime Server;
            public readonly List<ClientRuntime> Clients = new List<ClientRuntime>();
            public readonly ContentDb Content = Shared.Content;

            public Session(int delay = 0)
            {
                Hub.DelayPolls = delay;
                Server = new ServerRuntime(Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, Hub);
            }

            public SimWorld Sim => Server.World;

            public ClientRuntime Join(string name)
            {
                var client = new ClientRuntime(Content, Hub.CreateClient(), name, PlaytestFlags.None, 0);
                Clients.Add(client);
                Run(10 + Hub.DelayPolls * 2);
                return client;
            }

            public void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in Clients) c.Tick();
                    Server.Tick();
                }
            }

            public Player Truth(ClientRuntime client) => Sim.GetPlayer(client.World!.LocalId)!;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        /// <summary>Which mail is on which segment, lane by lane in order: what the checksum covers.</summary>
        private static string Layout(BeltNetwork belts) =>
            string.Join(" | ", belts.Segments.OrderBy(s => s.Head).Select(s =>
                s.Head + ":" + string.Join(",", s.Items.OrderBy(it => it.Lane).ThenByDescending(it => it.Pos).Select(it => $"{it.Lane}/{it.Mail.Id}"))));

        private static (Session, ClientRuntime) Built(int delay = 0)
        {
            var session = new Session(delay);
            var host = session.Join("host");
            Assert.NotEqual(0u, host.PressGo());
            session.Run(10 + delay * 2);
            Assert.Equal(RunPhase.Prep, session.Sim.Run.Phase);
            var truth = session.Truth(host);
            truth.Move.X = Centre(9);
            truth.Move.Z = Centre(Row) + 1f;
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("plank"), 20)));
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("iron_ingot"), 20)));
            session.Run(10 + delay * 2);
            var belt = session.Content.Recipes.Single(r => r.Building.Id == "belt_mk1");
            // Two lines into one tile, then on to the mailbox of number 1.
            Assert.NotEqual(0u, host.PlaceLine(belt, 5, Row, 7, Row, 0));
            Assert.NotEqual(0u, host.PlaceLine(belt, 8, Row - 2, 8, Row - 1, 1));
            Assert.NotEqual(0u, host.PlaceLine(belt, 8, Row, 11, Row, 0));
            session.Run(10 + delay * 2);
            Assert.Equal(9, session.Sim.Constructs.All.Count);
            Assert.Equal(3, host.World!.Belts.Segments.Count);
            return (session, host);
        }

        [Fact]
        public void ADragLine_ByAClient_IsBuiltTileByTile_AndItsGhostSaysWhichTilesWouldBe()
        {
            var session = new Session();
            var host = session.Join("host");
            Assert.NotEqual(0u, host.PressGo());
            session.Run(10);
            var truth = session.Truth(host);
            truth.Move.X = Centre(9);
            truth.Move.Z = Centre(Row) + 1f;
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("plank"), 3)));
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("iron_ingot"), 9)));
            session.Run(10);
            var belt = session.Content.Recipes.Single(r => r.Building.Id == "belt_mk1");
            var states = new List<(int, int, OpResult)>();
            // From the verge east into the mailbox's tile: five open, the mailbox blocked; three planks.
            Assert.True(host.World!.LineStates(belt, 7, Row, 12, Row, states));
            Assert.Equal(new[] { OpResult.Ok, OpResult.Ok, OpResult.Ok, OpResult.MissingMaterials, OpResult.MissingMaterials, OpResult.Blocked },
                states.Select(t => t.Item3));
            Assert.False(host.World.LineStates(belt, 7, Row, 9, Row + 1, states));
            Assert.NotEqual(0u, host.PlaceLine(belt, 7, Row, 12, Row, 0));
            session.Run(10);
            Assert.Equal(new[] { 7, 8, 9 }, session.Sim.Constructs.All.Select(c => c.Tx));
            Assert.Equal(3, host.World.Constructs.All.Count);
            Assert.Equal(0, BuildRules.Have(host.World.Hotbar, host.World.Inventory, session.Content.Item("plank")));
            // Nothing left to lay with: no request.
            Assert.Equal(0u, host.PlaceLine(belt, 10, Row, 11, Row, 0));
        }

        [Fact]
        public void TheClientsBelts_FollowTheServers_ChecksumsAgree_AndALateJoinerSeesWhatRides()
        {
            var (session, host) = Built(delay: 3);
            var sim = session.Sim;
            var to = sim.Map.Destinations.Single(d => d.Address.Number == 1);
            var west = sim.Belts.SegmentAt(5, Row)!;
            var north = sim.Belts.SegmentAt(8, Row - 2)!;
            uint id = 960000;
            for (int i = 0; i < 6 * 30; i++)
            {
                if (i % 8 == 0) sim.PutOnBelt(i % 16 == 0 ? west : north, TestMail.Letter(session.Content, to.Address, id++, sim.Run.Shift));
                session.Run(1);
            }
            // Backed up at the shut mailbox (Prep), nothing moves: what is where is the same on both ends.
            session.Run(10 * 30);
            Assert.True(sim.Belts.ItemCount > 10);
            Assert.Equal(Layout(sim.Belts), Layout(host.World!.Belts));
            // Never ahead of the server: nothing drawn past a segment's end.
            foreach (var s in host.World.Belts.Segments)
                Assert.All(s.Items, it => Assert.True(it.Pos <= s.Length + 1e-3f));
            session.Run(3 * 30);
            Assert.Equal(0, host.World.BeltMismatches);

            var late = session.Join("late");
            session.Run(10);
            Assert.Equal(Layout(sim.Belts), Layout(late.World!.Belts));
            session.Run(3 * 30);
            Assert.Equal(0, late.World.BeltMismatches);

            // Into Delivery: the mailbox takes them one by one; the feed says a belt did it.
            Assert.NotEqual(0u, host.PressGo());
            Assert.NotEqual(0u, late.PressGo());
            session.Run(10);
            Assert.Equal(RunPhase.Delivery, sim.Run.Phase);
            int before = sim.Belts.ItemCount;
            session.Run(5 * 30);
            Assert.True(sim.Belts.ItemCount <= before - 8, $"{before} → {sim.Belts.ItemCount}");
            // Until the belts are empty (two letters a second go in): then both ends agree again.
            for (int i = 0; i < 60 * 30 && sim.Belts.ItemCount > 0; i++) session.Run(1);
            session.Run(20);
            Assert.Equal(0, sim.Belts.ItemCount);
            Assert.Equal(Layout(sim.Belts), Layout(host.World.Belts));
            Assert.Equal(sim.Economy.Wallet, host.World.Global.Wallet);
            Assert.Equal(0, host.World.BeltMismatches + late.World.BeltMismatches);
        }

        [Fact]
        public void AClientWhoseBeltIsOff_IsSentTheSegmentWhole()
        {
            var (session, host) = Built();
            var sim = session.Sim;
            var to = sim.Map.Destinations.Single(d => d.Address.Number == 1);
            var merged = sim.Belts.SegmentAt(8, Row)!;
            sim.PutOnBelt(merged, TestMail.Letter(session.Content, to.Address, 950001, sim.Run.Shift));
            session.Run(10);
            Assert.Equal(Layout(sim.Belts), Layout(host.World!.Belts));
            // The client loses an item (a bug, a missed event): the next checksum puts it right.
            Assert.True(host.World.Belts.RemoveMail(950001));
            session.Run(BeltChecksum + 10);
            Assert.True(host.World.BeltMismatches >= 1);
            Assert.Equal(Layout(sim.Belts), Layout(host.World.Belts));
            int mismatches = host.World.BeltMismatches;
            session.Run(2 * BeltChecksum);
            Assert.Equal(mismatches, host.World.BeltMismatches);
        }

        private const int BeltChecksum = SimWorld.BeltChecksumTicks;

        private static void LookAt(ClientRuntime client, int tx, int tz) =>
            ViewRay.Aim(client.World!.Local, Centre(tx), 0f, Centre(tz), out client.Input.YawRadians, out client.Input.PitchRadians);

        [Fact]
        public void BuildMode_PressHoldAndLetGo_LaysALine_ABeltTheWayItWasDrawn_AWallAlongIt()
        {
            var session = new Session();
            var host = session.Join("host");
            Assert.NotEqual(0u, host.PressGo());
            session.Run(10);
            var truth = session.Truth(host);
            truth.Move.X = Centre(8);
            truth.Move.Z = Centre(Row) + 1f;
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("plank"), 10)));
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("iron_ingot"), 10)));
            Assert.True(session.Sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("log"), 9)));
            session.Run(10);
            var build = new BuildController(host);
            Assert.True(build.Toggle());
            var recipes = session.Content.Recipes.ToList();
            Assert.True(build.PressKey(recipes.FindIndex(r => r.Building.Id == "belt_mk1")));

            // Drawn from east to west: the belts run west.
            LookAt(host, 9, Row);
            build.Update(Dt, true, false, placeHeld: true);
            Assert.Equal(1, build.Ghost.Line!.Count);
            LookAt(host, 5, Row);
            build.Update(Dt, false, false, placeHeld: true);
            Assert.Equal(5, build.Ghost.Line!.Count);
            Assert.Equal((9, Row, 2), (build.Ghost.Tx, build.Ghost.Tz, build.Ghost.Rotation));
            // A little off the line: it keeps to the row.
            LookAt(host, 5, Row - 1);
            build.Update(Dt, false, false, placeHeld: true);
            Assert.All(build.Ghost.Line!, t => Assert.Equal(Row, t.Tz));
            Assert.Equal(5, build.Ghost.LineOk);
            build.Update(Dt, false, false, placeHeld: false);
            session.Run(10);
            build.Update(Dt, false, false, placeHeld: false);
            Assert.Null(build.Ghost.Line);
            Assert.Equal(new[] { 9, 8, 7, 6, 5 }, session.Sim.Constructs.All.Select(c => c.Tx));
            Assert.All(session.Sim.Constructs.All, c => Assert.Equal(2, c.Rotation));
            var seg = Assert.Single(host.World!.Belts.Segments);
            Assert.Equal(9 + Row * host.World.Map.Cols, seg.Head);

            // A wall drawn down a column stands along it; three Logs a wall, three walls.
            Assert.True(build.PressKey(recipes.FindIndex(r => r.Building.Id == "wall_wood")));
            truth.Move.X = Centre(9);
            truth.Move.Z = Centre(Row - 2) + 1f;
            session.Run(10);
            LookAt(host, 8, Row - 2);
            build.Update(Dt, true, false, placeHeld: true);
            LookAt(host, 8, Row - 1);
            build.Update(Dt, false, false, placeHeld: true);
            Assert.Equal(2, build.Ghost.Line!.Count);
            build.Update(Dt, false, false, placeHeld: false);
            session.Run(10);
            var walls = session.Sim.Constructs.All.Where(c => c.Def.Id == "wall_wood").ToList();
            Assert.Equal(2, walls.Count);
            Assert.All(walls, w => Assert.True(w.AlongZ));

            // A press let go where it began builds the one tile, as before.
            LookAt(host, 9, Row - 3);
            build.Update(Dt, true, false, placeHeld: false);
            session.Run(10);
            Assert.NotNull(session.Sim.Constructs.At(9, Row - 3));
        }

        private const double Dt = 1.0 / 30.0;
    }

    internal static class TestMail
    {
        public static MailItem Letter(ContentDb content, AddressId address, uint id, int shift)
        {
            var kind = content.MailKind("letter");
            return new MailItem { Id = id, Kind = kind.Index, Address = address, Value = (ushort)kind.BaseValue, SpawnShift = (byte)shift, DeadlineShift = (byte)shift };
        }
    }
}
