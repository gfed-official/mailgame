using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The static destination-row builder shared by the host's <see cref="DestinationRegistry"/> and the client's
/// <see cref="DestinationMirror"/> (mail-destinations stories 002 and 010; ADR-0016 section 1, ADR-0026): both build their rows from
/// <c>IWorldQuery.Lots</c> and the content's <c>DestinationTypeDef</c>s through these functions, so a destination's static facts
/// are produced by one copy of the code on both sides. Pure; the build-time members allocate, <see cref="Facts"/> does not.
/// </summary>
/// <example><code>(ulong[] masks, bool[] requires) = DestinationRows.BuildTypeFacts(content);</code></example>
internal static class DestinationRows
{
    /// <summary>
    /// Resolves the <c>house</c> type and its catch volume once, at build (no string compare at tick time). Every M-1a lot is a house.
    /// </summary>
    /// <param name="content">The content set; may be null only when <paramref name="lotCount"/> is 0.</param>
    /// <param name="lotCount">The number of lots.</param>
    /// <returns>The house type and its catch volume; defaults for no lots.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null while there are lots.</exception>
    /// <exception cref="ArgumentException">No <c>house</c> def, or its catch volume has a non-zero yaw or an extent of 0 or less.</exception>
    public static (DefIndex House, CatchVolume Volume) ResolveHouse(ContentSet? content, int lotCount)
    {
        if (lotCount == 0)
        {
            return (default, default);
        }

        ArgumentNullException.ThrowIfNull(content);
        DefIndex house = FindDefIndex(content, DestinationRegistry.HouseDefId);
        CatchVolume v = content.DestinationTypes[house].CatchVolume;
        if (v.YawMilliDeg != 0 || v.WidthMm <= 0 || v.HeightMm <= 0 || v.DepthMm <= 0)
        {
            throw new ArgumentException(
                "The house catch volume must have yaw 0 and positive extents so the world box is axis-aligned.", nameof(content));
        }

        return (house, v);
    }

    /// <summary>Per destination type: the accepted-kind bit mask (bit k = the kind with <c>DefIndex.Value</c> k; kinds 64 and up never set) and <c>RequiresUnit</c>.</summary>
    /// <param name="content">The content set; null gives empty tables.</param>
    /// <returns>The two tables, indexed by the type's <c>DefIndex.Value</c>.</returns>
    public static (ulong[] Masks, bool[] RequiresUnit) BuildTypeFacts(ContentSet? content)
    {
        if (content is null)
        {
            return (Array.Empty<ulong>(), Array.Empty<bool>());
        }

        ReadOnlySpan<DestinationTypeDef> types = content.DestinationTypes.AsSpan();
        var masks = new ulong[types.Length];
        var requires = new bool[types.Length];
        for (int t = 0; t < types.Length; t++)
        {
            ulong mask = 0UL;
            foreach (DefIndex kind in types[t].Accepts)
            {
                if (!kind.IsNone && kind.Value < 64)
                {
                    mask |= 1UL << kind.Value;
                }
            }

            masks[t] = mask;
            requires[t] = types[t].RequiresUnit;
        }

        return (masks, requires);
    }

    /// <summary>One destination's facts from its static columns, the type tables and a state. 0 B.</summary>
    /// <param name="id">The destination entity.</param>
    /// <param name="address">Its address.</param>
    /// <param name="type">Its type.</param>
    /// <param name="state">The state to report.</param>
    /// <param name="masks">The accepted-kind masks (<see cref="BuildTypeFacts"/>).</param>
    /// <param name="requiresUnit">The <c>RequiresUnit</c> table (<see cref="BuildTypeFacts"/>).</param>
    /// <returns>The facts; a type outside the tables accepts nothing.</returns>
    public static DestinationFacts Facts(
        EntityId id, AddressId address, DefIndex type, DestState state, ulong[] masks, bool[] requiresUnit)
    {
        ulong mask = 0UL;
        bool requires = false;
        if (!type.IsNone && type.Value < masks.Length)
        {
            mask = masks[type.Value];
            requires = requiresUnit[type.Value];
        }

        return new DestinationFacts(id, address, type, state, mask, requires);
    }

    private static DefIndex FindDefIndex(ContentSet content, string id)
    {
        ReadOnlySpan<DestinationTypeDef> defs = content.DestinationTypes.AsSpan();
        for (int i = 0; i < defs.Length; i++)
        {
            if (string.Equals(defs[i].Id, id, StringComparison.Ordinal))
            {
                return DefIndex.FromValue((ushort)i);
            }
        }

        throw new ArgumentException("The content set has no destination type '" + id + "'.", nameof(content));
    }
}
