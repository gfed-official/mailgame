namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Why a complaint was raised (GDD R9: <c>source ∈ {misdelivery, late}</c>); the <c>Source</c> byte of <see cref="ComplaintRaisedPayload"/>.
/// Complaint source codes are None = 0, Misdelivery = 1 and Late = 2; new codes only go at the end (Mail Dev default, approved by Evan
/// 2026-11-03 9:15 PM PT by card click (TD-193 item 3)). Implementation detail (Mail Dev, not part of Evan's ruling): None is never
/// raised, like <c>CreditReason.None</c>.
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
