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
/// <see cref="IWorldQuery"/> and takes every constant from <see cref="PlayerSettings"/>. It binds no phase yet (its
/// <c>PlayersStep</c> is story 019) and no op; it binds the join listener and its state contributor. Allocation-free once
/// constructed. Not thread-safe: used on the Sim tick thread.
/// </summary>
/// <example>
/// <code>
/// // Composition: new PlayersSystem(world, content, checkRing, counters)
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
    /// constant, including <c>player.maxAnchors</c>), the world's check ring and counters.
    /// </summary>
    /// <param name="world">The world's read interface (terrain height and the Post Office pad).</param>
    /// <param name="content">The content set (<c>Player</c>).</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayersWorldHasNoPostOffice</c> when the world has no spawn pad; <c>PlayerSettingsInvalid</c> for an unusable value.</exception>
    public PlayersSystem(IWorldQuery world, ContentSet content, SimCheckRing checks, SimCounters counters)
        : this(world, PlayerSettings.FromBalance((content ?? throw new ArgumentNullException(nameof(content))).Player), checks, counters)
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
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settings);
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
        Anchors = new AnchorRegistry(world.Bounds, settings.MaxAnchors, settings.RangeMm, settings.IncumbentSlackMm, checks, counters);
    }

    /// <summary>The interaction anchor registry (story 012); the focus and press stories read it, owners write it through <see cref="IPlayersApi"/>.</summary>
    internal AnchorRegistry Anchors { get; }

    /// <inheritdoc />
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
    /// Binds the join/leave listener and the canonical dump section; binds no phase, op or event (story 019 adds
    /// <c>PlayersStep</c>).
    /// </summary>
    /// <param name="builder">The world's builder.</param>
    /// <exception cref="SimBuildException"><c>PlayerJoinListenerAlreadyBound</c> or <c>DuplicateSection</c>.</exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindPlayerJoinListener(this);
        builder.BindStateContributor(new PlayersStateContributor(this));
    }

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

        // Joining to Alive is an edge of LifeStates (a test pins the table), so the transition cannot fail on the record just created.
        _ = TryTransition(slot, LifeState.Alive);
    }

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerLeft(PlayerSlot slot, EntityId player)
    {
        SimAssert.That(_records[slot.Value].PlayerId == player, SimAssertCode.PlayersLeaveMismatch);
        _records[slot.Value] = default;
    }
}
