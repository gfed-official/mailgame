namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Why <c>MailRules.Evaluate</c> refused a piece. This is Mail's own enum, NOT the wire reply codes: Delivery declares
/// the wire reason codes and maps these onto them (ADR-0009 erratum 2026-10-16).
/// </summary>
/// <example>
/// <code>
/// if (e.Result == EvalResult.Reject &amp;&amp; e.Reason == MailRejectReason.Closed) { /* map to the wire code */ }
/// </code>
/// </example>
public enum MailRejectReason : byte
{
    /// <summary>No reason (the result is not a rejection).</summary>
    None = 0,

    /// <summary>Deliveries are closed (and any grace window is over).</summary>
    Closed = 1,

    /// <summary>The destination's district is locked.</summary>
    NotOnRoute = 2,

    /// <summary>The item is not mail, or the destination type does not accept this kind.</summary>
    WontFit = 3,
}
