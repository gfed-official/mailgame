namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// A hand-written UTF-8 well-formedness scan (RFC 3629: no overlong forms, no surrogates, nothing above U+10FFFF) that also
/// spots a U+FEFF byte order mark. Stateless and allocation-free, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// int bad = Utf8Validator.FindProblem("[\"ok\"]"u8, 0, out bool isBom);   // -1
/// </code>
/// </example>
internal static class Utf8Validator
{
    /// <summary>
    /// Finds the first byte of the first ill-formed sequence, or of the first U+FEFF (reported through
    /// <paramref name="isBom"/>), scanning from <paramref name="from"/>.
    /// </summary>
    /// <param name="s">The bytes.</param>
    /// <param name="from">Where to start (3 when a leading BOM was already accepted).</param>
    /// <param name="isBom">True when the problem found is a U+FEFF rather than ill-formed UTF-8.</param>
    /// <returns>The offset of the problem, or -1 when the rest is well-formed.</returns>
    public static int FindProblem(ReadOnlySpan<byte> s, int from, out bool isBom)
    {
        isBom = false;
        int i = from;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80)
            {
                i++;
                continue;
            }

            int length = SequenceLength(b);
            if (length == 0 || !IsWellFormed(s, i, length))
            {
                return i;
            }

            if (IsBom(s, i, length))
            {
                isBom = true;
                return i;
            }

            i += length;
        }

        return -1;
    }

    private static int SequenceLength(byte lead) => lead switch
    {
        >= 0xC2 and <= 0xDF => 2,
        >= 0xE0 and <= 0xEF => 3,
        >= 0xF0 and <= 0xF4 => 4,
        _ => 0,
    };

    private static byte SecondLow(byte lead) => lead == 0xE0 ? (byte)0xA0 : lead == 0xF0 ? (byte)0x90 : (byte)0x80;

    private static byte SecondHigh(byte lead) => lead == 0xED ? (byte)0x9F : lead == 0xF4 ? (byte)0x8F : (byte)0xBF;

    private static bool IsWellFormed(ReadOnlySpan<byte> s, int i, int length)
    {
        if (i + length > s.Length || s[i + 1] < SecondLow(s[i]) || s[i + 1] > SecondHigh(s[i]))
        {
            return false;
        }

        for (int k = 2; k < length; k++)
        {
            if ((s[i + k] & 0xC0) != 0x80)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBom(ReadOnlySpan<byte> s, int i, int length) =>
        length == 3 && s[i] == 0xEF && s[i + 1] == 0xBB && s[i + 2] == 0xBF;
}
