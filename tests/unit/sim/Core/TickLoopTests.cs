using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-002, TR-sim-010, TR-sim-011 and TR-sim-041 (ADR-0003 subset) for story 011: the phase table, the
/// builder binds and <see cref="SimWorld.Tick"/> (GDD SC-R1-1 and SC-R3-1; AC-1 to AC-3). No time, I/O or global state.
/// </summary>
public class TickLoopTests
{
    /// <summary>Appends a labelled entry to a shared log; also exposes what the reader saw when it ran.</summary>
    private sealed class LogPhase : ISimPhase
    {
        private readonly List<string> _log;
        private readonly string _label;
        private readonly Func<ITickReader?> _reader;

        public LogPhase(List<string> log, string label, Func<ITickReader?>? reader = null)
        {
            _log = log;
            _label = label;
            _reader = reader ?? (() => null);
        }

        public int Calls { get; private set; }

        public void Run(in TickContext ctx)
        {
            Calls++;
            ITickReader? r = _reader();
            string seen = r is null ? string.Empty : $"|{r.Stage}/{r.Phase}/{r.CurrentTick}";
            _log.Add($"{_label}@{ctx.Phase}:{ctx.Tick}{seen}");
        }
    }

    private sealed class LogCheck : ITickEndCheck
    {
        private readonly List<string> _log;
        private readonly string _label;
        private readonly Func<ITickReader?> _reader;

        public LogCheck(List<string> log, string label, Func<ITickReader?>? reader = null)
        {
            _log = log;
            _label = label;
            _reader = reader ?? (() => null);
        }

        public void Check(uint tick)
        {
            ITickReader? r = _reader();
            string seen = r is null ? string.Empty : $"|{r.Stage}";
            _log.Add($"{_label}:{tick}{seen}");
        }
    }

    private sealed class LogProbe : IPhaseProbe
    {
        private readonly List<string> _log;

        public LogProbe(List<string> log)
        {
            _log = log;
        }

        public void Before(PhaseId phase) => _log.Add($"B{(int)phase}");

        public void After(PhaseId phase) => _log.Add($"A{(int)phase}");
    }

    private sealed class NoopPhase : ISimPhase
    {
        public int Sink;

        public void Run(in TickContext ctx) => Sink += (int)ctx.Tick;
    }

    private sealed class NoopCheck : ITickEndCheck
    {
        public int Sink;

        public void Check(uint tick) => Sink += (int)tick;
    }

    private sealed class TimerPhase : ISimPhase
    {
        private readonly ITickReader _reader;
        private readonly uint _deadlineTick;

        public TimerPhase(ITickReader reader, uint deadlineTick)
        {
            _reader = reader;
            _deadlineTick = deadlineTick;
        }

        public List<uint> CurrentTickSeen { get; } = new();

        public List<uint> ContextTickSeen { get; } = new();

        public List<bool> DueSeen { get; } = new();

        public void Run(in TickContext ctx)
        {
            CurrentTickSeen.Add(_reader.CurrentTick);
            ContextTickSeen.Add(ctx.Tick);
            DueSeen.Add(_reader.CurrentTick >= _deadlineTick);
        }
    }

    // ---- AC-1 (SC-R1-1) ----

    [Fact]
    [Trait("Id", "SC-R1-1")]
    public void Ac1_TickCountsFromZero_AndTimerIsDueAtItsAbsoluteDeadline()
    {
        var builder = new SimWorldBuilder();
        // The world does not exist until Freeze, so the phase reads it through a holder bound after Freeze is not
        // possible; instead bind a forwarding reader that resolves once the world exists.
        var holder = new ReaderHolder();
        var timer = new TimerPhase(holder, deadlineTick: 100);
        builder.Bind(PhaseId.PlayersStep, timer);
        SimWorld world = builder.Freeze();
        holder.Target = world;
        Assert.Equal(0u, world.CurrentTick);
        Assert.Equal(SimState.Ready, world.State);

        var after = new List<uint>();
        for (int i = 0; i < 101; i++)
        {
            world.Tick();
            after.Add(world.CurrentTick);
        }

        Assert.Equal(101, timer.CurrentTickSeen.Count);
        Assert.Equal(0u, timer.CurrentTickSeen[0]);
        Assert.Equal(0u, timer.ContextTickSeen[0]);
        Assert.Equal(1u, after[0]);
        Assert.Equal(101u, after[100]);
        Assert.Equal(100u, timer.CurrentTickSeen[100]);
        for (int i = 0; i < 101; i++)
        {
            Assert.Equal((uint)i, timer.CurrentTickSeen[i]);
            Assert.Equal((uint)i, timer.ContextTickSeen[i]);
            Assert.Equal((uint)(i + 1), after[i]);
        }

        Assert.False(timer.DueSeen[99]);
        Assert.True(timer.DueSeen[100]);
        Assert.Equal(SimState.Running, world.State);
    }

    /// <summary>Forwards to the world once it exists (binds happen before Freeze creates it).</summary>
    private sealed class ReaderHolder : ITickReader
    {
        public ITickReader? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;
    }

    [Fact]
    [Trait("Id", "SC-R1-1")]
    public void FreshWorld_IsReady_AtTickZero_AndFirstTickMovesToRunning()
    {
        SimWorld world = new SimWorldBuilder().Freeze();

        Assert.Equal(SimState.Ready, world.State);
        Assert.Equal(0u, world.CurrentTick);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);

        world.Tick();

        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(1u, world.CurrentTick);
    }

    // ---- AC-2 (SC-R3-1) ----

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void Ac2_OneTick_RunsPhasesInOrder_WithBindOrderPreserved_AndProbeBrackets()
    {
        var log = new List<string>();
        var holder = new ReaderHolder();
        var builder = new SimWorldBuilder(probe: new LogProbe(log));
        for (int p = 1; p <= 12; p++)
        {
            if (p == 11)
            {
                continue;
            }

            builder.Bind((PhaseId)p, new LogPhase(log, "S", () => holder));
        }

        builder.Bind(PhaseId.CombatReact, new LogPhase(log, "X", () => holder));
        builder.Bind(PhaseId.CombatReact, new LogPhase(log, "Y", () => holder));
        builder.BindTickEnd(new LogCheck(log, "C1", () => holder));
        builder.BindTickEnd(new LogCheck(log, "C2", () => holder));
        SimWorld world = builder.Freeze();
        holder.Target = world;

        world.Tick();

        var expected = new List<string> { "B0", "A0" };
        for (int p = 1; p <= 12; p++)
        {
            expected.Add($"B{p}");
            if (p == 11)
            {
                expected.Add($"X@CombatReact:0|InPhase/CombatReact/0");
                expected.Add($"Y@CombatReact:0|InPhase/CombatReact/0");
            }
            else
            {
                expected.Add($"S@{(PhaseId)p}:0|InPhase/{(PhaseId)p}/0");
            }

            expected.Add($"A{p}");
        }

        expected.Add("C1:0|TickEnd");
        expected.Add("C2:0|TickEnd");
        expected.Add("B13");
        expected.Add("A13");
        Assert.Equal(expected, log);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);
        Assert.Equal(1u, world.CurrentTick);
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void SamePhaseBinders_RunInBindOrder_EveryTick()
    {
        var log = new List<string>();
        var builder = new SimWorldBuilder();
        builder.Bind(PhaseId.MailStep, new LogPhase(log, "first"));
        builder.Bind(PhaseId.AgentsStep, new LogPhase(log, "agents"));
        builder.Bind(PhaseId.MailStep, new LogPhase(log, "second"));
        builder.Bind(PhaseId.MailStep, new LogPhase(log, "third"));
        SimWorld world = builder.Freeze();

        world.Tick();
        world.Tick();

        Assert.Equal(
            new[]
            {
                "agents@AgentsStep:0", "first@MailStep:0", "second@MailStep:0", "third@MailStep:0",
                "agents@AgentsStep:1", "first@MailStep:1", "second@MailStep:1", "third@MailStep:1",
            },
            log);
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void ReaderPhase_MatchesContextPhase_InsideEveryBoundPhase()
    {
        var holder = new ReaderHolder();
        var mismatches = new List<string>();
        var builder = new SimWorldBuilder();
        for (int p = 1; p <= 12; p++)
        {
            builder.Bind((PhaseId)p, new ReaderCheckPhase(holder, mismatches));
        }

        SimWorld world = builder.Freeze();
        holder.Target = world;

        world.Tick();

        Assert.Empty(mismatches);
    }

    private sealed class ReaderCheckPhase : ISimPhase
    {
        private readonly ITickReader _reader;
        private readonly List<string> _mismatches;

        public ReaderCheckPhase(ITickReader reader, List<string> mismatches)
        {
            _reader = reader;
            _mismatches = mismatches;
        }

        public void Run(in TickContext ctx)
        {
            if (_reader.Phase != ctx.Phase || _reader.Stage != TickStage.InPhase)
            {
                _mismatches.Add(ctx.Phase.ToString());
            }
        }
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void PhaseId_HasExactlyFourteenValues_InTheFixedOrder()
    {
        PhaseId[] all = Enum.GetValues<PhaseId>();

        Assert.Equal(14, all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            Assert.Equal(i, (int)all[i]);
        }

        Assert.Equal(PhaseId.InputsApply, all[0]);
        Assert.Equal(PhaseId.RunStep, all[12]);
        Assert.Equal(PhaseId.EventsFlush, all[13]);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(PhaseId)));
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void TickEndChecks_RunOncePerTick_InBindOrder_AfterPhase12AndBeforePhase13()
    {
        var log = new List<string>();
        var builder = new SimWorldBuilder(probe: new LogProbe(log));
        builder.BindTickEnd(new LogCheck(log, "C1"));
        builder.BindTickEnd(new LogCheck(log, "C2"));
        SimWorld world = builder.Freeze();

        world.Tick();
        world.Tick();

        int a12 = log.IndexOf("A12");
        int c1 = log.IndexOf("C1:0");
        int c2 = log.IndexOf("C2:0");
        int b13 = log.IndexOf("B13");
        Assert.True(a12 < c1 && c1 < c2 && c2 < b13);
        Assert.Contains("C1:1", log);
        Assert.Equal(2, log.FindAll(s => s.StartsWith("C1:", StringComparison.Ordinal)).Count);
        Assert.Equal(2, log.FindAll(s => s.StartsWith("C2:", StringComparison.Ordinal)).Count);
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void UnboundPhases_AreNoOps_ProbeStillBracketsAllFourteen()
    {
        var log = new List<string>();
        var builder = new SimWorldBuilder(probe: new LogProbe(log));
        var only = new LogPhase(log, "S");
        builder.Bind(PhaseId.EconomyFlush, only);
        SimWorld world = builder.Freeze();

        world.Tick();

        Assert.Equal(1, only.Calls);
        Assert.Equal(28 + 1, log.Count);
    }

    // ---- AC-3 ----

    [Theory]
    [Trait("Id", "SC-R3-1")]
    [InlineData(PhaseId.InputsApply)]
    [InlineData(PhaseId.EventsFlush)]
    public void Ac3_BindingReservedPhase_ThrowsReservedPhase(PhaseId reserved)
    {
        var builder = new SimWorldBuilder();

        var ex = Assert.Throws<SimBuildException>(() => builder.Bind(reserved, new NoopPhase()));

        Assert.Equal(SimBuildErrorCode.ReservedPhase, ex.Code);
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void Ac3_BindAndBindTickEndAfterFreeze_ThrowBindAfterFreeze()
    {
        var builder = new SimWorldBuilder();
        builder.Freeze();

        var bind = Assert.Throws<SimBuildException>(() => builder.Bind(PhaseId.MailStep, new NoopPhase()));
        var bindReserved = Assert.Throws<SimBuildException>(() => builder.Bind(PhaseId.InputsApply, new NoopPhase()));
        var bindEnd = Assert.Throws<SimBuildException>(() => builder.BindTickEnd(new NoopCheck()));
        var freezeAgain = Assert.Throws<SimBuildException>(() => builder.Freeze());

        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, bind.Code);
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, bindReserved.Code);
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, bindEnd.Code);
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, freezeAgain.Code);
    }

    [Fact]
    [Trait("Id", "SC-R3-1")]
    public void Ac3_WorldWithNoBinds_Ticks()
    {
        SimWorld world = new SimWorldBuilder().Freeze();

        for (int i = 0; i < 5; i++)
        {
            world.Tick();
        }

        Assert.Equal(5u, world.CurrentTick);
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);
    }

    [Fact]
    public void Bind_InvalidArguments_ThrowArgumentExceptions_NotBuildExceptions()
    {
        var builder = new SimWorldBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Bind(PhaseId.MailStep, null!));
        Assert.Throws<ArgumentNullException>(() => builder.BindTickEnd(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Bind((PhaseId)14, new NoopPhase()));
    }

    [Fact]
    public void FrozenTables_AreCopies_LaterBuilderMutationCannotReachTheWorld()
    {
        var log = new List<string>();
        var builder = new SimWorldBuilder();
        builder.Bind(PhaseId.MailStep, new LogPhase(log, "a"));
        SimWorld world = builder.Freeze();

        Assert.Throws<SimBuildException>(() => builder.Bind(PhaseId.MailStep, new LogPhase(log, "late")));
        world.Tick();

        Assert.Equal(new[] { "a@MailStep:0" }, log);
    }

    // ---- Settings, reader and probe defaults ----

    [Fact]
    public void SimCoreSettings_Defaults_MatchTheF8Values()
    {
        var s = new SimCoreSettings();

        Assert.Equal(4096, s.OpQueueCapacity);
        Assert.Equal(64, s.SystemOpReserve);
        Assert.Equal(64, s.MaxOpsDrainedPerTick);
        Assert.Equal(4096, s.EventLogCapacity);
        Assert.Equal(256, s.EventLogReserve);
        Assert.Equal(8, s.MaxEventReaders);
        Assert.Equal(4, s.MaxCatchUpTicksPerFrame);
        Assert.Equal(256, s.CheckRingCapacity);
        Assert.Equal(s, new SimCoreSettings());
        Assert.NotEqual(s, s with { OpQueueCapacity = 1 });
    }

    [Fact]
    public void World_ExposesBuilderSuppliedSettingsCountersAndRing_AndDefaultsPerWorld()
    {
        var settings = new SimCoreSettings { CheckRingCapacity = 8 };
        var counters = new SimCounters();
        var ring = new SimCheckRing(8);

        SimWorld w = new SimWorldBuilder(settings, counters, ring).Freeze();
        SimWorld d1 = new SimWorldBuilder().Freeze();
        SimWorld d2 = new SimWorldBuilder().Freeze();

        Assert.Same(settings, w.Settings);
        Assert.Same(counters, w.Counters);
        Assert.Same(ring, w.CheckRing);
        Assert.Equal(new SimCoreSettings(), d1.Settings);
        Assert.NotSame(d1.Counters, d2.Counters);
        Assert.NotSame(d1.CheckRing, d2.CheckRing);
    }

    [Fact]
    public void TickReader_Stage_IsBetweenTicksBeforeAndAfterTick_AndInPhaseAndTickEndInside()
    {
        var holder = new ReaderHolder();
        var stages = new List<TickStage>();
        var builder = new SimWorldBuilder();
        builder.Bind(PhaseId.RunStep, new StageRecorder(holder, stages));
        builder.BindTickEnd(new StageRecorder(holder, stages));
        SimWorld world = builder.Freeze();
        holder.Target = world;
        ITickReader reader = world;

        Assert.Equal(TickStage.BetweenTicks, reader.Stage);
        world.Tick();

        Assert.Equal(new[] { TickStage.InPhase, TickStage.TickEnd }, stages);
        Assert.Equal(TickStage.BetweenTicks, reader.Stage);
        Assert.Equal(1u, reader.CurrentTick);
    }

    private sealed class StageRecorder : ISimPhase, ITickEndCheck
    {
        private readonly ITickReader _reader;
        private readonly List<TickStage> _stages;

        public StageRecorder(ITickReader reader, List<TickStage> stages)
        {
            _reader = reader;
            _stages = stages;
        }

        public void Run(in TickContext ctx) => _stages.Add(_reader.Stage);

        public void Check(uint tick) => _stages.Add(_reader.Stage);
    }

    [Fact]
    public void DefaultProbe_IsANullObject_AndIsSafeToCallForEveryPhase()
    {
        IPhaseProbe probe = new NullPhaseProbe();

        foreach (PhaseId p in Enum.GetValues<PhaseId>())
        {
            probe.Before(p);
            probe.After(p);
        }

        SimWorld world = new SimWorldBuilder(probe: null).Freeze();
        world.Tick();
        Assert.Equal(1u, world.CurrentTick);
    }

    [Fact]
    public void TickContext_CarriesTickAndPhase_AndIsAReadonlyStruct()
    {
        var ctx = new TickContext(7u, PhaseId.MailStep);

        Assert.Equal(7u, ctx.Tick);
        Assert.Equal(PhaseId.MailStep, ctx.Phase);
        Assert.True(typeof(TickContext).IsValueType);
        Assert.True(typeof(TickContext).GetCustomAttributes(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false).Length > 0);
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="SimWorld.Tick"/> (story 011). A separate non-parallel class: runtime start-up
/// work (tiered JIT call counting, type loading) on another test's thread must not land in a measured window.
/// <see cref="AllocAssert"/> warms the same window up and passes if any measured window is clean; a real per-tick
/// allocation shows up in every window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class TickLoopAllocationTests
{
    private sealed class NoopPhase : ISimPhase
    {
        public int Sink;

        public void Run(in TickContext ctx) => Sink += (int)ctx.Tick;
    }

    private sealed class NoopCheck : ITickEndCheck
    {
        public int Sink;

        public void Check(uint tick) => Sink += (int)tick;
    }

    [Fact]
    public void Tick_WithBoundStubPhases_AllocatesNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var phase = new NoopPhase();
        var check = new NoopCheck();
        var builder = new SimWorldBuilder();
        for (int p = 1; p <= 12; p++)
        {
            builder.Bind((PhaseId)p, phase);
        }

        builder.Bind(PhaseId.CombatReact, phase);
        builder.BindTickEnd(check);
        builder.BindTickEnd(check);
        SimWorld world = builder.Freeze();
        for (int i = 0; i < 16; i++)
        {
            world.Tick();
        }

        // Act + Assert (0 B)
        int runs = AllocAssert.Zero("1,000 ticks with 13 stub phases and 2 tick-end checks", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                world.Tick();
            }
        });

        // Assert
        Assert.Equal((uint)(16 + (1000 * runs)), world.CurrentTick);
        Assert.NotEqual(0, phase.Sink);
        Assert.NotEqual(0, check.Sink);
    }
}
