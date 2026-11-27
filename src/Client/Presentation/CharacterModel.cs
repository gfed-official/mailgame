using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// A body of rounded parts on a few pivots (hips, shoulders, the whole trunk), moved by code:
    /// a walk cycle from the speed it is carried at, a raised arm that comes down, a white flash
    /// when hurt, a fall when down. Carriers, raiders, the swarm and the giant are all one of these
    /// (<see cref="Characters"/> builds them); yaw 0 faces −Z like everything else.
    /// </summary>
    public partial class CharacterModel : Node3D
    {
        /// <summary>Radians of leg swing per metre walked, at scale 1.</summary>
        private const float StridePerMetre = 2.6f;
        private const float StrikeSeconds = 0.22f;
        private const float RecoverSeconds = 0.3f;
        private const float FlashSeconds = 0.18f;

        /// <summary>Everything above the feet; it leans, bobs and falls as one.</summary>
        public Node3D Trunk { get; } = new Node3D { Name = "Trunk" };
        public Node3D ArmL { get; private set; } = null!;
        public Node3D ArmR { get; private set; } = null!;
        public Node3D LegL { get; private set; } = null!;
        public Node3D LegR { get; private set; } = null!;
        /// <summary>The right hand: what is held hangs here.</summary>
        public Node3D Hand { get; private set; } = null!;
        /// <summary>Top of the head in metres at scale 1 (name tags and health bars sit above it).</summary>
        public float Height = 1.8f;
        /// <summary>How far the striking arm is raised, radians.</summary>
        public float RaisedArm = 2.5f;
        /// <summary>A creature that strikes with both arms (the giant's slam, the swarm's bite).</summary>
        public bool BothArms;
        /// <summary>The right arm is held out level (a carrier with the cannon out).</summary>
        public bool Aiming;

        private const float AimedArm = 1.35f;

        private readonly List<MeshInstance3D> _meshes = new List<MeshInstance3D>();
        private StandardMaterial3D? _flash;
        private float _stride, _amplitude, _raise, _aim, _strike = -1f, _flashLeft, _down;

        public CharacterModel()
        {
            AddChild(Trunk);
        }

        /// <summary>A pivot on the trunk (a shoulder) or on the root (a hip).</summary>
        public Node3D Pivot(string name, Vector3 position, bool onTrunk = true)
        {
            var pivot = new Node3D { Name = name, Position = position };
            (onTrunk ? Trunk : this).AddChild(pivot);
            return pivot;
        }

        public MeshInstance3D Part(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 rotationDegrees = default, Vector3? scale = null)
        {
            var part = MeshKit.Instance(mesh, material, position, rotationDegrees);
            if (scale != null) part.Scale = scale.Value;
            parent.AddChild(part);
            _meshes.Add(part);
            return part;
        }

        public void SetLimbs(Node3D armL, Node3D armR, Node3D legL, Node3D legR, Node3D hand)
        {
            ArmL = armL; ArmR = armR; LegL = legL; LegR = legR; Hand = hand;
        }

        /// <summary>The blow comes down now (a hammer swing, an enemy's hit).</summary>
        public void Strike() => _strike = 0f;

        /// <summary>A white flash over the whole body: it was hit.</summary>
        public void Flash() => _flashLeft = FlashSeconds;

        /// <param name="speed">Metres per second over the ground.</param>
        /// <param name="windup">The striking arm is held up (an enemy about to hit).</param>
        /// <param name="down">Lying on the ground.</param>
        public void Animate(float delta, float speed, bool windup, bool down)
        {
            float scale = Mathf.Max(0.2f, Scale.Y);
            _stride += speed / scale * StridePerMetre * delta;
            _amplitude = Mathf.MoveToward(_amplitude, Mathf.Clamp(speed / (2.5f * scale), 0f, 1f), delta * 6f);
            float swing = Mathf.Sin(_stride) * 0.75f * _amplitude;
            LegL.Rotation = new Vector3(swing, 0f, 0f);
            LegR.Rotation = new Vector3(-swing, 0f, 0f);

            _raise = Mathf.MoveToward(_raise, windup ? 1f : 0f, delta * (windup ? 5f : 8f));
            // A positive turn about X brings a hanging arm forward (−Z) and up.
            float arm = -swing * 0.8f, struck = _raise * RaisedArm;
            if (_strike >= 0f)
            {
                _strike += delta;
                // Up in a blink, down hard, then back to the side.
                if (_strike < StrikeSeconds) struck = Mathf.Lerp(RaisedArm, 0.9f, Mathf.Ease(_strike / StrikeSeconds, 2.2f));
                else if (_strike < StrikeSeconds + RecoverSeconds) struck = Mathf.Lerp(0.9f, 0f, (_strike - StrikeSeconds) / RecoverSeconds);
                else _strike = -1f;
            }
            bool striking = _strike >= 0f || _raise > 0.01f;
            _aim = Mathf.MoveToward(_aim, Aiming ? 1f : 0f, delta * 8f);
            ArmR.Rotation = new Vector3(striking ? struck : Mathf.Lerp(arm, AimedArm, _aim), 0f, 0f);
            ArmL.Rotation = new Vector3(striking && BothArms ? struck : -arm, 0f, 0f);

            _down = Mathf.MoveToward(_down, down ? 1f : 0f, delta * 3.5f);
            float fall = Mathf.Ease(_down, 0.5f);
            float lean = _amplitude * -0.12f + _raise * 0.18f + (_strike >= 0f && _strike < StrikeSeconds ? -0.25f : 0f);
            Trunk.Rotation = new Vector3(Mathf.Lerp(lean, Mathf.Pi * 0.5f, fall), 0f, 0f);
            Trunk.Position = new Vector3(0f, Mathf.Abs(Mathf.Sin(_stride)) * 0.05f * _amplitude + fall * 0.22f, fall * 0.5f);
            LegL.Visible = LegR.Visible = _down < 0.5f;

            if (_flashLeft > 0f || _flash != null) ApplyFlash(delta);
        }

        private void ApplyFlash(float delta)
        {
            _flashLeft = Mathf.Max(0f, _flashLeft - delta);
            if (_flashLeft <= 0f)
            {
                if (_flash == null) return;
                foreach (var mesh in _meshes) mesh.MaterialOverlay = null;
                _flash = null;
                return;
            }
            if (_flash == null)
            {
                _flash = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                };
                foreach (var mesh in _meshes) mesh.MaterialOverlay = _flash;
            }
            _flash.AlbedoColor = new Color(1f, 0.96f, 0.9f, 0.85f * _flashLeft / FlashSeconds);
        }
    }

    /// <summary>The people and creatures of the concept video, built from rounded shapes (no art assets yet).</summary>
    public static class Characters
    {
        private static readonly Color Skin = new Color("e8b796");
        private static readonly Color Navy = new Color("2b3350");
        private static readonly Color Leather = new Color("7a4a2b");
        private static readonly Color Iron = new Color("8b9099");
        private static readonly Color Bone = new Color("e9dfc8");
        private static readonly Color RaiderRed = new Color("7d2227");
        private static readonly Color SwarmRed = new Color("d8402f");
        private static readonly Color GiantHide = new Color("4b3d5c");
        private static readonly Color ArcherGreen = new Color("3f5a34");

        /// <summary>A mail carrier: jacket and cap in the player's colour, satchel on the hip.</summary>
        /// <param name="capColour">The cap's (doc 10 §8 cosmetics); null for a shade darker than the uniform.</param>
        public static CharacterModel Carrier(Color colour, Color? capColour = null)
        {
            var m = new CharacterModel { Name = "Carrier", Height = 1.86f };
            var jacket = MeshKit.Mat(colour, 0.8f);
            var trousers = MeshKit.Mat(Navy, 0.85f);
            var skin = MeshKit.Mat(Skin, 0.7f);
            Humanoid(m, jacket, jacket, trousers, skin, MeshKit.Mat(new Color("2a2422"), 0.6f), shoulder: 0.31f, torsoRadius: 0.25f, limb: 0.085f);

            // The cap: a low crown and a brim over the eyes.
            var cap = MeshKit.Mat(capColour ?? colour.Darkened(0.35f), 0.7f);
            m.Part(m.Trunk, MeshKit.Sphere(0.215f), cap, new Vector3(0f, 1.74f, 0.01f), scale: new Vector3(1f, 0.62f, 1f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.3f, 0.035f, 0.2f), 0.017f), cap, new Vector3(0f, 1.71f, -0.24f), new Vector3(-8f, 0f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.03f, 10), MeshKit.Mat(new Color("f2c14e"), 0.4f, 0.6f), new Vector3(0f, 1.78f, -0.2f));
            // Eyes, so the facing reads from across the street.
            var eye = MeshKit.Mat(new Color("1b1b1e"), 0.4f);
            m.Part(m.Trunk, MeshKit.Sphere(0.028f, 10), eye, new Vector3(-0.075f, 1.63f, -0.185f));
            m.Part(m.Trunk, MeshKit.Sphere(0.028f, 10), eye, new Vector3(0.075f, 1.63f, -0.185f));
            // The satchel and its strap.
            var leather = MeshKit.Mat(Leather, 0.8f);
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.14f, 0.3f, 0.36f), 0.05f), leather, new Vector3(-0.3f, 0.92f, 0.02f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.62f, 0.05f, 0.03f), 0.012f), leather, new Vector3(0f, 1.2f, -0.235f), new Vector3(0f, 0f, -38f));
            return m;
        }

        /// <summary>The enemy the content calls <paramref name="id"/>; an unknown one is drawn as a raider.</summary>
        public static CharacterModel Enemy(string id, bool mega)
        {
            switch (id)
            {
                case "swarmling": return Swarmling(mega);
                case "giant": return Giant(mega);
                case "archer": return Archer(mega);
                case "wall_breaker": return WallBreaker(mega);
                case "hog_rider": return HogRider(mega);
                case "balloon": return Balloon(mega);
                case "tank": return Tank(mega);
                default: return Raider(mega);
            }
        }

        /// <summary>The helmeted dark-red raider of the video, club in hand.</summary>
        public static CharacterModel Raider(bool mega)
        {
            var m = new CharacterModel { Name = "Raider", Height = 1.9f };
            var tunic = MeshKit.Mat(RaiderRed, 0.85f);
            var skin = MeshKit.Mat(new Color("c99a7c"), 0.75f);
            Humanoid(m, tunic, skin, MeshKit.Mat(new Color("3b2a25"), 0.9f), skin, MeshKit.Mat(new Color("2a1f1c"), 0.8f), shoulder: 0.36f, torsoRadius: 0.3f, limb: 0.1f);

            var iron = MeshKit.Mat(Iron, 0.45f, 0.7f);
            // Helmet with a nose guard, a belt, and fur on the shoulders.
            m.Part(m.Trunk, MeshKit.Sphere(0.235f), iron, new Vector3(0f, 1.71f, 0.02f), scale: new Vector3(1f, 0.8f, 1f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.05f, 0.17f, 0.04f), 0.018f), iron, new Vector3(0f, 1.63f, -0.21f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.31f, 0.31f, 0.09f), MeshKit.Mat(Leather, 0.7f), new Vector3(0f, 0.95f, 0f));
            var fur = MeshKit.Mat(new Color("5a4638"), 1f);
            m.Part(m.Trunk, MeshKit.Blob(0.17f, 0.2f, 3f, 5, new Vector3(1.1f, 0.7f, 1.2f), 6), fur, new Vector3(-0.33f, 1.45f, 0f));
            m.Part(m.Trunk, MeshKit.Blob(0.17f, 0.2f, 3f, 9, new Vector3(1.1f, 0.7f, 1.2f), 6), fur, new Vector3(0.33f, 1.45f, 0f));
            Eyes(m, new Vector3(0f, 1.62f, -0.18f), 0.08f, 0.03f, mega);

            // The club, thick end up.
            var wood = MeshKit.Mat(new Color("6b4a2e"), 0.9f);
            m.Part(m.Hand, MeshKit.Cylinder(0.035f, 0.075f, 0.8f, 12), wood, new Vector3(0f, 0.05f, -0.3f), new Vector3(-80f, 0f, 0f));
            m.Part(m.Hand, MeshKit.Sphere(0.08f, 12), wood, new Vector3(0f, 0.12f, -0.7f));
            return m;
        }

        /// <summary>
        /// The Archer (doc 05 §2): a lean raider in a hooded green cloak with a quiver on the back
        /// and a long bow in the hand, so it reads apart from the clubs at a distance.
        /// </summary>
        public static CharacterModel Archer(bool mega)
        {
            var m = new CharacterModel { Name = "Archer", Height = 1.85f, RaisedArm = 1.6f };
            var cloak = MeshKit.Mat(ArcherGreen, 0.9f);
            var skin = MeshKit.Mat(new Color("c99a7c"), 0.75f);
            var leather = MeshKit.Mat(Leather, 0.8f);
            Humanoid(m, cloak, MeshKit.Mat(ArcherGreen.Darkened(0.25f), 0.9f), MeshKit.Mat(new Color("4a3b2a"), 0.9f), skin, MeshKit.Mat(new Color("2a1f1c"), 0.8f), shoulder: 0.32f, torsoRadius: 0.25f, limb: 0.085f);

            // The hood, its point falling back, and a dark face under it.
            m.Part(m.Trunk, MeshKit.Sphere(0.245f), cloak, new Vector3(0f, 1.68f, 0.03f), scale: new Vector3(1f, 1.05f, 1.05f));
            m.Part(m.Trunk, MeshKit.Cone(0.12f, 0.3f, 10), cloak, new Vector3(0f, 1.8f, 0.24f), new Vector3(-118f, 0f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.17f), MeshKit.Mat(new Color("2b2320"), 0.9f), new Vector3(0f, 1.63f, -0.08f), scale: new Vector3(1f, 1f, 0.8f));
            Eyes(m, new Vector3(0f, 1.65f, -0.22f), 0.06f, 0.025f, mega);
            // The cloak's hem over the shoulders and down the back.
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.62f, 0.9f, 0.08f), 0.04f), cloak, new Vector3(0f, 1.05f, 0.27f), new Vector3(6f, 0f, 0f));
            // The quiver, fletchings out of its top.
            m.Part(m.Trunk, MeshKit.Cylinder(0.08f, 0.07f, 0.6f, 12), leather, new Vector3(0.16f, 1.3f, 0.3f), new Vector3(0f, 0f, -20f));
            var fletch = MeshKit.Mat(new Color("e9dfc8"), 0.7f);
            for (int k = 0; k < 3; k++)
                m.Part(m.Trunk, MeshKit.Box(new Vector3(0.03f, 0.12f, 0.06f)), fletch, new Vector3(0.24f + k * 0.025f, 1.66f, 0.29f + (k - 1) * 0.03f), new Vector3(0f, 0f, -20f));

            // The bow: a curved stave of three pieces and its string, held upright out in front.
            var wood = MeshKit.Mat(new Color("8a5a32"), 0.7f);
            m.Part(m.Hand, MeshKit.Cylinder(0.025f, 0.025f, 0.5f, 8), wood, new Vector3(0f, 0f, -0.12f));
            m.Part(m.Hand, MeshKit.Cylinder(0.02f, 0.025f, 0.45f, 8), wood, new Vector3(0f, 0.43f, -0.04f), new Vector3(-22f, 0f, 0f));
            m.Part(m.Hand, MeshKit.Cylinder(0.025f, 0.02f, 0.45f, 8), wood, new Vector3(0f, -0.43f, -0.04f), new Vector3(22f, 0f, 0f));
            m.Part(m.Hand, MeshKit.Cylinder(0.006f, 0.006f, 1.25f, 4), fletch, new Vector3(0f, 0f, 0.05f));
            return m;
        }

        /// <summary>
        /// The Wall Breaker (doc 05 §2): a small runner in a rag mask with a black powder keg on its
        /// shoulder, its fuse lit — what it means to do reads from down the street.
        /// </summary>
        public static CharacterModel WallBreaker(bool mega)
        {
            var m = new CharacterModel { Name = "WallBreaker", Height = 1.6f, RaisedArm = 2.9f };
            var rags = MeshKit.Mat(new Color("8c6b3f"), 0.95f);
            var skin = MeshKit.Mat(new Color("b58a6c"), 0.8f);
            Humanoid(m, rags, skin, MeshKit.Mat(new Color("3b2a25"), 0.9f), skin, MeshKit.Mat(new Color("2a1f1c"), 0.8f), shoulder: 0.3f, torsoRadius: 0.24f, limb: 0.08f);
            // A rag mask over the face, two eye holes.
            m.Part(m.Trunk, MeshKit.Sphere(0.215f), MeshKit.Mat(new Color("d9c9a3"), 0.95f), new Vector3(0f, 1.65f, 0.01f), scale: new Vector3(1f, 0.95f, 1f));
            Eyes(m, new Vector3(0f, 1.66f, -0.19f), 0.075f, 0.035f, glow: true);
            // The keg on the left shoulder, banded, its fuse burning.
            var keg = MeshKit.Mat(new Color("26221f"), 0.6f, 0.2f);
            var band = MeshKit.Mat(new Color("8b9099"), 0.4f, 0.7f);
            m.Part(m.Trunk, MeshKit.Cylinder(0.24f, 0.24f, 0.46f, 16), keg, new Vector3(-0.32f, 1.72f, 0.06f), new Vector3(0f, 0f, 70f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.25f, 0.25f, 0.05f, 16), band, new Vector3(-0.24f, 1.69f, 0.06f), new Vector3(0f, 0f, 70f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.25f, 0.25f, 0.05f, 16), band, new Vector3(-0.4f, 1.75f, 0.06f), new Vector3(0f, 0f, 70f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.015f, 0.015f, 0.18f, 6), MeshKit.Mat(new Color("c9b48a"), 0.9f), new Vector3(-0.58f, 1.86f, 0.06f), new Vector3(0f, 0f, 40f));
            m.Part(m.Trunk, MeshKit.Sphere(0.05f, 8), MeshKit.Glow(new Color("ffb347"), 4f), new Vector3(-0.64f, 1.93f, 0.06f));
            return m;
        }

        /// <summary>
        /// The Hog Rider (doc 05 §2): a bare-armed brute with a black topknot astride a bristling
        /// brown hog with tusks, a war hammer in the hand; the hog's front legs carry the stride.
        /// </summary>
        public static CharacterModel HogRider(bool mega)
        {
            var m = new CharacterModel { Name = "HogRider", Height = 1.95f, RaisedArm = 2.6f };
            var hide = MeshKit.Mat(new Color("6e4b36"), 0.95f);
            var bristle = MeshKit.Mat(new Color("3b2a20"), 1f);
            var snout = MeshKit.Mat(new Color("c98f7a"), 0.8f);
            var bone = MeshKit.Mat(Bone, 0.55f);
            // The hog: a long round body, a heavy head low in front, a ridge of bristles down the back.
            m.Part(m.Trunk, MeshKit.Blob(0.42f, 0.06f, 2.4f, 13, new Vector3(0.95f, 0.85f, 1.55f), 9), hide, new Vector3(0f, 0.68f, 0.05f));
            m.Part(m.Trunk, MeshKit.Sphere(0.27f), hide, new Vector3(0f, 0.66f, -0.6f), scale: new Vector3(1f, 0.95f, 1.15f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.11f, 0.13f, 0.2f, 14), snout, new Vector3(0f, 0.6f, -0.86f), new Vector3(90f, 0f, 0f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.1f, 0.24f, 0.9f), 0.04f), bristle, new Vector3(0f, 1.0f, -0.05f));
            for (int side = -1; side <= 1; side += 2)
            {
                m.Part(m.Trunk, MeshKit.Cone(0.035f, 0.2f, 8), bone, new Vector3(side * 0.12f, 0.58f, -0.86f), new Vector3(-60f, 0f, side * -25f));
                m.Part(m.Trunk, MeshKit.Cone(0.07f, 0.16f, 8), bristle, new Vector3(side * 0.16f, 0.9f, -0.62f), new Vector3(-20f, 0f, side * -35f));
                m.Part(m.Trunk, MeshKit.Sphere(0.03f, 8), MeshKit.Mat(new Color("1b1b1e"), 0.4f), new Vector3(side * 0.13f, 0.74f, -0.8f));
            }
            // Its legs: the front pair walks, the hind pair stands.
            var legL = m.Pivot("LegL", new Vector3(-0.2f, 0.46f, -0.38f), onTrunk: false);
            var legR = m.Pivot("LegR", new Vector3(0.2f, 0.46f, -0.38f), onTrunk: false);
            foreach (var leg in new[] { legL, legR })
            {
                m.Part(leg, MeshKit.Capsule(0.085f, 0.46f, 12), hide, new Vector3(0f, -0.22f, 0f));
                m.Part(leg, MeshKit.Cylinder(0.075f, 0.085f, 0.08f, 10), bristle, new Vector3(0f, -0.43f, 0f));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                m.Part(m.Trunk, MeshKit.Capsule(0.095f, 0.5f, 12), hide, new Vector3(side * 0.21f, 0.24f, 0.48f));
                m.Part(m.Trunk, MeshKit.Cylinder(0.075f, 0.085f, 0.08f, 10), bristle, new Vector3(side * 0.21f, 0.03f, 0.48f));
            }

            // The rider, astride its back: legs down its flanks, a broad bare chest, a topknot.
            var skin = MeshKit.Mat(new Color("b98263"), 0.75f);
            var leather = MeshKit.Mat(Leather, 0.8f);
            m.Part(m.Trunk, MeshKit.Capsule(0.27f, 0.62f), skin, new Vector3(0f, 1.36f, 0.08f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.29f, 0.29f, 0.12f), leather, new Vector3(0f, 1.12f, 0.08f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.62f, 0.08f, 0.5f), 0.03f), MeshKit.Mat(RaiderRed, 0.85f), new Vector3(0f, 1.0f, 0.1f));
            for (int side = -1; side <= 1; side += 2)
            {
                m.Part(m.Trunk, MeshKit.Capsule(0.1f, 0.6f, 12), leather, new Vector3(side * 0.38f, 0.86f, 0.0f), new Vector3(-30f, 0f, side * 12f));
                m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.15f, 0.1f, 0.26f), 0.04f), MeshKit.Mat(new Color("2a1f1c"), 0.8f), new Vector3(side * 0.44f, 0.6f, -0.12f));
            }
            m.Part(m.Trunk, MeshKit.Sphere(0.2f), skin, new Vector3(0f, 1.8f, 0.04f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.06f, 0.09f, 0.2f, 10), bristle, new Vector3(0f, 2.0f, 0.08f));
            // A heavy brow over the eyes.
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.24f, 0.045f, 0.06f), 0.02f), bristle, new Vector3(0f, 1.88f, -0.16f), new Vector3(-10f, 0f, 0f));
            Eyes(m, new Vector3(0f, 1.82f, -0.15f), 0.07f, 0.028f, mega);
            var armL = m.Pivot("ArmL", new Vector3(-0.36f, 1.6f, 0.08f));
            var armR = m.Pivot("ArmR", new Vector3(0.36f, 1.6f, 0.08f));
            foreach (var arm in new[] { armL, armR })
            {
                m.Part(arm, MeshKit.Capsule(0.1f, 0.62f, 14), skin, new Vector3(0f, -0.26f, 0f));
                m.Part(arm, MeshKit.Sphere(0.11f, 12), skin, new Vector3(0f, -0.58f, 0f));
            }
            var hand = new Node3D { Name = "Hand", Position = new Vector3(0f, -0.58f, 0f) };
            armR.AddChild(hand);
            m.SetLimbs(armL, armR, legL, legR, hand);
            // The war hammer: a long haft and a heavy iron head.
            var iron = MeshKit.Mat(Iron, 0.45f, 0.7f);
            m.Part(hand, MeshKit.Cylinder(0.03f, 0.035f, 0.85f, 10), MeshKit.Mat(new Color("6b4a2e"), 0.9f), new Vector3(0f, 0.05f, -0.32f), new Vector3(-80f, 0f, 0f));
            m.Part(hand, MeshKit.RoundedBox(new Vector3(0.34f, 0.16f, 0.16f), 0.03f), iron, new Vector3(0f, 0.12f, -0.72f));
            return m;
        }

        /// <summary>
        /// The Bomb Balloon (doc 05 §2): a striped red and cream envelope over a wicker basket on four
        /// ropes, a goggled bomber in it with a bomb in the hand and more slung on the basket's sides.
        /// Its origin is the basket's floor; <see cref="EnemiesView"/> draws it at its altitude.
        /// </summary>
        public static CharacterModel Balloon(bool mega)
        {
            var m = new CharacterModel { Name = "Balloon", Height = 5.6f, RaisedArm = 2.4f };
            var red = MeshKit.Mat(RaiderRed.Lightened(0.12f), 0.75f);
            var cream = MeshKit.Mat(new Color("e6d8b8"), 0.8f);
            var wicker = MeshKit.Mat(new Color("9a7044"), 0.95f);
            var rope = MeshKit.Mat(new Color("c9b48a"), 0.9f);
            var dark = MeshKit.Mat(new Color("2a1f1c"), 0.8f);
            var bombMat = MeshKit.Mat(new Color("1f1d22"), 0.45f, 0.3f);
            // The basket and its rim.
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.95f, 0.62f, 0.95f), 0.06f), wicker, new Vector3(0f, 0.31f, 0f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(1.02f, 0.08f, 1.02f), 0.03f), MeshKit.Mat(new Color("5e4128"), 0.9f), new Vector3(0f, 0.62f, 0f));
            for (int i = 0; i < 3; i++)
                m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.97f, 0.025f, 0.97f), 0.01f), MeshKit.Mat(new Color("7c5833"), 0.95f), new Vector3(0f, 0.12f + i * 0.16f, 0f));
            // Bombs slung on its sides.
            for (int side = -1; side <= 1; side += 2)
            {
                m.Part(m.Trunk, MeshKit.Sphere(0.15f, 12), bombMat, new Vector3(side * 0.55f, 0.32f, 0.12f));
                m.Part(m.Trunk, MeshKit.Sphere(0.15f, 12), bombMat, new Vector3(side * 0.55f, 0.32f, -0.2f));
            }
            // The ropes up to the skirt, the burner's glow under the mouth.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    m.Part(m.Trunk, MeshKit.Cylinder(0.014f, 0.014f, 1.2f, 5), rope, new Vector3(sx * 0.5f, 1.22f, sz * 0.5f), new Vector3(sz * 4f, 0f, sx * -4f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.5f, 0.78f, 0.5f, 18), dark, new Vector3(0f, 2.02f, 0f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.1f, 0.14f, 0.16f, 10), MeshKit.Mat(Iron, 0.4f, 0.7f), new Vector3(0f, 1.62f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.11f, 10), MeshKit.Glow(new Color("ffb347"), 5f), new Vector3(0f, 1.74f, 0f));
            // The envelope: red, with eight cream gores running pole to pole, a dark cap on top.
            var envelope = new Vector3(0f, 3.75f, 0f);
            m.Part(m.Trunk, MeshKit.Sphere(1.6f, 28), red, envelope, scale: new Vector3(1f, 1.18f, 1f));
            for (int k = 0; k < 4; k++)
                m.Part(m.Trunk, MeshKit.Sphere(1.6f, 28), cream, envelope, new Vector3(0f, k * 45f, 0f), new Vector3(1.012f, 1.192f, 0.14f));
            m.Part(m.Trunk, MeshKit.Sphere(0.42f, 14), dark, envelope + new Vector3(0f, 1.78f, 0f), scale: new Vector3(1f, 0.4f, 1f));
            // The bomber: shoulders over the rim, a leather cap and brass goggles.
            var skin = MeshKit.Mat(new Color("c99a7c"), 0.75f);
            m.Part(m.Trunk, MeshKit.Capsule(0.2f, 0.5f, 12), MeshKit.Mat(RaiderRed, 0.85f), new Vector3(0f, 0.72f, 0.05f));
            m.Part(m.Trunk, MeshKit.Sphere(0.16f, 14), skin, new Vector3(0f, 1.08f, 0.02f));
            m.Part(m.Trunk, MeshKit.Sphere(0.17f, 14), MeshKit.Mat(Leather, 0.8f), new Vector3(0f, 1.13f, 0.04f), scale: new Vector3(1f, 0.75f, 1f));
            for (int side = -1; side <= 1; side += 2)
                m.Part(m.Trunk, MeshKit.Cylinder(0.05f, 0.05f, 0.05f, 10), MeshKit.Mat(new Color("c79a3c"), 0.35f, 0.8f), new Vector3(side * 0.06f, 1.1f, -0.13f), new Vector3(90f, 0f, 0f));
            Eyes(m, new Vector3(0f, 1.1f, -0.15f), 0.06f, 0.025f, mega);
            // The throwing arm, a bomb in the hand; the other arm and the legs are not seen.
            var armR = m.Pivot("ArmR", new Vector3(0.24f, 0.92f, 0f));
            m.Part(armR, MeshKit.Capsule(0.07f, 0.44f, 10), MeshKit.Mat(RaiderRed, 0.85f), new Vector3(0f, -0.18f, 0f));
            var hand = new Node3D { Name = "Hand", Position = new Vector3(0f, -0.4f, 0f) };
            armR.AddChild(hand);
            m.Part(hand, MeshKit.Sphere(0.14f, 12), bombMat, new Vector3(0f, -0.06f, 0f));
            m.Part(hand, MeshKit.Sphere(0.04f, 8), MeshKit.Glow(new Color("ff8a2a"), 4f), new Vector3(0f, 0.1f, 0f));
            m.SetLimbs(m.Pivot("ArmL", new Vector3(-0.24f, 0.92f, 0f)), armR, m.Pivot("LegL", Vector3.Zero, onTrunk: false), m.Pivot("LegR", Vector3.Zero, onTrunk: false), hand);
            return m;
        }

        /// <summary>
        /// The Iron Tank (doc 05 §2): a hulking brute in riveted iron plate, a slit-visored great helm,
        /// a tower shield bolted to its left arm held before it to shove with, a spiked mace in the
        /// right; it is drawn twice a carrier's size.
        /// </summary>
        public static CharacterModel Tank(bool mega)
        {
            var m = new CharacterModel { Name = "Tank", Height = 1.95f, RaisedArm = 2.4f };
            var plate = MeshKit.Mat(new Color("8d949d"), 0.5f, 0.35f);
            var dark = MeshKit.Mat(new Color("4b5058"), 0.6f, 0.3f);
            var rivet = MeshKit.Mat(new Color("a7adb5"), 0.3f, 0.9f);
            var cloth = MeshKit.Mat(RaiderRed, 0.85f);
            Humanoid(m, plate, plate, dark, dark, MeshKit.Mat(new Color("2a1f1c"), 0.8f), shoulder: 0.42f, torsoRadius: 0.36f, limb: 0.13f);
            // A red tabard over the plate, a heavy belt, round pauldrons with rivets.
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.42f, 0.62f, 0.06f), 0.03f), cloth, new Vector3(0f, 1.0f, -0.36f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.39f, 0.39f, 0.12f, 18), MeshKit.Mat(Leather, 0.8f), new Vector3(0f, 0.86f, 0f));
            for (int side = -1; side <= 1; side += 2)
            {
                m.Part(m.Trunk, MeshKit.Sphere(0.2f, 14), plate, new Vector3(side * 0.42f, 1.44f, 0f), scale: new Vector3(1.1f, 0.8f, 1.1f));
                m.Part(m.Trunk, MeshKit.Sphere(0.035f, 8), rivet, new Vector3(side * 0.5f, 1.55f, -0.12f));
            }
            // The great helm, its visor slit glowing for a mega.
            m.Part(m.Trunk, MeshKit.Cylinder(0.22f, 0.2f, 0.36f, 18), plate, new Vector3(0f, 1.68f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.2f, 16), plate, new Vector3(0f, 1.86f, 0f), scale: new Vector3(1f, 0.5f, 1f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.3f, 0.035f, 0.04f), 0.01f), mega ? MeshKit.Glow(new Color("ff9a3c"), 3f) : MeshKit.Mat(new Color("111114"), 0.6f), new Vector3(0f, 1.72f, -0.2f));
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.04f, 0.2f, 0.05f), 0.01f), dark, new Vector3(0f, 1.62f, -0.21f));
            // The tower shield, held before it on the left arm.
            var wood = MeshKit.Mat(new Color("6b4a2e"), 0.85f);
            m.Part(m.ArmL, MeshKit.RoundedBox(new Vector3(0.75f, 1.15f, 0.08f), 0.04f), wood, new Vector3(0.22f, -0.3f, -0.34f));
            m.Part(m.ArmL, MeshKit.RoundedBox(new Vector3(0.79f, 0.08f, 0.1f), 0.02f), plate, new Vector3(0.22f, 0.2f, -0.35f));
            m.Part(m.ArmL, MeshKit.RoundedBox(new Vector3(0.79f, 0.08f, 0.1f), 0.02f), plate, new Vector3(0.22f, -0.8f, -0.35f));
            m.Part(m.ArmL, MeshKit.Sphere(0.12f, 12), rivet, new Vector3(0.22f, -0.3f, -0.4f), scale: new Vector3(1f, 1f, 0.5f));
            // The spiked mace.
            m.Part(m.Hand, MeshKit.Cylinder(0.035f, 0.04f, 0.7f, 10), wood, new Vector3(0f, 0.05f, -0.28f), new Vector3(-80f, 0f, 0f));
            m.Part(m.Hand, MeshKit.Sphere(0.15f, 12), dark, new Vector3(0f, 0.11f, -0.62f));
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.Tau / 6f;
                m.Part(m.Hand, MeshKit.Cone(0.04f, 0.14f, 6), rivet, new Vector3(Mathf.Cos(a) * 0.15f, 0.11f + Mathf.Sin(a) * 0.15f, -0.62f), new Vector3(0f, 0f, Mathf.RadToDeg(a) - 90f));
            }
            return m;
        }

        /// <summary>One of the swarm: a round red body on short legs, all eyes and horns.</summary>
        public static CharacterModel Swarmling(bool mega)
        {
            var m = new CharacterModel { Name = "Swarmling", Height = 1.5f, RaisedArm = 2.0f, BothArms = true };
            var red = MeshKit.Mat(SwarmRed, 0.6f);
            var dark = MeshKit.Mat(SwarmRed.Darkened(0.45f), 0.7f);
            m.Part(m.Trunk, MeshKit.Blob(0.46f, 0.08f, 2.2f, 3, new Vector3(1f, 0.95f, 0.95f), 8), red, new Vector3(0f, 0.92f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.3f), MeshKit.Mat(new Color("f08a6a"), 0.7f), new Vector3(0f, 0.8f, -0.2f), scale: new Vector3(1f, 0.9f, 0.7f));

            var legL = m.Pivot("LegL", new Vector3(-0.2f, 0.5f, 0f), onTrunk: false);
            var legR = m.Pivot("LegR", new Vector3(0.2f, 0.5f, 0f), onTrunk: false);
            foreach (var leg in new[] { legL, legR })
            {
                m.Part(leg, MeshKit.Capsule(0.1f, 0.5f, 12), dark, new Vector3(0f, -0.25f, 0f));
                m.Part(leg, MeshKit.Sphere(0.13f, 12), dark, new Vector3(0f, -0.44f, -0.06f), scale: new Vector3(1f, 0.6f, 1.4f));
            }
            var armL = m.Pivot("ArmL", new Vector3(-0.44f, 1.0f, 0f));
            var armR = m.Pivot("ArmR", new Vector3(0.44f, 1.0f, 0f));
            foreach (var arm in new[] { armL, armR })
            {
                m.Part(arm, MeshKit.Capsule(0.08f, 0.46f, 12), dark, new Vector3(0f, -0.2f, 0f));
                m.Part(arm, MeshKit.Sphere(0.1f, 12), dark, new Vector3(0f, -0.42f, 0f));
            }
            var hand = new Node3D { Name = "Hand", Position = new Vector3(0f, -0.42f, 0f) };
            armR.AddChild(hand);
            m.SetLimbs(armL, armR, legL, legR, hand);

            var bone = MeshKit.Mat(Bone, 0.6f);
            m.Part(m.Trunk, MeshKit.Cone(0.07f, 0.26f, 10), bone, new Vector3(-0.22f, 1.4f, -0.02f), new Vector3(0f, 0f, 24f));
            m.Part(m.Trunk, MeshKit.Cone(0.07f, 0.26f, 10), bone, new Vector3(0.22f, 1.4f, -0.02f), new Vector3(0f, 0f, -24f));
            // Teeth under two big eyes.
            m.Part(m.Trunk, MeshKit.RoundedBox(new Vector3(0.3f, 0.07f, 0.04f), 0.02f), bone, new Vector3(0f, 0.78f, -0.42f));
            Eyes(m, new Vector3(0f, 1.04f, -0.37f), 0.15f, 0.11f, mega);
            return m;
        }

        /// <summary>The horned giant of the night raid: a heavy trunk on short legs, long arms, fists.</summary>
        public static CharacterModel Giant(bool mega)
        {
            var m = new CharacterModel { Name = "Giant", Height = 1.95f, RaisedArm = 2.7f, BothArms = true };
            var hide = MeshKit.Mat(GiantHide, 0.9f);
            var dark = MeshKit.Mat(GiantHide.Darkened(0.35f), 0.9f);
            var belly = MeshKit.Mat(GiantHide.Lightened(0.18f), 0.9f);
            m.Part(m.Trunk, MeshKit.Blob(0.47f, 0.07f, 2f, 11, new Vector3(1.1f, 1.05f, 0.9f), 9), hide, new Vector3(0f, 1.12f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.36f), belly, new Vector3(0f, 0.98f, -0.16f), scale: new Vector3(1f, 1f, 0.7f));
            m.Part(m.Trunk, MeshKit.Sphere(0.21f), hide, new Vector3(0f, 1.66f, -0.1f));
            m.Part(m.Trunk, MeshKit.Sphere(0.13f, 12), dark, new Vector3(0f, 1.6f, -0.26f), scale: new Vector3(1.2f, 0.8f, 1f));
            // Loincloth and a rope belt.
            m.Part(m.Trunk, MeshKit.Cylinder(0.46f, 0.42f, 0.2f), MeshKit.Mat(new Color("6b4a2e"), 1f), new Vector3(0f, 0.68f, 0f));

            var legL = m.Pivot("LegL", new Vector3(-0.24f, 0.62f, 0f), onTrunk: false);
            var legR = m.Pivot("LegR", new Vector3(0.24f, 0.62f, 0f), onTrunk: false);
            foreach (var leg in new[] { legL, legR })
            {
                m.Part(leg, MeshKit.Capsule(0.17f, 0.66f, 14), dark, new Vector3(0f, -0.3f, 0f));
                m.Part(leg, MeshKit.Sphere(0.19f, 12), dark, new Vector3(0f, -0.54f, -0.08f), scale: new Vector3(1f, 0.5f, 1.4f));
            }
            var armL = m.Pivot("ArmL", new Vector3(-0.56f, 1.42f, 0f));
            var armR = m.Pivot("ArmR", new Vector3(0.56f, 1.42f, 0f));
            foreach (var arm in new[] { armL, armR })
            {
                m.Part(arm, MeshKit.Sphere(0.2f, 14), hide, Vector3.Zero);
                m.Part(arm, MeshKit.Capsule(0.14f, 0.86f, 14), hide, new Vector3(0f, -0.4f, 0f));
                m.Part(arm, MeshKit.Sphere(0.2f, 14), dark, new Vector3(0f, -0.86f, 0f));
            }
            var hand = new Node3D { Name = "Hand", Position = new Vector3(0f, -0.86f, 0f) };
            armR.AddChild(hand);
            m.SetLimbs(armL, armR, legL, legR, hand);

            // The horns sweep out and up.
            var bone = MeshKit.Mat(Bone, 0.55f);
            for (int side = -1; side <= 1; side += 2)
            {
                m.Part(m.Trunk, MeshKit.Cylinder(0.075f, 0.06f, 0.26f, 12), bone, new Vector3(side * 0.27f, 1.76f, -0.08f), new Vector3(0f, 0f, side * -62f));
                m.Part(m.Trunk, MeshKit.Cone(0.06f, 0.34f, 12), bone, new Vector3(side * 0.43f, 1.94f, -0.08f), new Vector3(0f, 0f, side * -14f));
            }
            Eyes(m, new Vector3(0f, 1.7f, -0.29f), 0.08f, 0.04f, glow: true);
            return m;
        }

        /// <summary>Two legs, a capsule trunk, two arms and a head: the frame carriers and raiders share.</summary>
        private static void Humanoid(CharacterModel m, Material torso, Material sleeves, Material legs, Material skin, Material shoes, float shoulder, float torsoRadius, float limb)
        {
            m.Part(m.Trunk, MeshKit.Capsule(torsoRadius, 0.78f), torso, new Vector3(0f, 1.14f, 0f));
            m.Part(m.Trunk, MeshKit.Sphere(0.2f), skin, new Vector3(0f, 1.64f, 0f));
            m.Part(m.Trunk, MeshKit.Cylinder(0.08f, 0.09f, 0.1f, 12), skin, new Vector3(0f, 1.47f, 0f));

            var legL = m.Pivot("LegL", new Vector3(-0.13f, 0.84f, 0f), onTrunk: false);
            var legR = m.Pivot("LegR", new Vector3(0.13f, 0.84f, 0f), onTrunk: false);
            foreach (var leg in new[] { legL, legR })
            {
                m.Part(leg, MeshKit.Capsule(limb + 0.025f, 0.84f, 14), legs, new Vector3(0f, -0.4f, 0f));
                m.Part(leg, MeshKit.RoundedBox(new Vector3(0.17f, 0.11f, 0.3f), 0.05f), shoes, new Vector3(0f, -0.785f, -0.05f));
            }
            var armL = m.Pivot("ArmL", new Vector3(-shoulder, 1.4f, 0f));
            var armR = m.Pivot("ArmR", new Vector3(shoulder, 1.4f, 0f));
            foreach (var arm in new[] { armL, armR })
            {
                m.Part(arm, MeshKit.Capsule(limb, 0.64f, 14), sleeves, new Vector3(0f, -0.27f, 0f));
                m.Part(arm, MeshKit.Sphere(limb + 0.01f, 12), skin, new Vector3(0f, -0.6f, 0f));
            }
            var hand = new Node3D { Name = "Hand", Position = new Vector3(0f, -0.6f, 0f) };
            armR.AddChild(hand);
            m.SetLimbs(armL, armR, legL, legR, hand);
        }

        /// <summary>Two eyes around <paramref name="centre"/>; a mega's (and the giant's) burn orange.</summary>
        private static void Eyes(CharacterModel m, Vector3 centre, float apart, float size, bool glow)
        {
            var white = glow ? MeshKit.Glow(new Color("ff9a3c"), 3f) : MeshKit.Mat(new Color("fff6e6"), 0.4f);
            var pupil = MeshKit.Mat(new Color("1b1b1e"), 0.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                var at = centre + new Vector3(side * apart, 0f, 0f);
                m.Part(m.Trunk, MeshKit.Sphere(size, 12), white, at);
                if (!glow) m.Part(m.Trunk, MeshKit.Sphere(size * 0.5f, 10), pupil, at + new Vector3(0f, 0f, -size * 0.7f));
            }
        }
    }
}
