using PerformativeMail.Sim.Content.Balance;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The four permille knobs of the spawn-value formula F1 (GDD mail-destinations section F1; story 011): the per-district
/// step, the per-shift step, the Cursed multiplier and the Found multiplier. Immutable, 0 B, thread-safe. The distance and
/// shift steps are the <c>mail.distanceStepPm</c> / <c>mail.shiftStepPm</c> balance values; the Cursed and Found
/// multipliers have NO balance keys yet (<c>mail.cursedValueMultPm</c> / <c>mail.foundValueMultPm</c> do not exist; they
/// belong to the later R14/R15 stories), so <see cref="From"/> takes them as arguments and <see cref="Default"/> carries the
/// GDD defaults. A test pins <see cref="Default"/>'s two steps to the BuiltInContent balance defaults.
/// </summary>
/// <param name="DistanceStepPm">Permille added per district above 1 (GDD default 250).</param>
/// <param name="ShiftStepPm">Permille added per spawn shift above 1 (GDD default 100).</param>
/// <param name="CursedValueMultPm">Permille multiplier for a Cursed unit (GDD default 1500).</param>
/// <param name="FoundValueMultPm">Permille multiplier for a Found unit (GDD default 3000).</param>
/// <example>
/// <code>
/// MailValueKnobs k = MailValueKnobs.Default;
/// ushort v = SpawnValue.Compute(4, 2, 2, cursed: false, found: false, in k); // 6
/// </code>
/// </example>
public readonly record struct MailValueKnobs(int DistanceStepPm, int ShiftStepPm, int CursedValueMultPm, int FoundValueMultPm)
{
    /// <summary>The GDD defaults: 250 / 100 / 1500 / 3000.</summary>
    public static MailValueKnobs Default => new(250, 100, 1500, 3000);

    /// <summary>Builds the knobs from the resolved mail balance plus the (not yet keyed) Cursed and Found multipliers.</summary>
    /// <param name="mail">The typed <c>mail.*</c> balance record.</param>
    /// <param name="cursedValueMultPm">Cursed multiplier in permille.</param>
    /// <param name="foundValueMultPm">Found multiplier in permille.</param>
    /// <returns>The knobs.</returns>
    public static MailValueKnobs From(MailBalance mail, int cursedValueMultPm, int foundValueMultPm)
    {
        ArgumentNullException.ThrowIfNull(mail);
        return new MailValueKnobs(mail.DistanceStepPm, mail.ShiftStepPm, cursedValueMultPm, foundValueMultPm);
    }
}
