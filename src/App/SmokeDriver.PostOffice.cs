using System;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Post Office's HP (doc 05 §5, design choice 137) in the raid playtest: once the raid of
    /// shift 2 is beaten the Post Office takes a blow of 120; the HUD's tracker reads "Post Office
    /// 2880 / 3000 HP   ·   under attack", a number rises off it and the marker names it; in shift
    /// 3's Prep it mends.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int PostOfficeBlow = 120;
        private int _poStep, _poNumbers0;
        private uint _poArmTick;
        private bool _poTracker, _poMarker;

        /// <summary>In Delivery: the blow comes 4 s on, once the wall broken for the ruin is under attack no longer (the marker names the nearest).</summary>
        private void ArmPostOfficeBlow()
        {
            _poStep = -1;
            _poArmTick = _session.Server!.World.Tick;
        }

        private void HitPostOffice()
        {
            var server = _session.Server!.World;
            _poNumbers0 = _screen.ConstructHealth.NumbersShown;
            server.DamagePostOffice(PostOfficeBlow);
            Check(server.PostOfficeHp == server.PostOfficeMaxHp - PostOfficeBlow, $"the Post Office has {server.PostOfficeHp} HP after a blow of {PostOfficeBlow}");
            _poStep = 1;
        }

        /// <summary>Every frame of the raid playtest: what the HUD and the view show of it.</summary>
        private void TrackPostOffice()
        {
            var world = _session.Client.World!;
            if (_poStep == -1 && world.Global.Phase == RunPhase.Delivery && _session.Server!.World.Tick >= _poArmTick + 4 * Sim.Core.SimConstants.TickRate) HitPostOffice();
            if (_poStep != 1) return;
            var hud = _screen.Hud;
            int max = world.PostOfficeMaxHp;
            string want = $"Post Office   {max - PostOfficeBlow} / {max} HP   ·   under attack";
            if (hud.PostOfficeText == want) _poTracker = true;
            if (_screen.AttackMarker.Text == "Post Office under attack")
            {
                _poMarker = true;
                // The 0e940bc playtest: the marker over "PROTECTED in the yard".
                var at = _screen.AttackMarker.WordsRect;
                var words = new ScreenBox(at.Position.X, at.Position.Y, at.End.X, at.End.Y);
                var tip = _screen.AttackMarker.Tip;
                foreach (var box in hud.KeepClear())
                {
                    Check(!box.Overlaps(words), $"the marker's words {at} are over a line of the HUD ({box.X0:0},{box.Y0:0})–({box.X1:0},{box.Y1:0}), the banner \"{hud.BannerText}\"");
                    Check(!(tip.X > box.X0 && tip.X < box.X1 && tip.Y > box.Y0 && tip.Y < box.Y1), $"the marker's tip {tip} is in a line of the HUD, the banner \"{hud.BannerText}\"");
                }
            }
            if (_poTracker && _poMarker && _screen.ConstructHealth.NumbersShown > _poNumbers0)
            {
                Shot("postoffice-hit");
                _poStep = 2;
            }
        }

        /// <summary>In shift 3's Prep: everything was seen, and it mends.</summary>
        private void VerifyPostOffice()
        {
            var server = _session.Server!.World;
            var world = _session.Client.World!;
            Check(_poStep == 2, $"the blow on the Post Office was not seen: tracker {_poTracker}, marker {_poMarker}, \"{_screen.Hud.PostOfficeText}\", \"{_screen.AttackMarker.Text}\"");
            Check(world.Global.Phase == RunPhase.Prep && server.PostOfficeHp > server.PostOfficeMaxHp - PostOfficeBlow,
                $"in Prep the Post Office has {server.PostOfficeHp} HP, it does not mend");
            GD.Print($"[smoke] post office: a blow of {PostOfficeBlow} in shift 2's Delivery seen on the HUD's tracker, as a number and by the marker; {server.PostOfficeHp:0} HP mending in shift 3's Prep");
        }
    }
}
