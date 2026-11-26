using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;
using Xunit.Abstractions;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 12 M3 acceptance: "Enemy pathing never stalls: a fuzz test spawning waves against 200 random
    /// wall layouts finds a path or a wall to break within 2 s for every enemy" (design choice 147).
    /// </summary>
    public class PathingFuzzTests
    {
        private const int Tps = SimConstants.TickRate;
        private const int Layouts = 200;
        /// <summary>"within 2 s".</summary>
        private const int WithinTicks = 2 * Tps;
        /// <summary>Under way: this far in 2 s (a raider walks 4–14 m in them).</summary>
        private const float UnderWay = 1f;

        private readonly ITestOutputHelper _output;
        private uint _nextRequest = 1;

        public PathingFuzzTests(ITestOutputHelper output) => _output = output;

        private static ContentDb Db => TestContent.Db;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>One raider's last 2 s: where it stood each tick, and when it last struck.</summary>
        private sealed class Watch
        {
            public readonly float[] X = new float[WithinTicks + 1], Z = new float[WithinTicks + 1];
            public uint Born, LastStruck, Since;
            public bool Stalled;
        }

        /// <summary>Striking counts for 2 s, or for as long as it takes between its blows and a little (a Tank's are 3 s apart).</summary>
        private static uint StrikeWindow(Enemy e) => Math.Max((uint)WithinTicks, SimConstants.SecondsToTicks(e.Def.AttackRate + 0.5));

        /// <summary>
        /// Resting where it should: on its place on the raid's line with nobody to go for, waiting for room at
        /// its carrier, or within its reach of them or of the construct it goes for (it stands and strikes there).
        /// </summary>
        private static bool AtRest(SimWorld world, Enemy e)
        {
            if (e.Waiting) return true;
            // At what it breaks, it strikes at its own pace (a Tank's blows are 3 s apart).
            var goal = e.GoalTx >= 0 ? world.Constructs.At(e.GoalTx, e.GoalTz) : null;
            if (goal != null && world.Constructs.Distance(goal, e.X, e.Z, out _, out _) - goal.Def.Radius <= e.Def.AttackRange + e.Radius + 0.5f) return true;
            var target = world.GetPlayer(e.Target);
            if (target != null)
            {
                float tx = target.Move.X - e.X, tz = target.Move.Z - e.Z, reach = e.Def.AttackRange + e.Radius + 1f;
                return tx * tx + tz * tz <= reach * reach;
            }
            world.HoldSpot(e, out float hx, out float hz);
            return (hx - e.X) * (hx - e.X) + (hz - e.Z) * (hz - e.Z) <= 1.5f * 1.5f;
        }

        /// <summary>
        /// One layout: one or two carriers standing on the street round the Post Office, 10–80 Wooden and
        /// Stone Walls and Gates put up at random about them, and in a third of the layouts a ring of walls
        /// shut round a carrier (no way in but through one); then the raid of a random shift 2–5 comes at
        /// once, and every raider of it is watched for 45 s.
        /// </summary>
        private (int Raiders, List<string> Stalled, int Walls) Layout(int n)
        {
            var rng = new Random(1000 + n);
            int shift = 2 + rng.Next(4);
            var world = new SimWorld(Db, new SimSettings { Seed = (uint)(77 + n), Draft = false, StartingKits = false });
            var team = new List<Player>();
            int carriers = 1 + rng.Next(2);
            for (int i = 0; i < carriers; i++) team.Add(world.AddPlayer("carrier" + i)!);
            foreach (var p in team) world.SetPlaytest(p, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota | PlaytestFlags.RaidNow, shift);
            Assert.Equal(OpResult.Ok, Request(world, team[0], SimRequest.StartRun(0)));
            foreach (var p in team) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);

            var map = world.Map;
            int cx = (int)(map.PostOffice.SpawnPadX / SimConstants.TileSize), cz = (int)(map.PostOffice.SpawnPadZ / SimConstants.TileSize);
            var kinds = new[] { Db.Building("wall_wood"), Db.Building("wall_stone"), Db.Building("gate") };
            int walls = 0;
            bool Put(BuildingDef def, int tx, int tz, int rotation)
            {
                if (BuildRules.CheckPlacement(map, world.Constructs, def, tx, tz, rotation) != OpResult.Ok) return false;
                world.PutUp(def, tx, tz, rotation);
                walls++;
                return true;
            }

            // The carriers first, on open ground, so no wall goes up on them.
            foreach (var p in team)
            {
                for (int guard = 0; guard < 200; guard++)
                {
                    int tx = cx + rng.Next(-18, 19), tz = cz + rng.Next(-10, 11);
                    if (BuildRules.CheckPlacement(map, world.Constructs, kinds[0], tx, tz, 0) != OpResult.Ok) continue;
                    SimDriver.Teleport(p, BuildRules.TileCentre(tx), BuildRules.TileCentre(tz));
                    p.MaxHp = p.Hp = 1e9f;
                    break;
                }
            }
            var occupied = team.Select(p => ((int)(p.Move.X / SimConstants.TileSize), (int)(p.Move.Z / SimConstants.TileSize))).ToHashSet();
            if (rng.Next(3) == 0)
            {
                // Shut in: a square ring of walls two tiles out round the first carrier.
                var (px, pz) = occupied.First();
                for (int d = -2; d <= 2; d++)
                {
                    Put(kinds[rng.Next(2)], px + d, pz - 2, 0);
                    Put(kinds[rng.Next(2)], px + d, pz + 2, 0);
                    if (d > -2 && d < 2)
                    {
                        Put(kinds[rng.Next(2)], px - 2, pz + d, 1);
                        Put(kinds[rng.Next(2)], px + 2, pz + d, 1);
                    }
                }
            }
            int count = 10 + rng.Next(71);
            for (int i = 0, guard = 0; i < count && guard < count * 20; guard++)
            {
                int tx = cx + rng.Next(-24, 25), tz = cz + rng.Next(-14, 15);
                if (occupied.Contains((tx, tz))) continue;
                // Runs of wall as players build them, as often as lone pieces.
                int run = rng.Next(2) == 0 ? 1 : 2 + rng.Next(6);
                bool along = rng.Next(2) == 0;
                for (int k = 0; k < run; k++)
                {
                    int x = along ? tx + k : tx, z = along ? tz : tz + k;
                    if (occupied.Contains((x, z))) break;
                    if (Put(kinds[rng.Next(kinds.Length)], x, z, along ? 0 : 1)) i++;
                }
            }

            // Every raider of the raid, for 45 s from its start: no 2 s in which it neither went a metre
            // nor struck, unless it rested where it should.
            var watched = new Dictionary<EntityId, Watch>();
            var stalled = new List<string>();
            uint until = 0;
            for (int t = 0; t < 120 * Tps && (until == 0 || world.Tick < until); t++)
            {
                world.Step();
                if (until == 0 && world.RaidPulsesSpawned > 0) until = world.Tick + 45 * Tps;
                foreach (var e in world.Enemies)
                {
                    if (!watched.TryGetValue(e.Id, out var w)) watched[e.Id] = w = new Watch { Born = world.Tick, Since = world.Tick };
                    int slot = (int)(world.Tick % (WithinTicks + 1));
                    w.X[slot] = e.X;
                    w.Z[slot] = e.Z;
                    if (e.State == EnemyState.Windup) w.LastStruck = world.Tick;
                    if (AtRest(world, e)) w.Since = world.Tick;
                    if (w.Stalled || world.Tick - w.Born < WithinTicks || world.Tick - w.LastStruck <= StrikeWindow(e) || world.Tick - w.Since <= WithinTicks) continue;
                    float farthest = 0f;
                    for (int i = 0; i <= WithinTicks; i++)
                    {
                        float dx = w.X[i] - e.X, dz = w.Z[i] - e.Z;
                        farthest = MathF.Max(farthest, dx * dx + dz * dz);
                    }
                    if (farthest >= UnderWay * UnderWay) continue;
                    w.Stalled = true;
                    stalled.Add($"layout {n} (shift {shift}, {walls} walls): {e.Def.Id} {e.State} at ({e.X:0.0}, {e.Z:0.0}) went under {UnderWay} m in 2 s and struck nothing, path {e.Path.Count}, target {e.Target}: {Diag(world, e)}");
                }
            }
            return (watched.Count, stalled, walls);
        }

        /// <summary>What a stalled raider faced: its goal, its next waypoint, the props and walls about it.</summary>
        private static string Diag(SimWorld world, Enemy e)
        {
            var g = e.GoalTx >= 0 ? world.Constructs.At(e.GoalTx, e.GoalTz) : null;
            string goal = g == null ? "no construct" : $"{g.Def.Id} {world.Constructs.Distance(g, e.X, e.Z, out _, out _) - g.Def.Radius:0.00} m off";
            string next = e.Waypoint < e.Path.Count ? $"({e.Path[e.Waypoint].X:0.0}, {e.Path[e.Waypoint].Z:0.0})" : "none";
            var props = string.Join(" ", world.Map.Props.Where(pr => pr.Distance(e.X, e.Z, out _, out _) < 3f).Select(pr => $"r{pr.Radius:0.00} {pr.Distance(e.X, e.Z, out _, out _):0.00} m off"));
            var near = string.Join(" ", world.Constructs.All.Where(c => MathF.Abs(c.CentreX - e.X) < 4f && MathF.Abs(c.CentreZ - e.Z) < 4f).Select(c => $"{c.Def.Id}@{c.Tx},{c.Tz}"));
            return $"goal {goal}, next waypoint {next}, props [{props}], built [{near}], strikes again at {e.NextAttackTick} (tick {world.Tick})";
        }

        [Fact]
        public void TwoHundredRandomWallLayouts_EveryRaiderFindsAWayOrAWallToBreak_WithinTwoSeconds()
        {
            int raiders = 0, walls = 0;
            var stalled = new List<string>();
            for (int n = 0; n < Layouts; n++)
            {
                var (r, s, w) = Layout(n);
                raiders += r;
                walls += w;
                stalled.AddRange(s);
            }
            _output.WriteLine($"{Layouts} layouts, {walls} walls, {raiders} raiders watched for 45 s: {stalled.Count} stalled");
            foreach (var line in stalled.Take(200)) _output.WriteLine(line);
            Assert.True(raiders >= Layouts * 3, $"only {raiders} raiders came");
            Assert.Empty(stalled);
        }
    }
}
