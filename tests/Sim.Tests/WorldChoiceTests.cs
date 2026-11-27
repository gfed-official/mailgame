using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The host picks the world in the Lobby (doc 01 §5.3, doc 06 §5): the test street or the seed's
    /// Small Island, and the seed; the server swaps the map in, the carriers stand on its pad, the
    /// run that follows plays on it.
    /// </summary>
    public class WorldChoiceTests
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

        private static int Island => SimWorld.WorldChoices.ToList().IndexOf(WorldGenerator.SmallIsland);
        private static int Large => SimWorld.WorldChoices.ToList().IndexOf(LargeIslandGenerator.LargeIsland);

        /// <summary>Doc 01 §5.2: the Large Island opens at Postal Rank 3, the host's; --playtest-unlocks opens it whatever the rank.</summary>
        [Theory]
        [InlineData(1, false, OpResult.RankTooLow)]
        [InlineData(2, false, OpResult.RankTooLow)]
        [InlineData(3, false, OpResult.Ok)]
        [InlineData(1, true, OpResult.Ok)]
        public void TheLargeIsland_IsTheHostsFromPostalRankThree(int rank, bool unlocks, OpResult want)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false });
            var ada = world.AddPlayer("Ada")!;
            ada.Rank = rank;
            if (unlocks) ada.Playtest |= PlaytestFlags.AllUnlocks;
            Assert.Equal(want, Request(world, ada, SimRequest.SetWorld(0, Large, 7)));
            Assert.Equal(want == OpResult.Ok ? LargeIslandGenerator.LargeIsland : "test_street", world.Map.Id);
            if (want != OpResult.Ok) return;
            Assert.Equal(LargeIslandGenerator.Generate(Db, 7).MapHash, world.Map.MapHash);
            Assert.Equal("PM1-LARGE-00000007", SeedStrings.Format(Db, world.Map.Id, 7, 0));
            Assert.True(SeedStrings.TryParse(Db, "pm1-large-7", out int choice, out uint seed, out _, out _));
            Assert.Equal((Large, 7u), (choice, seed));
            // Ada stands on its pad.
            world.Map.PostOffice.Seat(ada.Slot, out float x, out float z);
            Assert.Equal((x, z), (ada.Move.X, ada.Move.Z));
        }

        [Fact]
        public void TheHostPicksTheIslandAndItsSeed_AndTheCarriersStandOnItsPad()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = true });
            var ada = world.AddPlayer("Ada")!;
            var bo = world.AddPlayer("Bo")!;
            uint oldIntake = world.Intake.Id;
            uint oldChest = world.MapChests[0].Id;
            Assert.Equal(0, world.WorldChoice);

            Assert.Equal(OpResult.NotHost, Request(world, bo, SimRequest.SetWorld(0, Island, 9)));
            Assert.Equal(OpResult.BadRequest, Request(world, ada, SimRequest.SetWorld(0, SimWorld.WorldChoices.Count, 9)));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetWorld(0, Island, 9)));
            Assert.Equal((WorldGenerator.SmallIsland, 9u), (world.Map.Id, world.Settings.Seed));
            Assert.Equal(WorldGenerator.Generate(Db, 9).MapHash, world.Map.MapHash);
            Assert.Equal(Island, world.WorldChoice);
            var changed = world.Events.OfType<WorldChangedEvent>().Single();
            Assert.Equal((WorldGenerator.SmallIsland, 9u, world.Map.MapHash), (changed.MapId, changed.Seed, changed.MapHash));
            Assert.Equal(world.MapChests.Select(c => c.Id), changed.ChestContainerIds);
            // The Intake is the same container, the chest a new one, gone from the old.
            Assert.Equal(oldIntake, world.Intake.Id);
            Assert.NotEqual(oldChest, world.MapChests[0].Id);
            Assert.Null(world.GetContainer(oldChest));
            // Everybody stands on the new pad.
            foreach (var p in world.Players)
            {
                world.Map.PostOffice.Seat(p.Slot, out float x, out float z);
                Assert.Equal((x, z), (p.Move.X, p.Move.Z));
            }

            // The run plays on it: its mail is addressed to the island's houses, the kit's Logs are in its chest.
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.Equal(OpResult.WrongPhase, Request(world, ada, SimRequest.SetWorld(0, 0, 3)));
            Assert.Equal(10, world.MapChests[0].Entries.Where(e => e.Stack.Kind == StackKind.Item).Sum(e => e.Stack.Count));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetReady(0, true)));
            Assert.Equal(OpResult.Ok, Request(world, bo, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            SimDriver.StepTicks(world, 60 * 30);
            var mail = world.Intake.Entries.Where(e => e.Stack.Kind == StackKind.Mail).ToList();
            Assert.NotEmpty(mail);
            Assert.All(mail, e => Assert.NotNull(world.Map.FindDestination(e.Stack.Address)));
            // A letter walked to its house there is delivered.
            var dest = world.Map.FindDestination(mail[0].Stack.Address)!;
            Assert.True(world.Give(ada.Hotbar, ItemStack.OfMail(TestContent.Mail("letter", dest.Address, 990001, 1))));
            Assert.True(SimDriver.WalkTo(world, ada, dest.MailboxX, dest.MailboxZ, stopDistance: 1.5f, maxTicks: 6000, sprint: true));
            var entry = ada.Hotbar.Entries.First(e => e.Stack.Kind == StackKind.Mail && e.Stack.Address == dest.Address);
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.Deliver(0, ada.Hotbar.Id, entry.EntryId, dest.Id)));
        }

        [Fact]
        public void ANewSeedForAMapFromAFile_KeepsTheMap_AndBackAgain()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 3, Raids = false, Draft = false });
            var ada = world.AddPlayer("Ada")!;
            var street = world.Map;
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetWorld(0, 0, 77)));
            Assert.Same(street, world.Map);
            Assert.Equal(77u, world.Settings.Seed);
            Assert.Single(world.Events.OfType<WorldChangedEvent>());
            // The same world and seed again: nothing to do, nothing said.
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetWorld(0, 0, 77)));
            Assert.Empty(world.Events.OfType<WorldChangedEvent>());
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetWorld(0, Island, 5)));
            Assert.Equal(OpResult.Ok, Request(world, ada, SimRequest.SetWorld(0, 0, 5)));
            Assert.Equal(street.MapHash, world.Map.MapHash);
        }
    }
}
