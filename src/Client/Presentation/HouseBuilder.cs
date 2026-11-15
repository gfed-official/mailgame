using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The houses of the concept video: tall clapboard cottages in strong colours with white trim, a
    /// steep shingle gable facing the street, a porch with posts and a pair of lit lanterns at the
    /// door. Everything goes into a <see cref="MeshBatch"/>, so ten houses cost what one does.
    /// </summary>
    public static class HouseBuilder
    {
        private const float Tile = SimConstants.TileSize;
        public const float Foundation = 0.25f;
        public const float WallHeight = 3.0f;

        private static readonly Color Trim = new Color("f6f0e4");
        private static readonly Color[] Roofs = { new Color("2d2a3c"), new Color("3a2a30"), new Color("27333d"), new Color("402c2a") };
        private static readonly Color[] Doors = { new Color("5a2e22"), new Color("2f4858"), new Color("6b3a2a"), new Color("3d5a3a") };
        public static readonly Color LanternLight = new Color(1.0f, 0.78f, 0.42f);

        /// <summary>The frame a house is built in: origin on the ground at the middle of its footprint, +Z out of the front door.</summary>
        public static Transform3D Frame(DestinationDef dest, out float width, out float depth)
        {
            Scenery.Front(dest, out int fx, out int fz);
            var b = dest.Building;
            width = (fz != 0 ? b.W : b.H) * Tile;
            depth = (fz != 0 ? b.H : b.W) * Tile;
            return new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(fx, fz)), new Vector3(b.CenterX, 0f, b.CenterZ));
        }

        /// <summary>Height of the ridge above the ground for a house this wide.</summary>
        public static float RidgeHeight(float width) => Foundation + WallHeight + Rise(width);

        private static float Rise(float width) => width * 0.5f * 1.05f;

        /// <returns>Where the porch light sits, in world space.</returns>
        public static Vector3 Build(MeshBatch batch, DestinationDef dest)
        {
            var frame = Frame(dest, out float w, out float d);
            batch.Frame = frame;
            int variant = dest.Variant;
            var colour = Palette.HouseColour(variant);
            var siding = Shaders.Clapboard(colour);
            var trim = MeshKit.Mat(Trim, 0.6f);
            var roof = Shaders.Shingles(Roofs[Mod(variant, Roofs.Length)]);
            var roofPlain = MeshKit.Mat(Roofs[Mod(variant, Roofs.Length)].Darkened(0.2f), 0.7f);
            float hw = w * 0.5f, hd = d * 0.5f, top = Foundation + WallHeight, rise = Rise(w);

            batch.Box(new Vector3(0f, Foundation * 0.5f, 0f), new Vector3(w + 0.16f, Foundation, d + 0.16f), MeshKit.Mat(new Color("8d8794"), 0.9f), 0.05f);
            batch.Add(MeshKit.Box(new Vector3(w, WallHeight, d)), siding, new Vector3(0f, Foundation + WallHeight * 0.5f, 0f));
            batch.Add(MeshKit.Gable(w, rise, d), siding, new Vector3(0f, top, 0f));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    batch.Box(new Vector3(sx * hw, Foundation + WallHeight * 0.5f, sz * hd), new Vector3(0.2f, WallHeight, 0.2f), trim, 0.03f);
            // The white band under the gable, front and back.
            for (int sz = -1; sz <= 1; sz += 2)
                batch.Box(new Vector3(0f, top, sz * (hd + 0.02f)), new Vector3(w + 0.22f, 0.24f, 0.12f), trim, 0.03f);

            // Roof: two shingled slabs meeting at the ridge, white bargeboards under their street ends.
            const float overhang = 0.45f, overhangZ = 0.4f, thick = 0.16f;
            float alpha = Mathf.Atan2(rise, hw);
            float run = hw + overhang, length = run / Mathf.Cos(alpha) + 0.06f;
            for (int side = -1; side <= 1; side += 2)
            {
                var basis = new Basis(Vector3.Back, -side * alpha);
                var normal = new Vector3(side * Mathf.Sin(alpha), Mathf.Cos(alpha), 0f);
                var mid = new Vector3(side * run * 0.5f, top + rise - run * 0.5f * Mathf.Tan(alpha), 0f);
                batch.Add(MeshKit.RoundedBox(new Vector3(length, thick, d + overhangZ * 2f), 0.05f), roof, new Transform3D(basis, mid + normal * thick * 0.5f));
                for (int sz = -1; sz <= 1; sz += 2)
                    batch.Add(MeshKit.RoundedBox(new Vector3(length, 0.22f, 0.1f), 0.03f), trim, new Transform3D(basis, mid - normal * 0.05f + new Vector3(0f, 0f, sz * (hd + overhangZ - 0.03f))));
            }
            batch.Box(new Vector3(0f, top + rise + thick / Mathf.Cos(alpha) - 0.02f, 0f), new Vector3(0.34f, 0.16f, d + overhangZ * 2f + 0.06f), roofPlain, 0.07f);
            if (Mod(variant, 3) != 1)
            {
                // A brick chimney through the back of the roof.
                float cx = (Mod(variant, 2) == 0 ? -1f : 1f) * hw * 0.5f;
                float cy = top + rise * 0.5f;
                batch.Box(new Vector3(cx, cy + 0.5f, -hd * 0.45f), new Vector3(0.62f, 2.0f, 0.62f), MeshKit.Mat(new Color("8a4a3c"), 0.9f), 0.04f);
                batch.Box(new Vector3(cx, cy + 1.55f, -hd * 0.45f), new Vector3(0.78f, 0.16f, 0.78f), MeshKit.Mat(new Color("5d5560"), 0.9f), 0.04f);
            }

            // Windows: one in the gable, one each side of the door, one on each side wall.
            bool lit = Mod(variant, 2) == 0;
            Window(batch, frame, new Vector3(0f, top + rise * 0.36f, hd), 0f, 0.95f, 1.15f, !lit);
            float doorX = Mod(variant, 3) == 0 ? -w * 0.2f : Mod(variant, 3) == 1 ? 0f : w * 0.2f;
            if (doorX <= 0f) Window(batch, frame, new Vector3(hw * 0.55f, Foundation + 1.75f, hd), 0f, 1.0f, 1.25f, lit);
            if (doorX >= 0f) Window(batch, frame, new Vector3(-hw * 0.55f, Foundation + 1.75f, hd), 0f, 1.0f, 1.25f, lit);
            Window(batch, frame, new Vector3(hw, Foundation + 1.75f, 0f), 90f, 1.0f, 1.25f, !lit);
            Window(batch, frame, new Vector3(-hw, Foundation + 1.75f, 0f), -90f, 1.0f, 1.25f, lit);
            Window(batch, frame, new Vector3(0f, Foundation + 1.75f, -hd), 180f, 1.0f, 1.25f, false);
            batch.Frame = frame;

            // Door, with a lantern either side: the lights the street is known by at dusk.
            float doorBase = Foundation;
            batch.Box(new Vector3(doorX, doorBase + 1.12f, hd + 0.02f), new Vector3(1.24f, 2.3f, 0.12f), trim, 0.03f);
            batch.Box(new Vector3(doorX, doorBase + 1.04f, hd + 0.06f), new Vector3(0.98f, 2.08f, 0.1f), MeshKit.Mat(Doors[Mod(variant, Doors.Length)], 0.55f), 0.03f);
            batch.Box(new Vector3(doorX, doorBase + 1.55f, hd + 0.1f), new Vector3(0.5f, 0.6f, 0.05f), MeshKit.Glow(new Color("ffd98a"), 1.4f), 0.02f);
            batch.Add(MeshKit.Sphere(0.05f, 12), MeshKit.Mat(new Color("d9b45a"), 0.3f, 0.8f), new Vector3(doorX + 0.36f, doorBase + 1.0f, hd + 0.14f));
            var iron = MeshKit.Mat(new Color("26222b"), 0.5f, 0.4f);
            var lantern = MeshKit.Glow(LanternLight, 3.2f);
            for (int side = -1; side <= 1; side += 2)
            {
                var at = new Vector3(doorX + side * 0.92f, doorBase + 2.0f, hd + 0.2f);
                batch.Box(at + new Vector3(0f, 0.02f, -0.1f), new Vector3(0.06f, 0.06f, 0.2f), iron, 0.02f);
                batch.Box(at, new Vector3(0.17f, 0.26f, 0.17f), lantern, 0.04f);
                batch.Add(MeshKit.Cone(0.16f, 0.14f, 4), iron, at + new Vector3(0f, 0.2f, 0f), new Vector3(0f, 45f, 0f));
                batch.Box(at + new Vector3(0f, -0.15f, 0f), new Vector3(0.2f, 0.04f, 0.2f), iron, 0.015f);
            }

            // Porch: a deck with steps, white posts and a lean-to roof.
            const float porchDepth = 1.3f, deckTop = 0.24f, postTop = 2.62f;
            float pw = w * 0.84f;
            var wood = MeshKit.Mat(new Color("8a5a3c"), 0.7f);
            batch.Box(new Vector3(0f, deckTop * 0.5f, hd + porchDepth * 0.5f), new Vector3(pw, deckTop, porchDepth), wood, 0.03f);
            batch.Box(new Vector3(doorX, 0.07f, hd + porchDepth + 0.2f), new Vector3(1.5f, 0.14f, 0.42f), wood, 0.03f);
            for (int side = -1; side <= 1; side += 2)
            {
                float px = side * (pw * 0.5f - 0.1f), pz = hd + porchDepth - 0.1f;
                batch.Box(new Vector3(px, (deckTop + postTop) * 0.5f, pz), new Vector3(0.15f, postTop - deckTop, 0.15f), trim, 0.03f);
                // A rail from the corner post to the wall, and along the front up to the steps.
                batch.Box(new Vector3(px, deckTop + 0.85f, hd + porchDepth * 0.5f - 0.05f), new Vector3(0.07f, 0.07f, porchDepth - 0.2f), trim, 0.02f);
                float railEnd = doorX + side * 0.8f, railLength = Mathf.Abs(px - railEnd);
                if (railLength > 0.4f)
                {
                    float railX = (px + railEnd) * 0.5f;
                    batch.Box(new Vector3(railX, deckTop + 0.85f, pz), new Vector3(railLength, 0.07f, 0.07f), trim, 0.02f);
                    batch.Box(new Vector3(railEnd, deckTop + 0.45f, pz), new Vector3(0.1f, 0.9f, 0.1f), trim, 0.03f);
                    for (float x = 0.22f; x < railLength - 0.1f; x += 0.22f)
                        batch.Box(new Vector3(px - side * x, deckTop + 0.43f, pz), new Vector3(0.04f, 0.8f, 0.04f), trim, 0.015f);
                }
            }
            batch.Box(new Vector3(0f, postTop + 0.09f, hd + porchDepth - 0.1f), new Vector3(pw + 0.1f, 0.18f, 0.17f), trim, 0.03f);
            const float tilt = 0.24f;
            var porchBasis = new Basis(Vector3.Up, -Mathf.Pi * 0.5f) * new Basis(Vector3.Back, -tilt);
            float slab = porchDepth + 0.45f;
            batch.Add(MeshKit.RoundedBox(new Vector3(slab / Mathf.Cos(tilt), 0.12f, pw + 0.4f), 0.04f), roof,
                new Transform3D(porchBasis, new Vector3(0f, postTop + 0.24f + slab * 0.5f * Mathf.Tan(tilt), hd + slab * 0.5f)));

            batch.Frame = Transform3D.Identity;
            return frame * new Vector3(doorX, 2.2f, hd + porchDepth * 0.6f);
        }

        /// <summary>A white-framed window on a wall. <paramref name="at"/> is on the wall's surface; <paramref name="yawDegrees"/> turns it to face out.</summary>
        public static void Window(MeshBatch batch, Transform3D frame, Vector3 at, float yawDegrees, float width, float height, bool lit, Color? trimColour = null)
        {
            batch.Frame = frame * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yawDegrees)), at);
            var trim = MeshKit.Mat(trimColour ?? Trim, 0.6f);
            var glass = lit ? MeshKit.Glow(new Color("ffd58a"), 1.5f) : MeshKit.Mat(new Color("31506e"), 0.12f, 0.35f);
            batch.Box(new Vector3(0f, 0f, 0.02f), new Vector3(width + 0.24f, height + 0.24f, 0.12f), trim, 0.03f);
            batch.Box(new Vector3(0f, 0f, 0.05f), new Vector3(width, height, 0.1f), glass, 0.02f);
            batch.Box(new Vector3(0f, 0f, 0.09f), new Vector3(0.05f, height, 0.05f), trim, 0.015f);
            batch.Box(new Vector3(0f, 0f, 0.09f), new Vector3(width, 0.05f, 0.05f), trim, 0.015f);
            batch.Box(new Vector3(0f, -height * 0.5f - 0.14f, 0.1f), new Vector3(width + 0.36f, 0.08f, 0.22f), trim, 0.03f);
        }

        private static int Mod(int v, int n) => ((v % n) + n) % n;
    }
}
