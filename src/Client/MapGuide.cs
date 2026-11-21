using System;
using System.Collections.Generic;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>
    /// What the map (doc 09 §2.6) shows of the way: the house of the mail in the hand — the held
    /// slot's, else the first mail in the hotbar — and the route there from where the local
    /// player stands, along the streets of the map's routing graph, with its length. Recomputed
    /// while the map is open; the sim decides nothing here.
    /// </summary>
    public sealed class MapGuide
    {
        private readonly List<(float X, float Z)> _points = new List<(float X, float Z)>();

        /// <summary>The house the route runs to; null when no mail is carried in the hotbar.</summary>
        public DestinationDef? Target { get; private set; }
        /// <summary>The route's length in metres.</summary>
        public float Metres { get; private set; }
        /// <summary>The route, from the local player to the mailbox.</summary>
        public IReadOnlyList<(float X, float Z)> Points => _points;
        /// <summary>The addresses of all the mail in the hotbar and the inventory: their houses are ringed.</summary>
        public HashSet<AddressId> Carried { get; } = new HashSet<AddressId>();

        public void Update(ClientWorld world, int heldSlot)
        {
            Target = null;
            Metres = 0f;
            _points.Clear();
            Carried.Clear();
            if (!world.HasLocal) return;
            foreach (var container in new[] { world.Hotbar, world.Inventory })
                foreach (var e in container.Entries)
                    if (e.Stack.Kind == StackKind.Mail) Carried.Add(e.Stack.Address);

            var hotbar = world.Hotbar;
            var held = heldSlot >= 0 && heldSlot < hotbar.Cols ? hotbar.EntryAt(heldSlot, 0) : null;
            if (held == null || held.Stack.Kind != StackKind.Mail)
            {
                held = null;
                for (int slot = 0; slot < hotbar.Cols && held == null; slot++)
                {
                    var e = hotbar.EntryAt(slot, 0);
                    if (e != null && e.Stack.Kind == StackKind.Mail) held = e;
                }
            }
            if (held == null) return;
            Target = world.Map.FindDestination(held.Stack.Address);
            if (Target == null) return;
            Metres = world.Map.Routes.Route(world.Local.X, world.Local.Z, Target.MailboxX, Target.MailboxZ, _points);
        }

        /// <summary>Seconds the route takes at a speed in m/s.</summary>
        public float Seconds(float speed) => speed > 0f ? Metres / speed : 0f;

        /// <summary>The line under the map: where the route goes and how long it is on foot, running, or by bike.</summary>
        public string Line(ClientWorld world, MoveConstants move)
        {
            if (Target == null) return "Take a piece of mail into the hotbar to see the way to its house";
            string to = $"To {world.Map.FormatAddress(Target.Address)} · {Metres:F0} m by the streets";
            var ride = world.LocalRide;
            if (ride != null) return $"{to} · {HudText.Clock(Seconds(ride.Def.RoadSpeed))} by {ride.Def.Name.ToLowerInvariant()}";
            return $"{to} · {HudText.Clock(Seconds(move.WalkSpeed))} on foot, {HudText.Clock(Seconds(move.SprintSpeed))} running";
        }

        /// <summary>The title over the map: the town, and how much of it is open.</summary>
        public static string Title(ClientWorld world)
        {
            int open = 0;
            foreach (var d in world.Map.Districts) if (d.UnlockShift <= Math.Max(1, (int)world.Global.Shift)) open++;
            string districts = world.Map.Districts.Count <= 1 ? "" : $" · {open} of {world.Map.Districts.Count} districts open";
            return $"MAP · {world.Map.Name}{districts}   [M] close";
        }

        /// <summary>
        /// The raiders' ways in the map marks (doc 09 §2.6, doc 05 §3.1 "the spawn edge(s) marked
        /// on the map"): every one open by the shift, and whether the raid on its way or under way
        /// comes by it.
        /// </summary>
        public static List<(SpawnEdge Edge, bool Raid)> Edges(ClientWorld world)
        {
            var list = new List<(SpawnEdge, bool)>();
            int shift = Math.Max(1, (int)world.Global.Shift);
            bool raid = world.Raid == RaidState.Warning || world.Raid == RaidState.Active;
            foreach (var e in world.Map.SpawnEdges)
            {
                bool comes = raid && (e.Index == world.Global.RaidEdgeA || e.Index == world.Global.RaidEdgeB);
                if (e.OpenAt(shift) || comes) list.Add((e, comes));
            }
            return list;
        }
    }
}
