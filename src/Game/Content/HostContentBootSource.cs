using PerformativeMail.Game.Boot;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Content;

/// <summary>
/// Boot step 2's opt-in host source (<c>--content=host</c> or <c>PM_CONTENT=host</c>): loads a <see cref="ContentHost"/> and
/// returns its set. A host that ends <see cref="ContentHostState.Failed"/> throws with every diagnostic, so step 2 shows the
/// content error screen and the Sim is never composed; it never falls back to built-in content (approved by Evan 2026-11-01
/// (TD-204 item 7)).
/// </summary>
/// <param name="host">The unloaded host boot constructed.</param>
/// <example><code>IBootStep step = new ContentSourceStep(new HostContentBootSource(new ContentHost()));</code></example>
public sealed class HostContentBootSource(ContentHost host) : IContentBootSource
{
    /// <summary>The host this source loads.</summary>
    public ContentHost Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

    /// <inheritdoc />
    public string Name => nameof(ContentHost);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The host ended <see cref="ContentHostState.Failed"/> (the message lists every
    /// diagnostic), or it had already loaded.</exception>
    public ContentSet Load()
    {
        if (Host.Load() == ContentHostState.Valid && Host.Set is ContentSet set)
        {
            return set;
        }

        throw new InvalidOperationException(
            $"The host content in {Host.Root} did not load ({Host.Diagnostics.Length} diagnostic(s)):\n" + string.Join("\n", Host.Diagnostics));
    }
}
