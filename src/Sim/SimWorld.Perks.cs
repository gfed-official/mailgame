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
                if (AtPersonalCap(p) && TeamPerkLeftFor(p)) return OpResult.PerkCapped;
            }
            return perk.ConflictsWithAny(_teamPerks) ? OpResult.PerkExcluded : OpResult.Ok;
        }

        /// <summary>Doc 01 §4.2: the personal perks a carrier may hold before a pick must be a team perk.</summary>
        public int PersonalPerkCap => Content.Balance.GetInt("personalPerkCap");

        /// <summary>The carrier holds as many personal perks as the cap allows (every stack counts).</summary>
        public bool AtPersonalCap(Player p) => p.Perks.Count >= PersonalPerkCap;

        /// <summary>
        /// Some team perk of the pool for the next shift is still open to this carrier. Only then does the cap close
        /// personal perks to them: with no team perk left, the 6th pick may be personal (design choice 155).
        /// </summary>
        private bool TeamPerkLeftFor(Player p)
        {
            foreach (var perk in Content.Perks)
                if (perk.Team && InPool(perk, Run.Shift + 1) && CanTake(p, perk) == OpResult.Ok) return true;
            return false;
        }

        /// <summary>The perk may be dealt for this shift: its shift and rank are reached.</summary>
        private bool InPool(PerkDef perk, int shift) => perk.ShiftMin <= shift && perk.RankMin <= 1;

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
            foreach (var p in _players) p.RerollsUsed = 0;
            _perkRng = new RngStream(Settings.Seed, "perks");
        }

        // --- the Draft --------------------------------------------------------------------------

        private const int RarityCount = 3;

        private RngStream? _perkRng;
        private readonly List<PerkDef> _offer = new List<PerkDef>();
        private readonly List<PerkDef> _candidates = new List<PerkDef>();
        private readonly int[] _rarityCount = new int[RarityCount];
        private readonly int[] _rarityWeight = new int[RarityCount];

        /// <summary>The shared cards of the Draft under way; empty outside it.</summary>
        public IReadOnlyList<PerkDef> DraftOffer => _offer;

        /// <summary>The cards before this player: their own after a reroll, else the shared offer.</summary>
        public IReadOnlyList<PerkDef> OfferFor(Player p) => p.Offer ?? (IReadOnlyList<PerkDef>)_offer;

        /// <summary>
        /// Rerolls a run gives a player (doc 01 §2.5, doc 11 §10: one, two from the Postal Rank
        /// of doc 01 §5.2's "second perk reroll").
        /// </summary>
        public int RerollsAllowed(Player p)
        {
            var b = Content.Balance;
            return Math.Max(0, b.GetInt("rerollsPerRun") + (p.Rank >= b.GetInt("rerollSecondRank") ? 1 : 0));
        }

        public int RerollsLeft(Player p) => Math.Max(0, RerollsAllowed(p) - p.RerollsUsed);

        /// <summary>
        /// Doc 01 §2.5: between a Payday the run survives and the next shift everybody takes one
        /// card of a shared offer. Returns false when there is no Draft to hold: the server has
        /// none, or no perk is left that anybody here could take.
        /// </summary>
        private bool BeginDraft()
        {
            if (!Settings.Draft || Settings.QuickStart) return false;
            RollOffer(_offer, Run.Shift + 1, null, null);
            if (_offer.Count == 0) return false;
            // The cards are dealt between shifts: nothing of the fight is left on the street.
            ClearCombat();
            foreach (var p in _players)
            {
                p.DraftPick = -1;
                p.Offer = null;
                DealIfCapped(p);
                // The same cards to everybody (but a capped carrier with none open), each with the rerolls they have left.
                Events.Add(OfferEvent(p, reroll: false));
            }
            EnterPhase(RunPhase.Draft, Content.Balance.GetDouble("draftSeconds"));
            return true;
        }

        /// <summary>
        /// Draws the cards for the Draft before <paramref name="shift"/>: for each card first a
        /// rarity by the weights (Rare gains weight with every shift), then one of the perks of
        /// that rarity that may still be offered.
        /// </summary>
        private void RollOffer(List<PerkDef> offer, int shift, Player? only, IReadOnlyList<PerkDef>? avoid)
        {
            var b = Content.Balance;
            var rng = _perkRng ??= new RngStream(Settings.Seed, "perks");
            int cards = b.GetInt("draftCards");
            double[] weights = b.GetArray("draftRarityWeights");
            for (int r = 0; r < RarityCount; r++)
                _rarityWeight[r] = r < weights.Length ? Math.Max(0, (int)Math.Round(weights[r])) : 0;
            _rarityWeight[(int)PerkRarity.Rare] += Math.Max(0, b.GetInt("draftRareWeightPerShift") * (shift - 2));

            offer.Clear();
            while (offer.Count < cards)
            {
                _candidates.Clear();
                Array.Clear(_rarityCount, 0, RarityCount);
                foreach (var perk in Content.Perks)
                {
                    if (!Offerable(perk, shift, offer, only, avoid)) continue;
                    _candidates.Add(perk);
                    _rarityCount[(int)perk.Rarity]++;
                }
                if (_candidates.Count == 0) break;

                int total = 0;
                for (int r = 0; r < RarityCount; r++)
                    if (_rarityCount[r] > 0) total += _rarityWeight[r];
                if (total <= 0)
                {
                    offer.Add(_candidates[rng.NextInt(_candidates.Count)]);
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
                    offer.Add(perk);
                    break;
                }
            }
        }

        /// <summary>
        /// A perk may be a card when its prerequisites are met, it is not on the table already and
        /// excludes no card that is, and somebody who is here could take it — for a reroll, the
        /// one who asked, and none of the cards they had. The perk pool is not rank-locked yet:
        /// every perk asks rank 1.
        /// </summary>
        private bool Offerable(PerkDef perk, int shift, List<PerkDef> offer, Player? only, IReadOnlyList<PerkDef>? avoid)
        {
            if (!InPool(perk, shift)) return false;
            foreach (var card in offer)
                if (ReferenceEquals(card, perk) || perk.ConflictsWith(card)) return false;
            if (avoid != null)
                foreach (var card in avoid)
                    if (ReferenceEquals(card, perk)) return false;
            if (only != null) return CanTake(only, perk) == OpResult.Ok;
            foreach (var p in _players)
                if (!p.Away && CanTake(p, perk) == OpResult.Ok) return true;
            return false;
        }

        /// <summary>
        /// Doc 01 §4.2: "the 6th pick must be a team perk if any is available". A carrier at the cap whose shared cards
        /// are all personal (or otherwise closed to them) is dealt cards of their own, which the cap fills with team
        /// perks; free, not a reroll. With no team perk left there is nothing to deal, and the cap gives way.
        /// </summary>
        private void DealIfCapped(Player p)
        {
            if (p.Away || !AtPersonalCap(p) || FirstOpenCard(p) >= 0) return;
            var cards = new List<PerkDef>();
            RollOffer(cards, Run.Shift + 1, p, null);
            if (cards.Count > 0) p.Offer = cards;
        }

        private int FirstOpenCard(Player p)
        {
            var offer = OfferFor(p);
            for (int i = 0; i < offer.Count; i++)
                if (CanTake(p, offer[i]) == OpResult.Ok) return i;
            return -1;
        }

        private DraftOfferEvent OfferEvent(Player p, bool reroll)
        {
            var e = new DraftOfferEvent { Audience = p.Id, Reroll = reroll, RerollsLeft = (byte)Math.Min(RerollsLeft(p), byte.MaxValue) };
            foreach (var perk in OfferFor(p)) e.Perks.Add(perk.Index);
            return e;
        }

        /// <summary>
        /// Doc 01 §2.5: "one free reroll per player per run". Before choosing, the player gets
        /// three new cards of their own — none of those they had, all open to them — and the
        /// others keep theirs. It draws from the Draft's perk stream like the shared cards.
        /// </summary>
        private OpResult RerollOp(Player player)
        {
            if (Run.Phase != RunPhase.Draft) return OpResult.WrongPhase;
            if (player.DraftPick >= 0) return OpResult.AlreadyPicked;
            if (RerollsLeft(player) <= 0) return OpResult.NoRerolls;
            var cards = new List<PerkDef>();
            RollOffer(cards, Run.Shift + 1, player, OfferFor(player));
            if (cards.Count == 0) return OpResult.NoNewCards;
            player.RerollsUsed++;
            player.Offer = cards;
            Events.Add(OfferEvent(player, reroll: true));
            return OpResult.Ok;
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
            if (r.Count >= OfferFor(player).Count) return OpResult.BadRequest;
            return TakeCard(player, r.Count, auto: false);
        }

        private OpResult TakeCard(Player p, int card, bool auto)
        {
            var verdict = Grant(p, OfferFor(p)[card], (byte)card, auto);
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
            foreach (var p in _players)
            {
                p.DraftPick = -1;
                p.Offer = null;
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
