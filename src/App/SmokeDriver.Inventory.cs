using System;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.Inventory;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.App
{
    /// <summary>
    /// The inventory screen (doc 09 §2.5), driven with the mouse events a player's hand makes: a
    /// drag with a turn, a Ctrl+drag split, a drop that cannot be, a click and the sort buttons, on
    /// the player's own grids and a chest. Every gesture must show at once and faint, and be what
    /// the server has a moment later.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int InventoryStepFrames = 90;

        private int _invStep, _invFrames, _invGestures;
        private GridContainer _chest = null!;
        private uint _logId, _fiberId, _stoneId, _chestLogId;
        private uint _predictedHash;

        private void StartInventory()
        {
            var world = _session.Client.World!;
            var server = _session.Server!.World;
            var player = server.GetPlayer(world.LocalId);
            if (player == null) { Fail("the server has no player for the host"); return; }
            var content = _session.Content;

            // A chest within reach, and a few things to move: a 1×2 log and seven fibers on the player, stones and logs in the chest.
            _chest = server.AddWorldContainer("chest", player.Move.X + 1f, player.Move.Z);
            bool given = server.Give(player.Inventory, ItemStack.OfItem(content.Item("log"), 1))
                && server.Give(player.Inventory, ItemStack.OfItem(content.Item("fiber"), 7))
                && server.Give(_chest, ItemStack.OfItem(content.Item("stone"), 3))
                && server.Give(_chest, ItemStack.OfItem(content.Item("log"), 2));
            Check(given, "the test items did not fit the inventory and the chest");
            _logId = player.Inventory.EntryAt(0, 0)?.EntryId ?? 0;
            _fiberId = player.Inventory.EntryAt(1, 0)?.EntryId ?? 0;
            _stoneId = _chest.EntryAt(0, 0)?.EntryId ?? 0;
            _chestLogId = _chest.EntryAt(1, 0)?.EntryId ?? 0;
            Check(_logId != 0 && _fiberId != 0 && _stoneId != 0 && _chestLogId != 0, "the test items are not where the smoke expects them");
            if (_session.Client.OpenContainer(_chest.Id) == 0) { Fail("the client could not ask to open the chest"); return; }
            Enter(Stage.Inventory);
        }

        /// <summary>One mouse event on a grid, at a canvas position; null for a move with the button held.</summary>
        private static void Mouse(GridView grid, Vector2 at, bool? pressed, bool ctrl = false)
        {
            var local = at - grid.GlobalPosition;
            InputEvent ev = pressed == null
                ? new InputEventMouseMotion { Position = local, ButtonMask = MouseButtonMask.Left }
                : new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed.Value, Position = local, CtrlPressed = ctrl };
            grid._GuiInput(ev);
        }

        private void NextInventoryStep()
        {
            _invStep++;
            _invFrames = 0;
        }

        /// <summary>True once the server has answered everything and the screen shows the confirmed state again.</summary>
        private bool InventorySettled(ClientWorld world) =>
            _session.Client.PendingRequestCount == 0 && world.Prediction.PendingCount == 0;

        private void StepInventory()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var panel = _screen.Inventory;
            var player = server.GetPlayer(world.LocalId)!;
            var prediction = world.Prediction;
            _screen.Hud.Refresh();
            if (++_invFrames > InventoryStepFrames)
            {
                Fail($"inventory step {_invStep} did not finish: {client.PendingRequestCount} requests pending, open container {world.OpenContainerId}, "
                    + $"panel visible {panel.Visible}, dragging {panel.Dragging}, hint \"{panel.HintText}\"");
                return;
            }

            switch (_invStep)
            {
                case 0:
                {
                    // The chest opens as a grid beside the player's own, once the layout has had a frame.
                    if (world.OpenContainerId != _chest.Id || !panel.Visible || !panel.ExternalGrid.IsVisibleInTree()) break;
                    if (panel.ExternalGrid.Size.X < 8 * GridView.Cell || panel.InventoryGrid.Size.X < 8 * GridView.Cell || _invFrames < 4) break;
                    Check(panel.ExternalTitle == "Chest", $"the open chest is titled \"{panel.ExternalTitle}\"");
                    Check(panel.FindChild("Manifest", true, false) is Control { Visible: false }, "the chest is shown as a manifest list");
                    Check(panel.HintText.Contains("Drag to move", StringComparison.Ordinal), $"the inventory hint is \"{panel.HintText}\"");
                    Check(world.Inventory.EntryCount == 2 && world.OpenContainer!.EntryCount == 2, "the client does not see the test items");
                    var screen = GetViewport().GetVisibleRect();
                    Check(screen.Encloses(panel.GetGlobalRect()), $"the inventory panel at {panel.GetGlobalRect()} is not inside the screen {screen}");
                    // Pick the log up by its lower cell and carry it over the hotbar.
                    Mouse(panel.InventoryGrid, panel.InventoryGrid.CellCentre(0, 1), true);
                    Mouse(panel.InventoryGrid, panel.HotbarGrid.CellCentre(3, 0), null);
                    NextInventoryStep();
                    break;
                }

                case 1:
                    // Upright, the 1×2 log is too tall for the one-row hotbar: the outline is red. R lays it down.
                    Check(panel.Dragging, "pressing a stack and moving the mouse did not start a drag");
                    Check(!panel.DropAllowed, "an upright log may be dropped into the one-row hotbar");
                    Check(panel.HintText.Contains(HudText.RotateKey, StringComparison.Ordinal), $"while dragging the hint is \"{panel.HintText}\"");
                    Check(world.Inventory.Get(_logId) != null && prediction.PendingCount == 0, "the dragged stack left its cell before the drop");
                    panel._Input(new InputEventAction { Action = InputActions.Rotate, Pressed = true });
                    NextInventoryStep();
                    break;

                case 2:
                {
                    Check(panel.DropAllowed, "a log lying down may not be dropped into the hotbar");
                    Shot("drag");
                    Mouse(panel.InventoryGrid, panel.HotbarGrid.CellCentre(3, 0), false);
                    // The same frame: on the screen the log lies in the hotbar, faint; the mirror still has it in the inventory.
                    var shown = world.Hotbar.Get(_logId);
                    Check(panel.LastRequestId != 0 && !panel.Dragging, "the drop did not send a move");
                    Check(shown != null && shown.X == 2 && shown.Y == 0 && shown.Rotated, "the dropped log is not shown lying in hotbar cells 3 and 4");
                    Check(shown != null && prediction.IsPending(world.HotbarId, shown), "the dropped log is not marked pending");
                    Check(world.Inventory.Get(_logId) == null, "the dropped log is still shown in the inventory");
                    Check(world.Containers[world.InventoryId].Get(_logId) != null, "the confirmed mirror changed before the server answered");
                    _invGestures++;
                    NextInventoryStep();
                    break;
                }

                case 3:
                {
                    if (!InventorySettled(world)) break;
                    var truth = player.Hotbar.Get(_logId);
                    var shown = world.Hotbar.Get(_logId);
                    Check(truth != null && truth.X == 2 && truth.Rotated, "the server did not put the log where it was dropped");
                    Check(shown != null && !prediction.IsPending(world.HotbarId, shown), "the confirmed log is still marked pending");
                    // Ctrl+drag: half of the seven fibers go to the chest.
                    Mouse(panel.InventoryGrid, panel.InventoryGrid.CellCentre(1, 0), true, ctrl: true);
                    Mouse(panel.InventoryGrid, panel.ExternalGrid.CellCentre(5, 2), null);
                    NextInventoryStep();
                    break;
                }

                case 4:
                {
                    Check(panel.Dragging && panel.DropAllowed, "half a stack of fibers may not be dropped into the chest");
                    Mouse(panel.InventoryGrid, panel.ExternalGrid.CellCentre(5, 2), false);
                    var half = world.OpenContainer?.EntryAt(5, 2);
                    Check(world.Inventory.Get(_fiberId)?.Stack.Count == 3, $"after the split the inventory shows {world.Inventory.Get(_fiberId)?.Stack.Count} fibers, expected 3");
                    Check(half != null && half.Stack.Count == 4 && InventoryPrediction.IsPrivate(half), "the split half is not shown in the chest");
                    _invGestures++;
                    NextInventoryStep();
                    break;
                }

                case 5:
                {
                    if (!InventorySettled(world)) break;
                    var half = world.OpenContainer?.EntryAt(5, 2);
                    Check(_chest.EntryAt(5, 2)?.Stack.Count == 4 && player.Inventory.Get(_fiberId)?.Stack.Count == 3, "the server did not split the fibers 3 / 4");
                    Check(half != null && !InventoryPrediction.IsPrivate(half) && half.EntryId == _chest.EntryAt(5, 2)?.EntryId, "the split half did not take the server's entry id");
                    // A drop that cannot be: stones onto the logs. Nothing is sent, the hint says why.
                    Mouse(panel.ExternalGrid, panel.ExternalGrid.CellCentre(0, 0), true);
                    Mouse(panel.ExternalGrid, panel.ExternalGrid.CellCentre(1, 1), null);
                    NextInventoryStep();
                    break;
                }

                case 6:
                    Check(panel.Dragging && !panel.DropAllowed, "stones may be dropped onto a stack of logs");
                    Mouse(panel.ExternalGrid, panel.ExternalGrid.CellCentre(1, 1), false);
                    Check(panel.LastRequestId == 0 && client.PendingRequestCount == 0, "a drop that does not fit was sent to the server");
                    Check(world.OpenContainer?.Get(_stoneId) is { X: 0, Y: 0 }, "the refused stones did not stay where they were");
                    NextInventoryStep();
                    break;

                case 7:
                {
                    Check(panel.HintText == HudText.MoveRefused(OpResult.DoesNotFit), $"after a refused drop the hint is \"{panel.HintText}\"");
                    // A click sends the stones across: into the first free hotbar slot.
                    Mouse(panel.ExternalGrid, panel.ExternalGrid.CellCentre(0, 0), true);
                    Mouse(panel.ExternalGrid, panel.ExternalGrid.CellCentre(0, 0), false);
                    Check(panel.LastRequestId != 0 && !panel.Dragging, "a click on a stack did not send a quick-move");
                    Check(world.Hotbar.EntryAt(1, 0)?.EntryId == _stoneId && world.OpenContainer?.Get(_stoneId) == null, "the clicked stones are not shown in hotbar slot 2");
                    _invGestures++;
                    NextInventoryStep();
                    break;
                }

                case 8:
                {
                    if (!InventorySettled(world)) break;
                    Check(player.Hotbar.EntryAt(1, 0)?.EntryId == _stoneId, "the server did not move the clicked stones to hotbar slot 2");
                    // The sort buttons: the chest by size (the logs lead), the inventory by address.
                    if (panel.FindChild("ExternalSortSize", true, false) is not Button bySize || panel.FindChild("InventorySortAddress", true, false) is not Button byAddress)
                    {
                        Fail("the sort buttons are missing");
                        return;
                    }
                    Check(bySize.IsVisibleInTree() && byAddress.IsVisibleInTree() && bySize.Text == HudText.SortBySize, "the sort buttons are not on screen");
                    bySize.EmitSignal(BaseButton.SignalName.Pressed);
                    Check(panel.LastRequestId != 0, "Sort by size sent nothing");
                    byAddress.EmitSignal(BaseButton.SignalName.Pressed);
                    Check(panel.LastRequestId != 0, "Sort by address sent nothing");
                    var first = world.OpenContainer?.EntryAt(0, 0);
                    Check(first != null && first.EntryId == _chestLogId && first.H == 2, "sorted by size, the chest does not show the logs first");
                    Check(world.Inventory.EntryAt(0, 0)?.EntryId == _fiberId, "the sorted inventory does not start with the fibers");
                    _predictedHash = world.OpenContainer?.ComputeHash() ?? 0;
                    _invGestures += 2;
                    NextInventoryStep();
                    break;
                }

                case 9:
                    if (!InventorySettled(world)) break;
                    Check(_chest.ComputeHash() == _predictedHash, "the server sorted the chest differently from what the screen showed");
                    Check(world.Inventory.ComputeHash() == player.Inventory.ComputeHash() && world.Hotbar.ComputeHash() == player.Hotbar.ComputeHash(),
                        "after the gestures the player's containers differ from the server's");
                    Check(client.ContainerDesyncs == 0, $"{client.ContainerDesyncs} container desyncs after the inventory gestures");
                    foreach (char c in FontCheck.MissingGlyphs(panel))
                        if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;
                    Check(_missingGlyphs.Length == 0, $"the UI font has no glyph for: {_missingGlyphs}");
                    Shot("chest");
                    if (client.CloseContainer() == 0) { Fail("the client could not ask to close the chest"); return; }
                    NextInventoryStep();
                    break;

                case 10:
                    if (world.OpenContainerId != 0 || panel.Visible) break;
                    Check(!panel.Dragging, "a drag outlived the closed panel");
                    GD.Print($"[smoke] inventory: {_invGestures} gestures (drag + turn, split, click, 2 sorts) shown at once and confirmed, 1 bad drop kept back; "
                        + $"{client.ContainerVersionsVerified} container versions verified");
                    StartRemote();
                    break;
            }
        }
    }
}
