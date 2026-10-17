namespace PerformativeMail.Sim.Content;

/// <summary>
/// Marker for a content def record (ADR-0010 section 3, CD6). Every def is a <c>sealed record class</c> with init-only
/// members, identified at run time by its <see cref="PerformativeMail.Sim.Primitives.DefIndex"/> and never by record
/// equality or by <see cref="Id"/>. The synthesised record <c>Equals</c>/<c>GetHashCode</c>/<c>with</c> members exist
/// (the same as every other Sim record) but nothing relies on them: <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// members compare by reference, and content is hashed through the canonical encoder (F6), not through a hash code.
/// The reflection gate in <c>DefShapeTests</c> discovers the def set through this marker, so a def that does not
/// implement it escapes the gate; the same test fails any type named <c>*Def</c> in this namespace that omits it.
/// </summary>
/// <example>
/// <code>
/// public sealed record class FooDef : IDef
/// {
///     public required string Id { get; init; }
/// }
/// </code>
/// </example>
public interface IDef
{
    /// <summary>
    /// The def id, <c>^[a-z][a-z0-9_]{0,47}$</c>: used by the F6 encoding and by diagnostics. Never used for equality
    /// or lookup in the tick path (use the <see cref="PerformativeMail.Sim.Primitives.DefIndex"/>).
    /// </summary>
    string Id { get; }
}

/// <summary>
/// Marker for a small value nested in a def (a <c>readonly record struct</c>). Subject to the same reflection gate
/// as <see cref="IDef"/>.
/// </summary>
/// <example>
/// <code>
/// public readonly record struct Point2(int X, int Y) : IDefLeaf;
/// </code>
/// </example>
public interface IDefLeaf
{
}
