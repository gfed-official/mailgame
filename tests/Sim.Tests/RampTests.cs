using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 04 §2.1 elevated variant, doc 10 §2.1 belt_mk1_ramp and belt_mk1_elevated: an Elevated
    /// Belt runs one level up and may stand on a street; a Belt Ramp, two tiles long, takes mail
    /// from one level to the other. Mail goes on only where the heights meet, so a line crosses
    /// Larch Lane over the heads of whoever walks it.
    /// </summary>
    public class RampTests
    {
        private const int Tps = 30;
        /// <summary>Larch Lane is rows 13 and 14 of the test street.</summary>
        private const int StreetZ0 = 13, StreetZ1 = 14;

        private uint _nextRequest = 1;
        private uint _nextMail = 995000;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Ramp => Db.Building("belt_mk1_ramp");
        private static BuildingDef Elevated => Db.Building("belt_mk1_elevated");
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

        private (SimWorld World, Player Host) InPrep()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 40)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 40)));
            return (world, host);
        }

        /// <summary>Builds from beside the tile, a step off the column the line runs down.</summary>
        private OpResult Put(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation)
        {
            SimDriver.Teleport(p, Centre(tx) + 1.6f, Centre(tz));
            return Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));
        }

        /// <summary>A column whose verges on both sides of Larch Lane are open for the crossing's eight tiles.</summary>
        private static int CrossingColumn(SimWorld world, int width = 1, int z0 = 9, int z1 = 18, int from = 16)
        {
            for (int x = from; x < world.Map.Cols - 2 - width; x++)
            {
                bool open = true;
                for (int z = z0; z <= z1 && open; z++)
                    for (int dx = 0; dx < width && open; dx++)
                        if (z < StreetZ0 || z > StreetZ1) open = BuildRules.CheckTile(world.Map, world.Constructs, x + dx, z) == OpResult.Ok;
                if (open) return x;
            }
            throw new Xunit.Sdk.XunitException($"no {width} open columns from row {z0} to {z1}");
        }

        [Fact]
        public void TheRampAndTheElevatedBelt_AreContent_OnTheBeltsKey()
        {
            Assert.Equal((2, 8, 120, false, true), (Ramp.Length, Ramp.Rotations, Ramp.Hp, Ramp.Drags, Ramp.Ramp));
            Assert.Equal((1, 4, 80, true, 1, true), (Elevated.Length, Elevated.Rotations, Elevated.Hp, Elevated.Drags, Elevated.Level, Elevated.OnStreet));
            Assert.Equal(new[] { ("plank", 3), ("iron_ingot", 2) }, Ramp.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal(new[] { ("plank", 2), ("iron_ingot", 1) }, Elevated.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Equal(Belt.Recipe.Key, Ramp.Recipe.Key);
            Assert.Equal(Belt.Recipe.Key, Elevated.Recipe.Key);
            Assert.False(Ramp.Rideable || Elevated.Rideable || Ramp.Solid || Elevated.Solid);
            Assert.True(Belt.Rideable);
        }

        [Fact]
        public void ARamp_TakesTwoTiles_BothOpen_AndGoesWhole_TakenDownByEither()
        {
            var (world, host) = InPrep();
            int x = CrossingColumn(world);
            // Facing the street from the north verge: its second tile would be on Larch Lane.
            Assert.Equal(OpResult.OnStreet, Put(world, host, Ramp, x, StreetZ0 - 1, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Ramp, x, StreetZ0 - 3, 1));
            var ramp = world.Constructs.At(x, StreetZ0 - 3)!;
            Assert.Same(ramp, world.Constructs.At(x, StreetZ0 - 2));
            Assert.Single(world.Constructs.All);
            Assert.Equal((0, 1, -1), (ramp.IndexOf(x, StreetZ0 - 3), ramp.IndexOf(x, StreetZ0 - 2), ramp.IndexOf(x, StreetZ0 - 1)));
            // Nothing else goes on its second tile.
            Assert.Equal(OpResult.Blocked, Put(world, host, Belt, x, StreetZ0 - 2, 0));
            // A belt may not stand on the street; an Elevated Belt may.
            Assert.Equal(OpResult.OnStreet, Put(world, host, Belt, x + 1, StreetZ0, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Elevated, x + 1, StreetZ0, 1));
            // Taken down by its second tile, it goes from both, and gives all of it back in Prep.
            int planks = host.Inventory.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == Db.Item("plank").Index).Sum(e => e.Stack.Count);
            SimDriver.Teleport(host, Centre(x) + 1.6f, Centre(StreetZ0 - 2));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, x, StreetZ0 - 2)));
            Assert.Null(world.Constructs.At(x, StreetZ0 - 3));
            Assert.Null(world.Constructs.At(x, StreetZ0 - 2));
            Assert.Equal(planks + 3, host.Inventory.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == Db.Item("plank").Index).Sum(e => e.Stack.Count));
        }

        [Fact]
        public void ALine_CrossesTheStreet_UpARamp_OverIt_AndDownAnother_AndTheMailRidesIt()
        {
            var (world, host) = InPrep();
            int x = CrossingColumn(world);
            // South down column x: a belt, a ramp up, two elevated belts over Larch Lane, a ramp down, a belt.
            Assert.Equal(OpResult.Ok, Put(world, host, Belt, x, 10, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Ramp, x, 11, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Elevated, x, 13, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Elevated, x, 14, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Ramp, x, 15, 5));
            Assert.Equal(OpResult.Ok, Put(world, host, Belt, x, 17, 1));
            world.Step();
            var belts = world.Belts;
            var head = belts.SegmentAt(x, 10)!;
            var up = belts.SegmentAt(x, 11)!;
            var over = belts.SegmentAt(x, 13)!;
            var down = belts.SegmentAt(x, 15)!;
            var foot = belts.SegmentAt(x, 17)!;
            Assert.Same(up, belts.SegmentAt(x, 12));
            Assert.Same(over, belts.SegmentAt(x, 14));
            Assert.Same(down, belts.SegmentAt(x, 16));
            Assert.Equal((2, 2, 2), (up.Tiles.Count, over.Tiles.Count, down.Tiles.Count));
            Assert.Same(up, head.Next);
            Assert.Same(over, up.Next);
            Assert.Same(down, over.Next);
            Assert.Same(foot, down.Next);
            // Up the ramp from the ground to a level up, over at that, and down again.
            Assert.Equal(0f, belts.HeightAt(up, 0f), 3);
            Assert.Equal(BeltNetwork.LevelHeight * 0.5f, belts.HeightAt(up, 2f), 3);
            Assert.Equal(BeltNetwork.LevelHeight, belts.HeightAt(up, 4f), 3);
            Assert.Equal(BeltNetwork.LevelHeight, belts.HeightAt(over, 1f), 3);
            Assert.Equal(0f, belts.HeightAt(down, 4f), 3);

            var dest = world.Map.Destinations.Single(d => d.Address.Number == 1);
            var letter = TestContent.Mail("letter", dest.Address, _nextMail++, world.Run.Shift);
            Assert.True(world.PutOnBelt(head, letter));
            // 18 m at 2 m/s: across in under ten seconds, waiting at the open end.
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.Equal(letter.Id, foot.Items.Single().Mail.Id);
        }

        [Fact]
        public void MailGoesOnlyWhereTheHeightsMeet_AndAMailboxTakesItOnlyFromTheGround()
        {
            var (world, host) = InPrep();
            int x = CrossingColumn(world, 1, 10, 12);
            int x2 = CrossingColumn(world, 1, 9, 11, from: x + 2);
            // A ground belt straight into the back of an elevated one, and on from its front into a ground belt.
            Assert.Equal(OpResult.Ok, Put(world, host, Belt, x, 10, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Elevated, x, 11, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Belt, x, 12, 1));
            // A ramp's high end facing a ground belt hands it nothing either.
            Assert.Equal(OpResult.Ok, Put(world, host, Ramp, x2, 9, 1));
            Assert.Equal(OpResult.Ok, Put(world, host, Belt, x2, 11, 1));
            world.Step();
            var belts = world.Belts;
            Assert.Equal(BeltEndKind.None, belts.SegmentAt(x, 10)!.End);
            Assert.Equal(BeltEndKind.None, belts.SegmentAt(x, 11)!.End);
            Assert.Equal(BeltEndKind.None, belts.SegmentAt(x2, 9)!.End);
            Assert.Equal(5, belts.Segments.Count);
            Assert.Equal((0, 2, 2), (belts.InLevel(10 * world.Map.Cols + x), belts.InLevel(11 * world.Map.Cols + x), belts.OutLevel(10 * world.Map.Cols + x2)));

            // An elevated belt facing a mailbox puts nothing into it: it runs over the box.
            var box = world.Map.Destinations.Single(d => d.Address.Number == 1);
            int bx = (int)(box.MailboxX / SimConstants.TileSize), bz = (int)(box.MailboxZ / SimConstants.TileSize);
            Assert.Equal(OpResult.Ok, Put(world, host, Elevated, bx - 1, bz, 0));
            world.Step();
            Assert.Equal(BeltEndKind.None, belts.SegmentAt(bx - 1, bz)!.End);
        }

        [Fact]
        public void UnderAnElevatedBelt_ACarrierWalksTheStreet_NotCarried()
        {
            var (world, host) = InPrep();
            int x = CrossingColumn(world);
            Assert.Equal(OpResult.Ok, Put(world, host, Elevated, x, StreetZ0, 0));
            var c = MoveConstants.From(Db);
            var s = new PlayerMoveState { X = Centre(x), Z = Centre(StreetZ0), Grounded = true };
            for (int i = 0; i < 30; i++) PlayerMovement.Step(ref s, new PlayerInput(), world.Map, 1f, c, world.Constructs);
            Assert.Equal((Centre(x), Centre(StreetZ0)), (s.X, s.Z));
            Assert.Equal(world.Map.HeightAt(s.X, s.Z), s.Y, 3);
        }
    }
}
