using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The aim preview's quantised cache key (ADR-0015 section 8, ADR-0016 section 7; GDD T3; throwing-catching story 004): the query re-runs
/// only when one of these changes. Beyond the ADR's dir, origin, destination phase, wallet and static version it holds the other F1 and
/// <c>Evaluate</c> inputs (the selected entry, its units and weight class, the hand and <c>ThrowRangePermille</c>), so a changed entry can
/// never keep the previous verdict. Teammate positions join it at M0. Immutable; compared field by field.
/// </summary>
/// <example><code>bool rerun = !key.SameAs(in previous);</code></example>
internal readonly struct PreviewCacheKey
{
    public readonly short Dx;
    public readonly short Dy;
    public readonly short Dz;
    public readonly Vec3Mm Feet;
    public readonly uint DestinationVersion;
    public readonly long Wallet;
    public readonly int StaticVersion;
    public readonly EntryId Entry;
    public readonly byte Units;
    public readonly WeightClass WeightClass;
    public readonly sbyte Side;
    public readonly int ThrowRangePermille;

    public PreviewCacheKey(
        short dx, short dy, short dz, Vec3Mm feet, uint destinationVersion, long wallet, int staticVersion, EntryId entry, byte units,
        WeightClass weightClass, sbyte side, int throwRangePermille)
    {
        Dx = dx;
        Dy = dy;
        Dz = dz;
        Feet = feet;
        DestinationVersion = destinationVersion;
        Wallet = wallet;
        StaticVersion = staticVersion;
        Entry = entry;
        Units = units;
        WeightClass = weightClass;
        Side = side;
        ThrowRangePermille = throwRangePermille;
    }

    /// <summary>True when every field equals <paramref name="other"/>'s.</summary>
    /// <param name="other">The other key.</param>
    /// <returns>True when equal.</returns>
    public bool SameAs(in PreviewCacheKey other) =>
        Dx == other.Dx && Dy == other.Dy && Dz == other.Dz
        && Feet.X == other.Feet.X && Feet.Y == other.Feet.Y && Feet.Z == other.Feet.Z
        && DestinationVersion == other.DestinationVersion && Wallet == other.Wallet && StaticVersion == other.StaticVersion
        && Entry == other.Entry && Units == other.Units && WeightClass == other.WeightClass
        && Side == other.Side && ThrowRangePermille == other.ThrowRangePermille;
}
