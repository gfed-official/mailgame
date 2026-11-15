using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Collects triangles with smooth vertex normals. The look of the concept video is rounded and
    /// soft ("remove the low poly"), so nothing built here has a faceted edge.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _indices = new List<int>();

        public int Add(Vector3 position, Vector3 normal, Vector2 uv = default)
        {
            _positions.Add(position);
            _normals.Add(normal);
            _uvs.Add(uv);
            return _positions.Count - 1;
        }

        /// <summary>Adds a triangle facing the way its vertex normals point (Godot's front faces wind clockwise).</summary>
        public void Tri(int a, int b, int c)
        {
            var face = (_positions[b] - _positions[a]).Cross(_positions[c] - _positions[a]);
            if (face.Dot(_normals[a] + _normals[b] + _normals[c]) > 0f) (b, c) = (c, b);
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>Replaces the normals by the area-weighted average of the faces around each position, across seams.</summary>
        public void SmoothNormals()
        {
            var sums = new Dictionary<Vector3I, Vector3>();
            Vector3I Key(Vector3 p) => new Vector3I(Mathf.RoundToInt(p.X * 2000f), Mathf.RoundToInt(p.Y * 2000f), Mathf.RoundToInt(p.Z * 2000f));
            for (int i = 0; i < _indices.Count; i += 3)
            {
                Vector3 a = _positions[_indices[i]], b = _positions[_indices[i + 1]], c = _positions[_indices[i + 2]];
                var face = (c - a).Cross(b - a);
                for (int k = 0; k < 3; k++)
                {
                    var key = Key(_positions[_indices[i + k]]);
                    sums[key] = sums.TryGetValue(key, out var sum) ? sum + face : face;
                }
            }
            for (int i = 0; i < _positions.Count; i++)
                if (sums.TryGetValue(Key(_positions[i]), out var sum) && sum.LengthSquared() > 1e-12f) _normals[i] = sum.Normalized();
        }

        public ArrayMesh Build(Material? material = null)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _positions.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (material != null) mesh.SurfaceSetMaterial(0, material);
            return mesh;
        }
    }

    /// <summary>
    /// Collects placed meshes and turns every (mesh, material) pair into one MultiMesh, so a street
    /// of houses, fences and trees costs a few dozen draw calls instead of thousands.
    /// </summary>
    public sealed class MeshBatch
    {
        private readonly Dictionary<(Mesh, Material), List<Transform3D>> _groups = new Dictionary<(Mesh, Material), List<Transform3D>>();

        /// <summary>Everything added is placed inside this frame (a house's, a truck's).</summary>
        public Transform3D Frame = Transform3D.Identity;

        public void Add(Mesh mesh, Material material, Transform3D local)
        {
            if (!_groups.TryGetValue((mesh, material), out var list)) _groups[(mesh, material)] = list = new List<Transform3D>();
            list.Add(Frame * local);
        }

        public void Add(Mesh mesh, Material material, Vector3 position, Vector3 rotationDegrees = default, Vector3? scale = null)
        {
            var rotation = new Vector3(Mathf.DegToRad(rotationDegrees.X), Mathf.DegToRad(rotationDegrees.Y), Mathf.DegToRad(rotationDegrees.Z));
            var basis = Basis.FromEuler(rotation);
            if (scale != null) basis = basis.Scaled(scale.Value);
            Add(mesh, material, new Transform3D(basis, position));
        }

        /// <summary>A rounded box given by its centre and size: what most of the town is built from.</summary>
        public void Box(Vector3 centre, Vector3 size, Material material, float radius = 0.04f, Vector3 rotationDegrees = default) =>
            Add(MeshKit.RoundedBox(size, radius), material, centre, rotationDegrees);

        /// <summary>Adds the instances under <paramref name="parent"/> and empties the batch. Returns how many nodes that took.</summary>
        public int Flush(Node3D parent, string name, bool castShadows = true)
        {
            int index = 0;
            foreach (var ((mesh, material), transforms) in _groups)
            {
                var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = transforms.Count };
                for (int i = 0; i < transforms.Count; i++) multimesh.SetInstanceTransform(i, transforms[i]);
                parent.AddChild(new MultiMeshInstance3D
                {
                    Name = $"{name}{index++}",
                    Multimesh = multimesh,
                    MaterialOverride = material,
                    CastShadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
            _groups.Clear();
            return index;
        }
    }

    /// <summary>The shapes the world is made of, and a cache so equal shapes and materials are shared (and batched).</summary>
    public static class MeshKit
    {
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>
        /// Builds every shape from about a fifth of the triangles (see <see cref="WorldView.Cheap"/>):
        /// the full town is some 1.3 million, which a software renderer takes a sixth of a second over.
        /// </summary>
        public static bool LowDetail;

        /// <summary>A box with rounded edges and corners.</summary>
        public static Mesh RoundedBox(Vector3 size, float radius = 0.06f, int segments = 3)
        {
            if (LowDetail) segments = 1;
            string key = $"rb{size.X:0.###}/{size.Y:0.###}/{size.Z:0.###}/{radius:0.###}/{segments}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            var half = size * 0.5f;
            radius = Mathf.Min(radius, Mathf.Min(half.X, Mathf.Min(half.Y, half.Z)));
            var b = new MeshBuilder();
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                float[] us = EdgeCoords(half[u], radius, segments), vs = EdgeCoords(half[v], radius, segments);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var grid = new int[us.Length, vs.Length];
                    for (int i = 0; i < us.Length; i++)
                    {
                        for (int j = 0; j < vs.Length; j++)
                        {
                            var p = new Vector3();
                            p[axis] = sign * half[axis];
                            p[u] = us[i];
                            p[v] = vs[j];
                            var inner = new Vector3(
                                Mathf.Clamp(p.X, -(half.X - radius), half.X - radius),
                                Mathf.Clamp(p.Y, -(half.Y - radius), half.Y - radius),
                                Mathf.Clamp(p.Z, -(half.Z - radius), half.Z - radius));
                            var n = (p - inner).Normalized();
                            grid[i, j] = b.Add(inner + n * radius, n, new Vector2(us[i], vs[j]));
                        }
                    }
                    for (int i = 0; i + 1 < us.Length; i++)
                        for (int j = 0; j + 1 < vs.Length; j++)
                            b.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                }
            }
            return Meshes[key] = b.Build();
        }

        /// <summary>Sample positions across one face: even steps of angle around each rounded edge, one span between.</summary>
        private static float[] EdgeCoords(float half, float radius, int segments)
        {
            float flat = half - radius;
            var coords = new List<float>();
            for (int k = segments; k >= 0; k--) coords.Add(-(flat + radius * Mathf.Tan(k / (float)segments * Mathf.Pi / 4f)));
            for (int k = flat > 1e-4f ? 0 : 1; k <= segments; k++) coords.Add(flat + radius * Mathf.Tan(k / (float)segments * Mathf.Pi / 4f));
            return coords.ToArray();
        }

        /// <summary>A lumpy ball: a tree canopy, a bush, a boulder. <paramref name="squash"/> scales it per axis.</summary>
        public static Mesh Blob(float radius, float bump, float frequency, int seed, Vector3 squash, int resolution = 9)
        {
            if (LowDetail) resolution = Mathf.Max(3, resolution / 2);
            string key = $"blob{radius:0.###}/{bump:0.###}/{frequency:0.###}/{seed}/{squash}/{resolution}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            var b = new MeshBuilder();
            AddBlob(b, Vector3.Zero, radius, bump, frequency, seed, squash, resolution);
            b.SmoothNormals();
            return Meshes[key] = b.Build();
        }

        /// <summary>Several lumpy balls as one mesh: the clumped crown of a tree. Each lump is (centre, radius).</summary>
        public static Mesh Clump(string name, (Vector3 Centre, float Radius)[] lumps, float bump, float frequency, int seed, int resolution = 8)
        {
            if (LowDetail) resolution = Mathf.Max(3, resolution / 2 - 1);
            string key = $"clump{name}/{seed}/{resolution}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            var b = new MeshBuilder();
            for (int i = 0; i < lumps.Length; i++)
                AddBlob(b, lumps[i].Centre, lumps[i].Radius, bump, frequency, seed * 31 + i, new Vector3(1f, 0.88f, 1f), resolution);
            b.SmoothNormals();
            return Meshes[key] = b.Build();
        }

        private static void AddBlob(MeshBuilder b, Vector3 centre, float radius, float bump, float frequency, int seed, Vector3 squash, int resolution)
        {
            var offset = new Vector3(seed * 17.3f, seed * 5.1f, seed * 29.7f);
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var grid = new int[resolution + 1, resolution + 1];
                    for (int i = 0; i <= resolution; i++)
                    {
                        for (int j = 0; j <= resolution; j++)
                        {
                            var p = new Vector3();
                            p[axis] = sign;
                            // The tangent warp spreads the cube's grid evenly over the sphere.
                            p[u] = Mathf.Tan((i / (float)resolution * 2f - 1f) * Mathf.Pi / 4f);
                            p[v] = Mathf.Tan((j / (float)resolution * 2f - 1f) * Mathf.Pi / 4f);
                            var dir = p.Normalized();
                            float r = radius * (1f + bump * (Noise3(dir * frequency + offset) * 2f - 1f));
                            grid[i, j] = b.Add(centre + dir * r * squash, dir, new Vector2(i, j) / resolution);
                        }
                    }
                    for (int i = 0; i < resolution; i++)
                        for (int j = 0; j < resolution; j++)
                            b.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                }
            }
        }

        /// <summary>A triangular prism lying along Z: the infill of a gable wall. The base is at y = 0, the tip at <paramref name="rise"/>.</summary>
        public static Mesh Gable(float width, float rise, float thickness)
        {
            string key = $"gable{width:0.###}/{rise:0.###}/{thickness:0.###}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            var b = new MeshBuilder();
            float hw = width * 0.5f, ht = thickness * 0.5f;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var n = new Vector3(0f, 0f, sign);
                b.Tri(b.Add(new Vector3(-hw, 0f, sign * ht), n), b.Add(new Vector3(hw, 0f, sign * ht), n), b.Add(new Vector3(0f, rise, sign * ht), n));
            }
            return Meshes[key] = b.Build();
        }

        public static Mesh Cylinder(float bottomRadius, float topRadius, float height, int segments = 20)
        {
            if (LowDetail) segments = Mathf.Max(6, segments / 2);
            string key = $"cyl{bottomRadius:0.###}/{topRadius:0.###}/{height:0.###}/{segments}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            return Meshes[key] = new CylinderMesh { BottomRadius = bottomRadius, TopRadius = topRadius, Height = height, RadialSegments = segments, Rings = 1 };
        }

        public static Mesh Sphere(float radius, int segments = 24)
        {
            if (LowDetail) segments = Mathf.Max(6, segments / 2);
            string key = $"sph{radius:0.###}/{segments}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            return Meshes[key] = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = segments, Rings = segments / 2 };
        }

        public static Mesh Capsule(float radius, float height, int segments = 24)
        {
            if (LowDetail) segments = Mathf.Max(6, segments / 2);
            string key = $"cap{radius:0.###}/{height:0.###}/{segments}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            return Meshes[key] = new CapsuleMesh { Radius = radius, Height = height, RadialSegments = segments, Rings = LowDetail ? 4 : 8 };
        }

        public static Mesh Cone(float radius, float height, int segments = 16) => Cylinder(radius, 0.001f, height, segments);

        public static Mesh Box(Vector3 size)
        {
            string key = $"box{size.X:0.###}/{size.Y:0.###}/{size.Z:0.###}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            return Meshes[key] = new BoxMesh { Size = size };
        }

        public static Mesh Torus(float innerRadius, float outerRadius)
        {
            string key = $"tor{innerRadius:0.###}/{outerRadius:0.###}";
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            return Meshes[key] = new TorusMesh { InnerRadius = innerRadius, OuterRadius = outerRadius, Rings = 24, RingSegments = 12 };
        }

        /// <summary>A plain surface: soft, slightly glossy, the way the video's painted wood and metal read.</summary>
        public static Material Mat(Color colour, float roughness = 0.75f, float metallic = 0f)
        {
            string key = $"m{colour.ToHtml()}/{roughness:0.##}/{metallic:0.##}";
            if (Materials.TryGetValue(key, out var cached)) return cached;
            return Materials[key] = new StandardMaterial3D { AlbedoColor = colour, Roughness = roughness, Metallic = metallic };
        }

        /// <summary>A surface that gives off light (lanterns, lit windows, the cannon's ring); bloom picks it up.</summary>
        public static Material Glow(Color colour, float energy = 2.5f)
        {
            string key = $"g{colour.ToHtml()}/{energy:0.##}";
            if (Materials.TryGetValue(key, out var cached)) return cached;
            return Materials[key] = new StandardMaterial3D
            {
                AlbedoColor = colour,
                Roughness = 0.4f,
                EmissionEnabled = true,
                Emission = colour,
                EmissionEnergyMultiplier = energy,
            };
        }

        public static MeshInstance3D Instance(Mesh mesh, Material material, Vector3 position, Vector3 rotationDegrees = default, string? name = null)
        {
            var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, RotationDegrees = rotationDegrees };
            if (name != null) node.Name = name;
            return node;
        }

        /// <summary>Smooth value noise in [0, 1], the same on every machine.</summary>
        public static float Noise3(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(p.Y), z = Mathf.FloorToInt(p.Z);
            float fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float Corner(int ix, int iy, int iz)
            {
                uint h = (uint)(ix * 374761393 + iy * 668265263 + iz * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
            float x00 = Mathf.Lerp(Corner(x, y, z), Corner(x + 1, y, z), fx), x10 = Mathf.Lerp(Corner(x, y + 1, z), Corner(x + 1, y + 1, z), fx);
            float x01 = Mathf.Lerp(Corner(x, y, z + 1), Corner(x + 1, y, z + 1), fx), x11 = Mathf.Lerp(Corner(x, y + 1, z + 1), Corner(x + 1, y + 1, z + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }
    }
}
