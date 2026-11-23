using System.Linq;
using PerformativeMail.Sim.Automation;
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
    /// The Fast Belt (doc 04 §2.1 Mk2, doc 10 §2.1 belt_mk2, doc 10 §3 bp_fast_belts): 4 m/s, a
    /// Plank and two Iron Ingots a tile, built once the team owns the blueprint the shop sells from
    /// shift 3; on the Conveyor Belt's key of Build mode, the wheel between them.
    /// </summary>
    public class FastBeltTests
    {
        private const int Tps = 30;
        private const int Row = 12;

        private uint _nextRequest = 1;
        private uint _nextMail = 980000;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Fast => Db.Building("belt_mk2");
        private static BuildingDef Belt => Db.Building("belt_mk1");
        private static ShopItemDef Blueprint => Db.ShopItem("bp_fast_belts");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static float Centre(int t) => BuildRules.TileCentre(t);

        private (SimWorld World, Player Host) InPrep(int shift)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
            world.Economy.AddLoot(1000);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            SimDriver.Teleport(host, Centre(9), Centre(Row) + 1f);
            return (world, host);
        }

        private OpResult Buy(SimWorld world, Player p)
        {
            var po = world.Map.PostOffice;
            float x = p.Move.X, z = p.Move.Z;
            SimDriver.Teleport(p, po.ShopX + 1.0f, po.ShopZ);
            SimDriver.StepTicks(world, 2);
            var result = Request(world, p, SimRequest.Buy(0, Blueprint.Index));
            SimDriver.Teleport(p, x, z);
            SimDriver.StepTicks(world, 15);
            return result;
        }

        private OpResult Line(SimWorld world, Player p, BuildingDef def, int x0, int x1) =>
            Request(world, p, SimRequest.PlaceLine(0, def.Index, x0, Row, x1, Row, 0));

        [Fact]
        public void TheFastBelt_IsContent_FourMetresASecond_BehindItsBlueprint_OnTheBeltsKey()
        {
            Assert.Equal((BuildingBehaviour.Belt, 4, 100, 4f), (Fast.Behaviour, Fast.Rotations, Fast.Hp, Fast.Speed));
            Assert.True(Fast.Drags && !Fast.Solid);
            Assert.Equal(new[] { ("plank", 1), ("iron_ingot", 2) }, Fast.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Same(Blueprint, Fast.Recipe.Blueprint);
            Assert.Equal((ShopKind.Blueprint, 500, 3), (Blueprint.Kind, Blueprint.Price, Blueprint.FromShift));
            // One key for both belts: the wheel steps between them (doc 09 §2.7).
            Assert.Equal(Belt.Recipe.Key, Fast.Recipe.Key);
            // The belt's variants: the Fast Belt, the Belt Ramp and the Elevated Belt (RampTests).
            var group = Db.RecipeKeys[Belt.Recipe.Key];
            Assert.Equal(new[] { "belt_mk1", "belt_mk2", "belt_mk1_ramp", "belt_mk1_elevated" }, group.Select(r => r.Building.Id));
            // And the sorter's: the Address Sorter and the Inserter (InserterTests).
            Assert.Equal(Db.Recipes.Count - 4, Db.RecipeKeys.Count);
            Assert.True(Db.RecipeKeys.Count <= ContentDb.MaxRecipeKeys);
            Assert.All(Db.Recipes.Where(r => r.Group.Length == 0), r => Assert.Equal(r, Db.RecipeKeys[r.Key].Single()));
        }

        [Fact]
        public void WithoutTheBlueprint_NoFastBelt_AndItIsNotSoldBeforeShift3()
        {
            var (world, host) = InPrep(2);
            Assert.Equal(OpResult.NeedsBlueprint, Line(world, host, Fast, 6, 9));
            Assert.Equal(OpResult.ShopLocked, Buy(world, host));
            Assert.Empty(world.Constructs.All);
        }

        [Fact]
        public void BoughtInShift3_FastBeltsAreLaid_AndMailRidesThemTwiceAsFast_AsASegmentOfTheirOwn()
        {
            var (world, host) = InPrep(3);
            int wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Buy(world, host));
            Assert.Equal(wallet - 500, world.Economy.Wallet);
            // Two Conveyor Belts, then four Fast Belts: the change of tier starts a segment.
            Assert.Equal(OpResult.Ok, Line(world, host, Belt, 5, 6));
            Assert.Equal(OpResult.Ok, Line(world, host, Fast, 7, 10));
            world.Step();
            Assert.Equal(6, world.Constructs.All.Count);
            var slow = world.Belts.SegmentAt(5, Row)!;
            var fast = world.Belts.SegmentAt(7, Row)!;
            Assert.NotSame(slow, fast);
            Assert.Same(fast, slow.Next);
            Assert.Equal((2f, 4f), (slow.Speed, fast.Speed));
            Assert.Equal(4, fast.Tiles.Count);

            var dest = world.Map.Destinations.Single(d => d.Address.Number == 1);
            Assert.True(world.Belts.TryInsert(fast, TestContent.Mail("letter", dest.Address, _nextMail++, world.Run.Shift), out _));
            SimDriver.StepTicks(world, Tps);
            Assert.InRange(fast.Items[0].Pos, 3.9f, 4.1f);
            // Fast mail backs up behind the end as slow mail does: 0.5 m apart.
            Assert.True(world.Belts.TryInsert(fast, TestContent.Mail("letter", dest.Address, _nextMail++, world.Run.Shift), out _));
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(2, fast.Items.Count);
        }
    }
}
