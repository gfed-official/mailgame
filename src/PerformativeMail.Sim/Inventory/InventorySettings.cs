using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The immutable Grid Inventory settings record (ADR-0003 section 4, ADR-0009 section 5): the capacities every table is preallocated
/// from at world build. Built once from content-data's <see cref="InventoryBalance"/>; the tick path never reads the balance table.
/// The three keys are PROPOSED rows (the GDD gives no capacity keys; flagged for the GDD re-review). Thread-safe (immutable).
/// </summary>
/// <example>
/// <code>
/// InventorySettings s = InventorySettings.FromBalance(BuiltInContent.CreateM1a().Inventory);
/// int containers = s.ContainerCapacity;   // 64
/// </code>
/// </example>
public sealed record class InventorySettings
{
    /// <summary>Container table slots. Key <c>inventory.containerCapacity</c>, default 64.</summary>
    public required int ContainerCapacity { get; init; }

    /// <summary>Entry pool slots (story 002). Key <c>inventory.entryPoolCapacity</c>, default 8192.</summary>
    public required int EntryPoolCapacity { get; init; }

    /// <summary>World item slots (cap plus headroom). Key <c>inventory.worldItemCapacity</c>, default 1024.</summary>
    public required int WorldItemCapacity { get; init; }

    /// <summary>Mail-ids arena unit budget across its nine size classes (story 003). Key <c>inventory.mailIdArenaUnits</c>, default 32768.</summary>
    public required int MailIdArenaUnits { get; init; }

    /// <summary>D1 <c>classPts[light]</c> (story 016). Key <c>inventory.weightPoints.light</c>, default 1.</summary>
    public required int WeightPointsLight { get; init; }

    /// <summary>D1 <c>classPts[medium]</c> (story 016). Key <c>inventory.weightPoints.medium</c>, default 3.</summary>
    public required int WeightPointsMedium { get; init; }

    /// <summary>D1 <c>classPts[heavy]</c> (story 016). Key <c>inventory.weightPoints.heavy</c>, default 8.</summary>
    public required int WeightPointsHeavy { get; init; }

    /// <summary>D1 speed penalty per weight point, permille (story 016). Key <c>inventory.ptsPermille</c>, default 10.</summary>
    public required int PtsPermille { get; init; }

    /// <summary>D1 minimum speed share, permille (story 016). Key <c>inventory.weightSpeedFloor</c>, default 600.</summary>
    public required int WeightSpeedFloor { get; init; }

    /// <summary>
    /// The last entry id already issued when the world is built, so the first new id is this + 1 (ADR-0004 section 3: entry ids only
    /// rise and are never reused). Not a balance key: 0 (the default) for a fresh run. No save/resume path sets it yet; the story 008
    /// tests set it near <see cref="Storage.EntryPool.MaxRealId"/> to reach id exhaustion without issuing two billion ids. It must be
    /// identical on every peer, or entry ids diverge. Above <see cref="Storage.EntryPool.MaxRealId"/> the world build throws the
    /// <see cref="System.ArgumentOutOfRangeException"/> from <see cref="Storage.EntryPool"/>'s constructor.
    /// </summary>
    public uint EntryInitialLastId { get; init; }

    /// <summary>
    /// Nodes in the shared outfeed FIFO pool (story 012; Planner R7): derived, <c>2 x</c> <see cref="EntryPoolCapacity"/> (16,384 by
    /// default), not a balance key. Compaction keeps each outfeed's tombstones at or below its live entries, so all outfeeds together
    /// hold at most twice the live entries and the pool never runs out.
    /// </summary>
    public int OutfeedNodeCapacity => 2 * EntryPoolCapacity;

    /// <summary>Builds the settings from <paramref name="balance"/>.</summary>
    /// <param name="balance">The content set's <c>inventory.*</c> record.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="balance"/> is null.</exception>
    /// <exception cref="SimBuildException"><c>InventorySettingsInvalid</c>: a capacity below 1, the container or entry capacity above 65,535 (slots are <c>ushort</c>), or an arena budget below 1024 units.</exception>
    public static InventorySettings FromBalance(InventoryBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (balance.ContainerCapacity < 1 || balance.ContainerCapacity > ushort.MaxValue)
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, "inventory.containerCapacity must be 1 to 65535.");
        }

        if (balance.EntryPoolCapacity < 1 || balance.EntryPoolCapacity > ushort.MaxValue)
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, "inventory.entryPoolCapacity must be 1 to 65535 (entry slots are ushort).");
        }

        if (balance.WorldItemCapacity < 1 || balance.WorldItemCapacity > World.WorldItemTable.MaxCapacity)
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, "inventory.worldItemCapacity must be 1 to 65535 (WorldItem slots are ushort).");
        }

        if (balance.MailIdArenaUnits < Storage.MailIdArena.MinTotalUnits)
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, $"inventory.mailIdArenaUnits must be at least {Storage.MailIdArena.MinTotalUnits}.");
        }

        return new InventorySettings
        {
            ContainerCapacity = balance.ContainerCapacity,
            EntryPoolCapacity = balance.EntryPoolCapacity,
            WorldItemCapacity = balance.WorldItemCapacity,
            MailIdArenaUnits = balance.MailIdArenaUnits,
            WeightPointsLight = balance.WeightPointsLight,
            WeightPointsMedium = balance.WeightPointsMedium,
            WeightPointsHeavy = balance.WeightPointsHeavy,
            PtsPermille = balance.PtsPermille,
            WeightSpeedFloor = balance.WeightSpeedFloor,
        };
    }
}
