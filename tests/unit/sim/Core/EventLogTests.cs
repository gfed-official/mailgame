using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>A 12-byte padding-free test payload.</summary>
internal struct Pay12
{
    public uint A;
    public uint B;
    public uint C;
}

/// <summary>A 4-byte padding-free test payload.</summary>
internal struct Pay4
{
    public uint A;
}

/// <summary>A second padding-free 12-byte payload: same size as <see cref="Pay12"/>, a different type.</summary>
internal struct Pay12Alt
{
    public uint A;
    public uint B;
    public uint C;
}

/// <summary>A payload with alignment padding (1 B then 4 B = 8 B, fields sum to 5 B): must be rejected by the descriptor check.</summary>
#pragma warning disable CS0649 // Never assigned: only its layout matters.
internal struct PayPadded
{
    public byte A;
    public uint B;
}
#pragma warning restore CS0649

/// <summary>A full 48-byte payload.</summary>
[InlineArray(EventPayload.Size)]
internal struct Pay48
{
    private byte _e0;
}

/// <summary>A payload one byte over the ceiling.</summary>
[InlineArray(EventPayload.Size + 1)]
internal struct Pay49
{
    private byte _e0;
}

/// <summary>Test-only event kinds in the Players block (0x0040 to 0x007F), plus one that is never declared.</summary>
internal static class TestKinds
{
    public const EventKind InternalKind = (EventKind)0x0040;
    public const EventKind ReplicatedKind = (EventKind)0x0041;
    public const EventKind OwnerOnlyKind = (EventKind)0x0042;
    public const EventKind TargetedKind = (EventKind)0x0043;
    public const EventKind Wide = (EventKind)0x0044;
    public const EventKind Undeclared = (EventKind)0x0045;

    /// <summary>Declared by hand with a 12-byte length and no <c>PayloadType</c>, so any payload up to 12 bytes is accepted.</summary>
    public const EventKind Loose12 = (EventKind)0x0046;

    /// <summary>Declared by hand with a 48-byte length and no <c>PayloadType</c>.</summary>
    public const EventKind Loose48 = (EventKind)0x0047;

    /// <summary>
    /// Freezes <paramref name="builder"/> and returns its log with the stage set to <see cref="TickStage.InPhase"/>, so tests can
    /// raise without ticking a world (a raise between ticks is misuse since Story 016).
    /// </summary>
    public static EventLog OpenLog(SimWorldBuilder builder)
    {
        EventLog log = builder.Freeze().Events;
        log.Stage = TickStage.InPhase;
        return log;
    }

    /// <summary>Returns a builder with the test kinds declared (everything but <see cref="Undeclared"/>).</summary>
    public static SimWorldBuilder Builder(SimCoreSettings? settings = null)
    {
        var builder = new SimWorldBuilder(settings);
        builder.BindEventKind(EventDescriptor.For<Pay4>(InternalKind, ReplicationClass.Internal, false, 1, 0, 4));
        builder.BindEventKind(EventDescriptor.For<Pay12>(ReplicatedKind, ReplicationClass.Replicated, false, 2, 0, 12));
        builder.BindEventKind(EventDescriptor.For<Pay12>(OwnerOnlyKind, ReplicationClass.OwnerOnly, false, 3, 0, 12));
        builder.BindEventKind(EventDescriptor.For<Pay12>(TargetedKind, ReplicationClass.Targeted, false, 4, 0, 12));
        builder.BindEventKind(EventDescriptor.For<Pay48>(Wide, ReplicationClass.Replicated, false, 5, 0, 48));
        builder.BindEventKind(new EventDescriptor(Loose12, ReplicationClass.Replicated, 12, false, 6, 0, 12));
        builder.BindEventKind(new EventDescriptor(Loose48, ReplicationClass.Replicated, 48, false, 7, 0, 48));
        return builder;
    }
}

/// <summary>
/// Covers TR-sim-021 and TR-sim-023 for story 015 (GDD SC-R12-2; AC-2 layout, descriptors and lifetime; AC-3 call scopes):
/// the 64-byte <see cref="EventEntry"/> layout, <see cref="EventLog.TryRaise{T}"/> stamping and zero fill, replication class
/// and audience rules, undeclared kinds, the descriptor enumeration helper, builder binding errors and call scopes. The
/// delivery half (AC-1, AC-2 lifetime through a world) is in <see cref="EventDeliveryTests"/>. Behaviour that differs between
/// SIM_ASSERT and other builds branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
[Trait("Id", "SC-R12-2")]
public class EventLogTests
{
    /// <summary>Freezes a world and returns its log opened for raising without ticking (the log is legal only in a phase run).</summary>
    private static EventLog LogOf(SimWorldBuilder builder) => TestKinds.OpenLog(builder);

    // ---------------------------------------------------------------- AC-2 layout

    [Fact]
    public void Ac2_EventEntry_IsExactly64Bytes()
    {
        Assert.Equal(64, Unsafe.SizeOf<EventEntry>());
        Assert.Equal(EventEntry.SizeBytes, Unsafe.SizeOf<EventEntry>());
        Assert.Equal(48, Unsafe.SizeOf<EventPayload>());
    }

    [Fact]
    public void Ac2_EventEntry_FieldOffsetsMatchAdr0008Section1()
    {
        EventEntry e = default;
        Assert.Equal(0, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<uint, byte>(ref e.Ordinal)));
        Assert.Equal(4, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<EventKind, byte>(ref e.Kind)));
        Assert.Equal(6, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<ReplicationClass, byte>(ref e.Class)));
        Assert.Equal(7, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref e.Audience));
        Assert.Equal(8, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<uint, byte>(ref e.CallSeq)));
        Assert.Equal(12, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref e.PayloadLength));
        Assert.Equal(13, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<EventFlags, byte>(ref e.Flags)));
        Assert.Equal(14, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<ushort, byte>(ref e.Reserved)));
        Assert.Equal(16, (int)Unsafe.ByteOffset(ref Unsafe.As<EventEntry, byte>(ref e), ref Unsafe.As<EventPayload, byte>(ref e.Payload)));
    }

    [Fact]
    public void Ac2_EnumWidths_AreU16AndU8()
    {
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(EventKind)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ReplicationClass)));
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(EventFlags)));
        Assert.Equal(1, (byte)EventFlags.Continuation);
        Assert.Equal(0, (byte)ReplicationClass.Internal);
        Assert.Equal(1, (byte)ReplicationClass.Replicated);
        Assert.Equal(2, (byte)ReplicationClass.OwnerOnly);
        Assert.Equal(3, (byte)ReplicationClass.Targeted);
    }

    [Fact]
    public void Ac2_Raise12BytePayload_StoresBytesAndZeroFillsBytes12To47()
    {
        // Arrange: dirty slot 0 with a full 48-byte payload of 0xAB, empty the log, then raise 12 bytes into the same slot.
        EventLog log = LogOf(TestKinds.Builder());
        Pay48 dirty = default;
        Span<byte> dirtyBytes = dirty;
        dirtyBytes.Fill(0xAB);
        Assert.True(log.TryRaise(TestKinds.Wide, 0b1, in dirty));
        log.Reset();
        var payload = new Pay12 { A = 0x1111_1111u, B = 0x2222_2222u, C = 0x3333_3333u };

        // Act
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in payload));

        // Assert
        EventEntry e = log.Span[0];
        Assert.Equal((byte)12, e.PayloadLength);
        Assert.Equal(payload, e.PayloadAs<Pay12>());
        ReadOnlySpan<byte> bytes = e.Payload;
        for (int i = 12; i < EventPayload.Size; i++)
        {
            Assert.Equal((byte)0, bytes[i]);
        }

        Assert.Equal((ushort)0, e.Reserved);
        Assert.Equal(EventFlags.None, e.Flags);
    }

    [Fact]
    public void Ac2_Raise_StampsOrdinalKindCallSeqAndLengthPerEntry()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12 { A = 1 };
        using (log.BeginCall())
        {
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b11, in p));
            Assert.True(log.TryRaise(TestKinds.OwnerOnlyKind, 0b10, in p));
        }

        Assert.Equal(2, log.Count);
        ReadOnlySpan<EventEntry> span = log.Span;
        Assert.Equal(0u, span[0].Ordinal);
        Assert.Equal(1u, span[1].Ordinal);
        Assert.Equal(TestKinds.ReplicatedKind, span[0].Kind);
        Assert.Equal(TestKinds.OwnerOnlyKind, span[1].Kind);
        Assert.Equal(1u, span[0].CallSeq);
        Assert.Equal(1u, span[1].CallSeq);
    }

    // ---------------------------------------------------------------- AC-2 classes, audience, descriptors

    [Fact]
    public void Ac2_UndeclaredKind_IsStoredAsInternalWithAudienceZero()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay4 { A = 7 };

        Assert.True(log.TryRaise(TestKinds.Undeclared, 0b1111_1111, in p));

        EventEntry e = log.Span[0];
        Assert.Equal(ReplicationClass.Internal, e.Class);
        Assert.Equal((byte)0, e.Audience);
        Assert.Equal(TestKinds.Undeclared, e.Kind);
        Assert.Equal(p, e.PayloadAs<Pay4>());
    }

    [Fact]
    public void Ac2_KindBeyondTheLookupTable_IsStoredAsInternal()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay4 { A = 7 };

        Assert.True(log.TryRaise((EventKind)0xFFFF, 0b1, in p));

        Assert.Equal(ReplicationClass.Internal, log.Span[0].Class);
        Assert.Null(log.DescriptorFor((EventKind)0xFFFF));
    }

    [Fact]
    public void Ac2_EachClass_TakesItsDescriptorsClass_AndInternalAudienceIsZero()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p4 = new Pay4 { A = 1 };
        var p12 = new Pay12 { A = 1 };

        Assert.True(log.TryRaise(TestKinds.InternalKind, 0b0110, in p4));
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b0110, in p12));
        Assert.True(log.TryRaise(TestKinds.OwnerOnlyKind, 0b0100, in p12));
        Assert.True(log.TryRaise(TestKinds.TargetedKind, 0b1001, in p12));

        ReadOnlySpan<EventEntry> s = log.Span;
        Assert.Equal(ReplicationClass.Internal, s[0].Class);
        Assert.Equal((byte)0, s[0].Audience);
        Assert.Equal(ReplicationClass.Replicated, s[1].Class);
        Assert.Equal((byte)0b0110, s[1].Audience);
        Assert.Equal(ReplicationClass.OwnerOnly, s[2].Class);
        Assert.Equal((byte)0b0100, s[2].Audience);
        Assert.Equal(ReplicationClass.Targeted, s[3].Class);
        Assert.Equal((byte)0b1001, s[3].Audience);
    }

    [Fact]
    public void Ac2_OwnerOnly_ZeroAudience_IsLegalAndStoredAsZero()
    {
        // ADR-0008 section 3: 0 bits delivers to nobody (a reply to System).
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12();

        Assert.True(log.TryRaise(TestKinds.OwnerOnlyKind, 0, in p));

        Assert.Equal((byte)0, log.Span[0].Audience);
    }

    [Fact]
    public void Ac2_OwnerOnly_MoreThanOneBit_AssertsOrKeepsTheLowestBit()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => log.TryRaise(TestKinds.OwnerOnlyKind, 0b0110_1000, in p));
            Assert.Equal(SimAssertCode.EventOwnerOnlyAudience, ex.Code);
            Assert.Equal(0, log.Count);
        }
        else
        {
            Assert.True(log.TryRaise(TestKinds.OwnerOnlyKind, 0b0110_1000, in p));
            Assert.Equal((byte)0b0000_1000, log.Span[0].Audience);
        }
    }

    [Fact]
    public void Ac2_PayloadOverDescriptorLength_AssertsInSimAssertBuilds()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12 { A = 1, B = 2, C = 3 };

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => log.TryRaise(TestKinds.InternalKind, 0, in p));
            Assert.Equal(SimAssertCode.EventPayloadExceedsDescriptor, ex.Code);
        }
        else
        {
            // Truncated to the declared 4 bytes: PayloadLength is the descriptor's and nothing non-zero follows it.
            Assert.True(log.TryRaise(TestKinds.InternalKind, 0, in p));
            Assert.Equal((byte)4, log.Span[0].PayloadLength);
            ReadOnlySpan<byte> bytes = log.Span[0].Payload;
            for (int i = 4; i < EventPayload.Size; i++)
            {
                Assert.Equal((byte)0, bytes[i]);
            }
        }
    }

    [Fact]
    public void Ac2_PayloadTypeNotTheDeclaredType_AssertsUnderSimAssert_OtherwiseStoresTheBytes()
    {
        // Pay12Alt has the same size as the declared Pay12, so only the type check can catch it (TD-032 item 1, TD-033 item 3).
        EventLog log = LogOf(TestKinds.Builder());
        var wrong = new Pay12Alt { A = 1, B = 2, C = 3 };

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => log.TryRaise(TestKinds.ReplicatedKind, 0b1, in wrong));
            Assert.Equal(SimAssertCode.EventPayloadTypeMismatch, ex.Code);
            Assert.Equal(0, log.Count);
        }
        else
        {
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in wrong));
            Assert.Equal((byte)12, log.Span[0].PayloadLength);
        }

        // The declared type is always accepted.
        var right = new Pay12 { A = 1, B = 2, C = 3 };
        int before = log.Count;
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in right));
        Assert.Equal(before + 1, log.Count);
    }

    [Fact]
    public void Ac2_DeclaredLengthLongerThanThePayload_StoresTheDescriptorLength_WithAZeroTail()
    {
        // TD-032 item 2: the stored length is the descriptor's (12), not sizeof(Pay4) (4), and bytes 4..47 are zero.
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay4 { A = 0xDEAD_BEEFu };

        Assert.True(log.TryRaise(TestKinds.Loose12, 0b1, in p));

        EventEntry e = log.Span[0];
        Assert.Equal((byte)12, e.PayloadLength);
        Assert.Equal(p, e.PayloadAs<Pay4>());
        ReadOnlySpan<byte> bytes = e.Payload;
        for (int i = 4; i < EventPayload.Size; i++)
        {
            Assert.Equal((byte)0, bytes[i]);
        }
    }

    [Fact]
    public void Ac2_SlotThatPreviouslyHeldALongerPayloadUnderTheSameKind_HasAZeroTail()
    {
        // TD-033 item 5: slot 0 held 48 dirty bytes of Loose48; after Reset a 12-byte payload of the same kind reuses it.
        EventLog log = LogOf(TestKinds.Builder());
        Pay48 dirty = default;
        Span<byte> dirtyBytes = dirty;
        dirtyBytes.Fill(0xAB);
        Assert.True(log.TryRaise(TestKinds.Loose48, 0b1, in dirty));
        log.Reset();
        var shorter = new Pay12 { A = 1, B = 2, C = 3 };

        Assert.True(log.TryRaise(TestKinds.Loose48, 0b1, in shorter));

        EventEntry e = log.Span[0];
        Assert.Equal((byte)48, e.PayloadLength);
        Assert.Equal(shorter, e.PayloadAs<Pay12>());
        ReadOnlySpan<byte> bytes = e.Payload;
        for (int i = 12; i < EventPayload.Size; i++)
        {
            Assert.Equal((byte)0, bytes[i]);
        }
    }

    [Fact]
    public void Ac2_PayloadOver48Bytes_AssertsOrIsDroppedWithoutAppending()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay49();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => log.TryRaise(TestKinds.Undeclared, 0, in p));
            Assert.Equal(SimAssertCode.EventPayloadTooLarge, ex.Code);
        }
        else
        {
            Assert.False(log.TryRaise(TestKinds.Undeclared, 0, in p));
        }

        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void Ac2_DescriptorHelper_ReportsAnUndeclaredKind()
    {
        EventLog log = LogOf(TestKinds.Builder());

        IReadOnlyList<EventKind> missing = EventDescriptorAssert.FindUndeclared(
            log,
            new[] { EventKind.OpReply, TestKinds.ReplicatedKind, TestKinds.Undeclared });

        Assert.Equal(new[] { TestKinds.Undeclared }, missing);
    }

    [Fact]
    public void Ac2_DescriptorHelper_ReportsEveryEnumMemberWhenNoDescriptorIsBound()
    {
        // A world assembled without the builder declares nothing, so every member is reported (None is skipped).
        var parts = new SimWorldParts(
            Enumerable.Range(0, 14).Select(_ => Array.Empty<ISimPhase>()).ToArray(),
            Array.Empty<ITickEndCheck>(),
            new SimCoreSettings(),
            new SimCounters(),
            new SimCheckRing(8),
            new NullPhaseProbe());
        EventLog log = new SimWorld(parts).Events;

        Assert.Contains(EventKind.OpReply, EventDescriptorAssert.FindUndeclared(log));
        Assert.DoesNotContain(EventKind.None, EventDescriptorAssert.FindUndeclared(log));
        Assert.Throws<Xunit.Sdk.FailException>(() => EventDescriptorAssert.AssertAllDeclared(log));
    }

    [Fact]
    public void Ac2_FullProfileWorld_EveryEventKindMemberIsDeclared()
    {
        // The builder binds Sim Core's own descriptors by default (OpReply); systems append theirs as their stories land.
        EventLog log = new SimWorldBuilder().Freeze().Events;

        EventDescriptorAssert.AssertAllDeclared(log);
        Assert.Empty(EventDescriptorAssert.FindUndeclared(log));
    }

    [Fact]
    public void Ac2_CoreDescriptors_PassTheSharedDescriptorChecks()
    {
        EventDescriptor reply = CoreEventDescriptors.OpReply;

        EventDescriptorAssert.Check(reply, OpKindBlock.Core);
        Assert.Equal(EventKind.OpReply, reply.Kind);
        Assert.Equal(ReplicationClass.OwnerOnly, reply.Class);
        Assert.Equal((byte)36, reply.PayloadLength);
        Assert.Equal(typeof(Reply), reply.PayloadType);
        Assert.Same(reply, new SimWorldBuilder().Freeze().Events.DescriptorFor(EventKind.OpReply));
    }

    [Fact]
    public void Ac2_EveryDeclaredEventKind_Mirrors_TheOpKindBlocks_AndOpReplyIs0x0001()
    {
        Assert.Equal((ushort)0x0001, (ushort)EventKind.OpReply);
        Assert.Equal((ushort)0, (ushort)EventKind.None);
        foreach (EventKind kind in Enum.GetValues<EventKind>())
        {
            if (kind == EventKind.None)
            {
                continue;
            }

            Assert.True(OpKindBlock.TryOwnerOf((OpKind)(ushort)kind, out _), $"{kind} is outside every OpKindBlock");
        }
    }

    [Fact]
    public void Ac2_DescriptorHelper_RejectsPaddedOversizedAndOutOfBlockDescriptors()
    {
        // Proves the shared check has teeth: each of these must produce a violation.
        var padded = EventDescriptor.For<PayPadded>(TestKinds.InternalKind, ReplicationClass.Internal, false, 0, 0, 5);
        Assert.Contains(EventDescriptorAssert.Validate(padded, OpKindBlock.Players), v => v.Contains("padding", StringComparison.Ordinal));

        var floaty = new EventDescriptor(TestKinds.InternalKind, ReplicationClass.Internal, 4, false, 0, 0, 4) { PayloadType = typeof(float) };
        Assert.Contains(EventDescriptorAssert.Validate(floaty, OpKindBlock.Players), v => v.Contains("floating", StringComparison.Ordinal));

        var wrongBlock = EventDescriptor.For<Pay4>(TestKinds.InternalKind, ReplicationClass.Internal, false, 0, 0, 4);
        Assert.Contains(EventDescriptorAssert.Validate(wrongBlock, OpKindBlock.Core), v => v.Contains("outside", StringComparison.Ordinal));

        var scope = EventDescriptor.For<Pay4>(TestKinds.InternalKind, ReplicationClass.Internal, false, 64, 0, 4);
        Assert.Contains(EventDescriptorAssert.Validate(scope, OpKindBlock.Players), v => v.Contains("ResyncScope", StringComparison.Ordinal));

        var none = EventDescriptor.For<Pay4>(EventKind.None, ReplicationClass.Internal, false, 0, 0, 4);
        Assert.Contains(EventDescriptorAssert.Validate(none, OpKindBlock.Players), v => v.Contains("None", StringComparison.Ordinal));

        Assert.Empty(EventDescriptorAssert.Validate(EventDescriptor.For<Pay12>(TestKinds.ReplicatedKind, ReplicationClass.Replicated, false, 2, 0, 12), OpKindBlock.Players));
    }

    // ---------------------------------------------------------------- AC-2 builder binding

    [Fact]
    public void Ac2_BindEventKind_DuplicateKind_Throws()
    {
        SimWorldBuilder builder = TestKinds.Builder();

        SimBuildException ex = Assert.Throws<SimBuildException>(
            () => builder.BindEventKind(EventDescriptor.For<Pay4>(TestKinds.InternalKind, ReplicationClass.Internal, false, 0, 0, 4)));

        Assert.Equal(SimBuildErrorCode.DuplicateEventKind, ex.Code);
    }

    [Fact]
    public void Ac2_BindEventKind_CoreKindAlreadyBoundByTheBuilder_Throws()
    {
        var builder = new SimWorldBuilder();

        SimBuildException ex = Assert.Throws<SimBuildException>(
            () => builder.BindEventKind(EventDescriptor.For<Reply>(EventKind.OpReply, ReplicationClass.OwnerOnly, false, 0, 0, 36)));

        Assert.Equal(SimBuildErrorCode.DuplicateEventKind, ex.Code);
    }

    [Fact]
    public void Ac2_BindEventKind_InvalidDescriptors_Throw()
    {
        var builder = new SimWorldBuilder();
        (EventDescriptor Descriptor, SimBuildErrorCode Code)[] bad =
        {
            (new(EventKind.None, ReplicationClass.Internal, 0, false, 0, 0, 0), SimBuildErrorCode.InvalidKind),
            (new(TestKinds.InternalKind, (ReplicationClass)9, 0, false, 0, 0, 0), SimBuildErrorCode.InvalidEventDescriptor),
            (new(TestKinds.InternalKind, ReplicationClass.Internal, 49, false, 0, 0, 0), SimBuildErrorCode.PayloadTooLarge),
            (new(TestKinds.InternalKind, ReplicationClass.Internal, 0, false, 64, 0, 0), SimBuildErrorCode.InvalidEventDescriptor),
        };

        foreach ((EventDescriptor descriptor, SimBuildErrorCode code) in bad)
        {
            SimBuildException ex = Assert.Throws<SimBuildException>(() => builder.BindEventKind(descriptor));
            Assert.Equal(code, ex.Code);
        }

        Assert.Throws<ArgumentNullException>(() => builder.BindEventKind(null!));
    }

    [Fact]
    public void Ac2_BindEventKindAndSink_AfterFreeze_Throw()
    {
        var builder = new SimWorldBuilder();
        builder.Freeze();

        Assert.Equal(
            SimBuildErrorCode.BindAfterFreeze,
            Assert.Throws<SimBuildException>(() => builder.BindEventKind(EventDescriptor.For<Pay4>(TestKinds.InternalKind, ReplicationClass.Internal, false, 0, 0, 4))).Code);
        Assert.Equal(
            SimBuildErrorCode.BindAfterFreeze,
            Assert.Throws<SimBuildException>(() => builder.BindEventSink(new RecordingSink())).Code);
    }

    [Fact]
    public void Ac2_BindEventSink_SecondSinkOrNull_Throws()
    {
        var builder = new SimWorldBuilder();
        builder.BindEventSink(new RecordingSink());

        Assert.Equal(SimBuildErrorCode.EventSinkAlreadyBound, Assert.Throws<SimBuildException>(() => builder.BindEventSink(new RecordingSink())).Code);
        Assert.Throws<ArgumentNullException>(() => new SimWorldBuilder().BindEventSink(null!));
    }

    [Fact]
    public void Ac2_Freeze_PreallocatesTheLogAtTheConfiguredCapacity()
    {
        Assert.Equal(4096, new SimWorldBuilder().Freeze().Events.Capacity);
        Assert.Equal(0, new SimWorldBuilder().Freeze().Events.Count);
        Assert.Equal(8, new SimWorldBuilder(new SimCoreSettings { EventLogCapacity = 8 }).Freeze().Events.Capacity);
        Assert.Equal(EventPayload.Size + 16, EventEntry.SizeBytes);
    }

    // ---------------------------------------------------------------- AC-2 capacity, closed, reset

    [Fact]
    public void Ac2_FullLog_ReturnsFalse_AndKeepsExistingEntriesUnchanged()
    {
        EventLog log = LogOf(TestKinds.Builder(new SimCoreSettings { EventLogCapacity = 3 }));
        for (uint i = 0; i < 3; i++)
        {
            var p = new Pay12 { A = i };
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
        }

        var extra = new Pay12 { A = 99 };
        Assert.False(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in extra));
        Assert.False(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in extra));

        Assert.Equal(3, log.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal((uint)i, log.Span[i].Ordinal);
            Assert.Equal((uint)i, log.Span[i].PayloadAs<Pay12>().A);
        }
    }

    [Fact]
    public void Ac2_Reset_EmptiesTheLog_AndFillsTheUsedSpanWithDdUnderSimAssert()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12 { A = 5 };
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));

        log.Reset();

        Assert.Equal(0, log.Count);
        Assert.Empty(log.Span.ToArray());
        if (TestBuildExpectation.SimAssertsExpected)
        {
            for (int i = 0; i < 2; i++)
            {
                EventEntry e = log.Storage[i];
                ReadOnlySpan<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<EventEntry>(in e));
                foreach (byte b in bytes)
                {
                    Assert.Equal((byte)0xDD, b);
                }
            }

            // Only the used span is filled: the next slot is still zero.
            Assert.Equal(0u, log.Storage[2].Ordinal);
            Assert.Equal(EventKind.None, log.Storage[2].Kind);
        }
    }

    // ---------------------------------------------------------------- AC-3 call scopes

    [Fact]
    public void Ac3_RaiseOutsideAnyScope_CarriesCallSeqZero()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12();

        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));

        Assert.Equal(0u, log.Span[0].CallSeq);
        Assert.Equal(0u, log.CurrentCallSeq);
    }

    [Fact]
    public void Ac3_NestedScopes_GetFreshIncreasingCallSeq_AndRestoreTheOuterOnDispose()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12();

        using (log.BeginCall())
        {
            Assert.Equal(1u, log.CurrentCallSeq);
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p)); // outer, seq 1
            using (log.BeginCall())
            {
                Assert.Equal(2u, log.CurrentCallSeq);
                Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p)); // inner, seq 2
                using (log.BeginCall())
                {
                    Assert.Equal(3u, log.CurrentCallSeq);
                    Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p)); // innermost, seq 3
                }

                Assert.Equal(2u, log.CurrentCallSeq);
                Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p)); // back in inner, seq 2
            }

            Assert.Equal(1u, log.CurrentCallSeq);
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p)); // back in outer, seq 1
            Assert.Equal(1, log.ScopeDepth);
        }

        Assert.Equal(0u, log.CurrentCallSeq);
        Assert.Equal(0, log.ScopeDepth);
        uint[] seqs = log.Span.ToArray().Select(e => e.CallSeq).ToArray();
        Assert.Equal(new uint[] { 1, 2, 3, 2, 1 }, seqs);
    }

    [Fact]
    public void Ac3_SiblingScopes_GetDistinctIncreasingCallSeq_NeverReusingAClosedOne()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12();

        for (int i = 0; i < 3; i++)
        {
            using (log.BeginCall())
            {
                Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
            }
        }

        Assert.Equal(new uint[] { 1, 2, 3 }, log.Span.ToArray().Select(e => e.CallSeq).ToArray());
    }

    [Fact]
    public void Ac3_DisposingAScopeTwice_IsHarmless()
    {
        EventLog log = LogOf(TestKinds.Builder());
        EventCallScope outer = log.BeginCall();
        EventCallScope inner = log.BeginCall();

        inner.Dispose();
        inner.Dispose();

        Assert.Equal(1u, log.CurrentCallSeq);
        Assert.Equal(1, log.ScopeDepth);
        outer.Dispose();
        Assert.Equal(0, log.ScopeDepth);
        Assert.Equal(0u, log.CurrentCallSeq);
    }

    [Fact]
    public void Ac3_CallSeqRestartsAtOne_AfterTheLogIsReset()
    {
        EventLog log = LogOf(TestKinds.Builder());
        var p = new Pay12();
        for (int i = 0; i < 5; i++)
        {
            using (log.BeginCall())
            {
                Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
            }
        }

        log.Reset();
        using (log.BeginCall())
        {
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
        }

        Assert.Equal(1u, log.Span[0].CallSeq);
        Assert.Equal(0u, log.Span[0].Ordinal);
    }

    [Fact]
    public void Ac3_StaleScopeDisposedAfterReset_WithANonZeroOuterCallSeq_ChangesNothing()
    {
        // TD-033 item 4: the inner scope's outer CallSeq is 1; restoring it after Reset would resurrect a dead scope.
        EventLog log = LogOf(TestKinds.Builder());
        EventCallScope outer = log.BeginCall();
        EventCallScope inner = log.BeginCall();
        Assert.Equal(2u, log.CurrentCallSeq);

        log.Reset();
        inner.Dispose();

        Assert.Equal(0u, log.CurrentCallSeq);
        Assert.Equal(0, log.ScopeDepth);

        // A new tick's scope is not popped by the stale ones either.
        using (log.BeginCall())
        {
            outer.Dispose();
            inner.Dispose();
            Assert.Equal(1u, log.CurrentCallSeq);
            Assert.Equal(1, log.ScopeDepth);
        }

        Assert.Equal(0, log.ScopeDepth);
        Assert.Equal(0u, log.CurrentCallSeq);
    }

    [Fact]
    public void Ac3_DisposingACopyOfAClosedScope_DoesNotPopTheOuterScope()
    {
        EventLog log = LogOf(TestKinds.Builder());
        EventCallScope outer = log.BeginCall();
        EventCallScope inner = log.BeginCall();
        EventCallScope copy = inner;

        inner.Dispose();
        copy.Dispose();

        Assert.Equal(1u, log.CurrentCallSeq);
        Assert.Equal(1, log.ScopeDepth);
        outer.Dispose();
        Assert.Equal(0, log.ScopeDepth);
    }
}

/// <summary>
/// Covers AC-3's allocation clause for story 015: <see cref="EventLog.BeginCall"/> and <see cref="EventLog.TryRaise{T}"/> allocate 0 B.
/// Joins the non-parallel collection so runtime warm-up on another test's thread cannot land in a measured window (see
/// <see cref="AllocAssert"/>).
/// </summary>
[Trait("Id", "SC-R12-2")]
[Collection(NonParallelCollection.Name)]
public class EventLogAllocationTests
{
    [Fact]
    public void Ac3_BeginCallAndTryRaise_AllocateNothing()
    {
        // Arrange
        EventLog log = TestKinds.OpenLog(TestKinds.Builder());
        var p = new Pay12 { A = 1, B = 2, C = 3 };
        int appended = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("2,000 nested BeginCall + TryRaise pairs", () =>
        {
            appended = 0;
            for (int i = 0; i < 1000; i++)
            {
                using (log.BeginCall())
                {
                    using (log.BeginCall())
                    {
                        if (log.TryRaise(TestKinds.ReplicatedKind, 0b11, in p))
                        {
                            appended++;
                        }
                    }

                    if (log.TryRaise(TestKinds.OwnerOnlyKind, 0b10, in p))
                    {
                        appended++;
                    }
                }
            }

            log.Reset();
        });

        // Assert
        Assert.Equal(2000, appended);
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void Ac3_RefusedRaises_AllocateNothing()
    {
        // Arrange: a log of 2 held full, so every raise is refused.
        EventLog log = TestKinds.OpenLog(TestKinds.Builder(new SimCoreSettings { EventLogCapacity = 2 }));
        var p = new Pay12();
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));
        Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p));

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("1,000 refused raises", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                log.TryRaise(TestKinds.ReplicatedKind, 0b1, in p);
            }
        });

        Assert.Equal(2, log.Count);
    }
}
