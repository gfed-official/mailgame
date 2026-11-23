using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Inserter (doc 04 §2.5, doc 10 §2.2, doc 11 "inserter rate 1.25 items/s"): one tile; a
    /// piece of mail at a time from what stands behind it (a chest, the end of a belt, the Intake)
    /// into what stands in front (a mailbox, a belt, a chest, a sorter), only what the front takes,
    /// optionally by kind.
    /// </summary>
    public class InserterTests
    {
        private const int Tps = 30;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Inserter => Db.Building("inserter");
        private static BuildingDef Long => Db.Building("inserter_long");
        private static BuildingDef Chest => Db.Building("chest");
        private static BuildingDef Belt => Db.Building("belt_mk1");

        private uint _nextRequest = 1;
        private uint _nextMail = 970000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep(float x, float z)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 20)));
            SimDriver.Teleport(host, x, z);
            return (world, host);
        }

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        private MailItem Letter(SimWorld world, AddressId address, string kind = "letter") =>
            TestContent.Mail(kind, address, _nextMail++, world.Run.Shift);

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void TheInserter_IsContent_OnTheSortersKey_NoBlueprint()
        {
            Assert.Equal((BuildingBehaviour.Inserter, 1, 4, 120, 1.25f), (Inserter.Behaviour, Inserter.TileCount, Inserter.Rotations, Inserter.Hp, Inserter.Rate));
            Assert.Equal(new[] { ("iron_ingot", 2), ("plank", 1) }, Inserter.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Null(Inserter.Recipe.Blueprint);
            Assert.Equal(1, Inserter.FilterOutputs);
            // Doc 09 §2.7's "Sorting" category: the sorter and the inserter on one key, the wheel between them.
            Assert.Equal(Db.Building("address_sorter_mk1").Recipe.Key, Inserter.Recipe.Key);
        }

        [Fact]
        public void FromAChestIntoAMailbox_OnlyItsOwnMail_InDelivery_AtItsRate()
        {
            // The first house's mailbox stands on (12, 12); the inserter west of it faces it, the chest behind.
            var (world, host) = InPrep(BuildRules.TileCentre(9), BuildRules.TileCentre(11));
            var dest = world.Map.Destinations[0];
            Assert.Equal((12, 12), ((int)(dest.MailboxX / SimConstants.TileSize), (int)(dest.MailboxZ / SimConstants.TileSize)));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, 10, 12, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Inserter, 11, 12, 0));
            var chest = world.GetContainer(world.Constructs.At(10, 12)!.ContainerId)!;
            for (int i = 0; i < 3; i++) Assert.True(world.Give(chest, ItemStack.OfMail(Letter(world, dest.Address))));
            var other = world.Map.Destinations[1].Address;
            Assert.True(world.Give(chest, ItemStack.OfMail(Letter(world, other))));
            Assert.Equal(4, MailIn(chest));

            // Prep: the mailbox is shut, nothing moves.
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(4, MailIn(chest));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            world.Events.Clear();
            int wallet = world.Economy.Wallet;
            // At 1.25 a second: two moves in the first second at most.
            for (int t = 0; t < Tps; t++) world.Step();
            Assert.InRange(3 - (MailIn(chest) - 1), 1, 2);
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(1, MailIn(chest));
            Assert.Equal(other, chest.Entries.Single(e => e.Stack.Kind == StackKind.Mail).Stack.Mail[0].Address);
            Assert.True(world.Economy.Wallet > wallet);
            Assert.Equal(3, world.Economy.ShiftDeliveriesByKind.Sum());
        }

        [Fact]
        public void FromTheIntake_TheOldestFirst_OntoABelt_AndOffTheEndOfABelt_IntoAChest()
        {
            var (world, _) = InPrep(0f, 0f);
            var belts = world.Belts;
            int ox = belts.OutfeedX, oz = belts.OutfeedZ;
            var host = world.Players[0];
            SimDriver.Teleport(host, BuildRules.TileCentre(ox) + 2.5f, BuildRules.TileCentre(oz - 1));
            SimDriver.StepTicks(world, 2);
            // On the outfeed's tile an inserter faces away from the Intake: a belt north, into another inserter, into a chest.
            Assert.Equal(OpResult.Ok, Place(world, host, Inserter, ox, oz, 3));
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, ox, oz - 1, 3));
            Assert.Equal(OpResult.Ok, Place(world, host, Inserter, ox, oz - 2, 3));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, ox, oz - 3, 0));
            var chest = world.GetContainer(world.Constructs.At(ox, oz - 3)!.ContainerId)!;
            var dest = world.Map.Destinations[0];
            for (int i = 0; i < 4; i++) world.Spawner.ReturnToIntake(Letter(world, dest.Address));
            int intake = MailIn(world.Intake);
            uint oldest = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail).Min(m => m.Id);
            world.Events.Clear();
            world.Step();
            world.Step();
            var first = world.Events.OfType<BeltItemAddedEvent>().First();
            Assert.Equal(oldest, first.Item.MailId);
            Assert.Contains(world.Events, e => e is InserterSwungEvent s && s.X == ox && s.Y == oz);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.True(MailIn(chest) >= 3, $"{MailIn(chest)} in the chest");
            Assert.Equal(intake, MailIn(world.Intake) + MailIn(chest) + world.Belts.ItemCount);
        }

        [Fact]
        public void AKindFilter_LetsOnlyItsKindThrough_AndIntoASorter()
        {
            var (world, host) = InPrep(BuildRules.TileCentre(15), BuildRules.TileCentre(23));
            // Into the input of a sorter at (16, 21) facing east, from a chest at (14, 21).
            world.Economy.AddLoot(1000);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(host, po.ShopX + 1.0f, po.ShopZ);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bp_sorting").Index)));
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(23));
            SimDriver.StepTicks(world, 15);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 6)));
            Assert.Equal(OpResult.Ok, Place(world, host, Db.Building("address_sorter_mk1"), 16, 21, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Inserter, 15, 21, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, 14, 21, 0));
            var letter = Db.MailKind("letter");
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 15, 21, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Kind, letter.Index).Encode())));
            // Only a kind: never a street.
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Configure(0, 15, 21, SimRequest.ConfigureSet, 0, 0, new MailFilter(FilterKind.Street, world.Map.Streets[0].Id).Encode())));
            var chest = world.GetContainer(world.Constructs.At(14, 21)!.ContainerId)!;
            var dest = world.Map.Destinations[0];
            Assert.True(world.Give(chest, ItemStack.OfMail(Letter(world, dest.Address, "postcard"))));
            Assert.True(world.Give(chest, ItemStack.OfMail(Letter(world, dest.Address))));
            Assert.True(world.Give(chest, ItemStack.OfMail(Letter(world, dest.Address))));
            SimDriver.StepTicks(world, 3 * Tps);
            var node = world.Belts.SorterOf(world.Constructs.At(16, 21)!)!;
            // The sorter has no belt out: the letters wait in its buffer, the postcard in the chest.
            Assert.Equal(2, node.Buffer.Count);
            Assert.All(node.Buffer, m => Assert.Equal(letter.Index, m.Kind));
            Assert.Equal(1, MailIn(chest));
        }
        private void BuySorting(SimWorld world, Player host, float x, float z)
        {
            world.Economy.AddLoot(1000);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(host, po.ShopX + 1.0f, po.ShopZ);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bp_sorting").Index)));
            SimDriver.Teleport(host, x, z);
            SimDriver.StepTicks(world, 15);
        }

        [Fact]
        public void TheLongInserter_IsContent_ReachTwo_BehindTheSortingBlueprint_OnTheSortersKey()
        {
            Assert.Equal((BuildingBehaviour.Inserter, 1, 4, 120, 1.25f, 2), (Long.Behaviour, Long.TileCount, Long.Rotations, Long.Hp, Long.Rate, Long.Reach));
            Assert.Equal(1, Inserter.Reach);
            Assert.Equal(new[] { ("iron_ingot", 3), ("plank", 1) }, Long.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Same(Db.ShopItem("bp_sorting"), Long.Recipe.Blueprint);
            Assert.Equal(Inserter.Recipe.Key, Long.Recipe.Key);
            Assert.Equal(new[] { "address_sorter_mk1", "inserter", "inserter_long" }, Db.RecipeKeys[Long.Recipe.Key].Select(r => r.Building.Id));

            var (world, host) = InPrep(BuildRules.TileCentre(9), BuildRules.TileCentre(11));
            Assert.Equal(OpResult.NeedsBlueprint, Place(world, host, Long, 10, 12, 0));
        }

        [Fact]
        public void TheLongInserter_FromAChestTwoBehind_OverABelt_IntoAMailboxTwoOn()
        {
            // The first house's mailbox on (12, 12); the long inserter on (10, 12) facing it, the chest on (8, 12).
            var (world, host) = InPrep(BuildRules.TileCentre(9), BuildRules.TileCentre(11));
            BuySorting(world, host, BuildRules.TileCentre(9), BuildRules.TileCentre(11));
            var dest = world.Map.Destinations[0];
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, 8, 12, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Long, 10, 12, 0));
            // A belt on the tile between, running north: the arm reaches over it, and it is not fed.
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, 11, 12, 3));
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, 9, 12, 3));
            var chest = world.GetContainer(world.Constructs.At(8, 12)!.ContainerId)!;
            for (int i = 0; i < 3; i++) Assert.True(world.Give(chest, ItemStack.OfMail(Letter(world, dest.Address))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Equal(0, MailIn(chest));
            Assert.Equal(3, world.Economy.ShiftDeliveriesByKind.Sum());
            Assert.Equal(0, world.Belts.ItemCount);
        }

        [Fact]
        public void TheLongInserter_FromTheIntake_OverTheOutfeed_OntoABelt_AndOffItsEnd_OverATile_IntoAChest()
        {
            var (world, _) = InPrep(0f, 0f);
            var belts = world.Belts;
            int ox = belts.OutfeedX, oz = belts.OutfeedZ;
            var host = world.Players[0];
            BuySorting(world, host, BuildRules.TileCentre(ox) - 2.5f, BuildRules.TileCentre(oz - 2));
            // The outfeed's tile holds a belt running east, not fed (it does not face away from the
            // Intake); a long inserter beyond it reaches over it into the Intake.
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, ox, oz, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Long, ox, oz - 1, 3));
            // A belt two on turns east and ends into the empty tile beyond; a long inserter past that takes off it into a chest two on.
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, ox, oz - 3, 0));
            SimDriver.Teleport(host, BuildRules.TileCentre(ox + 2), BuildRules.TileCentre(oz - 2));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Place(world, host, Long, ox + 2, oz - 3, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Chest, ox + 4, oz - 3, 0));
            var chest = world.GetContainer(world.Constructs.At(ox + 4, oz - 3)!.ContainerId)!;
            var dest = world.Map.Destinations[0];
            for (int i = 0; i < 4; i++) world.Spawner.ReturnToIntake(Letter(world, dest.Address));
            int intake = MailIn(world.Intake);
            uint oldest = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).SelectMany(e => e.Stack.Mail).Min(m => m.Id);
            world.Events.Clear();
            world.Step();
            world.Step();
            var first = world.Events.OfType<BeltItemAddedEvent>().First();
            Assert.Equal(oldest, first.Item.MailId);
            Assert.Equal(oz - 3, world.Belts.Segments.Single(s => s.Items.Any(it => it.Mail.Id == oldest)).Head / world.Map.Cols);
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.True(MailIn(chest) >= 3, $"{MailIn(chest)} in the chest");
            Assert.Equal(intake, MailIn(world.Intake) + MailIn(chest) + world.Belts.ItemCount);
            // Nothing ever rode the belt on the outfeed's tile.
            Assert.DoesNotContain(world.Belts.Segments, s => s.Head == oz * world.Map.Cols + ox && s.Items.Count > 0);
        }
    }
}
