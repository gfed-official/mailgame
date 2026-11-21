using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.World
{
    /// <summary>What an edge of the routing graph is made of (doc 02 §3.8 "surface type").</summary>
    public enum RouteSurface : byte
    {
        /// <summary>A town street, 2 tiles wide: the road a vehicle is fast on.</summary>
        Paved = 0,
        /// <summary>A 1-tile path between towns (none on a Small Island yet).</summary>
        Dirt = 1,
    }

    public enum RouteNodeKind : byte
    {
        /// <summary>Where streets meet or one street goes on into the next stretch.</summary>
        Junction = 0,
        /// <summary>Where a street ends in town.</summary>
        End = 1,
        /// <summary>Where a street leaves the map: the way in from the rest of the island.</summary>
        MapEdge = 2,
    }

    /// <summary>
    /// The routing graph of a map (doc 02 §3.8, §4): a node where streets meet or end, an edge
    /// along every stretch of street centre line between two of them, with its length in metres,
    /// its surface and its street. Built from the map's streets, so the server and every client
    /// have the same graph without sending it. Routes run from any point to any point: on to the
    /// nearest street, along the graph by the shortest way (Dijkstra), off it to the goal.
    /// </summary>
    public sealed class RoutingGraph
    {
        public readonly struct Node
        {
            public readonly int Id;
            public readonly float X, Z;
            public readonly RouteNodeKind Kind;
            public Node(int id, float x, float z, RouteNodeKind kind) { Id = id; X = x; Z = z; Kind = kind; }
        }

        public readonly struct Edge
        {
            public readonly int A, B;
            public readonly float Length;
            public readonly RouteSurface Surface;
            /// <summary>The <see cref="StreetDef.Id"/> the edge runs along.</summary>
            public readonly int Street;
            public Edge(int a, int b, float length, RouteSurface surface, int street) { A = a; B = b; Length = length; Surface = surface; Street = street; }
        }

        /// <summary>Points of two streets nearer than this are one node: a street's end on another's side.</summary>
        private const float Snap = 2.5f;

        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly List<List<int>> _adjacent = new List<List<int>>();

        public IReadOnlyList<Node> Nodes => _nodes;
        public IReadOnlyList<Edge> Edges => _edges;
        /// <summary>The edges at a node, as indices into <see cref="Edges"/>.</summary>
        public IReadOnlyList<int> EdgesAt(int node) => _adjacent[node];

        public float TotalLength
        {
            get
            {
                float sum = 0f;
                foreach (var e in _edges) sum += e.Length;
                return sum;
            }
        }

        /// <summary>The centre line of a street: from (x0, z0) to (x1, z1), along x or along z.</summary>
        public static void CentreLine(StreetDef street, out float x0, out float z0, out float x1, out float z1)
        {
            const float tile = SimConstants.TileSize;
            var r = street.Rect;
            if (r.W >= r.H)
            {
                z0 = z1 = (r.Y + r.H * 0.5f) * tile;
                x0 = r.X * tile;
                x1 = (r.X + r.W) * tile;
            }
            else
            {
                x0 = x1 = (r.X + r.W * 0.5f) * tile;
                z0 = r.Y * tile;
                z1 = (r.Y + r.H) * tile;
            }
        }

        public static RoutingGraph Build(WorldMap map)
        {
            var g = new RoutingGraph();
            int count = map.Streets.Count;
            var lines = new (float X0, float Z0, float X1, float Z1, bool AlongX)[count];
            for (int i = 0; i < count; i++)
            {
                CentreLine(map.Streets[i], out float x0, out float z0, out float x1, out float z1);
                lines[i] = (x0, z0, x1, z1, map.Streets[i].Rect.W >= map.Streets[i].Rect.H);
            }
            // The points along each street: its ends, and where any other street meets or crosses it.
            var along = new List<float>[count];
            for (int i = 0; i < count; i++)
            {
                var l = lines[i];
                along[i] = new List<float> { l.AlongX ? l.X0 : l.Z0, l.AlongX ? l.X1 : l.Z1 };
            }
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    if (i == j || lines[i].AlongX == lines[j].AlongX) continue;
                    var a = lines[i];
                    var b = lines[j];
                    // a along x at a.Z0, b along z at b.X0: they meet at (b.X0, a.Z0) if both reach it.
                    float px = a.AlongX ? b.X0 : a.X0, pz = a.AlongX ? a.Z0 : b.Z0;
                    if (!Reaches(a, px, pz) || !Reaches(b, px, pz)) continue;
                    along[i].Add(a.AlongX ? px : pz);
                    // The crossings are nodes first, so a street's end on another's side snaps to the crossing.
                    g.NodeAt(px, pz);
                }
            }
            for (int i = 0; i < count; i++)
            {
                var l = lines[i];
                var points = along[i];
                points.Sort();
                int previous = -1;
                float previousAt = 0f;
                foreach (float at in points)
                {
                    float x = l.AlongX ? at : l.X0, z = l.AlongX ? l.Z0 : at;
                    int node = g.NodeAt(x, z);
                    if (previous >= 0 && node != previous && at - previousAt > 0.01f) g.AddEdge(previous, node, map.Streets[i].Id);
                    if (node != previous) { previous = node; previousAt = at; }
                }
            }
            // What a node is: where streets meet, where one ends in town, where one leaves the map.
            for (int n = 0; n < g._nodes.Count; n++)
            {
                var node = g._nodes[n];
                var kind = g._adjacent[n].Count >= 2 ? RouteNodeKind.Junction : RouteNodeKind.End;
                if (node.X <= 0.5f || node.Z <= 0.5f || node.X >= map.WidthM - 0.5f || node.Z >= map.DepthM - 0.5f) kind = RouteNodeKind.MapEdge;
                g._nodes[n] = new Node(n, node.X, node.Z, kind);
            }
            return g;
        }

        /// <summary>True if the line reaches the point, give or take a street's half width at its ends.</summary>
        private static bool Reaches((float X0, float Z0, float X1, float Z1, bool AlongX) l, float x, float z)
        {
            if (l.AlongX) return MathF.Abs(z - l.Z0) < 0.01f && x >= l.X0 - Snap && x <= l.X1 + Snap;
            return MathF.Abs(x - l.X0) < 0.01f && z >= l.Z0 - Snap && z <= l.Z1 + Snap;
        }

        private int NodeAt(float x, float z)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (MathF.Abs(_nodes[i].X - x) < Snap && MathF.Abs(_nodes[i].Z - z) < Snap) return i;
            _nodes.Add(new Node(_nodes.Count, x, z, RouteNodeKind.End));
            _adjacent.Add(new List<int>());
            return _nodes.Count - 1;
        }

        private void AddEdge(int a, int b, int street)
        {
            foreach (int e in _adjacent[a])
                if (_edges[e].A == b || _edges[e].B == b) return;
            float dx = _nodes[b].X - _nodes[a].X, dz = _nodes[b].Z - _nodes[a].Z;
            _edges.Add(new Edge(a, b, MathF.Sqrt(dx * dx + dz * dz), RouteSurface.Paved, street));
            _adjacent[a].Add(_edges.Count - 1);
            _adjacent[b].Add(_edges.Count - 1);
        }

        /// <summary>True if every node can be reached from every other.</summary>
        public bool Connected()
        {
            if (_nodes.Count == 0) return true;
            var seen = new bool[_nodes.Count];
            var stack = new Stack<int>();
            stack.Push(0);
            seen[0] = true;
            int reached = 1;
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                foreach (int e in _adjacent[n])
                {
                    int m = _edges[e].A == n ? _edges[e].B : _edges[e].A;
                    if (seen[m]) continue;
                    seen[m] = true;
                    reached++;
                    stack.Push(m);
                }
            }
            return reached == _nodes.Count;
        }

        /// <summary>The nearest point on any edge to a point, with the edge's index and how far off it is; -1 with no edges.</summary>
        public int NearestEdge(float x, float z, out float nearX, out float nearZ, out float distance)
        {
            int best = -1;
            nearX = x; nearZ = z; distance = float.MaxValue;
            for (int i = 0; i < _edges.Count; i++)
            {
                var a = _nodes[_edges[i].A];
                var b = _nodes[_edges[i].B];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                float t = len2 > 1e-6f ? Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / len2, 0f, 1f) : 0f;
                float px = a.X + dx * t, pz = a.Z + dz * t;
                float d = MathF.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                if (d < distance) { distance = d; best = i; nearX = px; nearZ = pz; }
            }
            return best;
        }

        /// <summary>
        /// The way from one point to another along the streets: the points to pass, from the start
        /// to the goal, into <paramref name="points"/>; returns its length in metres (the walks on
        /// to the street and off it included). With no streets, or both points nearest the same
        /// stretch of street, it is the straight line.
        /// </summary>
        public float Route(float fromX, float fromZ, float toX, float toZ, List<(float X, float Z)> points)
        {
            points.Clear();
            points.Add((fromX, fromZ));
            int ea = NearestEdge(fromX, fromZ, out float ax, out float az, out float da);
            int eb = NearestEdge(toX, toZ, out float bx, out float bz, out float db);
            float straight = MathF.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ));
            if (ea < 0 || eb < 0 || ea == eb || straight <= da + db)
            {
                if (ea >= 0 && ea == eb && straight > da + db)
                {
                    points.Add((ax, az));
                    points.Add((bx, bz));
                    points.Add((toX, toZ));
                    return da + Dist(ax, az, bx, bz) + db;
                }
                points.Add((toX, toZ));
                return straight;
            }
            // Dijkstra from the entry point's two ends to the exit point's two ends.
            int n = _nodes.Count;
            var cost = new float[n];
            var via = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; via[i] = -1; }
            var entry = _edges[ea];
            cost[entry.A] = Dist(ax, az, _nodes[entry.A].X, _nodes[entry.A].Z);
            cost[entry.B] = Dist(ax, az, _nodes[entry.B].X, _nodes[entry.B].Z);
            while (true)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && cost[i] < float.MaxValue && (best < 0 || cost[i] < cost[best])) best = i;
                if (best < 0) break;
                done[best] = true;
                foreach (int e in _adjacent[best])
                {
                    int m = _edges[e].A == best ? _edges[e].B : _edges[e].A;
                    float c = cost[best] + _edges[e].Length;
                    if (c < cost[m]) { cost[m] = c; via[m] = best; }
                }
            }
            var exit = _edges[eb];
            float viaA = cost[exit.A] + Dist(_nodes[exit.A].X, _nodes[exit.A].Z, bx, bz);
            float viaB = cost[exit.B] + Dist(_nodes[exit.B].X, _nodes[exit.B].Z, bx, bz);
            int last = viaA <= viaB ? exit.A : exit.B;
            float total = MathF.Min(viaA, viaB);
            if (total >= float.MaxValue * 0.5f)
            {
                points.Add((toX, toZ));
                return straight;
            }
            points.Add((ax, az));
            int start = points.Count;
            for (int node = last; node >= 0; node = via[node]) points.Add((_nodes[node].X, _nodes[node].Z));
            points.Reverse(start, points.Count - start);
            points.Add((bx, bz));
            points.Add((toX, toZ));
            return da + total + db;
        }

        private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
    }
}
