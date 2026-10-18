namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// What a <see cref="FieldSpecTable.SelfCheck"/> finding is about, so tests (and a startup message) can tell two findings that
/// share a rule id apart. Closed enum with explicit values; append-only.
/// </summary>
/// <example>
/// <code>
/// FieldSpecIssue issue = FieldSpecTable.SelfCheck(rows)[0];
/// bool badDuration = issue.Kind == FieldSpecIssueKind.DurationUnit;
/// </code>
/// </example>
public enum FieldSpecIssueKind : byte
{
    /// <summary>A numeric row declares no <see cref="FieldUnit"/> (CV-GEN-06).</summary>
    MissingUnit = 1,

    /// <summary>An <see cref="FieldSpec.IsDuration"/> row has a unit other than Seconds or Ms (CV-GEN-06).</summary>
    DurationUnit = 2,

    /// <summary><see cref="FieldSpec.Min"/> is above <see cref="FieldSpec.Max"/> (CV-GEN-12).</summary>
    RangeInverted = 3,

    /// <summary>The numeric default lies outside [Min, Max] (CV-GEN-12).</summary>
    DefaultOutOfRange = 4,

    /// <summary>An enum or enum-list row names no enum set (CV-GEN-08).</summary>
    MissingEnumSet = 5,

    /// <summary>A reference, reference-list or reference-map row names no target def type (CV-GEN-01).</summary>
    MissingRefTarget = 6,

    /// <summary>A required row also carries a default (CV-GEN-05).</summary>
    DefaultOnRequired = 7,

    /// <summary>An object or object-list row has no child rows (CV-GEN-03).</summary>
    MissingChildren = 8,

    /// <summary>Two rows of one table share a name (CV-GEN-04).</summary>
    DuplicateName = 9,

    /// <summary>A variant rule names a discriminator that is not an enum row of the table, or sits on a required row (CV-GEN-14).</summary>
    VariantTarget = 10,

    /// <summary>A fixed length is set on a row that is not a list, or is negative (CV-GEN-12).</summary>
    FixedLengthMisuse = 11,
}
