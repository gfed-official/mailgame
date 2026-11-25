using System;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// The debug world's keys (design choice 119), on a map with a <see cref="WorldMap.Debug"/>
    /// block only: F1–F5 and F7–F11 put the free camera at the stations' bookmarks (F6 stays the
    /// hot reload), F12 gives the view back to the carrier, the backquote key frees the camera
    /// where it is or gives it back; free, WASD flies it the way it looks, Space and Ctrl lift and
    /// lower it, Shift speeds it up, and the mouse turns it while the carrier stands still. The
    /// comma, period and slash keys set the time scale to 0.25×, 1× and 4×. Three lines at the top left
    /// say what is on.
    /// </summary>
    public partial class DebugWorldController : Node
    {
        /// <summary>The bookmark keys, station by station.</summary>
        public static readonly Key[] BookmarkKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11 };
        public static readonly double[] Scales = { 0.25, 1.0, 4.0 };
        private const float FlySpeed = 8f, FastFlySpeed = 24f, MouseSensitivity = 0.0035f;

        private GameSession _session = null!;
        private GameScreen _screen = null!;
        private DebugWorldDef _debug = null!;
        private Label _line = null!;

        /// <summary>The station whose bookmark the camera was last put at; −1 for none.</summary>
        public int Station { get; private set; } = -1;
        public bool FreeLook => _screen.Camera.FreeLook;
        /// <summary>The words at the bottom left, as last drawn.</summary>
        public string LineText => _line.Text;

        public void Setup(GameSession session, GameScreen screen, DebugWorldDef debug)
        {
            _session = session;
            _screen = screen;
            _debug = debug;
            var layer = new CanvasLayer { Name = "DebugLine", Layer = 5 };
            AddChild(layer);
            _line = new Label { Name = "Line" };
            _line.AddThemeFontSizeOverride("font_size", 14);
            _line.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.35f));
            _line.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
            _line.AddThemeConstantOverride("outline_size", 4);
            // The top left, clear of the clock in the middle and the health and feed at the bottom.
            _line.OffsetLeft = 28f;
            _line.OffsetRight = 400f;
            _line.OffsetTop = 28f;
            _line.OffsetBottom = 90f;
            layer.AddChild(_line);
            Refresh();
        }

        /// <summary>The free camera at a station's bookmark.</summary>
        public void Bookmark(int station)
        {
            if (station < 0 || station >= _debug.Stations.Count) return;
            var s = _debug.Stations[station];
            _screen.Camera.LookFrom(new Vector3(s.EyeX, s.EyeY, s.EyeZ), new Vector3(s.TargetX, s.TargetY, s.TargetZ));
            Station = station;
            Refresh();
        }

        /// <summary>Frees the camera where it is now, or gives the view back to the carrier.</summary>
        public void ToggleFree()
        {
            var camera = _screen.Camera;
            if (camera.FreeLook) { BackToCarrier(); return; }
            camera.FreePosition = camera.GlobalPosition;
            camera.FreeYaw = camera.Rotation.Y;
            camera.FreePitch = camera.Rotation.X;
            camera.FreeLook = true;
            Refresh();
        }

        public void BackToCarrier()
        {
            _screen.Camera.FreeLook = false;
            Station = -1;
            _screen.Camera.Sync();
            Refresh();
        }

        public void SetScale(double scale)
        {
            _session.TimeScale = scale;
            Refresh();
        }

        /// <summary>The free camera flown for a frame: <paramref name="move"/> is right/forward on the keys' axes, <paramref name="lift"/> up.</summary>
        public void Fly(Vector2 move, float lift, bool fast, double delta)
        {
            var camera = _screen.Camera;
            if (!camera.FreeLook) return;
            var basis = Basis.FromEuler(new Vector3(camera.FreePitch, camera.FreeYaw, 0f));
            var dir = basis * new Vector3(move.X, 0f, move.Y) + Vector3.Up * lift;
            if (dir.LengthSquared() > 1f) dir = dir.Normalized();
            camera.FreePosition += dir * (fast ? FastFlySpeed : FlySpeed) * (float)delta;
            camera.FreePosition = new Vector3(camera.FreePosition.X, Mathf.Max(0.3f, camera.FreePosition.Y), camera.FreePosition.Z);
        }

        public override void _UnhandledInput(InputEvent ev)
        {
            if (ev is InputEventMouseMotion motion && FreeLook && Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
            {
                var camera = _screen.Camera;
                camera.FreeYaw = Mathf.Wrap(camera.FreeYaw - motion.Relative.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
                camera.FreePitch = Mathf.Clamp(camera.FreePitch - motion.Relative.Y * MouseSensitivity, -1.5f, 1.5f);
                return;
            }
            if (ev is not InputEventKey key || !key.Pressed || key.Echo) return;
            if (HandleKey(key.PhysicalKeycode)) GetViewport().SetInputAsHandled();
        }

        /// <summary>One of the debug world's keys; false for any other.</summary>
        public bool HandleKey(Key key)
        {
            int bookmark = Array.IndexOf(BookmarkKeys, key);
            if (bookmark >= 0) { Bookmark(bookmark); return true; }
            switch (key)
            {
                case Key.F12: BackToCarrier(); return true;
                case Key.Quoteleft: ToggleFree(); return true;
                case Key.Comma: SetScale(Scales[0]); return true;
                case Key.Period: SetScale(Scales[1]); return true;
                case Key.Slash: SetScale(Scales[2]); return true;
                default: return false;
            }
        }

        public override void _Process(double delta)
        {
            if (FreeLook && !_screen.Mouse.PanelOpen)
            {
                var move = Godot.Input.GetVector("move_left", "move_right", "move_forward", "move_back");
                float lift = (Godot.Input.IsActionPressed("jump") ? 1f : 0f) - (Godot.Input.IsKeyPressed(Key.Ctrl) ? 1f : 0f);
                // Real time: the free camera flies at its own pace whatever the time scale.
                Fly(move, lift, Godot.Input.IsActionPressed("sprint"), delta);
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_line == null) return;
            _line.Text = Line(_debug, FreeLook, Station, _session.TimeScale);
        }

        /// <summary>"DEBUG WORLD · god mode · unlimited · clock stopped · 1x · free camera at Address Sorter (F3) …".</summary>
        public static string Line(DebugWorldDef debug, bool free, int station, double scale)
        {
            string toggles = (debug.GodMode ? " · god mode" : "") + (debug.Unlimited ? " · unlimited" : "") + (debug.NoTimer ? " · clock stopped" : "");
            string where = !free ? "F1-F11 stations · ` free camera"
                : station >= 0 ? $"{BookmarkKeys[station]}: {debug.Stations[station].Name} · F12 back"
                : "free camera · WASD Space Ctrl Shift · F12 back";
            return $"DEBUG WORLD{toggles}\ntime {scale:0.##}x (, . /)\n{where}";
        }
    }
}
