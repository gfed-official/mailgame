using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>
    /// The route editor on the map (doc 09 §2.6 "when a Vehicle Depot console is open: click stops
    /// in order; district macro by clicking the district label; drag to reorder; estimated
    /// round-trip time shown from the routing graph"; design choice 122). Works on the stops of the
    /// console that is up (a depot's or a port's), as the panel shows them: a house clicked becomes
    /// the next empty stop, a district's label the district, a stop dragged onto another's place
    /// moves there, a stop right-clicked goes and the later ones move up. Each change is the
    /// console's own <c>SetFilter</c> requests, slot by slot, so the server judges it as it judges
    /// a key at the console.
    /// </summary>
    public sealed class RouteEditor
    {
        private readonly ClientRuntime _client;

        public RouteEditor(ClientRuntime client) => _client = client;

        /// <summary>The depot or port whose console is up; null for none (then the map is the map).</summary>
        public Construct? Console => _client.World?.FilterTarget is { Def.HasConsole: true } c ? c : null;

        public bool Active => Console != null;

        /// <summary>The stops as the console shows them, empty ones included.</summary>
        public List<MailFilter> Stops()
        {
            var list = new List<MailFilter>();
            var world = _client.World;
            var built = Console;
            if (world == null || built == null) return list;
            for (int i = 0; i < built.Def.RouteStops; i++) list.Add(world.ShownFilter(built, i, 0));
            return list;
        }

        /// <summary>The stop a house is (by itself, not by its street or district); −1 for none.</summary>
        public int StopOf(DestinationDef house) => Stops().IndexOf(new MailFilter(FilterKind.Address, house.Id));

        /// <summary>A house clicked: the next empty stop, if the console may call there. The stop it is, or −1.</summary>
        public int AddHouse(DestinationDef house) => Add(new MailFilter(FilterKind.Address, house.Id));

        /// <summary>A district's label clicked: the district as the next empty stop. The stop it is, or −1.</summary>
        public int AddDistrict(int district) => Add(new MailFilter(FilterKind.District, district));

        private int Add(MailFilter stop)
        {
            var world = _client.World;
            var built = Console;
            if (world == null || built == null || !world.FilterChoices(built).Contains(stop)) return -1;
            // A driver calls at houses: a Business Dock is a captain's (its Cargo comes off a boat or a truck in its zone).
            if (!built.Def.IsPort && stop.Kind == FilterKind.Address && world.Map.GetDestination(stop.Value) is { IsDock: true }) return -1;
            var stops = Stops();
            int had = stops.IndexOf(stop);
            if (had >= 0) return had;
            int free = stops.IndexOf(MailFilter.Empty);
            if (free < 0) return -1;
            // In order: after the last stop there is, not in a hole an earlier removal left.
            int last = stops.FindLastIndex(f => f != MailFilter.Empty);
            if (last + 1 < stops.Count) free = last + 1;
            return _client.SetFilter(free, 0, stop) != 0 ? free : -1;
        }

        /// <summary>A stop taken off; the later ones move up. False if there was none there.</summary>
        public bool Remove(int stop)
        {
            var stops = Stops();
            if (stop < 0 || stop >= stops.Count || stops[stop] == MailFilter.Empty) return false;
            stops.RemoveAt(stop);
            stops.Add(MailFilter.Empty);
            Send(stops);
            return true;
        }

        /// <summary>A stop dragged onto another's place: it goes there, the ones between move over. False if nothing moved.</summary>
        public bool Move(int from, int to)
        {
            var stops = Stops();
            if (from < 0 || from >= stops.Count || to < 0 || to >= stops.Count || from == to || stops[from] == MailFilter.Empty) return false;
            var moved = stops[from];
            stops.RemoveAt(from);
            // Not past the last stop there is: a route has no holes.
            int last = stops.FindLastIndex(f => f != MailFilter.Empty);
            stops.Insert(System.Math.Min(to, last + 1), moved);
            Send(stops);
            return true;
        }

        private void Send(List<MailFilter> stops)
        {
            for (int i = 0; i < stops.Count; i++) _client.SetFilter(i, 0, stops[i]);
        }

        /// <summary>The line the editor shows: the console's own, its round trip at the crew's pace.</summary>
        public string Line()
        {
            var world = _client.World;
            var built = Console;
            return world == null || built == null ? "" : HudText.RouteLine(world, built);
        }
    }
}
