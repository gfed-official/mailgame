using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Balance;

public static partial class BalanceKeys
{
    /// <summary>The <c>delivery.*</c> balance rows: key names, units, hard ranges, M-1a default values and safe ranges (CD9).</summary>
    /// <example><code>ImmutableArray&lt;BalanceRow&gt; rows = BalanceKeys.Delivery.Rows;</code></example>
    public static partial class Delivery
    {
        /// <summary>The owner file name and key prefix.</summary>
        public const string Owner = "delivery";

        /// <summary>The key <c>delivery.insertPmMax</c>.</summary>
        public const string InsertPmMaxKey = "insertPmMax";

        // One table drives the rows, the default entries and (with the generated handle record) the owner record, so a key's range and value cannot drift apart.
        private static ImmutableArray<BalanceKnob> Knobs => ImmutableArray.Create(
            new BalanceKnob(InsertPmMaxKey, BalanceUnit.Permille, 750, 0, 100_000));

        /// <summary>The registry rows (a new array each call; the class holds no static state).</summary>
        public static ImmutableArray<BalanceRow> Rows => BalanceKnob.ToRows(Owner, Knobs);

        /// <summary>The M-1a default entries with their safe-range annotations (a new array each call).</summary>
        /// <example><code>BalanceBuildResult r = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries());</code></example>
        public static ImmutableArray<BalanceEntry> DefaultEntries() => BalanceKnob.ToEntries(Owner, Knobs);
    }
}
