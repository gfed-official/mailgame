namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The result of <c>MailRules.Evaluate</c> (ADR-0017 key interfaces): an <see cref="EvalResult"/> and, for a rejection,
/// its <see cref="MailRejectReason"/>. A two-byte value; building one allocates nothing.
/// </summary>
/// <example>
/// <code>
/// DeliverEval r = DeliverEval.Reject(MailRejectReason.Closed);
/// bool rejected = r.Result == EvalResult.Reject;   // true; r.Reason == MailRejectReason.Closed
/// </code>
/// </example>
public readonly struct DeliverEval
{
    /// <summary>The outcome class.</summary>
    public readonly EvalResult Result;

    /// <summary>The rejection reason; <see cref="MailRejectReason.None"/> unless <see cref="Result"/> is a rejection.</summary>
    public readonly MailRejectReason Reason;

    /// <summary>Creates a result.</summary>
    /// <param name="result">The outcome class.</param>
    /// <param name="reason">The rejection reason (None unless rejected).</param>
    public DeliverEval(EvalResult result, MailRejectReason reason)
    {
        Result = result;
        Reason = reason;
    }

    /// <summary>An accepted delivery.</summary>
    /// <returns>Accept, no reason.</returns>
    public static DeliverEval Accept() => new DeliverEval(EvalResult.Accept, MailRejectReason.None);

    /// <summary>A misdelivery.</summary>
    /// <returns>Misdeliver, no reason.</returns>
    public static DeliverEval Misdeliver() => new DeliverEval(EvalResult.Misdeliver, MailRejectReason.None);

    /// <summary>A rejection.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>Reject with the reason.</returns>
    public static DeliverEval Reject(MailRejectReason reason) => new DeliverEval(EvalResult.Reject, reason);
}
