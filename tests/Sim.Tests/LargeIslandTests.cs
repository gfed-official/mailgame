using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Large Island from a seed (doc 02 §1, §3.3–§3.8, §3.12; doc 12 M4): two or three towns, the Post Office's
    /// holding districts 1–2 and the others 3–5, an inlet between them crossed by a road round its head or by a marked
    /// ferry lane, 60-odd houses, a Business Dock or two — and doc 12's acceptance: 100 seeds that validate.
    /// </summary>
    public class LargeIslandTests
    {
        private static ContentDb Db => TestContent.Db;

        [Fact]
        public void TheSameSeed_MakesTheSameIsland_AndOtherSeedsOthers()
        {
            var a = LargeIslandGenerator.Generate(Db, 42);
            var b = LargeIslandGenerator.Generate(Db, 42);
            Assert.Equal(a.MapHash, b.MapHash);
            Assert.Equal(a.Tiles, b.Tiles);
            Assert.Equal(a.Props.Count, b.Props.Count);
            Assert.Equal(a.FerryLanes.Select(l => (l.AX, l.AZ, l.BX, l.BZ)), b.FerryLanes.Select(l => (l.AX, l.AZ, l.BX, l.BZ)));
            Assert.Equal(LargeIslandGenerator.LargeIsland, a.Id);
            Assert.Equal(LargeIslandGenerator.TerrainSize, a.Terrain!.Cols);
            Assert.True(WorldGenerator.Generates(LargeIslandGenerator.LargeIsland));
            Assert.Equal(a.MapHash, WorldGenerator.Resolve(Db, LargeIslandGenerator.LargeIsland, 42).MapHash);
            Assert.NotEqual(WorldGenerator.Generate(Db, 42).MapHash, a.MapHash);
            var hashes = new HashSet<ulong>();
            for (uint seed = 1; seed <= 8; seed++) hashes.Add(LargeIslandGenerator.Generate(Db, seed).MapHash);
            Assert.Equal(8, hashes.Count);
        }

        /// <summary>Doc 12 M4: "100 Large Island seeds validate (all destinations reachable by walk or ferry lane; ≥ 1 Business Dock)".</summary>
        [Fact]
        public void AHundredSeeds_Validate_EveryDestinationWalkedToOrFerried_AndADockEach()
        {
            var maps = new WorldMap[100];
            Parallel.For(0, maps.Length, i => maps[i] = LargeIslandGenerator.Generate(Db, (uint)(i + 1)));
            int ferried = 0, roundTheHead = 0, threeTowns = 0;
            foreach (var map in maps)
            {
                string at = map.Name;
                Assert.Null(WorldGenerator.Validate(map));
                Assert.True(map.Destinations.Count(d => d.IsDock) >= 1, $"{at}: no Business Dock");
                Assert.Equal(map.Destinations.Count, map.Destinations.Select(d => map.FormatAddress(d.Address)).Distinct().Count());
                int houses = map.Houses.Count;
                Assert.InRange(houses, LargeIslandGenerator.MinHouses, LargeIslandGenerator.MaxHouses);
                Assert.Equal(5, map.Districts.Count);
                foreach (var district in map.Districts)
                    Assert.InRange(map.Houses.Count(h => h.Address.District == district.Id), LargeIslandGenerator.MinDistrictHouses, LargeIslandGenerator.MaxDistrictHouses);

                // Doc 02 §3.7: the towns past the Post Office's are districts 3–5, all of them west of the PO town's houses.
                float poTownWest = map.Houses.Where(h => h.Address.District <= 2).Min(h => h.MailboxX);
                Assert.All(map.Houses.Where(h => h.Address.District >= 3), h => Assert.True(h.MailboxX < poTownWest, $"{at}: {map.FormatAddress(h.Address)} of district {h.Address.District} in the PO town"));
                // A third town lies down a long road (80 m and more) west of the second.
                if (map.Houses.Where(h => h.Address.District == 4).Min(h => h.MailboxX) - map.Houses.Where(h => h.Address.District == 5).Max(h => h.MailboxX) > 80f) threeTowns++;

                var path = new List<(float X, float Z)>();
                var po = map.PostOffice;
                bool allWalked = map.Destinations.All(d => TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, d.MailboxX, d.MailboxZ, path));
                if (map.FerryLanes.Count == 0)
                {
                    // A road round the inlet's head: every mailbox walked to.
                    Assert.True(allWalked, $"{at}: no ferry lane, yet a mailbox no walk reaches");
                    roundTheHead++;
                    continue;
                }
                ferried++;
                // The lane is needed (the far town cannot be walked to), runs from the PO's shore to the far one over water only.
                Assert.False(allWalked, $"{at}: a ferry lane where every mailbox can be walked to");
                foreach (var lane in map.FerryLanes)
                {
                    Assert.True(TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, lane.AX, lane.AZ, path), $"{at}: the lane's near end is not walked to");
                    Assert.False(TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, lane.BX, lane.BZ, path), $"{at}: the lane's far end is walked to");
                    Assert.InRange(lane.Length, 20f, 40f);
                    for (int k = 1; k < 20; k++)
                    {
                        float x = lane.AX + (lane.BX - lane.AX) * k / 20f, z = lane.AZ + (lane.BZ - lane.AZ) * k / 20f;
                        int tx = (int)(x / 2f), tz = (int)(z / 2f);
                        Assert.True(map.IsWater(tx, tz) || (Math.Abs(x - lane.AX) < 1.5f && Math.Abs(z - lane.AZ) < 1.5f) || (Math.Abs(x - lane.BX) < 1.5f && Math.Abs(z - lane.BZ) < 1.5f), $"{at}: the lane crosses land at {tx},{tz}");
                    }
                }
                // Every mailbox is walked to from the pad or from a lane's far end.
                foreach (var d in map.Destinations)
                    Assert.True(TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, d.MailboxX, d.MailboxZ, path)
                        || map.FerryLanes.Any(l => TilePathfinder.Find(map, l.BX, l.BZ, d.MailboxX, d.MailboxZ, path)), $"{at}: {map.FormatAddress(d.Address)} reached neither way");
                // The raiders come in only where they can walk to the Post Office.
                foreach (var edge in map.SpawnEdges)
                    Assert.True(TilePathfinder.Find(map, edge.X, edge.Z, po.SpawnPadX, po.SpawnPadZ, path), $"{at}: the raiders' way in from {edge.Name} cannot reach the Post Office");
            }
            // Both crossings come up, and both town counts.
            Assert.InRange(ferried, 20, 80);
            Assert.InRange(roundTheHead, 20, 80);
            Assert.InRange(threeTowns, 20, 80);
            double mean = maps.Average(m => m.Houses.Count);
            Assert.InRange(mean, 60.0, 70.0);
        }

        [Fact]
        public void TheIsland_IsAThousandMetresAcross_WithTheInletCutIntoIt()
        {
            var map = Enumerable.Range(1, 20).Select(s => LargeIslandGenerator.Generate(Db, (uint)s)).First(m => m.FerryLanes.Count > 0);
            var t = map.Terrain!;
            Assert.Equal(1000f, t.Cols * 2f);
            // The grid's inlet is the island's water under the sea; its banks are beach.
            var lane = map.FerryLanes[0];
            int mx = (int)((lane.AX + lane.BX) * 0.25f), mz = (int)((lane.AZ + lane.BZ) * 0.25f);
            Assert.True(map.IsWater(mx, mz));
            Assert.True(IslandTerrain.IsWater(map.BiomeAt(mx, mz)));
            int bank = (int)(lane.AX / 2f);
            Assert.Equal(TileType.Beach, map.GetTile(bank, mz + 6));
            Assert.Equal(Biome.Beach, map.BiomeAt(bank, mz + 6));
            // Cut on north through the island to the sea: the island tile just north of the grid in the inlet is water.
            Assert.True(IslandTerrain.IsWater(t.BiomeAt(t.TownX + mx, t.TownZ - 1)));
            Assert.True(IslandTerrain.IsWater(t.BiomeAt(t.TownX + mx, t.TownZ - 20)));
            // Nothing grows on the water.
            foreach (var p in map.Props)
                Assert.False(map.IsWater((int)(p.X / 2f), (int)(p.Z / 2f)) && p.Kind != PropKind.Fence, $"a {p.Kind} on the water at {p.X:0},{p.Z:0}");
        }

        /// <summary>Doc 02 §3.8 "never a soft lock": the team's Rowboat at the lane's near end, rowed across, the far town walked to.</summary>
        [Fact]
        public void AFerryLane_HasTheTeamsRowboat_AndACarrierRowsAcrossToTheFarTown()
        {
            uint seed = Enumerable.Range(1, 20).Select(s => (uint)s).First(s => LargeIslandGenerator.Generate(Db, s).FerryLanes.Count > 0);
            var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = LargeIslandGenerator.LargeIsland, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            uint id = 1;
            OpResult Request(SimRequest r)
            {
                world.Events.Clear();
                r.RequestId = id++;
                world.Submit(host.Id, r);
                world.Step();
                return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == r.RequestId).Result;
            }
            void Move(float x, float z, bool jump = false)
            {
                world.QueueInput(host.Id, new Players.PlayerInput
                {
                    Seq = host.LastQueuedSeq + 1, MoveX = Players.PlayerInput.QuantizeAxis(x), MoveZ = Players.PlayerInput.QuantizeAxis(z),
                    Buttons = jump ? Players.InputButtons.Jump : 0,
                });
                world.Step();
            }
            Assert.Equal(OpResult.Ok, Request(SimRequest.StartRun(0)));
            var map = world.Map;
            var lane = map.FerryLanes[0];
            // Whatever the kit (the Land kit's here): the Rowboat moored a little out from the near end.
            var boat = Assert.Single(world.Vehicles, v => v.Def.Id == SimWorld.FerryBoat);
            Assert.True(map.IsWater((int)(boat.X / 2f), (int)(boat.Z / 2f)));
            Assert.InRange(MathF.Sqrt((boat.X - lane.AX) * (boat.X - lane.AX) + (boat.Z - lane.AZ) * (boat.Z - lane.AZ)), 2f, 4f);

            Assert.True(SimDriver.WalkTo(world, host, lane.AX, lane.AZ, stopDistance: 0.6f, maxTicks: 6000, sprint: true));
            Assert.Equal(OpResult.Ok, Request(SimRequest.Mount(0, boat.Id.Value)));
            Assert.True(host.Move.Riding);
            float ux = (lane.BX - lane.AX) / lane.Length, uz = (lane.BZ - lane.AZ) / lane.Length;
            for (int i = 0; i < 30 * 30 && MathF.Abs(host.Move.X - lane.BX) + MathF.Abs(host.Move.Z - lane.BZ) > 2.2f; i++) Move(ux, uz);
            for (int i = 0; i < 3 && host.Move.Riding; i++) Move(0f, 0f, jump: true);
            Assert.False(host.Move.Riding);
            var path = new List<(float X, float Z)>();
            var po = map.PostOffice;
            Assert.False(TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, host.Move.X, host.Move.Z, path), "the carrier landed on the Post Office's side");
            var far = map.Houses.First(d => !TilePathfinder.Find(map, po.SpawnPadX, po.SpawnPadZ, d.MailboxX, d.MailboxZ, path));
            Assert.True(SimDriver.WalkTo(world, host, far.MailboxX, far.MailboxZ, stopDistance: 1.5f, maxTicks: 9000, sprint: true), $"no walk from the landing to {map.FormatAddress(far.Address)}");
        }

        [Fact]
        public void ARunIsPlayedOnTheIsland_ALetterIsDeliveredInThePostOfficesTown()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, MapId = LargeIslandGenerator.LargeIsland, Raids = false, Draft = false, QuickStart = true });
            Assert.Equal(LargeIslandGenerator.LargeIsland, world.Map.Id);
            var host = world.AddPlayer("host")!;
            for (int i = 0; i < 30 * 30 && world.Intake.EntryCount == 0; i++) world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.Intake.EntryCount > 0);
            // Shift 1's mail is all for district 1.
            foreach (var e in world.Intake.Entries)
                if (e.Stack.Kind == StackKind.Mail) Assert.Equal(1, e.Stack.Address.District);

            var far = world.Map.Houses.Where(d => d.Address.District == 2).OrderByDescending(d => Math.Abs(d.MailboxX - host.Move.X)).First();
            Assert.True(SimDriver.WalkTo(world, host, far.MailboxX, far.MailboxZ, stopDistance: 1.5f, maxTicks: 6000, sprint: true));
        }
    }
}
