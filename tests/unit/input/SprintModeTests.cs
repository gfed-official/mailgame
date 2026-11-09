using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 012: Sprint per-device modes Hold / Toggle / Auto (H-A11Y-1 sprint class), the Toggle latch and its
/// unlatch causes (H-G7-3 sprint half) and F8 counted in client ticks (H-F8-1), read from the flushed <c>HeldBits</c>.
/// </summary>
public class SprintModeTests
{
    private static readonly ulong _sprint = FrameActions.Bit(FrameAction.Sprint);

    private readonly InputTestHarness _h = new();
    private InputRouter _router;

    public SprintModeTests()
    {
        _router = Router(InputSettings.Defaults.With(SettingsField.HoldModeSprintKbm, (int)HoldMode.Toggle));
    }

    public static TheoryData<CarrierStatus> UnlatchCauses => new()
    {
        new CarrierStatus { Life = LifeState.Dead, Body = BodyContext.Dead },
        new CarrierStatus { Body = BodyContext.Mounted },
        new CarrierStatus { Swimming = true },
        new CarrierStatus { PanelOpen = true },
    };

    [Theory]
    [Trait("Id", "H-G7-3")]
    [MemberData(nameof(UnlatchCauses))]
    public void ToggleSprint_OnePressLatches_TheNextUnlatches_AndDeathMountSwimAndPanelOpenUnlatch(CarrierStatus cause)
    {
        // Arrange / Act: press latches (the release is ignored), the next press unlatches.
        _router.Feed(_h.Press(KeyW));
        Tap(KeyShift);
        bool afterFirst = Flush();
        Tap(KeyShift);
        bool afterSecond = Flush();

        // Latch again; the cause is published, then cleared.
        Tap(KeyShift);
        _h.Carrier.Set(in cause);
        bool underCause = Flush();
        _h.Carrier.Set(default);
        bool afterCause = Flush();

        // Assert
        Assert.Equal((true, false, false, false), (afterFirst, afterSecond, underCause, afterCause));
    }

    [Theory]
    [Trait("Id", "H-F8-1")]
    [InlineData(3, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(12, false)]
    public void ToggleSprint_UnlatchesAfterEightNeutralClientTicks_CountedInTicksNeverMs(int neutralTicks, bool latched)
    {
        // Arrange: moving with sprint latched.
        _router.Feed(_h.Press(KeyW));
        Tap(KeyShift);
        _ = Flush();

        // Act: the stick passes through neutral for n client ticks, two rendered frames per tick 1 s apart, then moves again.
        _router.Feed(_h.Release(KeyW));
        for (int t = 0; t < neutralTicks; t++)
        {
            _h.Ticks.ClientTick++;
            _ = Flush();
            _h.Clock.AdvanceTicks(30);
            _ = Flush();
        }

        _router.Feed(_h.Press(KeyW));
        _h.Ticks.ClientTick++;
        bool final = Flush();

        // Assert
        Assert.Equal(latched, final);
    }

    [Theory]
    [Trait("Id", "H-A11Y-1")]
    [InlineData(false, HoldMode.Hold)]
    [InlineData(false, HoldMode.Toggle)]
    [InlineData(false, HoldMode.Auto)]
    [InlineData(true, HoldMode.Toggle)]
    public void Sprint_TurnsOnAndOffUnderEachMode(bool pad, HoldMode mode)
    {
        // Arrange: full forward move on the device, its Sprint input under the mode.
        _router = Router(InputSettings.Defaults.With(pad ? SettingsField.HoldModeSprintPad : SettingsField.HoldModeSprintKbm, (int)mode));
        PhysicalInput sprintInput = pad ? PadLeftStickClick : KeyShift;
        if (pad)
        {
            _router.Feed(_h.Axis(PadLeftStickY, short.MinValue));
        }
        else
        {
            _router.Feed(_h.Press(KeyW));
        }

        // Act: Hold sprints while down; Toggle on one press, off on the next; Auto sprints while moving and walks while held.
        bool idle = Flush();
        _router.Feed(_h.Press(sprintInput));
        bool down = Flush();
        _router.Feed(_h.Release(sprintInput));
        bool up = Flush();
        Tap(sprintInput);
        bool again = Flush();

        // Assert
        (bool, bool, bool, bool) expected = mode switch
        {
            HoldMode.Hold => (false, true, false, false),
            HoldMode.Toggle => (false, true, true, false),
            _ => (true, false, true, true),
        };
        Assert.Equal(expected, (idle, down, up, again));
    }

    private InputRouter Router(InputSettings settings) =>
        new(BuiltInActionMap.CreateM1a(), settings, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.Ticks, new UiEventLog());

    private void Tap(PhysicalInput input)
    {
        _router.Feed(_h.Press(input));
        _router.Feed(_h.Release(input));
    }

    private bool Flush()
    {
        _router.FlushToLatch(_h.Latch);
        return (_h.Latch.LastState.HeldBits & _sprint) != 0;
    }
}
