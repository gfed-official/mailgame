using System;
using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Conveyor belts (doc 04 §2.1) and the drag line that lays them (doc 04 §1.1): compiled into
    /// segments, two lanes of mail 0.5 m apart at 2 m/s, handed on from segment to segment, into
    /// a mailbox at its insert rate by the rules of a hand, and back to the Intake when the belt
    /// under them is taken down.
    /// </summary>
    public class BeltTests
    {
        private const int Tps = 30;
        /// <summary>The verge north of Larch Lane, west of the mailbox of number 1 (tile 12, 12).</summary>
        private const int Row = 12, MailboxTx = 12;

        private uint _nextRequest = 1;
        private uint _nextMail = 970000;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Belt => Db.Building("belt_mk1");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        private (SimWorld World, Player Host) InPrep(int planks = 20, int ingots = 20)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            SimDriver.Teleport(host, Centre(9), Centre(Row) + 1f);
            if (planks > 0) Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), planks)));
            if (ingots > 0) Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), ingots)));
            return (world, host);
        }

        private OpResult Line(SimWorld world, Player p, int x0, int z0, int x1, int z1, int rotation) =>
            Request(world, p, SimRequest.PlaceLine(0, Belt.Index, x0, z0, x1, z1, rotation));

        private static int Count(GridContainer c, string item)
        {
            ushort index = Db.Item(item).Index;
            return c.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        private MailItem Letter(SimWorld world, DestinationDef to) => TestContent.Mail("letter", to.Address, _nextMail++, world.Run.Shift);

        private static DestinationDef NumberOne(SimWorld world) =>
            world.Map.Destinations.Single(d => d.Address.Number == 1);

        [Fact]
        public void TheConveyorBelt_IsContent_FlatFourWaysTwoMetresASecond_APlankAndAnIngotATile()
        {
            Assert.Equal((BuildingBehaviour.Belt, 4, 80, 2f), (Belt.Behaviour, Belt.Rotations, Belt.Hp, Belt.Speed));
            Assert.False(Belt.Solid);
            Assert.True(Belt.Drags && Db.Building("wall_wood").Drags && !Db.Building("chest").Drags);
            Assert.Equal(new[] { ("plank", 1), ("iron_ingot", 1) }, Belt.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal((2, 2.0f), (Db.MailKind("cargo").BeltLanes, Db.MailKind("cargo").BeltLength));
        }

        [Fact]
        public void ADragLine_IsOneRequest_EveryTileJudged_AndStopsWhenTheMaterialsRunOut()
        {
            var (world, host) = InPrep(planks: 4);
            Assert.Equal(OpResult.BadRequest, Line(world, host, 6, Row, 8, Row + 1, 0));
            Assert.Equal(OpResult.Ok, Line(world, host, 6, Row, 11, Row, 0));
            // Four planks: four belts, from the start of the line on.
            Assert.Equal(4, world.Events.OfType<ConstructPlacedEvent>().Count());
            Assert.All(Enumerable.Range(6, 4), x => Assert.Equal(Belt, world.Constructs.At(x, Row)!.Def));
            Assert.Null(world.Constructs.At(10, Row));
            Assert.Equal((0, 16), (Count(host.Inventory, "plank"), Count(host.Inventory, "iron_ingot")));
            Assert.Equal(OpResult.MissingMaterials, Line(world, host, 10, Row, 11, Row, 0));

            // One segment, head at the west end, facing east.
            world.Step();
            var s = Assert.Single(world.Belts.Segments);
            Assert.Equal((6, Row, 4, 8f), (s.Head % world.Map.Cols, s.Head / world.Map.Cols, s.Tiles.Count, s.Length));
            Assert.Equal(BeltEndKind.None, s.End);
            // Laid where the carrier stands: it lies flat.
            SimDriver.Teleport(host, Centre(10), Centre(Row));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 2)));
            Assert.Equal(OpResult.Ok, Line(world, host, 10, Row, 11, Row, 0));
            world.Step();
            s = Assert.Single(world.Belts.Segments);
            Assert.Equal(6, s.Tiles.Count);
            Assert.Equal(BeltEndKind.Mailbox, s.End);
            Assert.Equal(NumberOne(world).Id, s.DestId);
        }

        [Fact]
        public void MailRides_TwoMetresASecond_HalfAMetreApart_InTwoLanes_AndWaitsAtAnOpenEnd()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Line(world, host, 6, Row, 9, Row, 0));
            world.Step();
            var s = world.Belts.Segments.Single();
            var to = NumberOne(world);
            Assert.True(world.Belts.TryInsert(s, Letter(world, to), out var first));
            Assert.Equal(BeltNetwork.LaneLeft, first.Lane);
            SimDriver.StepTicks(world, Tps);
            Assert.InRange(s.Items[0].Pos, 1.9f, 2.1f);
            // Room at the head again only once the one before is half a metre on.
            Assert.True(world.Belts.TryInsert(s, Letter(world, to), out var second));
            Assert.Equal(BeltNetwork.LaneRight, second.Lane);
            for (int i = 0; i < 10 * Tps; i++)
            {
                world.Belts.TryInsert(s, Letter(world, to), out _);
                world.Step();
            }
            // 8 m of belt: four tiles of four in each lane, all of them backed up at the end.
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(32, s.Items.Count);
            foreach (byte lane in new[] { BeltNetwork.LaneLeft, BeltNetwork.LaneRight })
            {
                var pos = s.Items.Where(it => it.Lane == lane).Select(it => it.Pos).ToArray();
                Assert.Equal(16, pos.Length);
                Assert.Equal(8f, pos[0], 3);
                for (int i = 1; i < pos.Length; i++) Assert.Equal(0.5f, pos[i - 1] - pos[i], 3);
            }
            Assert.False(world.Belts.TryInsert(s, Letter(world, to), out _));
        }

        [Fact]
        public void AtAMailbox_TheRightMailGoesIn_AtItsInsertRate_InDeliveryOnly_AndTheWrongMailWaits()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Line(world, host, 8, Row, 11, Row, 0));
            world.Step();
            var s = world.Belts.Segments.Single();
            var to = NumberOne(world);
            var other = world.Map.Destinations.First(d => d.Address.Number == 2);
            Assert.True(world.Belts.TryInsert(s, Letter(world, to), out _));
            SimDriver.StepTicks(world, 5 * Tps);
            // In Prep the mailboxes are shut: it waits at the end.
            Assert.Equal(8f, s.Items.Single().Pos, 3);

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            int wallet = world.Economy.Wallet;
            world.Events.Clear();
            world.Step();
            var paid = world.Events.OfType<DeliveredEvent>().Single();
            Assert.Equal((to.Id, EntityId.None), (paid.DestId, paid.Player));
            Assert.Equal(wallet + paid.Paid, world.Economy.Wallet);
            Assert.Empty(s.Items);

            // Wrong mail in the left lane stays at the end; the right lane goes on, at 2 a second.
            world.Belts.TryInsert(s, Letter(world, other), out var wrong);
            world.Belts.TryInsert(s, Letter(world, to), out var right1);
            Assert.NotEqual(wrong.Lane, right1.Lane);
            var delivered = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < 12 * Tps; i++)
            {
                if (i % 10 == 0) world.Belts.TryInsert(s, Letter(world, to), out _);
                world.Events.Clear();
                world.Step();
                foreach (var d in world.Events.OfType<DeliveredEvent>()) delivered.Add(world.Tick);
            }
            Assert.True(delivered.Count >= 8, $"{delivered.Count} delivered: " + string.Join(" ", s.Items.Select(it => $"{it.Lane}@{it.Pos:0.00}{(it.Mail.Address == to.Address ? "" : "!")}")));
            for (int i = 1; i < delivered.Count; i++) Assert.True(delivered[i] - delivered[i - 1] >= 15, "faster than the insert rate");
            Assert.Contains(s.Items, it => it.Mail.Id == wrong.Mail.Id && it.Pos >= 7.99f);
            Assert.Equal(0, world.Economy.ShiftMisdeliveries);
        }

        [Fact]
        public void TwoLinesIntoOneTile_MakeThreeSegments_AndTheMailIsHandedOn()
        {
            var (world, host) = InPrep();
            // West line east into (8, 12); a south-facing line from (8, 10) down to it: the tile has two feeders.
            Assert.Equal(OpResult.Ok, Line(world, host, 5, Row, 7, Row, 0));
            Assert.Equal(OpResult.Ok, Line(world, host, 8, Row - 2, 8, Row - 1, 1));
            Assert.Equal(OpResult.Ok, Line(world, host, 8, Row, 11, Row, 0));
            world.Step();
            Assert.Equal(3, world.Belts.Segments.Count);
            var merged = world.Belts.SegmentAt(8, Row)!;
            Assert.Equal(8 + Row * world.Map.Cols, merged.Head);
            Assert.Equal(BeltEndKind.Mailbox, merged.End);
            var west = world.Belts.SegmentAt(5, Row)!;
            var north = world.Belts.SegmentAt(8, Row - 2)!;
            Assert.Same(merged, west.Next);
            Assert.Same(merged, north.Next);

            var to = NumberOne(world);
            Assert.True(world.Belts.TryInsert(west, Letter(world, to), out var a));
            Assert.True(world.Belts.TryInsert(north, Letter(world, to), out var b));
            var moved = new System.Collections.Generic.List<BeltItemMovedEvent>();
            for (int i = 0; i < 8 * Tps; i++)
            {
                world.Events.Clear();
                world.Step();
                moved.AddRange(world.Events.OfType<BeltItemMovedEvent>());
            }
            Assert.Equal(new[] { a.Mail.Id, b.Mail.Id }.OrderBy(x => x), moved.Select(m => m.MailId).OrderBy(x => x));
            Assert.All(moved, m => Assert.Equal((8, Row), (m.X, m.Y)));
            // Both came in by the left lane (the north line from the left side): one at the end, one behind it.
            Assert.Equal(2, merged.Items.Count);
            Assert.All(merged.Items, it => Assert.Equal(BeltNetwork.LaneLeft, it.Lane));
            Assert.Equal(new[] { 8f, 7.5f }, merged.Items.Select(it => MathF.Round(it.Pos, 3)));
            // Straight on, mail keeps its lane; from the left side, whatever its lane, it comes into the left one.
            Assert.Equal(BeltNetwork.LaneRight, world.Belts.EntryLane(west, merged, BeltNetwork.LaneRight));
            Assert.Equal(BeltNetwork.LaneLeft, world.Belts.EntryLane(north, merged, BeltNetwork.LaneRight));
        }

        [Fact]
        public void TakingABeltDown_PutsTheMailOnIt_BackIntoTheIntake_AndTheRestKeepsItsPlace()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Line(world, host, 6, Row, 9, Row, 0));
            world.Step();
            var s = world.Belts.Segments.Single();
            var to = NumberOne(world);
            Assert.True(world.Belts.TryInsert(s, Letter(world, to), out var far));
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.True(world.Belts.TryInsert(s, Letter(world, to), out var near));
            SimDriver.StepTicks(world, 10);
            int inIntake = world.Intake.Entries.Sum(e => e.Stack.Kind == StackKind.Mail ? e.Stack.Mail.Count : 0);

            // The first belt goes, with the letter on it; the one 4 m on keeps going on what is left.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, 6, Row)));
            Assert.Contains(world.Events.OfType<BeltItemRemovedEvent>(), e => e.MailId == near.Mail.Id);
            Assert.Equal(inIntake + 1, world.Intake.Entries.Sum(e => e.Stack.Kind == StackKind.Mail ? e.Stack.Mail.Count : 0));
            var rest = world.Belts.Segments.Single();
            Assert.Equal(3, rest.Tiles.Count);
            var kept = rest.Items.Single();
            Assert.Equal(far.Mail.Id, kept.Mail.Id);
            Assert.InRange(kept.Pos, 2.0f, 2.8f);
            Assert.Contains(world.Events.OfType<BeltStateEvent>(), e => e.X == 7 && e.Y == Row && e.Items.Single().MailId == far.Mail.Id);
        }

        [Fact]
        public void MailJustPutOn_StaysOnTheBelt_WhenABeltIsBuiltElsewhere()
        {
            // Its rear still hangs behind the head for a quarter of a second: a recompile used to
            // count that as no room and put it back into the Intake.
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Line(world, host, 6, Row, 9, Row, 0));
            world.Step();
            var s = world.Belts.Segments.Single();
            Assert.True(world.Belts.TryInsert(s, Letter(world, NumberOne(world)), out var letter));
            Assert.True(world.Belts.TryInsert(s, Letter(world, NumberOne(world)), out var second));
            world.Step();
            Assert.All(s.Items, it => Assert.True(it.Pos < it.Length));
            Assert.Equal(OpResult.Ok, Line(world, host, 8, Row - 1, 9, Row - 1, 2));
            Assert.DoesNotContain(world.Events.OfType<BeltItemRemovedEvent>(), e => e.MailId == letter.Mail.Id || e.MailId == second.Mail.Id);
            var kept = world.Belts.SegmentAt(6, Row)!;
            Assert.Equal(new[] { letter.Mail.Id, second.Mail.Id }.OrderBy(x => x), kept.Items.Select(it => it.Mail.Id).OrderBy(x => x));
        }

        [Fact]
        public void ALoopOfBelts_IsOneSegment_ThatFeedsItself()
        {
            var (world, host) = InPrep();
            // A 2 × 2 ring: east, south, west, north.
            Assert.Equal(OpResult.Ok, Line(world, host, 8, Row - 1, 8, Row - 1, 0));
            Assert.Equal(OpResult.Ok, Line(world, host, 9, Row - 1, 9, Row - 1, 1));
            Assert.Equal(OpResult.Ok, Line(world, host, 9, Row, 9, Row, 2));
            Assert.Equal(OpResult.Ok, Line(world, host, 8, Row, 8, Row, 3));
            world.Step();
            var s = world.Belts.Segments.Single();
            Assert.Same(s, s.Next);
            Assert.Equal(8 + (Row - 1) * world.Map.Cols, s.Head);
            Assert.True(world.Belts.TryInsert(s, Letter(world, NumberOne(world)), out var item));
            SimDriver.StepTicks(world, 10 * Tps);
            // Round and round: never lost, never stuck.
            var now = s.Items.Single();
            Assert.Equal(item.Mail.Id, now.Mail.Id);
            float before = now.Pos;
            SimDriver.StepTicks(world, 15);
            Assert.NotEqual(before, s.Items.Single().Pos);
        }
    
        [Fact]
        public void TheIntakesOutfeed_FeedsABeltFacingAway_OldestMailFirst_AndOnlyThatOne()
        {
            var (world, host) = InPrep();
            var belts = world.Belts;
            // The tile north of the Intake's, the belt facing north, away from the counter.
            Assert.Equal((6, 8), (belts.OutfeedX, belts.OutfeedZ));
            Assert.Equal(OpResult.Ok, BuildRules.CheckTile(world.Map, world.Constructs, 6, 8));
            SimDriver.Teleport(host, Centre(8), Centre(7) + 1f);
            var to = NumberOne(world);
            var other = world.Map.Destinations.First(d => d.Address.Number == 2);
            // Turned the wrong way, nothing comes.
            Assert.Equal(OpResult.Ok, Line(world, host, 6, 8, 6, 8, 0));
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("letter", to.Address, 905))));
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("small", other.Address, 903))));
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("letter", to.Address, 909))));
            SimDriver.StepTicks(world, 30);
            Assert.Null(world.Belts.Outfeed);
            Assert.Equal(0, world.Belts.ItemCount);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, 6, 8)));

            // North, then east along the row: the Intake's mail comes out oldest first.
            Assert.Equal(OpResult.Ok, Line(world, host, 6, 7, 10, 7, 0));
            world.Submit(host.Id, SimRequest.PlaceLine(_nextRequest++, Belt.Index, 6, 8, 6, 8, BeltNetwork.OutfeedRotation));
            var order = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < 3 * Tps; i++)
            {
                world.Events.Clear();
                world.Step();
                order.AddRange(world.Events.OfType<BeltItemAddedEvent>().Select(e => e.Item.MailId));
            }
            Assert.Equal(new uint[] { 903, 905, 909 }, order);
            Assert.Equal(0, world.Intake.Entries.Sum(e => e.Stack.Count));
            var outfeed = world.Belts.Outfeed!;
            Assert.Equal(6 + 8 * world.Map.Cols, outfeed.Head);
            Assert.Equal(6, outfeed.Tiles.Count);
            Assert.Equal(3, outfeed.Items.Count);
            Assert.True(outfeed.Items.Min(it => it.Pos) > 1f);
        }

        [Fact]
        public void OnTheIsland_TheOutfeedTile_CanTakeABelt()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false });
                var belts = world.Belts;
                Assert.Equal(OpResult.Ok, BuildRules.CheckTile(world.Map, world.Constructs, belts.OutfeedX, belts.OutfeedZ));
                // And somewhere to go from it: the tile north of it, or one beside that.
                int x = belts.OutfeedX, z = belts.OutfeedZ - 1;
                Assert.True(new[] { (x, z), (x + 1, z + 1), (x - 1, z + 1) }.Any(t => BuildRules.CheckTile(world.Map, world.Constructs, t.Item1, t.Item2) == OpResult.Ok),
                    $"seed {seed}: the outfeed at ({x}, {z + 1}) leads nowhere");
            }
        }
    
        private OpResult Put(SimWorld world, Player p, string building, int tx, int tz, int rotation) =>
            Request(world, p, SimRequest.PlaceConstruct(0, Db.Building(building).Index, tx, tz, rotation));

        [Fact]
        public void ASplitter_HandsMailFrontLeftRight_InTurn_SkippingAWayThatIsFull()
        {
            var (world, host) = InPrep();
            SimDriver.Teleport(host, Centre(10), Centre(5));
            Assert.Equal(OpResult.Ok, Line(world, host, 8, 5, 9, 5, 0));
            Assert.Equal(OpResult.Ok, Put(world, host, "splitter", 10, 5, 0));
            Assert.Equal(OpResult.Ok, Put(world, host, "belt_mk1", 11, 5, 0));
            Assert.Equal(OpResult.Ok, Put(world, host, "belt_mk1", 10, 4, 3));
            Assert.Equal(OpResult.Ok, Put(world, host, "belt_mk1", 10, 6, 1));
            world.Step();
            var belts = world.Belts;
            var splitter = belts.SegmentAt(10, 5)!;
            Assert.Single(splitter.Tiles);
            Assert.Equal(BeltEndKind.Split, splitter.End);
            var front = belts.SegmentAt(11, 5)!;
            var left = belts.SegmentAt(10, 4)!;
            var right = belts.SegmentAt(10, 6)!;
            Assert.Equal(new[] { front, left, right }, splitter.Outs);
            Assert.Same(splitter, belts.SegmentAt(9, 5)!.Next);

            var input = belts.SegmentAt(8, 5)!;
            var to = NumberOne(world);
            var into = new System.Collections.Generic.List<BeltSegment>();
            for (int i = 0; i < 8 * Tps; i++)
            {
                if (i % 10 == 0 && i < 60) world.PutOnBelt(input, Letter(world, to));
                world.Events.Clear();
                world.Step();
                foreach (var m in world.Events.OfType<BeltItemMovedEvent>())
                    into.Add(belts.SegmentByHead(m.Y * world.Map.Cols + m.X)!);
            }
            var outs = into.Where(sg => sg != splitter).ToList();
            Assert.Equal(new[] { front, left, right, front, left, right }, outs);
            Assert.Equal((2, 2, 2), (front.Items.Count, left.Items.Count, right.Items.Count));

            // The left way full (a 2 m belt holds four a lane): the next four go front and right only.
            for (int i = 0; i < 4 * Tps; i++)
            {
                if (i % 8 == 0) world.PutOnBelt(left, Letter(world, to));
                world.Step();
            }
            Assert.Equal(8, left.Items.Count);
            into.Clear();
            for (int i = 0; i < 4 * Tps; i++)
            {
                if (i % 10 == 0 && i < 40) world.PutOnBelt(input, Letter(world, to));
                world.Events.Clear();
                world.Step();
                foreach (var m in world.Events.OfType<BeltItemMovedEvent>())
                    into.Add(belts.SegmentByHead(m.Y * world.Map.Cols + m.X)!);
            }
            Assert.Equal(new[] { front, right, front, right }, into.Where(sg => sg != splitter));
            Assert.Equal((4, 8, 4), (front.Items.Count, left.Items.Count, right.Items.Count));
        }

        [Fact]
        public void AMerger_TakesItsInputsInTurn_OntoItsOneOutput()
        {
            var (world, host) = InPrep();
            SimDriver.Teleport(host, Centre(10), Centre(5));
            Assert.Equal(OpResult.Ok, Put(world, host, "belt_mk1", 9, 5, 0));
            Assert.Equal(OpResult.Ok, Put(world, host, "belt_mk1", 10, 4, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, "belt_mk1", 10, 6, 3));
            Assert.Equal(OpResult.Ok, Put(world, host, "merger", 10, 5, 0));
            Assert.Equal(OpResult.Ok, Line(world, host, 11, 5, 12, 5, 0));
            world.Step();
            var belts = world.Belts;
            var merger = belts.SegmentAt(10, 5)!;
            var ins = new[] { belts.SegmentAt(9, 5)!, belts.SegmentAt(10, 4)!, belts.SegmentAt(10, 6)! };
            Assert.Equal(3, merger.Ins.Count);
            Assert.All(ins, i => Assert.Same(merger, i.Next));
            var output = belts.SegmentAt(11, 5)!;
            Assert.Same(output, merger.Next);

            // Three letters on each input, all at once: they come out one input after another.
            var to = NumberOne(world);
            var from = new System.Collections.Generic.Dictionary<uint, BeltSegment>();
            for (int k = 0; k < 3; k++)
            {
                foreach (var i in ins)
                {
                    var letter = Letter(world, to);
                    if (world.PutOnBelt(i, letter)) from[letter.Id] = i;
                }
                SimDriver.StepTicks(world, 8);
            }
            var order = new System.Collections.Generic.List<BeltSegment>();
            for (int t = 0; t < 10 * Tps; t++)
            {
                world.Events.Clear();
                world.Step();
                foreach (var m in world.Events.OfType<BeltItemMovedEvent>())
                    if (m.X == 10 && m.Y == 5) order.Add(from[m.MailId]);
            }
            Assert.Equal(9, from.Count);
            Assert.Equal(9, order.Count);
            // In turn: no input twice in a row while another had mail waiting.
            for (int k = 1; k < 6; k++) Assert.NotSame(order[k - 1], order[k]);
            Assert.All(ins, i => Assert.Equal(3, order.Count(o => o == i)));
        }

        [Fact]
        public void ExpressLane_MovesTheMailHalfAgainAsFast()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Line(world, host, 6, Row, 9, Row, 0));
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, Db.Perk("express_lane")));
            world.Step();
            var s = world.Belts.Segments.Single();
            Assert.True(world.Belts.TryInsert(s, Letter(world, NumberOne(world)), out _));
            SimDriver.StepTicks(world, Tps);
            Assert.InRange(s.Items[0].Pos, 2.9f, 3.1f);
        }

        [Fact]
        public void Presort_PutsTheIntakesMailOut_AStreetAtATime()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, Db.Perk("presort")));
            SimDriver.Teleport(host, Centre(8), Centre(7) + 1f);
            var larch = NumberOne(world).Address;
            var elsewhere = new AddressId(larch.District, larch.Street + 1, 3);
            // Oldest first would be 901, 902, 903, 904; a street at a time it is 901, 903, then the other street's.
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("letter", larch, 901))));
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("letter", elsewhere, 902))));
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("letter", larch, 903))));
            Assert.True(world.Give(world.Intake, ItemStack.OfMail(TestContent.Mail("letter", elsewhere, 904))));
            Assert.Equal(OpResult.Ok, Line(world, host, 6, 7, 10, 7, 0));
            world.Submit(host.Id, SimRequest.PlaceLine(_nextRequest++, Belt.Index, 6, 8, 6, 8, BeltNetwork.OutfeedRotation));
            var order = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < 4 * Tps; i++)
            {
                world.Events.Clear();
                world.Step();
                order.AddRange(world.Events.OfType<BeltItemAddedEvent>().Select(e => e.Item.MailId));
            }
            Assert.Equal(new uint[] { 901, 903, 902, 904 }, order);
        }
    }
}
