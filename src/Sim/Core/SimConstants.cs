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

        public static uint SecondsToTicks(double seconds) => (uint)Math.Max(0, Math.Round(seconds * TickRate));
        public static double TicksToSeconds(long ticks) => ticks / (double)TickRate;
    }
}
