namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// Names one def for diagnostics, <c>suppress</c> matching and skip accounting: its type and its string id.
/// Equality is ordinal (type, then id). <see cref="None"/> (the default) means "no particular def", used for
/// whole-set and file-level findings.
/// </summary>
/// <example>
/// <code>
/// var letter = new DefRef(DefType.MailKind, "letter");
/// bool same = letter == new DefRef(DefType.MailKind, "letter");   // true
/// </code>
/// </example>
public readonly record struct DefRef
{
    // Stored null for "no id" so default(DefRef) == new DefRef(0, "") == new DefRef(0, null!).
    private readonly string? _id;

    /// <summary>Creates a def address. A null or empty <paramref name="id"/> is normalised, so every "none" value is equal under <c>==</c>.</summary>
    /// <param name="type">The def type (0 for none).</param>
    /// <param name="id">The def's string id.</param>
    public DefRef(DefType type, string id)
    {
        Type = type;
        _id = string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>The def type; 0 for <see cref="None"/>.</summary>
    public DefType Type { get; }

    /// <summary>The def's string id; empty for <see cref="None"/>, never null.</summary>
    public string Id => _id ?? string.Empty;

    /// <summary>No def: whole-set or file-level.</summary>
    public static DefRef None => default;

    /// <summary>True for <see cref="None"/> (no type, no id).</summary>
    public bool IsNone => Type == 0 && _id is null;

    /// <summary>Orders by def type, then id (ordinal); the order used wherever diagnostics or skips tie-break on a def.</summary>
    /// <param name="a">The first def.</param>
    /// <param name="b">The second def.</param>
    public static int Compare(DefRef a, DefRef b)
    {
        int c = Comparer<DefType>.Default.Compare(a.Type, b.Type);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>The text used in messages, for example <c>MailKind:letter</c> or <c>(set)</c> for <see cref="None"/>.</summary>
    public override string ToString() => IsNone ? "(set)" : TypeName(Type) + ":" + Id;

    // Hand-written so no reflection-based Enum formatting is involved (ADR-0010 section 2 spirit).
    private static string TypeName(DefType type) => type switch
    {
        DefType.Item => "Item",
        DefType.Container => "Container",
        DefType.MailKind => "MailKind",
        DefType.DestinationType => "DestinationType",
        DefType.MailMix => "MailMix",
        _ => "DefType#" + ((int)type).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
