using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// An immovable world box (ADR-0026 section 5), created by <see cref="WorldTablesBuilder.AddSolid"/> and read through
/// <see cref="IWorldQuery.StaticSolids"/> / <see cref="IWorldQuery.QueryStaticSolids"/>. Field-wise equality.
/// </summary>
/// <example>
/// <code>
/// StaticSolid s = world.StaticSolids[3];
/// bool house = s.Kind == SolidKind.House;
/// </code>
/// </example>
public readonly struct StaticSolid : IEquatable<StaticSolid>
{
    /// <summary>Index in <see cref="IWorldQuery.StaticSolids"/> (ascending).</summary>
    public readonly ushort Index;

    /// <summary>Owning entity (prop, destination) or <see cref="EntityId.None"/>.</summary>
    public readonly EntityId Owner;

    /// <summary>The solid's box.</summary>
    public readonly AabbMm Box;

    /// <summary>The solid's category.</summary>
    public readonly SolidKind Kind;

    /// <summary>Creates a solid.</summary>
    /// <param name="index">Index in the solid table.</param>
    /// <param name="owner">Owning entity.</param>
    /// <param name="box">The box.</param>
    /// <param name="kind">The category.</param>
    public StaticSolid(ushort index, EntityId owner, AabbMm box, SolidKind kind)
    {
        Index = index;
        Owner = owner;
        Box = box;
        Kind = kind;
    }

    /// <inheritdoc />
    public bool Equals(StaticSolid other) =>
        Index == other.Index && Owner == other.Owner && Box == other.Box && Kind == other.Kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StaticSolid other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Index;
            hash = (hash * 16777619) ^ (int)Owner.Value;
            hash = (hash * 16777619) ^ Box.Min.X;
            hash = (hash * 16777619) ^ Box.Min.Y;
            hash = (hash * 16777619) ^ Box.Min.Z;
            hash = (hash * 16777619) ^ Box.Max.X;
            hash = (hash * 16777619) ^ Box.Max.Y;
            hash = (hash * 16777619) ^ Box.Max.Z;
            hash = (hash * 16777619) ^ (int)Kind;
            return hash;
        }
    }

    /// <summary>Field-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(StaticSolid left, StaticSolid right) => left.Equals(right);

    /// <summary>Field-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(StaticSolid left, StaticSolid right) => !left.Equals(right);
}
