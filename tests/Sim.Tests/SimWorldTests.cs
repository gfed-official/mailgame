using System.Linq;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>The M0 loop driven straight through SimWorld: spawn → pickup → walk → deliver → wallet.</summary>
    public class SimWorldTests
    {
        // QuickStart: Delivery of shift 1 begins as soon as a player is present (RunLoopTests covers the real phases).
        private static SimWorld NewWorld(uint seed = 7) =>
            new SimWorld(TestContent.Db, new SimSettings { Seed = seed, QuickStart = true });

        private static OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Single(e => e.RequestId == request.RequestId).Result;
        }

        private static void WaitForMail(SimWorld world)
        {
            for (int i = 0; i < 30 * 30 && world.Intake.EntryCount == 0; i++) world.Step();
            Assert.True(world.Intake.EntryCount > 0, "no mail spawned in 30 s");
        }

        /// <summary>Walks to the Intake and quick-moves the first letter stack onto the body.</summary>
        private static GridEntry PickUpLetters(SimWorld world, Player p)
        {
            Assert.True(SimDriver.WalkTo(world, p, world.Map.PostOffice.IntakeX + 1f, world.Map.PostOffice.IntakeZ, 0.5f));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Open(1, world.Intake.Id)));
            ushort letter = world.Content.MailKind("letter").Index;
            var entry = world.Intake.Entries.Where(e => e.Stack.DefIndex == letter).OrderBy(e => e.EntryId).First();
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.QuickMove(2, world.Intake.Id, entry.EntryId)));
            var carried = p.Hotbar.Entries.Single();
            Assert.Equal(entry.Stack.Address, carried.Stack.Address);
            return carried;
        }

        [Fact]
        public void QuickStart_FirstPlayerStartsTheDeliveryClock()
        {
            var world = NewWorld();
            world.Step();
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            world.AddPlayer("a");
            world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(600, world.Run.Quota);
            Assert.Equal(world.Tick + 240 * 30, world.Run.PhaseEndTick);
        }

        [Fact]
        public void MailSpawnsIntoTheIntake()
        {
            var world = NewWorld();
            world.AddPlayer("a");
            WaitForMail(world);
            Assert.All(world.Intake.Entries, e => Assert.Equal(StackKind.Mail, e.Stack.Kind));
            Assert.All(world.Intake.Entries, e => Assert.NotNull(world.Map.FindDestination(e.Stack.Address)));
        }

        [Fact]
        public void PickUpWalkDeliverPaysTheWallet()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            WaitForMail(world);
            var carried = PickUpLetters(world, p);
            int count = carried.Stack.Count;
            int value = carried.Stack.Mail.Sum(m => m.Value);
            var dest = world.Map.FindDestination(carried.Stack.Address)!;

            // Too far from the mailbox: refused, nothing changes.
            Assert.Equal(OpResult.OutOfRange, Request(world, p, SimRequest.Deliver(3, p.Hotbar.Id, carried.EntryId, dest.Id)));
            Assert.Equal(0, world.Economy.Wallet);

            Assert.True(SimDriver.WalkTo(world, p, dest.MailboxX, dest.MailboxZ, 1.5f));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Deliver(4, p.Hotbar.Id, carried.EntryId, dest.Id)));

            Assert.Equal(value, world.Economy.Wallet);
            Assert.Equal(value, world.Economy.ShiftEarnings);
            Assert.Equal(count, world.Economy.ShiftDeliveries);
            Assert.Equal(count, world.Events.OfType<DeliveredEvent>().Count());
            Assert.Equal(0, p.Hotbar.EntryCount);
            Assert.Equal(value, world.GetGlobalState().Wallet);
        }

        [Fact]
        public void WrongMailboxDeductsThePenalty()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            WaitForMail(world);
            var carried = PickUpLetters(world, p);
            int count = carried.Stack.Count;
            int penalty = carried.Stack.Mail.Sum(m => m.Value / 2);
            var wrong = world.Map.Destinations.First(d => d.Address != carried.Stack.Address);

            Assert.True(SimDriver.WalkTo(world, p, wrong.MailboxX, wrong.MailboxZ, 1.5f));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Deliver(3, p.Hotbar.Id, carried.EntryId, wrong.Id)));

            Assert.Equal(-penalty, world.Economy.Wallet);
            Assert.Equal(count, world.Economy.ShiftMisdeliveries);
            Assert.Equal(count, world.Events.OfType<MisdeliveredEvent>().Count());
            Assert.True(world.Economy.Complaint > 0);
        }

        [Fact]
        public void ContainerDeltasKeepAMirrorInSync()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            WaitForMail(world);
            Assert.True(SimDriver.WalkTo(world, p, world.Map.PostOffice.IntakeX + 1f, world.Map.PostOffice.IntakeZ, 0.5f));

            world.Events.Clear();
            world.Submit(p.Id, SimRequest.Open(1, world.Intake.Id));
            world.Step();
            var mirror = new GridContainer(world.Intake.Id, world.Intake.Def);
            mirror.Load(world.Events.OfType<ContainerStateEvent>().Single().State);
            Assert.Equal(world.Intake.ComputeHash(), mirror.ComputeHash());

            // Keep the Intake open across several spawn batches and a few pickups.
            uint req = 10;
            for (int i = 0; i < 30 * 40; i++)
            {
                world.Events.Clear();
                if (i % 200 == 0 && world.Intake.EntryCount > 0)
                    world.Submit(p.Id, SimRequest.QuickMove(req++, world.Intake.Id, world.Intake.Entries.First().EntryId));
                world.Step();
                foreach (var e in world.Events.OfType<ContainerDeltaEvent>())
                {
                    if (e.Audience != p.Id || e.Delta.ContainerId != mirror.Id) continue;
                    Assert.True(mirror.Apply(e.Delta));
                    Assert.Equal(e.Delta.Hash, mirror.ComputeHash());
                }
            }
            Assert.Equal(world.Intake.Version, mirror.Version);
            Assert.True(mirror.Version > 3);
        }

        [Fact]
        public void LeavingReturnsCarriedMailToTheIntake()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            WaitForMail(world);
            PickUpLetters(world, p);
            int before = world.Intake.Entries.Sum(e => e.Stack.Count);
            int carried = p.Hotbar.Entries.Sum(e => e.Stack.Count);
            world.RemovePlayer(p.Id);
            Assert.Equal(before + carried, world.Intake.Entries.Sum(e => e.Stack.Count));
            Assert.Empty(world.Players);
        }
    }
}
