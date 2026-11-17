using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;

namespace PerformativeMail.Client
{
    /// <summary>
    /// What the local player just picked up, as the cards of the concept video: the fallen enemy's
    /// name over one card for the coins and one per kind of item, each with what it is worth. Pickups
    /// that follow each other add to the cards on show; a while after the last one they go away.
    /// </summary>
    public sealed class LootCards
    {
        public const double ShownSeconds = 4.0;
        public const string CoinsName = "Coins";
        public const string DefaultTitle = "Loot";

        public sealed class Card
        {
            /// <summary>True for the coins; else <see cref="Item"/> is an item def index.</summary>
            public bool Coins;
            public ushort Item;
            public string Name = "";
            public int Count;
            /// <summary>Cents: the coins themselves, or what the items sell for.</summary>
            public int Value;
        }

        public IReadOnlyList<Card> Cards => _cards;
        public string Title { get; private set; } = DefaultTitle;
        public bool Visible => _cards.Count > 0;
        /// <summary>Bumped whenever the cards or the title change.</summary>
        public int Revision { get; private set; }

        /// <summary>The card worth the most, drawn in gold; -1 without cards.</summary>
        public int Best
        {
            get
            {
                int best = -1;
                for (int i = 0; i < _cards.Count; i++)
                    if (best < 0 || _cards[i].Value > _cards[best].Value) best = i;
                return best;
            }
        }

        private readonly List<Card> _cards = new List<Card>();
        /// <summary>Whose loot lies where: the taken event no longer says, and the bundle is gone by then.</summary>
        private readonly Dictionary<uint, string> _owners = new Dictionary<uint, string>();
        private double _age;

        public void Update(double dt)
        {
            if (_cards.Count == 0) return;
            _age += dt;
            if (_age >= ShownSeconds) Clear();
        }

        /// <summary>Takes the cards off the screen at once.</summary>
        public void Clear()
        {
            if (_cards.Count == 0) return;
            _cards.Clear();
            Title = DefaultTitle;
            Revision++;
        }

        public void OnEvent(ClientWorld world, SimEvent ev)
        {
            switch (ev)
            {
                case LootDroppedEvent e:
                    _owners[e.LootId] = HudText.EnemyName(world.Content, e.EnemyKind, e.Mega);
                    break;
                case LootGoneEvent e:
                    _owners.Remove(e.LootId);
                    break;
                case LootTakenEvent e:
                {
                    string? owner = _owners.TryGetValue(e.LootId, out string? name) ? name : null;
                    if (e.Gone) _owners.Remove(e.LootId);
                    if (e.Player != world.LocalId || (e.Coins == 0 && e.Items.Count == 0)) break;
                    if (owner != null) Title = owner;
                    if (e.Coins > 0) Add(true, 0, CoinsName, e.Coins, e.Coins);
                    foreach (var item in e.Items)
                    {
                        if (item.Item >= world.Content.Items.Count) continue;
                        var def = world.Content.Items[item.Item];
                        Add(false, item.Item, def.Name, item.Count, def.SellPrice * item.Count);
                    }
                    _age = 0;
                    Revision++;
                    break;
                }
            }
        }

        private void Add(bool coins, ushort item, string name, int count, int value)
        {
            foreach (var card in _cards)
            {
                if (card.Coins != coins || card.Item != item) continue;
                card.Count += count;
                card.Value += value;
                return;
            }
            _cards.Add(new Card { Coins = coins, Item = item, Name = name, Count = count, Value = value });
        }
    }
}
