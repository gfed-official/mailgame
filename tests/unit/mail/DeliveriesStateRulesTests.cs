using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Mail-destinations story 003, AC-4 (M-R4-2 at the facts level): the effective destination state and the closing grace.
/// </summary>
public class DeliveriesStateRulesTests
{
    private const uint T = 1000u;
    private const int Grace = 90;

    [Fact]
    [Trait("Id", "M-R4-2a")]
    public void ActionStartedBeforeCloseAndAppliedBeforeClose_WhileOpen_IsOpen()
    {
        Assert.Equal(DestState.Open, DeliveriesStateRules.Effective(true, true, T, T - 1, T - 1, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R4-2b")]
    public void ActionStartedAtTheCloseTick_AfterClose_IsClosed()
    {
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(true, false, T, T, T, Grace, 0u));
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(true, false, T, T + 5, T + 5, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R4-2c")]
    public void ActionStartedBeforeClose_IsOpenUpToCloseTickPlusGrace_AndClosedAfter()
    {
        Assert.Equal(DestState.Open, DeliveriesStateRules.Effective(true, false, T, T - 1, T + Grace, Grace, 0u));
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(true, false, T, T - 1, T + Grace + 1, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R4-2e")]
    public void NoStartTick_FeederOrNpc_AtTheCloseTick_IsClosed()
    {
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(true, false, T, null, T, Grace, 0u));
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(true, false, T, null, T - 1, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R4-2f")]
    public void ActionAtDeliveryStartTick_WithDeliveriesOpen_IsOpen()
    {
        const uint S = 500u;

        Assert.Equal(DestState.Open, DeliveriesStateRules.Effective(true, true, T, S, S, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R5-1a")]
    public void LockedDistrict_WhileOpen_IsLocked()
    {
        Assert.Equal(DestState.Locked, DeliveriesStateRules.Effective(false, true, T, T - 1, T - 1, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R5-1a")]
    public void LockedDistrict_WhileClosed_IsClosed_ClosedWins()
    {
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(false, false, T, null, T + 1, Grace, 0u));
        Assert.Equal(DestState.Closed, DeliveriesStateRules.Effective(false, false, T, T - 1, T + Grace + 1, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R4-2c")]
    public void LockedDistrict_WithinGrace_IsLocked()
    {
        Assert.Equal(DestState.Locked, DeliveriesStateRules.Effective(false, false, T, T - 1, T + 10, Grace, 0u));
    }

    [Fact]
    [Trait("Id", "M-R4-2c")]
    public void GraceBound_DoesNotOverflow_NearTheTopOfTheTickRange()
    {
        Assert.Equal(DestState.Open, DeliveriesStateRules.Effective(true, false, uint.MaxValue - 10u, 5u, uint.MaxValue, Grace, 0u));
    }

    // Mail R4, "an action is judged by when it started": a start while closed stays closed after deliveries reopen (Mail Review
    // 6736073794's row first), while an action started in the open window and a feeder while open are unchanged.
    [Theory]
    [Trait("Id", "M-R4-2")]
    [InlineData(true, 100u, 150u, 200u, 180u, DestState.Closed)]
    [InlineData(false, T, T - 1, T + Grace, 500u, DestState.Open)]
    [InlineData(true, T, null, T - 1, 500u, DestState.Open)]
    public void Effective_JudgesAnActionByItsStart(bool openNow, uint closeTick, uint? start, uint apply, uint openSince, DestState expected)
    {
        Assert.Equal(expected, DeliveriesStateRules.Effective(true, openNow, closeTick, start, apply, Grace, openSince));
    }
}
