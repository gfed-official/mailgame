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

        if (balance.WorldItemCapacity < 1)
        {
            throw new SimBuildException(SimBuildErrorCode.InventorySettingsInvalid, "inventory.worldItemCapacity must be at least 1.");
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
        };
    }
}
