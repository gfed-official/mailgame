using System;
using System.Collections.Generic;
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
        public string PromptText => _prompt.Text;
        /// <summary>The line over the hotbar: what is in the hand.</summary>
        public string HeldText => _held.Visible ? _held.Text : "";
        /// <summary>Where the held stack's name (or Build mode's keys) stands on screen.</summary>
        public Rect2 HeldRect => _held.GetGlobalRect();
        /// <summary>The lowest point of the event feed's lines on screen.</summary>
        public float FeedBottom => _feedLines[_feedLines.Length - 1].GetGlobalRect().End.Y;
        /// <summary>The top of the event feed's first line.</summary>
        public float FeedTop => _feedLines[0].GetGlobalRect().Position.Y;
        /// <summary>True while the map is up: the interact prompt keeps off it.</summary>
        public Func<bool>? MapOpen;
        /// <summary>Where the map is drawn while it is up: kept clear of world labels like the HUD's panels.</summary>
        public Func<Rect2>? MapArea;
        public string HintText => _hint.Text;
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
        /// <summary>The recipe of the bar that is selected, -1 while the bar is down.</summary>
        public int RecipeShown { get; private set; } = -1;

        private IEnumerable<string> RecipeCards()
        {
            foreach (var child in _recipeBar.GetChildren())
                if (child is Label label) yield return label.Text;
        }

        /// <summary>The lines along the edges, for the smoke's "nothing is clipped" check.</summary>
        public Control[] TopRow => new Control[] { _phase, _status, _quota, _wallet, _rank };
        /// <summary>The top group from the top down: quota bar, the shift's line, clock bar. No two of them may touch (nit 14).</summary>
        public Control[] TopStack => new Control[] { _quotaBar, _phase, _timeBar, _hint };
        public Control TopPlate => _topPlate;

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
            if (_hint.Visible && _hint.Text.Length > 0) AddClear(_hint, HintClearWidth);
            if (_banner.Visible) AddClear(_banner, BannerClearWidth);
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

        private const float HintClearWidth = 560f, BannerClearWidth = 640f;
        private readonly List<ScreenBox> _keepClear = new List<ScreenBox>();
        private Panel _topPlate = null!;

        private ClientRuntime _client = null!;
        private InteractionController _interaction = null!;
        private EventFeed _feed = null!;

        private Label _phase = null!, _status = null!, _tally = null!, _quota = null!, _wallet = null!, _rank = null!, _raid = null!, _bag = null!, _perks = null!;
        /// <summary>The Lobby's plates (doc 09 §2.2): the world and its seed, the kit, the host's keys for them.</summary>
        private Label _world = null!, _kit = null!, _worldKeys = null!;
        public string WorldText => _world.Visible ? _world.Text : "";
        public string KitText => _kit.Visible ? _kit.Text : "";
        public string WorldKeysText => _worldKeys.Visible ? _worldKeys.Text : "";
        private DraftPanel _draft = null!;
        private GeneratingPanel _generating = null!;
        private Label _prompt = null!, _held = null!, _hint = null!, _paused = null!, _mouseNote = null!, _banner = null!, _hp = null!, _weapons = null!;
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
        private const float RecipeBarHalfWidth = 280f, RecipeBarHeight = 58f;
        private readonly Label[] _feedLines = new Label[EventFeed.MaxEntries];
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

            // Top centre: what the shift has earned of its quota, the shift and its phase, the time left,
            // on a plate of their own so that the world behind cannot be read into them (nit 14).
            _topPlate = new Panel { Name = "TopPlate", MouseFilter = MouseFilterEnum.Ignore };
            var plate = new StyleBoxFlat { BgColor = new Color(0.06f, 0.1f, 0.16f, 0.6f), AntiAliasing = true };
            plate.SetCornerRadiusAll(10);
            _topPlate.AddThemeStyleboxOverride("panel", plate);
            Place(_topPlate, 0.5f, 0f, -BarWidth * 0.5f - 14f, m - 10f, BarWidth * 0.5f + 14f, m + 70f);
            AddChild(_topPlate);
            _quotaBar = AddBar("QuotaBar", 0.5f, 0f, -BarWidth * 0.5f, m, BarWidth * 0.5f, m + 8f, QuotaColour);
            _phase = AddLabel("Phase", 20, 0.5f, 0f, -350f, m + 12f, 350f, m + 48f, HorizontalAlignment.Center);
            _timeBar = AddBar("TimeBar", 0.5f, 0f, -BarWidth * 0.5f, m + 52f, BarWidth * 0.5f, m + 60f, Colors.White);

            // What the run waits for and what the "go" key does, under the plate.
            _hint = AddLabel("Hint", 18, 0.5f, 0f, -520f, m + 74f, 520f, m + 102f, HorizontalAlignment.Center);
            _hint.Modulate = Palette.Amber;

            // The raid warning and the countdown while down: across the view, above the crosshair.
            _banner = AddLabel("Banner", 28, 0.5f, 0.25f, -520f, -20f, 520f, 20f, HorizontalAlignment.Center);
            _banner.Visible = false;

            // Below the crosshair, clear of the Payday / Results panel a pause can sit on top of.
            _paused = AddLabel("Paused", 44, 0.5f, 0.6f, -300f, -32f, 300f, 32f, HorizontalAlignment.Center);
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
            var back = new StyleBoxFlat { BgColor = new Color(0.08f, 0.13f, 0.21f, 0.9f), BorderColor = new Color(0.45f, 0.72f, 0.86f, 0.35f) };
            back.SetCornerRadiusAll(9);
            back.SetBorderWidthAll(1);
            back.SetContentMarginAll(22f);
            _overlay.AddThemeStyleboxOverride("panel", back);
            _overlayLines = new VBoxContainer { Name = "Lines", MouseFilter = MouseFilterEnum.Ignore };
            _overlayLines.AddThemeConstantOverride("separation", 6);
            _overlay.AddChild(_overlayLines);
            AddChild(_overlay);

            // The cards of the Draft, where the Payday summary stood a moment ago.
            _draft = new DraftPanel();
            _draft.Setup(client);
            AddChild(_draft);
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
            var promptRow = new HBoxContainer
            {
                Name = "PromptRow",
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.62f, AnchorBottom = 0.62f,
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
                AnchorTop = 0.62f,
                AnchorBottom = 0.62f,
                OffsetLeft = -130f,
                OffsetRight = 130f,
                OffsetTop = 40f,
                OffsetBottom = 52f,
            };
            AddChild(_hold);

            // Bottom left: health in red with its number, stamina in five blocks; the feed above them.
            _healthBar = AddBar("Health", 0f, 1f, m, -m - 40f, m + 180f, -m - 28f, HealthColour);
            _hp = AddLabel("HealthNumber", 14, 0f, 1f, m + 188f, -m - 46f, m + 260f, -m - 22f, HorizontalAlignment.Left);
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
            _wallet = AddTracker("Wallet", 0, new Color("ffd24a"));
            _quota = AddTracker("Quota", 1, QuotaColour);
            // Between runs the quota line is empty, so the rank takes its place above the wallet.
            _rank = AddTracker("Rank", 1, new Color("c3a6ff"));
            _status = AddTracker("Status", 2, new Color("5bc0eb"));
            _tally = AddTracker("Tally", 3, Palette.Match);
            _perks = AddTracker("Perks", 4, PerkColour);
            _raid = AddTracker("Raid", 5, HealthColour);
            _bag = AddTracker("Bag", 6, BagColour);
            // In the Lobby, where the shift's plates are empty.
            _world = AddTracker("World", 2, new Color("7fd1b9"));
            _kit = AddTracker("Kit", 3, new Color("f4a261"));
            _worldKeys = AddTracker("WorldKeys", 4, new Color("b8c4d0"));

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

            // Build mode's recipes (doc 09 §2.7), a card each, where the hotbar is (the hands are on
            // the tools of building then), in as many rows as they take, clear of the feed and the trackers.
            _recipeBar = new HFlowContainer
            {
                Name = "RecipeBar",
                Alignment = FlowContainer.AlignmentMode.Center,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            _recipeBar.AddThemeConstantOverride("h_separation", 18);
            _recipeBar.AddThemeConstantOverride("v_separation", 1);
            // Three rows of the small font fit between the feed and the bottom edge, the keys over them.
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
            _quotaBar.Visible = !between;
            _quotaBar.Value = global.Quota > 0 ? global.ShiftEarnings / (float)global.Quota : 0f;
            _quotaBar.Fill = global.ShiftEarnings >= global.Quota && global.Phase != RunPhase.Prep ? Palette.Match : QuotaColour;

            _status.Visible = _tally.Visible = !between;
            _status.Text = HudText.Status(global);
            _tally.Text = HudText.Tally(global);
            _quota.Text = HudText.Quota(global);
            _quota.Modulate = global.ShiftEarnings >= global.Quota ? Palette.Match : Colors.White;
            _wallet.Text = HudText.Wallet(global);
            _wallet.Modulate = global.Wallet < 0 ? Palette.Mismatch : Colors.White;
            _rank.Visible = Profile != null && between;
            if (_rank.Visible) _rank.Text = HudText.Rank(Profile!.Profile, HudText.RankXpPerRank(world.Content));
            bool lobby = global.Phase == RunPhase.Lobby;
            _world.Visible = _kit.Visible = lobby;
            _worldKeys.Visible = lobby && world.IsHost;
            if (lobby)
            {
                _world.Text = HudText.WorldTracker(world);
                _kit.Text = HudText.KitTracker(world);
                _worldKeys.Text = HudText.WorldKeys;
            }
            _perks.Text = HudText.PerkTracker(world);
            _perks.Visible = _perks.Text.Length > 0;
            _raid.Text = HudText.RaidTracker(world);
            _raid.Visible = _raid.Text.Length > 0;
            _bag.Text = HudText.BagTracker(world);
            _bag.Visible = _bag.Text.Length > 0;
            foreach (var row in _trackerRows) row.Visible = !world.ShopOpen;
            // A lost host freezes the world where it stood (doc 06 §5); the banner says why nothing moves.
            bool lost = _client!.State == ClientState.Disconnected;
            _hint.Text = lost ? HudText.HostLostHint : HudText.PhaseHint(world);
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
            _weapons.Visible = !world.Local.Dead && (world.Raid != RaidState.None || world.Enemies.Count > 0);
            if (_weapons.Visible) _weapons.Text = HudText.Weapons(_client.CannonAmmo);

            var prompt = _interaction.Prompt;
            bool building = Build != null && Build.Active;
            float holdProgress = prompt.HoldProgress;
            PromptTone tone;
            // In Build mode the line is the ghost's, but a panel that is open still says how to close it.
            bool closing = prompt.Kind == InteractKind.CloseChest || prompt.Kind == InteractKind.CloseIntake || prompt.Kind == InteractKind.CloseBag || prompt.Kind == InteractKind.CloseShop;
            if (building && !closing)
            {
                var ghost = Build!.Ghost;
                _prompt.Text = HudText.BuildPrompt(world, ghost, out tone);
                holdProgress = ghost.HoldProgress;
            }
            else _prompt.Text = HudText.Prompt(world, prompt, out tone);
            if (MapOpen?.Invoke() == true)
            {
                _prompt.Text = "";
                tone = PromptTone.Neutral;
                holdProgress = 0f;
            }
            _prompt.Modulate = tone == PromptTone.Match ? Palette.Match : tone == PromptTone.Mismatch ? Palette.Mismatch : Colors.White;
            _mark.Tone = _prompt.Text.Length > 0 ? tone : PromptTone.Neutral;
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
            _held.OffsetBottom = building ? -m - RecipeBarHeight : -GridView.Cell - m - 4f;
            if (building)
            {
                _held.Text = HudText.BuildKeys;
                _held.Modulate = Palette.Amber;
                _held.Visible = true;
            }

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
            var recipes = world.Content.Recipes;
            if (_recipeBar.GetChildCount() != recipes.Count)
            {
                foreach (var child in _recipeBar.GetChildren())
                {
                    _recipeBar.RemoveChild(child);
                    child.QueueFree();
                }
                for (int i = 0; i < recipes.Count; i++) _recipeBar.AddChild(MakeLabel($"Recipe{i}", 13, HorizontalAlignment.Center));
            }
            RecipeShown = Build!.RecipeIndex;
            for (int i = 0; i < recipes.Count; i++)
            {
                var label = (Label)_recipeBar.GetChild(i);
                var state = world.RecipeState(recipes[i]);
                label.Text = HudText.RecipeCard(recipes[i], i);
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
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            return label;
        }
    }
}
