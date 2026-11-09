using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail's shift spawner (GDD mail-destinations R10, R11; ADR-0017 sections 6, 9 and 10; mail-destinations stories 013 and 014), owned by
/// <see cref="MailSystem"/>. <see cref="PrepareShift"/> stores the profile and marks the manifest dirty; phase 8 (<see cref="Step"/>)
/// rolls it once if dirty (so several changes in one tick roll once), on the shift's own <c>mail</c> stream (re-seeded, so identical
/// inputs roll identically). The lock is set when Run opens deliveries (<see cref="OnDeliveriesState"/>, called in phase 12 of
/// <c>S - 1</c>, so Delivery starts at <c>S</c>): from then on a profile for the same shift, or any profile while deliveries are open, is
/// ignored and counted. A profile for another shift while deliveries are closed starts that shift.
/// <para>
/// Firing: while locked, phase 8 compares the next batch's tick (<c>S + offset</c>) with the current tick and returns otherwise (no row
/// visited). At its tick, a batch fires only while deliveries are open (a batch tick in a closed state fires nothing and is not
/// deferred): each unit gets its mailId and row in manifest order (<see cref="MailTable.Create"/>), the units go to the Intake one
/// maximal same-key group (kind, address, flags; at most 255 ids) at a time through <see cref="IInventoryApi.Spawn"/> (NoMerge) while the
/// backlog is empty, the unplaced remainder and every later unit then go one unit per <see cref="IInventoryApi.AppendBacklog"/> call,
/// and one <see cref="MailEventKinds.BatchArrived"/> is raised. Inventory interaction is only through <see cref="IInventoryApi"/>.
/// </para>
/// Its dump (<see cref="WriteCanonical"/>) is the manifest's inputs and the lock, which reproduce the manifest. 0 B per tick after
/// construction. Sim tick thread only.
/// </summary>
internal sealed class MailShiftSpawner
{
    // Spawn takes 1 to 255 ids per call (grid-inventory 012).
    private const int MaxSpawnIds = 255;

    private readonly MailSystem _owner;
    private readonly IRngStreams _rng;
    private readonly ITickReader _tick;
    private readonly IInventoryApi _inventory;
    private readonly EventLogFacade _events;
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;
    private readonly ContentSet _content;
    private readonly MailMixDef? _mix;
    private readonly BatchKnobs _batchKnobs;
    private readonly MailValueKnobs _valueKnobs;
    private readonly ShiftManifest _manifest;
    private readonly AddressId[] _addresses;
    private readonly KindShare[] _shares = new KindShare[KindCounts.MaxKinds];
    private readonly byte[] _deadlineOffsets = new byte[KindCounts.MaxKinds];
    private readonly MailId[] _ids = new MailId[MaxSpawnIds];

    private SpawnProfile _profile;
    private bool _hasProfile;
    private bool _dirty;
    private bool _locked;
    private uint _deliveryStartTick;
    private int _nextBatch;

    public MailShiftSpawner(
        MailSystem owner,
        IRngStreams rng,
        ITickReader tick,
        IInventoryApi inventory,
        EventLogFacade events,
        MailTable table,
        DestinationRegistry registry,
        ContentSet content)
    {
        _owner = owner;
        _rng = rng;
        _tick = tick;
        _inventory = inventory;
        _events = events;
        _table = table;
        _registry = registry;
        _content = content;
        _mix = content.MailMixes.Count > 0 ? content.MailMixes.AsSpan()[0] : null;
        _batchKnobs = _mix is null ? BatchKnobs.Default : BatchKnobs.From(_mix);
        _valueKnobs = MailValueKnobs.From(content.Mail, MailValueKnobs.Default.CursedValueMultPm, MailValueKnobs.Default.FoundValueMultPm);
        _manifest = new ShiftManifest(registry.Count);
        _addresses = new AddressId[registry.Count];
    }

    /// <summary>The current manifest (empty before the first roll).</summary>
    public ShiftManifest Manifest => _manifest;

    /// <summary>True once Delivery has started for the prepared shift.</summary>
    public bool Locked => _locked;

    /// <summary>The Delivery-start tick of the locked shift (0 before the lock).</summary>
    public uint DeliveryStartTick => _deliveryStartTick;

    /// <summary><see cref="PrepareShift"/> calls ignored because the shift was locked or deliveries were open.</summary>
    public int IgnoredPrepareCount { get; private set; }

    /// <summary>The rolls made (a re-roll counts once per dirty tick).</summary>
    public int RollCount { get; private set; }

    /// <summary>
    /// Stores <paramref name="p"/> (see the type remarks). An unchanged profile changes nothing. A new shift sets Mail's current shift and
    /// zeroes the shift's wrong-delivery count (story 013 AC-5, AC-6).
    /// </summary>
    /// <param name="p">The profile.</param>
    public void PrepareShift(in SpawnProfile p)
    {
        bool newShift = !_hasProfile || p.Shift != _profile.Shift;
        if (_registry.Deliveries.Open || (_locked && !newShift))
        {
            IgnoredPrepareCount++;
            return;
        }

        if (!newShift && p == _profile)
        {
            return;
        }

        if (newShift)
        {
            _locked = false;
            _deliveryStartTick = 0;
            _nextBatch = 0;
            _owner.CurrentShift = p.Shift;
            _owner.MisdeliveriesThisShift = 0;
        }

        _profile = p;
        _hasProfile = true;
        _dirty = true;
    }

    /// <summary>Run set the deliveries state (phase 12 of the tick before it applies): opening locks the prepared shift.</summary>
    /// <param name="open">Whether deliveries open.</param>
    public void OnDeliveriesState(bool open)
    {
        if (open && _hasProfile && !_locked)
        {
            _locked = true;
            _deliveryStartTick = _tick.CurrentTick + 1;
        }
    }

    /// <summary>Phase 8: the pending roll, then any batch due this tick (see the type remarks).</summary>
    public void Step()
    {
        if (_dirty)
        {
            Roll();
        }

        if (!_locked)
        {
            return;
        }

        uint now = _tick.CurrentTick;
        ShiftManifest m = _manifest;
        while (_nextBatch < m.BatchCount && _deliveryStartTick + m.BatchOffset(_nextBatch) <= now)
        {
            if (_deliveryStartTick + m.BatchOffset(_nextBatch) == now && _registry.Deliveries.Open)
            {
                Fire(_nextBatch);
            }

            _nextBatch++;
        }
    }

    /// <summary>
    /// The dump, after the deliveries state: <c>hasProfile u8</c>, the profile (<c>shift i32, V i64, deliverySeconds i32,
    /// raidWindowSeconds i32, cursedSharePm i32, playerCount i32, playerScalePm i32, foundValueBudget i64</c>), <c>dirty u8, locked u8,
    /// deliveryStartTick u32, nextBatch u16, unitCount u16, batchCount u16</c>.
    /// </summary>
    /// <param name="w">The writer, inside the Mail section.</param>
    public void WriteCanonical(ref CanonicalWriter w)
    {
        w.WriteBool(_hasProfile);
        w.WriteI32(_profile.Shift);
        w.WriteI64(_profile.SpawnBudgetCents);
        w.WriteI32(_profile.DeliverySeconds);
        w.WriteI32(_profile.RaidWindowSeconds);
        w.WriteI32(_profile.CursedSharePm);
        w.WriteI32(_profile.PlayerCount);
        w.WriteI32(_profile.PlayerScalePm);
        w.WriteI64(_profile.FoundValueBudgetCents);
        w.WriteBool(_dirty);
        w.WriteBool(_locked);
        w.WriteU32(_deliveryStartTick);
        w.WriteU16((ushort)_nextBatch);
        w.WriteU16((ushort)_manifest.UnitCount);
        w.WriteU16((ushort)_manifest.BatchCount);
    }

    private void Roll()
    {
        _dirty = false;
        RollCount++;
        SpawnProfile p = _profile;
        if (_mix is null || p.Shift < RngNames.Mail.MinIndex || p.Shift > RngNames.Mail.MaxIndex)
        {
            _manifest.Reset(ManifestRollStatus.InvalidKinds);
            return;
        }

        int n = _registry.CopyUnlockedAddresses(_addresses, out ulong accepts);
        int kinds = ManifestRoller.BuildKinds(_mix, _content.MailKinds.AsSpan(), p.Shift, accepts, _shares, _deadlineOffsets);
        if (kinds < 0)
        {
            _manifest.Reset(kinds);
            return;
        }

        RngStream stream = _rng.Create(in RngNames.Mail, (uint)p.Shift);
        _ = ManifestRoller.Roll(
            stream,
            in p,
            _addresses.AsSpan(0, n),
            _shares.AsSpan(0, kinds),
            _deadlineOffsets.AsSpan(0, kinds),
            in _batchKnobs,
            in _valueKnobs,
            _manifest);
    }

    private void Fire(int b)
    {
        ShiftManifest m = _manifest;
        int start = m.BatchStart(b);
        int end = start + m.BatchUnitCount(b);
        byte spawnShift = (byte)Math.Clamp(_profile.Shift, 0, byte.MaxValue);
        ContainerId intake = _inventory.IntakeContainer;
        using EventCallScope scope = _events.BeginCall();
        int i = start;
        while (i < end)
        {
            // One maximal same-key run of the manifest (at most MaxSpawnIds ids), its rows created in manifest order.
            DefIndex kind = m.KindAt(i);
            AddressId address = m.AddressAt(i);
            MailFlags flags = m.FlagsAt(i);
            int ids = 0;
            for (; i < end && ids < MaxSpawnIds && m.KindAt(i) == kind && m.AddressAt(i) == address && m.FlagsAt(i) == flags; i++)
            {
                MailId id = _table.Create(kind, address, m.SpawnValueAt(i), spawnShift, flags);
                if (!id.IsNone)
                {
                    _ids[ids++] = id;
                }
            }

            if (ids > 0)
            {
                Hand(intake, kind, address, flags, _ids.AsSpan(0, ids));
            }
        }

        var payload = new BatchArrivedPayload { Units = (ushort)Math.Min(end - start, ushort.MaxValue) };
        _ = _events.TryRaise(MailEventKinds.BatchArrived, MailEventDescriptors.AudienceAll, in payload);
    }

    // R11: "backlog non-empty -> append" is checked before the Intake, so no Intake probe is spent; the unplaced remainder is appended
    // one unit per call, in order. A refused append leaves the unit's row with Mail (Inventory counts the refusal).
    private void Hand(ContainerId intake, DefIndex kind, AddressId address, MailFlags flags, ReadOnlySpan<MailId> ids)
    {
        int placed = 0;
        if (_inventory.BacklogEntries == 0)
        {
            placed = _inventory.Spawn(intake, kind, address.Value, (byte)flags, ids, MergePolicy.NoMerge).Placed;
        }

        for (int u = placed; u < ids.Length; u++)
        {
            _ = _inventory.AppendBacklog(new BacklogEntry(kind, address.Value, (byte)flags, ids.Slice(u, 1)));
        }
    }
}
