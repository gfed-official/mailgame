using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 06 §6: "Disconnect during a run: the player's character is frozen and made invulnerable
    /// for 120 s; their inventory stays on the body. If they reconnect (same account id) within
    /// 120 s they resume. Otherwise the body drops a death bag and despawns. Player count for
    /// scaling is not recalculated mid-shift; it drops at the next Delivery start."
    /// </summary>
    public class AwayTests
    {
        private const int Tps = 30;
        private const int Grace = 120 * Tps;

        private uint _nextRequest = 1;
        private uint _nextMailId = 900000;

        private static ContentDb Db => TestContent.Db;

        private static SimWorld NewWorld() => new SimWorld(Db, new SimSettings { Seed = 7, Draft = false });

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
            foreach (var p in world.Players)
                if (!p.Away) Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
        }

        /// <summary>Pays the shift's quota with one letter, so the run goes on.</summary>
        private void MeetTheQuota(SimWorld world, Player p)
        {
            var home = world.Map.Destinations[0];
            var item = TestContent.Mail("letter", home.Address, _nextMailId++, world.Run.Shift);
            item.Value = (ushort)world.Run.Quota;
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            SimDriver.Teleport(p, home.MailboxX, home.MailboxZ);
            SimDriver.StepTicks(world, 15);
            var entry = p.Hotbar.Entries.Single(e => e.Stack.Kind == StackKind.Mail);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Deliver(0, p.Hotbar.Id, entry.EntryId, home.Id)));
        }

        private uint GiveLetter(SimWorld world, Player p)
        {
            var item = TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMailId++);
            Assert.True(world.Give(p.Hotbar, ItemStack.OfMail(item)));
            return item.Id;
        }

        private static int Letters(GridContainer container) =>
            container.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Count);

        [Fact]
        public void TheGraceIsTwoMinutes()
        {
            Assert.Equal(120, Db.Balance.GetDouble("disconnectGraceSeconds"));
        }

        [Fact]
        public void InTheLobbyALostConnectionIsALeave()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            world.Events.Clear();

            Assert.False(world.Disconnect(b.Id));
            Assert.Single(world.Players);
            Assert.Equal(b.Id, Assert.Single(world.Events.OfType<PlayerLeftEvent>()).Player);
            Assert.Empty(world.Events.OfType<PlayerAwayEvent>());
            Assert.Null(world.FindAway(22));
            Assert.Equal(a.Id, world.Run.Host);
            // Nobody at all is nobody to keep.
            Assert.False(world.Disconnect(b.Id));
        }

        [Fact]
        public void DuringARunTheyStay_Away_WithTheirPocketsAndTheirSeat()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            StartDelivery(world, a);
            GiveLetter(world, b);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(b, po.IntakeX, po.IntakeZ + 1.5f);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, b, SimRequest.Open(0, world.Intake.Id)));
            Assert.Equal(world.Intake.Id, b.OpenContainer);
            world.Events.Clear();

            Assert.True(world.Disconnect(b.Id));
            Assert.True(b.Away);
            Assert.Equal(world.Tick + (uint)Grace, b.AwayUntilTick);
            Assert.Equal(0u, b.OpenContainer);
            var ev = Assert.Single(world.Events.OfType<PlayerAwayEvent>());
            Assert.Equal((b.Id, b.AwayUntilTick), (ev.Player, ev.UntilTick));
            Assert.Empty(world.Events.OfType<PlayerLeftEvent>());
            // Losing it twice changes nothing.
            uint until = b.AwayUntilTick;
            world.Step();
            Assert.True(world.Disconnect(b.Id));
            Assert.Equal(until, b.AwayUntilTick);
            Assert.Single(world.Events.OfType<PlayerAwayEvent>());

            Assert.Equal(2, world.Players.Count);
            Assert.Equal(1, world.PresentCount);
            Assert.Equal(1, Letters(b.Hotbar));
            Assert.Same(b, world.FindAway(22));
            Assert.Null(world.FindAway(11));
            Assert.Null(world.FindAway(0));
        }

        [Fact]
        public void WhoIsAwayComesToRest_AndWhatTheySentBeforeIsForgotten()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartDelivery(world, a);
            SimDriver.Teleport(b, 50f, 28f);
            // Running east, with more of it queued.
            for (int i = 0; i < 20; i++)
            {
                world.QueueInput(b.Id, new PlayerInput { Seq = b.LastQueuedSeq + 1, MoveX = 127, Buttons = InputButtons.Sprint });
                world.Step();
            }
            for (int i = 0; i < 3; i++) world.QueueInput(b.Id, new PlayerInput { Seq = b.LastQueuedSeq + 1, MoveX = 127 });
            world.Submit(b.Id, new SimRequest { RequestId = 7001, Kind = RequestKind.OpenContainer, Container = world.Intake.Id });

            world.Disconnect(b.Id);
            world.Events.Clear();
            SimDriver.StepTicks(world, Tps);
            Assert.DoesNotContain(world.Events.OfType<RequestResultEvent>(), e => e.RequestId == 7001);
            float x = b.Move.X, z = b.Move.Z;
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal((x, z), (b.Move.X, b.Move.Z));
            Assert.True(b.Move.Grounded);
            // What still arrives from the dead connection moves nobody.
            world.QueueInput(b.Id, new PlayerInput { Seq = b.LastQueuedSeq + 1, MoveX = 127 });
            SimDriver.StepTicks(world, 5);
            Assert.Equal(x, b.Move.X);
        }

        [Fact]
        public void RaidersLeaveThemAlone_AndNothingHurtsThem()
        {
            var world = NewWorld();
            var b = world.AddPlayer("b")!;
            StartDelivery(world, b);
            SimDriver.Teleport(b, 60f, 28f);
            var e = world.SpawnEnemy(Db.Enemy("barbarian"), 66f, 28f)!;
            SimDriver.StepTicks(world, 2);
            Assert.Equal(b.Id, e.Target);

            world.Disconnect(b.Id);
            Assert.True(e.Target.IsNone);
            SimDriver.StepTicks(world, 10 * Tps);
            Assert.True(e.Target.IsNone);
            Assert.Equal(b.MaxHp, b.Hp);
            world.DamagePlayer(b, 1000f);
            Assert.False(b.Dead);
            Assert.Equal(b.MaxHp, b.Hp);

            // Back, they are fair game again.
            world.Reconnect(b);
            SimDriver.Teleport(b, e.X - 4f, e.Z);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(b.Id, e.Target);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.True(b.Hp < b.MaxHp);
        }

        [Fact]
        public void LootIsNotPickedUpByWhoIsAway()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartDelivery(world, a);
            SimDriver.Teleport(a, 20f, 28f);
            SimDriver.Teleport(b, 60f, 28f);
            world.Disconnect(b.Id);
            var giant = world.SpawnEnemy(Db.Enemy("giant"), 60.5f, 28f)!;
            world.DamageEnemy(giant, 100000f, a);
            Assert.Single(world.Loot);
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Single(world.Loot);
            Assert.Equal(0, b.Inventory.EntryCount);

            world.Reconnect(b);
            SimDriver.StepTicks(world, Tps);
            Assert.Empty(world.Loot);
            Assert.True(b.Inventory.EntryCount > 0);
        }

        [Fact]
        public void BackInTime_TheyAreThemselvesAgain()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            StartDelivery(world, a);
            SimDriver.Teleport(b, 50f, 28f);
            uint letter = GiveLetter(world, b);
            world.QueueInput(b.Id, new PlayerInput { Seq = 400 });
            world.Step();
            Assert.Equal(400u, b.LastProcessedSeq);
            world.Disconnect(b.Id);
            SimDriver.StepTicks(world, Grace - 2);
            Assert.True(b.Away);
            world.Events.Clear();

            var found = world.FindAway(22);
            Assert.Same(b, found);
            world.Reconnect(found!);
            Assert.False(b.Away);
            Assert.Equal(b.Id, Assert.Single(world.Events.OfType<PlayerBackEvent>()).Player);
            Assert.Null(world.FindAway(22));
            Assert.Equal(2, world.PresentCount);
            Assert.Contains(b.Hotbar.Entries, e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == letter));
            // The new connection counts its inputs from one.
            Assert.Equal((0u, 0u), (b.LastQueuedSeq, b.LastProcessedSeq));
            float x = b.Move.X;
            for (int i = 0; i < 10; i++)
            {
                world.QueueInput(b.Id, new PlayerInput { Seq = b.LastQueuedSeq + 1, MoveX = 127 });
                world.Step();
            }
            Assert.True(b.Move.X > x + 0.5f);

            // The grace that ran out for nobody ends nothing.
            SimDriver.StepTicks(world, 10);
            Assert.Equal(2, world.Players.Count);
            Assert.Empty(world.Events.OfType<PlayerLeftEvent>());
            Assert.Empty(world.Bags);
        }

        [Fact]
        public void NotBackInTime_TheirPocketsLieInABagWhereTheyStood_AndTheyAreGone()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            StartDelivery(world, a);
            SimDriver.Teleport(b, 50f, 28f);
            uint letter = GiveLetter(world, b);
            Assert.True(world.Give(b.Inventory, ItemStack.OfItem(Db.Item("stone"), 3)));
            world.Step();
            world.Disconnect(b.Id);
            float x = b.Move.X, z = b.Move.Z;
            world.Events.Clear();

            SimDriver.StepTicks(world, Grace - 1);
            Assert.Equal(2, world.Players.Count);
            Assert.Empty(world.Bags);
            world.Step();

            Assert.Single(world.Players);
            Assert.Null(world.GetPlayer(b.Id));
            Assert.Null(world.FindAway(22));
            var bag = Assert.Single(world.Bags);
            Assert.Equal((x, z), (bag.X, bag.Z));
            // Nobody is left to call it theirs.
            Assert.True(bag.Owner.IsNone);
            Assert.Contains(bag.Container.Entries, e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == letter));
            Assert.Contains(bag.Container.Entries, e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == Db.Item("stone").Index && e.Stack.Count == 3);
            // The bag is told of before the leave, while its owner still has a name.
            var events = world.Events.ToList();
            int dropped = events.FindIndex(e => e is BagDroppedEvent);
            int left = events.FindIndex(e => e is PlayerLeftEvent l && l.Player == b.Id);
            Assert.True(dropped >= 0 && left > dropped);
            Assert.Equal(b.Id, ((BagDroppedEvent)events[dropped]).Owner);

            // The mail goes back to the Intake like any bag's.
            SimDriver.StepTicks(world, 30 * Tps + 1);
            Assert.Empty(world.Bags);
            Assert.Contains(world.Intake.Entries, e => e.Stack.Kind == StackKind.Mail && e.Stack.Mail.Any(m => m.Id == letter));
        }

        [Fact]
        public void EmptyPocketsLeaveNoBag()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            StartDelivery(world, a);
            foreach (var e in b.Hotbar.Entries.ToList()) b.Hotbar.Remove(e.EntryId);
            world.Disconnect(b.Id);
            SimDriver.StepTicks(world, Grace);
            Assert.Single(world.Players);
            Assert.Empty(world.Bags);
        }

        [Fact]
        public void TheSeatIsHeld_AndAStrangerDoesNotGetIt()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, QuickStart = true });
            var players = Enumerable.Range(0, Db.Balance.GetInt("maxPlayers")).Select(i => world.AddPlayer("p" + i, account: (ulong)(100 + i))!).ToList();
            world.Step();
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.True(world.IsFull);

            world.Disconnect(players[2].Id);
            Assert.True(world.IsFull);
            Assert.Null(world.AddPlayer("stranger", account: 999));
            Assert.Null(world.FindAway(999));

            SimDriver.StepTicks(world, Grace);
            Assert.False(world.IsFull);
            var late = world.AddPlayer("stranger", account: 999)!;
            Assert.Equal(players[2].Id, late.Id);
            Assert.False(late.Away);
        }

        [Fact]
        public void TheHostPassesOn_AndDoesNotComeBackAsHost()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            var c = world.AddPlayer("c", account: 33)!;
            StartDelivery(world, a);

            world.Disconnect(a.Id);
            Assert.Equal(b.Id, world.Run.Host);
            world.Reconnect(a);
            Assert.Equal(b.Id, world.Run.Host);

            // With nobody here to take it, it stays where it is, and whoever is back first has it.
            world.Disconnect(c.Id);
            world.Disconnect(b.Id);
            Assert.Equal(a.Id, world.Run.Host);
            world.Disconnect(a.Id);
            Assert.Equal(a.Id, world.Run.Host);
            world.Reconnect(c);
            Assert.Equal(c.Id, world.Run.Host);
            world.Reconnect(b);
            Assert.Equal(c.Id, world.Run.Host);
        }

        [Fact]
        public void PrepDoesNotWaitForThem_AndTheNextShiftCountsWithoutThem()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            var c = world.AddPlayer("c")!;
            StartDelivery(world, a);
            Assert.Equal(3, world.Run.PlayerCount);
            int quotaOfThree = world.Run.Quota;
            Assert.Equal(QuotaFormula.Quota(Db.Balance, 1, 3), quotaOfThree);

            // Mid-shift nothing is recalculated.
            world.Disconnect(c.Id);
            SimDriver.StepTicks(world, Tps);
            Assert.Equal(3, world.Run.PlayerCount);
            Assert.Equal(quotaOfThree, world.Run.Quota);

            MeetTheQuota(world, a);
            // The clock is shorter than the grace: c is still away at the Payday and in the Prep.
            SimDriver.Teleport(c, 50f, 28f);
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 241);
            c.AwayUntilTick = world.Tick + 100 * Tps;
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            Assert.True(c.Away);
            Assert.Equal(QuotaFormula.Quota(Db.Balance, 2, 2), world.Run.Quota);

            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, b, SimRequest.SetReady(0, true)));
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(2, world.Run.PlayerCount);
            Assert.Equal(QuotaFormula.Quota(Db.Balance, 2, 2), world.Run.Quota);
        }

        [Fact]
        public void TheRaidIsSizedForWhoStartedTheShift()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            var c = world.AddPlayer("c")!;
            world.SetPlaytest(a, PlaytestFlags.RaidNow, startShift: 2);
            StartDelivery(world, a);
            Assert.Equal(2, world.Run.Shift);
            world.Disconnect(b.Id);
            world.Disconnect(c.Id);
            RunUntil(world, () => world.Raid == RaidState.Active, 30);
            Assert.Equal(SimWorld.WaveBudget(Db.Waves, 2, 3, world.Economy.Complaint), world.RaidBudget, 6);
        }

        [Fact]
        public void WhatTheyAskedForAsAPlaytesterRestsWhileTheyAreAway()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            var b = world.AddPlayer("b")!;
            world.SetPlaytest(b, PlaytestFlags.NoTimer | PlaytestFlags.NoQuota, startShift: 3);
            StartDelivery(world, a);
            Assert.Equal(3, world.Run.Shift);
            Assert.True(world.ClockStopped && world.QuotaWaived);

            world.Disconnect(b.Id);
            Assert.False(world.ClockStopped || world.QuotaWaived);
            Assert.Equal(1, world.StartShift);
            // It is theirs again when their Hello says so; until then the body alone asks for nothing.
            world.Reconnect(b);
            Assert.True(world.ClockStopped && world.QuotaWaived);
        }

        [Fact]
        public void WithEverybodyAway_TheRunIsGivenUpAfterTheEmptyServerGrace_AndTheSeatsAreFree()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            StartDelivery(world, a);
            GiveLetter(world, b);
            world.Disconnect(a.Id);
            SimDriver.StepTicks(world, 10 * Tps);
            world.Disconnect(b.Id);
            world.Events.Clear();

            SimDriver.StepTicks(world, 60 * Tps - 1);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Equal(2, world.Players.Count);
            world.Step();
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Empty(world.Players);
            Assert.Equal(2, world.Events.OfType<PlayerLeftEvent>().Count());
            Assert.Empty(world.Bags);
            Assert.True(world.Run.Host.IsNone);
            Assert.Null(world.FindAway(11));

            var next = world.AddPlayer("c")!;
            Assert.Equal(next.Id, world.Run.Host);
            Assert.Equal(0, next.Inventory.EntryCount);
        }

        [Fact]
        public void OneWhoCameBackIsWaitedForAgain()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            StartDelivery(world, a);
            world.Disconnect(a.Id);
            SimDriver.StepTicks(world, 59 * Tps);
            world.Reconnect(a);
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            Assert.Single(world.Players);
        }

        [Fact]
        public void ThePauseEndsWhenItsHostIsAway_AndNobodyPausesWhileASeatIsHeld()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a")!;
            StartDelivery(world, a);
            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.SetPaused(0, true)));
            Assert.True(world.Paused);
            world.Disconnect(a.Id);
            Assert.False(world.Paused);
            uint tick = world.Tick;
            world.Step();
            Assert.Equal(tick + 1, world.Tick);

            var world2 = NewWorld();
            var host = world2.AddPlayer("host")!;
            var guest = world2.AddPlayer("guest")!;
            StartDelivery(world2, host);
            world2.Disconnect(guest.Id);
            Assert.Equal(OpResult.NotSolo, Request(world2, host, SimRequest.SetPaused(0, true)));
        }

        [Fact]
        public void BackInThePaydayOrTheResults_TheyAreToldWhatTheyMissed()
        {
            var world = NewWorld();
            var a = world.AddPlayer("a", account: 11)!;
            var b = world.AddPlayer("b", account: 22)!;
            StartDelivery(world, a);
            MeetTheQuota(world, a);
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery && world.Run.PhaseEndTick - world.Tick < 10 * Tps, 241);
            world.Disconnect(b.Id);
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 11);
            world.Events.Clear();
            world.Reconnect(b);
            var summary = Assert.Single(world.Events.OfType<ShiftSummaryEvent>());
            Assert.Equal(b.Id, summary.Audience);
            Assert.Same(world.Run.LastSummary, summary.Summary);

            // Shift 2 misses its quota: the run ends in the Results.
            RunUntil(world, () => world.Run.Phase == RunPhase.Prep, 21);
            StartDelivery(world, a);
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery && world.Run.PhaseEndTick - world.Tick < 10 * Tps, 271);
            world.Disconnect(b.Id);
            RunUntil(world, () => world.Run.Phase == RunPhase.Results, 40);
            world.Events.Clear();
            world.Reconnect(b);
            var results = Assert.Single(world.Events.OfType<RunResultsEvent>());
            Assert.Equal(b.Id, results.Audience);
            Assert.Same(world.Run.Results, results.Results);

            // The host goes back to the Lobby; nobody there is away.
            world.Disconnect(b.Id);
            Assert.Equal(OpResult.Ok, Request(world, a, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Same(a, Assert.Single(world.Players));
        }
    }
}
