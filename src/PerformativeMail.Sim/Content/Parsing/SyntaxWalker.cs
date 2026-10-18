using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// Stage 1 of the content load (ADR-0010 section 2, GDD CD3): the hand-written forward <see cref="Utf8JsonReader"/> walk over
/// one file's bytes. It checks the bytes are well-formed UTF-8, strips a BOM at offset 0, checks the JSON is valid (comments
/// skipped, trailing commas allowed, nesting at most <see cref="MaxDepth"/>) and starts with the expected top-level value,
/// reports every repeated key in an object (CV-GEN-04) and records each token's offset in a side table. A syntax error
/// (CV-GEN-16) is fail-fast: one per file, and it ends that file only. Stage 2 (unknown fields, required fields, enums, ids,
/// units) is not here. Positions are byte offsets in the ORIGINAL file (BOM counted), converted to
/// <c>path:line:column</c> with <see cref="TextPosition"/>: the position of a CV-GEN-04 is the duplicate key's opening
/// quote; the position of a CV-GEN-16 is the first offending byte (the reader's failing position), the opening bracket
/// that would be nesting level 33, the first byte of a BOM or invalid UTF-8 sequence, the opening quote of a key with an
/// unpaired surrogate escape, or the end of the file when no JSON value exists. String VALUES are not unescaped by stage 1,
/// so an unpaired surrogate escape in a value is accepted here. Pure and stateless, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// SyntaxResult r = SyntaxWalker.Walk("defs/items.jsonc", bytes, TopLevelShape.Array);
/// foreach (ContentDiagnostic d in r.Diagnostics) { Console.WriteLine(d.Location + " " + d.RuleId); }
/// if (r.Ok) { /* hand r.Tokens and r.Bytes to the schema walk */ }
/// </code>
/// </example>
public static class SyntaxWalker
{
    /// <summary>The deepest nesting a content file may have (the reader's <c>MaxDepth</c>).</summary>
    public const int MaxDepth = 32;

    private const string ValidJson = "valid JSON (comments and trailing commas allowed)";
    private const string AnyValue = "one JSON value at the top level";

    /// <summary>Walks one file.</summary>
    /// <param name="path">The content-relative path, put into every location.</param>
    /// <param name="file">The file's original bytes (a leading BOM is allowed).</param>
    /// <param name="expected">The top-level value the file must have: <see cref="TopLevelShape.Array"/> for a def file, <see cref="TopLevelShape.Object"/> for a balance file.</param>
    /// <returns>The findings and the token side table of the file.</returns>
    public static SyntaxResult Walk(string path, ReadOnlyMemory<byte> file, TopLevelShape expected)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new Pass(path, file, expected).Run();
    }

    /// <summary>
    /// The CV-GEN-10 finding for a path the manifest lists but the source cannot read. It has no line of its own, so it
    /// points at <c>1:1</c> of the missing file.
    /// </summary>
    /// <param name="path">The listed path.</param>
    /// <returns>The CV-GEN-10 diagnostic.</returns>
    public static ContentDiagnostic MissingFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new ContentDiagnostic(
            GeneralRuleIds.ManifestFile, RuleSeverity.Error, new SourceLocation(path, 1, 1), DefRef.None, path, "listed file present");
    }

    private sealed class Pass
    {
        private readonly string _path;
        private readonly ReadOnlyMemory<byte> _file;
        private readonly TopLevelShape _expected;
        private readonly List<ContentDiagnostic> _diagnostics = new();
        private readonly List<SyntaxToken> _tokens = new();
        private readonly KeyTracker _keys = new();
        private readonly (int KeyBase, int PoolBase)[] _frames = new (int KeyBase, int PoolBase)[MaxDepth + 1];
        private int _depth;
        private TextPosition? _positions;

        public Pass(string path, ReadOnlyMemory<byte> file, TopLevelShape expected)
        {
            _path = path;
            _file = file;
            _expected = expected;
        }

        public SyntaxResult Run()
        {
            ReadOnlySpan<byte> all = _file.Span;
            int bom = all.Length >= 3 && all[0] == 0xEF && all[1] == 0xBB && all[2] == 0xBF ? 3 : 0;
            bool fatal = !CheckUtf8(all, bom) || !WalkTokens(all, bom);
            return new SyntaxResult(
                _path,
                ImmutableArray.CreateRange(_diagnostics),
                fatal ? ImmutableArray<SyntaxToken>.Empty : ImmutableArray.CreateRange(_tokens),
                _file,
                bom);
        }

        private bool CheckUtf8(ReadOnlySpan<byte> all, int bom)
        {
            int bad = Utf8Validator.FindProblem(all, bom, out bool isBom);
            if (bad < 0)
            {
                return true;
            }

            if (isBom)
            {
                Fail(bad, "U+FEFF", "a byte order mark only at the start of the file", all);
            }
            else
            {
                Fail(bad, "byte 0x" + all[bad].ToString("X2", CultureInfo.InvariantCulture), "well-formed UTF-8", all);
            }

            return false;
        }

        private bool WalkTokens(ReadOnlySpan<byte> all, int bom)
        {
            var options = new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = MaxDepth,
            };
            var reader = new Utf8JsonReader(all.Slice(bom), options);
            try
            {
                if (!ReadAll(ref reader, all, bom))
                {
                    return false;
                }
            }
            catch (JsonException ex)
            {
                ReportJsonException(ex, all, bom, reader.BytesConsumed);
                return false;
            }

            if (_tokens.Count == 0)
            {
                Fail(all.Length, "end of file", AnyValue, all);
                return false;
            }

            if (_depth != 0)
            {
                Fail(all.Length, "end of file", "valid JSON", all);
                return false;
            }

            return true;
        }

        private bool ReadAll(ref Utf8JsonReader reader, ReadOnlySpan<byte> all, int bom)
        {
            while (reader.Read())
            {
                if (!Accept(ref reader, bom + (int)reader.TokenStartIndex, all))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Records the current token; false when it ends the file's walk (wrong top-level shape, bad key escape).</summary>
        private bool Accept(ref Utf8JsonReader reader, int start, ReadOnlySpan<byte> all)
        {
            JsonTokenType kind = reader.TokenType;
            if (_tokens.Count == 0 && !MatchesShape(kind))
            {
                Fail(start, DescribeKind(kind), _expected == TopLevelShape.Array ? "a top-level array (def file)" : "a top-level object (balance file)", all);
                return false;
            }

            _tokens.Add(new SyntaxToken(kind, start, TokenLength(ref reader)));
            if (kind == JsonTokenType.PropertyName)
            {
                return CheckKey(ref reader, start, all);
            }

            TrackNesting(kind);
            return true;
        }

        private void TrackNesting(JsonTokenType kind)
        {
            switch (kind)
            {
                case JsonTokenType.StartObject:
                    _frames[_depth++] = (_keys.KeyCount, _keys.PoolUsed);
                    break;
                case JsonTokenType.StartArray:
                    _frames[_depth++] = (0, 0);
                    break;
                case JsonTokenType.EndObject:
                    (int keyBase, int poolBase) = _frames[--_depth];
                    _keys.Truncate(keyBase, poolBase);
                    break;
                case JsonTokenType.EndArray:
                    _depth--;
                    break;
            }
        }

        /// <summary>Checks a property name against the keys of its object; false after an unpaired surrogate escape (CV-GEN-16).</summary>
        private bool CheckKey(ref Utf8JsonReader reader, int start, ReadOnlySpan<byte> all)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            if (name.IndexOf((byte)'\\') >= 0)
            {
                // Escaped names compare by their unescaped bytes, so "a" and "a" are the same key.
                byte[] buffer = new byte[name.Length];
                if (!TryUnescape(ref reader, buffer, out int written))
                {
                    Fail(start, "unpaired surrogate escape in key", ValidJson, all);
                    return false;
                }

                name = buffer.AsSpan(0, written);
            }

            if (!_keys.TryAdd(name, _frames[_depth - 1].KeyBase))
            {
                _diagnostics.Add(new ContentDiagnostic(
                    GeneralRuleIds.DuplicateKey, RuleSeverity.Error, Positions(all).At(start), DefRef.None,
                    Encoding.UTF8.GetString(name), "each key once per object"));
            }

            return true;
        }

        /// <summary>The reader accepts a lone surrogate escape in a name but cannot unescape it (it throws InvalidOperationException).</summary>
        private static bool TryUnescape(ref Utf8JsonReader reader, byte[] buffer, out int written)
        {
            try
            {
                written = reader.CopyString(buffer);
                return true;
            }
            catch (InvalidOperationException)
            {
                written = 0;
                return false;
            }
        }

        private void ReportJsonException(JsonException ex, ReadOnlySpan<byte> all, int bom, long consumed)
        {
            if (_tokens.Count == 0 && JsonTrivia.Skip(all, bom, skipSeparators: false) >= all.Length)
            {
                Fail(all.Length, "end of file", AnyValue, all);
                return;
            }

            int pos = ErrorOffset(ex, all, bom, consumed);
            if (_depth == MaxDepth && IsOpeningBracketAt(all, JsonTrivia.Skip(all, bom + (int)consumed, skipSeparators: true), pos))
            {
                Fail(pos, "depth " + (MaxDepth + 1).ToString(CultureInfo.InvariantCulture), "at most " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels of nesting", all);
                return;
            }

            Fail(pos, Describe(all, pos), ValidJson, all);
        }

        /// <summary>
        /// A depth failure is only reported as nesting when the reader failed exactly at the next value's opening bracket, so a
        /// stray comma before that bracket stays an ordinary syntax error.
        /// </summary>
        private static bool IsOpeningBracketAt(ReadOnlySpan<byte> all, int nextValue, int failedAt) =>
            nextValue == failedAt && failedAt < all.Length && all[failedAt] is (byte)'{' or (byte)'[';

        /// <summary>The failing byte in original-file offsets, from the reader's own line and in-line position (else from BytesConsumed).</summary>
        private int ErrorOffset(JsonException ex, ReadOnlySpan<byte> all, int bom, long consumed)
        {
            if (ex.LineNumber is long line && ex.BytePositionInLine is long inLine)
            {
                long lineStart = line > int.MaxValue ? all.Length : Positions(all).LineStart((int)line);
                return (int)Math.Min(all.Length, lineStart + (line == 0 ? bom : 0) + inLine);
            }

            return (int)Math.Min(all.Length, bom + consumed);
        }

        private void Fail(int offset, string value, string allowed, ReadOnlySpan<byte> all) =>
            _diagnostics.Add(new ContentDiagnostic(
                GeneralRuleIds.SyntaxShape, RuleSeverity.Error, Positions(all).At(offset), DefRef.None, value, allowed));

        private TextPosition Positions(ReadOnlySpan<byte> all) => _positions ??= new TextPosition(_path, all);

        private bool MatchesShape(JsonTokenType kind) =>
            _expected == TopLevelShape.Array ? kind == JsonTokenType.StartArray : kind == JsonTokenType.StartObject;

        private static int TokenLength(ref Utf8JsonReader reader) => reader.TokenType switch
        {
            JsonTokenType.String or JsonTokenType.PropertyName => reader.ValueSpan.Length + 2,
            JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null => reader.ValueSpan.Length,
            _ => 1,
        };

        private static string DescribeKind(JsonTokenType kind) => kind switch
        {
            JsonTokenType.StartObject => "object",
            JsonTokenType.StartArray => "array",
            JsonTokenType.String => "string",
            JsonTokenType.Number => "number",
            JsonTokenType.True or JsonTokenType.False => "boolean",
            JsonTokenType.Null => "null",
            _ => "token",
        };

        private static string Describe(ReadOnlySpan<byte> all, int pos)
        {
            if (pos >= all.Length)
            {
                return "end of file";
            }

            byte b = all[pos];
            return b is >= 0x21 and <= 0x7E
                ? "'" + (char)b + "'"
                : "byte 0x" + b.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
