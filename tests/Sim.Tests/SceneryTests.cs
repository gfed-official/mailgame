using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    public class SceneryTests
    {
        private const float Tile = SimConstants.TileSize;
        private static WorldMap Map => TestContent.Db.Map("test_street");
        private static readonly MoveConstants Consts = MoveConstants.From(TestContent.Db.Balance);

        [Fact]
        public void TheTownOfTheVideoIsThere()
        {
            var counts = new Dictionary<PropKind, int>();
            foreach (var p in Map.Props) counts[p.Kind] = counts.GetValueOrDefault(p.Kind) + 1;
            Assert.True(counts.GetValueOrDefault(PropKind.Fence) >= Map.Destinations.Count, "a fence in front of every house");
            Assert.True(counts.GetValueOrDefault(PropKind.Lamp) >= 6, "street lamps");
            Assert.True(counts.GetValueOrDefault(PropKind.Bench) >= 2, "benches");
            Assert.True(counts.GetValueOrDefault(PropKind.Tree) >= 10, "trees");
            Assert.True(counts.GetValueOrDefault(PropKind.Bush) >= Map.Destinations.Count, "bushes");
            Assert.True(counts.GetValueOrDefault(PropKind.Boulder) >= 3, "boulders");
            Assert.True(counts.GetValueOrDefault(PropKind.Flowers) >= 10, "flowers");
        }

        [Fact]
        public void SameMapSameScenery()
        {
            var a = Scenery.Generate(Map);
            var b = Scenery.Generate(Map);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Kind, b[i].Kind);
                Assert.Equal(a[i].X, b[i].X);
                Assert.Equal(a[i].Z, b[i].Z);
                Assert.Equal(a[i].X2, b[i].X2);
                Assert.Equal(a[i].Z2, b[i].Z2);
                Assert.Equal(a[i].Scale, b[i].Scale);
                Assert.Equal(a[i].Variant, b[i].Variant);
            }
        }

        [Fact]
        public void SolidPropsStandOnOpenGroundInsideTheMap()
        {
            foreach (var p in Map.Props)
            {
                if (!p.Solid) continue;
                foreach (var (x, z) in new[] { (p.X, p.Z), (p.X2, p.Z2) })
                {
                    Assert.InRange(x, 0f, Map.WidthM);
                    Assert.InRange(z, 0f, Map.DepthM);
                    int tx = (int)MathF.Floor(x / Tile), tz = (int)MathF.Floor(z / Tile);
                    Assert.False(Map.IsSolid(tx, tz), $"{p.Kind} at {x:0.0}, {z:0.0} is inside a building");
                    Assert.NotEqual(TileType.Street, Map.GetTile(tx, tz));
                }
            }
        }

        [Fact]
        public void NothingSolidStandsInTheWayOfTheJob()
        {
            var po = Map.PostOffice;
            foreach (var p in Map.Props)
            {
                if (!p.Solid) continue;
                Assert.True(p.Distance(po.IntakeX, po.IntakeZ, out _, out _) - p.Radius > 2f, $"{p.Kind} at the Intake");
                Assert.True(p.Distance(po.SpawnPadX, po.SpawnPadZ, out _, out _) - p.Radius > 2f, $"{p.Kind} on the spawn pad");
                foreach (var d in Map.Destinations)
                    Assert.True(p.Distance(d.MailboxX, d.MailboxZ, out _, out _) - p.Radius > 0.7f, $"{p.Kind} at the mailbox of {d.Label}");
            }
        }

        [Fact]
        public void StreetsStayClearAndEveryMailboxCanBeWalkedTo()
        {
            for (int tz = 0; tz < Map.Rows; tz++)
                for (int tx = 0; tx < Map.Cols; tx++)
                    if (Map.GetTile(tx, tz) == TileType.Street) Assert.False(Map.IsNavBlocked(tx, tz), $"street tile {tx}, {tz} is blocked by a prop");

            var po = Map.PostOffice;
            var path = new List<(float X, float Z)>();
            foreach (var d in Map.Destinations)
            {
                Assert.True(TilePathfinder.Find(Map, po.SpawnPadX, po.SpawnPadZ, d.MailboxX, d.MailboxZ, path), $"no path to {d.Label}");
                // Every waypoint but the last is the centre of a tile clear of props.
                for (int i = 0; i < path.Count - 1; i++)
                    Assert.False(Map.IsNavBlocked((int)MathF.Floor(path[i].X / Tile), (int)MathF.Floor(path[i].Z / Tile)));
            }
            Assert.True(TilePathfinder.Find(Map, Map.Destinations[0].MailboxX, Map.Destinations[0].MailboxZ, po.IntakeX, po.IntakeZ, path));
        }

        [Fact]
        public void AWalkerFollowingATilePathTouchesNothing()
        {
            // The far mailbox and back, at the widest radius a path is planned for.
            var po = Map.PostOffice;
            var goal = Map.Houses[Map.Houses.Count - 1];
            var path = new List<(float X, float Z)>();
            Assert.True(TilePathfinder.Find(Map, po.SpawnPadX, po.SpawnPadZ, goal.MailboxX, goal.MailboxZ, path));
            float x = po.SpawnPadX, z = po.SpawnPadZ;
            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int step = 1; step <= 10; step++)
                {
                    float px = x + (path[i].X - x) * step / 10f, pz = z + (path[i].Z - z) * step / 10f;
                    float cx = px, cz = pz;
                    PlayerMovement.ResolveCollisions(ref cx, ref cz, 0f, Map, Consts.Radius);
                    if (i > 0) Assert.True(MathF.Abs(cx - px) + MathF.Abs(cz - pz) < 1e-4f, $"pushed at {px:0.00}, {pz:0.00}");
                }
                x = path[i].X; z = path[i].Z;
            }
        }

        [Fact]
        public void PropsAreSolidAndFencesCanBeJumped()
        {
            Prop? tree = null, fence = null;
            foreach (var p in Map.Props)
            {
                if (p.Kind == PropKind.Tree) tree ??= p;
                if (p.Kind == PropKind.Fence) fence ??= p;
            }
            Assert.NotNull(tree);
            Assert.NotNull(fence);

            // Walking at a tree stops at its trunk.
            var t = tree!.Value;
            var s = new PlayerMoveState { X = t.X - 1.5f, Z = t.Z, Grounded = true };
            var east = new PlayerInput { MoveX = PlayerInput.QuantizeAxis(1f) };
            for (int i = 0; i < 30; i++) PlayerMovement.Step(ref s, east, Map, 1f, Consts);
            float gap = t.Distance(s.X, s.Z, out _, out _);
            Assert.True(gap >= t.Radius + Consts.Radius - 0.01f, $"inside the trunk: {gap:0.00} m from its centre");
            Assert.True(s.X < t.X + t.Radius + Consts.Radius + 0.5f || MathF.Abs(s.Z - t.Z) > 0.05f, "walked straight through");

            // A fence stops a walker and lets a jumper over.
            var f = fence!.Value;
            float mx = (f.X + f.X2) * 0.5f, mz = (f.Z + f.Z2) * 0.5f;
            bool alongX = MathF.Abs(f.X2 - f.X) > MathF.Abs(f.Z2 - f.Z);
            var walk = new PlayerMoveState { X = alongX ? mx : mx - 1.2f, Z = alongX ? mz - 1.2f : mz, Grounded = true };
            var jump = walk;
            var across = new PlayerInput { MoveX = PlayerInput.QuantizeAxis(alongX ? 0f : 1f), MoveZ = PlayerInput.QuantizeAxis(alongX ? 1f : 0f) };
            var hop = across;
            hop.Buttons = InputButtons.Jump;
            for (int i = 0; i < 20; i++)
            {
                PlayerMovement.Step(ref walk, across, Map, 1f, Consts);
                PlayerMovement.Step(ref jump, hop, Map, 1f, Consts);
            }
            float walked = alongX ? walk.Z - mz : walk.X - mx, jumped = alongX ? jump.Z - mz : jump.X - mx;
            Assert.True(walked < 0f, $"walked through the fence ({walked:0.00})");
            Assert.True(jumped > 0.5f, $"did not clear the fence ({jumped:0.00})");
        }
    }
}
