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
    /// The shop's first blueprint (doc 10 §3 bp_defense, doc 04 §1.3): Blueprint: Fortify, on the
    /// counter from shift 2, bought once for the whole team, and what it lets everybody build —
    /// the Stone Wall (doc 05 §4: 800 HP, 4 Stone) and the Spike Strip (150 HP, 2 Iron Ingots,
    /// 15 damage a second to a raider standing on it, in nobody's way).
    /// </summary>
    public class FortifyTests
    {
        private const int Tps = 30;
        /// <summary>Open ground south of the street, between the mailboxes of numbers 4 and 6.</summary>
        private const int Tx = 22, Tz = 15;

        private uint _nextRequest = 1;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef StoneWall => Db.Building("wall_stone");
        private static BuildingDef Spikes => Db.Building("spike_strip");
        private static ShopItemDef Fortify => Db.ShopItem("bp_defense");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        /// <summary>A run in the Prep of a shift, money in the wallet, Stone and Iron Ingots in the host's pockets.</summary>
        private (SimWorld World, Player Host) InPrep(int shift)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
            world.Economy.AddLoot(1000);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 8)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 4)));
            return (world, host);
        }

        private OpResult Buy(SimWorld world, Player p)
        {
            var po = world.Map.PostOffice;
            float x = p.Move.X, z = p.Move.Z;
            SimDriver.Teleport(p, po.ShopX + 1.0f, po.ShopZ);
            SimDriver.StepTicks(world, 2);
            var result = Request(world, p, SimRequest.Buy(0, Fortify.Index));
            SimDriver.Teleport(p, x, z);
            SimDriver.StepTicks(world, 15);
            return result;
        }

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation = 0) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        private static int Units(Player p, string item)
        {
            ushort index = Db.Item(item).Index;
            return p.Hotbar.Entries.Concat(p.Inventory.Entries).Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == index).Sum(e => e.Stack.Count);
        }

        [Fact]
        public void TheContentHasTheBlueprintAndWhatItBuilds()
        {
            Assert.Equal((ShopKind.Blueprint, 300, 2, true, false), (Fortify.Kind, Fortify.Price, Fortify.FromShift, Fortify.OncePerRun, Fortify.Rotating));
            Assert.Null(Fortify.Item);

            Assert.Equal((800, 2, BuildingBehaviour.Wall, true, true), (StoneWall.Hp, StoneWall.Rotations, StoneWall.Behaviour, StoneWall.Joins, StoneWall.Solid));
            var stone = StoneWall.Recipe;
            Assert.Equal(("stone", 4), (stone.Inputs.Single().Item.Id, stone.Inputs.Single().Count));
            Assert.Same(Fortify, stone.Blueprint);

            Assert.Equal((150, BuildingBehaviour.Spikes, false, false, 15f), (Spikes.Hp, Spikes.Behaviour, Spikes.Joins, Spikes.Solid, Spikes.DamagePerSecond));
            var spikes = Spikes.Recipe;
            Assert.Equal(("iron_ingot", 2), (spikes.Inputs.Single().Item.Id, spikes.Inputs.Single().Count));
            Assert.Same(Fortify, spikes.Blueprint);

            // The Logs' two still need none.
            Assert.Null(Db.Building("wall_wood").Recipe.Blueprint);
            Assert.Null(Db.Building("chest").Recipe.Blueprint);
        }

        [Fact]
        public void WithoutTheBlueprintNothingOfItIsBuilt_AndItIsNotSoldBeforeShift2()
        {
            var (world, host) = InPrep(1);
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.NeedsBlueprint, Place(world, host, StoneWall, Tx, Tz));
            Assert.Equal(OpResult.NeedsBlueprint, Place(world, host, Spikes, Tx, Tz));
            Assert.Equal(OpResult.ShopLocked, Buy(world, host));
            Assert.Empty(world.Constructs.All);
            Assert.Equal(8, Units(host, "stone"));
        }

        [Fact]
        public void BoughtOnceTheBlueprintIsTheTeams_ForTheRun()
        {
            var (world, host) = InPrep(2);
            var bo = world.AddPlayer("bo")!;
            SimDriver.StepTicks(world, 2);
            int wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Buy(world, host));
            Assert.Equal(wallet - 300, world.Economy.Wallet);
            Assert.True(world.OwnsBlueprint("bp_defense"));
            // Nothing lands in the pockets: the blueprint is knowledge, not a thing.
            Assert.Equal(2, host.Inventory.Entries.Count());
            Assert.Equal(OpResult.AlreadyOwned, Buy(world, host));
            Assert.Equal(OpResult.AlreadyOwned, Buy(world, bo));

            // Whoever has the Stone builds the wall, whoever bought it.
            Assert.True(world.Give(bo.Inventory, ItemStack.OfItem(Db.Item("stone"), 4)));
            SimDriver.Teleport(bo, Centre(Tx - 1), Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Place(world, bo, StoneWall, Tx, Tz, 1));
            var wall = world.Constructs.At(Tx, Tz)!;
            Assert.Equal((800, 1), (wall.Hp, (int)wall.Rotation));
            Assert.Equal(0, Units(bo, "stone"));

            // A Wooden Wall beside it closes the corner with it: walls join walls.
            Assert.True(world.Give(bo.Inventory, ItemStack.OfItem(Db.Item("log"), 3)));
            Assert.Equal(OpResult.Ok, Place(world, bo, Db.Building("wall_wood"), Tx + 1, Tz, 0));
            Assert.NotEqual(0, world.Constructs.Arms(wall) & ConstructField.ArmEast);
        }

        [Fact]
        public void AStoneWallTakesLongerToBreakThanAWoodenOne()
        {
            var (world, host) = InPrep(2);
            Assert.Equal(OpResult.Ok, Buy(world, host));
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Place(world, host, StoneWall, Tx, Tz));
            var wall = world.Constructs.At(Tx, Tz)!;
            float blow = Db.Enemy("barbarian").Damage;
            for (int i = 0; i < (int)(300 / blow) + 1; i++) world.DamageConstruct(wall, blow);
            // What would have felled a Wooden Wall leaves the stone one standing.
            Assert.Same(wall, world.Constructs.At(Tx, Tz));
            Assert.True(wall.Hp > 400, $"{wall.Hp} HP left");

            // Taken down at 4/8 of its HP in Prep: half the Stone back.
            world.DamageConstruct(wall, wall.Hp - 400);
            Assert.Equal(400, wall.Hp);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, Tx, Tz)));
            Assert.Equal(4 + 2, Units(host, "stone"));
        }

        [Fact]
        public void ASpikeStripStopsNobody_AndHurtsTheRaiderOnIt()
        {
            var (world, host) = InPrep(2);
            Assert.Equal(OpResult.Ok, Buy(world, host));
            SimDriver.Teleport(host, Centre(Tx - 1), Centre(Tz));
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Place(world, host, Spikes, Tx, Tz));
            Assert.Equal(2, Units(host, "iron_ingot"));
            var strip = world.Constructs.At(Tx, Tz)!;

            // In nobody's way: not in a tile path's, not in the carrier's, and nothing else goes on the tile.
            Assert.False(world.Constructs.Blocks(Tx, Tz));
            Assert.Equal(OpResult.Blocked, BuildRules.CheckTile(world.Map, world.Constructs, Tx, Tz));
            Assert.True(SimDriver.WalkTo(world, host, Centre(Tx + 1), Centre(Tz), maxTicks: 300));
            Assert.Equal(host.MaxHp, host.Hp);
            float x = Centre(Tx), z = Centre(Tz);
            float px = x, pz = z;
            PlayerMovement.ResolveCollisions(ref px, ref pz, 0f, world.Map, 0.4f, world.Constructs);
            Assert.Equal((x, z), (px, pz));

            // A raider standing on it loses fifteen HP a second; one beside it nothing.
            SimDriver.Teleport(host, Centre(Tx - 4), Centre(Tz));
            var on = world.SpawnEnemy(Db.Enemy("barbarian"), x, z)!;
            var beside = world.SpawnEnemy(Db.Enemy("barbarian"), Centre(Tx), Centre(Tz + 2))!;
            on.Speed = 0f;
            beside.Speed = 0f;
            float hpOn = on.Hp, hpBeside = beside.Hp;
            SimDriver.StepTicks(world, Tps);
            Assert.InRange(hpOn - on.Hp, 14.5f, 15.5f);
            Assert.Equal(hpBeside, beside.Hp);
            // It does not wear out under them.
            Assert.Equal(150, strip.Hp);

            // Held there, it falls to the spikes; nobody's kill.
            world.Events.Clear();
            for (int t = 0; t < 30 * Tps && world.Enemies.Contains(on); t++) world.Step();
            Assert.DoesNotContain(on, world.Enemies);
        }
    }
}
