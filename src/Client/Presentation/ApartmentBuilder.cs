using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// An Apartment Complex (doc 02 §3.5; design choice 170): a three-storey brick block on its wide lot, rows of white
    /// framed windows, a flat roof behind a parapet, and at the middle of the front the Mail Room's glazed door under a
    /// canopy, lit like a porch. Its mail goes into the bank of unit boxes at the street (<see cref="MailRoomBoxes"/>).
    /// </summary>
    public static class ApartmentBuilder
    {
        private const float Tile = SimConstants.TileSize;
        public const float Foundation = 0.3f, Storey = 3.0f, Storeys = 3;
        public static float Height => Foundation + Storey * Storeys;
        private static readonly Color[] Bricks = { new Color("9c4a3a"), new Color("8a5a44"), new Color("7d4f5a"), new Color("a0643f") };
        private static readonly Color Trim = new Color("f6f0e4");

        /// <returns>Where the entrance light sits, in world space.</returns>
        public static Vector3 Build(MeshBatch batch, DestinationDef dest)
        {
            var frame = HouseBuilder.Frame(dest, out float w, out float d);
            batch.Frame = frame;
            var brick = MeshKit.Mat(Bricks[Mod(dest.Variant, Bricks.Length)], 0.85f);
            var trim = MeshKit.Mat(Trim, 0.6f);
            var stone = MeshKit.Mat(new Color("8d8794"), 0.9f);
            var roof = MeshKit.Mat(new Color("3a3744"), 0.8f);
            float hw = w * 0.5f, hd = d * 0.5f, top = Height;

            batch.Box(new Vector3(0f, Foundation * 0.5f, 0f), new Vector3(w + 0.2f, Foundation, d + 0.2f), stone, 0.05f);
            batch.Box(new Vector3(0f, Foundation + Storey * Storeys * 0.5f, 0f), new Vector3(w, Storey * Storeys, d), brick, 0.04f);
            // A white band at each floor and the parapet round the flat roof.
            for (int f = 1; f < Storeys; f++)
                batch.Box(new Vector3(0f, Foundation + f * Storey, 0f), new Vector3(w + 0.14f, 0.16f, d + 0.14f), trim, 0.03f);
            batch.Box(new Vector3(0f, top + 0.05f, 0f), new Vector3(w - 0.2f, 0.1f, d - 0.2f), roof, 0.02f);
            for (int s = -1; s <= 1; s += 2)
            {
                batch.Box(new Vector3(0f, top + 0.35f, s * (hd - 0.1f)), new Vector3(w + 0.1f, 0.7f, 0.25f), brick, 0.03f);
                batch.Box(new Vector3(s * (hw - 0.1f), top + 0.35f, 0f), new Vector3(0.25f, 0.7f, d), brick, 0.03f);
                batch.Box(new Vector3(0f, top + 0.74f, s * (hd - 0.1f)), new Vector3(w + 0.2f, 0.1f, 0.32f), trim, 0.02f);
            }
            // A water tank on the roof, as such blocks have.
            batch.Add(MeshKit.Cylinder(0.6f, 0.6f, 1.2f, 12), MeshKit.Mat(new Color("6e4a35"), 0.8f), new Vector3(-hw * 0.5f, top + 0.7f, -hd * 0.3f));

            // Windows: four across each floor of the front (the door's bay left on the ground floor), two on each side.
            int across = Mathf.Max(3, Mathf.RoundToInt(w / 2.4f));
            for (int f = 0; f < Storeys; f++)
            {
                float y = Foundation + f * Storey + 1.65f;
                for (int i = 0; i < across; i++)
                {
                    float x = -hw + w * (i + 0.5f) / across;
                    bool doorBay = f == 0 && Mathf.Abs(x) < 1.3f;
                    if (!doorBay) HouseBuilder.Window(batch, frame, new Vector3(x, y, hd), 0f, 1.0f, 1.3f, (i + f + dest.Variant) % 3 == 0);
                    HouseBuilder.Window(batch, frame, new Vector3(x, y, -hd), 180f, 1.0f, 1.3f, (i + f) % 4 == 1);
                }
                for (int i = 0; i < 2; i++)
                {
                    float z = -hd + d * (i + 0.5f) / 2f;
                    HouseBuilder.Window(batch, frame, new Vector3(hw, y, z), 90f, 1.0f, 1.3f, (f + i) % 2 == 0);
                    HouseBuilder.Window(batch, frame, new Vector3(-hw, y, z), -90f, 1.0f, 1.3f, (f + i) % 3 == 1);
                }
            }
            batch.Frame = frame;

            // The Mail Room's door: glazed, double, under a canopy on two posts, a lamp over it.
            batch.Box(new Vector3(0f, Foundation + 1.25f, hd + 0.03f), new Vector3(2.0f, 2.5f, 0.12f), trim, 0.03f);
            batch.Box(new Vector3(0f, Foundation + 1.15f, hd + 0.07f), new Vector3(1.7f, 2.3f, 0.1f), MeshKit.Glow(new Color("ffd98a"), 1.2f), 0.02f);
            batch.Box(new Vector3(0f, Foundation + 1.15f, hd + 0.13f), new Vector3(0.08f, 2.3f, 0.06f), trim, 0.015f);
            batch.Box(new Vector3(0f, Foundation + 2.95f, hd + 0.8f), new Vector3(2.8f, 0.14f, 1.6f), roof, 0.03f);
            for (int s = -1; s <= 1; s += 2)
                batch.Box(new Vector3(s * 1.3f, Foundation + 1.45f, hd + 1.5f), new Vector3(0.12f, 2.9f, 0.12f), trim, 0.03f);
            batch.Box(new Vector3(0f, 0.1f, hd + 0.9f), new Vector3(2.6f, 0.2f, 1.8f), stone, 0.03f);
            var lamp = frame * new Vector3(0f, Foundation + 2.75f, hd + 0.8f);
            batch.Box(new Vector3(0f, Foundation + 2.75f, hd + 0.8f), new Vector3(0.4f, 0.14f, 0.4f), MeshKit.Glow(HouseBuilder.LanternLight, 3f), 0.04f);
            batch.Frame = Transform3D.Identity;
            return lamp;
        }

        /// <summary>
        /// The bank of the complex's unit boxes at the street, where its mail goes in (the Mail Room's one interaction
        /// point, doc 03 §2.1): grey steel doors in rows on a post, one a unit.
        /// </summary>
        public static void MailRoomBoxes(MeshBatch batch, DestinationDef dest, Color colour)
        {
            Scenery.Front(dest, out int fx, out int fz);
            var at = new Vector3(dest.MailboxX, 0f, dest.MailboxZ);
            batch.Frame = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(fx, fz)), at);
            var steel = MeshKit.Mat(new Color("9aa3ad"), 0.45f, 0.5f);
            var door = MeshKit.Mat(new Color("b8c0c8"), 0.4f, 0.5f);
            int units = Mathf.Max(1, dest.Units), cols = (units + 1) / 2;
            float width = 0.26f * cols + 0.1f;
            batch.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.14f, 0.9f, 0.14f), MeshKit.Mat(new Color("4a4f57"), 0.6f), 0.03f);
            batch.Box(new Vector3(0f, 1.2f, 0f), new Vector3(width, 0.62f, 0.36f), steel, 0.04f);
            batch.Box(new Vector3(0f, 1.56f, 0f), new Vector3(width + 0.08f, 0.08f, 0.42f), MeshKit.Mat(colour, 0.5f), 0.02f);
            for (int u = 0; u < units; u++)
            {
                int c = u % cols, r = u / cols;
                batch.Box(new Vector3(-width * 0.5f + 0.1f + 0.13f + c * 0.26f, 1.36f - r * 0.3f, 0.19f), new Vector3(0.22f, 0.24f, 0.03f), door, 0.01f);
            }
            batch.Frame = Transform3D.Identity;
        }

        private static int Mod(int v, int n) => ((v % n) + n) % n;
    }
}
