using System.Collections.Immutable;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;
using static PerformativeMail.Client.Core.Input.PhysicalInput;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 005 AC "H-R6-1 (M-1a rows)" plus "Required / reserved", "Deferred rows are absent" and the frame-bit
/// rows of "Frame bits and delivery": <see cref="BuiltInActionMap.CreateM1a"/> equals the GDD R6 table row by row (one
/// theory case per row, so a failure names the row), and every live-in set is compared state by state over the whole
/// reachable set against an independent restatement of the GDD shorthand.
/// </summary>
[Trait("Id", "H-R6-1")]
public class DefaultBindingsTests
{
    private sealed record Row(
        string Id,
        ushort NumericId,
        Grammar Grammar,
        Delivery Delivery,
        FrameAction? Bit,
        InputBinding Kbm1,
        InputBinding Kbm2,
        InputBinding Pad1,
        InputBinding Pad2,
        Func<RouterState, bool> Live,
        bool Required = false,
        PhysicalInput[]? Reserved = null);

    // Independent restatements of the GDD R2 shorthands (capture None is the data decision for every action but Pause and Back).
    private static bool NoCapture(RouterState s) => s.Capture == CaptureLayer.None;

    private static bool WorldS(RouterState s) => NoCapture(s) && s.Overlay == OverlayLayer.None && s.Body == BodyLayer.OnFoot && !s.Holding;

    private static bool WorldNotAimingS(RouterState s) => WorldS(s) && !s.Aiming;

    private static bool HoldingClosedS(RouterState s) => NoCapture(s) && s.Overlay == OverlayLayer.None && s.Body == BodyLayer.OnFoot && s.Holding;

    private static bool PanelS(RouterState s) => NoCapture(s) && s.Overlay == OverlayLayer.Panel && !s.Holding;

    private static bool HoldingOpenS(RouterState s) => NoCapture(s) && s.Overlay == OverlayLayer.Panel && s.Holding;

    private static bool StationS(RouterState s) => NoCapture(s) && s.Overlay == OverlayLayer.Station && s.Body == BodyLayer.OnFoot;

    private static bool MountedS(RouterState s) => NoCapture(s) && s.Overlay == OverlayLayer.None && s.Body == BodyLayer.Mounted;

    private static InputBinding S(PhysicalInput i) => InputBinding.Single(i);

    private static readonly InputBinding _none = InputBinding.Unbound;

    private const string PlayerCharacter = "design/gdd/player-character-movement.md";
    private const string Throwing = "design/gdd/throwing-catching.md";
    private const string Grid = "design/gdd/grid-inventory.md";
    private const string InputGdd = "design/gdd/input-controls.md";

    private static readonly ActionMap _map = BuiltInActionMap.CreateM1a();

    private static readonly Dictionary<string, Row> _rows = BuildRows();

    private static Dictionary<string, Row> BuildRows()
    {
        var wasd = InputBinding.Composite4(KeyW, KeyS, KeyA, KeyD);
        var arrows = InputBinding.Composite4(KeyUp, KeyDown, KeyLeft, KeyRight);
        var dpad = InputBinding.Composite4(PadDpadUp, PadDpadDown, PadDpadLeft, PadDpadRight);
        var ls = InputBinding.AxisPair(PadLeftStickX, PadLeftStickY);
        var rs = InputBinding.AxisPair(PadRightStickX, PadRightStickY);
        var enter = InputBinding.Single(KeyEnter).WithNumpadEnterAlias();
        var shiftEnter = InputBinding.Chord(KeyShift, KeyEnter).WithNumpadEnterAlias();

        var rows = new List<Row>
        {
            new("move", 1, Grammar.Axis, Delivery.Frame, null, wasd, _none, ls, _none, s => WorldS(s) || HoldingClosedS(s) || PanelS(s) || StationS(s), Required: true),
            new("look", 2, Grammar.Axis, Delivery.Frame, null, S(MouseMotion), _none, rs, _none, s => WorldS(s) || HoldingClosedS(s) || MountedS(s)),
            new("sprint", 3, Grammar.HeldState, Delivery.Frame, FrameAction.Sprint, S(KeyShift), _none, S(PadLeftStickClick), _none, s => WorldS(s) || HoldingClosedS(s)),
            new("jump", 4, Grammar.Tap, Delivery.Frame, FrameAction.Jump, S(KeySpace), _none, S(PadA), _none, s => WorldS(s) || HoldingClosedS(s)),
            new("interact", 5, Grammar.Hold, Delivery.Frame, FrameAction.Interact, S(KeyE), _none, S(PadX), _none, s => WorldNotAimingS(s) || HoldingClosedS(s), Required: true),
            new("throw", 6, Grammar.AimRelease, Delivery.Frame, FrameAction.Aim, S(MouseRight), _none, S(PadLeftTrigger), _none, WorldS, Required: true),
            new("tool", 7, Grammar.Tap, Delivery.Immediate, null, S(MouseLeft), _none, S(PadRightTrigger), _none, WorldNotAimingS),
            new("hotbar_step", 8, Grammar.Stepped, Delivery.Frame, FrameAction.HotbarStep, InputBinding.StepPair(WheelUp, WheelDown), _none, InputBinding.StepPair(PadDpadLeft, PadDpadRight), _none, s => WorldNotAimingS(s) || HoldingClosedS(s)),
            new("drop", 17, Grammar.Tap, Delivery.Immediate, null, S(KeyQ), _none, S(PadDpadDown), _none, s => WorldNotAimingS(s) || HoldingClosedS(s)),
            new("throw_while_holding", 18, Grammar.Tap, Delivery.Immediate, null, S(MouseRight), _none, S(PadLeftTrigger), _none, HoldingClosedS),
            new("tool_while_holding", 19, Grammar.Tap, Delivery.Immediate, null, S(MouseLeft), _none, S(PadRightTrigger), _none, HoldingClosedS),
            new("open_panel", 20, Grammar.Tap, Delivery.Immediate, null, S(KeyTab), _none, S(PadY), _none, s => WorldS(s) || HoldingClosedS(s), Required: true),
            new("shoulder_swap", 21, Grammar.Tap, Delivery.Immediate, null, S(KeyX), _none, S(PadRightBumper), _none, s => WorldS(s) || HoldingClosedS(s)),
            new("pause", 22, Grammar.Tap, Delivery.Immediate, null, S(KeyEscape).WithBackLadderStep(), S(KeyP), S(PadMenu), _none, _ => true, Reserved: [KeyEscape, PadMenu]),
            new("back", 23, Grammar.Tap, Delivery.Immediate, null, S(KeyEscape), _none, S(PadB), _none, _ => true, Required: true, Reserved: [KeyEscape]),
            new("grid_cursor", 24, Grammar.Stepped, Delivery.Immediate, null, S(MouseMotion), arrows, rs, dpad, s => PanelS(s) || HoldingOpenS(s)),
            new("pick_place", 25, Grammar.ClickOrDrag, Delivery.Immediate, null, S(MouseLeft), enter, S(PadA), _none, s => PanelS(s) || HoldingOpenS(s), Required: true),
            new("rotate", 26, Grammar.Tap, Delivery.Immediate, null, S(KeyR), _none, S(PadRightBumper), _none, HoldingOpenS),
            new("cancel_return", 27, Grammar.Tap, Delivery.Immediate, null, S(MouseRight), _none, S(PadB).WithBackLadderStep(), _none, HoldingOpenS),
            new("quick_move", 28, Grammar.Tap, Delivery.Immediate, null, InputBinding.Chord(KeyShift, MouseLeft), shiftEnter, S(PadX), _none, PanelS),
            new("close_panel", 29, Grammar.Tap, Delivery.Immediate, null, S(KeyTab), _none, S(PadB).WithBackLadderStep(), _none, s => PanelS(s) || HoldingOpenS(s)),
        };

        PhysicalInput[] digits = [KeyDigit1, KeyDigit2, KeyDigit3, KeyDigit4, KeyDigit5, KeyDigit6, KeyDigit7, KeyDigit8];
        for (int i = 0; i < 8; i++)
        {
            rows.Add(new($"hotbar_slot_{i + 1}", (ushort)(9 + i), Grammar.Tap, Delivery.Immediate, null, S(digits[i]), _none, _none, _none,
                s => WorldNotAimingS(s) || HoldingClosedS(s) || PanelS(s)));
        }

        return rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
    }

    private static string OwnerOf(string id) => id switch
    {
        "move" or "look" or "sprint" or "jump" or "interact" or "tool" or "shoulder_swap" => PlayerCharacter,
        "throw" => Throwing,
        "pause" or "back" => InputGdd,
        _ => Grid,
    };

    public static IEnumerable<object[]> RowIds() => _rows.Keys.OrderBy(k => _rows[k].NumericId).Select(k => new object[] { k });

    [Fact]
    public void CreateM1a_Validates_AndRegistersExactlyTheTableRows()
    {
        ActionMap map = BuiltInActionMap.CreateM1a();

        Assert.Equal(_rows.Count, map.Count);
        Assert.Equal(29, map.Count);
        foreach (ActionDef def in map.All)
        {
            Assert.True(_rows.ContainsKey(def.StringId), $"{def.StringId} has no test row");
        }

        Assert.True(ActionMapValidator.Validate(map.All).IsValid);
    }

    [Theory]
    [MemberData(nameof(RowIds))]
    public void Row_MatchesTheGddR6Table(string id)
    {
        Row row = _rows[id];
        ActionDef? def = _map.Resolve(id);

        Assert.NotNull(def);
        Assert.Equal(row.NumericId, def.Id);
        Assert.Same(def, _map.Get(row.NumericId));
        Assert.Equal(row.Grammar, def.Grammar);
        Assert.Equal(row.Delivery, def.Delivery);
        Assert.Equal(row.Bit, def.FrameBit);
        Assert.Equal(row.Kbm1, def.KbmPrimary);
        Assert.Equal(row.Kbm2, def.KbmSecondary);
        Assert.Equal(row.Pad1, def.PadPrimary);
        Assert.Equal(row.Pad2, def.PadSecondary);
        Assert.Equal(row.Required, def.Required);
        Assert.Equal(row.Reserved ?? [], def.Reserved.IsDefault ? [] : def.Reserved.ToArray());
        Assert.Equal(row.Reserved is { Length: > 0 }, def.IsReserved);
        Assert.Equal(OwnerOf(id), def.OwnerGdd);
        Assert.Empty(def.Aliases.IsDefault ? [] : def.Aliases);

        foreach (ushort stateId in ReachableSet.Default.Ids)
        {
            Assert.True(RouterState.TryFromId(stateId, out RouterState state));
            Assert.True(
                row.Live(state) == def.IsLive(stateId),
                $"{id}: live-in differs from the GDD in state {state.ToDebugString()} (expected {row.Live(state)})");
        }
    }

    // ------------------------------------------------------------------ the quirks and named AC lines, pinned explicitly

    [Fact]
    public void ActionIds_AreStable()
    {
        string[] byId =
        [
            "", "move", "look", "sprint", "jump", "interact", "throw", "tool", "hotbar_step",
            "hotbar_slot_1", "hotbar_slot_2", "hotbar_slot_3", "hotbar_slot_4", "hotbar_slot_5", "hotbar_slot_6", "hotbar_slot_7", "hotbar_slot_8",
            "drop", "throw_while_holding", "tool_while_holding", "open_panel", "shoulder_swap", "pause", "back",
            "grid_cursor", "pick_place", "rotate", "cancel_return", "quick_move", "close_panel",
        ];

        for (ushort i = 1; i < byId.Length; i++)
        {
            Assert.Equal(byId[i], _map.Get(i)?.StringId);
        }

        Assert.Equal(ActionId.HotbarSlot8, (ushort)16);
        Assert.Equal(ActionId.ClosePanel, (ushort)29);
        Assert.Null(_map.Get(ActionId.None));
        Assert.Null(_map.Get(30));
    }

    [Fact]
    public void Throw_IsOneDef_WithTheAimBitAndACommitFlag()
    {
        ActionDef def = _map.Get(ActionId.Throw)!;

        Assert.Equal(Grammar.AimRelease, def.Grammar);
        Assert.Equal(Delivery.Frame, def.Delivery);
        Assert.Equal(FrameAction.Aim, def.FrameBit);
        Assert.True(def.CommitOnRelease);
        Assert.Single(_map.All, d => d.CommitOnRelease);
    }

    [Fact]
    public void Interact_IsTargetDrivenHold()
    {
        ActionDef def = _map.Get(ActionId.Interact)!;

        Assert.Equal(Grammar.Hold, def.Grammar);
        Assert.True(def.TargetDriven);
        Assert.Single(_map.All, d => d.TargetDriven);
    }

    [Fact]
    public void NumpadEnter_IsAnAliasFlagOnEnter_NeverStoredInASlot()
    {
        foreach (ActionDef def in _map.All)
        {
            foreach (InputBinding b in new[] { def.KbmPrimary, def.KbmSecondary, def.PadPrimary, def.PadSecondary })
            {
                Assert.False(b.Contains(KeyNumpadEnter), def.StringId);
            }
        }

        Assert.Equal(S(KeyEnter).Input, _map.Get(ActionId.PickPlace)!.KbmSecondary.Input);
        Assert.True(_map.Get(ActionId.PickPlace)!.KbmSecondary.AcceptsNumpadEnter);
        Assert.True(_map.Get(ActionId.QuickMove)!.KbmSecondary.AcceptsNumpadEnter);
    }

    [Fact]
    public void Wasd_Arrows_Chords_AndSticks_AreExpressedAsStructuredBindings()
    {
        Assert.Equal(BindingKind.Composite4, _map.Get(ActionId.Move)!.KbmPrimary.Kind);
        Assert.Equal(BindingKind.AxisPair, _map.Get(ActionId.Move)!.PadPrimary.Kind);
        Assert.Equal(BindingKind.Chord, _map.Get(ActionId.QuickMove)!.KbmPrimary.Kind);
        Assert.Equal(BindingKind.Composite4, _map.Get(ActionId.GridCursor)!.KbmSecondary.Kind);
        Assert.Equal(BindingKind.Composite4, _map.Get(ActionId.GridCursor)!.PadSecondary.Kind);
    }

    [Fact]
    public void EveryDefaultBinding_IsFilledIn_AndOnTheRightDevice()
    {
        foreach (ActionDef def in _map.All)
        {
            Assert.False(def.KbmPrimary.IsMissing || def.KbmSecondary.IsMissing || def.PadPrimary.IsMissing || def.PadSecondary.IsMissing, def.StringId);
        }
    }

    // ------------------------------------------------------------------ required / reserved

    [Fact]
    public void RequiredFlags_AreExactlyMoveInteractThrowOpenPanelBackPickPlace()
    {
        string[] required = _map.All.Where(d => d.Required).Select(d => d.StringId).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "back", "interact", "move", "open_panel", "pick_place", "throw" }, required);
        foreach (ActionDef def in _map.All.Where(d => d.Required))
        {
            Assert.False(def.KbmPrimary.IsUnbound, def.StringId);
            Assert.False(def.PadPrimary.IsUnbound, def.StringId);
        }
    }

    [Fact]
    public void ReservedFlags_AreEscOnBackAndPause_AndMenuOnPause()
    {
        ActionDef back = _map.Get(ActionId.Back)!;
        ActionDef pause = _map.Get(ActionId.Pause)!;

        Assert.Equal(new[] { KeyEscape }, back.Reserved.ToArray());
        Assert.Equal(new[] { KeyEscape, PadMenu }, pause.Reserved.ToArray());
        Assert.Equal(new[] { "back", "pause" }, _map.All.Where(d => d.IsReserved).Select(d => d.StringId).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TheReservedInputs_AreBoundOnTheirActions()
    {
        Assert.Equal(S(KeyEscape), _map.Get(ActionId.Back)!.KbmPrimary);
        Assert.Equal(S(KeyEscape).WithBackLadderStep(), _map.Get(ActionId.Pause)!.KbmPrimary);
        Assert.Equal(S(PadMenu), _map.Get(ActionId.Pause)!.PadPrimary);
        Assert.Equal(S(KeyP), _map.Get(ActionId.Pause)!.KbmSecondary);
    }

    // ------------------------------------------------------------------ deferred rows

    [Fact]
    public void DeferredRows_ListEveryNonM1aR6Row_AndNoneIsRegistered()
    {
        // Written from the GDD R6 tables, not from the implementation. "Panel, Holding-open, action menu" table non-M-1a rows:
        // action menu, switch focus, split, sort by address, sort by size, take all, drop (Panel, M1), page manifest, jump to row.
        // "On foot and carrying" table non-M-1a rows: quick ping, comms wheel, chat, push-to-talk, history overlay, status read.
        // "Later contexts" table: vehicle enter/exit, throttle/steer, handbrake/horn, map, build toggle, build place, build rotate,
        // deconstruct, pipette, item/category select, belt tier, turret mount/dismount, turret aim/fire.
        string[] expected =
        [
            "action_menu", "switch_focus", "split", "sort_by_address", "sort_by_size", "take_all", "drop_in_panel", "page_manifest", "jump_to_row",
            "quick_ping", "comms_wheel", "chat", "push_to_talk", "history_overlay", "status_read",
            "vehicle_enter_exit", "vehicle_throttle", "vehicle_handbrake_horn", "map", "build_mode_toggle", "build_place", "build_rotate",
            "build_deconstruct", "build_pipette", "build_select", "build_belt_tier", "turret_mount_dismount", "turret_aim_fire",
        ];

        Assert.Equal(9 + 6 + 13, expected.Length);
        Assert.Equal(28, BuiltInActionMap.DeferredRows.Length);
        Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal), BuiltInActionMap.DeferredRows.OrderBy(s => s, StringComparer.Ordinal));
        foreach (string row in BuiltInActionMap.DeferredRows)
        {
            Assert.Null(_map.Resolve(row));
            Assert.DoesNotContain(_map.All, d => d.StringId == row);
        }

        Assert.Equal(BuiltInActionMap.DeferredRows.Length, BuiltInActionMap.DeferredRows.Distinct().Count());
    }

    [Fact]
    public void EveryOwnerGddPath_ExistsInTheRepository()
    {
        string root = FindRepoRoot();

        foreach (ActionDef def in _map.All)
        {
            Assert.True(File.Exists(Path.Combine(root, def.OwnerGdd)), $"{def.StringId}: owner GDD '{def.OwnerGdd}' not found under {root}");
        }
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void LadderSteps_AreNotSeparateActions()
    {
        Assert.Null(_map.Resolve("cancel_aim"));
        Assert.Null(_map.Resolve("esc_pause"));
    }

    // ------------------------------------------------------------------ capture is data

    [Fact]
    public void NoActionExceptPauseAndBack_IsLiveInAnyCapturedState()
    {
        int captured = 0;
        foreach (ushort id in ReachableSet.Default.Ids)
        {
            Assert.True(RouterState.TryFromId(id, out RouterState state));
            if (state.Capture == CaptureLayer.None)
            {
                continue;
            }

            captured++;
            foreach (ActionDef def in _map.All)
            {
                bool allowed = def.Id is ActionId.Pause or ActionId.Back;
                Assert.True(def.IsLive(id) == allowed, $"{def.StringId} in captured state {state.ToDebugString()}: live={def.IsLive(id)}");
            }
        }

        Assert.True(captured > 0);
    }

    [Fact]
    public void PauseAndBack_AreLiveInEveryReachableState()
    {
        foreach (ushort id in ReachableSet.Default.Ids)
        {
            Assert.True(_map.Get(ActionId.Pause)!.IsLive(id));
            Assert.True(_map.Get(ActionId.Back)!.IsLive(id));
        }
    }

    // ------------------------------------------------------------------ frame bits and delivery

    [Fact]
    public void FrameActions_CarryDistinctBits_ExceptAxes_AndImmediateActionsCarryNone()
    {
        var frame = _map.All.Where(d => d.Delivery == Delivery.Frame).ToArray();
        var bits = frame.Where(d => d.FrameBit is not null).Select(d => d.FrameBit!.Value).ToArray();

        Assert.Equal(
            new[] { "hotbar_step", "interact", "jump", "move", "look", "sprint", "throw" }.OrderBy(s => s, StringComparer.Ordinal),
            frame.Select(d => d.StringId).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(
            new[] { FrameAction.Interact, FrameAction.Jump, FrameAction.Sprint, FrameAction.Aim, FrameAction.HotbarStep }.OrderBy(b => (int)b),
            bits.OrderBy(b => (int)b));
        Assert.Equal(bits.Length, bits.Distinct().Count());
        foreach (ActionDef d in frame.Where(d => d.Grammar == Grammar.Axis))
        {
            Assert.Null(d.FrameBit);
        }

        foreach (ActionDef d in _map.All.Where(d => d.Delivery == Delivery.Immediate))
        {
            Assert.Null(d.FrameBit);
        }

        Assert.Equal(_map.Count, frame.Length + _map.All.Count(d => d.Delivery == Delivery.Immediate));
    }

    [Fact]
    public void LocalVerbsAndInventoryOps_AreImmediate()
    {
        ushort[] immediate =
        [
            ActionId.Pause, ActionId.ShoulderSwap, ActionId.HotbarSlot1, ActionId.HotbarSlot8, ActionId.GridCursor, ActionId.OpenPanel,
            ActionId.ClosePanel, ActionId.Back, ActionId.PickPlace, ActionId.Rotate, ActionId.CancelReturn, ActionId.Drop, ActionId.QuickMove,
        ];

        foreach (ushort id in immediate)
        {
            Assert.Equal(Delivery.Immediate, _map.Get(id)!.Delivery);
        }
    }

    [Fact]
    public void FrameAction_HotbarStep_IsTheAppendedBit4()
    {
        Assert.Equal(4, (int)FrameAction.HotbarStep);
        Assert.Equal(1UL << 4, FrameActions.Bit(FrameAction.HotbarStep));
    }
}
