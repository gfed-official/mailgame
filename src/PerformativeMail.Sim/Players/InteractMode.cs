namespace PerformativeMail.Sim.Players;

/// <summary>
/// How a verb is started (ADR-0014 section 1 and 3). Numbered from 1; 0 is never a valid registered value.
/// </summary>
/// <example><code>InteractMode m = VerbCatalogue.DefaultMode(Verb.Deliver);   // InteractMode.Hold</code></example>
public enum InteractMode : byte
{
    /// <summary>One press builds the request on that tick.</summary>
    Tap = 1,

    /// <summary>The press latches a hold; the request is built when the hold completes.</summary>
    Hold = 2,

    /// <summary>The press starts a channel (held until released), as Catch does.</summary>
    Channel = 3,
}
