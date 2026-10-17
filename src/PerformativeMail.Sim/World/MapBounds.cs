namespace PerformativeMail.Sim.World;

/// <summary>Axis-aligned map extent in mm, |coord| at most 1,000,000 (ADR-0026 <c>Bounds</c>).</summary>
/// <example>
/// <code>
/// MapBounds b = world.Bounds;
/// bool inside = x &gt;= b.MinXMm &amp;&amp; x &lt;= b.MaxXMm;
/// </code>
/// </example>
public readonly struct MapBounds : IEquatable<MapBounds>
{
    /// <summary>Minimum X in mm.</summary>
    public readonly long MinXMm;

    /// <summary>Minimum Z in mm.</summary>
    public readonly long MinZMm;

    /// <summary>Maximum X in mm.</summary>
    public readonly long MaxXMm;

    /// <summary>Maximum Z in mm.</summary>
    public readonly long MaxZMm;

    /// <summary>Creates bounds.</summary>
    /// <param name="minXMm">Minimum X.</param>
    /// <param name="minZMm">Minimum Z.</param>
    /// <param name="maxXMm">Maximum X.</param>
    /// <param name="maxZMm">Maximum Z.</param>
    public MapBounds(long minXMm, long minZMm, long maxXMm, long maxZMm)
    {
        MinXMm = minXMm;
        MinZMm = minZMm;
        MaxXMm = maxXMm;
        MaxZMm = maxZMm;
    }

    /// <inheritdoc />
    public bool Equals(MapBounds other) =>
        MinXMm == other.MinXMm && MinZMm == other.MinZMm && MaxXMm == other.MaxXMm && MaxZMm == other.MaxZMm;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MapBounds other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Fold(MinXMm);
            hash = (hash * 16777619) ^ Fold(MinZMm);
            hash = (hash * 16777619) ^ Fold(MaxXMm);
            hash = (hash * 16777619) ^ Fold(MaxZMm);
            return hash;
        }
    }

    private static int Fold(long v) => unchecked((int)v ^ (int)(v >> 32));

    /// <summary>Field-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(MapBounds left, MapBounds right) => left.Equals(right);

    /// <summary>Field-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(MapBounds left, MapBounds right) => !left.Equals(right);
}
