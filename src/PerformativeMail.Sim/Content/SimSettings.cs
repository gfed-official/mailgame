using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The Stats system's capacity sub-record (both values PROPOSED, GDD content-data CD11 and the story 004 table).
/// Immutable. Later owners add their own capacity sub-records to <see cref="SimSettings"/> the same way.
/// </summary>
/// <example>
/// <code>
/// var stats = new StatsSettings { ModifierPoolPerTeam = 128, ModifierPoolPerPlayer = 64 };
/// </code>
/// </example>
public sealed record class StatsSettings
{
    /// <summary>The default modifier pool size per team: the single source for the property and the built-in <c>sim.jsonc</c> entry.</summary>
    public const int DefaultModifierPoolPerTeam = 128;

    /// <summary>The default modifier pool size per player: the single source for the property and the built-in <c>sim.jsonc</c> entry.</summary>
    public const int DefaultModifierPoolPerPlayer = 64;

    /// <summary>Modifier pool size per team (default <see cref="DefaultModifierPoolPerTeam"/>).</summary>
    public int ModifierPoolPerTeam { get; init; } = DefaultModifierPoolPerTeam;

    /// <summary>Modifier pool size per player (default <see cref="DefaultModifierPoolPerPlayer"/>).</summary>
    public int ModifierPoolPerPlayer { get; init; } = DefaultModifierPoolPerPlayer;
}

/// <summary>
/// The immutable settings record built once from the <c>sim.*</c> balance keys (<c>balance/sim.jsonc</c>; Simulation
/// Core owns the keys, content-data loads and validates them; ADR-0003 section 4 "the system's own settings record").
/// It is a member of the content set, loaded at world build and never mutated inside a run. Composition:
/// <see cref="Core"/> is Sim Core's <see cref="SimCoreSettings"/>, <see cref="WarmupTicks"/> is the tick count excluded
/// from perf and alloc gates, <see cref="Stats"/> holds the Stats pools. <see cref="TickRateHz"/> and
/// <see cref="MaxEventReaders"/> stay code constants. Cross-field rules (SC-SET-1: powers of two, reserve at most a
/// quarter of capacity) are CV-SIM-01 to 04, tagged M-1b, and are not checked here. Thread-safe (immutable).
/// </summary>
/// <example>
/// <code>
/// BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());
/// SimSettings settings = SimSettings.FromTable(r.Table!);
/// var builder = new SimWorldBuilder(settings.Core);   // settings.Core.OpQueueCapacity == 4096
/// </code>
/// </example>
public sealed record class SimSettings
{
    /// <summary>The fixed tick rate (spec 07 section 1.4): a re-design to change, never a knob.</summary>
    public const int TickRateHz = 30;

    /// <summary>Most event readers that can be bound: a code constant (GDD Tuning Knobs, 4 to 16).</summary>
    public const int MaxEventReaders = 8;

    /// <summary>Sim Core's own settings, filled from the <c>sim.*</c> rows.</summary>
    public SimCoreSettings Core { get; init; } = new();

    /// <summary>The default warm-up: the single source for the property and the built-in <c>sim.jsonc</c> entry.</summary>
    public const int DefaultWarmupTicks = 300;

    /// <summary>Ticks excluded from perf and alloc gates (default <see cref="DefaultWarmupTicks"/>).</summary>
    public int WarmupTicks { get; init; } = DefaultWarmupTicks;

    /// <summary>The Stats sub-record.</summary>
    public StatsSettings Stats { get; init; } = new();

    /// <summary>The default mail table page count: the single source for the property and the built-in <c>sim.jsonc</c> entry (ADR-0017 section 2; 64 pages = 65,536 live rows).</summary>
    public const int DefaultMailTablePages = 64;

    /// <summary>Mail table pages (1,024 rows each) preallocated at world build (default <see cref="DefaultMailTablePages"/>); exhaustion grows the pool and records a check.</summary>
    public int MailTablePages { get; init; } = DefaultMailTablePages;

    /// <summary>
    /// Builds the record from a validated table. Reads each value by name through the table's own registry, once, at
    /// composition.
    /// </summary>
    /// <param name="table">A table whose registry holds the <c>sim.*</c> rows (the production registry or a test one that includes them).</param>
    /// <exception cref="ArgumentException">The table's registry lacks a <c>sim.*</c> row.</exception>
    /// <exception cref="OverflowException">A value does not fit in int (cannot happen while the hard ranges fit).</exception>
    public static SimSettings FromTable(BalanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        SimKeys k = BalanceKeys.Sim.Resolve(table.Registry);
        return new SimSettings
        {
            Core = new SimCoreSettings
            {
                OpQueueCapacity = Read(table, k.OpQueueCapacity),
                SystemOpReserve = Read(table, k.SystemOpReserve),
                MaxOpsDrainedPerTick = Read(table, k.MaxOpsDrainedPerTick),
                EventLogCapacity = Read(table, k.EventLogCapacity),
                EventLogReserve = Read(table, k.EventLogReserve),
                MaxEventReaders = MaxEventReaders,
                MaxCatchUpTicksPerFrame = Read(table, k.MaxCatchUpTicksPerFrame),
                CheckRingCapacity = Read(table, k.CheckRingCapacity),
            },
            WarmupTicks = Read(table, k.WarmupTicks),
            Stats = new StatsSettings
            {
                ModifierPoolPerTeam = Read(table, k.ModifierPoolPerTeam),
                ModifierPoolPerPlayer = Read(table, k.ModifierPoolPerPlayer),
            },
            MailTablePages = Read(table, k.MailTablePages),
        };
    }

    // Every sim row's hard range fits in int (pinned by a test); checked turns a future wider range into a loud failure, not a wrap.
    private static int Read(BalanceTable table, BalanceKey key) => checked((int)table.Get(key));

    /// <summary>
    /// The built-in contents of <c>balance/sim.jsonc</c>: the defaults with the Tuning Knobs safe ranges. The Core values
    /// come from <see cref="SimCoreSettings"/>'s own defaults and the others from the <c>Default*</c> constants, so each
    /// number has one source. This is the explicit feed for built-in content and tests; the table itself has no implicit
    /// defaults. Returns a new array each call.
    /// </summary>
    public static ImmutableArray<BalanceEntry> DefaultEntries()
    {
        var core = new SimCoreSettings();
        return ImmutableArray.Create(
            Entry(BalanceKeys.Sim.OpQueueCapacityKey, core.OpQueueCapacity, 4096, 16_384),
            Entry(BalanceKeys.Sim.SystemOpReserveKey, core.SystemOpReserve, 32, 256),
            Entry(BalanceKeys.Sim.MaxOpsDrainedPerTickKey, core.MaxOpsDrainedPerTick, 32, 256),
            Entry(BalanceKeys.Sim.EventLogCapacityKey, core.EventLogCapacity, 4096, 16_384),
            Entry(BalanceKeys.Sim.EventLogReserveKey, core.EventLogReserve, 64, 4096),
            Entry(BalanceKeys.Sim.MaxCatchUpTicksPerFrameKey, core.MaxCatchUpTicksPerFrame, 3, 8),
            Entry(BalanceKeys.Sim.CheckRingCapacityKey, core.CheckRingCapacity),
            Entry(BalanceKeys.Sim.WarmupTicksKey, DefaultWarmupTicks, 150, 900),
            Entry(BalanceKeys.Sim.ModifierPoolPerTeamKey, StatsSettings.DefaultModifierPoolPerTeam),
            Entry(BalanceKeys.Sim.ModifierPoolPerPlayerKey, StatsSettings.DefaultModifierPoolPerPlayer),
            Entry(BalanceKeys.Sim.MailTablePagesKey, DefaultMailTablePages));
    }

    private static BalanceEntry Entry(string key, long value) =>
        new(BalanceKeys.Sim.Owner, BalanceKeys.Sim.Owner + "." + key, value, SourceLocation.BuiltIn("builtin/balance/sim"));

    private static BalanceEntry Entry(string key, long value, long safeMin, long safeMax) =>
        Entry(key, value) with { Safe = new BalanceSafeRange(safeMin, safeMax) };

    /// <summary>
    /// Builds the default settings (the GDD defaults) through the validated path.
    /// </summary>
    /// <returns>The settings record.</returns>
    public static SimSettings CreateDefault()
    {
        BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());
        return FromTable(r.Table ?? throw new InvalidOperationException("The built-in sim balance entries do not validate."));
    }
}
