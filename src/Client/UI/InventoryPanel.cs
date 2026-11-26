using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Inventory screen (doc 09 §2.1, §2.5): the player's hotbar and inventory on the left, the
    /// open external container on the right — a grid, or for the Intake a manifest list (doc 03
    /// §3.2), one row per address and kind. Stacks are dragged between cells (R turns the one on
    /// the cursor, Ctrl+drag takes half), a click sends a stack to the other side, the buttons
    /// sort. Every move shows at once and faint until the server confirms it; a refused one snaps
    /// back and the hint line says why.
    /// </summary>
    public partial class InventoryPanel : PanelContainer
    {
        /// <summary>The pointer has to travel this far with the button down before a click becomes a drag.</summary>
        private const float DragThreshold = 6f;
        private const double NoticeSeconds = 1.8;
        private const string ManifestView = "manifest";

        private static readonly Color DropOk = new Color("59c36a");
        private static readonly Color DropBad = new Color("e5534b");
        private static readonly Color HintColour = new Color(1f, 1f, 1f, 0.7f);
        private static readonly Color NoticeColour = new Color("ff9d8a");

        private ClientRuntime _client = null!;
        private InventoryDrag _drag = null!;
        private readonly List<GridView> _grids = new List<GridView>();
        private Label _externalTitle = null!;
        private ItemList _manifest = null!;
        private VBoxContainer _externalSide = null!;
        private HBoxContainer _externalSort = null!;
        private Label _hint = null!;
        private Control _overlay = null!;
        private readonly ManifestList _rows = new ManifestList();
        private uint _listedId;
        private int _listedRevision = -1;

        // A press that may still become a click or a drag.
        private bool _pressed;
        private uint _pressContainer, _pressEntry;
        private Vector2I _pressCell;
        private Vector2 _pressAt, _pointer;
        private bool _pressHalf;

        // Where the dragged stack would land, as last aimed.
        private GridView? _hoverGrid;
        private Vector2I _hoverCell, _hoverSpot;
        private bool _hoverOk, _hoverRotated;
        private int _hoverRevision = -1;

        private string _notice = "";
        private double _noticeLeft;

        public GridView HotbarGrid { get; private set; } = null!;
        public GridView InventoryGrid { get; private set; } = null!;
        /// <summary>The open container when it is shown as a grid (a chest); hidden for the Intake's manifest.</summary>
        public GridView ExternalGrid { get; private set; } = null!;
        /// <summary>The cards of the shop, on the right while the local player stands at the counter with them up.</summary>
        public ShopPanel Shop { get; private set; } = null!;
        /// <summary>The filters of a sorter or a splitter, on the right while they are up.</summary>
        public FilterPanel Filters { get; private set; } = null!;

        public int ManifestRows => _manifest.ItemCount;
        public bool Dragging => _drag.Active;
        /// <summary>The cell the dragged stack would land on is free for it (what the green and red outline says).</summary>
        public bool DropAllowed => _drag.Active && _hoverGrid != null && _hoverOk;
        public string HintText => _hint.Text;
        public string ExternalTitle => _externalTitle.Text;
        /// <summary>The newest request a click, a drop or a sort button sent; 0 if the last gesture sent none.</summary>
        public uint LastRequestId { get; private set; }

        public void Setup(ClientRuntime client)
        {
            _client = client;
            _drag = new InventoryDrag(client);
            client.RequestCompleted += OnRequestCompleted;
            Name = "InventoryPanel";
            Visible = false;
            SetAnchorsPreset(LayoutPreset.Center);
            GrowHorizontal = GrowDirection.Both;
            GrowVertical = GrowDirection.Both;

            var margin = new MarginContainer();
            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
                margin.AddThemeConstantOverride(side, 14);
            AddChild(margin);
            var columns = new HBoxContainer();
            columns.AddThemeConstantOverride("separation", 24);
            margin.AddChild(columns);

            var own = new VBoxContainer();
            columns.AddChild(own);
            own.AddChild(new Label { Text = "Hotbar" });
            HotbarGrid = MakeGrid("HotbarGrid", world => world.Hotbar);
            own.AddChild(HotbarGrid);
            // The hotbar is not sorted: its slots are the number keys, and those stay where the player put them.
            InventoryGrid = MakeGrid("InventoryGrid", world => world.Inventory);
            var inventoryRow = MakeSortRow("InventorySort", InventoryGrid);
            var inventoryTitle = new Label { Text = "Inventory", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            inventoryRow.AddChild(inventoryTitle);
            inventoryRow.MoveChild(inventoryTitle, 0);
            own.AddChild(inventoryRow);
            own.AddChild(InventoryGrid);
            _hint = new Label
            {
                Modulate = HintColour,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(8 * GridView.Cell, 0f),
            };
            _hint.AddThemeFontSizeOverride("font_size", 13);
            own.AddChild(_hint);

            _externalSide = new VBoxContainer { Visible = false };
            columns.AddChild(_externalSide);
            _externalTitle = new Label();
            _externalSide.AddChild(_externalTitle);
            ExternalGrid = MakeGrid("ExternalGrid", world => world.OpenContainer is { } open && open.Def.View != ManifestView ? open : null);
            _externalSort = MakeSortRow("ExternalSort", ExternalGrid);
            _externalSide.AddChild(_externalSort);
            _externalSide.AddChild(ExternalGrid);
            _manifest = new ItemList
            {
                Name = "Manifest",
                CustomMinimumSize = new Vector2(380f, 330f),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            _manifest.ItemClicked += OnManifestClicked;
            _externalSide.AddChild(_manifest);

            // The shop's cards stand where an open container would: at the counter there is none.
            Shop = new ShopPanel();
            Shop.Setup(client);
            columns.AddChild(Shop);
            Filters = new FilterPanel();
            Filters.Setup(client);
            columns.AddChild(Filters);

            // Last child, so the stack on the cursor is drawn over everything else on the panel.
            _overlay = new Control { Name = "DragOverlay", MouseFilter = MouseFilterEnum.Ignore };
            _overlay.Draw += DrawOverlay;
            AddChild(_overlay);
        }

        private GridView MakeGrid(string name, System.Func<ClientWorld, GridContainer?> pick)
        {
            var grid = new GridView { Name = name };
            grid.Setup(_client, pick);
            grid.Drag = _drag;
            grid.PointerPressed += PointerDown;
            grid.PointerMoved += PointerMove;
            grid.PointerReleased += PointerUp;
            grid.PointerCancelled += PointerCancel;
            _grids.Add(grid);
            return grid;
        }

        /// <summary>"Sort by address" and "Sort by size" for one grid (doc 09 §2.5), named {name}Address / {name}Size.</summary>
        private HBoxContainer MakeSortRow(string name, GridView grid)
        {
            var row = new HBoxContainer { Name = name };
            row.AddChild(MakeSortButton(name + "Address", HudText.SortByAddress, grid, SortKey.Address));
            row.AddChild(MakeSortButton(name + "Size", HudText.SortBySize, grid, SortKey.Size));
            return row;
        }

        private Button MakeSortButton(string name, string text, GridView grid, SortKey key)
        {
            var button = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () =>
            {
                PointerCancel();
                var container = grid.Shown;
                LastRequestId = container == null ? 0 : _client.Sort(container.Id, key);
            };
            return button;
        }

        // --- pointer: press, drag, drop -----------------------------------------------------------

        private GridView? GridUnder(Vector2 at)
        {
            foreach (var grid in _grids)
                if (grid.Covers(at)) return grid;
            return null;
        }

        /// <summary>Button down at a canvas position; with <paramref name="half"/> a drag from here carries half the stack.</summary>
        public void PointerDown(Vector2 at, bool half)
        {
            PointerCancel();
            _pointer = at;
            var grid = GridUnder(at);
            var container = grid?.Shown;
            if (grid == null || container == null) return;
            var cell = grid.CellAt(at);
            var entry = container.EntryAt(cell.X, cell.Y);
            // The new half of a split has no name on the server yet: it can be touched once it has.
            if (entry == null || InventoryPrediction.IsPrivate(entry)) return;
            _pressed = true;
            _pressContainer = container.Id;
            _pressEntry = entry.EntryId;
            _pressCell = cell;
            _pressAt = at;
            _pressHalf = half;
        }

        public void PointerMove(Vector2 at)
        {
            _pointer = at;
            if (_drag.Active) return; // aimed once a frame, in _Process
            if (!_pressed || at.DistanceTo(_pressAt) < DragThreshold) return;
            _pressed = false;
            if (_drag.Begin(_pressContainer, _pressEntry, _pressCell.X, _pressCell.Y, _pressHalf)) DragChanged();
        }

        /// <summary>Button up: the end of a click (the stack goes to the other side) or of a drag (the stack drops here).</summary>
        public void PointerUp(Vector2 at)
        {
            _pointer = at;
            LastRequestId = 0;
            if (_drag.Active)
            {
                var grid = GridUnder(at);
                var container = grid?.Shown;
                if (grid != null && container != null)
                {
                    var cell = grid.CellAt(at);
                    var result = _drag.Aim(container.Id, cell.X, cell.Y, out _, out _);
                    if (result == OpResult.Ok) LastRequestId = _drag.Drop(container.Id, cell.X, cell.Y);
                    else Notice(HudText.MoveRefused(result));
                }
                _drag.Cancel();
                DragChanged();
            }
            else if (_pressed) LastRequestId = Click();
            _pressed = false;
        }

        /// <summary>A click on a stack: at the shop's counter it is sold (with Ctrl one of it), anywhere else it goes to the other side.</summary>
        private uint Click()
        {
            var world = _client.World;
            if (world == null || !world.ShopOpen) return _client.QuickMove(_pressContainer, _pressEntry);
            var entry = world.Prediction.View(_pressContainer)?.Get(_pressEntry);
            if (entry == null) return 0;
            if (entry.Stack.Kind != StackKind.Item)
            {
                Notice(HudText.ShopRefused(OpResult.NotSellable));
                return 0;
            }
            return _client.Sell(_pressContainer, _pressEntry, _pressHalf ? 1 : 0);
        }

        /// <summary>Puts the stack on the cursor back where it lies.</summary>
        public void PointerCancel()
        {
            _pressed = false;
            if (!_drag.Active) return;
            _drag.Cancel();
            DragChanged();
        }

        /// <summary>R: a quarter turn of the stack on the cursor.</summary>
        public void RotateDrag()
        {
            if (!_drag.Active) return;
            _drag.Rotate();
            DragChanged();
        }

        private void DragChanged()
        {
            _hoverGrid = null;
            _hoverRevision = -1;
            foreach (var grid in _grids) grid.QueueRedraw();
            _overlay.QueueRedraw();
        }

        /// <summary>Finds the cells the stack would take with the pointer where it is, and whether the server would agree.</summary>
        private void Aim(ClientWorld world)
        {
            var grid = GridUnder(_pointer);
            var cell = grid?.CellAt(_pointer) ?? default;
            int revision = world.Prediction.Revision;
            if (grid == _hoverGrid && cell == _hoverCell && _drag.Rotated == _hoverRotated && revision == _hoverRevision) return;
            _hoverGrid = grid;
            _hoverCell = cell;
            _hoverRotated = _drag.Rotated;
            _hoverRevision = revision;
            if (grid?.Shown is not { } container) return;
            _hoverOk = _drag.Aim(container.Id, cell.X, cell.Y, out int x, out int y) == OpResult.Ok;
            _hoverSpot = new Vector2I(x, y);
        }

        private void DrawOverlay()
        {
            var world = _client?.World;
            var entry = _drag.Entry;
            if (world == null || entry == null) return;
            var origin = _overlay.GlobalPosition;
            var size = new Vector2(_drag.W, _drag.H) * GridView.Cell;

            if (_hoverGrid != null)
            {
                var cells = new Rect2(_hoverGrid.GlobalPosition + (Vector2)_hoverSpot * GridView.Cell, size).Intersection(_hoverGrid.GetGlobalRect());
                cells.Position -= origin;
                var colour = _hoverOk ? DropOk : DropBad;
                _overlay.DrawRect(cells, new Color(colour, 0.25f));
                _overlay.DrawRect(cells, colour, false, 2f);
            }

            // The stack hangs on the cursor by the cell it was picked up by.
            var corner = _pointer - origin - (new Vector2(_drag.GrabX, _drag.GrabY) + new Vector2(0.5f, 0.5f)) * GridView.Cell;
            var rect = new Rect2(corner + new Vector2(3f, 3f), size - new Vector2(6f, 6f));
            GridView.DrawStack(_overlay, GetThemeDefaultFont(), world, entry.Stack, rect, _drag.Count, 0.9f);
        }

        public override void _Input(InputEvent ev)
        {
            if (!_drag.Active || !ev.IsActionPressed(InputActions.Rotate)) return;
            RotateDrag();
            GetViewport().SetInputAsHandled();
        }

        // --- feedback -----------------------------------------------------------------------------

        private void Notice(string text)
        {
            _notice = text;
            _noticeLeft = NoticeSeconds;
        }

        /// <summary>A refused move has already snapped back (the prediction dropped it); the hint line says why. No sound yet.</summary>
        /// <summary>A panel let go (the world replaced in the Lobby) hears no more of the client.</summary>
        public override void _ExitTree()
        {
            if (_client != null) _client.RequestCompleted -= OnRequestCompleted;
        }

        private void OnRequestCompleted(SimRequest request, OpResult result)
        {
            if (result == OpResult.Ok || !Visible) return;
            if (InventoryPrediction.Predicts(request.Kind)) Notice(HudText.MoveRefused(result));
            else if (request.Kind == RequestKind.Buy || request.Kind == RequestKind.Sell) Notice(HudText.ShopRefused(result));
        }

        public override void _Process(double delta)
        {
            var world = _client?.World;
            if (world == null || !Visible)
            {
                // Closed: nothing stays on the cursor, and the next time the list opens it starts in address order again.
                PointerCancel();
                _listedId = 0;
                _noticeLeft = 0;
                return;
            }
            // The dragged stack can vanish under the cursor: delivered, taken by someone else, its container shut.
            if (_drag.Active && _drag.Entry == null) PointerCancel();
            if (_drag.Active)
            {
                Aim(world);
                _overlay.QueueRedraw();
            }

            var external = world.OpenContainer;
            bool manifest = external != null && external.Def.View == ManifestView;
            _externalSide.Visible = external != null;
            _manifest.Visible = manifest;
            ExternalGrid.Visible = external != null && !manifest;
            _externalSort.Visible = external != null && !manifest;
            Shop.Refresh(world);
            Filters.Refresh(world);

            if (_noticeLeft > 0) _noticeLeft -= delta;
            bool noticing = _noticeLeft > 0;
            _hint.Text = noticing ? _notice : HudText.InventoryHint(external != null, _drag.Active, world.ShopOpen);
            _hint.Modulate = noticing ? NoticeColour : HintColour;

            if (external == null)
            {
                _listedId = 0;
                return;
            }
            if (!manifest)
            {
                _listedId = 0;
                _externalTitle.Text = HudText.ContainerTitle(world, external);
                return;
            }
            int revision = world.Prediction.Revision;
            if (external.Id == _listedId && revision == _listedRevision) return;
            if (external.Id != _listedId) _rows.Reset();
            _listedId = external.Id;
            _listedRevision = revision;
            RefreshManifest(world, external);
        }

        /// <summary>
        /// One row per (address, kind); a click takes the oldest stack of the row. The rows are
        /// updated in place and keep their position while the list is open (<see cref="ManifestList"/>).
        /// </summary>
        private void RefreshManifest(ClientWorld world, GridContainer external)
        {
            _rows.Update(external);
            var rows = _rows.Rows;
            while (_manifest.ItemCount > rows.Count) _manifest.RemoveItem(_manifest.ItemCount - 1);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (i >= _manifest.ItemCount) _manifest.AddItem("");
                _manifest.SetItemText(i, HudText.ManifestRow(world, row));
                _manifest.SetItemDisabled(i, row.Empty);
                var dest = world.Map.FindDestination(row.Address);
                var colour = dest != null ? Palette.HouseColour(dest.Variant).Lightened(0.4f) : Colors.White;
                _manifest.SetItemCustomFgColor(i, row.Empty ? new Color(colour, 0.35f) : colour);
                // Doc 05 §6's purple for a row with cursed mail in it.
                _manifest.SetItemCustomBgColor(i, row.Cursed > 0 ? new Color(Palette.Cursed, 0.35f) : new Color(0f, 0f, 0f, 0f));
            }
            _externalTitle.Text = $"Intake  ·  {HudText.CountAndKind("item", _rows.TotalItems)} waiting";
        }

        private void OnManifestClicked(long index, Vector2 atPosition, long mouseButtonIndex)
        {
            if (mouseButtonIndex != (long)MouseButton.Left || index < 0 || index >= _rows.Rows.Count) return;
            var row = _rows.Rows[(int)index];
            if (!row.Empty) LastRequestId = _client.QuickMove(_listedId, row.FirstEntry);
        }
    }
}
