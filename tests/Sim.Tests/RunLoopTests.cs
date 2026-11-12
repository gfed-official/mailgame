using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>The run state machine of doc 01: Lobby → Prep → Delivery → Payday → … → Results → Lobby.</summary>
    public class RunLoopTests
    {
        private const int Tps = 30;

        private uint _nextRequest = 1;
        private uint _nextMailId = 500000;

        private static SimWorld NewWorld(uint seed = 7) => new SimWorld(TestContent.Db, new SimSettings { Seed = seed });

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

        private void StartDelivery(SimWorld world, Player host)
        {
            if (world.Run.Phase == RunPhase.Lobby) Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            foreach (var p in world.Players) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        /// <summary>Hands the player one item worth <paramref name="value"/> ¢ and has them put it in a mailbox.</summary>
        private OpResult Deliver(SimWorld world, Player p, int value, bool wrong = false, string kind = "letter", int deadlineShift = 0)
        {
            var home = world.Map.Destinations[0];
            var target = wrong ? world.Map.Destinations[1] : home;
            var item = TestContent.Mail(kind, home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)value;
            if (deadlineShift > 0) item.DeadlineShift = (byte)deadlineShift;
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(p, target.MailboxX, target.MailboxZ);
            SimDriver.StepTicks(world, 15); // past the minimum time between two inserts
            var entry = p.Hotbar.Entries.Single();
            var result = Request(world, p, SimRequest.Deliver(0, p.Hotbar.Id, entry.EntryId, target.Id));
            if (result != OpResult.Ok) p.Hotbar.Remove(entry.EntryId);
            return result;
        }

        [Fact]
        public void LobbyWaitsForTheHostToStart()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            SimDriver.StepTicks(world, 20 * Tps);
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Equal(0u, world.Run.PhaseEndTick);
            Assert.Equal(host.Id, world.Run.Host);
            Assert.Equal(host.Id, world.GetGlobalState().Host);
            Assert.Equal(0, world.Intake.EntryCount);

            Assert.Equal(OpResult.NotHost, Request(world, guest, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);

            world.Events.Clear();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.Equal(1, world.Run.Shift);
            Assert.Equal(1, world.Run.RunNumber);
            Assert.Equal(world.Tick + 60 * Tps, world.Run.PhaseEndTick);
            var changed = world.Events.OfType<PhaseChangedEvent>().Single();
            Assert.Equal((RunPhase.Prep, world.Run.PhaseEndTick), (changed.Phase, changed.PhaseEndTick));

            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.StartRun(0)));
        }

        [Fact]
        public void PrepEndsOnItsClock_AndNoMailSpawnsBeforeDelivery()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Request(world, host, SimRequest.StartRun(0));
            uint end = world.Run.PhaseEndTick;
            RunUntil(world, () => world.Tick == end - 1, 61);
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.Equal(0, world.Intake.EntryCount);
            world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(world.Tick + 240 * Tps, world.Run.PhaseEndTick);
            Assert.Equal(600, world.Run.Quota);
            RunUntil(world, () => world.Intake.EntryCount > 0, 30);
        }

        [Fact]
        public void PrepEndsEarlyOnceEveryPlayerIsReady()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            Request(world, a, SimRequest.StartRun(0));

            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.SetReady(0, true)));
            SimDriver.StepTicks(world, 30);
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.True(a.Ready);

            // Un-readying counts: both must be ready at the same time.
            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.SetReady(0, false)));
            Assert.Equal(OpResult.Ok, Request(world, b, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.False(a.Ready);
            Assert.False(b.Ready);

            Assert.Equal(OpResult.WrongPhase, Request(world, a, SimRequest.SetReady(0, true)));
        }

        [Fact]
        public void MailboxesOnlyTakeMailDuringDelivery()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            Assert.Equal(OpResult.WrongPhase, Deliver(world, p, 100));
            Request(world, p, SimRequest.StartRun(0));
            Assert.Equal(OpResult.WrongPhase, Deliver(world, p, 100));
            StartDelivery(world, p);
            Assert.Equal(OpResult.Ok, Deliver(world, p, 100));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            Assert.Equal(OpResult.WrongPhase, Deliver(world, p, 100));
            Assert.Equal(100, world.Run.LastSummary!.Earned);
        }

        [Fact]
        public void PaydayChecksTheQuotaAndSummarisesTheShift()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartDelivery(world, a);
            int quota = world.Run.Quota;
            Assert.Equal(QuotaFormula.Quota(world.Content.Balance, 1, 2), quota);

            Assert.Equal(OpResult.Ok, Deliver(world, a, 500));
            Assert.Equal(OpResult.Ok, Deliver(world, b, 300, kind: "small"));
            Assert.Equal(OpResult.Ok, Deliver(world, b, 200, kind: "small"));
            int earned = 1000;
            Assert.True(earned >= quota);

            world.Events.Clear();
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            var s = world.Events.OfType<ShiftSummaryEvent>().Single().Summary;
            Assert.Same(s, world.Run.LastSummary);
            Assert.Equal(1, s.Shift);
            Assert.Equal(earned, s.Earned);
            Assert.Equal(quota, s.Quota);
            Assert.True(s.QuotaMet);
            Assert.Equal(100, s.Bonus); // +10 % for a shift without a misdelivery
            Assert.Equal(1, s.DeliveriesByKind[world.Content.MailKind("letter").Index]);
            Assert.Equal(2, s.DeliveriesByKind[world.Content.MailKind("small").Index]);
            Assert.Equal(3, s.Deliveries);
            Assert.Equal(0, s.Misdeliveries);
            Assert.Equal(b.Id, s.Mvp);
            Assert.Equal(2, s.MvpDeliveries);
            Assert.Equal(earned + 100, world.Economy.Wallet);
            Assert.Equal(1, world.Run.ShiftsCompleted);
            Assert.Equal(world.Tick + 20 * Tps, world.Run.PhaseEndTick);

            // The surplus stays in the wallet and the next shift starts with its own Prep.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            Assert.Equal(2, world.Run.Shift);
            Assert.Equal(world.Tick + 90 * Tps, world.Run.PhaseEndTick);
            Assert.Equal(QuotaFormula.Quota(world.Content.Balance, 2, 2), world.Run.Quota);
            Assert.Equal(0, world.Economy.ShiftEarnings);
            Assert.Equal(earned + 100, world.Economy.Wallet);
        }

        [Fact]
        public void AMisdeliveryForfeitsTheShiftBonus_AndLateMailIsCounted()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            StartDelivery(world, p);
            Assert.Equal(OpResult.Ok, Deliver(world, p, 800));
            Assert.Equal(OpResult.Ok, Deliver(world, p, 100, wrong: true));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            var s = world.Run.LastSummary!;
            Assert.Equal(750, s.Earned);
            Assert.True(s.QuotaMet);
            Assert.Equal(0, s.Bonus);
            Assert.Equal(1, s.Misdeliveries);
            Assert.Equal(750, world.Economy.Wallet);

            // Shift 2: a letter whose deadline was shift 1 pays half and counts as late.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            StartDelivery(world, p);
            Assert.Equal(OpResult.Ok, Deliver(world, p, 400, deadlineShift: 1));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 271);
            s = world.Run.LastSummary!;
            Assert.Equal(2, s.Shift);
            Assert.Equal(200, s.Earned);
            Assert.Equal(1, s.LateDeliveries);
            Assert.False(s.QuotaMet);
        }

        [Fact]
        public void MissedQuotaEndsTheRunInResults_AndTheHostReturnsToTheLobby()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            StartDelivery(world, host);
            Assert.Equal(OpResult.Ok, Deliver(world, guest, 120));
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            Assert.False(world.Run.LastSummary!.QuotaMet);

            world.Events.Clear();
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 21);
            var r = world.Events.OfType<RunResultsEvent>().Single().Results;
            Assert.Equal(RunOutcome.QuotaMissed, r.Outcome);
            Assert.Equal(RunOutcome.QuotaMissed, world.GetGlobalState().Outcome);
            Assert.Equal(0, r.ShiftsCompleted);
            Assert.Equal(132, r.TotalEarned); // 120 + the 10 % bonus
            Assert.Equal(132, r.Score);
            Assert.Equal(1, r.Deliveries);
            Assert.Equal(2, r.Players.Count);
            Assert.All(r.Players, x => Assert.Equal(5, x.Xp)); // 0 shifts, no victory, at least 1 delivery each
            Assert.Equal(1, r.Players.Single(x => x.Player == guest.Id).Deliveries);
            Assert.Equal(120, r.Players.Single(x => x.Player == guest.Id).Earned);

            // Results has no clock: it waits for the host.
            Assert.Equal(0u, world.Run.PhaseEndTick);
            SimDriver.StepTicks(world, 120 * Tps);
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            Assert.Equal(OpResult.NotHost, Request(world, guest, SimRequest.ContinueRun(0)));

            // Mail left over from the run is gone once the Lobby is back.
            Assert.True(world.Intake.EntryCount > 0);
            var leftover = TestContent.Mail("letter", world.Map.Destinations[0].Address, 999999);
            Assert.True(world.Give(guest.Inventory, ItemStack.OfMail(leftover)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Equal(RunOutcome.None, world.Run.Outcome);
            Assert.Null(world.Run.Results);
            Assert.Equal(0, world.Economy.Wallet);
            Assert.Equal(0, world.Intake.EntryCount);
            Assert.Equal(0, guest.Inventory.EntryCount);
            Assert.Equal(0, world.Spawner.BacklogCount);

            // And the next run is a fresh shift 1.
            StartDelivery(world, host);
            Assert.Equal(2, world.Run.RunNumber);
            Assert.Equal(1, world.Run.Shift);
        }

        [Fact]
        public void FiveShiftsOnQuotaIsAVictory()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            int total = 0;
            for (int shift = 1; shift <= 5; shift++)
            {
                StartDelivery(world, p);
                Assert.Equal(shift, world.Run.Shift);
                int quota = world.Run.Quota;
                Assert.Equal(QuotaFormula.Quota(world.Content.Balance, shift, 1), quota);
                Assert.Equal(OpResult.Ok, Deliver(world, p, quota));
                total += quota + quota / 10;
                RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 301);
                Assert.True(world.Run.LastSummary!.QuotaMet);
                RunUntil(world, () => world.Run.Phase != RunPhase.Payday, 21);
            }
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            var r = world.Run.Results!;
            Assert.Equal(RunOutcome.Victory, r.Outcome);
            Assert.Equal(5, r.ShiftsCompleted);
            Assert.Equal(total, r.TotalEarned);
            Assert.Equal(total, world.Economy.Wallet);
            Assert.Equal(100 * 5 + 50 + 5 * 5, r.Players.Single().Xp);
        }

        [Fact]
        public void QuotaIsSampledFromThePlayerCountWhenDeliveryStarts()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            Request(world, a, SimRequest.StartRun(0));
            Assert.Equal(600, world.Run.Quota);
            Assert.True(world.AcceptsPlayers);
            world.AddPlayer("b");
            world.AddPlayer("c");
            StartDelivery(world, a);
            Assert.Equal(3, world.Run.PlayerCount);
            Assert.Equal(QuotaFormula.Quota(world.Content.Balance, 1, 3), world.Run.Quota);
            Assert.Equal(1225, world.Run.Quota);
        }

        [Fact]
        public void JoiningIsOnlyAllowedInTheLobbyAndDuringPrep()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            Assert.True(world.AcceptsPlayers);
            Request(world, p, SimRequest.StartRun(0));
            Assert.True(world.AcceptsPlayers);
            StartDelivery(world, p);
            Assert.False(world.AcceptsPlayers);
            Deliver(world, p, 600);
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            Assert.False(world.AcceptsPlayers);
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            Assert.True(world.AcceptsPlayers);
        }

        [Fact]
        public void TheHostRolePassesToTheLongestConnectedPlayer()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            var c = world.AddPlayer("c")!;
            world.RemovePlayer(a.Id);
            Assert.Equal(b.Id, world.Run.Host);
            var d = world.AddPlayer("d")!; // takes a's free slot but is the newest
            world.RemovePlayer(b.Id);
            Assert.Equal(c.Id, world.Run.Host);
            Assert.Equal(OpResult.NotHost, Request(world, d, SimRequest.StartRun(0)));
            Assert.Equal(OpResult.Ok, Request(world, c, SimRequest.StartRun(0)));
            world.RemovePlayer(c.Id);
            world.RemovePlayer(d.Id);
            Assert.True(world.Run.Host.IsNone);
        }

        [Fact]
        public void AnEmptyServerAbandonsTheRunAfterTheGracePeriod()
        {
            var world = NewWorld();
            var p = world.AddPlayer("a")!;
            StartDelivery(world, p);
            Deliver(world, p, 50);
            world.RemovePlayer(p.Id);
            world.Events.Clear();
            SimDriver.StepTicks(world, 60 * Tps - 1);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            world.Step();
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Empty(world.Events.OfType<RunResultsEvent>());
            Assert.Equal(0, world.Economy.Wallet);
            Assert.Equal(0, world.Intake.EntryCount);

            // Whoever comes next hosts a fresh run.
            var next = world.AddPlayer("b")!;
            Assert.Equal(next.Id, world.Run.Host);
            StartDelivery(world, next);
            Assert.Equal(1, world.Run.Shift);
        }

        [Fact]
        public void TheSameSeedSpawnsTheSameMailInTheNextRun()
        {
            var world = NewWorld(seed: 21);
            var p = world.AddPlayer("a")!;

            List<(ushort Kind, uint Address, int Count)> FirstMinute()
            {
                StartDelivery(world, p);
                SimDriver.StepTicks(world, 60 * Tps);
                return world.Intake.Entries.OrderBy(e => e.EntryId)
                    .Select(e => (e.Stack.DefIndex, e.Stack.Address.Key, e.Stack.Count)).ToList();
            }

            var first = FirstMinute();
            Assert.NotEmpty(first);
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 300);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.ContinueRun(0)));
            Assert.Equal(first, FirstMinute());
        }

        [Fact]
        public void QuickStartServerCyclesThroughRunsByItself()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, QuickStart = true });
            world.AddPlayer("a");
            world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.AcceptsPlayers);
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 21);
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 22);
            Assert.Equal(2, world.Run.RunNumber);
            Assert.Equal(1, world.Run.Shift);
        }
    }
}
