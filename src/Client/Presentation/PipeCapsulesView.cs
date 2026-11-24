using Godot;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The capsules in the pneumatic pipes (doc 04 §2.2): one instance per capsule, where the
    /// client's pipes have it this frame, lying along the tube in the colour of the house its mail
    /// is for, seen through the glass. Under the ground (an underground pipe) none is drawn.
    /// </summary>
    public partial class PipeCapsulesView : Node3D
    {
        private ClientRuntime? _client;
        private MultiMesh? _mesh;

        /// <summary>Capsules drawn as of the last frame.</summary>
        public int Drawn { get; private set; }

        public void Setup(ClientRuntime client)
        {
            _client = client;
            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = MeshKit.Capsule(0.13f, 0.56f, 12),
                InstanceCount = 64,
                VisibleInstanceCount = 0,
            };
            AddChild(new MultiMeshInstance3D
            {
                Name = "PipeCapsules",
                Multimesh = _mesh,
                MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.35f, Metallic = 0.3f },
            });
        }

        public override void _Process(double delta) => Refresh();

        public void Refresh()
        {
            var world = _client?.World;
            if (world == null || _mesh == null) return;
            var pipes = world.Pipes;
            int n = pipes.Capsules.Count;
            if (n > _mesh.InstanceCount)
            {
                _mesh.VisibleInstanceCount = 0;
                _mesh.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)n);
            }
            int i = 0;
            float ahead = _client!.TickFraction * SimConstants.TickDt * pipes.Speed;
            foreach (var cap in pipes.Capsules)
            {
                pipes.PointAt(cap, ahead, out float x, out float z, out int dir, out int tile);
                var under = world.Constructs.At(tile % world.Map.Cols, tile / world.Map.Cols);
                if (under != null && under.Def.PipeRole == PipeRole.Underground) continue;
                float y = world.Map.HeightAt(x, z) + ConstructsView.PipeAxis;
                var basis = new Basis(Vector3.Up, dir * -Mathf.Pi * 0.5f) * new Basis(Vector3.Forward, Mathf.Pi * 0.5f);
                _mesh.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y, z)));
                var dest = world.Map.FindDestination(cap.Mail.Address);
                _mesh.SetInstanceColor(i, dest != null ? Palette.HouseColour(dest.Variant) : new Color("f2ead8"));
                i++;
            }
            _mesh.VisibleInstanceCount = i;
            Drawn = i;
        }
    }
}
