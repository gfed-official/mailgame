using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Draws the map's props (<see cref="Scenery"/>): round clumped trees, bright bushes, boulders,
    /// red street lamps with glowing lanterns, benches, white picket fences and flowers.
    /// </summary>
    public static class SceneryView
    {
        private static readonly Color[] Leaves = { new Color("1f4f1c"), new Color("2a5c1e"), new Color("1c4a26") };
        private static readonly Color[] LeafTips = { new Color("5f9a30"), new Color("78a636"), new Color("4f8f3a") };
        private static readonly Color[] Petals = { new Color("ffd23f"), new Color("fff4e0"), new Color("ff8fa3"), new Color("ff6b4a") };
        private static readonly Color White = new Color("f6f0e4");
        public static readonly Color LampLight = new Color(1.0f, 0.80f, 0.48f);

        private static readonly (Vector3, float)[][] Crowns =
        {
            new[] { (new Vector3(0f, 3.7f, 0f), 1.55f), (new Vector3(1.0f, 3.2f, 0.3f), 1.05f), (new Vector3(-0.9f, 3.3f, -0.4f), 1.15f), (new Vector3(0.1f, 4.7f, -0.2f), 1.0f), (new Vector3(-0.2f, 3.2f, 1.0f), 0.95f) },
            new[] { (new Vector3(0f, 3.4f, 0f), 1.35f), (new Vector3(0.7f, 4.2f, -0.5f), 1.0f), (new Vector3(-0.8f, 3.9f, 0.4f), 1.05f), (new Vector3(0.3f, 3.1f, 0.9f), 0.85f) },
            new[] { (new Vector3(0f, 2.3f, 0f), 0.95f), (new Vector3(0f, 3.4f, 0f), 0.9f), (new Vector3(0f, 4.4f, 0f), 0.7f), (new Vector3(0f, 5.2f, 0f), 0.45f) },
        };

        /// <returns>Where the street lamps' lights are.</returns>
        public static List<Vector3> Build(MeshBatch batch, WorldMap map)
        {
            var lamps = new List<Vector3>();
            foreach (var prop in map.Props)
            {
                float ground = map.HeightAt(prop.X, prop.Z);
                var at = new Vector3(prop.X, ground, prop.Z);
                switch (prop.Kind)
                {
                    case PropKind.Tree: Tree(batch, at, prop.Yaw, prop.Scale, prop.Variant); break;
                    case PropKind.Bush: Bush(batch, at, prop.Yaw, prop.Scale, prop.Variant); break;
                    case PropKind.Boulder: Boulder(batch, at, prop.Yaw, prop.Scale, prop.Variant); break;
                    case PropKind.Lamp: lamps.Add(Lamp(batch, at)); break;
                    case PropKind.Bench: Bench(batch, new Vector3((prop.X + prop.X2) * 0.5f, ground, (prop.Z + prop.Z2) * 0.5f), prop.Yaw); break;
                    case PropKind.Fence: Fence(batch, at, new Vector3(prop.X2, ground, prop.Z2)); break;
                    case PropKind.Flowers: Flowers(batch, at, prop.Scale, prop.Variant); break;
                }
            }
            return lamps;
        }

        public static void Tree(MeshBatch batch, Vector3 at, float yaw, float scale, int variant)
        {
            variant %= Crowns.Length;
            var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
            float trunk = variant == 2 ? 1.8f : 2.9f;
            batch.Add(MeshKit.Cylinder(0.26f, 0.15f, trunk, 12), MeshKit.Mat(new Color("5b3a26"), 0.9f), new Transform3D(basis, at + new Vector3(0f, trunk * 0.5f * scale, 0f)));
            batch.Add(MeshKit.Clump($"crown{variant}", Crowns[variant], 0.16f, 2.1f, variant + 1), Shaders.Foliage(Leaves[variant], LeafTips[variant], 2.2f, 3.2f), new Transform3D(basis, at));
        }

        public static void Bush(MeshBatch batch, Vector3 at, float yaw, float scale, int variant)
        {
            variant %= 3;
            var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
            batch.Add(MeshKit.Blob(0.62f, 0.16f, 2.4f, 11 + variant, new Vector3(1f, 0.82f, 1f)), Shaders.Foliage(new Color("2c6420"), new Color("7fae38"), -0.5f, 1f),
                new Transform3D(basis, at + new Vector3(0f, 0.4f * scale, 0f)));
        }

        public static void Boulder(MeshBatch batch, Vector3 at, float yaw, float scale, int variant)
        {
            variant %= 3;
            var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
            batch.Add(MeshKit.Blob(0.62f, 0.2f, 1.25f, 23 + variant, new Vector3(1.2f, 0.72f, 0.95f)), MeshKit.Mat(new Color("6d6878"), 0.9f),
                new Transform3D(basis, at + new Vector3(0f, 0.22f * scale, 0f)));
        }

        /// <returns>The lantern's position.</returns>
        public static Vector3 Lamp(MeshBatch batch, Vector3 at)
        {
            var red = MeshKit.Mat(new Color("a8322c"), 0.45f, 0.2f);
            var iron = MeshKit.Mat(new Color("26222b"), 0.5f, 0.4f);
            batch.Add(MeshKit.Cylinder(0.16f, 0.11f, 0.6f, 14), red, at + new Vector3(0f, 0.3f, 0f));
            batch.Add(MeshKit.Cylinder(0.075f, 0.06f, 2.7f, 12), red, at + new Vector3(0f, 1.95f, 0f));
            batch.Add(MeshKit.Sphere(0.1f, 12), red, at + new Vector3(0f, 0.62f, 0f));
            batch.Add(MeshKit.Cylinder(0.07f, 0.17f, 0.14f, 12), iron, at + new Vector3(0f, 3.34f, 0f));
            var lantern = at + new Vector3(0f, 3.62f, 0f);
            batch.Box(lantern, new Vector3(0.3f, 0.42f, 0.3f), MeshKit.Glow(LampLight, 4.0f), 0.06f);
            batch.Add(MeshKit.Cone(0.27f, 0.2f, 4), iron, lantern + new Vector3(0f, 0.31f, 0f), new Vector3(0f, 45f, 0f));
            batch.Add(MeshKit.Sphere(0.045f, 10), iron, lantern + new Vector3(0f, 0.44f, 0f));
            return lantern;
        }

        public static void Bench(MeshBatch batch, Vector3 centre, float yaw)
        {
            var saved = batch.Frame;
            batch.Frame = saved * new Transform3D(new Basis(Vector3.Up, yaw), centre);
            var wood = MeshKit.Mat(new Color("7a4a2c"), 0.65f);
            var iron = MeshKit.Mat(new Color("26222b"), 0.5f, 0.4f);
            for (int i = 0; i < 3; i++)
                batch.Box(new Vector3(0f, 0.46f, -0.14f + i * 0.15f), new Vector3(1.6f, 0.045f, 0.13f), wood, 0.02f);
            for (int i = 0; i < 2; i++)
                batch.Box(new Vector3(0f, 0.68f + i * 0.17f, -0.25f - i * 0.03f), new Vector3(1.6f, 0.13f, 0.045f), wood, 0.02f, new Vector3(-10f, 0f, 0f));
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.72f;
                batch.Box(new Vector3(x, 0.22f, 0.14f), new Vector3(0.06f, 0.44f, 0.06f), iron, 0.02f);
                batch.Box(new Vector3(x, 0.45f, -0.26f), new Vector3(0.06f, 0.9f, 0.06f), iron, 0.02f, new Vector3(-8f, 0f, 0f));
                batch.Box(new Vector3(x, 0.64f, -0.03f), new Vector3(0.07f, 0.05f, 0.48f), iron, 0.02f);
            }
            batch.Frame = saved;
        }

        public static void Fence(MeshBatch batch, Vector3 from, Vector3 to)
        {
            var white = MeshKit.Mat(White, 0.6f);
            var along = to - from;
            float length = along.Length();
            if (length < 0.1f) return;
            var saved = batch.Frame;
            // Local X runs along the fence.
            batch.Frame = saved * new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(-along.Z, along.X)), from);
            int pickets = Mathf.Max(2, Mathf.RoundToInt(length / 0.27f));
            for (int i = 0; i <= pickets; i++)
            {
                float x = length * i / pickets;
                bool post = i == 0 || i == pickets;
                float height = post ? 0.95f : 0.78f, width = post ? 0.13f : 0.15f, depth = post ? 0.13f : 0.04f;
                batch.Box(new Vector3(x, height * 0.5f + 0.04f, 0f), new Vector3(width, height, depth), white, 0.02f);
                // The pointed top: the same board turned on its corner.
                batch.Box(new Vector3(x, height + 0.04f, 0f), new Vector3(width * 0.72f, width * 0.72f, depth), white, 0.015f, new Vector3(0f, 0f, 45f));
            }
            for (int rail = 0; rail < 2; rail++)
                batch.Box(new Vector3(length * 0.5f, 0.3f + rail * 0.36f, -0.04f), new Vector3(length, 0.07f, 0.04f), white, 0.015f);
            batch.Frame = saved;
        }

        public static void Flowers(MeshBatch batch, Vector3 at, float scale, int variant)
        {
            var stem = MeshKit.Mat(new Color("4c8a2a"), 0.9f);
            var petal = MeshKit.Mat(Petals[variant % Petals.Length], 0.6f);
            var heart = MeshKit.Mat(new Color("e89b1c"), 0.6f);
            int count = 5 + (int)(MeshKit.Noise3(at * 3.1f) * 4f);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.4f + at.X, r = (0.12f + 0.38f * MeshKit.Noise3(at + new Vector3(i * 1.7f, 0f, 0f))) * scale;
                float h = (0.22f + 0.2f * MeshKit.Noise3(at + new Vector3(0f, i * 2.3f, 0f))) * scale;
                var foot = at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                batch.Add(MeshKit.Cylinder(0.012f, 0.012f, 1f, 5), stem, new Transform3D(Basis.Identity.Scaled(new Vector3(1f, h, 1f)), foot + new Vector3(0f, h * 0.5f, 0f)));
                batch.Add(MeshKit.Sphere(0.06f, 8), petal, new Transform3D(Basis.Identity.Scaled(new Vector3(1f, 0.55f, 1f)), foot + new Vector3(0f, h, 0f)));
                batch.Add(MeshKit.Sphere(0.028f, 6), heart, foot + new Vector3(0f, h + 0.025f, 0f));
            }
            batch.Add(MeshKit.Blob(0.2f, 0.25f, 3f, 5, new Vector3(1f, 0.45f, 1f), 5), Shaders.Foliage(new Color("3f7d25"), new Color("9cc43c"), -0.1f, 0.2f),
                new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), at + new Vector3(0f, 0.05f, 0f)));
        }
    }
}
