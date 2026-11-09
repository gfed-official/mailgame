namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// An <see cref="IImmediateActionSink"/> that does nothing (story 013): registered for a verb whose owner has no client code yet,
/// so its dispatch is logged as sent instead of <see cref="DropCause.NoSink"/>.
/// </summary>
/// <example><code>router.RegisterSink(ActionId.ShoulderSwap, NullImmediateSink.Instance);</code></example>
public sealed class NullImmediateSink : IImmediateActionSink
{
    private NullImmediateSink()
    {
    }

    /// <summary>The shared instance.</summary>
    public static NullImmediateSink Instance { get; } = new();

    /// <inheritdoc />
    public void OnImmediate(in ImmediateAction a)
    {
    }
}
