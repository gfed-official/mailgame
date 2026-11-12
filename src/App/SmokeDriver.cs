using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.App
{
    /// <summary>
    /// Headless smoke (run with "-- --smoke"): a DeliveryBot plays the real client inside the Godot
    /// host until it has delivered and misdelivered mail, then the driver checks spawn → pickup →
    /// deliver → wallet on both ends and on the screen. Prints "SMOKE OK" and exits 0, or
    /// "SMOKE FAIL: …" and exits 1.
    /// </summary>
    public partial class SmokeDriver : Node
    {
        private enum Phase { Deliver, RealTime, Done }

        private const int TicksPerFrame = 3;
        private const int BootFrames = 3;
        private const int TimeoutTicks = 5400;
        private const int WantDelivered = 3;
        private const int WantMisdelivered = 1;
        private const double RealTimeSeconds = 1.0;

        private GameSession _session = null!;
        private GameScreen _screen = null!;
        private DeliveryBot _bot = null!;
        private Action _botTick = null!;
        private Phase _phase = Phase.Deliver;

        /// <summary>With a window (not headless), PNGs of the boot view and each prompt are saved here.</summary>
        public string? ShotDir;

        private readonly List<string> _failures = new List<string>();
        private readonly HashSet<string> _shots = new HashSet<string>();
        private int _startWallet, _paid, _penalties, _deliveredEvents, _misdeliveredEvents, _mailArrived;
        private bool _sawIntakePrompt, _sawMailboxPrompt, _sawManifest, _sawCarriedMail, _sawMatchMark, _sawMismatchMark;
        private float _spawnDistance, _maxEyeOffset, _maxAimOffset;
        private string _missingGlyphs = "";
        private int _frames;
        private uint _startTick, _realTimeStartTick;
        private double _realTime;

        public void Setup(GameSession session, GameScreen screen)
        {
            _session = session;
            _screen = screen;
            var client = session.Client;
            var world = client.World!;

            var pad = world.Map.PostOffice;
            _spawnDistance = new Vector2(world.Local.X - pad.SpawnPadX, world.Local.Z - pad.SpawnPadZ).Length();
            _startWallet = session.Server!.World.Economy.Wallet;
            _startTick = session.Server.World.Tick;
            CheckBootView();

            client.EventReceived += OnEvent;
            _bot = new DeliveryBot(client) { MisdeliverEvery = 4 };
            // The bot waits a few frames, so the boot view is drawn (and can be pictured) untouched.
            _botTick = () => { if (_frames >= BootFrames) _bot.Tick(); };
            client.TickStarting += _botTick;
            session.FixedTicksPerFrame = TicksPerFrame;
        }

        private void OnEvent(SimEvent ev)
        {
            switch (ev)
            {
                case DeliveredEvent e: _paid += e.Paid; _deliveredEvents++; break;
                case MisdeliveredEvent e: _penalties += e.Penalty; _misdeliveredEvents++; break;
                case MailSpawnedEvent e: _mailArrived += e.Count; break;
            }
        }

        /// <summary>
        /// The first frame must be the player's own view of the street: the camera at the eyes,
        /// in the open, looking where the map says a fresh spawn looks.
        /// </summary>
        private void CheckBootView()
        {
            var world = _session.Client.World!;
            var map = world.Map;
            var camera = _screen.Camera;
            var eye = FirstPersonCamera.EyePosition(new Vector3(world.Local.X, world.Local.Y, world.Local.Z));
            float off = camera.GlobalPosition.DistanceTo(eye);
            Check(off <= 0.01f, $"at boot the camera is {off:0.00} m from the player's eyes");
            int tx = Mathf.FloorToInt(camera.GlobalPosition.X / SimConstants.TileSize), tz = Mathf.FloorToInt(camera.GlobalPosition.Z / SimConstants.TileSize);
            Check(!map.IsSolid(tx, tz), $"at boot the camera is inside a wall (tile {tx},{tz})");

            var forward = -camera.GlobalTransform.Basis.Z;
            float yaw = map.PostOffice.SpawnYaw;
            var want = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
            Check(forward.Dot(want) >= 0.99f, $"at boot the camera looks along {forward}, the map's spawn view is {want}");
            // The view must be clear for a few metres: no wall right in front of the lens.
            for (float d = 1f; d <= 8f; d += 1f)
            {
                int sx = Mathf.FloorToInt((eye.X + want.X * d) / SimConstants.TileSize), sz = Mathf.FloorToInt((eye.Z + want.Z * d) / SimConstants.TileSize);
                if (!map.IsSolid(sx, sz)) continue;
                _failures.Add($"at boot a wall blocks the view {d:0} m ahead (tile {sx},{sz})");
                break;
            }
        }

        /// <summary>Every frame the camera must be the local player's eyes: same place, same aim as the interact ray.</summary>
        private void TrackCamera()
        {
            var view = _screen.Players.LocalView;
            if (view == null) return;
            var camera = _screen.Camera;
            var input = _session.Client.Input;
            _maxEyeOffset = Mathf.Max(_maxEyeOffset, camera.GlobalPosition.DistanceTo(FirstPersonCamera.EyePosition(view.GlobalPosition)));
            var ray = new ViewRay(0f, 0f, 0f, input.YawRadians, input.PitchRadians);
            var forward = -camera.GlobalTransform.Basis.Z;
            _maxAimOffset = Mathf.Max(_maxAimOffset, forward.DistanceTo(new Vector3(ray.DirX, ray.DirY, ray.DirZ)));
        }

        private void Shot(string name)
        {
            if (ShotDir == null || DisplayServer.GetName() == "headless" || !_shots.Add(name)) return;
            // Once this frame is drawn, so the picture shows exactly the state that asked for it.
            Action? save = null;
            save = () =>
            {
                RenderingServer.FramePostDraw -= save;
                GetViewport().GetTexture().GetImage().SavePng($"{ShotDir}/{name}.png");
            };
            RenderingServer.FramePostDraw += save;
        }

        public override void _Process(double delta)
        {
            if (_phase == Phase.Done) return;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;

            if (_phase == Phase.Deliver)
            {
                // The bot sends its requests itself; the controller only computes the prompt the HUD shows.
                _session.Interaction.Update(delta, false, false);
                _screen.Hud.Refresh();
                var prompt = _session.Interaction.Prompt;
                bool shown = _screen.Hud.PromptText.Length > 0;
                if (shown && (prompt.Kind == InteractKind.OpenIntake || prompt.Kind == InteractKind.CloseIntake)) _sawIntakePrompt = true;
                if (shown && prompt.Kind == InteractKind.Deliver) _sawMailboxPrompt = true;
                if (_screen.Hud.PromptMark == PromptTone.Match && prompt.Kind == InteractKind.Deliver && prompt.AddressMatches) _sawMatchMark = true;
                if (_screen.Hud.PromptMark == PromptTone.Mismatch && prompt.Kind == InteractKind.Deliver && !prompt.AddressMatches) _sawMismatchMark = true;
                if (_screen.Inventory.Visible && _screen.Inventory.ManifestRows > 0) _sawManifest = true;
                if (world.Hotbar.EntryCount + world.Inventory.EntryCount > 0) _sawCarriedMail = true;
                if (_frames > 0) TrackCamera();
                foreach (char c in FontCheck.MissingGlyphs(_screen.Hud) + FontCheck.MissingGlyphs(_screen.Inventory))
                    if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;

                if (_frames == 1) Shot("boot");
                if (_sawIntakePrompt) Shot("intake");
                if (_sawManifest) Shot("manifest");
                if (_sawMatchMark) Shot("match");
                if (_sawMismatchMark) Shot("mismatch");
                _frames++;

                if (_bot.StacksDelivered >= WantDelivered && _bot.StacksMisdelivered >= WantMisdelivered && client.PendingRequestCount == 0)
                {
                    client.TickStarting -= _botTick;
                    client.Input.MoveX = 0f;
                    client.Input.MoveZ = 0f;
                    client.Input.Buttons = 0;
                    // Back to the frame clock: the same loop a player gets.
                    _session.FixedTicksPerFrame = 0;
                    _realTimeStartTick = server.Tick;
                    _phase = Phase.RealTime;
                }
                else if (server.Tick - _startTick > TimeoutTicks)
                {
                    _failures.Add($"timed out after {TimeoutTicks} ticks: delivered {_bot.StacksDelivered}/{WantDelivered}, misdelivered {_bot.StacksMisdelivered}/{WantMisdelivered}, "
                        + $"rejected {_bot.RequestsRejected} (last {_bot.LastRejection}), mail arrived {_mailArrived}");
                    Finish();
                }
                return;
            }

            _realTime += delta;
            if (_realTime < RealTimeSeconds) return;
            Verify();
            Finish();
        }

        private void Verify()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var global = world.Global;
            _screen.Hud.Refresh();

            Check(_spawnDistance <= 3f, $"spawned {_spawnDistance:0.0} m from the spawn pad");
            Check(_mailArrived > 0, "no mail arrived at the Intake");
            Check(_sawIntakePrompt, "the Intake prompt never showed");
            Check(_sawManifest, "the Intake manifest never listed mail");
            Check(_sawCarriedMail, "no mail was ever carried");
            Check(_sawMailboxPrompt, "the mailbox prompt never showed");
            Check(_deliveredEvents > 0 && _paid > 0, "no paid delivery event reached the client");
            Check(_misdeliveredEvents > 0 && _penalties > 0, "no misdelivery penalty event reached the client");

            int expected = _startWallet + _paid - _penalties;
            Check(server.Economy.Wallet == expected, $"server wallet {server.Economy.Wallet}, events add up to {expected}");
            Check(global.Wallet == server.Economy.Wallet, $"client wallet {global.Wallet}, server wallet {server.Economy.Wallet}");
            Check(global.ShiftEarnings == server.Economy.ShiftEarnings, $"client earnings {global.ShiftEarnings}, server {server.Economy.ShiftEarnings}");
            Check(_screen.Hud.WalletText == HudText.Wallet(global), $"HUD shows \"{_screen.Hud.WalletText}\", expected \"{HudText.Wallet(global)}\"");
            Check(_screen.Hud.QuotaText == HudText.Quota(global), $"HUD shows \"{_screen.Hud.QuotaText}\", expected \"{HudText.Quota(global)}\"");
            Check(_screen.Hud.PhaseText.Contains("DELIVERY", StringComparison.Ordinal), $"HUD phase line is \"{_screen.Hud.PhaseText}\"");

            Check(client.ContainerDesyncs == 0, $"{client.ContainerDesyncs} container desyncs");
            Check(client.BadPackets == 0, $"{client.BadPackets} bad packets");
            Check(server.Tick - _realTimeStartTick >= 10, $"server advanced only {server.Tick - _realTimeStartTick} ticks in {_realTime:0.00} s of real time");

            Check(_screen.World.MailboxCount == world.Map.Destinations.Count, $"{_screen.World.MailboxCount} mailboxes drawn for {world.Map.Destinations.Count} destinations");
            Check(_screen.Players.ViewCount == world.Players.Count, $"{_screen.Players.ViewCount} player views for {world.Players.Count} players");
            Check(GetViewport().GetCamera3D() == _screen.Camera, "the first-person camera is not the active camera");
            var view = _screen.Players.LocalView;
            var player = server.GetPlayer(world.LocalId);
            if (view == null || player == null) _failures.Add("no local player view or server player");
            else
            {
                // First person: the camera sits at the player's eye position, and the player's body is not drawn.
                var eye = FirstPersonCamera.EyePosition(new Vector3(player.Move.X, player.Move.Y, player.Move.Z));
                float off = _screen.Camera.GlobalPosition.DistanceTo(eye);
                Check(off <= 0.05f, $"the camera is {off:0.00} m from the server player's eye position {eye}");
                Check(Mathf.IsEqualApprox(_screen.Camera.GlobalPosition.Y - player.Move.Y, SimConstants.EyeHeight), $"the camera is {_screen.Camera.GlobalPosition.Y - player.Move.Y:0.00} m above the feet, eye height is {SimConstants.EyeHeight}");
                Check(_maxEyeOffset <= 0.001f, $"the camera strayed {_maxEyeOffset:0.000} m from the local player's eyes");
                Check(_maxAimOffset <= 0.001f, $"the camera's aim strayed {_maxAimOffset:0.000} from the interact ray");
                Check(view.Bodiless && view.GetChildCount() == 0, "the local player's body is drawn in first person");
            }

            Check(_sawMatchMark, "the HUD never drew the match tick for a stack at its own mailbox");
            Check(_sawMismatchMark, "the HUD never drew the mismatch cross for a stack at a wrong mailbox");
            Check(_missingGlyphs.Length == 0, $"the UI font has no glyph for: {_missingGlyphs}");
            var screenRect = GetViewport().GetVisibleRect().Grow(-8f);
            foreach (var control in _screen.Hud.TopRow)
            {
                var rect = control.GetGlobalRect();
                Check(screenRect.Encloses(rect), $"HUD {control.Name} at {rect} is not fully inside the screen {screenRect}");
                var min = control.GetCombinedMinimumSize();
                Check(rect.Size.Y >= min.Y && rect.Size.X >= min.X, $"HUD {control.Name} is {rect.Size}, its text needs {min}");
            }

            GD.Print($"[smoke] delivered {_bot.StacksDelivered} stacks ({_deliveredEvents} items, +{HudText.Money(_paid)}), "
                + $"misdelivered {_bot.StacksMisdelivered} ({_misdeliveredEvents} items, -{HudText.Money(_penalties)}), "
                + $"rejected {_bot.RequestsRejected}; {_screen.Hud.WalletText}; {_screen.Hud.QuotaText}; "
                + $"{server.Tick - _startTick} ticks, prediction rms {client.PredictionErrorRms:0.000} m");
        }

        private void Check(bool ok, string failure)
        {
            if (!ok) _failures.Add(failure);
        }

        private void Finish()
        {
            _phase = Phase.Done;
            if (_failures.Count == 0) GD.Print("SMOKE OK");
            else
                foreach (string failure in _failures) GD.PrintErr("SMOKE FAIL: ", failure);
            GetTree().Quit(_failures.Count == 0 ? 0 : 1);
        }
    }
}
