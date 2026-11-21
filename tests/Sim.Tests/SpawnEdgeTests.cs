using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The raiders' ways in (doc 02 §3.10, doc 05 §3.2): the east end of the first street from
    /// shift 1, one more facing each later district unless one lies near, a path to the Post
    /// Office each; a raid's first half of pulses by one of those open, the rest by two.
    /// </summary>
    public class SpawnEdgeTests
    {
        private const int Tps = 30;
        private static ContentDb Db => TestContent.Db;

        [Fact]
        public void TheTestStreet_HasOneWayIn_TheEastEndOfLarchLane_WithItsLineWhereItWas()
        {
            var map = new SimWorld(Db, new SimSettings { Seed = 7 }).Map;
            var edge = Assert.Single(map.SpawnEdges);
            Assert.Equal((map.WidthM - 1.5f, 28f, 1), (edge.X, edge.Z, edge.UnlockShift));
            Assert.Equal(("east", "Larch Lane", "the east, along Larch Lane"), (edge.Direction, edge.Street, edge.Name));
            Assert.Equal((-1f, 0f), (edge.InX, edge.InZ));
            // The line of nit 17: on the street, 19 m from the pad (14, 21).
            var po = map.PostOffice;
            edge.LinePoint(po.SpawnPadX, po.SpawnPadZ, 19f, 6f, out float x, out float z, out float dx, out float dz);
            Assert.Equal(14f + MathF.Sqrt(19f * 19f - 7f * 7f), x, 3);
            Assert.Equal(28f, z, 3);
            Assert.Equal((-1f, 0f), (dx, dz));
            // A standoff longer than the street leaves the line at the mouth's depth.
            edge.LinePoint(po.SpawnPadX, po.SpawnPadZ, 500f, 6f, out x, out _, out _, out _);
            Assert.Equal(edge.X - 6f, x, 3);
        }

        [Fact]
        public void EveryGeneratedTown_HasWaysInFacingItsDistricts_OnOpenGround_WithAWayToThePostOffice()
        {
            int twoOrMore = 0;
            for (uint seed = 1; seed <= 30; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                var edges = map.SpawnEdges;
                var po = map.PostOffice;
                // The first is the main street's east end, open from the first shift.
                Assert.Equal((map.WidthM - 1.5f, 1, "east"), (edges[0].X, edges[0].UnlockShift, edges[0].Direction));
                Assert.NotEqual("", edges[0].Street);
                if (edges.Count >= 2) twoOrMore++;
                var unlocks = map.Districts.Select(d => d.UnlockShift).ToHashSet();
                var path = new List<(float X, float Z)>();
                for (int i = 0; i < edges.Count; i++)
                {
                    var e = edges[i];
                    Assert.Equal(i, e.Index);
                    Assert.Contains(e.UnlockShift, unlocks);
                    if (i > 0) Assert.True(e.UnlockShift >= edges[i - 1].UnlockShift, $"seed {seed}: the ways in are in the order they open");
                    // At the map's edge, on open ground other than the beach, far enough from the pad.
                    Assert.True(e.X < 3f || e.Z < 3f || e.X > map.WidthM - 3f || e.Z > map.DepthM - 3f, $"seed {seed}: way in {i} at ({e.X}, {e.Z})");
                    int tx = (int)(e.X / SimConstants.TileSize), tz = (int)(e.Z / SimConstants.TileSize);
                    Assert.False(map.IsNavBlocked(tx, tz), $"seed {seed}: way in {i} on a blocked tile");
                    Assert.NotEqual(TileType.Beach, map.GetTile(tx, tz));
                    Assert.True(Dist(e.X, e.Z, po.SpawnPadX, po.SpawnPadZ) >= SpawnEdges.MinPadDistance);
                    Assert.True(TilePathfinder.Find(map, e.X, e.Z, po.SpawnPadX, po.SpawnPadZ, path), $"seed {seed}: no walk from way in {i}");
                    // Its path runs from the mouth to the pad.
                    Assert.Equal((e.X, e.Z), e.Path[0]);
                    Assert.Equal((po.SpawnPadX, po.SpawnPadZ), e.Path[^1]);
                    // The inward way points into the map.
                    float ix = e.X + e.InX * 4f, iz = e.Z + e.InZ * 4f;
                    Assert.InRange(ix, 0f, map.WidthM);
                    Assert.InRange(iz, 0f, map.DepthM);
                    for (int j = 0; j < i; j++)
                        Assert.True(Dist(e.X, e.Z, edges[j].X, edges[j].Z) >= SpawnEdges.ShareDistance, $"seed {seed}: ways in {j} and {i} side by side");
                }
            }
            // Most towns are come at from more than one side.
            Assert.True(twoOrMore >= 25, $"only {twoOrMore} of 30 towns have a second way in");
        }

        [Fact]
        public void TheSameSeed_GivesTheSameWaysIn()
        {
            var a = WorldGenerator.Generate(Db, 15).SpawnEdges;
            var b = WorldGenerator.Generate(Db, 15).SpawnEdges;
            Assert.Equal(a.Select(e => (e.X, e.Z, e.UnlockShift, e.Name)), b.Select(e => (e.X, e.Z, e.UnlockShift, e.Name)));
            Assert.Equal(a.SelectMany(e => e.Path), b.SelectMany(e => e.Path));
        }

        [Fact]
        public void AnAlarmPost_RingsEarly_NearAnyWayInOpenByTheShift()
        {
            // Seed 1: east from shift 1, north from shift 2.
            var map = WorldGenerator.Generate(Db, 1);
            var north = map.SpawnEdges[1];
            Assert.Equal(("north", 2), (north.Direction, north.UnlockShift));
            var alarm = Db.Building("alarm_post");
            int tx = (int)(north.X / SimConstants.TileSize), tz = (int)(north.Z / SimConstants.TileSize) + 3;
            Assert.False(RaidRules.AlarmInRange(map, alarm, tx, tz, 1));
            Assert.True(RaidRules.AlarmInRange(map, alarm, tx, tz, 2));
        }

        [Fact]
        public void PulsesOneToThree_ComeByOneWayIn_FourToSixByTwo_AndTheyFleeByTheirOwn()
        {
            // Seed 1 in shift 5: four ways in are open.
            var world = new SimWorld(Db, new SimSettings { Seed = 1, MapId = WorldGenerator.SmallIsland, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.RaidNow | PlaytestFlags.NoQuota, 5);
            world.Submit(host.Id, SimRequest.StartRun(1));
            world.Step();
            world.Submit(host.Id, SimRequest.SetReady(2, true));
            world.Step();
            Assert.Equal((RunPhase.Delivery, 5), (world.Run.Phase, world.Run.Shift));
            Assert.Equal(4, world.Map.SpawnEdges.Count(e => e.OpenAt(5)));
            int a = world.RaidEdgeA, b = world.RaidEdgeB;
            Assert.InRange(a, 0, 3);
            Assert.InRange(b, 0, 3);
            Assert.NotEqual(a, b);
            Assert.Equal((sbyte)a, world.GetGlobalState().RaidEdgeA);
            Assert.Equal((sbyte)b, world.GetGlobalState().RaidEdgeB);

            // Out of the way, in the Post Office yard, so nobody is fought.
            uint start = world.RaidStartTick;
            uint interval = (uint)(Db.Waves.PulseIntervalSeconds * Tps);
            var byPulse = new Dictionary<int, List<int>>();
            var seen = new HashSet<uint>();
            var kept = new List<Enemy>();
            while (world.RaidPulsesSpawned < Db.Waves.Pulses)
            {
                world.Step();
                foreach (var e in world.Enemies)
                {
                    if (!seen.Add(e.Id.Value)) continue;
                    int pulse = (int)((world.Tick - start) / interval);
                    (byPulse.TryGetValue(pulse, out var l) ? l : byPulse[pulse] = new List<int>()).Add(e.Edge);
                    var edge = world.EdgeOf(e);
                    Assert.True(Dist(e.X, e.Z, edge.X, edge.Z) <= 3f + edge.HalfWidth + 1f, $"a raider of pulse {pulse} came in {Dist(e.X, e.Z, edge.X, edge.Z):0.0} m from its way in");
                }
                // Felled as they come, so the cap on the living never stops a pulse; the last pulse is kept to flee.
                if (world.RaidPulsesSpawned < Db.Waves.Pulses)
                    foreach (var e in world.Enemies.ToList()) world.DamageEnemy(e, 1e6f, null);
            }
            kept.AddRange(world.Enemies);
            Assert.Equal(Db.Waves.Pulses, byPulse.Count);
            for (int pulse = 0; pulse < 3; pulse++) Assert.All(byPulse[pulse], edge => Assert.Equal(a, edge));
            var late = byPulse.Where(kv => kv.Key >= 3).SelectMany(kv => kv.Value).ToList();
            Assert.Contains(a, late);
            Assert.Contains(b, late);
            Assert.All(late, edge => Assert.True(edge == a || edge == b));

            // The clock runs out: each runs for the way it came by.
            Assert.Contains(kept, e => e.Edge == b);
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            Assert.Equal(RaidState.Fleeing, world.Raid);
            var startDist = kept.Where(e => world.Enemies.Contains(e)).ToDictionary(e => e, e => Dist(e.X, e.Z, world.EdgeOf(e).X, world.EdgeOf(e).Z));
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.All(startDist, kv =>
            {
                if (!world.Enemies.Contains(kv.Key)) return;
                var edge = world.EdgeOf(kv.Key);
                Assert.True(Dist(kv.Key.X, kv.Key.Z, edge.X, edge.Z) < kv.Value, "a fleeing raider did not head for its own way in");
            });
        }

        [Fact]
        public void TheRaidsWaysIn_AreTheSeedsToo_AndOnlyThoseOpen()
        {
            // Shift 2 on seed 1: the east and the north are open, so the raid comes by both.
            static (int, int) Raid(uint seed, int shift)
            {
                var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = WorldGenerator.SmallIsland, Draft = false });
                var host = world.AddPlayer("host")!;
                world.SetPlaytest(host, PlaytestFlags.RaidNow, shift);
                world.Submit(host.Id, SimRequest.StartRun(1));
                world.Step();
                world.Submit(host.Id, SimRequest.SetReady(2, true));
                world.Step();
                return (world.RaidEdgeA, world.RaidEdgeB);
            }
            var two = Raid(1, 2);
            Assert.Equal(new[] { 0, 1 }, new[] { two.Item1, two.Item2 }.OrderBy(i => i));
            Assert.Equal(two, Raid(1, 2));
            // The test street has one way in: the raid comes by it alone.
            var street = new SimWorld(Db, new SimSettings { Seed = 3, Draft = false });
            var p = street.AddPlayer("host")!;
            street.SetPlaytest(p, PlaytestFlags.RaidNow, 2);
            street.Submit(p.Id, SimRequest.StartRun(1));
            street.Step();
            street.Submit(p.Id, SimRequest.SetReady(2, true));
            street.Step();
            Assert.Equal((0, -1), (street.RaidEdgeA, street.RaidEdgeB));
        }

        private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
    }
}
