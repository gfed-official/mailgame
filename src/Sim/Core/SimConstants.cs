using System;

namespace PerformativeMail.Sim.Core
{
    public static class SimConstants
    {
        /// <summary>Fixed simulation rate (doc 06 §3).</summary>
        public const int TickRate = 30;
        public const float TickDt = 1f / TickRate;

        /// <summary>Building grid tile edge in metres (doc 00 assumptions).</summary>
        public const float TileSize = 2f;

        /// <summary>Height of a carrier's eyes above their feet: where the first-person camera and the interact ray start.</summary>
        public const float EyeHeight = 1.6f;

        public static uint SecondsToTicks(double seconds) => (uint)Math.Max(0, Math.Round(seconds * TickRate));
        public static double TicksToSeconds(long ticks) => ticks / (double)TickRate;
    }
}
