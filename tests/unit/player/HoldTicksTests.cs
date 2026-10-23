using System.Numerics;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 011, "Hold.HoldTicks, the single holdEff function" (TR-movement-012, TR-input-011; ADR-0014 section 4).
/// Rows PCM-F4-2 (player-character-movement F4) and H-F1-1 (input-controls F1) run against the one shared function, and the full
/// grid is compared with an independent exact <see cref="BigInteger"/> rational.
/// </summary>
public class HoldTicksTests
{
    private static readonly HoldSettings _defaults = new(400, 2000, 6, 750);

    /// <summary>Independent exact reference: ceil(a / b) for positive operands.</summary>
    private static BigInteger CeilDivBig(BigInteger a, BigInteger b)
    {
        BigInteger q = BigInteger.DivRem(a, b, out BigInteger r);
        return r.IsZero ? q : q + 1;
    }

    private static BigInteger ExactTicks(int baseMs, int insertPm, int settingMs)
    {
        BigInteger raw = CeilDivBig((BigInteger)baseMs * (1000 - insertPm) * settingMs * 30, (BigInteger)1000 * 400 * 1000);
        BigInteger ceil = CeilDivBig((BigInteger)Math.Max(baseMs, 2000) * 30, 1000);
        return BigInteger.Min(BigInteger.Max(raw, 6), ceil);
    }

    /// <summary>The PCM-F4-2 hold lengths.</summary>
    [Theory]
    [Trait("Id", "PCM-F4-2")]
    [InlineData(400, 0, 400, 12)]
    [InlineData(400, 500, 400, 6)]
    [InlineData(400, 500, 1000, 15)]
    [InlineData(400, 0, 200, 6)]
    [InlineData(400, 0, 1000, 30)]
    [InlineData(400, 1000, 400, 6)]
    [InlineData(2000, 0, 1000, 60)]
    public void HoldTicks_PcmF4Rows_MatchGdd(int baseMs, int insertPm, int settingMs, int expected)
    {
        // Arrange
        HoldSettings s = _defaults;

        // Act
        int ticks = Hold.HoldTicks(baseMs, insertPm, settingMs, in s);

        // Assert
        Assert.Equal(expected, ticks);
    }

    /// <summary>PCM-F4-2 progress: 1000 * 5 / 12 floors to 416.</summary>
    [Fact]
    [Trait("Id", "PCM-F4-2")]
    public void ProgressPm_FiveOfTwelve_Floors416()
    {
        Assert.Equal(416, Hold.ProgressPm(5, 12));
    }

    /// <summary>Progress is 0 for a non-positive length or held count, and never exceeds 1000.</summary>
    [Theory]
    [Trait("Id", "PCM-F4-2")]
    [InlineData(5, 0, 0)]
    [InlineData(5, -3, 0)]
    [InlineData(-4, 12, 0)]
    [InlineData(0, 12, 0)]
    [InlineData(12, 12, 1000)]
    [InlineData(40, 12, 1000)]
    [InlineData(int.MaxValue, 1, 1000)]
    [InlineData(int.MinValue, int.MinValue, 0)]
    public void ProgressPm_EdgeInputs_ClampTo0To1000(int held, int total, int expected)
    {
        Assert.Equal(expected, Hold.ProgressPm(held, total));
    }

    /// <summary>The input-controls H-F1-1 table.</summary>
    [Theory]
    [Trait("Id", "H-F1-1")]
    [InlineData(400, 0, 400, 12)]
    [InlineData(400, 500, 400, 6)]
    [InlineData(400, 500, 600, 9)]
    [InlineData(500, 0, 1000, 38)]
    [InlineData(800, 0, 1000, 60)]
    [InlineData(400, 500, 200, 6)]
    [InlineData(400, 500, 450, 7)]
    [InlineData(400, 750, 1000, 8)]
    public void HoldTicks_HF11Rows_MatchInputControlsTable(int baseMs, int insertPm, int settingMs, int expected)
    {
        // Arrange
        HoldSettings s = _defaults;

        // Act + Assert
        Assert.Equal(expected, Hold.HoldTicks(baseMs, insertPm, settingMs, in s));
    }

    /// <summary>Setting 200..1000 x insertPm {0,500,750} x base 100..2000 equals the exact rational.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_FullGrid_EqualsExactRational()
    {
        // Arrange
        HoldSettings s = _defaults;
        int cells = 0;

        // Act + Assert
        foreach (int insertPm in new[] { 0, 500, 750 })
        {
            for (int settingMs = 200; settingMs <= 1000; settingMs += 50)
            {
                for (int baseMs = 100; baseMs <= 2000; baseMs += 100)
                {
                    BigInteger expected = ExactTicks(baseMs, insertPm, settingMs);
                    Assert.True(
                        expected == Hold.HoldTicks(baseMs, insertPm, settingMs, in s),
                        $"base {baseMs} insert {insertPm} setting {settingMs}: expected {expected}, got {Hold.HoldTicks(baseMs, insertPm, settingMs, in s)}");
                    cells++;
                }
            }
        }

        Assert.Equal(3 * 17 * 20, cells);
    }

    /// <summary>A base above holdCeilMs lifts the upper clamp with it (ceil = base * 30 / 1000).</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_BaseAboveHoldCeil_UpperClampFollowsBase()
    {
        HoldSettings s = _defaults;
        Assert.Equal(ExactTicks(3000, 0, 1000), (BigInteger)Hold.HoldTicks(3000, 0, 1000, in s));
        Assert.Equal(90, Hold.HoldTicks(3000, 0, 1000, in s));

        // input-controls line 533: a 3.0 s content hold becomes 1.5 s at the 0.2 setting (the upper clamp does not bind).
        Assert.Equal(ExactTicks(3000, 0, 200), (BigInteger)Hold.HoldTicks(3000, 0, 200, in s));
        Assert.Equal(45, Hold.HoldTicks(3000, 0, 200, in s));
    }

    /// <summary>A default (all-zero) HoldSettings never throws: the zero divisor makes raw 0, so the result is the (zero) floor.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_DefaultSettings_NeverThrowsAndReturnsZero()
    {
        HoldSettings zero = default;
        int ticks = Hold.HoldTicks(400, 0, 400, in zero);
        Assert.Equal(0, ticks);
    }

    /// <summary>insertPm -5 acts as 0 and counts once.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_NegativeInsertPm_ActsAsZeroAndCountsOnce()
    {
        HoldSettings s = _defaults;
        var counters = new SimCounters();

        int ticks = Hold.HoldTicks(400, -5, 400, in s, counters);

        Assert.Equal(Hold.HoldTicks(400, 0, 400, in s), ticks);
        Assert.Equal(12, ticks);
        Assert.Equal(1, counters[CounterId.HoldInputClamped]);
    }

    /// <summary>insertPm 900 acts as 750 and counts once.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_InsertPmAboveMax_ActsAsMaxAndCountsOnce()
    {
        HoldSettings s = _defaults;
        var counters = new SimCounters();

        int ticks = Hold.HoldTicks(400, 900, 1000, in s, counters);

        Assert.Equal(Hold.HoldTicks(400, 750, 1000, in s), ticks);
        Assert.Equal(8, ticks);
        Assert.Equal(1, counters[CounterId.HoldInputClamped]);
    }

    /// <summary>In-range insertPm (0, 500, 750) and clamped settings never touch the counter.</summary>
    [Theory]
    [Trait("Id", "H-F1-1")]
    [InlineData(0, 400)]
    [InlineData(500, 400)]
    [InlineData(750, 1000)]
    [InlineData(500, 150)]
    [InlineData(500, 1200)]
    [InlineData(500, 430)]
    public void HoldTicks_InRangeInsertPm_CountsNothing(int insertPm, int settingMs)
    {
        HoldSettings s = _defaults;
        var counters = new SimCounters();

        _ = Hold.HoldTicks(400, insertPm, settingMs, in s, counters);

        Assert.Equal(0, counters[CounterId.HoldInputClamped]);
    }

    /// <summary>The counting overload returns what the plain overload returns, and a null counters object is tolerated.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_CountingOverload_ReturnsSameResultAsPlain()
    {
        HoldSettings s = _defaults;
        var counters = new SimCounters();

        foreach (int insertPm in new[] { -5, 0, 500, 750, 900 })
        {
            Assert.Equal(Hold.HoldTicks(700, insertPm, 650, in s), Hold.HoldTicks(700, insertPm, 650, in s, counters));
        }

        Assert.Equal(2, counters[CounterId.HoldInputClamped]);
        Assert.Equal(12, Hold.HoldTicks(400, 0, 400, in s, null));
    }

    /// <summary>holdSettingMs 150 acts as 200 and 1200 as 1000.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_SettingOutOfRange_ClampsTo200And1000()
    {
        HoldSettings s = _defaults;
        Assert.Equal(Hold.HoldTicks(1000, 0, 200, in s), Hold.HoldTicks(1000, 0, 150, in s));
        Assert.Equal(Hold.HoldTicks(1000, 0, 1000, in s), Hold.HoldTicks(1000, 0, 1200, in s));
        Assert.NotEqual(Hold.HoldTicks(1000, 0, 200, in s), Hold.HoldTicks(1000, 0, 1000, in s));
    }

    /// <summary>The documented rounding rule: nearest multiple of 50, ties up, after clamping to 200..1000.</summary>
    [Theory]
    [Trait("Id", "H-F1-1")]
    [InlineData(430, 450)]
    [InlineData(420, 400)]
    [InlineData(425, 450)]
    [InlineData(400, 400)]
    [InlineData(150, 200)]
    [InlineData(1200, 1000)]
    [InlineData(1000, 1000)]
    [InlineData(200, 200)]
    [InlineData(int.MinValue, 200)]
    [InlineData(int.MaxValue, 1000)]
    public void ClampSettingMs_Value_RoundsToNearestFiftyTiesUp(int input, int expected)
    {
        Assert.Equal(expected, Hold.ClampSettingMs(input));
    }

    /// <summary>HoldTicks applies the setting rounding: 430 gives the 450 result, 420 the 400 result.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_NonMultipleSetting_UsesRoundedSetting()
    {
        HoldSettings s = _defaults;
        Assert.Equal(Hold.HoldTicks(800, 0, 450, in s), Hold.HoldTicks(800, 0, 430, in s));
        Assert.Equal(Hold.HoldTicks(800, 0, 400, in s), Hold.HoldTicks(800, 0, 420, in s));
        Assert.NotEqual(Hold.HoldTicks(800, 0, 450, in s), Hold.HoldTicks(800, 0, 400, in s));
    }

    /// <summary>A negative base is treated as 0, giving the minHoldTicks floor.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_NegativeBase_GivesMinHoldTicks()
    {
        HoldSettings s = _defaults;
        Assert.Equal(6, Hold.HoldTicks(-400, 0, 400, in s));
        Assert.Equal(6, Hold.HoldTicks(int.MinValue, 0, 1000, in s));
    }

    /// <summary>No int32 input throws, and the result stays within [minHoldTicks, ceil].</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_ExtremeInts_NeverThrowAndStayInRange()
    {
        HoldSettings s = _defaults;
        int[] extremes = { int.MinValue, -1, 0, 1, 400, 1000, 2000, 123_456_789, int.MaxValue - 1, int.MaxValue };
        var counters = new SimCounters();

        foreach (int baseMs in extremes)
        {
            foreach (int insertPm in extremes)
            {
                foreach (int settingMs in extremes)
                {
                    int ticks = Hold.HoldTicks(baseMs, insertPm, settingMs, in s, counters);
                    long ceil = ((Math.Max(Math.Max(baseMs, 0), 2000) * 30L) + 999) / 1000;
                    Assert.InRange(ticks, s.MinHoldTicks, (int)ceil);
                }
            }
        }

        // The int.MaxValue base at insertPm 0 and the 1.0 s setting is the overflow case: it must equal the exact value.
        Assert.Equal(ExactTicks(int.MaxValue, 0, 1000), (BigInteger)Hold.HoldTicks(int.MaxValue, 0, 1000, in s));
    }

    /// <summary>HoldSettings copies MinHoldTicks and carries the Input defaults.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldSettings_FromPlayerSettings_CopiesMinHoldTicksAndDefaults()
    {
        PlayerSettings player = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        PlayerSettings tuned = player with { MinHoldTicks = 8 };

        HoldSettings fromDefault = HoldSettings.FromPlayerSettings(player);
        HoldSettings fromTuned = HoldSettings.FromPlayerSettings(tuned);

        Assert.Equal(player.MinHoldTicks, fromDefault.MinHoldTicks);
        Assert.Equal(400, fromDefault.HoldDefaultMs);
        Assert.Equal(2000, fromDefault.HoldCeilMs);
        Assert.Equal(750, fromDefault.MaxInsertPm);
        Assert.Equal(8, fromTuned.MinHoldTicks);
        Assert.Equal(8, Hold.HoldTicks(400, 500, 400, in fromTuned));
        Assert.Throws<ArgumentNullException>(() => HoldSettings.FromPlayerSettings(null!));
    }

    /// <summary>Hold is a static class and no member, local or return type is floating point.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void Hold_Type_IsStaticAndIntegerOnly()
    {
        Type t = typeof(Hold);
        Assert.True(t.IsAbstract && t.IsSealed);

        Type[] banned = { typeof(float), typeof(double), typeof(decimal) };
        BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (MethodInfo m in t.GetMethods(all))
        {
            Assert.DoesNotContain(m.ReturnType, banned);
            foreach (ParameterInfo p in m.GetParameters())
            {
                Assert.DoesNotContain(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType, banned);
            }

            IList<LocalVariableInfo>? locals = m.GetMethodBody()?.LocalVariables;
            foreach (LocalVariableInfo local in locals ?? Array.Empty<LocalVariableInfo>())
            {
                Assert.DoesNotContain(local.LocalType, banned);
            }
        }

        foreach (FieldInfo f in t.GetFields(all))
        {
            Assert.True(f.IsLiteral, $"Hold.{f.Name} must be a constant");
        }
    }
}

/// <summary>Hold.HoldTicks and ProgressPm allocate nothing (TD-008: optimised builds only).</summary>
[Collection(NonParallelCollection.Name)]
public sealed class HoldTicksAllocationTests
{
    /// <summary>10 000 calls of both overloads and ProgressPm allocate 0 B.</summary>
    [Fact]
    [Trait("Id", "H-F1-1")]
    public void HoldTicks_TenThousandCalls_AllocateZeroBytes()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(Hold).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var settings = new HoldSettings(400, 2000, 6, 750);
        var counters = new SimCounters();
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("10,000 Hold.HoldTicks + ProgressPm calls", () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                int ticks = Hold.HoldTicks(100 + (i % 20 * 100), (i % 5 * 250) - 100, 200 + (i % 17 * 50), in settings);
                ticks += Hold.HoldTicks(400, 900, 430, in settings, counters);
                sink += ticks + Hold.ProgressPm(i % 13, ticks);
            }
        });

        Assert.NotEqual(0, sink);
    }
}
