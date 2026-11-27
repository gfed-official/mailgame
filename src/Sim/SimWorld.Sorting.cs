using System;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The filters of Address Sorters and splitters (doc 04 §2.3, §2.4): set by a carrier at the
    /// construct, told to everybody, and, for whoever has them open, what the construct is doing
    /// and what of the Intake's mail each output would take (doc 09 §2.7).
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>How often the one looking at a sorter's filters hears what it is doing.</summary>
        public const int SorterStatusTicks = 15;
        /// <summary>A little more reach than building: the panel stays open as the carrier shifts their feet.</summary>
        private const float FilterReachSlack = 1.5f;

        private OpResult ConfigureOp(Player player, in SimRequest r)
        {
            if (player.Dead) return OpResult.Dead;
            var built = Constructs.At(r.X, r.Y);
            if (built == null || built.Def.FilterOutputs == 0) return OpResult.BadRequest;
            if (!player.WasWithin(built.CentreX, built.CentreZ, FilterReach(built), _rewindTicks)) return OpResult.OutOfRange;
            switch (r.Count)
            {
                case SimRequest.ConfigureOpen:
                    player.ViewingFilters = TileIndex(built) + 1;
                    SendSorterStatus(player, built);
                    return OpResult.Ok;
                case SimRequest.ConfigureClose:
                    player.ViewingFilters = 0;
                    return OpResult.Ok;
                case SimRequest.ConfigureHire:
                    return HireOp(player, built);
                case SimRequest.ConfigureSet:
                {
                    int output = (int)(r.EntryId / 16), slot = (int)(r.EntryId % 16);
                    if (output >= built.Def.FilterOutputs || slot >= built.Def.FilterSlots) return OpResult.BadRequest;
                    // Doc 10 §4.2 Extra Labels: the slots past the open ones are the perk's.
                    if (slot >= FilterSlotsOpen(built.Def)) return OpResult.NotAllowed;
                    var filter = MailFilter.Decode(r.DestId);
                    if (filter.Encode() != r.DestId || !FilterExists(built.Def, filter)) return OpResult.BadRequest;
                    var filters = new System.Collections.Generic.List<MailFilter>(built.Filters);
                    filters[Construct.FilterIndex(built.Def, output, slot)] = filter;
                    Constructs.SetFilters(built, filters);
                    Events.Add(Configured(built, player.Id));
                    if (player.ViewingFilters == TileIndex(built) + 1) SendSorterStatus(player, built);
                    return OpResult.Ok;
                }
                default: return OpResult.BadRequest;
            }
        }

        private float FilterReach(Construct built) => _serverBuildRange + built.Def.Radius;

        private int TileIndex(Construct built) => built.Tz * Map.Cols + built.Tx;

        /// <summary>A sorter and a pipe outlet filter by street, district or kind of this map and content; a splitter and an inserter by kind only (doc 04 §2.3).</summary>
        private bool FilterExists(BuildingDef def, MailFilter filter)
        {
            // A depot's stops: a house, a street or a district (doc 04 §3.2), never a kind.
            if (def.HasConsole && filter.Kind == FilterKind.Kind) return false;
            if (filter.Kind == FilterKind.Address) return def.HasConsole && Map.GetDestination(filter.Value) != null;
            switch (filter.Kind)
            {
                case FilterKind.None: return true;
                case FilterKind.Kind: return filter.Value < Content.MailKinds.Count;
                case FilterKind.Street: return (def.FiltersAddresses || def.HasConsole) && Map.GetStreet(filter.Value) != null;
                case FilterKind.District:
                    if (!def.FiltersAddresses && !def.HasConsole) return false;
                    foreach (var d in Map.Districts) if (d.Id == filter.Value) return true;
                    return false;
                default: return false;
            }
        }

        public static ConstructConfiguredEvent Configured(Construct built, EntityId by)
        {
            var ev = new ConstructConfiguredEvent { X = (byte)built.Tx, Y = (byte)built.Tz, Player = by };
            foreach (var f in built.Filters) ev.Filters.Add(f.Encode());
            return ev;
        }

        /// <summary>In the tick: who looks at a sorter's filters hears what it does; who walked off or went down stops.</summary>
        private void StepSorterViewers()
        {
            foreach (var p in _players)
            {
                if (p.ViewingFilters == 0) continue;
                int tile = p.ViewingFilters - 1;
                var built = Constructs.At(tile % Map.Cols, tile / Map.Cols);
                if (p.Dead || p.Away || built == null || built.Def.FilterOutputs == 0 || TileIndex(built) != tile
                    || !Near(p, built.CentreX, built.CentreZ, FilterReach(built) + FilterReachSlack))
                {
                    p.ViewingFilters = 0;
                    continue;
                }
                if (Tick % SorterStatusTicks == 0) SendSorterStatus(p, built);
            }
        }

        private static bool Near(Player p, float x, float z, float range)
        {
            float dx = p.Move.X - x, dz = p.Move.Z - z;
            return dx * dx + dz * dz <= range * range;
        }

        private void SendSorterStatus(Player p, Construct built)
        {
            var node = Belts.SorterOf(built);
            var ev = new SorterStatusEvent
            {
                Audience = p.Id, X = (byte)built.Tx, Y = (byte)built.Tz,
                Buffered = (byte)(node?.Buffer.Count ?? 0),
                Sorted = (ushort)Math.Min(ushort.MaxValue, node?.Sorted ?? 0),
                Overflowed = (ushort)Math.Min(ushort.MaxValue, node?.Overflowed ?? 0),
            };
            bool sorter = built.Def.Behaviour == BuildingBehaviour.Sorter;
            int outputs = built.Def.FilterOutputs + (sorter ? 1 : 0);
            var matches = new int[outputs];
            if (built.Def.HasConsole)
            {
                ev.Buffered = DepotStatus(built);
                CountRouteMatches(built, matches);
                foreach (int n in matches) ev.Matches.Add((ushort)Math.Min(ushort.MaxValue, n));
                Events.Add(ev);
                return;
            }
            foreach (var e in Intake.Entries)
            {
                if (e.Stack.Kind != StackKind.Mail) continue;
                foreach (var mail in e.Stack.Mail) CountMatch(built, sorter, mail, matches);
            }
            if (built.Def.PipeRole == PipeRole.Outlet) matches[0] = OutletMatches(built);
            foreach (int n in matches) ev.Matches.Add((ushort)Math.Min(ushort.MaxValue, n));
            Events.Add(ev);
        }

        /// <summary>
        /// Where a piece would go by the filters alone, belts or none: a sorter's first output that
        /// catches it, else its overflow; every output of a splitter that catches it.
        /// </summary>
        private static void CountMatch(Construct built, bool sorter, in Mail.MailItem mail, int[] matches)
        {
            int outputs = built.Def.FilterOutputs;
            for (int o = 0; o < outputs; o++)
            {
                bool caught = false;
                for (int k = 0; k < built.Def.FilterSlots; k++)
                    if (built.Filter(o, k).Matches(mail)) caught = true;
                if (!caught) continue;
                matches[o]++;
                if (sorter) return;
            }
            if (sorter) matches[outputs]++;
        }
    }
}
