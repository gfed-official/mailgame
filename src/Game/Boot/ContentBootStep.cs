using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Boot step 2, content (ADR-0019 section 3, ADR-0010 section 4, TR-content-021): loads the process's one
/// <see cref="ContentSet"/> from the <see cref="IContentBootSource"/> exactly once, places it in
/// <see cref="BootContext.Content"/> and logs the table counts, the content hash and the load time (advisory budget
/// 100 ms, TR-content-028: logged, not gated). A throw shows the content error screen (<see cref="BootFailureKind.Content"/>)
/// and the Sim is never composed.
/// </summary>
/// <param name="source">Where the content comes from (M-1a: <see cref="BuiltInContentSource"/>).</param>
/// <example><code>IBootStep step = new ContentBootStep(new BuiltInContentSource());</code></example>
public sealed class ContentBootStep(IContentBootSource source) : IBootStep
{
    private readonly IContentBootSource _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <inheritdoc />
    public int Number => BootPlan.ContentStep;

    /// <inheritdoc />
    public string Name => "content";

    /// <inheritdoc />
    public BootFailureKind FailureKind => BootFailureKind.Content;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The source returned null (any other exception is the source's own).</exception>
    public BootStepResult Run(BootContext ctx)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        ContentSet set = _source.Load()
            ?? throw new InvalidOperationException($"Content source {_source.Name} returned no content set.");
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        string hash = ContentHash.Compute(set).ToDisplayString();
        ctx.Log.Write(
            $"BOOT {Number} {Name}: source={_source.Name} containers={set.Containers.Count} items={set.Items.Count} " +
            $"mail_kinds={set.MailKinds.Count} destination_types={set.DestinationTypes.Count} mail_mixes={set.MailMixes.Count} " +
            $"hash={hash} load_ms={ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} (advisory budget 100)");

        // Placed last: a step that throws leaves no content behind (BootContext.Content is null on failure).
        ctx.Content = set;
        return BootStepResult.Ok;
    }
}
