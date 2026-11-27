using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Content;
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

        /// <summary>Jumps off the ground this carrier may make (doc 10 §4.1 Bunny Hop).</summary>
        private int AirJumps(Player p) => Math.Max(0, (int)MathF.Round(StatFor(p, Stat.AirJumps, 0f)));

        /// <summary>The speed of a vehicle this carrier pedals on land, as a share (doc 10 §4.1 Bike Courier).</summary>
        private float BikeMult(Player p) => StatFor(p, Stat.BikeSpeed, 1f);

        /// <summary>
        /// The grids the perks make bigger, a row at a time below the def's (doc 10 §4.1 Big Pockets, Bike Courier; §4.2
        /// Bulk Depot): every carrier's inventory, every bike's cargo, every built chest and depot. Called when a perk is
        /// taken or the perks are cleared, and when such a grid is made. A grid with something in the rows it would
        /// lose keeps them until they are empty and this is called again (design choice 158).
        /// </summary>
        private void ApplyContainerRows()
        {
            int cargo = 0;
            foreach (var p in _players)
            {
                ResizeContainer(p.Inventory, Math.Max(0, (int)MathF.Round(StatFor(p, Stat.InventoryRows, 0f))));
                cargo = Math.Max(cargo, (int)MathF.Round(StatFor(p, Stat.BikeCargoRows, 0f)));
            }
            foreach (var v in _vehicles)
                if (!v.Def.Fuelled && !v.Def.OnWater) ResizeContainer(v.Cargo, cargo);
            float storage = TeamStat(Stat.StorageRows, 1f);
            foreach (var c in Constructs.All)
                if (c.ContainerId != 0 && c.Def.Behaviour == BuildingBehaviour.Container && GetContainer(c.ContainerId) is { } grid)
                    ResizeContainer(grid, Math.Max(0, (int)MathF.Round(grid.Def.Rows * (storage - 1f))));
        }

        /// <summary>A grid to so many rows over its def's; who has it open is sent it whole.</summary>
        private void ResizeContainer(GridContainer grid, int extraRows)
        {
            if (grid.ExtraRows == extraRows || !grid.Resize(extraRows)) return;
            if (!_containers.TryGetValue(grid.Id, out var info)) return;
            foreach (var viewer in info.Viewers) Events.Add(new ContainerStateEvent { Audience = viewer, State = grid.Snapshot() });
        }

        /// <summary>A land vehicle's speed on a road, as a share (doc 01 §6 No Roads).</summary>
        private float RoadMult => TeamStat(Stat.VehicleRoadSpeed, 1f);
        /// <summary>A boat's speed on the water as a share, the team's (doc 10 §6 Sea Fog).</summary>
        private float BoatMult => TeamStat(Stat.BoatSpeed, 1f);

        /// <summary>A stat of the team as a whole, its stamps' and perks' (tests and tools).</summary>
        public float TeamStatOf(Stat stat, float baseValue) => TeamStat(stat, baseValue);

        /// <summary>A stat of the team as a whole.</summary>
        private float TeamStat(Stat stat, float baseValue) => _teamStats.Resolve(stat, baseValue);

        /// <summary>The head count the quota is reckoned for: those here and the stamps' more (doc 01 §6 Skeleton Crew: n + 2).</summary>
        private int QuotaHeads(int present) => present + Math.Max(0, (int)MathF.Round(TeamStat(Stat.QuotaPlayers, 0f)));

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

        /// <summary>The perk may be dealt for this shift: its shift and rank are reached, and what it builds on is there.</summary>
        private bool InPool(PerkDef perk, int shift) => perk.ShiftMin <= shift && perk.RankMin <= PerkPoolRank && PrerequisitesMet(perk);

        /// <summary>
        /// The Postal Rank the run's perk pool is opened for (doc 01 §5.2: the facility perks from rank 5, the defense
        /// perks from 8): the host's as the run is set up, as the stamps and the archetypes follow the host; every rank
        /// for a host with --playtest-stamps. Kept for the run, so a host handing over does not change the pool
        /// (design choice 164).
        /// </summary>
        public int PerkPoolRank { get; private set; } = 1;

        private void SetPerkPoolRank()
        {
            var host = GetPlayer(Run.Host);
            PerkPoolRank = host == null ? 1 : (host.Playtest & PlaytestFlags.AllUnlocks) != 0 ? int.MaxValue : host.Rank;
        }

        /// <summary>
        /// Doc 01 §2.5 "filtered by prerequisites": one of the buildings standing, one of the blueprints owned, one of
        /// the vehicles in the world, for each list the perk names.
        /// </summary>
        public bool PrerequisitesMet(PerkDef perk)
        {
            if (perk.BlueprintAny.Count > 0)
            {
                bool any = false;
                foreach (string id in perk.BlueprintAny) any |= OwnsBlueprint(id);
                if (!any) return false;
            }
            if (perk.VehicleAny.Count > 0)
            {
                bool any = false;
                foreach (var v in _vehicles) any |= Contains(perk.VehicleAny, v.Def.Id);
                if (!any) return false;
            }
            if (perk.BuiltAny.Count > 0)
            {
                bool any = false;
                foreach (var c in Constructs.All) any |= Contains(perk.BuiltAny, c.Def.Id);
                if (!any) return false;
            }
            return true;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) return true;
            return false;
        }

        /// <summary>Shifts before the map's that its districts open (doc 10 §4.3 Zoning Board); the raiders' ways in keep theirs.</summary>
        public int DistrictsEarly => Math.Max(0, (int)MathF.Round(TeamStat(Stat.DistrictsEarly, 0f)));

        /// <summary>
        /// The team's sheet anew: the run's stamps' modifiers (doc 01 §6: Rush Hour's −30 s, Megamail's ×2, …), then
        /// its team perks; and what the sheet sets that is kept. A change of the stamps and a run's start call it.
        /// </summary>
        private void RebuildTeamSheet()
        {
            _teamStats.Clear();
            foreach (var s in Content.Stamps)
                if (HasStamp(s)) _teamStats.Apply(s.Modifiers);
            foreach (var perk in _teamPerks) _teamStats.Apply(perk);
            // Doc 10 §6: a live run event's numbers (the Inspector's −10 %, Mega Migration's ×3) lie on the sheet too.
            foreach (var e in Content.RunEvents)
                if (EventLive(e)) _teamStats.Apply(e.Modifiers);
            ApplyTeamStats();
        }

        /// <summary>
        /// Doc 10 §4.3 Forecast Desk: as Prep begins, everybody is told about how much mail the coming Delivery brings
        /// each street, reckoned as Delivery will reckon its spawn (the quota of those here now, the team's perks).
        /// </summary>
        private void SendForecast()
        {
            if (!TeamHas(RuleFlag.ForecastDesk) || Map.Debug != null) return;
            int quota = QuotaFormula.Quota(Content.Balance, Run.Shift, QuotaHeads(Math.Max(1, PresentCount)));
            int value = (int)Math.Round(quota * Content.MailMix.SpawnOverhead * TeamStat(Stat.SpawnValue, 1f));
            Spawner.DistrictsEarly = DistrictsEarly;
            var e = new ForecastEvent { Shift = (byte)Run.Shift };
            var streets = new List<(int Street, double Count)>();
            foreach (var kv in Spawner.Forecast(Run.Shift, value)) streets.Add((kv.Key, kv.Value));
            streets.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : a.Street.CompareTo(b.Street));
            foreach (var (street, count) in streets)
            {
                int n = (int)Math.Round(count);
                if (n > 0) e.Streets.Add(((ushort)street, (ushort)Math.Min(n, ushort.MaxValue)));
            }
            LastForecast = e;
            Events.Add(e);
        }

        /// <summary>The forecast sent as this shift's Prep began; null without Forecast Desk.</summary>
        public ForecastEvent? LastForecast { get; private set; }

        /// <summary>Doc 10 §4.3 Priority Post: the spawner's part of Priority mail, its worth and its time, from the balance.</summary>
        private void ApplyPriorityPost()
        {
            var b = Content.Balance;
            bool on = TeamHas(RuleFlag.PriorityPost);
            Spawner.PriorityPermille = on ? (int)MathF.Round(b.GetFloat("priorityMailChance") * 1000f) : 0;
            Spawner.PriorityValueMult = b.GetFloat("priorityValueMult");
            Spawner.PriorityTicks = SimConstants.SecondsToTicks(b.GetDouble("priorityMailSeconds"));
        }

        /// <summary>The filter slots of an output of a building of this def a carrier may set (doc 10 §4.2 Extra Labels).</summary>
        public int FilterSlotsOpen(Content.BuildingDef def) =>
            Math.Clamp((int)MathF.Round(TeamStat(Stat.SorterFilterSlots, def.FilterSlotsOpen)), 1, def.FilterSlots);

        /// <summary>Doc 10 §4.3 Bulk Mailer: a stack of letters or postcards holds this many times its kind's.</summary>
        public int MailStackMult => Math.Max(1, (int)MathF.Round(TeamStat(Stat.PaperMailStack, 1f)));

        /// <summary>A team perk of the run holds the rule flag (ADR-0010 RuleFlag).</summary>
        public bool TeamHas(RuleFlag flag) => _teamStats.Has(flag);

        /// <summary>
        /// What the team's stats set that is kept rather than read at its use: every construct's most HP. Called when a
        /// team perk is taken and when the run's perks are cleared.
        /// </summary>
        private void ApplyTeamStats()
        {
            ApplyPriorityPost();
            int mult = MailStackMult;
            foreach (var info in _containers.Values) info.Container.MailStackMult = mult;
            float scale = TeamStat(Stat.ConstructHp, 1f);
            if (Math.Abs(scale - Constructs.HpScale) < 1e-6f) return;
            foreach (var (c, hp) in Constructs.Rescale(scale))
                Events.Add(new ConstructDamagedEvent { X = (byte)c.Tx, Y = (byte)c.Tz, Hp = (ushort)hp });
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
                ApplyTeamStats();
                ApplyContainerRows();
                foreach (var other in _players) RecomputeWeight(other);
            }
            else
            {
                p.Perks.Add(perk);
                p.Stats.Apply(perk);
                ApplyContainerRows();
                RecomputeWeight(p);
            }
            Events.Add(new PerkPickedEvent { Player = p.Id, Perk = perk.Index, Card = card, Auto = auto });
            return OpResult.Ok;
        }

        /// <summary>A run starts and ends without perks, and draws its cards from the seed anew.</summary>
        private void ClearPerks()
        {
            _teamPerks.Clear();
            RebuildTeamSheet();
            foreach (var p in _players)
            {
                p.Perks.Clear();
                p.Stats.Clear();
                p.BoostUntilTick = 0;
                RecomputeWeight(p);
            }
            ApplyContainerRows();
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
            // A playtest's perks first, past the pool's shift and rank (not on a reroll: the cards asked for are on the table).
            if (Settings.ForcedPerks != null && avoid == null)
                foreach (string id in Settings.ForcedPerks)
                {
                    if (offer.Count >= cards || !Content.TryPerk(id, out var forced)) continue;
                    if (Offerable(forced, shift, offer, only, avoid, pool: false)) offer.Add(forced);
                }
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
        /// one who asked, and none of the cards they had. The pool is the host's rank's (<see cref="PerkPoolRank"/>).
        /// </summary>
        private bool Offerable(PerkDef perk, int shift, List<PerkDef> offer, Player? only, IReadOnlyList<PerkDef>? avoid, bool pool = true)
        {
            if (pool && !InPool(perk, shift)) return false;
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
