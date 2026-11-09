namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The thrower's launch personals the aim preview feeds F1 (throwing-catching story 004): the dominant hand from the player profile and the
/// <c>ThrowRangePermille</c> stat from the client's StatSheet copy. <see cref="DefaultThrowerProfile"/> is the right-handed, no-perk default.
/// </summary>
/// <example><code>sbyte side = profile.Side;   // +1 right-handed</code></example>
public interface IThrowerProfile
{
    /// <summary>The throwing hand: +1 right-handed, −1 left-handed.</summary>
    sbyte Side { get; }

    /// <summary>The thrower's <c>ThrowRangePermille</c> (1000 = no perk).</summary>
    int ThrowRangePermille { get; }
}

/// <summary>The default <see cref="IThrowerProfile"/>: right-handed (+1), <c>ThrowRangePermille</c> 1000. Stateless.</summary>
/// <example><code>IThrowerProfile p = new DefaultThrowerProfile();</code></example>
public sealed class DefaultThrowerProfile : IThrowerProfile
{
    /// <inheritdoc />
    public sbyte Side => 1;

    /// <inheritdoc />
    public int ThrowRangePermille => 1000;
}
