using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-044 and TR-sim-007 for story 012 (GDD SC-ST-1; AC-1 to AC-4): lifecycle transitions, the
/// <see cref="SimWorld.Tick"/> guards, the fault path, <see cref="IRunControl"/> stop latching, host
/// <see cref="SimWorld.Stop"/> and the owner thread. Behaviour that differs between SIM_ASSERT and other builds
/// branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>. A light SC-ST-2 check (out-of-phase and second
/// stop counting) is included; its full verification is M-1b.
/// </summary>
[Trait("Id", "SC-ST-1")]
public class WorldLifecycleTests
{
    private sealed class ActionPhase : ISimPhase
    {
        private readonly Action<uint> _action;

        public ActionPhase(Action<uint> action)
        {
            _action = action;
        }

        public int Calls { get; private set; }

        public void Run(in TickContext ctx)
        {
            Calls++;
            _action(ctx.Tick);
        }
    }

    private sealed class ActionCheck : ITickEndCheck
    {
        private readonly Action<uint> _action;

        public ActionCheck(Action<uint> action)
        {
            _action = action;
        }

        public void Check(uint tick) => _action(tick);
    }

    /// <summary>Records every probe callback and optionally runs a hook before a phase.</summary>
    private sealed class LogProbe : IPhaseProbe
    {
        public List<string> Log { get; } = new();

        public Action<PhaseId>? OnBefore { get; set; }

        public void Before(PhaseId phase)
        {
            Log.Add($"B{(int)phase}");
            OnBefore?.Invoke(phase);
        }

        public void After(PhaseId phase) => Log.Add($"A{(int)phase}");
    }

    private static SimWorld Frozen(LogProbe probe, params (PhaseId Phase, ISimPhase Binder)[] binds)
    {
        var builder = new SimWorldBuilder(probe: probe);
        foreach ((PhaseId phase, ISimPhase binder) in binds)
        {
            builder.Bind(phase, binder);
        }

        return builder.Freeze();
    }

    /// <summary>Builds a world directly in <paramref name="state"/>, bypassing the builder (the only way to hold a Constructed world).</summary>
    private static SimWorld InState(SimState state, LogProbe probe)
    {
        var phases = new ISimPhase[14][];
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i] = Array.Empty<ISimPhase>();
        }

        var settings = new SimCoreSettings();
        var parts = new SimWorldParts(phases, Array.Empty<ITickEndCheck>(), settings, new SimCounters(), new SimCheckRing(settings.CheckRingCapacity), probe)
        {
            InitialState = state,
        };
        return new SimWorld(parts);
    }

    private static SimWorld Faulted(LogProbe probe)
    {
        SimWorld world = Frozen(probe, (PhaseId.PlayersStep, new ActionPhase(_ => throw new InvalidOperationException("boom"))));
        TickExpectingFault(world);
        return world;
    }

    /// <summary>Ticks a world whose phase throws <see cref="InvalidOperationException"/>: SIM_ASSERT builds rethrow it, others return.</summary>
    private static void TickExpectingFault(SimWorld world)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<InvalidOperationException>(world.Tick);
        }
        else
        {
            world.Tick();
        }
    }

    // ---- AC-1 (SC-ST-1): legal transitions, refused transitions, badTick ----

    [Fact]
    public void Ac1_LegalPath_ReadyThenRunningThenStopped()
    {
        SimWorld world = Frozen(new LogProbe());
        Assert.Equal(SimState.Ready, world.State);

        world.Tick();
        Assert.Equal(SimState.Running, world.State);

        world.Tick();
        Assert.Equal(SimState.Running, world.State);

        Assert.True(world.Stop());
        Assert.Equal(SimState.Stopped, world.State);
        Assert.Equal(2u, world.CurrentTick);
        Assert.Equal(0L, world.Counters[CounterId.BadTick]);
    }

    [Fact]
    public void Ac1_FreezeTwice_IsRefused()
    {
        var builder = new SimWorldBuilder();
        builder.Freeze();

        var ex = Assert.Throws<SimBuildException>(builder.Freeze);

        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, ex.Code);
    }

    [Theory]
    [InlineData(SimState.Constructed)]
    [InlineData(SimState.Ready)]
    [InlineData(SimState.Stopped)]
    [InlineData(SimState.Faulted)]
    public void Ac1_Stop_InIllegalState_IsRefusedAndChangesNothing(SimState state)
    {
        SimWorld world = state == SimState.Faulted ? Faulted(new LogProbe()) : InState(state, new LogProbe());

        Assert.Equal(state, world.State);
        Assert.False(world.Stop());
        Assert.Equal(state, world.State);
    }

    [Fact]
    public void Ac1_Stop_FromReady_IsRefused_AndWorldStillTicksNormally()
    {
        SimWorld world = Frozen(new LogProbe());

        Assert.False(world.Stop());
        Assert.Equal(SimState.Ready, world.State);

        world.Tick();

        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Equal(0L, world.Counters[CounterId.BadTick]);
    }

    [Theory]
    [InlineData(SimState.Constructed)]
    [InlineData(SimState.Stopped)]
    [InlineData(SimState.Faulted)]
    public void Ac1_Tick_InNonTickableState_CountsBadTick_AndRunsNothing(SimState state)
    {
        var probe = new LogProbe();
        SimWorld world = state == SimState.Faulted ? Faulted(probe) : InState(state, probe);
        if (state == SimState.Stopped)
        {
            world = Frozen(probe);
            world.Tick();
            Assert.True(world.Stop());
        }

        uint tickBefore = world.CurrentTick;
        long badBefore = world.Counters[CounterId.BadTick];
        probe.Log.Clear();

        world.Tick();
        world.Tick();

        Assert.Empty(probe.Log);
        Assert.Equal(tickBefore, world.CurrentTick);
        Assert.Equal(badBefore + 2, world.Counters[CounterId.BadTick]);
        Assert.Equal(state, world.State);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);
    }

    [Fact]
    public void Ac1_ReentrantTick_FromAPhase_CountsBadTick_AndRunsNoNestedPhase()
    {
        var probe = new LogProbe();
        SimWorld? world = null;
        var reentrant = new ActionPhase(_ => world!.Tick());
        world = Frozen(probe, (PhaseId.PlayersStep, reentrant));

        world.Tick();

        Assert.Equal(1, reentrant.Calls);
        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(1, probe.Log.Count(e => e == "B13"));
        Assert.Equal(1, probe.Log.Count(e => e == "B0"));
    }

    [Fact]
    public void Ac1_ReentrantTick_DuringPhase13Probe_CountsBadTick_NotAFault()
    {
        var probe = new LogProbe();
        SimWorld world = Frozen(probe);
        probe.OnBefore = phase =>
        {
            if (phase == PhaseId.EventsFlush)
            {
                world.Tick();
            }
        };

        world.Tick();

        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(1u, world.CurrentTick);
    }

    // ---- AC-2: fault path ----

    [Fact]
    public void Ac2_ThrowingPhase_FaultsWorld_SkipsLaterPhasesAndPhase13_ResetsStage()
    {
        var probe = new LogProbe();
        var before = new ActionPhase(_ => { });
        var thrower = new ActionPhase(_ => throw new InvalidOperationException("boom"));
        var after = new ActionPhase(_ => { });
        SimWorld world = Frozen(probe, (PhaseId.PlayersStep, before), (PhaseId.MailStep, thrower), (PhaseId.RunStep, after));

        TickExpectingFault(world);

        Assert.Equal(SimState.Faulted, world.State);
        Assert.Equal(1, before.Calls);
        Assert.Equal(1, thrower.Calls);
        Assert.Equal(0, after.Calls);
        Assert.DoesNotContain("B13", probe.Log);
        Assert.Equal(0u, world.CurrentTick);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);
        Assert.Equal(default(PhaseId), world.Phase);
        Assert.Equal(0L, world.Counters[CounterId.BadTick]);
    }

    [Fact]
    public void Ac2_AfterFault_ReentrancyFlagIsClear_NextTickCountsBadTickAndDoesNotThrow()
    {
        var probe = new LogProbe();
        SimWorld world = Faulted(probe);
        probe.Log.Clear();

        world.Tick();

        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        Assert.Empty(probe.Log);
        Assert.Equal(SimState.Faulted, world.State);
    }

    [Fact]
    public void Ac2_FaultMidRun_LeavesCurrentTickAtTheFailedTick()
    {
        var probe = new LogProbe();
        SimWorld world = Frozen(probe, (PhaseId.RunStep, new ActionPhase(t =>
        {
            if (t == 3)
            {
                throw new InvalidOperationException("boom");
            }
        })));
        world.Tick();
        world.Tick();
        world.Tick();

        TickExpectingFault(world);

        Assert.Equal(3u, world.CurrentTick);
        Assert.Equal(SimState.Faulted, world.State);
    }

    [Fact]
    public void Ac2_ThrowingTickEndCheck_FaultsWorld_AndSkipsPhase13()
    {
        var probe = new LogProbe();
        var builder = new SimWorldBuilder(probe: probe);
        builder.BindTickEnd(new ActionCheck(_ => throw new InvalidOperationException("boom")));
        SimWorld world = builder.Freeze();

        TickExpectingFault(world);

        Assert.Equal(SimState.Faulted, world.State);
        Assert.DoesNotContain("B13", probe.Log);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);
        Assert.Equal(0u, world.CurrentTick);
    }

    [Fact]
    public void Ac2_Fault_RethrowsOriginalExceptionOnlyWhenAssertsEnabled()
    {
        var original = new InvalidOperationException("original");
        SimWorld world = Frozen(new LogProbe(), (PhaseId.PlayersStep, new ActionPhase(_ => throw original)));

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var thrown = Assert.Throws<InvalidOperationException>(world.Tick);
            Assert.Same(original, thrown);
        }
        else
        {
            world.Tick();
        }

        Assert.Equal(SimState.Faulted, world.State);
    }

    [Fact]
    public void Ac2_AssertExceptionFromAPhase_IsAlsoAFault()
    {
        SimWorld world = Frozen(new LogProbe(), (PhaseId.PlayersStep, new ActionPhase(_ => throw new SimAssertException(SimAssertCode.IdSlotRange))));

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(world.Tick);
            Assert.Equal(SimAssertCode.IdSlotRange, ex.Code);
        }
        else
        {
            world.Tick();
        }

        Assert.Equal(SimState.Faulted, world.State);
    }

    // ---- AC-3: stop ----

    [Fact]
    public void Ac3_RequestStop_InPhase12_FinishesTickThroughPhase13_ThenStopped()
    {
        var probe = new LogProbe();
        SimWorld? world = null;
        var stopper = new ActionPhase(t =>
        {
            if (t == 2)
            {
                world!.RunControl.RequestStop(StopReason.Unspecified);
            }
        });
        world = Frozen(probe, (PhaseId.RunStep, stopper));
        world.Tick();
        world.Tick();
        probe.Log.Clear();

        world.Tick();

        Assert.Contains("B13", probe.Log);
        Assert.Contains("A13", probe.Log);
        Assert.Equal(3u, world.CurrentTick);
        Assert.Equal(SimState.Stopped, world.State);
        Assert.Equal(0L, world.Counters[CounterId.StopOutOfPhase]);
        Assert.Equal(0L, world.Counters[CounterId.StopIgnored]);

        probe.Log.Clear();
        world.Tick();

        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        Assert.Equal(3u, world.CurrentTick);
        Assert.Empty(probe.Log);
    }

    [Fact]
    public void Ac3_RequestStop_RunsRemainingPhase12BindersAndTickEndOfTheSameTick()
    {
        var probe = new LogProbe();
        SimWorld? world = null;
        var stopper = new ActionPhase(_ => world!.RunControl.RequestStop(StopReason.Unspecified));
        var later = new ActionPhase(_ => { });
        int checks = 0;
        var builder = new SimWorldBuilder(probe: probe);
        builder.Bind(PhaseId.RunStep, stopper);
        builder.Bind(PhaseId.RunStep, later);
        builder.BindTickEnd(new ActionCheck(_ => checks++));
        world = builder.Freeze();

        world.Tick();

        Assert.Equal(1, later.Calls);
        Assert.Equal(1, checks);
        Assert.Equal(SimState.Stopped, world.State);
    }

    [Fact]
    public void Ac3_HostStop_BetweenTicks_StopsImmediately()
    {
        var probe = new LogProbe();
        SimWorld world = Frozen(probe);
        world.Tick();
        probe.Log.Clear();

        bool stopped = world.Stop();

        Assert.True(stopped);
        Assert.Equal(SimState.Stopped, world.State);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Empty(probe.Log);

        world.Tick();

        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        Assert.Equal(1u, world.CurrentTick);
    }

    [Fact]
    public void Ac3_HostStop_FromInsideATick_IsRefusedAndCounted()
    {
        SimWorld? world = null;
        bool result = true;
        world = Frozen(new LogProbe(), (PhaseId.PlayersStep, new ActionPhase(_ => result = world!.Stop())));

        world.Tick();

        Assert.False(result);
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Equal(1L, world.Counters[CounterId.StopOutOfPhase]);
    }

    [Fact]
    public void Ac3_RequestStop_AfterFault_IsIgnoredAndCounted()
    {
        SimWorld world = Faulted(new LogProbe());

        world.RunControl.RequestStop(StopReason.Unspecified);

        Assert.Equal(SimState.Faulted, world.State);
        Assert.Equal(1L, world.Counters[CounterId.StopIgnored]);
    }

    [Fact]
    public void Ac3_PhaseThrowsAfterStopLatched_EndsFaulted_NotStopped()
    {
        var probe = new LogProbe();
        SimWorld? world = null;
        var builder = new SimWorldBuilder(probe: probe);
        builder.Bind(PhaseId.RunStep, new ActionPhase(_ => world!.RunControl.RequestStop(StopReason.Unspecified)));
        builder.Bind(PhaseId.RunStep, new ActionPhase(_ => throw new InvalidOperationException("boom")));
        world = builder.Freeze();

        TickExpectingFault(world);

        Assert.Equal(SimState.Faulted, world.State);
        Assert.DoesNotContain("B13", probe.Log);
        Assert.Equal(0u, world.CurrentTick);
    }

    // ---- SC-ST-2 mechanics (full verification is M-1b) ----

    [Fact]
    public void ScSt2_RequestStop_OutsidePhase12_IsLatchedAsIfPhase12_AndCounted()
    {
        var probe = new LogProbe();
        SimWorld? world = null;
        var mid = new ActionPhase(_ => world!.RunControl.RequestStop(StopReason.Unspecified));
        world = Frozen(probe, (PhaseId.MailStep, mid));

        world.Tick();

        Assert.Equal(1L, world.Counters[CounterId.StopOutOfPhase]);
        Assert.Contains("B13", probe.Log);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Equal(SimState.Stopped, world.State);
    }

    [Fact]
    public void ScSt2_RequestStop_BetweenTicks_LatchesForTheNextTick_AndCounts()
    {
        SimWorld world = Frozen(new LogProbe());
        world.Tick();

        world.RunControl.RequestStop(StopReason.Unspecified);

        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(1L, world.Counters[CounterId.StopOutOfPhase]);

        world.Tick();

        Assert.Equal(SimState.Stopped, world.State);
        Assert.Equal(2u, world.CurrentTick);
    }

    [Fact]
    public void ScSt2_SecondStop_IsIgnoredAndCounted()
    {
        SimWorld? world = null;
        var stopper = new ActionPhase(_ =>
        {
            world!.RunControl.RequestStop(StopReason.Unspecified);
            world.RunControl.RequestStop(StopReason.Unspecified);
            world.RunControl.RequestStop(StopReason.Unspecified);
        });
        world = Frozen(new LogProbe(), (PhaseId.RunStep, stopper));

        world.Tick();

        Assert.Equal(2L, world.Counters[CounterId.StopIgnored]);
        Assert.Equal(0L, world.Counters[CounterId.StopOutOfPhase]);
        Assert.Equal(SimState.Stopped, world.State);
        Assert.Equal(1u, world.CurrentTick);
    }

    // ---- AC-4: owner thread ----

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

    [Fact]
    public void Ac4_Tick_FromAnotherThread_AssertsInAssertBuilds_AndCountsBadTickOtherwise()
    {
        var probe = new LogProbe();
        SimWorld world = Frozen(probe);
        world.Tick();
        probe.Log.Clear();

        Exception? caught = RunOnOtherThread(world.Tick);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.IsType<SimAssertException>(caught);
            Assert.Equal(SimAssertCode.TickWrongThread, ex.Code);
        }
        else
        {
            Assert.Null(caught);
            Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        }

        Assert.Empty(probe.Log);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Equal(SimState.Running, world.State);

        world.Tick();

        Assert.Equal(2u, world.CurrentTick);
    }

    [Fact]
    public void Ac4_Tick_FromTheFreezingThread_NeverAsserts()
    {
        SimWorld world = Frozen(new LogProbe());

        Exception? caught = Record.Exception(world.Tick);

        Assert.Null(caught);
        Assert.Equal(0L, world.Counters[CounterId.BadTick]);
    }

    [Fact]
    public void Ac4_OwnerThread_IsTheThreadThatFroze()
    {
        SimWorld? world = null;
        Exception? freezeError = RunOnOtherThread(() => world = Frozen(new LogProbe()));
        Assert.Null(freezeError);

        // The test thread is not the freezing thread.
        Exception? caught = Record.Exception(world!.Tick);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.IsType<SimAssertException>(caught);
        }
        else
        {
            Assert.Null(caught);
            Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        }

        Assert.Equal(0u, world.CurrentTick);
    }

    [Fact]
    public void Ac4_Stop_AndRequestStop_FromAnotherThread_AssertOnlyInAssertBuilds()
    {
        SimWorld world = Frozen(new LogProbe());
        world.Tick();

        Exception? stopError = RunOnOtherThread(() => world.Stop());
        Exception? requestError = RunOnOtherThread(() => world.RunControl.RequestStop(StopReason.Unspecified));

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.IsType<SimAssertException>(stopError);
            Assert.IsType<SimAssertException>(requestError);
            Assert.Equal(SimState.Running, world.State);
            Assert.Equal(0L, world.Counters[CounterId.StopOutOfPhase]);
            Assert.Equal(0L, world.Counters[CounterId.StopIgnored]);
        }
        else
        {
            // No thread check: Stop() takes effect, then the request finds a stopped world and is ignored and counted.
            Assert.Null(stopError);
            Assert.Null(requestError);
            Assert.Equal(SimState.Stopped, world.State);
            Assert.Equal(1L, world.Counters[CounterId.StopOutOfPhase]);
            Assert.Equal(1L, world.Counters[CounterId.StopIgnored]);
        }
    }
}

/// <summary>
/// Zero-allocation guardrail for the lifecycle guards and stage checks (story 012). A separate non-parallel class: runtime
/// start-up work (tiered JIT call counting, type loading) on another test's thread must not land in a measured window.
/// <see cref="AllocAssert"/> warms the same window up and passes if any measured window is clean; a real per-call
/// allocation shows up in every window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class WorldLifecycleAllocationTests
{
    private sealed class NoopPhase : ISimPhase
    {
        public int Sink;

        public void Run(in TickContext ctx) => Sink += (int)ctx.Tick;
    }

    [Fact]
    public void Guards_AndStageChecks_AllocateNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var noop = new NoopPhase();
        var builder = new SimWorldBuilder();
        for (int p = 1; p <= 12; p++)
        {
            builder.Bind((PhaseId)p, noop);
        }

        SimWorld world = builder.Freeze();
        SimWorld stopped = new SimWorldBuilder().Freeze();
        stopped.Tick();
        Assert.True(stopped.Stop());
        for (int i = 0; i < 16; i++)
        {
            world.Tick();
            stopped.Tick();
        }

        // Act + Assert (0 B)
        int runs = AllocAssert.Zero("1,000 rounds of Tick, stopped Tick, ignored RequestStop and Stage/Phase reads", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                world.Tick();
                stopped.Tick();
                stopped.RunControl.RequestStop(StopReason.Unspecified);
                _ = world.Stage;
                _ = world.Phase;
            }
        });

        // Assert
        Assert.Equal((uint)(16 + (1000 * runs)), world.CurrentTick);
        Assert.Equal(16L + (1000L * runs), stopped.Counters[CounterId.BadTick]);
        Assert.Equal(1000L * runs, stopped.Counters[CounterId.StopIgnored]);
    }
}
