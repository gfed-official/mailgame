namespace PerformativeMail.Sim.Core;

/// <summary>Coarse outcome of a <see cref="Reply"/> (ADR-0007 section 5). <c>Ok</c> exactly when the reason is 0.</summary>
/// <example>
/// <code>
/// bool accepted = reply.Result == ReplyResult.Ok;
/// </code>
/// </example>
public enum ReplyResult : byte
{
    /// <summary>The op was applied (possibly partially; details in <see cref="Reply.Extra"/>). Reason is 0.</summary>
    Ok = 0,

    /// <summary>The op was refused; <see cref="Reply.Reason"/> says why (non-zero).</summary>
    Rejected = 1,
}
