using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>Greybox player: a capsule with a visor (so facing is readable), a name tag and an envelope when mail is in hand.</summary>
    public partial class PlayerView : Node3D
    {
        private MeshInstance3D _held = null!;

        public void Setup(string playerName, int slot, bool local)
        {
            var colour = Palette.PlayerColour(slot);
            AddChild(new MeshInstance3D
            {
                Name = "Body",
                Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f, Material = Palette.Flat(colour) },
                Position = new Vector3(0f, 0.9f, 0f),
            });
            // Yaw 0 faces −Z (the sim's convention and Godot's).
            AddChild(new MeshInstance3D
            {
                Name = "Visor",
                Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.16f, 0.2f), Material = Palette.Flat(new Color("1b1b1e")) },
                Position = new Vector3(0f, 1.5f, -0.32f),
            });
            _held = new MeshInstance3D
            {
                Name = "HeldMail",
                Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.24f, 0.04f), Material = Palette.Flat(new Color("fff8e7")) },
                Position = new Vector3(0.28f, 1.1f, -0.46f),
                Visible = false,
            };
            AddChild(_held);
            if (!local)
            {
                AddChild(new Label3D
                {
                    Name = "NameTag",
                    Text = playerName,
                    Position = new Vector3(0f, 2.25f, 0f),
                    FontSize = 48,
                    PixelSize = 0.008f,
                    OutlineSize = 8,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                });
            }
        }

        public void SetHoldingMail(bool holding) => _held.Visible = holding;
    }
}
