using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using static PerformativeMail.Sim.Tests.Unit.Input.InputFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Input;

// Inside the namespace so it wins over the PerformativeMail.Sim.Delivery namespace (see InputFixtures).
using Delivery = PerformativeMail.Client.Core.Input.Delivery;

/// <summary>
/// input-controls story 014: entering <c>aiming</c>, the aim-start refusals, F11 on the ms clock (H-F11-1, H-EC-14), the Throw
/// commit dispatched in the release's frame (H-R11-2) and the router-origin cancels (G2), through <see cref="InputRouter.Feed"/>,
/// <see cref="InputRouter.DispatchImmediates"/>, <see cref="InputRouter.FlushToLatch"/> and the story 015 / 016 / 018 cancel hooks.
/// </summary>
public class ThrowAimTests
{
    private const ushort Transition = 0;   // InputRouter.CauseAction

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveThrowPress_EntersAiming_SetsTheAimBit_EmitsAnAimPress_AndLogsTheTransition(bool pad)
    {
        // Arrange
        var rig = new AimRig();

        // Act: RMB, or LT squeezed past F4's on threshold.
        rig.Feed(pad ? rig.H.Axis(PadLeftTrigger, short.MaxValue) : rig.H.Press(MouseRight));
        rig.Router.FlushToLatch(rig.H.Latch);

        // Assert
        Assert.Equal(WorldAimingId, rig.Router.State);
        Assert.Equal(FrameActions.Bit(FrameAction.Aim), rig.H.Latch.LastState.HeldBits);
        Assert.Equal([(FrameAction.Aim, EdgeKind.Press, WorldId)], rig.H.Latch.Edges.Select(e => (e.Action, e.Kind, e.RouterStateId)));
        Assert.Contains(rig.Entries(), e => e.Kind == LogKind.Transition && e.Reason == Transition && e.IdA == ActionId.Throw && e.IdB == WorldId && e.IdC == WorldAimingId);
    }

    [Fact]
    public void ThrowPress_WithNotThrowable_StartsNoAim_LogsTheReasonCue_AndIsMutedUntilRelease()
    {
        // Arrange: the hands slot is selected (Throwing's reason id 1).
        var rig = new AimRig();
        rig.H.ThrowAim.NotThrowable = 1;

        // Act: press; the reason clears while RMB is still down; release at 1 s.
        rig.Feed(rig.H.Press(MouseRight));
        rig.H.ThrowAim.NotThrowable = 0;
        rig.H.Clock.SetSubTickOffset(1000);
        rig.Feed(rig.H.Release(MouseRight));
        rig.Router.DispatchImmediates();
        rig.Router.FlushToLatch(rig.H.Latch);

        // Assert: no state change, no edge, no throw; one cue with the reason id; the release swallowed.
        Assert.Equal(WorldId, rig.Router.State);
        Assert.Empty(rig.H.Latch.Edges);
        Assert.Empty(rig.H.Immediates.Calls);
        UiEventEntry cue = Assert.Single(rig.Entries(), e => e.Kind == LogKind.Cue);
        Assert.Equal(((byte)CueId.NotThrowable, ActionId.Throw, (ushort)CueWording.Kbm, WorldId, (byte)1), (cue.Reason, cue.IdA, cue.IdB, cue.IdC, cue.Flags));
        Assert.DoesNotContain(rig.Entries(), e => e.Kind is LogKind.GateFired or LogKind.Transition);
        Assert.Contains(rig.Entries(), e => e.Kind == LogKind.GateSwallowed && e.IdA == ActionId.Throw);
    }

    [Theory]
    [Trait("Id", "H-F11-1")]
    [Trait("Id", "H-R11-2")]
    [InlineData(249, 250, 500, false)]
    [InlineData(250, 250, 500, true)]
    [InlineData(160, 250, 500, false)]
    [InlineData(300, 250, 500, true)]
    [InlineData(160, 150, 500, true)]
    [InlineData(250, 250, 1000, true)]
    [InlineData(0, 250, 500, false)]   // press and release stamped in one frame: aimHeld ≈ 0
    public void Release_CommitsExactlyWhenAimHeldReachesThrowMinAim_NeverScaledByHoldSetting(int aimHeldMs, int minAimMs, int holdSettingMs, bool throws)
    {
        // Arrange
        InputSettings settings = InputSettings.Defaults
            .With(SettingsField.ThrowMinAimMs, minAimMs)
            .With(SettingsField.HoldSettingMs, holdSettingMs);
        var rig = new AimRig(settings);

        // Act
        rig.Feed(rig.H.Press(MouseRight));
        rig.H.Clock.SetSubTickOffset(aimHeldMs);
        rig.Feed(rig.H.Release(MouseRight));
        rig.Router.DispatchImmediates();
        rig.Router.FlushToLatch(rig.H.Latch);

        // Assert: Release + one Throw immediate, or Cancel + "Hold to aim" and the sink never called; aiming and the bit end.
        Assert.Equal(
            [(FrameAction.Aim, EdgeKind.Press, EdgeReason.None), (FrameAction.Aim, throws ? EdgeKind.Release : EdgeKind.Cancel, EdgeReason.None)],
            rig.H.Latch.Edges.Select(e => (e.Action, e.Kind, e.Reason)));
        Assert.Equal(throws ? 1 : 0, rig.H.Immediates.Calls.Count(c => c.ActionId == ActionId.Throw));
        Assert.Equal(!throws, rig.Entries().Any(e => e.Kind == LogKind.Cue && e.Reason == (byte)CueId.HoldToAim && e.IdA == ActionId.Throw));
        Assert.Equal(0UL, rig.H.Latch.LastState.HeldBits);
        Assert.Equal(WorldId, rig.Router.State);
    }

    [Fact]
    [Trait("Id", "H-EC-14")]
    public void ThrowHeldTenSeconds_HasNoTimeout_AndThrowsOnRelease()
    {
        // Arrange
        var rig = new AimRig();
        rig.Feed(rig.H.Press(MouseRight));

        // Act: 300 ticks of flushes with RMB down, then the release.
        for (int tick = 1; tick <= 300; tick++)
        {
            rig.H.Clock.SetTick(tick);
            rig.Router.DispatchImmediates();
            rig.Router.FlushToLatch(rig.H.Latch);
        }

        bool aimingAtTenSeconds = rig.Router.State == WorldAimingId;
        rig.Feed(rig.H.Release(MouseRight));
        rig.Router.DispatchImmediates();

        // Assert
        Assert.True(aimingAtTenSeconds);
        Assert.Equal([(ActionId.Throw, EdgeKind.Release, 10_000L)], rig.H.Immediates.Calls.Select(c => (c.ActionId, c.Edge, c.ArrivalMs)));
    }

    [Fact]
    [Trait("Id", "H-R11-2")]
    public void ReleaseFedAndDispatchedInOneFrame_CallsTheThrowSinkOnce_BeforeFlushToLatch()
    {
        // Arrange: aiming for 300 ms.
        var rig = new AimRig();
        rig.Feed(rig.H.Press(MouseRight));
        rig.Router.FlushToLatch(rig.H.Latch);
        rig.H.Latch.Clear();
        rig.H.Clock.SetSubTickOffset(300);

        // Act: the release frame.
        rig.Feed(rig.H.Release(MouseRight));
        rig.Router.DispatchImmediates();
        int flushesBeforeDispatch = rig.H.Latch.States.Count;
        rig.Router.DispatchImmediates();
        rig.Router.FlushToLatch(rig.H.Latch);

        // Assert: one commit, gated in aiming, before the frame's flush.
        Assert.Equal(0, flushesBeforeDispatch);
        Assert.Equal([new RecordingImmediateSink.ImmediateCall(ActionId.Throw, EdgeKind.Release, 300, WorldAimingId, 0, 0)], rig.H.Immediates.Calls);
        Assert.Equal([(FrameAction.Aim, EdgeKind.Release)], rig.H.Latch.Edges.Select(e => (e.Action, e.Kind)));
    }

    /// <summary>The G2 router-origin cancels, each started 100 ms into an aim; Busy is story 015's pending commit.</summary>
    public static TheoryData<string, EdgeReason> RouterCancels() => new()
    {
        { "back", EdgeReason.Back },
        { "state", EdgeReason.StateChange },
        { "focus", EdgeReason.FocusLost },
        { "device", EdgeReason.DeviceLost },
        { "stale", EdgeReason.EntryStale },
        { "busy", EdgeReason.Busy },
    };

    [Theory]
    [MemberData(nameof(RouterCancels))]
    public void RouterOriginCancel_EmitsCancelWithItsReason_NeverThrows_AndSwallowsTheRelease(string cause, EdgeReason reason)
    {
        // Arrange
        var rig = new AimRig();
        rig.Feed(rig.H.Press(MouseRight));
        rig.H.Clock.SetSubTickOffset(100);

        // Act: the cancel, then RMB released at 1 s.
        switch (cause)
        {
            case "back":
                Assert.True(rig.Router.TryCancelAim());
                break;
            case "state":
                rig.H.Carrier.Set(new CarrierStatus { CursorHolding = true });   // Holding-closed: Throw is not live
                break;
            case "focus":
                rig.Router.CancelAim(AimCancelReason.FocusLost);
                break;
            case "device":
                rig.Router.CancelAim(AimCancelReason.DeviceLost);
                break;
            case "stale":
                rig.H.ThrowAim.EntryStale = true;
                break;
            default:
                rig.Router.CancelAim(AimCancelReason.Busy);
                break;
        }

        rig.Router.FlushToLatch(rig.H.Latch);
        rig.H.Carrier.Set(default);
        rig.H.Clock.SetSubTickOffset(1000);
        rig.Feed(rig.H.Release(MouseRight));
        rig.Router.DispatchImmediates();
        rig.Router.FlushToLatch(rig.H.Latch);

        // Assert
        Assert.Equal(
            [(FrameAction.Aim, EdgeKind.Press, EdgeReason.None), (FrameAction.Aim, EdgeKind.Cancel, reason)],
            rig.H.Latch.Edges.Select(e => (e.Action, e.Kind, e.Reason)));
        Assert.Empty(rig.H.Immediates.Calls);
        Assert.DoesNotContain(rig.Entries(), e => e.Kind == LogKind.Cue);
        Assert.Contains(rig.Entries(), e => e.Kind == LogKind.GateSwallowed && e.IdA == ActionId.Throw);
        Assert.Equal(0UL, rig.H.Latch.LastState.HeldBits);
        Assert.Equal(WorldId, rig.Router.State);
    }
}

/// <summary>A router over the M-1a map with every immediate and the Throw commit sunk into the harness recorder (story 014 tests).</summary>
internal sealed class AimRig
{
    private readonly UiEventLog _log = new();

    public AimRig(InputSettings? settings = null)
    {
        ActionMap map = BuiltInActionMap.CreateM1a();
        Router = new InputRouter(map, settings ?? InputSettings.Defaults, H.Clock, H.Interaction, H.Presented, H.Carrier, H.ThrowAim, H.Ticks, _log);
        foreach (ActionDef d in map.All)
        {
            if (d.Delivery == Delivery.Immediate || d.CommitOnRelease)
            {
                Router.RegisterSink(d.Id, H.Immediates);
            }
        }
    }

    public InputTestHarness H { get; } = new();

    public InputRouter Router { get; }

    public void Feed(RawInputRecord r) => Router.Feed(in r);

    public List<UiEventEntry> Entries()
    {
        var list = new List<UiEventEntry>();
        for (int i = 0; i < _log.Count; i++)
        {
            _ = _log.TryGet(i, out UiEventEntry e);
            list.Add(e);
        }

        return list;
    }
}
