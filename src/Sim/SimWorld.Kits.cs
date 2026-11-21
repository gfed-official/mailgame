using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Sim
{
    /// <summary>
    /// The starting kits (doc 03 §4.4, doc 01 §5.2, doc 06 §5–§6): each player picks one in the
    /// Lobby from those their Postal Rank has opened. When the run starts every player present
    /// gets its personal items in their pockets, and the host's kit gives the team its items —
    /// into the chest by the Post Office, the map's "Depot" — and its vehicles, parked by the
    /// counter. Who joins in Prep gets the personal items as they come in.
    /// </summary>
    public sealed partial class SimWorld
    {
        /// <summary>The Postal Rank a player's Hello carries: it opens the kits up to it.</summary>
        public void SetRank(Player p, int rank) => p.Rank = Math.Max(1, rank);

        private OpResult SetKitOp(Player p, in SimRequest r)
        {
            if (Run.Phase != RunPhase.Lobby) return OpResult.WrongPhase;
            if (r.Count >= Content.Kits.Count) return OpResult.BadRequest;
            var kit = Content.Kits[r.Count];
            if (kit.Rank > p.Rank) return OpResult.RankTooLow;
            p.Kit = kit;
            return OpResult.Ok;
        }

        private void GiveKits()
        {
            foreach (var p in _players) p.KitGiven = false;
            if (!Settings.StartingKits) return;
            foreach (var p in _players)
                if (!p.Away) GivePersonalKit(p);
            GiveTeamKit();
        }

        private void GivePersonalKit(Player p)
        {
            if (!Settings.StartingKits || p.KitGiven) return;
            p.KitGiven = true;
            var kit = p.Kit ?? Content.DefaultKit;
            // Into the inventory (doc 03 §4.4), the hotbar if the inventory has no room.
            foreach (var s in kit.Personal)
                if (!Give(p.Inventory, ItemStack.OfItem(s.Item, s.Count))) Give(p.Hotbar, ItemStack.OfItem(s.Item, s.Count));
            Events.Add(new KitGivenEvent { Player = p.Id, Kit = kit.Index, Team = false });
        }

        private void GiveTeamKit()
        {
            var host = GetPlayer(Run.Host);
            var kit = host?.Kit ?? Content.DefaultKit;
            // The map's chest by the Post Office stands for the Depot; without one, the host carries it.
            var depot = _mapChests.Count > 0 ? _mapChests[0] : host?.Inventory;
            if (depot != null)
                foreach (var s in kit.Team) Give(depot, ItemStack.OfItem(s.Item, s.Count));
            foreach (var v in kit.TeamVehicles) AddVehicle(v, host?.Id ?? Core.EntityId.None);
            Events.Add(new KitGivenEvent { Player = host?.Id ?? Core.EntityId.None, Kit = kit.Index, Team = true });
        }
    }
}
