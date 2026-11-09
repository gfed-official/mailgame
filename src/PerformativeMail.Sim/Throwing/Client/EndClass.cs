namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Where the aim preview's arc ends (GDD T3 colour and end marker; throwing-catching story 004). A mailbox class comes only from
/// <c>MailRules.Evaluate</c> on the first catch volume entered; the others come from the static receiver kind.
/// </summary>
/// <example><code>bool green = state.EndClass == EndClass.Accept;</code></example>
public enum EndClass : byte
{
    /// <summary>No arc (the preview is inactive).</summary>
    None = 0,

    /// <summary>Descending entry into a destination whose <c>Evaluate</c> is Accept.</summary>
    Accept = 1,

    /// <summary>Descending entry into a destination whose <c>Evaluate</c> is Misdeliver.</summary>
    Misdeliver = 2,

    /// <summary>Descending entry into a destination whose <c>Evaluate</c> is Reject (Closed, NotOnRoute or WontFit; see the state's reason).</summary>
    Reject = 3,

    /// <summary>The terrain.</summary>
    Ground = 4,

    /// <summary>The water plane.</summary>
    Water = 5,

    /// <summary>A world solid (house, roof, prop) or a map side.</summary>
    Blocked = 6,

    /// <summary>A belt lane (reserved, M-1b).</summary>
    Belt = 7,

    /// <summary>No contact by <c>maxFlightTicks</c>.</summary>
    Timeout = 8,
}
