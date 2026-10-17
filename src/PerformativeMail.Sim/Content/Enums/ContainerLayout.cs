namespace PerformativeMail.Sim.Content;

/// <summary>
/// How a container arranges its contents (CD8).
/// Closed enum (TR-content-012, ADR-0010 section 2): members are numbered from 1 in CD8 order, 0 is never a valid
/// loaded value, and a value is never renumbered, reordered or reused. The values are written as u16 by the
/// canonical encoding (content-data F6), so they are part of the content hash and frozen by
/// <c>ContentEnumTests</c>. Data spellings are in <see cref="ContainerLayoutSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = ContainerLayoutSpelling.TryParse("grid"u8, out ContainerLayout value);   // ok, value == ContainerLayout.Grid
/// </code>
/// </example>
public enum ContainerLayout : byte
{
    /// <summary>A cell grid. Data spelling <c>grid</c>.</summary>
    Grid = 1,

    /// <summary>A fixed list of slots. Data spelling <c>slots</c>.</summary>
    Slots = 2,

    /// <summary>An unordered pile. Data spelling <c>pile</c>.</summary>
    Pile = 3,
}
