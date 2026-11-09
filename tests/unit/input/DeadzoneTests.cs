using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 009: GDD F3, the radial scaled deadzone and the look response curve, on Q15 values (1.0 = 32767),
/// through <see cref="Deadzone.Apply"/> and <see cref="ResponseCurveTable"/>.
/// </summary>
public class DeadzoneTests
{
    private const int DzInnerPm = 150;
    private const int DzOuterPm = 950;

    public static TheoryData<int, int> Curves => new()
    {
        { 1000, 18432 },   // 0.5625
        { 1500, 13824 },   // 0.5625^1.5 = 0.42
        { 3000, 5832 },    // 0.5625^3
    };

    [Fact]
    [Trait("Id", "H-F3-1")]
    public void Deadzone_ScalesPastTheInnerZone_KeepsDirection_AndStaysOnTheUnitDisc()
    {
        ResponseCurveTable look = ResponseCurveTable.Build(1500);

        // s = (0.6, 0): n = 0.5625; look 0.5625^1.5.
        Deadzone.Apply(19660, 0, DzInnerPm, DzOuterPm, ResponseCurveTable.Linear, out short moveX, out short moveY);
        Deadzone.Apply(19660, 0, DzInnerPm, DzOuterPm, look, out short lookX, out short lookY);

        // s = (0.14, 0) is inside the inner zone; (0.5, 0.5) is a diagonal; (1, 1) is a raw corner past the disc.
        Deadzone.Apply(4587, 0, DzInnerPm, DzOuterPm, look, out short innerX, out short innerY);
        Deadzone.Apply(16384, 16384, DzInnerPm, DzOuterPm, look, out short diagX, out short diagY);
        Deadzone.Apply(short.MaxValue, short.MaxValue, DzInnerPm, DzOuterPm, ResponseCurveTable.Linear, out short cornerX, out short cornerY);

        Assert.InRange(moveX, 18431, 18433);
        Assert.Equal(0, moveY);
        Assert.InRange(lookX, 13822, 13826);
        Assert.Equal(0, lookY);
        Assert.Equal((0, 0), (innerX, innerY));
        Assert.True(diagX > 0);
        Assert.Equal(diagX, diagY);
        Assert.Equal(cornerX, cornerY);
        Assert.InRange(Fix.ISqrt((ulong)((cornerX * cornerX) + (cornerY * cornerY))), 32766u, 32767u);
    }

    [Theory]
    [MemberData(nameof(Curves))]
    [Trait("Id", "H-F3-1")]
    public void PowerTable_IsMonotonic_FromZeroToOne_AndHitsTheSpotValue(int curvePm, int spotQ15)
    {
        ResponseCurveTable t = ResponseCurveTable.Build(curvePm);

        for (int i = 1; i < ResponseCurveTable.Length; i++)
        {
            Assert.True(t[i] >= t[i - 1], $"t[{i}] = {t[i]} < t[{i - 1}] = {t[i - 1]}");
        }

        Assert.Equal(0, t[0]);
        Assert.Equal(32767, t[ResponseCurveTable.Length - 1]);
        Assert.InRange(t.Map(18432), spotQ15 - 2, spotQ15 + 2);
    }
}
