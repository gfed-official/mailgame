namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Why a complaint was raised (GDD R9: <c>source ∈ {misdelivery, late}</c>); the <c>Source</c> byte of <see cref="ComplaintRaisedPayload"/>.
/// 0 is reserved as "none" and never raised, like <c>CreditReason.None</c> (Mail Dev default, pending Evan (TD-193 item 3)). Append-only.
/// </summary>
/// <example><code>mail.RaiseComplaint(d, ComplaintSource.Misdelivery, amountPm);</code></example>
public enum ComplaintSource : byte
{
    /// <summary>No source; never raised.</summary>
    None = 0,

    /// <summary>A misdelivery's complaint (F9 door and team buckets).</summary>
    Misdelivery = 1,

    /// <summary>A late Accept's complaint (F9 team-wide late window).</summary>
    Late = 2,
}
