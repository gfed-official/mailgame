namespace PerformativeMail.Sim.Core;

/// <summary>
/// Sim Core's own settings sub-record (ADR-0003 section 4 injectable list; GDD F8 defaults). content-data's
/// <c>SimSettings</c> holds it as a member and validates ranges (SC-SET-1, M-1b); nothing is validated here.
/// </summary>
/// <example>
/// <code>
/// var settings = new SimCoreSettings { OpQueueCapacity = 1024 };
/// var builder = new SimWorldBuilder(settings);
/// </code>
/// </example>
public sealed record class SimCoreSettings
{
    /// <summary>Op FIFO capacity (default 4096).</summary>
    public int OpQueueCapacity { get; init; } = 4096;

    /// <summary>Op FIFO slots reserved for system requests (default 64).</summary>
    public int SystemOpReserve { get; init; } = 64;

    /// <summary>Most ops drained in phase 0 per tick (default 64).</summary>
    public int MaxOpsDrainedPerTick { get; init; } = 64;

    /// <summary>Event log capacity (default 4096).</summary>
    public int EventLogCapacity { get; init; } = 4096;

    /// <summary>Event log slots reserved for must-keep events (default 256).</summary>
    public int EventLogReserve { get; init; } = 256;

    /// <summary>Most event readers that can be bound (default 8).</summary>
    public int MaxEventReaders { get; init; } = 8;

    /// <summary>Most ticks the host runs per rendered frame (default 4).</summary>
    public int MaxCatchUpTicksPerFrame { get; init; } = 4;

    /// <summary>Capacity of the always-on <see cref="SimCheckRing"/> (default 256).</summary>
    public int CheckRingCapacity { get; init; } = 256;

    /// <summary>
    /// Capacity of the transform-sample ring (Story 021; a power of two, default 256: 8 players x about 4 frames of 15 Hz
    /// samples with headroom; PROPOSED). Also the most samples one tick can latch. A full ring refuses writes.
    /// </summary>
    public int SampleRingCapacity { get; init; } = 256;
}
