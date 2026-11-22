using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Gate and the Alarm Post in the smoke's run (doc 05 §4): in the Prep of the Fortify
    /// visit, with Logs and Iron Ingots the server hands over, the host builds a Gate turned
    /// across the way east, walks through it while it swings open on screen, and builds an
    /// Alarm Post in range of the raiders' way in, whose prompt says it rings the raid in early.
    /// Both stand for the shift's raid: its feed line names the post's warning, the post's lamp
    /// flashes and the raiders near it wear its mark.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _defenceStep, _defenceFrames, _defenceVisits, _gateTx, _gateTz;
        private bool _sawGateOpen, _sawAlarmLine, _sawMarks, _sawLampLit;
        private Action? _walkEast;

        private bool StepDefences()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var build = _session.Build;
            var gate = world.Content.Recipes.First(r => r.Building.Id == "gate");
            var alarm = world.Content.Recipes.First(r => r.Building.Id == "alarm_post");
            var player = server.GetPlayer(world.LocalId)!;

            if (_defenceStep == 0)
            {
                if (_defenceVisits > 0 || _fortifyVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                _session.FixedTicksPerFrame = 1;
                Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("log"), 6)), "no room for six Logs");
                Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 2)), "no room for two Iron Ingots");
                // Somewhere with open ground on the far side as well, to walk through to.
                if (!StandAtBuildSite(accept: (tx, tz) => BuildRules.CheckTile(world.Map, world.Constructs, tx + 1, tz) == OpResult.Ok))
                    Fail("no open ground to put the Gate on");
                (_gateTx, _gateTz) = (_buildTx, _buildTz);
                _defenceStep = 1;
                _defenceFrames = 0;
                return true;
            }
            if (++_defenceFrames > BuildStepFrames)
            {
                Fail($"the Gate and Alarm Post visit stopped at step {_defenceStep}: ghost {build.Ghost.Aim} {build.Ghost.State}, prompt \"{hud.PromptText}\", host at {world.Local.X:0.00}, {world.Local.Z:0.00}");
                return false;
            }
            bool next = false;
            switch (_defenceStep)
            {
                case 1:
                {
                    // The Gate, turned to run along Z: across the way east.
                    AimAtTile(_gateTx, _gateTz);
                    bool arrived = MathF.Abs(world.Local.X - _buildX) < 0.3f && MathF.Abs(world.Local.Z - _buildZ) < 0.3f;
                    if (!arrived || _defenceFrames < 4 || client.PendingRequestCount > 0) break;
                    if (!build.Active) Check(build.Toggle(), $"B did not put Build mode up ({build.Refused})");
                    Check(build.PressKey(gate.Key) && build.Recipe == gate, "the Gate has no key in Build mode");
                    if ((build.Rotation & 1) == 0) build.Rotate();
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    _screen.Constructs.Refresh();
                    if (build.Ghost.Aim != BuildAim.Place || build.Ghost.State != OpResult.Ok) break;
                    Check(hud.PromptText.StartsWith("Build Gate", StringComparison.Ordinal) && _screen.Constructs.GhostGreen, $"the Gate's ghost: \"{hud.PromptText}\"");
                    Shot("gate-ghost");
                    build.Update(1.0 / 30.0, true, false);
                    next = true;
                    break;
                }
                case 2:
                {
                    var built = world.Constructs.At(_gateTx, _gateTz);
                    if (built == null || server.Constructs.At(_gateTx, _gateTz) == null || client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "gate" && built.AlongZ, $"built a {built.Def.Id} turned {built.Rotation}");
                    Check(world.Constructs.Blocks(_gateTx, _gateTz) && !world.Constructs.Blocks(_gateTx, _gateTz, carrier: true), "the Gate is not a wall to raiders and a way through to carriers");
                    build.Leave();
                    _screen.Constructs.Refresh();
                    Shot("gate");
                    next = true;
                    break;
                }
                case 3:
                {
                    // Walked through, east: it swings open as the host comes, and nothing stops them.
                    float goal = BuildRules.TileCentre(_gateTx) + 1.6f;
                    if (_walkEast == null)
                    {
                        // What the run's bot would ask for is replaced, tick by tick, by a walk east.
                        _walkEast = () =>
                        {
                            client.Input.YawRadians = -MathF.PI * 0.5f;
                            client.Input.PitchRadians = 0f;
                            client.Input.MoveX = 1f;
                            client.Input.MoveZ = 0f;
                            client.Input.Buttons = InputButtons.None;
                        };
                        client.TickStarting += _walkEast;
                    }
                    if (_screen.Constructs.GatesOpening > 0 && !_sawGateOpen)
                    {
                        _sawGateOpen = true;
                        Shot("gate-open");
                    }
                    if (world.Local.X < goal) break;
                    client.TickStarting -= _walkEast;
                    _walkEast = null;
                    client.Input.MoveX = 0f;
                    Check(_sawGateOpen, "the Gate never swung open for the host walking through it");
                    Check(MathF.Abs(world.Local.Z - _buildZ) < 0.3f, $"walking through the Gate the host was pushed to z {world.Local.Z:0.00} from {_buildZ:0.00}");
                    // On to the Alarm Post's place, near the raiders' way in.
                    RaidRules.Gate(world.Map, out float gx, out float gz);
                    if (!StandAtBuildSite(gx - 12f, gz, (tx, tz) => RaidRules.AlarmInRange(world.Map, alarm.Building, tx, tz, world.Global.Shift)))
                        Fail("no open ground in range of the raiders' way in for the Alarm Post");
                    next = true;
                    break;
                }
                case 4:
                {
                    client.Input.MoveX = 0f;
                    AimAtTile(_buildTx, _buildTz);
                    bool arrived = MathF.Abs(world.Local.X - _buildX) < 0.3f && MathF.Abs(world.Local.Z - _buildZ) < 0.3f;
                    if (!arrived || _defenceFrames < 4 || client.PendingRequestCount > 0) break;
                    if (!build.Active) Check(build.Toggle(), $"B did not put Build mode up ({build.Refused})");
                    Check(build.PressKey(alarm.Key) && build.Recipe == alarm, "the Alarm Post has no key in Build mode");
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    _screen.Constructs.Refresh();
                    if (build.Ghost.Aim != BuildAim.Place || build.Ghost.State != OpResult.Ok) break;
                    Check(hud.PromptText.StartsWith("Build Alarm Post", StringComparison.Ordinal) && hud.PromptText.Contains("rings the raid in 15 s early", StringComparison.Ordinal),
                        $"the Alarm Post's ghost: \"{hud.PromptText}\"");
                    Shot("alarm-ghost");
                    build.Update(1.0 / 30.0, true, false);
                    next = true;
                    break;
                }
                case 5:
                {
                    var built = world.Constructs.At(_buildTx, _buildTz);
                    if (built == null || server.Constructs.At(_buildTx, _buildTz) == null || client.PendingRequestCount > 0) break;
                    Check(built.Def.Id == "alarm_post", $"built a {built.Def.Id} for the Alarm Post");
                    build.Leave();
                    _screen.Constructs.Refresh();
                    Shot("alarm-post");
                    double warning = server.RaidWarningSeconds();
                    Check(warning >= 30.0, $"with the Alarm Post up the raid's warning is {warning} s");
                    GD.Print($"[smoke] defences: a Gate built at ({_gateTx}, {_gateTz}) and walked through, an Alarm Post at ({_buildTx}, {_buildTz}): {warning:0} s of warning");
                    _defenceVisits++;
                    _defenceStep = 0;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
                }
            }
            if (next)
            {
                _defenceStep++;
                _defenceFrames = 0;
            }
            return true;
        }

        /// <summary>Every frame of the run: what the Alarm Post does once there is a raid.</summary>
        private void TrackAlarm()
        {
            if (_defenceVisits == 0 || _sawMarks && _sawLampLit && _sawAlarmLine) return;
            var world = _session.Client.World!;
            if (world.Raid == RaidState.None) return;
            if (!_sawAlarmLine && _session.Feed.Entries.Any(e => e.Text.StartsWith(HudText.RaidWarning, StringComparison.Ordinal) && e.Text.Contains("with the Alarm Post", StringComparison.Ordinal)))
                _sawAlarmLine = true;
            if (_screen.Constructs.LampsLit > 0) _sawLampLit = true;
            if (_screen.Enemies.MarksShown > 0 && !_sawMarks)
            {
                _sawMarks = true;
                Check(world.MarkedEnemies > 0, "raiders wear the Alarm Post's mark that the world does not count");
                Shot("alarm-marks");
            }
        }

        private void VerifyDefences()
        {
            Check(_defenceVisits == 1, $"the Gate and the Alarm Post were built {_defenceVisits} times");
            Check(_sawAlarmLine, "the raid's feed line never named the Alarm Post's warning");
            Check(_sawLampLit && _sawMarks, $"in the raid the Alarm Post's lamp {(_sawLampLit ? "flashed" : "never flashed")} and the raiders were {(_sawMarks ? "" : "never ")}marked");
        }
    }
}
