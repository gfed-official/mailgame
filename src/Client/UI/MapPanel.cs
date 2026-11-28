using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The map (doc 09 §2.6, key M): the town from above, drawn from the tile grid — streets,
    /// lots in their district's colour, houses in their own, the Post Office, the beach — with
    /// the street names, the house numbers, the districts not open yet hatched with the shift
    /// they open in, the carriers, and the route from you to the house of the mail in your hand
    /// along the routing graph (doc 02 §3.8), how far and how long. An overlay: the clock and
    /// the HUD's top stay in view, the mouse stays the view's, the walk goes on under it.
    /// </summary>
    public partial class MapPanel : Control
    {
        private const int TexelsPerTile = 4;
        private const float Top = 132f, Side = 60f;
        private static readonly Color Back = new Color(0.07f, 0.1f, 0.14f, 0.92f);
        private static readonly Color Ink = new Color(0.96f, 0.94f, 0.88f);
        private static readonly Color Route = new Color("ffbf47");
        private static readonly Color Sand = new Color("d9c08a");
        private static readonly Color Shallow = new Color("5fa6b8"), Deep = new Color("2f6a86");
        private static readonly Color Raiders = new Color("e63946");
        private static readonly Color[] Districts =
        {
            new Color("e9c46a"), new Color("8ecae6"), new Color("f4a261"), new Color("b5e48c"), new Color("cdb4db"),
        };

        public static Color DistrictColour(int district) => Districts[Math.Max(0, district - 1) % Districts.Length];

        private ClientRuntime? _client;
        private MoveConstants _move;
        private ImageTexture? _texture;
        private WorldMap? _textureMap;
        private int _textureShift = -1;

        public MapGuide Guide { get; } = new MapGuide();
        /// <summary>The layers and the filter chips (doc 09 §2.6): what is drawn over the town.</summary>
        public MapLayers Layers { get; } = new MapLayers();
        /// <summary>The route editor (doc 09 §2.6): live while a depot's or a port's console is up.</summary>
        public RouteEditor Editor { get; private set; } = null!;
        /// <summary>True while the map is up over a console: clicks set its stops.</summary>
        public bool Editing => Visible && Editor.Active;
        /// <summary>The district labels a click makes a stop, where they were drawn.</summary>
        public IReadOnlyList<(int District, Rect2 Rect)> DistrictLabels => _districtLabels;
        private readonly List<(int District, Rect2 Rect)> _districtLabels = new List<(int, Rect2)>();
        /// <summary>The edited route's stop markers as drawn: the stop and where.</summary>
        public IReadOnlyList<(int Stop, Vector2 At)> StopMarkers => _stopMarkers;
        private readonly List<(int Stop, Vector2 At)> _stopMarkers = new List<(int, Vector2)>();
        private Vector2 _origin;
        private float _scale = 1f;
        private int _dragging = -1;
        /// <summary>What the last draw put on the map, layer by layer.</summary>
        public int BeltsDrawn { get; private set; }
        public int MachinesDrawn { get; private set; }
        public int VehiclesDrawn { get; private set; }
        public int CrewsDrawn { get; private set; }
        public int NodesDrawn { get; private set; }
        public int MailHousesDrawn { get; private set; }
        public string ChipsText { get; private set; } = "";
        /// <summary>The chips on the screen, in pixels, Mail first.</summary>
        public Rect2[] ChipRects { get; } = new Rect2[3];
        /// <summary>The line under the map, as last drawn; empty while it is closed.</summary>
        public string RouteText { get; private set; } = "";
        public string TitleText { get; private set; } = "";
        /// <summary>The raiders' ways in marked at the last draw, and whether the raid comes by each.</summary>
        public IReadOnlyList<(SpawnEdge Edge, bool Raid)> EdgesDrawn => _edges;
        private List<(SpawnEdge Edge, bool Raid)> _edges = new List<(SpawnEdge, bool)>();
        /// <summary>How many ferry lanes the map drew the last time it was drawn.</summary>
        public int FerriesDrawn { get; private set; }
        /// <summary>Where the map itself was drawn on the screen, in pixels.</summary>
        public Rect2 MapRect { get; private set; }
        /// <summary>The highest the HUD's lines along the bottom reach (the event feed): the panel ends above it.</summary>
        public Func<float>? BottomClear;
        /// <summary>The lowest the HUD's lines along the top reach (the hint under the plate): the panel starts under it (the e5be644 playtest's N7).</summary>
        public Func<float>? TopClear;

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Editor = new RouteEditor(client);
            _move = MoveConstants.From(client.World!.Content);
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        public void Toggle() => Visible = !Visible;

        /// <summary>The panel on the screen, in pixels: world labels keep off it while it is up (its back lets the view through).</summary>
        public Rect2 PanelArea()
        {
            var screen = Size;
            float bottom = Mathf.Min(screen.Y - 120f, (BottomClear?.Invoke() ?? screen.Y - 120f) - 8f);
            float top = Mathf.Max(Top, (TopClear?.Invoke() ?? 0f) + 6f);
            return new Rect2(Side, top, screen.X - Side * 2f, Mathf.Max(120f, bottom - top));
        }

        public override void _Process(double delta)
        {
            if (!Visible || _client?.World == null)
            {
                RouteText = TitleText = "";
                return;
            }
            var world = _client.World;
            Guide.Update(world, _client.Input.HeldSlot);
            RouteText = Guide.Target == null && HudText.MapForecast(world) is { Length: > 0 } forecast ? forecast : Guide.Line(world, _move);
            TitleText = MapGuide.Title(world);
            MouseFilter = Editing ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            Layers.UpdateEdited(world, Editing ? Editor.Console : null);
            if (Editing)
            {
                TitleText = HudText.RouteEditorTitle(Editor.Console!);
                RouteText = Editor.Line();
            }
            else _dragging = -1;
            _edges = MapGuide.Edges(world);
            Layers.Update(world, delta);
            ChipsText = Layers.ChipsText();
            QueueRedraw();
        }

        public override void _ExitTree()
        {
            _texture?.Dispose();
            _texture = null;
        }

        private static readonly Color ChipOn = new Color("ffbf47"), ChipOff = new Color(0.55f, 0.58f, 0.62f);
        private static readonly Color BeltCol = new Color("e9a23b"), FastCol = new Color("4ea3ff"), RaisedCol = new Color("ffd77a"), PipeCol = new Color("cfd8dc");
        private static readonly Color CrewCol = new Color("2ec4b6"), CaptainCol = new Color("7fdcff"), MailCol = new Color("ffe066");

        /// <summary>The chips (doc 09 §2.6 "Filter chips: Mail, Routes, Resources"): pills with their key, lit while on.</summary>
        private void DrawChips(Font font, Vector2 at)
        {
            float x = at.X;
            for (int i = 0; i < MapLayers.ChipNames.Length; i++)
            {
                bool on = Layers.On((MapChip)i);
                string text = $"{MapLayers.ChipKeys[i]}  {MapLayers.ChipNames[i]}";
                float w = font.GetStringSize(text, HorizontalAlignment.Left, -1f, 13).X + 22f;
                var rect = new Rect2(x, at.Y, w, 20f);
                ChipRects[i] = rect;
                DrawRect(rect, on ? new Color(ChipOn, 0.9f) : new Color(0.2f, 0.23f, 0.27f, 0.9f));
                DrawRect(rect, on ? ChipOn : ChipOff, false, 1.5f);
                DrawString(font, new Vector2(x + 11f, at.Y + 15f), text, HorizontalAlignment.Left, -1f, 13, on ? new Color(0.08f, 0.08f, 0.1f) : ChipOff);
                x += w + 8f;
            }
        }

        /// <summary>
        /// The layers over the tiles: resources (chip), constructs with the belts and pipes as lines
        /// the way they run, the crews' routes (chip), the houses with mail waiting (chip), the
        /// vehicles and the raiders an Alarm Post reveals.
        /// </summary>
        private void DrawLayers(ClientWorld world, float scale, Func<float, float, Vector2> At)
        {
            const float tile = SimConstants.TileSize;
            NodesDrawn = 0;
            foreach (var (x, z, node) in Layers.Nodes)
            {
                DrawCircle(At(x, z), Mathf.Max(2f, scale * 0.6f), ResourceColour(node.Resource));
                NodesDrawn++;
            }

            MachinesDrawn = 0;
            foreach (var c in Layers.Machines)
            {
                var colour = MachineColour(c.Def);
                for (int i = 0; i < Math.Max(1, c.Def.TileCount); i++)
                {
                    var (tx, tz) = c.TileAt(i);
                    var r = new Rect2(At(tx * tile, tz * tile) + new Vector2(1f, 1f), new Vector2(tile * scale - 2f, tile * scale - 2f));
                    DrawRect(r, colour);
                }
                MachinesDrawn++;
            }

            BeltsDrawn = 0;
            float width = Mathf.Clamp(scale * 0.8f, 2f, 5f);
            foreach (var b in Layers.Belts)
            {
                var from = At(b.X0, b.Z0);
                var to = At(b.X1, b.Z1);
                var colour = b.Kind switch { MapBeltKind.Fast => FastCol, MapBeltKind.Raised => RaisedCol, MapBeltKind.Pipe => PipeCol, _ => BeltCol };
                DrawLine(from, to, colour, b.Kind == MapBeltKind.Pipe ? width * 0.6f : width);
                if (b.Kind != MapBeltKind.Pipe && from.DistanceTo(to) > 6f)
                {
                    // A flow arrow in the middle of the tile.
                    var dir = (to - from).Normalized();
                    var across = new Vector2(-dir.Y, dir.X);
                    var mid = (from + to) * 0.5f + dir * 2f;
                    float s = Mathf.Clamp(scale * 0.7f, 3f, 6f);
                    DrawColoredPolygon(new[] { mid + dir * s, mid - dir * s + across * s, mid - dir * s - across * s }, new Color(0.1f, 0.08f, 0.06f, 0.9f));
                }
                BeltsDrawn++;
            }

            CrewsDrawn = 0;
            foreach (var crew in Layers.Crews)
            {
                if (crew.Points.Count < 2) continue;
                var colour = crew.Captain ? CaptainCol : CrewCol;
                for (int i = 0; i + 1 < crew.Points.Count; i++)
                    DrawDashedLine(At(crew.Points[i].X, crew.Points[i].Z), At(crew.Points[i + 1].X, crew.Points[i + 1].Z), colour, 2.5f, 7f);
                var font = GetThemeDefaultFont();
                for (int i = 0; i < crew.Stops.Count; i++)
                {
                    var stop = crew.Stops[i];
                    var at = At(stop.MailboxX, stop.MailboxZ);
                    DrawCircle(at, 7f, crew.Now == stop ? Colors.White : colour);
                    DrawString(font, at + new Vector2(-6f, 4f), (i + 1).ToString(), HorizontalAlignment.Center, 12f, 10, new Color(0.05f, 0.05f, 0.08f));
                }
                CrewsDrawn++;
            }

            MailHousesDrawn = 0;
            foreach (var (dest, count) in Layers.MailHouses)
            {
                var d = world.Map.GetDestination(dest);
                if (d == null) continue;
                var at = At(d.Building.CenterX, d.Building.CenterZ);
                float r = Mathf.Max(8f, scale * 2.6f);
                DrawArc(at, r, 0f, Mathf.Tau, 28, new Color(MailCol, 0.95f), 3f);
                var font = GetThemeDefaultFont();
                DrawStringOutline(font, at + new Vector2(r * 0.6f, -r * 0.6f), count.ToString(), HorizontalAlignment.Left, -1f, 12, 3, new Color(0f, 0f, 0f, 0.9f));
                DrawString(font, at + new Vector2(r * 0.6f, -r * 0.6f), count.ToString(), HorizontalAlignment.Left, -1f, 12, MailCol);
                MailHousesDrawn++;
            }

            VehiclesDrawn = 0;
            foreach (var v in Layers.Vehicles)
            {
                var at = At(v.X, v.Z);
                var fwd = new Vector2(-Mathf.Sin(v.Heading), -Mathf.Cos(v.Heading));
                var side = new Vector2(-fwd.Y, fwd.X);
                float len = Mathf.Max(5f, Mathf.Max(v.Def.Length, 1.6f) * scale * 0.5f), wide = Mathf.Max(3f, scale * 0.8f);
                var body = new[] { at + fwd * len + side * wide, at + fwd * len - side * wide, at - fwd * len - side * wide, at - fwd * len + side * wide };
                DrawColoredPolygon(body, v.Def.OnWater ? new Color("f4f7fb") : new Color("f2e8cf"));
                DrawPolyline(new[] { body[0], body[1], body[2], body[3], body[0] }, new Color(0.7f, 0.12f, 0.1f), 1.5f, true);
                DrawCircle(at + fwd * len, 2f, new Color(0.7f, 0.12f, 0.1f));
                VehiclesDrawn++;
            }

            foreach (var (x, z) in Layers.Raiders)
            {
                DrawCircle(At(x, z), 5f, Raiders);
                DrawArc(At(x, z), 7f, 0f, Mathf.Tau, 16, new Color(0f, 0f, 0f, 0.8f), 1.5f);
            }
        }

        /// <summary>
        /// The route editor's marks: the console's route as it stands, its stops numbered (a
        /// district's on its label), the district names to click, and the hint over the map's foot.
        /// </summary>
        private void DrawEditor(ClientWorld world, Font font, Func<float, float, Vector2> At)
        {
            _districtLabels.Clear();
            _stopMarkers.Clear();
            if (!Editing) return;
            var console = Editor.Console!;
            var edited = Layers.Edited;
            var colour = console.Def.IsPort ? CaptainCol : CrewCol;
            if (edited != null && edited.Points.Count >= 2)
                for (int i = 0; i + 1 < edited.Points.Count; i++)
                    DrawLine(At(edited.Points[i].X, edited.Points[i].Z), At(edited.Points[i + 1].X, edited.Points[i + 1].Z), colour, 3f, true);
            SimWorld.Bay(console, out float bx, out float bz, out _);
            DrawArc(At(bx, bz), 9f, 0f, Mathf.Tau, 20, Colors.White, 2.5f);

            // The districts' names, to click: each over the middle of its houses.
            foreach (var district in world.Map.Districts)
            {
                if (console.Def.IsPort) break;
                float sx = 0f, sz = 0f;
                int n = 0;
                foreach (var d in world.Map.Destinations)
                    if (d.Address.District == district.Id && !d.IsDock) { sx += d.Lot.CenterX; sz += d.Lot.CenterZ; n++; }
                if (n == 0) continue;
                string name = district.Name.Length > 0 ? district.Name : $"District {district.Id}";
                var size = font.GetStringSize(name, HorizontalAlignment.Left, -1f, 12) + new Vector2(14f, 6f);
                var rect = new Rect2(At(sx / n, sz / n) - size * 0.5f + new Vector2(0f, -18f), size);
                DrawRect(rect, new Color(0.1f, 0.12f, 0.16f, 0.85f));
                DrawRect(rect, colour, false, 1.5f);
                DrawString(font, rect.Position + new Vector2(7f, size.Y - 6f), name, HorizontalAlignment.Left, -1f, 12, Ink);
                _districtLabels.Add((district.Id, rect));
            }

            // The stops, numbered where they are: a house's at its mailbox, a street's on its middle, a district's on its label.
            var stops = Editor.Stops();
            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                Vector2? at = null;
                switch (stop.Kind)
                {
                    case FilterKind.Address when world.Map.GetDestination(stop.Value) is { } d:
                        at = At(d.MailboxX, d.MailboxZ);
                        break;
                    case FilterKind.Street when world.Map.GetStreet(stop.Value) is { } s:
                        at = At(s.Rect.CenterX, s.Rect.CenterZ);
                        break;
                    case FilterKind.District:
                        foreach (var (id, rect) in _districtLabels) if (id == stop.Value) at = rect.Position + new Vector2(-10f, rect.Size.Y * 0.5f);
                        break;
                }
                if (at == null) continue;
                var p = at.Value;
                if (_dragging == i) p = GetLocalMousePosition();
                DrawCircle(p, 10f, colour);
                DrawArc(p, 10f, 0f, Mathf.Tau, 20, new Color(0f, 0f, 0f, 0.8f), 2f);
                DrawString(font, p + new Vector2(-8f, 5f), (i + 1).ToString(), HorizontalAlignment.Center, 16f, 13, new Color(0.05f, 0.05f, 0.08f));
                _stopMarkers.Add((i, at.Value));
            }
            var area = PanelArea();
            DrawString(font, new Vector2(area.Position.X + 14f, area.End.Y - 10f), HudText.RouteEditorHint, HorizontalAlignment.Left, area.Size.X - 28f, 13, new Color(Ink, 0.85f));
        }

        /// <summary>The route editor's clicks (doc 09 §2.6): on a house, a district's name, a stop to drag or take off.</summary>
        public override void _GuiInput(InputEvent ev)
        {
            if (!Editing || ev is not InputEventMouseButton button) return;
            AcceptEvent();
            Click(button.Position, button.ButtonIndex, button.Pressed);
        }

        /// <summary>A press or a release of a mouse button on the map, in the panel's pixels: what the editor does with it.</summary>
        public void Click(Vector2 at, MouseButton button, bool pressed)
        {
            var world = _client?.World;
            if (world == null || !Editing) return;
            int marker = MarkerAt(at);
            if (button == MouseButton.Right)
            {
                if (pressed && marker >= 0) Editor.Remove(marker);
                return;
            }
            if (button != MouseButton.Left) return;
            if (pressed)
            {
                _dragging = marker;
                return;
            }
            int from = _dragging;
            _dragging = -1;
            if (from >= 0)
            {
                if (marker >= 0 && marker != from) Editor.Move(from, marker);
                return;
            }
            foreach (var (district, rect) in _districtLabels)
                if (rect.HasPoint(at)) { Editor.AddDistrict(district); return; }
            var house = HouseAt(world, at);
            if (house != null) Editor.AddHouse(house);
        }

        private int MarkerAt(Vector2 at)
        {
            foreach (var (stop, p) in _stopMarkers)
                if (p.DistanceTo(at) <= 11f) return stop;
            return -1;
        }

        /// <summary>The house whose building or mailbox is under the pointer (within a few pixels).</summary>
        private DestinationDef? HouseAt(ClientWorld world, Vector2 at)
        {
            if (!MapRect.HasPoint(at)) return null;
            float x = (at.X - _origin.X) / _scale, z = (at.Y - _origin.Y) / _scale;
            DestinationDef? best = null;
            float bestD = Mathf.Max(3f, 10f / _scale);
            foreach (var d in world.Map.Destinations)
            {
                var b = d.Building;
                const float tile = SimConstants.TileSize;
                float dx = Mathf.Max(0f, Mathf.Max(b.X * tile - x, x - (b.X + b.W) * tile));
                float dz = Mathf.Max(0f, Mathf.Max(b.Y * tile - z, z - (b.Y + b.H) * tile));
                float dist = Mathf.Min(Mathf.Sqrt(dx * dx + dz * dz), new Vector2(d.MailboxX - x, d.MailboxZ - z).Length());
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        /// <summary>Where a point of the world is on the screen, as last drawn (for the smoke's clicks).</summary>
        public Vector2 ScreenAt(float x, float z) => _origin + new Vector2(x, z) * _scale;

        private static Color MachineColour(Sim.Content.BuildingDef def) => def.Behaviour switch
        {
            Sim.Content.BuildingBehaviour.Wall or Sim.Content.BuildingBehaviour.Gate => new Color("8d8478"),
            Sim.Content.BuildingBehaviour.Container => new Color("a1683a"),
            Sim.Content.BuildingBehaviour.Sorter => new Color("9b7fd1"),
            Sim.Content.BuildingBehaviour.Inserter => new Color("e76f51"),
            Sim.Content.BuildingBehaviour.Pump => new Color("1d1b19"),
            Sim.Content.BuildingBehaviour.Turret => new Color("c0392b"),
            Sim.Content.BuildingBehaviour.VehicleDepot or Sim.Content.BuildingBehaviour.Port => new Color(0.55f, 0.6f, 0.65f, 0.9f),
            _ => new Color("b0a89a"),
        };

        private static Color ResourceColour(string resource) => resource switch
        {
            var r when r.Contains("log", StringComparison.OrdinalIgnoreCase) || r.Contains("wood", StringComparison.OrdinalIgnoreCase) => new Color("8a5a2b"),
            var r when r.Contains("stone", StringComparison.OrdinalIgnoreCase) => new Color("b8b8b8"),
            var r when r.Contains("fib", StringComparison.OrdinalIgnoreCase) => new Color("90be6d"),
            var r when r.Contains("berr", StringComparison.OrdinalIgnoreCase) => new Color("e63946"),
            var r when r.Contains("ore", StringComparison.OrdinalIgnoreCase) || r.Contains("iron", StringComparison.OrdinalIgnoreCase) => new Color("f4a261"),
            var r when r.Contains("sand", StringComparison.OrdinalIgnoreCase) => new Color("e9d8a6"),
            _ => new Color("ffffff"),
        };

        /// <summary>The tiles, 4 texels a tile; a district not open by the shift is hatched grey.</summary>
        private ImageTexture Tiles(WorldMap map, int shift)
        {
            int w = map.Cols * TexelsPerTile, h = map.Rows * TexelsPerTile;
            var bytes = new byte[w * h * 4];
            var houseColour = new Dictionary<int, Color>();
            foreach (var d in map.Destinations)
                for (int y = d.Building.Y; y < d.Building.Y + d.Building.H; y++)
                    for (int x = d.Building.X; x < d.Building.X + d.Building.W; x++)
                        houseColour[y * map.Cols + x] = d.IsDock ? new Color("5d7383") : Palette.HouseColour(d.Variant);
            var po = map.PostOffice;
            for (int ty = 0; ty < map.Rows; ty++)
            {
                for (int tx = 0; tx < map.Cols; tx++)
                {
                    int i = ty * map.Cols + tx;
                    var tile = map.GetTile(tx, ty);
                    int district = map.TileDistrict[i];
                    bool locked = district > 0 && map.Districts.Count > 1 && Unlock(map, district) > shift;
                    Color c = tile switch
                    {
                        TileType.Street => Palette.Street,
                        TileType.Lot => DistrictColour(district).Lerp(Palette.Grass, 0.45f),
                        TileType.Yard => Palette.Yard,
                        TileType.Beach => Sand,
                        TileType.Water => Shallow,
                        TileType.DeepWater => Deep,
                        TileType.Building => houseColour.TryGetValue(i, out var hc) ? hc : Palette.PostOffice,
                        _ => Palette.Grass.Darkened(0.25f),
                    };
                    if (tile == TileType.Building && po.Rect.Contains(tx, ty)) c = Palette.PostOffice;
                    locked &= tile != TileType.Street;
                    if (locked) c = c.Lerp(new Color(0.45f, 0.45f, 0.47f), 0.7f);
                    for (int py = 0; py < TexelsPerTile; py++)
                    {
                        for (int px = 0; px < TexelsPerTile; px++)
                        {
                            var texel = c;
                            // Hatching: a darker diagonal every fourth texel.
                            if (locked && ((tx * TexelsPerTile + px) + (ty * TexelsPerTile + py)) % 6 == 0) texel = texel.Darkened(0.35f);
                            int at = ((ty * TexelsPerTile + py) * w + tx * TexelsPerTile + px) * 4;
                            bytes[at] = (byte)(texel.R * 255f);
                            bytes[at + 1] = (byte)(texel.G * 255f);
                            bytes[at + 2] = (byte)(texel.B * 255f);
                            bytes[at + 3] = 255;
                        }
                    }
                }
            }
            using var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes);
            return ImageTexture.CreateFromImage(image);
        }

        private readonly List<Rect2> _streetLabels = new List<Rect2>(), _openLabels = new List<Rect2>();
        /// <summary>The street names' places, worked out again only when the map or its size changes.</summary>
        private List<StreetLabels.Label> _names = new List<StreetLabels.Label>();
        private WorldMap? _namesMap;
        private Vector2 _namesOrigin;
        private float _namesScale;
        /// <summary>Where the street names were written on the map, last drawn.</summary>
        public IReadOnlyList<Rect2> StreetLabelRects => _streetLabels;
        /// <summary>Where the districts not open yet say when they open, last drawn.</summary>
        public IReadOnlyList<Rect2> OpensLabelRects => _openLabels;
        /// <summary>Where an "Opens in shift N" label may stand round its district's middle house, nearest first.</summary>
        private static readonly Vector2[] LabelOffsets =
        {
            Vector2.Zero, new Vector2(0f, -16f), new Vector2(0f, 16f), new Vector2(0f, -32f), new Vector2(0f, 32f),
            new Vector2(-60f, 0f), new Vector2(60f, 0f), new Vector2(0f, -48f), new Vector2(0f, 48f),
            new Vector2(-60f, -16f), new Vector2(60f, -16f), new Vector2(-60f, 16f), new Vector2(60f, 16f),
            // Further out where a district's middle is crowded with street names (the Large Island's start district).
            new Vector2(0f, -64f), new Vector2(0f, 64f), new Vector2(-60f, 32f), new Vector2(60f, 32f), new Vector2(-60f, -32f),
            new Vector2(60f, -32f), new Vector2(-100f, 0f), new Vector2(100f, 0f), new Vector2(-60f, 48f), new Vector2(60f, 48f),
            new Vector2(-100f, 16f), new Vector2(100f, 16f), new Vector2(-100f, -16f), new Vector2(100f, -16f), new Vector2(0f, 80f),
        };

        /// <summary>How many edges the outline of the district opening next shift has on the map this frame (0 for none).</summary>
        public int IncomingOutlineSegments { get; private set; }
        private WorldMap? _outlineMap;
        private int _outlineShift = -1;
        private readonly List<(Vector2 A, Vector2 B)> _outline = new List<(Vector2, Vector2)>();

        /// <summary>
        /// The edges, in world metres, between the tiles of the district(s) opening at shift <paramref name="shift"/> + 1
        /// (its lots, yards and houses; not its streets) and every other tile. Kept until the map or the shift changes.
        /// </summary>
        private List<(Vector2 A, Vector2 B)> IncomingOutline(WorldMap map, int shift)
        {
            if (ReferenceEquals(map, _outlineMap) && shift == _outlineShift) return _outline;
            _outlineMap = map;
            _outlineShift = shift;
            _outline.Clear();
            if (map.Districts.Count <= 1) return _outline;
            var incoming = new HashSet<int>();
            foreach (var d in map.Districts) if (d.UnlockShift == shift + 1) incoming.Add(d.Id);
            if (incoming.Count == 0) return _outline;
            bool In(int x, int y) => map.InBounds(x, y) && map.GetTile(x, y) != TileType.Street && incoming.Contains(map.TileDistrict[y * map.Cols + x]);
            const float t = SimConstants.TileSize;
            for (int y = 0; y < map.Rows; y++)
                for (int x = 0; x < map.Cols; x++)
                {
                    if (!In(x, y)) continue;
                    if (!In(x, y - 1)) _outline.Add((new Vector2(x * t, y * t), new Vector2((x + 1) * t, y * t)));
                    if (!In(x, y + 1)) _outline.Add((new Vector2(x * t, (y + 1) * t), new Vector2((x + 1) * t, (y + 1) * t)));
                    if (!In(x - 1, y)) _outline.Add((new Vector2(x * t, y * t), new Vector2(x * t, (y + 1) * t)));
                    if (!In(x + 1, y)) _outline.Add((new Vector2((x + 1) * t, y * t), new Vector2((x + 1) * t, (y + 1) * t)));
                }
            return _outline;
        }

        private static int Unlock(WorldMap map, int district)
        {
            foreach (var d in map.Districts) if (d.Id == district) return d.UnlockShift;
            return 1;
        }

        public override void _Draw()
        {
            var world = _client?.World;
            if (world == null) return;
            var map = world.Map;
            int shift = Math.Max(1, (int)world.Global.Shift);
            if (_texture == null || _textureMap != map || _textureShift != shift)
            {
                _texture?.Dispose();
                _texture = Tiles(map, shift);
                _textureMap = map;
                _textureShift = shift;
            }

            var area = PanelArea();
            DrawRect(area, Back);
            var font = GetThemeDefaultFont();
            DrawString(font, area.Position + new Vector2(14f, 24f), TitleText, HorizontalAlignment.Left, area.Size.X - 28f, 17, Ink);
            DrawString(font, area.Position + new Vector2(14f, 46f), RouteText, HorizontalAlignment.Left, area.Size.X - 28f, 15,
                Guide.Target != null ? Route : new Color(Ink, 0.75f));
            DrawChips(font, area.Position + new Vector2(14f, 56f));
            var inner = new Rect2(area.Position + new Vector2(14f, 84f), area.Size - new Vector2(28f, 94f));
            float scale = Mathf.Min(inner.Size.X / map.WidthM, inner.Size.Y / map.DepthM);
            var size = new Vector2(map.WidthM, map.DepthM) * scale;
            var origin = inner.Position + (inner.Size - size) * 0.5f;
            MapRect = new Rect2(origin, size);
            _origin = origin;
            _scale = scale;
            Vector2 At(float x, float z) => origin + new Vector2(x, z) * scale;
            DrawTextureRect(_texture, MapRect, false);
            DrawLayers(world, scale, At);

            // House numbers where there is room for them; a ring round the houses of the mail carried.
            bool numbers = scale * SimConstants.TileSize >= 8f;
            foreach (var d in map.Destinations)
            {
                var b = At(d.Building.CenterX, d.Building.CenterZ);
                if (Guide.Carried.Contains(d.Address)) DrawArc(b, scale * 3.4f, 0f, Mathf.Tau, 24, Colors.White, 2f);
                if (d.IsPoBoxBank)
                {
                    // At its boxes, not on the Post Office's hall (where "PO" is written).
                    DrawString(font, At(d.MailboxX, d.MailboxZ) + new Vector2(-30f, 4f), "BOXES", HorizontalAlignment.Center, 60f, 10, Colors.White);
                    continue;
                }
                if (numbers || d.IsDock || d.IsApartment) DrawString(font, b + new Vector2(-20f, 4f), d.IsDock ? "DOCK" : d.IsApartment ? $"{d.Address.Number} APTS" : d.Address.Number.ToString(), HorizontalAlignment.Center, 40f, 10, d.IsDock || d.IsApartment ? Colors.White : new Color(0.1f, 0.1f, 0.12f));
            }
            // Doc 02 §3.8's ferry lanes: a dashed white line over the inlet, "FERRY" by it.
            foreach (var lane in map.FerryLanes)
            {
                Vector2 a = At(lane.AX, lane.AZ), b = At(lane.BX, lane.BZ);
                DrawDashedLine(a, b, Colors.White, 2.5f, 6f);
                DrawCircle(a, 3.5f, Colors.White);
                DrawCircle(b, 3.5f, Colors.White);
                var label = (a + b) * 0.5f + new Vector2(-40f, -8f);
                DrawStringOutline(font, label, HudText.FerryLabel, HorizontalAlignment.Center, 80f, 11, 3, new Color(0f, 0f, 0f, 0.85f));
                DrawString(font, label, HudText.FerryLabel, HorizontalAlignment.Center, 80f, 11, Colors.White);
            }
            FerriesDrawn = map.FerryLanes.Count;
            // Oil seeps: black drops where a pump may stand.
            foreach (var (sx, sz) in map.OilSeeps)
                DrawCircle(At((sx + 0.5f) * SimConstants.TileSize, (sz + 0.5f) * SimConstants.TileSize), Mathf.Max(3f, scale * 1.2f), new Color(0.06f, 0.05f, 0.04f));
            // The team's pings (doc 09 §2.6): a ring in the ping's colour with who and what beside it.
            if (_client?.World != null)
                foreach (var ping in _client.World.Pings)
                {
                    var at = At(ping.X, ping.Z);
                    var colour = PingsView.Colour(ping.Kind);
                    DrawArc(at, 9f, 0f, Mathf.Tau, 20, colour, 3f);
                    DrawCircle(at, 3.5f, colour);
                    DrawString(font, at + new Vector2(12f, 5f), HudText.PingLabel(_client.World, ping), HorizontalAlignment.Left, 260f, 12, colour.Lightened(0.3f));
                }
            // Doc 01 §2.1 "Preview": the district that opens next shift, outlined in amber round its lots.
            var outline = IncomingOutline(map, shift);
            foreach (var (a, b) in outline) DrawLine(At(a.X, a.Y), At(b.X, b.Y), Palette.Amber, 2.5f);
            IncomingOutlineSegments = outline.Count;
            // Street names over the outline (the ec62063 playtest read "Sl te Str t" through it), once per street: in the middle of its longest stretch, else along it off the names before it.
            _streetLabels.Clear();
            if (_namesMap != map || _namesOrigin != origin || _namesScale != scale)
            {
                _names = StreetLabels.Place(map, origin.X, origin.Y, scale, (name, size) => font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X + 6f);
                _namesMap = map;
                _namesOrigin = origin;
                _namesScale = scale;
            }
            foreach (var label in _names)
            {
                var mid = new Vector2(label.X, label.Y);
                _streetLabels.Add(new Rect2(label.Box.X0, label.Box.Y0, label.Box.X1 - label.Box.X0, label.Box.Y1 - label.Box.Y0));
                DrawSetTransform(mid, label.AlongX ? 0f : -Mathf.Pi * 0.5f, Vector2.One);
                DrawStringOutline(font, new Vector2(-100f, 4f), label.Name, HorizontalAlignment.Center, 200f, label.Size, 3, new Color(0f, 0f, 0f, 0.85f));
                DrawString(font, new Vector2(-100f, 4f), label.Name, HorizontalAlignment.Center, 200f, label.Size, Ink);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            _openLabels.Clear();
            // The districts not open yet say when they open.
            foreach (var district in map.Districts)
            {
                if (map.Districts.Count <= 1 || district.UnlockShift <= shift) continue;
                float sx = 0f, sz = 0f;
                int n = 0;
                foreach (var d in map.Destinations)
                    if (d.Address.District == district.Id) { sx += d.Lot.CenterX; sz += d.Lot.CenterZ; n++; }
                if (n == 0) continue;
                // On the district's own house nearest its middle: a district can lie on both sides of another.
                DestinationDef? middle = null;
                float best = float.MaxValue;
                foreach (var d in map.Destinations)
                {
                    if (d.Address.District != district.Id) continue;
                    float dd = MathF.Abs(d.Lot.CenterX - sx / n) + MathF.Abs(d.Lot.CenterZ - sz / n);
                    if (dd < best) { best = dd; middle = d; }
                }
                var home = At(middle!.Lot.CenterX, middle.Lot.CenterZ);
                bool next = district.UnlockShift == shift + 1;
                string text = next ? "Opens next shift" : $"Opens in shift {district.UnlockShift}";
                // The 24cbd0a playtest's N19: the label lay on street names and on the next district's. It takes the
                // first place near its house that is clear of both.
                float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 12).X + 8f;
                var at = home;
                foreach (var offset in LabelOffsets)
                {
                    var box = new Rect2(home.X + offset.X - width * 0.5f, home.Y + offset.Y - 9f, width, 16f);
                    if (_streetLabels.Exists(r => r.Intersects(box)) || _openLabels.Exists(r => r.Intersects(box))) continue;
                    at = home + offset;
                    break;
                }
                _openLabels.Add(new Rect2(at.X - width * 0.5f, at.Y - 9f, width, 16f));
                DrawStringOutline(font, at + new Vector2(-90f, 5f), text, HorizontalAlignment.Center, 180f, 12, 4, new Color(0f, 0f, 0f, 0.9f));
                DrawString(font, at + new Vector2(-90f, 5f), text, HorizontalAlignment.Center, 180f, 12, next ? Palette.Amber : new Color(0.88f, 0.88f, 0.88f));
            }
            // The raiders' ways in: a red arrowhead pointing into the town; the raid's own bright,
            // with its way to the Post Office.
            foreach (var (edge, raid) in _edges)
            {
                var colour = raid ? Raiders : new Color(Raiders, 0.55f);
                if (raid && edge.Path.Count >= 2)
                {
                    var path = new Vector2[edge.Path.Count];
                    for (int i = 0; i < path.Length; i++) path[i] = At(edge.Path[i].X, edge.Path[i].Z);
                    for (int i = 0; i + 1 < path.Length; i++) DrawDashedLine(path[i], path[i + 1], Raiders, 2f, 6f);
                }
                var tip = At(edge.X, edge.Z) + new Vector2(edge.InX, edge.InZ) * 15f;
                var into = new Vector2(edge.InX, edge.InZ);
                var across = new Vector2(-edge.InZ, edge.InX);
                var head = new[] { tip, tip - into * 14f + across * 9f, tip - into * 9f, tip - into * 14f - across * 9f };
                DrawColoredPolygon(head, colour);
                DrawPolyline(new[] { head[0], head[1], head[2], head[3], head[0] }, new Color(0f, 0f, 0f, 0.8f), 1.5f, true);
                if (raid)
                {
                    var label = tip + into * 8f + new Vector2(-30f, 4f);
                    DrawStringOutline(font, label, "RAID", HorizontalAlignment.Center, 60f, 12, 4, new Color(0f, 0f, 0f, 0.9f));
                    DrawString(font, label, "RAID", HorizontalAlignment.Center, 60f, 12, Raiders);
                }
            }

            DrawEditor(world, font, At);

            var po = map.PostOffice;
            DrawString(font, At(po.Rect.CenterX, po.Rect.CenterZ) + new Vector2(-40f, 4f), "PO", HorizontalAlignment.Center, 80f, 13, Ink);

            // The route, then the house it runs to.
            if (Guide.Points.Count >= 2)
            {
                var line = new Vector2[Guide.Points.Count];
                for (int i = 0; i < line.Length; i++) line[i] = At(Guide.Points[i].X, Guide.Points[i].Z);
                DrawPolyline(line, new Color(0f, 0f, 0f, 0.6f), 6f, true);
                DrawPolyline(line, Route, 3f, true);
            }
            if (Guide.Target != null)
            {
                var t = At(Guide.Target.MailboxX, Guide.Target.MailboxZ);
                DrawCircle(t, 6f, Route);
                DrawArc(t, 10f, 0f, Mathf.Tau, 24, Route, 2f);
            }

            // The carriers: the others as dots with their names, yourself as an arrow the way you look.
            double tick = world.ServerTick - ClientRuntime.InterpolationDelayTicks;
            foreach (var p in world.Players.Values)
            {
                if (p.Id == world.LocalId || !p.Interpolate(tick, out float px, out _, out float pz, out _)) continue;
                var at = At(px, pz);
                DrawCircle(at, 5f, Palette.PlayerColour(p.Slot));
                DrawString(font, at + new Vector2(8f, 4f), p.Name, HorizontalAlignment.Left, -1f, 12, Ink);
            }
            if (world.HasLocal)
            {
                var me = At(world.Local.X, world.Local.Z);
                float yaw = _client!.Input.YawRadians;
                // Yaw 0 looks along −Z, up on the map.
                var forward = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
                var right = new Vector2(-forward.Y, forward.X);
                var arrow = new[] { me + forward * 11f, me - forward * 6f + right * 7f, me - forward * 3f, me - forward * 6f - right * 7f };
                DrawColoredPolygon(arrow, Colors.White);
                DrawPolyline(new[] { arrow[0], arrow[1], arrow[2], arrow[3], arrow[0] }, new Color(0f, 0f, 0f, 0.8f), 1.5f, true);
            }
        }
    }
}
