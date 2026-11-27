using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// Postage Stamps (doc 01 §6, doc 08 §2.12; design choice 140): the host picks them in the Lobby;
    /// each multiplies the run's score and adds 10 % to its XP. The first is Cursed Mail (doc 05 §6):
    /// 10 % of the mail spawns cursed, worth half again; misdelivered from the hands, a mini-raid of 4
    /// Barbarians and an Archer comes at that mailbox at once, unannounced.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>The run's stamps, a bit per <see cref="StampDef.Index"/>.</summary>
        public byte Stamps { get; private set; }

        public bool HasStamp(StampDef stamp) => (Stamps & (1 << stamp.Index)) != 0;

        /// <summary>Mini-raids that came of cursed mail since the world began.</summary>
        public int MiniRaids { get; private set; }

        private OpResult SetStampsOp(Player p, in SimRequest r)
        {
            if (Run.Phase != RunPhase.Lobby) return OpResult.WrongPhase;
            if (p.Id != Run.Host) return OpResult.NotHost;
            if ((r.Count >> Content.Stamps.Count) != 0 || r.Count > byte.MaxValue) return OpResult.BadRequest;
            // Doc 01 §5.2: a stamp of a tier the host's Postal Rank has not opened is not theirs to put on.
            int tier = StampTierOpen(p);
            foreach (var s in Content.Stamps)
                if ((r.Count & (1 << s.Index)) != 0 && (Stamps & (1 << s.Index)) == 0 && s.Tier > tier) return OpResult.ShopLocked;
            SetStamps((byte)r.Count);
            return OpResult.Ok;
        }

        /// <summary>
        /// The highest tier of the Postage Stamps open to a host (doc 01 §5.2: tier 1 from the start, tier 2 from
        /// Postal Rank 6, tier 3 from 12; <c>stampTierRanks</c> in the balance); every tier with --playtest-stamps.
        /// </summary>
        public int StampTierOpen(Player host) => StampRules.TierOpen(Content.Balance, host.Rank, (host.Playtest & PlaytestFlags.AllStamps) != 0);

        /// <summary>The run's stamps, as the host's request sets them (and tests).</summary>
        public void SetStamps(byte mask)
        {
            Stamps = mask;
            ApplyStamps();
            RebuildTeamSheet();
        }

        /// <summary>The cursed part of the mail and its worth, for the spawner (a run's start and a change of the stamps).</summary>
        private void ApplyStamps()
        {
            var curse = CursedStamp();
            Spawner.CursedPermille = curse == null ? 0 : (int)MathF.Round(curse.CursedChance * 1000f);
            Spawner.CursedValueMult = curse?.CursedValueMult ?? 1f;
            Spawner.HeavyLoad = HasStampRule(StampRule.HeavyLoad);
        }

        /// <summary>A stamp of the run has the rule.</summary>
        public bool HasStampRule(StampRule rule)
        {
            foreach (var s in Content.Stamps)
                if (HasStamp(s) && s.Has(rule)) return true;
            return false;
        }

        /// <summary>The run's stamp with the Cursed Mail rule; null if none is on.</summary>
        public StampDef? CursedStamp()
        {
            foreach (var s in Content.Stamps)
                if (HasStamp(s) && s.Has(StampRule.CursedMail)) return s;
            return null;
        }

        /// <summary>Doc 01 §6: the run's earnings times the product of its stamps' multipliers.</summary>
        private int StampedScore(int earned)
        {
            double mult = 1.0;
            foreach (var s in Content.Stamps)
                if (HasStamp(s)) mult *= s.ScoreMult;
            return earned <= 0 ? earned : (int)Math.Round(earned * mult);
        }

        /// <summary>Doc 11: "Stamp XP bonus +10% per active stamp".</summary>
        private int StampedXp(int xp)
        {
            int n = 0;
            foreach (var s in Content.Stamps)
                if (HasStamp(s)) n++;
            return (int)Math.Round(xp * (1.0 + n * Content.Balance.GetDouble("stampXpBonus")));
        }

        /// <summary>
        /// Doc 05 §6: the raiders of the curse round the mailbox, 3 m out on open ground, coming at once.
        /// They are no raid: nothing is announced, and they flee when Delivery ends as raiders do.
        /// </summary>
        private void SpawnMiniRaid(DestinationDef dest, Player player, MailItem item)
        {
            var curse = CursedStamp();
            if (curse == null) return;
            int total = 0;
            foreach (var (_, count) in curse.MiniRaid) total += count;
            int k = 0, spawned = 0;
            foreach (var (enemy, count) in curse.MiniRaid)
                for (int i = 0; i < count; i++, k++)
                {
                    float a = k * (2f * MathF.PI) / Math.Max(1, total);
                    float x = dest.MailboxX, z = dest.MailboxZ;
                    for (float r = 3f; r >= 0.5f; r -= 0.5f)
                    {
                        float tx = dest.MailboxX + MathF.Sin(a) * r, tz = dest.MailboxZ + MathF.Cos(a) * r;
                        if (Map.IsSolid((int)(tx / SimConstants.TileSize), (int)(tz / SimConstants.TileSize))) continue;
                        (x, z) = (tx, tz);
                        break;
                    }
                    if (SpawnEnemy(enemy, x, z) != null) spawned++;
                }
            MiniRaids++;
            Events.Add(new CursedRaidEvent { DestId = (ushort)dest.Id, Player = player.Id, Kind = item.Kind, Raiders = (byte)Math.Min(spawned, byte.MaxValue) });
        }
    }
}
