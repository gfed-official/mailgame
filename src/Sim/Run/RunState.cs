using System;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Run
{
    /// <summary>Run phases (doc 01 §1). M0 only uses Lobby (no players yet) and Delivery.</summary>
    public enum RunPhase : byte
    {
        Lobby = 0,
        Prep = 1,
        Delivery = 2,
        Payday = 3,
        Draft = 4,
        Results = 5,
    }

    public sealed class RunState
    {
        public int Shift = 1;
        public RunPhase Phase = RunPhase.Lobby;
        public uint PhaseStartTick;
        public uint PhaseEndTick;
        public int Quota;
        /// <summary>Player count sampled at the start of Delivery (doc 06 §6).</summary>
        public int PlayerCount;
        /// <summary>How many Delivery clocks have started; M0 repeats shift 1.</summary>
        public int ClockIndex;
    }

    public static class QuotaFormula
    {
        /// <summary>quota(shift, n) = baseQuota[shift] × n^playerScaleExponent (doc 01 §3.1), rounded to whole ¢.</summary>
        public static int Quota(Balance balance, int shift, int players)
        {
            if (players < 1) players = 1;
            double scale = Math.Pow(players, balance.GetDouble("playerScaleExponent"));
            return (int)Math.Round(balance.GetForShift("baseQuota", shift) * scale);
        }
    }
}
