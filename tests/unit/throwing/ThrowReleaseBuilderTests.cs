using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Net;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing-catching story 007 (GDD T1 cooldown, T4 "Release"; ADR-0012 section 6, ADR-0015 section 1, ADR-0016 section 1):
/// <see cref="ThrowReleaseSender.Commit"/>, the target of the router's Throw commit, over a published <see cref="ThrowPreviewState"/>, a
/// recording <see cref="IRequestSink"/> and a host clock double; and <see cref="ThrowReplyPart"/>. The router stub is the test itself: a
/// commit calls <c>Commit</c> once, a cancelled aim never calls it. M-1a greybox: a commit inside the cooldown is dropped, not held.
/// </summary>
public class ThrowReleaseBuilderTests
{
    private const int CooldownTicks = 5;

    [Fact]
    [Trait("Id", "TC-X-3")]
    public void Commit_OnAnActivePreview_SendsOneThrowRelease_WithThePublishedIntegers_ThroughTheCodec()
    {
        // Arrange
        var rig = new Rig();

        // Act
        ThrowSendResult result = rig.Sender.Commit(20);

        // Assert
        Assert.Equal(ThrowSendResult.Sent, result);
        Op sent = Assert.Single(rig.Requests.Sent);
        var expected = new ThrowReleasePayload
        {
            EntryId = 41, Units = 5, Side = -1, FeetX = 10_000, FeetY = 0, FeetZ = 20_000, DirX = 0, DirY = 3406, DirZ = -16_026,
        };
        Assert.Equal(ThrowOpKinds.ThrowRelease, sent.Kind);
        Assert.Equal(expected, sent.PayloadAs<ThrowReleasePayload>());

        OpCodecRegistry registry = CodecTestKit.BuildRegistry();
        var wire = new byte[64];
        int written = RequestEnvelope.Encode(registry, in sent, wire);
        Assert.Equal(DecodeResult.Ok, RequestEnvelope.TryDecode(registry, wire.AsSpan(0, written), out Op decoded));
        Assert.Equal(expected, decoded.PayloadAs<ThrowReleasePayload>());
    }

    [Theory]
    [Trait("Id", "TC-T1-1b")]
    [InlineData(NotThrowable.HandsSlot)]
    [InlineData(NotThrowable.EmptySlot)]
    [InlineData(NotThrowable.Swimming)]
    [InlineData(NotThrowable.None)]          // not aiming: inactive with no refusal
    public void Commit_WithoutAnActiveArc_SendsNothing(NotThrowable refusal)
    {
        // Arrange
        var rig = new Rig();
        rig.Preview.State = default;
        rig.Preview.State.NotThrowable = refusal;

        // Act
        ThrowSendResult result = rig.Sender.Commit(20);

        // Assert
        Assert.Equal(ThrowSendResult.NothingToThrow, result);
        Assert.Empty(rig.Requests.Sent);
        Assert.Equal(0u, rig.Sender.Released);
    }

    [Theory]
    [Trait("Id", "TC-T1-2b")]
    [InlineData(4u, ThrowSendResult.Cooldown, 1)]   // inside throwCooldownTicks: dropped and counted
    [InlineData(5u, ThrowSendResult.Sent, 2)]
    public void SecondCommit_InsideTheCooldown_IsDropped_AtTheCooldown_IsSent(uint hostTicksLater, ThrowSendResult expected, int sent)
    {
        // Arrange
        var rig = new Rig();
        rig.Clock.Tick = 100;
        Assert.Equal(ThrowSendResult.Sent, rig.Sender.Commit(20));
        rig.Clock.Tick = 100 + hostTicksLater;

        // Act
        ThrowSendResult result = rig.Sender.Commit(24);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(sent, rig.Requests.Sent.Count);
        Assert.Equal(2 - sent, rig.Sender.CooldownDrops);
    }

    [Fact]
    public void BusySink_SendsNothing_AndDoesNotStartTheCooldown()
    {
        // Arrange
        var rig = new Rig();
        rig.Requests.Busy = true;

        // Act
        ThrowSendResult busy = rig.Sender.Commit(20);
        rig.Requests.Busy = false;
        rig.Clock.Tick++;
        ThrowSendResult next = rig.Sender.Commit(21);

        // Assert
        Assert.Equal((ThrowSendResult.Busy, ThrowSendResult.Sent), (busy, next));
        Assert.Single(rig.Requests.Sent);
        Assert.Equal(1u, rig.Sender.Released);
    }

    [Theory]
    [InlineData(EndClass.Misdeliver, 500)]   // red since client tick 10, released at 25: 15 ticks = 500 ms
    [InlineData(EndClass.Accept, 0)]
    public void SentRelease_AppendsARecord_WithTheClassAtReleaseAndTheRedAge(EndClass cls, int redAgeMs)
    {
        // Arrange
        var rig = new Rig();
        rig.Preview.State.EndClass = cls;
        rig.Preview.State.RedSinceTick = cls == EndClass.Misdeliver ? 10u : 0u;

        // Act
        Assert.Equal(ThrowSendResult.Sent, rig.Sender.Commit(25));

        // Assert
        ref readonly ReleaseRecord r = ref rig.Sender.Newest;
        Assert.Equal((1u, 25u, cls, redAgeMs, (byte)1), (r.Seq, r.ClientTick, r.ClassAtRelease, r.RedAgeAtReleaseMs, r.OriginClampedUnknown));
        Assert.Equal((G3(rig.Preview.State.Feet), (short)0, (short)3406, (short)-16_026), (G3(r.Feet), r.DirX, r.DirY, r.DirZ));
    }

    [Theory]
    [InlineData((ushort)0, false, 0u)]                                   // ok: nothing published
    [InlineData((ushort)ThrowReason.StaleEntry, false, 1u)]
    [InlineData(ThrowReasons.BadRequest, false, 1u)]
    [InlineData((ushort)ThrowReason.StaleEntry, true, 0u)]               // another kind's reply
    public void ReplyPart_PublishesOnlyRejectedThrowReleases(ushort reason, bool otherKind, uint published)
    {
        // Arrange
        var part = new ThrowReplyPart();
        var reply = new Reply { ReqId = 9, Kind = otherKind ? OpKind.TeleportAck : ThrowOpKinds.ThrowRelease, Reason = reason };

        // Act
        part.ApplyReply(in reply);

        // Assert
        Assert.Equal(published, part.Last.Seq);
        Assert.Equal(published == 1 ? ((uint)9, reason) : (0u, (ushort)0), (part.Last.ReqId, part.Last.Reason));
    }

    private static (int, int, int) G3(Vec3Mm v) => (v.X, v.Y, v.Z);

    private sealed class Rig
    {
        public Rig()
        {
            Preview.State.Seq = 1;
            Preview.State.Active = 1;
            Preview.State.Entry = EntryId.Real(41);
            Preview.State.Units = 5;
            Preview.State.Side = -1;
            Preview.State.Feet = new Vec3Mm(10_000, 0, 20_000);
            Preview.State.DirX = 0;
            Preview.State.DirY = 3406;
            Preview.State.DirZ = -16_026;
            Preview.State.EndClass = EndClass.Ground;
            Preview.State.EndAge = 26;
            Preview.State.PointCount = 27;
            Sender = new ThrowReleaseSender(Preview, Requests, Clock, CooldownTicks);
        }

        public PreviewDouble Preview { get; } = new();

        public RecordingSink Requests { get; } = new();

        public ClockDouble Clock { get; } = new() { Tick = 100 };

        public ThrowReleaseSender Sender { get; }
    }

    private sealed class PreviewDouble : IThrowPreviewRead
    {
        public ThrowPreviewState State;

        public ref readonly ThrowPreviewState Current => ref State;

        public ReadOnlySpan<Vec3Mm> Polyline => default;

        public ReadOnlySpan<Vec3Mm> HatPins => default;
    }

    private sealed class RecordingSink : IRequestSink
    {
        public List<Op> Sent { get; } = new();

        public bool Busy { get; set; }

        public bool TrySend(in Op op)
        {
            if (Busy)
            {
                return false;
            }

            Sent.Add(op);
            return true;
        }
    }

    private sealed class ClockDouble : IHostClock
    {
        public uint Tick { get; set; }

        public uint EstimatedHostTick => Tick;

        public bool IsValid => true;
    }
}
