using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Run.Events;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The client's destination mirror part (ADR-0016 section 1, ADR-0017 section 3; mail-destinations story 010): the static registry
/// rows, built once from the injected <see cref="IWorldQuery.Lots"/> and the content's <c>DestinationTypeDef</c>s through the same
/// <see cref="DestinationRows"/> helper as the host's registry (row <c>i</c> is lot <c>i</c>, so the index is the
/// <see cref="DestinationOrdinal"/>), plus the deliveries state from the replicated <see cref="RunEventKinds.DeliveriesState"/>.
/// Before the first event it reports the host's defaults: closed, close tick 0, District 1 unlocked. Fed only by <c>MirrorDrain</c>;
/// it never reads a <c>SimWorld</c> or a host table. <see cref="Apply"/> and the lookups allocate 0 B. Client thread only.
/// </summary>
/// <example>
/// <code>
/// var mirror = new DestinationMirror(world, content);
/// builder.BindMirrorPart(DestinationMirror.Kinds, mirror);
/// bool found = MailClientFacts.TryDestination(mirror, dest, holdStart, estimatedHostTick, balance.CloseGraceTicks, out DestinationFacts d);
/// </code>
/// </example>
public sealed class DestinationMirror : IMirrorPart
{
    private readonly EntityId[] _entity;
    private readonly AddressId[] _address;
    private readonly DefIndex[] _type;
    private readonly byte[] _district;
    private readonly ulong[] _typeAcceptsMask;
    private readonly bool[] _typeRequiresUnit;
    private readonly uint _firstId;
    private readonly bool _idsContiguous;

    /// <summary>Builds the static rows.</summary>
    /// <param name="world">The world tables (only <see cref="IWorldQuery.Lots"/> is read).</param>
    /// <param name="content">The accepted content set.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The content has no usable <c>house</c> type (as the host registry).</exception>
    public DestinationMirror(IWorldQuery world, ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ReadOnlySpan<LotRecord> lots = world.Lots;
        int n = lots.Length;
        (DefIndex house, _) = DestinationRows.ResolveHouse(content, n);
        (_typeAcceptsMask, _typeRequiresUnit) = DestinationRows.BuildTypeFacts(content);
        _entity = new EntityId[n];
        _address = new AddressId[n];
        _type = new DefIndex[n];
        _district = new byte[n];
        bool contiguous = n > 0;
        for (int i = 0; i < n; i++)
        {
            _entity[i] = lots[i].Destination;
            _address[i] = lots[i].Address;
            _type[i] = house;
            _district[i] = lots[i].DistrictId;
            contiguous &= lots[i].Destination.Value == unchecked(lots[0].Destination.Value + (uint)i);
        }

        _firstId = n > 0 ? lots[0].Destination.Value : 0u;
        _idsContiguous = contiguous;
    }

    /// <summary>The kinds this part applies.</summary>
    public static EventKindSet Kinds => EventKindSet.Of(RunEventKinds.DeliveriesState);

    /// <summary>The number of destinations (the lot count).</summary>
    public int Count => _entity.Length;

    /// <summary>True when the last <c>DeliveriesState</c> said deliveries are open.</summary>
    public bool Open { get; private set; }

    /// <summary>The close tick of the last <c>DeliveriesState</c> (0 before any).</summary>
    public uint CloseTick { get; private set; }

    /// <summary>The unlocked-district mask of the last <c>DeliveriesState</c> (bit <c>districtId - 1</c>; District 1 before any).</summary>
    public ulong UnlockedDistrictMask { get; private set; } = 1UL;

    /// <summary>The destination entity of row <paramref name="ordinal"/>.</summary>
    /// <param name="ordinal">The row, 0 to <see cref="Count"/> - 1.</param>
    /// <returns>The entity id.</returns>
    public EntityId DestinationAt(int ordinal) => _entity[ordinal];

    /// <summary>The raw state of row <paramref name="ordinal"/>, no grace: the host registry's rule, through <see cref="DeliveriesStateRules"/>.</summary>
    /// <param name="ordinal">The row, 0 to <see cref="Count"/> - 1.</param>
    /// <returns>OPEN, CLOSED or LOCKED.</returns>
    public DestState StateAt(int ordinal) => EffectiveStateAt(ordinal, null, 0u, 0);

    /// <inheritdoc />
    public void Apply(in EventEntry e, ReadOnlySpan<byte> blob)
    {
        if (e.Kind != RunEventKinds.DeliveriesState)
        {
            return;
        }

        DeliveriesStatePayload p = e.PayloadAs<DeliveriesStatePayload>();
        Open = p.Open != 0;
        CloseTick = p.CloseTick;
        UnlockedDistrictMask = p.UnlockedMask;
    }

    /// <inheritdoc />
    public void ApplyReply(in Reply r)
    {
    }

    /// <summary>Finds the row of <paramref name="dest"/>; 0 B (computed for the World's contiguous ids, else a scan).</summary>
    /// <param name="dest">The destination entity.</param>
    /// <param name="ordinal">The row when found, else -1.</param>
    /// <returns>True when <paramref name="dest"/> is a destination.</returns>
    public bool TryGetOrdinal(EntityId dest, out int ordinal)
    {
        if (_idsContiguous)
        {
            uint off = unchecked(dest.Value - _firstId);
            ordinal = off < (uint)_entity.Length ? (int)off : -1;
            return ordinal >= 0;
        }

        for (int i = 0; i < _entity.Length; i++)
        {
            if (_entity[i] == dest)
            {
                ordinal = i;
                return true;
            }
        }

        ordinal = -1;
        return false;
    }

    /// <summary>The facts of row <paramref name="ordinal"/> with <paramref name="state"/>, through the shared <see cref="DestinationRows.Facts"/>.</summary>
    internal DestinationFacts FactsAt(int ordinal, DestState state) =>
        DestinationRows.Facts(_entity[ordinal], _address[ordinal], _type[ordinal], state, _typeAcceptsMask, _typeRequiresUnit);

    /// <summary>The effective state of row <paramref name="ordinal"/>, through the one effective-state helper.</summary>
    internal DestState EffectiveStateAt(int ordinal, uint? actionStartTick, uint applyTick, int closeGraceTicks)
    {
        bool unlocked = DeliveriesStateRules.IsDistrictUnlocked(UnlockedDistrictMask, _district[ordinal]);
        return DeliveriesStateRules.Effective(unlocked, Open, CloseTick, actionStartTick, applyTick, closeGraceTicks);
    }
}
