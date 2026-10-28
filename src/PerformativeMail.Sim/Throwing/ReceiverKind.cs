namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// What a flight touched (GDD <c>design/gdd/throwing-catching.md</c> T6; ADR-0015 section 4). The numeric values are the
/// default ranks (armed catcher 0 · mailbox 1 · goon 2 · teammate 3 · belt 4 · solid 5 · ground 6 · water 7); the ranks
/// <see cref="FlightSweep"/> actually compares come from the content table through <see cref="ReceiverRanks"/>.
/// </summary>
/// <example>
/// <code>
/// bool landed = contact.Kind == ReceiverKind.Mailbox;
/// </code>
/// </example>
public enum ReceiverKind : byte
{
    /// <summary>An armed catcher's sphere (M0; story 023).</summary>
    ArmedCatcher = 0,

    /// <summary>A mailbox catch volume (descending entry only).</summary>
    Mailbox = 1,

    /// <summary>A goon cylinder (M-1b).</summary>
    Goon = 2,

    /// <summary>A teammate bonk cylinder (M0).</summary>
    Teammate = 3,

    /// <summary>A belt-lane top face (M-1b).</summary>
    Belt = 4,

    /// <summary>A world solid or one of the map's four vertical sides.</summary>
    Solid = 5,

    /// <summary>The terrain height field.</summary>
    Ground = 6,

    /// <summary>The sea-level water plane (y = 0) where the water depth is positive.</summary>
    Water = 7,
}
