using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The destination registry (Mail and Destinations R3, TR-mail-006 registry half, TR-mail-018; ADR-0017 section 3,
/// ADR-0026 section 6). Built once from <see cref="IWorldQuery.Lots"/> while the sim is <c>Constructed</c>, after the World
/// has allocated every destination <see cref="EntityId"/> in lot order: Mail allocates no destination ids. Row <c>i</c> is lot
/// <c>i</c>, so <see cref="DestinationOrdinal"/> <c>i</c> is the lot index and the wire id for <c>destination u16</c>.
/// <para>
/// Struct-of-arrays columns, all sized to the lot count at build and frozen thereafter (no growth, no Dictionary): entity id,
/// address, type index, district, interaction anchor, catch volume (world AABB), feeder slot (empty at M-1a; M-1b feeders),
/// and the per-door complaint state <c>tokensPm</c> / <c>lastTick</c> (initialised 0 here; <see cref="ComplaintBuckets"/>, story 006,
/// fills every row's <c>tokensPm</c> at construction, Evan approved 2026-10-29 (TD-187 item 2)).
/// </para>
/// <para>
/// Lookups: <see cref="TryGetOrdinal(EntityId, out DestinationOrdinal)"/> is O(1) when the World allocated contiguous
/// ascending ids (it does: one class-7 counter per lot, in lot order; checked at build, <see cref="IdLookupIsComputed"/>),
/// else a binary search over a sorted copy. <see cref="TryGetOrdinal(AddressId, out DestinationOrdinal, out int)"/> is a binary
/// search over a sorted <c>uint[]</c> / <c>ushort[]</c> pair (at most 10 probes at 1,000 lots), used only for misdelivery
/// attribution and never inside <c>Evaluate</c>. Every lookup allocates 0 B.
/// </para>
/// <para>
/// Catch volume to world AABB (Throwing's landing test consumes <see cref="CatchVolumeOf"/>): the mailbox base is
/// <c>Anchor.Y - 1000</c> (ADR-0026: anchor y = ground + 1000 mm). The box is centred horizontally on the anchor and its
/// centre is <c>CentreHeightMm</c> above the base. The def's width runs along the lot's face (z for PosX/NegX, x for PosZ/NegZ)
/// and its depth along the face normal; height is y. Each extent is exact: <c>min = centre - extent / 2</c> (integer division),
/// <c>max = min + extent</c>. The def's yaw is not applied (it is 0 for every M-1a type; a non-axis-aligned yaw would break
/// the four-direction guarantee).
/// </para>
/// Not thread-safe. Dumped through <see cref="MailStateContributor"/>.
/// </summary>
/// <example>
/// <code>
/// var registry = new DestinationRegistry(world, content);
/// if (registry.TryGetDestinationFacts(lot.Destination, out DestinationFacts f)) { /* f.State */ }
/// </code>
/// </example>
internal sealed class DestinationRegistry
{
    /// <summary>The def id of the type every M-1a residential lot has (resolved to a <see cref="DefIndex"/> once, at build).</summary>
    public const string HouseDefId = "house";

    /// <summary>The <c>feederSlot</c> value that means no feeder (M-1a has none).</summary>
    public const ushort NoFeeder = 0xFFFF;

    /// <summary>The mailbox base lies this far below the anchor (ADR-0026: anchor y = ground + 1000 mm).</summary>
    public const int AnchorAboveBaseMm = 1000;

    private readonly EntityId[] _entity;
    private readonly AddressId[] _address;
    private readonly DefIndex[] _type;
    private readonly byte[] _district;
    private readonly Vec3Mm[] _anchor;
    private readonly AabbMm[] _catch;
    private readonly ushort[] _feederSlot;
    private readonly long[] _tokensPm;
    private readonly long[] _lastTick;

    // Per destination type (indexed by DefIndex.Value): the accepted-kind bit mask and RequiresUnit, precomputed at build.
    private readonly ulong[] _typeAcceptsMask;
    private readonly bool[] _typeRequiresUnit;

    // Reverse AddressId -> ordinal: sorted keys and the matching ordinals.
    private readonly uint[] _addressKeys;
    private readonly ushort[] _addressOrdinals;

    // EntityId -> ordinal: computed when ids are contiguous ascending, else a sorted copy.
    private readonly bool _idsComputed;
    private readonly uint _firstId;
    private readonly uint[] _idKeys;
    private readonly ushort[] _idOrdinals;

    /// <summary>
    /// Builds the registry from the world's lots, in lot order, with every lot typed <c>house</c> (read from
    /// <paramref name="content"/> by def id, here, never at tick time).
    /// </summary>
    /// <param name="world">The world; only <see cref="IWorldQuery.Lots"/> is read.</param>
    /// <param name="content">The accepted content set.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">See the span overload: lot count, duplicates, missing house def, bad catch volume.</exception>
    public DestinationRegistry(IWorldQuery world, ContentSet content)
        : this(Lots(world), content)
    {
    }

    /// <summary>
    /// Builds the registry from a lot span directly (the same build as the world overload; for fixtures that need more lots
    /// than a hand-authored world, such as the 1,000-lot probe test).
    /// </summary>
    /// <param name="lots">The lots in ascending address order.</param>
    /// <param name="content">The content set; may be null only when <paramref name="lots"/> is empty.</param>
    /// <exception cref="ArgumentException">More than 65 535 lots, duplicate addresses or destination ids, no <c>house</c> def, or a
    /// <c>house</c> catch volume with a non-zero yaw or an extent of 0 or less (the box would not be axis-aligned and positive).</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null while <paramref name="lots"/> is not empty.</exception>
    internal DestinationRegistry(ReadOnlySpan<LotRecord> lots, ContentSet? content)
    {
        int n = lots.Length;
        CheckLotCount(n);
        (DefIndex house, CatchVolume volume) = ResolveHouse(content, n);
        bool contiguous = IsContiguousAscending(lots);

        Deliveries = new DeliveriesState();
        (_typeAcceptsMask, _typeRequiresUnit) = BuildTypeFacts(content);
        _entity = new EntityId[n];
        _address = new AddressId[n];
        _type = new DefIndex[n];
        _district = new byte[n];
        _anchor = new Vec3Mm[n];
        _catch = new AabbMm[n];
        _feederSlot = new ushort[n];
        _tokensPm = new long[n];
        _lastTick = new long[n];
        _addressKeys = new uint[n];
        _addressOrdinals = new ushort[n];

        // The sorted id copy exists only for the fallback path: contiguous ids are computed and never searched.
        _idKeys = contiguous ? Array.Empty<uint>() : new uint[n];
        _idOrdinals = contiguous ? Array.Empty<ushort>() : new ushort[n];
        _idsComputed = contiguous;
        _firstId = n > 0 ? lots[0].Destination.Value : 0u;

        FillColumns(lots, house, volume, contiguous);
        SortPairs(_addressKeys, _addressOrdinals);
        if (!contiguous)
        {
            SortPairs(_idKeys, _idOrdinals);
        }

        ValidateUnique();
    }

    /// <summary>The number of destinations (the lot count).</summary>
    public int Count => _entity.Length;

    /// <summary>The deliveries state store (defaults: closed, close tick 0, District 1 unlocked); story 009 writes it.</summary>
    public DeliveriesState Deliveries { get; }

    /// <summary>True when the EntityId lookup is computed (contiguous ascending ids) rather than searched.</summary>
    public bool IdLookupIsComputed => _idsComputed;

    /// <summary>Builds a registry with no destinations, for tests of other Mail parts that need no world.</summary>
    /// <returns>The empty registry.</returns>
    public static DestinationRegistry CreateEmpty() => new(ReadOnlySpan<LotRecord>.Empty, null);

    /// <summary>The destination entity of a row.</summary>
    /// <param name="o">The ordinal; must be below <see cref="Count"/>.</param>
    /// <returns>The entity id.</returns>
    public EntityId EntityIdOf(DestinationOrdinal o) => _entity[o.Value];

    /// <summary>The packed address of a row.</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The address.</returns>
    public AddressId AddressOf(DestinationOrdinal o) => _address[o.Value];

    /// <summary>The <c>DestinationTypeDef</c> index of a row.</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The type index.</returns>
    public DefIndex TypeOf(DestinationOrdinal o) => _type[o.Value];

    /// <summary>The district id of a row.</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The district id.</returns>
    public byte DistrictOf(DestinationOrdinal o) => _district[o.Value];

    /// <summary>The interaction anchor of a row (equal to <c>Lots[i].Anchor</c>).</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The anchor in mm.</returns>
    public Vec3Mm AnchorOf(DestinationOrdinal o) => _anchor[o.Value];

    /// <summary>The world-space catch volume of a row (see the type remarks for the mapping).</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The axis-aligned box in mm.</returns>
    public AabbMm CatchVolumeOf(DestinationOrdinal o) => _catch[o.Value];

    /// <summary>The feeder slot of a row; <see cref="NoFeeder"/> at M-1a.</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The slot.</returns>
    public ushort FeederSlotOf(DestinationOrdinal o) => _feederSlot[o.Value];

    /// <summary>The door complaint bucket tokens of a row, permille-ticks (×window; <see cref="ComplaintBuckets"/> fills and writes it; 0 in a bare registry).</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>A reference to the column cell.</returns>
    public ref long TokensPm(DestinationOrdinal o) => ref _tokensPm[o.Value];

    /// <summary>The tick of the last door complaint-bucket touch of a row (<see cref="ComplaintBuckets"/>; 0 until the first touch).</summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>A reference to the column cell.</returns>
    public ref long LastTick(DestinationOrdinal o) => ref _lastTick[o.Value];

    /// <summary>Looks up a destination's ordinal. O(1) when <see cref="IdLookupIsComputed"/>, else a binary search; 0 B.</summary>
    /// <param name="dest">The destination entity.</param>
    /// <param name="ordinal">The ordinal when found.</param>
    /// <returns>True when <paramref name="dest"/> is a registered destination.</returns>
    public bool TryGetOrdinal(EntityId dest, out DestinationOrdinal ordinal)
    {
        uint v = dest.Value;
        if (_idsComputed)
        {
            uint off = unchecked(v - _firstId);
            if (off < (uint)_entity.Length)
            {
                ordinal = new DestinationOrdinal((ushort)off);
                return true;
            }

            ordinal = default;
            return false;
        }

        int lo = 0;
        int hi = _idKeys.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            uint k = _idKeys[mid];
            if (k == v)
            {
                ordinal = new DestinationOrdinal(_idOrdinals[mid]);
                return true;
            }

            if (k < v)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        ordinal = default;
        return false;
    }

    /// <summary>Looks up an address's ordinal (misdelivery attribution only; never in <c>Evaluate</c>); 0 B.</summary>
    /// <param name="address">The address.</param>
    /// <param name="ordinal">The ordinal when found.</param>
    /// <returns>True when a destination has exactly this address.</returns>
    public bool TryGetOrdinal(AddressId address, out DestinationOrdinal ordinal) =>
        TryGetOrdinal(address, out ordinal, out _);

    /// <summary>
    /// As <see cref="TryGetOrdinal(AddressId, out DestinationOrdinal)"/>, reporting the number of key comparisons made (the
    /// probe count; at most floor(log2 n) + 1, which is 10 at 1,000 lots).
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="ordinal">The ordinal when found.</param>
    /// <param name="probes">The comparisons made.</param>
    /// <returns>True when found.</returns>
    public bool TryGetOrdinal(AddressId address, out DestinationOrdinal ordinal, out int probes)
    {
        uint key = address.Value;
        int lo = 0;
        int hi = _addressKeys.Length - 1;
        probes = 0;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            probes++;
            uint k = _addressKeys[mid];
            if (k == key)
            {
                ordinal = new DestinationOrdinal(_addressOrdinals[mid]);
                return true;
            }

            if (k < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        ordinal = default;
        return false;
    }

    /// <summary>
    /// Raw state of a row, no grace window: <see cref="DestState.Closed"/> when deliveries are not open (even for a locked
    /// district: Mail R5 "Evaluate checks CLOSED before LOCKED"), else <see cref="DestState.Locked"/> when its district is not
    /// unlocked, else <see cref="DestState.Open"/>. Delegates to <see cref="DeliveriesStateRules.Effective"/> with no action
    /// start tick, so the ordering rule has one copy.
    /// </summary>
    /// <param name="o">The ordinal.</param>
    /// <returns>The raw state.</returns>
    public DestState RawStateOf(DestinationOrdinal o) =>
        DeliveriesStateRules.Effective(
            districtUnlocked: Deliveries.IsDistrictUnlocked(_district[o.Value]),
            openNow: Deliveries.Open,
            closeTick: Deliveries.CloseTick,
            actionStartTick: null,
            applyTick: 0u,
            closeGraceTicks: 0);

    /// <summary>
    /// The <c>IMailApi.TryGetDestinationFacts</c> signature (story 005's <c>IMailApi</c> delegates here): fills
    /// <see cref="DestinationFacts"/> for a registered destination; false and <c>default</c> for any other entity. 0 B.
    /// </summary>
    /// <param name="dest">The destination entity.</param>
    /// <param name="f">The facts when found.</param>
    /// <returns>True when registered.</returns>
    public bool TryGetDestinationFacts(EntityId dest, out DestinationFacts f)
    {
        if (!TryGetOrdinal(dest, out DestinationOrdinal o))
        {
            f = default;
            return false;
        }

        DefIndex type = _type[o.Value];
        ulong mask = 0UL;
        bool requiresUnit = false;
        if (!type.IsNone && type.Value < _typeAcceptsMask.Length)
        {
            mask = _typeAcceptsMask[type.Value];
            requiresUnit = _typeRequiresUnit[type.Value];
        }

        f = new DestinationFacts(_entity[o.Value], _address[o.Value], type, RawStateOf(o), mask, requiresUnit);
        return true;
    }

    // Build time only: one accepted-kind mask (bit k = the kind with DefIndex.Value k; kinds >= 64 are never set) per type.
    private static (ulong[] Masks, bool[] RequiresUnit) BuildTypeFacts(ContentSet? content)
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

    /// <summary>
    /// Writes the registry's columns after the mail rows (<see cref="MailStateContributor"/>): <c>u16 destinationCount</c>, then
    /// per ordinal: entityId u32, addressId u32, typeIndex u16 (<see cref="DefIndex.Value"/>), districtId u8, anchor x y z i32,
    /// catch box min x y z i32 then max x y z i32, feederSlot u16, tokensPm i64, lastTick i64 (65 bytes per row).
    /// </summary>
    /// <param name="w">The writer, inside the Mail section.</param>
    public void WriteCanonical(ref CanonicalWriter w)
    {
        w.WriteU16((ushort)_entity.Length);
        for (int i = 0; i < _entity.Length; i++)
        {
            w.WriteU32(_entity[i].Value);
            w.WriteU32(_address[i].Value);
            w.WriteU16(_type[i].Value);
            w.WriteU8(_district[i]);
            WriteVec(ref w, _anchor[i]);
            WriteVec(ref w, _catch[i].Min);
            WriteVec(ref w, _catch[i].Max);
            w.WriteU16(_feederSlot[i]);
            w.WriteI64(_tokensPm[i]);
            w.WriteI64(_lastTick[i]);
        }
    }

    /// <summary>
    /// Converts a catch volume to a world box for a lot (the mapping in the type remarks). Pure and deterministic.
    /// </summary>
    /// <param name="anchor">The lot's anchor.</param>
    /// <param name="face">The lot's facing.</param>
    /// <param name="v">The def's volume.</param>
    /// <returns>The axis-aligned box in mm.</returns>
    public static AabbMm ToWorldBox(Vec3Mm anchor, FaceDir face, CatchVolume v)
    {
        bool alongX = face == FaceDir.PosX || face == FaceDir.NegX;   // normal along x: depth is x, width is z
        int extentX = alongX ? v.DepthMm : v.WidthMm;
        int extentZ = alongX ? v.WidthMm : v.DepthMm;
        int centreY = anchor.Y - AnchorAboveBaseMm + v.CentreHeightMm;
        int minX = anchor.X - (extentX / 2);
        int minY = centreY - (v.HeightMm / 2);
        int minZ = anchor.Z - (extentZ / 2);
        return new AabbMm(new Vec3Mm(minX, minY, minZ), new Vec3Mm(minX + extentX, minY + v.HeightMm, minZ + extentZ));
    }

    private static void CheckLotCount(int n)
    {
        if (n > ushort.MaxValue)
        {
            throw new ArgumentException("More than 65535 lots do not fit a DestinationOrdinal.", "lots");
        }
    }

    /// <summary>Resolves the <c>house</c> type and its catch volume once, at build (no string compare at tick time).</summary>
    private static (DefIndex House, CatchVolume Volume) ResolveHouse(ContentSet? content, int lotCount)
    {
        if (lotCount == 0)
        {
            return (default, default);
        }

        ArgumentNullException.ThrowIfNull(content);
        DefIndex house = FindDefIndex(content, HouseDefId);
        CatchVolume v = content.DestinationTypes[house].CatchVolume;
        if (v.YawMilliDeg != 0 || v.WidthMm <= 0 || v.HeightMm <= 0 || v.DepthMm <= 0)
        {
            throw new ArgumentException(
                "The house catch volume must have yaw 0 and positive extents so the world box is axis-aligned.", nameof(content));
        }

        return (house, v);
    }

    /// <summary>True when destination ids run first, first + 1, first + 2 ... (an empty list is not contiguous).</summary>
    private static bool IsContiguousAscending(ReadOnlySpan<LotRecord> lots)
    {
        if (lots.Length == 0)
        {
            return false;
        }

        uint first = lots[0].Destination.Value;
        for (int i = 1; i < lots.Length; i++)
        {
            if (lots[i].Destination.Value != unchecked(first + (uint)i))
            {
                return false;
            }
        }

        return true;
    }

    private void FillColumns(ReadOnlySpan<LotRecord> lots, DefIndex house, CatchVolume volume, bool idsContiguous)
    {
        for (int i = 0; i < lots.Length; i++)
        {
            ref readonly LotRecord lot = ref lots[i];
            _entity[i] = lot.Destination;
            _address[i] = lot.Address;
            _type[i] = house;
            _district[i] = lot.DistrictId;
            _anchor[i] = lot.Anchor;
            _catch[i] = ToWorldBox(lot.Anchor, lot.Face, volume);
            _feederSlot[i] = NoFeeder;
            _addressKeys[i] = lot.Address.Value;
            _addressOrdinals[i] = (ushort)i;
            if (!idsContiguous)
            {
                _idKeys[i] = lot.Destination.Value;
                _idOrdinals[i] = (ushort)i;
            }
        }
    }

    /// <summary>Rejects duplicate addresses and ids over the sorted keys (contiguous ids are unique by construction).</summary>
    private void ValidateUnique()
    {
        for (int i = 1; i < _addressKeys.Length; i++)
        {
            if (_addressKeys[i] == _addressKeys[i - 1])
            {
                throw new ArgumentException("Duplicate AddressId in Lots.", "lots");
            }
        }

        for (int i = 1; i < _idKeys.Length; i++)
        {
            if (_idKeys[i] == _idKeys[i - 1])
            {
                throw new ArgumentException("Duplicate destination EntityId in Lots.", "lots");
            }
        }
    }

    private readonly record struct KeyedOrdinal(uint Key, ushort Ordinal);

    private readonly struct ByKey : IComparer<KeyedOrdinal>
    {
        public int Compare(KeyedOrdinal x, KeyedOrdinal y) => x.Key.CompareTo(y.Key);
    }

    /// <summary>Sorts the parallel arrays by key ascending (ADR-0006 rule 5: the stable sort, build time only).</summary>
    private static void SortPairs(uint[] keys, ushort[] ordinals)
    {
        var data = new KeyedOrdinal[keys.Length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new KeyedOrdinal(keys[i], ordinals[i]);
        }

        StableSort.Sort<KeyedOrdinal, ByKey>(data, new KeyedOrdinal[data.Length], new ByKey());
        for (int i = 0; i < data.Length; i++)
        {
            keys[i] = data[i].Key;
            ordinals[i] = data[i].Ordinal;
        }
    }

    private static void WriteVec(ref CanonicalWriter w, Vec3Mm p)
    {
        w.WriteI32(p.X);
        w.WriteI32(p.Y);
        w.WriteI32(p.Z);
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

    private static ReadOnlySpan<LotRecord> Lots(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Lots;
    }
}
