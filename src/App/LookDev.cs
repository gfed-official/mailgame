using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;

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

        /// <summary>The host's world, where the cast is spawned; null for a guest, who gets no cast.</summary>
        public SimWorld? Server;

        private readonly List<CharacterModel> _carriers = new List<CharacterModel>();
        private bool _staged;
        private GameScreen _screen = null!;
        private Camera3D _camera = null!;
        private readonly List<View> _views = new List<View>();
        private int _index, _frames, _totalFrames;
        private ulong _startMsec;

        /// <summary>
        /// The people of the video for the pictures named "cast…": every enemy kind and a Mega walking
        /// up the street (the camera keeps ahead of them), and two carriers with the hammer and the
        /// cannon out for the pictures named "carriers…".
        /// </summary>
        private void StageCast()
        {
            var sim = Server;
            if (sim == null) return;
            var defs = sim.Content.Enemies;
            for (int i = 0; i < defs.Count; i++)
            {
                sim.SpawnEnemy(defs[i], 70f + i * 1.8f, 27f + (i % 2) * 1.6f);
                if (defs[i].Id == "swarmling") sim.SpawnEnemy(defs[i], 70.5f + i * 1.8f, 29.5f);
            }
            sim.SpawnEnemy(defs[0], 74f + defs.Count * 1.2f, 28f, mega: true);

            for (int i = 0; i < 2; i++)
            {
                var carrier = Characters.Carrier(Palette.PlayerColour(i + 1));
                carrier.Position = new Vector3(21.4f + i * 1.3f, 0f, 28f + i * 0.4f);
                carrier.Rotation = new Vector3(0f, i == 0 ? 0.3f : -0.3f, 0f);
                var weapon = i == 0 ? WeaponModels.Hammer() : WeaponModels.Cannon();
                weapon.Rotation = new Vector3(i == 0 ? -2.1f : -Mathf.Pi * 0.5f, 0f, 0f);
                carrier.Hand.AddChild(weapon);
                carrier.Aiming = i == 1;
                _screen.AddChild(carrier);
                _carriers.Add(carrier);
            }
        }

        /// <summary>The middle of the living enemies, on the ground.</summary>
        private bool CastCentre(out Vector3 centre)
        {
            centre = Vector3.Zero;
            var sim = Server;
            if (sim == null || sim.Enemies.Count == 0) return false;
            foreach (var e in sim.Enemies) centre += new Vector3(e.X, 0f, e.Z);
            centre /= sim.Enemies.Count;
            return true;
        }

        public void Setup(GameScreen screen)
        {
            _screen = screen;
            screen.DriveAtmosphere = false;
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
            _views.Add(new View("carriers", new Vector3(22.2f, 1.5f, 24.6f), new Vector3(22f, 1.1f, 28f), 0f));
            _views.Add(new View("cast", new Vector3(-6f, 1.7f, 0.8f), new Vector3(0f, 1.2f, 0f), 0f));
            _views.Add(new View("cast_night", new Vector3(-7.5f, 2.2f, -1.5f), new Vector3(0f, 1.3f, 0f), 1f));
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
            bool cast = view.Name.StartsWith("cast", StringComparison.Ordinal);
            if (!_staged && (cast || view.Name.StartsWith("carriers", StringComparison.Ordinal)))
            {
                _staged = true;
                StageCast();
            }
            foreach (var carrier in _carriers) carrier.Animate((float)delta, 0f, windup: carrier == _carriers[0], down: false);
            if (_frames == 0)
            {
                _camera.Current = true;
                _camera.LookAtFromPosition(view.Eye, view.Target, Vector3.Up);
                _screen.World.Atmosphere.TargetNight = view.Night;
                _screen.World.Atmosphere.Snap();
            }
            // A cast view's eye and target are relative to the cast, which is walking.
            if (cast && CastCentre(out var centre)) _camera.LookAtFromPosition(centre + view.Eye, centre + view.Target, Vector3.Up);
            // Enemies are drawn a few ticks in the past: no picture before every one of them is there.
            if (cast && Server != null && _screen.Enemies.ViewCount < Server.Enemies.Count) return;
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
