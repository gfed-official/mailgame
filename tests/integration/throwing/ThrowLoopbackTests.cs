using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Integration.ThrowingCatching;

/// <summary>
/// throwing-catching story 006, loopback end to end (ADR-0015 sections 1 and 9, ADR-0011; TR-throwing-009, TR-throwing-010): a
/// <see cref="HostSession"/> over a <c>SimProfile.ForTest</c> world whose stub system, registered under <see cref="SystemId.Throwing"/>,
/// binds <c>ThrowRelease</c> and the two event kinds with <see cref="ThrowingCodecs"/>. The client's <c>ThrowRelease</c> reaches the
/// host op ring stamped; its stub handler raises <c>ThrowLaunched</c> then <c>FlightResolved</c> for one fake flight, and the host
/// <c>ClientLayer</c> receives both decoded, in log order, at the next client tick. Deterministic: a fake integer clock, no I/O.
/// </summary>
public class ThrowLoopbackTests
{
    private const long Freq = 10_000_000;
    private const long OneTick = 333_334;

    private static readonly ThrowReleasePayload _release = new()
    {
        EntryId = 0x00C0FFEE, Units = 2, Side = -1, FeetX = -12_000, FeetY = 350, FeetZ = 40_500, DirX = -11585, DirY = 0, DirZ = 11585,
    };

    private static readonly ThrowLaunched _launched = new()
    {
        FlightId = 0xFFFF, Thrower = 0, Flags = ThrowLaunched.OriginClampedBit, EntryId = 0x00C0FFEE, Units = 2, Hop = 1, KindDef = 17,
        OriginX = -12_000, OriginY = 1_450, OriginZ = 40_500, VelX = -9_000, VelY = 6_000, VelZ = 9_000, LaunchTick = 1,
    };

    private static readonly FlightResolved _resolved = new()
    {
        FlightId = 0xFFFF, Terminal = Terminal.Water, Rank = 3, Receiver = 0, ContactX = -15_250, ContactY = -40, ContactZ = 43_750,
        Tick = 1, UnitsIn = 0, UnitsFumbled = 2, WorldItem = 0x0001_0002,
    };

    private sealed class FakeClock : IMonotonicClock
    {
        public long Now { get; set; }

        public long Timestamp => Now;

        public long Frequency => Freq;
    }

    /// <summary>Stand-in for the Throwing system: binds the kinds, and its handler raises one fake flight's two events to slot 0.</summary>
    private sealed class StubThrowing : ISimSystem, IOpHandler
    {
        private readonly EventLogFacade _events;
        private readonly Rig _rig;

        public StubThrowing(EventLogFacade events, Rig rig)
        {
            _events = events;
            _rig = rig;
        }

        public OpKind Kind => ThrowOpKinds.ThrowRelease;

        public void Bind(SimWorldBuilder builder)
        {
            builder.BindEventKind(ThrowEventDescriptors.ThrowLaunched);
            builder.BindEventKind(ThrowEventDescriptors.FlightResolved);
            builder.BindOp(ThrowDescriptors.ThrowRelease, this);
            builder.BindRecorder(new Recorder(_rig));
        }

        public Reply Apply(in Op op, in TickContext ctx)
        {
            _events.TryRaise(ThrowEventKinds.ThrowLaunched, 0x01, in _launched);
            _events.TryRaise(ThrowEventKinds.FlightResolved, 0x01, in _resolved);
            return Reply.Ok(in op);
        }
    }

    /// <summary>Sees every op as it enters the host op ring (after the adapter stamped it).</summary>
    private sealed class Recorder : IOpRecorder
    {
        private readonly Rig _rig;

        public Recorder(Rig rig) => _rig = rig;

        public void OnEnqueued(uint boundaryTick, in Op op)
        {
            if (op.Kind == ThrowOpKinds.ThrowRelease)
            {
                _rig.Enqueued.Add(op);
            }
        }

        public void OnDrained(uint applyTick, in Op op, in Reply reply)
        {
        }

        public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
        {
        }

        public void OnOverflow(uint tick)
        {
        }
    }

    /// <summary>The host client's mirror: records the Throwing entries MirrorDrain applies and the client tick it applied them at.</summary>
    private sealed class Mirror : IMirrorConsumer
    {
        private readonly Rig _rig;

        public Mirror(Rig rig) => _rig = rig;

        public void OnEntry(uint tick, in EventEntry entry)
        {
            if (entry.Kind == ThrowEventKinds.ThrowLaunched || entry.Kind == ThrowEventKinds.FlightResolved)
            {
                _rig.Entries.Add((_rig.Session.Client.ClientTick, entry));
            }
        }

        public void OnReply(uint tick, in Reply reply)
        {
        }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly List<Op> Enqueued = new();
        public readonly List<(uint ClientTick, EventEntry Entry)> Entries = new();
        public readonly HostSession Session;

        public Rig()
        {
            SimProfile profile = SimProfile.ForTest("throw-loopback", null, new SimSystemEntry(SystemId.Throwing, ctx => new StubThrowing(ctx.Events, this)));
            var args = new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 7);
            Session = new HostSession(args, Clock, ThrowingCodecs.CreateOps(), ThrowingCodecs.CreateEvents());
            Session.MirrorDrain.Register(new Mirror(this));
        }

        public void RunFrame()
        {
            Clock.Now += OneTick;
            Session.RunFrame();
        }
    }

    [Fact]
    [Trait("Id", "TR-throwing-009")]
    [Trait("Id", "TR-throwing-010")]
    public void ClientThrowRelease_ReachesTheOpRingStamped_AndBothEventsReachTheClientLayerDecodedInLogOrderAtTheNextClientTick()
    {
        // Arrange
        var rig = new Rig();
        rig.RunFrame(); // tick 0: the local join
        Assert.True(rig.Session.IsLocalJoined);
        var op = new Op { Kind = ThrowOpKinds.ThrowRelease };
        op.SetPayload(_release);
        byte[] wire = new byte[28];
        Assert.Equal(28, new ThrowReleaseCodec().Encode(in op, wire));

        // Act
        rig.Session.ClientAdapter.Send(ThrowOpKinds.ThrowRelease, wire);
        rig.RunFrame(); // tick 1 applies it and raises the two events
        uint clientTickAfterApply = rig.Session.Client.ClientTick;
        Assert.Empty(rig.Entries);
        rig.RunFrame(); // the next client tick's MirrorDrain applies them

        // Assert: the op ring
        Op enqueued = Assert.Single(rig.Enqueued);
        Assert.Equal(ThrowOpKinds.ThrowRelease, enqueued.Kind);
        Assert.Equal(ActorId.FromEntity(rig.Session.LocalPlayerId), enqueued.Actor);
        Assert.Equal(28, enqueued.PayloadLength);
        Assert.Equal(Bytes(_release), ((ReadOnlySpan<byte>)enqueued.Payload).Slice(0, 28).ToArray());

        // Assert: the client layer
        Assert.Equal(2, rig.Entries.Count);
        (uint launchedTick, EventEntry launched) = rig.Entries[0];
        (uint resolvedTick, EventEntry resolved) = rig.Entries[1];
        Assert.Equal(ThrowEventKinds.ThrowLaunched, launched.Kind);
        Assert.Equal(ThrowEventKinds.FlightResolved, resolved.Kind);
        Assert.True(launched.Ordinal < resolved.Ordinal);
        Assert.Equal(clientTickAfterApply, launchedTick);
        Assert.Equal(clientTickAfterApply, resolvedTick);
        Assert.Equal(40, launched.PayloadLength);
        Assert.Equal(32, resolved.PayloadLength);
        Assert.Equal(Bytes(_launched), Bytes(launched.PayloadAs<ThrowLaunched>()));
        Assert.Equal(Bytes(_resolved), Bytes(resolved.PayloadAs<FlightResolved>()));
    }

    private static byte[] Bytes<T>(T value)
        where T : unmanaged
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).ToArray();
}
