using System.Collections.Immutable;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The M-1a action map as code (ADR-0012 section 2, like <c>BuiltInContent</c>): the GDD R6 "M-1a" rows as
/// <see cref="ActionDef"/>s, loaded through the same <see cref="ActionMap.TryLoad(IReadOnlyList{ActionDef}, out ActionMap, out ValidationReport)"/>
/// as any other map (field checks plus the reachable-state conflict pass).
/// <para>
/// Capture is data: every predicate pins capture to <see cref="CaptureMask.None"/> (the GDD shorthands leave it open) except
/// Pause and Back, which are live in every state including Pause and TextEntry capture. Rows are written as the GDD writes
/// them. The R5 Back-ladder rows that share Back's input (Pause on Esc, Close panel and Cancel / return on pad B) carry
/// <see cref="InputBinding.WithBackLadderStep"/>, so the conflict validator (story 006) counts them as Back, once. "Mounted on KBM only" for Shoulder swap is not expressible as a device-independent predicate and waits for M1
/// vehicles. <see cref="DeferredRows"/> lists the R6 rows that are not M-1a so a later milestone moves them deliberately.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ActionMap map = BuiltInActionMap.CreateM1a();
/// ActionDef sprint = map.Get(ActionId.Sprint)!;
/// bool known = map.Resolve("quick_ping") is null;   // deferred rows are absent
/// </code>
/// </example>
public static class BuiltInActionMap
{
    private const string PlayerCharacter = "design/gdd/player-character-movement.md";
    private const string Throwing = "design/gdd/throwing-catching.md";
    private const string Grid = "design/gdd/grid-inventory.md";
    private const string InputGdd = "design/gdd/input-controls.md";

    // The GDD shorthands, with capture pinned to None.
    private static readonly LivePredicate _world = NamedStates.World;
    private static readonly LivePredicate _worldNotAiming = NamedStates.WorldNotAiming;
    private static readonly LivePredicate _holdingClosed = NamedStates.HoldingClosed with { CaptureMask = CaptureMask.None };
    private static readonly LivePredicate _panel = NamedStates.Panel with { CaptureMask = CaptureMask.None };
    private static readonly LivePredicate _holdingOpen = NamedStates.HoldingOpen with { CaptureMask = CaptureMask.None };

    // Panel or Holding-open: the overlay is a Panel, held or not.
    private static readonly LivePredicate _panelAny = LivePredicate.Any with { CaptureMask = CaptureMask.None, OverlayMask = OverlayMask.Panel };

    private static readonly LivePredicate _station = LivePredicate.Any with
    {
        CaptureMask = CaptureMask.None,
        OverlayMask = OverlayMask.Station,
        BodyMask = BodyMask.OnFoot,
    };

    private static readonly LivePredicate _mounted = LivePredicate.Any with
    {
        CaptureMask = CaptureMask.None,
        OverlayMask = OverlayMask.None,
        BodyMask = BodyMask.Mounted,
    };

    /// <summary>
    /// The R6 rows that are not M-1a (string ids, snake case): quick ping, comms wheel, chat, push-to-talk, history overlay,
    /// status read, action menu, external-panel focus switch, split / sort / take-all / page / jump-row rows, Drop in Panel
    /// (M1) and every row of the "Later contexts" table (vehicles, map, Build mode, turrets). <see cref="CreateM1a"/> registers none of them; a later milestone moves a row
    /// from here into the map on purpose.
    /// </summary>
    public static ImmutableArray<string> DeferredRows { get; } =
    [
        "quick_ping",
        "comms_wheel",
        "chat",
        "push_to_talk",
        "history_overlay",
        "status_read",
        "action_menu",
        "switch_focus",
        "split",
        "sort_by_address",
        "sort_by_size",
        "take_all",
        "drop_in_panel",
        "page_manifest",
        "jump_to_row",
        "vehicle_enter_exit",
        "vehicle_throttle",
        "vehicle_handbrake_horn",
        "map",
        "build_mode_toggle",
        "build_place",
        "build_rotate",
        "build_deconstruct",
        "build_pipette",
        "build_select",
        "build_belt_tier",
        "turret_mount_dismount",
        "turret_aim_fire",
    ];

    /// <summary>Builds and fully validates the M-1a map (field checks and the reachable-state pass). Allocates; call once at boot.</summary>
    /// <returns>The immutable map.</returns>
    /// <exception cref="InvalidOperationException">The built-in definitions fail validation (a programming error, including any conflict); the message is the report.</exception>
    /// <example><code>ActionMap map = BuiltInActionMap.CreateM1a();</code></example>
    public static ActionMap CreateM1a()
    {
        ImmutableArray<ActionDef> defs = Defs();
        if (!ActionMap.TryLoad(defs, out ActionMap? map, out ValidationReport report))
        {
            throw new InvalidOperationException("The built-in M-1a action map is invalid:\n" + report);
        }

        return map;
    }

    /// <summary>
    /// The M-1a definitions, unvalidated, in canonical order: the input of the boot entry point
    /// (<see cref="ActionMapValidator.ValidateForBoot(IReadOnlyList{ActionDef}, out ActionMap)"/>, story 028) and of test
    /// fixtures that change one row. Allocates.
    /// </summary>
    /// <returns>The definitions.</returns>
    /// <example><code>ValidationReport report = ActionMapValidator.ValidateForBoot(BuiltInActionMap.Definitions(), out ActionMap? map);</code></example>
    public static ImmutableArray<ActionDef> Definitions() => Defs();

    private static ImmutableArray<ActionDef> Defs()
    {
        InputBinding none = InputBinding.Unbound;
        InputBinding wasd = InputBinding.Composite4(KeyW, KeyS, KeyA, KeyD);
        InputBinding arrows = InputBinding.Composite4(KeyUp, KeyDown, KeyLeft, KeyRight);
        InputBinding dpad = InputBinding.Composite4(PadDpadUp, PadDpadDown, PadDpadLeft, PadDpadRight);
        InputBinding leftStick = InputBinding.AxisPair(PadLeftStickX, PadLeftStickY);
        InputBinding rightStick = InputBinding.AxisPair(PadRightStickX, PadRightStickY);
        InputBinding enter = InputBinding.Single(KeyEnter).WithNumpadEnterAlias();
        InputBinding shiftEnter = InputBinding.Chord(KeyShift, KeyEnter).WithNumpadEnterAlias();
        InputBinding S(PhysicalInput i) => InputBinding.Single(i);

        var list = ImmutableArray.CreateBuilder<ActionDef>();

        // On foot and carrying.
        list.Add(Make(ActionId.Move, "move", Grammar.Axis, Delivery.Frame, null, [_world, _holdingClosed, _panel, _station], wasd, none, leftStick, none, PlayerCharacter, required: true));
        list.Add(Make(ActionId.Look, "look", Grammar.Axis, Delivery.Frame, null, [_world, _holdingClosed, _mounted], S(MouseMotion), none, rightStick, none, PlayerCharacter));
        list.Add(Make(ActionId.Sprint, "sprint", Grammar.HeldState, Delivery.Frame, FrameAction.Sprint, [_world, _holdingClosed], S(KeyShift), none, S(PadLeftStickClick), none, PlayerCharacter));
        list.Add(Make(ActionId.Jump, "jump", Grammar.Tap, Delivery.Frame, FrameAction.Jump, [_world, _holdingClosed], S(KeySpace), none, S(PadA), none, PlayerCharacter));
        list.Add(Make(ActionId.Interact, "interact", Grammar.Hold, Delivery.Frame, FrameAction.Interact, [_worldNotAiming, _holdingClosed], S(KeyE), none, S(PadX), none, PlayerCharacter, required: true, targetDriven: true));
        list.Add(Make(ActionId.Throw, "throw", Grammar.AimRelease, Delivery.Frame, FrameAction.Aim, [_world], S(MouseRight), none, S(PadLeftTrigger), none, Throwing, required: true, commitOnRelease: true));
        list.Add(Make(ActionId.Tool, "tool", Grammar.Tap, Delivery.Immediate, null, [_worldNotAiming], S(MouseLeft), none, S(PadRightTrigger), none, PlayerCharacter));

        // Hotbar: eight local slot actions, one stepped Frame action for wheel and D-pad (a StepPair: previous / next in a single slot).
        list.Add(Make(ActionId.HotbarStep, "hotbar_step", Grammar.Stepped, Delivery.Frame, FrameAction.HotbarStep, [_worldNotAiming, _holdingClosed], InputBinding.StepPair(WheelUp, WheelDown), none, InputBinding.StepPair(PadDpadLeft, PadDpadRight), none, Grid));
        PhysicalInput[] digits = [KeyDigit1, KeyDigit2, KeyDigit3, KeyDigit4, KeyDigit5, KeyDigit6, KeyDigit7, KeyDigit8];
        for (int i = 0; i < digits.Length; i++)
        {
            list.Add(Make((ushort)(ActionId.HotbarSlot1 + i), $"hotbar_slot_{i + 1}", Grammar.Tap, Delivery.Immediate, null, [_worldNotAiming, _holdingClosed, _panel], S(digits[i]), none, none, none, Grid));
        }

        list.Add(Make(ActionId.Drop, "drop", Grammar.Tap, Delivery.Immediate, null, [_worldNotAiming, _holdingClosed], S(KeyQ), none, S(PadDpadDown), none, Grid));
        list.Add(Make(ActionId.ThrowWhileHolding, "throw_while_holding", Grammar.Tap, Delivery.Immediate, null, [_holdingClosed], S(MouseRight), none, S(PadLeftTrigger), none, Grid));
        list.Add(Make(ActionId.ToolWhileHolding, "tool_while_holding", Grammar.Tap, Delivery.Immediate, null, [_holdingClosed], S(MouseLeft), none, S(PadRightTrigger), none, Grid));
        list.Add(Make(ActionId.OpenPanel, "open_panel", Grammar.Tap, Delivery.Immediate, null, [_world, _holdingClosed], S(KeyTab), none, S(PadY), none, Grid, required: true));
        list.Add(Make(ActionId.ShoulderSwap, "shoulder_swap", Grammar.Tap, Delivery.Immediate, null, [_world, _holdingClosed], S(KeyX), none, S(PadRightBumper), none, PlayerCharacter));

        // Live everywhere, captures included.
        list.Add(Make(ActionId.Pause, "pause", Grammar.Tap, Delivery.Immediate, null, [LivePredicate.Any], S(KeyEscape).WithBackLadderStep(), S(KeyP), S(PadMenu), none, InputGdd, reserved: [KeyEscape, PadMenu]));
        list.Add(Make(ActionId.Back, "back", Grammar.Tap, Delivery.Immediate, null, [LivePredicate.Any], S(KeyEscape), none, S(PadB), none, InputGdd, required: true, reserved: [KeyEscape]));

        // Panel and Holding-open.
        list.Add(Make(ActionId.GridCursor, "grid_cursor", Grammar.Stepped, Delivery.Immediate, null, [_panelAny], S(MouseMotion), arrows, rightStick, dpad, Grid));
        list.Add(Make(ActionId.PickPlace, "pick_place", Grammar.ClickOrDrag, Delivery.Immediate, null, [_panelAny], S(MouseLeft), enter, S(PadA), none, Grid, required: true));
        list.Add(Make(ActionId.Rotate, "rotate", Grammar.Tap, Delivery.Immediate, null, [_holdingOpen], S(KeyR), none, S(PadRightBumper), none, Grid));
        list.Add(Make(ActionId.CancelReturn, "cancel_return", Grammar.Tap, Delivery.Immediate, null, [_holdingOpen], S(MouseRight), none, S(PadB).WithBackLadderStep(), none, Grid));
        list.Add(Make(ActionId.QuickMove, "quick_move", Grammar.Tap, Delivery.Immediate, null, [_panel], InputBinding.Chord(KeyShift, MouseLeft), shiftEnter, S(PadX), none, Grid));
        list.Add(Make(ActionId.ClosePanel, "close_panel", Grammar.Tap, Delivery.Immediate, null, [_panelAny], S(KeyTab), none, S(PadB).WithBackLadderStep(), none, Grid));

        return list.ToImmutable();
    }

    private static ActionDef Make(
        ushort id,
        string stringId,
        Grammar grammar,
        Delivery delivery,
        FrameAction? bit,
        LivePredicate[] liveIn,
        InputBinding kbm1,
        InputBinding kbm2,
        InputBinding pad1,
        InputBinding pad2,
        string owner,
        bool required = false,
        PhysicalInput[]? reserved = null,
        bool targetDriven = false,
        bool commitOnRelease = false) => new()
        {
            Id = id,
            StringId = stringId,
            Grammar = grammar,
            Delivery = delivery,
            FrameBit = bit,
            LiveIn = [.. liveIn],
            KbmPrimary = kbm1,
            KbmSecondary = kbm2,
            PadPrimary = pad1,
            PadSecondary = pad2,
            OwnerGdd = owner,
            Required = required,
            Reserved = reserved is null ? [] : [.. reserved],
            Aliases = [],
            TargetDriven = targetDriven,
            CommitOnRelease = commitOnRelease,
        };
}
