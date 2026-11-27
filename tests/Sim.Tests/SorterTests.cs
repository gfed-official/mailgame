using System;
using System.Collections.Generic;
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
    /// The Address Sorter (doc 04 §2.4, doc 10 §2.2, doc 12 M2) and the splitter's kind filter
    /// (doc 04 §2.3): 2 × 2 tiles, one input from behind, front, left and right outputs by their
    /// filters and an overflow beside the input; a buffer of eight, one piece every half second.
    /// </summary>
    public class SorterTests
    {
        private const int Tps = 30;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Sorter => Db.Building("address_sorter_mk1");
        private static BuildingDef Belt => Db.Building("belt_mk1");

        private uint _nextRequest = 1;

        private sealed class Sink : IBeltSink
        {
            public readonly List<uint> Taken = new List<uint>();
            public readonly List<(SorterNode Node, int Port, uint Mail)> Out = new List<(SorterNode, int, uint)>();
            public bool TryStore(BeltSegment segment, in BeltItem item) => false;
            public bool TryDeliver(BeltSegment segment, in BeltItem item) => false;
            public void Moved(BeltSegment from, BeltSegment to, in BeltItem item) { }
            public void Took(BeltSegment from, in BeltItem item) => Taken.Add(item.Mail.Id);
            public void Sorted(SorterNode sorter, int port, BeltSegment to, in BeltItem item) => Out.Add((sorter, port, item.Mail.Id));
        }

        /// <summary>
        /// A sorter facing <paramref name="facing"/> with its built tile at (sx, sz), a three-belt
        /// input line behind it and a three-belt line out of each of its ports.
        /// </summary>
        private static (Construct Built, int[] Heads) Layout(ConstructField field, WorldMap map, int sx, int sz, int facing)
        {
            var built = field.Add(Sorter, sx, sz, facing, Sorter.Hp, 0);
            for (int k = 1; k <= 3; k++)
            {
                var (x, z) = Construct.TileOf(sx, sz, facing, -k, 0);
                field.Add(Belt, x, z, facing, Belt.Hp, 0);
            }
            var heads = new int[SorterNode.Ports];
            for (int port = 0; port < SorterNode.Ports; port++)
            {
                var (along, across, dir) = SorterNode.Port(port, facing);
                var (px, pz) = Construct.TileOf(sx, sz, facing, along, across);
                for (int k = 1; k <= 3; k++)
                {
                    int x = px + BeltNetwork.Dirs[dir].Dx * k, z = pz + BeltNetwork.Dirs[dir].Dz * k;
                    field.Add(Belt, x, z, dir, Belt.Hp, 0);
                    if (k == 1) heads[port] = z * map.Cols + x;
                }
            }
            return (built, heads);
        }

        [Fact]
        public void TheSorter_IsContent_TwoByTwo_BehindTheSortingBlueprint()
        {
            Assert.Equal((BuildingBehaviour.Sorter, 2, 2, 4, 500), (Sorter.Behaviour, Sorter.Width, Sorter.Length, Sorter.Rotations, Sorter.Hp));
            Assert.Equal((2f, 8, 3, 1, 3), (Sorter.Throughput, Sorter.BufferSize, Sorter.FilterSlots, Sorter.FilterSlotsOpen, Sorter.FilterOutputs));
            Assert.True(Sorter.Solid && !Sorter.Carries);
            Assert.Equal(new[] { ("iron_ingot", 4), ("stone", 6) }, Sorter.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            var bp = Db.ShopItem("bp_sorting");
            Assert.Same(bp, Sorter.Recipe.Blueprint);
            Assert.Equal((ShopKind.Blueprint, 400, 1, true), (bp.Kind, bp.Price, bp.FromShift, bp.OncePerRun));
            Assert.Equal(3, Db.Building("splitter").FilterOutputs);
            Assert.Equal(0, Belt.FilterOutputs);
        }

        [Fact]
        public void AConstructOfTwoByTwo_StandsOnFourTiles_TurnedWithIt()
        {
            var map = new SimWorld(Db, new SimSettings { Seed = 7 }).Map;
            var field = new ConstructField(map);
            // Facing south: on along +z, across to the right of south, which is west.
            var c = field.Add(Sorter, 10, 20, 1, Sorter.Hp, 0);
            var tiles = Enumerable.Range(0, Sorter.TileCount).Select(c.TileAt).OrderBy(t => t).ToArray();
            Assert.Equal(new[] { (9, 20), (9, 21), (10, 20), (10, 21) }, tiles);
            Assert.All(tiles, t => Assert.Same(c, field.At(t.Item1, t.Item2)));
            Assert.Equal((10f * 2f, 21f * 2f), (c.CentreX, c.CentreZ));
            Assert.True(field.Remove(9, 21));
            Assert.All(tiles, t => Assert.Null(field.At(t.Item1, t.Item2)));
        }

        /// <summary>
        /// Doc 12 M2 acceptance: "A sorter with per-street filters routes 1000 generated items with
        /// zero misroutes; overflow receives exactly the unmatched items." Turned all four ways.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void AThousandPieces_GoOutByTheFirstFilterTheyMatch_AndTheRestToTheOverflow(int facing)
        {
            var map = new SimWorld(Db, new SimSettings { Seed = 7 }).Map;
            var field = new ConstructField(map);
            var belts = new BeltNetwork(Db, map, field);
            var (built, heads) = Layout(field, map, 20, 10, facing);
            const int streetA = 4, streetB = 9, district = 2;
            var parcel = Db.MailKind("small");
            field.SetFilters(built, new[]
            {
                new MailFilter(FilterKind.Street, streetA), new MailFilter(FilterKind.Street, streetB), new MailFilter(FilterKind.Kind, parcel.Index),
            });
            belts.Compile();
            var input = belts.SegmentAt(Construct.TileOf(20, 10, facing, -3, 0).Tx, Construct.TileOf(20, 10, facing, -3, 0).Tz)!;
            Assert.Equal(BeltEndKind.Sorter, input.End);
            var node = Assert.Single(belts.Sorters);
            Assert.Same(node, input.Sorter);
            for (int port = 0; port < SorterNode.Ports; port++) Assert.Equal(heads[port], node.Outs[port]!.Head);

            var rng = new Random(1000 + facing);
            var kinds = new[] { Db.MailKind("letter"), parcel };
            var expected = new Dictionary<uint, int>();
            var got = new Dictionary<uint, int>();
            var sink = new Sink();
            uint next = 1;
            int unmatched = 0;
            for (uint tick = 1; got.Count < 1000 && tick < 40000; tick++)
            {
                if (next <= 1000)
                {
                    int street = new[] { streetA, streetB, 5, 6 }[rng.Next(4)];
                    var kind = kinds[rng.Next(kinds.Length)];
                    var mail = TestContent.Mail(kind.Id, new AddressId(rng.Next(3) == 0 ? district : 1, street, 1 + rng.Next(40)), next);
                    if (belts.TryInsert(input, mail, out _))
                    {
                        int port = street == streetA ? SorterNode.Front : street == streetB ? SorterNode.Left : kind == parcel ? SorterNode.Right : SorterNode.Overflow;
                        if (port == SorterNode.Overflow) unmatched++;
                        expected[next++] = port;
                    }
                }
                belts.Step(SimConstants.TickDt, sink, tick);
                // What reaches the end of an output line is taken away.
                for (int port = 0; port < SorterNode.Ports; port++)
                {
                    var line = belts.SegmentByHead(heads[port])!;
                    foreach (var it in line.Items.Where(i => i.Pos >= line.Length - 1e-3f).ToList())
                    {
                        Assert.True(belts.RemoveMail(it.Mail.Id));
                        got[it.Mail.Id] = port;
                    }
                }
                Assert.InRange(node.Buffer.Count, 0, Sorter.BufferSize);
            }
            Assert.Equal(1000, got.Count);
            Assert.All(got, kv => Assert.Equal(expected[kv.Key], kv.Value));
            Assert.Equal(unmatched, got.Count(kv => kv.Value == SorterNode.Overflow));
            Assert.Equal((1000 - unmatched, unmatched), (node.Sorted, node.Overflowed));
            Assert.Equal(1000, sink.Taken.Count);
            // Every port had its share.
            Assert.All(Enumerable.Range(0, SorterNode.Ports), p => Assert.Contains(got.Values, v => v == p));
        }

        [Fact]
        public void TheSorterLooksAtTwoPiecesASecond_AndItsBufferHoldsEight_ThenTheLineBacksUp()
        {
            var map = new SimWorld(Db, new SimSettings { Seed = 7 }).Map;
            var field = new ConstructField(map);
            var belts = new BeltNetwork(Db, map, field);
            var (built, heads) = Layout(field, map, 20, 10, 0);
            // Every output by its filter takes nothing; only the overflow has a belt that runs on.
            field.Remove(heads[SorterNode.Overflow] % map.Cols, heads[SorterNode.Overflow] / map.Cols);
            belts.Compile();
            var node = belts.Sorters[0];
            Assert.Null(node.Outs[SorterNode.Overflow]);
            var input = belts.SegmentAt(17, 10)!;
            var sink = new Sink();
            uint id = 1;
            for (uint tick = 1; tick <= 20 * Tps; tick++)
            {
                belts.TryInsert(input, TestContent.Mail("letter", new AddressId(1, 1, 1), id), out _);
                id++;
                belts.Step(SimConstants.TickDt, sink, tick);
            }
            // Nowhere to go: the buffer is full and the input line stands full behind it.
            Assert.Equal(Sorter.BufferSize, node.Buffer.Count);
            Assert.Empty(sink.Out);
            Assert.Equal(Sorter.BufferSize, sink.Taken.Count);
            Assert.False(BeltNetwork.PickLane(input, Db.MailKind("letter"), out _));

            // With an overflow belt again it goes out at two a second.
            field.Add(Belt, heads[SorterNode.Overflow] % map.Cols, heads[SorterNode.Overflow] / map.Cols, 2, Belt.Hp, 0);
            var orphans = new List<BeltItem>();
            belts.Compile(null, orphans);
            Assert.Empty(orphans);
            Assert.Same(node, belts.Sorters[0]);
            Assert.Equal(Sorter.BufferSize, node.Buffer.Count);
            sink.Out.Clear();
            for (uint tick = 20 * Tps + 1; tick <= 22 * Tps; tick++) belts.Step(SimConstants.TickDt, sink, tick);
            Assert.InRange(sink.Out.Count, 4, 5);
            Assert.All(sink.Out, o => Assert.Equal(SorterNode.Overflow, o.Port));

            // Taken down, its buffer is off the network: the server puts it back into the Intake.
            int buffered = node.Buffer.Count;
            field.Remove(built.Tx, built.Tz);
            orphans.Clear();
            belts.Compile(null, orphans);
            Assert.Empty(belts.Sorters);
            Assert.True(orphans.Count >= buffered);
            Assert.Equal(BeltEndKind.None, belts.SegmentAt(17, 10)!.End);
        }

        [Fact]
        public void ASplittersKindFilter_SendsItsKindOneWay_AndTheRestRoundTheOthers()
        {
            var map = new SimWorld(Db, new SimSettings { Seed = 7 }).Map;
            var field = new ConstructField(map);
            var belts = new BeltNetwork(Db, map, field);
            // East into a splitter at (20, 10): its front east, left north, right south.
            field.Add(Belt, 19, 10, 0, Belt.Hp, 0);
            var splitter = field.Add(Db.Building("splitter"), 20, 10, 0, 150, 0);
            field.Add(Belt, 21, 10, 0, Belt.Hp, 0);
            field.Add(Belt, 20, 9, 3, Belt.Hp, 0);
            field.Add(Belt, 20, 11, 1, Belt.Hp, 0);
            var letter = Db.MailKind("letter");
            // The left output takes letters only.
            field.SetFilters(splitter, new[] { MailFilter.Empty, new MailFilter(FilterKind.Kind, letter.Index), MailFilter.Empty });
            belts.Compile();
            var input = belts.SegmentAt(19, 10)!;
            var outs = new Dictionary<int, string> { [belts.SegmentAt(21, 10)!.Head] = "front", [belts.SegmentAt(20, 9)!.Head] = "left", [belts.SegmentAt(20, 11)!.Head] = "right" };
            var sink = new MovesSink();
            uint id = 1;
            for (uint tick = 1; tick <= 60 * Tps; tick++)
            {
                if (tick % 20 == 0)
                    belts.TryInsert(input, TestContent.Mail(id % 2 == 0 ? "letter" : "small", new AddressId(1, 1, 1), id++), out _);
                belts.Step(SimConstants.TickDt, sink, tick);
                foreach (var head in outs.Keys)
                {
                    var line = belts.SegmentByHead(head)!;
                    foreach (var it in line.Items.Where(i => i.Pos >= line.Length - 1e-3f).ToList()) belts.RemoveMail(it.Mail.Id);
                }
            }
            var byWay = sink.Moves.Where(m => outs.ContainsKey(m.Head)).GroupBy(m => outs[m.Head]).ToDictionary(g => g.Key, g => g.Select(m => m.Kind).ToList());
            Assert.True(byWay["left"].Count > 20);
            Assert.All(byWay["left"], k => Assert.Equal(letter.Index, k));
            Assert.All(byWay["front"].Concat(byWay["right"]), k => Assert.NotEqual(letter.Index, k));
            // Round-robin between the two without a filter.
            Assert.InRange(byWay["front"].Count - byWay["right"].Count, -1, 1);
        }

        private sealed class MovesSink : IBeltSink
        {
            public readonly List<(int Head, ushort Kind)> Moves = new List<(int, ushort)>();
            public bool TryStore(BeltSegment segment, in BeltItem item) => false;
            public bool TryDeliver(BeltSegment segment, in BeltItem item) => false;
            public void Moved(BeltSegment from, BeltSegment to, in BeltItem item) => Moves.Add((to.Head, item.Mail.Kind));
            public void Took(BeltSegment from, in BeltItem item) { }
            public void Sorted(SorterNode sorter, int port, BeltSegment to, in BeltItem item) { }
        }

        // --- on the server ------------------------------------------------------------------------

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(1000);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(23));
            return (world, host);
        }

        private OpResult Buy(SimWorld world, Player p)
        {
            var po = world.Map.PostOffice;
            float x = p.Move.X, z = p.Move.Z;
            SimDriver.Teleport(p, po.ShopX + 1.0f, po.ShopZ);
            SimDriver.StepTicks(world, 2);
            var result = Request(world, p, SimRequest.Buy(0, Db.ShopItem("bp_sorting").Index));
            SimDriver.Teleport(p, x, z);
            SimDriver.StepTicks(world, 15);
            return result;
        }

        [Fact]
        public void OnTheServer_TheSorterIsBuiltOnFourOpenTiles_Configured_AndSaysWhatItDoes()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.NeedsBlueprint, Request(world, host, SimRequest.PlaceConstruct(0, Sorter.Index, 16, 21, 0)));
            Assert.Equal(OpResult.Ok, Buy(world, host));
            // (20, 21) is open, (20, 22)… is not: a sorter needs all four.
            Assert.Equal(OpResult.Blocked, Request(world, host, SimRequest.PlaceConstruct(0, Sorter.Index, 19, 21, 0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Sorter.Index, 16, 21, 0)));
            var built = world.Constructs.At(17, 22)!;
            Assert.Equal((Sorter, 16, 21), (built.Def, built.Tx, built.Tz));
            Assert.Equal(OpResult.Blocked, Request(world, host, SimRequest.PlaceConstruct(0, Belt.Index, 17, 21, 0)));
            // Nobody walks through it.
            Assert.True(world.Constructs.Blocks(17, 22));

            var street = world.Map.Streets[0];
            var filter = new MailFilter(FilterKind.Street, street.Id);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 17, 22, SimRequest.ConfigureSet, 1, 0, filter.Encode())));
            var configured = world.Events.OfType<ConstructConfiguredEvent>().Single();
            Assert.Equal(((byte)16, (byte)21, host.Id), (configured.X, configured.Y, configured.Player));
            // Three outputs of three slots each (doc 10 §4.2 Extra Labels opens the second and third).
            Assert.Equal(new ushort[] { 0, filter.Encode(), 0, 0, 0, 0, 0, 0, 0 }, configured.Filters);
            Assert.Equal(filter, built.Filter(1, 0));
            // Out of range of the outputs, the slots, the map, or a filter a sorter does not have.
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 3, 0, filter.Encode())));
            Assert.Equal(OpResult.NotAllowed, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 0, 1, filter.Encode())));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 0, 3, filter.Encode())));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Street, 250).Encode())));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 0, 0, 0x0900)));
            // Nor anything at a belt.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Belt.Index, 15, 21, 0)));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 15, 21, SimRequest.ConfigureOpen)));

            // Open, the status comes: what is in the Intake each output would take.
            var intakeMail = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail).ToList();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureOpen)));
            var status = world.Events.OfType<SorterStatusEvent>().Single();
            Assert.Equal(host.Id, status.Audience);
            Assert.Equal(4, status.Matches.Count);
            Assert.Equal(intakeMail.Count(m => m.Address.Street == street.Id), status.Matches[1]);
            Assert.Equal(intakeMail.Count, status.Matches.Sum(m => m));
            Assert.Equal(host.ViewingFilters, 21 * world.Map.Cols + 16 + 1);
            SimDriver.StepTicks(world, SimWorld.SorterStatusTicks);
            Assert.Contains(world.Events, e => e is SorterStatusEvent);
            // Walked off, it stops.
            SimDriver.Teleport(host, BuildRules.TileCentre(40), BuildRules.TileCentre(23));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(0, host.ViewingFilters);

            // A splitter filters by kind, and by nothing else.
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(23));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Db.Building("splitter").Index, 14, 22, 0)));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 14, 22, SimRequest.ConfigureSet, 0, 0, filter.Encode())));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 14, 22, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Kind, 0).Encode())));
        }

        [Fact]
        public void OnTheServer_MailRunsOutOfTheIntake_ThroughTheSorter_AndWhatIsInItGoesBackWhenItIsTakenDown()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Buy(world, host));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Sorter.Index, 16, 21, 0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Belt.Index, 13, 21, 15, 21, 0)));
            // Out of the overflow, two belts west.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Belt.Index, 15, 22, 14, 22, 2)));
            world.Step();
            var node = Assert.Single(world.Belts.Sorters);
            Assert.NotNull(node.Outs[SorterNode.Overflow]);
            Assert.Null(node.Outs[SorterNode.Front]);

            var dest = world.Map.Destinations[0];
            var input = world.Belts.SegmentAt(13, 21)!;
            int added = 0;
            for (uint i = 0; i < 6; i++)
            {
                if (world.PutOnBelt(input, TestContent.Mail("letter", dest.Address, 880000 + i, world.Run.Shift))) added++;
                SimDriver.StepTicks(world, 10);
            }
            Assert.True(added >= 4);
            world.Events.Clear();
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Equal(added, world.Belts.BufferedCount + world.Belts.ItemCount);
            Assert.True(node.Overflowed > 0);

            // Taken down, the sorter gives back a part of its materials and its buffer goes back into the Intake.
            int intakeBefore = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);
            int buffered = node.Buffer.Count;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, 17, 22)));
            world.Step();
            Assert.Null(world.Constructs.At(16, 21));
            int intakeAfter = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);
            Assert.Equal(intakeBefore + buffered, intakeAfter);
        }

        [Fact]
        public void ExtraLabels_OpensTheSecondAndThirdSlots_AndAPieceGoesOutByAnyLabelOfItsOutput()
        {
            var (world, host) = InPrep();
            Assert.False(world.PrerequisitesMet(Db.Perk("extra_labels")));
            Assert.Equal(OpResult.Ok, Buy(world, host));
            Assert.True(world.PrerequisitesMet(Db.Perk("extra_labels")));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Sorter.Index, 16, 21, 0)));
            var built = world.Constructs.At(16, 21)!;
            Assert.Equal(1, world.FilterSlotsOpen(Sorter));
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, Db.Perk("extra_labels")));
            Assert.Equal(3, world.FilterSlotsOpen(Sorter));
            var street = world.Map.Streets[0];
            var larch = world.Map.Destinations[0].Address;
            var elsewhere = new AddressId(larch.District, street.Id + 1, 5);
            // Out of the overflow too, so that "no filter" has somewhere to go.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Belt.Index, 15, 22, 14, 22, 2)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Belt.Index, 18, 21, 19, 21, 0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Street, street.Id).Encode())));
            world.Step();
            var node = Assert.Single(world.Belts.Sorters);
            int first = node.PortFor(TestContent.Mail("letter", larch, 2, 1));
            Assert.NotEqual(SorterNode.Overflow, first);
            Assert.Equal(SorterNode.Overflow, node.PortFor(TestContent.Mail("letter", elsewhere, 1, 1)));
            // The third label of the same output catches the district: the other street's mail goes out by it too.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 16, 21, SimRequest.ConfigureSet, 0, 2, new MailFilter(FilterKind.District, larch.District).Encode())));
            world.Step();
            Assert.Equal(first, node.PortFor(TestContent.Mail("letter", elsewhere, 1, 1)));
            // Postmaster's Eye: twice the pieces a second; its slots are all the sorter has already.
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, Db.Perk("postmasters_eye")));
            Assert.Equal(3, world.FilterSlotsOpen(Sorter));
            world.Step();
            Assert.Equal(2f, world.Belts.SorterThroughputMult, 3);
        }
    }
}
