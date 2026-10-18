namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The unit a numeric content field declares in its <see cref="FieldSpec"/> (content-data CD4, TR-content-005/006, ADR-0010
/// section 2). The unit decides how a decimal token is stored: <see cref="Seconds"/> as ms, <see cref="Metres"/> as mm,
/// <see cref="Ratio"/> as permille, <see cref="Points"/> as milli-points, <see cref="Deg"/> as milli-degrees, <see cref="PerSecond"/> as milli-units per second; the integer units
/// are stored as written. Closed enum: values are explicit, 0 is "no unit declared" and is never valid, and a value is never
/// renumbered, reordered or reused (append-only).
/// </summary>
/// <example>
/// <code>
/// long scale = FieldUnits.Scale(FieldUnit.Metres);   // 1000: 1.5 m is stored as 1500 mm
/// </code>
/// </example>
public enum FieldUnit : byte
{
    /// <summary>Whole or decimal seconds (<c>…Seconds</c>), stored as milliseconds.</summary>
    Seconds = 1,

    /// <summary>Metres (<c>…Metres</c>), stored as millimetres.</summary>
    Metres = 2,

    /// <summary>Unitless ratio (<c>…Ratio</c>, and a Mul <c>value</c>), stored as permille.</summary>
    Ratio = 3,

    /// <summary>Points (<c>…Points</c>), stored as milli-points.</summary>
    Points = 4,

    /// <summary>Degrees (<c>…Deg</c>), stored as milli-degrees.</summary>
    Deg = 5,

    /// <summary>Integer milliseconds (<c>…Ms</c>), stored as written.</summary>
    Ms = 6,

    /// <summary>Integer millimetres (<c>…Mm</c>), stored as written.</summary>
    Mm = 7,

    /// <summary>Integer permille (<c>…Pm</c>), stored as written.</summary>
    Pm = 8,

    /// <summary>Integer ticks (<c>…Ticks</c>), stored as written.</summary>
    Ticks = 9,

    /// <summary>Money as integer cents (<c>baseValue</c>, <c>price</c>, <c>…Cents</c>), stored as written.</summary>
    Cents = 10,

    /// <summary>A plain integer count (<c>grid</c>, <c>maxStack</c>), stored as written.</summary>
    Count = 11,

    /// <summary>A per-second rate (<c>insertRate</c>), up to three fraction digits, stored as milli-units per second (x1000).</summary>
    PerSecond = 12,
}

/// <summary>
/// Facts about <see cref="FieldUnit"/> members. Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool decimalOk = FieldUnits.IsDecimal(FieldUnit.Ratio);   // true
/// bool declared  = FieldUnits.IsDeclared(default);          // false
/// </code>
/// </example>
public static class FieldUnits
{
    /// <summary>True when <paramref name="unit"/> is one of the declared members (not 0, not an out-of-range cast).</summary>
    /// <param name="unit">The unit.</param>
    /// <example><code>bool ok = FieldUnits.IsDeclared(FieldUnit.Cents);   // true</code></example>
    public static bool IsDeclared(FieldUnit unit) => unit >= FieldUnit.Seconds && unit <= FieldUnit.PerSecond;

    /// <summary>True for the units whose tokens may carry up to three fraction digits (Seconds, Metres, Ratio, Points, Deg, PerSecond).</summary>
    /// <param name="unit">The unit.</param>
    /// <example><code>bool d = FieldUnits.IsDecimal(FieldUnit.Seconds);   // true</code></example>
    public static bool IsDecimal(FieldUnit unit) => (unit >= FieldUnit.Seconds && unit <= FieldUnit.Deg) || unit == FieldUnit.PerSecond;

    /// <summary>The integer part multiplier: 1000 for decimal units, 1 for integer units, 0 for an undeclared unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <example><code>long s = FieldUnits.Scale(FieldUnit.Deg);   // 1000</code></example>
    public static long Scale(FieldUnit unit) => IsDecimal(unit) ? 1000L : IsDeclared(unit) ? 1L : 0L;
}
