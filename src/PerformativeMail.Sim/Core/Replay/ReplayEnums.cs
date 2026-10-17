namespace PerformativeMail.Sim.Core.Replay;

/// <summary>
/// Which header field made <see cref="ReplayDriver.Run"/> refuse (no world is built). Append-only: values are explicit and never
/// renumbered or reused.
/// </summary>
/// <example><code>if (result.Refusal == ReplayRefusal.RunSeed) { /* the caller passed a different seed */ }</code></example>
public enum ReplayRefusal : byte
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>The recording's <c>formatVersion</c> is not the one this build reads.</summary>
    FormatVersion = 1,

    /// <summary>The SHA-256 of the <c>SimSettings</c> canonical encoding differs.</summary>
    SettingsHash = 2,

    /// <summary>The <c>ContentSet</c> hash differs (and the settings hash matches).</summary>
    ContentHash = 3,

    /// <summary>The run seed differs.</summary>
    RunSeed = 4,

    /// <summary>The initial counters (<c>LastOpSeq</c>, <c>LastMailId</c> or an entity counter) differ.</summary>
    InitialCounters = 5,
}

/// <summary>How a replay ended. Append-only: values are explicit.</summary>
/// <example><code>Assert.Equal(ReplayOutcome.Reproduced, result.Outcome);</code></example>
public enum ReplayOutcome : byte
{
    /// <summary>Every recomputed enqueue, apply tick, reply and (when present) dump hash matched.</summary>
    Reproduced = 1,

    /// <summary>The recording saw an event-log overflow (or lost records): replayed to completion but not claimed reproducible. Not a failure.</summary>
    NonReproducible = 2,

    /// <summary>The header differs from the replay inputs: <see cref="ReplayResult.Refusal"/> names the field; no world was built.</summary>
    Refused = 3,

    /// <summary>The replay ran but something differed (see the mismatch counts on <see cref="ReplayResult"/>).</summary>
    Diverged = 4,
}
