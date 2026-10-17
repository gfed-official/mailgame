using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The result of assigning ordinals to one def type's ids, created by <see cref="DefIdAssigner"/>.
/// Immutable. When <see cref="Status"/> is not <see cref="DefAssignmentStatus.Ok"/> both arrays are
/// empty and <see cref="Count"/> still carries the number of ids that were supplied.
/// </summary>
public sealed class DefAssignment
{
    internal DefAssignment(
        DefAssignmentStatus status,
        int count,
        ImmutableArray<string> sortedIds,
        ImmutableArray<DefIndex> indexOfInput)
    {
        Status = status;
        Count = count;
        SortedIds = sortedIds;
        IndexOfInput = indexOfInput;
    }

    /// <summary>Whether ordinals were assigned.</summary>
    public DefAssignmentStatus Status { get; }

    /// <summary>The number of ids supplied (for the "too many defs" diagnostic).</summary>
    public int Count { get; }

    /// <summary>The ids in <see cref="StringComparer.Ordinal"/> order; element <c>i</c> has <c>DefIndex</c> ordinal <c>i</c>. Empty unless <see cref="Status"/> is Ok.</summary>
    public ImmutableArray<string> SortedIds { get; }

    /// <summary>For each input position, the <see cref="DefIndex"/> assigned to that id. Empty unless <see cref="Status"/> is Ok.</summary>
    public ImmutableArray<DefIndex> IndexOfInput { get; }
}
