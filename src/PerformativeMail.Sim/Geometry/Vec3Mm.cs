namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// A point or vector in integer millimetres (X east, Y up, Z south; ADR-0006 positions are int mm).
/// Shared by World, Player Character, Throwing and Grid Inventory. Widen to <see cref="long"/>
/// before the first multiply.
/// </summary>
/// <example>
/// <code>
/// var p = new Vec3Mm(1000, 0, -2500);
/// bool same = p == new Vec3Mm(1000, 0, -2500);   // true
/// </code>
/// </example>
public readonly struct Vec3Mm : IEquatable<Vec3Mm>
{
    /// <summary>X in mm.</summary>
    public readonly int X;

    /// <summary>Y (up) in mm.</summary>
    public readonly int Y;

    /// <summary>Z in mm.</summary>
    public readonly int Z;

    /// <summary>Creates a vector.</summary>
    /// <param name="x">X in mm.</param>
    /// <param name="y">Y in mm.</param>
    /// <param name="z">Z in mm.</param>
    public Vec3Mm(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <inheritdoc />
    public bool Equals(Vec3Mm other) => X == other.X && Y == other.Y && Z == other.Z;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vec3Mm other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = X;
            hash = (hash * 16777619) ^ Y;
            hash = (hash * 16777619) ^ Z;
            return hash;
        }
    }

    /// <summary>Component-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(Vec3Mm left, Vec3Mm right) => left.Equals(right);

    /// <summary>Component-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(Vec3Mm left, Vec3Mm right) => !left.Equals(right);
}
