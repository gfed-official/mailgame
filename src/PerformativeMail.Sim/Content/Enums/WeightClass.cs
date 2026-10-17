namespace PerformativeMail.Sim.Content;

/// <summary>
/// How heavy an item or container is for carrying and belt purposes (CD8).
/// Closed enum (TR-content-012, ADR-0010 section 2): members are numbered from 1 in CD8 order, 0 is never a valid
/// loaded value, and a value is never renumbered, reordered or reused. The values are written as u16 by the
/// canonical encoding (content-data F6), so they are part of the content hash and frozen by
/// <c>ContentEnumTests</c>. Data spellings are in <see cref="WeightClassSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = WeightClassSpelling.TryParse("light"u8, out WeightClass value);   // ok, value == WeightClass.Light
/// </code>
/// </example>
public enum WeightClass : byte
{
    /// <summary>Light. Data spelling <c>light</c>.</summary>
    Light = 1,

    /// <summary>Medium. Data spelling <c>medium</c>.</summary>
    Medium = 2,

    /// <summary>Heavy. Data spelling <c>heavy</c>.</summary>
    Heavy = 3,

    /// <summary>Bulk. Data spelling <c>bulk</c>.</summary>
    Bulk = 4,
}
