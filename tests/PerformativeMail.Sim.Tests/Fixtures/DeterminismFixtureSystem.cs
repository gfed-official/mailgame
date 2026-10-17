using System.Runtime.InteropServices;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests;

/// <summary>Payload of the determinism fixture's two ops (8 B, padding-free).</summary>
/// <example><code>var op = new Op { Kind = DeterminismFixture.PlayerOpKind }; op.SetPayload(new DeterminismPayload { Verb = DeterminismFixture.VerbSet, Key = 7, Arg = 100 });</code></example>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct DeterminismPayload
{
    /// <summary>One of the <c>DeterminismFixture.Verb*</c> constants.</summary>
    public byte Verb;

    /// <summary>Explicit padding.</summary>
    public byte Pad0;

    /// <summary>Key of the state-table row the op touches.</summary>
    public ushort Key;

    /// <summary>Verb argument (value to store, amount to add, or RNG bound).</summary>
    public int Arg;
}

/// <summary>
/// Counters a <see cref="DeterminismFixtureSystem"/> keeps about what it did, so a test can prove the log it recorded really
/// exercised each part of the system (accepted and rejected ops, id allocations, RNG draws, events, folded samples).
/// </summary>
/// <example><code>Assert.True(fixture.System!.Stats.RngDraws &gt; 0);</code></example>
internal sealed class DeterminismStats
{
    /// <summary>Gets ops the handlers accepted.</summary>
    public long Accepted { get; internal set; }

    /// <summary>Gets ops the handlers rejected (ops the core rejects before a handler runs are not counted here).</summary>
    public long Rejected { get; internal set; }

    /// <summary>Gets entity ids allocated (class <see cref="EntityClass.WorldItem"/>).</summary>
    public long Allocations { get; internal set; }

    /// <summary>Gets RNG draws taken from the fixture's stream.</summary>
    public long RngDraws { get; internal set; }

    /// <summary>Gets events raised.</summary>
    public long EventsRaised { get; internal set; }

    /// <summary>Gets samples folded into the fixture's state in phase 3.</summary>
    public long SamplesFolded { get; internal set; }
}

/// <summary>
/// One world's worth of the SC-DET-1 fixture: the per-test <see cref="SimProfile"/> that composes a
/// <see cref="DeterminismFixtureSystem"/>, and the slot the test fills with the built world (the system reads the sample latch
/// through it). Create one per world; nothing here is static, so two worlds in one process never share state.
/// </summary>
/// <remarks>
/// Op kinds: ADR-0007's block table reserves no test range and shipped enum members must not be added, so the fixture reuses
/// two existing kinds the way the replay tests do: <see cref="PlayerOpKind"/> (<see cref="OpKind.TeleportAck"/>, any present
/// player) and <see cref="SystemOpKind"/> (<see cref="OpKind.FreezePlayer"/>, System-only). The fixture is composed as
/// <see cref="SystemId.EnemyAiWaves"/> so that its owner-filtered RNG view can create the real v1 <c>waves</c> stream;
/// <c>RngNames</c> gains no test name. Entity ids are allocated in the shipped <see cref="EntityClass.WorldItem"/> class (the
/// composition's allocator is built without test classes).
/// </remarks>
/// <example>
/// <code>
/// var f = new DeterminismFixture();
/// SimHost host = SimComposition.Build(new SimBuildArgs(f.Profile, content, runSeed));
/// f.World = host.World;
/// </code>
/// </example>
internal sealed class DeterminismFixture
{
    /// <summary>Verb of <see cref="PlayerOpKind"/>: upsert <c>Key</c> with <c>Value = Arg</c>; rejected when <c>Arg &lt; 0</c>.</summary>
    public const byte VerbSet = 0;

    /// <summary>Verb of <see cref="PlayerOpKind"/>: add <c>Arg</c> to an existing key; rejected when the key is absent.</summary>
    public const byte VerbAdd = 1;

    /// <summary>Verb of <see cref="PlayerOpKind"/>: remove an existing key; rejected when the key is absent.</summary>
    public const byte VerbRemove = 2;

    /// <summary>Verb of <see cref="PlayerOpKind"/>: draw <c>NextInt(Arg)</c> from the <c>waves</c> stream into the key's value (upserting); rejected when <c>Arg == 0</c>.</summary>
    public const byte VerbDraw = 3;

    /// <summary>Verb of <see cref="SystemOpKind"/>: allocate a world-item id and upsert it at <c>Key</c>.</summary>
    public const byte VerbAlloc = 0;

    /// <summary>Verb of <see cref="SystemOpKind"/>: bump the fixture's generation counter and raise an event.</summary>
    public const byte VerbBump = 1;

    /// <summary>The player-issued op kind (reuses a shipped kind; see the type remarks).</summary>
    public const OpKind PlayerOpKind = OpKind.TeleportAck;

    /// <summary>The System-only op kind (reuses a shipped kind; see the type remarks).</summary>
    public const OpKind SystemOpKind = OpKind.FreezePlayer;

    /// <summary>The event the fixture raises (payload: the touched key, u32).</summary>
    public const EventKind FixtureEvent = (EventKind)0x0F03;

    /// <summary>Creates a fixture with its own per-test profile.</summary>
    public DeterminismFixture()
    {
        Profile = SimProfile.ForTest(
            "determinism",
            null,
            new SimSystemEntry(SystemId.EnemyAiWaves, ctx =>
            {
                System = new DeterminismFixtureSystem(this, ctx.Events, ctx.EntityIds, ctx.Rng);
                return System;
            }));
    }

    /// <summary>Gets the per-test profile; compose it once per world.</summary>
    public SimProfile Profile { get; }

    /// <summary>Gets or sets the built world; set it right after <c>SimComposition.Build</c>, before the first tick.</summary>
    public SimWorld? World { get; set; }

    /// <summary>Gets the system the composition built (null before the build).</summary>
    public DeterminismFixtureSystem? System { get; private set; }
}

/// <summary>
/// The system the SC-DET-1 harness composes. It registers two ops (see <see cref="DeterminismFixture"/>), validates then mutates a
/// small keyed state table (an array kept in ascending key order; never a hashed collection), allocates entity ids, draws from
/// the real v1 <c>waves</c> RNG stream (created lazily at the first draw, so the stream only enters the dump, and the run seed
/// only influences state, from the first draw on), raises events, reads the sample latch in phase 3 and folds the samples into
/// its state, and writes all of it to the canonical dump in ascending key order.
/// </summary>
/// <remarks>Not thread-safe: one instance per world, touched only by that world's tick thread.</remarks>
/// <example><code>// Built through DeterminismFixture.Profile; ops go through SimWorld.EnqueueOp.</code></example>
internal sealed class DeterminismFixtureSystem : ISimSystem, ISimStateContributor
{
    private const int MaxRows = 512;

    private readonly DeterminismFixture _owner;
    private readonly EventLogFacade _events;
    private readonly EntityIds _ids;
    private readonly IRngStreams _rngView;
    private readonly Row[] _rows = new Row[MaxRows];
    private int _count;
    private RngStream? _rng;
    private uint _generation;
    private long _sampleFold;
    private uint _samplesSeen;

    /// <summary>Creates the system from the context's services.</summary>
    /// <param name="owner">The per-world fixture (gives access to the built world).</param>
    /// <param name="events">The event facade.</param>
    /// <param name="ids">The entity id allocator.</param>
    /// <param name="rng">The owner-filtered RNG view of <see cref="SystemId.EnemyAiWaves"/>.</param>
    public DeterminismFixtureSystem(DeterminismFixture owner, EventLogFacade events, EntityIds ids, IRngStreams rng)
    {
        _owner = owner;
        _events = events;
        _ids = ids;
        _rngView = rng;
    }

    /// <summary>Gets what this system has done so far.</summary>
    public DeterminismStats Stats { get; } = new();

    /// <summary>Gets the dump section id (test range, unique within the per-test profile).</summary>
    public ushort SectionId => 0x7102;

    /// <summary>Gets the number of live rows.</summary>
    public int RowCount => _count;

    /// <inheritdoc />
    public void Bind(SimWorldBuilder builder)
    {
        builder.BindEventKind(EventDescriptor.For<uint>(DeterminismFixture.FixtureEvent, ReplicationClass.Replicated, false, 2, 0, 4));
        builder.BindOp(OpDescriptor.For<DeterminismPayload>(DeterminismFixture.PlayerOpKind, false, 0, 0), new PlayerOps(this));
        builder.BindOp(OpDescriptor.For<DeterminismPayload>(DeterminismFixture.SystemOpKind, true, 0, 0), new SystemOps(this));
        builder.Bind(PhaseId.PlayersStep, new SampleFold(this));
        builder.BindStateContributor(this);
    }

    /// <inheritdoc />
    public void Write(ref CanonicalWriter w)
    {
        w.WriteU32((uint)_count);
        for (int i = 0; i < _count; i++)
        {
            ref readonly Row r = ref _rows[i];
            w.WriteU16(r.Key);
            w.WriteI64(r.Value);
            w.WriteU32(r.Id);
            w.WriteU32(r.Touched);
        }

        w.WriteU32(_generation);
        w.WriteI64(_sampleFold);
        w.WriteU32(_samplesSeen);
        w.WriteU64(_rng?.Draws ?? 0UL);
    }

    // Binary search over the ascending key array; returns the index, or the bitwise complement of the insertion point.
    private int Find(ushort key)
    {
        int lo = 0;
        int hi = _count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            ushort k = _rows[mid].Key;
            if (k == key)
            {
                return mid;
            }

            if (k < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    // Returns the row index for the key, inserting a zeroed row in key order when absent; -1 when the table is full.
    private int Upsert(ushort key)
    {
        int at = Find(key);
        if (at >= 0)
        {
            return at;
        }

        if (_count == MaxRows)
        {
            return -1;
        }

        at = ~at;
        Array.Copy(_rows, at, _rows, at + 1, _count - at);
        _rows[at] = new Row { Key = key };
        _count++;
        return at;
    }

    private void Raise(uint payload)
    {
        if (_events.TryRaise(DeterminismFixture.FixtureEvent, 0xFF, in payload))
        {
            Stats.EventsRaised++;
        }
    }

    private Reply Reject(in Op op)
    {
        Stats.Rejected++;
        return Reply.Reject(in op, CoreReason.BadRequest);
    }

    private Reply Accept(in Op op, uint extra)
    {
        Stats.Accepted++;
        Raise(op.ReqId);
        return Reply.Ok(in op, extra);
    }

    private Reply ApplyPlayer(in Op op, uint tick)
    {
        DeterminismPayload p = op.PayloadAs<DeterminismPayload>();
        switch (p.Verb)
        {
            case DeterminismFixture.VerbSet:
            {
                if (p.Arg < 0)
                {
                    return Reject(in op);
                }

                int at = Upsert(p.Key);
                if (at < 0)
                {
                    return Reject(in op);
                }

                _rows[at].Value = p.Arg;
                _rows[at].Touched = tick;
                return Accept(in op, (uint)p.Arg);
            }

            case DeterminismFixture.VerbAdd:
            {
                int at = Find(p.Key);
                if (at < 0)
                {
                    return Reject(in op);
                }

                _rows[at].Value += p.Arg;
                _rows[at].Touched = tick;
                return Accept(in op, (uint)_rows[at].Value);
            }

            case DeterminismFixture.VerbRemove:
            {
                int at = Find(p.Key);
                if (at < 0)
                {
                    return Reject(in op);
                }

                Array.Copy(_rows, at + 1, _rows, at, _count - at - 1);
                _count--;
                return Accept(in op, p.Key);
            }

            case DeterminismFixture.VerbDraw:
            {
                if (p.Arg <= 0)
                {
                    return Reject(in op);
                }

                int at = Upsert(p.Key);
                if (at < 0)
                {
                    return Reject(in op);
                }

                _rng ??= _rngView.Create(in RngNames.Waves, 0);
                uint roll = _rng.NextInt((uint)p.Arg);
                Stats.RngDraws++;
                _rows[at].Value += roll + 1;
                _rows[at].Touched = tick;
                return Accept(in op, roll);
            }

            default:
                return Reject(in op);
        }
    }

    private Reply ApplySystem(in Op op, uint tick)
    {
        DeterminismPayload p = op.PayloadAs<DeterminismPayload>();
        switch (p.Verb)
        {
            case DeterminismFixture.VerbAlloc:
            {
                int at = Upsert(p.Key);
                if (at < 0)
                {
                    return Reject(in op);
                }

                EntityId id = _ids.Allocate(EntityClass.WorldItem);
                Stats.Allocations++;
                _rows[at].Id = id.Value;
                _rows[at].Touched = tick;
                return Accept(in op, id.Value);
            }

            case DeterminismFixture.VerbBump:
                _generation++;
                return Accept(in op, _generation);

            default:
                return Reject(in op);
        }
    }

    private void FoldSamples()
    {
        ISampleLatch latch = _owner.World!.Samples;
        for (int i = 0; i < latch.Count; i++)
        {
            ref readonly TransformSample s = ref latch[i];
            unchecked
            {
                _sampleFold = (_sampleFold * 1_000_003L) + (s.Seq * 31L) + s.PosXMm + (s.PosZMm * 7L) + s.Slot + latch.ReceivedTick;
            }

            _samplesSeen++;
            Stats.SamplesFolded++;
        }
    }

    private struct Row
    {
        public ushort Key;
        public long Value;
        public uint Id;
        public uint Touched;
    }

    private sealed class PlayerOps : IOpHandler
    {
        private readonly DeterminismFixtureSystem _s;

        public PlayerOps(DeterminismFixtureSystem s) => _s = s;

        public OpKind Kind => DeterminismFixture.PlayerOpKind;

        public Reply Apply(in Op op, in TickContext ctx) => _s.ApplyPlayer(in op, ctx.Tick);
    }

    private sealed class SystemOps : IOpHandler
    {
        private readonly DeterminismFixtureSystem _s;

        public SystemOps(DeterminismFixtureSystem s) => _s = s;

        public OpKind Kind => DeterminismFixture.SystemOpKind;

        public Reply Apply(in Op op, in TickContext ctx) => _s.ApplySystem(in op, ctx.Tick);
    }

    private sealed class SampleFold : ISimPhase
    {
        private readonly DeterminismFixtureSystem _s;

        public SampleFold(DeterminismFixtureSystem s) => _s = s;

        public void Run(in TickContext ctx) => _s.FoldSamples();
    }
}
