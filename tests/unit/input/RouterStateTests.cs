using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>input-controls story 004 AC "State value type": <see cref="RouterState"/> round-trips through <see cref="RouterStateInfo"/>, unreachable ids are representable, equality and the debug string.</summary>
[Trait("Id", "TR-input-003")]
public class RouterStateTests
{
    [Fact]
    public void EveryId_RoundTripsThroughRouterStateAndRouterStateInfo()
    {
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            ushort id = (ushort)i;

            Assert.True(RouterState.TryFromId(id, out RouterState state));
            Assert.Equal(id, state.Id);
            Assert.Equal(
                id,
                RouterStateInfo.Pack(state.Capture, state.Overlay, state.Body, state.Holding, state.Aiming, state.Submenu));
        }
    }

    [Fact]
    public void Constructor_ExposesTheSixLayers()
    {
        var state = new RouterState(CaptureLayer.Pause, OverlayLayer.Panel, BodyLayer.OnFoot, holding: true, aiming: false, SubmenuLayer.SplitStepper);

        Assert.Equal(CaptureLayer.Pause, state.Capture);
        Assert.Equal(OverlayLayer.Panel, state.Overlay);
        Assert.Equal(BodyLayer.OnFoot, state.Body);
        Assert.True(state.Holding);
        Assert.False(state.Aiming);
        Assert.Equal(SubmenuLayer.SplitStepper, state.Submenu);
        Assert.Equal(
            RouterStateInfo.Pack(CaptureLayer.Pause, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.SplitStepper),
            state.Id);
    }

    [Fact]
    public void TryFromId_RejectsAnInvalidIdAndReturnsTheDefaultState()
    {
        bool ok = RouterState.TryFromId(RouterStateInfo.StateCount, out RouterState state);

        Assert.False(ok);
        Assert.Equal(default, state);
        Assert.Equal((ushort)0, state.Id);
    }

    [Fact]
    public void UnreachableState_IsRepresentableButNotReachable()
    {
        var aimingInPanel = new RouterState(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, holding: false, aiming: true, SubmenuLayer.None);

        Assert.True(RouterStateInfo.IsValid(aimingInPanel.Id));
        Assert.True(RouterState.TryFromId(aimingInPanel.Id, out RouterState back));
        Assert.Equal(aimingInPanel, back);
        Assert.False(aimingInPanel.IsReachable());
        Assert.False(RouterState.IsReachable(aimingInPanel.Id));
    }

    [Fact]
    public void ReachableState_ReportsReachable()
    {
        var world = default(RouterState);
        var holdingOpen = new RouterState(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.None);

        Assert.True(world.IsReachable());
        Assert.True(holdingOpen.IsReachable());
    }

    [Fact]
    public void IsReachable_IsFalseForAnInvalidId()
    {
        Assert.False(RouterState.IsReachable(RouterStateInfo.StateCount));
        Assert.False(RouterState.IsReachable(ushort.MaxValue));
    }

    [Fact]
    public void Equality_IsByValueAcrossAllSixLayers()
    {
        var a = new RouterState(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.None);
        var same = new RouterState(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.None);
        RouterState[] differing =
        [
            new(CaptureLayer.Pause, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.None),
            new(CaptureLayer.None, OverlayLayer.Map, BodyLayer.OnFoot, true, false, SubmenuLayer.None),
            new(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.Dead, true, false, SubmenuLayer.None),
            new(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, false, false, SubmenuLayer.None),
            new(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, true, SubmenuLayer.None),
            new(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.ActionMenu),
        ];

        Assert.True(a == same);
        Assert.True(a.Equals((object)same));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        foreach (RouterState other in differing)
        {
            Assert.True(a != other);
            Assert.False(a.Equals(other));
        }

        Assert.False(a.Equals("not a state"));
    }

    [Fact]
    public void GetHashCode_IsTheIdAndUniqueOverTheTupleSpace()
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < RouterStateInfo.StateCount; i++)
        {
            Assert.True(RouterState.TryFromId((ushort)i, out RouterState state));
            Assert.Equal(i, state.GetHashCode());
            Assert.True(seen.Add(state.GetHashCode()));
        }
    }

    [Fact]
    public void ToDebugString_NamesTheIdAndEveryLayer()
    {
        var state = new RouterState(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, true, false, SubmenuLayer.ActionMenu);
        ushort id = state.Id;

        string text = state.ToDebugString();

        Assert.Equal($"{id:D4} cap=None ovl=Panel body=OnFoot hold=1 aim=0 sub=ActionMenu", text);
        Assert.Equal(text, RouterState.ToDebugString(id));
        Assert.Equal(text, state.ToString());
    }

    [Fact]
    public void ToDebugString_OfAnInvalidIdSaysSo()
    {
        Assert.Equal("invalid(1008)", RouterState.ToDebugString(RouterStateInfo.StateCount));
        Assert.Equal("invalid(65535)", RouterState.ToDebugString(ushort.MaxValue));
    }
}
