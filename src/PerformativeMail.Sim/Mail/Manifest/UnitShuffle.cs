using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The Fisher-Yates shuffle of the kind-ordered unit list (GDD Determinism step 2; story 012; TR-mail-003). Exactly
/// <c>max(0, N - 1)</c> draws, each a single <see cref="RngStream.NextInt"/> (no rejection loop). 0 B, never throws.
/// </summary>
/// <example>
/// <code>
/// UnitShuffle.Shuffle(mailStream, kinds.AsSpan(0, n));   // n - 1 draws
/// </code>
/// </example>
public static class UnitShuffle
{
    /// <summary>The number of draws a shuffle of <paramref name="count"/> items makes.</summary>
    /// <param name="count">The item count.</param>
    /// <returns><c>max(0, count - 1)</c>.</returns>
    public static int DrawCount(int count) => count > 1 ? count - 1 : 0;

    /// <summary>Shuffles in place: for i from n-1 down to 1, swap item i with item <c>NextInt(i + 1)</c>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="rng">The Mail-owned <c>mail</c> stream for the shift.</param>
    /// <param name="items">The items to shuffle.</param>
    /// <returns>The number of draws made (<see cref="DrawCount"/>).</returns>
    public static int Shuffle<T>(RngStream rng, Span<T> items)
    {
        int draws = 0;
        for (int i = items.Length - 1; i >= 1; i--)
        {
            int j = (int)rng.NextInt((uint)(i + 1));
            (items[i], items[j]) = (items[j], items[i]);
            draws++;
        }

        return draws;
    }
}
