namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// The four corner heights (int32 mm) of one 2000 mm height-field tile, the input of the
/// bilinear kernel <see cref="WorldHeight.Sample"/> (ADR-0013 section 6, ADR-0026 section 2).
/// Naming is <c>H[x][z]</c>: <c>H10</c> is the corner at +X, <c>H01</c> the corner at +Z.
/// </summary>
/// <example>
/// <code>
/// // FX-Beach: the low (-1000) edge is at z = 0, the shallow (-200) edge at z = 2000.
/// var h = new HeightCorners(-1000, -1000, -200, -200);
/// int mm = WorldHeight.Sample(in h, 500, 1000);   // -600
/// </code>
/// </example>
public readonly struct HeightCorners : IEquatable<HeightCorners>
{
    /// <summary>Height (mm) at the tile's (x = 0, z = 0) corner.</summary>
    public readonly int H00;

    /// <summary>Height (mm) at the tile's (x = TileSize, z = 0) corner.</summary>
    public readonly int H10;

    /// <summary>Height (mm) at the tile's (x = 0, z = TileSize) corner.</summary>
    public readonly int H01;

    /// <summary>Height (mm) at the tile's (x = TileSize, z = TileSize) corner.</summary>
    public readonly int H11;

    /// <summary>Creates a corner set.</summary>
    /// <param name="h00">Height at (x 0, z 0).</param>
    /// <param name="h10">Height at (x S, z 0).</param>
    /// <param name="h01">Height at (x 0, z S).</param>
    /// <param name="h11">Height at (x S, z S).</param>
    public HeightCorners(int h00, int h10, int h01, int h11)
    {
        H00 = h00;
        H10 = h10;
        H01 = h01;
        H11 = h11;
    }

    /// <inheritdoc />
    public bool Equals(HeightCorners other) =>
        H00 == other.H00 && H10 == other.H10 && H01 == other.H01 && H11 == other.H11;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HeightCorners other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = H00;
            hash = (hash * 16777619) ^ H10;
            hash = (hash * 16777619) ^ H01;
            hash = (hash * 16777619) ^ H11;
            return hash;
        }
    }

    /// <summary>Field-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when all four corners are equal.</returns>
    public static bool operator ==(HeightCorners left, HeightCorners right) => left.Equals(right);

    /// <summary>Field-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when any corner differs.</returns>
    public static bool operator !=(HeightCorners left, HeightCorners right) => !left.Equals(right);
}
