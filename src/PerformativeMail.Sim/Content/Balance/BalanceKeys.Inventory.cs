using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Balance;

public static partial class BalanceKeys
{
    /// <summary>The <c>inventory.*</c> balance rows: key names, units, hard ranges, M-1a default values and safe ranges (CD9).</summary>
    /// <example><code>ImmutableArray&lt;BalanceRow&gt; rows = BalanceKeys.Inventory.Rows;</code></example>
    public static partial class Inventory
    {
        /// <summary>The owner file name and key prefix.</summary>
        public const string Owner = "inventory";

        /// <summary>The key <c>inventory.weightPoints.light</c>.</summary>
        public const string WeightPointsLightKey = "weightPoints.light";

        /// <summary>The key <c>inventory.weightPoints.medium</c>.</summary>
        public const string WeightPointsMediumKey = "weightPoints.medium";

        /// <summary>The key <c>inventory.weightPoints.heavy</c>.</summary>
        public const string WeightPointsHeavyKey = "weightPoints.heavy";

        /// <summary>The key <c>inventory.ptsPermille</c>.</summary>
        public const string PtsPermilleKey = "ptsPermille";

        /// <summary>The key <c>inventory.weightSpeedFloor</c>.</summary>
        public const string WeightSpeedFloorKey = "weightSpeedFloor";

        /// <summary>The key <c>inventory.maxRows</c>.</summary>
        public const string MaxRowsKey = "maxRows";

        /// <summary>The key <c>inventory.worldItemDespawnSeconds</c>.</summary>
        public const string WorldItemDespawnSecondsKey = "worldItemDespawnSeconds";

        /// <summary>The key <c>inventory.worldItemCap</c>.</summary>
        public const string WorldItemCapKey = "worldItemCap";

        /// <summary>The key <c>inventory.capEvictMinAgeSeconds</c>.</summary>
        public const string CapEvictMinAgeSecondsKey = "capEvictMinAgeSeconds";

        /// <summary>The key <c>inventory.waterReturnSeconds</c>.</summary>
        public const string WaterReturnSecondsKey = "waterReturnSeconds";

        /// <summary>The key <c>inventory.externalLeashMeters</c>.</summary>
        public const string ExternalLeashMetersKey = "externalLeashMeters";

        /// <summary>The key <c>inventory.backlogDrainPerTick</c>.</summary>
        public const string BacklogDrainPerTickKey = "backlogDrainPerTick";

        /// <summary>The key <c>inventory.manifestCellThreshold</c>.</summary>
        public const string ManifestCellThresholdKey = "manifestCellThreshold";

        /// <summary>The key <c>inventory.containerCapacity</c> (PROPOSED, GDD re-review).</summary>
        public const string ContainerCapacityKey = "containerCapacity";

        /// <summary>The key <c>inventory.entryPoolCapacity</c> (PROPOSED, GDD re-review).</summary>
        public const string EntryPoolCapacityKey = "entryPoolCapacity";

        /// <summary>The key <c>inventory.worldItemCapacity</c> (PROPOSED, GDD re-review).</summary>
        public const string WorldItemCapacityKey = "worldItemCapacity";

        /// <summary>The key <c>inventory.mailIdArenaUnits</c> (PROPOSED, GDD re-review; story 003).</summary>
        public const string MailIdArenaUnitsKey = "mailIdArenaUnits";

        // One table drives the rows, the default entries and (with the generated handle record) the owner record, so a key's range and value cannot drift apart.
        private static ImmutableArray<BalanceKnob> Knobs => ImmutableArray.Create(
            new BalanceKnob(WeightPointsLightKey, BalanceUnit.Count, 1, 0, 1_048_576, 1, 2),
            new BalanceKnob(WeightPointsMediumKey, BalanceUnit.Count, 3, 0, 1_048_576, 2, 5),
            new BalanceKnob(WeightPointsHeavyKey, BalanceUnit.Count, 8, 0, 1_048_576, 5, 12),
            new BalanceKnob(PtsPermilleKey, BalanceUnit.Permille, 10, 0, 100_000, 5, 20),
            new BalanceKnob(WeightSpeedFloorKey, BalanceUnit.Permille, 600, 0, 1000, 500, 800),
            new BalanceKnob(MaxRowsKey, BalanceUnit.Count, 16, 1, 1024),
            new BalanceKnob(WorldItemDespawnSecondsKey, BalanceUnit.Ms, 300_000, 0, 3_600_000, 60_000, 600_000),
            new BalanceKnob(WorldItemCapKey, BalanceUnit.Count, 512, 0, 1_048_576, 256, 2048),
            new BalanceKnob(CapEvictMinAgeSecondsKey, BalanceUnit.Ms, 10_000, 0, 3_600_000, 3000, 30_000),
            new BalanceKnob(WaterReturnSecondsKey, BalanceUnit.Ms, 10_000, 0, 3_600_000, 5000, 30_000),
            new BalanceKnob(ExternalLeashMetersKey, BalanceUnit.Mm, 3000, 0, 1_000_000, 3000, 5000),
            new BalanceKnob(BacklogDrainPerTickKey, BalanceUnit.Count, 32, 0, 1_048_576, 8, 128),
            new BalanceKnob(ManifestCellThresholdKey, BalanceUnit.Count, 160, 0, 1_048_576, 160, 239),
            new BalanceKnob(ContainerCapacityKey, BalanceUnit.Count, 64, 8, 4096, 32, 256),
            new BalanceKnob(EntryPoolCapacityKey, BalanceUnit.Count, 8192, 64, 1_048_576, 1024, 32_768),
            new BalanceKnob(WorldItemCapacityKey, BalanceUnit.Count, 1024, 16, 65_536, 512, 4096),
            new BalanceKnob(MailIdArenaUnitsKey, BalanceUnit.Count, 32_768, 1024, 1_048_576, 16_384, 65_536));

        /// <summary>The registry rows (a new array each call; the class holds no static state).</summary>
        public static ImmutableArray<BalanceRow> Rows => BalanceKnob.ToRows(Owner, Knobs);

        /// <summary>The M-1a default entries with their safe-range annotations (a new array each call).</summary>
        /// <example><code>BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());</code></example>
        public static ImmutableArray<BalanceEntry> DefaultEntries() => BalanceKnob.ToEntries(Owner, Knobs);

        /// <summary>Resolves every <c>inventory.*</c> handle against <paramref name="registry"/> once, at composition.</summary>
        /// <param name="registry">A registry that holds every inventory row (for example <see cref="CreateRegistry"/>).</param>
        /// <exception cref="ArgumentException">A row is missing.</exception>
        /// <example><code>InventoryKeys keys = BalanceKeys.Inventory.Resolve(table.Registry);</code></example>
        public static InventoryKeys Resolve(BalanceRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            return new InventoryKeys(
                BalanceKeys.Handle(registry, Owner, WeightPointsLightKey),
                BalanceKeys.Handle(registry, Owner, WeightPointsMediumKey),
                BalanceKeys.Handle(registry, Owner, WeightPointsHeavyKey),
                BalanceKeys.Handle(registry, Owner, PtsPermilleKey),
                BalanceKeys.Handle(registry, Owner, WeightSpeedFloorKey),
                BalanceKeys.Handle(registry, Owner, MaxRowsKey),
                BalanceKeys.Handle(registry, Owner, WorldItemDespawnSecondsKey),
                BalanceKeys.Handle(registry, Owner, WorldItemCapKey),
                BalanceKeys.Handle(registry, Owner, CapEvictMinAgeSecondsKey),
                BalanceKeys.Handle(registry, Owner, WaterReturnSecondsKey),
                BalanceKeys.Handle(registry, Owner, ExternalLeashMetersKey),
                BalanceKeys.Handle(registry, Owner, BacklogDrainPerTickKey),
                BalanceKeys.Handle(registry, Owner, ManifestCellThresholdKey),
                BalanceKeys.Handle(registry, Owner, ContainerCapacityKey),
                BalanceKeys.Handle(registry, Owner, EntryPoolCapacityKey),
                BalanceKeys.Handle(registry, Owner, WorldItemCapacityKey),
                BalanceKeys.Handle(registry, Owner, MailIdArenaUnitsKey));
        }
    }
}
