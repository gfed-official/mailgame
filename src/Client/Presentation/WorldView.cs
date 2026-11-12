using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Static world dressing built from the WorldMap (doc 07 §5): ground tiles, houses, mailboxes,
    /// the Post Office and its Intake. Greybox meshes; nothing here is read by the simulation.
    /// </summary>
    public partial class WorldView : Node3D
    {
        private const float Tile = SimConstants.TileSize;
        private const float HouseHeight = 3f;
        private const float PostOfficeHeight = 4.5f;

        public int MailboxCount { get; private set; }

        public void Build(WorldMap map)
        {
            AddSky();
            AddGround(map);
            foreach (var street in map.Streets)
                AddLabel(street.Name, new Vector3(street.Rect.CenterX, 0.05f, street.Rect.CenterZ), 96, Colors.White, flat: true);
            foreach (var dest in map.Destinations)
            {
                AddHouse(dest);
                AddMailbox(dest);
            }
            AddPostOffice(map.PostOffice);
        }

        private void AddSky()
        {
            var environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.8f,
                TonemapMode = Environment.ToneMapper.Filmic,
            };
            AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });
            AddChild(new DirectionalLight3D
            {
                Name = "Sun",
                RotationDegrees = new Vector3(-52f, -35f, 0f),
                ShadowEnabled = true,
                LightEnergy = 1.1f,
            });
        }

        private void AddGround(WorldMap map)
        {
            var byColour = new Dictionary<Color, List<Vector3>>();
            for (int tz = 0; tz < map.Rows; tz++)
            {
                for (int tx = 0; tx < map.Cols; tx++)
                {
                    var colour = Palette.Tile(map.GetTile(tx, tz));
                    if (!byColour.TryGetValue(colour, out var list)) byColour[colour] = list = new List<Vector3>();
                    list.Add(new Vector3((tx + 0.5f) * Tile, 0f, (tz + 0.5f) * Tile));
                }
            }
            foreach (var (colour, positions) in byColour)
            {
                var multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = new PlaneMesh { Size = new Vector2(Tile, Tile), Material = Palette.Flat(colour) },
                    InstanceCount = positions.Count,
                };
                for (int i = 0; i < positions.Count; i++)
                    multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, positions[i]));
                AddChild(new MultiMeshInstance3D { Name = $"Ground{colour.ToHtml(false)}", Multimesh = multimesh });
            }
        }

        private void AddHouse(DestinationDef dest)
        {
            var colour = Palette.HouseColour(dest.Variant);
            var b = dest.Building;
            var size = new Vector3(b.W * Tile, HouseHeight, b.H * Tile);
            var centre = new Vector3(b.CenterX, HouseHeight * 0.5f, b.CenterZ);
            AddBox($"House{dest.Address.Number}", centre, size, colour);
            AddChild(new MeshInstance3D
            {
                Name = $"Roof{dest.Address.Number}",
                Mesh = new PrismMesh { Size = new Vector3(size.X + 0.6f, 1.6f, size.Z + 0.6f), Material = Palette.Flat(colour.Darkened(0.35f)) },
                Position = new Vector3(centre.X, HouseHeight + 0.8f, centre.Z),
            });
            // The number floats above the wall that faces the mailbox, not above the ridge: up close the
            // label is small (SizedLabel3D), and over the middle of the house the roof would hide it.
            float dx = dest.MailboxX - centre.X, dz = dest.MailboxZ - centre.Z;
            var front = Mathf.Abs(dx) / size.X > Mathf.Abs(dz) / size.Z
                ? new Vector3(Mathf.Sign(dx) * size.X * 0.5f, 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(dz) * size.Z * 0.5f);
            AddLabel(dest.Address.Number.ToString(), new Vector3(centre.X, HouseHeight + 2.4f, centre.Z) + front, 160, colour.Lightened(0.5f));
        }

        private void AddMailbox(DestinationDef dest)
        {
            var colour = Palette.HouseColour(dest.Variant);
            var root = new Node3D { Name = $"Mailbox{dest.Address.Number}", Position = new Vector3(dest.MailboxX, 0f, dest.MailboxZ) };
            AddChild(root);
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.12f, 1.0f, 0.12f), Material = Palette.Flat(new Color("3d3d3d")) },
                Position = new Vector3(0f, 0.5f, 0f),
            });
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.35f, 0.65f), Material = Palette.Flat(colour) },
                Position = new Vector3(0f, 1.15f, 0f),
            });
            root.AddChild(MakeLabel(dest.Label, new Vector3(0f, 1.75f, 0f), 56, Colors.White, flat: false));
            MailboxCount++;
        }

        private void AddPostOffice(PostOfficeDef po)
        {
            foreach (var solid in po.Solid)
            {
                var size = new Vector3(solid.W * Tile, PostOfficeHeight, solid.H * Tile);
                var centre = new Vector3(solid.CenterX, PostOfficeHeight * 0.5f, solid.CenterZ);
                AddBox("PostOffice", centre, size, Palette.PostOffice);
                AddLabel("POST OFFICE", new Vector3(centre.X, PostOfficeHeight + 1.2f, centre.Z), 160, Colors.White);
            }
            // A counter at waist height with its sign above: together they fill the Intake's aim box.
            AddBox("Intake", new Vector3(po.IntakeX, 0.5f, po.IntakeZ), new Vector3(1.6f, 1.0f, 1.6f), Palette.Intake);
            AddLabel("INTAKE", new Vector3(po.IntakeX, 1.6f, po.IntakeZ), 72, Colors.White);
            AddChild(new MeshInstance3D
            {
                Name = "SpawnPad",
                Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 0.04f, Material = Palette.Flat(new Color("e9d8a6")) },
                Position = new Vector3(po.SpawnPadX, 0.02f, po.SpawnPadZ),
            });
        }

        private void AddBox(string name, Vector3 centre, Vector3 size, Color colour) =>
            AddChild(new MeshInstance3D
            {
                Name = name,
                Mesh = new BoxMesh { Size = size, Material = Palette.Flat(colour) },
                Position = centre,
            });

        private void AddLabel(string text, Vector3 position, int fontSize, Color colour, bool flat = false) =>
            AddChild(MakeLabel(text, position, fontSize, colour, flat));

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
