using PerformativeMail.Sim.Composition;

namespace PerformativeMail.Sim.Core.Replay;

/// <summary>
/// What <see cref="ReplayDriver.Run"/> reports. When <see cref="Outcome"/> is <see cref="ReplayOutcome.Refused"/>,
/// <see cref="Refusal"/> names the header field and <see cref="Host"/> is null (no world was built). Otherwise the counts say
/// what was replayed and what differed. Read-only to callers (the driver fills it in).
/// </summary>
/// <example>
/// <code>
/// ReplayResult r = ReplayDriver.Run(recorder, args);
/// if (r.Outcome == ReplayOutcome.Refused) { Console.WriteLine(r.Refusal); }
/// </code>
/// </example>
public sealed class ReplayResult
{
    internal ReplayResult()
    {
    }

    /// <summary>How the replay ended.</summary>
    public ReplayOutcome Outcome { get; internal set; }

    /// <summary>The header field that differed (<see cref="ReplayRefusal.None"/> unless refused).</summary>
    public ReplayRefusal Refusal { get; internal set; }

    /// <summary>The replay world and its host, or null when refused.</summary>
    public SimHost? Host { get; internal set; }

    /// <summary>Ticks the replay ran.</summary>
    public uint TicksRun { get; internal set; }

    /// <summary>Ops re-enqueued.</summary>
    public int OpsReplayed { get; internal set; }

    /// <summary>Samples re-latched.</summary>
    public int SamplesReplayed { get; internal set; }

    /// <summary>Drains compared with their record.</summary>
    public int DrainsChecked { get; internal set; }

    /// <summary>Dump hashes compared with their record.</summary>
    public int DumpHashesChecked { get; internal set; }

    /// <summary>Re-enqueues refused by the world, or whose restamped op (opSeq, actor, boundary tick) differed from the record.</summary>
    public int EnqueueMismatches { get; internal set; }

    /// <summary>Samples the world refused to take.</summary>
    public int SampleRefusals { get; internal set; }

    /// <summary>Latched samples that differed from the next recorded one (received tick, slot or bytes), surplus latches, and written samples never latched.</summary>
    public int SampleMismatches { get; internal set; }

    /// <summary>Drains whose recomputed apply tick differed from the recorded one.</summary>
    public int ApplyTickMismatches { get; internal set; }

    /// <summary>Drains whose op differed from the recorded one (same apply tick).</summary>
    public int OpMismatches { get; internal set; }

    /// <summary>Drains whose reply differed from the recorded one.</summary>
    public int ReplyMismatches { get; internal set; }

    /// <summary>Recorded drains the replay never produced, plus replayed drains with no record.</summary>
    public int DrainCountMismatches { get; internal set; }

    /// <summary>Dump hashes that differed from the recorded track.</summary>
    public int DumpHashMismatches { get; internal set; }

    /// <summary>True when the replay stopped before the recorded tick count (the world could not tick).</summary>
    public bool StoppedEarly { get; internal set; }

    /// <summary>The first tick at which anything differed, or null.</summary>
    public uint? FirstDivergenceTick { get; internal set; }

    /// <summary>True when any mismatch count is non-zero or the replay stopped early.</summary>
    public bool HasMismatch =>
        EnqueueMismatches + SampleRefusals + SampleMismatches + ApplyTickMismatches + OpMismatches + ReplyMismatches + DrainCountMismatches + DumpHashMismatches > 0
        || StoppedEarly;
}
