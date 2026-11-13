using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PerformativeMail.Client.Meta
{
    /// <summary>One line of <see cref="MetaProfile.RecentRuns"/> (doc 08 §3.8).</summary>
    public sealed class RecentRun
    {
        public string SeedString = "";
        public int ShiftsCompleted;
        public bool Victory;
        public int Score;
        public int Players;
        /// <summary>Local date the run ended, "yyyy-MM-dd".</summary>
        public string Date = "";
    }

    /// <summary>
    /// What a player keeps between runs (doc 07 §7.1, doc 08 §3.8): Postal Rank XP, lifetime stats
    /// and the last ten runs. It lives on the player's own machine; the server never sees it.
    /// Unlocks, cosmetics and settings are in the file format but nothing fills them yet.
    /// </summary>
    public sealed class MetaProfile
    {
        public const int MaxRecentRuns = 10;

        public string ProfileId = "";
        public string DisplayName = "";
        public int RankXp;
        public int Runs, Wins, Deliveries, Misdeliveries, BestScore;
        /// <summary>Newest first.</summary>
        public readonly List<RecentRun> RecentRuns = new List<RecentRun>();

        public static MetaProfile Create(string displayName) =>
            new MetaProfile { ProfileId = "local:" + Guid.NewGuid().ToString("N"), DisplayName = displayName };

        /// <summary>Rank 1 is where everybody starts; each <paramref name="xpPerRank"/> XP is one rank more (doc 01 §5.1).</summary>
        public int Rank(int xpPerRank) => 1 + RankXp / Math.Max(1, xpPerRank);

        /// <summary>XP earned inside the current rank, 0 .. <paramref name="xpPerRank"/> − 1.</summary>
        public int XpIntoRank(int xpPerRank) => RankXp % Math.Max(1, xpPerRank);

        public void AddRun(RecentRun run)
        {
            RecentRuns.Insert(0, run);
            if (RecentRuns.Count > MaxRecentRuns) RecentRuns.RemoveRange(MaxRecentRuns, RecentRuns.Count - MaxRecentRuns);
        }

        public string ToJson()
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("profileId", ProfileId);
                w.WriteString("displayName", DisplayName);
                w.WriteNumber("rankXp", RankXp);
                w.WriteStartObject("unlocks");
                foreach (string list in new[] { "kits", "archetypes", "perkPools", "cosmetics" })
                {
                    w.WriteStartArray(list);
                    w.WriteEndArray();
                }
                w.WriteNumber("stampTiers", 1);
                w.WriteEndObject();
                w.WriteStartObject("cosmetics");
                w.WriteEndObject();
                w.WriteStartObject("stats");
                w.WriteNumber("runs", Runs);
                w.WriteNumber("wins", Wins);
                w.WriteNumber("deliveries", Deliveries);
                w.WriteNumber("misdeliveries", Misdeliveries);
                w.WriteNumber("bestScore", BestScore);
                w.WriteEndObject();
                w.WriteStartArray("recentRuns");
                foreach (var run in RecentRuns)
                {
                    w.WriteStartObject();
                    w.WriteString("seedString", run.SeedString);
                    w.WriteNumber("shiftsCompleted", run.ShiftsCompleted);
                    w.WriteBoolean("victory", run.Victory);
                    w.WriteNumber("score", run.Score);
                    w.WriteNumber("players", run.Players);
                    w.WriteString("date", run.Date);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartObject("settings");
                w.WriteEndObject();
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Reads a profile; missing fields keep their defaults. Throws <see cref="FormatException"/> on a file that is not a profile.</summary>
        public static MetaProfile FromJson(string json)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (JsonException e) { throw new FormatException("profile: " + e.Message, e); }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new FormatException("profile: not a JSON object");
                var profile = new MetaProfile
                {
                    ProfileId = Str(root, "profileId"),
                    DisplayName = Str(root, "displayName"),
                    RankXp = Int(root, "rankXp"),
                };
                if (profile.ProfileId.Length == 0) throw new FormatException("profile: no profileId");
                if (root.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
                {
                    profile.Runs = Int(stats, "runs");
                    profile.Wins = Int(stats, "wins");
                    profile.Deliveries = Int(stats, "deliveries");
                    profile.Misdeliveries = Int(stats, "misdeliveries");
                    profile.BestScore = Int(stats, "bestScore");
                }
                if (root.TryGetProperty("recentRuns", out var runs) && runs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in runs.EnumerateArray())
                    {
                        if (r.ValueKind != JsonValueKind.Object || profile.RecentRuns.Count >= MaxRecentRuns) continue;
                        profile.RecentRuns.Add(new RecentRun
                        {
                            SeedString = Str(r, "seedString"),
                            ShiftsCompleted = Int(r, "shiftsCompleted"),
                            Victory = r.TryGetProperty("victory", out var v) && v.ValueKind == JsonValueKind.True,
                            Score = Int(r, "score"),
                            Players = Int(r, "players"),
                            Date = Str(r, "date"),
                        });
                    }
                }
                return profile;
            }
        }

        private static string Str(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        /// <summary>Counters never go below zero, whatever the file says.</summary>
        private static int Int(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? Math.Max(0, n) : 0;
    }
}
