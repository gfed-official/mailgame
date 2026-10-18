namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The verdict of a <see cref="DecimalToScaled"/> read. The caller attaches the position and the rule (see
/// <see cref="DecimalToScaled.RuleIdOf"/>). Closed enum with explicit values; never renumbered or reused.
/// </summary>
/// <example>
/// <code>
/// ScaledStatus s = DecimalToScaled.Read("0.05"u8, FieldUnit.Ratio, out long pm);   // ScaledStatus.Ok, pm == 50
/// </code>
/// </example>
public enum ScaledStatus : byte
{
    /// <summary>The token is valid and in range.</summary>
    Ok = 0,

    /// <summary>The token is not in the grammar (CV-GEN-06).</summary>
    Format = 1,

    /// <summary>The value is outside the field range (CV-GEN-12).</summary>
    Range = 2,

    /// <summary>The value is outside the duration cap of 0 to 86 400 000 ms (CV-GEN-07).</summary>
    Duration = 3,
}
