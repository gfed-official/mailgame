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
        public void BuildMode_TheWheel_StepsBetweenTheBeltTiers_OnOneKey_AndTheKeyRemembersTheChoice()
        {
            var session = new Session();
            var host = session.Join("host");
            Assert.NotEqual(0u, host.PressGo());
            session.Run(10);
            var build = new BuildController(host);
            Assert.True(build.Toggle());
            var content = session.Content;
            var belt = content.Recipes.Single(r => r.Building.Id == "belt_mk1");
            var fast = content.Recipes.Single(r => r.Building.Id == "belt_mk2");
            var splitter = content.Recipes.Single(r => r.Building.Id == "splitter");
            Assert.True(build.PressKey(belt.Key));
            Assert.Same(belt, build.Recipe);
            Assert.Equal($"[{belt.Key + 1}] Conveyor Belt 1/2   ·   Plank x1, Iron Ingot x1", HudText.RecipeCard(belt, belt.Key, content.RecipeKeys[belt.Key]));
            Assert.True(build.Cycle(1));
            Assert.Same(fast, build.Recipe);
            Assert.Equal($"[{belt.Key + 1}] Fast Belt 2/2   ·   Plank x1, Iron Ingot x2", HudText.RecipeCard(fast, belt.Key, content.RecipeKeys[belt.Key]));
            // Round again, either way.
            Assert.True(build.Cycle(1));
            Assert.Same(belt, build.Recipe);
            Assert.True(build.Cycle(-1));
            Assert.Same(fast, build.Recipe);
            // A recipe alone stays itself; back on the belts' key, the Fast Belt is still the choice.
            Assert.True(build.PressKey(splitter.Key));
            Assert.True(build.Cycle(1));
            Assert.Same(splitter, build.Recipe);
            Assert.Same(fast, build.Chosen(belt.Key));
            Assert.True(build.PressKey(belt.Key));
            Assert.Same(fast, build.Recipe);
            // Locked behind its blueprint, it says so.
            Assert.Equal(OpResult.NeedsBlueprint, host.World!.RecipeState(fast));
            Assert.False(build.PressKey(content.RecipeKeys.Count));
            build.Leave();
            Assert.False(build.Cycle(1));
            Assert.Equal("-", HudText.KeyName(10));
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

        [Fact]
        public void ThroughASplitterAndAMerger_TheClientsMirrorFollows()
        {
            var session = new Session(delay: 2);
            var host = session.Join("host");
            Assert.NotEqual(0u, host.PressGo());
            session.Run(15);
            var sim = session.Sim;
            var truth = session.Truth(host);
            truth.Move.X = Centre(10);
            truth.Move.Z = Centre(5);
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("plank"), 20)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(session.Content.Item("iron_ingot"), 20)));
            session.Run(15);
            RecipeDef R(string id) => session.Content.Recipes.Single(r => r.Building.Id == id);
            // In at (8, 5), split three ways at (9, 5): north, east, south; the north and south ways
            // turn back east and meet the middle one in a merger at (11, 5), out at (12, 5).
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 8, 5, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("splitter"), 9, 5, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 9, 4, 3));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 9, 6, 1));
            session.Run(10);
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 9, 3, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 9, 7, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 10, 5, 0));
            session.Run(10);
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 10, 3, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 10, 7, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 11, 3, 1));
            session.Run(10);
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 11, 4, 1));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 11, 7, 3));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 11, 6, 3));
            session.Run(10);
            Assert.NotEqual(0u, host.PlaceConstruct(R("merger"), 11, 5, 0));
            Assert.NotEqual(0u, host.PlaceConstruct(R("belt_mk1"), 12, 5, 0));
            session.Run(15);
            Assert.Equal(15, sim.Constructs.All.Count);
            var merger = sim.Belts.SegmentAt(11, 5)!;
            Assert.Equal(3, merger.Ins.Count);
            Assert.Equal(3, sim.Belts.SegmentAt(9, 5)!.Outs.Count);

            var to = sim.Map.Destinations.Single(d => d.Address.Number == 1);
            var input = sim.Belts.SegmentAt(8, 5)!;
            uint id = 940000;
            for (int i = 0; i < 9 * 30; i++)
            {
                if (i % 9 == 0) sim.PutOnBelt(input, TestMail.Letter(session.Content, to.Address, id++, sim.Run.Shift));
                session.Run(1);
            }
            session.Run(10 * 30);
            // Everything backed up at the open end out of the merger: the same on both ends.
            Assert.True(sim.Belts.SegmentAt(12, 5)!.Items.Count > 0);
            Assert.Equal(Layout(sim.Belts), Layout(host.World!.Belts));
            Assert.Equal(0, host.World.BeltMismatches);
        }
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
