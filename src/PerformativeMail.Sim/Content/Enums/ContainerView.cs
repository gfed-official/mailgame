namespace PerformativeMail.Sim.Content;

/// <summary>
/// How a container is shown to the player (CD8, CD10). <c>view</c> is required with no default (CD10 owner ruling 2026-10-21), so <c>default(ContainerView)</c> is never a loaded value.
/// Closed enum (TR-content-012, ADR-0010 section 2): members are numbered from 1 in CD8 order, 0 is never a valid
/// loaded value, and a value is never renumbered, reordered or reused. The values are written as u16 by the
/// canonical encoding (content-data F6), so they are part of the content hash and frozen by
/// <c>ContentEnumTests</c>. Data spellings are in <see cref="ContainerViewSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = ContainerViewSpelling.TryParse("grid"u8, out ContainerView value);   // ok, value == ContainerView.Grid
/// </code>
/// </example>
public enum ContainerView : byte
{
    /// <summary>Shown as a grid. Data spelling <c>grid</c>.</summary>
    Grid = 1,

    /// <summary>Shown as a manifest list. Data spelling <c>manifest</c>.</summary>
    Manifest = 2,

    /// <summary>Chosen from the container layout. Data spelling <c>auto</c>.</summary>
    Auto = 3,
}
