using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>Negative result codes of <see cref="DestinationDraw.DrawBatch"/>; a non-negative result is the pair count.</summary>
public static class DestinationDrawStatus
{
    /// <summary>The unit count is negative or the address list is empty for a non-empty batch.</summary>
    public const int InvalidInput = -10;

    /// <summary>An output span is shorter than the batch's unit count.</summary>
    public const int OutputTooSmall = -11;
}

/// <summary>
/// F4, destinations, streets and Cursed rolls (GDD mail-destinations section F4 and Determinism step 4; story 012;
/// TR-mail-002/003). Per batch: one pair-rounding draw, then per unit in manifest order one address draw and one Cursed
/// roll: <c>1 + 2m</c> draws, whatever the outcomes (the Cursed roll is drawn even at share 0). Every draw is a single
/// <see cref="RngStream.NextInt"/> (an address is chosen by counting the eligible addresses, drawing an index below the
/// count, then walking to it; no rejection loop). Streets are district-qualified <c>(district, street)</c>; "used
/// streets" is a 256-word <c>stackalloc</c> bitset (16,384 bits: 64 districts x 256 streets). The address list must be
/// sorted ascending (<c>AddressId.Value</c> order; not re-checked here). 0 B, never throws.
/// </summary>
/// <example>
/// <code>
/// int pairs = DestinationDraw.DrawBatch(stream, addresses, 5, 300, 0, addrOut, cursedOut);   // 1 + 2*5 draws
/// </code>
/// </example>
public static class DestinationDraw
{
    private const int UsedWords = 256;
    private const uint Permille = 1000;

    /// <summary>
    /// <c>pairs = min(floor(m / 2), floor(s m / 1000) + [NextInt(1000) &lt; (s m) mod 1000])</c> with
    /// <c>s = streetStreakRatioPm / 2</c>. Always exactly one draw, including <c>m &lt;= 1</c>.
    /// </summary>
    /// <param name="rng">The Mail-owned <c>mail</c> stream for the shift.</param>
    /// <param name="m">The batch's unit count (negative is treated as 0).</param>
    /// <param name="streetStreakRatioPm">The street streak ratio in permille.</param>
    /// <returns>The number of pairs.</returns>
    public static int PairCount(RngStream rng, int m, int streetStreakRatioPm)
    {
        long units = m < 0 ? 0 : m;
        long scaled = (long)(streetStreakRatioPm / 2) * units;
        uint draw = rng.NextInt(Permille);
        long pairs = (scaled / Permille) + (draw < (uint)(scaled % Permille) ? 1 : 0);
        return (int)Math.Min(units / 2, pairs);
    }

    /// <summary>Draws one batch: the pair count, then per unit an address and a Cursed roll.</summary>
    /// <param name="rng">The Mail-owned <c>mail</c> stream for the shift.</param>
    /// <param name="addresses">The unlocked addresses, ascending.</param>
    /// <param name="m">The batch's unit count.</param>
    /// <param name="streetStreakRatioPm">The street streak ratio in permille.</param>
    /// <param name="cursedSharePm">The chance a unit is Cursed, in permille.</param>
    /// <param name="addressOut">Receives each unit's address (length at least m).</param>
    /// <param name="cursedOut">Receives each unit's Cursed flag (length at least m).</param>
    /// <returns>The pair count, or a negative <see cref="DestinationDrawStatus"/> code with nothing drawn.</returns>
    public static int DrawBatch(
        RngStream rng,
        ReadOnlySpan<AddressId> addresses,
        int m,
        int streetStreakRatioPm,
        int cursedSharePm,
        Span<AddressId> addressOut,
        Span<bool> cursedOut)
    {
        int check = CheckInputs(addresses.Length, m, addressOut.Length, cursedOut.Length);
        if (check < 0)
        {
            return check;
        }

        int pairs = PairCount(rng, m, streetStreakRatioPm);
        Span<ulong> used = stackalloc ulong[UsedWords];
        used.Clear();
        uint cursed = cursedSharePm < 0 ? 0u : (uint)cursedSharePm;
        int firstKey = -1;
        for (int i = 0; i < m; i++)
        {
            bool second = i < 2 * pairs && (i & 1) == 1;
            AddressId a = second
                ? Pick(rng, addresses, used, firstKey, onStreet: true)
                : Pick(rng, addresses, used, -1, onStreet: false);
            addressOut[i] = a;
            firstKey = IsPairFirst(i, pairs) ? Key(a) : firstKey;
            Mark(used, Key(a));
            cursedOut[i] = rng.NextInt(Permille) < cursed;
        }

        return pairs;
    }

    private static bool IsPairFirst(int i, int pairs) => i < 2 * pairs && (i & 1) == 0;

    private static int CheckInputs(int addressCount, int m, int addressCapacity, int cursedCapacity)
    {
        if (m < 0 || (m > 0 && addressCount == 0))
        {
            return DestinationDrawStatus.InvalidInput;
        }

        return addressCapacity < m || cursedCapacity < m ? DestinationDrawStatus.OutputTooSmall : 0;
    }

    private static int Key(AddressId a) => (int)((a.District << 8) | a.Street);

    private static void Mark(Span<ulong> used, int key) => used[key >> 6] |= 1UL << (key & 63);

    private static bool IsUsed(ReadOnlySpan<ulong> used, int key) => (used[key >> 6] & (1UL << (key & 63))) != 0;

    // Eligible: addresses on the first's street (onStreet), else on unused streets, else all addresses when none remain.
    private static bool Eligible(AddressId a, ReadOnlySpan<ulong> used, int streetKey, bool onStreet, bool all)
        => all || (onStreet ? Key(a) == streetKey : !IsUsed(used, Key(a)));

    private static AddressId Pick(RngStream rng, ReadOnlySpan<AddressId> addresses, ReadOnlySpan<ulong> used, int streetKey, bool onStreet)
    {
        int eligible = 0;
        for (int i = 0; i < addresses.Length; i++)
        {
            eligible += Eligible(addresses[i], used, streetKey, onStreet, all: false) ? 1 : 0;
        }

        bool all = eligible == 0;
        uint target = rng.NextInt((uint)(all ? addresses.Length : eligible));
        for (int i = 0; i < addresses.Length; i++)
        {
            if (!Eligible(addresses[i], used, streetKey, onStreet, all))
            {
                continue;
            }

            if (target == 0)
            {
                return addresses[i];
            }

            target--;
        }

        return addresses[0];
    }
}
