using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// Grid Inventory's Sim module (GDD <c>grid-inventory.md</c> R1; ADR-0009 sections 1 and 5; ADR-0003; story grid-inventory/001).
/// It owns the container registry (<see cref="ContainerTable"/>): one <c>po_intake</c> world container created at construction, and for
/// each joining player slot a <c>hotbar</c>, <c>player_inventory</c> and <c>cursor</c> created when Sim Core's <c>PlayerJoin</c> handler
/// calls <see cref="IPlayerJoinListener"/>. A leave keeps the containers (disconnect handling is M0) and a rejoin of the same slot reuses
/// them. Container dimensions are instance state: the def's width and <c>IStatSheet.Read(InventoryRows)</c> rows.
/// <para>
/// It binds the empty <c>ItemsExpire</c> and <c>IntakeDrain</c> phases, a no-op conservation check (story 017's), the state
/// contributor, the join listener and the inventory event kinds (<see cref="InvEventDescriptors"/>, story 005). It binds no op yet
/// (handlers are stories 007-010) and no snapshot source (the builder has no <c>BindSnapshotSource</c> yet). Every container, entry and
/// occupancy write goes through the <see cref="Mutator"/> (story 005); an external container's audience is its
/// <see cref="Subscribers"/> set. Storage is preallocated from
/// <see cref="InventorySettings"/>; the join and leave paths allocate nothing and never throw. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Composition: new InventorySystem(context.Content, context.StatSheet, context.CheckRing, context.Events)
/// ContainerId hotbar = inventory.HotbarOf(PlayerSlot.FromValue(0));   // none until the slot's PlayerJoin
/// </code>
/// </example>
[SimSystem(SystemId.GridInventory)]
public sealed class InventorySystem : ISimSystem, IPlayerJoinListener
{
    private struct PanelIds
    {
        public ContainerId Hotbar;
        public ContainerId PlayerInventory;
        public ContainerId Cursor;
    }

    private const int PanelContainerCount = 3;

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
    private readonly DefIndex _hotbarDef;
    private readonly DefIndex _playerInventoryDef;
    private readonly DefIndex _cursorDef;
    private readonly PanelIds[] _panels = new PanelIds[PlayerSlot.Count];
    private readonly ContainerId _intake;

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

    /// <summary>Creates the module with the world's event-log writer (the composition root's constructor).</summary>
    /// <param name="content">The content set (container defs and the inventory balance).</param>
    /// <param name="stats">The stat sheets (container rows).</param>
    /// <param name="checkRing">The world's check ring.</param>
    /// <param name="events">The world's event-log writer facade (records and seals are raised through it).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException">As the three-argument constructor.</exception>
    public InventorySystem(ContentSet content, IStatSheet stats, SimCheckRing checkRing, EventLogFacade events)
        : this(content, stats, checkRing, InventorySettings.FromBalance((content ?? throw new ArgumentNullException(nameof(content))).Inventory), events ?? throw new ArgumentNullException(nameof(events)))
    {
    }

    /// <summary>Creates the module with ready settings (tests).</summary>
    /// <param name="content">The content set.</param>
    /// <param name="stats">The stat sheets.</param>
    /// <param name="checkRing">The check ring.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="events">The event-log writer; null gives an unattached facade of its own (a module that never mutates, e.g. catalogue tests: a raise through it is a misuse).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>InventorySettingsInvalid</c> when the settings cannot hold the intake container; <c>InventoryDefMissing</c> when an M-1a def is absent; <c>InventoryDefInvalid</c> when any container def is wider than 64 columns.</exception>
    internal InventorySystem(ContentSet content, IStatSheet stats, SimCheckRing checkRing, InventorySettings settings, EventLogFacade? events = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(checkRing);
        ArgumentNullException.ThrowIfNull(settings);
        _defs = content.Containers;
        _stats = stats;
        _checkRing = checkRing;
        _settings = settings;
        _containers = new ContainerTable(settings.ContainerCapacity);
        _entries = new EntryPool(settings.EntryPoolCapacity, checkRing);
        _mailIds = new MailIdArena(settings.MailIdArenaUnits, checkRing);
        _requestCheck = new InvRequestCheck(_containers, _entries, _mailIds);
        _subscribers = new ContainerSubscribers(settings.ContainerCapacity);

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
        var noFit = new NegativeFitCache(settings.ContainerCapacity, NegativeFitCache.WaysFor(content));
        _mutator = new InventoryMutator(_containers, _entries, _occupancy, _mailIds, _subscribers, facade, content, stats, new StackIndex(_entries), noFit);
        _autoPlace = new AutoPlace(_containers, _defs, _entries, _occupancy, _mutator, noFit, facade.Counters);

        DefIndex intakeDef = Resolve("po_intake");
        _hotbarDef = Resolve("hotbar");
        _playerInventoryDef = Resolve("player_inventory");
        _cursorDef = Resolve("cursor");

        if (!TryCreate(intakeDef, ContainerHeader.NoOwner, null, out _intake))
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, "The container table cannot hold the po_intake container.");
        }
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

    /// <summary>The per-container subscriber sets (the open set maintains them from story 011; tests set them directly until then).</summary>
    internal ContainerSubscribers Subscribers => _subscribers;

    /// <summary>The hotbar of <paramref name="slot"/>, or none when the slot has no containers.</summary>
    /// <param name="slot">The player slot.</param>
    public ContainerId HotbarOf(PlayerSlot slot) => slot.IsNone ? default : _panels[slot.Value].Hotbar;

    /// <summary>The player inventory of <paramref name="slot"/>, or none when the slot has no containers.</summary>
    /// <param name="slot">The player slot.</param>
    public ContainerId PlayerInventoryOf(PlayerSlot slot) => slot.IsNone ? default : _panels[slot.Value].PlayerInventory;

    /// <summary>The cursor container of <paramref name="slot"/>, or none when the slot has no containers.</summary>
    /// <param name="slot">The player slot.</param>
    public ContainerId CursorOf(PlayerSlot slot) => slot.IsNone ? default : _panels[slot.Value].Cursor;

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
    /// Binds the empty <c>ItemsExpire</c> and <c>IntakeDrain</c> phases, the stub conservation check, the state contributor, the
    /// join listener and every <see cref="InvEventDescriptors"/> kind. Binds no op (stories 007-010) or snapshot source.
    /// </summary>
    /// <param name="builder">The world's builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="SimBuildException"><c>PlayerJoinListenerAlreadyBound</c>, <c>PlayerJoinListenerLimit</c>, <c>DuplicateSection</c> or <c>DuplicateEventKind</c>.</exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(PhaseId.ItemsExpire, new ItemsExpirePhase());
        builder.Bind(PhaseId.IntakeDrain, new IntakeDrainPhase());
        builder.BindTickEnd(new InventoryConservationCheck());
        builder.BindStateContributor(new InventoryStateContributor(_containers));
        builder.BindPlayerJoinListener(this);
        foreach (EventDescriptor descriptor in InvEventDescriptors.All)
        {
            builder.BindEventKind(descriptor);
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
        return true;
    }

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick)
    {
        ref PanelIds panel = ref _panels[slot.Value];
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
    void IPlayerJoinListener.OnPlayerLeft(PlayerSlot slot, EntityId player)
    {
        // Containers stay (disconnect handling is M0).
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
        return true;
    }
}
