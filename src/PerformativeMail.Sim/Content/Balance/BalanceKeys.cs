using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// The production balance key registry's declarations (ADR-0010 section 4). Story 004 registered the
/// <c>sim.*</c> rows (Simulation Core's Tuning Knobs); story 006 registers the PROPOSED CD12 clamp and base keys of the
/// <c>player</c>, <c>inventory</c>, <c>mail</c>, <c>throw</c> and <c>delivery</c> owners (OQ-CDD-18), and story 008
/// appends the owners' remaining rows (and every default value) to those same owner tables, with the owner GDD's
/// key names verbatim (<c>throw.throwSpeed.light</c>). No static state (ADR-0003 section 4: no mutable statics and no
/// static fields of non-primitive types): the registry is built on each call, and handles come from
/// <see cref="Sim.Resolve"/> against a registry instance, once at composition.
/// </summary>
/// <example>
/// <code>
/// BalanceRegistry registry = BalanceKeys.CreateRegistry();
/// SimKeys keys = BalanceKeys.Sim.Resolve(registry);   // once, at composition
/// BalanceKey capacity = keys.OpQueueCapacity;
/// </code>
/// </example>
public static partial class BalanceKeys
{
    /// <summary>Builds the production registry from every owner table, in ordinal order (a new instance per call).</summary>
    public static BalanceRegistry CreateRegistry() =>
        Compose(Delivery.Rows, Inventory.Rows, Mail.Rows, Player.Rows, Sim.Rows, Throw.Rows);

    /// <summary>
    /// Every default entry (with safe ranges) for every row in <see cref="CreateRegistry"/>: the M-1a balance values. A new
    /// array each call. <c>BalanceTable.Build(CreateRegistry(), CreateDefaultEntries())</c> raises no CV-GEN-05.
    /// </summary>
    /// <example>
    /// <code>
    /// BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());
    /// </code>
    /// </example>
    public static ImmutableArray<BalanceEntry> CreateDefaultEntries()
    {
        ImmutableArray<BalanceEntry>.Builder all = ImmutableArray.CreateBuilder<BalanceEntry>();
        all.AddRange(Delivery.DefaultEntries());
        all.AddRange(Inventory.DefaultEntries());
        all.AddRange(Mail.DefaultEntries());
        all.AddRange(Player.DefaultEntries());
        all.AddRange(SimSettings.DefaultEntries());
        all.AddRange(Throw.DefaultEntries());
        return all.ToImmutable();
    }

    internal static BalanceKey Handle(BalanceRegistry registry, string owner, string key) =>
        registry.TryFind(owner + "." + key, out BalanceKey handle)
            ? handle
            : throw new ArgumentException("The registry has no row " + owner + "." + key + ".", nameof(registry));

    /// <summary>Builds a registry from owner tables (tests add a test-only table, for example a <c>player</c> row).</summary>
    /// <param name="tables">The owner row tables.</param>
    public static BalanceRegistry Compose(params ImmutableArray<BalanceRow>[] tables)
    {
        ImmutableArray<BalanceRow>.Builder all = ImmutableArray.CreateBuilder<BalanceRow>();
        foreach (ImmutableArray<BalanceRow> table in tables)
        {
            if (!table.IsDefault)
            {
                all.AddRange(table);
            }
        }

        return new BalanceRegistry(all.ToImmutable());
    }

    /// <summary>
    /// Simulation Core's keys (owner <c>sim</c>, file <c>balance/sim.jsonc</c>; GDD simulation-core Tuning Knobs and F8).
    /// Hard ranges are correctness bounds only (width, sign); the taste ranges are the <c>safe</c> annotations in
    /// <see cref="SimSettings.DefaultEntries"/>. <c>TickRateHz</c> (30) and <c>maxEventReaders</c> (8) are code constants and
    /// <c>catchUpGuardMs</c> is M0, so none is registered here.
    /// </summary>
    public static class Sim
    {
        /// <summary>The owner prefix and data file stem.</summary>
        public const string Owner = "sim";

        /// <summary>Key: op FIFO capacity.</summary>
        public const string OpQueueCapacityKey = "opQueueCapacity";

        /// <summary>Key: op FIFO slots reserved for system requests.</summary>
        public const string SystemOpReserveKey = "systemOpReserve";

        /// <summary>Key: most ops drained per tick.</summary>
        public const string MaxOpsDrainedPerTickKey = "maxOpsDrainedPerTick";

        /// <summary>Key: event log capacity.</summary>
        public const string EventLogCapacityKey = "eventLogCapacity";

        /// <summary>Key: event log slots reserved for must-keep events.</summary>
        public const string EventLogReserveKey = "eventLogReserve";

        /// <summary>Key: most ticks run per rendered frame.</summary>
        public const string MaxCatchUpTicksPerFrameKey = "maxCatchUpTicksPerFrame";

        /// <summary>Key: capacity of the always-on check ring.</summary>
        public const string CheckRingCapacityKey = "checkRingCapacity";

        /// <summary>Key: ticks excluded from perf and alloc gates.</summary>
        public const string WarmupTicksKey = "warmupTicks";

        /// <summary>Key: stat modifier pool size per team (PROPOSED).</summary>
        public const string ModifierPoolPerTeamKey = "modifierPoolPerTeam";

        /// <summary>Key: stat modifier pool size per player (PROPOSED).</summary>
        public const string ModifierPoolPerPlayerKey = "modifierPoolPerPlayer";

        /// <summary>Key: mail table pages preallocated at world build (1,024 rows each; ADR-0017 section 2).</summary>
        public const string MailTablePagesKey = "mailTablePages";

        /// <summary>The <c>sim.*</c> rows (a new array per call).</summary>
        public static ImmutableArray<BalanceRow> Rows => ImmutableArray.Create(
            Row(OpQueueCapacityKey, BalanceUnit.Count, 8, 1 << 20),
            Row(SystemOpReserveKey, BalanceUnit.Count, 0, 1 << 16),
            Row(MaxOpsDrainedPerTickKey, BalanceUnit.Count, 1, 1 << 16),
            Row(EventLogCapacityKey, BalanceUnit.Count, 8, 1 << 20),
            Row(EventLogReserveKey, BalanceUnit.Count, 0, 1 << 18),
            Row(MaxCatchUpTicksPerFrameKey, BalanceUnit.Count, 1, 64),
            Row(CheckRingCapacityKey, BalanceUnit.Count, 1, 1 << 16),
            Row(WarmupTicksKey, BalanceUnit.Ticks, 0, 100_000),
            Row(ModifierPoolPerTeamKey, BalanceUnit.Count, 1, 1 << 16),
            Row(ModifierPoolPerPlayerKey, BalanceUnit.Count, 1, 1 << 16),
            Row(MailTablePagesKey, BalanceUnit.Count, 1, 1024));

        /// <summary>
        /// Resolves the handles of every <c>sim.*</c> row against <paramref name="registry"/> in one pass. Call it once, at
        /// composition, with the registry of the table you will read (<c>table.Registry</c>) and keep the result in a
        /// <c>readonly</c> field; the handles carry the registry's stamp, so they are rejected by any other table.
        /// </summary>
        /// <param name="registry">A registry holding the <c>sim.*</c> rows (the production one, or a test one that includes them).</param>
        /// <exception cref="ArgumentException">The registry lacks a <c>sim.*</c> row.</exception>
        public static SimKeys Resolve(BalanceRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            return new SimKeys(
                Handle(registry, OpQueueCapacityKey),
                Handle(registry, SystemOpReserveKey),
                Handle(registry, MaxOpsDrainedPerTickKey),
                Handle(registry, EventLogCapacityKey),
                Handle(registry, EventLogReserveKey),
                Handle(registry, MaxCatchUpTicksPerFrameKey),
                Handle(registry, CheckRingCapacityKey),
                Handle(registry, WarmupTicksKey),
                Handle(registry, ModifierPoolPerTeamKey),
                Handle(registry, ModifierPoolPerPlayerKey),
                Handle(registry, MailTablePagesKey));
        }

        private static BalanceRow Row(string key, BalanceUnit unit, long hardMin, long hardMax) =>
            new(Owner, key, unit, hardMin, hardMax);

        private static BalanceKey Handle(BalanceRegistry registry, string key) =>
            registry.TryFind(Owner + "." + key, out BalanceKey handle)
                ? handle
                : throw new ArgumentException("The registry has no row " + Owner + "." + key + ".", nameof(registry));
    }
}
