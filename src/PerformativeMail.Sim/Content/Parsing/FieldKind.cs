namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The value kind of a <see cref="FieldSpec"/> row. Closed enum with explicit values; append-only, never renumbered or reused.
/// The list and object kinds (6 to 12) were appended by content-data story 018 for the nested M-1a fields.
/// </summary>
/// <example>
/// <code>
/// FieldSpec grid = FieldSpec.IntList("grid", FieldUnit.Count, fixedLength: 2);   // Kind == FieldKind.IntList
/// </code>
/// </example>
public enum FieldKind : byte
{
    /// <summary>A number; it must declare a <see cref="FieldUnit"/>.</summary>
    Number = 1,

    /// <summary>A string.</summary>
    Text = 2,

    /// <summary>A boolean.</summary>
    Bool = 3,

    /// <summary>A closed-enum spelling; <see cref="FieldSpec.EnumSet"/> names the set.</summary>
    Enum = 4,

    /// <summary>A reference to another def; <see cref="FieldSpec.RefTarget"/> names the def type.</summary>
    Reference = 5,

    /// <summary>An array of numbers sharing the row's unit and range (<c>grid</c>, <c>reservedSlots</c>); <see cref="FieldSpec.FixedLength"/> 0 means any length.</summary>
    IntList = 6,

    /// <summary>An array of strings (<c>tags</c>).</summary>
    TextList = 7,

    /// <summary>An array of closed-enum spellings (<c>allowedCategories</c>); <see cref="FieldSpec.EnumSet"/> names the set.</summary>
    EnumList = 8,

    /// <summary>An array of def references (<c>acceptedBy</c>, <c>accepts</c>); <see cref="FieldSpec.RefTarget"/> names the def type.</summary>
    RefList = 9,

    /// <summary>A nested object whose fields are <see cref="FieldSpec.Children"/> (<c>catchVolume</c>).</summary>
    Object = 10,

    /// <summary>An array of nested objects, each described by <see cref="FieldSpec.Children"/> (<c>shifts</c>).</summary>
    ObjectList = 11,

    /// <summary>
    /// An object keyed by def id (<see cref="FieldSpec.RefTarget"/>) whose values are numbers under the row's unit and range
    /// (<c>shares</c>: kind id to Ratio).
    /// </summary>
    RefMap = 12,
}
