using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace PerformativeMail.App
{
    /// <summary>
    /// Look development ("-- --lookdev=DIR"): pictures of the world from a fixed tour of viewpoints,
    /// by day and by night, to compare against the concept video's frames. Its own camera; the game
    /// underneath is untouched. Quits when the tour is done.
    /// </summary>
    public partial class LookDev : Node
    {
        /// <summary>Frames a view is held before its picture is taken, so shadows and the sky settle.</summary>
        private const int SettleFrames = 6;

        private readonly struct View
        {
            public readonly string Name;
            public readonly Vector3 Eye, Target;
            public readonly float Night;
            public View(string name, Vector3 eye, Vector3 target, float night) { Name = name; Eye = eye; Target = target; Night = night; }
        }

        public string Dir = "";
        /// <summary>Extra views: "name:ex,ey,ez:tx,ty,tz[:night]" separated by ';'.</summary>
        public string? Extra;

        private GameScreen _screen = null!;
        private Camera3D _camera = null!;
        private readonly List<View> _views = new List<View>();
        private int _index, _frames, _totalFrames;
        private ulong _startMsec;

        public void Setup(GameScreen screen)
        {
            _screen = screen;
            _camera = new Camera3D { Name = "LookDevCamera", Fov = 75f, Near = 0.05f, Far = 4000f };
            AddChild(_camera);
            _camera.Current = true;
            screen.Hud.Visible = false;
            DirAccess.MakeDirRecursiveAbsolute(Dir);

            if (Extra != null)
            {
                foreach (string spec in Extra.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = spec.Split(':');
                    if (parts.Length < 3 || !Parse(parts[1], out var eye) || !Parse(parts[2], out var target)) continue;
                    float night = parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0f;
                    _views.Add(new View(parts[0], eye, target, night));
                }
                return;
            }
            _views.Add(new View("street", new Vector3(17f, 1.6f, 28.5f), new Vector3(40f, 2.2f, 27f), 0f));
            _views.Add(new View("house", new Vector3(27f, 1.6f, 28.5f), new Vector3(23f, 2.6f, 20f), 0f));
            _views.Add(new View("south", new Vector3(44f, 1.6f, 27f), new Vector3(52f, 2.4f, 36f), 0f));
            _views.Add(new View("office", new Vector3(19f, 1.6f, 22f), new Vector3(11f, 2.4f, 17.5f), 0f));
            _views.Add(new View("west", new Vector3(30f, 1.6f, 28f), new Vector3(0f, 1.4f, 28f), 0f));
            _views.Add(new View("gate", new Vector3(78f, 1.6f, 28f), new Vector3(93f, 2.4f, 28f), 0f));
            _views.Add(new View("grass", new Vector3(46f, 1.6f, 46f), new Vector3(30f, 1.2f, 40f), 0f));
            _views.Add(new View("harbour", new Vector3(-4f, 9f, 6f), new Vector3(-40f, -2f, 36f), 0f));
            _views.Add(new View("above", new Vector3(46f, 60f, 100f), new Vector3(46f, 0f, 26f), 0f));
            _views.Add(new View("night_street", new Vector3(17f, 1.6f, 28.5f), new Vector3(40f, 2.2f, 27f), 1f));
            _views.Add(new View("night_gate", new Vector3(74f, 1.6f, 28f), new Vector3(93f, 3.2f, 28f), 1f));
        }

        private static bool Parse(string text, out Vector3 v)
        {
            v = default;
            string[] parts = text.Split(',');
            return parts.Length == 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v.X)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v.Y)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v.Z);
        }

        public override void _Process(double delta)
        {
            _totalFrames++;
            if (_index >= _views.Count)
            {
                if (_frames++ != 2) return;
                GD.Print($"[lookdev] {_views.Count} pictures in {Dir}; {(Time.GetTicksMsec() - _startMsec) / (double)_totalFrames:0} ms a frame");
                GetTree().Quit(0);
                return;
            }
            if (_totalFrames == 1) _startMsec = Time.GetTicksMsec();
            var view = _views[_index];
            if (_frames == 0)
            {
                _camera.Current = true;
                _camera.LookAtFromPosition(view.Eye, view.Target, Vector3.Up);
                _screen.World.Atmosphere.TargetNight = view.Night;
                _screen.World.Atmosphere.Snap();
            }
            if (_frames++ < SettleFrames) return;
            _frames = 0;
            _index++;
            string path = $"{Dir}/{view.Name}.png";
            Action? save = null;
            save = () =>
            {
                RenderingServer.FramePostDraw -= save;
                GetViewport().GetTexture().GetImage().SavePng(path);
            };
            RenderingServer.FramePostDraw += save;
        }
    }
}
