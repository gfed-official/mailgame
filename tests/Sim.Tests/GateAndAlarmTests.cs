using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
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
    /// The two defences doc 05 §4 builds without a blueprint: the Gate (500 HP, 4 Logs and an
    /// Iron Ingot; it opens for the carriers, the raiders treat it as a wall) and the Alarm Post
    /// (100 HP, 2 Logs and an Iron Ingot; 15 s more warning for every post in range of the
    /// raiders' way in, up to 45 s, and the raiders within 40 m of it marked).
    /// </summary>
    public class GateAndAlarmTests
    {
        private const int Tps = 30;
        /// <summary>Open ground south of the street, between the mailboxes of numbers 4 and 6.</summary>
        private const int Tx = 22, Tz = 15;

        private uint _nextRequest = 1;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Gate => Db.Building("gate");
        private static BuildingDef Alarm => Db.Building("alarm_post");
        private static BuildingDef Wall => Db.Building("wall_wood");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        /// <summary>A run of a shift in its Prep, Logs and Iron Ingots in the host's pockets.</summary>
        private (SimWorld World, Player Host) InPrep(int shift = 1, bool raids = false)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = raids, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 6)));
            return (world, host);
        }

        /// <summary>Stands the carrier beside a tile (not on it) and builds there; with no building, takes down what is there.</summary>
        private OpResult PlaceBeside(SimWorld world, Player p, BuildingDef? def, int tx, int tz, int rotation = 0)
        {
            foreach (var (dx, dz) in new[] { (0f, 2.6f), (0f, -2.6f), (2.6f, 0f), (-2.6f, 0f) })
            {
                int sx = (int)MathF.Floor((Centre(tx) + dx) / SimConstants.TileSize), sz = (int)MathF.Floor((Centre(tz) + dz) / SimConstants.TileSize);
                if (world.Map.IsNavBlocked(sx, sz) || world.Constructs.At(sx, sz) != null) continue;
                SimDriver.Teleport(p, Centre(tx) + dx, Centre(tz) + dz);
                SimDriver.StepTicks(world, 15);
                return def == null
                    ? Request(world, p, SimRequest.RemoveConstruct(0, tx, tz))
                    : Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));
            }
            throw new InvalidOperationException($"nowhere to stand beside ({tx}, {tz})");
        }

        private static int Units(Player p, string item)
        {
            ushort index = Db.Item(item).Index;
            return p.Hotbar.Entries.Concat(p.Inventory.Entries).Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        /// <summary>Open ground tiles, nearest the raiders' way in first, that an Alarm Post there would count from (or not).</summary>
        private static (int Tx, int Tz)[] AlarmTiles(SimWorld world, bool inRange, int count)
        {
            var map = world.Map;
            RaidRules.Gate(map, out float gx, out float gz);
            var tiles = new System.Collections.Generic.List<(int, int, float)>();
            for (int tz = 1; tz < map.Rows - 1; tz++)
                for (int tx = 1; tx < map.Cols - 1; tx++)
                {
                    if (BuildRules.CheckTile(map, world.Constructs, tx, tz) != OpResult.Ok) continue;
                    if (map.IsNavBlocked(tx, tz + 1) && map.IsNavBlocked(tx, tz - 1) && map.IsNavBlocked(tx + 1, tz) && map.IsNavBlocked(tx - 1, tz)) continue;
                    if (RaidRules.AlarmInRange(map, Alarm, tx, tz, 1) != inRange) continue;
                    float dx = Centre(tx) - gx, dz = Centre(tz) - gz;
                    tiles.Add((tx, tz, dx * dx + dz * dz));
                }
            // Two apart, so one does not stand in the way of building the next.
            var picked = new System.Collections.Generic.List<(int, int)>();
            foreach (var t in tiles.OrderBy(t => t.Item3))
            {
                if (picked.Any(p => Math.Abs(p.Item1 - t.Item1) < 3 && Math.Abs(p.Item2 - t.Item2) < 3)) continue;
                picked.Add((t.Item1, t.Item2));
                if (picked.Count == count) break;
            }
            Assert.Equal(count, picked.Count);
            return picked.ToArray();
        }

        [Fact]
        public void TheContentHasTheGateAndTheAlarmPost_WithoutABlueprint()
        {
            Assert.Equal((500, 2, BuildingBehaviour.Gate, true, true), (Gate.Hp, Gate.Rotations, Gate.Behaviour, Gate.Joins, Gate.Solid));
            Assert.Equal(new[] { ("log", 4), ("iron_ingot", 1) }, Gate.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)).ToArray());
            Assert.Null(Gate.Recipe.Blueprint);
            Assert.Equal(1, Gate.Recipe.UnlockShift);

            Assert.Equal((100, BuildingBehaviour.Alarm, false, true, 15f, 40f), (Alarm.Hp, Alarm.Behaviour, Alarm.Joins, Alarm.Solid, Alarm.WarningBonusSeconds, Alarm.RevealRadius));
            Assert.Equal(new[] { ("log", 2), ("iron_ingot", 1) }, Alarm.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)).ToArray());
            Assert.Null(Alarm.Recipe.Blueprint);

            // Doc 11 §10: 15 s, +15 per post in range, 45 s at most.
            Assert.Equal((15.0, 45.0), (Db.Waves.WarningSeconds, Db.Waves.MaxWarningSeconds));
            Assert.Equal(45.0, RaidRules.WarningSeconds(Db.Waves, 15, 60));
            Assert.Equal(35.0, RaidRules.WarningSeconds(Db.Waves, 35, 0));
            Assert.Equal(45.0, RaidRules.WarningSeconds(Db.Waves, 35, 15));
        }

        [Fact]
        public void BadAlarmsAreContentErrors()
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in System.IO.Directory.GetFiles(root, "*.json", System.IO.SearchOption.AllDirectories))
                source.Files[System.IO.Path.GetRelativePath(root, file).Replace(System.IO.Path.DirectorySeparatorChar, '/')] = System.IO.File.ReadAllText(file);
            string real = source.Files["buildings/buildings.json"];
            string Broken(string from, string to)
            {
                Assert.Contains(from, real);
                source.Files["buildings/buildings.json"] = real.Replace(from, to);
                ContentDb.TryLoad(source, out var errors);
                return string.Join("\n", errors);
            }
            Assert.Contains("'alarm_post': an alarm needs params.warningBonusSeconds > 0 and params.revealRadius > 0", Broken("\"revealRadius\": 40", "\"revealRadius\": 0"));
            Assert.Contains("'gate': only an alarm warns and reveals", Broken("\"params\": { \"radius\": 0.2, \"height\": 2.4 },\n    \"recipe\": \"recipe_gate\"", "\"params\": { \"radius\": 0.2, \"height\": 2.4, \"revealRadius\": 5 },\n    \"recipe\": \"recipe_gate\""));
        }

        [Fact]
        public void TheGateOpensForCarriers_AndIsAWallToTheRaiders()
        {
            var (world, host) = InPrep();
            // A wall, the gate and a wall across the way, turned along X.
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Wall, Tx - 1, Tz));
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Gate, Tx, Tz));
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Wall, Tx + 1, Tz));
            var gate = world.Constructs.At(Tx, Tz)!;
            Assert.Equal(500, gate.Hp);
            Assert.Equal((20 - 3 - 4 - 3, 6 - 1), (Units(host, "log"), Units(host, "iron_ingot")));
            // It closes the line with the walls on both sides.
            Assert.Equal(ConstructField.ArmEast | ConstructField.ArmWest, world.Constructs.Arms(gate));

            // A carrier walks straight through it, north to south.
            SimDriver.Teleport(host, Centre(Tx), Centre(Tz) - 1.6f);
            SimDriver.StepTicks(world, 2);
            Assert.True(SimDriver.WalkTo(world, host, Centre(Tx), Centre(Tz) + 1.6f, maxTicks: 120));
            float x = Centre(Tx), z = Centre(Tz) + 0.1f;
            PlayerMovement.ResolveCollisions(ref x, ref z, 0f, world.Map, 0.4f, world.Constructs, carrier: true);
            Assert.Equal(Centre(Tz) + 0.1f, z);

            // A raider is pushed out of it, its tile paths keep off it, and up against it it strikes it.
            x = Centre(Tx); z = Centre(Tz) + 0.1f;
            PlayerMovement.ResolveCollisions(ref x, ref z, 0f, world.Map, 0.4f, world.Constructs, carrier: false);
            Assert.True(z > Centre(Tz) + 0.55f, $"a raider stands {z - Centre(Tz):0.00} m from the gate's line");
            Assert.True(world.Constructs.Blocks(Tx, Tz));
            Assert.False(world.Constructs.Blocks(Tx, Tz, carrier: true));
            // A carrier's tile path goes through it; a raider's goes round the line.
            var path = new System.Collections.Generic.List<(float X, float Z)>();
            Assert.True(TilePathfinder.Find(world.Map, Centre(Tx), Centre(Tz) - 3f, Centre(Tx), Centre(Tz) + 3f, path, constructs: world.Constructs, carrier: true));
            Assert.Contains(path, w => (int)MathF.Floor(w.X / SimConstants.TileSize) == Tx && (int)MathF.Floor(w.Z / SimConstants.TileSize) == Tz);
            Assert.True(TilePathfinder.Find(world.Map, Centre(Tx), Centre(Tz) - 3f, Centre(Tx), Centre(Tz) + 3f, path, constructs: world.Constructs));
            Assert.DoesNotContain(path, w => (int)MathF.Floor(w.Z / SimConstants.TileSize) == Tz && Math.Abs((int)MathF.Floor(w.X / SimConstants.TileSize) - Tx) <= 1);
            Assert.Same(gate, world.Constructs.Ahead(Centre(Tx), Centre(Tz) + 0.45f, 0f, -1f, 0.3f));
            Assert.Same(gate, world.Constructs.Between(Centre(Tx), Centre(Tz) - 1f, Centre(Tx), Centre(Tz) + 1f));
            world.DamageConstruct(gate, 499);
            Assert.Equal(1, gate.Hp);
            world.DamageConstruct(gate, 10);
            Assert.Null(world.Constructs.At(Tx, Tz));
        }

        [Fact]
        public void AlarmPostsInRangeRingTheRaidInEarlier_UpTo45Seconds()
        {
            var (world, host) = InPrep(shift: 2, raids: true);
            var near = AlarmTiles(world, inRange: true, 4);
            var far = AlarmTiles(world, inRange: false, 1)[0];
            // One out of range, built in Prep: it changes nothing.
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Alarm, far.Tx, far.Tz));
            Assert.Equal(0.0, RaidRules.AlarmBonusSeconds(world.Map, world.Constructs, world.Run.Shift));
            Assert.Equal(15.0, world.RaidWarningSeconds());
            // One in range, built in Prep: the shift's raid is scheduled with it.
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Alarm, near[0].Tx, near[0].Tz));
            Assert.Equal(30.0, world.RaidWarningSeconds());
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            uint start = world.RaidStartTick;
            Assert.NotEqual(0u, start);
            Assert.Equal(start - 30 * Tps, world.RaidWarnTick);

            // Two more in Delivery, before the warning: earlier still; a fourth is past the cap.
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Alarm, near[1].Tx, near[1].Tz));
            Assert.Equal(start - 45 * Tps, world.RaidWarnTick);
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Alarm, near[2].Tx, near[2].Tz));
            Assert.Equal(45.0, world.RaidWarningSeconds());
            Assert.Equal(start - 45 * Tps, world.RaidWarnTick);
            // Taken down again: back to what the rest give.
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, null, near[2].Tx, near[2].Tz));
            Assert.Equal(start - 45 * Tps, world.RaidWarnTick);
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, null, near[1].Tx, near[1].Tz));
            Assert.Equal(start - 30 * Tps, world.RaidWarnTick);

            // The warning comes when it says, and from then on stays where it is.
            while (world.Raid == RaidState.None && world.Tick < start) world.Step();
            Assert.Equal(RaidState.Warning, world.Raid);
            Assert.Equal(start - 30 * Tps, world.Tick);
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Alarm, near[3].Tx, near[3].Tz));
            Assert.Equal(start - 30 * Tps, world.RaidWarnTick);
            Assert.Equal(start, world.RaidStartTick);
        }

        [Fact]
        public void TheRaidersNearAnAlarmPostAreRevealed()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, PlaceBeside(world, host, Alarm, Tx, Tz));
            var post = world.Constructs.At(Tx, Tz)!;
            Assert.True(RaidRules.Revealed(world.Constructs, post.X + 39.5f, post.Z));
            Assert.False(RaidRules.Revealed(world.Constructs, post.X + 40.5f, post.Z));
            // It stands in the way like a chest does: a tile path keeps off it.
            Assert.True(world.Constructs.Blocks(Tx, Tz));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, Tx, Tz)));
            Assert.False(RaidRules.Revealed(world.Constructs, post.X, post.Z));
        }
    }
}
