using System.Collections.Immutable;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Core.Replay;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// The system the replay tests compose: one op kind (<see cref="OpKind.TeleportAck"/>: ReqId divisible by 5 is rejected, else
/// the tick-weighted sum of ReqIds grows), a dump section holding that state (so dump hashes vary with what was applied), a
/// phase-3-style binder that can inject an op from inside the tick (live session only) and raise a burst of events (to
/// overflow a small log), and an optional stop request at a chosen tick.
/// </summary>
internal sealed class ReplayFixture
{
    public const EventKind Ev = (EventKind)0x0F02;
    public const OpKind Kind = OpKind.TeleportAck;
    public static readonly ActorId Stranger = new(0x0100_0077u);

    private ContentSet? _content;

    public ReplayFixture()
    {
        Profile = SimProfile.ForTest(
            "replay",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx =>
            {
                Constructions++;
                return new ReplaySystem(this, ctx.Events);
            }),
            new SimSystemEntry(SystemId.RunStructure, ctx =>
            {
                Constructions++;
                return new StopSystem(this, ctx.RunControl);
            }));
    }

    public SimProfile Profile { get; }

    /// <summary>How many systems the composition has constructed (two per world built).</summary>
    public int Constructions { get; set; }

    /// <summary>The world of the live session; the injecting binder enqueues through it.</summary>
    public SimWorld? World { get; set; }

    /// <summary>True only while recording: the binder then injects ReqId <see cref="InjectReqId"/> from inside tick <see cref="InjectAtTick"/>.</summary>
    public bool Live { get; set; }

    public uint InjectAtTick { get; set; } = uint.MaxValue;

    public uint InjectReqId { get; set; }

    /// <summary>Deterministic system behaviour (live and replay): raise <see cref="RaiseCount"/> events in tick <see cref="RaiseAtTick"/>.</summary>
    public uint RaiseAtTick { get; set; } = uint.MaxValue;

    public int RaiseCount { get; set; }

    /// <summary>Deterministic system behaviour: request a stop in phase 12 of this tick.</summary>
    public uint StopAtTick { get; set; } = uint.MaxValue;

    public ContentSet Content => _content ??= BuiltInContent.CreateM1a();

    private sealed class ReplaySystem : ISimSystem, ISimStateContributor, IOpHandler
    {
        private readonly ReplayFixture _f;
        private readonly EventLogFacade _events;
        private long _sum;
        private uint _applied;

        public ReplaySystem(ReplayFixture f, EventLogFacade events)
        {
            _f = f;
            _events = events;
        }

        public ushort SectionId => 0x7101;

        public OpKind Kind => ReplayFixture.Kind;

        public void Bind(SimWorldBuilder builder)
        {
            builder.BindEventKind(EventDescriptor.For<uint>(Ev, ReplicationClass.Replicated, false, 2, 0, 4));
            builder.BindOp(OpDescriptor.Empty(ReplayFixture.Kind, false, 0, 0), this);
            builder.Bind(PhaseId.MailStep, new ActionBinder(OnMailStep));
            builder.BindStateContributor(this);
        }

        public Reply Apply(in Op op, in TickContext ctx)
        {
            _applied++;
            if (op.ReqId % 5 == 0)
            {
                return Reply.Reject(in op, CoreReason.BadRequest);
            }

            _sum += (long)op.ReqId * (ctx.Tick + 1);
            return Reply.Ok(in op, (uint)_sum);
        }

        public void Write(ref CanonicalWriter w)
        {
            w.WriteI64(_sum);
            w.WriteU32(_applied);
        }

        private void OnMailStep(uint tick)
        {
            if (_f.Live && tick == _f.InjectAtTick && _f.World is { } world)
            {
                var op = new Op { Kind = ReplayFixture.Kind, ReqId = _f.InjectReqId };
                world.EnqueueOp(ActorId.System, in op);
            }

            if (tick == _f.RaiseAtTick)
            {
                for (uint i = 0; i < _f.RaiseCount; i++)
                {
                    _events.TryRaise(Ev, 0xFF, in i);
                }
            }
        }
    }

    private sealed class StopSystem : ISimSystem
    {
        private readonly ReplayFixture _f;
        private readonly IRunControl _run;

        public StopSystem(ReplayFixture f, IRunControl run)
        {
            _f = f;
            _run = run;
        }

        public void Bind(SimWorldBuilder builder) =>
            builder.Bind(PhaseId.RunStep, new ActionBinder(tick =>
            {
                if (tick == _f.StopAtTick)
                {
                    _run.RequestStop(StopReason.Unspecified);
                }
            }));
    }
}

/// <summary>Forwards to an <see cref="InMemoryRecorder"/> and lets a test act from inside the tick prologue (the sample latch runs before the phase-0 drain).</summary>
internal sealed class PrologueHookRecorder : IOpRecorder
{
    private readonly InMemoryRecorder _inner;

    public PrologueHookRecorder(InMemoryRecorder inner)
    {
        _inner = inner;
    }

    public Action<uint>? OnSampleLatched { get; set; }

    public void OnEnqueued(uint boundaryTick, in Op op) => _inner.OnEnqueued(boundaryTick, in op);

    public void OnDrained(uint applyTick, in Op op, in Reply reply) => _inner.OnDrained(applyTick, in op, in reply);

    public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
    {
        _inner.OnSample(receivedTick, slot, in sample);
        OnSampleLatched?.Invoke(receivedTick);
    }

    public void OnOverflow(uint tick) => _inner.OnOverflow(tick);
}

/// <summary>
/// Story 026, AC-2 and AC-3 (TR-sim-045, TR-sim-018): <see cref="ReplayDriver"/> rebuilds the world from the header, re-enqueues
/// each op before its boundary tick and re-latches each sample at its received tick, reproduces every apply tick (and reply and
/// dump hash), refuses on any header mismatch without building a world, and reports an overflowed recording as non-reproducible
/// instead of failing it.
/// </summary>
public class ReplayDriverTests
{
    private const ulong Seed = 0xC0FFEE;

    private static readonly SimInitialCounters _counters = new()
    {
        LastOpSeq = 100,
        LastMailId = 7,
        LastEntityCounters = ImmutableArray.Create(0u, 4u),
    };

    // ------------------------------------------------------------ helpers

    private sealed record Session(InMemoryRecorder Recorder, SimBuildArgs Args, string FinalDump);

    private static Op Op(uint req, OpKind kind = ReplayFixture.Kind) => new() { Kind = kind, ReqId = req };

    private static void Enq(SimWorld w, uint req, ActorId? actor = null, OpKind kind = ReplayFixture.Kind)
    {
        Op op = Op(req, kind);
        Assert.True(w.EnqueueOp(actor ?? ActorId.System, in op));
    }

    private static void Sample(SimWorld w, byte slot, uint seq, int x)
    {
        var s = new TransformSample { Seq = seq, PosXMm = x, PosZMm = -x };
        Assert.True(w.TryWriteSample(PlayerSlot.FromValue(slot), in s));
    }

    /// <summary>Ten ticks of mixed input: joins, accepted and rejected ops, a 100-op burst that backs up past the drain cap, samples.</summary>
    private static Session Record(ReplayFixture f, ContentSet? content = null, bool dumps = true, ulong seed = Seed)
    {
        content ??= f.Content;
        var args = new SimBuildArgs(f.Profile, content, seed, _counters);
        var rec = new InMemoryRecorder(RecordingHeader.Create(content, seed, _counters));
        SimHost host = SimComposition.Build(new SimBuildArgs(f.Profile, content, seed, _counters) { Recorder = rec });
        SimWorld w = host.World;
        f.World = w;
        if (dumps)
        {
            Assert.True(rec.RecordDump(w));
        }

        for (uint t = 0; t < 10 && w.State != SimState.Stopped; t++)
        {
            Script(w, t);
            w.Tick();
            if (dumps)
            {
                Assert.True(rec.RecordDump(w));
            }

            rec.Flush(w.CurrentTick);
        }

        return new Session(rec, args, DumpHash.Sha256(w));
    }

    private static void Script(SimWorld w, uint t)
    {
        switch (t)
        {
            case 0:
                var join = new Op { Kind = OpKind.PlayerJoin };
                join.SetPayload(new PlayerJoinPayload { Slot = 2 });
                Assert.True(w.EnqueueOp(ActorId.System, in join));
                Enq(w, 1);
                Enq(w, 2);
                Enq(w, 5);
                break;
            case 1:
                Sample(w, 2, 1, 1000);
                Enq(w, 7, ReplayFixture.Stranger);
                Enq(w, 9, kind: (OpKind)0x00F0);
                break;
            case 2:
                for (uint r = 100; r < 200; r++) // 100 ops > the default per-tick drain cap of 64: 36 wait for tick 3
                {
                    Enq(w, r);
                }

                break;
            case 3:
                Sample(w, 2, 2, 1010);
                Sample(w, 5, 1, -50);
                break;
            case 4:
                Enq(w, 12);
                Enq(w, 13);
                break;
            case 6:
                Sample(w, 2, 3, 1020);
                Enq(w, 15);
                break;
        }
    }

    private static ReplayResult Replay(Session s, InMemoryRecorder? recording = null) => ReplayDriver.Run(recording ?? s.Recorder, s.Args);

    private static void AssertClean(ReplayResult r)
    {
        Assert.False(r.HasMismatch);
        Assert.Equal(0, r.EnqueueMismatches + r.SampleRefusals + r.SampleMismatches + r.ApplyTickMismatches + r.OpMismatches + r.ReplyMismatches);
        Assert.Equal(0, r.DrainCountMismatches + r.DumpHashMismatches);
        Assert.False(r.StoppedEarly);
        Assert.Null(r.FirstDivergenceTick);
    }

    /// <summary>A copy of <paramref name="s"/> with chosen records changed, dropped or added, for tamper tests.</summary>
    private static InMemoryRecorder Clone(
        InMemoryRecorder s,
        Func<RecordedOp, RecordedOp>? mapOp = null,
        Func<RecordedDrain, RecordedDrain>? mapDrain = null,
        Func<RecordedSample, RecordedSample>? mapSample = null,
        Func<RecordedDumpHash, RecordedDumpHash>? mapDump = null,
        Func<int, bool>? dropDrain = null,
        uint? flush = null,
        RecordingHeader? header = null)
    {
        var c = new InMemoryRecorder(header ?? s.Header, new RecorderCapacity(8192, 8192, 1024, 64, 1024, 16));
        for (int i = 0; i < s.Ops.Count; i++)
        {
            RecordedOp r = mapOp is null ? s.Ops[i] : mapOp(s.Ops[i]);
            Op op = r.Op;
            c.OnEnqueued(r.BoundaryTick, in op);
        }

        for (int i = 0; i < s.Drains.Count; i++)
        {
            if (dropDrain?.Invoke(i) == true)
            {
                continue;
            }

            RecordedDrain d = mapDrain is null ? s.Drains[i] : mapDrain(s.Drains[i]);
            Op op = d.Op;
            Reply reply = d.Reply;
            c.OnDrained(d.ApplyTick, in op, in reply);
        }

        for (int i = 0; i < s.Samples.Count; i++)
        {
            RecordedSample r = mapSample is null ? s.Samples[i] : mapSample(s.Samples[i]);
            TransformSample sample = r.SampleValue;
            c.OnSample(r.ReceivedTick, PlayerSlot.FromValue(r.Slot), in sample);
        }

        for (int i = 0; i < s.Overflows.Count; i++)
        {
            c.OnOverflow(s.Overflows[i].Tick);
        }

        for (int i = 0; i < s.DumpHashes.Count; i++)
        {
            RecordedDumpHash h = mapDump is null ? s.DumpHashes[i] : mapDump(s.DumpHashes[i]);
            c.RecordDumpHash(h.AtTick, h.Hash);
        }

        c.Flush(flush ?? s.TickCount);
        return c;
    }

    private static ContentSet Variant(Func<ImmutableArray<BalanceEntry>, ImmutableArray<BalanceEntry>>? tweak = null, bool changeAnItem = false)
    {
        RuleRegistry rules = RuleRegistry.CreateDefault();
        ImmutableArray<BalanceEntry> entries = BalanceKeys.CreateDefaultEntries();
        if (tweak is not null)
        {
            entries = tweak(entries);
        }

        BalanceBuildResult balance = BalanceTable.Build(BalanceKeys.CreateRegistry(), entries, rules);
        Assert.NotNull(balance.Table);
        ImmutableArray<ItemDef> items = BuiltInContent.M1aItems();
        if (changeAnItem)
        {
            items = items.Select(i => i.Id == "plank" ? i with { SellPriceCents = i.SellPriceCents + 1 } : i).ToImmutableArray();
        }

        ContentBuildResult built = BuiltInContent.Build(
            balance.Table!,
            BuiltInContent.M1aContainers(),
            items,
            BuiltInContent.M1aMailKinds(),
            BuiltInContent.M1aDestinationTypes(),
            rules);
        Assert.NotNull(built.Set);
        return built.Set!;
    }

    private static ImmutableArray<BalanceEntry> SetSim(ImmutableArray<BalanceEntry> entries, string key, long value)
    {
        string name = BalanceKeys.Sim.Owner + "." + key;
        return entries.Select(e => e.Name == name ? e with { Value = value } : e).ToImmutableArray();
    }

    // ------------------------------------------------------------ AC-2: round trip

    [Fact]
    public void RoundTrip_ReproducesEveryEnqueueApplyTickReplyAndDumpHash()
    {
        // Arrange
        var f = new ReplayFixture();
        Session s = Record(f);
        f.Live = false;

        // Act
        ReplayResult r = Replay(s);

        // Assert
        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
        Assert.Equal(ReplayRefusal.None, r.Refusal);
        AssertClean(r);
        Assert.Equal(s.Recorder.TickCount, r.TicksRun);
        Assert.Equal(10u, r.TicksRun);
        Assert.Equal(s.Recorder.Ops.Count, r.OpsReplayed);
        Assert.Equal(s.Recorder.Samples.Count, r.SamplesReplayed);
        Assert.Equal(s.Recorder.Drains.Count, r.DrainsChecked);
        Assert.Equal(s.Recorder.DumpHashes.Count, r.DumpHashesChecked);
        Assert.Equal(11, r.DumpHashesChecked); // before tick 0 and after each of the ten ticks
        Assert.NotNull(r.Host);
        Assert.Equal(s.FinalDump, DumpHash.Sha256(r.Host!.World));
        Assert.Equal(10u, r.Host.World.CurrentTick);
    }

    [Fact]
    public void RoundTrip_CoversBacklogRejectsAndSamples_SoTheChecksAreNotVacuous()
    {
        // Arrange + Act
        var f = new ReplayFixture();
        Session s = Record(f);

        // Assert: the recording really contains what the round trip claims to reproduce.
        RecordedDrain[] drains = s.Recorder.Drains.ToArray();
        RecordedOp[] ops = s.Recorder.Ops.ToArray();
        Assert.Contains(drains, d => ops.Single(o => o.Op.OpSeq == d.Op.OpSeq).BoundaryTick < d.ApplyTick); // backlog: later apply tick
        Assert.Contains(drains, d => d.Reply.Reason == CoreReason.BadRequest); // handler reject
        Assert.Contains(drains, d => d.Reply.Reason == CoreReason.UnknownActor); // core reject
        Assert.Contains(drains, d => d.Reply.Reason == CoreReason.UnknownKind); // core reject
        Assert.Contains(drains, d => d.Op.Kind == OpKind.PlayerJoin && d.Reply.IsOk);
        Assert.Equal(4, s.Recorder.Samples.Count);
        Assert.Equal(101u, ops[0].Op.OpSeq); // counters honoured: LastOpSeq 100
        Assert.Equal(s.Recorder.Ops.Count, drains.Length);
        Assert.Equal(11, s.Recorder.DumpHashes.Count);
        Assert.Equal(11, s.Recorder.DumpHashes.ToArray().Select(h => h.Hash).Distinct().Count()); // state changes every tick
    }

    [Fact]
    public void RoundTrip_WithoutADumpTrack_StillReproducesApplyTicks()
    {
        var f = new ReplayFixture();
        Session s = Record(f, dumps: false);

        ReplayResult r = Replay(s);

        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
        Assert.Equal(0, r.DumpHashesChecked);
        Assert.Equal(s.Recorder.Drains.Count, r.DrainsChecked);
        Assert.Equal(s.FinalDump, DumpHash.Sha256(r.Host!.World));
    }

    [Fact]
    public void Replay_EnqueueFromInsideATick_IsReEnqueuedBeforeTheNextTick_AndReproducesTheApplyTick()
    {
        // Arrange: the live session injects ReqId 11 from the phase-3-style binder of tick 3.
        var f = new ReplayFixture { Live = true, InjectAtTick = 3, InjectReqId = 11 };
        Session s = Record(f, dumps: false);
        f.Live = false; // the replay world must not inject it a second time
        RecordedOp injected = s.Recorder.Ops.ToArray().Single(o => o.Op.ReqId == 11);

        // Act
        ReplayResult r = Replay(s);

        // Assert
        Assert.Equal(4u, injected.BoundaryTick); // tick 3's drain was already over: it waits for tick 4
        Assert.Equal(4u, s.Recorder.Drains.ToArray().Single(d => d.Op.ReqId == 11).ApplyTick);
        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
        AssertClean(r);
        Assert.Equal(s.Recorder.Ops.Count, r.OpsReplayed);
        Assert.Equal(s.FinalDump, DumpHash.Sha256(r.Host!.World));
    }

    [Fact]
    public void Replay_EnqueueFromInsideATick_DumpHashOfThatTickDiffers_ApplyTicksStillMatch()
    {
        // The documented limit of the boundary-tick rule: at the end of tick 3 the live queue holds the injected op (queue boundary
        // tick 3) and the replay queue does not yet, so exactly that dump hash differs. Nothing else does.
        var f = new ReplayFixture { Live = true, InjectAtTick = 3, InjectReqId = 11 };
        Session s = Record(f, dumps: true);
        f.Live = false;

        ReplayResult r = Replay(s);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.DumpHashMismatches);
        Assert.Equal(4u, r.FirstDivergenceTick); // the hash taken with 4 ticks completed
        Assert.Equal(0, r.ApplyTickMismatches + r.OpMismatches + r.ReplyMismatches + r.EnqueueMismatches + r.DrainCountMismatches);
    }

    [Fact]
    public void Replay_EnqueueFromPhaseZeroHook_ReproducesApplyTickAndEveryDumpHash()
    {
        // Arrange: a between-ticks op (ReqId 21) for tick 3, then an op (ReqId 22) injected from the tick-3 prologue, before the
        // phase-0 drain (the sample latch is the only prologue hook a composed world offers). Live-only: the replay re-enqueues it.
        var f = new ReplayFixture();
        var args = new SimBuildArgs(f.Profile, f.Content, Seed, _counters);
        var rec = new InMemoryRecorder(RecordingHeader.Create(f.Content, Seed, _counters));
        var hook = new PrologueHookRecorder(rec);
        SimWorld w = SimComposition.Build(args with { Recorder = hook }).World;
        bool injected = false;
        hook.OnSampleLatched = tick =>
        {
            if (tick == 3 && !injected)
            {
                injected = true;
                Enq(w, 22);
            }
        };
        Assert.True(rec.RecordDump(w));
        for (uint t = 0; t < 10; t++)
        {
            Script(w, t);
            if (t == 3)
            {
                Enq(w, 21);
            }

            w.Tick();
            Assert.True(rec.RecordDump(w));
            rec.Flush(w.CurrentTick);
        }

        var s = new Session(rec, args, DumpHash.Sha256(w));

        // Act
        ReplayResult r = Replay(s);

        // Assert
        RecordedOp between = rec.Ops.ToArray().Single(o => o.Op.ReqId == 21);
        RecordedOp fromHook = rec.Ops.ToArray().Single(o => o.Op.ReqId == 22);
        Assert.True(injected);
        Assert.Equal(3u, between.BoundaryTick);
        Assert.Equal(3u, fromHook.BoundaryTick);
        Assert.True(between.Op.OpSeq < fromHook.Op.OpSeq);
        Assert.Equal(3u, rec.Drains.ToArray().Single(d => d.Op.ReqId == 21).ApplyTick);
        Assert.Equal(3u, rec.Drains.ToArray().Single(d => d.Op.ReqId == 22).ApplyTick);
        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
        AssertClean(r);
        Assert.Equal(0, r.DumpHashMismatches);
        Assert.Equal(11, r.DumpHashesChecked);
        Assert.Equal(rec.Ops.Count, r.OpsReplayed);
        Assert.Equal(s.FinalDump, DumpHash.Sha256(r.Host!.World));
    }

    [Fact]
    public void Observer_EnqueueTheDriverDidNotMake_IsReportedAsADivergence_NotThrown()
    {
        // Arrange: a recording with no ops, so there is no record an unexpected in-tick enqueue could be compared with.
        var f = new ReplayFixture();
        var rec = new InMemoryRecorder(RecordingHeader.Create(f.Content, Seed, _counters));
        var result = new ReplayResult();
        var observer = new ReplayDriver.Observer(rec, result) { Tick = 4 };
        Op op = Op(11);

        // Act
        Exception? thrown = null;
        try
        {
            observer.OnEnqueued(5, in op);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        // Assert
        Assert.Null(thrown);
        Assert.Equal(1, result.EnqueueMismatches);
        Assert.Equal(4u, result.FirstDivergenceTick);
        Assert.False(observer.EnqueueSeen);
        Assert.True(result.HasMismatch);
    }

    [Fact]
    public void EmptyRecording_BuildsTheWorldAndRunsNoTicks()
    {
        var f = new ReplayFixture();
        var args = new SimBuildArgs(f.Profile, f.Content, Seed, _counters);
        var rec = new InMemoryRecorder(RecordingHeader.Create(f.Content, Seed, _counters));

        ReplayResult r = ReplayDriver.Run(rec, args);

        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
        Assert.Equal(0u, r.TicksRun);
        Assert.NotNull(r.Host);
        Assert.Equal(2, f.Constructions);
    }

    [Fact]
    public void Replay_IgnoresARecorderPassedInTheArgs_AndDoesNotTouchTheRecording()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        var other = new InMemoryRecorder(s.Recorder.Header);
        int opsBefore = s.Recorder.Ops.Count;
        SimBuildArgs args = s.Args with { Recorder = other };

        ReplayResult r = ReplayDriver.Run(s.Recorder, args);

        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
        Assert.Equal(0, other.Ops.Count + other.Drains.Count);
        Assert.Equal(opsBefore, s.Recorder.Ops.Count);
    }

    [Fact]
    public void Run_RejectsNullArguments()
    {
        var f = new ReplayFixture();
        Session s = Record(f);

        Assert.Throws<ArgumentNullException>(() => ReplayDriver.Run(null!, s.Args));
        Assert.Throws<ArgumentNullException>(() => ReplayDriver.Run(s.Recorder, new SimBuildArgs(f.Profile, null!, Seed)));
    }

    [Fact]
    public void ReplayRunTwice_GivesTheSameResult()
    {
        var f = new ReplayFixture();
        Session s = Record(f);

        ReplayResult a = Replay(s);
        ReplayResult b = Replay(s);

        Assert.Equal(DumpHash.Sha256(a.Host!.World), DumpHash.Sha256(b.Host!.World));
        Assert.Equal(a.DrainsChecked, b.DrainsChecked);
        Assert.Equal(ReplayOutcome.Reproduced, b.Outcome);
    }

    // ------------------------------------------------------------ AC-2: divergences are detected

    [Fact]
    public void Tampered_ApplyTick_IsReported()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        uint seq = s.Recorder.Drains[3].Op.OpSeq;
        uint wrong = s.Recorder.Drains[3].ApplyTick + 1;
        InMemoryRecorder tampered = Clone(s.Recorder, mapDrain: d => d.Op.OpSeq == seq ? new RecordedDrain(wrong, d.Op, d.Reply) : d);

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.ApplyTickMismatches);
        Assert.Equal(wrong - 1, r.FirstDivergenceTick);
    }

    [Fact]
    public void Tampered_Reply_IsReported()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        InMemoryRecorder tampered = Clone(s.Recorder, mapDrain: d =>
        {
            if (d.Op.ReqId != 2)
            {
                return d;
            }

            Reply reply = d.Reply;
            reply.Reason = 99;
            reply.Result = ReplyResult.Rejected;
            return new RecordedDrain(d.ApplyTick, d.Op, reply);
        });

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.ReplyMismatches);
        Assert.Equal(0u, r.FirstDivergenceTick);
    }

    [Fact]
    public void Tampered_DrainedOp_IsReported()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        InMemoryRecorder tampered = Clone(s.Recorder, mapDrain: d =>
        {
            Op op = d.Op;
            if (op.ReqId == 2)
            {
                op.ReqId = 4;
            }

            return new RecordedDrain(d.ApplyTick, op, d.Reply);
        });

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.OpMismatches);
    }

    [Fact]
    public void Tampered_OpSeqOfAnEnqueue_IsReported()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        InMemoryRecorder tampered = Clone(s.Recorder, mapOp: r =>
        {
            Op op = r.Op;
            if (op.ReqId == 12)
            {
                op.OpSeq += 1000;
            }

            return new RecordedOp(r.BoundaryTick, in op);
        });

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.EnqueueMismatches);
        Assert.Equal(4u, r.FirstDivergenceTick);
    }

    [Fact]
    public void Tampered_DrainCount_MissingAndExtraDrainsAreReported()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        int last = s.Recorder.Drains.Count - 1;

        ReplayResult missing = Replay(s, Clone(s.Recorder, dropDrain: i => i == last));
        InMemoryRecorder extra = Clone(s.Recorder);
        Op op = Op(77);
        Reply reply = Reply.Ok(in op);
        extra.OnDrained(9, in op, in reply);
        ReplayResult surplus = Replay(s, extra);

        Assert.Equal(ReplayOutcome.Diverged, missing.Outcome);
        Assert.Equal(1, missing.DrainCountMismatches);
        Assert.Equal(ReplayOutcome.Diverged, surplus.Outcome);
        Assert.Equal(1, surplus.DrainCountMismatches);
    }

    [Fact]
    public void Tampered_DumpHash_IsReportedAtItsTick()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        InMemoryRecorder tampered = Clone(s.Recorder, mapDump: h => h.AtTick == 6 ? new RecordedDumpHash(6, Sha256.Hash("wrong"u8)) : h);

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.DumpHashMismatches);
        Assert.Equal(6u, r.FirstDivergenceTick);
        Assert.Equal(11, r.DumpHashesChecked);
    }

    [Fact]
    public void Tampered_SampleSlot_IsRefusedByTheWorld_AndReported()
    {
        var f = new ReplayFixture();
        Session s = Record(f, dumps: false);
        InMemoryRecorder tampered = Clone(s.Recorder, mapSample: r => r.ReceivedTick == 3 && r.Slot == 5 ? new RecordedSample(3, PlayerSlot.NoneValue, r.SampleValue) : r);

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.Equal(1, r.SampleRefusals);
        Assert.Equal(3u, r.FirstDivergenceTick);
    }

    [Fact]
    public void Tampered_SampleReceivedTick_IsReported()
    {
        // Arrange: slot 2's second sample claims tick 8, so it (and the records after it) are written after the later ones.
        var f = new ReplayFixture();
        Session s = Record(f, dumps: false);
        InMemoryRecorder tampered = Clone(s.Recorder, mapSample: r => r.Slot == 2 && r.ReceivedTick == 3 ? new RecordedSample(8, r.Slot, r.SampleValue) : r);

        // Act
        ReplayResult r = Replay(s, tampered);

        // Assert
        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.True(r.SampleMismatches > 0);
        Assert.Equal(8u, r.FirstDivergenceTick);
        Assert.Equal(0, r.SampleRefusals + r.ApplyTickMismatches + r.EnqueueMismatches);
    }

    [Fact]
    public void Tampered_SamplePayload_IsReported()
    {
        // Arrange: slot 2's second sample carries Seq 1 again, which the latch discards as stale: the replay never latches it.
        var f = new ReplayFixture();
        Session s = Record(f, dumps: false);
        InMemoryRecorder tampered = Clone(s.Recorder, mapSample: r =>
        {
            if (r.Slot != 2 || r.ReceivedTick != 3)
            {
                return r;
            }

            TransformSample stale = r.SampleValue;
            stale.Seq = 1;
            return new RecordedSample(r.ReceivedTick, r.Slot, stale);
        });

        // Act
        ReplayResult r = Replay(s, tampered);

        // Assert
        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.True(r.SampleMismatches > 0);
        Assert.Equal(3u, r.FirstDivergenceTick);
        Assert.Equal(0, r.SampleRefusals);
    }

    [Fact]
    public void ReplayThatCannotTickAsLongAsTheRecording_StopsEarlyAndDiverges()
    {
        // Arrange: the system stops the world in tick 2 (deterministically, live and replay). Claiming 8 ticks cannot be honoured.
        var f = new ReplayFixture { StopAtTick = 2 };
        Session s = Record(f);
        Assert.Equal(3u, s.Recorder.TickCount);
        Assert.Equal(ReplayOutcome.Reproduced, Replay(s).Outcome);

        // Act
        ReplayResult r = Replay(s, Clone(s.Recorder, flush: 8));

        // Assert
        Assert.Equal(ReplayOutcome.Diverged, r.Outcome);
        Assert.True(r.StoppedEarly);
        Assert.True(r.HasMismatch);
        Assert.Equal(3u, r.TicksRun);
        Assert.Equal(3u, r.FirstDivergenceTick);
    }

    // ------------------------------------------------------------ AC-3: refusals

    private static void AssertRefused(ReplayFixture f, ReplayResult r, ReplayRefusal field)
    {
        Assert.Equal(ReplayOutcome.Refused, r.Outcome);
        Assert.Equal(field, r.Refusal);
        Assert.Null(r.Host);
        Assert.Equal(0, f.Constructions); // composition never ran: no system was constructed, so no world was built
        Assert.Equal(0u, r.TicksRun);
        Assert.Equal(0, r.OpsReplayed + r.SamplesReplayed + r.DrainsChecked + r.DumpHashesChecked);
    }

    [Fact]
    public void Refused_WhenTheContentSetDiffers_NamingContentHash()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        ContentSet other = Variant(changeAnItem: true);
        Assert.NotEqual(ContentHash.Compute(f.Content), ContentHash.Compute(other));
        Assert.Equal(ContentHash.ComputeSettings(f.Content.Settings), ContentHash.ComputeSettings(other.Settings));
        f.Constructions = 0;

        ReplayResult r = ReplayDriver.Run(s.Recorder, s.Args with { Content = other });

        AssertRefused(f, r, ReplayRefusal.ContentHash);
    }

    [Fact]
    public void Refused_WhenTheSettingsDiffer_NamingSettingsHash()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        ContentSet other = Variant(e => SetSim(e, BalanceKeys.Sim.WarmupTicksKey, 301));
        Assert.NotEqual(ContentHash.ComputeSettings(f.Content.Settings), ContentHash.ComputeSettings(other.Settings));
        f.Constructions = 0;

        ReplayResult r = ReplayDriver.Run(s.Recorder, s.Args with { Content = other });

        AssertRefused(f, r, ReplayRefusal.SettingsHash);
    }

    [Fact]
    public void Refused_WhenTheRunSeedDiffers_NamingRunSeed()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        f.Constructions = 0;

        ReplayResult r = ReplayDriver.Run(s.Recorder, s.Args with { RunSeed = Seed + 1 });

        AssertRefused(f, r, ReplayRefusal.RunSeed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Refused_WhenTheInitialCountersDiffer_NamingInitialCounters(int variant)
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        f.Constructions = 0;
        SimInitialCounters? wrong = variant switch
        {
            0 => _counters with { LastOpSeq = 101 },
            1 => _counters with { LastMailId = 8 },
            2 => _counters with { LastEntityCounters = ImmutableArray.Create(0u, 5u) },
            _ => null,
        };

        ReplayResult r = ReplayDriver.Run(s.Recorder, s.Args with { Counters = wrong });

        AssertRefused(f, r, ReplayRefusal.InitialCounters);
    }

    [Fact]
    public void Refused_WhenTheFormatVersionIsUnknown_NamingFormatVersion()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        RecordingHeader h = s.Recorder.Header;
        var future = new RecordingHeader(2, h.ContentHash, h.SettingsHash, h.RunSeed, h.ToInitialCounters());
        f.Constructions = 0;

        ReplayResult r = Replay(s, Clone(s.Recorder, header: future));

        AssertRefused(f, r, ReplayRefusal.FormatVersion);
    }

    [Fact]
    public void NotRefused_WhenTheCountersAreEquivalentSpellings()
    {
        // Counters recorded as a short entity array replay with a longer, zero-padded one (missing classes are 0).
        var f = new ReplayFixture();
        var shortCounters = new SimInitialCounters { LastOpSeq = 100, LastMailId = 7, LastEntityCounters = ImmutableArray.Create(0u, 4u) };
        var paddedCounters = new SimInitialCounters { LastOpSeq = 100, LastMailId = 7, LastEntityCounters = ImmutableArray.Create(0u, 4u, 0u, 0u) };
        Session s = Record(f);

        ReplayResult r = ReplayDriver.Run(s.Recorder, s.Args with { Counters = paddedCounters });

        Assert.Equal(shortCounters.LastOpSeq, paddedCounters.LastOpSeq);
        Assert.Equal(ReplayOutcome.Reproduced, r.Outcome);
    }

    // ------------------------------------------------------------ AC-3: non-reproducible

    [Fact]
    public void RecordingThatSawOverflow_ReplaysToCompletion_AndIsReportedNonReproducible()
    {
        // Arrange: a content set with the smallest legal log (8); tick 2 raises 12 events, so the live log overflows in tick 2.
        ContentSet small = Variant(e => SetSim(e, BalanceKeys.Sim.EventLogCapacityKey, 8));
        var f = new ReplayFixture { RaiseAtTick = 2, RaiseCount = 12 };
        Session s = Record(f, small);
        Assert.Contains(s.Recorder.Overflows.ToArray(), o => o.Tick == 2); // later ticks (the drain backlog's replies) may overflow the 8-slot log too

        // Act
        ReplayResult r = Replay(s);

        // Assert
        Assert.Equal(ReplayOutcome.NonReproducible, r.Outcome);
        Assert.Equal(ReplayRefusal.None, r.Refusal);
        Assert.False(s.Recorder.IsReproducible);
        Assert.NotNull(r.Host);
        Assert.Equal(10u, r.TicksRun); // replayed to completion
        Assert.Equal(s.Recorder.Drains.Count, r.DrainsChecked);
        AssertClean(r);
    }

    [Fact]
    public void OverflowRecording_WithADivergence_IsStillNonReproducible_NotDiverged()
    {
        var f = new ReplayFixture();
        Session s = Record(f);
        InMemoryRecorder tampered = Clone(s.Recorder, mapDrain: d => d.Op.ReqId == 2 ? new RecordedDrain(d.ApplyTick + 1, d.Op, d.Reply) : d);
        tampered.OnOverflow(3);

        ReplayResult r = Replay(s, tampered);

        Assert.Equal(ReplayOutcome.NonReproducible, r.Outcome);
        Assert.Equal(1, r.ApplyTickMismatches); // still counted for inspection
        Assert.Equal(10u, r.TicksRun);
    }

    [Fact]
    public void RecordingWithLostRecords_IsReportedNonReproducible()
    {
        var f = new ReplayFixture();
        var args = new SimBuildArgs(f.Profile, f.Content, Seed, _counters);
        var tiny = new InMemoryRecorder(RecordingHeader.Create(f.Content, Seed, _counters), new RecorderCapacity(1, 1, 1, 1, 1, 0));
        SimHost host = SimComposition.Build(new SimBuildArgs(f.Profile, f.Content, Seed, _counters) { Recorder = tiny });
        Enq(host.World, 2);
        Enq(host.World, 4);
        host.World.Tick();
        Assert.True(tiny.LostRecords > 0);

        ReplayResult r = ReplayDriver.Run(tiny, args);

        Assert.Equal(ReplayOutcome.NonReproducible, r.Outcome);
    }
}
