using System;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.World
{
    /// <summary>
    /// The resource nodes of a map (doc 02 §3.9) and what each has left. The nodes are the trees,
    /// bushes and boulders the map is dressed with (<see cref="Scenery"/>), told apart by
    /// content/nodes; a node is known by its index into <see cref="WorldMap.Props"/>. The server
    /// holds one, and every client mirrors it.
    /// </summary>
    public sealed class NodeField
    {
        private readonly NodeDef?[] _defs;
        private readonly byte[] _hits;

        /// <summary>Props of the map, nodes or not.</summary>
        public int Count => _defs.Length;
        /// <summary>How many of them are nodes.</summary>
        public int NodeCount { get; }
        /// <summary>Goes up whenever a node runs out or is whole again: what a view redraws by.</summary>
        public int Version { get; private set; }

        public NodeField(ContentDb content, WorldMap map)
        {
            _defs = new NodeDef?[map.Props.Count];
            _hits = new byte[_defs.Length];
            for (int i = 0; i < _defs.Length; i++)
            {
                var def = content.NodeFor(map.Props[i]);
                if (def == null) continue;
                _defs[i] = def;
                _hits[i] = (byte)def.Hits;
                NodeCount++;
            }
        }

        /// <summary>What the prop is as a node; null for scenery and for an index off the map.</summary>
        public NodeDef? Def(int index) => (uint)index < (uint)_defs.Length ? _defs[index] : null;

        public int HitsLeft(int index) => (uint)index < (uint)_hits.Length ? _hits[index] : 0;

        public bool Depleted(int index) => Def(index) != null && _hits[index] == 0;

        /// <summary>True for a node that is not as it grew: what a late joiner has to be told.</summary>
        public bool Touched(int index)
        {
            var def = Def(index);
            return def != null && _hits[index] != def.Hits;
        }

        public void Set(int index, int hitsLeft)
        {
            var def = Def(index);
            if (def == null) return;
            byte value = (byte)Math.Clamp(hitsLeft, 0, def.Hits);
            if (_hits[index] == value) return;
            if ((_hits[index] == 0) != (value == 0)) Version++;
            _hits[index] = value;
        }

        /// <summary>
        /// Makes whole again what regrows, or everything; true if that changed a node.
        /// </summary>
        public bool Regrow(bool all)
        {
            bool changed = false;
            for (int i = 0; i < _defs.Length; i++)
            {
                var def = _defs[i];
                if (def == null || _hits[i] == def.Hits || !(all || def.Regrows)) continue;
                Set(i, def.Hits);
                changed = true;
            }
            return changed;
        }
    }
}
