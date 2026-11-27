using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 11 §10.1's first sanity check, played on the real sim rather than BalanceSim's abstract agents
    /// (design choice 151): "Solo shift 2 … a solo player loses 0–2 belt tiles if undefended and nothing if they
    /// built 6 walls." One carrier (the clumsy bot of <see cref="RaidFightTests"/>) answers shift 2's raid with a
    /// line of six Conveyor Belts between the Post Office and the raiders' way in; with six Wooden Walls on the
    /// line's side towards them, and without. Shift 2 sends Raiders, Swarmlings and Archers only, which go for
    /// carriers and hold their line otherwise (design choice 128, nit 17); a belt is struck only where it blocks
    /// one's way, so the check holds with and without the walls — recorded as an open question (choice 151).
    /// </summary>
    public class BalanceSanityTests
    {
        private const int Tps = SimConstants.TickRate;
        private const int Line = 6;
        private static readonly uint[] Seeds = { 3, 11, 21, 40 };

        private readonly ITestOutputHelper _output;
        private uint _nextRequest = 1;

        public BalanceSanityTests(ITestOutputHelper output) => _output = output;

        private static ContentDb Db => TestContent.Db;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <returns>The belt tiles lost, how many raiders came, and how near the nearest came to the line, m.</returns>
        private (int Lost, int Raiders, float Nearest) ShiftTwo(uint seed, bool walls)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = seed, Draft = false, StartingKits = false });
            var p = world.AddPlayer("carrier")!;
            world.SetPlaytest(p, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, 2);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal((RunPhase.Delivery, 2), (world.Run.Phase, world.Run.Shift));

            var map = world.Map;
            var belt = Db.Building("belt_mk1");
            var wall = Db.Building("wall_wood");
            var edge = map.SpawnEdges.First(e => e.UnlockShift <= 2);
            int padX = (int)(map.PostOffice.SpawnPadX / SimConstants.TileSize), padZ = (int)(map.PostOffice.SpawnPadZ / SimConstants.TileSize);
            int toward = edge.X > map.PostOffice.SpawnPadX ? 1 : -1;
            bool Fits(BuildingDef def, int tx, int tz, int rotation) => BuildRules.CheckPlacement(map, world.Constructs, def, tx, tz, rotation) == OpResult.Ok;

            // A line out along the raiders' way in (the street from the edge to the Post Office), from 12 tiles in
            // from the edge, beside the street, the walls' tiles free on its side towards the edge.
            int edgeX = (int)(edge.X / SimConstants.TileSize), edgeZ = (int)(edge.Z / SimConstants.TileSize);
            (int X, int Z)? site = null;
            for (int in_ = 12; in_ <= Math.Abs(edgeX - padX) && site == null; in_++)
                for (int dz = 0; dz <= 8 && site == null; dz++)
                    foreach (int side in new[] { 1, -1 })
                {
                    if (site != null) break;
                    int tx = edgeX - toward * in_, tz = edgeZ + side * dz - (side < 0 ? Line - 1 : 0);
                    bool ok = true;
                    for (int k = 0; k < Line && ok; k++) ok = Fits(belt, tx, tz + k, 1) && Fits(wall, tx + toward, tz + k, 1);
                    if (ok) site = (tx, tz);
                }
            Assert.True(site != null, "no place for a line of six belts with room for walls beside it");
            var (sx, sz) = site!.Value;
            var belts = new List<Construct>();
            for (int k = 0; k < Line; k++) belts.Add(world.PutUp(belt, sx, sz + k, 1));
            var screen = new List<Construct>();
            if (walls)
                for (int k = 0; k < Line; k++) screen.Add(world.PutUp(wall, sx + toward, sz + k, 1));

            var bot = new RaidBot(seed) { AimErrorDegrees = 25f, LookEveryTicks = 12, SwingEveryTicks = 36 };
            int pulses = Db.Waves.Pulses;
            float nearest = float.MaxValue;
            for (int t = 0; t < 300 * Tps; t++)
            {
                world.QueueInput(p.Id, bot.Decide(world, p));
                world.Step();
                foreach (var e in world.Enemies)
                    foreach (var b in belts)
                    {
                        float dx = e.X - b.CentreX, dz = e.Z - b.CentreZ;
                        nearest = MathF.Min(nearest, MathF.Sqrt(dx * dx + dz * dz));
                    }
                if (world.RaidPulsesSpawned >= pulses && world.Enemies.All(e => e.Def.Airborne)) break;
            }
            int lost = belts.Count(b => world.Constructs.At(b.Tx, b.Tz) != b);
            float beltHurt = belts.Sum(b => b.Def.Hp - b.Hp), wallHurt = screen.Sum(w => w.Def.Hp - w.Hp);
            _output.WriteLine($"seed {seed}, walls {walls}: belts at ({sx}, {sz}) toward {edge.Direction}, pad ({padX}, {padZ}), edge ({edge.X:0}, {edge.Z:0}); {world.EnemiesSpawnedTotal} raiders, {world.EnemiesKilledTotal} felled, {world.Tick / Tps} s; belts lost {lost}, hurt {beltHurt:0}; walls hurt {wallHurt:0}; the nearest raider {nearest:0.0} m from the line");
            return (lost, world.EnemiesSpawnedTotal, nearest);
        }

        [Fact]
        public void ASoloCarrierWithSixWalls_LosesNoBeltInShiftTwo()
        {
            foreach (uint seed in Seeds)
            {
                var (lost, raiders, nearest) = ShiftTwo(seed, walls: true);
                Assert.True(raiders > 0, $"seed {seed}: no raid came");
                // The line is on the raid's way: raiders come within their 15 m aggro range of it.
                Assert.True(nearest <= 15f, $"seed {seed}: no raider came within 15 m of the line ({nearest:0.0} m)");
                Assert.True(lost == 0, $"seed {seed}: {lost} belt tiles lost behind six walls");
            }
        }

        [Fact]
        public void ASoloCarrierWithoutWalls_LosesTwoBeltsAtMostInShiftTwo()
        {
            foreach (uint seed in Seeds)
            {
                var (lost, _, nearest) = ShiftTwo(seed, walls: false);
                Assert.True(nearest <= 15f, $"seed {seed}: no raider came within 15 m of the line ({nearest:0.0} m)");
                Assert.True(lost <= 2, $"seed {seed}: {lost} belt tiles lost undefended (doc 11 §10.1: 0–2)");
            }
        }
    }
}
