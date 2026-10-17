using System.Collections.Immutable;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Starting values for the world's monotonic counters (ADR-0003 section 4 <c>SimBuildArgs.counters</c>), so a test or
/// Free Play can begin near exhaustion without a seam. More counters are added here by later stories.
/// </summary>
/// <example>
/// <code>
/// // The very next enqueue gets opSeq 2^32 - 1; the one after is refused as exhausted.
/// var builder = new SimWorldBuilder(initialCounters: new SimInitialCounters { LastOpSeq = uint.MaxValue - 2 });
///
/// // Through the composition root: ids continue after the given last-issued values (TD-043 item 2: 0 = none issued).
/// var counters = new SimInitialCounters { LastMailId = 99, LastEntityCounters = ImmutableArray.Create(0u, 5u) };   // Player: 5
/// SimHost host = SimComposition.Build(new SimBuildArgs(profile, content, seed, counters));   // next player id counter 6
/// </code>
/// </example>
public sealed record class SimInitialCounters
{
    /// <summary>
    /// The last <c>opSeq</c> already issued (default 0, so the first accepted op gets 1; 0 means "none", ADR-0004 section 1).
    /// At <see cref="uint.MaxValue"/> every enqueue is refused: the counter never wraps.
    /// </summary>
    public uint LastOpSeq { get; init; }

    /// <summary>
    /// The last mail id already issued (default 0, so the first is 1). Seeds the world's <see cref="MailIds"/> when built by
    /// the composition root. At <see cref="uint.MaxValue"/> mail allocation is exhausted.
    /// </summary>
    public uint LastMailId { get; init; }

    /// <summary>
    /// The last entity counter already issued per <see cref="EntityClass"/>, indexed by class byte (default: empty, all 0).
    /// A shorter array leaves the remaining classes at 0; values above 2^24 - 1 clamp to exhausted. Seeds the world's
    /// <see cref="EntityIds"/> when built by the composition root.
    /// </summary>
    public ImmutableArray<uint> LastEntityCounters { get; init; }

    /// <summary><see cref="LastEntityCounters"/> as a span (empty when default).</summary>
    internal ReadOnlySpan<uint> EntityCountersSpan => LastEntityCounters.IsDefault ? default : LastEntityCounters.AsSpan();
}
