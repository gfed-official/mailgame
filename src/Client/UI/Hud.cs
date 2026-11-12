using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The minimal HUD (doc 09 §2.4): shift, phase and timer, quota and wallet, the interact prompt
    /// with its hold bar, the event feed and the hotbar. Every string comes from <see cref="HudText"/>.
    /// </summary>
    public partial class Hud : Control
    {
        private const double AmberSeconds = 60.0;
        private const double RedSeconds = 15.0;

        public string PhaseText => _phase.Text;
        public string QuotaText => _quota.Text;
        public string WalletText => _wallet.Text;
        public string PromptText => _prompt.Text;

        private ClientRuntime _client = null!;
        private InteractionController _interaction = null!;
        private EventFeed _feed = null!;

        private Label _phase = null!, _status = null!, _quota = null!, _wallet = null!, _prompt = null!, _held = null!;
        private ProgressBar _hold = null!;
        private GridView _hotbar = null!;
        private readonly Label[] _feedLines = new Label[EventFeed.MaxEntries];
        private int _feedRevision = -1;

        public void Setup(ClientRuntime client, InteractionController interaction, EventFeed feed)
        {
            _client = client;
            _interaction = interaction;
            _feed = feed;
            Name = "Hud";
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            _phase = AddLabel("Phase", 26, 0f, 0f, 16f, 10f, 700f, 44f, HorizontalAlignment.Left);
            _status = AddLabel("Status", 16, 0f, 0f, 16f, 46f, 700f, 70f, HorizontalAlignment.Left);
            _quota = AddLabel("Quota", 22, 1f, 0f, -520f, 10f, -16f, 40f, HorizontalAlignment.Right);
            _wallet = AddLabel("Wallet", 22, 1f, 0f, -520f, 40f, -16f, 70f, HorizontalAlignment.Right);

            _prompt = AddLabel("Prompt", 20, 0f, 0.66f, 0f, 0f, 0f, 30f, HorizontalAlignment.Center);
            _prompt.AnchorRight = 1f;
            _hold = new ProgressBar
            {
                Name = "Hold",
                MinValue = 0.0,
                MaxValue = 1.0,
                Step = 0.0,
                ShowPercentage = false,
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 0.5f,
                AnchorRight = 0.5f,
                AnchorTop = 0.66f,
                AnchorBottom = 0.66f,
                OffsetLeft = -130f,
                OffsetRight = 130f,
                OffsetTop = 36f,
                OffsetBottom = 48f,
            };
            AddChild(_hold);

            for (int i = 0; i < _feedLines.Length; i++)
            {
                float top = -92f - (_feedLines.Length - i) * 24f;
                _feedLines[i] = AddLabel($"Feed{i}", 15, 0f, 1f, 16f, top, 620f, top + 24f, HorizontalAlignment.Left);
            }

            _hotbar = new GridView { Name = "Hotbar", ShowSlotNumbers = true, MouseFilter = MouseFilterEnum.Ignore };
            _hotbar.Setup(client, world => world.Hotbar);
            float half = _hotbar.CustomMinimumSize.X * 0.5f;
            _hotbar.AnchorLeft = _hotbar.AnchorRight = 0.5f;
            _hotbar.AnchorTop = _hotbar.AnchorBottom = 1f;
            _hotbar.OffsetLeft = -half;
            _hotbar.OffsetRight = half;
            _hotbar.OffsetTop = -GridView.Cell - 16f;
            _hotbar.OffsetBottom = -16f;
            AddChild(_hotbar);

            _held = AddLabel("Held", 16, 0f, 1f, 0f, -GridView.Cell - 46f, 0f, -GridView.Cell - 20f, HorizontalAlignment.Center);
            _held.AnchorRight = 1f;

            Refresh();
        }

        public override void _Process(double delta) => Refresh();

        /// <summary>Pulls the current state into the widgets; also called by the smoke before it reads them.</summary>
        public void Refresh()
        {
            var world = _client?.World;
            if (world == null || !world.HasLocal) return;
            var global = world.Global;

            _phase.Text = HudText.ShiftAndPhase(world);
            double left = world.PhaseSecondsLeft;
            bool timed = global.Phase == RunPhase.Delivery;
            _phase.Modulate = timed && left < RedSeconds ? Palette.Mismatch : timed && left < AmberSeconds ? Palette.Amber : Colors.White;
            _status.Text = $"Complaint {global.Complaint}   ·   Backlog {global.Backlog}   ·   Delivered {global.Deliveries}   ·   Misdelivered {global.Misdeliveries}";
            _quota.Text = HudText.Quota(global);
            _quota.Modulate = global.ShiftEarnings >= global.Quota ? Palette.Match : Colors.White;
            _wallet.Text = HudText.Wallet(global);
            _wallet.Modulate = global.Wallet < 0 ? Palette.Mismatch : Colors.White;

            var prompt = _interaction.Prompt;
            _prompt.Text = HudText.Prompt(world, prompt, out var tone);
            _prompt.Modulate = tone == PromptTone.Match ? Palette.Match : tone == PromptTone.Mismatch ? Palette.Mismatch : Colors.White;
            _hold.Visible = prompt.HoldProgress > 0f;
            _hold.Value = prompt.HoldProgress;

            int slot = _client!.Input.HeldSlot;
            _hotbar.SelectedColumn = slot;
            var held = slot > 0 ? world.Hotbar.EntryAt(slot, 0) : null;
            _held.Text = held == null ? "" : HudText.Stack(world, held.Stack);
            var heldDest = held != null && held.Stack.Kind == StackKind.Mail ? world.Map.FindDestination(held.Stack.Address) : null;
            _held.Modulate = heldDest != null ? Palette.HouseColour(heldDest.Variant).Lightened(0.35f) : Colors.White;

            if (_feedRevision != _feed.Revision)
            {
                _feedRevision = _feed.Revision;
                // Newest line at the bottom.
                int offset = _feedLines.Length - _feed.Entries.Count;
                for (int i = 0; i < _feedLines.Length; i++)
                {
                    var entry = i >= offset ? _feed.Entries[i - offset] : null;
                    _feedLines[i].Text = entry?.Text ?? "";
                    _feedLines[i].Modulate = entry != null && entry.Bad ? Palette.Mismatch : Colors.White;
                }
            }
        }

        /// <summary>A label anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>) with pixel offsets from that point.</summary>
        private Label AddLabel(string name, int fontSize, float anchorX, float anchorY, float left, float top, float right, float bottom, HorizontalAlignment align)
        {
            var label = new Label
            {
                Name = name,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = anchorX,
                AnchorRight = anchorX,
                AnchorTop = anchorY,
                AnchorBottom = anchorY,
                OffsetLeft = left,
                OffsetTop = top,
                OffsetRight = right,
                OffsetBottom = bottom,
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            AddChild(label);
            return label;
        }
    }
}
