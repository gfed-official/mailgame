using Godot;
using PerformativeMail.Client.Meta;
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
        /// <summary>
        /// Nothing sits closer to the screen edge than this: a window's title bar, rounded corners
        /// or a TV's overscan can eat the outermost pixels.
        /// </summary>
        public const float SafeMargin = 28f;

        public string PhaseText => _phase.Text;
        public string QuotaText => _quota.Text;
        public string WalletText => _wallet.Text;
        public string PromptText => _prompt.Text;
        public string HintText => _hint.Text;
        /// <summary>The Postal Rank line (Lobby and Results only), else empty.</summary>
        public string RankText => _rank.Visible ? _rank.Text : "";
        /// <summary>The local player's profile; without one the HUD shows no rank.</summary>
        public ProfileTracker? Profile;
        /// <summary>The lines of the Payday summary / Results panel while it is shown, else empty.</summary>
        public string OverlayText => _overlay.Visible ? _overlayText : "";
        public Control Overlay => _overlay;
        /// <summary>The tick (Match) or cross (Mismatch) drawn beside the prompt right now; Neutral when there is none.</summary>
        public PromptTone PromptMark => _mark.Tone;
        /// <summary>The widgets along the top edge, for the smoke's "nothing is clipped" check.</summary>
        public Control[] TopRow => new Control[] { _phase, _status, _quota, _wallet, _rank };

        private ClientRuntime _client = null!;
        private InteractionController _interaction = null!;
        private EventFeed _feed = null!;

        private Label _phase = null!, _status = null!, _quota = null!, _wallet = null!, _rank = null!, _prompt = null!, _held = null!, _hint = null!;
        private PanelContainer _overlay = null!;
        private VBoxContainer _overlayLines = null!;
        private object? _overlaySource;
        private string _overlayText = "";
        private ProgressBar _hold = null!;
        private MatchMark _mark = null!;
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

            const float m = SafeMargin;
            _phase = AddLabel("Phase", 26, 0f, 0f, m, m, 700f, m + 38f, HorizontalAlignment.Left);
            _status = AddLabel("Status", 16, 0f, 0f, m, m + 40f, 700f, m + 66f, HorizontalAlignment.Left);
            _quota = AddLabel("Quota", 22, 1f, 0f, -520f, m, -m, m + 34f, HorizontalAlignment.Right);
            _wallet = AddLabel("Wallet", 22, 1f, 0f, -520f, m + 34f, -m, m + 68f, HorizontalAlignment.Right);
            // Between runs the quota line is empty, so the rank takes its place above the wallet.
            _rank = AddLabel("Rank", 16, 1f, 0f, -520f, m + 4f, -m, m + 30f, HorizontalAlignment.Right);

            // What the run waits for and what the "go" key does, under the top row.
            _hint = AddLabel("Hint", 20, 0.5f, 0f, -520f, m + 78f, 520f, m + 108f, HorizontalAlignment.Center);
            _hint.Modulate = Palette.Amber;

            // Payday summary / run results: a panel that sizes itself to its lines, upper middle of the screen.
            _overlay = new PanelContainer
            {
                Name = "Overlay",
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.2f, AnchorBottom = 0.2f,
                GrowHorizontal = GrowDirection.Both,
                GrowVertical = GrowDirection.End,
            };
            var back = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.08f, 0.82f) };
            back.SetCornerRadiusAll(6);
            back.SetContentMarginAll(22f);
            _overlay.AddThemeStyleboxOverride("panel", back);
            _overlayLines = new VBoxContainer { Name = "Lines", MouseFilter = MouseFilterEnum.Ignore };
            _overlayLines.AddThemeConstantOverride("separation", 6);
            _overlay.AddChild(_overlayLines);
            AddChild(_overlay);

            // First person: a dot marks the exact point the interact ray leaves from.
            var crosshair = new ColorRect
            {
                Name = "Crosshair",
                Color = new Color(1f, 1f, 1f, 0.85f),
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
                OffsetLeft = -2f, OffsetRight = 2f, OffsetTop = -2f, OffsetBottom = 2f,
            };
            AddChild(crosshair);

            // The prompt line: the match mark, then the text, centred as one.
            var promptRow = new HBoxContainer
            {
                Name = "PromptRow",
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.66f, AnchorBottom = 0.66f,
                OffsetLeft = m, OffsetRight = -m, OffsetTop = -2f, OffsetBottom = 32f,
            };
            promptRow.AddThemeConstantOverride("separation", 10);
            AddChild(promptRow);
            _mark = new MatchMark { Name = "Mark", CustomMinimumSize = new Vector2(30f, 30f) };
            promptRow.AddChild(_mark);
            _prompt = MakeLabel("Prompt", 20, HorizontalAlignment.Center);
            promptRow.AddChild(_prompt);
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
                OffsetTop = 40f,
                OffsetBottom = 52f,
            };
            AddChild(_hold);

            for (int i = 0; i < _feedLines.Length; i++)
            {
                float top = -76f - m - (_feedLines.Length - i) * 24f;
                _feedLines[i] = AddLabel($"Feed{i}", 15, 0f, 1f, m, top, 620f, top + 24f, HorizontalAlignment.Left);
            }

            _hotbar = new GridView { Name = "Hotbar", ShowSlotNumbers = true, MouseFilter = MouseFilterEnum.Ignore };
            _hotbar.Setup(client, world => world.Hotbar);
            float half = _hotbar.CustomMinimumSize.X * 0.5f;
            _hotbar.AnchorLeft = _hotbar.AnchorRight = 0.5f;
            _hotbar.AnchorTop = _hotbar.AnchorBottom = 1f;
            _hotbar.OffsetLeft = -half;
            _hotbar.OffsetRight = half;
            _hotbar.OffsetTop = -GridView.Cell - m;
            _hotbar.OffsetBottom = -m;
            AddChild(_hotbar);

            _held = AddLabel("Held", 16, 0f, 1f, 0f, -GridView.Cell - m - 30f, 0f, -GridView.Cell - m - 4f, HorizontalAlignment.Center);
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
            _rank.Visible = Profile != null && (global.Phase == RunPhase.Lobby || global.Phase == RunPhase.Results);
            if (_rank.Visible) _rank.Text = HudText.Rank(Profile!.Profile, HudText.RankXpPerRank(world.Content));
            _hint.Text = HudText.PhaseHint(world);
            RefreshOverlay(world);

            var prompt = _interaction.Prompt;
            _prompt.Text = HudText.Prompt(world, prompt, out var tone);
            _prompt.Modulate = tone == PromptTone.Match ? Palette.Match : tone == PromptTone.Mismatch ? Palette.Mismatch : Colors.White;
            _mark.Tone = _prompt.Text.Length > 0 ? tone : PromptTone.Neutral;
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

        /// <summary>Payday shows the shift summary, Results the run results; the lines are rebuilt only when their source changes.</summary>
        private void RefreshOverlay(ClientWorld world)
        {
            object? source = world.Global.Phase == RunPhase.Payday ? world.LastSummary
                : world.Global.Phase == RunPhase.Results ? world.Results
                : null;
            _overlay.Visible = source != null;
            if (ReferenceEquals(source, _overlaySource)) return;
            _overlaySource = source;

            foreach (var child in _overlayLines.GetChildren()) child.QueueFree();
            _overlayText = "";
            if (source == null) return;
            var lines = source is ShiftSummary summary
                ? HudText.SummaryLines(world, summary)
                : HudText.ResultsLines(world, (RunResults)source, Profile?.Profile, Profile?.LastGain);
            bool good = source is ShiftSummary s ? s.QuotaMet : ((RunResults)source).Outcome == RunOutcome.Victory;
            for (int i = 0; i < lines.Count; i++)
            {
                var label = MakeLabel($"Line{i}", i == 0 ? 28 : 18, HorizontalAlignment.Center);
                label.Text = lines[i];
                if (i == 0) label.Modulate = good ? Palette.Match : Palette.Mismatch;
                _overlayLines.AddChild(label);
            }
            _overlayText = string.Join("\n", lines);
        }

        /// <summary>A label anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>) with pixel offsets from that point.</summary>
        private Label AddLabel(string name, int fontSize, float anchorX, float anchorY, float left, float top, float right, float bottom, HorizontalAlignment align)
        {
            var label = MakeLabel(name, fontSize, align);
            label.AnchorLeft = anchorX;
            label.AnchorRight = anchorX;
            label.AnchorTop = anchorY;
            label.AnchorBottom = anchorY;
            label.OffsetLeft = left;
            label.OffsetTop = top;
            label.OffsetRight = right;
            label.OffsetBottom = bottom;
            AddChild(label);
            return label;
        }

        private static Label MakeLabel(string name, int fontSize, HorizontalAlignment align)
        {
            var label = new Label
            {
                Name = name,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            return label;
        }
    }
}
