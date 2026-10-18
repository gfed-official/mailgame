using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core.Replay;

/// <summary>
/// Replays an <see cref="InMemoryRecorder"/> recording (ADR-0006 section 7, TR-sim-045). A headless host (ADR-0002 section 5):
/// no <c>FrameRunner</c>, no clock; it builds a world with <see cref="SimComposition.Build"/> and loops
/// <see cref="SimWorld.Tick"/>.
/// <para>
/// <b>Header.</b> The caller supplies the <see cref="SimBuildArgs"/> it wants to replay on (the profile is the caller's; the
/// content, seed and counters are checked against the recording). The driver compares format version, settings hash, content
/// hash, run seed and initial counters with the recorded header; the first difference refuses the replay
/// (<see cref="ReplayOutcome.Refused"/>, <see cref="ReplayResult.Refusal"/> names the field) and no world is built.
/// </para>
/// <para>
/// <b>Schedule.</b> For each tick <c>T</c> from 0 to <see cref="InMemoryRecorder.TickCount"/> - 1, immediately before
/// <c>Tick()</c>: every recorded sample with <c>ReceivedTick == T</c> is written to the sample ring, then every recorded op with
/// <c>BoundaryTick == T</c> is re-enqueued in <c>opSeq</c> order with its recorded (already stamped) actor; replay bypasses the
/// adapter. The world re-stamps <c>opSeq</c> and each re-enqueue is checked against the record. During the tick every drain is
/// checked against its record (apply tick, op bytes, reply bytes). After the tick, any recorded dump hash for the new tick count
/// is compared with the hash of the replay world's dump.
/// </para>
/// <para>
/// <b>Boundary-tick rule.</b> An op's recorded boundary tick is the tick whose phase-0 drain first sees it: <c>CurrentTick</c>
/// for an enqueue between ticks or before the drain of the tick in progress (a phase-0 hook), <c>CurrentTick + 1</c> for an
/// enqueue made after that drain began (from a handler or a later phase, which the drain's captured batch cannot include).
/// Re-enqueuing before the boundary tick therefore reproduces the apply tick in both cases. Limitation: a mid-tick enqueue
/// after the drain is re-enqueued only before the next tick, so a dump-hash record taken at the end of that tick (queue
/// contents) will not match; the apply ticks still do. Ops that Sim code itself enqueues in a tick are consequences, not inputs,
/// and would be duplicated by a replay; no shipped system enqueues from inside a tick. Such an unexpected enqueue is counted in
/// <see cref="ReplayResult.EnqueueMismatches"/> and marks the replay diverged; it never throws.
/// </para>
/// <para>
/// <b>Samples.</b> The replay world's own latch is compared with the record: each latched sample must equal the next recorded one
/// (received tick, slot, bytes), and every sample written to the ring must be latched (a stale or reordered one is not), else
/// <see cref="ReplayResult.SampleMismatches"/>. A record whose received tick moved without changing the latch order cannot be
/// told apart from the original (the replay latches where the record says); only the dump hashes can catch that.
/// </para>
/// <para>
/// <b>Non-reproducible.</b> A recording that saw <c>OnOverflow</c> (or lost records) is replayed to completion and reported
/// <see cref="ReplayOutcome.NonReproducible"/>, not <see cref="ReplayOutcome.Diverged"/>; the counts are still filled in.
/// Allocates freely (a test or tool host); never run inside a tick. Not thread-safe: call on the thread that will tick.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ReplayResult result = ReplayDriver.Run(recorder, new SimBuildArgs(SimProfile.M1aGreybox, content, runSeed: 7));
/// if (result.Outcome != ReplayOutcome.Reproduced) { /* inspect result.Refusal / result.FirstDivergenceTick */ }
/// </code>
/// </example>
public static class ReplayDriver
{
    /// <summary>Replays <paramref name="recording"/> on a world built from <paramref name="args"/>.</summary>
    /// <param name="recording">The recording.</param>
    /// <param name="args">Profile, content, run seed and initial counters of the replay; <see cref="SimBuildArgs.Recorder"/> is replaced by the driver's own observer.</param>
    /// <returns>The result; <see cref="ReplayResult.Host"/> is null when refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recording"/> or <c>args.Content</c> is null.</exception>
    /// <exception cref="SimBuildException">The composition failed (a profile problem, not a header mismatch).</exception>
    /// <example><code>ReplayResult r = ReplayDriver.Run(recorder, new SimBuildArgs(profile, content, seed, counters));</code></example>
    public static ReplayResult Run(InMemoryRecorder recording, in SimBuildArgs args)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(args.Content);
        var result = new ReplayResult();

        var inputs = RecordingHeader.Create(args.Content, args.RunSeed, args.Counters, args.WorldSource);
        ReplayRefusal refusal = recording.Header.FirstMismatch(inputs);
        if (refusal != ReplayRefusal.None)
        {
            result.Outcome = ReplayOutcome.Refused;
            result.Refusal = refusal;
            return result;
        }

        var observer = new Observer(recording, result);
        SimBuildArgs replayArgs = args with { Recorder = observer };
        SimHost host = SimComposition.Build(in replayArgs);
        result.Host = host;
        Replay(recording, host.World, observer, result);

        result.Outcome = !recording.IsReproducible
            ? ReplayOutcome.NonReproducible
            : result.HasMismatch ? ReplayOutcome.Diverged : ReplayOutcome.Reproduced;
        return result;
    }

    private static void Replay(InMemoryRecorder rec, SimWorld world, Observer observer, ReplayResult result)
    {
        var dumpBuffer = new ArrayBufferWriter<byte>();
        int nextOp = 0;
        int nextSample = 0;
        int nextDump = 0;
        uint ticks = rec.TickCount;
        CompareDumps(rec, world, dumpBuffer, ref nextDump, result);
        while (world.CurrentTick < ticks)
        {
            uint tick = world.CurrentTick;
            observer.Tick = tick;
            ReplaySamples(rec, world, observer, result, tick, ref nextSample);
            ReplayOps(rec, world, observer, result, tick, ref nextOp);
            world.Tick();
            if (world.CurrentTick != tick + 1)
            {
                result.StoppedEarly = true;
                observer.Diverged(tick);
                break;
            }

            result.TicksRun++;
            CompareDumps(rec, world, dumpBuffer, ref nextDump, result);
        }

        observer.Finish();
    }

    // Writes every recorded sample due by `tick` to the sample ring (the world latches them in this tick's prologue).
    private static void ReplaySamples(InMemoryRecorder rec, SimWorld world, Observer observer, ReplayResult result, uint tick, ref int next)
    {
        while (next < rec.Samples.Count && rec.Samples[next].ReceivedTick <= tick)
        {
            ref readonly RecordedSample s = ref rec.Samples[next++];
            TransformSample sample = s.SampleValue;
            if (world.TryWriteSample(PlayerSlot.FromValue(s.Slot), in sample))
            {
                result.SamplesReplayed++;
            }
            else
            {
                result.SampleRefusals++;
                observer.Diverged(tick);
            }
        }
    }

    // Re-enqueues every recorded op whose boundary tick is `tick`, in opSeq order, checking each against its record.
    private static void ReplayOps(InMemoryRecorder rec, SimWorld world, Observer observer, ReplayResult result, uint tick, ref int next)
    {
        while (next < rec.Ops.Count && rec.Ops[next].BoundaryTick <= tick)
        {
            ref readonly RecordedOp r = ref rec.Ops[next];
            observer.ExpectEnqueue(next);
            Op op = r.Op;
            if (!world.EnqueueOp(op.Actor, in op) || !observer.EnqueueSeen)
            {
                result.EnqueueMismatches++;
                observer.Diverged(tick);
            }

            result.OpsReplayed++;
            next++;
        }
    }

    private static void CompareDumps(InMemoryRecorder rec, SimWorld world, ArrayBufferWriter<byte> buffer, ref int next, ReplayResult result)
    {
        uint now = world.CurrentTick;
        while (next < rec.DumpHashes.Count && rec.DumpHashes[next].AtTick <= now)
        {
            RecordedDumpHash recorded = rec.DumpHashes[next++];
            if (recorded.AtTick != now)
            {
                continue; // a record for a tick count this replay never stood at (cannot happen when TickCount covers it)
            }

            bool matched = DumpHashing.TryHash(world, buffer, out Hash256 hash) && hash == recorded.Hash;
            result.DumpHashesChecked++;
            if (!matched)
            {
                result.DumpHashMismatches++;
                result.FirstDivergenceTick ??= now;
            }
        }
    }

    private static bool SameBytes<T>(in T a, in T b)
        where T : unmanaged
    {
        ReadOnlySpan<byte> x = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in a), 1));
        ReadOnlySpan<byte> y = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in b), 1));
        return x.SequenceEqual(y);
    }

    // The replay world's recorder: compares every re-enqueue and every drain with its record as it happens.
    internal sealed class Observer : IOpRecorder
    {
        private readonly InMemoryRecorder _rec;
        private readonly ReplayResult _result;
        private int _expectedOp;
        private bool _armed;
        private int _nextDrain;
        private int _samplesSeen;

        public Observer(InMemoryRecorder rec, ReplayResult result)
        {
            _rec = rec;
            _result = result;
        }

        public uint Tick { get; set; }

        public bool EnqueueSeen { get; private set; }

        public void ExpectEnqueue(int index)
        {
            _expectedOp = index;
            _armed = true;
            EnqueueSeen = false;
        }

        public void Diverged(uint tick) => _result.FirstDivergenceTick ??= tick;

        public void Finish()
        {
            int unlatched = _result.SamplesReplayed - _samplesSeen;
            if (unlatched > 0)
            {
                _result.SampleMismatches += unlatched; // written to the ring but never latched (for example discarded as stale)
                Diverged(Tick);
            }

            int leftover = _rec.Drains.Count - _nextDrain;
            if (leftover > 0 && !_result.StoppedEarly)
            {
                _result.DrainCountMismatches += leftover;
                Diverged(_rec.Drains[_nextDrain].ApplyTick);
            }
        }

        public void OnEnqueued(uint boundaryTick, in Op op)
        {
            if (!_armed)
            {
                // An enqueue the driver did not make (Sim code enqueuing from inside a tick): it duplicates a recorded op, so the
                // replay is not faithful. Reported, never thrown.
                _result.EnqueueMismatches++;
                Diverged(Tick);
                return;
            }

            _armed = false;
            EnqueueSeen = true;
            ref readonly RecordedOp expected = ref _rec.Ops[_expectedOp];
            Op recordedOp = expected.Op;
            if (boundaryTick != expected.BoundaryTick || !SameBytes(in op, in recordedOp))
            {
                _result.EnqueueMismatches++;
                Diverged(Tick);
            }
        }

        public void OnDrained(uint applyTick, in Op op, in Reply reply)
        {
            if (_nextDrain >= _rec.Drains.Count)
            {
                _result.DrainCountMismatches++;
                Diverged(applyTick);
                return;
            }

            ref readonly RecordedDrain expected = ref _rec.Drains[_nextDrain++];
            Op recordedOp = expected.Op;
            Reply recordedReply = expected.Reply;
            _result.DrainsChecked++;
            if (expected.ApplyTick != applyTick)
            {
                _result.ApplyTickMismatches++;
                Diverged(applyTick);
            }
            else if (!SameBytes(in op, in recordedOp))
            {
                _result.OpMismatches++;
                Diverged(applyTick);
            }
            else if (!SameBytes(in reply, in recordedReply))
            {
                _result.ReplyMismatches++;
                Diverged(applyTick);
            }
        }

        public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
        {
            int index = _samplesSeen++;
            if (index >= _rec.Samples.Count)
            {
                _result.SampleMismatches++;
                Diverged(receivedTick);
                return;
            }

            ref readonly RecordedSample expected = ref _rec.Samples[index];
            TransformSample recorded = expected.SampleValue;
            if (expected.ReceivedTick != receivedTick || expected.Slot != slot.Value || !SameBytes(in sample, in recorded))
            {
                _result.SampleMismatches++;
                Diverged(receivedTick);
            }
        }

        public void OnOverflow(uint tick)
        {
        }
    }
}
