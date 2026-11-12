using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Sim;

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
        private const int TimeoutTicks = 5400;
        private const int WantDelivered = 3;
        private const int WantMisdelivered = 1;
        private const double RealTimeSeconds = 1.0;

        private GameSession _session = null!;
        private GameScreen _screen = null!;
        private DeliveryBot _bot = null!;
        private Action _botTick = null!;
        private Phase _phase = Phase.Deliver;

        private readonly List<string> _failures = new List<string>();
        private int _startWallet, _paid, _penalties, _deliveredEvents, _misdeliveredEvents, _mailArrived;
        private bool _sawIntakePrompt, _sawMailboxPrompt, _sawManifest, _sawCarriedMail;
        private float _spawnDistance;
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

            client.EventReceived += OnEvent;
            _bot = new DeliveryBot(client) { MisdeliverEvery = 4 };
            _botTick = _bot.Tick;
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
                if (_screen.Inventory.Visible && _screen.Inventory.ManifestRows > 0) _sawManifest = true;
                if (world.Hotbar.EntryCount + world.Inventory.EntryCount > 0) _sawCarriedMail = true;

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
            Check(GetViewport().GetCamera3D() == _screen.Camera.Camera, "the rig camera is not the active camera");
            var view = _screen.Players.LocalView;
            var player = server.GetPlayer(world.LocalId);
            if (view == null || player == null) _failures.Add("no local player view or server player");
            else
            {
                float off = view.Position.DistanceTo(new Vector3(player.Move.X, player.Move.Y, player.Move.Z));
                Check(off <= 1f, $"the local player view is {off:0.00} m from the server position");
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
