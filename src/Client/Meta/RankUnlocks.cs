using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Meta
{
    /// <summary>What a Postal Rank has opened (doc 01 §5.2, doc 08 §3.8 "unlocks"), as the profile file keeps it.</summary>
    public sealed class ProfileUnlocks
    {
        public List<string> Kits = new List<string>();
        public List<string> Archetypes = new List<string>();
        public int StampTiers;
        public List<string> PerkPools = new List<string>();
        public List<string> Cosmetics = new List<string>();
        /// <summary>Draft rerolls a run (doc 01 §5.2 rank 4: the second).</summary>
        public int Rerolls;
    }

    /// <summary>
    /// Doc 01 §5.2's unlock tracks, read from where the content already keeps them rather than a table of their own
    /// (design choice 167): the kits' ranks, the stamps' tier ranks, the perks' rankMin by category, the second reroll's
    /// rank. "Ranks unlock content, never stats."
    /// </summary>
    public static class RankUnlocks
    {
        private static readonly PerkCategory[] PoolOrder = { PerkCategory.Carrier, PerkCategory.Postal, PerkCategory.Facility, PerkCategory.Defense };

        public static ProfileUnlocks For(ContentDb content, int rank)
        {
            var u = new ProfileUnlocks();
            foreach (var kit in content.Kits)
                if (kit.Rank <= rank) u.Kits.Add(kit.Id);
            // The Small Island is everybody's; the Large Island from rank 3 (doc 01 §5.2).
            u.Archetypes.Add(WorldGenerator.SmallIsland);
            if (rank >= Sim.SimWorld.WorldRank(content, LargeIslandGenerator.LargeIsland)) u.Archetypes.Add(LargeIslandGenerator.LargeIsland);
            u.StampTiers = Sim.Run.StampRules.TierOpen(content.Balance, rank, false);
            foreach (var category in PoolOrder)
            {
                int open = int.MaxValue;
                foreach (var perk in content.Perks)
                    if (perk.Category == category) open = Math.Min(open, perk.RankMin);
                if (open <= rank) u.PerkPools.Add(category.ToString().ToLowerInvariant());
            }
            u.Rerolls = content.Balance.GetInt("rerollsPerRun") + (rank >= content.Balance.GetInt("rerollSecondRank") ? 1 : 0);
            return u;
        }

        /// <summary>
        /// What rising from <paramref name="before"/> to <paramref name="after"/> opened, in words for the Results card
        /// (doc 09 §2.11 "unlock reveals"): "the Sea kit", "Postage Stamps tier 2", "facility perks in the Draft",
        /// "a second Draft reroll".
        /// </summary>
        public static List<string> Revealed(ContentDb content, int before, int after)
        {
            var was = For(content, before);
            var now = For(content, after);
            var words = new List<string>();
            foreach (var kit in content.Kits)
                if (now.Kits.Contains(kit.Id) && !was.Kits.Contains(kit.Id)) words.Add($"the {kit.Name} kit");
            if (now.Archetypes.Contains(LargeIslandGenerator.LargeIsland) && !was.Archetypes.Contains(LargeIslandGenerator.LargeIsland)) words.Add("the Large Island");
            for (int t = was.StampTiers + 1; t <= now.StampTiers; t++) words.Add($"Postage Stamps tier {t}");
            foreach (string pool in now.PerkPools)
                if (!was.PerkPools.Contains(pool)) words.Add($"{pool} perks in the Draft");
            if (now.Rerolls > was.Rerolls) words.Add(now.Rerolls == 2 ? "a second Draft reroll" : $"{now.Rerolls} Draft rerolls");
            return words;
        }
    }
}
