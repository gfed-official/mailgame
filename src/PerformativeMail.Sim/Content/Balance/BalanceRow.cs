namespace PerformativeMail.Sim.Content.Balance;

/// <summary>
/// One registry row (GDD CD9): the owner GDD, the key within it, the stored unit and the HARD range (correctness: a
/// value outside it is CV-GEN-13, an Error). The data-file <c>safe</c> range (taste) is not part of the row; it travels
/// with each supplied value (<see cref="BalanceEntry.Safe"/>). A key name may itself contain dots
/// (<c>throw.throwSpeed.light</c>): the first dot separates the owner. Immutable.
/// </summary>
/// <param name="Owner">The owner prefix, with no dot (<c>sim</c>, <c>player</c>); also the data file stem <c>balance/&lt;owner&gt;.jsonc</c>.</param>
/// <param name="Key">The key within the owner, verbatim from the owner GDD (may contain dots).</param>
/// <param name="Unit">The stored unit.</param>
/// <param name="HardMin">The lowest correct value (inclusive).</param>
/// <param name="HardMax">The highest correct value (inclusive).</param>
/// <example>
/// <code>
/// var row = new BalanceRow("player", "rangeMm", BalanceUnit.Mm, 100, 20_000);
/// string name = row.Name;   // "player.rangeMm"
/// </code>
/// </example>
public sealed record BalanceRow(string Owner, string Key, BalanceUnit Unit, long HardMin, long HardMax)
{
    /// <summary>The full address <c>owner.key</c> (computed, so it follows a <c>with</c> copy; allocates, so not for per-tick code; the registry caches the names).</summary>
    public string Name => Owner + "." + Key;
}
