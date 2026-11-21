using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The routing graph (doc 02 §3.8): nodes where streets meet, end or leave the map, edges with
    /// their length, surface and street; every street of the map on it, all of it one network, and
    /// routes along it from anywhere to anywhere by the shortest way.
    /// </summary>
    public class RoutingGraphTests
    {
        private static ContentDb Db => TestContent.Db;

        [Fact]
        public void TheTestStreet_IsOneEdgeFromEdgeToEdge()
        {
            var map = Db.Map("test_street");
            var g = map.Routes;
            Assert.Equal(2, g.Nodes.Count);
            var edge = Assert.Single(g.Edges);
            Assert.Equal(map.WidthM, edge.Length, 3);
            Assert.Equal((RouteSurface.Paved, 1), (edge.Surface, edge.Street));
            Assert.All(g.Nodes, n => Assert.Equal(RouteNodeKind.MapEdge, n.Kind));
            Assert.True(g.Connected());

            // Along the street a route is the street; from a mailbox to the next it steps on and off it.
            var points = new List<(float X, float Z)>();
            float z = map.Streets[0].Rect.CenterZ;
            Assert.Equal(60f, g.Route(10f, z, 70f, z, points), 3);
            Assert.Equal((70f, z), points[^1]);
        }

        [Fact]
        public void EveryTownsStreets_AreOneNetwork_WithEveryStreetTileOnIt()
        {
            for (uint seed = 1; seed <= 40; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                var g = map.Routes;
                Assert.True(g.Connected(), $"seed {seed}");
                Assert.Equal(g.Edges.Sum(e => e.Length), g.TotalLength, 1);
                foreach (var e in g.Edges)
                {
                    var a = g.Nodes[e.A];
                    var b = g.Nodes[e.B];
                    // Along x or along z, the length its own, on a street of the map.
                    Assert.True(MathF.Abs(a.X - b.X) < 0.01f || MathF.Abs(a.Z - b.Z) < 0.01f);
                    Assert.Equal(MathF.Abs(a.X - b.X) + MathF.Abs(a.Z - b.Z), e.Length, 2);
                    Assert.NotNull(map.GetStreet(e.Street));
                }
                for (int tz = 0; tz < map.Rows; tz++)
                    for (int tx = 0; tx < map.Cols; tx++)
                    {
                        if (map.GetTile(tx, tz) != TileType.Street) continue;
                        g.NearestEdge((tx + 0.5f) * SimConstants.TileSize, (tz + 0.5f) * SimConstants.TileSize, out _, out _, out float off);
                        Assert.True(off <= 1.5f, $"seed {seed}: the street tile ({tx}, {tz}) is {off:F1} m from the graph");
                    }
                // The main street leaves the map at both ends: two ways in from the island.
                Assert.Equal(2, g.Nodes.Count(n => n.Kind == RouteNodeKind.MapEdge));
                Assert.Contains(g.Nodes, n => n.Kind == RouteNodeKind.Junction && g.EdgesAt(n.Id).Count >= 3);
                Assert.All(g.Nodes.Where(n => n.Kind == RouteNodeKind.End), n => Assert.Single(g.EdgesAt(n.Id)));
            }
        }

        [Fact]
        public void ARoute_RunsAlongTheStreets_NoShorterThanTheCrowFlies_AndNoLongerThanRoundTheBlocks()
        {
            var points = new List<(float X, float Z)>();
            for (uint seed = 1; seed <= 10; seed++)
            {
                var map = WorldGenerator.Generate(Db, seed);
                var po = map.PostOffice;
                foreach (var d in map.Destinations)
                {
                    float length = map.Routes.Route(po.SpawnPadX, po.SpawnPadZ, d.MailboxX, d.MailboxZ, points);
                    float straight = MathF.Sqrt((d.MailboxX - po.SpawnPadX) * (d.MailboxX - po.SpawnPadX) + (d.MailboxZ - po.SpawnPadZ) * (d.MailboxZ - po.SpawnPadZ));
                    Assert.True(length >= straight - 0.01f);
                    // Round the blocks it is at most the two legs of the right angle, and out to a cross
                    // street and back at either end (cross streets are at most 16 tiles, 32 m, apart).
                    Assert.True(length <= MathF.Abs(d.MailboxX - po.SpawnPadX) + MathF.Abs(d.MailboxZ - po.SpawnPadZ) + 4 * 32f + 10f,
                        $"seed {seed}: {length:F0} m to {map.FormatAddress(d.Address)}");
                    Assert.Equal((po.SpawnPadX, po.SpawnPadZ), points[0]);
                    Assert.Equal((d.MailboxX, d.MailboxZ), points[^1]);
                    float sum = 0f;
                    for (int i = 1; i < points.Count; i++)
                        sum += MathF.Sqrt((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X) + (points[i].Z - points[i - 1].Z) * (points[i].Z - points[i - 1].Z));
                    Assert.Equal(length, sum, 1);
                    // Between the step on and the step off, every point is on a street.
                    for (int i = 1; i < points.Count - 1; i++)
                        Assert.Equal(TileType.Street, map.GetTile((int)MathF.Floor(points[i].X / SimConstants.TileSize), (int)MathF.Floor(points[i].Z / SimConstants.TileSize)));
                }
            }
        }

        [Fact]
        public void TheSameSeed_MakesTheSameGraph()
        {
            var a = WorldGenerator.Generate(Db, 17).Routes;
            var b = WorldGenerator.Generate(Db, 17).Routes;
            Assert.Equal(a.Nodes.Select(n => (n.X, n.Z, n.Kind)), b.Nodes.Select(n => (n.X, n.Z, n.Kind)));
            Assert.Equal(a.Edges.Select(e => (e.A, e.B, e.Length, e.Street)), b.Edges.Select(e => (e.A, e.B, e.Length, e.Street)));
        }
    }
}
