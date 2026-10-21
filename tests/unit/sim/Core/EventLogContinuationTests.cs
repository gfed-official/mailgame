using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// <see cref="EventLog.TryRaiseContinuation{T}"/> (ADR-0008 section 1.3 option (b), owner chunking; added for story grid-inventory/005's
/// mailId runs): the <c>Continuation</c> flag, the head's <c>CallSeq</c>, the chunk length (clamped to the descriptor length, asserted
/// under <c>SIM_ASSERT</c>), the zero-filled tail, admission when the log is full, and the orphan-chunk assert. Behaviour that differs
/// between SIM_ASSERT and other builds branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
public class EventLogContinuationTests
{
    private static Pay48 Filled(byte value)
    {
        Pay48 chunk = default;
        Span<byte> bytes = chunk;
        bytes.Fill(value);
        return chunk;
    }

    [Fact]
    public void Continuation_AfterItsHead_CarriesTheFlagTheHeadsCallSeqAndItsLength_AndAZeroTail()
    {
        // Arrange
        EventLog log = TestKinds.OpenLog(TestKinds.Builder());
        Pay48 head = Filled(0x11);
        Pay48 chunk = Filled(0xFF);
        using EventCallScope scope = log.BeginCall();

        // Act
        Assert.True(log.TryRaise(TestKinds.Wide, 0b1, in head));
        bool raised = log.TryRaiseContinuation(TestKinds.Wide, 0b1, in chunk, 12);

        // Assert
        Assert.True(raised);
        Assert.Equal(2, log.Count);
        EventEntry e = log.Span[1];
        Assert.Equal(TestKinds.Wide, e.Kind);
        Assert.Equal(1u, e.Ordinal);
        Assert.Equal(EventFlags.Continuation, e.Flags);
        Assert.Equal(EventFlags.None, log.Span[0].Flags);
        Assert.Equal(log.Span[0].CallSeq, e.CallSeq);
        Assert.Equal(log.CurrentCallSeq, e.CallSeq);
        Assert.Equal(ReplicationClass.Replicated, e.Class);
        Assert.Equal(0b1, e.Audience);
        Assert.Equal(12, e.PayloadLength);
        ReadOnlySpan<byte> payload = e.Payload;
        Assert.True(payload[..12].ToArray().All(b => b == 0xFF));
        Assert.True(payload[12..].ToArray().All(b => b == 0));
        Assert.Equal(0, e.Reserved);
    }

    [Fact]
    public void Continuation_LengthOverTheDescriptorLength_AssertsUnderSimAssert_OrIsClampedElsewhere()
    {
        // Arrange: Loose12 declares 12 B
        EventLog log = TestKinds.OpenLog(TestKinds.Builder());
        var head = new Pay12 { A = 1 };
        Pay48 chunk = Filled(0xAB);
        using EventCallScope scope = log.BeginCall();
        Assert.True(log.TryRaise(TestKinds.Loose12, 0, in head));

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException over = Assert.Throws<SimAssertException>(() => log.TryRaiseContinuation(TestKinds.Loose12, 0, in chunk, 20));
            Assert.Equal(SimAssertCode.EventPayloadExceedsDescriptor, over.Code);
            SimAssertException negative = Assert.Throws<SimAssertException>(() => log.TryRaiseContinuation(TestKinds.Loose12, 0, in chunk, -1));
            Assert.Equal(SimAssertCode.EventPayloadExceedsDescriptor, negative.Code);
            Assert.Equal(1, log.Count);
        }
        else
        {
            Assert.True(log.TryRaiseContinuation(TestKinds.Loose12, 0, in chunk, 20));
            Assert.Equal(12, log.Span[1].PayloadLength);
            ReadOnlySpan<byte> payload = log.Span[1].Payload;
            Assert.True(payload[12..].ToArray().All(b => b == 0));
        }
    }

    [Theory]
    [InlineData("no-head")]
    [InlineData("other-kind")]
    [InlineData("other-call")]
    [InlineData("no-scope")]
    public void Continuation_Orphan_AssertsUnderSimAssert_OrIsAppendedAsGivenElsewhere(string orphan)
    {
        // Arrange
        EventLog log = TestKinds.OpenLog(TestKinds.Builder());
        Pay48 wide = Filled(0x22);
        var other = new Pay12 { A = 7 };
        Pay48 chunk = Filled(0x33);
        EventCallScope first = log.BeginCall();
        switch (orphan)
        {
            case "other-kind":
                Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0, in other));
                break;
            case "other-call":
                Assert.True(log.TryRaise(TestKinds.Wide, 0, in wide));
                first.Dispose();
                first = log.BeginCall();
                break;
            case "no-scope":
                Assert.True(log.TryRaise(TestKinds.Wide, 0, in wide));
                first.Dispose();
                break;
        }

        int before = log.Count;

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => log.TryRaiseContinuation(TestKinds.Wide, 0, in chunk, 6));
            Assert.Equal(SimAssertCode.EventContinuationOrphan, thrown.Code);
            Assert.Equal(before, log.Count);
        }
        else
        {
            Assert.True(log.TryRaiseContinuation(TestKinds.Wide, 0, in chunk, 6));
            Assert.Equal(before + 1, log.Count);
            Assert.Equal(EventFlags.Continuation, log.Span[before].Flags);
        }

        first.Dispose();
    }

    [Fact]
    public void Continuation_LogFull_IsDroppedAndCountedLikeItsHead_WithoutAnOrphanAssert()
    {
        // Arrange: fill the log, so the next head is dropped
        EventLog log = TestKinds.OpenLog(TestKinds.Builder());
        var payload = new Pay12 { A = 3 };
        using EventCallScope scope = log.BeginCall();
        for (int i = 0; i < log.Capacity; i++)
        {
            Assert.True(log.TryRaise(TestKinds.ReplicatedKind, 0b10, in payload));
        }

        Assert.False(log.TryRaise(TestKinds.ReplicatedKind, 0b10, in payload));

        // Act
        bool raised = log.TryRaiseContinuation(TestKinds.ReplicatedKind, 0b10, in payload, 12);

        // Assert
        Assert.False(raised);
        Assert.Equal(log.Capacity, log.Count);
        Assert.True(log.Overflow.Overflow);
        Assert.Equal(2u, log.Overflow.DroppedCount);
        Assert.Equal(0b10, log.Overflow.DroppedAudienceMask);
    }
}
