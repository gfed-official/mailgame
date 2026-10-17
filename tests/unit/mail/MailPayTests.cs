using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 004: <see cref="MailPay"/> F5 timeliness, F6 unit pay, F7 unit penalty, F8 wallet guard and F10 hint
/// payout (AC-1 to AC-5). Sheets are real (<see cref="StatSheet.Build"/> over the M-1a content) with synthetic perk sources.
/// </summary>
public class MailPayTests
{
    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    private static Modifier Team(Stat stat, ModifierOp op, int value, ushort qualifier = StatSheet.NoQualifier) =>
        new(SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.None, stat, qualifier, op, value, ModifierLifetime.Run);

    private static Modifier Mine(int slot, Stat stat, ModifierOp op, int value) =>
        new(SourceKind.Perk, DefIndex.FromValue(0), 0, PlayerSlot.FromValue((byte)slot), stat, StatSheet.NoQualifier, op, value, ModifierLifetime.Run);

    private static IStatSheet Build(params Modifier[] modifiers) => StatSheet.Build(_content, new StatSources(modifiers));

    private static DefIndex Kind(string id)
    {
        ReadOnlySpan<MailKindDef> kinds = _content.MailKinds.AsSpan();
        for (int i = 0; i < kinds.Length; i++)
        {
            if (kinds[i].Id == id)
            {
                return DefIndex.FromValue((ushort)i);
            }
        }

        throw new InvalidOperationException("no mail kind " + id);
    }

    private static Modifier StampCollector() =>
        Team(Stat.MailValueMultPm, ModifierOp.Mul, 1250, Kind("postcard").Value);

    private static Modifier StampCollectorLetter() =>
        Team(Stat.MailValueMultPm, ModifierOp.Mul, 1250, Kind("letter").Value);

    private static Modifier Grace() => Team(Stat.LateValueRatioPm, ModifierOp.Set, 1000);

    private static Modifier Fragile() => Team(Stat.MisdeliveryPenaltyMultPm, ModifierOp.Mul, 500);

    private static IStatSheet StampSheet() => Build(StampCollector(), StampCollectorLetter());

    private static UnitFacts Unit(string kind, int value, int deadline = 1) =>
        new(Kind(kind), ItemCategory.Mail, AddressId.None, (ushort)value, (byte)deadline, MailFlags.None);

    private static long Pay(UnitFacts u, int t, int thrownPm, IStatSheet s)
    {
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        return MailPay.UnitPay(in u, t, thrownPm, s, null, ref sink).Value;
    }

    private static long Penalty(UnitFacts u, IStatSheet s)
    {
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        return MailPay.UnitPenalty(in u, _content.Mail.MisdeliveryPenaltyRatio, _content.Mail.MinPenaltyCents, s, ref sink).Value;
    }

    private static long Payout(HintRun[] runs, int shift, string kind, IStatSheet s)
    {
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        return MailPay.HintPayout(
            runs, shift, Kind(kind), MailPay.LateValueRatioPm(s), _content.Mail.DeadLetterShifts, 1000, s, null, ref sink).Value;
    }

    private static long OneUnitPenalty(HintRun[] runs, string kind, IStatSheet s)
    {
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        return MailPay.HintPenalty(
            runs, Kind(kind), _content.Mail.MisdeliveryPenaltyRatio, _content.Mail.MinPenaltyCents, s, ref sink).Value;
    }

    private static Cents C(long value)
    {
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        return Cents.FromCents(value, ref sink);
    }

    // ------------------------------------------------------------ AC-1 (F5)

    [Theory]
    [InlineData(2, 2, 1000)]
    [InlineData(2, 3, 500)]
    [InlineData(2, 4, 0)]
    [InlineData(3, 2, 1000)]
    [Trait("Id", "M-F5-1")]
    public void Timeliness_DefaultBalance_ReturnsOnTimeLateOrDeadLetter(int deadline, int current, int expected)
    {
        // Arrange
        IStatSheet sheet = Build();

        // Act
        int t = MailPay.Timeliness(current, deadline, MailPay.LateValueRatioPm(sheet), _content.Mail.DeadLetterShifts);

        // Assert
        Assert.Equal(expected, t);
    }

    [Fact]
    [Trait("Id", "M-F5-1")]
    public void LateValueRatioPm_WithoutAndWithGracePeriod_ReadsDataValueThenOneThousand()
    {
        // Arrange / Act / Assert
        Assert.Equal(_content.Mail.LateValueRatio, MailPay.LateValueRatioPm(Build()));
        Assert.Equal(1000, MailPay.LateValueRatioPm(Build(Grace())));
    }

    [Theory]
    [InlineData(2, 3, 1000)]
    [InlineData(2, 4, 0)]
    [Trait("Id", "M-F5-1")]
    public void Timeliness_WithGracePeriod_LateStillPaysFullAndDeadLetterPaysNothing(int deadline, int current, int expected)
    {
        // Arrange
        IStatSheet sheet = Build(Grace());

        // Act
        int t = MailPay.Timeliness(current, deadline, MailPay.LateValueRatioPm(sheet), _content.Mail.DeadLetterShifts);

        // Assert
        Assert.Equal(expected, t);
    }

    [Fact]
    [Trait("Id", "M-F5-1")]
    public void Timeliness_WithDeadLetterShiftsOne_OneShiftLateIsADeadLetter()
    {
        // Arrange / Act
        int t = MailPay.Timeliness(3, 2, 500, 1);

        // Assert
        Assert.Equal(0, t);
    }

    // ------------------------------------------------------------ AC-2 (F6)

    [Fact]
    [Trait("Id", "M-F6-1")]
    public void UnitPay_LateLetterWithStampCollector_RoundsOnceToNine()
    {
        // Arrange: 14 x 500 x 1250 x 1000 / 10^9 = 8.75
        IStatSheet sheet = StampSheet();

        // Act
        long pay = Pay(Unit("letter", 14), 500, 1000, sheet);

        // Assert
        Assert.Equal(9, pay);
    }

    [Fact]
    [Trait("Id", "M-F6-1")]
    public void UnitPay_RoundsOnceOverTheWholeProduct_NotPerFactor()
    {
        // Arrange: 5 x 0.5 x 1.25 x 1.1 = 3.4375 -> 3 in one rounding; per-factor rounding (3 -> 4 -> 4) would give 4
        IStatSheet sheet = StampSheet();

        // Act
        long pay = Pay(Unit("letter", 5), 500, 1100, sheet);

        // Assert
        Assert.Equal(3, pay);
    }

    [Fact]
    [Trait("Id", "M-R7-1a")]
    public void UnitPay_IgnoresCannonArmOnTheSheet_WhenThrownPmIsOneThousand()
    {
        // Arrange: Cannon Arm is on the sheet for slot 1, but the caller passes thrownPm 1000 (a hand delivery)
        IStatSheet sheet = Build(Mine(1, Stat.ThrownCorrectPayMultPm, ModifierOp.Mul, 1100));
        UnitFacts u = Unit("letter", 8);
        Span<long> counts = stackalloc long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(counts);

        // Act: a hidden sheet read would give 8.8 -> 9
        long pay = MailPay.UnitPay(in u, 1000, 1000, sheet, PlayerSlot.FromValue(1), ref sink).Value;

        // Assert
        Assert.Equal(1100, sheet.Read(Stat.ThrownCorrectPayMultPm, slot: PlayerSlot.FromValue(1)));
        Assert.Equal(8, pay);
    }

    [Fact]
    [Trait("Id", "M-F5-1")]
    public void Timeliness_WithDeadLetterShiftsThree_TwoShiftsLateIsStillLate_ThreeIsDead()
    {
        Assert.Equal(500, MailPay.Timeliness(4, 2, 500, 3));
        Assert.Equal(0, MailPay.Timeliness(5, 2, 500, 3));
    }

    [Fact]
    [Trait("Id", "M-R7-1a")]
    public void UnitPay_ThrownOnTimeLetterWithStampCollectorAndCannonArm_PaysEleven()
    {
        // Arrange: thrownPm is read from the sheet by the caller, then passed in
        IStatSheet sheet = Build(StampCollector(), StampCollectorLetter(), Mine(1, Stat.ThrownCorrectPayMultPm, ModifierOp.Mul, 1100));
        int thrownPm = sheet.Read(Stat.ThrownCorrectPayMultPm, slot: PlayerSlot.FromValue(1));

        // Act
        long pay = Pay(Unit("letter", 8), 1000, thrownPm, sheet);

        // Assert
        Assert.Equal(1100, thrownPm);
        Assert.Equal(11, pay);
    }

    [Fact]
    [Trait("Id", "M-R8-1a")]
    public void UnitPay_DeadLetterUnderStampCollectorAndCannonArm_PaysNothing()
    {
        // Arrange
        IStatSheet sheet = Build(StampCollector(), StampCollectorLetter(), Mine(1, Stat.ThrownCorrectPayMultPm, ModifierOp.Mul, 1100));

        // Act
        long pay = Pay(Unit("letter", 8), MailPay.Timeliness(4, 2, 500, 2), 1100, sheet);

        // Assert
        Assert.Equal(0, pay);
    }

    [Theory]
    [InlineData("letter", 8, 10)]
    [InlineData("postcard", 4, 5)]
    [InlineData("small", 30, 30)]
    [Trait("Id", "M-F6-1")]
    public void UnitPay_StampCollector_AppliesToPostcardAndLetterOnly(string kind, int value, long expected)
    {
        // Arrange
        IStatSheet sheet = StampSheet();

        // Act
        long pay = Pay(Unit(kind, value), 1000, 1000, sheet);

        // Assert
        Assert.Equal(expected, pay);
    }

    [Fact]
    [Trait("Id", "M-F6-1")]
    public void UnitPay_Parameters_TakeNoWalletOrRandomSource()
    {
        // Arrange
        var method = typeof(MailPay).GetMethod(nameof(MailPay.UnitPay))!;

        // Act
        Type[] types = method.GetParameters().Select(p => p.ParameterType).ToArray();

        // Assert
        Assert.Equal(
            [typeof(UnitFacts).MakeByRefType(), typeof(int), typeof(int), typeof(IStatSheet), typeof(PlayerSlot?), typeof(SimCounterSink).MakeByRefType()],
            types);
    }

    [Fact]
    [Trait("Id", "M-F6-1")]
    public void UnitPay_FortyUnitsAtMaximumPay_SumsWithinInt32()
    {
        // Arrange: largest spawn 1344, T 1000, MailValueMult at payMultMaxPm, thrownPm at payMultMaxPm
        int max = _content.Mail.PayMultMaxPm;
        IStatSheet sheet = Build(Team(Stat.MailValueMultPm, ModifierOp.Mul, max, Kind("large").Value));

        // Act
        long pay = Pay(Unit("large", 1344), 1000, max, sheet);

        // Assert
        Assert.Equal(1344L * max / 1000 * max / 1000, pay);
        Assert.True(40 * pay <= int.MaxValue);
    }

    // ------------------------------------------------------------ AC-3 (F7)

    [Theory]
    [InlineData(8, 4)]
    [InlineData(22, 11)]
    [InlineData(5, 3)]
    [Trait("Id", "M-F7-1")]
    public void UnitPenalty_DefaultSheet_IsHalfTheSpawnValueRoundedHalfUp(int value, long expected)
    {
        // Arrange
        IStatSheet sheet = Build();

        // Act
        long penalty = Penalty(Unit("letter", value), sheet);

        // Assert
        Assert.Equal(expected, penalty);
    }

    [Fact]
    [Trait("Id", "M-R8-1a")]
    public void UnitPenalty_DeadLetter_StillUsesTheSpawnValue()
    {
        // Arrange
        IStatSheet sheet = Build();
        UnitFacts dead = Unit("letter", 8, deadline: 1);

        // Act
        long onTime = Penalty(dead, sheet);
        long afterDeadline = Penalty(dead, sheet);

        // Assert
        Assert.Equal(0, MailPay.Timeliness(4, dead.DeadlineShift, 500, 2));
        Assert.Equal(4, onTime);
        Assert.Equal(onTime, afterDeadline);
    }

    [Theory]
    [InlineData("postcard", 4, 1)]
    [InlineData("letter", 9, 2)]
    [InlineData("letter", 1, 1)]
    [Trait("Id", "M-F7-1")]
    public void UnitPenalty_FragileHandling_HalvesAndRespectsTheMinimum(string kind, int value, long expected)
    {
        // Arrange
        IStatSheet sheet = Build(Fragile());

        // Act
        long penalty = Penalty(Unit(kind, value), sheet);

        // Assert
        Assert.Equal(expected, penalty);
    }

    [Fact]
    [Trait("Id", "M-R8-1d")]
    public void UnitPenalty_WithStampCollector_IsUnchanged()
    {
        // Arrange / Act
        long baseline = Penalty(Unit("letter", 8), Build());
        long withStamp = Penalty(Unit("letter", 8), StampSheet());

        // Assert
        Assert.Equal(baseline, withStamp);
        Assert.Equal(4, withStamp);
    }

    // ------------------------------------------------------------ AC-4 (F8)

    [Theory]
    [InlineData(-496, 4, -500, true)]
    [InlineData(-500, 1, -500, false)]
    [InlineData(-600, 1, -500, false)]
    [InlineData(0, 220, -500, true)]
    [InlineData(-440, 60, -500, true)]
    [InlineData(-495, 11, -500, false)]
    [Trait("Id", "M-F8-1d")]
    public void WalletGuard_BalanceMinusPenaltyAgainstFloor_AllowsExactlyTheFloor(long balance, long penalty, long floor, bool expected)
    {
        // Arrange / Act
        bool allowed = MailPay.WalletGuard(C(balance), C(penalty), C(floor));

        // Assert
        Assert.Equal(expected, allowed);
    }

    // ------------------------------------------------------------ AC-5 (F10)

    private static HintRun[] DistrictOneRuns() => [new HintRun(1, 3, 8), new HintRun(2, 2, 9)];

    [Fact]
    [Trait("Id", "M-F10-1a")]
    public void HintPayout_DistrictOneRunsAtShiftTwo_IsThirtyCentsAndOneUnitPenaltyFour()
    {
        // Arrange
        IStatSheet sheet = Build();
        HintRun[] runs = DistrictOneRuns();

        // Act
        long payout = Payout(runs, 2, "letter", sheet);
        long penalty = OneUnitPenalty(runs, "letter", sheet);

        // Assert: 3 x 4 + 2 x 9
        Assert.Equal(30, payout);
        Assert.Equal(4, penalty);
    }

    [Fact]
    [Trait("Id", "M-F10-1c")]
    public void HintPayout_RecomputedAtALaterShift_NeedsNoNewHint()
    {
        // Arrange
        IStatSheet sheet = Build();
        HintRun[] runs = DistrictOneRuns();

        // Act: run 1 is now a dead letter, run 2 one shift late (4.5 -> 5)
        long payout = Payout(runs, 3, "letter", sheet);

        // Assert
        Assert.Equal(2 * 5, payout);
    }

    [Fact]
    [Trait("Id", "M-F10-1d")]
    public void HintPayout_EqualsTheSumOfPerUnitPays()
    {
        // Arrange
        IStatSheet sheet = StampSheet();
        HintRun[] runs = DistrictOneRuns();
        long expected = 0;
        foreach (HintRun run in runs)
        {
            int t = MailPay.Timeliness(3, run.DeadlineShift, 500, 2);
            for (int i = 0; i < run.Count; i++)
            {
                expected += Pay(Unit("letter", run.UnitSpawnValue, run.DeadlineShift), t, 1000, sheet);
            }
        }

        // Act
        long payout = Payout(runs, 3, "letter", sheet);

        // Assert
        Assert.Equal(expected, payout);
    }

    [Fact]
    [Trait("Id", "M-F10-1a")]
    public void HintPenalty_WithNoRuns_IsZero()
    {
        // Arrange / Act / Assert
        Assert.Equal(0, OneUnitPenalty([], "letter", Build()));
        Assert.Equal(0, Payout([], 2, "letter", Build()));
    }

    [Fact]
    [Trait("Id", "M-R7-2a")]
    public void HintPayout_WithUnrelatedStatModifiers_LeavesPayAtBaseline()
    {
        // Arrange: stats Run Structure owns or that are unrelated to pay
        IStatSheet noisy = Build(
            Team(Stat.MailMaxStack, ModifierOp.Mul, 1500, Kind("letter").Value),
            Team(Stat.MisdeliveryComplaintMultPm, ModifierOp.Mul, 500));
        HintRun[] runs = DistrictOneRuns();

        // Act / Assert
        Assert.Equal(Payout(runs, 2, "letter", Build()), Payout(runs, 2, "letter", noisy));
        Assert.Equal(OneUnitPenalty(runs, "letter", Build()), OneUnitPenalty(runs, "letter", noisy));
    }
}
