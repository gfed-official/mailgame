using System;
using System.Linq;
using System.Text;
using PerformativeMail.Sim.Automation;
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
    /// The debug world (content/maps/debug_world.json, <c>--debug-world</c>; design choice 119):
    /// every construct up and running as the run starts, the mail going through each station, and
    /// the debug toggles — no damage, a full wallet, no materials, the clock standing, no raid.
    /// </summary>
    public class DebugWorldTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private readonly ITestOutputHelper _out;

        public DebugWorldTests(ITestOutputHelper output) => _out = output;

        /// <summary>A quick-start server on the debug world, as <c>--debug-world</c> hosts it: in Delivery from the first tick.</summary>
        private static (SimWorld World, Player Host) Start(bool raid = false)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, MapId = "debug_world", QuickStart = true, StartingKits = true });
            var host = world.AddPlayer("host")!;
            if (raid) world.SetPlaytest(host, PlaytestFlags.RaidNow);
            SimDriver.StepTicks(world, 2);
            return (world, host);
        }

        /// <summary>The map in letters with what is built on it, for a layout that does not work.</summary>
        private static string Dump(SimWorld world)
        {
            var map = world.Map;
            var sb = new StringBuilder();
            for (int z = 0; z < map.Rows; z++)
            {
                sb.Append($"{z,2} ");
                for (int x = 0; x < map.Cols; x++)
                {
                    var c = world.Constructs.At(x, z);
                    char ch = c != null ? c.Def.Id switch
                    {
                        "belt_mk1" or "belt_mk2" or "belt_mk1_elevated" => "><v^"[c.Facing switch { 0 => 0, 2 => 1, 1 => 2, _ => 3 }],
                        "belt_mk1_ramp" => 'R',
                        "splitter" => 'S', "merger" => 'M', "address_sorter_mk1" => 'A',
                        "pipe" or "pipe_underground" => '|', "pipe_junction" => '+', "pipe_inlet" => 'I', "pipe_outlet" => 'O',
                        "chest" => 'c', "depot" => 'D', "inserter" or "inserter_long" => 'i',
                        "vehicle_depot" => 'V', "small_port" => 'P', "oil_pump" => 'p',
                        "turret" => 'T',
                        _ => '?',
                    } : map.GetTile(x, z) switch
                    {
                        TileType.Street => '=', TileType.Building => '#', TileType.Lot => ',', TileType.Yard => ':',
                        TileType.Water or TileType.DeepWater => '~', _ => '.',
                    };
                    foreach (var d in map.Destinations)
                        if ((int)(d.MailboxX / SimConstants.TileSize) == x && (int)(d.MailboxZ / SimConstants.TileSize) == z) ch = d.IsDock ? 'K' : 'B';
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        [Fact]
        public void TheDebugWorldIsAMapOfItsOwn_NotOfferedInTheLobby_WithItsShowcaseAndToggles()
        {
            var map = Db.Map("debug_world");
            Assert.True(map.Bare);
            Assert.Empty(map.Props);
            Assert.DoesNotContain("debug_world", SimWorld.WorldChoices);
            var debug = map.Debug!;
            Assert.True(debug.GodMode && debug.Unlimited && debug.NoTimer && !debug.Raids);
            Assert.Equal(11, debug.Stations.Count);
            Assert.Equal(("barbarian", 57, 2), (debug.Targets.Single().Enemy, debug.Targets.Single().X, debug.Targets.Single().Z));
            Assert.All(debug.Stations, s => Assert.NotEmpty(s.Proofs));
            Assert.Null(Db.Map("test_street").Debug);
        }

        [Fact]
        public void EveryConstructStandsAndRuns_AsTheRunStarts_InDelivery()
        {
            var (world, _) = Start();
            _out.WriteLine(Dump(world));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(5, world.Run.Shift);
            var debug = world.Map.Debug!;
            Assert.Equal(debug.Constructs.Count, world.Constructs.All.Count);
            foreach (var id in debug.Constructs.Select(c => c.Building).Distinct())
                Assert.Contains(world.Constructs.All, c => c.Def.Id == id);
            Assert.Equal(2, world.Drivers.Count(d => d.Hired));
            Assert.Contains(world.Vehicles, v => v.Def.Id == "mail_truck");
            Assert.Contains(world.Vehicles, v => v.Def.Id == "motorboat");
            Assert.True(world.ClockStopped);
            Assert.True(world.OwnsBlueprint("bp_pipes") && world.OwnsBlueprint("bp_oil") && world.OwnsBlueprint("bp_sorting"));
        }

        [Fact]
        public void MailGoesThroughEveryStation_WithinTwoMinutes()
        {
            var (world, _) = Start();
            var probe = world.Showcase!;
            for (int i = 0; i < 120 * Tps && !probe.AllDone; i++)
            {
                world.Step();
                world.Events.Clear();
            }
            _out.WriteLine(Dump(world));
            _out.WriteLine($"done at {(world.Tick / (float)Tps):0.0} s");
            for (int st = 0; st < probe.Stations.Count; st++)
                _out.WriteLine($"{probe.Stations[st].Name}: {string.Join(", ", probe.Stations[st].Proofs.Select((p, k) => $"{p} x{probe.Count(st, k)}"))}");
            Assert.True(probe.AllDone, $"not yet: {probe.Missing()}");
            Assert.Equal(0, world.Economy.ShiftMisdeliveries);
        }

        [Fact]
        public void TheMailKeepsFlowing_NothingJamsInFiveMinutes()
        {
            var (world, _) = Start();
            var probe = world.Showcase!;
            SimDriver.StepTicks(world, 240 * Tps);
            int Total(int st) => Enumerable.Range(0, probe.Stations[st].Proofs.Count).Sum(k => probe.Count(st, k));
            var before = Enumerable.Range(0, probe.Stations.Count).Select(Total).ToArray();
            SimDriver.StepTicks(world, 60 * Tps);
            for (int st = 0; st < probe.Stations.Count; st++)
                Assert.True(Total(st) > before[st], $"{probe.Stations[st].Name} stood still in the fifth minute\n{Dump(world)}");
            // Mail does not pile up anywhere: the Intake keeps up with the feed.
            Assert.Equal(0, world.Spawner.BacklogCount);
            Assert.True(world.Intake.EntryCount < 12, $"{world.Intake.EntryCount} stacks wait in the Intake");
            Assert.Equal(0, world.Economy.ShiftMisdeliveries);
        }

        [Fact]
        public void TheToggles_NoDamage_AFullWallet_NoMaterials_TheClockStands_NoRaid()
        {
            var (world, host) = Start();
            world.DamagePlayer(host, 500f);
            Assert.Equal(host.MaxHp, host.Hp);
            Assert.False(host.Dead);
            Assert.Equal(PlaytestMask.MaxWallet, world.Economy.Wallet);
            uint end = world.Run.PhaseEndTick;
            SimDriver.StepTicks(world, 30 * Tps);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.Run.PhaseEndTick > end);
            Assert.Equal(Combat.RaidState.None, world.Raid);
            // Only the turret range's practice raider, within the turret's reach.
            var turret = world.Constructs.At(54, 10)!;
            Assert.All(world.Enemies, e => Assert.InRange(MathF.Sqrt((e.X - turret.CentreX) * (e.X - turret.CentreX) + (e.Z - turret.CentreZ) * (e.Z - turret.CentreZ)), 0f, 18f));

            // A wall out of empty pockets, by the spawn pad.
            foreach (var c in new[] { host.Hotbar, host.Inventory })
                foreach (uint id in c.Entries.Select(e => e.EntryId).ToList()) c.Remove(id);
            var wall = Db.Building("wall_wood");
            SimDriver.Teleport(host, BuildRules.TileCentre(10), BuildRules.TileCentre(17));
            SimDriver.StepTicks(world, 12);
            world.Events.Clear();
            world.Submit(host.Id, SimRequest.PlaceConstruct(77, wall.Index, 10, 15, 0));
            world.Step();
            Assert.Equal(OpResult.Ok, world.Events.OfType<RequestResultEvent>().Single(e => e.RequestId == 77).Result);
            Assert.Equal(wall, world.Constructs.At(10, 15)!.Def);
        }

        [Fact]
        public void TheTurretRange_ARaiderStandsUp_IsShotDown_AndAnotherComes()
        {
            var (world, _) = Start();
            var turret = world.Constructs.At(54, 10)!;
            Assert.True(turret.Def.IsTurret);
            int killed = world.EnemiesKilledTotal;
            SimDriver.StepTicks(world, 20 * Tps);
            Assert.True(world.EnemiesKilledTotal - killed >= 2, $"{world.EnemiesKilledTotal - killed} practice raiders fell in 20 s");
            Assert.True(world.TurretShots >= 10);
            // Nothing of the showcase was broken by them.
            Assert.All(world.Constructs.All, c => Assert.Equal(c.Def.Hp, c.Hp));
        }

        [Fact]
        public void RaidAsksForTheRaid_OnTheDebugWorldToo()
        {
            var (world, _) = Start(raid: true);
            SimDriver.StepTicks(world, (int)((PlaytestMask.RaidNowSeconds + 3) * Tps));
            Assert.NotEqual(Combat.RaidState.None, world.Raid);
        }
    }
}
