namespace PerformativeMail.Sim.Core;

/// <summary>
/// Everything <see cref="SimWorldBuilder.Freeze"/> hands to the <see cref="SimWorld"/> constructor. Internal:
/// new world dependencies (Stories 015, 020, 021) are added here instead of growing a positional parameter list.
/// </summary>
/// <param name="Phases">Frozen binder arrays, one per phase.</param>
/// <param name="TickEnd">Frozen tick-end checks.</param>
/// <param name="Settings">Sim Core settings.</param>
/// <param name="Counters">The world's counters.</param>
/// <param name="CheckRing">The world's check ring.</param>
/// <param name="Probe">The phase timing probe.</param>
/// <example>
/// <code>
/// var world = new SimWorld(new SimWorldParts(phases, checks, settings, counters, ring, probe));
/// </code>
/// </example>
internal sealed record SimWorldParts(
    ISimPhase[][] Phases,
    ITickEndCheck[] TickEnd,
    SimCoreSettings Settings,
    SimCounters Counters,
    SimCheckRing CheckRing,
    IPhaseProbe Probe)
{
    /// <summary>The state the world starts in; the builder always uses <c>Ready</c>. Tests set other states to exercise the guards.</summary>
    public SimState InitialState { get; init; } = SimState.Ready;

    /// <summary>Starting counter values (default: all zero).</summary>
    public SimInitialCounters InitialCounters { get; init; } = new();

    /// <summary>Event descriptors indexed by <c>(ushort)EventKind</c>; null where a kind is undeclared (default: none declared).</summary>
    public EventDescriptor?[] EventDescriptors { get; init; } = Array.Empty<EventDescriptor?>();

    /// <summary>The host's event sink; null when none is bound (default).</summary>
    public IEventSink? EventSink { get; init; }

    /// <summary>The bound pull readers' ids, in bind order (default: none). Parallel to <see cref="EventReaderFilters"/>.</summary>
    public ReaderId[] EventReaderIds { get; init; } = Array.Empty<ReaderId>();

    /// <summary>Each reader's kind filter as an ascending <c>ushort[]</c>, parallel to <see cref="EventReaderIds"/>.</summary>
    public ushort[][] EventReaderFilters { get; init; } = Array.Empty<ushort[]>();

    /// <summary>Declared op kinds, ascending, parallel to <see cref="OpDescriptors"/> and <see cref="OpHandlers"/> (default: none).</summary>
    public ushort[] OpKinds { get; init; } = Array.Empty<ushort>();

    /// <summary>Op descriptors, parallel to <see cref="OpKinds"/>.</summary>
    public OpDescriptor[] OpDescriptors { get; init; } = Array.Empty<OpDescriptor>();

    /// <summary>Op handlers, parallel to <see cref="OpKinds"/>; never null once built by <c>Freeze()</c>.</summary>
    public IOpHandler[] OpHandlers { get; init; } = Array.Empty<IOpHandler>();

    /// <summary>The Sim Core objects section 1 of the canonical dump reads; null when none were bound (default).</summary>
    internal SimCoreStateSources? CoreState { get; init; }

    /// <summary>The op recorder; the null-object recorder by default.</summary>
    public IOpRecorder Recorder { get; init; } = NullOpRecorder.Instance;

    /// <summary>Bound state contributors in registration order, excluding Sim Core's own section (default: none).</summary>
    public ISimStateContributor[] StateContributors { get; init; } = Array.Empty<ISimStateContributor>();
}
