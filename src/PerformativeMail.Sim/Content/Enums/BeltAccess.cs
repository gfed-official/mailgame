namespace PerformativeMail.Sim.Content;

/// <summary>
/// Which sides of a container a belt or inserter may use (CD8, CD10). The data default is <see cref="None"/>, supplied by the field spec, never by <c>default(BeltAccess)</c>.
/// Closed enum (TR-content-012, ADR-0010 section 2): members are numbered from 1 in CD8 order, 0 is never a valid
/// loaded value, and a value is never renumbered, reordered or reused. The values are written as u16 by the
/// canonical encoding (content-data F6), so they are part of the content hash and frozen by
/// <c>ContentEnumTests</c>. Data spellings are in <see cref="BeltAccessSpelling"/>.
/// </summary>
/// <example>
/// <code>
/// bool ok = BeltAccessSpelling.TryParse("none"u8, out BeltAccess value);   // ok, value == BeltAccess.None
/// </code>
/// </example>
public enum BeltAccess : byte
{
    /// <summary>No belt access. Data spelling <c>none</c>.</summary>
    None = 1,

    /// <summary>A belt may use any side. Data spelling <c>any_side</c>.</summary>
    AnySide = 2,

    /// <summary>Only the loading face. Data spelling <c>loading_face</c>.</summary>
    LoadingFace = 3,

    /// <summary>Outfeed only. Data spelling <c>outfeed_only</c>.</summary>
    OutfeedOnly = 4,

    /// <summary>Inserters only. Data spelling <c>inserter_only</c>.</summary>
    InserterOnly = 5,
}
