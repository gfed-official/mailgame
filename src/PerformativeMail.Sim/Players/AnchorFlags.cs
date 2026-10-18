namespace PerformativeMail.Sim.Players;

/// <summary>
/// Per-anchor flags (ADR-0014 section 1). A <c>byte</c> bit set, not <c>bool</c> fields, so registered structs keep an explicit
/// layout. Bits outside the three below are rejected at registration.
/// </summary>
/// <example><code>var flags = AnchorFlags.AllowWhileSwimming | AnchorFlags.BuildModeOnly;</code></example>
[Flags]
public enum AnchorFlags : byte
{
    /// <summary>No flag (the default for every verb).</summary>
    None = 0,

    /// <summary>The anchor stays eligible while the carrier swims (set explicitly by Open-on-pile and Pick-up-on-Spill-heap owners).</summary>
    AllowWhileSwimming = 1,

    /// <summary>The anchor is eligible only in the Build context; it registers under context mask 2.</summary>
    BuildModeOnly = 2,

    /// <summary>The anchor is a seat of its entity (reserved for M1 vehicles).</summary>
    SeatOf = 4,
}
