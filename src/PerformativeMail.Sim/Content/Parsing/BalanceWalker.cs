using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>What <see cref="BalanceWalker.Walk"/> read from one balance file: its owner, its entries and its findings.</summary>
/// <param name="Path">The file path (<c>balance/&lt;owner&gt;.jsonc</c>).</param>
/// <param name="Owner">The owner: the file name without <c>balance/</c> and <c>.jsonc</c>.</param>
/// <param name="Entries">One entry per well-formed key, in file order.</param>
/// <param name="Diagnostics">The file's stage 1 findings, then the walk's, in file order.</param>
/// <example>
/// <code>
/// BalanceFileResult r = BalanceWalker.Walk(SyntaxWalker.Walk("balance/sim.jsonc", bytes, TopLevelShape.Object), registry);
/// if (r.Ok) { entries.AddRange(r.Entries); }
/// </code>
/// </example>
public sealed record BalanceFileResult(
    string Path, string Owner, ImmutableArray<BalanceEntry> Entries, ImmutableArray<ContentDiagnostic> Diagnostics)
{
    /// <summary>True when the file passed stage 1 and every key was well formed.</summary>
    public bool Ok
    {
        get
        {
            foreach (ContentDiagnostic d in Diagnostics)
            {
                if (d.Severity == RuleSeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Reads one balance file (GDD CD9, ADR-0010 section 1; TD-178 item 2, Approved by Evan 2026-10-28 7:24 AM PT): a top-level
/// object of <c>key: value</c> or <c>key: { "value": v, "safe": [min, max] }</c>. The owner is the file name, so
/// <c>balance/player.jsonc</c> holds <c>player.*</c> and each entry's <see cref="BalanceEntry.Name"/> is <c>&lt;owner&gt;.&lt;key&gt;</c>
/// (a key only another owner registers is addressed to that owner, so BalanceTable reports it as in the wrong file).
/// Each value (and its safe bounds) is scaled by its registry row's unit through <see cref="DecimalToScaled"/>; the
/// owner, duplicate, registry, hard-range and safe-range checks (CV-GEN-13, CV-GEN-20, CV-GEN-05) are
/// <see cref="BalanceTable.Build"/>'s. A file that failed stage 1 is not walked. Pure and stateless, so thread-safe; load-time
/// only (allocates).
/// </summary>
/// <example>
/// <code>
/// BalanceFileResult r = BalanceWalker.Walk(SyntaxWalker.Walk("balance/sim.jsonc", bytes, TopLevelShape.Object), BalanceKeys.CreateRegistry());
/// </code>
/// </example>
public static class BalanceWalker
{
    /// <summary>The balance directory prefix.</summary>
    public const string Prefix = "balance/";

    private const string Extension = ".jsonc";

    /// <summary>The owner of a balance file path: <c>balance/sim.jsonc</c> gives <c>sim</c>.</summary>
    /// <param name="path">A <c>balance/&lt;owner&gt;.jsonc</c> path.</param>
    /// <example><code>string owner = BalanceWalker.OwnerOf("balance/player.jsonc");   // "player"</code></example>
    public static string OwnerOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int start = path.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix.Length : 0;
        int end = path.EndsWith(Extension, StringComparison.Ordinal) ? path.Length - Extension.Length : path.Length;
        return end > start ? path.Substring(start, end - start) : string.Empty;
    }

    /// <summary>Walks one balance file.</summary>
    /// <param name="file">The file's stage 1 result (shape <see cref="TopLevelShape.Object"/>).</param>
    /// <param name="registry">The key registry whose row units scale the values.</param>
    /// <returns>The entries and findings.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static BalanceFileResult Walk(SyntaxResult file, BalanceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(registry);
        string owner = OwnerOf(file.Path);
        if (!file.Ok || file.Tokens.IsEmpty)
        {
            return new BalanceFileResult(file.Path, owner, ImmutableArray<BalanceEntry>.Empty, file.Diagnostics);
        }

        return new Pass(file, owner, registry).Run();
    }

    /// <summary>
    /// The unit a balance key is authored in (GDD CD4: the stored unit and the key's suffix decide it, so <c>…Seconds</c> is a
    /// decimal stored as ms and <c>…Pm</c> an integer). Approved by Evan 2026-10-28 10:55 PM PT (TD-185 item 1): <c>Ms</c> keys ending
    /// <c>Seconds</c> are authored as decimal seconds, <c>Mm</c> keys ending <c>Metres</c> or <c>Meters</c> as decimal metres,
    /// <c>Permille</c> keys without <c>Pm</c> or <c>Permille</c> in the name as decimal ratios, milli-points as decimal points
    /// and milli-degrees as decimal degrees; every other unit (counts, ticks, cents, mm/s, mm/s², deg/s) as an integer.
    /// </summary>
    /// <param name="row">The key's registry row.</param>
    /// <example><code>FieldUnit u = BalanceWalker.AuthoredUnit(new BalanceRow("mail", "lateValueRatio", BalanceUnit.Permille, 0, 1000));   // Ratio</code></example>
    public static FieldUnit AuthoredUnit(BalanceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string key = row.Key;
        return row.Unit switch
        {
            BalanceUnit.Ms => key.EndsWith("Seconds", StringComparison.Ordinal) ? FieldUnit.Seconds : FieldUnit.Ms,
            BalanceUnit.Mm => key.EndsWith("Metres", StringComparison.Ordinal) || key.EndsWith("Meters", StringComparison.Ordinal)
                ? FieldUnit.Metres
                : FieldUnit.Mm,
            BalanceUnit.Permille => key.Contains("Pm", StringComparison.Ordinal) || key.Contains("Permille", StringComparison.Ordinal)
                ? FieldUnit.Pm
                : FieldUnit.Ratio,
            BalanceUnit.MilliPoints => FieldUnit.Points,
            BalanceUnit.MilliDeg => FieldUnit.Deg,
            BalanceUnit.Ticks => FieldUnit.Ticks,
            BalanceUnit.Cents => FieldUnit.Cents,
            _ => FieldUnit.Count,
        };
    }

    // Approved by Evan 2026-10-28 10:55 PM PT (TD-185 item 2): the balance-file shape findings reuse the def walk's rules: an unknown key
    // inside { value, safe } is CV-GEN-03, a missing "value" CV-GEN-05, a value that is not a number CV-GEN-06, a safe range
    // that is not a [min, max] pair of numbers CV-GEN-16 (shape) or CV-GEN-12 (length, or min above max); "$schema" is skipped.
    private sealed class Pass
    {
        private const string ValueKey = "value";
        private const string SafeKey = "safe";

        private readonly SyntaxResult _syntax;
        private readonly string _owner;
        private readonly BalanceRegistry _registry;
        private readonly ImmutableArray<SyntaxToken> _tokens;
        private readonly TextPosition _positions;
        private readonly ImmutableArray<BalanceEntry>.Builder _entries = ImmutableArray.CreateBuilder<BalanceEntry>();
        private readonly List<ContentDiagnostic> _found = new();
        private int _i;

        public Pass(SyntaxResult syntax, string owner, BalanceRegistry registry)
        {
            _syntax = syntax;
            _owner = owner;
            _registry = registry;
            _tokens = syntax.Tokens;
            _positions = new TextPosition(syntax.Path, syntax.Bytes.Span);
        }

        private SyntaxToken Tok => _tokens[_i];

        public BalanceFileResult Run()
        {
            _i = 1;   // past the top-level '{'
            while (Tok.Kind == JsonTokenType.PropertyName)
            {
                SyntaxToken key = Tok;
                _i++;
                string name = KeyText(key);
                if (string.Equals(name, SchemaWalker.SchemaKey, StringComparison.Ordinal))
                {
                    SkipValue();
                    continue;
                }

                string full = Resolve(name);
                // An unregistered key is still read (as a decimal) so its shape is checked; BalanceTable reports it (CV-GEN-13).
                FieldUnit unit = _registry.TryFind(full, out BalanceKey handle) ? AuthoredUnit(_registry[handle]) : FieldUnit.Ratio;
                ReadEntry(full, key, unit);
            }

            return new BalanceFileResult(_syntax.Path, _owner, _entries.ToImmutable(), _syntax.Diagnostics.AddRange(_found));
        }

        /// <summary>
        /// The full name of <paramref name="key"/>: <c>&lt;owner&gt;.&lt;key&gt;</c>. Approved by Evan 2026-10-28 10:55 PM PT (TD-185 item 6): when
        /// that is unregistered but exactly one other owner registers the key, the entry is addressed to that owner, so a key in
        /// the wrong owner's file is BalanceTable's one CV-GEN-13 ("in &lt;file&gt;, owner file &lt;owner&gt;") and still counts as
        /// supplied (no CV-GEN-05 for it in its own file).
        /// </summary>
        private string Resolve(string key)
        {
            string own = _owner + "." + key;
            if (_registry.TryFind(own, out _))
            {
                return own;
            }

            string? other = null;
            foreach (BalanceRow row in _registry.Rows)
            {
                if (string.Equals(row.Key, key, StringComparison.Ordinal))
                {
                    if (other is not null)
                    {
                        return own;   // ambiguous: left unregistered
                    }

                    other = row.Name;
                }
            }

            return other ?? own;
        }

        private void ReadEntry(string full, SyntaxToken key, FieldUnit unit)
        {
            if (Tok.Kind == JsonTokenType.Number)
            {
                if (ReadNumber(unit, out long plain))
                {
                    _entries.Add(new BalanceEntry(_owner, full, plain, Loc(key)));
                }

                return;
            }

            if (Tok.Kind != JsonTokenType.StartObject)
            {
                Report(GeneralRuleIds.NumberFormat, Tok, Describe(Tok), "a number or { \"value\": n, \"safe\": [min, max] }");
                SkipValue();
                return;
            }

            SyntaxToken open = Tok;
            _i++;
            long? value = null;
            BalanceSafeRange? safe = null;
            bool bad = false;
            while (Tok.Kind == JsonTokenType.PropertyName)
            {
                SyntaxToken member = Tok;
                _i++;
                string name = KeyText(member);
                if (string.Equals(name, ValueKey, StringComparison.Ordinal))
                {
                    bad |= !ReadNumber(unit, out long v);
                    value = v;
                }
                else if (string.Equals(name, SafeKey, StringComparison.Ordinal))
                {
                    safe = ReadSafe(unit);
                    bad |= safe is null;
                }
                else
                {
                    Report(GeneralRuleIds.UnknownField, member, name, "value, safe");
                    SkipValue();
                    bad = true;
                }
            }

            _i++;   // past '}'
            if (value is null)
            {
                Report(GeneralRuleIds.RequiredFieldMissing, open, full + ".value", "the required field 'value'");
            }
            else if (!bad)
            {
                _entries.Add(new BalanceEntry(_owner, full, value.Value, Loc(key), safe));
            }
        }

        private BalanceSafeRange? ReadSafe(FieldUnit unit)
        {
            if (Tok.Kind != JsonTokenType.StartArray)
            {
                Report(GeneralRuleIds.SyntaxShape, Tok, Describe(Tok), "a [min, max] pair");
                SkipValue();
                return null;
            }

            SyntaxToken open = Tok;
            _i++;
            var bounds = new List<long>(2);
            bool bad = false;
            while (Tok.Kind != JsonTokenType.EndArray)
            {
                bad |= !ReadNumber(unit, out long v);
                bounds.Add(v);
            }

            _i++;   // past ']'
            if (bad)
            {
                return null;
            }

            if (bounds.Count != 2)
            {
                Report(GeneralRuleIds.ValueOutOfRange, open, bounds.Count.ToString(CultureInfo.InvariantCulture) + " elements", "exactly 2 elements");
                return null;
            }

            if (bounds[0] > bounds[1])
            {
                Report(GeneralRuleIds.ValueOutOfRange, open, "safe [" + Num(bounds[0]) + ", " + Num(bounds[1]) + "]", "min at most max");
                return null;
            }

            return new BalanceSafeRange(bounds[0], bounds[1]);
        }

        private bool ReadNumber(FieldUnit unit, out long value)
        {
            SyntaxToken t = Tok;
            if (t.Kind != JsonTokenType.Number)
            {
                Report(GeneralRuleIds.NumberFormat, t, Describe(t), "a number");
                SkipValue();
                value = 0;
                return false;
            }

            _i++;
            ScaledStatus status = DecimalToScaled.Read(_syntax.Raw(t), unit, long.MinValue, long.MaxValue, out value);
            if (status == ScaledStatus.Ok)
            {
                return true;
            }

            string allowed = FieldUnits.IsDecimal(unit)
                ? "-?(0|[1-9][0-9]*)(.[0-9]{1,3})?, no exponent, no -0"
                : "an integer -?(0|[1-9][0-9]*), no fraction, no exponent, no -0";
            Report(DecimalToScaled.RuleIdOf(status)!, t, Encoding.UTF8.GetString(_syntax.Raw(t)), allowed);
            return false;
        }

        // Stage 1 already rejected a key with an unpaired surrogate escape, so a key always unescapes.
        private string KeyText(SyntaxToken key)
        {
            ReadOnlySpan<byte> inner = _syntax.Raw(key).Slice(1, key.Length - 2);
            if (inner.IndexOf((byte)'\\') < 0)
            {
                return Encoding.UTF8.GetString(inner);
            }

            var reader = new Utf8JsonReader(_syntax.Raw(key));
            reader.Read();
            return reader.GetString() ?? string.Empty;
        }

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

        private SourceLocation Loc(SyntaxToken t) => _positions.At(t.Start);

        private void Report(string ruleId, SyntaxToken at, string value, string allowed) =>
            _found.Add(new ContentDiagnostic(ruleId, RuleSeverity.Error, Loc(at), DefRef.None, value, allowed));

        private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

        private static string Describe(SyntaxToken t) => t.Kind switch
        {
            JsonTokenType.StartObject => "object",
            JsonTokenType.StartArray => "array",
            JsonTokenType.String => "string",
            JsonTokenType.True or JsonTokenType.False => "boolean",
            JsonTokenType.Null => "null",
            _ => "token",
        };
    }
}
