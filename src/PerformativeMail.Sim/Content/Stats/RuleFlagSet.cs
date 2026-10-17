namespace PerformativeMail.Sim.Content.Stats;

/// <summary>
/// A set of <see cref="RuleFlag"/>s, one bit per flag (bit <c>flag - 1</c>). A source holds a set; the sheet counts live sources
/// per flag (TR-content-018), so a flag is on while at least one live source holds it. Immutable.
/// </summary>
/// <param name="Bits">The raw bits; bit <c>n</c> is the flag whose value is <c>n + 1</c>.</param>
/// <example>
/// <code>
/// RuleFlagSet flags = RuleFlagSet.Of(RuleFlag.PriorityMail).With(RuleFlag.RushHour);
/// bool has = flags.Contains(RuleFlag.RushHour);   // true
/// </code>
/// </example>
public readonly record struct RuleFlagSet(uint Bits)
{
    /// <summary>The set of one flag; empty when <paramref name="flag"/> is outside 1 to 32.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>The set.</returns>
    public static RuleFlagSet Of(RuleFlag flag) => default(RuleFlagSet).With(flag);

    /// <summary>This set plus <paramref name="flag"/>; unchanged when the flag is outside 1 to 32.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>The new set.</returns>
    public RuleFlagSet With(RuleFlag flag) => BitOf(flag) is { } bit ? new RuleFlagSet(Bits | bit) : this;

    /// <summary>True when <paramref name="flag"/> is in the set.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>True when held.</returns>
    public bool Contains(RuleFlag flag) => BitOf(flag) is { } bit && (Bits & bit) != 0;

    private static uint? BitOf(RuleFlag flag)
    {
        int ordinal = (int)flag - 1;
        return unchecked((uint)ordinal) < 32u ? 1u << ordinal : null;
    }

    /// <summary>
    /// The highest defined <see cref="RuleFlag"/> value: the width of a per-flag table. <see cref="RuleFlag"/> is append-only;
    /// whoever appends a flag moves this to the new last member (a test pins it to the enum's maximum). No reflection
    /// (content sources never use reflection-based enum APIs).
    /// </summary>
    internal const int DefinedWidth = (int)RuleFlag.RushHour;
}
