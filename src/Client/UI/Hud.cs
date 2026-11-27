using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PerformativeMail.Client.Meta;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The HUD (doc 09 §2.4) in the layout of the concept video: the quota and the clock as two thin
    /// bars at the top with the shift between them, health and stamina at the bottom left, the
    /// hotbar in the middle with the loot cards over it, the trackers (wallet, quota, backlog, the
    /// raid) at the bottom right, a ring for a crosshair. Every string comes from <see cref="HudText"/>.
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
        private const float BarWidth = 420f;
        private const float TrackerWidth = 330f;
        private static readonly Color QuotaColour = new Color("f0a020");
        private static readonly Color HealthColour = new Color("e5484d");
        private static readonly Color BagColour = new Color("c98a4b");
        private static readonly Color PerkColour = new Color("e58fd0");

        public string PhaseText => _phase.Text;
        public string QuotaText => _quota.Text;
        public string WalletText => _wallet.Text;
        public string PromptText => _card.Text;
        /// <summary>The line over the hotbar: what is in the hand.</summary>
        public string HeldText => _held.Visible ? _held.Text : "";
        /// <summary>Where the held stack's name (or Build mode's keys) stands on screen.</summary>
        public Rect2 HeldRect => _held.GetGlobalRect();
        /// <summary>How wide the held line's words are drawn (the label itself is as wide as the screen, its words in the middle).</summary>
        public float HeldTextWidth => _held.GetThemeFont("font").GetStringSize(_held.Text, HorizontalAlignment.Left, -1, _held.GetThemeFontSize("font_size")).X;
        /// <summary>The lowest point of the event feed's lines on screen.</summary>
        public float FeedBottom => _feedLines[_feedLines.Length - 1].GetGlobalRect().End.Y;
        /// <summary>The top of the event feed's first line.</summary>
        public float FeedTop => _feedLines[0].GetGlobalRect().Position.Y;
        /// <summary>True while the map is up: the interact prompt keeps off it.</summary>
        public Func<bool>? MapOpen;
        /// <summary>Where the map is drawn while it is up: kept clear of world labels like the HUD's panels.</summary>
        public Func<Rect2>? MapArea;
        public string HintText => _hint.Text;
        /// <summary>The playtest's note in the top left corner ("Playtest: the shift clock is stopped · [G] ends the shift").</summary>
        public string PlaytestTagText => _playtestTag.Visible ? _playtestTag.Text : "";
        /// <summary>The big PAUSED line while a solo run is paused, else empty.</summary>
        public string PausedText => _paused.Visible ? _paused.Text : "";
        /// <summary>The Postal Rank line (Lobby and Results only), else empty.</summary>
        public string RankText => _rank.Visible ? _rank.Text : "";
        /// <summary>The line across the view: the raid warning, or the countdown while down; else empty.</summary>
        public string BannerText => _banner.Visible ? _banner.Text : "";
        /// <summary>The number beside the health bar.</summary>
        public string HealthText => _hp.Text;
        /// <summary>How full the health and stamina bars are drawn, 0..1.</summary>
        public float HealthShown => _healthBar.Value;
        public float StaminaShown => _staminaBar.Value;
        /// <summary>The raid's tracker row while there is a raid, else empty.</summary>
        public string RaidText => _raid.Visible ? _raid.Text : "";
        public string PostOfficeText => _postOffice.Visible ? _postOffice.Text : "";
        /// <summary>The Post Office row's HP bar, 0..1; below 0 while it has none.</summary>
        public float PostOfficeBar => ((TrackerRow)_postOffice.GetParent()).Bar;
        /// <summary>The words of every tracker row shown at the bottom right.</summary>
        public IEnumerable<string> TrackerTexts => _trackerRows.OfType<TrackerRow>().Where(r => r.Visible && r.Line.Visible && r.Line.Text.Length > 0).Select(r => r.Line.Text);
        /// <summary>The tracker row of the local player's own bag while it lies in the world, else empty.</summary>
        public string BagText => _bag.Visible ? _bag.Text : "";
        /// <summary>What the hammer and the cannon are bound to, while there is something to hit; else empty.</summary>
        public string WeaponsText => _weapons.Visible ? _weapons.Text : "";
        /// <summary>The cards of what was just picked up.</summary>
        public LootPanel Loot => _loot;
        /// <summary>The local player's profile; without one the HUD shows no rank.</summary>
        public ProfileTracker? Profile;
        /// <summary>The lines of the Payday summary / Results panel while it is shown, else empty.</summary>
        public string OverlayText => _overlay.Visible ? _overlayText : "";
        public Control Overlay => _overlay;
        /// <summary>The cards of the Draft while they lie open.</summary>
        public DraftPanel Draft => _draft;
        /// <summary>The loading screen of the Generating phase.</summary>
        public GeneratingPanel Generating => _generating;
        /// <summary>Whose the mouse is; without it the HUD says nothing about a mouse let go.</summary>
        public MouseCapture? Mouse;
        /// <summary>The line that says the mouse was let go and how to take it back; empty while the view has it, a panel is open or the run is paused.</summary>
        public string MouseText => _mouseNote.Visible ? _mouseNote.Text : "";
        /// <summary>The inventory screen: while it is open no world label is drawn on it.</summary>
        public Control? Panel;
        /// <summary>The tracker row of the run's perks once there are any, else empty.</summary>
        public string PerksText => _perks.Visible ? _perks.Text : "";
        /// <summary>The tick (Match) or cross (Mismatch) drawn beside the prompt right now; Neutral when there is none.</summary>
        public PromptTone PromptMark => _mark.Tone;
        /// <summary>How far the bar under a held prompt is filled right now; 0 when it is not shown.</summary>
        public float HoldShown => _hold.Visible ? (float)_hold.Value : 0f;
        /// <summary>Build mode; without it the HUD shows none.</summary>
        public BuildController? Build;
        /// <summary>The recipes on the bar where the hotbar is while Build mode is up, one a line; else empty.</summary>
        public string RecipeBarText => _recipeBar.Visible ? string.Join("\n", RecipeCards()) : "";
        /// <summary>The bar of recipes, for the smoke's "nothing on anything else" check.</summary>
        public Control RecipeBar => _recipeBar;
        /// <summary>The key of the bar that is selected, -1 while the bar is down.</summary>
        public int RecipeShown { get; private set; } = -1;

        private IEnumerable<string> RecipeCards()
        {
            foreach (var child in _recipeBar.GetChildren())
                if (child is Label label) yield return label.Text;
        }

        /// <summary>The lines along the edges, for the smoke's "nothing is clipped" check.</summary>
        public Control[] TopRow => new Control[] { _phase, _quota, _wallet, _rank };
        /// <summary>The top group from the top down, as far as it is shown: gold, quota line and bar, the shift's line, clock bar, hint. No two of them may touch (nit 14).</summary>
        public Control[] TopStack => new Control[] { _wallet, _quota, _quotaBar, _phase, _timeBar, _hint }.Where(c => c.Visible).ToArray();
        /// <summary>The shift's line on the top plate.</summary>
        public Control PhaseLine => _phase;
        public Control TopPlate => _topPlate;
        /// <summary>The faces the prompt and the shift's line are drawn in, for the smoke's check of the theme.</summary>
        public (Font Body, Font Display) Faces => (_card.BodyFont, _phase.GetThemeFont("font"));

        /// <summary>
        /// Where no world label may be drawn (nit 14): the top plate and the hint under it, and the
        /// banner while it says something. Not the ring in the middle: the address of the mailbox
        /// being looked at stands right over it. And every panel that is up (issue (d) of the 25fbca5
        /// playtest: a guest's name over a corner of the Draft's cards): the cards, the Payday
        /// summary and the Results, the inventory screen.
        /// </summary>
        public IReadOnlyList<ScreenBox> KeepClear()
        {
            _keepClear.Clear();
            var top = _topPlate.GetGlobalRect();
            _keepClear.Add(new ScreenBox(top.Position.X, 0f, top.End.X, top.End.Y));
            // The hint as wide as its words too ("Playtest: the shift clock is stopped · [G] ends the shift").
            if (_hint.Visible && _hint.Text.Length > 0) AddClear(_hint, Mathf.Max(HintClearWidth, TextWidth(_hint) + 2f * BannerClearMargin));
            // As wide as its words, at least: "PROTECTED in the yard   ·   until you leave it …" is wider
            // than the raid's banner, and the under-attack marker stood on its ends (the 0e940bc playtest).
            if (_banner.Visible) AddClear(_banner, Mathf.Max(BannerClearWidth, BannerTextWidth + 2f * BannerClearMargin));
            if (_playtestTag.Visible)
            {
                var tag = _playtestTag.GetGlobalRect();
                _keepClear.Add(new ScreenBox(tag.Position.X, tag.Position.Y, tag.Position.X + TextWidth(_playtestTag) + 8f, tag.End.Y));
            }
            AddPanel(_draft);
            AddPanel(_generating);
            AddPanel(_overlay);
            AddPanel(Panel);
            if (MapOpen?.Invoke() == true && MapArea != null)
            {
                var map = MapArea();
                _keepClear.Add(new ScreenBox(map.Position.X, map.Position.Y, map.End.X, map.End.Y));
            }
            return _keepClear;
        }

        private void AddPanel(Control? panel)
        {
            if (panel == null || !panel.Visible) return;
            var rect = panel.GetGlobalRect();
            _keepClear.Add(new ScreenBox(rect.Position.X, rect.Position.Y, rect.End.X, rect.End.Y));
        }

        /// <summary>A centred line of text keeps <paramref name="width"/> of its row clear, not the whole row it is laid out in.</summary>
        private void AddClear(Control line, float width)
        {
            var rect = line.GetGlobalRect();
            _keepClear.Add(ScreenBox.Around(rect.GetCenter().X, rect.GetCenter().Y, width, rect.Size.Y));
        }

        /// <summary>
        /// The prompt stays on the screen: a long card ("MAILBOX · … · WRONG ADDRESS" over "Deliver Cursed 1
        /// Postcard for …") is drawn smaller, down to 12 px, until its widest line fits beside the mark (the
        /// 0e940bc playtest's line ran off the right edge at 1280×720).
        /// </summary>
        private void FitPrompt()
        {
            // Out of the tree (the Results → Lobby rebuild) there is no viewport to fit to: next frame.
            if (!IsInsideTree()) return;
            _card.Fit(GetViewportRect().Size.X - 2f * SafeMargin - _mark.CustomMinimumSize.X - 10f - 2f * 6f);
        }

        /// <summary>Where the prompt's words are drawn on screen, for the smoke's "on the screen" check.</summary>
        /// Worked out from the card's size for its text rather than read from its laid-out rect, which lags a frame
        /// behind a change of text: the row centres the mark, a gap and the card as one, from the row's top.
        public Rect2 PromptTextRect
        {
            get
            {
                if (!IsInsideTree() || _card.Text.Length == 0) return new Rect2();
                var size = _card.GetCombinedMinimumSize();
                float lead = _mark.CustomMinimumSize.X + 10f;
                var row = _promptRow.GetGlobalRect();
                float x = row.GetCenter().X - (lead + size.X) * 0.5f + lead;
                return new Rect2(x, row.Position.Y, size);
            }
        }

        /// <summary>Where the hold bar stands (shown or not), under the crosshair.</summary>
        public Rect2 HoldBarRect => _hold.GetGlobalRect();

        /// <summary>The prompt card's keycaps, top row first ("E", "HOLD E", "F").</summary>
        public IEnumerable<string> PromptKeycaps => _card.Keycaps;

        private const float HintClearWidth = 560f, BannerClearWidth = 640f, BannerClearMargin = 24f;

        /// <summary>Where the banner's words are drawn on screen (centred on its line).</summary>
        public Rect2 BannerTextRect
        {
            get
            {
                var rect = _banner.GetGlobalRect();
                float w = BannerTextWidth;
                return new Rect2(rect.GetCenter().X - w * 0.5f, rect.Position.Y, w, rect.Size.Y);
            }
        }

        /// <summary>How wide the banner's words are drawn, px.</summary>
        public float BannerTextWidth => TextWidth(_banner);

        private static float TextWidth(Label line) =>
            line.GetThemeFont("font").GetStringSize(line.Text, HorizontalAlignment.Left, -1, line.GetThemeFontSize("font_size")).X;
        private readonly List<ScreenBox> _keepClear = new List<ScreenBox>();
        private Panel _topPlate = null!;

        private ClientRuntime _client = null!;
        private InteractionController _interaction = null!;
        private EventFeed _feed = null!;

        private Label _postOffice = null!;
        private Label _earnPop = null!;
        private Label _phase = null!, _quota = null!, _wallet = null!, _rank = null!, _raid = null!, _bag = null!, _perks = null!;
        /// <summary>The Lobby's plates (doc 09 §2.2): the world and its seed, the kit, the host's keys for them.</summary>
        private Label _world = null!, _kit = null!, _worldKeys = null!, _stamps = null!;
        /// <summary>The Lobby's Postage Stamps plate (doc 01 §6).</summary>
        public string StampsText => _stamps.Visible ? _stamps.Text : "";
        public string WorldText => _world.Visible ? _world.Text : "";
        public string KitText => _kit.Visible ? _kit.Text : "";
        public string WorldKeysText => _worldKeys.Visible ? _worldKeys.Text : "";
        private DraftPanel _draft = null!;
        private GeneratingPanel _generating = null!;
        private MeterBar _repairBar = null!;
        /// <summary>The Repair Hammer's target bar under the crosshair: -1 while it is down, else what it shows.</summary>
        public float RepairBarValue => _repairBar.Visible ? _repairBar.Value : -1f;
        private PromptCard _card = null!;
        private HBoxContainer _promptRow = null!;
        private Label _playtestTag = null!;
        private Label _held = null!, _hint = null!, _paused = null!, _mouseNote = null!, _banner = null!, _hp = null!, _weapons = null!;
        private MeterBar _quotaBar = null!, _timeBar = null!, _healthBar = null!, _staminaBar = null!;
        private ColorRect _hurt = null!;
        private LootPanel _loot = null!;
        private PanelContainer _overlay = null!;
        private VBoxContainer _overlayLines = null!;
        private object? _overlaySource;
        private string _overlayText = "";
        private ProgressBar _hold = null!;
        private MatchMark _mark = null!;
        private GridView _hotbar = null!;
        private HFlowContainer _recipeBar = null!;
        private const float RecipeBarHalfWidth = 280f, RecipeBarHeight = 94f;
        /// <summary>A recipe card's width: half the bar, less the gap between two.</summary>
        public const float RecipeCardWidth = RecipeBarHalfWidth - RecipeCardGap * 0.5f;
        private const float RecipeCardGap = 18f;
        /// <summary>How far the feed goes up while Build mode is up, for the recipe bar's fifth row and the keys line over it.</summary>
        private const float FeedLiftBuilding = 36f;
        private readonly Label[] _feedLines = new Label[EventFeed.VisibleRows];
        private int _feedRevision = -1;
        private RunPhase _clockPhase;
        private int _clockShift;
        private double _clockTotal;
        private uint _hurtTick;
        private float _hurtFlash;

        public void Setup(ClientRuntime client, InteractionController interaction, EventFeed feed)
        {
            _client = client;
            _interaction = interaction;
            _feed = feed;
            Name = "Hud";
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            const float m = SafeMargin;

            // Being hit reddens the whole view for a moment; being down keeps it dim.
            _hurt = new ColorRect { Name = "Hurt", Color = new Color(0.75f, 0.04f, 0.04f, 0f), MouseFilter = MouseFilterEnum.Ignore };
            _hurt.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_hurt);

            // Top centre (UI polish plan item 3; ref R.E.P.O.): the team's gold as a big number, what the
            // shift has earned of its quota under it with its bar, then the shift and its phase and the time
            // left, on a plate of their own so that the world behind cannot be read into them (nit 14).
            _topPlate = new Panel { Name = "TopPlate", MouseFilter = MouseFilterEnum.Ignore };
            var plate = new StyleBoxFlat { BgColor = new Color(0.06f, 0.1f, 0.16f, 0.6f), AntiAliasing = true };
            plate.SetCornerRadiusAll(10);
            _topPlate.AddThemeStyleboxOverride("panel", plate);
            AddChild(_topPlate);
            _wallet = AddLabel("Wallet", 30, 0.5f, 0f, -BarWidth * 0.5f, m - 8f, BarWidth * 0.5f, m + 30f, HorizontalAlignment.Center, display: true);
            // "+17 g" beside the number for a moment after it changes.
            _earnPop = AddLabel("EarnPop", 18, 0.5f, 0f, 0f, m, 140f, m + 26f, HorizontalAlignment.Left, display: true);
            _earnPop.Visible = false;
            _quota = AddLabel("Quota", 15, 0.5f, 0f, -BarWidth * 0.5f, m + 33f, BarWidth * 0.5f, m + 54f, HorizontalAlignment.Center);
            _quotaBar = AddBar("QuotaBar", 0.5f, 0f, -BarWidth * 0.5f, m + 57f, BarWidth * 0.5f, m + 62f, QuotaColour);
            _phase = AddLabel("Phase", 20, 0.5f, 0f, -350f, 0f, 350f, 0f, HorizontalAlignment.Center, display: true);
            _timeBar = AddBar("TimeBar", 0.5f, 0f, -BarWidth * 0.5f, 0f, BarWidth * 0.5f, 0f, Colors.White);

            // What the run waits for and what the "go" key does, under the plate.
            _hint = AddLabel("Hint", 18, 0.5f, 0f, -520f, 0f, 520f, 0f, HorizontalAlignment.Center);
            _hint.Modulate = Palette.Amber;
            _playtestTag = AddLabel("PlaytestTag", 13, 0f, 0f, m, m - 8f, m + 420f, m + 12f, HorizontalAlignment.Left);
            _playtestTag.Modulate = Palette.Amber;
            _playtestTag.Visible = false;

            // The raid warning and the countdown while down: across the view, above the crosshair.
            _banner = AddLabel("Banner", 28, 0.5f, 0.25f, -520f, -20f, 520f, 20f, HorizontalAlignment.Center, display: true);
            _banner.Visible = false;

            // Below the crosshair, clear of the Payday / Results panel a pause can sit on top of.
            _paused = AddLabel("Paused", 44, 0.5f, 0.6f, -300f, -32f, 300f, 32f, HorizontalAlignment.Center, display: true);
            _paused.Text = HudText.PausedTitle;
            _paused.Visible = false;
            // Where PAUSED stands in a solo run: with others in the run Esc only lets the mouse go, and the screen has to say so.
            _mouseNote = AddLabel("MouseNote", 20, 0.5f, 0.6f, -400f, -16f, 400f, 16f, HorizontalAlignment.Center);
            _mouseNote.Text = HudText.MouseReleased;
            _mouseNote.Modulate = Palette.Amber;
            _mouseNote.Visible = false;

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
            // Its plate is the theme's PanelContainer panel (content/ui/theme.tres).
            _overlayLines = new VBoxContainer { Name = "Lines", MouseFilter = MouseFilterEnum.Ignore };
            _overlayLines.AddThemeConstantOverride("separation", 6);
            _overlay.AddChild(_overlayLines);
            AddChild(_overlay);

            // The cards of the Draft, where the Payday summary stood a moment ago.
            _draft = new DraftPanel();
            _draft.Setup(client);
            AddChild(_draft);
            // The panels hang from under the hint, which the top plate's gold and quota pushed down (UI polish plan item 3).
            _topWithQuota = null;
            LayoutTop(quota: true);
            _generating = new GeneratingPanel();
            AddChild(_generating);

            // First person: the ring marks the exact point the interact ray leaves from.
            var crosshair = new RingCrosshair
            {
                Name = "Crosshair",
                AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
                OffsetLeft = -12f, OffsetRight = 12f, OffsetTop = -12f, OffsetBottom = 12f,
            };
            AddChild(crosshair);

            // The prompt line: the match mark, then the text, centred as one.
            var promptRow = _promptRow = new HBoxContainer
            {
                Name = "PromptRow",
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                // Under the hold bar, which stands right under the crosshair: a card of three rows ends clear of the
                // loot cards over the hotbar at 1280×720 (UI polish plan item 5).
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
                OffsetLeft = m, OffsetRight = -m, OffsetTop = PromptRowTop, OffsetBottom = PromptRowTop + PromptRowHeight,
            };
            promptRow.AddThemeConstantOverride("separation", 10);
            AddChild(promptRow);
            _mark = new MatchMark { Name = "Mark", CustomMinimumSize = new Vector2(30f, 30f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            promptRow.AddChild(_mark);
            _card = new PromptCard();
            promptRow.AddChild(_card);
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
                AnchorTop = 0.5f,
                AnchorBottom = 0.5f,
                OffsetLeft = -130f,
                OffsetRight = 130f,
                OffsetTop = 22f,
                OffsetBottom = 34f,
            };
            AddChild(_hold);
            // Doc 09 §3 "Repair hammer shows a target health bar": under the crosshair, where the hold
            // bar stands (no hold goes with the hammer). The bar over the construct itself stands above
            // a first-person view this close (the 0e940bc playtest saw it in no shot).
            _repairBar = AddBar("RepairTarget", 0.5f, 0.5f, -130f, 22f, 130f, 32f, HealthColour);
            _repairBar.Visible = false;

            // Bottom left: health in red with its number, stamina in five blocks; the feed above them.
            _healthBar = AddBar("Health", 0f, 1f, m, -m - 40f, m + 180f, -m - 28f, HealthColour);
            _hp = AddLabel("HealthNumber", 14, 0f, 1f, m + 188f, -m - 46f, m + 260f, -m - 22f, HorizontalAlignment.Left, display: true);
            _staminaBar = AddBar("Stamina", 0f, 1f, m, -m - 16f, m + 180f, -m - 4f, Colors.White);
            _staminaBar.Segments = 5;
            _weapons = AddLabel("Weapons", 14, 0f, 1f, m, -m - 74f, m + 560f, -m - 50f, HorizontalAlignment.Left);
            _weapons.Visible = false;
            for (int i = 0; i < _feedLines.Length; i++)
            {
                float top = -m - 82f - (_feedLines.Length - i) * 24f;
                _feedLines[i] = AddLabel($"Feed{i}", 15, 0f, 1f, m, top, 620f, top + 24f, HorizontalAlignment.Left);
            }

            // Bottom right: the trackers, one plate a line, from the bottom up.
            // (The gold and the quota are on the top plate, UI polish plan item 3.)
            // Between runs the rank is the bottom row.
            // Only what is urgent (UI polish plan item 4; ref Valheim, Peak): the shift's tallies are on the
            // inventory screen's head and the Payday card, the Post Office only when hurt, the raid only in one.
            _rank = AddTracker("Rank", 0, new Color("c3a6ff"));
            _perks = AddTracker("Perks", 0, PerkColour);
            _raid = AddTracker("Raid", 1, HealthColour);
            _bag = AddTracker("Bag", 2, BagColour);
            _postOffice = AddTracker("PostOffice", 3, new Color("e5383b"));
            // In the Lobby, where the shift's plates are empty.
            _world = AddTracker("World", 1, new Color("7fd1b9"));
            _kit = AddTracker("Kit", 2, new Color("f4a261"));
            _worldKeys = AddTracker("WorldKeys", 3, new Color("b8c4d0"));
            _stamps = AddTracker("Stamps", 4, Palette.Cursed);

            _hotbar = new GridView { Name = "Hotbar", ShowSlotNumbers = true, HotbarStyle = true, MouseFilter = MouseFilterEnum.Ignore };
            // The Slingshot's shots left as a badge on it (UI polish plan item 8).
            _hotbar.Badge = stack =>
            {
                var world = client.World;
                if (world == null || stack.Kind != StackKind.Item) return "";
                var def = world.Content.Items[stack.DefIndex];
                return def.Shoots && def.Ammo != null ? SlingRules.Shots(def, client.SlingLoaded, world.Hotbar, world.Inventory).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            };
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

            // Build mode's recipes (doc 09 §2.7), a card each, where the hotbar is (the hands are on
            // the tools of building then), in as many rows as they take, clear of the feed and the trackers.
            _recipeBar = new HFlowContainer
            {
                Name = "RecipeBar",
                Alignment = FlowContainer.AlignmentMode.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            _recipeBar.AddThemeConstantOverride("h_separation", (int)RecipeCardGap);
            _recipeBar.AddThemeConstantOverride("v_separation", 1);
            // Five rows of the small font fit between the feed (lifted while building) and the bottom edge, the keys over them.
            Place(_recipeBar, 0.5f, 1f, -RecipeBarHalfWidth, -m - RecipeBarHeight, RecipeBarHalfWidth, -m);
            AddChild(_recipeBar);

            // What was just picked up takes the place of the held stack's name for a few seconds.
            _loot = new LootPanel { Lift = m + GridView.Cell + 10f };
            AddChild(_loot);
            client.EventReceived += OnLootEvent;

            Refresh();
        }

        private void OnLootEvent(SimEvent ev)
        {
            if (_client.World != null) _loot.Cards.OnEvent(_client.World, ev);
        }

        /// <summary>A HUD let go (the world replaced in the Lobby) hears no more of the client.</summary>
        public override void _ExitTree()
        {
            if (_client != null) _client.EventReceived -= OnLootEvent;
        }

        public override void _Process(double delta)
        {
            Refresh();
            StepEarnPop((float)delta);
            StepOverlay((float)delta);
            _heldAge += (float)delta;
            _held.SelfModulate = new Color(1f, 1f, 1f, HeldAlpha);
            var world = _client?.World;
            if (world == null || !world.HasLocal) return;
            _hurtFlash = Mathf.MoveToward(_hurtFlash, 0f, (float)delta * 2.5f);
            float dim = world.Local.Dead ? 0.3f : 0f;
            _hurt.Color = new Color(_hurt.Color, Mathf.Max(dim, _hurtFlash * 0.38f));
        }

        /// <summary>Pulls the current state into the widgets; also called by the smoke before it reads them.</summary>
        public void Refresh()
        {
            var world = _client?.World;
            if (world == null || !world.HasLocal) return;
            var global = world.Global;
            bool between = global.Phase == RunPhase.Lobby || global.Phase == RunPhase.Generating || global.Phase == RunPhase.Results;

            _phase.Text = HudText.ShiftAndPhase(world);
            double left = world.PhaseSecondsLeft;
            bool timed = global.Phase == RunPhase.Delivery;
            var urgency = timed && left < RedSeconds ? Palette.Mismatch : timed && left < AmberSeconds ? Palette.Amber : Colors.White;
            _phase.Modulate = urgency;
            // The clock as a bar: full when the phase began (or when this client first saw it).
            if (global.Phase != _clockPhase || global.Shift != _clockShift)
            {
                _clockPhase = global.Phase;
                _clockShift = global.Shift;
                _clockTotal = 0.0;
            }
            if (left > _clockTotal) _clockTotal = left;
            _timeBar.Visible = world.PhaseHasClock;
            _timeBar.Value = _clockTotal > 0.0 ? (float)(left / _clockTotal) : 0f;
            _timeBar.Fill = urgency;
            LayoutTop(quota: !between);
            _quota.Visible = !between;
            _quotaBar.Visible = !between;
            _quotaBar.Value = global.Quota > 0 ? global.ShiftEarnings / (float)global.Quota : 0f;
            _quotaBar.Fill = global.ShiftEarnings >= global.Quota && global.Phase != RunPhase.Prep ? Palette.Match : QuotaColour;

            _quota.Text = HudText.Quota(global);
            _quota.Modulate = global.ShiftEarnings >= global.Quota ? Palette.Match : Colors.White;
            _wallet.Text = HudText.Wallet(global);
            _wallet.Modulate = global.Wallet < 0 ? Palette.Mismatch : Palette.Gold;
            NoteWallet(global.Wallet, between);
            _rank.Visible = Profile != null && between;
            if (_rank.Visible) _rank.Text = HudText.Rank(Profile!.Profile, HudText.RankXpPerRank(world.Content));
            bool lobby = global.Phase == RunPhase.Lobby;
            _world.Visible = _kit.Visible = _stamps.Visible = lobby;
            _worldKeys.Visible = lobby && world.IsHost;
            if (lobby)
            {
                _world.Text = HudText.WorldTracker(world);
                _kit.Text = HudText.KitTracker(world);
                _worldKeys.Text = HudText.WorldKeys;
                _stamps.Text = HudText.StampsTracker(world);
            }
            _perks.Text = HudText.PerkTracker(world);
            _perks.Visible = _perks.Text.Length > 0;
            _raid.Text = HudText.RaidTracker(world);
            _raid.Visible = _raid.Text.Length > 0;
            _bag.Text = HudText.BagTracker(world);
            _bag.Visible = _bag.Text.Length > 0;
            _postOffice.Text = HudText.PostOfficeTracker(world);
            _postOffice.Visible = _postOffice.Text.Length > 0;
            var poRow = (TrackerRow)_postOffice.GetParent();
            poRow.Bar = _postOffice.Visible && world.PostOfficeMaxHp > 0 ? Mathf.Clamp(world.PostOfficeHp / (float)world.PostOfficeMaxHp, 0f, 1f) : -1f;
            poRow.Pulse = _postOffice.Visible && _postOffice.Text.EndsWith(HudText.UnderAttackTag, StringComparison.Ordinal);
            foreach (var row in _trackerRows) row.Visible = !world.ShopOpen;
            var panel = PanelBox();
            LayoutTrackers(panel);
            // A lost host freezes the world where it stood (doc 06 §5); the banner says why nothing moves.
            bool lost = _client!.State == ClientState.Disconnected;
            string hint = lost ? HudText.HostLostHint : HudText.PhaseHint(world);
            // A playtest's note is a small tag in the top left corner, not a line competing with the banners (UI polish plan item 9).
            bool playtest = !lost && HudText.IsPlaytestHint(world);
            _hint.Text = playtest ? "" : hint;
            _playtestTag.Text = playtest ? hint : "";
            _playtestTag.Visible = playtest;
            _paused.Visible = world.Paused || lost;
            _paused.Text = lost ? HudText.HostLostTitle : HudText.PausedTitle;
            _mouseNote.Visible = Mouse != null && Mouse.Released && !Mouse.PanelOpen && !_paused.Visible;
            RefreshOverlay(world);
            _draft.Mouse = Mouse;
            _draft.Refresh(world);
            _generating.Refresh(world);

            // Health, stamina, and what the fight asks of the player.
            _healthBar.Value = world.LocalMaxHp > 0f ? world.LocalHp / world.LocalMaxHp : 0f;
            _hp.Text = HudText.Health(world.LocalHp);
            _staminaBar.Value = world.Local.Stamina;
            _staminaBar.Fill = world.Local.Winded ? Palette.Amber : Colors.White;
            if (world.LocalHurtTick != _hurtTick)
            {
                _hurtTick = world.LocalHurtTick;
                _hurtFlash = 1f;
            }
            string down = HudText.Down(world);
            string safe = HudText.Protected(world);
            _banner.Text = down.Length > 0 ? down : safe.Length > 0 ? safe : HudText.RaidBanner(world);
            // The Payday and Results panel stands where the banner does, and so do the cards; the tracker still says the raiders flee.
            _banner.Visible = _banner.Text.Length > 0 && !Overlay.Visible && !_draft.Visible;
            _banner.Modulate = down.Length > 0 ? Palette.Mismatch : safe.Length > 0 ? Palette.Match : world.Raid == RaidState.Warning ? Palette.Mismatch : Palette.Amber;
            var manned = world.LocalTurret;
            var mender = RepairRules.Hammer(world.Content, world.Hotbar, _client!.Input.HeldSlot);
            var sling = SlingRules.Held(world.Content, world.Hotbar, _client.Input.HeldSlot);
            _weapons.Visible = !world.Local.Dead && (world.Raid != RaidState.None || world.Enemies.Count > 0 || manned != null || mender != null || sling != null);
            if (_weapons.Visible)
                _weapons.Text = manned != null ? HudText.TurretWeapons(manned.Def)
                    : mender != null ? HudText.RepairWeapons(mender, _client.CannonAmmo)
                    : sling != null ? HudText.SlingWeapons(_client.SlingShots, _client.CannonAmmo)
                    : HudText.Weapons(_client.CannonAmmo);

            var prompt = _interaction.Prompt;
            bool building = Build != null && Build.Active;
            float holdProgress = prompt.HoldProgress;
            PromptTone tone;
            string text;
            // In Build mode the line is the ghost's, but a panel that is open still says how to close it.
            bool closing = prompt.Kind == InteractKind.CloseChest || prompt.Kind == InteractKind.CloseIntake || prompt.Kind == InteractKind.CloseBag || prompt.Kind == InteractKind.CloseShop;
            if (building && !closing)
            {
                var ghost = Build!.Ghost;
                text = HudText.BuildPrompt(world, ghost, out tone);
                holdProgress = ghost.HoldProgress;
            }
            // Nothing to interact with (or a closed counter): the Repair Hammer's line at what is hurt before it (doc 05 §4, doc 09 §3 "a target health bar").
            else text = HudText.FocusPrompt(world, prompt, _client.Input.HeldSlot, out tone);
            if (MapOpen?.Invoke() == true)
            {
                text = "";
                tone = PromptTone.Neutral;
                holdProgress = 0f;
            }
            _card.SetText(text);
            FitPrompt();
            DodgePrompt(panel);
            _card.SetTone(tone == PromptTone.Match ? Palette.Match : tone == PromptTone.Mismatch ? Palette.Mismatch : Colors.White);
            _mark.Tone = text.Length > 0 ? tone : PromptTone.Neutral;
            float mendHealth = building || MapOpen?.Invoke() == true ? -1f : HudText.RepairTargetHealth(world, _client!.Input.HeldSlot);
            _repairBar.Visible = mendHealth >= 0f && holdProgress <= 0f && panel == null;
            if (_repairBar.Visible) _repairBar.Value = mendHealth;
            _hold.Visible = holdProgress > 0f;
            _hold.Value = holdProgress;
            RefreshRecipeBar(world, building);

            int slot = _client!.Input.HeldSlot;
            _hotbar.SelectedColumn = slot;
            var held = slot > 0 ? world.Hotbar.EntryAt(slot, 0) : null;
            _held.Text = held == null ? "" : HudText.Held(world, held.Stack);
            var heldDest = held != null && held.Stack.Kind == StackKind.Mail ? world.Map.FindDestination(held.Stack.Address) : null;
            _held.Modulate = heldDest != null ? Palette.HouseColour(heldDest.Variant).Lightened(0.35f) : Colors.White;
            _held.Visible = !_loot.Cards.Visible;
            // Over the hotbar, or over the recipe bar's rows while Build mode is up.
            float m = SafeMargin;
            _held.OffsetTop = building ? -m - RecipeBarHeight - 24f : -GridView.Cell - m - 30f;
            for (int i = 0; i < _feedLines.Length; i++)
            {
                float top = -m - 82f - (_feedLines.Length - i) * 24f - (building ? FeedLiftBuilding : 0f);
                _feedLines[i].OffsetTop = top;
                _feedLines[i].OffsetBottom = top + 24f;
                // Lifted over the recipe bar's rows, the oldest line would run into the prompt: it waits.
                _feedLines[i].Visible = !(building && i == 0);
            }
            _held.OffsetBottom = building ? -m - RecipeBarHeight : -GridView.Cell - m - 4f;
            if (building)
            {
                _held.Text = HudText.BuildKeys;
                _held.Modulate = Palette.Amber;
                _held.Visible = true;
            }
            // UI polish plan item 8 (ref Valheim): the name of what is in the hand shows for a moment after a switch, then fades.
            string heldKey = building ? "" : $"{slot}|{_held.Text}";
            if (heldKey != _heldKey)
            {
                _heldKey = heldKey;
                _heldAge = 0f;
            }
            // The keys of Build mode fit over the recipe bar, clear of the trackers beside it.
            _held.AddThemeFontSizeOverride("font_size", building ? 14 : 16);

            if (_feedRevision != _feed.Revision)
            {
                _feedRevision = _feed.Revision;
                // Newest line at the bottom.
                int offset = _feedLines.Length - _feed.Entries.Count;
                for (int i = 0; i < _feedLines.Length; i++)
                {
                    var entry = i >= offset ? _feed.Entries[i - offset] : null;
                    _feedLines[i].Text = entry?.Text ?? "";
                    // Only bad news is red; good news is green (UI polish plan item 9).
                    _feedLines[i].Modulate = entry == null ? Colors.White : entry.Bad ? Palette.Mismatch : entry.Good ? FeedGood : Colors.White;
                }
            }
            // The last second and a half of a line's six, it fades.
            int first = _feedLines.Length - _feed.Entries.Count;
            for (int i = 0; i < _feedLines.Length; i++)
            {
                var entry = i >= first ? _feed.Entries[i - first] : null;
                float alpha = entry == null ? 1f : Mathf.Clamp((float)((EventFeed.EntrySeconds - entry.Age) / FeedFadeSeconds), 0f, 1f);
                _feedLines[i].SelfModulate = new Color(1f, 1f, 1f, alpha);
            }
        }

        /// <summary>The recipe cards while Build mode is up: the selected one green, one the pockets cannot pay for red.</summary>
        private void RefreshRecipeBar(ClientWorld world, bool building)
        {
            _recipeBar.Visible = building;
            _hotbar.Visible = !building;
            if (!building)
            {
                RecipeShown = -1;
                return;
            }
            var keys = world.Content.RecipeKeys;
            if (_recipeBar.GetChildCount() != keys.Count)
            {
                foreach (var child in _recipeBar.GetChildren())
                {
                    _recipeBar.RemoveChild(child);
                    child.QueueFree();
                }
                for (int i = 0; i < keys.Count; i++)
                {
                    // Two to a row, each its half of the bar: a long one is cut short rather than taking a row of its own.
                    var card = MakeLabel($"Recipe{i}", 12, HorizontalAlignment.Center);
                    card.CustomMinimumSize = new Vector2(RecipeCardWidth, 0f);
                    card.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                    _recipeBar.AddChild(card);
                }
            }
            RecipeShown = Build!.Recipe?.Key ?? -1;
            for (int i = 0; i < keys.Count; i++)
            {
                var label = (Label)_recipeBar.GetChild(i);
                var recipe = Build.Chosen(i);
                var state = world.RecipeState(recipe);
                label.Text = HudText.RecipeCard(recipe, i, keys[i]);
                label.Modulate = i == RecipeShown ? (state == OpResult.Ok ? Palette.Match : Palette.Amber)
                    : state == OpResult.Ok ? Colors.White : new Color(Palette.Mismatch, 0.8f);
                label.AddThemeConstantOverride("outline_size", i == RecipeShown ? 10 : 6);
            }
        }

        /// <summary>Payday shows the shift summary, Results the run results; the lines are rebuilt only when their source changes.</summary>
        private void RefreshOverlay(ClientWorld world)
        {
            object? source = world.Global.Phase == RunPhase.Payday ? world.LastSummary
                : world.Global.Phase == RunPhase.Results ? world.Results
                : null;
            // At Payday's counter the shop's cards stand where the summary does.
            _overlay.Visible = source != null && !world.ShopOpen;
            if (ReferenceEquals(source, _overlaySource)) return;
            _overlaySource = source;

            foreach (var child in _overlayLines.GetChildren()) child.QueueFree();
            _overlayText = "";
            _earnedLabel = null;
            _xpBar = null;
            _stamp = null;
            _overlayAge = 0f;
            if (source == null) return;
            var lines = source is ShiftSummary summary
                ? HudText.SummaryLines(world, summary)
                : HudText.ResultsLines(world, (RunResults)source, Profile?.Profile, Profile?.LastGain);
            HudText.StampTone tone;
            if (source is ShiftSummary ss)
            {
                HudText.SummaryStamp(world, ss, out tone);
                _earnedTarget = ss.Earned;
                _earnedQuota = ss.Quota;
            }
            else HudText.ResultsStamp((RunResults)source, out tone);
            var toneColour = tone == HudText.StampTone.Good ? Palette.Match : tone == HudText.StampTone.Bad ? Palette.Mismatch : StampGrey;
            for (int i = 0; i < lines.Count; i++)
            {
                if (i == HudText.GradeLineIndex) continue;
                if (i == HudText.StampLine)
                {
                    _overlayLines.AddChild(StampRow(lines[i], lines[HudText.GradeLineIndex], toneColour));
                    continue;
                }
                bool earned = source is ShiftSummary && i == HudText.EarnedLine;
                var label = i == 0 ? MakeDisplayLabel($"Line{i}", 28, HorizontalAlignment.Center)
                    : earned ? MakeDisplayLabel($"Line{i}", 24, HorizontalAlignment.Center)
                    : MakeLabel($"Line{i}", 18, HorizontalAlignment.Center);
                label.Text = lines[i];
                // The card's title in cream on the kraft (UI polish plan item 11): the stamp's colour says how it went.
                if (i == 0) label.Modulate = CardTitle;
                if (earned)
                {
                    // Counted up from nothing (UI polish plan item 7; ref R.E.P.O.).
                    label.Modulate = Palette.Gold;
                    label.Text = HudText.Earned(0, _earnedQuota);
                    _earnedLabel = label;
                }
                _overlayLines.AddChild(label);
                // The Results' rank line has the XP towards the next rank as a bar under it, filling.
                if (source is RunResults && Profile != null && i == lines.Count - 1 && lines[i].Contains("Postal Rank", StringComparison.Ordinal))
                {
                    int perRank = HudText.RankXpPerRank(world.Content);
                    _xpTarget = perRank > 0 ? Mathf.Clamp(Profile.Profile.XpIntoRank(perRank) / (float)perRank, 0f, 1f) : 0f;
                    _xpBar = new MeterBar { Name = "XpBar", Fill = new Color("c3a6ff"), CustomMinimumSize = new Vector2(360f, 8f), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
                    _overlayLines.AddChild(_xpBar);
                }
            }
            _overlayText = string.Join("\n", lines);
            StepOverlay(0f);
        }

        private static readonly Color StampGrey = new Color("b8c4d0");
        private static readonly Color CardTitle = new Color("f6f0e4");
        private static readonly Color FeedGood = new Color("a8f0b0");
        private const float FeedFadeSeconds = 1.5f;
        private string _heldKey = "";
        private float _heldAge;
        /// <summary>How long the held stack's name stays after a switch, then how long it takes to fade, s.</summary>
        public const float HeldShowSeconds = 1.5f, HeldFadeSeconds = 0.4f;
        /// <summary>How much of the held stack's name is drawn this frame: 1 after a switch, 0 once faded (Build mode's keys stay).</summary>
        public float HeldAlpha => _heldKey.Length == 0 ? 1f : Mathf.Clamp(1f - (_heldAge - HeldShowSeconds) / HeldFadeSeconds, 0f, 1f);
        /// <summary>How long the earned gold and the XP bar take to count up, s; the stamp's thump.</summary>
        private const float CountUpSeconds = 1.2f, StampSeconds = 0.25f;
        private Label? _earnedLabel;
        private MeterBar? _xpBar;
        private Control? _stamp;
        private int _earnedTarget, _earnedQuota;
        private float _xpTarget, _overlayAge;

        /// <summary>What the Payday card's earned line reads this frame (it counts up), for the smoke.</summary>
        public string EarnedShown => _earnedLabel?.Text ?? "";
        /// <summary>The stamp's words and its turn, while the card is up.</summary>
        public (string Text, float Degrees) StampShown => _stamp is { } st && st.GetChildCount() > 0 && st.GetChild(0) is Label l ? (l.Text, Mathf.RadToDeg(st.Rotation)) : ("", 0f);

        /// <summary>
        /// The stamp, a word in a rimmed plate turned 8° as if pressed by hand (a rotated Label in a PanelContainer, no
        /// texture), beside the postal grade in a ring.
        /// </summary>
        private Control StampRow(string stamp, string grade, Color colour)
        {
            var row = new HBoxContainer { Name = "StampRow", Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 28);
            var plate = new StyleBoxFlat { BgColor = new Color(colour, 0.08f), BorderColor = colour, AntiAliasing = true };
            plate.SetBorderWidthAll(4);
            plate.SetCornerRadiusAll(6);
            plate.SetContentMarginAll(8f);
            plate.ContentMarginLeft = plate.ContentMarginRight = 16f;
            var stampBox = new PanelContainer { Name = "Stamp", MouseFilter = MouseFilterEnum.Ignore };
            stampBox.AddThemeStyleboxOverride("panel", plate);
            var word = MakeDisplayLabel("Word", 30, HorizontalAlignment.Center);
            word.Text = stamp;
            word.Modulate = colour;
            stampBox.AddChild(word);
            // The holder keeps the row's room; the plate turns inside it about its middle.
            var holder = new Control { Name = "StampHolder", MouseFilter = MouseFilterEnum.Ignore };
            // Measured from the face, not the plate: out of the tree the theme is not on it yet.
            var face = UiTheme.DisplayFont ?? GetThemeDefaultFont();
            var size = face.GetStringSize(stamp, HorizontalAlignment.Left, -1f, 30) + new Vector2(2f * 16f + 8f, 2f * 8f + 8f);
            // Room for the turn: a plate turned 8° reaches out by its height's share at each end.
            holder.CustomMinimumSize = size + new Vector2(16f + size.Y * 0.3f, 18f + size.X * 0.14f);
            stampBox.Position = (holder.CustomMinimumSize - size) * 0.5f;
            stampBox.Size = size;
            stampBox.PivotOffset = size * 0.5f;
            stampBox.Rotation = Mathf.DegToRad(StampDegrees);
            holder.AddChild(stampBox);
            row.AddChild(holder);
            _stamp = stampBox;

            var ring = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.25f), BorderColor = colour, AntiAliasing = true };
            ring.SetBorderWidthAll(3);
            ring.SetCornerRadiusAll(32);
            var gradeBox = new PanelContainer { Name = "Grade", MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(64f, 64f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            gradeBox.AddThemeStyleboxOverride("panel", ring);
            var letter = MakeDisplayLabel("Letter", 34, HorizontalAlignment.Center);
            letter.Text = grade.Substring(grade.LastIndexOf(' ') + 1);
            gradeBox.AddChild(letter);
            var gradeCol = new VBoxContainer { Name = "GradeColumn", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            var caption = MakeLabel("Caption", 12, HorizontalAlignment.Center);
            caption.Text = "GRADE";
            caption.Modulate = new Color(1f, 1f, 1f, 0.7f);
            gradeCol.AddChild(gradeBox);
            gradeCol.AddChild(caption);
            row.AddChild(gradeCol);
            return row;
        }

        /// <summary>The stamp's turn, degrees (anticlockwise, as a right hand presses it).</summary>
        public const float StampDegrees = -8f;

        /// <summary>The card's count-up and the stamp's thump, a frame at a time.</summary>
        private void StepOverlay(float delta)
        {
            if (_earnedLabel == null && _xpBar == null && _stamp == null) return;
            _overlayAge += delta;
            float t = Mathf.Clamp(_overlayAge / CountUpSeconds, 0f, 1f);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            if (_earnedLabel != null)
            {
                string text = HudText.Earned(Mathf.RoundToInt(_earnedTarget * eased), _earnedQuota);
                if (_earnedLabel.Text != text) _earnedLabel.Text = text;
            }
            if (_xpBar != null) _xpBar.Value = _xpTarget * eased;
            if (_stamp != null)
            {
                float k = Mathf.Clamp(_overlayAge / StampSeconds, 0f, 1f);
                float scale = 1f + 0.6f * (1f - k) * (1f - k);
                _stamp.Scale = new Vector2(scale, scale);
            }
        }

        private bool? _topWithQuota;

        /// <summary>The box of what covers the middle of the screen: the inventory screen (the Intake, a chest, the bag) or the Payday / Results card; else null.</summary>
        private ScreenBox? PanelBox()
        {
            // The inventory screen, else the Payday / Results card (the 720p Results had a pump's prompt on it).
            Control? up = Panel != null && Panel.Visible && Panel.IsInsideTree() ? Panel : _overlay.Visible && _overlay.IsInsideTree() ? _overlay : null;
            if (up == null) return null;
            var rect = up.GetGlobalRect();
            return new ScreenBox(rect.Position.X, rect.Position.Y, rect.End.X, rect.End.Y);
        }

        /// <summary>
        /// The rows that say something, from the bottom right up with no gaps; from the top right down while an open
        /// panel would cover them there (the f40acb7 playtest: the Intake over the Post Office's HP row).
        /// </summary>
        private void LayoutTrackers(ScreenBox? panel)
        {
            int shown = 0;
            foreach (var row in _trackerRows)
                if (row.Visible && row is TrackerRow t && t.Line.Visible && t.Line.Text.Length > 0) shown++;
            if (!IsInsideTree()) return;
            var screen = GetViewportRect().Size;
            const float m = SafeMargin, step = TrackerRow.RowHeight + 6f;
            bool fromTop = PanelDodge.TrackersFromTop(shown, screen.X - m - TrackerWidth, screen.X - m, screen.Y, m, TrackerRow.RowHeight, 6f, panel);
            TrackersFromTop = fromTop;
            int k = 0;
            foreach (var row in _trackerRows)
            {
                if (!(row.Visible && row is TrackerRow t && t.Line.Visible && t.Line.Text.Length > 0)) continue;
                if (fromTop) Place(row, 1f, 0f, -m - TrackerWidth, m + k * step, -m, m + k * step + TrackerRow.RowHeight);
                else Place(row, 1f, 1f, -m - TrackerWidth, -m - k * step - TrackerRow.RowHeight, -m, -m - k * step);
                k++;
            }
        }

        /// <summary>Where the tracker rows that say something stand on screen.</summary>
        public IEnumerable<Rect2> TrackerRects => _trackerRows.OfType<TrackerRow>().Where(r => r.Visible && r.Line.Visible && r.Line.Text.Length > 0).Select(r => r.GetGlobalRect());

        /// <summary>Whether the trackers stand from the top right this frame, out of an open panel's way.</summary>
        public bool TrackersFromTop { get; private set; }

        /// <summary>The prompt under the crosshair, or out of the way of an open panel that covers it ("Close Intake [E]").</summary>
        private void DodgePrompt(ScreenBox? panel)
        {
            if (!IsInsideTree()) return;
            var screen = GetViewportRect().Size;
            var card = _card.GetCombinedMinimumSize();
            float mid = screen.Y * 0.5f, tall = Mathf.Max(PromptRowHeight, card.Y);
            // Under the hint at the top, over the held stack's name at the foot.
            float ceiling = _hint.GetGlobalRect().End.Y + 4f, floor = screen.Y - SafeMargin - GridView.Cell - 34f;
            var row = PanelDodge.Prompt(mid + PromptRowTop, tall, card.X + _mark.CustomMinimumSize.X + 10f, panel, ceiling, floor, screen.X, SafeMargin);
            _promptRow.OffsetLeft = row.X0;
            _promptRow.OffsetRight = row.X1 - screen.X;
            _promptRow.OffsetTop = row.Y0 - mid;
            _promptRow.OffsetBottom = row.Y0 - mid + PromptRowHeight;
        }

        private const float PromptRowTop = 40f, PromptRowHeight = 34f;

        /// <summary>
        /// The top plate from the top down: the gold, the quota's line and bar (in a run), the shift's line, the
        /// clock bar; the hint under the plate. Between runs there is no quota and the rest closes up under the gold.
        /// </summary>
        private void LayoutTop(bool quota)
        {
            if (_topWithQuota == quota) return;
            _topWithQuota = quota;
            const float m = SafeMargin;
            float y = quota ? m + 66f : m + 34f;
            Place(_phase, 0.5f, 0f, -350f, y, 350f, y + 28f);
            Place(_timeBar, 0.5f, 0f, -BarWidth * 0.5f, y + 31f, BarWidth * 0.5f, y + 37f);
            Place(_topPlate, 0.5f, 0f, -BarWidth * 0.5f - 14f, m - 10f, BarWidth * 0.5f + 14f, y + 45f);
            Place(_hint, 0.5f, 0f, -520f, y + 51f, 520f, y + 79f);
            // Payday's and Results' panel and the Draft's cards stand under the hint, not at a fifth of the screen
            // (at 1280×720 the hint, under the taller plate, reached 173 px and the cards stood at 144).
            float panelTop = y + 87f;
            // The banner too: at a quarter of 720 px its words ran into the lowered hint.
            Place(_banner, 0.5f, 0f, -520f, y + 83f, 520f, y + 123f);
            foreach (var panel in new Control?[] { _overlay, _draft })
            {
                if (panel == null) continue;
                panel.AnchorTop = panel.AnchorBottom = 0f;
                panel.OffsetTop = panel.OffsetBottom = panelTop;
            }
        }

        private const float EarnPopSeconds = 1.1f, EarnPopRise = 16f;
        private int _shownWallet;
        private bool _walletSeen;
        private int _popAmount;
        private float _popAge = EarnPopSeconds;

        /// <summary>What the gold's pop says while it is up ("+17 g"), else empty.</summary>
        public string EarnPopText => _earnPop.Visible ? _earnPop.Text : "";

        /// <summary>The wallet moved: the pop says by how much, adding up what comes while it is still up.</summary>
        private void NoteWallet(int wallet, bool between)
        {
            if (!_walletSeen || between)
            {
                _walletSeen = true;
                _shownWallet = wallet;
                return;
            }
            if (wallet == _shownWallet) return;
            _popAmount = _popAge < EarnPopSeconds ? _popAmount + wallet - _shownWallet : wallet - _shownWallet;
            _shownWallet = wallet;
            _popAge = 0f;
            _earnPop.Text = HudText.EarnPop(_popAmount);
            _earnPop.Modulate = _popAmount >= 0 ? Palette.Match : Palette.Mismatch;
        }

        /// <summary>The pop rises and fades over a second, beside the number (no allocation a frame).</summary>
        private void StepEarnPop(float delta)
        {
            _popAge = Mathf.Min(EarnPopSeconds, _popAge + delta);
            float t = _popAge / EarnPopSeconds;
            _earnPop.Visible = t < 1f && _wallet.Visible && _popAmount != 0;
            if (!_earnPop.Visible) return;
            float half = TextWidth(_wallet) * 0.5f;
            const float m = SafeMargin;
            float rise = EarnPopRise * t;
            Place(_earnPop, 0.5f, 0f, half + 10f, m - rise, half + 150f, m + 26f - rise);
            _earnPop.Modulate = new Color(_earnPop.Modulate, 1f - t * t);
        }

        /// <summary>A label anchored at (<paramref name="anchorX"/>, <paramref name="anchorY"/>) with pixel offsets from that point.</summary>
        private Label AddLabel(string name, int fontSize, float anchorX, float anchorY, float left, float top, float right, float bottom, HorizontalAlignment align, bool display = false)
        {
            var label = display ? MakeDisplayLabel(name, fontSize, align) : MakeLabel(name, fontSize, align);
            Place(label, anchorX, anchorY, left, top, right, bottom);
            AddChild(label);
            return label;
        }

        private MeterBar AddBar(string name, float anchorX, float anchorY, float left, float top, float right, float bottom, Color fill)
        {
            var bar = new MeterBar { Name = name, Fill = fill };
            Place(bar, anchorX, anchorY, left, top, right, bottom);
            AddChild(bar);
            return bar;
        }

        /// <summary>A tracker plate at the bottom right, <paramref name="slot"/> rows up; returns its line.</summary>
        private Label AddTracker(string name, int slot, Color mark)
        {
            var row = new TrackerRow(MakeLabel(name, 15, HorizontalAlignment.Left)) { Name = name + "Row", Mark = mark };
            float bottom = -SafeMargin - slot * (TrackerRow.RowHeight + 6f);
            Place(row, 1f, 1f, -SafeMargin - TrackerWidth, bottom - TrackerRow.RowHeight, -SafeMargin, bottom);
            AddChild(row);
            _trackerRows.Add(row);
            return row.Line;
        }

        /// <summary>The tracker plates; the shop's cards take their corner while they are up (the wallet is on the cards' title).</summary>
        private readonly System.Collections.Generic.List<Control> _trackerRows = new System.Collections.Generic.List<Control>();

        private static void Place(Control control, float anchorX, float anchorY, float left, float top, float right, float bottom)
        {
            control.AnchorLeft = anchorX;
            control.AnchorRight = anchorX;
            control.AnchorTop = anchorY;
            control.AnchorBottom = anchorY;
            control.OffsetLeft = left;
            control.OffsetTop = top;
            control.OffsetRight = right;
            control.OffsetBottom = bottom;
        }

        internal static Label MakeLabel(string name, int fontSize, HorizontalAlignment align)
        {
            var label = new Label
            {
                Name = name,
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                ThemeTypeVariation = UiTheme.Hud,
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            return label;
        }

        /// <summary>A number, a title or a banner: <see cref="MakeLabel"/> in the display face.</summary>
        internal static Label MakeDisplayLabel(string name, int fontSize, HorizontalAlignment align)
        {
            var label = MakeLabel(name, fontSize, align);
            label.ThemeTypeVariation = UiTheme.Display;
            return label;
        }
    }
}
