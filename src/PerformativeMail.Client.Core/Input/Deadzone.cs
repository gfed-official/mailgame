using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// GDD F3: the radial, scaled stick deadzone and response curve on Q15 axes (ADR-0012 section 5).
/// <c>n = clamp((|s| − dzInner) / (dzOuter − dzInner), 0, 1)</c>, <c>|s|</c> by <see cref="Fix.ISqrt"/> of the squared sum;
/// the output is <c>s / |s| × curve(n)</c>, so it keeps the input's direction (no cardinal snap) and lies on the unit disc.
/// Integer-only, pure, 0 B.
/// </summary>
/// <example>
/// <code>
/// Deadzone.Apply(19660, 0, 150, 950, ResponseCurveTable.Linear, out short x, out short y);   // x about 18432 (0.5625)
/// </code>
/// </example>
internal static class Deadzone
{
    /// <summary>Applies F3 to one stick sample.</summary>
    /// <param name="sx">Raw X, Q15.</param>
    /// <param name="sy">Raw Y, Q15.</param>
    /// <param name="dzInnerPm">Inner deadzone, permille.</param>
    /// <param name="dzOuterPm">Outer deadzone (full deflection), permille; above <paramref name="dzInnerPm"/>.</param>
    /// <param name="curve">The response curve table built for <c>curvePm</c> (<see cref="ResponseCurveTable.Linear"/> for the move stick).</param>
    /// <param name="x">Output X, Q15.</param>
    /// <param name="y">Output Y, Q15.</param>
    public static void Apply(short sx, short sy, int dzInnerPm, int dzOuterPm, ResponseCurveTable curve, out short x, out short y)
    {
        long mag = Fix.ISqrt((ulong)(((long)sx * sx) + ((long)sy * sy)));
        long innerScaled = (long)dzInnerPm * TrigQ15.One;
        long over = (mag * 1000) - innerScaled;
        if (over <= 0)
        {
            x = 0;
            y = 0;
            return;
        }

        long n = Math.Min(over / (dzOuterPm - dzInnerPm), TrigQ15.One);
        long outMag = curve.Map((int)n);
        x = (short)(sx * outMag / mag);
        y = (short)(sy * outMag / mag);
    }
}
