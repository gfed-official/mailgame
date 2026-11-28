using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Vehicles
{
    /// <summary>
    /// The Small Ports a captain's route calls at besides the docks (doc 11 §4 "Motorboat to second town"; design choice
    /// 175): a house of the route no boat can lie by is served from the team's Small Port nearest it, if that is another
    /// port than the captain's own — the boat lies in that port's bay and what stands behind its loading face takes the
    /// mail out. The server's captain and a client's console plan by the same code.
    /// </summary>
    public static class SeaRoute
    {
        /// <summary>The Small Port nearest a house's mailbox as the crow flies; null if that is <paramref name="home"/> or there is none.</summary>
        public static Construct? PortFor(ConstructField constructs, Construct home, DestinationDef house)
        {
            Construct? best = null;
            float bestD = float.MaxValue;
            foreach (var c in constructs.All)
            {
                if (!c.Def.IsPort) continue;
                float dx = c.CentreX - house.MailboxX, dz = c.CentreZ - house.MailboxZ, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best == home ? null : best;
        }

        /// <summary>
        /// The other ports of a route, in the order its houses come (each once), for the houses with no berth that
        /// <paramref name="wanted"/> says yes to (null: every one).
        /// </summary>
        public static void Ports(WorldMap map, ConstructField constructs, Construct home, IReadOnlyList<MailFilter> stops, float reach,
            System.Func<DestinationDef, bool>? wanted, List<Construct> ports)
        {
            ports.Clear();
            var houses = new List<DestinationDef>();
            SimWorld.Bay(home, out float bx, out float bz, out _);
            RoutePlan.Houses(map, stops, bx, bz, d => !WaterNav.Berth(map, constructs, d, reach, out _, out _) && (wanted == null || wanted(d)), houses);
            foreach (var h in houses)
                if (PortFor(constructs, home, h) is { } port && !ports.Contains(port)) ports.Add(port);
        }
    }
}
