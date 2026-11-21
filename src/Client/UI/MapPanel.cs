using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
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
        /// <summary>The line under the map, as last drawn; empty while it is closed.</summary>
        public string RouteText { get; private set; } = "";
        public string TitleText { get; private set; } = "";
        /// <summary>Where the map itself was drawn on the screen, in pixels.</summary>
        public Rect2 MapRect { get; private set; }
        /// <summary>The highest the HUD's lines along the bottom reach (the event feed): the panel ends above it.</summary>
        public Func<float>? BottomClear;

        public void Setup(ClientRuntime client)
        {
            _client = client;
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
            return new Rect2(Side, Top, screen.X - Side * 2f, Mathf.Max(120f, bottom - Top));
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
            RouteText = Guide.Line(world, _move);
            TitleText = MapGuide.Title(world);
            QueueRedraw();
        }

        public override void _ExitTree()
        {
            _texture?.Dispose();
            _texture = null;
        }

        /// <summary>The tiles, 4 texels a tile; a district not open by the shift is hatched grey.</summary>
        private ImageTexture Tiles(WorldMap map, int shift)
        {
            int w = map.Cols * TexelsPerTile, h = map.Rows * TexelsPerTile;
            var bytes = new byte[w * h * 4];
            var houseColour = new Dictionary<int, Color>();
            foreach (var d in map.Destinations)
                for (int y = d.Building.Y; y < d.Building.Y + d.Building.H; y++)
                    for (int x = d.Building.X; x < d.Building.X + d.Building.W; x++)
                        houseColour[y * map.Cols + x] = Palette.HouseColour(d.Variant);
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
            var inner = new Rect2(area.Position + new Vector2(14f, 58f), area.Size - new Vector2(28f, 68f));
            float scale = Mathf.Min(inner.Size.X / map.WidthM, inner.Size.Y / map.DepthM);
            var size = new Vector2(map.WidthM, map.DepthM) * scale;
            var origin = inner.Position + (inner.Size - size) * 0.5f;
            MapRect = new Rect2(origin, size);
            Vector2 At(float x, float z) => origin + new Vector2(x, z) * scale;
            DrawTextureRect(_texture, MapRect, false);

            // Street names, once per street: in the middle of its longest stretch.
            var longest = new Dictionary<string, StreetDef>();
            foreach (var s in map.Streets)
                if (!longest.TryGetValue(s.Name, out var l) || Math.Max(s.Rect.W, s.Rect.H) > Math.Max(l.Rect.W, l.Rect.H)) longest[s.Name] = s;
            foreach (var s in longest.Values)
            {
                RoutingGraph.CentreLine(s, out float x0, out float z0, out float x1, out float z1);
                var mid = At((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                bool alongX = s.Rect.W >= s.Rect.H;
                DrawSetTransform(mid, alongX ? 0f : -Mathf.Pi * 0.5f, Vector2.One);
                DrawStringOutline(font, new Vector2(-100f, 4f), s.Name, HorizontalAlignment.Center, 200f, 12, 3, new Color(0f, 0f, 0f, 0.85f));
                DrawString(font, new Vector2(-100f, 4f), s.Name, HorizontalAlignment.Center, 200f, 12, Ink);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            // House numbers where there is room for them; a ring round the houses of the mail carried.
            bool numbers = scale * SimConstants.TileSize >= 8f;
            foreach (var d in map.Destinations)
            {
                var b = At(d.Building.CenterX, d.Building.CenterZ);
                if (Guide.Carried.Contains(d.Address)) DrawArc(b, scale * 3.4f, 0f, Mathf.Tau, 24, Colors.White, 2f);
                if (numbers) DrawString(font, b + new Vector2(-20f, 4f), d.Address.Number.ToString(), HorizontalAlignment.Center, 40f, 10, new Color(0.1f, 0.1f, 0.12f));
            }
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
                var at = At(middle!.Lot.CenterX, middle.Lot.CenterZ);
                string text = $"Opens in shift {district.UnlockShift}";
                DrawStringOutline(font, at + new Vector2(-90f, 5f), text, HorizontalAlignment.Center, 180f, 12, 4, new Color(0f, 0f, 0f, 0.9f));
                DrawString(font, at + new Vector2(-90f, 5f), text, HorizontalAlignment.Center, 180f, 12, new Color(0.88f, 0.88f, 0.88f));
            }
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
