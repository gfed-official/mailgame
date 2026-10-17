using System.Collections.Immutable;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Core.Replay;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>Probe whose Before(phase) calls a delegate, to enqueue from "inside" the tick (the phase-0 hook).</summary>
internal sealed class HookProbe : IPhaseProbe
{
    public Action<PhaseId>? OnBefore { get; set; }

    public void Before(PhaseId phase) => OnBefore?.Invoke(phase);

    public void After(PhaseId phase)
    {
    }
}

/// <summary>A phase binder whose body is a delegate.</summary>
internal sealed class ActionBinder : ISimPhase
{
    private readonly Action<uint> _action;

    public ActionBinder(Action<uint> action)
    {
        _action = action;
    }

    public void Run(in TickContext ctx) => _action(ctx.Tick);
}

/// <summary>
/// One world with an <see cref="InMemoryRecorder"/> bound, a single op kind (<see cref="Kind"/>: even <c>ReqId</c> accepted, odd
/// rejected with <c>BadRequest</c>) and one event kind, no Sim systems. Built directly with the world builder so tests can pass a
/// phase probe.
/// </summary>
internal sealed class RecRig
{
    public const OpKind Kind = OpKind.TeleportAck;
    public const OpKind Unregistered = (OpKind)0x00F0;
    public const EventKind Ev = (EventKind)0x0F01;

    public RecRig(
        RecorderCapacity? capacity = null,
        int maxDrain = 64,
        int opCapacity = 4096,
        int reserve = 8,
        int eventLog = 4096,
        uint lastOpSeq = 0,
        IPhaseProbe? probe = null,
        Action<SimWorldBuilder>? configure = null,
        LambdaHandler.ApplyFn? apply = null)
    {
        var settings = new SimCoreSettings { MaxOpsDrainedPerTick = maxDrain, OpQueueCapacity = opCapacity, SystemOpReserve = reserve, EventLogCapacity = eventLog };
        Recorder = new InMemoryRecorder(new RecordingHeader(1, default, default, 0, null), capacity ?? RecorderCapacity.Default);
        var builder = new SimWorldBuilder(settings, probe: probe, initialCounters: new SimInitialCounters { LastOpSeq = lastOpSeq });
        builder.BindRecorder(Recorder);
        builder.BindEventKind(EventDescriptor.For<uint>(Ev, ReplicationClass.Replicated, false, 1, 0, 4));
        builder.BindOp(OpDescriptor.Empty(Kind, false, 0, 0), new LambdaHandler(Kind, apply ?? Default));
        configure?.Invoke(builder);
        World = builder.Freeze();
    }

    public SimWorld World { get; }

    public InMemoryRecorder Recorder { get; }

    public static Op Make(uint reqId, OpKind kind = Kind) => new() { Kind = kind, ReqId = reqId };

    public bool Enqueue(uint reqId, ActorId? actor = null, OpKind kind = Kind)
    {
        Op op = Make(reqId, kind);
        return World.EnqueueOp(actor ?? ActorId.System, in op);
    }

    public void RaiseEvents(int count)
    {
        for (uint i = 0; i < count; i++)
        {
            World.Events.TryRaise(Ev, 0xFF, in i);
        }
    }

    private static Reply Default(in Op op, in TickContext ctx) =>
        op.ReqId % 2 == 0 ? Reply.Ok(in op) : Reply.Reject(in op, CoreReason.BadRequest);
}

/// <summary>
/// Story 026, AC-1 (TR-sim-045, TR-sim-018): what the in-memory recorder stores and in what order: one record per accepted
/// enqueue (none for a refused one), per drained op (accepted or rejected), per latched sample and per event-log overflow,
/// plus the header. Also the recorder's own capacity rules (growth only between ticks).
/// </summary>
public class RecorderTests
{
    // ------------------------------------------------------------ header

    [Fact]
    public void Header_Create_HoldsVersionOne_TheRealHashes_Seed_AndCounters()
    {
        // Arrange
        ContentSet content = BuiltInContent.CreateM1a();
        var counters = new SimInitialCounters { LastOpSeq = 5, LastMailId = 9, LastEntityCounters = ImmutableArray.Create(0u, 3u) };

        // Act
        RecordingHeader header = RecordingHeader.Create(content, 0xABCDEFu, counters);

        // Assert
        Assert.Equal((ushort)1, header.FormatVersion);
        Assert.Equal(RecordingHeader.CurrentFormatVersion, header.FormatVersion);
        Assert.Equal(ContentHash.Compute(content), header.ContentHash);
        Assert.Equal(ContentHash.ComputeSettings(content.Settings), header.SettingsHash);
        Assert.Equal(32, header.ContentHash.ToArray().Length);
        Assert.Contains(header.ContentHash.ToArray(), b => b != 0);
        Assert.Contains(header.SettingsHash.ToArray(), b => b != 0);
        Assert.NotEqual(header.ContentHash, header.SettingsHash);
        Assert.Equal(0xABCDEFul, header.RunSeed);
        Assert.Equal(5u, header.LastOpSeq);
        Assert.Equal(9u, header.LastMailId);
        Assert.Equal(new[] { 0u, 3u }, header.LastEntityCounters);
        SimInitialCounters back = header.ToInitialCounters();
        Assert.Equal(5u, back.LastOpSeq);
        Assert.Equal(9u, back.LastMailId);
        Assert.Equal(new[] { 0u, 3u }, back.LastEntityCounters);
    }

    [Fact]
    public void Header_NoCounters_IsAllZeroAndEmpty()
    {
        RecordingHeader header = RecordingHeader.Create(BuiltInContent.CreateM1a(), 1);

        Assert.Equal(0u, header.LastOpSeq);
        Assert.Equal(0u, header.LastMailId);
        Assert.Empty(header.LastEntityCounters);
        Assert.Throws<ArgumentNullException>(() => RecordingHeader.Create(null!, 1));
    }

    [Fact]
    public void Header_FirstMismatch_NamesEachFieldInOrder()
    {
        // Arrange
        ContentSet content = BuiltInContent.CreateM1a();
        RecordingHeader baseline = RecordingHeader.Create(content, 7);
        Hash256 other = Sha256.Hash("other"u8);

        // Act + Assert
        Assert.Equal(ReplayRefusal.None, baseline.FirstMismatch(RecordingHeader.Create(content, 7)));
        Assert.Equal(ReplayRefusal.FormatVersion, baseline.FirstMismatch(new RecordingHeader(2, baseline.ContentHash, baseline.SettingsHash, 7, null)));
        Assert.Equal(ReplayRefusal.SettingsHash, baseline.FirstMismatch(new RecordingHeader(1, baseline.ContentHash, other, 7, null)));
        Assert.Equal(ReplayRefusal.ContentHash, baseline.FirstMismatch(new RecordingHeader(1, other, baseline.SettingsHash, 7, null)));
        Assert.Equal(ReplayRefusal.RunSeed, baseline.FirstMismatch(RecordingHeader.Create(content, 8)));
        Assert.Equal(ReplayRefusal.InitialCounters, baseline.FirstMismatch(RecordingHeader.Create(content, 7, new SimInitialCounters { LastOpSeq = 1 })));
        Assert.Equal(ReplayRefusal.InitialCounters, baseline.FirstMismatch(RecordingHeader.Create(content, 7, new SimInitialCounters { LastMailId = 1 })));
        Assert.Equal(
            ReplayRefusal.InitialCounters,
            baseline.FirstMismatch(RecordingHeader.Create(content, 7, new SimInitialCounters { LastEntityCounters = ImmutableArray.Create(0u, 1u) })));
        Assert.Throws<ArgumentNullException>(() => baseline.FirstMismatch(null!));
    }

    [Fact]
    public void Header_EntityCountersCompare_TreatsMissingTrailingClassesAsZero()
    {
        ContentSet content = BuiltInContent.CreateM1a();
        RecordingHeader shortForm = RecordingHeader.Create(content, 7, new SimInitialCounters { LastEntityCounters = ImmutableArray.Create(2u) });
        RecordingHeader longForm = RecordingHeader.Create(content, 7, new SimInitialCounters { LastEntityCounters = ImmutableArray.Create(2u, 0u, 0u) });

        Assert.Equal(ReplayRefusal.None, shortForm.FirstMismatch(longForm));
        Assert.Equal(ReplayRefusal.None, longForm.FirstMismatch(shortForm));
    }

    [Fact]
    public void Recorder_ExposesItsHeader()
    {
        var header = new RecordingHeader(1, default, default, 3, null);

        Assert.Same(header, new InMemoryRecorder(header).Header);
    }

    // ------------------------------------------------------------ enqueue records

    [Fact]
    public void AcceptedEnqueues_AreRecordedInOrder_WithTheStampedOpSeqAndActor()
    {
        // Arrange
        var rig = new RecRig();
        Op claimed = RecRig.Make(2);
        claimed.OpSeq = 99;
        claimed.Actor = new ActorId(0x0100_0042u);

        // Act
        rig.World.EnqueueOp(ActorId.System, in claimed);
        rig.Enqueue(4);
        rig.Enqueue(6);

        // Assert
        Assert.Equal(3, rig.Recorder.Ops.Count);
        for (int i = 0; i < 3; i++)
        {
            RecordedOp r = rig.Recorder.Ops[i];
            Assert.Equal((uint)(i + 1), r.Op.OpSeq);
            Assert.Equal(ActorId.System, r.Op.Actor);
            Assert.Equal(0u, r.BoundaryTick);
        }

        Assert.Equal(2u, rig.Recorder.Ops[0].Op.ReqId);
        Assert.Equal(6u, rig.Recorder.Ops[2].Op.ReqId);
    }

    [Fact]
    public void EnqueueBetweenTicks_RecordsTheCurrentTickAsBoundary()
    {
        var rig = new RecRig();
        rig.World.Tick();
        rig.World.Tick();

        rig.Enqueue(2);

        Assert.Equal(2u, rig.Recorder.Ops[0].BoundaryTick);
    }

    [Fact]
    public void RefusedEnqueues_RecordNothing()
    {
        // Arrange: capacity 4, reserve 1 -> a player is refused at 3 queued, a System op at 4 queued.
        var rig = new RecRig(opCapacity: 4, reserve: 1);
        Assert.True(rig.Enqueue(2));
        Assert.True(rig.Enqueue(4));
        Assert.True(rig.Enqueue(6));

        // Act
        bool busyPlayer = rig.Enqueue(8, new ActorId(0x0100_0005u));
        bool lastSlot = rig.Enqueue(10);
        bool busySystem = rig.Enqueue(12);

        // Assert
        Assert.False(busyPlayer);
        Assert.True(lastSlot);
        Assert.False(busySystem);
        Assert.Equal(new uint[] { 2, 4, 6, 10 }, Enumerable.Range(0, rig.Recorder.Ops.Count).Select(i => rig.Recorder.Ops[i].Op.ReqId));
    }

    [Fact]
    public void EnqueueRefusedForExhaustedOpSeq_OrBadState_RecordsNothing()
    {
        // Arrange
        var exhausted = new RecRig(lastOpSeq: uint.MaxValue);
        var stopped = new RecRig();
        stopped.World.Tick();
        Assert.True(stopped.World.Stop());

        // Act
        bool exhaustedAccepted = exhausted.Enqueue(2);
        bool stoppedAccepted = stopped.Enqueue(2);

        // Assert
        Assert.False(exhaustedAccepted);
        Assert.False(stoppedAccepted);
        Assert.Equal(0, exhausted.Recorder.Ops.Count);
        Assert.Equal(0, stopped.Recorder.Ops.Count);
    }

    // ------------------------------------------------------------ boundary tick rule (in-tick enqueues)

    [Fact]
    public void EnqueueFromThePhaseZeroHook_RecordsTheCurrentTick_AndTheOpAppliesInThatSameTick()
    {
        // Arrange
        var hook = new HookProbe();
        var rig = new RecRig(probe: hook);
        rig.World.Tick();
        rig.World.Tick();
        bool accepted = false;
        hook.OnBefore = phase =>
        {
            if (phase == PhaseId.InputsApply && accepted == false)
            {
                accepted = rig.Enqueue(2);
            }
        };

        // Act
        rig.World.Tick();

        // Assert
        Assert.True(accepted);
        Assert.Equal(2u, rig.Recorder.Ops[0].BoundaryTick);
        Assert.Equal(2u, Assert.Single(Enumerable.Range(0, rig.Recorder.Drains.Count).Select(i => rig.Recorder.Drains[i])).ApplyTick);
    }

    [Fact]
    public void EnqueueFromAHandlerDuringTheDrain_RecordsTheNextTick_AndTheOpAppliesThere()
    {
        // Arrange: the handler of ReqId 2 enqueues ReqId 4 while tick 0 drains.
        RecRig? rig = null;
        rig = new RecRig(apply: (in Op op, in TickContext _) =>
        {
            if (op.ReqId == 2)
            {
                rig!.Enqueue(4);
            }

            return Reply.Ok(in op);
        });
        rig.Enqueue(2);

        // Act
        rig.World.Tick();
        rig.World.Tick();

        // Assert
        Assert.Equal(0u, rig.Recorder.Ops[0].BoundaryTick);
        Assert.Equal(1u, rig.Recorder.Ops[1].BoundaryTick);
        Assert.Equal(0u, rig.Recorder.Drains[0].ApplyTick);
        Assert.Equal(1u, rig.Recorder.Drains[1].ApplyTick);
        Assert.Equal(4u, rig.Recorder.Drains[1].Op.ReqId);
    }

    [Fact]
    public void EnqueueFromALaterPhase_RecordsTheNextTick_AndTheNextEnqueueBetweenTicksSharesIt()
    {
        // Arrange
        RecRig? rig = null;
        rig = new RecRig(configure: b => b.Bind(PhaseId.MailStep, new ActionBinder(tick =>
        {
            if (tick == 3)
            {
                rig!.Enqueue(2);
            }
        })));
        for (int i = 0; i < 3; i++)
        {
            rig.World.Tick();
        }

        // Act
        rig.World.Tick();
        rig.Enqueue(4);
        rig.World.Tick();

        // Assert: the phase-3 style enqueue of tick 3 waits for tick 4's drain; so does the between-ticks one.
        Assert.Equal(4u, rig.Recorder.Ops[0].BoundaryTick);
        Assert.Equal(4u, rig.Recorder.Ops[1].BoundaryTick);
        Assert.Equal(new uint[] { 4, 4 }, Enumerable.Range(0, rig.Recorder.Drains.Count).Select(i => rig.Recorder.Drains[i].ApplyTick));
    }

    [Fact]
    public void BoundaryTick_EqualsTheApplyTick_WhenTheQueueIsUnderTheDrainCap()
    {
        // Arrange: ops enqueued between ticks and from the hook across several ticks.
        var hook = new HookProbe();
        var rig = new RecRig(probe: hook);
        uint next = 100;
        hook.OnBefore = phase =>
        {
            if (phase == PhaseId.InputsApply)
            {
                rig.Enqueue(next += 2);
            }
        };

        // Act
        for (int t = 0; t < 5; t++)
        {
            rig.Enqueue((uint)(2 * t));
            rig.World.Tick();
        }

        // Assert
        Assert.Equal(10, rig.Recorder.Drains.Count);
        for (int i = 0; i < rig.Recorder.Drains.Count; i++)
        {
            RecordedDrain d = rig.Recorder.Drains[i];
            RecordedOp enq = rig.Recorder.Ops.ToArray().Single(o => o.Op.OpSeq == d.Op.OpSeq);
            Assert.Equal(enq.BoundaryTick, d.ApplyTick);
        }
    }

    // ------------------------------------------------------------ drain records

    [Fact]
    public void Drains_AcceptedAndRejected_AreRecordedWithApplyTickOpAndReply()
    {
        // Arrange: ReqId 2 accepted by the handler, 3 rejected by the handler, an unregistered kind rejected by Sim Core.
        var rig = new RecRig();
        rig.World.Tick();
        rig.Enqueue(2);
        rig.Enqueue(3);
        rig.Enqueue(5, kind: RecRig.Unregistered);

        // Act
        rig.World.Tick();

        // Assert
        Assert.Equal(3, rig.Recorder.Drains.Count);
        RecordedDrain ok = rig.Recorder.Drains[0];
        RecordedDrain handlerReject = rig.Recorder.Drains[1];
        RecordedDrain coreReject = rig.Recorder.Drains[2];
        Assert.Equal(1u, ok.ApplyTick);
        Assert.Equal(2u, ok.Op.ReqId);
        Assert.Equal(1u, ok.Op.OpSeq);
        Assert.True(ok.Reply.IsOk);
        Assert.Equal(ReplyResult.Ok, ok.Reply.Result);
        Assert.Equal(2u, ok.Reply.ReqId);
        Assert.Equal(CoreReason.BadRequest, handlerReject.Reply.Reason);
        Assert.Equal(ReplyResult.Rejected, handlerReject.Reply.Result);
        Assert.Equal(3u, handlerReject.Op.ReqId);
        Assert.Equal(CoreReason.UnknownKind, coreReject.Reply.Reason);
        Assert.Equal(RecRig.Unregistered, coreReject.Op.Kind);
        Assert.Equal(3u, coreReject.Op.OpSeq);
    }

    [Fact]
    public void Drains_BeyondTheCap_AreRecordedWithTheirLaterApplyTicks()
    {
        var rig = new RecRig(maxDrain: 2);
        for (uint i = 1; i <= 5; i++)
        {
            rig.Enqueue(i * 2);
        }

        for (int t = 0; t < 3; t++)
        {
            rig.World.Tick();
        }

        Assert.Equal(new uint[] { 0, 0, 1, 1, 2 }, Enumerable.Range(0, rig.Recorder.Drains.Count).Select(i => rig.Recorder.Drains[i].ApplyTick));
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5 }, Enumerable.Range(0, rig.Recorder.Drains.Count).Select(i => rig.Recorder.Drains[i].Op.OpSeq));
    }

    // ------------------------------------------------------------ sample records

    [Fact]
    public void Samples_AreRecordedInLatchOrder_AndStaleOnesAreNot()
    {
        // Arrange: slot 3 seq 1, slot 1 seq 1, slot 1 seq 1 again (stale), slot 3 seq 2.
        var rig = new RecRig();
        Write(rig, 3, 1, 300);
        Write(rig, 1, 1, 100);
        Write(rig, 1, 1, 111);
        Write(rig, 3, 2, 301);

        // Act
        rig.World.Tick();
        Write(rig, 1, 2, 102);
        rig.World.Tick();

        // Assert
        RecordedSample[] all = rig.Recorder.Samples.ToArray();
        Assert.Equal(4, all.Length);
        Assert.Equal(new byte[] { 1, 3, 3, 1 }, all.Select(s => s.Slot));
        Assert.Equal(new uint[] { 1, 1, 2, 2 }, all.Select(s => s.SampleValue.Seq));
        Assert.Equal(new[] { 100, 300, 301, 102 }, all.Select(s => s.SampleValue.PosXMm));
        Assert.Equal(new uint[] { 0, 0, 0, 1 }, all.Select(s => s.ReceivedTick));
        Assert.Equal(new byte[] { 1, 3, 3, 1 }, all.Select(s => s.SampleValue.Slot));
    }

    private static void Write(RecRig rig, int slot, uint seq, int x)
    {
        var s = new TransformSample { Seq = seq, PosXMm = x };
        Assert.True(rig.World.TryWriteSample(PlayerSlot.FromValue((byte)slot), in s));
    }

    // ------------------------------------------------------------ overflow records

    [Fact]
    public void Overflow_IsRecordedForTheTickWhoseLogOverflowed_OnlyThen()
    {
        // Arrange: a log of 8 entries; tick 1 raises 20 events.
        RecRig? rig = null;
        rig = new RecRig(eventLog: 8, configure: b => b.Bind(PhaseId.MailStep, new ActionBinder(tick =>
        {
            if (tick == 1)
            {
                rig!.RaiseEvents(20);
            }
            else
            {
                rig!.RaiseEvents(2);
            }
        })));

        // Act
        rig.World.Tick();
        Assert.Equal(0, rig.Recorder.Overflows.Count);
        Assert.False(rig.Recorder.SawOverflow);
        rig.World.Tick();
        rig.World.Tick();

        // Assert
        Assert.Equal(1, rig.Recorder.Overflows.Count);
        Assert.Equal(1u, rig.Recorder.Overflows[0].Tick);
        Assert.True(rig.Recorder.SawOverflow);
        Assert.False(rig.Recorder.IsReproducible);
        Assert.Equal(0, rig.Recorder.LostRecords);
    }

    [Fact]
    public void Overflow_IsRecordedEvenWithNoSinkBound()
    {
        RecRig? rig = null;
        rig = new RecRig(eventLog: 8, configure: b => b.Bind(PhaseId.MailStep, new ActionBinder(_ => rig!.RaiseEvents(9))));

        rig.World.Tick();

        Assert.Equal(0u, Assert.Single(rig.Recorder.Overflows.ToArray()).Tick);
    }

    // ------------------------------------------------------------ interleaving across kinds

    [Fact]
    public void MixedSession_KeepsEveryKindInArrivalOrder_AndTickCountCoversTheRecords()
    {
        // Arrange + Act
        var rig = new RecRig();
        rig.Enqueue(2);
        Write(rig, 0, 1, 10);
        rig.World.Tick();
        rig.Enqueue(3);
        rig.World.Tick();
        rig.World.Tick();

        // Assert
        Assert.Equal(new uint[] { 0, 1 }, Enumerable.Range(0, rig.Recorder.Ops.Count).Select(i => rig.Recorder.Ops[i].BoundaryTick));
        Assert.Equal(new uint[] { 0, 1 }, Enumerable.Range(0, rig.Recorder.Drains.Count).Select(i => rig.Recorder.Drains[i].ApplyTick));
        Assert.Equal(0u, rig.Recorder.Samples[0].ReceivedTick);
        Assert.Equal(2u, rig.Recorder.TickCount); // derived: last record names tick 1; a Flush extends it
        rig.Recorder.Flush(rig.World.CurrentTick);
        Assert.Equal(3u, rig.Recorder.TickCount);
        rig.Recorder.Flush(1);
        Assert.Equal(3u, rig.Recorder.TickCount);
    }

    // ------------------------------------------------------------ dump-hash track

    [Fact]
    public void RecordDump_StoresTheSha256OfTheCanonicalDump_AtTheCompletedTickCount()
    {
        // Arrange
        var rig = new RecRig();

        // Act
        Assert.True(rig.Recorder.RecordDump(rig.World));
        rig.Enqueue(2);
        rig.World.Tick();
        Assert.True(rig.Recorder.RecordDump(rig.World));

        // Assert
        Assert.Equal(2, rig.Recorder.DumpHashes.Count);
        Assert.Equal(0u, rig.Recorder.DumpHashes[0].AtTick);
        Assert.Equal(1u, rig.Recorder.DumpHashes[1].AtTick);
        Assert.Equal(DumpHash.Sha256(rig.World), rig.Recorder.DumpHashes[1].Hash.ToHexString(), ignoreCase: true);
        Assert.NotEqual(rig.Recorder.DumpHashes[0].Hash, rig.Recorder.DumpHashes[1].Hash);
        Assert.Equal(1u, rig.Recorder.TickCount);
        Assert.Throws<ArgumentNullException>(() => rig.Recorder.RecordDump(null!));
    }

    [Fact]
    public void RecordDump_FromInsideATick_IsRefusedAndRecordsNothing()
    {
        // Arrange
        RecRig? rig = null;
        bool? result = null;
        rig = new RecRig(configure: b => b.Bind(PhaseId.MailStep, new ActionBinder(_ => result = rig!.Recorder.RecordDump(rig.World))));

        // Act
        rig.World.Tick();

        // Assert
        Assert.False(result);
        Assert.Equal(0, rig.Recorder.DumpHashes.Count);
    }

    [Fact]
    public void RecordDumpHash_AppendsAnExplicitRecord()
    {
        var recorder = new InMemoryRecorder(new RecordingHeader(1, default, default, 0, null));
        Hash256 h = Sha256.Hash("x"u8);

        recorder.RecordDumpHash(7, h);

        Assert.Equal(7u, recorder.DumpHashes[0].AtTick);
        Assert.Equal(h, recorder.DumpHashes[0].Hash);
        Assert.Equal(7u, recorder.TickCount);
    }

    // ------------------------------------------------------------ capacity and growth

    [Fact]
    public void Records_PastThePrimaryCapacity_SpillToScratch_AndFlushMovesThemInInOrder()
    {
        // Arrange: room for 2 ops, 3 in the scratch.
        var recorder = new InMemoryRecorder(new RecordingHeader(1, default, default, 0, null), new RecorderCapacity(2, 2, 2, 2, 2, 3));
        var op = new Op();

        // Act
        for (uint i = 0; i < 5; i++)
        {
            op.OpSeq = i + 1;
            recorder.OnEnqueued(i, in op);
        }

        // Assert: nothing lost, nothing grown yet.
        Assert.Equal(5, recorder.Ops.Count);
        Assert.Equal(2, recorder.Ops.Capacity);
        Assert.Equal(3, recorder.Ops.ScratchCount);
        Assert.Equal(0, recorder.LostRecords);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4 }, Enumerable.Range(0, 5).Select(i => recorder.Ops[i].BoundaryTick));

        // Act: between ticks.
        recorder.Flush(1);

        // Assert
        Assert.Equal(0, recorder.Ops.ScratchCount);
        Assert.True(recorder.Ops.Capacity >= 5 + 3);
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5 }, Enumerable.Range(0, 5).Select(i => recorder.Ops[i].Op.OpSeq));
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4 }, recorder.Ops.ToArray().Select(o => o.BoundaryTick));
        Assert.True(recorder.IsReproducible);

        // Records added after the flush continue the order.
        op.OpSeq = 6;
        recorder.OnEnqueued(5, in op);
        Assert.Equal(6u, recorder.Ops[5].Op.OpSeq);
    }

    [Fact]
    public void Records_PastPrimaryAndScratch_AreCounted_AndTheRecordingIsNotReproducible()
    {
        var recorder = new InMemoryRecorder(new RecordingHeader(1, default, default, 0, null), new RecorderCapacity(1, 1, 1, 1, 1, 1));
        var op = new Op();
        var reply = new Reply();
        var sample = new TransformSample();

        for (uint i = 0; i < 4; i++)
        {
            recorder.OnEnqueued(i, in op);
            recorder.OnDrained(i, in op, in reply);
            recorder.OnSample(i, PlayerSlot.FromValue(0), in sample);
            recorder.OnOverflow(i);
            recorder.RecordDumpHash(i, default);
        }

        Assert.Equal(2, recorder.Ops.Count);
        Assert.Equal(2L, recorder.Ops.Lost);
        Assert.Equal(2L, recorder.Drains.Lost);
        Assert.Equal(2L, recorder.Samples.Lost);
        Assert.Equal(2L, recorder.Overflows.Lost);
        Assert.Equal(2L, recorder.DumpHashes.Lost);
        Assert.Equal(10L, recorder.LostRecords);
        Assert.False(recorder.IsReproducible);
    }

    [Fact]
    public void Flush_GrowsAheadSoTheNextTickDoesNotSpill()
    {
        // Arrange: 8 slots, 6 used -> fewer than half free, so Flush doubles.
        var recorder = new InMemoryRecorder(new RecordingHeader(1, default, default, 0, null), new RecorderCapacity(8, 1, 1, 1, 1, 0));
        var op = new Op();
        for (uint i = 0; i < 6; i++)
        {
            recorder.OnEnqueued(i, in op);
        }

        // Act
        recorder.Flush(1);

        // Assert
        Assert.Equal(16, recorder.Ops.Capacity);
        Assert.Equal(6, recorder.Ops.Count);
    }

    [Fact]
    public void RecordList_IndexOutOfRange_Throws_AndToArrayCopiesInOrder()
    {
        var recorder = new InMemoryRecorder(new RecordingHeader(1, default, default, 0, null));
        var op = new Op();
        recorder.OnEnqueued(4, in op);
        recorder.OnEnqueued(5, in op);

        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Ops[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.Ops[-1]);
        Assert.Equal(new uint[] { 4, 5 }, recorder.Ops.ToArray().Select(o => o.BoundaryTick));
    }

    [Fact]
    public void Constructor_RejectsBadArguments()
    {
        var header = new RecordingHeader(1, default, default, 0, null);

        Assert.Throws<ArgumentNullException>(() => new InMemoryRecorder(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRecorder(header, new RecorderCapacity(0, 1, 1, 1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRecorder(header, new RecorderCapacity(1, 0, 1, 1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRecorder(header, new RecorderCapacity(1, 1, 0, 1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRecorder(header, new RecorderCapacity(1, 1, 1, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRecorder(header, new RecorderCapacity(1, 1, 1, 1, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRecorder(header, new RecorderCapacity(1, 1, 1, 1, 1, -1)));
    }

    // ------------------------------------------------------------ composition binding

    [Fact]
    public void Composition_BindsTheRecorderFromBuildArgs_AndNullBindsNone()
    {
        // Arrange
        ContentSet content = BuiltInContent.CreateM1a();
        var recorder = new InMemoryRecorder(RecordingHeader.Create(content, 5));

        // Act
        SimHost recorded = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, 5) { Recorder = recorder });
        SimHost plain = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, 5));
        var op = new Op { Kind = OpKind.PlayerJoin };
        op.SetPayload(new PlayerJoinPayload { Slot = 2 });
        Assert.True(recorded.World.EnqueueOp(ActorId.System, in op));
        Assert.True(plain.World.EnqueueOp(ActorId.System, in op));
        recorded.World.Tick();
        plain.World.Tick();

        // Assert
        Assert.Equal(1, recorder.Ops.Count);
        Assert.Equal(1, recorder.Drains.Count);
        Assert.True(recorder.Drains[0].Reply.IsOk);
        Assert.Equal(DumpHash.Sha256(plain.World), DumpHash.Sha256(recorded.World)); // a recorder does not change the state
    }
}

/// <summary>
/// The recorder's tick-path callbacks allocate nothing while within capacity, and nothing when spilling or losing records
/// (the 0 B guardrail applies to optimised builds, TD-008). The recorded world's enqueue, drain and sample paths are measured.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class RecorderAllocationTests
{
    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private static void Cycle(RecRig rig)
    {
        Op ok = RecRig.Make(2);
        Op bad = RecRig.Make(3);
        var sample = new TransformSample { Seq = 0, PosXMm = 5 };
        for (int i = 0; i < 4; i++)
        {
            rig.World.EnqueueOp(ActorId.System, in ok);
            rig.World.EnqueueOp(ActorId.System, in bad);
        }

        sample.Seq = (uint)rig.World.CurrentTick + 1;
        rig.World.TryWriteSample(PlayerSlot.FromValue(1), in sample);
        rig.World.Tick();
    }

    [Fact]
    public void TickWithAnInMemoryRecorder_WithinCapacity_AllocatesNothing()
    {
        SkipWhenUnoptimised();
        var rig = new RecRig(capacity: new RecorderCapacity(4096, 4096, 256, 16, 16, 16));

        AllocAssert.Zero("enqueue 8 ops + 1 sample + tick, recorder within capacity", () => Cycle(rig));

        Assert.True(rig.Recorder.Drains.Count >= 8);
        Assert.Equal(0, rig.Recorder.LostRecords);
        Assert.Equal(0, rig.Recorder.Ops.ScratchCount);
    }

    [Fact]
    public void TickWithAnInMemoryRecorder_SpillingAndLosingRecords_AllocatesNothing()
    {
        SkipWhenUnoptimised();
        var rig = new RecRig(capacity: new RecorderCapacity(2, 2, 1, 1, 1, 3));

        AllocAssert.Zero("enqueue 8 ops + 1 sample + tick, recorder spilling and losing", () => Cycle(rig));

        Assert.True(rig.Recorder.LostRecords > 0);
        Assert.True(rig.Recorder.Ops.ScratchCount > 0);
        Assert.Equal(2, rig.Recorder.Ops.Capacity); // no growth on the tick path
    }
}
