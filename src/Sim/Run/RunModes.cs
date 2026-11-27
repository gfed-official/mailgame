using System;
using System.Globalization;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Run
{
    /// <summary>What kind of run the host has chosen in the Lobby (doc 01 §5.3, doc 09 §6).</summary>
    public enum RunMode : byte
    {
        /// <summary>Any world and seed, the stamps the host puts on.</summary>
        Normal = 0,
        /// <summary>Doc 09 §6: the test street from a fixed seed, two shifts, no raid, no XP.</summary>
        Practice = 1,
        /// <summary>Doc 01 §5.3: the Small Island from the UTC date's seed, with a fixed stamp set.</summary>
        Daily = 2,
    }

    /// <summary>The practice run's and the Daily Seed's numbers, as the server and the clients reckon them.</summary>
    public static class RunModes
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The shifts of a run: the balance's five, a practice run's <c>practiceShifts</c>.</summary>
        public static int ShiftCount(ContentDb content, RunMode mode)
        {
            int all = content.Balance.GetArray("baseQuota").Length;
            return mode == RunMode.Practice ? Math.Clamp(content.Balance.GetInt("practiceShifts"), 1, all) : all;
        }

        public static uint PracticeSeed(ContentDb content) => (uint)Math.Max(1, content.Balance.GetInt("practiceSeed"));

        /// <summary>The UTC day a moment falls on, counted from 1970-01-01.</summary>
        public static ushort Day(DateTime utc) => (ushort)Math.Clamp((utc.ToUniversalTime().Date - Epoch).Days, 0, ushort.MaxValue);

        /// <summary>"2026-11-27".</summary>
        public static string DayText(ushort day) => Epoch.AddDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// The Daily Seed (doc 01 §5.3 "UTC date hashed"): FNV-1a of "PM1-DAILY-2026-11-27", never 0, so everybody
        /// who plays the same day plays the same seed.
        /// </summary>
        public static uint DailySeed(ushort day)
        {
            uint h = 2166136261;
            foreach (char c in "PM1-DAILY-" + DayText(day))
            {
                h ^= c;
                h *= 16777619;
            }
            return h == 0 ? 1u : h;
        }

        /// <summary>The Daily Seed's fixed stamps (their <c>"daily"</c> in the content), a bit per stamp.</summary>
        public static byte DailyStamps(ContentDb content)
        {
            int mask = 0;
            foreach (var s in content.Stamps)
                if (s.Daily) mask |= 1 << s.Index;
            return (byte)mask;
        }
    }
}
