using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The two weapons of the concept video, built once for the hands of the first-person view and
    /// once (smaller in the picture) for the other carriers. Each has its origin at the grip.
    /// </summary>
    public static class WeaponModels
    {
        private static readonly Color Stone = new Color("9a9aa6");
        private static readonly Color Band = new Color("c23a2e");
        private static readonly Color Wood = new Color("8a5a34");
        private static readonly Color Cream = new Color("f1e6cf");
        private static readonly Color Ring = new Color("ff8a2a");

        /// <summary>The stamp hammer: a block of grey stone with two red bands on a wooden handle that points up (+Y).</summary>
        public static Node3D Hammer()
        {
            var root = new Node3D { Name = "Hammer" };
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.026f, 0.03f, 0.72f, 12), MeshKit.Mat(Wood, 0.8f), new Vector3(0f, 0.3f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Sphere(0.04f, 12), MeshKit.Mat(Wood.Darkened(0.3f), 0.7f), new Vector3(0f, -0.06f, 0f)));
            // The head lies along Z: it strikes with the face that points away from the carrier.
            var stone = MeshKit.Mat(Stone, 0.9f);
            var band = MeshKit.Mat(Band, 0.6f);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.2f, 0.2f, 0.36f), 0.04f), stone, new Vector3(0f, 0.68f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.215f, 0.215f, 0.05f), 0.02f), band, new Vector3(0f, 0.68f, -0.1f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.215f, 0.215f, 0.05f), 0.02f), band, new Vector3(0f, 0.68f, 0.1f)));
            return root;
        }

        private static readonly Color Iron = new Color("6f737c");
        private static readonly Color Edge = new Color("c9ccd2");

        /// <summary>
        /// A harvesting tool by its item id, built like the hammer (grip at the origin, the handle
        /// up +Y, the working end striking towards −Z); null for an item that has no model.
        /// </summary>
        public static Node3D? Tool(string itemId) => itemId switch
        {
            "axe" => Axe(),
            "pickaxe" => Pickaxe(),
            "shovel" => Shovel(),
            "repair_hammer" => RepairHammer(),
            _ => null,
        };

        /// <summary>
        /// The Repair Hammer: a short handle wrapped in safety orange at the grip, a squared steel head
        /// with a striking face one way and a split claw the other, and a band of tape at its neck.
        /// </summary>
        public static Node3D RepairHammer()
        {
            var root = new Node3D { Name = "Repair Hammer" };
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.022f, 0.026f, 0.56f, 12), MeshKit.Mat(Wood.Lightened(0.15f), 0.75f), new Vector3(0f, 0.24f, 0f)));
            var grip = MeshKit.Mat(new Color("e8742a"), 0.6f);
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.031f, 0.031f, 0.2f, 12), grip, new Vector3(0f, 0.02f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.028f, 0.028f, 0.04f, 12), MeshKit.Mat(new Color("f2c230"), 0.6f), new Vector3(0f, 0.43f, 0f)));
            var steel = MeshKit.Mat(Iron.Lightened(0.2f), 0.4f, 0.5f);
            // The head across the top of the handle: the face towards −Z (the way it strikes), the claw behind.
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.06f, 0.07f, 0.12f), 0.012f), steel, new Vector3(0f, 0.54f, -0.03f)));
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.04f, 0.04f, 0.06f, 14), MeshKit.Mat(Edge, 0.3f, 0.5f), new Vector3(0f, 0.54f, -0.11f), new Vector3(90f, 0f, 0f)));
            for (int k = -1; k <= 1; k += 2)
                root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.02f, 0.035f, 0.13f), 0.008f), steel, new Vector3(k * 0.017f, 0.52f, 0.09f), new Vector3(-22f, 0f, 0f)));
            return root;
        }

        /// <summary>The Axe: a long ash handle and a wedge of a head with a bright edge.</summary>
        public static Node3D Axe()
        {
            var root = new Node3D { Name = "Axe" };
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.024f, 0.028f, 0.78f, 12), MeshKit.Mat(Wood, 0.8f), new Vector3(0f, 0.32f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Sphere(0.036f, 12), MeshKit.Mat(Wood.Darkened(0.3f), 0.7f), new Vector3(0f, -0.07f, 0f)));
            var iron = MeshKit.Mat(Iron, 0.5f, 0.3f);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.05f, 0.13f, 0.2f), 0.015f), iron, new Vector3(0f, 0.66f, -0.06f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.035f, 0.2f, 0.05f), 0.012f), MeshKit.Mat(Edge, 0.35f, 0.3f), new Vector3(0f, 0.66f, -0.17f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.06f, 0.06f, 0.07f), 0.015f), iron, new Vector3(0f, 0.66f, 0.06f)));
            return root;
        }

        /// <summary>The Pickaxe: a curved iron head across the top of the handle, a point each way.</summary>
        public static Node3D Pickaxe()
        {
            var root = new Node3D { Name = "Pickaxe" };
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.025f, 0.029f, 0.74f, 12), MeshKit.Mat(Wood, 0.8f), new Vector3(0f, 0.3f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Sphere(0.036f, 12), MeshKit.Mat(Wood.Darkened(0.3f), 0.7f), new Vector3(0f, -0.07f, 0f)));
            var iron = MeshKit.Mat(Iron, 0.5f, 0.3f);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.06f, 0.07f, 0.09f), 0.015f), iron, new Vector3(0f, 0.66f, 0f)));
            for (int side = -1; side <= 1; side += 2)
            {
                // Each half of the head bends down a little towards its point.
                root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.03f, 0.008f, 0.24f, 8), iron, new Vector3(0f, 0.63f, side * 0.15f), new Vector3(side * 72f, 0f, 0f)));
            }
            return root;
        }

        /// <summary>The Shovel: a long handle with a D-grip and a rounded steel blade.</summary>
        public static Node3D Shovel()
        {
            var root = new Node3D { Name = "Shovel" };
            var wood = MeshKit.Mat(Wood, 0.8f);
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.022f, 0.025f, 0.86f, 12), wood, new Vector3(0f, 0.35f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.1f, 0.03f, 0.03f), 0.01f), wood, new Vector3(0f, -0.1f, 0f)));
            var steel = MeshKit.Mat(Iron.Lightened(0.25f), 0.4f, 0.3f);
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.03f, 0.024f, 0.08f, 10), steel, new Vector3(0f, 0.8f, 0f)));
            // The blade faces −Z, the way it digs, with its tip rounded.
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.2f, 0.22f, 0.025f), 0.01f), steel, new Vector3(0f, 0.94f, -0.015f), new Vector3(-8f, 0f, 0f)));
            var tip = MeshKit.Instance(MeshKit.Sphere(0.1f, 12), steel, new Vector3(0f, 1.04f, -0.03f));
            tip.Scale = new Vector3(1f, 0.55f, 0.13f);
            root.AddChild(tip);
            return root;
        }

        /// <summary>The mail cannon: a cream barrel along −Z with red trim and a glowing geared ring at the muzzle.</summary>
        public static Node3D Cannon()
        {
            var root = new Node3D { Name = "Cannon" };
            var cream = MeshKit.Mat(Cream, 0.55f);
            var red = MeshKit.Mat(Band, 0.5f);
            var dark = MeshKit.Mat(new Color("2a2c33"), 0.5f, 0.4f);
            var lying = new Vector3(90f, 0f, 0f);
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.1f, 0.085f, 0.5f), cream, new Vector3(0f, 0.12f, -0.2f), lying));
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.115f, 0.115f, 0.06f), red, new Vector3(0f, 0.12f, 0.02f), lying));
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.105f, 0.105f, 0.04f), red, new Vector3(0f, 0.12f, -0.26f), lying));
            root.AddChild(MeshKit.Instance(MeshKit.Sphere(0.1f, 16), cream, new Vector3(0f, 0.12f, 0.05f)));
            // The muzzle: a dark bore inside the orange ring and its gear teeth.
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.07f, 0.07f, 0.03f), dark, new Vector3(0f, 0.12f, -0.455f), lying));
            root.AddChild(MeshKit.Instance(MeshKit.Torus(0.085f, 0.125f), MeshKit.Glow(Ring, 2.2f), new Vector3(0f, 0.12f, -0.44f), lying));
            var tooth = MeshKit.RoundedBox(new Vector3(0.035f, 0.045f, 0.035f), 0.01f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Tau / 8f;
                root.AddChild(MeshKit.Instance(tooth, MeshKit.Glow(Ring, 2.2f), new Vector3(Mathf.Cos(a) * 0.135f, 0.12f + Mathf.Sin(a) * 0.135f, -0.44f), new Vector3(0f, 0f, Mathf.RadToDeg(a) + 90f)));
            }
            // The grip, a little behind the middle, and the hopper the packages go in.
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.05f, 0.17f, 0.06f), 0.02f), dark, new Vector3(0f, -0.03f, 0f), new Vector3(-14f, 0f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.13f, 0.1f, 0.16f), 0.03f), red, new Vector3(0f, 0.24f, -0.08f)));
            return root;
        }

        /// <summary>A parcel as it flies from the cannon or lies in the loot: brown paper and string.</summary>
        public static Node3D Parcel(float size = 0.3f)
        {
            var root = new Node3D { Name = "Parcel" };
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(size, size * 0.7f, size * 0.8f), size * 0.08f), MeshKit.Mat(new Color("c79a62"), 0.9f), Vector3.Zero));
            var twine = MeshKit.Mat(new Color("f3ead6"), 0.8f);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(size * 1.02f, size * 0.72f, size * 0.07f), size * 0.02f), twine, Vector3.Zero));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(size * 0.07f, size * 0.72f, size * 0.82f), size * 0.02f), twine, Vector3.Zero));
            return root;
        }

        /// <summary>A letter in the hand: cream paper with a red stamp.</summary>
        public static Node3D Letter()
        {
            var root = new Node3D { Name = "Letter" };
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.3f, 0.2f, 0.02f), 0.008f), MeshKit.Mat(new Color("fff8e7"), 0.8f), Vector3.Zero));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.05f, 0.06f, 0.024f), 0.006f), MeshKit.Mat(Band, 0.7f), new Vector3(0.1f, 0.05f, 0f)));
            // Doc 05 §6: cursed mail's glowing purple label, shown only while the letter in the hand is cursed.
            var curse = MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.18f, 0.05f, 0.026f), 0.006f), MeshKit.Glow(new Color("b65cff"), 3f), new Vector3(-0.04f, -0.05f, 0f), default, "Curse");
            curse.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            curse.Visible = false;
            root.AddChild(curse);
            return root;
        }
    }
}
