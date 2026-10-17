using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-024 for story 016 (GDD SC-R14-1, single tier; ADR-0008 section 6): a log that cannot admit an entry drops it
/// (never reordering, never rolling back) and reports exactly what was lost in <see cref="OverflowInfo"/>; raises outside the
/// legal window (between ticks, at tick end, during <c>Deliver</c>) assert under SIM_ASSERT and are dropped and counted
/// otherwise. Behaviour that differs between SIM_ASSERT and other builds branches on
/// <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
public class EventOverflowTests
{
    private const byte Slot0 = 0b001;
    private const byte Slot1 = 0b010;
    private const byte Slot2 = 0b100;
    private const byte Connected = 0b111;

    private sealed class ActionPhase : ISimPhase
    {
        private readonly Action<uint> _action;

        public ActionPhase(Action<uint> action)
        {
            _action = action;
        }

        public void Run(in TickContext ctx) => _action(ctx.Tick);
    }

    private sealed class TickEndRaiser : ITickEndCheck
    {
        public SimWorld? World { get; set; }

        public bool? Raised { get; private set; }

        public void Check(uint tick)
        {
            var p = new Pay12 { A = 77 };
            Raised = World!.Events.TryRaise(TestKinds.OwnerOnlyKind, Slot2, in p);
        }
    }

    /// <summary>A sink that raises inside Deliver and then re-reads the live <see cref="OverflowInfo"/> it was given.</summary>
    private sealed class RaisingSink : IEventSink
    {
        public SimWorld? World { get; set; }

        public bool? Raised { get; private set; }

        public ushort? AssertCode { get; private set; }

        public OverflowInfo Before { get; private set; }

        public OverflowInfo After { get; private set; }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            Before = overflow;
            var p = new Pay12 { A = 5 };
            try
            {
                Raised = World!.Events.TryRaise(TestKinds.TargetedKind, Slot1, in p);
            }
            catch (SimAssertException ex)
            {
                AssertCode = ex.Code;
            }

            After = overflow;
        }
    }

    private static SimWorld Small(RecordingSink sink, int capacity, params (PhaseId Phase, ISimPhase Binder)[] binds)
    {
        SimWorldBuilder builder = TestKinds.Builder(new SimCoreSettings { EventLogCapacity = capacity });
        foreach ((PhaseId phase, ISimPhase binder) in binds)
        {
            builder.Bind(phase, binder);
        }

        builder.BindEventSink(sink);
        SimWorld world = builder.Freeze();
        sink.Reader = world;
        return world;
    }

    private static bool Raise(SimWorld world, EventKind kind, byte audience, uint tag)
    {
        var p = new Pay12 { A = tag };
        return world.Events.TryRaise(kind, audience, in p);
    }

    // ---------------------------------------------------------------- AC-1

    [Fact]
    [Trait("Id", "SC-R14-1")]
    public void Ac1_LogOverflows_LaterEntriesDropped_NothingReorderedOrRolledBack_AndOverflowInfoNamesExactlyTheLoss()
    {
        // Arrange: capacity 16. Slots 0 and 1 fill the log; slot 2's only entries come after the limit.
        var sink = new RecordingSink();
        SimWorld? world = null;
        int committed = 0;
        var raiser = new ActionPhase(_ =>
        {
            for (uint i = 0; i < 16; i++)
            {
                committed++;
                Assert.True(Raise(world!, TestKinds.ReplicatedKind, (byte)(Slot0 | Slot1), i));
            }

            // After the limit: slot 2's OwnerOnly (scope 3), a Replicated to every connected slot (scope 2), slot 0's Targeted (scope 4).
            committed++;
            Assert.False(Raise(world!, TestKinds.OwnerOnlyKind, Slot2, 100));
            committed++;
            Assert.False(Raise(world!, TestKinds.ReplicatedKind, Connected, 101));
            committed++;
            Assert.False(Raise(world!, TestKinds.TargetedKind, Slot0, 102));
        });
        world = Small(sink, 16, (PhaseId.PlayersStep, raiser));

        // Act
        world.Tick();

        // Assert: the state changes made by the raisers stand (no rollback).
        Assert.Equal(19, committed);

        // Assert: the 16 admitted entries are delivered in raise order, untouched.
        Assert.Equal(16, sink.Entries.Count);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (uint)i).ToArray(), sink.Entries.Select(e => e.Ordinal).ToArray());
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (uint)i).ToArray(), sink.Entries.Select(e => e.PayloadAs<Pay12>().A).ToArray());

        // Assert: OverflowInfo is exactly the OR of the three dropped entries.
        OverflowInfo info = sink.Overflows[0];
        Assert.True(info.Overflow);
        Assert.Equal(3u, info.DroppedCount);
        Assert.Equal(Connected, info.DroppedAudienceMask); // slot 2's bit, the connected mask, slot 0's bit
        Assert.Equal((1UL << 3) | (1UL << 2) | (1UL << 4), info.DroppedKindsMask);
        Assert.Equal(3L, world.Counters[CounterId.EventsDropped]);
    }

    [Fact]
    public void Ac1_OnlySlot2Dropped_MaskIsSlot2Alone_AndKindsMaskIsThatKindsScope()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var raiser = new ActionPhase(_ =>
        {
            Assert.True(Raise(world!, TestKinds.ReplicatedKind, Slot0, 1));
            Assert.True(Raise(world!, TestKinds.ReplicatedKind, Slot1, 2));
            Assert.False(Raise(world!, TestKinds.OwnerOnlyKind, Slot2, 3));
        });
        world = Small(sink, 2, (PhaseId.PlayersStep, raiser));

        world.Tick();

        Assert.Equal(2, sink.Entries.Count);
        Assert.True(sink.Overflows[0].Overflow);
        Assert.Equal(Slot2, sink.Overflows[0].DroppedAudienceMask);
        Assert.Equal(1UL << 3, sink.Overflows[0].DroppedKindsMask);
        Assert.Equal(1u, sink.Overflows[0].DroppedCount);
    }

    [Fact]
    public void Ac1_DroppedInternalEntry_ContributesNoAudience_ButItsScope()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var raiser = new ActionPhase(_ =>
        {
            Assert.True(Raise(world!, TestKinds.ReplicatedKind, Slot0, 1));
            var p4 = new Pay4 { A = 9 };
            Assert.False(world!.Events.TryRaise(TestKinds.InternalKind, Connected, in p4));
        });
        world = Small(sink, 1, (PhaseId.PlayersStep, raiser));

        world.Tick();

        Assert.True(sink.Overflows[0].Overflow);
        Assert.Equal((byte)0, sink.Overflows[0].DroppedAudienceMask);
        Assert.Equal(1UL << 1, sink.Overflows[0].DroppedKindsMask);
    }

    [Fact]
    public void Ac1_QuietTickAfterAnOverflow_ReportsDefaultOverflowInfo_AndTheAccumulatorIsClearAfterDeliver()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var raiser = new ActionPhase(tick =>
        {
            if (tick == 0)
            {
                Raise(world!, TestKinds.ReplicatedKind, Slot0, 1);
                Raise(world!, TestKinds.ReplicatedKind, Slot1, 2);
            }
        });
        world = Small(sink, 1, (PhaseId.PlayersStep, raiser));

        world.Tick();
        Assert.False(world.Events.Overflow.Overflow);
        Assert.Equal(0u, world.Events.Overflow.DroppedCount);
        world.Tick();

        Assert.True(sink.Overflows[0].Overflow);
        Assert.False(sink.Overflows[1].Overflow);
        Assert.Equal(0u, sink.Overflows[1].DroppedCount);
        Assert.Equal((byte)0, sink.Overflows[1].DroppedAudienceMask);
        Assert.Equal(0UL, sink.Overflows[1].DroppedKindsMask);
    }

    [Fact]
    public void Ac1_DroppedUndeclaredKind_CountsAsScopeZero()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var raiser = new ActionPhase(_ =>
        {
            Raise(world!, TestKinds.ReplicatedKind, Slot0, 1);
            var p4 = new Pay4();
            Assert.False(world!.Events.TryRaise(TestKinds.Undeclared, Slot1, in p4));
        });
        world = Small(sink, 1, (PhaseId.PlayersStep, raiser));

        world.Tick();

        Assert.Equal(1UL, sink.Overflows[0].DroppedKindsMask);
        Assert.Equal((byte)0, sink.Overflows[0].DroppedAudienceMask); // undeclared is stored as Internal: no audience
    }

    // ---------------------------------------------------------------- AC-2 misuse

    [Fact]
    public void Ac2_RaiseBetweenTicks_AssertsUnderSimAssert_OtherwiseDroppedAndCountedWithoutOverflow()
    {
        var sink = new RecordingSink();
        SimWorld world = Small(sink, 16);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => Raise(world, TestKinds.ReplicatedKind, Slot0, 1));
            Assert.Equal(SimAssertCode.EventRaiseOutsideTick, ex.Code);
        }
        else
        {
            Assert.False(Raise(world, TestKinds.ReplicatedKind, Slot0, 1));
            Assert.Equal(1L, world.Counters[CounterId.EventsOutsideTick]);
            Assert.Equal(1L, world.Counters[CounterId.EventsDropped]);
        }

        // No delivery is open between ticks, so nothing may leak into the next tick's OverflowInfo.
        Assert.Equal(0, world.Events.Count);
        Assert.False(world.Events.Overflow.Overflow);
        world.Tick();
        Assert.False(sink.Overflows[0].Overflow);
        Assert.Equal(0L, world.Counters[CounterId.EventsAtTickEnd]);
        Assert.Equal(0L, world.Counters[CounterId.EventsDuringFlush]);
    }

    [Fact]
    public void Ac2_RaiseFromATickEndCheck_AssertsUnderSimAssert_OtherwiseDroppedCountedAndFlagsOverflow()
    {
        // Arrange
        var sink = new RecordingSink();
        var check = new TickEndRaiser();
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindTickEnd(check);
        builder.BindEventSink(sink);
        SimWorld world = builder.Freeze();
        check.World = world;

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(world.Tick);
            Assert.Equal(SimAssertCode.EventRaiseAtTickEnd, ex.Code);
            Assert.Equal(SimState.Faulted, world.State);
            Assert.Equal(0, sink.Calls);
        }
        else
        {
            world.Tick();
            Assert.False(check.Raised);
            Assert.Equal(1L, world.Counters[CounterId.EventsAtTickEnd]);
            Assert.Equal(1L, world.Counters[CounterId.EventsDropped]);
            Assert.Empty(sink.Entries);
            Assert.True(sink.Overflows[0].Overflow);
            Assert.Equal(Slot2, sink.Overflows[0].DroppedAudienceMask);
            Assert.Equal(1UL << 3, sink.Overflows[0].DroppedKindsMask);
            Assert.Equal(1u, sink.Overflows[0].DroppedCount);
        }
    }

    [Fact]
    public void Ac2_RaiseFromInsideDeliver_AssertsUnderSimAssert_OtherwiseDroppedCountedAndFlagsOverflow()
    {
        // Arrange
        var sink = new RaisingSink();
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventSink(sink);
        SimWorld world = builder.Freeze();
        sink.World = world;

        // Act
        world.Tick();

        // Assert
        Assert.False(sink.Before.Overflow);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.EventRaiseDuringFlush, sink.AssertCode);
            Assert.Null(sink.Raised);
            Assert.False(sink.After.Overflow);
        }
        else
        {
            Assert.False(sink.Raised);
            Assert.Equal(1L, world.Counters[CounterId.EventsDuringFlush]);
            Assert.Equal(1L, world.Counters[CounterId.EventsDropped]);

            // The sink holds the log's live value by `in`, so the same delivery sees the flag the misuse set.
            Assert.True(sink.After.Overflow);
            Assert.Equal(Slot1, sink.After.DroppedAudienceMask);
            Assert.Equal(1UL << 4, sink.After.DroppedKindsMask);
            Assert.Equal(1u, sink.After.DroppedCount);
        }

        // The log resets after Deliver: the next tick starts clean.
        Assert.False(world.Events.Overflow.Overflow);
        Assert.Equal(0, world.Events.Count);
    }

    [Fact]
    public void Ac2_MisuseDoesNotChangeAdmission_ARaiseInThePhaseAfterwardsStillWorks()
    {
        var sink = new RecordingSink();
        SimWorld? world = null;
        var phase = new ActionPhase(tick => Assert.True(Raise(world!, TestKinds.ReplicatedKind, Slot0, tick)));
        world = Small(sink, 16, (PhaseId.PlayersStep, phase));

        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.False(Raise(world, TestKinds.ReplicatedKind, Slot0, 99)); // between ticks: dropped
        }

        world.Tick();
        world.Tick();

        Assert.Equal(new uint[] { 0, 1 }, sink.Entries.Select(e => e.PayloadAs<Pay12>().A).ToArray());
        Assert.False(sink.Overflows[0].Overflow);
    }

    // ---------------------------------------------------------------- carry-over: scopeLeak

    [Fact]
    public void Ac2_ScopeLeftOpenInATickEndCheck_AssertsUnderSimAssert_OtherwiseCountsScopeLeak()
    {
        // Arrange
        var sink = new RecordingSink();
        SimWorld? world = null;
        var leaker = new LeakingCheck();
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindTickEnd(leaker);
        builder.BindEventSink(sink);
        world = builder.Freeze();
        leaker.World = world;

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.EventScopeLeak, Assert.Throws<SimAssertException>(world.Tick).Code);
        }
        else
        {
            world.Tick();
            Assert.Equal(1L, world.Counters[CounterId.ScopeLeak]);
            Assert.Equal(0, world.Events.ScopeDepth);
            Assert.Equal(1, sink.Calls);
        }
    }

    private sealed class LeakingCheck : ITickEndCheck
    {
        public SimWorld? World { get; set; }

        public void Check(uint tick) => _ = World!.Events.BeginCall();
    }

    // ---------------------------------------------------------------- append-only ids

    [Fact]
    public void NewIds_AreAppendOnly_AndPinned()
    {
        Assert.Equal(24, (int)CounterId.ScopeLeak);
        Assert.Equal(35, SimAssertCode.EventPayloadTypeMismatch);
        Assert.Equal(36, SimAssertCode.EventRaiseOutsideTick);
        Assert.Equal(37, SimAssertCode.EventRaiseAtTickEnd);
        Assert.Equal(38, SimAssertCode.EventRaiseDuringFlush);
        Assert.Equal(39, SimAssertCode.EventReaderUnknown);
        Assert.Equal(6, (int)SimBuildErrorCode.ReaderLimit);
        Assert.Equal(7, (int)SimBuildErrorCode.InvalidEventReader);
    }
}
