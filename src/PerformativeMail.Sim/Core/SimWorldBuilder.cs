namespace PerformativeMail.Sim.Core;

/// <summary>
/// Collects phase binds and tick-end checks while the world is <see cref="SimState.Constructed"/>, then
/// <see cref="Freeze"/>s them into a <see cref="SimWorld"/> (ADR-0003 section 3). Bind order stands in for
/// composition order (Story 025): binders of one phase run in the order they were bound. Not thread-safe;
/// use on the thread that will call <see cref="SimWorld.Tick"/>. Errors are <see cref="SimBuildException"/>s.
/// </summary>
/// <example>
/// <code>
/// var builder = new SimWorldBuilder();
/// builder.Bind(PhaseId.MailStep, mailPhase);
/// builder.BindTickEnd(invariantCheck);
/// SimWorld world = builder.Freeze();   // Ready
/// world.Tick();                        // processes tick 0
/// </code>
/// </example>
public sealed class SimWorldBuilder
{
    private const int PhaseCount = 14;

    private readonly List<ISimPhase>[] _phases = new List<ISimPhase>[PhaseCount];
    private readonly List<ITickEndCheck> _tickEnd = new();
    private readonly SimCoreSettings _settings;
    private readonly SimCounters _counters;
    private readonly SimCheckRing _checkRing;
    private readonly IPhaseProbe _probe;
    private readonly SimInitialCounters _initialCounters;
    private readonly List<EventDescriptor> _eventKinds = new();
    private readonly List<ReaderId> _readerIds = new();
    private readonly List<ushort[]> _readerFilters = new();
    private readonly List<OpDescriptor> _opDescriptors = new();
    private readonly List<IOpHandler?> _opHandlers = new();
    private readonly List<ISimStateContributor> _stateContributors = new();
    private IEventSink? _eventSink;
    private IOpRecorder? _recorder;
    private SimCoreStateSources? _coreState;
    private readonly IPlayerJoinListener[] _joinListeners = new IPlayerJoinListener[MaxJoinListeners];
    private int _joinListenerCount;
    private bool _frozen;

    /// <summary>Creates a builder. Every argument defaults to a fresh per-world instance.</summary>
    /// <param name="settings">Sim Core settings (default <see cref="SimCoreSettings"/> defaults).</param>
    /// <param name="counters">The world's counters (default a new <see cref="SimCounters"/>).</param>
    /// <param name="checkRing">The world's check ring (default sized by <see cref="SimCoreSettings.CheckRingCapacity"/>).</param>
    /// <param name="probe">Timing probe (default a <see cref="NullPhaseProbe"/>).</param>
    /// <param name="initialCounters">Starting values for monotonic counters such as <c>opSeq</c> (default all zero).</param>
    public SimWorldBuilder(
        SimCoreSettings? settings = null,
        SimCounters? counters = null,
        SimCheckRing? checkRing = null,
        IPhaseProbe? probe = null,
        SimInitialCounters? initialCounters = null)
    {
        _settings = settings ?? new SimCoreSettings();
        _counters = counters ?? new SimCounters();
        _checkRing = checkRing ?? new SimCheckRing(_settings.CheckRingCapacity);
        _probe = probe ?? new NullPhaseProbe();
        _initialCounters = initialCounters ?? new SimInitialCounters();
        for (int i = 0; i < PhaseCount; i++)
        {
            _phases[i] = new List<ISimPhase>();
        }

        // Sim Core declares its own event kinds; systems bind theirs with BindEventKind.
        _eventKinds.Add(CoreEventDescriptors.OpReply);
    }

    /// <summary>Binds <paramref name="phase"/> to run in <paramref name="p"/>, after earlier binders of the same phase.</summary>
    /// <param name="phase">A phase from <see cref="PhaseId.ItemsExpire"/> to <see cref="PhaseId.RunStep"/>.</param>
    /// <param name="p">The work to run.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>ReservedPhase</c> for phase 0 or 13.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="p"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> is not a defined phase.</exception>
    /// <example><code>builder.Bind(PhaseId.PlayersStep, playersPhase);</code></example>
    public void Bind(PhaseId phase, ISimPhase p)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(p);
        if ((byte)phase >= PhaseCount)
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        if (phase == PhaseId.InputsApply || phase == PhaseId.EventsFlush)
        {
            throw new SimBuildException(SimBuildErrorCode.ReservedPhase, "Phases 0 (InputsApply) and 13 (EventsFlush) are built in and cannot be bound.");
        }

        _phases[(int)phase].Add(p);
    }

    /// <summary>Binds a tick-end check, run after phase 12 and before phase 13, in bind order.</summary>
    /// <param name="c">The check.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="c"/> is null.</exception>
    /// <example><code>builder.BindTickEnd(new NoNegativeBalance());</code></example>
    public void BindTickEnd(ITickEndCheck c)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(c);
        _tickEnd.Add(c);
    }

    /// <summary>
    /// Declares an <see cref="EventKind"/> (class, payload length, resync scope, wire fields). Every kind a system raises
    /// must be declared; an undeclared kind is stored as <c>Internal</c> and fails the descriptor enumeration test. The
    /// builder binds Sim Core's own kinds (<see cref="CoreEventDescriptors.OpReply"/>) itself, so a system must not.
    /// </summary>
    /// <param name="descriptor">The declaration.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>InvalidKind</c> for kind <c>None</c>; <c>PayloadTooLarge</c> for a payload over 48 bytes; <c>InvalidEventDescriptor</c> for an undefined class or a resync scope over 63; <c>DuplicateEventKind</c> when the kind is already declared.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is null.</exception>
    /// <example><code>builder.BindEventKind(EventDescriptor.For&lt;MovedPayload&gt;(movedKind, ReplicationClass.Replicated, false, 3, 7, 12));</code></example>
    public void BindEventKind(EventDescriptor descriptor)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateEventDescriptor(descriptor);

        for (int i = 0; i < _eventKinds.Count; i++)
        {
            if (_eventKinds[i].Kind == descriptor.Kind)
            {
                throw new SimBuildException(SimBuildErrorCode.DuplicateEventKind, $"Event kind 0x{(ushort)descriptor.Kind:X4} is already declared.");
            }
        }

        _eventKinds.Add(descriptor);
    }

    /// <summary>
    /// Declares an <see cref="OpKind"/> without a handler: the owner promises to attach one with <see cref="BindOp"/> before
    /// <see cref="Freeze"/>, which throws <c>MissingOpHandler</c> otherwise. Declaring the same descriptor again is a no-op.
    /// </summary>
    /// <param name="descriptor">The kind's registration record.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>InvalidKind</c> for kind <c>None</c>; <c>PayloadTooLarge</c> for a payload over 48 bytes; <c>SystemReasonBelow64</c> for a reason code below 64; <c>DuplicateOpHandler</c> for a different descriptor of an already declared kind.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is null.</exception>
    /// <example><code>builder.DeclareOp(OpDescriptor.Empty(OpKind.PlayerLeave, false, 2, 0));</code></example>
    public void DeclareOp(OpDescriptor descriptor)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateOpDescriptor(descriptor);
        DeclareChecked(descriptor);
    }

    /// <summary>
    /// Declares <paramref name="descriptor"/> and binds <paramref name="handler"/> to its kind (ADR-0007). A second handler for one
    /// kind throws <c>DuplicateOpHandler</c> at this call (the faulty call site is on the stack). A null handler declares the kind
    /// with no handler, so <see cref="Freeze"/> throws <c>MissingOpHandler</c> unless a later <see cref="BindOp"/> supplies one.
    /// </summary>
    /// <param name="descriptor">The kind's registration record.</param>
    /// <param name="handler">The handler for <c>descriptor.Kind</c>.</param>
    /// <exception cref="SimBuildException">As <see cref="DeclareOp"/>, plus <c>DuplicateOpHandler</c> when the kind already has a handler and <c>HandlerKindMismatch</c> when <c>handler.Kind != descriptor.Kind</c> (checked before anything is declared).</exception>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is null.</exception>
    /// <example><code>builder.BindOp(OpDescriptor.Empty(OpKind.PlayerLeave, false, 2, 0), new LeaveHandler());</code></example>
    public void BindOp(OpDescriptor descriptor, IOpHandler handler)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateOpDescriptor(descriptor);
        if (handler is not null && handler.Kind != descriptor.Kind)
        {
            throw new SimBuildException(
                SimBuildErrorCode.HandlerKindMismatch,
                $"Handler reports kind 0x{(ushort)handler.Kind:X4} but the descriptor declares 0x{(ushort)descriptor.Kind:X4}.");
        }

        int index = DeclareChecked(descriptor);
        if (handler is null)
        {
            return;
        }

        if (_opHandlers[index] is not null)
        {
            throw new SimBuildException(SimBuildErrorCode.DuplicateOpHandler, $"Op kind 0x{(ushort)descriptor.Kind:X4} already has a handler.");
        }

        _opHandlers[index] = handler;
    }

    /// <summary>
    /// Binds the optional <see cref="IOpRecorder"/> that sees every drained op (and, in later stories, enqueues, samples and
    /// overflow). Without one the world uses a null-object recorder. A later call replaces the earlier recorder.
    /// </summary>
    /// <param name="recorder">The recorder.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recorder"/> is null.</exception>
    /// <example><code>builder.BindRecorder(new MyRecorder());</code></example>
    public void BindRecorder(IOpRecorder recorder)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <summary>
    /// Binds the host's <see cref="IEventSink"/>, which receives each tick's log once in phase 13. Optional: a world with no
    /// sink still ticks and still resets its log.
    /// </summary>
    /// <param name="sink">The sink.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>EventSinkAlreadyBound</c> when one is already bound.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
    /// <example><code>builder.BindEventSink(hostAdapter);</code></example>
    public void BindEventSink(IEventSink sink)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(sink);
        if (_eventSink is not null)
        {
            throw new SimBuildException(SimBuildErrorCode.EventSinkAlreadyBound, "An IEventSink is already bound; a world has at most one.");
        }

        _eventSink = sink;
    }

    /// <summary>
    /// Declares a pull reader (ADR-0008 section 4): a later-phase consumer that reads the kinds in
    /// <paramref name="filter"/> with <see cref="EventLog.Read"/> instead of subscribing. The filter is copied and sorted
    /// ascending into a <c>ushort[]</c> (a stable insertion sort; <c>Array.Sort</c> is banned) so reads can binary-search it.
    /// At <see cref="Freeze"/> every kind in a filter is flagged <c>HasReader</c> (<see cref="EventLog.HasReader"/>); the
    /// reserve tier of overflow admission keeps those kinds (a later story).
    /// </summary>
    /// <param name="id">The reader's id; must not be <see cref="ReaderId.None"/> or already bound.</param>
    /// <param name="filter">The kinds to read; not null, not empty, no <see cref="EventKind.None"/>. The array is not kept.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>ReaderLimit</c> when <see cref="SimCoreSettings.MaxEventReaders"/> readers are already bound; <c>InvalidEventReader</c> for a bad id or filter.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null.</exception>
    /// <example><code>builder.BindEventReader(new ReaderId(1), new[] { EventKind.OpReply });</code></example>
    public void BindEventReader(ReaderId id, EventKind[] filter)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(filter);
        ValidateReader(id, filter);
        _readerIds.Add(id);
        _readerFilters.Add(SortReaderFilter(filter));
    }

    /// <summary>
    /// Binds a system's canonical dump section (ADR-0006 section 4). Sections are written after Sim Core's section 1 in bind
    /// order, so bind order is the composition order and must not depend on a dictionary or hash.
    /// </summary>
    /// <param name="contributor">The contributor; its <see cref="ISimStateContributor.SectionId"/> is read now and must stay constant.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>InvalidSection</c> for id 0; <c>DuplicateSection</c> when the id is already bound or is <see cref="SectionIds.SimCore"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="contributor"/> is null.</exception>
    /// <example><code>builder.BindStateContributor(new InventoryDump(containers));</code></example>
    public void BindStateContributor(ISimStateContributor contributor)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(contributor);
        ushort id = contributor.SectionId;
        if (id == 0)
        {
            throw new SimBuildException(SimBuildErrorCode.InvalidSection, "A state contributor's SectionId must not be 0.");
        }

        if (id == SectionIds.SimCore)
        {
            throw new SimBuildException(SimBuildErrorCode.DuplicateSection, $"Section id {id} is reserved for Sim Core's own section.");
        }

        for (int i = 0; i < _stateContributors.Count; i++)
        {
            if (_stateContributors[i].SectionId == id)
            {
                throw new SimBuildException(SimBuildErrorCode.DuplicateSection, $"Dump section id {id} is already bound.");
            }
        }

        _stateContributors.Add(contributor);
    }

    /// <summary>
    /// Hands the world the Sim Core objects whose state section 1 of the canonical dump encodes: the id allocators, the RNG
    /// registry and the slot table. The composition root builds them against a forwarding tick reader (they need the world
    /// that does not exist yet) and passes them here. Any argument may be null (dumped as a fresh object); a later call
    /// replaces the earlier sources wholesale.
    /// </summary>
    /// <param name="ids">The entity id allocator.</param>
    /// <param name="mail">The mail id allocator.</param>
    /// <param name="rng">The RNG registry.</param>
    /// <param name="slots">The player slot table.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>.</exception>
    /// <example><code>builder.BindCoreState(ids, mailIds, rngRegistry, slotTable);</code></example>
    public void BindCoreState(EntityIds? ids, MailIds? mail, RngRegistry? rng, SlotTable? slots)
    {
        ThrowIfFrozen();
        _coreState = new SimCoreStateSources(ids, mail, rng, slots);
    }

    /// <summary>The most <see cref="IPlayerJoinListener"/>s a world fans a join or leave out to.</summary>
    internal const int MaxJoinListeners = 4;

    /// <summary>
    /// Binds an <see cref="IPlayerJoinListener"/> (Player Character creates and removes its carrier from one, Grid Inventory its
    /// panel containers from another). Internal to the Sim assembly. At most <see cref="MaxJoinListeners"/> are bound; the join and
    /// leave handlers call them in bind order (composition order). Without one the handlers use a null-object listener. Binding the
    /// same instance twice throws.
    /// </summary>
    /// <param name="listener">The listener, called inside the handler's call scope so its events precede the OpReply.</param>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> after <see cref="Freeze"/>; <c>PlayerJoinListenerAlreadyBound</c> when this instance is already bound; <c>PlayerJoinListenerLimit</c> past 4 listeners.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="listener"/> is null.</exception>
    /// <example><code>builder.BindPlayerJoinListener(players);</code></example>
    internal void BindPlayerJoinListener(IPlayerJoinListener listener)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(listener);
        for (int i = 0; i < _joinListenerCount; i++)
        {
            if (ReferenceEquals(_joinListeners[i], listener))
            {
                throw new SimBuildException(SimBuildErrorCode.PlayerJoinListenerAlreadyBound, "This IPlayerJoinListener is already bound.");
            }
        }

        if (_joinListenerCount == MaxJoinListeners)
        {
            throw new SimBuildException(SimBuildErrorCode.PlayerJoinListenerLimit, $"At most {MaxJoinListeners} IPlayerJoinListeners can be bound.");
        }

        _joinListeners[_joinListenerCount++] = listener;
    }

    /// <summary>Checks the reader limit, the id (non-zero, not already bound) and that the filter is non-empty.</summary>
    private void ValidateReader(ReaderId id, EventKind[] filter)
    {
        if (_readerIds.Count >= _settings.MaxEventReaders)
        {
            throw new SimBuildException(SimBuildErrorCode.ReaderLimit, $"At most {_settings.MaxEventReaders} event readers can be bound.");
        }

        if (id.Value == 0 || filter.Length == 0)
        {
            throw new SimBuildException(SimBuildErrorCode.InvalidEventReader, "An event reader needs a non-zero ReaderId and a non-empty filter.");
        }

        if (_readerIds.Contains(id))
        {
            throw new SimBuildException(SimBuildErrorCode.InvalidEventReader, $"Event reader {id.Value} is already bound.");
        }
    }

    /// <summary>Copies <paramref name="filter"/> into an ascending <c>ushort[]</c>, rejecting <see cref="EventKind.None"/>.</summary>
    private static ushort[] SortReaderFilter(EventKind[] filter)
    {
        var sorted = new ushort[filter.Length];
        for (int i = 0; i < filter.Length; i++)
        {
            ushort kind = (ushort)filter[i];
            if (kind == 0)
            {
                throw new SimBuildException(SimBuildErrorCode.InvalidEventReader, "An event reader filter must not contain EventKind.None.");
            }

            // Insertion sort, strictly-greater shift: stable, no Array.Sort (ADR-0006 section 3 rule 5).
            int j = i - 1;
            while (j >= 0 && sorted[j] > kind)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = kind;
        }

        return sorted;
    }

    /// <summary>
    /// Validates the tables, copies them to arrays and returns the world in <see cref="SimState.Ready"/>; the builder is then
    /// spent. Op kinds are stored as a sorted <c>ushort[]</c> with parallel descriptor and handler arrays (binary-search lookup).
    /// Validation runs before anything is frozen, so a failed freeze leaves the builder usable (bind the missing handler, retry).
    /// </summary>
    /// <returns>The frozen world.</returns>
    /// <exception cref="SimBuildException"><c>BindAfterFreeze</c> if already frozen; <c>MissingOpHandler</c> for a declared op kind with no handler.</exception>
    /// <example><code>SimWorld world = builder.Freeze();</code></example>
    public SimWorld Freeze()
    {
        ThrowIfFrozen();
        RegisterPlayerOps();
        ThrowIfOpHandlerMissing();
        _frozen = true;

        var phases = new ISimPhase[PhaseCount][];
        for (int i = 0; i < PhaseCount; i++)
        {
            phases[i] = _phases[i].ToArray();
        }

        FreezeOps(out ushort[] opKinds, out OpDescriptor[] opDescriptors, out IOpHandler[] opHandlers);
        return new SimWorld(new SimWorldParts(phases, _tickEnd.ToArray(), _settings, _counters, _checkRing, _probe)
        {
            InitialCounters = _initialCounters,
            EventDescriptors = BuildEventLookup(),
            EventSink = _eventSink,
            EventReaderIds = _readerIds.ToArray(),
            EventReaderFilters = _readerFilters.ToArray(),
            OpKinds = opKinds,
            OpDescriptors = opDescriptors,
            OpHandlers = opHandlers,
            CoreState = _coreState,
            Recorder = _recorder ?? NullOpRecorder.Instance,
            StateContributors = _stateContributors.ToArray(),
        });
    }

    // Sim Core's own System-only PlayerJoin/PlayerLeave ops (Story 022). Registered when BindCoreState supplied both the id allocator
    // and the slot table (a world without them has no player slots, TD-045 item 1). A kind the composition root already declared
    // is left alone, so a test can still substitute its own handler.
    private void RegisterPlayerOps()
    {
        EntityIds? ids = _coreState?.Ids;
        SlotTable? slots = _coreState?.Slots;
        if (ids is null || slots is null)
        {
            return;
        }

        IPlayerJoinListener[] listeners = new IPlayerJoinListener[_joinListenerCount];
        Array.Copy(_joinListeners, listeners, _joinListenerCount);
        if (!IsDeclared(OpKind.PlayerJoin))
        {
            BindOp(CorePlayerOps.JoinDescriptor, new PlayerJoinHandler(ids, slots, listeners));
        }

        if (!IsDeclared(OpKind.PlayerLeave))
        {
            BindOp(CorePlayerOps.LeaveDescriptor, new PlayerLeaveHandler(slots, listeners));
        }
    }

    private bool IsDeclared(OpKind kind)
    {
        for (int i = 0; i < _opDescriptors.Count; i++)
        {
            if (_opDescriptors[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    // ushort-indexed descriptor lookup (an array, so a raise is one bounds check; no dictionary on the hot path).
    private EventDescriptor?[] BuildEventLookup()
    {
        int maxKind = 0;
        for (int i = 0; i < _eventKinds.Count; i++)
        {
            maxKind = Math.Max(maxKind, (ushort)_eventKinds[i].Kind);
        }

        var lookup = new EventDescriptor?[maxKind + 1];
        for (int i = 0; i < _eventKinds.Count; i++)
        {
            lookup[(ushort)_eventKinds[i].Kind] = _eventKinds[i];
        }

        return lookup;
    }

    // Copies the declared ops into arrays sorted ascending by kind: a stable insertion sort of parallel arrays (Array.Sort is banned).
    private void FreezeOps(out ushort[] kinds, out OpDescriptor[] descriptors, out IOpHandler[] handlers)
    {
        int n = _opDescriptors.Count;
        kinds = new ushort[n];
        descriptors = new OpDescriptor[n];
        handlers = new IOpHandler[n];
        for (int i = 0; i < n; i++)
        {
            ushort kind = (ushort)_opDescriptors[i].Kind;
            int j = i - 1;
            while (j >= 0 && kinds[j] > kind)
            {
                kinds[j + 1] = kinds[j];
                descriptors[j + 1] = descriptors[j];
                handlers[j + 1] = handlers[j];
                j--;
            }

            kinds[j + 1] = kind;
            descriptors[j + 1] = _opDescriptors[i];
            handlers[j + 1] = _opHandlers[i]!;
        }
    }

    private void ThrowIfOpHandlerMissing()
    {
        for (int i = 0; i < _opHandlers.Count; i++)
        {
            if (_opHandlers[i] is null)
            {
                throw new SimBuildException(SimBuildErrorCode.MissingOpHandler, $"Op kind 0x{(ushort)_opDescriptors[i].Kind:X4} is declared but has no handler.");
            }
        }
    }

    // Adds the descriptor (handler slot empty) or confirms the same instance is already declared; returns its index.
    private int DeclareChecked(OpDescriptor descriptor)
    {
        for (int i = 0; i < _opDescriptors.Count; i++)
        {
            if (_opDescriptors[i].Kind != descriptor.Kind)
            {
                continue;
            }

            if (!ReferenceEquals(_opDescriptors[i], descriptor))
            {
                throw new SimBuildException(SimBuildErrorCode.DuplicateOpHandler, $"Op kind 0x{(ushort)descriptor.Kind:X4} is already declared with a different descriptor.");
            }

            return i;
        }

        _opDescriptors.Add(descriptor);
        _opHandlers.Add(null);
        return _opDescriptors.Count - 1;
    }

    private static void ValidateEventDescriptor(EventDescriptor d)
    {
        if (d.Kind == EventKind.None)
        {
            throw new SimBuildException(SimBuildErrorCode.InvalidKind, "An event descriptor must not use EventKind.None.");
        }

        if (d.PayloadLength > EventPayload.Size)
        {
            throw new SimBuildException(SimBuildErrorCode.PayloadTooLarge, $"Event kind 0x{(ushort)d.Kind:X4} declares a {d.PayloadLength} B payload; the limit is {EventPayload.Size} B.");
        }

        if (d.Class > ReplicationClass.Targeted || d.ResyncScope > 63)
        {
            throw new SimBuildException(SimBuildErrorCode.InvalidEventDescriptor, $"Event descriptor for 0x{(ushort)d.Kind:X4} is invalid: class undefined or resync scope over 63.");
        }
    }

    private static void ValidateOpDescriptor(OpDescriptor d)
    {
        if (d.Kind == OpKind.None)
        {
            throw new SimBuildException(SimBuildErrorCode.InvalidKind, "An op descriptor must not use OpKind.None.");
        }

        if (d.PayloadLength > OpPayload.Size)
        {
            throw new SimBuildException(SimBuildErrorCode.PayloadTooLarge, $"Op kind 0x{(ushort)d.Kind:X4} declares a {d.PayloadLength} B payload; the limit is {OpPayload.Size} B.");
        }

        for (int i = 0; i < d.Reasons.Length; i++)
        {
            if (d.Reasons[i].Code < CoreReason.FirstSystemReason)
            {
                throw new SimBuildException(SimBuildErrorCode.SystemReasonBelow64, $"Op kind 0x{(ushort)d.Kind:X4} declares reason code {d.Reasons[i].Code}; system reasons start at {CoreReason.FirstSystemReason}.");
            }
        }
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new SimBuildException(SimBuildErrorCode.BindAfterFreeze, "The world is frozen; binds are only legal before Freeze().");
        }
    }
}
