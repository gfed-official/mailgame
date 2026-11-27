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
    /// <summary>
    /// Playtest nits 10 and 13: the stopped shift clock, the carrier parked before a mailbox with
    /// mail for it, the run that starts at a later shift, the quota that ends nothing and the raid
    /// that comes as the shift starts.
    /// </summary>
    public class PlaytestTests
    {
        private const int Tps = 30;

        private uint _nextRequest = 1;

        private static SimWorld NewWorld(bool raids = true) => new SimWorld(TestContent.Db, new SimSettings { Seed = 7, Draft = false, Raids = raids });

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
            world.SetPlaytest(a, PlaytestFlags.NoTimer | PlaytestFlags.AtMailbox);
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
            foreach (var dest in map.Houses)
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

        [Fact]
        public void AtMailboxWithRaidNow_TheParkedCarriersAreLetBe_UntilTheyWalkOffOrStrike()
        {
            // Playtest df77576: the raid holds its line 19 m from the Post Office, a few metres past
            // the mailboxes of seats 0 and 1, and downed both carriers parked there within seconds.
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            var flags = PlaytestFlags.RaidNow | PlaytestFlags.AtMailbox | PlaytestFlags.NoQuota | PlaytestFlags.NoTimer;
            world.SetPlaytest(host, flags);
            world.SetPlaytest(guest, flags);
            StartDelivery(world, host);
            Assert.True(world.Protected(host) && world.Protected(guest));

            // Every pulse comes and the raiders stand about the mailboxes; nobody is hurt or gone for.
            var waves = world.Content.Waves;
            SimDriver.StepTicks(world, (int)((5 + waves.Pulses * waves.PulseIntervalSeconds) * Tps));
            Assert.Equal(waves.Pulses, world.RaidPulsesSpawned);
            Assert.NotEmpty(world.Enemies);
            Assert.Contains(world.Enemies, e => MathF.Abs(e.X - host.Move.X) + MathF.Abs(e.Z - host.Move.Z) < 15f);
            foreach (var p in new[] { host, guest })
            {
                Assert.False(p.Dead);
                Assert.Equal(p.MaxHp, p.Hp);
                Assert.True(world.Protected(p));
                Assert.DoesNotContain(world.Enemies, e => e.Target == p.Id);
            }

            // The host strikes a raider: they are in the fight again.
            world.DamageEnemy(world.Enemies[0], 1f, host);
            Assert.False(world.Protected(host));
            // The guest walks off the spot: the same, and it does not come back by walking back.
            float x = guest.Move.X, z = guest.Move.Z;
            var off = guest.Move;
            off.X += PlaytestSpot.GuardRadius + 1f;
            guest.Move = off;
            world.Step();
            Assert.False(world.Protected(guest));
            var back = guest.Move;
            back.X = x; back.Z = z;
            guest.Move = back;
            world.Step();
            Assert.False(world.Protected(guest));
        }

        [Fact]
        public void AtMailboxWithoutARaid_NoGuard()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.AtMailbox);
            StartDelivery(world, host);
            Assert.True(host.ProtectedAtPark == false && !world.Protected(host));
        }

        [Fact]
        public void AtMailbox_AskedForInARaidUnderWay_TheCarrierIsLetBeAtTheMailbox()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.RaidNow);
            StartDelivery(world, host);
            SimDriver.StepTicks(world, 8 * Tps);
            Assert.NotEmpty(world.Enemies);
            // A guest's Hello in the middle of it.
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.AtMailbox);
            Assert.True(world.Protected(guest));
            SimDriver.StepTicks(world, 20 * Tps);
            Assert.Equal(guest.MaxHp, guest.Hp);
        }

        [Fact]
        public void AtMailbox_ARiderIsParkedInTheSaddle_TheBikeComesAlong()
        {
            // Playtest c59d497: the park at the shift's start took the rider off the bike and left it at the Post Office.
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(1000);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, TestContent.Db.ShopItem("bike").Index)));
            var bike = world.Vehicles.Last();
            SimDriver.Teleport(host, bike.X + 1f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, bike.Id.Value)));
            world.SetPlaytest(host, PlaytestFlags.AtMailbox);
            world.Events.Clear();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);

            var dest = PlaytestSpot.Mailbox(world.Map, host.Slot);
            AssertParked(world, host, dest);
            Assert.True(host.Move.Riding, "the park took the rider off the bike");
            Assert.Equal(host.Id, bike.Rider);
            Assert.DoesNotContain(world.Events, e => e is VehicleParkedEvent);
            SimDriver.StepTicks(world, 2);
            Assert.Equal((host.Move.X, host.Move.Z), (bike.X, bike.Z));
            Assert.Equal(0f, host.Move.RideSpeed);
            // It rides on from there, along the street towards its middle.
            float x0 = host.Move.X, z0 = host.Move.Z;
            var street = dest.Street.Rect;
            float mx = (street.X + street.W * 0.5f) * SimConstants.TileSize - x0, mz = (street.Y + street.H * 0.5f) * SimConstants.TileSize - z0;
            if (street.W >= street.H) mz = 0f; else mx = 0f;
            float ml = MathF.Max(1e-3f, MathF.Sqrt(mx * mx + mz * mz));
            for (int i = 0; i < Tps; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(mx / ml), MoveZ = PlayerInput.QuantizeAxis(mz / ml) });
                world.Step();
            }
            Assert.True(MathF.Abs(host.Move.X - x0) + MathF.Abs(host.Move.Z - z0) > 2f, "the parked rider does not ride on");
            Assert.True(host.Move.Riding);
        }

        [Fact]
        public void AtMailbox_OnTheIsland_EverySeatStandsFree_OnFootAndOnABike()
        {
            // Playtest c59d497: parked on the island the host stood wedged between a house and its bush.
            var db = TestContent.Db;
            var move = MoveConstants.From(db.Balance);
            float bike = db.Vehicle("bike").Radius;
            float reach = db.Balance.GetFloat("interactRange");
            for (uint seed = 1; seed <= 12; seed++)
            {
                var map = WorldGenerator.Generate(db, seed);
                for (int slot = 0; slot < 4; slot++)
                {
                    var dest = PlaytestSpot.Mailbox(map, slot);
                    PlaytestSpot.Stand(map, dest, bike, out float x, out float z);
                    float dx = dest.MailboxX - x, dz = dest.MailboxZ - z;
                    Assert.InRange(MathF.Sqrt(dx * dx + dz * dz), 0.8f, reach - 0.5f);
                    Assert.Equal(TileType.Street, map.GetTile((int)(x / SimConstants.TileSize), (int)(z / SimConstants.TileSize)));
                    foreach (float r in new[] { move.Radius, bike })
                    {
                        float rx = x, rz = z;
                        PlayerMovement.ResolveCollisions(ref rx, ref rz, map.HeightAt(x, z), map, r);
                        Assert.True(MathF.Abs(rx - x) + MathF.Abs(rz - z) < 1e-3f, $"seed {seed} seat {slot}: {map.FormatAddress(dest.Address)} pushes a radius {r} out of ({x:0.00}, {z:0.00})");
                    }
                }
            }
        }

        // --- --playtest-wallet=N ---------------------------------------------------------------------

        [Fact]
        public void Wallet_TheRunStartsWithTheMostAsked_ToppedUpNotAdded_AndNotCountedAsIncome()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(host, PlaytestFlags.None, startWallet: 300);
            world.SetPlaytest(guest, PlaytestFlags.None, startWallet: 450);
            Assert.Equal(450, world.StartWallet);
            Assert.Equal(0, world.Economy.Wallet);
            StartDelivery(world, host);
            Assert.Equal(450, world.Economy.Wallet);
            Assert.Equal((0, 0, 0), (world.Economy.TotalEarned, world.Economy.ShiftEarnings, world.Economy.TotalLoot));

            // Asked again in the run with less than the wallet holds: nothing is taken away; with more, it is topped up.
            world.SetPlaytest(host, PlaytestFlags.None, startWallet: 100);
            Assert.Equal(450, world.Economy.Wallet);
            world.SetPlaytest(host, PlaytestFlags.None, startWallet: 900);
            Assert.Equal(900, world.Economy.Wallet);
            // Never past the cap.
            world.SetPlaytest(host, PlaytestFlags.None, startWallet: int.MaxValue);
            Assert.Equal(PlaytestMask.MaxWallet, host.PlaytestWallet);
        }

        [Fact]
        public void Wallet_WithoutItARunStartsWithNothing()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            StartDelivery(world, host);
            Assert.Equal(0, world.Economy.Wallet);
        }

        // --- nit 13: --shift=N ---------------------------------------------------------------------

        [Fact]
        public void StartShift_TheRunStartsAtTheAskedShift_WithItsQuotaAndItsRaid()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.Equal(1, world.StartShift);
            world.SetPlaytest(host, PlaytestFlags.None, 2);
            Assert.Equal((PlaytestFlags.None, (byte)2), (host.Playtest, host.PlaytestShift));
            Assert.Equal(2, world.StartShift);
            // Nothing happens in the Lobby: the shift is where the next run starts.
            Assert.Equal((RunPhase.Lobby, 1), (world.Run.Phase, world.Run.Shift));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, 2), (world.Run.Phase, world.Run.Shift));
            Assert.Equal(QuotaFormula.Quota(world.Content.Balance, 2, 1), world.Run.Quota);
            Assert.Equal((byte)2, world.GetGlobalState().Shift);
            Assert.Equal(90u * Tps, world.Run.PhaseEndTick - world.Run.PhaseStartTick);

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal((RunPhase.Delivery, 2), (world.Run.Phase, world.Run.Shift));
            Assert.Equal(270u * Tps, world.Run.PhaseEndTick - world.Run.PhaseStartTick);
            // Shift 2's own raid, at its own time: the last minute and a half.
            Assert.Equal(world.Run.PhaseEndTick - 90u * Tps, world.RaidStartTick);
            Assert.Equal(RaidState.None, world.Raid);

            // The quota is missed as it would be: the run ends, and the next one starts at shift 2 again.
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            while (world.Run.Phase == RunPhase.Payday) world.Step();
            Assert.Equal((RunPhase.Results, RunOutcome.QuotaMissed), (world.Run.Phase, world.Run.Outcome));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal((RunPhase.Lobby, 1), (world.Run.Phase, world.Run.Shift));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, 2), (world.Run.Phase, world.Run.Shift));
        }

        [Fact]
        public void StartShift_TheLatestAskedWins_NeverPastTheLastShift_AndGoesWithThePlayer()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(host, PlaytestFlags.None, 2);
            world.SetPlaytest(guest, PlaytestFlags.None, 4);
            Assert.Equal(4, world.StartShift);
            world.SetPlaytest(guest, PlaytestFlags.None, 200);
            Assert.Equal(world.ShiftCount, guest.PlaytestShift);
            Assert.Equal(world.ShiftCount, world.StartShift);
            world.SetPlaytest(guest, PlaytestFlags.None, -3);
            Assert.Equal(0, guest.PlaytestShift);
            world.SetPlaytest(guest, PlaytestFlags.None, 4);

            world.RemovePlayer(guest.Id);
            Assert.Equal(2, world.StartShift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(2, world.Run.Shift);
        }

        [Fact]
        public void StartShift_AGuestJoiningInPrepMovesTheRunUp_NeverBack()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.Equal((RunPhase.Prep, 1), (world.Run.Phase, world.Run.Shift));

            world.Events.Clear();
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.None, 3);
            Assert.Equal((RunPhase.Prep, 3), (world.Run.Phase, world.Run.Shift));
            // Shift 3's Prep from its start, and everybody hears of it.
            Assert.Equal(world.Tick, world.Run.PhaseStartTick);
            Assert.Equal(QuotaFormula.Quota(world.Content.Balance, 3, 2), world.Run.Quota);
            var phase = world.Events.OfType<PhaseChangedEvent>().Last();
            Assert.Equal(((byte)3, RunPhase.Prep), (phase.Shift, phase.Phase));

            var late = world.AddPlayer("late")!;
            world.SetPlaytest(late, PlaytestFlags.None, 2);
            Assert.Equal(3, world.Run.Shift);

            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal((RunPhase.Delivery, 3), (world.Run.Phase, world.Run.Shift));
            // A shift under way is not replaced: a later ask waits for the next run.
            world.SetPlaytest(late, PlaytestFlags.None, 5);
            Assert.Equal((RunPhase.Delivery, 3), (world.Run.Phase, world.Run.Shift));
        }

        // --- nit 13: --no-quota --------------------------------------------------------------------

        [Fact]
        public void NoQuota_AMissedQuotaDoesNotEndTheRun_UntilTheLastShiftIsOver()
        {
            // Without the raids: who answers none loses the Post Office to shift 5's Tank (design choice 141).
            var world = NewWorld(raids: false);
            var host = world.AddPlayer("host")!;
            Assert.False(world.QuotaWaived);
            Assert.False(world.GetGlobalState().QuotaWaived);
            world.SetPlaytest(host, PlaytestFlags.NoQuota);
            Assert.True(world.QuotaWaived);
            Assert.True(world.GetGlobalState().QuotaWaived);
            Assert.False(world.ClockStopped);

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            for (int shift = 1; shift <= world.ShiftCount; shift++)
            {
                Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
                Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
                while (world.Run.Phase == RunPhase.Delivery) world.Step();
                Assert.Equal(RunPhase.Payday, world.Run.Phase);
                Assert.False(world.Run.LastSummary!.QuotaMet);
                while (world.Run.Phase == RunPhase.Payday) world.Step();
            }
            // Nothing was earned: the run is over after its last shift, and not as a victory.
            Assert.Equal((RunPhase.Results, RunOutcome.QuotaMissed), (world.Run.Phase, world.Run.Outcome));
            Assert.Equal(0, world.Run.ShiftsCompleted);
            Assert.Equal(0, world.Run.Results!.ShiftsCompleted);
        }

        [Fact]
        public void NoQuota_EndsWithTheTesterWhoAskedForIt()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.NoQuota);
            StartDelivery(world, host);
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            Assert.False(world.Run.LastSummary!.QuotaMet);
            // The guest leaves during Payday: the missed quota counts after all.
            world.RemovePlayer(guest.Id);
            Assert.False(world.QuotaWaived);
            while (world.Run.Phase == RunPhase.Payday) world.Step();
            Assert.Equal((RunPhase.Results, RunOutcome.QuotaMissed), (world.Run.Phase, world.Run.Outcome));
        }

        [Fact]
        public void NoQuota_AMetQuotaStillCounts_AndTheLastOneIsAVictory()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoQuota, world.ShiftCount);
            StartDelivery(world, host);
            Assert.Equal(world.ShiftCount, world.Run.Shift);
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, 800000, (byte)world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(host.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(host, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Deliver(0, host.Hotbar.Id, host.Hotbar.Entries.Single().EntryId, home.Id)));
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            Assert.True(world.Run.LastSummary!.QuotaMet);
            while (world.Run.Phase == RunPhase.Payday) world.Step();
            Assert.Equal((RunPhase.Results, RunOutcome.Victory), (world.Run.Phase, world.Run.Outcome));
            Assert.Equal(1, world.Run.ShiftsCompleted);
        }

        // --- nit 13: --raid ------------------------------------------------------------------------

        [Fact]
        public void RaidNow_TheRaidComesFiveSecondsIntoTheShift_AlsoInShiftOneWhichHasNone()
        {
            var world = NewWorld();
            var waves = world.Content.Waves;
            var host = world.AddPlayer("host")!;
            Assert.False(world.RaidsAtShiftStart);
            world.SetPlaytest(host, PlaytestFlags.RaidNow);
            Assert.True(world.RaidsAtShiftStart);
            Assert.Equal(0, waves.BudgetFor(1));

            StartDelivery(world, host);
            uint start = world.Run.PhaseStartTick;
            Assert.Equal(start + 5u * Tps, world.RaidStartTick);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(RaidState.Warning, world.Raid);
            var warned = world.Events.OfType<RaidChangedEvent>().Last();
            Assert.Equal((RaidState.Warning, world.RaidStartTick), (warned.Raid, warned.Tick));
            Assert.Empty(world.Enemies);

            while (world.Tick < world.RaidStartTick) world.Step();
            world.Step();
            Assert.Equal(RaidState.Active, world.Raid);
            // Shift 1 has no raid of its own: it borrows shift 2's budget and shift 2's raiders.
            Assert.Equal(SimWorld.WaveBudget(waves, 2, 1, world.Economy.Complaint), world.RaidBudget);
            Assert.Equal(1, world.RaidPulsesSpawned);
            Assert.NotEmpty(world.Enemies);
            var allowed = waves.WeightsFor(2).Select(w => w.Key.Id).ToList();
            Assert.All(world.Enemies, e => Assert.Contains(e.Def.Id, allowed));

            // The pulses come at their usual pace, all of them long before the shift ends.
            SimDriver.StepTicks(world, (int)((waves.Pulses - 1) * waves.PulseIntervalSeconds * Tps));
            Assert.Equal(waves.Pulses, world.RaidPulsesSpawned);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.EnemiesSpawnedTotal >= waves.Pulses);
            // The shift's clock runs as ever and its end breaks the raid off.
            Assert.Equal(start + 240u * Tps, world.Run.PhaseEndTick);
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            Assert.Equal(RaidState.Fleeing, world.Raid);
            while (world.Run.Phase == RunPhase.Payday) world.Step();
            Assert.Equal(RaidState.None, world.Raid);
            Assert.Empty(world.Enemies);
        }

        [Fact]
        public void RaidNow_AShiftWithItsOwnRaidHasItEarly_WithItsOwnBudget()
        {
            var world = NewWorld();
            var waves = world.Content.Waves;
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.RaidNow, 3);
            StartDelivery(world, host);
            Assert.Equal(3, world.Run.Shift);
            Assert.Equal(world.Run.PhaseStartTick + 5u * Tps, world.RaidStartTick);
            while (world.Raid != RaidState.Active) world.Step();
            Assert.Equal(SimWorld.WaveBudget(waves, 3, 1, world.Economy.Complaint), world.RaidBudget);
        }

        [Fact]
        public void RaidNow_ComesAlsoWhenTheClockIsStopped_AndTheClockStaysStopped()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.RaidNow | PlaytestFlags.NoTimer);
            StartDelivery(world, host);
            uint left = world.Run.PhaseEndTick - world.Tick;
            uint raid = world.RaidStartTick;
            SimDriver.StepTicks(world, 6 * Tps);
            Assert.Equal(raid, world.RaidStartTick);
            Assert.Equal(RaidState.Active, world.Raid);
            Assert.NotEmpty(world.Enemies);
            Assert.Equal(left, world.Run.PhaseEndTick - world.Tick);
            // The host ends the shift by hand, and the raid with it.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal((RunPhase.Payday, RaidState.Fleeing), (world.Run.Phase, world.Raid));
        }

        [Fact]
        public void RaidNow_AskedInAShiftUnderWay_ComesFiveSecondsLater()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, QuickStart = true });
            var host = world.AddPlayer("host")!;
            SimDriver.StepTicks(world, 20 * Tps);
            Assert.Equal((RunPhase.Delivery, 1, RaidState.None), (world.Run.Phase, world.Run.Shift, world.Raid));
            Assert.Equal(0u, world.RaidStartTick);

            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.RaidNow);
            Assert.Equal(world.Tick + 5u * Tps, world.RaidStartTick);
            uint due = world.RaidStartTick;
            SimDriver.StepTicks(world, 5 * Tps + 1);
            Assert.Equal(RaidState.Active, world.Raid);
            // A raid under way is not started over by a second tester.
            var third = world.AddPlayer("third")!;
            world.SetPlaytest(third, PlaytestFlags.RaidNow);
            Assert.Equal(due, world.RaidStartTick);
            Assert.Equal(host.Id, world.Run.Host);
        }

        [Fact]
        public void RaidNow_WithRaidsSwitchedOff_NothingComes()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.RaidNow);
            StartDelivery(world, host);
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.Equal((0u, RaidState.None), (world.RaidStartTick, world.Raid));
        }

        // --- nit 18: --playtest-package ---------------------------------------------------------

        private static int Packages(SimWorld world, Player p) =>
            p.Hotbar.Entries.Concat(p.Inventory.Entries).Where(e => e.Stack.Kind == StackKind.Mail && world.Content.MailKinds[e.Stack.DefIndex].CannonAmmo).Sum(e => e.Stack.Count);

        private static void Fire(SimWorld world, Player p)
        {
            world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, Yaw = p.Move.Yaw, Buttons = InputButtons.AltAttack });
            world.Step();
        }

        [Fact]
        public void Packages_TheShiftStartsWithFourForTheCannon_AndOnlyForWhoAsked()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.Packages);
            // The Lobby and Prep have no mail (doc 01 §1).
            Assert.Equal(0, Packages(world, guest));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, 0), (world.Run.Phase, Packages(world, guest)));
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);

            Assert.Equal(4, PlaytestMask.PackageCount);
            Assert.Equal((0, 4), (Packages(world, host), Packages(world, guest)));
            Assert.False(world.HasAmmo(host));
            Assert.True(world.HasAmmo(guest));
            // Nobody was moved for it: that is --playtest-mailbox.
            Assert.DoesNotContain(world.Events, e => e is PlayerParkedEvent);

            // They are what the cannon fires: one shot, one package less.
            Fire(world, guest);
            var shot = Assert.Single(world.Events.OfType<AttackEvent>());
            Assert.Equal((guest.Id, Weapon.Cannon), (shot.Player, shot.Weapon));
            Assert.Equal(3, Packages(world, guest));

            // And they are real mail with a real address: a house takes one and pays for it.
            var entry = guest.Hotbar.Entries.Concat(guest.Inventory.Entries).First(e => e.Stack.Kind == StackKind.Mail);
            var container = guest.Hotbar.Entries.Contains(entry) ? guest.Hotbar : guest.Inventory;
            var dest = world.Map.Destinations.Single(d => d.Address == entry.Stack.Address);
            SimDriver.Teleport(guest, dest.MailboxX, dest.MailboxZ + (dest.MailboxZ < 28f ? 1.2f : -1.2f));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, guest, SimRequest.Deliver(0, container.Id, entry.EntryId, dest.Id)));
            Assert.Equal((1, 0), (guest.Deliveries, guest.Misdeliveries));
            Assert.Equal(2, Packages(world, guest));
        }

        [Fact]
        public void Packages_AreToppedUpWhenTheNextShiftStarts_AndWhenTheCarrierIsBackOnTheirFeet()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.Packages | PlaytestFlags.NoQuota);
            StartDelivery(world, host);
            Fire(world, host);
            SimDriver.StepTicks(world, 3 * Tps);
            Fire(world, host);
            Assert.Equal(2, Packages(world, host));

            // Down: the two left are in the bag. Up again: four new ones, and five seconds to aim.
            world.DamagePlayer(host, 9999f, (byte)world.Content.Enemy("barbarian").Index);
            Assert.True(host.Dead);
            Assert.Equal(0, Packages(world, host));
            while (host.Dead) world.Step();
            Assert.Equal(4, Packages(world, host));
            Assert.True(world.Protected(host));

            // The next shift tops up what was fired, and gives no more than four.
            Fire(world, host);
            Assert.Equal(3, Packages(world, host));
            while (world.Run.Phase == RunPhase.Delivery) world.Step();
            Assert.Equal(3, Packages(world, host));
            while (world.Run.Phase != RunPhase.Prep) world.Step();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal((RunPhase.Delivery, 2), (world.Run.Phase, world.Run.Shift));
            Assert.Equal(4, Packages(world, host));
        }

        [Fact]
        public void Packages_AskedInAShiftUnderWay_AreThereAtOnce()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, QuickStart = true });
            var host = world.AddPlayer("host")!;
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.Packages);
            Assert.Equal((0, 4), (Packages(world, host), Packages(world, guest)));
            // Asking twice gives no more.
            world.SetPlaytest(guest, PlaytestFlags.Packages);
            Assert.Equal(4, Packages(world, guest));
        }

        [Fact]
        public void Packages_GoWithTheMailboxAndTheRaid_SoTheCannonCanBeTriedInOne()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.Packages | PlaytestFlags.AtMailbox | PlaytestFlags.RaidNow);
            StartDelivery(world, host);
            var dest = PlaytestSpot.Mailbox(world.Map, host.Slot);
            AssertParked(world, host, dest);
            // The letters for the mailbox and the packages for the cannon, side by side.
            Assert.Equal(4, Packages(world, host));
            Assert.Equal(PlaytestSpot.MailCount + 4, MailFor(host, dest.Address));
            Assert.Equal(world.Run.PhaseStartTick + 5u * Tps, world.RaidStartTick);

            // The raid is there five seconds later, and a package out of the cannon fells or hurts who it hits.
            while (world.Enemies.Count == 0) world.Step();
            var raider = world.Enemies[0];
            SimDriver.Teleport(host, raider.X - 8f, raider.Z);
            world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, Yaw = PlayerInput.QuantizeYaw(-MathF.PI / 2f), Buttons = InputButtons.AltAttack });
            world.Step();
            Assert.Equal(3, Packages(world, host));
            float hp = raider.Hp;
            SimDriver.StepTicks(world, Tps);
            Assert.True(raider.Hp < hp || !world.Enemies.Contains(raider), "the package missed a raider eight metres down the street");
        }

        // --- the f40acb7 playtest: --playtest-materials ------------------------------------------

        private static (int Logs, int Stone) Materials(SimWorld world, Player p) =>
            (BuildRules.Have(p.Hotbar, p.Inventory, world.Content.Item("log")), BuildRules.Have(p.Hotbar, p.Inventory, world.Content.Item("stone")));

        [Fact]
        public void Materials_LogsAndStoneInPrep_ToppedUpWhenBackOnTheirFeet_AndOnlyForWhoAsked()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            world.SetPlaytest(guest, PlaytestFlags.Materials | PlaytestFlags.NoQuota);
            var before = Materials(world, host);
            // Nothing in the Lobby, where nothing is built.
            Assert.Equal((0, 0), Materials(world, guest));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.Equal(new[] { ("log", 20), ("stone", 40) }, PlaytestMask.Materials);
            Assert.Equal((20, 40), Materials(world, guest));
            Assert.Equal(before, Materials(world, host));
            // In the inventory, the hotbar left to tools and mail.
            Assert.DoesNotContain(guest.Hotbar.Entries, e => e.Stack.Kind == StackKind.Item && (world.Content.Items[e.Stack.DefIndex].Id is "log" or "stone"));

            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            // Topped up, not doubled.
            Assert.Equal((20, 40), Materials(world, guest));
            world.DamagePlayer(guest, 9999f, (byte)world.Content.Enemy("barbarian").Index);
            Assert.Equal((0, 0), Materials(world, guest));
            while (guest.Dead) world.Step();
            Assert.Equal((20, 40), Materials(world, guest));
        }

        [Fact]
        public void Materials_AKnockdownsBagReturn_DoesNotAddThemAgain()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.Materials | PlaytestFlags.NoQuota | PlaytestFlags.NoTimer);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            var barbarian = (byte)world.Content.Enemy("barbarian").Index;
            // The e5be644 playtest: 200 → 396 → 588 … shots over repeated knockdowns. Each bag's return is the flag's top-up no more.
            for (int round = 0; round < 3; round++)
            {
                while (world.Protected(host)) world.Step();
                world.DamagePlayer(host, 9999f, barbarian);
                Assert.True(host.Dead);
                while (host.Dead) world.Step();
                Assert.Equal((20, 40), Materials(world, host));
                while (world.Bags.Count > 0) world.Step();
                Assert.Equal((20, 40), Materials(world, host));
            }
            // What they held beyond the flag's amount comes back with the bag.
            var stone = world.Content.Item("stone");
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(stone, 15)));
            Assert.Equal((20, 55), Materials(world, host));
            while (world.Protected(host)) world.Step();
            world.DamagePlayer(host, 9999f, barbarian);
            Assert.True(host.Dead);
            Assert.Equal(1, world.Bags.Count);
            Assert.Equal((0, 0), Materials(world, host));
            while (host.Dead) world.Step();
            Assert.Equal((20, 40), Materials(world, host));
            while (world.Bags.Count > 0) world.Step();
            Assert.Equal((20, 55), Materials(world, host));
        }

        [Fact]
        public void Materials_BuildAWoodenWall()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.Materials);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            var recipe = world.Content.Recipes.Single(r => r.Id == "recipe_wall_wood");
            Assert.True(BuildRules.HasMaterials(recipe, host.Hotbar, host.Inventory));
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
