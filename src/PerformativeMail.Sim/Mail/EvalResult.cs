namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The outcome class of <c>MailRules.Evaluate</c> (ADR-0017 section 4): the piece is refused, accepted at the right
/// address, or accepted at a wrong address.
/// </summary>
/// <example>
/// <code>
/// DeliverEval e = MailRules.Evaluate(in dest, in unit);
/// bool paid = e.Result == EvalResult.Accept;
/// </code>
/// </example>
public enum EvalResult : byte
{
    /// <summary>Refused; <see cref="DeliverEval.Reason"/> says why.</summary>
    Reject = 0,

    /// <summary>The destination takes the piece and the address matches.</summary>
    Accept = 1,

    /// <summary>The destination takes the piece but the address does not match.</summary>
    Misdeliver = 2,
}
