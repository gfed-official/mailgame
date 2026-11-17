using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>Playtest nit 10: the stopped shift clock and the carrier parked before a mailbox with mail for it.</summary>
    public class PlaytestTests
    {
        private const int Tps = 30;

        private uint _nextRequest = 1;

        private static SimWorld NewWorld() => new SimWorld(TestContent.Db, new SimSettings { Seed = 7 });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private void StartDelivery(SimWorld world, Player host)
        {
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        private static int MailFor(Player p, AddressId address) =>
            p.Hotbar.Entries.Concat(p.Inventory.Entries).Where(e => e.Stack.Kind == StackKind.Mail && e.Stack.Address == address).Sum(e => e.Stack.Count);

        [Fact]
        public void WithoutTheFlagsNothingChanges()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None);
            Assert.False(world.ClockStopped);
            Assert.False(world.GetGlobalState().ClockStopped);
            var po = world.Map.PostOffice;
            Assert.InRange(MathF.Abs(host.Move.X - po.SpawnPadX) + MathF.Abs(host.Move.Z - po.SpawnPadZ), 0f, 6f);
            StartDelivery(world, host);
            Assert.Equal(0, host.Hotbar.EntryCount + host.Inventory.EntryCount);
            Assert.DoesNotContain(world.Events, e => e is PlayerParkedEvent);
        }

        [Fact]
        public void NoTimer_TheShiftClockStandsAndTheHostEndsTheShift()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            // The guest asks; the clock is everybody's.
            world.SetPlaytest(guest, (PlaytestFlags)0xFF);
            Assert.Equal(PlaytestMask.All, guest.Playtest);
            world.SetPlaytest(guest, PlaytestFlags.NoTimer);
            Assert.True(world.ClockStopped);
            Assert.True(world.GetGlobalState().ClockStopped);

            // Prep keeps its clock: it is the shift that must not run out under a tester.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            uint prepEnd = world.Run.PhaseEndTick;
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(prepEnd, world.Run.PhaseEndTick);
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);

            uint left = world.Run.PhaseEndTick - world.Tick;
            Assert.InRange(left, 240u * Tps - 1, 240u * Tps);
            // Twice the length of the shift: no Payday, no raid, and the mail still comes.
            for (int i = 0; i < 2 * 240 * Tps; i++)
            {
                world.Step();
                Assert.Equal(left, world.Run.PhaseEndTick - world.Tick);
            }
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.Intake.EntryCount > 0);
            Assert.Equal(world.Run.PhaseEndTick, world.GetGlobalState().PhaseEndTick);

            // Only the host ends such a shift, with the request that leaves Results.
            Assert.Equal(OpResult.NotHost, Request(world, guest, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            Assert.NotNull(world.Run.LastSummary);
        }

        [Fact]
        public void NoTimer_TheRaidWaitsWithTheClock()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoTimer);
            ReachShiftTwo(world, host);

            // Shift 2 has a raid in its last minute and a half; that minute never comes.
            uint untilRaid = world.RaidStartTick - world.Tick;
            Assert.InRange(untilRaid, (270u - 90u) * Tps - 1, (270u - 90u) * Tps);
            for (int i = 0; i < 2 * 270 * Tps; i++)
            {
                world.Step();
                Assert.Equal(untilRaid, world.RaidStartTick - world.Tick);
            }
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(RaidState.None, world.Raid);
            Assert.Empty(world.Enemies);
        }

        /// <summary>Shift 1 on quota, ended by the host's key, and on into the Delivery of shift 2.</summary>
        private void ReachShiftTwo(SimWorld world, Player host)
        {
            StartDelivery(world, host);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, 800000, 1);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(host, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, host.Hotbar.Entries.Single().EntryId, home.Id)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Payday, world.Run.Phase);
            while (world.Run.Phase == RunPhase.Payday) world.Step();
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal((RunPhase.Delivery, 2), (world.Run.Phase, world.Run.Shift));
        }

        [Fact]
        public void NoTimer_ARunningShiftCannotBeEndedByHand()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            StartDelivery(world, host);
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        [Fact]
        public void NoTimer_TheClockRunsAgainWhenTheTesterLeaves()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.NoTimer);
            ReachShiftTwo(world, host);
            SimDriver.StepTicks(world, 60 * Tps);
            uint left = world.Run.PhaseEndTick - world.Tick;
            Assert.InRange(left, 270u * Tps - 1, 270u * Tps);

            world.RemovePlayer(guest.Id);
            Assert.False(world.ClockStopped);
            uint end = world.Run.PhaseEndTick;
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.Equal(end, world.Run.PhaseEndTick);
            // With the whole shift still on the clock the raid comes at its usual time before the end.
            while (world.Run.Phase == RunPhase.Delivery && world.Raid == RaidState.None) world.Step();
            Assert.Equal(RaidState.Warning, world.Raid);
            Assert.InRange((int)(end - world.Tick), 90 * Tps, 106 * Tps);
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            Assert.Equal(end, world.Tick);
        }

        [Fact]
        public void AtMailbox_ParkedWhenJoining_WithMailOnlyOnceTheShiftStarts()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.AtMailbox);
            Assert.False(world.ClockStopped);

            var dest = PlaytestSpot.Mailbox(world.Map, host.Slot);
            Assert.Same(world.Map.Destinations[0], dest);
            AssertParked(world, host, dest);
            var parked = Assert.Single(world.Events.OfType<PlayerParkedEvent>());
            Assert.Equal((host.Id, dest.Id), (parked.Audience, parked.Destination));
            // The Lobby has no mail (doc 01 §1).
            Assert.Equal(0, host.Hotbar.EntryCount + host.Inventory.EntryCount);

            // Wander off in the Lobby; the shift puts the carrier back, now with mail for the box.
            Assert.True(SimDriver.WalkTo(world, host, host.Move.X + 6f, host.Move.Z + (dest.MailboxZ < host.Move.Z ? 1f : -1f), 0.4f));
            world.Events.Clear();
            StartDelivery(world, host);
            AssertParked(world, host, dest);
            Assert.Equal(PlaytestSpot.MailCount, MailFor(host, dest.Address));
            Assert.Equal(PlaytestSpot.MailCount, host.Hotbar.Entries.Sum(e => e.Stack.Count));
            Assert.Contains(world.Events.OfType<PlayerParkedEvent>(), e => e.Audience == host.Id && e.Destination == dest.Id);

            // It is real mail: the box takes it and pays for it.
            var entry = host.Hotbar.Entries.First();
            int count = entry.Stack.Count;
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, entry.EntryId, dest.Id)));
            Assert.Equal(count, host.Deliveries);
            Assert.Equal(0, host.Misdeliveries);
            Assert.True(world.Economy.ShiftEarnings > 0);
        }

        [Fact]
        public void AtMailbox_EverySeatHasItsOwnBox_AndNobodyElseIsMoved()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            float hostX = host.Move.X, hostZ = host.Move.Z;
            world.SetPlaytest(a, PlaytestMask.All);
            world.SetPlaytest(b, PlaytestFlags.AtMailbox);

            var destA = PlaytestSpot.Mailbox(world.Map, a.Slot);
            var destB = PlaytestSpot.Mailbox(world.Map, b.Slot);
            Assert.NotSame(destA, destB);
            AssertParked(world, a, destA);
            AssertParked(world, b, destB);
            Assert.Equal((hostX, hostZ), (host.Move.X, host.Move.Z));

            StartDelivery(world, host);
            AssertParked(world, a, destA);
            AssertParked(world, b, destB);
            Assert.Equal(PlaytestSpot.MailCount, MailFor(a, destA.Address));
            Assert.Equal(PlaytestSpot.MailCount, MailFor(b, destB.Address));
            Assert.Equal(0, MailFor(a, destB.Address));
            Assert.Equal(0, host.Hotbar.EntryCount + host.Inventory.EntryCount);
            // Mail ids of a playtest never meet the spawner's.
            var ids = a.Hotbar.Entries.Concat(b.Hotbar.Entries).SelectMany(e => e.Stack.Mail).Select(m => m.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.True(id >= 0xF0000000));
        }

        [Fact]
        public void AtMailbox_EveryMailboxOfTheStreetHasAFreeSpotInReach()
        {
            var map = TestContent.Db.Map("test_street");
            var move = MoveConstants.From(TestContent.Db.Balance);
            float reach = TestContent.Db.Balance.GetFloat("interactRange");
            foreach (var dest in map.Destinations)
            {
                PlaytestSpot.Stand(map, dest, move.Radius, out float x, out float z);
                float dx = dest.MailboxX - x, dz = dest.MailboxZ - z;
                Assert.InRange(MathF.Sqrt(dx * dx + dz * dz), 0.8f, reach - 0.5f);
                // Standing there is standing still: nothing pushes the carrier out.
                float rx = x, rz = z;
                PlayerMovement.ResolveCollisions(ref rx, ref rz, map.HeightAt(x, z), map, move.Radius);
                Assert.Equal((x, z), (rx, rz));
                Assert.Equal(TileType.Street, map.GetTile((int)(x / SimConstants.TileSize), (int)(z / SimConstants.TileSize)));
            }
        }

        private static void AssertParked(SimWorld world, Player p, DestinationDef dest)
        {
            float dx = dest.MailboxX - p.Move.X, dz = dest.MailboxZ - p.Move.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            Assert.InRange(dist, 0.8f, world.Content.Balance.GetFloat("interactRange") - 0.5f);
            // Yaw 0 looks along −Z: the body faces the box.
            float yaw = p.Move.Yaw / 65536f * MathF.Tau;
            float fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
            Assert.True((fx * dx + fz * dz) / dist > 0.99f, $"{p.Name} looks past the mailbox");
            Assert.True(p.Move.Grounded);
        }
    }
}
