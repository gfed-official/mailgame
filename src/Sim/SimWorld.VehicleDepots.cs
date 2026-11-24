using System;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The Vehicle Depot (doc 04 §3.2, doc 10 §2.2): 3 × 3 tiles a truck drives onto, one parking
    /// bay in the middle facing the way the depot does, its loading face on the back middle tile
    /// (where the truck's rear is when it stands in the bay), and a Route Console whose stops are
    /// kept as the construct's filters — an address, a street or a district each.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>Doc 04 §3.2 "one depot per 2 players rounded up".</summary>
        public int VehicleDepotLimit => Math.Max(1, (PresentCount + 1) / 2);

        private int VehicleDepotCount()
        {
            int n = 0;
            foreach (var c in Constructs.All) if (c.Def.IsVehicleDepot) n++;
            return n;
        }

        /// <summary>Where a truck stands in a depot's bay: the middle of its nine tiles, its nose the way the depot faces.</summary>
        public static void Bay(Construct depot, out float x, out float z, out float heading)
        {
            x = depot.CentreX;
            z = depot.CentreZ;
            var (dx, dz) = BeltNetwork.Dirs[depot.Facing];
            heading = Vehicle.YawOf(dx, dz, 0f);
        }

        /// <summary>
        /// Where its Route Console stands: on the left of the bay, at the outer edge of the middle
        /// tile of the depot's left column, clear of a truck's body.
        /// </summary>
        public static void Console(Construct depot, out float x, out float z)
        {
            var (tx, tz) = depot.TileAt(1);
            x = (tx + 0.5f) * Core.SimConstants.TileSize;
            z = (tz + 0.5f) * Core.SimConstants.TileSize;
            float ox = x - depot.CentreX, oz = z - depot.CentreZ, len = MathF.Sqrt(ox * ox + oz * oz);
            if (len > 0.01f) { x += ox / len * ConsoleOutset; z += oz / len * ConsoleOutset; }
        }

        /// <summary>How far past the middle of its tile the console stands, outwards.</summary>
        public const float ConsoleOutset = 0.45f;

        /// <summary>The vehicle standing parked in a depot's bay; null for none.</summary>
        public Vehicle? InBay(Construct depot)
        {
            Bay(depot, out float bx, out float bz, out _);
            foreach (var v in _vehicles)
                if (!v.Ridden && !v.NpcDriving && Near(v.X, v.Z, bx, bz, 0.25f)) return v;
            return null;
        }

        /// <summary>
        /// Parked with its middle on a depot's tiles, a truck is set into the bay (doc 04 §3.1
        /// "Depots and Ports provide marked parking zones"; design choice 109), if the bay is free.
        /// </summary>
        private void SnapIntoBay(Vehicle v)
        {
            if (!v.Def.LoadingFace) return;
            var depot = Constructs.At((int)MathF.Floor(v.X / Core.SimConstants.TileSize), (int)MathF.Floor(v.Z / Core.SimConstants.TileSize));
            if (depot == null || !depot.Def.IsVehicleDepot) return;
            var there = InBay(depot);
            if (there != null && there != v) return;
            Bay(depot, out v.X, out v.Z, out v.Heading);
        }

        /// <summary>
        /// For whoever has a depot's console up: how many pieces in the truck in its bay each stop
        /// would take (the panel's "N in the truck").
        /// </summary>
        private void CountRouteMatches(Construct depot, int[] matches)
        {
            var truck = InBay(depot);
            if (truck == null) return;
            foreach (var e in truck.Cargo.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                foreach (var mail in e.Stack.Mail)
                    for (int s = 0; s < depot.Def.RouteStops; s++)
                        if (StopTakes(depot.Filter(s, 0), mail)) { matches[s]++; break; }
            }
        }

        /// <summary>The console's status bits (<see cref="SorterStatusEvent.Buffered"/>): 1 a truck stands in the bay.</summary>
        public const byte DepotTruckInBay = 1;

        private byte DepotStatus(Construct depot) => InBay(depot) != null ? DepotTruckInBay : (byte)0;

        /// <summary>Whether a stop of a route is where a piece of mail goes: its house, its street or its district.</summary>
        public bool StopTakes(MailFilter stop, in MailItem mail) => stop.Kind == FilterKind.Address
            ? Map.GetDestination(stop.Value) is { } dest && dest.Address == mail.Address
            : stop.Kind != FilterKind.Kind && stop.Matches(mail);
    }
}
