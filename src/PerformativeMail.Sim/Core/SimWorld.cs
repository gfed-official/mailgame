using System.Buffers;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The authoritative simulation host: owns the frozen phase table and runs the tick (ADR-0003 section 2).
/// Created only by <see cref="SimWorldBuilder.Freeze"/>. Not thread-safe: the thread that called
/// <c>Freeze()</c> is the owner thread and the only one that may call <see cref="Tick"/>, <see cref="Stop"/> or
/// the <see cref="IRunControl"/>; <c>SIM_ASSERT</c> builds throw <see cref="SimAssertException"/>
/// otherwise. Systems never receive a <see cref="SimWorld"/>; they receive <see cref="ITickReader"/>.
/// <para>
/// Lifecycle (ADR-0003 section 7): <c>Ready</c> (freeze) to <c>Running</c> (first <see cref="Tick"/>) to
/// <c>Stopped</c> or <c>Faulted</c>. A <see cref="Tick"/> in any other state, off the owner thread (Release) or
/// re-entrant counts <see cref="CounterId.BadTick"/> and runs nothing. An exception escaping a phase marks the
/// world <c>Faulted</c>, skips phase 13 and, in <c>SIM_ASSERT</c> builds, is rethrown.
/// </para>
/// <para>
/// <see cref="Tick"/> allocates nothing: it iterates arrays with <c>for</c>, with no LINQ or delegates.
/// Stories 020 and 021 fill the phase-0 drain and the sample prologue; phase 13 delivers the event log (Story 015).
/// </para>
/// </summary>
/// <example>
/// <code>
/// SimWorld world = new SimWorldBuilder().Freeze();
/// world.Tick();                       // tick 0
/// uint next = world.CurrentTick;      // 1
/// </code>
/// </example>
public sealed partial class SimWorld : ITickReader, IWorldStateReader
{
    private const PhaseId LastBindablePhase = PhaseId.RunStep;

    private readonly ISimPhase[][] _phases;
    private readonly ITickEndCheck[] _tickEnd;
    private readonly IPhaseProbe _probe;
    private readonly IRunControl _runControl;
    private readonly int _ownerThread;
    private readonly OpQueue _opQueue;
    private readonly SlotTable? _slots;
    private readonly IOpRecorder _recorder;
    private readonly SampleRing _sampleRing;
    private readonly SampleLatch _sampleLatch;
    private readonly EventLog _log;
    private readonly IEventSink? _sink;
    private readonly ushort[] _opKinds;
    private readonly OpDescriptor[] _opDescriptors;
    private readonly IOpHandler[] _opHandlers;
    private readonly ISimStateContributor[] _dumpSections;
    private readonly CanonicalBuffer _dumpScratch = new();
    private bool _dumping;
    private bool _delivering;
    private uint _lastOpSeq;
    private bool _inTick;
    private bool _drainBegun;
    private bool _stopLatched;
    private TickStage _stage;

    internal SimWorld(SimWorldParts parts)
    {
        _phases = parts.Phases;
        _tickEnd = parts.TickEnd;
        Settings = parts.Settings;
        Counters = parts.Counters;
        CheckRing = parts.CheckRing;
        _probe = parts.Probe;
        _runControl = new RunControlPort(this);
        _ownerThread = Environment.CurrentManagedThreadId;
        _opQueue = new OpQueue(Settings.OpQueueCapacity, Settings.SystemOpReserve);
        _log = new EventLog(Settings.EventLogCapacity, parts.EventDescriptors, Counters, parts.EventReaderIds, parts.EventReaderFilters);
        _sink = parts.EventSink;
        _slots = parts.CoreState?.Slots;
        _recorder = parts.Recorder;
        _sampleRing = new SampleRing(Settings.SampleRingCapacity);
        _sampleLatch = new SampleLatch(_sampleRing.Capacity);
        _opKinds = parts.OpKinds;
        _opDescriptors = parts.OpDescriptors;
        _declaredEventDescriptors = CompactEventDescriptors(parts.EventDescriptors);
        _opHandlers = parts.OpHandlers;
        for (int i = 0; i < _opHandlers.Length; i++)
        {
            if (_opHandlers[i] is PlayerLeaveHandler leave)
            {
                leave.AttachLatch(_sampleLatch); // the latch is the world's own, so the handler gets it here
            }
        }

        _lastOpSeq = parts.InitialCounters.LastOpSeq;
        _dumpSections = BuildDumpSections(parts);
        State = parts.InitialState;
        Stage = TickStage.BetweenTicks;
    }

    // Section 1 (Sim Core) first, then the bound contributors in registration order.
    private ISimStateContributor[] BuildDumpSections(SimWorldParts parts)
    {
        var sections = new ISimStateContributor[parts.StateContributors.Length + 1];
        sections[0] = new SimCoreStateContributor(this, parts.CoreState);
        Array.Copy(parts.StateContributors, 0, sections, 1, parts.StateContributors.Length);
        return sections;
    }

    /// <summary>Lifecycle state: <c>Ready</c> after freeze, <c>Running</c> from the first <see cref="Tick"/>, then <c>Stopped</c> or <c>Faulted</c>.</summary>
    public SimState State { get; private set; }

    /// <summary>The managed thread id captured at freeze (the owner thread); read by <c>FrameRunner</c> for its own owner-thread assert.</summary>
    internal int OwnerThreadId => _ownerThread;

    /// <summary>The tick being processed during <see cref="Tick"/>; the next tick to process between ticks (0 when <c>Ready</c>). Unchanged by a refused or faulted tick.</summary>
    public uint CurrentTick { get; private set; }

    /// <inheritdoc />
    public TickStage Stage
    {
        get => _stage;
        private set
        {
            _stage = value;
            _log.Stage = value; // the log checks the legal raise window against this
        }
    }

    /// <inheritdoc />
    public PhaseId Phase { get; private set; }

    /// <summary>The world's Sim Core settings.</summary>
    public SimCoreSettings Settings { get; }

    /// <summary>The world's always-on counters.</summary>
    public SimCounters Counters { get; }

    /// <summary>The world's always-on check ring.</summary>
    public SimCheckRing CheckRing { get; }

    /// <summary>The reason of the first accepted stop request; <see cref="StopReason.Unspecified"/> until one is latched.</summary>
    public StopReason StopReasonLatched { get; private set; }

    /// <summary>Read-only view of the op FIFO for the dump and tests. The only writers are <see cref="EnqueueOp"/> and the phase-0 drain (TD-031).</summary>
    internal OpQueueView Ops => new(_opQueue);

    /// <summary>The transform samples latched at this tick's prologue, for Player Character's phase 3 (read-only; slot order then sequence order).</summary>
    internal ISampleLatch Samples => _sampleLatch;

    /// <summary>The latch itself, for the dump contributor (per-slot last seq).</summary>
    internal SampleLatch SampleLatchState => _sampleLatch;

    /// <summary>
    /// Writes a transform sample into the sample ring (Story 021). Called by the host adapter on the Sim thread between ticks
    /// (or from inside a phase: it is then drained at the next tick's prologue); never from a transport callback. The sample's
    /// <see cref="TransformSample.Slot"/> is overwritten with <paramref name="slot"/> (the adapter stamps it from the peer).
    /// The ring is drained into the latch, stamped <c>receivedTick = CurrentTick</c>, at the start of the next <see cref="Tick"/>.
    /// Allocation-free; owner thread only (<c>SIM_ASSERT</c> builds throw <see cref="SimAssertException"/> otherwise).
    /// <para>
    /// Refusal returns false and counts <see cref="CounterId.SamplesRefused"/>: the ring is full (it never grows or overwrites),
    /// <paramref name="slot"/> is none, the world is not <c>Ready</c>/<c>Running</c>, or the call is off the owner thread (Release).
    /// </para>
    /// </summary>
    /// <param name="slot">The sender's slot.</param>
    /// <param name="sample">The sample; copied, not modified.</param>
    /// <returns>True when the sample was queued.</returns>
    /// <example><code>var s = new TransformSample { Seq = 1, PosXMm = 100 }; world.TryWriteSample(PlayerSlot.FromValue(0), in s);</code></example>
    public bool TryWriteSample(PlayerSlot slot, in TransformSample sample)
    {
        AssertOwnerThread();
        if (Environment.CurrentManagedThreadId != _ownerThread
            || slot.IsNone
            || (State != SimState.Ready && State != SimState.Running)
            || !_sampleRing.TryWrite(in sample))
        {
            Counters.Add(CounterId.SamplesRefused);
            return false;
        }

        // The ring slot just written is the newest; stamp the slot into the stored copy, not the caller's.
        _sampleRing.StampNewestSlot(slot.Value);
        return true;
    }

    /// <summary>The mutable op FIFO, for tests that need to dequeue or drop ops directly. Production code uses <see cref="Ops"/>.</summary>
    internal OpQueue OpsForTests => _opQueue;

    /// <summary>
    /// The per-tick event log, preallocated at <see cref="SimWorldBuilder.Freeze"/>. Internal: the composition root (Story 025)
    /// hands it to the systems that raise; Sim Core reads it in phase 13 and resets it afterwards.
    /// </summary>
    internal EventLog Events => _log;

    /// <summary>
    /// Looks up the registration and handler of an op kind by binary search over the frozen, sorted kind table. Allocation-free;
    /// for the drain (Story 020). Returns false for a kind nobody declared.
    /// </summary>
    /// <param name="kind">The op kind.</param>
    /// <param name="descriptor">The kind's descriptor, or null when not found.</param>
    /// <param name="handler">The kind's handler, or null when not found.</param>
    /// <returns>True when the kind is declared (and so has a handler).</returns>
    /// <example><code>if (world.TryGetOp(op.Kind, out var d, out var h)) { Reply r = h.Apply(in op, in ctx); }</code></example>
    internal bool TryGetOp(OpKind kind, out OpDescriptor descriptor, out IOpHandler handler)
    {
        ushort key = (ushort)kind;
        int lo = 0;
        int hi = _opKinds.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            ushort probe = _opKinds[mid];
            if (probe == key)
            {
                descriptor = _opDescriptors[mid];
                handler = _opHandlers[mid];
                return true;
            }

            if (probe < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        descriptor = null!;
        handler = null!;
        return false;
    }

    /// <summary>True only while <see cref="IEventSink.Deliver"/> is running (the Sim is closed to ops, ticks and raises).</summary>
    internal bool IsDelivering => _delivering;

    /// <summary>The last <c>opSeq</c> issued (0 when none yet). Never wraps: at <see cref="uint.MaxValue"/> every enqueue is refused.</summary>
    internal uint LastOpSeq => _lastOpSeq;

    /// <summary>
    /// The stop port, handed only to Run Structure by the composition root (ADR-0003 sections 4 and 5).
    /// Legal in phase 12; from any other point (between ticks, another phase, tick end or phase 13 inside
    /// <c>IEventSink.Deliver</c>) the request is latched as if raised in phase 12 and counted
    /// (<see cref="CounterId.StopOutOfPhase"/>); a second request is ignored and counted (<see cref="CounterId.StopIgnored"/>).
    /// </summary>
    internal IRunControl RunControl => _runControl;

    /// <summary>
    /// Runs one tick: phases 0 to 12, the tick-end checks, phase 13, then advances <see cref="CurrentTick"/>.
    /// The first call after <c>Ready</c> processes tick 0. Allocation-free. Owner thread only.
    /// <para>
    /// Counts <see cref="CounterId.BadTick"/> and runs nothing when the state is not <c>Ready</c>/<c>Running</c>,
    /// when called off the owner thread (<c>SIM_ASSERT</c> builds throw <see cref="SimAssertException"/>
    /// first), or when called re-entrantly from a phase. If a phase throws, the world becomes <c>Faulted</c>,
    /// phase 13 is skipped, <see cref="Stage"/> returns to <c>BetweenTicks</c> and <c>SIM_ASSERT</c> builds
    /// rethrow the original exception; other builds return normally. A stop latched in phase 12 is applied after
    /// phase 13 of the same tick.
    /// </para>
    /// </summary>
    /// <example><code>world.Tick();</code></example>
    public void Tick()
    {
        AssertOwnerThread(SimAssertCode.TickWrongThread);
        if (_inTick || Environment.CurrentManagedThreadId != _ownerThread || (State != SimState.Running && State != SimState.Ready))
        {
            Counters.Add(CounterId.BadTick);
            SimAssert.That(!_delivering, SimAssertCode.EventTickDuringFlush);
            return;
        }

        _inTick = true;
        try
        {
            RunTick();
        }
        catch (Exception)
        {
            // Fault path: empty the log without delivering it (ADR-0008 section 7), and reopen the Sim.
            _delivering = false;
            _log.Reset();
            State = SimState.Faulted;
#if SIM_ASSERT
            throw;
#endif
        }
        finally
        {
            _inTick = false;
            _drainBegun = false;
            Stage = TickStage.BetweenTicks;
            Phase = default;
        }
    }

    /// <summary>
    /// Writes the canonical state dump (ADR-0006 section 4, GDD R30) to <paramref name="output"/>: the header
    /// (<c>"PMSD"</c>, format version 1), Sim Core's section 1, then each bound <see cref="ISimStateContributor"/> in
    /// registration order. The same state always produces the same bytes; the Sim never hashes them (tests take SHA-256).
    /// <para>
    /// Legal only between ticks on a <c>Ready</c>, <c>Running</c> or <c>Stopped</c> world. Called from a phase, a tick-end
    /// check or <see cref="IEventSink.Deliver"/> it writes nothing, returns false and counts <see cref="CounterId.DumpDuringTick"/>;
    /// on a <c>Constructed</c> or <c>Faulted</c> world, or re-entrantly from a contributor, it writes nothing and returns false.
    /// Never on the tick path: it reuses the world's section buffer, so a warm dump allocates nothing beyond what
    /// <paramref name="output"/> itself needs. Owner thread only (<c>SIM_ASSERT</c> builds throw <see cref="SimAssertException"/>
    /// otherwise; other builds refuse). If a contributor throws, the exception propagates and <paramref name="output"/> holds a
    /// partial dump the caller must discard.
    /// </para>
    /// </summary>
    /// <param name="output">Receives the dump.</param>
    /// <returns>True when the whole dump was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    /// <example>
    /// <code>
    /// var buffer = new ArrayBufferWriter&lt;byte&gt;();
    /// if (world.TryWriteDump(buffer)) { byte[] hash = SHA256.HashData(buffer.WrittenSpan); }
    /// </code>
    /// </example>
    public bool TryWriteDump(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        AssertOwnerThread();
        if (Environment.CurrentManagedThreadId != _ownerThread)
        {
            return false;
        }

        if (_inTick || _delivering || _stage != TickStage.BetweenTicks)
        {
            Counters.Add(CounterId.DumpDuringTick);
            return false;
        }

        if (_dumping || (State != SimState.Ready && State != SimState.Running && State != SimState.Stopped))
        {
            return false;
        }

        _dumping = true;
        try
        {
            var writer = new CanonicalWriter(output, _dumpScratch);
            writer.WriteHeader();
            ISimStateContributor[] sections = _dumpSections;
            for (int i = 0; i < sections.Length; i++)
            {
                writer.BeginSection(sections[i].SectionId);
                sections[i].Write(ref writer);
                writer.EndSection();
            }
        }
        finally
        {
            _dumping = false;
        }

        return true;
    }

    /// <summary>
    /// Host stop outside a tick: moves <c>Running</c> to <c>Stopped</c> immediately (the only legal exit besides a
    /// fault). Refused (returns false, state unchanged) from <c>Constructed</c>, <c>Ready</c> (whose only exit is
    /// <c>Running</c>), <c>Stopped</c> and <c>Faulted</c>. Called from inside a tick it is refused and counted as
    /// <see cref="CounterId.StopOutOfPhase"/>, the same counter <see cref="IRunControl.RequestStop"/> uses for an
    /// out-of-phase call (ADR-0003 section 5); use <see cref="IRunControl"/> there. Owner thread only.
    /// </summary>
    /// <returns>True when the world was moved to <c>Stopped</c>.</returns>
    /// <example><code>if (world.Stop()) { /* world.State == SimState.Stopped */ }</code></example>
    public bool Stop()
    {
        AssertOwnerThread();
        if (_inTick)
        {
            Counters.Add(CounterId.StopOutOfPhase);
            return false;
        }

        if (State != SimState.Running)
        {
            return false;
        }

        State = SimState.Stopped;
        return true;
    }

    /// <summary>
    /// Appends a copy of <paramref name="op"/> to the op FIFO for phase 0 to drain (ADR-0007 section 2). The copy's
    /// <see cref="Op.Actor"/> is overwritten with <paramref name="actor"/> (the adapter stamps it from the peer; the incoming
    /// field and any payload content are never trusted) and its <see cref="Op.OpSeq"/> with the next sequence number (1, 2, ...,
    /// strictly increasing, never reused). Its queue boundary tick is <see cref="CurrentTick"/> at the call; the bound
    /// <see cref="IOpRecorder"/> is told the tick whose drain first sees the op (the next tick for an enqueue made after this
    /// tick's drain began). Allocation-free;
    /// owner thread only (<c>SIM_ASSERT</c> builds throw <see cref="SimAssertException"/> otherwise).
    /// <para>
    /// A refusal returns false, consumes no <c>opSeq</c>, stores and records nothing, and never throws. The Sim only
    /// returns false; the adapter replies <c>busy</c>. Checked in order: the world is not <c>Ready</c>/<c>Running</c>
    /// (<see cref="CounterId.EnqueueBadState"/>); a non-System actor with <c>Count &gt;= capacity - systemOpReserve</c>
    /// (<see cref="CounterId.BusyPlayer"/>); the ring completely full (<see cref="CounterId.BusySystem"/> plus one
    /// <see cref="CheckCode.BusySystem"/> record, for a System actor); the <c>opSeq</c> counter exhausted
    /// (<see cref="CounterId.OpSeqExhausted"/> plus one <see cref="CheckCode.OpSeqExhausted"/> record).
    /// </para>
    /// </summary>
    /// <param name="actor">Who issued the op; stored in <see cref="Op.Actor"/>.</param>
    /// <param name="op">The op to copy in; not modified.</param>
    /// <returns>True when the op was queued.</returns>
    /// <example>
    /// <code>
    /// var op = new Op { Kind = OpKind.TeleportAck, ReqId = 5 };
    /// if (!world.EnqueueOp(ActorId.FromEntity(player), in op)) { /* reply busy */ }
    /// </code>
    /// </example>
    public bool EnqueueOp(ActorId actor, in Op op)
    {
        AssertOwnerThread();
        if (State != SimState.Ready && State != SimState.Running)
        {
            Counters.Add(CounterId.EnqueueBadState);
            return false;
        }

        if (_delivering)
        {
            Counters.Add(CounterId.EnqueueDuringFlush);
            SimAssert.Fail(SimAssertCode.EventEnqueueDuringFlush);
            return false;
        }

        OpAdmission admission = _opQueue.Admit(actor.IsSystem);
        if (admission == OpAdmission.BusyPlayer)
        {
            Counters.Add(CounterId.BusyPlayer);
            return false;
        }

        if (admission == OpAdmission.BusySystem)
        {
            Counters.Add(CounterId.BusySystem);
            CheckRing.Record(CheckCode.BusySystem, CurrentTick, _opQueue.Count, _opQueue.Capacity);
            return false;
        }

        if (_lastOpSeq == uint.MaxValue)
        {
            Counters.Add(CounterId.OpSeqExhausted);
            CheckRing.Record(CheckCode.OpSeqExhausted, CurrentTick, _lastOpSeq, actor.Value);
            return false;
        }

        uint next = _lastOpSeq + 1;

        // Admit proved there is room, so Push cannot refuse here.
        _opQueue.Push(in op, actor, next, CurrentTick);
        _lastOpSeq = next;

        // The recorder is told the tick whose phase-0 drain first sees the op (RecordedOp.BoundaryTick): CurrentTick between ticks and
        // before the drain of the tick in progress (for example from a phase-0 hook), the NEXT tick once this tick's drain has
        // captured its batch. The queue's own boundary tick (dumped, ADR-0007) stays CurrentTick. See ReplayDriver.
        _recorder.OnEnqueued(_drainBegun ? unchecked(CurrentTick + 1) : CurrentTick, in _opQueue.PeekAt(_opQueue.Count - 1));
        return true;
    }

    private void RunTick()
    {
        if (State == SimState.Ready)
        {
            State = SimState.Running;
        }

        SamplePrologue();
        uint tick = CurrentTick;
        RunPhases(tick);
        RunTickEnd(tick);
        CloseLeakedScopes();
        RunEventsFlush();
        Stage = TickStage.BetweenTicks;
        CurrentTick = tick + 1;
        if (_stopLatched)
        {
            State = SimState.Stopped;
        }
    }

    private void RunPhases(uint tick)
    {
        Stage = TickStage.InPhase;
        for (int p = 0; p <= (int)LastBindablePhase; p++)
        {
            var phase = (PhaseId)p;
            var ctx = new TickContext(tick, phase);
            Phase = phase;
            _probe.Before(phase);
            if (phase == PhaseId.InputsApply)
            {
                InputsDrain();
            }

            ISimPhase[] binders = _phases[p];
            for (int i = 0; i < binders.Length; i++)
            {
                // One call scope per phase run: raises made by plain phase step code carry this run's CallSeq.
                using (_log.BeginCall())
                {
                    binders[i].Run(in ctx);
                }

                CloseLeakedScopes();
            }

            _probe.After(phase);
        }
    }

    private void RunTickEnd(uint tick)
    {
        Stage = TickStage.TickEnd;
        ITickEndCheck[] checks = _tickEnd;
        for (int i = 0; i < checks.Length; i++)
        {
            checks[i].Check(tick);
        }
    }

    private void RunEventsFlush()
    {
        Stage = TickStage.InPhase;
        Phase = PhaseId.EventsFlush;
        _probe.Before(PhaseId.EventsFlush);
        EventsFlush();
        _probe.After(PhaseId.EventsFlush);
    }

    /// <summary>
    /// Latches a stop. Outside phase 12 the request is counted (<see cref="CounterId.StopOutOfPhase"/>) but still latched as if
    /// raised in phase 12, which is ADR-0003 section 5 ("from any other phase it is latched as if raised in phase 12"). That
    /// includes phase 13: a stop requested from inside <c>IEventSink.Deliver</c> (which the sink contract forbids) is counted
    /// and latched, and applies after the same tick. TD-032 item 4 proposed returning early there; that would contradict the
    /// ADR wording, so the behaviour is documented and pinned by a test instead.
    /// </summary>
    private void LatchStop(StopReason reason)
    {
        AssertOwnerThread();
        bool inPhase12 = _inTick && Stage == TickStage.InPhase && Phase == LastBindablePhase;
        if (!inPhase12)
        {
            Counters.Add(CounterId.StopOutOfPhase);
        }

        if (_stopLatched || (State != SimState.Ready && State != SimState.Running))
        {
            Counters.Add(CounterId.StopIgnored);
            return;
        }

        _stopLatched = true;
        StopReasonLatched = reason;
    }

    [System.Diagnostics.Conditional("SIM_ASSERT")]
    private void AssertOwnerThread(ushort code = SimAssertCode.WrongThread) =>
        SimAssert.That(Environment.CurrentManagedThreadId == _ownerThread, code);

    /// <summary>
    /// Tick prologue (after the guard, before phase 0): drains the sample ring into the latch, stamping each accepted sample
    /// <c>receivedTick = CurrentTick</c> and reporting it to the recorder (Story 021). Allocation-free; owner thread only.
    /// </summary>
    private void SamplePrologue()
    {
        AssertOwnerThread();
        _sampleLatch.DrainFrom(_sampleRing, CurrentTick, Counters, _recorder);
    }

    /// <summary>
    /// Phase 13 built-in: calls <c>IEventSink.Deliver</c> once with the whole log (skipped when no sink is bound), then
    /// resets the log. While the sink runs <see cref="_delivering"/> is set: <c>EnqueueOp</c> is refused, a nested
    /// <c>Tick</c> is counted and raises are dropped. If the sink throws, the finally block still reopens the Sim and resets
    /// the log, and the exception reaches <see cref="Tick"/>'s fault path (world <c>Faulted</c>, rethrown under
    /// <c>SIM_ASSERT</c>), the same as an exception escaping a phase.
    /// </summary>
    private void EventsFlush()
    {
        _delivering = true;
        _log.Closed = true;
        try
        {
            IEventSink? sink = _sink;
            if (sink is not null)
            {
                // The log's live accumulator: a misuse drop made from inside Deliver also reaches this tick's info.
                sink.Deliver(CurrentTick, _log.Span, in _log.Overflow);
            }

            // After delivery, so a drop made from inside Deliver (a misuse) is included. The log is reset in the finally block below.
            if (_log.Overflow.Overflow)
            {
                _recorder.OnOverflow(CurrentTick);
            }
        }
        finally
        {
            _delivering = false;
            _log.Reset();
        }
    }

    /// <summary>After a phase run and at tick end every call scope must be closed; a leak (for example a discarded <c>log.BeginCall();</c>) asserts, and other builds reset the depth.</summary>
    private void CloseLeakedScopes()
    {
        SimAssert.That(_log.ScopeDepth == 0, SimAssertCode.EventScopeLeak);
        if (_log.ScopeDepth != 0)
        {
            Counters.Add(CounterId.ScopeLeak);
            _log.ResetScopeDepth();
        }
    }

    private sealed class RunControlPort : IRunControl
    {
        private readonly SimWorld _world;

        public RunControlPort(SimWorld world)
        {
            _world = world;
        }

        public void RequestStop(StopReason reason) => _world.LatchStop(reason);
    }
}
