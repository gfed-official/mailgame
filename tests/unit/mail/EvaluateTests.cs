using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 003, AC-1 to AC-3: <c>MailRules.Evaluate</c> over the facts structs (M-R5-1, M-PERF-1a).
/// </summary>
public class EvaluateTests
{
    private const int CaseCount = 7;

    private readonly record struct Case(DestinationFacts Dest, UnitFacts Unit, EvalResult Result, MailRejectReason Reason);

    [Theory]
    [Trait("Id", "M-R5-1a")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Ac1_Evaluate_FollowsTheCheckOrder_ForEachCase(int index)
    {
        Case c = Make(index);

        DestinationFacts d = c.Dest;
        UnitFacts u = c.Unit;
        DeliverEval e = MailRules.Evaluate(in d, in u);

        Assert.Equal(c.Result, e.Result);
        Assert.Equal(c.Reason, e.Reason);
    }

    [Fact]
    [Trait("Id", "M-R5-1a")]
    public void Ac1_ClosedAndLockedDistrict_RegistryFactsAreClosed_AndRejectClosed()
    {
        Case c = Make(2);

        DestinationFacts d = c.Dest;
        UnitFacts u = c.Unit;
        DeliverEval e = MailRules.Evaluate(in d, in u);

        Assert.Equal(DestState.Closed, d.State);
        Assert.Equal(EvalResult.Reject, e.Result);
        Assert.Equal(MailRejectReason.Closed, e.Reason);
    }

    [Fact]
    [Trait("Id", "M-R5-1a")]
    public void Ac1_StateChecks_PrecedeTheCategoryAndKindChecks()
    {
        // A closed mailbox offered a tool reads "closed", a locked one reads "not on route": the state checks run first.
        DestinationFacts closed = RegistryFacts(false, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
        DestinationFacts locked = RegistryFacts(true, 1UL << 1, out _);
        var tool = new UnitFacts(KindOf(c, closed), ItemCategory.Tool, closed.Address, 0, 0, MailFlags.None);
        var strangeKind = new UnitFacts(DefIndex.FromValue(63), ItemCategory.Mail, locked.Address, 0, 0, MailFlags.None);

        DeliverEval closedTool = MailRules.Evaluate(in closed, in tool);
        DeliverEval lockedTool = MailRules.Evaluate(in locked, in tool);
        DeliverEval lockedKind = MailRules.Evaluate(in locked, in strangeKind);

        Assert.Equal((EvalResult.Reject, MailRejectReason.Closed), (closedTool.Result, closedTool.Reason));
        Assert.Equal((EvalResult.Reject, MailRejectReason.NotOnRoute), (lockedTool.Result, lockedTool.Reason));
        Assert.Equal((EvalResult.Reject, MailRejectReason.NotOnRoute), (lockedKind.Result, lockedKind.Reason));
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac1_RegistryFacts_AcceptsMask_CoversEveryKindTheHouseAccepts_AndNoOther()
    {
        DestinationFacts d = RegistryFacts(true, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
        DestinationTypeDef house = c.DestinationTypes[d.Type];
        ulong expected = 0UL;

        foreach (DefIndex kind in house.Accepts)
        {
            expected |= 1UL << kind.Value;
            var u = new UnitFacts(kind, ItemCategory.Mail, d.Address, 0, 0, MailFlags.None);
            Assert.Equal(EvalResult.Accept, MailRules.Evaluate(in d, in u).Result);
        }

        var outside = new UnitFacts(DefIndex.FromValue((ushort)house.Accepts.Length), ItemCategory.Mail, d.Address, 0, 0, MailFlags.None);

        Assert.Equal(5, house.Accepts.Length);
        Assert.Equal(expected, d.AcceptsMask);
        Assert.False(house.RequiresUnit);
        Assert.Equal(house.RequiresUnit, d.RequiresUnit);
        Assert.Equal(MailRejectReason.WontFit, MailRules.Evaluate(in d, in outside).Reason);
    }

    [Fact]
    [Trait("Id", "M-R5-1a")]
    public void Ac1_KindOf64OrMore_IsNeverAccepted_EvenWithAFullMask()
    {
        DestinationFacts d = OpenFacts(ulong.MaxValue, requiresUnit: false);
        var u = new UnitFacts(DefIndex.FromValue(64), ItemCategory.Mail, d.Address, 0, 0, MailFlags.None);

        DeliverEval e = MailRules.Evaluate(in d, in u);

        Assert.Equal(EvalResult.Reject, e.Result);
        Assert.Equal(MailRejectReason.WontFit, e.Reason);
    }

    [Fact]
    [Trait("Id", "M-R5-1b")]
    public void Ac2_RepeatedCalls_GiveIdenticalResults()
    {
        for (int index = 0; index < CaseCount; index++)
        {
            Case c = Make(index);
            DestinationFacts d = c.Dest;
            UnitFacts u = c.Unit;
            DeliverEval first = MailRules.Evaluate(in d, in u);
            for (int i = 0; i < 1000; i++)
            {
                DeliverEval again = MailRules.Evaluate(in d, in u);
                Assert.Equal(first.Result, again.Result);
                Assert.Equal(first.Reason, again.Reason);
            }
        }
    }

    [Fact]
    [Trait("Id", "M-R5-1b")]
    public void Ac2_EvaluateSignature_IsExactlyDestinationFactsAndUnitFacts_NoWalletOrRng()
    {
        System.Reflection.MethodInfo? m = typeof(MailRules).GetMethod(nameof(MailRules.Evaluate));

        Assert.NotNull(m);
        System.Reflection.ParameterInfo[] ps = m.GetParameters();
        Assert.Equal(2, ps.Length);
        Assert.Equal(typeof(DestinationFacts), ps[0].ParameterType.GetElementType());
        Assert.Equal(typeof(UnitFacts), ps[1].ParameterType.GetElementType());
        Assert.True(ps[0].IsIn);
        Assert.True(ps[1].IsIn);
        Assert.True(m.IsStatic);
        Assert.Equal(typeof(DeliverEval), m.ReturnType);
    }

    [Fact]
    [Trait("Id", "M-R5-1c")]
    public void Ac2_RequiresUnit_UnitMismatchMisdelivers_AndMatchAccepts()
    {
        AddressId house = AddressId.Pack(1, 1, 7, 0);
        AddressId unit3 = AddressId.Pack(1, 1, 7, 3);
        AddressId unit4 = AddressId.Pack(1, 1, 7, 4);
        DestinationFacts d = Facts(unit3, mask: 1UL, requiresUnit: true);

        Assert.Equal(EvalResult.Accept, Eval(d, unit3).Result);
        Assert.Equal(EvalResult.Misdeliver, Eval(d, unit4).Result);
        Assert.Equal(EvalResult.Misdeliver, Eval(d, house).Result);
    }

    [Fact]
    [Trait("Id", "M-R5-1c")]
    public void Ac2_WithoutRequiresUnit_UnitMismatchStillAccepts_ButAnotherNumberMisdelivers()
    {
        DestinationFacts d = Facts(AddressId.Pack(1, 1, 7, 3), mask: 1UL, requiresUnit: false);

        Assert.Equal(EvalResult.Accept, Eval(d, AddressId.Pack(1, 1, 7, 4)).Result);
        Assert.Equal(EvalResult.Accept, Eval(d, AddressId.Pack(1, 1, 7, 0)).Result);
        Assert.Equal(EvalResult.Misdeliver, Eval(d, AddressId.Pack(1, 1, 8, 3)).Result);
    }

    [Fact]
    [Trait("Id", "M-PERF-1a")]
    public void Ac3_PredicateChecks_AreAtMostSix_PerCall_InInstrumentedBuilds()
    {
        Assert.SkipUnless(
            TestBuildExpectation.SimAssertsExpected,
            "The predicate counter exists only in SIM_ASSERT builds (Debug / ReleaseAssert).");
#if PM_EXPECT_SIM_ASSERTS
        for (int index = 0; index < CaseCount; index++)
        {
            Case c = Make(index);
            DestinationFacts d = c.Dest;
            UnitFacts u = c.Unit;

            DeliverEval counted = MailRules.EvaluateCounted(in d, in u, out int checks);

            Assert.InRange(checks, 1, MailRules.MaxPredicateChecks);
            Assert.Equal(c.Result, counted.Result);
            Assert.Equal(c.Reason, counted.Reason);
        }
#endif
    }

    private static DeliverEval Eval(DestinationFacts d, AddressId unitAddress)
    {
        var u = new UnitFacts(DefIndex.FromValue(0), ItemCategory.Mail, unitAddress, 0, 0, MailFlags.None);
        return MailRules.Evaluate(in d, in u);
    }

    private static DestinationFacts Facts(AddressId address, ulong mask, bool requiresUnit) =>
        new DestinationFacts(
            EntityId.Compose(EntityClass.Destination, 1), address, DefIndex.FromValue(0), DestState.Open, mask, requiresUnit);

    private static DestinationFacts OpenFacts(ulong mask, bool requiresUnit) => Facts(AddressId.Pack(1, 1, 7), mask, requiresUnit);

    /// <summary>The registry facts for the first lot of the M-1a street; <paramref name="open"/> and the unlocked mask are set first.</summary>
    private static DestinationFacts RegistryFacts(bool open, ulong unlockedMask, out ContentSet content)
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        WorldTables world = b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        content = BuiltInContent.CreateM1a();
        var registry = new DestinationRegistry(world, content);
        registry.Deliveries.Set(open, closeTick: 100, unlockedMask);
        Assert.True(registry.TryGetDestinationFacts(world.Lots[0].Destination, out DestinationFacts f));
        return f;
    }

    private static Case Make(int index)
    {
        switch (index)
        {
            case 0: // CLOSED
            {
                DestinationFacts d = RegistryFacts(false, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
                return new Case(d, Mail(c, d.Address), EvalResult.Reject, MailRejectReason.Closed);
            }

            case 1: // LOCKED (deliveries open, only District 2 unlocked)
            {
                DestinationFacts d = RegistryFacts(true, 1UL << 1, out ContentSet c);
                return new Case(d, Mail(c, d.Address), EvalResult.Reject, MailRejectReason.NotOnRoute);
            }

            case 2: // closed AND locked: closed wins
            {
                DestinationFacts d = RegistryFacts(false, 1UL << 1, out ContentSet c);
                return new Case(d, Mail(c, d.Address), EvalResult.Reject, MailRejectReason.Closed);
            }

            case 3: // a tool
            {
                DestinationFacts d = RegistryFacts(true, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
                UnitFacts u = new UnitFacts(KindOf(c, d), ItemCategory.Tool, d.Address, 0, 0, MailFlags.None);
                return new Case(d, u, EvalResult.Reject, MailRejectReason.WontFit);
            }

            case 4: // a kind outside the type's accepts (hand-built facts with a mask that excludes it)
            {
                DestinationFacts reg = RegistryFacts(true, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
                DefIndex kind = KindOf(c, reg);
                ulong without = reg.AcceptsMask & ~(1UL << kind.Value);
                var d = new DestinationFacts(reg.Id, reg.Address, reg.Type, DestState.Open, without, reg.RequiresUnit);
                return new Case(d, Mail(c, d.Address), EvalResult.Reject, MailRejectReason.WontFit);
            }

            case 5: // same address
            {
                DestinationFacts d = RegistryFacts(true, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
                return new Case(d, Mail(c, d.Address), EvalResult.Accept, MailRejectReason.None);
            }

            default: // another address
            {
                DestinationFacts d = RegistryFacts(true, DeliveriesState.DefaultUnlockedMask, out ContentSet c);
                AddressId other = AddressId.Pack(1, 1, (int)d.Address.Number + 1);
                return new Case(d, Mail(c, other), EvalResult.Misdeliver, MailRejectReason.None);
            }
        }
    }

    private static DefIndex KindOf(ContentSet content, DestinationFacts d) => content.DestinationTypes[d.Type].Accepts[0];

    private static UnitFacts Mail(ContentSet content, AddressId address)
    {
        DestinationTypeDef house = content.DestinationTypes.AsSpan()[0];
        return new UnitFacts(house.Accepts[0], ItemCategory.Mail, address, 5, 0, MailFlags.None);
    }
}
