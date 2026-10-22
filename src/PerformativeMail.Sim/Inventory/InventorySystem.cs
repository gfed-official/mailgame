using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's Sim module (GDD <c>grid-inventory.md</c> R1; ADR-0009 sections 1 and 5; ADR-0003; story grid-inventory/001).
/// It owns the container registry (<see cref="ContainerTable"/>): one <c>po_intake</c> world container created at construction, and for
/// each joining player slot a <c>hotbar</c>, <c>player_inventory</c> and <c>cursor</c> created when Sim Core's <c>PlayerJoin</c> handler
/// calls <see cref="IPlayerJoinListener"/>. A leave keeps the containers (disconnect handling is M0) and a rejoin of the same slot reuses
/// them. Container dimensions are instance state: the def's width and <c>IStatSheet.Read(InventoryRows)</c> rows.
/// <para>
/// It binds the <c>ItemsExpire</c> phase (WorldItem expiry, water float and the over-cap re-check, story 013; see below), the still
/// empty <c>IntakeDrain</c> phase, a no-op conservation check (story 017's), the state contributor (containers and WorldItems), the join listener and the inventory event kinds (<see cref="InvEventDescriptors"/>, story 005). Its op
/// handlers are <c>Pick</c> and <c>Move</c> (story 007; <see cref="PickHandler"/>, <see cref="MoveHandler"/>), <c>Merge</c> (story 008),
/// <c>QuickMove</c> and <c>Return</c> (story 010; <see cref="QuickMoveHandler"/>, <see cref="ReturnHandler"/>) and <c>Close</c> (story
/// 011); the other kinds' handlers are story 019's. Each slot's cursor origin (<see cref="TryGetCursorOrigin"/>, story 010) is kept by the <see cref="Mutator"/>. It binds no snapshot source (the builder has no
/// <c>BindSnapshotSource</c> yet). Every container, entry and
/// occupancy write goes through the <see cref="Mutator"/> (story 005); an external container's audience is its
/// <see cref="Subscribers"/> set, which the open set (<see cref="OpenSet"/>, story 011) keeps in step. Storage is preallocated from
/// <see cref="InventorySettings"/>; the join and leave paths allocate nothing and never throw. Not thread-safe: Sim tick thread only.
/// </para>
/// <para>
/// It implements and publishes <see cref="IInventoryApi"/> (story 012; ADR-0009 Key Interfaces as implemented, ADR-0003 section 5) for
/// the systems that declare Grid Inventory a dependency in <c>SystemGraph</c> (<c>context.Api&lt;IInventoryApi&gt;(SystemId.GridInventory)</c>):
/// <see cref="Spawn"/>, <see cref="PeekOutfeed"/>, <see cref="TakeUnit"/>, <see cref="CreateWorldItem"/> (story 013),
/// <see cref="ConsumeUnit"/>, <see cref="DetachForThrow"/>, <see cref="TryInsert"/> and <see cref="CountItem"/> (story 015),
/// <see cref="WeightPoints"/> and <see cref="SpeedPermille"/> (story 016), plus the
/// internal <see cref="ReturnToIntake"/> (callers story 013 and the backlog, story 021). The mutating calls check the tick guard (<see cref="TickGuard"/>, through the injected
/// <see cref="ITickReader"/>) and run through the same <see cref="Mutator"/> as the op handlers, each in its own call scope and
/// <c>Begin</c>/<c>End</c> bracket. They allocate nothing and never throw on the tick path.
/// </para>
/// <para>
/// Story 013: the WorldItems (<see cref="WorldItems"/>: table, expiry FIFOs, tombstones, <c>worldSeq</c>), whose ids come from the
/// injected <see cref="EntityIds"/> (class <c>WorldItem</c>) and whose world records go to the connected slots of the injected
/// <see cref="ISlotView"/>; the <c>ItemsExpire</c> phase runs their expiry and cap re-check.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Composition: new InventorySystem(context.Content, context.StatSheet, context.CheckRing, context.Events, context.Tick, context.EntityIds, context.Slots)
/// ContainerId hotbar = inventory.HotbarOf(PlayerSlot.FromValue(0));   // none until the slot's PlayerJoin
/// </code>
/// </example>
[SimSystem(SystemId.GridInventory)]
public sealed class InventorySystem : ISimSystem, IPlayerJoinListener, IInventoryApi
{
    private struct PanelIds
    {
        public ContainerId Hotbar;
        public ContainerId PlayerInventory;
        public ContainerId Cursor;

        // The slot's player entity at its latest PlayerJoin: DetachForThrow's seal actor. Derived from the join ops (Sim Core's slot
        // table holds it too), so it is not in the canonical dump; the load path must rebuild the whole struct (TD-146).
        public EntityId Player;
    }

    private const int PanelContainerCount = 3;

    /// <summary>
    /// Where the entry on a slot's cursor came from (story grid-inventory/010; GDD R7 <c>return</c> l.95, "into its origin cells"). It is
    /// set and cleared at one choke point, the <see cref="InventoryMutator"/>: its <c>Place</c> into a cursor records the container,
    /// cells and rotation the entry left earlier in the same call (a pick, a move or a swap onto the cursor), or for a split piece its
    /// source entry's (a split onto the cursor), and its <c>Remove</c> from a cursor clears it (a whole place, a quick_move, a return,
    /// a leash put-away, a merge out of the cursor or a consume). So an origin is present exactly while the cursor holds
    /// <see cref="Entry"/>, the entry it was recorded for; a partial place or merge out keeps it (the entry keeps its id on the cursor),
    /// and an entry that arrives from no container in that call has none. The origin of a move or split onto the cursor is a Mail Lead
    /// planning default pending Evan (TD-153). Sim state that <c>return</c> reads: story 017's dump section carries it; a load without it
    /// leaves origin none and <c>return</c> uses the panel leg.
    /// </summary>
    internal struct CursorOrigin
    {
        /// <summary>The entry that was put on the cursor.</summary>
        public EntryId Entry;

        /// <summary>The container it came from.</summary>
        public ContainerId Container;

        /// <summary>Its top-left column there.</summary>
        public byte X;

        /// <summary>Its top-left row there.</summary>
        public byte Y;

        /// <summary>Its rotation there.</summary>
        public bool Rotated;
    }

    private readonly ContentSet _content;
    private readonly DefTable<ContainerDef> _defs;
    private readonly IStatSheet _stats;
    private readonly SimCheckRing _checkRing;
    private readonly InventorySettings _settings;
    private readonly ContainerTable _containers;
    private readonly EntryPool _entries;
    private readonly Occupancy _occupancy;
    private readonly MailIdArena _mailIds;
    private readonly InvRequestCheck _requestCheck;
    private readonly ContainerSubscribers _subscribers;
    private readonly InventoryMutator _mutator;
    private readonly AutoPlace _autoPlace;
    private readonly EventLogFacade _events;
    private readonly ITickReader _tick;
    private readonly DefIndex _hotbarDef;
    private readonly DefIndex _playerInventoryDef;
    private readonly DefIndex _cursorDef;
    private readonly PanelIds[] _panels = new PanelIds[PlayerSlot.Count];
    private readonly ContainerId _intake;
    private readonly ushort _intakeSlot;
    private readonly WorldItems _worldItems;
    private readonly PickupCore _pickup;
    private readonly OpenSet _openSet;
    private readonly ISlotView? _slots;

    /// <summary>Creates the module: settings from the content's <c>inventory.*</c> record, the four M-1a defs resolved once by id, and the <c>po_intake</c> container.</summary>
    /// <param name="content">The content set (container defs and the inventory balance).</param>
    /// <param name="stats">The stat sheets (container rows).</param>
    /// <param name="checkRing">The world's check ring (a full container table is recorded here).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>InventorySettingsInvalid</c> for an unusable capacity; <c>InventoryDefMissing</c> when an M-1a def is absent; <c>InventoryDefInvalid</c> when any container def is wider than 64 columns.</exception>
    internal InventorySystem(ContentSet content, IStatSheet stats, SimCheckRing checkRing)
        : this(content, stats, checkRing, InventorySettings.FromBalance((content ?? throw new ArgumentNullException(nameof(content))).Inventory), null)
    {
    }

    /// <summary>Creates the module with the world's event-log writer, tick reader, entity ids and slot view (the composition root's constructor).</summary>
    /// <param name="content">The content set (container defs and the inventory balance).</param>
    /// <param name="stats">The stat sheets (container rows).</param>
    /// <param name="checkRing">The world's check ring.</param>
    /// <param name="events">The world's event-log writer facade (records and seals are raised through it).</param>
    /// <param name="tick">The world's tick reader (the <see cref="IInventoryApi"/> tick guard, entry and arena check records).</param>
    /// <param name="entityIds">The world's entity id allocator (WorldItem ids, story 013).</param>
    /// <param name="slots">The slot view (the connected-slot audience of the replicated world records, story 013).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException">As the three-argument constructor.</exception>
    public InventorySystem(ContentSet content, IStatSheet stats, SimCheckRing checkRing, EventLogFacade events, ITickReader tick, EntityIds entityIds, ISlotView slots)
        : this(
            content,
            stats,
            checkRing,
            InventorySettings.FromBalance((content ?? throw new ArgumentNullException(nameof(content))).Inventory),
            events ?? throw new ArgumentNullException(nameof(events)),
            tick ?? throw new ArgumentNullException(nameof(tick)),
            entityIds ?? throw new ArgumentNullException(nameof(entityIds)),
            slots ?? throw new ArgumentNullException(nameof(slots)))
    {
    }

    /// <summary>Creates the module with ready settings (tests).</summary>
    /// <param name="content">The content set.</param>
    /// <param name="stats">The stat sheets.</param>
    /// <param name="checkRing">The check ring.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="events">The event-log writer; null gives an unattached facade of its own (a module that never mutates, e.g. catalogue tests: a raise through it is a misuse).</param>
    /// <param name="tick">The tick reader; null gives one that is always between ticks at tick 0, so every mutating <see cref="IInventoryApi"/> call is refused.</param>
    /// <param name="entityIds">The entity id allocator; null means <see cref="CreateWorldItem"/> creates nothing.</param>
    /// <param name="slots">The slot view; null gives the world records audience 0.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>InventorySettingsInvalid</c> when the settings cannot hold the intake container; <c>InventoryDefMissing</c> when an M-1a def is absent; <c>InventoryDefInvalid</c> when any container def is wider than 64 columns.</exception>
    internal InventorySystem(
        ContentSet content,
        IStatSheet stats,
        SimCheckRing checkRing,
        InventorySettings settings,
        EventLogFacade? events = null,
        ITickReader? tick = null,
        EntityIds? entityIds = null,
        ISlotView? slots = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(checkRing);
        ArgumentNullException.ThrowIfNull(settings);
        _content = content;
        _defs = content.Containers;
        _stats = stats;
        _checkRing = checkRing;
        _settings = settings;
        _containers = new ContainerTable(settings.ContainerCapacity);
        _entries = new EntryPool(settings.EntryPoolCapacity, checkRing, settings.EntryInitialLastId);
        _mailIds = new MailIdArena(settings.MailIdArenaUnits, checkRing);
        _requestCheck = new InvRequestCheck(_containers, _entries, _mailIds);
        _subscribers = new ContainerSubscribers(settings.ContainerCapacity);
        _openSet = new OpenSet(_containers);
        _slots = slots;

        // Rows are clamped to inventory.maxRows by the stat sheet; widths are bounded here for every def (the bitmaps hold 64 columns)
        // and the cellOwner array is sized by the widest def rather than the full 64 (ADR-0009 memory guidance).
        int widest = WidestGrid(_defs);
        if (widest > Occupancy.BitmapWidth)
        {
            throw new SimBuildException(SimBuildErrorCode.InventoryDefInvalid, $"A container def is wider ({widest}) than the {Occupancy.BitmapWidth}-column occupancy bitmap.");
        }

        _occupancy = new Occupancy(settings.ContainerCapacity, content.Inventory.MaxRows, Math.Max(1, widest));

        // Story 009: the StackKey index (heads sized from the entry pool) and the negative-fit cache (the content's shapes per
        // container) are derived memos, preallocated here like the rest of the storage.
        EventLogFacade facade = events ?? new EventLogFacade(new SimCounters());
        _events = facade;
        _tick = tick ?? new BetweenTicksReader();
        var noFit = new NegativeFitCache(settings.ContainerCapacity, NegativeFitCache.WaysFor(content));

        // Story 012: the outfeed FIFO memo; its node pool is 2 x the entry pool (InventorySettings.OutfeedNodeCapacity).
        var outfeed = new OutfeedFifo(settings.OutfeedNodeCapacity, settings.ContainerCapacity, _entries, facade.Counters);

        // Story 015: the itemId -> units index CountItem reads (keys sized from the entry pool), maintained by the mutator's primitives.
        var itemCounts = new ItemCountIndex(_entries.Capacity, _containers.Capacity, facade.Counters);
        // Story 016: the per-slot weight points memo, its class points read once from the settings.
        var weight = new Weight(content, settings);
        DefIndex intakeDef = Resolve("po_intake");
        _hotbarDef = Resolve("hotbar");
        _playerInventoryDef = Resolve("player_inventory");
        _cursorDef = Resolve("cursor");
        _mutator = new InventoryMutator(_containers, _entries, _occupancy, _mailIds, _subscribers, facade, content, stats, new StackIndex(_entries), itemCounts, noFit, outfeed, weight, _cursorDef, slots);
        _autoPlace = new AutoPlace(_containers, _defs, _entries, _occupancy, _mutator, noFit, facade.Counters);

        // Story 013: the WorldItems, sized from inventory.worldItemCapacity; durations and the cap are read from the balance once here.
        _worldItems = new WorldItems(this, _mutator, _entries, content.Inventory, settings.WorldItemCapacity, facade, _tick, entityIds, checkRing, facade.Counters);

        // Story 014: the pickup core (story 019's range-gated Pickup handler calls it; no Pickup handler is bound until then).
        _pickup = new PickupCore(this, content, stats, _tick, facade.Counters);

        if (!TryCreate(intakeDef, ContainerHeader.NoOwner, null, out _intake))
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, "The container table cannot hold the po_intake container.");
        }

        _ = _containers.TryGetSlot(_intake, out _intakeSlot);
    }

    /// <summary>The settings the tables were allocated from.</summary>
    public InventorySettings Settings => _settings;

    /// <summary>The number of live containers.</summary>
    public int LiveContainerCount => _containers.LiveCount;

    /// <summary>The Post Office intake container (<c>po_intake</c>), created at construction.</summary>
    public ContainerId IntakeContainer => _intake;

    /// <summary>The container table (state contributor, later handlers, tests).</summary>
    internal ContainerTable Containers => _containers;

    /// <summary>The entry pool (story 002 preallocation; the mutator of story 005 is its caller).</summary>
    internal EntryPool Entries => _entries;

    /// <summary>The per-container occupancy (story 002 preallocation; every live container has a reset grid).</summary>
    internal Occupancy Occupancy => _occupancy;

    /// <summary>The mail-ids arena (story 003 preallocation; the mutator of story 005 is its caller).</summary>
    internal MailIdArena MailIds => _mailIds;

    /// <summary>The shared steps of every inventory handler's <c>Validate</c> (story 004, plus the arena-reservation step of story 005; the handlers of stories 007-010 call them).</summary>
    internal InvRequestCheck RequestCheck => _requestCheck;

    /// <summary>The one mutation layer (story 005): handlers' <c>Commit</c> and <c>IInventoryApi</c> calls write state only through it.</summary>
    internal InventoryMutator Mutator => _mutator;

    /// <summary>AutoPlace (story 009): the shared placement passes of quick_move, spawn, return, inserter, belt, pickup, catch and grant.</summary>
    internal AutoPlace AutoPlace => _autoPlace;

    /// <summary>The per-container subscriber sets, maintained by <see cref="TryOpenExternal"/>, <see cref="CloseExternal"/> and the leave path (story 011).</summary>
    internal ContainerSubscribers Subscribers => _subscribers;

    /// <summary>The players' open sets (story 011): each slot's external and the <see cref="Inventory.OpenSet.IsInOpenSet"/> predicate of the <c>not_open</c> step.</summary>
    internal OpenSet OpenSet => _openSet;

    /// <summary>
    /// The backlog seam (story 012, Planner R6; ADR-0017 section 6): whether the Intake accepts spawns and returns now. Always true at
    /// M-1a; story 021 makes it false while the backlog holds entries (nothing overtakes the backlog), so <see cref="Spawn"/> into the
    /// Intake and <see cref="ReturnToIntake"/> place nothing and probe nothing.
    /// </summary>
    internal bool IntakeAccepting => true;

    /// <summary>The Intake's version (story 013's interim return memo: a failed return is not retried against the same version in a tick).</summary>
    internal uint IntakeVersion => _containers[_intakeSlot].Version;

    /// <summary>The WorldItems (story 013): table, expiry FIFOs, tombstones and <c>worldSeq</c>; the pickup story's remove and resolve hooks.</summary>
    internal WorldItems WorldItems => _worldItems;

    /// <summary>The pickup core (story 014): everything a <c>pickup</c> does after Player Character's P13 chain; story 019's handler calls it.</summary>
    internal PickupCore Pickup => _pickup;

    /// <summary>The hotbar of <paramref name="slot"/>, or none when the slot has no containers.</summary>
    /// <param name="slot">The player slot.</param>
    public ContainerId HotbarOf(PlayerSlot slot) => slot.IsNone ? default : _panels[slot.Value].Hotbar;

    /// <inheritdoc />
    /// <remarks>Reads the mutator's per-slot memo (<see cref="Weight.Points"/>); 0 for a none or out-of-range slot.</remarks>
    public short WeightPoints(PlayerSlot slot) => slot.IsNone || slot.Value >= PlayerSlot.Count ? (short)0 : _mutator.Weight.Points(slot.Value);

    /// <inheritdoc />
    /// <remarks>
    /// D1 (<see cref="Weight.SpeedPermille(int, int, int, int, ref SimCounterSink)"/>) over the memo's <see cref="int"/>
    /// accumulator (not the saturated <see cref="WeightPoints"/>), the settings' <c>ptsPermille</c> and floor, and
    /// <c>IStatSheet.Read(Stat.WeightPenaltyPm, slot: slot)</c>; 1000 for a none or out-of-range slot.
    /// </remarks>
    public int SpeedPermille(PlayerSlot slot)
    {
        if (slot.IsNone || slot.Value >= PlayerSlot.Count)
        {
            return Weight.Unit;
        }

        int penaltyPm = _stats.Read(Stat.WeightPenaltyPm, slot: slot);
        SimCounterSink sink = _events.Counters.PrimitiveSink();
        return Weight.SpeedPermille(_mutator.Weight.RawPoints(slot.Value), _settings.PtsPermille, _settings.WeightSpeedFloor, penaltyPm, ref sink);
    }

    /// <summary>The player inventory of <paramref name="slot"/>, or none when the slot has no containers.</summary>
    /// <param name="slot">The player slot.</param>
    public ContainerId PlayerInventoryOf(PlayerSlot slot) => slot.IsNone ? default : _panels[slot.Value].PlayerInventory;

    /// <summary>The cursor container of <paramref name="slot"/>, or none when the slot has no containers.</summary>
    /// <param name="slot">The player slot.</param>
    public ContainerId CursorOf(PlayerSlot slot) => slot.IsNone ? default : _panels[slot.Value].Cursor;

    /// <summary>
    /// The origin of the entry on <paramref name="slot"/>'s cursor (story 010; <see cref="ReturnHandler"/> reads it), as the
    /// <see cref="InventoryMutator"/> keeps it (see <see cref="CursorOrigin"/>): present exactly while the cursor holds the entry it was
    /// recorded for. No per-read entry check is needed: the mutator's <c>Place</c> and <c>Remove</c> are the only writes of a cursor's
    /// cells, and they record and clear it. Pure; allocation-free.
    /// </summary>
    /// <param name="slot">The player slot.</param>
    /// <param name="origin">The origin when present; default otherwise.</param>
    /// <returns>True when an origin is recorded for the entry on the cursor.</returns>
    internal bool TryGetCursorOrigin(PlayerSlot slot, out CursorOrigin origin)
    {
        if (slot.IsNone || slot.Value >= PlayerSlot.Count)
        {
            origin = default;
            return false;
        }

        return _mutator.TryGetCursorOrigin(slot.Value, out origin);
    }

    /// <summary>The table slots of <paramref name="slot"/>'s hotbar and player_inventory (story 014: the pickup and catch rows).</summary>
    /// <param name="slot">The player slot.</param>
    /// <param name="hotbar">The hotbar's table slot.</param>
    /// <param name="playerInventory">The player_inventory's table slot.</param>
    /// <returns>False for a none or out-of-range slot, or a slot with no panel.</returns>
    internal bool TryGetPanel(PlayerSlot slot, out ushort hotbar, out ushort playerInventory)
    {
        playerInventory = 0;
        if (slot.IsNone || slot.Value >= PlayerSlot.Count || !_containers.TryGetSlot(_panels[slot.Value].Hotbar, out hotbar))
        {
            hotbar = 0;
            return false;
        }

        // A panel's three containers are created together (OnPlayerJoined), so a live hotbar has a live player_inventory.
        return _containers.TryGetSlot(_panels[slot.Value].PlayerInventory, out playerInventory);
    }

    /// <summary>Copies the header of <paramref name="id"/>.</summary>
    /// <param name="id">The container id.</param>
    /// <param name="header">The header when found.</param>
    /// <returns>True when the container is live.</returns>
    public bool TryGetHeader(ContainerId id, out ContainerHeader header)
    {
        if (_containers.TryGetSlot(id, out ushort slot))
        {
            header = _containers[slot];
            return true;
        }

        header = default;
        return false;
    }

    /// <summary>
    /// Binds the <c>ItemsExpire</c> phase (WorldItem expiry and cap re-check, story 013), the empty <c>IntakeDrain</c> phase, the stub
    /// conservation check, the state contributor, the join listener, every <see cref="InvEventDescriptors"/> kind and the
    /// <see cref="InvOpDescriptors.Pick"/> and <see cref="InvOpDescriptors.Move"/> handlers (story 007), the
    /// <see cref="InvOpDescriptors.Merge"/> handler (story 008), the <see cref="InvOpDescriptors.QuickMove"/> and
    /// <see cref="InvOpDescriptors.Return"/> handlers (story 010) and the <see cref="InvOpDescriptors.Close"/> handler (story 011). Binds no
    /// other op (story 019) and no snapshot source.
    /// </summary>
    /// <param name="builder">The world's builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayerJoinListenerAlreadyBound</c>, <c>PlayerJoinListenerLimit</c>, <c>DuplicateSection</c>, <c>DuplicateEventKind</c> or <c>DuplicateOpHandler</c> (another <c>Pick</c>, <c>Move</c>, <c>Merge</c>, <c>QuickMove</c>, <c>Return</c> or <c>Close</c> handler).</exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(PhaseId.ItemsExpire, new ItemsExpirePhase(_worldItems));
        builder.Bind(PhaseId.IntakeDrain, new IntakeDrainPhase());
        builder.BindTickEnd(new InventoryConservationCheck());
        builder.BindStateContributor(new InventoryStateContributor(_containers, _worldItems));
        builder.BindPlayerJoinListener(this);
        foreach (EventDescriptor descriptor in InvEventDescriptors.All)
        {
            builder.BindEventKind(descriptor);
        }

        builder.BindOp(InvOpDescriptors.Pick, new PickHandler(this));
        builder.BindOp(InvOpDescriptors.Move, new MoveHandler(this, _content, _stats));
        builder.BindOp(InvOpDescriptors.Merge, new MergeHandler(this, _content, _stats));
        builder.BindOp(InvOpDescriptors.QuickMove, new QuickMoveHandler(this, _content, _stats));
        builder.BindOp(InvOpDescriptors.Return, new ReturnHandler(this, _content, _stats));
        builder.BindOp(InvOpDescriptors.Close, new CloseHandler(this));
    }

    /// <summary>
    /// Makes <paramref name="c"/> <paramref name="slot"/>'s external container (GDD R10; story 011; called by story 019's range-gated
    /// <c>Open</c> handler): an external that slot already had is closed for that slot only (its subscriber bit cleared; other slots'
    /// bits are untouched), then <paramref name="c"/> is recorded and the slot's bit set in its <see cref="Subscribers"/> mask, so its
    /// records reach the slot from the next raise. No record, no version, no tick guard (it changes no container). False with nothing
    /// changed for a none slot, a dead or never-issued id, or a container with an owner slot (a panel or cursor is always in its owner's
    /// set and never another's); true with nothing changed when <paramref name="c"/> is already the slot's external. Allocation-free.
    /// </summary>
    /// <param name="slot">The opening player's slot.</param>
    /// <param name="c">The external container.</param>
    /// <returns>True when <paramref name="c"/> is the slot's external afterwards.</returns>
    internal bool TryOpenExternal(PlayerSlot slot, ContainerId c)
    {
        if (slot.IsNone || !_containers.TryGetSlot(c, out ushort containerSlot) || _containers[containerSlot].OwnerSlot != ContainerHeader.NoOwner)
        {
            return false;
        }

        if (_openSet.ExternalOf(slot) == c)
        {
            return true;
        }

        ClearExternal(slot);
        _openSet.SetExternal(slot, c);
        _subscribers.Add(containerSlot, slot);
        return true;
    }

    /// <summary>
    /// Closes <paramref name="slot"/>'s external container (story 011; the <c>Close</c> op, and story 019's leash with
    /// <see cref="CloseCause.Leash"/>): the slot's external is cleared and its bit removed from that container's
    /// <see cref="Subscribers"/> mask, with no record and no version. With <see cref="CloseCause.Leash"/> the cursor entry is then put
    /// away in the same call (GDD R10, H-R10-1; <see cref="PutAwayCursor"/>); <see cref="CloseCause.Close"/> leaves it on the cursor
    /// (R11: the cursor survives a manual close), so the <c>Close</c> handler raises nothing in its op's scope. A none slot or a slot
    /// with no external changes nothing (no put-away either). Stays off <see cref="IInventoryApi"/> until the ADR-0014 section 6
    /// knockdown chain needs it (TD-149). Allocation-free.
    /// </summary>
    /// <param name="slot">The player slot.</param>
    /// <param name="cause">Why it closes.</param>
    /// <exception cref="SimAssertException">A leash put-away outside a tick phase (SIM_ASSERT builds only; the external is closed first).</exception>
    internal void CloseExternal(PlayerSlot slot, CloseCause cause)
    {
        if (slot.IsNone || _openSet.ExternalOf(slot).IsNone)
        {
            return;
        }

        ClearExternal(slot);
        if (cause == CloseCause.Leash)
        {
            PutAwayCursor(slot);
        }
    }

    /// <summary>
    /// The leash put-away (GDD R10 and R9 cursor row; story 011 AC 5): the cursor entry of <paramref name="slot"/>, if any, is AutoPlaced
    /// into the panel by <see cref="TargetOrder.CursorRow"/> (the entry as source, its whole count, <see cref="MergePolicy.Merge"/>,
    /// pass-first), then the arena reservation. An empty cursor (or a slot with no panel) changes nothing and takes no tick guard. Else the
    /// tick guard (<see cref="TickGuard"/>: out of phase counts, asserts under <c>SIM_ASSERT</c>, and in Release leaves the entry on the
    /// cursor); then a refused plan (<c>no_room</c>, or <c>capacity_exhausted</c> for a mail merge's union block) records its no-fit facts
    /// and leaves the entry on the cursor with no record; a stackable may go in partly, the rest staying on the cursor (R8). A plan that
    /// passed commits in its own call scope and one <c>Begin</c>/<c>End</c> bracket, sealed with the slot's player as actor and cause
    /// <see cref="InvCause.None"/> (as <see cref="DetachForThrow"/>), so the cursor and each target it touched bump once.
    /// </summary>
    private void PutAwayCursor(PlayerSlot slot)
    {
        ref PanelIds panel = ref _panels[slot.Value];
        if (!_containers.TryGetSlot(panel.Cursor, out ushort cursor))
        {
            return;
        }

        ushort entrySlot = _occupancy.OwnerAt(cursor, _containers[cursor].W, 0, 0);
        if (entrySlot == Occupancy.Free || !TickGuard.IsMutable(_tick, _events.Counters))
        {
            return;
        }

        // A panel's three containers are created together (OnPlayerJoined), so a live cursor has a live hotbar and player_inventory.
        _ = _containers.TryGetSlot(panel.Hotbar, out ushort hotbar);
        _ = _containers.TryGetSlot(panel.PlayerInventory, out ushort playerInventory);
        ref EntryRecord e = ref _entries[entrySlot];
        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        Span<ushort> targets = stackalloc ushort[TargetOrder.CursorRowLength];
        TargetOrder.CursorRow(subject.Category, hotbar, playerInventory, targets);
        var plan = new InvPlan();
        ushort reason = _autoPlace.Plan(ref plan, in subject, e.Count, entrySlot, targets, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
        if (reason == 0)
        {
            reason = _requestCheck.CheckArenaReservation(in plan);
        }

        if (reason != 0)
        {
            _autoPlace.RecordNoFits(in ap);
            return;
        }

        var extra = new InvReplyExtra();
        using (EventCallScope scope = _events.BeginCall())
        {
            _mutator.Begin(ref plan, ActorId.FromEntity(panel.Player), InvCause.None);
            _autoPlace.Commit(ref plan, in ap);
            _mutator.End(ref plan, ref extra);
        }
    }

    /// <summary>
    /// Creates a container of <paramref name="def"/> with explicit dimensions and owner (boot-time or test-fixture use: the external
    /// containers proper — chest, depot — arrive in M-1b; until then tests use this for a fixture external container). False when the
    /// table is full or the dimensions are outside what the occupancy preallocation holds (0 to its <c>MaxWidth</c> / <c>MaxRows</c>);
    /// both are checked before anything is created, so a refusal leaves no container behind.
    /// </summary>
    /// <param name="def">The def ordinal.</param>
    /// <param name="w">Columns.</param>
    /// <param name="h">Rows.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="ownerSlot">The owner slot value, or <see cref="ContainerHeader.NoOwner"/> for an external container.</param>
    /// <param name="id">The new id.</param>
    /// <returns>True when created.</returns>
    internal bool TryCreateContainer(DefIndex def, int w, int h, ContainerLayout layout, byte ownerSlot, out ContainerId id)
    {
        if (w < 0 || w > _occupancy.MaxWidth || h < 0 || h > _occupancy.MaxRows)
        {
            id = default;
            return false;
        }

        if (!_containers.TryCreate(def, w, h, layout, ownerSlot, out id))
        {
            return false;
        }

        _ = _containers.TryGetSlot(id, out ushort slot);
        _occupancy.Reset(slot, w, h);
        _mutator.Outfeed.ResetSlot(slot);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Per id (R1, Planner resolution): <see cref="PlacementSubject.ForMail"/>, <see cref="Placement.AutoPlace.Plan"/> with no source and
    /// one unit, the arena reservation of the planned blocks plus the new entry's one-unit block (on a per-id plan, so blocks acquired by
    /// earlier ids are not counted again), <see cref="EntryPool.TryAllocate"/> and the block, then <see cref="Placement.AutoPlace.Commit(ref InvPlan, in AutoPlacePlan, ushort)"/>
    /// into the call's one plan. After a refused plan the loop stops, and once <c>End</c> has sealed the Intake the failing plan's
    /// no-fit facts are recorded at the sealed version (<see cref="Placement.AutoPlace.RecordNoFitsAtCurrentVersion"/>), outside
    /// planning: the failing id was scanned after every earlier id was placed and the seal is the only later change, so the facts
    /// still hold and a repeat into the unchanged Intake probes nothing (recording at the pre-seal version could never hit once
    /// <c>Placed &gt; 0</c> bumped it). A NoSource plan places up to <c>effMax</c> units as one entry (TD-130 item 2), so the loop is per id. The
    /// Intake is consulted through <see cref="IntakeAccepting"/> first.
    /// </remarks>
    public SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p)
    {
        ushort none = (ushort)Math.Min(ids.Length, ushort.MaxValue);
        if (!TickGuard.IsMutable(_tick, _events.Counters))
        {
            return new SpawnResult(0, none);
        }

        if (!IsWellFormedSpawn(c, mailKind, ids, out ushort slot) || (slot == _intakeSlot && !IntakeAccepting))
        {
            return new SpawnResult(0, none); // bad_request-style refusal (or the backlog gate): nothing mutated, no event
        }

        PlacementSubject subject = PlacementSubject.ForMail(_content.MailKinds[mailKind], mailKind, addressId, flags, _stats);
        MergePolicy policy = _mutator.HasOutfeed(slot) ? MergePolicy.NoMerge : p; // every Intake insertion is NoMerge (R14)
        ReadOnlySpan<ushort> targets = new ReadOnlySpan<ushort>(in slot);
        uint tick = _tick.CurrentTick;
        Span<uint> one = stackalloc uint[1];
        var plan = new InvPlan();
        _ = plan.Touch(slot);
        var extra = new InvReplyExtra();
        int placed = 0;
        bool refused = false;
        AutoPlacePlan ap = default;
        using (EventCallScope scope = _events.BeginCall())
        {
            _mutator.Begin(ref plan, ActorId.System, InvCause.System);
            for (; placed < ids.Length; placed++)
            {
                var idPlan = new InvPlan();
                ushort reason = _autoPlace.Plan(ref idPlan, in subject, 1, Placement.AutoPlace.NoSource, targets, policy, PlaceOrder.PassFirst, out ap);
                if (reason == 0)
                {
                    _ = idPlan.AddAcquiredBlock(1); // the new entry's one-unit block
                    reason = _requestCheck.CheckArenaReservation(in idPlan);
                }

                if (reason != 0)
                {
                    refused = true; // ap is kept: its no-fit facts are recorded after the seal
                    break;
                }

                if (!_entries.TryAllocate(tick, out ushort entrySlot, out _))
                {
                    break; // the pool wrote its check record; nothing mutated for this id
                }

                one[0] = ids[placed].Value;
                ref EntryRecord e = ref _entries[entrySlot];
                if (!_mailIds.TryCreate(one, tick, out e.MailIds))
                {
                    _entries.Free(entrySlot); // unreachable after the reservation; the arena wrote its record
                    break;
                }

                e.IsMail = true;
                e.MailKind = mailKind.Value;
                e.AddressId = addressId;
                e.Flags = flags;
                e.W = subject.W;
                e.H = subject.H;
                _autoPlace.Commit(ref plan, in ap, entrySlot);
            }

            _mutator.End(ref plan, ref extra);
        }

        if (refused)
        {
            _autoPlace.RecordNoFitsAtCurrentVersion(in ap);
        }

        return new SpawnResult((ushort)placed, (ushort)(none - placed));
    }

    /// <inheritdoc />
    public bool PeekOutfeed(ContainerId c, Selector s, out UnitRef u)
    {
        if (!TryOutfeedHead(c, s, out _, out ushort entrySlot))
        {
            u = default;
            return false;
        }

        u = UnitOf(entrySlot);
        return true;
    }

    /// <inheritdoc />
    public bool TakeUnit(ContainerId c, Selector s, ActorId actor, out UnitRef u)
    {
        u = default;
        if (!TickGuard.IsMutable(_tick, _events.Counters) || !TryOutfeedHead(c, s, out ushort slot, out ushort entrySlot))
        {
            return false;
        }

        UnitRef unit = UnitOf(entrySlot);
        Span<uint> taken = stackalloc uint[1];
        var plan = new InvPlan();
        _ = plan.Touch(slot);
        var extra = new InvReplyExtra();
        int took;
        using (EventCallScope scope = _events.BeginCall())
        {
            _mutator.Begin(ref plan, actor, InvCause.None);
            took = _mutator.TakeOldest(ref plan, entrySlot, 1, taken);
            _mutator.End(ref plan, ref extra);
        }

        // Unreachable (the head is a live placed mail entry and k = 1); kept so TakeUnit's contract does not rely on that invariant.
        if (took != 1)
        {
            return false;
        }

        u = unit;
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The tick guard, then the ref's checks (a live container, a live entry whose <c>ContainerSlot</c> is that container, a count of at
    /// least 1, a defined reason), then <see cref="InventoryMutator.TakeOldest"/> of 1 in one call scope and one bracket with cause
    /// <see cref="InvCause.Consumed"/>, so the records, the seal and the last-unit removal are <c>TakeOldest</c>'s (story 012).
    /// </remarks>
    public bool ConsumeUnit(EntryRef entry, ConsumeReason reason, ActorId actor)
    {
        if (!TickGuard.IsMutable(_tick, _events.Counters) || reason > ConsumeReason.ReturnedToSender
            || !_containers.TryGetSlot(entry.Container, out ushort slot) || !_entries.TryGetSlot(entry.Entry, out ushort entrySlot))
        {
            return false;
        }

        ref EntryRecord e = ref _entries[entrySlot];
        if (e.ContainerSlot != slot || e.Count < 1)
        {
            return false; // stale ref (the entry moved, or is detached) or an empty entry: nothing mutated, no event
        }

        Span<uint> taken = stackalloc uint[1];
        var plan = new InvPlan();
        _ = plan.Touch(slot);
        var extra = new InvReplyExtra();
        int took;
        using (EventCallScope scope = _events.BeginCall())
        {
            _mutator.Begin(ref plan, actor, InvCause.Consumed);
            took = _mutator.TakeOldest(ref plan, entrySlot, 1, taken);
            _mutator.End(ref plan, ref extra);
        }

        // story 017 ledger hook: the taken unit (taken[0] for mail) crosses PLACED -> CONSUMED with reason.
        return took == 1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The tick guard; <c>bad_request</c> unless <paramref name="slot"/> has a hotbar; then the id through <see cref="Ops.InvRequestCheck"/>'s
    /// own entry-id steps with <paramref name="slot"/> as the temp namespace: <c>bad_request</c> for none, a real id above
    /// <see cref="Storage.EntryPool.LastId"/>, a temp id with any of bits 23-30 set or a temp id of another slot's namespace;
    /// <c>stale_entry</c> for a temp id of <paramref name="slot"/>'s own namespace (none is mapped yet, so it was "never mapped",
    /// ADR-0009 l.78 and l.165) or a real id at or below the high-water that is not live (consumed, merged away, retired or freed; entry
    /// ids have no tombstone, ADR-0009 l.116-117); then <c>bad_request</c> for a live entry not placed in this hotbar (the cursor's entry, a panel
    /// entry, another slot's hotbar entry, or a detached entry, i.e. a duplicate release); then <c>stale_entry</c> when
    /// <paramref name="units"/> is not the entry's count. Then
    /// <see cref="InventoryMutator.Remove"/> in one call scope and one bracket, actor the slot's player, so the records and seal are
    /// <c>Remove</c>'s and the entry is left live and detached. The seal's cause is <see cref="InvCause.None"/>: the closest existing
    /// cause, since the thrower changes their own hotbar ("an ordinary change by the actor"); <c>Taken</c> is another actor's take and
    /// <c>Consumed</c> would mislabel units that are still live. A refusal raises nothing and changes nothing.
    /// </remarks>
    public DetachResult DetachForThrow(PlayerSlot slot, EntryId entryId, byte units)
    {
        var badRequest = new DetachResult((InvReason)InvReasons.BadRequest, default, 0);
        if (!TickGuard.IsMutable(_tick, _events.Counters))
        {
            return badRequest; // out of phase: counted by the guard, nothing mutated
        }

        if (slot.IsNone || slot.Value >= PlayerSlot.Count || !_containers.TryGetSlot(_panels[slot.Value].Hotbar, out ushort hotbar))
        {
            return badRequest;
        }

        // The id goes through the op path's own two steps, with the thrower's slot as the temp namespace (ADR-0009 l.78, l.116-117, l.165).
        ushort reason = InvRequestCheck.CheckEntryNamespace(_entries, entryId.Value, slot, false);
        if (reason == 0)
        {
            reason = InvRequestCheck.ResolveEntry(_entries, entryId.Value);
        }

        if (reason != 0)
        {
            return new DetachResult((InvReason)reason, default, 0);
        }

        _ = _entries.TryGetSlot(entryId, out ushort entrySlot); // a live real id: ResolveEntry passed

        if (_entries[entrySlot].ContainerSlot != hotbar)
        {
            return badRequest; // the cursor's, a panel's or another slot's entry, or a live detached entry (a duplicate release)
        }

        if (_entries[entrySlot].Count != units)
        {
            return new DetachResult(InvReason.StaleEntry, default, 0);
        }

        var plan = new InvPlan();
        _ = plan.Touch(hotbar);
        var extra = new InvReplyExtra();
        using (EventCallScope scope = _events.BeginCall())
        {
            _mutator.Begin(ref plan, ActorId.FromEntity(_panels[slot.Value].Player), InvCause.None);
            _mutator.Remove(ref plan, entrySlot);
            _mutator.End(ref plan, ref extra);
        }

        // story 017 ledger hook: the entry's units cross PLACED -> FLIGHT. Story 016's weight (WeightPoints) follows from Remove.
        return new DetachResult(default, entryId, units);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The tick guard (<c>bad_request</c>), then <see cref="ValidateCatch"/>; a refusal writes its always-on record outside Validate (the
    /// arena's <see cref="CheckCode.InventoryArenaExhausted"/> for a refused merge block, ADR-0009 section 5; the WorldItem table's
    /// <see cref="CheckCode.InventoryWorldItemCapacity"/> for a full table) and returns with nothing else changed; else
    /// <see cref="CommitCatch"/>. A whole fumble records the refused AutoPlace's no-fit facts after the decision, as
    /// <see cref="PutAwayCursor"/> does; a committed placement records them in its commit.
    /// </remarks>
    public CatchResult TryCatch(PlayerSlot slot, in FlightPayload flight)
    {
        if (!TickGuard.IsMutable(_tick, _events.Counters))
        {
            return new CatchResult(0, 0, (InvReason)InvReasons.BadRequest);
        }

        var plan = new InvPlan();
        CatchResult result = ValidateCatch(slot, in flight, ref plan, out CatchPlan c);
        if (c.Refused)
        {
            if (c.ArenaRefused)
            {
                _mailIds.RecordReservationRefusal(plan.AcquiredBlocks, _tick.CurrentTick);
            }
            else if (c.TableFull)
            {
                _worldItems.RecordCapacityRefusal(_tick.CurrentTick);
            }

            return result;
        }

        if (!c.Places)
        {
            _autoPlace.RecordNoFits(in c.Placement); // a whole fumble: the plan moved nothing (no_room)
        }

        CommitCatch(ref plan, in c, in flight);
        return result;
    }

    /// <summary>
    /// The pure half of <see cref="TryCatch"/> (story 014; ADR-0009 section 2): <c>bad_request</c> for a slot with no panel or an entry
    /// that is not live and detached or is held by a WorldItem; then the panel AutoPlace (<see cref="PickupCore.PlanIntoPanel"/>, the
    /// pickup rows): <c>no_room</c> (and the unreachable <c>not_allowed</c> / <c>not_carryable</c>, asserted there) is a whole fumble with
    /// reason 0; a plan that moved units then takes the arena reservation of its mail merge union blocks, and a refusal there refuses the
    /// whole catch with <c>capacity_exhausted</c>, <c>{0, 0}</c>, nothing mutated (ADR-0009 section 5 l.157; TD-154 item 7); last, any
    /// fumble needs a creatable WorldItem (<see cref="World.WorldItems.CanCreate"/>), else the whole catch is refused with
    /// <c>capacity_exhausted</c>, <c>{0, 0}</c>. Reads state only; writes nothing but <paramref name="plan"/> and <paramref name="c"/>.
    /// </summary>
    /// <param name="slot">The catcher's slot.</param>
    /// <param name="flight">The flight.</param>
    /// <param name="plan">The call's plan.</param>
    /// <param name="c">The catch to commit.</param>
    /// <returns>The result the commit will produce, or the refusal.</returns>
    internal CatchResult ValidateCatch(PlayerSlot slot, in FlightPayload flight, ref InvPlan plan, out CatchPlan c)
    {
        c = default;
        c.Refused = true;
        var badRequest = new CatchResult(0, 0, (InvReason)InvReasons.BadRequest);
        if (!TryGetPanel(slot, out ushort hotbar, out ushort playerInventory)
            || !_entries.TryGetSlot(flight.Entry, out ushort entrySlot)
            || _entries[entrySlot].ContainerSlot != EntryRecord.NoContainer || _worldItems.OwnsEntry(entrySlot))
        {
            return badRequest;
        }

        c.EntrySlot = entrySlot;
        c.Actor = ActorId.FromEntity(_panels[slot.Value].Player);
        plan.SetPrimary(hotbar, playerInventory);
        byte units = _entries[entrySlot].Count;
        ushort reason = _pickup.PlanIntoPanel(ref plan, entrySlot, hotbar, playerInventory, out c.Placement);
        if (reason == CoreReason.BadRequest)
        {
            return badRequest; // unreachable: a live source, two live distinct targets, units = its count
        }

        if (reason == 0 && _requestCheck.CheckArenaReservation(in plan) != 0)
        {
            c.ArenaRefused = true; // ADR-0009 section 5: refused, nothing mutated (TD-154 item 7)
            return new CatchResult(0, 0, InvReason.CapacityExhausted);
        }

        c.Places = reason == 0;
        byte unitsIn = c.Places ? (byte)c.Placement.MovedUnits : (byte)0;
        byte fumbled = (byte)(units - unitsIn);
        if (fumbled > 0 && !_worldItems.CanCreate())
        {
            c.Places = false;
            c.TableFull = _worldItems.TableFull;
            return new CatchResult(0, 0, InvReason.CapacityExhausted);
        }

        c.Refused = false;
        return new CatchResult(unitsIn, fumbled, 0);
    }

    /// <summary>
    /// The mutating half of <see cref="TryCatch"/>: in one call scope, the panel commit when <see cref="CatchPlan.Places"/> (one bracket,
    /// the catcher as actor, cause <see cref="InvCause.None"/>), then, if units are left on the entry, the fumble
    /// (<see cref="World.WorldItems.Create"/>, <see cref="WorldItemOrigin.Thrown"/>, created now, in its own nested scope). Never fails:
    /// Validate proved the WorldItem creatable.
    /// </summary>
    /// <param name="plan">The plan <see cref="ValidateCatch"/> passed.</param>
    /// <param name="c">The catch <see cref="ValidateCatch"/> passed.</param>
    /// <param name="flight">The flight (the fumble's position and water flag).</param>
    internal void CommitCatch(ref InvPlan plan, in CatchPlan c, in FlightPayload flight)
    {
        using EventCallScope scope = _events.BeginCall();
        if (c.Places)
        {
            var extra = new InvReplyExtra();
            _mutator.Begin(ref plan, c.Actor, InvCause.None);
            _autoPlace.Commit(ref plan, in c.Placement);
            _mutator.End(ref plan, ref extra);

            // story 017 ledger hook: the units in cross FLIGHT -> PLACED.
        }

        // A whole placement moved the entry itself and a full merge freed it; otherwise the newest units are still on the entry.
        if (_entries[c.EntrySlot].IsLive && _entries[c.EntrySlot].Id == flight.Entry && _entries[c.EntrySlot].ContainerSlot == EntryRecord.NoContainer)
        {
            // Cannot be None: ValidateCatch proved a WorldItem creatable, createdTick is now and the entry is live, detached and unowned.
            _ = _worldItems.Create(flight.Entry, flight.CatcherPosMm, WorldItemOrigin.Thrown, flight.OnWater, _tick.CurrentTick);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The tick guard; a live container without an outfeed (<c>outfeed_only</c> admits no insertion), a real mail id and a known mail
    /// kind; then <see cref="Placement.AutoPlace.Plan"/> of 1 unit with no source, <see cref="MergePolicy.Merge"/>, pass-first, into
    /// <paramref name="c"/> only (the whitelist, PASS M, then a free cell), and the arena reservation (the merge's union block, or the new
    /// entry's one-unit block). A merge commits through <see cref="InventoryMutator.MergeUnitInto"/> with no entry created; a free-cell
    /// placement allocates the entry and its block, then commits through <see cref="Placement.AutoPlace.Commit(ref InvPlan, in AutoPlacePlan, ushort)"/>,
    /// as <see cref="Spawn"/> does. One call scope, one bracket with <paramref name="actor"/> and cause <see cref="InvCause.None"/>. A
    /// refused plan records its no-fit facts and nothing else; no entry or arena block is taken before the plan and reservation pass.
    /// <see cref="UnitRef.Entry"/> is ignored (the unit's old entry, e.g. a belt's <see cref="TakeUnit"/>). Duplicate-id detection is
    /// the ledger's (story 017).
    /// </remarks>
    public bool TryInsert(ContainerId c, in UnitRef u, ActorId actor)
    {
        if (!TickGuard.IsMutable(_tick, _events.Counters) || !_containers.TryGetSlot(c, out ushort slot) || _mutator.HasOutfeed(slot)
            || u.Mail.IsNone || u.MailKind.IsNone || u.MailKind.Value >= _content.MailKinds.Count)
        {
            return false;
        }

        PlacementSubject subject = PlacementSubject.ForMail(_content.MailKinds[u.MailKind], u.MailKind, u.AddressId, u.Flags, _stats);
        var plan = new InvPlan();
        ushort reason = _autoPlace.Plan(
            ref plan, in subject, 1, Placement.AutoPlace.NoSource, new ReadOnlySpan<ushort>(in slot), MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
        bool merges = ap.MergeCount > 0;
        if (reason == 0 && !merges)
        {
            _ = plan.AddAcquiredBlock(1); // the new entry's one-unit block
        }

        if (reason == 0)
        {
            reason = _requestCheck.CheckArenaReservation(in plan);
        }

        if (reason != 0)
        {
            _autoPlace.RecordNoFits(in ap);
            return false;
        }

        ushort entrySlot = 0;
        if (!merges)
        {
            uint tick = _tick.CurrentTick;
            if (!_entries.TryAllocate(tick, out entrySlot, out _))
            {
                return false; // the pool wrote its check record; nothing mutated
            }

            ref EntryRecord e = ref _entries[entrySlot];
            uint id = u.Mail.Value;
            if (!_mailIds.TryCreate(new ReadOnlySpan<uint>(in id), tick, out e.MailIds))
            {
                _entries.Free(entrySlot); // unreachable after the reservation; the arena wrote its record
                return false;
            }

            e.IsMail = true;
            e.MailKind = u.MailKind.Value;
            e.AddressId = u.AddressId;
            e.Flags = u.Flags;
            e.W = subject.W;
            e.H = subject.H;
        }

        var extra = new InvReplyExtra();
        bool inserted = true;
        using (EventCallScope scope = _events.BeginCall())
        {
            _mutator.Begin(ref plan, actor, InvCause.None);
            if (merges)
            {
                _autoPlace.RecordNoFits(in ap);
                inserted = _mutator.MergeUnitInto(ref plan, ap.Merge(0).TargetEntrySlot, subject.Key, u.Mail);
            }
            else
            {
                _autoPlace.Commit(ref plan, in ap, entrySlot);
            }

            _mutator.End(ref plan, ref extra);
        }

        // story 017 ledger hook: on true only, the unit crosses into PLACED (from Belt, or wherever the caller took it from).
        return inserted;
    }

    /// <inheritdoc />
    /// <remarks>Sums <see cref="ItemCountIndex.Units"/> over the live containers of <paramref name="cs"/>.</remarks>
    public int CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId)
    {
        if (itemId.IsNone)
        {
            return 0;
        }

        int units = 0;
        for (int i = 0; i < cs.Length; i++)
        {
            if (_containers.TryGetSlot(cs[i], out ushort slot))
            {
                units += _mutator.ItemCounts.Units(slot, itemId.Value);
            }
        }

        return units;
    }

    /// <inheritdoc />
    /// <remarks>Delegates to <see cref="World.WorldItems.Create"/> (story 013).</remarks>
    public EntityId CreateWorldItem(EntryId entry, Vec3Mm posMm, WorldItemOrigin origin, bool onWater, uint createdTick)
        => _worldItems.Create(entry, posMm, origin, onWater, createdTick);

    /// <summary>
    /// Returns a detached entry to the Intake whole (story 012, Planner R4; callers story 013's expiry and water returns, bags later,
    /// story 021's backlog branch): AutoPlace of its whole count with the entry as source, NoMerge, pass-first, into the Intake only, then
    /// the commit in one call scope and one bracket with <paramref name="actor"/> and <paramref name="cause"/>. The entry keeps its
    /// entryId and gets a new <c>arrivalSeq</c> (the mutator's <c>Place</c>). False with nothing changed when the entry is not a live
    /// detached entry, is held by a WorldItem (story 013 returns those itself, through the same two steps), the Intake refuses it
    /// (whitelist) or has no room (the plan's no-fit facts are recorded), the Intake is not accepting (<see cref="IntakeAccepting"/>), or
    /// the call is outside a tick phase.
    /// </summary>
    /// <param name="detachedEntrySlot">The entry's pool slot (allocated, in no container).</param>
    /// <param name="actor">The actor written into the seal.</param>
    /// <param name="cause">The cause written into the seal.</param>
    /// <returns>True when the entry was placed in the Intake.</returns>
    internal bool ReturnToIntake(ushort detachedEntrySlot, ActorId actor, InvCause cause)
    {
        if (!TickGuard.IsMutable(_tick, _events.Counters) || _worldItems.OwnsEntry(detachedEntrySlot))
        {
            return false;
        }

        var plan = new InvPlan();
        if (!TryPlanReturnToIntake(detachedEntrySlot, ref plan, out AutoPlacePlan ap))
        {
            return false;
        }

        using (EventCallScope scope = _events.BeginCall())
        {
            CommitReturnToIntake(ref plan, in ap, actor, cause);
        }

        return true;
    }

    /// <summary>
    /// The planning half of <see cref="ReturnToIntake"/> (pure but for the no-fit facts a refused plan records): false when the Intake is
    /// not accepting, the entry is not a live detached entry, or the AutoPlace or arena reservation refuses it. The caller has checked the
    /// tick guard.
    /// </summary>
    /// <param name="detachedEntrySlot">The entry's pool slot.</param>
    /// <param name="plan">The call's plan (filled).</param>
    /// <param name="ap">The placement to commit.</param>
    /// <returns>True when the commit can run.</returns>
    internal bool TryPlanReturnToIntake(ushort detachedEntrySlot, ref InvPlan plan, out AutoPlacePlan ap)
    {
        ap = default;
        if (!IntakeAccepting || detachedEntrySlot >= _entries.Capacity || !_entries[detachedEntrySlot].IsLive
            || _entries[detachedEntrySlot].ContainerSlot != EntryRecord.NoContainer)
        {
            return false;
        }

        ref EntryRecord e = ref _entries[detachedEntrySlot];
        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        ushort intake = _intakeSlot;
        ushort reason = _autoPlace.Plan(
            ref plan, in subject, e.Count, detachedEntrySlot, new ReadOnlySpan<ushort>(in intake), MergePolicy.NoMerge, PlaceOrder.PassFirst, out ap);
        if (reason == 0)
        {
            reason = _requestCheck.CheckArenaReservation(in plan);
        }

        if (reason != 0)
        {
            _autoPlace.RecordNoFits(in ap);
            return false;
        }

        return true;
    }

    /// <summary>The commit half of <see cref="ReturnToIntake"/>: one <c>Begin</c>/<c>End</c> bracket in the caller's call scope.</summary>
    /// <param name="plan">The plan <see cref="TryPlanReturnToIntake"/> filled.</param>
    /// <param name="ap">Its placement.</param>
    /// <param name="actor">The actor written into the seal.</param>
    /// <param name="cause">The cause written into the seal.</param>
    internal void CommitReturnToIntake(ref InvPlan plan, in AutoPlacePlan ap, ActorId actor, InvCause cause)
    {
        var extra = new InvReplyExtra();
        _mutator.Begin(ref plan, actor, cause);
        _autoPlace.Commit(ref plan, in ap);
        _mutator.End(ref plan, ref extra);
    }

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick)
    {
        ref PanelIds panel = ref _panels[slot.Value];
        panel.Player = player;
        if (!panel.PlayerInventory.IsNone)
        {
            return; // rejoin of a slot that kept its containers across a leave
        }

        if (_containers.Capacity - _containers.LiveCount < PanelContainerCount)
        {
            _checkRing.Record(CheckCode.InventoryContainerCapacity, tick, slot.Value, _containers.Capacity);
            return;
        }

        // The capacity pre-check above guarantees all three fit, so a panel is never created partially.
        _ = TryCreate(_hotbarDef, slot.Value, slot, out ContainerId hotbar);
        _ = TryCreate(_playerInventoryDef, slot.Value, slot, out ContainerId inventory);
        _ = TryCreate(_cursorDef, slot.Value, slot, out ContainerId cursor);
        panel.Hotbar = hotbar;
        panel.PlayerInventory = inventory;
        panel.Cursor = cursor;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Clears the slot's external and its subscriber bit (GDD l.194; story 011), so the slot's next player starts with no external and
    /// receives none of its records. No record, no version; allocation-free. Containers stay, and the cursor entry stays on the cursor
    /// (disconnect handling is M0).
    /// </remarks>
    void IPlayerJoinListener.OnPlayerLeft(PlayerSlot slot, EntityId player) => ClearExternal(slot);

    /// <summary>
    /// The <c>Close</c> op (story 011; ADR-0009 section 2): <see cref="InvRequestCheck.CheckRequest"/> first (<c>bad_request</c> for
    /// container 0 or at/above the high-water), then <c>container_gone</c> for an issued id that is no longer live (no container is
    /// destroyed before M-1b, whose tombstones add <c>byActorId</c>), then <c>not_open</c> when the container is not in the actor's open
    /// set (the System actor has none). A pass closes the actor's external through <see cref="CloseExternal"/> with
    /// <see cref="CloseCause.Close"/>; closing the actor's own panel or cursor (always in their set, never closable) is <c>Ok</c> with
    /// nothing changed. No range gate, no record, no version on any path. Allocation-free.
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <returns>The reply.</returns>
    internal Reply ApplyClose(in Op op)
    {
        PlayerSlot actor = ActorSlot(op.Actor);
        ushort reason = _requestCheck.CheckRequest(in op, actor);
        if (reason != 0)
        {
            return Reply.Reject(in op, reason);
        }

        var c = new ContainerId(op.PayloadAs<ClosePayload>().Container);
        if (!_containers.TryGetSlot(c, out _))
        {
            return Reply.Reject(in op, (ushort)InvReason.ContainerGone);
        }

        if (!_openSet.IsInOpenSet(actor, c))
        {
            return Reply.Reject(in op, (ushort)InvReason.NotOpen);
        }

        if (_openSet.ExternalOf(actor) == c)
        {
            CloseExternal(actor, CloseCause.Close);
        }

        return Reply.Ok(in op);
    }

    /// <summary>
    /// The slot of an op's actor (from <see cref="Op.Actor"/> through the injected <see cref="ISlotView"/>, never the payload): none for
    /// the System actor, an actor in no slot, or a module built without a slot view. A none slot has an empty open set.
    /// </summary>
    /// <param name="actor">The op's actor.</param>
    /// <returns>The actor's slot, or none.</returns>
    internal PlayerSlot ActorSlot(ActorId actor) =>
        actor.IsSystem || _slots is null ? PlayerSlot.None : _slots.SlotOf(new EntityId(actor.Value));

    /// <summary>Clears <paramref name="slot"/>'s external and its bit in that container's subscriber mask (a dead external has no mask to clear).</summary>
    private void ClearExternal(PlayerSlot slot)
    {
        ContainerId current = _openSet.ExternalOf(slot);
        if (current.IsNone)
        {
            return;
        }

        if (_containers.TryGetSlot(current, out ushort containerSlot))
        {
            _subscribers.Remove(containerSlot, slot);
        }

        _openSet.SetExternal(slot, default);
    }

    /// <summary>The <see cref="Spawn"/> handler-limit checks: a live container, a known mail kind, 1 to 255 strictly ascending real ids.</summary>
    private bool IsWellFormedSpawn(ContainerId c, DefIndex mailKind, ReadOnlySpan<MailId> ids, out ushort slot)
    {
        if (!_containers.TryGetSlot(c, out slot) || mailKind.IsNone || mailKind.Value >= _content.MailKinds.Count
            || ids.Length < 1 || ids.Length > byte.MaxValue || ids[0].IsNone)
        {
            return false;
        }

        for (int i = 1; i < ids.Length; i++)
        {
            if (ids[i].Value <= ids[i - 1].Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The live head of <paramref name="c"/>'s outfeed: false for a dead container, one without an outfeed, an unknown selector or an
    /// empty outfeed. A non-mail head is unreachable (the whitelist admits mail only) and asserts
    /// <see cref="SimAssertCode.InventoryOutfeedHeadNotMail"/> under <c>INVENTORY_ASSERT</c>; Release returns false with nothing
    /// popped or mutated, so the outfeed reads as empty until that entry leaves the Intake by another path.
    /// </summary>
    private bool TryOutfeedHead(ContainerId c, Selector s, out ushort slot, out ushort entrySlot)
    {
        entrySlot = 0;
        if (s != Selector.Fifo || !_containers.TryGetSlot(c, out slot) || !_mutator.HasOutfeed(slot))
        {
            slot = 0;
            return false;
        }

        if (!_mutator.Outfeed.TryPeekHead(slot, ref _containers[slot], out entrySlot))
        {
            return false;
        }

        bool isMail = _entries[entrySlot].IsMail;
        InventoryAssert.That(isMail, SimAssertCode.InventoryOutfeedHeadNotMail);
        return isMail;
    }

    /// <summary>The oldest unit of a live mail entry, with its StackKey.</summary>
    private UnitRef UnitOf(ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        return new UnitRef(new MailId(_mailIds.Read(in e.MailIds)[0]), e.Id, DefIndex.FromValue(e.MailKind), e.AddressId, e.Flags);
    }

    private static int WidestGrid(DefTable<ContainerDef> defs)
    {
        ReadOnlySpan<ContainerDef> span = defs.AsSpan();
        int widest = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].HasGrid && span[i].GridW > widest)
            {
                widest = span[i].GridW;
            }
        }

        return widest;
    }

    private DefIndex Resolve(string id)
    {
        ReadOnlySpan<ContainerDef> defs = _defs.AsSpan();
        for (int i = 0; i < defs.Length; i++)
        {
            if (defs[i].Id == id)
            {
                return DefIndex.FromValue((ushort)i);
            }
        }

        throw new SimBuildException(SimBuildErrorCode.InventoryDefMissing, $"The content set has no container def '{id}'.");
    }

    private bool TryCreate(DefIndex def, byte ownerSlot, PlayerSlot? statSlot, out ContainerId id)
    {
        ref readonly ContainerDef d = ref _defs[def];
        int rows = _stats.Read(Stat.InventoryRows, def.Value, statSlot);
        if (!_containers.TryCreate(def, d.GridW, rows, d.Layout, ownerSlot, out id))
        {
            return false;
        }

        // The width was bounded at construction and the rows by the stat sheet, so Reset cannot throw here.
        _ = _containers.TryGetSlot(id, out ushort slot);
        _occupancy.Reset(slot, d.GridW, rows);
        _mutator.Outfeed.ResetSlot(slot);
        return true;
    }

    /// <summary>The production <c>Close</c> handler (story 011): <see cref="ApplyClose"/>.</summary>
    private sealed class CloseHandler : IOpHandler
    {
        private readonly InventorySystem _owner;

        public CloseHandler(InventorySystem owner)
        {
            _owner = owner;
        }

        public OpKind Kind => InvOpKinds.Close;

        public Reply Apply(in Op op, in TickContext ctx) => _owner.ApplyClose(in op);
    }

    /// <summary>The tick reader of a module built without one (tests): always between ticks at tick 0.</summary>
    private sealed class BetweenTicksReader : ITickReader
    {
        public uint CurrentTick => 0;

        public TickStage Stage => TickStage.BetweenTicks;

        public PhaseId Phase => PhaseId.InputsApply;
    }
}
