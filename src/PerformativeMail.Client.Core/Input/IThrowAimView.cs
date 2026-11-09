namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's read of Throwing's published <c>ThrowPreviewState</c> (story 014; throwing-catching story 004; GDD G1 and G2):
/// whether a throw can start and whether the entry latched at aim start has gone stale. Throwing implements it over its state;
/// the router reads it at event time and at every <see cref="InputRouter.FlushToLatch"/> and never writes it.
/// </summary>
/// <example><code>bool refused = view.NotThrowable != 0;   // "Hands slot", "Empty slot", "Swimming", …: the strings are HUD's</code></example>
public interface IThrowAimView
{
    /// <summary>
    /// Why no throw can start: 0 (Throwing's <c>NotThrowable.None</c>) when one can, else the reason id Throwing defines (hands
    /// slot, empty slot, swimming, …). Opaque to the router: a refused press carries it in its <see cref="CueId.NotThrowable"/> cue.
    /// </summary>
    byte NotThrowable { get; }

    /// <summary>True when the selected entry differs from the one latched at aim start, or is gone (Throwing's <c>EntryStale</c>).</summary>
    bool EntryStale { get; }
}
