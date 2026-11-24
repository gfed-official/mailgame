using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The world as the concept video shows it (design/reference/higgsfield-01), built from the
    /// WorldMap (doc 07 §5): a dirt-and-cobble street through grass, clapboard houses with lit
    /// lanterns, mailboxes, the Post Office and its Intake counter, the map's props, and the island
    /// around it all. Nothing here is read by the simulation.
    /// </summary>
    public partial class WorldView : Node3D
    {
        private const float Tile = SimConstants.TileSize;
        /// <summary>The ground texture reaches this far past the map, so streets run on to the harbour and the gate.</summary>
        private const float GroundMargin = 24f;
        private const int TexelsPerMetre = 2;
        private const float GroundChunk = 16f;
        /// <summary>
        /// A mailbox's address is for the last steps to it; the number over the house is what is read
        /// from down the street, and past half the street even that is only clutter (nit 15).
        /// </summary>
        public const float AddressFadeStart = 9f, AddressFadeEnd = 15f;
        public const float NumberFadeStart = 34f, NumberFadeEnd = 46f;

        private static readonly Color PostOfficeWall = new Color("efe2c4");
        private static readonly Color PostOfficeRed = new Color("b23a34");
        private static readonly Color ShopTop = new Color("2f9c7a");

        public int MailboxCount { get; private set; }
        public Atmosphere Atmosphere { get; private set; } = null!;

        /// <summary>Lamps, porch lights and torches with their energy by day and by night.</summary>
        private readonly List<(OmniLight3D Light, float Day, float Night)> _lights = new List<(OmniLight3D, float, float)>();
        private float _litFor = -1f;
        private readonly List<(DestinationDef Dest, Label3D Label)> _mailboxLabels = new List<(DestinationDef, Label3D)>();
        private WorldMap? _map;
        /// <summary>The map this view was built for.</summary>
        public WorldMap? Map => _map;
        private int _districtsFor = -1;

        /// <summary>Mailboxes whose district is not open yet in the shift last shown (doc 01 §1.2 district unlock).</summary>
        public int LockedMailboxes { get; private set; }

        /// <summary>
        /// Draw the world without what costs a renderer most: lamp and porch lights, shadows, bloom
        /// and anti-aliasing, and with coarser shapes (<see cref="MeshKit.LowDetail"/>). For "--low"
        /// and for the smoke under a software renderer.
        /// </summary>
        public static bool Cheap;

        public override void _EnterTree()
        {
            if (!Cheap) GetViewport().Msaa3D = Viewport.Msaa.Msaa2X;
        }

        /// <param name="nodes">The props of the map that <see cref="NodesView"/> draws, if one does.</param>
        public void Build(WorldMap map, NodeField? nodes = null)
        {
            _map = map;
            MeshKit.LowDetail = Cheap;
            Atmosphere = new Atmosphere { Name = "Atmosphere" };
            AddChild(Atmosphere);
            Atmosphere.Build(Cheap);

            // The meshes keep the material and the material keeps the texture; C# lets go of both now rather than at exit, after the renderer has gone.
            using var groundMap = GroundMap(map);
            using var ground = Shaders.Ground(groundMap, new Vector2(-GroundMargin, -GroundMargin), new Vector2(map.WidthM + GroundMargin * 2f, map.DepthM + GroundMargin * 2f), IslandView.SeaLevelOf(map));
            if (map.Terrain is IslandTerrain island)
            {
                using var biomeMap = BiomeMap(island);
                Shaders.GroundBiomes(ground, biomeMap, new Vector2(-island.TownX * Tile, -island.TownZ * Tile), new Vector2(island.Cols * Tile, island.Rows * Tile), new Vector2(map.WidthM, map.DepthM));
            }
            AddGround(map, ground);
            IslandView.Basin(this, map);

            var batch = new MeshBatch();
            foreach (var dest in map.Destinations)
            {
                AddLight(HouseBuilder.Build(batch, dest), HouseBuilder.LanternLight, 7f, 0.5f, 2.6f);
                AddHouseNumber(dest);
                AddMailbox(batch, dest);
            }
            AddPostOffice(batch, map);
            foreach (var street in map.Streets) AddStreetSign(batch, map, street);
            foreach (var lamp in SceneryView.Build(batch, map, nodes)) AddLight(lamp + new Vector3(0f, -0.3f, 0f), SceneryView.LampLight, 12f, 0.8f, 4.5f);
            foreach (var torch in IslandView.Build(this, batch, map, ground)) AddLight(torch + new Vector3(-0.4f, 0.2f, 0f), new Color(1.0f, 0.6f, 0.25f), 12f, 0.8f, 3.0f);
            batch.Flush(this, "Batch");
            ApplyNight();
        }

        public override void _Process(double delta) => ApplyNight();

        /// <summary>
        /// The houses of a district not open yet by <paramref name="shift"/> say when it opens, in
        /// grey under their address; the others read as always. Cheap to call every frame.
        /// </summary>
        public void ShowDistricts(int shift)
        {
            if (shift == _districtsFor || _map == null) return;
            _districtsFor = shift;
            int locked = 0;
            foreach (var (dest, label) in _mailboxLabels)
            {
                int opens = HudText.DistrictOpensAt(_map, dest);
                bool closed = opens > shift;
                if (closed) locked++;
                label.Text = closed ? $"{dest.Label}\n{HudText.OpensInShift(opens)}" : dest.Label;
                label.Modulate = closed ? new Color(0.78f, 0.78f, 0.8f, 0.6f) : Colors.White;
            }
            LockedMailboxes = locked;
        }

        /// <summary>The lamps come up as the light goes down.</summary>
        private void ApplyNight()
        {
            float night = Atmosphere.Night;
            if (Mathf.IsEqualApprox(night, _litFor)) return;
            _litFor = night;
            foreach (var (light, day, dark) in _lights) light.LightEnergy = Mathf.Lerp(day, dark, night);
        }

        private void AddLight(Vector3 at, Color colour, float range, float day, float night)
        {
            if (Cheap) return;
            var light = new OmniLight3D
            {
                Position = at,
                LightColor = colour,
                OmniRange = range,
                OmniAttenuation = 1.4f,
                LightEnergy = day,
                ShadowEnabled = false,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 70f,
                DistanceFadeLength = 20f,
            };
            AddChild(light);
            _lights.Add((light, day, night));
        }

        /// <summary>
        /// What the ground is made of, as a texture the ground shader reads: red where a path runs
        /// (streets, and the walk from each front door), green on the lots' lawns, blue on the Post
        /// Office yard, alpha on the beach. Blurred, so edges are soft and the shader can tell a
        /// path's middle from its rim.
        /// </summary>
        private const int Channels = 4;

        private static ImageTexture GroundMap(WorldMap map)
        {
            int width = Mathf.CeilToInt((map.WidthM + GroundMargin * 2f) * TexelsPerMetre), height = Mathf.CeilToInt((map.DepthM + GroundMargin * 2f) * TexelsPerMetre);
            var data = new float[width * height * Channels];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float x = (px + 0.5f) / TexelsPerMetre - GroundMargin, z = (py + 0.5f) / TexelsPerMetre - GroundMargin;
                    int tx = Mathf.FloorToInt(x / Tile), tz = Mathf.FloorToInt(z / Tile);
                    int at = (py * width + px) * Channels;
                    if (map.InBounds(tx, tz))
                    {
                        switch (map.GetTile(tx, tz))
                        {
                            case TileType.Street: data[at] = 1f; break;
                            case TileType.Lot: case TileType.Building: data[at + 1] = 1f; break;
                            case TileType.Yard: data[at + 2] = 1f; break;
                            case TileType.Beach: data[at + 3] = 1f; break;
                        }
                        continue;
                    }
                    // Past the edge a street carries on in a straight line.
                    foreach (var street in map.Streets)
                    {
                        var r = street.Rect;
                        bool alongX = r.W >= r.H;
                        if (alongX ? tz >= r.Y && tz < r.Y + r.H && (r.X == 0 && tx < 0 || r.X + r.W == map.Cols && tx >= map.Cols)
                                   : tx >= r.X && tx < r.X + r.W && (r.Y == 0 && tz < 0 || r.Y + r.H == map.Rows && tz >= map.Rows))
                            data[at] = 1f;
                    }
                }
            }
            foreach (var dest in map.Destinations)
            {
                Scenery.Walkway(dest, out float x0, out float z0, out float x1, out float z1);
                var line = new Prop(PropKind.Fence, x0, z0, x1, z1, 0f, 1f, 0, 0f, 0f);
                int minX = Mathf.FloorToInt((Mathf.Min(x0, x1) - 1f + GroundMargin) * TexelsPerMetre), maxX = Mathf.CeilToInt((Mathf.Max(x0, x1) + 1f + GroundMargin) * TexelsPerMetre);
                int minY = Mathf.FloorToInt((Mathf.Min(z0, z1) - 1f + GroundMargin) * TexelsPerMetre), maxY = Mathf.CeilToInt((Mathf.Max(z0, z1) + 1f + GroundMargin) * TexelsPerMetre);
                for (int py = Mathf.Max(0, minY); py < Mathf.Min(height, maxY); py++)
                    for (int px = Mathf.Max(0, minX); px < Mathf.Min(width, maxX); px++)
                        if (line.Distance((px + 0.5f) / TexelsPerMetre - GroundMargin, (py + 0.5f) / TexelsPerMetre - GroundMargin, out _, out _) < 0.7f)
                            data[(py * width + px) * Channels] = 0.8f;
            }
            for (int pass = 0; pass < 2; pass++) data = Blur(data, width, height);

            var bytes = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(data[i] * 255f), 0, 255);
            using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes);
            return ImageTexture.CreateFromImage(image);
        }

        /// <summary>The island's biomes a texel a tile, for the ground shader: red beach, green forest, blue rocky.</summary>
        private static ImageTexture BiomeMap(IslandTerrain island)
        {
            var bytes = new byte[island.Cols * island.Rows * 4];
            for (int i = 0; i < island.Biomes.Length; i++)
            {
                int at = i * 4;
                switch (island.Biomes[i])
                {
                    case Biome.Beach: bytes[at] = 255; break;
                    case Biome.Forest: bytes[at + 1] = 255; break;
                    case Biome.Rocky: bytes[at + 2] = 255; break;
                }
                bytes[at + 3] = 255;
            }
            using var image = Image.CreateFromData(island.Cols, island.Rows, false, Image.Format.Rgba8, bytes);
            return ImageTexture.CreateFromImage(image);
        }

        /// <summary>A 5×5 box blur (one metre each way), done as two passes of five.</summary>
        private static float[] Blur(float[] source, int width, int height)
        {
            var across = new float[source.Length];
            var result = new float[source.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                var from = pass == 0 ? source : across;
                var to = pass == 0 ? across : result;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        for (int c = 0; c < Channels; c++)
                        {
                            float sum = 0f;
                            for (int k = -2; k <= 2; k++)
                            {
                                int sx = pass == 0 ? Mathf.Clamp(x + k, 0, width - 1) : x, sy = pass == 0 ? y : Mathf.Clamp(y + k, 0, height - 1);
                                sum += from[(sy * width + sx) * Channels + c];
                            }
                            to[(y * width + x) * Channels + c] = sum / 5f;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>The map's flat ground, in chunks, so each is lit by the few lamps near it.</summary>
        private void AddGround(WorldMap map, ShaderMaterial material)
        {
            // The water along the south edge is the harbour basin's (IslandView.Basin), not ground.
            float depth = map.LandRows * Tile;
            for (float z = 0f; z < depth; z += GroundChunk)
            {
                for (float x = 0f; x < map.WidthM; x += GroundChunk)
                {
                    float w = Mathf.Min(GroundChunk, map.WidthM - x), d = Mathf.Min(GroundChunk, depth - z);
                    AddChild(new MeshInstance3D
                    {
                        Name = $"Ground{x:0}_{z:0}",
                        Mesh = new PlaneMesh { Size = new Vector2(w, d) },
                        MaterialOverride = material,
                        Position = new Vector3(x + w * 0.5f, 0f, z + d * 0.5f),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }
            }
        }

        private void AddHouseNumber(DestinationDef dest)
        {
            var colour = Palette.HouseColour(dest.Variant);
            var frame = HouseBuilder.Frame(dest, out float width, out float depth);
            // The number floats above the gable that faces the mailbox: up close the label is small
            // (SizedLabel3D), and over the middle of the house the roof would hide it.
            var number = AddLabel(dest.Address.Number.ToString(), frame * new Vector3(0f, HouseBuilder.RidgeHeight(width) + 0.9f, depth * 0.5f), 160, colour.Lightened(0.5f));
            number.Name = $"Number{dest.Address.Number}";
            Fade(number, NumberFadeStart, NumberFadeEnd, 0);
        }

        private void AddMailbox(MeshBatch batch, DestinationDef dest)
        {
            var colour = Palette.HouseColour(dest.Variant);
            Scenery.Front(dest, out int fx, out int fz);
            var at = new Vector3(dest.MailboxX, 0f, dest.MailboxZ);
            batch.Frame = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(fx, fz)), at);
            batch.Box(new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 1.0f, 0.12f), MeshKit.Mat(new Color("6b4630"), 0.8f), 0.03f);
            batch.Box(new Vector3(0f, 0.98f, 0.05f), new Vector3(0.3f, 0.06f, 0.5f), MeshKit.Mat(new Color("6b4630"), 0.8f), 0.02f);
            // The box itself: round-topped, in the colour of its house, the flag up.
            batch.Box(new Vector3(0f, 1.17f, 0.05f), new Vector3(0.34f, 0.34f, 0.62f), MeshKit.Mat(colour, 0.45f, 0.1f), 0.15f);
            batch.Box(new Vector3(0f, 1.15f, 0.37f), new Vector3(0.28f, 0.26f, 0.03f), MeshKit.Mat(colour.Darkened(0.25f), 0.45f, 0.1f), 0.012f);
            batch.Box(new Vector3(0.19f, 1.3f, 0.1f), new Vector3(0.025f, 0.26f, 0.05f), MeshKit.Mat(new Color("d6332c"), 0.5f), 0.01f);
            batch.Box(new Vector3(0.19f, 1.4f, 0.16f), new Vector3(0.025f, 0.1f, 0.14f), MeshKit.Mat(new Color("d6332c"), 0.5f), 0.01f);
            batch.Frame = Transform3D.Identity;
            var label = MakeLabel(dest.Label, at + new Vector3(0f, 1.75f, 0f), 56, Colors.White, flat: false);
            label.Name = $"Mailbox{dest.Address.Number}";
            Fade(label, AddressFadeStart, AddressFadeEnd, 1);
            AddChild(label);
            _mailboxLabels.Add((dest, label));
            MailboxCount++;
        }

        private void AddPostOffice(MeshBatch batch, WorldMap map)
        {
            var po = map.PostOffice;
            var siding = Shaders.Clapboard(PostOfficeWall);
            var red = MeshKit.Mat(PostOfficeRed, 0.55f);
            var cream = MeshKit.Mat(new Color("f6f0e4"), 0.6f);
            var roof = Shaders.Shingles(new Color("7a2c2a"));
            const float foundation = 0.3f, wall = 4.2f;
            float intakeYaw = 0f;
            foreach (var solid in po.Solid)
            {
                // The hall faces the Intake: its long roof runs along that front.
                float dx = po.IntakeX - solid.CenterX, dz = po.IntakeZ - solid.CenterZ;
                bool frontX = Mathf.Abs(dx) / solid.W > Mathf.Abs(dz) / solid.H;
                float yaw = frontX ? (dx < 0 ? -Mathf.Pi * 0.5f : Mathf.Pi * 0.5f) : (dz < 0 ? Mathf.Pi : 0f);
                float w = (frontX ? solid.H : solid.W) * Tile, d = (frontX ? solid.W : solid.H) * Tile;
                var frame = new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(solid.CenterX, 0f, solid.CenterZ));
                intakeYaw = yaw;
                batch.Frame = frame;
                float hw = w * 0.5f, hd = d * 0.5f, top = foundation + wall, rise = hd * 0.62f;

                batch.Box(new Vector3(0f, foundation * 0.5f, 0f), new Vector3(w + 0.16f, foundation, d + 0.16f), MeshKit.Mat(new Color("8d8794"), 0.9f), 0.05f);
                batch.Add(MeshKit.Box(new Vector3(w, wall, d)), siding, new Vector3(0f, foundation + wall * 0.5f, 0f));
                batch.Add(MeshKit.Gable(d, rise, w), siding, new Vector3(0f, top, 0f), new Vector3(0f, 90f, 0f));
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        batch.Box(new Vector3(sx * hw, foundation + wall * 0.5f, sz * hd), new Vector3(0.24f, wall, 0.24f), red, 0.03f);
                batch.Box(new Vector3(0f, foundation + 0.5f, hd + 0.02f), new Vector3(w, 1.0f, 0.08f), red, 0.03f);

                const float overhang = 0.6f, thick = 0.18f;
                float alpha = Mathf.Atan2(rise, hd), run = hd + overhang, length = run / Mathf.Cos(alpha) + 0.06f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var basis = new Basis(Vector3.Up, -side * Mathf.Pi * 0.5f) * new Basis(Vector3.Back, -alpha);
                    var normal = new Vector3(0f, Mathf.Cos(alpha), side * Mathf.Sin(alpha));
                    var mid = new Vector3(0f, top + rise - run * 0.5f * Mathf.Tan(alpha), side * run * 0.5f);
                    batch.Add(MeshKit.RoundedBox(new Vector3(length, thick, w + 1.0f), 0.05f), roof, new Transform3D(basis, mid + normal * thick * 0.5f));
                    for (int end = -1; end <= 1; end += 2)
                        batch.Add(MeshKit.RoundedBox(new Vector3(length, 0.24f, 0.1f), 0.03f), cream, new Transform3D(basis, mid - normal * 0.05f + new Vector3(end * (hw + 0.47f), 0f, 0f)));
                }
                batch.Box(new Vector3(0f, top + rise + thick / Mathf.Cos(alpha) - 0.02f, 0f), new Vector3(w + 1.06f, 0.18f, 0.36f), MeshKit.Mat(new Color("5d2220"), 0.7f), 0.08f);

                // The front: the sign over the counter, lit windows, the staff door.
                var local = frame.AffineInverse() * new Vector3(po.IntakeX, 0f, po.IntakeZ);
                float ix = Mathf.Clamp(local.X, -hw + 2.6f, hw - 2.6f);
                batch.Box(new Vector3(ix, 3.75f, hd + 0.06f), new Vector3(5.4f, 0.95f, 0.14f), red, 0.05f);
                batch.Box(new Vector3(ix, 3.75f, hd + 0.1f), new Vector3(5.1f, 0.7f, 0.1f), MeshKit.Mat(new Color("f3e6c4"), 0.6f), 0.04f);
                AddChild(new Label3D
                {
                    Name = "PostOfficeSign",
                    Text = "POST OFFICE",
                    FontSize = 96,
                    PixelSize = 0.006f,
                    OutlineSize = 0,
                    Modulate = new Color("8f2a26"),
                    DoubleSided = false,
                    Transform = frame * new Transform3D(Basis.Identity, new Vector3(ix, 3.75f, hd + 0.16f)),
                });
                foreach (float wx in new[] { ix - 4.1f, ix + 4.1f })
                {
                    if (wx < -hw + 0.9f || wx > hw - 0.9f) continue;
                    if (wx > ix)
                    {
                        batch.Box(new Vector3(wx, foundation + 1.15f, hd + 0.03f), new Vector3(1.3f, 2.35f, 0.12f), cream, 0.03f);
                        batch.Box(new Vector3(wx, foundation + 1.07f, hd + 0.07f), new Vector3(1.04f, 2.12f, 0.1f), red, 0.03f);
                        batch.Box(new Vector3(wx, foundation + 1.6f, hd + 0.11f), new Vector3(0.55f, 0.6f, 0.05f), MeshKit.Glow(new Color("ffd98a"), 1.4f), 0.02f);
                    }
                    else HouseBuilder.Window(batch, frame, new Vector3(wx, foundation + 1.9f, hd), 0f, 1.2f, 1.4f, true);
                    batch.Frame = frame;
                }
                HouseBuilder.Window(batch, frame, new Vector3(hw, foundation + 1.9f, 0f), 90f, 1.4f, 1.4f, true);
                HouseBuilder.Window(batch, frame, new Vector3(-hw, foundation + 1.9f, 0f), -90f, 1.4f, 1.4f, true);
                batch.Frame = frame;

                // The service window and its striped awning.
                batch.Box(new Vector3(ix, 1.78f, hd + 0.03f), new Vector3(3.3f, 1.55f, 0.12f), red, 0.03f);
                batch.Box(new Vector3(ix, 1.78f, hd + 0.06f), new Vector3(3.0f, 1.3f, 0.1f), MeshKit.Glow(new Color("ffcf87"), 0.9f), 0.02f);
                for (int i = 0; i < 9; i++)
                    batch.Box(new Vector3(ix + (i - 4) * 0.5f, 2.95f, hd + 0.72f), new Vector3(0.5f, 0.07f, 1.6f), i % 2 == 0 ? red : cream, 0.02f, new Vector3(22f, 0f, 0f));
                AddLight(frame * new Vector3(ix, 2.4f, hd + 1.0f), HouseBuilder.LanternLight, 8f, 0.5f, 1.8f);
                // The landmark of the street: read from anywhere.
                Fade(AddLabel("POST OFFICE", frame * new Vector3(0f, top + rise + 1.2f, 0f), 160, Colors.White), 0f, 0f, 1);
                batch.Frame = Transform3D.Identity;
            }

            // The Intake: a counter at waist height with its sign above; together they fill its aim box.
            var wood = MeshKit.Mat(new Color("8a5a3c"), 0.7f);
            var intake = new Vector3(po.IntakeX, 0f, po.IntakeZ);
            batch.Frame = new Transform3D(new Basis(Vector3.Up, intakeYaw), intake);
            batch.Box(new Vector3(0f, 0.48f, 0f), new Vector3(2.9f, 0.96f, 1.6f), wood, 0.05f);
            batch.Box(new Vector3(0f, 1.0f, 0f), new Vector3(3.1f, 0.1f, 1.8f), MeshKit.Mat(Palette.Intake, 0.4f), 0.04f);
            var parcel = MeshKit.Mat(new Color("c89a62"), 0.8f);
            batch.Box(new Vector3(0.9f, 1.23f, 0.35f), new Vector3(0.6f, 0.36f, 0.5f), parcel, 0.04f, new Vector3(0f, 18f, 0f));
            batch.Box(new Vector3(-0.95f, 1.15f, 0.4f), new Vector3(0.34f, 0.2f, 0.42f), MeshKit.Mat(new Color("fff8e7"), 0.7f), 0.03f, new Vector3(0f, -12f, 0f));
            batch.Frame = Transform3D.Identity;
            // The outfeed (doc 04 §2.6): a steel chute out of the counter's north end down to the
            // tile a belt is fed on, and a yellow arrow painted there the way the belt has to run.
            {
                const float T = SimConstants.TileSize;
                int itx = (int)Mathf.Floor(po.IntakeX / T), itz = (int)Mathf.Floor(po.IntakeZ / T);
                float ox = (itx + 0.5f) * T, oz = (itz - 0.5f) * T;
                var steel = MeshKit.Mat(new Color("8d939b"), 0.45f, 0.6f);
                var hazard = MeshKit.Mat(new Color("e0a526"), 0.6f);
                float gy = map.HeightAt(ox, oz);
                batch.Frame = Transform3D.Identity;
                batch.Box(new Vector3(ox, gy + 0.62f, itz * T + 0.05f), new Vector3(1.1f, 0.5f, 0.5f), steel, 0.04f, new Vector3(-25f, 0f, 0f));
                batch.Box(new Vector3(ox, gy + 0.9f, itz * T + 0.15f), new Vector3(1.2f, 0.12f, 0.3f), hazard, 0.02f);
                for (int arm = -1; arm <= 1; arm += 2)
                    batch.Box(new Vector3(ox + arm * 0.22f, gy + 0.015f, oz + 0.2f), new Vector3(0.12f, 0.02f, 0.75f), hazard, 0.01f, new Vector3(0f, arm * 40f, 0f));
                batch.Box(new Vector3(ox, gy + 0.015f, oz + 0.25f), new Vector3(0.12f, 0.02f, 0.9f), hazard, 0.01f);
            }

            // Out from the wall, so the billboard does not turn into it.
            var sign = AddLabel("INTAKE", intake + new Basis(Vector3.Up, intakeYaw) * new Vector3(0f, 1.7f, 0.7f), 72, Colors.White);
            sign.Name = "Intake";
            Fade(sign, 30f, 42f, 1);

            if (po.HasShop)
            {
                // The shop: a shorter counter along the same wall with its goods laid out; it fills its aim box too.
                var shop = new Vector3(po.ShopX, 0f, po.ShopZ);
                batch.Frame = new Transform3D(new Basis(Vector3.Up, intakeYaw), shop);
                batch.Box(new Vector3(0f, 0.48f, 0f), new Vector3(1.7f, 0.96f, 1.5f), wood, 0.05f);
                batch.Box(new Vector3(0f, 1.0f, 0f), new Vector3(1.86f, 0.1f, 1.66f), MeshKit.Mat(ShopTop, 0.4f), 0.04f);
                // Bandages, a bundle of planks, an oil can.
                batch.Box(new Vector3(-0.55f, 1.16f, 0.4f), new Vector3(0.36f, 0.22f, 0.3f), MeshKit.Mat(new Color("fff8e7"), 0.7f), 0.03f, new Vector3(0f, 14f, 0f));
                batch.Box(new Vector3(-0.55f, 1.275f, 0.4f), new Vector3(0.2f, 0.02f, 0.07f), red, 0f, new Vector3(0f, 14f, 0f));
                batch.Box(new Vector3(-0.55f, 1.275f, 0.4f), new Vector3(0.07f, 0.02f, 0.2f), red, 0f, new Vector3(0f, 14f, 0f));
                for (int i = 0; i < 3; i++)
                    batch.Box(new Vector3(0.1f, 1.09f + i * 0.075f, 0.2f), new Vector3(0.24f, 0.07f, 1.0f), MeshKit.Mat(new Color("c79a5e").Darkened(i * 0.06f), 0.8f), 0.01f, new Vector3(0f, -8f + i * 5f, 0f));
                batch.Add(MeshKit.Cylinder(0.11f, 0.13f, 0.3f, 14), MeshKit.Mat(new Color("4d6f8a"), 0.35f, 0.6f), new Vector3(0.6f, 1.2f, 0.45f));
                batch.Frame = Transform3D.Identity;
                var shopSign = AddLabel("SHOP", shop + new Basis(Vector3.Up, intakeYaw) * new Vector3(0f, 1.7f, 0.7f), 72, Colors.White);
                shopSign.Name = "Shop";
                Fade(shopSign, 30f, 42f, 1);
            }

            AddChild(MeshKit.Instance(MeshKit.Cylinder(1.4f, 1.4f, 0.05f, 40), MeshKit.Mat(new Color("d9c9a3"), 0.9f), new Vector3(po.SpawnPadX, 0.025f, po.SpawnPadZ), name: "SpawnPad"));
            batch.Add(MeshKit.Cylinder(1.15f, 1.15f, 0.06f, 40), MeshKit.Mat(new Color("b23a34"), 0.8f), new Vector3(po.SpawnPadX, 0.026f, po.SpawnPadZ));
            batch.Add(MeshKit.Cylinder(1.0f, 1.0f, 0.07f, 40), MeshKit.Mat(new Color("d9c9a3"), 0.9f), new Vector3(po.SpawnPadX, 0.027f, po.SpawnPadZ));
        }

        /// <summary>A signpost with the street's name where the street starts.</summary>
        private void AddStreetSign(MeshBatch batch, WorldMap map, StreetDef street)
        {
            var r = street.Rect;
            bool alongX = r.W >= r.H;
            // A fifth of the way along, on the low side's verge, turned across the street so walkers read it.
            float along = (alongX ? r.X + r.W * 0.2f : r.Y + r.H * 0.2f) * Tile + 0.5f, side = (alongX ? r.Y : r.X) * Tile - 1.25f;
            var at = alongX ? new Vector3(along, 0f, side) : new Vector3(side, 0f, along);
            var basis = new Basis(Vector3.Up, alongX ? Mathf.Pi * 0.5f : 0f);
            batch.Frame = new Transform3D(basis, at);
            batch.Box(new Vector3(0f, 1.25f, 0f), new Vector3(0.12f, 2.5f, 0.12f), MeshKit.Mat(new Color("6b4630"), 0.8f), 0.03f);
            batch.Box(new Vector3(0f, 2.2f, 0f), new Vector3(1.9f, 0.42f, 0.07f), MeshKit.Mat(new Color("2f4858"), 0.6f), 0.04f);
            batch.Frame = Transform3D.Identity;
            for (int face = -1; face <= 1; face += 2)
            {
                AddChild(new Label3D
                {
                    Name = $"StreetSign{street.Id}{(face < 0 ? "a" : "b")}",
                    Text = street.Name,
                    FontSize = 64,
                    PixelSize = 0.004f,
                    OutlineSize = 0,
                    Modulate = new Color("f6f0e4"),
                    DoubleSided = false,
                    Transform = new Transform3D(basis * new Basis(Vector3.Up, face < 0 ? Mathf.Pi : 0f), at + new Vector3(0f, 2.2f, 0f) + basis * new Vector3(0f, 0f, face * 0.045f)),
                });
            }
        }

        /// <summary>From how far a label is read (nit 15) and which one stays where two would cover each other.</summary>
        private static void Fade(Label3D label, float start, float end, int rank)
        {
            if (label is not SizedLabel3D sized) return;
            sized.FadeStart = start;
            sized.FadeEnd = end;
            sized.Rank = rank;
        }

        private Label3D AddLabel(string text, Vector3 position, int fontSize, Color colour, bool flat = false)
        {
            var label = MakeLabel(text, position, fontSize, colour, flat);
            AddChild(label);
            return label;
        }

        private static Label3D MakeLabel(string text, Vector3 position, int fontSize, Color colour, bool flat)
        {
            // A flat label lies on the ground, readable from above; the others face the camera and
            // are capped on screen so that walking up to one does not fill the view with it.
            var label = flat ? new Label3D() : new SizedLabel3D();
            label.Text = text;
            label.Position = position;
            label.FontSize = fontSize;
            label.PixelSize = 0.01f;
            label.OutlineSize = fontSize / 6;
            label.Modulate = colour;
            label.Billboard = flat ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled;
            if (flat) label.RotationDegrees = new Vector3(-90f, 0f, 0f);
            return label;
        }
    }
}
