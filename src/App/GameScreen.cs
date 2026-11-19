using Godot;
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
        public PlayersView Players { get; private set; } = null!;
        public EnemiesView Enemies { get; private set; } = null!;
        public CombatEffects Effects { get; private set; } = null!;
        /// <summary>The raid brings the night (as in the concept video); look-dev sets the hour itself.</summary>
        public bool DriveAtmosphere = true;
        public FirstPersonCamera Camera { get; private set; } = null!;
        public Hud Hud { get; private set; } = null!;
        public InventoryPanel Inventory { get; private set; } = null!;
        public LabelDeclutterer Labels { get; private set; } = null!;

        private GameSession _session = null!;
        private PlayerInputController? _input;
        private bool _inventoryOpen;

        /// <param name="humanInput">False when something else (the smoke's bot) drives the client.</param>
        public void Setup(GameSession session, bool humanInput)
        {
            _session = session;
            var client = session.Client;

            World = new WorldView { Name = "World" };
            World.Build(client.World!.Map);
            AddChild(World);

            Players = new PlayersView { Name = "Players" };
            AddChild(Players);
            Players.Setup(client);

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
            layer.AddChild(Hud);
            Hud.Profile = session.Profile;
            Hud.Setup(client, session.Interaction, session.Feed);
            Inventory = new InventoryPanel();
            layer.AddChild(Inventory);
            Inventory.Setup(client);

            // World labels thin out with distance, give way to nearer ones and stay off the HUD.
            Labels = new LabelDeclutterer { Name = "Labels", KeepClear = Hud.KeepClear };
            AddChild(Labels);

            if (!humanInput) return;
            _input = new PlayerInputController { Name = "Input" };
            _input.Setup(client, session.Interaction);
            _input.InventoryToggled += () => _inventoryOpen = !_inventoryOpen;
            _input.Cancelled += () =>
            {
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
            // The shop's cards stand on the inventory screen, like an open chest.
            bool open = _inventoryOpen || world.OpenContainerId != 0 || world.ShopOpen;
            Inventory.Visible = open;
            if (_input != null) _input.UiOpen = open;
        }

        /// <summary>Dusk falls with the warning, night with the raiders, and the day is back when they are gone.</summary>
        public static float NightFor(RaidState raid) => raid == RaidState.None ? 0f : raid == RaidState.Warning ? 0.55f : 1f;

        public override void _ExitTree()
        {
            if (_input != null && _session != null) _session.InputSource = null;
        }
    }
}
