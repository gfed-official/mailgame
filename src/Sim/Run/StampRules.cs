using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.Run
{
    /// <summary>The Postage Stamps' tiers (doc 01 §5.2), as the server and the host's client reckon them.</summary>
    public static class StampRules
    {
        /// <summary>The highest tier open to a host of this Postal Rank: each tier opens at its rank of <c>stampTierRanks</c>.</summary>
        public static int TierOpen(Balance balance, int rank, bool all)
        {
            double[] ranks = balance.GetArray("stampTierRanks");
            if (all) return ranks.Length;
            int tier = 0;
            for (int i = 0; i < ranks.Length; i++)
                if (rank >= ranks[i]) tier = i + 1;
            return tier;
        }
    }
}
