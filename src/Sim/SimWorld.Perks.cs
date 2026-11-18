using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

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

        public static int Stacks(Player p, PerkDef perk) => perk.StacksIn(p.Perks);

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
                    if (perk.ConflictsWithAny(other.Perks)) return OpResult.PerkExcluded;
            }
            else
            {
                if (Stacks(p, perk) >= perk.MaxStacks) return OpResult.PerkMaxed;
                if (perk.ConflictsWithAny(p.Perks)) return OpResult.PerkExcluded;
            }
            return perk.ConflictsWithAny(_teamPerks) ? OpResult.PerkExcluded : OpResult.Ok;
        }

        /// <summary>
        /// Gives the perk if <see cref="CanTake"/> allows it: a team perk to everybody, a personal
        /// one to <paramref name="p"/>. It acts from this tick on; what was sampled before (the
        /// quota of a Delivery under way, a raid already scheduled) stays as it was.
        /// </summary>
        public OpResult GrantPerk(Player p, PerkDef perk) => Grant(p, perk, PerkPickedEvent.NoCard, auto: false);

        private OpResult Grant(Player p, PerkDef perk, byte card, bool auto)
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
            Events.Add(new PerkPickedEvent { Player = p.Id, Perk = perk.Index, Card = card, Auto = auto });
            return OpResult.Ok;
        }

        /// <summary>A run starts and ends without perks, and draws its cards from the seed anew.</summary>
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
            CloseDraft();
            _perkRng = new RngStream(Settings.Seed, "perks");
        }

        // --- the Draft --------------------------------------------------------------------------

        private const int RarityCount = 3;

        private RngStream? _perkRng;
        private readonly List<PerkDef> _offer = new List<PerkDef>();
        private readonly List<PerkDef> _candidates = new List<PerkDef>();
        private readonly int[] _rarityCount = new int[RarityCount];
        private readonly int[] _rarityWeight = new int[RarityCount];

        /// <summary>The cards of the Draft under way, the same for everybody; empty outside it.</summary>
        public IReadOnlyList<PerkDef> DraftOffer => _offer;

        /// <summary>
        /// Doc 01 §2.5: between a Payday the run survives and the next shift everybody takes one
        /// card of a shared offer. Returns false when there is no Draft to hold: the server has
        /// none, or no perk is left that anybody here could take.
        /// </summary>
        private bool BeginDraft()
        {
            if (!Settings.Draft || Settings.QuickStart) return false;
            RollOffer(Run.Shift + 1);
            if (_offer.Count == 0) return false;
            // The cards are dealt between shifts: nothing of the fight is left on the street.
            ClearCombat();
            foreach (var p in _players) p.DraftPick = -1;
            var offer = new DraftOfferEvent();
            foreach (var perk in _offer) offer.Perks.Add(perk.Index);
            Events.Add(offer);
            EnterPhase(RunPhase.Draft, Content.Balance.GetDouble("draftSeconds"));
            return true;
        }

        /// <summary>
        /// Draws the cards for the Draft before <paramref name="shift"/>: for each card first a
        /// rarity by the weights (Rare gains weight with every shift), then one of the perks of
        /// that rarity that may still be offered.
        /// </summary>
        private void RollOffer(int shift)
        {
            var b = Content.Balance;
            var rng = _perkRng ??= new RngStream(Settings.Seed, "perks");
            int cards = b.GetInt("draftCards");
            double[] weights = b.GetArray("draftRarityWeights");
            for (int r = 0; r < RarityCount; r++)
                _rarityWeight[r] = r < weights.Length ? Math.Max(0, (int)Math.Round(weights[r])) : 0;
            _rarityWeight[(int)PerkRarity.Rare] += Math.Max(0, b.GetInt("draftRareWeightPerShift") * (shift - 2));

            _offer.Clear();
            while (_offer.Count < cards)
            {
                _candidates.Clear();
                Array.Clear(_rarityCount, 0, RarityCount);
                foreach (var perk in Content.Perks)
                {
                    if (!Offerable(perk, shift)) continue;
                    _candidates.Add(perk);
                    _rarityCount[(int)perk.Rarity]++;
                }
                if (_candidates.Count == 0) break;

                int total = 0;
                for (int r = 0; r < RarityCount; r++)
                    if (_rarityCount[r] > 0) total += _rarityWeight[r];
                if (total <= 0)
                {
                    _offer.Add(_candidates[rng.NextInt(_candidates.Count)]);
                    continue;
                }
                int roll = rng.NextInt(total);
                int rarity = 0;
                for (; rarity < RarityCount - 1; rarity++)
                {
                    if (_rarityCount[rarity] == 0) continue;
                    if (roll < _rarityWeight[rarity]) break;
                    roll -= _rarityWeight[rarity];
                }
                int nth = rng.NextInt(_rarityCount[rarity]);
                foreach (var perk in _candidates)
                {
                    if ((int)perk.Rarity != rarity || nth-- > 0) continue;
                    _offer.Add(perk);
                    break;
                }
            }
        }

        /// <summary>
        /// A perk may be a card when its prerequisites are met, it is not on the table already and
        /// excludes no card that is, and somebody who is here could take it. The server is not
        /// told anybody's Postal Rank yet: every carrier counts as rank 1.
        /// </summary>
        private bool Offerable(PerkDef perk, int shift)
        {
            if (perk.ShiftMin > shift || perk.RankMin > 1) return false;
            foreach (var card in _offer)
                if (ReferenceEquals(card, perk) || perk.ConflictsWith(card)) return false;
            foreach (var p in _players)
                if (!p.Away && CanTake(p, perk) == OpResult.Ok) return true;
            return false;
        }

        private int FirstOpenCard(Player p)
        {
            for (int i = 0; i < _offer.Count; i++)
                if (CanTake(p, _offer[i]) == OpResult.Ok) return i;
            return -1;
        }

        /// <summary>Everybody who is here has taken a card, or has none left to take.</summary>
        private bool DraftSettled()
        {
            if (PresentCount == 0) return false;
            foreach (var p in _players)
                if (!p.Away && p.DraftPick < 0 && FirstOpenCard(p) >= 0) return false;
            return true;
        }

        private OpResult PickPerkOp(Player player, in SimRequest r)
        {
            if (Run.Phase != RunPhase.Draft) return OpResult.WrongPhase;
            if (player.DraftPick >= 0) return OpResult.AlreadyPicked;
            if (r.Count >= _offer.Count) return OpResult.BadRequest;
            return TakeCard(player, r.Count, auto: false);
        }

        private OpResult TakeCard(Player p, int card, bool auto)
        {
            var verdict = Grant(p, _offer[card], (byte)card, auto);
            if (verdict == OpResult.Ok) p.DraftPick = (sbyte)card;
            return verdict;
        }

        /// <summary>
        /// The Draft is over. Whoever is here and has not chosen is given the first card still
        /// open to them; who is away goes without. Then the next shift begins.
        /// </summary>
        private void EndDraft()
        {
            foreach (var p in _players)
            {
                if (p.Away || p.DraftPick >= 0) continue;
                int card = FirstOpenCard(p);
                if (card >= 0) TakeCard(p, card, auto: true);
            }
            CloseDraft();
            Run.Shift++;
            BeginShift();
        }

        private void CloseDraft()
        {
            _offer.Clear();
            foreach (var p in _players) p.DraftPick = -1;
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
