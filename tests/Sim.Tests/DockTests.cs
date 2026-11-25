using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Business Dock (doc 02 §3.5, doc 03 §1.1 and §2.1; design choice 114): a destination on
    /// the coast that takes Cargo only, from a vehicle standing in its unloading zone (a truck on
    /// its yard, a boat at its quay) or a belt's end, never from the hand; on the test street at
    /// the east end of the quay from shift 3.
    /// </summary>
    public class DockTests
    {
        private uint _nextRequest = 1;
        private uint _nextMail = 1;
        private const int Tps = SimConstants.TickRate;

        private static ContentDb Db => TestContent.Db;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        /// <summary>A solo run at the start of a shift's Delivery.</summary>
        private (SimWorld World, Player Host, DestinationDef Dock) InDelivery(int shift = 3)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            for (int i = 0; i < 10 * Tps && world.Run.Phase != RunPhase.Delivery; i++) world.Step();
            Assert.Equal((RunPhase.Delivery, shift), (world.Run.Phase, world.Run.Shift));
            return (world, host, world.Map.Destinations.Single(d => d.IsDock));
        }

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void CargoIsAddressedToTheDock_FromTheShiftItOpens()
        {
            var map = Db.Map("test_street");
            var dock = map.Destinations.Single(d => d.IsDock);
            Assert.False(Db.MailKind("cargo").CarryByHand);
            Assert.True(Mail.DeliveryRules.KindAccepted(Db.MailKind("cargo"), dock.Type));
            Assert.False(Mail.DeliveryRules.KindAccepted(Db.MailKind("letter"), dock.Type));

            // Shift 5 alone: Cargo in the mix (enough of the shift's value for a crate), and all of it for the dock.
            var (world, _, _) = InDelivery(5);
            int crates = 0;
            for (int i = 0; i < 300 * Tps && world.Run.Phase == RunPhase.Delivery && crates == 0; i++)
            {
                world.Step();
                if (i % Tps != 0) continue;
                foreach (var e in world.Intake.Entries)
                {
                    if (e.Stack.Kind != StackKind.Mail || e.Stack.DefIndex != Db.MailKind("cargo").Index) continue;
                    Assert.Equal(dock.Address, e.Stack.Address);
                    crates++;
                }
            }
            Assert.True(crates > 0, "no Cargo came in shift 5");
        }

        [Fact]
        public void ATruckStandingInTheZone_HasItsCargoForTheDockTakenOut_AtTheDocksRate_AndNothingElse()
        {
            var (world, host, dock) = InDelivery();
            var truck = world.AddVehicleAt(Db.Vehicle("mail_truck"), dock.MailboxX, dock.MailboxZ - 2f, 0f, host.Id);
            Assert.True(world.InDockZone(dock, truck));
            // A crate for the dock and a letter for a house, which it does not take.
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMail++, 3))));
            int wallet = world.Economy.Wallet;
            int delivered = world.Economy.ShiftDeliveries;

            SimDriver.StepTicks(world, 1);
            Assert.Equal(1, MailIn(truck.Cargo));
            Assert.Equal(delivered + 1, world.Economy.ShiftDeliveries);
            Assert.True(world.Economy.Wallet > wallet);
            Assert.Contains(world.Events, e => e is DeliveredEvent d && d.Machine == DeliveryMachine.Dock && d.DestId == dock.Id);
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(1, MailIn(truck.Cargo));

            // Two more, one after the other: half a second apart, the dock's insert rate.
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            SimDriver.StepTicks(world, 1);
            Assert.Equal(1, MailIn(truck.Cargo));
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(2, MailIn(truck.Cargo));
            SimDriver.StepTicks(world, Tps / 2);
            Assert.Equal(1, MailIn(truck.Cargo));
        }

        [Fact]
        public void OutsideTheZone_OrDrivenThrough_NothingIsTaken()
        {
            var (world, host, dock) = InDelivery();
            var truck = world.AddVehicleAt(Db.Vehicle("mail_truck"), dock.MailboxX - 8f, dock.MailboxZ - 2f, 0f, host.Id);
            Assert.False(world.InDockZone(dock, truck));
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(1, MailIn(truck.Cargo));

            // At the wheel and rolling through the zone: not yet.
            SimDriver.Teleport(host, truck.X, truck.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, truck.Id.Value)));
            for (int i = 0; i < 2 * Tps; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(1f) });
                world.Step();
                if (world.InDockZone(dock, truck)) Assert.True(host.Move.RideSpeed > 0.5f);
            }
            Assert.Equal(1, MailIn(truck.Cargo));
        }

        [Fact]
        public void AMotorBoatAtTheDocksQuay_IsUnloadedToo()
        {
            var (world, host, dock) = InDelivery();
            var boat = world.AddVehicleAt(Db.Vehicle("motorboat"), dock.MailboxX, dock.MailboxZ + 2.4f, 0f, host.Id);
            Assert.True(world.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor(boat.Z / 2f)));
            Assert.True(world.InDockZone(dock, boat));
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            SimDriver.StepTicks(world, Tps);
            Assert.Equal(0, MailIn(boat.Cargo));
        }

        [Fact]
        public void ABeltEndingIntoTheZone_FeedsTheDock_AndTheHandCannot()
        {
            var (world, host, dock) = InDelivery();
            int tx = (int)MathF.Floor(dock.MailboxX / 2f), tz = (int)MathF.Floor(dock.MailboxZ / 2f);
            world.Economy.AddLoot(500);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 2)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 2)));
            SimDriver.Teleport(host, BuildRules.TileCentre(tx - 2), BuildRules.TileCentre(tz));
            SimDriver.StepTicks(world, 12);
            // From the west, facing east into the zone's tile.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Db.Building("belt_mk1").Index, tx - 1, tz, 0)));
            world.Step();
            int before = world.Economy.ShiftDeliveries;
            Assert.True(world.PutOnBelt(world.Belts.SegmentAt(tx - 1, tz)!, TestContent.Mail("cargo", dock.Address, _nextMail++, 3)));
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(before + 1, world.Economy.ShiftDeliveries);

            // A letter in the hand at the dock: it takes none.
            var letter = TestContent.Mail("letter", dock.Address, _nextMail++, 3);
            Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(letter)));
            SimDriver.Teleport(host, dock.MailboxX - 1f, dock.MailboxZ - 1f);
            SimDriver.StepTicks(world, 12);
            var entry = host.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.WontFitHere, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, entry.EntryId, dock.Id)));
        }
    }
}
