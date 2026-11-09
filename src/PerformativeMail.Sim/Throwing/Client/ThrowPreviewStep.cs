using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The aim preview at <see cref="PreviewSubStep.ThrowPreview"/> (4c) (GDD T3; ADR-0016 sections 2, 3 and 7, ADR-0015 section 8;
/// throwing-catching story 004, TR-throwing-003 and 017). Once per client tick:
/// <list type="number">
/// <item><see cref="NotThrowable"/>: swimming, else the hands slot (index 0), else an empty selected slot, else none; published aiming or not.</item>
/// <item>The entry latch: the selected entry's id and units are latched when an aim starts on a throwable entry; while aiming,
/// <c>EntryStale</c> is 1 when the selected entry's id or count differs from the latch or the entry vanished.</item>
/// <item>While <see cref="CarrierStatus.Aiming"/> and throwable: <see cref="ThrowAim.Quantise"/> of the camera aim, then, only when
/// the <see cref="PreviewCacheKey"/> changed, <see cref="LaunchMath.Compute"/> from the predicted feet → <see cref="Ballistic.PositionAt"/>
/// for <c>k = 1 … maxFlightTicks</c> → <see cref="FlightSweep.FirstContact"/> on each segment <c>p(k−1) → p(k)</c> over a
/// <see cref="ReceiverMask.StaticOnly"/> view with no catchers (the host's functions; goons and catchers are never seen). A mailbox
/// contact's class is <see cref="MailRules.Evaluate"/> over the mirror's effective destination facts and the selected entry's unit facts;
/// the wallet is a key input only.</item>
/// </list>
/// The published <see cref="ThrowPreviewState.Seq"/> changes only when the state does. The polyline buffer holds
/// <c>maxFlightTicks + 1</c> points, allocated once. Reads only its constructor ports, never <c>SimWorld</c>. Allocation-free per
/// <see cref="Step"/>; not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var preview = new ThrowPreviewStep(carrier, selected, mirror, new DefaultThrowerProfile(), receivers, ranks, settings, balance.LoftDeg);
/// preview.BindTo(builder);
/// ref readonly ThrowPreviewState s = ref preview.Current;
/// </code>
/// </example>
public sealed class ThrowPreviewStep : IClientStep, IThrowPreviewRead
{
    private readonly IThrowCarrierSource _carrier;
    private readonly ISelectedEntryView _selected;
    private readonly IThrowPreviewMirror _mirror;
    private readonly IThrowerProfile _profile;
    private readonly ReceiverViewBuilder _receivers;
    private readonly ReceiverRanks _ranks;
    private readonly ThrowSettings _settings;
    private readonly int _loftMilliDeg;
    private readonly IChestOriginProbe _chestProbe;
    private readonly ReceiverScratch _scratch = new();
    private readonly Vec3Mm[] _points;
    private ThrowPreviewState _state;
    private PreviewCacheKey _key;
    private bool _latched;
    private EntryId _latchedEntry;
    private byte _latchedUnits;

    /// <summary>Creates the step.</summary>
    /// <param name="carrier">The local carrier: predicted feet, aim, status.</param>
    /// <param name="selected">The selected hotbar entry.</param>
    /// <param name="mirror">Destination facts, their version and the wallet.</param>
    /// <param name="profile">The hand and <c>ThrowRangePermille</c>; null means <see cref="DefaultThrowerProfile"/> (+1, 1000).</param>
    /// <param name="receivers">The shared receiver-view builder over the client's world and catch volumes.</param>
    /// <param name="ranks">The receiver ranks (the content table, as the host's).</param>
    /// <param name="settings">The throw knobs (<c>maxFlightTicks</c> sizes the polyline).</param>
    /// <param name="loftMilliDeg"><c>throw.loftDeg</c>, milli-degrees.</param>
    /// <param name="chestProbe">F1's chest-to-origin probe (the host's <see cref="ChestOriginProbe"/> over the same world); null means none.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public ThrowPreviewStep(
        IThrowCarrierSource carrier,
        ISelectedEntryView selected,
        IThrowPreviewMirror mirror,
        IThrowerProfile? profile,
        ReceiverViewBuilder receivers,
        ReceiverRanks ranks,
        ThrowSettings settings,
        int loftMilliDeg,
        IChestOriginProbe? chestProbe = null)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(receivers);
        ArgumentNullException.ThrowIfNull(settings);
        _carrier = carrier;
        _selected = selected;
        _mirror = mirror;
        _profile = profile ?? new DefaultThrowerProfile();
        _receivers = receivers;
        _ranks = ranks;
        _settings = settings;
        _loftMilliDeg = loftMilliDeg;
        _chestProbe = chestProbe ?? NoChestProbe.Instance;
        _points = new Vec3Mm[settings.MaxFlightTicks + 1];
    }

    /// <inheritdoc />
    public ref readonly ThrowPreviewState Current => ref _state;

    /// <inheritdoc />
    public ReadOnlySpan<Vec3Mm> Polyline => new(_points, 0, _state.PointCount);

    /// <inheritdoc />
    public ReadOnlySpan<Vec3Mm> HatPins => default;

    /// <summary>How many times the arc query ran (the ADR-0016 section 7 evaluation-counter test seam).</summary>
    internal int Evaluations { get; private set; }

    /// <summary>Binds this step at <see cref="PreviewSubStep.ThrowPreview"/> (4c): after the deliver preview, before edge resolution.</summary>
    /// <param name="builder">The client layer's builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public void BindTo(ClientLayerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindPreview(PreviewSubStep.ThrowPreview, this);
    }

    /// <inheritdoc />
    public void Step(in ClientStepContext ctx)
    {
        ref readonly CarrierStatus status = ref _carrier.Status;
        byte index = _selected.SelectedIndex;
        bool hasEntry = _selected.TryGet(out SelectedEntryFacts entry);
        NotThrowable refusal = status.Swimming ? NotThrowable.Swimming
            : index == 0 ? NotThrowable.HandsSlot
            : !hasEntry ? NotThrowable.EmptySlot
            : NotThrowable.None;

        if (!status.Aiming)
        {
            _latched = false;
        }
        else if (!_latched && refusal == NotThrowable.None)
        {
            _latched = true;
            _latchedEntry = entry.Entry;
            _latchedUnits = entry.Units;
        }

        byte stale = (byte)(_latched && (!hasEntry || entry.Entry != _latchedEntry || entry.Units != _latchedUnits) ? 1 : 0);
        EntryId shownEntry = _latched ? _latchedEntry : entry.Entry;
        byte shownUnits = _latched ? _latchedUnits : entry.Units;
        if (!status.Aiming || refusal != NotThrowable.None)
        {
            PublishInactive(refusal, index, stale, shownEntry, shownUnits, ctx.ClientTick);
            return;
        }

        ThrowAim.Quantise(_carrier.AimYaw, _carrier.AimPitch, _loftMilliDeg, out short dx, out short dy, out short dz);
        Vec3Mm feet = _carrier.Feet;
        sbyte side = _profile.Side;
        int range = _profile.ThrowRangePermille;
        var key = new PreviewCacheKey(
            dx, dy, dz, feet, _mirror.DestinationVersion, _mirror.Wallet.Value, ReceiverViewBuilder.StaticVersion, entry.Entry, entry.Units,
            entry.WeightClass, side, range);
        if (_state.Active == 1 && key.SameAs(in _key) && _state.EntryStale == stale && _state.Entry == shownEntry && _state.Units == shownUnits)
        {
            return;
        }

        _key = key;
        Evaluations++;
        var input = new LaunchInput(feet, dx, dy, dz, side, entry.WeightClass, range, _carrier.BodyYaw, _settings, _chestProbe);
        if (!LaunchMath.Compute(in input, out Launch launch, out bool fromChest))
        {
            // Unreachable for a quantised direction and valid content: publish an active state with no arc rather than a stale one.
            Publish(1, refusal, index, stale, shownEntry, shownUnits, ctx.ClientTick, EndClass.None, MailRejectReason.None, dx, dy, dz, side, feet, 0, 0, 0, false);
            return;
        }

        EndClass cls = EndClass.Timeout;
        MailRejectReason reason = MailRejectReason.None;
        uint endKey = 0;
        int max = _settings.MaxFlightTicks;
        int age = max;
        _points[0] = Ballistic.PositionAt(in launch, 0);
        for (int k = 1; k <= max; k++)
        {
            Vec3Mm p0 = _points[k - 1];
            Vec3Mm p1 = Ballistic.PositionAt(in launch, k);
            ReceiverView view = _receivers.Build(in p0, in p1, ReceiverMask.StaticOnly, _ranks, default, _scratch);
            if (FlightSweep.FirstContact(in p0, in p1, in view, default, out Contact c))
            {
                _points[k] = c.Point;
                age = k;
                endKey = c.ReceiverKey;
                cls = Classify(in c, in entry, ctx.ClientTick, k, out reason);
                break;
            }

            _points[k] = p1;
        }

        Publish(1, refusal, index, stale, shownEntry, shownUnits, ctx.ClientTick, cls, reason, dx, dy, dz, side, feet, endKey, age, age + 1, fromChest);
    }

    private EndClass Classify(in Contact c, in SelectedEntryFacts entry, uint now, int age, out MailRejectReason reason)
    {
        reason = MailRejectReason.None;
        switch (c.Kind)
        {
            case ReceiverKind.Mailbox:
                if (!_mirror.TryGetDestination(c.ReceiverKey, now, unchecked(now + (uint)age), out DestinationFacts d))
                {
                    return EndClass.Blocked;   // a volume no mirrored destination owns: show no verdict
                }

                DeliverEval e = MailRules.Evaluate(in d, in entry.Unit);
                reason = e.Reason;
                return e.Result switch
                {
                    EvalResult.Accept => EndClass.Accept,
                    EvalResult.Misdeliver => EndClass.Misdeliver,
                    _ => EndClass.Reject,
                };
            case ReceiverKind.Ground:
                return EndClass.Ground;
            case ReceiverKind.Water:
                return EndClass.Water;
            case ReceiverKind.Belt:
                return EndClass.Belt;
            default:
                return EndClass.Blocked;   // solids, roofs and the map sides (no dynamic kind is in a StaticOnly view)
        }
    }

    private void PublishInactive(NotThrowable refusal, byte index, byte stale, EntryId entry, byte units, uint tick)
    {
        ref readonly ThrowPreviewState s = ref _state;
        if (s.Seq != 0 && s.Active == 0 && s.NotThrowable == refusal && s.HotbarIndex == index && s.EntryStale == stale && s.Entry == entry
            && s.Units == units)
        {
            return;
        }

        Publish(0, refusal, index, stale, entry, units, tick, EndClass.None, MailRejectReason.None, 0, 0, 0, 0, default, 0, 0, 0, false);
    }

    private void Publish(
        byte active, NotThrowable refusal, byte index, byte stale, EntryId entry, byte units, uint tick, EndClass cls, MailRejectReason reason,
        short dx, short dy, short dz, sbyte side, Vec3Mm feet, uint endKey, int endAge, int pointCount, bool fromChest)
    {
        if (cls != _state.EndClass || _state.Seq == 0)
        {
            _state.ClassSinceTick = tick;
            _state.RedSinceTick = cls == EndClass.Misdeliver ? tick : 0;
        }

        _state.Seq++;
        _state.Active = active;
        _state.NotThrowable = refusal;
        _state.EndClass = cls;
        _state.RejectReason = reason;
        _state.EntryStale = stale;
        _state.HotbarIndex = index;
        _state.Units = units;
        _state.OriginFromChest = (byte)(fromChest ? 1 : 0);
        _state.Side = side;
        _state.Entry = entry;
        _state.DirX = dx;
        _state.DirY = dy;
        _state.DirZ = dz;
        _state.Feet = feet;
        _state.EndPoint = pointCount > 0 ? _points[endAge] : default;
        _state.EndKey = endKey;
        _state.EndAge = (ushort)endAge;
        _state.PointCount = (ushort)pointCount;
    }
}
