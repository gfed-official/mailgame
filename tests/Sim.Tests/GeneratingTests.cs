using System.Linq;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Doc 01 §1: Lobby → Generating (host starts run) → Prep (map ready, players spawned). The
    /// run is set up as Generating begins; Prep comes once everybody here has said their world is
    /// drawn (their Ready), or when generatingSeconds are up.
    /// </summary>
    public class GeneratingTests
    {
        private const int Tps = 30;
        private uint _nextRequest = 1;

        private static SimWorld NewWorld(bool generating = true) =>
            new SimWorld(TestContent.Db, new SimSettings { Seed = 7, Draft = false, Raids = false, Generating = generating });

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        [Fact]
        public void TheHostsStart_SetsTheRunUp_AndPrepWaitsForEverybodysWorld()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            var guest = world.AddPlayer("guest")!;
            Assert.Equal(OpResult.Ok, Request(world, guest, SimRequest.SetReady(0, true)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Generating, world.Run.Phase);
            var entered = world.Events.OfType<PhaseChangedEvent>().Single();
            Assert.Equal((RunPhase.Generating, 1), (entered.Phase, (int)entered.Shift));
            Assert.Equal(entered.Tick + 10 * Tps, entered.PhaseEndTick);
            // Set up: the run's first shift, nobody ready yet (the Lobby's Ready does not carry over).
            Assert.Equal(1, world.Run.Shift);
            Assert.False(host.Ready || guest.Ready);
            Assert.Equal(RunPhase.Generating, world.GetGlobalState().Phase);

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            SimDriver.StepTicks(world, 30);
            Assert.Equal(RunPhase.Generating, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, guest, SimRequest.SetReady(0, true)));
            world.Step();
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            Assert.False(host.Ready || guest.Ready);
            // Nothing of the Generating phase is a request any more.
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.StartRun(0)));
        }

        [Fact]
        public void WhoNeverSaysTheirWorldIsDrawn_IsWaitedForTenSecondsAtMost()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            uint end = world.Run.PhaseEndTick;
            while (world.Tick < end - 1) world.Step();
            Assert.Equal(RunPhase.Generating, world.Run.Phase);
            world.Step();
            world.Step();
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
        }

        [Fact]
        public void APlayerMayJoinWhileTheWorldIsGenerated_AndIsWaitedFor_TheirStartShiftCounting()
        {
            var world = NewWorld();
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.AcceptsPlayers);
            var late = world.AddPlayer("late")!;
            world.SetPlaytest(late, PlaytestFlags.None, 3);
            Assert.Equal(3, world.Run.Shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            world.Step();
            Assert.Equal(RunPhase.Generating, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, late, SimRequest.SetReady(0, true)));
            world.Step();
            Assert.Equal((RunPhase.Prep, 3), (world.Run.Phase, world.Run.Shift));
        }

        [Fact]
        public void WithoutTheSetting_TheStartGoesStraightToPrep()
        {
            var world = NewWorld(generating: false);
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
        }
    }
}
