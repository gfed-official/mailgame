using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Client.UI;

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
        public FirstPersonCamera Camera { get; private set; } = null!;
        public Hud Hud { get; private set; } = null!;
        public InventoryPanel Inventory { get; private set; } = null!;

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

            // After the players in the tree, so each frame it sits on where the local view just moved to.
            Camera = new FirstPersonCamera { Name = "Camera", Target = Players.LocalView };
            AddChild(Camera);
            Camera.Setup(client);

            var layer = new CanvasLayer { Name = "UI" };
            AddChild(layer);
            Hud = new Hud();
            layer.AddChild(Hud);
            Hud.Setup(client, session.Interaction, session.Feed);
            Inventory = new InventoryPanel();
            layer.AddChild(Inventory);
            Inventory.Setup(client);

            if (!humanInput) return;
            _input = new PlayerInputController { Name = "Input" };
            _input.Setup(client, session.Interaction);
            _input.InventoryToggled += () => _inventoryOpen = !_inventoryOpen;
            _input.Cancelled += () =>
            {
                _inventoryOpen = false;
                client.CloseContainer();
            };
            AddChild(_input);
            session.InputSource = _input.Sample;
        }

        public override void _Process(double delta)
        {
            var world = _session?.Client.World;
            if (world == null) return;
            Camera.Target = Players.LocalView;
            bool open = _inventoryOpen || world.OpenContainerId != 0;
            Inventory.Visible = open;
            if (_input != null) _input.UiOpen = open;
        }

        public override void _ExitTree()
        {
            if (_input != null && _session != null) _session.InputSource = null;
        }
    }
}
