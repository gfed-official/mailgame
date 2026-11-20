using System;
using System.IO;
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
    /// Resource nodes and harvesting (doc 02 §3.9): the map's trees, bushes and boulders give Logs,
    /// Fiber, Berries, Stone and Iron Ore to a carrier who swings at them in Prep or Delivery.
    /// </summary>
    public class HarvestTests
    {
        private const int Tps = 30;
        /// <summary>More than a swing takes (balance harvestSwingSeconds, the tools' swingTime).</summary>
        private const int SwingTicks = 20;

        private uint _nextRequest = 1;
        private uint _nextMailId = 970000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld(uint seed = 7) =>
            new SimWorld(Db, new SimSettings { Seed = seed, Raids = false, Draft = false });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds)
        {
            for (int i = 0; i < maxSeconds * Tps && !done(); i++) world.Step();
            Assert.True(done(), $"still in {world.Run.Phase} of shift {world.Run.Shift} after {maxSeconds} s");
        }

        /// <summary>A run in its first Prep.</summary>
        private (SimWorld World, Player Host) InPrep(uint seed = 7)
        {
            var world = NewWorld(seed);
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            return (world, host);
        }

        /// <summary>The <paramref name="nth"/> prop of the map that is this node.</summary>
        private static int Node(SimWorld world, string id, int nth = 0)
        {
            for (int i = 0; i < world.Nodes.Count; i++)
                if (world.Nodes.Def(i)?.Id == id && nth-- == 0) return i;
            throw new InvalidOperationException($"the map has no '{id}'");
        }

        private static void Beside(SimWorld world, Player p, int node)
        {
            var prop = world.Map.Props[node];
            SimDriver.Teleport(p, prop.X + prop.Radius + 0.9f, prop.Z);
        }

        /// <summary>One swing at the node, after waiting out the one before.</summary>
        private OpResult Swing(SimWorld world, Player p, int node, int slot = 0)
        {
            SimDriver.StepTicks(world, SwingTicks);
            return Request(world, p, SimRequest.Harvest(0, node, slot));
        }

        /// <summary>Puts one of an item on the hotbar and says which slot it is in.</summary>
        private static int Hold(SimWorld world, Player p, string item)
        {
            Assert.True(world.Give(p.Hotbar, ItemStack.OfItem(Db.Item(item), 1)));
            ushort index = Db.Item(item).Index;
            return p.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).X;
        }

        private static int Count(Player p, string item)
        {
            ushort index = Db.Item(item).Index;
            return p.Hotbar.Entries.Concat(p.Inventory.Entries).Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        /// <summary>Plays the shift at hand on quota into its Payday.</summary>
        private void IntoPayday(SimWorld world, Player carrier)
        {
            if (world.Run.Phase == RunPhase.Prep) Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(carrier.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(carrier, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            var entry = carrier.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.Ok, Request(world, carrier, SimRequest.Deliver(0, carrier.Hotbar.Id, entry.EntryId, home.Id)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 310);
        }

        /// <summary>The real content with these files in place of its own.</summary>
        private static MemoryContentSource Source(string? nodes = null, string? tools = null)
        {
            string root = DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!;
            var source = new MemoryContentSource();
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                source.Files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllText(file);
            if (nodes != null) source.Files["nodes/nodes.json"] = nodes;
            if (tools != null) source.Files["items/tools.json"] = tools;
            return source;
        }

        // --- content ----------------------------------------------------------------------------

        [Fact]
        public void TheNodesAreTheMapsTreesBushesAndBoulders()
        {
            // Doc 02 §3.9's table.
            Assert.Equal(new[] { "tree", "fiber_bush", "berry_bush", "boulder", "ore_vein", "sand_pile" }, Db.Nodes.Select(n => n.Id).ToArray());
            var tree = Db.Nodes[0];
            Assert.Equal(("log", 2, 5, false, NodeTool.Optional, 0.5f), (tree.Item.Id, tree.Yield, tree.Hits, tree.Regrows, tree.Tool, tree.HandYield));
            Assert.Equal(("fiber", 3, 3, true, NodeTool.None), (Db.Nodes[1].Item.Id, Db.Nodes[1].Yield, Db.Nodes[1].Hits, Db.Nodes[1].Regrows, Db.Nodes[1].Tool));
            Assert.Equal(("berries", 2, 2, true, NodeTool.None), (Db.Nodes[2].Item.Id, Db.Nodes[2].Yield, Db.Nodes[2].Hits, Db.Nodes[2].Regrows, Db.Nodes[2].Tool));
            Assert.Equal(("stone", 3, 6, false, NodeTool.Required), (Db.Nodes[3].Item.Id, Db.Nodes[3].Yield, Db.Nodes[3].Hits, Db.Nodes[3].Regrows, Db.Nodes[3].Tool));
            Assert.Equal(("iron_ore", 2, 8, false, NodeTool.Required), (Db.Nodes[4].Item.Id, Db.Nodes[4].Yield, Db.Nodes[4].Hits, Db.Nodes[4].Regrows, Db.Nodes[4].Tool));
            Assert.Equal(("sand", 4, 4, true, NodeTool.Required), (Db.Nodes[5].Item.Id, Db.Nodes[5].Yield, Db.Nodes[5].Hits, Db.Nodes[5].Regrows, Db.Nodes[5].Tool));

            // Doc 08 §2.1, doc 10 §3: the tools, and their cards on the counter.
            var axe = Db.Item("axe");
            Assert.Equal(("tool", 1, 0.6f), (axe.Category, axe.MaxStack, axe.SwingSeconds));
            Assert.True(axe.HarvestsResource("wood") && !axe.HarvestsResource("stone"));
            var pickaxe = Db.Item("pickaxe");
            Assert.True(pickaxe.HarvestsResource("stone") && pickaxe.HarvestsResource("ore") && !pickaxe.HarvestsResource("wood"));
            Assert.Equal((80, "axe", 1, false), (Db.ShopItem("axe").Price, Db.ShopItem("axe").Item!.Id, Db.ShopItem("axe").Count, Db.ShopItem("axe").Special));
            Assert.Equal((100, "pickaxe"), (Db.ShopItem("pickaxe").Price, Db.ShopItem("pickaxe").Item!.Id));
            var shovel = Db.Item("shovel");
            Assert.True(shovel.HarvestsResource("sand") && !shovel.HarvestsResource("stone"));
            Assert.Equal((90, "shovel", false), (Db.ShopItem("shovel").Price, Db.ShopItem("shovel").Item!.Id, Db.ShopItem("shovel").Special));
            Assert.Empty(Db.Item("log").Harvests);

            // Every tree, bush, boulder and sand pile of the map is a node, and nothing else is; all six kinds grow on it.
            var world = NewWorld();
            var map = world.Map;
            Assert.Equal(map.Props.Count, world.Nodes.Count);
            for (int i = 0; i < map.Props.Count; i++)
            {
                var kind = map.Props[i].Kind;
                var def = world.Nodes.Def(i);
                Assert.Equal(kind == PropKind.Tree || kind == PropKind.Bush || kind == PropKind.Boulder || kind == PropKind.SandPile, def != null);
                if (def == null) continue;
                Assert.Equal(kind, def.Prop);
                Assert.Equal(def.Hits, world.Nodes.HitsLeft(i));
                Assert.False(world.Nodes.Depleted(i) || world.Nodes.Touched(i));
            }
            Assert.Equal(map.Props.Count(p => p.Kind == PropKind.Tree || p.Kind == PropKind.Bush || p.Kind == PropKind.Boulder || p.Kind == PropKind.SandPile), world.Nodes.NodeCount);
            foreach (var node in Db.Nodes) Assert.True(Node(world, node.Id) >= 0);
            Assert.Null(world.Nodes.Def(-1));
            Assert.Null(world.Nodes.Def(map.Props.Count));
        }

        [Fact]
        public void BadNodesAndToolsAreContentErrors()
        {
            string real = File.ReadAllText(Path.Combine(DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!, "nodes", "nodes.json"));
            Assert.Equal(Db.ContentHash, ContentDb.Load(Source(real)).ContentHash);
            Assert.NotEqual(Db.ContentHash, ContentDb.Load(Source(real.Replace("\"hits\": 5", "\"hits\": 4"))).ContentHash);

            string Broken(string from, string to)
            {
                Assert.Contains(from, real);
                ContentDb.TryLoad(Source(real.Replace(from, to)), out var errors);
                return string.Join("\n", errors);
            }

            Assert.Contains("'tree': gives unknown item 'timber'", Broken("\"item\": \"log\"", "\"item\": \"timber\""));
            Assert.Contains("'tree': prop must be 'tree', 'bush', 'boulder' or 'sand_pile', not 'lamp'", Broken("\"prop\": \"tree\"", "\"prop\": \"lamp\""));
            Assert.Contains("'tree': tool must be 'none', 'optional' or 'required', not 'maybe'", Broken("\"tool\": \"optional\"", "\"tool\": \"maybe\""));
            Assert.Contains("'tree': handYield is for a node whose tool is 'optional'", Broken("\"tool\": \"optional\"", "\"tool\": \"none\""));
            Assert.Contains("'tree': hits must be in 1..255", Broken("\"hits\": 5", "\"hits\": 0"));
            Assert.Contains("'fiber_bush': yield must be in 1..", Broken("\"yield\": 3, \"hits\": 3", "\"yield\": 0, \"hits\": 3"));
            Assert.Contains("'berry_bush': bush variant 1 is 'fiber_bush' already", Broken("\"variants\": [2],\n    \"item\": \"berries\"", "\"variants\": [1, 2],\n    \"item\": \"berries\""));
            Assert.Contains("'ore_vein': variants must be in 0..2", Broken("\"variants\": [2],\n    \"item\": \"iron_ore\"", "\"variants\": [3],\n    \"item\": \"iron_ore\""));
            Assert.Contains("'boulder': no tool harvests 'granite'", Broken("\"resource\": \"stone\"", "\"resource\": \"granite\""));
            Assert.Contains("'pickaxe' harvests 'stone', which no node gives", Broken("\"resource\": \"stone\"", "\"resource\": \"granite\""));
            Assert.Contains("duplicate id 'tree'", Broken("\"id\": \"ore_vein\"", "\"id\": \"tree\""));

            string tools = File.ReadAllText(Path.Combine(DirectoryContentSource.FindContentRoot(AppContext.BaseDirectory)!, "items", "tools.json"));
            ContentDb.TryLoad(Source(tools: tools.Replace("\"harvests\": [\"wood\"]", "\"harvests\": []")), out var noHarvest);
            Assert.Contains(noHarvest, e => e.Contains("'axe': a tool harvests something"));
            ContentDb.TryLoad(Source(tools: tools.Replace("\"yieldMultiplier\": 1.0, \"swingTime\": 0.6 } },", "\"yieldMultiplier\": 1.0, \"swingTime\": 0 } },")), out var noSwing);
            Assert.Contains(noSwing, e => e.Contains("'axe': tool.yieldMultiplier and tool.swingTime must be > 0"));
            ContentDb.TryLoad(Source(tools: tools.Replace("\"id\": \"axe\", \"name\": \"Axe\", \"category\": \"tool\"", "\"id\": \"axe\", \"name\": \"Axe\", \"category\": \"material\"")), out var notATool);
            Assert.Contains(notATool, e => e.Contains("'axe': only an item of category 'tool' has a 'tool' block"));
        }

        // --- swinging ---------------------------------------------------------------------------

        [Fact]
        public void ABushGivesItsFiberToBareHands_AndIsBareAfterThreeHits()
        {
            var (world, host) = InPrep();
            int bush = Node(world, "fiber_bush");
            Beside(world, host, bush);

            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            var hit = world.Events.OfType<NodeHarvestedEvent>().Single();
            Assert.Equal((host.Id, (ushort)bush, (byte)2, Db.Item("fiber").Index, (byte)3), (hit.Player, hit.Node, hit.HitsLeft, hit.Item, hit.Count));
            Assert.Equal(EntityId.None, hit.Audience);
            // Materials go to the inventory.
            Assert.Equal(3, host.Inventory.Entries.Single().Stack.Count);
            Assert.Empty(host.Hotbar.Entries);
            Assert.True(world.Nodes.Touched(bush));
            Assert.False(world.Nodes.Depleted(bush));
            int version = world.Nodes.Version;

            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Assert.Equal(version, world.Nodes.Version);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Assert.Equal(9, Count(host, "fiber"));
            Assert.True(world.Nodes.Depleted(bush));
            Assert.Equal(version + 1, world.Nodes.Version);

            Assert.Equal(OpResult.Depleted, Swing(world, host, bush));
            Assert.Equal(9, Count(host, "fiber"));
            Assert.Equal(3, world.Events.OfType<NodeHarvestedEvent>().Count());

            // The bush beside it is another node.
            int other = Node(world, "fiber_bush", 1);
            Assert.Equal(Db.Nodes[1].Hits, world.Nodes.HitsLeft(other));
        }

        [Fact]
        public void BerriesGoToTheHotbar()
        {
            var (world, host) = InPrep();
            int bush = Node(world, "berry_bush");
            Beside(world, host, bush);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Assert.Equal(2, host.Hotbar.Entries.Single().Stack.Count);
            Assert.Empty(host.Inventory.Entries);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Assert.Equal(4, Count(host, "berries"));
            Assert.Equal(OpResult.Depleted, Swing(world, host, bush));
        }

        [Fact]
        public void ATreeGivesHalfToBareHands_AndAllOfItToAnAxe()
        {
            var (world, host) = InPrep();
            int tree = Node(world, "tree");
            Beside(world, host, tree);

            // Doc 02 §3.9: by hand 50 %.
            Assert.Equal(OpResult.Ok, Swing(world, host, tree));
            Assert.Equal(1, Count(host, "log"));
            // A Pickaxe is no better than a hand at a tree.
            int pickaxe = Hold(world, host, "pickaxe");
            Assert.Equal(OpResult.Ok, Swing(world, host, tree, pickaxe));
            Assert.Equal(2, Count(host, "log"));

            int axe = Hold(world, host, "axe");
            Assert.NotEqual(pickaxe, axe);
            Assert.Equal(OpResult.Ok, Swing(world, host, tree, axe));
            Assert.Equal(4, Count(host, "log"));
            // The Axe lies across two slots, and is in the hand from either.
            Assert.Equal(OpResult.Ok, Swing(world, host, tree, axe + 1));
            Assert.Equal(6, Count(host, "log"));
            // With the Axe on the belt but the hands empty it is by hand again.
            Assert.Equal(OpResult.Ok, Swing(world, host, tree, 0));
            Assert.Equal(7, Count(host, "log"));

            Assert.True(world.Nodes.Depleted(tree));
            Assert.Equal(OpResult.Depleted, Swing(world, host, tree, axe));
            // A slot the hotbar does not have is an empty hand, not an error.
            int second = Node(world, "tree", 1);
            Beside(world, host, second);
            Assert.Equal(OpResult.Ok, Swing(world, host, second, 200));
            Assert.Equal(8, Count(host, "log"));
        }

        [Fact]
        public void ABoulderAndAnOreVeinWantAPickaxe()
        {
            var (world, host) = InPrep();
            int boulder = Node(world, "boulder");
            Beside(world, host, boulder);
            int axe = Hold(world, host, "axe");

            Assert.Equal(OpResult.NeedsTool, Swing(world, host, boulder));
            Assert.Equal(OpResult.NeedsTool, Swing(world, host, boulder, axe));
            Assert.False(world.Nodes.Touched(boulder));
            Assert.Empty(world.Events.OfType<NodeHarvestedEvent>());

            int pickaxe = Hold(world, host, "pickaxe");
            Assert.Equal(OpResult.Ok, Swing(world, host, boulder, pickaxe));
            Assert.Equal(3, Count(host, "stone"));
            Assert.Equal(Db.Nodes[3].Hits - 1, world.Nodes.HitsLeft(boulder));

            int vein = Node(world, "ore_vein");
            Beside(world, host, vein);
            Assert.Equal(OpResult.NeedsTool, Swing(world, host, vein));
            Assert.Equal(OpResult.Ok, Swing(world, host, vein, pickaxe));
            Assert.Equal(2, Count(host, "iron_ore"));
        }

        [Fact]
        public void ASandPileOnTheBeachWantsAShovel_AndIsFullAgainWithTheNextShift()
        {
            var (world, host) = InPrep();
            var map = world.Map;
            int pile = Node(world, "sand_pile");
            var prop = map.Props[pile];
            Assert.Equal(PropKind.SandPile, prop.Kind);
            Assert.Equal(TileType.Beach, map.GetTile((int)MathF.Floor(prop.X / SimConstants.TileSize), (int)MathF.Floor(prop.Z / SimConstants.TileSize)));
            Beside(world, host, pile);

            Assert.Equal(OpResult.NeedsTool, Swing(world, host, pile));
            Assert.Equal(OpResult.NeedsTool, Swing(world, host, pile, Hold(world, host, "pickaxe")));
            int shovel = Hold(world, host, "shovel");
            for (int i = 0; i < 4; i++) Assert.Equal(OpResult.Ok, Swing(world, host, pile, shovel));
            Assert.Equal(16, Count(host, "sand"));
            Assert.True(world.Nodes.Depleted(pile));
            Assert.Equal(OpResult.Depleted, Swing(world, host, pile, shovel));

            IntoPayday(world, host);
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep && world.Run.Shift == 2, 30);
            Assert.False(world.Nodes.Touched(pile));
        }

        [Fact]
        public void TheBeachIsOpenGround_AndMovesNoTreeOfTheStreet()
        {
            var map = Db.Map("test_street");
            int beach = 0, piles = 0;
            for (int tz = 0; tz < map.Rows; tz++)
                for (int tx = 0; tx < map.Cols; tx++)
                {
                    if (map.GetTile(tx, tz) != TileType.Beach) continue;
                    beach++;
                    Assert.False(map.IsSolid(tx, tz));
                }
            Assert.True(beach > 0);
            foreach (var p in map.Props)
            {
                var tile = map.GetTile((int)MathF.Floor(p.X / SimConstants.TileSize), (int)MathF.Floor(p.Z / SimConstants.TileSize));
                if (p.Kind == PropKind.SandPile) { piles++; Assert.Equal(TileType.Beach, tile); }
                else if (p.Kind is PropKind.Tree or PropKind.Boulder) Assert.NotEqual(TileType.Beach, tile);
            }
            Assert.InRange(piles, 3, 20);
            // A tile of beach without a pile on it can be built on.
            var world = NewWorld();
            bool buildable = false;
            for (int tz = 0; tz < map.Rows && !buildable; tz++)
                for (int tx = 0; tx < map.Cols && !buildable; tx++)
                    buildable = map.GetTile(tx, tz) == TileType.Beach && BuildRules.CheckTile(map, world.Constructs, tx, tz) == OpResult.Ok;
            Assert.True(buildable);

            // The same map without its beach: another map hash, the same scenery key, and every
            // tree, bush and boulder off the sand where it stood.
            var source = Source();
            string mapJson = source.Files["maps/test_street.json"];
            Assert.Contains("\"beaches\"", mapJson);
            source.Files["maps/test_street.json"] = System.Text.RegularExpressions.Regex.Replace(mapJson, @"\s*""beaches"": \[\[[^\n]*\]\],", "");
            var bare = ContentDb.Load(source).Map("test_street");
            Assert.NotEqual(map.MapHash, bare.MapHash);
            Assert.Equal(map.SceneryKey, bare.SceneryKey);
            Assert.DoesNotContain(bare.Props, p => p.Kind == PropKind.SandPile);
            int kept = 0;
            foreach (var p in map.Props)
            {
                if (p.Kind is not (PropKind.Tree or PropKind.Bush or PropKind.Boulder)) continue;
                Assert.Contains(bare.Props, q => q.Kind == p.Kind && q.X == p.X && q.Z == p.Z);
                kept++;
            }
            Assert.True(kept > 50);
        }

        // --- refusals ---------------------------------------------------------------------------

        [Fact]
        public void OnlyInPrepAndInDelivery()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            int bush = Node(world, "fiber_bush");
            int second = Node(world, "fiber_bush", 1);
            Beside(world, host, bush);
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Equal(OpResult.WrongPhase, Swing(world, host, bush));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Beside(world, host, bush);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));

            IntoPayday(world, host);
            Beside(world, host, second);
            Assert.Equal(OpResult.WrongPhase, Swing(world, host, second));
            Assert.False(world.Nodes.Touched(second));
        }

        [Fact]
        public void OutOfReach_NotANode_AndDown()
        {
            var (world, host) = InPrep();
            int bush = Node(world, "fiber_bush");
            var prop = world.Map.Props[bush];
            float reach = Db.Balance.GetFloat("serverInteractRange") + prop.Radius;

            SimDriver.Teleport(host, prop.X + reach + 0.5f, prop.Z);
            // Long enough for the rewind window to forget where they stood before.
            SimDriver.StepTicks(world, 30);
            Assert.Equal(OpResult.OutOfRange, Swing(world, host, bush));
            SimDriver.Teleport(host, prop.X + reach - 0.2f, prop.Z);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));

            int lamp = Enumerable.Range(0, world.Map.Props.Count).First(i => world.Map.Props[i].Kind == PropKind.Lamp);
            Assert.Equal(OpResult.BadRequest, Swing(world, host, lamp));
            Assert.Equal(OpResult.BadRequest, Swing(world, host, 60000));

            world.DamagePlayer(host, 500f);
            Assert.True(host.Dead);
            SimDriver.StepTicks(world, SwingTicks);
            Assert.Equal(OpResult.Dead, Request(world, host, SimRequest.Harvest(0, bush, 0)));
            Assert.Equal(2, world.Nodes.HitsLeft(bush));
        }

        [Fact]
        public void ASwingThatComesTooFastIsRefused()
        {
            var (world, host) = InPrep();
            int tree = Node(world, "tree");
            Beside(world, host, tree);
            Assert.Equal(OpResult.Ok, Swing(world, host, tree));
            // Half a second (the swing less the slack) is 15 ticks; the request itself takes one.
            Assert.Equal(OpResult.TooFast, Request(world, host, SimRequest.Harvest(0, tree, 0)));
            SimDriver.StepTicks(world, 10);
            Assert.Equal(OpResult.TooFast, Request(world, host, SimRequest.Harvest(0, tree, 0)));
            SimDriver.StepTicks(world, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Harvest(0, tree, 0)));
            Assert.Equal(2, Count(host, "log"));
            Assert.Equal(Db.Nodes[0].Hits - 2, world.Nodes.HitsLeft(tree));
        }

        [Fact]
        public void WhatDoesNotFitIsNotHarvested()
        {
            var (world, host) = InPrep();
            int bush = Node(world, "fiber_bush");
            Beside(world, host, bush);
            // Pockets full of Planks, bar one Fiber stack that has room for two.
            var fiber = Db.Item("fiber");
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(fiber, fiber.MaxStack - 2)));
            var plank = Db.Item("plank");
            while (world.Give(host.Inventory, ItemStack.OfItem(plank, plank.MaxStack))) { }
            while (world.Give(host.Hotbar, ItemStack.OfItem(plank, plank.MaxStack))) { }
            int planks = Count(host, "plank");

            Assert.Equal(OpResult.DoesNotFit, Swing(world, host, bush));
            Assert.False(world.Nodes.Touched(bush));
            Assert.Equal(fiber.MaxStack - 2, Count(host, "fiber"));
            Assert.Equal(planks, Count(host, "plank"));

            // One Fiber less in the pockets and the hit goes in.
            host.Inventory.Entries.Single(e => e.Stack.DefIndex == fiber.Index).Stack.ItemCount -= 1;
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Assert.Equal(fiber.MaxStack, Count(host, "fiber"));
            Assert.Equal(2, world.Nodes.HitsLeft(bush));
        }

        // --- regrowing --------------------------------------------------------------------------

        [Fact]
        public void BushesAreFullAgainWithTheNextShift_TreesAndBouldersAreNot()
        {
            var (world, host) = InPrep();
            int bush = Node(world, "fiber_bush"), berries = Node(world, "berry_bush"), tree = Node(world, "tree"), boulder = Node(world, "boulder");
            int pickaxe = Hold(world, host, "pickaxe");
            Beside(world, host, bush);
            for (int i = 0; i < 3; i++) Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Beside(world, host, berries);
            Assert.Equal(OpResult.Ok, Swing(world, host, berries));
            Beside(world, host, tree);
            for (int i = 0; i < 5; i++) Assert.Equal(OpResult.Ok, Swing(world, host, tree));
            Beside(world, host, boulder);
            Assert.Equal(OpResult.Ok, Swing(world, host, boulder, pickaxe));
            Assert.True(world.Nodes.Depleted(bush) && world.Nodes.Depleted(tree));
            // A run that begins with every node whole says nothing about them.
            Assert.Empty(world.Events.OfType<NodesRegrownEvent>());

            IntoPayday(world, host);
            Assert.True(world.Nodes.Depleted(bush));
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep && world.Run.Shift == 2, 30);

            Assert.False(world.Nodes.Touched(bush) || world.Nodes.Touched(berries));
            Assert.True(world.Nodes.Depleted(tree));
            Assert.Equal(Db.Nodes[3].Hits - 1, world.Nodes.HitsLeft(boulder));
            var regrown = world.Events.OfType<NodesRegrownEvent>().Single();
            Assert.False(regrown.All);
            // It comes before the shift's phase change, as the shop's offer does.
            Assert.True(world.Events.IndexOf(regrown) < world.Events.FindLastIndex(e => e is PhaseChangedEvent));
            Beside(world, host, bush);
            Assert.Equal(OpResult.Ok, Swing(world, host, bush));
            Beside(world, host, tree);
            Assert.Equal(OpResult.Depleted, Swing(world, host, tree));

            // The run ends on a missed quota; back in the Lobby the town is whole again.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 310);
            Assert.True(world.Nodes.Depleted(tree));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            for (int i = 0; i < world.Nodes.Count; i++) Assert.False(world.Nodes.Touched(i));
            Assert.True(world.Events.OfType<NodesRegrownEvent>().Last().All);
            Assert.Equal(2, world.Events.OfType<NodesRegrownEvent>().Count());

            // And the next run begins without another word about it.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(2, world.Events.OfType<NodesRegrownEvent>().Count());
            Beside(world, host, tree);
            Assert.Equal(OpResult.Ok, Swing(world, host, tree));
        }
    }
}
