using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// A Business Dock (doc 02 §3.5; design choice 114) the way the concept video's harbour reads
    /// next to its cottages: a ribbed steel warehouse on a concrete yard, a wide amber roll-up door
    /// facing the quay, a lamp over it, pallets of crates beside it, the unloading zone painted in
    /// yellow at the quay's edge with iron bollards either side and a jib crane over the water.
    /// </summary>
    public static class DockBuilder
    {
        private const float Tile = SimConstants.TileSize;
        private const float WallHeight = 4.2f, Foundation = 0.3f;
        private static readonly Color Steel = new Color("5d7383");
        private static readonly Color Rib = new Color("4b5f6e");
        private static readonly Color Concrete = new Color("a7a39b");
        private static readonly Color Amber = new Color("d89a2b");
        private static readonly Color Yellow = new Color("e8c13a");
        private static readonly Color Iron = new Color("2c2f33");
        private static readonly Color Wood = new Color("9a7448");
        public static readonly Color LampLight = new Color(1.0f, 0.82f, 0.5f);

        /// <returns>Where the lamp over the door sits, in world space.</returns>
        public static Vector3 Build(MeshBatch batch, DestinationDef dest)
        {
            var concrete = MeshKit.Mat(Concrete, 0.95f);
            var steel = MeshKit.Mat(Steel, 0.5f, 0.35f);
            var rib = MeshKit.Mat(Rib, 0.5f, 0.4f);
            var amber = MeshKit.Mat(Amber, 0.5f, 0.2f);
            var yellow = MeshKit.Mat(Yellow, 0.6f);
            var iron = MeshKit.Mat(Iron, 0.6f, 0.5f);
            var wood = MeshKit.Mat(Wood, 0.85f);

            // The yard: concrete over the whole lot.
            var lot = dest.Lot;
            batch.Frame = Transform3D.Identity;
            batch.Box(new Vector3(lot.CenterX, 0.03f, lot.CenterZ), new Vector3(lot.W * Tile - 0.1f, 0.06f, lot.H * Tile - 0.1f), concrete, 0.02f);

            var frame = HouseBuilder.Frame(dest, out float w, out float d);
            batch.Frame = frame;
            float hw = w * 0.5f, hd = d * 0.5f, top = Foundation + WallHeight;
            batch.Box(new Vector3(0f, Foundation * 0.5f, 0f), new Vector3(w + 0.2f, Foundation, d + 0.2f), MeshKit.Mat(Concrete.Darkened(0.15f), 0.9f), 0.04f);
            batch.Box(new Vector3(0f, Foundation + WallHeight * 0.5f, 0f), new Vector3(w, WallHeight, d), steel, 0.03f);
            // Ribs down every wall.
            for (float x = -hw + 0.3f; x <= hw - 0.3f; x += 0.6f)
                for (int sz = -1; sz <= 1; sz += 2)
                    batch.Box(new Vector3(x, Foundation + WallHeight * 0.5f, sz * (hd + 0.03f)), new Vector3(0.08f, WallHeight, 0.06f), rib, 0.01f);
            for (float z = -hd + 0.3f; z <= hd - 0.3f; z += 0.6f)
                for (int sx = -1; sx <= 1; sx += 2)
                    batch.Box(new Vector3(sx * (hw + 0.03f), Foundation + WallHeight * 0.5f, z), new Vector3(0.06f, WallHeight, 0.08f), rib, 0.01f);
            // A shallow roof: steel gable ends, two slabs meeting at a dark ridge cap.
            const float rise = 0.9f, overhang = 0.35f, thick = 0.12f;
            batch.Add(MeshKit.Gable(w, rise, d), steel, new Vector3(0f, top, 0f));
            var roof = MeshKit.Mat(new Color("3b4148"), 0.6f, 0.3f);
            float alpha = Mathf.Atan2(rise, hw), run = hw + overhang, length = run / Mathf.Cos(alpha) + 0.06f;
            for (int side = -1; side <= 1; side += 2)
            {
                var basis = new Basis(Vector3.Back, -side * alpha);
                var normal = new Vector3(side * Mathf.Sin(alpha), Mathf.Cos(alpha), 0f);
                var mid = new Vector3(side * run * 0.5f, top + rise - run * 0.5f * Mathf.Tan(alpha), 0f);
                batch.Add(MeshKit.RoundedBox(new Vector3(length, thick, d + 0.5f), 0.03f), roof, new Transform3D(basis, mid + normal * thick * 0.5f));
            }
            batch.Box(new Vector3(0f, top + rise + 0.1f, 0f), new Vector3(0.3f, 0.12f, d + 0.56f), iron, 0.03f);
            // The roll-up door facing the quay, with its frame and a band of the post's red over it.
            float doorW = Mathf.Min(w - 1.2f, 3.6f);
            batch.Box(new Vector3(0f, Foundation + 1.6f, hd + 0.05f), new Vector3(doorW, 3.2f, 0.06f), amber, 0.01f);
            for (float y = Foundation + 0.2f; y < Foundation + 3.2f; y += 0.3f)
                batch.Box(new Vector3(0f, y, hd + 0.09f), new Vector3(doorW - 0.06f, 0.03f, 0.02f), MeshKit.Mat(Amber.Darkened(0.25f), 0.5f), 0.005f);
            for (int sx = -1; sx <= 1; sx += 2)
                batch.Box(new Vector3(sx * (doorW * 0.5f + 0.1f), Foundation + 1.7f, hd + 0.08f), new Vector3(0.2f, 3.4f, 0.12f), iron, 0.02f);
            batch.Box(new Vector3(0f, Foundation + 3.55f, hd + 0.08f), new Vector3(w * 0.8f, 0.4f, 0.08f), MeshKit.Mat(new Color("c8322c"), 0.55f), 0.02f);
            var lamp = new Vector3(0f, Foundation + 3.95f, hd + 0.35f);
            batch.Box(lamp, new Vector3(0.5f, 0.12f, 0.3f), iron, 0.03f);
            batch.Box(lamp + new Vector3(0f, -0.1f, 0f), new Vector3(0.36f, 0.06f, 0.2f), MeshKit.Glow(LampLight, 3f), 0.02f);

            // Pallets of crates beside the warehouse, on the yard.
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(-hw + 0.7f + i * 0.05f, 0f, hd + 1.2f + i * 1.15f);
                if (Mathf.Abs(at.X) > hw + 0.5f) continue;
                batch.Box(at + new Vector3(0f, 0.08f, 0f), new Vector3(1.1f, 0.14f, 1.0f), wood, 0.01f);
                batch.Box(at + new Vector3(0f, 0.5f, 0f), new Vector3(0.95f, 0.7f, 0.85f), MeshKit.Mat(new Color("b98e5a"), 0.8f), 0.03f);
                if (i != 1) batch.Box(at + new Vector3(0.05f, 1.15f, 0f), new Vector3(0.8f, 0.6f, 0.7f), MeshKit.Mat(new Color("a77d4c"), 0.8f), 0.03f);
            }

            // The unloading zone, in world space: a yellow frame round its middle at the quay's edge, hatched.
            batch.Frame = Transform3D.Identity;
            var zone = new Vector3(dest.MailboxX, 0f, dest.MailboxZ);
            const float zw = 3.6f, zd = 1.6f;
            for (int s = -1; s <= 1; s += 2)
            {
                batch.Box(zone + new Vector3(0f, 0.075f, s * zd * 0.5f), new Vector3(zw, 0.03f, 0.12f), yellow, 0.005f);
                batch.Box(zone + new Vector3(s * zw * 0.5f, 0.075f, 0f), new Vector3(0.12f, 0.03f, zd), yellow, 0.005f);
            }
            for (int i = -3; i <= 3; i++)
                batch.Box(zone + new Vector3(i * 0.5f, 0.07f, 0f), new Vector3(0.1f, 0.02f, zd - 0.1f), yellow, 0.005f, new Vector3(0f, 35f, 0f));
            // Bollards either side, a jib crane leaning out over the water.
            for (int s = -1; s <= 1; s += 2)
            {
                var b = zone + new Vector3(s * (zw * 0.5f + 0.5f), 0f, 0.4f);
                batch.Add(MeshKit.Cylinder(0.16f, 0.2f, 0.6f, 12), iron, b + new Vector3(0f, 0.3f, 0f));
                batch.Add(MeshKit.Cylinder(0.24f, 0.24f, 0.08f, 12), iron, b + new Vector3(0f, 0.62f, 0f));
            }
            var mast = zone + new Vector3(zw * 0.5f + 1.3f, 0f, -0.2f);
            batch.Box(mast + new Vector3(0f, 2.2f, 0f), new Vector3(0.28f, 4.4f, 0.28f), yellow, 0.03f);
            // The jib out over the water (+Z from the quay), its hook hanging above where a boat lies.
            batch.Box(mast + new Vector3(0f, 4.3f, 1.4f), new Vector3(0.18f, 0.18f, 3.2f), yellow, 0.02f);
            batch.Box(mast + new Vector3(0f, 3.5f, 2.8f), new Vector3(0.03f, 1.6f, 0.03f), iron, 0.005f);
            batch.Box(mast + new Vector3(0f, 2.65f, 2.8f), new Vector3(0.22f, 0.18f, 0.12f), iron, 0.02f);
            batch.Frame = Transform3D.Identity;
            return frame * lamp;
        }
    }
}
