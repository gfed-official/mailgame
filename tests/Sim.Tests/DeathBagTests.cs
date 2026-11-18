using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 01 §2.2: who dies leaves their inventory "as a lootable backpack at the death location
    /// (30 s despawn, then contents return to PO Intake)".
    /// </summary>
    public class DeathBagTests
    {
        private const int Tps = 30;

        private uint _nextRequest = 1;
        private uint _nextMailId = 800000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld() => new SimWorld(Db, new SimSettings { Seed = 7 });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static Player PlayerOnTheStreet(SimWorld world, float x = 50f, float z = 28f)
        {
            var p = world.AddPlayer("carrier")!;
            SimDriver.Teleport(p, x, z);
            return p;
        }

        /// <summary>A letter for the first house, into the hotbar. Letters for one address stack.</summary>
        private uint GiveLetter(SimWorld world, Player p)
        {
            var item = TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMailId++);
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            return item.Id;
        }

        private static void GiveStone(SimWorld world, Player p, int count = 3) =>
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(Db.Item("stone"), count)));

        private static bool InIntake(SimWorld world, uint mailId) =>
            world.Intake.Entries.Any(e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == mailId));

        private static int Stones(GridContainer container) =>
            container.Entries.Where(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == Db.Item("stone").Index).Sum(e => e.Stack.Count);

        [Fact]
        public void TheBagIsAContainerOfTheContent_BigEnoughForBothPockets()
        {
            var bag = Db.Container(SimWorld.DeathBagDefId);
            var hotbar = Db.Container("hotbar");
            var inventory = Db.Container("player_inventory");
            Assert.Equal((8, 4), (bag.Cols, bag.Rows));
            Assert.True(bag.Cols >= hotbar.Cols && bag.Cols >= inventory.Cols);
            Assert.True(bag.Rows >= hotbar.Rows + inventory.Rows);
            Assert.False(bag.HandCarried);
            Assert.Equal(30, Db.Balance.GetDouble("deathBagDespawnSeconds"));
        }

        [Fact]
        public void WhoGoesDownLeavesEverythingInABagWhereTheyFell_EachStackInItsCell()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world, 50f, 28f);
            uint letterId = GiveLetter(world, p);
            Assert.True(world.Give(p.Inventory, ItemStack.OfMail(TestContent.Mail("medium", world.Map.Destinations[1].Address, _nextMailId++))));
            GiveStone(world, p);
            var letter = p.Hotbar.Entries.Single();
            var package = p.Inventory.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            var stone = p.Inventory.Entries.Single(e => e.Stack.Kind == StackKind.Item);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Move(0, p.Inventory.Id, stone.EntryId, p.Inventory.Id, 5, 1)));
            Assert.Equal((1, 0), (letter.X, letter.Y)); // cell 0 of the hotbar is the hammer's
            Assert.Equal((0, 0, 2, 2), (package.X, package.Y, package.W, package.H));
            Assert.True(p.SpeedMult < 1f);

            world.DamagePlayer(p, 1000f);

            Assert.True(p.Dead);
            Assert.Equal(0, p.Hotbar.EntryCount);
            Assert.Equal(0, p.Inventory.EntryCount);
            Assert.Equal(1f, p.SpeedMult);

            var bag = Assert.Single(world.Bags);
            Assert.Equal((p.Id, 50f, 28f), (bag.Owner, bag.X, bag.Z));
            Assert.Equal(world.Tick + 30 * Tps, bag.DespawnTick);
            Assert.Same(world.GetContainer(bag.Id), bag.Container);
            Assert.Equal(SimWorld.DeathBagDefId, bag.Container.Def.Id);
            Assert.Equal(3, bag.Container.EntryCount);
            // The same entries, ids and all: the hotbar is the first row, the inventory the two under it.
            Assert.Same(letter, bag.Container.Get(letter.EntryId));
            Assert.Equal((1, 0), (letter.X, letter.Y));
            Assert.Equal(letterId, letter.Stack.Mail.Single().Id);
            Assert.Equal((0, 1, 2, 2), (package.X, package.Y, package.W, package.H));
            Assert.Same(package, bag.Container.EntryAt(1, 2));
            Assert.Equal((5, 2), (stone.X, stone.Y));
            Assert.Equal(3, stone.Stack.Count);

            // Everybody hears of it, after the death itself.
            var dropped = Assert.Single(world.Events.OfType<BagDroppedEvent>());
            Assert.True(dropped.Audience.IsNone);
            Assert.Equal((bag.Id, p.Id, 50f, 28f, bag.DespawnTick), (dropped.ContainerId, dropped.Owner, dropped.X, dropped.Z, dropped.DespawnTick));
            Assert.True(world.Events.IndexOf(dropped) > world.Events.FindIndex(e => e is PlayerDiedEvent));
            // The carrier saw their pockets empty.
            Assert.Contains(world.Events.OfType<ContainerDeltaEvent>(), e => e.Audience == p.Id && e.Delta.ContainerId == p.Hotbar.Id && e.Delta.Removed.Contains(letter.EntryId));
            Assert.Contains(world.Events.OfType<ContainerDeltaEvent>(), e => e.Audience == p.Id && e.Delta.ContainerId == p.Inventory.Id && e.Delta.Removed.Count == 2);
        }

        [Fact]
        public void EmptyPocketsLeaveNoBag()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world);
            uint containers = world.AddWorldContainer("chest", 0, 0).Id;
            world.DamagePlayer(p, 1000f);
            Assert.True(p.Dead);
            Assert.Empty(world.Bags);
            Assert.Empty(world.Events.OfType<BagDroppedEvent>());
            // No container was made for it either.
            Assert.Equal(containers + 1, world.AddWorldContainer("chest", 0, 0).Id);
        }

        [Fact]
        public void AnyoneInReachCanTakeFromTheBag_AndAnEmptiedBagIsGone()
        {
            var world = NewWorld();
            var fallen = PlayerOnTheStreet(world, 50f, 28f);
            var friend = PlayerOnTheStreet(world, 60f, 28f);
            uint letterId = GiveLetter(world, fallen);
            GiveStone(world, fallen);
            world.DamagePlayer(fallen, 1000f);
            var bag = world.Bags.Single();
            uint letter = bag.Container.Entries.Single(e => e.Stack.Kind == StackKind.Mail).EntryId;
            uint stone = bag.Container.Entries.Single(e => e.Stack.Kind == StackKind.Item).EntryId;

            // Not from ten metres away, and not by the one who lies there.
            Assert.Equal(OpResult.OutOfRange, Request(world, friend, SimRequest.Open(0, bag.Id)));
            Assert.Equal(OpResult.Dead, Request(world, fallen, SimRequest.Open(0, bag.Id)));

            SimDriver.Teleport(friend, 51.5f, 28f);
            Assert.Equal(OpResult.Ok, Request(world, friend, SimRequest.Open(0, bag.Id)));
            Assert.Equal(bag.Id, friend.OpenContainer);
            var state = world.Events.OfType<ContainerStateEvent>().Last(e => e.Audience == friend.Id).State;
            Assert.Equal((bag.Id, 2), (state.ContainerId, state.Entries.Count));

            Assert.Equal(OpResult.Ok, Request(world, friend, SimRequest.QuickMove(0, bag.Id, letter)));
            Assert.Equal(letterId, friend.Hotbar.Entries.Single().Stack.Mail.Single().Id);
            Assert.Single(world.Bags);
            Assert.Empty(world.Events.OfType<BagGoneEvent>());
            // Things can be put back in as well.
            Assert.Equal(OpResult.Ok, Request(world, friend, SimRequest.QuickMove(0, friend.Hotbar.Id, letter)));
            Assert.Equal(2, bag.Container.EntryCount);
            Assert.Equal(OpResult.Ok, Request(world, friend, SimRequest.QuickMove(0, bag.Id, letter)));

            Assert.Equal(OpResult.Ok, Request(world, friend, SimRequest.QuickMove(0, bag.Id, stone)));
            Assert.Equal(3, Stones(friend.Hotbar) + Stones(friend.Inventory));
            Assert.Empty(world.Bags);
            Assert.Null(world.GetContainer(bag.Id));
            Assert.Equal(0u, friend.OpenContainer);
            Assert.Contains(world.Events.OfType<ContainerClosedEvent>(), e => e.Audience == friend.Id && e.ContainerId == bag.Id);
            var gone = Assert.Single(world.Events.OfType<BagGoneEvent>());
            Assert.Equal((bag.Id, false, (ushort)0), (gone.ContainerId, gone.Returned, gone.Mail));
            Assert.Equal(OpResult.UnknownContainer, Request(world, friend, SimRequest.Open(0, bag.Id)));
            Assert.False(InIntake(world, letterId));
            SimDriver.StepTicks(world, 31 * Tps);
            Assert.Single(world.Events.OfType<BagGoneEvent>());
        }

        [Fact]
        public void AfterThirtySecondsTheMailIsBackInTheIntake_AndTheRestWithItsOwner()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world, 50f, 28f);
            var friend = PlayerOnTheStreet(world, 51.5f, 28f);
            uint first = GiveLetter(world, p), second = GiveLetter(world, p);
            GiveStone(world, p);
            world.DamagePlayer(p, 1000f);
            var bag = world.Bags.Single();
            Assert.Equal(OpResult.Ok, Request(world, friend, SimRequest.Open(0, bag.Id)));

            while (world.Tick < bag.DespawnTick - 1) world.Step();
            Assert.Single(world.Bags);
            Assert.False(p.Dead); // up again after 10 s, with nothing
            Assert.Equal(0, p.Inventory.EntryCount);
            Assert.False(InIntake(world, first));

            world.Step();
            Assert.Empty(world.Bags);
            Assert.Null(world.GetContainer(bag.Id));
            Assert.True(InIntake(world, first));
            Assert.True(InIntake(world, second));
            Assert.Equal(3, Stones(p.Inventory));
            Assert.Equal(0, Stones(friend.Inventory));
            var gone = Assert.Single(world.Events.OfType<BagGoneEvent>());
            Assert.Equal((bag.Id, true, (ushort)2), (gone.ContainerId, gone.Returned, gone.Mail));
            // Whoever was looking into it is looking at nothing now, and the next tick finds no container to miss.
            Assert.Equal(0u, friend.OpenContainer);
            Assert.Contains(world.Events.OfType<ContainerClosedEvent>(), e => e.Audience == friend.Id && e.ContainerId == bag.Id);
            SimDriver.StepTicks(world, 3);
        }

        [Fact]
        public void EveryDeathLeavesItsOwnBag()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world, 50f, 28f);
            GiveLetter(world, p);
            world.DamagePlayer(p, 1000f);
            // Ten seconds down, then the five in which nothing can hurt them.
            SimDriver.StepTicks(world, 16 * Tps);
            Assert.False(p.Dead);
            SimDriver.Teleport(p, 70f, 28f);
            GiveStone(world, p);
            world.DamagePlayer(p, 1000f);
            Assert.Equal(new[] { 50f, 70f }, world.Bags.Select(b => b.X));
            Assert.NotEqual(world.Bags[0].Id, world.Bags[1].Id);
            Assert.Equal(16u * Tps, world.Bags[1].DespawnTick - world.Bags[0].DespawnTick);
        }

        [Fact]
        public void NoBagOutlivesItsPhase()
        {
            var world = NewWorld();
            var p = world.AddPlayer("carrier")!;
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            SimDriver.Teleport(p, 50f, 28f);
            uint letterId = GiveLetter(world, p);
            GiveStone(world, p);
            world.DamagePlayer(p, 1000f);
            uint bag = world.Bags.Single().Id;

            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Empty(world.Bags);
            Assert.Null(world.GetContainer(bag));
            var gone = Assert.Single(world.Events.OfType<BagGoneEvent>());
            Assert.Equal((bag, true, (ushort)1), (gone.ContainerId, gone.Returned, gone.Mail));
            Assert.True(InIntake(world, letterId));
            Assert.Equal(3, Stones(p.Inventory));
            Assert.False(p.Dead);
        }

        [Fact]
        public void ALeaversBagIsNobodys_AndWhoTakesTheirSeatInheritsNothing()
        {
            var world = NewWorld();
            var host = PlayerOnTheStreet(world, 20f, 28f);
            var guest = PlayerOnTheStreet(world, 50f, 28f);
            uint letterId = GiveLetter(world, guest);
            GiveStone(world, guest);
            world.DamagePlayer(guest, 1000f);
            var bag = world.Bags.Single();
            Assert.Equal(guest.Id, bag.Owner);

            world.RemovePlayer(guest.Id);
            Assert.Same(bag, world.Bags.Single());
            Assert.Equal(EntityId.None, bag.Owner);
            var next = world.AddPlayer("next")!;
            Assert.Equal(guest.Id, next.Id);

            while (world.Tick < bag.DespawnTick) world.Step();
            Assert.Empty(world.Bags);
            Assert.True(InIntake(world, letterId));
            // The stone had nobody to go back to.
            Assert.Equal(0, next.Inventory.EntryCount + next.Hotbar.EntryCount + host.Inventory.EntryCount);
        }

        [Fact]
        public void WhatDoesNotFitBackWithItsOwnerIsLost()
        {
            var world = NewWorld();
            var p = PlayerOnTheStreet(world, 50f, 28f);
            GiveStone(world, p, 5);
            world.DamagePlayer(p, 1000f);
            var bag = world.Bags.Single();
            SimDriver.StepTicks(world, 11 * Tps);
            Assert.False(p.Dead);
            // By the time the bag comes back the pockets are full, but for a stack of stone with room for 3.
            var log = Db.Item("log");
            var fiber = Db.Item("fiber");
            var stone = Db.Item("stone");
            for (int i = 0; i < 7; i++) Assert.True(world.Give(p.Inventory, ItemStack.OfItem(log, log.MaxStack)));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(stone, stone.MaxStack - 3)));
            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(fiber, fiber.MaxStack)));
            for (int i = 0; i < 7; i++) Assert.True(world.Give(p.Hotbar, ItemStack.OfItem(fiber, fiber.MaxStack)));
            Assert.False(p.Inventory.FindFreeSpot(1, 1, out _, out _) || p.Hotbar.FindFreeSpot(1, 1, out _, out _));

            while (world.Tick < bag.DespawnTick) world.Step();
            Assert.Empty(world.Bags);
            Assert.Equal(stone.MaxStack, Stones(p.Inventory));
            Assert.Equal(0, Stones(p.Hotbar));
            Assert.Equal(9, p.Inventory.EntryCount);
            Assert.Equal(7, p.Hotbar.EntryCount);
        }
    }
}
