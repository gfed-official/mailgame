using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests;

/// <summary>An <see cref="IOpHandler"/> whose body is a delegate, for drain tests.</summary>
internal sealed class LambdaHandler : IOpHandler
{
    /// <summary>The handler body.</summary>
    public delegate Reply ApplyFn(in Op op, in TickContext ctx);

    private readonly ApplyFn _fn;

    public LambdaHandler(OpKind kind, ApplyFn fn)
    {
        Kind = kind;
        _fn = fn;
    }

    public OpKind Kind { get; }

    /// <summary>How many times <see cref="Apply"/> ran.</summary>
    public int Calls { get; private set; }

    public Reply Apply(in Op op, in TickContext ctx)
    {
        Calls++;
        return _fn(in op, in ctx);
    }
}

/// <summary>Recorder stub that remembers every drained op.</summary>
internal sealed class RecorderStub : IOpRecorder
{
    public List<(uint Tick, Op Op, Reply Reply)> Drained { get; } = new();

    public int Other { get; private set; }

    public void OnEnqueued(uint boundaryTick, in Op op) => Other++;

    public void OnDrained(uint applyTick, in Op op, in Reply reply) => Drained.Add((applyTick, op, reply));

    public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample) => Other++;

    public void OnOverflow(uint tick) => Other++;
}

/// <summary>Event sink that keeps a copy of every tick's log.</summary>
internal sealed class CaptureSink : IEventSink
{
    public List<EventEntry[]> Ticks { get; } = new();

    public EventEntry[] Last => Ticks[^1];

    public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow) => Ticks.Add(entries.ToArray());
}

/// <summary>
/// One world with every Sim Core state source bound (ids, mail ids, RNG with one live stream, slot table with two players),
/// a recorder stub and a capture sink. Shared by the drain tests and <see cref="RejectHarness"/>.
/// </summary>
internal sealed class DrainRig
{
    /// <summary>Player in slot 0.</summary>
    public static readonly ActorId PlayerA = new(0x0100_0005u);

    /// <summary>Player in slot 3.</summary>
    public static readonly ActorId PlayerB = new(0x0100_0009u);

    /// <summary>Not in the slot table.</summary>
    public static readonly ActorId Stranger = new(0x0100_0077u);

    /// <summary>A kind nobody registered.</summary>
    public const OpKind UnregisteredKind = (OpKind)0x00F0;

    /// <summary>Test event kind (Replicated, 4 B payload).</summary>
    public const EventKind TestEvent = (EventKind)0x0F01;

    public DrainRig(Action<SimWorldBuilder>? configure = null, int maxDrain = 64, uint lastOpSeq = 0, int opCapacity = 4096, bool instrument = true)
    {
        Counters = new SimCounters();
        var ring = new SimCheckRing();
        var holder = new Forwarding();
        Ids = new EntityIds(holder, ring, Counters, allowTestClasses: true);
        Mail = new MailIds(holder, ring, Counters);
        Rng = new RngRegistry(1, Counters);
        Slots = new SlotTable();
        Slots.Assign(PlayerSlot.FromValue(0), new EntityId(PlayerA.Value), 0);
        Slots.Assign(PlayerSlot.FromValue(3), new EntityId(PlayerB.Value), 0);
        Stream = Rng.Create(RngNames.Mail, 1);
        Recorder = new RecorderStub();
        Sink = new CaptureSink();
        var settings = new SimCoreSettings { MaxOpsDrainedPerTick = maxDrain, OpQueueCapacity = opCapacity, SystemOpReserve = 8 };
        var builder = new SimWorldBuilder(settings, Counters, ring, initialCounters: new SimInitialCounters { LastOpSeq = lastOpSeq });
        builder.BindCoreState(Ids, Mail, Rng, Slots);
        if (instrument)
        {
            // Off for allocation tests: the stub recorder and capture sink allocate by design.
            builder.BindRecorder(Recorder);
            builder.BindEventSink(Sink);
        }

        builder.BindEventKind(EventDescriptor.For<uint>(TestEvent, ReplicationClass.Replicated, false, 1, 0, 4));
        configure?.Invoke(builder);
        World = builder.Freeze();
        holder.Target = World;
    }

    public SimCounters Counters { get; }

    public EntityIds Ids { get; }

    public MailIds Mail { get; }

    public RngRegistry Rng { get; }

    public SlotTable Slots { get; }

    public RngStream Stream { get; }

    public RecorderStub Recorder { get; }

    public CaptureSink Sink { get; }

    public SimWorld World { get; }

    /// <summary>Raises <see cref="TestEvent"/> in the current scope.</summary>
    public void Raise(uint value) => World.Events.TryRaise(TestEvent, 0xFF, in value);

    /// <summary>Last-issued id counters of every entity class, then the mail counter, then every live stream's draws.</summary>
    public ulong[] Snapshot()
    {
        var list = new List<ulong>();
        for (int c = 0; c < 256; c++)
        {
            list.Add(Ids.Peek((EntityClass)c));
        }

        list.Add(Mail.Peek());
        var scratch = new RngStream[Rng.HandleCount];
        int live = Rng.CopyLive(scratch);
        for (int i = 0; i < live; i++)
        {
            list.Add(scratch[i].Draws);
        }

        return list.ToArray();
    }

    private sealed class Forwarding : ITickReader, IWorldStateReader
    {
        public SimWorld? Target { get; set; }

        public uint CurrentTick => Target!.CurrentTick;

        public TickStage Stage => Target!.Stage;

        public PhaseId Phase => Target!.Phase;

        public SimState State => Target!.State;
    }
}

/// <summary>
/// The SC-R8-2 harness (ADR-0007 "SC-R8-2 harness per handler"), reusable by every system's handler stories. <see cref="Run"/>
/// enqueues one op against a rig, drains it, and asserts that a rejection changed nothing: the canonical dump hash equals the
/// hash of a twin world that never saw the op (twin starts with <c>opSeq</c> already at 1, because the enqueue consumed one),
/// every id allocator and every stream's draws are as before, the tick's only log entry is a Rejected <c>OpReply</c> with the
/// expected reason, and the recorder saw <c>OnDrained</c> exactly once with the apply tick and that reply.
/// </summary>
/// <example>
/// <code>
/// RejectHarness.Run((b, rig) => b.BindOp(descriptor, new MyHandler(rig)), op, DrainRig.PlayerA, expectedReason: 64);
/// </code>
/// </example>
internal static class RejectHarness
{
    /// <summary>Runs the harness; throws an xUnit exception when the op mutated anything or the reply is wrong.</summary>
    /// <param name="bind">Binds the descriptor and handler under test; called once per world (the rig and its untouched twin) with a getter for the world being built, so a handler can reach its own rig.</param>
    /// <param name="op">The op to reject.</param>
    /// <param name="actor">Who issues it.</param>
    /// <param name="expectedReason">The reason the reply must carry.</param>
    /// <returns>The rig that drained the op.</returns>
    public static DrainRig Run(Action<SimWorldBuilder, Func<DrainRig>> bind, in Op op, ActorId actor, ushort expectedReason)
    {
        DrainRig? rigRef = null;
        DrainRig? twinRef = null;
        var rig = rigRef = new DrainRig(b => bind(b, () => rigRef!));
        var twin = twinRef = new DrainRig(b => bind(b, () => twinRef!), lastOpSeq: 1);
        ulong[] before = rig.Snapshot();
        Assert.True(rig.World.EnqueueOp(actor, in op));

        rig.World.Tick();
        twin.World.Tick();

        Assert.Equal(before, rig.Snapshot());
        Assert.Equal(DumpHash.Sha256(twin.World), DumpHash.Sha256(rig.World));
        EventEntry only = Assert.Single(rig.Sink.Last);
        Assert.Equal(EventKind.OpReply, only.Kind);
        Reply reply = only.PayloadAs<Reply>();
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal(expectedReason, reply.Reason);
        Assert.Equal(op.ReqId, reply.ReqId);
        (uint tick, Op drained, Reply recorded) = Assert.Single(rig.Recorder.Drained);
        Assert.Equal(0u, tick);
        Assert.Equal(op.ReqId, drained.ReqId);
        Assert.Equal(reply.Reason, recorded.Reason);
        Assert.Equal(reply.Result, recorded.Result);
        return rig;
    }
}
