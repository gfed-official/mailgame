using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// The smoke of the raid playtest (nits 13 and 18), run with
    /// "--smoke --shift=2 --no-quota --raid --playtest-package": the run starts at shift 2, the
    /// night raid is announced as the shift starts and comes five seconds later, the host meets
    /// it on the street through the client's own input — the packages the flag gave them out of
    /// the mail cannon, then the hammer — and the quota the fight left no time for does not end
    /// the run. Every step is read off the real HUD.
    /// </summary>
    public partial class SmokeDriver
    {
        private const PlaytestFlags RaidPlaytest = PlaytestFlags.NoQuota | PlaytestFlags.RaidNow | PlaytestFlags.Packages;
        private const int RaidPlaytestShift = 2;
        /// <summary>From the mailbox side of the yard out onto the street, where the raiders come along.</summary>
        private const float StandX = 20f, StandZ = 28f;
        /// <summary>The hammer is swung at whatever is this near.</summary>
        private const float SwingAt = 3.5f;
        /// <summary>
        /// Raiders with nobody in their aggro range hold their line on the street (nit 17): the host
        /// goes at whoever is farther off than this, which is well inside that range.
        /// </summary>
        private const float AdvanceBeyond = 10f;
        /// <summary>The cannon is for raiders this far off: nearer is the hammer's, farther is past a sure shot.</summary>
        private const float CannonFrom = 5f, CannonTo = 24f;
        private bool _ptLootSeen;
        private const int PlaytestStepFrames = 400, FightFrames = 2400, LootFrames = 300;

        private int _ptStep, _ptFrames, _ptSwings, _ptShots, _ptBlasts, _ptBlastHits, _ptDeaths, _ptProtected;
        private uint _ptDeliveryTick;
        private bool _ptWasDead, _ptSawEnemyView, _ptSawTracker, _ptBadTracker, _ptSawHammer, _ptSawCannon, _ptSawCards, _ptBadBanner, _ptPicked;
        private int _ptMostSeen;
        private readonly List<(float X, float Z)> _ptPath = new List<(float, float)>();
        private int _ptWaypoint;
        private float _ptGoalX, _ptGoalZ;

        private void StartPlaytest()
        {
            _stage = Stage.Playtest;
            // The host is played from here, not by the delivery bot.
            _session.Client.TickStarting -= _botTick;
        }

        private void NextPlaytestStep()
        {
            _ptStep++;
            _ptFrames = 0;
        }

        private void StopHost()
        {
            var client = _session.Client;
            client.Input.MoveX = 0f;
            client.Input.MoveZ = 0f;
            client.Input.Buttons = InputButtons.None;
        }

        /// <summary>Walks the host along the tile path to a point; true once there.</summary>
        private bool WalkHostTo(ClientWorld world, float x, float z)
        {
            var client = _session.Client;
            float px = world.Local.X, pz = world.Local.Z;
            client.Input.MoveX = 0f;
            client.Input.MoveZ = 0f;
            if (new Vector2(x - px, z - pz).Length() < 0.4f) return true;
            // A goal that moved (a raider) is walked to along a new path.
            if (new Vector2(x - _ptGoalX, z - _ptGoalZ).Length() > 2f) _ptPath.Clear();
            if (_ptPath.Count == 0)
            {
                _ptWaypoint = 0;
                _ptGoalX = x;
                _ptGoalZ = z;
                if (!TilePathfinder.Find(world.Map, px, pz, x, z, _ptPath)) { Fail($"no way from {px:0.0},{pz:0.0} to {x:0.0},{z:0.0}"); return false; }
            }
            while (true)
            {
                var to = new Vector2(_ptPath[_ptWaypoint].X - px, _ptPath[_ptWaypoint].Z - pz);
                if (to.Length() < 0.6f && _ptWaypoint < _ptPath.Count - 1) { _ptWaypoint++; continue; }
                if (to.Length() < 0.05f) return true;
                to = to.Normalized();
                client.Input.MoveX = to.X;
                client.Input.MoveZ = to.Y;
                client.Input.YawRadians = MathF.Atan2(-to.X, -to.Y);
                client.Input.PitchRadians = 0f;
                return false;
            }
        }

        /// <summary>
        /// Faces the nearest raider and swings once it is in reach; fires the cannon at one down
        /// the street while there is a package for it; goes at one that keeps its distance on the
        /// raid's line; without one, stands.
        /// </summary>
        private void FightNearest(ClientWorld world)
        {
            StopHost();
            if (world.Local.Dead) return;
            ClientEnemy? nearest = null;
            float best = float.MaxValue;
            foreach (var e in world.Enemies.Values)
            {
                float d = new Vector2(e.X - world.Local.X, e.Z - world.Local.Z).Length();
                if (d >= best) continue;
                best = d;
                nearest = e;
            }
            if (nearest == null) return;
            if (_session.Client.CannonAmmo > 0 && best >= CannonFrom && best <= CannonTo && OpenGround(world, nearest.X, nearest.Z))
            {
                AimAt(nearest.X, nearest.Z, InputButtons.AltAttack);
                return;
            }
            if (best > AdvanceBeyond)
            {
                WalkHostTo(world, nearest.X, nearest.Z);
                return;
            }
            AimAt(nearest.X, nearest.Z, best <= SwingAt ? InputButtons.Attack : InputButtons.None);
        }

        /// <summary>True if nothing solid lies between the host and a point: a package fired there arrives.</summary>
        private static bool OpenGround(ClientWorld world, float x, float z)
        {
            const float tile = SimConstants.TileSize;
            var from = new Vector2(world.Local.X, world.Local.Z);
            var to = new Vector2(x, z);
            int steps = Math.Max(1, (int)MathF.Ceiling(from.DistanceTo(to) / 0.4f));
            for (int i = 0; i <= steps; i++)
            {
                var at = from.Lerp(to, i / (float)steps);
                if (world.Map.IsSolid((int)MathF.Floor(at.X / tile), (int)MathF.Floor(at.Y / tile))) return false;
            }
            return true;
        }

        private void StepPlaytest()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var waves = _session.Content.Waves;
            var player = server.GetPlayer(world.LocalId);
            if (player == null) { Fail("the server has no player for the host"); return; }
            hud.Refresh();
            TrackCamera();
            foreach (char c in FontCheck.MissingGlyphs(hud))
                if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;
            if (_screen.Enemies.ViewCount > 0) _ptSawEnemyView = true;
            if (_screen.Camera.WeaponDrawn) _ptSawHammer = true;
            if (_screen.Camera.CannonOut) _ptSawCannon = true;
            if (hud.Loot.Cards.Visible) _ptSawCards = true;
            if (world.Local.Dead && !_ptWasDead)
            {
                _ptDeaths++;
                // Back on the pad the way to the raiders is another one.
                _ptPath.Clear();
            }
            _ptWasDead = world.Local.Dead;
            // Nit 17: back on their feet the host is protected, and the banner says so.
            if (world.LocalProtected)
            {
                _ptProtected++;
                if (hud.BannerText != HudText.Protected(world) && !_ptBadBanner)
                {
                    _ptBadBanner = true;
                    Check(false, $"protected after standing up, the banner says \"{hud.BannerText}\", expected \"{HudText.Protected(world)}\"");
                }
            }
            _ptMostSeen = Math.Max(_ptMostSeen, world.Enemies.Count);
            TrackRuinFeed();
            TrackPostOffice();
            _ptFrames++;
            _frames++;

            switch (_ptStep)
            {
                case 0:
                    // The Lobby as ever; what was asked on the command line is noted on the server's player.
                    if (_ptFrames < BootFrames) break;
                    if (_ptFrames == BootFrames)
                    {
                        if (_session.Playtest != RaidPlaytest || _session.StartShift != RaidPlaytestShift)
                        {
                            Fail($"the playtest smoke is run with --shift={RaidPlaytestShift} --no-quota --raid --playtest-package, not with {_session.Playtest} and shift {_session.StartShift}");
                            break;
                        }
                        Check(server.Run.Phase == RunPhase.Lobby && hud.PhaseText == "LOBBY", $"booted into {server.Run.Phase}, the HUD says \"{hud.PhaseText}\"");
                        Check(player.Playtest == RaidPlaytest && player.PlaytestShift == RaidPlaytestShift,
                            $"the host asked for {RaidPlaytest} and shift {RaidPlaytestShift}, the server noted {player.Playtest} and shift {player.PlaytestShift}");
                        Check(server.StartShift == RaidPlaytestShift && server.QuotaWaived && server.RaidsAtShiftStart && !server.ClockStopped,
                            $"the server would start at shift {server.StartShift}, quota waived {server.QuotaWaived}, raids at shift start {server.RaidsAtShiftStart}, clock stopped {server.ClockStopped}");
                    }
                    // The Cursed Mail stamp (doc 01 §6), set with the host's key before the run starts.
                    if (StepLobbyStamp()) break;
                    Go();
                    if (world.Global.Phase == RunPhase.Prep) NextPlaytestStep();
                    else if (_ptFrames > PlaytestStepFrames) Fail("the run did not leave the Lobby after the host pressed go");
                    break;

                case 1:
                {
                    // --shift=2: the run's first Prep is the second shift's, with that shift's quota.
                    if (_ptFrames < 3) break;
                    if (_ptFrames == 3)
                    {
                        string want = $"Shift {RaidPlaytestShift} / {HudText.ShiftCount(world.Content)}   PREP";
                        Check(server.Run.Shift == RaidPlaytestShift && server.Run.RunNumber == 1, $"the run started at shift {server.Run.Shift}");
                        Check(hud.PhaseText.StartsWith(want, StringComparison.Ordinal), $"in the first Prep the HUD phase line is \"{hud.PhaseText}\", expected \"{want} …\"");
                        Check(world.Global.Quota == QuotaFormula.Quota(world.Content.Balance, RaidPlaytestShift, 1) && hud.QuotaText == HudText.Quota(world.Global),
                            $"in the first Prep the HUD quota line is \"{hud.QuotaText}\" for a quota of {world.Global.Quota}");
                        Check(world.Raid == RaidState.None && hud.RaidText.Length == 0, $"in Prep the raid tracker says \"{hud.RaidText}\"");
                        // --playtest-package: nothing before the shift, there is no mail in Prep.
                        Check(client.CannonAmmo == 0 && !server.HasAmmo(player), $"in Prep the host has {client.CannonAmmo} packages for the cannon");
                        Shot("playtest-prep");
                    }
                    if (!world.LocalReady) Go();
                    if (world.Global.Phase == RunPhase.Delivery)
                    {
                        _ptDeliveryTick = server.Run.PhaseStartTick;
                        _ptSwings = client.LocalHammerSwings;
                        _ptShots = client.LocalCannonShots;
                        _ptBlasts = _screen.Effects.Blasts;
                        _ptBlastHits = _screen.Effects.BlastsThatHit;
                        NextPlaytestStep();
                    }
                    else if (_ptFrames > PlaytestStepFrames) Fail("Prep did not end after the only player readied up");
                    break;
                }

                case 2:
                {
                    // --raid: the warning is up as the shift starts, counting down the five seconds.
                    WalkHostTo(world, StandX, StandZ);
                    if (world.Raid == RaidState.None)
                    {
                        if (_ptFrames > 30) Fail($"no raid warning {server.Tick - _ptDeliveryTick} ticks into the shift (server: {server.Raid})");
                        break;
                    }
                    uint lead = server.RaidStartTick - _ptDeliveryTick;
                    Check(lead == SimConstants.SecondsToTicks(PlaytestMask.RaidNowSeconds), $"the raid is due {lead} ticks into the shift");
                    Check(world.Raid == RaidState.Warning && world.RaidSecondsLeft > 0 && world.RaidSecondsLeft <= PlaytestMask.RaidNowSeconds,
                        $"the client sees the raid as {world.Raid}, {world.RaidSecondsLeft:0.0} s away");
                    Check(hud.BannerText == HudText.RaidBanner(world) && hud.BannerText.StartsWith(HudText.RaidWarning, StringComparison.Ordinal),
                        $"with a raid announced the banner says \"{hud.BannerText}\"");
                    Check(hud.RaidText == HudText.RaidTracker(world) && hud.RaidText.StartsWith("Raid in ", StringComparison.Ordinal),
                        $"with a raid announced the tracker says \"{hud.RaidText}\"");
                    Check(hud.PhaseText.Contains("DELIVERY", StringComparison.Ordinal) && !hud.PhaseText.Contains(HudText.ClockStoppedTag, StringComparison.Ordinal),
                        $"in the shift the HUD phase line is \"{hud.PhaseText}\"");
                    // --playtest-package (nit 18): the shift starts with packages for the cannon, and the HUD counts them.
                    Check(client.CannonAmmo == PlaytestMask.PackageCount && hud.WeaponsText == HudText.Weapons(PlaytestMask.PackageCount),
                        $"as the shift starts the host has {client.CannonAmmo} packages for the cannon and the weapons line says \"{hud.WeaponsText}\"");
                    Shot("playtest-warning");
                    NextPlaytestStep();
                    break;
                }

                case 3:
                    // The raid begins on time: the banner goes, the tracker counts raiders.
                    WalkHostTo(world, StandX, StandZ);
                    if (world.Raid == RaidState.Warning)
                    {
                        if (_ptFrames > PlaytestStepFrames) Fail("the announced raid never began");
                        break;
                    }
                    Check(world.Raid == RaidState.Active && server.Raid == RaidState.Active, $"after the warning the raid is {world.Raid} (server: {server.Raid})");
                    Check(server.Tick - server.RaidStartTick < 30, $"the raid began {server.Tick - server.RaidStartTick} ticks late");
                    Check(server.RaidBudget > 0, "the raid has no budget");
                    NextPlaytestStep();
                    break;

                case 4:
                {
                    // The fight: out on the street until every pulse is in and nobody of it stands.
                    bool there = WalkHostTo(world, StandX, StandZ);
                    if (there || world.Enemies.Count > 0 && NearestEnemy(world) < 8f) _ptStep = 5;
                    else if (_ptFrames > PlaytestStepFrames) Fail($"the host did not reach the street (at {world.Local.X:0.0},{world.Local.Z:0.0})");
                    break;
                }

                case 5:
                {
                    FightNearest(world);
                    if (world.Raid == RaidState.Active && world.Enemies.Count > 0)
                    {
                        if (hud.RaidText == HudText.RaidTracker(world) && hud.RaidText.Contains("left", StringComparison.Ordinal)) _ptSawTracker = true;
                        else if (!_ptBadTracker)
                        {
                            _ptBadTracker = true;
                            Check(false, $"with {world.Enemies.Count} raiders about the tracker says \"{hud.RaidText}\"");
                        }
                        if (_ptSawHammer) Shot("playtest-fight");
                    }
                    if (world.Global.Phase != RunPhase.Delivery || _ptFrames > FightFrames)
                    {
                        Fail($"the raid was not beaten: {world.Global.Phase} after {_ptFrames} frames, pulse {server.RaidPulsesSpawned} of {waves.Pulses}, "
                            + $"{server.Enemies.Count} raiders left of {server.EnemiesSpawnedTotal}, {world.LocalKills} felled, down {_ptDeaths} times, hp {world.LocalHp:0}");
                        break;
                    }
                    if (server.RaidPulsesSpawned < waves.Pulses || server.Enemies.Count > 0 || world.Enemies.Count > 0 || _screen.Enemies.ViewCount > 0) break;
                    StopHost();
                    int spawned = server.EnemiesSpawnedTotal;
                    Check(spawned >= waves.Pulses, $"{waves.Pulses} pulses brought {spawned} raiders");
                    Check(server.EnemiesKilledTotal == spawned && world.LocalKills == spawned, $"of {spawned} raiders {server.EnemiesKilledTotal} fell, the client counts {world.LocalKills}");
                    Check(_ptSawEnemyView, "no raider was ever drawn");
                    Check(_ptSawTracker, "the raid tracker never counted the raiders");
                    Check(_ptSawHammer, "the hammer never showed in the player's hands");
                    int shots = client.LocalCannonShots - _ptShots;
                    Check(shots == PlaytestMask.PackageCount && client.CannonAmmo == 0 && !server.HasAmmo(player),
                        $"of {PlaytestMask.PackageCount} packages the host fired {shots} at the raid and has {client.CannonAmmo} left");
                    Check(_ptSawCannon && _screen.Effects.Blasts - _ptBlasts == shots, $"{shots} cannon shots: the cannon showed in the player's hands: {_ptSawCannon}, {_screen.Effects.Blasts - _ptBlasts} blasts");
                    // Aimed at a raider, a package hits one: the playtest's cannon never did.
                    int hit = _screen.Effects.BlastsThatHit - _ptBlastHits;
                    Check(hit >= shots - 1, $"of {shots} packages fired at raiders {hit} hit one");
                    Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("You felled ", StringComparison.Ordinal)), "the feed does not say a raider was felled");
                    Check(hud.RaidText == HudText.RaidTracker(world), $"with the raid beaten the tracker says \"{hud.RaidText}\"");
                    NextPlaytestStep();
                    break;
                }

                case 6:
                    // What the raiders left is taken on foot: coins in the wallet line, cards over the hotbar.
                    WalkToLoot(world);
                    if (world.Loot.Count > 0 && _ptFrames <= LootFrames) break;
                    if (world.Global.Wallet != server.Economy.Wallet && _ptFrames <= LootFrames + 30) break;
                    if (!_ptLootSeen)
                    {
                        StopHost();
                        Check(server.Economy.Wallet > 0 && player.LootCoins == server.Economy.Wallet, $"the wallet holds {server.Economy.Wallet}, the host took {player.LootCoins} in loot");
                        Check(hud.WalletText == HudText.Wallet(world.Global), $"with the loot taken the HUD shows \"{hud.WalletText}\", expected \"{HudText.Wallet(world.Global)}\"");
                        Check(_ptSawCards, "the loot cards never showed");
                        Check(server.Economy.ShiftEarnings < server.Run.Quota, $"the shift's quota of {server.Run.Quota} was met by a fight ({server.Economy.ShiftEarnings})");
                        Shot("playtest-loot");
                        _ptLootSeen = true;
                    }
                    // Doc 05 §6: a cursed letter at the wrong mailbox, its mini-raid seen.
                    if (StepCursedMail()) break;
                    // A wall broken in the shift, for its ruin to be rebuilt in the next Prep.
                    LeaveRuinForPrep();
                    ArmPostOfficeBlow();
                    // The rest of the shift has nothing to show: a second of it per frame.
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    NextPlaytestStep();
                    break;

                case 7:
                {
                    // --no-quota: the Payday of a missed quota says the run goes on.
                    if (world.Global.Phase == RunPhase.Delivery)
                    {
                        if (_ptFrames > PlaytestStepFrames) Fail("the shift did not end");
                        break;
                    }
                    if (world.Global.Phase != RunPhase.Payday) { Fail($"after the shift came {world.Global.Phase}"); break; }
                    _session.FixedTicksPerFrame = TicksPerFrame;
                    var summary = world.LastSummary;
                    if (summary == null)
                    {
                        if (_ptFrames > PlaytestStepFrames) Fail("no summary of the shift reached the client");
                        break;
                    }
                    Check(summary.Shift == RaidPlaytestShift && !summary.QuotaMet, $"the summary is of shift {summary.Shift}, quota met: {summary.QuotaMet}");
                    string want = string.Join("\n", HudText.SummaryLines(world, summary));
                    Check(hud.Overlay.Visible && hud.OverlayText == want, $"at Payday the panel shows \"{hud.OverlayText}\", expected \"{want}\"");
                    // --no-quota: the 0e940bc playtest's Payday read "QUOTA MISSED" though the quota was off.
                    Check(hud.OverlayText.Contains("QUOTA OFF", StringComparison.Ordinal) && !hud.OverlayText.Contains("MISSED", StringComparison.Ordinal), "the panel does not say the quota is off");
                    Check(world.Raid == RaidState.Fleeing && hud.RaidText == HudText.RaidTracker(world) && hud.BannerText.Length == 0,
                        $"at Payday the raid is {world.Raid}, the tracker says \"{hud.RaidText}\" and the banner \"{hud.BannerText}\" lies over the panel");
                    Check(hud.HintText.Contains(HudText.QuotaWaivedTag, StringComparison.Ordinal) && hud.HintText.Contains($"shift {RaidPlaytestShift + 1} in ", StringComparison.Ordinal),
                        $"at the Payday of a waived quota the HUD hint is \"{hud.HintText}\"");
                    Shot("playtest-payday");
                    NextPlaytestStep();
                    break;
                }

                case 8:
                {
                    // … and it does: the next shift's Prep, no Results, nothing of the fight left.
                    if (world.Global.Phase == RunPhase.Payday)
                    {
                        if (_ptFrames > PlaytestStepFrames) Fail("Payday did not end");
                        break;
                    }
                    if (world.Global.Phase == RunPhase.Draft)
                    {
                        // The Draft between the two shifts: a card, with the pointer this time (the full run takes its by key), and on.
                        if (!_ptPicked && world.ChoosingCard && hud.Draft.Visible)
                        {
                            Check(!_screen.Mouse.Wanted, "with the Draft's cards open to the host the mouse is not free for them");
                            hud.Draft.Click(world.FirstOpenCard);
                            _ptPicked = hud.Draft.LastRequestId != 0;
                            Check(_ptPicked, $"a click on card {world.FirstOpenCard + 1} of the Draft did not take it");
                        }
                        if (_ptFrames > 2 * PlaytestStepFrames) Fail("the Draft did not end");
                        break;
                    }
                    if (_ptFrames < 2 * PlaytestStepFrames && world.Global.Shift != server.Run.Shift) break;
                    if (StepRuinRebuild()) break;
                    Check(_ruinStep == RuinStep.Done, $"the ruin of shift {RaidPlaytestShift} was not rebuilt in the next Prep ({_ruinStep})");
                    if (StepRepairWall()) break;
                    VerifyPostOffice();
                    string want = $"Shift {RaidPlaytestShift + 1} / {HudText.ShiftCount(world.Content)}   PREP";
                    Check(server.Run.Phase == RunPhase.Prep && server.Run.Shift == RaidPlaytestShift + 1 && server.Run.Outcome == RunOutcome.None && server.Run.Results == null,
                        $"after the missed quota the server is in {server.Run.Phase} of shift {server.Run.Shift} ({server.Run.Outcome})");
                    Check(world.Results == null && hud.PhaseText.StartsWith(want, StringComparison.Ordinal), $"after the missed quota the HUD phase line is \"{hud.PhaseText}\", expected \"{want} …\"");
                    Check(server.Run.ShiftsCompleted == 0, $"a missed quota counts as {server.Run.ShiftsCompleted} shifts completed");
                    Check(world.Raid == RaidState.None && world.Enemies.Count == 0 && server.Enemies.Count == 0 && hud.RaidText.Length == 0, $"in the next Prep the raid is {world.Raid}, \"{hud.RaidText}\"");
                    Check(_ptPicked && world.LocalPerks.Count + world.TeamPerks.Count == 1 && _screen.Mouse.Wanted, $"after the Draft the host holds {world.LocalPerks.Count + world.TeamPerks.Count} perks and the mouse is {(_screen.Mouse.Wanted ? "the view's" : "free")}");
                    Check(client.ContainerDesyncs == 0 && client.BadPackets == 0, $"{client.ContainerDesyncs} desyncs, {client.BadPackets} bad packets");
                    Check(_maxEyeOffset <= 0.001f && _maxAimOffset <= 0.001f, $"the camera strayed {_maxEyeOffset:0.000} m from the eyes and {_maxAimOffset:0.000} from the aim");
                    Check(_missingGlyphs.Length == 0, $"the UI font has no glyph for: {_missingGlyphs}");
                    GD.Print($"[smoke] playtest: run started at shift {RaidPlaytestShift}; raid announced at the shift's start, {server.EnemiesSpawnedTotal} raiders in {waves.Pulses} pulses (most at once {_ptMostSeen}) felled with "
                        + $"{client.LocalCannonShots - _ptShots} packages out of the cannon ({_screen.Effects.BlastsThatHit - _ptBlastHits} hit) and {client.LocalHammerSwings - _ptSwings} swings, down {_ptDeaths} times, protected for {_ptProtected} frames, loot +{HudText.Gold(server.Economy.Wallet)}"
                        + $"; quota missed and waived, a card of the Draft taken with a click, on to shift {server.Run.Shift}");
                    Finish();
                    break;
                }
            }
        }

        private static float NearestEnemy(ClientWorld world)
        {
            float best = float.MaxValue;
            foreach (var e in world.Enemies.Values)
                best = Math.Min(best, new Vector2(e.X - world.Local.X, e.Z - world.Local.Z).Length());
            return best;
        }
    }
}
