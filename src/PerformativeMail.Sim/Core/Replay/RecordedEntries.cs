using PerformativeMail.Sim.Content.Hashing;

namespace PerformativeMail.Sim.Core.Replay;

/// <summary>
/// One <see cref="IOpRecorder.OnEnqueued"/> record: an accepted op after <c>opSeq</c> and <c>Actor</c> were stamped, and the tick
/// whose phase-0 drain first sees it. A blittable value (no strings, no references) whose fields are directly addressable, so the
/// M5 file writer can serialise it field by field. Immutable.
/// </summary>
/// <example>
/// <code>
/// RecordedOp r = recorder.Ops[0];
/// uint boundary = r.BoundaryTick;   // the replay re-enqueues r.Op immediately before the tick with this number
/// </code>
/// </example>
public readonly struct RecordedOp
{
    /// <summary>Creates a record.</summary>
    /// <param name="boundaryTick">The tick whose drain first sees the op (see <see cref="IOpRecorder.OnEnqueued"/>).</param>
    /// <param name="op">The stamped op.</param>
    public RecordedOp(uint boundaryTick, in Op op)
    {
        BoundaryTick = boundaryTick;
        Op = op;
    }

    /// <summary>The tick whose phase-0 drain first sees the op; the replay enqueues it immediately before that tick.</summary>
    public uint BoundaryTick { get; }

    /// <summary>The op as queued: <see cref="Core.Op.OpSeq"/> and <see cref="Core.Op.Actor"/> are the stamped values.</summary>
    public Op Op { get; }
}

/// <summary>
/// One <see cref="IOpRecorder.OnDrained"/> record: a drained op (accepted or rejected), the tick whose phase 0 applied it and
/// the reply appended to the event log. Blittable and immutable.
/// </summary>
/// <example>
/// <code>
/// RecordedDrain d = recorder.Drains[0];
/// bool rejected = !d.Reply.IsOk;
/// </code>
/// </example>
public readonly struct RecordedDrain
{
    /// <summary>Creates a record.</summary>
    /// <param name="applyTick">The tick whose phase 0 applied the op.</param>
    /// <param name="op">The op as drained.</param>
    /// <param name="reply">The reply.</param>
    public RecordedDrain(uint applyTick, in Op op, in Reply reply)
    {
        ApplyTick = applyTick;
        Op = op;
        Reply = reply;
    }

    /// <summary>The tick whose phase 0 applied the op.</summary>
    public uint ApplyTick { get; }

    /// <summary>The op as drained.</summary>
    public Op Op { get; }

    /// <summary>The reply (a Core reject or the handler's).</summary>
    public Reply Reply { get; }
}

/// <summary>One <see cref="IOpRecorder.OnSample"/> record: an accepted (non-stale) transform sample and the tick that latched it. Blittable and immutable.</summary>
/// <example>
/// <code>
/// RecordedSample s = recorder.Samples[0];
/// world.TryWriteSample(PlayerSlot.FromValue(s.Slot), in s.SampleValue);   // what the replay does before tick s.ReceivedTick
/// </code>
/// </example>
public readonly struct RecordedSample
{
    /// <summary>Creates a record.</summary>
    /// <param name="receivedTick">The tick that latched the sample.</param>
    /// <param name="slot">The sender's raw slot value (0 to 7).</param>
    /// <param name="sample">The sample.</param>
    public RecordedSample(uint receivedTick, byte slot, in TransformSample sample)
    {
        ReceivedTick = receivedTick;
        Slot = slot;
        SampleValue = sample;
    }

    /// <summary>The tick whose prologue latched the sample.</summary>
    public uint ReceivedTick { get; }

    /// <summary>The sender's raw slot value (0 to 7).</summary>
    public byte Slot { get; }

    /// <summary>The sample as latched.</summary>
    public TransformSample SampleValue { get; }
}

/// <summary>One <see cref="IOpRecorder.OnOverflow"/> record: the tick whose event log overflowed. Blittable and immutable.</summary>
/// <example><code>uint tick = recorder.Overflows[0].Tick;</code></example>
public readonly struct RecordedOverflow
{
    /// <summary>Creates a record.</summary>
    /// <param name="tick">The tick.</param>
    public RecordedOverflow(uint tick)
    {
        Tick = tick;
    }

    /// <summary>The tick whose log overflowed.</summary>
    public uint Tick { get; }
}

/// <summary>
/// One entry of the optional per-tick dump-hash track: the SHA-256 of the canonical state dump taken between ticks, when
/// <see cref="AtTick"/> ticks had completed (the world's <c>CurrentTick</c>; 0 = before the first tick). Blittable and immutable.
/// </summary>
/// <example><code>recorder.RecordDump(world);   // immediately after world.Tick(), before the next enqueue</code></example>
public readonly struct RecordedDumpHash
{
    /// <summary>Creates a record.</summary>
    /// <param name="atTick">The world's <c>CurrentTick</c> when the dump was taken.</param>
    /// <param name="hash">SHA-256 of the dump bytes.</param>
    public RecordedDumpHash(uint atTick, Hash256 hash)
    {
        AtTick = atTick;
        Hash = hash;
    }

    /// <summary>The world's <c>CurrentTick</c> when the dump was taken (ticks completed).</summary>
    public uint AtTick { get; }

    /// <summary>SHA-256 of the canonical dump bytes.</summary>
    public Hash256 Hash { get; }
}
