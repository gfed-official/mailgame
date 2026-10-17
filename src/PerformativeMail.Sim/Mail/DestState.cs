namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// A destination's raw delivery state (Mail R3 / ADR-0017 key interfaces). This is the state before the post-close grace
/// window; the effective state with grace is story 003's (<c>MailRules</c>). Appended to the wire only as a byte.
/// </summary>
public enum DestState : byte
{
    /// <summary>Deliveries are open and the destination's district is unlocked.</summary>
    Open = 0,

    /// <summary>The district is unlocked but deliveries are closed.</summary>
    Closed = 1,

    /// <summary>The destination's district is not unlocked.</summary>
    Locked = 2,
}
