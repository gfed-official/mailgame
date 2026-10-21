using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Boot;

/// <summary>The M-1a content source: <see cref="BuiltInContent.CreateM1a"/> (ADR-0010 section 4).</summary>
/// <example><code>IContentBootSource source = new BuiltInContentSource();</code></example>
public sealed class BuiltInContentSource : IContentBootSource
{
    /// <inheritdoc />
    public string Name => nameof(BuiltInContent);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The built-in data fails its own validation (the message lists the findings).</exception>
    public ContentSet Load() => BuiltInContent.CreateM1a();
}
