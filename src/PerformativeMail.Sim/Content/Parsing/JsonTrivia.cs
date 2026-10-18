namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// Skips JSON whitespace and comments (and optionally commas and colons) by hand, to classify where a reader failure
/// happened. Stateless and allocation-free, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// int next = JsonTrivia.Skip("  // c\n [1]"u8, 0, skipSeparators: false);   // 9, the '['
/// </code>
/// </example>
internal static class JsonTrivia
{
    /// <summary>
    /// Returns the offset of the first byte at or after <paramref name="from"/> that is not whitespace or a complete comment
    /// (and, when asked, not a comma or colon); the length when there is none. An unterminated block comment counts as
    /// significant at its opening slash.
    /// </summary>
    /// <param name="s">The bytes.</param>
    /// <param name="from">Where to start.</param>
    /// <param name="skipSeparators">True to also skip commas and colons.</param>
    /// <returns>The offset of the first significant byte, or <c>s.Length</c>.</returns>
    public static int Skip(ReadOnlySpan<byte> s, int from, bool skipSeparators)
    {
        int i = from;
        while (i < s.Length)
        {
            int next = SkipOne(s, i, skipSeparators);
            if (next == i)
            {
                return i;
            }

            i = next;
        }

        return s.Length;
    }

    private static int SkipOne(ReadOnlySpan<byte> s, int i, bool skipSeparators)
    {
        byte b = s[i];
        if (IsBlank(b) || (skipSeparators && b is (byte)',' or (byte)':'))
        {
            return i + 1;
        }

        return b == (byte)'/' ? SkipComment(s, i) : i;
    }

    private static bool IsBlank(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    /// <summary>The offset after the comment starting at <paramref name="i"/>, or <paramref name="i"/> when it is not a complete comment.</summary>
    private static int SkipComment(ReadOnlySpan<byte> s, int i)
    {
        if (i + 1 >= s.Length)
        {
            return i;
        }

        if (s[i + 1] == (byte)'/')
        {
            int newline = s.Slice(i + 2).IndexOf((byte)'\n');
            return newline < 0 ? s.Length : i + 2 + newline;
        }

        if (s[i + 1] == (byte)'*')
        {
            int end = s.Slice(i + 2).IndexOf("*/"u8);
            return end < 0 ? i : i + 2 + end + 2;
        }

        return i;
    }
}
