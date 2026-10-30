using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Player Character &amp; Movement's host-side module (GDD <c>player-character-movement.md</c> P1; ADR-0013 section 2, ADR-0014
/// section 6; story player-character/001). It holds one <see cref="PlayerRecord"/> per player slot: it creates the record when
/// Sim Core's <c>PlayerJoin</c> handler reports a join (through the internal <c>IPlayerJoinListener</c> seam, bound at
/// <see cref="Bind"/>) and removes it on <c>PlayerLeave</c>. The carrier spawns on the Post Office pad ring slot
/// <c>k = PlayerSlot</c> (M-1a placeholder for the M0 <c>Teleport{Join}</c>), Grounded and Alive, with
/// <c>ClockInTick</c> = the join tick and <c>ProtectedUntilTick</c> = 0. The module reads the world only through
/// <see cref="IWorldQuery"/> and takes every constant from <see cref="PlayerSettings"/>. Story 019 adds the host side: phase 3
/// <see cref="PlayersStep"/> ingests the latched transform samples into a per-slot host position (seeded at the pad on join), and
/// <see cref="CheckInteraction"/> and <see cref="LeashExceeded"/> read it. It binds the join listener, phase 3 and its state
/// contributor, and no op. Allocation-free once constructed. Not thread-safe: used on the Sim tick thread.
/// </summary>
/// <example>
/// <code>
/// // Composition: new PlayersSystem(world, content, samples, slots, tick, checkRing, counters)
/// LifeState life = players.GetLifeState(PlayerSlot.FromValue(0));   // Alive after the slot's PlayerJoin
/// </code>
/// </example>
[SimSystem(SystemId.PlayerCharacter)]
public sealed class PlayersSystem : ISimSystem, IPlayersApi, IPlayerJoinListener
{
    private readonly IWorldQuery _world;
    private readonly PostOfficeRecord _postOffice;
    private readonly PlayerSettings _settings;
    private readonly PlayerRecord[] _records = new PlayerRecord[PlayerSlot.Count];
    private readonly EmptyRemoteCapsuleSource _emptyRemotes = new();
    private readonly EmptySupportTransformSource _emptySupports = new();
    private readonly ISampleLatch _samples;
    private readonly ISlotView _slots;
    private readonly ITickReader _tick;
    private readonly SimCounters _counters;
    private readonly IPositionHistory _history;
    private readonly InteractionGate _gate = new(PlayerReasons.Framework);   // reason ids from PlayerReasons: Evan approved (TD-194 item 9), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019"
    private readonly HoldSettings _hold;

    /// <summary>
    /// Creates the module with a private check ring and counter set (tests and tools; the
    /// composition root uses the full overload).
    /// </summary>
    /// <param name="world">The world's read interface (terrain height and the Post Office pad).</param>
    /// <param name="balance">The content set's <c>player.*</c> record.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayersWorldHasNoPostOffice</c> when the world has no spawn pad; <c>PlayerSettingsInvalid</c> for an unusable value.</exception>
    internal PlayersSystem(IWorldQuery world, PlayerBalance balance)
        : this(world, PlayerSettings.FromBalance(balance))
    {
    }

    /// <summary>
    /// The composition constructor (the one public constructor; ADR-0003 section 4): the content's <c>player.*</c> record (every
    /// constant, including <c>player.maxAnchors</c>), the tick's latched transform samples (the <see cref="ISampleLatch"/> row, Player
    /// Character only: Evan approved 2026-10-29 (ADR-0003 §4 erratum)), the slot view, the tick reader, the world's check ring and counters.
    /// </summary>
    /// <param name="world">The world's read interface (terrain height and the Post Office pad).</param>
    /// <param name="content">The content set (<c>Player</c>).</param>
    /// <param name="samples">The tick's latched transform samples (phase 3 ingest).</param>
    /// <param name="slots">The read-only slot table (<c>ActorId</c> to slot).</param>
    /// <param name="tick">The tick reader (staleness is measured against <see cref="ITickReader.CurrentTick"/>).</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayersWorldHasNoPostOffice</c> when the world has no spawn pad; <c>PlayerSettingsInvalid</c> for an unusable value.</exception>
    public PlayersSystem(IWorldQuery world, ContentSet content, ISampleLatch samples, ISlotView slots, ITickReader tick, SimCheckRing checks, SimCounters counters)
        : this(world, PlayerSettings.FromBalance((content ?? throw new ArgumentNullException(nameof(content))).Player), samples, slots, tick, checks, counters)
    {
    }

    /// <summary>Creates the module over <paramref name="world"/> with ready settings and private check ring and counters (tests).</summary>
    /// <param name="world">The world's read interface.</param>
    /// <param name="settings">The settings.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayersWorldHasNoPostOffice</c> when the world has no spawn pad.</exception>
    internal PlayersSystem(IWorldQuery world, PlayerSettings settings)
        : this(world, settings, new SimCheckRing(), new SimCounters())
    {
    }

    /// <summary>Creates the module over <paramref name="world"/> with ready settings, check ring and counters.</summary>
    /// <param name="world">The world's read interface.</param>
    /// <param name="settings">The settings (<see cref="PlayerSettings.MaxAnchors"/> sizes the anchor registry).</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayersWorldHasNoPostOffice</c> when the world has no spawn pad.</exception>
    internal PlayersSystem(IWorldQuery world, PlayerSettings settings, SimCheckRing checks, SimCounters counters)
        : this(world, settings, new SampleLatchForwarder(), new SlotTable(), new WorldTickForwarder(), checks, counters)
    {
    }

    /// <summary>Creates the module over <paramref name="world"/> with every port supplied (the composition path).</summary>
    /// <param name="world">The world's read interface.</param>
    /// <param name="settings">The settings (<see cref="PlayerSettings.MaxAnchors"/> sizes the anchor registry).</param>
    /// <param name="samples">The tick's latched transform samples.</param>
    /// <param name="slots">The read-only slot table.</param>
    /// <param name="tick">The tick reader.</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayersWorldHasNoPostOffice</c> when the world has no spawn pad.</exception>
    internal PlayersSystem(IWorldQuery world, PlayerSettings settings, ISampleLatch samples, ISlotView slots, ITickReader tick, SimCheckRing checks, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(counters);
        PostOfficeRecord po = world.PostOffice;
        if (po.PadSlots == 0)
        {
            throw new SimBuildException(SimBuildErrorCode.PlayersWorldHasNoPostOffice, "Players needs a world with a Post Office spawn pad.");
        }

        _world = world;
        _postOffice = po;
        _settings = settings;
        _samples = samples;
        _slots = slots;
        _tick = tick;
        _counters = counters;
        _hold = HoldSettings.FromPlayerSettings(settings);
        _history = new NewestSamplePositionHistory(HostPositions);
        Anchors = new AnchorRegistry(world.Bounds, settings.MaxAnchors, settings.RangeMm, settings.IncumbentSlackMm, checks, counters);
    }

    /// <summary>The interaction anchor registry (story 012); the focus and press stories read it, owners write it through <see cref="IPlayersApi"/>.</summary>
    internal AnchorRegistry Anchors { get; }

    /// <summary>The host's newest transform per slot (story 019): seeded on join, written by <see cref="PlayersStep"/>, read by the range checks.</summary>
    internal HostPositionTable HostPositions { get; } = new();

    /// <summary>Anchors currently registered.</summary>
    public int AnchorCount => Anchors.Count;

    /// <inheritdoc />
    public RegisterResult RegisterAnchor(in AnchorDef def, IInteractable impl) => Anchors.Register(in def, impl);

    /// <inheritdoc />
    public RegisterResult UnregisterAnchor(AnchorRef anchor) => Anchors.Unregister(anchor);

    /// <inheritdoc />
    public RegisterResult MoveAnchor(AnchorRef anchor, in Vec3Mm point) => Anchors.Move(anchor, in point);

    /// <summary>The settings this module reads every constant from.</summary>
    public PlayerSettings Settings => _settings;

    /// <summary>
    /// The M-1a (<c>M1aGreybox</c>) carrier env for <paramref name="self"/>: this world, these settings and the empty ports (solo, no vehicles
    /// or belts). The empty-source instances live here, not in statics (ADR-0013 section 7).
    /// </summary>
    /// <param name="self">The carrier's entity id.</param>
    /// <returns>The env (a <c>ref struct</c>).</returns>
    /// <example><code>CarrierEnv env = players.CreateCarrierEnv(selfId);</code></example>
    public CarrierEnv CreateCarrierEnv(EntityId self) => CarrierEnv.ForM1a(_world, _settings, self, _emptyRemotes, _emptySupports);

    /// <inheritdoc />
    public LifeState GetLifeState(PlayerSlot slot) =>
        slot.IsNone || _records[slot.Value].IsEmpty ? LifeState.Despawned : _records[slot.Value].Life;

    /// <summary>
    /// Binds the join/leave listener, phase 3 (<see cref="PlayersStep"/>, story 019) and the canonical dump section; binds no op or
    /// event.
    /// </summary>
    /// <param name="builder">The world's builder.</param>
    /// <exception cref="SimBuildException"><c>PlayerJoinListenerAlreadyBound</c> or <c>DuplicateSection</c>.</exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindPlayerJoinListener(this);
        builder.Bind(PhaseId.PlayersStep, new PlayersStep(this, _samples, _slots, _counters));
        builder.BindStateContributor(new PlayersStateContributor(this));
    }

    /// <inheritdoc />
    public InteractionCheck CheckInteraction(ActorId actor, AnchorRef anchor, in ClaimedPosition claim)
    {
        // Read-only and legal in any phase and between ticks: no TickGuard.IsMutable call, no write, no event (ADR-0014 section 5).
        PlayerSlot slot = _slots.SlotOf(new EntityId(actor.Value));
        LifeState life = GetLifeState(slot);

        // Frozen -> Frozen; every other non-Alive state (Dead, DeadAway, Joining, not present) -> Dead. Evan approved 2026-10-29 (TD-188 item 5).
        if (life == LifeState.Frozen)
        {
            return InteractionCheck.Of(InteractionCheckKind.Frozen);
        }

        if (life != LifeState.Alive)
        {
            return InteractionCheck.Of(InteractionCheckKind.Dead);
        }

        if (!Anchors.TryGet(anchor, out int index))
        {
            return InteractionCheck.Of(InteractionCheckKind.NoTarget);
        }

        ref readonly AnchorDef def = ref Anchors.DefAt(index);
        ref readonly HostPosition host = ref HostPositions[slot];
        ref readonly PlayerRecord record = ref _records[slot.Value];

        // The framework gate first, then the owner's IsAvailable. Evan approved 2026-10-29 (TD-188 item 4). Host state only: the
        // stored sample's swimming bit; the panel is client-only (closed) and no seat is occupied at M-1a.
        var status = new CarrierStatus
        {
            Life = life,
            Body = BodyContext.OnFoot,   // Alive; the host knows no mount or build mode at M-1a
            Swimming = (host.Flags & SampleFlags.Swimming) != 0,
            PanelOpen = false,
            OccupiedSeat = AnchorRef.None,
        };
        GateResult gate = _gate.Evaluate(in def, in status);
        if (gate.Kind == GateKind.NoCandidate)
        {
            // Not eligible in the actor's body context, as if no such anchor. Evan approved (TD-194 item 6), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019".
            return InteractionCheck.Of(InteractionCheckKind.NoTarget);
        }

        if (gate.Kind == GateKind.Unavailable)
        {
            return InteractionCheck.Unavailable(gate.Reason);
        }

        // The host-side InteractionContext. Evan approved 2026-10-29 (TD-188 item 3).
        var c = new InteractionContext
        {
            Tick = _tick.CurrentTick,
            Actor = slot,
            ActorEntity = record.PlayerId,
            ChestMm = Chest(in host.Feet),
            Anchor = def.Ref,
            Verb = def.Verb,
            Mode = def.Mode,
            HoldBaseMs = def.HoldBaseMs,
            RangeMm = def.RangeMm,
            HoldSettingMs = _hold.HoldDefaultMs,
            HoldSettings = _hold,
            ContextMask = Anchors.ContextMaskAt(index),
            IsHost = 0,
        };
        if (!Anchors.ImplAt(index).IsAvailable(in c, out StringId reason))
        {
            return InteractionCheck.Unavailable(reason);
        }

        if (IsStale(in host))
        {
            return InteractionCheck.Of(InteractionCheckKind.PositionStale);
        }

        // In long: the claim is client data, and SIM_ASSERT builds check int overflow.
        bool inRange = PlayersRange.Within(
            (long)claim.X - def.Point.X,
            (long)claim.Y + _settings.ChestMm - def.Point.Y,
            (long)claim.Z - def.Point.Z,
            (long)def.RangeMm + _settings.RangeSlackMm);
        return inRange && _history.Corroborates(slot, in claim, _settings.ClaimToleranceMm)
            ? InteractionCheck.Of(InteractionCheckKind.Ok)
            : InteractionCheck.Of(InteractionCheckKind.OutOfRange);
    }

    /// <inheritdoc />
    public bool LeashExceeded(PlayerSlot slot, in Vec3Mm anchorPoint, int leashMm)
    {
        // GDD F5 leash(p, anchor), read-only. Evan approved 2026-10-29 (TD-188 item 2).
        // No player in the slot: nothing to close. Evan approved (TD-194 item 7), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019".
        if (slot.IsNone || _records[slot.Value].IsEmpty)
        {
            return false;
        }

        ref readonly HostPosition host = ref HostPositions[slot];
        if (IsStale(in host))
        {
            return false;
        }

        return !PlayersRange.Within(
            (long)host.Feet.X - anchorPoint.X, (long)host.Feet.Y + _settings.ChestMm - anchorPoint.Y, (long)host.Feet.Z - anchorPoint.Z, leashMm);
    }

    // GDD F5 stale(p): nowTick - lastReceivedTick[p] > positionStaleTicks, with nowTick = ITickReader.CurrentTick (the tick in progress;
    // between ticks, the next one). Evan approved (TD-194 item 8), 2026-10-29 by 11:34 AM PT: "Approve all nine TD-194 defaults for player-character 019".
    private bool IsStale(in HostPosition host) => (long)_tick.CurrentTick - host.LastReceivedTick > _settings.PositionStaleTicks;

    // The hook context's chest point; the feet come from a client sample, so an absurd Y wraps instead of throwing.
    private Vec3Mm Chest(in Vec3Mm feet) => new(feet.X, unchecked(feet.Y + _settings.ChestMm), feet.Z);

    /// <summary>The record of <paramref name="slot"/> for reading and for the later solver/phase code to write (empty slot: <see cref="PlayerRecord.IsEmpty"/>).</summary>
    /// <param name="slot">The slot (not none).</param>
    /// <returns>A reference into the module's fixed slot array.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is <see cref="PlayerSlot.None"/>.</exception>
    internal ref PlayerRecord RecordRef(PlayerSlot slot)
    {
        if (slot.IsNone)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return ref _records[slot.Value];
    }

    /// <summary>Moves the player in <paramref name="slot"/> to <paramref name="to"/> when <see cref="LifeStates.CanTransition"/> allows it.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="to">The requested state.</param>
    /// <returns>True when the slot holds a player and the edge was legal (and was taken).</returns>
    internal bool TryTransition(PlayerSlot slot, LifeState to)
    {
        if (slot.IsNone || _records[slot.Value].IsEmpty || !LifeStates.CanTransition(_records[slot.Value].Life, to))
        {
            return false;
        }

        _records[slot.Value].Life = to;
        return true;
    }

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick)
    {
        SimAssert.That(_records[slot.Value].IsEmpty, SimAssertCode.PlayersJoinSlotOccupied);
        Vec3Mm pad = PadSlots.Position(_world, _postOffice, slot.Value);
        ref PlayerRecord r = ref _records[slot.Value];
        r = default;
        r.PlayerId = player;
        r.Life = LifeState.Joining;
        r.ClockInTick = tick;
        r.CapsuleRadiusMm = _settings.CapsuleRadiusMm;
        r.CapsuleHeightMm = _settings.CapsuleHeightMm;
        r.Carrier.X = pad.X;
        r.Carrier.Y = pad.Y;
        r.Carrier.Z = pad.Z;
        r.Carrier.Loco = LocoState.Grounded;

        // Face outward like the pad slot (PadSlots: yaw k * 8192); camera starts level on the same heading.
        ushort yaw = unchecked((ushort)(slot.Value * PadSlots.SlotStepBam));
        r.Carrier.BodyYaw = yaw;
        r.LookYaw = yaw;
        r.LookPitch = 0;

        // Story 019: the host position starts at the pad, received now, so a request on the next tick is not position_stale.
        HostPositions[slot] = new HostPosition { Feet = pad, Yaw = yaw, Flags = SampleFlags.None, LastReceivedTick = tick };

        // Joining to Alive is an edge of LifeStates (a test pins the table), so the transition cannot fail on the record just created.
        _ = TryTransition(slot, LifeState.Alive);
    }

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerLeft(PlayerSlot slot, EntityId player)
    {
        SimAssert.That(_records[slot.Value].PlayerId == player, SimAssertCode.PlayersLeaveMismatch);
        _records[slot.Value] = default;
        HostPositions[slot] = default;
    }
}
