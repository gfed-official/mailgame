using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// Stage 2 of the content load (ADR-0010 section 2, GDD CD3.3 to CD10): a forward walk over one def file's stage 1 token side
/// table, driven by the def type's <see cref="FieldSpec"/> table. It is non-recursive in the file dimension (one pass, no
/// re-reads); the value reads recurse through the table's nested rows only, so their depth is bounded by the field table
/// (at most 3 levels: a def, a <c>shifts</c> entry, its <c>shares</c> map), never by the input, and stage 1 already caps
/// nesting at <see cref="SyntaxWalker.MaxDepth"/> (32). For each element of the
/// top-level array it looks every key up by ordinal compare and reports:
/// <list type="bullet">
/// <item>CV-GEN-02: an <c>id</c> failing the <see cref="IdLint"/> byte scan, or written with an escape;</item>
/// <item>CV-GEN-03: an unknown key, at the key's own line and column, with the <see cref="FieldSuggester"/> text
/// (<c>$schema</c> and <c>suppress</c> are the only reserved def keys; <c>suppress</c> is parsed into
/// <see cref="ParsedDef.Suppress"/>, never into a field, so it is never stored on the record or hashed);</item>
/// <item>CV-GEN-05: a required field, or a variant-required one, that is absent;</item>
/// <item>CV-GEN-06, 07, 12: a number token the row's unit and range reject (<see cref="FieldSpec.ReadNumber"/>);</item>
/// <item>CV-GEN-08: an enum value that is not exactly one of its set's spellings (byte-exact, no case folding);</item>
/// <item>CV-GEN-12: a list (or id map) with more than <see cref="MaxListLength"/> elements, or a string longer than
/// <see cref="MaxStringBytes"/> UTF-8 bytes, so the canonical encoder's <c>ContentEncodingException</c> is unreachable from
/// loaded content (TD-075(1));</item>
/// <item>CV-GEN-14: a field the variant forbids (<c>grid</c> on a <c>pile</c> container);</item>
/// <item>the row's <see cref="FieldSpec.EmptyListRule"/> for a present but empty list (CV-GRD-03 for
/// <c>allowedCategories: []</c>, so the def never reaches stage 4; TD-063(1)).</item>
/// </list>
/// Every finding is an Error at the offending token (CV-GEN-05: the object's opening brace) and names the def when its id is
/// valid. A def with any finding is <see cref="ParsedDef.Failed"/>. A file that failed stage 1 is not walked. Pure and
/// stateless, so thread-safe; load-time only (allocates).
/// </summary>
/// <example>
/// <code>
/// SyntaxResult syntax = SyntaxWalker.Walk("items/core.jsonc", bytes, TopLevelShape.Array);
/// SchemaResult r = SchemaWalker.Walk(syntax, new SchemaTable(DefType.Item, nameof(ItemDef), ItemDefFieldSpec.Rows));
/// </code>
/// </example>
public static class SchemaWalker
{
    /// <summary>The most elements a list (or entries an id map) may have: the canonical encoding's <c>u16</c> count.</summary>
    public const int MaxListLength = 65_535;

    /// <summary>The longest string in UTF-8 bytes: the canonical encoding's <c>u16</c> length.</summary>
    public const int MaxStringBytes = 65_535;

    /// <summary>The reserved editor-schema key; its value is skipped.</summary>
    public const string SchemaKey = "$schema";

    /// <summary>The reserved suppress key: <c>[{"rule": ..., "why": ...}]</c> (story 003 policy).</summary>
    public const string SuppressKey = "suppress";

    /// <summary>Walks one def file.</summary>
    /// <param name="syntax">The file's stage 1 result.</param>
    /// <param name="table">The def type's table.</param>
    /// <returns>Every def and every finding of the file.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SchemaResult Walk(SyntaxResult syntax, SchemaTable table)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(table);
        // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): a file with any stage 1 Error (a CV-GEN-04 duplicate key included) is not walked at stage 2.
        if (!syntax.Ok || syntax.Tokens.IsEmpty)
        {
            return new SchemaResult(syntax.Path, table.Type, ImmutableArray<ParsedDef>.Empty, syntax.Diagnostics) { SyntaxFailed = true };
        }

        return new Pass(syntax, table).Run();
    }

    private sealed class Pass
    {
        private readonly SyntaxResult _syntax;
        private readonly SchemaTable _table;
        private readonly ImmutableArray<SyntaxToken> _tokens;
        private readonly TextPosition _positions;
        private readonly ImmutableArray<FieldSpec> _suppressRows = ImmutableArray.Create(
            FieldSpec.Text("rule", required: false), FieldSpec.Text("why", required: false));

        private readonly List<ContentDiagnostic> _all = new();
        private readonly List<ContentDiagnostic> _def = new();   // the findings of the def being read; its DefRef is stamped at its end
        private int _i;

        public Pass(SyntaxResult syntax, SchemaTable table)
        {
            _syntax = syntax;
            _table = table;
            _tokens = syntax.Tokens;
            _positions = new TextPosition(syntax.Path, syntax.Bytes.Span);
        }

        private SyntaxToken Tok => _tokens[_i];

        public SchemaResult Run()
        {
            ImmutableArray<ParsedDef>.Builder defs = ImmutableArray.CreateBuilder<ParsedDef>();
            _i = 1;   // past the top-level '['
            while (Tok.Kind != JsonTokenType.EndArray)
            {
                defs.Add(Tok.Kind == JsonTokenType.StartObject ? ReadDef() : NotADef());
            }

            return new SchemaResult(_syntax.Path, _table.Type, defs.ToImmutable(), _syntax.Diagnostics.AddRange(_all));
        }

        // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): an array element that is not an object is CV-GEN-16 (shape) at stage 2.
        private ParsedDef NotADef()
        {
            SyntaxToken t = Tok;
            Report(GeneralRuleIds.SyntaxShape, t, Describe(t), "a def object");
            SkipValue();
            FinishDef(null);
            return new ParsedDef(_table.Type, null, Loc(t), ImmutableArray<ParsedField>.Empty, ImmutableArray<SuppressEntry>.Empty, Failed: true);
        }

        private ParsedDef ReadDef()
        {
            SyntaxToken open = Tok;
            _i++;
            ImmutableArray<SuppressEntry>.Builder suppress = ImmutableArray.CreateBuilder<SuppressEntry>();
            ImmutableArray<ParsedField> fields = ReadObject(_table.Rows, open, suppress);
            string? id = IdOf(fields);
            bool failed = FinishDef(id);
            return new ParsedDef(_table.Type, id, Loc(open), fields, suppress.ToImmutable(), failed);
        }

        private static string? IdOf(ImmutableArray<ParsedField> fields)
        {
            foreach (ParsedField f in fields)
            {
                if (string.Equals(f.Name, FieldSpec.IdName, StringComparison.Ordinal))
                {
                    return f.Value.Text;
                }
            }

            return null;
        }

        /// <summary>Stamps the def's findings with its ref and moves them to the file's list; true when any is an Error.</summary>
        private bool FinishDef(string? id)
        {
            bool failed = false;
            foreach (ContentDiagnostic d in _def)
            {
                failed |= d.Severity == RuleSeverity.Error;
                _all.Add(id is null ? d : d with { Def = new DefRef(_table.Type, id) });
            }

            _def.Clear();
            return failed;
        }

        /// <summary>
        /// Reads the keys of an object whose '{' was just consumed, through its '}'. <paramref name="suppress"/> is non-null for a
        /// def object, where <c>$schema</c>, <c>suppress</c> and the <c>id</c> lint apply.
        /// </summary>
        private ImmutableArray<ParsedField> ReadObject(
            ImmutableArray<FieldSpec> rows, SyntaxToken open, ImmutableArray<SuppressEntry>.Builder? suppress)
        {
            var byRow = new ParsedField?[rows.Length];
            var keyAt = new SyntaxToken?[rows.Length];
            while (Tok.Kind == JsonTokenType.PropertyName)
            {
                SyntaxToken key = Tok;
                _i++;
                string name = KeyText(key);
                if (suppress is not null && ReadReserved(name, suppress))
                {
                    continue;
                }

                int row = IndexOf(rows, name);
                if (row < 0)
                {
                    Report(GeneralRuleIds.UnknownField, key, name, FieldSuggester.Allowed(rows, name));
                    SkipValue();
                    continue;
                }

                keyAt[row] = key;
                bool isId = suppress is not null && string.Equals(name, FieldSpec.IdName, StringComparison.Ordinal);
                ParsedValue? v = isId ? ReadId() : ReadValue(rows[row], rows[row].Kind);
                if (v is not null)
                {
                    byRow[row] = new ParsedField(row, name, Loc(key), v);
                }
            }

            _i++;   // past '}'
            CheckPresence(rows, byRow, keyAt, open);
            return Compact(byRow);
        }

        private bool ReadReserved(string name, ImmutableArray<SuppressEntry>.Builder suppress)
        {
            if (string.Equals(name, SchemaKey, StringComparison.Ordinal))
            {
                SkipValue();
                return true;
            }

            if (string.Equals(name, SuppressKey, StringComparison.Ordinal))
            {
                ReadSuppress(suppress);
                return true;
            }

            return false;
        }

        private void ReadSuppress(ImmutableArray<SuppressEntry>.Builder suppress)
        {
            if (!Expect(JsonTokenType.StartArray, "a list of {\"rule\", \"why\"} objects", GeneralRuleIds.SyntaxShape))
            {
                return;
            }

            _i++;
            while (Tok.Kind != JsonTokenType.EndArray)
            {
                if (!Expect(JsonTokenType.StartObject, "a {\"rule\", \"why\"} object", GeneralRuleIds.SyntaxShape))
                {
                    continue;
                }

                SyntaxToken open = Tok;
                _i++;
                ImmutableArray<ParsedField> f = ReadObject(_suppressRows, open, null);
                suppress.Add(new SuppressEntry(TextOf(f, 0), TextOf(f, 1), Loc(open)));
            }

            _i++;
        }

        private static string? TextOf(ImmutableArray<ParsedField> fields, int row)
        {
            foreach (ParsedField f in fields)
            {
                if (f.Row == row)
                {
                    return f.Value.Text;
                }
            }

            return null;
        }

        private void CheckPresence(ImmutableArray<FieldSpec> rows, ParsedField?[] byRow, SyntaxToken?[] keyAt, SyntaxToken open)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                FieldSpec row = rows[r];
                bool present = keyAt[r] is not null;
                if (row.Required && !present)
                {
                    Report(GeneralRuleIds.RequiredFieldMissing, open, row.Name, "the required field '" + row.Name + "'");
                }

                if (row.Variant is FieldVariant v && Discriminator(rows, byRow, v.On) is string on)
                {
                    CheckVariant(row, v, on, keyAt[r], open);
                }
            }
        }

        private void CheckVariant(FieldSpec row, FieldVariant v, string on, SyntaxToken? key, SyntaxToken open)
        {
            if (key is SyntaxToken at && v.ForbiddenFor.Contains(on, StringComparer.Ordinal))
            {
                Report(
                    GeneralRuleIds.VariantField, at, row.Name + " on " + v.On + " '" + on + "'",
                    "no " + row.Name + " when " + v.On + " is " + string.Join(" or ", v.ForbiddenFor));
            }
            else if (key is null && v.RequiredFor.Contains(on, StringComparer.Ordinal))
            {
                Report(GeneralRuleIds.RequiredFieldMissing, open, row.Name, "the field '" + row.Name + "' when " + v.On + " is '" + on + "'");
            }
        }

        /// <summary>The spelling of the sibling enum <paramref name="name"/>, or null when it is absent or invalid (no variant check then).</summary>
        private static string? Discriminator(ImmutableArray<FieldSpec> rows, ParsedField?[] byRow, string name)
        {
            int row = IndexOf(rows, name);
            return row >= 0 && byRow[row] is ParsedField f && f.Value.Kind == FieldKind.Enum ? f.Value.Text : null;
        }

        private static ImmutableArray<ParsedField> Compact(ParsedField?[] byRow)
        {
            ImmutableArray<ParsedField>.Builder b = ImmutableArray.CreateBuilder<ParsedField>();
            foreach (ParsedField? f in byRow)
            {
                if (f is not null)
                {
                    b.Add(f);
                }
            }

            return b.ToImmutable();
        }

        // ------------------------------------------------------------ values

        /// <summary>Reads one value as <paramref name="kind"/> under <paramref name="row"/>; null when it raised a finding.</summary>
        private ParsedValue? ReadValue(FieldSpec row, FieldKind kind)
        {
            int before = _def.Count;
            ParsedValue? v = kind switch
            {
                FieldKind.Number => ReadNumber(row),
                FieldKind.Text => ReadText(),
                FieldKind.Bool => ReadBool(),
                FieldKind.Enum => ReadEnum(row),
                FieldKind.Reference => ReadReference(),
                FieldKind.Object => ReadNested(row),
                FieldKind.RefMap => ReadMap(row),
                _ => ReadList(row),
            };
            return _def.Count == before ? v : null;
        }

        private ParsedValue? ReadId()
        {
            SyntaxToken t = Tok;
            if (t.Kind != JsonTokenType.String)
            {
                Report(GeneralRuleIds.IdFormat, t, Describe(t), IdLint.Allowed);
                SkipValue();
                return null;
            }

            _i++;
            ReadOnlySpan<byte> inner = Inner(t);
            string text = Encoding.UTF8.GetString(inner);
            if (inner.IndexOf((byte)'\\') >= 0)
            {
                Report(GeneralRuleIds.IdFormat, t, text, IdLint.Allowed + " with no escape sequence");
                return null;
            }

            if (!IdLint.IsValid(inner))
            {
                Report(GeneralRuleIds.IdFormat, t, "'" + text + "'", IdLint.Allowed);
                return null;
            }

            return ParsedValue.FromText(text);
        }

        private ParsedValue? ReadNumber(FieldSpec row)
        {
            if (!Expect(JsonTokenType.Number, "a number", GeneralRuleIds.NumberFormat))
            {
                return null;
            }

            SyntaxToken t = Tok;
            _i++;
            ScaledStatus status = row.ReadNumber(_syntax.Raw(t), out long value);
            if (status != ScaledStatus.Ok)
            {
                Report(DecimalToScaled.RuleIdOf(status)!, t, Encoding.UTF8.GetString(_syntax.Raw(t)), NumberAllowed(row, status));
                return null;
            }

            return ParsedValue.Number(value);
        }

        private static string NumberAllowed(FieldSpec row, ScaledStatus status) => status switch
        {
            ScaledStatus.Format => FieldUnits.IsDecimal(row.Unit)
                ? "-?(0|[1-9][0-9]*)(.[0-9]{1,3})?, no exponent, no -0"
                : "an integer -?(0|[1-9][0-9]*), no fraction, no exponent, no -0",
            ScaledStatus.Duration => "a duration of 0 to 86400 s, within the field's range",
            _ => "a stored value in [" + row.Min.ToString(CultureInfo.InvariantCulture) + ", " + row.Max.ToString(CultureInfo.InvariantCulture) + "]",
        };

        private ParsedValue? ReadText()
        {
            if (!Expect(JsonTokenType.String, "a string", GeneralRuleIds.SyntaxShape) || !TakeString(out string text, out SyntaxToken t))
            {
                return null;
            }

            int bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes > MaxStringBytes)
            {
                Report(GeneralRuleIds.ValueOutOfRange, t, bytes.ToString(CultureInfo.InvariantCulture) + " UTF-8 bytes", "a string of at most 65535 UTF-8 bytes");
                return null;
            }

            return ParsedValue.FromText(text);
        }

        private ParsedValue? ReadBool()
        {
            SyntaxToken t = Tok;
            if (t.Kind is not (JsonTokenType.True or JsonTokenType.False))
            {
                Report(GeneralRuleIds.SyntaxShape, t, Describe(t), "true or false");
                SkipValue();
                return null;
            }

            _i++;
            return ParsedValue.Bool(t.Kind == JsonTokenType.True);
        }

        private ParsedValue? ReadEnum(FieldSpec row)
        {
            string set = row.EnumSet ?? string.Empty;
            if (!Expect(JsonTokenType.String, EnumSets.Spellings(set), GeneralRuleIds.EnumSpelling) || !TakeString(out string text, out SyntaxToken t))
            {
                return null;
            }

            if (!EnumSets.TryParse(set, Encoding.UTF8.GetBytes(text), out int value))
            {
                Report(GeneralRuleIds.EnumSpelling, t, "'" + text + "'", EnumSets.Spellings(set));
                return null;
            }

            return ParsedValue.Enum(value, text);
        }

        private ParsedValue? ReadReference() =>
            Expect(JsonTokenType.String, "a def id", GeneralRuleIds.SyntaxShape) && TakeString(out string id, out _)
                ? ParsedValue.Reference(id)
                : null;

        private ParsedValue? ReadNested(FieldSpec row)
        {
            if (!Expect(JsonTokenType.StartObject, "an object", GeneralRuleIds.SyntaxShape))
            {
                return null;
            }

            SyntaxToken open = Tok;
            _i++;
            return ParsedValue.Object(ReadObject(row.Children, open, null));
        }

        private ParsedValue? ReadMap(FieldSpec row)
        {
            if (!Expect(JsonTokenType.StartObject, "an object of def id to number", GeneralRuleIds.SyntaxShape))
            {
                return null;
            }

            SyntaxToken open = Tok;
            _i++;
            ImmutableArray<ParsedEntry>.Builder entries = ImmutableArray.CreateBuilder<ParsedEntry>();
            int count = 0;
            while (Tok.Kind == JsonTokenType.PropertyName)
            {
                SyntaxToken key = Tok;
                _i++;
                count++;
                if (ReadValue(row, FieldKind.Number) is ParsedValue v)
                {
                    entries.Add(new ParsedEntry(KeyText(key), Loc(key), v.Integer));
                }
            }

            _i++;
            CheckLength(row, open, count);
            return ParsedValue.Map(entries.ToImmutable());
        }

        private ParsedValue? ReadList(FieldSpec row)
        {
            if (!Expect(JsonTokenType.StartArray, "a list", GeneralRuleIds.SyntaxShape))
            {
                return null;
            }

            SyntaxToken open = Tok;
            _i++;
            FieldKind element = ElementKind(row.Kind);
            ImmutableArray<ParsedValue>.Builder items = ImmutableArray.CreateBuilder<ParsedValue>();
            int count = 0;
            while (Tok.Kind != JsonTokenType.EndArray)
            {
                count++;
                if (ReadValue(row, element) is ParsedValue v)
                {
                    items.Add(v);
                }
            }

            _i++;
            CheckLength(row, open, count);
            return ParsedValue.List(row.Kind, items.ToImmutable());
        }

        private static FieldKind ElementKind(FieldKind list) => list switch
        {
            FieldKind.IntList => FieldKind.Number,
            FieldKind.TextList => FieldKind.Text,
            FieldKind.EnumList => FieldKind.Enum,
            FieldKind.RefList => FieldKind.Reference,
            _ => FieldKind.Object,
        };

        private void CheckLength(FieldSpec row, SyntaxToken open, int count)
        {
            string n = count.ToString(CultureInfo.InvariantCulture) + " elements";
            if (count > MaxListLength)
            {
                Report(GeneralRuleIds.ValueOutOfRange, open, n, "at most 65535 elements");
            }
            else if (row.FixedLength > 0 && count != row.FixedLength)
            {
                // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): a fixed-length list (grid [w, h]) of another length is CV-GEN-12.
                Report(GeneralRuleIds.ValueOutOfRange, open, n, "exactly " + row.FixedLength.ToString(CultureInfo.InvariantCulture) + " elements");
            }
            else if (count == 0 && row.EmptyListRule is string rule)
            {
                Report(rule, open, row.Name + ": []", "omit " + row.Name + " (any) or list at least one value");
            }
        }

        // ------------------------------------------------------------ tokens

        /// <summary>True when the current token is <paramref name="kind"/>; otherwise reports <paramref name="ruleId"/> and skips the value.</summary>
        private bool Expect(JsonTokenType kind, string allowed, string ruleId)
        {
            if (Tok.Kind == kind)
            {
                return true;
            }

            // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): a value of the wrong JSON kind is reported by the row kind's own rule where one exists
            // (CV-GEN-06 for a number, CV-GEN-08 for an enum), else as CV-GEN-16 (shape).
            Report(ruleId, Tok, Describe(Tok), allowed);
            SkipValue();
            return false;
        }

        /// <summary>Consumes the current string token and unescapes it; false (reported) for an unpaired surrogate escape.</summary>
        private bool TakeString(out string text, out SyntaxToken t)
        {
            t = Tok;
            _i++;
            if (TryUnescape(t, out text))
            {
                return true;
            }

            // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): an unpaired surrogate escape in a string value is CV-GEN-16, as stage 1 treats one in a key.
            Report(GeneralRuleIds.SyntaxShape, t, "unpaired surrogate escape", "valid JSON (comments and trailing commas allowed)");
            return false;
        }

        private bool TryUnescape(SyntaxToken t, out string text)
        {
            ReadOnlySpan<byte> inner = Inner(t);
            if (inner.IndexOf((byte)'\\') < 0)
            {
                text = Encoding.UTF8.GetString(inner);
                return true;
            }

            var reader = new Utf8JsonReader(_syntax.Raw(t));
            reader.Read();
            try
            {
                text = reader.GetString() ?? string.Empty;
                return true;
            }
            catch (InvalidOperationException)
            {
                text = string.Empty;
                return false;
            }
        }

        // Stage 1 already rejected a key with an unpaired surrogate escape, so a key always unescapes.
        private string KeyText(SyntaxToken key) => TryUnescape(key, out string name) ? name : string.Empty;

        private ReadOnlySpan<byte> Inner(SyntaxToken t) => _syntax.Raw(t).Slice(1, t.Length - 2);

        private void SkipValue()
        {
            int depth = 0;
            do
            {
                JsonTokenType k = Tok.Kind;
                if (k is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    depth++;
                }
                else if (k is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    depth--;
                }

                _i++;
            }
            while (depth > 0);
        }

        private static int IndexOf(ImmutableArray<FieldSpec> rows, string name)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                if (string.Equals(rows[r].Name, name, StringComparison.Ordinal))
                {
                    return r;
                }
            }

            return -1;
        }

        private SourceLocation Loc(SyntaxToken t) => _positions.At(t.Start);

        private void Report(string ruleId, SyntaxToken at, string value, string allowed) =>
            _def.Add(new ContentDiagnostic(ruleId, RuleSeverity.Error, Loc(at), DefRef.None, value, allowed));

        private static string Describe(SyntaxToken t) => t.Kind switch
        {
            JsonTokenType.StartObject => "object",
            JsonTokenType.StartArray => "array",
            JsonTokenType.String => "string",
            JsonTokenType.Number => "number",
            JsonTokenType.True or JsonTokenType.False => "boolean",
            JsonTokenType.Null => "null",
            _ => "token",
        };
    }
}
