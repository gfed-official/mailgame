using PerformativeMail.Sim.Content.Rules;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The hand-written number reader (ADR-0010 section 2, content-data F2). It reads the raw token bytes (the JSON reader's
/// <c>ValueSpan</c>) and never uses <c>GetDouble</c>, <c>GetDecimal</c> or <c>double.Parse</c>. Grammar:
/// <c>-?(0|[1-9][0-9]*)(\.[0-9]{1,3})?</c>, with the fraction allowed only for decimal units. It rejects an exponent, <c>-0</c>
/// (and <c>-0.00</c>), a leading <c>+</c>, a leading zero, a bare <c>.</c>, a missing digit either side of the point and a fourth
/// fraction digit, all as <see cref="ScaledStatus.Format"/>. It accumulates in <see cref="long"/>, stops accumulating as soon as the
/// integer part times the scale passes the field bound (it keeps scanning so a later format error still wins), uses no division,
/// no string and no allocation, and holds no state, so it is thread-safe. The stored value is
/// <c>sign x (intPart x scale + fraction x 10^(3 - fractionDigits))</c>. Seconds to ticks is
/// <see cref="Hosting.TickTime.ContentTicks"/> and the whole-tick rule is <see cref="ExactTickCheck"/>; neither is redefined here.
/// </summary>
/// <example>
/// <code>
/// ScaledStatus s = DecimalToScaled.Read("1.5"u8, FieldUnit.Metres, out long mm);          // Ok, mm == 1500
/// ScaledStatus d = DecimalToScaled.ReadDuration("86400.001"u8, FieldUnit.Seconds, out _);  // Duration (CV-GEN-07)
/// string rule = DecimalToScaled.RuleIdOf(d);                                               // "CV-GEN-07"
/// </code>
/// </example>
public static class DecimalToScaled
{
    /// <summary>The duration cap in milliseconds (Sim Core F1, 24 hours).</summary>
    public const long DurationMaxMs = 86_400_000;

    // The cap on the accumulated integer part is long.MaxValue / 1000 for EVERY unit, including the scale-1 integer units
    // (Count, Cents, Ms, ...): an integer-unit token whose integer part exceeds ~9.2e15 is reported as out of range even
    // though it would still fit a long. Harmless today (every field range is int-wide); it keeps the Seconds/Metres
    // multiply by 1000 overflow-free without a per-unit branch in the digit loop.
    private const long IntPartCap = long.MaxValue / 1000;
    private const long MaxMagnitude = long.MaxValue - 1000;

    /// <summary>Reads <paramref name="token"/> with the stored <see cref="int"/> range as the field range.</summary>
    /// <param name="token">The raw number token bytes.</param>
    /// <param name="unit">The field's declared unit.</param>
    /// <param name="value">The stored value; 0 unless the result is <see cref="ScaledStatus.Ok"/>.</param>
    /// <returns>The verdict.</returns>
    /// <example><code>ScaledStatus s = DecimalToScaled.Read("0.05"u8, FieldUnit.Ratio, out long pm);   // Ok, 50</code></example>
    public static ScaledStatus Read(ReadOnlySpan<byte> token, FieldUnit unit, out long value) =>
        Convert(token, unit, int.MinValue, int.MaxValue, ScaledStatus.Range, out value);

    /// <summary>Reads <paramref name="token"/> against an explicit stored range; out of range gives <see cref="ScaledStatus.Range"/>.</summary>
    /// <param name="token">The raw number token bytes.</param>
    /// <param name="unit">The field's declared unit.</param>
    /// <param name="min">The smallest stored value allowed.</param>
    /// <param name="max">The largest stored value allowed.</param>
    /// <param name="value">The stored value; 0 unless the result is <see cref="ScaledStatus.Ok"/>.</param>
    /// <returns>The verdict.</returns>
    /// <example><code>ScaledStatus s = DecimalToScaled.Read("7"u8, FieldUnit.Count, 1, 6, out _);   // Range</code></example>
    public static ScaledStatus Read(ReadOnlySpan<byte> token, FieldUnit unit, long min, long max, out long value) =>
        Convert(token, unit, min, max, ScaledStatus.Range, out value);

    /// <summary>
    /// Reads a duration (<see cref="FieldUnit.Seconds"/> or <see cref="FieldUnit.Ms"/>); outside 0 to <see cref="DurationMaxMs"/> gives
    /// <see cref="ScaledStatus.Duration"/> (CV-GEN-07). Any other unit gives <see cref="ScaledStatus.Format"/>: a duration with a
    /// non-time unit is a spec-table defect (<see cref="FieldSpecTable.SelfCheck"/> flags it), never silently capped as ms.
    /// </summary>
    /// <param name="token">The raw number token bytes.</param>
    /// <param name="unit">The field's declared unit.</param>
    /// <param name="value">The duration in milliseconds; 0 unless the result is <see cref="ScaledStatus.Ok"/>.</param>
    /// <returns>The verdict.</returns>
    /// <example><code>ScaledStatus s = DecimalToScaled.ReadDuration("15"u8, FieldUnit.Seconds, out long ms);   // Ok, 15000</code></example>
    public static ScaledStatus ReadDuration(ReadOnlySpan<byte> token, FieldUnit unit, out long value) =>
        ReadDuration(token, unit, 0, DurationMaxMs, out value);

    /// <summary>
    /// Reads a duration against the row's own range clamped to the duration cap: the allowed stored range is
    /// [max(<paramref name="min"/>, 0), min(<paramref name="max"/>, <see cref="DurationMaxMs"/>)] and outside it gives
    /// <see cref="ScaledStatus.Duration"/> (CV-GEN-07). A unit other than Seconds or Ms gives <see cref="ScaledStatus.Format"/>.
    /// </summary>
    /// <param name="token">The raw number token bytes.</param>
    /// <param name="unit">The field's declared unit (Seconds or Ms).</param>
    /// <param name="min">The row's smallest stored value (milliseconds).</param>
    /// <param name="max">The row's largest stored value (milliseconds).</param>
    /// <param name="value">The duration in milliseconds; 0 unless the result is <see cref="ScaledStatus.Ok"/>.</param>
    /// <returns>The verdict.</returns>
    /// <example><code>ScaledStatus s = DecimalToScaled.ReadDuration("2"u8, FieldUnit.Seconds, 0, 1500, out _);   // Duration</code></example>
    public static ScaledStatus ReadDuration(ReadOnlySpan<byte> token, FieldUnit unit, long min, long max, out long value)
    {
        if (unit != FieldUnit.Seconds && unit != FieldUnit.Ms)
        {
            value = 0;
            return ScaledStatus.Format;
        }

        return Convert(token, unit, Math.Max(min, 0), Math.Min(max, DurationMaxMs), ScaledStatus.Duration, out value);
    }

    /// <summary>The catalogue rule id for a status: CV-GEN-06, CV-GEN-12 or CV-GEN-07; <c>null</c> for <see cref="ScaledStatus.Ok"/>.</summary>
    /// <param name="status">The verdict.</param>
    /// <example><code>string? id = DecimalToScaled.RuleIdOf(ScaledStatus.Format);   // "CV-GEN-06"</code></example>
    public static string? RuleIdOf(ScaledStatus status) => status switch
    {
        ScaledStatus.Format => GeneralRuleIds.NumberFormat,
        ScaledStatus.Range => GeneralRuleIds.ValueOutOfRange,
        ScaledStatus.Duration => GeneralRuleIds.DurationOverCap,
        _ => null,
    };

    private static ScaledStatus Convert(
        ReadOnlySpan<byte> token, FieldUnit unit, long min, long max, ScaledStatus outOfRange, out long value)
    {
        value = 0;
        long scale = FieldUnits.Scale(unit);
        if (scale == 0)
        {
            return ScaledStatus.Format;
        }

        bool negative = token.Length > 0 && token[0] == (byte)'-';
        int i = negative ? 1 : 0;
        if (!IsFormatOk(token, ref i, scale, BoundMagnitude(min, max), out long intPart, out bool over, out long fraction))
        {
            return ScaledStatus.Format;
        }

        return Assemble(intPart, fraction, over, negative, scale, min, max, outOfRange, out value);
    }

    // Grammar check: the integer part, the optional fraction, then nothing else. Format errors win over an overflow.
    private static bool IsFormatOk(
        ReadOnlySpan<byte> token, ref int i, long scale, long limit, out long intPart, out bool over, out long fraction)
    {
        fraction = 0;
        return ReadInteger(token, ref i, scale, limit, out intPart, out over)
            && ReadFraction(token, ref i, scale != 1, out fraction)
            && i == token.Length;
    }

    // Turns a well-formed token's parts into the stored value and applies the zero-sign and range rules.
    private static ScaledStatus Assemble(
        long intPart, long fraction, bool over, bool negative, long scale, long min, long max, ScaledStatus outOfRange, out long value)
    {
        value = 0;
        if (over)
        {
            return outOfRange;
        }

        long magnitude = (intPart * scale) + fraction;
        if (negative && magnitude == 0)
        {
            return ScaledStatus.Format;
        }

        long result = negative ? -magnitude : magnitude;
        if (result < min || result > max)
        {
            return outOfRange;
        }

        value = result;
        return ScaledStatus.Ok;
    }

    private static long BoundMagnitude(long min, long max)
    {
        long up = max > 0 ? max : 0;
        long down = min < 0 ? (min == long.MinValue ? long.MaxValue : -min) : 0;
        return Math.Min(Math.Max(up, down), MaxMagnitude);
    }

    private static bool ReadInteger(
        ReadOnlySpan<byte> token, ref int i, long scale, long limit, out long intPart, out bool over)
    {
        intPart = 0;
        over = false;
        if (i >= token.Length || !IsDigit(token[i]))
        {
            return false;
        }

        if (token[i] == (byte)'0')
        {
            i++;
            return i >= token.Length || !IsDigit(token[i]);
        }

        while (i < token.Length && IsDigit(token[i]))
        {
            if (!over)
            {
                intPart = (intPart * 10) + (token[i] - (byte)'0');
                over = intPart > IntPartCap || (intPart * scale) > limit;
            }

            i++;
        }

        return true;
    }

    private static bool ReadFraction(ReadOnlySpan<byte> token, ref int i, bool allowed, out long fraction)
    {
        fraction = 0;
        if (i >= token.Length || token[i] != (byte)'.')
        {
            return true;
        }

        i++;
        int digits = 0;
        while (allowed && i < token.Length && IsDigit(token[i]) && digits < 3)
        {
            fraction = (fraction * 10) + (token[i] - (byte)'0');
            digits++;
            i++;
        }

        if (digits == 0)
        {
            return false;
        }

        for (int k = digits; k < 3; k++)
        {
            fraction *= 10;
        }

        return true;
    }

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';
}
