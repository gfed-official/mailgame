using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Core.Net.Codec;

/// <summary>
/// The wire id maps of Mail's events (ADR-0017 sections 1 and 3; mail-destinations story 015): <c>DestinationOrdinal u16</c> (the lot
/// index, as the registry numbers its rows) to and from the destination's <c>EntityId</c> value, and <c>AddressOrdinal u16</c> (the index
/// in the ascending address table) to and from the <c>AddressId</c> value. Built once from <see cref="IWorldQuery.Lots"/>, so every peer
/// that built the same world tables has the same maps. Ids are narrowed only through these maps, never by truncation. Lookups are
/// binary searches and allocate 0 B. Immutable after construction; thread-safe for reads.
/// </summary>
/// <example>
/// <code>
/// var maps = new WorldIdMaps(world);
/// bool ok = maps.TryDestinationOrdinal(payload.Destination, out ushort ordinal);
/// </code>
/// </example>
public sealed class WorldIdMaps
{
    private readonly uint[] _entityByOrdinal;
    private readonly uint[] _entitySorted;
    private readonly ushort[] _ordinalOfSortedEntity;
    private readonly uint[] _addressByOrdinal;

    /// <summary>Builds the maps from the world's lots.</summary>
    /// <param name="world">The world tables.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    /// <exception cref="ArgumentException">More than 65,535 lots.</exception>
    public WorldIdMaps(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ReadOnlySpan<LotRecord> lots = world.Lots;
        if (lots.Length > ushort.MaxValue)
        {
            throw new ArgumentException("More than 65535 lots do not fit a u16 ordinal.", nameof(world));
        }

        _entityByOrdinal = new uint[lots.Length];
        _addressByOrdinal = new uint[lots.Length];
        for (int i = 0; i < lots.Length; i++)
        {
            _entityByOrdinal[i] = lots[i].Destination.Value;
            _addressByOrdinal[i] = lots[i].Address.Value;
        }

        Sort(_addressByOrdinal, new ushort[lots.Length]);
        _entitySorted = (uint[])_entityByOrdinal.Clone();
        _ordinalOfSortedEntity = new ushort[lots.Length];
        for (int i = 0; i < _ordinalOfSortedEntity.Length; i++)
        {
            _ordinalOfSortedEntity[i] = (ushort)i;
        }

        Sort(_entitySorted, _ordinalOfSortedEntity);
    }

    /// <summary>The number of destinations (and addresses).</summary>
    public int Count => _entityByOrdinal.Length;

    /// <summary>The <c>DestinationOrdinal</c> of a destination's <c>EntityId</c> value.</summary>
    /// <param name="entity">The entity id value.</param>
    /// <param name="ordinal">The ordinal when found.</param>
    /// <returns>False for an entity that is not a destination.</returns>
    public bool TryDestinationOrdinal(uint entity, out ushort ordinal)
    {
        int i = Find(_entitySorted, entity);
        ordinal = i >= 0 ? _ordinalOfSortedEntity[i] : (ushort)0;
        return i >= 0;
    }

    /// <summary>The destination <c>EntityId</c> value of an ordinal.</summary>
    /// <param name="ordinal">The ordinal.</param>
    /// <param name="entity">The entity id value when in range.</param>
    /// <returns>False for an ordinal outside the map.</returns>
    public bool TryDestination(ushort ordinal, out uint entity)
    {
        bool ok = ordinal < _entityByOrdinal.Length;
        entity = ok ? _entityByOrdinal[ordinal] : 0u;
        return ok;
    }

    /// <summary>The <c>AddressOrdinal</c> of an <c>AddressId</c> value.</summary>
    /// <param name="address">The address value.</param>
    /// <param name="ordinal">The ordinal when found.</param>
    /// <returns>False for an address no lot has.</returns>
    public bool TryAddressOrdinal(uint address, out ushort ordinal)
    {
        int i = Find(_addressByOrdinal, address);
        ordinal = i >= 0 ? (ushort)i : (ushort)0;
        return i >= 0;
    }

    /// <summary>The <c>AddressId</c> value of an ordinal.</summary>
    /// <param name="ordinal">The ordinal.</param>
    /// <param name="address">The address value when in range.</param>
    /// <returns>False for an ordinal outside the map.</returns>
    public bool TryAddress(ushort ordinal, out uint address)
    {
        bool ok = ordinal < _addressByOrdinal.Length;
        address = ok ? _addressByOrdinal[ordinal] : 0u;
        return ok;
    }

    // Build time: sorts the keys ascending, carrying the ordinals (ADR-0006 rule 5: the stable sort).
    private static void Sort(uint[] keys, ushort[] ordinals)
    {
        var data = new Keyed[keys.Length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new Keyed(keys[i], ordinals[i]);
        }

        StableSort.Sort<Keyed, ByKey>(data, new Keyed[data.Length], new ByKey());
        for (int i = 0; i < data.Length; i++)
        {
            keys[i] = data[i].Key;
            ordinals[i] = data[i].Ordinal;
        }
    }

    private static int Find(uint[] sorted, uint key)
    {
        int lo = 0;
        int hi = sorted.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (sorted[mid] == key)
            {
                return mid;
            }

            if (sorted[mid] < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }

    private readonly record struct Keyed(uint Key, ushort Ordinal);

    private readonly struct ByKey : IComparer<Keyed>
    {
        public int Compare(Keyed x, Keyed y) => x.Key.CompareTo(y.Key);
    }
}
