using System.Buffers.Binary;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;
using static PerformativeMail.Sim.Tests.Unit.Net.NetTestKit;

namespace PerformativeMail.Sim.Tests.Integration.Net;

/// <summary>
/// network-adapter-loopback story 007: <see cref="HostSession"/> = <c>SimComposition.Build</c> + <see cref="HostAdapter"/> and
/// <see cref="ClientAdapter"/> over <see cref="LoopbackTransport"/> + <see cref="FrameRunner"/>. AC-1 = SC-R2-1 over loopback,
/// AC-2 = local join, AC-3 = SC-X-2 timing rows with a fixture op (story 009 re-runs it with the real request). Also covers
/// TD-065 item 1 (deferred sink), TD-068 item 1 (quiet-tick clock) and TD-062 item 8 (handler sees the stamped actor).
/// Deterministic: a fake integer clock, fixed seed, no I/O. Non-parallel because of the allocation test.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class HostLoopbackSessionTests
{
    private const long Freq = 10_000_000;
    private const long ThreeTicks = 1_000_000;
    private const long OneTick = 333_334;
    private const long ZeroTicks = 1_000;
    private const OpKind FixtureOp = (OpKind)0x0F01;
    private const EventKind FixtureEvent = (EventKind)0x0F20;
    private const ushort OpCodecId = 930;
    private const ushort EventCodecId = 931;

    private static readonly byte[] _payload = { 7, 0, 0, 0 };

    // ------------------------------------------------------------ fixtures

    private sealed class FakeClock : IMonotonicClock
    {
        public long Now { get; set; }

        public long Timestamp => Now;

        public long Frequency => Freq;
    }

    /// <summary>Test-profile system: one fixture op whose handler raises one event to slot 0 and replies Ok.</summary>
    private sealed class FixtureSystem : ISimSystem, IOpHandler, ISimPhase
    {
        private readonly EventLogFacade _events;
        private readonly Rig _rig;

        public FixtureSystem(EventLogFacade events, Rig rig)
        {
            _events = events;
            _rig = rig;
        }

        public OpKind Kind => FixtureOp;

        public void Bind(SimWorldBuilder builder)
        {
            builder.BindEventKind(EventDescriptor.For<uint>(FixtureEvent, ReplicationClass.Replicated, false, 1, EventCodecId, 4));
            builder.BindOp(OpDescriptor.For<uint>(FixtureOp, false, OpCodecId, 4), this);
            builder.Bind(PhaseId.RunStep, this);
            builder.BindRecorder(new Recorder(_rig));
        }

        public Reply Apply(in Op op, in TickContext ctx)
        {
            if (_rig.Recording)
            {
                _rig.Applied.Add((ctx.Tick, op.Actor));
            }

            uint payload = op.PayloadAs<uint>();
            _events.TryRaise(FixtureEvent, 0x01, in payload);
            return Reply.Ok(in op);
        }

        public void Run(in TickContext ctx)
        {
            if (_rig.Recording)
            {
                _rig.Log.Add("Sim");
            }
        }
    }

    /// <summary>Sees every drained op with its stamped actor (PlayerJoin included).</summary>
    private sealed class Recorder : IOpRecorder
    {
        private readonly Rig _rig;

        public Recorder(Rig rig) => _rig = rig;

        public void OnEnqueued(uint boundaryTick, in Op op)
        {
        }

        public void OnDrained(uint applyTick, in Op op, in Reply reply)
        {
            if (_rig.Recording)
            {
                _rig.Drained.Add((applyTick, op.Kind, op.Actor, reply.Result));
            }
        }

        public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
        {
        }

        public void OnOverflow(uint tick)
        {
        }
    }

    private sealed class Transport : IFrameTransport
    {
        private readonly Rig _rig;
        private readonly IFrameTransport _inner;

        public Transport(Rig rig, IFrameTransport inner)
        {
            _rig = rig;
            _inner = inner;
        }

        public void PumpInbound()
        {
            if (_rig.Recording)
            {
                _rig.Log.Add("Pump");
            }

            _inner.PumpInbound();
        }

        public void FlushOutbound(uint tick)
        {
            if (_rig.Recording)
            {
                _rig.Log.Add("Flush");
                _rig.WorldTickAtFlush.Add((tick, _rig.Session.World.CurrentTick));
            }

            _inner.FlushOutbound(tick);
        }
    }

    private sealed class ClientProbe : IClientStep
    {
        private readonly Rig _rig;

        public ClientProbe(Rig rig) => _rig = rig;

        public void Step(in ClientStepContext ctx)
        {
            if (_rig.SendInFirstStep && ctx.IsFirstTickInBatch)
            {
                _rig.SendInFirstStep = false;
                _rig.SentInStep.Add((_rig.Frame, ctx.ClientTick));
                _rig.Send();
            }

            if (_rig.Recording)
            {
                _rig.Log.Add("Client");
                _rig.FirstFlags.Add(ctx.IsFirstTickInBatch);
            }
        }
    }

    /// <summary>Host Presentation stand-in: sees each tick's log in the frame that ran it.</summary>
    private sealed class SpySink : IEventSink
    {
        private readonly Rig _rig;

        public SpySink(Rig rig) => _rig = rig;

        public long Fixture;

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Kind == FixtureEvent)
                {
                    Fixture++;
                    if (_rig.Recording)
                    {
                        _rig.SpySaw.Add((_rig.Frame, tick));
                    }
                }
            }
        }
    }

    /// <summary>The host client's mirror: records what MirrorDrain applies and at which client tick.</summary>
    private sealed class Mirror : IMirrorConsumer
    {
        private readonly Rig _rig;

        public Mirror(Rig rig) => _rig = rig;

        public long Replies;
        public long BusyReplies;
        public long Entries;

        public void OnEntry(uint tick, in EventEntry entry)
        {
            if (entry.Kind == FixtureEvent)
            {
                Entries++;
                if (_rig.Recording)
                {
                    _rig.EntrySeen.Add((_rig.Frame, _rig.Session.Client.ClientTick, tick));
                }
            }
        }

        public void OnReply(uint tick, in Reply reply)
        {
            Replies++;
            BusyReplies += reply.Reason == CoreReason.Busy ? 1 : 0;
            if (_rig.Recording)
            {
                _rig.ReplySeen.Add((_rig.Frame, _rig.Session.Client.ClientTick, tick, reply.Result));
            }
        }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly List<string> Log = new();
        public readonly List<bool> FirstFlags = new();
        public readonly List<(uint Tick, uint WorldTick)> WorldTickAtFlush = new();
        public readonly List<(uint Tick, ActorId Actor)> Applied = new();
        public readonly List<(uint Tick, OpKind Kind, ActorId Actor, ReplyResult Result)> Drained = new();
        public readonly List<(int Frame, uint ClientTick)> SentInStep = new();
        public readonly List<(int Frame, uint HostTick)> SpySaw = new();
        public readonly List<(int Frame, uint ClientTick, uint HostTick)> EntrySeen = new();
        public readonly List<(int Frame, uint ClientTick, uint HostTick, ReplyResult Result)> ReplySeen = new();
        public readonly SpySink Spy;
        public readonly Mirror Mirror;
        public readonly HostSession Session;
        public bool Recording = true;
        public bool SendInFirstStep;
        public int Frame;

        public Rig(bool recording = true)
        {
            Recording = recording;
            Spy = new SpySink(this);
            Mirror = new Mirror(this);
            SimProfile profile = SimProfile.ForTest("host-loopback", null, new SimSystemEntry(SystemId.GridInventory, ctx => new FixtureSystem(ctx.Events, this)));
            var args = new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 7);
            var options = new HostSessionOptions
            {
                ConfigureClient = b => b.Bind(ClientStepId.CarrierStep, new ClientProbe(this)),
                WrapTransport = inner => new Transport(this, inner),
            };
            Session = new HostSession(
                args,
                Clock,
                new IOpCodec[] { new U32Codec(OpCodecId) },
                new IEventCodec[] { new FixedLengthEventCodec(EventCodecId, 4) },
                options);
            Session.AddEventSink(Spy);
            Session.MirrorDrain.Register(Mirror);
        }

        /// <summary>One frame: advance the fake clock by <paramref name="dt"/> counts and run it.</summary>
        public void RunFrame(long dt)
        {
            Frame++;
            Clock.Now += dt;
            Session.RunFrame();
        }

        public ushort Send() => Session.ClientAdapter.Send(FixtureOp, _payload);

        public void AssertClocksAgree()
        {
            uint tick = Session.World.CurrentTick;
            Assert.Equal(tick, Session.HostClock.EstimatedHostTick);
            Assert.True(Session.ClientAdapter.Clock.IsValid);
            Assert.Equal(tick, Session.ClientAdapter.Clock.EstimatedHostTick);
        }
    }

    // ------------------------------------------------------------ AC-1

    [Fact]
    [Trait("Id", "SC-R2-1")]
    public void Ac1_ThreeTicks_LogIsPumpThenClientSimFlushPerTick_EdgesOnlyAtFirst_FlushAfterEachTick()
    {
        var rig = new Rig();
        rig.Log.Clear();

        rig.RunFrame(ThreeTicks);

        Assert.Equal(
            new[] { "Pump", "Client", "Sim", "Flush", "Client", "Sim", "Flush", "Client", "Sim", "Flush" },
            rig.Log.ToArray());
        Assert.Equal(new[] { true, false, false }, rig.FirstFlags.ToArray());
        Assert.Equal(3u, rig.Session.World.CurrentTick);
        // Flush(t) runs after Tick() returned: the world is already at t + 1; the last flush follows the last Tick.
        Assert.Equal(new[] { (0u, 1u), (1u, 2u), (2u, 3u) }, rig.WorldTickAtFlush.ToArray());
        rig.AssertClocksAgree();
    }

    // ------------------------------------------------------------ AC-2

    [Fact]
    [Trait("Id", "SC-R2-1")]
    public void Ac2_LocalJoin_AppliesOnTickZero_AndLaterRequestsAreStampedWithThePlayerId()
    {
        var rig = new Rig();
        Assert.Equal(PeerState.Joining, rig.Session.HostAdapter.GetPeerState(PeerOrigin.Loopback, rig.Session.LocalPeer));
        Assert.True(rig.Session.LocalPlayerId.IsNone);
        Assert.False(rig.Session.IsLocalJoined);

        rig.RunFrame(OneTick); // exactly tick 0

        Assert.Equal(1u, rig.Session.World.CurrentTick);
        Assert.Equal(PeerState.Admitted, rig.Session.HostAdapter.GetPeerState(PeerOrigin.Loopback, rig.Session.LocalPeer));
        EntityId player = rig.Session.LocalPlayerId;
        Assert.False(player.IsNone);
        Assert.True(rig.Session.HostAdapter.TryGetPeer(PeerOrigin.Loopback, rig.Session.LocalPeer, out _, out PlayerSlot slot));
        Assert.Equal((byte)0, slot.Value);
        Assert.True(rig.Session.IsLocalJoined);
        (uint joinTick, OpKind joinKind, ActorId joinActor, ReplyResult joinResult) = Assert.Single(rig.Drained);
        Assert.Equal(0u, joinTick);
        Assert.Equal(OpKind.PlayerJoin, joinKind);
        Assert.True(joinActor.IsSystem);
        Assert.Equal(ReplyResult.Ok, joinResult);

        // TD-062 item 8: the handler sees the stamped actor after a drain (never a payload identity).
        rig.Send();
        rig.RunFrame(OneTick);
        (uint tick, ActorId actor) = Assert.Single(rig.Applied);
        Assert.Equal(1u, tick);
        Assert.Equal(ActorId.FromEntity(player), actor);
        Assert.False(actor.IsSystem);
    }

    // ------------------------------------------------------------ AC-3

    [Fact]
    [Trait("Id", "SC-X-2")]
    public void Ac3_RequestAppliesAtTheFirstInputsApply_SpySeesEventsThatFrame_ReplyAtExactlyTheNextClientTick()
    {
        var rig = new Rig();
        rig.RunFrame(OneTick); // frame 1: tick 0, join
        rig.AssertClocksAgree();

        rig.Send();            // frame 2: sent before the frame, enqueued by its PumpInbound
        int sendFrame = rig.Frame + 1;
        rig.RunFrame(OneTick); // tick 1 applies it

        Assert.Equal(sendFrame, rig.Frame);
        (uint appliedTick, _) = Assert.Single(rig.Applied);
        Assert.Equal(1u, appliedTick);
        // Host Presentation stand-in saw the event in the frame that ran that tick.
        Assert.Equal((sendFrame, 1u), Assert.Single(rig.SpySaw));
        // Nothing reached the host ClientLayer in that frame: its next MirrorDrain has not run.
        Assert.Empty(rig.ReplySeen);
        Assert.Empty(rig.EntrySeen);
        rig.AssertClocksAgree();
        uint clientTickAfterSendFrame = rig.Session.Client.ClientTick;

        rig.RunFrame(OneTick); // the next client tick: MirrorDrain applies reply and event

        (int replyFrame, uint replyClientTick, uint replyHostTick, ReplyResult result) = Assert.Single(rig.ReplySeen);
        Assert.Equal(sendFrame + 1, replyFrame);
        Assert.Equal(clientTickAfterSendFrame, replyClientTick); // the very next Step
        Assert.Equal(1u, replyHostTick);
        Assert.Equal(ReplyResult.Ok, result);
        (int entryFrame, uint entryClientTick, uint entryHostTick) = Assert.Single(rig.EntrySeen);
        Assert.Equal(replyFrame, entryFrame);
        Assert.Equal(replyClientTick, entryClientTick);
        Assert.Equal(1u, entryHostTick);
        Assert.Equal(0, rig.Session.ClientAdapter.PendingCount);
        Assert.Equal(1, rig.Spy.Fixture);
        rig.AssertClocksAgree();
    }

    [Fact]
    [Trait("Id", "SC-X-2")]
    public void Ac3_WithThreeTicksInTheFrame_TheReplyIsAppliedByTheNextTicksStepInTheSameFrame()
    {
        var rig = new Rig();
        rig.RunFrame(OneTick);
        rig.Send();
        uint clientTickBefore = rig.Session.Client.ClientTick;

        rig.RunFrame(ThreeTicks); // ticks 1, 2, 3: the op applies at tick 1; Step for tick 2 drains the reply

        (int frame, uint clientTick, uint hostTick, _) = Assert.Single(rig.ReplySeen);
        Assert.Equal(rig.Frame, frame);
        Assert.Equal(clientTickBefore + 1, clientTick); // step i = 1 of this frame
        Assert.Equal(1u, hostTick);
        Assert.Equal((rig.Frame, 1u), Assert.Single(rig.SpySaw));
        rig.AssertClocksAgree();
    }

    [Fact]
    [Trait("Id", "SC-X-2")]
    public void Ac3_BothClocksEqualCurrentTickBetweenFrames_IncludingQuietTicksAndZeroTickFrames()
    {
        var rig = new Rig();
        long[] steps = { OneTick, ZeroTicks, OneTick, OneTick, ThreeTicks, ZeroTicks, OneTick };
        foreach (long dt in steps)
        {
            rig.RunFrame(dt);
            rig.AssertClocksAgree();
        }

        Assert.True(rig.Session.World.CurrentTick >= 6u);
        Assert.True(rig.Session.HostAdapter.HeartbeatsSent > 0);
    }

    [Fact]
    [Trait("Id", "SC-X-2")]
    public void Ac3_RequestSentFromInsideAClientStep_AppliesAtTheFirstTickOfTheNextFrame_ReplyAtTheStepAfterThatTick()
    {
        var rig = new Rig();
        rig.RunFrame(OneTick); // join (tick 0)
        rig.SendInFirstStep = true;

        rig.RunFrame(ThreeTicks); // frame F: ticks 1-3; the request is sent at step i = 0, after that frame's PumpInbound
        int frameF = rig.Frame;

        Assert.Equal(frameF, Assert.Single(rig.SentInStep).Frame);
        Assert.Empty(rig.Applied);   // HostAdapter.PumpInbound runs at frame start only (TR-sim-008)
        Assert.Empty(rig.SpySaw);
        Assert.Equal(4u, rig.Session.World.CurrentTick);
        rig.AssertClocksAgree();
        uint clientTickAtF1 = rig.Session.Client.ClientTick;

        rig.RunFrame(ThreeTicks); // frame F + 1: ticks 4-6

        (uint appliedTick, _) = Assert.Single(rig.Applied);
        Assert.Equal(4u, appliedTick);                            // the first tick of F + 1
        Assert.Equal((frameF + 1, 4u), Assert.Single(rig.SpySaw)); // presentation sees it in F + 1
        (int replyFrame, uint replyClientTick, uint replyHostTick, ReplyResult result) = Assert.Single(rig.ReplySeen);
        Assert.Equal(frameF + 1, replyFrame);
        Assert.Equal(clientTickAtF1 + 1, replyClientTick);        // MirrorDrain of the step after tick 4 (i = 1)
        Assert.Equal(4u, replyHostTick);
        Assert.Equal(ReplyResult.Ok, result);
        rig.AssertClocksAgree();
    }

    // ------------------------------------------------------------ in-flight release (TD-065 item 1)

    [Fact]
    [Trait("Id", "SC-X-2")]
    public void SequentialRequests_PastTheInFlightCap_NeverGoBusy_BecauseTheDeferredSinkReleasesEveryReqId()
    {
        var rig = new Rig();
        rig.RunFrame(OneTick);
        ActorId player = ActorId.FromEntity(rig.Session.LocalPlayerId);

        for (int i = 0; i < 150; i++)
        {
            rig.Send();
            rig.RunFrame(OneTick);
        }

        rig.RunFrame(OneTick);
        Assert.Equal(150L, rig.Mirror.Replies);
        Assert.Equal(0L, rig.Mirror.BusyReplies);
        Assert.Equal(0L, rig.Session.HostAdapter.BusyReplyCount);
        Assert.Equal(150L, rig.Spy.Fixture);
        Assert.Equal(150L, rig.Mirror.Entries);
        Assert.Equal(0, rig.Session.ClientAdapter.PendingCount);
        Assert.Equal(0L, rig.Session.ClientAdapter.IgnoredReplyCount);
        Assert.Equal(150, rig.Applied.Count);
        Assert.All(rig.Applied, a => Assert.Equal(player, a.Actor)); // every request stamped with the local player, past the 64 cap
        Assert.Equal(rig.Applied[0].Tick + 149u, rig.Applied[149].Tick);
    }

    [Fact]
    [Trait("Id", "SC-X-2")]
    public void RequestsSentBeforeTheJoinCompletes_AreDroppedUnadmitted_NotStamped()
    {
        var rig = new Rig();
        rig.Send(); // the local peer is still Joining: the host drops it (no identity yet)

        rig.RunFrame(OneTick);

        Assert.Empty(rig.Applied);
        Assert.Equal(1L, rig.Session.HostAdapter.UnadmittedDropCount);
        Assert.True(rig.Session.IsLocalJoined);
        // Dropped unanswered: its pending slot stays held (TD-068 item 2) - why callers wait for IsLocalJoined.
        Assert.Equal(1, rig.Session.ClientAdapter.PendingCount);
        rig.RunFrame(OneTick);
        Assert.Equal(1, rig.Session.ClientAdapter.PendingCount);
        Assert.Empty(rig.ReplySeen);
    }

    // ------------------------------------------------------------ construction

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        SimProfile profile = SimProfile.ForTest("x", null, new SimSystemEntry(SystemId.GridInventory, ctx => new FixtureSystem(ctx.Events, new Rig(false))));
        var args = new SimBuildArgs(profile, BuiltInContent.CreateM1a(), 7);
        Assert.Throws<ArgumentNullException>(() => new HostSession(args, null!));
    }

    [Fact]
    public void DeferredEventSink_FansOutInAttachOrder_AndIgnoresDeliveriesBeforeAnyTarget()
    {
        var relay = new DeferredEventSink();
        var order = new List<int>();
        relay.Deliver(0, ReadOnlySpan<EventEntry>.Empty, default);
        relay.Attach(new OrderSink(order, 1));
        relay.Attach(new OrderSink(order, 2));
        relay.Deliver(1, ReadOnlySpan<EventEntry>.Empty, default);

        Assert.Equal(2, relay.TargetCount);
        Assert.Equal(new[] { 1, 2 }, order.ToArray());
        Assert.Throws<ArgumentNullException>(() => relay.Attach(null!));
        Assert.Throws<ArgumentException>(() => relay.Attach(relay));
    }

    private sealed class OrderSink : IEventSink
    {
        private readonly List<int> _order;
        private readonly int _id;

        public OrderSink(List<int> order, int id)
        {
            _order = order;
            _id = id;
        }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow) => _order.Add(_id);
    }

    // ------------------------------------------------------------ guardrail (advisory)

    [Fact]
    public void RunFrame_OverLoopback_AllocatesZeroBytesAfterWarmUp_QuietAndWithARequestPerFrame()
    {
        var rig = new Rig(recording: false);
        rig.RunFrame(OneTick); // join

        AllocAssert.Zero("quiet RunFrame", () => rig.RunFrame(OneTick), warmUpRuns: 8);
        AllocAssert.Zero(
            "RunFrame with a request, an event and a reply",
            () =>
            {
                rig.Send();
                rig.RunFrame(OneTick);
            },
            warmUpRuns: 8);
    }
}
