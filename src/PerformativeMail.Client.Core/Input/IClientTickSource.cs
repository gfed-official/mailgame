namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The ClientLayer-published client tick counter (ADR-0012 section 3). The router counts tick-based rules from it (F8 sprint
/// unlatch, story 012; the issuing tick of immediates, story 013), never from the ms clock.
/// </summary>
/// <example><code>uint tick = ticks.ClientTick;</code></example>
public interface IClientTickSource
{
    /// <summary>The client tick most recently stepped.</summary>
    uint ClientTick { get; }
}
