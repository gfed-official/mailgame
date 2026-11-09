namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The result of the <c>ThrowRelease</c> validation chain (ADR-0015 section 1; throwing-catching story 009): reason 0 when every check
/// passed, else the first failing check's reply reason, keyed <c>(ThrowRelease, Reason)</c>: Sim Core's <c>bad_request</c> (8) or a
/// <see cref="ThrowReason"/> (64 and up).
/// </summary>
/// <example><code>ThrowCheck c = ThrowValidator.Validate(in op, tick, in facts, out ThrowPlan plan); if (!c.IsOk) { return Reply.Reject(in op, c.Reason); }</code></example>
public readonly struct ThrowCheck
{
    /// <summary>Creates a check result.</summary>
    /// <param name="reason">0, or the reply reason.</param>
    public ThrowCheck(ushort reason)
    {
        Reason = reason;
    }

    /// <summary>A passing validation.</summary>
    public static ThrowCheck Ok => default;

    /// <summary>0 when valid, else the reply reason.</summary>
    public ushort Reason { get; }

    /// <summary>True when every check passed.</summary>
    public bool IsOk => Reason == 0;

    /// <summary>A rejection with <paramref name="reason"/>.</summary>
    /// <param name="reason">The Throwing reason.</param>
    /// <returns>The result.</returns>
    public static ThrowCheck Of(ThrowReason reason) => new((ushort)reason);
}
