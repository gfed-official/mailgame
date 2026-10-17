namespace PerformativeMail.Sim.Content;

/// <summary>
/// Outcome of <see cref="DefIdAssigner.Assign(ReadOnlySpan{string})"/>. When more than one problem
/// applies the first of this list wins: <see cref="TooManyDefs"/>, then <see cref="InvalidId"/>, then
/// <see cref="DuplicateIds"/>.
/// </summary>
public enum DefAssignmentStatus
{
    /// <summary>Every id was given an ordinal.</summary>
    Ok = 0,

    /// <summary>More than 65 534 ids: no indices are assigned (content validation CV-GEN-09). Checked on the count alone, before the ids are inspected.</summary>
    TooManyDefs = 1,

    /// <summary>The ids were not unique: no indices are assigned (content validation CV-GEN-01 should have caught it first).</summary>
    DuplicateIds = 2,

    /// <summary>An id was null or empty: no indices are assigned (content validation CV-GEN-02 should have caught it first).</summary>
    InvalidId = 3,
}
