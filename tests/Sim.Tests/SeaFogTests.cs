using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>Doc 10 §6 Sea Fog: "Random Delivery start (Large Island) · Boat speed −30%, Balloons do not spawn".</summary>
    public class SeaFogTests
    {
        private static ContentDb Db => TestContent.Db;
        private uint _nextRequest = 1;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) Delivery(string mapId, int shift, PlaytestFlags flags = PlaytestFlags.NoQuota)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 4, MapId = mapId, Draft = false, RunEvents = true, ForcedEvent = "fog" });
            var host = world.AddPlayer("host")!;
            host.MaxHp = host.Hp = 100000f;
            world.SetPlaytest(host, flags, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            return (world, host);
        }

        [Fact]
        public void TheFog_ComesAsDeliveryStarts_OnTheLargeIslandOnly_AndSlowsTheTeamsBoats()
        {
            var (street, _) = Delivery("test_street", 2);
            Assert.Null(street.ShiftEvent);
            Assert.Equal(1f, street.TeamStatOf(Stat.BoatSpeed, 1f));

            var (world, _) = Delivery(LargeIslandGenerator.LargeIsland, 2);
            var fog = Db.RunEvent("fog");
            Assert.Same(fog, world.ShiftEvent);
            Assert.True(world.EventLive(fog));
            Assert.Equal(0.7f, world.TeamStatOf(Stat.BoatSpeed, 1f), 3);
        }

        [Fact]
        public void InTheFog_ARowboatRowsAtSeventyPerCent()
        {
            var (world, host) = Delivery(LargeIslandGenerator.LargeIsland, 2);
            var map = world.Map;
            // Out on the inlet, deep water a good way north and south of it.
            (int X, int Z) at = (-1, -1);
            for (int tz = 20; tz < 40 && at.X < 0; tz++)
                for (int tx = 0; tx < map.Cols && at.X < 0; tx++)
                    if (Enumerable.Range(-12, 25).All(dz => Enumerable.Range(-1, 3).All(dx => map.GetTile(tx + dx, tz + dz) == TileType.DeepWater))) at = (tx, tz);
            Assert.True(at.X >= 0, "no inlet to row on");
            var boat = world.AddVehicleAt(Db.Vehicle("rowboat"), BuildRules.TileCentre(at.X), BuildRules.TileCentre(at.Z + 10), 0f, host.Id);
            SimDriver.Teleport(host, boat.X, boat.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            void Row(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, MoveZ = PlayerInput.QuantizeAxis(-1f) });
                    world.Step();
                }
            }
            Row(60);
            float z0 = host.Move.Z;
            Row(30);
            // 3 m/s in clear weather (BoatTests); 2.1 in the fog.
            Assert.InRange(z0 - host.Move.Z, 2.0f, 2.2f);
        }

        [Fact]
        public void InTheFog_NoBalloonComes()
        {
            var (world, _) = Delivery(LargeIslandGenerator.LargeIsland, 5, PlaytestFlags.NoQuota | PlaytestFlags.NoTimer | PlaytestFlags.RaidNow);
            Assert.True(world.EventLive(Db.RunEvent("fog")));
            int seen = 0;
            var ids = new System.Collections.Generic.HashSet<uint>();
            for (int i = 0; i < 30 * 120; i++)
            {
                world.Step();
                foreach (var e in world.Enemies)
                {
                    Assert.False(e.Def.Airborne, $"a {e.Def.Name} came in the fog");
                    if (ids.Add(e.Id.Value)) seen++;
                }
            }
            Assert.True(seen >= 10, $"only {seen} raiders came");
        }
    }
}
