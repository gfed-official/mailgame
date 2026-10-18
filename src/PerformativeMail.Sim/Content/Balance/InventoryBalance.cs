namespace PerformativeMail.Sim.Content.Balance;

/// <summary>The resolved handles of every <c>inventory.*</c> balance key (see <see cref="BalanceKeys.Inventory.Resolve"/>).</summary>
public readonly record struct InventoryKeys(
    BalanceKey WeightPointsLight,
    BalanceKey WeightPointsMedium,
    BalanceKey WeightPointsHeavy,
    BalanceKey PtsPermille,
    BalanceKey WeightSpeedFloor,
    BalanceKey MaxRows,
    BalanceKey WorldItemDespawnSeconds,
    BalanceKey WorldItemCap,
    BalanceKey CapEvictMinAgeSeconds,
    BalanceKey WaterReturnSeconds,
    BalanceKey ExternalLeashMeters,
    BalanceKey BacklogDrainPerTick,
    BalanceKey ManifestCellThreshold,
    BalanceKey ContainerCapacity,
    BalanceKey EntryPoolCapacity,
    BalanceKey WorldItemCapacity,
    BalanceKey MailIdArenaUnits);

/// <summary>
/// The typed <c>inventory.*</c> values, built once from the <see cref="BalanceTable"/> when the <see cref="ContentSet"/> is
/// built. A system receives only its own record (ADR-0003 section 4). Every value is an already-scaled integer (CD4);
/// <c>...Seconds</c> keys hold milliseconds. Immutable; safe from any thread.
/// </summary>
/// <example><code>InventoryBalance p = contentSet.Inventory; int walk = p.WeightPointsLight;</code></example>
public sealed record class InventoryBalance
{
    /// <summary>Weight points of a light entry. Key <c>inventory.weightPoints.light</c>, default 1.</summary>
    public int WeightPointsLight { get; init; }

    /// <summary>Weight points of a medium entry. Key <c>inventory.weightPoints.medium</c>, default 3.</summary>
    public int WeightPointsMedium { get; init; }

    /// <summary>Weight points of a heavy entry. Key <c>inventory.weightPoints.heavy</c>, default 8.</summary>
    public int WeightPointsHeavy { get; init; }

    /// <summary>Speed penalty per weight point, permille. Key <c>inventory.ptsPermille</c>, default 10.</summary>
    public int PtsPermille { get; init; }

    /// <summary>Minimum carrier speed share. Key <c>inventory.weightSpeedFloor</c>, default 600.</summary>
    public int WeightSpeedFloor { get; init; }

    /// <summary>Cap on container rows (CD12 clamp). Key <c>inventory.maxRows</c>, default 16.</summary>
    public int MaxRows { get; init; }

    /// <summary>World item lifetime, ms. Key <c>inventory.worldItemDespawnSeconds</c>, default 300_000.</summary>
    public int WorldItemDespawnSeconds { get; init; }

    /// <summary>Soft cap on world items. Key <c>inventory.worldItemCap</c>, default 512.</summary>
    public int WorldItemCap { get; init; }

    /// <summary>Minimum age before cap eviction, ms. Key <c>inventory.capEvictMinAgeSeconds</c>, default 10_000.</summary>
    public int CapEvictMinAgeSeconds { get; init; }

    /// <summary>Sea-landed mail return delay, ms. Key <c>inventory.waterReturnSeconds</c>, default 10_000.</summary>
    public int WaterReturnSeconds { get; init; }

    /// <summary>External container auto-close distance, mm. Key <c>inventory.externalLeashMeters</c>, default 3000.</summary>
    public int ExternalLeashMeters { get; init; }

    /// <summary>Backlog entries placed per tick. Key <c>inventory.backlogDrainPerTick</c>, default 32.</summary>
    public int BacklogDrainPerTick { get; init; }

    /// <summary>Cell count above which a container renders as a manifest. Key <c>inventory.manifestCellThreshold</c>, default 160.</summary>
    public int ManifestCellThreshold { get; init; }

    /// <summary>Slots in the container table (live containers at most). Key <c>inventory.containerCapacity</c>, default 64.</summary>
    public int ContainerCapacity { get; init; }

    /// <summary>Slots in the entry pool (story 002). Key <c>inventory.entryPoolCapacity</c>, default 8192.</summary>
    public int EntryPoolCapacity { get; init; }

    /// <summary>Slots for world items, cap plus temporary headroom. Key <c>inventory.worldItemCapacity</c>, default 1024.</summary>
    public int WorldItemCapacity { get; init; }

    /// <summary>Unit budget of the mail-ids arena (story 003). Key <c>inventory.mailIdArenaUnits</c>, default 32768.</summary>
    public int MailIdArenaUnits { get; init; }

    /// <summary>Reads every inventory value out of <paramref name="table"/>.</summary>
    /// <param name="table">A table built from a registry that holds the inventory rows.</param>
    /// <exception cref="ArgumentException">The table's registry lacks a inventory row.</exception>
    public static InventoryBalance FromTable(BalanceTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        InventoryKeys k = BalanceKeys.Inventory.Resolve(table.Registry);
        return new InventoryBalance
        {
            WeightPointsLight = table.GetInt(k.WeightPointsLight),
            WeightPointsMedium = table.GetInt(k.WeightPointsMedium),
            WeightPointsHeavy = table.GetInt(k.WeightPointsHeavy),
            PtsPermille = table.GetInt(k.PtsPermille),
            WeightSpeedFloor = table.GetInt(k.WeightSpeedFloor),
            MaxRows = table.GetInt(k.MaxRows),
            WorldItemDespawnSeconds = table.GetInt(k.WorldItemDespawnSeconds),
            WorldItemCap = table.GetInt(k.WorldItemCap),
            CapEvictMinAgeSeconds = table.GetInt(k.CapEvictMinAgeSeconds),
            WaterReturnSeconds = table.GetInt(k.WaterReturnSeconds),
            ExternalLeashMeters = table.GetInt(k.ExternalLeashMeters),
            BacklogDrainPerTick = table.GetInt(k.BacklogDrainPerTick),
            ManifestCellThreshold = table.GetInt(k.ManifestCellThreshold),
            ContainerCapacity = table.GetInt(k.ContainerCapacity),
            EntryPoolCapacity = table.GetInt(k.EntryPoolCapacity),
            WorldItemCapacity = table.GetInt(k.WorldItemCapacity),
            MailIdArenaUnits = table.GetInt(k.MailIdArenaUnits),
        };
    }
}
