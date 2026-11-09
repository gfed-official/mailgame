namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// What a thrown landing did (<see cref="LandingResult.Kind"/>; mail-destinations story 008). The values mirror <see cref="EvalResult"/>,
/// so <c>default</c> is <see cref="Rejected"/>: a landing Mail did not resolve bounces whole. The name and values are uncredited
/// implementation detail.
/// </summary>
/// <example><code>bool bounced = r.Kind == LandingKind.Rejected;</code></example>
public enum LandingKind : byte
{
    /// <summary>Nothing consumed; the whole entry is the caller's.</summary>
    Rejected = 0,

    /// <summary>The right house: every unit paid.</summary>
    Accepted = 1,

    /// <summary>A wrong house: units misdelivered, oldest first, until the wallet floor.</summary>
    Misdelivered = 2,
}
