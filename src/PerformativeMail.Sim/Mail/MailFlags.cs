namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The mail row <c>flags</c> byte (ADR-0017 section 2, GDD mail-destinations R2). Bits 0 and 1 are defined; bits 2 and 3 are
/// reserved for Priority and Fragile (not yet named, stored as written); bits 4 to 7 are unused.
/// </summary>
/// <example>
/// <code>
/// MailFlags f = MailFlags.Cursed | MailFlags.Found;   // (byte)f == 3
/// </code>
/// </example>
[Flags]
public enum MailFlags : byte
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>The piece is cursed (bit 0).</summary>
    Cursed = 1,

    /// <summary>The piece was found rather than dealt at the batch tick (bit 1).</summary>
    Found = 2,
}
