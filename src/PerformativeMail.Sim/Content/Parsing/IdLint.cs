namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The id lint (ADR-0010 section 3, GDD CD3): a byte scan equivalent to <c>^[a-z][a-z0-9_]{0,47}$</c> with no trailing
/// newline leniency (a .NET Regex <c>$</c> would accept one). Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool ok = IdLint.IsValid("plank_2"u8);   // true
/// bool no = IdLint.IsValid("Letter"u8);    // false (upper case)
/// </code>
/// </example>
public static class IdLint
{
    /// <summary>The longest id, in bytes.</summary>
    public const int MaxLength = 48;

    /// <summary>The human form of the rule, for the <c>Allowed</c> text of a CV-GEN-02 finding.</summary>
    public const string Allowed = "^[a-z][a-z0-9_]{0,47}$";

    /// <summary>True when <paramref name="utf8"/> is a valid id.</summary>
    /// <param name="utf8">The id bytes (no quotes, raw: an escape sequence fails the scan).</param>
    /// <example><code>bool ok = IdLint.IsValid("ab"u8);</code></example>
    public static bool IsValid(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length < 1 || utf8.Length > MaxLength || !IsLower(utf8[0]))
        {
            return false;
        }

        for (int i = 1; i < utf8.Length; i++)
        {
            byte b = utf8[i];
            if (!IsLower(b) && !(b >= (byte)'0' && b <= (byte)'9') && b != (byte)'_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLower(byte b) => b >= (byte)'a' && b <= (byte)'z';
}
