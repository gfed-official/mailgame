using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// A recording <see cref="IEventSink"/> stub. Copies every delivered entry into a list (test code may allocate), notes the
/// tick, the world's reported stage and phase at the moment of the call, and optionally runs a hook inside <c>Deliver</c>.
/// </summary>
internal sealed class RecordingSink : IEventSink
{
    public int Calls { get; private set; }

    public List<uint> Ticks { get; } = new();

    public List<EventEntry> Entries { get; } = new();

    public List<OverflowInfo> Overflows { get; } = new();

    public ITickReader? Reader { get; set; }

    public TickStage StageAtDeliver { get; private set; }

    public PhaseId PhaseAtDeliver { get; private set; }

    public Action? OnDeliver { get; set; }

    public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
    {
        Calls++;
        Ticks.Add(tick);
        Overflows.Add(overflow);
        foreach (EventEntry e in entries)
        {
            Entries.Add(e);
        }

        if (Reader is not null)
        {
            StageAtDeliver = Reader.Stage;
            PhaseAtDeliver = Reader.Phase;
        }

        OnDeliver?.Invoke();
    }
}

/// <summary>
/// Covers TR-sim-022 for story 015 (GDD SC-R12-2; AC-1 delivery, AC-2 lifetime, AC-3 phase-run scopes through a world):
/// <see cref="IEventSink.Deliver"/> once per tick from phase 13 in raise order with every entry, the closed Sim during
/// <c>Deliver</c> (<c>EnqueueOp</c>, <c>Tick</c> and raises), the log reset after delivery, no sink, a faulted tick, a
/// throwing sink and the per-phase-run call scope. Behaviour that differs between SIM_ASSERT and other builds branches on
/// <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
[Trait("Id", "SC-R12-2")]
public class EventDeliveryTests
{
    private static readonly ActorId _actor = new(0x0100_0005u);

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

    private static SimWorld Frozen(RecordingSink? sink, params (PhaseId Phase, ISimPhase Binder)[] binds)
    {
        SimWorldBuilder builder = TestKinds.Builder();
        foreach ((PhaseId phase, ISimPhase binder) in binds)
        {
            builder.Bind(phase, binder);
        }

        if (sink is not null)
        {
            builder.BindEventSink(sink);
        }

        SimWorld world = builder.Freeze();
        if (sink is not null)
        {
            sink.Reader = world;
        }

        return world;
    }

    private static void Raise(SimWorld world, EventKind kind, byte audience, uint tag)
    {
        var p = new Pay12 { A = tag };
        Assert.True(world.Events.TryRaise(kind, audience, in p));
    }

    /// <summary>Ticks a world whose phase throws: SIM_ASSERT builds rethrow the original exception, others return.</summary>
    private static void TickExpectingFault<TException>(SimWorld world)
        where TException : Exception
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<TException>(world.Tick);
        }
        else
        {
            world.Tick();
        }
    }

    // ---------------------------------------------------------------- AC-1

    [Fact]
    public void Ac1_MixedClasses_DeliveredOnceFromPhase13_EveryEntryInRaiseOrder()
    {
        // Arrange: phases 3, 8 and 12 each raise; one Internal, two Replicated, one OwnerOnly, one Targeted.
        var sink = new RecordingSink();
        SimWorld? world = null;
        var players = new ActionPhase(_ =>
        {
            var p4 = new Pay4 { A = 10 };
            Assert.True(world!.Events.TryRaise(TestKinds.InternalKind, 0b1111, in p4));
            Raise(world, TestKinds.ReplicatedKind, 0b0000_0111, 11);
        });
        var mail = new ActionPhase(_ =>
        {
            Raise(world!, TestKinds.OwnerOnlyKind, 0b0000_0100, 12);
            Raise(world!, TestKinds.ReplicatedKind, 0b0000_0011, 13);
        });
        var run = new ActionPhase(_ => Raise(world!, TestKinds.TargetedKind, 0b1000_0001, 14));
        world = Frozen(sink, (PhaseId.PlayersStep, players), (PhaseId.MailStep, mail), (PhaseId.RunStep, run));

        // Act
        world.Tick();

        // Assert: exactly one call, with the tick number, from phase 13.
        Assert.Equal(1, sink.Calls);
        Assert.Equal(new uint[] { 0 }, sink.Ticks);
        Assert.Equal(PhaseId.EventsFlush, sink.PhaseAtDeliver);
        Assert.Equal(TickStage.InPhase, sink.StageAtDeliver);

        // Assert: all five entries, Internal included, in raise order with Ordinal 0..4.
        Assert.Equal(5, sink.Entries.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((uint)i, sink.Entries[i].Ordinal);
        }

        Assert.Equal(
            new[] { TestKinds.InternalKind, TestKinds.ReplicatedKind, TestKinds.OwnerOnlyKind, TestKinds.ReplicatedKind, TestKinds.TargetedKind },
            sink.Entries.Select(e => e.Kind).ToArray());
        Assert.Equal(
            new[] { ReplicationClass.Internal, ReplicationClass.Replicated, ReplicationClass.OwnerOnly, ReplicationClass.Replicated, ReplicationClass.Targeted },
            sink.Entries.Select(e => e.Class).ToArray());
        Assert.Equal(new byte[] { 0, 0b0111, 0b0100, 0b0011, 0b1000_0001 }, sink.Entries.Select(e => e.Audience).ToArray());
        Assert.Equal(new uint[] { 10, 11, 12, 13, 14 }, sink.Entries.Select(e => e.PayloadAs<Pay12>().A).ToArray());
    }

    [Fact]
    public void Ac1_EmptyTick_StillDeliversOnce_WithAnEmptySpanAndNoOverflow()
    {
        var sink = new RecordingSink();
        SimWorld world = Frozen(sink);

        world.Tick();

        Assert.Equal(1, sink.Calls);
        Assert.Empty(sink.Entries);
        Assert.False(sink.Overflows[0].Overflow);
        Assert.Equal(0u, sink.Overflows[0].DroppedCount);
        Assert.Equal(0UL, sink.Overflows[0].DroppedKindsMask);
        Assert.Equal((byte)0, sink.Overflows[0].DroppedAudienceMask);
    }

    [Fact]
    public void Ac1_EveryTick_DeliversOnce_AndOrdinalsRestartAtZero()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(tick =>
        {
            Raise(world!, TestKinds.ReplicatedKind, 0b1, tick * 10);
            Raise(world!, TestKinds.ReplicatedKind, 0b1, (tick * 10) + 1);
        });
        world = Frozen(sink, (PhaseId.PlayersStep, phase));

        world.Tick();
        world.Tick();
        world.Tick();

        Assert.Equal(3, sink.Calls);
        Assert.Equal(new uint[] { 0, 1, 2 }, sink.Ticks);
        Assert.Equal(new uint[] { 0, 1, 0, 1, 0, 1 }, sink.Entries.Select(e => e.Ordinal).ToArray());
        Assert.Equal(new uint[] { 0, 1, 10, 11, 20, 21 }, sink.Entries.Select(e => e.PayloadAs<Pay12>().A).ToArray());
    }

    [Fact]
    public void Ac1_EnqueueOpInsideDeliver_ReturnsFalse_CountsEnqueueDuringFlush_AndAssertsUnderSimAssert()
    {
        // Arrange
        var sink = new RecordingSink();
        SimWorld world = Frozen(sink);
        bool? accepted = null;
        ushort? assertCode = null;
        sink.OnDeliver = () =>
        {
            Op op = new() { Kind = OpKind.TeleportAck, ReqId = 1 };
            try
            {
                accepted = world.EnqueueOp(_actor, in op);
            }
            catch (SimAssertException ex)
            {
                assertCode = ex.Code;
            }
        };

        // Act
        world.Tick();

        // Assert
        Assert.Equal(1L, world.Counters[CounterId.EnqueueDuringFlush]);
        Assert.Equal(0, world.Ops.Count);
        Assert.Equal(0u, world.LastOpSeq);
        Assert.Equal(0L, world.Counters[CounterId.EnqueueBadState]);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Null(accepted);
            Assert.Equal(SimAssertCode.EventEnqueueDuringFlush, assertCode);
        }
        else
        {
            Assert.False(accepted);
            Assert.Null(assertCode);
        }

        // The Sim is open again once Deliver returned.
        Op later = new() { Kind = OpKind.TeleportAck, ReqId = 2 };
        Assert.True(world.EnqueueOp(_actor, in later));
        Assert.Equal(1L, world.Counters[CounterId.EnqueueDuringFlush]);
        Assert.False(world.IsDelivering);
    }

    [Fact]
    public void Ac1_ReentrantTickInsideDeliver_CountsBadTick_RunsNothing_AndAssertsUnderSimAssert()
    {
        // Arrange
        var sink = new RecordingSink();
        var phase = new ActionPhase(_ => { });
        SimWorld world = Frozen(sink, (PhaseId.PlayersStep, phase));
        ushort? assertCode = null;
        sink.OnDeliver = () =>
        {
            try
            {
                world.Tick();
            }
            catch (SimAssertException ex)
            {
                assertCode = ex.Code;
            }
        };

        // Act
        world.Tick();

        // Assert: the nested Tick ran no phase and delivered nothing more; the outer tick completed normally.
        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
        Assert.Equal(1, phase.Calls);
        Assert.Equal(1, sink.Calls);
        Assert.Equal(1u, world.CurrentTick);
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(TestBuildExpectation.SimAssertsExpected ? (ushort?)SimAssertCode.EventTickDuringFlush : null, assertCode);
    }

    [Fact]
    public void Ac1_RaiseInsideDeliver_IsDropped_AndTheDeliveredSpanIsUnchanged()
    {
        // Arrange
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(_ => Raise(world!, TestKinds.ReplicatedKind, 0b1, 1));
        world = Frozen(sink, (PhaseId.PlayersStep, phase));
        bool? raised = null;
        ushort? assertCode = null;
        sink.OnDeliver = () =>
        {
            var p = new Pay12 { A = 2 };
            try
            {
                raised = world.Events.TryRaise(TestKinds.ReplicatedKind, 0b1, in p);
            }
            catch (SimAssertException ex)
            {
                assertCode = ex.Code;
            }
        };

        // Act
        world.Tick();

        // Assert: dropped (Release) or asserted (SIM_ASSERT); either way the delivered span is unchanged.
        Assert.Equal(TestBuildExpectation.SimAssertsExpected ? null : false, raised);
        Assert.Equal(TestBuildExpectation.SimAssertsExpected ? (ushort?)SimAssertCode.EventRaiseDuringFlush : null, assertCode);
        Assert.Single(sink.Entries);
        Assert.Equal(0, world.Events.Count);
    }

    [Fact]
    public void Ac1_RaiseAfterDeliver_BetweenTicks_IsMisuse_AndTheLogIsOpenAgainOnTheNextTick()
    {
        // Story 016 AC-2: a between-ticks raise is misuse (asserts, or dropped and counted). Story 015's intent still holds:
        // Deliver reopens the log, so the next tick's own raises are accepted from Ordinal 0.
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(tick => Raise(world!, TestKinds.ReplicatedKind, 0b1, tick));
        world = Frozen(sink, (PhaseId.PlayersStep, phase));
        world.Tick();
        var p = new Pay12();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.EventRaiseOutsideTick, Assert.Throws<SimAssertException>(() => world.Events.TryRaise(TestKinds.ReplicatedKind, 0b1, in p)).Code);
        }
        else
        {
            Assert.False(world.Events.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
            Assert.Equal(1L, world.Counters[CounterId.EventsOutsideTick]);
        }

        Assert.Equal(0, world.Events.Count);
        world.Tick();
        Assert.Equal(new uint[] { 0, 0 }, sink.Entries.Select(e => e.Ordinal).ToArray());
        Assert.Equal(new uint[] { 0, 1 }, sink.Entries.Select(e => e.PayloadAs<Pay12>().A).ToArray());
    }

    // ---------------------------------------------------------------- AC-2

    [Fact]
    public void Ac2_AfterDeliver_CountIsZero_AndTheUsedSpanReadsDdUnderSimAssert()
    {
        // Arrange
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(_ =>
        {
            Raise(world!, TestKinds.ReplicatedKind, 0b1, 1);
            Raise(world!, TestKinds.OwnerOnlyKind, 0b10, 2);
            Raise(world!, TestKinds.Undeclared, 0, 3);
        });
        world = Frozen(sink, (PhaseId.PlayersStep, phase));

        // Act
        world.Tick();

        // Assert
        Assert.Equal(3, sink.Entries.Count);
        Assert.Equal(0, world.Events.Count);
        Assert.Equal(0u, world.Events.CurrentCallSeq);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            ReadOnlySpan<byte> used = System.Runtime.InteropServices.MemoryMarshal.AsBytes(world.Events.Storage[..3]);
            foreach (byte b in used)
            {
                Assert.Equal((byte)0xDD, b);
            }
        }
    }

    [Fact]
    public void Ac2_NoSinkBound_TickCompletes_AndTheLogStillResets()
    {
        // Arrange
        SimWorld? world = null;
        var phase = new ActionPhase(_ => Raise(world!, TestKinds.ReplicatedKind, 0b1, 1));
        world = Frozen(null, (PhaseId.PlayersStep, phase));

        // Act
        world.Tick();
        world.Tick();

        // Assert: both ticks completed and each tick's log started empty (Ordinal 0 again, count back to 0).
        Assert.Equal(2u, world.CurrentTick);
        Assert.Equal(SimState.Running, world.State);
        Assert.Equal(0, world.Events.Count);
        Assert.Equal(0L, world.Counters[CounterId.BadTick]);
    }

    [Fact]
    public void Ac2_NoSinkBound_ATickTwoRaise_GetsOrdinalZero()
    {
        // TD-033 (1): with no sink the log must still reset, so tick 2's first raise is Ordinal 0, not 1.
        SimWorld? world = null;
        uint? ordinalInTick2 = null;
        var phase = new ActionPhase(tick =>
        {
            Raise(world!, TestKinds.ReplicatedKind, 0b1, tick);
            if (tick == 1)
            {
                ordinalInTick2 = world!.Events.Span[0].Ordinal;
            }
        });
        world = Frozen(null, (PhaseId.PlayersStep, phase));

        world.Tick();
        world.Tick();

        Assert.Equal(0u, ordinalInTick2);
    }

    [Fact]
    public void Ac2_FaultedTick_NeverCallsDeliver_AndTheLogIsResetWithoutDelivery()
    {
        // Arrange: an earlier phase raises, a later one throws.
        var sink = new RecordingSink();
        SimWorld? world = null;
        var raiser = new ActionPhase(_ => Raise(world!, TestKinds.ReplicatedKind, 0b1, 1));
        var thrower = new ActionPhase(_ => throw new InvalidOperationException("boom"));
        world = Frozen(sink, (PhaseId.PlayersStep, raiser), (PhaseId.MailStep, thrower));

        // Act
        TickExpectingFault<InvalidOperationException>(world);

        // Assert
        Assert.Equal(SimState.Faulted, world.State);
        Assert.Equal(0, sink.Calls);
        Assert.Equal(0, world.Events.Count);
        Assert.Equal(0u, world.Events.CurrentCallSeq);
        Assert.Equal(0, world.Events.ScopeDepth);

        // A faulted world refuses further ticks, still without delivering.
        world.Tick();
        Assert.Equal(0, sink.Calls);
        Assert.Equal(1L, world.Counters[CounterId.BadTick]);
    }

    [Fact]
    public void Ac2_FaultInsideAScope_ResetsTheScopeState()
    {
        // The phase throws while its run scope (and a nested API scope) is open: the world must not carry that depth forward.
        var sink = new RecordingSink();
        SimWorld? world = null;
        var thrower = new ActionPhase(tick =>
        {
            _ = world!.Events.BeginCall();
            throw new InvalidOperationException("boom");
        });
        world = Frozen(sink, (PhaseId.PlayersStep, thrower));

        TickExpectingFault<InvalidOperationException>(world);

        Assert.Equal(0, world.Events.ScopeDepth);
        Assert.Equal(0u, world.Events.CurrentCallSeq);
    }

    [Fact]
    public void Ac2_ThrowingSink_FaultsTheWorld_ResetsTheLog_AndReopensTheSim()
    {
        // Arrange
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(_ => Raise(world!, TestKinds.ReplicatedKind, 0b1, 1));
        world = Frozen(sink, (PhaseId.PlayersStep, phase));
        sink.OnDeliver = () => throw new InvalidOperationException("sink boom");

        // Act
        TickExpectingFault<InvalidOperationException>(world);

        // Assert: same outcome as an exception escaping a phase.
        Assert.Equal(SimState.Faulted, world.State);
        Assert.Equal(1, sink.Calls);
        Assert.Equal(0u, world.CurrentTick);
        Assert.Equal(TickStage.BetweenTicks, world.Stage);
        Assert.Equal(0, world.Events.Count);
        Assert.False(world.IsDelivering);

        // The flush flag is clear: a refused enqueue is a bad-state refusal, not a during-flush one.
        Op op = new() { Kind = OpKind.TeleportAck, ReqId = 1 };
        Assert.False(world.EnqueueOp(_actor, in op));
        Assert.Equal(1L, world.Counters[CounterId.EnqueueBadState]);
        Assert.Equal(0L, world.Counters[CounterId.EnqueueDuringFlush]);
    }

    [Fact]
    public void Ac2_RequestStopInsideDeliver_IsCountedOutOfPhase_AndStillLatched()
    {
        // TD-032 item 4: ADR-0003 section 5 latches a stop from any phase other than 12 as if raised in 12, phase 13 included.
        var sink = new RecordingSink();
        SimWorld world = Frozen(sink);
        sink.OnDeliver = () => world.RunControl.RequestStop(StopReason.Unspecified);

        world.Tick();

        Assert.Equal(1L, world.Counters[CounterId.StopOutOfPhase]);
        Assert.Equal(SimState.Stopped, world.State);
        Assert.Equal(1u, world.CurrentTick);
    }

    // ---------------------------------------------------------------- AC-3 (through a world)

    [Fact]
    public void Ac3_EntriesRaisedInAPhaseRun_CarryThatRunsCallSeq_AndRunsGetIncreasingSeqs()
    {
        // Arrange: two binders in phase 3 and one in phase 8; each raises two entries.
        var sink = new RecordingSink();
        SimWorld? world = null;
        ISimPhase Make(uint tag) => new ActionPhase(_ =>
        {
            Raise(world!, TestKinds.ReplicatedKind, 0b1, tag);
            Raise(world!, TestKinds.ReplicatedKind, 0b1, tag + 1);
        });
        world = Frozen(sink, (PhaseId.PlayersStep, Make(100)), (PhaseId.PlayersStep, Make(200)), (PhaseId.MailStep, Make(300)));

        // Act
        world.Tick();

        // Assert: one fresh CallSeq per run, in run order, starting at 1.
        Assert.Equal(new uint[] { 1, 1, 2, 2, 3, 3 }, sink.Entries.Select(e => e.CallSeq).ToArray());
        Assert.Equal(new uint[] { 100, 101, 200, 201, 300, 301 }, sink.Entries.Select(e => e.PayloadAs<Pay12>().A).ToArray());
    }

    [Fact]
    public void Ac3_CallSeqRestartsAtOneEachTick()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(tick => Raise(world!, TestKinds.ReplicatedKind, 0b1, tick));
        world = Frozen(sink, (PhaseId.PlayersStep, phase), (PhaseId.MailStep, phase));

        world.Tick();
        world.Tick();

        Assert.Equal(new uint[] { 1, 2, 1, 2 }, sink.Entries.Select(e => e.CallSeq).ToArray());
    }

    [Fact]
    public void Ac3_ApiScopeInsideAPhaseRun_GetsAFreshSeq_AndThePhaseRunSeqIsRestoredAfterIt()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(_ =>
        {
            Raise(world!, TestKinds.ReplicatedKind, 0b1, 1);
            using (world!.Events.BeginCall())
            {
                Raise(world, TestKinds.ReplicatedKind, 0b1, 2);
            }

            Raise(world, TestKinds.ReplicatedKind, 0b1, 3);
        });
        world = Frozen(sink, (PhaseId.PlayersStep, phase));

        world.Tick();

        Assert.Equal(new uint[] { 1, 2, 1 }, sink.Entries.Select(e => e.CallSeq).ToArray());
    }

    [Fact]
    public void Ac3_LeakedScope_AssertsUnderSimAssert_OtherwiseTheDepthIsResetAndTheTickContinues()
    {
        // Arrange: a discarded `BeginCall();` never pops. Sim Core catches it at the end of the phase run.
        var sink = new RecordingSink();
        SimWorld? world = null;
        var leaker = new ActionPhase(tick => _ = world!.Events.BeginCall());
        var after = new ActionPhase(_ => Raise(world!, TestKinds.ReplicatedKind, 0b1, 1));
        world = Frozen(sink, (PhaseId.PlayersStep, leaker), (PhaseId.MailStep, after));

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(world.Tick);
            Assert.Equal(SimAssertCode.EventScopeLeak, ex.Code);
            Assert.Equal(SimState.Faulted, world.State);
            Assert.Equal(0, sink.Calls);
        }
        else
        {
            world.Tick();
            Assert.Equal(SimState.Running, world.State);
            Assert.Equal(1, sink.Calls);
            Assert.Single(sink.Entries);
            Assert.Equal(0, world.Events.ScopeDepth);
            Assert.Equal(1L, world.Counters[CounterId.ScopeLeak]);
        }

        Assert.Equal(0, world.Events.ScopeDepth);
    }
}

/// <summary>
/// Covers AC-3's allocation clause for a whole tick of story 015: a tick that opens phase-run scopes, raises events and
/// delivers them to a copy-only sink allocates 0 B. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Trait("Id", "SC-R12-2")]
[Collection(NonParallelCollection.Name)]
public class EventDeliveryAllocationTests
{
    /// <summary>A sink that copies into a preallocated buffer, as a real adapter must.</summary>
    private sealed class CopySink : IEventSink
    {
        private readonly EventEntry[] _buffer = new EventEntry[4096];

        public int Copied { get; private set; }

        public long Calls { get; private set; }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            entries.CopyTo(_buffer);
            Copied = entries.Length;
            Calls++;
        }
    }

    private sealed class RaisingPhase : ISimPhase
    {
        private readonly EventLog _log;

        public RaisingPhase(EventLog log)
        {
            _log = log;
        }

        public void Run(in TickContext ctx)
        {
            var p = new Pay12 { A = ctx.Tick };
            for (int i = 0; i < 8; i++)
            {
                _log.TryRaise(TestKinds.ReplicatedKind, 0b11, in p);
            }
        }
    }

    [Fact]
    public void Ac3_TickWithScopesRaisesAndDelivery_AllocatesNothing()
    {
        // Arrange: the phase needs the log, which exists only after Freeze, so bind through a forwarding phase.
        var sink = new CopySink();
        var forward = new ForwardingPhase();
        SimWorldBuilder builder = TestKinds.Builder();
        builder.Bind(PhaseId.PlayersStep, forward);
        builder.Bind(PhaseId.MailStep, forward);
        builder.BindEventSink(sink);
        SimWorld world = builder.Freeze();
        forward.Target = new RaisingPhase(world.Events);

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("200 ticks with raises and delivery", () =>
        {
            for (int i = 0; i < 200; i++)
            {
                world.Tick();
            }
        });

        // Assert: 2 phase runs x 8 raises per tick were delivered each tick, and the log is empty afterwards.
        Assert.Equal(16, sink.Copied);
        Assert.True(sink.Calls >= 200);
        Assert.Equal(0, world.Events.Count);
    }

    private sealed class ForwardingPhase : ISimPhase
    {
        public ISimPhase? Target { get; set; }

        public void Run(in TickContext ctx) => Target!.Run(in ctx);
    }
}
