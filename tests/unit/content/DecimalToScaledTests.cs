using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 016 (TR-content-005/006; GDD CDD-UNIT-1, CDD-UNIT-2, CDD-UNIT-3, CDD-PARSE-3 CV-GEN-06 half): the
/// <see cref="DecimalToScaled"/> reader, the <see cref="FieldUnit"/> scales, the <see cref="FieldSpec"/> unit self-check and
/// the existing F3 <see cref="ExactTickCheck"/> against F1 <see cref="TickTime.ContentTicks"/>.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class DecimalToScaledTests
{
    private static ScaledStatus Run(string token, FieldUnit unit, out long value) =>
        DecimalToScaled.Read(Encoding.UTF8.GetBytes(token), unit, out value);

    private static ScaledStatus Dur(string token, FieldUnit unit, out long value) =>
        DecimalToScaled.ReadDuration(Encoding.UTF8.GetBytes(token), unit, out value);

    // ------------------------------------------------------------ CDD-UNIT-2

    [Theory]
    [Trait("Id", "CDD-UNIT-2")]
    [InlineData("0.4", 400)]
    [InlineData("1.5", 1500)]
    [InlineData("15", 15000)]
    [InlineData("0.05", 50)]
    [InlineData("-2.25", -2250)]
    [InlineData("0", 0)]
    [InlineData("0.0", 0)]
    [InlineData("0.001", 1)]
    [InlineData("2147483.647", 2147483647)]
    [InlineData("-2147483.648", -2147483648)]
    public void Decimal_ConvertsToThousandths(string token, long expected)
    {
        Assert.Equal(ScaledStatus.Ok, Run(token, FieldUnit.Ratio, out long v));
        Assert.Equal(expected, v);
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void Decimal_FourthFractionDigit_IsFormat_CvGen06()
    {
        Assert.Equal(ScaledStatus.Format, Run("0.1234", FieldUnit.Metres, out long v));
        Assert.Equal(0, v);
        Assert.Equal("CV-GEN-06", DecimalToScaled.RuleIdOf(ScaledStatus.Format));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void Decimal_OverInt32_IsRange_CvGen12_AndTheMaxPasses()
    {
        Assert.Equal(ScaledStatus.Range, Run("2147483.648", FieldUnit.Ratio, out long v));
        Assert.Equal(0, v);
        Assert.Equal(ScaledStatus.Ok, Run("2147483.647", FieldUnit.Ratio, out v));
        Assert.Equal(2147483647L, v);
        Assert.Equal("CV-GEN-12", DecimalToScaled.RuleIdOf(ScaledStatus.Range));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void Integer_Int32Max_Passes_AndOneMoreIsRange()
    {
        Assert.Equal(ScaledStatus.Ok, Run("2147483647", FieldUnit.Count, out long v));
        Assert.Equal(2147483647L, v);
        Assert.Equal(ScaledStatus.Range, Run("2147483648", FieldUnit.Count, out _));
        Assert.Equal(ScaledStatus.Ok, Run("-2147483648", FieldUnit.Count, out v));
        Assert.Equal(-2147483648L, v);
        Assert.Equal(ScaledStatus.Range, Run("-2147483649", FieldUnit.Count, out _));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void Duration_Cap_86400Seconds_Passes_AndOneMillisecondMoreIsCvGen07()
    {
        Assert.Equal(ScaledStatus.Ok, Dur("86400", FieldUnit.Seconds, out long v));
        Assert.Equal(86_400_000L, v);
        Assert.Equal(ScaledStatus.Duration, Dur("86400.001", FieldUnit.Seconds, out v));
        Assert.Equal(0, v);
        Assert.Equal("CV-GEN-07", DecimalToScaled.RuleIdOf(ScaledStatus.Duration));
        Assert.Equal(ScaledStatus.Ok, Dur("86400000", FieldUnit.Ms, out v));
        Assert.Equal(ScaledStatus.Duration, Dur("86400001", FieldUnit.Ms, out _));
        Assert.Equal(ScaledStatus.Duration, Dur("-1", FieldUnit.Ms, out _));
        Assert.Equal(ScaledStatus.Ok, Dur("0", FieldUnit.Seconds, out v));
        Assert.Equal(0, v);
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void Duration_FarOverflow_IsDuration_NotFormat()
    {
        Assert.Equal(ScaledStatus.Duration, Dur("99999999999999999999999999", FieldUnit.Seconds, out _));
        Assert.Equal(ScaledStatus.Duration, Dur("99999999999999999999999999.5", FieldUnit.Seconds, out _));
    }

    // ------------------------------------------------------------ CDD-PARSE-3 (CV-GEN-06 half)

    [Theory]
    [Trait("Id", "CDD-PARSE-3")]
    [InlineData("1e3")]
    [InlineData("-0")]
    [InlineData("-0.00")]
    [InlineData("0.1000")]
    public void DecimalField_BadTokens_AreExactlyOneCvGen06(string token)
    {
        ScaledStatus status = Run(token, FieldUnit.Metres, out long v);
        Assert.Equal(ScaledStatus.Format, status);
        Assert.Equal(0, v);
        Assert.Equal("CV-GEN-06", DecimalToScaled.RuleIdOf(status));
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-3")]
    public void IntegerField_FractionToken_IsCvGen06()
    {
        Assert.Equal(ScaledStatus.Format, Run("1.0", FieldUnit.Count, out long v));
        Assert.Equal(0, v);
        Assert.Equal(ScaledStatus.Format, Run("1.0", FieldUnit.Ms, out _));
        Assert.Equal(ScaledStatus.Format, Run("400.5", FieldUnit.Cents, out _));
    }

    // ------------------------------------------------------------ grammar boundaries

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("+5")]
    [InlineData("5.")]
    [InlineData(".5")]
    [InlineData(".")]
    [InlineData("-.5")]
    [InlineData("01")]
    [InlineData("00")]
    [InlineData("-01")]
    [InlineData("0.5.5")]
    [InlineData("1E3")]
    [InlineData("1e-3")]
    [InlineData("1.5e3")]
    [InlineData("--1")]
    [InlineData("1 ")]
    [InlineData(" 1")]
    [InlineData("0x10")]
    [InlineData("1,5")]
    [InlineData("NaN")]
    [InlineData("-0.0")]
    [InlineData("-0.000")]
    public void MalformedTokens_AreFormat(string token)
    {
        Assert.Equal(ScaledStatus.Format, Run(token, FieldUnit.Ratio, out long v));
        Assert.Equal(0, v);
    }

    [Fact]
    public void LongOverflow_StopsEarly_AsRange_AndALaterFormatErrorStillWins()
    {
        Assert.Equal(ScaledStatus.Range, Run("99999999999999999999", FieldUnit.Count, out _));
        Assert.Equal(ScaledStatus.Range, Run("9223372036854775807", FieldUnit.Count, out _));
        Assert.Equal(ScaledStatus.Range, Run("9223372036854775808.5", FieldUnit.Ratio, out _));
        Assert.Equal(ScaledStatus.Range, Run("-99999999999999999999999.5", FieldUnit.Ratio, out _));
        Assert.Equal(ScaledStatus.Format, Run("99999999999999999999e3", FieldUnit.Count, out _));
        Assert.Equal(ScaledStatus.Format, Run("99999999999999999999.1234", FieldUnit.Ratio, out _));
        Assert.Equal(ScaledStatus.Format, Run("99999999999999999999.", FieldUnit.Ratio, out _));
    }

    [Fact]
    public void ExplicitRange_IsCheckedOnTheAssembledValue()
    {
        var token = "7"u8.ToArray();
        Assert.Equal(ScaledStatus.Range, DecimalToScaled.Read(token, FieldUnit.Count, 1, 6, out long v));
        Assert.Equal(0, v);
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.Read("6"u8, FieldUnit.Count, 1, 6, out v));
        Assert.Equal(6, v);
        Assert.Equal(ScaledStatus.Range, DecimalToScaled.Read("0"u8, FieldUnit.Count, 1, 6, out _));
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.Read("0.5"u8, FieldUnit.Ratio, 0, 1000, out v));
        Assert.Equal(500, v);
        Assert.Equal(ScaledStatus.Range, DecimalToScaled.Read("1.001"u8, FieldUnit.Ratio, 0, 1000, out _));
        Assert.Equal(ScaledStatus.Range, DecimalToScaled.Read("-0.001"u8, FieldUnit.Ratio, 0, 1000, out _));
    }

    [Fact]
    public void LongExtremeBounds_DoNotOverflow()
    {
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.Read("9000000000000000.5"u8, FieldUnit.Ratio, long.MinValue, long.MaxValue, out long v));
        Assert.Equal(9_000_000_000_000_000_500L, v);
        Assert.Equal(ScaledStatus.Range, DecimalToScaled.Read("9223372036854775"u8, FieldUnit.Ratio, long.MinValue, long.MaxValue, out _));
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.Read("-5"u8, FieldUnit.Count, long.MinValue, long.MaxValue, out v));
        Assert.Equal(-5, v);
    }

    [Fact]
    public void UndeclaredUnit_IsFormat()
    {
        Assert.Equal(ScaledStatus.Format, Run("5", default, out long v));
        Assert.Equal(0, v);
        Assert.Equal(ScaledStatus.Format, Run("5", (FieldUnit)99, out _));
    }

    [Fact]
    public void Ok_HasNoRuleId()
    {
        Assert.Null(DecimalToScaled.RuleIdOf(ScaledStatus.Ok));
        Assert.Equal(GeneralRuleIds.NumberFormat, DecimalToScaled.RuleIdOf(ScaledStatus.Format));
        Assert.Equal(GeneralRuleIds.ValueOutOfRange, DecimalToScaled.RuleIdOf(ScaledStatus.Range));
        Assert.Equal(GeneralRuleIds.DurationOverCap, DecimalToScaled.RuleIdOf(ScaledStatus.Duration));
    }

    // ------------------------------------------------------------ CDD-UNIT-1

    [Theory]
    [Trait("Id", "CDD-UNIT-1")]
    [InlineData("15", FieldUnit.Seconds, 15000)]     // batchIntervalSeconds
    [InlineData("1.5", FieldUnit.Metres, 1500)]      // catchRadiusMetres
    [InlineData("0.5", FieldUnit.Ratio, 500)]        // lateValueRatio
    [InlineData("5", FieldUnit.Points, 5000)]        // complaintOnMisdeliveryPoints
    [InlineData("6", FieldUnit.Deg, 6000)]           // loftDeg
    [InlineData("400", FieldUnit.Cents, 400)]        // price
    [InlineData("150", FieldUnit.Ms, 150)]
    [InlineData("1200", FieldUnit.Mm, 1200)]
    [InlineData("25", FieldUnit.Pm, 25)]
    [InlineData("12", FieldUnit.Ticks, 12)]
    [InlineData("9", FieldUnit.Count, 9)]
    public void EveryUnit_StoresAtItsScale(string token, FieldUnit unit, long expected)
    {
        Assert.Equal(ScaledStatus.Ok, Run(token, unit, out long v));
        Assert.Equal(expected, v);
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-1")]
    public void BatchIntervalSeconds_15_Is15000Ms_And450Ticks_ViaF1()
    {
        Assert.Equal(ScaledStatus.Ok, Dur("15", FieldUnit.Seconds, out long ms));
        Assert.Equal(15_000L, ms);
        Assert.Equal(450L, TickTime.ContentTicks(ms));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-1")]
    public void Scales_AndClassification_AreFrozen()
    {
        FieldUnit[] decimals = { FieldUnit.Seconds, FieldUnit.Metres, FieldUnit.Ratio, FieldUnit.Points, FieldUnit.Deg, FieldUnit.PerSecond };
        FieldUnit[] integers = { FieldUnit.Ms, FieldUnit.Mm, FieldUnit.Pm, FieldUnit.Ticks, FieldUnit.Cents, FieldUnit.Count };
        foreach (FieldUnit u in decimals)
        {
            Assert.True(FieldUnits.IsDeclared(u));
            Assert.True(FieldUnits.IsDecimal(u));
            Assert.Equal(1000L, FieldUnits.Scale(u));
        }

        foreach (FieldUnit u in integers)
        {
            Assert.True(FieldUnits.IsDeclared(u));
            Assert.False(FieldUnits.IsDecimal(u));
            Assert.Equal(1L, FieldUnits.Scale(u));
        }

        Assert.False(FieldUnits.IsDeclared(default));
        Assert.False(FieldUnits.IsDeclared((FieldUnit)13));
        Assert.Equal(0L, FieldUnits.Scale(default));
    }

    [Fact]
    public void FieldUnit_Values_AreExplicitAndFrozen()
    {
        FieldUnit[] order =
        {
            FieldUnit.Seconds, FieldUnit.Metres, FieldUnit.Ratio, FieldUnit.Points, FieldUnit.Deg, FieldUnit.Ms,
            FieldUnit.Mm, FieldUnit.Pm, FieldUnit.Ticks, FieldUnit.Cents, FieldUnit.Count, FieldUnit.PerSecond,
        };
        for (int i = 0; i < order.Length; i++)
        {
            Assert.Equal(i + 1, (int)order[i]);
        }

        Assert.Equal(order.Length, Enum.GetValues<FieldUnit>().Length);
    }

    // ------------------------------------------------------------ FieldSpec

    [Fact]
    [Trait("Id", "CDD-UNIT-1")]
    public void SelfCheck_NumberWithNoUnit_FailsWithCvGen06()
    {
        FieldSpec[] table =
        {
            FieldSpec.Number("price", FieldUnit.Cents),
            FieldSpec.Number("grid", default),
            FieldSpec.Other("category", FieldKind.Enum, enumSet: "ItemCategory"),
            FieldSpec.Number("maxStack", (FieldUnit)200),
        };

        var issues = FieldSpecTable.SelfCheck(table);

        Assert.Equal(2, issues.Length);
        Assert.Equal(new FieldSpecIssue("grid", "CV-GEN-06"), issues[0]);
        Assert.Equal(new FieldSpecIssue("maxStack", "CV-GEN-06"), issues[1]);
        Assert.Empty(FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("price", FieldUnit.Cents), FieldSpec.Other("owner", FieldKind.Reference, refTarget: "ItemDef") }));
    }

    [Fact]
    public void FieldSpec_ReadNumber_UsesUnitRangeAndDurationCap()
    {
        FieldSpec ratio = FieldSpec.Number("lateValueRatio", FieldUnit.Ratio, min: 0, max: 1000);
        Assert.Equal(ScaledStatus.Ok, ratio.ReadNumber("0.5"u8, out long v));
        Assert.Equal(500, v);
        Assert.Equal(ScaledStatus.Range, ratio.ReadNumber("1.5"u8, out _));

        FieldSpec dur = FieldSpec.Number("batchIntervalSeconds", FieldUnit.Seconds, isDuration: true);
        Assert.Equal(ScaledStatus.Ok, dur.ReadNumber("15"u8, out v));
        Assert.Equal(15_000L, v);
        Assert.Equal(ScaledStatus.Duration, dur.ReadNumber("86400.001"u8, out _));

        FieldSpec opt = FieldSpec.Number("maxStack", FieldUnit.Count, required: false, defaultValue: 1);
        Assert.False(opt.Required);
        Assert.Equal(1L, opt.Default);
        Assert.Equal("ItemDef", FieldSpec.Other("item", FieldKind.Reference, refTarget: "ItemDef").RefTarget);
    }

    // ------------------------------------------------------------ CDD-UNIT-3 (existing F3)

    [Theory]
    [Trait("Id", "CDD-UNIT-3")]
    [InlineData(500L, true, 15L)]
    [InlineData(100L, true, 3L)]
    [InlineData(501L, false, 16L)]
    [InlineData(50L, false, 2L)]
    [InlineData(150L, false, 5L)]
    public void ExactTickCheck_Vectors_AndF1Agreement(long ms, bool whole, long contentTicks)
    {
        Assert.Equal(whole, ExactTickCheck.IsWholeTicks(ms));
        Assert.Equal(contentTicks, TickTime.ContentTicks(ms));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_150_IsInvalid_WhileF1GivesFiveTicks()
    {
        Assert.False(ExactTickCheck.IsWholeTicks(150));
        Assert.Equal(5L, TickTime.ContentTicks(150));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_Property_WholeIffMultipleOf100_OverTheFullDomain()
    {
        int mismatches = 0;
        for (long ms = 0; ms <= 86_400_000; ms++)
        {
            if (ExactTickCheck.IsWholeTicks(ms) != (ms % 100 == 0))
            {
                mismatches++;
            }
        }

        Assert.Equal(0, mismatches);
    }

    // ------------------------------------------------------------ 0-allocation guardrail (TD-008)

    [Fact]
    public void Read_AllocatesNothing_InOptimisedBuilds()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(DecimalToScaled).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        byte[][] tokens =
        {
            Encoding.UTF8.GetBytes("0.4"), Encoding.UTF8.GetBytes("-2.25"), Encoding.UTF8.GetBytes("15"),
            Encoding.UTF8.GetBytes("0.1234"), Encoding.UTF8.GetBytes("2147483.648"), Encoding.UTF8.GetBytes("1e3"),
            Encoding.UTF8.GetBytes("99999999999999999999"), Encoding.UTF8.GetBytes("-0"),
        };
        long sum = 0;

        AllocAssert.Zero("DecimalToScaled.Read", () =>
        {
            sum = 0;
            for (int n = 0; n < 1000; n++)
            {
                for (int t = 0; t < tokens.Length; t++)
                {
                    sum += (long)DecimalToScaled.Read(tokens[t], FieldUnit.Ratio, out long v) + v;
                    sum += (long)DecimalToScaled.ReadDuration(tokens[t], FieldUnit.Seconds, out long d) + d;
                }
            }
        });

        Assert.NotEqual(0L, sum);
    }

    // ------------------------------------------------------------ TD-090 (story 018 carry-forward)

    [Theory]
    [Trait("Id", "CDD-UNIT-2")]
    [InlineData(FieldUnit.Mm)]
    [InlineData(FieldUnit.Metres)]
    [InlineData(FieldUnit.Count)]
    [InlineData((FieldUnit)0)]
    public void ReadDuration_WithANonTimeUnit_IsFormat_NotSilentlyCapped(FieldUnit unit)
    {
        Assert.Equal(ScaledStatus.Format, Dur("5", unit, out long v));
        Assert.Equal(0, v);
        Assert.Equal(ScaledStatus.Format, DecimalToScaled.ReadDuration("5"u8, unit, 0, 1000, out _));
    }

    [Theory]
    [Trait("Id", "CDD-UNIT-2")]
    [InlineData("-1.5e3")]
    [InlineData("-1.5E+3")]
    public void Exponent_IsFormat_EvenWhenNegative(string token)
    {
        Assert.Equal(ScaledStatus.Format, Run(token, FieldUnit.Ratio, out long v));
        Assert.Equal(0, v);
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void ReadDuration_HonoursTheRowRange_ClampedToTheDurationCap()
    {
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.ReadDuration("1.5"u8, FieldUnit.Seconds, 1000, 2000, out long v));
        Assert.Equal(1500, v);
        Assert.Equal(ScaledStatus.Duration, DecimalToScaled.ReadDuration("0.999"u8, FieldUnit.Seconds, 1000, 2000, out _));   // below the row min
        Assert.Equal(ScaledStatus.Duration, DecimalToScaled.ReadDuration("2.001"u8, FieldUnit.Seconds, 1000, 2000, out _));   // above the row max
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.ReadDuration("0"u8, FieldUnit.Seconds, long.MinValue, long.MaxValue, out v));   // min clamps up to 0
        Assert.Equal(ScaledStatus.Duration, DecimalToScaled.ReadDuration("86400.001"u8, FieldUnit.Seconds, long.MinValue, long.MaxValue, out _));   // max clamps down to the cap
        Assert.Equal(ScaledStatus.Duration, DecimalToScaled.ReadDuration("1"u8, FieldUnit.Seconds, 5000, 1000, out _));   // inverted range admits nothing

        FieldSpec row = FieldSpec.Number("batchIntervalSeconds", FieldUnit.Seconds, min: 5_000, max: 60_000, isDuration: true);
        Assert.Equal(ScaledStatus.Duration, row.ReadNumber("4.999"u8, out _));
        Assert.Equal(ScaledStatus.Ok, row.ReadNumber("5"u8, out v));
        Assert.Equal(5000, v);
        Assert.Equal(ScaledStatus.Ok, row.ReadNumber("60"u8, out _));
        Assert.Equal(ScaledStatus.Duration, row.ReadNumber("60.001"u8, out _));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-2")]
    public void IntegerUnits_FarOverflow_IsRange_AtTheDocumentedIntPartCap()
    {
        // IntPartCap (long.MaxValue / 1000) applies to scale-1 units too: just under it still reads, above it is Range.
        Assert.Equal(ScaledStatus.Range, Run("9223372036854776", FieldUnit.Count, out _));
        Assert.Equal(ScaledStatus.Range, Run("9223372036854775", FieldUnit.Cents, out _));   // fits a long, but is past any int-wide range anyway
        Assert.Equal(ScaledStatus.Ok, DecimalToScaled.Read("9223372036854775"u8, FieldUnit.Cents, 0, long.MaxValue / 2 + 1, out long v));
        Assert.Equal(9223372036854775L, v);
    }

    [Theory]
    [Trait("Id", "CDD-UNIT-2")]
    [InlineData("2", 2000)]
    [InlineData("0.5", 500)]
    [InlineData("1.25", 1250)]
    [InlineData("0", 0)]
    public void PerSecond_IsDecimal_StoredThousandths(string token, long expected)
    {
        Assert.True(FieldUnits.IsDeclared(FieldUnit.PerSecond));
        Assert.True(FieldUnits.IsDecimal(FieldUnit.PerSecond));
        Assert.Equal(1000, FieldUnits.Scale(FieldUnit.PerSecond));
        Assert.Equal(ScaledStatus.Ok, Run(token, FieldUnit.PerSecond, out long v));
        Assert.Equal(expected, v);
        Assert.Equal(ScaledStatus.Format, Run("1.2345", FieldUnit.PerSecond, out _));
        Assert.False(FieldUnits.IsDeclared((FieldUnit)13));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_NegativeAndOutOfDomain_FollowTheFormula_TheCallerRangeChecks()
    {
        Assert.True(ExactTickCheck.IsWholeTicks(-100));      // a negative multiple of 100 answers true: the caller range-checks
        Assert.False(ExactTickCheck.IsWholeTicks(-150));
        Assert.False(ExactTickCheck.IsWholeTicks(-1));
        Assert.True(ExactTickCheck.IsWholeTicks(0));
        Assert.True(ExactTickCheck.IsWholeTicks(86_400_000));
        Assert.False(ExactTickCheck.IsWholeTicks(86_400_001));
        Assert.True(ExactTickCheck.IsWholeTicks(86_400_100));   // past the duration cap: still a whole number of ticks
        Assert.Equal(ScaledStatus.Duration, Dur("86400.1", FieldUnit.Seconds, out _));   // ...so CV-GEN-07 is what rejects it
    }

    private static FieldSpecIssue? Only(params FieldSpec[] table)
    {
        var issues = FieldSpecTable.SelfCheck(table);
        return issues.Length == 1 ? issues[0] : null;
    }

    [Fact]
    public void SelfCheck_DurationWithANonTimeUnit_IsFlagged_AsCvGen06_ButDistinguishable()
    {
        Assert.Equal(
            new FieldSpecIssue("hold", "CV-GEN-06", FieldSpecIssueKind.DurationUnit),
            Only(FieldSpec.Number("hold", FieldUnit.Mm, isDuration: true)));
        Assert.Empty(FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("hold", FieldUnit.Ms, isDuration: true) }));   // Ms is fine
        Assert.Empty(FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("hold", FieldUnit.Seconds, isDuration: true) }));
        Assert.NotEqual(FieldSpecIssueKind.MissingUnit, FieldSpecIssueKind.DurationUnit);
    }

    [Fact]
    public void SelfCheck_MinAboveMax_IsFlagged()
    {
        Assert.Equal(
            new FieldSpecIssue("n", "CV-GEN-12", FieldSpecIssueKind.RangeInverted),
            Only(FieldSpec.Number("n", FieldUnit.Count, min: 5, max: 4)));
        Assert.Empty(FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("n", FieldUnit.Count, min: 5, max: 5) }));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(11L)]
    public void SelfCheck_DefaultOutsideTheRange_IsFlagged(long defaultValue)
    {
        Assert.Equal(
            new FieldSpecIssue("n", "CV-GEN-12", FieldSpecIssueKind.DefaultOutOfRange),
            Only(FieldSpec.Number("n", FieldUnit.Count, required: false, min: 0, max: 10, defaultValue: defaultValue)));
        Assert.Empty(FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("n", FieldUnit.Count, required: false, min: 0, max: 10, defaultValue: 10) }));
    }

    [Fact]
    public void SelfCheck_EnumWithoutASet_AndReferenceWithoutATarget_AreFlagged()
    {
        Assert.Equal(
            new FieldSpecIssue("category", "CV-GEN-08", FieldSpecIssueKind.MissingEnumSet),
            Only(FieldSpec.Other("category", FieldKind.Enum)));
        Assert.Equal(
            new FieldSpecIssue("cats", "CV-GEN-08", FieldSpecIssueKind.MissingEnumSet),
            Only(FieldSpec.Other("cats", FieldKind.EnumList, enumSet: "")));
        Assert.Equal(
            new FieldSpecIssue("owner", "CV-GEN-01", FieldSpecIssueKind.MissingRefTarget),
            Only(FieldSpec.Other("owner", FieldKind.Reference)));
        Assert.Equal(
            new FieldSpecIssue("accepts", "CV-GEN-01", FieldSpecIssueKind.MissingRefTarget),
            Only(FieldSpec.Other("accepts", FieldKind.RefList)));
    }

    [Fact]
    public void SelfCheck_DefaultOnARequiredRow_IsFlagged()
    {
        Assert.Equal(
            new FieldSpecIssue("n", "CV-GEN-05", FieldSpecIssueKind.DefaultOnRequired),
            Only(FieldSpec.Number("n", FieldUnit.Count, required: true, defaultValue: 0)));
        Assert.Equal(
            new FieldSpecIssue("slot", "CV-GEN-05", FieldSpecIssueKind.DefaultOnRequired),
            Only(FieldSpec.Enumeration("slot", "EquipSlot", required: true, defaultSpelling: "none")));
    }

    [Fact]
    public void SelfCheck_NestedTablesAndShapeDefects_AreFlagged_WithADottedPath()
    {
        var child = System.Collections.Immutable.ImmutableArray.Create(FieldSpec.Number("w", default));
        var issues = FieldSpecTable.SelfCheck(new[] { FieldSpec.Object("volume", child) });
        Assert.Equal(new FieldSpecIssue("volume.w", "CV-GEN-06", FieldSpecIssueKind.MissingUnit), Assert.Single(issues));

        Assert.Equal(
            new FieldSpecIssue("volume", "CV-GEN-03", FieldSpecIssueKind.MissingChildren),
            Only(FieldSpec.Object("volume", default)));
        Assert.Equal(
            new FieldSpecIssue("a", "CV-GEN-04", FieldSpecIssueKind.DuplicateName),
            FieldSpecTable.SelfCheck(new[] { FieldSpec.Text("a"), FieldSpec.Text("a") })[0]);
        Assert.Equal(
            new FieldSpecIssue("n", "CV-GEN-12", FieldSpecIssueKind.FixedLengthMisuse),
            Only(new FieldSpec { Name = "n", Kind = FieldKind.Number, Unit = FieldUnit.Count, FixedLength = 2 }));
        Assert.Equal(
            new FieldSpecIssue("g", "CV-GEN-14", FieldSpecIssueKind.VariantTarget),
            Only(FieldSpec.Number("g", FieldUnit.Count, required: false).WithVariant("layout", new[] { "grid" }, new string[0])));
    }
}
