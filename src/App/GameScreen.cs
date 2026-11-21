using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.Combat;

namespace PerformativeMail.App
{
    /// <summary>
    /// Everything the player sees for a joined session: the world, the players, the camera, the HUD
    /// and the inventory panel. Reads the ClientWorld; writes nothing but input and requests.
    /// </summary>
    public partial class GameScreen : Node
    {
        public WorldView World { get; private set; } = null!;
        public NodesView Nodes { get; private set; } = null!;
        public ConstructsView Constructs { get; private set; } = null!;
        public PlayersView Players { get; private set; } = null!;
        public VehiclesView Vehicles { get; private set; } = null!;
        public EnemiesView Enemies { get; private set; } = null!;
        public CombatEffects Effects { get; private set; } = null!;
        /// <summary>The raid brings the night (as in the concept video); look-dev sets the hour itself.</summary>
        public bool DriveAtmosphere = true;
        public FirstPersonCamera Camera { get; private set; } = null!;
        public Hud Hud { get; private set; } = null!;
        public InventoryPanel Inventory { get; private set; } = null!;
        /// <summary>The map overlay (doc 09 §2.6), M.</summary>
        public MapPanel Map { get; private set; } = null!;
        public LabelDeclutterer Labels { get; private set; } = null!;
        /// <summary>Whose the mouse is: the view's, or the pointer's while a panel is open (or it was let go).</summary>
        public MouseCapture Mouse { get; } = new MouseCapture();

        private GameSession _session = null!;
        private PlayerInputController? _input;
        private bool _inventoryOpen, _draftAside;

        private bool _humanInput;

        /// <param name="humanInput">False when something else (the smoke's bot) drives the client.</param>
        public void Setup(GameSession session, bool humanInput)
        {
            _session = session;
            _humanInput = humanInput;
            Build();
        }

        // The host picked another world in the Lobby: everything drawn of the old one goes.
        public override void _EnterTree() => _session.Client.WorldReplaced += Rebuild;

        /// <summary>
        /// The world was replaced (doc 06 §5): the views of the old map are let go and made anew
        /// for the new one, out of the tree as the first time (controls placed by an anchor preset
        /// inside the tree would keep the rect they had), then the screen goes back where it stood.
        /// </summary>
        private void Rebuild()
        {
            var parent = GetParent();
            int index = GetIndex();
            parent.RemoveChild(this);
            _input = null;
            foreach (var child in GetChildren())
            {
                RemoveChild(child);
                child.QueueFree();
            }
            _inventoryOpen = false;
            Build();
            parent.AddChild(this);
            parent.MoveChild(this, index);
        }

        private void Build()
        {
            var session = _session;
            bool humanInput = _humanInput;
            var client = session.Client;

            World = new WorldView { Name = "World" };
            World.Build(client.World!.Map, client.World.Nodes);
            AddChild(World);
            Nodes = new NodesView { Name = "Nodes" };
            AddChild(Nodes);
            Nodes.Setup(client);
            Constructs = new ConstructsView { Name = "Constructs" };
            AddChild(Constructs);
            Constructs.Setup(client, session.Build);

            Players = new PlayersView { Name = "Players" };
            AddChild(Players);
            Players.Setup(client);
            // After the players in the tree: a ridden bike goes where its rider was just drawn.
            Vehicles = new VehiclesView { Name = "Vehicles" };
            AddChild(Vehicles);
            Vehicles.Setup(client, Players);

            Enemies = new EnemiesView { Name = "Enemies" };
            AddChild(Enemies);
            Enemies.Setup(client);
            Effects = new CombatEffects { Name = "Effects" };
            AddChild(Effects);
            Effects.Setup(client, Players);

            // After the players in the tree, so each frame it sits on where the local view just moved to.
            Camera = new FirstPersonCamera { Name = "Camera", Target = Players.LocalView };
            AddChild(Camera);
            Camera.Setup(client);

            var layer = new CanvasLayer { Name = "UI" };
            AddChild(layer);
            Hud = new Hud();
            // Under the HUD: the map is an overlay, the clock and the plates stay on top of it.
            Map = new MapPanel { Name = "Map" };
            layer.AddChild(Map);
            Map.Setup(client);
            layer.AddChild(Hud);
            Hud.Profile = session.Profile;
            Hud.Setup(client, session.Interaction, session.Feed);
            Hud.Build = session.Build;
            Hud.Mouse = Mouse;
            Inventory = new InventoryPanel();
            layer.AddChild(Inventory);
            Inventory.Setup(client);
            Hud.Panel = Inventory;
            Hud.MapOpen = () => Map.Visible;
            Map.BottomClear = () => Hud.FeedTop;

            // World labels thin out with distance, give way to nearer ones and stay off the HUD.
            Labels = new LabelDeclutterer { Name = "Labels", KeepClear = Hud.KeepClear };
            AddChild(Labels);

            if (!humanInput) return;
            _input = new PlayerInputController { Name = "Input" };
            _input.Setup(client, session.Interaction, Mouse, session.Build);
            _input.InventoryToggled += () => _inventoryOpen = !_inventoryOpen;
            _input.MapToggled += Map.Toggle;
            _input.MapOpen = () => Map.Visible;
            _input.Cancelled += () =>
            {
                // Esc closes what is open; with only the Draft's cards up it sets them aside, to look around before choosing by key.
                if (!InventoryOpen(client.World)) _draftAside = true;
                _inventoryOpen = false;
                client.CloseContainer();
                client.CloseShop();
            };
            AddChild(_input);
            session.InputSource = _input.Sample;
        }

        public override void _Process(double delta)
        {
            var world = _session?.Client.World;
            if (world == null) return;
            Camera.Target = Players.LocalView;
            if (DriveAtmosphere) World.Atmosphere.TargetNight = NightFor(world.Raid);
            World.ShowDistricts(world.Global.Shift);
            bool open = InventoryOpen(world);
            Inventory.Visible = open;
            // The Draft's cards take a click until the local player has chosen; then the mouse is the view's again.
            if (!world.ChoosingCard) _draftAside = false;
            Mouse.SetPanelOpen(open || (world.ChoosingCard && !_draftAside));
        }

        /// <summary>The shop's cards stand on the inventory screen, like an open chest.</summary>
        private bool InventoryOpen(ClientWorld? world) => _inventoryOpen || (world != null && (world.OpenContainerId != 0 || world.ShopOpen));

        /// <summary>Dusk falls with the warning, night with the raiders, and the day is back when they are gone.</summary>
        public static float NightFor(RaidState raid) => raid == RaidState.None ? 0f : raid == RaidState.Warning ? 0.55f : 1f;

        public override void _ExitTree()
        {
            if (_session != null) _session.Client.WorldReplaced -= Rebuild;
            if (_input != null && _session != null) _session.InputSource = null;
        }
    }
}
