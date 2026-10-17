using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Hosting;

/// <summary>
/// Covers story 024 (TR-sim-003/008/014, GDD SC-R2-1 runner half): the <see cref="FrameRunner"/> frame order with a
/// recording stub <see cref="IFrameTransport"/>, zero-tick / pure-client / hitch frames, the owner-thread assert and
/// the 0 B frame loop. Integer timestamps (frequency 10^7), the real <see cref="TickDriver"/>, no real clock.
/// </summary>
public class FrameRunnerTests
{
    private const long Freq = 10_000_000;
    private const long ThreeTicks = 1_000_000; // exactly 3 x (Freq / 30) rounded down to whole counts: acc30 releases 3 ticks
    private const int Cap = 4;

    private sealed class FakeClock : IMonotonicClock
    {
        public long Now { get; set; }

        public long Timestamp => Now;

        public long Frequency => Freq;
    }

    private sealed class FakeHostClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;
    }

    private sealed class Rig
    {
        public readonly List<string> Log = new();
        public readonly List<bool> FirstFlags = new();
        public readonly List<uint> WorldTickAtFlush = new();
        public bool Recording = true;
        public readonly FakeClock Clock = new();
        public readonly TickDriver Driver = new(Freq, Cap);
        public SimWorld? World;
        public ClientLayerSut Client = null!;
        public FrameRunner Runner = null!;
        public Transport Stub = null!;
    }

    private sealed class Transport : IFrameTransport
    {
        private readonly Rig _rig;

        public Transport(Rig rig) => _rig = rig;

        public int Pumps;
        public int Flushes;

        public void PumpInbound()
        {
            Pumps++;
            if (_rig.Recording)
            {
                _rig.Log.Add("Pump");
            }
        }

        public void FlushOutbound(uint tick)
        {
            Flushes++;
            if (!_rig.Recording)
            {
                return;
            }

            _rig.Log.Add($"Flush({tick})");
            _rig.WorldTickAtFlush.Add(_rig.World?.CurrentTick ?? uint.MaxValue);
        }
    }

    private sealed class ClientProbe : IClientStep
    {
        private readonly Rig _rig;

        public ClientProbe(Rig rig) => _rig = rig;

        public void Step(in ClientStepContext ctx)
        {
            _rig.Log.Add("Client");
            _rig.FirstFlags.Add(ctx.IsFirstTickInBatch);
        }
    }

    private sealed class PhaseProbe : ISimPhase
    {
        private readonly Rig _rig;

        public PhaseProbe(Rig rig) => _rig = rig;

        public void Run(in TickContext ctx) => _rig.Log.Add($"Sim({ctx.Tick})");
    }

    private sealed class Noop : ISimPhase
    {
        public void Run(in TickContext ctx)
        {
        }
    }

    private static Rig Build(bool withWorld = true, bool recording = true, bool start = true)
    {
        var rig = new Rig { Recording = recording };
        rig.Stub = new Transport(rig);
        var clientBuilder = new ClientLayerBuilder(new FakeHostClock());
        if (recording)
        {
            clientBuilder.Bind(ClientStepId.CarrierStep, new ClientProbe(rig));
        }

        rig.Client = clientBuilder.Build();
        if (withWorld)
        {
            var worldBuilder = new SimWorldBuilder();
            worldBuilder.Bind(PhaseId.PlayersStep, recording ? new PhaseProbe(rig) : new Noop());
            rig.World = worldBuilder.Freeze();
        }

        rig.Runner = new FrameRunner(rig.Clock, rig.Driver, rig.Client, rig.World, rig.Stub, FrameRunnerSettings.Default);
        if (start)
        {
            rig.Driver.Start(0);
        }

        return rig;
    }

    private static Exception? RunOnOtherThread(Action action)
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.Start();
        thread.Join();
        return caught;
    }

    // ---- AC-1 SC-R2-1 (stub transport) ----

    [Fact]
    [Trait("Id", "SC-R2-1")]
    public void Ac1_ThreeTicks_RecordsPumpThenClientSimFlushPerTick()
    {
        Rig rig = Build();
        rig.Clock.Now = ThreeTicks;

        rig.Runner.RunFrame();

        Assert.Equal(
            new[] { "Pump", "Client", "Sim(0)", "Flush(0)", "Client", "Sim(1)", "Flush(1)", "Client", "Sim(2)", "Flush(2)" },
            rig.Log);
        Assert.Equal(3u, rig.World!.CurrentTick);
    }

    [Fact]
    [Trait("Id", "SC-R2-1")]
    public void Ac1_IsFirstTickInBatch_IsTrueOnlyForTheFirstTick()
    {
        Rig rig = Build();
        rig.Clock.Now = ThreeTicks;

        rig.Runner.RunFrame();

        Assert.Equal(new[] { true, false, false }, rig.FirstFlags);
    }

    [Fact]
    [Trait("Id", "SC-R2-1")]
    public void Ac1_EachFlush_RunsAfterItsTickReturned_AndCarriesThatTicksNumber()
    {
        Rig rig = Build();
        rig.Clock.Now = ThreeTicks;

        rig.Runner.RunFrame();

        // CurrentTick has already advanced past the tick when the flush runs, and the flush carries the tick that ran.
        Assert.Equal(new uint[] { 1, 2, 3 }, rig.WorldTickAtFlush);
        Assert.Equal("Flush(2)", rig.Log[^1]);
        Assert.Equal("Sim(2)", rig.Log[^2]);
    }

    [Fact]
    [Trait("Id", "SC-R2-1")]
    public void Ac1_SecondFrame_ContinuesTickNumbersAndRestartsTheBatchFlag()
    {
        Rig rig = Build();
        rig.Clock.Now = ThreeTicks;
        rig.Runner.RunFrame();
        rig.Log.Clear();
        rig.FirstFlags.Clear();
        rig.Clock.Now = 2 * ThreeTicks;

        rig.Runner.RunFrame();

        Assert.Equal(
            new[] { "Pump", "Client", "Sim(3)", "Flush(3)", "Client", "Sim(4)", "Flush(4)", "Client", "Sim(5)", "Flush(5)" },
            rig.Log);
        Assert.Equal(new[] { true, false, false }, rig.FirstFlags);
    }

    // ---- AC-2 zero ticks, pure client, hitch ----

    [Fact]
    public void Ac2_ZeroTickFrame_StillPumpsInbound_AndRunsNothingElse()
    {
        Rig rig = Build();
        rig.Clock.Now = 100_000; // 10 ms: under one tick

        rig.Runner.RunFrame();

        Assert.Equal(new[] { "Pump" }, rig.Log);
        Assert.Equal(0u, rig.World!.CurrentTick);
        Assert.Equal(0u, rig.Client.ClientTick);
        Assert.Equal(0, rig.World.CheckRing.Count);
    }

    [Fact]
    public void Ac2_DriverNotStarted_AdvanceReturnsZero_PumpOnly()
    {
        Rig rig = Build(start: false);
        rig.Clock.Now = ThreeTicks;

        rig.Runner.RunFrame();

        Assert.Equal(new[] { "Pump" }, rig.Log);
    }

    [Fact]
    public void Ac2_PureClient_StepsAndFlushesWithClientTickNumbers_AndNoTick()
    {
        Rig rig = Build(withWorld: false);
        rig.Clock.Now = ThreeTicks;

        rig.Runner.RunFrame();

        Assert.Equal(
            new[] { "Pump", "Client", "Flush(0)", "Client", "Flush(1)", "Client", "Flush(2)" },
            rig.Log);
        Assert.Equal(new[] { true, false, false }, rig.FirstFlags);
        Assert.Equal(3u, rig.Client.ClientTick);

        rig.Log.Clear();
        rig.Clock.Now = 2 * ThreeTicks;
        rig.Runner.RunFrame();

        Assert.Equal(new[] { "Pump", "Client", "Flush(3)", "Client", "Flush(4)", "Client", "Flush(5)" }, rig.Log);
    }

    [Fact]
    public void Ac2_PureClient_DroppedTicks_WriteNoRecordAndDoNotThrow()
    {
        Rig rig = Build(withWorld: false, recording: false);
        rig.Clock.Now = 5 * Freq;

        rig.Runner.RunFrame();

        Assert.Equal(1L, rig.Driver.Hitches);
        Assert.Equal((uint)Cap, rig.Client.ClientTick);
    }

    [Fact]
    public void Ac2_DroppedTicks_WriteExactlyOneTickHitchRecord_WithDroppedAndClamped()
    {
        Rig rig = Build(recording: false);
        rig.Clock.Now = 5 * Freq; // 5 s elapsed: clamped to 1 s (4 s discarded), 30 due, 4 run, 26 dropped

        rig.Runner.RunFrame();

        Assert.Equal(Cap, (int)rig.World!.CurrentTick);
        Assert.Equal(1, rig.World.CheckRing.Count);
        SimCheckRecord r = rig.World.CheckRing[0];
        Assert.Equal(CheckCode.TickHitch, r.Check);
        Assert.Equal(26L, r.A);
        Assert.Equal(4 * Freq, r.B);
        Assert.Equal(rig.World.CurrentTick, r.Tick);
    }

    [Fact]
    public void Ac2_HitchRecord_CarriesThisFramesDeltas_NotTheDriverTotals()
    {
        Rig rig = Build(recording: false);
        rig.Clock.Now = 5 * Freq;
        rig.Runner.RunFrame(); // first hitch: 26 dropped, 4 s clamped

        // Quiet frames until past the ring's per-code rate-limit window (30 ticks), none of which hits the cap.
        while (rig.World!.CurrentTick < 4 + (uint)rig.World.CheckRing.RateLimitTicks + 1)
        {
            rig.Clock.Now += 100_000;
            rig.Runner.RunFrame();
        }

        Assert.Equal(1L, rig.Driver.Hitches);
        Assert.Equal(1, rig.World.CheckRing.Count);
        rig.Clock.Now += 15 * Freq / 10; // 1.5 s: 0.5 s clamped, 30 due, 4 run, 26 dropped

        rig.Runner.RunFrame();

        Assert.Equal(2L, rig.Driver.Hitches);
        Assert.Equal(2, rig.World.CheckRing.Count);
        SimCheckRecord r = rig.World.CheckRing[1];
        Assert.Equal(CheckCode.TickHitch, r.Check);
        Assert.Equal(26L, r.A);
        Assert.Equal(Freq / 2, r.B);
    }

    [Fact]
    public void Ac2_FrameWithoutNewHitch_WritesNoRecord()
    {
        Rig rig = Build(recording: false);
        rig.Clock.Now = ThreeTicks; // 3 ticks, under the cap

        rig.Runner.RunFrame();
        rig.Clock.Now += 1_500_000; // 4 ticks exactly at the cap with no overflow left (150 ms catch-up row)
        rig.Runner.RunFrame();

        Assert.Equal(0L, rig.Driver.Hitches);
        Assert.Equal(0, rig.World!.CheckRing.Count);
    }

    [Fact]
    public void Ctor_RejectsNullsAndNegativeGuard_AndExposesDriverAndSettings()
    {
        Rig rig = Build();
        var clock = new FakeClock();

        Assert.Throws<ArgumentNullException>(() => new FrameRunner(null!, rig.Driver, rig.Client, rig.World, rig.Stub, default));
        Assert.Throws<ArgumentNullException>(() => new FrameRunner(clock, null!, rig.Client, rig.World, rig.Stub, default));
        Assert.Throws<ArgumentNullException>(() => new FrameRunner(clock, rig.Driver, null!, rig.World, rig.Stub, default));
        Assert.Throws<ArgumentNullException>(() => new FrameRunner(clock, rig.Driver, rig.Client, rig.World, null!, default));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FrameRunner(clock, rig.Driver, rig.Client, rig.World, rig.Stub, new FrameRunnerSettings(-1)));
        Assert.Same(rig.Driver, rig.Runner.Driver);
        Assert.Equal(0, rig.Runner.Settings.CatchUpGuardMs);
        Assert.Equal(0, FrameRunnerSettings.Default.CatchUpGuardMs);
    }

    [Fact]
    public void Ac2_NonZeroGuardSetting_IsAcceptedButNotBuilt_AllReleasedTicksStillRun()
    {
        Rig rig = Build();
        var runner = new FrameRunner(rig.Clock, rig.Driver, rig.Client, rig.World, rig.Stub, new FrameRunnerSettings(10));
        rig.Clock.Now = 5 * Freq;

        runner.RunFrame();

        Assert.Equal(Cap, (int)rig.World!.CurrentTick);
    }

    // ---- AC-3 thread and allocation ----

    [Fact]
    public void Ac3_RunFrame_FromNonOwnerThread_AssertsUnderSimAssert_AndRunsOtherwise()
    {
        Rig rig = Build(recording: false);
        rig.Clock.Now = 100_000; // zero ticks, so a Release build's Tick() thread guard is not involved

        Exception? caught = RunOnOtherThread(rig.Runner.RunFrame);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.IsType<SimAssertException>(caught);
            Assert.Equal(SimAssertCode.WrongThread, ex.Code);
            Assert.Equal(0, rig.Stub.Pumps); // asserted before step 1
        }
        else
        {
            Assert.Null(caught);
            Assert.Equal(1, rig.Stub.Pumps);
        }

        rig.Runner.RunFrame(); // the owner thread is unaffected
        Assert.Equal(TestBuildExpectation.SimAssertsExpected ? 1 : 2, rig.Stub.Pumps);
    }

    [Fact]
    public void Ac3_OwnerThread_IsTheWorldsOwner_OrTheConstructingThreadForAPureClient()
    {
        // World host: the owner is the thread that froze the world, wherever the runner was constructed.
        Rig? hostRig = null;
        Exception? buildError = RunOnOtherThread(() => hostRig = Build(recording: false));
        Assert.Null(buildError);
        hostRig!.Clock.Now = 100_000;
        Exception? hostCaught = Record.Exception(hostRig.Runner.RunFrame); // test thread is not the owner

        // Pure client: the owner is the constructing thread.
        Rig? clientRig = null;
        buildError = RunOnOtherThread(() => clientRig = Build(withWorld: false, recording: false));
        Assert.Null(buildError);
        clientRig!.Clock.Now = 100_000;
        Exception? clientCaught = Record.Exception(clientRig.Runner.RunFrame);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.IsType<SimAssertException>(hostCaught);
            Assert.IsType<SimAssertException>(clientCaught);
        }
        else
        {
            Assert.Null(hostCaught);
            Assert.Null(clientCaught);
        }
    }

    [Collection(NonParallelCollection.Name)]
    public class ZeroAllocation
    {
        [Fact]
        public void Ac3_RunFrame_AllocatesZeroBytes_Over1000WarmedFrames()
        {
            Rig rig = Build(recording: false);
            Rig pure = Build(withWorld: false, recording: false);
            Rig[] rigs = { rig, pure };

            int runs = AllocAssert.Zero("FrameRunner.RunFrame", () =>
            {
                for (int frame = 0; frame < 1000; frame++)
                {
                    // 400 000 counts a frame is 1 or 2 ticks; every 250th frame is a 5 s stall (cap hit, clamp, hitch record).
                    long step = frame % 250 == 249 ? 5 * Freq : 400_000;
                    for (int r = 0; r < rigs.Length; r++)
                    {
                        rigs[r].Clock.Now += step;
                        rigs[r].Runner.RunFrame();
                    }
                }
            });

            Assert.Equal(runs * 1000L, rig.Stub.Pumps);
            Assert.True(rig.World!.CurrentTick > 0u);
            Assert.True(rig.Driver.Hitches > 0L);
            Assert.True(pure.Client.ClientTick > 0u);
        }
    }
}
