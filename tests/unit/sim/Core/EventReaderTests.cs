using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers TR-sim-022 for story 016 (GDD SC-R12-1 mechanics, ADR-0008 section 4): pull readers declared at build with a sorted
/// <c>ushort[]</c> filter, read with <see cref="EventLog.Read"/>; cursors see only entries already in the log, never replay
/// within a tick, rewind each tick; kinds with a reader are flagged <c>HasReader</c> at <c>Freeze()</c>; at most 8 readers.
/// The <c>Read</c> allocation clause is in <see cref="EventReaderAllocationTests"/>.
/// </summary>
public class EventReaderTests
{
    private static readonly ReaderId _reader = new(1);

    // A and B are the filtered kinds; C is raised but not read.
    private const EventKind A = TestKinds.ReplicatedKind;
    private const EventKind B = TestKinds.OwnerOnlyKind;
    private const EventKind C = TestKinds.TargetedKind;

    private sealed class ActionPhase : ISimPhase
    {
        private readonly Action<uint> _action;

        public ActionPhase(Action<uint> action)
        {
            _action = action;
        }

        public void Run(in TickContext ctx) => _action(ctx.Tick);
    }

    private static void Raise(EventLog log, EventKind kind, uint tag)
    {
        var p = new Pay12 { A = tag };
        Assert.True(log.TryRaise(kind, 0b1, in p));
    }

    private static List<(EventKind Kind, uint Tag)> Drain(EventLog log, ref EventCursor cursor)
    {
        var seen = new List<(EventKind, uint)>();
        while (log.Read(ref cursor, out EventRef e))
        {
            seen.Add((e.Kind, e.PayloadAs<Pay12>().A));
        }

        return seen;
    }

    private static EventLog LogWithReader(params EventKind[] filter)
    {
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventReader(_reader, filter);
        return TestKinds.OpenLog(builder);
    }

    // ---------------------------------------------------------------- AC-3

    [Fact]
    public void Ac3_ReaderFilteringAAndB_AfterRaisesAcb_GetsAThenBThenFalse_AndASecondLoopGetsNothing()
    {
        // Arrange: the filter is declared unsorted on purpose (B before A).
        EventLog log = LogWithReader(B, A);
        var cursor = new EventCursor(_reader);
        Raise(log, A, 1);
        Raise(log, C, 2);
        Raise(log, B, 3);

        // Act
        List<(EventKind Kind, uint Tag)> first = Drain(log, ref cursor);
        List<(EventKind Kind, uint Tag)> second = Drain(log, ref cursor);

        // Assert
        Assert.Equal(new[] { (A, 1u), (B, 3u) }, first);
        Assert.Empty(second);
        Assert.Equal(3, cursor.Position);
    }

    [Fact]
    public void Ac3_EntryTheReaderRaisesItself_IsAppendedAfterEverythingAlreadyInTheLog_AndSeenByALaterRead()
    {
        EventLog log = LogWithReader(A, B);
        var cursor = new EventCursor(_reader);
        Raise(log, A, 1);
        Raise(log, C, 2);
        Raise(log, B, 3);
        Assert.Equal(2, Drain(log, ref cursor).Count);

        Raise(log, A, 4); // the reader's own raise

        Assert.Equal(3u, log.Span[3].Ordinal);
        Assert.Equal(4, log.Count);
        Assert.Equal(new[] { (A, 4u) }, Drain(log, ref cursor));
    }

    [Fact]
    public void Ac3_ReaderSeesOnlyEntriesAlreadyInTheLog_NotLaterOnes()
    {
        EventLog log = LogWithReader(A);
        var cursor = new EventCursor(_reader);
        Raise(log, A, 1);

        Assert.Equal(new[] { (A, 1u) }, Drain(log, ref cursor));
        Assert.False(log.Read(ref cursor, out _));

        Raise(log, A, 2); // raised after the reader finished its loop

        Assert.Equal(new[] { (A, 2u) }, Drain(log, ref cursor));
    }

    [Fact]
    public void Ac3_ThroughAWorld_PhaseTenRaises_PhaseElevenReads_AndTheCursorRestartsAtZeroNextTick()
    {
        // Arrange: phase 10 raises A, C, B; phase 11 reads with a cursor the reading system keeps across ticks.
        SimWorld? world = null;
        var cursor = new EventCursor(_reader);
        var perTick = new List<List<(EventKind Kind, uint Tag)>>();
        var secondLoops = new List<int>();
        var resolve = new ActionPhase(tick =>
        {
            Raise(world!.Events, A, (tick * 10) + 1);
            Raise(world.Events, C, (tick * 10) + 2);
            Raise(world.Events, B, (tick * 10) + 3);
        });
        var react = new ActionPhase(_ =>
        {
            perTick.Add(Drain(world!.Events, ref cursor));
            secondLoops.Add(Drain(world.Events, ref cursor).Count);
        });
        SimWorldBuilder builder = TestKinds.Builder();
        builder.Bind(PhaseId.CombatResolve, resolve);
        builder.Bind(PhaseId.CombatReact, react);
        builder.BindEventReader(_reader, new[] { A, B });
        world = builder.Freeze();

        // Act
        world.Tick();
        world.Tick();

        // Assert: both ticks see their own A then B from position 0, whatever the stale handle mirrored.
        Assert.Equal(new[] { (A, 1u), (B, 3u) }, perTick[0]);
        Assert.Equal(new[] { (A, 11u), (B, 13u) }, perTick[1]);
        Assert.Equal(new[] { 0, 0 }, secondLoops);
    }

    [Fact]
    public void Ac3_CopyOfTheHandle_CannotReplayEntriesTheReaderAlreadyConsumed()
    {
        EventLog log = LogWithReader(A);
        var cursor = new EventCursor(_reader);
        EventCursor stale = cursor; // position 0
        Raise(log, A, 1);
        Assert.Single(Drain(log, ref cursor));

        Assert.Empty(Drain(log, ref stale));
    }

    [Fact]
    public void Ac3_TwoReaders_KeepIndependentPositions()
    {
        var other = new ReaderId(2);
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventReader(_reader, new[] { A });
        builder.BindEventReader(other, new[] { A, C });
        EventLog log = TestKinds.OpenLog(builder);
        var first = new EventCursor(_reader);
        var second = new EventCursor(other);
        Raise(log, A, 1);
        Raise(log, C, 2);

        Assert.Equal(new[] { (A, 1u) }, Drain(log, ref first));
        Assert.Equal(new[] { (A, 1u), (C, 2u) }, Drain(log, ref second));
        Assert.Empty(Drain(log, ref first));
    }

    [Fact]
    public void Ac3_EventRef_ExposesTheEntryFieldsReadOnly()
    {
        EventLog log = LogWithReader(B);
        var cursor = new EventCursor(_reader);
        using (log.BeginCall())
        {
            var p = new Pay12 { A = 7, B = 8, C = 9 };
            Assert.True(log.TryRaise(B, 0b100, in p));
        }

        Assert.True(log.Read(ref cursor, out EventRef e));
        Assert.Equal(0u, e.Ordinal);
        Assert.Equal(B, e.Kind);
        Assert.Equal(ReplicationClass.OwnerOnly, e.Class);
        Assert.Equal((byte)0b100, e.Audience);
        Assert.Equal(1u, e.CallSeq);
        Assert.Equal((byte)12, e.PayloadLength);
        Assert.Equal(EventFlags.None, e.Flags);
        Assert.Equal(new Pay12 { A = 7, B = 8, C = 9 }, e.PayloadAs<Pay12>());
        Assert.Equal(e.Ordinal, e.Entry.Ordinal);
    }

    [Fact]
    public void Ac3_HasReader_IsTrueForTheFilteredKindsAfterFreeze_AndFalseForTheOthers()
    {
        EventLog log = LogWithReader(A, B);

        Assert.True(log.HasReader(A));
        Assert.True(log.HasReader(B));
        Assert.False(log.HasReader(C));
        Assert.False(log.HasReader(EventKind.OpReply));
        Assert.False(log.HasReader(EventKind.None));
        Assert.False(log.HasReader((EventKind)0xFFFF)); // beyond every table
        Assert.Equal(1, log.ReaderCount);
    }

    [Fact]
    public void Ac3_HasReader_IsFalseForEveryKind_WhenNoReaderIsBound()
    {
        EventLog log = TestKinds.OpenLog(TestKinds.Builder());

        Assert.False(log.HasReader(A));
        Assert.False(log.HasReader(EventKind.OpReply));
        Assert.Equal(0, log.ReaderCount);
    }

    [Fact]
    public void Ac3_HasReader_CoversAReaderKindBeyondTheDeclaredDescriptorTable()
    {
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventReader(_reader, new[] { (EventKind)0x0200 });
        EventLog log = TestKinds.OpenLog(builder);

        Assert.True(log.HasReader((EventKind)0x0200));
        Assert.False(log.HasReader((EventKind)0x01FF));
    }

    [Fact]
    public void Ac3_ReadWithAnUnboundReaderId_AssertsUnderSimAssert_OtherwiseReturnsFalse()
    {
        EventLog log = LogWithReader(A);
        Raise(log, A, 1);
        var cursor = new EventCursor(new ReaderId(9));

        if (TestBuildExpectation.SimAssertsExpected)
        {
            EventCursor c = cursor;
            SimAssertException ex = Assert.Throws<SimAssertException>(() => log.Read(ref c, out _));
            Assert.Equal(SimAssertCode.EventReaderUnknown, ex.Code);
        }
        else
        {
            Assert.False(log.Read(ref cursor, out _));
        }

        // A default cursor (ReaderId.None) is never a bound reader either.
        var none = default(EventCursor);
        Assert.Equal(ReaderId.None, none.ReaderId);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.False(log.Read(ref none, out _));
        }
    }

    [Fact]
    public void Ac3_ResetRewindsEveryReader()
    {
        EventLog log = LogWithReader(A);
        var cursor = new EventCursor(_reader);
        Raise(log, A, 1);
        Assert.Single(Drain(log, ref cursor));

        log.Reset();
        Raise(log, A, 2);

        Assert.Equal(new[] { (A, 2u) }, Drain(log, ref cursor));
    }

    // ---------------------------------------------------------------- builder: declaration and limit

    [Fact]
    public void Build_TheFilterIsCopied_AndSortedAscendingIntoTheWorld()
    {
        var filter = new[] { C, A, B };
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventReader(_reader, filter);
        filter[0] = EventKind.OpReply; // mutating the caller's array afterwards must change nothing
        EventLog log = TestKinds.OpenLog(builder);
        var cursor = new EventCursor(_reader);
        Raise(log, A, 1);
        Raise(log, B, 2);
        Raise(log, C, 3);

        Assert.Equal(new[] { (A, 1u), (B, 2u), (C, 3u) }, Drain(log, ref cursor));
        Assert.False(log.HasReader(EventKind.OpReply));
    }

    [Fact]
    public void Build_EightReadersBind_TheNinthThrowsReaderLimit()
    {
        SimWorldBuilder builder = TestKinds.Builder();
        for (byte i = 1; i <= 8; i++)
        {
            builder.BindEventReader(new ReaderId(i), new[] { A });
        }

        SimBuildException ex = Assert.Throws<SimBuildException>(() => builder.BindEventReader(new ReaderId(9), new[] { A }));

        Assert.Equal(SimBuildErrorCode.ReaderLimit, ex.Code);
        Assert.Equal(8, builder.Freeze().Events.ReaderCount);
    }

    [Fact]
    public void Build_TheLimitFollowsMaxEventReaders()
    {
        SimWorldBuilder builder = TestKinds.Builder(new SimCoreSettings { MaxEventReaders = 1 });
        builder.BindEventReader(_reader, new[] { A });

        Assert.Equal(SimBuildErrorCode.ReaderLimit, Assert.Throws<SimBuildException>(() => builder.BindEventReader(new ReaderId(2), new[] { A })).Code);
    }

    [Fact]
    public void Build_InvalidDeclarations_ThrowInvalidEventReader_AndNullThrowsArgumentNull()
    {
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventReader(_reader, new[] { A });

        Assert.Equal(SimBuildErrorCode.InvalidEventReader, Assert.Throws<SimBuildException>(() => builder.BindEventReader(ReaderId.None, new[] { A })).Code);
        Assert.Equal(SimBuildErrorCode.InvalidEventReader, Assert.Throws<SimBuildException>(() => builder.BindEventReader(_reader, new[] { B })).Code);
        Assert.Equal(SimBuildErrorCode.InvalidEventReader, Assert.Throws<SimBuildException>(() => builder.BindEventReader(new ReaderId(2), Array.Empty<EventKind>())).Code);
        Assert.Equal(SimBuildErrorCode.InvalidEventReader, Assert.Throws<SimBuildException>(() => builder.BindEventReader(new ReaderId(2), new[] { A, EventKind.None })).Code);
        Assert.Throws<ArgumentNullException>(() => builder.BindEventReader(new ReaderId(2), null!));
    }
}

/// <summary>
/// Covers the allocation clause of story 016 AC-3: <see cref="EventLog.Read"/> allocates 0 B. Joins the non-parallel
/// collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class EventReaderAllocationTests
{
    [Fact]
    public void Ac3_Read_AllocatesNothing()
    {
        // Arrange
        var reader = new ReaderId(1);
        SimWorldBuilder builder = TestKinds.Builder();
        builder.BindEventReader(reader, new[] { TestKinds.OwnerOnlyKind, TestKinds.ReplicatedKind });
        EventLog log = TestKinds.OpenLog(builder);
        var p = new Pay12 { A = 1 };
        for (int i = 0; i < 30; i++)
        {
            log.TryRaise(i % 3 == 0 ? TestKinds.TargetedKind : TestKinds.ReplicatedKind, 0b1, in p);
        }

        var cursor = new EventCursor(reader);
        long matched = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("Read over a 30-entry log, 200 loops", () =>
        {
            matched = 0;
            for (int loop = 0; loop < 200; loop++)
            {
                log.Reset();
                for (int i = 0; i < 30; i++)
                {
                    log.TryRaise(i % 3 == 0 ? TestKinds.TargetedKind : TestKinds.ReplicatedKind, 0b1, in p);
                }

                while (log.Read(ref cursor, out EventRef e))
                {
                    matched += e.PayloadAs<Pay12>().A;
                }
            }
        });

        // Assert: 20 of every 30 entries match the filter.
        Assert.Equal(200L * 20, matched);
    }
}
