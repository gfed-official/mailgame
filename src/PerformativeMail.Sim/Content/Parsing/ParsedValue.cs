using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// A stage 2 value, already converted: numbers in stored units, strings unescaped, enums as their numeric member plus the
/// spelling, references as the target id (resolved at stage 3/4), lists, objects and maps as nested values. Which members
/// are set follows <see cref="Kind"/>. Immutable, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// ParsedValue v = ParsedValue.Number(1500);          // beltLengthMetres: 1.5
/// ParsedValue e = ParsedValue.Enum(1, "light");      // weightClass
/// </code>
/// </example>
public sealed record ParsedValue
{
    /// <summary>The row kind the value was read as.</summary>
    public required FieldKind Kind { get; init; }

    /// <summary>The stored number (Number), 0/1 (Bool) or the enum member's numeric value (Enum).</summary>
    public long Integer { get; init; }

    /// <summary>The text (Text), the target id (Reference) or the data spelling (Enum).</summary>
    public string? Text { get; init; }

    /// <summary>The elements of a list kind, in authored order.</summary>
    public ImmutableArray<ParsedValue> Items { get; init; } = ImmutableArray<ParsedValue>.Empty;

    /// <summary>The fields of an <see cref="FieldKind.Object"/> value, in row order.</summary>
    public ImmutableArray<ParsedField> Fields { get; init; } = ImmutableArray<ParsedField>.Empty;

    /// <summary>The entries of a <see cref="FieldKind.RefMap"/> value, in authored order.</summary>
    public ImmutableArray<ParsedEntry> Entries { get; init; } = ImmutableArray<ParsedEntry>.Empty;

    /// <summary>A stored number.</summary>
    /// <param name="value">The value in the row's stored unit.</param>
    /// <example><code>ParsedValue v = ParsedValue.Number(400);</code></example>
    public static ParsedValue Number(long value) => new() { Kind = FieldKind.Number, Integer = value };

    /// <summary>An unescaped string.</summary>
    /// <param name="text">The text.</param>
    /// <example><code>ParsedValue v = ParsedValue.FromText("item.plank");</code></example>
    public static ParsedValue FromText(string text) => new() { Kind = FieldKind.Text, Text = text };

    /// <summary>A boolean (stored 1 or 0).</summary>
    /// <param name="value">The value.</param>
    /// <example><code>ParsedValue v = ParsedValue.Bool(true);</code></example>
    public static ParsedValue Bool(bool value) => new() { Kind = FieldKind.Bool, Integer = value ? 1 : 0 };

    /// <summary>An enum member and its spelling.</summary>
    /// <param name="value">The member's numeric value.</param>
    /// <param name="spelling">The data spelling as written.</param>
    /// <example><code>ParsedValue v = ParsedValue.Enum((int)WeightClass.Light, "light");</code></example>
    public static ParsedValue Enum(int value, string spelling) => new() { Kind = FieldKind.Enum, Integer = value, Text = spelling };

    /// <summary>A reference by target id.</summary>
    /// <param name="id">The target id as written.</param>
    /// <example><code>ParsedValue v = ParsedValue.Reference("house");</code></example>
    public static ParsedValue Reference(string id) => new() { Kind = FieldKind.Reference, Text = id };

    /// <summary>A list of a given kind.</summary>
    /// <param name="kind">The list kind.</param>
    /// <param name="items">The elements.</param>
    /// <example><code>ParsedValue v = ParsedValue.List(FieldKind.IntList, items);</code></example>
    public static ParsedValue List(FieldKind kind, ImmutableArray<ParsedValue> items) => new() { Kind = kind, Items = items };

    /// <summary>A nested object.</summary>
    /// <param name="fields">The fields in row order.</param>
    /// <example><code>ParsedValue v = ParsedValue.Object(fields);</code></example>
    public static ParsedValue Object(ImmutableArray<ParsedField> fields) => new() { Kind = FieldKind.Object, Fields = fields };

    /// <summary>An id-keyed number map.</summary>
    /// <param name="entries">The entries in authored order.</param>
    /// <example><code>ParsedValue v = ParsedValue.Map(entries);</code></example>
    public static ParsedValue Map(ImmutableArray<ParsedEntry> entries) => new() { Kind = FieldKind.RefMap, Entries = entries };
}
