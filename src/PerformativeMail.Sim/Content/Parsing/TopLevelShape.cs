namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>The JSON value a content file must start with. Values are append-only.</summary>
/// <example>
/// <code>
/// TopLevelShape shape = path.StartsWith("balance/", StringComparison.Ordinal) ? TopLevelShape.Object : TopLevelShape.Array;
/// </code>
/// </example>
public enum TopLevelShape : byte
{
    /// <summary>A top-level array: a def file (<c>[ {...}, ... ]</c>).</summary>
    Array = 1,

    /// <summary>A top-level object: a balance file (<c>{ "key": value, ... }</c>).</summary>
    Object = 2,
}
