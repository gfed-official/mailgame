using Godot;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The map's resource nodes (doc 02 §3.9): the trees, bushes and boulders that give something,
    /// drawn as the <see cref="NodeField"/> has them. They are batched like the rest of the town
    /// and drawn anew only when one runs out or is whole again.
    /// </summary>
    public partial class NodesView : Node3D
    {
        /// <summary>Nodes drawn with something left, and worked out, as of the last redraw.</summary>
        public int Whole { get; private set; }
        public int Bare { get; private set; }
        public int Redraws { get; private set; }

        private ClientRuntime? _client;
        private NodeField? _drawn;
        private int _version;

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Refresh();
        }

        public override void _Process(double delta) => Refresh();

        /// <summary>Draws the nodes anew if any of them has changed since they were drawn.</summary>
        public void Refresh()
        {
            var world = _client?.World;
            if (world == null) return;
            var nodes = world.Nodes;
            if (ReferenceEquals(nodes, _drawn) && nodes.Version == _version) return;
            _drawn = nodes;
            _version = nodes.Version;

            foreach (var child in GetChildren())
            {
                RemoveChild(child);
                child.QueueFree();
            }
            var batch = new MeshBatch();
            Whole = Bare = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var def = nodes.Def(i);
                if (def == null) continue;
                bool depleted = nodes.Depleted(i);
                SceneryView.ResourceNode(batch, world.Map, world.Map.Props[i], def, depleted);
                if (depleted) Bare++;
                else Whole++;
            }
            batch.Flush(this, "Nodes");
            Redraws++;
        }
    }
}
