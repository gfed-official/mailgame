using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;

namespace PerformativeMail.App
{
    /// <summary>
    /// "-- --bot": a DeliveryBot plays this process's client in real time, as host or as a guest
    /// (doc 06 §9 bot clients). With several processes it is the soak over real sockets: every few
    /// seconds a "[bot]" line of counters, and with --quit-after=SECONDS a final "BOT OK" or
    /// "BOT FAIL: …" line and exit code.
    /// </summary>
    public partial class BotDriver : Node
    {
        private const double ReportSeconds = 10.0;

        /// <summary>0 = play until the process is stopped.</summary>
        public double QuitAfterSeconds;

        private GameSession _session = null!;
        private DeliveryBot _bot = null!;
        private double _elapsed, _sinceReport;
        private bool _finished;

        /// <param name="waitPlayers">As host, the run starts once this many players are in the Lobby.</param>
        public void Setup(GameSession session, int waitPlayers)
        {
            _session = session;
            _bot = new DeliveryBot(session.Client) { MinPlayersToStart = waitPlayers };
            session.Client.TickStarting += _bot.Tick;
            // Nobody watches a bot: no reason to spin frames faster than the simulation needs.
            Engine.MaxFps = 60;
        }

        public override void _Process(double delta)
        {
            if (_finished) return;
            _elapsed += delta;
            _sinceReport += delta;
            if (_sinceReport >= ReportSeconds)
            {
                _sinceReport = 0;
                GD.Print("[bot] ", Stats());
            }
            if (QuitAfterSeconds > 0 && _elapsed >= QuitAfterSeconds) Finish(null);
        }

        /// <summary>Ends the process with the verdict; <paramref name="problem"/> is a failure from outside (the host was lost).</summary>
        public void Finish(string? problem)
        {
            if (_finished) return;
            _finished = true;
            var client = _session.Client;
            if (problem == null && client.State != ClientState.Joined) problem = $"the client ended {client.State}";
            if (problem == null && client.ContainerDesyncs > 0) problem = $"{client.ContainerDesyncs} container desyncs";
            if (problem == null && client.BadPackets > 0) problem = $"{client.BadPackets} bad packets";
            // A rejected request is no fault here: two bots reach for the same stack and one of them loses.
            if (problem == null && _bot.StacksDelivered == 0) problem = "the bot delivered nothing";
            GD.Print("[bot] ", Stats());
            if (problem == null) GD.Print("BOT OK");
            else GD.PrintErr("BOT FAIL: ", problem);
            GetTree().Quit(problem == null ? 0 : 1);
        }

        private string Stats()
        {
            var client = _session.Client;
            var world = client.World;
            string where = world == null ? client.State.ToString() : $"{HudText.ShiftAndPhase(world)}, {world.Players.Count} players, {HudText.Wallet(world.Global)}";
            string server = _session.Server == null ? "" : $"; server tick {_session.Server.World.Tick}, max {_session.Server.MaxTickMs:0.00} ms";
            return $"{_elapsed:0} s: {where}; delivered {_bot.StacksDelivered} stacks, misdelivered {_bot.StacksMisdelivered}, rejected {_bot.RequestsRejected}; "
                + $"{client.SnapshotsReceived} snapshots, {client.ContainerVersionsVerified} container versions verified, {client.ContainerDesyncs} desyncs, "
                + $"{client.BadPackets} bad packets, prediction rms {client.PredictionErrorRms * 100.0:0.0} cm (max {client.PredictionErrorMax * 100.0:0.0}){server}";
        }
    }
}
