using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Client.Meta;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// Headless smoke (run with "-- --smoke"): a DeliveryBot plays the real client inside the Godot
    /// host through one whole run. Lobby → Prep → Delivery, where the driver checks spawn → pickup →
    /// deliver → wallet on both ends and on the screen; then the rest of the run fast-forwarded,
    /// checking every Payday summary and the Results panel, and back to the Lobby. Prints
    /// "SMOKE OK" and exits 0, or "SMOKE FAIL: …" and exits 1.
    /// </summary>
    public partial class SmokeDriver : Node
    {
        private enum Stage { Lobby, Prep, Deliver, RealTime, RunOut, BackToLobby, Done }

        private const int TicksPerFrame = 3;
        /// <summary>Once the close-up checks are done the remaining shifts run at one second per frame.</summary>
        private const int RunOutTicksPerFrame = 30;
        private const int BootFrames = 3;
        private const int TimeoutTicks = 5400;
        /// <summary>Longer than five full shifts with their Prep and Payday.</summary>
        private const int RunTimeoutTicks = 70000;
        private const int WantDelivered = 3;
        private const int WantMisdelivered = 1;
        private const double RealTimeSeconds = 1.0;

        private GameSession _session = null!;
        private GameScreen _screen = null!;
        private DeliveryBot _bot = null!;
        private Action _botTick = null!;
        private Stage _stage = Stage.Lobby;
        private int _stageFrames, _goCooldown;
        private uint _runOutStartTick;
        private int _checkedSummaryShift;
        private bool _checkedResults;
        private RunResults? _results;
        private readonly List<RunPhase> _phases = new List<RunPhase>();
        private readonly List<ShiftSummary> _summaries = new List<ShiftSummary>();

        /// <summary>With a window (not headless), PNGs of the boot view and each prompt are saved here.</summary>
        public string? ShotDir;

        private readonly List<string> _failures = new List<string>();
        private readonly HashSet<string> _shots = new HashSet<string>();
        private int _startWallet, _paid, _penalties, _deliveredEvents, _misdeliveredEvents, _mailArrived;
        private bool _sawIntakePrompt, _sawMailboxPrompt, _sawManifest, _sawCarriedMail, _sawMatchMark, _sawMismatchMark;
        private float _spawnDistance, _maxEyeOffset, _maxAimOffset, _maxLabelOver;
        private string _missingGlyphs = "", _biggestLabel = "";
        private long _labels;
        private int _startXp, _startRuns, _startDeliveries, _ownDeliveries;
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
            CheckBootView();

            var profile = session.Profile.Profile;
            (_startXp, _startRuns, _startDeliveries) = (profile.RankXp, profile.Runs, profile.Deliveries);
            client.EventReceived += OnEvent;
            // The driver presses "go" itself, so each phase stays on screen long enough to be checked.
            _bot = new DeliveryBot(client) { MisdeliverEvery = 4, AutoRun = false };
            // The bot waits a few frames, so the boot view is drawn (and can be pictured) untouched.
            _botTick = () => { if (_frames >= BootFrames) _bot.Tick(); };
            client.TickStarting += _botTick;
            session.FixedTicksPerFrame = TicksPerFrame;
            // Checks run once everything else has moved this frame (the camera, the labels that fit to it).
            ProcessPriority = 1000;
        }

        private void OnEvent(SimEvent ev)
        {
            switch (ev)
            {
                case DeliveredEvent e:
                    _paid += e.Paid;
                    _deliveredEvents++;
                    if (e.Player == _session.Client.World!.LocalId) _ownDeliveries++;
                    break;
                case MisdeliveredEvent e: _penalties += e.Penalty; _misdeliveredEvents++; break;
                case MailSpawnedEvent e: _mailArrived += e.Count; break;
                case PhaseChangedEvent e: _phases.Add(e.Phase); break;
                case ShiftSummaryEvent e: _summaries.Add(e.Summary); break;
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
            // World labels (house numbers, signs) are capped on screen however close the player stands.
            foreach (var node in GetTree().GetNodesInGroup(SizedLabel3D.Group))
            {
                if (node is not SizedLabel3D label) continue;
                _labels++;
                float over = label.ScreenFraction(camera) / label.MaxScreenFraction;
                if (over <= _maxLabelOver) continue;
                _maxLabelOver = over;
                _biggestLabel = label.Text;
            }
        }

        private void Shot(string name)
        {
            if (ShotDir == null || DisplayServer.GetName() == "headless" || !_shots.Add(name)) return;
            if (_shots.Count == 1) DirAccess.MakeDirRecursiveAbsolute(ShotDir);
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
            if (_stage == Stage.Done) return;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            if (_goCooldown > 0) _goCooldown--;

            switch (_stage)
            {
                case Stage.Lobby:
                    // The game boots into the Lobby: nothing runs until the host presses "go".
                    hud.Refresh();
                    if (_frames > 0) TrackCamera();
                    if (_frames == 1) Shot("boot");
                    if (_frames == BootFrames)
                    {
                        Check(server.Run.Phase == RunPhase.Lobby && world.Global.Phase == RunPhase.Lobby, $"booted into {server.Run.Phase}, not the Lobby");
                        Check(world.IsHost, "the hosting player is not the host");
                        Check(hud.PhaseText == "LOBBY", $"in the Lobby the HUD phase line is \"{hud.PhaseText}\"");
                        Check(hud.HintText.Contains($"Press {HudText.GoKey} to start", StringComparison.Ordinal), $"in the Lobby the HUD hint is \"{hud.HintText}\"");
                        Check(hud.QuotaText.Length == 0, $"in the Lobby the HUD shows a quota: \"{hud.QuotaText}\"");
                        Check(_mailArrived == 0 && server.Intake.EntryCount == 0, "mail arrived before the run started");
                        Go();
                    }
                    _frames++;
                    if (world.Global.Phase == RunPhase.Prep) Enter(Stage.Prep);
                    else if (_frames > BootFrames + 60) Fail("the run did not leave the Lobby after the host pressed go");
                    break;

                case Stage.Prep:
                    hud.Refresh();
                    TrackCamera();
                    if (_stageFrames == 2)
                    {
                        Check(hud.PhaseText.Contains("PREP", StringComparison.Ordinal), $"in Prep the HUD phase line is \"{hud.PhaseText}\"");
                        Check(hud.QuotaText == HudText.Quota(world.Global) && hud.QuotaText.StartsWith("Quota $", StringComparison.Ordinal), $"in Prep the HUD quota line is \"{hud.QuotaText}\"");
                        Check(hud.HintText.Contains("ready", StringComparison.Ordinal), $"in Prep the HUD hint is \"{hud.HintText}\"");
                        Check(server.Intake.EntryCount == 0, "mail arrived during Prep");
                        Shot("prep");
                    }
                    // Ready ends Prep early (doc 01 §1).
                    if (_stageFrames >= 2 && !world.LocalReady) Go();
                    _stageFrames++;
                    if (world.Global.Phase == RunPhase.Delivery)
                    {
                        _startWallet = server.Economy.Wallet;
                        _startTick = server.Tick;
                        Enter(Stage.Deliver);
                    }
                    else if (_stageFrames > 80) Fail("Prep did not end after the only player readied up");
                    break;

                case Stage.Deliver:
                    StepDeliver(delta);
                    break;

                case Stage.RealTime:
                    _realTime += delta;
                    if (_realTime < RealTimeSeconds) break;
                    Verify();
                    if (_failures.Count > 0) { Finish(); break; }
                    // The rest of the run, fast: the bot delivers flat out, the driver presses "go".
                    _bot.MisdeliverEvery = 0;
                    client.TickStarting += _botTick;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    _runOutStartTick = server.Tick;
                    Enter(Stage.RunOut);
                    break;

                case Stage.RunOut:
                    StepRunOut();
                    break;

                case Stage.BackToLobby:
                    hud.Refresh();
                    _stageFrames++;
                    if (world.Global.Phase != RunPhase.Lobby)
                    {
                        if (_stageFrames > 60) Fail("the run did not return to the Lobby after the host continued");
                        break;
                    }
                    if (world.Global.Wallet != 0 && _stageFrames <= 60) break; // the global block follows within a few ticks
                    VerifyRun();
                    Finish();
                    break;
            }
        }

        private void Enter(Stage stage)
        {
            _stage = stage;
            _stageFrames = 0;
        }

        private void Fail(string failure)
        {
            _failures.Add(failure);
            Finish();
        }

        /// <summary>The "go" key (start / ready / continue), at most once every few frames so the answer can arrive.</summary>
        private void Go()
        {
            if (_goCooldown > 0 || _session.Client.PendingRequestCount > 0) return;
            if (_session.Client.PressGo() != 0) _goCooldown = 6;
        }

        private void StepDeliver(double delta)
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;

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
            TrackCamera();
            foreach (char c in FontCheck.MissingGlyphs(_screen.Hud) + FontCheck.MissingGlyphs(_screen.Inventory))
                if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;

            if (_sawIntakePrompt) Shot("intake");
            if (_sawManifest) Shot("manifest");
            if (_sawMatchMark) Shot("match");
            if (_sawMismatchMark) Shot("mismatch");

            if (_bot.StacksDelivered >= WantDelivered && _bot.StacksMisdelivered >= WantMisdelivered && client.PendingRequestCount == 0)
            {
                client.TickStarting -= _botTick;
                client.Input.MoveX = 0f;
                client.Input.MoveZ = 0f;
                client.Input.Buttons = 0;
                // Back to the frame clock: the same loop a player gets.
                _session.FixedTicksPerFrame = 0;
                _realTimeStartTick = server.Tick;
                Enter(Stage.RealTime);
            }
            else if (server.Tick - _startTick > TimeoutTicks)
            {
                Fail($"timed out after {TimeoutTicks} ticks: delivered {_bot.StacksDelivered}/{WantDelivered}, misdelivered {_bot.StacksMisdelivered}/{WantMisdelivered}, "
                    + $"rejected {_bot.RequestsRejected} (last {_bot.LastRejection}), mail arrived {_mailArrived}");
            }
        }

        /// <summary>
        /// Delivery → Payday → next Prep … → Results. At each Payday and at Results the panel on
        /// screen must show exactly what the server decided.
        /// </summary>
        private void StepRunOut()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            hud.Refresh();
            foreach (char c in FontCheck.MissingGlyphs(hud))
                if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;

            switch (world.Global.Phase)
            {
                case RunPhase.Prep:
                    if (!world.LocalReady) Go();
                    break;

                case RunPhase.Payday:
                {
                    var summary = world.LastSummary;
                    if (summary == null || summary.Shift == _checkedSummaryShift) break;
                    _checkedSummaryShift = summary.Shift;
                    var truth = server.Run.LastSummary;
                    Check(truth != null && truth.Shift == summary.Shift && truth.Earned == summary.Earned && truth.Quota == summary.Quota
                        && truth.QuotaMet == summary.QuotaMet && truth.Bonus == summary.Bonus && truth.Deliveries == summary.Deliveries,
                        $"the client's summary of shift {summary.Shift} differs from the server's");
                    Check(summary.QuotaMet == (summary.Earned >= summary.Quota), $"shift {summary.Shift}: earned {summary.Earned} of {summary.Quota} but QuotaMet is {summary.QuotaMet}");
                    string want = string.Join("\n", HudText.SummaryLines(world, summary));
                    Check(hud.Overlay.Visible && hud.OverlayText == want, $"at Payday of shift {summary.Shift} the panel shows \"{hud.OverlayText}\", expected \"{want}\"");
                    Check(hud.PhaseText.Contains("PAYDAY", StringComparison.Ordinal), $"at Payday the HUD phase line is \"{hud.PhaseText}\"");
                    Check(hud.HintText.Contains(summary.QuotaMet ? "uota met" : "Quota missed", StringComparison.Ordinal), $"at Payday the HUD hint is \"{hud.HintText}\"");
                    Shot($"payday{summary.Shift}");
                    break;
                }

                case RunPhase.Results:
                {
                    var results = world.Results;
                    if (results == null) break;
                    if (!_checkedResults)
                    {
                        _checkedResults = true;
                        _results = results;
                        var truth = server.Run.Results;
                        Check(truth != null && truth.Outcome == results.Outcome && truth.Score == results.Score && truth.ShiftsCompleted == results.ShiftsCompleted
                            && truth.Deliveries == results.Deliveries && truth.Players.Count == results.Players.Count,
                            "the client's run results differ from the server's");
                        var tracker = _session.Profile;
                        string want = string.Join("\n", HudText.ResultsLines(world, results, tracker.Profile, tracker.LastGain));
                        Check(hud.Overlay.Visible && hud.OverlayText == want, $"at Results the panel shows \"{hud.OverlayText}\", expected \"{want}\"");
                        CheckProfile(world, results);
                        Check(hud.PhaseText.StartsWith("RESULTS", StringComparison.Ordinal), $"at Results the HUD phase line is \"{hud.PhaseText}\"");
                        Check(hud.HintText.Contains($"Press {HudText.GoKey}", StringComparison.Ordinal), $"at Results the HUD hint is \"{hud.HintText}\"");
                        Shot("results");
                        // A slower clock again, so the picture is taken before the host leaves the screen.
                        _session.FixedTicksPerFrame = TicksPerFrame;
                        _stageFrames = 0;
                    }
                    else if (++_stageFrames >= 3)
                    {
                        Go();
                        if (_goCooldown > 0)
                        {
                            client.TickStarting -= _botTick;
                            Enter(Stage.BackToLobby);
                        }
                    }
                    break;
                }
            }

            if (_stage == Stage.RunOut && server.Tick - _runOutStartTick > RunTimeoutTicks)
                Fail($"the run did not reach Results within {RunTimeoutTicks} ticks (shift {server.Run.Shift}, {server.Run.Phase}; saw {string.Join(", ", _phases)})");
        }

        /// <summary>At Results the run's XP is in the profile, on the HUD and in the profile's store (doc 07 §7.1).</summary>
        private void CheckProfile(ClientWorld world, RunResults results)
        {
            var tracker = _session.Profile;
            var profile = tracker.Profile;
            var hud = _screen.Hud;
            int xp = -1;
            foreach (var p in results.Players)
                if (p.Player == world.LocalId) xp = p.Xp;
            Check(xp > 0, $"the results give the local player {xp} XP");
            Check(tracker.LastGain != null && tracker.LastGain.Value.Xp == xp, "the profile did not take the run's XP");
            Check(profile.RankXp == _startXp + xp, $"the profile has {profile.RankXp} XP, expected {_startXp} + {xp}");
            Check(profile.Runs == _startRuns + 1, $"the profile counts {profile.Runs} runs, expected {_startRuns + 1}");
            Check(profile.Deliveries == _startDeliveries + _ownDeliveries, $"the profile counts {profile.Deliveries} deliveries, expected {_startDeliveries} + {_ownDeliveries}");
            Check(profile.RecentRuns.Count > 0 && profile.RecentRuns[0].Score == results.Score && profile.RecentRuns[0].ShiftsCompleted == results.ShiftsCompleted,
                "the newest recent run in the profile is not this run");
            int perRank = HudText.RankXpPerRank(world.Content);
            Check(hud.RankText == HudText.Rank(profile, perRank), $"at Results the HUD rank line is \"{hud.RankText}\", expected \"{HudText.Rank(profile, perRank)}\"");
            Check(hud.OverlayText.Contains($"Postal Rank {profile.Rank(perRank)}", StringComparison.Ordinal), "the results panel does not show the Postal Rank");

            Check(tracker.SaveProblem == null, $"saving the profile failed: {tracker.SaveProblem}");
            // One save per Payday and one at Results, and what is stored reads back as the same profile.
            string? stored = tracker.Store.Read();
            if (stored == null) _failures.Add("no profile was stored at Results");
            else
            {
                var read = MetaProfile.FromJson(stored);
                Check(read.ProfileId == profile.ProfileId && read.RankXp == profile.RankXp && read.Runs == profile.Runs && read.Deliveries == profile.Deliveries
                    && read.RecentRuns.Count == profile.RecentRuns.Count, "the stored profile differs from the one in memory");
            }
            GD.Print($"[smoke] profile: +{xp} XP, {HudText.Rank(profile, perRank)}, {profile.Runs} runs, {profile.Deliveries} deliveries");
        }

        /// <summary>After Results → Lobby: the whole run as the client saw it.</summary>
        private void VerifyRun()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            hud.Refresh();

            string seen = string.Join(", ", _phases);
            Check(_phases.Count >= 5 && _phases[0] == RunPhase.Prep && _phases[1] == RunPhase.Delivery && _phases[2] == RunPhase.Payday
                && _phases[_phases.Count - 2] == RunPhase.Results && _phases[_phases.Count - 1] == RunPhase.Lobby,
                $"the phases the client saw were: {seen}");
            Check(_summaries.Count >= 1 && _results != null, $"{_summaries.Count} shift summaries and {(_results == null ? "no" : "the")} run results reached the client");
            if (_results != null)
            {
                int met = 0, earned = 0;
                foreach (var s in _summaries) { if (s.QuotaMet) met++; earned += s.Earned + s.Bonus; }
                Check(_results.ShiftsCompleted == met, $"results say {_results.ShiftsCompleted} shifts completed, the summaries say {met}");
                Check(_results.TotalEarned == earned, $"results say {_results.TotalEarned} earned, the summaries add up to {earned}");
                var wantOutcome = met == HudText.ShiftCount(world.Content) ? RunOutcome.Victory : RunOutcome.QuotaMissed;
                Check(_results.Outcome == wantOutcome, $"the run ended as {_results.Outcome} after {met} of {_summaries.Count} quotas");
            }

            Check(server.Run.Phase == RunPhase.Lobby && server.Run.RunNumber == 1, $"the server is in {server.Run.Phase} of run {server.Run.RunNumber}");
            Check(hud.PhaseText == "LOBBY", $"back in the Lobby the HUD phase line is \"{hud.PhaseText}\"");
            Check(!hud.Overlay.Visible, "the results panel is still shown in the Lobby");
            string rank = HudText.Rank(_session.Profile.Profile, HudText.RankXpPerRank(world.Content));
            Check(hud.RankText == rank, $"back in the Lobby the HUD rank line is \"{hud.RankText}\", expected \"{rank}\"");
            Check(_session.Profile.LastGain == null, "the last run's XP gain is still pending in the Lobby");
            Check(world.Global.Wallet == 0 && server.Economy.Wallet == 0, $"a new run starts with wallet {world.Global.Wallet} (server {server.Economy.Wallet})");
            Check(world.Hotbar.EntryCount + world.Inventory.EntryCount == 0, "mail from the finished run is still carried in the Lobby");
            Check(client.ContainerDesyncs == 0, $"{client.ContainerDesyncs} container desyncs");
            Check(client.BadPackets == 0, $"{client.BadPackets} bad packets");
            Check(_bot.RequestsRejected == 0, $"{_bot.RequestsRejected} bot requests rejected (last {_bot.LastRejection})");
            Check(_missingGlyphs.Length == 0, $"the UI font has no glyph for: {_missingGlyphs}");

            var book = new List<string>();
            foreach (var s in _summaries) book.Add($"{HudText.Money(s.Earned)}/{HudText.Money(s.Quota)}");
            GD.Print($"[smoke] run: {seen}; shifts {string.Join(" ", book)}; {(_results == null ? "no results" : $"{_results.Outcome}, score {HudText.Money(_results.Score)}")}; {server.Tick} ticks");
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
            Check(_labels > 0, "no world labels were checked");
            Check(_maxLabelOver <= 1.001f, $"the world label \"{_biggestLabel}\" grew to {_maxLabelOver:0.00} times its screen cap");

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
            _stage = Stage.Done;
            if (_failures.Count == 0) GD.Print("SMOKE OK");
            else
                foreach (string failure in _failures) GD.PrintErr("SMOKE FAIL: ", failure);
            GetTree().Quit(_failures.Count == 0 ? 0 : 1);
        }
    }
}
