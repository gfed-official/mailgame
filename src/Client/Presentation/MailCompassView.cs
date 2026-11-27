using Godot;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.Perks;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Doc 10 §4.1 Local Knowledge: "mail in your inventory shows a compass arrow to its mailbox". No new HUD widget
    /// (ADR-0018): a second edge marker, the under-attack marker's kind in teal, points to the mailbox of the mail in
    /// hand (the map's way-finding target: the held slot's mail, else the first in the hotbar) — over it in view, on
    /// the screen's edge where not — with the house number and how far.
    /// </summary>
    public partial class MailCompassView : Node
    {
        public static readonly Color Teal = new Color("4fd1c5");
        private const float MarkerMargin = 48f, MarkerHeight = 1.6f;

        private ClientRuntime _client = null!;
        private Camera3D _camera = null!;
        private AttackMarker _marker = null!;

        public void Setup(ClientRuntime client, Camera3D camera, AttackMarker marker)
        {
            _client = client;
            _camera = camera;
            _marker = marker;
        }

        public override void _Process(double delta)
        {
            var world = _client?.World;
            if (world == null || _camera == null) return;
            var held = world.HasLocal && !world.Local.Dead && world.LocalHas(RuleFlag.LocalKnowledge) ? MapGuide.MailInHand(world, _client!.Input.HeldSlot) : null;
            var dest = held == null ? null : world.Map.FindDestination(held.Stack.Address);
            if (dest == null) { _marker.Visible = false; return; }
            var at = new Vector3(dest.MailboxX, world.Map.HeightAt(dest.MailboxX, dest.MailboxZ) + MarkerHeight, dest.MailboxZ);
            bool behind = _camera.IsPositionBehind(at);
            var p = _camera.UnprojectPosition(at);
            var screen = _marker.GetViewportRect().Size;
            bool over = EdgeMarker.Place(p.X, p.Y, behind, screen.X, screen.Y, MarkerMargin, out float x, out float y, out float angle);
            float dx = dest.MailboxX - world.Local.X, dz = dest.MailboxZ - world.Local.Z;
            var (tag, words) = HudText.MailCompass(world, dest, Mathf.Sqrt(dx * dx + dz * dz));
            _marker.Place(new Vector2(x, y), angle, over, words, tag);
        }
    }
}
