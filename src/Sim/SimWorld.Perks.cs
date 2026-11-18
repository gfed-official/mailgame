using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Perks (doc 01 §2.5): a team perk is the whole team's once, a personal perk its holder's and
    /// may stack. They last from the pick to the end of the run and act through the stat sheets;
    /// no def is changed.
    /// </summary>
    public sealed partial class SimWorld
    {
        private readonly List<PerkDef> _teamPerks = new List<PerkDef>();
        private readonly StatSheet _teamStats = new StatSheet();

        /// <summary>The team perks of the run, in the order they were taken.</summary>
        public IReadOnlyList<PerkDef> TeamPerks => _teamPerks;

        /// <summary>A stat as this carrier has it: the team's perks and their own over the base.</summary>
        private float StatFor(Player p, Stat stat, float baseValue) => _teamStats.Resolve(stat, baseValue, p.Stats);

        /// <summary>A stat of the team as a whole.</summary>
        private float TeamStat(Stat stat, float baseValue) => _teamStats.Resolve(stat, baseValue);

        private int TeamQuota(int quota) => Math.Max(0, (int)Math.Round(TeamStat(Stat.Quota, quota)));

        public static int Stacks(Player p, PerkDef perk)
        {
            int n = 0;
            foreach (var held in p.Perks) if (ReferenceEquals(held, perk)) n++;
            return n;
        }

        /// <summary>
        /// Whether this carrier may take the perk: a team perk once per run, a personal perk as
        /// often as it stacks, and never next to a perk it excludes: for a team perk anybody's,
        /// for a personal one the team's and the carrier's own.
        /// </summary>
        public OpResult CanTake(Player p, PerkDef perk)
        {
            if (perk.Team)
            {
                if (_teamPerks.Contains(perk)) return OpResult.PerkTaken;
                foreach (var other in _players)
                    if (ConflictsWithAny(perk, other.Perks)) return OpResult.PerkExcluded;
            }
            else
            {
                if (Stacks(p, perk) >= perk.MaxStacks) return OpResult.PerkMaxed;
                if (ConflictsWithAny(perk, p.Perks)) return OpResult.PerkExcluded;
            }
            return ConflictsWithAny(perk, _teamPerks) ? OpResult.PerkExcluded : OpResult.Ok;
        }

        private static bool ConflictsWithAny(PerkDef perk, List<PerkDef> held)
        {
            foreach (var other in held)
                if (!ReferenceEquals(other, perk) && perk.ConflictsWith(other)) return true;
            return false;
        }

        /// <summary>
        /// Gives the perk if <see cref="CanTake"/> allows it: a team perk to everybody, a personal
        /// one to <paramref name="p"/>. It acts from this tick on; what was sampled before (the
        /// quota of a Delivery under way, a raid already scheduled) stays as it was.
        /// </summary>
        public OpResult GrantPerk(Player p, PerkDef perk)
        {
            var verdict = CanTake(p, perk);
            if (verdict != OpResult.Ok) return verdict;
            if (perk.Team)
            {
                _teamPerks.Add(perk);
                _teamStats.Apply(perk);
                foreach (var other in _players) RecomputeWeight(other);
            }
            else
            {
                p.Perks.Add(perk);
                p.Stats.Apply(perk);
                RecomputeWeight(p);
            }
            return OpResult.Ok;
        }

        /// <summary>A run starts and ends without perks.</summary>
        private void ClearPerks()
        {
            _teamPerks.Clear();
            _teamStats.Clear();
            foreach (var p in _players)
            {
                p.Perks.Clear();
                p.Stats.Clear();
                p.BoostUntilTick = 0;
                RecomputeWeight(p);
            }
        }

        /// <summary>A correct delivery starts (or renews) the speed of <see cref="Stat.DeliveryBoost"/>.</summary>
        private void StartDeliveryBoost(Player p)
        {
            uint ticks = SimConstants.SecondsToTicks(StatFor(p, Stat.DeliveryBoostSeconds, 0f));
            if (ticks == 0) return;
            p.BoostUntilTick = Tick + ticks;
            RecomputeWeight(p);
        }
    }
}
