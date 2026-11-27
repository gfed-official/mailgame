using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.Meta
{
    /// <summary>
    /// The onboarding tips of a first run (doc 09 §6; design choice 173): in shift 1's Delivery, one tip at a time on the
    /// hint's line — take mail from the Intake, match the address, build a belt from the Intake's outfeed, raids start
    /// next shift. A tip whose action the local carrier performs, or that is put away with the help key, is written into
    /// <see cref="MetaProfile.TipsDone"/> and never shown again. A profile that has finished a run sees none, unless
    /// <c>--playtest-tips</c> forces them (then the profile's done list is not read, only written).
    /// </summary>
    public sealed class TipTracker
    {
        public const string Intake = "intake", Address = "address", Belt = "belt", Raids = "raids";
        /// <summary>In the order they are shown.</summary>
        public static readonly string[] All = { Intake, Address, Belt, Raids };
        /// <summary>The raids' tip comes at the latest this long before shift 1's Delivery ends, whatever else is undone, s.</summary>
        public const double RaidsTipSeconds = 60.0;

        private readonly ClientRuntime _client;
        private readonly ProfileTracker _profile;
        private readonly bool _forced;
        /// <summary>What this session has done or put away: under <c>--playtest-tips</c> the profile's list is not read.</summary>
        private readonly HashSet<string> _done = new HashSet<string>();
        private bool _raidsShown;

        public TipTracker(ClientRuntime client, ProfileTracker profile, bool forced)
        {
            _client = client;
            _profile = profile;
            _forced = forced;
            if (!forced) _done.UnionWith(profile.Profile.TipsDone);
            client.EventReceived += OnEvent;
        }

        /// <summary>Whether the tip has been done or put away (in this session's view).</summary>
        public bool IsDone(string id) => _done.Contains(id);

        /// <summary>The id of the tip to show now, or null.</summary>
        public string? Current
        {
            get
            {
                var world = _client.World;
                if (world == null || !world.HasLocal || world.Global.Phase != RunPhase.Delivery || world.Local.Dead) return null;
                // A first run's (doc 09 §6); a playtest's forced tips come in any shift.
                if (!_forced && (_profile.Profile.Runs > 0 || world.Global.Shift != 1)) return null;
                bool carrying = Carrying(world);
                if (carrying) Done(Intake);
                // Raids start next shift: only where a next shift has one (not a practice run, not the last shift).
                bool raidsNext = world.Global.Shift == 1 && world.Global.Mode != RunMode.Practice && HudText.ShiftCount(world) > 1;
                bool late = world.PhaseSecondsLeft <= RaidsTipSeconds && !world.ClockStopped;
                if (raidsNext && !IsDone(Raids) && (late || (IsDone(Intake) && IsDone(Address) && IsDone(Belt))))
                {
                    _raidsShown = true;
                    return Raids;
                }
                if (!IsDone(Intake)) return Intake;
                if (!IsDone(Address) && carrying) return Address;
                if (!IsDone(Address)) return null;
                if (!IsDone(Belt)) return Belt;
                return null;
            }
        }

        /// <summary>The help key during a run: the tip shown now is put away for good. False when none was shown.</summary>
        public bool Dismiss()
        {
            string? id = Current;
            if (id == null) return false;
            Done(id);
            return true;
        }

        private static bool Carrying(ClientWorld world)
        {
            foreach (var c in new[] { world.Hotbar, world.Inventory })
                if (c != null)
                    foreach (var e in c.Entries)
                        if (e.Stack.Kind == StackKind.Mail) return true;
            return false;
        }

        private void Done(string id)
        {
            if (!_done.Add(id)) return;
            if (!_profile.Profile.TipsDone.Contains(id)) _profile.Profile.TipsDone.Add(id);
            _profile.Save();
        }

        private void OnEvent(SimEvent ev)
        {
            var world = _client.World;
            if (world == null) return;
            switch (ev)
            {
                case DeliveredEvent e when e.Player == world.LocalId:
                    Done(Intake);
                    Done(Address);
                    break;
                case ConstructPlacedEvent e when e.Player == world.LocalId && e.Building < world.Content.Buildings.Count && world.Content.Buildings[e.Building].Carries:
                    Done(Belt);
                    break;
                // Read to the shift's end: the raids' tip has said what it had to.
                case ShiftSummaryEvent _ when _raidsShown:
                    Done(Raids);
                    break;
            }
        }
    }
}
