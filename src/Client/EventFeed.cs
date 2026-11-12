using System.Collections.Generic;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client
{
    /// <summary>
    /// HUD event feed (doc 09 §2.4): the last 5 events, 6 s each. A stack goes into a mailbox item
    /// by item, so consecutive deliveries by one player to one mailbox are compressed into one line.
    /// </summary>
    public sealed class EventFeed
    {
        public const int MaxEntries = 5;
        public const double EntrySeconds = 6.0;

        public sealed class Entry
        {
            public string Text = "";
            public bool Bad;
            public double Age;

            internal EntityId Player;
            internal ushort DestId, Kind;
            internal bool Delivery;
            internal int Count, Amount;
        }

        public IReadOnlyList<Entry> Entries => _entries;
        /// <summary>Bumped whenever the visible lines change.</summary>
        public int Revision { get; private set; }

        private readonly List<Entry> _entries = new List<Entry>();

        public void Update(double dt)
        {
            foreach (var e in _entries) e.Age += dt;
            if (_entries.RemoveAll(e => e.Age >= EntrySeconds) > 0) Revision++;
        }

        public void Add(string text, bool bad = false) => Push(new Entry { Text = text, Bad = bad });

        public void OnEvent(ClientWorld world, SimEvent ev)
        {
            switch (ev)
            {
                case DeliveredEvent e: AddDelivery(world, e.Player, e.DestId, e.Kind, e.Paid, bad: false); break;
                case MisdeliveredEvent e: AddDelivery(world, e.Player, e.DestId, e.Kind, e.Penalty, bad: true); break;
                case PlayerJoinedEvent e when e.Player != world.LocalId: Add($"{e.Name} joined"); break;
                case MailSpawnedEvent e: Add($"{HudText.CountAndKind("item", e.Count)} of mail arrived at the Intake"); break;
            }
        }

        private void AddDelivery(ClientWorld world, EntityId player, ushort destId, ushort kind, int amount, bool bad)
        {
            Entry? entry = null;
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.Delivery && last.Player == player && last.DestId == destId && last.Kind == kind && last.Bad == bad)
                    entry = last;
            }
            if (entry == null)
            {
                entry = new Entry { Delivery = true, Player = player, DestId = destId, Kind = kind, Bad = bad };
                Push(entry);
            }
            entry.Count++;
            entry.Amount += amount;
            entry.Age = 0;

            string who = player == world.LocalId ? "You" : world.Players.TryGetValue(player, out var p) ? p.Name : "Someone";
            string what = HudText.CountAndKind(kind < world.Content.MailKinds.Count ? world.Content.MailKinds[kind].Name : "item", entry.Count);
            string where = world.Map.GetDestination(destId)?.Label ?? "?";
            entry.Text = bad
                ? $"{who} misdelivered {what} at {where}  -{HudText.Money(entry.Amount)}"
                : $"{who} delivered {what} to {where}  +{HudText.Money(entry.Amount)}";
            Revision++;
        }

        private void Push(Entry entry)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Revision++;
        }
    }
}
