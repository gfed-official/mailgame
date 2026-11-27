using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The Small Island town from a seed (doc 02): the same seed makes the same town to the last
    /// tile, every town is valid — mailboxes the Post Office walks to, unique addresses, districts
    /// of houses grown out from it —, and a run plays on it.
    /// </summary>
    public class WorldGeneratorTests
    {
        private static ContentDb Db => TestContent.Db;

        [Fact]
        public void TheSameSeed_MakesTheSameTown_AndOtherSeedsOthers()
        {
            var a = WorldGenerator.Generate(Db, 42);
            var b = WorldGenerator.Generate(Db, 42);
            Assert.Equal(a.MapHash, b.MapHash);
            Assert.Equal(a.Tiles, b.Tiles);
            Assert.Equal(a.TileDistrict, b.TileDistrict);
            Assert.Equal(a.Props.Count, b.Props.Count);
            Assert.Equal(a.Destinations.Select(d => (d.Address, d.MailboxX, d.MailboxZ, d.Variant)), b.Destinations.Select(d => (d.Address, d.MailboxX, d.MailboxZ, d.Variant)));

            var hashes = new HashSet<ulong>();
            for (uint seed = 1; seed <= 20; seed++) hashes.Add(WorldGenerator.Generate(Db, seed).MapHash);
            Assert.True(hashes.Count >= 19, $"{hashes.Count} different towns from 20 seeds");
            Assert.Equal(WorldGenerator.SmallIsland, a.Id);
            Assert.True(WorldGenerator.Generates(WorldGenerator.SmallIsland));
            Assert.Same(Db.Map("test_street"), WorldGenerator.Resolve(Db, "test_street", 42));
        }

        [Fact]
        public void EveryTownOfFiftySeeds_IsAMediumTownThatValidates()
        {
            for (uint seed = 1; seed <= 50; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                Assert.Null(WorldGenerator.Validate(map));
                // Doc 02 §1: a medium town grown towards a large one through its districts.
                Assert.InRange(map.Destinations.Count, 26, 60);
                Assert.Equal(map.Destinations.Count, map.Destinations.Select(d => d.Address).Distinct().Count());
                // Unique names per street (segments of one street share it), all from streets.json.
                foreach (var s in map.Streets) Assert.Contains(s.Name, Db.StreetNames);
                // Every house a lot of its own on the grid, its mailbox on open ground at its front.
                foreach (var d in map.Destinations)
                {
                    int tx = (int)MathF.Floor(d.MailboxX / SimConstants.TileSize), tz = (int)MathF.Floor(d.MailboxZ / SimConstants.TileSize);
                    Assert.False(map.IsSolid(tx, tz));
                    Assert.Equal(TileType.Lot, map.GetTile(d.Lot.X + 3, d.Lot.Y + (d.Building.Y == d.Lot.Y ? 3 : 0)));
                }
                // A beach along the south shore, the Post Office on the main street with its counter.
                Assert.Equal(TileType.Beach, map.GetTile(1, map.LandRows - 1));
                // Under it the harbour basin (doc 02 §3.2): shallow rows, then deep ones, to the map's edge.
                Assert.Equal((TileType.Water, TileType.DeepWater, map.LandRows + 6), (map.GetTile(1, map.LandRows), map.GetTile(map.Cols - 2, map.Rows - 1), map.Rows));
                Assert.True(map.PostOffice.HasShop);
                Assert.Single(map.Chests);
                // The main street runs from shore to shore in segments, the raiders' way in at its east end.
                var mainStreet = map.Streets.Where(st => st.Rect.Y == map.Streets[0].Rect.Y && st.Rect.H == 2 && st.Rect.W > 2).ToList();
                Assert.Equal((0, map.Cols), (mainStreet.Min(st => st.Rect.X), mainStreet.Sum(st => st.Rect.W)));
            }
        }

        [Fact]
        public void DistrictsGrowFromThePostOfficeOut_EightToTwelveHousesWhereTheyCan()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                Assert.InRange(map.Districts.Count, 3, WorldGenerator.MaxDistricts);
                for (int i = 0; i < map.Districts.Count; i++)
                {
                    var district = map.Districts[i];
                    Assert.Equal((i + 1, i + 1), (district.Id, district.UnlockShift));
                    int houses = map.Destinations.Count(d => d.Address.District == district.Id);
                    Assert.True(houses >= 1, $"seed {seed}: district {district.Id} is empty");
                    // A district is closed once it has eight; one segment more can take it a little past twelve.
                    if (district.Id < map.Districts.Count) Assert.InRange(houses, 8, 20);
                }
                // District 1 is the Post Office's: on average its houses are nearer than the last's.
                var po = map.PostOffice;
                double Mean(int id) => map.Destinations.Where(d => d.Address.District == id)
                    .Average(d => Math.Abs(d.MailboxX - po.SpawnPadX) + Math.Abs(d.MailboxZ - po.SpawnPadZ));
                Assert.True(Mean(1) < Mean(map.Districts.Count), $"seed {seed}: district 1 is not the nearest");
                // Every street segment and lot tile is marked with its district.
                foreach (var d in map.Destinations)
                    Assert.Equal(d.Address.District, map.TileDistrict[d.Lot.Y * map.Cols + d.Lot.X]);
            }
        }

        [Fact]
        public void NumbersRunFromTheWestEnd_OddNorthEvenSouth()
        {
            var map = WorldGenerator.Generate(Db, 7);
            foreach (var group in map.Destinations.GroupBy(d => d.Street.Name))
            {
                foreach (var d in group)
                {
                    bool northSide = d.Building.CenterZ < d.MailboxZ;
                    Assert.Equal(northSide ? 1 : 0, d.Address.Number % 2);
                }
                foreach (bool side in new[] { true, false })
                {
                    var row = group.Where(d => (d.Building.CenterZ < d.MailboxZ) == side).OrderBy(d => d.MailboxX).Select(d => d.Address.Number).ToList();
                    for (int i = 0; i < row.Count; i++) Assert.Equal(side ? 2 * i + 1 : 2 * i + 2, row[i]);
                }
            }
        }

        [Fact]
        public void ARunIsPlayedOnTheTown_MailComesAndIsDelivered()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 11, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false, QuickStart = true });
            Assert.Equal(WorldGenerator.SmallIsland, world.Map.Id);
            Assert.Equal(WorldGenerator.Generate(Db, 11).MapHash, world.Map.MapHash);
            var host = world.AddPlayer("host")!;
            for (int i = 0; i < 30 * 30 && world.Intake.EntryCount == 0; i++) world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.Intake.EntryCount > 0);

            // A letter for the farthest house, walked there from the pad along the tile paths.
            var far = world.Map.Houses.OrderByDescending(d => Math.Abs(d.MailboxX - host.Move.X) + Math.Abs(d.MailboxZ - host.Move.Z)).First();
            var letter = TestContent.Mail("letter", far.Address, 990001, world.Run.Shift);
            Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(letter)));
            Assert.True(SimDriver.WalkTo(world, host, far.MailboxX, far.MailboxZ, stopDistance: 1.5f, maxTicks: 6000, sprint: true));
            var entry = host.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail && e.Stack.Address == far.Address);
            world.Events.Clear();
            world.Submit(host.Id, SimRequest.Deliver(1, host.Hotbar.Id, entry.EntryId, far.Id));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, world.Events.OfType<RequestResultEvent>().Single(e => e.RequestId == 1).Result);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MailGoesOnlyToTheDistrictsOpenByTheShift(int shift)
        {
            // Doc 01 §1.2: district N opens in shift N; until then no mail is addressed to its houses.
            var world = new SimWorld(Db, new SimSettings { Seed = 5, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, shift);
            world.Submit(host.Id, SimRequest.StartRun(1));
            world.Step();
            world.Submit(host.Id, SimRequest.SetReady(2, true));
            world.Step();
            Assert.Equal((RunPhase.Delivery, shift), (world.Run.Phase, world.Run.Shift));
            SimDriver.StepTicks(world, 90 * 30);
            var districts = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Select(e => e.Stack.Address.District).ToList();
            Assert.NotEmpty(districts);
            Assert.All(districts, d => Assert.InRange(d, 1, shift));
            // The newest district gets its share at once.
            Assert.Contains(shift, districts);
        }
        [Fact]
        public void EveryTownOfFiftySeeds_HasABusinessDockOnTheBeachOverDeepWater_FromShiftFour()
        {
            var path = new System.Collections.Generic.List<(float X, float Z)>();
            for (uint seed = 1; seed <= 50; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                var dock = Assert.Single(map.Destinations, d => d.IsDock);
                Assert.Equal(("business_dock", WorldGenerator.DockShift), (dock.Type.Id, map.OpensAt(dock)));
                int tx = (int)MathF.Floor(dock.MailboxX / SimConstants.TileSize), tz = (int)MathF.Floor(dock.MailboxZ / SimConstants.TileSize);
                // At the quay's edge: open ground under it, deep water in front, walked to from the Post Office.
                Assert.False(map.IsSolid(tx, tz) || map.IsWater(tx, tz));
                Assert.Equal(TileType.DeepWater, map.GetTile(tx, tz + 1));
                Assert.True(TilePathfinder.Find(map, map.PostOffice.SpawnPadX, map.PostOffice.SpawnPadZ, dock.MailboxX, dock.MailboxZ, path), $"seed {seed}: no walk to the dock");
                Assert.True(dock.MailboxX > map.PostOffice.SpawnPadX, $"seed {seed}: the dock is not east of the Post Office");
            }
        }

        /// <summary>The dock's number is past the whole street's, not its segment's: seed 15 had "14 Heron Bend" for a house and the dock.</summary>
        [Theory]
        [InlineData(15u)]
        [InlineData(132u)]
        public void NoTwoDestinations_ShareALabel(uint seed)
        {
            var map = WorldGenerator.Generate(Db, seed);
            Assert.Equal(map.Destinations.Count, map.Destinations.Select(d => map.FormatAddress(d.Address)).Distinct().Count());
        }
    }
}
