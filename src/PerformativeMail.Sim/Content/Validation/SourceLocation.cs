namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// Where a diagnostic points: <c>path:line:column</c>. Lines are counted by LF and start at 1; the column is the
/// 1-based UTF-8 BYTE offset within the line (a tab counts 1, a CR before LF counts as a column of its line, a
/// multi-byte character counts its byte length). Content built in code (<c>BuiltInContent</c>) has no file and
/// reports <c>0:0</c> (<see cref="BuiltIn"/>). Paths print with <c>/</c> separators relative to the content root; this
/// type does not rewrite them.
/// </summary>
/// <example>
/// <code>
/// var a = new SourceLocation("items.jsonc", 7, 14);        // "items.jsonc:7:14"
/// var b = SourceLocation.BuiltIn("builtin/items/letter");  // "builtin/items/letter:0:0"
/// var c = SourceLocation.FromUtf8Offset("a.jsonc", "{\n  \"id\"\n}"u8, 4);  // line 2, column 3
/// </code>
/// </example>
public readonly record struct SourceLocation
{
    private readonly string? _path;

    /// <summary>Creates a file location.</summary>
    /// <param name="path">The content-relative path, <c>/</c>-separated; never null.</param>
    /// <param name="line">1-based line, or 0 together with column 0 for built-in content.</param>
    /// <param name="column">1-based UTF-8 byte column, or 0 together with line 0 for built-in content.</param>
    /// <exception cref="ArgumentException">Line and column are not both at least 1 or both 0.</exception>
    public SourceLocation(string path, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(path);
        bool both0 = line == 0 && column == 0;
        bool bothPositive = line >= 1 && column >= 1;
        if (!both0 && !bothPositive)
        {
            throw new ArgumentException("Line and column must both be at least 1, or both 0 for built-in content.");
        }

        _path = path.Length == 0 ? null : path;   // null for empty so default(SourceLocation) == new SourceLocation("", 0, 0)
        Line = line;
        Column = column;
    }

    /// <summary>The content-relative path; never null (<c>default</c> reads as empty and equals <c>new SourceLocation("", 0, 0)</c>).</summary>
    public string Path => _path ?? string.Empty;

    /// <summary>The 1-based line, or 0 for built-in content.</summary>
    public int Line { get; }

    /// <summary>The 1-based UTF-8 byte column, or 0 for built-in content.</summary>
    public int Column { get; }

    /// <summary>True when the source is built-in content (line 0, column 0).</summary>
    public bool IsBuiltIn => Line == 0 && Column == 0;

    /// <summary>A location for content built in code: <paramref name="path"/> is a logical address, line and column are 0.</summary>
    /// <param name="path">A logical path such as <c>builtin/mail_kinds/letter</c>.</param>
    public static SourceLocation BuiltIn(string path) => new(path, 0, 0);

    /// <summary>
    /// Converts a byte offset into <paramref name="utf8"/> to a line and column (LF lines, 1-based UTF-8 byte column).
    /// </summary>
    /// <param name="path">The file's content-relative path.</param>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="byteOffset">0 to <c>utf8.Length</c> (the end of file is a valid position).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="byteOffset"/> is outside the buffer.</exception>
    public static SourceLocation FromUtf8Offset(string path, ReadOnlySpan<byte> utf8, int byteOffset)
    {
        if ((uint)byteOffset > (uint)utf8.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        int line = 1;
        int lineStart = 0;
        for (int i = 0; i < byteOffset; i++)
        {
            if (utf8[i] == (byte)'\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new SourceLocation(path, line, byteOffset - lineStart + 1);
    }

    /// <summary>The <c>path:line:column</c> text.</summary>
    public override string ToString() =>
        Path + ":" + Line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
        Column.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
