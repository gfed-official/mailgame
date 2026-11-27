using System.Collections.Generic;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The map's Mail chip (doc 09 §2.6; design choice 121): which houses have mail waiting for them
    /// in the Intake and in the team's storage — the map's chests, the Chests and Depots built —
    /// told to everybody when it changes, at most once a second. What a carrier holds the client
    /// knows itself.
    /// </summary>
    public sealed partial class SimWorld
    {
        private const int MailWaitingTicks = SimConstants.TickRate;
        private readonly SortedDictionary<ushort, int> _waitingScratch = new SortedDictionary<ushort, int>();
        private readonly List<(ushort, ushort)> _waitingSent = new List<(ushort, ushort)>();
        private Dictionary<AddressId, ushort>? _destByAddress;

        /// <summary>Pieces of mail waiting for each house in the Intake and the storage, now.</summary>
        public IReadOnlyDictionary<ushort, int> MailWaiting()
        {
            _waitingScratch.Clear();
            if (_destByAddress == null || _destByAddressMap != Map)
            {
                _destByAddress = new Dictionary<AddressId, ushort>();
                foreach (var d in Map.Destinations)
                {
                    _destByAddress[d.Address] = d.Id;
                    // An apartment's or a bank's mail is for its units: each counts for the one interaction point.
                    for (int u = 1; u <= d.Units; u++) _destByAddress[new AddressId(d.Address.Key, (byte)u)] = d.Id;
                }
                _destByAddressMap = Map;
            }
            Count(Intake);
            foreach (var chest in _mapChests) Count(chest);
            foreach (var c in Constructs.All)
                if (c.ContainerId != 0 && !c.Def.IsPump && GetContainer(c.ContainerId) is { } box) Count(box);
            return _waitingScratch;

            void Count(GridContainer box)
            {
                foreach (var e in box.Entries)
                {
                    if (e.Stack.Kind != StackKind.Mail) continue;
                    foreach (var m in e.Stack.Mail)
                        if (_destByAddress!.TryGetValue(m.Address, out ushort dest))
                            _waitingScratch[dest] = _waitingScratch.TryGetValue(dest, out int n) ? n + 1 : 1;
                }
            }
        }

        private WorldMap? _destByAddressMap;

        /// <summary>In the tick: the counts, sent if they changed since last told.</summary>
        private void StepMailWaiting()
        {
            if (Tick % MailWaitingTicks != 0) return;
            if (Run.Phase != RunPhase.Prep && Run.Phase != RunPhase.Delivery && _waitingSent.Count == 0) return;
            var now = MailWaiting();
            bool same = now.Count == _waitingSent.Count;
            if (same)
            {
                int i = 0;
                foreach (var kv in now)
                {
                    if (_waitingSent[i].Item1 != kv.Key || _waitingSent[i].Item2 != Clamp16(kv.Value)) { same = false; break; }
                    i++;
                }
            }
            if (same) return;
            _waitingSent.Clear();
            foreach (var kv in now) _waitingSent.Add((kv.Key, Clamp16(kv.Value)));
            Events.Add(Waiting(EntityId.None));
        }

        /// <summary>To a joiner: the counts as last told.</summary>
        public void SendMailWaiting(EntityId player)
        {
            if (_waitingSent.Count > 0) Events.Add(Waiting(player));
        }

        private MailWaitingEvent Waiting(EntityId audience)
        {
            var ev = new MailWaitingEvent { Audience = audience };
            ev.Houses.AddRange(_waitingSent);
            return ev;
        }
    }
}
