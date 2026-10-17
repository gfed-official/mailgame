using System.Buffers;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core.Replay;

/// <summary>Capacities of an <see cref="InMemoryRecorder"/>: one primary array per record kind, plus a shared scratch size per kind.</summary>
/// <param name="Ops">Enqueue records.</param>
/// <param name="Drains">Drain records.</param>
/// <param name="Samples">Sample records.</param>
/// <param name="Overflows">Overflow records.</param>
/// <param name="DumpHashes">Dump-hash records.</param>
/// <param name="Scratch">Records per kind that may spill past a full primary array within one tick; moved in by <c>Flush</c>.</param>
/// <example><code>var capacity = RecorderCapacity.Default with { Ops = 100_000 };</code></example>
public readonly record struct RecorderCapacity(int Ops, int Drains, int Samples, int Overflows, int DumpHashes, int Scratch)
{
    /// <summary>Generous defaults for a test session (a few thousand ops, ~1 MiB in total).</summary>
    public static RecorderCapacity Default => new(4096, 4096, 1024, 16, 256, 256);
}

/// <summary>
/// The M-1a in-memory <see cref="IOpRecorder"/> (ADR-0006 section 7, TR-sim-045). Preallocated at construction; the
/// <c>On*</c> callbacks (tick path) only write into preallocated arrays and never allocate. A record that would exceed the
/// primary capacity during a tick spills into a fixed scratch array; <see cref="Flush"/>, called between ticks, grows the
/// primary arrays and moves the spill in. Beyond the scratch a record is dropped and counted (<see cref="LostRecords"/>), and
/// the recording is then reported non-reproducible. Size the capacities for the session, and call <see cref="Flush"/> after
/// every tick (or every few) for open-ended sessions.
/// <para>
/// Records are kept per kind, each in arrival order; replay needs no global interleaving because every record carries its
/// tick (and ops their <c>opSeq</c>). The optional dump-hash track (<see cref="RecordDump"/>) lets a replay compare the world
/// state after every recorded tick. Not thread-safe: the tick thread only. The M5 file writer will serialise these records.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var header = RecordingHeader.Create(content, runSeed: 7);
/// var recorder = new InMemoryRecorder(header);
/// SimHost host = SimComposition.Build(new SimBuildArgs(profile, content, 7) { Recorder = recorder });
/// recorder.RecordDump(host.World);                // state before tick 0
/// host.World.EnqueueOp(actor, in op);
/// host.World.Tick();
/// recorder.RecordDump(host.World);                // immediately after the tick, before the next enqueue
/// recorder.Flush(host.World.CurrentTick);         // between ticks
/// ReplayResult result = ReplayDriver.Run(recorder, new SimBuildArgs(profile, content, 7));
/// </code>
/// </example>
public sealed class InMemoryRecorder : IOpRecorder
{
    private readonly ArrayBufferWriter<byte> _dumpBuffer = new();
    private uint _flushedTicks;
    private uint _derivedTicks;

    /// <summary>Creates a recorder with the <see cref="RecorderCapacity.Default"/> capacities.</summary>
    /// <param name="header">The header of the session (see <see cref="RecordingHeader.Create"/>).</param>
    public InMemoryRecorder(RecordingHeader header)
        : this(header, RecorderCapacity.Default)
    {
    }

    /// <summary>Creates a recorder and preallocates every array.</summary>
    /// <param name="header">The header of the session.</param>
    /// <param name="capacity">Capacities; each count must be at least 1 and <see cref="RecorderCapacity.Scratch"/> at least 0.</param>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A capacity is out of range.</exception>
    public InMemoryRecorder(RecordingHeader header, RecorderCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (capacity.Ops < 1 || capacity.Drains < 1 || capacity.Samples < 1 || capacity.Overflows < 1 || capacity.DumpHashes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Every record capacity must be at least 1.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(capacity.Scratch, nameof(capacity));
        Header = header;
        Ops = new RecordList<RecordedOp>(capacity.Ops, capacity.Scratch);
        Drains = new RecordList<RecordedDrain>(capacity.Drains, capacity.Scratch);
        Samples = new RecordList<RecordedSample>(capacity.Samples, capacity.Scratch);
        Overflows = new RecordList<RecordedOverflow>(capacity.Overflows, capacity.Scratch);
        DumpHashes = new RecordList<RecordedDumpHash>(capacity.DumpHashes, capacity.Scratch);
    }

    /// <summary>The session header.</summary>
    public RecordingHeader Header { get; }

    /// <summary>One record per accepted enqueue, in <c>opSeq</c> order.</summary>
    public RecordList<RecordedOp> Ops { get; }

    /// <summary>One record per drained op (accepted or rejected), in drain order.</summary>
    public RecordList<RecordedDrain> Drains { get; }

    /// <summary>One record per latched sample, in latch order.</summary>
    public RecordList<RecordedSample> Samples { get; }

    /// <summary>One record per tick whose event log overflowed.</summary>
    public RecordList<RecordedOverflow> Overflows { get; }

    /// <summary>The optional per-tick dump-hash track, in <see cref="RecordedDumpHash.AtTick"/> order.</summary>
    public RecordList<RecordedDumpHash> DumpHashes { get; }

    /// <summary>True when an <see cref="IOpRecorder.OnOverflow"/> was recorded: the dropped events may have mattered, so the recording is non-reproducible.</summary>
    public bool SawOverflow => Overflows.Count > 0 || Overflows.Lost > 0;

    /// <summary>Records dropped for lack of capacity (all kinds). Non-zero means the recording is incomplete.</summary>
    public long LostRecords => Ops.Lost + Drains.Lost + Samples.Lost + Overflows.Lost + DumpHashes.Lost;

    /// <summary>True when a replay may claim exact reproduction: no overflow was seen and no record was lost.</summary>
    public bool IsReproducible => !SawOverflow && LostRecords == 0;

    /// <summary>
    /// How many ticks the recorded session ran: the larger of the last <see cref="Flush"/> argument and one past the last tick
    /// any drain, sample, overflow or dump record names.
    /// </summary>
    public uint TickCount => Math.Max(_flushedTicks, _derivedTicks);

    /// <inheritdoc />
    public void OnEnqueued(uint boundaryTick, in Op op) => Ops.Add(new RecordedOp(boundaryTick, in op));

    /// <inheritdoc />
    public void OnDrained(uint applyTick, in Op op, in Reply reply)
    {
        Drains.Add(new RecordedDrain(applyTick, in op, in reply));
        Note(applyTick);
    }

    /// <inheritdoc />
    public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
    {
        Samples.Add(new RecordedSample(receivedTick, (byte)slot.Value, in sample));
        Note(receivedTick);
    }

    /// <inheritdoc />
    public void OnOverflow(uint tick)
    {
        Overflows.Add(new RecordedOverflow(tick));
        Note(tick);
    }

    /// <summary>
    /// Between ticks only: grows the primary arrays that spilled or are running low (allocates) and notes that
    /// <paramref name="ticksCompleted"/> ticks have run. Never call from inside a tick, a phase or a sink.
    /// </summary>
    /// <param name="ticksCompleted">The world's <c>CurrentTick</c> (the number of ticks run so far).</param>
    /// <example><code>world.Tick(); recorder.Flush(world.CurrentTick);</code></example>
    public void Flush(uint ticksCompleted)
    {
        _flushedTicks = Math.Max(_flushedTicks, ticksCompleted);
        Ops.Grow();
        Drains.Grow();
        Samples.Grow();
        Overflows.Grow();
        DumpHashes.Grow();
    }

    /// <summary>
    /// Appends a dump-hash record for the world's current state. Between ticks only, and by convention immediately after
    /// <c>Tick()</c> returns (or right after the build), before the next enqueue, so a replay that enqueues just before each
    /// tick sees the same state. Allocates (dump buffer, hashing): never on the tick path.
    /// </summary>
    /// <param name="world">The world being recorded.</param>
    /// <returns>False when the dump was refused (in a tick, or the world is not in a dumpable state); nothing is recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <example><code>world.Tick(); recorder.RecordDump(world);</code></example>
    public bool RecordDump(SimWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!DumpHashing.TryHash(world, _dumpBuffer, out Hash256 hash))
        {
            return false;
        }

        RecordDumpHash(world.CurrentTick, hash);
        return true;
    }

    /// <summary>Appends an explicit dump-hash record (a loader, or a test that needs a wrong hash).</summary>
    /// <param name="atTick">The ticks completed when the dump was taken.</param>
    /// <param name="hash">SHA-256 of the dump.</param>
    /// <example><code>recorder.RecordDumpHash(5, hash);</code></example>
    public void RecordDumpHash(uint atTick, Hash256 hash)
    {
        DumpHashes.Add(new RecordedDumpHash(atTick, hash));
        _derivedTicks = Math.Max(_derivedTicks, atTick);
    }

    private void Note(uint tick)
    {
        uint next = unchecked(tick + 1);
        if (next > _derivedTicks)
        {
            _derivedTicks = next;
        }
    }
}

/// <summary>Hashes a world's canonical dump with the Sim's own SHA-256 (no platform crypto; never called on the tick path).</summary>
internal static class DumpHashing
{
    /// <summary>Writes the dump into <paramref name="buffer"/> (cleared first) and hashes it.</summary>
    public static bool TryHash(SimWorld world, ArrayBufferWriter<byte> buffer, out Hash256 hash)
    {
        buffer.ResetWrittenCount();
        if (!world.TryWriteDump(buffer))
        {
            hash = default;
            return false;
        }

        hash = Sha256.Hash(buffer.WrittenSpan);
        return true;
    }
}
