namespace PerformativeMail.Sim.World;

/// <summary>
/// The axis a street's centre strip runs along (world-stub story 004). Decides "along the street" and "which side" in the
/// Mail R3 mailbox-spacing check of <see cref="WorldTablesBuilder"/>.
/// </summary>
/// <example><code>builder.AddStreet(1, 1, name, footprint, StreetAxis.X);   // the street runs along +X</code></example>
public enum StreetAxis : byte
{
    /// <summary>The street runs along X; its sides are +Z and -Z of the centre line.</summary>
    X = 0,

    /// <summary>The street runs along Z; its sides are +X and -X of the centre line.</summary>
    Z = 1,
}
