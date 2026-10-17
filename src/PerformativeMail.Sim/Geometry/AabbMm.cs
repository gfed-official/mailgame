namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// An axis-aligned box in integer millimetres, <see cref="Min"/> and <see cref="Max"/> both inclusive
/// corners (callers define whether a touching face counts; see ADR-0015 for the sweep rules).
/// </summary>
/// <example>
/// <code>
/// var house = new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(6000, 4000, 8000));
/// </code>
/// </example>
public readonly struct AabbMm : IEquatable<AabbMm>
{
    /// <summary>Minimum corner.</summary>
    public readonly Vec3Mm Min;

    /// <summary>Maximum corner.</summary>
    public readonly Vec3Mm Max;

    /// <summary>Creates a box.</summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    public AabbMm(Vec3Mm min, Vec3Mm max)
    {
        Min = min;
        Max = max;
    }

    /// <inheritdoc />
    public bool Equals(AabbMm other) => Min == other.Min && Max == other.Max;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AabbMm other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Min.X;
            hash = (hash * 16777619) ^ Min.Y;
            hash = (hash * 16777619) ^ Min.Z;
            hash = (hash * 16777619) ^ Max.X;
            hash = (hash * 16777619) ^ Max.Y;
            hash = (hash * 16777619) ^ Max.Z;
            return hash;
        }
    }

    /// <summary>Corner-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(AabbMm left, AabbMm right) => left.Equals(right);

    /// <summary>Corner-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(AabbMm left, AabbMm right) => !left.Equals(right);
}
