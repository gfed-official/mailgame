using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Vehicles
{
    /// <summary>
    /// The houses a Vehicle Depot's route visits (doc 04 §3.2), in order, and how far that is
    /// along the routing graph. A stop is a house, a street or a district; a street's or a
    /// district's houses are visited nearest first from where the last one left off. Each house
    /// once, at the first stop that names it. The server's NPC driver and a client's console
    /// estimate plan by the same code.
    /// </summary>
    public static class RoutePlan
    {
        /// <summary>
        /// The houses of the stops in the order a driver visits them, from a start point; only
        /// those <paramref name="wanted"/> says yes to (null: every one).
        /// </summary>
        public static void Houses(WorldMap map, IReadOnlyList<MailFilter> stops, float fromX, float fromZ,
            Func<DestinationDef, bool>? wanted, List<DestinationDef> order)
        {
            order.Clear();
            float x = fromX, z = fromZ;
            var group = new List<DestinationDef>();
            foreach (var stop in stops)
            {
                group.Clear();
                switch (stop.Kind)
                {
                    case FilterKind.Address:
                        if (map.GetDestination(stop.Value) is { } one) group.Add(one);
                        break;
                    case FilterKind.Street:
                    case FilterKind.District:
                        foreach (var d in map.Destinations)
                            if (stop.Kind == FilterKind.Street ? d.Address.Street == stop.Value : d.Address.District == stop.Value) group.Add(d);
                        break;
                    default: continue;
                }
                group.RemoveAll(d => order.Contains(d) || (wanted != null && !wanted(d)));
                // Nearest first, as the crow flies from the last house.
                while (group.Count > 0)
                {
                    int best = 0;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < group.Count; i++)
                    {
                        float dx = group[i].MailboxX - x, dz = group[i].MailboxZ - z, d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    var next = group[best];
                    group.RemoveAt(best);
                    order.Add(next);
                    x = next.MailboxX;
                    z = next.MailboxZ;
                }
            }
        }

        /// <summary>Metres by the streets from a point round the houses and back to it.</summary>
        public static float RoundTrip(WorldMap map, IReadOnlyList<DestinationDef> houses, float fromX, float fromZ, List<(float X, float Z)> scratch)
        {
            if (houses.Count == 0) return 0f;
            float metres = 0f, x = fromX, z = fromZ;
            foreach (var d in houses)
            {
                metres += map.Routes.Route(x, z, d.MailboxX, d.MailboxZ, scratch);
                x = d.MailboxX;
                z = d.MailboxZ;
            }
            return metres + map.Routes.Route(x, z, fromX, fromZ, scratch);
        }
    }
}
