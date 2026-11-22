using Godot;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The mail riding the belts (doc 07 §8 BeltSegmentView: "instances item meshes via MultiMesh
    /// along the segment path"): one instance per item, where the client's belt sim has it this
    /// frame, in its lane, sized by its kind — an envelope lies flat, a parcel stands — and in the
    /// colour of the house it is for, so a line of mail shows where it is going.
    /// </summary>
    public partial class BeltItemsView : Node3D
    {
        private ClientRuntime? _client;
        private MultiMeshInstance3D? _instances;
        private MultiMesh? _mesh;

        /// <summary>Items drawn as of the last frame.</summary>
        public int Drawn { get; private set; }

        public void Setup(ClientRuntime client)
        {
            _client = client;
            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = MeshKit.RoundedBox(Vector3.One, 0.08f, 2),
                InstanceCount = 256,
                VisibleInstanceCount = 0,
            };
            _instances = new MultiMeshInstance3D
            {
                Name = "BeltItems",
                Multimesh = _mesh,
                MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f },
            };
            AddChild(_instances);
        }

        public override void _Process(double delta) => Refresh();

        public void Refresh()
        {
            var world = _client?.World;
            if (world == null || _mesh == null) return;
            var belts = world.Belts;
            int n = belts.ItemCount;
            if (n > _mesh.InstanceCount)
            {
                _mesh.VisibleInstanceCount = 0;
                _mesh.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)n);
            }
            int i = 0;
            // Between ticks the items go on a little at their speed, as far as the belt lets them.
            float ahead = _client!.TickFraction * SimConstants.TickDt;
            foreach (var s in belts.Segments)
            {
                foreach (var it in s.Items)
                {
                    if (i >= _mesh.InstanceCount) break;
                    float pos = Mathf.Min(it.Pos + s.Speed * ahead, s.Length) - it.Length * 0.5f;
                    belts.PointAt(s, Mathf.Max(0f, pos), it.Lane, out float x, out float z, out int dir);
                    var kind = it.Mail.Kind < world.Content.MailKinds.Count ? world.Content.MailKinds[it.Mail.Kind] : null;
                    var size = Size(kind?.Id);
                    float y = world.Map.HeightAt(x, z) + ConstructsView.BeltTop + 0.04f + size.Y * 0.5f;
                    var basis = new Basis(Vector3.Up, dir * -Mathf.Pi * 0.5f).Scaled(size);
                    _mesh.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y, z)));
                    var dest = world.Map.FindDestination(it.Mail.Address);
                    var colour = dest != null ? Palette.HouseColour(dest.Variant) : new Color("f2ead8");
                    // Paper is pale with the house's colour; a parcel is cardboard with it as a label.
                    _mesh.SetInstanceColor(i, kind != null && kind.MaxStack > 1 ? colour.Lerp(Colors.White, 0.45f) : colour.Lerp(new Color("b98a55"), 0.55f));
                    i++;
                }
            }
            _mesh.VisibleInstanceCount = i;
            Drawn = i;
        }

        /// <summary>Along, up, across: the size of a kind of mail on a belt.</summary>
        private static Vector3 Size(string? kind) => kind switch
        {
            "postcard" => new Vector3(0.3f, 0.03f, 0.2f),
            "letter" => new Vector3(0.36f, 0.05f, 0.24f),
            "small" => new Vector3(0.36f, 0.24f, 0.3f),
            "medium" => new Vector3(0.42f, 0.36f, 0.4f),
            "large" => new Vector3(0.46f, 0.46f, 0.44f),
            "cargo" => new Vector3(1.9f, 0.9f, 1.3f),
            _ => new Vector3(0.36f, 0.2f, 0.3f),
        };
    }
}
