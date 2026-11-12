using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Run
{
    /// <summary>
    /// Run phases (doc 01 §1). Generating is skipped while the map is a static file, Raid arrives
    /// with combat, and Draft is passed over until perks exist. RunOver and Victory are the Results
    /// phase with a <see cref="RunOutcome"/>.
    /// </summary>
    public enum RunPhase : byte
    {
        Lobby = 0,
        Prep = 1,
        Delivery = 2,
        Payday = 3,
        Draft = 4,
        Results = 5,
    }

    /// <summary>How a run ended (doc 01 §7). None while it is still going.</summary>
    public enum RunOutcome : byte
    {
        None = 0,
        Victory = 1,
        QuotaMissed = 2,
    }

    /// <summary>The Payday summary of one shift (doc 01 §2.4).</summary>
    public sealed class ShiftSummary
    {
        public byte Shift;
        /// <summary>Delivery income of the shift with penalties subtracted; this is what the quota is checked against.</summary>
        public int Earned;
        public int Quota;
        public bool QuotaMet;
        /// <summary>Zero-misdelivery bonus paid to the wallet at Payday (doc 03 §4.2), 0 if none.</summary>
        public int Bonus;
        /// <summary>Correct deliveries per mail kind, indexed like ContentDb.MailKinds.</summary>
        public ushort[] DeliveriesByKind = Array.Empty<ushort>();
        public ushort Misdeliveries;
        public ushort LateDeliveries;
        /// <summary>Player with the most deliveries this shift; None if nobody delivered.</summary>
        public EntityId Mvp;
        public ushort MvpDeliveries;

        public int Deliveries
        {
            get
            {
                int n = 0;
                foreach (var k in DeliveriesByKind) n += k;
                return n;
            }
        }
    }

    public struct PlayerResult
    {
        public EntityId Player;
        public ushort Deliveries;
        public ushort Misdeliveries;
        public int Earned;
        /// <summary>Postal Rank XP awarded for this run (doc 01 §5.1).</summary>
        public int Xp;
    }

    /// <summary>The Results payload (doc 01 §1 phase table, doc 08 §3.2).</summary>
    public sealed class RunResults
    {
        public RunOutcome Outcome;
        public byte ShiftsCompleted;
        /// <summary>Shift earnings plus shift bonuses over the whole run.</summary>
        public int TotalEarned;
        /// <summary>totalEarned × stamp multipliers (doc 01 §6); no stamps exist yet, so equal to TotalEarned.</summary>
        public int Score;
        public int Deliveries;
        public int Misdeliveries;
        public readonly List<PlayerResult> Players = new List<PlayerResult>();
    }

    public sealed class RunState
    {
        public int Shift = 1;
        public RunPhase Phase = RunPhase.Lobby;
        public uint PhaseStartTick;
        /// <summary>Tick the phase ends on; 0 for a phase without a clock (Lobby, Results).</summary>
        public uint PhaseEndTick;
        public int Quota;
        /// <summary>Player count sampled at the start of Delivery (doc 06 §6).</summary>
        public int PlayerCount;
        /// <summary>Runs started on this server, including the current one.</summary>
        public int RunNumber;
        public int ShiftsCompleted;
        public RunOutcome Outcome;
        /// <summary>The player who may start the run and leave Results; None while nobody is connected.</summary>
        public EntityId Host;
        /// <summary>Summary of the most recent Payday of this run.</summary>
        public ShiftSummary? LastSummary;
        /// <summary>Set while in Results.</summary>
        public RunResults? Results;
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

        /// <summary>
        /// Postal Rank XP per player (doc 01 §5.1, doc 11): 100 × shiftsCompleted + 50 × victory +
        /// 5 × (team deliveries ÷ players, at least 1).
        /// </summary>
        public static int Xp(Balance balance, int shiftsCompleted, bool victory, int teamDeliveries, int players)
        {
            int share = Math.Max(1, teamDeliveries / Math.Max(1, players));
            return balance.GetInt("xpPerShift") * shiftsCompleted
                + (victory ? balance.GetInt("xpVictory") : 0)
                + balance.GetInt("xpPerDelivery") * share;
        }
    }
}
