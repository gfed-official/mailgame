using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// Maps byte offsets in a file's ORIGINAL bytes (BOM included) to <c>path:line:column</c> (ADR-0010 section 2). Lines are
/// counted by LF and start at 1; the column is the 1-based UTF-8 byte offset in the line (tab = 1, CR is a byte of its line,
/// a multi-byte character counts its byte length). It builds a line-start index once, so each lookup is a binary search.
/// Gives the same answers as <see cref="SourceLocation.FromUtf8Offset"/>. Immutable after construction, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// var pos = new TextPosition("items.jsonc", "{\n  \"id\"\n}"u8);
/// SourceLocation at = pos.At(4);   // items.jsonc:2:3
/// </code>
/// </example>
public sealed class TextPosition
{
    private readonly string _path;
    private readonly int[] _lineStarts;
    private readonly int _length;

    /// <summary>Indexes <paramref name="file"/>.</summary>
    /// <param name="path">The content-relative path put into every location.</param>
    /// <param name="file">The file's original bytes.</param>
    public TextPosition(string path, ReadOnlySpan<byte> file)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
        _length = file.Length;
        int lines = 1 + file.Count((byte)'\n');
        _lineStarts = new int[lines];
        int n = 1;
        for (int i = 0; i < file.Length; i++)
        {
            if (file[i] == (byte)'\n')
            {
                _lineStarts[n++] = i + 1;
            }
        }
    }

    /// <summary>The number of lines (a file with no LF has one).</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>The byte offset where the 0-based line <paramref name="lineIndex"/> starts; an index past the last line gives the file length.</summary>
    /// <param name="lineIndex">0-based line index.</param>
    /// <returns>The start offset of that line, or the file length when the index is past the last line.</returns>
    public int LineStart(int lineIndex) =>
        lineIndex < 0 ? 0 : lineIndex >= _lineStarts.Length ? _length : _lineStarts[lineIndex];

    /// <summary>The location of <paramref name="byteOffset"/>; an offset outside the file is clamped to it (end of file is valid).</summary>
    /// <param name="byteOffset">Offset into the original bytes.</param>
    /// <returns>The <c>path:line:column</c> location of that offset.</returns>
    public SourceLocation At(int byteOffset)
    {
        int offset = byteOffset < 0 ? 0 : byteOffset > _length ? _length : byteOffset;
        int lo = 0;
        int hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo + 1) >> 1);
            if (_lineStarts[mid] <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return new SourceLocation(_path, lo + 1, offset - _lineStarts[lo] + 1);
    }
}
