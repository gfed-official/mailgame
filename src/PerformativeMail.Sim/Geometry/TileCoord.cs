namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// A 2000 mm height-field tile index <c>(Tx, Tz)</c>, zero-based from the map's minimum corner.
/// </summary>
/// <example>
/// <code>
/// var t = new TileCoord(3, 12);
/// </code>
/// </example>
public readonly struct TileCoord : IEquatable<TileCoord>
{
    /// <summary>Tile index along X.</summary>
    public readonly int Tx;

    /// <summary>Tile index along Z.</summary>
    public readonly int Tz;

    /// <summary>Creates a tile coordinate.</summary>
    /// <param name="tx">Tile index along X.</param>
    /// <param name="tz">Tile index along Z.</param>
    public TileCoord(int tx, int tz)
    {
        Tx = tx;
        Tz = tz;
    }

    /// <inheritdoc />
    public bool Equals(TileCoord other) => Tx == other.Tx && Tz == other.Tz;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TileCoord other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return (Tx * 16777619) ^ Tz;
        }
    }

    /// <summary>Index-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(TileCoord left, TileCoord right) => left.Equals(right);

    /// <summary>Index-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(TileCoord left, TileCoord right) => !left.Equals(right);
}
