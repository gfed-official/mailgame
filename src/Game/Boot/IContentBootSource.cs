using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Where boot step 2 gets the one <see cref="ContentSet"/> of the process (ADR-0010 section 4, CD21). M-1a uses
/// <see cref="BuiltInContentSource"/>; story 007 adds the M0 <c>ContentHost</c> as another source without editing the
/// sequence. Any exception shows the content error screen and the Sim is never composed.
/// </summary>
/// <example><code>ContentSet set = new BuiltInContentSource().Load();</code></example>
public interface IContentBootSource
{
    /// <summary>A short name of the source, logged by step 2 (for example <c>BuiltInContent</c>).</summary>
    string Name { get; }

    /// <summary>Builds the content set; called exactly once per boot.</summary>
    /// <returns>The validated content set.</returns>
    ContentSet Load();
}
