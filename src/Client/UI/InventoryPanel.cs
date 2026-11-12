using Godot;
using PerformativeMail.Client.Presentation;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Inventory screen (doc 09 §2.1): the player's hotbar and inventory on the left, the open
    /// external container on the right. The Intake is a manifest list (doc 03 §3.2), one row per
    /// address and kind. A click moves a stack to the other side (QuickMove).
    /// </summary>
    public partial class InventoryPanel : PanelContainer
    {
        private ClientRuntime _client = null!;
        private Label _externalTitle = null!;
        private ItemList _manifest = null!;
        private VBoxContainer _externalSide = null!;
        private Label _hint = null!;
        private readonly ManifestList _rows = new ManifestList();
        private uint _listedId, _listedVersion;

        public int ManifestRows => _manifest.ItemCount;

        public void Setup(ClientRuntime client)
        {
            _client = client;
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
            own.AddChild(MakeGrid("HotbarGrid", world => world.Hotbar));
            own.AddChild(new Label { Text = "Inventory" });
            own.AddChild(MakeGrid("InventoryGrid", world => world.Inventory));
            _hint = new Label { Modulate = new Color(1f, 1f, 1f, 0.7f) };
            _hint.AddThemeFontSizeOverride("font_size", 13);
            own.AddChild(_hint);

            _externalSide = new VBoxContainer { Visible = false };
            columns.AddChild(_externalSide);
            _externalTitle = new Label();
            _externalSide.AddChild(_externalTitle);
            _manifest = new ItemList
            {
                Name = "Manifest",
                CustomMinimumSize = new Vector2(380f, 330f),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            _manifest.ItemClicked += OnManifestClicked;
            _externalSide.AddChild(_manifest);
        }

        private GridView MakeGrid(string name, System.Func<ClientWorld, GridContainer?> pick)
        {
            var grid = new GridView { Name = name };
            grid.Setup(_client, pick);
            grid.EntryClicked += (container, entry) => _client.QuickMove(container.Id, entry.EntryId);
            return grid;
        }

        public override void _Process(double delta)
        {
            var world = _client?.World;
            var external = Visible ? world?.OpenContainer : null;
            if (world == null || external == null)
            {
                // Closed: the next time the list opens it starts in address order again.
                _listedId = 0;
                if (world == null || !Visible) return;
            }
            _externalSide.Visible = external != null;
            _hint.Text = external != null
                ? "Click a stack to move it across  ·  [E] or [Tab] closes"
                : "Click a stack to move it between hotbar and inventory  ·  [Tab] closes";
            if (external == null || (external.Id == _listedId && external.Version == _listedVersion)) return;
            if (external.Id != _listedId) _rows.Reset();
            _listedId = external.Id;
            _listedVersion = external.Version;
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
            }
            _externalTitle.Text = $"Intake  ·  {HudText.CountAndKind("item", _rows.TotalItems)} waiting";
        }

        private void OnManifestClicked(long index, Vector2 atPosition, long mouseButtonIndex)
        {
            if (mouseButtonIndex != (long)MouseButton.Left || index < 0 || index >= _rows.Rows.Count) return;
            var row = _rows.Rows[(int)index];
            if (!row.Empty) _client.QuickMove(_listedId, row.FirstEntry);
        }
    }
}
