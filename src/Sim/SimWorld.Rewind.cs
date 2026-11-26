using System;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Lag compensation (doc 06 §4.3; doc 12 M3 "lag compensation rewind buffer"; design choice 145):
    /// a carrier's swing and their hitscan shots (the Slingshot, a turret at their controls) are
    /// judged against the raiders where that carrier's client drew them when the input was made, at
    /// most 250 ms back. Each raider keeps where it stood at the end of its last ticks
    /// (<see cref="Enemy.RecordPosition"/>); the blow, the shove and the damage land on it where it is.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Doc 06 §4.3 "rewind buffer of 250 ms": 7 ticks at 30 Hz.</summary>
        public const int MaxRewindTicks = (int)(0.25 * Core.SimConstants.TickRate);
        /// <summary>Doc 06 §4.3 "Melee: … within an expanded hit volume (+0.3 m)".</summary>
        public const float MeleeHitSlack = 0.3f;

        /// <summary>How far back the input being spent judges its hits; set for each input before its attack.</summary>
        private int _attackRewind;

        /// <summary>The last rewind an attack was judged with, in ticks (tests, diagnostics).</summary>
        public int LastRewindTicks { get; private set; }

        /// <summary>
        /// Ticks back from the raiders' places now (the end of the last tick) to the tick the input's
        /// client drew them at: 0 for an input that says none or a client drawing ahead of the server,
        /// <see cref="MaxRewindTicks"/> at most.
        /// </summary>
        public int RewindTicks(in PlayerInput input)
        {
            if (input.View == 0 || Tick == 0) return 0;
            int back = (ushort)((Tick - 1) - input.View);
            if (back >= 0x8000) return 0;
            return Math.Min(back, MaxRewindTicks);
        }

        /// <summary>Where the raider stood for the attack being judged.</summary>
        private void Rewound(Enemy e, out float x, out float z)
        {
            LastRewindTicks = _attackRewind;
            e.PositionAgo(_attackRewind, out x, out z);
        }

        private void RecordEnemyPositions()
        {
            foreach (var e in _enemies) e.RecordPosition();
        }
    }
}
