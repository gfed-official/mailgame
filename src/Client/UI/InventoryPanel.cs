using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Inventory;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;
using PerformativeMail.Sim.World;

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
        private readonly List<uint> _rowEntries = new List<uint>();
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
            if (world == null || !Visible) return;
            var external = world.OpenContainer;
            _externalSide.Visible = external != null;
            _hint.Text = external != null
                ? "Click a stack to move it across  ·  [E] or [Tab] closes"
                : "Click a stack to move it between hotbar and inventory  ·  [Tab] closes";
            if (external == null)
            {
                _listedId = 0;
                return;
            }
            if (external.Id == _listedId && external.Version == _listedVersion) return;
            _listedId = external.Id;
            _listedVersion = external.Version;
            RebuildManifest(world, external);
        }

        /// <summary>One row per (address, kind), in address order; a click takes the oldest stack of the row.</summary>
        private void RebuildManifest(ClientWorld world, GridContainer external)
        {
            var groups = new SortedDictionary<(AddressId Address, ushort Kind), (uint First, int Stacks, int Items)>();
            foreach (var e in external.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                var key = (e.Stack.Address, e.Stack.DefIndex);
                groups[key] = groups.TryGetValue(key, out var g)
                    ? (System.Math.Min(g.First, e.EntryId), g.Stacks + 1, g.Items + e.Stack.Count)
                    : (e.EntryId, 1, e.Stack.Count);
            }

            _manifest.Clear();
            _rowEntries.Clear();
            int total = 0;
            foreach (var (key, g) in groups)
            {
                string kind = HudText.CountAndKind(world.Content.MailKinds[key.Kind].Name, g.Items);
                string stacks = g.Stacks > 1 ? $"  ({g.Stacks} stacks)" : "";
                int row = _manifest.AddItem($"{world.Map.FormatAddress(key.Address)}  —  {kind}{stacks}");
                var dest = world.Map.FindDestination(key.Address);
                if (dest != null) _manifest.SetItemCustomFgColor(row, Palette.HouseColour(dest.Variant).Lightened(0.4f));
                _rowEntries.Add(g.First);
                total += g.Items;
            }
            _externalTitle.Text = $"Intake  ·  {HudText.CountAndKind("item", total)} waiting";
        }

        private void OnManifestClicked(long index, Vector2 atPosition, long mouseButtonIndex)
        {
            if (mouseButtonIndex != (long)MouseButton.Left || index < 0 || index >= _rowEntries.Count) return;
            _client.QuickMove(_listedId, _rowEntries[(int)index]);
        }
    }
}
