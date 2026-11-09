using System.Buffers.Binary;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Sim.Tests.Unit.Net.NetTestKit;
using EventCursor = PerformativeMail.Client.Core.Presentation.EventCursor;

namespace PerformativeMail.Sim.Tests.Integration.Presentation;

/// <summary>
/// godot-host-shell story 006 AC-3 (ADR-0016 section 5): with the <see cref="PresentationEventStream"/> set as the client adapter's
/// <see cref="ClientAdapter.DecodedEvents"/> (what boot step 7 does), an event a loopback fixture op raises is read by a cursor opened
/// before the first frame in the same frame its batch is decoded, before <c>MirrorDrain</c> applies it, exactly once per cursor and in
/// (<c>sourceTick</c>, <c>ordinal</c>) order. Fixture op in the test-profile style of network-adapter-loopback 007; fake clock, fixed seed.
/// </summary>
public class PresentationEventStreamFeedTests
{
    private const long Freq = 10_000_000;
    private const long OneTick = 333_334;
    private const OpKind FixtureOp = (OpKind)0x0F01;
    private const EventKind FixtureEvent = (EventKind)0x0F20;
    private const ushort OpCodecId = 930;
    private const ushort EventCodecId = 931;

    [Fact]
    [Trait("Id", "GHS-006-AC3")]
    public void DecodedEvent_IsReadInTheSameFrameBeforeMirrorDrain_OncePerCursor()
    {
        var rig = new Rig();
        EventCursor first = rig.Events.OpenCursor(EventKindSet.Of(FixtureEvent));
        EventCursor second = rig.Events.OpenCursor(EventKindSet.Of(FixtureEvent));
        rig.RunFrame(); // tick 0: the local join

        rig.Send(7);
        rig.RunFrame(); // tick 1 applies the op; its batch is decoded at the end of this frame

        Assert.Equal(0, rig.Mirror.Entries);
        Assert.True(first.TryRead(out PresentedEvent e));
        Assert.Equal((FixtureEvent, 1u, 0u, 7u), (e.Kind, e.SourceTick, e.Ordinal, e.PayloadAs<uint>()));
        Assert.False(first.TryRead(out _));
        Assert.True(second.TryRead(out PresentedEvent again));
        Assert.Equal((1u, 0u, 7u), (again.SourceTick, again.Ordinal, again.PayloadAs<uint>()));
        Assert.False(second.TryRead(out _));

        rig.RunFrame(); // the next client tick's MirrorDrain applies the event

        Assert.Equal(1, rig.Mirror.Entries);
        Assert.False(first.TryRead(out _));
        Assert.False(second.TryRead(out _));
    }

    [Fact]
    [Trait("Id", "GHS-006-AC3")]
    public void EventsOfSeveralBatches_AreReadInSourceTickThenOrdinalOrder()
    {
        var rig = new Rig();
        EventCursor cursor = rig.Events.OpenCursor(EventKindSet.Of(FixtureEvent));
        rig.RunFrame(); // tick 0: the local join
        rig.Send(1);
        rig.Send(2);
        rig.RunFrame(); // tick 1: two events (the first op's reply record sits between them)
        rig.Send(3);
        rig.RunFrame(); // tick 2

        var read = new List<(uint Tick, uint Ordinal, uint Payload)>();
        while (cursor.TryRead(out PresentedEvent e))
        {
            read.Add((e.SourceTick, e.Ordinal, e.PayloadAs<uint>()));
        }

        Assert.Equal([1u, 2u, 3u], read.Select(r => r.Payload));
        Assert.Equal([1u, 1u, 2u], read.Select(r => r.Tick));
        Assert.True(read[0].Ordinal < read[1].Ordinal);
    }

    [Fact]
    [Trait("Id", "M-MD7-2")]
    public void OwnReply_IsPublishedOnceInTheFrameItsBatchIsDecoded_AfterTheOpsEvents()
    {
        var rig = new Rig();
        EventCursor cursor = rig.Events.OpenCursor(EventKindSet.Of(FixtureEvent, EventKind.OpReply));
        rig.RunFrame(); // tick 0: the local join (its reply is the join op's, read and skipped below)
        while (cursor.TryRead(out _))
        {
        }

        rig.Send(5);
        rig.RunFrame(); // tick 1 applies the op; its event and reply records are decoded at the end of this frame

        Assert.True(cursor.TryRead(out PresentedEvent e));
        Assert.True(cursor.TryRead(out PresentedEvent reply));
        Assert.Equal((FixtureEvent, EventKind.OpReply, 1u), (e.Kind, reply.Kind, reply.SourceTick));
        Assert.Equal((FixtureOp, true), (reply.PayloadAs<Reply>().Kind, reply.PayloadAs<Reply>().IsOk));
        Assert.True(e.Ordinal < reply.Ordinal);
        rig.RunFrame();
        Assert.False(cursor.TryRead(out _));
    }

    private sealed class FakeClock : IMonotonicClock
    {
        public long Now { get; set; }

        public long Timestamp => Now;

        public long Frequency => Freq;
    }

    /// <summary>Test-profile system: one fixture op whose handler raises one event carrying the op's payload and replies Ok.</summary>
    private sealed class FixtureSystem(EventLogFacade events) : ISimSystem, IOpHandler
    {
        public OpKind Kind => FixtureOp;

        public void Bind(SimWorldBuilder builder)
        {
            builder.BindEventKind(EventDescriptor.For<uint>(FixtureEvent, ReplicationClass.Replicated, false, 1, EventCodecId, 4));
            builder.BindOp(OpDescriptor.For<uint>(FixtureOp, false, OpCodecId, 4), this);
        }

        public Reply Apply(in Op op, in TickContext ctx)
        {
            uint payload = op.PayloadAs<uint>();
            events.TryRaise(FixtureEvent, 0x01, in payload);
            return Reply.Ok(in op);
        }
    }

    /// <summary>Counts the fixture events the host client's MirrorDrain applies.</summary>
    private sealed class MirrorCounter : IMirrorConsumer
    {
        public long Entries { get; private set; }

        public void OnEntry(uint tick, in EventEntry entry) => Entries += entry.Kind == FixtureEvent ? 1 : 0;

        public void OnReply(uint tick, in Reply reply)
        {
        }
    }

    private sealed class Rig
    {
        private readonly FakeClock _clock = new();
        private readonly HostSession _session;
        private readonly byte[] _payload = new byte[4];

        public Rig()
        {
            SimProfile profile = SimProfile.ForTest("presentation-feed", null, new SimSystemEntry(SystemId.GridInventory, ctx => new FixtureSystem(ctx.Events)));
            _session = new HostSession(
                new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 7),
                _clock,
                new IOpCodec[] { new U32Codec(OpCodecId) },
                new IEventCodec[] { new FixedLengthEventCodec(EventCodecId, 4) },
                new HostSessionOptions());
            _session.MirrorDrain.Register(Mirror);
            _session.ClientAdapter.DecodedEvents = Events;
        }

        public PresentationEventStream Events { get; } = new();

        public MirrorCounter Mirror { get; } = new();

        public void RunFrame()
        {
            _clock.Now += OneTick;
            _session.RunFrame();
        }

        public void Send(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_payload, value);
            _session.ClientAdapter.Send(FixtureOp, _payload);
        }
    }
}
